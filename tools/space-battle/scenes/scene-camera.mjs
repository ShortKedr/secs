import assert from 'node:assert/strict';

export function parseSettings(args) {
  const values = { width: 1920, height: 1080, fps: 60, slowmo: 1, annotate: 1, frameCodec: 'jpeg', ffmpeg: 'ffmpeg', ffprobe: 'ffprobe', rank: 0 };
  const names = new Set(['search', 'type', 'id', 'output', 'width', 'height', 'fps', 'slowmo', 'annotate', 'frame-codec', 'ffmpeg', 'ffprobe']);
  const seen = new Set();
  for (let i = 0; i < args.length; i++) {
    const match = /^--([a-z]+(?:-[a-z]+)*)(\d+)?$/.exec(args[i]);
    assert.ok(match, `Invalid argument: ${args[i]}`);
    const [, name, attached] = match;
    assert.ok(!seen.has(name), `Duplicate option: ${name}`); seen.add(name);
    if (name === 'rank') {
      values.rank = Number(attached ?? args[++i]);
      assert.ok(Number.isSafeInteger(values.rank) && values.rank >= 1 && values.rank <= 12, 'Rank must be 1..12');
      continue;
    }
    assert.ok(names.has(name), `Unknown option: ${name}`);
    const value = attached ?? args[++i];
    assert.ok(value && !value.startsWith('--'), `Missing value for --${name}`);
    values[name === 'frame-codec' ? 'frameCodec' : name] = ['width', 'height', 'fps', 'slowmo', 'annotate'].includes(name) ? Number(value) : value;
  }
  assert.ok(values.search && values.output, '--search and --output are required');
  assert.ok(Boolean(values.id) !== Boolean(values.type), 'Choose exactly one of --type or --id');
  assert.ok(!values.id || !values.rank, 'Use either exact --id or ranked --type selection');
  if (values.type) assert.ok(['group', 'heavy', 'warp'].includes(values.type), 'Invalid type');
  for (const name of ['width', 'height']) assert.ok(Number.isSafeInteger(values[name]) && values[name] >= 64 && values[name] <= 8192 && values[name] % 2 === 0, `${name} must be even, 64..8192`);
  assert.equal(values.fps, 60, 'Only 60 fps is supported');
  assert.ok([1, 2].includes(values.slowmo), 'slowmo must be 1 or 2');
  assert.ok([0, 1].includes(values.annotate), 'annotate must be 0 or 1');
  assert.ok(['jpeg', 'png'].includes(values.frameCodec), 'frame-codec must be jpeg or png');
  return values;
}

export function selectEpisode(episodes, { id, type, rank = 0 }) {
  assert.ok(Array.isArray(episodes), 'Invalid episode shortlist');
  const candidates = episodes.filter(e => id ? e.id === id : e.type === type);
  assert.ok(candidates.length, 'No matching episode');
  assert.equal(new Set(candidates.map(e => e.id)).size, candidates.length, 'Ambiguous episode IDs');
  if (id) { assert.equal(candidates.length, 1); return candidates[0]; }
  assert.ok(Number.isSafeInteger(rank) && rank >= 0 && rank <= 12, 'Rank must be 1..12');
  if (candidates.length > 1) assert.ok(rank > 0, 'Multiple matches; specify --rank1..12 or --id');
  for (const e of candidates) assert.ok(Number.isFinite(e.score) && Number.isSafeInteger(e.eventTick) && typeof e.id === 'string', 'Invalid ranking fields');
  candidates.sort((a, b) => b.score - a.score || a.eventTick - b.eventTick || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
  const index = (rank || 1) - 1;
  assert.ok(index < candidates.length, `Rank ${rank} exceeds ${candidates.length} matching episodes`);
  return candidates[index];
}

function sourceEvents(e) {
  const v = e.evidence;
  return e.type === 'heavy' ? [v.shot, v.hit, v.destroy] : e.type === 'group' ? [...(v.shots ?? []), ...(v.hits ?? []), v.destroy] : [v.open, ...(v.arrivals ?? []), ...(v.hits ?? [])];
}

export function requiredCheckpointTicks(e) {
  return [...new Set([e.startTick, e.eventTick, e.endTick, ...e.eventTicks, ...sourceEvents(e).filter(Boolean).map(event => event.Tick)])].sort((a, b) => a - b);
}

export function representativeKeyframes(e, limit = 16) {
  assert.ok(Number.isSafeInteger(limit) && limit >= 3 && limit <= 16, 'Keyframe limit must be 3..16');
  const priority = [e.startTick, e.eventTick, e.endTick];
  if (e.type === 'heavy') priority.push(e.evidence.shot?.Tick, e.eventTick - 1);
  if (e.type === 'group') priority.push(e.eventTick - 1);
  const selected = new Set([...new Set(priority.filter(t => Number.isSafeInteger(t) && t >= e.startTick && t <= e.endTick))].slice(0, limit));
  const remaining = requiredCheckpointTicks(e).filter(t => !selected.has(t));
  const count = Math.min(limit - selected.size, remaining.length);
  for (let i = 0; i < count; i++) selected.add(remaining[Math.floor((i + 0.5) * remaining.length / count)]);
  return [...selected].sort((a, b) => a - b);
}

const sameIdentity = (a, ap, b, bp) => ['Id', 'Generation'].every(suffix =>
  Number.isSafeInteger(a[ap + suffix]) && a[ap + suffix] >= 0 && a[ap + suffix] === b[bp + suffix]);
const hostileHit = h => h.Type === 'hit' && Number.isSafeInteger(h.Team) && h.Team >= 0 &&
  Number.isSafeInteger(h.TargetTeam) && h.TargetTeam >= 0 && h.Team !== h.TargetTeam && Number.isFinite(h.Damage) && h.Damage > 0;
const lethalHit = h => hostileHit(h) && Number.isFinite(h.HullBefore) && h.HullBefore > 0 && h.HullAfter === 0;
const isHeavy = kind => kind === 4 || kind === 'HeavyCannon4';

function validateEvidence(e) {
  const v = e.evidence, events = sourceEvents(e);
  assert.ok(events.length && events.every(event => event && Number.isSafeInteger(event.Tick) && event.Tick >= e.startTick && event.Tick <= e.endTick &&
    Number.isSafeInteger(event.Order) && event.Order >= 0), 'Invalid source event range/order');
  assert.ok(events.every(event => e.eventTicks.includes(event.Tick)), 'Source event tick omitted from eventTicks');
  if (e.type === 'heavy' || e.type === 'group') {
    assert.ok(e.eventTick > 0, 'Removal evidence requires a pre-impact simulation tick');
    const rawHit = e.type === 'heavy' ? v.hit : v.hits?.at(-1), d = v.destroy;
    let h = rawHit;
    if (e.type === 'heavy' && rawHit?.Owner === -1) {
      const s = v.shot, attribution = v.attribution;
      assert.ok(s && rawHit.Team === -1 && rawHit.OwnerClass === -1 && sameIdentity(s, 'Owner', rawHit, 'Owner') &&
        attribution?.source === 'shot-birth' && attribution.ownerKey === `${s.OwnerId}:${s.OwnerGeneration}` &&
        attribution.loggedOwner === rawHit.Owner && attribution.loggedTeam === rawHit.Team &&
        attribution.loggedOwnerClass === rawHit.OwnerClass, 'Invalid dead-owner shot-birth attribution');
      h = { ...rawHit, Owner: s.Owner, Team: s.Team, OwnerClass: s.OwnerClass };
    } else if (e.type === 'heavy') {
      assert.ok(!v.attribution || v.attribution.source === 'live-owner', 'Live hit cannot claim shot-birth attribution');
    }
    assert.ok(h && d && lethalHit(h) && d.Type === 'destroy' && d.Tick === h.Tick && d.Order > h.Order &&
      h.Tick === e.eventTick && d.Target === h.Target && d.TargetTeam === h.TargetTeam && sameIdentity(h, 'Target', d, 'Target'), 'Invalid lethal hit/destroy linkage');
    assert.ok(e.focus.targets.includes(h.Target), 'Victim missing from focus');
    if (e.type === 'heavy') {
      const s = v.shot;
      assert.ok(s.Type === 'shot' && isHeavy(s.Kind) && isHeavy(h.Kind) && s.Tick < h.Tick &&
        s.Order < h.Order && s.ExpiresTick >= h.Tick && s.Owner === h.Owner && s.Team === h.Team &&
        s.Projectile === h.Projectile && s.Projectile === e.focus.projectile && sameIdentity(s, 'Owner', h, 'Owner') &&
        sameIdentity(s, 'Entity', h, 'Entity') && e.focus.ships.includes(h.Owner), 'Invalid heavy shot/hit causal linkage');
    } else {
      assert.ok(Array.isArray(v.roster) && v.roster.length >= 2 && v.roster.length <= 4 && new Set(v.roster).size === v.roster.length &&
        Number.isSafeInteger(v.leader) && v.leader >= 0 && v.roster.includes(v.leader), 'Invalid group roster');
      assert.ok(Array.isArray(v.hits) && v.hits.length >= 2 && v.hits.every((hit, i) => hostileHit(hit) && hit.Team === h.Team &&
        hit.Target === h.Target && hit.TargetTeam === h.TargetTeam && sameIdentity(hit, 'Target', h, 'Target') &&
        sameIdentity(hit, 'Owner', hit, 'Owner') && v.roster.includes(hit.Owner) &&
        (!i || hit.Tick >= v.hits[i - 1].Tick && hit.Order > v.hits[i - 1].Order)), 'Invalid shared-target group hits');
      const contributors = [...new Set(v.hits.map(hit => hit.Owner))].sort((a, b) => a - b);
      assert.ok(contributors.length >= 2, 'Group needs multiple actual contributors');
      assert.deepEqual([...v.contributors].sort((a, b) => a - b), contributors, 'Contributor evidence mismatch');
      assert.deepEqual([...e.focus.ships].sort((a, b) => a - b), contributors, 'Group focus mismatch');
      if (Object.hasOwn(v, 'shots')) {
        assert.ok(Array.isArray(v.shots), 'Invalid group shot-birth evidence');
        const births = new Map(v.shots.map(shot => [shot.Projectile, shot]));
        assert.ok(births.size === v.shots.length && births.size === new Set(v.hits.map(hit => hit.Projectile)).size,
          'Group needs exactly one birth record per hit projectile');
        for (const hit of v.hits) {
          const shot = births.get(hit.Projectile);
          assert.ok(shot && shot.Type === 'shot' && Number.isSafeInteger(shot.Projectile) && shot.Projectile >= 0 &&
            shot.Tick <= hit.Tick && shot.Order < hit.Order && shot.ExpiresTick >= hit.Tick &&
            (shot.Kind === hit.Kind || isHeavy(shot.Kind) && isHeavy(hit.Kind)) &&
            shot.Owner === hit.Owner && shot.Team === hit.Team && sameIdentity(shot, 'Owner', hit, 'Owner') &&
            sameIdentity(shot, 'Entity', hit, 'Entity') && shot.Target === hit.Target && shot.TargetTeam === hit.TargetTeam &&
            sameIdentity(shot, 'Target', hit, 'Target'), 'Invalid group intended-target shot/hit linkage');
        }
      }
    }
  } else {
    const o = v.open;
    assert.ok(o.Type === 'warp-open' && e.focus.gate.x === o.GateX && e.focus.gate.y === o.GateY, 'Warp gate evidence mismatch');
    assert.ok(Array.isArray(v.arrivals) && v.arrivals.length && v.arrivals.every(a => a.Type === 'warp-arrival' &&
      a.Tick >= o.Tick && a.Tick >= o.ArrivalTick && ['Team', 'Wave', 'Corner', 'ArrivalTick', 'GateX', 'GateY'].every(k => a[k] === o[k])), 'Warp arrival linkage mismatch');
    const serial = a => Number.isSafeInteger(a.Target) && a.Target >= 0 ? a.Target : a.Entity ?? a.Serial;
    assert.deepEqual([...e.focus.ships].sort((a, b) => a - b), [...new Set(v.arrivals.map(serial))].sort((a, b) => a - b), 'Warp focus mismatch');
    assert.ok(Array.isArray(v.hits) && v.hits.length && v.hits.every(h => hostileHit(h) && h.Team === o.Team && v.arrivals.some(a =>
      serial(a) === h.Owner && a.Tick <= h.Tick && (sameIdentity(a, 'Target', h, 'Owner') || sameIdentity(a, 'Entity', h, 'Owner')))), 'Warp attack causal linkage mismatch');
    assert.deepEqual([...e.focus.targets].sort((a, b) => a - b), [...new Set(v.hits.map(h => h.Target))].sort((a, b) => a - b), 'Warp target focus mismatch');
    assert.ok(Number.isSafeInteger(v.enemyKills) && v.enemyKills > 0 && v.enemyKills <= v.hits.filter(lethalHit).length &&
      Number.isSafeInteger(v.reverseHits) && v.reverseHits > 0, 'Warp battle evidence missing');
    assert.ok(v.battle && Number.isSafeInteger(v.battle.tick) && v.battle.tick >= o.Tick && v.battle.tick <= e.eventTick &&
      Object.values(v.battle.teams ?? {}).filter(n => Number.isSafeInteger(n) && n > 0).length >= 2 && v.battle.projectiles > 0, 'Invalid full-battle evidence');
  }
}

export function validateRemoval(e, before, after) {
  assert.ok(['heavy', 'group'].includes(e.type), 'Removal checks apply to heavy/group episodes');
  const hit = e.type === 'heavy' ? e.evidence.hit : e.evidence.hits.at(-1);
  assert.equal(before.tick, hit.Tick - 1, 'Before-impact tick must be exact');
  assert.equal(after.tick, hit.Tick, 'Impact tick must be exact');
  assert.ok(before.ships.some(r => r[0] === hit.Target), 'Victim missing before recorded impact');
  assert.ok(!after.ships.some(r => r[0] === hit.Target), 'Recorded victim was not removed at impact');
  if (e.type === 'heavy') {
    assert.ok(before.projectiles.some(r => r[0] === hit.Projectile), 'Heavy projectile missing before recorded impact');
    assert.ok(!after.projectiles.some(r => r[0] === hit.Projectile), 'Heavy projectile survived recorded impact');
  }
  return { beforeTick: before.tick, impactTick: after.tick, victim: hit.Target, projectile: e.type === 'heavy' ? hit.Projectile : null, verified: true };
}

export function validateEpisode(e, run) {
  assert.ok(e && e.schema === 1 && typeof e.id === 'string' && e.id.length && ['group', 'heavy', 'warp'].includes(e.type), 'Invalid episode');
  assert.ok(run && run.profile === e.profile && run.seed === e.seed, 'Episode run missing');
  for (const key of ['startTick', 'endTick', 'eventTick']) assert.ok(Number.isSafeInteger(e[key]) && e[key] >= 0, `Invalid ${key}`);
  assert.ok(Number.isSafeInteger(run.ticks) && e.startTick < e.endTick && e.endTick <= run.ticks && e.eventTick >= e.startTick && e.eventTick <= e.endTick, 'Stale or invalid episode range');
  assert.ok(Array.isArray(e.eventTicks) && e.eventTicks.every(t => Number.isSafeInteger(t) && t >= e.startTick && t <= e.endTick), 'Invalid event ticks');
  assert.ok(e.focus && Array.isArray(e.focus.ships) && Array.isArray(e.focus.targets), 'Missing focus');
  for (const id of [...e.focus.ships, ...e.focus.targets]) assert.ok(Number.isSafeInteger(id) && id >= 0, 'Invalid focus serial');
  assert.ok(e.focus.projectile === null || (Number.isSafeInteger(e.focus.projectile) && e.focus.projectile >= 0), 'Invalid projectile');
  assert.ok(Array.isArray(e.cameraPoints) && e.cameraPoints.every(validPoint), 'Invalid camera points');
  assert.ok(e.evidence && typeof e.evidence === 'object', 'Missing evidence');
  if (e.type === 'heavy') {
    assert.ok(e.cameraPoints.length >= 2, 'Heavy capture requires recorded shooter/path/victim points');
    const { shot, hit, destroy } = e.evidence;
    assert.ok(shot && hit && destroy && Number.isSafeInteger(shot.Tick) && shot.Tick >= e.startTick &&
      shot.Tick <= hit.Tick && hit.Tick === e.eventTick && destroy.Tick === hit.Tick, 'Heavy range must contain the complete recorded flight and lethal hit');
    assert.ok(e.focus.projectile !== null && shot.Projectile === e.focus.projectile && hit.Projectile === e.focus.projectile &&
      e.focus.ships.includes(shot.Owner) && e.focus.targets.includes(hit.Target), 'Heavy focus/evidence mismatch');
  }
  if (e.type === 'warp') assert.ok(validPoint(e.focus.gate), 'Warp capture requires a recorded gate');
  validateEvidence(e);
  assert.ok(Array.isArray(run.checkpoints) && run.checkpoints.length, 'Run checkpoints missing');
  let previous = -1;
  for (const cp of run.checkpoints) {
    assert.ok(Number.isSafeInteger(cp.tick) && cp.tick > previous && cp.tick <= run.ticks, 'Unordered run checkpoints');
    previous = cp.tick;
    for (const field of ['checksum', 'counts', 'counters', 'teams']) assert.ok(cp[field] !== undefined, `Checkpoint missing ${field}`);
  }
  const covered = new Set(run.checkpoints.map(cp => cp.tick));
  for (const tick of requiredCheckpointTicks(e)) assert.ok(covered.has(tick), `Missing required checkpoint at ${tick}`);
}

export function frameMap(e, slowmo = 1) {
  assert.ok(Number.isSafeInteger(e.startTick) && Number.isSafeInteger(e.endTick) && e.startTick >= 0 && e.endTick >= e.startTick, 'Frame map requires integer source ticks');
  assert.ok([1, 2].includes(slowmo), 'Invalid slowmo');
  assert.ok(slowmo === 1 || e.type === 'heavy', 'Slow motion is only available for heavy episodes');
  const count = (e.endTick - e.startTick + 1) * slowmo;
  assert.ok(Number.isSafeInteger(count) && count > 0 && count <= 10800, 'Capture exceeds 180 seconds / 10800 frames');
  return Array.from({ length: count }, (_, index) => ({ index, pts: index / 60, tick: e.startTick + Math.floor(index / slowmo), repeat: index % slowmo }));
}

const validPoint = p => p && Number.isFinite(p.x) && Number.isFinite(p.y);
const smoothstep = t => { t = Math.max(0, Math.min(1, t)); return t * t * (3 - 2 * t); };

export function fitPoints(points, width, height, originY = height / 2, padding = 64) {
  const good = points.filter(validPoint);
  if (!good.length) return null;
  const minX = Math.min(...good.map(p => p.x)), maxX = Math.max(...good.map(p => p.x));
  const minY = Math.min(...good.map(p => p.y)), maxY = Math.max(...good.map(p => p.y));
  const usableHeight = Math.max(1, 2 * Math.min(originY - padding, height - originY - padding));
  return { x: (minX + maxX) / 2, y: (minY + maxY) / 2,
    scale: Math.min(16, Math.max(1, width - padding * 2) / Math.max(80, maxX - minX + 48), usableHeight / Math.max(80, maxY - minY + 48)) };
}

const groupCameraRevision = 'group-cinema-v2';
const zoomRate = Math.log(1.25) / 60;

function soften(values, radius) {
  return values.map((_, i) => {
    let sum = 0, weight = 0;
    for (let offset = -radius; offset <= radius; offset++) {
      const w = radius + 1 - Math.abs(offset);
      sum += values[Math.max(0, Math.min(values.length - 1, i + offset))] * w;
      weight += w;
    }
    return sum / weight;
  });
}

// Offline presentation only: survey the recorded window before choosing continuous framing.
export function planGroupCamera(e, snapshots, { width, height, originY = height / 2 }) {
  assert.equal(e.type, 'group', 'Group camera needs a group episode');
  assert.ok(Number.isSafeInteger(e.startTick) && Number.isSafeInteger(e.endTick) && e.startTick >= 0 && e.endTick >= e.startTick && e.endTick <= 18000);
  assert.ok(snapshots.length === e.endTick - e.startTick + 1 && snapshots.length <= 10800, 'Complete bounded camera survey required');
  assert.ok([width, height, originY].every(Number.isFinite) && width >= 64 && height >= 64 && originY > 0 && originY < height);
  const ships = [...new Set(e.focus.ships)].sort((a, b) => a - b);
  const targets = [...new Set(e.focus.targets)].sort((a, b) => a - b);
  assert.ok(ships.length <= 4 && targets.length <= 4, 'Group camera focus budget exceeded');
  const ids = new Set([...ships, ...targets]), last = new Map(), desired = [], visible = [], fallbacks = [];
  const margin = Math.min(64, width / 4, height / 4, originY / 2, (height - originY) / 2);
  const blend = (a, b, weight) => ({ x: a.x + (b.x - a.x) * weight, y: a.y + (b.y - a.y) * weight });
  const centerOf = points => {
    const weight = points.reduce((n, p) => n + p.weight, 0);
    return weight > 0 ? { x: points.reduce((n, p) => n + p.x * p.weight, 0) / weight,
      y: points.reduce((n, p) => n + p.y * p.weight, 0) / weight } : null;
  };
  for (let i = 0; i < snapshots.length; i++) {
    const snapshot = snapshots[i], tick = e.startTick + i;
    assert.equal(snapshot.tick, tick, 'Camera survey must have contiguous exact ticks');
    const alive = new Map();
    for (const row of snapshot.ships ?? []) if (ids.has(row[0]) && Number.isFinite(row[3]) && Number.isFinite(row[4])) {
      const p = { x: row[3], y: row[4], radius: Math.max(24, Number.isFinite(row[8]) ? row[8] * 2 : 24), tick };
      alive.set(row[0], p); last.set(row[0], p);
    }
    const points = selected => selected.map(id => last.get(id)).filter(Boolean).map(p => ({ ...p,
      weight: 1 - smoothstep((tick - p.tick - 30) / 60) })).filter(p => p.weight > 0);
    const main = centerOf(points(ships)), targetPoints = points(targets), victim = centerOf(targetPoints);
    const targetWeight = targets.length ? targetPoints.reduce((n, p) => n + p.weight, 0) / targets.length : 0;
    const arena = Number.isFinite(snapshot.arenaHalfSize) && snapshot.arenaHalfSize > 0 ? snapshot.arenaHalfSize : 2000;
    const fallback = fitPoints(e.cameraPoints ?? [], width, height, originY) ??
      fitPoints([{ x: -arena, y: -arena }, { x: arena, y: arena }], width, height, originY);
    fallbacks.push(fallback);
    desired.push(main && victim ? blend(main, victim, 0.35 * targetWeight) : main ?? victim ?? desired.at(-1) ?? fallback);
    visible.push([...alive.values()]);
  }
  // Two finite, symmetric filters soften both velocity and acceleration at focus changes.
  const xs = soften(soften(desired.map(p => p.x), 36), 18);
  const ys = soften(soften(desired.map(p => p.y), 36), 18);
  const safeZoom = visible.map((points, i) => {
    let scale = Math.min(16, points.length ? fitPoints(points, width, height, originY).scale : fallbacks[i].scale);
    for (const p of points) scale = Math.min(scale,
      (width / 2 - margin) / (Math.abs(p.x - xs[i]) + p.radius),
      (Math.min(originY, height - originY) - margin) / (Math.abs(p.y - ys[i]) + p.radius));
    return Math.log(scale);
  });
  // A rate-limited lower envelope anticipates expansion; it never needs a one-frame zoom cut.
  const envelope = [...safeZoom];
  for (let i = 1; i < envelope.length; i++) envelope[i] = Math.min(envelope[i], envelope[i - 1] + zoomRate);
  for (let i = envelope.length - 2; i >= 0; i--) envelope[i] = Math.min(envelope[i], envelope[i + 1] + zoomRate);
  const radius = 24, logs = soften(envelope, radius);
  // Subtract the filter's Lipschitz bound, so smoothing cannot crop any surveyed focus.
  const allowance = zoomRate * Array.from({ length: radius * 2 + 1 }, (_, i) => (radius + 1 - Math.abs(i - radius)) * Math.abs(i - radius))
    .reduce((a, b) => a + b, 0) / ((radius + 1) ** 2);
  const frames = snapshots.map((s, i) => ({ tick: s.tick, x: xs[i], y: ys[i], scale: Math.exp(logs[i] - allowance) }));
  assert.ok(frames.every(c => [c.x, c.y, c.scale].every(Number.isFinite) && c.scale > 0), 'Invalid planned camera');
  return { revision: groupCameraRevision, frames, focusPolicy: 'stable-contributors-and-victim',
    centerFilterRadiiTicks: [36, 18], lostFocusHoldTicks: 30, lostFocusFadeTicks: 60,
    zoomFilterRadiusTicks: radius, maximumZoomRatioPerSecond: 1.25, paddingPixels: margin,
    note: 'Offline framing anticipates surveyed motion; it never changes AI targets or game time.' };
}

export function createCamera(e, { width, height, originY = height / 2 }) {
  let camera = null, previousTick = null;
  const last = new Map();
  const focusIds = new Set([...e.focus.ships, ...e.focus.targets]);
  const heavy = fitPoints(e.cameraPoints, width, height, originY);
  function update({ tick, ships = [], arenaHalfSize = 2000 }) {
    for (const row of ships) if (focusIds.has(row[0]) && Number.isFinite(row[3]) && Number.isFinite(row[4]))
      last.set(row[0], { x: row[3], y: row[4] });
    const full = fitPoints([{ x: -arenaHalfSize, y: -arenaHalfSize }, { x: arenaHalfSize, y: arenaHalfSize }], width, height, originY);
    const focus = [...focusIds].map(id => last.get(id)).filter(Boolean);
    let desired;
    if (e.type === 'heavy') desired = heavy;
    else if (e.type === 'warp') {
      const gate = fitPoints([e.focus.gate], width, height, originY);
      const arrivalTick = Math.min(...(e.evidence?.arrivals ?? []).map(a => a.Tick).filter(Number.isSafeInteger));
      const openTick = e.evidence?.open?.Tick ?? e.startTick - 90;
      const approach = smoothstep((tick - (openTick - 60)) / 90);
      const transitionStart = Number.isFinite(arrivalTick) ? Math.min(e.endTick - 1, Math.max(openTick + 30, arrivalTick + 120)) : e.eventTick;
      const blend = smoothstep((tick - transitionStart) / Math.max(1, e.endTick - transitionStart));
      desired = Object.fromEntries(['x', 'y', 'scale'].map(k => {
        const approaching = full[k] + (gate[k] - full[k]) * approach;
        return [k, approaching + (full[k] - approaching) * blend];
      }));
      if (blend === 1) desired = { ...full };
    } else desired = fitPoints(focus, width, height, originY) || camera || fitPoints(e.cameraPoints, width, height, originY) || full;
    if (!camera || e.type === 'heavy' || e.type === 'warp') camera = { ...desired };
    else {
      const alpha = 1 - Math.exp(-Math.max(0, tick - previousTick) / 12);
      for (const key of ['x', 'y', 'scale']) camera[key] += (desired[key] - camera[key]) * alpha;
      // Expand immediately when necessary; smooth centering must not crop moving focus.
      if (e.type === 'group') for (const p of focus) {
        const margin = Math.min(64, width / 4, height / 4);
        camera.scale = Math.min(camera.scale, (width / 2 - margin) / (Math.abs(p.x - camera.x) + 24),
          (Math.min(originY, height - originY) - margin) / (Math.abs(p.y - camera.y) + 24));
      }
      if (e.type === 'warp' && tick === e.endTick) camera = { ...full };
    }
    previousTick = tick;
    assert.ok(['x', 'y', 'scale'].every(k => Number.isFinite(camera[k])) && camera.scale > 0, 'Invalid camera');
    return { ...camera };
  }
  Object.defineProperty(update, 'cachedFocusCount', { get: () => last.size });
  return update;
}
