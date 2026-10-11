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

## Каталог проверок

| Точка входа | Открытые исходники и проверки |
| --- | --- |
| Comparison `--validate` | [Program.cs](Program.cs), [Adapters.cs](Adapters.cs), [StaticAdapters.cs](StaticAdapters.cs): жизнь и данные при обоих базовых размерах для пяти фреймворков |
| Comparison `--validate-stress` | [StressRunner.cs](StressRunner.cs), [StressWorkload.cs](../SECS.Benchmarks/StressWorkload.cs): 257/4097 сущностей, семь сценариев по три раза, round-trip компонента и устаревшие handles |
| xUnit | [WorldTests](../SECS.Tests/WorldTests.cs), [DefaultWorldTests](../SECS.Tests/DefaultWorldTests.cs), [OptimizationTests](../SECS.Tests/OptimizationTests.cs), [AccessorSwapTests](../SECS.Tests/AccessorSwapTests.cs) |
| Старый набор `--validate` | [SECS.OtherBenchmarks/Program.cs](../SECS.OtherBenchmarks/Program.cs), [BenchmarkInfrastructure.cs](../SECS.OtherBenchmarks/BenchmarkInfrastructure.cs): регрессии жизненного цикла фикстур создания/удаления |
| Headless `--validate-adapters` | [AdapterValidation.cs](../SpaceBattle.Adapters/AdapterValidation.cs): четыре сторонних хранилища, компоненты/запросы/ref, запрет мутаций, устаревшие handles, 70000 циклов и жизнь/регистрация StaticEcs |
| Headless `--validate` / `--validate-ports` | [Validation.cs](../SpaceBattle.Headless/Validation.cs): игровой процесс/физика SECS; четыре порта сравниваются с SECS в десяти точках за 3000 тиков |
| Тесты отчётов/документации | [test_check_docs.py](../tools/test_check_docs.py), [test_report_locale.py](../tools/space-battle/test_report_locale.py), [test_composition_report.py](../tools/space-battle/test_composition_report.py) |
| Тесты состава/восстановления исходников | [test_check_vendor.py](../tools/test_check_vendor.py) проверяет целостность и лицензии; [test_fetch.ps1](../tools/test_fetch.ps1) проверяет восстановление из архива без сети и сохраняет изменённые файлы |

Это проверки нагрузок/адаптеров репозитория, а не полные upstream-наборы тестов фреймворков. При ошибке assertions выбрасывают исключения; headless возвращает ненулевой код. Сохранённые JSON-замеры и smoke-отчёты BenchmarkDotNet не заменяют свежий запуск тестов.

2026-10-11 отдельная копия только публикуемых файлов собрала весь solution
в Release без ошибок и предупреждений. Прошли все 16 тестов xUnit, базовая и
стресс-валидация пяти фреймворков, старые проверки жизненного цикла и все три
режима headless-валидации. Все пять игровых портов совпали в десяти контрольных
точках replay. Исторические замеры и измеренные бинарные файлы не изменены;
это проверка поставки исходников, а не новая кампания замеров или юридическая сертификация.

## Локальное воспроизведение

Запускайте PowerShell из корня репозитория. Требуются Python для проверки состава, .NET SDK с поддержкой целевой .NET 8/runtime проектов и доступные NuGet-зависимости xUnit, BenchmarkDotNet и физики SpaceBattle. Графикам дополнительно нужны Python, matplotlib и NumPy. Vendor-исходники включены; восстановление пакетов всё ещё может требовать сеть или заполненный локальный кэш. Выполняйте restore/build последовательно, замеры без конкурирующей нагрузки.

```powershell
python tools/check_vendor.py
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"
$env:TEMP = "$PWD/Benchmarks.Local/tmp"
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
dotnet build Benchmarks.Local/Comparison.csproj -c Release -p:GeneratePackageOnBuild=false -m:1
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --validate
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --validate-stress
dotnet test SECS.Tests/SECS.Tests.csproj -c Release --logger "trx;LogFileName=secs.trx" --results-directory Benchmarks.Local/test-results
dotnet run -c Release --project SECS.OtherBenchmarks -- --validate
dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release -p:GeneratePackageOnBuild=false -m:1
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-adapters
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-ports
```

При ненулевом коде остановитесь и выясните причину до замеров. Новые имена ниже сохраняют прежние результаты; повторный запуск примеров перезаписывает их файлы `repro-*`.

Для проверки самой поставки исходников без изменения измеренных сборок запустите
[verify-source-distribution.ps1](../tools/verify-source-distribution.ps1).
Он копирует только публикуемые файлы в игнорируемый каталог внутри проекта,
собирает solution и запускает xUnit, обе проверки сравнения, старую проверку
и все режимы headless-валидации. Он использует кеш NuGet внутри проекта и может
требовать сеть для недостающих пакетов; ничего не публикует.

```powershell
$env:DOTNET_TieredCompilation = '0'
Remove-Item Env:SECS_BENCHMARK_SCENARIOS -ErrorAction SilentlyContinue
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll Benchmarks.Local/repro-base.json
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --stress Benchmarks.Local/repro-stress.json
python Benchmarks.Local/plot.py Benchmarks.Local/repro-base.json -o Benchmarks.Local/repro-charts/comparison.png
python Benchmarks.Local/plot_stress.py Benchmarks.Local/repro-stress.json --output-dir Benchmarks.Local/repro-charts
```

`--secs-only` ограничивает любое сравнение SECS; `SECS_BENCHMARK_SCENARIOS` фильтрует стресс-сценарии списком через запятую. Удалите переменную для полной матрицы из 70 случаев. Необязательный `SECS_BENCHMARK_REVISION` записывает предоставленную пользователем метку происхождения, но не проверяет commit.

Запуск всех классов BenchmarkDotNet SECS, включая исходные iteration/churn, общие SECS-фикстуры, стресс и оптимизированные API, затем старого набора:

```powershell
Remove-Item Env:SECS_STRESS_EXTREME -ErrorAction SilentlyContinue
dotnet run -c Release --project SECS.Benchmarks -- --filter '*' --artifacts Benchmarks.Local/repro-bdn
dotnet run -c Release --project SECS.OtherBenchmarks -- --filter '*' --artifacts Benchmarks.Local/repro-legacy-bdn
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --optimized-api Benchmarks.Local/repro-optimized-api.json
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --storage Benchmarks.Local/repro-storage.json
```

[Раннер оптимизированных API](OptimizedApiRunner.cs) проверяет отдельные эксперименты создания/чтения SECS; [диагностика хранилища](StorageRunner.cs) не измеряет время и учитывает сохранённые нативные колонки без накладных расходов аллокатора и управляемых массивов. `SECS_STRESS_EXTREME=1` выбирает пять миллионов сущностей только в стресс-наборе BenchmarkDotNet, не в раннере пяти фреймворков. Сохранённый extreme smoke имеется только для DenseIteration; это не результат полного extreme-набора.

[Headless CLI SpaceBattle](../SpaceBattle.Headless/Program.cs) принимает `--framework secs|lite|classic|morpeh|static`, `--ticks` или `--seconds`, `--warmup`, `--seed`, `--ships-per-team`, `--churn`, `--particles`, `--output` и необязательный `--realtime`. Короткое последовательное воспроизведение для каждого хранилища:

```powershell
foreach ($framework in @('secs', 'lite', 'classic', 'morpeh', 'static')) {
    dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --framework $framework --ticks 3600 --warmup 600 --seed 7331 --ships-per-team 96 --output "Benchmarks.Local/repro-space-$framework.json"
    if ($LASTEXITCODE -ne 0) { throw "SpaceBattle failed: $framework" }
}
```

Это короткий запуск, а не воспроизведение всех сохранённых длинных профилей. Headless пишет JSON и `.ticks.csv`; измеряемый Step включает системы, физику и commit, поэтому не является чистым ECS-микробенчмарком. Конфигурации профилей и ограничения измерений приведены в [отчёте SpaceBattle](../docs/space-battle/REPORT.ru.md).

## Карта сохранённых данных

- Базовый набор пяти фреймворков: [results-all-updated.json](results-all-updated.json); стресс: [stress-all-updated.json](stress-all-updated.json), отдельно сохранён [первый стресс-запуск](stress-all-updated-initial.json). Старые запуски остаются отдельными: [results.json](results.json), [stress-results.json](stress-results.json), [results-initial.json](results-initial.json), [results-four-frameworks.json](results-four-frameworks.json).
- Парная история SECS: [базовый baseline](results-baseline-repeat.json), [базовый optimized](results-optimized.json), [стресс baseline](stress-baseline-repeat.json), [стресс optimized](stress-results-optimized.json). [Методология](../docs/benchmarks/optimization-methodology.ru.md) описывает сравнение с сохранённым бинарником; сборка текущих исходников не воспроизводит исторический бинарник. [plot_optimization.py](plot_optimization.py) использует эти парные входные данные.
- Отдельные эксперименты API/хранилища: [optimized-api-results.json](optimized-api-results.json), [storage-baseline.json](storage-baseline.json), [storage-optimized.json](storage-optimized.json); проверки итерации: [baseline](iteration-baseline-check.json), [hoist](iteration-hoist-check.json), [optimized](iteration-optimized-check.json).
- Smoke-свидетельства BenchmarkDotNet: [сравнение](bdn-smoke-inprocess/results/SECS.Benchmarks.ComparisonBenchmarks-report-github.md), [стресс](bdn-stress-smoke/results/SECS.Benchmarks.StressBenchmarks-report-github.md), [extreme](bdn-extreme-smoke/results/SECS.Benchmarks.StressBenchmarks-report-github.md). Отдельные отчёты API: [создание](optimization-bdn-artifacts/results/SECS.Benchmarks.OptimizedApiCreationBenchmarks-report-github.md), [чтение](optimization-bdn-artifacts/results/SECS.Benchmarks.OptimizedApiLookupBenchmarks-report-github.md). Это не количества пройденных тестов полного набора.
- SpaceBattle: [исходные JSON/CSV по фреймворкам](../docs/space-battle/results/), [summary.csv](../docs/space-battle/summary.csv), [windows.csv](../docs/space-battle/windows.csv), [метаданные оборудования](../docs/space-battle/hardware.json) и [отчёт](../docs/space-battle/REPORT.ru.md).

Перед сравнением проверьте метаданные и полноту каждого результата. Отфильтрованный запуск, ошибочный/smoke-отчёт или старое подмножество фреймворков нельзя представлять полной актуальной матрицей пяти фреймворков.
