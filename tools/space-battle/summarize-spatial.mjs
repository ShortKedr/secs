import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

export function summarize(values) {
  assert.ok(values.length && values.every(Number.isFinite), "Finite independent run means required");
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return { median: sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2,
    minimum: sorted[0], maximum: sorted.at(-1), runMeans: values };
}

export function verifyNative(before, after) {
  assert.deepEqual(after.Metadata.Options, before.Metadata.Options, "Different configurations");
  assert.equal(after.Metadata.Ticks, before.Metadata.Ticks, "Different tick windows");
  assert.equal(after.Metadata.WarmupTicks, before.Metadata.WarmupTicks, "Different warmup");
  assert.equal(after.Metadata.Runtime, before.Metadata.Runtime, "Different native runtimes");
  assert.equal(after.Checksum, before.Checksum, "Changed native trajectory");
  assert.deepEqual(after.Checkpoints, before.Checkpoints, "Changed checkpoints");
  assert.deepEqual(after.Counters, before.Counters, "Changed gameplay counters");
  assert.deepEqual(after.Tactics, before.Tactics, "Changed tactical decisions");
}

async function main() {
  const arg = (name, fallback) => { const i = process.argv.indexOf(name); return i < 0 ? fallback : process.argv[i + 1]; };
  const root = fileURLToPath(new URL("../../", import.meta.url));
  const beforeDir = path.resolve(arg("--before", "Benchmarks.Local/tmp/spatial-before/results-verified"));
  const afterDir = path.resolve(arg("--after", "Benchmarks.Local/tmp/spatial-verified/results-verified"));
  const webFile = path.resolve(arg("--web", "Benchmarks.Local/tmp/spatial-verified/web-comparison.json"));
  const output = path.resolve(arg("--output", "docs/space-battle/spatial-optimization/summary.json"));
  const relative = path.relative(root, output);
  assert.ok(relative && relative !== ".." && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative), "Output must stay inside project");
  const read = async file => JSON.parse(await fs.readFile(file, "utf8"));
  const scenarios = [];
  const comparison = (runs, readValue) => ({ before: summarize(runs.map(r => readValue(r.before))), after: summarize(runs.map(r => readValue(r.after))) });
  for (const name of ["early", "scale"]) {
    const runs = [];
    for (let run = 1; run <= 3; run++) {
      const before = await read(path.join(beforeDir, `${name}-${run}.json`));
      const after = await read(path.join(afterDir, `${name}-${run}.json`));
      verifyNative(before, after);
      const compact = r => ({ timing: r.Timing, stages: r.Stages, allocatedBytes: r.AllocatedBytes, checksum: r.Checksum,
        counters: r.Counters, tactics: r.Tactics, runtime: r.Metadata.Runtime, config: r.Metadata.Options,
        ticks: r.Metadata.Ticks, warmupTicks: r.Metadata.WarmupTicks, binaryHashes: r.Provenance.Binaries });
      runs.push({ run, before: compact(before), after: compact(after) });
    }
    scenarios.push({ name, runs, coreMs: comparison(runs, r => r.timing.MeanMs),
      stages: runs[0].before.stages.map((stage, i) => ({ name: stage.Name, meanMs: comparison(runs, r => r.stages[i].Timing.MeanMs) })) });
  }
  const web = await read(webFile);
  for (const run of web.runs) assert.equal(run.runtimeMode, "aot-binary", "Production AOT runs required");
  const phases = [];
  for (let p = 0; p < web.runs[0].phases.length; p++) {
    const before = web.runs.filter(r => r.revision === "baseline").map(r => r.phases[p]);
    const after = web.runs.filter(r => r.revision === "optimized").map(r => r.phases[p]);
    assert.equal(before.length, 3); assert.equal(after.length, 3);
    for (const run of [...before, ...after]) {
      assert.equal(run.checksum, before[0].checksum, "Changed browser trajectory");
      assert.deepEqual(run.counters, before[0].counters, "Changed browser gameplay counters");
      assert.deepEqual(run.meanCounts, before[0].meanCounts, "Changed browser populations");
    }
    const metric = name => ({ before: summarize(before.map(r => r[name].meanMs)), after: summarize(after.map(r => r[name].meanMs)) });
    phases.push({ startTick: before[0].startTick, endTick: before[0].endTick,
      coreMs: metric("coreStep"), stepAndExportMs: metric("stepAndExport"), exportMs: metric("perTickExportAndInterop"),
      physicsQueries: after[0].physicsQueries, checksum: before[0].checksum });
  }
  const summary = { generatedAt: new Date().toISOString(), scope: "SECS only; identical seeded trajectories within each runtime; independent fresh warmed runs; median of three run means; no cross-runtime lockstep claim", native: scenarios, browser: phases, browserRuns: web.runs };
  await fs.mkdir(path.dirname(output), { recursive: true });
  await fs.writeFile(output, JSON.stringify(summary, null, 2) + "\n");
  console.log(JSON.stringify({ native: scenarios.map(s => ({ name: s.name, coreMs: s.coreMs })), browser: phases }, null, 2));
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) await main();
