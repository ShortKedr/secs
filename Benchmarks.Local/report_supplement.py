"""Validate supplemental campaign inputs and write deterministic bilingual reports.

BDN aggregate: metadata + Benchmarks (unchanged full-json exporter records).
No benchmark, chart or build is launched by this module.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import statistics
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from benchmark_colors import Metric, shade_values, marker, compact_legend


def require(ok, message):
    if not ok:
        raise ValueError(message)


def number(value):
    require(isinstance(value, (int, float)) and not isinstance(value, bool)
            and math.isfinite(value) and value >= 0, f'Invalid measurement: {value!r}')
    return value


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'),
                      parse_constant=lambda value: (_ for _ in ()).throw(ValueError(value)))


def expected_secs():
    result = set()
    for kind, methods, counts in (
        ('EcsBenchmarks', ['LinearIteration', 'AddRemoveStructuralChurn'], [100000]),
        ('ComparisonBenchmarks', ['Create', 'Iterate', 'Churn', 'Destroy'], [1000, 100000]),
        ('StressBenchmarks', ['DenseIteration', 'SparseIteration', 'WideIteration', 'RandomLookup',
                             'PayloadChurn', 'Recycle10Percent', 'MixedFrames'], [100000, 1000000]),
        ('OptimizedApiCreationBenchmarks', ['EmptyAddAdd', 'CreateEntityComponents', 'PreparedFactoryBatch'], [100000, 1000000]),
        ('OptimizedApiLookupBenchmarks', ['WorldGet', 'CachedAccessorGet'], [100000, 1000000]),
    ):
        result.update((kind, method, count, None) for method in methods for count in counts)
    return result


def expected_legacy():
    # Concrete source classes include inherited [Benchmark] Run methods.
    source = (ROOT / 'SECS.OtherBenchmarks/Benchmarks.cs').read_text(encoding='utf-8-sig')
    names = re.findall(r'public class (\w+)\s*:', source)
    names += ['DeleteEntity', 'MultiSystems', 'FilterMismatchSystems']
    return {(name, 'Run', 100000, padding) for name in names
            for padding in ([0, 10] if name.startswith('SystemWith') else [None])}


def bdn_key(row):
    kind = row['Type'].split('.')[-1]
    params = row['Parameters']
    if isinstance(params, dict):
        values = {key: int(value) for key, value in params.items()}
    else:
        values = {key: int(value) for key, value in re.findall(r'(EntityCount|Padding)\s*[=:]\s*(\d+)', str(params))}
    require('EntityCount' in values, 'BDN case has no EntityCount')
    return kind, row['Method'], values['EntityCount'], values.get('Padding')


def validate_bdn(data, expected):
    metadata = data['metadata']
    require(metadata['configuration'] == 'Release', 'BDN must use Release')
    require(metadata['job'] == 'Short' and metadata['toolchain'] == 'InProcess', 'Unexpected BDN job/toolchain')
    require(metadata['iterationCount'] == 8 and metadata['warmupCount'] == 3, 'Expected 8 actual / 3 warmup iterations')
    require(isinstance(metadata['runtime'], str) and metadata['runtime'].strip(), 'Missing BDN runtime')
    indexed = {}
    for row in data['Benchmarks']:
        key = bdn_key(row)
        require(key not in indexed, f'Duplicate BDN case: {key}')
        actual = [item for item in row['Measurements'] if item['IterationMode'] == 'Workload'
                  and item['IterationStage'] == 'Actual']
        require(len(actual) == 8, f'Expected 8 actual samples: {key}')
        for item in actual:
            require(number(item['Operations']) > 0, 'Zero measurement operations')
            number(item['Nanoseconds'])
        stats = row['Statistics']
        number(stats['Mean']); number(stats['StandardDeviation'] if 'StandardDeviation' in stats else stats['StdDev'])
        require(isinstance(stats['N'], int) and 1 <= stats['N'] <= 8, 'Invalid retained Statistics.N')
        indexed[key] = row
    require(set(indexed) == expected, f'BDN coverage mismatch: missing={expected-set(indexed)}, extra={set(indexed)-expected}')
    return indexed


def validate_api(data):
    expected = {(operation, method, count) for operation, methods in (
        ('Create', ['EmptyAddAdd', 'CreateEntityComponents', 'PreparedFactoryBatch']),
        ('Lookup', ['WorldGet', 'CachedAccessorGet'])) for method in methods for count in (100000, 1000000)}
    indexed = {}
    for row in data['results']:
        key = row['operation'], row['method'], row['entityCount']
        require(key not in indexed, 'Duplicate API case')
        samples = list(map(number, row['samplesNs']))
        require(len(samples) == 8, 'API needs exactly 8 samples')
        require(math.isclose(statistics.mean(samples), number(row['meanNs']), rel_tol=1e-12), 'API mean mismatch')
        require(math.isclose(statistics.stdev(samples), number(row['stddevNs']), rel_tol=1e-12, abs_tol=1e-9), 'API deviation mismatch')
        number(row['allocatedBytes']); number(row['storageNativeBytes'])
        indexed[key] = row
    require(set(indexed) == expected, 'API coverage mismatch')
    return indexed


def validate_storage(data):
    require(data['metadata']['passes'] == 20 and data['metadata']['entityCount'] == 100000, 'Storage scope mismatch')
    require([row['pass'] for row in data['samples']] == list(range(21)), 'Storage pass coverage mismatch')
    for row in data['samples']:
        number(row['nativeBytes']); number(row['chunkCount'])


def table(headers, rows):
    return '\n'.join(['| ' + ' | '.join(headers) + ' |', '| ' + ' | '.join(['---'] * len(headers)) + ' |']
                     + ['| ' + ' | '.join(map(str, row)) + ' |' for row in rows])


def api_table(indexed, operation, language):
    methods = ['EmptyAddAdd', 'CreateEntityComponents', 'PreparedFactoryBatch'] if operation == 'Create' else ['WorldGet', 'CachedAccessorGet']
    rows = []
    for count in (100000, 1000000):
        raw = [indexed[operation, method, count]['meanNs'] for method in methods]
        cells = shade_values([Metric(f'{value / 1e6:.6f}', comparison=value) for value in raw],
                             language=language, asset_path='../heatmap')
        rows.append([f'{count:,}', *cells])
    return table(['N', *methods], rows)


def bdn_table(indexed, language):
    neutral = marker('equal', 'Neutral' if language == 'en' else 'Нейтрально', '../heatmap')
    rows = []
    for (kind, method, count, padding), row in sorted(indexed.items()):
        stats = row['Statistics']
        deviation = stats.get('StandardDeviation', stats.get('StdDev'))
        rows.append([kind, method, f'{count:,}', '—' if padding is None else padding,
                     f'{neutral} {stats["Mean"] / 1e6:.6f}', f'{deviation / 1e6:.6f}', f'8 / {stats["N"]}'])
    return table(['Class', 'Method', 'N', 'Padding', 'Mean ms', 'SD ms', 'Actual / retained'], rows)


def render(api, storage, secs, legacy, language, secs_metadata=None, legacy_metadata=None):
    ru = language == 'ru'
    parts = [('[English](SUPPLEMENT.md) | Русский' if ru else 'English | [Русский](SUPPLEMENT.ru.md)'),
             '# ' + ('Дополнительные измерения SECS' if ru else 'Supplemental SECS measurements'),
             ('Все значения относятся к текущим локальным исходникам и этой машине. Полные операции измерены в миллисекундах. Это отдельные нагрузки; таблицы не создают общего рейтинга API и систем.' if ru else
              'These measurements apply to the current local sources and this machine. Timings are milliseconds per complete operation. These are separate workloads; tables do not form an overall ranking of APIs or systems.'),
             ('Сырые данные: [API](optimized-api.json), [storage](storage.json), [BDN SECS](bdn-secs.json), [BDN legacy](bdn-legacy.json).' if ru else
              'Raw data: [API](optimized-api.json), [storage](storage.json), [BDN SECS](bdn-secs.json), [BDN legacy](bdn-legacy.json).'),
             ('[Метаданные кампании](supplement.json) фиксируют ревизию, исходники и хеши входных данных. [Сравнение пяти ECS](REPORT.ru.md) — отдельная общая нагрузка.' if ru else
              '[Campaign metadata](supplement.json) records the revision, sources and input hashes. The [five-ECS comparison](REPORT.md) uses a separate shared workload.'),
             '## API',
             ('Восемь замеров на кейс. Создание: новый мир за повтор; подготовка, проверка, освобождение и принудительный GC вне таймера. Lookup использует прогретый мир и общий seeded порядок. Цвет сравнивает только методы одной операции при одинаковом N, по исходной точности; он не доказывает статистическую значимость.' if ru else
              'Eight samples per case. Creation uses a fresh world per repetition; setup, validation, disposal and forced GC are outside timing. Lookup uses warmed storage and a shared seeded order. Colour compares only methods of the same operation at the same N, using raw precision; it does not establish statistical significance.'),
             '### Create', api_table(api, 'Create', language), '### Lookup', api_table(api, 'Lookup', language),
             table(['Operation', 'Method', 'N', 'SD ms', 'Managed allocated bytes', 'Retained native bytes'],
                   [[operation, method, f'{count:,}', f'{row["stddevNs"] / 1e6:.6f}',
                     f'{row["allocatedBytes"]:.3f}', row['storageNativeBytes']]
                    for (operation, method, count), row in sorted(api.items())]),
             '\n'.join(compact_legend(language, '../heatmap', '../COLORS.ru.md' if ru else '../COLORS.md')),
             '## Storage',
             ('Не измерение времени: сохранённые нативные колонковые буферы после добавления/удаления payload 256 bytes, N=100000. Управляемые массивы, заголовки и overhead allocator исключены; значения не являются полной памятью процесса.' if ru else
              'Untimed retained native column buffers after adding/removing a 256-byte payload, N=100000. Managed arrays, headers and allocator overhead are excluded; these are not total process memory.'),
             table(['Pass', 'Chunks', 'Native bytes'], [[r['pass'], r['chunkCount'], r['nativeBytes']] for r in storage['samples']]),
             '## BenchmarkDotNet',
             table(['Suite', 'Runtime', 'Configuration', 'Toolchain'],
                   [[name, metadata['runtime'], metadata['configuration'], metadata['toolchain']]
                    for name, metadata in [('SECS', secs_metadata), ('Legacy', legacy_metadata)]]),
             ('Release, Short, InProcess: заданы 3 warmup и 8 actual итераций, вместо стандартных 3 actual у Short. Колонка actual / retained отделяет все собранные замеры от выборки Statistics после обработки выбросов. Setup/cleanup вне операций; естественный GC внутри включён. InProcess использует общий процесс, без изоляции child runtime. Allocation diagnostics не измеряют нативные колонки.' if ru else
              'Release, Short, InProcess: explicitly 3 warmup and 8 actual iterations, rather than Short’s default 3 actual iterations. Actual / retained distinguishes collected observations from Statistics after outlier processing. Setup/cleanup are outside operations; natural GC within operations is included. InProcess shares the host process without child-runtime isolation. Allocation diagnostics exclude native columns.'),
             ('34 кейса SECS и 51 legacy кейс проверены по точным ключам. Здесь нет сопоставления разных операций: все ячейки нейтральны. MixedFrames — пакет 120 кадров. Padding 10 меняет долю совпадающих сущностей и не сравнивается с Padding 0 как ускорение одного workload.' if ru else
              'Exact keys cover 34 SECS and 51 legacy cases. Different operations are not ranked: these cells are neutral. MixedFrames is a 120-frame batch. Padding 10 changes the fraction of matching entities and is not compared with Padding 0 as a speedup of one workload.'),
             ('Mutable-кейсы выполняют одну операцию за итерацию, чтобы сохранить корректный reset. Для многих BDN сообщает MinIterationTime ниже рекомендуемых 100 ms: восемь коротких наблюдений имеют ограниченную точность. Эти числа не заменяют длинный default-job и общую матрицу пяти ECS.' if ru else
              'Mutable cases invoke one operation per iteration to preserve reset semantics. Many receive BDN MinIterationTime warnings below its recommended 100 ms: eight short observations have limited precision. These values do not replace a longer default job or the shared five-ECS matrix.'),
             '### SECS: 34', bdn_table(secs, language), '### Legacy: 51', bdn_table(legacy, language)]
    return '\n\n'.join(parts) + '\n'


def generate(directory):
    directory = directory.resolve()
    require(directory.is_relative_to(ROOT), 'Outputs must stay inside this project')
    manifest_path = directory / 'supplement.json'
    if manifest_path.exists():
        manifest = load(manifest_path)
        require(manifest['completed'] is True and manifest['sourceRevision'] and manifest['sourceHashes'], 'Incomplete campaign provenance')
        require(set(manifest['inputHashes']) == {'optimized-api.json', 'storage.json', 'bdn-secs.json', 'bdn-legacy.json'}, 'Incomplete supplemental inputs')
        for name, expected in manifest['inputHashes'].items():
            require(hashlib.sha256((directory / name).read_bytes()).hexdigest().upper() == expected.upper(), 'Input hash mismatch: ' + name)
    api_data = load(directory / 'optimized-api.json')
    storage = load(directory / 'storage.json')
    secs_data = load(directory / 'bdn-secs.json')
    legacy_data = load(directory / 'bdn-legacy.json')
    api = validate_api(api_data)
    validate_storage(storage)
    secs = validate_bdn(secs_data, expected_secs())
    legacy = validate_bdn(legacy_data, expected_legacy())
    require(secs_data['metadata']['runtime'] == legacy_data['metadata']['runtime'], 'Mixed BDN runtimes')
    outputs = {name: render(api, storage, secs, legacy, language, secs_data['metadata'], legacy_data['metadata']) for name, language in
               [('SUPPLEMENT.md', 'en'), ('SUPPLEMENT.ru.md', 'ru')]}
    # Validate all four inputs and both reports before replacing either output.
    for name, content in outputs.items():
        (directory / name).write_text(content, encoding='utf-8')
    return outputs


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', nargs='?', type=Path, default=ROOT / 'docs/benchmarks/current')
    generate(parser.parse_args().directory)
