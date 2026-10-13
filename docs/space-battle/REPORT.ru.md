[English](REPORT.md) | Русский

# Бенчмарки Space Battle

Актуальная кампания: 2026-10-13T15:03:13.9771756+05:00 — 2026-10-13T15:11:20.5557936+05:00.

Измерена текущая headless-симуляция на пяти ECS: `mounted-armament-v7`, `layered-squads-v3`, `circle-kernel-v1`, `layered-weapon-damage-v1`.

**Проверка пройдена:** 15 случаев, 1 620 000 измеренных тиков. В каждом профиле совпали все 30 контрольных точек, итоговые checksum, игровые и тактические счётчики и поминутная численность сущностей.

Тики CSV проверены локально: пересчитаны среднее, квантили, разброс, превышения бюджета и аллокации. Хеши исходных CSV сохранены в манифесте.

## Как читать результаты

Каждый случай — один независимый прогон из 108 000 тиков (30 минут симуляции) после отдельного прогрева на 600 тиков. Low/nominal/high меняют только churn: 0.5/1/2. Seed 7331, 96 эскортов и 3 носителя на команду, 60 тиков/с. Использованы headless defaults: равномерный состав, прежняя случайная расстановка, бесконечные подкрепления выключены. Графическая игра использует другой игровой preset; это видно в [настройках клиента](../../SpaceBattle.Game/README.ru.md).

Время Step включает игровые системы, физику и структурные commit. Рендеринг, начальное заполнение, snapshots и запись телеметрии не входят. Бюджет 16.67 ms относится к симуляции, а не к FPS. Показаны наблюдения этого компьютера; один прогон на случай не даёт устойчивого рейтинга и доверительного интервала. Аллокации — управляемые выделения потока внутри Step, без нативной памяти.

### Цветовая шкала

![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") до 5% · ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 15% · ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") от 30% · ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") нейтрально.

Цвет показывает отставание от лучшего значения в сопоставимой группе: зелёный → жёлтый → красный. Если вся группа укладывается в 5%, она серая. Цвет описывает размер различия, а не статистическую значимость. [Правила сравнения](../benchmarks/COLORS.ru.md).

## Время тика

| Profile | ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| low | secs | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1638 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2584 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.4543 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 6.0640 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 109.0 |
| low | lite | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.2002 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.3228 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.5329 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 7.0941 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 146.1 |
| low | classic | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.1810 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.2915 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 0.5171 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 6.4586 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 133.5 |
| low | morpeh | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.1700 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.2769 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.4887 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 5.9493 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 152.0 |
| low | static | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.1944 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.3153 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.5151 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 6.1519 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 109.4 |
| nominal | secs | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.2225 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.3351 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.4740 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 5.9797 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 197.0 |
| nominal | lite | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.2850 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.4444 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.6047 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 6.3300 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 238.4 |
| nominal | classic | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.2615 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.4021 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.5596 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 6.3914 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 232.4 |
| nominal | morpeh | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.2489 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 0.3873 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.5286 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 5.9502 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 249.0 |
| nominal | static | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.2816 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.4362 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.5989 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 6.0453 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 198.7 |
| high | secs | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.3522 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.5533 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.7024 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 6.1444 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 413.2 |
| high | lite | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.4693 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.7366 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.9177 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 6.3862 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 492.8 |
| high | classic | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.4253 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.6697 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.8484 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 6.3780 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 469.2 |
| high | morpeh | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.3987 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 0.6422 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.8367 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 6.4360 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 466.1 |
| high | static | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.4394 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.7172 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.9454 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 9.0184 | ![Практически неразличимо или недостаточно данных о разбросе](../benchmarks/heatmap/equal.svg "Практически неразличимо или недостаточно данных о разбросе") 0 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 416.1 |

![Mean Step time](current/MeanMs.png)

![P99 Step time](current/P99Ms.png)

## Состояние симуляции и стадии

| Profile | Checksum | Ships | Turrets | Shots | Hits | Created | Destroyed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| low | 69D77D8C4F53CE02 | 36 | 56 | 162943 | 144404 | 1533 | 1497 |
| nominal | C2521F09C1B2C7E3 | 38 | 48 | 270351 | 230461 | 2754 | 2716 |
| high | 28184C58A2877695 | 56 | 46 | 414013 | 327079 | 4763 | 4707 |

Среднее время стадий в nominal, ms. В каждой строке сравнивается одна стадия пяти портов; полные данные находятся в JSON.

| Stage | SECS | Lite | Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: |
| collect | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0467 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0557 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0477 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0476 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 0.0536 |
| ai | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0377 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.0457 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.0423 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.0411 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.0475 |
| thermal-inventory | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0048 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0065 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.0059 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0052 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0070 |
| steering | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0083 | ![Отставание между 5% и 30%](../benchmarks/heatmap/poor.svg "Отставание между 5% и 30%") 0.0102 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.0094 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0088 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0112 |
| weapons | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0183 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.0229 | ![Отставание между 5% и 30%](../benchmarks/heatmap/strong.svg "Отставание между 5% и 30%") 0.0199 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0191 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0239 |
| carriers | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0006 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0006 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0004 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 0.0005 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0008 |
| physics | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0194 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0404 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0311 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0353 | ![Отставание от 30%](../benchmarks/heatmap/worst.svg "Отставание от 30%") 0.0389 |
| hits | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0725 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0850 | ![Отставание между 5% и 30%](../benchmarks/heatmap/weak.svg "Отставание между 5% и 30%") 0.0865 | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0755 | ![Отставание между 5% и 30%](../benchmarks/heatmap/middle.svg "Отставание между 5% и 30%") 0.0827 |
| lifecycle | ![Отставание до 5%](../benchmarks/heatmap/best.svg "Отставание до 5%") 0.0142 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.0180 | ![Отставание между 5% и 30%](../benchmarks/heatmap/bad.svg "Отставание между 5% и 30%") 0.0183 | ![Отставание между 5% и 30%](../benchmarks/heatmap/good.svg "Отставание между 5% и 30%") 0.0158 | ![Отставание между 5% и 30%](../benchmarks/heatmap/fair.svg "Отставание между 5% и 30%") 0.0159 |

## Происхождение данных

- CPU: AMD Ryzen 9 9900X 12-Core Processor; RAM: 61.49 GiB.
- Runtime: .NET 8.0.31; OS: Microsoft Windows 10.0.26200; tiered compilation: `0`.
- Source: `07e7ad04a18a15bfe24c130426fa8f2f2352b981`; source SHA256: [current/manifest.json](current/manifest.json).
- [JSON](current/), [summary.csv](current/summary.csv), [hardware.json](current/hardware.json).

Манифест фиксирует исходники и хеши входных файлов; каждый JSON — точные upstream pins и SHA256 всех измеряемых DLL. Поле Provenance.Sources.SECS в старом headless host содержит описательную историческую строку; точная ревизия и исходники этой кампании задаются манифестом.

## Воспроизведение

Из корня проекта, с .NET 8 и доступным кешем NuGet. Для графиков нужны Python и matplotlib. Запускайте измерения последовательно, без другой нагрузочной работы. Команда перезаписывает current; для собственных экспериментов задайте другой OutputDirectory внутри проекта.

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1 -p:GeneratePackageOnBuild=false
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-adapters
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-ports
./tools/space-battle/run-suite.ps1 -SkipBuild -OutputDirectory docs/space-battle/current
python tools/space-battle/analyze_current.py --verify-ticks
```

Без --verify-ticks анализатор проверяет опубликованные JSON и хеши и строит тот же отчёт без требования локальных CSV. Длинные CSV остаются локально; опубликованных JSON достаточно для сводных таблиц и графиков.

## История и связанные документы

- [Архив измерений 2026-10-11](REPORT_2026-10-11.ru.md): predictive-neutral-v1, Aether, старый frontend replay и состав сущностей. Его цифры не сравниваются с этой кампанией: изменились правила игры, физика и нагрузка.
- [Архитектура](ARCHITECTURE.ru.md), [физика](PHYSICS.ru.md), [оружие](ARMAMENT.ru.md), [тактика](TACTICS_V3.ru.md).
- [Микробенчмарки SECS](../../SECS.Benchmarks/README.ru.md): отдельная нагрузка без игровой логики.
