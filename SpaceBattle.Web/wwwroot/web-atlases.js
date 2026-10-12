export const browserContract = "png-assets-v1";
export const atlasRevision = "png-atlases-v1";

export async function digest(bytes) {
  const hash = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(hash), value => value.toString(16).padStart(2, "0")).join("");
}

export async function verifiedJson(url, expected, fetcher = fetch) {
  const response = await fetcher(url, { cache: "no-store" });
  if (!response.ok) throw new Error(`Resource unavailable: ${url} (${response.status})`);
  const bytes = await response.arrayBuffer();
  if (expected && await digest(bytes) !== expected) throw new Error(`Mixed cached resource: ${url}. Reload the page.`);
  return JSON.parse(new TextDecoder().decode(bytes));
}

export async function verifyBuild(moduleUrl, fetcher = fetch) {
  const build = await verifiedJson("build-identity.json", null, fetcher);
  if (build.schema !== 1 || build.contract !== browserContract || !build.files ||
      ["app.js", "_framework/blazor.boot.json", "assets/web-atlases.json"].some(key => !/^[a-f0-9]{64}$/.test(build.files[key])))
    throw new Error("Unsupported web build identity");
  if (new URL(moduleUrl).searchParams.get("v") !== build.files["app.js"])
    throw new Error("Mixed cached application. Reload the page to load one complete build.");
  // Blazor also checks each boot resource's integrity before reusing its cache.
  await verifiedJson("_framework/blazor.boot.json?v=" + build.files["_framework/blazor.boot.json"],
    build.files["_framework/blazor.boot.json"], fetcher);
  return build;
}

export function validateBundle(bundle) {
  if (bundle.schema !== 1 || bundle.revision !== atlasRevision ||
      JSON.stringify(bundle.palette) !== JSON.stringify([[0.2, 0.85, 0.95], [1, 0.37, 0.4]]))
    throw new Error("Unsupported PNG atlas bundle");
  let total = 0;
  for (const family of ["ships", "guns", "effects", "warp"]) {
    const atlas = bundle.atlases?.[family];
    if (!atlas || ![atlas.width, atlas.height].every(value => Number.isInteger(value) && value > 0 && value <= 8192) ||
        atlas.alphaThreshold !== 32 || !Array.isArray(atlas.sprites) ||
        !Array.isArray(atlas.textures) || atlas.textures.length !== (["ships", "guns"].includes(family) ? 2 : 1))
      throw new Error(`Invalid ${family} atlas`);
    const seen = new Set();
    for (const sprite of atlas.sprites) {
      const kind = family === "ships" ? sprite.class : sprite.kind;
      const category = family === "effects" ? sprite.family : family;
      const maximum = family === "ships" ? 11 : family === "guns" || category === "projectile" ? 4 : 3;
      const key = category + ":" + kind;
      if (!Number.isInteger(kind) || kind < (family === "guns" ? -1 : 0) || kind > maximum || seen.has(key) ||
          family === "effects" && !["projectile", "particle"].includes(category) ||
          ![sprite.x, sprite.y, sprite.width, sprite.height].every(Number.isInteger) ||
          sprite.x < 0 || sprite.y < 0 || sprite.width < 1 || sprite.height < 1 ||
          sprite.x + sprite.width > atlas.width || sprite.y + sprite.height > atlas.height)
        throw new Error(`Invalid ${family} sprite crop or kind`);
      if (family !== "ships" && (!Number.isFinite(sprite.pivotX) || !Number.isFinite(sprite.pivotY) ||
          sprite.pivotX < 0 || sprite.pivotY < 0 || sprite.pivotX > sprite.width || sprite.pivotY > sprite.height))
        throw new Error(`Invalid ${family} sprite pivot`);
      if ((category === "particle" || family === "warp") &&
          (sprite.width !== sprite.height || sprite.pivotX !== sprite.width / 2 || sprite.pivotY !== sprite.height / 2))
        throw new Error("Particle/warp sprites require square cells and centered pivots");
      seen.add(key);
    }
    if (seen.size !== { ships: 12, guns: 6, effects: 9, warp: 4 }[family]) throw new Error(`Incomplete ${family} sprites`);
    for (const texture of atlas.textures) {
      if (!/^[a-z0-9-]+\.png$/.test(texture.file) || !/^[a-f0-9]{64}$/.test(texture.sha256) ||
          !texture.file.includes(texture.sha256.slice(0, 20)) || texture.width !== atlas.width || texture.height !== atlas.height)
        throw new Error(`Invalid ${family} PNG identity`);
      total += texture.width * texture.height * 4;
    }
  }
  if (total !== bundle.logicalDecodedBytes) throw new Error("Invalid atlas decoded-byte accounting");
  return bundle;
}

export async function loadAtlasBundle(build, { decodeImages = true, fetcher = fetch, ImageType = Image } = {}) {
  const hash = build.files["assets/web-atlases.json"];
  const bundle = validateBundle(await verifiedJson("assets/web-atlases.json?v=" + hash, hash, fetcher));
  const images = {}, allocated = [];
  try {
    for (const [family, atlas] of Object.entries(bundle.atlases)) {
      images[family] = [];
      if (!decodeImages) continue;
      // Decode sequentially: no original PNGs, readback buffers or Canvas sources.
      for (const texture of atlas.textures) {
        const image = new ImageType(); allocated.push(image);
        image.src = "assets/" + texture.file;
        await image.decode();
        if (!image.complete || image.naturalWidth !== texture.width || image.naturalHeight !== texture.height)
          throw new Error(`PNG dimensions mismatch: ${texture.file}`);
        images[family].push(image);
      }
    }
    return { bundle, images, diagnostics: { revision: bundle.revision, sourceType: "image",
      imageCount: allocated.length, canvasCount: 0, loads: allocated.length,
      decodedBytes: decodeImages ? bundle.logicalDecodedBytes : 0 } };
  } catch (error) {
    for (const image of allocated) image.src = "";
    throw error;
  }
}
