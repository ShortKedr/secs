"""Rebuild the archived predictive-neutral-v1 report (2026-10-11).

For the current gameplay campaign use analyze_current.py instead.
"""

import argparse
import csv
from datetime import datetime
import hashlib
import json
import math
import os
from pathlib import Path
import sys
from composition_report import composition_section
from report_locale import report_versions
from heatmap import Metric, TimingPolicy, legend, shade_columns, write_assets

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs/space-battle"
RESULTS = DOCS / "results"
PROFILES = {"low": 0.5, "nominal": 1, "high": 2}
FRAMEWORKS = {
    "secs": "SECS", "lite": "LeoECSLite", "classic": "LeoECS Classic",
    "morpeh": "Morpeh", "static": "StaticEcs 1.2.01",
}
COUNTERS = "ShipsCreated ShipsDestroyed Shots Hits PlasmaShieldHits ShieldBreaks Overheats Dockings Repairs Builds ItemUses DebrisHits Explosions StructuralAdds StructuralRemoves AimDecisions ImpreciseAimDecisions FriendlyFireHits SelfHits".split()
STAGES = "collect ai thermal-inventory steering weapons carriers physics hits lifecycle".split()
MEMORY = ("ManagedHeapBytes", "PrivateBytes", "WorkingSetBytes")
TICKS = 108000
BIN_TICKS = 3600
BUDGET = 1000 / 60
MIB = 1024 ** 2
SOURCES = {
    "Lite": "5053141d7d663c6b1855c8409ac93dfac807b239",
    "Classic": "1213c31c9ef8325d48e74a52b345c4db242d038b",
    "Morpeh": "9f6369384792a01d74df24efd35b08ae313e4f12",
    "StaticEcs": "6323d710c22cfff70100fe3748de432cb5fc70c8",
    "StaticPack": "72ce8b993593e5cfd24c6a9289a4b132305ef493",
}
OPTIONS = dict(Seed=7331, ShipsPerTeam=96, CarriersPerTeam=3, ArenaHalfSize=210,
               TickRate=60, ReplenishCarriers=True, ParticleMultiplier=1,
               MaxProjectiles=24000, MaxDebris=16000, MaxParticles=32000)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def number(value, label, integer=False):
    require(type(value) in (int, float) and math.isfinite(value) and value >= 0,
            f"{label}: expected finite nonnegative number, got {value!r}")
    if integer:
        require(type(value) is int, f"{label}: expected integer")
    return value


def stats(value, label, count):
    for key in ("MeanMs", "StddevMs", "MedianMs", "P95Ms", "P99Ms", "MaxMs", "BudgetMissPercent"):
        number(value[key], f"{label}.{key}")
    number(value["BudgetMisses"], label + ".BudgetMisses", True)
    require(value["MedianMs"] <= value["P95Ms"] <= value["P99Ms"] <= value["MaxMs"],
            label + ": invalid quantile ordering")
    require(value["MeanMs"] <= value["MaxMs"], label + ": mean exceeds max")
    require(value["BudgetMisses"] <= count, label + ": too many misses")
    require(math.isclose(value["BudgetMissPercent"], value["BudgetMisses"] * 100 / count,
                         abs_tol=1e-9), label + ": miss percentage inconsistent")


def check_counters(value, label):
    require(set(value) == set(COUNTERS), label + ": missing/unknown PascalCase counter fields")
    for key in COUNTERS:
        number(value[key], label + "." + key, True)
    require(value["ImpreciseAimDecisions"] <= value["AimDecisions"], label + ": imprecise decisions exceed all decisions")
    require(value["SelfHits"] <= value["FriendlyFireHits"] <= value["Hits"], label + ": invalid observed friendly/self-hit subsets")


def checksum(value, label):
    require(isinstance(value, str) and len(value) == 16
            and all(c in "0123456789ABCDEF" for c in value), label + ": invalid checksum")


def read_json(path):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, f"{path.name}: duplicate JSON key {key}")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=pairs)


def validate_run(data, profile, framework):
    m = data["Metadata"]
    require(m["Framework"] == FRAMEWORKS[framework], "framework/filename mismatch")
    for key, expected in dict(Ticks=TICKS, WarmupTicks=600, TargetTickRate=60,
                              SimulatedSeconds=1800, Realtime=False, TieredCompilation="0").items():
        require(m[key] == expected, f"Metadata.{key}: expected {expected}, got {m[key]}")
    require(math.isclose(m["BudgetMs"], BUDGET, abs_tol=1e-10), "unexpected tick budget")
    require(m["Options"] == dict(OPTIONS, ChurnMultiplier=PROFILES[profile]),
            "Options do not match the full expected benchmark configuration")
    rules = m["GameplayRules"]
    for key, expected in {
        "Revision": "predictive-neutral-v1",
        "ProjectileTeam": "none",
        "Aim": "constant-velocity intercept including inherited shooter velocity and muzzle offset",
        "CollisionImmunity": "none; all enabled ship fixtures can be hit, including the shooter",
        "FriendlyFireCounter": "observed hits with a still-live owner identity; excludes attribution after owner destruction",
    }.items():
        require(rules[key] == expected, "stale/incompatible Metadata.GameplayRules." + key)
    for key, expected in (("ImpreciseAimProbability", 0.25), ("MaxSpreadRadians", 0.03)):
        number(rules[key], "GameplayRules." + key)
        require(math.isclose(rules[key], expected, rel_tol=0, abs_tol=1e-8), "unexpected GameplayRules." + key)
    number(m["WallSeconds"], "WallSeconds")
    require(m["WallSeconds"] > 0, "WallSeconds must be positive")
    for key in ("Runtime", "Os", "Machine", "Date", "TimingScope", "MemoryScope", "Collision"):
        require(isinstance(m[key], str) and m[key], "missing metadata " + key)
    provenance = data["Provenance"]
    for key, pin in SOURCES.items():
        require(provenance["Sources"][key] == pin, "unexpected source pin " + key)
    require(bool(provenance["Sources"]["SECS"]), "missing SECS source description")
    binaries = provenance["Binaries"]
    require(bool(binaries), "missing binary SHA256 provenance")
    for name, digest in binaries.items():
        require(name.endswith(".dll") and isinstance(digest, str) and len(digest) == 64
                and all(c in "0123456789ABCDEF" for c in digest), "invalid binary SHA256 " + name)
    stats(data["Timing"], "Timing", TICKS)
    number(data["AllocatedBytes"], "AllocatedBytes", True)
    number(data["HarnessAndSimulationAllocatedBytes"], "HarnessAndSimulationAllocatedBytes", True)
    require(len(data["GcCollections"]) == 3, "expected three GC generations")
    for value in data["GcCollections"]:
        number(value, "GcCollections", True)
    require([s["Name"] for s in data["Stages"]] == STAGES, "stage schema mismatch")
    for stage in data["Stages"]:
        stats(stage["Timing"], "Stages." + stage["Name"], TICKS)
    check_counters(data["Counters"], "Counters")
    checksum(data["Checksum"], "Checksum")
    windows = data["Windows"]
    require(len(windows) == 30 and len(data["Checkpoints"]) == 30, "expected exactly 30 minute bins/checkpoints")
    previous = None
    for i, (window, point) in enumerate(zip(windows, data["Checkpoints"])):
        require((window["StartTick"], window["EndTick"]) == (i * BIN_TICKS + 1, (i + 1) * BIN_TICKS),
                f"window {i + 1}: noncontiguous/incorrect tick range")
        require(point["Tick"] == window["EndTick"], "checkpoint tick mismatch")
        checksum(point["Checksum"], "checkpoint")
        stats(window["Timing"], f"Windows[{i}].Timing", BIN_TICKS)
        check_counters(window["Counters"], f"Windows[{i}].Counters")
        for key in (*MEMORY, "AllocatedBytes", "Ships", "Projectiles", "Debris", "Particles"):
            number(window[key], "Windows." + key, True)
        require(len(window["Gc"]) == 3, "window GC schema mismatch")
        for value in window["Gc"]:
            number(value, "Windows.Gc", True)
        store = window["Store"]
        for key in ("Entities", "Archetypes", "Chunks", "NativeBytes", "AccessorBytes"):
            if store[key] is not None:
                number(store[key], "Store." + key, True)
        require(store["Entities"] == sum(window[k] for k in ("Ships", "Projectiles", "Debris", "Particles")),
                "store entity count differs from logical live entities")
        if previous:
            require(all(window["Counters"][k] >= previous["Counters"][k] for k in COUNTERS),
                    "cumulative counter decreased")
            require(all(a >= b for a, b in zip(window["Gc"], previous["Gc"])), "cumulative GC count decreased")
        previous = window
    require(windows[-1]["Counters"] == data["Counters"], "final window/global counters mismatch")
    require(data["Checkpoints"][-1]["Checksum"] == data["Checksum"], "final checkpoint/checksum mismatch")
    require(sum(w["AllocatedBytes"] for w in windows) == data["AllocatedBytes"], "allocation sum mismatch")
    require(sum(w["Timing"]["BudgetMisses"] for w in windows) == data["Timing"]["BudgetMisses"], "budget miss sum mismatch")
    require(max(w["Timing"]["MaxMs"] for w in windows) == data["Timing"]["MaxMs"], "max aggregation mismatch")
    require(math.isclose(sum(w["Timing"]["MeanMs"] for w in windows) / 30,
                         data["Timing"]["MeanMs"], rel_tol=1e-8, abs_tol=1e-9), "mean aggregation mismatch")


def collect():
    runs, errors = {}, []
    expected = {f"{p}-{f}.json" for p in PROFILES for f in FRAMEWORKS}
    actual = {p.name for p in RESULTS.glob("*.json") if not p.name.lower().startswith("smoke")}
    if actual != expected:
        errors.append(f"run set mismatch: missing={sorted(expected - actual)}, unexpected={sorted(actual - expected)}")
    for p in PROFILES:
        for f in FRAMEWORKS:
            path = RESULTS / f"{p}-{f}.json"
            try:
                data = read_json(path)
                validate_run(data, p, f)
                runs[p, f] = data
            except (ValueError, KeyError, TypeError, OSError) as error:
                errors.append(f"{path.name}: {error}")
    for p in PROFILES:
        reference = runs.get((p, "secs"))
        if not reference:
            continue
        for f in FRAMEWORKS:
            run = runs.get((p, f))
            if not run:
                continue
            for key in ("Checkpoints", "Checksum", "Counters", "Provenance"):
                if run[key] != reference[key]:
                    errors.append(f"{p}-{f}: FAIL {key} differs from {p}-secs")
            for i, (a, b) in enumerate(zip(run["Windows"], reference["Windows"])):
                for key in ("Counters", "Ships", "Projectiles", "Debris", "Particles"):
                    if a[key] != b[key]:
                        errors.append(f"{p}-{f}: FAIL window {i + 1} {key} mismatch")
    if runs:
        reference = next(iter(runs.values()))
        # A consistent old suite is still stale after the gameplay build changes.
        binary_root = ROOT / "SpaceBattle.Headless/bin/Release/net8.0"
        for name, expected_hash in reference["Provenance"]["Binaries"].items():
            try:
                require(Path(name).name == name, "binary provenance must contain filenames only")
                actual_hash = hashlib.sha256((binary_root / name).read_bytes()).hexdigest().upper()
                require(actual_hash == expected_hash, "stale result: current Release binary differs: " + name)
            except (ValueError, OSError) as error:
                errors.append("current build provenance: " + str(error))
        for (p, f), run in runs.items():
            for key in ("Machine", "Runtime", "Os", "TieredCompilation", "TimingScope", "MemoryScope", "Collision", "GameplayRules"):
                if run["Metadata"][key] != reference["Metadata"][key]:
                    errors.append(f"{p}-{f}: mixed environment/scope {key}")
            if run["Provenance"] != reference["Provenance"]:
                errors.append(f"{p}-{f}: mixed suite source/binary provenance")
    return runs, errors


def screenshot():
    require((DOCS / "action.png").is_file(), "missing real frontend action.png")
    capture = read_json(DOCS / "action.png.json")
    for key, expected in dict(Tick=900, Width=1600, Height=1000, Zoom=2,
                              ConfiguredShipsPerTeam=256, Nonblank=True).items():
        require(capture[key] == expected, "action screenshot metadata mismatch: " + key)
    require(capture["SceneBrightPixels"] > 0 and capture["SceneUniqueColors"] > 1,
            "action screenshot has no scene evidence")
    return capture


FRONTEND_METRICS = {
    "SimulationCpuMs": "simulation_cpu_ms", "SnapshotCaptureCpuMs": "snapshot_capture_cpu_ms",
    "RenderSubmissionCpuMs": "render_submission_cpu_ms", "TotalCpuFrameMs": "total_cpu_frame_ms",
    "WallFrameIntervalMs": "wall_frame_interval_ms", "ReplayWallFramesPerSecond": "replay_wall_fps",
}
FRONTEND_COLUMNS = "frame tick simulation_cpu_ms snapshot_capture_cpu_ms render_submission_cpu_ms total_cpu_frame_ms wall_frame_interval_ms replay_wall_fps wall_16_67ms_miss cpu_16_67ms_miss gpu_render_ms present_cpu_ms present_wait_ms ships projectiles debris particles".split()


def compare_number(actual, expected, label):
    number(actual, label)
    require(math.isclose(actual, expected, rel_tol=1e-8, abs_tol=1e-9),
            f"{label}: JSON {actual} differs from CSV recalculation {expected}")


def frontend_stats(actual, values, label):
    if not values:
        require(actual is None, label + ": empty samples must have null statistics")
        return
    ordered = sorted(values)
    require(set(actual) == {"Count", "Min", "Mean", "P50", "P95", "P99", "Max"}, label + ": statistics schema mismatch")
    require(type(actual["Count"]) is int and actual["Count"] == len(values), label + ": sample count mismatch")
    expected = {"Min": ordered[0], "Mean": math.fsum(values) / len(values), "Max": ordered[-1]}
    for key, percentile in (("P50", .5), ("P95", .95), ("P99", .99)):
        expected[key] = ordered[max(0, math.ceil(percentile * len(values)) - 1)]
    for key, value in expected.items():
        compare_number(actual[key], value, label + "." + key)


def validate_frontend_csv_path(recorded, csv_path):
    raw_path = Path(recorded)
    if raw_path == Path(raw_path.name):
        # New captures contain only the CSV filename, so no local home path is published.
        require(raw_path.name == csv_path.name, "frontend: RawCsvPath must identify frontend.csv")
    else:
        # Existing captures may still have a repo-relative or absolute path.
        if not raw_path.is_absolute():
            raw_path = ROOT / raw_path
        require(raw_path.resolve() == csv_path.resolve(), "frontend: RawCsvPath must identify docs/space-battle/frontend.csv")


def validate_frontend(runs):
    path, csv_path = DOCS / "frontend.json", DOCS / "frontend.csv"
    data = read_json(path)
    for key, expected in dict(SchemaVersion=1, SampleFrames=3600, WarmupFrames=120, StepsPerFrame=1,
                              ReplayStartTick=900, FirstMeasuredTick=1021, LastMeasuredTick=4620,
                              RealTimeGameplayFpsMeasured=False, VSync=False, FrameRateCap=0, UpdateRateCap=0,
                              Framework="SECS", Width=1440, Height=900, Zoom=1, Hidden=True, Atlas=True,
                              ControlsVerified=True, PresentCpuMs=None, PresentWaitMs=None).items():
        require(type(data[key]) is type(expected) and data[key] == expected, "frontend: unexpected " + key)
    require(data["Mode"] == "deterministic fixed replay: exactly one simulation Step per rendered frame, uncapped, VSync off",
            "frontend: unexpected measurement mode")
    for key in ("Width", "Height", "StopwatchFrequencyHz", "FirstMeasuredStartTimestamp", "LastMeasuredBoundaryTimestamp"):
        require(number(data[key], "frontend." + key, True) > 0, "frontend: zero " + key)
    require(data["LastMeasuredBoundaryTimestamp"] > data["FirstMeasuredStartTimestamp"], "frontend: invalid wall boundaries")
    require(.25 <= number(data["Zoom"], "frontend.Zoom") <= 24, "frontend: invalid zoom")
    for key in ("Hidden", "Atlas", "ControlsVerified", "GpuDataValid", "ValidationPass"):
        require(type(data[key]) is bool, "frontend: expected boolean " + key)
    config = data["Config"]
    require(config == dict(OPTIONS, ChurnMultiplier=1), "frontend: unexpected battle options")
    require(type(config["ShipsPerTeam"]) is int and 8 <= config["ShipsPerTeam"] <= 10000, "frontend: invalid ShipsPerTeam")
    for key in ("Vendor", "Renderer", "Version"):
        require(isinstance(data["Driver"][key], str) and data["Driver"][key], "frontend: missing driver " + key)
    for key in ("SimulationCPU", "SnapshotCaptureCPU", "RenderSubmissionCPU", "TotalCPUFrame", "WallFrameInterval", "GPU", "Present", "Percentiles", "Excluded"):
        require(isinstance(data["Definitions"][key], str) and data["Definitions"][key], "frontend: missing scope " + key)
    binary_root = ROOT / "SpaceBattle.Game/bin/Release/net8.0"
    for key, name in (("GameDllSha256", "SpaceBattle.Game.dll"), ("CoreDllSha256", "SpaceBattle.Core.dll"),
                      ("SecsAdapterDllSha256", "SpaceBattle.SECS.dll")):
        digest = data[key]
        require(isinstance(digest, str) and len(digest) == 64 and all(c in "0123456789ABCDEF" for c in digest), "frontend: invalid " + key)
        require(hashlib.sha256((binary_root / name).read_bytes()).hexdigest().upper() == digest, "frontend: stale current Game build " + name)
        if name != "SpaceBattle.Game.dll":
            require(runs["nominal", "secs"]["Provenance"]["Binaries"][name] == digest, "frontend/headless binary mismatch: " + name)
    checksum(data["FinalBattleChecksum"], "frontend.FinalBattleChecksum")
    validate_frontend_csv_path(data["RawCsvPath"], csv_path)
    with csv_path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        require(reader.fieldnames == FRONTEND_COLUMNS, "frontend: CSV header schema mismatch")
        rows = list(reader)
    require(len(rows) == 3600, "frontend: expected exactly 3600 CSV rows")
    series = {key: [] for key in FRONTEND_METRICS}
    gpu_values, cpu_misses, wall_misses = [], 0, 0
    for i, row in enumerate(rows):
        require(set(row) == set(FRONTEND_COLUMNS) and all(v is not None for v in row.values()), "frontend: malformed CSV row")
        require(int(row["frame"]) == i and int(row["tick"]) == 1021 + i, "frontend: discontinuous frame/tick sequence")
        require(row["present_cpu_ms"] == row["present_wait_ms"] == "", "frontend: present measurements must be unavailable")
        for key, column in FRONTEND_METRICS.items():
            value = float(row[column])
            number(value, "frontend CSV " + column)
            series[key].append(value)
        wall = series["WallFrameIntervalMs"][-1]
        require(wall > 0, "frontend: missing/zero start-to-start interval")
        compare_number(series["ReplayWallFramesPerSecond"][-1], 1000 / wall, "frontend CSV replay_wall_fps")
        total = series["TotalCpuFrameMs"][-1]
        require(sum(series[k][-1] for k in ("SimulationCpuMs", "SnapshotCaptureCpuMs", "RenderSubmissionCpuMs")) <= total + 1e-6,
                "frontend: CPU stage sum exceeds callback total")
        cpu_miss, wall_miss = int(total > 16.67), int(wall > 16.67)
        require(row["cpu_16_67ms_miss"] == str(cpu_miss) and row["wall_16_67ms_miss"] == str(wall_miss), "frontend: CSV budget flag mismatch")
        cpu_misses += cpu_miss
        wall_misses += wall_miss
        if row["gpu_render_ms"] != "":
            gpu_values.append(number(float(row["gpu_render_ms"]), "frontend CSV gpu_render_ms"))
        for key in ("ships", "projectiles", "debris", "particles"):
            number(int(row[key]), "frontend CSV " + key, True)
    for key, values in series.items():
        frontend_stats(data[key], values, "frontend." + key)
    for key in ("Ships", "Projectiles", "Debris", "Particles"):
        require(data["FinalEntities"][key] == int(rows[-1][key.lower()]), "frontend: final entity count mismatch " + key)
    duration = math.fsum(series["WallFrameIntervalMs"])
    compare_number(data["MeasuredWallDurationMs"], duration, "frontend.MeasuredWallDurationMs")
    compare_number(data["ReplayWallThroughputFramesPerSecond"], 3600 * 1000 / duration, "frontend.ReplayWallThroughputFramesPerSecond")
    compare_number(duration, (data["LastMeasuredBoundaryTimestamp"] - data["FirstMeasuredStartTimestamp"]) * 1000 / data["StopwatchFrequencyHz"], "frontend.wall timestamp duration")
    require(data["Budget"]["CpuFrameMisses"] == cpu_misses and data["Budget"]["WallIntervalMisses"] == wall_misses, "frontend: budget misses mismatch")
    compare_number(data["Budget"]["ThresholdMs"], 16.67, "frontend.Budget.ThresholdMs")
    compare_number(data["Budget"]["WallIntervalMissPercent"], wall_misses * 100 / 3600, "frontend.Budget.WallIntervalMissPercent")
    gpu = data["Gpu"]
    require(type(gpu["Supported"]) is bool, "frontend: invalid GPU supported flag")
    for key in ("CounterBits", "QueryRingSize", "QueryRingExhaustedFrames", "Samples", "NullCount", "ZeroSamples"):
        number(gpu[key], "frontend.Gpu." + key, True)
    require(gpu["QueryRingSize"] == 32, "frontend: query ring size mismatch")
    require(gpu["Samples"] == len(gpu_values) and gpu["NullCount"] == 3600 - len(gpu_values)
            and gpu["ZeroSamples"] == gpu_values.count(0), "frontend: GPU sample accounting mismatch")
    require(gpu["QueryRingExhaustedFrames"] <= gpu["NullCount"], "frontend: exhausted query count exceeds missing samples")
    frontend_stats(gpu["RenderMs"], gpu_values, "frontend.Gpu.RenderMs")
    if gpu["Supported"]:
        require(gpu["CounterBits"] > 0 and gpu["UnavailableReason"] is None, "frontend: inconsistent supported GPU metadata")
    else:
        require(not gpu_values and isinstance(gpu["UnavailableReason"], str) and gpu["UnavailableReason"], "frontend: unavailable GPU must have reason and no samples")
    valid_gpu = not gpu["Supported"] or any(v > 0 for v in gpu_values)
    require(data["GpuDataValid"] == valid_gpu, "frontend: GpuDataValid inconsistent")
    number(data["GlErrorOverflow"], "frontend.GlErrorOverflow", True)
    require(isinstance(data["GlErrors"], list), "frontend: invalid GlErrors")
    for error in data["GlErrors"]:
        require(type(error["Frame"]) is int and -1 <= error["Frame"] <= 3720, "frontend: invalid GL error frame")
        require(number(error["Code"], "frontend.GL error code", True) > 0, "frontend: invalid GL error code")
    expected_pass = not data["GlErrors"] and data["GlErrorOverflow"] == 0 and valid_gpu
    require(data["ValidationPass"] == expected_pass, "frontend: ValidationPass inconsistent")
    require(expected_pass, "frontend FAIL: " + json.dumps({k: data[k] for k in ("GlErrors", "GlErrorOverflow", "GpuDataValid")}) + "; Gpu=" + json.dumps(gpu))
    return data


def validate_parity(runs, capture, frontend):
    reference = runs["nominal", "secs"]
    for kind, ticks, ships, expected_checksum in (
        ("capture", 900, 256, capture["BattleChecksum"]),
        ("profile", 4620, 96, frontend["FinalBattleChecksum"]),
    ):
        data = read_json(DOCS / "custom" / f"frontend-{kind}-parity.json")
        m = data["Metadata"]
        require(m["Framework"] == "SECS" and m["Ticks"] == ticks and m["WarmupTicks"] == 0
                and m["Realtime"] is False and m["TargetTickRate"] == 60 and m["TieredCompilation"] == "0",
                "frontend parity: unexpected mode/config " + kind)
        require(m["Options"] == dict(OPTIONS, ShipsPerTeam=ships, ChurnMultiplier=1), "frontend parity: battle options mismatch " + kind)
        require(m["GameplayRules"] == reference["Metadata"]["GameplayRules"], "frontend parity: stale rules " + kind)
        checksum(data["Checksum"], "frontend parity " + kind)
        require(data["Checksum"] == expected_checksum, "frontend/headless parity checksum mismatch " + kind)
        require(data["Checkpoints"][-1] == {"Tick": ticks, "Checksum": expected_checksum}, "frontend parity: final checkpoint mismatch " + kind)
        for name in ("SpaceBattle.Core.dll", "SpaceBattle.SECS.dll"):
            require(data["Provenance"]["Binaries"][name] == reference["Provenance"]["Binaries"][name], "frontend parity: stale binary " + name)
    for key in ("CoreDllSha256", "GameDllSha256", "SecsAdapterDllSha256"):
        require(capture[key] == frontend[key], "capture/profile binary mismatch " + key)
    require(capture["BattleChecksum"] == "76139FC912B8B3E7"
            and frontend["FinalBattleChecksum"] == "D4DEA4EA08683883", "unexpected new-rule frontend parity state")


def validate_hardware(runs):
    data = read_json(DOCS / "hardware.json")
    require(set(data) == {"Machine", "CapturedAt", "Cpu", "TotalPhysicalMemoryBytes", "Os", "OsVersion",
                          "LogicalProcessorCountVisibleToPowerShell", "Notes"}, "hardware: schema mismatch")
    require(data["Machine"] == runs["nominal", "secs"]["Metadata"]["Machine"], "hardware/headless Machine mismatch")
    for key in ("CapturedAt", "Os", "OsVersion", "Notes"):
        require(isinstance(data[key], str) and data[key], "hardware: missing " + key)
    captured = datetime.fromisoformat(data["CapturedAt"])
    require(captured.utcoffset() is not None, "hardware: CapturedAt must include timezone")
    require(all(captured >= datetime.fromisoformat(r["Metadata"]["Date"]) for r in runs.values()),
            "hardware: collect inventory only after campaign timings finish")
    require(number(data["TotalPhysicalMemoryBytes"], "hardware.RAM", True) > 0, "hardware: no RAM size")
    require(number(data["LogicalProcessorCountVisibleToPowerShell"], "hardware.visible processors", True) > 0, "hardware: no visible processors")
    require(isinstance(data["Cpu"], list) and data["Cpu"], "hardware: expected nonempty CPU array")
    for cpu in data["Cpu"]:
        require(set(cpu) == {"Name", "Manufacturer", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed"}, "hardware: CPU schema mismatch")
        for key in ("Name", "Manufacturer"):
            require(isinstance(cpu[key], str) and cpu[key], "hardware: missing CPU " + key)
        for key in ("NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed"):
            require(number(cpu[key], "hardware.Cpu." + key, True) > 0, "hardware: invalid CPU " + key)
        require(cpu["NumberOfLogicalProcessors"] >= cpu["NumberOfCores"], "hardware: invalid core/thread counts")
    return data


def tick_gc_samples(runs):
    """Recover a bounded GC interval from post-Step samples, never a tick-zero baseline."""
    output = {}
    columns = "tick simulationSeconds tickMs allocatedBytes gen0 gen1 gen2 ships projectiles debris particles adds removes".split()
    for (profile, framework), run in runs.items():
        path = RESULTS / f"{profile}-{framework}.ticks.csv"
        first = last = None
        count = allocated = misses = 0
        timing_sum = 0.0
        with path.open(encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            require(reader.fieldnames == columns, path.name + ": tick CSV schema mismatch")
            for count, row in enumerate(reader, 1):
                require(set(row) == set(columns) and all(v is not None for v in row.values()), path.name + ": malformed row")
                require(int(row["tick"]) == count, path.name + ": discontinuous ticks")
                current = tuple(number(int(row[f"gen{g}"]), path.name + f".gen{g}", True) for g in range(3))
                if last is not None:
                    require(all(a >= b for a, b in zip(current, last)), path.name + ": cumulative GC count decreased")
                if first is None:
                    first = current
                last = current
                tick_ms = number(float(row["tickMs"]), path.name + ".tickMs")
                timing_sum += tick_ms
                misses += tick_ms > BUDGET
                allocated += number(int(row["allocatedBytes"]), path.name + ".allocatedBytes", True)
            require(count == TICKS, path.name + ": expected 108000 post-Step samples")
            require(allocated == run["AllocatedBytes"] and misses == run["Timing"]["BudgetMisses"], path.name + ": JSON allocation/budget mismatch")
            compare_number(timing_sum / TICKS, run["Timing"]["MeanMs"], path.name + ": mean timing")
            require(int(row["adds"]) == run["Counters"]["StructuralAdds"]
                    and int(row["removes"]) == run["Counters"]["StructuralRemoves"], path.name + ": final logical counters mismatch")
        delta = tuple(b - a for a, b in zip(first, last))
        require(all(broad >= sample for broad, sample in zip(run["GcCollections"], delta)), path.name + ": broader JSON GC smaller than bounded sample delta")
        output[profile, framework] = {"FirstPostStep": first, "LastPostStep": last, "Delta": delta}
    return output


def frontend_report(data):
    lines = ["## Отдельный профиль фронтенда", "",
             "JSON/CSV фронтенда проверены отдельно от 15 headless-запусков: SchemaVersion=1, "
             "3600 измеренных кадров, 120 кадров прогрева после ReplayStartTick=900, измеренные тики 1021-4620. "
             "Один Step на кадр, без ограничения частоты, VSync off; это deterministic replay throughput, "
             "не измерение realtime gameplay FPS. RealTimeGameplayFpsMeasured=false.", "",
             f"Конфигурация: {data['Width']} x {data['Height']}, zoom {data['Zoom']}, "
             f"Hidden={data['Hidden']}, Atlas={data['Atlas']}, seed {data['Config']['Seed']}, "
             f"{data['Config']['ShipsPerTeam']} эскортов/команду, Framework={data['Framework']}.",
             f"GPU/driver: {data['Driver']['Vendor']}; {data['Driver']['Renderer']}; {data['Driver']['Version']}.", ""]
    rows = []
    for key in FRONTEND_METRICS:
        s = data[key]
        rows.append((key, "frames/s" if key == "ReplayWallFramesPerSecond" else "ms", s["Count"],
                     *(f"{s[k]:.5f}" for k in ("Min", "Mean", "P50", "P95", "P99", "Max"))))
    gpu = data["Gpu"]
    s = gpu["RenderMs"]
    rows.append(("Gpu.RenderMs", "ms", gpu["Samples"],
                 *(f"{s[k]:.5f}" if s else "недоступно" for k in ("Min", "Mean", "P50", "P95", "P99", "Max"))))
    lines += table(["Метрика", "Единицы", "Count", "Min", "Mean", "P50", "P95", "P99", "Max"], rows)
    lines += ["", f"Replay wall throughput: {data['ReplayWallThroughputFramesPerSecond']:.3f} frames/s за "
              f"{data['MeasuredWallDurationMs']:.3f} ms. Это число кадров / суммарное время, "
              "не среднее мгновенных FPS. Все статистики пересчитаны из CSV по nearest-rank ceil(p*N)-1 "
              "и сверены с JSON, включая бюджетные флаги, GPU null/zero accounting и временные границы.", "",
              f"Бюджет фронтенда {data['Budget']['ThresholdMs']} ms: CPU frame misses={data['Budget']['CpuFrameMisses']}, "
              f"wall interval misses={data['Budget']['WallIntervalMisses']} "
              f"({data['Budget']['WallIntervalMissPercent']:.3f}%). Это отдельный порог профайлера, "
              "не доказательство устойчивого realtime 60 FPS.", "",
              f"Gpu.Supported={gpu['Supported']}; GpuDataValid={data['GpuDataValid']}; Samples={gpu['Samples']}; "
              f"NullCount={gpu['NullCount']}; ZeroSamples={gpu['ZeroSamples']}; CounterBits={gpu['CounterBits']}; "
              f"QueryRingExhaustedFrames={gpu['QueryRingExhaustedFrames']}; "
              f"UnavailableReason={gpu['UnavailableReason']!r}. "
              f"GlErrors={json.dumps(data['GlErrors'])}; GlErrorOverflow={data['GlErrorOverflow']}; ValidationPass={data['ValidationPass']}.", "",
              "SimulationCpuMs измеряет Step и rolling HUD timing queue, без snapshot/render. SnapshotCaptureCpuMs - "
              "только Battle.Capture. RenderSubmissionCpuMs - генерация сцены/HUD, telemetry queries, title и GL clear/upload/draw, "
              "без Step, Capture и timer-query bookkeeping. TotalCpuFrameMs - callback до окончания query/error bookkeeping; "
              "включает polling, но исключает auto-present, сохранение sample и возврат callback. WallFrameIntervalMs - "
              "от входа callback до следующего входа, включая auto-present/event loop; последний интервал закрыт без лишнего Step.", "",
              "Асинхронный GL_TIME_ELAPSED охватывает clear и последнюю scene/HUD draw: это elapsed GPU timeline, "
              "включающий возможные idle gaps, пока CPU создаёт геометрию, а не чистое device busy time/utilization. "
              "GPU и CPU перекрываются; их нельзя складывать или вычитать для оценки present. PresentCpuMs и PresentWaitMs "
              "равны null: Silk auto-present происходит после callback, отдельного измерения нет. Недоступный GPU "
              "не заменяется CPU временем и не считается нулём. Полный GPU tail drain через GL.Finish исключён из throughput, "
              "как fast-forward, warmup, framebuffer readback, hashes/checkpoints, aggregation и file writing.", "",
              "### Проверка состояния и происхождения", "",
              "Сверены SHA256 текущих Game/Core/SECS adapter DLL; Core/adapter также совпадают с headless suite. "
              "Custom parity-запуски подтверждают только состояние: capture tick 900 / 256 эскортов "
              "(76139FC912B8B3E7) и profile tick 4620 / 96 эскортов (D4DEA4EA08683883). "
              "Их warmup=0, поэтому их timing/JIT tails не включены в показатели производительности или 1 620 000 тиков suite.", ""]
    lines += table(["Артефакт", "SHA256"], [(f"[{name}]({name})", hashlib.sha256((DOCS / name).read_bytes()).hexdigest())
                   for name in ("frontend.json", "frontend.csv", "action.png", "action.png.json",
                                "custom/frontend-capture-parity.json", "custom/frontend-profile-parity.json")])
    lines += ["", f"CoreDllSha256={data['CoreDllSha256']}; GameDllSha256={data['GameDllSha256']}; "
              f"SecsAdapterDllSha256={data['SecsAdapterDllSha256']}.", ""]
    lines += ["Отдельно сообщалось о проверке новых правил: основной исполнитель сообщил об успешном Core --validate "
              "и коротком сравнении пяти портов (3 000 тиков, 10 checkpoints, checksum 666A96DD2647E74D); "
              "SECS.Tests также прошли 16/16 в Debug и Release. Автор отчёта эти проверки не повторял. "
              "Эти результаты со слов исполнителя не заменяют полный измеренный набор и отдельную проверку frontend CSV.", ""]
    return lines


def table(headers, rows):
    def cell(value):
        return str(value).replace("|", "\\|").replace("\n", " ")
    return ["| " + " | ".join(map(cell, headers)) + " |",
            "| " + " | ".join("---" for _ in headers) + " |"] + [
                "| " + " | ".join(map(cell, row)) + " |" for row in rows]


def plots(runs):
    # Import plotting packages only after the evidence gate has passed.
    sys.path.insert(0, str(ROOT / "Benchmarks.Local/python-packages"))
    os.environ["MPLCONFIGDIR"] = str(ROOT / "Benchmarks.Local" / "matplotlib-cache")
    import numpy as np
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    output = DOCS / "charts"
    output.mkdir(exist_ok=True)
    colors = ["#0072B2", "#D55E00", "#009E73", "#CC79A7", "#555555"]
    x = np.arange(1, 31)
    chart_names = []

    def chart(name, title, ylabel, series, log=False, budget=False):
        fig, axes = plt.subplots(1, 3, figsize=(17, 4.6), sharey=True)
        for axis, profile in zip(axes, PROFILES):
            for (framework, label, values), color in zip(series(profile), colors):
                axis.plot(x, values, label=label, color=color, linewidth=1.4)
            if budget:
                axis.axhline(BUDGET, color="black", linestyle="--", label="16.67 ms budget")
            if log:
                axis.set_yscale("log")
            axis.set(title=f"{profile} (churn {PROFILES[profile]:g})", xlabel="Simulated minute")
            axis.grid(alpha=0.2)
        axes[0].set_ylabel(ylabel)
        handles, labels = axes[0].get_legend_handles_labels()
        fig.legend(handles, labels, fontsize=8, loc="lower center", ncol=6, bbox_to_anchor=(0.5, 0.01))
        fig.suptitle(title)
        fig.tight_layout(rect=(0, 0.12, 1, 0.95))
        fig.savefig(output / name, dpi=150)
        plt.close(fig)
        chart_names.append(name)

    def values(profile, getter):
        return [(f, FRAMEWORKS[f], getter(runs[profile, f])) for f in FRAMEWORKS]

    for metric in ("MeanMs", "P95Ms", "P99Ms"):
        chart(f"timing-{metric}.png", metric + " per minute (not averaged global percentiles)",
              "Tick time (ms; logarithmic)", lambda p, k=metric: values(p, lambda r: [w["Timing"][k] for w in r["Windows"]]),
              log=True, budget=True)
    for metric in MEMORY:
        chart(f"memory-{metric}.png", "Minute-end " + metric, "MiB",
              lambda p, k=metric: values(p, lambda r: [w[k] / MIB for w in r["Windows"]]))
    chart("native-secs.png", "SECS native storage only (unknown values skipped)", "MiB",
          lambda p: [("secs", "SECS", [w["Store"]["NativeBytes"] / MIB if w["Store"]["NativeBytes"] is not None else np.nan
                                        for w in runs[p, "secs"]["Windows"]])])
    for metric in ("Ships", "Projectiles", "Debris", "Particles"):
        chart(f"entities-{metric}.png", "Minute-end live " + metric, "Entity count",
              lambda p, k=metric: values(p, lambda r: [w[k] for w in r["Windows"]]))
    for metric in ("StructuralAdds", "StructuralRemoves"):
        chart(f"rate-{metric}.png", metric + " (logical component operations)", "Operations / simulated second",
              lambda p, k=metric: values(p, lambda r: np.diff([0] + [w["Counters"][k] for w in r["Windows"]]) / 60))
    fig, axes = plt.subplots(1, 3, figsize=(17, 5), sharey=True)
    for axis, profile in zip(axes, PROFILES):
        bottom = np.zeros(5)
        for index, stage in enumerate(STAGES):
            heights = np.array([runs[profile, f]["Stages"][index]["Timing"]["MeanMs"] for f in FRAMEWORKS])
            axis.bar(list(FRAMEWORKS), heights, bottom=bottom, label=stage)
            bottom += heights
        axis.set(title=profile, xlabel="Framework")
    axes[0].set_ylabel("Mean stage time (ms / tick)")
    handles, labels = axes[0].get_legend_handles_labels()
    fig.legend(handles, labels, fontsize=8, loc="lower center", ncol=5, bbox_to_anchor=(0.5, 0.01))
    fig.suptitle("Instrumented stage means (not sums of stage percentiles)")
    fig.tight_layout(rect=(0, 0.16, 1, 0.95))
    fig.savefig(output / "stages.png", dpi=150)
    plt.close(fig)
    chart_names.append("stages.png")
    return chart_names


def report(runs, capture, charts, frontend, hardware, gc_samples, heat_policy=None):
    heat_policy = heat_policy or TimingPolicy()
    lines = ["![Обложка текущего геймплея](cover.png)", "",
             "Обложка: текущий геймплей, а не зафиксированный измеренный прогон. [Процесс захвата](COVER.md).", "",
             "Следующие метаданные захвата относятся к зафиксированному [action.png](action.png), а не к обложке.", "",
             "Снимок OpenGL-фронтенда: 1600 x 1000, seed 7331, 256 эскортов на команду, tick 900, zoom 2. "
             f"Кораблей в snapshot: {capture['Ships']}; отрисовано после clipping: {capture['RenderedShips']}; "
             f"живых по командам: {capture['AliveCyan']}/{capture['AliveRed']} (докованные корабли могут не входить в список snapshot). "
             "Это НЕ конфигурация бенчмарка с 96 эскортами на команду и не результат измерения времени. "
             "PNG sidecar не содержит поля seed. Seed 7331 подтверждён Options.Seed парного "
             "[headless capture replay](custom/frontend-capture-parity.json), совпавшими checksum и Core/adapter hashes "
             "с текущим action sidecar, а также известной командой захвата; это не прочитанное из PNG sidecar поле.", "",
             "# Результаты Space Battle", "", "**PASS: проверка сопоставимости пройдена.** У всех пяти фреймворков внутри каждого профиля "
             "совпадают все 30 checkpoints, итоговый checksum, итоговые Counters и поминутные логические счётчики и количества сущностей. "
             "Каждый из 15 запусков содержит 108000 измеренных тиков, 600 отдельных тиков прогрева и 30 полных минутных окон. "
             "Всего: 1 620 000 измеренных тиков; 30 минут симуляции на запуск, 450 минут симуляции на весь набор.", "",
             "## Ключевые результаты", ""]
    misses = sum(r["Timing"]["BudgetMisses"] for r in runs.values())
    if misses == 0:
        lines += ["- Во всех 15 случаях: 0 тиков с превышением бюджета 16.67 ms. Это headless Step, не обещание realtime FPS."]
    else:
        lines += [f"- Суммарно во всех 15 случаях: {misses} тиков с превышением бюджета 16.67 ms; подробности по случаям ниже."]
    lowest = {p: [f for f in FRAMEWORKS if runs[p, f]["Timing"]["MeanMs"] == min(runs[p, k]["Timing"]["MeanMs"] for k in FRAMEWORKS)] for p in PROFILES}
    if all(lowest[p] == ["morpeh"] for p in PROFILES):
        lines += ["- Наблюдаемый MeanMs Morpeh наименьший во всех трёх профилях этого набора; это не универсальный победитель и не статистически устойчивый рейтинг."]
    else:
        lines += ["- Наблюдаемый минимум MeanMs по профилям: " + "; ".join(p + ": " + ", ".join(lowest[p]) for p in PROFILES) + ". Это не универсальное ранжирование."]
    nominal = runs["nominal", "secs"]["Timing"]
    shares = [100 * sum(s["Timing"]["MeanMs"] for s in r["Stages"] if s["Name"] in ("physics", "hits")) / r["Timing"]["MeanMs"] for r in runs.values()]
    lines += [f"- SECS nominal: MeanMs={nominal['MeanMs']:.4f} ms, P99Ms={nominal['P99Ms']:.4f} ms.",
              f"- physics + hits занимают {min(shares):.0f}-{max(shares):.0f}% среднего Step по 15 случаям; общая боевая логика может маскировать различия ECS.",
              ""]
    lines += legend(heat_policy)
    lines += composition_section(DOCS, runs, read_json, require, table, heat_policy)
    lines += ["## Данные запусков", ""]
    rows = []
    for (p, f), r in runs.items():
        path = RESULTS / f"{p}-{f}.json"
        m = r["Metadata"]
        rows.append((p, f, m["Ticks"], len(r["Windows"]), Metric(f"{m['WallSeconds']:.2f}", comparison=m["WallSeconds"]), m["Date"], r["Checksum"],
                     f"[{path.name}](results/{path.name})", hashlib.sha256(path.read_bytes()).hexdigest()))
    lines += table(["Профиль", "Фреймворк", "Тики", "Окна", "Реальное время, s", "Дата", "Итоговый checksum", "JSON", "JSON SHA256"],
                   shade_columns(rows, [4], group_by=[0],
                                 absolute_thresholds=lambda row, column: heat_policy.threshold(row[0], "wall_seconds")))
    lines += ["", "Конфигурация: seed 7331, 96 эскортов и 3 авианосца на команду (198 кораблей в начале), "
              "churn low/nominal/high = 0.5/1/2, particles 1, без realtime-пейсинга. Остальные параметры BattleConfig проверены на соответствие значениям по умолчанию.",
              "", "ChurnMultiplier меняет cooldown стрельбы, интервал производства и длительность boost. "
              "Это интерактивная динамика боя с обратными связями: более частая стрельба влияет на нагрев, "
              "уничтожение кораблей и последующее число участников и операций. Параметр не является чистым "
              "множителем частоты компонентных операций: high=2 не означает вдвое больше фактического churn. "
              "Ориентироваться следует на измеренные StructuralAdds/StructuralRemoves и их поминутные частоты "
              "на графиках rate-StructuralAdds/rate-StructuralRemoves, а не на число в названии профиля. "
              "Все пять фреймворков сравниваются ВНУТРИ каждого профиля на одинаковом battle trace по проверенным "
              "checkpoints и Counters. Между профилями battle trace различается по замыслу; это разные workload, "
              "не изолированный эксперимент с удвоением одной операции.",
              "", "Методика нового набора: GameplayRules.Revision=predictive-neutral-v1 проверена у всех 15 запусков. "
              "Predictive AI aim решает квадратное уравнение перехвата при постоянных скоростях с учётом muzzle offset "
              "и унаследованной скорости стреляющего корабля. Небольшой детерминированный spread до 0.03 rad "
              "выбирается hash-решением с вероятностью 0.25 на firing decision/burst; вся очередь получает одинаковый offset. "
              "Это не обещание ровно четверти решений в коротких выборках или четверти снарядов/попаданий. "
              "Projectile и Debris не имеют Team; Owner используется только для телеметрии, Target - для homing, не для иммунитета. "
              "Под объектами столкновения здесь понимаются включённые ship fixtures. Particles/Debris не имеют health/body; "
              "перехват снарядами других снарядов не заявляется. Снаряды наносят урон любому такому столкнувшемуся кораблю; "
              "командная принадлежность не даёт иммунитета. Эти правила меняют траектории, попадания и обратные связи боя. "
              "После их изменения требуется пересборка и полный повтор 15 запусков; прежний набор не является данными "
              "для новых правил. SHA256 результатов проверены относительно текущих Release-бинарников. "
              "Checkpoints/Counters подтверждают согласованность trace портов; revision фиксирует заявленную методику, "
              "но не заменяет проверку реализации Core.",
              "", "### Совпадающие логические счётчики", ""]
    lines += table(["Метрика", *PROFILES], [(k, *(runs[p, "secs"]["Counters"][k] for p in PROFILES)) for k in COUNTERS])
    lines += ["", "Каждая строка одинакова для всех пяти фреймворков, а не является их суммой. "
              "Поминутная частота структурных операций вычисляется как разность накопительных счётчиков / 60 секунд симуляции. "
              "Первое окно включает начальную композицию: снимка счётчиков на tick 0 нет. Последующие окна отражают разности за соответствующую минуту."]
    lines += ["", "### Фактические события механизмов", "",
              "Наличие механизма и его предварительная проверка не означают, что он сработал во всех профилях длинного запуска. "
              "Таблица ниже вычислена из Counters: значение >0 подтверждает зарегистрированные события в этом профиле, "
              "0 означает отсутствие зарегистрированных событий, а не отсутствие поддержки механизма. "
              "JSON не содержит статуса предварительного запуска --validate/--validate-adapters/--validate-ports; "
              "их успешность сама по себе здесь не заявляется. Минимум взят по трём профилям, не по независимым повторам.", ""]
    events = {
        "Выстрелы": "Shots", "Попадания": "Hits", "Плазма по щиту": "PlasmaShieldHits",
        "Разрушения щита": "ShieldBreaks", "Перегревы": "Overheats", "Докования": "Dockings",
        "Ремонты": "Repairs", "Строительство": "Builds", "Использование предметов": "ItemUses",
        "Попадания обломков": "DebrisHits", "Взрывы": "Explosions",
        "Решения о стрельбе": "AimDecisions", "Решения со spread": "ImpreciseAimDecisions",
        "Наблюдаемые попадания по своим": "FriendlyFireHits", "Попадания в себя": "SelfHits",
    }
    rows = []
    for label, counter in events.items():
        counts = [runs[p, "secs"]["Counters"][counter] for p in PROFILES]
        rows.append((label, counter, *counts, min(counts)))
    lines += table(["Механизм", "Counter", *PROFILES, "Минимум по профилям"], rows)
    lines += ["", "### Прицеливание и наблюдаемый friendly fire", ""]
    rows = []
    for p in PROFILES:
        c = runs[p, "secs"]["Counters"]
        ratio = f"{100 * c['ImpreciseAimDecisions'] / c['AimDecisions']:.3f}" if c["AimDecisions"] else "не определено"
        rows.append((p, c["AimDecisions"], c["ImpreciseAimDecisions"], ratio, c["FriendlyFireHits"], c["SelfHits"]))
    lines += table(["Профиль", "AimDecisions", "ImpreciseAimDecisions", "Наблюдаемая доля %", "FriendlyFireHits", "SelfHits"], rows)
    lines += ["", "Значения совпадают у всех пяти фреймворков внутри профиля. Наблюдаемая доля равна "
              "ImpreciseAimDecisions / AimDecisions, не ImpreciseAimDecisions / Shots: одна очередь может создать несколько "
              "снарядов или быть ограничена нагревом/лимитом. FriendlyFireHits учитывает попадания только когда "
              "Identity владельца ещё присутствует в ECS, до Destroy/Commit; это не обязательно означает положительный hull. "
              "После удаления Identity полная атрибуция не сохраняется. Это неполное число friendly-fire "
              "попаданий, а не полный учёт или ноль после гибели владельца. SelfHits - подмножество FriendlyFireHits, "
              "их нельзя складывать. Нулевые значения сохраняются без предположения, что каждый механизм обязательно сработал."]
    m = runs["nominal", "secs"]["Metadata"]
    lines += ["", f"Среда: {m['Machine']}; {m['Os']}; {m['Runtime']}; TieredCompilation={m['TieredCompilation']!r}.",
              f"RAM: {hardware['TotalPhysicalMemoryBytes'] / (1024 ** 3):.2f} GiB; OS inventory: {hardware['Os']} "
              f"{hardware['OsVersion']}; видимых PowerShell логических процессоров: {hardware['LogicalProcessorCountVisibleToPowerShell']}.",
              f"GPU/driver фронтенда: {frontend['Driver']['Vendor']}; {frontend['Driver']['Renderer']}; {frontend['Driver']['Version']}.", ""]
    lines += table(["CPU", "Производитель", "Ядра", "Логические процессоры", "MaxClockSpeed, MHz (номинальный)"],
                   [(c["Name"], c["Manufacturer"], c["NumberOfCores"], c["NumberOfLogicalProcessors"], c["MaxClockSpeed"]) for c in hardware["Cpu"]])
    lines += ["", f"[hardware.json](hardware.json), CapturedAt={hardware['CapturedAt']}, "
              f"SHA256={hashlib.sha256((DOCS / 'hardware.json').read_bytes()).hexdigest()}. "
              "Инвентаризация выполнена после измерений; Machine совпадает с headless. MaxClockSpeed - "
              "номинальная максимальная частота из CIM, не измеренная частота во время бенчмарка.",
              f"Область измерений (исходные поля metadata): TimingScope={m['TimingScope']}; MemoryScope={m['MemoryScope']}.", "", "## Время тика", ""]
    rows = []
    for (p, f), r in runs.items():
        t = r["Timing"]
        rows.append((p, f, *(Metric(f"{t[k]:.4f}", comparison=t[k]) for k in ("MeanMs", "P95Ms", "P99Ms", "MaxMs")),
                     t["BudgetMisses"], Metric(f"{t['BudgetMissPercent']:.5f}", comparison=t['BudgetMissPercent']),
                     Metric(f"{r['AllocatedBytes'] / TICKS:.2f}", comparison=r['AllocatedBytes'] / TICKS)))
    timing_columns = {2: "tick.mean_ms", 3: "tick.p95_ms", 4: "tick.p99_ms", 5: "tick.max_ms"}
    lines += table(["Профиль", "Фреймворк", "Mean ms", "p95 ms", "p99 ms", "Max ms", "Тики >16.67ms", "Miss %", "Step managed alloc B/tick"],
                   shade_columns(rows, range(2, 9), group_by=[0],
                                 absolute_thresholds=lambda row, column: heat_policy.threshold(row[0], timing_columns.get(column))))
    lines += ["", "Глобальные квантили взяты непосредственно из JSON полного запуска, а не усреднены из минутных квантилей. "
              "AllocatedBytes и приведённые B/tick означают managed-аллокации текущего потока строго внутри Step; "
              "timing-квантили и эта метрика не включают последующую обработку результатов.", "", "### GC и широкие интервалы обвязки", ""]
    rows = []
    for (p, f), r in runs.items():
        sample = gc_samples[p, f]
        rows.append((p, f, "/".join(map(str, sample["FirstPostStep"])), "/".join(map(str, sample["LastPostStep"])),
                     "/".join(map(str, sample["Delta"])), "/".join(map(str, r["GcCollections"])), r["HarnessAndSimulationAllocatedBytes"]))
    lines += table(["Профиль", "Фреймворк", "GC sample после tick 1 (0/1/2)", "GC sample после tick 108000 (0/1/2)",
                    "CSV GC delta tick 1→108000 (0/1/2)", "JSON GC широкий интервал (0/1/2)", "HarnessAndSimulationAllocatedBytes широкий интервал, B"],
                   shade_columns(rows, [6], group_by=[0]))
    lines += ["", "CSV GC delta = последний post-Step GC sample минус первый post-Step sample, ticks 1→108000. "
              "Интервал включает телеметрию между Step, но исключает неизвестные GC первого Step. Начальный baseline "
              "tick 0 не сохранён: точное число GC только за игровой цикл из этих данных восстановить нельзя. "
              "Поля Gc в минутных окнах также являются абсолютными накопительными значениями.", "",
              "JSON GcCollections вычислен позже: после battle.Validate, чтения DLL/SHA256 provenance и глобального Stats(timing). "
              "Он охватывает более широкий интервал, включая эту постцикловую работу, и не является счётчиком только gameplay/loop. "
              "Разность JSON GcCollections и CSV sample delta нельзя атрибутировать: она может включать GC до первого "
              "post-Step sample (в том числе первый Step) и после последнего sample (телеметрия и постцикловая работа). "
              "Без сохранённого tick-zero baseline нельзя различить эти источники. Даже если broad JSON превышает "
              "sample delta ровно на одну сборку, это не доказывает одну дополнительную post-loop GC. "
              "HarnessAndSimulationAllocatedBytes также вычислен после этих операций и включает работу обвязки внутри своего "
              "интервала, поминутную телеметрию и постцикловую validation/hashing/global timing aggregation; это не Step allocation. "
              "Исходные JSON/CSV не изменены, широкие значения не вычитаются для получения вымышленного tick-zero baseline.", "", "## Выборки памяти", ""]
    rows = []
    for (p, f), r in runs.items():
        for metric in (*MEMORY, "NativeBytes"):
            samples = [w["Store"][metric] if metric == "NativeBytes" else w[metric] for w in r["Windows"]]
            known = [(i + 1, v / MIB) for i, v in enumerate(samples) if v is not None]
            if not known:
                rows.append((p, f, metric, "неизвестно", "неизвестно", "неизвестно", "неизвестно"))
                continue
            minute, peak = max(known, key=lambda pair: pair[1])
            slope = sum((i - sum(x for x, _ in known) / len(known)) * (v - sum(y for _, y in known) / len(known)) for i, v in known)
            denominator = sum((i - sum(x for x, _ in known) / len(known)) ** 2 for i, _ in known)
            rows.append((p, f, metric, Metric(f"{peak:.2f}", f" @ {minute}", comparison=peak),
                         Metric(f"{known[-1][1]:.2f}", comparison=known[-1][1]),
                         f"{known[-1][1] - known[0][1]:+.2f}", f"{slope / denominator:+.3f}" if denominator else "неизвестно"))
    lines += table(["Профиль", "Фреймворк", "Метрика", "Пик выборок MiB @ минута", "Последняя MiB", "Последняя-первая MiB", "OLS MiB/min"],
                   shade_columns(rows, [3, 4], group_by=[0, 2]))
    lines += ["", "Пики - максимумы выборок в конце каждой минуты, а не непрерывно измеренные пики процесса. "
              "Тренды - описательные OLS-аппроксимации, не диагностика утечек. Managed heap, process private и working set "
              "являются разными метриками, их нельзя складывать. Native storage SECS показан отдельно; неизвестные diagnostics "
              "других фреймворков остаются null, а не нулём. Память процесса включает буферы измерительной обвязки.",
              "", "## Влияние фаз", ""]
    rows = []
    for (p, f), r in runs.items():
        phase_means = []
        for start, end in ((0, 10), (10, 20), (20, 30)):
            phase_means.append(sum(w["Timing"]["MeanMs"] for w in r["Windows"][start:end]) / 10)
        stage = max(r["Stages"], key=lambda s: s["Timing"]["MeanMs"])
        physics = next(s for s in r["Stages"] if s["Name"] == "physics")["Timing"]["MeanMs"]
        hits = next(s for s in r["Stages"] if s["Name"] == "hits")["Timing"]["MeanMs"]
        rows.append((p, f, *(Metric(f"{v:.4f}", comparison=v) for v in phase_means), f"{phase_means[-1] / phase_means[0]:.3f}" if phase_means[0] else "не определено",
                     stage["Name"], f"{stage['Timing']['MeanMs'] / r['Timing']['MeanMs'] * 100:.1f}",
                     f"{physics / r['Timing']['MeanMs'] * 100:.1f}",
                     f"{(physics + hits) / r['Timing']['MeanMs'] * 100:.1f}"))
    phase_columns = {2: "phase.early.mean_ms", 3: "phase.middle.mean_ms", 4: "phase.late.mean_ms"}
    lines += table(["Профиль", "Фреймворк", "Мин 1-10 mean ms", "Мин 11-20 mean ms", "Мин 21-30 mean ms", "Поздняя/ранняя", "Наибольшая стадия", "Stage % of Step", "physics % of Step", "physics + hits % of Step"],
                   shade_columns(rows, [2, 3, 4], group_by=[0],
                                 absolute_thresholds=lambda row, column: heat_policy.threshold(row[0], phase_columns[column])))
    lines += ["", "Фазы по фиксированным интервалам времени симуляции описывают наблюдения, а не причинный эксперимент. "
              "Средние времена стадий можно складывать; p95/p99 стадий складывать нельзя. Доля physics + hits вычислена "
              "из суммы средних времён этих стадий относительно среднего Step отдельно для каждого запуска, не задана заранее. "
              "Большая доля означает, что общая физика и обработка попаданий занимают значительную часть именно этого workload "
              "и могут скрывать различия ECS. Это не прямое доказательство универсального выигрыша какого-либо фреймворка. "
              "Один запуск на комбинацию профиля и фреймворка не даёт оценки уверенности в ранжировании.", "", "## Графики", ""]
    for name in charts:
        lines += [f"![{name}](charts/{name})", ""]
    lines += frontend_report(frontend)
    lines += ["## Связь с микробенчмарками", "",
              "ShipsCreated/ShipsDestroyed в таблице совпадающих счётчиков - итоговые логические Create/Destroy кораблей, "
              "а не все операции жизненного цикла сущностей ECS. Создание кораблей включает начальную композицию, пополнение "
              "и строительство. Shots учитывает создание снарядов, но накопительные создания частиц/обломков и итоговые "
              "уничтожения временных сущностей не записаны. Количества живых сущностей в конце минуты не позволяют восстановить "
              "создания/уничтожения между выборками: полного числа transient-операций или знаменателя Create/Destroy в этой схеме нет.", "",
              "StructuralAdds/StructuralRemoves и частоты их минутных приращений считают общие логические операции "
              "с компонентами, не вызовы Create/Destroy сущностей. Они включают реальные изменения состояний: перегрев, бой, "
              "ускорение, разрушение щита и докование. Стадия lifecycle смешивает изменения состояний, удаление сущностей, "
              "эффекты, строительство, structural commits и итоговую сборку/сортировку. Деление её времени на количество "
              "кораблей, выстрелов или компонентных операций не даёт Create/Destroy ns/op отдельного примитива.", "",
              "Прежний выигрыш в микробенчмарке плоской итерации не предсказывает суммарную производительность Step: "
              "в неё входят общая физика, five-ray CCD, AI, сортировка и случайные обращения к состояниям/компонентам через "
              "native stores и identity bridge. Средние времена стадий и частоты churn описывают этот workload, "
              "а не изолированные примитивы ECS; суммарную разницу нельзя приписать одной итерации.", ""]
    lines += ["## Адаптация и ограничения", "",
              "Все пять используют NativeStore identity translation с ограниченными переиспользуемыми слотами и обратным "
              "поиском живых сущностей. Общая стоимость identity layer, включая dictionary lookup, присутствует у всех пяти "
              "и может маскировать различия нативных API. Компоненты и запросы принадлежат нативным фреймворкам, "
              "а не параллельной теневой ECS. Classic, Lite и Morpeh добавляют приватный LifetimeMarker, "
              "чтобы сохранить пустую сущность при композиции, в том числе до Commit. Поэтому эти три адаптера имеют по дополнительному "
              "native-компоненту; общие structural counters считают только логические компоненты и исключают маркеры. "
              "Morpeh/Static Wrapper<T> сохраняет sizeof(T) unmanaged-данных. Остальные логические layout компонентов совпадают. "
              "Специальные benchmark API SECS accessor/factory не используются. Free-ID слоты сервиса Aether ограничены "
              "максимальным числом когда-либо выделенных слотов (high-water mark).", "",
              "Общий Aether.Physics2D использует составные fixtures и five-ray приближение projectile CCD, не точную swept collision. "
              "Обломки движутся без damping; удаляются по истечении 3600 тиков жизни, при попадании или выходе "
              "за границу 2.5 * ArenaHalfSize по любой координате. Это очистка жизненного цикла, не drag. "
              "Validate отклоняет NaN/нефинитные состояния; автоматическое удаление NaN здесь не заявляется. "
              "Одна машина, один seed и отсутствие повторов не дают статистически устойчивого результата; доверительные "
              "интервалы и универсальное ранжирование не заявляются. 30 минут означают время симуляции, не реальную длительность "
              "запуска. Сеть, стоимость рендеринга, задержка ввода, replay и другое оборудование не покрыты headless-измерениями. "
              "UI/рендеринг рассмотрены отдельно в фиксированном frontend replay, не в headless-рейтинге. "
              "Ни остаток бюджета headless Step, ни снимок, ни uncapped replay не подтверждают realtime 60 FPS фронтенда. "
              "Равенство checkpoints подтверждает записанный детерминированный workload, но не все возможные состояния или свойства корректности.", "",
              "Source pins и SHA256 бинарников включены в каждый JSON и проверены на согласованность; "
              "SECS собран из оптимизированного рабочего дерева, а не чистого закреплённого commit. JSON SHA256 выше идентифицирует "
              "проанализированные данные. Происхождение сгенерированных спрайтов и полный prompt: "
              "[Asset provenance](../../SpaceBattle.Game/Assets/PROVENANCE.md). "
              "[Фронтенд и правила захвата](../../SpaceBattle.Game/README.md).", ""]
    return "\n".join(lines)


def write_csv(runs, gc_samples):
    with (DOCS / "summary.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["profile", "framework", "meanMs", "p95Ms", "p99Ms", "maxMs", "budgetMisses", "stepManagedAllocatedBytesPerTick",
                         "csvGcGen0DeltaPostStepTick1To108000", "csvGcGen1DeltaPostStepTick1To108000", "csvGcGen2DeltaPostStepTick1To108000",
                         "jsonGcGen0BroadIncludingPostLoop", "jsonGcGen1BroadIncludingPostLoop", "jsonGcGen2BroadIncludingPostLoop",
                         "harnessAndSimulationAllocatedBytesBroadIncludingPostLoop"])
        for (p, f), r in runs.items():
            writer.writerow([p, f, *(r["Timing"][k] for k in ("MeanMs", "P95Ms", "P99Ms", "MaxMs", "BudgetMisses")),
                             r["AllocatedBytes"] / TICKS, *gc_samples[p, f]["Delta"], *r["GcCollections"], r["HarnessAndSimulationAllocatedBytes"]])
    with (DOCS / "windows.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["profile", "framework", "minute", "meanMs", "p95Ms", "p99Ms", *MEMORY, "NativeBytes", "Ships", "Projectiles", "Debris", "Particles", "addsPerSimSecond", "removesPerSimSecond"])
        for (p, f), r in runs.items():
            previous = dict(StructuralAdds=0, StructuralRemoves=0)
            for i, w in enumerate(r["Windows"], 1):
                writer.writerow([p, f, i, *(w["Timing"][k] for k in ("MeanMs", "P95Ms", "P99Ms")), *(w[k] for k in MEMORY), w["Store"]["NativeBytes"], *(w[k] for k in ("Ships", "Projectiles", "Debris", "Particles")), *((w["Counters"][k] - previous[k]) / 60 for k in ("StructuralAdds", "StructuralRemoves"))])
                previous = w["Counters"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--heatmap-policy", type=Path, default=Path(__file__).with_name("heatmap_policy.json"))
    arguments = parser.parse_args()
    DOCS.mkdir(parents=True, exist_ok=True)
    try:
        heat_policy = TimingPolicy.from_file(arguments.heatmap_policy)
        runs, errors = collect()
        try:
            capture = screenshot()
        except (ValueError, KeyError, TypeError, OSError) as error:
            errors.append("screenshot: " + str(error))
        require(not errors, "\n".join(errors))
        frontend = validate_frontend(runs)
        validate_parity(runs, capture, frontend)
        hardware = validate_hardware(runs)
        gc_samples = tick_gc_samples(runs)
        write_assets(ROOT / "docs")
        charts = plots(runs)
        write_csv(runs, gc_samples)
        english, russian = report_versions(report(runs, capture, charts, frontend, hardware, gc_samples, heat_policy),
                                           DOCS, extra_phrases=heat_policy.phrases)
        (DOCS / "REPORT_2026-10-11.md").write_text(english.replace("REPORT.ru.md", "REPORT_2026-10-11.ru.md", 1), encoding="utf-8")
        (DOCS / "REPORT_2026-10-11.ru.md").write_text(russian.replace("(REPORT.md)", "(REPORT_2026-10-11.md)", 1), encoding="utf-8")
    except Exception as error:
        message = "FAIL: analysis/comparison incomplete\n" + str(error)
        (DOCS / "REPORT_2026-10-11.ru.md").write_text("[English](REPORT_2026-10-11.md) | Русский\n\n![Обложка текущего геймплея](cover.png)\n\n"
            "Обложка: текущий геймплей, а не зафиксированный измеренный прогон. [Процесс захвата](COVER.ru.md).\n\n# FAIL: анализ не завершён\n\n"
            "Сопоставимость и завершение не подтверждены. Существующие графики/CSV могут быть устаревшими и не являются данными этой неудачной попытки.\n\n```text\n"
            + message + "\n```\n", encoding="utf-8")
        (DOCS / "REPORT_2026-10-11.md").write_text("English | [Русский](REPORT_2026-10-11.ru.md)\n\n![Current gameplay cover](cover.png)\n\n"
            "Cover: current gameplay, not the frozen measured run. [Capture workflow](COVER.md).\n\n# FAIL: Analysis Incomplete\n\n"
            "Comparability and completion are not confirmed. Existing charts/CSVs may be stale and are not evidence for this failed attempt.\n\n```text\n"
            + message + "\n```\n", encoding="utf-8")
        print(message, file=sys.stderr)
        return 1
    print("PASS: 15 timing runs (1,620,000 ticks) + 3 composition replays (324,000 ticks); wrote English/Russian reports, charts and CSVs")
    return 0


if __name__ == "__main__":
    sys.exit(main())
