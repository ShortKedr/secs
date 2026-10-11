import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { PNG } from "pngjs";
import { GUARDRAILS, measure, compareFamilies } from "./effect-contrast-metrics.mjs";

const options = new Map();
for (let i = 2; i < process.argv.length; i += 2) {
  const key = process.argv[i], value = process.argv[i + 1];
  if (!["--url", "--baseline-url", "--output"].includes(key) || !value || options.has(key))
    throw new Error("Usage: node tools/web/check-effect-contrast.mjs --url NEW_AOT_URL [--baseline-url http://127.0.0.1:8044/] [--output Benchmarks.Local/tmp/effect-contrast]");
  options.set(key, value);
}
if (!options.has("--url")) throw new Error("--url is required; no candidate site is assumed");
function localUrl(value) {
  const url = new URL(value);
  assert.ok(url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname), "Only local HTTP sites are allowed");
  assert.ok(!url.username && !url.password, "No URL credentials");
  url.search = ""; url.hash = "";
  if (!url.pathname.endsWith("/")) url.pathname += "/";
  return url.href;
}
const urls = { baseline: localUrl(options.get("--baseline-url") || "http://127.0.0.1:8044/"),
  candidate: localUrl(options.get("--url")) };
const root = fileURLToPath(new URL("../../", import.meta.url));
const tmp = path.resolve(root, "Benchmarks.Local/tmp");
const output = path.resolve(root, options.get("--output") || "Benchmarks.Local/tmp/effect-contrast");
assert.ok(output.startsWith(tmp + path.sep), "Output must be a subdirectory of project-ignored Benchmarks.Local/tmp");
// Reject junction/symlink escapes before creating or writing evidence.
let ancestor = output;
while (true) {
  try {
    const real = await fs.realpath(ancestor);
    assert.ok(real === path.resolve(root) || real.startsWith(path.resolve(root) + path.sep), "Output resolves outside project");
    break;
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
    ancestor = path.dirname(ancestor);
  }
}
await fs.mkdir(output, { recursive: true });
const report = { urls, guardrails: GUARDRAILS,
  scope: "Fixed artistic visual-regression budgets on composited linear-sRGB Rec709 Y; not medical safety, accessibility certification, bloom-engine measurement or hardware brightness.",
  protocol: { viewport: [1280, 800], deviceScaleFactor: 1, seed: 7331,
    ticks: [300, 600], profiles: ["low", "nominal", "high"], zooms: [1, 3],
    probeExtent: 128, probeOpacity: 1,
    probeGeometry: "Baseline crop aspect ratio; centered identical rendered rectangles, independent of new atlas geometry. Whole rectangle is the fraction/percentile denominator, including transparent pixels.",
    tileGrid: "Non-overlapping 64x64 scene-pixel tiles; partial tiles use their actual area.",
    contribution: "max(0, Y(lit)-Y(hidden)); effects hidden by drawImage interception only; no RGB multiplier.",
    capture: "Playwright #scene PNG screenshots; sibling UI overlays hidden by screenshot-only CSS in both captures to exclude changing performance text." },
  manifests: {}, frames: [], probes: {}, parity: [], errors: [], failures: [] };
const browser = await chromium.launch({ headless: true });
const state = page => page.evaluate(() => JSON.parse(window.render_game_to_text()));
const settle = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
function check(condition, message) { if (!condition) report.failures.push(message); }
function validateManifest(m) {
  assert.ok(Number.isInteger(m.width) && m.width > 0 && Number.isInteger(m.height) && m.height > 0);
  assert.equal(m.sprites.length, 9);
  for (const family of ["projectile", "particle"]) {
    const sprites = m.sprites.filter(s => s.family === family);
    assert.deepEqual(sprites.map(s => s.kind).sort((a, b) => a - b), family === "projectile" ? [0, 1, 2, 3, 4] : [0, 1, 2, 3]);
    for (const s of sprites) {
      assert.ok([s.x, s.y, s.width, s.height].every(Number.isInteger));
      assert.ok(s.x >= 0 && s.y >= 0 && s.width > 0 && s.height > 0 && s.x + s.width <= m.width && s.y + s.height <= m.height);
      assert.ok(Number.isFinite(s.pivotX) && Number.isFinite(s.pivotY) && s.pivotX >= 0 && s.pivotX < s.width && s.pivotY >= 0 && s.pivotY < s.height);
      if (family === "particle") {
        assert.equal(s.width, s.height); assert.equal(s.pivotX, s.width / 2); assert.equal(s.pivotY, s.height / 2);
      }
    }
  }
}
async function open(label) {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 });
  page.on("pageerror", e => report.errors.push({ label, error: e.message }));
  page.on("console", e => { if (e.type() === "error") report.errors.push({ label, error: e.text() }); });
  page.on("response", r => { if (r.status() >= 400) report.errors.push({ label, error: `${r.status()} ${r.url()}` }); });
  // Fetch each build's manifest, not the working tree's potentially newer manifest.
  const response = await page.request.get(new URL("assets/effects-atlas.json", urls[label]).href);
  assert.ok(response.ok(), `${label} manifest HTTP ${response.status()}`);
  const manifest = await response.json(); validateManifest(manifest);
  report.manifests[label] = manifest;
  await page.addInitScript(({ manifest, imageUrl }) => {
    const original = CanvasRenderingContext2D.prototype.drawImage;
    const atlases = new WeakSet();
    window.__contrast = { hidden: false, draws: 0, identified: 0, probe: null, last: null };
    CanvasRenderingContext2D.prototype.drawImage = function (image, ...args) {
      const control = window.__contrast;
      if (image instanceof HTMLImageElement && image.src === imageUrl &&
          this.canvas.width === manifest.width && this.canvas.height === manifest.height) {
        atlases.add(this.canvas); control.identified++;
      }
      const sprite = image instanceof HTMLCanvasElement && atlases.has(image) && args.length === 8 &&
        manifest.sprites.find(s => s.x === args[0] && s.y === args[1] && s.width === args[2] && s.height === args[3]);
      if (sprite) {
        control.draws++;
        if (control.hidden) return;
        if (control.probe) {
          const { width, height, opacity, family, kind } = control.probe;
          if (family !== sprite.family || kind !== sprite.kind) throw new Error("Unexpected sprite in isolated probe");
          const anchorX = args[4] + sprite.pivotX / sprite.width * args[6];
          const anchorY = args[5] + sprite.pivotY / sprite.height * args[7];
          args.splice(4, 4, anchorX - width / 2, anchorY - height / 2, width, height);
          this.globalAlpha = opacity;
          const m = this.getTransform();
          control.last = { family, kind, width, height, alpha: this.globalAlpha,
            x: m.a * args[4] + m.c * args[5] + m.e,
            y: m.b * args[4] + m.d * args[5] + m.f,
            matrix: [m.a, m.b, m.c, m.d, m.e, m.f] };
        }
      }
      return original.call(this, image, ...args);
    };
  }, { manifest, imageUrl: new URL("assets/effects-atlas.png", urls[label]).href });
  const url = new URL(urls[label]); url.searchParams.set("test", "1");
  await page.goto(url.href, { timeout: 120000 });
  await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
  const initial = await state(page);
  assert.equal(initial.runtimeMode, "aot-binary"); assert.equal(initial.paused, true);
  assert.equal(initial.seed, 7331); assert.equal(initial.tick, 0);
  assert.equal(initial.visualAssets.effectsAtlas, true);
  assert.equal(await page.evaluate(() => window.__contrast.identified), 1, "Exactly one real effectsAtlas identified");
  return page;
}
async function pixels(page, hidden) {
  await page.evaluate(hidden => { window.__contrast.hidden = hidden; window.__contrast.draws = 0; }, hidden);
  await settle(page);
  const buffer = await page.locator("#scene").screenshot({
    style: "#battle > :not(#scene) { visibility: hidden !important; }",
  });
  return { buffer, ...PNG.sync.read(buffer), draws: await page.evaluate(() => window.__contrast.draws) };
}
function crop(frame, rect) {
  assert.ok(rect.x >= 0 && rect.y >= 0 && rect.x + rect.width <= frame.width && rect.y + rect.height <= frame.height,
    `Probe crop ${JSON.stringify(rect)} outside ${frame.width}x${frame.height} frame`);
  const result = new PNG({ width: rect.width, height: rect.height });
  PNG.bitblt(frame, result, rect.x, rect.y, rect.width, rect.height, 0, 0);
  return result;
}
async function savePair(prefix, lit, hidden) {
  await fs.writeFile(path.join(output, `${prefix}-lit.png`), lit.buffer || PNG.sync.write(lit));
  await fs.writeFile(path.join(output, `${prefix}-hidden.png`), hidden.buffer || PNG.sync.write(hidden));
}
async function probes(page, label, geometry) {
  const before = await state(page);
  const camera = await page.evaluate(() => ({ ...window.spaceBattle.camera }));
  await page.evaluate(() => {
    const game = window.spaceBattle, keys = ["ships", "projectiles", "debris", "particles", "turrets"];
    window.__contrast.saved = keys.map(key => [key, game.snapshot[key].slice()]);
    for (const key of keys) game.snapshot[key].length = 0;
    Object.assign(game.camera, { x: 0, y: 0, zoom: 1 });
  });
  const results = [];
  try {
    for (const sprite of geometry) {
      const width = 128, height = Math.max(1, Math.round(128 * sprite.height / sprite.width));
      await page.evaluate(({ sprite, width, height }) => {
        const row = Array(16).fill(0);
        row[0] = 900000 + sprite.kind; row[1] = -1; row[2] = sprite.kind;
        row[5] = 120; row[8] = 14; row[15] = 1;
        window.spaceBattle.snapshot[sprite.family === "particle" ? "particles" : "projectiles"].push(row);
        window.__contrast.probe = { family: sprite.family, kind: sprite.kind, width, height, opacity: 1 };
        window.__contrast.last = null;
      }, { sprite, width, height });
      const hidden = await pixels(page, true), lit = await pixels(page, false);
      const draw = await page.evaluate(() => window.__contrast.last);
      assert.ok(draw && lit.draws > 0 && hidden.draws > 0, "Real sprite draw reached both probe captures");
      assert.ok(draw.matrix.slice(0, 4).every((value, i) => Math.abs(value - [1, 0, 0, 1][i]) < 1e-9),
        "Probe extent is in real screenshot pixels");
      const rect = { x: Math.floor(draw.x), y: Math.floor(draw.y), width: width + 1, height: height + 1 };
      const light = crop(lit, rect), dark = crop(hidden, rect);
      const metrics = measure(light.data, dark.data, rect.width, rect.height);
      check(metrics.changedFraction > 0, `${label} ${sprite.family}/${sprite.kind}: no added luminance`);
      results.push({ family: sprite.family, kind: sprite.kind, rect, draw, metrics });
      await savePair(`${label}-probe-${sprite.family}-${sprite.kind}`, light, dark);
      await page.evaluate(family => {
        window.spaceBattle.snapshot[family === "particle" ? "particles" : "projectiles"].length = 0;
      }, sprite.family);
    }
  } finally {
    await page.evaluate(camera => {
      const c = window.__contrast;
      for (const [key, rows] of c.saved) {
        const target = window.spaceBattle.snapshot[key]; target.length = 0;
        for (const row of rows) target.push(row);
      }
      delete c.saved; c.probe = null; c.hidden = false;
      Object.assign(window.spaceBattle.camera, camera);
    }, camera);
    await settle(page);
    const restored = await state(page);
    assert.equal(restored.checksum, before.checksum, "Synthetic probes restore checksum");
    assert.deepEqual(restored.counts, before.counts);
    assert.deepEqual(restored.renderCounts, before.renderCounts);
    assert.equal(restored.tick, before.tick);
    assert.deepEqual(await page.evaluate(() => ({ ...window.spaceBattle.camera })), camera);
    assert.equal(await page.evaluate(() => window.spaceBattle.validate()), before.checksum);
    report.parity.push({ label, check: "probe-restoration", tick: before.tick, checksum: before.checksum, counts: before.counts });
  }
  return results;
}
try {
  const pages = { baseline: await open("baseline"), candidate: await open("candidate") };
  for (const profile of report.protocol.profiles) {
    for (const page of Object.values(pages)) await page.selectOption("#profile", profile);
    for (const tick of report.protocol.ticks) {
      for (const page of Object.values(pages)) await page.evaluate(() => window.advanceTime(5000));
      const before = await state(pages.baseline), after = await state(pages.candidate);
      assert.equal(before.tick, tick); assert.equal(after.tick, tick);
      assert.equal(after.checksum, before.checksum, `${profile}/${tick} simulation checksum parity`);
      assert.deepEqual(after.counts, before.counts); assert.deepEqual(after.renderCounts, before.renderCounts);
      report.parity.push({ profile, tick, checksum: after.checksum, counts: after.counts });
      for (const zoom of report.protocol.zooms) {
        for (const [label, page] of Object.entries(pages)) {
          const savedCamera = await page.evaluate(() => ({ ...window.spaceBattle.camera }));
          try {
            await page.evaluate(zoom => Object.assign(window.spaceBattle.camera, { x: 0, y: 0, zoom }), zoom);
            const hidden = await pixels(page, true), lit = await pixels(page, false);
            assert.equal(lit.width, hidden.width); assert.equal(lit.height, hidden.height);
            const metrics = measure(lit.data, hidden.data, lit.width, lit.height);
            report.frames.push({ label, profile, tick, zoom, width: lit.width, height: lit.height, effectDraws: lit.draws, metrics });
            if (label === "candidate") check(metrics.budgetPass, `${profile}/${tick}/zoom${zoom}: candidate washout budget exceeded`);
            if (tick === 600 && zoom === 3 && profile !== "low")
              await savePair(`${label}-${profile}-${tick}-zoom${zoom}`, lit, hidden);
          } finally {
            await page.evaluate(camera => { window.__contrast.hidden = false; Object.assign(window.spaceBattle.camera, camera); }, savedCamera);
            await settle(page);
          }
          const restored = await state(page);
          assert.equal(restored.tick, tick); assert.equal(restored.checksum, after.checksum);
          assert.deepEqual(restored.counts, after.counts);
        }
      }
    }
  }
  for (const [label, page] of Object.entries(pages))
    report.probes[label] = await probes(page, label, report.manifests.baseline.sprites);
  assert.deepEqual(report.probes.candidate.map(s => s.rect), report.probes.baseline.map(s => s.rect), "Matched isolated probe geometry");
  report.spriteComparison = report.probes.baseline.map(before => {
    const after = report.probes.candidate.find(s => s.family === before.family && s.kind === before.kind);
    return { family: before.family, kind: before.kind,
      baselineStrongVisibleFraction: before.metrics.strongVisibleFraction,
      candidateStrongVisibleFraction: after.metrics.strongVisibleFraction,
      baselineP75AddedY: before.metrics.p75AddedY, candidateP75AddedY: after.metrics.p75AddedY,
      p75Ratio: before.metrics.p75AddedY > 0 ? after.metrics.p75AddedY / before.metrics.p75AddedY : null };
  });
  report.improvement = compareFamilies(report.probes.baseline, report.probes.candidate);
  for (const family of report.improvement) check(family.pass, `${family.family}: median p75 effect-added Y did not improve by >=10% (zero baseline is inconclusive)`);
  check(report.errors.length === 0, "Browser console/page/HTTP errors present");
} catch (error) {
  report.failures.push(String(error)); report.exception = error.stack;
} finally {
  await browser.close();
  report.pass = report.failures.length === 0 && report.errors.length === 0;
  await fs.writeFile(path.join(output, "contrast.json"), JSON.stringify(report, null, 2) + "\n");
}
console.log(`${report.pass ? "PASS" : "FAIL"}: effect contrast verification; evidence ${path.join(output, "contrast.json")}`);
for (const failure of report.failures) console.error(failure);
if (!report.pass) process.exitCode = 1;
