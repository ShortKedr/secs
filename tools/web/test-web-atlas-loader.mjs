import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs/promises";
import { digest, verifiedJson, verifyBuild, validateBundle, loadAtlasBundle } from "../../SpaceBattle.Web/wwwroot/web-atlases.js";

async function fixture() {
  const atlases = {};
  const sha = "ab".repeat(32);
  let total = 0;
  for (const family of ["ships", "guns", "effects", "warp"]) {
    const data = JSON.parse(await fs.readFile(new URL(`../../SpaceBattle.Game/Assets/${family}-atlas.json`, import.meta.url), "utf8"));
    data.textures = Array.from({ length: ["ships", "guns"].includes(family) ? 2 : 1 }, (_, index) =>
      ({ file: `${family}-${index}-${sha.slice(0, 20)}.png`, sha256: sha, width: data.width, height: data.height }));
    total += data.width * data.height * 4 * data.textures.length;
    atlases[family] = data;
  }
  return { schema: 1, revision: "png-atlases-v1", palette: [[0.2, 0.85, 0.95], [1, 0.37, 0.4]],
    atlases, logicalDecodedBytes: total };
}

test("bundle keeps all crops, pivots, palette and strict resource identities", async () => {
  const bundle = await fixture(); assert.equal(validateBundle(bundle), bundle);
  for (const mutate of [b => b.palette[0][0] = 0, b => b.atlases.ships.textures[0].file = "../escape.png",
    b => b.logicalDecodedBytes++, b => b.atlases.guns.sprites[0].pivotX = -1,
    b => b.atlases.effects.sprites[0].width = 9999, b => b.atlases.warp.sprites.pop()]) {
    const invalid = structuredClone(bundle); mutate(invalid); assert.throws(() => validateBundle(invalid));
  }
});

test("hash verification rejects stale JSON before consumption", async () => {
  const bytes = new TextEncoder().encode('{"safe":true}'); const hash = await digest(bytes);
  const fetcher = async () => new Response(bytes);
  assert.deepEqual(await verifiedJson("test", hash, fetcher), { safe: true });
  await assert.rejects(verifiedJson("test", "00".repeat(32), fetcher), /Mixed cached resource/);
  await assert.rejects(verifiedJson("test", hash, async () => new Response("", { status: 503 })), /unavailable/);
});

test("mixed app/boot deployment is rejected before Mono initialization", async () => {
  const boot = new TextEncoder().encode('{"resources":{}}'), bootHash = await digest(boot);
  const files = { "app.js": "ab".repeat(32), "_framework/blazor.boot.json": bootHash, "assets/web-atlases.json": "cd".repeat(32) };
  const identity = { schema: 1, contract: "png-assets-v1", files };
  let calls = 0;
  const fetcher = async url => { calls++; return new Response(url === "build-identity.json" ? JSON.stringify(identity) : boot); };
  await assert.rejects(verifyBuild("https://local/app.js?v=old", fetcher), /Mixed cached application/);
  assert.equal(calls, 1);
  assert.deepEqual(await verifyBuild("https://local/app.js?v=" + files["app.js"], fetcher), identity);
  await assert.rejects(verifyBuild("https://local/app.js?v=" + files["app.js"],
    async url => new Response(url === "build-identity.json" ? JSON.stringify(identity) : "{}")), /Mixed cached resource/);
});

test("loads only six direct images sequentially, never original PNGs or Canvas", async () => {
  const bundle = await fixture(), bytes = new TextEncoder().encode(JSON.stringify(bundle)), hash = await digest(bytes);
  const allocated = []; let decoding = 0;
  class FakeImage {
    constructor() { allocated.push(this); }
    async decode() {
      assert.equal(decoding++, 0); await Promise.resolve(); decoding--;
      const atlas = Object.values(bundle.atlases).find(a => a.textures.some(t => "assets/" + t.file === this.src));
      assert.ok(atlas); this.naturalWidth = atlas.width; this.naturalHeight = atlas.height; this.complete = true;
    }
  }
  const build = { files: { "assets/web-atlases.json": hash } };
  const loaded = await loadAtlasBundle(build, { fetcher: async () => new Response(bytes), ImageType: FakeImage });
  assert.equal(allocated.length, 6); assert.equal(loaded.diagnostics.canvasCount, 0);
  assert.equal(loaded.diagnostics.decodedBytes, bundle.logicalDecodedBytes);
  assert.ok(allocated.every(image => !image.src.includes("-atlas.png")));
  const isolated = await loadAtlasBundle(build, { decodeImages: false, fetcher: async () => new Response(bytes), ImageType: FakeImage });
  assert.equal(allocated.length, 6); assert.equal(isolated.diagnostics.imageCount, 0);
  assert.equal(isolated.diagnostics.decodedBytes, 0);
});

test("partial decode failure releases every temporary image source", async () => {
  const bytes = new TextEncoder().encode(JSON.stringify(await fixture())), hash = await digest(bytes), images = [];
  class BrokenImage {
    constructor() { images.push(this); }
    async decode() { throw new Error("decode failed"); }
  }
  await assert.rejects(loadAtlasBundle({ files: { "assets/web-atlases.json": hash } },
    { fetcher: async () => new Response(bytes), ImageType: BrokenImage }), /decode failed/);
  assert.ok(images.length > 0); assert.ok(images.every(image => image.src === ""));
});
