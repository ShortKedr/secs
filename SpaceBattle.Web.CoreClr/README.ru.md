[English](README.md) | Русский

# Экспериментальный Веб-Хост CoreCLR

[Основной хост Mono AOT](../SpaceBattle.Web/README.ru.md) | [Отчёт Space Battle](../docs/space-battle/REPORT.ru.md)

Этот вариант подключает ту же C#-симуляцию, адаптер SECS, бинарный мост,
Canvas-рендер и изображения. Игровая логика не переписывается на JavaScript.
Десктоп и существующий Pages workflow остаются без изменений.

## Зафиксированный Toolchain

Windows x64, PowerShell 7, .NET SDK `11.0.100-rc.1.26425.128`, runtime и
crossgen2 `11.0.0-rc.1.26425.128`, соответствующий workload `wasm-tools` и Node 24.
Генератор статических roots использует только при сборке Mono.Cecil `0.11.6` (MIT).
SDK, workload-паки, NuGet-кэши и результаты сборок находятся в
`Benchmarks.Local`, исключённом из Git.

Microsoft обозначает [браузерный CoreCLR как экспериментальный](https://github.com/dotnet/runtime/blob/ab19415702aa8139d5369e47c73edb47343c34ad/docs/workflow/building/coreclr/wasm.md).
[Задача о диагностике R2R](https://github.com/dotnet/runtime/issues/132410)
также показывает нерешённые проблемы инструментов, а не гарантирует скорость.
Текущий `main` runtime относится к более новой разрабатываемой версии:
его targets не подмешиваются молча в зафиксированный SDK .NET 11.

Скачайте Windows x64 SDK ZIP из официального
[release manifest .NET 11](https://github.com/dotnet/core/blob/main/release-notes/11.0/releases.json),
сверьте SHA-512 с манифестом и распакуйте в
`Benchmarks.Local/tmp/dotnet11-coreclr`. Из корня репозитория:

```powershell
$root = $PWD.Path
$env:DOTNET_ROOT = "$root/Benchmarks.Local/tmp/dotnet11-coreclr"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_HOME = "$root/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$root/Benchmarks.Local/nuget-packages"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& "$env:DOTNET_ROOT/dotnet.exe" workload install wasm-tools --skip-manifest-update --temp-dir "$root/Benchmarks.Local/tmp/dotnet11-workload-downloads"
./tools/build-space-battle-coreclr.ps1 -NodeExecutable 'C:/Program Files/nodejs/node.exe'
```

Скрипт не принимает существующий каталог результата и другую версию SDK.
Для каждого сравнения задавайте отдельный `-OutputDirectory`. `-Interpreter`
собирает явно обозначенный IL-only контроль. `-AllowPartial` отключает проверку
горячих методов для диагностики: такой результат нельзя называть или публиковать
как проверенный AOT-апгрейд. Прямой `dotnet publish` является контролем с
интерпретатором, а не процедурой R2R.

## Компиляция и Доказательства

Штатный SDK RC1 отклоняет `PublishReadyToRun=true` для `browser-wasm` с
`NETSDK1095`. Отдельный [скрипт](../tools/build-space-battle-coreclr.ps1)
публикует и обрезает IL, запускает зафиксированный crossgen2 с `-O`,
`--targetos:browser --targetarch:wasm --obj-format:wasm` и подключает результаты
через существующий metadata hook SDK `R2RWebcilPath`.
SDK рассчитывает отпечатки ресурсов, SHA-256 integrity, размеры образов и таблиц
Webcil, а также сжатие. Правки runtime-пака и ручная перезапись boot-манифеста
не нужны. При компиляции и упаковке проверяются одинаковые хеши конечных DLL.

[Генератор статических roots](../tools/CoreClrR2RRoots/Program.cs) читает конечный
IL Core и подставляет `SecsStore` вместо типа хранилища боя. Детерминированный
MIBC содержит закрытые доступы к компонентам, процессоры запросов и их bridge-
циклы. Это **статические входные данные компиляции**, а не измеренный PGO,
веса сэмплов или правка игрового поведения. Несопоставленные и открытые
interface callsites останавливают сборку. Проверяются все соответствующие
store/query/processor bridge-специализации, а не случайный экспорт `ForEach`.
У зафиксированного WASM-компилятора падает `--map`: скрипт сохраняет журналы
`--verbose` и читает экспорты native-функций.

Аргументы и журналы компилятора, а также два MSBuild binlog сохраняются в
изолированном каталоге сборки. `r2r-inventory.json` проверяет байты упакованных
модулей, хеши, размеры таблиц и экспортированные WASM-функции. Покрытие компиляцией
необходимо, но **не доказывает**, что runtime выбрал именно эти функции,
а не fallback в интерпретатор. Строка режима моста обозначает конфигурацию сборки.

[Проверка Chromium](../tools/web/probe-coreclr-r2r.mjs) записывает CPU-профиль
и сопоставляет индексы WASM-функций с экспортами компилятора и native-символами.
Она отделяет кадры предкомпилированной симуляции от сэмплов `InterpExecMethod`.
Время этого запуска включает накладные расходы профилирования: это **не бенчмарк**.
Независимые запуски без инструментирования допустимы после проверки горячего
пути; сохраняйте checksum и игровые счётчики и не предполагаете одинаковую
float-траекторию разных runtime заранее.

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/Benchmarks.Local/tmp/playwright-browsers"
node tools/web/probe-coreclr-r2r.mjs Benchmarks.Local/tmp/web-coreclr-r2r/wwwroot Benchmarks.Local/tmp/coreclr-probe
node --test tools/web/test-coreclr-r2r.mjs
```

Флаг `--require-no-interpreter` отклоняет наблюдаемые листовые сэмплы
интерпретатора. Текущий эксперимент эту runtime-проверку не проходит. Даже
ноль сэмплов означал бы ограниченную выборку, а не универсальный запрет fallback.

## Текущая Проверка

IL-only контроль CoreCLR прошёл существующий полный браузерный checker: три
профиля, seeded reset parity, бой, управление, живую статистику и адаптивный
рендер десктопа и мобильного экрана. Все 53 обрезанные DLL компилируются в R2R
и упаковываются SDK RC1. Первый частичный прототип выполняет предкомпилированные
`Battle.Step` и физику, но автоматически не создаёт закрытые struct generic
специализации `SecsStore.ForEach` / `SECS.Query.ForEach`. В наблюдаемом CPU-профиле
доминировал `InterpExecMethod`: такой вариант не является ускорением.

Явные статические roots устраняют пробел компиляции запросов: 276 generic
interface IL sites разворачиваются в 104 roots, включая 15 циклов адаптера,
15 query-циклов, 15 processor bridges и 23 специализации `Get` / 23 `Set`.
Упакованный результат проходит полную статическую проверку. В CPU-стеке браузера
действительно исполняются предкомпилированные бой, физика, доступы к компонентам
и запросы. Однако **85.0% наблюдаемых листовых сэмплов всё ещё выполняют
`InterpExecMethod`**, с переходами внутри физических `Query` / `RayCast` и
обработки контактов. Это смешанный R2R, а не горячий путь без интерпретатора;
листовой сэмпл сам по себе не определяет каждый managed-метод, вызвавший fallback.

Три независимых запуска Chromium без профилировщика, с чередованием runtime:
nominal / seed 7331, отдельный прогрев 30 тиков, reset, измерение тиков 1-120:

| Runtime | Медиана среднего Core Step по запускам, мс | Диапазон средних, мс |
| --- | ---: | ---: |
| Mono .NET 8 AOT | 10.245 | 10.152-10.492 |
| CoreCLR .NET 11 RC1 R2R | 252.131 | 251.627-252.723 |

Во всех шести запусках совпадают checksum `905660D8DFD501EC`, количества
сущностей и боевые счётчики. Эксперимент примерно в 24.6 раза медленнее на этом
**коротком начальном участке**: это не оценка установившегося режима или всего
боя. Для этих измерений профилирование отключено. Локальные исходные данные:
`Benchmarks.Local/tmp/coreclr-r2r-comparison-short.json`; CPU-профиль:
`Benchmarks.Local/tmp/coreclr-r2r-rooted-probe`. Эти ignored-файлы не являются
опубликованными результатами бенчмарков. Ускорение, полное отсутствие fallback,
переход production и удалённая публикация не заявлены.
Исходные измерения бенчмарков и исторические отчёты не изменяются.

## Сервер и Лицензии

После успешной локальной публикации обслуживайте `wwwroot` обычным статическим
сервером. По умолчанию base равен `/`; `-BasePath /secs/` доступен для отдельно
разрешённой публикации. Сам скрипт ничего не публикует в сеть и не меняет
удалённое состояние.

```powershell
python -m http.server 8041 --directory Benchmarks.Local/tmp/web-coreclr-r2r/wwwroot
```

Пакет сохраняет общие notices Aether/Lucide/изображений и копирует inventories
зафиксированных пакетов .NET 11, лицензию и авторов Emscripten 6.0.2.
SDK, компиляторы и зависимости инструментов сборки в веб-сайт не включаются.
