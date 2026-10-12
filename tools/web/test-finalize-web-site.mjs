import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import { test } from "node:test";
import {
  buildFinalizedSite, finalize, moduleFile, parseArguments, projectRoot, requiredFiles,
  rewriteImports, rewriteIndex, safeFileKey, sha256, staticImports, validateSiteDirectory, versionUrl,
} from "./finalize-web-site.mjs";

const html = '<!doctype html>\r\n<base href="/secs/" />\r\n' +
  '<link rel="stylesheet" href="styles.css?theme=dark&amp;v=old" />\r\n' +
  '<script src="assets/lucide.min.js?v=old"></script>\r\n' +
  '<script autostart="false" src=\'_framework/blazor.webassembly.js\'></script>\r\n' +
  '<script type="module" src="app.js?v=old#load"></script>\r\n';

function fixtureSources() {
  return new Map(Object.entries({
    "index.html": html,
    "app.js": 'import { observe } from "./observer-camera.js?v=old";\r\n' +
      "import { draw } from './collider-debug.js?mode=debug&v=old#part';\r\n" +
      'import {\r\n loadAtlases,\r\n} from "./web-atlases.js";\r\n' +
      '// import("./dynamic.js") is deliberately untouched\r\nconsole.log("ready");\r\n',
    "observer-camera.js": 'export const observe = () => "camera";\r\n',
    "collider-debug.js": "export const draw = () => 1;\r\n",
    "web-atlases.js": "export const loadAtlases = () => 2;\r\n",
    "styles.css": "body { color: red; }\r\n",
    "_framework/blazor.boot.json": '{"resources":{}}\n',
    "assets/web-atlases.json": '{"schema":1,"revision":"png-atlases-v1"}\n',
    "assets/lucide.min.js": "window.lucide = {};\n",
    "_framework/blazor.webassembly.js": "window.Blazor = {};\n",
  }).map(([key, value]) => [key, Buffer.from(value)]));
}

test("URL versions replace duplicate/stale v while preserving other params, fragments and relative paths", () => {
  const hash = sha256("child");
  assert.equal(versionUrl("./app.js", hash), `./app.js?v=${hash}`);
  assert.equal(versionUrl("app.js?v=old&mode=debug&v=older#entry", hash), `app.js?v=${hash}&mode=debug#entry`);
  assert.equal(versionUrl("app.js?mode=debug#entry", hash), `app.js?mode=debug&v=${hash}#entry`);
  assert.throws(() => versionUrl("app.js", "ABC"));
});

test("named, default, multiline and side-effect static imports use exact child byte hashes", () => {
  const app = 'import camera from "./observer-camera.js";\n' +
    "import './collider-debug.js';\n" + 'import {\n load\n} from "./web-atlases.js?v=old";\n' +
    'const dynamic = import("./not-static.js");\nconst version = import.meta.url;\n';
  const hashes = Object.fromEntries(["observer-camera.js", "collider-debug.js", "web-atlases.js"].map(file => [file, sha256(file)]));
  assert.deepEqual(staticImports(app).map(entry => entry.file), Object.keys(hashes));
  const rewritten = rewriteImports(app, hashes);
  for (const entry of staticImports(rewritten)) assert.equal(new URL(entry.url, "https://fixture.invalid/").searchParams.get("v"), hashes[entry.file]);
  assert.ok(rewritten.includes('import("./not-static.js")'));
  assert.ok(rewritten.includes("import.meta.url"));
  assert.equal(rewriteImports(rewritten, hashes), rewritten);
  assert.throws(() => rewriteImports(app, {}), /Missing static import/);
});

test("index versions only four intended references, preserving base, attributes and other scripts", () => {
  const sources = fixtureSources(), hashes = Object.fromEntries(requiredFiles.map(file => [file, sha256(sources.get(file))]));
  const original = html + '<script src="unrelated.js?v=stay"></script>\r\n';
  const result = rewriteIndex(original, hashes);
  assert.ok(result.includes('<base href="/secs/" />'));
  assert.ok(result.includes('autostart="false"'));
  assert.ok(result.includes('<script src="unrelated.js?v=stay"></script>'));
  assert.ok(result.includes(`styles.css?theme=dark&amp;v=${hashes["styles.css"]}`));
  assert.ok(result.includes(`app.js?v=${hashes["app.js"]}#load`));
  assert.ok(result.includes(`assets/lucide.min.js?v=${hashes["assets/lucide.min.js"]}`));
  assert.ok(result.includes(`_framework/blazor.webassembly.js?v=${hashes["_framework/blazor.webassembly.js"]}`));
  assert.equal(rewriteIndex(result, hashes), result);
  assert.throws(() => rewriteIndex(html.replace('<script type="module"', '<div type="module"'), hashes), /reference/);
  assert.throws(() => rewriteIndex(html + '<script src="app.js"></script>', hashes), /exactly one/);
});

test("identity schema uses lowercase string hashes of final app and all existing imported modules", () => {
  const sources = fixtureSources(), before = new Map([...sources].map(([key, bytes]) => [key, Buffer.from(bytes)]));
  const result = buildFinalizedSite(sources);
  assert.deepEqual(sources, before);
  assert.deepEqual(Object.keys(result.identity), ["schema", "contract", "files", "runtime"]);
  assert.equal(result.identity.schema, 1); assert.equal(result.identity.contract, "png-assets-v1");
  assert.equal(result.identity.runtime, "8.0.31"); assert.equal(Object.keys(result.identity.files).length, 9);
  for (const [file, hash] of Object.entries(result.identity.files)) {
    assert.match(hash, /^[a-f0-9]{64}$/);
    assert.equal(hash, sha256(file === "app.js" ? result.appBytes : sources.get(file)));
  }
  assert.notEqual(result.identity.files["app.js"], sha256(sources.get("app.js")));
  assert.deepEqual(JSON.parse(result.identityBytes), result.identity);
  const repeated = new Map(sources); repeated.set("app.js", result.appBytes); repeated.set("index.html", result.indexBytes);
  assert.deepEqual(buildFinalizedSite(repeated), result);
  const changed = new Map(sources); changed.set("observer-camera.js", Buffer.from("changed child"));
  const newer = buildFinalizedSite(changed);
  assert.notEqual(newer.identity.files["observer-camera.js"], result.identity.files["observer-camera.js"]);
  assert.notEqual(newer.identity.files["app.js"], result.identity.files["app.js"]);
});

test("missing base files or imported modules and unsafe import traversal are rejected before writes", () => {
  for (const file of [...requiredFiles, "index.html", "observer-camera.js", "collider-debug.js", "web-atlases.js"]) {
    const sources = fixtureSources(); sources.delete(file);
    assert.throws(() => buildFinalizedSite(sources), /Missing site file/);
  }
  for (const filename of ["../escape.js", "./../escape.js", "a/../../escape.js", "a\\escape.js", "/root.js",
    "https://example.invalid/app.js", "//example.invalid/app.js", "C:/escape.js", "%2e%2e/escape.js", "a//child.js"])
    assert.throws(() => moduleFile(filename), /Unsafe/);
  assert.throws(() => safeFileKey("a/../app.js"));
  assert.throws(() => moduleFile("./styles.css"), /JS file/);
  const sources = fixtureSources(); sources.set("app.js", Buffer.from('import x from "../escape.js";'));
  assert.throws(() => buildFinalizedSite(sources), /Unsafe/);
});

test("CLI accepts exactly --site with a value", () => {
  assert.deepEqual(parseArguments(["--site", "Benchmarks.Local/tmp/build/wwwroot"]), { site: "Benchmarks.Local/tmp/build/wwwroot" });
  for (const args of [[], ["--site"], ["--site", ""], ["--site", "--site"], ["--output", "value"],
    ["--site", "a", "--site", "b"]]) assert.throws(() => parseArguments(args), /Usage/);
});

async function withFixture(run) {
  const temporary = path.join(projectRoot, "Benchmarks.Local", "tmp");
  execFileSync("git", ["check-ignore", "--quiet", "--", temporary], { cwd: projectRoot, stdio: "pipe" });
  // Validate existing ancestors before creating any missing fixture directory.
  for (const directory of [projectRoot, path.dirname(temporary), temporary]) {
    let stat;
    try { stat = await fs.lstat(directory); }
    catch (error) { if (error.code !== "ENOENT") throw error; }
    if (!stat) {
      assert.notEqual(directory, projectRoot);
      await fs.mkdir(directory);
      stat = await fs.lstat(directory);
    }
    assert.ok(stat.isDirectory() && !stat.isSymbolicLink());
    assert.equal(path.relative(directory, await fs.realpath(directory)), "");
  }
  const root = await fs.mkdtemp(path.join(temporary, "web-finalize-unit-tests-")), site = path.join(root, "wwwroot");
  try {
    const sources = fixtureSources();
    await fs.mkdir(site);
    for (const [filename, bytes] of sources) {
      await fs.mkdir(path.dirname(path.join(site, filename)), { recursive: true });
      await fs.writeFile(path.join(site, filename), bytes, { flag: "wx" });
    }
    await run({ root, site, sources });
  } finally {
    assert.ok(path.dirname(root) === temporary && path.basename(root).startsWith("web-finalize-unit-tests-"));
    await fs.rm(root, { recursive: true, force: true });
  }
}

async function snapshot(site) {
  const names = [...fixtureSources().keys(), "build-identity.json"];
  const result = new Map();
  for (const name of names) {
    try { result.set(name, await fs.readFile(path.join(site, name))); }
    catch (error) { if (error.code !== "ENOENT") throw error; }
  }
  return result;
}

test("finalize writes only published app/index/identity, preserves all child bytes and repeats without changes", async () => {
  await withFixture(async ({ site, sources }) => {
    const expected = buildFinalizedSite(sources), identity = await finalize(site);
    assert.deepEqual(identity, expected.identity);
    const first = await snapshot(site);
    assert.deepEqual(first.get("app.js"), expected.appBytes);
    assert.deepEqual(first.get("index.html"), expected.indexBytes);
    assert.deepEqual(first.get("build-identity.json"), expected.identityBytes);
    for (const [filename, bytes] of sources)
      if (!["app.js", "index.html"].includes(filename)) assert.deepEqual(first.get(filename), bytes);
    assert.deepEqual(await finalize(path.relative(projectRoot, site)), identity);
    assert.deepEqual(await snapshot(site), first);
    assert.ok(!(await fs.readdir(site)).some(name => name.startsWith(".finalize-")));
    assert.equal((await fs.stat(path.join(site, "build-identity.json"))).nlink, 1);
  });
});

test("a different existing identity prevents every write, including to app and index", async () => {
  await withFixture(async ({ site }) => {
    await fs.writeFile(path.join(site, "build-identity.json"), "historical identity\n");
    const before = await snapshot(site);
    await assert.rejects(finalize(site), /fresh site/);
    assert.deepEqual(await snapshot(site), before);
    assert.ok(!(await fs.readdir(site)).some(name => name.startsWith(".finalize-")));
  });
  await withFixture(async ({ site }) => {
    await finalize(site);
    await fs.writeFile(path.join(site, "web-atlases.js"), "changed module");
    const before = await snapshot(site);
    await assert.rejects(finalize(site), /fresh site/);
    assert.deepEqual(await snapshot(site), before);
  });
});

test("site restriction rejects outside/source/traversal paths and directory junctions or hardlinked targets", async () => {
  await withFixture(async ({ root, site }) => {
    for (const target of [undefined, "", projectRoot, root, path.join(projectRoot, "SpaceBattle.Web/wwwroot"),
      path.join(root, "../../../../wwwroot"), path.join(path.dirname(projectRoot), "wwwroot")])
      await assert.rejects(validateSiteDirectory(target));
    const linked = path.join(root, "linked");
    await fs.symlink(site, linked, process.platform === "win32" ? "junction" : "dir");
    await assert.rejects(finalize(path.join(linked, "wwwroot")), /symlink\/junction|ENOENT/);
    const backup = path.join(root, "original-app.js");
    await fs.link(path.join(site, "app.js"), backup);
    await assert.rejects(finalize(site), /linked\/non-file/);
    assert.deepEqual(await fs.readFile(backup), fixtureSources().get("app.js"));
  });
});

test("missing files fail without touching published app/index or creating identity", async () => {
  await withFixture(async ({ site, sources }) => {
    await fs.unlink(path.join(site, "web-atlases.js"));
    await assert.rejects(finalize(site), /Missing/);
    assert.deepEqual(await fs.readFile(path.join(site, "app.js")), sources.get("app.js"));
    assert.deepEqual(await fs.readFile(path.join(site, "index.html")), sources.get("index.html"));
    await assert.rejects(fs.stat(path.join(site, "build-identity.json")), { code: "ENOENT" });
  });
});

test("CLI fixture smoke reports app identity after finalization", async () => {
  await withFixture(async ({ site, sources }) => {
    const stdout = execFileSync(process.execPath, [path.join(projectRoot, "tools/web/finalize-web-site.mjs"),
      "--site", site], { cwd: projectRoot, encoding: "utf8" });
    assert.deepEqual(JSON.parse(stdout), { site, contract: "png-assets-v1", runtime: "8.0.31", files: 9,
      appSha256: buildFinalizedSite(sources).identity.files["app.js"] });
  });
});
