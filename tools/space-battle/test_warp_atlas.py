"""Validate warp source pixels and the four quadrant crop/pivot contract."""

import copy
import json
from pathlib import Path
import unittest

from PIL import Image


PROJECT = Path(__file__).resolve().parents[2]
ASSETS = PROJECT / "SpaceBattle.Game/Assets"


def validate_warp_atlas(image, manifest):
    def require(condition, message):
        if not condition:
            raise ValueError(message)

    require(image.format in (None, "PNG") and image.mode == "RGBA", "Warp image must be an RGBA PNG")
    require(all(type(manifest[k]) is int for k in ("width", "height", "alphaThreshold")), "Integer dimensions/threshold required")
    require(image.size == (manifest["width"], manifest["height"]), "Dimensions must match actual PNG")
    require(manifest["alphaThreshold"] == 32, "alphaThreshold must be 32")
    require(image.width == image.height and image.width > 0 and image.width % 2 == 0, "Square 2x2 grid required")
    sprites = manifest["sprites"]
    require(len(sprites) == 4, "Exactly four sprites required")
    kinds = set()
    cell = image.width // 2
    alpha = image.getchannel("A")
    require(alpha.getextrema()[0] == 0 and alpha.getextrema()[1] > 32, "Real transparent and visible pixels required")
    for sprite in sprites:
        require(all(type(sprite[k]) is int for k in ("kind", "x", "y", "width", "height")), "Integer kind and crop required")
        kind = sprite["kind"]
        require(0 <= kind <= 3 and kind not in kinds, "Unique kinds 0 through 3 required")
        kinds.add(kind)
        require("family" not in sprite or sprite["family"] == "warp", "Optional family must be warp")
        x, y, w, h = (sprite[k] for k in ("x", "y", "width", "height"))
        require(w > 0 and w == h, "Square nonempty crop required")
        left, top = kind % 2 * cell, kind // 2 * cell
        require(left <= x and top <= y and x + w <= left + cell and y + h <= top + cell,
                "Crop must not cross into a neighboring quadrant")
        require(all(type(sprite[k]) in (int, float) for k in ("pivotX", "pivotY")), "Numeric pivots required")
        require(sprite["pivotX"] == w / 2 and sprite["pivotY"] == h / 2, "Crop-relative centered pivots required")
        crop = alpha.crop((x, y, x + w, y + h))
        require(crop.getextrema()[0] == 0 and crop.getextrema()[1] > 32, "Each crop needs transparent and visible pixels")


class WarpContractTests(unittest.TestCase):
    def setUp(self):
        self.image = Image.new("RGBA", (16, 16))
        self.manifest = {"width": 16, "height": 16, "alphaThreshold": 32, "sprites": []}
        for kind in range(4):
            x, y = kind % 2 * 8, kind // 2 * 8
            self.image.putpixel((x + 2, y + 2), (20, 120, 240, 200))
            self.manifest["sprites"].append({"kind": kind, "x": x, "y": y, "width": 8, "height": 8,
                                              "pivotX": 4, "pivotY": 4})

    def tearDown(self):
        self.image.close()

    def test_accepts_optional_family_and_crop_relative_pivot(self):
        validate_warp_atlas(self.image, self.manifest)
        for sprite in self.manifest["sprites"]:
            sprite.update(family="warp", x=sprite["x"] + 1, y=sprite["y"] + 1,
                          width=6, height=6, pivotX=3, pivotY=3)
        validate_warp_atlas(self.image, self.manifest)

    def test_rejects_invalid_contracts(self):
        changes = [
            (None, {"width": 18}), (None, {"alphaThreshold": 31}),
            (0, {"kind": 1}), (0, {"kind": True}), (0, {"family": "particle"}),
            (0, {"x": 1}), (1, {"x": 0}), (2, {"y": 0}),
            (0, {"width": 7}), (0, {"pivotX": 3}), (0, {"pivotY": float("nan")}),
        ]
        for index, change in changes:
            with self.subTest(index=index, change=change):
                manifest = copy.deepcopy(self.manifest)
                target = manifest if index is None else manifest["sprites"][index]
                target.update(change)
                with self.assertRaises(ValueError):
                    validate_warp_atlas(self.image, manifest)
        self.manifest["sprites"].pop()
        with self.assertRaises(ValueError):
            validate_warp_atlas(self.image, self.manifest)

    def test_rejects_missing_real_alpha_and_empty_crop(self):
        for mode, color in (("RGB", (0, 0, 0)), ("RGBA", (0, 0, 0, 255)), ("RGBA", (0, 0, 0, 0))):
            with self.subTest(mode=mode, color=color), Image.new(mode, (16, 16), color) as image:
                with self.assertRaises(ValueError):
                    validate_warp_atlas(image, self.manifest)
        self.image.putpixel((2, 2), (0, 0, 0, 0))
        with self.assertRaises(ValueError):
            validate_warp_atlas(self.image, self.manifest)


class WarpAssetTests(unittest.TestCase):
    def test_actual_png_and_manifest(self):
        manifest = json.loads((ASSETS / "warp-atlas.json").read_text(encoding="utf-8"))
        with Image.open(ASSETS / "warp-atlas.png") as image:
            self.assertEqual(image.format, "PNG")
            validate_warp_atlas(image, manifest)

    def test_native_right_hand_pack_fits_4096(self):
        sizes = []
        for name in ("ships", "guns", "effects", "warp"):
            with Image.open(ASSETS / f"{name}-atlas.png") as image:
                sizes.append(image.size)
        self.assertLessEqual(max(w for w, _ in sizes[:3]) + sizes[3][0], 4096)
        self.assertLessEqual(max(sum(h for _, h in sizes[:3]), sizes[3][1]), 4096)


if __name__ == "__main__":
    unittest.main()
