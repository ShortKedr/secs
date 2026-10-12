import fs from "node:fs/promises";
import path from "node:path";
import { createHash } from "node:crypto";
import { fileURLToPath } from "node:url";

export const median = values => {
  const sorted = [...values].sort((a, b) => a - b);
  if (!sorted.length || sorted.some(v => !Number.isFinite(v))) throw new Error("Finite independent run means required");
  const i = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[i] : (sorted[i - 1] + sorted[i]) / 2;
};
const mean = values => values.reduce((a, b) => a + b, 0) / values.length;
const describe = values => ({ median: median(values), minimum: Math.min(...values), maximum: Math.max(...values), runMeans: values });
const sha = bytes => createHash("sha256").update(bytes).digest("hex");

export async function summarizeNative(directory) {
  const manifest = JSON.parse(await fs.readFile(path.join(directory, "manifest.json"), "utf8"));
  const scenarios = [];
  for (const file of (await fs.readdir(directory)).filter(f => f.endsWith("-raw.json")).sort()) {
    const bytes = await fs.readFile(path.join(directory, file));
    const raw = JSON.parse(bytes);
    const engines = {};
    for (const engine of ["aether-2.2.0", "circles-relativeCCD"]) {
      const outer = raw.samples.filter(s => s.Engine === engine && !s.Instrumented);
      const inner = raw.samples.filter(s => s.Engine === engine && s.Instrumented);
      if (outer.length < 3 || inner.length !== outer.length || new Set(outer.map(s => s.Repeat)).size !== outer.length)
        throw new Error(`Independent repeats missing: ${file}/${engine}`);
      engines[engine] = {
        totalMs: describe(outer.map(s => mean(s.Frames.map(f => f.TotalMs)))),
        combinedStepMs: describe(outer.map(s => mean(s.Frames.map(f => f.CombinedStepMs)))),
        hitsMs: describe(outer.map(s => mean(s.Frames.map(f => f.HitsOuterMs)))),
        allocatedBytes: outer.map(s => s.Frames.reduce((n, f) => n + f.AllocatedBytes, 0)),
        phasesMs: Object.fromEntries(["ContactCandidatesMs", "SolverMs", "HitCandidatesMs", "HitNarrowMs"].map(key =>
          [key, describe(inner.map(s => mean(s.Frames.map(f => f.Phases[key]))))])),
      };
    }
    scenarios.push({ name: file.replace("-raw.json", ""), inputSha256: raw.inputSha256, rawSha256: sha(bytes), engines });
  }
  return { runtime: manifest.runtime, seed: manifest.seed, ticks: manifest.ticks, warmup: manifest.warmup,
    sweeps: manifest.sweeps, repeats: manifest.repeats, runtimeEnvironment: manifest.runtimeEnvironment,
    binaries: manifest.assemblies, scenarios };
}

export async function summarizeWeb(directory) {
  const manifest = JSON.parse(await fs.readFile(path.join(directory, "manifest.json"), "utf8"));
  const runs = [];
  for (const file of (await fs.readdir(directory)).filter(f => /^\d\d-.*\.json$/.test(f)).sort()) {
    const bytes = await fs.readFile(path.join(directory, file));
    const raw = JSON.parse(bytes);
    if (raw.runtimeMode !== "aot-binary" || raw.errors.length || raw.samples.length !== manifest.config.ticks)
      throw new Error(`Incomplete AOT evidence: ${file}`);
    runs.push({ ...raw, rawSha256: sha(bytes) });
  }
  const profiles = manifest.config.profiles.map(profile => {
    const roles = {};
    for (const role of ["before", "after"]) {
      const selected = runs.filter(r => r.trial.profile === profile && r.trial.role === role && !r.trial.collectTimings);
      if (selected.length < 3 || new Set(selected.map(r => r.trial.repeat)).size !== selected.length)
        throw new Error(`Independent browser repeats missing: ${profile}/${role}`);
      if (new Set(selected.map(r => r.checksum)).size !== 1) throw new Error(`Within-runtime replay differs: ${profile}/${role}`);
      roles[role] = { stepMs: describe(selected.map(r => mean(r.samples.map(s => s.stepMs)))),
        advanceAndExportMs: describe(selected.map(r => mean(r.samples.map(s => s.advanceAndExportMs)))),
        physicsMs: describe(selected.map(r => mean(r.samples.map(s => s.combinedPhysicsStageMs)))),
        hitsMs: describe(selected.map(r => mean(r.samples.map(s => s.combinedHitsStageMs)))),
        finals: selected.map(r => r.final), checksum: selected[0].checksum,
        nativeWasm: selected[0].assets.filter(a => /dotnet\.native.*\.wasm/.test(a.url)),
        rawSha256: selected.map(r => r.rawSha256) };
    }
    const inner = runs.filter(r => r.trial.profile === profile && r.trial.role === "after" && r.trial.collectTimings);
    if (inner.length !== roles.after.stepMs.runMeans.length || new Set(inner.map(r => r.trial.repeat)).size !== inner.length)
      throw new Error(`Independent inner browser repeats missing: ${profile}`);
    if (inner.some(r => r.checksum !== roles.after.checksum))
      throw new Error(`Instrumentation changed replay: ${profile}`);
    const phasesMs = Object.fromEntries(["ContactCandidatesMs", "SolverMs", "HitCandidatesMs", "HitNarrowMs"].map(key =>
      [key, describe(inner.map(r => mean(r.samples.map(s => s.inner[key]))))]));
    return { profile, roles, phasesMs, instrumentedStepMs: describe(inner.map(r => mean(r.samples.map(s => s.stepMs)))) };
  });
  return { config: manifest.config, scope: manifest.scope, profiles, browserVersion: runs[0].browserVersion };
}

if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))) {
  const native = await summarizeNative(path.resolve(process.argv[2]));
  const web = process.argv[3] ? await summarizeWeb(path.resolve(process.argv[3])) : null;
  process.stdout.write(JSON.stringify({ schema: 1, aggregation: "Median of independent run means; sequential ticks are not repeats", native, web }, null, 2) + "\n");
}
