import hashlib
from pathlib import Path
import tempfile
import unittest

import check_vendor


class VendorTests(unittest.TestCase):
    def setUp(self):
        temporary = check_vendor.ROOT / "Benchmarks.Local/tmp"
        temporary.mkdir(parents=True, exist_ok=True)
        self.directory = tempfile.TemporaryDirectory(dir=temporary)
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        base = self.root / "probe"
        base.mkdir()
        data = b"Original license\n"
        (base / "LICENSE.md").write_bytes(data)
        self.component = {"id": "probe", "revision": "a" * 40, "license_file": "LICENSE.md",
                          "files": [{"path": "LICENSE.md", "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}]}

    def test_original_bytes(self):
        self.assertEqual(check_vendor.verify_component(self.component, self.root), {"Benchmarks.Local/vendor/probe/LICENSE.md"})

    def test_changed_source(self):
        (self.root / "probe/LICENSE.md").write_bytes(b"Changed license\n")
        with self.assertRaisesRegex(ValueError, "Changed pinned"):
            check_vendor.verify_component(self.component, self.root)

    def test_license_required(self):
        self.component["files"] = []
        with self.assertRaisesRegex(ValueError, "license is not included"):
            check_vendor.verify_component(self.component, self.root)

    def test_no_path_escape(self):
        self.component["files"][0]["path"] = "../LICENSE.md"
        with self.assertRaisesRegex(ValueError, "Unsafe manifest path"):
            check_vendor.verify_component(self.component, self.root)

    def test_case_matters(self):
        self.component["files"][0]["path"] = "license.md"
        with self.assertRaisesRegex(ValueError, "wrong-case"):
            check_vendor.verify_component(self.component, self.root)

    def test_duplicate_rejected(self):
        self.component["files"] *= 2
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            check_vendor.verify_component(self.component, self.root)


if __name__ == "__main__":
    unittest.main()
