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
    def current_manifest(self):
        return {"Format": 3, "Profile": "high", "FrameworkOrder": list(report.FRAMEWORKS),
                "Workload": {"Preset": "game", "Seed": 7331, "ShipsPerTeam": 96,
                             "GameplayChurnMultiplier": 2}}

    def test_format3_requires_exact_single_game_profile_and_five_frameworks(self):
        report.validate_current_profile(self.current_manifest())
        for key, value in (("Format", 2), ("Profile", "high-churn-1"),
                           ("Profiles", ["high"]), ("PrimaryProfile", "high"),
                           ("FrameworkOrder", list(report.FRAMEWORKS)[:-1])):
            with self.subTest(key=key):
                manifest = self.current_manifest()
                manifest[key] = value
                with self.assertRaises(ValueError):
                    report.validate_current_profile(manifest)

    def test_current_report_rejects_legacy_campaign_format(self):
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            root = Path(directory)
            current = root / "current"
            current.mkdir()
            with patch.object(report, "DOCS", root):
                for format_number in (1, 2):
                    (current / "manifest.json").write_text(json.dumps({"Format": format_number}), encoding="utf-8")
                    with self.assertRaisesRegex(ValueError, "single real-game high campaign"):
                        report.validate(current, False)

    def test_format3_rejects_workload_changes_and_synthetic_parameters(self):
        for key, value in (("Preset", "benchmark"), ("GameplayChurnMultiplier", 1),
                           ("Seed", 1), ("ShipsPerTeam", 32),
                           ("SyntheticChurnOpsPerTick", 0), ("PoolSize", 32)):
            with self.subTest(key=key):
                manifest = self.current_manifest()
                manifest["Workload"][key] = value
                with self.assertRaisesRegex(ValueError, "real game high workload"):
                    report.validate_current_profile(manifest)

    def test_format3_rejects_synthetic_csv_columns_even_with_zero_operations(self):
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            path = Path(directory)
            (path / "high-secs.ticks.csv").write_text(
                ",".join(["tick", "tickMs", "hudMs", *report.STAGE_NAMES, "syntheticOperations"]) + "\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "synthetic CSV columns"):
                report.validate_ticks(path, "high", "secs", sample_run("game", hud=True))

    def test_format3_rejects_synthetic_metadata(self):
        with tempfile.TemporaryDirectory(dir=report.ROOT / "Benchmarks.Local/tmp") as directory:
            path = Path(directory)
            manifest = {**self.current_manifest(), "Completed": True, "Ticks": report.TICKS,
                        "WarmupTicks": 600, "Preset": "game", "SourceRevision": "test",
                        "SourceHashes": {"source.cs": "hash"}, "Files": {}}
            (path / "hardware.json").write_text("{}", encoding="utf-8")
            for framework, name in report.FRAMEWORKS.items():
                run = sample_run("game", hud=True)
                run["Metadata"].update({"Framework": name, "WarmupTicks": 600,
                    "TargetTickRate": 60, "SimulatedSeconds": 1800, "Realtime": False,
                    "TieredCompilation": "0", "BudgetMs": report.BUDGET,
                    "SyntheticChurn": {"OpsPerTick": 0}})
                run["Metadata"]["Options"].update({"ChurnMultiplier": 2, "Seed": 7331,
                    "ShipsPerTeam": 96, "CarriersPerTeam": 3})
                (path / f"high-{framework}.json").write_text(json.dumps(run), encoding="utf-8")
                manifest["Files"][f"high-{framework}.ticks.csv"] = "unpublished"
            for file in path.glob("*.json"):
                manifest["Files"][file.name] = report.digest(file)
            with self.assertRaisesRegex(ValueError, "synthetic metadata is forbidden"):
                report.validate_format3(path, manifest, False)

    def test_format3_report_preserves_bilingual_structure_colours_and_five_ecs(self):
        runs = {}
        for profile in (report.CURRENT_PROFILE,):
            for framework in report.FRAMEWORKS:
                stages = [{"Name": name, "Timing": summary(.2)} for name in report.STAGE_NAMES]
                phase = {"Timing": summary(1), "Stages": stages}
                opening = {"First600": phase, "First2000": phase,
                           "RemainderAfter2000": phase}
                runs[profile, framework] = {
                    "Metadata": {"Runtime": "test runtime", "Os": "test OS"},
                    "Timing": summary(1), "HudTiming": summary(1), "Stages": stages, "OpeningPhases": opening,
                    "Provenance": {"Sources": {"Lite": "1" * 40, "Classic": "2" * 40,
                                               "Morpeh": "3" * 40, "StaticEcs": "a" * 40,
                                               "StaticPack": "b" * 40}},
                    "HudWindows": [{"StartTick": i*600+1, "EndTick": (i+1)*600,
                                    "Timing": summary(.5), "Stages": stages,
                                    **{"Mean" + key: 1 for key in report.COUNTS}}
                                   for i in range(2)],
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
                report.render_format3(runs, manifest, hardware, data, True)
            english = (root / "REPORT.md").read_text(encoding="utf-8")
            russian = (root / "REPORT.ru.md").read_text(encoding="utf-8")
            self.assertIn("Cumulative outer mean 1–600", english)
            self.assertIn("Remainder outer mean 2,001–108,000", english)
            self.assertIn("### Совпадение игрового состояния", russian)
            self.assertIn("### Стадии SECS: начало, остаток и полный прогон", russian)
            for label in ("Framework revisions used", "Reading the results", "### Colour scale",
                          "Tick time", "Simulation state and stages", "HUD windows: 600 ticks each",
                          "Data provenance", "Reproduction", "History and related documents"):
                self.assertIn(label, english)
            self.assertEqual(
                [line for line in english.splitlines() if line.startswith("## ")],
                ["## Framework revisions used", "## Reading the results", "## Validation",
                 "## Tick time", "## Opening windows",
                 "## Simulation state and stages", "## HUD windows: 600 ticks each",
                 "## Data provenance", "## Reproduction", "## History and related documents"])
            self.assertIn("../benchmarks/heatmap/", english.split("## Tick time", 1)[1].split("## Opening windows", 1)[0])
            self.assertIn("../benchmarks/heatmap/", russian.split("## Время тика", 1)[1].split("## Окна начала прогона", 1)[0])
            self.assertIn("../benchmarks/heatmap/", english.split("### Full-run stage means", 1)[1].split("### SECS stages", 1)[0])
            self.assertIn("| P95 ms | P99 ms | Max ms | >16.67 ms |", english)
            for text, tick_heading, opening_heading, legend, rules in (
                    (english, "## Tick time", "## Opening windows", "### Colour scale", "COLORS.md"),
                    (russian, "## Время тика", "## Окна начала прогона", "### Цветовая шкала", "COLORS.ru.md")):
                self.assertIn(legend, text)
                self.assertIn(rules, text)
                timing = text.split(tick_heading, 1)[1].split(opening_heading, 1)[0]
                for framework in report.FRAMEWORKS.values():
                    self.assertEqual(timing.count("| high | " + framework + " |"), 1)
                self.assertNotIn("high-churn", timing)
                self.assertIn("../benchmarks/heatmap/", timing)
            self.assertEqual(len((data / "summary.csv").read_text().splitlines()), 6)
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
