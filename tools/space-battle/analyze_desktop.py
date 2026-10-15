"""Compare desktop startup with warmed headless timing for the same battle."""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / 'docs/space-battle'
sys.path.insert(0, str(ROOT / 'tools'))
from benchmark_colors import Metric, shade_rows, compact_legend

STAGES = ('collect', 'ai', 'thermal-inventory', 'steering', 'weapons', 'carriers', 'physics', 'hits', 'lifecycle')
CASES = ('headless-warm-fast', 'headless-warm-paced', 'desktop-cold-fast-tiering0',
         'desktop-cold-fast-default', 'desktop-cold-paced-default')
LABELS = {
    'en': ('Headless warm / fast', 'Headless warm / 60 Hz', 'Desktop cold / tiering off', 'Desktop cold / default', 'Desktop cold / default 60 Hz'),
    'ru': ('Headless прогрет', 'Headless прогрет / 60 Hz', 'Desktop старт / tiering off', 'Desktop старт / default', 'Desktop старт / default 60 Hz'),
}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def mean(values):
    require(bool(values) and all(math.isfinite(v) and v >= 0 for v in values), 'Invalid samples')
    return math.fsum(values) / len(values)


def hud_mean(values, tick, capacity=600):
    require(1 <= tick <= len(values), 'HUD tick outside samples')
    return mean(values[max(0, tick-capacity):tick])


def table(headers, rows):
    return ['| ' + ' | '.join(headers) + ' |', '| --- | ' + ' | '.join(['---:']*(len(headers)-1)) + ' |',
            *['| ' + ' | '.join(map(str, row)) + ' |' for row in rows]]


def product_identity(manifest):
    """Use the recorded source paths to retain the original SECS study contract."""
    sources = manifest.get('SourceHashes', {})
    legacy = any(path.replace('\\', '/').startswith('SpaceBattle.SECS/') for path in sources)
    if legacy:
        return ('SECS', ('SpaceBattle.Core.dll', 'SpaceBattle.SECS.dll', 'SECS.dll'),
                ('CoreDllSha256', 'SecsAdapterDllSha256', 'SecsDllSha256'))
    return ('SKECS', ('SpaceBattle.Core.dll', 'SpaceBattle.SKECS.dll', 'SKECS.dll'),
            ('CoreDllSha256', 'SkecsAdapterDllSha256', 'SkecsDllSha256'))


def validate(directory):
    manifest = load(directory / 'manifest.json')
    _, dll_names, desktop_fields = product_identity(manifest)
    require(manifest['Completed'] is True and manifest['Ticks'] >= 1200, 'Incomplete desktop campaign')
    require(set(manifest['Files']) == {case + suffix for case in CASES for suffix in ('.json', '.ticks.csv')}, 'Incomplete input provenance')
    records, samples, checksum = {}, {}, None
    hashes, environment, gameplay = None, None, None
    host_hashes = {}
    config = None
    for case in CASES:
        record = load(directory / (case + '.json'))
        headless = case.startswith('headless')
        settings = record['Metadata']['Options'] if headless else record['Config']
        # Same complete config, not just the initial number of ships.
        if config is None:
            config = settings
        require(settings == config, 'Mixed BattleConfig: ' + case)
        final = record['Checksum'] if headless else record['FinalBattleChecksum']
        if checksum is None:
            checksum = final
        require(final == checksum, 'Canonical final-state divergence: ' + case)
        if headless:
            require(record['Metadata']['Ticks'] == manifest['Ticks'] and record['Metadata']['Preset'] == 'game', 'Wrong headless workload')
            require(record['Metadata']['WarmupTicks'] == 600 and record['Metadata']['TieredCompilation'] == '0', 'Wrong headless warmup/tiering')
            require(record['Metadata']['Realtime'] == (case == 'headless-warm-paced'), 'Wrong headless pacing')
            current_hashes = tuple(record['Provenance']['Binaries'][name] for name in dll_names)
            current_environment = (record['Metadata']['Runtime'], record['Metadata']['Machine'])
            host_hash = record['Provenance']['Binaries']['SpaceBattle.Headless.dll']
            final_counts = {key: record['Windows'][-1][key] for key in ('Ships', 'Projectiles', 'Debris', 'Particles', 'Turrets')}
        else:
            require(record['SampleFrames'] == manifest['Ticks'] and record['WarmupFrames'] == 0 and
                    record['FirstMeasuredTick'] == 1 and record['LastMeasuredTick'] == manifest['Ticks'], 'Wrong desktop tick range')
            require(record['GlErrors'] == [] and record['GlErrorOverflow'] == 0 and record['ValidationPass'] is True, 'Desktop GL validation failed')
            require(record['FrameRateCap'] == (60 if case.endswith('paced-default') else 0), 'Wrong desktop pacing')
            require(record['TieredCompilation'] == ('0' if case.endswith('tiering0') else None), 'Wrong desktop tiering')
            current_hashes = tuple(record[name] for name in desktop_fields)
            current_environment = (record['Runtime'], record['Machine'])
            host_hash = record['GameDllSha256']
            final_counts = {key: record['FinalAliveEntities'][key] for key in ('Ships', 'Projectiles', 'Debris', 'Particles', 'Turrets')}
        if hashes is None:
            hashes = current_hashes
        require(current_hashes == hashes, 'Loaded assembly mismatch: ' + case)
        host = 'headless' if headless else 'desktop'
        host_hashes.setdefault(host, host_hash)
        require(host_hash == host_hashes[host], 'Host assembly changed: ' + case)
        if environment is None:
            environment = current_environment
        require(environment == current_environment, 'Mixed runtime/host: ' + case)
        current_gameplay = (record['Counters'], record['Tactics'], final_counts)
        if gameplay is None:
            gameplay = current_gameplay
        require(gameplay == current_gameplay, 'Gameplay counter/population divergence: ' + case)
        with (directory / (case + '.ticks.csv')).open(encoding='utf-8-sig', newline='') as stream:
            rows = list(csv.DictReader(stream))
        require(len(rows) == manifest['Ticks'] and [int(row['tick']) for row in rows] == list(range(1, manifest['Ticks']+1)), 'Wrong CSV trajectory')
        hud_key = 'hudMs' if headless else 'hud_ms'
        hud = [float(row[hud_key]) for row in rows]
        stats = record['HudTiming'] if headless else record['HudTickMs']
        require(math.isclose(mean(hud), stats['MeanMs'] if headless else stats['Mean'], rel_tol=1e-8, abs_tol=1e-9), 'HUD mean mismatch')
        require(len(record['HudWindows']) == manifest['Ticks']//600, 'Incomplete HUD windows')
        for i, window in enumerate(record['HudWindows']):
            begin, end = window['StartTick']-1, window['EndTick']
            require(begin == i*600 and end == min((i+1)*600, manifest['Ticks']), 'Wrong HUD boundaries')
            window_mean = window['Timing']['MeanMs'] if headless else window['Timing']['Mean']
            require(math.isclose(mean(hud[begin:end]), window_mean, rel_tol=1e-8, abs_tol=1e-9), 'HUD window mismatch')
            require(tuple(stage['Name'] for stage in window['Stages']) == STAGES, 'Missing desktop stages')
            for stage in window['Stages']:
                published = stage['Timing']['MeanMs'] if headless else stage['Timing']['Mean']
                measured = mean([float(row[stage['Name']]) for row in rows[begin:end]])
                require(math.isclose(measured, published, rel_tol=1e-8, abs_tol=1e-9), 'Stage window mismatch')
        samples[case] = {'hud': hud, **{stage: [float(row[stage]) for row in rows] for stage in STAGES}}
        records[case] = record
    for name, expected in manifest['Files'].items():
        path = directory / name
        require(path.parent == directory and hashlib.sha256(path.read_bytes()).hexdigest().upper() == expected, 'Input hash mismatch: ' + name)
    return manifest, records, samples, checksum


def render(directory):
    manifest, records, samples, checksum = validate(directory)
    product, _, _ = product_identity(manifest)
    relative = directory.relative_to(DOCS).as_posix()
    for ru in (False, True):
        suffix, language = ('.ru', 'ru') if ru else ('', 'en')
        lines = [('[English](DESKTOP_TIMING.md) | Русский' if ru else 'English | [Русский](DESKTOP_TIMING.ru.md)'), '',
                 '# ' + ('Почему HUD Mean отличается от headless' if ru else 'Why HUD Mean differs from headless'), '',
                 ((f"В этом свежем прогоне Release-клиент с tiering off показал {hud_mean(samples['desktop-cold-fast-tiering0']['hud'], 30):.3f} ms Mean к тику 30 и {hud_mean(samples['desktop-cold-fast-tiering0']['hud'], 1200):.3f} ms в окне 601–1200. С обычным runtime — {hud_mean(samples['desktop-cold-fast-default']['hud'], 10):.3f} ms к тику 10 и {hud_mean(samples['desktop-cold-fast-default']['hud'], 1200):.3f} ms в окне 601–1200. Высокий Mean на старте воспроизведён; это не средняя цена прогретого боя." if ru else
                   f"In this fresh Release run, tiering off produced {hud_mean(samples['desktop-cold-fast-tiering0']['hud'], 30):.3f} ms Mean at tick 30 and {hud_mean(samples['desktop-cold-fast-tiering0']['hud'], 1200):.3f} ms for ticks 601–1200. Default runtime produced {hud_mean(samples['desktop-cold-fast-default']['hud'], 10):.3f} ms at tick 10 and {hud_mean(samples['desktop-cold-fast-default']['hud'], 1200):.3f} ms for ticks 601–1200. High startup Mean was reproduced; it is not the steady warmed battle cost.")), '',
                 ((f'Пять независимых Release-процессов повторяют один и тот же nominal game-бой: seed 7331, 96 эскортов и 3 носителя на сторону, LightSwarm, Tactical, подкрепления включены. Core, {product} и адаптер имеют одинаковые DLL-хеши; итоговые checksum, счётчики и численность совпали.') if ru else
                  f'Five independent Release processes replay the same nominal game battle: seed 7331, 96 escorts and 3 carriers per team, LightSwarm, Tactical, reinforcements enabled. Core, {product} and adapter DLL hashes match; final checksums, counters and populations match.'), '',
                 f"Checksum: `{checksum}`. [Manifest]({relative}/manifest.json).", '',
                 ('Headless отдельно прогревает 600 тиков на другом экземпляре боя. Desktop начинается без прогрева Step: стартовые JIT и ленивые инициализации входят в его таймер. Default означает отсутствие переопределения DOTNET_TieredCompilation; tiering0 — принудительно выключенную tiered compilation. Paced desktop ограничен 60 кадрами/с, выполняет ровно один Step на кадр, VSync выключен; это контролируемый replay, а не замер FPS обычного накопителя игры. Все процессы запускаются последовательно.' if ru else
                  'Headless separately warms 600 ticks in another battle. Desktop records Step from cold startup, including initial JIT and lazy initialization. Default removes the DOTNET_TieredCompilation override; tiering0 explicitly disables tiered compilation. Paced desktop caps rendering at 60 frames/s, runs exactly one Step/frame and disables VSync: a controlled replay, not live accumulator FPS. Processes run sequentially.'), '',
                 ('Mean HUD усредняет min(600, число выполненных тиков) последних тиков. Поэтому первые 10/30/60 тиков — ещё короткая стартовая выборка; после 600 это скользящее окно. Capture, рендер и ожидание следующего кадра не входят в Battle.LastTickMs. Их нагрузка на процесс может менять цену последующего Step.' if ru else
                  'HUD Mean averages the last min(600, completed tick count) samples. The first 10/30/60 ticks are short startup samples; after 600 it is a rolling window. Capture, rendering and frame waiting are outside Battle.LastTickMs. Their process load can affect the following Step.'), '',
                 *compact_legend(language, '../benchmarks/heatmap', '../benchmarks/COLORS'+suffix+'.md'),
                 '## ' + ('Mean HUD в момент тика, ms' if ru else 'HUD Mean at tick, ms'), '']
        rows = []
        for tick in (10, 30, 60, 120, 600, 1200):
            rows.append([tick, *[Metric(f'{hud_mean(samples[case]["hud"], tick):.4f}', comparison=hud_mean(samples[case]['hud'], tick)) for case in CASES]])
        lines += table(['Tick', *LABELS[language]], shade_rows(rows, range(1,6), language=language, asset_path='../benchmarks/heatmap'))
        for begin, end in ((0,600),(600,1200)):
            lines += ['', f'## Stage mean: {begin+1}–{end}, ms', '']
            rows = [[stage, *[Metric(f'{mean(samples[case][stage][begin:end]):.4f}', comparison=mean(samples[case][stage][begin:end])) for case in CASES]] for stage in STAGES]
            rows += [['Total HUD Step', *[Metric(f'{mean(samples[case]["hud"][begin:end]):.4f}', comparison=mean(samples[case]['hud'][begin:end])) for case in CASES]]]
            lines += table(['Stage', *LABELS[language]], shade_rows(rows, range(1,6), language=language, asset_path='../benchmarks/heatmap'))
        lines += ['', ('Это один запуск каждой конфигурации; цвет показывает наблюдаемую разницу на одинаковом диапазоне тиков. Для содержательного сравнения должны совпадать настройки боя, runtime, диапазон тиков, прогрев и границы таймера. В desktop Step входит время с первого тика без прогрева; в headless перед замером отдельно выполняются 600 тиков прогрева. Числа этого запуска не устанавливают причину устойчивого значения около 4 ms в другой фазе боя. Поле RawCsvPath хранит имя CSV без локального пути пользователя. [Полный игровой прогон и все пять ECS](GAME_REPORT.ru.md) содержат 30 минут симуляции и последующие окна HUD.' if ru else
                       'This is one run per configuration; colour shows observed differences at matched tick ranges. A meaningful comparison needs the same battle settings, runtime, tick range, warmup and timer boundaries. Desktop Step timing starts at the first tick without warmup; headless runs a separate 600-tick warmup before measurement. These results do not establish the cause of a sustained value near 4 ms in another battle phase. RawCsvPath records the CSV filename without a local user path. The [full game run on all five ECS](GAME_REPORT.md) contains 30 simulated minutes and later HUD windows.'), '']
        (DOCS / ('DESKTOP_TIMING'+suffix+'.md')).write_text('\n'.join(map(str, lines)), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-dir', type=Path, default=ROOT / 'docs/space-battle/desktop-current')
    directory = parser.parse_args().data_dir.resolve()
    require(directory.is_relative_to(ROOT / 'docs/space-battle'), 'Published results must stay inside docs/space-battle')
    render(directory)
    print('PASS: matched desktop/headless startup, CSV HUD/stage windows and input hashes')
