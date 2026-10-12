import copy
import unittest

from summarise import NOMINAL_SEEDS, schedule, summary


def report(variant, seed, rep):
    return {"Schema": "tactics-study-v1", "Variant": variant, "Port": "secs", "Seed": seed, "Repetition": rep,
            "ProcessId": seed * 100 + rep * 10 + (1 if variant == "after" else 0), "StartedUtc": "2026-10-12T00:00:00Z",
            "ConfigSha256": f"full-config-{variant}-{seed}", "SharedComparisonConfigSha256": f"same-config-{seed}",
            "DamageRevision": "layered-weapon-damage-v1", "Ticks": 2400, "WarmupTicks": 2400, "CheckpointTicks": 240,
            "Runtime": "same-runtime", "Os": "same-os", "Architecture": "X64", "LogicalProcessors": 8,
            "BuildConfiguration": "Release", "SimulationSource": [{"Path": "Core.cs", "Sha256": variant}],
            "HarnessSource": [{"Path": "Program.cs", "Sha256": "shared"}],
            "Binaries": [{"Name": "Core", "Sha256": variant}],
            "Run": {"ReplayVerified": True, "Step": {"MeanMs": 2 if variant == "before" else 1.9},
                    "Ai": {"MeanMs": 0.4}, "Counters": {"ShipsCreated": 24, "ShipsDestroyed": 7, "FriendlyFireHits": 1}, "AiCounters": {},
                    "Teams": [{"Team": 0, "BirthsCumulative": 12, "DeathsCumulative": 3, "AliveAtEnd": 9},
                              {"Team": 1, "BirthsCumulative": 12, "DeathsCumulative": 4, "AliveAtEnd": 8}],
                    "Units": [{"TrackedInitial": True, "TtkEligible": True, "DeathTick": None, "RightCensored": True, "Unexposed": False}]}}


class SummaryTests(unittest.TestCase):
    def series(self):
        return [report(variant, seed, rep) for variant, seed, rep in schedule(sorted(NOMINAL_SEEDS), 3)]

    def test_rotation_alternates_and_reverses_pair_order(self):
        rows = schedule([27, 7331, 61012], 3)
        self.assertEqual(len(rows), 18)
        for i in range(0, len(rows), 2):
            self.assertEqual(rows[i][1:], rows[i + 1][1:])
            self.assertNotEqual(rows[i][0], rows[i + 1][0])
        self.assertNotEqual(rows[0][0], rows[6][0])
        with self.assertRaises(ValueError):
            schedule([27], 2)

    def test_complete_series_and_explicit_censoring(self):
        result = summary(self.series())
        self.assertTrue(result["nominal_three_seed_three_rep_series_complete"])
        self.assertEqual(len(result["paired_deltas_after_minus_before"]), 9)
        for group in result["groups"]:
            self.assertEqual(group["ttk_right_censored_by_process"], [1, 1, 1])
            self.assertEqual(group["ttk_death_events_by_process"], [0, 0, 0])

    def test_mismatch_duplicate_unpaired_and_survivor_inference_rejected(self):
        cases = []
        data = self.series(); data[1]["SharedComparisonConfigSha256"] = "different"; cases.append(data)
        data = self.series(); data.append(copy.deepcopy(data[0])); cases.append(data)
        cases.append(self.series()[:-1])
        data = self.series(); data[0]["Run"]["Counters"]["ShipsDestroyed"] = 0; cases.append(data)
        data = self.series(); data[0]["Run"]["Teams"][0]["AliveAtEnd"] = 1; cases.append(data)
        data = self.series(); data[0]["Run"]["ReplayVerified"] = False; cases.append(data)
        data = self.series(); data[1]["ProcessId"] = data[0]["ProcessId"]; cases.append(data)
        for data in cases:
            with self.subTest(data=data[0]["Seed"]), self.assertRaises(ValueError):
                summary(data)

    def test_one_pair_reports_incomplete_not_three_independent_reps(self):
        result = summary([report("before", 27, 1), report("after", 27, 1)])
        self.assertFalse(result["nominal_three_seed_three_rep_series_complete"])


if __name__ == "__main__":
    unittest.main()
