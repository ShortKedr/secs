"""Fail closed when the SECS release package differs from the tagged source."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


EXPECTED_TFMS = {"netstandard2.0", "netstandard2.1", "net8.0"}
FORBIDDEN_PARTS = {
    "spacebattle",
    "spacebattle.web",
    "benchmarks.local",
    "vendor",
    "secs.tests",
    "secs.benchmarks",
    "obj",
    "bin",
    "tmp",
}


def fail(message: str) -> None:
    raise ValueError(message)


def text_of(root: ET.Element, name: str) -> str:
    node = root.find(f".//{{*}}metadata/{{*}}{name}")
    return (node.text or "").strip() if node is not None else ""


def inspect_package(path: Path, package_id: str, version: str, commit: str, license_file: Path) -> None:
    if not path.is_file():
        fail(f"Package is missing: {path}")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        fail("Expected source commit must be a full 40-character SHA-1")

    with zipfile.ZipFile(path) as archive:
        bad = archive.testzip()
        if bad:
            fail(f"Corrupt package member: {bad}")
        names = archive.namelist()
        nuspecs = [name for name in names if name.lower().endswith(".nuspec")]
        if len(nuspecs) != 1:
            fail(f"Expected one .nuspec, found {nuspecs}")
        for name in names:
            if Path(name).is_absolute() or ".." in name.replace("\\", "/").split("/"):
                fail(f"Unsafe package path: {name}")
            parts = {part.lower() for part in name.replace("\\", "/").split("/")}
            if parts & FORBIDDEN_PARTS:
                fail(f"Unwanted content in package: {name}")

        root = ET.fromstring(archive.read(nuspecs[0]))
        metadata = root.find("{*}metadata")
        if metadata is None:
            fail("Package has no nuspec metadata")
        checks = {
            "id": package_id,
            "version": version,
            "license": "MIT",
        }
        for field, expected in checks.items():
            actual = text_of(root, field)
            if actual != expected:
                fail(f"Package {field}: expected {expected!r}, got {actual!r}")
        license_node = metadata.find("{*}license")
        if license_node is None or license_node.attrib.get("type") != "expression":
            fail("Package license must use the MIT license expression")
        for field in ("authors", "description"):
            value = text_of(root, field)
            if not value or "package description" in value.lower() or "todo" in value.lower():
                fail(f"Package {field} is empty or a template: {value!r}")

        repository = metadata.find("{*}repository")
        if repository is None:
            fail("Package has no repository metadata")
        if repository.attrib.get("type") != "git":
            fail("Package repository type must be git")
        if repository.attrib.get("commit", "").lower() != commit.lower():
            fail("Package repository commit differs from release commit")
        url = repository.attrib.get("url", "")
        if not url.startswith("https://github.com/ShortKedr/secs"):
            fail(f"Unexpected repository URL: {url!r}")

        readme = text_of(root, "readme")
        if not readme or readme not in names:
            fail("Package readme metadata does not point to a packaged file")
        if not archive.read(readme).strip():
            fail("Packaged README is empty")
        if "LICENSE" not in names or not archive.read("LICENSE").strip():
            fail("Package must contain the repository MIT LICENSE at its root")
        if archive.read("LICENSE") != license_file.read_bytes():
            fail("Packaged LICENSE differs from the repository LICENSE")

        dlls = {
            name for name in names
            if re.fullmatch(r"lib/[^/]+/SECS\.dll", name, flags=re.IGNORECASE)
        }
        expected_dlls = {f"lib/{tfm}/SECS.dll" for tfm in EXPECTED_TFMS}
        if dlls != expected_dlls:
            fail(f"Expected SECS DLLs {sorted(expected_dlls)}, found {sorted(dlls)}")
        other_libs = {
            name for name in names
            if name.lower().startswith("lib/") and name.lower().endswith(".dll")
            and name not in expected_dlls
        }
        if other_libs:
            fail(f"Unexpected DLLs in core package: {sorted(other_libs)}")
        dotnet = shutil.which("dotnet")
        if dotnet is None:
            fail("dotnet SDK is required to verify assembly versions")
        with tempfile.TemporaryDirectory(prefix="secs-package-assembly-", dir=path.parent) as directory:
            for tfm in sorted(EXPECTED_TFMS):
                dll_file = Path(directory) / f"SECS-{tfm}.dll"
                dll_file.write_bytes(archive.read(f"lib/{tfm}/SECS.dll"))
                command = [dotnet, "run", "--project", str(Path(__file__).with_name("InspectAssemblyRefs.csproj")),
                           "--configuration", "Release", "--", str(dll_file)]
                result = subprocess.run(command, capture_output=True, text=True, check=False)
                if result.returncode:
                    fail(f"Assembly inspection failed for {tfm}: {result.stderr.strip()}")
                try:
                    identity = json.loads(result.stdout.strip().splitlines()[-1])
                except (IndexError, json.JSONDecodeError) as error:
                    fail(f"Assembly inspection returned invalid JSON for {tfm}: {error}")
                if identity.get("assemblyVersion") != f"{version}.0":
                    fail(f"{tfm} DLL assembly version {identity.get('assemblyVersion')} differs from {version}.0")

        dependency_groups = metadata.find("{*}dependencies")
        if dependency_groups is None:
            fail("Package dependency groups are missing")
        groups = {}
        for group in dependency_groups.findall("{*}group"):
            tfm = group.attrib.get("targetFramework", "").lower()
            tfm = tfm.replace(".netstandard", "netstandard").replace(".netcoreapp", "net")
            if tfm in groups:
                fail(f"Duplicate dependency group: {tfm}")
            groups[tfm] = [(item.attrib.get("id"), item.attrib.get("version"))
                           for item in group.findall("{*}dependency")]
        if set(groups) != EXPECTED_TFMS:
            fail(f"Expected dependency groups {sorted(EXPECTED_TFMS)}, found {sorted(groups)}")
        if groups["netstandard2.0"] != [("System.Memory", "4.5.4")]:
            fail(f"Unexpected netstandard2.0 dependencies: {groups['netstandard2.0']}")
        for tfm in ("netstandard2.1", "net8.0"):
            if groups[tfm]:
                fail(f"Expected self-contained {tfm}, found dependencies: {groups[tfm]}")

        print(f"PASS: {path.name}: {package_id} {version}, {sorted(EXPECTED_TFMS)}, commit {commit}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("--package-id", default="ShortKedr.SECS")
    parser.add_argument("--version", default="0.1.0")
    parser.add_argument("--commit", required=True)
    parser.add_argument("--license", type=Path, default=Path("LICENSE"))
    args = parser.parse_args()
    try:
        inspect_package(args.package, args.package_id, args.version, args.commit, args.license)
    except (OSError, ValueError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
