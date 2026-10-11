"""Validate the separate logical composition replay and generate report sections."""

import csv
import hashlib
import math
from pathlib import Path
from report_locale import catalogue_body

NAMES = ("Identity Transform Motion Hull Shield Heat Brain Weapon SecondaryWeapon Collider PhysicsLink "
         "CombatState Carrier Docked Overheated InCombat Boosting ShieldBroken Projectile Debris Particle Lifetime Inventory").split()
GROUPS = "Escorts Carriers Projectiles Debris Particles Unclassified".split()
GROUP_LABELS = dict(Escorts="Эскорты", Carriers="Авианосцы", Projectiles="Снаряды", Debris="Обломки",
                    Particles="Частицы", Unclassified="Без семейства")
BASE_SHIP = "Identity Hull Shield Heat Brain Weapon Collider PhysicsLink CombatState Inventory".split()
TAGS = "Docked Overheated InCombat Boosting ShieldBroken".split()


def composition_section(docs, runs, read_json, require, table):
    profiles = ("low", "nominal", "high")
    measured = {}
    for profile in profiles:
        path = docs / "composition" / (profile + ".json")
        r = read_json(path)
        original = runs[profile, "secs"]
        ticks = original["Metadata"]["Ticks"]
        require(r["SchemaVersion"] == 1 and r["Profile"] == profile and r["Ticks"] == ticks,
                "composition schema/profile/duration mismatch")
        require(r["Config"] == original["Metadata"]["Options"], "composition options mismatch")
        source = docs / r["SourceResult"]
        require(source == docs / "results" / (profile + "-secs.json"), "composition source mismatch")
        require(r["SourceSha256"] == hashlib.sha256(source.read_bytes()).hexdigest().upper(), "composition source hash mismatch")
        for name, digest in r["BinaryHashes"].items():
            require(digest == original["Provenance"]["Binaries"][name], "composition measured DLL mismatch: " + name)
        require(set(r["BinaryHashes"]) == {"SpaceBattle.Core.dll", "SpaceBattle.SECS.dll", "SECS.dll", "Aether.Physics2D.dll"},
                "composition missing DLL provenance")
        observer = docs.parents[1] / "tools/space-battle/composition/bin/Release/net8.0/Composition.dll"
        require(r["ObserverSha256"] == hashlib.sha256(observer.read_bytes()).hexdigest().upper(), "composition observer hash mismatch")
        require(r["Checksum"] == original["Checksum"], "composition final checksum mismatch")
        expected_points = [dict(p, NativeComponentCountsVerified=True) for p in original["Checkpoints"]]
        require(r["Checkpoints"] == expected_points, "composition native census/checkpoints mismatch")
        require([c["Name"] for c in r["Components"]] == NAMES, "composition component catalogue mismatch")
        require([g["Name"] for g in r["Groups"]] == GROUPS, "composition groups mismatch")
        csv_path = path.with_suffix(".ticks.csv")
        require(r["TickCsvSha256"] == hashlib.sha256(csv_path.read_bytes()).hexdigest().upper(), "composition CSV hash mismatch")
        names = ["tick", "entities", "components", *NAMES, *(n + "Entities" for n in GROUPS),
                 *(n + "Components" for n in GROUPS), *("CountWith" + str(n) for n in range(24))]
        sums = dict.fromkeys(names[1:], 0)
        minima = dict.fromkeys(names[1:], math.inf)
        maxima = dict.fromkeys(names[1:], 0)
        mean_ratios = 0.0
        last = None
        with csv_path.open(encoding="utf-8", newline="") as stream, source.with_suffix(".ticks.csv").open(encoding="utf-8", newline="") as old:
            rows, old_rows = csv.DictReader(stream), csv.DictReader(old)
            require(rows.fieldnames == names, "composition CSV columns mismatch")
            for tick, row in enumerate(rows, 1):
                v = {k: int(row[k]) for k in names}
                o = next(old_rows)
                require(v["tick"] == tick and all(x >= 0 for x in v.values()), "composition CSV tick/value mismatch")
                require(v["entities"] > 0 and sum(v[n] for n in NAMES) == v["components"], "composition component total mismatch")
                require(v["components"] == int(o["adds"]) - int(o["removes"]), "composition structural conservation mismatch")
                ships = int(o["ships"])
                expected = [ships - v["Carrier"], v["Carrier"], int(o["projectiles"]), int(o["debris"]), int(o["particles"]), 0]
                require([v[g + "Entities"] for g in GROUPS] == expected and sum(expected) == v["entities"], "composition entity trace mismatch")
                require(v["Transform"] == v["Motion"] == v["entities"] and v["Lifetime"] == 0, "common/unused component mismatch")
                require(all(v[n] == ships for n in BASE_SHIP), "ship base count mismatch")
                require((v["Projectile"], v["Debris"], v["Particle"]) == tuple(expected[2:5]), "transient count mismatch")
                require(sum(v[g + "Components"] for g in GROUPS) == v["components"], "group component conservation mismatch")
                require(all(v[g + "Components"] == 3 * v[g + "Entities"] for g in GROUPS[2:]), "transient composition mismatch")
                require(sum(v[g + "Components"] for g in GROUPS[:2]) == 12 * ships + v["SecondaryWeapon"] + v["Carrier"] + sum(v[n] for n in TAGS),
                        "ship composition mismatch")
                hist = [v["CountWith" + str(n)] for n in range(24)]
                require(sum(hist) == v["entities"] and sum(n * h for n, h in enumerate(hist)) == v["components"], "histogram conservation mismatch")
                for k in sums:
                    sums[k] += v[k]
                    minima[k] = min(minima[k], v[k])
                    maxima[k] = max(maxima[k], v[k])
                mean_ratios += v["components"] / v["entities"]
                last = v
            require(tick == ticks and next(old_rows, None) is None, "composition CSV length mismatch")
        def equal(actual, expected, label):
            require(math.isclose(actual, expected, rel_tol=1e-12, abs_tol=1e-9), "composition aggregate mismatch: " + label)
        s = r["Summary"]
        for key, expected in dict(EntityTicks=sums["entities"], ComponentTicks=sums["components"],
                                  MeanEntities=sums["entities"] / ticks, MeanComponents=sums["components"] / ticks,
                                  MeanComponentsPerEntity=sums["components"] / sums["entities"], MeanOfPerTickMeans=mean_ratios / ticks,
                                  MinEntities=minima["entities"], MaxEntities=maxima["entities"],
                                  MinComponents=minima["components"], MaxComponents=maxima["components"]).items():
            equal(s[key], expected, key)
        for c in r["Components"]:
            n = c["Name"]
            for key, expected in dict(MeanActive=sums[n] / ticks, EntityTicks=sums[n], MinActive=minima[n], MaxActive=maxima[n],
                                      Final=last[n], EntityPresencePercent=100 * sums[n] / sums["entities"]).items():
                equal(c[key], expected, n + "." + key)
            require(c["Initial"] + c["AddsDuringSimulation"] - c["RemovesDuringSimulation"] == c["Final"], "per-type structural conservation mismatch")
            require(all(type(c[k]) is int and c[k] >= 0 for k in ("LogicalPayloadBytes", "Initial", "AddsDuringSimulation", "RemovesDuringSimulation", "GetCalls", "HasCalls", "QueryBindings")), "invalid component counters")
        require(sum(c["Initial"] + c["AddsDuringSimulation"] for c in r["Components"]) == original["Counters"]["StructuralAdds"], "total additions mismatch")
        require(sum(c["RemovesDuringSimulation"] for c in r["Components"]) == original["Counters"]["StructuralRemoves"], "total removals mismatch")
        for g in r["Groups"]:
            e, c = g["Name"] + "Entities", g["Name"] + "Components"
            for key, expected in dict(EntityTicks=sums[e], ComponentTicks=sums[c], MeanEntities=sums[e] / ticks,
                                      MinEntities=minima[e], MaxEntities=maxima[e], EntitySharePercent=100 * sums[e] / sums["entities"]).items():
                equal(g[key], expected, g["Name"] + "." + key)
            if sums[e]:
                equal(g["MeanComponentsPerEntity"], sums[c] / sums[e], g["Name"] + ".mean components")
            else:
                require(g["MeanComponentsPerEntity"] is None, "empty group mean must be null")
        h = {x["Components"]: x for x in r["ComponentCountHistogram"]}
        require(set(h) == {n for n in range(24) if sums["CountWith" + str(n)]}, "histogram nonempty bins mismatch")
        for n, value in h.items():
            equal(value["EntityTicks"], sums["CountWith" + str(n)], "histogram count")
            equal(value["Percent"], 100 * value["EntityTicks"] / sums["entities"], "histogram share")
        signatures = r["Compositions"]
        require(len(signatures) == s["ObservedCompositions"], "composition signature count mismatch")
        require(sum(x["EntityTicks"] for x in signatures) == sums["entities"] and
                sum(x["EntityTicks"] * x["ComponentCount"] for x in signatures) == sums["components"], "signature conservation mismatch")
        require(len({tuple(x["Components"]) for x in signatures}) == len(signatures), "duplicate signature")
        for n in NAMES:
            require(sum(x["EntityTicks"] for x in signatures if n in x["Components"]) == sums[n], "signature type conservation mismatch: " + n)
        for n in h:
            require(sum(x["EntityTicks"] for x in signatures if x["ComponentCount"] == n) == h[n]["EntityTicks"], "signature histogram mismatch")
        for x in signatures:
            require(x["ComponentCount"] == len(x["Components"]) and all(n in NAMES for n in x["Components"]), "invalid signature")
            equal(x["Percent"], 100 * x["EntityTicks"] / sums["entities"], "signature share")
        measured[profile] = r

    lines = ["## Системы и компоненты", "",
             "Ниже приведён каталог текущего сценария. Полная самостоятельная версия: [SYSTEMS.md](SYSTEMS.md). "
             "Числа состава относятся к логическим компонентам игры, без приватного LifetimeMarker адаптеров.", ""]
    lines += catalogue_body(docs, "ru").splitlines()
    lines += ["", "### Среднее время стадий, nominal", "",
              "Значения в ms из исходных performance-запусков, не из диагностического observer. "
              "Стадии включают указанные выше обходы, вычисления и структурные последствия; "
              "это не изолированная стоимость одной ECS-операции.", ""]
    frameworks = ("secs", "lite", "classic", "morpeh", "static")
    stage_times = {f: {s["Name"]: s["Timing"]["MeanMs"] for s in runs["nominal", f]["Stages"]} for f in frameworks}
    lines += table(["Стадия", "SECS ms", "Lite ms", "Classic ms", "Morpeh ms", "Static ms", "Доля SECS Step %"],
                   [(s["Name"], *(f"{stage_times[f][s['Name']]:.5f}" for f in frameworks),
                     f"{100 * stage_times['secs'][s['Name']] / runs['nominal', 'secs']['Timing']['MeanMs']:.2f}")
                    for s in runs["nominal", "secs"]["Stages"]])
    lines += ["", "## Измеренный состав сущностей", "",
              "Для каждого профиля выполнен отдельный диагностический SECS replay всех 108000 тиков, "
              "без изменения ранее измеренных Core/SECS/Aether DLL: их SHA256 совпадает с исходными JSON. "
              "На каждом тике совпадают численности четырёх семейств и StructuralAdds/Removes исходного CSV; "
              "все 30 минутных checksums и итоговые Counters совпадают. На tick 0 и каждые 3600 тиков "
              "наблюдатель дополнительно сверяет численности всех 23 типов независимыми native-запросами. "
              "Это подтверждает состав данного replay, но не заменяет проверку каждой сущности во всех пяти адаптерах.", "",
              "Состав снимается после Step на каждом tick 1..108000. Начальное состояние tick 0, прогрев, Capture, "
              "Validate и Checksum не входят в средние и API-счётчики. Единица entity-tick: одна живая сущность "
              "на одном таком тике. Среднее компонентов на сущность = сумма экземпляров компонентов по тикам / "
              "сумма живых сущностей по тикам; это взвешенное по времени жизни среднее, не среднее по уникальным "
              "созданным сущностям. Краткоживущие объекты, успевшие создаться и исчезнуть внутри одного Step, "
              "учтены в структурных операциях/API, но не в end-of-tick популяции. Отдельно дано невзвешенное "
              "среднее отношений каждого тика. Поэтому определения не нужно смешивать.", ""]
    lines += table(["Профиль", "Среднее живых сущностей", "Min / max сущностей", "Среднее экземпляров компонентов",
                    "Компонентов / сущность", "Среднее отношений по тикам", "Наблюдаемых составов"],
                   [(p, f"{r['Summary']['MeanEntities']:.2f}", f"{r['Summary']['MinEntities']} / {r['Summary']['MaxEntities']}",
                     f"{r['Summary']['MeanComponents']:.2f}", f"{r['Summary']['MeanComponentsPerEntity']:.4f}",
                     f"{r['Summary']['MeanOfPerTickMeans']:.4f}", r['Summary']['ObservedCompositions']) for p, r in measured.items()])
    lines += ["", "### По семействам", ""]
    lines += table(["Семейство", "low среднее сущностей", "low комп./сущ.", "nominal среднее сущностей", "nominal комп./сущ.",
                    "high среднее сущностей", "high комп./сущ."],
                   [(GROUP_LABELS[g], *(v for p in profiles for item in measured[p]["Groups"] if item["Name"] == g
                                       for v in (f"{item['MeanEntities']:.2f}", f"{item['MeanComponentsPerEntity']:.4f}"))) for g in GROUPS[:-1]])
    lines += ["", "У снарядов, обломков и частиц всегда по три компонента. Именно многочисленные временные "
              "объекты снижают общее среднее; число компонентов корабля нельзя оценивать по среднему всей ECS. "
              "Докованные корабли считаются живыми, даже когда они исключены из списка Ships визуального BattleFrame.", "",
              "### Распределение числа компонентов", ""]
    bins = sorted({x["Components"] for r in measured.values() for x in r["ComponentCountHistogram"]})
    lines += table(["Компонентов на сущности", "low доля entity-ticks %", "nominal %", "high %"],
                   [(n, *(f"{next((x['Percent'] for x in measured[p]['ComponentCountHistogram'] if x['Components'] == n), 0):.4f}"
                          for p in profiles)) for n in bins])
    lines += ["", "### Самые распространённые компоненты", "",
              "Это распространённость в живой популяции, не число обращений. Для каждого типа указаны "
              "среднее активных экземпляров и доля entity-ticks с этим компонентом. Доли разных типов "
              "не обязаны давать 100%: одна сущность содержит несколько компонентов. Рейтинг отсортирован "
              "по nominal. Payload - sizeof(T) логического unmanaged значения; без выравнивания хранилища, "
              "чанков, sparse maps, identity bridge, физики и других накладных расходов.", ""]
    components = {p: {c["Name"]: c for c in r["Components"]} for p, r in measured.items()}
    order = sorted(NAMES, key=lambda n: (-components["nominal"][n]["MeanActive"], NAMES.index(n)))
    lines += table(["Компонент", "Payload bytes", "low среднее / доля %", "nominal среднее / доля %", "high среднее / доля %"],
                   [(f"`{n}`", components["nominal"][n]["LogicalPayloadBytes"],
                     *(f"{components[p][n]['MeanActive']:.2f} / {components[p][n]['EntityPresencePercent']:.3f}" for p in profiles)) for n in order])
    lines += ["", "Transform и Motion присутствуют у всех сущностей. Lifetime во всех end-of-tick "
              "снимках отсутствует: срок жизни временных объектов хранится в ExpiresTick соответствующего "
              "Projectile/Debris/Particle. InventoryItem/InventoryItems и SecondaryWeapon.Value не добавляют "
              "отдельных компонентов на сущность.", "", "### Фактическая активность API", "",
              "Таблица ниже ранжирует nominal по GetCalls + QueryBindings (выдачам ref), а не по наносекундам. "
              "GetCalls - вызовы IEcsStore.Get<T>; HasCalls - проверки наличия. QueryBindings - количество "
              "раз, когда тип передан процессору нативного ForEach по ref: даже если его поля не читались. "
              "Чтения/записи через уже полученную ref, копии значений, внутренние обращения самого адаптера "
              "и обращения рендера здесь не считаются. Одна выдача может обслужить много операций с полями. "
              "Это логическая активность gameplay API, не аппаратные обращения к памяти и не отдельный "
              "бенчмарк Get/Has/Query. Диагностический observer имеет собственные накладные расходы; "
              "его wall time/LastStageMs не используются в рейтинге производительности.", ""]
    hot = sorted(NAMES, key=lambda n: -(components["nominal"][n]["GetCalls"] + components["nominal"][n]["QueryBindings"]))
    lines += table(["Компонент, nominal", "Get, млн", "Query bindings, млн", "Has, млн", "Get + bindings, млн"],
                   [(f"`{n}`", f"{components['nominal'][n]['GetCalls'] / 1e6:.3f}",
                     f"{components['nominal'][n]['QueryBindings'] / 1e6:.3f}", f"{components['nominal'][n]['HasCalls'] / 1e6:.3f}",
                     f"{(components['nominal'][n]['GetCalls'] + components['nominal'][n]['QueryBindings']) / 1e6:.3f}") for n in hot])
    lines += ["", "### Структурная активность по типам", "",
              "Значения Add / Remove за tick 1..108000, без начальной композиции. Destroy включает "
              "удаление всех логических компонентов сущности; повторный Set уже существующего типа "
              "не является Add. Сумма Add плюс 2484 компонента начальных 198 кораблей совпадает с "
              "StructuralAdds, сумма Remove - с StructuralRemoves исходного запуска.", ""]
    churn_order = sorted(NAMES, key=lambda n: -(components["nominal"][n]["AddsDuringSimulation"] + components["nominal"][n]["RemovesDuringSimulation"]))
    lines += table(["Компонент", "low Add / Remove", "nominal Add / Remove", "high Add / Remove"],
                   [(f"`{n}`", *(f"{components[p][n]['AddsDuringSimulation']} / {components[p][n]['RemovesDuringSimulation']}" for p in profiles)) for n in churn_order])
    lines += ["", "### Частые составы, nominal", "",
              "База корабля - 12 компонентов из каталога выше. Показаны десять наиболее частых полных "
              "наборов по entity-ticks, включая временные объекты; это не количество архетипов, "
              "созданных внутри конкретного ECS. Редкие наборы и все доли сохранены в JSON.", ""]
    base = {"Identity", "Transform", "Motion", *BASE_SHIP}
    def signature(names):
        values = set(names)
        return "База корабля" + (" + " + ", ".join(f"`{n}`" for n in names if n not in base) if values - base else "") if base <= values else " + ".join(f"`{n}`" for n in names)
    lines += table(["Полный состав", "Компонентов", "Доля entity-ticks %"],
                   [(signature(x["Components"]), x["ComponentCount"], f"{x['Percent']:.4f}") for x in measured["nominal"]["Compositions"][:10]])
    lines += ["", "### Графики состава", "",
              "![Средняя численность семейств](charts/composition-families.png)", "",
              "![Распределение числа компонентов](charts/composition-histogram.png)", "",
              "![Распространённость компонентов](charts/composition-presence.png)", "",
              "### Источники диагностики", ""]
    lines += table(["Профиль", "JSON", "Полный CSV по тикам", "JSON SHA256"],
                   [(p, f"[{p}.json](composition/{p}.json)", f"[{p}.ticks.csv](composition/{p}.ticks.csv)",
                     hashlib.sha256((docs / "composition" / (p + ".json")).read_bytes()).hexdigest()) for p in profiles])
    lines += ["", "Данные состава напрямую измерены в SECS diagnostic replay, не в новых пяти замерах "
              "скорости. Сходство gameplay trace остальных четырёх портов подтверждено исходным набором "
              "checkpoints/Counters; отдельная перепись их каждого компонента не выполнялась. Для Classic, "
              "Lite и Morpeh число нативных компонентов дополнительно включает один LifetimeMarker на "
              "живую сущность. Это не меняет приведённый общий логический состав.", ""]
    composition_charts(docs, measured)
    return lines


def composition_charts(docs: Path, measured):
    import matplotlib.pyplot as plt
    import numpy as np
    directory = docs / "charts"
    directory.mkdir(exist_ok=True)
    colors = ["#287b8e", "#ce6553", "#768c36", "#94659c", "#cfaa35"]
    fig, ax = plt.subplots(figsize=(10, 4.8))
    bottom = np.zeros(3)
    for j, name in enumerate(GROUPS[:-1]):
        values = [next(g["MeanEntities"] for g in r["Groups"] if g["Name"] == name) for r in measured.values()]
        ax.bar(list(measured), values, bottom=bottom, color=colors[j], label=name)
        bottom += values
    ax.set_ylabel("Mean live entities (all end-of-tick samples)")
    ax.set_title("Population mix by profile")
    ax.legend(loc="upper left", bbox_to_anchor=(1.02, 1))
    fig.tight_layout(); fig.savefig(directory / "composition-families.png", dpi=150); plt.close(fig)
    bins = sorted({h["Components"] for r in measured.values() for h in r["ComponentCountHistogram"]})
    fig, axes = plt.subplots(1, 2, figsize=(13, 4.8))
    for ax, selected, ships_only in ((axes[0], bins, False), (axes[1], [n for n in bins if n >= 12], True)):
        for j, (p, r) in enumerate(measured.items()):
            denominator = sum(g["EntityTicks"] for g in r["Groups"][:2]) if ships_only else r["Summary"]["EntityTicks"]
            shares = [100 * next((h["EntityTicks"] for h in r["ComponentCountHistogram"] if h["Components"] == n), 0) / denominator for n in selected]
            ax.bar(np.arange(len(selected)) + (j - 1) * .25, shares, .25, label=p, color=colors[j])
        ax.set_xticks(np.arange(len(selected)), selected)
        ax.set_ylabel("Share of ship entity-ticks (%)" if ships_only else "Share of all entity-ticks (%)")
        ax.set_xlabel("Logical components per entity")
        ax.set_title("Ships only" if ships_only else "Entire population")
    axes[1].legend(loc="upper left", bbox_to_anchor=(1.02, 1))
    fig.tight_layout(); fig.savefig(directory / "composition-histogram.png", dpi=150); plt.close(fig)
    nominal = {c["Name"]: c for c in measured["nominal"]["Components"]}
    names = sorted(NAMES, key=lambda n: nominal[n]["EntityPresencePercent"], reverse=True)
    fig, ax = plt.subplots(figsize=(10, 8))
    for j, (p, r) in enumerate(measured.items()):
        values = {c["Name"]: c["EntityPresencePercent"] for c in r["Components"]}
        ax.barh(np.arange(len(names)) + (j - 1) * .25, [values[n] for n in names], .25, label=p, color=colors[j])
    ax.set_yticks(np.arange(len(names)), names); ax.invert_yaxis()
    ax.set_xlabel("Share of entity-ticks with component (%)")
    ax.set_title("Component prevalence (not access frequency)")
    ax.legend(loc="upper left", bbox_to_anchor=(1.02, 1))
    fig.tight_layout(); fig.savefig(directory / "composition-presence.png", dpi=150); plt.close(fig)
