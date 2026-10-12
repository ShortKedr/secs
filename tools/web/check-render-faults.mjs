import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { chromium, webkit } from "playwright";

const option = (key, fallback) => { const index = process.argv.indexOf(key); return index < 0 ? fallback : process.argv[index + 1]; };
const url = new URL(option("--url", "http://127.0.0.1:8062/"));
url.searchParams.set("test", "1"); url.searchParams.delete("diagnostics");
const output = path.resolve(option("--output", "Benchmarks.Local/tmp/ios-web-fix-20261012/render-faults.json"));
if (!["localhost", "127.0.0.1"].includes(url.hostname) || !output.startsWith(path.resolve("Benchmarks.Local/tmp") + path.sep))
  throw new Error("Local preview and ignored output only");
const engine = option("--browser", "chromium");
if (!["chromium", "webkit"].includes(engine)) throw new Error("Unknown browser");
const browser = await ({ chromium, webkit })[engine].launch({ headless: true });
const cases = [];
try {
  for (const kind of ["raf-draw", "null-context", "mixed-cache"]) {
    const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
    const errors = [], requested = []; page.on("pageerror", error => errors.push(String(error)));
    page.on("request", request => requested.push(request.url()));
    if (kind === "raf-draw") await page.addInitScript(() => {
      const raf = requestAnimationFrame;
      window.__insideRaf = false;
      window.requestAnimationFrame = callback => raf(time => { window.__insideRaf = true;
        try { callback(time); } finally { window.__insideRaf = false; } });
    });
    if (kind === "null-context") await page.addInitScript(() => {
      const original = HTMLCanvasElement.prototype.getContext;
      HTMLCanvasElement.prototype.getContext = function (kind, ...args) { return kind === "2d" ? null : original.call(this, kind, ...args); };
    });
    if (kind === "mixed-cache") await page.route("**/build-identity.json", async route => {
      const response = await route.fetch(), identity = await response.json(); identity.files["app.js"] = "00".repeat(32);
      await route.fulfill({ response, body: JSON.stringify(identity) });
    });
    await page.goto(url.href, { waitUntil: "networkidle", timeout: 60000 });
    if (kind === "raf-draw") {
      await page.waitForFunction(() => window.spaceBattle?.ready);
      await page.evaluate(() => {
        const original = CanvasRenderingContext2D.prototype.drawImage; window.__faults = 0;
        CanvasRenderingContext2D.prototype.drawImage = function (...args) {
          if (window.__insideRaf) { CanvasRenderingContext2D.prototype.drawImage = original; window.__faults++;
            throw new Error("Injected real RAF draw failure"); }
          return original.apply(this, args);
        };
      });
    }
    await page.waitForFunction(() => JSON.parse(window.render_game_to_text()).diagnostics.error, null, { timeout: 10000 });
    const before = await page.evaluate(() => JSON.parse(window.render_game_to_text()));
    await page.waitForTimeout(150);
    const after = await page.evaluate(() => JSON.parse(window.render_game_to_text()));
    assert.equal(after.diagnostics.frames, before.diagnostics.frames); assert.equal(after.tick, before.tick);
    assert.equal(after.ready, false); assert.equal(after.paused, true);
    assert.ok(await page.locator("#load-state").isVisible()); assert.ok(await page.locator("#retry").isVisible());
    if (kind === "raf-draw") assert.equal(await page.evaluate(() => window.__faults), 1);
    else assert.equal(requested.filter(address => address.includes("dotnet.native.wasm")).length, 0, "Early error loaded WASM");
    assert.deepEqual(errors, []);
    cases.push({ kind, error: after.diagnostics.error, frames: after.diagnostics.frames, tick: after.tick, passed: true });
    await page.close();
  }
  await fs.writeFile(output, JSON.stringify({ browser: engine, cases, realIos: false }, null, 2), { flag: "wx" });
  console.log(`PASS: ${engine} real RAF stop, null context and mixed-cache failure`);
} finally { await browser.close(); }
