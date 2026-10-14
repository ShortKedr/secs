"""Verify a downloaded Unity ZIP against the exact NuGet package and source."""

import argparse
import hashlib
import json
import sys
import zipfile
from pathlib import Path

from unity_evidence import load_unity_evidence, render_unity_matrix


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def verify(package: Path, unity_zip: Path, license_file: Path, guide: Path,
           version: str, commit: str, final: bool = False,
           evidence_dir: Path | None = None, run_id: str | None = None) -> None:
    expected = {"lib/netstandard2.1/SECS.dll", "LICENSE", "INSTALL.md", "manifest.json"}
    if final:
        expected.add("VERIFIED_UNITY.md")
    with zipfile.ZipFile(package) as source, zipfile.ZipFile(unity_zip) as archive:
        if source.testzip() or archive.testzip():
            raise ValueError("Package or Unity ZIP is corrupt")
        if set(archive.namelist()) != expected or len(archive.namelist()) != len(expected):
            raise ValueError(f"Unity ZIP must contain exactly {sorted(expected)}")
        dll = archive.read("lib/netstandard2.1/SECS.dll")
        if dll != source.read("lib/netstandard2.1/SECS.dll"):
            raise ValueError("Unity DLL differs from the package netstandard2.1 asset")
        if archive.read("LICENSE") != license_file.read_bytes():
            raise ValueError("Unity LICENSE differs from the repository")
        if archive.read("INSTALL.md") != guide.read_bytes():
            raise ValueError("Unity installation guide differs from the repository")
        manifest = json.loads(archive.read("manifest.json"))
        for key, value in (("package", "ShortKedr.SECS"), ("version", version),
                           ("sourceCommit", commit.lower()), ("framework", "netstandard2.1")):
            if manifest.get(key) != value:
                raise ValueError(f"Unity manifest {key} mismatch")
        if manifest.get("files") != {name: sha(archive.read(name)) for name in expected - {"manifest.json"}}:
            raise ValueError("Unity manifest file checksums do not match ZIP contents")
        if manifest.get("assemblyReferences") != ["netstandard"]:
            raise ValueError(f"Unexpected Unity DLL assembly references: {manifest.get('assemblyReferences')}")
        if final:
            if evidence_dir is None or run_id is None:
                raise ValueError("Final Unity ZIP verification requires six Unity evidence records and run ID")
            records = load_unity_evidence(evidence_dir, version, commit, run_id)
            if manifest.get("verification") != {"runId": run_id, "unityChecks": records}:
                raise ValueError("Final Unity manifest does not match the six verified runtime records")
            if archive.read("VERIFIED_UNITY.md") != render_unity_matrix(version, commit, run_id, records):
                raise ValueError("Final Unity ZIP does not contain the exact verified Editor patch/backend table")
        elif "verification" in manifest:
            raise ValueError("Provisional Unity ZIP cannot claim runtime verification")
    print(f"PASS: {unity_zip.name}: source and manifest match {package.name}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--unity-zip", type=Path, required=True)
    parser.add_argument("--license", type=Path, default=Path("LICENSE"))
    parser.add_argument("--guide", type=Path, default=Path("docs/secs/INSTALL.md"))
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--final", action="store_true")
    parser.add_argument("--evidence-dir", type=Path)
    parser.add_argument("--run-id")
    args = parser.parse_args()
    try:
        verify(args.package, args.unity_zip, args.license, args.guide,
               args.version, args.commit, args.final, args.evidence_dir, args.run_id)
    except (OSError, KeyError, ValueError, zipfile.BadZipFile, json.JSONDecodeError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
