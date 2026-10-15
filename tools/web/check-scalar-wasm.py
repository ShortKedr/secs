"""Decode a production module and reject SIMD types/opcodes, not strings/metadata."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
TOKEN = re.compile(r"\b(?:v128|i8x16|i16x8|i32x4|i64x2|f32x4|f64x2)\b")
STRING = re.compile(r'"(?:\\.|[^"\\])*"')


def simd_lines(lines):
    matches = []
    for number, line in enumerate(lines, 1):
        # Binaryen emits data strings on one line; interpreter opcode names in data are harmless.
        if line.lstrip().startswith("(data "):
            continue
        code = STRING.sub('""', line).split(";;", 1)[0]
        if TOKEN.search(code):
            matches.append({"line": number, "code": code.strip()[:240]})
    return matches


def decoder_path(explicit):
    if explicit:
        return Path(explicit).resolve()
    sdk = os.environ.get("DOTNET_ROOT")
    if sdk:
        pinned = ET.parse(ROOT / "SpaceBattle.Web/SpaceBattle.Web.csproj").findtext("./PropertyGroup/RuntimeFrameworkVersion")
        binary = "wasm-dis.exe" if os.name == "nt" else "wasm-dis"
        pinned_version = tuple(int(part) for part in pinned.split("."))
        candidates = []
        for file in Path(sdk).glob(f"packs/Microsoft.NET.Runtime.Emscripten.*.Sdk.*/*/tools/bin/{binary}"):
            if not file.is_file():
                continue
            try:
                version = tuple(int(part) for part in file.parents[2].name.split("."))
            except ValueError:
                continue
            # The SDK can roll forward to a newer runtime patch when installing wasm-tools.
            if len(version) == 3 and version[:2] == pinned_version[:2] and version >= pinned_version:
                candidates.append((version, file))
        if candidates:
            newest = max(version for version, _ in candidates)
            files = [file for version, file in candidates if version == newest]
            if len(files) == 1:
                return files[0]
    found = shutil.which("wasm-dis")
    if found:
        return Path(found)
    raise ValueError("Provide --wasm-dis or a DOTNET_ROOT with a compatible Emscripten pack")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("module", type=Path)
    parser.add_argument("--wasm-dis")
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    module = args.module.resolve(); module.relative_to(ROOT)
    output = args.output.resolve(); output.relative_to(ROOT / "Benchmarks.Local/tmp")
    if output.exists():
        raise ValueError("Use a fresh diagnostic output")
    before = hashlib.sha256(module.read_bytes()).hexdigest()
    decoder = decoder_path(args.wasm_dis)
    decoded = subprocess.run([str(decoder), str(module)], capture_output=True, text=True, check=True)
    matches = simd_lines(decoded.stdout.splitlines())
    assert hashlib.sha256(module.read_bytes()).hexdigest() == before, "Input module changed"
    report = {"scalar": not matches, "module": str(module), "sha256": before,
              "bytes": module.stat().st_size, "decoder": str(decoder), "simdSyntaxLines": len(matches),
              "examples": matches[:10], "scope": "Binaryen-decoded types/opcodes; data strings and custom metadata do not impose SIMD. This is not a CPU measurement or proof of support for other WASM features."}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if matches:
        raise ValueError(f"SIMD module rejected: {len(matches)} type/opcode lines")
    print(f"PASS: scalar module, {report['bytes']} bytes, SHA256 {before}")


if __name__ == "__main__":
    main()
