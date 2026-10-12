"""Audit executed .NET8 Mono build commands, response files and LTO inputs."""
import argparse
import hashlib
import json
import re
from collections import Counter
from pathlib import Path


def object_kind(data):
    if data.startswith(b"BC\xc0\xde") or data.startswith(b"\xde\xc0\x17\x0b"):
        return "llvm-bitcode"
    if data.startswith(b"\0asm"):
        return "wasm-object"
    if data.startswith(b"\x7fELF"):
        return "elf"
    return "other"


def archive_kinds(data):
    if not data.startswith(b"!<arch>\n"):
        raise ValueError("Not an ar archive")
    result = Counter()
    offset = 8
    while offset + 60 <= len(data):
        header = data[offset:offset + 60]
        if header[58:60] != b"`\n":
            raise ValueError("Invalid ar header")
        size = int(header[48:58].strip())
        name = header[:16].decode("ascii").strip()
        payload = data[offset + 60:offset + 60 + size]
        if name not in ("/", "//", "/SYM64/"):
            if name.startswith("#1/"):
                payload = payload[int(name[3:]):]
            result[object_kind(payload)] += 1
        offset += 60 + size + size % 2
    return dict(result)


def audit(variant):
    completion = json.loads((variant / "completion.json").read_text(encoding="utf-8-sig"))
    if completion["exitCode"] != 0:
        raise ValueError("Build did not succeed")
    log = (variant / "build-diagnostic.log").read_text(encoding="utf-8-sig", errors="replace")
    responses = {}
    for file in variant.glob("build/obj/SpaceBattle.Web/**/emcc*.rsp"):
        responses[file.name] = file.read_text(encoding="utf-8-sig")
    for match in re.finditer(r'"@([^"\r\n]*[\\/]native[\\/]src[\\/]emcc-(?:default|link)\.rsp)"', log):
        file = Path(match.group(1))
        if file.is_file():
            responses["runtime-" + file.name] = file.read_text(encoding="utf-8-sig")
    if not all(name in responses for name in ("emcc-compile.rsp", "emcc-compile-bc.rsp", "emcc-link.rsp")):
        raise ValueError("Missing compilation stage evidence")
    flags = {name: re.findall(r"(?<!\S)(-O[0-3sz]|-flto(?:=\w+)?|-ffast-math)(?!\S)", text)
             for name, text in responses.items()}
    if any("-ffast-math" in values for values in flags.values()):
        raise ValueError("Forbidden fast math")
    for name in ("emcc-compile.rsp", "emcc-compile-bc.rsp", "emcc-link.rsp"):
        optimization = [flag for flag in flags[name] if flag.startswith("-O")]
        if len(optimization) != 1:
            raise ValueError(f"Ambiguous optimization flags in {name}: {optimization}")
    wasm = list((variant / "publish/wwwroot/_framework").glob("dotnet.native*.wasm"))
    if len(wasm) != 1:
        raise ValueError("Expected one native WASM module")
    archives = {}
    for line in responses["emcc-link.rsp"].splitlines():
        file = Path(line.strip().strip('"'))
        if file.suffix == ".a" and file.is_file():
            archives[file.name] = archive_kinds(file.read_bytes())
    objects = {}
    for file in variant.glob("build/obj/SpaceBattle.Web/**/*.o"):
        with file.open("rb") as stream:
            kind = object_kind(stream.read(4))
        objects[file.name] = kind
    commands = [line.strip() for line in log.splitlines()
                if ("--aot=" in line or "clang" in line or "wasm-opt" in line or "emcc \"@" in line or "wasm-ld" in line)
                and not line.lstrip().startswith(("Task Parameter", "Set Property"))]
    if not any("--aot=" in command for command in commands):
        raise ValueError("No actual Mono AOT invocation")
    return {"variant": variant.name, "build": completion, "responseFiles": responses, "optimizationFlags": flags,
            "objectKinds": objects, "runtimeArchiveMembers": archives,
            "commands": commands, "wasmBytes": wasm[0].stat().st_size,
            "wasmBrotliBytes": sum(file.stat().st_size for file in wasm[0].parent.glob("dotnet.native*.wasm.br")),
            "wasmSha256": hashlib.sha256(wasm[0].read_bytes()).hexdigest(),
            "notes": "Prebuilt wasm runtime objects cannot be reoptimized by ordinary LLVM LTO. Flags are verified by response files plus actual verbose clang/link commands."}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("variant", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    args.output.resolve().relative_to(root / "Benchmarks.Local" / "tmp")
    if args.output.exists():
        raise ValueError("Refusing to overwrite audit")
    result = audit(args.variant.resolve())
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("variant", "optimizationFlags", "wasmBytes", "runtimeArchiveMembers")}))


if __name__ == "__main__":
    main()
