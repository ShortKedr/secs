import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";
import { PNG } from "pngjs";

const root = fileURLToPath(new URL("../../", import.meta.url));
const sha256 = bytes => crypto.createHash("sha256").update(bytes).digest("hex").toUpperCase();
const mime = { ".html": "text/html", ".js": "text/javascript", ".css": "text/css", ".json": "application/json",
  ".wasm": "application/wasm", ".png": "image/png", ".txt": "text/plain", ".md": "text/plain" };

export function validateSettings(s) {
  assert.deepEqual(Object.keys(s).sort(), ["Seed", "Tick", "ShipsPerTeam", "Width", "Height", "Zoom"].sort());
  for (const [key, low, high] of [["Seed", 0, 2147483647], ["Tick", 1, 108000],
    ["ShipsPerTeam", 96, 96], ["Width", 960, 4096], ["Height", 540, 4096]])
    assert.ok(Number.isInteger(s[key]) && s[key] >= low && s[key] <= high, "Invalid cover setting " + key);
  assert.ok(Number.isFinite(s.Zoom) && s.Zoom >= .25 && s.Zoom <= 24, "Invalid cover zoom");
  return s;
}

export function captureMetadata(s, state, ships, browserVersion, buildFiles) {
  assert.equal(state.ready, true); assert.equal(state.paused, true);
  assert.equal(state.tick, s.Tick); assert.equal(state.seed, s.Seed); assert.equal(state.profile, "nominal");
  assert.equal(state.runtimeMode, "aot-binary"); assert.equal(state.camera.zoom, s.Zoom);
  assert.equal(state.camera.width, s.Width); assert.equal(state.camera.height, s.Height);
  assert.equal(state.following, false); assert.equal(state.selected, null); assert.equal(state.colliderDebug.enabled, false);
  assert.equal(state.binary.version, 3); assert.equal(state.binary.headerValues, 80); assert.equal(state.binary.rowValues, 16);
  assert.ok(/^[A-F0-9]{16}$/.test(state.checksum));
  const c = state.counts, r = state.renderCounts, a = state.aliveTeams;
  assert.ok([...Object.values(c), ...Object.values(r), ...a].every(n => Number.isInteger(n) && n >= 0));
  assert.ok(r.ships <= c.ships && r.turrets <= c.turrets);
  assert.equal(state.binary.totalRows, Object.values(r).reduce((sum, n) => sum + n, 0));
  assert.equal(state.statistics.entities, Object.values(c).reduce((sum, n) => sum + n, 0));
  assert.equal(a[0] + a[1], c.ships);
  assert.equal(state.statistics.samples, Math.min(s.Tick, 600));
  assert.ok([state.statistics.meanMs, state.statistics.p95Ms, state.statistics.p99Ms].every(Number.isFinite));
  const assets = state.visualAssets;
  assert.ok(assets.gunsAtlas && assets.effectsAtlas && assets.warpAtlas && assets.teamTint.hullTextures === 2);
  let rendered = 0;
  for (const row of ships) {
    const size = Math.max(row[2] >= 8 ? 13 : 5, row[8] * state.camera.scale * 2);
    const x = s.Width / 2 + (row[3] - state.camera.x) * state.camera.scale;
    const y = state.camera.originY - (row[4] - state.camera.y) * state.camera.scale;
    if (x >= -size && y >= -size && x <= s.Width + size && y <= s.Height + size) rendered++;
  }
  assert.equal(ships.length, r.ships);
  assert.ok(rendered >= 24 && a[0] > 0 && a[1] > 0 && c.projectiles > 0, "Cover must show an active two-sided battle");
  return {
    Tick: s.Tick, Seed: s.Seed, Width: s.Width, Height: s.Height, Zoom: s.Zoom, ConfiguredShipsPerTeam: s.ShipsPerTeam,
    Renderer: "SpaceBattle.Web Chromium Canvas", RuntimeMode: state.runtimeMode, CaptureSurface: "CanvasOnly",
    BrowserVersion: browserVersion, Profile: state.profile, BattleChecksum: state.checksum,
    GameplayRevision: state.revision, PhysicsRevision: state.physicsRevision, FleetRevision: state.fleetRevision,
    Atlas: true, ControlsVerified: true, CountsValid: true, StatisticsValid: true, OfflineCapture: true,
    Ships: r.ships, RenderedShips: rendered, AliveCyan: a[0], AliveRed: a[1], AliveShips: c.ships,
    DockedShips: c.ships - r.ships, Projectiles: c.projectiles, Debris: c.debris, Particles: c.particles,
    AliveEntities: state.statistics.entities, Turrets: r.turrets, AliveTurrets: c.turrets,
    DockedTurrets: c.turrets - r.turrets, HeavyShots: state.counters.heavyShots, TurretShots: state.counters.turretShots,
    BuildFiles: buildFiles,
  };
}

export function pixelEvidence(bytes, settings) {
  const image = PNG.sync.read(bytes);
  assert.equal(image.width, settings.Width); assert.equal(image.height, settings.Height);
  let bright = 0;
  const colors = new Set();
  for (let y = 140; y < image.height - 80; y++) for (let x = 0; x < image.width; x++) {
    const i = (y * image.width + x) * 4, r = image.data[i], g = image.data[i + 1], b = image.data[i + 2];
    colors.add(r * 65536 + g * 256 + b); if (r + g + b > 100) bright++;
  }
  assert.ok(bright > 100 && colors.size > 16, "Blank/monochrome cover scene");
  return { Nonblank: true, SceneBrightPixels: bright, SceneUniqueColors: colors.size };
}

async function projectPath(value, existing = true) {
  const target = path.resolve(value), relative = path.relative(root, target);
  assert.ok(relative && !relative.startsWith("..") && !path.isAbsolute(relative), "Paths must remain in the project");
  let ancestor = target;
  if (!existing) while (true) {
    try { await fs.stat(ancestor); break; } catch (error) { if (error.code !== "ENOENT") throw error; ancestor = path.dirname(ancestor); }
  }
  const real = await fs.realpath(ancestor), realRelative = path.relative(root, real);
  assert.ok(!realRelative.startsWith("..") && !path.isAbsolute(realRelative), "Symlink escape outside project");
  return target;
}

async function main() {
  const options = new Map();
  for (let i = 2; i < process.argv.length; i += 2) {
    const key = process.argv[i], value = process.argv[i + 1];
    assert.ok(["--site", "--settings", "--output", "--base"].includes(key) && value && !options.has(key), "Invalid capture arguments");
    options.set(key, value);
  }
  const site = await projectPath(options.get("--site")), settingsPath = await projectPath(options.get("--settings"));
  const output = await projectPath(options.get("--output"), false), base = options.get("--base") || "/";
  assert.ok(/^\/(?:[a-zA-Z0-9_-]+\/)*$/.test(base), "Invalid local base path");
  const settings = validateSettings(JSON.parse((await fs.readFile(settingsPath, "utf8")).replace(/^\uFEFF/, "")));
  const tmp = path.join(root, "Benchmarks.Local/tmp");
  assert.ok(output.startsWith(tmp + path.sep), "Capture must be staged in ignored project output");
  await fs.mkdir(tmp, { recursive: true }); await fs.mkdir(path.dirname(output), { recursive: true });
  process.env.TEMP = process.env.TMP = process.env.TMPDIR = tmp;
  const { chromium } = await import("playwright");
  const servedFiles = new Map(), errors = [];
  const server = http.createServer(async (request, response) => {
    try {
      const pathname = decodeURIComponent(new URL(request.url, "http://local").pathname);
      if (!pathname.startsWith(base)) { response.writeHead(404).end(); return; }
      const name = pathname.slice(base.length) || "index.html", file = path.resolve(site, name);
      if (!file.startsWith(site + path.sep) || !(await fs.realpath(file)).startsWith(site + path.sep)) { response.writeHead(403).end(); return; }
      const bytes = await fs.readFile(file);
      const sha = sha256(bytes);
      assert.ok(!servedFiles.has(name) || servedFiles.get(name) === sha, "Served build changed during capture");
      servedFiles.set(name, sha);
      response.writeHead(200, { "Content-Type": mime[path.extname(file)] || "application/octet-stream", "Cache-Control": "no-store" }).end(bytes);
    } catch (error) { response.writeHead(404).end(String(error)); }
  });
  let browser, page;
  try {
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
    browser = await chromium.launch({ headless: true });
    page = await browser.newPage({ viewport: { width: settings.Width, height: settings.Height }, deviceScaleFactor: 1 });
    page.on("pageerror", error => errors.push(error.message));
    page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
    page.on("response", response => { if (response.status() >= 400) errors.push(`${response.status()} ${response.url()}`); });
    const origin = `http://127.0.0.1:${server.address().port}`;
    await page.route("**/*", async route => {
      const url = new URL(route.request().url());
      if (["http:", "https:"].includes(url.protocol) && url.origin !== origin) {
        errors.push("External capture resource: " + url.href); await route.abort();
      } else await route.continue();
    });
    await page.goto(`${origin}${base}?test=1&lang=en`, { waitUntil: "networkidle", timeout: 120000 });
    await page.waitForFunction(() => window.spaceBattle?.ready || !document.querySelector("#retry").hidden, null, { timeout: 120000 });
    assert.ok(await page.evaluate(() => window.spaceBattle?.ready), await page.locator("#load-detail").textContent());
    await page.selectOption("#profile", "nominal"); await page.fill("#seed", String(settings.Seed)); await page.click("#reset");
    const initial = await page.evaluate(() => JSON.parse(window.render_game_to_text()));
    assert.equal(initial.tick, 0); assert.equal(initial.paused, true); assert.equal(initial.counts.ships, 2 * (settings.ShipsPerTeam + 3));
    await page.click("#zoom-in"); await page.click("#fit");
    assert.equal(await page.evaluate(() => JSON.parse(window.render_game_to_text()).checksum), initial.checksum, "Fit/zoom cannot advance or alter simulation");
    await page.evaluate(async s => {
      window.spaceBattle.camera.zoom = s.Zoom;
      window.spaceBattle.select(null);
      await window.advanceTime(s.Tick * 1000 / 60);
    }, settings);
    // Hide DOM overlays without resizing Canvas or changing its camera fit area.
    await page.addStyleTag({ content: "#battle > :not(canvas) { visibility:hidden!important; }" });
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    const state = await page.evaluate(() => ({ ...JSON.parse(window.render_game_to_text()), aliveTeams: window.spaceBattle.aliveTeamCounts() }));
    const ships = await page.evaluate(() => window.spaceBattle.snapshot.ships.map(row => Array.from(row)));
    assert.equal(await page.evaluate(() => window.spaceBattle.validate()), state.checksum, "C# invariants must pass");
    assert.equal(await page.evaluate(() => devicePixelRatio), 1);
    const png = await page.locator("#scene").screenshot();
    assert.equal(await page.evaluate(() => window.spaceBattle.snapshot.tick), settings.Tick, "Screenshot cannot advance paused battle");
    assert.deepEqual(errors, [], "Browser errors must not be hidden");
    const capture = { ...captureMetadata(settings, state, ships, browser.version(), Object.fromEntries([...servedFiles].sort())),
      ...pixelEvidence(png, settings), Statistics: state.statistics, ControlChecks: ["reset", "fit", "zoom", "paused fixed-tick capture"] };
    await fs.writeFile(output, png);
    await fs.writeFile(output + ".json", JSON.stringify(capture, null, 2) + "\n");
    console.log(`PASS: Chromium ${browser.version()}, AOT tick ${settings.Tick}, Canvas ${settings.Width}x${settings.Height}, ${capture.RenderedShips} on-screen ships`);
  } catch (error) {
    await fs.writeFile(output + ".failure.json", JSON.stringify({ error: String(error), stack: error.stack, errors }, null, 2));
    throw error;
  } finally {
    await browser?.close();
    await new Promise(resolve => server.close(resolve));
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error); process.exitCode = 1; });
