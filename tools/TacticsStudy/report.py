"""Derive descriptive KM/timing/gameplay evidence from existing tactics raw JSON.

No simulation, builds, plots, confidence intervals, or population replication.
"""

import argparse
from collections import defaultdict
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import statistics

from summarise import ROOT, fresh_path, summary, write_json


def kaplan_meier(observations, tick_rate=60):
    """Smallest elapsed tick with S(t) <= 1/2; events precede same-time censoring."""
    if type(tick_rate) is not int or tick_rate <= 0:
        raise ValueError("Positive integer tick rate required")
    times = defaultdict(lambda: [0, 0])
    subjects = events = 0
    for elapsed, event in observations:
        if type(elapsed) is not int or elapsed < 0 or type(event) is not bool:
            raise ValueError("Require nonnegative integer elapsed ticks and boolean event indicators")
        times[elapsed][0 if event else 1] += 1
        subjects += 1
        events += event
    risk = subjects
    survival = Fraction(1)
    median = None
    curve = []
    for elapsed, (deaths, censored) in sorted(times.items()):
        # Everyone censored at this tick remains at risk for this tick's death events.
        survival *= Fraction(risk - deaths, risk)
        if median is None and survival <= Fraction(1, 2):
            median = elapsed
        curve.append({"elapsed_ticks": elapsed, "at_risk": risk, "events": deaths,
                      "censored": censored, "survival": float(survival)})
        risk -= deaths + censored
    return {"subjects": subjects, "events": events, "right_censored": subjects - events,
            "median_ticks": median, "median_seconds": None if median is None else median / tick_rate,
            "median_status": "no_eligible_subjects" if not subjects else "not_reached" if median is None else "reached",
            "curve": curve}


def cohort(units, team=None):
    selected = []
    for unit in units:
        if not unit["TrackedInitial"] or not unit["TtkEligible"] or team is not None and unit["Team"] != team:
            continue
        elapsed = unit["ElapsedExposureTicks"]
        death = unit["DeathTick"]
        if type(elapsed) is not int or elapsed < 0 or death is not None and (type(death) is not int or death < 0):
            raise ValueError("Eligible initial unit has invalid/null elapsed ticks or death tick")
        event = death is not None
        if "RightCensored" in unit and unit["RightCensored"] != (not event):
            raise ValueError("Eligible unit's censor flag contradicts DeathTick")
        selected.append((elapsed, event))
    return selected


def km_cohorts(units, tick_rate):
    return {"both_teams": kaplan_meier(cohort(units), tick_rate),
            "by_team": {str(team): kaplan_meier(cohort(units, team), tick_rate) for team in (0, 1)}}


def gameplay_fingerprint(report):
    run = report["Run"]
    units = run["Units"]
    serials = [unit["Serial"] for unit in units]
    if len(set(serials)) != len(serials):
        raise ValueError("Duplicate unit serial within one gameplay replay")
    payload = {key: run[key] for key in ("Checkpoints", "Counters", "AiCounters", "Populations", "ObservationDigest")}
    payload["Units"] = sorted(units, key=lambda unit: unit["Serial"])
    payload["Teams"] = sorted(run["Teams"], key=lambda team: team["Team"])
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()
    return hashlib.sha256(encoded).hexdigest()


def timing(reports, name):
    means = [report["Run"][name]["MeanMs"] for report in reports]
    return {"median_process_mean_ms": statistics.median(means),
            "process_mean_range_ms": [min(means), max(means)],
            "median_process_p95_ms": statistics.median(report["Run"][name]["P95Ms"] for report in reports),
            "median_process_p99_ms": statistics.median(report["Run"][name]["P99Ms"] for report in reports)}


def gameplay(report):
    run = report["Run"]
    units = run["Units"]
    teams = sorted(run["Teams"], key=lambda team: team["Team"])
    group_changes = {str(team): sum(unit["GroupMembershipChanges"] for unit in units if unit["Team"] == team)
                     for team in (0, 1)}
    if any(group_changes[str(team["Team"])] != team["GroupMembershipChanges"] for team in teams):
        raise ValueError("Unit membership transitions disagree with observer team totals")
    if any(unit["Team"] not in (0, 1) for unit in units):
        raise ValueError("Unexpected team")
    return {"deaths_total": sum(team["DeathsCumulative"] for team in teams),
            "deaths_by_team": {str(team["Team"]): team["DeathsCumulative"] for team in teams},
            "initial_deaths_by_team": {str(team["Team"]): team["InitialDeaths"] for team in teams},
            "newborn_deaths_by_team": {str(team["Team"]): team["NewbornDeaths"] for team in teams},
            "alive_at_end_by_team": {str(team["Team"]): team["AliveAtEnd"] for team in teams},
            "friendly_fire_hits": run["Counters"]["FriendlyFireHits"],
            "core_target_switches": run["AiCounters"]["TargetSwitches"],
            "observer_group_membership_changes": sum(group_changes.values()),
            "observer_group_membership_changes_by_team": group_changes,
            "initial_unexposed": sum(unit["TrackedInitial"] and not unit["TtkEligible"] for unit in units),
            "initial_unexposed_deaths": sum(unit["TrackedInitial"] and not unit["TtkEligible"] and
                                            unit["DeathTick"] is not None for unit in units),
            "km_ttk": km_cohorts(units, report["Config"]["TickRate"])}


def build_report(reports):
    # Reuse the frozen comparator's config, binary/source, process independence and conservation guards.
    validated = summary(reports)
    groups = defaultdict(list)
    for report in reports:
        groups[(report["Port"], report["Seed"], report["Variant"])].append(report)
    representatives = {}
    cases = []
    for port, seed in sorted({(port, seed) for port, seed, _ in groups}):
        row = {"port": port, "seed": seed}
        for variant in ("before", "after"):
            values = sorted(groups[(port, seed, variant)], key=lambda report: report["Repetition"])
            fingerprints = [gameplay_fingerprint(report) for report in values]
            if len(set(fingerprints)) != 1:
                raise ValueError(f"Gameplay differs across timing repetitions: {port}/{seed}/{variant}")
            representative = values[0]
            representatives[(port, seed, variant)] = representative
            row[variant] = {"timing_processes": len(values), "repetitions": [r["Repetition"] for r in values],
                            "gameplay_representative_repetition": representative["Repetition"],
                            "gameplay_sha256": fingerprints[0], "shared_config_sha256": representative["SharedComparisonConfigSha256"],
                            "tactics_revision": representative["TacticsRevision"],
                            "step": timing(values, "Step"), "ai": timing(values, "Ai"), "gameplay": gameplay(representative)}
        before, after = row["before"], row["after"]
        row["delta_after_minus_before"] = {
            "step_median_process_mean_ms": after["step"]["median_process_mean_ms"] - before["step"]["median_process_mean_ms"],
            "ai_median_process_mean_ms": after["ai"]["median_process_mean_ms"] - before["ai"]["median_process_mean_ms"],
            **{key: after["gameplay"][key] - before["gameplay"][key] for key in
               ("deaths_total", "friendly_fire_hits", "core_target_switches", "observer_group_membership_changes")}}
        cases.append(row)
    pooled = []
    for port in sorted({port for port, _, _ in groups}):
        for variant in ("before", "after"):
            values = [report for (p, _, v), report in sorted(representatives.items()) if p == port and v == variant]
            rates = {report["Config"]["TickRate"] for report in values}
            if len(rates) != 1:
                raise ValueError("Cannot pool elapsed tick cohorts with different tick rates")
            units = [unit for report in values for unit in report["Run"]["Units"]]
            pooled.append({"port": port, "variant": variant, "seeds_counted_once": [report["Seed"] for report in values],
                           "km_ttk": km_cohorts(units, rates.pop())})
    return {"schema": "tactics-derived-summary-v1", "input_processes": len(reports),
            "nominal_series_complete": validated["nominal_three_seed_three_rep_series_complete"],
            "gameplay_identical_within_each_variant_seed": True, "cases": cases, "pooled_unique_seed_cohorts": pooled,
            "definitions": {
                "timing": "Median/range of independent process mean Step/AI; median of process p95/p99, not pooled tick percentiles.",
                "gameplay": "Exactly one representative per port/variant/seed after exact gameplay fingerprint comparison; repetitions add timing evidence only.",
                "km": "TrackedInitial AND TtkEligible; duration ElapsedExposureTicks; event DeathTick != null. S(t) product-limit, same-tick deaths before censor removal, median first S(t)<=0.5. Null median means not reached or no eligible subjects, never infinity or a survivor-only mean.",
                "groups": "Sum Run.Units.GroupMembershipChanges over all observed units including newborns, checked against Run.Teams; NOT optional AiCounters.GroupChanges.",
                "losses": "Cumulative observed deaths by both teams, including newborns, not differences inferred from final survivor populations."},
            "limitations": [
                "Exposure-to-observed-death/censor duration in a shared battle, not controlled duel TTK. Within-tick changes and lethal-first exposures can be missed; unexposed initial deaths are excluded explicitly.",
                "Cohorts and exposure times can differ by tactics variant. Right-censoring may be informative; ships interact and are not independent statistical trials. KM is descriptive; no confidence intervals or significance claims.",
                "Three timing repetitions replay identical gameplay per seed; only three seed scenarios contribute gameplay evidence, not nine independent populations.",
                "Finite LightSwarm battles have different endgame populations and lifetimes; whole-population Step means are outcome-dependent, not pure kernel throughput or balance proof."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-directory", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = fresh_path(args.output)
    if output.suffix != ".json":
        raise ValueError("Output must be a fresh JSON file")
    directory = Path(args.input_directory).resolve()
    if not directory.is_dir() or not directory.is_relative_to(ROOT):
        raise ValueError("Input directory must exist inside this project")
    paths = sorted(directory.glob("*seed*-rep*.json"))
    if not paths:
        raise ValueError("No raw replay files")
    inputs = [(path, path.read_bytes()) for path in paths]
    reports = [json.loads(content) for _, content in inputs]
    result = build_report(reports)
    result["evidence_inputs"] = [{"path": str(path.relative_to(ROOT)), "sha256": hashlib.sha256(content).hexdigest()} for path, content in inputs]
    result["report_source_sha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    write_json(output, result)
    print(output)


if __name__ == "__main__":
    main()
