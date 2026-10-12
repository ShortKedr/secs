import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import { test } from "node:test";
import {
  buildBundle, decodePng, encodePng, families, palette, parseArguments, prepare,
  projectRoot, sha256, sharedAssets, tintLut, transformRgba, validateManifest, validateOutputDirectory,
} from "./prepare-web-atlases.mjs";

function syntheticSources() {
  const sources = {};
  for (const family of families) {
    const width = 8, height = 8;
    const crop = { x: 1, y: 1, width: 2, height: 2, pivotX: 1, pivotY: 1 };
    const sprites = family === "ships" ? Array.from({ length: 12 }, (_, kind) => ({ ...crop, class: kind })) :
      family === "guns" ? Array.from({ length: 6 }, (_, kind) => ({ ...crop, kind: kind - 1 })) :
      family === "effects" ? [
        ...Array.from({ length: 5 }, (_, kind) => ({ ...crop, family: "projectile", kind })),
        ...Array.from({ length: 4 }, (_, kind) => ({ ...crop, family: "particle", kind })),
      ] : Array.from({ length: 4 }, (_, kind) => ({ family: "warp", kind,
        x: (kind % 2) * 4, y: Math.floor(kind / 2) * 4, width: 3, height: 3, pivotX: 1.5, pivotY: 1.5 }));
    const data = Buffer.alloc(width * height * 4);
    const alphas = [0, 1, 31, 32, 33, 127, 254, 255];
    for (let i = 0; i < data.length; i += 4) {
      data[i] = (i * 17 + 5) % 256; data[i + 1] = (i * 11 + 10) % 256;
      data[i + 2] = (i * 7 + 30) % 256; data[i + 3] = alphas[(i / 4) % alphas.length];
    }
    sources[family] = { json: Buffer.from(JSON.stringify({ width, height, alphaThreshold: 32, sprites })),
      png: encodePng({ width, height, data }) };
  }
  return sources;
}

function sourceManifest(family) {
  return JSON.parse(syntheticSources()[family].json.toString());
}

test("exact palette and LUT match all runtime Uint8ClampedArray assignments", () => {
  assert.deepEqual(palette, [[0.2, 0.85, 0.95], [1, 0.37, 0.4]]);
  for (const rgb of palette) {
    const lut = tintLut(rgb);
    for (let channel = 0; channel < 3; channel++) for (let value = 0; value < 256; value++) {
      const reference = new Uint8ClampedArray([value]);
      reference[0] *= rgb[channel];
      assert.equal(lut[channel][value], reference[0]);
    }
  }
  const ties = tintLut([0.5, 0.5, 0.5])[0];
  assert.equal(ties[1], 0); assert.equal(ties[3], 2); assert.equal(ties[5], 2); assert.equal(ties[7], 4);
  assert.throws(() => tintLut([1, NaN, 0]), /tint/);
});

test("alpha <= 32 clears only alpha; ships preserve all alpha including 1..32", () => {
  const source = Buffer.from([5, 10, 30, 0, 5, 10, 30, 1, 5, 10, 30, 31,
    5, 10, 30, 32, 5, 10, 30, 33, 5, 10, 30, 255]);
  const before = Buffer.from(source);
  for (const threshold of [-1, 32]) for (const rgb of [null, ...palette]) {
    const actual = transformRgba(source, rgb, threshold), reference = new Uint8ClampedArray(source);
    for (let i = 0; i < reference.length; i += 4) {
      if (rgb) for (let channel = 0; channel < 3; channel++) reference[i + channel] *= rgb[channel];
      if (reference[i + 3] <= threshold) reference[i + 3] = 0;
    }
    assert.deepEqual(actual, Buffer.from(reference));
  }
  assert.deepEqual(source, before);
  assert.throws(() => transformRgba(Buffer.alloc(3)), /RGBA/);
  assert.throws(() => transformRgba(source, null, 32.5), /threshold/);
});

test("six RGBA PNGs, metadata, hashes and manifest bytes repeat exactly without source mutation", () => {
  const sources = syntheticSources(), before = structuredClone(sources);
  const first = buildBundle(sources), second = buildBundle(sources);
  assert.deepEqual(first.manifestBytes, second.manifestBytes);
  assert.deepEqual(first.files, second.files);
  assert.deepEqual(sources, Object.fromEntries(families.map(family => [family, {
    json: Buffer.from(before[family].json), png: Buffer.from(before[family].png),
  }])));
  assert.equal(first.files.size, 6);
  assert.equal(first.manifest.schema, 1); assert.equal(first.manifest.revision, "png-atlases-v1");
  assert.equal(first.manifest.logicalDecodedBytes, 6 * 8 * 8 * 4);
  assert.deepEqual(JSON.parse(first.manifestBytes), first.manifest);
  for (const family of families) {
    const atlas = first.manifest.atlases[family], metadata = JSON.parse(sources[family].json);
    const { textures, ...originalMetadata } = atlas;
    assert.deepEqual(originalMetadata, metadata);
    assert.equal(textures.length, ["ships", "guns"].includes(family) ? 2 : 1);
    assert.equal(first.manifest.sourceHashes[`${family}-atlas.json`], sha256(sources[family].json));
    assert.equal(first.manifest.sourceHashes[`${family}-atlas.png`], sha256(sources[family].png));
    const original = decodePng(sources[family].png);
    for (const [index, texture] of textures.entries()) {
      const bytes = first.files.get(texture.file), image = decodePng(bytes);
      assert.match(texture.file, /^[a-z]+-(?:team-[01]|clean)-[a-f0-9]{20}\.png$/);
      assert.ok(texture.file.includes(texture.sha256.slice(0, 20)));
      assert.equal(texture.sha256, sha256(bytes));
      assert.equal(bytes[24], 8); assert.equal(bytes[25], 6);
      assert.equal(image.width, atlas.width); assert.equal(image.height, atlas.height);
      assert.deepEqual(image.data, transformRgba(original.data, textures.length === 2 ? palette[index] : null,
        family === "ships" ? -1 : 32));
    }
  }
  const changed = syntheticSources(), image = decodePng(changed.ships.png);
  image.data[0] = 255; changed.ships.png = encodePng(image);
  assert.notEqual(buildBundle(changed).manifest.atlases.ships.textures[0].file,
    first.manifest.atlases.ships.textures[0].file);
});

test("integer dimensions, crops, all kinds, pivots, square particles and warp quadrants are validated", () => {
  for (const family of families) {
    const original = sourceManifest(family);
    assert.equal(validateManifest(family, original), original);
    const invalid = mutate => {
      const manifest = structuredClone(original); mutate(manifest);
      assert.throws(() => validateManifest(family, manifest));
    };
    invalid(m => { m.width = 8.5; }); invalid(m => { m.height = 0; });
    invalid(m => { m.alphaThreshold = 31; }); invalid(m => { m.sprites.pop(); });
    invalid(m => { m.sprites[0].x = -1; }); invalid(m => { m.sprites[0].y = 0.5; });
    invalid(m => { m.sprites[0].width = 0; }); invalid(m => { m.sprites[0].height = 100; });
    invalid(m => { m.sprites[0].pivotX = NaN; }); invalid(m => { m.sprites[0].pivotY = -1; });
    invalid(m => { m.sprites[0][family === "ships" ? "class" : "kind"] = 99; });
    invalid(m => { m.sprites[1] = { ...m.sprites[0] }; });
  }
  const effects = sourceManifest("effects");
  for (const mutate of [m => { m.sprites[5].width = 3; }, m => { m.sprites[5].pivotX = 0.5; },
    m => { m.sprites[0].family = "ship"; }, m => { m.sprites[0].pivotX = 2; }]) {
    const manifest = structuredClone(effects); mutate(manifest);
    assert.throws(() => validateManifest("effects", manifest));
  }
  const guns = sourceManifest("guns"); guns.sprites[0].pivotX = 2;
  validateManifest("guns", guns); // Runtime permits gun pivots on the crop edge.
  const warp = sourceManifest("warp"); warp.sprites[1].x = 1;
  assert.throws(() => validateManifest("warp", warp), /quadrant/);
});

test("PNG CRC corruption, truncation, trailing bytes and dimensions mismatches fail", () => {
  const sources = syntheticSources(), png = sources.ships.png;
  const corrupt = Buffer.from(png); corrupt[29] ^= 1;
  for (const bytes of [Buffer.from("not png"), corrupt, png.subarray(0, png.length - 1),
    Buffer.concat([png, Buffer.from([0])])]) assert.throws(() => decodePng(bytes));
  assert.throws(() => decodePng(png, { width: 9, height: 8 }), /width/);
  assert.throws(() => decodePng(png, { width: 8, height: 9 }), /height/);
  assert.throws(() => encodePng({ width: 8, height: 8, data: Buffer.alloc(4) }), /length/);
  sources.guns.json = Buffer.from("{broken"); assert.throws(() => buildBundle(sources));
});

test("CLI requires output, supports only input/output and rejects missing or duplicate values", () => {
  assert.deepEqual(parseArguments(["--output", "Benchmarks.Local/tmp/build"]),
    { input: sharedAssets, output: "Benchmarks.Local/tmp/build" });
  assert.deepEqual(parseArguments(["--input", "source", "--output", "target"]), { input: "source", output: "target" });
  for (const args of [[], ["--output"], ["--output", ""], ["--output", "--input"],
    ["--wat", "value"], ["--output", "a", "--output", "b"]]) assert.throws(() => parseArguments(args));
});

async function withFixture(run) {
  const temporary = path.join(projectRoot, "Benchmarks.Local", "tmp");
  // Validate the ancestors before creating the isolated synthetic fixture.
  await validateOutputDirectory(sharedAssets, path.join(temporary, "web-atlas-unit-tests"));
  await fs.mkdir(temporary, { recursive: true });
  const root = await fs.mkdtemp(path.join(temporary, "web-atlas-unit-tests-"));
  const input = path.join(root, "input"), output = path.join(root, "output");
  try {
    await fs.mkdir(input);
    const sources = syntheticSources();
    for (const family of families) for (const extension of ["json", "png"])
      await fs.writeFile(path.join(input, `${family}-atlas.${extension}`), sources[family][extension], { flag: "wx" });
    await run({ root, input, output, sources });
  } finally {
    assert.ok(path.dirname(root) === temporary && path.basename(root).startsWith("web-atlas-unit-tests-"));
    await fs.rm(root, { recursive: true, force: true });
  }
}

test("prepare accepts published originals, reuses identical output, preserves source bytes and rejects collisions", async () => {
  await withFixture(async ({ input, output, sources }) => {
    await fs.mkdir(output);
    await fs.writeFile(path.join(output, "ships-atlas.png"), sources.ships.png);
    await fs.writeFile(path.join(output, "historical-result.json"), "untouched");
    const first = await prepare(input, output);
    const names = (await fs.readdir(output)).sort(), bytes = new Map();
    for (const name of names) bytes.set(name, await fs.readFile(path.join(output, name)));
    assert.deepEqual(await prepare(input, output), first);
    assert.deepEqual((await fs.readdir(output)).sort(), names);
    for (const name of names) assert.deepEqual(await fs.readFile(path.join(output, name)), bytes.get(name));
    for (const family of families) for (const extension of ["json", "png"])
      assert.deepEqual(await fs.readFile(path.join(input, `${family}-atlas.${extension}`)), sources[family][extension]);
    const file = first.atlases.ships.textures[0].file;
    await fs.writeFile(path.join(output, file), "corrupt output");
    await assert.rejects(prepare(input, output), /overwrite different/);
    assert.equal(await fs.readFile(path.join(output, file), "utf8"), "corrupt output");
    await fs.writeFile(path.join(output, file), bytes.get(file));
    await fs.writeFile(path.join(output, "web-atlases.json"), "historical manifest");
    await assert.rejects(prepare(input, output), /fresh output/);
    assert.equal(await fs.readFile(path.join(output, "web-atlases.json"), "utf8"), "historical manifest");
  });
});

test("safe output rejects project/source roots, traversal and linked directories/files", async () => {
  await withFixture(async ({ root, input, output }) => {
    for (const target of [undefined, "", projectRoot, sharedAssets, path.dirname(root), input,
      path.join(input, "nested"), root, path.join(root, "../../../../escaped")])
      await assert.rejects(validateOutputDirectory(input, target));
    const relative = path.relative(projectRoot, output);
    assert.equal((await validateOutputDirectory(input, relative)).output, output);
    const link = path.join(root, "junction");
    await fs.symlink(input, link, process.platform === "win32" ? "junction" : "dir");
    await assert.rejects(prepare(input, path.join(link, "nested")), /symlink\/junction/);
    const manifest = await prepare(input, output);
    const file = path.join(output, manifest.atlases.guns.textures[0].file);
    await fs.unlink(file);
    await fs.link(path.join(input, "guns-atlas.png"), file);
    await assert.rejects(prepare(input, output), /linked\/non-file/);
    assert.equal((await fs.stat(path.join(input, "guns-atlas.png"))).nlink, 2);
  });
});

test("corrupt inputs and output conflicts fail before generating any output files", async () => {
  await withFixture(async ({ input, output, sources }) => {
    const corrupt = Buffer.from(sources.warp.png); corrupt[29] ^= 1;
    await fs.writeFile(path.join(input, "warp-atlas.png"), corrupt);
    await assert.rejects(prepare(input, output));
    await assert.rejects(fs.stat(output), { code: "ENOENT" });
    await fs.writeFile(path.join(input, "warp-atlas.png"), sources.warp.png);
    await fs.mkdir(output);
    await fs.writeFile(path.join(output, "web-atlases.json"), "do not replace");
    await assert.rejects(prepare(input, output), /overwrite different/);
    assert.deepEqual(await fs.readdir(output), ["web-atlases.json"]);
  });
});

test("CLI smoke prepares only synthetic assets and reports six textures", async () => {
  await withFixture(async ({ input, output }) => {
    const stdout = execFileSync(process.execPath, [path.join(projectRoot, "tools/web/prepare-web-atlases.mjs"),
      "--input", input, "--output", output], { cwd: projectRoot, encoding: "utf8" });
    assert.deepEqual(JSON.parse(stdout), { output, revision: "png-atlases-v1", pngCount: 6,
      logicalDecodedBytes: 6 * 8 * 8 * 4 });
  });
});
