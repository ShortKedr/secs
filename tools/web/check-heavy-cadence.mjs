import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright";

const arg = (name, fallback) => {
  const index = process.argv.indexOf(name);
  return index < 0 ? fallback : process.argv[index + 1];
};
const output = path.resolve(arg("--output", "Benchmarks.Local/tmp/heavy-cadence-checks"));
const url = arg("--url", "http://127.0.0.1:8040/");
const baselineUrl = arg("--baseline-url", null);
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [];
try {
  async function opening(source, label, current) {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    page.on("pageerror", (error) => errors.push(error.message));
    page.on("console", (message) => { if (message.type() === "error") errors.push(message.text()); });
    await page.goto(source + "?test=1&lang=ru");
    await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
    const result = {};
    for (const profile of ["low", "nominal", "high"]) {
      await page.selectOption("#profile", profile);
      await page.click("#reset");
      await page.waitForFunction((p) => window.spaceBattle.snapshot.profile === p && window.spaceBattle.snapshot.tick === 0, profile);
      result[profile] = await page.evaluate(async (isCurrent) => {
        const state = () => {
          const text = JSON.parse(window.render_game_to_text());
          return { tick: text.tick, revision: text.revision, checksum: text.checksum,
            counts: text.counts, counters: text.counters };
        };
        const initial = state();
        if (initial.tick !== 0 || window.spaceBattle.runtimeMode !== "aot-binary") throw new Error("Fresh production AOT battle required");
        if (isCurrent && initial.revision !== "mounted-armament-v7") throw new Error("Current armament revision v7 required");
        const placement = window.spaceBattle.snapshot.ships.map((row) => Array.from(row));
        const samples = [initial];
        for (const tick of [120, 300, 600, 1200, 1800]) {
          await window.advanceTime((tick - window.spaceBattle.snapshot.tick) * 1000 / 60);
          samples.push(state());
        }
        return { placement, samples };
      }, current);
      if (current) {
        const samples = result[profile].samples;
        assert.ok(samples.every((sample) => sample.counters.selfHits === 0), `${profile}: projectiles never hit their owner`);
        const quietTick = profile === "high" ? 120 : profile === "low" ? 600 : 300;
        const quiet = samples.find((sample) => sample.tick === quietTick);
        assert.equal(quiet.counters.heavyShots, 0, `${profile}: no heavy shots before minimum birth charge`);
        assert.ok(quiet.counters.shots > 0 && quiet.counters.turretShots > 0 && quiet.counters.hits > 0,
          `${profile}: ordinary guns continue real combat during heavy charge`);
        assert.ok(samples.at(-1).counters.heavyShots > 0, `${profile}: charge does not disable heavy cannons`);
        const first = samples.find((sample) => sample.tick === 600);
        await page.click("#reset");
        await page.evaluate(() => window.advanceTime(10000));
        const replay = await page.evaluate(() => {
          const text = JSON.parse(window.render_game_to_text());
          return { checksum: text.checksum, counters: text.counters };
        });
        assert.equal(replay.checksum, first.checksum, `${profile}: independent reset repeats exact state`);
        assert.deepEqual(replay.counters, first.counters, `${profile}: independent reset repeats shot schedule`);
        if (profile === "nominal") {
          await page.screenshot({ path: path.join(output, `${label}-battle-600.png`) });
          await page.click("#reset");
          await page.evaluate(() => window.advanceTime(5000));
          await page.screenshot({ path: path.join(output, `${label}-opening-300.png`) });
        }
      }
    }
    await page.close();
    return result;
  }
  const before = baselineUrl ? await opening(baselineUrl, "before", false) : null;
  const after = await opening(url, "after", true);
  if (before) {
    for (const profile of Object.keys(after)) {
      assert.deepEqual(after[profile].placement, before[profile].placement, `${profile}: fleet placement/durability unaffected`);
      // Total heavy shots also depend on survival after turret balance changes.
      // Exact charge/repeat deadlines are checked in isolated headless scenes.
    }
  }
  assert.deepEqual(errors, [], "No browser errors");
  await fs.writeFile(path.join(output, "opening.json"), JSON.stringify({
    note: "Deterministic opening diagnostics, not a timing benchmark, kill attribution or balance proof.", before, after,
  }, null, 2));
  console.log("PASS: AOT birth charge, ordinary combat, eventual heavy fire, reset replay and unchanged initial fleet in all profiles.");
} finally {
  await browser.close();
}
