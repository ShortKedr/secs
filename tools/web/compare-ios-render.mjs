import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import { chromium, webkit } from "playwright";

const argument = (key, fallback) => { const i = process.argv.indexOf(key); return i < 0 ? fallback : process.argv[i + 1]; };
const sites = argument("--sites", "").split(",").map(value => path.resolve(value));
const output = path.resolve(argument("--output", "")), engine = argument("--browser", "chromium");
assert.equal(sites.length, 2); assert.ok(["chromium", "webkit"].includes(engine));
assert.ok(output.startsWith(path.resolve("Benchmarks.Local/tmp") + path.sep));
const handle = await fs.open(output, "wx"), servers = [], urls = [], runs = [];
const mime = { ".js": "text/javascript", ".json": "application/json", ".html": "text/html", ".wasm": "application/wasm", ".png": "image/png", ".css": "text/css" };
let browser;
try {
  for (const site of sites) {
    assert.ok(site.startsWith(path.resolve("Benchmarks.Local/tmp") + path.sep));
    const server = http.createServer(async (request, response) => {
      try {
        const file = path.resolve(site, new URL(request.url, "http://local").pathname.slice(1) || "index.html");
        if (!file.startsWith(site + path.sep)) throw new Error("Unsafe path");
        response.writeHead(200, { "Content-Type": mime[path.extname(file)] ?? "application/octet-stream" }).end(await fs.readFile(file));
      } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve)); servers.push(server);
    urls.push(`http://127.0.0.1:${server.address().port}/`);
  }
  browser = await ({ chromium, webkit })[engine].launch({ headless: true });
  for (let repeat = 1; repeat <= 3; repeat++) for (const variant of repeat % 2 ? [0, 1] : [1, 0]) {
    const page = await browser.newPage({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 });
    const errors = []; page.on("pageerror", error => errors.push(String(error)));
    const source = await fs.readFile(path.join(sites[variant], "app.js"), "utf8");
    const marker = "  window.spaceBattle = {";
    assert.equal(source.split(marker).length, 2);
    const injected = source.replaceAll("requestAnimationFrame(frame);", "0;").replace(marker,
      `  window.__iosCompare = { draw, advance, exportFrame: () => accept(bridge.Snapshot()),
        resources: () => [...atlas, ...gunAtlas, effectsAtlas, warpAtlas].map(source => ({ kind: source.tagName,
          bytes: (source.naturalWidth || source.width) * (source.naturalHeight || source.height) * 4 })) };
${marker}`);
    await page.route("**/app.js*", route => route.fulfill({ contentType: "text/javascript", body: injected }));
    await page.goto(urls[variant] + "?test=1", { waitUntil: "networkidle", timeout: 60000 });
    await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 60000 });
    const result = await page.evaluate(async () => {
      const api = window.spaceBattle, probe = window.__iosCompare;
      api.pause(true); await window.advanceTime(1000 / 60); api.reset();
      const step = [], capture = [], exportInterop = [];
      for (let i = 0; i < 60; i++) {
        probe.advance(1); step.push(api.snapshot.stepMs); capture.push(api.snapshot.performance.exportCpuMs);
        const started = performance.now(); probe.exportFrame(); exportInterop.push(performance.now() - started);
      }
      const stepState = JSON.parse(window.render_game_to_text());
      api.reset(); probe.advance(24); api.refresh();
      const checksum = api.validate(), render = [];
      for (let i = 0; i < 12; i++) probe.draw();
      for (let i = 0; i < 60; i++) { const started = performance.now(); probe.draw(); render.push(performance.now() - started); }
      const frozenState = JSON.parse(window.render_game_to_text()); if (frozenState.checksum !== checksum) throw new Error("Renderer changed battle");
      return { step, capture, exportInterop, render, resources: probe.resources(), stepState, frozenState,
        memory: { wasmLinearBytes: getDotnetRuntime(0).localHeapViewF64().buffer.byteLength,
          managedBytes: api.snapshot.performance.managedBytes, jsHeapBytes: performance.memory?.usedJSHeapSize ?? null } };
    });
    assert.deepEqual(errors, []);
    if (repeat === 1) await page.screenshot({ path: output + `.${variant ? "after" : "before"}.png` });
    runs.push({ variant: variant ? "after" : "before", repeat, ...result }); await page.close();
  }
  const stable = value => ({ tick: value.tick, checksum: value.checksum, counts: value.counts, counters: value.counters });
  for (const run of runs) { assert.deepEqual(stable(run.stepState), stable(runs[0].stepState));
    assert.deepEqual(stable(run.frozenState), stable(runs[0].frozenState)); }
  const mean = samples => samples.reduce((sum, value) => sum + value, 0) / samples.length;
  const summary = ["before", "after"].map(variant => ({ variant,
    stages: Object.fromEntries(["step", "capture", "exportInterop", "render"].map(key => {
      const values = runs.filter(run => run.variant === variant).map(run => mean(run[key])).sort((a, b) => a - b);
      return [key, { medianProcessMeanMs: values[1], rangeMs: [values[0], values[2]] }];
    })) }));
  await handle.writeFile(JSON.stringify({ browser: engine, summary, runs,
    scope: "Three fresh pages/variant, alternating order; same Mono source/seed/fleet/physics. One warm tick on discarded battle. 60 Step samples then separate tick24 frozen 12 warm/60 draw calls. Step is C# LastTickMs; capture is C# Snapshot; exportInterop is independent Snapshot+accept wall time and includes capture. No subtraction or GPU/system memory/FPS claim. No real iOS." }, null, 2));
  console.log(JSON.stringify(summary));
} finally {
  await handle.close(); await browser?.close(); for (const server of servers) await new Promise(resolve => server.close(resolve));
}
