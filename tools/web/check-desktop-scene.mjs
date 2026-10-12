import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { PNG } from "pngjs";

const option = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const root = process.cwd(), tmp = path.join(root, "Benchmarks.Local/tmp");
const nativePath = path.resolve(option("--native", "Benchmarks.Local/tmp/desktop-web-visuals/native-gallery.png"));
const output = path.resolve(option("--output", "Benchmarks.Local/tmp/desktop-web-visuals"));
assert.ok(nativePath.startsWith(tmp + path.sep) && output.startsWith(tmp + path.sep), "Only project-local ignored evidence");
const url = new URL(option("--url", "http://127.0.0.1:8054/"));
assert.ok(url.protocol === "http:" && ["127.0.0.1", "localhost"].includes(url.hostname), "Loopback browser only");
url.searchParams.set("test", "1"); url.searchParams.set("lang", "ru");
process.env.PLAYWRIGHT_BROWSERS_PATH ||= path.join(tmp, "playwright-browsers");
process.env.TEMP = process.env.TMP = process.env.TMPDIR = tmp;
const { chromium } = await import("playwright");
const fixture = JSON.parse(await fs.readFile(nativePath + ".json", "utf8"));
assert.equal(fixture.Schema, 1);
const native = PNG.sync.read(await fs.readFile(nativePath));
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: fixture.Width, height: fixture.Height }, deviceScaleFactor: 1 });
const errors = [], comparisons = [];
page.on("pageerror", e => errors.push(e.message));
page.on("console", e => { if (e.type() === "error") errors.push(e.text()); });
function point(row) {
  return { x: fixture.Width / 2 + (row[3] - fixture.CenterX) * fixture.Scale,
    y: fixture.OriginY - (row[4] - fixture.CenterY) * fixture.Scale };
}
function pixel(image, x, y) {
  const i = (Math.floor(y) * image.width + Math.floor(x)) * 4;
  return Array.from(image.data.subarray(i, i + 3));
}
function hullBounds(image, row, extent) {
  const p = point(row), bounds = { left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity }, count = { value: 0 };
  for (let y = Math.max(0, Math.floor(p.y - extent)); y <= Math.min(image.height - 1, Math.ceil(p.y + extent)); y++)
  for (let x = Math.max(0, Math.floor(p.x - extent)); x <= Math.min(image.width - 1, Math.ceil(p.x + extent)); x++) {
    const [r, g, b] = pixel(image, x, y);
    const colored = row[1] === 0 ? g > 20 && b > 20 && g > r * 1.8 && b > r * 1.8
      : r > 25 && r > g * 1.5 && r > b * 1.4;
    if (!colored) continue;
    count.value++; bounds.left = Math.min(bounds.left, x); bounds.right = Math.max(bounds.right, x);
    bounds.top = Math.min(bounds.top, y); bounds.bottom = Math.max(bounds.bottom, y);
  }
  assert.ok(count.value > 0, "visible team-painted hull " + row[2]);
  return { ...bounds, pixels: count.value };
}
try {
  await page.goto(url.href, { waitUntil: "networkidle", timeout: 120000 });
  await page.waitForFunction(() => window.spaceBattle?.ready || !document.querySelector("#retry").hidden, null, { timeout: 120000 });
  assert.ok(await page.evaluate(() => window.spaceBattle?.ready));
  await page.addStyleTag({ content: "#battle > :not(canvas) { visibility:hidden!important; }" });
  const before = await page.evaluate(() => JSON.parse(window.render_game_to_text()).checksum);
  await page.evaluate(f => {
    window.spaceBattle.fit(); window.spaceBattle.pause(true);
    const current = JSON.parse(window.render_game_to_text()), camera = window.spaceBattle.camera;
    assertReady();
    const snapshot = window.spaceBattle.snapshot;
    snapshot.arenaHalfSize = f.ArenaHalfSize;
    for (const [key, source] of [["ships", "Ships"], ["projectiles", "Projectiles"], ["debris", "Debris"], ["particles", "Particles"], ["turrets", "Turrets"]])
      snapshot[key] = f[source];
    camera.x = f.CenterX; camera.y = f.CenterY; camera.zoom = f.Scale / current.camera.scale;
    window.spaceBattle.select(f.Selected);
    function assertReady() {
      if (Math.abs(current.camera.originY - f.OriginY) > .01) throw new Error("Reference camera origin mismatch");
    }
  }, fixture);
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  const png = await page.locator("#scene").screenshot();
  await fs.mkdir(output, { recursive: true }); await fs.writeFile(path.join(output, "web-gallery.png"), png);
  const web = PNG.sync.read(png); assert.equal(web.width, native.width); assert.equal(web.height, native.height);
  assert.equal(await page.evaluate(() => JSON.parse(window.render_game_to_text()).checksum), before, "fixture only replaces render data, never simulation");
  assert.deepEqual(pixel(native, 0, 0), [8, 10, 12]); assert.deepEqual(pixel(web, 0, 0), [8, 10, 12]);
  for (const row of fixture.Debris) {
    const p = point(row), n = pixel(native, p.x, p.y), w = pixel(web, p.x, p.y);
    assert.deepEqual(n, [96, 96, 96]); assert.deepEqual(w, [96, 96, 96]);
    comparisons.push({ kind: "debris", serial: row[0], nativeRgb: n, webRgb: w, radius: Math.max(1, row[8] * fixture.Scale) });
  }
  for (const row of fixture.Ships) {
    const size = Math.max(row[2] >= 8 ? 13 : 5, row[8] * fixture.Scale * 2), extent = size * 1.5;
    const n = hullBounds(native, row, extent), w = hullBounds(web, row, extent);
    const difference = Math.max(...["left", "right", "top", "bottom"].map(key => Math.abs(n[key] - w[key])));
    assert.ok(difference <= 3, `hull ${row[2]} silhouette should match within rasterizer-edge tolerance, got ${difference}`);
    comparisons.push({ kind: "ship", class: row[2], native: n, web: w, maximumEdgeDifferencePx: difference });
  }
  assert.deepEqual(errors, []);
  const report = { pass: true, fixtureRevision: fixture.Revision,
    scope: "Same render-only fixture and projection through real native and web scene paths; not cross-platform simulation/checksum equality.",
    tolerance: "Hull colored silhouette edges <=3 pixels: GL texel-center sampling/alpha discard and Canvas filtering/antialiasing differ. Debris centers must match exact #606060.",
    comparisons, errors };
  await fs.writeFile(path.join(output, "scene-comparison.json"), JSON.stringify(report, null, 2));
  console.log(JSON.stringify({ pass: true, comparedHulls: fixture.Ships.length, comparedDebris: fixture.Debris.length, errors }));
} finally { await browser.close(); }
