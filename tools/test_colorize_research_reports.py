import unittest

import colorize_research_reports as report


class ResearchColourTests(unittest.TestCase):
    def test_groups_never_compare_different_profiles(self):
        rows = [['low', 'before', '100'], ['high', 'before', '1000'],
                ['low', 'after', '130'], ['high', 'after', '1010']]
        result = report.colorize_table(rows, [('column', (2,), (0,))], 'en', 'heatmap')
        self.assertIn('best.svg', result[0][2])
        self.assertIn('worst.svg', result[2][2])
        self.assertIn('equal.svg', result[1][2])
        self.assertIn('equal.svg', result[3][2])
        self.assertEqual(rows[0][2], '100')
        self.assertEqual([r[:2] for r in result], [r[:2] for r in rows])

    def test_reported_range_never_invents_independent_runs(self):
        metric = report.metric('100.00 [90.00, 120.00]')
        self.assertEqual(metric.runs, ())
        self.assertEqual(metric.interval, (90, 120))
        self.assertEqual(str(metric), '100.00 [90.00, 120.00]')

    def test_clean_retains_unrelated_images_and_code_examples(self):
        image = '![Diagram](diagrams/flow.svg)'
        marker = '![Lag up to 5%](heatmap/best.svg "Lag up to 5%") '
        for fence in ('```', '~~~'):
            example = f'{fence}markdown\n{marker}100\n{fence}\n'
            text = image + '\n' + marker + '100\n' + example
            cleaned = report.clean(text)
            self.assertIn(image, cleaned)
            self.assertIn(example, cleaned)
            self.assertEqual(cleaned, image + '\n100\n' + example)

    def test_missing_repeat_envelopes_are_neutral(self):
        result = report.colorize_table([['before', '100'], ['after', '200']],
                    [('column', (1,))], 'en', 'heatmap', repeated=True)
        self.assertTrue(all('equal.svg' in row[1] for row in result))

    def test_changed_table_inventory_requires_explicit_review(self):
        text = '# Report\n\n| Case | Before | After |\n| --- | --- | --- |\n| Test | 100 | 130 |\n'
        with self.assertRaisesRegex(ValueError, 'table inventory changed'):
            report.colorize_document(text, 'docs/benchmarks/optimization-methodology', 'en',
                    report.ROOT / 'docs/benchmarks/optimization-methodology.md')

    def test_all_research_reports_are_idempotent(self):
        for stem in report.SCHEMAS:
            for language in ('en', 'ru'):
                path = report.ROOT / (stem + ('.ru.md' if language == 'ru' else '.md'))
                with path.open(encoding='utf-8', newline='') as stream:
                    text = stream.read()
                self.assertEqual(report.colorize_document(text, stem, language, path), text, str(path))


if __name__ == '__main__':
    unittest.main()
