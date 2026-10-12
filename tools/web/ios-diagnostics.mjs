import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";

export const MODES = Object.freeze(["simulation", "render", "minimal", "normal"]);
export const LIMITS = Object.freeze({ ticks: 120, repeats: 3, pageMs: 30000, totalMs: 300000 });
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

export function localUrl(value) {
  const url = new URL(value);
  if (!["http:", "https:"].includes(url.protocol) ||
      !["localhost", "127.0.0.1", "[::1]"].includes(url.hostname) || url.username || url.password) {
    throw new Error("Only credential-free loopback HTTP(S) URLs are allowed");
  }
  return url.href;
}

export function parseModes(value = MODES.join(",")) {
  const modes = value.split(",");
  if (!modes.length || new Set(modes).size !== modes.length || modes.some(mode => !MODES.includes(mode))) {
    throw new Error("Modes must be unique simulation,render,minimal,normal entries");
  }
  return modes;
}

export function parseArgs(args) {
  const values = {};
  const options = new Set(["url", "baseline", "output", "browser", "modes", "ticks", "repeats", "retry"]);
  for (let i = 0; i < args.length; i++) {
    const key = args[i].replace(/^--/, "");
    if (args[i] !== `--${key}` || Object.hasOwn(values, key)) throw new Error("Unknown or duplicate option");
    if (key === "faults") { values[key] = true; continue; }
    if (!options.has(key) || !args[i + 1] || args[i + 1].startsWith("--")) throw new Error(`Invalid --${key}`);
    values[key] = args[++i];
  }
  const integer = (key, fallback, max) => {
    const raw = values[key] ?? String(fallback);
    if (!/^\d+$/.test(raw) || Number(raw) < 1 || Number(raw) > max) throw new Error(`--${key} must be 1..${max}`);
    return Number(raw);
  };
  if (!values.url || !values.output) throw new Error("Required: --url LOCAL_URL --output NEW_IGNORED_JSON");
  const browser = values.browser ?? "chromium";
  if (!["chromium", "webkit"].includes(browser)) throw new Error("--browser must be chromium or webkit");
  if (values.retry && !["button", "reload"].includes(values.retry)) throw new Error("--retry must be button or reload");
  return { url: localUrl(values.url), baseline: values.baseline ? localUrl(values.baseline) : null,
    output: path.resolve(values.output), browser, modes: parseModes(values.modes),
    ticks: integer("ticks", 60, LIMITS.ticks), repeats: integer("repeats", 3, LIMITS.repeats),
    faults: Boolean(values.faults), retry: "reload" };
}

export function windowSizes(ticks) {
  assert.ok(Number.isInteger(ticks) && ticks >= 1 && ticks <= LIMITS.ticks);
  return [1, Math.min(24, ticks), ticks];
}

export function diagnosticUrl(url, mode) {
  assert.ok(MODES.includes(mode));
  const result = new URL(localUrl(url));
  result.searchParams.set("diagnostics", mode);
  result.searchParams.set("test", "1");
  return result.href;
}

export function stats(values) {
  assert.ok(values.length > 0 && values.every(v => Number.isFinite(v) && v >= 0));
  const sorted = [...values].sort((a, b) => a - b);
  return { n: values.length, mean: values.reduce((a, b) => a + b, 0) / values.length,
    median: (sorted[Math.floor((sorted.length - 1) / 2)] + sorted[Math.floor(sorted.length / 2)]) / 2,
    p95: sorted[Math.ceil(sorted.length * 0.95) - 1], max: sorted.at(-1) };
}

export function sampleRequest(mode, ticks = 1, renders = 1) {
  assert.ok(MODES.includes(mode));
  assert.ok(Number.isInteger(ticks) && ticks >= 1 && ticks <= LIMITS.ticks);
  assert.ok(Number.isInteger(renders) && renders >= 0 && renders <= 1);
  return { mode, ticks, renders };
}

export function scenePreparation(mode) {
  assert.ok(MODES.includes(mode));
  return mode === "render" || mode === "minimal" ? sampleRequest("simulation", 24, 0) : null;
}

export function validateSample(sample, mode, before, ticks = 1) {
  sampleRequest(mode, ticks);
  assert.equal(sample.mode, mode);
  for (const key of ["stepMs", "exportMs", "renderMs"]) assert.ok(Number.isFinite(sample[key]) && sample[key] >= 0, key);
  assert.ok(Number.isInteger(sample.tick) && sample.tick >= 0);
  assert.ok(typeof sample.checksum === "string" || typeof sample.checksum === "number");
  assert.ok(sample.counts && sample.counters && sample.memory);
  for (const key of ["wasmLinearBytes", "managedBytes", "jsHeapBytes", "canvasBackingBytes", "atlasDecodedBytes"]) {
    assert.ok(Object.hasOwn(sample.memory, key), `Missing memory.${key}`);
    assert.ok(sample.memory[key] === null || (Number.isFinite(sample.memory[key]) && sample.memory[key] >= 0), key);
  }
  if (mode === "render" || mode === "minimal") {
    assert.equal(sample.tick, before.tick, "Frozen render advanced tick");
    assert.equal(sample.checksum, before.checksum, "Frozen render changed checksum");
    assert.equal(sample.stepMs, 0, "Frozen render performed Step");
    assert.deepEqual(sample.counts, before.counts, "Frozen render changed counts");
    assert.deepEqual(sample.counters, before.counters, "Frozen render changed counters");
  } else assert.equal(sample.tick, before.tick + ticks, "Controlled sample must advance exactly the requested ticks");
  if (mode === "simulation") assert.equal(sample.renderMs, 0, "Simulation performed a draw");
  return sample;
}

export function summarizeRun(samples) {
  return Object.fromEntries(["stepMs", "exportMs", "renderMs"].map(key => [key, stats(samples.map(s => s[key]))]));
}

export function summarizePages(runs) {
  // Only independent page means are aggregated; ticks/windows are never independent runs.
  const groups = new Map();
  for (const run of runs) for (let i = 0; i < run.windows.length; i++) {
    const key = `${run.label}:${run.mode}:${i}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push({ run, window: run.windows[i] });
  }
  return [...groups.values()].map(rows => ({ label: rows[0].run.label, mode: rows[0].run.mode,
    windowIndex: rows[0].window.index, calls: rows[0].window.calls, independentPages: rows.length,
    pageMeanMs: Object.fromEntries(["stepMs", "exportMs", "renderMs"].map(key =>
      [key, stats(rows.map(row => row.window.timings[key].mean))])) }));
}

export function validateOutputPath(output, root = ROOT) {
  const relative = path.relative(root, output);
  if (!relative || relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative) || path.extname(output) !== ".json") {
    throw new Error("Output must be a new project-local .json file");
  }
  return relative;
}

export async function validateOutput(output, root = ROOT) {
  const relative = validateOutputPath(output, root);
  try { await fs.lstat(output); throw new Error("Output already exists"); }
  catch (error) { if (error.code !== "ENOENT") throw error; }
  let ancestor = path.dirname(output);
  for (;;) {
    try {
      const real = await fs.realpath(ancestor), realRoot = await fs.realpath(root);
      const inside = path.relative(realRoot, real);
      if (inside.startsWith(`..${path.sep}`) || path.isAbsolute(inside)) throw new Error("Output parent escapes project through a link");
      break;
    } catch (error) { if (error.code !== "ENOENT") throw error; ancestor = path.dirname(ancestor); }
  }
  try { execFileSync("git", ["check-ignore", "--quiet", "--no-index", "--", relative], { cwd: root, stdio: "pipe" }); }
  catch { throw new Error("Output must be covered by existing project ignore rules (e.g. Benchmarks.Local/tmp/...)"); }
}

async function state(page) {
  return page.evaluate(() => JSON.parse(window.render_game_to_text()));
}

async function reset(page) {
  await page.evaluate(async () => {
    document.getElementById("profile").value = "nominal";
    document.getElementById("seed").value = "7331";
    await window.spaceBattle.reset();
    window.spaceBattle.pause(true);
  });
  const value = await state(page);
  assert.equal(value.tick, 0);
  assert.equal(value.seed, 7331);
  assert.equal(value.profile, "nominal");
  assert.equal(value.paused, true);
  return value;
}

async function sample(page, mode, ticks = 1, renders = 1) {
  return page.evaluate(request => window.spaceBattle.diagnosticSample(request), sampleRequest(mode, ticks, renders));
}

async function noLoop(page) {
  const before = await page.evaluate(() => ({ ...window.__iosRaf }));
  await page.waitForTimeout(100);
  assert.deepEqual(await page.evaluate(() => ({ ...window.__iosRaf })), before, "Diagnostic page has an ongoing RAF loop");
}

export function warmupMode(mode) {
  assert.ok(MODES.includes(mode));
  return mode === "simulation" || mode === "minimal" ? "simulation" : "normal";
}

export function validateAssets(assets, mode) {
  assert.ok(MODES.includes(mode));
  const imageLess = mode === "simulation" || mode === "minimal";
  assert.equal(assets.sourceType, "image");
  assert.equal(assets.imageCount, imageLess ? 0 : 6);
  assert.equal(assets.canvasCount, 0);
  const currentAtlasBytes = 4 * (2 * 1448 * 1086 + 2 * 1024 * 1536 + 2 * 1254 * 1254);
  assert.equal(assets.decodedBytes, imageLess ? 0 : currentAtlasBytes);
  assert.ok(Number.isInteger(assets.loads) && assets.loads >= assets.imageCount);
  if (imageLess) assert.equal(assets.loads, 0);
  assert.ok(Object.hasOwn(assets, "revision"));
  return assets;
}

async function assetCheck(page, mode) {
  const read = () => page.evaluate(() => window.spaceBattle.assetDiagnostics());
  const before = validateAssets(await read(), mode);
  for (let i = 0; i < 2; i++) {
    await reset(page);
    assert.deepEqual(await read(), before, "Reset changed image resources/load count");
  }
  let resize = { status: "unsupported" };
  if (await page.evaluate(() => typeof window.spaceBattle.resize === "function")) {
    const beforeState = await state(page);
    assert.ok(Number.isInteger(beforeState.diagnostics?.canvasResizes), "resize requires diagnostics.canvasResizes");
    await page.evaluate(() => {
      window.spaceBattle.resize();
      window.dispatchEvent(new Event("resize"));
    });
    await page.waitForTimeout(100);
    const after = await state(page);
    assert.equal(after.diagnostics.canvasResizes, beforeState.diagnostics.canvasResizes, "Same viewport resized backing canvas");
    resize = { status: "passed", canvasResizes: after.diagnostics.canvasResizes };
  }
  return { before, resets: 2, resize, evidence: "Counts/loads/revision only; no direct Image identity or GPU allocation proof" };
}

async function openPage(browser, url, mode, pages) {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, serviceWorkers: "block" });
  pages.add(page);
  // Playwright's default timeout does not bound evaluate() or a hung async app hook.
  const deadline = setTimeout(() => { void page.close().catch(() => {}); }, LIMITS.pageMs);
  page.once("close", () => clearTimeout(deadline));
  page.setDefaultTimeout(LIMITS.pageMs);
  const errors = [];
  page.on("pageerror", error => errors.push(String(error)));
  await page.route("**/*", route => {
    const request = new URL(route.request().url());
    return request.origin === new URL(url).origin || ["data:", "blob:"].includes(request.protocol)
      ? route.continue() : route.abort("blockedbyclient");
  });
  await page.addInitScript(() => {
    window.__iosRaf = { requested: 0, executed: 0 };
    const original = window.requestAnimationFrame.bind(window);
    window.requestAnimationFrame = callback => {
      window.__iosRaf.requested++;
      return original(time => { window.__iosRaf.executed++; callback(time); });
    };
    window.__iosImageDraws = 0;
    const drawImage = CanvasRenderingContext2D.prototype.drawImage;
    CanvasRenderingContext2D.prototype.drawImage = function (...args) {
      window.__iosImageDraws++;
      return drawImage.apply(this, args);
    };
  });
  await page.goto(diagnosticUrl(url, mode), { waitUntil: "domcontentloaded", timeout: LIMITS.pageMs });
  await page.waitForFunction(() => window.spaceBattle?.ready, null, { polling: 100, timeout: LIMITS.pageMs });
  assert.ok(await page.evaluate(() => typeof window.spaceBattle.diagnosticSample === "function" &&
    typeof window.spaceBattle.assetDiagnostics === "function"), "Main diagnostic hooks missing");
  assert.equal((await state(page)).paused, true, "Startup must already be paused");
  await noLoop(page);
  return { page, errors };
}

async function measurePage(browser, url, label, mode, repeat, options, pages) {
  const { page, errors } = await openPage(browser, url, mode, pages);
  try {
    const warmInitial = await reset(page);
    const warmMode = warmupMode(mode);
    const warm = validateSample(await sample(page, warmMode), warmMode, warmInitial);
    // Warm a separate battle, then discard its state before all measured windows.
    const assets = await assetCheck(page, mode), windows = [];
    for (const [index, calls] of windowSizes(options.ticks).entries()) {
      const resetState = await reset(page), samples = [];
      const preparationRequest = scenePreparation(mode);
      let preparation = null;
      if (preparationRequest) {
        preparation = validateSample(await sample(page, preparationRequest.mode, preparationRequest.ticks,
          preparationRequest.renders), "simulation", resetState, 24);
      }
      const initial = preparation ? await state(page) : resetState;
      if (preparation) {
        assert.equal(initial.tick, 24);
        assert.equal(initial.checksum, preparation.checksum);
        assert.equal(initial.paused, true);
      }
      const imageDraws = await page.evaluate(() => window.__iosImageDraws);
      let previous = initial;
      for (let i = 0; i < calls; i++) {
        const next = validateSample(await sample(page, mode), mode, previous);
        samples.push(next); previous = next;
      }
      const final = await state(page);
      assert.equal(final.tick, previous.tick);
      assert.equal(final.checksum, previous.checksum);
      assert.equal(final.paused, true);
      const imageDrawDelta = await page.evaluate(() => window.__iosImageDraws) - imageDraws;
      if (mode === "simulation" || mode === "minimal") assert.equal(imageDrawDelta, 0, "Image draw in image-less mode");
      if (mode === "normal" || mode === "render") assert.ok(imageDrawDelta > 0, "Image render path was not exercised");
      windows.push({ index, calls, initial, preparation, final, imageDrawDelta, samples, timings: summarizeRun(samples) });
    }
    await noLoop(page);
    assert.deepEqual(errors, [], "Unexpected page errors");
    return { label, mode, repeat, warm, assets, windows };
  } finally { await page.close(); pages.delete(page); }
}

async function faultPage(browser, url, kind, retry, pages) {
  const { page, errors } = await openPage(browser, url, "normal", pages);
  try {
    await reset(page);
    if (kind === "drawImage") {
      await page.evaluate(() => {
        const original = CanvasRenderingContext2D.prototype.drawImage;
        window.__iosFaultHits = 0;
        CanvasRenderingContext2D.prototype.drawImage = function (...args) {
          CanvasRenderingContext2D.prototype.drawImage = original;
          window.__iosFaultHits++;
          throw new Error("ios-diagnostics injected drawImage failure");
        };
      });
      await page.evaluate(async () => {
        try { await window.spaceBattle.diagnosticSample({ mode: "normal", ticks: 1, renders: 1 }); }
        catch (error) { window.__iosSampleRejection = String(error); }
      });
      assert.equal(await page.evaluate(() => window.__iosFaultHits), 1, "drawImage injection not exercised");
    } else {
      await page.evaluate(() => document.querySelector("#scene").dispatchEvent(new Event("contextlost", { cancelable: true })));
    }
    await page.waitForFunction(() => Boolean(JSON.parse(window.render_game_to_text()).diagnostics?.error), null,
      { polling: 100, timeout: 3000 });
    const failed = await state(page);
    assert.equal(typeof failed.diagnostics.error.stage, "string");
    assert.equal(typeof failed.diagnostics.error.message, "string");
    assert.equal(failed.paused, true);
    assert.equal(await page.locator("#load-state").isVisible(), true);
    assert.equal(await page.locator("#retry").isVisible(), true);
    await noLoop(page);
    const after = await state(page);
    assert.equal(after.tick, failed.tick, "Faulted battle silently kept advancing");
    assert.deepEqual(after.diagnostics.error, failed.diagnostics.error);
    // Fatal retry always reloads to avoid creating a second Mono runtime in this page.
    await Promise.all([page.waitForEvent("domcontentloaded"), page.locator("#retry").click()]);
    await page.waitForFunction(() => window.spaceBattle?.ready &&
      !JSON.parse(window.render_game_to_text()).diagnostics?.error, null, { polling: 100 });
    assert.equal(await page.locator("#load-state").isVisible(), false);
    const recoveredAssets = await assetCheck(page, "normal");
    const before = await reset(page);
    const recovered = validateSample(await sample(page, "normal"), "normal", before);
    await noLoop(page);
    assert.ok(errors.every(error => kind === "drawImage" && error.includes("ios-diagnostics injected drawImage failure")),
      `Unexpected recovery page errors: ${errors.join("; ")}`);
    return { kind, retry, status: "passed", failed, recovered, recoveredAssets, pageErrors: errors };
  } finally { await page.close(); pages.delete(page); }
}

export async function run(options) {
  await validateOutput(options.output);
  await fs.mkdir(path.dirname(options.output), { recursive: true });
  const output = await fs.open(options.output, "wx");
  const report = { schema: 1, browser: options.browser, options,
    scope: "Bounded desktop browser regression diagnostics, NOT real iOS, jetsam, GPU busy time or live FPS. renderMs is JS Canvas submission time; memory categories are separate, not additive. GPU memory is unknown.",
    gpuMemoryBytes: null, warmup: "One simulation sample for simulation/minimal pages, one normal sample for normal/render pages; separate battle discarded by seed7331 nominal reset",
    frozenScene: "Render/minimal windows each reset, then prepare simulation ticks:24/renders:0; draw-only measurements freeze tick24",
    runs: [], faults: [], status: "running" };
  let browser, timer;
  const pages = new Set();
  try {
    const playwright = await import("playwright");
    browser = await playwright[options.browser].launch({ headless: true, timeout: LIMITS.pageMs });
    const work = async () => {
      const sites = options.baseline ? [["baseline", options.baseline], ["current", options.url]] : [["current", options.url]];
      for (let repeat = 1; repeat <= options.repeats; repeat++) {
        for (const [label, url] of repeat % 2 ? sites : [...sites].reverse()) {
          for (const mode of options.modes) report.runs.push(await measurePage(browser, url, label, mode, repeat, options, pages));
        }
      }
      if (options.faults) for (const kind of ["drawImage", "contextlost"]) {
        report.faults.push(await faultPage(browser, options.url, kind, options.retry, pages));
      }
      report.summary = summarizePages(report.runs);
      report.comparison = compareStates(report.runs);
      report.status = "passed";
    };
    await Promise.race([work(), new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error("Bounded diagnostic deadline exceeded")), LIMITS.totalMs);
    })]);
  } catch (error) { report.status = "failed"; report.error = error.stack ?? String(error); }
  finally {
    clearTimeout(timer);
    for (const page of pages) await page.close().catch(() => {});
    await browser?.close();
    await output.writeFile(JSON.stringify(report, null, 2));
    await output.close();
  }
  return report;
}

export function compareStates(runs) {
  const seen = new Map();
  let comparisons = 0;
  for (const run of runs) for (const window of run.windows) {
    const advancing = run.mode === "simulation" || run.mode === "normal";
    const key = advancing ? `step:${window.index}` : "frozen:24";
    if (!advancing) {
      assert.equal(window.initial.tick, 24);
      assert.equal(window.final.tick, 24);
      assert.equal(window.final.checksum, window.initial.checksum);
    }
    const value = { tick: window.final.tick, checksum: window.final.checksum,
      counts: window.final.counts, counters: window.final.counters };
    if (seen.has(key)) { assert.deepEqual(value, seen.get(key), `State parity failed: ${run.label}/${run.mode}/${run.repeat}`); comparisons++; }
    else seen.set(key, value);
  }
  return { comparisons, status: "passed", scope: "Final tick/checksum/counts/counters across fresh pages: advancing windows match by index, all frozen windows match tick24; no older-app fallback" };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const report = await run(parseArgs(process.argv.slice(2)));
    console.log(JSON.stringify({ status: report.status, output: report.options.output, error: report.error ?? null }));
    if (report.status !== "passed") process.exitCode = 1;
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
