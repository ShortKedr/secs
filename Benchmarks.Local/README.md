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

## Current campaign

The canonical [2026-10-13 report](../docs/benchmarks/current/REPORT.md) links [base raw samples](../docs/benchmarks/current/base.json), [stress raw samples](../docs/benchmarks/current/stress.json), [source/runtime metadata](../docs/benchmarks/current/campaign.json) and three charts. This is the complete fresh five-framework matrix. Files in the directory root and `docs/benchmarks/updated` are retained history; they are not the current ranking.

## Reproduce

Run from the repository root, with .NET 8 runtime, a suitable SDK and the existing local NuGet cache. This offline command builds Release, validates all adapters, records source/assembly hashes, and runs base then stress with one measurement process at a time:

```powershell
./Benchmarks.Local/run_campaign.ps1 -OutputDirectory Benchmarks.Local/repro-campaign
```

Stop other benchmarks, game runtimes and builds before timing. `--no-restore` deliberately requires cached dependencies; if absent, populate them separately before measurement. `GeneratePackageOnBuild=false` avoids package generation. The script keeps .NET/NuGet/temp paths inside the project and disables tiering. `-SkipBuild` is intended only for a separately verified fresh Release build. Reproduction writes its own results and does not overwrite canonical published data.

Plot after all measurement processes finish. The plotting scripts require Python with compatible matplotlib/NumPy installations; use the same Python version as compiled packages in the local cache (currently Python 3.12):

```powershell
$env:PYTHONPATH = "$PWD/Benchmarks.Local/python-packages"
$env:MPLCONFIGDIR = "$PWD/Benchmarks.Local/matplotlib-cache"
python Benchmarks.Local/plot.py Benchmarks.Local/repro-campaign/base.json -o Benchmarks.Local/repro-campaign/comparison.png
python Benchmarks.Local/plot_stress.py Benchmarks.Local/repro-campaign/stress.json --output-dir Benchmarks.Local/repro-campaign
```

Standalone correctness checks after building:

```powershell
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --validate
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --validate-stress
```

Base validation checks entity data and lifetime at both measured entity counts. Stress preflight uses 257/4,097 entities, seven scenarios repeated three times, payload round-trip and retired-handle checks. Every measured stress case also validates its full population after warmup and samples. These checks validate this repository's workload/adapter contract; they are not complete upstream test suites.

## All SECS suites

The [supplemental campaign](../docs/benchmarks/current/SUPPLEMENT.md) covers every default SECS BenchmarkDotNet case, the legacy suite and direct API/storage diagnostics. Run `./Benchmarks.Local/run_supplement.ps1`, then `python Benchmarks.Local/report_supplement.py docs/benchmarks/current`. The runner uses the same project-local cache/temp paths and runs each workload sequentially; `supplement.json` records source and input hashes.

## Retained experiments

The [SECS optimization methodology](../docs/benchmarks/optimization-methodology.md) explains preserved baseline/optimized binaries and paired datasets. `results-all-updated.json` / `stress-all-updated.json` and `docs/benchmarks/updated` are the previous complete five-framework run. Other `results*.json`, storage/accessor experiments and BenchmarkDotNet smoke outputs retain their own metadata and scope. Do not mix competitor observations across campaigns or use Dry/smoke timings as a full performance ranking.

BenchmarkDotNet launch instructions are in [SECS.Benchmarks](../SECS.Benchmarks/README.md). The separate end-to-end simulation and its reproduction live in the [SpaceBattle report](../docs/space-battle/REPORT.md).
