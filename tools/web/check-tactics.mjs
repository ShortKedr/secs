import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright";

const argument = (name, fallback) => process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : fallback;
const url = new URL(argument("--url", "http://127.0.0.1:8060/"));
assert.ok(url.protocol === "http:" && ["127.0.0.1", "localhost"].includes(url.hostname), "Local preview only");
url.searchParams.set("test", "1"); url.searchParams.set("lang", "ru");
const output = path.resolve(argument("--output", "Benchmarks.Local/tmp/tactics-browser"));
assert.ok(output.startsWith(path.resolve("Benchmarks.Local/tmp") + path.sep));
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const errors = [], runs = [], captures = [];
page.on("pageerror", e => errors.push(String(e)));
page.on("console", m => { if (m.type() === "error") errors.push(m.text()); });
async function inspect() {
  return page.evaluate(async () => {
    const runtime = getDotnetRuntime(0);
    const bridge = (await runtime.getAssemblyExports("SpaceBattle.Web.dll")).SpaceBattle.Web.BattleBridge;
    return {
      state: JSON.parse(window.render_game_to_text()),
      ships: window.spaceBattle.snapshot.ships.map(row => ({ serial: row[0], team: row[1], kind: row[2],
        hull: row[9], shield: row[10], heat: row[11], x: row[3], y: row[4],
        mode: bridge.ShipTactic(row[0], 0), target: bridge.ShipTactic(row[0], 1),
        leader: bridge.ShipTactic(row[0], 2), cover: bridge.ShipTactic(row[0], 3), size: bridge.ShipTactic(row[0], 5) })),
      diagnostics: Array.from({ length: 7 }, (_, field) => bridge.DiagnosticTactics(field)),
    };
  });
}
async function capture(name, serial) {
  await page.evaluate(serial => { window.spaceBattle.follow(serial); window.spaceBattle.camera.zoom = 3; window.spaceBattle.refresh(); }, serial);
  await page.screenshot({ path: path.join(output, name + ".png") });
  await fs.writeFile(path.join(output, name + ".json"), JSON.stringify(await inspect(), null, 2));
  captures.push(name);
  await page.evaluate(() => { window.spaceBattle.select(null); window.spaceBattle.fit(); });
}
try {
  await page.goto(url.href, { waitUntil: "networkidle", timeout: 120000 });
  await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
  let sawGroup = false, sawScreen = false;
  for (const seed of [27, 7331, 61012]) {
    for (let repeat = 0; repeat < 2; repeat++) {
      await page.evaluate(seed => { document.querySelector("#seed").value = String(seed);
        document.querySelector("#profile").value = "nominal"; window.spaceBattle.reset(); window.spaceBattle.pause(true); window.spaceBattle.showColliders(false); }, seed);
      const checkpoints = [];
      for (let tick = 120; tick <= 1200; tick += 120) {
        await page.evaluate(() => window.advanceTime(2000));
        const data = await inspect();
        assert.equal(data.state.tick, tick); assert.equal(data.state.tacticalRevision, "layered-squads-v3");
        assert.equal(data.state.damageRevision, "layered-weapon-damage-v1");
        assert.equal(data.state.runtimeMode, "aot-binary"); assert.equal(data.state.colliderDebug.enabled, false);
        assert.equal(data.state.counters.selfHits, 0); assert.ok(data.diagnostics.every(Number.isFinite));
        const groups = new Map();
        for (const ship of data.ships) if (ship.leader >= 0) {
          assert.ok(ship.kind <= 3, "Large ships never join small squads");
          const group = groups.get(ship.leader) ?? []; group.push(ship); groups.set(ship.leader, group);
        }
        for (const group of groups.values()) {
          // A same-tick dock/death can hide a member before next-phase roster repair.
          assert.ok(group.length >= 1 && group.length <= 4, "Rendered group members stay bounded");
          assert.ok(group.every(s => s.size >= 2 && s.size <= 4 && group.length <= s.size), "Stored phase roster is bounded");
          assert.equal(new Set(group.map(s => s.team)).size, 1, "Same-team group");
          assert.equal(new Set(group.map(s => s.target)).size, 1, "Common live group target");
        }
        checkpoints.push({ tick, checksum: data.state.checksum, groups: groups.size,
          screens: data.ships.filter(s => s.mode === 4).length, counts: data.state.counts,
          counters: data.state.counters, diagnostics: data.diagnostics });
        const attacking = [...groups.values()].find(g => g.filter(s => s.mode === 0 && s.target >= 0).length >= 2);
        if (!sawGroup && attacking) { await capture("joint-attack", attacking[0].serial); sawGroup = true; }
        const screened = data.ships.find(s => s.mode === 4 && s.cover >= 0);
        if (!sawScreen && screened) { await capture("damaged-member-cover", screened.serial); sawScreen = true; }
      }
      if (repeat) assert.deepEqual(checkpoints, runs.find(r => r.seed === seed).checkpoints, "Same-environment full replay");
      runs.push({ seed, repeat, checkpoints });
    }
  }
  assert.ok(sawGroup, "Actual production joint attack observed");
  assert.ok(sawScreen, "Actual production damaged member cover observed");
  assert.deepEqual(errors, []);
  await fs.writeFile(path.join(output, "results.json"), JSON.stringify({ runs, captures, errors, sawGroup, sawScreen }, null, 2));
  console.log("PASS: production AOT group size/team/target, three seeded repeated battles, joint attack and damaged cover captures");
} catch (error) {
  await fs.writeFile(path.join(output, "failure.json"), JSON.stringify({ error: String(error), stack: error.stack, runs, captures, errors }, null, 2));
  throw error;
} finally { await browser.close(); }
