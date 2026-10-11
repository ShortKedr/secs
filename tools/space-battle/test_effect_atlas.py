"""Validate generated VFX source pixels and the shared frontend contract."""

import hashlib
import json
from pathlib import Path
import unittest

from PIL import Image


PROJECT = Path(__file__).resolve().parents[2]
ASSETS = PROJECT / "SpaceBattle.Game/Assets"


class EffectAtlasTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((ASSETS / "effects-atlas.json").read_text())
        cls.image = Image.open(ASSETS / "effects-atlas.png")
        cls.alpha = cls.image.getchannel("A")

    @classmethod
    def tearDownClass(cls):
        cls.image.close()

    def test_dimensions_and_real_transparency(self):
        self.assertEqual(self.image.mode, "RGBA")
        self.assertEqual(self.image.size, (self.manifest["width"], self.manifest["height"]))
        self.assertEqual(self.manifest["alphaThreshold"], 32)
        self.assertEqual(self.alpha.getextrema(), (0, 255))
        self.assertGreater(sum(a == 0 for a in self.alpha.getdata()), self.image.width * self.image.height // 2)

    def test_complete_unique_family_mapping(self):
        actual = sorted((s["family"], s["kind"]) for s in self.manifest["sprites"])
        expected = sorted([("projectile", k) for k in range(5)] + [("particle", k) for k in range(4)])
        self.assertEqual(actual, expected)

    def test_bounds_padding_pivots_and_no_overlap(self):
        rectangles = []
        for s in self.manifest["sprites"]:
            with self.subTest(family=s["family"], kind=s["kind"]):
                self.assertTrue(all(isinstance(s[k], int) for k in ("x", "y", "width", "height", "pivotX", "pivotY")))
                self.assertGreater(s["width"], 0)
                self.assertGreater(s["height"], 0)
                self.assertGreaterEqual(s["x"], 0)
                self.assertGreaterEqual(s["y"], 0)
                rect = (s["x"], s["y"], s["x"] + s["width"], s["y"] + s["height"])
                self.assertLessEqual(rect[2], self.image.width)
                self.assertLessEqual(rect[3], self.image.height)
                self.assertTrue(0 <= s["pivotX"] < s["width"])
                self.assertTrue(0 <= s["pivotY"] < s["height"])
                self.assertGreater(self.alpha.getpixel((s["x"] + s["pivotX"], s["y"] + s["pivotY"])), 32)
                crop = self.alpha.crop(rect).point(lambda a: 255 if a > 32 else 0)
                bbox = crop.getbbox()
                self.assertIsNotNone(bbox)
                if s["family"] == "projectile":
                    self.assertEqual(bbox, (4, 4, s["width"] - 4, s["height"] - 4))
                else:
                    # Square cells retain transparent padding around non-square particle artwork.
                    self.assertGreaterEqual(min(bbox[0], bbox[1], s["width"] - bbox[2], s["height"] - bbox[3]), 4)
                for other in rectangles:
                    self.assertFalse(rect[0] < other[2] and other[0] < rect[2] and rect[1] < other[3] and other[1] < rect[3])
                rectangles.append(rect)

    def test_distinct_sprites_and_projectile_head_anchors(self):
        hashes = set()
        for s in self.manifest["sprites"]:
            crop = self.image.crop((s["x"], s["y"], s["x"] + s["width"], s["y"] + s["height"]))
            hashes.add(hashlib.sha256(crop.tobytes()).digest())
            if s["family"] == "projectile":
                self.assertGreater(s["width"], s["height"])
                self.assertGreater(s["pivotX"], s["width"] / 2)
        self.assertEqual(len(hashes), 9)

    def test_soft_particle_alpha_is_not_a_solid_rectangle(self):
        for s in self.manifest["sprites"]:
            if s["family"] != "particle":
                continue
            with self.subTest(kind=s["kind"]):
                a = self.alpha.crop((s["x"], s["y"], s["x"] + s["width"], s["y"] + s["height"]))
                self.assertTrue(any(32 < v < 220 for v in a.getdata()))
                self.assertGreater(sum(v == 0 for v in a.getdata()), 0)

    def test_native_combined_texture_fits_4096(self):
        manifests = [json.loads((ASSETS / name).read_text()) for name in ("ships-atlas.json", "guns-atlas.json", "effects-atlas.json")]
        self.assertLessEqual(max(m["width"] for m in manifests), 4096)
        self.assertLessEqual(sum(m["height"] for m in manifests), 4096)

    def test_particle_cells_are_square_and_centered(self):
        for s in self.manifest["sprites"]:
            if s["family"] == "particle":
                with self.subTest(kind=s["kind"]):
                    self.assertEqual(s["width"], s["height"])
                    self.assertEqual(s["pivotX"] * 2, s["width"])
                    self.assertEqual(s["pivotY"] * 2, s["height"])

    def test_shared_sources_and_saved_prompt(self):
        web = (PROJECT / "SpaceBattle.Web/SpaceBattle.Web.csproj").read_text()
        game = (PROJECT / "SpaceBattle.Game/SpaceBattle.Game.csproj").read_text()
        for name in ("effects-atlas.png", "effects-atlas.json"):
            self.assertIn(f'../SpaceBattle.Game/Assets/{name}', web)
        self.assertIn('Update="Assets/**" CopyToOutputDirectory="PreserveNewest"', game)
        self.assertIn("transparent", (ASSETS / "effects-prompt.txt").read_text().lower())

    def test_contrast_snapshot_matches_current_artwork_and_budgets(self):
        snapshot = json.loads((ASSETS / "effects-contrast-checks.json").read_text())
        image_hash = hashlib.sha256((ASSETS / "effects-atlas.png").read_bytes()).hexdigest().upper()
        manifest = json.dumps(self.manifest, sort_keys=True, separators=(",", ":")).encode("ascii")
        self.assertEqual(snapshot["source"]["imageSha256"], image_hash)
        self.assertEqual(snapshot["source"]["manifestCanonicalSha256"], hashlib.sha256(manifest).hexdigest().upper())
        self.assertEqual(snapshot["protocol"]["candidateScenePairs"], 12)
        self.assertEqual(snapshot["protocol"]["pairedSpriteProbes"], 9)
        for key, limit in (("meanAddedY", 0.03), ("hotFraction", 0.01), ("tileHotFraction", 0.25)):
            self.assertEqual(snapshot["guardrails"][key], limit)
            self.assertLessEqual(snapshot["candidateMaxima"][key], limit)
        self.assertEqual({r["family"] for r in snapshot["familyComparison"]}, {"projectile", "particle"})
        for result in snapshot["familyComparison"]:
            self.assertGreaterEqual(result["ratio"], 1.1)
        self.assertTrue(snapshot["checksumAndCountParity"])
        self.assertEqual(snapshot["browserErrors"], 0)
        self.assertTrue(snapshot["passed"])
        self.assertIn("contrast", (ASSETS / "effects-contrast-prompt.txt").read_text().lower())


if __name__ == "__main__":
    unittest.main()
