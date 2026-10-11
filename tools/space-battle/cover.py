"""Capture and validate the real native frontend; never publish or run benchmarks."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import sys

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SETTINGS = ROOT / "docs/space-battle/cover-settings.json"
PURPOSE = "current-gameplay-cover-not-benchmark"
BINARY_FIELDS = {
    "GameDllSha256": "SpaceBattle.Game.dll",
    "CoreDllSha256": "SpaceBattle.Core.dll",
    "SecsAdapterDllSha256": "SpaceBattle.SECS.dll",
}
COUNTS = "Ships RenderedShips AliveCyan AliveRed Projectiles Debris Particles AliveEntities AliveShips DockedShips Turrets AliveTurrets DockedTurrets HeavyShots TurretShots".split()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def project_path(path):
    path = Path(path).resolve()
    require(path.is_relative_to(ROOT) and path != ROOT, "Paths must stay inside the current project")
    return path


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()


def validate_settings(settings):
    require(set(settings) == {"Seed", "Tick", "ShipsPerTeam", "Width", "Height", "Zoom"}, "Unexpected cover settings")
    for key, low, high in (("Seed", 0, 2147483647), ("Tick", 1, 108000),
                           ("ShipsPerTeam", 8, 10000), ("Width", 960, 4096), ("Height", 540, 4096)):
        require(type(settings[key]) is int and low <= settings[key] <= high, "Invalid setting " + key)
    zoom = settings["Zoom"]
    require(type(zoom) in (int, float) and math.isfinite(zoom) and .25 <= zoom <= 24, "Invalid setting Zoom")
    return settings


def validate_frame(path, settings):
    with Image.open(path) as image:
        require(image.format == "PNG" and image.size == (settings["Width"], settings["Height"]), "Incorrect PNG dimensions or format")
        image.load()
        # Exclude the native HUD so text alone cannot pass the scene check.
        scene = image.convert("RGB").crop((0, 140, image.width, image.height - 80))
        colors = scene.getcolors(scene.width * scene.height)
        bright = sum(count for count, rgb in colors if sum(rgb) > 100)
        require(bright > 100 and len(colors) > 16, "Blank or monochrome battle scene")
    return {"SceneBrightPixels": bright, "SceneUniqueColors": len(colors)}


def validate_counts(capture):
    for key in COUNTS:
        require(type(capture[key]) is int and capture[key] >= 0, "Invalid count " + key)
    require(capture["RenderedShips"] <= capture["Ships"] <= capture["AliveShips"], "Invalid visible ship counts")
    require(capture["AliveCyan"] + capture["AliveRed"] == capture["AliveShips"], "Invalid team population")
    require(capture["DockedShips"] == capture["AliveShips"] - capture["Ships"], "Invalid docked ship count")
    require(capture["DockedTurrets"] == capture["AliveTurrets"] - capture["Turrets"], "Invalid docked turret count")
    require(capture["AliveEntities"] == sum(capture[key] for key in ("AliveShips", "AliveTurrets", "Projectiles", "Debris", "Particles")), "Invalid ECS population")
    require(capture["RenderedShips"] >= 24 and capture["AliveCyan"] > 0 and capture["AliveRed"] > 0 and capture["Projectiles"] > 0,
            "Fixed capture tick does not show an active two-sided battle; review cover-settings.json")


def validate_capture(path, settings, assembly_directory):
    capture = read_json(str(path) + ".json")
    for key in ("Tick", "Width", "Height", "Zoom"):
        require(capture[key] == settings[key], "Capture setting mismatch: " + key)
    require(capture["ConfiguredShipsPerTeam"] == settings["ShipsPerTeam"], "Capture ship setting mismatch")
    for key in ("Nonblank", "Atlas", "ControlsVerified", "CountsValid", "StatisticsValid"):
        require(capture[key] is True, "Capture failed " + key)
    require(capture["Statistics"]["OfflineCapture"] is True, "Capture must use fixed-tick offline simulation")
    require(re.fullmatch(r"[0-9A-F]{16}", capture["BattleChecksum"]) is not None, "Invalid battle checksum")
    require(isinstance(capture["GameplayRevision"], str) and capture["GameplayRevision"], "Missing gameplay revision")
    for key, name in BINARY_FIELDS.items():
        require(capture[key] == digest(assembly_directory / name), "Capture binary mismatch: " + name)
    validate_counts(capture)
    pixels = validate_frame(path, settings)
    require(all(capture[key] == value for key, value in pixels.items()), "PNG differs from the captured framebuffer evidence")
    return capture, pixels


def source_state(commit):
    require(re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", commit) is not None, "Invalid source commit")
    git = shutil.which("git")
    require(git is not None, "Git is needed only to read local source provenance")
    def read(*args):
        return subprocess.run([git, *args], cwd=ROOT, check=True, capture_output=True, text=True).stdout.strip()
    require(read("rev-parse", "HEAD") == commit, "Requested source commit differs from the local checkout")
    return bool(read("status", "--porcelain", "--untracked-files=normal"))


def capture_cover(assembly, output, settings, commit):
    assembly, output = project_path(assembly), project_path(output)
    require(assembly.name == "SpaceBattle.Game.dll" and assembly.is_file(), "Build the native frontend first")
    dirty = source_state(commit)
    output.mkdir(parents=True, exist_ok=True)
    png = output / "frame.png"
    command = ["dotnet", str(assembly), "--hidden", "--seed", str(settings["Seed"]),
               "--ships-per-team", str(settings["ShipsPerTeam"]), "--capture-tick", str(settings["Tick"]),
               "--width", str(settings["Width"]), "--height", str(settings["Height"]), "--zoom", str(settings["Zoom"]),
               "--capture", str(png), "--exit-after-capture", "--validate-render", "--validate-controls"]
    run = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=180)
    (output / "capture.log").write_text(run.stdout + run.stderr, encoding="utf-8")
    require(run.returncode == 0, "Native capture failed; see " + str(output / "capture.log"))
    capture, pixels = validate_capture(png, settings, assembly.parent)
    manifest = {
        "SchemaVersion": 1, "Purpose": PURPOSE, "SourceCommit": commit, "SourceDirty": dirty,
        "Renderer": "SpaceBattle.Game OpenGL framebuffer", "Settings": settings,
        "PngSha256": digest(png), "GameplayRevision": capture["GameplayRevision"],
        "BattleChecksum": capture["BattleChecksum"], "Atlas": True, "ControlsVerified": True,
        "CountsValid": True, "StatisticsValid": True, **pixels,
        **{key: capture[key] for key in BINARY_FIELDS}, **{key: capture[key] for key in COUNTS},
    }
    # Only validated files get the publishable names. Timings stay in the raw artifact.
    shutil.copyfile(png, output / "cover.png")
    (output / "cover.png.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: fixed tick {settings['Tick']}, seed {settings['Seed']}, {capture['RenderedShips']} rendered ships, {capture['Projectiles']} projectiles; checksum {capture['BattleChecksum']}")


def validate_cover(directory, settings, commit=None, allow_dirty=False):
    directory = project_path(directory)
    manifest = read_json(directory / "cover.png.json")
    require(manifest["SchemaVersion"] == 1 and manifest["Purpose"] == PURPOSE, "Invalid cover manifest")
    require(manifest["Settings"] == settings, "Cover settings differ from current configuration")
    require(type(manifest["SourceDirty"]) is bool, "Invalid source state")
    require(allow_dirty or not manifest["SourceDirty"], "Uncommitted local cover cannot be published by Actions")
    require(re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", manifest["SourceCommit"]) is not None, "Invalid source commit")
    if commit is not None:
        require(manifest["SourceCommit"] == commit, "Cover belongs to a different source commit")
    require(re.fullmatch(r"[0-9A-F]{16}", manifest["BattleChecksum"]) is not None, "Invalid battle checksum")
    for key in ("PngSha256", *BINARY_FIELDS):
        require(re.fullmatch(r"[0-9A-F]{64}", manifest[key]) is not None, "Invalid hash " + key)
    for key in ("Atlas", "ControlsVerified", "CountsValid", "StatisticsValid"):
        require(manifest[key] is True, "Invalid cover evidence " + key)
    require(manifest["PngSha256"] == digest(directory / "cover.png"), "Cover PNG checksum mismatch")
    require(all(manifest[key] == value for key, value in validate_frame(directory / "cover.png", settings).items()), "Cover pixel evidence mismatch")
    validate_counts(manifest)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    capture = commands.add_parser("capture")
    capture.add_argument("--assembly", required=True)
    capture.add_argument("--output", default="Benchmarks.Local/tmp/space-battle-cover")
    capture.add_argument("--source-commit", required=True)
    validate = commands.add_parser("validate")
    validate.add_argument("--directory", default="docs/space-battle")
    validate.add_argument("--source-commit")
    validate.add_argument("--allow-dirty", action="store_true", help="For explicit local previews only")
    args = parser.parse_args()
    try:
        settings = validate_settings(read_json(SETTINGS))
        if args.command == "capture":
            capture_cover(args.assembly, args.output, settings, args.source_commit)
        else:
            validate_cover(args.directory, settings, args.source_commit, args.allow_dirty)
            print("PASS: cover image, source provenance, configuration and battle evidence")
    except (ValueError, KeyError, TypeError, OSError, subprocess.SubprocessError) as error:
        print("FAIL: " + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
