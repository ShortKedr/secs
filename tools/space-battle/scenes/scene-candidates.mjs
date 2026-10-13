// Pure incremental diagnostic consumer; safe to import in Node or a browser.
export const SCENE_DEFAULTS = Object.freeze({ leadTicks: 120, tailTicks: 180,
  maxCandidates: 12, hitWindowTicks: 180, minArrivals: 16, minWarpEnemyHits: 12,
  minWarpEnemyKills: 1, minBattleTeamShips: 24, minBattleProjectiles: 100,
  warpWindowTicks: 1050, maxShots: 24000, maxShotAgeTicks: 3600,
  maxHistoryHits: 24000, maxWaves: 128, maxArrivals: 4096, maxPathPoints: 600,
  battleRadius: 110, maxFrameRows: 24000 });
export const SCENE_LIMITS = Object.freeze({ maxCandidates: 12, maxShots: 24000,
  maxShotAgeTicks: 3600, maxPathPoints: 600, warpWindowTicks: 1050,
  maxWaves: 128, maxArrivals: 4096, maxHistoryHits: 24000, battleRadius: 440, maxFrameRows: 24000 });

const compare = (a, b) => b.score - a.score || a.eventTick - b.eventTick || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
const hostile = e => e.Team >= 0 && e.TargetTeam >= 0 && e.Team !== e.TargetTeam && e.Damage > 0;
const heavy = kind => kind === 4 || kind === 'HeavyCannon4';
const identity = (e, prefix = '') => `${e[`${prefix}Id`]}:${e[`${prefix}Generation`]}`;
const validIdentity = (e, prefix = '') => Number.isInteger(e[`${prefix}Id`]) && e[`${prefix}Id`] >= 0 &&
  Number.isInteger(e[`${prefix}Generation`]) && e[`${prefix}Generation`] >= 0;
const point = e => ({ x: e.X, y: e.Y });
const finitePoint = p => Number.isFinite(p.x) && Number.isFinite(p.y);
const rowPoint = r => r && ({ x: r[3], y: r[4] });
const actualDamage = e => Math.max(0, e.ShieldBefore - e.ShieldAfter) + Math.max(0, e.HullBefore - e.HullAfter);

export class SceneFinder {
  constructor(options = {}) {
    this.options = { ...SCENE_DEFAULTS, ...options };
    const o = this.options;
    if (!Number.isInteger(o.ticks) || o.ticks < 0) throw new Error('ticks must be a nonnegative integer');
    for (const k of Object.keys(SCENE_DEFAULTS)) {
      if (k === 'battleRadius') continue;
      if (!Number.isInteger(o[k]) || o[k] < 0) throw new Error(`Invalid budget/threshold: ${k}`);
    }
    if (!o.maxPathPoints || !Number.isFinite(o.battleRadius) || o.battleRadius <= 0 ||
        Object.entries(SCENE_LIMITS).some(([key, limit]) => o[key] > limit))
      throw new Error('Requested budget exceeds hard limit');
    this.shots = new Map(); this.histories = new Map(); this.waves = new Map();
    this.candidates = []; this.lastTick = -1; this.lastOrder = -1; this.closed = false;
    this.stats = { frames: 0, events: 0, shots: 0, expiredShots: 0, peakShots: 0,
      confirmedKills: 0, candidates: { group: 0, heavy: 0, warp: 0 } };
  }

  budget(size, limit, name) { if (size > limit) throw new Error(`${name} budget overflow (${size} > ${limit})`); }

  episode(type, first, last, score, focus, evidence, cameraPoints) {
    const o = this.options;
    const e = { schema: 1, id: type === 'warp' ? `warp:${first.Tick}:${first.Order}` : `${type}:${first.Tick}:${first.Order}:${last.Order}`,
      type, profile: o.profile, seed: o.seed, startTick: Math.max(0, first.Tick - o.leadTicks),
      endTick: Math.min(o.ticks, last.Tick + o.tailTicks), eventTick: last.Tick,
      eventTicks: [...new Set([first.Tick, ...(evidence.arrivals ?? []).map(a => a.Tick),
        ...(evidence.shots ?? []).map(s => s.Tick),
        ...(evidence.hits ?? []).map(h => h.Tick), last.Tick])].sort((a, b) => a - b),
      score, focus, cameraPoints: cameraPoints.filter(finitePoint).slice(0, o.maxPathPoints), evidence };
    const prior = this.candidates.findIndex(c => c.id === e.id);
    if (prior >= 0) this.candidates.splice(prior, 1); else this.stats.candidates[type]++;
    this.candidates.push(e); this.candidates.sort(compare);
    // Keep a bounded shortlist for each type so a busy type cannot hide missing evidence.
    this.candidates = this.candidates.filter((c, i, all) => all.slice(0, i).filter(x => x.type === c.type).length < o.maxCandidates);
  }

  ingest(frame) {
    if (this.failed) throw this.failed;
    try { return this.ingestFrame(frame); }
    catch (error) { this.failed = error; throw error; }
  }

  ingestFrame(frame) {
    if (this.closed) throw new Error('SceneFinder is finished');
    const o = this.options, tick = frame.tick;
    if (!Number.isInteger(tick) || tick <= this.lastTick || tick > o.ticks) throw new Error('Frames must have increasing ticks within ticks');
    this.lastTick = tick; this.stats.frames++;
    for (const [id, shot] of this.shots) if (shot.expiry < tick) { this.shots.delete(id); this.stats.expiredShots++; }
    for (const [key, history] of this.histories) {
      const recent = history.filter(h => h.Tick >= tick - o.hitWindowTicks);
      if (recent.length) this.histories.set(key, recent); else this.histories.delete(key);
    }
    for (const [key, wave] of this.waves) if (tick > (wave.first?.Tick ?? wave.open.ArrivalTick ?? wave.open.Tick) + o.warpWindowTicks) this.waves.delete(key);
    const events = frame.events ?? [];
    for (const name of ['ships', 'previousShips', 'projectiles', 'tactics'])
      this.budget(frame[name]?.length ?? 0, o.maxFrameRows, `frame ${name}`);
    this.budget(events.length, o.maxHistoryHits, 'frame events');
    for (const e of events) {
      if (e.Tick !== tick || !Number.isInteger(e.Order) || e.Order <= this.lastOrder) throw new Error('Invalid event Tick/Order');
      this.lastOrder = e.Order; this.stats.events++;
    }
    const destroys = new Map();
    for (const e of events) if (e.Type === 'destroy') {
      const prefix = validIdentity(e, 'Target') ? 'Target' : 'Entity';
      if (validIdentity(e, prefix)) destroys.set(`${e.Target ?? e.Serial ?? e.Entity}:${identity(e, prefix)}`, e);
    }
    const tactics = new Map((frame.tactics ?? []).map(t => [t.Serial, t]));
    const previousShips = new Map((frame.previousShips ?? []).map(r => [r[0], r]));
    for (const [key, history] of this.histories) {
      const h = history[0], t = tactics.get(h.Owner);
      const roster = t ? [...tactics.values()].filter(v => v.Team === t.Team && v.Leader === t.Leader && v.Target === t.Target).map(v => v.Serial).sort((a, b) => a - b) : [];
      if (!t || key !== `${h.Team}:${t.Leader}:${h.Target}:${identity(h, 'Target')}:${roster.join(',')}`) this.histories.delete(key);
    }
    for (const raw of events) {
      let e = raw;
      const shot = e.Type === 'hit' ? this.shots.get(e.Projectile) : null;
      const birthKeyMatches = shot && shot.expiry >= tick && shot.event.Tick <= tick &&
        (shot.event.Kind === e.Kind || heavy(shot.event.Kind) && heavy(e.Kind)) &&
        validIdentity(e, 'Owner') && identity(shot.event, 'Owner') === identity(e, 'Owner');
      const deadOwner = e.Owner === -1 && e.Team === -1 && e.OwnerClass === -1;
      const linked = birthKeyMatches && (deadOwner || shot.event.Owner === e.Owner && shot.event.Team === e.Team);
      if (deadOwner && linked) e = { ...e, Owner: shot.event.Owner, Team: shot.event.Team,
        OwnerClass: shot.event.OwnerClass, attribution: { source: 'shot-birth', ownerKey: identity(e, 'Owner'),
          loggedOwner: raw.Owner, loggedTeam: raw.Team, loggedOwnerClass: raw.OwnerClass } };
      if (e.Type === 'hit' && shot && !linked) continue;
      if (e.Type === 'shot') {
        if (!validIdentity(e, 'Owner')) continue;
        if (this.shots.has(e.Projectile)) throw new Error('Duplicate live projectile serial');
        const expiry = Math.min(Number.isInteger(e.ExpiresTick) ? e.ExpiresTick : tick + o.maxShotAgeTicks, tick + o.maxShotAgeTicks);
        this.shots.set(e.Projectile, { event: { ...e }, expiry,
          ownerPosition: rowPoint(previousShips.get(e.Owner)), path: [point(e)].filter(finitePoint) });
        this.stats.shots++; this.budget(this.shots.size, o.maxShots, 'shots');
        this.stats.peakShots = Math.max(this.stats.peakShots, this.shots.size);
      } else if (e.Type === 'warp-open') {
        const key = `${e.Team}:${e.Wave}`;
        if (this.waves.has(key)) throw new Error('Duplicate warp-open');
        this.waves.set(key, { open: { ...e }, arrivals: new Map(), hits: [], kills: 0, reverseHits: 0, peak: null });
        this.budget(this.waves.size, o.maxWaves, 'waves');
      } else if (e.Type === 'warp-arrival') {
        const w = this.waves.get(`${e.Team}:${e.Wave}`);
        const prefix = validIdentity(e, 'Target') ? 'Target' : 'Entity';
        if (!w || e.Tick < w.open.Tick || e.Tick < w.open.ArrivalTick || e.ArrivalTick !== w.open.ArrivalTick || e.Corner !== w.open.Corner ||
            e.GateX !== w.open.GateX || e.GateY !== w.open.GateY || !validIdentity(e, prefix)) continue;
        const serial = prefix === 'Target' ? e.Target : e.Entity ?? e.Serial ?? e.Owner;
        if (!Number.isInteger(serial)) continue;
        w.first ??= { ...e }; w.arrivals.set(serial, { ...e, arrivalKey: identity(e, prefix) });
        let arrivals = 0;
        for (const wave of this.waves.values()) arrivals += wave.arrivals.size;
        this.budget(arrivals, o.maxArrivals, 'arrivals');
      } else if (e.Type === 'hit' && hostile(e) && validIdentity(e, 'Target')) {
        const destroy = destroys.get(`${e.Target}:${identity(e, 'Target')}`);
        const lethal = e.HullBefore > 0 && e.HullAfter === 0 && !!destroy;
        if (lethal) this.stats.confirmedKills++;
        if (linked && shot.event.Tick < e.Tick && heavy(shot.event.Kind) && heavy(e.Kind) && lethal) {
          const anchors = [rowPoint(previousShips.get(e.Owner)) ?? shot.ownerPosition,
            rowPoint(previousShips.get(e.Target)), point(shot.event), point(e)].filter(p => p && finitePoint(p));
          const room = Math.max(0, o.maxPathPoints - anchors.length);
          const path = room ? shot.path.filter((_, i) => i % Math.max(1, Math.ceil(shot.path.length / room)) === 0).slice(0, room) : [];
          this.episode('heavy', shot.event, e, 100 + Math.min(100, e.Damage),
            { ships: [e.Owner], targets: [e.Target], projectile: e.Projectile, gate: null },
            { shot: shot.event, hit: { ...raw }, attribution: e.attribution ?? { source: 'live-owner' },
              destroy: { ...destroy }, intendedTarget: shot.event.Target, actualTarget: e.Target },
            [...anchors, ...path]);
        }
        const tactic = tactics.get(e.Owner);
        if (actualDamage(e) > 0 && linked && shot.event.Target === e.Target && validIdentity(shot.event, 'Target') &&
            identity(shot.event, 'Target') === identity(e, 'Target') &&
            tactic && tactic.Team === e.Team && tactic.Target === e.Target && tactic.Leader >= 0 &&
            tactic.Leader === shot.event.Leader) {
          const roster = [...tactics.values()].filter(t => t.Team === e.Team && t.Leader === tactic.Leader && t.Target === e.Target).map(t => t.Serial).sort((a, b) => a - b);
          if (roster.length >= 2 && roster.length <= 4 && roster.every(s => tactics.get(s).Members === roster.length)) {
            const key = `${e.Team}:${tactic.Leader}:${e.Target}:${identity(e, 'Target')}:${roster.join(',')}`;
            const history = this.histories.get(key) ?? [];
            history.push({ ...e, shotBirth: { ...shot.event } }); this.histories.set(key, history);
            const contributors = [...new Set(history.filter(h => roster.includes(h.Owner)).map(h => h.Owner))].sort((a, b) => a - b);
            if (lethal && contributors.length >= 2) this.episode('group', history[0], e,
              100 + contributors.length * 20 + Math.min(history.length, 100),
              { ships: contributors, targets: [e.Target], projectile: null, gate: null },
              { leader: tactic.Leader, roster, contributors,
                shots: [...new Map(history.map(h => [h.shotBirth.Projectile, h.shotBirth])).values()].map(s => ({ ...s })),
                hits: history.map(({ shotBirth, ...h }) => h), destroy: { ...destroy } }, history.map(point));
          }
        }
        for (const w of this.waves.values()) {
          if (!w.first || tick > w.first.Tick + o.warpWindowTicks) continue;
          const arrival = w.arrivals.get(e.Owner);
          if (arrival && arrival.arrivalKey === identity(e, 'Owner') && e.Team === w.open.Team) {
            w.hits.push({ ...e }); if (lethal) w.kills++;
            this.budget(w.hits.length, o.maxHistoryHits, 'warp hits');
          }
          if (e.TargetTeam === w.open.Team && e.Team !== w.open.Team) { w.reverseHits++; w.reverseTick = tick; }
        }
      }
    }
    let totalHits = 0;
    for (const h of this.histories.values()) totalHits += h.length;
    for (const w of this.waves.values()) totalHits += w.hits.length;
    this.budget(totalHits, o.maxHistoryHits, 'group history');
    for (const row of frame.projectiles ?? []) {
      const s = this.shots.get(row[0]);
      if (s && heavy(s.event.Kind) && finitePoint({ x: row[3], y: row[4] })) {
        s.path.push({ x: row[3], y: row[4] });
        if (s.path.length > o.maxPathPoints) s.path.splice(1, 1);
      }
    }
    for (const w of this.waves.values()) {
      if (!w.first || !w.hits.length || tick > w.first.Tick + o.warpWindowTicks) continue;
      const center = point(w.hits.at(-1)), radius = o.battleRadius;
      if (!finitePoint(center)) continue;
      const nearby = r => (r[3] - center.x) ** 2 + (r[4] - center.y) ** 2 <= radius ** 2;
      const counts = new Map();
      for (const r of frame.ships ?? []) if (r[1] >= 0 && nearby(r)) counts.set(r[1], (counts.get(r[1]) ?? 0) + 1);
      let projectiles = 0;
      for (const r of frame.projectiles ?? []) if (nearby(r)) projectiles++;
      const battle = (counts.get(w.open.Team) ?? 0) >= o.minBattleTeamShips &&
        [...counts].some(([team, count]) => team !== w.open.Team && count >= o.minBattleTeamShips) &&
        projectiles >= o.minBattleProjectiles;
      if (battle && (!w.peak || projectiles > w.peak.projectiles))
        w.peak = { tick, teams: Object.fromEntries(counts), projectiles, center, radius };
      if (w.arrivals.size < o.minArrivals || w.hits.length < o.minWarpEnemyHits || w.kills < o.minWarpEnemyKills || !w.reverseHits || !w.peak) continue;
      const last = { ...w.hits.at(-1), Tick: Math.max(w.hits.at(-1).Tick, w.peak.tick, w.reverseTick) };
      this.episode('warp', w.open, last, 100 + Math.min(w.arrivals.size, 100) + Math.min(w.hits.length, 100) + Math.min(w.kills, 20) * 5,
        { ships: [...w.arrivals.keys()].sort((a, b) => a - b), targets: [...new Set(w.hits.map(h => h.Target))].sort((a, b) => a - b), projectile: null, gate: { x: w.open.GateX, y: w.open.GateY } },
        { open: w.open, arrivals: [...w.arrivals.values()], hits: w.hits.map(h => ({ ...h })), enemyKills: w.kills, reverseHits: w.reverseHits, battle: w.peak },
        [{ x: w.open.GateX, y: w.open.GateY }, ...w.hits.map(point)]);
    }
    return this;
  }

  finish() {
    if (this.failed) throw this.failed;
    this.closed = true;
    const episodes = this.candidates.slice().sort(compare).map(e => ({ ...e,
      endTick: Math.min(e.endTick, Math.max(0, this.lastTick)),
      evidence: { ...e.evidence, truncated: e.eventTick + this.options.tailTicks > Math.min(this.options.ticks, this.lastTick) } }));
    return { episodes, stats: { ...this.stats, retainedShots: this.shots.size, lastTick: this.lastTick },
      missingTypes: ['group', 'heavy', 'warp'].filter(type => !episodes.some(e => e.type === type)) };
  }
}

export default SceneFinder;
