"""Refresh explicitly selected research comparisons without changing their numbers.

No benchmarks run. JSON/CSV artifacts are read-only. Selection is by report/table
ordinal and column, never by searching all numbers for a favorable comparison.
"""
import argparse
import json
import os
from pathlib import Path
import re

from benchmark_colors import Metric, IndependentRun, shade_values, marker, EN_LABELS, LABELS, compact_legend

ROOT = Path(__file__).resolve().parents[1]
MARKER = re.compile(r'!\[[^\]]*\]\((?:[^()\n]*/)?heatmap/(?:best|strong|good|fair|middle|weak|poor|bad|worst|equal)\.svg(?: "[^"\n]*")?\)\s*')
NUMBER = re.compile(r'(?<![A-Za-z])(?:\d[\d,]*\.?\d*|\.\d+)')
FENCES = re.compile(r'(?ms)^ {0,3}(?:`{3,}[^\n]*\n.*?^ {0,3}`{3,}|~{3,}[^\n]*\n.*?^ {0,3}~{3,})[^\n]*')

# Columns are zero-based. Row groups compare variants for one fixed workload;
# column groups never compare different runtimes, profiles, or tick windows.
SCHEMAS = {
    'docs/benchmarks/optimization-methodology': {
        0: [('row', (1, 2))], 1: [('row', (1, 2))],
        2: [('row', (1, 2)), ('row', (3, 4))]},
    'docs/space-battle/PERFORMANCE_INVESTIGATION': {
        0: [('column', (1, 2, 3, 4))], 2: [('row', (1, 2))]},
    'docs/space-battle/SPATIAL_OPTIMIZATION': {1: [('row', (1, 2))]},
    'docs/space-battle/HARDWARE_OPTIMIZATION': {1: [('column', (1, 2))]},
    'docs/space-battle/PHYSICS': {
        0: [('row', (1, 2)), ('inside', (3, 4))], 1: [('row', (1, 2))]},
    'tools/HardwareStudy/LOCALITY_OPTIMIZATION': {
        0: [('row', (1, 2)), ('paired', (4, 5))], 1: [('column', (1, 2, 3))]},
    'tools/web/MONO_COMPILER_STUDY': {
        0: [('column', (3, 4, 5))],
        1: [('column', (2, 4), (0,)), ('column_parts', (3,), (0,))],
        2: [('column', (1, 2, 3))], 3: [('row', (1, 2, 3))]},
}
TABLE_COUNTS = {
    'docs/benchmarks/optimization-methodology': 3,
    'docs/space-battle/PERFORMANCE_INVESTIGATION': 3,
    'docs/space-battle/SPATIAL_OPTIMIZATION': 2,
    'docs/space-battle/HARDWARE_OPTIMIZATION': 2,
    'docs/space-battle/PHYSICS': 4,
    'tools/HardwareStudy/LOCALITY_OPTIMIZATION': 2,
    'tools/web/MONO_COMPILER_STUDY': 5,
}
BEGIN, END = '<!-- research-colors:start -->', '<!-- research-colors:end -->'


def load(relative):
    return json.loads((ROOT / relative).read_text(encoding='utf-8-sig'))


def clean(text):
    parts, cursor = [], 0
    for block in FENCES.finditer(text):
        parts.append(MARKER.sub('', text[cursor:block.start()]))
        parts.append(block.group(0))
        cursor = block.end()
    parts.append(MARKER.sub('', text[cursor:]))
    return ''.join(parts)


def number(text):
    match = NUMBER.match(text.strip())
    if not match:
        raise ValueError(f'Expected numerical comparison: {text!r}')
    return float(match[0].replace(',', ''))


def metric(text):
    """Published range is a descriptive run envelope, never invented run IDs."""
    values = NUMBER.findall(text)
    interval = tuple(float(v.replace(',', '')) for v in values[1:3]) if '[' in text and len(values) == 3 else None
    return Metric(text, comparison=number(text), interval=interval)


def envelope(text, summary):
    if 'runMeans' in summary:
        return Metric(text, comparison=summary['median'], runs=tuple(
            IndependentRun(f'published-repeat-{i + 1}', value) for i, value in enumerate(summary['runMeans'])))
    low = summary.get('minimum', summary.get('min'))
    high = summary.get('maximum', summary.get('max'))
    return Metric(text, comparison=summary['median'], interval=(low, high))


def source_metric(stem, table, row, col, text, part=None):
    if stem == 'docs/space-battle/PERFORMANCE_INVESTIGATION' and table == 0:
        platform, start, field = {1: ('native', 1, 'stepMs'), 2: ('wasm', 1, 'stepMs'),
                                 3: ('wasm', 1, 'totalMs'), 4: ('wasm', 601, 'stepMs')}[col]
        revision = ('before', 'tactics', 'stats')[row]
        entry = next(v for v in load('docs/space-battle/performance-study/summary.json')
                     if (v['platform'], v['revision'], v['startTick']) == (platform, revision, start))
        return envelope(text, entry[field])
    if stem == 'docs/space-battle/PHYSICS':
        scenes = load('docs/space-battle/physics-experiment-summary.json')['native']['scenarios']
        if table == 0:
            engine = list(scenes[row]['engines'].values())[(col - 1) if col in (1, 2) else part]
            return envelope(text, engine[{1: 'totalMs', 2: 'totalMs', 3: 'combinedStepMs', 4: 'hitsMs'}[col]])
        if table == 1:
            engine = list(scenes[5]['engines'].values())[col - 1]
            field = ('ContactCandidatesMs', 'SolverMs', 'HitCandidatesMs', 'HitNarrowMs')[row]
            return envelope(text, engine['phasesMs'][field])
    if stem == 'docs/space-battle/SPATIAL_OPTIMIZATION' and table == 1:
        summary = load('docs/space-battle/spatial-optimization/summary.json')
        mode = ('before', 'after')[col - 1]
        if row in (0, 5):
            source = summary['native'][0 if row == 0 else 1]['coreMs'][mode]
        else:
            field = {1: 'coreMs', 2: 'stepAndExportMs', 3: 'exportMs', 4: 'coreMs'}[row]
            source = summary['browser'][1 if row == 4 else 0][field][mode]
        return envelope(text, source)
    return metric(text)


def tables(text):
    """Return Markdown tables outside fenced code, with stable ordinals."""
    lines = text.splitlines(keepends=True)
    found, fenced, i = [], False, 0
    while i < len(lines):
        if lines[i].lstrip().startswith(('```', '~~~')):
            fenced = not fenced
        if not fenced and lines[i].startswith('|'):
            start = i
            while i < len(lines) and lines[i].startswith('|'):
                i += 1
            if i - start >= 3 and re.fullmatch(r'[|:\-\s]+', lines[start + 1]):
                found.append((start, i))
            continue
        i += 1
    return lines, found


def colorize_table(rows, rules, language, assets, resolver=None, repeated=False, eligible=None):
    out = [list(r) for r in rows]
    labels = LABELS if language == 'ru' else EN_LABELS

    def paint(locations, parts=False):
        if eligible and any(not eligible(r, c) for r, c, p in locations):
            return
        values = []
        for r, c, p in locations:
            text = rows[r][c] if p is None else rows[r][c].split(' / ')[p]
            values.append(resolver(r, c, text, p) if resolver else metric(text))
        # Missing repeat envelopes stay neutral. A point estimate alone cannot
        # prove that a measured lag exceeds run-to-run variation.
        unknown = repeated and any(not v.runs and v.interval is None for v in values)
        shaded = ([marker('equal', labels['equal'], asset_path=assets) + ' ' + str(v) for v in values]
                  if unknown else shade_values(values, language=language, asset_path=assets))
        for (r, c, p), value in zip(locations, shaded):
            if p is None:
                out[r][c] = str(value)
            else:
                chunks = out[r][c].split(' / ')
                chunks[p] = str(value)
                out[r][c] = ' / '.join(chunks)

    for mode, columns, *grouping in rules:
        if mode in ('row', 'paired', 'inside'):
            for r in range(len(rows)):
                if mode == 'row':
                    paint([(r, c, None) for c in columns])
                elif mode == 'inside':
                    for c in columns:
                        paint([(r, c, p) for p in range(len(rows[r][c].split(' / ')))])
                else:
                    for p in range(len(rows[r][columns[0]].split(' / '))):
                        paint([(r, c, p) for c in columns])
        else:
            groups = {}
            for r, row in enumerate(rows):
                groups.setdefault(tuple(row[c] for c in (grouping[0] if grouping else ())), []).append(r)
            for indexes in groups.values():
                for c in columns:
                    if mode == 'column':
                        paint([(r, c, None) for r in indexes])
                    else:
                        for p in range(len(rows[indexes[0]][c].split(' / '))):
                            paint([(r, c, p) for r in indexes])
    return out


def colorize_document(text, stem, language, path):
    original = text
    text = clean(text)
    text = re.sub(re.escape(BEGIN) + r'.*?' + re.escape(END) + r'\n*', '', text, flags=re.S)
    lines, blocks = tables(text)
    if len(blocks) != TABLE_COUNTS[stem]:
        raise ValueError(f'{path}: table inventory changed; review explicit schema')
    assets = Path(os.path.relpath(ROOT / 'docs/benchmarks/heatmap', path.parent)).as_posix()
    for ordinal, (start, end) in reversed(list(enumerate(blocks))):
        if ordinal not in SCHEMAS[stem]:
            continue
        rows = [[v.strip() for v in line.strip().strip('|').split('|')] for line in lines[start + 2:end]]
        repeated = stem != 'docs/benchmarks/optimization-methodology' and not (stem.endswith('MONO_COMPILER_STUDY') and ordinal == 0)
        resolver = lambda r, c, t, p: source_metric(stem, ordinal, r, c, t, p)
        eligible = lambda r, c: not (stem.endswith('/PHYSICS') and ordinal == 0 and 'legacy-compound' in rows[r][0])
        shaded = colorize_table(rows, SCHEMAS[stem][ordinal], language, assets, resolver, repeated, eligible)
        newline = '\r\n' if lines[start].endswith('\r\n') else '\n'
        lines[start + 2:end] = ['| ' + ' | '.join(row) + ' |' + newline for row in shaded]
    text = ''.join(lines)
    details = Path(os.path.relpath(ROOT / ('docs/benchmarks/COLORS.ru.md' if language == 'ru' else 'docs/benchmarks/COLORS.md'), path.parent)).as_posix()
    legend = compact_legend(language=language, asset_path=assets, details_link=details)
    note = ('Сравниваются только выбранные метрики одного сценария/окна/среды. Если в таблице и связанном сводном источнике нет диапазона независимых повторов, измеренные сравнения остаются серыми. Доли стадий и игровые счётчики не окрашены.' if language == 'ru' else
            'Only selected metrics within one scenario/window/environment are compared. Measured comparisons stay grey when neither the table nor its linked summary supplies independent-repeat envelopes. Stage shares and gameplay counters are not colored.')
    addition = BEGIN + '\n' + '\n'.join(legend) + '\n\n' + note + '\n' + END + '\n\n'
    heading = re.search(r'(?m)^# .*\n', text)
    text = text[:heading.end()] + '\n' + addition + text[heading.end():].lstrip('\r\n')
    if NUMBER.findall(clean(original)) != NUMBER.findall(clean(re.sub(re.escape(BEGIN) + r'.*?' + re.escape(END), '', text, flags=re.S))):
        # The legend contains its own threshold numbers, removed above.
        plain_original = re.sub(re.escape(BEGIN) + r'.*?' + re.escape(END), '', clean(original), flags=re.S)
        if NUMBER.findall(plain_original) != NUMBER.findall(clean(re.sub(re.escape(BEGIN) + r'.*?' + re.escape(END), '', text, flags=re.S))):
            raise AssertionError(f'{path}: numerical content changed')
    if FENCES.findall(original) != FENCES.findall(text):
        raise AssertionError(f'{path}: fenced examples changed')
    return text


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Verify without writing')
    args = parser.parse_args()
    changed = []
    for stem in SCHEMAS:
        for language in ('en', 'ru'):
            path = ROOT / (stem + ('.ru.md' if language == 'ru' else '.md'))
            with path.open(encoding='utf-8', newline='') as stream:
                original = stream.read()
            updated = colorize_document(original, stem, language, path)
            if updated != original:
                changed.append(path.relative_to(ROOT).as_posix())
                if not args.check:
                    with path.open('w', encoding='utf-8', newline='') as stream:
                        stream.write(updated)
    print(('Needs refresh: ' if args.check else 'Refreshed: ') + ', '.join(changed) if changed else 'Research comparison colors are current.')
    return int(bool(changed) and args.check)


if __name__ == '__main__':
    raise SystemExit(main())
