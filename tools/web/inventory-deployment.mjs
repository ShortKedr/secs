import fs from "node:fs/promises";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const arg = (name, fallback) => { const i = process.argv.indexOf(name); return i < 0 ? fallback : process.argv[i + 1]; };
const base = new URL(arg("--url", "https://shortkedr.github.io/secs/"));
const output = path.resolve(arg("--output", "Benchmarks.Local/tmp/ios-web-fix-20261012/deployed"));
if (base.protocol !== "https:" || base.hostname !== "shortkedr.github.io" || base.pathname !== "/secs/" ||
    !output.startsWith(path.join(root, "Benchmarks.Local/tmp") + path.sep)) throw new Error("Expected the specified public site and project-local evidence");
await fs.mkdir(output, { recursive: false });
const records = [];
async function read(relative, expected = null) {
  const response = await fetch(new URL(relative, base), { cache: "no-store" });
  if (!response.ok) throw new Error(`GET ${relative}: ${response.status}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  const sha256 = crypto.createHash("sha256").update(bytes).digest("hex");
  const integrity = "sha256-" + Buffer.from(sha256, "hex").toString("base64");
  if (expected && integrity !== expected) throw new Error(`Boot integrity mismatch: ${relative}`);
  const target = path.resolve(output, relative);
  if (!target.startsWith(output + path.sep)) throw new Error("Unsafe asset path");
  await fs.mkdir(path.dirname(target), { recursive: true });
  await fs.writeFile(target, bytes, { flag: "wx" });
  records.push({ relative, url: response.url, bytes: bytes.length, sha256, bootIntegrityVerified: Boolean(expected),
    headers: Object.fromEntries(["etag", "last-modified", "cache-control", "age", "content-type"].map(key => [key, response.headers.get(key)])) });
  return bytes;
}
try {
  for (const file of ["index.html", "app.js", "styles.css", "observer-camera.js", "collider-debug.js", "_framework/blazor.webassembly.js"])
    await read(file);
  for (const family of ["ships", "guns", "effects", "warp"])
    for (const suffix of ["json", "png"]) await read(`assets/${family}-atlas.${suffix}`);
  const boot = JSON.parse((await read("_framework/blazor.boot.json")).toString("utf8"));
  for (const [kind, resources] of Object.entries(boot.resources)) {
    if (!resources || typeof resources !== "object" || Array.isArray(resources)) continue;
    for (const [file, integrity] of Object.entries(resources)) {
      if (typeof integrity === "string" && integrity.startsWith("sha256-") && !records.some(item => item.relative === "_framework/" + file))
        await read("_framework/" + file, integrity);
    }
  }
  await fs.writeFile(path.join(output, "inventory.json"), JSON.stringify({ capturedUtc: new Date().toISOString(),
    scope: "Read-only fresh HTTP GETs; hashes verify a captured deployment, not what a past phone session loaded.",
    bootMainAssembly: boot.mainAssemblyName, bootResourcesHash: boot.resources.hash, records }, null, 2), { flag: "wx" });
  console.log(`Verified ${records.length} published resources; all fetched boot hashes match`);
} catch (error) {
  await fs.writeFile(path.join(output, "failure.json"), JSON.stringify({ error: String(error), records }, null, 2), { flag: "wx" });
  throw error;
}
