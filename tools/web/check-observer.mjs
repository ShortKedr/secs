import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright";

const option = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const watchedSerial = Number(option("--ship", "22"));
if (!Number.isInteger(watchedSerial) || watchedSerial < 0) throw new Error("Invalid watched ship serial");
const url = new URL(option("--url", "http://127.0.0.1:8053/"));
if (url.protocol !== "http:" || !["127.0.0.1", "localhost"].includes(url.hostname)) throw new Error("Loopback preview only");
url.searchParams.set("test", "1"); url.searchParams.set("lang", "ru");
const root = process.cwd(), output = path.resolve(option("--output", "Benchmarks.Local/tmp/observer-web"));
if (!output.startsWith(root + path.sep)) throw new Error("Output must stay in the project");
await fs.mkdir(output, { recursive: true });
process.env.TEMP = process.env.TMP = process.env.TMPDIR = path.join(root, "Benchmarks.Local/tmp");
process.env.PLAYWRIGHT_BROWSERS_PATH ||= path.join(root, "Benchmarks.Local/tmp/playwright-browsers");
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const errors = [], transitions = [], captures = [];
page.on("pageerror", e => errors.push(String(e)));
page.on("console", m => { if (m.type() === "error") errors.push(m.text()); });
const state = () => page.evaluate(() => JSON.parse(window.render_game_to_text()));
function fits(s) {
  assert.equal(s.following, true);
  const viewport = s.observation.viewport, cam = s.camera;
  assert.ok(viewport && s.observation.main && Number.isFinite(cam.x + cam.y + cam.zoom));
  const center = { x: (viewport.left + viewport.right) / 2, y: (viewport.top + viewport.bottom) / 2 };
  const projected = ship => ({ x: cam.width / 2 + (ship.x - cam.x) * cam.scale,
    y: cam.originY - (ship.y - cam.y) * cam.scale });
  for (const ship of [s.observation.main, s.observation.target].filter(Boolean)) {
    const p = projected(ship), pad = ship.radius * 2.2 * cam.scale + 14;
    assert.ok(p.x - pad >= viewport.left - 0.01 && p.x + pad <= viewport.right + 0.01 &&
      p.y - pad >= viewport.top - 0.01 && p.y + pad <= viewport.bottom + 0.01, `safe frame at ${s.tick}`);
  }
  if (s.observation.target) {
    const a = projected(s.observation.main), b = projected(s.observation.target);
    assert.ok(Math.hypot(a.x - center.x, a.y - center.y) <= Math.hypot(b.x - center.x, b.y - center.y) + 0.01, "primary centering priority");
  }
}
async function capture(label) {
  const s = await state(); fits(s);
  const image = `${label}-${String(s.tick).padStart(4, "0")}.png`;
  await page.screenshot({ path: path.join(output, image) });
  await fs.writeFile(path.join(output, image + ".json"), JSON.stringify(s, null, 2));
  captures.push({ image, tick: s.tick, main: s.selected, target: s.observation.target?.serial ?? -1 });
}
try {
  await page.goto(url.href, { waitUntil: "networkidle", timeout: 120000 });
  await page.waitForFunction(() => window.spaceBattle?.ready || !document.querySelector("#retry").hidden, null, { timeout: 120000 });
  assert.ok(await page.evaluate(() => window.spaceBattle?.ready), await page.locator("#load-detail").textContent());
  assert.equal((await state()).runtimeMode, "aot-binary");
  const initial = (await state()).checksum;
  await page.evaluate(async serial => {
    const runtime = getDotnetRuntime(0);
    window.__observerTest = { runtime, bridge: (await runtime.getAssemblyExports("SpaceBattle.Web.dll")).SpaceBattle.Web.BattleBridge };
    window.spaceBattle.select(serial);
  }, watchedSerial);
  await page.click("#follow");
  assert.equal((await state()).checksum, initial, "follow toggle cannot alter simulation");
  assert.equal(await page.locator("#follow").getAttribute("aria-pressed"), "true");
  await capture("follow");
  for (let tick = 1; tick <= 600; tick++) {
    const sample = await page.evaluate(() => {
      const { bridge, runtime } = window.__observerTest;
      const previous = JSON.parse(window.render_game_to_text());
      const pointer = bridge.Advance(1);
      const death = { serial: bridge.ObservedDeath(0), killer: bridge.ObservedDeath(1), x: bridge.ObservedDeath(2), y: bridge.ObservedDeath(3) };
      let expected = previous.selected;
      if (!bridge.ShipObservation(previous.selected, 0)) {
        expected = -1;
        if (death.serial === previous.selected && bridge.ShipObservation(death.killer, 0)) expected = death.killer;
        else {
          const position = death.serial === previous.selected ? death : previous.observation.lastPosition;
          const heap = runtime.localHeapViewF64(), start = pointer / 8;
          let best = Infinity;
          for (let i = 0; i < heap[start + 10]; i++) {
            const row = start + 80 + i * 16, serial = heap[row];
            const distance = (heap[row + 3] - position.x) ** 2 + (heap[row + 4] - position.y) ** 2;
            if (distance < best || distance === best && serial < expected) { expected = serial; best = distance; }
          }
        }
      }
      window.spaceBattle.refresh();
      return { before: previous.selected, expected, death, state: JSON.parse(window.render_game_to_text()) };
    });
    const s = sample.state; fits(s);
    assert.equal(s.selected, sample.expected, "live killer else nearest visible at exact death tick");
    if (s.selected !== sample.before) {
      transitions.push({ tick, from: sample.before, to: s.selected, reason: s.observation.reason, death: sample.death });
      await capture("handoff");
    }
    if (tick % 30 === 0) await capture("follow");
    if (tick % 5 === 0) await page.waitForTimeout(16);
  }
  const final = await state();
  assert.ok(transitions.length > 0, "actual combat death handoff");
  if (process.argv.includes("--require-killer")) assert.ok(transitions.some(t => t.reason === "killer"), "actual live-killer handoff");
  assert.ok(captures.some(c => c.target >= 0), "live targets framed");
  const markerPixels = await page.evaluate(() => {
    const s = JSON.parse(window.render_game_to_text()), canvas = document.querySelector("#scene");
    const dpr = canvas.width / s.camera.width, ctx = canvas.getContext("2d");
    function count(ship, amber) {
      if (!ship) return 0;
      const x = s.camera.width / 2 + (ship.x - s.camera.x) * s.camera.scale;
      const y = s.camera.originY - (ship.y - s.camera.y) * s.camera.scale;
      const size = Math.max(ship.kind >= 8 ? 13 : 5, ship.radius * 2 * s.camera.scale), radius = size * 0.82 + 8;
      let matches = 0;
      for (const dx of [-1, 1]) for (const dy of [-1, 1]) {
        const data = ctx.getImageData(Math.round((x + dx * radius - 6) * dpr), Math.round((y + dy * radius - 6) * dpr), Math.ceil(12 * dpr), Math.ceil(12 * dpr)).data;
        for (let i = 0; i < data.length; i += 4)
          if (amber ? data[i] > 225 && data[i + 1] > 155 && data[i + 1] < 225 && data[i + 2] < 145 :
            data[i] > 220 && data[i + 1] > 230 && data[i + 2] > 230) matches++;
      }
      return matches;
    }
    return { main: count(s.observation.main, false), target: count(s.observation.target, true) };
  });
  assert.ok(markerPixels.main > 0 && markerPixels.target > 0, "actual white main and amber target pixels");
  await page.evaluate(() => window.spaceBattle.reset());
  await page.evaluate(() => window.advanceTime(10000));
  assert.equal((await state()).checksum, final.checksum, "600-tick watched/plain replay parity");
  await page.evaluate(() => window.spaceBattle.follow(1));
  const checksum = (await state()).checksum;
  for (const [width, height, label] of [[390, 844, "portrait"], [844, 390, "landscape"], [2560, 900, "wide"]]) {
    await page.setViewportSize({ width, height }); await page.waitForTimeout(180); await capture(label);
    assert.equal((await state()).checksum, checksum, "viewport changes are presentation only");
  }
  await page.click("#zoom-in"); assert.equal((await state()).following, false, "manual zoom cancels");
  await page.evaluate(() => window.spaceBattle.follow(1));
  await page.mouse.move(900, 350); await page.mouse.down(); await page.mouse.move(960, 390, { steps: 3 }); await page.mouse.up();
  assert.equal((await state()).following, false, "manual pan cancels");
  await page.evaluate(() => window.spaceBattle.follow(1)); await page.keyboard.press("KeyC");
  assert.equal((await state()).following, false, "C toggles off");
  await page.keyboard.press("KeyC"); assert.equal((await state()).following, true, "C toggles on");
  await page.evaluate(() => window.spaceBattle.fit()); assert.equal((await state()).following, false, "fit cancels");
  await page.evaluate(() => window.spaceBattle.follow(1)); await page.evaluate(() => window.spaceBattle.reset());
  assert.equal((await state()).following, false); assert.equal((await state()).selected, null);
  assert.deepEqual(errors, []);
  await fs.writeFile(path.join(output, "results.json"), JSON.stringify({ transitions, captures, markerPixels, finalChecksum: final.checksum, errors }, null, 2));
  console.log(`PASS: 600-tick real handoffs (${transitions.length}), exact killer/fallback, marked safe framing, responsive/cancel/reset, watched/plain checksum ${final.checksum}`);
} catch (error) {
  await fs.writeFile(path.join(output, "failure.json"), JSON.stringify({ error: String(error), stack: error.stack, errors, transitions, captures }, null, 2));
  throw error;
} finally { await browser.close(); }
