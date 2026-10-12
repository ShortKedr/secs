import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { fileURLToPath } from "node:url";
import { run, parseArgs } from "./ios-diagnostics.mjs";

const args = process.argv.slice(2), index = args.indexOf("--site");
if (index < 0 || !args[index + 1]) throw new Error("--site is required");
const site = path.resolve(args.splice(index, 2)[1]);
const faultIndex = args.indexOf("--raf-faults"), rafFaults = faultIndex >= 0;
if (rafFaults) args.splice(faultIndex, 1);
if (!site.startsWith(path.resolve("Benchmarks.Local/tmp") + path.sep)) throw new Error("Local generated site only");
const html = await fs.readFile(path.join(site, "index.html"), "utf8");
const base = /<base href="(\/[^"?#]*)"\s*\/?>/.exec(html)?.[1];
if (!base || !base.endsWith("/")) throw new Error("Expected absolute site base directory");
const mime = { ".js": "text/javascript", ".json": "application/json", ".html": "text/html", ".css": "text/css",
  ".wasm": "application/wasm", ".png": "image/png", ".txt": "text/plain" };
const server = http.createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, "http://local").pathname);
    if (!pathname.startsWith(base)) throw new Error("Wrong base path");
    const file = path.resolve(site, pathname.slice(base.length) || "index.html");
    if (!file.startsWith(site + path.sep)) throw new Error("Unsafe path");
    response.writeHead(200, { "Content-Type": mime[path.extname(file)] ?? "application/octet-stream" }).end(await fs.readFile(file));
  } catch { response.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
try {
  const report = await run(parseArgs(["--url", `http://127.0.0.1:${server.address().port}${base}`, ...args]));
  console.log(JSON.stringify({ status: report.status, output: report.options.output, error: report.error ?? null }));
  if (report.status !== "passed") process.exitCode = 1;
  else if (rafFaults) {
    const helper = fileURLToPath(new URL("./check-render-faults.mjs", import.meta.url));
    const result = await promisify(execFile)(process.execPath, [helper, "--url", `http://127.0.0.1:${server.address().port}${base}`,
      "--browser", report.options.browser, "--output", report.options.output + ".raf.json"], { windowsHide: true, timeout: 120000 });
    console.log(result.stdout.trim());
  }
} finally { await new Promise(resolve => server.close(resolve)); }
