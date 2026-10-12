import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { PNG } from "pngjs";

const option = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const url = new URL(option("--url", "http://127.0.0.1:8054/"));
if (url.protocol !== "http:" || !["127.0.0.1", "localhost"].includes(url.hostname)) throw new Error("Loopback preview only");
url.searchParams.set("test", "1"); url.searchParams.set("lang", "ru");
const root = process.cwd(), output = path.resolve(option("--output", "Benchmarks.Local/tmp/colliders-web"));
if (!output.startsWith(root + path.sep)) throw new Error("Output must stay in the project");
await fs.mkdir(output, { recursive: true });
process.env.TEMP = process.env.TMP = process.env.TMPDIR = path.join(root, "Benchmarks.Local/tmp");
process.env.PLAYWRIGHT_BROWSERS_PATH ||= path.join(root, "Benchmarks.Local/tmp/playwright-browsers");
const { chromium } = await import("playwright");
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const errors = [], captures = [];
page.on("pageerror", e => errors.push(String(e)));
page.on("console", m => { if (m.type() === "error") errors.push(m.text()); });
const state = () => page.evaluate(() => JSON.parse(window.render_game_to_text()));
const rows = () => page.evaluate(() => window.spaceBattle.colliders());
async function capture(label) {
  await page.screenshot({ path: path.join(output, label + ".png") });
  const s = await state(); await fs.writeFile(path.join(output, label + ".json"), JSON.stringify(s, null, 2));
  captures.push(label);
}
async function checkGeometry() {
  const data = await page.evaluate(() => ({ debug: window.spaceBattle.colliders(), snapshot: {
    ships: window.spaceBattle.snapshot.ships.map(r => Array.from(r)),
    projectiles: window.spaceBattle.snapshot.projectiles.map(r => Array.from(r)),
    debris: window.spaceBattle.snapshot.debris.map(r => Array.from(r)),
  } }));
  assert.equal(data.debug.enabled, true);
  for (const [kind, group] of [["ship", "ships"], ["projectile", "projectiles"], ["debris", "debris"]]) {
    const colliders = data.debug.rows.filter(r => r.kind === kind), rendered = data.snapshot[group];
    assert.equal(colliders.length, rendered.length, "active physical shapes only: " + group);
    const map = new Map(rendered.map(r => [r[0], r]));
    for (const c of colliders) {
      const r = map.get(c.serial); assert.ok(r);
      assert.equal(c.x, r[3]); assert.equal(c.y, r[4]); assert.equal(c.angle, r[7]); assert.equal(c.radius, r[8]);
      if (kind !== "projectile") assert.equal(c.shape, "disk");
      else if (c.shape === "rectangle") { assert.equal(c.halfLength, c.radius * 2); assert.equal(c.halfWidth, c.radius); }
    }
  }
}
try {
  await page.goto(url.href, { waitUntil: "networkidle", timeout: 120000 });
  await page.waitForFunction(() => window.spaceBattle?.ready || !document.querySelector("#retry").hidden, null, { timeout: 120000 });
  assert.ok(await page.evaluate(() => window.spaceBattle?.ready), await page.locator("#load-detail").textContent());
  const initial = await state(); assert.equal(initial.paused, true); assert.equal(initial.colliderDebug.enabled, false);
  assert.equal(initial.runtimeMode, "aot-binary");
  await page.click("#colliders");
  assert.equal((await state()).checksum, initial.checksum); assert.equal((await rows()).ships, initial.renderCounts.ships);
  assert.equal(await page.locator("#colliders").getAttribute("aria-pressed"), "true");
  await checkGeometry();
  await page.keyboard.press("F2"); assert.equal((await rows()).enabled, false);
  await page.evaluate(() => window.advanceTime(5000));
  const baseline = await state(); assert.equal(baseline.tick, 300);
  await page.evaluate(() => { window.spaceBattle.camera.zoom = 3; window.spaceBattle.refresh(); });
  const before = PNG.sync.read(await page.locator("#scene").screenshot());
  await capture("desktop-off");
  await page.keyboard.press("F2"); await checkGeometry();
  const on = await state(); assert.equal(on.tick, baseline.tick); assert.equal(on.checksum, baseline.checksum);
  assert.deepEqual(on.binary, baseline.binary);
  assert.ok(on.colliderDebug.rectangles > 0 && on.colliderDebug.disks > on.colliderDebug.ships);
  const after = PNG.sync.read(await page.locator("#scene").screenshot());
  let changedPixels = 0, greenPixels = 0;
  for (let i = 0; i < after.data.length; i += 4) {
    if (after.data[i] !== before.data[i] || after.data[i + 1] !== before.data[i + 1] || after.data[i + 2] !== before.data[i + 2]) changedPixels++;
    if (after.data[i + 1] > 200 && after.data[i + 1] > after.data[i] * 1.2 && after.data[i + 1] > after.data[i + 2] * 1.1) greenPixels++;
  }
  assert.ok(changedPixels > 100 && greenPixels > 20, "colliders are visible in actual Canvas pixels");
  await capture("desktop-on");
  await page.evaluate(() => window.spaceBattle.follow(window.spaceBattle.snapshot.ships.find(r => r[2] >= 8)[0]));
  await page.waitForTimeout(300); await checkGeometry();
  assert.equal((await state()).following, true); await capture("desktop-follow");
  for (const [width, height] of [[390, 844], [360, 740], [1920, 1080]]) {
    await page.setViewportSize({ width, height }); await page.waitForTimeout(150);
    const box = await page.locator(".camera-tools").boundingBox();
    assert.ok(box.x >= 0 && box.x + box.width <= width && box.y >= 0 && box.y + box.height <= height);
    await checkGeometry(); await capture(`viewport-${width}`);
  }
  await page.evaluate(() => window.spaceBattle.reset());
  assert.equal((await state()).checksum, initial.checksum); assert.equal((await rows()).enabled, true);
  await checkGeometry();
  await page.evaluate(() => window.advanceTime(5000));
  assert.equal((await state()).checksum, baseline.checksum, "enabled/disabled independent replay parity");
  await page.keyboard.press("F2"); assert.equal((await rows()).rows.length, 0);
  const pointer = await page.evaluate(async () => {
    const runtime = getDotnetRuntime(0), bridge = (await runtime.getAssemblyExports("SpaceBattle.Web.dll")).SpaceBattle.Web.BattleBridge;
    return bridge.ColliderSnapshot();
  });
  assert.equal(pointer, 0, "disabled renderer cannot request debug data");
  await page.click("#language"); assert.equal(await page.locator("#colliders").getAttribute("title"), "Collider outlines (F2)");
  await page.click("#colliders"); await page.keyboard.press("F2");
  assert.equal((await state()).checksum, baseline.checksum); assert.deepEqual(errors, []);
  const result = { pass: true, captures, changedPixels, greenPixels, checksum: baseline.checksum, errors };
  await fs.writeFile(path.join(output, "result.json"), JSON.stringify(result, null, 2)); console.log(JSON.stringify(result));
} finally { await browser.close(); }
