#!/usr/bin/env python3
"""Validate local SECS release evidence against the exact clean checkout.

The report lives under an ignored artifacts directory. Validation is read-only;
it does not run the checks or create the evidence it validates.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime
from pathlib import Path


CHECK_MARKERS = {
    "library-build": "SECS_LIBRARY_BUILD_OK",
    "unit-tests": "SECS_UNIT_TESTS_OK",
    "package-content": "SECS_PACKAGE_CONTENT_OK",
    "unity-zip-content": "SECS_UNITY_ZIP_CONTENT_OK",
    "documentation": "SECS_DOCUMENTATION_OK",
    "dotnet8-consumer": "SECS_CONSUMER_OK",
    "dotnet10-consumer": "SECS_CONSUMER_OK",
    "net48-consumer": "SECS_CONSUMER_OK",
    "godot45-consumer": "SECS_GODOT_OK",
    "unity-2022.3-editor": "SECS_UNITY_EDITOR_OK",
    "unity-2022.3-mono": "SECS_UNITY_OK",
    "unity-2022.3-il2cpp": "SECS_UNITY_OK",
    "unity-6-editor": "SECS_UNITY_EDITOR_OK",
    "unity-6-mono": "SECS_UNITY_OK",
    "unity-6-il2cpp": "SECS_UNITY_OK",
}
PLAYER_BACKENDS = {
    "unity-2022.3-mono": "Mono",
    "unity-2022.3-il2cpp": "IL2CPP",
    "unity-6-mono": "Mono",
    "unity-6-il2cpp": "IL2CPP",
}
SHA40 = re.compile(r"[0-9a-f]{40}\Z")
SHA256 = re.compile(r"[0-9a-f]{64}\Z")


class InvalidReport(Exception):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise InvalidReport(message)


def git(repo: Path, *args: str) -> str:
    result = subprocess.run(
        ["git", "-C", str(repo), *args],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    require(result.returncode == 0, f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_nonempty(path: Path) -> bytes:
    require(path.is_file(), f"missing file: {path}")
    data = path.read_bytes()
    require(bool(data), f"empty file: {path}")
    return data


def relative_file(repo: Path, name: object, label: str) -> Path:
    require(isinstance(name, str) and bool(name), f"{label} must be a relative path")
    candidate = Path(name)
    require(not candidate.is_absolute() and ".." not in candidate.parts, f"unsafe {label}: {name}")
    resolved = (repo / candidate).resolve()
    require(resolved.is_relative_to(repo), f"{label} escapes checkout: {name}")
    require(resolved.is_relative_to(repo / "artifacts"), f"{label} must be under artifacts/: {name}")
    return resolved


def project_version(repo: Path) -> str:
    project = repo / "SECS" / "SECS.csproj"
    require(project.is_file(), f"missing project: {project}")
    values = [node.text.strip() for node in ET.parse(project).iter("Version") if node.text]
    require(len(values) == 1 and bool(re.fullmatch(r"\d+\.\d+\.\d+", values[0])),
            "SECS.csproj must contain one numeric Version")
    return values[0]


def validate_artifacts(report: dict, repo: Path, report_path: Path, version: str, head: str) -> None:
    artifacts = report.get("artifacts")
    require(isinstance(artifacts, list), "artifacts must be an array")
    expected = {
        f"ShortKedr.SECS.{version}.nupkg",
        f"SECS.Unity.{version}.zip",
        "cone.png",
    }
    paths: dict[str, Path] = {}
    for entry in artifacts:
        require(isinstance(entry, dict), "artifact entry must be an object")
        name, digest = entry.get("file"), entry.get("sha256")
        require(isinstance(name, str) and Path(name).name == name and name not in (".", ".."),
                "artifact file must be a basename")
        require(name not in paths, f"duplicate artifact: {name}")
        require(isinstance(digest, str) and SHA256.fullmatch(digest) is not None,
                f"invalid SHA-256 for {name}")
        path = (report_path.parent / name).resolve()
        require(path.is_relative_to(repo / "artifacts"), f"artifact outside artifacts/: {name}")
        require(path != report_path, "report cannot hash itself")
        require(sha256(read_nonempty(path)) == digest, f"SHA-256 mismatch: {name}")
        paths[name] = path
    require(expected.issubset(paths), f"missing required artifacts: {sorted(expected - paths.keys())}")

    package = paths[f"ShortKedr.SECS.{version}.nupkg"]
    unity_zip = paths[f"SECS.Unity.{version}.zip"]
    try:
        with zipfile.ZipFile(package) as archive:
            nuspecs = [name for name in archive.namelist() if name.endswith(".nuspec")]
            require(len(nuspecs) == 1, "package must contain exactly one nuspec")
            nuspec = ET.fromstring(archive.read(nuspecs[0]))
            metadata = nuspec.find("{*}metadata")
            require(metadata is not None, "package nuspec lacks metadata")
            require(metadata.findtext("{*}id") == "ShortKedr.SECS", "package ID mismatch")
            require(metadata.findtext("{*}version") == version, "package version mismatch")
            repository = metadata.find("{*}repository")
            require(repository is not None and repository.get("commit") == head,
                    "package repository commit differs from HEAD")
            dll = archive.read("lib/netstandard2.1/SECS.dll")
        with zipfile.ZipFile(unity_zip) as archive:
            manifest = json.loads(archive.read("manifest.json"))
            require(manifest.get("package") == "ShortKedr.SECS", "Unity manifest package mismatch")
            require(manifest.get("version") == version, "Unity manifest version mismatch")
            require(manifest.get("sourceCommit") == head, "Unity manifest commit differs from HEAD")
            files = manifest.get("files")
            require(isinstance(files, dict), "Unity manifest lacks file hashes")
            for name in ("INSTALL.md", "LICENSE", "lib/netstandard2.1/SECS.dll"):
                require(isinstance(files.get(name), str) and SHA256.fullmatch(files[name]) is not None,
                        f"Unity manifest lacks SHA-256 for {name}")
                require(sha256(archive.read(name)) == files[name], f"Unity ZIP hash mismatch: {name}")
            require(archive.read("lib/netstandard2.1/SECS.dll") == dll,
                    "Unity DLL differs from package netstandard2.1 DLL")
            require(archive.read("INSTALL.md") == read_nonempty(repo / "docs" / "secs" / "INSTALL.md"),
                    "Unity guide differs from checked-out INSTALL.md")
            require(archive.read("LICENSE") == read_nonempty(repo / "LICENSE"),
                    "Unity LICENSE differs from checked-out LICENSE")
    except (KeyError, ValueError, ET.ParseError, zipfile.BadZipFile) as error:
        raise InvalidReport(f"invalid package or Unity ZIP: {error}") from error

    checksums = paths.get("SHA256SUMS.txt")
    if checksums:
        lines = read_nonempty(checksums).decode("utf-8").splitlines()
        listed: dict[str, str] = {}
        for line in lines:
            match = re.fullmatch(r"([0-9a-f]{64})  ([^/\\]+)", line)
            require(match is not None, f"malformed SHA256SUMS line: {line}")
            digest, name = match.groups()
            require(name not in listed, f"duplicate SHA256SUMS entry: {name}")
            listed[name] = digest
        for name in expected:
            require(listed.get(name) == sha256(read_nonempty(paths[name])),
                    f"SHA256SUMS mismatch or missing entry: {name}")


def validate_checks(report: dict, repo: Path) -> None:
    checks = report.get("checks")
    require(isinstance(checks, list), "checks must be an array")
    seen: set[str] = set()
    for entry in checks:
        require(isinstance(entry, dict), "check entry must be an object")
        check_id = entry.get("id")
        require(isinstance(check_id, str) and check_id in CHECK_MARKERS,
                f"unknown check: {check_id}")
        require(check_id not in seen, f"duplicate check: {check_id}")
        seen.add(check_id)
        require(entry.get("status") == "passed", f"check not passed: {check_id}")
        require(isinstance(entry.get("detail"), str) and bool(entry["detail"].strip()),
                f"missing detail: {check_id}")
        require(type(entry.get("exitCode")) is int and entry["exitCode"] == 0,
                f"missing successful exitCode: {check_id}")
        log = relative_file(repo, entry.get("log"), f"{check_id}.log")
        content = read_nonempty(log).decode("utf-8", errors="replace")
        require(CHECK_MARKERS[check_id] in content,
                f"missing {CHECK_MARKERS[check_id]} marker: {check_id}")
        if check_id in PLAYER_BACKENDS:
            build_log = relative_file(repo, entry.get("buildLog"), f"{check_id}.buildLog")
            build_content = read_nonempty(build_log).decode("utf-8", errors="replace")
            backend = PLAYER_BACKENDS[check_id]
            require(f"SECS_UNITY_BUILD_OK {backend}" in build_content,
                    f"missing Unity {backend} build marker: {check_id}")
        else:
            require("buildLog" not in entry, f"unexpected buildLog: {check_id}")
    require(seen == set(CHECK_MARKERS), f"missing checks: {sorted(set(CHECK_MARKERS) - seen)}")


def validate(report_path: Path, repo: Path) -> None:
    repo = repo.resolve()
    report_path = report_path.resolve()
    require(repo.is_dir(), f"missing checkout: {repo}")
    require(Path(git(repo, "rev-parse", "--show-toplevel")).resolve() == repo,
            "--repo-root must be the Git checkout root")
    require(report_path.is_relative_to(repo / "artifacts"), "report must be under artifacts/")
    report = json.loads(read_nonempty(report_path))
    require(isinstance(report, dict), "report must be a JSON object")
    head = git(repo, "rev-parse", "HEAD")
    require(SHA40.fullmatch(head) is not None, "Git HEAD is not a full SHA")
    require(not git(repo, "status", "--porcelain=v1", "--untracked-files=all"),
            "checkout has staged, modified, deleted or untracked files")
    version = project_version(repo)
    require(type(report.get("schemaVersion")) is int and report["schemaVersion"] == 1,
            "unsupported schemaVersion")
    require(report.get("release") == f"SECS v{version} — Cone", "release/version mismatch")
    require(report.get("candidateTag") == f"secs-v{version}", "candidateTag/version mismatch")
    require(report.get("headCommit") == head, "report headCommit differs from HEAD")
    for key in ("workingTreeClean", "trackedSourceClean", "sourceIdentityVerified", "releaseReady"):
        require(report.get(key) is True, f"{key} must be true")
    require(type(report.get("publicationVerified")) is bool, "publicationVerified must be boolean")
    require(isinstance(report.get("environment"), str) and bool(report["environment"].strip()),
            "environment is missing")
    try:
        checked_at = datetime.fromisoformat(report.get("checkedAt", ""))
    except (TypeError, ValueError) as error:
        raise InvalidReport("checkedAt must be an ISO timestamp") from error
    require(checked_at.tzinfo is not None and checked_at.utcoffset() is not None,
            "checkedAt must include a timezone offset")
    require(isinstance(report.get("blockers"), list) and not report["blockers"],
            "releaseReady report cannot have blockers")
    validate_artifacts(report, repo, report_path, version, head)
    validate_checks(report, repo)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path, help="local verification JSON under checkout/artifacts/")
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2],
                        help="Git checkout root (default: script's checkout)")
    args = parser.parse_args()
    try:
        validate(args.report, args.repo_root)
    except (InvalidReport, OSError, UnicodeError, json.JSONDecodeError, ET.ParseError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print(f"PASS: {args.report} matches the clean checkout and required evidence")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
