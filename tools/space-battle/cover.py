"""Capture and validate the production browser frontend; never publish or run benchmarks."""

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
RENDERER = "SpaceBattle.Web Chromium Canvas"
BUILD_FILES = {
    "index.html", "app.js", "styles.css", "observer-camera.js", "collider-debug.js",
    "web-atlases.js", "build-identity.json", "assets/web-atlases.json", "assets/lucide.min.js",
    "_framework/blazor.boot.json", "_framework/blazor.webassembly.js", "_framework/dotnet.native.wasm",
    "_framework/SpaceBattle.Web.wasm", "_framework/SpaceBattle.Core.wasm",
    "_framework/SpaceBattle.SECS.wasm", "_framework/SECS.wasm",
}
ATLAS_SLOTS = {
    "ships": ("ships-team-0", "ships-team-1"),
    "guns": ("guns-team-0", "guns-team-1"),
    "effects": ("effects-clean",), "warp": ("warp-clean",),
}
ATLAS_FILE = re.compile(r"assets/(ships-team-[01]|guns-team-[01]|effects-clean|warp-clean)-([a-f0-9]{20})\.png")
COUNTS = "Ships RenderedShips AliveCyan AliveRed Projectiles Debris Particles AliveEntities AliveShips DockedShips Turrets AliveTurrets DockedTurrets HeavyShots TurretShots".split()
FLAGS = "Nonblank Atlas ControlsVerified CountsValid StatisticsValid OfflineCapture".split()
REVISIONS = "GameplayRevision PhysicsRevision FleetRevision".split()


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
                           ("ShipsPerTeam", 96, 96), ("Width", 960, 4096), ("Height", 540, 4096)):
        require(type(settings[key]) is int and low <= settings[key] <= high, "Invalid setting " + key)
    zoom = settings["Zoom"]
    require(type(zoom) in (int, float) and math.isfinite(zoom) and .25 <= zoom <= 24, "Invalid setting Zoom")
    return settings


def validate_frame(path, settings):
    with Image.open(path) as image:
        require(image.format == "PNG" and image.size == (settings["Width"], settings["Height"]), "Incorrect PNG dimensions or format")
        image.load()
        # The central battle area, not incidental UI or stars, must carry the image.
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


def validate_build_files(files, site=None):
    require(type(files) is dict, "Invalid captured web build files")
    missing = BUILD_FILES - files.keys()
    require(not missing, "Missing captured web build files: " + ", ".join(sorted(missing)))
    textures = {}
    for name, sha in files.items():
        require(type(name) is str and re.fullmatch(r"[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*", name) is not None
                and all(part not in (".", "..") for part in name.split("/")), "Invalid build file path")
        require(type(sha) is str and re.fullmatch(r"[0-9A-F]{64}", sha) is not None, "Invalid build file hash")
        match = ATLAS_FILE.fullmatch(name)
        if match:
            slot, prefix = match.groups()
            require(slot not in textures, "Duplicate captured atlas texture: " + slot)
            require(sha.lower().startswith(prefix), "Atlas filename/hash mismatch: " + name)
            textures[slot] = name
        if site is not None:
            file = project_path(site / name)
            require(file.is_relative_to(site) and file.is_file() and digest(file) == sha, "Capture build mismatch: " + name)
    missing = {slot for slots in ATLAS_SLOTS.values() for slot in slots} - textures.keys()
    require(not missing, "Missing captured atlas textures: " + ", ".join(sorted(missing)))
    if site is not None:
        validate_build_manifests(files, textures, site)


def validate_build_manifests(files, textures, site):
    identity = read_json(site / "build-identity.json")
    require(type(identity) is dict and identity.get("schema") == 1 and
            identity.get("contract") == "png-assets-v1", "Invalid web build identity")
    declared = identity.get("files")
    require(type(declared) is dict and
            {"app.js", "_framework/blazor.boot.json", "assets/web-atlases.json"} <= declared.keys(),
            "Incomplete web build identity")
    for name, sha in declared.items():
        require(type(sha) is str and re.fullmatch(r"[a-f0-9]{64}", sha) is not None and
                files.get(name) == sha.upper(), "Build identity/capture mismatch: " + name)

    # The browser loads generated PNGs, not the original source atlas PNG/JSON pairs.
    bundle = read_json(site / "assets/web-atlases.json")
    require(type(bundle) is dict and bundle.get("schema") == 1 and
            bundle.get("revision") == "png-atlases-v1", "Invalid web atlas bundle")
    atlases = bundle.get("atlases")
    require(type(atlases) is dict and atlases.keys() == ATLAS_SLOTS.keys(), "Incomplete web atlas bundle")
    for family, slots in ATLAS_SLOTS.items():
        atlas = atlases[family]
        declared = atlas.get("textures") if type(atlas) is dict else None
        require(type(declared) is list and len(declared) == len(slots), "Invalid atlas textures: " + family)
        expected = {textures[slot] for slot in slots}
        for texture in declared:
            require(type(texture) is dict and type(texture.get("file")) is str, "Invalid atlas texture: " + family)
            name, sha = "assets/" + texture["file"], texture.get("sha256")
            require(name in expected and type(sha) is str and re.fullmatch(r"[a-f0-9]{64}", sha) is not None and
                    files.get(name) == sha.upper(), "Atlas manifest/capture mismatch: " + name)
            expected.remove(name)


def validate_capture(path, settings, site):
    capture = read_json(str(path) + ".json")
    for key in ("Tick", "Width", "Height", "Zoom"):
        require(capture[key] == settings[key], "Capture setting mismatch: " + key)
    require(capture["Seed"] == settings["Seed"] and capture["ConfiguredShipsPerTeam"] == settings["ShipsPerTeam"], "Capture scenario mismatch")
    require(capture["Renderer"] == RENDERER and capture["RuntimeMode"] == "aot-binary" and
            capture["CaptureSurface"] == "CanvasOnly" and capture["Profile"] == "nominal", "Incorrect browser capture path")
    require(type(capture["BrowserVersion"]) is str and capture["BrowserVersion"], "Missing Chromium version")
    for key in FLAGS:
        require(capture[key] is True, "Capture failed " + key)
    require(re.fullmatch(r"[0-9A-F]{16}", capture["BattleChecksum"]) is not None, "Invalid battle checksum")
    for key in REVISIONS:
        require(type(capture[key]) is str and capture[key], "Missing " + key)
    validate_build_files(capture["BuildFiles"], site)
    validate_counts(capture)
    pixels = validate_frame(path, settings)
    require(all(capture[key] == value for key, value in pixels.items()), "PNG differs from the captured browser evidence")
    return capture, pixels


def source_state(commit):
    require(re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", commit) is not None, "Invalid source commit")
    git = shutil.which("git")
    require(git is not None, "Git is needed only to read local source provenance")
    def read(*args):
        return subprocess.run([git, *args], cwd=ROOT, check=True, capture_output=True, text=True).stdout.strip()
    require(read("rev-parse", "HEAD") == commit, "Requested source commit differs from the local checkout")
    return bool(read("status", "--porcelain", "--untracked-files=normal"))


def capture_cover(site, output, settings, commit, base="/"):
    site, output = project_path(site), project_path(output)
    require((site / "_framework/blazor.boot.json").is_file(), "Publish the AOT web frontend first")
    require(re.fullmatch(r"/(?:[A-Za-z0-9_-]+/)*", base) is not None, "Invalid local base path")
    dirty = source_state(commit)
    output.mkdir(parents=True, exist_ok=True)
    png = output / "frame.png"
    settings_path = output / "capture-settings.json"
    settings_path.write_text(json.dumps(settings), encoding="utf-8")
    command = ["node", str(ROOT / "tools/web/capture-cover.mjs"), "--site", str(site), "--base", base,
               "--settings", str(settings_path), "--output", str(png)]
    run = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=300)
    log = run.stdout + run.stderr
    (output / "capture.log").write_text(log, encoding="utf-8")
    if run.returncode != 0:
        print(log[-8000:], file=sys.stderr)
    require(run.returncode == 0, "Browser capture failed; see " + str(output / "capture.log"))
    require(source_state(commit) == dirty, "Source state changed during capture")
    capture, pixels = validate_capture(png, settings, site)
    manifest = {
        "SchemaVersion": 2, "Purpose": PURPOSE, "SourceCommit": commit, "SourceDirty": dirty,
        "Renderer": RENDERER, "Settings": settings, "PngSha256": digest(png),
        **{key: capture[key] for key in ("RuntimeMode", "BrowserVersion", "CaptureSurface", "Profile", "BuildFiles", "BattleChecksum", *REVISIONS, *FLAGS)},
        **pixels, **{key: capture[key] for key in COUNTS},
    }
    # Only validated files get the publishable names. Timings stay in the raw artifact.
    shutil.copyfile(png, output / "cover.png")
    (output / "cover.png.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: browser tick {settings['Tick']}, seed {settings['Seed']}, {capture['RenderedShips']} rendered ships, {capture['Projectiles']} projectiles; checksum {capture['BattleChecksum']}")


def validate_cover(directory, settings, commit=None, allow_dirty=False):
    directory = project_path(directory)
    manifest = read_json(directory / "cover.png.json")
    require(manifest["SchemaVersion"] == 2 and manifest["Purpose"] == PURPOSE, "Invalid cover manifest")
    require(manifest["Renderer"] == RENDERER and manifest["RuntimeMode"] == "aot-binary" and
            manifest["CaptureSurface"] == "CanvasOnly" and manifest["Profile"] == "nominal", "Invalid browser cover")
    require(type(manifest["BrowserVersion"]) is str and manifest["BrowserVersion"], "Missing Chromium version")
    require(manifest["Settings"] == settings, "Cover settings differ from current configuration")
    require(type(manifest["SourceDirty"]) is bool, "Invalid source state")
    require(allow_dirty or not manifest["SourceDirty"], "Uncommitted local cover cannot be published by Actions")
    require(re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", manifest["SourceCommit"]) is not None, "Invalid source commit")
    if commit is not None:
        require(manifest["SourceCommit"] == commit, "Cover belongs to a different source commit")
    require(re.fullmatch(r"[0-9A-F]{16}", manifest["BattleChecksum"]) is not None, "Invalid battle checksum")
    require(re.fullmatch(r"[0-9A-F]{64}", manifest["PngSha256"]) is not None, "Invalid PNG hash")
    for key in FLAGS:
        require(manifest[key] is True, "Invalid cover evidence " + key)
    for key in REVISIONS:
        require(type(manifest[key]) is str and manifest[key], "Missing " + key)
    validate_build_files(manifest["BuildFiles"])
    require(manifest["PngSha256"] == digest(directory / "cover.png"), "Cover PNG checksum mismatch")
    require(all(manifest[key] == value for key, value in validate_frame(directory / "cover.png", settings).items()), "Cover pixel evidence mismatch")
    validate_counts(manifest)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    capture = commands.add_parser("capture")
    capture.add_argument("--site", required=True)
    capture.add_argument("--base", default="/")
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
            capture_cover(args.site, args.output, settings, args.source_commit, args.base)
        else:
            validate_cover(args.directory, settings, args.source_commit, args.allow_dirty)
            print("PASS: cover image, source provenance, configuration and battle evidence")
    except (ValueError, KeyError, TypeError, OSError, subprocess.SubprocessError) as error:
        print("FAIL: " + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
