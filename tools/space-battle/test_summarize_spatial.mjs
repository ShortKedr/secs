import assert from "node:assert/strict";
import test from "node:test";
import { summarize, verifyNative } from "./summarize-spatial.mjs";

test("summarize independent run means without selecting the fastest", () => {
  assert.deepEqual(summarize([12, 9, 10]), { median: 10, minimum: 9, maximum: 12, runMeans: [12, 9, 10] });
  assert.equal(summarize([9, 11]).median, 10);
  assert.throws(() => summarize([]));
  assert.throws(() => summarize([NaN]));
});

test("accept timing changes but reject changed workload or gameplay", () => {
  const before = { Metadata: { Options: { Seed: 7331 }, Ticks: 600, WarmupTicks: 600, Runtime: ".NET 8.0.31" }, Checksum: "ABCD", Checkpoints: [{ Tick: 600, Checksum: "ABCD" }],
    Counters: { Hits: 100 }, Tactics: { GroupOrders: 10 }, Timing: { MeanMs: 10 } };
  const after = structuredClone(before); after.Timing.MeanMs = 6;
  verifyNative(before, after);
  for (const mutate of [r => r.Metadata.Options.Seed++, r => r.Metadata.Ticks++, r => r.Metadata.WarmupTicks++, r => r.Metadata.Runtime = ".NET 9", r => r.Checksum = "DCBA",
    r => r.Checkpoints[0].Checksum = "DCBA", r => r.Counters.Hits++, r => r.Tactics.GroupOrders++]) {
    const changed = structuredClone(after); mutate(changed);
    assert.throws(() => verifyNative(before, changed));
  }
});
