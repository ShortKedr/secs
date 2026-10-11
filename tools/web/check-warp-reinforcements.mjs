import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { PNG } from "pngjs";
import { measure, GUARDRAILS } from "./effect-contrast-metrics.mjs";

const options = new Map();
for (let i = 2; i < process.argv.length; i += 2) {
  const key = process.argv[i], value = process.argv[i + 1];
  assert.ok(["--url", "--output"].includes(key) && value && !options.has(key),
    "Usage: node tools/web/check-warp-reinforcements.mjs --url http://localhost:8047/ --output Benchmarks.Local/tmp/warp-RUN");
  options.set(key, value);
}
assert.ok(options.has("--url"), "--url is required; run only after main publishes the warp artwork");
const url = new URL(options.get("--url"));
const isLocal = u => u.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(u.hostname);
assert.ok(isLocal(url) && !url.username && !url.password, "Only credential-free localhost HTTP previews allowed");
url.search = "?test=1"; url.hash = "";
if (!url.pathname.endsWith("/")) url.pathname += "/";
const root = await fs.realpath(fileURLToPath(new URL("../../", import.meta.url)));
const tmp = path.join(root, "Benchmarks.Local/tmp");
const output = path.resolve(root, options.get("--output") || "Benchmarks.Local/tmp/warp-reinforcements");
const within = (parent, child) => {
  const relative = path.relative(parent, child);
  return relative && relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative);
};
assert.ok(within(tmp, output), "Output must be below this project's Benchmarks.Local/tmp");
// Validate every ancestor before creating anything, including junctions above tmp.
let cursor = root;
for (const part of path.relative(root, output).split(path.sep)) {
  cursor = path.join(cursor, part);
  try {
    const stat = await fs.lstat(cursor);
    assert.ok(stat.isDirectory() && !stat.isSymbolicLink(), `No symlink/junction output: ${cursor}`);
    assert.ok(within(root, await fs.realpath(cursor)), "Output realpath must stay in project");
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }
}
await fs.mkdir(output, { recursive: true });
assert.ok(within(await fs.realpath(tmp), await fs.realpath(output)), "Output realpath must stay below tmp");
assert.equal((await fs.readdir(output)).length, 0, "Use a fresh empty output directory; existing evidence is preserved");
const report = { url: url.href, output, guardrails: GUARDRAILS, pass: false, status: "running",
  protocol: { firstWaveBound: 3600, smokeBound: 6000, wallSeconds: 480, flashTicks: 30,
    brightness: "Fixed composited linear-sRGB budgets; no family-gain requirement", cap: 99 },
  errors: [], failures: [], checkpoints: [], frames: [], capSamples: [], smoke: {} };
let browser, context, page, tracing = false;
const deadline = Date.now() + report.protocol.wallSeconds * 1000;
const check = (condition, message) => { if (!condition) report.failures.push(message); };
const save = (name, data) => fs.writeFile(path.join(output, name), data);
const state = () => page.evaluate(() => {
  const game = window.spaceBattle, s = game.snapshot, waves = game.reinforcements();
  const alive = typeof game.aliveTeamCounts === "function" ? game.aliveTeamCounts() : waves.map(w => w.aliveShips);
  return { tick: s.tick, seed: s.seed, profile: s.profile, counts: s.counts, counters: s.counters,
    pending: s.pendingReinforcements, binary: s.binary, waves, alive,
    ships: s.ships.map(r => Array.from(r)), particles: s.particles.filter(r => r[2] >= 4).map(r => Array.from(r)),
    checksum: JSON.parse(window.render_game_to_text()).checksum };
});
function cap(s) {
  const valid = Array.isArray(s.alive) && s.alive.length === 2 && s.alive.every(n => Number.isInteger(n) && n >= 0);
  if (!valid) { report.capDiagnosticMissing = true; return; }
  assert.equal(s.alive[0] + s.alive[1], s.counts.ships, "Alive counts include docked ECS ships");
  assert.ok(s.alive.every(n => n <= 99), `Alive team cap at tick ${s.tick}: ${s.alive}`);
  report.capSamples.push({ tick: s.tick, alive: s.alive });
}
async function advance(ticks) {
  assert.ok(Date.now() < deadline, "Bounded validation wall-clock budget exhausted");
  await page.evaluate(ticks => window.advanceTime(ticks * 1000 / 60), ticks);
  const s = await state(); cap(s); return s;
}
async function firstWave() {
  let s = await state(); cap(s);
  while (s.tick <= 3600) {
    const candidates = s.waves.map((w, team) => ({ ...w, team }))
      .filter(w => w.Active === 1 && w.wave > 0 && w.pending > 0 && w.ArrivalTick >= s.tick + 30)
      .sort((a, b) => a.ArrivalTick - b.ArrivalTick || a.team - b.team);
    if (candidates.length) return { wave: candidates[0], discovered: s.tick };
    assert.ok(s.tick < 3600, "No warning wave found naturally by tick 3600");
    s = await advance(Math.min(60, 3600 - s.tick));
  }
}
async function toTick(tick) {
  let s = await state();
  assert.ok(s.tick <= tick, `Cannot rewind from ${s.tick} to ${tick}`);
  while (s.tick < tick) s = await advance(Math.min(60, tick - s.tick));
  return s;
}
const signature = s => ({ tick: s.tick, seed: s.seed, waves: s.waves, counts: s.counts,
  counters: s.counters, pending: s.pending, checksum: s.checksum });
async function focus(wave, mobile = false) {
  await page.setViewportSize(mobile ? { width: 390, height: 844 } : { width: 1280, height: 800 });
  await page.evaluate(async ({ wave, zoom }) => {
    Object.assign(window.spaceBattle.camera, { x: wave.X, y: wave.Y, zoom });
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    await window.advanceTime(0);
  }, { wave, zoom: mobile ? 2 : 3 });
}
async function pair(name) {
  const shots = [];
  for (const hidden of [false, true]) {
    await page.evaluate(async hidden => { window.__warp.hidden = hidden; await window.advanceTime(0); }, hidden);
    const buffer = await page.locator("#scene").screenshot({
      style: "#battle > :not(#scene) { visibility: hidden !important; }",
    });
    await save(`${name}-${hidden ? "hidden" : "lit"}.png`, buffer);
    shots.push(PNG.sync.read(buffer));
  }
  await page.evaluate(async () => { window.__warp.hidden = false; await window.advanceTime(0); });
  assert.equal(shots[0].width, shots[1].width); assert.equal(shots[0].height, shots[1].height);
  const metrics = measure(shots[0].data, shots[1].data, shots[0].width, shots[0].height);
  check(metrics.budgetPass, `${name}: fixed brightness guardrails exceeded`);
  return metrics;
}
async function drawRecord(row) {
  return page.evaluate(async row => {
    const c = window.__warp; c.hidden = false; c.draws = []; c.record = true;
    await window.advanceTime(0); c.record = false;
    const camera = JSON.parse(window.render_game_to_text()).camera;
    const x = camera.width / 2 + (row[3] - camera.x) * camera.scale;
    const y = camera.height / 2 + (camera.width < 620 ? 30 : 15) - (row[4] - camera.y) * camera.scale;
    const match = c.draws.findIndex(d => d.source === "warp" && d.kind === row[2] - 4 &&
      Math.abs(d.x - x) < 1 && Math.abs(d.y - y) < 1 && Math.abs(d.alpha - row[15]) < 1e-5);
    const hulls = c.draws.map((d, i) => d.source === "ships" || d.source === "guns" ? i : -1).filter(i => i >= 0);
    return { match, draw: c.draws[match] ?? null, firstHull: hulls[0] ?? -1, lastHull: hulls.at(-1) ?? -1,
      visible: x >= 0 && y >= 0 && x < camera.width && y < camera.height, identified: c.identified };
  }, row);
}
async function firstSpawn(wave, before) {
  let s = before;
  const bound = wave.ArrivalTick + 300;
  while (s.tick < bound) {
    const previous = s, ids = new Set(previous.ships.map(r => r[0]));
    s = await advance(1);
    if (s.tick < wave.ArrivalTick)
      assert.equal(s.counters.reinforcementShips, before.counters.reinforcementShips, "No reinforcement before due tick; ordinary production is allowed");
    if (s.counters.reinforcementShips > previous.counters.reinforcementShips) {
      assert.ok(s.tick >= wave.ArrivalTick, "No early reinforcement");
      const ship = s.ships.find(r => !ids.has(r[0]) && r[1] === wave.team && Math.hypot(r[3] - wave.X, r[4] - wave.Y) <= 29);
      assert.ok(ship, "New reinforcement ship is within 29 world units of its wave corner");
      assert.ok(ship[3] * ship[5] + ship[4] * ship[6] < 0, "Initial velocity points inward");
      assert.ok(ship[3] * Math.cos(ship[7]) + ship[4] * Math.sin(ship[7]) < 0, "Initial heading points inward");
      const flash = s.particles.find(r => r[2] === 6 + wave.team && r[1] === wave.team &&
        Math.hypot(r[3] - ship[3], r[4] - ship[4]) < 1e-4 && r[15] > 0);
      assert.ok(flash, "Immediate team-specific arrival flash at new ship position");
      return { state: s, ship, flash };
    }
  }
  throw new Error("No reinforcement delta within the bounded first-arrival search");
}
try {
  browser = await chromium.launch({ headless: true });
  context = await browser.newContext({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 });
  // Do not run stale previews or fabricate a pass while artwork is pending.
  const response = await context.request.get(new URL("assets/warp-atlas.json", url).href, { timeout: 10000, maxRedirects: 0 });
  if (!response.ok()) {
    report.status = "held"; report.reason = `Warp manifest HTTP ${response.status()}; wait for main's 8047 publish`;
  } else {
    const manifest = await response.json(); report.manifest = manifest;
    const imageResponse = await context.request.get(new URL("assets/warp-atlas.png", url).href, { timeout: 10000, maxRedirects: 0 });
    if (!imageResponse.ok()) {
      const error = new Error(`Warp PNG HTTP ${imageResponse.status()}; wait for main's 8047 publish`);
      error.code = "ARTWORK_PENDING"; throw error;
    }
    const png = PNG.sync.read(await imageResponse.body());
    assert.equal(png.width, manifest.width); assert.equal(png.height, manifest.height);
    assert.equal(manifest.alphaThreshold, 32);
    assert.deepEqual(manifest.sprites.map(s => s.kind).sort((a, b) => a - b), [0, 1, 2, 3]);
    assert.ok(png.width === png.height && png.width % 2 === 0, "Square 2x2 PNG grid");
    for (const s of manifest.sprites) {
      const cell = png.width / 2, x = s.kind % 2 * cell, y = Math.floor(s.kind / 2) * cell;
      assert.ok([s.x, s.y, s.width, s.height].every(Number.isInteger) && s.width > 0 && s.width === s.height);
      assert.ok(s.x >= x && s.y >= y && s.x + s.width <= x + cell && s.y + s.height <= y + cell);
      assert.ok(s.pivotX === s.width / 2 && s.pivotY === s.height / 2 && (!Object.hasOwn(s, "family") || s.family === "warp"));
      let transparent = false, visible = false;
      for (let j = s.y; j < s.y + s.height; j++) for (let i = s.x; i < s.x + s.width; i++) {
        const alpha = png.data[(j * png.width + i) * 4 + 3];
        transparent ||= alpha === 0; visible ||= alpha > 32;
      }
      assert.ok(transparent && visible, `Warp crop ${s.kind} has real transparent and visible pixels`);
    }
    await context.tracing.start({ screenshots: true, snapshots: true, sources: true }); tracing = true;
    await context.route("**/*", route => {
      const request = route.request(), target = new URL(request.url());
      return isLocal(target) && request.method() === "GET" ? route.continue() : route.abort();
    });
    page = await context.newPage();
    page.on("pageerror", e => report.errors.push(e.message));
    page.on("console", e => { if (e.type() === "error") report.errors.push(e.text()); });
    page.on("response", r => { if (r.status() >= 400) report.errors.push(`${r.status()} ${r.url()}`); });
    await page.addInitScript(({ manifest, base }) => {
      const original = CanvasRenderingContext2D.prototype.drawImage, atlases = new WeakMap();
      window.__warp = { hidden: false, record: false, draws: [], identified: 0 };
      CanvasRenderingContext2D.prototype.drawImage = function(image, ...args) {
        const c = window.__warp;
        let source = image instanceof HTMLImageElement ? ["warp", "effects", "guns", "ships"]
          .find(name => image.src === new URL(`assets/${name}-atlas.png`, base).href) : atlases.get(image);
        if (image instanceof HTMLImageElement && source && this.canvas.id !== "scene") {
          atlases.set(this.canvas, source); if (source === "warp") c.identified++;
        }
        const sprite = source === "warp" && args.length === 8 && manifest.sprites.find(s =>
          s.x === args[0] && s.y === args[1] && s.width === args[2] && s.height === args[3]);
        if (this.canvas.id === "scene" && c.record && c.draws.length < 20000) {
          const m = this.getTransform(), sx = sprite ? args[4] + sprite.pivotX / sprite.width * args[6] : 0;
          const sy = sprite ? args[5] + sprite.pivotY / sprite.height * args[7] : 0;
          c.draws.push({ source: source ?? "other", kind: sprite?.kind, alpha: this.globalAlpha,
            x: m.a * sx + m.c * sy + m.e, y: m.b * sx + m.d * sy + m.f,
            extent: sprite ? Math.abs(args[6]) : null });
        }
        if (sprite && this.canvas.id === "scene" && c.hidden) return;
        return original.call(this, image, ...args);
      };
    }, { manifest, base: url.href });
    await page.goto(url.href, { timeout: 120000 });
    await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
    const initialText = await page.evaluate(() => JSON.parse(window.render_game_to_text()));
    assert.equal(initialText.runtimeMode, "aot-binary"); assert.equal(initialText.paused, true);
    assert.equal(initialText.tick, 0); assert.equal(initialText.visualAssets.warpAtlas, true);
    assert.deepEqual(initialText.visualAssets.particleKinds, [0, 1, 2, 3]);
    assert.deepEqual(initialText.visualAssets.warpKinds, [0, 1, 2, 3]);
    const initial = await state(); report.initial = signature(initial);
    assert.equal(initial.binary.version, 3); assert.equal(initial.binary.headerValues, 80); assert.equal(initial.binary.rowValues, 16);
    const discovery = await firstWave(), wave = discovery.wave; report.firstWave = discovery;
    const warning = await toTick(wave.ArrivalTick - 30); report.warning = signature(warning);
    assert.equal(warning.counters.reinforcementShips, initial.counters.reinforcementShips, "First warning precedes all reinforcement spawning");
    const portal = warning.particles.find(r => r[2] === 4 + wave.team && r[1] === wave.team && Math.hypot(r[3] - wave.X, r[4] - wave.Y) < 1e-4);
    assert.ok(portal && portal[8] > 0 && portal[15] > 0, "Real team portal during the warning phase");
    for (const mobile of [false, true]) {
      await focus(wave, mobile);
      const draw = await drawRecord(portal), label = mobile ? "mobile-warning" : "desktop-warning";
      assert.equal(draw.identified, 1, "Exactly one canvas loaded from warp PNG independently of effects PNG");
      assert.ok(draw.match >= 0 && draw.visible, `${label}: portal uses the visible warp source`);
      if (draw.firstHull >= 0) assert.ok(draw.match < draw.firstHull, "Portal is behind ships and guns");
      const metrics = await pair(label); check(metrics.changedFraction > 0, `${label}: portal must change actual pixels`);
      await page.screenshot({ path: path.join(output, `${label}-page.png`) });
      report.checkpoints.push({ label, tick: warning.tick, portal, draw, metrics });
    }
    await focus(wave);
    const arrival = await firstSpawn(wave, warning); report.arrival = { state: signature(arrival.state), ship: arrival.ship, flash: arrival.flash };
    for (let age = 0; age <= 30; age++) {
      const s = age ? await advance(1) : arrival.state;
      const row = s.particles.find(r => r[0] === arrival.flash[0]);
      const record = { age, tick: s.tick, row: row ?? null };
      if (age < 30) {
        assert.ok(row, `Arrival flash alive at age ${age}`);
        assert.equal(row[3], arrival.flash[3]); assert.equal(row[4], arrival.flash[4]);
        assert.equal(row[5], 0); assert.equal(row[6], 0);
        const radius = arrival.flash[8] * (1 + 1.4 * Math.min(age / 8, 1));
        const alpha = 0.85 * (1 - age / 30) ** 2;
        assert.ok(Math.abs(row[8] - radius) < 1e-4 && Math.abs(row[15] - alpha) < 1e-5, "Shared Capture radius/alpha animation");
        record.draw = await drawRecord(row);
        assert.ok(record.draw.match >= 0 && record.draw.visible, "Arrival uses warp PNG and stays in view");
        assert.ok(record.draw.lastHull >= 0 && record.draw.match > record.draw.lastHull, "Arrival is drawn above ships and guns");
      } else assert.equal(row, undefined, "First flash expires exactly at age 30");
      record.metrics = await pair(`arrival-age-${String(age).padStart(2, "0")}`);
      if (age === 0) check(record.metrics.changedFraction > 0, "Arrival changes actual composited pixels");
      report.frames.push(record);
    }
    report.afterFlash = signature(await state());
    let smoke = await state(), secondArrival = null;
    while (smoke.tick < 6000 && !secondArrival && Date.now() < deadline - 60000) {
      smoke = await advance(Math.min(60, 6000 - smoke.tick));
      const team = smoke.waves.findIndex((w, team) => team === wave.team && w.wave > wave.wave && smoke.tick >= w.ArrivalTick &&
        smoke.particles.some(r => r[2] === 6 + team && r[1] === team));
      if (team >= 0) secondArrival = { team, tick: smoke.tick, wave: smoke.waves[team],
        reinforcementShips: smoke.counters.reinforcementShips };
    }
    report.smoke = { tick: smoke.tick, waves: smoke.waves, counters: smoke.counters,
      secondArrival, status: secondArrival ? "observed" : "bounded-inconclusive" };
    check(secondArrival || smoke.tick < 6000, "No second same-team arrival observed by the natural 6000-tick smoke bound");
    report.validation = await page.evaluate(() => window.spaceBattle.validate());
    await page.evaluate(() => { window.spaceBattle.pause(true); window.spaceBattle.reset(); });
    assert.deepEqual(signature(await state()), signature(initial), "Paused reset restores seed, counters, waves and checksum");
    const replay = await firstWave(); assert.deepEqual(replay, discovery, "Deterministic first wave timing, corner and composition");
    const replayWarning = await toTick(wave.ArrivalTick - 30);
    assert.deepEqual(signature(replayWarning), signature(warning), "Warning replay counts and checksum");
    const replayArrival = await firstSpawn(wave, replayWarning);
    assert.deepEqual(signature(replayArrival.state), signature(arrival.state), "First arrival replay counts and checksum");
    assert.deepEqual(replayArrival.ship, arrival.ship); assert.deepEqual(replayArrival.flash, arrival.flash);
    await toTick(report.afterFlash.tick);
    assert.deepEqual(signature(await state()), report.afterFlash, "Full flash lifetime replay checksum");
    report.replay = { pass: true, tick: report.afterFlash.tick, checksum: report.afterFlash.checksum };
    await page.evaluate(() => window.spaceBattle.validate());
    check(!report.capDiagnosticMissing, "Cap unverified: expose aliveShips on both reinforcements or spaceBattle.aliveTeamCounts(); render rows exclude docked ships");
    check(report.errors.length === 0, "No browser/page/HTTP errors");
    report.status = report.failures.length ? "failed" : "complete";
    report.pass = report.failures.length === 0;
  }
} catch (error) {
  if (error.code === "ARTWORK_PENDING") { report.status = "held"; report.reason = error.message; }
  else { report.status = "failed"; report.failures.push(String(error)); report.exception = error.stack; }
  if (page) await page.screenshot({ path: path.join(output, "failure.png") }).catch(() => {});
} finally {
  if (tracing) await context.tracing.stop({ path: path.join(output, "trace.zip") }).catch(error => report.failures.push(String(error)));
  if (browser) await browser.close();
  report.pass &&= report.failures.length === 0;
  if (report.failures.length) report.status = "failed";
  await save("warp.json", JSON.stringify(report, null, 2) + "\n");
}
console.log(JSON.stringify({ pass: report.pass, status: report.status, output, failures: report.failures, smoke: report.smoke.status }));
if (!report.pass) process.exitCode = report.status === "held" ? 2 : 1;
