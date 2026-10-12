"""Guard the supported web runtime and absence of retired build entry points."""

import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]


class MonoRuntimeTests(unittest.TestCase):
    def test_supported_project_is_stable_mono(self):
        project = ET.parse(ROOT / "SpaceBattle.Web/SpaceBattle.Web.csproj").getroot()
        properties = {child.tag: child.text for group in project.findall("PropertyGroup") for child in group}
        self.assertEqual(properties["TargetFramework"], "net8.0")
        self.assertEqual(properties["UseMonoRuntime"], "true")
        self.assertEqual(properties["PublishReadyToRun"], "false")
        self.assertEqual(properties["RunAOTCompilation"], "true")
        sdk = json.loads((ROOT / "SpaceBattle.Web/global.json").read_text(encoding="utf-8"))["sdk"]
        self.assertTrue(sdk["version"].startswith("8."))
        self.assertNotEqual(sdk.get("allowPrerelease"), True)
        self.assertEqual([item.attrib["Include"] for item in project.findall(".//PackageReference")],
                         ["Microsoft.AspNetCore.Components.WebAssembly"])
        self.assertFalse(project.findall(".//PackageDownload"))

    def test_retired_build_entry_points_are_absent(self):
        retired = (
            "SpaceBattle.Web.CoreClr", "tools/CoreClrR2RRoots", "tools/build-space-battle-coreclr.ps1",
            "tools/web/audit-coreclr-r2r.mjs", "tools/web/coreclr-profile.mjs",
            "tools/web/probe-coreclr-r2r.mjs", "tools/web/test-coreclr-r2r.mjs",
        )
        for relative in retired:
            with self.subTest(path=relative):
                self.assertFalse((ROOT / relative).exists())

    def test_shared_host_no_longer_accepts_retired_modes(self):
        bridge = (ROOT / "SpaceBattle.Web/BattleBridge.cs").read_text(encoding="utf-8")
        app = (ROOT / "SpaceBattle.Web/wwwroot/app.js").read_text(encoding="utf-8")
        for text in (bridge, app):
            self.assertNotIn("coreclr-r2r-binary", text)
            self.assertNotIn("coreclr-interpreter-binary", text)
            self.assertIn('"aot-binary"', text)
            self.assertIn('"interpreter-binary"', text)
        self.assertNotIn("WEB_CORECLR", bridge)

    def test_ci_requires_production_mono(self):
        workflow = (ROOT / ".github/workflows/space-battle-pages.yml").read_text(encoding="utf-8")
        self.assertIn("global-json-file: SpaceBattle.Web/global.json", workflow)
        self.assertIn("./tools/web/test-mono-runtime.ps1", workflow)
        self.assertIn("--require-mode aot-binary", workflow)
        self.assertNotIn("SpaceBattle.Web.CoreClr", workflow)


if __name__ == "__main__":
    unittest.main()
