"""Validate canonical measurements and write matching EN/RU result tables."""
import json
import hashlib
import math
import statistics
from pathlib import Path
import sys
from plot import validate as validate_base
from plot_stress import validate as validate_stress

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from benchmark_colors import Metric, compact_legend, shade_values

root = Path(__file__).resolve().parents[1]
out = root / 'docs/benchmarks/current'
base = json.loads((out / 'base.json').read_text(encoding='utf-8-sig'))
stress = json.loads((out / 'stress.json').read_text(encoding='utf-8-sig'))
manifest = json.loads((out / 'campaign.json').read_text(encoding='utf-8-sig'))
validate_base(base)
validate_stress(stress)
def require(condition, message):
    if not condition:
        raise ValueError(message)
for filename, field in (('base.json', 'baseInputSha256'), ('stress.json', 'stressInputSha256')):
    require(hashlib.sha256((out / filename).read_bytes()).hexdigest() == manifest[field], f'{filename} digest differs from campaign metadata')
for data, count in ((base, 20), (stress, 8)):
    for row in data['results']:
        samples = row['samplesNs']
        require(len(samples) == count, 'Unexpected sample count')
        require(math.isclose(statistics.mean(samples), row['meanNs'], rel_tol=1e-12), 'Mean differs from raw samples')
        require(math.isclose(statistics.stdev(samples), row['stddevNs'], rel_tol=1e-12), 'Standard deviation differs from raw samples')
    require(data['metadata']['tieredCompilation'] == '0', 'Tiering must be disabled')
    require(data['metadata']['runtime'] == base['metadata']['runtime'], 'Mixed runtimes')
date = manifest['started'][:10]
products = {row['framework'] for data in (base, stress) for row in data['results']
            if row['framework'] in ('SECS', 'SKECS')}
require(len(products) == 1, 'Campaign must measure exactly one SECS/SKECS product identity')
product = products.pop()
frameworks = (product, 'LeoECS Lite', 'LeoECS Classic', 'Morpeh', 'StaticEcs')
vendor_path = root / 'Benchmarks.Local/vendor-manifest.json'
require(hashlib.sha256(vendor_path.read_bytes()).hexdigest() ==
        manifest['sourceHashes']['Benchmarks.Local/vendor-manifest.json'],
        'Vendor manifest differs from measured campaign')
vendor = {item['name']: item['revision'] for item in json.loads(vendor_path.read_text(encoding='utf-8'))['components']}
for name, key in (('StaticEcs', 'staticEcs'), ('StaticPack', 'staticPack')):
    require(base['metadata']['revisions'][key] == vendor[name], f'Base {name} revision differs from vendor manifest')
    require(stress['metadata']['revisions'][key] == vendor[name], f'Stress {name} revision differs from vendor manifest')
static_version = stress['metadata']['versions']['staticEcs']
static_pack_version = stress['metadata']['versions']['staticPack']
pin_rows = '\n'.join(f'| {name} | `{manifest["sourceRevision"] if name == product else vendor[name]}` |'
                     for name in frameworks)
def table(data, language):
    indexed = {(r['scenario'], r['entityCount'], r['framework']): r for r in data['results']}
    cases = sorted({(r['entityCount'], r['scenario']) for r in data['results']})
    lines = ['| Scenario | N | ' + ' | '.join(frameworks) + ' |', '| --- | ---: | ' + ' | '.join('---:' for _ in frameworks) + ' |']
    for count, scenario in cases:
        raw_values = [indexed[scenario, count, f]['meanNs'] for f in frameworks]
        values = [value / 1e6 for value in raw_values]
        labels = [f'{v:.6f}' if v < 0.01 else f'{v:.4f}' if v < 1 else f'{v:.3f}' for v in values]
        formatted = shade_values([Metric(label, comparison=value) for label, value in zip(labels, raw_values)],
                                 language=language, asset_path='../heatmap')
        lines.append(f'| {scenario} | {count:,} | ' + ' | '.join(formatted) + ' |')
    return '\n'.join(lines)
en_tables = table(base, 'en') + '\n\n' + table(stress, 'en')
ru_tables = table(base, 'ru') + '\n\n' + table(stress, 'ru')
en_legend = '\n'.join(compact_legend('en', '../heatmap', '../COLORS.md'))
ru_legend = '\n'.join(compact_legend('ru', '../heatmap', '../COLORS.ru.md'))
en = f'''English | [Русский](REPORT.ru.md)

# ECS comparison — {date}

[Benchmark overview](../../../SKECS.Benchmarks/README.md) | [Harness and reproduction](../../../Benchmarks.Local/README.md)

[Complete SECS suites and API measurements](SUPPLEMENT.md) include 34 current and 51 legacy BenchmarkDotNet cases, plus creation/accessor and retained-storage diagnostics.

All five frameworks were measured afresh in one sequential campaign: 40 base cases and 70 stress cases. Each cell below is mean milliseconds per complete operation; lower is better. Markers show relative lag within each scenario and entity-count row; they describe effect size, not statistical significance.

Environment: {base['metadata']['runtime']}, {base['metadata']['architecture']}, {manifest['cpu']}, {manifest['logicalProcessors']} logical processors. Release, one thread, tiered compilation off. Base repository commit: `{manifest['sourceRevision']}`; working tree modified: `{manifest['workingTreeModified']}`. [Campaign metadata](campaign.json) pins the measured source and assembly hashes; [base raw samples](base.json) and [stress raw samples](stress.json) retain all observations.

## Framework revisions used

Third-party rows show exact upstream commits, not guessed release numbers. The {product} row shows the campaign's repository revision; [campaign metadata](campaign.json) also records hashes of the measured source and assemblies. Third-party commits come from the hash-verified [vendor manifest](../../../Benchmarks.Local/vendor-manifest.json).

StaticEcs is release **{static_version}**. Its compiled StaticPack dependency is **{static_pack_version}**, pinned to commit `{vendor['StaticPack']}`. The manifest records SHA-256 for every included upstream source file.

| Framework | Source revision |
| --- | --- |
{pin_rows}

## Recorded means

{en_tables}

{en_legend}

## Charts and interpretation

![Base comparison](comparison.png)

![Stress at 100,000 entities](stress-100k.png)

![Stress at 1,000,000 entities](stress-1m.png)

Error bars show sample standard deviation, not confidence intervals. Base timings retain 20 samples; stress retains 8. Setup, validation, disposal and forced GC are excluded; natural collections and Morpeh commits during operations are included. Create/Destroy use fresh worlds; other workloads reuse warmed storage. MixedFrames is a complete 120-frame batch, not one frame. Allocation fields measure managed thread allocations and exclude native memory.

Base validation passed for all five frameworks at 1,000/100,000 entities before timing. Stress validation passed at 257/4,097 entities (all seven scenarios repeated three times, payload round-trip, retired handles); each measured stress case also passed final full-population validation. Exact matrix keys, sample counts and mean/standard-deviation consistency were checked before report generation.

These are equivalent logical operations through the selected adapters, not identical storage or each framework's best possible API. Results apply to this machine, runtime, snapshots and workload. They do not establish a universal ECS ranking, Unity/IL2CPP performance, total-memory efficiency or cross-platform determinism. The complete samples expose variability; close means should not be treated as reliable wins without further experiments.

## Retained history

Previous five-framework runs: [base](../../../Benchmarks.Local/results-all-updated.json), [stress](../../../Benchmarks.Local/stress-all-updated.json), [charts](../updated/comparison.png). The separate [SECS optimization experiment](../optimization-methodology.md) uses preserved historical binaries. Neither is the current campaign or evidence of today's before/after improvement.
'''
ru = f'''[English](REPORT.md) | Русский

# Сравнение ECS — {date}

[Обзор бенчмарков](../../../SKECS.Benchmarks/README.ru.md) | [Раннер и воспроизведение](../../../Benchmarks.Local/README.ru.md)

[Полные наборы SECS и замеры API](SUPPLEMENT.ru.md): 34 актуальных и 51 legacy-кейс BenchmarkDotNet, создание/аксессоры и диагностика сохранённого хранилища.

Все пять фреймворков измерены заново в одной последовательной кампании: 40 базовых и 70 стресс-кейсов. Каждая ячейка ниже — среднее время полной операции в миллисекундах; меньше лучше. Маркеры показывают относительное отставание внутри строки сценария и числа сущностей; цвет описывает величину различия, а не статистическую значимость.

Среда: {base['metadata']['runtime']}, {base['metadata']['architecture']}, {manifest['cpu']}, {manifest['logicalProcessors']} логических процессоров. Release, один поток, tiered compilation отключён. Базовый коммит репозитория: `{manifest['sourceRevision']}`; рабочее дерево изменено: `{manifest['workingTreeModified']}`. [Метаданные кампании](campaign.json) фиксируют хеши измеренных исходников и сборок; [базовые сырые замеры](base.json) и [стресс-замеры](stress.json) сохраняют все наблюдения.

## Ревизии фреймворков в замерах

Для сторонних ECS указаны точные коммиты исходных репозиториев, а не предполагаемые номера релизов. Для {product} указана ревизия репозитория кампании; [метаданные](campaign.json) также содержат хеши измеренных исходников и сборок. Коммиты сторонних ECS взяты из проверенного по хешу [манифеста](../../../Benchmarks.Local/vendor-manifest.json).

StaticEcs соответствует релизу **{static_version}**. Его зависимость StaticPack **{static_pack_version}** собрана из коммита `{vendor['StaticPack']}`. Манифест содержит SHA-256 каждого включённого файла исходников.

| Фреймворк | Ревизия исходников |
| --- | --- |
{pin_rows}

## Измеренные средние

{ru_tables}

{ru_legend}

## Графики и интерпретация

![Базовое сравнение](comparison.png)

![Стресс при 100,000 сущностей](stress-100k.png)

![Стресс при 1,000,000 сущностей](stress-1m.png)

Погрешности показывают стандартное отклонение выборки, а не доверительные интервалы. В базовых кейсах 20 замеров, в стресс-кейсах 8. Подготовка, проверки, освобождение и принудительный GC исключены; естественные сборки и Morpeh Commit внутри операций включены. Create/Destroy используют свежие миры; остальные нагрузки повторно используют прогретое хранилище. MixedFrames — полный пакет из 120 кадров, а не один кадр. Поля аллокаций показывают управляемые выделения текущего потока и исключают нативную память.

Перед измерениями базовая проверка прошла для всех пяти фреймворков при 1,000/100,000 сущностей. Стресс-проверка прошла при 257/4,097 сущностей (семь сценариев по три повтора, добавление/удаление payload, устаревшие handles); каждый измеренный стресс-кейс также прошёл итоговую проверку всей популяции. Перед генерацией отчёта проверены точные ключи матриц, число замеров и соответствие среднего/стандартного отклонения сырым выборкам.

Это эквивалентные логические операции через выбранные адаптеры, а не одинаковое хранилище или лучшие возможные API каждого фреймворка. Результаты относятся к этой машине, runtime, снимкам исходников и нагрузке. Они не устанавливают универсальный рейтинг ECS, производительность Unity/IL2CPP, эффективность полной памяти или межплатформенный детерминизм. Сырые выборки показывают вариативность; близкие средние нельзя считать надёжной победой без дополнительных экспериментов.

## Сохранённая история

Предыдущие прогоны пяти фреймворков: [база](../../../Benchmarks.Local/results-all-updated.json), [стресс](../../../Benchmarks.Local/stress-all-updated.json), [графики](../updated/comparison.png). Отдельный [эксперимент оптимизации SECS](../optimization-methodology.ru.md) использует сохранённые исторические бинарники. Это не текущая кампания и не доказательство сегодняшнего улучшения до/после.
'''
(out / 'REPORT.md').write_text(en, encoding='utf-8')
(out / 'REPORT.ru.md').write_text(ru, encoding='utf-8')
print('Validated 40 base / 70 stress cases and wrote matching reports')
