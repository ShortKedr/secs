import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import crypto from "node:crypto";
import { chromium } from "playwright";

const arg = (name, fallback) => { const i = process.argv.indexOf(name); return i < 0 ? fallback : process.argv[i + 1]; };
const sites = arg("--sites", "").split(",").filter(Boolean).map(p => path.resolve(p));
const labels = arg("--labels", "before,after").split(",");
const output = path.resolve(arg("--output", "Benchmarks.Local/tmp/hardware-opt/render.json"));
if (sites.length !== labels.length || !sites.length || !output.startsWith(process.cwd() + path.sep)) throw new Error("Project-local output and matching sites/labels required");
await fs.access(output).then(() => { throw new Error("Choose new output"); }, () => {});
const servers = [], urls = [], runs = [];
let browser;
try {
  const mime = { ".js": "text/javascript", ".json": "application/json", ".html": "text/html", ".wasm": "application/wasm", ".png": "image/png", ".css": "text/css" };
  for (const site of sites) {
    const server = http.createServer(async (request, response) => {
      try {
        const file = path.resolve(site, decodeURIComponent(new URL(request.url, "http://local").pathname).slice(1) || "index.html");
        if (!file.startsWith(site + path.sep)) throw new Error("Unsafe path");
        response.writeHead(200, { "Content-Type": mime[path.extname(file)] ?? "text/plain" }).end(await fs.readFile(file));
      } catch { response.writeHead(404).end(); }
    });
    servers.push(server);
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    urls.push(`http://127.0.0.1:${server.address().port}/`);
  }
  browser = await chromium.launch({ headless: true });
  for (let run = 0; run < 3; run++) {
    const order = Array.from(sites.keys()); if (run % 2) order.reverse();
    for (const i of order) {
      const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
      const errors = []; page.on("pageerror", e => errors.push(String(e)));
      const source = await fs.readFile(path.join(sites[i], "app.js"), "utf8");
      const marker = "  window.spaceBattle = {";
      if (source.split(marker).length !== 2) throw new Error("Renderer injection marker changed");
      // Expose the original closure only in this diagnostic page; production files stay untouched.
      await page.route("**/app.js*", route => route.fulfill({ contentType: "text/javascript",
        body: source.replace(marker, "  window.__hardwareDraw = draw;\n" + marker) }));
      await page.goto(urls[i] + "?test=1", { waitUntil: "networkidle", timeout: 120000 });
      await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
      const result = await page.evaluate(async () => {
        const bridge = (await getDotnetRuntime(0).getAssemblyExports("SpaceBattle.Web.dll")).SpaceBattle.Web.BattleBridge;
        window.spaceBattle.pause(true); bridge.Reset("nominal", 7331); bridge.Advance(300);
        window.spaceBattle.refresh();
        const before = bridge.Checksum();
        for (let j = 0; j < 60; j++) window.__hardwareDraw();
        const samples = [];
        for (let j = 0; j < 120; j++) { const start = performance.now(); window.__hardwareDraw(); samples.push(performance.now() - start); }
        const checksum = bridge.Checksum(); if (checksum !== before) throw new Error("Drawing mutated battle");
        return { checksum, tick: window.spaceBattle.snapshot.tick, samples, runtimeMode: bridge.Mode() };
      });
      if (errors.length) throw new Error(errors.join("\n"));
      if (runs.length && result.checksum !== runs[0].checksum) throw new Error("Renderer state mismatch");
      await fs.mkdir(path.dirname(output), { recursive: true });
      if (run === 0) await page.screenshot({ path: `${output}.${labels[i]}.png` });
      const samples = result.samples.slice().sort((a,b) => a-b);
      runs.push({ label: labels[i], run: run + 1, ...result,
        originalAppSha256: crypto.createHash("sha256").update(source).digest("hex"),
        meanMs: samples.reduce((a,b) => a+b, 0)/samples.length, p95Ms: samples[Math.ceil((samples.length-1)*.95)] });
      await page.close();
    }
  }
  await fs.writeFile(output, JSON.stringify({ scope: "CPU Canvas draw submission only on the same paused tick300 snapshot. No Step/export/DOM update; 60 draw warmups, 120 samples, 3 independent fresh pages. Temporary closure exposure, untouched production file. Not GPU busy time or live FPS.", runs }, null, 2));
  console.log(runs.map(({ label, run, meanMs, checksum }) => ({ label, run, meanMs, checksum })));
} finally {
  await browser?.close(); for (const server of servers) await new Promise(resolve => server.close(resolve));
}
