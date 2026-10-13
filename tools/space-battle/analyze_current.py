"""Validate and report the current 15-case SpaceBattle campaign, offline.

Tick CSVs are local verification inputs. Public evidence is the JSON matrix,
summary CSV, charts and manifest with source/binary/input hashes.
"""

import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import sys

from heatmap import Metric, PALETTE, EQUAL_COLOR, TimingPolicy, colors, compact_legend, shade_columns, shade_rows

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs/space-battle"
PROFILES = {"low": 0.5, "nominal": 1, "high": 2}
FRAMEWORKS = {"secs": "SECS", "lite": "LeoECSLite", "classic": "LeoECS Classic",
              "morpeh": "Morpeh", "static": "StaticEcs 1.2.01"}
RULES = {"Revision": "mounted-armament-v7", "TacticalRevision": "layered-squads-v3",
         "PhysicsRevision": "circle-kernel-v1", "DamageRevision": "layered-weapon-damage-v1"}
BUDGET = 1000 / 60


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_json(path):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, f"{path.name}: duplicate key {key}")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=pairs)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def close(actual, expected, label):
    require(math.isfinite(actual) and math.isclose(actual, expected, rel_tol=1e-8, abs_tol=1e-9),
            f"{label}: {actual} differs from {expected}")


def validate_stats(stats, values, label):
    ordered = sorted(values)
    count = len(ordered)
    require(count > 0 and all(math.isfinite(v) and v >= 0 for v in values), label + ": invalid timing")
    total = sum(ordered)
    expected = {"MeanMs": total / count,
                "StddevMs": math.sqrt(max(0, (sum(v*v for v in ordered) - total*total/count) / (count-1))) if count > 1 else 0,
                "MedianMs": ordered[math.ceil((count-1)*0.5)],
                "P95Ms": ordered[math.ceil((count-1)*0.95)],
                "P99Ms": ordered[math.ceil((count-1)*0.99)], "MaxMs": ordered[-1],
                "BudgetMisses": sum(v > BUDGET for v in values)}
    expected["BudgetMissPercent"] = expected["BudgetMisses"] * 100 / count
    for key, value in expected.items():
        close(stats[key], value, label + "." + key)


def canonical(run):
    return {"Checkpoints": run["Checkpoints"], "Checksum": run["Checksum"],
            "Counters": run["Counters"], "Tactics": run["Tactics"],
            "Windows": [{key: w[key] for key in ("StartTick", "EndTick", "Ships", "Projectiles",
                         "Debris", "Particles", "Turrets", "Counters")} for w in run["Windows"]]}


def validate(directory, verify_ticks):
    manifest = read_json(directory / "manifest.json")
    require(manifest["Completed"] is True, "Campaign is incomplete")
    require(manifest["Ticks"] == 108000 and manifest["WarmupTicks"] == 600,
            "Published campaign requires 108000 ticks and 600 separate warmup ticks")
    require(bool(manifest["SourceRevision"]) and bool(manifest["SourceHashes"]), "Missing source provenance")
    expected_files = {f"{p}-{f}.json" for p in PROFILES for f in FRAMEWORKS}
    actual_files = {p.name for p in directory.glob("*.json")} - {"manifest.json", "hardware.json"}
    require(actual_files == expected_files, "Expected exactly the complete 15-case JSON matrix")
    for name, expected in manifest["Files"].items():
        path = directory / name
        require(path.parent == directory and path.name == name, "Invalid manifest filename")
        if path.is_file():
            require(digest(path) == expected, "Input hash mismatch: " + name)
        else:
            require(not verify_ticks and name.endswith(".ticks.csv"), "Missing input: " + name)
    hardware = read_json(directory / "hardware.json")
    runs, conditions, provenance = {}, None, None
    for profile, churn in PROFILES.items():
        baseline = None
        for framework, name in FRAMEWORKS.items():
            filename = f"{profile}-{framework}.json"
            require(filename in manifest["Files"], "Unhashed result: " + filename)
            run = read_json(directory / filename)
            m = run["Metadata"]
            require(m["Framework"] == name, filename + ": wrong framework")
            for key, expected in {"Ticks": 108000, "WarmupTicks": 600, "TargetTickRate": 60,
                                  "SimulatedSeconds": 1800, "Realtime": False, "TieredCompilation": "0"}.items():
                require(m[key] == expected, filename + ": wrong " + key)
            close(m["BudgetMs"], BUDGET, filename + ".BudgetMs")
            options = dict(m["Options"])
            require(options.pop("ChurnMultiplier") == churn, filename + ": wrong churn")
            require(options["Seed"] == 7331 and options["ShipsPerTeam"] == 96 and options["CarriersPerTeam"] == 3,
                    filename + ": wrong fleet/seed")
            require(options["FleetComposition"] == 0 and options["Deployment"] == 0 and not options["EndlessReinforcements"],
                    filename + ": unexpected gameplay preset")
            require(all(m["GameplayRules"][key] == value for key, value in RULES.items()), filename + ": stale gameplay rules")
            condition = (m["Machine"], m["Runtime"], m["Os"], options, m["GameplayRules"], m["Collision"])
            if conditions is None:
                conditions, provenance = condition, run["Provenance"]
            require(condition == conditions, filename + ": mixed campaign conditions")
            require(run["Provenance"] == provenance, filename + ": mixed source/binary provenance")
            require(m["Machine"] == hardware["Machine"], filename + ": hardware host mismatch")
            require(len(run["Windows"]) == len(run["Checkpoints"]) == 30, filename + ": incomplete windows")
            for i, (window, point) in enumerate(zip(run["Windows"], run["Checkpoints"])):
                require((window["StartTick"], window["EndTick"], point["Tick"]) == (i*3600+1, (i+1)*3600, (i+1)*3600),
                        filename + ": noncontiguous windows/checkpoints")
                require(len(point["Checksum"]) == 16 and all(c in "0123456789ABCDEF" for c in point["Checksum"]),
                        filename + ": malformed checksum")
                require(window["Store"]["Entities"] == sum(window[k] for k in ("Ships", "Projectiles", "Debris", "Particles", "Turrets")),
                        filename + ": store census mismatch")
            require(run["Checkpoints"][-1]["Checksum"] == run["Checksum"] and run["Windows"][-1]["Counters"] == run["Counters"],
                    filename + ": final state mismatch")
            require(sum(w["AllocatedBytes"] for w in run["Windows"]) == run["AllocatedBytes"], filename + ": allocations mismatch")
            state = canonical(run)
            if baseline is None:
                baseline = state
            require(state == baseline, filename + ": port divergence")
            if verify_ticks:
                validate_ticks(directory, profile, framework, run)
            runs[profile, framework] = run
    return runs, manifest, hardware


def validate_ticks(directory, profile, framework, run):
    path = directory / f"{profile}-{framework}.ticks.csv"
    timings, allocations, gc_samples = [], [], []
    with path.open(encoding="utf-8-sig", newline="") as stream:
        for i, row in enumerate(csv.DictReader(stream), 1):
            require(int(row["tick"]) == i, path.name + ": missing/duplicate tick")
            close(float(row["simulationSeconds"]), i/60, path.name + ": simulation clock")
            timings.append(float(row["tickMs"]))
            allocations.append(int(row["allocatedBytes"]))
            require(allocations[-1] >= 0, path.name + ": negative allocation")
            gc_samples.append([int(row[f"gen{g}"]) for g in range(3)])
            require(all(v >= 0 for v in gc_samples[-1]), path.name + ": negative GC count")
            if len(gc_samples) > 1:
                require(all(a >= b for a, b in zip(gc_samples[-1], gc_samples[-2])), path.name + ": GC count decreased")
            if i % 3600 == 0:
                window = run["Windows"][i//3600-1]
                for key in ("ships", "projectiles", "debris", "particles", "turrets"):
                    require(int(row[key]) == window[key.title()], path.name + ": entity census mismatch")
                for key, counter in (("adds", "StructuralAdds"), ("removes", "StructuralRemoves")):
                    require(int(row[key]) == window["Counters"][counter], path.name + ": counter mismatch")
                validate_stats(window["Timing"], timings[-3600:], path.name + f" window {i//3600}")
                require(sum(allocations[-3600:]) == window["AllocatedBytes"], path.name + ": window allocations mismatch")
    require(len(timings) == 108000, path.name + ": incomplete CSV")
    validate_stats(run["Timing"], timings, path.name)
    require(sum(allocations) == run["AllocatedBytes"], path.name + ": total allocations mismatch")
    for i, window in enumerate(run["Windows"]):
        end = (i+1)*3600 - 1
        # CSV is sampled immediately after Step. Window statistics allocate
        # between that sample and Window.Gc; they can legitimately trigger GC.
        upper = gc_samples[end+1] if end+1 < len(gc_samples) else [
            a+b for a, b in zip(gc_samples[0], run["GcCollections"])]
        validate_gc_interval(gc_samples[end], window["Gc"], upper, path.name)


def validate_gc_interval(post_step, checkpoint, next_sample, label):
    require(len(post_step) == len(checkpoint) == len(next_sample) == 3 and
            all(a <= b <= c for a, b, c in zip(post_step, checkpoint, next_sample)),
            label + ": GC checkpoint outside sample interval")


def charts(runs, directory):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.patches import Patch
    plt.rcParams.update({"font.size": 10})
    policy = TimingPolicy.from_file(Path(__file__).with_name("heatmap_policy.json"))
    policy_keys = {"MeanMs": "tick.mean_ms", "P99Ms": "tick.p99_ms"}
    for metric in ("MeanMs", "P99Ms"):
        fig, axes = plt.subplots(1, 3, figsize=(13, 4), sharey=True)
        for axis, profile in zip(axes, PROFILES):
            values = [runs[profile, f]["Timing"][metric] for f in FRAMEWORKS]
            bars = axis.bar(range(5), values, color=colors(values, absolute_threshold=policy.threshold(profile, policy_keys[metric])))
            axis.bar_label(bars, labels=[f"{v:.4f}" for v in values], padding=3, fontsize=8)
            axis.margins(y=.15)
            axis.set_xticks(range(5), ["SECS", "Lite", "Classic", "Morpeh", "Static"], rotation=25)
            axis.set_title(profile)
            axis.set_ylabel("Step time (ms)")
            axis.grid(axis="y", alpha=.2)
        fig.suptitle(f"SpaceBattle: {metric} — one run per case, 108,000 ticks")
        palette = dict(PALETTE)
        fig.legend(handles=[Patch(facecolor=palette["best"], label="Lag ≤5%"),
                            Patch(facecolor=palette["middle"], label="Lag 15%"),
                            Patch(facecolor=palette["worst"], label="Lag ≥30%"),
                            Patch(facecolor=EQUAL_COLOR, label="Neutral / close group")],
                   loc="lower center", ncol=4, frameon=False, fontsize=9)
        fig.tight_layout(rect=(0, .10, 1, .93))
        fig.savefig(directory / f"{metric}.png", dpi=150)
        plt.close(fig)


def render(runs, manifest, hardware, directory, verify_ticks):
    relative = directory.relative_to(DOCS).as_posix()
    rows = [[p, f, f"{r['Timing']['MeanMs']:.4f}", f"{r['Timing']['P95Ms']:.4f}", f"{r['Timing']['P99Ms']:.4f}",
             f"{r['Timing']['MaxMs']:.4f}", str(r['Timing']['BudgetMisses']), f"{r['AllocatedBytes']/108000:.1f}"]
            for (p, f), r in runs.items()]
    with (directory / "summary.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["profile", "framework", "meanMs", "p95Ms", "p99Ms", "maxMs", "budgetMisses", "stepManagedBytesPerTick"])
        writer.writerows(rows)
    first = runs["nominal", "secs"]
    m = first["Metadata"]
    cpus = hardware["Cpu"] if isinstance(hardware["Cpu"], list) else [hardware["Cpu"]]
    cpu = ", ".join(c["Name"].strip() for c in cpus)
    policy = TimingPolicy.from_file(Path(__file__).with_name("heatmap_policy.json"))
    metric_keys = {2: "tick.mean_ms", 3: "tick.p95_ms", 4: "tick.p99_ms", 5: "tick.max_ms"}
    for ru in (False, True):
        name = "REPORT.ru.md" if ru else "REPORT.md"
        suffix = ".ru" if ru else ""
        language = "ru" if ru else "en"
        asset_path = "../benchmarks/heatmap"
        table_rows = []
        for row, ((profile, _), run) in zip(rows, runs.items()):
            table_rows.append([row[0], row[1], *[
                Metric(row[column], comparison=run["Timing"][key])
                for column, key in ((2, "MeanMs"), (3, "P95Ms"), (4, "P99Ms"), (5, "MaxMs"), (6, "BudgetMisses"))],
                Metric(row[7], comparison=run["AllocatedBytes"]/108000)])
        table_rows = shade_columns(table_rows, range(2, 8), group_by=(0,),
            absolute_thresholds=lambda row, column: policy.threshold(row[0], metric_keys[column]) if column in metric_keys else None,
            language=language, asset_path=asset_path)
        lines = [("[English](REPORT.md) | Русский" if ru else "English | [Русский](REPORT.ru.md)"), "",
                 "# " + ("Бенчмарки Space Battle" if ru else "Space Battle benchmarks"), "",
                 ("Актуальная кампания: " if ru else "Current campaign: ") + f"{manifest['StartedAt']} — {manifest['FinishedAt']}.", "",
                 ("Измерена текущая headless-симуляция на пяти ECS: " if ru else "Measured the current headless simulation on five ECS: ") +
                 ", ".join(f"`{v}`" for v in RULES.values()) + ".", "",
                 ("**Проверка пройдена:** 15 случаев, 1 620 000 измеренных тиков. В каждом профиле совпали все 30 контрольных точек, итоговые checksum, игровые и тактические счётчики и поминутная численность сущностей." if ru else
                  "**Validation passed:** 15 cases, 1,620,000 measured ticks. Within each profile all 30 checkpoints, final checksums, gameplay and tactical counters, and per-minute entity counts match."), "",
                 ("Тики CSV проверены локально: пересчитаны среднее, квантили, разброс, превышения бюджета и аллокации. Хеши исходных CSV сохранены в манифесте." if ru else
                  "Local tick CSV verification recomputed means, quantiles, spread, budget misses and allocations. Input CSV hashes are retained in the manifest.") if verify_ticks else
                 ("Проверена опубликованная JSON-матрица и её хеши; локальные CSV в этом запуске анализатора не проверялись." if ru else "Validated the published JSON matrix and hashes; local CSVs were not checked in this analyzer run."), "",
                 "## " + ("Как читать результаты" if ru else "Reading the results"), "",
                 ("Каждый случай — один независимый прогон из 108 000 тиков (30 минут симуляции) после отдельного прогрева на 600 тиков. Low/nominal/high меняют только churn: 0.5/1/2. Seed 7331, 96 эскортов и 3 носителя на команду, 60 тиков/с. Использованы headless defaults: равномерный состав, прежняя случайная расстановка, бесконечные подкрепления выключены. Графическая игра использует другой игровой preset; это видно в [настройках клиента](../../SpaceBattle.Game/README.ru.md)." if ru else
                  "Each case is one independent run of 108,000 ticks (30 simulated minutes), following a separate 600-tick warmup. Low/nominal/high change only churn: 0.5/1/2. Seed 7331, 96 escorts and 3 carriers per team, 60 ticks/s. Headless defaults use a uniform fleet, legacy random deployment and no endless reinforcements. The graphical game uses a different gameplay preset; see [client settings](../../SpaceBattle.Game/README.md)."), "",
                 ("Время Step включает игровые системы, физику и структурные commit. Рендеринг, начальное заполнение, snapshots и запись телеметрии не входят. Бюджет 16.67 ms относится к симуляции, а не к FPS. Показаны наблюдения этого компьютера; один прогон на случай не даёт устойчивого рейтинга и доверительного интервала. Аллокации — управляемые выделения потока внутри Step, без нативной памяти." if ru else
                  "Step time includes gameplay systems, physics and structural commits. Rendering, setup, snapshots and telemetry serialization are excluded. The 16.67 ms budget is a simulation budget, not FPS. These are observations on this machine; one run per case does not establish a stable ranking or confidence interval. Allocations count managed bytes on the Step thread and exclude native storage."), "",
                 *compact_legend(language, asset_path, f"../benchmarks/COLORS{suffix}.md"),
                 "## " + ("Время тика" if ru else "Tick time"), "",
                 "| Profile | ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick |",
                 "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
        lines += ["| " + " | ".join(map(str, row)) + " |" for row in table_rows]
        lines += ["", f"![Mean Step time]({relative}/MeanMs.png)", "", f"![P99 Step time]({relative}/P99Ms.png)", "",
                  "## " + ("Состояние симуляции и стадии" if ru else "Simulation state and stages"), "",
                  "| Profile | Checksum | Ships | Turrets | Shots | Hits | Created | Destroyed |",
                  "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |"]
        for p in PROFILES:
            r = runs[p, "secs"]
            w, c = r["Windows"][-1], r["Counters"]
            lines.append(f"| {p} | {r['Checksum']} | {w['Ships']} | {w['Turrets']} | {c['Shots']} | {c['Hits']} | {c['ShipsCreated']} | {c['ShipsDestroyed']} |")
        lines += ["", ("Среднее время стадий в nominal, ms. В каждой строке сравнивается одна стадия пяти портов; полные данные находятся в JSON." if ru else
                        "Mean stage time in nominal, ms. Each row compares the same stage across five ports; complete data is in JSON."), "",
                  "| Stage | SECS | Lite | Classic | Morpeh | StaticEcs |", "| --- | ---: | ---: | ---: | ---: | ---: |"]
        stages = []
        for i, stage in enumerate(first["Stages"]):
            stages.append([stage["Name"], *[Metric(f"{runs['nominal', f]['Stages'][i]['Timing']['MeanMs']:.4f}",
                 comparison=runs["nominal", f]["Stages"][i]["Timing"]["MeanMs"]) for f in FRAMEWORKS]])
        stages = shade_rows(stages, range(1, 6),
            absolute_thresholds=lambda row: policy.threshold("nominal", f"stage.{row[0]}.mean_ms"),
            language=language, asset_path=asset_path)
        lines += ["| " + " | ".join(map(str, row)) + " |" for row in stages]
        lines += ["", "## " + ("Происхождение данных" if ru else "Data provenance"), "",
                  f"- CPU: {cpu}; RAM: {hardware['TotalPhysicalMemoryBytes']/1024**3:.2f} GiB.",
                  f"- Runtime: {m['Runtime']}; OS: {m['Os']}; tiered compilation: `0`.",
                  f"- Source: `{manifest['SourceRevision']}`; source SHA256: [{relative}/manifest.json]({relative}/manifest.json).",
                  f"- [JSON]({relative}/), [summary.csv]({relative}/summary.csv), [hardware.json]({relative}/hardware.json).", "",
                  ("Манифест фиксирует исходники и хеши входных файлов; каждый JSON — точные upstream pins и SHA256 всех измеряемых DLL. Поле Provenance.Sources.SECS в старом headless host содержит описательную историческую строку; точная ревизия и исходники этой кампании задаются манифестом." if ru else
                   "The manifest records source and input hashes; each JSON records exact upstream pins and SHA256 hashes of all measured DLLs. Provenance.Sources.SECS in the headless host is a historical descriptive string; the campaign manifest supplies its exact revision and source hashes."), "",
                  "## " + ("Воспроизведение" if ru else "Reproduction"), "",
                  ("Из корня проекта, с .NET 8 и доступным кешем NuGet. Для графиков нужны Python и matplotlib. Запускайте измерения последовательно, без другой нагрузочной работы. Команда перезаписывает current; для собственных экспериментов задайте другой OutputDirectory внутри проекта." if ru else
                   "From the project root, with .NET 8 and available NuGet packages. Charts require Python and matplotlib. Run measurements sequentially without other heavy work. This command replaces current; use another OutputDirectory inside the project for experiments."), "",
                  "```powershell", '$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"', '$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"',
                  "$env:MSBuildEnableWorkloadResolver = 'false'",
                  "dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1 -p:GeneratePackageOnBuild=false",
                  "dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate",
                  "dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-adapters",
                  "dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-ports",
                  "./tools/space-battle/run-suite.ps1 -SkipBuild -OutputDirectory docs/space-battle/current",
                  "python tools/space-battle/analyze_current.py --verify-ticks", "```", "",
                  ("Без --verify-ticks анализатор проверяет опубликованные JSON и хеши и строит тот же отчёт без требования локальных CSV. Длинные CSV остаются локально; опубликованных JSON достаточно для сводных таблиц и графиков." if ru else
                   "Without --verify-ticks the analyzer verifies published JSON and hashes and builds the report without requiring local CSVs. Long tick CSVs stay local; published JSON is sufficient for summary tables and charts."), "",
                  "## " + ("История и связанные документы" if ru else "History and related documents"), "",
                  (f"- [Архив измерений 2026-10-11](REPORT_2026-10-11{suffix}.md): predictive-neutral-v1, Aether, старый frontend replay и состав сущностей. Его цифры не сравниваются с этой кампанией: изменились правила игры, физика и нагрузка." if ru else
                   "- [Archived 2026-10-11 campaign](REPORT_2026-10-11.md): predictive-neutral-v1, Aether, old frontend replay and entity composition. Its values are not compared with this campaign because gameplay rules, physics and workload changed."),
                  (f"- [Архитектура](ARCHITECTURE{suffix}.md), [физика](PHYSICS{suffix}.md), [оружие](ARMAMENT{suffix}.md), [тактика](TACTICS_V3{suffix}.md)." if ru else
                   "- [Architecture](ARCHITECTURE.md), [physics](PHYSICS.md), [armament](ARMAMENT.md), [tactics](TACTICS_V3.md)."),
                  (f"- [Микробенчмарки SECS](../../SECS.Benchmarks/README{suffix}.md): отдельная нагрузка без игровой логики." if ru else
                   "- [SECS microbenchmarks](../../SECS.Benchmarks/README.md): separate workloads without gameplay logic."), ""]
        (DOCS / name).write_text("\n".join(lines), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=DOCS / "current")
    parser.add_argument("--verify-ticks", action="store_true")
    args = parser.parse_args()
    directory = args.directory.resolve()
    require(directory.is_relative_to(DOCS), "Published results must stay inside docs/space-battle")
    runs, manifest, hardware = validate(directory, args.verify_ticks)
    charts(runs, directory)
    render(runs, manifest, hardware, directory, args.verify_ticks)
    print("PASS: 15 current cases; 1,620,000 ticks; canonical port parity; wrote bilingual reports, charts and summary")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, OSError, TypeError) as error:
        print("FAIL: " + str(error), file=sys.stderr)
        sys.exit(1)
