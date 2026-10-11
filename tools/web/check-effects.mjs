import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright";

const arg = (name, fallback) => {
  const i = process.argv.indexOf(name);
  return i < 0 ? fallback : process.argv[i + 1];
};
const url = arg("--url", "http://127.0.0.1:8039/");
const baselineUrl = arg("--baseline-url", null);
const output = path.resolve(arg("--output", "Benchmarks.Local/tmp/web-effects"));
const manifest = JSON.parse(await fs.readFile("SpaceBattle.Game/Assets/effects-atlas.json", "utf8"));
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [], evidence = { parity: [], pixels: [], invalidManifests: [] };
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  page.on("pageerror", (e) => errors.push(e.message));
  page.on("console", (e) => { if (e.type() === "error") errors.push(e.text()); });
  await page.addInitScript(({ width, height }) => {
    const original = CanvasRenderingContext2D.prototype.drawImage;
    CanvasRenderingContext2D.prototype.drawImage = function (image, ...args) {
      const effect = image instanceof HTMLCanvasElement && image.width === width && image.height === height && args.length === 8;
      if (effect && window.__recordEffects) {
        const m = this.getTransform();
        window.__effectDraws.push({ source: args.slice(0, 4), destination: args.slice(4),
          matrix: [m.a, m.b, m.c, m.d, m.e, m.f], alpha: this.globalAlpha });
      }
      if (effect && window.__hideEffects) return;
      return original.call(this, image, ...args);
    };
  }, { width: manifest.width, height: manifest.height });
  const ready = async (p) => p.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
  const state = (p = page) => p.evaluate(() => JSON.parse(window.render_game_to_text()));
  await page.goto(url + "?test=1&lang=ru");
  await ready(page);
  const initial = await state();
  assert.equal(initial.runtimeMode, "aot-binary");
  assert.equal(initial.visualAssets.effectsAtlas, true);
  assert.deepEqual(initial.visualAssets.projectileKinds, [0, 1, 2, 3, 4]);
  assert.deepEqual(initial.visualAssets.particleKinds, [0, 1, 2, 3]);
  let baseline;
  if (baselineUrl) {
    baseline = await browser.newPage();
    await baseline.goto(baselineUrl + "?test=1&lang=ru"); await ready(baseline);
  }
  for (const tick of [0, 300, 600]) {
    if (tick) {
      await page.evaluate(() => window.advanceTime(5000));
      if (baseline) await baseline.evaluate(() => window.advanceTime(5000));
    }
    const current = await state();
    assert.equal(current.tick, tick);
    if (baseline) {
      const before = await state(baseline);
      assert.equal(current.checksum, before.checksum, `Visual change preserves checksum at ${tick}`);
      assert.deepEqual(current.counts, before.counts, `Visual change preserves entity counts at ${tick}`);
    }
    evidence.parity.push({ tick, checksum: current.checksum, counts: current.counts });
  }
  if (baseline) await baseline.close();
  await page.screenshot({ path: path.join(output, "desktop-battle.png") });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: path.join(output, "mobile-battle.png") });
  await page.setViewportSize({ width: 1280, height: 800 });
  evidence.pixels = await page.evaluate(async (sprites) => {
    const game = window.spaceBattle, snapshot = game.snapshot;
    const checksum = JSON.parse(window.render_game_to_text()).checksum;
    const groups = ["ships", "projectiles", "debris", "particles", "turrets"];
    const saved = groups.map((key) => snapshot[key].slice()), camera = { ...game.camera };
    const canvas = document.querySelector("#scene"), context = canvas.getContext("2d");
    const nextDraw = () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const rectangle = () => {
      const s = JSON.parse(window.render_game_to_text()), dpr = canvas.width / s.camera.width;
      const p = { x: s.camera.width / 2, y: s.camera.height / 2 + 15 };
      return { x: Math.round((p.x - 160) * dpr), y: Math.round((p.y - 160) * dpr),
        size: Math.round(320 * dpr), anchor: [p.x * dpr, p.y * dpr] };
    };
    const proof = [];
    try {
      for (const key of groups) snapshot[key].length = 0;
      Object.assign(game.camera, { x: 0, y: 0, zoom: 6 });
      await nextDraw();
      const region = rectangle();
      const capture = () => context.getImageData(region.x, region.y, region.size, region.size).data;
      for (const sprite of sprites) {
        const row = Array(16).fill(0);
        row[0] = 900000 + sprite.kind; row[1] = -1; row[2] = sprite.kind;
        row[5] = 120; row[7] = Math.PI / 2; row[8] = sprite.family === "particle" ? 14 : 1;
        row[12] = row[13] = row[14] = row[15] = 1;
        const rows = snapshot[sprite.family === "particle" ? "particles" : "projectiles"];
        rows.push(row);
        window.__hideEffects = true; await nextDraw(); const hidden = capture();
        window.__effectDraws = []; window.__recordEffects = true; window.__hideEffects = false;
        await nextDraw(); const visible = capture(); window.__recordEffects = false;
        let changed = 0, energy = 0;
        for (let i = 0; i < visible.length; i += 4) {
          const delta = Math.abs(visible[i] - hidden[i]) + Math.abs(visible[i + 1] - hidden[i + 1]) + Math.abs(visible[i + 2] - hidden[i + 2]);
          if (delta > 10) changed++; energy += delta;
        }
        const draw = window.__effectDraws.at(-1);
        if (!draw) throw new Error("Effect never reaches drawImage");
        const record = { family: sprite.family, kind: sprite.kind, changed, energy, draw, anchor: region.anchor };
        if (sprite.family === "particle") {
          row[15] = 0.25; await nextDraw(); const faded = capture(); let fadedEnergy = 0;
          for (let i = 0; i < faded.length; i += 4)
            fadedEnergy += Math.abs(faded[i] - hidden[i]) + Math.abs(faded[i + 1] - hidden[i + 1]) + Math.abs(faded[i + 2] - hidden[i + 2]);
          record.fadeRatio = fadedEnergy / energy;
        } else {
          row[5] = 0; row[6] = 120;
          window.__effectDraws = []; window.__recordEffects = true; await nextDraw();
          record.vertical = window.__effectDraws.at(-1); window.__recordEffects = false;
          row[6] = 0;
          window.__effectDraws = []; window.__recordEffects = true; await nextDraw();
          record.stationary = window.__effectDraws.at(-1); window.__recordEffects = false;
        }
        proof.push(record); rows.length = 0;
      }
    } finally {
      window.__hideEffects = false; window.__recordEffects = false;
      for (let i = 0; i < groups.length; i++) {
        const rows = snapshot[groups[i]]; rows.length = saved[i].length;
        for (let j = 0; j < rows.length; j++) rows[j] = saved[i][j];
      }
      Object.assign(game.camera, camera); await nextDraw();
    }
    if (JSON.parse(window.render_game_to_text()).checksum !== checksum) throw new Error("Pixel probes altered simulation state");
    return proof;
  }, manifest.sprites);
  for (const record of evidence.pixels) {
    const sprite = manifest.sprites.find((s) => s.family === record.family && s.kind === record.kind);
    assert.ok(record.changed > 10, `${record.family} ${record.kind} changes real pixels`);
    assert.deepEqual(record.draw.source, [sprite.x, sprite.y, sprite.width, sprite.height]);
    const [x, y, w, h] = record.draw.destination, [a, b, c, d, e, f] = record.draw.matrix;
    assert.ok(Math.abs(w / h - sprite.width / sprite.height) < 1e-7, "Preserved source aspect ratio");
    const px = x + sprite.pivotX / sprite.width * w, py = y + sprite.pivotY / sprite.height * h;
    assert.ok(Math.abs(a * px + c * py + e - record.anchor[0]) < 1e-6 &&
      Math.abs(b * px + d * py + f - record.anchor[1]) < 1e-6, "Head/center pivot remains at entity position");
    if (record.family === "projectile") {
      assert.equal(record.draw.alpha, 1, "Particle alpha cannot leak into projectile pass");
      assert.ok(a > 0 && Math.abs(b) < 1e-7, "Actual rightward velocity overrides stale angle");
      for (const rotated of [record.vertical, record.stationary])
        assert.ok(Math.abs(rotated.matrix[0]) < 1e-7 && rotated.matrix[1] < 0, "Upward velocity and stationary heading use world-Y inversion");
    } else {
      assert.equal(sprite.width, sprite.height, "Particles have their own square atlas cells");
      assert.equal(sprite.pivotX * 2, sprite.width, "Particle pivot X is centered");
      assert.equal(sprite.pivotY * 2, sprite.height, "Particle pivot Y is centered");
      assert.ok(Math.abs(w - h) < 1e-7, "Particle destination is a square, not a projectile strip");
      assert.ok(record.fadeRatio > 0.15 && record.fadeRatio < 0.4, "Lifetime alpha dims particle image");
    }
  }
  for (const defect of ["duplicate", "pivot", "missing", "family", "bounds", "particle-aspect", "particle-pivot"]) {
    const bad = structuredClone(manifest);
    if (defect === "duplicate") bad.sprites.push(bad.sprites[0]);
    if (defect === "pivot") bad.sprites[0].pivotX = bad.sprites[0].width;
    if (defect === "missing") bad.sprites.pop();
    if (defect === "family") bad.sprites[0].family = "unknown";
    if (defect === "bounds") bad.sprites[0].x = bad.width;
    if (defect === "particle-aspect") bad.sprites.find((s) => s.family === "particle").height--;
    if (defect === "particle-pivot") bad.sprites.find((s) => s.family === "particle").pivotX++;
    await page.route("**/assets/effects-atlas.json", (route) => route.fulfill({ contentType: "application/json", body: JSON.stringify(bad) }));
    await page.reload(); await page.waitForSelector("#retry", { state: "visible", timeout: 120000 });
    assert.equal(await page.evaluate(() => window.spaceBattle.ready), false);
    await page.unroute("**/assets/effects-atlas.json"); await page.click("#retry"); await ready(page);
    assert.equal((await state()).checksum, initial.checksum, "Retry recovers original simulation");
    evidence.invalidManifests.push(defect);
  }
  assert.deepEqual(errors, [], "No page or console errors");
  await fs.writeFile(path.join(output, "checks.json"), JSON.stringify(evidence, null, 2) + "\n");
  console.log("PASS: nine generated effects change pixels; square centered particles, heading/head anchors, fade, replay parity, desktop/mobile and invalid-atlas recovery");
} finally {
  await browser.close();
}
