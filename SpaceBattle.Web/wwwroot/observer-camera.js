export function resolveObservedShip(serial, lastPosition, death, lookup, ships) {
  const current = lookup(serial);
  if (current) return { ship: current, reason: "tracking" };
  if (death?.serial === serial) {
    const killer = lookup(death.killerSerial);
    if (killer) return { ship: killer, reason: "killer" };
  }
  const origin = death?.serial === serial ? death.position : lastPosition;
  let nearest = null, distance = Infinity;
  if (origin) for (const ship of ships) {
    const d = (ship.x - origin.x) ** 2 + (ship.y - origin.y) ** 2;
    if (d < distance || d === distance && ship.serial < nearest?.serial) {
      nearest = ship; distance = d;
    }
  }
  return { ship: nearest, reason: nearest ? "nearest" : "empty" };
}

export function observationViewport(width, height, obstacles) {
  let candidates = [{ left: 18, top: 18, right: width - 18, bottom: height - 18 }];
  for (const o of obstacles) {
    const next = [];
    for (const r of candidates) {
      if (o.right <= r.left || o.left >= r.right || o.bottom <= r.top || o.top >= r.bottom) { next.push(r); continue; }
      for (const part of [
        { ...r, right: Math.min(r.right, o.left - 12) },
        { ...r, left: Math.max(r.left, o.right + 12) },
        { ...r, bottom: Math.min(r.bottom, o.top - 12) },
        { ...r, top: Math.max(r.top, o.bottom + 12) },
      ]) if (part.right - part.left >= 48 && part.bottom - part.top >= 48) next.push(part);
    }
    candidates = next;
  }
  return candidates.sort((a, b) => (b.right - b.left) * (b.bottom - b.top) - (a.right - a.left) * (a.bottom - a.top))[0]
    ?? { left: 18, top: 18, right: width - 18, bottom: height - 18 };
}

export function frameObservedShips(camera, main, target, viewport, view, seconds, snap = false) {
  const width = Math.max(1, viewport.right - viewport.left - 28);
  const height = Math.max(1, viewport.bottom - viewport.top - 28);
  const focus = target ? { x: main.x + (target.x - main.x) * 0.28, y: main.y + (target.y - main.y) * 0.28 } : main;
  const subjects = target ? [main, target] : [main];
  const fitAt = at => {
    let halfX = 1, halfY = 1;
    for (const ship of subjects) {
      halfX = Math.max(halfX, Math.abs(ship.x - at.x) + ship.radius * 2.2);
      halfY = Math.max(halfY, Math.abs(ship.y - at.y) + ship.radius * 2.2);
    }
    return Math.max(1e-6, Math.min(width / (2 * halfX), height / (2 * halfY), view.fitScale * 20));
  };
  const fitLimit = fitAt(focus);
  const desiredScale = Math.min(fitLimit, 72 / (2 * Math.max(0.1, main.radius)));
  const dt = Math.max(0, Math.min(0.25, seconds));
  const blend = tau => snap ? 1 : -Math.expm1(-dt / tau);
  const currentScale = camera.zoom * view.fitScale;
  const origin = { x: view.width / 2, y: view.centerY };
  const safeCenter = { x: (viewport.left + viewport.right) / 2, y: (viewport.top + viewport.bottom) / 2 };
  const previous = { x: camera.x + (safeCenter.x - origin.x) / currentScale,
    y: camera.y - (safeCenter.y - origin.y) / currentScale };
  const framed = { x: previous.x + (focus.x - previous.x) * blend(0.16),
    y: previous.y + (focus.y - previous.y) * blend(0.16) };
  if (target) {
    const dx = target.x - main.x, dy = target.y - main.y, squared = dx * dx + dy * dy;
    const along = squared ? ((framed.x - main.x) * dx + (framed.y - main.y) * dy) / squared : 0;
    if (along > 0.28) { framed.x -= dx * (along - 0.28); framed.y -= dy * (along - 0.28); }
  }
  // A conservative fit during easing prevents the newly acquired target from leaving the frame.
  const scale = Math.min(fitAt(framed), currentScale + (desiredScale - currentScale) * blend(desiredScale < currentScale ? 0.10 : 0.60));
  camera.x = framed.x - (safeCenter.x - origin.x) / scale;
  camera.y = framed.y + (safeCenter.y - origin.y) / scale;
  camera.zoom = scale / view.fitScale;
  return { ...viewport, scale };
}
