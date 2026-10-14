"""Validate and report the current SpaceBattle campaign, offline.

Tick CSVs are local verification inputs. Public evidence is the JSON matrix,
summary CSV, charts and manifest with source/binary/input hashes.
"""

import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import sys

from heatmap import Metric, PALETTE, EQUAL_COLOR, TimingPolicy, colors, compact_legend, shade_columns, shade_rows

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs/space-battle"
PROFILES = {"low": 0.5, "nominal": 1, "high": 2}
CURRENT_PROFILE = "high"
FRAMEWORKS = {"secs": "SECS", "lite": "LeoECSLite", "classic": "LeoECS Classic",
              "morpeh": "Morpeh", "static": "StaticEcs 2.2.9"}
VERIFIED_VERSIONS = {
    ("SECS", "beca1e4c0277ca7f03b972a8d4ffef789b45ab19"): ("0.1.0", None),
    ("LeoECS Lite", "5053141d7d663c6b1855c8409ac93dfac807b239"):
        ("2024.5.22", "https://github.com/Leopotam/ecslite/releases/tag/2024.5.22"),
    ("LeoECS Classic", "1213c31c9ef8325d48e74a52b345c4db242d038b"):
        ("2023.6.22", "https://github.com/Leopotam/ecs/releases/tag/2023.6.22"),
    ("Morpeh", "9f6369384792a01d74df24efd35b08ae313e4f12"):
        ("2024.1.1", "https://github.com/scellecs/morpeh/releases/tag/2024.1.1"),
    ("StaticEcs", "23b1cf36d89497e8a126d38459d688e2b0a5dea3"):
        ("2.2.9", "https://github.com/Felid-Force-Studios/StaticEcs/releases/tag/v2.2.9"),
}
RULES = {"Revision": "mounted-armament-v7", "TacticalRevision": "layered-squads-v3",
         "PhysicsRevision": "circle-kernel-v1", "DamageRevision": "layered-weapon-damage-v1"}
BUDGET = 1000 / 60
TICKS = 108000
HUD_WINDOW = 600
COUNTS = ("Ships", "Projectiles", "Debris", "Particles", "Turrets")
STAGE_NAMES = ("collect", "ai", "thermal-inventory", "steering", "weapons", "carriers", "physics", "hits", "lifecycle")
PRESETS = {
    "benchmark": {"FleetComposition": 0, "Deployment": 0, "EndlessReinforcements": False, "ReplenishCarriers": True},
    "game": {"FleetComposition": 1, "Deployment": 1, "EndlessReinforcements": True, "ReplenishCarriers": False},
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def version_cell(framework, pin):
    version = VERIFIED_VERSIONS.get((framework, pin))
    require(version is not None, f"No verified version for {framework} at {pin}")
    number, url = version
    return f"[{number}]({url})" if url else f"`{number}`"


def read_json(path):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, f"{path.name}: duplicate key {key}")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=pairs)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def close(actual, expected, label):
    require(math.isfinite(actual) and math.isclose(actual, expected, rel_tol=1e-8, abs_tol=1e-9),
            f"{label}: {actual} differs from {expected}")


def validate_stats(stats, values, label):
    ordered = sorted(values)
    count = len(ordered)
    require(count > 0 and all(math.isfinite(v) and v >= 0 for v in values), label + ": invalid timing")
    total = sum(ordered)
    expected = {"MeanMs": total / count,
                "StddevMs": math.sqrt(max(0, (sum(v*v for v in ordered) - total*total/count) / (count-1))) if count > 1 else 0,
                "MedianMs": ordered[math.ceil((count-1)*0.5)],
                "P95Ms": ordered[math.ceil((count-1)*0.95)],
                "P99Ms": ordered[math.ceil((count-1)*0.99)], "MaxMs": ordered[-1],
                "BudgetMisses": sum(v > BUDGET for v in values)}
    expected["BudgetMissPercent"] = expected["BudgetMisses"] * 100 / count
    for key, value in expected.items():
        close(stats[key], value, label + "." + key)


def validate_summary(stats, count, label):
    """Check published summaries without pretending that quantiles can be reconstructed."""
    for key in ("MeanMs", "StddevMs", "MedianMs", "P95Ms", "P99Ms", "MaxMs", "BudgetMissPercent"):
        value = stats[key]
        require(isinstance(value, (int, float)) and not isinstance(value, bool) and
                math.isfinite(value) and value >= 0, label + ": invalid " + key)
    require(stats["MedianMs"] <= stats["P95Ms"] <= stats["P99Ms"] <= stats["MaxMs"] and
            stats["MeanMs"] <= stats["MaxMs"], label + ": inconsistent timing summary")
    misses = stats["BudgetMisses"]
    require(type(misses) is int and 0 <= misses <= count, label + ": invalid BudgetMisses")
    close(stats["BudgetMissPercent"], misses * 100 / count, label + ".BudgetMissPercent")


def validate_preset(metadata, preset, label):
    require(preset in PRESETS, label + ": unknown preset")
    # Old reports predate explicit preset metadata and used benchmark defaults.
    require(metadata.get("Preset", "benchmark") == preset, label + ": wrong Preset")
    options = dict(metadata["Options"])
    for key, expected in PRESETS[preset].items():
        require(key in options and type(options[key]) is type(expected) and options[key] == expected,
                label + ": wrong preset option " + key)
    return options


def validate_stages(stages, count, label):
    require(tuple(stage["Name"] for stage in stages) == STAGE_NAMES, label + ": wrong/missing/duplicate stages")
    for stage in stages:
        validate_summary(stage["Timing"], count, label + "." + stage["Name"])


def validate_hud(run, label):
    windows = run.get("HudWindows")
    if windows is None:
        require(run["Metadata"].get("Preset") is None, label + ": explicit preset requires HudWindows")
        return
    require(len(windows) == TICKS // HUD_WINDOW, label + ": incomplete HudWindows")
    validate_summary(run["HudTiming"], TICKS, label + ".HudTiming")
    for i, window in enumerate(windows):
        require((window["StartTick"], window["EndTick"]) == (i*HUD_WINDOW+1, (i+1)*HUD_WINDOW),
                label + ": noncontiguous HudWindows")
        validate_summary(window["Timing"], HUD_WINDOW, label + f".HudWindows[{i}]")
        validate_stages(window["Stages"], HUD_WINDOW, label + f".HudWindows[{i}].Stages")
        for key in COUNTS:
            value = window["Mean" + key]
            require(isinstance(value, (int, float)) and not isinstance(value, bool) and
                    math.isfinite(value) and value >= 0, label + ": invalid Mean" + key)
    for i, stage in enumerate(run["Stages"]):
        close(sum(w["Stages"][i]["Timing"]["MeanMs"] for w in windows) / len(windows),
              stage["Timing"]["MeanMs"], label + ": HUD stage mean " + stage["Name"])
    close(sum(w["Timing"]["MeanMs"] for w in windows) / len(windows),
          run["HudTiming"]["MeanMs"], label + ": HUD overall mean")


def total_step_seconds(run):
    return run["Timing"]["MeanMs"] * run["Metadata"]["Ticks"] / 1000


def hud_selection(runs, profile):
    """Choose one common worst phase; each ECS is compared at that same tick range."""
    windows = runs[profile, "secs"].get("HudWindows", [])
    if not windows:
        return []
    worst = max(range(len(windows)), key=lambda i: max(
        runs[profile, f]["HudWindows"][i]["Timing"]["MeanMs"] for f in FRAMEWORKS))
    return [("first", 0), ("second", 1), ("worst", worst)]


def canonical(run):
    return {"Checkpoints": run["Checkpoints"], "Checksum": run["Checksum"],
            "Counters": run["Counters"], "Tactics": run["Tactics"],
            "Windows": [{key: w[key] for key in ("StartTick", "EndTick", "Ships", "Projectiles",
                         "Debris", "Particles", "Turrets", "Counters")} for w in run["Windows"]],
            "HudCounts": [{key: w[key] for key in ("StartTick", "EndTick", *("Mean" + k for k in COUNTS))}
                          for w in run.get("HudWindows", [])]}


def validate_current_profile(manifest):
    require(manifest.get("Format") == 3, "Current campaign requires Format 3")
    require(manifest.get("Profile") == CURRENT_PROFILE and "Profiles" not in manifest and
            "PrimaryProfile" not in manifest, "Current campaign must have only one real-game high profile")
    require(tuple(manifest.get("FrameworkOrder", ())) == tuple(FRAMEWORKS),
            "Current campaign must include exactly the five ECS implementations")
    workload = manifest["Workload"]
    require(set(workload) == {"Preset", "Seed", "ShipsPerTeam", "GameplayChurnMultiplier"} and
            workload["Preset"] == "game" and workload["Seed"] == 7331 and
            workload["ShipsPerTeam"] == 96 and workload["GameplayChurnMultiplier"] == 2,
            "Current campaign must use the real game high workload without synthetic parameters")


def validate(directory, verify_ticks, expected_preset=None):
    manifest = read_json(directory / "manifest.json")
    if directory.resolve() == (DOCS / "current").resolve():
        require(manifest.get("Format") == 3,
                "The current report requires the single real-game high campaign (Format 3)")
    if manifest.get("Format", 1) == 3:
        return validate_format3(directory, manifest, verify_ticks, expected_preset)
    require(manifest.get("Format", 1) == 1, "Unsupported campaign format")
    require(manifest["Completed"] is True, "Campaign is incomplete")
    require(manifest["Ticks"] == 108000 and manifest["WarmupTicks"] == 600,
            "Published campaign requires 108000 ticks and 600 separate warmup ticks")
    preset = manifest.get("Preset", "benchmark")
    require(preset in PRESETS, "Unknown campaign preset")
    require(expected_preset is None or preset == expected_preset, "Unexpected campaign preset")
    require(bool(manifest["SourceRevision"]) and bool(manifest["SourceHashes"]), "Missing source provenance")
    expected_files = {f"{p}-{f}.json" for p in PROFILES for f in FRAMEWORKS}
    actual_files = {p.name for p in directory.glob("*.json")} - {"manifest.json", "hardware.json"}
    require(actual_files == expected_files, "Expected exactly the complete 15-case JSON matrix")
    for name, expected in manifest["Files"].items():
        path = directory / name
        require(path.parent == directory and path.name == name, "Invalid manifest filename")
        if path.is_file():
            require(digest(path) == expected, "Input hash mismatch: " + name)
        else:
            require(not verify_ticks and name.endswith(".ticks.csv"), "Missing input: " + name)
    hardware = read_json(directory / "hardware.json")
    runs, conditions, provenance = {}, None, None
    for profile, churn in PROFILES.items():
        baseline = None
        for framework, name in FRAMEWORKS.items():
            filename = f"{profile}-{framework}.json"
            require(filename in manifest["Files"], "Unhashed result: " + filename)
            if verify_ticks:
                csv_name = f"{profile}-{framework}.ticks.csv"
                require(csv_name in manifest["Files"], "Unhashed tick input: " + csv_name)
            run = read_json(directory / filename)
            m = run["Metadata"]
            require(m["Framework"] == name, filename + ": wrong framework")
            for key, expected in {"Ticks": 108000, "WarmupTicks": 600, "TargetTickRate": 60,
                                  "SimulatedSeconds": 1800, "Realtime": False, "TieredCompilation": "0"}.items():
                require(m[key] == expected, filename + ": wrong " + key)
            close(m["BudgetMs"], BUDGET, filename + ".BudgetMs")
            options = validate_preset(m, preset, filename)
            require(("Preset" in m) == ("Preset" in manifest), filename + ": inconsistent explicit preset metadata")
            require(options.pop("ChurnMultiplier") == churn, filename + ": wrong churn")
            require(options["Seed"] == 7331 and options["ShipsPerTeam"] == 96 and options["CarriersPerTeam"] == 3,
                    filename + ": wrong fleet/seed")
            validate_summary(run["Timing"], TICKS, filename + ".Timing")
            validate_stages(run["Stages"], TICKS, filename + ".Stages")
            validate_hud(run, filename)
            require(all(m["GameplayRules"][key] == value for key, value in RULES.items()), filename + ": stale gameplay rules")
            condition = (m["Machine"], m["Runtime"], m["Os"], options, m["GameplayRules"], m["Collision"])
            if conditions is None:
                conditions, provenance = condition, run["Provenance"]
            require(condition == conditions, filename + ": mixed campaign conditions")
            require(run["Provenance"] == provenance, filename + ": mixed source/binary provenance")
            require(m["Machine"] == hardware["Machine"], filename + ": hardware host mismatch")
            require(len(run["Windows"]) == len(run["Checkpoints"]) == 30, filename + ": incomplete windows")
            for i, (window, point) in enumerate(zip(run["Windows"], run["Checkpoints"])):
                validate_summary(window["Timing"], 3600, filename + f".Windows[{i}]")
                require((window["StartTick"], window["EndTick"], point["Tick"]) == (i*3600+1, (i+1)*3600, (i+1)*3600),
                        filename + ": noncontiguous windows/checkpoints")
                require(len(point["Checksum"]) == 16 and all(c in "0123456789ABCDEF" for c in point["Checksum"]),
                        filename + ": malformed checksum")
                require(window["Store"]["Entities"] == sum(window[k] for k in ("Ships", "Projectiles", "Debris", "Particles", "Turrets")),
                        filename + ": store census mismatch")
            require(run["Checkpoints"][-1]["Checksum"] == run["Checksum"] and run["Windows"][-1]["Counters"] == run["Counters"],
                    filename + ": final state mismatch")
            require(sum(w["AllocatedBytes"] for w in run["Windows"]) == run["AllocatedBytes"], filename + ": allocations mismatch")
            close(sum(w["Timing"]["MeanMs"] for w in run["Windows"]) / 30,
                  run["Timing"]["MeanMs"], filename + ": minute Step mean")
            state = canonical(run)
            if baseline is None:
                baseline = state
            require(state == baseline, filename + ": port divergence")
            if verify_ticks:
                validate_ticks(directory, profile, framework, run)
            runs[profile, framework] = run
    return runs, manifest, hardware


def validate_format3(directory, manifest, verify_ticks, expected_preset=None):
    """Validate the single real-game high campaign without weakening archived Format 1 checks."""
    require(manifest["Completed"] is True, "Campaign is incomplete")
    validate_current_profile(manifest)
    require(manifest["Ticks"] == TICKS and manifest["WarmupTicks"] == 600,
            "Current campaign requires 108000 ticks and 600 separate warmup ticks")
    require(manifest.get("Preset") == "game", "Current campaign requires the game preset")
    require(expected_preset is None or expected_preset == "game", "Unexpected campaign preset")
    require(bool(manifest.get("SourceRevision")) and bool(manifest.get("SourceHashes")), "Missing source provenance")
    expected_files = {"hardware.json"}
    for framework in FRAMEWORKS:
        expected_files.add(f"high-{framework}.json")
        expected_files.add(f"high-{framework}.ticks.csv")
    actual_json = {p.name for p in directory.glob("*.json")} - {"manifest.json"}
    require(actual_json == {name for name in expected_files if name.endswith(".json")},
            "Expected exactly five high result JSON files and hardware.json")
    require(set(manifest["Files"]) == expected_files, "Current manifest must list only the five high cases")
    actual_csv = {p.name for p in directory.glob("*.ticks.csv")}
    require(not actual_csv or actual_csv == {name for name in expected_files if name.endswith(".ticks.csv")},
            "Current result directory contains extra tick CSV files")
    for name, expected in manifest["Files"].items():
        path = directory / name
        require(path.parent == directory and path.name == name, "Invalid manifest filename")
        if path.is_file():
            require(digest(path) == expected, "Input hash mismatch: " + name)
        else:
            require(not verify_ticks and name.endswith(".ticks.csv"), "Missing input: " + name)
    hardware = read_json(directory / "hardware.json")
    runs, conditions, provenance = {}, None, None
    initial_state = None
    profile = CURRENT_PROFILE
    port_state = None
    for framework, name in FRAMEWORKS.items():
            filename = f"high-{framework}.json"
            run = read_json(directory / filename)
            m = run["Metadata"]
            require(m["Framework"] == name, filename + ": wrong framework")
            for key, expected in {"Ticks": TICKS, "WarmupTicks": 600, "TargetTickRate": 60,
                                  "SimulatedSeconds": 1800, "Realtime": False, "TieredCompilation": "0"}.items():
                require(m[key] == expected, filename + ": wrong " + key)
            close(m["BudgetMs"], BUDGET, filename + ".BudgetMs")
            options = validate_preset(m, "game", filename)
            require(options.pop("ChurnMultiplier") == manifest["Workload"]["GameplayChurnMultiplier"],
                    filename + ": gameplay ChurnMultiplier must stay fixed")
            require(options["Seed"] == 7331 and options["ShipsPerTeam"] == 96 and options["CarriersPerTeam"] == 3,
                    filename + ": wrong fleet/seed")
            require("SyntheticChurn" not in m and not any("synthetic" in key.lower() for key in m),
                    filename + ": synthetic metadata is forbidden")
            validate_summary(run["Timing"], TICKS, filename + ".Timing")
            validate_stages(run["Stages"], TICKS, filename + ".Stages")
            validate_hud(run, filename)
            opening = run["OpeningPhases"]
            for phase_name, ticks in (("First600", 600), ("First2000", 2000)):
                phase = opening[phase_name]
                require((phase["StartTick"], phase["EndTick"]) == (1, ticks),
                        filename + ": wrong OpeningPhases." + phase_name + " tick bounds")
                validate_summary(phase["Timing"], ticks, filename + ".OpeningPhases." + phase_name + ".Timing")
                validate_summary(phase["HudTiming"], ticks, filename + ".OpeningPhases." + phase_name + ".HudTiming")
                validate_stages(phase["Stages"], ticks, filename + ".OpeningPhases." + phase_name + ".Stages")
                for key in COUNTS:
                    require(math.isfinite(phase["Mean" + key]) and phase["Mean" + key] >= 0,
                            filename + ": invalid OpeningPhases mean " + key)
            remainder = opening["RemainderAfter2000"]
            require((remainder["StartTick"], remainder["EndTick"]) == (2001, TICKS),
                    filename + ": wrong remainder tick bounds")
            validate_summary(remainder["Timing"], TICKS - 2000, filename + ".OpeningPhases.RemainderAfter2000.Timing")
            validate_stages(remainder["Stages"], TICKS - 2000, filename + ".OpeningPhases.RemainderAfter2000.Stages")
            for key in COUNTS:
                close(opening["First600"]["Mean" + key], run["HudWindows"][0]["Mean" + key],
                      filename + ": First600 mean " + key)
            close(opening["First600"]["HudTiming"]["MeanMs"], run["HudWindows"][0]["Timing"]["MeanMs"],
                  filename + ": First600 HUD timing")
            initial = run["InitialWindow"]
            require((initial["StartTick"], initial["EndTick"]) == (1, 2000), filename + ": wrong initial window")
            validate_summary(initial["Timing"], 2000, filename + ".InitialWindow.Timing")
            validate_stages(initial["Stages"], 2000, filename + ".InitialWindow.Stages")
            require(len(initial["Checksum"]) == 16 and all(c in "0123456789ABCDEF" for c in initial["Checksum"]),
                    filename + ": malformed initial checksum")
            require(all(m["GameplayRules"][key] == value for key, value in RULES.items()), filename + ": stale gameplay rules")
            condition = (m["Machine"], m["Runtime"], m["Os"], options, m["GameplayRules"], m["Collision"])
            if conditions is None:
                conditions, provenance = condition, run["Provenance"]
            require(condition == conditions, filename + ": mixed campaign conditions")
            require(run["Provenance"] == provenance, filename + ": mixed source/binary provenance")
            require(m["Machine"] == hardware["Machine"], filename + ": hardware host mismatch")
            require(len(run["Windows"]) == len(run["Checkpoints"]) == 30, filename + ": incomplete windows")
            for i, (window, point) in enumerate(zip(run["Windows"], run["Checkpoints"])):
                validate_summary(window["Timing"], 3600, filename + f".Windows[{i}]")
                require((window["StartTick"], window["EndTick"], point["Tick"]) == (i*3600+1, (i+1)*3600, (i+1)*3600),
                        filename + ": noncontiguous windows/checkpoints")
                require(window["Store"]["Entities"] == sum(window[k] for k in COUNTS),
                        filename + ": store census must contain only gameplay entities")
            require(run["Checkpoints"][-1]["Checksum"] == run["Checksum"] and run["Windows"][-1]["Counters"] == run["Counters"],
                    filename + ": final state mismatch")
            require(sum(w["AllocatedBytes"] for w in run["Windows"]) == run["AllocatedBytes"], filename + ": allocations mismatch")
            close(sum(w["Timing"]["MeanMs"] for w in run["Windows"]) / 30,
                  run["Timing"]["MeanMs"], filename + ": minute Step mean")
            state = canonical(run)
            if port_state is None:
                port_state = state
            require(state == port_state, filename + ": port divergence")
            early_state = (initial["Checksum"], initial["Counters"], tuple(initial[k] for k in COUNTS))
            if initial_state is None:
                initial_state = early_state
            require(early_state == initial_state, filename + ": gameplay diverges by tick 2000 across ECS implementations")
            if verify_ticks:
                validate_ticks(directory, profile, framework, run)
            runs[profile, framework] = run
    return runs, manifest, hardware


def validate_ticks(directory, profile, framework, run):
    path = directory / f"{profile}-{framework}.ticks.csv"
    timings, allocations, gc_samples = [], [], []
    hud_timings, stages = [], {name: [] for name in STAGE_NAMES}
    counts = {key: [] for key in COUNTS}
    with path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        has_hud = "HudWindows" in run
        require(reader.fieldnames is not None and not any("synthetic" in field.lower() for field in reader.fieldnames),
                path.name + ": synthetic CSV columns are forbidden")
        if has_hud:
            require(reader.fieldnames is not None and
                    {"hudMs", *STAGE_NAMES}.issubset(reader.fieldnames), path.name + ": missing HUD/stage samples")
        for i, row in enumerate(reader, 1):
            require(i <= TICKS, path.name + ": too many CSV ticks")
            require(int(row["tick"]) == i, path.name + ": missing/duplicate tick")
            close(float(row["simulationSeconds"]), i/60, path.name + ": simulation clock")
            timings.append(float(row["tickMs"]))
            allocations.append(int(row["allocatedBytes"]))
            require(allocations[-1] >= 0, path.name + ": negative allocation")
            gc_samples.append([int(row[f"gen{g}"]) for g in range(3)])
            require(all(v >= 0 for v in gc_samples[-1]), path.name + ": negative GC count")
            if len(gc_samples) > 1:
                require(all(a >= b for a, b in zip(gc_samples[-1], gc_samples[-2])), path.name + ": GC count decreased")
            if has_hud:
                hud_timings.append(float(row["hudMs"]))
                for name in STAGE_NAMES:
                    stages[name].append(float(row[name]))
                for key in COUNTS:
                    value = int(row[key.lower()])
                    require(value >= 0, path.name + ": negative entity count")
                    counts[key].append(value)
                if i % HUD_WINDOW == 0:
                    window = run["HudWindows"][i//HUD_WINDOW-1]
                    validate_stats(window["Timing"], hud_timings[-HUD_WINDOW:], path.name + f" HUD window {i//HUD_WINDOW}")
                    for stage in window["Stages"]:
                        validate_stats(stage["Timing"], stages[stage["Name"]][-HUD_WINDOW:],
                                       path.name + " HUD " + stage["Name"])
                    for key in COUNTS:
                        close(window["Mean" + key], sum(counts[key][-HUD_WINDOW:])/HUD_WINDOW,
                              path.name + ": HUD Mean" + key)
            if i % 3600 == 0:
                window = run["Windows"][i//3600-1]
                for key in ("ships", "projectiles", "debris", "particles", "turrets"):
                    require(int(row[key]) == window[key.title()], path.name + ": entity census mismatch")
                for key, counter in (("adds", "StructuralAdds"), ("removes", "StructuralRemoves")):
                    require(int(row[key]) == window["Counters"][counter], path.name + ": counter mismatch")
                validate_stats(window["Timing"], timings[-3600:], path.name + f" window {i//3600}")
                require(sum(allocations[-3600:]) == window["AllocatedBytes"], path.name + ": window allocations mismatch")
    require(len(timings) == 108000, path.name + ": incomplete CSV")
    validate_stats(run["Timing"], timings, path.name)
    if has_hud:
        validate_stats(run["HudTiming"], hud_timings, path.name + ".HudTiming")
        for stage in run["Stages"]:
            validate_stats(stage["Timing"], stages[stage["Name"]], path.name + "." + stage["Name"])
    require(sum(allocations) == run["AllocatedBytes"], path.name + ": total allocations mismatch")
    if "OpeningPhases" in run:
        validate_stats(run["InitialWindow"]["Timing"], timings[:2000], path.name + ".InitialWindow")
        for stage in run["InitialWindow"]["Stages"]:
            validate_stats(stage["Timing"], stages[stage["Name"]][:2000], path.name + ".InitialWindow." + stage["Name"])
        for phase_name, count in (("First600", 600), ("First2000", 2000)):
            phase = run["OpeningPhases"][phase_name]
            validate_stats(phase["Timing"], timings[:count], path.name + ".OpeningPhases." + phase_name)
            validate_stats(phase["HudTiming"], hud_timings[:count], path.name + ".OpeningPhases." + phase_name + ".HudTiming")
            for stage in phase["Stages"]:
                validate_stats(stage["Timing"], stages[stage["Name"]][:count],
                               path.name + ".OpeningPhases." + phase_name + "." + stage["Name"])
            for key in COUNTS:
                close(phase["Mean" + key], sum(counts[key][:count])/count,
                      path.name + ".OpeningPhases." + phase_name + ".Mean" + key)
        remainder = run["OpeningPhases"]["RemainderAfter2000"]
        validate_stats(remainder["Timing"], timings[2000:], path.name + ".OpeningPhases.RemainderAfter2000")
        for stage in remainder["Stages"]:
            validate_stats(stage["Timing"], stages[stage["Name"]][2000:],
                           path.name + ".OpeningPhases.RemainderAfter2000." + stage["Name"])
    for i, window in enumerate(run["Windows"]):
        end = (i+1)*3600 - 1
        # CSV is sampled immediately after Step. Window statistics allocate
        # between that sample and Window.Gc; they can legitimately trigger GC.
        upper = gc_samples[end+1] if end+1 < len(gc_samples) else [
            a+b for a, b in zip(gc_samples[0], run["GcCollections"])]
        validate_gc_interval(gc_samples[end], window["Gc"], upper, path.name)


def validate_gc_interval(post_step, checkpoint, next_sample, label):
    require(len(post_step) == len(checkpoint) == len(next_sample) == 3 and
            all(a <= b <= c for a, b, c in zip(post_step, checkpoint, next_sample)),
            label + ": GC checkpoint outside sample interval")


def charts(runs, directory):
    if set(runs) == {(CURRENT_PROFILE, framework) for framework in FRAMEWORKS}:
        return charts_current(runs, directory)
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.patches import Patch
    plt.rcParams.update({"font.size": 10})
    policy = TimingPolicy.from_file(Path(__file__).with_name("heatmap_policy.json"))
    policy_keys = {"MeanMs": "tick.mean_ms", "P99Ms": "tick.p99_ms"}
    for metric in ("MeanMs", "P99Ms"):
        fig, axes = plt.subplots(1, 3, figsize=(13, 4), sharey=True)
        for axis, profile in zip(axes, PROFILES):
            values = [runs[profile, f]["Timing"][metric] for f in FRAMEWORKS]
            bars = axis.bar(range(5), values, color=colors(values, absolute_threshold=policy.threshold(profile, policy_keys[metric])))
            axis.bar_label(bars, labels=[f"{v:.4f}" for v in values], padding=3, fontsize=8)
            axis.margins(y=.15)
            axis.set_xticks(range(5), ["SECS", "Lite", "Classic", "Morpeh", "Static"], rotation=25)
            axis.set_title(profile)
            axis.set_ylabel("Step time (ms)")
            axis.grid(axis="y", alpha=.2)
        preset = runs["nominal", "secs"]["Metadata"].get("Preset", "benchmark")
        fig.suptitle(f"SpaceBattle ({preset}): {metric} — one run per case, 108,000 ticks")
        palette = dict(PALETTE)
        fig.legend(handles=[Patch(facecolor=palette["best"], label="Lag ≤5%"),
                            Patch(facecolor=palette["middle"], label="Lag 15%"),
                            Patch(facecolor=palette["worst"], label="Lag ≥30%"),
                            Patch(facecolor=EQUAL_COLOR, label="Neutral / close group")],
                   loc="lower center", ncol=4, frameon=False, fontsize=9)
        fig.tight_layout(rect=(0, .10, 1, .93))
        fig.savefig(directory / f"{metric}.png", dpi=150)
        plt.close(fig)


def charts_current(runs, directory):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.patches import Patch
    plt.rcParams.update({"font.size": 10})
    policy = TimingPolicy.from_file(Path(__file__).with_name("heatmap_policy.json"))
    profile = CURRENT_PROFILE
    for metric in ("MeanMs", "P99Ms"):
        fig, axis = plt.subplots(figsize=(9, 4))
        values = [runs[profile, f]["Timing"][metric] for f in FRAMEWORKS]
        bars = axis.bar(range(5), values, color=colors(values, absolute_threshold=policy.threshold(profile, "tick.mean_ms" if metric == "MeanMs" else "tick.p99_ms")))
        axis.bar_label(bars, labels=[f"{v:.4f}" for v in values], padding=3, fontsize=8)
        axis.margins(y=.16)
        axis.set_xticks(range(5), ["SECS", "Lite", "Classic", "Morpeh", "Static"], rotation=25)
        axis.set_ylabel("Tick time (ms)")
        axis.grid(axis="y", alpha=.2)
        fig.suptitle(f"SpaceBattle game preset, ChurnMultiplier=2: {metric} — 108,000 measured ticks")
        palette = dict(PALETTE)
        fig.legend(handles=[Patch(facecolor=palette["best"], label="Lag ≤5%"),
                            Patch(facecolor=palette["middle"], label="Lag 15%"),
                            Patch(facecolor=palette["worst"], label="Lag ≥30%"),
                            Patch(facecolor=EQUAL_COLOR, label="Neutral / close group")],
                   loc="lower center", ncol=4, frameon=False, fontsize=9)
        fig.tight_layout(rect=(0, .10, 1, .93))
        fig.savefig(directory / f"{metric}.png", dpi=150)
        plt.close(fig)

    fig, axis = plt.subplots(figsize=(10, 5))
    x = [0, 1, 2, 3]
    for framework in FRAMEWORKS:
        run = runs[profile, framework]
        means = [run["OpeningPhases"]["First600"]["Timing"]["MeanMs"],
                 run["OpeningPhases"]["First2000"]["Timing"]["MeanMs"],
                 run["OpeningPhases"]["RemainderAfter2000"]["Timing"]["MeanMs"], run["Timing"]["MeanMs"]]
        axis.plot(x, means, marker="o", label=FRAMEWORKS[framework])
    axis.set_xticks(x, ["1–600", "1–2,000", "2,001–108,000", "1–108,000"])
    axis.set_ylabel("Outer mean tick time (ms)")
    axis.grid(True, alpha=.25)
    axis.legend(loc="best", fontsize=8)
    fig.suptitle("High: cumulative opening windows, remainder, and full run")
    fig.tight_layout()
    fig.savefig(directory / "InitialWindows.png", dpi=150)
    plt.close(fig)


def stage_table(runs, profile, policy, language="en", asset_path="../benchmarks/heatmap"):
    rows = []
    for i, name in enumerate(STAGE_NAMES):
        values = [runs[profile, f]["Stages"][i]["Timing"]["MeanMs"] for f in FRAMEWORKS]
        rows.append([name, *[Metric(f"{v:.4f}", comparison=v) for v in values]])
    values = [runs[profile, f]["Timing"]["MeanMs"] for f in FRAMEWORKS]
    rows.append(["Total Step", *[Metric(f"{v:.4f}", comparison=v) for v in values]])
    return shade_rows(rows, range(1, 6),
        absolute_thresholds=lambda row: policy.threshold(profile,
            "tick.mean_ms" if row[0] == "Total Step" else f"stage.{row[0]}.mean_ms"),
        language=language, asset_path=asset_path)


def hud_tables(runs, policy, ru, asset_path):
    language = "ru" if ru else "en"
    lines = ["", "## " + ("Окна HUD: по 600 тиков" if ru else "HUD windows: 600 ticks each"), ""]
    if not runs["nominal", "secs"].get("HudWindows"):
        return lines + [("В старых входных данных нет HUD-окон; короткие фазы по ним не восстановить." if ru else
                        "Legacy inputs contain no HUD windows; short phases cannot be reconstructed from them."), ""]
    lines += [("Первое, второе и худшее окно показывают по одной и той же фазе всех пяти ECS. Худшее — окно с максимальным средним HUD-временем среди всех ECS профиля; его границы общие, а не отдельные максимумы портов. Численность — среднее после каждого тика окна." if ru else
               "First, second and worst windows compare the same phase across all five ECS. Worst is the window with the largest mean HUD time across any ECS in that profile; its tick range is shared rather than selecting each port's own maximum. Counts are means sampled after each tick."), ""]
    lines += [("Среднее внутреннего HUD-таймера за все 108 000 тиков:" if ru else
               "Internal HUD timer mean over all 108,000 ticks:"), "",
              "| Profile | HUD mean ms: SECS | Lite | Classic | Morpeh | StaticEcs |",
              "| --- | ---: | ---: | ---: | ---: | ---: |"]
    rows = [[profile, *[Metric(f"{runs[profile, f]['HudTiming']['MeanMs']:.4f}",
                              comparison=runs[profile, f]["HudTiming"]["MeanMs"]) for f in FRAMEWORKS]] for profile in PROFILES]
    rows = shade_rows(rows, range(1, 6), language=language, asset_path=asset_path,
                      absolute_thresholds=lambda row: policy.threshold(row[0], "tick.mean_ms"))
    lines += ["| " + " | ".join(map(str, row)) + " |" for row in rows]
    lines.append("")
    for profile in PROFILES:
        lines += [f"### {profile}", "", "| Phase / ticks | HUD / stage mean ms | SECS | Lite | Classic | Morpeh | StaticEcs |",
                  "| --- | --- | ---: | ---: | ---: | ---: | ---: |"]
        for phase, i in hud_selection(runs, profile):
            windows = [runs[profile, f]["HudWindows"][i] for f in FRAMEWORKS]
            span = f"{phase} / {windows[0]['StartTick']}–{windows[0]['EndTick']}"
            rows = [[span, "HUD total", *[Metric(f"{w['Timing']['MeanMs']:.4f}", comparison=w["Timing"]["MeanMs"]) for w in windows]]]
            for j, name in enumerate(STAGE_NAMES):
                values = [w["Stages"][j]["Timing"]["MeanMs"] for w in windows]
                rows.append([span, name, *[Metric(f"{v:.4f}", comparison=v) for v in values]])
            rows = shade_rows(rows, range(2, 7), language=language, asset_path=asset_path,
                absolute_thresholds=lambda row: policy.threshold(profile,
                    "tick.mean_ms" if row[1] == "HUD total" else f"stage.{row[1]}.mean_ms"))
            lines += ["| " + " | ".join(map(str, row)) + " |" for row in rows]
        lines += ["", "| Phase / ticks | Mean ships | Mean projectiles | Mean debris | Mean particles | Mean turrets |",
                  "| --- | ---: | ---: | ---: | ---: | ---: |"]
        for phase, i in hud_selection(runs, profile):
            w = runs[profile, "secs"]["HudWindows"][i]
            lines.append("| " + " | ".join([f"{phase} / {w['StartTick']}–{w['EndTick']}",
                *[f"{w['Mean' + key]:.2f}" for key in COUNTS]]) + " |")
        lines += ["", ("Средняя численность совпадает во всех пяти ECS и показана один раз." if ru else
                       "Mean counts match across all five ECS and are shown once."), ""]
    return lines


def current_hud_tables(runs, ru, asset_path):
    """Retain the phase and population view when the campaign targets high only."""
    language = "ru" if ru else "en"
    lines = ["## " + ("Окна HUD: по 600 тиков" if ru else "HUD windows: 600 ticks each"), "",
             ("Первое, второе и худшее окно сравнивают одну фазу всех пяти ECS. Худшее окно выбрано по максимальному среднему HUD-времени среди всех портов; границы у портов общие." if ru else
              "First, second and worst windows compare the same phase across all five ECS. The worst window has the largest mean HUD time among all ports; its tick range is shared."), "",
             ("Среднее внутреннего HUD-таймера за все 108 000 тиков:" if ru else
              "Internal HUD timer mean over all 108,000 ticks:"), "",
             "| Profile | HUD mean ms: SECS | Lite | Classic | Morpeh | StaticEcs |",
             "| --- | ---: | ---: | ---: | ---: | ---: |"]
    for profile in (CURRENT_PROFILE,):
        row = [profile, *[Metric(f"{runs[profile, f]['HudTiming']['MeanMs']:.4f}",
                                comparison=runs[profile, f]["HudTiming"]["MeanMs"])
                          for f in FRAMEWORKS]]
        shaded = shade_rows([row], range(1, 6), language=language, asset_path=asset_path)[0]
        lines.append("| " + " | ".join(map(str, shaded)) + " |")
    lines.append("")
    for profile in (CURRENT_PROFILE,):
        lines += [f"### {profile}", "", "| Phase / ticks | HUD / stage mean ms | SECS | Lite | Classic | Morpeh | StaticEcs |",
                  "| --- | --- | ---: | ---: | ---: | ---: | ---: |"]
        for phase, i in hud_selection(runs, profile):
            windows = [runs[profile, f]["HudWindows"][i] for f in FRAMEWORKS]
            span = f"{phase} / {windows[0]['StartTick']}–{windows[0]['EndTick']}"
            rows = [[span, "HUD total", *[Metric(f"{w['Timing']['MeanMs']:.4f}", comparison=w["Timing"]["MeanMs"])
                                          for w in windows]]]
            for j, stage in enumerate(STAGE_NAMES):
                values = [w["Stages"][j]["Timing"]["MeanMs"] for w in windows]
                rows.append([span, stage, *[Metric(f"{value:.4f}", comparison=value) for value in values]])
            shaded = shade_rows(rows, range(2, 7), language=language, asset_path=asset_path)
            lines += ["| " + " | ".join(map(str, row)) + " |" for row in shaded]
        lines += ["", "| Phase / ticks | Mean ships | Mean projectiles | Mean debris | Mean particles | Mean turrets |",
                  "| --- | ---: | ---: | ---: | ---: | ---: |"]
        for phase, i in hud_selection(runs, profile):
            window = runs[profile, "secs"]["HudWindows"][i]
            lines.append("| " + " | ".join([f"{phase} / {window['StartTick']}–{window['EndTick']}",
                *[f"{window['Mean' + key]:.2f}" for key in COUNTS]]) + " |")
        lines += ["", ("Средняя численность совпадает во всех пяти ECS и показана один раз." if ru else
                       "Mean counts match across all five ECS and are shown once."), ""]
    return lines


def render(runs, manifest, hardware, directory, verify_ticks, report_base=None):
    if manifest.get("Format") == 3:
        return render_format3(runs, manifest, hardware, directory, verify_ticks)
    preset = manifest.get("Preset", "benchmark")
    report_base = report_base or ("GAME_REPORT" if preset == "game" else "REPORT")
    relative = directory.relative_to(DOCS).as_posix()
    rows = [[p, f, f"{r['Timing']['MeanMs']:.4f}", f"{r['Timing']['P95Ms']:.4f}", f"{r['Timing']['P99Ms']:.4f}",
             f"{r['Timing']['MaxMs']:.4f}", str(r['Timing']['BudgetMisses']), f"{r['AllocatedBytes']/108000:.1f}",
             f"{total_step_seconds(r):.6f}"]
            for (p, f), r in runs.items()]
    with (directory / "summary.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["profile", "framework", "meanMs", "p95Ms", "p99Ms", "maxMs", "budgetMisses", "stepManagedBytesPerTick", "totalStepSeconds"])
        writer.writerows(rows)
    first = runs["nominal", "secs"]
    m = first["Metadata"]
    source_pins = first["Provenance"]["Sources"]
    framework_pins = [("SECS", manifest["SourceRevision"]),
                      ("LeoECS Lite", source_pins["Lite"]),
                      ("LeoECS Classic", source_pins["Classic"]),
                      ("Morpeh", source_pins["Morpeh"]),
                      ("StaticEcs", source_pins["StaticEcs"])]
    require(all(len(pin) == 40 and all(char in "0123456789abcdefABCDEF" for char in pin)
                for _, pin in framework_pins), "Invalid framework source revision")
    cpus = hardware["Cpu"] if isinstance(hardware["Cpu"], list) else [hardware["Cpu"]]
    cpu = ", ".join(c["Name"].strip() for c in cpus)
    policy = TimingPolicy.from_file(Path(__file__).with_name("heatmap_policy.json"))
    metric_keys = {2: "tick.mean_ms", 3: "tick.p95_ms", 4: "tick.p99_ms", 5: "tick.max_ms"}
    for ru in (False, True):
        name = report_base + (".ru.md" if ru else ".md")
        suffix = ".ru" if ru else ""
        language = "ru" if ru else "en"
        asset_path = "../benchmarks/heatmap"
        pin_rows = ["| " + ("Фреймворк | Ревизия исходников |" if ru else "Framework | Source revision |"),
                    "| --- | --- |",
                    *(f"| {name} | `{pin}` |" for name, pin in framework_pins)]
        table_rows = []
        for row, ((profile, _), run) in zip(rows, runs.items()):
            table_rows.append([row[0], row[1], *[
                Metric(row[column], comparison=run["Timing"][key])
                for column, key in ((2, "MeanMs"), (3, "P95Ms"), (4, "P99Ms"), (5, "MaxMs"), (6, "BudgetMisses"))],
                Metric(row[7], comparison=run["AllocatedBytes"]/108000),
                Metric(row[8], comparison=total_step_seconds(run))])
        table_rows = shade_columns(table_rows, range(2, 9), group_by=(0,),
            absolute_thresholds=lambda row, column: policy.threshold(row[0], metric_keys[column]) if column in metric_keys else None,
            language=language, asset_path=asset_path)
        lines = [(f"[English]({report_base}.md) | Русский" if ru else f"English | [Русский]({report_base}.ru.md)"), "",
                 "# " + (("Бенчмарки Space Battle — игровой preset" if preset == "game" else "Бенчмарки Space Battle — конечный бой") if ru else
                         ("Space Battle benchmarks — game preset" if preset == "game" else "Space Battle benchmarks — finite battle")), "",
                 f"Preset: `{preset}`.", "",
                 ("Актуальная кампания: " if ru else "Current campaign: ") + f"{manifest['StartedAt']} — {manifest['FinishedAt']}.", "",
                 ("Текущая headless-симуляция измерена на пяти ECS: SECS, LeoEcsLite, LeoEcsClassic, Morpeh и StaticEcs. Ревизии игровых систем: " if ru else
                  "The current headless simulation was measured with five ECS implementations: SECS, LeoEcsLite, LeoEcsClassic, Morpeh, and StaticEcs. Gameplay system revisions: ") +
                 ", ".join(f"`{v}`" for v in RULES.values()) + ".", "",
                 "## " + ("Ревизии фреймворков в замерах" if ru else "Framework revisions used"), "",
                 ("Для сторонних ECS указаны точные коммиты исходных репозиториев, а не предполагаемые номера релизов. Для SECS указан базовый коммит репозитория; манифест фиксирует хеши именно измеренных исходников. Коммиты сторонних ECS записаны в JSON-результатах." if ru else
                  "Third-party rows show exact upstream commits, not guessed release numbers. The SECS row shows the base repository commit; the manifest records hashes of the measured sources. Third-party commits are recorded in the result JSON files."), "",
                 *pin_rows, "",
                 (f"StaticEcs: релиз [2.2.9](https://github.com/Felid-Force-Studios/StaticEcs/releases/tag/v2.2.9); зависимость StaticPack 1.2.6 собрана из коммита `{source_pins['StaticPack']}`." if ru else
                  f"StaticEcs: release [2.2.9](https://github.com/Felid-Force-Studios/StaticEcs/releases/tag/v2.2.9); its StaticPack 1.2.6 dependency was built from commit `{source_pins['StaticPack']}`."), "",
                 ("**Проверка пройдена:** 15 случаев, 1 620 000 измеренных тиков. В каждом профиле совпали все 30 контрольных точек, итоговые checksum, игровые и тактические счётчики и поминутная численность сущностей." if ru else
                  "**Validation passed:** 15 cases, 1,620,000 measured ticks. Within each profile all 30 checkpoints, final checksums, gameplay and tactical counters, and per-minute entity counts match."), "",
                 ("Тики CSV проверены локально: пересчитаны среднее, квантили, разброс, превышения бюджета и аллокации. Хеши исходных CSV сохранены в манифесте." if ru else
                  "Local tick CSV verification recomputed means, quantiles, spread, budget misses and allocations. Input CSV hashes are retained in the manifest.") if verify_ticks else
                 ("Проверена опубликованная JSON-матрица и её хеши; локальные CSV в этом запуске анализатора не проверялись." if ru else "Validated the published JSON matrix and hashes; local CSVs were not checked in this analyzer run."), "",
                 "## " + ("Как читать результаты" if ru else "Reading the results"), "",
                 ("Каждый случай — один независимый прогон из 108 000 тиков (30 минут симуляции) после отдельного прогрева на 600 тиков. Low/nominal/high меняют только churn: 0.5/1/2. Seed 7331, 96 эскортов и 3 носителя на команду, 60 тиков/с." if ru else
                  "Each case is one independent run of 108,000 ticks (30 simulated minutes), following a separate 600-tick warmup. Low/nominal/high change only churn: 0.5/1/2. Seed 7331, 96 escorts and 3 carriers per team, 60 ticks/s."), "",
                 (("Preset game: LightSwarm, Tactical, EndlessReinforcements=true, ReplenishCarriers=false — параметры симуляции desktop Release по умолчанию." if ru else
                   "Game preset: LightSwarm, Tactical, EndlessReinforcements=true, ReplenishCarriers=false — the default desktop Release simulation settings.") if preset == "game" else
                  ("Preset benchmark: Uniform, LegacyRandom, EndlessReinforcements=false, ReplenishCarriers=true — конечный бой. С игровым preset его числовые результаты не сравниваются." if ru else
                   "Benchmark preset: Uniform, LegacyRandom, EndlessReinforcements=false, ReplenishCarriers=true — a finite battle. Its values are not compared with the game preset.")), "",
                 ("Время Step включает игровые системы, физику и структурные commit. Рендеринг, начальное заполнение, snapshots и запись телеметрии не входят. Бюджет 16.67 ms относится к симуляции, а не к FPS. Показаны наблюдения этого компьютера; один прогон на случай не даёт устойчивого рейтинга и доверительного интервала. Аллокации — управляемые выделения потока внутри Step, без нативной памяти." if ru else
                  "Step time includes gameplay systems, physics and structural commits. Rendering, setup, snapshots and telemetry serialization are excluded. The 16.67 ms budget is a simulation budget, not FPS. These are observations on this machine; one run per case does not establish a stable ranking or confidence interval. Allocations count managed bytes on the Step thread and exclude native storage."), "",
                 ("Квантили headless выбирают sorted[ceil((N−1)×p)]: это верхний наблюдаемый элемент для позиции p. Live HUD использует nearest rank ceil(N×p)−1; Mean от этого различия не зависит." if ru else
                  "Headless quantiles select sorted[ceil((N−1)×p)], the upper observed sample at position p. The live HUD uses nearest rank ceil(N×p)−1; Mean is unaffected by this convention."), "",
                 ("Mean HUD усредняет последние до 600 тиков, тогда как общий headless Mean охватывает все 108 000 тиков после отдельного прогрева на 600 тиков. Конечный benchmark-бой может позднее обрабатывать мало живых сущностей и снижать среднее за 30 минут; игровой preset сохраняет подкрепления. Окна ниже используют внутренний Battle.LastTickMs, как HUD, и раскрывают краткие фазы и их численность. Сравнивайте результаты при одинаковых настройках боя, runtime, диапазоне тиков, прогреве и границах таймера: desktop-рендеринг и ожидание кадра в Battle.LastTickMs не входят, хотя могут влиять на нагрузку процесса." if ru else
                  "HUD Mean averages the latest up to 600 ticks, while the full headless Mean covers all 108,000 ticks after a separate 600-tick warmup. The finite benchmark battle can later process few live entities and lower its 30-minute mean; the game preset keeps reinforcements active. The windows below use internal Battle.LastTickMs, as the HUD does, and expose short phases and their counts. Compare results only with matching battle settings, runtime, tick range, warmup and timer boundaries: desktop rendering and frame waiting are outside Battle.LastTickMs, though they can affect process load."), "",
                 *compact_legend(language, asset_path, f"../benchmarks/COLORS{suffix}.md"),
                 ((f"Другой режим: [конечный бой](REPORT{suffix}.md)." if preset == "game" else
                   f"Другой режим: [настройки игры](GAME_REPORT{suffix}.md).") if ru else
                  ("Other workload: [finite battle](REPORT.md)." if preset == "game" else
                   "Other workload: [game defaults](GAME_REPORT.md).")), "",
                 (f"[Профиль Release-клиента и первые тики HUD](DESKTOP_TIMING{suffix}.md) отдельно проверяют холодный старт, tiering и paced replay." if ru else
                  "The [Release client and initial HUD ticks](DESKTOP_TIMING.md) separately check cold startup, tiering and paced replay."), "",
                 "## " + ("Время тика" if ru else "Tick time"), "",
                 "| Profile | ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total Step s |",
                 "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"]
        lines += ["| " + " | ".join(map(str, row)) + " |" for row in table_rows]
        lines += ["", f"![Mean Step time]({relative}/MeanMs.png)", "", f"![P99 Step time]({relative}/P99Ms.png)", "",
                  "## " + ("Состояние симуляции и стадии" if ru else "Simulation state and stages"), "",
                  "| Profile | Checksum | Ships | Turrets | Shots | Hits | Created | Destroyed |",
                  "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
        for p in PROFILES:
            r = runs[p, "secs"]
            w, c = r["Windows"][-1], r["Counters"]
            lines.append(f"| {p} | {r['Checksum']} | {w['Ships']} | {w['Turrets']} | {c['Shots']} | {c['Hits']} | {c['ShipsCreated']} | {c['ShipsDestroyed']} |")
        lines += ["", ("Среднее всех 108 000 тиков, ms. Каждая строка сравнивает одинаковую стадию и профиль пяти ECS. Total Step — отдельный внешний таймер всей симуляции, включая commit и накладные расходы между стадиями; это не сумма строк. Total Step s = Mean ms × 108 000 / 1000; это измеренное время Step, а не WallSeconds процесса." if ru else
                        "Means cover all 108,000 ticks, in ms. Each row compares the same stage and profile across five ECS. Total Step is the separate external timer of the whole simulation, including commits and work between stages; it is not the sum of rows. Total Step s = Mean ms × 108,000 / 1000; this is measured Step time, not process WallSeconds."), ""]
        for profile in PROFILES:
            lines += [f"### {profile}", "", "| Stage | SECS | Lite | Classic | Morpeh | StaticEcs |",
                      "| --- | ---: | ---: | ---: | ---: | ---: |"]
            lines += ["| " + " | ".join(map(str, row)) + " |" for row in stage_table(runs, profile, policy, language, asset_path)]
            lines.append("")
        lines += hud_tables(runs, policy, ru, asset_path)
        lines += ["", "## " + ("Происхождение данных" if ru else "Data provenance"), "",
                  f"- CPU: {cpu}; RAM: {hardware['TotalPhysicalMemoryBytes']/1024**3:.2f} GiB.",
                  f"- Runtime: {m['Runtime']}; OS: {m['Os']}; tiered compilation: `0`.",
                  f"- Base commit: `{manifest['SourceRevision']}`; measured source SHA256: [{relative}/manifest.json]({relative}/manifest.json).",
                  f"- [JSON]({relative}/), [summary.csv]({relative}/summary.csv), [hardware.json]({relative}/hardware.json).", "",
                  ("Манифест фиксирует исходники и хеши входных файлов; каждый JSON — точные upstream pins и SHA256 всех измеряемых DLL. Поле Provenance.Sources.SECS в старом headless host содержит описательную историческую строку; точная ревизия и исходники этой кампании задаются манифестом." if ru else
                   "The manifest records source and input hashes; each JSON records exact upstream pins and SHA256 hashes of all measured DLLs. Provenance.Sources.SECS in the headless host is a historical descriptive string; the campaign manifest supplies its exact revision and source hashes."), "",
                  "## " + ("Воспроизведение" if ru else "Reproduction"), "",
                  ("Из корня проекта, с .NET 8, SDK с поддержкой C# 13 (9.0 или новее) и доступным кешем NuGet. Для графиков нужны Python 3.12 и matplotlib. Запускайте измерения последовательно, без другой нагрузочной работы. Команда перезаписывает указанный OutputDirectory; для собственных экспериментов задайте другую директорию внутри проекта." if ru else
                   "From the project root, with .NET 8, an SDK supporting C# 13 (9.0 or newer), and available NuGet packages. Charts require Python 3.12 and matplotlib. Run measurements sequentially without other heavy work. This command replaces the specified OutputDirectory; use another directory inside the project for experiments."), "",
                  "```powershell", '$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"', '$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"',
                  "$env:MSBuildEnableWorkloadResolver = 'false'",
                  "dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1 -p:GeneratePackageOnBuild=false",
                  "dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate",
                  "dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-adapters",
                  "dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-ports",
                  f"./tools/space-battle/run-suite.ps1 -SkipBuild -Preset {preset} -OutputDirectory docs/space-battle/{relative}",
                  f"python tools/space-battle/analyze_current.py --data-dir docs/space-battle/{relative} --preset {preset} --report-base {report_base} --verify-ticks", "```", "",
                  ("Без --verify-ticks анализатор проверяет опубликованные JSON и хеши и строит тот же отчёт без требования локальных CSV. Длинные CSV остаются локально; опубликованных JSON достаточно для сводных таблиц и графиков." if ru else
                   "Without --verify-ticks the analyzer verifies published JSON and hashes and builds the report without requiring local CSVs. Long tick CSVs stay local; published JSON is sufficient for summary tables and charts."), "",
                  "## " + ("История и связанные документы" if ru else "History and related documents"), "",
                  (f"- [Архив измерений 2026-10-11](REPORT_2026-10-11{suffix}.md): predictive-neutral-v1, Aether, старый frontend replay и состав сущностей. Его цифры не сравниваются с этой кампанией: изменились правила игры, физика и нагрузка." if ru else
                   "- [Archived 2026-10-11 campaign](REPORT_2026-10-11.md): predictive-neutral-v1, Aether, old frontend replay and entity composition. Its values are not compared with this campaign because gameplay rules, physics and workload changed."),
                  (f"- [Архитектура](ARCHITECTURE{suffix}.md), [физика](PHYSICS{suffix}.md), [оружие](ARMAMENT{suffix}.md), [тактика](TACTICS_V3{suffix}.md)." if ru else
                   "- [Architecture](ARCHITECTURE.md), [physics](PHYSICS.md), [armament](ARMAMENT.md), [tactics](TACTICS_V3.md)."),
                  (f"- [Микробенчмарки SECS](../../SECS.Benchmarks/README{suffix}.md): отдельная нагрузка без игровой логики." if ru else
                   "- [SECS microbenchmarks](../../SECS.Benchmarks/README.md): separate workloads without gameplay logic."), ""]
        (DOCS / name).write_text("\n".join(lines), encoding="utf-8")


def render_format3(runs, manifest, hardware, directory, verify_ticks):
    relative = directory.relative_to(DOCS).as_posix()
    frameworks = list(FRAMEWORKS)
    profiles = [CURRENT_PROFILE]
    cpu_data = hardware["Cpu"] if isinstance(hardware["Cpu"], list) else [hardware["Cpu"]]
    cpu = ", ".join(item["Name"].strip() for item in cpu_data)
    with (directory / "summary.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["profile", "framework", "opening600MeanMs",
                         "opening2000MeanMs", "remainderMeanMs", "meanMs", "p95Ms", "p99Ms", "maxMs",
                         "budgetMisses", "managedBytesPerTick", "totalTickSeconds", "checksum"])
        for profile in profiles:
            for framework in frameworks:
                run = runs[profile, framework]
                writer.writerow([profile, framework,
                                 run["OpeningPhases"]["First600"]["Timing"]["MeanMs"],
                                 run["OpeningPhases"]["First2000"]["Timing"]["MeanMs"],
                                 run["OpeningPhases"]["RemainderAfter2000"]["Timing"]["MeanMs"],
                                 run["Timing"]["MeanMs"], run["Timing"]["P95Ms"], run["Timing"]["P99Ms"],
                                 run["Timing"]["MaxMs"], run["Timing"]["BudgetMisses"],
                                 run["AllocatedBytes"] / TICKS, run["Timing"]["MeanMs"] * TICKS / 1000,
                                 run["Checksum"]])
    early_more_expensive = sum(
        runs["high", f]["OpeningPhases"]["First2000"]["Timing"]["MeanMs"] >
        runs["high", f]["OpeningPhases"]["RemainderAfter2000"]["Timing"]["MeanMs"]
        for f in frameworks)
    pins = runs["high", "secs"]["Provenance"]["Sources"]
    framework_pins = [("SECS", manifest["SourceRevision"]),
                      ("LeoECS Lite", pins["Lite"]),
                      ("LeoECS Classic", pins["Classic"]),
                      ("Morpeh", pins["Morpeh"]),
                      ("StaticEcs", pins["StaticEcs"])]
    require(all(len(pin) == 40 and all(char in "0123456789abcdefABCDEF" for char in pin)
                for _, pin in framework_pins), "Invalid framework source revision")
    for ru in (False, True):
        suffix = ".ru" if ru else ""
        name = "REPORT" + suffix + ".md"
        language = "ru" if ru else "en"
        asset_path = "../benchmarks/heatmap"
        lines = [f"English | [Русский](REPORT.ru.md)" if not ru else "[English](REPORT.md) | Русский", "",
                 "# " + ("Space Battle benchmarks — real-game high" if not ru else
                        "Бенчмарки Space Battle — игровой high"), "",
                 ("Preset: `game`." if not ru else "Preset: `game`."), "",
                 (f"Current campaign: {manifest['StartedAt']} — {manifest['FinishedAt']}. Profile: `high`." if not ru else
                  f"Актуальная кампания: {manifest['StartedAt']} — {manifest['FinishedAt']}. Профиль: `high`."), "",
                 ("The current headless simulation was measured with five ECS implementations: SECS, LeoECS Lite, LeoECS Classic, Morpeh and StaticEcs. Gameplay system revisions: " if not ru else
                  "Текущая headless-симуляция измерена на пяти ECS: SECS, LeoECS Lite, LeoECS Classic, Morpeh и StaticEcs. Ревизии игровых систем: ") +
                 ", ".join(f"`{revision}`" for revision in RULES.values()) + ".", "",
                 "## " + ("Ревизии фреймворков в замерах" if ru else "Framework revisions used"), "",
                 ("Для сторонних ECS указаны точные коммиты исходников и соответствующие им релизы. Для SECS указаны базовый коммит и версия проекта; хеши фактически измеренных исходников и DLL фиксируют манифест и JSON." if ru else
                  "Third-party ECS rows show exact upstream commits and their matching releases. The SECS row shows the base commit and project version; the manifest and JSON record hashes of the measured sources and DLLs."), "",
                 ("| Фреймворк | Ревизия исходников | Версия |" if ru else "| Framework | Source revision | Version |"),
                 "| --- | --- | --- |",
                 *(f"| {framework} | `{pin}` | {version_cell(framework, pin)} |" for framework, pin in framework_pins), "",
                 (f"StaticEcs использует исходники StaticPack из коммита `{pins['StaticPack']}`." if ru else
                  f"StaticEcs uses StaticPack source `{pins['StaticPack']}`."), "",
                 "## " + ("Как читать результаты" if ru else "Reading the results"), "",
                 ("The single `high` profile uses the real game preset with its normal `ChurnMultiplier=2`, seed 7331, 96 escorts and 3 carriers per team. The same simulation runs on all five ECS implementations. The archived low/nominal/high campaign used a different preset and gameplay workload; its timings are not directly comparable." if not ru else
                  "Единственный профиль `high` использует игровой preset со штатным `ChurnMultiplier=2`, seed 7331, 96 эскортами и 3 носителями на команду. Один и тот же сценарий выполняется на пяти ECS. Архивная кампания low/nominal/high использовала другой preset и игровую нагрузку; её время напрямую не сравнивается."), "",
                 ("Game preset: LightSwarm fleet, Tactical deployment, EndlessReinforcements=true and ReplenishCarriers=false. Runs use Release, tiered compilation disabled and no realtime pacing." if not ru else
                  "Игровой preset: флот LightSwarm, расстановка Tactical, EndlessReinforcements=true и ReplenishCarriers=false. Прогоны выполняются в Release, без tiered compilation и realtime-пейсинга."), "",
                 ("The [archived finite campaign](REPORT_2026-10-14_FINITE.md) preserves the previous method and results." if not ru else
                  "[Архивная кампания конечного боя](REPORT_2026-10-14_FINITE.ru.md) сохраняет прежнюю методику и результаты."), "",
                 ("The full-run mean covers 108,000 measured ticks after a separate 600-tick warmup battle. Desktop HUD Mean covers the latest up to 600 ticks and uses Battle.LastTickMs. The headless outer tick timer and HUD/stage timers have different boundaries; compare desktop and headless only at matching game settings and tick ranges." if not ru else
                  "Среднее полного прогона охватывает 108 000 измеряемых тиков после отдельного прогрева боя на 600 тиков. Desktop HUD Mean охватывает последние до 600 тиков и использует Battle.LastTickMs. Границы внешнего headless-таймера и таймеров HUD/стадий различаются; сравнивайте desktop и headless при одинаковых игровых настройках и диапазонах тиков."), "",
                 ("Each case is one independent run of 108,000 ticks (30 simulated minutes), at seed 7331 and 60 ticks/s. The 16.67 ms budget is a simulation budget, not FPS. Headless quantiles use sorted[ceil((N−1)×p)]; the live HUD uses nearest rank ceil(N×p)−1. One run per case does not establish a stable ranking or confidence interval." if not ru else
                  "Каждый случай — один независимый прогон из 108 000 тиков (30 минут симуляции), seed 7331, 60 тиков/с. Бюджет 16.67 ms относится к симуляции, а не к FPS. Квантили headless используют sorted[ceil((N−1)×p)]; live HUD — nearest rank ceil(N×p)−1. Один прогон на случай не даёт устойчивого рейтинга или доверительного интервала."), "",
                 *compact_legend(language, asset_path, f"../benchmarks/COLORS{suffix}.md"),
                 (f"[Профиль Release-клиента и первые тики HUD](DESKTOP_TIMING{suffix}.md) отдельно проверяют холодный старт, tiering и paced replay." if ru else
                  "The [Release client and initial HUD ticks](DESKTOP_TIMING.md) separately check cold startup, tiering and paced replay."), "",
                 ("## Validation" if not ru else "## Проверка"), "",
                 ("Validation passed: five ECS cases, 540,000 measured ticks; hashes and provenance match; gameplay checksums, counters, tactics and entity traces match across ports. Store census contains gameplay entities only." if not ru else
                  "Проверка пройдена: пять ECS-кейсов, 540 000 измеренных тиков; хеши и происхождение данных совпадают; игровые checksum, счётчики, тактика и трассы сущностей совпадают между портами. Census хранилища содержит только игровые сущности."), "",
                 (("Локальные tick CSV проверены: время, стадии, HUD, аллокации и игровые счётчики." if ru else
                   "Local tick CSV files were checked for timing, stages, HUD, allocations and gameplay counters.") if verify_ticks else
                  ("Проверены опубликованные JSON и хеши входных файлов. Локальные tick CSV в этом запуске не проверялись." if ru else
                   "Published JSON and input hashes were checked. Local tick CSV files were not verified in this run.")), "",
                 "## " + ("Время тика" if ru else "Tick time"), "",
                 ("Каждая цветная ячейка сравнивает пять ECS по одной метрике профиля high. Внешний тик включает игровые системы, физику и ECS commits; Managed B/тик не включает нативное хранилище ECS." if ru else
                  "Each coloured cell compares five ECS for one metric in the high profile. The outer tick includes gameplay systems, physics and ECS commits; managed B/tick excludes native ECS storage."), "",
                 ("| Профиль | ECS | Среднее ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/тик | Всего внешних тиков s |" if ru else
                  "| Profile | ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer tick s |"),
                 "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"]
        timing_rows = []
        for profile in profiles:
            for framework in frameworks:
                run = runs[profile, framework]
                full = run["Timing"]
                label = profile
                timing_rows.append([profile, label, FRAMEWORKS[framework],
                    *[Metric(f"{full[key]:.4f}", comparison=full[key])
                      for key in ("MeanMs", "P95Ms", "P99Ms", "MaxMs")],
                    Metric(str(full["BudgetMisses"]), comparison=full["BudgetMisses"]),
                    Metric(f"{run['AllocatedBytes']/TICKS:.1f}", comparison=run["AllocatedBytes"]/TICKS),
                    Metric(f"{full['MeanMs']*TICKS/1000:.6f}", comparison=full["MeanMs"]*TICKS/1000)])
        timing_rows = shade_columns(timing_rows, range(3, 10), group_by=(0,), language=language, asset_path=asset_path)
        lines += ["| " + " | ".join(map(str, row[1:])) + " |" for row in timing_rows]
        lines += ["", f"![{'Среднее время тика' if ru else 'Mean tick time'}]({relative}/MeanMs.png)", "",
                  f"![{'P99 времени тика' if ru else 'P99 tick time'}]({relative}/P99Ms.png)", "",
                  "## " + ("Окна начала прогона" if ru else "Opening windows"), "",
                  ("Окна 1–600 и 1–2 000 являются накопительными; остаток 2 001–108 000 их не перекрывает." if ru else
                   "Windows 1–600 and 1–2,000 are cumulative; the remainder 2,001–108,000 is disjoint."), "",
                  ("| Профиль | ECS | Накопительное среднее внешнее 1–600, ms | Накопительное среднее внешнее 1–2000, ms | Среднее внешнее 2001–108000, ms | Среднее внешнее 1–108000, ms |" if ru else
                   "| Profile | ECS | Cumulative outer mean 1–600 ms | Cumulative outer mean 1–2,000 ms | Remainder outer mean 2,001–108,000 ms | Full outer mean 1–108,000 ms |"),
                  "| --- | --- | ---: | ---: | ---: | ---: |"]
        for profile in profiles:
            for framework in frameworks:
                run = runs[profile, framework]
                early = run["OpeningPhases"]
                values = [early["First600"]["Timing"]["MeanMs"], early["First2000"]["Timing"]["MeanMs"],
                          early["RemainderAfter2000"]["Timing"]["MeanMs"], run["Timing"]["MeanMs"]]
                lines.append(f"| {profile} | {FRAMEWORKS[framework]} | " +
                             " | ".join(f"{value:.4f}" for value in values) + " |")
        lines += ["", (f"The first 2,000 ticks have a higher mean than the remainder for {early_more_expensive} of five ECS implementations." if not ru else
                       f"Первые 2 000 тиков дороже остатка у {early_more_expensive} из пяти ECS."), "",
                  f"[{'Сводная CSV-таблица' if ru else 'Summary CSV'}]({relative}/summary.csv)", "",
                  f"![{'Окна 1–600, 1–2 000, остаток и полный прогон' if ru else 'Windows 1–600, 1–2,000, remainder and full run'}]({relative}/InitialWindows.png)", "",
                  "## " + ("Состояние симуляции и стадии" if ru else "Simulation state and stages"), "",
                  ("Состояние игры одинаково во всех пяти портах. Каждая стадия сравнивается между пятью ECS только в профиле high." if ru else
                   "Gameplay state matches across all five ports. Each stage is compared across five ECS in the high profile."), "",
                  "### " + ("Совпадение игрового состояния" if ru else "Gameplay parity"), "",
                  ("| Профиль | Checksum | Кораблей | Турелей | Выстрелов | Попаданий | Создано | Уничтожено |" if ru else
                   "| Profile | Checksum | Ships | Turrets | Shots | Hits | Created | Destroyed |"),
                  "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
        for profile in profiles:
            run = runs[profile, "secs"]
            window, counters = run["Windows"][-1], run["Counters"]
            lines.append(f"| {profile} | {run['Checksum']} | {window['Ships']} | {window['Turrets']} | {counters['Shots']} | {counters['Hits']} | {counters['ShipsCreated']} | {counters['ShipsDestroyed']} |")
        lines += ["", "### " + ("Средние стадий за полный прогон" if ru else "Full-run stage means"), ""]
        for profile in profiles:
            lines += [f"#### {profile}", "",
                      ("| Стадия | SECS, ms | Lite, ms | Classic, ms | Morpeh, ms | StaticEcs, ms |" if ru else
                       "| Stage | SECS ms | Lite ms | Classic ms | Morpeh ms | StaticEcs ms |"),
                      "| --- | ---: | ---: | ---: | ---: | ---: |"]
            stage_rows = []
            for stage_index, stage_name in enumerate(STAGE_NAMES):
                values = [runs[profile, f]["Stages"][stage_index]["Timing"]["MeanMs"] for f in frameworks]
                stage_rows.append([stage_name, *[Metric(f"{value:.4f}", comparison=value) for value in values]])
            values = [runs[profile, f]["HudTiming"]["MeanMs"] for f in frameworks]
            stage_rows.append(["Battle.LastTickMs", *[Metric(f"{value:.4f}", comparison=value) for value in values]])
            values = [runs[profile, f]["Timing"]["MeanMs"] for f in frameworks]
            stage_rows.append(["Outer tick", *[Metric(f"{value:.4f}", comparison=value) for value in values]])
            shaded = shade_rows(stage_rows, range(1, 6), language=language, asset_path=asset_path)
            lines += ["| " + " | ".join(map(str, row)) + " |" for row in shaded]
            lines.append("")
        lines += ["### " + ("Стадии SECS: начало, остаток и полный прогон" if ru else "SECS stages: opening, remainder and full run"), "",
                  ("Стадии измерены внутри Battle.Step; внешний таймер тика включает также накладные расходы между стадиями и commit ECS." if ru else
                   "Stages are measured inside Battle.Step; the outer tick timer also includes work between stages and ECS commits."), ""]
        for profile in profiles:
            lines += [f"#### {profile}", "",
                      ("| Стадия | 1–600 ms | 1–2000 ms | 2001–108000 ms | 1–108000 ms |" if ru else
                       "| Stage | 1–600 ms | 1–2,000 ms | 2,001–108,000 ms | 1–108,000 ms |"),
                      "| --- | ---: | ---: | ---: | ---: |"]
            run = runs[profile, "secs"]
            early = run["OpeningPhases"]
            for i, stage in enumerate(STAGE_NAMES):
                lines.append(f"| {stage} | {early['First600']['Stages'][i]['Timing']['MeanMs']:.4f} | {early['First2000']['Stages'][i]['Timing']['MeanMs']:.4f} | {early['RemainderAfter2000']['Stages'][i]['Timing']['MeanMs']:.4f} | {run['Stages'][i]['Timing']['MeanMs']:.4f} |")
            lines.append(f"| {'Внешний тик' if ru else 'Outer tick'} | {early['First600']['Timing']['MeanMs']:.4f} | {early['First2000']['Timing']['MeanMs']:.4f} | {early['RemainderAfter2000']['Timing']['MeanMs']:.4f} | {run['Timing']['MeanMs']:.4f} |")
            lines.append("")
        lines += current_hud_tables(runs, ru, asset_path)
        lines += ["## " + ("Происхождение данных" if ru else "Data provenance"), "",
                  f"- CPU: {cpu}; RAM: {hardware['TotalPhysicalMemoryBytes']/1024**3:.2f} GiB.",
                  f"- Runtime: {runs['high', 'secs']['Metadata']['Runtime']}; OS: {runs['high', 'secs']['Metadata']['Os']}; tiered compilation: `0`.",
                  (f"- Базовый commit: `{manifest['SourceRevision']}`; хеши измеренных файлов отражают фактическое рабочее дерево и записаны в [{relative}/manifest.json]({relative}/manifest.json)." if ru else
                   f"- Base commit: `{manifest['SourceRevision']}`; measured file hashes describe the actual working tree and are recorded in [{relative}/manifest.json]({relative}/manifest.json)."),
                  (f"- [Опубликованные JSON и графики]({relative}/); [сводная CSV-таблица]({relative}/summary.csv); [оборудование]({relative}/hardware.json). Локальные tick CSV остаются входом для проверки, их хеши записаны в манифесте." if ru else
                   f"- [Published JSON and charts]({relative}/); [summary CSV]({relative}/summary.csv); [hardware details]({relative}/hardware.json). Local tick CSV files remain verification inputs; their hashes are in the manifest."),
                  (f"- Исходники StaticEcs: `{pins['StaticEcs']}`; исходники StaticPack: `{pins['StaticPack']}`." if ru else
                   f"- StaticEcs source `{pins['StaticEcs']}`; StaticPack source `{pins['StaticPack']}`."), "",
                  ("One run per ECS describes this machine and does not establish a stable ranking or confidence interval. Outer tick time includes gameplay systems, physics and ECS commits; Battle.LastTickMs and stage timings cover their own narrower scopes. Rendering and snapshot/report work are excluded. Managed allocation excludes native ECS storage." if not ru else
                   "Один прогон каждой ECS описывает эту машину и не даёт устойчивого рейтинга или доверительного интервала. Внешнее время тика включает игровые системы, физику и ECS commit; Battle.LastTickMs и таймеры стадий имеют более узкие границы. Рендеринг и snapshots/отчёт не входят. Managed allocation не включает нативное хранилище ECS."), "",
                  "## " + ("Воспроизведение" if ru else "Reproduction"), "",
                  ("Из корня проекта с .NET 8, SDK с поддержкой C# 13, локальным кешем NuGet, Python 3.12 и matplotlib. Команда run-suite перезаписывает выбранную директорию результатов: для нового эксперимента укажите другую директорию внутри проекта. Анализатор проверяет данные и строит оба отчёта и графики." if ru else
                   "From the project root with .NET 8, a C# 13-capable SDK, the local NuGet cache, Python 3.12 and matplotlib. run-suite replaces the selected result directory: choose a different directory inside the project for a new experiment. The analyzer validates the data and builds both reports and charts."), "",
                  "```powershell",
                  "./tools/space-battle/run-suite.ps1 -Preset game -OutputDirectory docs/space-battle/current",
                  "python tools/space-battle/analyze_current.py --data-dir docs/space-battle/current --preset game --verify-ticks",
                  "```", "",
                  ("Если локальные tick CSV недоступны, уберите --verify-ticks: анализатор проверит опубликованные JSON и хеши, но не повторит проверку CSV." if ru else
                   "If local tick CSV files are unavailable, omit --verify-ticks: the analyzer validates published JSON and hashes but does not repeat CSV verification."), "",
                  "## " + ("История и связанные документы" if ru else "History and related documents"), "",
                  (f"- [Архив конечного боя 2026-10-14](REPORT_2026-10-14_FINITE{suffix}.md): прежняя матрица low/nominal/high, другая методика и игровой workload; время напрямую не сравнивается." if ru else
                   "- [Archived finite battle, 2026-10-14](REPORT_2026-10-14_FINITE.md): the previous low/nominal/high matrix uses a different method and gameplay workload; timings are not directly comparable."),
                  (f"- [Архив измерений 2026-10-11](REPORT_2026-10-11{suffix}.md), [архитектура](ARCHITECTURE{suffix}.md), [физика](PHYSICS{suffix}.md), [тактика](TACTICS_V3{suffix}.md)." if ru else
                   "- [Archived 2026-10-11 campaign](REPORT_2026-10-11.md), [architecture](ARCHITECTURE.md), [physics](PHYSICS.md), [tactics](TACTICS_V3.md)."),
                  (f"- [Микробенчмарки SECS](../../SECS.Benchmarks/README{suffix}.md): отдельная нагрузка без игровой логики." if ru else
                   "- [SECS microbenchmarks](../../SECS.Benchmarks/README.md): a separate workload without gameplay logic."), ""]
        (DOCS / name).write_text("\n".join(lines), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", "--directory", dest="directory", type=Path, default=DOCS / "current")
    parser.add_argument("--preset", choices=tuple(PRESETS), help="Expected input preset; legacy inputs mean benchmark")
    parser.add_argument("--report-base", help="Sibling report filename stem (default REPORT or GAME_REPORT)")
    parser.add_argument("--verify-ticks", action="store_true")
    parser.add_argument("--skip-charts", action="store_true",
                        help="Regenerate reports from saved results while preserving existing chart files")
    args = parser.parse_args()
    directory = args.directory.resolve()
    require(directory.is_relative_to(DOCS), "Published results must stay inside docs/space-battle")
    if args.report_base is not None:
        require(bool(args.report_base) and all(c in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-" for c in args.report_base),
                "Report base must be a simple filename stem")
    runs, manifest, hardware = validate(directory, args.verify_ticks, args.preset)
    if not args.skip_charts:
        charts(runs, directory)
    render(runs, manifest, hardware, directory, args.verify_ticks, args.report_base)
    outputs = "bilingual reports and summary" if args.skip_charts else "bilingual reports, charts and summary"
    print(f"PASS: {len(runs)} {manifest.get('Preset', 'benchmark')} cases; {len(runs)*TICKS:,} ticks; canonical port parity; wrote {outputs}")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, OSError, TypeError) as error:
        print("FAIL: " + str(error), file=sys.stderr)
        sys.exit(1)
