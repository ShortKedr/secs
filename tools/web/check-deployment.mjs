import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { chromium, webkit } from "playwright";

const argument = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const url = localUrl(argument("--url", "http://127.0.0.1:8063/"));
const beforeUrl = localUrl(argument("--before-url", "http://127.0.0.1:8062/"));
const output = path.resolve(argument("--output", "Benchmarks.Local/tmp/fleet-deployment-checks"));
assert.ok(output.startsWith(path.resolve("Benchmarks.Local/tmp") + path.sep));
const engine = argument("--engine", "chromium");
assert.ok(["chromium", "webkit"].includes(engine));
await fs.mkdir(output, { recursive: true });
const browser = await ({ chromium, webkit })[engine].launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const errors = [], runs = [];
page.on("pageerror", error => errors.push(String(error)));
page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });

function localUrl(value) {
  const result = new URL(value);
  assert.ok(result.protocol === "http:" && ["127.0.0.1", "localhost"].includes(result.hostname), "Local previews only");
  result.searchParams.set("test", "1"); result.searchParams.set("lang", "ru");
  return result.href;
}
async function load(address) {
  await page.goto(address, { waitUntil: "networkidle", timeout: 120000 });
  await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
}
async function inspect() {
  return page.evaluate(() => ({ state: JSON.parse(window.render_game_to_text()),
    ships: window.spaceBattle.snapshot.ships.map(row => Array.from(row)),
    turrets: window.spaceBattle.snapshot.turrets.map(row => Array.from(row)) }));
}
function geometry(ships) {
  let overlaps = 0, clearance = Infinity, opposingClearance = Infinity;
  for (let i = 0; i < ships.length; i++) for (let j = i + 1; j < ships.length; j++) {
    const a = ships[i], b = ships[j];
    const distance = Math.hypot(a[3] - b[3], a[4] - b[4]) - a[8] - b[8];
    if (distance < -1e-5) overlaps++;
    clearance = Math.min(clearance, distance);
    if (a[1] !== b[1]) opposingClearance = Math.min(opposingClearance, distance);
  }
  return { overlaps, clearance, opposingClearance,
    width: Math.max(...ships.map(s => s[3] + s[8])) - Math.min(...ships.map(s => s[3] - s[8])),
    height: Math.max(...ships.map(s => s[4] + s[8])) - Math.min(...ships.map(s => s[4] - s[8])) };
}
function checkInitial(data) {
  assert.equal(data.state.tick, 0); assert.equal(data.state.deploymentRevision, "dispersed-front-v1");
  assert.equal(data.state.runtimeMode, "aot-binary"); assert.equal(data.ships.length, 198);
  assert.equal(data.state.colliderDebug.enabled, false);
  assert.equal(data.state.counters.shots, 0); assert.equal(data.state.counters.shipsDestroyed, 0);
  const space = geometry(data.ships);
  assert.equal(space.overlaps, 0); assert.ok(space.clearance >= 3.6 - 1e-4);
  assert.ok(space.opposingClearance > 100, "Initial central standoff, not interleaved fleets");
  const cyan = data.ships.filter(s => s[1] === 0), red = data.ships.filter(s => s[1] === 1);
  assert.equal(cyan.length, 99); assert.equal(red.length, 99);
  for (let i = 0; i < cyan.length; i++) {
    assert.equal(cyan[i][2], red[i][2]); assert.equal(cyan[i][3], -red[i][3]); assert.equal(cyan[i][4], red[i][4]);
  }
  for (const ship of data.ships) {
    assert.ok(Math.abs(ship[3]) + ship[8] < 210 && Math.abs(ship[4]) + ship[8] < 210);
    assert.equal(ship[9], 1); assert.equal(ship[10], 1); assert.equal(ship[11], 0);
  }
  return space;
}
try {
  await load(beforeUrl);
  const before = await inspect();
  await page.screenshot({ path: path.join(output, "before-initial.png") });
  await load(url);
  const after = await inspect(), space = checkInitial(after);
  assert.deepEqual(after.ships.map(s => [s[0], s[1], s[2], s[5], s[6], s[7], s[8], s[9], s[10], s[11]]),
    before.ships.map(s => [s[0], s[1], s[2], s[5], s[6], s[7], s[8], s[9], s[10], s[11]]), "Roster, birth serials, motion and attributes unchanged");
  assert.deepEqual(after.turrets.map(s => [s[0], s[1], s[2], s[8], s[9]]),
    before.turrets.map(s => [s[0], s[1], s[2], s[8], s[9]]), "Mounted guns unchanged");
  assert.ok(space.width > geometry(before.ships).width * 1.6);
  assert.ok(space.height > geometry(before.ships).height * 1.6);
  await page.screenshot({ path: path.join(output, "after-initial.png") });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.evaluate(() => window.spaceBattle.refresh());
  await page.screenshot({ path: path.join(output, "after-mobile-initial.png") });
  await page.setViewportSize({ width: 1440, height: 900 });

  for (const [profile, seed] of [["low", 7331], ["nominal", 7331], ["high", 7331], ["nominal", 27]]) {
    let expected;
    for (let repeat = 0; repeat < 2; repeat++) {
      await page.evaluate(({ profile, seed }) => {
        document.querySelector("#profile").value = profile; document.querySelector("#seed").value = String(seed);
        window.spaceBattle.reset(); window.spaceBattle.pause(true); window.spaceBattle.fit();
      }, { profile, seed });
      const initial = await inspect(); checkInitial(initial);
      const checkpoints = [{ tick: 0, checksum: initial.state.checksum, counts: initial.state.counts, counters: initial.state.counters }];
      let previousTick = 0;
      for (const tick of [120, 360, 900]) {
        await page.evaluate(ms => window.advanceTime(ms), (tick - previousTick) * 1000 / 60);
        const data = await inspect();
        assert.equal(data.state.tick, tick); assert.equal(data.state.counters.selfHits, 0);
        assert.ok(data.ships.every(s => s.every(Number.isFinite)), "No nonfinite simulation rows");
        checkpoints.push({ tick, checksum: data.state.checksum, counts: data.state.counts, counters: data.state.counters });
        previousTick = tick;
        if (profile === "nominal" && seed === 7331 && !repeat)
          await page.screenshot({ path: path.join(output, `after-tick-${tick}.png`) });
      }
      assert.ok(checkpoints.at(-1).counters.shots > 0 && checkpoints.at(-1).counters.hits > 0 &&
        checkpoints.at(-1).counters.shipsDestroyed > 0, "Dispersed fleet reaches actual live combat");
      if (repeat) assert.deepEqual(checkpoints, expected, "Independent same-environment replay after reset");
      else expected = checkpoints;
      runs.push({ profile, seed, repeat, checkpoints });
    }
  }
  assert.deepEqual(errors, []);
  const result = { engine, before: geometry(before.ships), after: space, runs, errors };
  await fs.writeFile(path.join(output, "results.json"), JSON.stringify(result, null, 2));
  console.log(JSON.stringify({ before: result.before, after: space }));
  console.log(`PASS: ${engine} production AOT deployment, unchanged roster, eight replay runs, live battle and desktop/mobile captures`);
} catch (error) {
  await fs.writeFile(path.join(output, "failure.json"), JSON.stringify({ error: String(error), stack: error.stack, runs, errors }, null, 2));
  throw error;
} finally { await browser.close(); }
