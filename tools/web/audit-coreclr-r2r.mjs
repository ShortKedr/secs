import fs from "node:fs/promises";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";

const begin = "/*json-start*/", end = "/*json-end*/";
export function readBoot(source) {
  const start = source.indexOf(begin), finish = source.indexOf(end, start);
  if (start < 0 || finish < 0 || source.indexOf(begin, start + begin.length) >= 0)
    throw new Error("Expected one .NET 11 embedded boot configuration");
  return { config: JSON.parse(source.slice(start + begin.length, finish)), start, finish };
}

const digest = (bytes) => crypto.createHash("sha256").update(bytes).digest();
export function functionExports(bytes) {
  let position = 8;
  const integer = () => {
    let result = 0, shift = 0, byte;
    do {
      if (position >= bytes.length || shift > 28) throw new Error("Malformed WASM integer");
      byte = bytes[position++]; result += (byte & 127) * 2 ** shift; shift += 7;
    } while (byte & 128);
    return result;
  };
  while (position < bytes.length) {
    const section = bytes[position++], size = integer(), limit = position + size;
    if (limit > bytes.length) throw new Error("Malformed WASM section");
    if (section === 7) {
      const count = integer(), exports = {};
      for (let i = 0; i < count; i++) {
        const length = integer(), name = bytes.subarray(position, position + length).toString("utf8");
        position += length;
        const kind = bytes[position++], index = integer();
        if (position > limit) throw new Error("Malformed WASM export");
        if (kind === 0) (exports[index] ??= []).push(name);
      }
      return exports;
    }
    position = limit;
  }
  return {};
}
const hotExports = [
  ["SpaceBattle.Web", "BattleBridge__Advance"],
  ["SpaceBattle.Core", "Battle_1<System___Canon>__Step"],
  ["SpaceBattle.Core", "PhysicsService__Step"],
  ["SpaceBattle.Core", "AiSystem<System___Canon>__Execute"],
  ["SpaceBattle.Core", "WeaponSystem<System___Canon>__Execute"],
  ["SpaceBattle.Core", "__UpdateTurrets"],
  ["SpaceBattle.SECS", "SecsStore__ForEach"],
  ["SECS", "ForEach"],
  ["SpaceBattle.Core", "PhysicsService__SolveVelocities"],
  ["SpaceBattle.Core", "CircleSweepMath__TryCircleRelative"],
  ["SpaceBattle.Core", "CircleSweepMath__TryRectangleRelative"],
];

export function checkHotExports(inventory) {
  return hotExports.map(([assembly, fragment]) => {
    const matches = inventory[assembly]?.functions.filter((name) => name.includes(fragment)) ?? [];
    if (!matches.length) throw new Error(`R2R hot-code gate failed: ${assembly} / ${fragment}`);
    return { assembly, fragment, matches };
  });
}

export function checkStaticRoots(inventory, manifest) {
  if (manifest.provenance !== "static-il-roots-not-measured-pgo" || !manifest.roots?.length)
    throw new Error("Missing static IL root provenance");
  const checked = [];
  for (const root of manifest.roots.filter((root) => root.kinds.includes("store-call"))) {
    const method = root.method.match(/::([^<]+)</)?.[1];
    const functions = inventory["SpaceBattle.SECS"]?.functions ?? [];
    let fragments;
    if (method === "ForEach") {
      const processor = root.genericArguments.at(-1).match(/\/([^/<]+)</)?.[1];
      if (!processor) throw new Error(`Cannot identify processor: ${root.method}`);
      fragments = [`SecsStore__ForEach`, `_${processor}<`];
      const loops = inventory.SECS?.functions ?? [];
      if (!loops.some((name) => name.includes("__ForEach<") && name.includes(`_${processor}<`)))
        throw new Error(`Missing compiled SECS query loop for ${processor}`);
      if (!functions.some((name) => name.includes("SecsStore_Bridge_") && name.includes(`_${processor}<`) && name.includes("__Execute")))
        throw new Error(`Missing compiled processor bridge for ${processor}`);
    } else {
      const component = root.genericArguments[0]?.match(/\]SpaceBattle\.([\w]+)$/)?.[1];
      if (!component || !["Get", "Has", "Set", "Remove"].includes(method))
        throw new Error(`Unrecognized closed store root: ${root.method}`);
      fragments = [`SecsStore__${method}<`, `SpaceBattle_Core_SpaceBattle_${component}>`];
    }
    if (!functions.some((name) => fragments.every((fragment) => name.includes(fragment))))
      throw new Error(`Missing compiled store specialization: ${root.method}`);
    checked.push(root.key);
  }
  return checked;
}

export async function audit(site, r2r, requireHot = true) {
  const framework = path.join(site, "_framework"), boot = path.join(framework, "dotnet.js");
  const source = await fs.readFile(boot, "utf8"), { config } = readBoot(source);
  const inventory = {};
  for (const group of ["coreAssembly", "assembly"]) {
    if (!Array.isArray(config.resources[group])) throw new Error(`Missing ${group}`);
    for (const asset of config.resources[group]) {
      const assembly = asset.virtualPath.replace(/\.wasm$/, "");
      if (path.basename(asset.name) !== asset.name || path.basename(assembly) !== assembly)
        throw new Error("Unsafe boot resource path");
      const bytes = await fs.readFile(path.join(r2r, `${assembly}.wasm`));
      const module = new WebAssembly.Module(bytes);
      const exports = WebAssembly.Module.exports(module);
      if (!exports.some((item) => item.name === "webcilVersion"))
        throw new Error(`${assembly} is not Webcil v1`);
      const functions = exports.filter((item) => item.kind === "function").map((item) => item.name);
      const packaged = await fs.readFile(path.join(framework, asset.name));
      if (!packaged.equals(bytes)) throw new Error(`Packaged ${assembly} differs from crossgen2 output`);
      if (asset.hash !== `sha256-${digest(bytes).toString("base64")}`)
        throw new Error(`Integrity mismatch for ${assembly}`);
      if (functions.length && !(asset.tableSize > 0)) throw new Error(`Missing R2R tableSize for ${assembly}`);
      inventory[assembly] = { name: asset.name, bytes: bytes.length, sha256: digest(bytes).toString("hex"), functions, functionIndices: functionExports(bytes) };
    }
  }
  let coverage = [], staticCoverage = [], coverageError;
  try {
    coverage = checkHotExports(inventory);
    const manifest = JSON.parse(await fs.readFile(path.join(site, "r2r-static-roots.json"), "utf8"));
    staticCoverage = checkStaticRoots(inventory, manifest);
  }
  catch (error) { coverageError = error.message; }
  await fs.writeFile(path.join(site, "r2r-inventory.json"), JSON.stringify({
    runtime: "11.0.0-rc.1.26425.128", compiler: "crossgen2", coverage, staticCoverage, coverageError, assemblies: inventory,
    note: "Exports prove emission, not absence of runtime interpreter fallback. Browser execution must be checked separately.",
  }, null, 2));
  if (coverageError && requireHot) throw new Error(coverageError);
  return { assemblies: Object.keys(inventory).length, coverage: coverage.length, staticCoverage: staticCoverage.length, coverageError };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  const [site, r2r] = process.argv.slice(2);
  if (!site || !r2r) throw new Error("Usage: node --experimental-wasm-exnref audit-coreclr-r2r.mjs <wwwroot> <r2r>");
  console.log(await audit(path.resolve(site), path.resolve(r2r), !process.argv.includes("--allow-partial")));
}
