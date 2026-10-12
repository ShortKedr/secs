import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { PNG } from "pngjs";

export const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
export const sharedAssets = path.join(projectRoot, "SpaceBattle.Game", "Assets");
export const palette = Object.freeze([
  Object.freeze([0.2, 0.85, 0.95]), Object.freeze([1, 0.37, 0.4]),
]);
export const families = Object.freeze(["ships", "guns", "effects", "warp"]);
export const revision = "png-atlases-v1";

export function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

// Typed-array assignment implements the runtime's ToUint8Clamp, including ties-to-even.
export function tintLut(rgb) {
  assert.ok(Array.isArray(rgb) && rgb.length === 3 &&
    rgb.every(value => Number.isFinite(value) && value >= 0 && value <= 1), "Invalid RGB tint");
  return rgb.map(factor => Uint8ClampedArray.from({ length: 256 }, (_, value) => value * factor));
}

export function transformRgba(data, rgb = null, alphaThreshold = -1) {
  assert.ok(data instanceof Uint8Array && data.length % 4 === 0, "Expected RGBA bytes");
  assert.ok(Number.isInteger(alphaThreshold) && alphaThreshold >= -1 && alphaThreshold <= 255,
    "Invalid alpha threshold");
  const result = Buffer.from(data), lut = rgb === null ? null : tintLut(rgb);
  for (let i = 0; i < result.length; i += 4) {
    if (lut) for (let channel = 0; channel < 3; channel++) result[i + channel] = lut[channel][data[i + channel]];
    if (data[i + 3] <= alphaThreshold) result[i + 3] = 0;
  }
  return result;
}

function dimensions(width, height) {
  assert.ok(Number.isSafeInteger(width) && Number.isSafeInteger(height) && width > 0 && height > 0 &&
    width <= 0x7fffffff && height <= 0x7fffffff && Number.isSafeInteger(width * height * 4),
  "Invalid atlas dimensions");
}

export function validateManifest(family, manifest) {
  assert.ok(families.includes(family), "Unknown atlas family");
  assert.ok(manifest && typeof manifest === "object" && !Array.isArray(manifest), "Invalid source manifest");
  dimensions(manifest.width, manifest.height);
  assert.equal(manifest.alphaThreshold, 32, `${family}: expected source alpha threshold 32`);
  assert.ok(Array.isArray(manifest.sprites), `${family}: missing sprites`);
  const seen = new Set();
  if (family === "warp") assert.ok(manifest.width === manifest.height && manifest.width % 2 === 0,
    "Warp atlas needs a square 2x2 grid");
  for (const sprite of manifest.sprites) {
    assert.ok(sprite && typeof sprite === "object" && !Array.isArray(sprite), "Invalid sprite");
    const { x, y, width, height, pivotX, pivotY } = sprite;
    assert.ok([x, y, width, height].every(Number.isSafeInteger) && x >= 0 && y >= 0 &&
      width > 0 && height > 0 && x + width <= manifest.width && y + height <= manifest.height,
    `${family}: invalid sprite crop`);
    const kind = family === "ships" ? sprite.class : sprite.kind;
    const category = family === "effects" ? sprite.family : family;
    assert.ok(family !== "effects" || ["projectile", "particle"].includes(category), "Invalid effects family");
    assert.ok(family !== "warp" || !Object.hasOwn(sprite, "family") || sprite.family === "warp", "Invalid warp family");
    const minimum = family === "guns" ? -1 : 0;
    const maximum = family === "ships" ? 11 : family === "guns" || category === "projectile" ? 4 : 3;
    const key = `${category}:${kind}`;
    assert.ok(Number.isSafeInteger(kind) && kind >= minimum && kind <= maximum && !seen.has(key),
      `${family}: invalid or duplicate sprite kind`);
    seen.add(key);
    if (family !== "ships" || Object.hasOwn(sprite, "pivotX") || Object.hasOwn(sprite, "pivotY")) {
      assert.ok(Number.isFinite(pivotX) && Number.isFinite(pivotY) && pivotX >= 0 && pivotY >= 0 &&
        (family === "guns" ? pivotX <= width && pivotY <= height : pivotX < width && pivotY < height),
      `${family}: invalid sprite pivot`);
    }
    if (category === "particle" || family === "warp") assert.ok(width === height &&
      pivotX === width / 2 && pivotY === height / 2, `${family}: square cells and centered pivots required`);
    if (family === "warp") {
      const cell = manifest.width / 2, left = (kind % 2) * cell, top = Math.floor(kind / 2) * cell;
      assert.ok(x >= left && y >= top && x + width <= left + cell && y + height <= top + cell,
        "Warp sprite crop must stay in its own quadrant");
    }
  }
  assert.equal(seen.size, { ships: 12, guns: 6, effects: 9, warp: 4 }[family],
    `${family}: incomplete sprite kinds`);
  return manifest;
}

export function decodePng(bytes, expected) {
  assert.ok(Buffer.isBuffer(bytes) && bytes.length >= 33 &&
    bytes.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])) &&
    bytes.readUInt32BE(8) === 13 && bytes.toString("ascii", 12, 16) === "IHDR", "Invalid PNG header");
  const width = bytes.readUInt32BE(16), height = bytes.readUInt32BE(20);
  dimensions(width, height);
  if (expected) {
    assert.equal(width, expected.width, "PNG width does not match manifest");
    assert.equal(height, expected.height, "PNG height does not match manifest");
  }
  const image = PNG.sync.read(bytes, { checkCRC: true, skipRescale: false });
  assert.equal(image.data.length, width * height * 4, "Decoded PNG must be RGBA");
  return { width, height, data: image.data };
}

export function encodePng({ width, height, data }) {
  dimensions(width, height);
  assert.ok(data instanceof Uint8Array && data.length === width * height * 4, "Invalid RGBA image length");
  return PNG.sync.write({ width, height, data: Buffer.from(data) }, {
    bitDepth: 8, colorType: 6, inputColorType: 6, inputHasAlpha: true,
    filterType: 4, deflateLevel: 9, deflateStrategy: 3, deflateChunkSize: 32768,
  });
}

// Pure preparation: source bytes in, deterministic manifest and six PNG buffers out.
export function buildBundle(sources) {
  const atlases = {}, sourceHashes = {}, files = new Map();
  let logicalDecodedBytes = 0;
  for (const family of families) {
    const source = sources[family];
    assert.ok(source && Buffer.isBuffer(source.json) && Buffer.isBuffer(source.png), `${family}: missing source bytes`);
    const metadata = validateManifest(family, JSON.parse(source.json.toString("utf8")));
    const image = decodePng(source.png, metadata);
    sourceHashes[`${family}-atlas.json`] = sha256(source.json);
    sourceHashes[`${family}-atlas.png`] = sha256(source.png);
    const tints = family === "ships" || family === "guns" ? palette : [null];
    const textures = tints.map((rgb, index) => {
      // Ships deliberately ignore the manifest's 32: the existing runtime defaults to -1.
      const data = transformRgba(image.data, rgb, family === "ships" ? -1 : 32);
      const bytes = encodePng({ ...image, data }), hash = sha256(bytes);
      const variant = rgb === null ? "clean" : `team-${index}`;
      const file = `${family}-${variant}-${hash.slice(0, 20)}.png`;
      files.set(file, bytes);
      logicalDecodedBytes += image.width * image.height * 4;
      return { file, sha256: hash, width: image.width, height: image.height };
    });
    atlases[family] = { ...metadata, textures };
  }
  assert.ok(Number.isSafeInteger(logicalDecodedBytes), "Decoded byte total overflow");
  const manifest = { schema: 1, revision, palette, atlases, logicalDecodedBytes, sourceHashes };
  return { manifest, files, manifestBytes: Buffer.from(JSON.stringify(manifest, null, 2) + "\n") };
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
  assert.ok(stat.isDirectory() && !stat.isSymbolicLink(), `No symlink/junction output directories: ${directory}`);
  assert.equal(path.relative(directory, await fs.realpath(directory)), "", `Output realpath escaped: ${directory}`);
}

function checkIgnored(target) {
  execFileSync("git", ["check-ignore", "--quiet", "--", target], { cwd: projectRoot, stdio: "pipe" });
}

export async function validateOutputDirectory(inputDirectory, outputDirectory) {
  assert.ok(typeof outputDirectory === "string" && outputDirectory.trim(), "--output is required");
  const output = path.resolve(projectRoot, outputDirectory), input = await fs.realpath(inputDirectory);
  assert.ok(inside(path.join(projectRoot, "Benchmarks.Local", "tmp"), output),
    "Output must be inside this project's ignored Benchmarks.Local/tmp (not its root)");
  assert.ok(output !== input && !inside(input, output) && !inside(output, input), "Output must not overlap source assets");
  checkIgnored(output);
  await checkDirectory(projectRoot);
  let directory = projectRoot;
  const directories = [];
  for (const component of path.relative(projectRoot, output).split(path.sep)) {
    assert.ok(component && !component.includes(":"), "Invalid output path component");
    directory = path.join(directory, component);
    directories.push(directory);
    if (await statIfPresent(directory)) await checkDirectory(directory);
  }
  return { output, directories };
}

async function checkOutputFile(output, filename, bytes) {
  assert.ok(/^[a-z0-9][a-z0-9.-]*$/.test(filename) && inside(output, path.join(output, filename)), "Unsafe output filename");
  const target = path.join(output, filename);
  checkIgnored(target);
  const stat = await statIfPresent(target);
  if (!stat) return false;
  assert.ok(stat.isFile() && !stat.isSymbolicLink() && stat.nlink === 1,
    `Refusing linked/non-file output: ${target}`);
  assert.ok((await fs.readFile(target)).equals(bytes), `Refusing to overwrite different existing bytes: ${target}; use a fresh output directory`);
  return true;
}

export async function prepare(inputDirectory = sharedAssets, outputDirectory) {
  const input = path.resolve(projectRoot, inputDirectory);
  const { output, directories } = await validateOutputDirectory(input, outputDirectory);
  const sources = {};
  for (const family of families) sources[family] = {
    json: await fs.readFile(path.join(input, `${family}-atlas.json`)),
    png: await fs.readFile(path.join(input, `${family}-atlas.png`)),
  };
  const bundle = buildBundle(sources);
  const outputs = new Map([...bundle.files, ["web-atlases.json", bundle.manifestBytes]]);
  // Check every collision before writing anything, so corrupt/historical bundles stay untouched.
  for (const [filename, bytes] of outputs) await checkOutputFile(output, filename, bytes);
  for (const directory of directories) {
    await checkDirectory(path.dirname(directory));
    if (!(await statIfPresent(directory))) await fs.mkdir(directory);
    await checkDirectory(directory);
  }
  for (const [filename, bytes] of outputs) {
    for (const directory of directories) await checkDirectory(directory);
    if (await checkOutputFile(output, filename, bytes)) continue;
    try { await fs.writeFile(path.join(output, filename), bytes, { flag: "wx" }); }
    catch (error) {
      if (error.code !== "EEXIST") throw error;
      await checkOutputFile(output, filename, bytes);
    }
  }
  return bundle.manifest;
}

export function parseArguments(args) {
  const values = { input: sharedAssets, output: undefined }, seen = new Set();
  for (let i = 0; i < args.length; i += 2) {
    const name = args[i];
    assert.ok(["--input", "--output"].includes(name) && !seen.has(name), `Unknown or duplicate option: ${name}`);
    const value = args[i + 1];
    assert.ok(typeof value === "string" && value.trim() && !value.startsWith("--"), `Missing value for ${name}`);
    seen.add(name);
    values[name.slice(2)] = value;
  }
  assert.ok(values.output, "--output is required");
  return values;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const { input, output } = parseArguments(process.argv.slice(2));
    const manifest = await prepare(input, output);
    console.log(JSON.stringify({ output: path.resolve(projectRoot, output), revision: manifest.revision,
      pngCount: Object.values(manifest.atlases).reduce((count, atlas) => count + atlas.textures.length, 0),
      logicalDecodedBytes: manifest.logicalDecodedBytes }));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
