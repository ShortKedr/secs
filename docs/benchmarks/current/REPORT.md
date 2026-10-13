English | [Русский](REPORT.ru.md)

# ECS comparison — 2026-10-14

[Benchmark overview](../../../SECS.Benchmarks/README.md) | [Harness and reproduction](../../../Benchmarks.Local/README.md)

[Complete SECS suites and API measurements](SUPPLEMENT.md) include 34 current and 51 legacy BenchmarkDotNet cases, plus creation/accessor and retained-storage diagnostics.

All five frameworks were measured afresh in one sequential campaign: 40 base cases and 70 stress cases. Each cell below is mean milliseconds per complete operation; lower is better. Markers show relative lag within each scenario and entity-count row; they describe effect size, not statistical significance.

Environment: .NET 8.0.31, X64, AMD Ryzen 9 9900X 12-Core Processor, 24 logical processors. Release, one thread, tiered compilation off. Source revision: `80fd4f7f8d93e2a2d665d3024c615991e34199b7`. [Campaign metadata](campaign.json) pins source and measured assembly hashes; [base raw samples](base.json) and [stress raw samples](stress.json) retain all observations.

## Recorded means

| Scenario | N | SECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Churn | 1,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0861 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.009847 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0158 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0202 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.006209 |
| Create | 1,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0984 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0269 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0413 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0313 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0151 |
| Destroy | 1,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0268 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0312 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0389 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0220 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0141 |
| Iterate | 1,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.000580 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.001280 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.001247 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.003237 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.001878 |
| Churn | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 9.369 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.9783 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 1.651 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.300 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.5273 |
| Create | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 11.061 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 4.645 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 10.698 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 6.558 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.295 |
| Destroy | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.467 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.159 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.607 | ![Lag between 5% and 30%](../heatmap/bad.svg "Lag between 5% and 30%") 1.710 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.333 |
| Iterate | 100,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0643 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1330 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1303 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.3310 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.2037 |

| Scenario | N | SECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DenseIteration | 100,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.1112 | ![Lag between 5% and 30%](../heatmap/poor.svg "Lag between 5% and 30%") 0.1350 | ![Lag between 5% and 30%](../heatmap/weak.svg "Lag between 5% and 30%") 0.1304 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.5307 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1958 |
| MixedFrames | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 189.360 | ![Lag between 5% and 30%](../heatmap/good.svg "Lag between 5% and 30%") 84.752 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 138.423 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 150.057 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 76.553 |
| PayloadChurn | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 41.224 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 7.615 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 26.706 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 15.860 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 7.695 |
| RandomLookup | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.9334 | ![Lag between 5% and 30%](../heatmap/middle.svg "Lag between 5% and 30%") 0.2589 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.9805 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.7402 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.2261 |
| Recycle10Percent | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 4.045 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.694 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.875 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.622 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.238 |
| SparseIteration | 100,000 | ![Lag between 5% and 30%](../heatmap/bad.svg "Lag between 5% and 30%") 0.002689 | ![Lag between 5% and 30%](../heatmap/strong.svg "Lag between 5% and 30%") 0.002298 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.002149 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.007807 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0101 |
| WideIteration | 100,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.3019 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.9230 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.4645 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 5.688 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.2891 |
| DenseIteration | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.409 | ![Lag between 5% and 30%](../heatmap/good.svg "Lag between 5% and 30%") 1.601 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.450 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 8.901 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.527 |
| MixedFrames | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 5237.207 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2671.732 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 5147.027 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 5012.136 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1716.710 |
| PayloadChurn | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 648.555 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 253.369 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 543.099 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 358.604 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 134.177 |
| RandomLookup | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 54.442 | ![Lag between 5% and 30%](../heatmap/middle.svg "Lag between 5% and 30%") 6.974 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 71.819 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 38.755 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 6.026 |
| Recycle10Percent | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 68.378 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 75.179 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 88.666 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 82.024 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 40.614 |
| SparseIteration | 1,000,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.009442 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0308 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0328 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1149 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1590 |
| WideIteration | 1,000,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 8.646 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 33.707 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 19.938 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 111.960 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 8.866 |

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
