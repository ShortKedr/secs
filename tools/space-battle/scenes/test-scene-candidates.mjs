import test from 'node:test';
import assert from 'node:assert/strict';
import { SceneFinder, SCENE_DEFAULTS, SCENE_LIMITS } from './scene-candidates.mjs';

const finder = options => new SceneFinder({ profile: 'fixture', seed: 42, ticks: 5000, ...options });
const event = (Type, Tick, Order, overrides = {}) => ({ Type, Tick, Order,
  Projectile: 100, Owner: 1, Target: 9, Kind: 4, Team: 0, TargetTeam: 1,
  OwnerId: 1, OwnerGeneration: 1, TargetId: 9, TargetGeneration: 1,
  Leader: 1, Members: 3, X: 10, Y: 20, Damage: 10,
  HullBefore: 10, HullAfter: 0, ShieldBefore: 0, ShieldAfter: 0,
  ExpiresTick: Tick + 100, ...overrides });
const tactics = [1, 2, 3].map(Serial => ({ Serial, Team: 0, Class: 1, Leader: 1, Target: 9, Members: 3 }));
const frame = (tick, events = [], extra = {}) => ({ tick, events, tactics,
  ships: [], previousShips: [], projectiles: [], counters: {}, teams: [], ...extra });
const shot = (tick, order, more = {}) => event('shot', tick, order, more);
const hit = (tick, order, more = {}) => event('hit', tick, order, more);
const destroy = (tick, order, more = {}) => event('destroy', tick, order, more);

test('group uses actual current contributors, not the shared roster', () => {
  const f = finder();
  f.ingest(frame(1, [shot(1, 0, { Kind: 1 }), shot(1, 1, { Owner: 2, OwnerId: 2, Projectile: 101, Kind: 1 })]));
  f.ingest(frame(2, [hit(2, 2, { Kind: 1, HullBefore: 20, HullAfter: 10 })]));
  f.ingest(frame(3, [hit(3, 3, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 }), destroy(3, 4)]));
  const { episodes } = f.finish();
  assert.equal(episodes.length, 1); assert.equal(episodes[0].type, 'group');
  assert.deepEqual(episodes[0].focus.ships, [1, 2]);
  assert.deepEqual(episodes[0].evidence.roster, [1, 2, 3]);
  assert.deepEqual(episodes[0].eventTicks, [1, 2, 3]);
  assert.deepEqual(episodes[0].evidence.shots, [shot(1, 0, { Kind: 1 }),
    shot(1, 1, { Owner: 2, OwnerId: 2, Projectile: 101, Kind: 1 })]);
  for (const h of episodes[0].evidence.hits) {
    const birth = episodes[0].evidence.shots.find(s => s.Projectile === h.Projectile);
    assert.equal(birth.Target, h.Target); assert.equal(birth.TargetId, h.TargetId);
    assert.equal(birth.TargetGeneration, h.TargetGeneration);
    assert.ok(!Object.hasOwn(h, 'shotBirth'));
  }
  assert.equal(episodes[0].evidence.truncated, true);
});

test('group rejects stray hits from different intended targets or target entity keys', () => {
  for (const mismatch of [{ Target: 8 }, { TargetId: 8 }, { TargetGeneration: 2 },
    { TargetId: undefined, TargetGeneration: undefined }]) {
    const f = finder();
    f.ingest(frame(1, [shot(1, 0, { Kind: 1, ...mismatch }),
      shot(1, 1, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 })]));
    f.ingest(frame(2, [hit(2, 2, { Kind: 1, HullAfter: 5 })]));
    f.ingest(frame(3, [hit(3, 3, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 }), destroy(3, 4)]));
    assert.deepEqual(f.finish().episodes, []);
  }
});

test('group shot proof retains expired births and contains one birth per contributing projectile', () => {
  const f = finder();
  const births = [shot(1, 0, { Kind: 1, ExpiresTick: 2 }),
    shot(1, 1, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 })];
  f.ingest(frame(1, births));
  f.ingest(frame(2, [hit(2, 2, { Kind: 1, HullBefore: 30, HullAfter: 20 }),
    hit(2, 3, { Kind: 1, HullBefore: 20, HullAfter: 10 })]));
  f.ingest(frame(3, [hit(3, 4, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 }), destroy(3, 5)]));
  const result = f.finish(), group = result.episodes.find(e => e.type === 'group');
  assert.equal(result.stats.expiredShots, 1);
  assert.deepEqual(group.evidence.shots, births);
  assert.equal(group.evidence.hits.length, 3);
  assert.deepEqual(group.eventTicks, [1, 2, 3]);
});

test('heavy excludes same-tick birth and impact with no visible flight', () => {
  const f = finder();
  f.ingest(frame(1, [shot(1, 0), hit(1, 1), destroy(1, 2)]));
  const result = f.finish();
  assert.ok(!result.episodes.some(e => e.type === 'heavy'));
  assert.equal(result.stats.confirmedKills, 1);
});

test('heavy retains birth ownership and follows actual rather than intended victim', () => {
  const f = finder();
  f.ingest(frame(1, [shot(1, 0, { Target: 8 })]));
  f.ingest(frame(3, [hit(3, 1), destroy(3, 2)], { tactics: [] }));
  f.ingest(frame(200));
  const e = f.finish().episodes[0];
  assert.equal(e.type, 'heavy'); assert.equal(e.evidence.intendedTarget, 8);
  assert.equal(e.evidence.actualTarget, 9); assert.deepEqual(e.focus.ships, [1]);
  assert.equal(e.evidence.truncated, false); assert.equal(e.endTick, 183);
});

for (const [name, h, d] of [
  ['friendly', { TargetTeam: 0 }, {}], ['neutral', { TargetTeam: -1 }, {}],
  ['nonlethal', { HullAfter: 1 }, {}], ['zero damage', { Damage: 0 }, {}],
  ['wrong generation', {}, { TargetGeneration: 2 }], ['wrong target', {}, { Target: 8 }],
  ['wrong owner generation', { OwnerGeneration: 2 }, {}],
]) test(`rejects ${name}`, () => {
  const f = finder(); f.ingest(frame(1, [shot(1, 0)]));
  f.ingest(frame(2, [hit(2, 1, h), destroy(2, 2, d)]));
  assert.deepEqual(f.finish().episodes, []);
});

test('destroy is required on the exact hit tick even if the ship disappears', () => {
  const f = finder(); f.ingest(frame(1, [shot(1, 0)]));
  f.ingest(frame(2, [hit(2, 1)])); f.ingest(frame(3, [destroy(3, 2)]));
  assert.deepEqual(f.finish().episodes, []);
});

test('group roster and stable leader must remain current throughout the hit window', () => {
  const f = finder();
  f.ingest(frame(1, [shot(1, 0, { Kind: 1 }), shot(1, 1, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 })]));
  f.ingest(frame(2, [hit(2, 2, { Kind: 1, HullAfter: 5 })]));
  f.ingest(frame(3, [], { tactics: tactics.map(t => ({ ...t, Leader: 2 })) }));
  f.ingest(frame(4, [hit(4, 3, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 }), destroy(4, 4)]));
  assert.deepEqual(f.finish().episodes, []);
});

test('group contribution window expires and a changed roster cannot borrow old hits', () => {
  for (const changedRoster of [false, true]) {
    const f = finder({ hitWindowTicks: 2 });
    f.ingest(frame(1, [shot(1, 0, { Kind: 1 }), shot(1, 1, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 })]));
    f.ingest(frame(2, [hit(2, 2, { Kind: 1, HullAfter: 5 })]));
    const tick = changedRoster ? 3 : 5;
    f.ingest(frame(tick, [hit(tick, 3, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 }), destroy(tick, 4)],
      changedRoster ? { tactics: tactics.slice(0, 2).map(t => ({ ...t, Members: 2 })) } : {}));
    assert.deepEqual(f.finish().episodes, []);
  }
});

test('shots expire inclusively, hard age is bounded, and overflow throws', () => {
  const f = finder({ maxShots: 1 });
  f.ingest(frame(1, [shot(1, 0, { ExpiresTick: 2 })]));
  assert.throws(() => f.ingest(frame(2, [shot(2, 1, { Projectile: 101 })])), /shots budget overflow/);
  assert.throws(() => f.finish(), /shots budget overflow/);
  const expired = finder(); expired.ingest(frame(1, [shot(1, 0, { ExpiresTick: 2 })]));
  expired.ingest(frame(3, [hit(3, 1), destroy(3, 2)]));
  assert.equal(expired.finish().stats.expiredShots, 1); assert.deepEqual(expired.finish().episodes, []);
  const age = finder(); age.ingest(frame(1, [shot(1, 0, { ExpiresTick: 9000 })]));
  age.ingest(frame(3602)); assert.equal(age.finish().stats.retainedShots, 0);
  assert.throws(() => finder({ maxShots: 24001 }), /hard limit/);
});

test('ties and candidate limits are deterministic and paths remain bounded', () => {
  const run = () => {
    const f = finder({ maxCandidates: 1, maxPathPoints: 2 });
    f.ingest(frame(1, [shot(1, 0), shot(1, 1, { Projectile: 101 })]));
    f.ingest(frame(2, [], { projectiles: [[100, 0, 4, 2, 3]] }));
    f.ingest(frame(3, [], { projectiles: [[100, 0, 4, 4, 5]] }));
    f.ingest(frame(4, [hit(4, 2), destroy(4, 3), hit(4, 4, { Projectile: 101 }), destroy(4, 5)]));
    return f.finish();
  };
  assert.deepEqual(run(), run()); assert.equal(run().episodes.length, 1);
  assert.equal(run().episodes[0].focus.projectile, 100);
  assert.ok(run().episodes[0].cameraPoints.length <= 2);
});

function warpRun({ exact = true, reverse = true, generation = 1, outside = false,
  distantShips = false, distantProjectiles = false, options = {} } = {}) {
  const f = finder({ minArrivals: 2, minWarpEnemyHits: 1, minWarpEnemyKills: 1,
    minBattleTeamShips: 1, minBattleProjectiles: 1, ...options });
  const gate = { Team: 0, Wave: 1, Corner: 2, ArrivalTick: 2, GateX: 10, GateY: 20 };
  f.ingest(frame(1, exact ? [event('warp-open', 1, 0, gate)] : []));
  f.ingest(frame(2, exact ? [event('warp-arrival', 2, 1, { ...gate, Target: 1, TargetId: 1, TargetGeneration: 1 }),
    event('warp-arrival', 2, 2, { ...gate, Target: 2, TargetId: 2, TargetGeneration: 1 })] : [],
    { ships: [[1, 0], [2, 0]], counters: { reinforcementShips: 2, shipsCreated: 2 } }));
  const tick = outside ? 1053 : 3;
  const events = [hit(tick, 3, { OwnerGeneration: generation }), destroy(tick, 4)];
  if (reverse) events.push(hit(tick, 5, { Owner: 9, OwnerId: 9, Target: 1, TargetId: 1, Team: 1, TargetTeam: 0, HullAfter: 5 }));
  const shipX = distantShips ? 1000 : 10, projectileX = distantProjectiles ? 1000 : 10;
  f.ingest(frame(tick, events, { ships: [[1, 0, 1, shipX, 20], [9, 1, 1, shipX, 20]],
    projectiles: [[100, 0, 4, projectileX, 20]] }));
  return f.finish();
}

test('warp requires exact open/arrivals, arrived identities, kill and two-sided battle', () => {
  const result = warpRun(); const w = result.episodes.find(e => e.type === 'warp');
  assert.ok(w); assert.deepEqual(w.focus.ships, [1, 2]); assert.equal(w.evidence.enemyKills, 1);
  assert.deepEqual(w.focus.gate, { x: 10, y: 20 });
  assert.deepEqual(w.evidence.battle.center, { x: 10, y: 20 });
  assert.equal(w.evidence.battle.radius, 110);
  for (const options of [{ exact: false }, { reverse: false }, { generation: 2 }, { outside: true }])
    assert.ok(!warpRun(options).episodes.some(e => e.type === 'warp'));
});

test('dead logger owner is attributed by birth entity key, with actual victim and camera anchors', () => {
  const f = finder();
  f.ingest(frame(1, [shot(1, 0, { Target: 8, OwnerClass: 8, X: 11, Y: 21 })],
    { previousShips: [[1, 0, 8, 5, 6]] }));
  f.ingest(frame(2, [], { projectiles: [[100, 0, 4, 12, 22]] }));
  f.ingest(frame(3, [hit(3, 1, { Owner: -1, Team: -1, OwnerClass: -1, X: 13, Y: 23 }), destroy(3, 2)],
    { tactics: [], previousShips: [[9, 1, 6, 14, 24]] }));
  const e = f.finish().episodes[0];
  assert.equal(e.type, 'heavy'); assert.deepEqual(e.focus.ships, [1]);
  assert.equal(e.evidence.hit.Owner, -1); assert.equal(e.evidence.actualTarget, 9);
  assert.equal(e.evidence.intendedTarget, 8);
  assert.deepEqual(e.evidence.attribution, { source: 'shot-birth', ownerKey: '1:1',
    loggedOwner: -1, loggedTeam: -1, loggedOwnerClass: -1 });
  for (const p of [{ x: 5, y: 6 }, { x: 14, y: 24 }, { x: 11, y: 21 }, { x: 12, y: 22 }, { x: 13, y: 23 }])
    assert.ok(e.cameraPoints.some(actual => actual.x === p.x && actual.y === p.y));
});

test('dead-owner attribution does not rescue key, live serial, team or friendly-victim mismatches', () => {
  for (const override of [
    { Owner: -1, Team: -1, OwnerClass: -1, OwnerGeneration: 2 },
    { Owner: -1, Team: -1, OwnerClass: -1, OwnerId: 2 },
    { Owner: -1, Team: -1, OwnerClass: -1, TargetTeam: 0 },
    { Owner: 2 }, { Team: 1 },
  ]) {
    const f = finder(); f.ingest(frame(1, [shot(1, 0)]));
    f.ingest(frame(2, [hit(2, 1, override), destroy(2, 2)]));
    assert.deepEqual(f.finish().episodes, []);
  }
});

test('group contribution requires actual layer reduction, not nominal source damage', () => {
  const f = finder();
  f.ingest(frame(1, [shot(1, 0, { Kind: 1 }), shot(1, 1, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 })]));
  f.ingest(frame(2, [hit(2, 2, { Kind: 1, HullBefore: 10, HullAfter: 10 })]));
  f.ingest(frame(3, [hit(3, 3, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 }), destroy(3, 4)]));
  assert.deepEqual(f.finish().episodes, []);
});

test('warp density is local to an actual participant contact, with configurable bounded radius', () => {
  for (const settings of [{ distantShips: true }, { distantProjectiles: true }])
    assert.ok(!warpRun(settings).episodes.some(e => e.type === 'warp'));
  const w = warpRun({ options: { battleRadius: 55.5 } }).episodes.find(e => e.type === 'warp');
  assert.equal(w.evidence.battle.radius, 55.5);
  assert.throws(() => finder({ battleRadius: 441 }), /hard limit/);
  assert.throws(() => finder({ battleRadius: 0 }), /hard limit/);
});

test('busy high-scoring heavies cannot hide group or warp themes', () => {
  const run = () => {
    const f = finder({ maxCandidates: 2, minArrivals: 1, minWarpEnemyHits: 1,
      minWarpEnemyKills: 1, minBattleTeamShips: 1, minBattleProjectiles: 1 });
    const gate = { Team: 0, Wave: 1, Corner: 2, ArrivalTick: 2, GateX: 10, GateY: 20 };
    f.ingest(frame(1, [event('warp-open', 1, 0, gate), shot(1, 1, { Kind: 1 }),
      shot(1, 2, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 }),
      ...[102, 103, 104, 105].map((Projectile, i) => shot(1, i + 3, { Projectile }))]));
    // Conflicting legacy fields must not override the actual DTO Target fields.
    f.ingest(frame(2, [event('warp-arrival', 2, 7, { ...gate, Target: 1, TargetId: 1,
      Entity: 999, EntityId: 999, EntityGeneration: 3 }), hit(2, 8, { Kind: 1, HullAfter: 5 })]));
    f.ingest(frame(3, [hit(3, 9, { Kind: 1, Owner: 2, OwnerId: 2, Projectile: 101 }), destroy(3, 10),
      hit(3, 11, { Projectile: 102, Damage: 100 }), destroy(3, 12),
      hit(3, 13, { Projectile: 500, Owner: 9, OwnerId: 9, Target: 1, TargetId: 1, Team: 1, TargetTeam: 0, HullAfter: 5 })],
      { ships: [[1, 0, 1, 10, 20], [9, 1, 1, 10, 20]], projectiles: [[500, 0, 4, 10, 20]] }));
    for (let tick = 4; tick <= 6; tick++)
      f.ingest(frame(tick, [hit(tick, 14 + (tick - 4) * 2, { Projectile: tick + 99, Damage: 100 }),
        destroy(tick, 15 + (tick - 4) * 2)]));
    return f.finish();
  };
  const result = run();
  assert.deepEqual(result.missingTypes, []);
  assert.equal(result.episodes.filter(e => e.type === 'heavy').length, 2);
  assert.ok(result.episodes.some(e => e.type === 'group'));
  assert.ok(result.episodes.some(e => e.type === 'warp'));
  assert.deepEqual(result, run());
  assert.equal(result.episodes[0].score, 200);
});

test('all configurable storage and scan budgets have hard caps', () => {
  for (const [key, cap] of Object.entries(SCENE_LIMITS))
    assert.throws(() => finder({ [key]: cap + 1 }), /hard limit/);
  assert.throws(() => finder({ maxFrameRows: 1 }).ingest(frame(1, [], { ships: [[1], [2]], tactics: [] })), /frame ships budget/);
  const f = finder({ maxArrivals: 0 });
  const gate = { Team: 0, Wave: 1, Corner: 2, ArrivalTick: 2, GateX: 10, GateY: 20 };
  f.ingest(frame(1, [event('warp-open', 1, 0, gate)]));
  assert.throws(() => f.ingest(frame(2, [event('warp-arrival', 2, 1, { ...gate, Target: 1, TargetId: 1 })])), /arrivals budget/);
});

test('empty diagnostics do not prove counter-inferred scenes; validate order and budgets', () => {
  const f = finder(); f.ingest(frame(1));
  assert.deepEqual(f.finish().missingTypes, ['group', 'heavy', 'warp']);
  assert.throws(() => f.ingest(frame(2)), /finished/);
  assert.throws(() => finder().ingest(frame(1, [shot(1, 1), shot(1, 0)])), /Order/);
  assert.throws(() => finder({ maxHistoryHits: 0 }).ingest(frame(1, [shot(1, 0)])), /budget overflow/);
  const w = finder({ maxWaves: 0 });
  assert.throws(() => w.ingest(frame(1, [event('warp-open', 1, 0)])), /waves budget overflow/);
  assert.equal(SCENE_DEFAULTS.maxShots, 24000);
});
