import copy
import json
import unittest
from unittest.mock import patch

from compare import compare_browser, compare_native


def native(mean=1.0):
    phase = dict(EndTick=300, Checksum="0123", Counters={"Hits": 7}, Tactics={"TargetSwitches": 2},
                 MeanCounts=[10, 20, 30, 40, 5], FinalCounts=[10, 20, 30, 40, 5],
                 Step={"MeanMs": mean}, Stages=[{"Name": "ai", "Timing": {"MeanMs": mean / 10}}],
                 AllocatedBytes=100, ManagedHeapBytes=1000, Store={"Entities": 105}, Export={"MeanMs": .1})
    return dict(Config={"Seed": 7331}, Runs=[dict(Port="SECS", Phases=[phase])])


class ComparisonTests(unittest.TestCase):
    def test_only_documented_structural_physics_controls_are_ignored(self):
        before, after = native(), native()
        before["Config"]["Physics"] = {"Friction": .08}
        after["Config"]["Physics"] = {"Friction": .08, "ContactCandidateMargin": 4, "CacheContactCandidates": True}
        self.compare(before, after)
        after["Config"]["Physics"]["Friction"] = .09
        with self.assertRaises(AssertionError):
            self.compare(before, after)
    def compare(self, a, b):
        with patch("pathlib.Path.read_text", side_effect=[json.dumps(a), json.dumps(b)]):
            return compare_native(["before.json", "after.json"])

    def test_timing_noise_does_not_change_behavior(self):
        rows = self.compare(native(), native(1.4))
        self.assertEqual(rows[1]["medianMs"], 1.4)

    def test_every_checkpoint_counter_and_population_is_required(self):
        for field in ("Checksum", "Counters", "Tactics", "MeanCounts", "FinalCounts"):
            before, after = native(), native()
            after["Runs"][0]["Phases"][0][field] = None
            with self.assertRaises(AssertionError, msg=field):
                self.compare(before, after)

    def test_config_and_missing_port_rejected(self):
        after = native(); after["Config"]["Seed"] = 42
        with self.assertRaises(AssertionError):
            self.compare(native(), after)
        after = native(); after["Runs"] = []
        with self.assertRaises(AssertionError):
            self.compare(native(), after)

    def test_independent_run_means_not_fastest_tick(self):
        before = native(); before["Runs"].append(copy.deepcopy(before["Runs"][0]))
        before["Runs"][1]["Phases"][0]["Step"]["MeanMs"] = 3
        rows = self.compare(before, before)
        self.assertEqual(rows[0]["medianMs"], 2)
        self.assertEqual(rows[0]["repeats"], 2)

    def test_repeated_baseline_noise_cannot_hide_gameplay_changes(self):
        before = native(); before["Runs"].append(copy.deepcopy(before["Runs"][0]))
        before["Runs"][1]["Phases"][0]["Counters"]["Hits"] += 1
        with self.assertRaises(AssertionError):
            self.compare(before, native())

    def test_browser_runtime_and_ai_counters(self):
        phase = dict(endTick=300, checksum="0123", counters=[7], tactics=[2], meanCounts=[10], finalCounts=[10],
                     coreStep={"meanMs": 1}, stages=[dict(name="ai", timing={"meanMs": .1})],
                     exportOnlyAtPhaseEnd={"meanMs": .2})
        run = dict(revision="before", runtimeMode="aot-binary", phases=[phase])
        study = dict(runs=[run, copy.deepcopy(run)])
        study["runs"][1]["revision"] = "after"
        with patch("pathlib.Path.read_text", return_value=json.dumps(study)):
            self.assertEqual(len(compare_browser("test.json")), 2)
        study["runs"][1]["phases"][0]["tactics"] = [3]
        with patch("pathlib.Path.read_text", return_value=json.dumps(study)), self.assertRaises(AssertionError):
            compare_browser("test.json")
        study["runs"][1]["runtimeMode"] = "coreclr-r2r-binary"
        with patch("pathlib.Path.read_text", return_value=json.dumps(study)), self.assertRaises(AssertionError):
            compare_browser("test.json")


if __name__ == "__main__":
    unittest.main()
