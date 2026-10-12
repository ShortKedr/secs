import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import { chromium } from "playwright";

const arg = (name, fallback) => {
  const index = process.argv.indexOf(name);
  return index < 0 ? fallback : process.argv[index + 1];
};
let urls = arg("--urls", "http://127.0.0.1:8030/,http://127.0.0.1:8031/,http://127.0.0.1:8032/").split(",");
const labels = arg("--labels", "before,tactics,stats").split(",");
const sites = arg("--sites", "").split(",").filter(Boolean).map(site => path.resolve(site));
const repeats = Number(arg("--repeats", "3"));
const ticks = Number(arg("--ticks", "1200"));
const phaseTicks = Number(arg("--phase-ticks", "600"));
const warmupTicks = Number(arg("--warmup-ticks", "600"));
const collectPhysicsTimings = process.argv.includes("--physics-timings");
const profile = arg("--profile", "nominal");
const seed = Number(arg("--seed", "7331"));
if (!["low", "nominal", "high"].includes(profile) || !Number.isInteger(seed) || seed < 0 || seed > 0xffffffff)
  throw new Error("Valid profile and uint seed required");
const output = path.resolve(arg("--output", "Benchmarks.Local/tmp/performance-study/web.json"));
const projectRoot = path.resolve(arg("--project-root", "."));
if ((sites.length || urls.length) !== labels.length || labels.length < 1) throw new Error("Matching sites/URLs and labels required");
for (const value of [repeats, ticks, phaseTicks]) {
  if (!Number.isInteger(value) || value < 1) throw new Error("Positive integer counts required");
}
if (!Number.isInteger(warmupTicks) || warmupTicks < 0 || warmupTicks > 18000) throw new Error("Warmup ticks must be 0..18000");
const relative = path.relative(projectRoot, output);
if (relative === ".." || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) throw new Error("Output must stay inside project");
if (await fs.stat(output).then(() => true, () => false)) throw new Error("Use a new output file");
const servers = [];
let browser;
const runs = [];
try {
  if (sites.length) {
    urls = [];
    const mime = { ".html": "text/html", ".js": "text/javascript", ".css": "text/css", ".json": "application/json", ".wasm": "application/wasm", ".png": "image/png" };
    for (const site of sites) {
      const server = http.createServer(async (request, response) => {
        try {
          const pathname = decodeURIComponent(new URL(request.url, "http://local").pathname);
          const file = path.resolve(site, pathname.slice(1) || "index.html");
          if (!file.startsWith(site + path.sep)) { response.writeHead(403).end(); return; }
          response.writeHead(200, { "Content-Type": mime[path.extname(file)] || "application/octet-stream" }).end(await fs.readFile(file));
        } catch { if (!response.headersSent) response.writeHead(404); response.end(); }
      });
      servers.push(server);
      await new Promise((resolve, reject) => { server.once("error", reject); server.listen(0, "127.0.0.1", resolve); });
      urls.push(`http://127.0.0.1:${server.address().port}/`);
    }
  }
  browser = await chromium.launch({ headless: true });
  for (let run = 0; run < repeats; run++) {
    const order = Array.from({ length: urls.length }, (_, i) => i);
    if (run % 2) order.reverse();
    for (const index of order) {
      const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
      const errors = [];
      page.on("pageerror", (error) => errors.push(String(error)));
      await page.goto(`${urls[index]}?test=1`, { waitUntil: "networkidle", timeout: 120000 });
      await page.waitForFunction(() => window.spaceBattle?.ready, null, { timeout: 120000 });
      const result = await page.evaluate(async ({ ticks, phaseTicks, warmupTicks, collectPhysicsTimings, profile, seed }) => {
        window.spaceBattle.pause(true);
        const runtime = getDotnetRuntime(0);
        const exports = await runtime.getAssemblyExports("SpaceBattle.Web.dll");
        const bridge = exports.SpaceBattle.Web.BattleBridge;
        if (bridge.Mode() !== "aot-binary") throw new Error("Production Mono AOT bridge required");
        const reset = () => collectPhysicsTimings ? bridge.ResetPhysicsProfile(profile, seed, true) : bridge.Reset(profile, seed);
        reset();
        for (let remaining = warmupTicks; remaining > 0; remaining -= Math.min(600, remaining))
          bridge.Advance(Math.min(600, remaining));
        // Warmup must not advance the measured battle's starting state.
        reset();
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
          const header = runtime.localHeapViewF64().subarray(pointer / 8, pointer / 8 + 80);
          if (header[1] !== tick) throw new Error(`Unexpected tick: ${header[1]} != ${tick}`);
          samples.push({ stepMs: header[5], totalMs: elapsedMs, counts: [...header.subarray(6, 10), header[63]], bytes: header[31],
            counters: [...header.subarray(14, 30), ...header.subarray(65, 69)], tactics: Array.from(header.subarray(58, 62)),
            linearMemoryBytes: runtime.localHeapViewU8().length,
            managedHeapBytes: header[44], nativeBytes: header[38], accessorBytes: header[39],
            physicsTimings: bridge.PhysicsTiming ? [0, 1, 2, 3].map(field => bridge.PhysicsTiming(field)) : null,
            stages: bridge.DiagnosticStage ? stageNames.map((_, stage) => bridge.DiagnosticStage(stage)) : null,
            physicsQueries: bridge.DiagnosticPhysicsQuery ? [0, 1, 2, 3].map(field => bridge.DiagnosticPhysicsQuery(field)) : null });
          if (tick % phaseTicks !== 0 && tick !== ticks) continue;
          const exportSamples = [];
          for (let i = 0; i < 20; i++) {
            const begin = performance.now(); bridge.Snapshot(); exportSamples.push(performance.now() - begin);
          }
          phases.push({ startTick: tick - samples.length + 1, endTick: tick,
            rawTicks: samples,
            coreStep: stats(samples.map(s => s.stepMs)), stepAndExport: stats(samples.map(s => s.totalMs)),
            perTickExportAndInterop: stats(samples.map(s => s.totalMs - s.stepMs)), exportOnlyAtPhaseEnd: stats(exportSamples),
            stages: samples[0].stages ? stageNames.map((name, stage) => ({ name, timing: stats(samples.map(s => s.stages[stage])) })) : null,
            physicsQueries: samples[0].physicsQueries ? {
              names: ["sweeps", "emptySweeps", "rays", "pointQueries"],
              totals: [0, 1, 2, 3].map(field => samples.reduce((sum, s) => sum + s.physicsQueries[field], 0))
            } : null,
            meanCounts: Array.from({ length: 5 }, (_, c) => samples.reduce((sum, s) => sum + s.counts[c], 0) / samples.length),
            finalCounts: samples.at(-1).counts, frameBytes: samples.at(-1).bytes, checksum: bridge.Checksum(),
            counters: samples.at(-1).counters, tactics: samples.at(-1).tactics,
            memory: { linearHighWaterBytes: Math.max(...samples.map(s => s.linearMemoryBytes)),
              telemetryManagedHeapBytes: samples.at(-1).managedHeapBytes, nativeBytes: samples.at(-1).nativeBytes,
              accessorBytes: samples.at(-1).accessorBytes },
            physicsTimings: samples[0].physicsTimings ? ["contact-candidates", "solver", "hit-candidates", "hit-narrow"].map((name, field) =>
              ({ name, timing: stats(samples.map(s => s.physicsTimings[field])) })) : null });
          samples = [];
        }
        bridge.Validate();
        return { runtimeMode: bridge.Mode(), userAgent: navigator.userAgent, logicalProcessors: navigator.hardwareConcurrency, phases };
      }, { ticks, phaseTicks, warmupTicks, collectPhysicsTimings, profile, seed });
      if (errors.length) throw new Error(errors.join("\n"));
      runs.push({ revision: labels[index], run: run + 1, url: urls[index], ...result });
      await page.close();
      await fs.mkdir(path.dirname(output), { recursive: true });
      await fs.writeFile(output, JSON.stringify({ measuredAt: new Date().toISOString(), ticks, phaseTicks, warmupTicks, repeats, collectPhysicsTimings, profile, seed,
        scope: `Fresh independent seeded replays, ${warmupTicks}-tick separate warmup, production one-tick Advance plus binary export. Canvas/DOM paused; diagnostics read outside Step timing. Alternating revision order.`, runs }, null, 2));
      console.log(JSON.stringify({ revision: labels[index], run: run + 1, phases: result.phases.map(p => ({ startTick: p.startTick, meanMs: p.coreStep.meanMs, totalMs: p.stepAndExport.meanMs, aiMs: p.stages?.[1].timing.meanMs, hitsMs: p.stages?.[7].timing.meanMs, checksum: p.checksum })) }));
    }
  }
} finally {
  await browser?.close();
  for (const server of servers) await new Promise(resolve => server.close(resolve));
}
