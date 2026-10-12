import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import { chromium } from "playwright";
const sites = process.argv[2].split(",").map(value => path.resolve(value)), output = path.resolve(process.argv[3]);
if (!output.startsWith(path.resolve("Benchmarks.Local/tmp") + path.sep)) throw new Error("Ignored output required");
const browser = await chromium.launch({ headless: true }), results = [];
const mime = { ".html": "text/html", ".js": "text/javascript", ".json": "application/json", ".wasm": "application/wasm", ".png": "image/png", ".css": "text/css" };
try {
  for (const site of sites) {
    const server = http.createServer(async (request, response) => {
      try { const file = path.resolve(site, new URL(request.url, "http://local").pathname.slice(1) || "index.html");
        if (!file.startsWith(site + path.sep)) throw new Error("Unsafe path");
        response.writeHead(200, { "Content-Type": mime[path.extname(file)] ?? "text/plain" }).end(await fs.readFile(file));
      } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    const page = await browser.newPage();
    try {
      const source = await fs.readFile(path.join(site, "app.js"), "utf8");
      await page.route("**/app.js*", route => route.fulfill({ contentType: "text/javascript", body: source.replaceAll("requestAnimationFrame(frame);", "0;") }));
      await page.addInitScript(() => {
        window.__growthPhase = "startup"; window.__growth = [];
        const original = WebAssembly.Memory.prototype.grow;
        WebAssembly.Memory.prototype.grow = function (delta) {
          const before = this.buffer.byteLength, result = original.call(this, delta);
          window.__growth.push({ phase: window.__growthPhase, before, delta, after: this.buffer.byteLength }); return result;
        };
      });
      await page.goto(`http://127.0.0.1:${server.address().port}/?test=1`, { waitUntil: "networkidle", timeout: 60000 });
      await page.waitForFunction(() => window.spaceBattle?.ready);
      const data = await page.evaluate(async () => {
        const points = []; const read = phase => { window.__growthPhase = phase;
          const state = JSON.parse(window.render_game_to_text()); points.push({ phase, linearBytes: getDotnetRuntime(0).localHeapViewF64().buffer.byteLength,
            tick: state.tick, checksum: state.checksum, managedBytes: state.statistics.managedBytes }); };
        read("ready"); await window.advanceTime(1000 / 60); read("warm-1");
        window.spaceBattle.reset(); read("reset-1");
        for (let i = 0; i < 60; i++) await window.advanceTime(1000 / 60); read("tick-60");
        window.spaceBattle.reset(); read("reset-2"); await window.advanceTime(24 * 1000 / 60); read("tick-24");
        window.spaceBattle.validate(); read("post-validate"); window.spaceBattle.refresh(); read("post-export");
        return { points, growth: window.__growth };
      });
      results.push({ site, ...data });
    } finally { await page.close(); await new Promise(resolve => server.close(resolve)); }
  }
  await fs.writeFile(output, JSON.stringify({ scope: "Instrumented JS memory.grow diagnostic, not throughput or physical footprint. One page/site, same bounded reset sequence.", results }, null, 2), { flag: "wx" });
  console.log(JSON.stringify(results.map(result => ({ site: result.site, points: result.points }))));
} finally { await browser.close(); }
