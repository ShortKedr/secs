import unittest

from report import build_report, cohort, kaplan_meier


def unit(serial, team, elapsed, death, initial=True, eligible=True, changes=1):
    return {"Serial": serial, "Team": team, "TrackedInitial": initial, "TtkEligible": eligible, "Unexposed": not eligible,
            "ElapsedExposureTicks": elapsed, "DeathTick": death, "RightCensored": eligible and death is None,
            "GroupMembershipChanges": changes}


def raw(variant, seed, rep):
    after = variant == "after"
    units = [unit(1, 0, 2, 2, changes=1), unit(2, 0, 8, None, changes=2),
             unit(3, 1, 4, 4, changes=3), unit(4, 1, 8, None, changes=4),
             unit(5, 0, None, 6, initial=False, eligible=False, changes=5)]
    if after:
        for row in units:
            row["GroupMembershipChanges"] *= 2
    teams = [{"Team": team, "InitialShips": 2, "NewbornsObserved": 1 if team == 0 else 0,
              "BirthsCumulative": 3 if team == 0 else 2, "DeathsCumulative": 2 if team == 0 else 1,
              "InitialDeaths": 1, "NewbornDeaths": 1 if team == 0 else 0, "AliveAtEnd": 1,
              "GroupMembershipChanges": sum(row["GroupMembershipChanges"] for row in units if row["Team"] == team)} for team in (0, 1)]
    counters = {"TargetSwitches": 80 if after else 100}
    if after:
        counters["GroupChanges"] = 990  # Must never replace observed membership transitions.
    return {"Schema": "tactics-study-v1", "Variant": variant, "Port": "secs", "Seed": seed, "Repetition": rep,
            "ProcessId": seed * 100 + rep * 10 + int(after), "StartedUtc": "2026-10-12T00:00:00Z",
            "SharedComparisonConfigSha256": f"shared-{seed}", "DamageRevision": "layered-weapon-damage-v1",
            "TacticsRevision": "v3" if after else "v2", "Ticks": 2400, "WarmupTicks": 2400, "CheckpointTicks": 240,
            "Runtime": "same-runtime", "Os": "same-os", "Architecture": "X64", "LogicalProcessors": 8,
            "BuildConfiguration": "Release", "Config": {"TickRate": 60},
            "SimulationSource": [{"Path": "Core.cs", "Sha256": variant}],
            "HarnessSource": [{"Path": "Program.cs", "Sha256": "same"}], "Binaries": [{"Name": "Core", "Sha256": variant}],
            "Run": {"ReplayVerified": True, "ObservationDigest": variant, "Checkpoints": [{"Tick": 0, "Checksum": variant}],
                    "Populations": [{"Tick": 1, "Alive": 5}], "Units": units, "Teams": teams, "AiCounters": counters,
                    "Counters": {"ShipsCreated": 5, "ShipsDestroyed": 3, "FriendlyFireHits": 3206 if after else 4076},
                    "Step": {"MeanMs": (0.30 if after else 0.28) + (rep - 1) * 0.01,
                             "P95Ms": 0.8 + rep * 0.1, "P99Ms": 1 + rep * 0.1},
                    "Ai": {"MeanMs": (0.049 if after else 0.019) + (rep - 1) * 0.001,
                           "P95Ms": 0.1 + rep * 0.01, "P99Ms": 0.2 + rep * 0.01}}}


class KaplanMeierTests(unittest.TestCase):
    def test_exact_events_median_and_zero(self):
        result = kaplan_meier([(1, True), (2, True), (3, True), (4, True)], 2)
        self.assertEqual(result["median_ticks"], 2)
        self.assertEqual(result["median_seconds"], 1)
        self.assertEqual(result["curve"][-1]["survival"], 0)
        self.assertEqual(kaplan_meier([(0, True)])["median_ticks"], 0)

    def test_events_and_censoring_tied_in_same_risk_set(self):
        result = kaplan_meier([(5, True), (5, True), (5, False), (9, True)])
        self.assertEqual(result["median_ticks"], 5)
        self.assertEqual(result["curve"][0], {"elapsed_ticks": 5, "at_risk": 4, "events": 2, "censored": 1, "survival": 0.5})
        self.assertEqual(result["curve"][1]["at_risk"], 1)
        tied = kaplan_meier([(5, True), (5, False), (10, False)])
        self.assertIsNone(tied["median_ticks"])
        self.assertAlmostEqual(tied["curve"][0]["survival"], 2 / 3)

    def test_early_censor_changes_later_risk_set(self):
        result = kaplan_meier([(1, False), (2, True), (3, True)])
        self.assertEqual(result["median_ticks"], 2)
        self.assertEqual(result["curve"][1]["at_risk"], 2)

    def test_empty_all_censored_and_invalid_null(self):
        self.assertEqual(kaplan_meier([])["median_status"], "no_eligible_subjects")
        result = kaplan_meier([(1, False), (3, False)])
        self.assertIsNone(result["median_seconds"])
        self.assertEqual(result["median_status"], "not_reached")
        for rows in ([(None, True)], [(-1, True)], [(True, True)], [(1, None)]):
            with self.subTest(rows=rows), self.assertRaises(ValueError):
                kaplan_meier(rows)
        with self.assertRaises(ValueError):
            cohort([unit(1, 0, None, 1)])

    def test_filter_null_ineligible_and_death_zero_event(self):
        rows = [unit(1, 0, None, None, eligible=False), unit(2, 0, 0, 0),
                unit(3, 1, 4, None), unit(4, 0, 1, 1, initial=False, eligible=False)]
        self.assertEqual(cohort(rows), [(0, True), (4, False)])
        self.assertEqual(cohort(rows, 0), [(0, True)])


class ReportTests(unittest.TestCase):
    def series(self):
        return [raw(variant, seed, rep) for seed in (27, 7331, 61012) for rep in (1, 2, 3) for variant in ("before", "after")]

    def test_timing_and_gameplay_not_multiplied_by_reps(self):
        result = build_report(self.series())
        self.assertEqual(len(result["cases"]), 3)
        case = result["cases"][0]
        self.assertAlmostEqual(case["before"]["step"]["median_process_mean_ms"], 0.29)
        self.assertAlmostEqual(case["after"]["step"]["median_process_mean_ms"], 0.31)
        self.assertAlmostEqual(case["before"]["step"]["median_process_p95_ms"], 1.0)
        self.assertAlmostEqual(case["before"]["step"]["median_process_p99_ms"], 1.2)
        self.assertAlmostEqual(case["before"]["step"]["process_mean_range_ms"][0], 0.28)
        self.assertEqual(case["before"]["gameplay"]["deaths_by_team"], {"0": 2, "1": 1})
        self.assertEqual(case["before"]["gameplay"]["observer_group_membership_changes"], 15)
        self.assertEqual(case["after"]["gameplay"]["observer_group_membership_changes"], 30)
        self.assertEqual(case["delta_after_minus_before"]["friendly_fire_hits"], -870)
        self.assertEqual(case["before"]["gameplay"]["km_ttk"]["both_teams"]["subjects"], 4)
        self.assertEqual(case["before"]["gameplay"]["km_ttk"]["both_teams"]["median_ticks"], 4)
        for pooled in result["pooled_unique_seed_cohorts"]:
            self.assertEqual(pooled["km_ttk"]["both_teams"]["subjects"], 12)  # Not 36 repeat-populations.

    def test_gameplay_mutations_rejected_even_with_same_digest(self):
        for field in ("Checkpoints", "Counters", "Units", "AiCounters", "Populations"):
            reports = self.series()
            mutated = reports[2]["Run"]
            if field == "Checkpoints": mutated[field][0]["Checksum"] = "changed"
            elif field == "Units": mutated[field][0]["ElapsedExposureTicks"] = 99
            elif field == "Counters": mutated[field]["FriendlyFireHits"] += 1
            elif field == "AiCounters": mutated[field]["TargetSwitches"] += 1
            else: mutated[field][0]["Alive"] = 99
            with self.subTest(field=field), self.assertRaises(ValueError):
                build_report(reports)

    def test_observer_sum_mismatch_rejected(self):
        reports = self.series()
        for report in reports:
            report["Run"]["Teams"][0]["GroupMembershipChanges"] += 1
        with self.assertRaises(ValueError):
            build_report(reports)


if __name__ == "__main__":
    unittest.main()
