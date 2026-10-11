English | [Русский](README.ru.md)

# Local ECS Comparisons

[Repository](../README.md) | [Benchmark report](../SECS.Benchmarks/README.md) | [SpaceBattle report](../docs/space-battle/REPORT.md) | [Third-party notices](../THIRD_PARTY_NOTICES.md)

This directory contains the five-framework Stopwatch comparison harness, its adapters, stress backends, retained JSON measurements, and plotting scripts. BenchmarkDotNet suites live separately in `SECS.Benchmarks` and `SECS.OtherBenchmarks` and measure SECS, not all five frameworks.

## Source Catalogue

| Framework | Base workload implementation | Stress implementation | SpaceBattle native store | Runtime sources |
| --- | --- | --- | --- | --- |
| SECS | [SecsWorkload](Adapters.cs), wrapping [ComparisonWorkload](../SECS.Benchmarks/ComparisonWorkload.cs) | [SecsStressBackend](../SECS.Benchmarks/SecsStressBackend.cs) | [SecsStore](../SpaceBattle.SECS/SecsStore.cs) | [SECS](../SECS/) |
| LeoECS Lite | [LiteWorkload](Adapters.cs) | [LiteStressBackend](StressAdapters.cs) | [LeoEcsLiteStore](../SpaceBattle.Adapters/LeoEcsLiteStore.cs) | [ecslite/src](vendor/ecslite/src/) |
| LeoECS Classic | [ClassicWorkload](Adapters.cs) | [ClassicStressBackend](StressAdapters.cs) | [LeoEcsClassicStore](../SpaceBattle.Adapters/LeoEcsClassicStore.cs) | [ecs/src](vendor/ecs/src/) |
| Morpeh | [MorpehWorkload](Adapters.cs) | [MorpehStressBackend](StressAdapters.cs) | [MorpehStore](../SpaceBattle.Adapters/MorpehStore.cs) | [Morpeh Core](vendor/morpeh/Scellecs.Morpeh/Core/) |
| StaticEcs | [StaticWorkload](StaticAdapters.cs) | [StaticStressBackend](StaticAdapters.cs) | [StaticEcsStore](../SpaceBattle.Adapters/StaticEcsStore.cs) | [StaticEcs/Src](vendor/StaticEcs/Src/), [StaticPack/Src](vendor/StaticPack/Src/) |

Build definitions: [Comparison.csproj](Comparison.csproj), [StaticLibrary.csproj](StaticLibrary.csproj), [MorpehLibrary.csproj](MorpehLibrary.csproj), and [SpaceBattle.Adapters.csproj](../SpaceBattle.Adapters/SpaceBattle.Adapters.csproj). StaticPack supports StaticEcs; it is not a sixth compared framework. SpaceBattle stores share the [IEcsStore](../SpaceBattle.Core/EcsContract.cs) contract and [NativeStore](../SpaceBattle.Core/NativeStore.cs) identity layer; see the [adapter contract](../SpaceBattle.Adapters/README.md).

The [vendor manifest](vendor-manifest.json) records pinned sources; [check_vendor.py](../tools/check_vendor.py) verifies the distributed inventory. The distribution contains runtime source subsets and exact upstream licenses, not upstream documentation/assets, binary NuGet caches, or generated build products. A normal clone includes these source subsets and needs no vendor download. [fetch.ps1](fetch.ps1) is an optional manifest-driven recovery step requiring network access, not a normal build prerequisite.

Licenses are not uniformly unrestricted MIT: LeoECS Classic offers proprietary or MIT-Red terms, LeoECS Lite uses MIT-Red, and Morpeh, StaticEcs, and StaticPack use MIT. Read the [licensing inventory](../THIRD_PARTY_NOTICES.md) and exact [Classic](vendor/ecs/LICENSE.md), [Lite](vendor/ecslite/LICENSE.md), [Morpeh](vendor/morpeh/LICENSE.md), [StaticEcs](vendor/StaticEcs/LICENSE.md), and [StaticPack](vendor/StaticPack/LICENSE.md) licenses before redistribution. This catalogue is not a legal guarantee.

## Shared Workloads

[Program.cs](Program.cs) registers all five base adapters. At 1,000 and 100,000 entities it runs Create, Iterate, Churn, and Destroy: Position/Velocity each contain two floats; movement adds velocity to position; churn adds then removes a one-float Mass component. Validation checks every entity before timing. Setup, seeding, world disposal, and forced GC are outside timings; Morpeh commits are included. Each case warms for at least 400 ms and retains 20 samples. Create/Destroy use one fresh batch per sample; Iterate/Churn target 30 ms with at most 1,000 repetitions.

[StressRunner.cs](StressRunner.cs), [StressContract.cs](../SECS.Benchmarks/StressContract.cs), and [StressWorkload.cs](../SECS.Benchmarks/StressWorkload.cs) share initialized data, shape masks, and seeded operation orders across all five backends. The normal matrix is 100,000/1,000,000 entities, seven workloads, eight samples per case, at least 300 ms warmup, and up to 16 repetitions targeting 30 ms. MixedFrames uses one invocation per sample.

| Stress scenario | One operation |
| --- | --- |
| DenseIteration | Move all entities across populated layouts |
| SparseIteration | Move the 1% Hot selection |
| WideIteration | Move entities and update first/last fields of a 256-byte payload |
| RandomLookup | Read positions in seeded shuffled order and return a checksum |
| PayloadChurn | Add and remove the 256-byte payload in shuffled order |
| Recycle10Percent | Destroy/recreate 10%, restoring logical data and layouts |
| MixedFrames | 120 frames, each with full movement, 5% payload churn, 10% reads, 1% recycling |

Stress uses 256 base masks from eight optional four-byte components, 12-byte Position/Velocity, and four-byte Hot on every hundredth entity. Intermediate transitions and Hot can create additional archetypes. MixedFrames repeats fixed subsets; it is not uniform random population replacement. RNG, sorting, setup, validation, disposal, and forced GC are untimed. Natural GC caused by measured operations remains included.

Equivalent logical work does not mean identical layouts, queries, or optimal APIs. Allocation fields measure managed thread allocations, excluding native storage, not total memory. Error bars represent sample standard deviation, not confidence intervals. Results apply to the recorded machine/runtime/revisions, not Unity/IL2CPP or a universal framework ranking. SECS-only optimized creation/accessor experiments are separate and do not replace the shared API comparison.

## Validation Catalogue

| Entry point | Visible source and checks |
| --- | --- |
| Comparison `--validate` | [Program.cs](Program.cs), [Adapters.cs](Adapters.cs), [StaticAdapters.cs](StaticAdapters.cs): lifetime and data at both base entity counts for all five frameworks |
| Comparison `--validate-stress` | [StressRunner.cs](StressRunner.cs), [StressWorkload.cs](../SECS.Benchmarks/StressWorkload.cs): 257/4,097 entities, seven scenarios repeated three times, payload round-trip and retired handles |
| xUnit | [WorldTests](../SECS.Tests/WorldTests.cs), [DefaultWorldTests](../SECS.Tests/DefaultWorldTests.cs), [OptimizationTests](../SECS.Tests/OptimizationTests.cs), [AccessorSwapTests](../SECS.Tests/AccessorSwapTests.cs) |
| Legacy `--validate` | [SECS.OtherBenchmarks/Program.cs](../SECS.OtherBenchmarks/Program.cs), [BenchmarkInfrastructure.cs](../SECS.OtherBenchmarks/BenchmarkInfrastructure.cs): creation/deletion fixture lifecycle regressions |
| Headless `--validate-adapters` | [AdapterValidation.cs](../SpaceBattle.Adapters/AdapterValidation.cs): four competitor stores, component/query/ref semantics, mutation rejection, stale handles, 70,000 recycle iterations and StaticEcs lifetime/registration |
| Headless `--validate` / `--validate-ports` | [Validation.cs](../SpaceBattle.Headless/Validation.cs): SECS gameplay/physics checks; all four ports compared to SECS at ten checkpoints over 3,000 ticks |
| Report/document tests | [test_check_docs.py](../tools/test_check_docs.py), [test_report_locale.py](../tools/space-battle/test_report_locale.py), [test_composition_report.py](../tools/space-battle/test_composition_report.py) |
| Source inventory/recovery tests | [test_check_vendor.py](../tools/test_check_vendor.py) checks integrity and licenses; [test_fetch.ps1](../tools/test_fetch.ps1) tests offline archive recovery and preserves changed files |

These are repository workload/adapter checks, not complete upstream framework test suites. Assertions throw on failure; headless validation returns a nonzero exit code. Retained measurement JSON and BenchmarkDotNet smoke reports are not substitutes for a fresh test run.

On 2026-10-11, a separate copy containing only publishable files built the full
Release solution with no errors or warnings. All 16 xUnit tests, base and stress
validation for all five frameworks, the legacy lifecycle checks, and all three
headless validation modes passed. All five battle ports matched at ten replay
checkpoints. Historical timings and measured binaries were left unchanged; this
is a source-distribution validation, not a new performance campaign or legal certification.

## Reproduce Locally

Run PowerShell from the repository root. Prerequisites: Python for inventory checking, a .NET SDK supporting the projects' .NET 8 target/runtime, and available NuGet dependencies for xUnit, BenchmarkDotNet, and SpaceBattle physics. Plotting additionally needs Python with matplotlib and NumPy. Source vendors are included; package restore may still require network access or a populated local cache. Run restores/builds serially and measurements without competing workloads.

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

Stop on a nonzero exit code and investigate before timing. Fresh output names below preserve retained results; rerunning these examples overwrites their `repro-*` files.

To check the actual source distribution without changing measured build outputs,
run [verify-source-distribution.ps1](../tools/verify-source-distribution.ps1).
It copies only publishable files into an ignored project-local directory, builds
the solution, and runs xUnit, both comparison validations, legacy validation,
and all headless validation modes. It uses the project-local NuGet cache and may
need network access to restore missing packages; it does not publish anything.

```powershell
$env:DOTNET_TieredCompilation = '0'
Remove-Item Env:SECS_BENCHMARK_SCENARIOS -ErrorAction SilentlyContinue
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll Benchmarks.Local/repro-base.json
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --stress Benchmarks.Local/repro-stress.json
python Benchmarks.Local/plot.py Benchmarks.Local/repro-base.json -o Benchmarks.Local/repro-charts/comparison.png
python Benchmarks.Local/plot_stress.py Benchmarks.Local/repro-stress.json --output-dir Benchmarks.Local/repro-charts
```

`--secs-only` restricts either comparison to SECS; `SECS_BENCHMARK_SCENARIOS` is a comma-separated stress scenario filter. Clear it for the complete 70-case stress matrix. An optional `SECS_BENCHMARK_REVISION` label records provenance supplied by the caller; it does not verify a commit.

Run all SECS BenchmarkDotNet classes, including the original iteration/churn, shared SECS fixtures, stress, and optimized APIs, then the legacy suite:

```powershell
Remove-Item Env:SECS_STRESS_EXTREME -ErrorAction SilentlyContinue
dotnet run -c Release --project SECS.Benchmarks -- --filter '*' --artifacts Benchmarks.Local/repro-bdn
dotnet run -c Release --project SECS.OtherBenchmarks -- --filter '*' --artifacts Benchmarks.Local/repro-legacy-bdn
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --optimized-api Benchmarks.Local/repro-optimized-api.json
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --storage Benchmarks.Local/repro-storage.json
```

The [optimized API runner](OptimizedApiRunner.cs) validates separate SECS creation/lookup experiments; the [storage diagnostic](StorageRunner.cs) is untimed and reports retained native columns, excluding allocator overhead and managed arrays. `SECS_STRESS_EXTREME=1` selects five million entities only in the BenchmarkDotNet stress suite, not the five-framework runner. Only DenseIteration has retained extreme smoke evidence; it is not a full extreme-suite result.

SpaceBattle's [headless CLI](../SpaceBattle.Headless/Program.cs) accepts `--framework secs|lite|classic|morpeh|static`, `--ticks` or `--seconds`, `--warmup`, `--seed`, `--ships-per-team`, `--churn`, `--particles`, `--output`, and optional `--realtime`. A short sequential reproduction for every store:

```powershell
foreach ($framework in @('secs', 'lite', 'classic', 'morpeh', 'static')) {
    dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --framework $framework --ticks 3600 --warmup 600 --seed 7331 --ships-per-team 96 --output "Benchmarks.Local/repro-space-$framework.json"
    if ($LASTEXITCODE -ne 0) { throw "SpaceBattle failed: $framework" }
}
```

This is a short run, not reproduction of every retained long profile. Headless output includes JSON and `.ticks.csv`; its timed Step includes systems, physics, and commits, so it is not an ECS-only microbenchmark. See the [SpaceBattle report](../docs/space-battle/REPORT.md) for profile configurations and measurement limits.

## Retained Data Map

- Five-framework base: [results-all-updated.json](results-all-updated.json); stress: [stress-all-updated.json](stress-all-updated.json), with [initial stress run](stress-all-updated-initial.json) retained separately. Older runs remain distinct: [results.json](results.json), [stress-results.json](stress-results.json), [results-initial.json](results-initial.json), [results-four-frameworks.json](results-four-frameworks.json).
- Paired SECS history: [base baseline](results-baseline-repeat.json), [base optimized](results-optimized.json), [stress baseline](stress-baseline-repeat.json), [stress optimized](stress-results-optimized.json). [Methodology](../docs/benchmarks/optimization-methodology.md) explains the preserved-binary comparison; rebuilding current source does not recreate that historical binary. [plot_optimization.py](plot_optimization.py) consumes those paired inputs.
- Separate API/storage experiments: [optimized-api-results.json](optimized-api-results.json), [storage-baseline.json](storage-baseline.json), [storage-optimized.json](storage-optimized.json); iteration checks: [baseline](iteration-baseline-check.json), [hoist](iteration-hoist-check.json), [optimized](iteration-optimized-check.json).
- BenchmarkDotNet smoke evidence: [comparison report](bdn-smoke-inprocess/results/SECS.Benchmarks.ComparisonBenchmarks-report-github.md), [stress report](bdn-stress-smoke/results/SECS.Benchmarks.StressBenchmarks-report-github.md), [extreme report](bdn-extreme-smoke/results/SECS.Benchmarks.StressBenchmarks-report-github.md). Separate API reports: [creation](optimization-bdn-artifacts/results/SECS.Benchmarks.OptimizedApiCreationBenchmarks-report-github.md), [lookup](optimization-bdn-artifacts/results/SECS.Benchmarks.OptimizedApiLookupBenchmarks-report-github.md). These are not full-suite pass counts.
- SpaceBattle: [raw per-framework JSON/CSV](../docs/space-battle/results/), [summary.csv](../docs/space-battle/summary.csv), [windows.csv](../docs/space-battle/windows.csv), [hardware metadata](../docs/space-battle/hardware.json), and [report](../docs/space-battle/REPORT.md).

Inspect each result's metadata and completeness before comparing it. A filtered run, failed/smoke report, or older framework subset must not be presented as the complete current five-framework matrix.
