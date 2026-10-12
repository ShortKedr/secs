import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { test, after } from "node:test";
import { randomUUID } from "node:crypto";
import {
  PROJECT_ROOT, PROFILES, HELP, parseArgs, inside, outputPath, variantOrder, makePlan,
  orderBalance, stats, readHeader, stateFromHeader, compareTrajectories, summarizePhases,
  independentRunMedians, injectHooks, assertNoLinks, reserveOutput, hashTree,
  runtimeVersions, replayInPage,
} from "./mono-compiler-study.mjs";

// Fixtures/results are confined to a NEW ignored study directory; no real replay.
const directory = outputPath(PROJECT_ROOT, `Benchmarks.Local/tmp/mono-study-unit-${randomUUID()}`);
await reserveOutput(PROJECT_ROOT, directory);
const results = [];
function unit(name, body) {
  test(name, async context => {
    try { const detail = await body(context); results.push({ name, passed: true, ...detail }); }
    catch (error) { results.push({ name, passed: false, error: String(error) }); throw error; }
  });
}
after(async () => {
  await fs.writeFile(path.join(directory, "acceptance.json"), JSON.stringify({
    scope: "Unit/fake-bridge checks only. No browser launched, build, network access, or real performance measurement.",
    passed: results.every(result => result.passed), testCount: results.length,
    skipped: results.filter(result => result.skipped).length, results,
    invocation: "node --test tools/web/test-mono-compiler-study.mjs",
  }, null, 2) + "\n", { flag: "wx" });
  console.log(`Acceptance result: ${path.join(directory, "acceptance.json")}`);
});

const argv = ["--sites", "mono-study-20261012/A/publish/wwwroot", "mono-study-20261012/B/publish/wwwroot",
  "mono-study-20261012/C/publish/wwwroot", "mono-study-20261012/D/publish/wwwroot", "--labels", "A", "B", "C", "D"];
const config = parseArgs(argv);

unit("CLI defaults implement requested primary and separate frontend windows", () => {
  assert.deepEqual(config.profiles, PROFILES);
  assert.equal(config.repeats, 3);
  assert.equal(config.ticks, 1200);
  assert.equal(config.phaseTicks, 300);
  assert.equal(config.warmupTicks, 1200);
  assert.equal(config.frontendStartTick, 300);
  assert.equal(config.frontendTicks, 300);
  assert.equal(config.sourceRoots[0], path.join(PROJECT_ROOT, "mono-study-20261012/A"));
  const frozen = path.join(PROJECT_ROOT, "frozen");
  const parsed = parseArgs([...argv, "--source-roots", ...Array(4).fill(frozen), "--frontend-start-tick", "120", "--frontend-ticks", "180"]);
  assert.deepEqual(parsed.sourceRoots, Array(4).fill(frozen));
  assert.equal(parsed.frontendStartTick + parsed.frontendTicks, 300);
});

unit("CLI comma lists, custom ticks, zero warmup and help are import-safe", () => {
  const parsed = parseArgs(["--sites", "a,b", "--labels", "A,B", "--ticks", "9", "--phase-ticks", "4", "--warmup-ticks", "0", "--start-tick", "7"]);
  assert.equal(parsed.ticks, 9);
  assert.equal(parsed.phaseTicks, 4);
  assert.equal(parsed.warmupTicks, 0);
  assert.equal(parsed.startTick, 7);
  assert.deepEqual(parseArgs(["--help"]), { help: true });
  assert.match(HELP, /OS file caches/);
  assert.match(HELP, /global percentiles/);
});

unit("CLI refuses bad/duplicate arguments, labels, profiles and repeat counts", () => {
  for (const extra of [["--repeats", "2"], ["--ticks", "0"], ["--warmup-ticks", "-1"], ["--seed", "4294967296"],
    ["--ticks", "1.5"], ["--ticks", "NaN"], ["--ticks", ""], ["--ticks", "3", "--ticks", "4"],
    ["--bogus", "x"], ["--profile", "nominal"], ["--ticks"], ["--output", "outside.json"]])
    assert.throws(() => parseArgs([...argv, ...extra]));
  for (const args of [["--sites", "a,a", "--labels", "A,B"], ["--sites", "a,b", "--labels", "A,A"],
    ["--sites", "a,b", "--labels", "A,E"], ["--sites", "a,b,", "--labels", "A,B"],
    ["--sites", "a", "--labels", "A"], ["--sites", "../a,b", "--labels", "A,B"],
    [...argv, "--source-roots", "a,b"]]) assert.throws(() => parseArgs(args));
});

unit("four-variant order rotates and two variants alternate", () => {
  assert.deepEqual(variantOrder(4, 0), [0, 1, 2, 3]);
  assert.deepEqual(variantOrder(4, 1), [1, 2, 3, 0]);
  assert.deepEqual(variantOrder(4, 2), [2, 3, 0, 1]);
  assert.deepEqual(variantOrder(4, 3), [3, 0, 1, 2]);
  assert.deepEqual(variantOrder(2, 1), [1, 0]);
  for (const pair of [[1, 0], [5, 0], [4, -1], [4, .1]]) assert.throws(() => variantOrder(...pair));
});

unit("plan has exactly three independent repeats per variant/profile/mode, bounded imbalance", () => {
  const plan = makePlan(config);
  assert.equal(plan.length, 4 * 3 * 3 * 2);
  const coverage = new Map();
  for (const trial of plan) {
    const key = `${trial.label}/${trial.profile}/${trial.mode}`;
    coverage.set(key, [...(coverage.get(key) ?? []), trial.repeat]);
    assert.equal(trial.ordinal, plan.indexOf(trial));
    assert.equal(trial.startTick, trial.mode === "core" ? 0 : 300);
    assert.equal(trial.ticks, trial.mode === "core" ? 1200 : 300);
  }
  for (const repeats of coverage.values()) assert.deepEqual(repeats, [1, 2, 3]);
  for (const group of Object.values(orderBalance(plan))) for (const counts of Object.values(group)) {
    assert.equal(counts.reduce((a, b) => a + b), 3);
    assert.ok(Math.max(...counts) - Math.min(...counts) <= 1);
  }
  for (const group of Object.values(orderBalance(makePlan({ ...config, repeats: 4 }))))
    for (const counts of Object.values(group)) assert.deepEqual(counts, [1, 1, 1, 1]);
});

unit("A/B/C plan works without unsupported D and has exact three-repeat position balance", () => {
  const parsed = parseArgs(["--sites", ...config.sites.slice(0, 3), "--labels", "A", "B", "C"]);
  const plan = makePlan(parsed);
  assert.equal(plan.length, 54);
  assert.ok(plan.every(trial => trial.label !== "D"));
  for (const group of Object.values(orderBalance(plan)))
    for (const counts of Object.values(group)) assert.deepEqual(counts, [1, 1, 1]);
});

unit("explicit core-only confirmation plan keeps independent coverage and rejects invalid modes", () => {
  const config = parseArgs(argv);
  config.modes = ["core"];
  config.profiles = ["low", "high"];
  const plan = makePlan(config);
  assert.equal(plan.length, 24);
  assert.ok(plan.every(trial => trial.mode === "core" && ["low", "high"].includes(trial.profile)));
  for (const modes of [[], ["bad"], ["core", "core"]]) assert.throws(() => makePlan({ ...config, modes }));
});

unit("nearest-rank p95/p99, mean and even/odd medians are defined", () => {
  assert.deepEqual(stats([9]), { count: 1, mean: 9, median: 9, p95: 9, p99: 9, min: 9, max: 9 });
  assert.equal(stats([1, 100, 3, 2]).median, 2.5);
  const values = Array.from({ length: 100 }, (_, i) => i + 1);
  const original = [...values];
  assert.equal(stats(values).p95, 95);
  assert.equal(stats(values).p99, 99);
  assert.deepEqual(values, original);
  for (const values of [[], [NaN], [Infinity], [1, undefined]]) assert.throws(() => stats(values));
});

function fakeHeader(tick = 0) {
  const h = Array(80).fill(0);
  Object.assign(h, { 0: 3, 1: tick, 2: 100, 3: 7331, 4: 1, 5: .25,
    6: 1, 10: 1, 14: tick, 30: 1, 31: 768, 37: 1, 38: 2048, 39: 512,
    42: 1, 43: 1, 44: 4096, 45: .125, 58: tick * 2, 70: 1, 72: 1 });
  return h;
}
unit("binary v3 pointer/header guards preserve Step in header[5]", () => {
  const heap = new Float64Array(160);
  heap.set(fakeHeader(7), 8);
  const h = readHeader(heap, 64);
  assert.equal(h[5], .25);
  assert.equal(stateFromHeader(h).tick, 7);
  for (const pointer of [-8, 1, 0.5, Infinity, 800]) assert.throws(() => readHeader(heap, pointer));
  for (const [field, bad] of [[0, 2], [30, -1], [30, .5], [31, 640]]) {
    heap.set(fakeHeader(), 8); heap[8 + field] = bad;
    assert.throws(() => readHeader(heap, 64));
  }
  const bad = fakeHeader(); bad[70] = NaN;
  assert.throws(() => stateFromHeader(bad));
});

function fakeRun(label = "A", repeat = 1, mode = "core") {
  return { trial: { label, repeat, mode, profile: "nominal", startTick: 0, ticks: 3 },
    revisions: { Revision: "r", FleetRevision: "f", PhysicsRevision: "p" },
    checkpoints: [0, 3].map(tick => ({ state: stateFromHeader(fakeHeader(tick)), checksum: `sum${tick}`,
      shipTactics: [{ serial: 1, fields: [0, 2, 3, 4, tick] }], reinforcements: [[0, tick], [1, tick]] })),
    samples: [1, 2, 3].map(tick => ({ state: stateFromHeader(fakeHeader(tick)), timing: { stepMs: tick, exportMs: .1 }, memory: { managed: tick } })) };
}

unit("equivalence compares variants/repeats and ignores timing/memory", () => {
  const runs = [fakeRun(), fakeRun("B"), fakeRun("A", 2), fakeRun("A", 1, "frontend")];
  runs[1].samples[0].timing.stepMs = 999;
  runs[1].samples[0].memory.managed = 999;
  const result = compareTrajectories(runs);
  assert.equal(result.equivalent, true);
  assert.ok(result.comparisons > 0);
});

unit("equivalence rejects identical-checksum counter/count/population/tactic drift at ANY tick", () => {
  for (const field of ["counts", "counters", "populations", "tactics", "renderCounts"]) {
    const changed = fakeRun("B");
    changed.samples[1].state[field][0]++;
    const result = compareTrajectories([fakeRun(), changed]);
    assert.equal(result.equivalent, false);
    assert.ok(result.mismatches.some(diff => diff.field.includes(field)));
  }
});

unit("equivalence rejects checkpoints, ship tactics, checksum, revisions and missing ticks", () => {
  for (const change of [r => r.checkpoints[1].checksum = "wrong", r => r.checkpoints[1].shipTactics[0].fields[2]++,
    r => r.checkpoints[1].reinforcements[0][1]++, r => r.checkpoints[1].state.counters[0]++,
    r => r.revisions.PhysicsRevision = "changed", r => r.samples.pop()]) {
    const changed = fakeRun("B"); change(changed);
    assert.equal(compareTrajectories([fakeRun(), changed]).equivalent, false);
  }
});

unit("cross-mode overlapping trajectories/checksums are checked after independent resets", () => {
  const core = fakeRun();
  const frontend = fakeRun("B", 1, "frontend");
  frontend.samples = frontend.samples.slice(1);
  assert.equal(compareTrajectories([core, frontend]).equivalent, true);
  frontend.samples[0].state.counters[0]++;
  const result = compareTrajectories([core, frontend]);
  assert.equal(result.equivalent, false);
  assert.ok(result.mismatches.some(diff => diff.location === "crossMode.tick2"));
});

unit("coverage guard catches missing or duplicate independent replays", () => {
  const runs = [fakeRun(), fakeRun("B")];
  const plan = runs.map(r => r.trial);
  assert.equal(compareTrajectories(runs, plan).equivalent, true);
  assert.equal(compareTrajectories(runs.slice(1), plan).equivalent, false);
  assert.equal(compareTrajectories([runs[0], runs[0]], plan).equivalent, false);
  assert.equal(compareTrajectories([]).equivalent, false);
});

unit("phase stats include short tails and aggregation is independent-run median, never pooled", () => {
  const runs = [fakeRun(), fakeRun("A", 2), fakeRun("A", 3)];
  for (let index = 0; index < runs.length; index++) {
    for (const sample of runs[index].samples) sample.timing.stepMs *= [1, 2, 100][index];
    runs[index].phases = summarizePhases(runs[index], 2);
    assert.deepEqual(runs[index].phases.map(p => [p.startTick, p.endTick]), [[1, 2], [3, 3]]);
  }
  const aggregation = independentRunMedians(runs);
  assert.equal(aggregation.length, 2);
  assert.equal(aggregation[0].mediansAcrossIndependentRuns.stepMs.mean, 3);
  assert.equal(aggregation[0].mediansAcrossIndependentRuns.stepMs.p95, 4);
  assert.equal(aggregation[0].repeats.length, 3);
  assert.match(aggregation[0].note, /NOT pooled/);
});

unit("in-memory hooks match current production app and do not edit it", async () => {
  const file = path.join(PROJECT_ROOT, "SpaceBattle.Web/wwwroot/app.js");
  const original = await fs.readFile(file, "utf8");
  const injected = injectHooks(original);
  assert.notEqual(injected, original);
  assert.match(injected, /runtimeReadyMs = performance.now/);
  assert.match(injected, /appReadyMs = performance.now/);
  assert.match(injected, /__monoCompilerStudy.header/);
  assert.equal(await fs.readFile(file, "utf8"), original);
  assert.throws(() => injectHooks(injected));
  assert.throws(() => injectHooks(original + "\n  window.spaceBattle = {"));
  assert.throws(() => injectHooks(original.replace("runtimeStarted = true;", "runtimeStarted = false;")));
});

unit("runtime version is explicit boot provenance, not a compiler label guess", () => {
  const version = runtimeVersions({ resources: { hash: "boot-hash", jsModuleRuntime: { "dotnet.runtime.8.0.31.abc.js": "hash" } } });
  assert.deepEqual(version.versions, ["8.0.31"]);
  assert.equal(version.bootResourceHash, "boot-hash");
  assert.throws(() => runtimeVersions({ resources: {} }));
});

unit("output guards restrict all artifacts to NEW project-local ignored study dirs", async () => {
  for (const value of ["Benchmarks.Local/tmp", "Benchmarks.Local/tmp/../outside", "Benchmarks.Local/tmp/new/nested",
    "tools/web/result.json", "../elsewhere", "Benchmarks.Local/tmp/.hidden", "Benchmarks.Local/tmp/a:b"])
    assert.throws(() => outputPath(PROJECT_ROOT, value));
  assert.equal(inside(PROJECT_ROOT, directory), true);
  assert.equal(inside(PROJECT_ROOT, PROJECT_ROOT + "-other/file"), false);
  await assert.rejects(reserveOutput(PROJECT_ROOT, directory), { code: "EEXIST" });
  const file = path.join(directory, "exclusive.json");
  await fs.writeFile(file, "original", { flag: "wx" });
  await assert.rejects(fs.writeFile(file, "replacement", { flag: "wx" }), { code: "EEXIST" });
  assert.equal(await fs.readFile(file, "utf8"), "original");
});

unit("mock metadata verifies junction rejection at root, tmp ancestor and final target", async () => {
  for (const unsafe of [PROJECT_ROOT, path.join(PROJECT_ROOT, "Benchmarks.Local", "tmp"), directory]) {
    const lstat = async current => ({ isSymbolicLink: () => current === unsafe });
    await assert.rejects(assertNoLinks(PROJECT_ROOT, directory, lstat), /Symlink\/junction/);
  }
  await assertNoLinks(PROJECT_ROOT, directory, async () => ({ isSymbolicLink: () => false }));
  await assert.rejects(assertNoLinks(PROJECT_ROOT, path.resolve(PROJECT_ROOT, "../outside")), /escapes project/);
});

unit("real junction/symlink guards reject redirected directories when sandbox allows creation", async context => {
  const target = path.join(directory, "link-target");
  const link = path.join(directory, "junction");
  await fs.mkdir(target);
  try { await fs.symlink(target, link, process.platform === "win32" ? "junction" : "dir"); }
  catch (error) {
    if (error.code !== "EPERM" && error.code !== "EACCES") throw error;
    const reason = `Sandbox denies junction creation (${error.code}); mock metadata guard tested separately`;
    context.skip(reason);
    return { skipped: true, reason };
  }
  await assert.rejects(assertNoLinks(PROJECT_ROOT, link), /Symlink\/junction/);
  await assert.rejects(hashTree(link), /Symlink/);
});

unit("source hashing includes global.json/projects and excludes publish/build noise", async () => {
  const root = path.join(directory, "sources");
  await fs.mkdir(root);
  for (const [name, body] of [["global.json", '{"sdk":{"version":"8.0.400"}}'], ["Battle.cs", "source"], ["Web.csproj", "project"]])
    await fs.writeFile(path.join(root, name), body, { flag: "wx" });
  await fs.mkdir(path.join(root, "publish"));
  await fs.writeFile(path.join(root, "publish", "build.log"), "noise", { flag: "wx" });
  const hash = await hashTree(root, true);
  assert.equal(hash.fileCount, 3);
  assert.ok(hash.files.some(file => file.path === "global.json"));
  assert.equal((await hashTree(root, true)).sha256, hash.sha256);
  assert.notEqual((await hashTree(root, false)).sha256, hash.sha256);
  await fs.writeFile(path.join(root, "new.cs"), "new", { flag: "wx" });
  assert.notEqual((await hashTree(root, true)).sha256, hash.sha256);
});

async function withFakeBrowser(mode, body, runtimeMode = "aot-binary") {
  const names = ["window", "document", "getDotnetRuntime", "navigator", "devicePixelRatio"];
  const originals = new Map(names.map(name => [name, Object.getOwnPropertyDescriptor(globalThis, name)]));
  const heap = new Float64Array(160);
  let tick = 0, resets = 0, frontendCalls = 0;
  const events = [];
  const writeFrame = () => { heap.set(fakeHeader(tick), 8); heap[8 + 80] = 1; return 64; };
  const checksum = () => `tick-${tick}`;
  const bridge = {
    Reset(profile, seed) { assert.equal(profile, "nominal"); assert.equal(seed, 7331); events.push(`reset:${tick}`); resets++; tick = 0; return writeFrame(); },
    Advance(count) { assert.equal(count, 1); events.push(`advance:${tick + 1}`); tick++; return writeFrame(); },
    Snapshot: writeFrame, Checksum: checksum, Validate: checksum, DebugColliders: () => {},
    ShipTactic: (serial, field) => serial + field, Reinforcement: (team, field) => team + field,
    Mode: () => runtimeMode, Revision: () => "r", FleetRevision: () => "f", PhysicsRevision: () => "p",
  };
  const elements = { profile: { value: "nominal" }, seed: { value: "7331" } };
  const fakeWindow = { __monoCompilerStudy: { header: () => readHeader(heap, 64) },
    spaceBattle: { ready: true, pause: () => {}, showColliders: () => {}, colliders: () => ({ enabled: false }),
      reset: () => bridge.Reset(elements.profile.value, Number(elements.seed.value)), snapshot: { ships: [[1]] } },
    advanceTime: async ms => { assert.equal(ms, 1000 / 60); frontendCalls++; bridge.Advance(1); } };
  const globals = { window: fakeWindow, document: { getElementById: name => elements[name] },
    getDotnetRuntime: () => ({ localHeapViewF64: () => heap, localHeapViewU8: () => new Uint8Array(heap.buffer),
      getAssemblyExports: async () => ({ SpaceBattle: { Web: { BattleBridge: bridge } } }), runtimeBuildInfo: { ProductVersion: "fake-test-only" } }),
    navigator: { userAgent: "fake-browser-no-measurement", hardwareConcurrency: 1 }, devicePixelRatio: 1 };
  try {
    for (const name of names) Object.defineProperty(globalThis, name, { configurable: true, value: globals[name] });
    const options = { config: { seed: 7331, warmupTicks: 2, phaseTicks: 2 },
      trial: { mode, profile: "nominal", startTick: 2, ticks: 3 },
      helpers: { readHeader: readHeader.toString(), stateFromHeader: stateFromHeader.toString() } };
    await body(options, () => ({ resets, frontendCalls, events }));
  } finally {
    for (const name of names) {
      const descriptor = originals.get(name);
      if (descriptor) Object.defineProperty(globalThis, name, descriptor); else delete globalThis[name];
    }
  }
}

unit("fake core replay warms a separate battle, resets again, samples exactly one tick and checkpoints every phase", async () => {
  await withFakeBrowser("core", async (options, inspect) => {
    const result = await replayInPage(options);
    assert.equal(inspect().resets, 2);
    assert.equal(inspect().frontendCalls, 0);
    assert.equal(result.warmupFinalTick, 2);
    assert.deepEqual(result.samples.map(sample => sample.state.tick), [3, 4, 5]);
    assert.deepEqual(result.checkpoints.map(checkpoint => checkpoint.state.tick), [0, 2, 4, 5]);
    assert.ok(result.samples.every(sample => sample.timing.stepMs === .25 && sample.timing.exportMs === .125));
    assert.equal(inspect().events[3], "reset:2");
  });
});

unit("fake frontend replay uses original single-tick advanceTime for warmup/prelude/measurement", async () => {
  await withFakeBrowser("frontend", async (options, inspect) => {
    const result = await replayInPage(options);
    assert.equal(inspect().resets, 2);
    assert.equal(inspect().frontendCalls, 7);
    assert.deepEqual(result.samples.map(sample => sample.state.tick), [3, 4, 5]);
    assert.ok(result.samples.every(sample => "fullFrontendMs" in sample.timing && !("advanceWallMs" in sample.timing)));
    assert.deepEqual(result.checkpoints.map(checkpoint => checkpoint.checksum), ["tick-0", "tick-2", "tick-4", "tick-5"]);
  });
});

unit("runtime guard accepts only production AOT and refuses interpreter/CoreCLR before Reset", async () => {
  for (const runtimeMode of ["interpreter-binary", "coreclr-r2r-binary", "coreclr-interpreter-binary", "unknown"])
    await withFakeBrowser("core", async (options, inspect) => {
      await assert.rejects(replayInPage(options), /aot-binary only/);
      assert.equal(inspect().resets, 0);
    }, runtimeMode);
});
