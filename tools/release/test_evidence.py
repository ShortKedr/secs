"""Regression checks for binding release evidence to the exact package bytes."""

import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

from finalize_unity_zip import finalize
from prepare_release import REQUIRED_EVIDENCE, evidence_table
from unity_evidence import UNITY_KINDS, load_unity_evidence
from verify_unity_zip import verify


COMMIT = "a" * 40
RUN_ID = "12345"
VERSION = "0.2.0"


class EvidenceTests(unittest.TestCase):
    def test_all_consumer_evidence_must_match_package_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            package = root / "ShortKedr.SKECS.0.2.0.nupkg"
            dll = b"synthetic test assembly"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("lib/netstandard2.1/SKECS.dll", dll)
            license_file = root / "LICENSE"
            guide = root / "INSTALL.md"
            license_file.write_bytes(b"test license")
            guide.write_bytes(b"test guide")
            entries = {"lib/netstandard2.1/SKECS.dll": dll,
                       "LICENSE": license_file.read_bytes(), "INSTALL.md": guide.read_bytes()}
            manifest = {"package": "ShortKedr.SKECS", "version": VERSION,
                        "sourceCommit": COMMIT, "framework": "netstandard2.1",
                        "assemblyReferences": ["netstandard"],
                        "files": {name: hashlib.sha256(value).hexdigest() for name, value in entries.items()}}
            provisional = root / "provisional.zip"
            with zipfile.ZipFile(provisional, "w") as archive:
                for name, value in entries.items():
                    archive.writestr(name, value)
                archive.writestr("manifest.json", json.dumps(manifest))
            sha = hashlib.sha256(package.read_bytes()).hexdigest()
            evidence = root / "evidence"
            evidence.mkdir()
            for kind in REQUIRED_EVIDENCE:
                runtime = ("Unity 2022.3.62f1 (Editor)" if kind == "unity-2022.3-Editor"
                           else "Unity 6000.0.60f1 (Editor)" if kind == "unity-6-Editor"
                           else "Unity 2022.3.62f1 (Mono)" if kind == "unity-2022.3-Mono"
                           else "Unity 2022.3.62f1 (IL2CPP)" if kind == "unity-2022.3-IL2CPP"
                           else "Unity 6000.0.60f1 (Mono)" if kind == "unity-6-Mono"
                           else "Unity 6000.0.60f1 (IL2CPP)" if kind == "unity-6-IL2CPP"
                           else "Microsoft.NETCore.App 8.0.1" if kind == "dotnet8"
                           else "Microsoft.NETCore.App 10.0.1" if kind == "dotnet10"
                           else ".NET Framework 4.8-compatible (registry release 528040)" if kind == "framework"
                           else "Godot 4.5.stable")
                (evidence / f"{kind}.json").write_text(json.dumps({
                    "kind": kind, "packageVersion": VERSION, "sourceCommit": COMMIT,
                    "packageSha256": sha, "runId": RUN_ID, "runtime": runtime,
                    "os": "Windows 11", "result": "passed",
                }), encoding="utf-8")

            self.assertEqual(len(load_unity_evidence(evidence, VERSION, COMMIT, RUN_ID, package)),
                             len(UNITY_KINDS))
            self.assertIn("| .NET 8 |", evidence_table(evidence, VERSION, COMMIT, RUN_ID, package))
            output = root / "final.zip"
            finalize(package, provisional, output, license_file, guide, evidence,
                     VERSION, COMMIT, RUN_ID)
            verify(package, output, license_file, guide, VERSION, COMMIT,
                   True, evidence, RUN_ID)

            package.write_bytes(b"different package with same name")
            with self.assertRaisesRegex(ValueError, "packageSha256"):
                load_unity_evidence(evidence, VERSION, COMMIT, RUN_ID, package)
            with self.assertRaisesRegex(ValueError, "packageSha256"):
                evidence_table(evidence, VERSION, COMMIT, RUN_ID, package)


if __name__ == "__main__":
    unittest.main()
