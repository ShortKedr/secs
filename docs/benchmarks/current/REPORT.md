English | [Русский](REPORT.ru.md)

# ECS comparison — 2026-10-14

[Benchmark overview](../../../SKECS.Benchmarks/README.md) | [Harness and reproduction](../../../Benchmarks.Local/README.md)

[Complete SECS suites and API measurements](SUPPLEMENT.md) include 34 current and 51 legacy BenchmarkDotNet cases, plus creation/accessor and retained-storage diagnostics.

This report predates the SKECS rename. Its `SECS` framework labels, measurements,
raw files, chart labels, and source hashes are preserved as the identity of the
measured build; they have not been relabelled as SKECS results.

All five frameworks were measured afresh in one sequential campaign: 40 base cases and 70 stress cases. Each cell below is mean milliseconds per complete operation; lower is better. Markers show relative lag within each scenario and entity-count row; they describe effect size, not statistical significance.

Environment: .NET 8.0.32, X64, AMD Ryzen 9 9900X 12-Core Processor, 24 logical processors. Release, one thread, tiered compilation off. Base repository commit: `ed0f4841a3b597e8f7ee7251b3d7e982b43d676a`; working tree modified: `True`. [Campaign metadata](campaign.json) pins the measured source and assembly hashes; [base raw samples](base.json) and [stress raw samples](stress.json) retain all observations.

## Framework revisions used

Third-party rows show exact upstream commits, not guessed release numbers. The SECS row shows the campaign's repository revision; [campaign metadata](campaign.json) also records hashes of the measured source and assemblies. Third-party commits come from the hash-verified [vendor manifest](../../../Benchmarks.Local/vendor-manifest.json).

StaticEcs is release **2.2.9**. Its compiled StaticPack dependency is **1.2.6**, pinned to commit `7bfb6cf925901b9366dda884258d727a89fff76e`. The manifest records SHA-256 for every included upstream source file.

| Framework | Source revision |
| --- | --- |
| SECS | `ed0f4841a3b597e8f7ee7251b3d7e982b43d676a` |
| LeoECS Lite | `5053141d7d663c6b1855c8409ac93dfac807b239` |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` |
| StaticEcs | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` |

## Recorded means

| Scenario | N | SECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Churn | 1,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0865 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.009289 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0164 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0234 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.006301 |
| Create | 1,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0933 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0257 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0382 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0324 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0117 |
| Destroy | 1,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0237 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0292 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0431 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0188 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0107 |
| Iterate | 1,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.000580 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.001260 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.001259 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.003193 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.001867 |
| Churn | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 9.469 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.9863 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 1.624 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.031 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.4292 |
| Create | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 9.376 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 4.782 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 9.625 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 8.872 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.081 |
| Destroy | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.469 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.503 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.887 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 1.644 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.036 |
| Iterate | 100,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0577 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1320 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1308 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.3595 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1897 |

| Scenario | N | SECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DenseIteration | 100,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.1087 | ![Lag between 5% and 30%](../heatmap/bad.svg "Lag between 5% and 30%") 0.1355 | ![Lag between 5% and 30%](../heatmap/bad.svg "Lag between 5% and 30%") 0.1374 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.5169 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1914 |
| MixedFrames | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 184.382 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 81.523 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 134.968 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 143.663 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 82.236 |
| PayloadChurn | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 42.027 | ![Lag between 5% and 30%](../heatmap/weak.svg "Lag between 5% and 30%") 7.816 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 19.613 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 17.777 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 6.509 |
| RandomLookup | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 1.572 | ![Lag between 5% and 30%](../heatmap/weak.svg "Lag between 5% and 30%") 0.2655 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.9977 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.7551 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.2233 |
| Recycle10Percent | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.927 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.301 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.959 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.643 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.209 |
| SparseIteration | 100,000 | ![Lag between 5% and 30%](../heatmap/poor.svg "Lag between 5% and 30%") 0.002445 | ![Lag between 5% and 30%](../heatmap/bad.svg "Lag between 5% and 30%") 0.002509 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.001995 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.007623 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.009072 |
| WideIteration | 100,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.2627 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.8067 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.4842 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 4.780 | ![Lag between 5% and 30%](../heatmap/strong.svg "Lag between 5% and 30%") 0.2818 |
| DenseIteration | 1,000,000 | ![Lag between 5% and 30%](../heatmap/fair.svg "Lag between 5% and 30%") 1.689 | ![Lag between 5% and 30%](../heatmap/good.svg "Lag between 5% and 30%") 1.679 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.511 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 9.003 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.698 |
| MixedFrames | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 4313.376 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2410.164 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 4797.842 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 4644.283 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1853.528 |
| PayloadChurn | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 618.734 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 268.362 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 512.311 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 338.901 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 115.487 |
| RandomLookup | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 55.936 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 5.184 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 65.364 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 31.836 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 7.674 |
| Recycle10Percent | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 67.392 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 76.111 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 94.969 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 79.598 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 48.597 |
| SparseIteration | 1,000,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0132 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0282 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0230 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0922 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1623 |
| WideIteration | 1,000,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 8.804 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 30.760 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 17.444 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 101.302 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 8.900 |

### Colour scale

![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") up to 5% · ![Lag between 5% and 30%](../heatmap/middle.svg "Lag between 5% and 30%") 15% · ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") from 30% · ![Neutral or insufficient separation](../heatmap/equal.svg "Neutral or insufficient separation") neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../COLORS.md).


## Charts and interpretation

![Base comparison](comparison.png)

![Stress at 100,000 entities](stress-100k.png)

![Stress at 1,000,000 entities](stress-1m.png)

Error bars show sample standard deviation, not confidence intervals. Base timings retain 20 samples; stress retains 8. Setup, validation, disposal and forced GC are excluded; natural collections and Morpeh commits during operations are included. Create/Destroy use fresh worlds; other workloads reuse warmed storage. MixedFrames is a complete 120-frame batch, not one frame. Allocation fields measure managed thread allocations and exclude native memory.

Base validation passed for all five frameworks at 1,000/100,000 entities before timing. Stress validation passed at 257/4,097 entities (all seven scenarios repeated three times, payload round-trip, retired handles); each measured stress case also passed final full-population validation. Exact matrix keys, sample counts and mean/standard-deviation consistency were checked before report generation.

These are equivalent logical operations through the selected adapters, not identical storage or each framework's best possible API. Results apply to this machine, runtime, snapshots and workload. They do not establish a universal ECS ranking, Unity/IL2CPP performance, total-memory efficiency or cross-platform determinism. The complete samples expose variability; close means should not be treated as reliable wins without further experiments.

## Retained history

Previous five-framework runs: [base](../../../Benchmarks.Local/results-all-updated.json), [stress](../../../Benchmarks.Local/stress-all-updated.json), [charts](../updated/comparison.png). The separate [SECS optimization experiment](../optimization-methodology.md) uses preserved historical binaries. Neither is the current campaign or evidence of today's before/after improvement.
