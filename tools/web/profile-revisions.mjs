import fs from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright";

const arg = (name, fallback) => {
  const index = process.argv.indexOf(name);
  return index < 0 ? fallback : process.argv[index + 1];
};
const urls = arg("--urls", "http://127.0.0.1:8030/,http://127.0.0.1:8031/,http://127.0.0.1:8032/").split(",");
const labels = arg("--labels", "before,tactics,stats").split(",");
const repeats = Number(arg("--repeats", "3"));
const ticks = Number(arg("--ticks", "1200"));
const phaseTicks = Number(arg("--phase-ticks", "600"));
const output = path.resolve(arg("--output", "Benchmarks.Local/tmp/performance-study/web.json"));
const projectRoot = path.resolve(arg("--project-root", "."));
if (urls.length !== labels.length || urls.length < 1) throw new Error("Matching URLs and labels required");
for (const value of [repeats, ticks, phaseTicks]) {
  if (!Number.isInteger(value) || value < 1) throw new Error("Positive integer counts required");
}
const relative = path.relative(projectRoot, output);
if (relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) throw new Error("Output must stay inside project");
const browser = await chromium.launch({ headless: true });
const runs = [];
try {
  for (let run = 0; run < repeats; run++) {
    const order = Array.from({ length: urls.length }, (_, i) => i);
    if (run % 2) order.reverse();
    for (const index of order) {
      const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
      const errors = [];
      page.on("pageerror", (error) => errors.push(String(error)));
      await page.goto(`${urls[index]}?test=1`, { waitUntil: "networkidle", timeout: 120000 });
      await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
      const result = await page.evaluate(async ({ ticks, phaseTicks }) => {
        window.spaceBattle.pause(true);
        const runtime = getDotnetRuntime(0);
        const exports = await runtime.getAssemblyExports("SpaceBattle.Web.dll");
        const bridge = exports.SpaceBattle.Web.BattleBridge;
        if (!bridge.DiagnosticStage) throw new Error("Isolated diagnostic bridge required");
        bridge.Reset("nominal", 7331);
        bridge.Advance(600);
        // Warmup must not advance the measured battle's starting state.
        bridge.Reset("nominal", 7331);
        const stageNames = ["collect", "ai", "thermal-inventory", "steering", "weapons", "carriers", "physics", "hits", "lifecycle"];
        const phases = [];
        let samples = [];
        const stats = (values) => {
          const sorted = values.slice().sort((a, b) => a - b);
          const p = (fraction) => sorted[Math.ceil((sorted.length - 1) * fraction)];
          return { meanMs: values.reduce((a, b) => a + b, 0) / values.length, p50Ms: p(.5), p95Ms: p(.95), p99Ms: p(.99), maxMs: sorted.at(-1),
            budgetMissPercent: 100 * values.filter(v => v > 1000 / 60).length / values.length };
        };
        for (let tick = 1; tick <= ticks; tick++) {
          const start = performance.now();
          const pointer = bridge.Advance(1);
          const elapsedMs = performance.now() - start;
          const header = runtime.localHeapViewF64().subarray(pointer / 8, pointer / 8 + 32);
          if (header[1] !== tick) throw new Error(`Unexpected tick: ${header[1]} != ${tick}`);
          samples.push({ stepMs: header[5], totalMs: elapsedMs, counts: Array.from(header.subarray(6, 10)), bytes: header[31],
            stages: stageNames.map((_, stage) => bridge.DiagnosticStage(stage)) });
          if (tick % phaseTicks !== 0 && tick !== ticks) continue;
          const exportSamples = [];
          for (let i = 0; i < 20; i++) {
            const begin = performance.now(); bridge.Snapshot(); exportSamples.push(performance.now() - begin);
          }
          phases.push({ startTick: tick - samples.length + 1, endTick: tick,
            coreStep: stats(samples.map(s => s.stepMs)), stepAndExport: stats(samples.map(s => s.totalMs)),
            perTickExportAndInterop: stats(samples.map(s => s.totalMs - s.stepMs)), exportOnlyAtPhaseEnd: stats(exportSamples),
            stages: stageNames.map((name, stage) => ({ name, timing: stats(samples.map(s => s.stages[stage])) })),
            meanCounts: Array.from({ length: 4 }, (_, c) => samples.reduce((sum, s) => sum + s.counts[c], 0) / samples.length),
            finalCounts: samples.at(-1).counts, frameBytes: samples.at(-1).bytes, checksum: bridge.Checksum(),
            counters: Array.from(runtime.localHeapViewF64().subarray(pointer / 8 + 14, pointer / 8 + 30)) });
          samples = [];
        }
        bridge.Validate();
        return { runtimeMode: bridge.Mode(), userAgent: navigator.userAgent, logicalProcessors: navigator.hardwareConcurrency, phases };
      }, { ticks, phaseTicks });
      if (errors.length) throw new Error(errors.join("\n"));
      runs.push({ revision: labels[index], run: run + 1, url: urls[index], ...result });
      await page.close();
      await fs.mkdir(path.dirname(output), { recursive: true });
      await fs.writeFile(output, JSON.stringify({ measuredAt: new Date().toISOString(), ticks, phaseTicks, repeats,
        scope: "Fresh independent seeded replays, 600-tick separate warmup, production one-tick Advance plus binary export. Canvas/DOM paused; diagnostics read outside Step timing. Alternating revision order.", runs }, null, 2));
      console.log(JSON.stringify({ revision: labels[index], run: run + 1, phases: result.phases.map(p => ({ startTick: p.startTick, meanMs: p.coreStep.meanMs, totalMs: p.stepAndExport.meanMs, aiMs: p.stages[1].timing.meanMs, hitsMs: p.stages[7].timing.meanMs, checksum: p.checksum })) }));
    }
  }
} finally {
  await browser.close();
}
