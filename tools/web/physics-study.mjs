import fs from "node:fs/promises";
import path from "node:path";
import { createHash, randomUUID } from "node:crypto";
import { fileURLToPath } from "node:url";

const argv = process.argv.slice(2);
const value = (name, fallback) => {
  const index = argv.indexOf(name);
  if (index < 0) return fallback;
  if (!argv[index + 1]) throw new Error(`Missing value for ${name}`);
  return argv[index + 1];
};
const integer = (name, fallback, minimum) => {
  const result = Number(value(name, String(fallback)));
  if (!Number.isSafeInteger(result) || result < minimum) throw new Error(`Invalid ${name}`);
  return result;
};
function localUrl(input) {
  const url = new URL(input);
  if (url.protocol !== "http:" || !["127.0.0.1", "localhost", "[::1]"].includes(url.hostname) || url.username || url.password)
    throw new Error("Only local loopback HTTP endpoints are allowed");
  url.searchParams.set("test", "1");
  return url.href;
}
const assetPath = url => decodeURIComponent(new URL(url).pathname);
const trackedAsset = url => /\.(?:wasm|dll|json|m?js)(?:\.(?:br|gz))?$/i.test(assetPath(url));
const nativeWasm = url => /^dotnet\.native(?:\.[^.]+)*\.wasm(?:\.(?:br|gz))?$/i.test(path.posix.basename(assetPath(url)));
const config = {
  before: localUrl(value("--before", "http://127.0.0.1:8049/")),
  after: localUrl(value("--after", "http://127.0.0.1:8050/")),
  repeats: integer("--repeats", 3, 3), ticks: integer("--ticks", 600, 1),
  warmup: integer("--warmup-ticks", 120, 1), seed: integer("--seed", 7331, 0),
  profiles: value("--profiles", "low,nominal,high").split(","),
};
if (config.seed > 0xffffffff || config.before === config.after ||
    config.profiles.some(p => !["low", "nominal", "high"].includes(p)) ||
    new Set(config.profiles).size !== config.profiles.length) throw new Error("Invalid comparison configuration");
function plan() {
  const trials = [];
  for (const profile of config.profiles)
    for (let repeat = 0; repeat < config.repeats; repeat++) {
      for (const role of repeat % 2 ? ["after", "before"] : ["before", "after"])
        trials.push({ role, url: config[role], profile, repeat, collectTimings: false });
      trials.push({ role: "after", url: config.after, profile, repeat, collectTimings: true });
    }
  return trials;
}
if (argv.includes("--help") || !argv.length) {
  console.log("node tools/web/physics-study.mjs --verify | --validate-assets-live | --approved-timing-window [--before http://127.0.0.1:8049/ --after http://127.0.0.1:8050/ --profiles low,nominal,high --repeats 3 --ticks 600 --warmup-ticks 120 --seed 7331 --run NAME]");
  console.log("Main must complete both production AOT builds and explicitly open the timing window first. No servers or builds are started here.");
} else if (argv.includes("--verify")) {
  for (const profile of config.profiles) {
    const trials = plan().filter(p => p.profile === profile);
    for (const role of ["before", "after"])
      if (trials.filter(t => t.role === role && !t.collectTimings).length !== config.repeats) throw new Error("Repeat plan failed");
    if (trials.filter(t => t.collectTimings).length !== config.repeats) throw new Error("Instrumented plan failed");
  }
  let rejected = false;
  try { localUrl("https://example.com/"); } catch { rejected = true; }
  if (!rejected) throw new Error("Local-only URL guard failed");
  for (const suffix of ["dotnet.native.wasm", "dotnet.native.8.0.31.hash.wasm?v=1", "dotnet.native.hash.wasm.br"])
    if (!trackedAsset(`http://localhost/_framework/${suffix}`) || !nativeWasm(`http://localhost/_framework/${suffix}`))
      throw new Error("Native WASM URL classification failed");
  if (nativeWasm("http://localhost/_framework/SpaceBattle.Web.wasm")) throw new Error("Assembly mistaken for native runtime");
  console.log("Local URL guards, repeat/mode plan and configuration passed. No browser, server, output or timing run started.");
} else if (argv.includes("--validate-assets-live")) {
  if (argv.includes("--approved-timing-window")) throw new Error("Choose either capture-only validation or timing, not both");
  await measure(true);
} else {
  if (!argv.includes("--approved-timing-window")) throw new Error("Timing window must be explicitly approved by main first");
  await measure();
}

async function measure(validationOnly = false) {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
  const run = value("--run", `web-${validationOnly ? "capture-" : ""}${new Date().toISOString().replace(/[^0-9TZ]/g, "")}-${randomUUID().slice(0, 8)}`);
  if (!/^[a-zA-Z0-9_-]+$/.test(run)) throw new Error("Run must be a simple ASCII identifier");
  const output = path.join(root, "Benchmarks.Local/tmp/physics-study", run);
  await fs.mkdir(path.dirname(output), { recursive: true });
  await fs.mkdir(output); // Never overwrite an existing run.
  const temp = path.join(output, "browser-tmp");
  await fs.mkdir(temp);
  process.env.TMPDIR = process.env.TMP = process.env.TEMP = temp;
  const localBrowsers = path.join(root, "Benchmarks.Local/tmp/playwright-browsers");
  if (!process.env.PLAYWRIGHT_BROWSERS_PATH && await fs.stat(localBrowsers).then(s => s.isDirectory()).catch(() => false))
    process.env.PLAYWRIGHT_BROWSERS_PATH = localBrowsers;
  const { chromium } = await import("playwright");
  const script = await fs.readFile(fileURLToPath(import.meta.url));
  await fs.writeFile(path.join(output, "physics-study.mjs"), script, { flag: "wx" });
  const trials = validationOnly ? ["before", "after"].map(role => ({ role, url: config[role], profile: config.profiles[0], repeat: 0, collectTimings: false })) : plan();
  const manifest = {
    schema: 2, validationOnly, createdAt: new Date().toISOString(), config, trials, node: process.version,
    assetCapture: "same-origin route.fetch(maxRedirects=0), body/hash then fulfil original response; no inspector body cache",
    scriptSha256: createHash("sha256").update(script).digest("hex"),
    scope: "Production AOT before/after with same seed, LightSwarm initial config and churn profiles. Dynamic states, survival and hits intentionally differ. Not isolated compiler performance, not canvas/FPS throughput, no cross-engine checksum parity assertion.",
    protocol: { headerVersion: 3, rows: 16, advance: "Advance(1)", profileAdvance: "Not exported by inspected bridge; not assumed." },
    phases: { oldInner: "unavailable: old bridge lacks PhysicsTiming/ResetPhysicsProfile", newFields: ["ContactCandidatesMs", "SolverMs", "HitCandidatesMs", "HitNarrowMs"] },
  };
  await fs.writeFile(path.join(output, "manifest.json"), JSON.stringify(manifest, null, 2), { flag: "wx" });
  const browser = await chromium.launch({ headless: true, downloadsPath: path.join(temp, "downloads") });
  const completed = [];
  try {
    let index = 0;
    for (const trial of trials) {
      const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, serviceWorkers: "block" });
      const errors = [], blocked = [], assets = [];
      const origin = new URL(trial.url).origin;
      const pendingAssets = new Set();
      let captureSealed = false;
      let fetches = 0;
      const fetchWaiters = [];
      const acquireFetch = async () => {
        if (fetches < 2) { fetches++; return; }
        await new Promise(resolve => fetchWaiters.push(resolve));
      };
      const releaseFetch = () => {
        const next = fetchWaiters.shift();
        if (next) next(); else fetches--;
      };
      await context.route("**/*", route => {
        const url = new URL(route.request().url());
        if (url.origin !== origin) { blocked.push(url.href); return route.abort(); }
        if (!trackedAsset(url.href)) return route.continue();
        // APIResponse owns the fetched bytes, avoiding Chromium's inspector eviction for large WASM.
        const capture = (async () => {
          let response;
          await acquireFetch();
          try {
            if (captureSealed) throw new Error(`Asset request after capture barrier: ${url.href}`);
            response = await route.fetch({ maxRedirects: 0, timeout: 180000 });
            if (new URL(response.url()).origin !== origin || response.status() >= 300 && response.status() < 400)
              throw new Error(`Asset redirects are forbidden: ${url.href} (${response.status()})`);
            if (!response.ok()) throw new Error(`Asset HTTP ${response.status()}: ${url.href}`);
            const body = await response.body();
            assets.push({ url: url.href, responseUrl: response.url(), status: response.status(),
              contentType: response.headers()["content-type"] ?? null, capture: "route-fetch-fulfilled-response",
              bytes: body.length, sha256: createHash("sha256").update(body).digest("hex") });
            await route.fulfill({ response });
          } catch (error) {
            errors.push(`Asset provenance: ${error}`);
            await route.abort().catch(() => {});
          } finally { await response?.dispose(); releaseFetch(); }
        })();
        pendingAssets.add(capture);
        void capture.then(() => pendingAssets.delete(capture), error => {
          errors.push(`Asset capture cleanup: ${error}`);
          pendingAssets.delete(capture);
        });
        return capture;
      });
      const page = await context.newPage();
      page.on("pageerror", error => errors.push(String(error)));
      try {
        await page.goto(trial.url, { waitUntil: "domcontentloaded", timeout: 180000 });
        await page.waitForFunction(() => window.spaceBattle?.ready === true && typeof getDotnetRuntime === "function", null, { timeout: 180000 });
        const readiness = await page.evaluate(inspectReadyBridge, trial);
        await page.waitForLoadState("networkidle", { timeout: 180000 });
        while (pendingAssets.size) await Promise.all([...pendingAssets]);
        captureSealed = true;
        if (errors.length) throw new Error(errors.join("\n"));
        if (!assets.some(a => nativeWasm(a.url) && a.bytes > 8)) throw new Error("No captured production native WASM asset observed");
        const result = validationOnly ? { readiness, validationOnly: true, nativeAssets: assets.filter(a => nativeWasm(a.url)) }
          : await page.evaluate(runInPage, { ...config, ...trial });
        const raw = { trial, browserVersion: browser.version(), errors, blockedExternalRequests: blocked, assets, ...result };
        await fs.writeFile(path.join(output, `${String(index++).padStart(2, "0")}-${trial.profile}-${trial.role}-${trial.collectTimings ? "inner" : "outer"}-${trial.repeat}.json`), JSON.stringify(raw, null, 2), { flag: "wx" });
        if (errors.length) throw new Error(errors.join("\n"));
        completed.push(raw);
        if (validationOnly) {
          console.log(`Asset capture validated ${trial.role}: ${readiness.runtimeMode}, ${assets.length} assets, native ${result.nativeAssets.map(a => `${path.posix.basename(assetPath(a.url))} (${a.bytes} bytes)`).join(", ")}, tick ${readiness.tick}; no Advance/timing calls`);
          continue;
        }
        const controls = completed.filter(r => r.trial.profile === trial.profile && !r.trial.collectTimings);
        if (controls.length > 1) {
          const first = controls[0].initial;
          if (controls.some(r => r.initial.seed !== first.seed || r.initial.profile !== first.profile ||
              r.initial.ships !== first.ships || r.fleetRevision !== controls[0].fleetRevision))
            throw new Error("Initial config/fleet mismatch; raw evidence retained");
        }
        console.log(`Completed ${trial.profile} ${trial.role} repeat ${trial.repeat + 1} ${trial.collectTimings ? "inner" : "outer"}`);
      } catch (error) {
        await fs.writeFile(path.join(output, "failure.json"), JSON.stringify({ trial, error: String(error), errors, blocked, assets }, null, 2), { flag: "wx" });
        throw error;
      } finally { await context.close(); }
    }
    if (validationOnly) {
      await fs.writeFile(path.join(output, "capture-validation.json"), JSON.stringify({ validationOnly: true,
        roles: completed.map(r => ({ role: r.trial.role, readiness: r.readiness, nativeAssets: r.nativeAssets, assetCount: r.assets.length })),
        note: "Live asset/readiness validation only. No Reset, Advance, warmup, performance.now, PhysicsTiming or benchmark executed." }, null, 2), { flag: "wx" });
      console.log(output);
      return;
    }
    await fs.writeFile(path.join(output, "comparison.json"), JSON.stringify({
      scope: manifest.scope,
      summaries: completed.map(r => ({ trial: r.trial, initial: r.initial, final: r.final, checksum: r.checksum,
        meanStepMs: r.samples.reduce((s, x) => s + x.stepMs, 0) / r.samples.length,
        meanAdvanceAndExportMs: r.samples.reduce((s, x) => s + x.advanceAndExportMs, 0) / r.samples.length,
        innerAvailability: r.innerAvailability })),
      note: "Keep every repeat. Old inner phases are unavailable, never zero substitutes. Use after outer/inner controls to inspect instrumentation overhead; do not assert checksum/survival/hit parity."
    }, null, 2), { flag: "wx" });
    console.log(output);
  } finally { await browser.close(); }
}

async function inspectReadyBridge(trial) {
  window.spaceBattle.pause(true);
  const text = JSON.parse(window.render_game_to_text());
  if (!text.ready || !text.paused || text.tick !== 0) throw new Error("Expected a ready paused test-mode battle at tick zero");
  const runtime = getDotnetRuntime(0);
  const exports = await runtime.getAssemblyExports("SpaceBattle.Web.dll");
  const bridge = exports.SpaceBattle?.Web?.BattleBridge;
  if (!bridge || bridge.Mode() !== "aot-binary") throw new Error("Expected production AOT bridge");
  for (const name of ["Reset", "Advance", "Snapshot", "FleetRevision"])
    if (typeof bridge[name] !== "function") throw new Error(`Missing bridge export ${name}`);
  const innerSupported = typeof bridge.ResetPhysicsProfile === "function" && typeof bridge.PhysicsTiming === "function";
  if (innerSupported !== (trial.role === "after")) throw new Error("Profiling API does not match requested revision");
  return { ready: true, paused: true, tick: text.tick, runtimeMode: bridge.Mode(), fleetRevision: bridge.FleetRevision(), innerSupported };
}

async function runInPage(options) {
  window.spaceBattle.pause(true);
  if (!JSON.parse(window.render_game_to_text()).paused) throw new Error("Canvas simulation not paused");
  const runtime = getDotnetRuntime(0);
  const exports = await runtime.getAssemblyExports("SpaceBattle.Web.dll");
  const bridge = exports.SpaceBattle?.Web?.BattleBridge;
  if (!bridge || bridge.Mode() !== "aot-binary") throw new Error("Both sides must use production real AOT binary bridges");
  if (typeof bridge.Advance !== "function" || typeof bridge.Reset !== "function") throw new Error("Missing common one-tick bridge API");
  const innerSupported = typeof bridge.ResetPhysicsProfile === "function" && typeof bridge.PhysicsTiming === "function";
  if (options.role === "after" && !innerSupported) throw new Error("New profiling bridge is not built yet");
  if (options.role === "before" && innerSupported) throw new Error("Baseline unexpectedly contains new physics profiling API");
  const names = ["ContactCandidatesMs", "SolverMs", "HitCandidatesMs", "HitNarrowMs"];
  const reset = () => innerSupported
    ? bridge.ResetPhysicsProfile(options.profile, options.seed, options.collectTimings)
    : bridge.Reset(options.profile, options.seed);
  const header = pointer => {
    const heap = runtime.localHeapViewF64();
    const start = pointer / 8;
    if (!Number.isInteger(start) || start < 0 || start + 32 > heap.length) throw new Error("Invalid binary pointer");
    const fixed = heap.subarray(start, start + 32);
    const length = fixed[31] / 8 - fixed[30] * 16;
    if (fixed[0] !== 3 || !Number.isInteger(length) || length < 73 || length > 1024 || start + length > heap.length)
      throw new Error("Unsupported binary header protocol");
    return Array.from(heap.subarray(start, start + length));
  };
  const state = h => ({ tick: h[1], seed: h[3], profile: h[4], ships: h[6], projectiles: h[7], debris: h[8], particles: h[9],
    shots: h[14], hits: h[15], created: h[16], destroyed: h[17], bodies: h[42], fixtures: h[43], aliveTeams: [h[70], h[71]], fleetComposition: h[72] });
  reset();
  for (let tick = 0; tick < options.warmup; tick++) bridge.Advance(1);
  const initialHeader = header(reset()); // Dispose warmup battle and restore exact initial seed/profile.
  const initial = state(initialHeader);
  if (initial.tick !== 0 || initial.seed !== options.seed || initial.profile !== ["low", "nominal", "high"].indexOf(options.profile) || initial.fleetComposition !== 1 || initial.ships < 1)
    throw new Error("Invalid LightSwarm starting configuration");
  const fleetRevision = typeof bridge.FleetRevision === "function" ? bridge.FleetRevision() : null;
  if (!fleetRevision) throw new Error("Fleet revision evidence missing");
  const innerAvailability = !innerSupported ? "unavailable: old bridge has no inner exports"
    : !options.collectTimings ? "disabled: outer-only control" : "available: PhysicsTiming fields 0..3";
  const samples = [];
  for (let tick = 1; tick <= options.ticks; tick++) {
    const start = performance.now();
    const pointer = bridge.Advance(1);
    const elapsed = performance.now() - start;
    const h = header(pointer);
    if (h[1] !== tick) throw new Error(`Unexpected tick ${h[1]}, expected ${tick}`);
    const stages = typeof bridge.DiagnosticStage === "function" ? Array.from({ length: 9 }, (_, i) => bridge.DiagnosticStage(i)) : null;
    const queries = typeof bridge.DiagnosticPhysicsQuery === "function" ? Array.from({ length: 4 }, (_, i) => bridge.DiagnosticPhysicsQuery(i)) : null;
    const inner = innerSupported && options.collectTimings ? Object.fromEntries(names.map((name, i) => [name, bridge.PhysicsTiming(i)])) : null;
    if (!Number.isFinite(h[5]) || h[5] < 0 || inner && Object.values(inner).some(x => !Number.isFinite(x) || x < 0))
      throw new Error("Invalid timer export");
    samples.push({ tick, stepMs: h[5], advanceAndExportMs: elapsed,
      combinedPhysicsStageMs: stages?.[6] ?? null, combinedHitsStageMs: stages?.[7] ?? null,
      inner, queries, stages, state: state(h), frameBytes: h[31] });
  }
  const final = state(header(bridge.Snapshot()));
  const checksum = typeof bridge.Checksum === "function" ? bridge.Checksum() : null;
  if (typeof bridge.Validate === "function") bridge.Validate();
  // Direct bridge calls bypass the client's reusable row views; refresh before RAF resumes.
  window.spaceBattle.refresh();
  return { runtimeMode: bridge.Mode(), fleetRevision,
    physicsRevision: typeof bridge.PhysicsRevision === "function" ? bridge.PhysicsRevision() : "preserved-aether-baseline",
    initial, final, checksum, innerAvailability, samples,
    userAgent: navigator.userAgent, hardwareConcurrency: navigator.hardwareConcurrency };
}
