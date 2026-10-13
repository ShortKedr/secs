import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { parseSettings, selectEpisode, validateEpisode, validateRemoval, representativeKeyframes, frameMap, createCamera, planGroupCamera } from './scene-camera.mjs';

const children = new Set();
let interrupted = null;
const signal = name => { interrupted = new Error(`Interrupted by ${name}`); for (const child of children) child.kill(); };
const checkSignal = () => { if (interrupted) throw interrupted; };

function processTask(command, args, { input = false, timeout = 120000 } = {}) {
  const child = spawn(command, args, { windowsHide: true, stdio: [input ? 'pipe' : 'ignore', input ? 'ignore' : 'pipe', 'pipe'] });
  children.add(child);
  let stderr = '', stdout = '', failure = null;
  child.stderr.on('data', chunk => { stderr = (stderr + chunk.toString()).slice(-65536); });
  child.stdout?.on('data', chunk => { stdout += chunk.toString(); if (stdout.length > 1048576) { failure = new Error('Process output exceeds limit'); child.kill(); } });
  child.on('error', error => { failure = error; });
  if (input) child.stdin.on('error', error => { failure = error; });
  const timer = setTimeout(() => { failure = new Error(`${command} timed out`); child.kill(); }, timeout);
  const done = new Promise((resolve, reject) => child.on('close', code => {
    clearTimeout(timer); children.delete(child);
    if (failure || code !== 0) reject(failure || new Error(`${command} exited ${code}: ${stderr}`));
    else resolve({ stdout, stderr });
  }));
  // Observe rejection immediately while frames are still being prepared.
  done.catch(() => {});
  return { child, done, write: bytes => new Promise((resolve, reject) => {
    if (failure || child.exitCode !== null || child.stdin.destroyed) return reject(failure || new Error('Encoder stopped'));
    child.stdin.write(bytes, error => error ? reject(error) : resolve());
  }) };
}

export function validateMedia(probe, settings, frames) {
  const video = probe.streams?.filter(s => s.codec_type === 'video') || [];
  const audio = probe.streams?.filter(s => s.codec_type === 'audio') || [];
  assert.equal(video.length, 1, 'Expected one video stream'); assert.equal(audio.length, 1, 'Expected one audio stream');
  const v = video[0], a = audio[0];
  assert.equal(v.codec_name, 'h264'); assert.equal(v.pix_fmt, 'yuv420p');
  assert.equal(v.width, settings.width); assert.equal(v.height, settings.height);
  for (const field of ['r_frame_rate', 'avg_frame_rate']) {
    const [n, d] = String(v[field]).split('/').map(Number); assert.equal(n / d, 60, `Invalid ${field}`);
  }
  assert.equal(Number(v.nb_read_frames), frames); assert.equal(Number(v.nb_frames), frames);
  assert.equal(a.codec_name, 'aac'); assert.equal(Number(a.sample_rate), 48000); assert.equal(a.channels, 2);
  for (const duration of [v.duration, a.duration, probe.format?.duration]) {
    assert.ok(Number.isFinite(Number(duration)) && Math.abs(Number(duration) - frames / 60) <= 0.1, 'Invalid media duration');
  }
}

export function gameplayRegion(width, height) {
  const x = Math.ceil(width * 0.025), y = Math.max(48, Math.ceil(height * 0.1));
  const bottom = Math.max(8, Math.ceil(height * 0.1));
  return { x, y, width: width - x * 2, height: height - y - bottom };
}

// Checksum export can grow the WASM heap: refresh before reading binary rows.
export function readVisualState() {
  const state = JSON.parse(window.render_game_to_text()), game = window.spaceBattle;
  game.refresh();
  const snapshot = game.snapshot, camera = state.camera;
  if (snapshot.tick !== state.tick) throw new Error('Visual state tick changed during refresh');
  const bounds = { left: Math.ceil(camera.width * 0.025), right: camera.width - Math.ceil(camera.width * 0.025),
    top: Math.max(48, Math.ceil(camera.height * 0.1)), bottom: camera.height - Math.max(8, Math.ceil(camera.height * 0.1)) };
  const counts = { ships: 0, projectiles: 0, particles: 0 };
  for (const kind of Object.keys(counts)) for (const row of snapshot[kind]) {
    if (!Number.isFinite(row[3]) || !Number.isFinite(row[4])) continue;
    if (kind === 'particles' && Number.isFinite(row[15]) && row[15] <= 0) continue;
    const x = camera.width / 2 + (row[3] - camera.x) * camera.scale;
    const y = camera.originY - (row[4] - camera.y) * camera.scale;
    const minimum = kind === 'ships' ? 7 : kind === 'projectiles' ? 3 : 2;
    const radius = Math.max(minimum, Number.isFinite(row[8]) ? Math.abs(row[8] * camera.scale) * (kind === 'ships' ? 2 : 1) : minimum);
    if (x + radius >= bounds.left && x - radius < bounds.right && y + radius >= bounds.top && y - radius < bounds.bottom)
      counts[kind]++;
  }
  state.visibleSceneRows = counts.ships + counts.projectiles + counts.particles;
  state.visibleSceneRowCounts = counts;
  state.foregroundBounds = bounds;
  return state;
}

// Serialized by Playwright; all drawing is confined to the presentation canvas.
export async function renderPresentationFrame({ episode, frame, originY, annotate, key, frameCodec = 'jpeg' }) {
  episode ||= window.__scenesPresentationEpisode;
  if (!['jpeg', 'png'].includes(frameCodec)) throw new Error('Unsupported frame codec');
  const source = document.querySelector('#scene');
  const productionPng = key ? source.toDataURL('image/png').split(',')[1] : null;
  const mime = frameCodec === 'jpeg' ? 'image/jpeg' : 'image/png';
  if (!annotate) return { frameBase64: frameCodec === 'png' && key ? productionPng : source.toDataURL(mime, 0.97).split(',')[1],
    productionPng, keyPng: key ? productionPng : null };
  const output = window.__scenesPresentationCanvas ||= new OffscreenCanvas(source.width, source.height);
  const ctx = output.getContext('2d'), width = output.width, height = output.height;
  ctx.clearRect(0, 0, width, height); ctx.drawImage(source, 0, 0);
  const rows = new Map(window.spaceBattle.snapshot.ships.map(r => [r[0], r]));
  const placed = [];
  ctx.save(); ctx.beginPath(); ctx.rect(0, 48, width, height - 56); ctx.clip();
  function mark(serial, color) {
    const row = rows.get(serial); if (!row) return;
    const p = { x: width / 2 + (row[3] - frame.camera.x) * frame.camera.scale,
      y: originY - (row[4] - frame.camera.y) * frame.camera.scale };
    const radius = Math.max(8, row[8] * frame.camera.scale * 2 + 5), length = Math.min(12, radius * 0.35);
    if (p.x + radius < 0 || p.x - radius > width || p.y + radius < 48 || p.y - radius > height - 8) return;
    ctx.strokeStyle = color; ctx.lineWidth = 1;
    ctx.beginPath();
    for (const sx of [-1, 1]) for (const sy of [-1, 1]) {
      ctx.moveTo(p.x + sx * radius, p.y + sy * (radius - length));
      ctx.lineTo(p.x + sx * radius, p.y + sy * radius);
      ctx.lineTo(p.x + sx * (radius - length), p.y + sy * radius);
    }
    ctx.stroke();
    const label = '#' + serial;
    ctx.font = '12px monospace';
    const labelWidth = ctx.measureText(label).width + 6;
    if (labelWidth > width - 6) return;
    const x = Math.max(3, Math.min(width - labelWidth - 3, p.x + radius + 3));
    for (const y of [p.y - radius - 15, p.y + radius + 2, p.y - 7]) {
      if (y < 50 || y + 16 > height - 10 || placed.some(b => x < b.x + b.width && x + labelWidth > b.x && y < b.y + 16 && y + 16 > b.y)) continue;
      ctx.fillStyle = '#080a0ccc'; ctx.fillRect(x, y, labelWidth, 16);
      ctx.fillStyle = color; ctx.fillText(label, x + 3, y + 12);
      placed.push({ x, y, width: labelWidth }); break;
    }
  }
  if (episode.type !== 'warp') {
    for (const id of new Set(episode.focus.ships)) mark(id, '#9de1e9');
    for (const id of new Set(episode.focus.targets)) mark(id, '#f4cc77');
  }
  ctx.restore();
  ctx.fillStyle = '#080a0cdd'; ctx.fillRect(0, 0, width, 48);
  const warp = episode.type === 'warp' ? ` / WAVE ${episode.evidence.open.Wave} / ARRIVALS ${episode.evidence.arrivals.filter(a => a.Tick <= frame.tick).length}` : '';
  const header = `${episode.type.toUpperCase()} / ${episode.profile} / SEED ${episode.seed} / TICK ${frame.tick}${warp}`;
  ctx.font = '14px monospace';
  const fontSize = Math.min(14, 14 * Math.max(1, width - 24) / Math.max(1, ctx.measureText(header).width));
  ctx.font = `${fontSize}px monospace`; ctx.fillStyle = '#dce9ed'; ctx.fillText(header, 12, 29);
  const progress = (frame.tick - episode.startTick) / Math.max(1, episode.endTick - episode.startTick);
  ctx.fillStyle = '#263337'; ctx.fillRect(0, height - 4, width, 4);
  ctx.fillStyle = '#9de1e9'; ctx.fillRect(0, height - 4, width * progress, 4);
  ctx.fillStyle = '#f4cc77';
  const marks = new Set(episode.eventTicks.map(t => Math.min(width - 1, Math.floor(width * (t - episode.startTick) / Math.max(1, episode.endTick - episode.startTick)))));
  for (const x of marks) ctx.fillRect(x, height - 8, 1, 8);
  async function encode(options) {
    const bytes = new Uint8Array(await (await output.convertToBlob(options)).arrayBuffer());
    let binary = '';
    for (let i = 0; i < bytes.length; i += 32768) binary += String.fromCharCode(...bytes.subarray(i, i + 32768));
    return btoa(binary);
  }
  const frameBase64 = await encode({ type: mime, quality: 0.97 });
  const keyPng = key ? frameCodec === 'png' ? frameBase64 : await encode({ type: 'image/png' }) : null;
  return { frameBase64, productionPng, keyPng };
}

export function presentationEpisode(episode) {
  return { type: episode.type, profile: episode.profile, seed: episode.seed,
    startTick: episode.startTick, endTick: episode.endTick, eventTicks: episode.eventTicks, focus: episode.focus,
    evidence: { open: episode.evidence.open ? { Wave: episode.evidence.open.Wave } : null,
      arrivals: (episode.evidence.arrivals || []).map(a => ({ Tick: a.Tick })) } };
}

export function readCaptureSnapshot() {
  const s = window.spaceBattle.snapshot, e = window.__scenesPresentationEpisode;
  const ids = new Set([...e.focus.ships, ...e.focus.targets]);
  return { tick: s.tick, arenaHalfSize: s.arenaHalfSize,
    ships: e.type === 'warp' ? [] : s.ships.filter(r => ids.has(r[0])).map(r => Array.from(r)),
    projectiles: e.type === 'heavy' ? s.projectiles.filter(r => r[0] === e.focus.projectile).map(r => Array.from(r)) : [] };
}

export function validatePixels(image, settings, state) {
  assert.equal(image.width, settings.width, 'PNG width mismatch');
  assert.equal(image.height, settings.height, 'PNG height mismatch');
  const region = gameplayRegion(image.width, image.height), colors = new Set();
  let coloredNonBackgroundPixels = 0;
  // Exact RGB scan of the inner gameplay canvas, not a page screenshot or HUD.
  for (let y = region.y; y < region.y + region.height; y++) for (let x = region.x; x < region.x + region.width; x++) {
    const at = (y * image.width + x) * 4;
    const r = image.data[at], g = image.data[at + 1], b = image.data[at + 2];
    if (image.data[at + 3] >= 250 && Math.max(Math.abs(r - 8), Math.abs(g - 10), Math.abs(b - 12)) >= 12 &&
      Math.max(r, g, b) - Math.min(r, g, b) >= 10) {
      coloredNonBackgroundPixels++;
      if (colors.size < 16) colors.add((r << 16) | (g << 8) | b);
    }
  }
  const assets = state?.visualAssets;
  const visualAssetsPresent = Boolean(state?.ready && !state.diagnostics?.error && !state.diagnostics?.contextLost &&
    assets?.gunsAtlas === true && assets.effectsAtlas === true && assets.warpAtlas === true &&
    assets.teamTint?.hullTextures >= 2 && assets.teamTint?.gunTextures >= 2 &&
    ['gunKinds', 'projectileKinds', 'particleKinds', 'warpKinds'].every(key => Array.isArray(assets[key]) && assets[key].length));
  const evidence = { region, scannedPixels: region.width * region.height, coloredNonBackgroundPixels,
    colorCount: colors.size, colorCountCappedAt: 16, backgroundRgb: [8, 10, 12],
    thresholds: { coloredPixels: 50, colors: 16, foregroundRows: 1 }, visualAssetsPresent, visualAssets: assets,
    visibleSceneRows: state?.visibleSceneRows, visibleSceneRowCounts: state?.visibleSceneRowCounts, foregroundBounds: state?.foregroundBounds,
    verified: coloredNonBackgroundPixels >= 50 && colors.size >= 16 && visualAssetsPresent &&
      Number.isSafeInteger(state?.visibleSceneRows) && state.visibleSceneRows >= 1 };
  if (!evidence.verified) {
    const error = new Error(`Blank gameplay or missing runtime visual assets: ${coloredNonBackgroundPixels} colored pixels, ${colors.size} colors, foreground=${state?.visibleSceneRows}, assets=${visualAssetsPresent}`);
    error.pixelEvidence = evidence; throw error;
  }
  return evidence;
}

export async function capture(args = process.argv.slice(2)) {
  let output, session;
  const pixelEvidence = [];
  interrupted = null;
  let closing = null;
  const stop = name => {
    signal(name);
    // Closing our own page interrupts in-flight Playwright calls immediately.
    if (session && !closing) { closing = session.close(); closing.catch(() => {}); }
  };
  const onInt = () => stop('SIGINT'), onTerm = () => stop('SIGTERM');
  process.on('SIGINT', onInt); process.on('SIGTERM', onTerm);
  try {
    const settings = parseSettings(args);
    const common = await import('./common.mjs');
    const { readJson, projectPath, freshOutputDirectory, hashSite, openSite, resetBattle, advanceTo, checkpoint, writeJson, projectRoot } = common;
    const require = createRequire(path.join(projectRoot, 'tools/web/package.json'));
    require('playwright');
    const { PNG } = require('pngjs');
    const searchPath = await projectPath(settings.search, { ignored: true });
    const searchBytes = await fs.readFile(searchPath);
    const searchSha256 = createHash('sha256').update(searchBytes).digest('hex');
    const search = await readJson(searchPath);
    assert.deepEqual(search, JSON.parse(searchBytes.toString('utf8').replace(/^\uFEFF/, '')), 'Search changed while reading');
    assert.equal(search.schema, 1); assert.equal(search.verified, true); assert.deepEqual(search.errors, []);
    assert.ok(typeof search.browserVersion === 'string' && search.browserVersion.length, 'Browser version missing');
    assert.ok(Array.isArray(search.episodes) && Array.isArray(search.runs), 'Invalid search');
    const episode = selectEpisode(search.episodes, settings);
    const runs = search.runs.filter(r => r.profile === episode.profile && r.seed === episode.seed);
    assert.equal(runs.length, 1, 'Missing or ambiguous run'); const run = runs[0];
    validateEpisode(episode, run);
    const frames = frameMap(episode, settings.slowmo);
    const keys = new Set(representativeKeyframes(episode));
    const region = gameplayRegion(settings.width, settings.height);
    assert.ok(region.width * region.height * keys.size <= 30000000, 'Keyframe pixel validation exceeds 30M-pixel budget');
    const site = await projectPath(search.builds?.production?.path, { ignored: true });
    const build = await hashSite(site);
    assert.equal(build.fingerprint, search.builds.production.fingerprint, 'Stale production fingerprint');
    assert.deepEqual(build.files, search.builds.production.files, 'Stale production file hashes');
    output = await freshOutputDirectory(settings.output);
    session = await openSite(site, { width: settings.width, height: settings.height + 108 });
    checkSignal();
    assert.equal(session.browser.version(), search.browserVersion, 'Exact Chromium version required');
    const page = session.page;
    page.setDefaultTimeout(30000);
    let size = await page.locator('#scene').evaluate(c => ({ width: c.clientWidth, height: c.clientHeight }));
    await page.setViewportSize({ width: settings.width, height: settings.height + 108 + settings.height - size.height });
    await resetBattle(page, episode.profile, episode.seed);
    size = await page.locator('#scene').evaluate(c => ({ width: c.width, height: c.height }));
    assert.deepEqual(size, { width: settings.width, height: settings.height }, 'Exact production Canvas dimensions required');
    assert.equal(await page.evaluate(() => window.spaceBattle.runtimeMode), 'aot-binary', 'Production AOT required');
    const actualCheckpoints = [];
    await page.evaluate(e => { window.__scenesPresentationEpisode = e; }, presentationEpisode(episode));
    for (const expected of run.checkpoints.filter(cp => cp.tick <= episode.endTick)) {
      checkSignal(); await advanceTo(page, expected.tick);
      const actual = await checkpoint(page);
      for (const field of ['tick', 'checksum', 'counts', 'counters', 'teams']) assert.deepEqual(actual[field], expected[field], `Replay mismatch at ${expected.tick}: ${field}`);
      actualCheckpoints.push(actual);
    }
    let removalEvidence = null;
    if (episode.type === 'heavy' || episode.type === 'group') {
      await resetBattle(page, episode.profile, episode.seed);
      const state = () => page.evaluate(() => ({ tick: window.spaceBattle.snapshot.tick,
        ships: window.spaceBattle.snapshot.ships.map(r => Array.from(r)),
        projectiles: window.spaceBattle.snapshot.projectiles.map(r => Array.from(r)) }));
      checkSignal(); await advanceTo(page, episode.eventTick - 1);
      const before = await state();
      checkSignal(); await advanceTo(page, episode.eventTick);
      removalEvidence = validateRemoval(episode, before, await state());
    }
    let heavySurvey = null;
    if (episode.type === 'heavy') {
      await resetBattle(page, episode.profile, episode.seed);
      const points = [...episode.cameraPoints];
      const ids = [...episode.focus.ships, ...episode.focus.targets];
      for (let tick = episode.startTick; tick <= episode.endTick; tick++) {
        checkSignal(); await advanceTo(page, tick);
        const observed = await page.evaluate(({ ids, projectile }) => {
          const s = window.spaceBattle.snapshot;
          const shipPoints = s.ships.filter(r => ids.includes(r[0])).flatMap(r => {
            const radius = Math.max(24, r[8] * 2);
            return [{ x: r[3] - radius, y: r[4] - radius }, { x: r[3] + radius, y: r[4] + radius }];
          });
          return [...shipPoints, ...s.projectiles.filter(r => r[0] === projectile).map(r => ({ x: r[3], y: r[4] }))];
        }, { ids, projectile: episode.focus.projectile });
        points.push(...observed);
      }
      assert.ok(points.every(p => Number.isFinite(p.x) && Number.isFinite(p.y)), 'Invalid surveyed positions');
      const bounds = points.reduce((b, p) => [{ x: Math.min(b[0].x, p.x), y: Math.min(b[0].y, p.y) },
        { x: Math.max(b[1].x, p.x), y: Math.max(b[1].y, p.y) }],
      [{ x: Infinity, y: Infinity }, { x: -Infinity, y: -Infinity }]);
      heavySurvey = { pointCount: points.length, startTick: episode.startTick, endTick: episode.endTick,
        bounds };
    }
    let groupCameraPlan = null;
    if (episode.type === 'group') {
      await resetBattle(page, episode.profile, episode.seed);
      const survey = [];
      for (let tick = episode.startTick; tick <= episode.endTick; tick++) {
        checkSignal(); await advanceTo(page, tick);
        survey.push(await page.evaluate(readCaptureSnapshot));
      }
      const originY = await page.evaluate(() => JSON.parse(window.render_game_to_text()).camera.originY);
      groupCameraPlan = planGroupCamera(episode, survey, { ...settings, originY });
    }
    // Encoding starts from a separate reset, never from the verification or camera survey state.
    await resetBattle(page, episode.profile, episode.seed);
    await page.evaluate(() => { const game = window.spaceBattle; game.pause(true); game.select(null); game.showColliders(false); });
    let initialBeforeImpact = null;
    if ((episode.type === 'heavy' || episode.type === 'group') && episode.startTick === episode.eventTick) {
      checkSignal(); await advanceTo(page, episode.eventTick - 1);
      initialBeforeImpact = await page.evaluate(() => ({ tick: window.spaceBattle.snapshot.tick,
        ships: window.spaceBattle.snapshot.ships.map(r => Array.from(r)),
        projectiles: window.spaceBattle.snapshot.projectiles.map(r => Array.from(r)) }));
    }
    await advanceTo(page, episode.startTick);
    const originY = await page.evaluate(() => JSON.parse(window.render_game_to_text()).camera.originY);
    await page.evaluate(e => { window.__scenesPresentationEpisode = e; }, presentationEpisode(episode));
    const camera = groupCameraPlan ? snapshot => {
      const frame = groupCameraPlan.frames[snapshot.tick - episode.startTick];
      assert.equal(frame?.tick, snapshot.tick, 'Planned camera tick missing');
      return { x: frame.x, y: frame.y, scale: frame.scale };
    } : createCamera(heavySurvey ? { ...episode, cameraPoints: heavySurvey.bounds } : episode, { ...settings, originY });
    const partial = path.join(output, 'capture.partial.mp4'), final = path.join(output, 'capture.mp4');
    const encoder = processTask(settings.ffmpeg, ['-hide_banner', '-loglevel', 'warning', '-nostdin', '-n',
      '-f', 'image2pipe', '-framerate', '60', '-c:v', settings.frameCodec === 'jpeg' ? 'mjpeg' : 'png', '-i', 'pipe:0',
      '-f', 'lavfi', '-i', 'anullsrc=channel_layout=stereo:sample_rate=48000',
      '-map', '0:v:0', '-map', '1:a:0', '-c:v', 'libx264', '-threads', '4', '-preset', 'fast', '-crf', '18',
      '-vf', 'scale=in_range=full:out_range=limited,format=yuv420p', '-color_range', 'tv',
      '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '64k', '-t', String(frames.length / 60), '-shortest',
      '-movflags', '+faststart', partial], { input: true, timeout: 30 * 60 * 1000 });
    const checkpointByTick = new Map(run.checkpoints.map(cp => [cp.tick, cp]));
    const keyframes = [], captureCheckpoints = []; let frameBytes, renderedTick = -1;
    let beforeImpact = initialBeforeImpact;
    const captureStarted = performance.now();
    for (const frame of frames) {
      checkSignal();
      if (frame.tick !== renderedTick) {
        await advanceTo(page, frame.tick);
        const snapshot = await page.evaluate(readCaptureSnapshot);
        assert.equal(snapshot.tick, frame.tick, 'Exact simulation tick required');
        if (episode.type === 'heavy' || episode.type === 'group') {
          if (frame.tick === episode.eventTick - 1) beforeImpact = snapshot;
          if (frame.tick === episode.eventTick) {
            assert.ok(beforeImpact, 'Pre-impact state missing from capture replay');
            validateRemoval(episode, beforeImpact, snapshot);
          }
        }
        frame.camera = camera(snapshot);
        await page.evaluate(c => {
          const game = window.spaceBattle, state = JSON.parse(window.render_game_to_text()).camera;
          game.camera.x = c.x; game.camera.y = c.y; game.camera.zoom = c.scale / (state.scale / state.zoom); game.refresh();
        }, frame.camera);
        const rendered = await page.evaluate(renderPresentationFrame, { frame, originY, annotate: settings.annotate,
          frameCodec: settings.frameCodec, key: keys.has(frame.tick) });
        frameBytes = Buffer.from(rendered.frameBase64, 'base64');
        const expected = checkpointByTick.get(frame.tick);
        if (keys.has(frame.tick) || expected) {
          const actual = await checkpoint(page);
          if (expected) for (const field of ['tick', 'checksum', 'counts', 'counters', 'teams'])
            assert.deepEqual(actual[field], expected[field], `Capture mismatch at ${frame.tick}: ${field}`);
          captureCheckpoints.push(actual);
        }
        if (keys.has(frame.tick)) {
          const state = await page.evaluate(readVisualState);
          try { pixelEvidence.push({ tick: frame.tick, source: 'unannotated-production-canvas',
            ...validatePixels(PNG.sync.read(Buffer.from(rendered.productionPng, 'base64')), settings, state) }); }
          catch (error) {
            if (error.pixelEvidence) pixelEvidence.push({ tick: frame.tick, source: 'unannotated-production-canvas', ...error.pixelEvidence });
            throw error;
          }
          const keyBytes = Buffer.from(rendered.keyPng, 'base64');
          PNG.sync.read(keyBytes);
          const file = `tick-${frame.tick}.png`; await fs.writeFile(path.join(output, file), keyBytes, { flag: 'wx' });
          keyframes.push({ tick: frame.tick, index: frame.index, pts: frame.pts, file });
        }
        renderedTick = frame.tick;
      } else frame.camera = { ...frames[frame.index - 1].camera };
      await encoder.write(frameBytes);
    }
    encoder.child.stdin.end(); const encoding = await encoder.done;
    checkSignal();
    assert.deepEqual(session.errors, [], 'Browser errors during capture');
    const probing = await processTask(settings.ffprobe, ['-v', 'error', '-count_frames', '-show_streams', '-show_format', '-of', 'json', partial]).done;
    const media = JSON.parse(probing.stdout); validateMedia(media, settings, frames.length);
    const decoding = await processTask(settings.ffmpeg, ['-hide_banner', '-v', 'error', '-xerror', '-err_detect', 'explode', '-i', partial,
      '-map', '0:v:0', '-map', '0:a:0', '-f', 'null', '-'], { timeout: 10 * 60 * 1000 }).done;
    assert.equal(decoding.stderr.trim(), '', 'Full A/V decode errors');
    assert.deepEqual(await hashSite(site), build, 'Production changed during capture'); checkSignal();
    assert.equal(createHash('sha256').update(await fs.readFile(searchPath)).digest('hex'), searchSha256, 'Search changed during capture');
    const groupCameraPolicy = groupCameraPlan && Object.fromEntries(Object.entries(groupCameraPlan).filter(([key]) => key !== 'frames'));
    const manifest = { schema: 1, verified: true, episode, browserVersion: session.browser.version(), identity: session.identity,
      build, settings, annotations: settings.annotate === 1, frameCount: frames.length, duration: frames.length / 60, frameMap: frames, keyframes, pixelEvidence,
      captureEncodingWallSeconds: (performance.now() - captureStarted) / 1000,
      presentationCompression: { frameCodec: settings.frameCodec, jpegQuality: settings.frameCodec === 'jpeg' ? 0.97 : null, keyframeCodec: 'png', pixelProofCodec: 'png', videoColorRange: 'limited' },
      groupShotBirthProvenance: episode.type === 'group' ? Object.hasOwn(episode.evidence, 'shots') ? 'validated-birth-records' : 'legacy-no-birth-records' : null,
      actualCheckpoints, captureCheckpoints, removalEvidence, heavySurvey, groupCameraPolicy, searchSha256, media, encoderStderr: encoding.stderr, silentAudio: true, simulationModified: false, errors: [] };
    await writeJson(path.join(output, 'manifest.json'), manifest);
    // A hard link publishes atomically without rename's overwrite behavior on POSIX.
    await fs.link(partial, final); await fs.unlink(partial);
    return { output, file: final, frames: frames.length };
  } catch (error) {
    if (output) await fs.writeFile(path.join(output, 'failure.json'), JSON.stringify({ schema: 1, verified: false, pixelEvidence, error: String(error.stack || error) }, null, 2), { flag: 'wx' }).catch(() => {});
    throw error;
  } finally {
    const active = [...children]; for (const child of active) child.kill();
    await Promise.all(active.map(child => child.exitCode !== null || child.signalCode !== null ? Promise.resolve() : new Promise(resolve => child.once('close', resolve))));
    try { await (closing || session?.close()); }
    finally { process.off('SIGINT', onInt); process.off('SIGTERM', onTerm); }
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  capture().then(result => console.log(JSON.stringify(result))).catch(error => { console.error(String(error.stack || error)); process.exitCode = 1; });
}
