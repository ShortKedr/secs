import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { projectRoot, projectPath, filesBelow, sha256, readJson, writeJson, hashSite, parseOptions, freshOutputDirectory } from './common.mjs';

export function insertOnce(text, anchor, replacement, name) {
  assert.equal(text.split(anchor).length - 1, 1, `Scene probe anchor changed or is ambiguous: ${name}. Review instrumentation before preparing a new build.`);
  return text.replace(anchor, replacement);
}

export function instrumentSources(sources, template) {
  assert.ok(!sources.has('SpaceBattle.Core/SceneEvents.cs'), 'Diagnostic file name collides with an existing Core source; review the probe before continuing');
  const output = new Map(sources);
  const patch = (file, anchor, replacement, name) => output.set(file,
    insertOnce(output.get(file).replaceAll('\r\n', '\n'), anchor, replacement, name));
  patch('SpaceBattle.Core/Battle.cs', '        Tick++;', '        SceneBeginTick();\n        Tick++;', 'begin tick');
  patch('SpaceBattle.Core/Battle.cs', 'Store.ForEach<Identity, Brain, Transform, AiSystem>(ref ai); Mark(1);',
    'Store.ForEach<Identity, Brain, Transform, AiSystem>(ref ai); SceneCaptureTactics(); Mark(1);', 'post-AI roster');
  patch('SpaceBattle.Core/Battle.cs', '        SpawnTurrets(e, id, transform);\n        return e;',
    '        SpawnTurrets(e, id, transform);\n        SceneSpawn(e);\n        return e;', 'ship birth');
  patch('SpaceBattle.Core/CombatSystems.cs', '                projectiles.Add(new(e, number)); Counters.Shots++; Counters.StructuralAdds += 3;',
    '                projectiles.Add(new(e, number)); Counters.Shots++; Counters.StructuralAdds += 3;\n                SceneShot(e, number, shot, position);', 'projectile birth');
  patch('SpaceBattle.Core/CombatSystems.cs', '            DamageWeapon(target, p.Damage, p.Kind, p.Owner); Counters.Hits++;',
    '            var sceneShield = Store.Get<Shield>(target).Current; var sceneHull = Store.Get<Hull>(target).Current;\n' +
    '            DamageWeapon(target, p.Damage, p.Kind, p.Owner); Counters.Hits++;\n' +
    '            SceneHit(row.Entity, p, target, impact, sceneShield, sceneHull);', 'resolved CCD hit');
  patch('SpaceBattle.Core/CombatSystems.cs', '                    Store.Destroy(e); Counters.ShipsDestroyed++; Counters.Explosions++;',
    '                    SceneDestroyed(e);\n                    Store.Destroy(e); Counters.ShipsDestroyed++; Counters.Explosions++;', 'actual destruction');
  patch('SpaceBattle.Core/WarpReinforcements.cs', '            wave.NextShip++; teamCounts[team]++; Counters.ReinforcementShips++;',
    '            wave.NextShip++; teamCounts[team]++; Counters.ReinforcementShips++;\n            SceneWarpArrival(ship, team, wave);', 'exact warp arrival');
  patch('SpaceBattle.Core/WarpReinforcements.cs', '        Counters.ReinforcementWaves++;',
    '        Counters.ReinforcementWaves++;\n        SceneWarpOpen(team, wave);', 'warp opening');
  patch('SpaceBattle.Web/BattleBridge.cs', '    public static string Checksum() => Current.Checksum().ToString("X16");',
    '    public static string Checksum() => Current.Checksum().ToString("X16");\n\n' +
    '    [JSExport]\n    public static string SceneCapture() => System.Text.Json.JsonSerializer.Serialize(Current.SceneCapture(), SceneJsonContext.Default.ScenePacket);', 'diagnostic export');
  output.set('SpaceBattle.Core/SceneEvents.cs', template);
  return output;
}

export async function sourceIdentity() {
  const paths = [];
  for (const dir of ['SKECS', 'SpaceBattle.Core', 'SpaceBattle.SKECS', 'SpaceBattle.Web', 'SpaceBattle.Game/Assets'])
    paths.push(...await filesBelow(path.join(projectRoot, dir)));
  const files = {};
  for (const file of paths.sort()) files[path.relative(projectRoot, file).split(path.sep).join('/')] = sha256(await fs.readFile(file));
  for (const name of ['tools/space-battle/scenes/prepare-scenes.mjs', 'tools/space-battle/scenes/probe/SceneEvents.cs.txt',
    'tools/space-battle/prepare-scenes.ps1', 'tools/web/prepare-web-atlases.mjs', 'tools/web/finalize-web-site.mjs'])
    files[name] = sha256(await fs.readFile(path.join(projectRoot, name)));
  return { fingerprint: sha256(JSON.stringify(files)), files };
}

export async function initialize(workValue) {
  const work = await projectPath(workValue, { output: true, ignored: true }), current = await sourceIdentity();
  const ready = path.join(work, 'prepared.json');
  if (await fs.stat(ready).then(() => true, () => false)) {
    const existing = await readJson(ready);
    assert.equal(existing.source.fingerprint, current.fingerprint, 'Sources/tooling changed: use a new -WorkDirectory; old builds are immutable');
    for (const build of Object.values(existing.builds)) assert.equal((await hashSite(build.path)).fingerprint, build.fingerprint, 'Prepared site changed');
    return { reused: true, work, prepared: ready };
  }
  await freshOutputDirectory(work);
  const roots = { production: path.join(work, 'source-production'), probe: path.join(work, 'source-probe') };
  const sources = new Map();
  for (const [name, expected] of Object.entries(current.files)) {
    const bytes = await fs.readFile(path.join(projectRoot, name));
    assert.equal(sha256(bytes), expected, 'Source changed while freezing: ' + name);
    if (name.startsWith('tools/')) continue;
    sources.set(name, bytes);
    for (const root of Object.values(roots)) {
      const target = path.join(root, name); await fs.mkdir(path.dirname(target), { recursive: true }); await fs.writeFile(target, bytes, { flag: 'wx' });
    }
  }
  const texts = new Map([...sources].filter(([name]) => name.endsWith('.cs')).map(([name, bytes]) => [name, bytes.toString('utf8')]));
  const template = await fs.readFile(path.join(projectRoot, 'tools/space-battle/scenes/probe/SceneEvents.cs.txt'), 'utf8');
  const patches = instrumentSources(texts, template), changes = [];
  for (const [name, text] of patches) if (!texts.has(name) || text !== texts.get(name)) {
    await fs.writeFile(path.join(roots.probe, name), text);
    changes.push({ path: name, original: current.files[name] || null, probe: sha256(text) });
  }
  await writeJson(path.join(work, 'source.json'), { schema: 1, source: current, roots, changes });
  return { reused: false, work, roots };
}

async function main() {
  const options = parseOptions(process.argv.slice(2), ['work', 'phase', 'sdk']);
  const work = await projectPath(options.work, { output: true, ignored: true });
  if (options.phase === 'init') console.log(JSON.stringify(await initialize(work)));
  else if (options.phase === 'finish') {
    const frozen = await readJson(path.join(work, 'source.json'));
    assert.equal((await sourceIdentity()).fingerprint, frozen.source.fingerprint, 'Sources changed during preparation: reject mixed build');
    const builds = {};
    for (const name of ['production', 'probe']) builds[name] = await hashSite(path.join(work, name + '-site/wwwroot'));
    await writeJson(path.join(work, 'prepared.json'), { schema: 1, source: frozen.source, builds, sdk: options.sdk, preparedAt: new Date().toISOString() });
    console.log(JSON.stringify({ prepared: path.join(work, 'prepared.json'), builds: Object.fromEntries(Object.entries(builds).map(([k, v]) => [k, v.fingerprint])) }));
  } else throw new Error('--phase must be init or finish');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  main().catch(error => { console.error(error); process.exitCode = 1; });
