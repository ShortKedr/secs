import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { chromium } from "playwright";

const arg = (name, fallback) => {
  const i = process.argv.indexOf(name);
  return i < 0 ? fallback : process.argv[i + 1];
};
const url = arg("--url", "http://localhost:8047/");
const baselineUrl = arg("--baseline-url", null);
const output = path.resolve(arg("--output", "Benchmarks.Local/tmp/web-team-tint"));
const root = await fs.realpath(".");
const temporaryRoot = path.join(root, "Benchmarks.Local", "tmp");
const inside = (parent, child) => {
  const relative = path.relative(parent, child);
  return relative !== "" && relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative);
};
assert.ok(inside(temporaryRoot, output), "Output must be a fresh subdirectory of this project's Benchmarks.Local/tmp");
const testUrl = (address) => {
  const value = new URL(address);
  assert.equal(value.protocol, "http:", "Only local HTTP previews are allowed");
  assert.ok(!value.username && !value.password, "Preview URL must not contain credentials");
  assert.ok(["localhost", "127.0.0.1", "[::1]"].includes(value.hostname), "Local previews only");
  value.searchParams.set("test", "1"); value.searchParams.set("lang", "en");
  return value.href;
};
const previewUrl = testUrl(url), baselinePreviewUrl = baselineUrl ? testUrl(baselineUrl) : null;
const statIfPresent = async (target) => {
  try { return await fs.lstat(target); }
  catch (error) { if (error.code === "ENOENT") return null; throw error; }
};
const assertDirectory = async (target) => {
  const stat = await fs.lstat(target);
  assert.ok(stat.isDirectory() && !stat.isSymbolicLink(), `No symlinks/junctions allowed in output path: ${target}`);
  assert.equal(path.relative(target, await fs.realpath(target)), "", `Output realpath escaped: ${target}`);
};
const assertIgnored = (target) => execFileSync("git", ["check-ignore", "--quiet", "--", target], { cwd: root });
assertIgnored(output);
assert.equal(await statIfPresent(output), null, "Use a fresh output directory (no existing files may be overwritten)");
// Validate every existing ancestor before creating any directory, including junctions.
const directories = [];
let parent = root;
for (const component of path.relative(root, output).split(path.sep)) {
  assert.ok(component && !component.includes(":"), "Invalid output path component");
  parent = path.join(parent, component);
  directories.push(parent);
  if (await statIfPresent(parent)) await assertDirectory(parent);
}
const outputFile = async (filename) => {
  for (const directory of directories) await assertDirectory(directory);
  const target = path.join(output, filename);
  assert.ok(inside(output, target), "Output file escaped directory");
  assertIgnored(target);
  assert.equal(await statIfPresent(target), null, `Refusing to overwrite existing output: ${target}`);
  return target;
};
const assets = {};
for (const family of ["ships", "guns"]) {
  assets[family] = {
    manifest: JSON.parse(await fs.readFile(`SpaceBattle.Game/Assets/${family}-atlas.json`, "utf8")),
    png: (await fs.readFile(`SpaceBattle.Game/Assets/${family}-atlas.png`)).toString("base64"),
  };
}
for (const directory of directories) {
  await assertDirectory(path.dirname(directory));
  if (!(await statIfPresent(directory))) await fs.mkdir(directory);
  await assertDirectory(directory);
}
const browser = await chromium.launch({ headless: true });
const errors = [], evidence = { parity: [], probes: [], baselineCompared: Boolean(baselineUrl) };
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 });
  page.on("pageerror", (e) => errors.push(e.message));
  page.on("console", (e) => { if (e.type() === "error") errors.push(e.text()); });
  // Opt-in negative control: substituted source PNG must make this check fail.
  if (process.argv.includes("--negative-source")) {
    await page.route("**/assets/ships-atlas.png", (route) => route.fulfill({
      contentType: "image/png", body: Buffer.from(assets.guns.png, "base64"),
    }));
  }
  await page.addInitScript(() => {
    const prototype = CanvasRenderingContext2D.prototype;
    const drawImage = prototype.drawImage, getImageData = prototype.getImageData;
    const putImageData = prototype.putImageData;
    const textures = new Map();
    window.__tintProbe = { textures, draws: [], record: false, mode: "actual", pixelReads: 0, pixelWrites: 0 };
    prototype.getImageData = function (...args) {
      if (this.canvas.id !== "scene") window.__tintProbe.pixelReads++;
      return getImageData.apply(this, args);
    };
    prototype.putImageData = function (...args) {
      if (this.canvas.id !== "scene") window.__tintProbe.pixelWrites++;
      return putImageData.apply(this, args);
    };
    prototype.drawImage = function (image, ...args) {
      const probe = window.__tintProbe;
      if (image instanceof HTMLImageElement) {
        const family = /\/(ships|guns)-atlas\.png(?:\?|$)/.exec(image.src)?.[1];
        if (family) textures.set(this.canvas, { family, id: textures.size });
      }
      const entry = textures.get(image);
      if (this.canvas.id === "scene" && entry) {
        if (probe.record) {
          const m = this.getTransform();
          probe.draws.push({ ...entry, args, matrix: [m.a, m.b, m.c, m.d, m.e, m.f],
            alpha: this.globalAlpha, filter: this.filter, composite: this.globalCompositeOperation });
        }
        if (probe.mode === "hidden") return;
        if (probe.mode === "expected") image = probe.expected.get(image);
      }
      return drawImage.call(this, image, ...args);
    };
  });
  const ready = (p) => p.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
  const state = (p) => p.evaluate(() => JSON.parse(window.render_game_to_text()));
  await page.goto(previewUrl); await ready(page);
  const initial = await state(page);
  assert.equal(initial.runtimeMode, "aot-binary", "Use the real production AOT preview");
  assert.deepEqual(initial.visualAssets.teamTint, {
    palette: [[0.2, 0.85, 0.95], [1, 0.37, 0.4]], hullTextures: 2, gunTextures: 2,
  });
  assert.equal(initial.visualAssets.warpAtlas, true);
  assert.deepEqual(initial.visualAssets.gunKinds.slice().sort((a, b) => a - b), [-1, 0, 1, 2, 3, 4]);
  let baseline;
  if (baselineUrl) {
    baseline = await browser.newPage(); await baseline.goto(baselinePreviewUrl); await ready(baseline);
  }
  for (const tick of [0, 300, 600]) {
    if (tick) {
      await page.evaluate(() => window.advanceTime(5000));
      if (baseline) await baseline.evaluate(() => window.advanceTime(5000));
    }
    const current = await state(page);
    assert.equal(current.tick, tick);
    if (baseline) {
      const before = await state(baseline);
      for (const key of ["checksum", "counts", "renderCounts", "counters", "pendingReinforcements", "reinforcements"])
        assert.deepEqual(current[key], before[key], `Before/after ${key} at tick ${tick}`);
      assert.deepEqual(await page.evaluate(() => window.spaceBattle.aliveTeamCounts()),
        await baseline.evaluate(() => window.spaceBattle.aliveTeamCounts()));
    }
    evidence.parity.push({ tick, checksum: current.checksum, counts: current.counts,
      aliveTeams: await page.evaluate(() => window.spaceBattle.aliveTeamCounts()) });
  }
  if (baseline) await baseline.close();
  // Runtime sources are compared with the actual local PNGs, not a generated swatch.
  evidence.atlases = await page.evaluate(async (assets) => {
    const probe = window.__tintProbe;
    const palette = [[0.2, 0.85, 0.95], [1, 0.37, 0.4]], expected = new Map(), results = [];
    for (const family of ["ships", "guns"]) {
      const image = new Image(); image.src = `data:image/png;base64,${assets[family].png}`; await image.decode();
      const entries = [...probe.textures].filter(([, item]) => item.family === family);
      if (entries.length !== 2) throw new Error(`${family}: exactly two cached textures required`);
      for (let team = 0; team < 2; team++) {
        const texture = entries[team][0], reference = document.createElement("canvas");
        reference.width = image.naturalWidth; reference.height = image.naturalHeight;
        if (texture.width !== reference.width || texture.height !== reference.height)
          throw new Error(`${family}: source PNG dimensions changed`);
        const context = reference.getContext("2d"); context.drawImage(image, 0, 0);
        const pixels = context.getImageData(0, 0, reference.width, reference.height);
        for (let i = 0; i < pixels.data.length; i += 4) {
          for (let channel = 0; channel < 3; channel++)
            pixels.data[i + channel] = pixels.data[i + channel] * palette[team][channel];
          if (family === "guns" && pixels.data[i + 3] <= assets.guns.manifest.alphaThreshold)
            pixels.data[i + 3] = 0;
        }
        context.putImageData(pixels, 0, 0);
        const wanted = context.getImageData(0, 0, reference.width, reference.height).data;
        const actual = texture.getContext("2d").getImageData(0, 0, texture.width, texture.height).data;
        let nontransparent = 0;
        for (let i = 0; i < actual.length; i++) {
          if (actual[i] !== wanted[i]) throw new Error(`${family} team ${team}: RGBA mismatch at byte ${i}`);
          if (i % 4 === 3 && actual[i] !== 0) nontransparent++;
        }
        if (!nontransparent) throw new Error("Empty atlas");
        expected.set(texture, reference); results.push({ family, team, nontransparent, id: entries[team][1].id });
      }
    }
    probe.expected = expected;
    return results;
  }, assets);
  for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(viewport);
    await page.evaluate(() => window.spaceBattle.fit());
    await page.screenshot({ path: await outputFile(`${viewport.width}-battle.png`) });
    const probes = await page.evaluate(async ({ assets, viewport }) => {
      const game = window.spaceBattle, snapshot = game.snapshot, probe = window.__tintProbe;
      const groups = ["ships", "turrets", "projectiles", "particles", "debris"];
      const saved = groups.map((key) => snapshot[key].slice()), camera = { ...game.camera };
      const canvas = document.querySelector("#scene"), ctx = canvas.getContext("2d");
      const checksum = JSON.parse(window.render_game_to_text()).checksum;
      const nextDraw = () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      const capture = () => ctx.getImageData(0, 0, canvas.width, canvas.height).data;
      const difference = (a, b) => {
        let changed = 0;
        for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) changed++;
        return changed;
      };
      const results = [];
      try {
        for (const key of groups) snapshot[key].length = 0;
        Object.assign(game.camera, { x: 0, y: 0, zoom: 8 });
        const reads = probe.pixelReads, writes = probe.pixelWrites, textureCount = probe.textures.size;
        for (const team of [0, 1]) {
          for (const kind of assets.ships.manifest.sprites.map((s) => s.class)) {
            const row = Array(16).fill(0);
            Object.assign(row, { 0: 900000, 1: team, 2: kind, 7: Math.PI / 2, 8: 22, 9: 1, 10: 1 });
            snapshot.ships.push(row);
            await check("hull", kind, team, 0, 0);
            snapshot.ships.length = 0;
          }
          for (let kind = 0; kind < 5; kind++) {
            const row = Array(16).fill(0);
            Object.assign(row, { 0: 900001, 1: team, 2: kind, 7: Math.PI / 2, 8: 18 });
            snapshot.turrets.push(row);
            const cold = await check("gun", kind, team, 0, 0);
            row[10] = 1; const recoil = await check("gun", kind, team, 1, 0);
            if (JSON.stringify(cold.draws[0].matrix) !== JSON.stringify(recoil.draws[0].matrix))
              throw new Error("Recoil moved gun base");
            if (JSON.stringify(cold.draws[1].matrix) === JSON.stringify(recoil.draws[1].matrix))
              throw new Error("Recoil did not move gun head");
            row[11] = 1; await check("gun", kind, team, 1, 1);
            snapshot.turrets.length = 0;
          }
        }
        if (probe.pixelReads !== reads || probe.pixelWrites !== writes || probe.textures.size !== textureCount)
          throw new Error("Atlas preparation/pixel processing repeated during rendering");
      } finally {
        probe.mode = "actual"; probe.record = false;
        for (let i = 0; i < groups.length; i++) snapshot[groups[i]].splice(0, snapshot[groups[i]].length, ...saved[i]);
        Object.assign(game.camera, camera); await nextDraw();
      }
      if (JSON.parse(window.render_game_to_text()).checksum !== checksum) throw new Error("Probe changed simulation");
      return results;
      async function check(family, kind, team, recoil, heat) {
        probe.mode = "actual"; probe.draws = []; probe.record = true;
        await nextDraw();
        const drawCount = family === "gun" ? 2 : [5, 6, 8, 9, 10].includes(kind) ? 3 : 1;
        const actual = capture(), draws = probe.draws.slice(-drawCount); probe.record = false;
        if (!draws.length) throw new Error("No sprite rendered");
        const hull = draws.find((d) => d.family === "ships");
        const guns = draws.filter((d) => d.family === "guns");
        if (family === "hull" && (!hull || hull.id !== team)) throw new Error("Wrong hull team texture");
        if ((family === "gun" || [5, 6, 8, 9, 10].includes(kind)) &&
            (guns.length !== 2 || guns.some((d) => d.id !== team + 2))) throw new Error("Wrong base/head team texture");
        for (const draw of draws) {
          if (draw.filter !== "none" || draw.composite !== "source-over" || draw.alpha !== 1)
            throw new Error("Tint leaked into render context");
        }
        probe.mode = "expected"; await nextDraw();
        if (difference(actual, capture())) throw new Error(`Rendered source-pixel parity failed: ${family}/${kind}/${team}`);
        probe.mode = "hidden"; await nextDraw(); const changed = difference(actual, capture());
        if (changed < 100) throw new Error("Rendered sprite is blank or too small");
        probe.mode = "actual";
        if (family === "gun" && heat === 1) {
          await nextDraw();
          const baseMatrix = guns[0].matrix;
          const center = ctx.getImageData(Math.floor(baseMatrix[4]), Math.floor(baseMatrix[5]), 1, 1).data;
          if (center[0] !== 209 || center[1] !== 122 || center[2] !== 61) throw new Error("Heat indicator was tinted");
        }
        const result = { viewport, family, kind, team, recoil, heat, changed, draws };
        results.push(result); return result;
      }
    }, { assets, viewport });
    evidence.probes.push(...probes);
  }
  assert.deepEqual(errors, [], "No browser errors");
  await fs.writeFile(await outputFile("evidence.json"), JSON.stringify(evidence, null, 2), { flag: "wx" });
  console.log(`Team tint passed: ${evidence.atlases.length} cached atlases, ${evidence.probes.length} render probes; baseline ${baselineUrl ? "compared" : "not supplied"}.`);
} finally {
  await browser.close();
}
