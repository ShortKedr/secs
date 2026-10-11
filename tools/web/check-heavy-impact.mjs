import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { isDeepStrictEqual } from "node:util";
import { chromium } from "playwright";
import { PNG } from "pngjs";
import { measure, GUARDRAILS } from "./effect-contrast-metrics.mjs";

const arg = (name, fallback) => {
  const i = process.argv.indexOf(name);
  return i < 0 ? fallback : process.argv[i + 1];
};
const localUrl = value => {
  const url = new URL(value);
  assert.ok(url.protocol === "http:" && ["127.0.0.1", "localhost", "[::1]"].includes(url.hostname), "Local previews only");
  url.searchParams.set("test", "1"); url.searchParams.set("lang", "ru");
  return url.href;
};
const url = localUrl(arg("--url", "http://127.0.0.1:8046/"));
const baselineUrl = arg("--baseline-url", null);
const output = path.resolve(arg("--output", "Benchmarks.Local/tmp/web-heavy-impact"));
const temporaryRoot = path.resolve("Benchmarks.Local/tmp") + path.sep;
assert.ok(output.startsWith(temporaryRoot), "Evidence stays inside the project's ignored temporary directory");
const manifest = JSON.parse(await fs.readFile("SpaceBattle.Game/Assets/effects-atlas.json", "utf8"));
const sprite = manifest.sprites.find(s => s.family === "particle" && s.kind === 3);
await fs.mkdir(output, { recursive: true });
assert.ok((await fs.realpath(output)).startsWith(await fs.realpath(temporaryRoot) + path.sep), "No output symlink escape");
const browser = await chromium.launch({ headless: true });
const errors = [], evidence = { guardrails: GUARDRAILS, frames: [] };
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  page.on("pageerror", e => errors.push(e.message));
  page.on("console", e => { if (e.type() === "error") errors.push(e.text()); });
  await page.addInitScript(({ manifest, sprite }) => {
    const original = CanvasRenderingContext2D.prototype.drawImage;
    CanvasRenderingContext2D.prototype.drawImage = function(image, ...args) {
      const atlas = image instanceof HTMLCanvasElement && image.width === manifest.width && image.height === manifest.height && args.length === 8;
      const heavy = atlas && args[0] === sprite.x && args[1] === sprite.y;
      if (window.__impactDraws) window.__impactDraws.push(heavy ? "heavy" : atlas ? "otherEffect" : "hullOrGun");
      if (heavy && window.__hideHeavyImpact) return;
      return original.call(this, image, ...args);
    };
  }, { manifest, sprite });
  await page.goto(url);
  await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
  assert.equal(await page.evaluate(() => window.spaceBattle.runtimeMode), "aot-binary");
  evidence.impact = await page.evaluate(async () => {
    for (let tick = 0; tick < 1800; tick++) {
      const frame = await window.advanceTime(1000 / 60);
      const core = frame.particles.find(r => r[2] === 3 && r[5] === 0 && r[6] === 0 && r[8] >= 4 && r[15] === 1);
      if (core) return { tick: frame.tick, serial: core[0], x: core[3], y: core[4], radius: core[8], alpha: core[15] };
    }
    throw new Error("No real heavy impact within the bounded search");
  });
  const impact = evidence.impact;
  await page.evaluate(({ x, y }) => Object.assign(window.spaceBattle.camera, { x, y, zoom: 3 }), impact);
  let baseline;
  if (baselineUrl) {
    baseline = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await baseline.goto(localUrl(baselineUrl));
    await baseline.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
    await baseline.evaluate(ticks => window.advanceTime(ticks * 1000 / 60), impact.tick);
  }
  const gameplay = p => p.evaluate(() => {
    const s = window.spaceBattle.snapshot;
    // Structural telemetry includes changed particle expiry, not just gameplay entities.
    const { structuralAdds, structuralRemoves, ...counters } = s.counters;
    return { tick: s.tick, ships: s.ships, projectiles: s.projectiles, debris: s.debris, turrets: s.turrets, counters };
  });
  const canvas = page.locator("#scene");
  for (let age = 0; age <= 36; age++) {
    if (age) {
      await page.evaluate(() => window.advanceTime(1000 / 60));
      if (baseline) await baseline.evaluate(() => window.advanceTime(1000 / 60));
    }
    if (baseline) assert.ok(isDeepStrictEqual(await gameplay(page), await gameplay(baseline)), `Gameplay unchanged at impact age ${age}`);
    const record = await page.evaluate(async serial => {
      window.__hideHeavyImpact = false; window.__impactDraws = [];
      await window.advanceTime(0);
      const r = window.spaceBattle.snapshot.particles.find(r => r[0] === serial);
      const draws = window.__impactDraws; window.__impactDraws = null;
      return { tick: window.spaceBattle.snapshot.tick, core: r ? { radius: r[8], alpha: r[15], x: r[3], y: r[4] } : null,
        lastImpactDraw: draws.lastIndexOf("heavy"), lastHullDraw: draws.lastIndexOf("hullOrGun") };
    }, impact.serial);
    if (age < 36) {
      assert.ok(record.core && record.core.alpha > 0 && record.core.alpha <= 1);
      assert.equal(record.core.x, impact.x); assert.equal(record.core.y, impact.y);
      assert.ok(record.lastImpactDraw > record.lastHullDraw, "Impact is composited above hulls and turrets");
    } else assert.equal(record.core, null, "Core expires exactly after 36 ticks");
    if (age === 6) assert.ok(record.core.radius > impact.radius, "Blast expands rapidly");
    if (age === 35) assert.ok(record.core.alpha < 0.01, "No abrupt bright disappearance");
    const litBuffer = await canvas.screenshot();
    await fs.writeFile(path.join(output, `impact-age-${String(age).padStart(2, "0")}.png`), litBuffer);
    if (age === 6) {
      await page.setViewportSize({ width: 390, height: 844 });
      await page.screenshot({ path: path.join(output, "mobile-impact.png") });
      await page.setViewportSize({ width: 1280, height: 800 });
      await page.evaluate(() => window.advanceTime(0));
    }
    await page.evaluate(async () => { window.__hideHeavyImpact = true; await window.advanceTime(0); });
    const hiddenBuffer = await canvas.screenshot();
    await page.evaluate(async () => { window.__hideHeavyImpact = false; await window.advanceTime(0); });
    const lit = PNG.sync.read(litBuffer), hidden = PNG.sync.read(hiddenBuffer);
    const metrics = measure(lit.data, hidden.data, lit.width, lit.height);
    assert.ok(metrics.budgetPass, `Heavy impact brightness budget at age ${age}: ${JSON.stringify(metrics)}`);
    if (age === 0) assert.ok(metrics.changedFraction > 0, "Real impact changes rendered pixels");
    evidence.frames.push({ age, ...record, metrics });
  }
  await page.evaluate(() => window.spaceBattle.validate());
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: path.join(output, "mobile-battle.png") });
  assert.deepEqual(errors, [], "No browser errors");
  evidence.gameplayParity = Boolean(baseline);
  evidence.peakMeanAddedY = Math.max(...evidence.frames.map(f => f.metrics.meanAddedY));
  evidence.peakHotFraction = Math.max(...evidence.frames.map(f => f.metrics.hotFraction));
  evidence.peakTileHotFraction = Math.max(...evidence.frames.map(f => f.metrics.maxTileHotFraction));
  await fs.writeFile(path.join(output, "impact.json"), JSON.stringify(evidence, null, 2) + "\n");
  console.log(JSON.stringify({ pass: true, impact, frames: evidence.frames.length, gameplayParity: evidence.gameplayParity,
    peakMeanAddedY: evidence.peakMeanAddedY, peakHotFraction: evidence.peakHotFraction, peakTileHotFraction: evidence.peakTileHotFraction, output }));
} finally {
  await browser.close();
}
