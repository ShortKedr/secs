"""Validate the six package-backed Unity consumer results for one release run."""

from __future__ import annotations

import json
import hashlib
import re
from pathlib import Path


UNITY_KINDS = tuple(
    f"unity-{branch}-{backend}"
    for branch in ("2022.3", "6")
    for backend in ("Editor", "Mono", "IL2CPP")
)
VERSION_PATTERN = {
    "2022.3": re.compile(r"2022\.3\.\d+[abcfpx]\d+"),
    "6": re.compile(r"6000\.\d+\.\d+[abcfpx]\d+"),
}


def load_unity_evidence(directory: Path, version: str, commit: str, run_id: str,
                        package: Path) -> list[dict[str, str]]:
    if not run_id.isdigit() or not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        raise ValueError("Unity evidence needs a numeric run ID and full source commit")
    package_sha = hashlib.sha256(package.read_bytes()).hexdigest()
    records = []
    for kind in UNITY_KINDS:
        path = directory / f"{kind}.json"
        if not path.is_file():
            raise ValueError(f"Missing verified Unity run: {path}")
        entry = json.loads(path.read_text(encoding="utf-8"))
        expected = {
            "kind": kind,
            "packageVersion": version,
            "sourceCommit": commit.lower(),
            "packageSha256": package_sha,
            "runId": run_id,
            "result": "passed",
        }
        for field, value in expected.items():
            if entry.get(field) != value:
                raise ValueError(f"{kind}: {field} must be {value!r}, got {entry.get(field)!r}")
        branch, backend = kind.removeprefix("unity-").rsplit("-", 1)
        runtime = entry.get("runtime", "")
        if not isinstance(runtime, str) or not runtime.startswith("Unity ") or not runtime.endswith(f" ({backend})"):
            raise ValueError(f"{kind}: runtime lacks exact Unity Editor/backend: {runtime!r}")
        editor_version = runtime.removeprefix("Unity ").removesuffix(f" ({backend})")
        if not VERSION_PATTERN[branch].fullmatch(editor_version):
            raise ValueError(f"{kind}: expected a full {branch} Editor patch, got {editor_version!r}")
        os_name = entry.get("os", "")
        if not isinstance(os_name, str) or not os_name.startswith("Windows ") or any(char in os_name for char in "|\n\r"):
            raise ValueError(f"{kind}: expected a single-line Windows runner OS")
        records.append({
            "branch": branch,
            "editorVersion": editor_version,
            "backend": backend,
            "os": os_name,
            "result": "passed",
        })
    return records


def render_unity_matrix(version: str, commit: str, run_id: str, records: list[dict[str, str]]) -> bytes:
    lines = [
        f"# Verified Unity consumers for SKECS {version}",
        "",
        "The SKECS.dll in this ZIP is byte-for-byte the netstandard2.1 assembly in the release NuGet package.",
        f"Source commit: `{commit.lower()}`.",
        f"Evidence: https://github.com/ShortKedr/skecs/actions/runs/{run_id}.",
        "",
        "| Unity branch | Exact Editor version | Backend exercised | Runner OS | Result |",
        "| --- | --- | --- | --- | --- |",
    ]
    for item in records:
        lines.append(
            f"| {item['branch']} | {item['editorVersion']} | {item['backend']} | {item['os']} | Passed |"
        )
    lines.extend(("", "Editor means an Editor smoke run; Mono and IL2CPP mean Windows players built and run.", ""))
    return "\n".join(lines).encode("utf-8")
