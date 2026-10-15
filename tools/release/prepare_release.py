"""Validate a SKECS tag and prepare immutable GitHub Release files locally."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from zoneinfo import ZoneInfo


VERSION_PATTERN = re.compile(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?$")
TAG_PATTERN = re.compile(r"skecs-v" + VERSION_PATTERN.pattern)
NAMES = {(0, 1): "Cone", (0, 2): "Branch", (1, 0): "Canopy"}
REQUIRED_EVIDENCE = (
    "dotnet8", "dotnet10", "framework", "godot",
    "unity-2022.3-Editor", "unity-2022.3-Mono", "unity-2022.3-IL2CPP",
    "unity-6-Editor", "unity-6-Mono", "unity-6-IL2CPP",
)


def version_from_project(project: Path) -> str:
    root = ET.parse(project).getroot()
    versions = [(node.text or "").strip() for node in root.findall(".//Version")]
    if len(versions) != 1 or not versions[0]:
        raise ValueError(f"Expected exactly one nonempty Version in {project}")
    if VERSION_PATTERN.fullmatch(versions[0]) is None:
        raise ValueError(f"Project Version must use numeric SemVer: {versions[0]!r}")
    return versions[0]


def check_tag(tag: str, project: Path) -> str:
    match = TAG_PATTERN.fullmatch(tag)
    if match is None:
        raise ValueError(f"Tag must use skecs-vMAJOR.MINOR.PATCH: {tag!r}")
    tag_version = tag.removeprefix("skecs-v")
    project_version = version_from_project(project)
    if project_version != tag_version:
        raise ValueError(f"Tag version {tag_version} differs from project Version {project_version}")
    return tag_version


def name_for(version: str) -> str:
    match = VERSION_PATTERN.fullmatch(version)
    if match is None:
        raise ValueError(f"Invalid version: {version}")
    line = (int(match.group(1)), int(match.group(2)))
    if line not in NAMES:
        raise ValueError(f"No release name and artwork defined for {line[0]}.{line[1]}.x")
    return NAMES[line]


def evidence_table(directory: Path, version: str, commit: str, run_id: str, package: Path) -> str:
    if not run_id.isdigit():
        raise ValueError("GitHub run ID must be numeric")
    package_sha = hashlib.sha256(package.read_bytes()).hexdigest()
    actual_files = {path.stem for path in directory.glob("*.json")}
    if actual_files != set(REQUIRED_EVIDENCE):
        raise ValueError(f"Expected runtime evidence {list(REQUIRED_EVIDENCE)}, found {sorted(actual_files)}")
    rows = []
    for kind in REQUIRED_EVIDENCE:
        entry = json.loads((directory / f"{kind}.json").read_text(encoding="utf-8"))
        for field, expected in (("kind", kind), ("packageVersion", version),
                                ("sourceCommit", commit.lower()), ("packageSha256", package_sha),
                                ("runId", run_id),
                                ("result", "passed")):
            if entry.get(field) != expected:
                raise ValueError(f"{kind} evidence {field}: expected {expected!r}, got {entry.get(field)!r}")
        runtime = entry.get("runtime", "")
        os_name = entry.get("os", "")
        if not isinstance(runtime, str) or not isinstance(os_name, str) or not runtime or not os_name:
            raise ValueError(f"{kind} evidence lacks runtime or OS")
        if "|" in runtime + os_name or "\n" in runtime + os_name:
            raise ValueError(f"{kind} evidence contains unsafe Markdown table content")
        expected_runtime = {
            "dotnet8": "Microsoft.NETCore.App 8.",
            "dotnet10": "Microsoft.NETCore.App 10.",
            "framework": ".NET Framework 4.8-compatible (registry release ",
            "godot": "Godot 4.5",
        }.get(kind)
        if kind.startswith("unity-"):
            branch, backend = kind.removeprefix("unity-").rsplit("-", 1)
            expected_runtime = f"Unity {'6000.' if branch == '6' else '2022.3.'}"
            if not runtime.endswith(f"({backend})"):
                raise ValueError(f"{kind} evidence names the wrong backend: {runtime}")
        if not runtime.startswith(expected_runtime):
            raise ValueError(f"{kind} evidence names the wrong runtime: {runtime}")
        if kind == "framework":
            release_code = re.search(r"registry release (\d+)\)", runtime)
            if release_code is None or int(release_code.group(1)) < 528040:
                raise ValueError("Windows runner lacks .NET Framework 4.8 or later")
        label = kind.replace("unity-", "Unity ").replace("dotnet", ".NET ").replace("framework", ".NET Framework")
        rows.append(f"| {label} | {runtime} | {os_name} | Passed |")
    return "| Consumer | Exact runtime/editor | Runner OS | Result |\n| --- | --- | --- | --- |\n" + "\n".join(rows)


def create_files(tag: str, commit: str, package: Path, unity_zip: Path, cover: Path,
                 install: Path, output: Path, project: Path, evidence: Path, run_id: str) -> None:
    version = check_tag(tag, project)
    codename = name_for(version)
    if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        raise ValueError("Source commit must be a full 40-character SHA-1")
    expected_names = {f"ShortKedr.SKECS.{version}.nupkg", f"SKECS.Unity.{version}.zip", f"{codename.lower()}.png"}
    assets = (package, unity_zip, cover)
    if {asset.name for asset in assets} != expected_names:
        raise ValueError(f"Expected asset names {sorted(expected_names)}, found {[item.name for item in assets]}")
    for asset in assets:
        if not asset.is_file() or asset.stat().st_size == 0:
            raise ValueError(f"Release asset missing or empty: {asset}")
    if not install.is_file() or not install.read_text(encoding="utf-8").strip():
        raise ValueError("Installation guide is missing or empty")
    matrix = evidence_table(evidence, version, commit, run_id, package)

    output.mkdir(parents=True, exist_ok=True)
    sums = []
    for asset in sorted(assets, key=lambda item: item.name):
        digest = hashlib.sha256(asset.read_bytes()).hexdigest()
        sums.append(f"{digest}  {asset.name}")
    checksums = "\n".join(sums) + "\n"
    (output / "SHA256SUMS.txt").write_text(checksums, encoding="utf-8", newline="\n")

    today = dt.datetime.now(ZoneInfo("Asia/Qyzylorda")).date().isoformat()
    title = f"SKECS v{version} — {codename} · {today}"
    (output / "title.txt").write_text(title + "\n", encoding="utf-8", newline="\n")
    base = f"https://github.com/ShortKedr/skecs/releases/download/{tag}"
    if version == "0.1.0":
        introduction = "the first early release of the C# ECS core"
        changes = ("Initial public package. Create worlds and entities with unmanaged components, "
                   "iterate queries, read or modify components, and dispose world memory. "
                   "The pre-1.0 API can change.")
    else:
        change_file = Path(f"docs/skecs/RELEASE_NOTES_{version}.md")
        if not change_file.is_file() or not change_file.read_text(encoding="utf-8").strip():
            raise ValueError(f"A version-specific change summary is required: {change_file}")
        introduction = "a release of the C# ECS core"
        changes = change_file.read_text(encoding="utf-8").strip()
    # GitHub renders title.txt above the body; do not repeat the release title here.
    notes = f"""![SKECS {codename}]({base}/{codename.lower()}.png)

This is {introduction}.

## Changes

{changes}

## Install

- .NET 8 or .NET 10: download `{package.name}` and add it as a local NuGet package source.
- .NET Framework 4.8: use the same package with the `netstandard2.0` asset.
- Unity 2022.3 LTS / Unity 6: download `{unity_zip.name}` and import the DLL as described in the installation guide.
- Godot 4.5 .NET desktop: add the local NuGet package to a C# project.

The [installation guide](https://github.com/ShortKedr/skecs/blob/{commit}/docs/skecs/INSTALL.md) contains exact commands and backend limits.

## Verified consumer matrix

{matrix}

These results come from the [release workflow run](https://github.com/ShortKedr/skecs/actions/runs/{run_id}) at the tagged commit; its logs and artifacts show the individual smoke runs.

## Contents and limits

The release contains the SKECS core, its package and Unity import archive. Space Battle, benchmarks and third-party vendor code are excluded. Fixed, SIMD, multithreading and GPU execution are future work, not features of this release.

## Verification and provenance

The release workflow builds and tests from [`{commit}`](https://github.com/ShortKedr/skecs/commit/{commit}), checks the consumer fixtures, inspects package metadata and ZIP contents, and checks SHA-256 digests before publication. Download all assets together and compare them with `SHA256SUMS.txt`.

Source: [`{tag}`](https://github.com/ShortKedr/skecs/tree/{tag}) · License: [MIT](https://github.com/ShortKedr/skecs/blob/{commit}/LICENSE).
"""
    (output / "notes.md").write_text(notes, encoding="utf-8", newline="\n")
    print(title)
    print(checksums, end="")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    check = sub.add_parser("check-tag")
    check.add_argument("--tag", required=True)
    check.add_argument("--project", type=Path, default=Path("SKECS/SKECS.csproj"))
    version_command = sub.add_parser("version")
    version_command.add_argument("--project", type=Path, default=Path("SKECS/SKECS.csproj"))
    name_command = sub.add_parser("name")
    name_command.add_argument("--version", required=True)
    make = sub.add_parser("create")
    make.add_argument("--tag", required=True)
    make.add_argument("--commit", required=True)
    make.add_argument("--package", type=Path, required=True)
    make.add_argument("--unity-zip", type=Path, required=True)
    make.add_argument("--cover", type=Path, required=True)
    make.add_argument("--install", type=Path, default=Path("docs/skecs/INSTALL.md"))
    make.add_argument("--output", type=Path, required=True)
    make.add_argument("--project", type=Path, default=Path("SKECS/SKECS.csproj"))
    make.add_argument("--evidence-dir", type=Path, required=True)
    make.add_argument("--run-id", required=True)
    args = parser.parse_args()
    try:
        if args.command == "check-tag":
            version = check_tag(args.tag, args.project)
            name_for(version)
            print(version)
        elif args.command == "version":
            print(version_from_project(args.project))
        elif args.command == "name":
            print(name_for(args.version).lower())
        else:
            create_files(args.tag, args.commit, args.package, args.unity_zip, args.cover,
                         args.install, args.output, args.project, args.evidence_dir, args.run_id)
    except (OSError, ValueError, ET.ParseError, json.JSONDecodeError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
