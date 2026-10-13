import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { parseSettings, selectEpisode, validateEpisode, validateRemoval, requiredCheckpointTicks, representativeKeyframes, frameMap, createCamera, fitPoints } from './scene-camera.mjs';
import { validateMedia, gameplayRegion, validatePixels, readVisualState, renderPresentationFrame, presentationEpisode, readCaptureSnapshot } from './capture-scene.mjs';

test('per-frame capture transfer contains only focus rows and the exact heavy projectile', () => {
  const saved = globalThis.window;
  try {
    const snapshot = { tick: 5, arenaHalfSize: 210, ships: [[1], [2], [3]], projectiles: [[10], [11]] };
    const e = { type: 'heavy', focus: { ships: [1], targets: [2], projectile: 10 } };
    globalThis.window = { spaceBattle: { snapshot }, __scenesPresentationEpisode: e };
    assert.deepEqual(readCaptureSnapshot(), { tick: 5, arenaHalfSize: 210, ships: [[1], [2]], projectiles: [[10]] });
    e.type = 'warp'; assert.deepEqual(readCaptureSnapshot().ships, []); assert.deepEqual(readCaptureSnapshot().projectiles, []);
    assert.equal(snapshot.ships.length, 3); assert.equal(snapshot.projectiles.length, 2);
  } finally { if (saved === undefined) delete globalThis.window; else globalThis.window = saved; }
});

test('presentation payload omits full causal history and is cached once, not sent per frame', () => {
  const episode = { type: 'warp', profile: 'high', seed: 1, startTick: 10, endTick: 900, eventTicks: [10, 40], focus: { ships: [1], targets: [] },
    evidence: { open: { Wave: 2, Tick: 20 }, arrivals: [{ Tick: 40, Target: 1 }], hits: Array.from({ length: 1000 }, (_, i) => ({ Tick: i, Damage: 100, Target: i })) } };
  const payload = presentationEpisode(episode);
  assert.deepEqual(payload.evidence, { open: { Wave: 2 }, arrivals: [{ Tick: 40 }] });
  assert.ok(JSON.stringify(payload).length < 500);
  assert.equal(episode.evidence.hits.length, 1000);
});
const { PNG } = createRequire(new URL('../../web/package.json', import.meta.url))('pngjs');

const episode = (type = 'group') => ({ schema: 1, id: 'event', type, profile: 'test', seed: 1,
  startTick: 10, endTick: 100, eventTick: 40, eventTicks: [40, 70], score: 1,
  focus: { ships: [1], targets: [2], projectile: null, gate: { x: 1900, y: -1900 } },
  cameraPoints: [{ x: -100, y: 80 }, { x: 900, y: -400 }], evidence: {} });
const args = ['--search', 'search.json', '--type', 'group', '--output', 'fresh'];

function causalEpisode(type = 'group') {
  const hit = { Type: 'hit', Tick: 40, Order: 40, Owner: 1, OwnerId: 11, OwnerGeneration: 1,
    Target: 2, TargetId: 12, TargetGeneration: 1, Team: 0, TargetTeam: 1, Damage: 10,
    HullBefore: 10, HullAfter: 0, Projectile: 3, EntityId: 30, EntityGeneration: 1, Kind: 4 };
  const destroy = { Type: 'destroy', Tick: 40, Order: 41, Target: 2, TargetId: 12, TargetGeneration: 1, TargetTeam: 1 };
  const e = { ...episode(type), eventTicks: [20, 40] };
  if (type === 'heavy') return { ...e, focus: { ...e.focus, projectile: 3 }, evidence: {
    shot: { ...hit, Type: 'shot', Tick: 20, Order: 20, ExpiresTick: 60 }, hit, destroy } };
  if (type === 'group') return { ...e, focus: { ...e.focus, ships: [1, 3] }, evidence: {
    leader: 1, roster: [1, 3], contributors: [1, 3], destroy,
    hits: [{ ...hit, Tick: 20, Order: 20, HullAfter: 10 }, { ...hit, Owner: 3, OwnerId: 13 }] } };
  return { ...e, eventTick: 70, eventTicks: [10, 20, 40, 70], evidence: {
    open: { Type: 'warp-open', Tick: 10, Order: 10, Team: 0, Wave: 1, Corner: 3, ArrivalTick: 20, GateX: 1900, GateY: -1900 },
    arrivals: [{ Type: 'warp-arrival', Tick: 20, Order: 20, Team: 0, Wave: 1, Corner: 3, ArrivalTick: 20,
      GateX: 1900, GateY: -1900, Target: 1, TargetId: 11, TargetGeneration: 1 }],
    hits: [hit], enemyKills: 1, reverseHits: 1, battle: { tick: 70, teams: { 0: 30, 1: 30 }, projectiles: 200 } } };
}
const coveredRun = e => ({ profile: e.profile, seed: e.seed, ticks: e.endTick,
  checkpoints: [...new Set([0, ...requiredCheckpointTicks(e)])].sort((a, b) => a - b)
    .map(tick => ({ tick, checksum: 'hash', counts: {}, counters: {}, teams: [1, 1] })) });

test('settings accept defaults and attached values; reject invalid inputs', () => {
  assert.equal(parseSettings(args).width, 1920);
  assert.equal(parseSettings(args).frameCodec, 'jpeg');
  assert.equal(parseSettings([...args, '--frame-codec', 'png']).frameCodec, 'png');
  assert.equal(parseSettings([...args, '--frame-codec', 'jpeg']).frameCodec, 'jpeg');
  assert.equal(parseSettings(args).annotate, 1);
  assert.equal(parseSettings([...args, '--annotate', '0']).annotate, 0);
  assert.equal(parseSettings([...args, '--annotate1']).annotate, 1);
  assert.equal(parseSettings([...args, '--width1920', '--height1080', '--fps60', '--slowmo2', '--rank1']).rank, 1);
  assert.equal(parseSettings([...args, '--rank12']).rank, 12);
  assert.equal(parseSettings([...args, '--rank', '2']).rank, 2);
  for (const extra of [['--width', '1919'], ['--height', 'NaN'], ['--fps30'], ['--slowmo3'], ['--annotate2'], ['--annotate', 'NaN'],
    ['--frame-codec', 'jpg'], ['--frame-codec'], ['--jpeg-quality', '0.5'], ['--wat', '1'], ['--id', 'x'], ['--rank0'], ['--rank13'], ['--rank', '1.5'], ['--width']])
    assert.throws(() => parseSettings([...args, ...extra]));
  assert.throws(() => parseSettings([...args, '--type', 'warp']));
});

test('ranks select the type-filtered stable shortlist; reject missing/out-of-range ranks', () => {
  const episodes = [
    { ...episode('warp'), id: 'warp', score: 999 },
    { ...episode(), id: 'b', score: 10, eventTick: 30 },
    { ...episode(), id: 'c', score: 20, eventTick: 40 },
    { ...episode(), id: 'a', score: 10, eventTick: 30 },
    { ...episode(), id: 'd', score: 10, eventTick: 40 }];
  assert.deepEqual([1, 2, 3, 4].map(rank => selectEpisode(episodes, { type: 'group', rank }).id), ['c', 'a', 'b', 'd']);
  assert.equal(selectEpisode(episodes, { id: 'b' }).id, 'b');
  assert.throws(() => selectEpisode(episodes, { type: 'group' }));
  assert.throws(() => selectEpisode(episodes, { type: 'group', rank: 5 }));
  assert.throws(() => selectEpisode(episodes, { type: 'group', rank: 13 }));
  assert.throws(() => selectEpisode(episodes, { id: 'missing' }));
  const twelve = Array.from({ length: 12 }, (_, i) => ({ ...episode(), id: `event-${i}`, score: 12 - i }));
  assert.equal(selectEpisode(twelve, { type: 'group', rank: 12 }).id, 'event-11');
});

test('frame maps include endpoints and repeat only exact heavy ticks', () => {
  const e = episode('heavy'), normal = frameMap(e), slow = frameMap(e, 2);
  assert.equal(normal.length, 91); assert.equal(slow.length, 182);
  assert.equal(slow.at(-1).tick, 100); assert.equal(slow.at(-1).pts, 181 / 60);
  for (let i = 0; i < slow.length; i += 2) assert.equal(slow[i].tick, slow[i + 1].tick);
  assert.throws(() => frameMap(episode(), 2));
  assert.throws(() => frameMap({ ...e, endTick: 10810 }));
});

test('episode ranges and ordered complete checkpoints are required', () => {
  const e = causalEpisode(), run = coveredRun(e);
  validateEpisode(e, run);
  for (const bad of [{ endTick: 101 }, { startTick: 40, endTick: 40 }, { eventTick: 9 }, { eventTicks: [101] }, { focus: {} }])
    assert.throws(() => validateEpisode({ ...e, ...bad }, run));
  assert.throws(() => validateEpisode(e, { ...run, checkpoints: [{ tick: 0 }] }));
  const heavy = causalEpisode('heavy');
  validateEpisode(heavy, run);
  assert.throws(() => validateEpisode({ ...heavy, startTick: 21 }, run));
  assert.throws(() => validateEpisode({ ...heavy, focus: { ...heavy.focus, projectile: 4 } }, run));
});

test('required start/events/end checkpoint coverage cannot be replaced by verified=true', () => {
  for (const type of ['group', 'heavy', 'warp']) {
    const e = causalEpisode(type), run = coveredRun(e);
    validateEpisode(e, run);
    for (const tick of requiredCheckpointTicks(e)) assert.throws(() => validateEpisode(e, {
      ...run, verified: true, checkpoints: run.checkpoints.filter(cp => cp.tick !== tick) }));
  }
});

test('zero-lead group deaths allow pre-impact validation outside the encoded range', () => {
  const e = causalEpisode(); e.startTick = 40; e.eventTicks = [40]; e.evidence.hits[0].Tick = 40;
  validateEpisode(e, coveredRun(e));
  assert.equal(validateRemoval(e, { tick: 39, ships: [[2]], projectiles: [] },
    { tick: 40, ships: [], projectiles: [] }).verified, true);
});

test('Pascal-case causal evidence rejects forged heavy, group and warp links', () => {
  for (const [type, mutate] of [
    ['heavy', e => { e.evidence.shot.OwnerGeneration++; }],
    ['heavy', e => { e.evidence.hit.EntityGeneration++; }],
    ['heavy', e => { e.evidence.hit.HullAfter = 1; }],
    ['group', e => { e.evidence.destroy.TargetGeneration++; }],
    ['group', e => { e.evidence.hits[0].Target = 99; }],
    ['group', e => { e.evidence.contributors = [1]; }],
    ['warp', e => { e.evidence.arrivals[0].Wave++; }],
    ['warp', e => { e.evidence.hits[0].OwnerGeneration++; }],
    ['warp', e => { e.evidence.battle.teams = { 0: 30 }; }],
  ]) {
    const e = causalEpisode(type), run = coveredRun(e); mutate(e);
    assert.throws(() => validateEpisode(e, run), `${type} tampering must fail`);
  }
});

test('dead heavy owner uses honest birth attribution without modifying the raw hit or victim', () => {
  const e = causalEpisode('heavy');
  e.evidence.shot.OwnerClass = 6;
  Object.assign(e.evidence.hit, { Owner: -1, Team: -1, OwnerClass: -1 });
  e.evidence.attribution = { source: 'shot-birth', ownerKey: '11:1', loggedOwner: -1, loggedTeam: -1, loggedOwnerClass: -1 };
  const raw = JSON.stringify(e.evidence.hit), run = coveredRun(e);
  validateEpisode(e, run); assert.equal(JSON.stringify(e.evidence.hit), raw);
  assert.equal(validateRemoval(e, { tick: 39, ships: [[2]], projectiles: [[3]] },
    { tick: 40, ships: [], projectiles: [] }).victim, 2);
  for (const mutate of [
    bad => { bad.evidence.hit.OwnerGeneration++; },
    bad => { bad.evidence.attribution.ownerKey = '99:1'; },
    bad => { bad.evidence.attribution.loggedTeam = 0; },
    bad => { bad.evidence.hit.Team = 0; },
    bad => { bad.evidence.hit.TargetTeam = bad.evidence.shot.Team; },
    bad => { bad.evidence.attribution.source = 'live-owner'; },
  ]) {
    const bad = structuredClone(e); mutate(bad); assert.throws(() => validateEpisode(bad, run));
  }
});

test('group birth proofs match intended victim, weapon/projectile and owner keys; legacy proofs remain compatible', () => {
  const legacy = causalEpisode(); validateEpisode(legacy, coveredRun(legacy));
  const e = causalEpisode();
  e.evidence.hits[1].Projectile = 4; e.evidence.hits[1].EntityId = 40;
  e.evidence.shots = e.evidence.hits.map(hit => ({ ...hit, Type: 'shot', Tick: hit.Tick - 5,
    Order: hit.Order - 1, ExpiresTick: hit.Tick + 10 }));
  e.eventTicks = [15, 20, 35, 40];
  const run = coveredRun(e); validateEpisode(e, run);
  assert.ok(requiredCheckpointTicks(e).includes(15) && requiredCheckpointTicks(e).includes(35));
  assert.throws(() => validateEpisode(e, { ...run, checkpoints: run.checkpoints.filter(cp => cp.tick !== 15) }));
  for (const mutate of [
    bad => { bad.evidence.shots[0].Target = 99; },
    bad => { bad.evidence.shots[0].TargetGeneration++; },
    bad => { bad.evidence.shots[0].OwnerGeneration++; },
    bad => { bad.evidence.shots[0].Owner = 99; },
    bad => { bad.evidence.shots[0].Kind = 0; },
    bad => { bad.evidence.shots[0].Projectile = 99; },
    bad => { bad.evidence.shots[0].EntityGeneration++; },
    bad => { bad.evidence.shots.pop(); },
    bad => { bad.evidence.shots.push({ ...bad.evidence.shots[0] }); },
    bad => { bad.evidence.shots = []; },
  ]) {
    const bad = structuredClone(e); mutate(bad); assert.throws(() => validateEpisode(bad, run));
  }
});

test('removal checks require exact before/impact ticks and actual victim/projectile disappearance', () => {
  for (const type of ['heavy', 'group']) {
    const e = causalEpisode(type), before = { tick: 39, ships: [[2]], projectiles: [[3]] }, after = { tick: 40, ships: [], projectiles: [] };
    assert.equal(validateRemoval(e, before, after).verified, true);
    assert.throws(() => validateRemoval(e, before, { ...after, ships: [[2]] }));
    assert.throws(() => validateRemoval(e, { ...before, ships: [] }, after));
    assert.throws(() => validateRemoval(e, before, { ...after, tick: 41 }));
    if (type === 'heavy') {
      assert.throws(() => validateRemoval(e, { ...before, projectiles: [] }, after));
      assert.throws(() => validateRemoval(e, before, { ...after, projectiles: [[3]] }));
    }
  }
});

test('representative keyframes stay at most 16 despite thousands of arrivals', () => {
  const e = { ...episode('warp'), endTick: 5000, eventTick: 4500,
    eventTicks: Array.from({ length: 4096 }, (_, i) => i + 10), evidence: {} };
  const keys = representativeKeyframes(e);
  assert.ok(keys.length <= 16); assert.equal(new Set(keys).size, keys.length);
  for (const t of [e.startTick, e.eventTick, e.endTick]) assert.ok(keys.includes(t));
  assert.ok(representativeKeyframes(causalEpisode('heavy')).includes(39));
  assert.equal(representativeKeyframes(causalEpisode('heavy'), 3).length, 3);
});

test('empty camera and missing entities have finite stable fallback; tails retain last focus', () => {
  assert.equal(fitPoints([], 1920, 1080), null);
  const camera = createCamera({ ...episode(), cameraPoints: [] }, { width: 1920, height: 1080 });
  const empty = camera({ tick: 10 }); assert.ok(empty.scale > 0);
  const focused = camera({ tick: 11, ships: [[1, 0, 0, 800, 900], [2, 0, 0, 1000, 900]] });
  const tail = camera({ tick: 12, ships: [] }); assert.ok(tail.x >= focused.x); assert.ok(Number.isFinite(tail.scale));
});

test('historical camera cache retains only selected focus IDs across unrelated births', () => {
  for (const type of ['group', 'warp']) {
    const camera = createCamera(episode(type), { width: 1920, height: 1080 });
    camera({ tick: 10, ships: [[1, 0, 0, 10, 10], [2, 0, 0, 30, 30]] });
    assert.equal(camera.cachedFocusCount, 2);
    for (let tick = 11; tick < 111; tick++) {
      const ships = Array.from({ length: 100 }, (_, i) => [1000 + tick * 100 + i, 0, 0, i, tick]);
      camera({ tick, ships });
      assert.equal(camera.cachedFocusCount, 2);
    }
    assert.throws(() => { camera.cachedFocusCount = 999; });
  }
});

const visualState = () => ({ ready: true, visibleSceneRows: 1, visibleSceneRowCounts: { ships: 1, projectiles: 0, particles: 0 },
  diagnostics: { error: null, contextLost: false }, visualAssets: {
  gunsAtlas: true, effectsAtlas: true, warpAtlas: true, teamTint: { hullTextures: 2, gunTextures: 2 },
  gunKinds: [1], projectileKinds: [1], particleKinds: [1], warpKinds: [1] } });
function pixelFixture({ count = 64, colors = 16, hudOnly = false, grayscale = false } = {}) {
  const image = new PNG({ width: 128, height: 96 });
  for (let i = 0; i < image.data.length; i += 4) {
    image.data[i] = 8; image.data[i + 1] = 10; image.data[i + 2] = 12; image.data[i + 3] = 255;
  }
  const region = gameplayRegion(image.width, image.height);
  for (let i = 0; i < count; i++) {
    const offset = ((hudOnly ? 0 : region.y) * image.width + region.x + i) * 4;
    image.data[offset] = grayscale ? 120 : 100 + i % colors;
    image.data[offset + 1] = grayscale ? 120 : 190;
    image.data[offset + 2] = grayscale ? 120 : 40;
  }
  return PNG.sync.read(PNG.sync.write(image));
}

test('decoded PNG gameplay pixels validate diversity, exclude HUD, and reject blank/monochrome frames', () => {
  const settings = { width: 128, height: 96 };
  const result = validatePixels(pixelFixture(), settings, visualState());
  assert.equal(result.coloredNonBackgroundPixels, 64); assert.equal(result.colorCount, 16);
  assert.equal(result.visualAssetsPresent, true); assert.equal(result.verified, true);
  assert.throws(() => validatePixels(pixelFixture(), settings, { ...visualState(), visibleSceneRows: 0 }),
    error => error.pixelEvidence.visibleSceneRows === 0, 'Colored stars alone cannot validate gameplay');
  for (const fixture of [{ count: 0 }, { count: 49 }, { colors: 1 }, { hudOnly: true }, { grayscale: true }])
    assert.throws(() => validatePixels(pixelFixture(fixture), settings, visualState()));
  const state = visualState(); state.visualAssets.effectsAtlas = false;
  assert.throws(() => validatePixels(pixelFixture(), settings, state), error => error.pixelEvidence.visualAssetsPresent === false);
  assert.throws(() => validatePixels(pixelFixture(), { ...settings, width: 64 }, visualState()));
});

test('foreground visibility reads refreshed binary rows after checksum heap growth and excludes debris/offscreen rows', () => {
  const saved = globalThis.window;
  const calls = [], camera = { width: 128, height: 96, originY: 48, x: 0, y: 0, scale: 1 };
  const fresh = { tick: 40, ships: [[1, 0, 0, 0, 0, 0, 0, 0, 2]],
    projectiles: [[3, 0, 0, 1000, 1000]], particles: [[4, 0, 0, 0, 0]], debris: [[5, 0, 0, 0, 0]] };
  try {
    const game = { snapshot: { tick: 40, ships: [], projectiles: [], particles: [] },
      refresh() { calls.push('refresh'); this.snapshot = fresh; } };
    globalThis.window = { spaceBattle: game, render_game_to_text() { calls.push('checksum'); return JSON.stringify({ tick: 40, camera }); } };
    const state = readVisualState();
    assert.deepEqual(calls, ['checksum', 'refresh']);
    assert.equal(state.visibleSceneRows, 2);
    assert.deepEqual(state.visibleSceneRowCounts, { ships: 1, projectiles: 0, particles: 1 });
    fresh.ships = []; fresh.particles = [];
    assert.equal(readVisualState().visibleSceneRows, 0, 'Gray debris cannot satisfy foreground presence');
    fresh.particles = [[4, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0]];
    assert.equal(readVisualState().visibleSceneRows, 0, 'Fully transparent particles do not count as visible foreground');
    fresh.projectiles = [[3, 0, 0, -60, -1, 0, 0, 0, 1]];
    assert.equal(readVisualState().visibleSceneRows, 1, 'Minimum projectile extent intersects the gameplay viewport');
  } finally { if (saved === undefined) delete globalThis.window; else globalThis.window = saved; }
});

test('16 full-HD keyframes remain under the 30M gameplay pixel scan budget', () => {
  const region = gameplayRegion(1920, 1080);
  assert.ok(region.width * region.height * 16 <= 30000000);
});

test('annotation composition draws only into a separate canvas; clean footage stays untouched', async () => {
  const saved = Object.fromEntries(['document', 'window', 'OffscreenCanvas'].map(key => [key, globalThis[key]]));
  const texts = [], draws = [], urls = [], blobs = []; let canvases = 0;
  const source = { width: 640, height: 360, toDataURL: (type, quality) => {
    urls.push({ type, quality });
    return `data:${type};base64,${Buffer.from(type === 'image/jpeg' ? 'clean-jpeg' : 'clean').toString('base64')}`;
  },
    getContext: () => { throw new Error('Production canvas must not be drawn into'); } };
  const context = { clearRect() {}, drawImage: (...args) => draws.push(args), save() {}, restore() {}, beginPath() {},
    rect() {}, clip() {}, moveTo() {}, lineTo() {}, stroke() {}, fillRect() {},
    measureText: text => ({ width: text.length * 7 }), fillText: text => texts.push(text) };
  try {
    globalThis.document = { querySelector: () => source };
    globalThis.window = { spaceBattle: { snapshot: { ships: [[1, 0, 0, 0, 0, 0, 0, 0, 2], [2, 0, 0, 80, 0, 0, 0, 0, 2]] } } };
    globalThis.OffscreenCanvas = class {
      constructor(width, height) { this.width = width; this.height = height; canvases++; }
      getContext() { return context; }
      async convertToBlob(options) { blobs.push(options); return new Blob([options.type === 'image/jpeg' ? 'overlay-jpeg' : 'overlay-png']); }
    };
    const e = { ...episode(), focus: { ...episode().focus, ships: [1, 999] } };
    const payload = { episode: e, frame: { tick: 40, camera: { x: 0, y: 0, scale: 1 } }, originY: 180, key: true };
    const clean = await renderPresentationFrame({ ...payload, annotate: 0 });
    assert.equal(Buffer.from(clean.frameBase64, 'base64').toString(), 'clean-jpeg');
    assert.equal(clean.keyPng, 'Y2xlYW4='); assert.equal(clean.productionPng, clean.keyPng);
    assert.deepEqual(urls, [{ type: 'image/png', quality: undefined }, { type: 'image/jpeg', quality: 0.97 }]);
    assert.equal(canvases, 0); assert.equal(draws.length, 0);
    const annotated = await renderPresentationFrame({ ...payload, annotate: 1 });
    assert.equal(annotated.productionPng, clean.productionPng); assert.notEqual(annotated.frameBase64, clean.frameBase64);
    assert.equal(Buffer.from(annotated.frameBase64, 'base64').toString(), 'overlay-jpeg');
    assert.equal(Buffer.from(annotated.keyPng, 'base64').toString(), 'overlay-png');
    assert.deepEqual(blobs, [{ type: 'image/jpeg', quality: 0.97 }, { type: 'image/png' }]);
    assert.equal(canvases, 1); assert.equal(draws[0][0], source);
    assert.ok(texts.includes('#1') && texts.includes('#2')); assert.ok(!texts.includes('#999'));
    assert.ok(texts.includes('GROUP / test / SEED 1 / TICK 40'));
    assert.ok(!texts.some(text => /kill|destroy|class/i.test(text)));
    const lossless = await renderPresentationFrame({ ...payload, annotate: 1, frameCodec: 'png' });
    assert.equal(lossless.frameBase64, lossless.keyPng);
    assert.equal(Buffer.from(lossless.frameBase64, 'base64').toString(), 'overlay-png');
    const cleanLossless = await renderPresentationFrame({ ...payload, annotate: 0, frameCodec: 'png' });
    assert.equal(cleanLossless.frameBase64, cleanLossless.productionPng);
    const beforeBlobs = blobs.length, beforeUrls = urls.length;
    const nonkey = await renderPresentationFrame({ ...payload, annotate: 1, key: false });
    assert.equal(nonkey.keyPng, null); assert.equal(nonkey.productionPng, null);
    assert.equal(blobs.length, beforeBlobs + 1); assert.equal(urls.length, beforeUrls);
    const cleanNonkey = await renderPresentationFrame({ ...payload, annotate: 0, key: false });
    assert.equal(cleanNonkey.productionPng, null); assert.equal(cleanNonkey.keyPng, null);
    assert.deepEqual(urls.at(-1), { type: 'image/jpeg', quality: 0.97 });
    await assert.rejects(renderPresentationFrame({ ...payload, annotate: 0, frameCodec: 'gif' }));
    texts.length = 0;
    const warp = causalEpisode('warp');
    warp.focus.ships = Array.from({ length: 128 }, (_, i) => i + 1);
    warp.focus.targets = [1, 2];
    warp.evidence.arrivals.push({ ...warp.evidence.arrivals[0], Tick: 60, Target: 2 });
    await renderPresentationFrame({ ...payload, episode: warp, annotate: 1 });
    assert.equal(texts.length, 1, 'Warp renders only its header, no arrival/target serial labels');
    assert.equal(texts[0], 'WARP / test / SEED 1 / TICK 40 / WAVE 1 / ARRIVALS 1');
  } finally {
    for (const [key, value] of Object.entries(saved)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
});

test('pixel validation excludes the full 48px header and bottom 8px annotation strips', () => {
  for (const [width, height] of [[128, 96], [1280, 720], [1920, 1080]]) {
    const region = gameplayRegion(width, height);
    assert.ok(region.y >= 48 && region.y + region.height <= height - 8);
  }
  const image = pixelFixture({ count: 0 });
  for (let y = 0; y < image.height; y++) if (y < 48 || y >= image.height - 8)
    for (let x = 0; x < image.width; x++) {
      const at = (y * image.width + x) * 4;
      image.data[at] = 100 + x % 16; image.data[at + 1] = 190; image.data[at + 2] = 40;
    }
  assert.throws(() => validatePixels(image, { width: 128, height: 96 }, visualState()));
});

test('heavy camera stays fixed and fits all flight points, including edge positions', () => {
  const e = episode('heavy'), dimensions = { width: 1920, height: 1080, originY: 555 };
  const camera = createCamera(e, dimensions), first = camera({ tick: 10 });
  assert.deepEqual(camera({ tick: 100, ships: [[1, 0, 0, 9999, 9999]] }), first);
  for (const p of e.cameraPoints) {
    const x = 960 + (p.x - first.x) * first.scale, y = 555 - (p.y - first.y) * first.scale;
    assert.ok(x >= 64 && x <= 1856 && y >= 64 && y <= 1016);
  }
});

test('group edge focus stays visible despite smooth camera lag', () => {
  const camera = createCamera(episode(), { width: 640, height: 360 });
  camera({ tick: 10, ships: [[1, 0, 0, 0, 0]] });
  const c = camera({ tick: 11, ships: [[1, 0, 0, 10000, -10000], [2, 0, 0, -5000, 8000]] });
  for (const p of [{ x: 10000, y: -10000 }, { x: -5000, y: 8000 }]) {
    assert.ok(Math.abs((p.x - c.x) * c.scale) <= 288);
    assert.ok(Math.abs((p.y - c.y) * c.scale) <= 148);
  }
});

test('warp starts at gate and ends with the whole arena visible', () => {
  const camera = createCamera(episode('warp'), { width: 1920, height: 1080 });
  const first = camera({ tick: 10 }); assert.equal(first.x, 1900); assert.equal(first.y, -1900);
  const last = camera({ tick: 100 }); assert.equal(last.x, 0); assert.equal(last.y, 0);
  assert.ok(last.scale * 2000 <= 508);
});

test('warp transition follows recorded arrivals, before the late battle event', () => {
  const e = { ...episode('warp'), endTick: 1000, eventTick: 900, evidence: { arrivals: [{ Tick: 60 }] } };
  const camera = createCamera(e, { width: 1920, height: 1080 });
  camera({ tick: 10 });
  const moving = camera({ tick: 500 }); assert.ok(moving.x < 1900 && moving.x > 0);
  assert.equal(camera({ tick: 1000 }).x, 0);
});

test('warp lead-in stays wide, approaches open-60..open+30, holds after arrivals, then returns wide', () => {
  const e = { ...episode('warp'), startTick: 1440, endTick: 2300, eventTick: 2100,
    evidence: { open: { Tick: 1560 }, arrivals: [{ Tick: 1650 }] } };
  const camera = createCamera(e, { width: 1280, height: 720 });
  const first = camera({ tick: 1440 }); assert.equal(first.x, 0); assert.equal(first.y, 0);
  assert.deepEqual(camera({ tick: 1500 }), first);
  const approach = camera({ tick: 1530 }); assert.ok(approach.x > 0 && approach.x < 1900);
  const gate = camera({ tick: 1590 }); assert.equal(gate.x, 1900); assert.equal(gate.y, -1900);
  assert.deepEqual(camera({ tick: 1770 }), gate);
  const returning = camera({ tick: 2000 }); assert.ok(returning.x > 0 && returning.x < 1900);
  assert.deepEqual(camera({ tick: 2300 }), first);
});

test('media validation checks both streams, dimensions, frame count and duration', () => {
  const settings = { width: 1920, height: 1080 }, probe = { streams: [
    { codec_type: 'video', codec_name: 'h264', pix_fmt: 'yuv420p', width: 1920, height: 1080,
      r_frame_rate: '60/1', avg_frame_rate: '60/1', nb_frames: '120', nb_read_frames: '120', duration: '2' },
    { codec_type: 'audio', codec_name: 'aac', sample_rate: '48000', channels: 2, duration: '2' }], format: { duration: '2' } };
  validateMedia(probe, settings, 120);
  for (const field of ['codec_name', 'pix_fmt', 'width', 'avg_frame_rate', 'nb_read_frames', 'duration']) {
    const bad = structuredClone(probe); bad.streams[0][field] = 'bad'; assert.throws(() => validateMedia(bad, settings, 120));
  }
  assert.throws(() => validateMedia({ ...probe, streams: [probe.streams[0]] }, settings, 120));
});
