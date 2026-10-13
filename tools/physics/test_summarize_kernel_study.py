import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('kernel_summary', Path(__file__).with_name('summarize_kernel_study.py'))
summary_tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(summary_tool)
from benchmark_colors import levels


class SummaryColourTests(unittest.TestCase):
    def test_rounding_does_not_hide_a_difference_beyond_five_percent(self):
        def distribution(value):
            return dict(median=value, minimum=value, maximum=value)
        summary = {'kernel': [{'scenario': 'scene', 'versions': {
            'before': {'step': {'meanMs': distribution(100)}},
            'after': {'step': {'meanMs': distribution(105.000001)}}}}]}
        values = [summary_tool.comparison_metric(summary, 0, ['scene', version], 2, text)
                  for version, text in [('before', '100.000 [100.000, 100.000]'),
                                        ('after', '105.000 [105.000, 105.000]')]]
        self.assertEqual(levels(values), ['best', 'strong'])
        self.assertEqual(str(values[1]), '105.000 [105.000, 105.000]')

    def test_desktop_gpu_uses_its_own_distribution(self):
        summary = {'desktop': [{'version': 'final', 'gpuRenderMs':
                    {'median': .0034567, 'minimum': .0031, 'maximum': .0039}}]}
        metric = summary_tool.comparison_metric(summary, 3, ['final', 'GPU render'], 2, '0.003 [0.003, 0.004]')
        self.assertEqual(metric.comparison, .0034567)
        self.assertEqual(metric.interval, (.0031, .0039))

    def test_instrumented_counters_are_not_performance_comparisons(self):
        with self.assertRaises(ValueError):
            summary_tool.comparison_metric({}, 4, [], 2, '100')


if __name__ == '__main__':
    unittest.main()
