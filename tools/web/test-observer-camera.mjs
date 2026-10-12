import assert from "node:assert/strict";
import { test } from "node:test";
import { frameObservedShips, resolveObservedShip, observationViewport } from "../../SpaceBattle.Web/wwwroot/observer-camera.js";

const ship = (serial, x = 0, y = 0, radius = 4) => ({ serial, x, y, radius });
const distance = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);
const screen = (ship, camera, view) => ({ x: view.width / 2 + (ship.x - camera.x) * camera.zoom * view.fitScale,
  y: view.centerY - (ship.y - camera.y) * camera.zoom * view.fitScale });
function assertFits(subject, camera, view, viewport) {
  const p = screen(subject, camera, view), padding = subject.radius * 2.2 * camera.zoom * view.fitScale + 14;
  assert.ok(p.x - padding >= viewport.left - 1e-7 && p.x + padding <= viewport.right + 1e-7 &&
    p.y - padding >= viewport.top - 1e-7 && p.y + padding <= viewport.bottom + 1e-7, "complete sprite safe bounds");
}

test("weighted framing and every eased frame fit both participants across sizes and radii", () => {
  for (const [width, height] of [[1440, 900], [390, 844], [844, 390], [2560, 720]])
  for (const [dx, dy] of [[900, 0], [0, 900], [-1200, -700], [0, 0]])
  for (const [r1, r2] of [[4, 4], [40, 3], [3, 60]]) {
    const main = ship(1, -100, 70, r1), target = ship(2, -100 + dx, 70 + dy, r2);
    const viewport = { left: 18, right: width - 18, top: 150, bottom: height - 80 };
    const view = { width, height, centerY: height / 2 + 15, fitScale: 0.5 };
    const camera = { x: 0, y: 0, zoom: 1 };
    frameObservedShips(camera, main, target, viewport, view, 0, true);
    const center = { x: (viewport.left + viewport.right) / 2, y: (viewport.top + viewport.bottom) / 2 };
    const weighted = { x: main.x + dx * 0.28, y: main.y + dy * 0.28 };
    assert.ok(distance(screen(weighted, camera, view), center) < 1e-7);
    assert.ok(main.radius * 2 * camera.zoom * view.fitScale <= 72 + 1e-7);
    main.x += 500; main.y -= 350; target.x -= 600; target.y += 400;
    for (const dt of [0, 1 / 60, 1 / 30, 0.1, 0.5]) {
      frameObservedShips(camera, main, target, viewport, view, dt);
      assertFits(main, camera, view, viewport); assertFits(target, camera, view, viewport);
      assert.ok(distance(screen(main, camera, view), center) <= distance(screen(target, camera, view), center) + 1e-7);
      assert.ok(Number.isFinite(camera.x + camera.y + camera.zoom));
    }
  }
});

test("solo camera has timestep-based pan/zoom, no accumulation, valid target loss and re-entry", () => {
  const view = { width: 1440, height: 900, centerY: 465, fitScale: 0.5 };
  const viewport = { left: 18, right: 1422, top: 150, bottom: 820 };
  const main = ship(1, 0, 0, 8), camera = { x: 0, y: 0, zoom: 1 };
  frameObservedShips(camera, main, null, viewport, view, 0, true);
  const originalScale = camera.zoom * view.fitScale;
  main.radius = 16;
  frameObservedShips(camera, main, null, viewport, view, 0.05);
  assert.ok(Math.abs(camera.zoom * view.fitScale - (originalScale + (2.25 - originalScale) * -Math.expm1(-0.5))) < 1e-7);
  main.radius = 8;
  const first = { ...camera }, second = { ...camera };
  main.x = 10;
  for (let i = 0; i < 60; i++) frameObservedShips(first, main, null, viewport, view, 1 / 60);
  for (let i = 0; i < 30; i++) frameObservedShips(second, main, null, viewport, view, 1 / 30);
  assert.ok(distance(first, second) < 1e-6 && Math.abs(first.zoom - second.zoom) < 1e-6, "30/60 Hz same easing");
  frameObservedShips(first, main, ship(2, 400), viewport, view, 0.016);
  assertFits(main, first, view, viewport);
  frameObservedShips(first, main, null, viewport, view, 0.016);
  assertFits(main, first, view, viewport);
});

test("live killer wins; missing/dead killer uses nearest visible with stable ties and no survivors stops", () => {
  const a = ship(2, 10), b = ship(3, -10), far = ship(4, 100);
  const live = new Map([[2, a], [3, b], [4, far]]), lookup = serial => live.get(serial);
  const death = { serial: 1, killerSerial: 4, position: { x: 0, y: 0 } };
  assert.equal(resolveObservedShip(1, null, death, lookup, [b, a]).ship, far);
  death.killerSerial = 99;
  assert.equal(resolveObservedShip(1, null, death, lookup, [b, a]).ship, a);
  assert.equal(resolveObservedShip(1, { x: 0, y: 0 }, null, lookup, [b, a]).reason, "nearest");
  assert.equal(resolveObservedShip(1, null, death, lookup, []).reason, "empty");
  assert.equal(resolveObservedShip(2, null, death, lookup, []).reason, "tracking");
});

test("safe viewport excludes overlays and stays responsive", () => {
  for (const [width, height] of [[390, 844], [1440, 900], [844, 390]]) {
    const obstacles = [{ left: 0, right: width, top: 0, bottom: 100 },
      { left: 0, right: width, top: height - 50, bottom: height },
      { left: 18, right: 288, top: height - 220, bottom: height - 64 }];
    const r = observationViewport(width, height, obstacles);
    assert.ok(r.right > r.left && r.bottom > r.top);
    for (const o of obstacles) assert.ok(r.right <= o.left || r.left >= o.right || r.bottom <= o.top || r.top >= o.bottom);
  }
});
