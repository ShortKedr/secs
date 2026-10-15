import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import { fileURLToPath } from "node:url";
import { createHash, randomUUID } from "node:crypto";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const tmpRoot = path.join(projectRoot, "Benchmarks.Local/tmp");
export const categories = ["managed-own-simulation", "managed-library-compute", "mono-interpreter", "dispatch-runtime-helpers", "gc", "interop", "other-js-renderer", "unknown"];
const wasmIndex = name => /^\$?wasm-function\[(\d+)\](?::0x[\da-f]+)?$/i.exec(name)?.[1];
const isWasm = frame => wasmIndex(frame.functionName ?? "") !== undefined || /^(?:wasm:)|\.wasm(?:[?#]|$)/i.test(frame.url ?? "");
const nativeUrl = url => /(?:^|\/)dotnet\.native(?:\.[^/]*)?\.wasm(?:[?#]|$)/i.test(url);

export function parseSymbolMap(text) {
  const map = new Map();
  for (const [offset, line] of text.replace(/^\uFEFF/, "").split(/\r?\n/).entries()) {
    if (!line.trim()) continue;
    const match = /^(\d+):(.+)$/.exec(line);
    if (!match || !Number.isSafeInteger(Number(match[1]))) throw new Error(`Invalid symbol map line ${offset + 1}`);
    const index = Number(match[1]), name = match[2].trim();
    if (!name || (map.has(index) && map.get(index) !== name)) throw new Error(`Conflicting/empty symbol at index ${index}`);
    map.set(index, name);
  }
  if (!map.size) throw new Error("Empty symbol map");
  return map;
}

export function classifyFrame(frame) {
  const name = frame.name ?? frame.functionName ?? "";
  if (frame.unresolved || wasmIndex(name) !== undefined) return "unknown";
  if (/^\(garbage collector\)$|^(?:_?sgen_|_?mono_gc_|GC_|gc_collect|System[._]GC[._])/i.test(name)) return "gc";
  if (/^_?mono_interp_.*trampoline/i.test(name)) return "dispatch-runtime-helpers";
  if (/^(?:_?mono_interp_|interp_exec|interp_execute|InterpExecMethod)/i.test(name)) return "mono-interpreter";
  if (/wrapper_(?:managed_to_native|native_to_managed)|^_?mono_wasm_|^_?emscripten_|^System[._]Runtime[._]InteropServices|^SystemJS_|^invoke_[a-z]+$|^js_to_mono|^mono_to_js/i.test(name)) return "interop";
  if (/wrapper_(?:delegate|runtime_invoke|other|gsharedvt)|aot_wrapper_gsharedvt|^(?:_?mono_|ves_icall_|rgctx_|gsharedvt_|do_jit_call$|[a-z_]*trampoline_|llvm_|_*memcpy$|_*memmove$|_*memset$|malloc$|free$)/i.test(name)) return "dispatch-runtime-helpers";
  if (frame.wasm && /^(?:aot_instances_)?(?:SpaceBattle|SKECS)[._:]/.test(name)) return "managed-own-simulation";
  if (frame.wasm && /^(?:(?:corlib|aot_instances)_)?System[._]/i.test(name)) return "managed-library-compute";
  if (frame.wasm || !name || /^\((?:root|idle|program)\)$/.test(name)) return "unknown";
  return "other-js-renderer";
}

export function summarizeProfile(profile, { symbols = new Map(), requestedIntervalUs = null, top = 40 } = {}) {
  if (!Array.isArray(profile.nodes)) throw new Error("CPU profile nodes required");
  const nodes = new Map(), parents = new Map();
  for (const node of profile.nodes) {
    if (nodes.has(node.id)) throw new Error("Duplicate CPU profile node");
    nodes.set(node.id, node);
  }
  for (const node of nodes.values()) for (const child of node.children ?? []) {
    if (!nodes.has(child) || parents.has(child)) throw new Error("Invalid CPU profile tree");
    parents.set(child, node.id);
  }
  const moduleKey = frame => frame.url || `script:${frame.scriptId ?? "unknown"}`;
  const modules = new Set(profile.nodes.filter(node => isWasm(node.callFrame)).map(node => moduleKey(node.callFrame)));
  const soleModule = modules.size === 1 ? [...modules][0] : null;
  let opaqueMapUsed = false;
  const frames = new Map(profile.nodes.map(node => {
    const frame = node.callFrame, rawName = frame.functionName ?? "", index = wasmIndex(rawName);
    const wasm = isWasm(frame);
    // .NET 8 Mono has one executable native module; do not map foreign/multiple opaque modules by index.
    const opaque = /^(?:wasm:)/i.test(frame.url ?? "") || !frame.url;
    const canMap = nativeUrl(frame.url ?? "") || (opaque && moduleKey(frame) === soleModule);
    const mapped = index !== undefined && canMap ? symbols.get(Number(index)) : undefined;
    if (mapped && opaque) opaqueMapUsed = true;
    const name = mapped ?? rawName;
    const resolved = { nodeId: node.id, name, rawName, url: frame.url ?? "", scriptId: frame.scriptId,
      lineNumber: frame.lineNumber, columnNumber: frame.columnNumber, wasm,
      unresolved: wasm && (!name || wasmIndex(name) !== undefined),
      resolution: mapped ? "symbol-map" : name && index === undefined ? "chrome-name" : "unresolved" };
    resolved.category = classifyFrame(resolved);
    return [node.id, resolved];
  }));
  const self = new Map(), inclusive = new Map(), stacks = new Map(), categoryCounts = new Map(categories.map(c => [c, 0]));
  const samples = profile.samples ?? [];
  for (const id of samples) {
    if (!nodes.has(id)) throw new Error(`Sample references missing node ${id}`);
    self.set(id, (self.get(id) ?? 0) + 1);
    categoryCounts.set(frames.get(id).category, categoryCounts.get(frames.get(id).category) + 1);
    const stack = [], seen = new Set();
    let current = id;
    while (current !== undefined) {
      if (seen.has(current)) throw new Error("Cyclic CPU profile tree");
      seen.add(current); stack.unshift(current);
      inclusive.set(current, (inclusive.get(current) ?? 0) + 1);
      current = parents.get(current);
    }
    const key = stack.join(",");
    const item = stacks.get(key) ?? { nodeIds: stack, samples: 0 };
    item.samples++; stacks.set(key, item);
  }
  const percent = count => samples.length ? count * 100 / samples.length : 0;
  const rows = [...inclusive].map(([id, count]) => ({ ...frames.get(id), selfSamples: self.get(id) ?? 0,
    selfSamplePercent: percent(self.get(id) ?? 0), inclusiveSamples: count, inclusiveSamplePercent: percent(count) }));
  const wasmRows = rows.filter(row => row.wasm);
  const unresolved = wasmRows.filter(row => row.unresolved);
  const deltas = profile.timeDeltas;
  if (deltas && (deltas.length !== samples.length || deltas.some(d => !Number.isFinite(d) || d < 0))) throw new Error("Invalid timeDeltas");
  const sorted = deltas?.slice().sort((a, b) => a - b);
  const sum = deltas?.reduce((a, b) => a + b, 0) ?? null;
  const limitations = [
    "Profile-only sampling overhead: not a throughput benchmark or unprofiled tick timing.",
    "Self percentages count leaf samples only; inclusive caller percentages overlap and must never be summed as self.",
    "Advance(1) includes per-tick binary Snapshot/export, bridge interop, statistics, and existing simulation diagnostics.",
    "Automatic app RAF is disabled only in this profiling page; C# Advance(1)/binary export are unchanged. CDP/evaluate boundaries may still include JS/idle work.",
    "Name-based classification is conservative; unknown frames are never evidence of AOT execution. AOT site mode is not an interpreter-free hot-path guarantee.",
    "Sampling may miss short calls; inlining, generic sharing and missing native/library names limit attribution.",
    "Symbol maps must come from this exact final post-Binaryen build; maps cannot recover inlined frames or establish managed source lines.",
  ];
  if (!symbols.size) limitations.push("No symbol map loaded; only Chrome-provided names are available.");
  if (opaqueMapUsed) limitations.push("Symbol map applied to the sole observed opaque wasm module, assumed to be dotnet.native.wasm.");
  if (unresolved.length) limitations.push(`${unresolved.length} sampled wasm nodes remain unnamed; no AOT attribution made for them.`);
  if (!samples.length) limitations.push("No CPU samples captured.");
  if (!deltas) limitations.push("CDP omitted timeDeltas; observed sampling intervals unavailable.");
  const naming = !wasmRows.length ? "no sampled wasm frames" : unresolved.length ? "partial/unresolved wasm names" : "named sampled wasm frames";
  return { title: `Chrome CDP CPU profile: Mono AOT site (${naming})`, sampleCount: samples.length,
    durationUs: profile.endTime - profile.startTime, startTimeUs: profile.startTime, endTimeUs: profile.endTime,
    intervals: { requestedIntervalUs, count: deltas?.length ?? 0, totalDeltaUs: sum,
      minUs: sorted?.[0] ?? null, maxUs: sorted?.at(-1) ?? null, meanUs: sorted?.length ? sum / sorted.length : null,
      p50Us: sorted?.length ? sorted[Math.floor((sorted.length - 1) * .5)] : null,
      p95Us: sorted?.length ? sorted[Math.ceil((sorted.length - 1) * .95)] : null,
      unsampledTailUs: sum === null ? null : profile.endTime - profile.startTime - sum },
    symbolization: { mapEntries: symbols.size, sampledWasmNodes: wasmRows.length, unresolvedWasmNodes: unresolved.length,
      mappedNodes: wasmRows.filter(row => row.resolution === "symbol-map").length, opaqueSingleModuleAssumption: opaqueMapUsed },
    selfCategories: [...categoryCounts].map(([category, count]) => ({ category, selfSamples: count, selfSamplePercent: percent(count) })),
    topLeaves: rows.filter(row => row.selfSamples).sort((a, b) => b.selfSamples - a.selfSamples).slice(0, top),
    topCallersInclusive: rows.filter(row => nodes.get(row.nodeId).children?.length).sort((a, b) => b.inclusiveSamples - a.inclusiveSamples).slice(0, top),
    topStackPaths: [...stacks.values()].sort((a, b) => b.samples - a.samples).slice(0, top)
      .map(item => ({ samples: item.samples, samplePercent: percent(item.samples), path: item.nodeIds.map(id => frames.get(id)) })),
    limitations };
}

export function parseArgs(args) {
  const options = { profile: "nominal", seed: 7331, startTick: 0, ticks: 1200, warmupTicks: 1200, intervalUs: 1000, top: 40, channel: "chrome" };
  const names = { "--site": "site", "--output": "output", "--profile": "profile", "--seed": "seed", "--start-tick": "startTick", "--ticks": "ticks", "--warmup-ticks": "warmupTicks", "--interval-us": "intervalUs", "--symbol-map": "symbolMap", "--channel": "channel", "--top": "top" };
  for (let i = 0; i < args.length; i++) {
    if (args[i] === "--help") { options.help = true; continue; }
    const key = names[args[i]], value = args[++i];
    if (!key || !value || value.startsWith("--")) throw new Error("Unknown option or missing value");
    options[key] = typeof options[key] === "number" ? Number(value) : value;
  }
  if (options.help) return options;
  if (!options.site) throw new Error("--site must point to an existing published wwwroot; no builds are performed");
  for (const key of ["seed", "startTick", "ticks", "warmupTicks", "intervalUs", "top"]) {
    if (!Number.isSafeInteger(options[key]) || options[key] < (["ticks", "intervalUs", "top"].includes(key) ? 1 : 0)) throw new Error(`Invalid ${key}`);
  }
  if (options.seed > 0xffffffff || options.startTick + options.ticks > 0x7fffffff || options.warmupTicks > 18000) throw new Error("Tick/seed limits exceeded");
  if (!["low", "nominal", "high"].includes(options.profile) || !["chrome", "chromium"].includes(options.channel)) throw new Error("Invalid profile/channel");
  return options;
}

export function checkOutputPath(output) {
  const result = path.resolve(output), relative = path.relative(tmpRoot, result);
  if (!relative || relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) throw new Error("Output must be a fresh directory inside Benchmarks.Local/tmp");
  return result;
}

async function freshOutput(output) {
  const result = checkOutputPath(output);
  let current = projectRoot;
  for (const part of path.relative(projectRoot, result).split(path.sep)) {
    current = path.join(current, part);
    const stat = await fs.lstat(current).catch(error => { if (error.code !== "ENOENT") throw error; return null; });
    if (stat?.isSymbolicLink()) throw new Error(`Output ancestor is a symlink/junction: ${current}`);
    if (current === result && stat) throw new Error("Output already exists; use a fresh directory");
  }
  await promisify(execFile)("git", ["check-ignore", "--quiet", "--no-index", "--", path.relative(projectRoot, result)], { cwd: projectRoot });
  await fs.mkdir(path.dirname(result), { recursive: true });
  await fs.mkdir(result);
  return result;
}

async function serveSite(site) {
  const mime = { ".html": "text/html", ".js": "text/javascript", ".json": "application/json", ".wasm": "application/wasm", ".css": "text/css", ".png": "image/png" };
  const server = http.createServer(async (request, response) => {
    try {
      if (!["GET", "HEAD"].includes(request.method)) { response.writeHead(405).end(); return; }
      const pathname = decodeURIComponent(new URL(request.url, "http://local").pathname);
      const file = await fs.realpath(path.resolve(site, pathname.slice(1) || "index.html"));
      const relative = path.relative(site, file);
      if (!relative || relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) { response.writeHead(403).end(); return; }
      const data = await fs.readFile(file);
      response.writeHead(200, { "Content-Type": mime[path.extname(file)] ?? "application/octet-stream", "Cache-Control": "no-store" });
      response.end(request.method === "HEAD" ? undefined : data);
    } catch { response.writeHead(404).end(); }
  });
  await new Promise((resolve, reject) => { server.once("error", reject); server.listen(0, "127.0.0.1", resolve); });
  return { server, url: `http://127.0.0.1:${server.address().port}/` };
}

export async function prepareBattle({ profile, seed, warmupTicks, startTick }) {
  window.spaceBattle.pause(true);
  const runtime = globalThis.getDotnetRuntime(0);
  const exports = await runtime.getAssemblyExports("SpaceBattle.Web.dll");
  const bridge = exports.SpaceBattle.Web.BattleBridge;
  if (bridge.Mode() !== "aot-binary") throw new Error("Production Mono AOT bridge required");
  const readTick = pointer => runtime.localHeapViewF64()[pointer / 8 + 1];
  const advance = count => {
    let pointer;
    for (let remaining = count; remaining > 0;) {
      const batch = Math.min(600, remaining); pointer = bridge.Advance(batch); remaining -= batch;
    }
    return pointer;
  };
  bridge.DebugColliders(false);
  bridge.Reset(profile, seed);
  advance(warmupTicks);
  let pointer = bridge.Reset(profile, seed);
  if (readTick(pointer) !== 0) throw new Error("Reset did not return tick zero");
  if (startTick) pointer = advance(startTick);
  if (readTick(pointer) !== startTick) throw new Error("Unexpected start tick");
  window.__monoCpuProfile = { runtime, bridge };
  return { runtimeMode: bridge.Mode(), startTick: readTick(pointer), userAgent: navigator.userAgent };
}

export function advanceWindow({ startTick, ticks }) {
  const { runtime, bridge } = window.__monoCpuProfile;
  let pointer;
  // One synchronous task prevents renderer work between the gameplay-shaped per-tick exports.
  for (let tick = 0; tick < ticks; tick++) pointer = bridge.Advance(1);
  const endTick = runtime.localHeapViewF64()[pointer / 8 + 1];
  if (endTick !== startTick + ticks) throw new Error(`Unexpected end tick ${endTick}`);
  return { endTick, advanceCalls: ticks, ticksPerAdvance: 1 };
}

export async function captureWindow(page, cdp, options) {
  await cdp.send("Profiler.enable");
  try {
    await cdp.send("Profiler.setSamplingInterval", { interval: options.intervalUs });
    await cdp.send("Profiler.start");
    let windowResult;
    let profile;
    try { windowResult = await page.evaluate(advanceWindow, options); }
    finally { ({ profile } = await cdp.send("Profiler.stop")); }
    return { profile, windowResult };
  } finally { await cdp.send("Profiler.disable"); }
}

export async function main(args = process.argv.slice(2)) {
  const options = parseArgs(args);
  if (options.help) {
    console.log("Usage: node tools/web/profile-mono-cpu.mjs --site <published-wwwroot> [--symbol-map <final-dotnet.native.js.symbols>] [--output <fresh-Benchmarks.Local/tmp/dir>] [--start-tick 0] [--ticks 1200] [--warmup-ticks 1200] [--profile nominal] [--seed 7331] [--interval-us 1000] [--channel chrome|chromium] [--top 40]\nNo builds. CPU profile only, synchronous Advance(1), not throughput. Warmup/reset/seek are outside capture. Writes profile.cpuprofile and summary.json into a fresh ignored directory.");
    return;
  }
  const site = await fs.realpath(path.resolve(options.site));
  const boot = JSON.parse(await fs.readFile(path.join(site, "_framework/blazor.boot.json"), "utf8"));
  const runtimeAssets = Object.keys(boot.resources?.jsModuleRuntime ?? {});
  if (!runtimeAssets.some(name => /^dotnet\.runtime\.8\./.test(name)) || !Object.keys(boot.resources?.wasmNative ?? {}).some(nativeUrl)) throw new Error("Stable .NET 8 Mono native site required");
  let symbolPath = options.symbolMap ? path.resolve(options.symbolMap) : null;
  if (!symbolPath) for (const candidate of ["dotnet.native.js.symbols", "dotnet.native.wasm.symbols"]) {
    const file = path.join(site, "_framework", candidate);
    if (await fs.stat(file).then(s => s.isFile(), () => false)) { symbolPath = file; break; }
  }
  const symbolText = symbolPath ? await fs.readFile(symbolPath, "utf8") : null;
  const symbols = symbolText === null ? new Map() : parseSymbolMap(symbolText);
  const output = await freshOutput(options.output ?? path.join(tmpRoot, `mono-cpu-${new Date().toISOString().replace(/[:.]/g, "-")}-${randomUUID()}`));
  let server, browser;
  const oldTemp = Object.fromEntries(["TMP", "TEMP", "TMPDIR"].map(key => [key, process.env[key]]));
  try {
    const local = await serveSite(site); server = local.server;
    const { chromium } = await import("playwright");
    // Keep Playwright and Chrome temporary writes inside the project as well.
    for (const key of Object.keys(oldTemp)) process.env[key] = output;
    browser = await chromium.launch({ headless: true, ...(options.channel === "chrome" ? { channel: "chrome" } : {}),
      args: ["--disable-background-networking", "--disable-component-update", "--no-first-run"] });
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    const appSource = await fs.readFile(path.join(site, "app.js"), "utf8");
    if (appSource.split("requestAnimationFrame(frame);").length !== 3) throw new Error("Profiling RAF marker changed");
    const appServed = appSource.replaceAll("requestAnimationFrame(frame);", "/* RAF disabled in CPU-profile-only page. */");
    await page.route("**/*", route => new URL(route.request().url()).origin === new URL(local.url).origin ? route.continue() : route.abort());
    await page.route("**/app.js*", route => route.fulfill({ body: appServed, contentType: "text/javascript" }));
    const errors = []; page.on("pageerror", error => errors.push(String(error)));
    await page.goto(`${local.url}?test=1`, { waitUntil: "networkidle", timeout: 120000 });
    await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
    const prepared = await page.evaluate(prepareBattle, options);
    if (errors.length) throw new Error(errors.join("\n"));
    const cdp = await page.context().newCDPSession(page);
    const { profile, windowResult } = await captureWindow(page, cdp, options);
    const rawPath = path.join(output, "profile.cpuprofile"), summaryPath = path.join(output, "summary.json");
    await fs.writeFile(rawPath, JSON.stringify(profile), { flag: "wx" });
    const checksum = await page.evaluate(() => window.__monoCpuProfile.bridge.Validate());
    const parsed = summarizeProfile(profile, { symbols, requestedIntervalUs: options.intervalUs, top: options.top });
    const summary = { ...parsed, capturedAt: new Date().toISOString(), site, bootRuntimeAssets: runtimeAssets,
      nativeWasmAssets: boot.resources.wasmNative, browserVersion: browser.version(), options,
      window: { ...prepared, ...windowResult, firstAdvancedTick: options.startTick + 1, checksum, warmupTicks: options.warmupTicks, resetAfterWarmup: true },
      symbolMap: { path: symbolPath, sha256: symbolText === null ? null : createHash("sha256").update(symbolText).digest("hex") },
      pageErrors: errors, files: [rawPath, summaryPath] };
    summary.originalAppSha256 = createHash("sha256").update(appSource).digest("hex");
    summary.servedAppSha256 = createHash("sha256").update(appServed).digest("hex");
    await fs.writeFile(summaryPath, JSON.stringify(summary, null, 2), { flag: "wx" });
    console.log(JSON.stringify({ title: summary.title, sampleCount: summary.sampleCount, window: summary.window, files: summary.files }, null, 2));
    if (errors.length) throw new Error("Page errors saved in summary; capture is not clean");
  } finally {
    try { await browser?.close(); }
    finally {
      for (const [key, value] of Object.entries(oldTemp)) { if (value === undefined) delete process.env[key]; else process.env[key] = value; }
      if (server) await new Promise(resolve => server.close(resolve));
    }
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => { console.error(error.stack ?? String(error)); process.exitCode = 1; });
}
