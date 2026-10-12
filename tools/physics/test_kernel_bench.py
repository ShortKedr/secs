import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("kernel_bench", Path(__file__).with_name("kernel_bench.py"))
bench = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bench)


class KernelBenchTests(unittest.TestCase):
    def test_percentiles_use_ticks_only_within_a_repeat(self):
        self.assertEqual(bench.quantile(list(range(1, 101)), .95), 95)
        self.assertEqual(bench.quantile(list(range(1, 101)), .99), 99)
        with self.assertRaises(ValueError):
            bench.quantile([], .95)

    def test_invalid_timing(self):
        for values in ([], [-1], [float("nan")], [float("inf")]):
            with self.assertRaises(ValueError):
                bench.timing(values)

    def test_artifact_paths(self):
        with self.assertRaises(ValueError):
            bench.artifact_path(bench.ROOT / "docs" / "result.json")
        with self.assertRaises(ValueError):
            bench.artifact_path(bench.ROOT / "Benchmarks.Local" / "tmp" / ".." / "old.json")

    def test_exact_signature_excludes_performance_counters(self):
        frame = dict(frame=0, bodies=2, fixtures=2, contacts=1, hits=1, digests={"state": "ABC"}, stepMs=1)
        changed = {**frame, "stepMs": 99}
        self.assertEqual(bench.signature(frame), bench.signature(changed))
        changed["digests"] = {"state": "DEF"}
        self.assertNotEqual(bench.signature(frame), bench.signature(changed))

    def test_unpaired_repeats_rejected(self):
        with self.assertRaises(ValueError):
            bench.compare_runs([{"repeat": 0}], [{"repeat": 1}])


if __name__ == "__main__":
    unittest.main()
