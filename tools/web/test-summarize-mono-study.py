import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("summary", Path(__file__).with_name("summarize-mono-study.py"))
summary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(summary)


class SummaryTests(unittest.TestCase):
    def test_independent_median_not_best_single_run(self):
        self.assertEqual(summary.range_median([3, 1, 2]), {"median": 2, "min": 1, "max": 3, "runs": 3})

    def test_even_repeat_count(self):
        self.assertEqual(summary.range_median([4, 1, 3, 2])["median"], 2.5)


if __name__ == "__main__":
    unittest.main()
