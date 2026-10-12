"""Rotate independent BEFORE/AFTER processes, or summarize already recorded raw JSON.

Never builds binaries. All generated files must be fresh and under Benchmarks.Local/tmp.
"""

import argparse
import datetime as dt
import json
from pathlib import Path
import statistics
import subprocess


ROOT = Path(__file__).resolve().parents[2]
NOMINAL_SEEDS = {27, 7331, 61012}


def fresh_path(path):
    path = Path(path)
    if not path.is_absolute():
        raise ValueError("Output must be absolute")
    allowed = (ROOT / "Benchmarks.Local" / "tmp").resolve()
    resolved = path.resolve()
    if resolved == allowed or not resolved.is_relative_to(allowed) or path.exists():
        raise ValueError("Use a fresh output under ignored Benchmarks.Local/tmp")
    return resolved


def write_json(path, value):
    path = fresh_path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write("\n")


def schedule(seeds, reps):
    if reps < 3 or not seeds or len(set(seeds)) != len(seeds):
        raise ValueError("Use distinct seeds and at least three independent repetitions")
    rows = []
    for rep in range(1, reps + 1):
        for index, seed in enumerate(seeds):
            variants = ("before", "after") if (rep + index) % 2 else ("after", "before")
            rows.extend((variant, seed, rep) for variant in variants)
    return rows


def rotate(args):
    jobs = schedule(args.seeds, args.reps)
    binaries = {"before": Path(args.before).resolve(), "after": Path(args.after).resolve()}
    if any(not binary.is_file() or not binary.is_relative_to(ROOT) for binary in binaries.values()):
        raise ValueError("Both harness DLLs must already exist inside this project")
    destination = fresh_path(args.output_directory)
    destination.mkdir(parents=True, exist_ok=False)
    manifest = {"schema": "tactics-rotation-v1", "scope": "Independent processes; no builds. Process wall time includes warmup and untimed verification, not Step timing.", "runs": []}
    try:
        for variant, seed, rep in jobs:
            stem = f"{variant}-{args.port}-seed{seed}-rep{rep}"
            output = destination / f"{stem}.json"
            command = [args.dotnet, str(binaries[variant]), "--variant", variant, "--port", args.port,
                       "--seed", str(seed), "--rep", str(rep), "--ships", str(args.ships),
                       "--ticks", str(args.ticks), "--warmup", str(args.warmup),
                       "--checkpoint", str(args.checkpoint), "--output", str(output)]
            started = dt.datetime.now(dt.timezone.utc)
            with (destination / f"{stem}.stdout.txt").open("x", encoding="utf-8") as stdout, \
                    (destination / f"{stem}.stderr.txt").open("x", encoding="utf-8") as stderr:
                process = subprocess.run(command, cwd=ROOT, stdout=stdout, stderr=stderr, check=False)
            manifest["runs"].append({"variant": variant, "seed": seed, "rep": rep, "command": command,
                                     "started_utc": started.isoformat(), "output": str(output), "returncode": process.returncode})
            if process.returncode:
                raise RuntimeError(f"{stem} failed; preserved logs/raw results in {destination}")
    finally:
        write_json(destination / "batch.json", manifest)
    print(destination)


def summary(reports):
    if not reports:
        raise ValueError("No raw reports")
    indexed = {}
    identities = {}
    processes = set()
    scenarios = {}
    for report in reports:
        if report.get("Schema") != "tactics-study-v1" or not report["Run"]["ReplayVerified"]:
            raise ValueError("Require replay-verified tactics-study-v1 inputs")
        key = (report["Port"], report["Seed"], report["Repetition"], report["Variant"])
        if key in indexed or key[-1] not in {"before", "after"}:
            raise ValueError("Duplicate run identity or unknown variant")
        indexed[key] = report
        process = (report["ProcessId"], report["StartedUtc"])
        if process in processes:
            raise ValueError("Reused process report cannot count as an independent repetition")
        processes.add(process)
        scenario = (report["SharedComparisonConfigSha256"], report["Ticks"], report["WarmupTicks"], report["CheckpointTicks"])
        case = (report["Port"], report["Seed"])
        if case in scenarios and scenarios[case] != scenario:
            raise ValueError("Config/cadence changed across repetitions of the same scenario")
        scenarios[case] = scenario
        environment = (report["Variant"], report["Port"])
        identity = (report["Runtime"], report["Architecture"], report["BuildConfiguration"],
                    tuple((file["Path"], file["Sha256"]) for file in report["SimulationSource"]),
                    tuple((file["Path"], file["Sha256"]) for file in report["HarnessSource"]),
                    tuple((binary["Name"], binary["Sha256"]) for binary in report["Binaries"]))
        if environment in identities and identities[environment] != identity:
            raise ValueError("Source/binary/runtime identity changed within one comparison environment")
        identities[environment] = identity
        teams = report["Run"]["Teams"]
        if len(teams) != 2 or {team["Team"] for team in teams} != {0, 1}:
            raise ValueError("Require both team outcomes")
        if sum(team["DeathsCumulative"] for team in teams) != report["Run"]["Counters"]["ShipsDestroyed"]:
            raise ValueError("Observed deaths must match cumulative Core destruction, not final survivor counts")
        if sum(team["BirthsCumulative"] for team in teams) != report["Run"]["Counters"]["ShipsCreated"]:
            raise ValueError("Observed births must match Core creation, including newborns")
        if any(team["BirthsCumulative"] - team["DeathsCumulative"] != team["AliveAtEnd"] for team in teams):
            raise ValueError("Birth/death conservation failed")

    paired = []
    groups = {}
    for port, seed, rep, variant in indexed:
        if variant != "before":
            continue
        before = indexed[(port, seed, rep, "before")]
        after = indexed.get((port, seed, rep, "after"))
        if after is None:
            raise ValueError("Missing paired AFTER process")
        shared = ("SharedComparisonConfigSha256", "DamageRevision", "Ticks", "WarmupTicks", "CheckpointTicks", "Runtime", "Os", "Architecture", "LogicalProcessors", "BuildConfiguration")
        if any(before[field] != after[field] for field in shared) or before["HarnessSource"] != after["HarnessSource"]:
            raise ValueError("Pair config/cadence/runtime/harness source mismatch")
        pair = {"port": port, "seed": seed, "rep": rep,
                "step_mean_delta_ms": after["Run"]["Step"]["MeanMs"] - before["Run"]["Step"]["MeanMs"],
                "ai_mean_delta_ms": after["Run"]["Ai"]["MeanMs"] - before["Run"]["Ai"]["MeanMs"],
                "before_team_deaths": [t["DeathsCumulative"] for t in sorted(before["Run"]["Teams"], key=lambda t: t["Team"])],
                "after_team_deaths": [t["DeathsCumulative"] for t in sorted(after["Run"]["Teams"], key=lambda t: t["Team"])]}
        paired.append(pair)
        for mode, report in (("before", before), ("after", after)):
            groups.setdefault((port, seed, mode), []).append(report)
    if len(paired) * 2 != len(indexed):
        raise ValueError("Unpaired inputs")

    rows = []
    for (port, seed, variant), values in sorted(groups.items()):
        eligible = [[unit for unit in r["Run"]["Units"] if unit["TrackedInitial"] and unit["TtkEligible"]] for r in values]
        rows.append({"port": port, "seed": seed, "variant": variant, "independent_processes": len(values),
                     "step_mean_ms_by_process": [r["Run"]["Step"]["MeanMs"] for r in values],
                     "ai_mean_ms_by_process": [r["Run"]["Ai"]["MeanMs"] for r in values],
                     "median_process_step_mean_ms": statistics.median(r["Run"]["Step"]["MeanMs"] for r in values),
                     "median_process_ai_mean_ms": statistics.median(r["Run"]["Ai"]["MeanMs"] for r in values),
                     "team_outcomes_by_process": [r["Run"]["Teams"] for r in values],
                     "friendly_fire_by_process": [r["Run"]["Counters"]["FriendlyFireHits"] for r in values],
                     "ai_counters_by_process": [r["Run"]["AiCounters"] for r in values],
                     "ttk_exposed_initial_by_process": [len(units) for units in eligible],
                     "ttk_death_events_by_process": [sum(unit["DeathTick"] is not None for unit in units) for units in eligible],
                     "ttk_right_censored_by_process": [sum(unit["RightCensored"] for unit in units) for units in eligible],
                     "initial_unexposed_deaths_by_process": [sum(unit["TrackedInitial"] and unit["Unexposed"] and unit["DeathTick"] is not None for unit in r["Run"]["Units"]) for r in values]})
    ports = {port for port, _, _ in groups}
    complete = all(len(groups.get((port, seed, mode), [])) >= 3
                   for port in ports for seed in NOMINAL_SEEDS for mode in ("before", "after"))
    return {"schema": "tactics-summary-v1", "scope": "Process-level descriptive comparison, no significance or balance claims. Shared config excludes only root Tactics; full config and intentional AI settings/revision remain in raw reports. Tick samples are not independent replicates; no survivor-only TTK averages. Old-vs-new checksums intentionally need not match. Finite-horizon full Step includes outcome-dependent populations, not pure kernel throughput.",
            "nominal_three_seed_three_rep_series_complete": complete, "groups": rows, "paired_deltas_after_minus_before": paired}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    run = commands.add_parser("rotate")
    run.add_argument("--before", required=True)
    run.add_argument("--after", required=True)
    run.add_argument("--dotnet", default="dotnet")
    run.add_argument("--output-directory", required=True)
    run.add_argument("--port", default="secs", choices=["secs", "lite", "classic", "morpeh", "static"])
    run.add_argument("--seeds", type=int, nargs="+", default=[27, 7331, 61012])
    run.add_argument("--reps", type=int, default=3)
    run.add_argument("--ships", type=int, default=96)
    run.add_argument("--ticks", type=int, default=2400)
    run.add_argument("--warmup", type=int, default=2400)
    run.add_argument("--checkpoint", type=int, default=240)
    summarize = commands.add_parser("summary")
    summarize.add_argument("inputs", nargs="+")
    summarize.add_argument("--output", required=True)
    args = parser.parse_args()
    if args.command == "rotate":
        rotate(args)
    else:
        paths = []
        for value in args.inputs:
            path = Path(value)
            paths.extend(sorted(path.glob("*seed*-rep*.json")) if path.is_dir() else [path])
        reports = [json.loads(path.read_text(encoding="utf-8")) for path in paths]
        write_json(args.output, summary(reports))
        print(args.output)


if __name__ == "__main__":
    main()
