import json
import math
from pathlib import Path
import tempfile
import unittest

import analyze_current as report


class CurrentCampaignTests(unittest.TestCase):
    def test_recomputes_quantiles_and_budget_misses(self):
        values = [0, 1, 2, 3, 20]
        stats = {"MeanMs": 5.2, "StddevMs": math.sqrt(69.7),
                 "MedianMs": 2, "P95Ms": 20, "P99Ms": 20, "MaxMs": 20,
                 "BudgetMisses": 1, "BudgetMissPercent": 20}
        report.validate_stats(stats, values, "sample")
        stats["P99Ms"] = 3
        with self.assertRaisesRegex(ValueError, "P99Ms"):
            report.validate_stats(stats, values, "sample")

    def test_timing_is_not_canonical_but_turrets_and_tactics_are(self):
        run = {"Checkpoints": [], "Checksum": "A"*16, "Counters": {}, "Tactics": {"Leases": 1},
               "Timing": {"MeanMs": 1}, "Windows": [{"StartTick": 1, "EndTick": 3600,
               "Ships": 1, "Projectiles": 2, "Debris": 3, "Particles": 4, "Turrets": 5, "Counters": {}}]}
        baseline = report.canonical(run)
        run["Timing"]["MeanMs"] = 999
        self.assertEqual(report.canonical(run), baseline)
        run["Windows"][0]["Turrets"] += 1
        self.assertNotEqual(report.canonical(run), baseline)

    def test_rejects_unfinished_campaign(self):
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            path = Path(directory)
            (path / "manifest.json").write_text('{"Completed": false}', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "incomplete"):
                report.validate(path, False)

    def test_rejects_partial_case_matrix(self):
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            path = Path(directory)
            manifest = {"Completed": True, "Ticks": 108000, "WarmupTicks": 600,
                        "SourceRevision": "test", "SourceHashes": {"source.cs": "hash"}}
            (path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "15-case"):
                report.validate(path, False)

    def test_duplicate_json_keys_fail(self):
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            path = Path(directory) / "result.json"
            path.write_text('{"Ticks": 108000, "Ticks": 1}', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "duplicate"):
                report.read_json(path)

    def test_gc_during_window_aggregation_is_valid(self):
        report.validate_gc_interval([3, 1, 0], [4, 1, 0], [4, 1, 0], "telemetry")
        with self.assertRaisesRegex(ValueError, "outside sample interval"):
            report.validate_gc_interval([3, 1, 0], [5, 1, 0], [4, 1, 0], "telemetry")


if __name__ == "__main__":
    unittest.main()
