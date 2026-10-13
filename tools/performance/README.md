English | [Русский](README.ru.md)

# Space Battle Diagnostic Tools

The protocol below reproduces **2026-10-11** source snapshots (`6150700`, `c51390c`, `d853ae7`) with the earlier Aether physics. For current circle-kernel profiling use [HardwareStudy](../HardwareStudy/README.md) and the [physics tools](../physics/README.md).

[Research index](../../docs/space-battle/RESEARCH.md) | [Current campaign](../../docs/space-battle/REPORT.md)

These tools investigate simulation/front-end costs, independently of the frozen five-ECS results. See the [study](../../docs/space-battle/PERFORMANCE_INVESTIGATION.md).

Run from the repository root. Keep archives, builds, caches and output inside `Benchmarks.Local/tmp/performance-study`. Do not rebuild into canonical `bin/Release` or overwrite report results. The [experimental patch](sweep-empty-space.patch) is publishable as a research/reproduction artifact, not as a production fix. Never apply it to the production project or deploy it with production; apply it only to an isolated archive for reproduction as described below.

## Native

Export each revision locally with `git archive`, then extract using Windows' `C:\Windows\System32\tar.exe` (a standard OS tool, not a private project path). The study used `before=6150700`, `tactics=c51390c`, `stats=d853ae7`. Archive exports do not change Git checkout or remote state.

```powershell
$root = (Get-Location).Path
$env:DOTNET_CLI_HOME = "$root/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$root/Benchmarks.Local/nuget-packages"
dotnet build tools/performance/SpaceBattle.Profile.csproj -c Release `
  --artifacts-path "$root/Benchmarks.Local/tmp/performance-study/native-stats" `
  "-p:StudySourceRoot=$root/Benchmarks.Local/tmp/performance-study/stats" `
  -p:NuGetAudit=false
dotnet "$root/Benchmarks.Local/tmp/performance-study/native-stats/bin/SpaceBattle.Profile/release/SpaceBattle.Profile.dll" `
  --revision d853ae7 --output "$root/Benchmarks.Local/tmp/performance-study/native-stats.json"
```

Defaults: three independent nominal/7331 replays, 1,200 ticks, 600-tick reporting phases; separate 1,200-tick warmup. `--ticks`, `--phase-ticks` and `--repeats` override counts. Core timings exclude rendering, snapshots and telemetry. Counts/aggregation/checksums are outside Step. Small harness allocations can still indirectly affect GC; compare equivalent harnesses.

## Browser

Install the existing `tools/web` package and Chromium locally if absent. Use the project's pinned .NET SDK/wasm-tools and shared project-local NuGet cache. Publish each archived `SpaceBattle.Web.csproj` with `-c Release`, its own `--artifacts-path`, and its own output directory. Do not use `--Interpreter`.

The isolated browser harness needs this diagnostic method inside each **archived** `BattleBridge`:

```csharp
[JSExport]
public static double DiagnosticStage(int stage) => Current.LastStageMs[stage];
```

It reads existing timing values after Step; no gameplay logic is changed. Serve published `wwwroot` directories on three distinct localhost ports, with their HTML base path `/`. All three full AOT builds must finish before timing starts.

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/Benchmarks.Local/tmp/playwright-browsers"
node tools/web/profile-revisions.mjs `
  --urls http://127.0.0.1:8030/,http://127.0.0.1:8031/,http://127.0.0.1:8032/ `
  --labels before,tactics,stats
node tools/web/summarize-performance.mjs Benchmarks.Local/tmp/performance-study
```

Browser defaults: three independent replays per URL, 600-tick separate warmup, then fresh ticks 1-1,200 in 600-tick phases. It alternates URL order and rejects unexpected tick counts and runtime errors. It pauses drawing/DOM, times production `Advance(1)` separately from diagnostic reads, and records exact existing stage durations plus population/counter/checksum data. `--ticks`, `--phase-ticks`, `--repeats` and `--output` are configurable.

The summarizer checks checksum, population and gameplay-counter repeatability within each revision/runtime. It does not perform inferential significance testing and does not compare native/WASM checksums. Medians are across independent run means, not minima across individual ticks.

For a separate dense-phase/full-frontend probe:

```powershell
node tools/web/profile-web.mjs --url http://127.0.0.1:8032/ `
  --warmup-ticks 180 --output Benchmarks.Local/tmp/performance-study/web-live-1.json
```

This samples ticks 181-220, then export-only and forced UI at later states, followed by five seconds of live RAF. Those windows must not be subtracted or treated as identical workloads. Run it three times with fresh pages. Do not run builds or other profiling concurrently.

## Sweep Experiment

Use a fresh archived `d853ae7` copy. Apply the patch with an explicit **archive-only** directory, never the checkout root:

```powershell
git apply --check --directory=Benchmarks.Local/tmp/performance-study/stats tools/performance/sweep-empty-space.patch
git apply --directory=Benchmarks.Local/tmp/performance-study/stats tools/performance/sweep-empty-space.patch
```

Publish to a different output directory, keeping the original published baseline intact. AOT artifacts may be reused for the same archived project. Build the native harness against the patched archive into `native-sweep`, then invoke its DLL with `--verify-sweep`. `SweepUnfiltered` exists only in that experimental source for differential validation, and is unused/trim-eligible in the browser.

Use the browser harness with two URLs, `--labels stats,sweep` and a distinct `--output` path. Check original versus experimental phase checksums, populations and all gameplay counters, not only timing. This experiment is not an integrated production fix; more seeds and physics edge cases are required.
