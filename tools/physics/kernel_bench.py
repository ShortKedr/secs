"""Exact paired native circle-kernel replay. No builds, Aether runs, or remote writes.

Run only after main approves a timing window:
  python -B tools/physics/kernel_bench.py run --baseline OLD_DLL --candidate NEW_DLL
      --output Benchmarks.Local/tmp/NEW_PAIR --approved-timing-window
Add --instrumented for a separate diagnostic pair (never mixed with throughput).
Existing independent run directories can be compared without running physics:
  python -B tools/physics/kernel_bench.py compare --baseline OLD_RUN_DIRS...
      --candidate NEW_RUN_DIRS... [--output Benchmarks.Local/tmp/NEW_SUMMARY.json]
"""
import argparse
import hashlib
import json
import math
import statistics
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def artifact_path(path):
    path = Path(path).resolve()
    tmp = (ROOT / "Benchmarks.Local" / "tmp").resolve()
    if not tmp.is_relative_to(ROOT) or path == tmp or not path.is_relative_to(tmp):
        raise ValueError("Artifacts must stay strictly inside project Benchmarks.Local/tmp")
    return path


def write_new(path, value):
    path = artifact_path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)


def quantile(values, fraction):
    ordered = sorted(values)
    if not ordered or not 0 < fraction <= 1:
        raise ValueError("Nonempty sample and fraction in (0,1] required")
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]


def timing(values):
    if not values or any(not math.isfinite(v) or v < 0 for v in values):
        raise ValueError("Invalid frame timings")
    return dict(meanMs=statistics.mean(values), p95Ms=quantile(values, .95),
                p99Ms=quantile(values, .99), maxMs=max(values))


def signature(frame):
    # Every exposed body/contact/impact field is hashed; work counters may legitimately change.
    return {key: frame[key] for key in ("frame", "bodies", "fixtures", "contacts", "hits", "digests")}


def load_runs(paths):
    runs = []
    for value in paths:
        path = artifact_path(value)
        if path.is_dir():
            path /= "run.json"
        run = json.loads(path.read_text(encoding="utf-8-sig"))
        input_hash = hashlib.sha256((path.parent / "inputs.json").read_bytes()).hexdigest().upper()
        if run["schema"] != 1 or input_hash != run["inputSha256"] or run["warmupPasses"] < 1:
            raise ValueError(f"Invalid/unwarmed/tampered run: {path}")
        run["_assemblies"] = json.loads((path.parent / "assemblies.json").read_text(encoding="utf-8-sig"))
        runs.append(run)
    if len(runs) < 3 or len({r["repeat"] for r in runs}) != len(runs):
        raise ValueError("Require >=3 independent runs with distinct repeat numbers per revision")
    first = runs[0]
    for run in runs:
        for key in ("runtimeIdentity", "inputSha256", "warmupPasses", "instrumented", "stopwatchFrequency", "_assemblies", "cellSize", "margin"):
            if run.get(key) != first.get(key):
                raise ValueError(f"Mixed run configuration/binaries: {key}")
        names = [sample["name"] for sample in run["samples"]]
        if len(names) < 4 or len(set(names)) != len(names):
            raise ValueError("Missing/duplicate scenarios")
        for sample in run["samples"]:
            if not sample["frames"] or [f["frame"] for f in sample["frames"]] != list(range(len(sample["frames"]))):
                raise ValueError("Missing/duplicate/nonsequential frames")
    return sorted(runs, key=lambda r: r["repeat"])


def summarize_sample(sample, repeat):
    frames = sample["frames"]
    return dict(repeat=repeat, frames=len(frames),
                setup=timing([f["setupMs"] for f in frames]),
                step=timing([f["stepMs"] for f in frames]),
                hits=timing([f["hitMs"] for f in frames]),
                critical=timing([f["stepMs"] + f["hitMs"] for f in frames]),
                allocatedBytes={phase: sum(f[f"{phase}AllocatedBytes"] for f in frames)
                                for phase in ("setup", "step", "hit")},
                maxFrameAllocatedBytes={phase: max(f[f"{phase}AllocatedBytes"] for f in frames)
                                        for phase in ("setup", "step", "hit")},
                observerAllocatedBytes=sample["observerAllocatedBytes"],
                memoryBefore=sample["memoryBefore"], memoryAfter=sample["memoryAfter"],
                gcCollectionsIncludingObservers=sample["gcCollections"],
                workCounters=[dict(frame=f["frame"], queries=f["queries"],
                                   stepDiagnostics=f["stepDiagnostics"], diagnostics=f["diagnostics"])
                              for f in frames])


def compare_runs(baseline, candidate):
    if [r["repeat"] for r in baseline] != [r["repeat"] for r in candidate]:
        raise ValueError("Unpaired repeat numbers")
    for key in ("schema", "runtimeIdentity", "inputSha256", "warmupPasses", "instrumented", "stopwatchFrequency"):
        if baseline[0][key] != candidate[0][key]:
            raise ValueError(f"Cross-revision config/runtime mismatch: {key}")
    reference = {s["name"]: s for s in baseline[0]["samples"]}
    mismatches, rows = [], []
    for name, expected in reference.items():
        samples = {"baseline": [], "candidate": []}
        for label, runs in (("baseline", baseline), ("candidate", candidate)):
            for run in runs:
                indexed = {s["name"]: s for s in run["samples"]}
                if set(indexed) != set(reference):
                    raise ValueError("Scenario set changed")
                sample = indexed[name]
                if sample["bodyCount"] != expected["bodyCount"] or len(sample["frames"]) != len(expected["frames"]):
                    raise ValueError("Scenario/frame dimensions changed")
                for frame, old in zip(sample["frames"], expected["frames"]):
                    if signature(frame) != signature(old):
                        mismatches.append(dict(revision=label, repeat=run["repeat"], scenario=name, frame=frame["frame"]))
                samples[label].append(summarize_sample(sample, run["repeat"]))
        medians = {label: {phase: statistics.median(s[phase]["meanMs"] for s in repeats)
                           for phase in ("setup", "step", "hits", "critical")}
                   for label, repeats in samples.items()}
        paired_ratios = {phase: [old[phase]["meanMs"] / new[phase]["meanMs"]
                                if new[phase]["meanMs"] else None
                                for old, new in zip(samples["baseline"], samples["candidate"])]
                         for phase in ("step", "hits", "critical")}
        rows.append(dict(scenario=name, bodyCount=expected["bodyCount"],
                         medianRepeatMeanMs=medians, pairedSpeedupRatios=paired_ratios,
                         medianPairedSpeedup={phase: statistics.median(v for v in values if v is not None)
                                             if any(v is not None for v in values) else None
                                             for phase, values in paired_ratios.items()}, repeats=samples))
    allocation_free = all(s["allocatedBytes"]["step"] == 0 and s["allocatedBytes"]["hit"] == 0
                          for row in rows for s in row["repeats"]["candidate"])
    return dict(schema=1, instrumented=baseline[0]["instrumented"], runtimeIdentity=baseline[0]["runtimeIdentity"],
                exactWithinRuntimeReplay=not mismatches, mismatches=mismatches,
                candidateAllocationFreeCriticalPath=allocation_free,
                percentiles="Nearest-rank p95/p99 per independent repeat; never pooled across repeats",
                memoryScope="Process memory includes inputs/harness; GC counts include observers. Only step/hit allocation deltas describe the critical path.",
                diagnosticsAvailable={label: runs[0]["diagnosticsAvailable"]
                                      for label, runs in (("baseline", baseline), ("candidate", candidate))},
                scenarios=rows)


def run_pair(args):
    if not args.approved_timing_window:
        raise ValueError("Main-agent timing-window approval is required")
    if args.repeats < 3 or args.warmup_passes < 1:
        raise ValueError("Require repeats >=3 and warmup-passes >=1")
    output = artifact_path(args.output)
    binaries = {"baseline": Path(args.baseline).resolve(strict=True),
                "candidate": Path(args.candidate).resolve(strict=True)}
    if any(not p.is_relative_to(ROOT) for p in binaries.values()):
        raise ValueError("Harness binaries must be inside the project")
    output.mkdir(parents=True, exist_ok=False)
    write_new(output / "pair.json", dict(schema=1, binaries={k: str(p) for k, p in binaries.items()},
              repeats=args.repeats, instrumented=args.instrumented, warmupPasses=args.warmup_passes))
    command = [args.dotnet, str(binaries["baseline"]), "--generate", "--output", str(output / "inputs"),
               "--frames", str(args.frames), "--sweeps", str(args.sweeps), "--seed", str(args.seed)]
    subprocess.run(command, cwd=ROOT, check=True)
    paths = {label: [] for label in binaries}
    for repeat in range(args.repeats):
        for label in (("baseline", "candidate") if repeat % 2 == 0 else ("candidate", "baseline")):
            directory = output / f"{label}-{repeat:02d}"
            command = [args.dotnet, str(binaries[label]), "--run", "--inputs", str(output / "inputs" / "inputs.json"),
                       "--output", str(directory), "--repeat", str(repeat), "--warmup-passes", str(args.warmup_passes),
                       "--approved-timing-window"]
            if args.instrumented:
                command.append("--instrumented")
            if label == "candidate":
                if args.cell_size is not None: command += ["--cell-size", str(args.cell_size)]
                if args.margin is not None: command += ["--margin", str(args.margin)]
            subprocess.run(command, cwd=ROOT, check=True)
            paths[label].append(directory)
    return compare_runs(load_runs(paths["baseline"]), load_runs(paths["candidate"])), output / "summary.json"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    modes = parser.add_subparsers(dest="mode", required=True)
    run = modes.add_parser("run")
    for name in ("baseline", "candidate", "output"):
        run.add_argument(f"--{name}", required=True)
    run.add_argument("--dotnet", default="dotnet")
    run.add_argument("--repeats", type=int, default=3)
    run.add_argument("--frames", type=int, default=120)
    run.add_argument("--sweeps", type=int, default=1024)
    run.add_argument("--seed", type=int, default=7331)
    run.add_argument("--warmup-passes", type=int, default=2)
    run.add_argument("--approved-timing-window", action="store_true")
    run.add_argument("--instrumented", action="store_true")
    run.add_argument("--cell-size", type=float)
    run.add_argument("--margin", type=float)
    compare = modes.add_parser("compare")
    compare.add_argument("--baseline", nargs="+", required=True)
    compare.add_argument("--candidate", nargs="+", required=True)
    compare.add_argument("--output")
    args = parser.parse_args()
    if args.mode == "run":
        report, destination = run_pair(args)
    else:
        report = compare_runs(load_runs(args.baseline), load_runs(args.candidate))
        destination = args.output
    if destination:
        write_new(destination, report)
        print(destination)
    print(json.dumps({k: v for k, v in report.items() if k != "scenarios"}, indent=2))
    return 0 if report["exactWithinRuntimeReplay"] and report["candidateAllocationFreeCriticalPath"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
