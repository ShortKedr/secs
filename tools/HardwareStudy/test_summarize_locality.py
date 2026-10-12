import copy
import json
from pathlib import Path
import tempfile
import unittest

from summarize_locality import percentile, summarize


class LocalitySummaryTests(unittest.TestCase):
    def setUp(self):
        base = Path(__file__).resolve().parents[2] / "Benchmarks.Local/tmp"
        base.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=base)
        self.directory = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        plan = []
        for label in ("before", "after"):
            for repeat, value in enumerate((1., 2., 10.), 1):
                path = self.directory / f"{label}-{repeat}.json"
                phase = {"EndTick": 2, "Checksum": "ABC", "Counters": {"Hits": 2}, "Tactics": {},
                         "MeanCounts": [3], "FinalCounts": [3], "StepSamplesMs": [value, value],
                         "AllocatedBytes": 0, "ManagedHeapBytes": 32,
                         "Export": {"MeanMs": .2}, "Stages": [{"Name": "ai", "Timing": {"MeanMs": value}}]}
                study = {"Config": {"Seed": 7}, "Ticks": 2, "PhaseTicks": 2, "Runs": [{"Port": "SECS", "Phases": [phase]}]}
                path.write_text(json.dumps(study), encoding="utf-8")
                plan.append({"profile": "low", "label": label, "repeat": repeat, "output": str(path)})
        self.manifest = {"plan": plan, "ticks": 2, "phaseTicks": 2}
        self.save_manifest()

    def save_manifest(self):
        (self.directory / "manifest.json").write_text(json.dumps(self.manifest), encoding="utf-8")

    def change(self, field, value):
        path = Path(self.manifest["plan"][-1]["output"])
        study = json.loads(path.read_text())
        study["Runs"][0]["Phases"][0][field] = copy.deepcopy(value)
        path.write_text(json.dumps(study), encoding="utf-8")

    def test_independent_run_median_not_pooled_mean(self):
        row = summarize(self.directory)["rows"][0]
        self.assertEqual(row["stepMeanMs"], {"median": 2., "min": 1., "max": 10., "runs": 3})
        self.assertEqual(row["p99Ms"]["median"], 2.)

    def test_checkpoint_rejection(self):
        self.change("Checksum", "DEF")
        with self.assertRaises(AssertionError):
            summarize(self.directory)

    def test_population_rejection(self):
        self.change("MeanCounts", [2])
        with self.assertRaises(AssertionError):
            summarize(self.directory)

    def test_missing_independent_repeat(self):
        self.manifest["plan"].pop(); self.save_manifest()
        with self.assertRaises(AssertionError):
            summarize(self.directory)

    def test_nearest_rank_and_singleton(self):
        self.assertEqual(percentile([7], .99), 7)
        self.assertEqual(percentile(list(range(1, 21)), .95), 19)


if __name__ == "__main__":
    unittest.main()
