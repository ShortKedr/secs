import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright";

const arg = (name, fallback) => {
  const i = process.argv.indexOf(name);
  return i < 0 ? fallback : process.argv[i + 1];
};
const output = path.resolve(arg("--output", "Benchmarks.Local/tmp/web-armament"));
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [];
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  page.on("pageerror", (e) => errors.push(e.message));
  page.on("console", (e) => { if (e.type() === "error") errors.push(e.text()); });
  await page.goto(arg("--url", "http://127.0.0.1:8030/") + "?test=1&lang=ru");
  await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
  const evidence = await page.evaluate(() => {
    const game = window.spaceBattle;
    assertReady();
    const frames = [];
    for (let tick = 0; tick < 240; tick++) {
      window.advanceTime(1000 / 60);
      const s = game.snapshot;
      const round = s.projectiles.find((r) => r[2] === 4);
      if (!round) continue;
      const before = Array.from(round);
      window.advanceTime(1000 / 60);
      const after = game.snapshot.projectiles.find((r) => r[0] === before[0]);
      if (!after) continue;
      frames.push({ before, after: Array.from(after), tick: game.snapshot.tick });
      break;
    }
    const parent = game.snapshot.ships.find((r) => r[2] === 10) || game.snapshot.ships.find((r) => r[2] >= 8);
    if (!parent) throw new Error("No carrier available for close view");
    game.camera.x = parent[3]; game.camera.y = parent[4]; game.camera.zoom = 6;
    game.select(parent[0]); game.refresh();
    return { frames, parent: Array.from(parent), state: JSON.parse(window.render_game_to_text()) };
    function assertReady() {
      if (game.runtimeMode !== "aot-binary") throw new Error("Production AOT build required");
    }
  });
  assert.equal(evidence.frames.length, 1, "Heavy round survives between consecutive ticks");
  const { before, after } = evidence.frames[0];
  assert.ok(Math.hypot(after[3] - before[3], after[4] - before[4]) > 0.1, "Heavy round actually travels");
  const guns = evidence.state.turrets.filter((t) => t.parentSerial === evidence.parent[0]);
  assert.ok(guns.length >= 4, "Carrier has several independent visible guns");
  assert.ok(guns.some((g) => Math.abs(g.angle - evidence.parent[7]) > 0.1), "Gun swivel differs from hull heading");
  await page.screenshot({ path: path.join(output, "carrier-close.png") });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: path.join(output, "carrier-mobile.png") });
  assert.deepEqual(errors, [], "No browser errors");
  await fs.writeFile(path.join(output, "evidence.json"), JSON.stringify({ ...evidence, errors }, null, 2));
  console.log("PASS: AOT heavy projectile travel, carrier-mounted swivel and desktop/mobile close captures");
} finally {
  await browser.close();
}
