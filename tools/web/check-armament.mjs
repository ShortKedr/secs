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
  await page.addInitScript(() => {
    const drawImage = CanvasRenderingContext2D.prototype.drawImage;
    CanvasRenderingContext2D.prototype.drawImage = function (image, ...args) {
      const gun = image instanceof HTMLCanvasElement && image.width === 1024 && image.height === 1536 && args.length === 8;
      if (gun && window.__recordGunDraws) {
        const m = this.getTransform();
        window.__gunDraws.push({ source: args.slice(0, 4), destination: args.slice(4), matrix: [m.a, m.b, m.c, m.d, m.e, m.f] });
      }
      if (gun && window.__hideGunHeads && args[0] !== 114) return;
      return drawImage.call(this, image, ...args);
    };
  });
  page.on("pageerror", (e) => errors.push(e.message));
  page.on("console", (e) => { if (e.type() === "error") errors.push(e.text()); });
  await page.goto(arg("--url", "http://127.0.0.1:8030/") + "?test=1&lang=ru");
  await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
  const evidence = await page.evaluate(() => {
    const game = window.spaceBattle;
    assertReady();
    const frames = [];
    // Heavy guns now charge on birth; the first real shot is no longer an opening volley.
    for (let tick = 0; tick < 1800; tick++) {
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
    game.select(parent[0]);
    window.__gunDraws = []; window.__recordGunDraws = true;
    game.refresh(); window.__recordGunDraws = false;
    return { frames, parent: Array.from(parent), state: JSON.parse(window.render_game_to_text()), spriteDraws: window.__gunDraws };
    function assertReady() {
      if (game.runtimeMode !== "aot-binary") throw new Error("Production AOT build required");
    }
  });
  assert.equal(evidence.frames.length, 1, "Heavy round survives between consecutive ticks");
  assert.equal(evidence.state.visualAssets.gunsAtlas, true, "Generated gun sprites are loaded");
  assert.deepEqual(evidence.state.visualAssets.gunKinds, [-1, 0, 1, 2, 3, 4]);
  const { before, after } = evidence.frames[0];
  assert.ok(Math.hypot(after[3] - before[3], after[4] - before[4]) > 0.1, "Heavy round actually travels");
  const guns = evidence.state.turrets.filter((t) => t.parentSerial === evidence.parent[0]);
  assert.ok(guns.length >= 4, "Carrier has several independent visible guns");
  assert.ok(guns.some((g) => Math.abs(g.angle - evidence.parent[7]) > 0.1), "Gun swivel differs from hull heading");
  const sources = new Set(evidence.spriteDraws.map((d) => d.source[0]));
  for (const x of [114, 155, 631, 630]) assert.ok(sources.has(x), "Mount, cannon, plasma and heavy sprites reach Canvas drawImage");
  await page.screenshot({ path: path.join(output, "carrier-close.png") });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: path.join(output, "carrier-mobile.png") });
  await page.setViewportSize({ width: 1440, height: 900 });
  const pixelProof = await page.evaluate(async () => {
    const game = window.spaceBattle, snapshot = game.snapshot;
    const checksum = JSON.parse(window.render_game_to_text()).checksum;
    const groups = ["ships", "projectiles", "debris", "particles", "turrets"];
    const saved = groups.map((key) => snapshot[key]);
    const camera = { ...game.camera };
    const row = Array.from(snapshot.turrets.find((r) => r[2] === 1));
    const canvas = document.querySelector("#scene"), context = canvas.getContext("2d");
    const pixels = () => context.getImageData(0, 0, canvas.width, canvas.height).data;
    const difference = (a, b) => {
      let count = 0;
      for (let i = 0; i < a.length; i += 4)
        if (Math.abs(a[i] - b[i]) + Math.abs(a[i + 1] - b[i + 1]) + Math.abs(a[i + 2] - b[i + 2]) > 20) count++;
      return count;
    };
    try {
      // Isolate a render row only; no managed entity or simulation state is changed.
      for (const key of groups) snapshot[key] = [];
      snapshot.turrets = [row];
      game.camera.x = row[3]; game.camera.y = row[4]; game.camera.zoom = 20;
      row[10] = 0; window.__hideGunHeads = true;
      await window.advanceTime(0); const base = pixels();
      window.__hideGunHeads = false;
      const kindPixels = [];
      for (let kind = 0; kind <= 4; kind++) {
        row[2] = kind; await window.advanceTime(0);
        kindPixels.push(difference(base, pixels()));
      }
      row[2] = 1;
      window.__recordGunDraws = true; window.__gunDraws = [];
      await window.advanceTime(0); const head = pixels(), rest = window.__gunDraws;
      row[10] = 1; window.__gunDraws = [];
      await window.advanceTime(0); const recoil = pixels(), fired = window.__gunDraws;
      row[7] += Math.PI / 2; window.__gunDraws = [];
      await window.advanceTime(0); const rotated = pixels(), turned = window.__gunDraws;
      return { headPixels: difference(base, head), recoilPixels: difference(head, recoil),
        rotationPixels: difference(recoil, rotated), kindPixels, rest, fired, turned, checksum };
    } finally {
      window.__recordGunDraws = window.__hideGunHeads = false;
      groups.forEach((key, i) => { snapshot[key] = saved[i]; });
      Object.assign(game.camera, camera);
      game.refresh();
      if (JSON.parse(window.render_game_to_text()).checksum !== checksum)
        throw new Error("Visual checks changed managed simulation state");
    }
  });
  assert.ok(pixelProof.headPixels > 50, "Gun head changes actual canvas pixels");
  assert.ok(pixelProof.recoilPixels > 10, "Recoil moves actual sprite pixels");
  assert.ok(pixelProof.rotationPixels > 50, "Gun sprite visibly rotates");
  assert.ok(pixelProof.kindPixels.every((pixels) => pixels > 50), "All five delivered heads change actual canvas pixels");
  assert.deepEqual(pixelProof.rest[0], pixelProof.fired[0], "Mount does not recoil");
  assert.deepEqual(pixelProof.rest[0], pixelProof.turned[0], "Mount does not swivel with head");
  assert.notDeepEqual(pixelProof.rest[1].matrix, pixelProof.fired[1].matrix, "Head bearing has recoil translation");
  assert.notDeepEqual(pixelProof.fired[1].matrix, pixelProof.turned[1].matrix, "Head rotation reaches Canvas transform");
  const invalid = await browser.newPage();
  await invalid.route("**/assets/guns-atlas.json", async (route) => {
    const response = await route.fetch(), manifest = await response.json();
    manifest.sprites[0].pivotX = -1;
    await route.fulfill({ response, json: manifest });
  });
  await invalid.goto(arg("--url", "http://127.0.0.1:8030/") + "?test=1");
  await invalid.locator("#retry").waitFor({ state: "visible", timeout: 120000 });
  assert.equal(await invalid.evaluate(() => window.spaceBattle.ready), false, "Invalid gun pivot blocks startup");
  await invalid.unroute("**/assets/guns-atlas.json");
  await invalid.locator("#retry").click();
  await invalid.waitForFunction(() => window.spaceBattle.ready, null, { timeout: 120000 });
  await invalid.close();
  assert.deepEqual(errors, [], "No browser errors");
  await fs.writeFile(path.join(output, "evidence.json"), JSON.stringify({ ...evidence, pixelProof, errors }, null, 2));
  console.log("PASS: AOT heavy projectile travel, generated gun sprites/pixels, recoil, fixed mounts, swivel and desktop/mobile captures");
} finally {
  await browser.close();
}
