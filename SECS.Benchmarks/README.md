English | [Русский](README.ru.md)

# SECS benchmarks

[Project overview](../README.md) | [SpaceBattle report](../docs/space-battle/REPORT.md) | [Harness](../Benchmarks.Local/README.md)

## Current comparison

The [current campaign report](../docs/benchmarks/current/REPORT.md) contains a fresh comparison of SECS, LeoECS Lite, LeoECS Classic, Morpeh and StaticEcs: 40 base cases and 70 stress cases, with complete raw samples, source hashes and three charts. All five frameworks were measured again using the same shared workload contract.

![Base comparison](../docs/benchmarks/current/comparison.png)

The base matrix measures Create, Iterate, Churn and Destroy at 1,000 and 100,000 entities. Stress measures dense/sparse/wide iteration, random lookup, 256-byte payload churn, 10% recycling and a 120-frame mixed batch at 100,000 and 1,000,000 entities. The [report](../docs/benchmarks/current/REPORT.md) gives operation times and explains measurement limits. The [harness README](../Benchmarks.Local/README.md) documents exact scenarios and reproduction.

These are Stopwatch measurements of equivalent logical work through selected adapters. They characterize the recorded machine and snapshots; they do not establish a universal ranking or Unity/IL2CPP performance. Error bars show sample standard deviation. Managed allocations in JSON exclude native memory.

## SECS BenchmarkDotNet suites

[ComparisonBenchmarks](ComparisonBenchmarks.cs) uses the same [base workload](ComparisonWorkload.cs) as the five-framework runner. [StressBenchmarks](StressBenchmarks.cs) uses the same [stress workload](StressWorkload.cs). [OptimizedApiBenchmarks](OptimizedApiBenchmarks.cs) evaluates separate SECS factory/accessor APIs; these measurements are not substituted into the shared comparison. [Program.cs](Program.cs) also retains the original iteration/churn benchmarks. `SECS.OtherBenchmarks` contains a separate legacy suite.

BenchmarkDotNet here measures SECS only. For a quick launch check after a Release build:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"
dotnet build SECS.Benchmarks/SECS.Benchmarks.csproj -c Release --no-restore -p:GeneratePackageOnBuild=false -m:1
dotnet SECS.Benchmarks/bin/Release/net8.0/SECS.Benchmarks.dll --filter '*ComparisonBenchmarks.Iterate*' --job Dry --inProcess --artifacts Benchmarks.Local/repro-bdn-smoke
```

A Dry job checks launch and workload execution; its timings are not a performance ranking. The custom comparison campaign already covers the complete five-framework workload matrix. A full BenchmarkDotNet run is optional and should run separately from any other measurement process.

## Historical experiments

The [2026-10-11 optimization methodology](../docs/benchmarks/optimization-methodology.md) records a separate SECS before/after experiment and the preserved binaries required to interpret it. [Previous base](../Benchmarks.Local/results-all-updated.json) and [stress](../Benchmarks.Local/stress-all-updated.json) data remain available, alongside [previous charts](../docs/benchmarks/updated/comparison.png). They are retained history, not today's rating. Rebuilding the current source does not reproduce the old baseline binary.
