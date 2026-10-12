import assert from "node:assert/strict";
import test from "node:test";
import { readColliderBuffer, drawColliders, colliderSummary, colliderDetails } from "../../SpaceBattle.Web/wwwroot/collider-debug.js";

function payload(...rows) {
  const data = new Float64Array(4 + rows.length * 9);
  data.set([1, rows.length, 9, data.byteLength]);
  rows.forEach((r, i) => data.set(r, 4 + i * 9));
  return data;
}
function context() {
  const calls = [];
  const ctx = Object.fromEntries(["save", "restore", "translate", "rotate", "strokeRect", "beginPath", "arc", "stroke", "setLineDash"]
    .map(key => [key, (...args) => calls.push([key, ...args])]));
  return { ctx, calls };
}
const camera = { x: 5, y: -2, scale: 3, width: 400, height: 300, originY: 150 };

test("binary collider schema retains exact disk and rectangle geometry", () => {
  const data = payload([12, 0, 0, 6, -1, 0.5, 2, 2, 2], [13, 1, 1, 10, 5, 0.7, 0.1, 7.25, 0.1], [14, 2, 0, 0, 0, 0, 1, 1, 1]);
  const rows = readColliderBuffer(data.buffer, 0);
  assert.equal(rows.buffer, data.buffer);
  assert.deepEqual(colliderSummary(rows), { ships: 1, projectiles: 1, debris: 1, disks: 2, rectangles: 1 });
  assert.equal(colliderDetails(rows)[1].halfLength, 7.25);
});
test("circle projection uses camera and exact radius without a minimum visual size", () => {
  const data = payload([12, 0, 0, 6, -1, 0.5, 0.01, 0.01, 0.01]);
  const { ctx, calls } = context();
  drawColliders(ctx, readColliderBuffer(data.buffer, 0), camera);
  assert.deepEqual(calls.find(c => c[0] === "arc"), ["arc", 203, 147, 0.03, 0, Math.PI * 2]);
  assert.equal(calls.at(-1)[0], "restore");
});
test("rectangle thickness and orientation reflect world-up to screen-down conversion", () => {
  const data = payload([13, 1, 1, 10, 5, 0.7, 0.1, 7.25, 0.1]);
  const { ctx, calls } = context();
  drawColliders(ctx, readColliderBuffer(data.buffer, 0), camera);
  assert.deepEqual(calls.find(c => c[0] === "translate"), ["translate", 215, 129]);
  assert.deepEqual(calls.find(c => c[0] === "rotate"), ["rotate", -0.7]);
  assert.deepEqual(calls.find(c => c[0] === "strokeRect"), ["strokeRect", -21.75, -0.1 * 3, 43.5, 0.1 * 2 * 3]);
});
test("offscreen forms are culled, disabled overlay does no canvas work", () => {
  const { ctx, calls } = context(); drawColliders(ctx, null, camera); assert.equal(calls.length, 0);
  const data = payload([1, 0, 0, 10000, 0, 0, 1, 1, 1]);
  drawColliders(ctx, readColliderBuffer(data.buffer, 0), camera);
  assert.ok(!calls.some(c => c[0] === "arc" || c[0] === "strokeRect"));
});
test("malformed geometry and headers cannot reach Canvas", () => {
  assert.throws(() => readColliderBuffer(new ArrayBuffer(4), 0));
  const data = payload([1, 0, 0, 0, 0, 0, 1, 1, 1]);
  assert.throws(() => readColliderBuffer(data.buffer, 1));
  for (const [index, bad] of [[0, 2], [1, -1], [2, 8], [3, 10], [5, 4], [6, 8], [7, NaN], [10, -1], [11, -1]]) {
    const corrupt = data.slice(); corrupt[index] = bad;
    assert.throws(() => readColliderBuffer(corrupt.buffer, 0));
  }
  const empty = payload(); assert.equal(readColliderBuffer(empty.buffer, 0).length, 0);
});
