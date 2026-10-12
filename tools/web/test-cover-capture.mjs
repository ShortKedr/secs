import assert from "node:assert/strict";
import test from "node:test";
import { PNG } from "pngjs";
import { validateSettings, captureMetadata, pixelEvidence } from "./capture-cover.mjs";

const settings = { Seed: 7331, Tick: 180, ShipsPerTeam: 96, Width: 960, Height: 540, Zoom: 2 };
function fixture() {
  const state = { ready: true, paused: true, tick: 180, seed: 7331, profile: "nominal", runtimeMode: "aot-binary",
    camera: { zoom: 2, width: 960, height: 540, scale: 2, x: 0, y: 0, originY: 285 }, following: false, selected: null,
    colliderDebug: { enabled: false }, binary: { version: 3, headerValues: 80, rowValues: 16, totalRows: 34 },
    checksum: "0123456789ABCDEF", counts: { ships: 30, projectiles: 1, debris: 2, particles: 3, turrets: 4 },
    renderCounts: { ships: 24, projectiles: 1, debris: 2, particles: 3, turrets: 4 }, aliveTeams: [20, 10],
    statistics: { entities: 40, samples: 180, meanMs: 1, p95Ms: 2, p99Ms: 3 },
    visualAssets: { gunsAtlas: true, effectsAtlas: true, warpAtlas: true, teamTint: { hullTextures: 2 } },
    revision: "mounted-armament-v7", physicsRevision: "circle-kernel-v1", fleetRevision: "light-swarm-v1",
    counters: { heavyShots: 2, turretShots: 20 } };
  const ships = Array.from({ length: 24 }, (_, i) => [i + 1, i % 2, 0, 0, 0, 0, 0, 0, 2]);
  return { state, ships };
}
test("settings reject ignored roster changes and invalid input", () => {
  assert.deepEqual(validateSettings(settings), settings);
  for (const changes of [{ ShipsPerTeam: 97 }, { Tick: 0 }, { Seed: -1 }, { Width: 959 }, { Zoom: NaN },
    { Zoom: 25 }, { Height: true }, { extra: 1 }]) assert.throws(() => validateSettings({ ...settings, ...changes }));
});
test("capture metadata uses actual populations and on-screen counts, with no timings", () => {
  const { state, ships } = fixture(), m = captureMetadata(settings, state, ships, "chromium-fixture", {});
  assert.equal(m.RenderedShips, 24); assert.equal(m.DockedShips, 6); assert.equal(m.CaptureSurface, "CanvasOnly");
  assert.equal(m.AliveCyan + m.AliveRed, m.AliveShips); assert.equal(m.RuntimeMode, "aot-binary");
  assert.ok(!("Statistics" in m) && !("MeanMs" in m));
});
test("wrong runtime, tick, seed, zoom, view state and inconsistent populations cannot be captured", () => {
  for (const mutate of [s => s.tick++, s => s.seed++, s => s.runtimeMode = "interpreter-binary", s => s.paused = false,
    s => s.camera.zoom = 3, s => s.following = true, s => s.selected = 1, s => s.colliderDebug.enabled = true,
    s => s.statistics.entities++, s => s.counts.ships = -1, s => s.aliveTeams = [0, 30], s => s.visualAssets.gunsAtlas = false]) {
    const { state, ships } = fixture(); mutate(state);
    assert.throws(() => captureMetadata(settings, state, ships, "chromium-fixture", {}));
  }
});
test("offscreen fleets do not pass the activity gate with mere render rows", () => {
  const { state, ships } = fixture(); for (const row of ships) row[3] = 10000;
  assert.throws(() => captureMetadata(settings, state, ships, "chromium-fixture", {}));
});
test("blank, wrong-size and HUD-only screenshots fail independent pixel checks", () => {
  const image = new PNG({ width: settings.Width, height: settings.Height }); image.data.fill(0);
  assert.throws(() => pixelEvidence(PNG.sync.write(image), settings));
  for (let y = 0; y < 80; y++) for (let x = 0; x < settings.Width; x++) {
    const i = (y * image.width + x) * 4; image.data.set([x % 255, 220, y, 255], i);
  }
  assert.throws(() => pixelEvidence(PNG.sync.write(image), settings));
  for (let y = 200; y < 220; y++) for (let x = 50; x < 150; x++) {
    const i = (y * image.width + x) * 4; image.data.set([x % 255, 220, y, 255], i);
  }
  assert.ok(pixelEvidence(PNG.sync.write(image), settings).SceneBrightPixels > 100);
  assert.throws(() => pixelEvidence(PNG.sync.write(image), { ...settings, Width: 961 }));
});
