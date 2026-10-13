import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseOptions, projectPath, freshOutputDirectory, readJson, writeJson, hashSite, openSite,
  resetBattle, advanceTo, checkpoint, sha256 } from './common.mjs';

export function searchSettings(options) {
  const profiles = (options.profiles || 'nominal,high').split(','), seeds = (options.seeds || '7331').split(',').map(Number);
  const ticks = Number(options.ticks || 4200), maxCandidates = Number(options['max-candidates'] || 3);
  assert.ok(profiles.length && new Set(profiles).size === profiles.length && profiles.every(p => ['low', 'nominal', 'high'].includes(p)), 'Invalid or duplicate profiles');
  assert.ok(seeds.length <= 8 && seeds.length && new Set(seeds).size === seeds.length && seeds.every(s => Number.isInteger(s) && s >= 0 && s <= 0xFFFFFFFF), 'Use at most eight distinct uint32 seeds');
  assert.ok(Number.isInteger(ticks) && ticks >= 1 && ticks <= 18000, 'ticks must be 1..18000');
  assert.ok(Number.isInteger(maxCandidates) && maxCandidates >= 1 && maxCandidates <= 12, 'max-candidates must be 1..12 per type');
  assert.ok(profiles.length * seeds.length <= 12 && profiles.length * seeds.length * ticks <= 216000, 'Search budget: at most 12 runs / 216000 ticks');
  return { profiles, seeds, ticks, maxCandidates };
}

const eventFields = ['Type', 'Tick', 'Order', 'Projectile', 'EntityId', 'EntityGeneration', 'OwnerId', 'OwnerGeneration',
  'TargetId', 'TargetGeneration', 'Kind', 'X', 'Y', 'Damage', 'ShieldBefore', 'ShieldAfter', 'HullBefore', 'HullAfter', 'Wave', 'Corner', 'ArrivalTick', 'GateX', 'GateY'];
const eventKey = e => JSON.stringify(eventFields.map(field => e[field]));
export function evidenceEvents(episode) {
  const e = episode.evidence;
  return [e.shot, e.hit, e.destroy, e.open, ...(e.shots || []), ...(e.arrivals || []), ...(e.hits || [])].filter(Boolean);
}

export async function search(args = process.argv.slice(2)) {
  const options = parseOptions(args, ['prepared', 'output', 'profiles', 'seeds', 'ticks', 'max-candidates']);
  const settings = searchSettings(options), prepared = await readJson(await projectPath(options.prepared, { ignored: true }));
  assert.equal(prepared.schema, 1);
  for (const build of Object.values(prepared.builds)) assert.equal((await hashSite(build.path)).fingerprint, build.fingerprint, 'Prepared AOT site changed');
  const output = await freshOutputDirectory(options.output), sessions = [], runs = [], episodes = [];
  const started = Date.now();
  let cancelled = false;
  const abort = () => { cancelled = true; for (const session of sessions) session.close().catch(() => {}); };
  process.on('SIGINT', abort); process.on('SIGTERM', abort);
  try {
    const diagnostic = await openSite(prepared.builds.probe.path); sessions.push(diagnostic);
    const modulePath = fileURLToPath(new URL('./scene-candidates.mjs', import.meta.url));
    const moduleBytes = await fs.readFile(modulePath);
    await diagnostic.page.evaluate(async source => {
      window.__scenesFinder = (await import('data:text/javascript;base64,' + source)).SceneFinder;
    }, moduleBytes.toString('base64'));
    console.log(`Declared search: ${settings.profiles.join(',')} / seeds ${settings.seeds.join(',')} / ${settings.ticks} ticks per run`);
    for (const profile of settings.profiles) for (const seed of settings.seeds) {
      await resetBattle(diagnostic.page, profile, seed);
      const run = await diagnostic.page.evaluate(async config => {
        const game = window.spaceBattle, bridge = window.__scenesBridge;
        if (typeof bridge.SceneCapture !== 'function') throw new Error('Prepared diagnostic export missing');
        const initialPacket = JSON.parse(bridge.SceneCapture()); game.refresh();
        const finder = new window.__scenesFinder(config), checkpoints = [];
        const check = () => {
          const s = JSON.parse(window.render_game_to_text());
          checkpoints.push({ tick: s.tick, checksum: s.checksum, counts: s.counts, counters: s.counters, teams: game.aliveTeamCounts() });
        };
        check();
        let previousShips = game.snapshot.ships.map(r => Array.from(r));
        for (let tick = 1; tick <= config.ticks; tick++) {
          bridge.Advance(1);
          const packet = JSON.parse(bridge.SceneCapture());
          // JSON marshalling can grow WASM memory. Rebind before borrowing/copying any row.
          game.refresh();
          const ships = game.snapshot.ships.map(r => Array.from(r)), projectiles = game.snapshot.projectiles.map(r => Array.from(r));
          if (packet.Schema !== 1 || packet.Tick !== tick || game.snapshot.tick !== tick) throw new Error('Diagnostic contract/tick mismatch');
          finder.ingest({ tick, events: packet.Events, tactics: packet.Tactics, ships, previousShips, projectiles,
            counters: { ...game.snapshot.counters }, teams: game.aliveTeamCounts() });
          previousShips = ships;
          if (tick % 120 === 0 || tick === config.ticks) check();
          if (tick % 60 === 0) await new Promise(resolve => setTimeout(resolve, 0));
        }
        bridge.Validate(); game.refresh();
        return { ...config, resolvedConfig: initialPacket.Config, checkpoints, ...finder.finish() };
      }, { profile, seed, ticks: settings.ticks, maxCandidates: settings.maxCandidates });
      for (const episode of run.episodes) episode.id = `${profile}:${seed}:${episode.id}`;
      runs.push(run);
      episodes.push(...run.episodes);
      await writeJson(path.join(output, `run-${profile}-${seed}.json`), run);
      console.log(`${profile}/${seed}: ${run.episodes.map(e => e.type + '@' + e.eventTick).join(', ') || 'no qualifying episodes'}`);
    }
    assert.deepEqual(diagnostic.errors, []);
    const production = await openSite(prepared.builds.production.path); sessions.push(production);
    assert.equal(production.browser.version(), diagnostic.browser.version(), 'Same Chromium version for replay');
    for (const run of runs) {
      assert.equal(cancelled, false, 'Search interrupted');
      const selected = episodes.filter(e => e.profile === run.profile && e.seed === run.seed);
      const expectedEvents = [...new Map(selected.flatMap(evidenceEvents).map(e => [eventKey(e), e])).values()];
      const ticks = [...new Set([...run.checkpoints.map(c => c.tick), ...expectedEvents.map(e => e.Tick),
        ...selected.flatMap(e => [e.startTick, e.endTick, e.eventTick, ...e.eventTicks,
          ...(e.evidence.battle ? [e.evidence.battle.tick] : [])])])].sort((a, b) => a - b);
      const proof = [], exact = [];
      await resetBattle(diagnostic.page, run.profile, run.seed);
      for (const tick of ticks) {
        await advanceTo(diagnostic.page, tick);
        if (expectedEvents.some(e => e.Tick === tick)) {
          const events = await diagnostic.page.evaluate(() => {
            const packet = JSON.parse(window.__scenesBridge.SceneCapture()); window.spaceBattle.refresh(); return packet.Events;
          });
          const actual = new Set(events.map(eventKey));
          for (const expected of expectedEvents.filter(e => e.Tick === tick)) {
            assert.ok(actual.has(eventKey(expected)), `Exact ${expected.Type} evidence did not replay at tick ${tick}`);
            exact.push(eventKey(expected));
          }
        }
        const point = await checkpoint(diagnostic.page), original = run.checkpoints.find(c => c.tick === tick);
        if (original) assert.deepEqual(point, original, 'Diagnostic scout replay changed');
        proof.push(point);
      }
      await resetBattle(production.page, run.profile, run.seed);
      for (const expected of proof) {
        await advanceTo(production.page, expected.tick);
        assert.deepEqual(await checkpoint(production.page), expected, `Production/probe replay mismatch at ${expected.tick}`);
      }
      run.checkpoints = proof; run.exactEventsVerified = exact.length;
      console.log(`PASS ${run.profile}/${run.seed}: ${proof.length} production checkpoints, ${exact.length} exact diagnostic events`);
    }
    for (const session of sessions) {
      assert.deepEqual(session.errors, []);
      assert.equal((await hashSite(session.identity.path)).fingerprint, session.identity.fingerprint, 'Site bytes changed');
    }
    const browserVersion = production.browser.version();
    assert.equal(cancelled, false, 'Search interrupted');
    const missingTypes = ['group', 'heavy', 'warp'].filter(type => !episodes.some(e => e.type === type));
    const result = { schema: 1, verified: true, browserVersion, builds: prepared.builds, source: prepared.source,
      finderSha256: sha256(moduleBytes), settings, runs, episodes, missingTypes, errors: [], elapsedSeconds: (Date.now() - started) / 1000,
      limits: { searchTicks: settings.profiles.length * settings.seeds.length * settings.ticks, maximumSearchTicks: 216000,
        verificationTicks: runs.reduce((sum, r) => sum + r.ticks * 2, 0), independentReplays: runs.length * 2,
        note: 'Diagnostic timings are not throughput benchmarks; checksums are same-environment evidence, not complete snapshots.' } };
    await writeJson(path.join(output, 'search.json'), result);
    console.log(`Verified search: ${path.join(output, 'search.json')}. Missing types: ${missingTypes.join(', ') || 'none'}`);
    return result;
  } catch (error) {
    await writeJson(path.join(output, 'failure.json'), { error: String(error), stack: error.stack, runs,
      errors: sessions.flatMap(s => s.errors) });
    throw error;
  } finally {
    for (const session of sessions.reverse()) await session.close();
    process.off('SIGINT', abort); process.off('SIGTERM', abort);
  }
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  search().catch(error => { console.error(error); process.exitCode = 1; });
