import fs from "node:fs/promises";
import path from "node:path";
import assert from "node:assert/strict";

const directory = path.resolve(process.argv[2] || "Benchmarks.Local/tmp/performance-study");
const median = (values) => {
  const sorted = values.slice().sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
};
const summarize = (values) => ({ median: median(values), min: Math.min(...values), max: Math.max(...values) });
const summary = [];
const checkEquivalent = (phases, fields, context) => {
  for (const phase of phases.slice(1)) {
    for (const field of fields) assert.deepEqual(phase[field], phases[0][field], `${context}/${field}`);
  }
};
for (const revision of ["before", "tactics", "stats"]) {
  const native = JSON.parse(await fs.readFile(path.join(directory, `native-${revision}.json`), "utf8"));
  for (let phase = 0; phase < native.Runs[0].Phases.length; phase++) {
    const phases = native.Runs.map(run => run.Phases[phase]);
    checkEquivalent(phases, ["Checksum", "MeanCounts", "FinalCounts", "Counters"], `native/${revision}/${phase}`);
    summary.push({ platform: "native", revision, startTick: phases[0].StartTick, endTick: phases[0].EndTick,
      stepMs: summarize(phases.map(p => p.CoreStep.MeanMs)), p95Ms: summarize(phases.map(p => p.CoreStep.P95Ms)),
      stages: phases[0].Stages.map((s, i) => ({ name: s.Name, meanMs: summarize(phases.map(p => p.Stages[i].Timing.MeanMs)) })),
      meanCounts: phases[0].MeanCounts, finalCounts: phases[0].FinalCounts, checksum: phases[0].Checksum,
      allocatedBytesPerTick: phases[0].AllocatedBytes / (phases[0].EndTick - phases[0].StartTick + 1) });
  }
}
const web = JSON.parse(await fs.readFile(path.join(directory, "web.json"), "utf8"));
for (const revision of [...new Set(web.runs.map(r => r.revision))]) {
  const runs = web.runs.filter(r => r.revision === revision);
  for (let phase = 0; phase < runs[0].phases.length; phase++) {
    const phases = runs.map(run => run.phases[phase]);
    checkEquivalent(phases, ["checksum", "meanCounts", "finalCounts", "counters"], `wasm/${revision}/${phase}`);
    summary.push({ platform: "wasm", revision, startTick: phases[0].startTick, endTick: phases[0].endTick,
      stepMs: summarize(phases.map(p => p.coreStep.meanMs)), p95Ms: summarize(phases.map(p => p.coreStep.p95Ms)),
      totalMs: summarize(phases.map(p => p.stepAndExport.meanMs)), exportAndInteropMs: summarize(phases.map(p => p.perTickExportAndInterop.meanMs)),
      stages: phases[0].stages.map((s, i) => ({ name: s.name, meanMs: summarize(phases.map(p => p.stages[i].timing.meanMs)) })),
      meanCounts: phases[0].meanCounts, finalCounts: phases[0].finalCounts, checksum: phases[0].checksum });
  }
}
const serialize = JSON.stringify(summary, null, 2);
await fs.writeFile(path.join(directory, "summary.json"), serialize);
for (const row of summary) console.log(`${row.platform} ${row.revision} ${row.startTick}-${row.endTick}: ${row.stepMs.median.toFixed(3)} ms [${row.stepMs.min.toFixed(3)}, ${row.stepMs.max.toFixed(3)}], p95 ${row.p95Ms.median.toFixed(3)}, AI ${row.stages[1].meanMs.median.toFixed(3)}, hits ${row.stages[7].meanMs.median.toFixed(3)}${row.totalMs ? `, with export ${row.totalMs.median.toFixed(3)}` : ""}`);
