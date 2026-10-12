import fs from "node:fs/promises";
import path from "node:path";
import crypto from "node:crypto";

const input = process.argv[2], output = process.argv[3], symbols = process.argv[4];
if (!input || !output || !path.resolve(output).startsWith(path.resolve("Benchmarks.Local/tmp") + path.sep))
  throw new Error("Usage: inspect-wasm-code.mjs <module.wasm> <fresh ignored JSON> [symbols]");
const bytes = await fs.readFile(input);
if (!bytes.subarray(0, 8).equals(Buffer.from([0, 97, 115, 109, 1, 0, 0, 0]))) throw new Error("Invalid WASM module");
let cursor = 8, bound = bytes.length, importedFunctions = 0;
const imports = [], exports = [], memory = [], bodies = [], names = new Map();
function byte() { if (cursor >= bound) throw new Error("Truncated WASM section"); return bytes[cursor++]; }
function u32() {
  let value = 0;
  for (let i = 0; i < 5; i++) { const n = byte(); value += (n & 127) * 2 ** (i * 7);
    if (!(n & 128)) { if (value > 0xffffffff) throw new Error("Invalid u32"); return value; } }
  throw new Error("Invalid LEB128");
}
function text() { const length = u32(); if (cursor + length > bound) throw new Error("Invalid name");
  const result = bytes.toString("utf8", cursor, cursor + length); cursor += length; return result; }
function type() { const value = byte(); if (value === 0x63 || value === 0x64) { while (byte() & 128) {} } }
function limits() { const flags = u32(); if (flags & 4) throw new Error("Memory64 is outside this diagnostic parser");
  const minimum = u32(), maximum = flags & 1 ? u32() : null; return { minimum, maximum, shared: Boolean(flags & 2) }; }
while (cursor < bytes.length) {
  bound = bytes.length;
  const id = byte(), length = u32(), end = cursor + length;
  if (end > bytes.length) throw new Error("Truncated section");
  bound = end;
  if (id === 2) {
    const count = u32();
    for (let i = 0; i < count; i++) {
      const module = text(), name = text(), kind = byte(); imports.push({ module, name, kind });
      if (kind === 0) { u32(); importedFunctions++; }
      else if (kind === 1) { type(); limits(); }
      else if (kind === 2) memory.push(limits());
      else if (kind === 3) { type(); byte(); }
      else if (kind === 4) { byte(); u32(); }
      else throw new Error("Unknown import kind");
    }
  } else if (id === 5) { const count = u32(); for (let i = 0; i < count; i++) memory.push(limits()); }
  else if (id === 7) { const count = u32(); for (let i = 0; i < count; i++) {
    const name = text(), kind = byte(), index = u32(); exports.push({ name, kind, index }); if (kind === 0) names.set(index, name);
  } } else if (id === 10) {
    const count = u32(); for (let i = 0; i < count; i++) { const size = u32(); if (cursor + size > bound) throw new Error("Truncated function");
      bodies.push({ index: importedFunctions + i, bodyBytes: size }); cursor += size; }
  }
  cursor = end;
}
if (symbols) for (const line of (await fs.readFile(symbols, "utf8")).split(/\r?\n/)) {
  const match = /^(\d+):(.*)$/.exec(line); if (match) names.set(Number(match[1]), match[2]);
}
const result = { input: path.resolve(input), sha256: crypto.createHash("sha256").update(bytes).digest("hex"), bytes: bytes.length,
  importedFunctions, definedFunctions: bodies.length, memoryPages: memory,
  largestFunctions: bodies.sort((a, b) => b.bodyBytes - a.bodyBytes || a.index - b.index).slice(0, 30)
    .map(body => ({ ...body, name: names.get(body.index) ?? null })),
  functionsOver40KiB: bodies.filter(body => body.bodyBytes > 40960).length,
  asyncifyInterface: [...imports, ...exports].filter(item => /asyncify/i.test(item.name)),
  scope: "Offline section/body byte sizes only; no WASM compilation, execution, JSC cost estimate, memory attribution or proof of a WebKit bug." };
await fs.writeFile(output, JSON.stringify(result, null, 2), { flag: "wx" });
console.log(JSON.stringify({ bytes: result.bytes, definedFunctions: result.definedFunctions,
  largestBodyBytes: result.largestFunctions[0]?.bodyBytes, asyncifyInterface: result.asyncifyInterface.length }));
