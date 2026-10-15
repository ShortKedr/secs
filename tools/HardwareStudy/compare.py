"""Compare independent run means and exact behavior, never individual ticks as repeats."""
import argparse
import json
import statistics
from pathlib import Path


def behavior_config(config):
    normalized = dict(config)
    physics = dict(normalized.get("Physics", {}))
    # Structural/observer knobs are checked through replay, not part of the numerical model.
    for key in ("CollectDiagnostics", "ContactCellSize", "CacheContactCandidates",
                "ContactCandidateMargin", "MaximumCandidatePairs"):
        physics.pop(key, None)
    normalized["Physics"] = physics
    return normalized


def compare_native(paths):
    studies = [(Path(path).stem, json.loads(Path(path).read_text(encoding="utf-8-sig"))) for path in paths]
    baseline = studies[0][1]
    expected = {}
    fields = ("Checksum", "Counters", "Tactics", "MeanCounts", "FinalCounts")
    for run in baseline["Runs"]:
        for phase in run["Phases"]:
            key = (run["Port"], phase["EndTick"])
            state = {field: phase[field] for field in fields}
            if key in expected:
                assert expected[key] == state, f"Baseline repeated-run mismatch: {key}"
            expected[key] = state
    rows = []
    for label, study in studies:
        assert behavior_config(study["Config"]) == behavior_config(baseline["Config"]), "Different physical/gameplay config"
        groups = {}
        for run in study["Runs"]:
            for phase in run["Phases"]:
                key = (run["Port"], phase["EndTick"])
                assert expected[key] == {field: phase[field] for field in fields}, f"Behavior changed: {label} {key}"
                groups.setdefault(key, []).append(phase)
        assert set(groups) == set(expected), "Missing comparison groups"
        for (port, tick), phases in groups.items():
            values = [p["Step"]["MeanMs"] for p in phases]
            stages = {item["Name"]: statistics.median(p["Stages"][i]["Timing"]["MeanMs"] for p in phases)
                      for i, item in enumerate(phases[0]["Stages"])}
            rows.append(dict(stage=label, port=port, endTick=tick, repeats=len(phases),
                             medianMs=statistics.median(values), minMs=min(values), maxMs=max(values),
                             stages=stages, allocatedBytes=statistics.median(p["AllocatedBytes"] for p in phases),
                             managedHeapBytes=statistics.median(p["ManagedHeapBytes"] for p in phases),
                             store=phases[0]["Store"],
                             exportMedianMs=statistics.median(p["Export"]["MeanMs"] for p in phases)))
    return rows


def compare_browser(path):
    study = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    expected, groups = {}, {}
    fields = ("checksum", "counters", "meanCounts", "finalCounts")
    if "tactics" in study["runs"][0]["phases"][0]:
        fields += ("tactics",)
    mode = study["runs"][0]["runtimeMode"]
    for run in study["runs"]:
        assert run["runtimeMode"] == mode, "Do not compare different runtimes as behavior-equivalent"
        for phase in run["phases"]:
            key = phase["endTick"]
            state = {field: phase[field] for field in fields}
            if key in expected:
                assert expected[key] == state, f'Browser behavior changed: {run["revision"]} tick{key}'
            expected[key] = state
            groups.setdefault((run["revision"], key), []).append(phase)
    rows = []
    endpoints = set(expected)
    for label in {label for label, _ in groups}:
        assert {tick for revision, tick in groups if revision == label} == endpoints, f"Missing browser checkpoints: {label}"
    for (label, tick), phases in groups.items():
        values = [p["coreStep"]["meanMs"] for p in phases]
        rows.append(dict(stage=label, port=mode, endTick=tick, repeats=len(phases),
                         medianMs=statistics.median(values), minMs=min(values), maxMs=max(values),
                         stages={item["name"]: statistics.median(p["stages"][i]["timing"]["meanMs"] for p in phases)
                                 for i, item in enumerate(phases[0]["stages"])},
                         exportMedianMs=statistics.median(p["exportOnlyAtPhaseEnd"]["meanMs"] for p in phases)))
    return rows


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("paths", nargs="+")
    parser.add_argument("--output")
    parser.add_argument("--browser", action="store_true")
    args = parser.parse_args()
    rows = compare_browser(args.paths[0]) if args.browser else compare_native(args.paths)
    if args.output:
        target = Path(args.output).resolve()
        assert target.is_relative_to(Path.cwd().resolve()) and not target.exists(), "Use a new project-local output"
        target.write_text(json.dumps(rows, indent=2), encoding="utf-8")
    print("PASS: exact checkpoints, gameplay/AI counters and all population aggregates across every run/port/stage")
    for row in rows:
        if row["port"] in ("SECS", "SKECS") or args.browser:
            print(f'{row["stage"]} tick{row["endTick"]}: {row["medianMs"]:.4f}ms [{row["minMs"]:.4f}, {row["maxMs"]:.4f}]')
