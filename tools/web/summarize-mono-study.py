"""Read independent browser replays; never pool ticks into independent repeats."""
import argparse
import json
from pathlib import Path
from statistics import median


def range_median(values):
    return {"median": median(values), "min": min(values), "max": max(values), "runs": len(values)}


def summarize(directory):
    runs = [json.loads(file.read_text(encoding="utf-8")) for file in sorted(directory.glob("[0-9]*-r*.json"))]
    equivalent = json.loads((directory / "equivalence.json").read_text(encoding="utf-8"))
    groups = {}
    for run in runs:
        trial = run["trial"]
        key = (trial["mode"], trial["profile"], trial["label"])
        groups.setdefault(key, []).append(run)
    result = []
    for (mode, profile, label), group in groups.items():
        metrics = {}
        for name in group[0]["samples"][0]["timing"]:
            values = [sum(sample["timing"][name] for sample in run["samples"]) / len(run["samples"]) for run in group]
            metrics[name] = range_median(values)
        cold = {name: range_median([run["cold"][name] for run in group]) for name in
                ("runtimeReadyFromNavigationMs", "appReadyFromNavigationMs", "launchWallMs")}
        result.append({"mode": mode, "profile": profile, "label": label, "metrics": metrics, "cold": cold,
                       "maximumLinearMemoryBytes": max(sample["memory"]["linearMemoryBytes"] for run in group for sample in run["samples"]),
                       "endChecksums": [run["checkpoints"][-1]["checksum"] for run in group]})
    return {"equivalence": equivalent, "aggregateWindows": result,
            "phaseDetails": json.loads((directory / "independent-run-medians.json").read_text(encoding="utf-8")),
            "note": "Median and range of independent run means for each fixed mode/profile/window; per-phase p95/p99 are independent-run medians, not pooled percentiles."}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    args.output.resolve().relative_to(root / "Benchmarks.Local/tmp")
    if args.output.exists():
        raise ValueError("Choose fresh output")
    result = summarize(args.directory)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    for group in result["aggregateWindows"]:
        print(group["mode"], group["profile"], group["label"], json.dumps(group["metrics"]))


if __name__ == "__main__":
    main()
