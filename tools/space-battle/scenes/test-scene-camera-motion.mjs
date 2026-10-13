import test from 'node:test';
import assert from 'node:assert/strict';
import * as cameraModule from './scene-camera.mjs';

const dimensions = { width: 640, height: 360, originY: 180 };
const logStep = Math.log(1.25) / 60;
const ship = (id, x, y, radius = 12) => [id, id === 9 ? 1 : 0, 1, x, y, 0, 0, 100, radius];
const episode = (startTick = 100, endTick = 580) => ({
  schema: 1, id: 'camera-motion', type: 'group', profile: 'test', seed: 1,
  startTick, endTick, eventTick: Math.floor((startTick + endTick) / 2),
  eventTicks: [], focus: { ships: [1, 2], targets: [9], projectile: null },
  cameraPoints: [], evidence: { contributors: [1, 2], roster: [1, 2], leader: 1 },
});
const snapshots = (e, rows = () => []) => Array.from({ length: e.endTick - e.startTick + 1 },
  (_, i) => ({ tick: e.startTick + i, arenaHalfSize: 2000, ships: rows(i), projectiles: [] }));

function plan(e, frames, viewport = dimensions) {
  assert.equal(typeof cameraModule.planGroupCamera, 'function', 'scene-camera must export planGroupCamera');
  return cameraModule.planGroupCamera(e, frames, viewport);
}

function checkPlan(result, e) {
  assert.ok(result.revision !== undefined && result.revision !== null, 'plan needs a revision');
  assert.ok(Array.isArray(result.frames));
  assert.equal(result.frames.length, e.endTick - e.startTick + 1);
  assert.ok(result.frames.length <= 10800);
  for (const [i, frame] of result.frames.entries()) {
    assert.equal(frame.tick, e.startTick + i, 'source ticks must be exact and contiguous');
    for (const key of ['x', 'y', 'scale']) assert.ok(Number.isFinite(frame[key]), `${key} finite at ${frame.tick}`);
    assert.ok(frame.scale > 0 && frame.scale <= 16, `bounded positive scale at ${frame.tick}`);
    if (i) assert.ok(Math.abs(Math.log(frame.scale / result.frames[i - 1].scale)) <= logStep + 1e-12,
      `no emergency zoom: log-scale step at tick ${frame.tick}`);
  }
}

function checkVisible(result, e, source, viewport = dimensions) {
  checkPlan(result, e);
  const ids = new Set([...e.focus.ships, ...e.focus.targets]);
  for (const [i, frame] of result.frames.entries()) for (const row of source[i].ships) {
    if (!ids.has(row[0]) || !Number.isFinite(row[3]) || !Number.isFinite(row[4]) || !Number.isFinite(row[8])) continue;
    const radius = Math.max(24, Math.abs(row[8]) * 2) * frame.scale;
    const x = viewport.width / 2 + (row[3] - frame.x) * frame.scale;
    const y = viewport.originY - (row[4] - frame.y) * frame.scale;
    assert.ok(x - radius >= 64 - 1e-7 && x + radius <= viewport.width - 64 + 1e-7,
      `selected #${row[0]} sprite horizontally safe at ${frame.tick}`);
    assert.ok(y - radius >= 64 - 1e-7 && y + radius <= viewport.height - 64 + 1e-7,
      `selected #${row[0]} sprite vertically safe at ${frame.tick}`);
  }
}

function freeze(value) {
  if (value && typeof value === 'object') {
    for (const nested of Object.values(value)) freeze(nested);
    Object.freeze(value);
  }
  return value;
}

test('empty focus uses a finite whole-arena fallback, not unrelated ship anchors', () => {
  const e = episode(20, 80); e.focus.ships = []; e.focus.targets = [];
  const source = snapshots(e, () => [ship(77, 1500, -1400)]);
  const result = plan(e, source);
  checkPlan(result, e);
  assert.deepEqual(result.frames, plan(e, snapshots(e)).frames);
  for (const frame of result.frames) {
    assert.equal(frame.x, 0); assert.equal(frame.y, 0);
    assert.ok(2000 * frame.scale <= Math.min(dimensions.width / 2 - 64, dimensions.height / 2 - 64));
  }
});

test('selected IDs absent throughout also fall back to the full arena', () => {
  const e = episode(0, 120);
  const source = snapshots(e, () => [ship(77, -1700, 1700)]);
  const result = plan(e, source); checkPlan(result, e);
  for (const frame of result.frames) {
    assert.equal(frame.x, 0); assert.equal(frame.y, 0);
    assert.ok(2000 * frame.scale <= dimensions.height / 2 - 64);
  }
});

test('symmetric ties and row/focus ordering cannot change the camera trajectory', () => {
  const e = episode();
  const source = snapshots(e, i => [ship(1, -300 - i, 200), ship(2, 300 + i, -200), ship(9, 0, 0), ship(77, 1e8, -1e8)]);
  const result = plan(e, source); checkVisible(result, e, source);
  const reordered = structuredClone(e);
  reordered.focus.ships.reverse(); reordered.evidence.contributors.reverse(); reordered.evidence.roster.reverse();
  assert.deepEqual(plan(reordered, source.map(frame => ({ ...frame, ships: [...frame.ships].reverse() }))).frames, result.frames);
  assert.deepEqual(plan(e, source.map(frame => ({ ...frame, ships: frame.ships.filter(row => row[0] !== 77) }))).frames, result.frames);
});

test('dead victim and missing contributor fade out and transfer framing to survivors', () => {
  const e = episode(0, 720);
  const source = snapshots(e, i => [ship(1, -800, 0), ...(i < 240 ? [ship(2, -700, 0), ship(9, 900, 200)] : [])]);
  const result = plan(e, source); checkVisible(result, e, source);
  const jump = Math.hypot(result.frames[240].x - result.frames[239].x, result.frames[240].y - result.frames[239].y);
  assert.ok(jump < 170, 'death must not snap by a substantial fraction of the old focus span');
  assert.ok(Math.abs(result.frames.at(-1).x + 800) < 5 && Math.abs(result.frames.at(-1).y) < 5,
    'long-missing focus must not remain permanently cached');
  assert.ok(result.frames.at(-1).scale > result.frames[239].scale, 'camera recovers detail around surviving contributor');
});

test('opposite-side target discontinuity remains framed with smooth position and zoom', () => {
  const e = episode(0, 600);
  const source = snapshots(e, i => [ship(1, -50, -30), ship(2, 50, 30), ship(9, i < 300 ? -9000 : 9000, i < 300 ? -6000 : 6000, 40)]);
  const result = plan(e, source); checkVisible(result, e, source);
  let largestStep = 0;
  for (let i = 1; i < result.frames.length; i++) {
    const a = result.frames[i - 1], b = result.frames[i];
    largestStep = Math.max(largestStep, Math.hypot(b.x - a.x, b.y - a.y));
  }
  assert.ok(largestStep < Math.hypot(18000, 12000) / 10, 'position must not follow target teleport in one tick');
  assert.ok(result.frames[299].x < 0 && result.frames[300].x > result.frames[299].x,
    'camera transfers toward opposite-side target');
});

test('suddenly appearing far focus is already visible on its first alive tick', () => {
  const e = episode(0, 600);
  const source = snapshots(e, i => [ship(1, -30, 0), ship(2, 30, 0), ...(i >= 300 ? [ship(9, 18000, -12000, 60)] : [])]);
  for (const viewport of [dimensions, { width: 1280, height: 720, originY: 390 }, { width: 1920, height: 1080, originY: 555 }]) {
    const result = plan(e, source, viewport); checkVisible(result, e, source, viewport);
    assert.ok(result.frames[299].scale <= result.frames[200].scale,
      'offline plan anticipates distant appearance rather than emergency zooming');
  }
});

test('alternating tight and wide focus has bounded smooth zoom on every tick', () => {
  const e = episode(0, 900);
  const source = snapshots(e, i => {
    const spread = Math.floor(i / 90) % 2 ? 8000 : 20;
    return [ship(1, -spread, -spread / 2), ship(2, spread, spread / 2), ship(9, 0, 0)];
  });
  checkVisible(plan(e, source), e, source);
});

test('exact tick validation rejects omissions, duplicates, reordering and foreign ticks', () => {
  const e = episode(30, 34), source = snapshots(e);
  checkPlan(plan(e, source), e);
  for (const bad of [source.slice(1), source.slice(0, -1), [source[0], source[1], source[1], ...source.slice(3)],
    [...source].reverse(), [...source, { ...source.at(-1), tick: 35 }],
    source.map((frame, i) => i === 2 ? { ...frame, tick: 32.5 } : frame)]) assert.throws(() => plan(e, bad));
  for (const range of [{ startTick: -1 }, { startTick: 30.5 }, { endTick: 29 }, { endTick: Infinity },
    { endTick: Number.MAX_SAFE_INTEGER + 1 }]) assert.throws(() => plan({ ...e, ...range }, source));
  const negative = episode(-2, 2);
  assert.throws(() => plan(negative, snapshots(negative)), 'negative exact source ticks must be rejected');
});

test('single tick and exactly 10800 frames work; larger plans are rejected', () => {
  for (const count of [1, 10800]) {
    const e = episode(10, 10 + count - 1);
    checkPlan(plan(e, snapshots(e)), e);
  }
  const tooLong = episode(0, 10800);
  assert.throws(() => plan(tooLong, snapshots(tooLong)));
});

test('frozen inputs and nonfinite rows are handled without mutation, with repeat determinism', () => {
  const e = episode(0, 240);
  const source = snapshots(e, i => [ship(1, i, -i), ship(2, -i, i), ship(9, NaN, Infinity), ship(77, -Infinity, NaN)]);
  const beforeEpisode = structuredClone(e), beforeSource = structuredClone(source), viewport = { ...dimensions };
  freeze(e); freeze(source); freeze(viewport);
  const result = plan(e, source, viewport); checkVisible(result, e, source, viewport);
  assert.deepEqual(plan(e, source, viewport), result);
  assert.deepEqual(e, beforeEpisode); assert.deepEqual(source, beforeSource); assert.deepEqual(viewport, dimensions);
  assert.deepEqual(plan(e, snapshots(e, i => [ship(1, i, -i), ship(2, -i, i)])).frames, result.frames,
    'nonfinite positions cannot poison valid focus');
});
