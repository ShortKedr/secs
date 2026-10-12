"""Aggregate independent replays; refuse performance comparison if gameplay differs."""
import argparse
import json
import math
from pathlib import Path
from statistics import median


def stats(values):
    return {"median": median(values), "min": min(values), "max": max(values), "runs": len(values)}


def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]


def summarize(directory):
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8-sig"))
    records = []
    for trial in manifest["plan"]:
        study = json.loads(Path(trial["output"]).read_text(encoding="utf-8-sig"))
        assert len(study["Runs"]) == 1, "One independent measured replay per trial required"
        records.append((trial, study, study["Runs"][0]))
    expected = {}
    fields = ("Checksum", "Counters", "Tactics", "MeanCounts", "FinalCounts")
    configs = {}
    for trial, study, run in records:
        profile = trial["profile"]
        configs.setdefault(profile, study["Config"])
        assert configs[profile] == study["Config"], "Gameplay configurations differ"
        assert study["Ticks"] == manifest["ticks"] and study["PhaseTicks"] == manifest["phaseTicks"]
        for phase in run["Phases"]:
            key = (profile, run["Port"], phase["EndTick"])
            state = {field: phase[field] for field in fields}
            expected.setdefault(key, state)
            assert expected[key] == state, f"Gameplay divergence: {trial} {key}"
    groups = {}
    for trial, study, run in records:
        groups.setdefault((trial["profile"], trial["label"]), []).append((study, run))
    result = []
    for (profile, label), runs in groups.items():
        means, p95, p99, allocations, heaps, exports = [], [], [], [], [], []
        stages = {}
        for study, run in runs:
            phases = run["Phases"]
            samples = [v for phase in phases for v in phase["StepSamplesMs"]]
            assert len(samples) == manifest["ticks"]
            means.append(sum(samples) / len(samples))
            p95.append(percentile(samples, .95)); p99.append(percentile(samples, .99))
            allocations.append(sum(p["AllocatedBytes"] for p in phases) / len(samples))
            heaps.append(max(p["ManagedHeapBytes"] for p in phases))
            exports.append(sum(p["Export"]["MeanMs"] for p in phases) / len(phases))
            for i, stage in enumerate(phases[0]["Stages"]):
                weighted = sum(p["Stages"][i]["Timing"]["MeanMs"] * len(p["StepSamplesMs"]) for p in phases)
                stages.setdefault(stage["Name"], []).append(weighted / len(samples))
        assert len(runs) >= 3
        result.append({"profile": profile, "label": label, "stepMeanMs": stats(means),
                       "p95Ms": stats(p95), "p99Ms": stats(p99), "bytesPerTick": stats(allocations),
                       "maxManagedHeapBytes": stats(heaps), "exportMeanMs": stats(exports),
                       "stages": {key: stats(values) for key, values in stages.items()}})
    return {"equivalent": True, "checkpointGroups": len(expected), "rows": result,
            "scope": "Median/range of independent run means and per-run fixed-window percentiles; not pooled ticks. FullOpts native with separate full-length warmup, no renderer. Export measured separately. Heap is sampled live telemetry, not private/peak process memory."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    args.directory.resolve().relative_to(root / "Benchmarks.Local/tmp")
    args.output.resolve().relative_to(root / "Benchmarks.Local/tmp")
    if args.output.exists():
        raise ValueError("Use a fresh output")
    result = summarize(args.directory)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("PASS: exact configurations, checkpoints, counters, tactics and populations")
    for row in result["rows"]:
        print(row["profile"], row["label"], json.dumps({k: row[k] for k in ("stepMeanMs", "p95Ms", "p99Ms", "bytesPerTick")}))
