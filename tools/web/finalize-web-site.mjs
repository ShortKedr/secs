import assert from "node:assert/strict";
import { createHash, randomUUID } from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
export const contract = "png-assets-v1";
export const runtime = "8.0.31";
export const requiredFiles = Object.freeze([
  "app.js", "styles.css", "_framework/blazor.boot.json", "assets/web-atlases.json",
  "assets/lucide.min.js", "_framework/blazor.webassembly.js",
]);
const indexReferences = Object.freeze({
  "app.js": "src", "styles.css": "href", "assets/lucide.min.js": "src",
  "_framework/blazor.webassembly.js": "src",
});

export function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

export function safeFileKey(filename) {
  assert.ok(typeof filename === "string" && filename.split("/").every(part =>
    /^[a-zA-Z0-9_-][a-zA-Z0-9_.-]*$/.test(part) && part !== "." && part !== ".."),
  `Unsafe site file path: ${filename}`);
  return filename;
}

function urlPath(url) {
  return url.split(/[?#]/, 1)[0];
}

export function moduleFile(url) {
  const filename = safeFileKey(urlPath(url).replace(/^\.\//, ""));
  assert.ok(filename.endsWith(".js"), `Static import must reference a local JS file: ${url}`);
  return filename;
}

export function versionUrl(url, hash) {
  assert.match(hash, /^[a-f0-9]{64}$/, "Expected lowercase SHA-256");
  const fragmentIndex = url.indexOf("#");
  const fragment = fragmentIndex < 0 ? "" : url.slice(fragmentIndex);
  const address = fragmentIndex < 0 ? url : url.slice(0, fragmentIndex);
  const queryIndex = address.indexOf("?");
  const pathname = queryIndex < 0 ? address : address.slice(0, queryIndex);
  const params = new URLSearchParams(queryIndex < 0 ? "" : address.slice(queryIndex + 1));
  params.set("v", hash);
  return `${pathname}?${params}${fragment}`;
}

// Target the generated app's top-level static imports, not arbitrary JavaScript syntax.
function staticImportPattern() {
  return /^([\t ]*import\s+(?:[^;"']*?\s+from\s*)?)(["'])([^"'\r\n]+)\2/gm;
}

export function staticImports(app) {
  return [...app.matchAll(staticImportPattern())].map(match => ({ url: match[3], file: moduleFile(match[3]) }));
}

export function rewriteImports(app, hashes) {
  return app.replace(staticImportPattern(), (match, prefix, quote, url) => {
    const file = moduleFile(url);
    assert.ok(Object.hasOwn(hashes, file), `Missing static import file: ${file}`);
    return `${prefix}${quote}${versionUrl(url, hashes[file])}${quote}`;
  });
}

export function rewriteIndex(html, hashes) {
  const counts = Object.fromEntries(Object.keys(indexReferences).map(file => [file, 0]));
  const result = html.replace(/<(script|link)\b[^>]*>/gi, (tag, name) =>
    tag.replace(/(\s+(src|href)\s*=\s*)(["'])([^"']*)\3/gi, (attribute, prefix, kind, quote, value) => {
      const url = value.replace(/&amp;/gi, "&");
      const file = urlPath(url).replace(/^\.\//, "");
      if (!Object.hasOwn(indexReferences, file)) return attribute;
      assert.equal(kind.toLowerCase(), indexReferences[file], `Wrong index attribute for ${file}`);
      assert.equal(name.toLowerCase(), kind.toLowerCase() === "src" ? "script" : "link", `Wrong index tag for ${file}`);
      assert.ok(Object.hasOwn(hashes, file), `Missing index file: ${file}`);
      counts[file]++;
      const versioned = versionUrl(url, hashes[file]).replaceAll("&", "&amp;");
      return `${prefix}${quote}${versioned}${quote}`;
    }));
  for (const [file, count] of Object.entries(counts)) assert.equal(count, 1, `Index needs exactly one reference to ${file}`);
  return result;
}

function utf8(bytes, filename) {
  const text = bytes.toString("utf8");
  assert.ok(Buffer.from(text).equals(bytes), `Invalid UTF-8 file: ${filename}`);
  return text;
}

function sourceBytes(sources, filename) {
  safeFileKey(filename);
  const bytes = sources.get(filename);
  assert.ok(Buffer.isBuffer(bytes), `Missing site file: ${filename}`);
  return bytes;
}

// Pure finalization: hashes describe the exact versioned app bytes, never the source app.
export function buildFinalizedSite(sources) {
  assert.ok(sources instanceof Map, "Expected a Map of site file buffers");
  const app = utf8(sourceBytes(sources, "app.js"), "app.js");
  const modules = [...new Set(staticImports(app).map(entry => entry.file))].sort();
  assert.ok(!modules.includes("app.js"), "App must not import itself");
  const files = {};
  for (const filename of [...new Set([...requiredFiles, ...modules])].sort())
    if (filename !== "app.js") files[filename] = sha256(sourceBytes(sources, filename));
  const appBytes = Buffer.from(rewriteImports(app, files));
  files["app.js"] = sha256(appBytes);
  const orderedFiles = Object.fromEntries(Object.keys(files).sort().map(filename => [filename, files[filename]]));
  const indexBytes = Buffer.from(rewriteIndex(utf8(sourceBytes(sources, "index.html"), "index.html"), files));
  const identity = { schema: 1, contract, files: orderedFiles, runtime };
  const identityBytes = Buffer.from(JSON.stringify(identity, null, 2) + "\n");
  return { identity, identityBytes, appBytes, indexBytes };
}

export function inside(parent, child) {
  const relative = path.relative(parent, child);
  return relative !== "" && relative !== ".." && !relative.startsWith(".." + path.sep) && !path.isAbsolute(relative);
}

async function statIfPresent(target) {
  try { return await fs.lstat(target); }
  catch (error) { if (error.code === "ENOENT") return null; throw error; }
}

async function checkDirectory(directory) {
  const stat = await fs.lstat(directory);
  assert.ok(stat.isDirectory() && !stat.isSymbolicLink(), `No symlink/junction site directories: ${directory}`);
  assert.equal(path.relative(directory, await fs.realpath(directory)), "", `Site realpath escaped: ${directory}`);
}

function checkIgnored(target, directory = false) {
  execFileSync("git", ["check-ignore", "--quiet", "--", target + (directory ? path.sep : "")], { cwd: projectRoot, stdio: "pipe" });
}

export async function validateSiteDirectory(siteDirectory) {
  assert.ok(typeof siteDirectory === "string" && siteDirectory.trim(), "--site is required");
  const site = path.resolve(projectRoot, siteDirectory);
  assert.ok(inside(path.join(projectRoot, "Benchmarks.Local", "tmp"), site) && path.basename(site) === "wwwroot",
    "Site must be an existing wwwroot inside this project's ignored Benchmarks.Local/tmp");
  await checkDirectory(projectRoot);
  let directory = projectRoot;
  for (const component of path.relative(projectRoot, site).split(path.sep)) {
    assert.ok(component && !component.includes(":"), "Invalid site path component");
    directory = path.join(directory, component);
    await checkDirectory(directory);
  }
  checkIgnored(site, true);
  return site;
}

async function checkSiteFile(site, filename, optional = false) {
  safeFileKey(filename);
  await validateSiteDirectory(site);
  let directory = site;
  for (const component of filename.split("/").slice(0, -1)) {
    directory = path.join(directory, component);
    await checkDirectory(directory);
  }
  const target = path.join(site, filename);
  const stat = await statIfPresent(target);
  assert.ok(!stat || stat.isFile() && !stat.isSymbolicLink() && stat.nlink === 1,
    `Missing or linked/non-file site file: ${filename}`);
  checkIgnored(target);
  if (!stat && optional) return null;
  assert.ok(stat && stat.isFile() && !stat.isSymbolicLink() && stat.nlink === 1,
    `Missing or linked/non-file site file: ${filename}`);
  return target;
}

async function readSiteFile(site, filename, optional = false) {
  const target = await checkSiteFile(site, filename, optional);
  return target === null ? null : fs.readFile(target);
}

function checkIdentity(existing, expected) {
  assert.ok(existing === null || existing.equals(expected),
    "Different existing build-identity.json; use a fresh site directory (nothing was overwritten)");
}

export async function finalize(siteDirectory) {
  const site = await validateSiteDirectory(siteDirectory), sources = new Map();
  for (const filename of ["index.html", ...requiredFiles]) sources.set(filename, await readSiteFile(site, filename));
  for (const { file } of staticImports(utf8(sources.get("app.js"), "app.js")))
    if (!sources.has(file)) sources.set(file, await readSiteFile(site, file));
  const result = buildFinalizedSite(sources);
  const existingIdentity = await readSiteFile(site, "build-identity.json", true);
  checkIdentity(existingIdentity, result.identityBytes);
  const outputs = new Map([["app.js", result.appBytes], ["index.html", result.indexBytes]]);
  if (existingIdentity === null) outputs.set("build-identity.json", result.identityBytes);
  const staged = new Map();
  try {
    for (const [filename, bytes] of outputs) {
      if (sources.get(filename)?.equals(bytes)) continue;
      await validateSiteDirectory(site);
      const temporary = path.join(site, `.finalize-${randomUUID()}.tmp`);
      checkIgnored(temporary);
      await fs.writeFile(temporary, bytes, { flag: "wx" });
      staged.set(filename, temporary);
    }
    // Recheck the snapshot and identity before replacing published files; identity commits last.
    for (const [filename, bytes] of sources)
      assert.ok((await readSiteFile(site, filename)).equals(bytes), `Site changed during finalization: ${filename}`);
    checkIdentity(await readSiteFile(site, "build-identity.json", true), result.identityBytes);
    for (const [filename, temporary] of staged) {
      await validateSiteDirectory(site);
      const target = await checkSiteFile(site, filename, filename === "build-identity.json");
      if (filename === "build-identity.json") {
        if (target !== null) {
          checkIdentity(await fs.readFile(target), result.identityBytes);
          continue;
        }
        // Atomic no-clobber creation, unlike rename-overwrite, for the identity marker.
        await fs.link(temporary, path.join(site, filename));
      } else await fs.rename(temporary, target);
    }
  } finally {
    await validateSiteDirectory(site);
    for (const temporary of staged.values()) {
      assert.ok(inside(site, temporary) && path.dirname(temporary) === site);
      await fs.unlink(temporary).catch(error => { if (error.code !== "ENOENT") throw error; });
    }
  }
  return result.identity;
}

export function parseArguments(args) {
  assert.ok(args.length === 2 && args[0] === "--site" && typeof args[1] === "string" &&
    args[1].trim() && !args[1].startsWith("--"), "Usage: node tools/web/finalize-web-site.mjs --site <published-wwwroot>");
  return { site: args[1] };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const { site } = parseArguments(process.argv.slice(2));
    const identity = await finalize(site);
    console.log(JSON.stringify({ site: path.resolve(projectRoot, site), contract: identity.contract,
      runtime: identity.runtime, files: Object.keys(identity.files).length, appSha256: identity.files["app.js"] }));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
