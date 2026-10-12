[English](README.md) | Русский

# Исследование Аппаратных Оптимизаций

Только локальная диагностика. Не перезаписывайте замороженные бенчмарки и не публикуйте сборки. До правок сохраните текущие исходники, включая незакоммиченные изменения, и Release DLL в новый каталог `Benchmarks.Local/tmp`. `StudyAssemblies` собирает одинаковый harness против сохранённых DLL, не пересобирая их.

```powershell
$root = (Get-Location).Path
$env:DOTNET_CLI_HOME = "$root/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$root/Benchmarks.Local/nuget-packages"
dotnet build tools/HardwareStudy/HardwareStudy.csproj -c Release `
  --artifacts-path Benchmarks.Local/tmp/my-study-build `
  "-p:StudyAssemblies=$root/Benchmarks.Local/tmp/my-preserved-dlls" -p:NuGetAudit=false
dotnet Benchmarks.Local/tmp/my-study-build/bin/HardwareStudy/release/HardwareStudy.dll `
  --game --repeats 5 --output Benchmarks.Local/tmp/my-new-study.json
python tools/HardwareStudy/compare.py Benchmarks.Local/tmp/my-before.json Benchmarks.Local/tmp/my-after.json
```

Без `StudyAssemblies` используются ссылки на текущие проекты. Не заменяйте DLL сохранённой базы новыми. Выходной файл должен быть новым и внутри проекта. `--port` выбирает `secs`, `lite`, `classic`, `morpeh`, `static` или `all`. `--seed`, `--ships`, `--churn`, `--ticks`, `--phase-ticks` и `--repeats` задают одинаковые нагрузки.

`--game` соответствует nominal фронтенда: LightSwarm, бесконечный доварп, без замены носителей. Без него Uniform остаётся отдельным регрессионным сценарием. Прогрев выполняется отдельным полным replay. Измеряемые независимые бои начинаются с нуля. После каждой фазы должны совпадать checksum, все игровые/AI-счётчики и средние/конечные популяции. Capture(false) измеряется отдельно, не вычитается из Step. Аллокации native Step, managed heap и native storage bytes различаются; метаданные адаптера/snapshot находятся в managed памяти и не входят в native storage totals.

`--physics-timings` включает часы contact-candidates/solver/hit-candidates/narrow-phase. Они имеют накладные расходы; нельзя сравнивать инструментированные и обычные прогоны. `--store-probes` запускает native микротесты SECS accessor и конечного архетипа на новых мирах вместо боёв. Они не доказывают end-to-end ускорение и не меняют игровые счётчики. Количество строковых операций рождения установлено чтением кода, а не инструментированием copy bytes/repairs.

Браузерные измерения требуют production-сборок стабильного .NET 8 Mono AOT и выполняются через `tools/web/profile-revisions.mjs --sites <comma-separated-wwwroot-directories> --labels <matching-labels> --repeats 3 --output <new-project-local-json>`. Порядок версий чередуется; прогрев идёт отдельными replay на 600 тиков. Step, export/interop, export в конце фазы, игровые/AI-счётчики, популяции, линейная WASM-память и telemetry-managed heap разделены. `--physics-timings` включает те же отдельные часы. Чистый Canvas здесь не измеряется: фронтендные пробы запускаются отдельно, разные окна не вычитаются друг из друга. Локальная диагностика Mono interpreter поддерживается, но не заменяет production AOT измерения.

Во время измерений не запускайте сборки и другие пробы. Показывайте медианы и диапазоны средних независимых прогонов, а не самые быстрые тики; неконтролируемая фоновая активность desktop остаётся ограничением. Native/browser checksum сравниваются внутри одного runtime/config, это не доказательство межплатформенной битовой детерминированности.

```powershell
python -m unittest discover -s tools/HardwareStudy -p test_compare.py
python tools/HardwareStudy/compare.py --browser Benchmarks.Local/tmp/my-browser.json
```
