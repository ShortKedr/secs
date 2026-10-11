English | [Русский](README.ru.md)

# SECS benchmarks

[Project overview](../README.md) | [Space Battle report](../docs/space-battle/REPORT.md)

## Latest Full Comparison

The updated run measures the optimized SECS working tree against all four
other frameworks afresh, using the pinned versions listed below. It does not
combine new SECS measurements with historical competitor measurements.
The shared workloads use the existing APIs, not SECS-only accessors or factories.

![Updated base comparison](../docs/benchmarks/updated/comparison.png)

![Updated stress comparison at 100,000 entities](../docs/benchmarks/updated/stress-100k.png)

![Updated stress comparison at 1,000,000 entities](../docs/benchmarks/updated/stress-1m.png)

Raw results: `Benchmarks.Local/results-all-updated.json` (40 cases, 20 samples
each) and `Benchmarks.Local/stress-all-updated.json` (70 cases, 8 samples each).
The stress suite was repeated without plotting during measurement; the initial
run is retained as `stress-all-updated-initial.json`. Historical charts below
remain unchanged. Error bars show one sample standard deviation.

```powershell
$env:DOTNET_TieredCompilation = '0'
$env:SECS_BENCHMARK_REVISION = 'optimized working tree (872e0df + runtime optimizations)'
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll Benchmarks.Local/results-all-updated.json
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --stress Benchmarks.Local/stress-all-updated.json
python Benchmarks.Local/plot.py Benchmarks.Local/results-all-updated.json -o docs/benchmarks/updated/comparison.png
python Benchmarks.Local/plot_stress.py Benchmarks.Local/stress-all-updated.json --output-dir docs/benchmarks/updated
```

## SECS BenchmarkDotNet Suite

Run the SECS-only BenchmarkDotNet suite in Release:

```powershell
dotnet run -c Release --project SECS.Benchmarks -- --filter '*ComparisonBenchmarks*'
```

`ComparisonBenchmarks` covers 1,000 and 100,000 entities. `Create` creates a
fresh batch with Position (two floats) and Velocity (two floats). `Iterate`
adds Velocity to Position. `Churn` adds Mass (one float) to every entity,
then removes it from every entity. `Destroy` destroys a populated batch.
World creation, handle allocation, seeding for iteration/churn/destruction,
and disposal are outside the measured operation. The create/destroy cases
reset their world for every BenchmarkDotNet iteration. SECS chunk capacity
is 256. Queries are prepared before measurement.

## Local comparison

![Five-framework comparison](../docs/benchmarks/comparison.png)

The chart compares the same logical workloads on SECS, LeoECS Lite,
LeoECS Classic, Morpeh, and StaticEcs. Values are nanoseconds per entity, lower is better.
Churn includes both the add and remove passes, not the time for one change.
Morpeh's deferred structural changes include `Commit()` after each pass.
The iteration case uses each library's normal typed query/filter API; it
does not imply identical memory layouts or query implementation.

The comparative runner is a local Stopwatch harness, not BenchmarkDotNet
output. It checks every entity's data and lifetime before timing, warms each
case for at least 400 ms, measures 20 samples, and reports mean and sample
standard deviation. Create/destroy measure one fresh batch per sample, with
full GC after untimed preparation; iteration/churn repeat the operation to
target 30 ms per sample (capped at 1,000 repetitions), with GC before each
sample. Preparation, disposal, and forced GC are excluded from timings;
collections triggered by the measured operation itself are included.
The chart's error bars show one standard deviation, not confidence intervals.
Release .NET 8, x64, single thread; tiered compilation is disabled for the
comparison run. Results characterize this machine and these fixed revisions,
not Unity/IL2CPP or all workloads. Allocations in local JSON are managed bytes
only; they exclude SECS native storage and must not be used as a total-memory
comparison.

Dependencies are pinned to the revisions referenced by the pre-existing
local benchmark checkout:

| Framework | Repository | Revision |
| --- | --- | --- |
| LeoECS Lite | https://github.com/Leopotam/ecslite | `5053141d7d663c6b1855c8409ac93dfac807b239` |
| LeoECS Classic | https://github.com/Leopotam/ecs | `1213c31c9ef8325d48e74a52b345c4db242d038b` |
| Morpeh | https://github.com/scellecs/morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` |
| StaticEcs 1.2.01 | https://github.com/Felid-Force-Studios/StaticEcs | `6323d710c22cfff70100fe3748de432cb5fc70c8` |
| StaticPack 1.0.6 | https://github.com/Felid-Force-Studios/StaticPack | `72ce8b993593e5cfd24c6a9289a4b132305ef493` |

`Benchmarks.Local/` contains the comparison runner, adapters, downloaded
sources, raw measurements, and plotting script. It is intentionally outside
the intended commit and is not added to ignore rules. The SECS-only suite
above remains independently buildable without those local dependencies.

Local reproduction from the repository root (matplotlib must be available):

```powershell
./Benchmarks.Local/fetch.ps1
dotnet build Benchmarks.Local/Comparison.csproj -c Release -p:GeneratePackageOnBuild=false
$env:DOTNET_TieredCompilation = '0'
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll Benchmarks.Local/results.json
python Benchmarks.Local/plot.py
```

Intended commit paths: benchmark-only changes under `SECS.Benchmarks/`,
`SECS.OtherBenchmarks/`, and PNG charts under `docs/benchmarks/`. Runtime source, local adapters, vendor
sources, and raw comparative results are not part of that change.

## Existing Coverage

Before stress additions, `SECS.Benchmarks` had two original scenarios at
100,000 entities (iteration and structural churn) plus four comparison
scenarios at 1,000/100,000 entities. `SECS.OtherBenchmarks` has 48 concrete
classes: 8 creation, 14 addition, 8 removal, 12 structural transitions,
1 deletion, 3 single-system, 1 multi-system, and 1 filter-mismatch class.
The systems use padding 0/10, giving 51 parameter combinations overall.
The declared 500,000 constant is not used by their parameter attributes.
Their component payloads are one integer; multi-system covers three systems,
and filter-mismatch covers 100 nonmatching two-component queries.

The old creation suite now has iteration setup to restrict BenchmarkDotNet
to one creation batch per iteration, preventing overwritten handles from
leaking entities during repeated invocations. Deletion retains its handle
array so later setup iterations continue to delete the requested batch.
Lifecycle regression validation:

```powershell
dotnet run -c Release --project SECS.OtherBenchmarks -- --validate
```

## Stress Coverage

The stress workload uses 256 base layouts from eight optional four-byte
components, Position/Velocity (12 bytes each), and a four-byte Hot component
on every hundredth entity. Hot adds further composition variants; "256"
describes the base shape masks, not the total number of SECS archetypes
created by intermediate structural transitions. All libraries receive the
same shape masks, initialization data, seeded random order, and operations.
Velocity is read in every movement path. Hot is a normal component, not a
library-specific zero-size tag optimization.

| Scenario | One Measured Operation |
| --- | --- |
| DenseIteration | Update all positions across the populated layouts |
| SparseIteration | Update the 1% Hot selection through a typed query |
| WideIteration | Update positions and the first/last fields of a 256-byte component |
| RandomLookup | Read all positions in a seeded shuffled order and return a checksum |
| PayloadChurn | Add then remove the 256-byte component on all entities in random order |
| Recycle10Percent | Destroy and recreate 10% in shuffled order, restoring their logical data and layout |
| MixedFrames | 120 consecutive frames: full movement, 5% payload add/remove, 10% reads, 1% recycle per frame |

MixedFrames repeats fixed seeded subsets, so it measures sustained localized
turnover, not uniform random replacement of the whole population. The
other randomized cases visit a full deterministic permutation. No RNG,
sorting, fixture seeding, validation, world disposal, or forced GC is timed.
Morpeh commits both payload passes and the recycle batch inside their
timings. SECS chunk capacity remains 256. Backend dispatch is once per
batch; per-entity access uses native typed APIs.

Run the SECS BenchmarkDotNet stress suite (14 cases):

```powershell
dotnet run -c Release --project SECS.Benchmarks -- --filter 'SECS.Benchmarks.StressBenchmarks.*'
```

An opt-in 5-million-entity profile is available via
`$env:SECS_STRESS_EXTREME = '1'`; remove that variable to return to the normal
100,000/1,000,000 profile. It is not part of the measured comparison below.
Only DenseIteration has been smoke-checked at 5 million; the full extreme
suite has not been run.
This scales entity counts rather than claiming to exhaust every failure mode.

The comparison has 70 cases (5 frameworks x 7 workloads x 2 entity counts),
8 retained samples each, >=300 ms warmup, and up to 16 repetitions targeting
30 ms per sample. MixedFrames always runs once per sample. A full GC runs
outside the timer before each sample. Natural GC and allocations triggered
by measured operations remain included. Validation checks every entity
after setup and after all invocations, plus smaller boundary workloads
(257 and 4,097 entities) repeated three times. It also checks payload data
at both ends and retired generation-aware handles. Managed allocation data
excludes native memory and is not a total-memory measurement.

Charts show milliseconds per complete operation, including all 120 frames
for MixedFrames; each panel has its own logarithmic scale. They are not
an overall architecture ranking. These are pinned library versions, not
claims about later StaticEcs 2.x or other future releases.

![Stress at 100,000 entities](../docs/benchmarks/stress-100k.png)
![Stress at 1,000,000 entities](../docs/benchmarks/stress-1m.png)

Local validation, timing, and chart reproduction:

```powershell
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --validate-stress
$env:DOTNET_TieredCompilation = '0'
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --stress Benchmarks.Local/stress-results.json
python Benchmarks.Local/plot_stress.py
```

## Runtime optimization checks

The five-framework charts above retain their original runtime measurements.
The optimized working tree has a separate paired comparison against the
preserved original SECS binary:

![Original versus optimized SECS](../docs/benchmarks/optimization.png)

See [optimization methodology and results](../docs/benchmarks/optimization-methodology.md)
and [runtime API documentation](../docs/SECS_OPTIMIZATION.md).
The old fixtures remain unchanged. New direct-creation and cached-accessor
APIs are measured separately, not substituted into the original comparison.

```powershell
dotnet run -c Release --project SECS.Benchmarks -- --filter '*OptimizedApi*'
$env:DOTNET_TieredCompilation = '0'
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --secs-only Benchmarks.Local/results-optimized.json
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --stress --secs-only Benchmarks.Local/stress-results-optimized.json
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --optimized-api Benchmarks.Local/optimized-api-results.json
dotnet Benchmarks.Local/bin/Release/net8.0/Comparison.dll --storage Benchmarks.Local/storage-optimized.json
python Benchmarks.Local/plot_optimization.py
```
