import assert from "node:assert/strict";
import { test } from "node:test";
import { median } from "./summarize.mjs";

test("run means use median, not best run or maximum from integer rounding", () => {
  assert.equal(median([3, 1, 2]), 2);
  assert.equal(median([4, 1, 3, 2]), 2.5);
  assert.throws(() => median([]));
  assert.throws(() => median([1, NaN, 3]));
});
