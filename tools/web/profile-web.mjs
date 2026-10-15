import fs from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright";

const arg = (name, fallback) => {
  const i = process.argv.indexOf(name);
  return i < 0 ? fallback : process.argv[i + 1];
};
const url = arg("--url", "https://shortkedr.github.io/skecs/");
const warmupTicks = Number(arg("--warmup-ticks", "600"));
if (!Number.isInteger(warmupTicks) || warmupTicks < 0 || warmupTicks > 3600) {
  throw new Error("Warmup ticks must be an integer from 0 to 3600");
}
const output = path.resolve(
  arg("--output", "Benchmarks.Local/tmp/web-performance.json"),
);
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 900 },
  });
  await page.goto(url, { waitUntil: "networkidle", timeout: 120000 });
  await page.waitForFunction(() => window.spaceBattle?.ready, null, {
    timeout: 120000,
  });
  const results = await page.evaluate(async (warmupTicks) => {
    window.spaceBattle.pause(true);
    document.getElementById("profile").value = "nominal";
    document.getElementById("seed").value = "7331";
    window.spaceBattle.reset();
    await window.advanceTime((warmupTicks * 1000) / 60);
    const runtime = getDotnetRuntime(0);
    const exports = await runtime.getAssemblyExports("SpaceBattle.Web.dll");
    const binary = Boolean(exports.SpaceBattle?.Web?.BattleBridge?.Mode);
    const bridge = exports.SpaceBattle?.Web?.BattleBridge;
    const invoke = (name, ...args) =>
      binary
        ? bridge[name](...args)
        : DotNet.invokeMethod("SpaceBattle.Web", name, ...args);
    const samples = [],
      exportSamples = [],
      parseSamples = [];
    let bytes = 0;
    for (let i = 0; i < 40; i++) {
      const start = performance.now();
      const result = invoke("Advance", 1);
      const end = performance.now();
      let stepMs;
      if (binary) {
        const heap = runtime.localHeapViewF64();
        stepMs = heap[result / 8 + 5];
        bytes = heap[result / 8 + 31];
      } else {
        const parseStart = performance.now();
        const frame = JSON.parse(result);
        parseSamples.push(performance.now() - parseStart);
        stepMs = frame.stepMs;
        bytes = result.length;
      }
      samples.push({ totalMs: end - start, stepMs });
    }
    for (let i = 0; i < 40; i++) {
      const start = performance.now();
      invoke("Snapshot");
      exportSamples.push(performance.now() - start);
    }
    window.spaceBattle.refresh();
    const frameSamples = [];
    for (let i = 0; i < 30; i++) {
      const start = performance.now();
      await window.advanceTime(1000 / 60);
      frameSamples.push(performance.now() - start);
    }
    const intervals = [];
    let frames = 0,
      last = 0,
      active = true;
    const observe = () => {
      if (!active) return;
      const now = performance.now();
      if (last) intervals.push(now - last);
      last = now;
      frames++;
      requestAnimationFrame(observe);
    };
    const initialTick = window.spaceBattle.snapshot.tick;
    const start = performance.now();
    requestAnimationFrame(observe);
    window.spaceBattle.pause(false);
    await new Promise((resolve) => setTimeout(resolve, 5000));
    window.spaceBattle.pause(true);
    active = false;
    const durationMs = performance.now() - start;
    const elapsedTicks = window.spaceBattle.snapshot.tick - initialTick;
    const stats = (values) => {
      const sorted = values.slice().sort((a, b) => a - b);
      return {
        meanMs: values.reduce((a, b) => a + b, 0) / values.length,
        p50Ms: sorted[Math.floor(sorted.length * 0.5)],
        p95Ms: sorted[Math.floor(sorted.length * 0.95)],
      };
    };
    return {
      runtimeMode: binary ? bridge.Mode() : "interpreter-json",
      userAgent: navigator.userAgent,
      hardwareConcurrency: navigator.hardwareConcurrency,
      nominalSeed: 7331,
      warmupTicks,
      viewport: [innerWidth, innerHeight],
      coreStep: stats(samples.map((s) => s.stepMs)),
      stepAndExport: stats(samples.map((s) => s.totalMs)),
      exportOnly: stats(exportSamples),
      parse: parseSamples.length ? stats(parseSamples) : null,
      frameWithForcedUi: stats(frameSamples),
      frameBytes: bytes,
      realtime: {
        durationMs,
        frames,
        framesPerSecond: (frames * 1000) / durationMs,
        elapsedTicks,
        simulationTicksPerSecond: (elapsedTicks * 1000) / durationMs,
        frameIntervals: stats(intervals),
      },
      finalCounts: window.spaceBattle.snapshot.counts,
    };
  }, warmupTicks);
  await fs.mkdir(path.dirname(output), { recursive: true });
  await fs.writeFile(
    output,
    JSON.stringify(
      { url, measuredAt: new Date().toISOString(), ...results },
      null,
      2,
    ),
  );
  console.log(JSON.stringify(results, null, 2));
} finally {
  await browser.close();
}
