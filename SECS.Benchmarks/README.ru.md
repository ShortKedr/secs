[English](README.md) | Русский

# Бенчмарки SECS

[Обзор проекта](../README.ru.md) | [Отчёт SpaceBattle](../docs/space-battle/REPORT.ru.md) | [Раннер](../Benchmarks.Local/README.ru.md)

## Текущее сравнение

[Отчёт текущей кампании](../docs/benchmarks/current/REPORT.ru.md) содержит свежее сравнение SECS, LeoECS Lite, LeoECS Classic, Morpeh и StaticEcs: 40 базовых и 70 стресс-кейсов, полные сырые выборки, хеши исходников и три графика. Все пять фреймворков измерены заново по общему контракту нагрузки.

![Базовое сравнение](../docs/benchmarks/current/comparison.png)

Базовая матрица измеряет Create, Iterate, Churn и Destroy при 1,000 и 100,000 сущностей. Стресс измеряет плотную/разреженную/широкую итерацию, случайный доступ, churn 256-байтового payload, переработку 10% и смешанный пакет из 120 кадров при 100,000 и 1,000,000 сущностей. [Отчёт](../docs/benchmarks/current/REPORT.ru.md) содержит время операций и ограничения измерений. [README раннера](../Benchmarks.Local/README.ru.md) описывает точные сценарии и воспроизведение.

Это Stopwatch-замеры эквивалентной логической работы через выбранные адаптеры. Они характеризуют записанную машину и снимки исходников; они не устанавливают универсальный рейтинг или производительность Unity/IL2CPP. Погрешности показывают стандартное отклонение выборки. Управляемые аллокации в JSON исключают нативную память.

## Наборы BenchmarkDotNet для SECS

[ComparisonBenchmarks](ComparisonBenchmarks.cs) использует ту же [базовую нагрузку](ComparisonWorkload.cs), что и раннер пяти фреймворков. [StressBenchmarks](StressBenchmarks.cs) использует ту же [стресс-нагрузку](StressWorkload.cs). [OptimizedApiBenchmarks](OptimizedApiBenchmarks.cs) проверяет отдельные API factory/accessor SECS; эти замеры не подставляются в общее сравнение. [Program.cs](Program.cs) также сохраняет исходные бенчмарки итерации/churn. `SECS.OtherBenchmarks` содержит отдельный legacy-набор.

BenchmarkDotNet здесь измеряет только SECS. Быстрая проверка запуска после Release-сборки:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"
dotnet build SECS.Benchmarks/SECS.Benchmarks.csproj -c Release --no-restore -p:GeneratePackageOnBuild=false -m:1
dotnet SECS.Benchmarks/bin/Release/net8.0/SECS.Benchmarks.dll --filter '*ComparisonBenchmarks.Iterate*' --job Dry --inProcess --artifacts Benchmarks.Local/repro-bdn-smoke
```

Dry job проверяет запуск и выполнение нагрузки; его времена не являются рейтингом производительности. Сравнительная custom-кампания уже покрывает полную матрицу нагрузки пяти фреймворков. Полный BenchmarkDotNet-прогон необязателен и должен выполняться отдельно от любых других измерений.

## Исторические эксперименты

[Методология оптимизации от 2026-10-11](../docs/benchmarks/optimization-methodology.ru.md) описывает отдельный эксперимент SECS до/после и сохранённые бинарники для его интерпретации. [Предыдущая база](../Benchmarks.Local/results-all-updated.json) и [стресс](../Benchmarks.Local/stress-all-updated.json) доступны вместе с [предыдущими графиками](../docs/benchmarks/updated/comparison.png). Это сохранённая история, а не сегодняшний рейтинг. Пересборка текущих исходников не воспроизводит старый baseline-бинарник.
