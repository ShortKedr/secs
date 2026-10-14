import json
import math
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import analyze_current as report


def summary(mean=1):
    return {"MeanMs": mean, "StddevMs": 0, "MedianMs": mean, "P95Ms": mean,
            "P99Ms": mean, "MaxMs": mean, "BudgetMisses": 0, "BudgetMissPercent": 0}


def sample_run(preset=None, mean=1, hud=False):
    metadata = {"Ticks": report.TICKS, "Runtime": "test runtime", "Os": "test OS",
                "Options": {**report.PRESETS[preset or "benchmark"]}}
    if preset is not None:
        metadata["Preset"] = preset
    stages = [{"Name": name, "Timing": summary(mean/20)} for name in report.STAGE_NAMES]
    result = {"Metadata": metadata, "Timing": summary(mean), "Stages": stages,
              "Provenance": {"Sources": {"Lite": "0"*40, "Classic": "1"*40,
                                         "Morpeh": "2"*40, "StaticEcs": "3"*40,
                                         "StaticPack": "4"*40}},
              "AllocatedBytes": 0, "Checksum": "A"*16,
              "Counters": {"Shots": 1, "Hits": 1, "ShipsCreated": 1, "ShipsDestroyed": 1},
              "Windows": [{"Ships": 1, "Turrets": 2}]}
    if hud:
        result["HudTiming"] = summary(mean/2)
        result["HudWindows"] = [{"StartTick": i*report.HUD_WINDOW+1, "EndTick": (i+1)*report.HUD_WINDOW,
            "Timing": summary(mean/2), "Stages": stages,
            **{"Mean" + key: 3.5 for key in report.COUNTS}} for i in range(report.TICKS//report.HUD_WINDOW)]
    return result


class CurrentCampaignTests(unittest.TestCase):
    def test_format2_requires_exact_primary_and_churn_profiles(self):
        manifest = {"Workload": {"Preset": "game", "Seed": 7331, "ShipsPerTeam": 96,
                                  "GameplayChurnMultiplier": 2},
                    "Profiles": [{"Name": name, "Primary": name == "high",
                                  "SyntheticChurnOpsPerTick": rate,
                                  "FrameworkOrder": list(report.V2_FRAMEWORK_ORDER[name])}
                                 for name, rate in report.V2_PROFILES.items()]}
        self.assertEqual(tuple(report.validate_v2_profiles(manifest)), tuple(report.V2_PROFILES))
        manifest["Profiles"][1]["SyntheticChurnOpsPerTick"] = 2
        with self.assertRaisesRegex(ValueError, "synthetic churn rate"):
            report.validate_v2_profiles(manifest)

    def test_format2_synthetic_metadata_checks_expected_count_and_scope(self):
        metadata = {"OpsPerTick": 4, "PoolSize": 32, "Operations": 4 * report.TICKS,
                    "Adds": 2 * report.TICKS, "Removes": 2 * report.TICKS,
                    "Scope": "fixed pre-Step hook included in outer tick timer; excluded from Battle.LastTickMs and stage timings; pool setup excluded",
                    "Timing": summary()}
        report.validate_synthetic_metadata(metadata, 4, "sample")
        metadata["Operations"] -= 1
        with self.assertRaisesRegex(ValueError, "totals mismatch"):
            report.validate_synthetic_metadata(metadata, 4, "sample")
        metadata["Operations"] += 1
        metadata["Adds"] += 33
        metadata["Removes"] -= 33
        with self.assertRaisesRegex(ValueError, "totals mismatch"):
            report.validate_synthetic_metadata(metadata, 4, "sample")

    def test_format2_report_localizes_sections_and_compares_cumulative_windows_to_remainder(self):
        runs = {}
        for profile in report.V2_PROFILES:
            for framework in report.FRAMEWORKS:
                stages = [{"Name": name, "Timing": summary(.2)} for name in report.STAGE_NAMES]
                phase = {"Timing": summary(1), "Stages": stages}
                opening = {"First600": phase, "First2000": phase,
                           "RemainderAfter2000": phase}
                runs[profile, framework] = {
                    "Metadata": {"Runtime": "test runtime", "Os": "test OS",
                                 "SyntheticChurn": {"Timing": summary(.01)}},
                    "Timing": summary(1), "HudTiming": summary(1), "Stages": stages, "OpeningPhases": opening,
                    "Provenance": {"Sources": {"StaticEcs": "a" * 40, "StaticPack": "b" * 40}},
                    "HudWindows": [{"Timing": summary(.5), "MeanShips": 1}],
                    "InitialWindow": {"Timing": summary(1)},
                    "Windows": [{"Ships": 1, "Turrets": 2}],
                    "Counters": {"Shots": 1, "Hits": 1, "ShipsCreated": 1, "ShipsDestroyed": 0},
                    "Checksum": "A" * 16, "AllocatedBytes": 0,
                }
        manifest = {"StartedAt": "start", "FinishedAt": "finish", "SourceRevision": "f" * 40}
        hardware = {"Cpu": {"Name": "test CPU"}, "TotalPhysicalMemoryBytes": 1024**3}
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            root = Path(directory)
            data = root / "current"
            data.mkdir()
            with patch.object(report, "DOCS", root):
                report.render_format2(runs, manifest, hardware, data, True)
            english = (root / "REPORT.md").read_text(encoding="utf-8")
            russian = (root / "REPORT.ru.md").read_text(encoding="utf-8")
            self.assertIn("Cumulative outer mean 1–600", english)
            self.assertIn("Remainder outer mean 2,001–108,000", english)
            self.assertIn("## Совпадение игрового состояния", russian)
            self.assertIn("## Стадии SECS: начало, остаток и полный прогон", russian)
            self.assertTrue((data / "summary.csv").exists())
    def test_campaign_preset_is_known_and_matches_requested_input(self):
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            path = Path(directory)
            manifest = {"Completed": True, "Ticks": 108000, "WarmupTicks": 600, "Preset": "typo"}
            (path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Unknown campaign preset"):
                report.validate(path, False)
            manifest["Preset"] = "benchmark"
            (path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Unexpected campaign preset"):
                report.validate(path, False, "game")

    def test_preset_validation_keeps_legacy_benchmark_and_rejects_mixed_options(self):
        legacy = sample_run()["Metadata"]
        report.validate_preset(legacy, "benchmark", "legacy")
        with self.assertRaisesRegex(ValueError, "Preset"):
            report.validate_preset(legacy, "game", "legacy")
        game = sample_run("game")["Metadata"]
        report.validate_preset(game, "game", "game")
        game["Options"]["EndlessReinforcements"] = False
        with self.assertRaisesRegex(ValueError, "EndlessReinforcements"):
            report.validate_preset(game, "game", "game")

    def test_preset_booleans_are_not_accepted_as_enum_integers(self):
        metadata = sample_run()["Metadata"]
        metadata["Options"]["FleetComposition"] = False
        with self.assertRaisesRegex(ValueError, "FleetComposition"):
            report.validate_preset(metadata, "benchmark", "legacy")

    def test_hud_requires_complete_contiguous_samples_and_consistent_stage_means(self):
        run = sample_run("game", hud=True)
        report.validate_hud(run, "game")
        run["HudWindows"][1]["StartTick"] += 1
        with self.assertRaisesRegex(ValueError, "noncontiguous"):
            report.validate_hud(run, "game")
        run["HudWindows"][1]["StartTick"] -= 1
        removed = run["HudWindows"].pop()
        with self.assertRaisesRegex(ValueError, "incomplete HudWindows"):
            report.validate_hud(run, "game")
        run["HudWindows"].append(removed)
        run["Stages"] = [{"Name": stage["Name"], "Timing": summary(2)} for stage in run["Stages"]]
        with self.assertRaisesRegex(ValueError, "HUD stage mean"):
            report.validate_hud(run, "game")

    def test_hud_missing_is_legacy_only_and_nonfinite_count_fails(self):
        report.validate_hud(sample_run(), "legacy")
        with self.assertRaisesRegex(ValueError, "requires HudWindows"):
            report.validate_hud(sample_run("benchmark"), "fresh")
        run = sample_run("game", hud=True)
        run["HudWindows"][0]["MeanShips"] = float("nan")
        with self.assertRaisesRegex(ValueError, "MeanShips"):
            report.validate_hud(run, "game")

    def test_stage_names_and_published_summary_bounds_are_validated(self):
        run = sample_run()
        report.validate_stages(run["Stages"], report.TICKS, "stages")
        run["Stages"][1]["Name"] = "collect"
        with self.assertRaisesRegex(ValueError, "stages"):
            report.validate_stages(run["Stages"], report.TICKS, "stages")
        stats = summary()
        stats["P99Ms"] = 2
        with self.assertRaisesRegex(ValueError, "inconsistent"):
            report.validate_summary(stats, report.TICKS, "timing")

    def test_common_worst_hud_window_compares_same_phase_across_ecs(self):
        runs = {("nominal", f): sample_run("game", hud=True) for f in report.FRAMEWORKS}
        runs["nominal", "secs"]["HudWindows"][4]["Timing"] = summary(5)
        runs["nominal", "lite"]["HudWindows"][9]["Timing"] = summary(6)
        self.assertEqual(report.hud_selection(runs, "nominal"), [("first", 0), ("second", 1), ("worst", 9)])

    def test_hud_count_trajectory_is_canonical_but_timing_is_not(self):
        run = {**sample_run("game", hud=True), "Checkpoints": [], "Tactics": {},
               "Windows": []}
        canonical = report.canonical(run)
        run["HudWindows"][0]["Timing"] = summary(99)
        self.assertEqual(report.canonical(run), canonical)
        run["HudWindows"][0]["MeanShips"] = 2
        self.assertNotEqual(report.canonical(run), canonical)

    def test_total_step_uses_measured_mean_and_all_profiles_show_every_stage(self):
        runs = {(p, f): sample_run(mean=.2) for p in report.PROFILES for f in report.FRAMEWORKS}
        runs["nominal", "secs"]["Metadata"]["WallSeconds"] = 999
        self.assertAlmostEqual(report.total_step_seconds(runs["nominal", "secs"]), 21.6)
        for profile in report.PROFILES:
            rows = report.stage_table(runs, profile, report.TimingPolicy())
            self.assertEqual([row[0] for row in rows], [*report.STAGE_NAMES, "Total Step"])
            self.assertTrue(all("0.2000" in str(value) for value in rows[-1][1:]))

    def test_stage_colors_keep_unrounded_values(self):
        # Both print 0.0001, but their raw difference exceeds the 5% threshold.
        runs = {("nominal", f): sample_run(mean=1) for f in report.FRAMEWORKS}
        for f in report.FRAMEWORKS:
            runs["nominal", f]["Stages"][0]["Timing"] = summary(.0001049)
        runs["nominal", "secs"]["Stages"][0]["Timing"] = summary(.0000999)
        rows = report.stage_table(runs, "nominal", report.TimingPolicy())
        self.assertIn("/best.svg", str(rows[0][1]))
        self.assertIn("0.0001", str(rows[0][1]))
        self.assertNotIn("/best.svg", str(rows[0][2]))
        self.assertIn("0.0001", str(rows[0][2]))

    def test_fresh_csv_requires_raw_hud_and_stage_columns(self):
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            path = Path(directory)
            (path / "nominal-secs.ticks.csv").write_text("tick,tickMs\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "missing HUD/stage samples"):
                report.validate_ticks(path, "nominal", "secs", sample_run("game", hud=True))

    def test_game_report_defaults_to_its_own_bilingual_names(self):
        runs = {(p, f): sample_run("game", hud=True) for p in report.PROFILES for f in report.FRAMEWORKS}
        manifest = {"Preset": "game", "StartedAt": "start", "FinishedAt": "finish", "SourceRevision": "f"*40}
        hardware = {"Cpu": {"Name": "test CPU"}, "TotalPhysicalMemoryBytes": 1024**3}
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            root = Path(directory)
            data = root / "game-current"
            data.mkdir()
            with patch.object(report, "DOCS", root):
                report.render(runs, manifest, hardware, data, False)
            self.assertFalse((root / "REPORT.md").exists())
            english = (root / "GAME_REPORT.md").read_text(encoding="utf-8")
            russian = (root / "GAME_REPORT.ru.md").read_text(encoding="utf-8")
            self.assertIn("[Русский](GAME_REPORT.ru.md)", english)
            self.assertIn("[English](GAME_REPORT.md)", russian)
            self.assertIn("# Space Battle benchmarks — game preset", english)
            self.assertIn("# Бенчмарки Space Battle — игровой preset", russian)
            self.assertIn("Other workload: [finite battle](REPORT.md).", english)
            self.assertIn("Другой режим: [конечный бой](REPORT.ru.md).", russian)
            self.assertNotIn("](GAME_REPORT.md)", english.split("Other workload:", 1)[1])
            self.assertIn("Total Step s", english)
            self.assertEqual(english.count("| Total Step |"), 3)
            self.assertIn("Game preset: LightSwarm", english)
            self.assertIn("--data-dir docs/space-battle/game-current --preset game --report-base GAME_REPORT", english)
            self.assertIn("worst / 1–600", english)

    def test_finite_report_links_only_to_the_game_preset(self):
        runs = {(p, f): sample_run("benchmark") for p in report.PROFILES for f in report.FRAMEWORKS}
        manifest = {"Preset": "benchmark", "StartedAt": "start", "FinishedAt": "finish", "SourceRevision": "f"*40}
        hardware = {"Cpu": {"Name": "test CPU"}, "TotalPhysicalMemoryBytes": 1024**3}
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            root = Path(directory)
            data = root / "current"
            data.mkdir()
            with patch.object(report, "DOCS", root):
                report.render(runs, manifest, hardware, data, False)
            english = (root / "REPORT.md").read_text(encoding="utf-8")
            russian = (root / "REPORT.ru.md").read_text(encoding="utf-8")
            self.assertIn("# Space Battle benchmarks — finite battle", english)
            self.assertIn("# Бенчмарки Space Battle — конечный бой", russian)
            self.assertIn("Other workload: [game defaults](GAME_REPORT.md).", english)
            self.assertIn("Другой режим: [настройки игры](GAME_REPORT.ru.md).", russian)
            self.assertNotIn("](REPORT.md)", english.split("Other workload:", 1)[1])

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
