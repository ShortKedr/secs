"""Verify the exact licensed source subset, without network access or Git writes."""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
VENDOR = ROOT / "Benchmarks.Local/vendor"
MANIFEST = ROOT / "Benchmarks.Local/vendor-manifest.json"
PINS = [
    ("ecs", "LeoECS Classic", "Leopotam/ecs", "1213c31c9ef8325d48e74a52b345c4db242d038b", "MIT-Red OR proprietary", "src"),
    ("ecslite", "LeoECS Lite", "Leopotam/ecslite", "5053141d7d663c6b1855c8409ac93dfac807b239", "MIT-Red", "src"),
    ("morpeh", "Morpeh", "scellecs/morpeh", "9f6369384792a01d74df24efd35b08ae313e4f12", "MIT", "Scellecs.Morpeh/Core"),
    ("StaticEcs", "StaticEcs", "Felid-Force-Studios/StaticEcs", "23b1cf36d89497e8a126d38459d688e2b0a5dea3", "MIT", "Src"),
    ("StaticPack", "StaticPack", "Felid-Force-Studios/StaticPack", "7bfb6cf925901b9366dda884258d727a89fff76e", "MIT", "Src"),
]


def safe_path(base, relative):
    path = PurePosixPath(relative)
    if path.is_absolute() or not path.parts or any(p in (".", "..") or ":" in p or "\\" in p for p in path.parts):
        raise ValueError("Unsafe manifest path: " + relative)
    target = base.joinpath(*path.parts)
    if target.is_symlink() or not target.resolve().is_relative_to(base.resolve()):
        raise ValueError("Manifest path escapes its component: " + relative)
    current = base
    for part in path.parts:
        if current.is_dir() and part not in {p.name for p in current.iterdir()}:
            raise ValueError("Missing or wrong-case source: " + str(target))
        current /= part
    return target


def verify_component(component, root=VENDOR):
    name = component["id"]
    if not re.fullmatch(r"[A-Za-z0-9_-]+", name) or not re.fullmatch(r"[0-9a-f]{40}", component["revision"]):
        raise ValueError("Invalid component identity or revision")
    base = root / name
    expected = set()
    for item in component["files"]:
        if item["path"] in expected:
            raise ValueError("Duplicate manifest file: " + item["path"])
        expected.add(item["path"])
        target = safe_path(base, item["path"])
        data = target.read_bytes()
        if len(data) != item["bytes"] or hashlib.sha256(data).hexdigest() != item["sha256"]:
            raise ValueError("Changed pinned upstream file: " + str(target))
    if component["license_file"] not in expected:
        raise ValueError("Original license is not included: " + name)
    return {f"Benchmarks.Local/vendor/{name}/{path}" for path in expected}


def git_files():
    bundled = Path.home() / ".cache/codex-runtimes/codex-primary-runtime/dependencies/native/git/cmd/git.exe"
    git = str(bundled) if bundled.is_file() else "git"
    result = subprocess.run([git, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "Benchmarks.Local/vendor"],
                            cwd=ROOT, capture_output=True, check=True)
    # The index may still list deleted files while a vendor upgrade is uncommitted.
    return {path for path in result.stdout.decode("utf-8").split("\0")
            if path and (ROOT / path).is_file()}


def verify():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    if manifest["format"] != 1:
        raise ValueError("Unsupported vendor manifest format")
    expected, ids = set(), set()
    for component in manifest["components"]:
        if component["id"] in ids:
            raise ValueError("Duplicate component")
        ids.add(component["id"])
        pin = next((p for p in PINS if p[0] == component["id"]), None)
        if pin is None or (component["name"], component["repository"], component["revision"], component["license"], component["source_root"]) != (pin[1], "https://github.com/" + pin[2], pin[3], pin[4], pin[5]):
            raise ValueError("Component metadata differs from audited pin: " + component["id"])
        expected.update(verify_component(component))
    if ids != {pin[0] for pin in PINS}:
        raise ValueError("Missing or unexpected vendored framework/dependency")
    actual = git_files()
    if actual != expected:
        raise ValueError("Publishable vendor subset differs from manifest; missing=" + str(sorted(expected - actual)) + "; extra=" + str(sorted(actual - expected)))
    return len(ids), len(expected)


def record_from_archives():
    # Maintenance only: derive hashes from the original pinned archives, not edits.
    components = []
    for name, title, repo, revision, license_name, source_root in PINS:
        files = []
        with zipfile.ZipFile(ROOT / f"Benchmarks.Local/{name}.zip") as archive:
            prefix = f"{name}-{revision}/"
            for entry in sorted(archive.infolist(), key=lambda e: e.filename):
                if not entry.filename.startswith(prefix) or entry.is_dir():
                    continue
                relative = entry.filename[len(prefix):]
                if relative != "LICENSE.md" and not (relative.startswith(source_root + "/") and relative.endswith(".cs")):
                    continue
                data = archive.read(entry)
                target = safe_path(VENDOR / name, relative)
                if data != target.read_bytes():
                    raise ValueError("Local source differs from pinned archive: " + str(target))
                files.append({"path": relative, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
        components.append({"id": name, "name": title, "repository": f"https://github.com/{repo}", "revision": revision,
                           "license": license_name, "license_file": "LICENSE.md", "source_root": source_root, "files": files})
    MANIFEST.write_text(json.dumps({"format": 1, "distribution": "runtime-source-only", "components": components}, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--record-from-archives", action="store_true", help="Maintenance: regenerate hashes from existing pinned ZIP archives")
    options = parser.parse_args()
    try:
        if options.record_from_archives:
            record_from_archives()
        components, files = verify()
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print("FAIL: " + str(error), file=sys.stderr)
        return 1
    print(f"PASS: {components} pinned upstream components; {files} byte-identical sources/licenses; exact publishable subset")
    return 0


if __name__ == "__main__":
    sys.exit(main())
