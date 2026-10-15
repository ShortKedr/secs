"""Build the self-contained Unity import ZIP from the verified NuGet package."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


def inspect_assembly_references(dll: bytes, scratch_root: Path, version: str) -> list[str]:
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        raise ValueError("dotnet SDK is required to inspect Unity assembly references")
    dotnet_root = Path(os.environ.get("DOTNET_ROOT", "")) if os.environ.get("DOTNET_ROOT") else Path(dotnet).resolve().parent
    ref_packs = sorted((dotnet_root / "packs" / "NETStandard.Library.Ref").glob("*/ref/netstandard2.1"))
    if not ref_packs:
        raise ValueError(".NET Standard 2.1 reference pack is missing")
    framework_references = {path.stem for path in ref_packs[-1].glob("*.dll")}
    with tempfile.TemporaryDirectory(prefix="skecs-unity-assembly-", dir=scratch_root) as directory:
        assembly = Path(directory) / "SKECS.dll"
        assembly.write_bytes(dll)
        command = [
            dotnet, "run", "--project", str(Path(__file__).with_name("InspectAssemblyRefs.csproj")),
            "--configuration", "Release", "--", str(assembly),
        ]
        result = subprocess.run(command, capture_output=True, text=True, check=False)
    if result.returncode:
        raise ValueError(f"Assembly reference inspection failed: {result.stderr.strip()}")
    try:
        identity = json.loads(result.stdout.strip().splitlines()[-1])
    except (IndexError, json.JSONDecodeError) as error:
        raise ValueError(f"Assembly reference inspector returned invalid JSON: {result.stdout!r}") from error
    if identity.get("assemblyVersion") != f"{version}.0":
        raise ValueError(f"Unity DLL assembly version differs from package: {identity.get('assemblyVersion')}")
    references = identity.get("references")
    if not isinstance(references, list):
        raise ValueError("Assembly reference inspector omitted references")
    external = sorted(set(references) - framework_references)
    if external:
        raise ValueError(f"Unity DLL refers to assemblies outside the .NET Standard 2.1 reference pack: {external}")
    return references


def make_zip(package: Path, license_file: Path, guide: Path, output: Path, version: str, commit: str) -> None:
    if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        raise ValueError("Commit must be a full 40-character SHA-1")
    with zipfile.ZipFile(package) as source:
        if source.testzip():
            raise ValueError("NuGet package failed ZIP integrity check")
        nuspecs = [name for name in source.namelist() if name.lower().endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise ValueError("Expected exactly one nuspec")
        root = ET.fromstring(source.read(nuspecs[0]))
        metadata = root.find("{*}metadata")
        if metadata is None:
            raise ValueError("Package metadata missing")
        package_version = metadata.find("{*}version")
        if package_version is None or package_version.text != version:
            raise ValueError("Package version differs from requested Unity ZIP version")
        repository = metadata.find("{*}repository")
        if repository is None or repository.attrib.get("commit", "").lower() != commit.lower():
            raise ValueError("Package commit differs from requested Unity ZIP commit")
        dependencies = metadata.find("{*}dependencies")
        if dependencies is not None:
            for group in dependencies.findall("{*}group"):
                target = group.attrib.get("targetFramework", "").lower()
                if target in ("netstandard2.1", ".netstandard2.1") and group.findall("{*}dependency"):
                    raise ValueError("netstandard2.1 has package dependencies; Unity ZIP cannot omit their DLLs")
        dll_name = "lib/netstandard2.1/SKECS.dll"
        try:
            dll = source.read(dll_name)
        except KeyError as error:
            raise ValueError(f"Missing Unity-compatible assembly: {dll_name}") from error

    if not dll:
        raise ValueError("Unity-compatible assembly is empty")
    output.parent.mkdir(parents=True, exist_ok=True)
    assembly_references = inspect_assembly_references(dll, output.parent, version)
    license_bytes = license_file.read_bytes()
    guide_bytes = guide.read_bytes()
    if not license_bytes or not guide_bytes:
        raise ValueError("License and Unity import guide must be nonempty")

    entries = {
        "lib/netstandard2.1/SKECS.dll": dll,
        "LICENSE": license_bytes,
        "INSTALL.md": guide_bytes,
    }
    manifest = {
        "package": "ShortKedr.SKECS",
        "version": version,
        "sourceCommit": commit.lower(),
        "framework": "netstandard2.1",
        "assemblyReferences": assembly_references,
        "files": {name: hashlib.sha256(data).hexdigest() for name, data in sorted(entries.items())},
    }
    entries["manifest.json"] = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(entries.items()):
            archive.writestr(name, data)
    with zipfile.ZipFile(output) as archive:
        if archive.testzip() or set(archive.namelist()) != set(entries):
            raise ValueError("Built Unity ZIP failed integrity or membership check")
        for name, data in entries.items():
            if archive.read(name) != data:
                raise ValueError(f"Built Unity ZIP has altered data: {name}")
    print(f"PASS: {output}: {version}, {commit}, netstandard2.1, {len(entries)} files")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--license", type=Path, default=Path("LICENSE"))
    parser.add_argument("--guide", type=Path, default=Path("docs/skecs/INSTALL.md"))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", default="0.2.0")
    parser.add_argument("--commit", required=True)
    args = parser.parse_args()
    try:
        make_zip(args.package, args.license, args.guide, args.output, args.version, args.commit)
    except (OSError, ValueError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
