"""Check delivered art/manifest without changing pixels or running a battle."""

import hashlib
import json
from pathlib import Path
import unittest

from PIL import Image


ASSETS = Path(__file__).resolve().parents[2] / "SpaceBattle.Game/Assets"


class GunAtlasTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((ASSETS / "guns-atlas.json").read_text())
        cls.image = Image.open(ASSETS / "guns-atlas.png")
        cls.alpha = cls.image.getchannel("A")

    @classmethod
    def tearDownClass(cls):
        cls.image.close()

    def test_dimensions_and_real_transparency(self):
        self.assertEqual(self.image.mode, "RGBA")
        self.assertEqual(self.image.size, (self.manifest["width"], self.manifest["height"]))
        self.assertEqual(self.manifest["alphaThreshold"], 32)
        self.assertEqual(self.alpha.getextrema()[0], 0)
        self.assertGreater(sum(a == 0 for a in self.alpha.getdata()), self.image.width * self.image.height // 2)

    def test_complete_unique_weapon_mapping(self):
        self.assertEqual(sorted(s["kind"] for s in self.manifest["sprites"]), [-1, 0, 1, 2, 3, 4])

    def test_measured_rectangles_and_bearing_pivots(self):
        for s in self.manifest["sprites"]:
            with self.subTest(kind=s["kind"]):
                self.assertTrue(all(isinstance(s[key], int) for key in ("x", "y", "width", "height")))
                self.assertGreater(s["width"], 0)
                self.assertGreater(s["height"], 0)
                self.assertGreaterEqual(s["x"], 0)
                self.assertGreaterEqual(s["y"], 0)
                self.assertLessEqual(s["x"] + s["width"], self.image.width)
                self.assertLessEqual(s["y"] + s["height"], self.image.height)
                self.assertTrue(0 <= s["pivotX"] < s["width"])
                self.assertTrue(0 <= s["pivotY"] < s["height"])
                self.assertGreater(self.alpha.getpixel((s["x"] + s["pivotX"], s["y"] + s["pivotY"])), 220)

    def test_transparent_padding_and_no_overlapping_crops(self):
        rectangles = []
        for s in self.manifest["sprites"]:
            rect = (s["x"], s["y"], s["x"] + s["width"], s["y"] + s["height"])
            crop = self.alpha.crop(rect).point(lambda a: 255 if a > 32 else 0)
            bounds = crop.getbbox()
            self.assertIsNotNone(bounds)
            self.assertEqual(bounds, (4, 4, s["width"] - 4, s["height"] - 4))
            for other in rectangles:
                self.assertFalse(rect[0] < other[2] and other[0] < rect[2] and rect[1] < other[3] and other[1] < rect[3])
            rectangles.append(rect)

    def test_distinct_heads_and_long_heavy_barrel(self):
        hashes = set()
        for s in self.manifest["sprites"]:
            crop = self.image.crop((s["x"], s["y"], s["x"] + s["width"], s["y"] + s["height"]))
            hashes.add(hashlib.sha256(crop.tobytes()).digest())
        self.assertEqual(len(hashes), 6)
        heavy = next(s for s in self.manifest["sprites"] if s["kind"] == 4)
        cannon = next(s for s in self.manifest["sprites"] if s["kind"] == 1)
        self.assertGreater(heavy["pivotY"] / heavy["width"], cannon["pivotY"] / cannon["width"])

    def test_both_frontends_publish_the_same_sources(self):
        project = ASSETS.parents[1]
        web = (project / "SpaceBattle.Web/SpaceBattle.Web.csproj").read_text()
        game = (project / "SpaceBattle.Game/SpaceBattle.Game.csproj").read_text()
        for name in ("guns-atlas.png", "guns-atlas.json"):
            self.assertIn(f'../SpaceBattle.Game/Assets/{name}', web)
        self.assertIn('Update="Assets/**" CopyToOutputDirectory="PreserveNewest"', game)
        self.assertIn("transparent", (ASSETS / "guns-prompt.txt").read_text().lower())


if __name__ == "__main__":
    unittest.main()
