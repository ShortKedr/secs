import unittest

import analyze
import report_locale


class ReportLocaleTests(unittest.TestCase):
    def source(self, prose):
        return report_locale.catalogue_body(analyze.DOCS, "ru") + "\n\n" + prose

    def test_locale_navigation_and_data_preservation(self):
        en, ru = report_locale.report_versions(self.source("## Время тика\n\n| low | 0.3966 | BC1ED478AF94A722 |\n[SYSTEMS.md](SYSTEMS.md)"), analyze.DOCS)
        self.assertTrue(en.startswith("English | [Русский](REPORT.ru.md)"))
        self.assertTrue(ru.startswith("[English](REPORT.md) | Русский"))
        self.assertIn("| low | 0.3966 | BC1ED478AF94A722 |", en)
        self.assertIn("| low | 0.3966 | BC1ED478AF94A722 |", ru)
        self.assertIn("](SYSTEMS.ru.md)", ru)
        self.assertIn("## Tick Timing", en)
        self.assertIn("https://shortkedr.github.io/secs/", en)
        self.assertIn("https://shortkedr.github.io/secs/?lang=ru", ru)
        self.assertIn("](../../SpaceBattle.Web/README.md)", en)
        self.assertIn("](../../SpaceBattle.Web/README.ru.md)", ru)
        self.assertIn(report_locale.HISTORICAL_NOTICE_EN, en)
        self.assertIn(report_locale.HISTORICAL_NOTICE_RU, ru)
        self.assertIn("](TACTICS.md)", en)
        self.assertIn("](TACTICS.ru.md)", ru)

    def test_missing_translation_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Missing English"):
            report_locale.report_versions(self.source("Новая непереведённая фраза"), analyze.DOCS)

    def test_existing_russian_link_is_not_localized_twice(self):
        _, ru = report_locale.report_versions(self.source("[TACTICS](TACTICS.ru.md)"), analyze.DOCS)
        self.assertIn("](TACTICS.ru.md)", ru)
        self.assertNotIn(".ru.ru.md", ru)

    def test_missing_catalogue_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "exactly one"):
            report_locale.report_versions("## Время тика", analyze.DOCS)

    def test_dynamic_misses_and_ranking(self):
        text = ("- Суммарно во всех 15 случаях: 7 тиков с превышением бюджета 16.67 ms; подробности по случаям ниже.\n"
                "- Наблюдаемый минимум MeanMs по профилям: low: secs; high: lite. Это не универсальное ранжирование.")
        en, _ = report_locale.report_versions(self.source(text), analyze.DOCS)
        self.assertIn("7 ticks above the 16.67 ms budget", en)
        self.assertIn("low: secs; high: lite", en)


if __name__ == "__main__":
    unittest.main()
