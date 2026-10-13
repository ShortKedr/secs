[English](README.md) | Русский

# Локальное сравнение физики

[Индекс исследований](../../docs/space-battle/RESEARCH.ru.md) | [Текущая кампания](../../docs/space-battle/REPORT.ru.md)

Это отдельный инструмент сравнения, не реализация игровой физики.
Полный сохраненный Aether-wrapper находится в `AetherReference.cs`, namespace
`SpaceBattle.PhysicsStudy`. Приватный путь к исходникам baseline не нужен.
Aether 2.2.0 закреплен только в проекте инструмента; production-код здесь не меняется.
Числа производительности пока не публикуются.

## Сборка и проверка

Из корня репозитория, с .NET 8 SDK, до разрешенного окна замеров:

```powershell
dotnet build tools/physics/PhysicsStudy.csproj -c Release --ignore-failed-sources
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --inspect
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --verify-baseline
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --verify
```

`--inspect` показывает публичные диагностические свойства World и доступность
нового API. `--verify-baseline` проверяет воспроизводимость входов и операции
baseline; `--verify` дополнительно проверяет новый API, попадания и SweepRectangle.
Это не timed benchmark. Отсутствующий новый API приводит к явной ошибке, а не
к сравнению старой реализации с самой собой.

Ожидаются `PhysicsService(PhysicsSettings? settings = null)`, `CollectTimings`,
`Timings { ContactCandidatesMs, SolverMs, HitCandidatesMs, HitNarrowMs }`,
`QueryStatistics.Candidates/NarrowTests`, `GetMassProperties` и
`SweepRectangle(from,to,halfExtents,angle,ignoredSerial,out serial,out impact)`.
Существующие SetShip, Sweep, Bodies, Fixtures и Contacts используются напрямую.
Настройки связываются reflection вне измеряемых циклов.

## Разрешенное окно

Выделите свободное окно после завершения всех сборок и других измерений.
Обязательный флаг подтверждает это условие и не обнаруживает конкурирующие нагрузки.
Во время замера запускается готовая DLL, не `dotnet run`:

```powershell
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --approved-timing-window --run local-01 --seed 7331 --ticks 120 --warmup 20 --sweeps 1024 --repeats 3
```

Для повторяемой native-диагностики используйте DOTNET_TieredCompilation=0 и warmup=120:
этот протокол выбран после холодных tier-переходов в исходном исследовании.
Сначала соберите tool, затем в свободном окне запускайте готовую DLL.
После запуска восстановить предыдущее значение env в своей оболочке.

```powershell
$env:DOTNET_TieredCompilation = "0"
dotnet tools/physics/bin/Release/net8.0/PhysicsStudy.dll --approved-timing-window --run circle-native-final-01 --seed 7331 --ticks 120 --warmup 120 --sweeps 1024 --repeats 3
```

Manifest сохраняет DOTNET_TieredCompilation, DOTNET_TieredPGO, DOTNET_TC_QuickJit,
DOTNET_TC_QuickJitForLoops и DOTNET_ReadyToRun; null означает unset, не disabled.
circle-native-01 остается диагностическим evidence, не final-effect результатом.
Минимум три свежих повтора сохраняется. Настройки не обнаруживают занятость машины.

Все входы, raw-кадры, сравнения, manifest и копии исходников/бинарников сохраняются
только в ignored `Benchmarks.Local/tmp/physics-study/<run>/`. Существующие каталоги
не перезаписываются. Нет удалений, Git, публикации, перезапусков preview/host/browser.
Синтетические native-сцены, browser workload и полная headless-кампания — отдельные измерения.

## Границы сравнения

Десять синтетических сцен: 198 и 774 корабля, для каждого размера sparse round,
mixed radii round, legacy compound из 4/5/6 fixtures, dense round и moving targets.
Это исторические количества кораблей, не воспроизведение исторического матча.
Геометрия round-сцен одинаковая. Compound-сцена намеренно сравнивает старые составные
fixtures с новыми кругами. Генератор xorshift с seed и сохраненные входы/SHA-256
задают одинаковое состояние и минимум 1000 sweeps на кадр для обеих реализаций.
Есть нулевая длина, нулевой радиус и ignoredSerial. Moving-probe hits учитываются
отдельно; различия ожидаемы из-за учета относительного движения в новом движке.

SetShip восстанавливает заданные позиции и скорости перед каждым Step. Кроме
moving-target сцены, перед sweeps задается одинаковое кинематическое конечное состояние: end=start+velocity*dt,
Previous=start. Перемещение от solver не влияет на сравнение попаданий. Результаты
предыдущего кадра не становятся входом следующего. В изолированной moving-target
сцене пути Step сохраняются, потому что SetShip очищает историю CCD. Все цели имеют
одинаковые скорости и большие интервалы; любой контакт является ошибкой, чтобы
solver не менял заданные пути. --verify проверяет пересекающееся движение: relative
CCD попадает, статический Aether ray промахивается. Это не связанная игровая
траектория. Moving targets: radius=4, speed=1080, 18 единиц/кадр, ниже ограничения
перемещения Aether; verify сначала проверяет конечные позиции обоих движков.
Baseline сохраняет friction=0
и restitution=0.1; новые defaults (включая friction=0.08) записываются в manifest.
Одинаковая геометрия не означает идентичные настройки solver. Contacts считает
уникальные begin events, не все сохраняющиеся контакты Aether.

Warmup имеет отдельный world для каждого режима/движка. После него минимум три
свежих независимых world; порядок движков чередуется. Подготовка входов, создание
world, GC, warmup и I/O вне замеров. Внутри движка проверяется стабильность подписей
попаданий/контактов между повторами и режимами; сбой сохраняет raw evidence.
Различия между движками записываются, не маскируются. TotalMs включает два reset,
Step и sweeps; CombinedStepMs и HitsOuterMs измеряют более узкие внешние участки.
Снимки counters/reflection после внешнего таймера и учета аллокаций.

## Честные фазы

Есть отдельные прогоны CollectTimings=false для внешних totals и true для фаз.
Оба median totals сохраняются, чтобы увидеть overhead timestamp-инструментации.
Для throughput используются uninstrumented totals.

В проверенном пакете 2.2.0 net8.0 все семь свойств имеют тип TimeSpan, а
EnableDiagnostics является constant true. Встроенный overhead остается в обоих
режимах; сравнивается отдельно добавленный overhead wrapper.
Aether требует Settings.EnableDiagnostics=true. Если флаг изменяемый, wrapper
восстанавливает его при Dispose; world запускаются последовательно. Если пакет
собран с constant/readonly false, включить diagnostics нельзя: split остается null,
несмотря на публичные свойства. --inspect показывает значение и изменяемость флага.
Constant true нельзя отключить, тогда встроенная инструментация остается и в
outer-only прогонах. Публичные TimeSpan свойства:

- Search internals: NewContactsTime + ContactsUpdateTime.
- Solver internals: SolveUpdateTime + ContinuousPhysicsTime.
- combinedStep: внешнее время World.Step.
- HitCandidatesMs: empty-rejection QueryAABB вместе с подготовкой bounds.
- HitNarrowMs: весь point-query/five-ray путь, включая внутренний tree search.

Это реальные внутренние таймеры Aether, не полное непересекающееся разбиение Step.
Continuous solve может включать дополнительный поиск. Controllers, add/remove и
работа wrapper не входят в две суммы. Нельзя сложить фазы и объявить их outer total;
время rays нельзя назвать чистой narrowphase. Недоступные или не-TimeSpan свойства
записываются как null; без split остается только combinedStep, без выдуманных нулей.
Candidates baseline считает посещенные fixtures раннего empty-rejection с остановкой
на первом подходящем. NarrowTests считает только явные TestPoint; внутренние тесты
rays не экспортируются и не выдумываются. Rays/PointQueries сохраняют исходный смысл.
Эти counters не эквивалентны новым pair/candidate counters.

Gameplay и эквивалентность compound/rectangle вне границ native study.

## Отдельное сравнение production WASM

`tools/web/physics-study.mjs` сравнивает сохранённый Aether + LightSwarm
с отдельно подготовленной circle-kernel production AOT-сборкой. Порты 8049/8050 —
примеры из исходного исследования, а не гарантированно работающие серверы.
Подготовьте и проверьте обе сборки; закончите компиляцию и конкурирующие нагрузки
до замера. Скрипт не запускает серверы или сборки.

```powershell
node --check tools/web/physics-study.mjs
node tools/web/physics-study.mjs --verify
node tools/web/physics-study.mjs --validate-assets-live --run web-capture-only-01
node tools/web/physics-study.mjs --approved-timing-window --run web-local-01 --before http://127.0.0.1:8049/ --after http://127.0.0.1:8050/ --profiles low,nominal,high --seed 7331 --repeats 3 --warmup-ticks 120 --ticks 600
```

Первые две команды не открывают browser, не обращаются к серверам и не измеряют
время. Для замеров обязательны Mode()==aot-binary и загруженный native WASM asset.
Сохраняются hash WASM/assembly/JS/config, runtime и fleet revisions. URL только
loopback; внешние запросы блокируются. Raw results и временные файлы browser
только в ignored physics-study/<run>, без remote writes.

Для каждого churn profile (low=0.5, nominal=1, high=2) минимум три свежих browser
context и battle на revision; seed и начальная конфигурация LightSwarm одинаковы.
Warmup выполняется в отдельном battle, затем Reset создает измеряемый battle с
tick=0. Порядок revisions чередуется. Canvas simulation paused через test=1 и
pause(true). Синхронный page task вызывает существующий Advance(1), не позволяя UI
рендериться внутри цикла. ProfileAdvance в просмотренном bridge не экспортируется,
его наличие и семантика не предполагаются.

В old bridge внутренние physics phases недоступны: явно unavailable/null.
DiagnosticStage physics/hits остается combined gameplay stages, без выдуманного
search/solver split. Новый ResetPhysicsProfile(profile,seed,collect) и
PhysicsTiming(0..3) дают согласованные четыре фазы. Для new отдельно свежие outer
collect=false и inner collect=true прогоны, чтобы видеть overhead. Diagnostics
читаются после outer timer Advance/export. Не-AOT, отсутствующие новые exports,
несовпадение начальной конфигурации/fleet и JS errors являются явной ошибкой;
локальные raw evidence сохраняются.

--validate-assets-live открывает свежий paused test-mode context для каждой из двух
локальных сборок, проверяет tick=0, readiness, AOT bridge и assets. Без Reset,
Advance, PhysicsTiming, warmup и таймеров; окно benchmark не требуется. Capture
ожидается после точной готовности bridge и network idle. Assets захватываются через
same-origin route.fetch(maxRedirects=0), hash вычисляется до route.fulfill из того
же response, не отдельного повторного запроса. Это устраняет inspector-cache
eviction большого native WASM. Redirects запрещены; поздние assets после barrier
считаются ошибкой. Старые failed runs не меняются. Если env override отсутствует,
используется существующий project-local cache Playwright browsers.

Это production before/after runtime physics workload, не native prescribed scenes
и не изолированная скорость компилятора. Survival/hits/entity counts/checksums могут
намеренно отличаться. Различия сохраняются, cross-engine checksum parity не
утверждается и не проверяется. Нет FPS/render-throughput claim. Исторические
опубликованные результаты не меняются.
