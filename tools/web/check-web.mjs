import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import { chromium } from "playwright";
import { PNG } from "pngjs";

const argument = (name, fallback) => {
  const index = process.argv.indexOf(name);
  return index < 0 ? fallback : process.argv[index + 1];
};
const site = path.resolve(
  argument("--site", "Benchmarks.Local/tmp/web-publish/wwwroot"),
);
const base = argument("--base", "/");
const remote = argument("--url", null);
const evidence = path.resolve(
  argument("--output", "Benchmarks.Local/tmp/web-checks"),
);
await fs.mkdir(evidence, { recursive: true });
const errors = [],
  results = [];
const mime = {
  ".html": "text/html",
  ".js": "text/javascript",
  ".css": "text/css",
  ".json": "application/json",
  ".wasm": "application/wasm",
  ".png": "image/png",
  ".txt": "text/plain",
  ".md": "text/plain",
};
let server, browser;
try {
  if (!remote) {
    server = http.createServer(async (request, response) => {
      try {
        const pathname = decodeURIComponent(
          new URL(request.url, "http://local").pathname,
        );
        if (!pathname.startsWith(base)) {
          response.writeHead(404).end();
          return;
        }
        const relative = pathname.slice(base.length) || "index.html";
        const file = path.resolve(site, relative);
        if (!file.startsWith(site + path.sep)) {
          response.writeHead(403).end();
          return;
        }
        const bytes = await fs.readFile(file);
        response
          .writeHead(200, {
            "Content-Type":
              mime[path.extname(file)] || "application/octet-stream",
          })
          .end(bytes);
      } catch {
        response.writeHead(404).end();
      }
    });
    await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  }
  const url = remote || `http://127.0.0.1:${server.address().port}${base}`;
  browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
  });
  function monitor(page) {
    page.on("pageerror", (error) => errors.push(String(error)));
    page.on("console", (message) => {
      if (message.type() === "error") errors.push(message.text());
    });
    page.on("response", (response) => {
      if (response.status() >= 400)
        errors.push(`${response.status()} ${response.url()}`);
    });
  }
  const page = await context.newPage();
  monitor(page);
  await page.goto(`${url}?test=1`, {
    waitUntil: "networkidle",
    timeout: 120000,
  });
  await page.waitForFunction(() => window.spaceBattle?.ready, null, {
    timeout: 120000,
  });
  const state = () =>
    page.evaluate(() => JSON.parse(window.render_game_to_text()));
  let initial = await state();
  const requiredMode = argument("--require-mode", null);
  if (requiredMode)
    assert.equal(
      initial.runtimeMode,
      requiredMode,
      "Expected production AOT/binary build",
    );
  assert.equal(initial.seed, 7331);
  assert.equal(initial.revision, "tactical-lock-v2");
  assert.equal(initial.tick, 0);
  assert.equal(initial.counts.ships, 198);
  assert.equal(initial.paused, true);
  assert.equal(initial.statistics.entities, 198);
  assert.equal(initial.statistics.samples, 0);
  assert.equal(initial.statistics.meanMs, 0);
  assert.equal(initial.statistics.ticksPerSecond, 0);
  assert.ok(
    initial.statistics.nativeBytes > 0 && initial.statistics.managedBytes > 0,
  );
  await page.click("#statistics");
  assert.equal(await page.locator("#simulation-stats").isVisible(), true);
  assert.equal(
    await page.locator("#statistics").getAttribute("aria-expanded"),
    "true",
  );
  assert.equal(await page.locator("#metric-total").textContent(), "198");
  await page.click("#stats-close");
  assert.equal(await page.locator("#simulation-stats").isVisible(), false);
  assert.equal(
    (await state()).checksum,
    initial.checksum,
    "Statistics toggle does not alter simulation",
  );
  assert.equal(await page.locator("#load-state").isVisible(), false);
  assert.ok(
    (await page.locator("button svg").count()) >= 8,
    "Lucide icons loaded",
  );

  async function capture(name) {
    await page.screenshot({ path: path.join(evidence, `${name}.png`) });
    const canvas = PNG.sync.read(await page.locator("canvas").screenshot());
    let bright = 0;
    for (let i = 0; i < canvas.data.length; i += 4)
      if (Math.max(...canvas.data.subarray(i, i + 3)) > 110) bright++;
    assert.ok(
      bright > 200,
      `Nonblank scene: ${name} (${bright} visible pixels)`,
    );
    const snapshot = await state();
    await fs.writeFile(
      path.join(evidence, `${name}.json`),
      JSON.stringify(snapshot, null, 2),
    );
    return snapshot;
  }
  await capture("desktop-initial");
  const before = await page.locator("canvas").screenshot();
  await page.evaluate(() => window.advanceTime(10000));
  const nominal = await state();
  assert.equal(nominal.tick, 600);
  const metric = nominal.statistics;
  assert.equal(metric.samples, 600);
  assert.equal(
    metric.entities,
    Object.values(nominal.counts).reduce((sum, value) => sum + value, 0),
  );
  assert.equal(metric.stages.length, 9);
  assert.ok(
    metric.meanMs > 0 &&
      metric.p95Ms > 0 &&
      metric.p99Ms >= metric.p95Ms &&
      metric.maxMs >= metric.p99Ms,
  );
  assert.ok(metric.stages.every((ms) => Number.isFinite(ms) && ms >= 0));
  assert.ok(
    metric.physicsBodies > 0 && metric.physicsFixtures >= metric.physicsBodies,
  );
  assert.equal(
    metric.ticksPerSecond,
    0,
    "Offline advance does not count as realtime throughput",
  );
  assert.ok(Math.abs(metric.budgetMs - 1000 / 60) < 1e-8);
  assert.ok(
    Math.abs(
      metric.budgetUsedPercent - (metric.meanMs / metric.budgetMs) * 100,
    ) < 1e-8,
  );
  await page.click("#statistics");
  assert.equal(await page.locator("#stage-metrics dd").count(), 9);
  assert.match(
    await page.locator("#metric-samples").textContent(),
    /600 \/ 600/,
  );
  await page.screenshot({ path: path.join(evidence, "statistics-en.png") });
  await page.click("#stats-close");
  assert.ok(
    nominal.tactics.targetRetentions > 0 && nominal.tactics.groupOrders > 0,
    "Target leases and group focus run in WASM",
  );
  const tactical = await page.evaluate(() => window.spaceBattle.tactics());
  assert.ok(tactical.coverOrders > 0, "Wounded-ship screening runs in WASM");
  await fs.writeFile(
    path.join(evidence, "tactics.json"),
    JSON.stringify(tactical, null, 2),
  );
  const squads = new Map();
  for (const ship of tactical.ships) {
    if (ship.squadLeader < 0) continue;
    if (!squads.has(ship.squadLeader)) squads.set(ship.squadLeader, []);
    squads.get(ship.squadLeader).push(ship);
  }
  assert.ok(squads.size > 0, "Live small-ship squads");
  for (const squad of squads.values()) {
    assert.ok(squad.length <= 4, "Bounded live squads");
    assert.equal(
      new Set(squad.map((ship) => ship.team)).size,
      1,
      "Friendly squads only",
    );
    assert.equal(
      new Set(squad.map((ship) => ship.target)).size,
      1,
      "Shared squad focus",
    );
  }
  assert.ok(
    nominal.counters.shots > 0 && nominal.counters.hits > 0,
    "Live weapons and collisions",
  );
  assert.ok(
    nominal.counters.impreciseAimDecisions > 0,
    "Deterministic aim spread runs in WASM",
  );
  assert.ok(
    nominal.counters.friendlyFireHits > 0,
    "Neutral projectiles hit friendly ships",
  );
  assert.notDeepEqual(
    before,
    await page.locator("canvas").screenshot(),
    "Moving scene",
  );
  assert.equal(
    await page.evaluate(() => window.spaceBattle.validate()),
    nominal.checksum,
  );
  await capture("desktop-battle");
  const debrisChecks = await page.evaluate(async () => {
    const battle = window.spaceBattle,
      snapshot = battle.snapshot,
      camera = { ...battle.camera },
      groups = ["ships", "projectiles", "debris", "particles"],
      saved = groups.map((key) => snapshot[key].slice());
    const debris = saved[2].reduce(
      (largest, row) => (!largest || row[8] > largest[8] ? row : largest),
      null,
    );
    if (!debris) throw new Error("Expected actual combat debris");
    const checks = [];
    try {
      // Isolate one real render row without touching WASM simulation memory.
      for (const key of groups) snapshot[key].length = 0;
      snapshot.debris.push(debris);
      battle.camera.x = debris[3];
      battle.camera.y = debris[4];
      for (const zoom of [0.35, 1, 4, 12, 20]) {
        battle.camera.zoom = zoom;
        await new Promise((resolve) =>
          requestAnimationFrame(() => requestAnimationFrame(resolve)),
        );
        const canvas = document.querySelector("canvas"),
          state = JSON.parse(window.render_game_to_text()),
          dpr = canvas.width / state.camera.width,
          x = Math.floor((state.camera.width / 2) * dpr),
          y = Math.floor((state.camera.height / 2 + 15) * dpr),
          pixel = Array.from(
            canvas.getContext("2d").getImageData(x, y, 1, 1).data,
          );
        checks.push({ zoom, pixel });
      }
    } finally {
      for (let i = 0; i < groups.length; i++) {
        const rows = snapshot[groups[i]];
        rows.length = saved[i].length;
        for (let j = 0; j < rows.length; j++) rows[j] = saved[i][j];
      }
      Object.assign(battle.camera, camera);
    }
    return checks;
  });
  for (const { zoom, pixel } of debrisChecks) {
    const rgb = pixel.slice(0, 3);
    assert.ok(Math.min(...rgb) >= 50, `Visible debris at zoom ${zoom}`);
    assert.ok(Math.max(...rgb) <= 96, `Muted debris at zoom ${zoom}`);
    assert.ok(
      Math.max(...rgb) - Math.min(...rgb) <= 2,
      `Neutral gray debris at zoom ${zoom}`,
    );
  }
  assert.equal((await state()).checksum, nominal.checksum);
  results.push({ check: "muted-debris-five-zooms", samples: debrisChecks });
  await page.click("#reset");
  assert.equal((await state()).checksum, initial.checksum);
  await page.evaluate(async () => {
    await window.advanceTime(5000);
    await window.advanceTime(5000);
  });
  assert.equal(
    (await state()).checksum,
    nominal.checksum,
    "Same-browser reset and split-step parity",
  );
  results.push({
    check: "nominal-600-ticks",
    checksum: nominal.checksum,
    counters: nominal.counters,
  });
  for (const profile of ["low", "high"]) {
    await page.selectOption("#profile", profile);
    assert.equal((await state()).tick, 0);
    await page.evaluate(() => window.advanceTime(10000));
    const current = await state();
    assert.equal(current.profile, profile);
    assert.equal(current.tick, 600);
    assert.notEqual(current.checksum, nominal.checksum);
    assert.equal(
      await page.evaluate(() => window.spaceBattle.validate()),
      current.checksum,
    );
    results.push({
      check: `${profile}-600-ticks`,
      checksum: current.checksum,
      counters: current.counters,
    });
  }
  await page.selectOption("#profile", "nominal");
  await page.fill("#seed", "12345");
  await page.locator("#seed").press("Tab");
  assert.equal((await state()).seed, 12345);
  assert.notEqual((await state()).checksum, initial.checksum);
  await page.fill("#seed", "7331");
  await page.locator("#seed").press("Tab");

  const ship = (await state()).ships[0];
  await page.mouse.click(ship.screen.x, ship.screen.y);
  assert.equal((await state()).selected, ship.serial);
  assert.equal(await page.locator("#telemetry").isVisible(), true);
  assert.equal(
    Number(await page.locator("#hull").getAttribute("value")),
    Math.min(100, ship.hull * 100),
    "Snapshot hull is a normalized fraction",
  );
  await capture("selected-ship");
  await page.click("#follow");
  assert.equal((await state()).following, true);
  await page.evaluate(() => window.advanceTime(500));
  const tracked = await state();
  assert.ok(Math.abs(tracked.camera.x - tracked.ships[0].x) < 0.001);
  await page.click("#deselect");
  assert.equal((await state()).selected, null);
  const zoom = (await state()).camera.zoom;
  await page.click("#zoom-in");
  assert.ok((await state()).camera.zoom > zoom);
  await page.click("#zoom-out");
  await page.mouse.move(1000, 450);
  await page.mouse.wheel(0, -200);
  await page.waitForTimeout(100);
  assert.ok((await state()).camera.zoom > zoom);
  await page.mouse.move(1000, 450);
  await page.mouse.down();
  await page.mouse.move(1090, 490, { steps: 4 });
  await page.mouse.up();
  assert.notEqual((await state()).camera.x, tracked.camera.x);
  await page.click("#fit");
  assert.deepEqual(
    [
      (await state()).camera.x,
      (await state()).camera.y,
      (await state()).camera.zoom,
    ],
    [0, 0, 1],
  );
  await page.click('[data-speed="4"]');
  assert.ok(
    Math.abs((await state()).statistics.budgetMs - 1000 / 240) < 1e-8,
    "Speed changes the requested tick budget",
  );
  assert.ok(
    await page
      .locator('[data-speed="4"]')
      .evaluate((button) => button.classList.contains("active")),
  );
  await page.click("#language");
  assert.equal(await page.locator("html").getAttribute("lang"), "ru");
  await page.click("#statistics");
  assert.equal(await page.locator("#stats-title").textContent(), "Статистика");
  await page.screenshot({ path: path.join(evidence, "statistics-ru.png") });
  await page.click("#stats-close");
  assert.match(
    await page.locator("#report").getAttribute("href"),
    /REPORT\.ru\.md$/,
  );
  await page.keyboard.press("f");
  await page.waitForFunction(() => Boolean(document.fullscreenElement));
  await page.keyboard.press("f");
  await page.waitForFunction(() => !document.fullscreenElement);

  for (const [name, width, height] of [
    ["desktop-wide", 1920, 1080],
    ["mobile", 390, 844],
    ["mobile-small", 320, 568],
    ["mobile-landscape", 844, 390],
  ]) {
    await page.setViewportSize({ width, height });
    await page.waitForTimeout(100);
    const dimensions = await page.evaluate(() => ({
      width: innerWidth,
      scroll: document.documentElement.scrollWidth,
      canvas: document.querySelector("canvas").getBoundingClientRect().toJSON(),
      bad: [
        ...document.querySelectorAll(
          ".toolbar button,.toolbar input,.toolbar select",
        ),
      ]
        .filter((e) => {
          const b = e.getBoundingClientRect();
          return b.left < -1 || b.right > innerWidth + 1;
        })
        .map((e) => e.id),
    }));
    assert.equal(dimensions.scroll, width, `No horizontal overflow: ${name}`);
    assert.deepEqual(dimensions.bad, [], `All tools fit: ${name}`);
    assert.equal(Math.round(dimensions.canvas.width), width);
    await capture(name);
    await page.click("#statistics");
    const panelLayout = await page.evaluate(() => {
      const panel = document
        .getElementById("simulation-stats")
        .getBoundingClientRect();
      const overlaps = (a, b) =>
        a.left < b.right &&
        a.right > b.left &&
        a.top < b.bottom &&
        a.bottom > b.top;
      return {
        overflow:
          panel.left < 0 ||
          panel.right > innerWidth ||
          panel.top < 0 ||
          panel.bottom > innerHeight,
        collisions: [
          ".toolbar",
          ".scoreboard",
          ".camera-tools",
          "footer",
        ].filter((selector) =>
          overlaps(
            panel,
            document.querySelector(selector).getBoundingClientRect(),
          ),
        ),
        textOverflow: [
          ...document.querySelectorAll("#simulation-stats dd"),
        ].some((el) => el.scrollWidth > el.clientWidth + 1),
      };
    });
    assert.equal(
      panelLayout.overflow,
      false,
      `Statistics fit viewport: ${name}`,
    );
    assert.deepEqual(
      panelLayout.collisions,
      [],
      `Statistics do not overlap controls: ${name}`,
    );
    assert.equal(
      panelLayout.textOverflow,
      false,
      `Statistics values fit: ${name}`,
    );
    await page.screenshot({
      path: path.join(evidence, `statistics-${name}.png`),
    });
    await page.click("#stats-close");
  }
  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    hasTouch: true,
    isMobile: true,
  });
  const touchPage = await mobileContext.newPage();
  monitor(touchPage);
  await touchPage.goto(`${url}?test=1&lang=ru`, {
    waitUntil: "networkidle",
    timeout: 120000,
  });
  await touchPage.waitForFunction(() => window.spaceBattle?.ready);
  const touchShip = await touchPage.evaluate(
    () => JSON.parse(window.render_game_to_text()).ships[0],
  );
  await touchPage.touchscreen.tap(touchShip.screen.x, touchShip.screen.y);
  assert.equal(
    await touchPage.evaluate(
      () => JSON.parse(window.render_game_to_text()).selected,
    ),
    touchShip.serial,
  );
  const cdp = await mobileContext.newCDPSession(touchPage);
  await cdp.send("Input.dispatchTouchEvent", {
    type: "touchStart",
    touchPoints: [
      { x: 160, y: 500, id: 1 },
      { x: 230, y: 500, id: 2 },
    ],
  });
  await cdp.send("Input.dispatchTouchEvent", {
    type: "touchMove",
    touchPoints: [
      { x: 130, y: 500, id: 1 },
      { x: 260, y: 500, id: 2 },
    ],
  });
  await cdp.send("Input.dispatchTouchEvent", {
    type: "touchEnd",
    touchPoints: [],
  });
  assert.ok(
    await touchPage.evaluate(() => window.spaceBattle.camera.zoom > 1),
    "Two-finger zoom",
  );
  await touchPage.screenshot({ path: path.join(evidence, "touch.png") });
  await mobileContext.close();

  // A failed asset request must show an error and recover through Retry.
  const recovery = await context.newPage();
  await recovery.route("**/assets/ships-atlas.json", (route) =>
    route.fulfill({ status: 503, body: "Unavailable" }),
  );
  await recovery.goto(`${url}?test=1`, {
    waitUntil: "networkidle",
    timeout: 120000,
  });
  await recovery.waitForFunction(
    () => !document.getElementById("retry").hidden,
  );
  assert.equal(await recovery.evaluate(() => window.spaceBattle.ready), false);
  await recovery.unroute("**/assets/ships-atlas.json");
  monitor(recovery);
  await recovery.click("#retry");
  await recovery.waitForFunction(() => window.spaceBattle.ready);
  assert.equal(
    await recovery.evaluate(() => window.spaceBattle.snapshot.tick),
    0,
  );
  await recovery.close();
  results.push({ check: "touch-and-error-recovery", passed: true });

  // Test actual RAF stepping separately from the deterministic harness.
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(url, { waitUntil: "networkidle", timeout: 120000 });
  await page.waitForFunction(() => window.spaceBattle?.ready);
  await page.waitForTimeout(1400);
  const running = await state();
  assert.ok(running.tick > 0);
  assert.ok(
    running.statistics.ticksPerSecond > 0 &&
      running.statistics.fps > 0 &&
      running.statistics.renderCpuMs > 0,
    "Live tick/render rates are distinct positive measurements",
  );
  await page.click("#pause");
  const stopped = (await state()).tick;
  await page.waitForTimeout(250);
  assert.equal((await state()).tick, stopped);
  assert.equal(
    (await state()).statistics.ticksPerSecond,
    0,
    "Paused simulation rate is zero",
  );
  await page.keyboard.press("Space");
  await page.waitForTimeout(250);
  assert.ok((await state()).tick > stopped);
  await capture("realtime");
  await page.click("#statistics");
  await page.screenshot({
    path: path.join(evidence, "statistics-realtime.png"),
  });
  await page.click("#reset");
  await page.click("#pause");
  await page.evaluate(() => window.spaceBattle.reset());
  const resetStats = (await state()).statistics;
  assert.equal(resetStats.samples, 0);
  assert.equal(resetStats.meanMs, 0);
  assert.equal(resetStats.p99Ms, 0);
  assert.equal(resetStats.maxMs, 0);
  assert.equal(resetStats.discardedTicks, 0);
  assert.ok(resetStats.stages.every((value) => value === 0));
  assert.deepEqual(errors, [], "No browser errors or missing assets");
  results.push({ check: "controls-responsive-realtime", passed: true });
  await fs.writeFile(
    path.join(evidence, "results.json"),
    JSON.stringify({ url, results, errors }, null, 2),
  );
  console.log(
    `PASS: WASM simulation, three profiles, reset parity, combat, controls, responsive canvas and realtime pause/resume (${url})`,
  );
} catch (error) {
  await fs.writeFile(
    path.join(evidence, "failure.json"),
    JSON.stringify(
      { error: String(error), stack: error.stack, errors, results },
      null,
      2,
    ),
  );
  throw error;
} finally {
  await browser?.close();
  if (server) await new Promise((resolve) => server.close(resolve));
}
