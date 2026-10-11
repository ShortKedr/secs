"""Fixed effect scale, independent repeats and scoped report heatmap policy."""

from decimal import Decimal
import json
from pathlib import Path
import re
import tempfile
import unittest
import xml.etree.ElementTree as ET

import analyze
from heatmap import (
    IndependentRun, Metric, PALETTE, TimingPolicy, legend,
    shade_columns, shade_rows, shade_values, write_assets,
)
import report_locale


MARKERS = ("best", "strong", "good", "fair", "middle", "weak", "poor", "bad", "worst")
STAGES = "collect ai thermal-inventory steering weapons carriers physics hits lifecycle".split()
TIMING_METRICS = (
    "wall_seconds", "tick.mean_ms", "tick.p95_ms", "tick.p99_ms", "tick.max_ms",
    "phase.early.mean_ms", "phase.middle.mean_ms", "phase.late.mean_ms",
    *(f"stage.{stage}.mean_ms" for stage in STAGES),
)


class HeatmapTests(unittest.TestCase):
    def assert_marker(self, value, name):
        self.assertEqual(re.findall(r"heatmap/(\w+)\.svg", str(value)), [name])
        self.assertNotIn("**", str(value))

    def assert_markers(self, values, names):
        self.assertEqual(len(values), len(names))
        for value, name in zip(values, names):
            self.assert_marker(value, name)

    @staticmethod
    def repeated(display, summaries, comparison=None):
        return Metric(display, comparison=comparison, runs=tuple(
            IndependentRun(f"run-{index}", value) for index, value in enumerate(summaries)
        ))

    @staticmethod
    def entry(**overrides):
        entry = {
            "profile": "nominal", "metric": "stage.ai.mean_ms", "threshold_ms": "0.01",
            "reason_en": "Calibrated practical AI cost.",
            "reason_ru": "Калиброванная практическая стоимость ИИ.",
        }
        entry.update(overrides)
        return entry

    def load_policy(self, entries):
        with tempfile.TemporaryDirectory(dir=analyze.ROOT) as directory:
            path = Path(directory) / "policy.json"
            path.write_text(json.dumps({"timing_absolute_thresholds": entries}, ensure_ascii=False),
                            encoding="utf-8")
            return TimingPolicy.from_file(path)

    def test_fixed_palette_and_piecewise_scale(self):
        self.assertEqual(tuple(name for name, _ in PALETTE), MARKERS)
        values = [Decimal(value) for value in (
            "100", "105", "107.5", "110", "112.5", "115",
            "118.75", "122.5", "126.25", "130", "1000",
        )]
        self.assert_markers(shade_values(values), (
            "best", "best", "strong", "good", "fair", "middle",
            "weak", "poor", "bad", "worst", "worst",
        ))

    def test_fixed_scale_does_not_stretch_to_group_endpoints(self):
        self.assert_markers(shade_values([100, 110]), ["best", "good"])
        self.assert_markers(shade_values([100, 110, 115, 1000]),
                            ["best", "good", "middle", "worst"])
        self.assert_markers(shade_values([1000, 1100]), ["best", "good"])

    def test_full_red_starts_at_thirty_percent_only(self):
        self.assert_markers(shade_values([100, Decimal("129.999999"), 130]),
                            ["best", "bad", "worst"])
        self.assert_markers(shade_values([100, Decimal("70.000001"), 70], "higher"),
                            ["best", "bad", "worst"])

    def test_higher_direction_uses_best_as_denominator(self):
        self.assert_markers(shade_values([
            Decimal(value) for value in ("70", "77.5", "85", "90", "95", "100")
        ], direction="higher"), ["worst", "poor", "middle", "good", "best", "best"])

    def test_equal_tiny_and_five_percent_groups_are_neutral(self):
        for direction in ("lower", "higher"):
            for values in ([0, "0.000", 0], [100, 100], [100, Decimal("100.000001")]):
                with self.subTest(direction=direction, values=values):
                    self.assert_markers(shade_values(values, direction), ["equal"] * len(values))
        self.assert_markers(shade_values([100, 105]), ["equal", "equal"])
        self.assert_markers(shade_values([95, 100], "higher"), ["equal", "equal"])
        self.assert_markers(shade_values([100, Decimal("105.000001")]), ["best", "best"])

    def test_ties_never_bold_numeric_extremes(self):
        self.assert_markers(shade_values([100, 100, 115, 130, 130]),
                            ["best", "best", "middle", "worst", "worst"])

    def test_zero_baseline_without_epsilon(self):
        self.assert_markers(shade_values([0, Decimal("0.000000001"), 1]),
                            ["best", "worst", "worst"])
        self.assert_markers(shade_values([0, 1], "higher"), ["worst", "best"])
        self.assert_markers(shade_values([0, 0], "higher"), ["equal", "equal"])
        self.assert_markers(shade_values([0, Decimal("0.01")], absolute_threshold="0.01"),
                            ["equal", "equal"])

    def test_unknown_nonfinite_and_negative_values_are_excluded(self):
        unknown = [None, "unknown", "NaN", "Infinity", "-Infinity", -1, Decimal("-0.01")]
        self.assertEqual(shade_values(unknown), unknown)
        self.assertEqual(shade_values([*unknown, 7]), [*unknown, 7])
        result = shade_values([*unknown, 100, 115])
        self.assertEqual(result[:len(unknown)], unknown)
        self.assert_markers(result[len(unknown):], ["best", "middle"])
        metric = Metric("1.00", comparison="NaN")
        self.assertEqual(shade_values([metric, 100]), [metric, 100])

    def test_raw_comparison_preserves_display_and_suffix(self):
        values = [Metric("0.00", " @ 30", Decimal("0.001")),
                  Metric("0.00", " @ 1", Decimal("0.00115"))]
        result = shade_values(values)
        self.assert_markers(result, ["best", "middle"])
        self.assertTrue(result[0].endswith(" 0.00 @ 30"))
        self.assertTrue(result[1].endswith(" 0.00 @ 1"))
        self.assertEqual(str(values[0]), "0.00 @ 30")
        self.assertEqual(values[0].comparison, Decimal("0.001"))
        self.assert_markers(shade_values([Metric("3.40", " @ 30"), Metric("5.20", " @ 1")]),
                            ["best", "worst"])
        self.assert_markers(shade_values([Metric("99.00", comparison=0), Metric("99.00", comparison=1)]),
                            ["best", "worst"])
        self.assert_markers(shade_values([Metric("unknown", comparison=100), Metric("unknown", comparison=115)]),
                            ["best", "middle"])

    def test_independent_groups_preserve_rows_and_unselected_columns(self):
        rows = [
            ["low", "heap", 100, "a"], ["high", "heap", 1000, "b"],
            ["low", "heap", 110, "c"], ["high", "heap", 1300, "d"],
            ["low", "private", 100, "e"], ["low", "private", 115, "f"],
            ["low", "native", 8, "g"], ["low", "native", "unknown", "h"],
        ]
        original = [row[:] for row in rows]
        result = shade_columns(rows, [2], group_by=(0, 1))
        self.assert_markers([row[2] for row in result[:6]],
                            ["best", "best", "good", "worst", "best", "middle"])
        self.assertEqual(result[6:], rows[6:])
        self.assertEqual(rows, original)
        self.assertEqual([row[:2] + row[3:] for row in result],
                         [row[:2] + row[3:] for row in original])

    def test_stage_rows_leave_share_neutral_and_input_unchanged(self):
        rows = [["ai", 100, 110, 90], ["physics", 115, 100, 50]]
        result = shade_rows(rows, [1, 2])
        self.assert_markers(result[0][1:3], ["best", "good"])
        self.assert_markers(result[1][1:3], ["middle", "best"])
        self.assertEqual([row[3] for row in result], [90, 50])
        self.assertEqual(rows, [["ai", 100, 110, 90], ["physics", 115, 100, 50]])

    def test_overlapping_and_near_touching_repeat_envelopes_are_neutral(self):
        for second in ([105, 130, 140], [121, 130, 140], [126, 130, 140]):
            with self.subTest(second=second):
                self.assert_markers(shade_values([
                    self.repeated("100", [90, 100, 120]), self.repeated("130", second),
                ]), ["equal", "equal"])

    def test_median_not_lucky_minimum_controls_repeated_ranking(self):
        values = [self.repeated("1", [1, 100, 100], comparison=1),
                  self.repeated("110", [110, 110, 110])]
        result = shade_values(values)
        self.assert_markers(result, ["best", "good"])
        self.assertTrue(result[0].endswith(" 1"))

    def test_even_repeat_median_not_minimum_sets_reference(self):
        self.assert_markers(shade_values([
            self.repeated("90", [90, 110]), self.repeated("121", [121, 124]),
        ]), ["best", "poor"])

    def test_higher_repeat_median_ignores_lucky_high_outlier(self):
        self.assert_markers(shade_values([
            self.repeated("1000", [100, 100, 1000]), self.repeated("90", [90, 90, 90]),
        ], "higher"), ["best", "good"])

    def test_clear_repeated_differences_in_both_directions(self):
        values = [self.repeated("100", [99, 100, 101]),
                  self.repeated("130", [129, 130, 131])]
        self.assert_markers(shade_values(values), ["best", "worst"])
        self.assert_markers(shade_values(values, "higher"), ["poor", "best"])

    def test_higher_repeat_envelope_suppresses_lag(self):
        self.assert_markers(shade_values([
            self.repeated("100", [90, 100, 110]), self.repeated("80", [70, 80, 90]),
        ], "higher"), ["equal", "equal"])

    def test_tied_reference_envelopes_do_not_depend_on_framework_order(self):
        narrow = self.repeated("100", [99, 100, 101])
        wide = self.repeated("100", [80, 100, 125])
        candidate = self.repeated("130", [129, 130, 131])
        for values in ([narrow, wide, candidate], [wide, narrow, candidate]):
            self.assert_markers(shade_values(values), ["equal", "equal", "equal"])

    def test_partial_or_singleton_repeats_make_whole_known_group_gray(self):
        cases = [
            [self.repeated("100", [99, 101]), Metric(130)],
            [self.repeated("100", [100]), self.repeated("130", [129, 131])],
            [self.repeated("100", [100]), self.repeated("130", [130])],
        ]
        for values in cases:
            with self.subTest(values=values):
                self.assert_markers(shade_values(values), ["equal", "equal"])

    def test_unknown_metric_does_not_require_repeat_data(self):
        values = [self.repeated("100", [99, 101]), self.repeated("130", [129, 131]), None]
        self.assert_markers(shade_values(values)[:2], ["best", "worst"])
        self.assertIsNone(shade_values(values)[2])

    def test_invalid_independent_run_ids_are_rejected(self):
        for ids in (("same", "same"), ("", "other"), ("   ", "other")):
            with self.subTest(ids=ids), self.assertRaises(ValueError):
                shade_values([Metric(100, runs=tuple(IndependentRun(key, 100) for key in ids)), 130])

    def test_absolute_threshold_is_inclusive_and_can_neutralize_one_row(self):
        self.assert_markers(shade_values([100, 110], absolute_threshold=10), ["equal", "equal"])
        self.assert_markers(shade_values([100, 110, 130], absolute_threshold=10),
                            ["best", "equal", "worst"])
        self.assert_markers(shade_values([100, 90, 70], "higher", absolute_threshold=10),
                            ["best", "equal", "worst"])
        self.assert_markers(shade_values([100, 110], absolute_threshold=0), ["best", "good"])

    def test_repeat_envelope_must_clear_absolute_and_relative_thresholds(self):
        values = [self.repeated("100", [99, 100, 101]),
                  self.repeated("130", [129, 130, 131])]
        self.assert_markers(shade_values(values, absolute_threshold=28), ["equal", "equal"])
        self.assert_markers(shade_values(values, absolute_threshold=27), ["best", "worst"])

    def test_invalid_direction_and_absolute_thresholds_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "direction"):
            shade_values([1, 2], direction="guess")
        for threshold in (-1, "NaN", "Infinity", "-Infinity", "unknown"):
            with self.subTest(threshold=threshold), self.assertRaises(ValueError):
                shade_values([1, 2], absolute_threshold=threshold)

    def test_policy_defaults_and_exact_scopes_with_wall_unit_conversion(self):
        empty = self.load_policy([])
        for profile in ("low", "nominal", "high"):
            for metric in TIMING_METRICS:
                self.assertIsNone(empty.threshold(profile, metric))
        policy = self.load_policy([
            self.entry(), self.entry(profile="high", metric="wall_seconds", threshold_ms="12.5"),
        ])
        self.assertEqual(policy.threshold("nominal", "stage.ai.mean_ms"), Decimal("0.01"))
        self.assertIsInstance(policy.threshold("nominal", "stage.ai.mean_ms"), Decimal)
        self.assertEqual(policy.threshold("high", "wall_seconds"), Decimal("0.0125"))
        self.assertIsNone(policy.threshold("low", "stage.ai.mean_ms"))
        self.assertIsNone(policy.threshold("nominal", "stage.physics.mean_ms"))
        self.assertIsNone(policy.threshold("nominal", "wall_seconds"))

    def test_policy_accepts_all_and_only_explicit_timing_scopes(self):
        entries = [self.entry(profile=profile, metric=metric, threshold_ms=0)
                   for profile in ("low", "nominal", "high") for metric in TIMING_METRICS]
        policy = self.load_policy(entries)
        for entry in entries:
            self.assertEqual(policy.threshold(entry["profile"], entry["metric"]), Decimal(0))
        for overrides in (
            {"profile": "Nominal"}, {"profile": "all"}, {"profile": ""},
            {"metric": "stage.unknown.mean_ms"}, {"metric": "stage.ai.p95_ms"},
            {"metric": "tick.stddev_ms"}, {"metric": "memory.heap_bytes"}, {"metric": "*"},
        ):
            with self.subTest(overrides=overrides), self.assertRaises(ValueError):
                self.load_policy([self.entry(**overrides)])

    def test_policy_rejects_duplicates_invalid_thresholds_and_missing_reasons(self):
        with self.assertRaises(ValueError):
            self.load_policy([self.entry(), self.entry(threshold_ms=2)])
        for threshold in (-1, "NaN", "Infinity", "-Infinity", None):
            with self.subTest(threshold=threshold), self.assertRaises(ValueError):
                self.load_policy([self.entry(threshold_ms=threshold)])
        for key in ("reason_en", "reason_ru"):
            for value in (None, "", "   "):
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    self.load_policy([self.entry(**{key: value})])
            entry = self.entry()
            del entry[key]
            with self.subTest(missing=key), self.assertRaises(ValueError):
                self.load_policy([entry])

    def test_column_threshold_callback_is_scoped_to_group_and_column(self):
        policy = self.load_policy([self.entry()])
        rows = [[profile, framework, Decimal("0.01") * multiplier, Decimal("0.01") * multiplier]
                for profile in ("nominal", "low") for framework, multiplier in (("a", 1), ("b", 2))]
        metrics = {2: "stage.ai.mean_ms", 3: "stage.physics.mean_ms"}
        result = shade_columns(rows, [2, 3], group_by=(0,),
                               absolute_thresholds=lambda row, column: policy.threshold(row[0], metrics[column]))
        self.assert_markers([row[2] for row in result], ["equal", "equal", "best", "worst"])
        self.assert_markers([row[3] for row in result], ["best", "worst", "best", "worst"])
        self.assertEqual(rows[0][2], Decimal("0.01"))

    def test_row_threshold_callback_does_not_leak_between_metrics(self):
        policy = self.load_policy([self.entry()])
        rows = [["nominal", metric, Decimal("0.01"), Decimal("0.02"), 90]
                for metric in ("stage.ai.mean_ms", "stage.physics.mean_ms")]
        result = shade_rows(rows, [2, 3],
                            absolute_thresholds=lambda row: policy.threshold(row[0], row[1]))
        self.assert_markers(result[0][2:4], ["equal", "equal"])
        self.assert_markers(result[1][2:4], ["best", "worst"])
        self.assertEqual([row[4] for row in result], [90, 90])

    def test_assets_are_unchanged_small_static_and_deterministic(self):
        colors = ("#2e9b50", "#59b35d", "#91cb69", "#c7df78", "#f2d85c",
                  "#efad46", "#e87c3f", "#dc513c", "#c93637", "#8b949e")
        with tempfile.TemporaryDirectory(dir=analyze.ROOT) as directory:
            docs = Path(directory)
            write_assets(docs)
            before = {path.name: path.read_bytes() for path in (docs / "heatmap").glob("*.svg")}
            self.assertEqual(set(before), {f"{name}.svg" for name in (*MARKERS, "equal")})
            for name, color in zip((*MARKERS, "equal"), colors):
                data = before[f"{name}.svg"]
                root = ET.fromstring(data)
                self.assertEqual(root.tag, "{http://www.w3.org/2000/svg}svg")
                self.assertEqual(root.attrib, {"width": "12", "height": "12", "viewBox": "0 0 12 12"})
                self.assertEqual([element.tag.rsplit("}", 1)[-1] for element in root], ["title", "rect"])
                self.assertEqual(root[0].text, name)
                self.assertEqual(root[1].attrib, {
                    "x": "1", "y": "1", "width": "10", "height": "10", "rx": "2",
                    "fill": color, "stroke": "#59636e", "stroke-width": "0.6",
                })
                self.assertLess(len(data), 400)
            write_assets(docs)
            self.assertEqual(before, {path.name: path.read_bytes() for path in (docs / "heatmap").glob("*.svg")})

    def test_bilingual_legend_markers_and_policy_reasons(self):
        policy = self.load_policy([self.entry()])
        cells = shade_values(["100.00", "110.00", "130.00"])
        source = report_locale.catalogue_body(analyze.DOCS, "ru") + "\n" + "\n".join(legend(policy))
        source += "\n" + "\n".join(analyze.table(["Метрика", "low", "nominal", "high"], [["ms", *cells]]))
        self.assertIn("### Цветовая шкала", "\n".join(legend()))
        self.assertIn(self.entry()["reason_ru"], source)
        en, ru = report_locale.report_versions(
            source, analyze.DOCS, extra_phrases={self.entry()["reason_ru"]: self.entry()["reason_en"]},
        )
        self.assertIn(self.entry()["reason_en"], en)
        self.assertNotIn(self.entry()["reason_ru"], en)
        self.assertIn(self.entry()["reason_ru"], ru)
        self.assertIn("### Color Scale", en)
        self.assertIn("### Цветовая шкала", ru)
        pattern = r'!\[([^\]]+)\]\(heatmap/(\w+)\.svg "([^"]+)"\)'
        en_markers, ru_markers = re.findall(pattern, en), re.findall(pattern, ru)
        self.assertEqual([name for _, name, _ in en_markers], [name for _, name, _ in ru_markers])
        self.assertEqual(set(name for _, name, _ in en_markers), {*MARKERS, "equal"})
        for (en_alt, _, en_title), (ru_alt, _, ru_title) in zip(en_markers, ru_markers):
            self.assertEqual(en_alt, en_title)
            self.assertEqual(ru_alt, ru_title)
            self.assertNotRegex(en_alt, r"[А-Яа-яЁё]")
            self.assertRegex(ru_alt, r"[А-Яа-яЁё]")
        for number in ("100.00", "110.00", "130.00"):
            self.assertIn(number, en)
            self.assertIn(number, ru)
        self.assertNotRegex(en + ru, r'heatmap/\w+\.svg "[^"]+"\) \*\*[\d.]+\*\*')

    def test_report_versions_extra_phrases_remains_optional(self):
        source = report_locale.catalogue_body(analyze.DOCS, "ru") + "\n" + "\n".join(legend())
        en, ru = report_locale.report_versions(source, analyze.DOCS)
        self.assertIn("### Color Scale", en)
        self.assertIn("### Цветовая шкала", ru)
        self.assertEqual(re.findall(r"heatmap/\w+\.svg", en), re.findall(r"heatmap/\w+\.svg", ru))


if __name__ == "__main__":
    unittest.main()
