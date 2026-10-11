import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import { chromium } from "playwright";
import { summarizeProfile, requireInterpreterFree } from "./coreclr-profile.mjs";

const [siteArg, outputArg] = process.argv.slice(2);
if (!siteArg || !outputArg) throw new Error("Usage: probe-coreclr-r2r.mjs <wwwroot> <evidence>");
const site = path.resolve(siteArg), output = path.resolve(outputArg);
await fs.mkdir(output, { recursive: true });
const errors = [], messages = [];
const mime = { ".js": "text/javascript", ".wasm": "application/wasm", ".html": "text/html", ".png": "image/png", ".json": "application/json", ".css": "text/css" };
const server = http.createServer(async (request, response) => {
  try {
    const name = decodeURIComponent(new URL(request.url, "http://local").pathname).slice(1) || "index.html";
    const file = path.resolve(site, name);
    if (!file.startsWith(site + path.sep)) throw new Error("Unsafe path");
    response.writeHead(200, { "Content-Type": mime[path.extname(file)] ?? "text/plain" }).end(await fs.readFile(file));
  } catch { response.writeHead(404).end(); }
});
await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  page.on("pageerror", (error) => errors.push(String(error)));
  page.on("console", (message) => { messages.push(`${message.type()}: ${message.text()}`); if (message.type() === "error") errors.push(message.text()); });
  await page.goto(`http://127.0.0.1:${server.address().port}/?test=1&lang=ru`, { timeout: 120000 });
  await page.waitForFunction(() => window.spaceBattle?.ready || !document.getElementById("retry").hidden, null, { timeout: 120000 });
  const ready = await page.evaluate(() => !!window.spaceBattle?.ready);
  if (!ready) throw new Error("CoreCLR failed to start");
  const session = await page.context().newCDPSession(page);
  await page.evaluate(async () => {
    window.spaceBattle.pause(true);
    window.r2rProbeBridge = (await getDotnetRuntime(0).getAssemblyExports("SpaceBattle.Web.dll")).SpaceBattle.Web.BattleBridge;
    window.r2rProbeBridge.Reset("nominal", 7331);
  });
  await session.send("Profiler.enable");
  await session.send("Profiler.setSamplingInterval", { interval: 100 });
  await session.send("Profiler.start");
  const advanceMs = await page.evaluate(() => {
    const started = performance.now();
    window.r2rProbeBridge.Advance(120);
    return performance.now() - started;
  });
  const { profile } = await session.send("Profiler.stop");
  const state = await page.evaluate(() => {
    window.spaceBattle.refresh();
    return { mode: window.r2rProbeBridge.Mode(), checksum: window.r2rProbeBridge.Checksum(), state: JSON.parse(window.render_game_to_text()) };
  });
  await fs.writeFile(path.join(output, "profile.cpuprofile"), JSON.stringify(profile));
  const inventory = JSON.parse(await fs.readFile(path.join(site, "r2r-inventory.json"), "utf8"));
  const symbols = await fs.readFile(path.join(site, "r2r-native-symbols.txt"), "utf8").catch(() => "");
  const summary = summarizeProfile(profile, inventory.assemblies, symbols);
  await fs.writeFile(path.join(output, "result.json"), JSON.stringify({ ...state, advanceMs, ...summary, errors, messages }, null, 2));
  await page.screenshot({ path: path.join(output, "battle.png") });
  console.log(JSON.stringify({ advanceMs, mode: state.mode, checksum: state.checksum, errors, interpreterLeafPercent: summary.interpreterLeafPercent, top: summary.functions.slice(0, 8) }, null, 2));
  if (errors.length) throw new Error(errors.join("\n"));
  if (process.argv.includes("--require-no-interpreter")) requireInterpreterFree(summary);
} catch (error) {
  await fs.writeFile(path.join(output, "failure.json"), JSON.stringify({ error: String(error), errors, messages }, null, 2));
  throw error;
} finally {
  await browser?.close();
  await new Promise((resolve) => server.close(resolve));
}
