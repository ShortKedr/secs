import unittest
from decimal import Decimal

from benchmark_colors import EQUAL_COLOR, Metric, colors, compact_legend, levels, shade_values


class SharedColourTests(unittest.TestCase):
    def test_tables_and_charts_share_group_colours(self):
        values = [100, 115, 130]
        self.assertEqual(levels(values), ["best", "middle", "worst"])
        self.assertEqual(colors(values), ["#2e9b50", "#f2d85c", "#c93637"])
        markers = shade_values(values, language="en", asset_path="../heatmap")
        self.assertTrue(all("../heatmap/" in str(v) for v in markers))
        self.assertIn("Lag", markers[0])

    def test_close_groups_stay_grey_on_charts_and_tables(self):
        values = [100, 103, 105]
        self.assertEqual(colors(values), [EQUAL_COLOR]*3)
        self.assertEqual(levels(values), ["equal"]*3)
        self.assertEqual(levels([100]), [None])

    def test_display_rounding_does_not_control_colours(self):
        values = [Metric("0.00", comparison=.001), Metric("0.00", comparison=.00115)]
        self.assertEqual(levels(values), ["best", "middle"])
        self.assertTrue(all(str(v).endswith(" 0.00") for v in shade_values(values)))

    def test_beyond_five_percent_never_gets_up_to_five_label(self):
        for values, direction in (([100, Decimal("105.000001")], "lower"),
                                  ([100, Decimal("94.999999")], "higher")):
            self.assertEqual(levels(values, direction), ["best", "strong"])
            marked = shade_values(values, direction, language="en")
            self.assertIn("Lag between 5% and 30%", marked[1])

    def test_published_envelopes_suppress_overlapping_comparisons(self):
        values = [Metric("100", comparison=100, interval=(90, 120)),
                  Metric("130", comparison=130, interval=(110, 140))]
        self.assertEqual(levels(values), ["equal", "equal"])
        values[1] = Metric("150", comparison=150, interval=(145, 155))
        self.assertEqual(levels(values), ["best", "worst"])

    def test_incomplete_or_invalid_envelopes_do_not_invent_evidence(self):
        self.assertEqual(levels([Metric(100, interval=(90, 110)), 150]), ["equal", "equal"])
        for metric in (Metric(100, interval=(105, 110)), Metric("range", interval=(90, 110))):
            with self.assertRaises(ValueError):
                levels([metric, 150])

    def test_localised_legends_share_fixed_thresholds(self):
        for language in ("en", "ru"):
            text = "\n".join(compact_legend(language, "../heatmap"))
            self.assertIn("5%", text)
            self.assertIn("15%", text)
            self.assertIn("30%", text)
            self.assertIn("../heatmap/equal.svg", text)


if __name__ == "__main__":
    unittest.main()
