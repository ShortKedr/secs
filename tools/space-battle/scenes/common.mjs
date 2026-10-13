import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import crypto from "node:crypto";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

export const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
process.env.PLAYWRIGHT_BROWSERS_PATH ||= path.join(projectRoot, 'Benchmarks.Local/tmp/playwright-browsers');
export const dependencies = createRequire(path.join(projectRoot, "tools/web/package.json"));
export const sha256 = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
export const inside = (root, target) => {
  const relative = path.relative(root, target);
  return relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative);
};

export async function projectPath(value, { output = false, ignored = false } = {}) {
  assert.ok(typeof value === "string" && value.trim(), "A project path is required");
  const target = path.resolve(value);
  assert.ok(inside(projectRoot, target) && target !== projectRoot, "Path must remain inside this project");
  if (ignored) assert.ok(inside(path.join(projectRoot, "Benchmarks.Local/tmp"), target) &&
    target !== path.join(projectRoot, "Benchmarks.Local/tmp"), "Output must be below Benchmarks.Local/tmp");
  let ancestor = target;
  for (;;) {
    try { await fs.lstat(ancestor); break; }
    catch (error) { if (!output || error.code !== "ENOENT") throw error; ancestor = path.dirname(ancestor); }
  }
  const realRoot = await fs.realpath(projectRoot), realAncestor = await fs.realpath(ancestor);
  assert.ok(inside(realRoot, realAncestor), "Symlink escape outside project");
  if (ignored) {
    const ignoredRoot = path.join(projectRoot, 'Benchmarks.Local/tmp');
    if (inside(ignoredRoot, ancestor))
      assert.ok(inside(path.join(realRoot, 'Benchmarks.Local/tmp'), realAncestor), 'Symlink escape outside ignored output');
    else assert.equal(realAncestor.toLowerCase(), path.resolve(realRoot, path.relative(projectRoot, ancestor)).toLowerCase(), 'Aliased output ancestor');
  }
  return target;
}

export async function freshOutputDirectory(value) {
  const target = await projectPath(value, { output: true, ignored: true });
  await fs.mkdir(path.dirname(target), { recursive: true });
  await fs.mkdir(target); // Exclusive: never mix a new run with a completed or interrupted one.
  return target;
}
export async function readJson(value) {
  return JSON.parse((await fs.readFile(await projectPath(value), "utf8")).replace(/^\uFEFF/, ""));
}
export async function writeJson(file, data) {
  await fs.writeFile(await projectPath(file, { output: true, ignored: true }), JSON.stringify(data, null, 2) + "\n", { flag: "wx" });
}

export async function filesBelow(directory) {
  const result = [];
  async function walk(folder) {
    for (const entry of (await fs.readdir(folder, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name, "en"))) {
      assert.ok(!entry.isSymbolicLink(), "Source/site symlinks are not accepted: " + entry.name);
      const file = path.join(folder, entry.name);
      if (entry.isDirectory()) {
        if (!['bin', 'obj', 'node_modules', '.git'].includes(entry.name)) await walk(file);
      } else if (entry.isFile()) result.push(file);
    }
  }
  await walk(directory);
  return result;
}

export async function hashSite(value) {
  const site = await projectPath(value), files = {};
  for (const file of await filesBelow(site)) files[path.relative(site, file).split(path.sep).join("/")] = sha256(await fs.readFile(file));
  for (const name of ["index.html", "build-identity.json", "_framework/dotnet.native.wasm", "_framework/blazor.boot.json"])
    assert.ok(files[name], "A finalized production AOT site is required: missing " + name);
  return { path: site, fingerprint: sha256(JSON.stringify(files)), files };
}

const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.wasm': 'application/wasm', '.png': 'image/png', '.svg': 'image/svg+xml', '.txt': 'text/plain', '.md': 'text/plain' };

export async function openSite(value, { width = 1920, height = 1188 } = {}) {
  const identity = await hashSite(value), site = identity.path, errors = [];
  const html = await fs.readFile(path.join(site, "index.html"), "utf8");
  const base = html.match(/<base href="([^\"]+)"/)?.[1];
  assert.ok(/^\/(?:[a-zA-Z0-9_-]+\/)*$/.test(base), "Unsupported local base URL");
  const tmp = path.join(projectRoot, "Benchmarks.Local/tmp");
  await fs.mkdir(tmp, { recursive: true });
  process.env.TEMP = process.env.TMP = process.env.TMPDIR = tmp;
  const server = http.createServer(async (request, response) => {
    try {
      const pathname = decodeURIComponent(new URL(request.url, 'http://local').pathname);
      if (pathname === '/favicon.ico') { response.writeHead(204).end(); return; }
      assert.ok(pathname.startsWith(base), "Wrong base path");
      const name = pathname.slice(base.length) || 'index.html', file = path.resolve(site, name);
      assert.ok(inside(site, file) && file !== site && inside(site, await fs.realpath(file)), "Unsafe resource path");
      const bytes = await fs.readFile(file);
      assert.equal(sha256(bytes), identity.files[name], "Site changed during playback: " + name);
      response.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' }).end(bytes);
    } catch (error) { errors.push(String(error)); response.writeHead(404).end(); }
  });
  let browser, closing;
  const close = () => closing ||= (async () => {
    try { await browser?.close(); }
    finally { if (server.listening) await new Promise(resolve => server.close(resolve)); }
  })();
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
    const { chromium } = dependencies('playwright');
    browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
    page.on('pageerror', error => errors.push(String(error)));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    page.on('response', response => { if (response.status() >= 400) errors.push(`${response.status()} ${response.url()}`); });
    const origin = `http://127.0.0.1:${server.address().port}`;
    await page.route('**/*', async route => {
      const address = new URL(route.request().url());
      if (['http:', 'https:'].includes(address.protocol) && address.origin !== origin) {
        errors.push('External resource blocked: ' + address.href); await route.abort();
      } else await route.continue();
    });
    await page.goto(`${origin}${base}?test=1&lang=en`, { waitUntil: 'networkidle', timeout: 120000 });
    await page.waitForFunction(() => window.spaceBattle?.ready || !document.querySelector('#retry').hidden, null, { timeout: 120000 });
    assert.ok(await page.evaluate(() => window.spaceBattle?.ready), await page.locator('#load-detail').textContent());
    assert.equal(await page.evaluate(() => window.spaceBattle.runtimeMode), 'aot-binary', 'Mono AOT is required');
    await page.evaluate(async () => {
      window.__scenesBridge = (await getDotnetRuntime(0).getAssemblyExports('SpaceBattle.Web.dll')).SpaceBattle.Web.BattleBridge;
    });
    return { browser, page, errors, identity, close };
  } catch (error) { await close(); throw error; }
}

export async function checkpoint(page) {
  return page.evaluate(() => {
    const game = window.spaceBattle, state = JSON.parse(window.render_game_to_text());
    if (!state.ready || !state.paused || state.runtimeMode !== 'aot-binary' || state.diagnostics.error)
      throw new Error('Replay is not a healthy paused production AOT battle');
    return { tick: state.tick, checksum: state.checksum, counts: state.counts, counters: state.counters, teams: game.aliveTeamCounts() };
  });
}

export async function resetBattle(page, profile, seed) {
  assert.ok(['low', 'nominal', 'high'].includes(profile));
  assert.ok(Number.isInteger(seed) && seed >= 0 && seed <= 0xFFFFFFFF);
  await page.evaluate(({ profile, seed }) => {
    const game = window.spaceBattle;
    document.querySelector('#profile').value = profile; document.querySelector('#seed').value = String(seed);
    game.reset(); game.pause(true); game.select(null); game.showColliders(false);
  }, { profile, seed });
  const state = await checkpoint(page); assert.equal(state.tick, 0);
  return state;
}

export async function advanceTo(page, tick) {
  assert.ok(Number.isInteger(tick) && tick >= 0 && tick <= 18000, "Invalid replay tick");
  await page.evaluate(tick => {
    const game = window.spaceBattle;
    if (!JSON.parse(window.render_game_to_text()).paused || tick < game.snapshot.tick) throw new Error('Replay must be paused and forward-only');
    while (game.snapshot.tick < tick) {
      window.__scenesBridge.Advance(Math.min(600, tick - game.snapshot.tick)); game.refresh();
    }
  }, tick);
}

export function parseOptions(argv, allowed) {
  const result = {};
  for (let i = 0; i < argv.length; i += 2) {
    const key = argv[i], value = argv[i + 1];
    assert.ok(key.startsWith('--') && allowed.includes(key.slice(2)) && value && !Object.hasOwn(result, key.slice(2)), 'Unknown, duplicate or missing option: ' + key);
    result[key.slice(2)] = value;
  }
  return result;
}
