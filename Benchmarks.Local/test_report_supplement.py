"""Focused report validation tests; use unittest after the measurement campaign."""
import copy
import json
from pathlib import Path
import statistics
import tempfile
import unittest

import report_supplement as report


def bdn_fixture(keys):
    rows = []
    for kind, method, count, padding in sorted(keys):
        rows.append({'Type': kind, 'Method': method,
                     'Parameters': f'EntityCount={count}' + (f', Padding={padding}' if padding is not None else ''),
                     'Statistics': {'Mean': 1000.125, 'StandardDeviation': 2, 'N': 7},
                     'Measurements': [{'IterationMode': 'Workload', 'IterationStage': 'Actual',
                                       'Operations': 1, 'Nanoseconds': 1000 + i} for i in range(8)]})
    return {'metadata': {'configuration': 'Release', 'runtime': '.NET 8 fixture', 'job': 'Short',
                         'toolchain': 'InProcess', 'iterationCount': 8, 'warmupCount': 3}, 'Benchmarks': rows}


def api_fixture():
    rows = []
    for operation, methods in [('Create', ['EmptyAddAdd', 'CreateEntityComponents', 'PreparedFactoryBatch']),
                               ('Lookup', ['WorldGet', 'CachedAccessorGet'])]:
        for count in (100000, 1000000):
            for index, method in enumerate(methods):
                samples = [1000.1 * (index + 1) + i for i in range(8)]
                rows.append({'operation': operation, 'method': method, 'entityCount': count,
                             'samplesNs': samples, 'meanNs': statistics.mean(samples),
                             'stddevNs': statistics.stdev(samples), 'allocatedBytes': 42, 'storageNativeBytes': 256})
    return {'metadata': {'sampleCount': 8}, 'results': rows}


class ReportTests(unittest.TestCase):
    def test_exact_matrix_coverage(self):
        self.assertEqual(len(report.expected_secs()), 34)
        self.assertEqual(len(report.expected_legacy()), 51)
        for keys in (report.expected_secs(), report.expected_legacy()):
            data = bdn_fixture(keys)
            self.assertEqual(set(report.validate_bdn(data, keys)), keys)
            missing = copy.deepcopy(data)
            missing['Benchmarks'].pop()
            with self.assertRaises(ValueError):
                report.validate_bdn(missing, keys)

    def test_reject_invalid_samples_and_duplicates(self):
        keys = report.expected_secs()
        for mutate in (lambda d: d['Benchmarks'][0]['Measurements'].pop(),
                       lambda d: d['Benchmarks'][0]['Statistics'].update(Mean=float('nan')),
                       lambda d: d['Benchmarks'][0]['Measurements'][0].update(Operations=0),
                       lambda d: d['Benchmarks'].append(copy.deepcopy(d['Benchmarks'][0]))):
            data = bdn_fixture(keys)
            mutate(data)
            with self.assertRaises(ValueError):
                report.validate_bdn(data, keys)

    def test_api_checks_raw_sample_mean(self):
        data = api_fixture()
        self.assertEqual(len(report.validate_api(data)), 10)
        data['results'][0]['samplesNs'][0] *= 2
        with self.assertRaises(ValueError):
            report.validate_api(data)

    def test_color_uses_unrounded_values(self):
        indexed = report.validate_api(api_fixture())
        for method, value in [('EmptyAddAdd', 1), ('CreateEntityComponents', 1.300001), ('PreparedFactoryBatch', 1.01)]:
            indexed['Create', method, 100000]['meanNs'] = value
        content = report.api_table(indexed, 'Create', 'en')
        self.assertIn('worst.svg', content)

    def test_bilingual_outputs_idempotent_and_neutral_bdn(self):
        with tempfile.TemporaryDirectory(dir=report.ROOT / 'Benchmarks.Local/tmp') as name:
            directory = Path(name)
            fixtures = {'optimized-api.json': api_fixture(),
                        'storage.json': {'metadata': {'passes': 20, 'entityCount': 100000},
                                         'samples': [{'pass': i, 'chunkCount': 2, 'nativeBytes': 256} for i in range(21)]},
                        'bdn-secs.json': bdn_fixture(report.expected_secs()),
                        'bdn-legacy.json': bdn_fixture(report.expected_legacy())}
            for filename, data in fixtures.items():
                (directory / filename).write_text(json.dumps(data), encoding='utf-8')
            first = report.generate(directory)
            self.assertEqual(first, report.generate(directory))
            for content in first.values():
                self.assertIn('8 / 7', content)
                self.assertIn('equal.svg', content)
                self.assertIn('0.001000', content)
            (directory / 'bdn-legacy.json').write_text('{"Benchmarks": []}', encoding='utf-8')
            with self.assertRaises((ValueError, KeyError)):
                report.generate(directory)
            self.assertEqual((directory / 'SUPPLEMENT.md').read_text(encoding='utf-8'), first['SUPPLEMENT.md'])


if __name__ == '__main__':
    unittest.main()
