[English](MONO_COMPILER_STUDY.md) | Русский

# Проверка Компиляции Stable Mono AOT

## Решение

2026-10-12: исходная production-конфигурация сохранена. Устойчивый общий выигрыш
от O3/LTO не подтверждён. Это измеренный отрицательный результат, не утверждение,
что флаги никогда не помогут другой сцене или машине. C#-симуляция, SECS, физика,
AI, баланс, RNG и рендер не менялись. Добавлены измерительные инструменты, CI-тесты
и отдельный `-ArtifactsDirectory` штатного build-скрипта; defaults совместимы.
Удалённых операций и перезаписи исторических отчётов не было.

## Входы И Реальные Команды

Windows 11 Pro, Ryzen 9 9900X,24 логических процессора, около61.5 GiB доступной RAM;
Chromium151.0.7922.34, Playwright1.62.1, Node24.14.0. SDK **8.0.425**, Mono/runtime
**8.0.31**, LLVM/Clang **16.0.5**, Emscripten **3.1.34**. CoreCLR/R2R,
NativeAOT-LLVM и .NET10 не использовались; desktop не мигрировал.

Baseline - текущий dirty workspace, не опубликованный бенчмарк и не живая Pages.
HEAD: `2f3625a408625b74fa0b42edaac6a20a121ccca5`. Заморожен81 файл исходников,
конфигурации и ассетов. SHA256 manifest:
`EAB44BC1B7D6B1B93C270E70267E4CBA8C0DF331E377526FCEAF5B2D30F876DB`.

Команды Mono действительно содержат `--wasm-exceptions --debug --llvm` и
`--aot=no-opt,static,direct-icalls,deterministic,mattr=simd,...,llvmonly,interp,asmonly`.
AOT mode - `LLVMOnlyInterp`, AOT прошли33 сборки. `no-opt` не означает отсутствие
оптимизации на последующих LLVM-стадиях. Реальные emcc/clang, wasm-ld/Binaryen,
response-файлы и binlog сохранены для каждого варианта. Конфликтующих optimization
flags, fast-math или новых отключений исключений/safety checks не обнаружено.

| Вариант | Native C-файлы | AOT-bitcode / финальный link | WASM MB | Brotli MB | Сборка, секунд |
|---|---|---|---:|---:|---:|
| A | -Oz | -O2 | 9.847 | 2.295 | 75.6 |
| B | -Oz | -O3 | 10.287 | 2.324 | 117.6 |
| C | -O3 | -O3 | 10.272 | 2.312 | 114.0 |
| D | -O3, -flto | -O3, -flto | 10.262 | 2.311 | 144.9 |

MB десятичные; build time - одиночные наблюдения, не независимые бенчмарки.
Link optimization управляет также emcc-компиляцией AOT-bitcode, compile optimization
отдельно управляет `driver.c`, `pinvoke.c`, `corebindings.c`.

Первый D упал: `llvm-size` SDK не читает сохранённые bitcode-объекты. Initial heap
зафиксировали на фактических **16 MiB** A, затем обнаружилось отсутствие LTO
системных библиотек в frozen SDK cache. Подготовлен отдельный скопированный кэш
тем же `embuilder.py build MINIMAL --lto` (87.5 секунд без копирования). Финальный D
использует его, maximum heap остаётся **256 MiB**. `-flto` проверен в native C,
AOT-bitcode и link; перед link объекты сохраняют bitcode magic. Готовые runtime
архивы содержат WASM-объекты, включая267 членов `libmonosgen-2.0.a`. Это обычный
LLVM LTO с ограниченным охватом, **не whole-program оптимизация runtime**.
Обе неудачные попытки сохранены; обходы не добавлены в production.

## Методика

Основная серия:72 свежих browser process/context/page, четыре варианта,
low/nominal/high, три независимых повтора отдельно core и frontend. Порядок
вращается, дисбаланс позиций ограничен единицей. Одинаковые seed7331, LightSwarm,
endless reinforcements и настройки; colliders off, viewport1440x900. Отдельный бой
греется1200 тиков, затем Reset. Core измеряет1-1200 с фазами300 тиков; frontend
измеряет301-600 после своего Reset и доведения до окна. Тики не независимые повторы.

Подтверждение:18 свежих процессов, A/C/D, low/high, по три повтора, сбалансированный
порядок трёх вариантов; отдельный прогрев3600 тиков, Reset, окно1-2400 с фазами600.
Сборки, CPU-профиль и throughput не пересекались. Чужие процессы не трогались;
фоновая активность, питание и температура не контролировались. Разные окна и
прогревы не объединяются.

Step - header[5], export - header[45], `Advance(1)` wall включает export/interop.
Остатки через вычитание - оценки, могут быть отрицательными при разрешении таймера.
Frontend использует исходный `advanceTime(1000/60)` с binary ingestion, DOM и
Canvas draw submission. Автоматический RAF выключен только в отдаваемой
измерительной странице: production-файлы не меняются. Это исключает фоновый draw
и старые frontend views после прямого bridge.Reset. Замер отражает клиентский
CPU-путь, **не RAF pacing, GPU busy time и не реальный FPS**.

Свежий процесс исключает process-shared WASM code cache. Cold ready считается от
navigation, без browser launch/polling. OS-кэши прогреты, файлы локальные несжатые;
это не сетевой download benchmark. Начальный startup-бой везде nominal, измеряемый
профиль выбирается позднее. Только в отдаваемом app.js есть два startup timestamp
и getter заголовка вне таймера. Исходный/отдаваемый hashes, browser/runtime identity,
сырые тайминги и память сохранены.

## Результаты

Медиана средних трёх независимых прогонов, min-max диапазон в скобках; миллисекунды.
p95/p99 - медианы перцентилей отдельных прогонов одинакового окна1-1200,
не перцентили объединённых повторов/профилей.

| Профиль | Вариант | Step mean [диапазон] | Step p95 / p99 | Export mean |
|---|---|---:|---:|---:|
| low | A | 2.416 [2.379-2.920] | 4.4 / 5.5 | 0.528 |
| low | B | 2.794 [2.392-2.818] | 5.3 / 6.1 | 0.621 |
| low | C | 2.595 [2.360-2.836] | 5.2 / 6.1 | 0.587 |
| low | D | 2.315 [2.241-2.797] | 4.0 / 4.5 | 0.511 |
| nominal | A | 3.147 [3.142-3.278] | 6.2 / 7.4 | 0.751 |
| nominal | B | 3.129 [3.089-3.162] | 6.0 / 7.0 | 0.781 |
| nominal | C | 3.085 [3.060-3.578] | 5.9 / 6.7 | 0.760 |
| nominal | D | 3.083 [2.984-3.134] | 5.9 / 6.4 | 0.740 |
| high | A | 4.703 [4.488-4.717] | 9.6 / 13.8 | 1.202 |
| high | B | 4.492 [4.285-5.165] | 9.1 / 11.2 | 1.184 |
| high | C | 4.304 [4.246-5.248] | 9.3 / 11.9 | 1.143 |
| high | D | 4.312 [4.259-4.456] | 8.7 / 12.4 | 1.127 |

| Вариант | Клиент low [диапазон] | Клиент nominal [диапазон] | Клиент high [диапазон] |
|---|---:|---:|---:|
| A | 6.555 [6.525-7.126] | 5.626 [5.073-6.469] | 7.567 [7.547-8.153] |
| B | 6.617 [6.505-6.681] | 5.387 [5.144-6.673] | 7.396 [7.299-8.189] |
| C | 6.562 [6.450-6.700] | 5.316 [5.260-5.406] | 7.639 [7.314-7.668] |
| D | 6.806 [6.646-7.154] | 5.402 [5.357-6.428] | 8.762 [7.568-9.130] |

Runtime-ready medians основных core-групп: A335-344ms, B348-350ms, C343-367ms,
D343-356ms; app-ready:536-541,544-559,542-569,540-553ms соответственно. Улучшение
старта не подтверждено. Maximum capacity линейной памяти: A/B121,044,992bytes,
C/D120,848,384bytes; не live allocation totals. Все отдельные cold samples,
диапазоны, client/export p95/p99 и фазовая статистика находятся в ignored JSON.

| Подтверждение | A Step [диапазон] | C Step [диапазон] | D Step [диапазон] |
|---|---:|---:|---:|
| low1-2400 | 3.147 [3.043-3.200] | 2.998 [2.987-3.151] | 2.997 [2.958-3.042] |
| high1-2400 | 4.200 [3.952-5.007] | 4.018 [3.967-4.952] | 4.185 [3.872-4.943] |

Основной high D показал снижение8.3%, подтверждение - **0.35%** с широким
перекрытием диапазонов. C в подтверждении даёт4.7% low /4.3% high с перекрытием;
основной low показал обратное. В подтверждающем high1-600 A/C/D means равны
4.513/4.562/4.926ms, p99 -13.5/16.1/14.0ms: общее среднее не скрывает регрессии
плотной фазы/хвостов. P-value и универсальный причинный выигрыш не заявляются.

## Именованный CPU-Профиль Mono

Отдельная Release/O2-сборка с `WasmNativeStrip=false`, `WasmEmitSymbolMap=true`;
не throughput/publish-кандидат. CDP сэмплирует `Advance(1)` с binary export после
отдельного прогрева/Reset.3112 и2745 сэмплов начального/позднего окна. Ниже
**self leaf-sample counts**, не inclusive stack time и не точные доли CPU-времени.

| Атрибуция | Тики1-1200 | Тики1201-2400 |
|---|---:|---:|
| Собственный C#/SECS, включая Capture/export | 44.5% | 46.2% |
| Именованные тела managed-библиотек | 3.3% | 3.8% |
| Исполнение mono_interp_* | 17.5% | 14.9% |
| Dispatch/runtime helpers | 26.5% | 26.0% |
| GC | 1.7% | 1.3% |
| Именованные interop leaves | 0.10% | 0.07% |
| Другой JS | 0.4% | 0.2% |
| Неизвестные | 5.9% | 7.5% |

Расходы: реальные вычисления/Capture, `World.Get<T>_GSHAREDVT`, ABI-обёртки generic
value types, `do_jit_call`, очистка/копирование памяти, интерпретация. AOT не
означает interpreter-free hot path. Это **не прежние85% из CoreCLR**. Широкая
категория helpers не измеряет только interface/delegate dispatch. Именованные
пути отличают обёртки от тел библиотек, но inlining мешает точной source attribution.
Непрозрачные маленькие WASM-модули остаются unknown; map главного модуля к ним не
применяется. GC/direct interop leaves не доминируют; inclusive callers нельзя
складывать или считать marshalling cost. Compile flag не перекомпилирует готовый
runtime. Исчезновение bounds checks/снижение branch misses не заявляются.

## Эквивалентность И Повторение

Основная серия:11,106 сравнений; подтверждение:48 структурных групп. Ноль
расхождений checksum, sampled counters/populations, видимого состава, тактики
и подкреплений внутри browser/runtime. Это не native/WASM bitwise proof.
Первый профиль/основная попытка с ошибкой диагностических RAF views сохранены,
но исключены; последующие captures/replays чистые.

Raw root: `Benchmarks.Local/tmp/mono-study-20261012`; серии:
`Benchmarks.Local/tmp/mono-compiler-primary-20261012-02` и
`Benchmarks.Local/tmp/mono-compiler-confirm-20261012-01`. Они ignored и в GitHub
checkout недоступны; сломанных Markdown-ссылок на них нет.

```powershell
# New ignored Study directory; configured project-local SDK8.0.425/wasm-tools.
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant Freeze
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant A
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant B
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant C
./tools/web/prepare-mono-lto-cache.ps1 -Study Benchmarks.Local/tmp/my-mono-study
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant D -PrivateLtoCache
node tools/web/mono-compiler-study.mjs --help
node tools/web/profile-mono-cpu.mjs --help
node --test tools/web/test-mono-compiler-study.mjs tools/web/test-profile-mono-cpu.mjs
./tools/web/test-build-mono-study.ps1
```

Build/cache scripts для установленных Windows8.0.31 packs запрещают reuse.
Перед сравнениями задайте `PLAYWRIGHT_BROWSERS_PATH` на существующий project-local
browser cache, `TEMP`/`TMP` внутрь Benchmarks.Local; автоматического download нет.
CLI принимает четыре sites/labels и одинаковый frozen source root. Подтверждение
через `parseArgs`/`runStudy`: `config.profiles=["low","high"]`, `config.modes=["core"]`,
2400 ticks,600 phase ticks,3600 warmup ticks, sites A/C/D. Версия основного harness
сохранена с первоначальным hash.

Приняты изоляция артефактов, аудит команд, независимые повторы, statistical/trajectory
guards, профилирование. B/C/D как defaults, слепое удаление интерпретатора, fast-math
и ослабление safety отклонены. IL stripping/compression/удаление зависимостей не
были CPU-экспериментами; Brotli - только loading cost. Worker не исследовался.
Другие машины/браузеры, сетевой cold start, настоящий RAF/GPU, более длинные
многоволновые бои, .NET10 Mono и точная атрибуция opaque thunks остаются неизмеренными.

## Проверки

Штатный локальный publish прошёл с реальными -Oz/-O2 response-файлами. Браузерные
проверки прошли: три профиля, бой, reset parity, управление, responsive Canvas,
realtime pause/resume. Скриншоты стандартного action-клиента на тиках180/360 и
обычного UI на тике600 просмотрены: ассеты загружены, корабли движутся, есть
попадания и warp, colliders off, page errors нет. На тике360: SelfHits0,
friendlyFireHits3201, debrisHits3975. Это проверки, не throughput samples.

32 Node-теста прошли, один real-junction пропущен из-за sandbox-запрета создания
(mock metadata rejection прошёл). Шесть Python-тестов, parsing трёх PowerShell
scripts и пять pre-build path guards прошли. Docs checker:58 bilingual documents,
1606 local links/anchors, numeric/code parity. Hashes runtime source/config
совпадают с frozen input; изменены только ссылки Web README. CI изменён локально,
не запускался.
