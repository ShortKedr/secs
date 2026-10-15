import { resolveObservedShip, frameObservedShips, observationViewport } from "./observer-camera.js";
import { readColliderBuffer, drawColliders, colliderSummary, colliderDetails } from "./collider-debug.js";
import { verifyBuild, loadAtlasBundle, browserContract } from "./web-atlases.js";

(() => {
  "use strict";
  const $ = (id) => document.getElementById(id);
  const query = new URLSearchParams(location.search);
  const testMode = query.get("test") === "1";
  const diagnosticMode = ["simulation", "render", "minimal", "normal"].includes(query.get("diagnostics")) ? query.get("diagnostics") : null;
  const imageLess = diagnosticMode === "simulation" || diagnosticMode === "minimal";
  let assetState = null, buildIdentity = null, revisions = null;
  let fatalError = null, frameRequest = 0, frameStopped = false, canvasResizes = 0;
  let contextLost = false, frames = 0;
  const strings = {
    en: {
      title: "Space battle",
      profile: "Profile",
      seed: "Seed",
      low: "Low",
      nominal: "Nominal",
      high: "High",
      cyan: "CYAN",
      red: "RED",
      hull: "Hull",
      shield: "Shield",
      heat: "Heat",
      loading: "Loading simulation",
      retry: "Retry",
      pause: "Pause",
      play: "Play",
      reset: "Reset battle",
      report: "Simulation report",
      fullscreen: "Fullscreen",
      follow: "Follow ship",
      colliders: "Collider outlines (F2)",
      docked: "Docked at",
      close: "Clear selection",
      zoomIn: "Zoom in",
      zoomOut: "Zoom out",
      fit: "Fit arena",
      running: "Simulation running",
      paused: "Paused",
      error: "Simulation unavailable",
      destroyed: "Ship destroyed",
      ships: "ships",
      projectiles: "projectiles",
      carrier: "Carrier",
      escort: "Escort",
      teamHull: "hull",
      invalidSeed: "Seed must be an integer from 0 to 4294967295",
      preparing: "Starting .NET WebAssembly and loading ship sprites",
      failed: "Reload to retry the runtime initialization.",
      statistics: "Statistics",
      closeStats: "Close statistics",
      performance: "Performance",
      renderFps: "Render FPS",
      tickRate: "Ticks/s / target",
      tickMean: "Tick mean / last",
      tickMaximum: "Maximum tick",
      tickBudget: "Tick budget / used",
      tickSamples: "Tick samples",
      renderCpu: "Canvas CPU",
      exportCpu: "Render export CPU",
      backlog: "Backlog / discarded ticks",
      entities: "ECS entities",
      total: "Total",
      shipCounts: "Ships / docked",
      projectileCounts: "Projectiles",
      turretCounts: "Turrets / docked",
      specialShots: "Heavy / turret shots",
      debrisCounts: "Debris / particles",
      physicsCounts: "Bodies / fixtures",
      stageCosts: "Stages · mean ms",
      storage: "Storage",
      nativeMemory: "Native ECS / accessors",
      managedMemory: "Managed heap",
      archetypes: "Archetypes / chunks",
      combat: "Combat",
      shotCounts: "Shots / hits",
      production: "Destroyed / built",
      friendlyHits: "Friendly / self hits",
      groupCounts: "Group / cover orders",
      collect: "Collect",
      ai: "AI",
      thermalInventory: "Thermal / inventory",
      steering: "Steering",
      weapons: "Weapons",
      carriers: "Carriers",
      physics: "Physics",
      hits: "Hits",
      lifecycle: "Lifecycle",
      tickUnit: "ticks/s",
      entityUnit: "entities",
      ms: "ms",
      tickTimeUnit: "ms/tick",
    },
    ru: {
      title: "Космический бой",
      profile: "Профиль",
      seed: "Сид",
      low: "Низкий",
      nominal: "Обычный",
      high: "Высокий",
      cyan: "БИРЮЗОВЫЕ",
      red: "КРАСНЫЕ",
      hull: "Корпус",
      shield: "Щит",
      heat: "Нагрев",
      loading: "Загрузка симуляции",
      retry: "Повторить",
      pause: "Пауза",
      play: "Продолжить",
      reset: "Начать заново",
      report: "Отчёт о симуляции",
      fullscreen: "Полный экран",
      follow: "Следить за кораблём",
      colliders: "Контуры коллайдеров (F2)",
      docked: "Пристыкован к",
      close: "Снять выделение",
      zoomIn: "Приблизить",
      zoomOut: "Отдалить",
      fit: "Вся арена",
      running: "Симуляция идёт",
      paused: "Пауза",
      error: "Симуляция недоступна",
      destroyed: "Корабль уничтожен",
      ships: "кораблей",
      projectiles: "снарядов",
      carrier: "Носитель",
      escort: "Эскорт",
      teamHull: "корпус",
      invalidSeed: "Сид: целое число от 0 до 4294967295",
      preparing: "Запуск .NET WebAssembly и загрузка спрайтов",
      failed: "Перезагрузите страницу для повторного запуска.",
      statistics: "Статистика",
      closeStats: "Закрыть статистику",
      performance: "Производительность",
      renderFps: "FPS рендера",
      tickRate: "Тики/с / цель",
      tickMean: "Тик: среднее / последний",
      tickMaximum: "Максимальный тик",
      tickBudget: "Бюджет тика / занят",
      tickSamples: "Замеров тиков",
      renderCpu: "Canvas CPU",
      exportCpu: "Экспорт рендера CPU",
      backlog: "Очередь / отброшено тиков",
      entities: "ECS-сущности",
      total: "Всего",
      shipCounts: "Корабли / в доке",
      projectileCounts: "Снаряды",
      turretCounts: "Турели / в доке",
      specialShots: "Тяжёлые / турельные выстрелы",
      debrisCounts: "Обломки / частицы",
      physicsCounts: "Тела / коллайдеры",
      stageCosts: "Стадии · среднее мс",
      storage: "Хранилище",
      nativeMemory: "ECS / буферы доступа",
      managedMemory: "Управляемая куча",
      archetypes: "Архетипы / чанки",
      combat: "Бой",
      shotCounts: "Выстрелы / попадания",
      production: "Уничтожено / построено",
      friendlyHits: "Союзные / самопопадания",
      groupCounts: "Группы / укрытия: приказы",
      collect: "Сбор",
      ai: "ИИ",
      thermalInventory: "Нагрев / инвентарь",
      steering: "Движение",
      weapons: "Оружие",
      carriers: "Носители",
      physics: "Физика",
      hits: "Попадания",
      lifecycle: "Жизненный цикл",
      tickUnit: "тиков/с",
      entityUnit: "сущностей",
      ms: "мс",
      tickTimeUnit: "мс/тик",
    },
  };
  let lang = query.get("lang");
  try {
    lang ||= localStorage.getItem("spaceBattle.lang");
  } catch {}
  lang = lang === "ru" ? "ru" : "en";
  let ready = false,
    paused = testMode || Boolean(diagnosticMode),
    speed = 1,
    snapshot = null,
    atlas,
    sprites,
    gunAtlas,
    gunSprites,
    effectsAtlas,
    projectileSprites,
    particleSprites,
    warpAtlas,
    warpSprites;
  let selected = null,
    following = false,
    accumulator = 0,
    last = 0,
    busy = false,
    runtimeStarted = false;
  let observedPosition = null, observationMain = null, observationTarget = null,
    observationReason = null, observationBounds = null, observationClock = 0, observationSwitches = 0;
  let runtime,
    bridge,
    runtimeMode = null,
    heapBuffer = null,
    framePointer = -1,
    header = null,
    fps = 0,
    lastFrameMs = 0,
    lastAdvanceMs = 0,
    tickCostMs = 0;
  let ticksPerSecond = 0,
    rateTicks = 0,
    rateStart = 0,
    renderCpuMs = 0,
    discardedTicks = 0;
  const rowViews = [];
  let colliderDebug = false, colliderRows = null;
  const frameGroups = [[], [], [], [], []];
  const profiles = ["low", "nominal", "high"];
  const counterNames = [
    "shots",
    "hits",
    "shipsCreated",
    "shipsDestroyed",
    "aimDecisions",
    "impreciseAimDecisions",
    "friendlyFireHits",
    "selfHits",
    "dockings",
    "builds",
    "repairs",
    "overheats",
    "shieldBreaks",
    "debrisHits",
    "structuralAdds",
    "structuralRemoves",
  ];
  const tickMs = 1000 / 60;
  const headerValues = 80,
    headerBytes = headerValues * 8;
  const stageKeys = [
    "collect",
    "ai",
    "thermalInventory",
    "steering",
    "weapons",
    "carriers",
    "physics",
    "hits",
    "lifecycle",
  ];
  for (const key of stageKeys) {
    const row = document.createElement("div"),
      label = document.createElement("dt"),
      value = document.createElement("dd");
    label.dataset.i18n = key;
    value.id = "stage-" + key;
    row.append(label, value);
    $("stage-metrics").append(row);
  }
  const catchUpBudgetMs = 6;
  for (const [id, label, anchor] of [
    ["turrets", "turretCounts", "ships"],
    ["special-shots", "specialShots", "shots"],
  ]) {
    const row = document.createElement("div");
    const dt = document.createElement("dt"), dd = document.createElement("dd");
    dt.dataset.i18n = label;
    dd.id = "metric-" + id;
    row.append(dt, dd);
    $("metric-" + anchor).parentElement.after(row);
  }
  let width = 1,
    height = 1,
    dpr = 1,
    fitScale = 1,
    uiTime = 0;
  const camera = { x: 0, y: 0, zoom: 1 };
  const canvas = $("scene");
  let ctx = null, contextError = null;
  try { ctx = canvas.getContext("2d", { alpha: false }); }
  catch (error) { contextError = error; }
  const t = (key) => strings[lang][key] || key;
  const icons = () => window.lucide?.createIcons();
  function localize() {
    document.documentElement.lang = lang;
    document.querySelectorAll("[data-i18n]").forEach((el) => {
      el.textContent = t(el.dataset.i18n);
    });
    document.querySelectorAll("[data-tip]").forEach((el) => {
      const text = t(
        el.id === "pause" ? (paused ? "play" : "pause") : el.dataset.tip,
      );
      el.title = text;
      el.setAttribute("aria-label", text);
    });
    $("language").textContent = lang === "en" ? "RU" : "EN";
    $("report").href =
      "https://github.com/ShortKedr/skecs/blob/main/docs/space-battle/" +
      (lang === "ru" ? "REPORT.ru.md" : "REPORT.md");
    updateUI();
  }
  function accept(pointer) {
    const colliderPointer = colliderDebug ? bridge.ColliderSnapshot() : 0;
    const buffer = runtime.localHeapViewF64().buffer;
    if (
      !Number.isInteger(pointer) ||
      pointer < 0 ||
      pointer % 8 !== 0 ||
      pointer + headerBytes > buffer.byteLength
    ) {
      throw new Error("Invalid WASM frame pointer");
    }
    if (buffer !== heapBuffer || pointer !== framePointer) {
      heapBuffer = buffer;
      framePointer = pointer;
      header = new Float64Array(buffer, pointer, headerValues);
      rowViews.length = 0;
    }
    const totalRows = header[30],
      byteLength = header[31];
    if (
      header[0] !== 3 ||
      !Number.isInteger(totalRows) ||
      totalRows < 0 ||
      !Number.isInteger(byteLength) ||
      byteLength !== headerBytes + totalRows * 128 ||
      pointer + byteLength > buffer.byteLength
    ) {
      throw new Error("Invalid WASM binary frame header");
    }
    let renderRows = 0;
    for (const i of [10, 11, 12, 13, 64]) {
      if (!Number.isInteger(header[i]) || header[i] < 0) {
        throw new Error("Invalid WASM render count");
      }
      renderRows += header[i];
    }
    if (renderRows !== totalRows || !profiles[header[4]]) {
      throw new Error("Inconsistent WASM binary frame");
    }
    const entityTotal = [6, 7, 8, 9, 63].reduce((sum, i) => sum + header[i], 0);
    if ([6, 7, 8, 9, 63].some((i) => !Number.isInteger(header[i]) || header[i] < 0) ||
        entityTotal !== header[37] || header[64] > header[63] ||
        header[62] !== header[6] - header[10]) {
      throw new Error("Inconsistent WASM ECS counts");
    }
    // Views belong to buffer slots, not entities: grouping boundaries can move.
    while (rowViews.length < totalRows) {
      rowViews.push(
        new Float64Array(
          buffer,
          pointer + headerBytes + rowViews.length * 128,
          16,
        ),
      );
    }
    snapshot ||= {
      counts: {},
      counters: {},
      performance: {
        stages: new Array(stageKeys.length).fill(0),
        gcCollections: [0, 0, 0],
      },
      ships: frameGroups[0],
      projectiles: frameGroups[1],
      debris: frameGroups[2],
      particles: frameGroups[3],
      turrets: frameGroups[4],
      binary: {},
      ...revisions,
    };
    snapshot.tick = header[1];
    snapshot.arenaHalfSize = header[2];
    snapshot.seed = header[3];
    snapshot.profile = profiles[header[4]];
    snapshot.stepMs = header[5];
    snapshot.runtimeMode = runtimeMode;
    Object.assign(snapshot.binary, { version: header[0], headerValues, rowValues: 16, totalRows, byteLength });
    snapshot.counts.ships = header[6];
    snapshot.counts.projectiles = header[7];
    snapshot.counts.debris = header[8];
    snapshot.counts.particles = header[9];
    snapshot.counts.turrets = header[63];
    snapshot.counters.heavyShots = header[65];
    snapshot.counters.turretShots = header[66];
    snapshot.counters.reinforcementWaves = header[67];
    snapshot.counters.reinforcementShips = header[68];
    snapshot.pendingReinforcements = header[69];
    snapshot.aliveTeams = [header[70], header[71]];
    snapshot.fleetComposition = ["uniform", "light-swarm"][header[72]];
    Object.assign(snapshot.performance, {
      samples: header[32],
      meanMs: header[33],
      p95Ms: header[34],
      p99Ms: header[35],
      maxMs: header[36],
      entities: header[37],
      nativeBytes: header[38],
      accessorBytes: header[39],
      archetypes: header[40],
      chunks: header[41],
      physicsBodies: header[42],
      physicsFixtures: header[43],
      managedBytes: header[44],
      exportCpuMs: header[45],
      dockedShips: header[62],
    });
    for (let i = 0; i < stageKeys.length; i++)
      snapshot.performance.stages[i] = header[46 + i];
    for (let i = 0; i < 3; i++)
      snapshot.performance.gcCollections[i] = header[55 + i];
    for (let i = 0; i < counterNames.length; i++) {
      snapshot.counters[counterNames[i]] = header[14 + i];
    }
    let offset = 0;
    for (let group = 0; group < frameGroups.length; group++) {
      const rows = frameGroups[group],
        count = header[group === 4 ? 64 : 10 + group];
      rows.length = count;
      for (let i = 0; i < count; i++) rows[i] = rowViews[offset++];
    }
    if (following) resolveObservation();
    colliderRows = colliderDebug ? readColliderBuffer(buffer, colliderPointer) : null;
  }
  function showColliders(enabled) {
    if (!ready || busy) return;
    colliderDebug = Boolean(enabled);
    accept(bridge.DebugColliders(colliderDebug));
    $("colliders").classList.toggle("active", colliderDebug);
    $("colliders").setAttribute("aria-pressed", String(colliderDebug));
    draw();
  }
  function advance(ticks) {
    const started = performance.now();
    accept(bridge.Advance(ticks));
    lastAdvanceMs = performance.now() - started;
    tickCostMs = lastAdvanceMs / ticks;
  }
  function reset() {
    if (!ready || busy) return;
    const seed = Number($("seed").value);
    if (
      $("seed").value === "" ||
      !Number.isInteger(seed) ||
      seed < 0 ||
      seed > 4294967295
    ) {
      $("seed").setCustomValidity(t("invalidSeed"));
      $("seed").reportValidity();
      return;
    }
    $("seed").setCustomValidity("");
    try {
      stopFollowing();
      const pointer = bridge.Reset($("profile").value, seed);
      revisions ||= { revision: bridge.Revision(), fleetRevision: bridge.FleetRevision(), deploymentRevision: bridge.DeploymentRevision(),
        physicsRevision: bridge.PhysicsRevision(), damageRevision: bridge.DamageRevision(), tacticalRevision: bridge.TacticalRevision() };
      accept(pointer);
      selected = null;
      observationSwitches = 0;
      accumulator = 0;
      last = 0;
      tickCostMs = 0;
      lastAdvanceMs = 0;
      ticksPerSecond =
        rateTicks =
        renderCpuMs =
        discardedTicks =
        fps =
        lastFrameMs =
          0;
      rateStart = performance.now();
      fit();
      updateUI();
      draw();
    } catch (error) {
      fail(error);
    }
  }
  function setPaused(value) {
    paused = value;
    accumulator = 0;
    last = 0;
    ticksPerSecond = rateTicks = 0;
    rateStart = performance.now();
    $("pause").innerHTML =
      '<i data-lucide="' + (paused ? "play" : "pause") + '"></i>';
    icons();
    localize();
  }
  function resize() {
    const nextWidth = canvas.clientWidth, nextHeight = canvas.clientHeight;
    if (nextWidth <= 0 || nextHeight <= 0) return;
    width = nextWidth; height = nextHeight;
    dpr = Math.min(devicePixelRatio || 1, 2);
    const pixelsX = Math.round(width * dpr), pixelsY = Math.round(height * dpr);
    if (canvas.width !== pixelsX || canvas.height !== pixelsY) {
      canvas.width = pixelsX; canvas.height = pixelsY;
      canvasResizes++;
    }
    calculateFit();
    draw();
  }
  function calculateFit() {
    const half = snapshot?.arenaHalfSize || 2000;
    fitScale = Math.max(
      0.001,
      Math.min(
        (width - 40) / (half * 2),
        (height - (width < 620 ? 220 : 170)) / (half * 2),
      ),
    );
  }
  function fit() {
    camera.x = 0;
    camera.y = 0;
    camera.zoom = 1;
    stopFollowing();
    calculateFit();
  }
  function centerY() {
    return height / 2 + (width < 620 ? 30 : 15);
  }
  function screen(row) {
    const s = fitScale * camera.zoom;
    return {
      x: width / 2 + (row[3] - camera.x) * s,
      y: centerY() - (row[4] - camera.y) * s,
    };
  }
  function world(x, y) {
    const s = fitScale * camera.zoom;
    return {
      x: camera.x + (x - width / 2) / s,
      y: camera.y - (y - centerY()) / s,
    };
  }
  function zoom(factor, x = width / 2, y = centerY()) {
    stopFollowing();
    const before = world(x, y);
    camera.zoom = Math.max(0.35, Math.min(20, camera.zoom * factor));
    const after = world(x, y);
    camera.x += before.x - after.x;
    camera.y += before.y - after.y;
    draw();
  }
  function observationRow(row) {
    return { serial: row[0], team: row[1], kind: row[2], x: row[3], y: row[4], radius: row[8],
      hull: row[9], shield: row[10], heat: row[11] };
  }
  function observe(serial) {
    if (!ready || serial === null || serial < 0 || !bridge.ShipObservation(serial, 0)) return null;
    const row = snapshot.ships.find(r => r[0] === serial);
    const ship = row ? observationRow(row) : { serial, x: bridge.ShipObservation(serial, 3), y: bridge.ShipObservation(serial, 4),
      radius: bridge.ShipObservation(serial, 5), team: bridge.ShipObservation(serial, 6), kind: bridge.ShipObservation(serial, 7),
      hull: bridge.ShipObservation(serial, 8), shield: bridge.ShipObservation(serial, 9), heat: bridge.ShipObservation(serial, 10) };
    ship.targetSerial = bridge.ShipObservation(serial, 1);
    ship.dockCarrierSerial = bridge.ShipObservation(serial, 2);
    return ship;
  }
  function stopFollowing() {
    following = false;
    observationMain = observationTarget = observationBounds = null;
    observationClock = 0;
    if (ready && snapshot) bridge.ObserveShip(-1);
    $("follow").classList.remove("active");
    $("follow").setAttribute("aria-pressed", "false");
  }
  function setFollowing(value) {
    if (!value || !observe(selected)) { stopFollowing(); updateUI(); draw(); return; }
    following = true;
    observationReason = "selected";
    bridge.ObserveShip(selected);
    resolveObservation();
    updateUI();
    track(true);
    draw();
  }
  function resolveObservation() {
    const current = observe(selected);
    const death = current ? null : { serial: bridge.ObservedDeath(0), killerSerial: bridge.ObservedDeath(1),
      position: { x: bridge.ObservedDeath(2), y: bridge.ObservedDeath(3) } };
    const resolved = current ? { ship: current, reason: "tracking" } : resolveObservedShip(selected, observedPosition, death,
      serial => serial === selected ? current : observe(serial), snapshot.ships.map(observationRow));
    if (!resolved.ship) { selected = null; observationReason = "empty"; stopFollowing(); return; }
    if (resolved.ship.serial !== selected) {
      selected = resolved.ship.serial;
      observationReason = resolved.reason;
      observationSwitches++;
      bridge.ObserveShip(selected);
    }
    const watched = current ?? observe(selected);
    observedPosition = { x: watched.x, y: watched.y };
    const carrier = watched.dockCarrierSerial >= 0 ? snapshot.ships.find(r => r[0] === watched.dockCarrierSerial) : null;
    observationMain = carrier ? observationRow(carrier) : watched;
    const target = snapshot.ships.find(r => r[0] === watched.targetSerial && r[0] !== observationMain.serial);
    observationTarget = target ? observationRow(target) : null;
  }
  function track(snap = false) {
    if (!following || !observationMain) return;
    const now = performance.now(), seconds = observationClock ? (now - observationClock) / 1000 : 0;
    observationClock = now;
    const obstacles = [".toolbar", ".scoreboard", "#telemetry", "#simulation-stats", ".camera-tools", "footer"]
      .map(selector => document.querySelector(selector)).filter(el => !el.hidden)
      .map(el => el.getBoundingClientRect());
    const safe = observationViewport(width, height, obstacles);
    observationBounds = frameObservedShips(camera, observationMain, observationTarget, safe,
      { width, height, centerY: centerY(), fitScale }, seconds, snap);
  }
  const color = (r) =>
    `rgba(${Math.round(r[12] * 255)},${Math.round(r[13] * 255)},${Math.round(r[14] * 255)},${Math.max(0, Math.min(1, r[15]))})`;
  const teamPalette = [[0.2, 0.85, 0.95], [1, 0.37, 0.4]];
  const teamPaint = teamPalette.map(rgb => `rgb(${rgb.map(value => Math.round(value * 255)).join(",")})`);
  function draw(minimal = imageLess) {
    if (fatalError) return false;
    if (diagnosticMode === "simulation") return true;
    try {
      ensureHeapViews();
      if (!ctx || contextLost || ctx.isContextLost?.()) throw new Error("Canvas 2D context unavailable or lost");
      drawScene(minimal);
      return true;
    } catch (error) { fail(error, "render"); return false; }
  }
  function drawScene(minimal) {
    track();
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = "#080a0c";
    ctx.fillRect(0, 0, width, height);
    // Deterministic stars are visual only, independent of simulation time and seed.
    for (let i = 0; i < 170; i++) {
      const x = (((i * 7919 + 113) % 10007) / 10007) * width,
        y = (((i * 3571 + 317) % 9973) / 9973) * height;
      ctx.fillStyle = i % 7 === 0 ? "#64757a" : "#263135";
      ctx.fillRect(x, y, i % 7 === 0 ? 1.5 : 1, 1);
    }
    if (!snapshot) return;
    const scale = fitScale * camera.zoom,
      half = snapshot.arenaHalfSize;
    const origin = screen([0, 0, 0, -half, half]);
    ctx.strokeStyle = "#263337";
    ctx.lineWidth = 1;
    ctx.strokeRect(origin.x, origin.y, half * 2 * scale, half * 2 * scale);
    // Debris is a background hazard; reserve bright colors for combat effects.
    ctx.fillStyle = "#606060";
    for (const r of snapshot.debris) {
      const p = screen(r);
      ctx.beginPath();
      ctx.arc(p.x, p.y, Math.max(1, r[8] * scale), 0, Math.PI * 2);
      ctx.fill();
    }
    for (const r of snapshot.particles) {
      if (r[2] !== 3 && r[2] !== 6 && r[2] !== 7) drawParticle(r, scale, minimal);
    }
    ctx.globalAlpha = 1;
    for (const r of snapshot.projectiles) {
      const p = screen(r);
      const heavy = r[2] === 4;
      const length = Math.max(heavy ? 18 : r[2] === 3 ? 10 : 6,
        Math.hypot(r[5], r[6]) * scale * (heavy ? 0.07 : 0.025),
        r[8] * scale * (heavy ? 4 : 2));
      if (p.x < -length || p.y < -length || p.x > width + length || p.y > height + length) continue;
      const angle = r[5] || r[6] ? Math.atan2(-r[6], r[5]) : -r[7];
      const c = Math.cos(angle), s = Math.sin(angle);
      ctx.setTransform(dpr * c, dpr * s, -dpr * s, dpr * c, p.x * dpr, p.y * dpr);
      if (minimal) { ctx.fillStyle = color(r); ctx.fillRect(0, -1, length, 2); }
      else drawEffectSprite(projectileSprites.get(r[2]), 0, 0, length);
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    for (const r of snapshot.ships) {
      const p = screen(r),
        sprite = sprites.get(r[2]);
      const size = Math.max(r[2] >= 8 ? 13 : 5, r[8] * 2 * scale);
      if (
        p.x < -size ||
        p.y < -size ||
        p.x > width + size ||
        p.y > height + size
      )
        continue;
      ctx.save();
      ctx.translate(p.x, p.y);
      ctx.rotate(Math.PI / 2 - r[7]);
      if (sprite && !minimal) {
        const h = size * 1.5,
          w = (h * sprite.width) / sprite.height;
        ctx.drawImage(
          atlas[r[1] === 0 ? 0 : 1],
          sprite.x,
          sprite.y,
          sprite.width,
          sprite.height,
          -w / 2,
          -h / 2,
          w,
          h,
        );
      } else if (minimal) {
        ctx.fillStyle = teamPaint[r[1] === 0 ? 0 : 1];
        ctx.fillRect(-size * 0.3, -size * 0.6, size * 0.6, size * 1.2);
      }
      ctx.restore();
      if (!minimal && [5, 6, 8, 9, 10].includes(r[2]))
        drawGun(p, Math.max(1.5, size * 0.12), r[7], 4, 0, r[11], r[1]);
      ctx.strokeStyle = r[1] === 0 ? "#5de4f1" : "#ff686f";
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      ctx.arc(p.x, p.y, size * 0.55, Math.PI * 0.15, Math.PI * 0.85);
      ctx.stroke();
      if (r[0] === selected) {
        ctx.strokeStyle = "#f3fafb";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(p.x, p.y, size * 0.75 + 5, 0, Math.PI * 2);
        ctx.stroke();
      }
      if (following && r[0] === observationMain?.serial) observationMarker(p, size * 0.82 + 8, "#f3fafb", 2);
      if (following && r[0] === observationTarget?.serial) observationMarker(p, size * 0.82 + 8, "#ffc366", 1.6);
    }
    for (const r of snapshot.turrets) {
      const p = screen(r), radius = Math.max(1.4, r[8] * scale);
      if (p.x < -radius * 4 || p.y < -radius * 4 || p.x > width + radius * 4 || p.y > height + radius * 4) continue;
      if (!minimal) drawGun(p, radius, r[7], r[2], r[10], r[11], r[1]);
    }
    // Contact and warp arrival flashes remain visible over the hull.
    for (const r of snapshot.particles) {
      if (r[2] === 3 || r[2] === 6 || r[2] === 7) drawParticle(r, scale, minimal);
    }
    ctx.globalAlpha = 1;
    if (colliderDebug) drawColliders(ctx, colliderRows,
      { ...camera, scale, width, height, originY: centerY() });
    $("zoom-label").textContent = Math.round(camera.zoom * 100) + "%";
  }
  function observationMarker(p, radius, color, thickness) {
    const arm = Math.min(14, radius * 0.4);
    ctx.strokeStyle = color; ctx.lineWidth = thickness; ctx.beginPath();
    for (const [x, y] of [[-1, -1], [-1, 1], [1, -1], [1, 1]]) {
      ctx.moveTo(p.x + x * radius, p.y + y * (radius - arm));
      ctx.lineTo(p.x + x * radius, p.y + y * radius);
      ctx.lineTo(p.x + x * (radius - arm), p.y + y * radius);
    }
    ctx.stroke();
  }
  function drawParticle(r, scale, minimal = false) {
    const isWarp = r[2] >= 4;
    const p = screen(r), sprite = isWarp ? warpSprites.get(r[2] - 4) : particleSprites.get(r[2]);
    const source = isWarp ? warpAtlas : effectsAtlas;
    const size = Math.max(2, r[8] * scale * 2);
    if (p.x < -size || p.y < -size || p.x > width + size || p.y > height + size) return;
    ctx.globalAlpha = Math.max(0, Math.min(1, r[15]));
    if (minimal) { ctx.fillStyle = color(r); ctx.fillRect(p.x - size / 2, p.y - size / 2, size, size); return; }
    if (r[7] === 0) drawEffectSprite(sprite, p.x, p.y, size, source);
    else {
      ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(-r[7]);
      drawEffectSprite(sprite, 0, 0, size, source); ctx.restore();
    }
  }
  function drawEffectSprite(sprite, x, y, extent, source = effectsAtlas) {
    const scale = extent / Math.max(sprite.width, sprite.height);
    ctx.drawImage(source, sprite.x, sprite.y, sprite.width, sprite.height,
      x - sprite.pivotX * scale, y - sprite.pivotY * scale,
      sprite.width * scale, sprite.height * scale);
  }
  function drawGunSprite(sprite, radius, source) {
    const unit = (radius * 2) / sprite.width;
    ctx.drawImage(source, sprite.x, sprite.y, sprite.width, sprite.height,
      -sprite.pivotX * unit, -sprite.pivotY * unit,
      sprite.width * unit, sprite.height * unit);
  }
  function drawGun(p, radius, angle, kind, recoil, heat, team) {
    const source = gunAtlas[team === 0 ? 0 : 1];
    ctx.save();
    ctx.translate(p.x, p.y);
    drawGunSprite(gunSprites.get(-1), radius, source);
    ctx.rotate(Math.PI / 2 - angle);
    ctx.translate(0, Math.max(0, Math.min(1, recoil)) * radius * 0.7);
    drawGunSprite(gunSprites.get(kind), radius, source);
    ctx.restore();
    ctx.fillStyle = heat > 0.75 ? "#d17a3d" : teamPaint[team === 0 ? 0 : 1];
    ctx.beginPath();
    ctx.arc(p.x, p.y, Math.max(0.5, radius * 0.15), 0, Math.PI * 2);
    ctx.fill();
  }
  function updateUI() {
    if (!ready || !snapshot) return;
    $("status").textContent = t(paused ? "paused" : "running");
    $("live-dot").style.background = paused ? "#89979e" : "#55d6e8";
    const seconds = Math.floor(snapshot.tick / 60);
    $("clock").textContent =
      String(Math.floor(seconds / 60)).padStart(2, "0") +
      ":" +
      String(seconds % 60).padStart(2, "0");
    $("tick").textContent = "TICK " + snapshot.tick.toLocaleString(lang);
    for (let team = 0; team < 2; team++) {
      const rows = snapshot.ships.filter((r) => r[1] === team),
        prefix = team === 0 ? "cyan" : "red";
      $(prefix + "-count").textContent = rows.length;
      $(prefix + "-hull").textContent =
        Math.round(
          (rows.reduce((n, r) => n + r[9], 0) / Math.max(1, rows.length)) * 100,
        ) +
        "% " +
        t("teamHull");
    }
    const metrics = statistics();
    $("stats").textContent =
      `${metrics.entities.toLocaleString(lang)} ${t("entityUnit")} · ${metrics.fps.toFixed(0)} FPS`;
    $("checksum").textContent =
      `${metrics.ticksPerSecond.toFixed(1)} ${t("tickUnit")} · ${metrics.meanMs.toFixed(2)} ${t("tickTimeUnit")}`;
    if (!$("simulation-stats").hidden) updateStatistics(metrics);
    const ship = snapshot.ships.find((r) => r[0] === selected);
    const detail = ship ? observationRow(ship) : observe(selected);
    $("telemetry").hidden = selected === null;
    if (selected !== null) {
      $("ship-name").textContent = detail
        ? `${t(detail.kind >= 8 ? "carrier" : "escort")} ${detail.kind} · #${detail.serial}`
        : t("destroyed");
      $("follow").classList.toggle("active", following);
      $("follow").setAttribute("aria-pressed", String(following));
      $("follow").disabled = !detail;
      for (const [name, value] of detail
        ? [
            ["hull", detail.hull],
            ["shield", detail.shield],
            ["heat", detail.heat],
          ]
        : [
            ["hull", 0],
            ["shield", 0],
            ["heat", 0],
          ]) {
        $(name).value = Math.max(0, Math.min(100, value * 100));
        $(name + "-value").textContent = Math.round(value * 100) + "%";
      }
      $("ship-position").textContent = detail?.dockCarrierSerial >= 0
        ? `${t("docked")} #${detail.dockCarrierSerial}` : ship
        ? `X ${ship[3].toFixed(0)} · Y ${ship[4].toFixed(0)} · V ${Math.hypot(ship[5], ship[6]).toFixed(1)}`
        : "";
    }
  }
  function statistics() {
    if (!snapshot) return null;
    const budgetMs = tickMs / speed;
    return {
      ...snapshot.performance,
      fps,
      ticksPerSecond:
        paused || document.hidden || testMode ? 0 : ticksPerSecond,
      targetTicksPerSecond: paused ? 0 : 60 * speed,
      budgetMs,
      budgetUsedPercent: (snapshot.performance.meanMs / budgetMs) * 100,
      lastMs: snapshot.stepMs,
      renderCpuMs,
      backlogTicks: accumulator / tickMs,
      discardedTicks,
      counts: { ...snapshot.counts },
      runtimeMode,
    };
  }
  function updateStatistics(m) {
    const number = (value, digits = 0) =>
      Number.isFinite(value)
        ? value.toLocaleString(lang, {
            minimumFractionDigits: digits,
            maximumFractionDigits: digits,
          })
        : "—";
    const pair = (a, b, digits = 0) =>
      `${number(a, digits)} / ${number(b, digits)}`;
    const ms = (value) => `${number(value, 2)} ${t("ms")}`;
    const put = (key, value) => {
      $("metric-" + key).textContent = value;
    };
    put("fps", number(m.fps, 1));
    put("rate", pair(m.ticksPerSecond, m.targetTicksPerSecond, 1));
    put("mean", pair(m.meanMs, m.lastMs, 2) + " " + t("ms"));
    put("percentiles", pair(m.p95Ms, m.p99Ms, 2) + " " + t("ms"));
    put("max", ms(m.maxMs));
    put("budget", `${ms(m.budgetMs)} / ${number(m.budgetUsedPercent)}%`);
    put("samples", `${m.samples} / 600`);
    put("render", ms(m.renderCpuMs));
    put("export", ms(m.exportCpuMs));
    put("backlog", pair(m.backlogTicks, m.discardedTicks, 1));
    put("total", number(m.entities));
    put("ships", pair(m.counts.ships, m.dockedShips));
    put("turrets", pair(m.counts.turrets, m.counts.turrets - snapshot.turrets.length));
    put("special-shots", pair(snapshot.counters.heavyShots, snapshot.counters.turretShots));
    put("projectiles", number(m.counts.projectiles));
    put("debris", pair(m.counts.debris, m.counts.particles));
    put("physics", pair(m.physicsBodies, m.physicsFixtures));
    put(
      "native",
      pair(m.nativeBytes / 1048576, m.accessorBytes / 1048576, 2) + " MiB",
    );
    put("managed", number(m.managedBytes / 1048576, 2) + " MiB");
    put("archetypes", pair(m.archetypes, m.chunks));
    put("gc", m.gcCollections.join(" / "));
    put("shots", pair(snapshot.counters.shots, snapshot.counters.hits));
    put(
      "production",
      pair(snapshot.counters.shipsDestroyed, snapshot.counters.builds),
    );
    put(
      "friendly",
      pair(snapshot.counters.friendlyFireHits, snapshot.counters.selfHits),
    );
    put("groups", pair(header[60], header[61]));
    stageKeys.forEach((key, i) => {
      $("stage-" + key).textContent = number(m.stages[i], 3);
    });
    $("stats-runtime").textContent =
      `SECS · ${runtimeMode} · ${snapshot.revision}`;
  }
  function showStatistics(value) {
    $("simulation-stats").hidden = !value;
    $("statistics").classList.toggle("active", value);
    $("statistics").setAttribute("aria-expanded", String(value));
    $("battle").classList.toggle("statistics-open", value);
    updateUI();
  }
  function fail(error, stage = "startup") {
    if (fatalError) return;
    fatalError = { stage, message: String(error?.message || error), tick: snapshot?.tick ?? null };
    frameStopped = true;
    cancelAnimationFrame(frameRequest);
    ready = false;
    paused = true;
    $("load-state").hidden = false;
    $("spinner").hidden = true;
    $("load-title").textContent = t("error");
    $("load-detail").textContent = String(error?.message || error);
    $("retry").hidden = false;
    $("status").textContent = t("error");
  }
  async function start() {
    $("load-state").hidden = false;
    $("spinner").hidden = false;
    $("retry").hidden = true;
    $("load-title").textContent = t("loading");
    $("load-detail").textContent = t("preparing");
    try {
      buildIdentity ||= await verifyBuild(import.meta.url);
      if (!runtimeStarted) {
        await Blazor.start();
        runtimeStarted = true;
      }
      runtime = getDotnetRuntime(0);
      const exports = await runtime.getAssemblyExports("SpaceBattle.Web.dll");
      bridge = exports.SpaceBattle.Web.BattleBridge;
      runtimeMode = bridge.Mode();
      if (
        runtimeMode !== "aot-binary" &&
        runtimeMode !== "interpreter-binary"
      ) {
        throw new Error("Unsupported simulation runtime mode: " + runtimeMode);
      }
      if (bridge.BrowserContract?.() !== browserContract) throw new Error("Mixed cached WASM/application contract. Reload the page.");
      assetState ||= await loadAtlasBundle(buildIdentity, { decodeImages: !imageLess });
      const data = assetState.bundle.atlases;
      sprites = new Map(data.ships.sprites.map(sprite => [sprite.class, sprite]));
      gunSprites = new Map(data.guns.sprites.map(sprite => [sprite.kind, sprite]));
      projectileSprites = new Map(data.effects.sprites.filter(sprite => sprite.family === "projectile").map(sprite => [sprite.kind, sprite]));
      particleSprites = new Map(data.effects.sprites.filter(sprite => sprite.family === "particle").map(sprite => [sprite.kind, sprite]));
      warpSprites = new Map(data.warp.sprites.map(sprite => [sprite.kind, sprite]));
      atlas = assetState.images.ships; gunAtlas = assetState.images.guns;
      effectsAtlas = assetState.images.effects[0]; warpAtlas = assetState.images.warp[0];
      ready = true;
      $("colliders").disabled = false;
      reset();
      if (!ready) return;
      $("load-state").hidden = true;
      setPaused(testMode || Boolean(diagnosticMode));
      resize();
    } catch (error) {
      fail(error);
    }
  }
  function frame(now) {
    if (frameStopped) return;
    try {
    frames++;
    const started = performance.now();
    const interval = last ? now - last : 0;
    if (interval > 0) {
      const measuredFps = 1000 / interval;
      fps = fps ? fps * 0.9 + measuredFps * 0.1 : measuredFps;
    }
    const elapsed = Math.min(100, interval);
    last = now;
    if (ready && !paused && !testMode && !document.hidden && !busy) {
      accumulator += elapsed * speed;
      const budgetTicks =
        tickCostMs > 0
          ? Math.max(1, Math.floor(catchUpBudgetMs / tickCostMs))
          : 1;
      const ticks = Math.min(12, budgetTicks, Math.floor(accumulator / tickMs));
      if (ticks) {
        try {
          advance(ticks);
          rateTicks += ticks;
          accumulator -= ticks * tickMs;
          discardedTicks += Math.max(0, accumulator / tickMs - 12);
          accumulator = Math.min(accumulator, tickMs * 12);
        } catch (error) {
          fail(error);
        }
      }
    }
    if (ready && !paused && !testMode && !document.hidden && interval > 100)
      discardedTicks += ((interval - 100) * speed) / tickMs;
    if (now - rateStart >= 1000) {
      ticksPerSecond = (rateTicks * 1000) / (now - rateStart);
      rateTicks = 0;
      rateStart = now;
    }
    const renderStart = performance.now();
    if (!draw()) return;
    const renderSample = performance.now() - renderStart;
    renderCpuMs = renderCpuMs
      ? renderCpuMs * 0.9 + renderSample * 0.1
      : renderSample;
    if (now - uiTime > 120) {
      updateUI();
      uiTime = now;
    }
    lastFrameMs = performance.now() - started;
    } catch (error) { fail(error, "frame"); }
    if (!frameStopped && !diagnosticMode) frameRequest = requestAnimationFrame(frame);
  }
  const pointers = new Map();
  let drag = null,
    pinch = null;
  canvas.addEventListener("pointerdown", (event) => {
    canvas.setPointerCapture(event.pointerId);
    pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    drag = {
      x: event.clientX,
      y: event.clientY,
      startX: event.clientX,
      startY: event.clientY,
      moved: false,
    };
    if (pointers.size === 2) {
      const [a, b] = [...pointers.values()];
      pinch = Math.hypot(a.x - b.x, a.y - b.y);
      drag.moved = true;
    }
  });
  canvas.addEventListener("pointermove", (event) => {
    if (!pointers.has(event.pointerId)) return;
    const previous = pointers.get(event.pointerId);
    pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pointers.size === 2) {
      const [a, b] = [...pointers.values()],
        distance = Math.hypot(a.x - b.x, a.y - b.y);
      if (pinch > 0) zoom(distance / pinch, (a.x + b.x) / 2, (a.y + b.y) / 2);
      pinch = distance;
      stopFollowing();
    } else if (drag) {
      const dx = event.clientX - previous.x,
        dy = event.clientY - previous.y;
      if (
        Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) > 5
      )
        drag.moved = true;
      if (drag.moved) {
        camera.x -= dx / (fitScale * camera.zoom);
        camera.y += dy / (fitScale * camera.zoom);
        stopFollowing();
      }
    }
  });
  function pointerEnd(event) {
    if (drag && !drag.moved && snapshot) {
      let best = null,
        distance = 22;
      for (const r of snapshot.ships) {
        const p = screen(r),
          d = Math.hypot(p.x - event.clientX, p.y - event.clientY);
        if (
          d < Math.max(distance, r[8] * fitScale * camera.zoom) &&
          (!best || d < distance)
        ) {
          best = r;
          distance = d;
        }
      }
      selected = best?.[0] ?? null;
      stopFollowing();
      updateUI();
    }
    pointers.delete(event.pointerId);
    pinch = null;
    drag = null;
  }
  canvas.addEventListener("pointerup", pointerEnd);
  canvas.addEventListener("pointercancel", () => {
    pointers.clear();
    drag = null;
    pinch = null;
  });
  canvas.addEventListener(
    "wheel",
    (event) => {
      event.preventDefault();
      zoom(Math.exp(-event.deltaY * 0.001), event.clientX, event.clientY);
    },
    { passive: false },
  );
  $("pause").onclick = () => setPaused(!paused);
  $("reset").onclick = reset;
  $("profile").onchange = reset;
  $("seed").onchange = reset;
  $("seed").oninput = () => $("seed").setCustomValidity("");
  document.querySelectorAll("[data-speed]").forEach(
    (button) =>
      (button.onclick = () => {
        speed = Number(button.dataset.speed);
        ticksPerSecond = rateTicks = 0;
        rateStart = performance.now();
        document
          .querySelectorAll("[data-speed]")
          .forEach((b) => b.classList.toggle("active", b === button));
        updateUI();
      }),
  );
  $("language").onclick = () => {
    lang = lang === "en" ? "ru" : "en";
    try {
      localStorage.setItem("spaceBattle.lang", lang);
    } catch {}
    const url = new URL(location.href);
    url.searchParams.set("lang", lang);
    history.replaceState(null, "", url);
    localize();
  };
  $("zoom-in").onclick = () => zoom(1.3);
  $("zoom-out").onclick = () => zoom(1 / 1.3);
  $("fit").onclick = () => {
    fit();
    draw();
  };
  $("deselect").onclick = () => {
    selected = null;
    stopFollowing();
    updateUI();
  };
  $("follow").onclick = () => setFollowing(!following);
  $("colliders").onclick = () => showColliders(!colliderDebug);
  $("fullscreen").onclick = async () => {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await $("battle").requestFullscreen();
    } catch (error) {
      $("status").textContent = error.message;
    }
  };
  window.addEventListener("keydown", (event) => {
    if (event.target.matches("input,select,textarea")) return;
    if (event.code === "Space") {
      event.preventDefault();
      setPaused(!paused);
    } else if (event.code === "KeyF") {
      $("fullscreen").click();
    } else if (event.code === "KeyC") {
      setFollowing(!following);
    } else if (event.code === "F2") {
      event.preventDefault();
      if (!event.repeat) showColliders(!colliderDebug);
    } else if (event.code === "Escape" && !document.fullscreenElement) {
      selected = null;
      stopFollowing();
      updateUI();
    }
  });
  $("retry").onclick = () => location.reload();
  $("statistics").onclick = () => showStatistics($("simulation-stats").hidden);
  $("stats-close").onclick = () => showStatistics(false);
  document.addEventListener("visibilitychange", () => {
    last = 0;
    accumulator = 0;
    ticksPerSecond = rateTicks = 0;
    rateStart = performance.now();
  });
  function ensureHeapViews() {
    if (runtime && header && runtime.localHeapViewF64().buffer !== heapBuffer) accept(framePointer);
  }
  async function diagnosticSample({ mode, ticks = 1, renders = 1 } = {}) {
    if (!diagnosticMode || !ready || busy || fatalError) throw new Error("Paused diagnostic page required");
    if (!["simulation", "render", "minimal", "normal"].includes(mode) ||
        !Number.isInteger(ticks) || ticks < 0 || ticks > 120 ||
        !Number.isInteger(renders) || renders < 0 || renders > 120)
      throw new Error("Diagnostic requests are bounded to 120 ticks/draws");
    if (diagnosticMode === "simulation" && mode !== "simulation") throw new Error("Simulation-only diagnostic page cannot draw");
    setPaused(true);
    busy = true;
    try {
      const stepMs = mode === "simulation" || mode === "normal" ? bridge.DiagnosticStep(ticks) : 0;
      const exportStarted = performance.now();
      // Every export reacquires views; no saved row views are used after memory growth.
      accept(bridge.Snapshot());
      const exportMs = performance.now() - exportStarted;
      let renderMs = 0;
      if (mode !== "simulation") {
        const started = performance.now();
        for (let i = 0; i < renders; i++) if (!draw(mode === "minimal" || imageLess)) throw new Error(fatalError.message);
        renderMs = performance.now() - started;
      }
      const checksum = bridge.Checksum();
      ensureHeapViews();
      updateUI();
      return { mode, stepMs, exportMs, renderMs, checksum, tick: snapshot.tick,
        counts: { ...snapshot.counts }, counters: { ...snapshot.counters }, memory: {
          wasmLinearBytes: runtime.localHeapViewF64().buffer.byteLength,
          managedBytes: snapshot.performance.managedBytes,
          jsHeapBytes: performance.memory?.usedJSHeapSize ?? null,
          canvasBackingBytes: canvas.width * canvas.height * 4,
          atlasDecodedBytes: assetState?.diagnostics.decodedBytes ?? 0 } };
    } catch (error) { fail(error, "diagnostics"); throw error; }
    finally { busy = false; }
  }
  window.addEventListener("resize", resize);
  canvas.addEventListener("contextlost", event => {
    event.preventDefault(); contextLost = true;
    fail(new Error("Canvas context lost. Reload after the browser recovers."), "context-lost");
  });
  canvas.addEventListener("contextrestored", () => { contextLost = false; });
  new ResizeObserver(resize).observe(canvas);
  window.advanceTime = async (ms) => {
    if (!ready) throw new Error("Simulation not ready");
    if (!Number.isFinite(ms) || ms < 0) throw new Error("Invalid duration");
    busy = true;
    try {
      let ticks = Math.round((ms * 60) / 1000);
      while (ticks > 0) {
        const count = Math.min(600, ticks);
        advance(count);
        ticks -= count;
        if (ticks > 0) await new Promise((resolve) => setTimeout(resolve, 0));
      }
      accumulator = 0;
      last = 0;
      ticksPerSecond = rateTicks = 0;
      rateStart = performance.now();
      if (!draw()) throw new Error(fatalError.message);
      updateUI();
      return snapshot;
    } finally {
      busy = false;
    }
  };
  function tacticCounters() {
    if (!ready) return null;
    return {
      targetSwitches: bridge.TacticCounter(0),
      targetRetentions: bridge.TacticCounter(1),
      groupOrders: bridge.TacticCounter(2),
      coverOrders: bridge.TacticCounter(3),
    };
  }
  function tactics() {
    if (!ready) return null;
    return {
      ...tacticCounters(),
      ships: snapshot.ships.map((r) => ({
        serial: r[0],
        team: r[1],
        mode: ["attack", "retreat", "docking", "patrol", "screen"][
          bridge.ShipTactic(r[0], 0)
        ],
        target: bridge.ShipTactic(r[0], 1),
        squadLeader: bridge.ShipTactic(r[0], 2),
        coverAlly: bridge.ShipTactic(r[0], 3),
        lockUntilTick: bridge.ShipTactic(r[0], 4),
      })),
    };
  }
  function reinforcements() {
    if (!ready) return null;
    const fields = ["wave", "corner", "composition", "pending", "ArrivalTick", "NextSpawnTick", "EndTick", "X", "Y", "Active"];
    return [0, 1].map((team) => Object.fromEntries(
      fields.map((name, field) => [name, bridge.Reinforcement(team, field)]),
    ));
  }
  window.render_game_to_text = () => {
    const checksum = ready ? bridge.Checksum() : undefined;
    ensureHeapViews();
    return JSON.stringify({
      ready,
      paused,
      profile: snapshot?.profile || $("profile").value,
      seed: snapshot?.seed ?? Number($("seed").value),
      tick: snapshot?.tick || 0,
      counts: snapshot?.counts || {},
      binary: snapshot?.binary,
      colliderDebug: { enabled: colliderDebug, ...colliderSummary(colliderRows) },
      renderCounts: snapshot ? Object.fromEntries(["ships", "projectiles", "debris", "particles", "turrets"].map((key) => [key, snapshot[key].length])) : {},
      counters: snapshot?.counters || {},
      pendingReinforcements: snapshot?.pendingReinforcements ?? 0,
      reinforcements: reinforcements(),
      checksum,
      runtimeMode,
      diagnostics: { mode: diagnosticMode, error: fatalError, frames, canvasResizes, contextLost,
        build: buildIdentity?.files?.["app.js"], assets: assetState?.diagnostics },
      visualAssets: { gunsAtlas: Boolean(gunAtlas), gunKinds: gunSprites ? [...gunSprites.keys()] : [],
        teamTint: { palette: teamPalette, hullTextures: atlas?.length ?? 0, gunTextures: gunAtlas?.length ?? 0 },
        effectsAtlas: Boolean(effectsAtlas), projectileKinds: projectileSprites ? [...projectileSprites.keys()] : [],
        particleKinds: particleSprites ? [...particleSprites.keys()] : [],
        warpAtlas: Boolean(warpAtlas), warpKinds: warpSprites ? [...warpSprites.keys()] : [] },
      revision: snapshot?.revision,
      fleetRevision: snapshot?.fleetRevision,
      deploymentRevision: snapshot?.deploymentRevision,
      physicsRevision: snapshot?.physicsRevision,
      damageRevision: snapshot?.damageRevision,
      tacticalRevision: snapshot?.tacticalRevision,
      fleetComposition: snapshot?.fleetComposition,
      tactics: tacticCounters(),
      statistics: statistics(),
      fps,
      lastFrameMs,
      stepMs: snapshot?.stepMs ?? 0,
      lastAdvanceMs,
      camera: { ...camera, scale: fitScale * camera.zoom, width, height, originY: centerY() },
      selected,
      following,
      observation: { main: observationMain, target: observationTarget, viewport: observationBounds,
        reason: observationReason, switches: observationSwitches, lastPosition: observedPosition },
      coordinateConvention:
        "World +X right, +Y up; screen +X right, +Y down; angle radians from world +X; sprites nose-up",
      ships: (snapshot?.ships || []).slice(0, 12).map((r) => ({
        serial: r[0],
        team: r[1],
        kind: r[2],
        angle: r[7],
        x: r[3],
        y: r[4],
        screen: screen(r),
        hull: r[9],
        shield: r[10],
        heat: r[11],
      })),
      projectiles: (snapshot?.projectiles || [])
        .slice(0, 4)
        .map((r) => ({ serial: r[0], kind: r[2], team: r[1], x: r[3], y: r[4], screen: screen(r) })),
      heavyProjectiles: (snapshot?.projectiles || []).filter((r) => r[2] === 4)
        .map((r) => ({ serial: r[0], x: r[3], y: r[4], vx: r[5], vy: r[6], radius: r[8], screen: screen(r) })),
      turrets: (snapshot?.turrets || []).map((r) => ({
        serial: r[0], team: r[1], kind: r[2], parentSerial: r[9],
        x: r[3], y: r[4], angle: r[7], radius: r[8], recoil: r[10], heat: r[11], screen: screen(r),
      })),
    });
  };
  window.spaceBattle = {
    get snapshot() {
      return snapshot;
    },
    get ready() {
      return ready;
    },
    get runtimeMode() {
      return runtimeMode;
    },
    validate: () => bridge.Validate(),
    tactics,
    reinforcements,
    aliveTeamCounts: () => snapshot?.aliveTeams.slice() ?? [0, 0],
    statistics,
    resize,
    assetDiagnostics: () => assetState ? { ...assetState.diagnostics } : null,
    diagnosticSample,
    showColliders,
    colliders: () => ({ enabled: colliderDebug, ...colliderSummary(colliderRows), rows: colliderDetails(colliderRows) }),
    refresh: () => {
      accept(bridge.Snapshot());
      updateUI();
      draw();
      return snapshot;
    },
    reset,
    pause: setPaused,
    fit: () => {
      fit();
      draw();
    },
    select: (serial) => {
      stopFollowing();
      selected = serial;
      updateUI();
      draw();
    },
    follow: (serial = selected) => {
      selected = serial;
      setFollowing(true);
    },
    camera,
  };
  icons();
  localize();
  if (query.has("diagnostics") && !diagnosticMode) { fail(new Error("Unknown paused diagnostic mode"), "diagnostics"); return; }
  if (!ctx) { fail(contextError || new Error("Canvas 2D context is unavailable"), "context"); return; }
  resize();
  if (!diagnosticMode && !frameStopped) frameRequest = requestAnimationFrame(frame);
  start();
})();
