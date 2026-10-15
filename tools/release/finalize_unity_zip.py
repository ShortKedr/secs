"""Add exact successful Unity Editor/backend results after the CI Unity matrix."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import zipfile
from pathlib import Path

from unity_evidence import load_unity_evidence, render_unity_matrix
from verify_unity_zip import verify


def finalize(package: Path, provisional: Path, output: Path, license_file: Path,
             guide: Path, evidence_dir: Path, version: str, commit: str, run_id: str) -> None:
    if provisional.resolve() == output.resolve():
        raise ValueError("Final ZIP output must differ from the tested provisional ZIP")
    # The input ZIP is exactly the binary that all six Unity jobs imported.
    verify(package, provisional, license_file, guide, version, commit)
    records = load_unity_evidence(evidence_dir, version, commit, run_id, package)
    with zipfile.ZipFile(provisional) as source:
        entries = {name: source.read(name) for name in ("lib/netstandard2.1/SKECS.dll", "LICENSE", "INSTALL.md")}
        manifest = json.loads(source.read("manifest.json"))
    entries["VERIFIED_UNITY.md"] = render_unity_matrix(version, commit, run_id, records)
    manifest["verification"] = {"runId": run_id, "packageSha256": hashlib.sha256(package.read_bytes()).hexdigest(),
                                "unityChecks": records}
    manifest["files"] = {
        name: hashlib.sha256(contents).hexdigest()
        for name, contents in sorted(entries.items())
    }
    entries["manifest.json"] = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")

    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w") as archive:
        for name, contents in sorted(entries.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, contents)
    verify(package, output, license_file, guide, version, commit, True, evidence_dir, run_id)
    print(f"PASS: finalized {output.name} with six exact Unity runtime results; DLL unchanged")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--provisional", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--license", type=Path, default=Path("LICENSE"))
    parser.add_argument("--guide", type=Path, default=Path("docs/skecs/INSTALL.md"))
    parser.add_argument("--evidence-dir", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--run-id", required=True)
    args = parser.parse_args()
    try:
        finalize(args.package, args.provisional, args.output, args.license, args.guide,
                 args.evidence_dir, args.version, args.commit, args.run_id)
    except (OSError, KeyError, ValueError, json.JSONDecodeError, zipfile.BadZipFile) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
