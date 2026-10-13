import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { projectRoot, projectPath, inside, parseOptions } from './common.mjs';
import { searchSettings, evidenceEvents } from './find-scenes.mjs';
import { insertOnce, instrumentSources } from './prepare-scenes.mjs';

test('PowerShell preparation uses the platform PATH separator', async () => {
  const script = await fs.readFile(path.join(projectRoot, 'tools/space-battle/prepare-scenes.ps1'), 'utf8');
  const assignment = script.split(/\r?\n/).find(line => /^\s*\$env:PATH\s*=/.test(line));
  assert.ok(assignment?.includes('[IO.Path]::PathSeparator'), 'PATH must work with both Windows ; and Unix :');
});

test('paths cannot escape project or write outside ignored outputs', async () => {
  assert.ok(inside(projectRoot, path.join(projectRoot, 'tools')));
  assert.equal(inside(projectRoot, path.resolve(projectRoot, '../ecs2')), false);
  await assert.rejects(projectPath('../outside', { output: true }));
  await assert.rejects(projectPath('docs/new-scenes', { output: true, ignored: true }));
  assert.ok((await projectPath('Benchmarks.Local/tmp/new-scenes', { output: true, ignored: true })).endsWith('new-scenes'));
});
test('an ignored-looking junction cannot redirect generated files into project docs', async () => {
  const root = path.join(projectRoot, 'Benchmarks.Local/tmp');
  await fs.mkdir(root, { recursive: true });
  const temp = await fs.mkdtemp(path.join(root, 'scene-path-test-')), link = path.join(temp, 'alias');
  try {
    await fs.symlink(path.join(projectRoot, 'docs'), link, process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(projectPath(path.join(link, 'must-not-be-created'), { output: true, ignored: true }), /ignored output/);
  } finally { await fs.unlink(link).catch(() => {}); await fs.rmdir(temp); }
});
test('options and search budgets fail closed', () => {
  assert.throws(() => parseOptions(['--output'], ['output']));
  assert.throws(() => parseOptions(['--foo', 'x'], ['output']));
  assert.throws(() => parseOptions(['--output', 'x', '--output', 'y'], ['output']));
  for (const options of [{ profiles: 'all' }, { seeds: '0,0' }, { seeds: '-1' }, { ticks: '18001' },
    { profiles: 'low,nominal,high', seeds: '1,2,3,4,5' }, { 'max-candidates': '13' }, { seeds: '4294967296' }])
    assert.throws(() => searchSettings(options));
  assert.deepEqual(searchSettings({}).profiles, ['nominal', 'high']);
});
test('instrumentation refuses missing/ambiguous source anchors', () => {
  assert.equal(insertOnce('abc', 'b', 'B', 'test'), 'aBc');
  assert.throws(() => insertOnce('abc', 'z', 'Z', 'test'));
  assert.throws(() => insertOnce('abcb', 'b', 'B', 'test'));
});
test('probe edits are isolated reads at known phases and original source map stays unchanged', async () => {
  const sources = new Map();
  for (const file of ['SpaceBattle.Core/Battle.cs', 'SpaceBattle.Core/CombatSystems.cs',
    'SpaceBattle.Core/WarpReinforcements.cs', 'SpaceBattle.Web/BattleBridge.cs'])
    sources.set(file, await fs.readFile(path.join(projectRoot, file), 'utf8'));
  const original = new Map(sources);
  const template = await fs.readFile(path.join(projectRoot, 'tools/space-battle/scenes/probe/SceneEvents.cs.txt'), 'utf8');
  assert.throws(() => instrumentSources(new Map([...sources, ['SpaceBattle.Core/SceneEvents.cs', 'existing user code']]), template), /collides/);
  const probe = instrumentSources(sources, template);
  assert.deepEqual(sources, original);
  assert.equal(probe.size, sources.size + 1);
  assert.match(probe.get('SpaceBattle.Core/CombatSystems.cs'), /SceneHit\(row.Entity/);
  assert.match(probe.get('SpaceBattle.Core/WarpReinforcements.cs'), /SceneWarpArrival\(ship/);
  assert.match(probe.get('SpaceBattle.Core/Battle.cs'), /SceneCaptureTactics\(\); Mark\(1\)/);
});
test('causal evidence includes births, hits, removals and exact warp arrivals', () => {
  const shot = { Type: 'shot' }, hit = { Type: 'hit' }, destroy = { Type: 'destroy' }, open = { Type: 'warp-open' }, arrival = { Type: 'warp-arrival' };
  assert.deepEqual(evidenceEvents({ evidence: { shot, hit, destroy } }), [shot, hit, destroy]);
  assert.deepEqual(evidenceEvents({ evidence: { open, arrivals: [arrival], hits: [hit] } }), [open, arrival, hit]);
});
