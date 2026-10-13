"""Self-contained cover validation tests; no renderer, benchmarks, or Git writes."""

import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image, ImageDraw, UnidentifiedImageError


SPEC = importlib.util.spec_from_file_location("cover", Path(__file__).with_name("cover.py"))
cover = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cover)
COMMIT = "a" * 40
SETTINGS = {"Seed": 7331, "Tick": 180, "ShipsPerTeam": 96,
            "Width": 960, "Height": 540, "Zoom": 2}
# Model the production browser's requests independently of the validator's constants.
STATIC_FILES = """index.html app.js styles.css observer-camera.js collider-debug.js
web-atlases.js build-identity.json assets/web-atlases.json assets/lucide.min.js
_framework/blazor.boot.json _framework/blazor.webassembly.js _framework/dotnet.native.wasm
_framework/SpaceBattle.Web.wasm _framework/SpaceBattle.Core.wasm
_framework/SpaceBattle.SECS.wasm _framework/SECS.wasm""".split()
IDENTITY_FILES = """app.js styles.css observer-camera.js collider-debug.js web-atlases.js
assets/web-atlases.json assets/lucide.min.js _framework/blazor.boot.json
_framework/blazor.webassembly.js""".split()


class CoverTests(unittest.TestCase):
    def setUp(self):
        temporary_root = cover.ROOT / "Benchmarks.Local/tmp"
        temporary_root.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(dir=temporary_root)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.settings = SETTINGS.copy()
        self.assembly_directory = self.root / "site"
        self.assembly_directory.mkdir()
        self.build_files = STATIC_FILES.copy()
        for name in self.build_files:
            file = self.assembly_directory / name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(("fixture:" + name).encode())
        self.bundle = {"schema": 1, "revision": "png-atlases-v1", "atlases": {}}
        for family, slots in (("ships", ("ships-team-0", "ships-team-1")),
                              ("guns", ("guns-team-0", "guns-team-1")),
                              ("effects", ("effects-clean",)), ("warp", ("warp-clean",))):
            textures = []
            for slot in slots:
                data = ("fixture texture:" + slot).encode()
                sha = hashlib.sha256(data).hexdigest()
                name = slot + "-" + sha[:20] + ".png"
                (self.assembly_directory / "assets" / name).write_bytes(data)
                self.build_files.append("assets/" + name)
                textures.append({"file": name, "sha256": sha, "width": 64, "height": 64})
            self.bundle["atlases"][family] = {"textures": textures}
        (self.assembly_directory / "assets/web-atlases.json").write_text(json.dumps(self.bundle))
        self.identity = {"schema": 1, "contract": "png-assets-v1", "runtime": "8.0.31",
                         "files": {name: cover.digest(self.assembly_directory / name).lower() for name in IDENTITY_FILES}}
        (self.assembly_directory / "build-identity.json").write_text(json.dumps(self.identity))
        self.png = self.root / "frame.png"
        self.make_scene(self.png)
        self.capture = self.capture_metadata(self.png)
        self.write_capture(self.png, self.capture)

    def make_scene(self, path, size=None, hud_only=False):
        image = Image.new("RGB", size or (self.settings["Width"], self.settings["Height"]))
        draw = ImageDraw.Draw(image)
        for index in range(32):
            x = 10 + index * 25
            y = 20 if hud_only else 180 + (index % 4) * 35
            draw.rectangle((x, y, x + 15, y + 15),
                           fill=(40 + index * 6, 240 - index * 4, 100 + index * 3))
        image.save(path, format="PNG")

    def capture_metadata(self, png):
        # Browser artifact fields, including raw timings that must not be published.
        counts = dict(Ships=30, RenderedShips=28, AliveCyan=20, AliveRed=16,
                      Projectiles=7, Debris=2, Particles=3, AliveEntities=53,
                      AliveShips=36, DockedShips=6, Turrets=3, AliveTurrets=5,
                      DockedTurrets=2, HeavyShots=4, TurretShots=3)
        return {
            "Tick": self.settings["Tick"], "Width": self.settings["Width"],
            "Height": self.settings["Height"], "Zoom": self.settings["Zoom"],
            "ConfiguredShipsPerTeam": self.settings["ShipsPerTeam"],
            "Seed": self.settings["Seed"], "Renderer": cover.RENDERER,
            "RuntimeMode": "aot-binary", "BrowserVersion": "fixture-chromium",
            "CaptureSurface": "CanvasOnly", "Profile": "nominal", "OfflineCapture": True,
            **counts, **cover.validate_frame(png, self.settings),
            "BrightPixels": 8192, "UniqueColors": 33,
            "Nonblank": True, "Atlas": True, "ControlsVerified": True,
            "CountsValid": True, "StatisticsValid": True,
            "BattleChecksum": "0123456789ABCDEF", "GameplayRevision": "fixture-revision",
            "PhysicsRevision": "fixture-physics", "FleetRevision": "fixture-fleet",
            "BuildFiles": {name: cover.digest(self.assembly_directory / name) for name in self.build_files},
            "LastTickMs": 1.25, "P99": 2.5,
            "Statistics": {"OfflineCapture": True, "SampleCount": 180, "Capacity": 600,
                           "LastMs": 1.25, "MeanMs": 1.5, "P95Ms": 2, "P99Ms": 2.5,
                           "MaximumMs": 3, "WallTicksPerSecond": 60, "RenderFps": 60,
                           "RenderSubmissionCpuMs": .2, "StageMeanMs": [1, .5]},
            "UnvalidatedCounter": 999,
        }

    def write_capture(self, path, metadata):
        Path(str(path) + ".json").write_text(json.dumps(metadata), encoding="utf-8")

    def write_build_manifest(self, name, value):
        (self.assembly_directory / name).write_text(json.dumps(value))
        if name == "assets/web-atlases.json":
            identity = cover.read_json(self.assembly_directory / "build-identity.json")
            identity["files"][name] = cover.digest(self.assembly_directory / name).lower()
            (self.assembly_directory / "build-identity.json").write_text(json.dumps(identity))
        for manifest in ("assets/web-atlases.json", "build-identity.json"):
            self.capture["BuildFiles"][manifest] = cover.digest(self.assembly_directory / manifest)

    def invoke_capture(self, output, metadata=None, returncode=0, dirty=False):
        def browser(command, **kwargs):
            self.assertEqual(command[:2], ["node", str(cover.ROOT / "tools/web/capture-cover.mjs")])
            self.assertEqual(kwargs, dict(cwd=cover.ROOT, capture_output=True,
                                         text=True, timeout=300))
            self.assertEqual(command[command.index("--site") + 1], str(self.assembly_directory))
            self.assertEqual(cover.read_json(command[command.index("--settings") + 1]), self.settings)
            self.assertEqual(command[command.index("--base") + 1], "/")
            destination = Path(command[command.index("--output") + 1])
            self.assertEqual(destination, output / "frame.png")
            self.assertFalse((output / "cover.png").exists())
            self.assertFalse((output / "cover.png.json").exists())
            self.make_scene(destination)
            self.write_capture(destination, metadata if metadata is not None else self.capture)
            return subprocess.CompletedProcess(command, returncode, "browser stdout\n", "browser stderr\n")

        with patch.object(cover, "source_state", return_value=dirty) as provenance, \
                patch.object(cover.subprocess, "run", side_effect=browser) as run, \
                patch("sys.stdout", new_callable=io.StringIO), patch("sys.stderr", new_callable=io.StringIO):
            cover.capture_cover(self.assembly_directory, output, self.settings, COMMIT)
            self.assertEqual(provenance.call_count, 2)
            self.assertEqual(provenance.call_args.args, (COMMIT,))
            run.assert_called_once()

    def published_fixture(self):
        output = self.root / "published"
        self.invoke_capture(output)
        return output, cover.read_json(output / "cover.png.json")

    def test_settings_accept_boundaries(self):
        for key, values in {"Seed": (0, 2147483647), "Tick": (1, 108000),
                            "ShipsPerTeam": (96,), "Width": (960, 4096),
                            "Height": (540, 4096), "Zoom": (.25, 24)}.items():
            for value in values:
                with self.subTest(key=key, value=value):
                    settings = {**self.settings, key: value}
                    self.assertEqual(cover.validate_settings(settings), settings)

    def test_settings_reject_types_ranges_and_nonfinite_zoom(self):
        ranges = {"Seed": (-1, 2147483648), "Tick": (0, 108001),
                  "ShipsPerTeam": (95, 97), "Width": (959, 4097),
                  "Height": (539, 4097), "Zoom": (.249, 24.001)}
        for key, outside in ranges.items():
            invalid = [*outside, True, False, None, "2", [], float("nan"),
                       float("inf"), -float("inf")]
            if key != "Zoom":
                invalid.append(float(self.settings[key]))
            for value in invalid:
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    cover.validate_settings({**self.settings, key: value})
        for settings in ({k: v for k, v in self.settings.items() if k != "Seed"},
                         {**self.settings, "Unexpected": 1}):
            with self.assertRaises(ValueError):
                cover.validate_settings(settings)

    def test_frame_accepts_colorful_scene(self):
        pixels = cover.validate_frame(self.png, self.settings)
        self.assertEqual(pixels, {"SceneBrightPixels": 32 * 16 * 16,
                                  "SceneUniqueColors": 33})

    def test_frame_rejects_black_monochrome_and_hud_only(self):
        for color in ("black", "white"):
            with self.subTest(color=color):
                Image.new("RGB", (960, 540), color).save(self.png)
                with self.assertRaisesRegex(ValueError, "Blank or monochrome"):
                    cover.validate_frame(self.png, self.settings)
        self.make_scene(self.png, hud_only=True)
        with self.assertRaisesRegex(ValueError, "Blank or monochrome"):
            cover.validate_frame(self.png, self.settings)

    def test_frame_rejects_wrong_dimensions_format_and_corruption(self):
        for size in ((961, 540), (960, 541)):
            self.make_scene(self.png, size=size)
            with self.assertRaisesRegex(ValueError, "dimensions or format"):
                cover.validate_frame(self.png, self.settings)
        Image.new("RGB", (960, 540), "white").save(self.png, format="JPEG")
        with self.assertRaisesRegex(ValueError, "dimensions or format"):
            cover.validate_frame(self.png, self.settings)
        self.png.write_bytes(b"not a PNG")
        with self.assertRaises((OSError, UnidentifiedImageError)):
            cover.validate_frame(self.png, self.settings)

    def test_counts_accept_active_battle_with_docked_ships_and_turrets(self):
        cover.validate_counts(self.capture)

    def test_counts_reject_invalid_types_and_negative_values(self):
        for key in cover.COUNTS:
            for value in (-1, True, 1.0, "1", None):
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    cover.validate_counts({**self.capture, key: value})

    def test_counts_reject_population_and_docking_inconsistencies(self):
        for key, value in {"RenderedShips": 31, "Ships": 37, "AliveRed": 17,
                           "DockedShips": 5, "DockedTurrets": 1, "Turrets": 6,
                           "AliveEntities": 52}.items():
            with self.subTest(key=key), self.assertRaises(ValueError):
                cover.validate_counts({**self.capture, key: value})

    def test_counts_reject_consistent_but_inactive_battle(self):
        cases = [{"RenderedShips": 23}, {"AliveCyan": 0, "AliveRed": 36},
                 {"AliveRed": 0, "AliveCyan": 36}, {"Projectiles": 0, "AliveEntities": 46}]
        for changes in cases:
            with self.subTest(changes=changes), self.assertRaisesRegex(ValueError, "active two-sided"):
                cover.validate_counts({**self.capture, **changes})

    def test_capture_accepts_real_files_and_metadata(self):
        for family in ("ships", "guns", "effects", "warp"):
            for suffix in ("png", "json"):
                self.assertNotIn(f"assets/{family}-atlas.{suffix}", self.capture["BuildFiles"])
        capture, pixels = cover.validate_capture(self.png, self.settings, self.assembly_directory)
        self.assertEqual(capture, self.capture)
        self.assertEqual(pixels, cover.validate_frame(self.png, self.settings))

    def test_capture_rejects_settings_flags_checksum_revision_and_pixels(self):
        invalid = {"Tick": 181, "Width": 961, "Height": 541, "Zoom": 3,
                   "ConfiguredShipsPerTeam": 97, "Seed": 7332, "BattleChecksum": "invalid",
                   "RuntimeMode": "interpreter-binary", "CaptureSurface": "Page", "Profile": "high",
                   "GameplayRevision": "", "SceneBrightPixels": 1, "SceneUniqueColors": 1}
        invalid.update({key: False for key in ("Nonblank", "Atlas", "ControlsVerified",
                                               "CountsValid", "StatisticsValid")})
        for key, value in invalid.items():
            with self.subTest(key=key):
                self.write_capture(self.png, {**self.capture, key: value})
                with self.assertRaises(ValueError):
                    cover.validate_capture(self.png, self.settings, self.assembly_directory)
        metadata = copy.deepcopy(self.capture)
        metadata["OfflineCapture"] = False
        self.write_capture(self.png, metadata)
        with self.assertRaisesRegex(ValueError, "OfflineCapture"):
            cover.validate_capture(self.png, self.settings, self.assembly_directory)

    def test_capture_rejects_each_changed_binary(self):
        for name in self.build_files:
            path = self.assembly_directory / name
            original = path.read_bytes()
            with self.subTest(binary=name):
                path.write_bytes(original + b"tampered")
                with self.assertRaisesRegex(ValueError, "build mismatch"):
                    cover.validate_capture(self.png, self.settings, self.assembly_directory)
                path.write_bytes(original)

    def test_capture_requires_current_modules_manifests_and_each_generated_texture(self):
        for name in self.build_files:
            files = self.capture["BuildFiles"].copy()
            del files[name]
            with self.subTest(file=name), self.assertRaisesRegex(ValueError, "Missing captured"):
                cover.validate_build_files(files)

    def test_capture_rejects_atlas_filename_hash_mismatch_and_duplicate_slot(self):
        name = "assets/" + self.bundle["atlases"]["ships"]["textures"][0]["file"]
        files = self.capture["BuildFiles"].copy()
        files[name] = "A" * 64
        with self.assertRaisesRegex(ValueError, "filename/hash mismatch"):
            cover.validate_build_files(files)
        files = self.capture["BuildFiles"].copy()
        files["assets/ships-team-0-" + "b" * 20 + ".png"] = "B" * 64
        with self.assertRaisesRegex(ValueError, "Duplicate captured atlas"):
            cover.validate_build_files(files)

    def test_capture_rejects_build_identity_mismatches(self):
        cases = [None, {**self.identity, "schema": 2}, {**self.identity, "contract": "old"},
                 {**self.identity, "files": {}},
                 {**self.identity, "files": {**self.identity["files"], "app.js": "a" * 64}},
                 {**self.identity, "files": {**self.identity["files"], "unloaded.js": "a" * 64}}]
        for identity in cases:
            with self.subTest(identity=identity), self.assertRaisesRegex(ValueError, "identity"):
                self.write_build_manifest("build-identity.json", identity)
                cover.validate_build_files(self.capture["BuildFiles"], self.assembly_directory)

    def test_capture_rejects_incomplete_or_mismatched_atlas_manifest(self):
        cases = [None, {**self.bundle, "schema": 2}, {**self.bundle, "revision": "old"},
                 {**self.bundle, "atlases": {}}]
        for family in ("ships", "guns", "effects", "warp"):
            bundle = copy.deepcopy(self.bundle)
            bundle["atlases"][family]["textures"].pop()
            cases.append(bundle)
        for field, value in (("sha256", "bad"), ("sha256", "a" * 64), ("file", "../secret.png")):
            bundle = copy.deepcopy(self.bundle)
            bundle["atlases"]["ships"]["textures"][0][field] = value
            cases.append(bundle)
        bundle = copy.deepcopy(self.bundle)
        bundle["atlases"]["ships"]["textures"][1] = bundle["atlases"]["ships"]["textures"][0].copy()
        cases.append(bundle)
        bundle = copy.deepcopy(self.bundle)
        bundle["atlases"]["effects"], bundle["atlases"]["warp"] = bundle["atlases"]["warp"], bundle["atlases"]["effects"]
        cases.append(bundle)
        for bundle in cases:
            with self.subTest(bundle=bundle), self.assertRaisesRegex(ValueError, "atlas|Atlas"):
                self.write_build_manifest("assets/web-atlases.json", bundle)
                cover.validate_build_files(self.capture["BuildFiles"], self.assembly_directory)

    def test_capture_rejects_unvalidated_counter(self):
        self.write_capture(self.png, {**self.capture, "DockedTurrets": 99, "CountsValid": True})
        with self.assertRaisesRegex(ValueError, "docked turret"):
            cover.validate_capture(self.png, self.settings, self.assembly_directory)

    def test_source_state_rejects_bad_and_wrong_commit_without_git_writes(self):
        with patch.object(cover.subprocess, "run") as run:
            for commit in ("", "a" * 39, "G" * 40, "A" * 40):
                with self.subTest(commit=commit), self.assertRaisesRegex(ValueError, "Invalid source commit"):
                    cover.source_state(commit)
            run.assert_not_called()
        with patch.object(cover.shutil, "which", return_value="git"), \
                patch.object(cover.subprocess, "run", return_value=subprocess.CompletedProcess(
                    [], 0, "b" * 40, "")) as run:
            with self.assertRaisesRegex(ValueError, "differs from the local checkout"):
                cover.source_state(COMMIT)
            self.assertEqual(run.call_args.args[0], ["git", "rev-parse", "HEAD"])

    def test_cover_accepts_matching_commit_and_rejects_bad_provenance(self):
        output, manifest = self.published_fixture()
        self.assertEqual(cover.validate_cover(output, self.settings, COMMIT), manifest)
        for changes in ({"SourceCommit": "bad"}, {"SourceCommit": "b" * 40},
                        {"SourceDirty": True}, {"SourceDirty": 1}):
            with self.subTest(changes=changes):
                (output / "cover.png.json").write_text(json.dumps({**manifest, **changes}))
                with self.assertRaises(ValueError):
                    cover.validate_cover(output, self.settings, COMMIT)
        (output / "cover.png.json").write_text(json.dumps({**manifest, "SourceDirty": True}))
        self.assertTrue(cover.validate_cover(output, self.settings, COMMIT, allow_dirty=True)["SourceDirty"])

    def test_cover_rejects_changed_expected_size_seed_and_tick(self):
        output, _ = self.published_fixture()
        for key in ("Width", "Height", "Seed", "Tick"):
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "settings differ"):
                cover.validate_cover(output, {**self.settings, key: self.settings[key] + 1})

    def test_cover_rejects_tampered_png_and_pixel_evidence(self):
        output, manifest = self.published_fixture()
        png = output / "cover.png"
        with Image.open(png) as image:
            image.load()
            image.putpixel((0, 200), (255, 255, 255))
            image.save(png)
        with self.assertRaisesRegex(ValueError, "PNG checksum mismatch"):
            cover.validate_cover(output, self.settings)
        manifest["PngSha256"] = cover.digest(png)
        (output / "cover.png.json").write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, "pixel evidence mismatch"):
            cover.validate_cover(output, self.settings)

    def test_capture_writes_only_local_output_and_filters_manifest(self):
        output = self.root / "output"
        write_text, copyfile = Path.write_text, cover.shutil.copyfile

        def guarded_write(path, *args, **kwargs):
            self.assertTrue(path.resolve().is_relative_to(output))
            return write_text(path, *args, **kwargs)

        def guarded_copy(source, destination, *args, **kwargs):
            self.assertTrue(Path(destination).resolve().is_relative_to(output))
            return copyfile(source, destination, *args, **kwargs)

        with patch.object(Path, "write_text", guarded_write), \
                patch.object(cover.shutil, "copyfile", guarded_copy):
            self.invoke_capture(output)
        self.assertEqual((output / "capture.log").read_text(), "browser stdout\nbrowser stderr\n")
        self.assertEqual((output / "cover.png").read_bytes(), (output / "frame.png").read_bytes())
        manifest = cover.validate_cover(output, self.settings, COMMIT)
        expected = {"SchemaVersion", "Purpose", "SourceCommit", "SourceDirty", "Renderer",
                    "Settings", "PngSha256", "GameplayRevision", "BattleChecksum", "Atlas",
                    "ControlsVerified", "CountsValid", "StatisticsValid", "SceneBrightPixels",
                    "SceneUniqueColors", "RuntimeMode", "BrowserVersion", "CaptureSurface", "Profile", "BuildFiles",
                    "PhysicsRevision", "FleetRevision", "Nonblank", "OfflineCapture", *cover.COUNTS}
        self.assertEqual(set(manifest), expected)
        for key in ("Statistics", "LastTickMs", "P99", "UnvalidatedCounter", "BrightPixels", "UniqueColors"):
            self.assertNotIn(key, manifest)
        self.assertEqual(set(manifest["Settings"]), set(SETTINGS))

    def test_capture_never_publishes_failed_browser_or_invalid_capture(self):
        cases = [("process", self.capture, 1),
                 ("atlas", {**self.capture, "Atlas": False}, 0),
                 ("counter", {**self.capture, "AliveEntities": 1}, 0),
                 ("pixels", {**self.capture, "SceneBrightPixels": 1}, 0)]
        for name, metadata, returncode in cases:
            output = self.root / name
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.invoke_capture(output, metadata, returncode)
            self.assertTrue((output / "capture.log").is_file())
            self.assertFalse((output / "cover.png").exists())
            self.assertFalse((output / "cover.png.json").exists())

    def test_workflow_publication_is_guarded_and_narrow(self):
        workflow = (cover.ROOT / ".github/workflows/space-battle-cover.yml").read_text()
        self.assertRegex(workflow, r"push:\s+branches: \[main\]")
        self.assertIn("-p test_cover.py", workflow)
        self.assertIn("wasm-tools", workflow)
        self.assertIn("build-space-battle-web.ps1", workflow)
        self.assertIn("playwright install --with-deps chromium", workflow)
        self.assertNotIn("xvfb-run", workflow)
        self.assertIn("needs: capture", workflow)
        self.assertIn("git add -- docs/space-battle/cover.png docs/space-battle/cover.png.json", workflow)
        self.assertIn("git push origin HEAD:refs/heads/main", workflow)
        self.assertNotRegex(workflow, r"--force(?:-with-lease)?|git push[^\n]*\s-f(?:\s|$)|secrets\.")
        guard = workflow.index('if [ "$(git rev-parse FETCH_HEAD)" != "$GITHUB_SHA" ]; then')
        self.assertLess(workflow.index("Validate the downloaded image"), guard)
        self.assertLess(guard, workflow.index("cp Benchmarks.Local/tmp/space-battle-cover/cover.png "))

    def test_cover_rejects_missing_files_and_unsafe_build_paths(self):
        for files in ({}, {**self.capture["BuildFiles"], "../secret.txt": "A" * 64},
                      {**self.capture["BuildFiles"], "index.html": "bad"}):
            with self.subTest(files=files), self.assertRaises(ValueError):
                cover.validate_build_files(files)

    def test_capture_rejects_source_change_during_render(self):
        output = self.root / "changed-source"
        with patch.object(cover, "source_state", side_effect=[False, True]), \
                patch.object(cover.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "", "")), \
                self.assertRaisesRegex(ValueError, "Source state changed"):
            cover.capture_cover(self.assembly_directory, output, self.settings, COMMIT)
        self.assertFalse((output / "cover.png").exists())


if __name__ == "__main__":
    unittest.main()
