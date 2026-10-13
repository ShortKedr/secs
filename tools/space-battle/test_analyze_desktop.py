import math
import unittest

import analyze_desktop as report


class HudTimingTests(unittest.TestCase):
    def test_startup_spike_ages_out_of_the_hud_but_not_full_run_mean(self):
        values = [301.0] + [1.0] * 1199
        self.assertEqual(report.hud_mean(values, 10), 31.0)
        self.assertEqual(report.hud_mean(values, 30), 11.0)
        self.assertEqual(report.hud_mean(values, 600), 1.5)
        self.assertEqual(report.hud_mean(values, 601), 1.0)
        self.assertEqual(report.hud_mean(values, 1200), 1.0)
        self.assertEqual(report.mean(values), 1.25)

    def test_hud_uses_only_the_latest_complete_window(self):
        values = [1.0] * 600 + [4.0] * 600
        self.assertEqual(report.hud_mean(values, 900), 2.5)
        self.assertEqual(report.hud_mean(values, 1200), 4.0)
        self.assertEqual(report.mean(values), 2.5)

    def test_invalid_or_missing_samples_do_not_become_valid_means(self):
        for values in ([], [math.nan], [math.inf], [-1.0]):
            with self.assertRaises(ValueError):
                report.mean(values)
        for tick in (0, 3):
            with self.assertRaises(ValueError):
                report.hud_mean([1.0, 2.0], tick)


if __name__ == '__main__':
    unittest.main()
