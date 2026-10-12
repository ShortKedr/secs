import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import { createHash, randomUUID } from "node:crypto";
import { performance } from "node:perf_hooks";
import { fileURLToPath, pathToFileURL } from "node:url";

export const PROJECT_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
export const PROFILES = ["low", "nominal", "high"];
export const HELP = `Mono compiler study (no builds, downloads, or remote writes).
node tools/web/mono-compiler-study.mjs --sites <A-wwwroot> <B-wwwroot> <C-wwwroot> <D-wwwroot> --labels A B C D
  --repeats 3 --ticks 1200 --phase-ticks 300 --warmup-ticks 1200
  --start-tick 0 --frontend-start-tick 300 --frontend-ticks 300
  --output Benchmarks.Local/tmp/<new-study> [--source-roots <A> <B> <C> <D>]
  [--browser-path <existing-Chromium-executable>] [--timeout-ms 180000]
Lists accept spaces or commas. All three profiles and both replay modes always run.
--repeats is exact (>=3); rotating orders have position counts differing by at most
one when repeats is not divisible by variant count. No extra repeats are hidden.
Each replay launches a fresh browser process/context/page, sequentially. Launch
cost is separate from navigation-to-runtime.ready. OS file caches are NOT flushed.
Frontend uses existing advanceTime(1000/60): update/export/DOM/Canvas submission,
not RAF scheduling, compositor/GPU busy time, FPS, or whole-system CPU usage.
Readiness/header hooks alter only the served app.js in memory; hashes and overhead
scope are recorded. Per-run phase percentiles and independent-run medians only;
never pooled/global percentiles. Production aot-binary only, no interpreter/CoreCLR.
--help and importing this module do not launch a browser or create outputs.`;

export function inside(root, target) {
  const relative = path.relative(root, target);
  return relative === "" || (!path.isAbsolute(relative) && relative !== ".." && !relative.startsWith(`..${path.sep}`));
}

export function outputPath(root, value) {
  const output = path.resolve(root, value);
  const base = path.join(root, "Benchmarks.Local", "tmp");
  const relative = path.relative(base, output);
  if (path.dirname(output) !== base || !/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(relative))
    throw new Error("Output must be a NEW direct child of project Benchmarks.Local/tmp/<study>");
  return output;
}

export function parseArgs(argv, root = PROJECT_ROOT) {
  const listKeys = new Set(["sites", "labels", "source-roots"]);
  const scalarKeys = new Set(["repeats", "ticks", "phase-ticks", "warmup-ticks", "start-tick", "frontend-start-tick", "frontend-ticks", "seed", "output", "browser-path", "timeout-ms"]);
  const values = {};
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--help" && argv.length === 1) return { help: true };
    const key = argv[i].replace(/^--/, "");
    if (!argv[i].startsWith("--") || (!listKeys.has(key) && !scalarKeys.has(key)) || key in values)
      throw new Error(`Unknown or duplicate option: ${argv[i]}`);
    const parts = [];
    while (i + 1 < argv.length && !argv[i + 1].startsWith("--")) parts.push(argv[++i]);
    if (!parts.length || (!listKeys.has(key) && parts.length !== 1)) throw new Error(`Missing/invalid value for --${key}`);
    values[key] = listKeys.has(key) ? parts.flatMap(p => p.split(",")).map(p => p.trim()) : parts[0];
    if (listKeys.has(key) && values[key].some(p => !p)) throw new Error(`Empty --${key} entry`);
  }
  const number = (key, fallback, min = 0, max = 1_000_000) => {
    const raw = values[key] ?? String(fallback);
    const value = Number(raw);
    if (!/^\d+$/.test(raw) || !Number.isSafeInteger(value) || value < min || value > max)
      throw new Error(`--${key} must be an integer in ${min}..${max}`);
    return value;
  };
  const labels = values.labels ?? ["A", "B", "C", "D"];
  const sites = (values.sites ?? []).map(site => path.resolve(root, site));
  if (sites.length < 2 || sites.length > 4 || sites.length !== labels.length || new Set(sites).size !== sites.length ||
      new Set(labels).size !== labels.length || labels.some(label => !/^[ABCD]$/.test(label)))
    throw new Error("Provide 2..4 unique sites and matching unique labels from A B C D");
  const sourceRoots = values["source-roots"]?.map(p => path.resolve(root, p)) ?? sites.map(site =>
    path.basename(site) === "wwwroot" && path.basename(path.dirname(site)) === "publish" ? path.resolve(site, "../..") : site);
  if (sourceRoots.length !== sites.length || [...sites, ...sourceRoots].some(p => !inside(root, p)))
    throw new Error("Sites/source roots must remain inside this project and match labels");
  return {
    root, sites, labels, sourceRoots, profiles: [...PROFILES], repeats: number("repeats", 3, 3, 100),
    ticks: number("ticks", 1200, 1), phaseTicks: number("phase-ticks", 300, 1), warmupTicks: number("warmup-ticks", 1200),
    startTick: number("start-tick", 0), frontendStartTick: number("frontend-start-tick", 300),
    frontendTicks: number("frontend-ticks", 300, 1), seed: number("seed", 7331, 0, 0xffffffff),
    timeoutMs: number("timeout-ms", 180000, 1000, 3_600_000),
    browserPath: values["browser-path"] ? path.resolve(root, values["browser-path"]) : null,
    output: outputPath(root, values.output ?? `Benchmarks.Local/tmp/mono-study-${new Date().toISOString().replace(/[:.]/g, "-")}-${randomUUID().slice(0, 8)}`),
  };
}

export function variantOrder(count, round) {
  if (!Number.isInteger(count) || count < 2 || count > 4 || !Number.isInteger(round) || round < 0) throw new Error("Invalid order arguments");
  return Array.from({ length: count }, (_, position) => (position + round) % count);
}

export function makePlan(config) {
  const plan = [];
  let round = 0;
  const modes = config.modes ?? ["core", "frontend"];
  if (!modes.length || new Set(modes).size !== modes.length || modes.some(mode => !["core", "frontend"].includes(mode))) throw new Error("Invalid replay modes");
  for (const mode of modes)
    for (const profile of config.profiles)
      for (let repeat = 1; repeat <= config.repeats; repeat++, round++)
        for (const [position, variant] of variantOrder(config.labels.length, round).entries())
          plan.push({ ordinal: plan.length, mode, profile, repeat, position, variant, label: config.labels[variant],
            startTick: mode === "core" ? config.startTick : config.frontendStartTick,
            ticks: mode === "core" ? config.ticks : config.frontendTicks });
  return plan;
}

export function orderBalance(plan) {
  const groups = new Map();
  for (const trial of plan) {
    const key = `${trial.mode}/${trial.profile}`;
    if (!groups.has(key)) groups.set(key, {});
    const group = groups.get(key);
    group[trial.label] ??= Array(new Set(plan.map(t => t.label)).size).fill(0);
    group[trial.label][trial.position]++;
  }
  return Object.fromEntries(groups);
}

export function stats(values) {
  if (!values.length || values.some(v => !Number.isFinite(v))) throw new Error("Finite nonempty samples required");
  const sorted = [...values].sort((a, b) => a - b);
  const percentile = fraction => sorted[Math.max(0, Math.ceil(sorted.length * fraction) - 1)];
  const middle = Math.floor(sorted.length / 2);
  return { count: sorted.length, mean: values.reduce((a, b) => a + b, 0) / values.length,
    median: sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2,
    p95: percentile(.95), p99: percentile(.99), min: sorted[0], max: sorted.at(-1) };
}

export function readHeader(heap, pointer) {
  const start = pointer / 8;
  if (!Number.isInteger(start) || start < 0 || start + 80 > heap.length) throw new Error("Invalid binary pointer");
  const h = Array.from(heap.subarray(start, start + 80));
  if (h[0] !== 3 || !Number.isInteger(h[30]) || h[30] < 0 || h[31] !== 640 + h[30] * 128 || start * 8 + h[31] > heap.byteLength)
    throw new Error("Unsupported binary frame protocol");
  return h;
}

export function stateFromHeader(h) {
  const pick = indices => indices.map(i => h[i]);
  const state = { tick: h[1], seed: h[3], profile: h[4], arenaHalfSize: h[2], fleetComposition: h[72],
    counts: pick([6, 7, 8, 9, 63]), renderCounts: pick([10, 11, 12, 13, 64]),
    counters: [...h.slice(14, 30), ...h.slice(65, 69)], tactics: h.slice(58, 62),
    populations: pick([37, 42, 43, 62, 69, 70, 71]), totalRows: h[30] };
  if (h.length !== 80 || Object.values(state).flat().some(v => !Number.isFinite(v))) throw new Error("Nonfinite/incomplete trajectory state");
  return state;
}

function differences(a, b, prefix = "") {
  if (Object.is(a, b)) return [];
  if (a && b && typeof a === "object" && typeof b === "object") {
    return [...new Set([...Object.keys(a), ...Object.keys(b)])].sort().flatMap(key =>
      differences(a[key], b[key], prefix ? `${prefix}.${key}` : key));
  }
  return [{ field: prefix, expected: a ?? null, actual: b ?? null }];
}

export function compareTrajectories(runs, plan = null) {
  const mismatches = [];
  if (!runs.length) mismatches.push({ location: "coverage", field: "runs", expected: "nonempty independent replays", actual: [] });
  let comparisons = 0;
  const add = (reference, candidate, location, a, b) => {
    comparisons++;
    for (const diff of differences(a, b)) mismatches.push({ reference: reference.trial, candidate: candidate.trial, location, ...diff });
  };
  if (plan) {
    const id = t => `${t.mode}/${t.profile}/${t.label}/${t.repeat}`;
    const expected = plan.map(id);
    const actual = runs.map(r => id(r.trial));
    if (actual.length !== expected.length || new Set(actual).size !== actual.length || expected.some(key => !actual.includes(key)))
      mismatches.push({ location: "coverage", field: "runs", expected, actual });
  }
  for (const profile of PROFILES) {
    const group = runs.filter(r => r.trial.profile === profile);
    for (const mode of ["core", "frontend"]) {
      const sameMode = group.filter(r => r.trial.mode === mode);
      const reference = sameMode[0];
      for (const candidate of sameMode.slice(1)) {
        add(reference, candidate, "revisions", reference.revisions, candidate.revisions);
        add(reference, candidate, "checkpoints", reference.checkpoints, candidate.checkpoints);
        add(reference, candidate, "perTickStates", reference.samples.map(s => s.state), candidate.samples.map(s => s.state));
      }
    }
    const core = group.find(r => r.trial.mode === "core");
    if (!core) continue;
    const coreSamples = new Map(core.samples.map(s => [s.state.tick, s.state]));
    const coreCheckpoints = new Map(core.checkpoints.map(c => [c.state.tick, c]));
    for (const frontend of group.filter(r => r.trial.mode === "frontend")) {
      for (const sample of frontend.samples) {
        const match = coreSamples.get(sample.state.tick);
        if (match) add(core, frontend, `crossMode.tick${sample.state.tick}`, match, sample.state);
      }
      for (const checkpoint of frontend.checkpoints) {
        const match = coreCheckpoints.get(checkpoint.state.tick);
        if (match) add(core, frontend, `crossMode.checkpoint${checkpoint.state.tick}`, match, checkpoint);
      }
    }
  }
  return { equivalent: mismatches.length === 0, comparisons, mismatches,
    scope: "Exact checkpoint checksum/state/ship tactics/reinforcements and every sampled tick's counts/counters/populations/tactics; overlapping core/frontend windows. Timing/memory are excluded from equivalence." };
}

export function summarizePhases(run, phaseTicks) {
  const phases = [];
  for (let offset = 0; offset < run.samples.length; offset += phaseTicks) {
    const samples = run.samples.slice(offset, offset + phaseTicks);
    const timing = Object.fromEntries(Object.keys(samples[0].timing).map(key => [key, stats(samples.map(s => s.timing[key]))]));
    phases.push({ startTick: samples[0].state.tick, endTick: samples.at(-1).state.tick, timing });
  }
  return phases;
}

export function independentRunMedians(runs) {
  const groups = new Map();
  for (const run of runs) for (const phase of run.phases) {
    const key = `${run.trial.mode}/${run.trial.profile}/${run.trial.label}/${phase.startTick}-${phase.endTick}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push({ repeat: run.trial.repeat, timing: phase.timing });
  }
  return [...groups].map(([key, repeats]) => ({ key, repeats,
    mediansAcrossIndependentRuns: Object.fromEntries(Object.keys(repeats[0].timing).map(metric => [metric,
      Object.fromEntries(["mean", "median", "p95", "p99"].map(statistic =>
        [statistic, stats(repeats.map(run => run.timing[metric][statistic])).median]))])),
    note: "p95/p99 here are medians of independent per-run phase percentiles, NOT pooled/global percentiles." }));
}

export function injectHooks(source) {
  const replacements = [
    ["        runtimeStarted = true;", "        window.__monoCompilerStudy.runtimeReadyMs = performance.now();\n        runtimeStarted = true;"],
    ["      setPaused(testMode || Boolean(diagnosticMode));\n      resize();", "      setPaused(testMode || Boolean(diagnosticMode));\n      resize();\n      window.__monoCompilerStudy.appReadyMs = performance.now();"],
    ["  window.spaceBattle = {", "  window.__monoCompilerStudy.header = () => Array.from(new Float64Array(runtime.localHeapViewF64().buffer, framePointer, 80));\n  window.spaceBattle = {"],
  ];
  let body = source.replace(/\r\n/g, "\n");
  if (body.includes("__monoCompilerStudy")) throw new Error("Source already contains study hooks");
  for (const [marker, replacement] of replacements) {
    if (body.split(marker).length !== 2) throw new Error(`app.js hook marker changed/nonunique: ${marker}`);
    body = body.replace(marker, replacement);
  }
  if (body.split("requestAnimationFrame(frame);").length !== 3) throw new Error("RAF measurement marker changed");
  body = body.replaceAll("requestAnimationFrame(frame);", "/* Automatic RAF disabled in this measurement page; advanceTime still draws. */");
  return body;
}

const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");

export async function assertNoLinks(root, target, lstat = fs.lstat) {
  if (!inside(root, target)) throw new Error("Path escapes project");
  let current = root;
  for (const segment of ["", ...path.relative(root, target).split(path.sep).filter(Boolean)]) {
    if (segment) current = path.join(current, segment);
    const stat = await lstat(current);
    if (stat.isSymbolicLink()) throw new Error(`Symlink/junction paths are forbidden: ${current}`);
  }
}

export async function reserveOutput(root, output) {
  outputPath(root, output);
  // Never follow a junction to a directory outside the project, or reuse a study.
  await assertNoLinks(root, path.join(root, "Benchmarks.Local"));
  const tmp = path.join(root, "Benchmarks.Local", "tmp");
  await fs.mkdir(tmp).catch(error => { if (error.code !== "EEXIST") throw error; });
  await assertNoLinks(root, tmp);
  await fs.mkdir(output); // Exclusive directory creation: EEXIST is fatal.
}

export async function hashTree(directory, sourceOnly = false) {
  if ((await fs.lstat(directory)).isSymbolicLink()) throw new Error(`Symlink provenance root: ${directory}`);
  const files = [];
  const walk = async relative => {
    const entries = await fs.readdir(path.join(directory, relative), { withFileTypes: true });
    entries.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
    for (const entry of entries) {
      if (sourceOnly && ["publish", "bin", "obj", ".git", "node_modules"].includes(entry.name)) continue;
      if (entry.isSymbolicLink()) throw new Error(`Symlink in provenance tree: ${entry.name}`);
      const name = relative ? `${relative}/${entry.name}` : entry.name;
      if (entry.isDirectory()) await walk(name);
      else if (entry.isFile()) {
        const bytes = await fs.readFile(path.join(directory, name));
        files.push({ path: name, bytes: bytes.length, sha256: sha256(bytes) });
      } else throw new Error(`Unsupported source entry: ${name}`);
    }
  };
  await walk("");
  return { directory, files, sha256: sha256(JSON.stringify(files)), fileCount: files.length };
}

export function runtimeVersions(boot) {
  const assets = Object.keys(boot.resources?.jsModuleRuntime ?? {});
  const versions = [...new Set(assets.map(name => /^dotnet\.runtime\.(\d+\.\d+\.\d+(?:-[^.]+)?)\./.exec(name)?.[1]).filter(Boolean))];
  if (!versions.length) throw new Error("Cannot identify runtime version from boot jsModuleRuntime assets");
  return { versions, runtimeAssets: assets, bootResourceHash: boot.resources?.hash ?? null };
}

async function prepareVariant(config, variant) {
  const site = config.sites[variant];
  await assertNoLinks(config.root, site);
  await assertNoLinks(config.root, config.sourceRoots[variant]);
  const artifacts = await hashTree(site);
  const source = await fs.readFile(path.join(site, "app.js"), "utf8");
  const injected = injectHooks(source);
  const boot = JSON.parse(await fs.readFile(path.join(site, "_framework", "blazor.boot.json"), "utf8"));
  if (!artifacts.files.some(f => /^_framework\/dotnet\.native.*\.wasm$/.test(f.path))) throw new Error("Missing Mono native WASM artifact");
  return { label: config.labels[variant], site, artifacts, sources: await hashTree(config.sourceRoots[variant], true),
    runtime: runtimeVersions(boot), originalAppSha256: sha256(Buffer.from(source)), servedAppSha256: sha256(Buffer.from(injected)), injected };
}

async function serve(variant) {
  const mime = { ".js": "text/javascript", ".json": "application/json", ".html": "text/html", ".wasm": "application/wasm", ".css": "text/css", ".png": "image/png", ".svg": "image/svg+xml", ".webp": "image/webp" };
  const expected = new Map(variant.artifacts.files.map(file => [file.path, file.sha256]));
  const errors = [];
  const server = http.createServer(async (request, response) => {
    try {
      if (request.method !== "GET" && request.method !== "HEAD") { response.writeHead(405).end(); return; }
      const pathname = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
      const file = path.resolve(variant.site, pathname.slice(1) || "index.html");
      if (!inside(variant.site, file) || file === variant.site) { response.writeHead(403).end(); return; }
      const relative = path.relative(variant.site, file).split(path.sep).join("/");
      if (!expected.has(relative)) { response.writeHead(404).end(); return; }
      await assertNoLinks(variant.site, file);
      let bytes = await fs.readFile(file);
      if (sha256(bytes) !== expected.get(relative)) throw new Error(`Artifact changed during study: ${relative}`);
      if (relative === "app.js") bytes = Buffer.from(variant.injected);
      response.writeHead(200, { "Content-Type": mime[path.extname(file)] ?? "application/octet-stream", "Cache-Control": "no-store" });
      response.end(request.method === "HEAD" ? undefined : bytes);
    } catch (error) {
      errors.push(String(error));
      if (!response.headersSent) response.writeHead(500);
      response.end();
    }
  });
  await new Promise((resolve, reject) => { server.once("error", reject); server.listen(0, "127.0.0.1", resolve); });
  return { server, errors, url: `http://127.0.0.1:${server.address().port}/?test=1&lang=en` };
}

// Serialized into the page: helpers are passed as source, not imported there.
export async function replayInPage({ config, trial, helpers }) {
  const { readHeader, stateFromHeader } = Object.fromEntries(Object.entries(helpers).map(([name, source]) => [name, (0, eval)(`(${source})`)]));
  const runtime = getDotnetRuntime(0);
  const bridge = (await runtime.getAssemblyExports("SpaceBattle.Web.dll")).SpaceBattle.Web.BattleBridge;
  for (const name of ["Reset", "Advance", "Snapshot", "Checksum", "Validate", "DebugColliders", "ShipTactic", "Reinforcement", "Mode"])
    if (typeof bridge[name] !== "function") throw new Error(`Missing bridge export: ${name}`);
  if (bridge.Mode() !== "aot-binary") throw new Error("Production Mono AOT bridge required (aot-binary only; no interpreter/CoreCLR)");
  window.spaceBattle.pause(true);
  window.spaceBattle.showColliders(false);
  bridge.DebugColliders(false);
  if (window.spaceBattle.colliders().enabled) throw new Error("Colliders must be off");
  document.getElementById("profile").value = trial.profile;
  document.getElementById("seed").value = String(config.seed);
  const frontend = trial.mode === "frontend";
  let pointer;
  const header = () => frontend ? window.__monoCompilerStudy.header() : readHeader(runtime.localHeapViewF64(), pointer);
  const reset = () => {
    if (frontend) window.spaceBattle.reset(); else pointer = bridge.Reset(trial.profile, config.seed);
    const h = header();
    if (!window.spaceBattle.ready || h[1] !== 0 || h[3] !== config.seed || h[4] !== ["low", "nominal", "high"].indexOf(trial.profile))
      throw new Error("Reset did not restore requested tick/seed/profile");
  };
  const advance = async () => {
    if (frontend) await window.advanceTime(1000 / 60); else pointer = bridge.Advance(1);
  };
  const memory = h => ({ linearMemoryBytes: runtime.localHeapViewU8().length,
    managedHeapBytesTelemetry: Number.isFinite(h[44]) ? h[44] : null,
    nativeEcsBytesTelemetry: Number.isFinite(h[38]) ? h[38] : null,
    accessorBytesTelemetry: Number.isFinite(h[39]) ? h[39] : null,
    gcCollectionsTelemetry: h.slice(55, 58),
    jsHeapUsedBytes: performance.memory?.usedJSHeapSize ?? null });
  const checkpoint = () => {
    const h = header(), heap = runtime.localHeapViewF64();
    // The frontend pointer stays private; visible ship rows already expose serials.
    const serials = frontend ? window.spaceBattle.snapshot.ships.map(row => row[0]) :
      Array.from({ length: h[10] }, (_, i) => heap[pointer / 8 + 80 + i * 16]);
    const shipTactics = serials.sort((a, b) => a - b).map(serial => ({ serial,
      fields: [0, 1, 2, 3, 4].map(field => bridge.ShipTactic(serial, field)) }));
    return { state: stateFromHeader(h), checksum: bridge.Checksum(), shipTactics,
      reinforcements: [0, 1].map(team => Array.from({ length: 10 }, (_, field) => bridge.Reinforcement(team, field))) };
  };
  reset();
  // Warm a separate battle using the mode's own path, then discard it.
  for (let tick = 1; tick <= config.warmupTicks; tick++) await advance();
  const warmupFinalTick = header()[1];
  if (warmupFinalTick !== config.warmupTicks) throw new Error("Warmup tick drift");
  reset();
  const checkpoints = [checkpoint()];
  for (let tick = 1; tick <= trial.startTick; tick++) await advance();
  if (header()[1] !== trial.startTick) throw new Error("Window start tick drift");
  if (trial.startTick) checkpoints.push(checkpoint());
  const samples = [];
  for (let offset = 1; offset <= trial.ticks; offset++) {
    let wallMs;
    if (frontend) {
      const started = performance.now();
      await window.advanceTime(1000 / 60);
      wallMs = performance.now() - started;
    } else {
      const started = performance.now();
      pointer = bridge.Advance(1);
      wallMs = performance.now() - started;
    }
    const h = header();
    if (h[1] !== trial.startTick + offset) throw new Error("Measured tick drift");
    if (![h[5], h[45], wallMs].every(v => Number.isFinite(v) && v >= 0)) throw new Error("Invalid timing telemetry");
    const timing = frontend ? { stepMs: h[5], exportMs: h[45], fullFrontendMs: wallMs,
      clientRemainderEstimateMs: wallMs - h[5] - h[45] } :
      { stepMs: h[5], advanceWallMs: wallMs, exportMs: h[45],
        exportAndInteropEstimateMs: wallMs - h[5], interopRemainderEstimateMs: wallMs - h[5] - h[45] };
    samples.push({ state: stateFromHeader(h), timing, frameBytes: h[31], memory: memory(h) });
    if (offset % config.phaseTicks === 0 || offset === trial.ticks) checkpoints.push(checkpoint());
  }
  const validatedChecksum = bridge.Validate();
  if (validatedChecksum !== checkpoints.at(-1).checksum) throw new Error("Validation changed final checksum");
  const runtimeConfig = typeof runtime.getConfig === "function" ? runtime.getConfig() : null;
  return { runtimeMode: bridge.Mode(), runtimeBuildInfo: runtime.runtimeBuildInfo ?? null,
    runtimeVersionReported: runtimeConfig?.productVersion ?? runtimeConfig?.runtimeVersion ?? null,
    userAgent: navigator.userAgent, logicalProcessors: navigator.hardwareConcurrency, devicePixelRatio,
    warmupFinalTick, checkpoints, samples,
    revisions: Object.fromEntries(["Revision", "FleetRevision", "PhysicsRevision"].map(name => [name, typeof bridge[name] === "function" ? bridge[name]() : null])) };
}

const NOTES = {
  cold: "runtime.ready is the in-page timestamp immediately after await Blazor.start(), before assembly exports/atlases/reset. Both readiness clocks use performance.now() relative to navigation timeOrigin; no networkidle or Node polling delay. Launch wall cost is separate.",
  isolation: "Fresh Chromium process/context/page per independent replay, all sequential; process-shared compiled WASM cache avoided. OS file caches, hardware temperature, power policy and background workload are not controlled/flushed. Local server hash/read work also contributes to cold startup.",
  instrumentation: "Served app.js only: two startup performance.now calls and a getter copying the existing binary header. Getter runs OUTSIDE frontend timing; startup hooks and app routing alter cold timing. No per-tick timing hook, extra Snapshot, DOM replacement or production-source edit. Source and served hashes differ and are recorded. Core/diagnostic state reads/checksums/memory copies are outside timed calls but may perturb caches/GC. Frontend timer includes the advanceTime Promise continuation.",
  timing: "Step=header[5]; export=header[45]; Advance wall includes Step/export/interop. Remainders are subtraction estimates, may be negative at timer resolution; never clamp. Managed/native/accessor telemetry refreshes roughly every 250ms, not a fresh allocation measurement. JS heap telemetry is optional/approximate. WASM bytes are high-water capacity, not live memory.",
  statistics: "Nearest-rank p95/p99 per independent run and fixed phase only. Phase boundaries mean window offsets; do not pool stages/profiles/modes/repeats. Aggregation is median of independent run statistics, not a global percentile. No speedup conclusions when trajectories differ.",
  scope: "Automatic app RAF disabled only in served measurement pages, avoiding stale frontend views after direct bridge.Reset and excluding background draw work. Frontend advanceTime retains original one-tick CPU update/export/DOM/draw submission on identical seeded tick windows after a separate warmup and Reset. Excludes RAF pacing, compositor/GPU execution and real FPS. No worker experiment is performed.",
};

export async function runStudy(config) {
  // Validate/hash first; no browser launch or output until preflight is complete.
  const variants = [];
  for (let i = 0; i < config.labels.length; i++) variants.push(await prepareVariant(config, i));
  const { chromium } = await import("playwright");
  const executablePath = config.browserPath ?? chromium.executablePath();
  const executableBytes = await fs.readFile(executablePath); // No install/download fallback.
  const references = {};
  for (const relative of ["tools/web/mono-compiler-study.mjs", "tools/web/test-mono-compiler-study.mjs", "tools/web/profile-revisions.mjs", "tools/web/profile-render.mjs", "SpaceBattle.Web/BattleBridge.cs", "SpaceBattle.Web/wwwroot/app.js"])
    references[relative] = sha256(await fs.readFile(path.join(config.root, relative)));
  await reserveOutput(config.root, config.output);
  const write = (name, data) => fs.writeFile(path.join(config.output, name), JSON.stringify(data, null, 2) + "\n", { flag: "wx" });
  const plan = makePlan(config);
  await write("manifest.json", { schema: 1, createdAt: new Date().toISOString(), config, plan, orderBalance: orderBalance(plan),
    notes: NOTES, browser: { executablePath, executableSha256: sha256(executableBytes), playwrightBrowsersPath: process.env.PLAYWRIGHT_BROWSERS_PATH ?? null },
    node: process.version, platform: process.platform, architecture: process.arch, currentReferenceHashes: references,
    provenanceNote: "Variant source roots (including frozen baseline projects/global.json if present) and served publish trees are separately hashed. Current reference hashes describe this checkout, NOT proof of inputs used to compile a prebuilt variant.",
    variants: variants.map(({ injected, ...variant }) => variant) });
  const servers = [], runs = [];
  let trial = null, browser = null, browserVersion = null;
  try {
    for (const variant of variants) servers.push(await serve(variant));
    for (trial of plan) {
      const errors = [], blocked = [];
      const started = performance.now();
      browser = await chromium.launch({ headless: true, executablePath });
      const launchWallMs = performance.now() - started;
      const version = browser.version();
      browserVersion ??= version;
      if (version !== browserVersion) throw new Error("Browser version changed between replays");
      const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, serviceWorkers: "block" });
      const origin = new URL(servers[trial.variant].url).origin;
      await context.route("**/*", route => {
        const url = new URL(route.request().url());
        if (url.origin !== origin) { blocked.push(url.href); return route.abort(); }
        return route.continue();
      });
      await context.addInitScript(() => { window.__monoCompilerStudy = {}; });
      const page = await context.newPage();
      page.on("pageerror", error => errors.push(String(error)));
      await page.goto(servers[trial.variant].url, { waitUntil: "commit", timeout: config.timeoutMs });
      await page.waitForFunction(() => Number.isFinite(window.__monoCompilerStudy?.appReadyMs) && window.spaceBattle?.ready, null, { timeout: config.timeoutMs });
      const cold = await page.evaluate(() => ({ runtimeReadyFromNavigationMs: window.__monoCompilerStudy.runtimeReadyMs,
        appReadyFromNavigationMs: window.__monoCompilerStudy.appReadyMs, navigationTimeOrigin: performance.timeOrigin }));
      if (!Number.isFinite(cold.runtimeReadyFromNavigationMs) || cold.runtimeReadyFromNavigationMs < 0 || cold.appReadyFromNavigationMs < cold.runtimeReadyFromNavigationMs)
        throw new Error("Missing/invalid cold startup hooks");
      const result = await page.evaluate(replayInPage, { config, trial,
        helpers: { readHeader: readHeader.toString(), stateFromHeader: stateFromHeader.toString() } });
      const run = { trial, cold: { ...cold, launchWallMs }, browserVersion: version,
        publishedRuntime: variants[trial.variant].runtime, errors, blockedExternalRequests: blocked, ...result };
      run.phases = summarizePhases(run, config.phaseTicks);
      await write(`${String(trial.ordinal).padStart(3, "0")}-${trial.mode}-${trial.profile}-${trial.label}-r${trial.repeat}.json`, run);
      runs.push(run);
      if (errors.length || servers.some(s => s.errors.length)) throw new Error([...errors, ...servers.flatMap(s => s.errors)].join("\n"));
      if (blocked.length) throw new Error("Unexpected external requests; raw run retained");
      await context.close();
      await browser.close();
      browser = null;
      console.log(`Completed ${trial.mode} ${trial.profile} ${trial.label} repeat ${trial.repeat}; no aggregate speedup claim`);
    }
    // Rehash at the end to reject mutated frozen input/artifacts.
    for (const variant of variants) {
      if ((await hashTree(variant.site)).sha256 !== variant.artifacts.sha256 ||
          (await hashTree(variant.sources.directory, true)).sha256 !== variant.sources.sha256)
        throw new Error(`Variant ${variant.label} provenance changed during study`);
    }
    if (sha256(await fs.readFile(executablePath)) !== sha256(executableBytes)) throw new Error("Browser executable changed during study");
    const equivalence = compareTrajectories(runs, plan);
    await write("equivalence.json", equivalence);
    await write("independent-run-medians.json", { notes: NOTES.statistics, equivalencePassed: equivalence.equivalent,
      performanceComparisonEligible: equivalence.equivalent, phases: independentRunMedians(runs) });
    console.log(config.output);
    if (!equivalence.equivalent) throw new Error("Trajectory equivalence failed; raw evidence and differences retained");
    return { output: config.output, equivalent: true, runs: runs.length };
  } catch (error) {
    await write("failure.json", { trial, completedRuns: runs.length, error: String(error), serverErrors: servers.flatMap(s => s.errors) });
    throw error;
  } finally {
    await browser?.close();
    for (const { server } of servers) await new Promise(resolve => server.close(resolve));
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const config = parseArgs(process.argv.slice(2));
    if (config.help) console.log(HELP); else await runStudy(config);
  } catch (error) { console.error(String(error)); process.exitCode = 1; }
}
