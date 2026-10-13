[English](README.md) | Русский

# Локальные сравнения ECS

[Репозиторий](../README.ru.md) | [Отчёт о бенчмарках](../SECS.Benchmarks/README.ru.md) | [Отчёт SpaceBattle](../docs/space-battle/REPORT.ru.md) | [Уведомления о сторонних компонентах](../THIRD_PARTY_NOTICES.ru.md)

Здесь находятся сравнительный Stopwatch-раннер пяти фреймворков, адаптеры, стресс-бэкенды, сохранённые JSON-замеры и скрипты графиков. Наборы BenchmarkDotNet расположены отдельно в `SECS.Benchmarks` и `SECS.OtherBenchmarks` и измеряют SECS, а не все пять фреймворков.

## Каталог исходников

| Фреймворк | Реализация базовой нагрузки | Стресс-реализация | Нативное хранилище SpaceBattle | Исходники runtime |
| --- | --- | --- | --- | --- |
| SECS | [SecsWorkload](Adapters.cs), обёртка [ComparisonWorkload](../SECS.Benchmarks/ComparisonWorkload.cs) | [SecsStressBackend](../SECS.Benchmarks/SecsStressBackend.cs) | [SecsStore](../SpaceBattle.SECS/SecsStore.cs) | [SECS](../SECS/) |
| LeoECS Lite | [LiteWorkload](Adapters.cs) | [LiteStressBackend](StressAdapters.cs) | [LeoEcsLiteStore](../SpaceBattle.Adapters/LeoEcsLiteStore.cs) | [ecslite/src](vendor/ecslite/src/) |
| LeoECS Classic | [ClassicWorkload](Adapters.cs) | [ClassicStressBackend](StressAdapters.cs) | [LeoEcsClassicStore](../SpaceBattle.Adapters/LeoEcsClassicStore.cs) | [ecs/src](vendor/ecs/src/) |
| Morpeh | [MorpehWorkload](Adapters.cs) | [MorpehStressBackend](StressAdapters.cs) | [MorpehStore](../SpaceBattle.Adapters/MorpehStore.cs) | [Morpeh Core](vendor/morpeh/Scellecs.Morpeh/Core/) |
| StaticEcs | [StaticWorkload](StaticAdapters.cs) | [StaticStressBackend](StaticAdapters.cs) | [StaticEcsStore](../SpaceBattle.Adapters/StaticEcsStore.cs) | [StaticEcs/Src](vendor/StaticEcs/Src/), [StaticPack/Src](vendor/StaticPack/Src/) |

Проекты сборки: [Comparison.csproj](Comparison.csproj), [StaticLibrary.csproj](StaticLibrary.csproj), [MorpehLibrary.csproj](MorpehLibrary.csproj) и [SpaceBattle.Adapters.csproj](../SpaceBattle.Adapters/SpaceBattle.Adapters.csproj). StaticPack обслуживает StaticEcs и не является шестым сравниваемым фреймворком. Хранилища SpaceBattle используют общий контракт [IEcsStore](../SpaceBattle.Core/EcsContract.cs) и слой идентичности [NativeStore](../SpaceBattle.Core/NativeStore.cs); см. [контракт адаптеров](../SpaceBattle.Adapters/README.ru.md).

[Манифест vendor](vendor-manifest.json) фиксирует исходники; [check_vendor.py](../tools/check_vendor.py) проверяет распространяемый состав. Поставка содержит подмножества исходников runtime и точные upstream-лицензии, но не upstream-документацию/ассеты, бинарные NuGet-кэши или результаты сборки. Обычный клон уже содержит эти исходники и не требует скачивания vendor. [fetch.ps1](fetch.ps1) является необязательным восстановлением по манифесту с доступом к сети, а не обязательным этапом сборки.

Лицензии не являются единообразно неограниченными MIT: LeoECS Classic предлагает проприетарные условия или MIT-Red, LeoECS Lite использует MIT-Red, Morpeh, StaticEcs и StaticPack используют MIT. Перед распространением прочтите [лицензионный реестр](../THIRD_PARTY_NOTICES.ru.md) и точные лицензии [Classic](vendor/ecs/LICENSE.md), [Lite](vendor/ecslite/LICENSE.md), [Morpeh](vendor/morpeh/LICENSE.md), [StaticEcs](vendor/StaticEcs/LICENSE.md), [StaticPack](vendor/StaticPack/LICENSE.md). Этот каталог не является юридической гарантией.

## Общие нагрузки

[Program.cs](Program.cs) регистрирует все пять базовых адаптеров. На 1 000 и 100 000 сущностей выполняются Create, Iterate, Churn и Destroy: Position/Velocity содержат по два float; движение прибавляет скорость к позиции; churn добавляет и затем удаляет Mass с одним float. Перед замерами проверяются все сущности. Подготовка, заполнение мира, его освобождение и принудительный GC не измеряются; commit Morpeh включён. Каждый случай прогревается минимум 400 мс и сохраняет 20 выборок. Create/Destroy выполняют один свежий пакет на выборку; Iterate/Churn стремятся к 30 мс, максимум 1 000 повторов.

[StressRunner.cs](StressRunner.cs), [StressContract.cs](../SECS.Benchmarks/StressContract.cs) и [StressWorkload.cs](../SECS.Benchmarks/StressWorkload.cs) используют одинаковые начальные данные, маски состава и порядки операций с фиксированным seed для всех пяти бэкендов. Обычная матрица: 100 000/1 000 000 сущностей, семь нагрузок, восемь выборок на случай, прогрев минимум 300 мс, до 16 повторов с целевыми 30 мс. MixedFrames выполняется один раз на выборку.

| Стресс-сценарий | Одна операция |
| --- | --- |
| DenseIteration | Перемещение всех сущностей во всех заполненных составах |
| SparseIteration | Перемещение выборки 1% с Hot |
| WideIteration | Перемещение и изменение первого/последнего полей 256-байтного компонента |
| RandomLookup | Чтение позиций в перемешанном порядке с фиксированным seed и контрольной суммой |
| PayloadChurn | Добавление и удаление 256-байтного компонента в перемешанном порядке |
| Recycle10Percent | Удаление/пересоздание 10% с восстановлением логических данных и состава |
| MixedFrames | 120 кадров: полное движение, 5% churn, 10% чтений и 1% пересозданий в каждом |

Стресс использует 256 базовых масок из восьми необязательных четырёхбайтных компонентов, 12-байтные Position/Velocity и четырёхбайтный Hot на каждой сотой сущности. Промежуточные переходы и Hot могут создавать дополнительные архетипы. MixedFrames повторяет фиксированные подмножества, а не равномерную случайную замену всей популяции. RNG, сортировка, подготовка, проверки, освобождение и принудительный GC не измеряются. Естественный GC, вызванный измеряемыми операциями, включён.

Одинаковая логическая работа не означает одинаковую раскладку памяти, запросы или оптимальные API. Поля аллокаций измеряют управляемые выделения текущего потока, исключая нативное хранилище, а не полную память. Погрешности на графиках показывают выборочное стандартное отклонение, не доверительные интервалы. Результаты относятся к записанной машине/runtime/ревизиям, а не к Unity/IL2CPP или универсальному рейтингу. Эксперименты с оптимизированным созданием/accessor SECS выделены отдельно и не заменяют сравнение общих API.

## Текущая кампания

Канонический [отчёт от 2026-10-13](../docs/benchmarks/current/REPORT.ru.md) содержит [базовые сырые замеры](../docs/benchmarks/current/base.json), [стресс-замеры](../docs/benchmarks/current/stress.json), [метаданные исходников/runtime](../docs/benchmarks/current/campaign.json) и три графика. Это полная свежая матрица пяти фреймворков. Файлы в корне каталога и `docs/benchmarks/updated` — сохранённая история, а не текущий рейтинг.

## Воспроизведение

Запускайте из корня репозитория с runtime .NET 8, подходящим SDK и существующим локальным NuGet-кешем. Эта offline-команда собирает Release, проверяет все адаптеры, записывает хеши исходников/сборок и последовательно запускает базу и стресс с одним измерительным процессом одновременно:

```powershell
./Benchmarks.Local/run_campaign.ps1 -OutputDirectory Benchmarks.Local/repro-campaign
```

Остановите другие бенчмарки, игровые runtime и сборки перед измерениями. `--no-restore` намеренно требует закешированные зависимости; при их отсутствии подготовьте их отдельно до измерений. `GeneratePackageOnBuild=false` отключает создание пакетов. Скрипт сохраняет .NET/NuGet/temp внутри проекта и отключает tiering. `-SkipBuild` предназначен только для отдельно проверенной свежей Release-сборки. Воспроизведение пишет собственные результаты и не перезаписывает канонические опубликованные данные.

Стройте графики после завершения всех измерительных процессов. Скриптам нужны Python с совместимыми matplotlib/NumPy; используйте ту же версию Python, для которой собраны пакеты в локальном кеше (сейчас Python 3.12):

```powershell
$env:PYTHONPATH = "$PWD/Benchmarks.Local/python-packages"
$env:MPLCONFIGDIR = "$PWD/Benchmarks.Local/matplotlib-cache"
python Benchmarks.Local/plot.py Benchmarks.Local/repro-campaign/base.json -o Benchmarks.Local/repro-campaign/comparison.png
python Benchmarks.Local/plot_stress.py Benchmarks.Local/repro-campaign/stress.json --output-dir Benchmarks.Local/repro-campaign
```

Отдельные проверки корректности после сборки:

```powershell
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --validate
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --validate-stress
```

Базовая проверка проверяет данные и жизненный цикл при обоих измеряемых размерах популяции. Стресс-проверка использует 257/4,097 сущностей, семь сценариев по три повтора, добавление/удаление payload и устаревшие handles. Каждый измеренный стресс-кейс также проверяет всю популяцию после прогрева и замеров. Эти проверки подтверждают контракт нагрузки/адаптеров в этом репозитории; они не являются полными upstream-наборами тестов.

## Сохранённые эксперименты

[Методология оптимизации SECS](../docs/benchmarks/optimization-methodology.ru.md) описывает сохранённые бинарники baseline/optimized и парные данные. `results-all-updated.json` / `stress-all-updated.json` и `docs/benchmarks/updated` — предыдущий полный прогон пяти фреймворков. Остальные `results*.json`, эксперименты storage/accessor и BenchmarkDotNet smoke сохраняют свои метаданные и границы применимости. Не смешивайте наблюдения конкурентов из разных кампаний и не используйте времена Dry/smoke как полный рейтинг производительности.

Инструкции запуска BenchmarkDotNet находятся в [SECS.Benchmarks](../SECS.Benchmarks/README.ru.md). Отдельная сквозная симуляция и её воспроизведение — в [отчёте SpaceBattle](../docs/space-battle/REPORT.ru.md).
