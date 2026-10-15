import test from "node:test";
import assert from "node:assert/strict";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseSymbolMap, classifyFrame, summarizeProfile, parseArgs, checkOutputPath, prepareBattle, advanceWindow, captureWindow } from "./profile-mono-cpu.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const frame = (functionName, url = "wasm://wasm/native") => ({ functionName, url, scriptId: "3", lineNumber: 0, columnNumber: 0 });
const profile = (names, samples = [3, 3, 2, 4], deltas = [10, 20, 30, 40]) => ({
  startTime: 0, endTime: 120, samples, timeDeltas: deltas,
  nodes: [{ id: 1, callFrame: frame("(root)", ""), children: [2, 4] },
    { id: 2, callFrame: frame(names[0]), children: [3] },
    { id: 3, callFrame: frame(names[1]) }, { id: 4, callFrame: frame(names[2]) }],
});

test("index:name map preserves colons, CRLF, BOM, rejects invalid/conflicting entries", () => {
  assert.deepEqual([...parseSymbolMap("\uFEFF2:SpaceBattle.Battle:Step()\r\n3:SKECS_Query_1_ForEach\n")], [[2, "SpaceBattle.Battle:Step()"], [3, "SKECS_Query_1_ForEach"]]);
  assert.throws(() => parseSymbolMap("2:a\n2:b"));
  assert.throws(() => parseSymbolMap("n:a"));
  assert.throws(() => parseSymbolMap(""));
});

test("known named frames classify conservatively, wrappers override own namespace", () => {
  for (const [name, expected] of [
    ["SpaceBattle_Battle_1_Step", "managed-own-simulation"], ["SKECS_Query_1_ForEach", "managed-own-simulation"],
    ["mono_interp_exec_method", "mono-interpreter"], ["interp_exec_method", "mono-interpreter"],
    ["mono_interp_jit_wasm_entry_trampoline", "dispatch-runtime-helpers"],
    ["SpaceBattle_wrapper_delegate_invoke_System_Action", "dispatch-runtime-helpers"],
    ["mono_runtime_invoke", "dispatch-runtime-helpers"], ["mono_rgctx_method", "dispatch-runtime-helpers"],
    ["System_Collections_Generic_List_1_Add", "managed-library-compute"],
    ["sgen_collect_major", "gc"], ["mono_gc_collect", "gc"],
    ["mono_wasm_invoke_bound_function", "interop"], ["SpaceBattle_wrapper_managed_to_native_foo", "interop"],
    ["memcpy", "dispatch-runtime-helpers"], ["do_jit_call", "dispatch-runtime-helpers"],
    ["aot_instances_aot_wrapper_gsharedvt_out_sig", "dispatch-runtime-helpers"],
    ["aot_instances_SpaceBattle_NativeStore_Alive", "managed-own-simulation"],
    ["corlib_System_Collections_Generic_List_MoveNext", "managed-library-compute"],
    ["unrecognized_native", "unknown"], ["wasm-function[17]", "unknown"],
  ]) assert.equal(classifyFrame({ name, wasm: true }), expected, name);
  assert.equal(classifyFrame({ name: "draw", wasm: false }), "other-js-renderer");
  assert.equal(classifyFrame({ name: "", wasm: false }), "unknown");
});

test("self samples partition, inclusive callers overlap, stack paths retain leaf/caller names", () => {
  const result = summarizeProfile(profile(["SpaceBattle_Battle_Step", "SKECS_Query_ForEach", "mono_interp_exec_method"]), { requestedIntervalUs: 1000 });
  const own = result.selfCategories.find(row => row.category === "managed-own-simulation");
  assert.equal(own.selfSamples, 3); assert.equal(own.selfSamplePercent, 75);
  assert.equal(result.selfCategories.reduce((sum, row) => sum + row.selfSamples, 0), 4);
  assert.equal(result.topLeaves[0].name, "SKECS_Query_ForEach");
  const caller = result.topCallersInclusive.find(row => row.name === "SpaceBattle_Battle_Step");
  assert.equal(caller.selfSamples, 1); assert.equal(caller.inclusiveSamples, 3);
  assert.equal(caller.selfSamplePercent, 25); assert.equal(caller.inclusiveSamplePercent, 75);
  assert.deepEqual(result.topStackPaths[0].path.map(row => row.name), ["(root)", "SpaceBattle_Battle_Step", "SKECS_Query_ForEach"]);
  assert.equal(result.intervals.meanUs, 25); assert.equal(result.intervals.unsampledTailUs, 20);
  assert.match(result.title, /named sampled wasm/);
});

test("unnamed wasm stays unknown; exact index map resolves sole opaque native module", () => {
  const raw = profile(["wasm-function[2]", "wasm-function[3]:0x10", "wasm-function[4]"]);
  const unnamed = summarizeProfile(raw);
  assert.equal(unnamed.selfCategories.find(row => row.category === "unknown").selfSamples, 4);
  assert.match(unnamed.title, /partial\/unresolved/);
  const named = summarizeProfile(raw, { symbols: parseSymbolMap("2:SpaceBattle_Battle_Step\n3:SKECS_Query_ForEach\n4:mono_interp_exec_method") });
  assert.equal(named.symbolization.mappedNodes, 3);
  assert.equal(named.symbolization.opaqueSingleModuleAssumption, true);
  assert.equal(named.selfCategories.find(row => row.category === "mono-interpreter").selfSamples, 1);
  assert.equal(raw.nodes[1].callFrame.functionName, "wasm-function[2]");
});

test("no guessing indices across modules or foreign named WASM modules", () => {
  const raw = profile(["wasm-function[2]", "wasm-function[3]", "wasm-function[4]"]);
  raw.nodes[1].callFrame.url = "http://local/_framework/dotnet.native.wasm";
  raw.nodes[2].callFrame.url = "wasm://wasm/foreign";
  raw.nodes[3].callFrame.url = "http://local/_framework/foreign.wasm";
  const result = summarizeProfile(raw, { symbols: parseSymbolMap("2:SpaceBattle_Battle_Step\n3:SKECS_Query_ForEach\n4:SKECS_Query_ForEach") });
  assert.equal(result.symbolization.mappedNodes, 1);
  assert.equal(result.symbolization.unresolvedWasmNodes, 2);
});

test("no samples does not promote hitCount to samples; missing deltas explicit; invalid trees reject", () => {
  const raw = profile(["a", "b", "c"], [], []); raw.nodes[2].hitCount = 99;
  const result = summarizeProfile(raw); assert.equal(result.sampleCount, 0); assert.deepEqual(result.topLeaves, []);
  delete raw.timeDeltas; assert.equal(summarizeProfile(raw).intervals.meanUs, null);
  assert.throws(() => summarizeProfile(profile(["a", "b", "c"], [99], [1])), /missing node/);
  assert.throws(() => summarizeProfile(profile(["a", "b", "c"], [3], [1, 2])), /timeDeltas/);
  const cyclic = profile(["a", "b", "c"]); cyclic.nodes[2].children = [1];
  assert.throws(() => summarizeProfile(cyclic), /Cyclic/);
});

test("CLI defaults match requested gameplay window; output confined to ignored tmp subtree", () => {
  const options = parseArgs(["--site", "example", "--start-tick", "1200", "--ticks", "1200"]);
  assert.equal(options.warmupTicks, 1200); assert.equal(options.startTick, 1200);
  assert.equal(options.channel, "chrome");
  assert.throws(() => parseArgs(["--site", "x", "--ticks", "0"]));
  assert.throws(() => parseArgs(["--site", "x", "--seed", "4294967296"]));
  assert.throws(() => parseArgs(["--site", "x", "--wat", "1"]));
  assert.throws(() => parseArgs(["--site"]));
  assert.throws(() => checkOutputPath(path.join(root, "artifacts/capture")));
  assert.throws(() => checkOutputPath(path.join(root, "Benchmarks.Local/tmp")));
  assert.equal(checkOutputPath(path.join(root, "Benchmarks.Local/tmp/fresh")), path.join(root, "Benchmarks.Local/tmp/fresh"));
});

test("mock battle warms independently then resets, seeks outside capture, profiles Advance(1)", async () => {
  const old = { window: globalThis.window, getDotnetRuntime: globalThis.getDotnetRuntime };
  let tick = 0; const calls = [];
  const bridge = { Mode: () => "aot-binary", DebugColliders: enabled => calls.push(["colliders", enabled]),
    Reset: (...args) => { calls.push(["reset", ...args]); tick = 0; return 0; },
    Advance: n => { calls.push(["advance", n]); tick += n; return 0; } };
  globalThis.window = { spaceBattle: { pause: value => calls.push(["pause", value]) } };
  globalThis.getDotnetRuntime = () => ({ getAssemblyExports: async () => ({ SpaceBattle: { Web: { BattleBridge: bridge } } }), localHeapViewF64: () => [0, tick] });
  try {
    await prepareBattle({ profile: "nominal", seed: 7331, warmupTicks: 1200, startTick: 3 });
    assert.deepEqual(calls.filter(call => call[0] !== "pause" && call[0] !== "colliders"), [["reset", "nominal", 7331], ["advance", 600], ["advance", 600], ["reset", "nominal", 7331], ["advance", 3]]);
    const result = advanceWindow({ startTick: 3, ticks: 4 });
    assert.equal(result.endTick, 7); assert.equal(result.advanceCalls, 4);
    assert.deepEqual(calls.slice(-4), Array.from({ length: 4 }, () => ["advance", 1]));
  } finally {
    for (const [key, value] of Object.entries(old)) { if (value === undefined) delete globalThis[key]; else globalThis[key] = value; }
  }
});

test("CDP ordering excludes warm/reset/validation and always stops on evaluation failure", async () => {
  const calls = [], raw = profile(["a", "b", "c"]);
  const cdp = { send: async (method, parameters) => { calls.push([method, parameters]); return method === "Profiler.stop" ? { profile: raw } : {}; } };
  const page = { evaluate: async (callback, options) => { assert.equal(callback, advanceWindow); calls.push(["advance-window", options.ticks]); return { endTick: 1200 }; } };
  const result = await captureWindow(page, cdp, { intervalUs: 1000, ticks: 1200 });
  assert.equal(result.profile, raw);
  assert.deepEqual(calls.map(call => call[0]), ["Profiler.enable", "Profiler.setSamplingInterval", "Profiler.start", "advance-window", "Profiler.stop", "Profiler.disable"]);
  assert.deepEqual(calls[1][1], { interval: 1000 });
  calls.length = 0;
  await assert.rejects(captureWindow({ evaluate: async () => { throw new Error("boom"); } }, cdp, { intervalUs: 1000 }), /boom/);
  assert.deepEqual(calls.slice(-2).map(call => call[0]), ["Profiler.stop", "Profiler.disable"]);
});
