import assert from "node:assert/strict";
import test from "node:test";
import path from "node:path";
import { MODES, parseArgs, parseModes, localUrl, diagnosticUrl, windowSizes, stats,
  validateSample, summarizeRun, summarizePages, compareStates, validateOutputPath,
  validateAssets, warmupMode, sampleRequest, scenePreparation } from "./ios-diagnostics.mjs";

const args = ["--url", "http://127.0.0.1:8123/", "--output", "Benchmarks.Local/tmp/ios/report.json"];
const memory = { wasmLinearBytes: 65536, managedBytes: 100, jsHeapBytes: null,
  canvasBackingBytes: 4096, atlasDecodedBytes: 8192 };
const before = { tick: 0, checksum: "initial", counts: {}, counters: {} };
const sample = mode => ({ mode, stepMs: ["render", "minimal"].includes(mode) ? 0 : 1,
  exportMs: 2, renderMs: mode === "simulation" ? 0 : 3,
  tick: ["render", "minimal"].includes(mode) ? 0 : 1,
  checksum: ["render", "minimal"].includes(mode) ? "initial" : "next", counts: {}, counters: {}, memory });

test("import is side-effect free; default bounded options", () => {
  const options = parseArgs(args);
  assert.equal(options.ticks, 60);
  assert.equal(options.repeats, 3);
  assert.equal(options.browser, "chromium");
  assert.deepEqual(options.modes, MODES);
  assert.equal(options.faults, false);
  assert.deepEqual(windowSizes(60), [1, 24, 60]);
  assert.deepEqual(windowSizes(1), [1, 1, 1]);
  assert.deepEqual(windowSizes(120), [1, 24, 120]);
});

test("strict parsing rejects stress, unknown and duplicate options", () => {
  for (const addition of [["--ticks", "121"], ["--ticks", "0"], ["--ticks", "1.5"],
    ["--ticks", "Infinity"], ["--repeats", "4"], ["--repeats", "0"], ["--browser", "firefox"],
    ["--stress"], ["--retry", "guess"], ["--ticks", "2", "--ticks", "3"],
    ["--ticks"], ["--faults", "--faults"]]) assert.throws(() => parseArgs([...args, ...addition]));
  assert.throws(() => parseArgs([]));
  assert.equal(parseArgs([...args, "--faults", "--retry", "reload"]).retry, "reload");
  assert.equal(parseArgs([...args, "--faults", "--retry", "button"]).retry, "reload");
  assert.equal(parseArgs([...args, "--faults"]).retry, "reload");
  assert.equal(parseArgs([...args, "--browser", "webkit"]).browser, "webkit");
  assert.throws(() => windowSizes(121));
});

test("mode parsing is exact and unique", () => {
  assert.deepEqual(parseModes("render,minimal"), ["render", "minimal"]);
  for (const modes of ["", "render,render", "normal,", "stress", " render", "Normal"]) {
    assert.throws(() => parseModes(modes));
  }
});

test("image-less assets and warming remain isolated from PNG decoding", () => {
  const empty = { sourceType: "image", imageCount: 0, canvasCount: 0, decodedBytes: 0, loads: 0, revision: "test" };
  const images = { ...empty, imageCount: 6, decodedBytes: 37743264, loads: 6 };
  for (const mode of ["simulation", "minimal"]) {
    assert.equal(warmupMode(mode), "simulation");
    assert.equal(validateAssets(empty, mode), empty);
    assert.throws(() => validateAssets(images, mode));
    assert.throws(() => validateAssets({ ...empty, loads: 1 }, mode));
    assert.throws(() => validateAssets({ ...empty, decodedBytes: 1 }, mode));
  }
  for (const mode of ["normal", "render"]) {
    assert.equal(warmupMode(mode), "normal");
    assert.equal(validateAssets(images, mode), images);
    assert.throws(() => validateAssets(empty, mode));
    assert.throws(() => validateAssets({ ...images, decodedBytes: 1 }, mode));
  }
  assert.throws(() => warmupMode("stress"));
  assert.throws(() => validateAssets({ ...empty, canvasCount: 1 }, "minimal"));
});

test("only loopback URLs, diagnostic startup parameters override query", () => {
  for (const url of ["https://example.com/", "http://127.0.0.1.example.com/", "file:///tmp/x",
    "http://user@localhost/", "ftp://localhost/", "http://192.168.1.5/"]) assert.throws(() => localUrl(url));
  assert.ok(localUrl("http://[::1]:8123/"));
  const url = new URL(diagnosticUrl("http://localhost:8123/?test=0&diagnostics=x&other=ok", "render"));
  assert.equal(url.searchParams.get("diagnostics"), "render");
  assert.equal(url.searchParams.get("test"), "1");
  assert.equal(url.searchParams.get("other"), "ok");
});

test("sample contract and immutable frozen state", () => {
  for (const mode of MODES) assert.equal(validateSample(sample(mode), mode, before).mode, mode);
  assert.throws(() => validateSample({ ...sample("render"), checksum: "changed" }, "render", before));
  assert.throws(() => validateSample({ ...sample("minimal"), tick: 1 }, "minimal", before));
  assert.throws(() => validateSample({ ...sample("minimal"), counts: { ships: 1 } }, "minimal", before));
  assert.throws(() => validateSample({ ...sample("normal"), tick: 2 }, "normal", before));
  assert.throws(() => validateSample({ ...sample("simulation"), renderMs: 1 }, "simulation", before));
  assert.throws(() => validateSample({ ...sample("normal"), memory: {} }, "normal", before));
  assert.throws(() => validateSample({ ...sample("normal"), stepMs: NaN }, "normal", before));
});

test("frozen scenes prepare tick24 without rendering; step requests remain bounded", () => {
  for (const mode of ["render", "minimal"]) {
    assert.deepEqual(scenePreparation(mode), { mode: "simulation", ticks: 24, renders: 0 });
    const prepared = { ...sample("simulation"), tick: 24, checksum: "tick24" };
    validateSample(prepared, "simulation", before, 24);
    validateSample({ ...sample(mode), tick: 24, checksum: "tick24" }, mode, prepared);
    assert.throws(() => validateSample({ ...sample(mode), tick: 25 }, mode, prepared));
  }
  assert.equal(scenePreparation("simulation"), null);
  assert.equal(scenePreparation("normal"), null);
  assert.deepEqual(sampleRequest("simulation"), { mode: "simulation", ticks: 1, renders: 1 });
  for (const ticks of [0, 121, 1.5, Infinity]) assert.throws(() => sampleRequest("simulation", ticks, 0));
  assert.throws(() => sampleRequest("render", 1, 2));
});

test("per-run stage statistics preserve separate timings and unknown memory", () => {
  assert.deepEqual(stats([4, 1, 2, 3]), { n: 4, mean: 2.5, median: 2.5, p95: 4, max: 4 });
  assert.throws(() => stats([]));
  assert.throws(() => stats([-1]));
  const values = [sample("normal"), { ...sample("normal"), stepMs: 5 }];
  const summary = summarizeRun(values);
  assert.equal(summary.stepMs.mean, 3);
  assert.equal(summary.exportMs.mean, 2);
  assert.equal(summary.renderMs.mean, 3);
  assert.equal(values[0].memory.jsHeapBytes, null);
});

test("independent pages, not ticks, are statistical units", () => {
  const row = (repeat, values) => ({ label: "current", mode: "normal", repeat,
    windows: [{ index: 0, calls: values.length, timings: summarizeRun(values) }] });
  const summary = summarizePages([row(1, [sample("normal")]),
    row(2, Array.from({ length: 60 }, () => ({ ...sample("normal"), stepMs: 5 })))])[0];
  assert.equal(summary.independentPages, 2);
  assert.equal(summary.pageMeanMs.stepMs.n, 2);
  assert.equal(summary.pageMeanMs.stepMs.mean, 3);
});

test("cross-page/baseline state comparisons are grouped by advancing/frozen family", () => {
  const row = (label, mode, checksum, tick) => ({ label, mode, repeat: 1,
    windows: [{ index: 0, initial: { tick, checksum }, final: { tick, checksum, counts: { ships: 1 }, counters: {} } }] });
  const rows = [row("baseline", "simulation", "next", 1), row("current", "normal", "next", 1),
    row("current", "render", "tick24", 24), row("current", "minimal", "tick24", 24)];
  assert.equal(compareStates(rows).comparisons, 2);
  assert.throws(() => compareStates([...rows, row("current", "normal", "wrong", 1)]));
  const otherWindow = row("current", "minimal", "tick24", 24);
  otherWindow.windows[0].index = 2;
  assert.equal(compareStates([...rows, otherWindow]).comparisons, 3);
  otherWindow.windows[0].initial.checksum = otherWindow.windows[0].final.checksum = "wrong";
  assert.throws(() => compareStates([...rows, otherWindow]));
  assert.throws(() => compareStates([row("current", "render", "initial", 0)]));
});

test("pure output path validation cannot escape project or use a non-JSON path", () => {
  const root = path.resolve(".");
  assert.throws(() => validateOutputPath(path.resolve(root, "..", "escape.json"), root));
  assert.throws(() => validateOutputPath(path.resolve(root, "report.txt"), root));
  assert.throws(() => validateOutputPath(path.resolve(root, "tools/web/ios-diagnostics.mjs"), root));
  assert.equal(validateOutputPath(path.resolve(root, "Benchmarks.Local/tmp/ios/report.json"), root),
    path.join("Benchmarks.Local", "tmp", "ios", "report.json"));
});
