English | [Русский](REPORT.ru.md)

# ECS comparison — 2026-10-13

[Benchmark overview](../../../SECS.Benchmarks/README.md) | [Harness and reproduction](../../../Benchmarks.Local/README.md)

All five frameworks were measured afresh in one sequential campaign: 40 base cases and 70 stress cases. Each cell below is mean milliseconds per complete operation; lower is better. Markers show relative lag within each scenario and entity-count row; they describe effect size, not statistical significance.

Environment: .NET 8.0.31, X64, AMD Ryzen 9 9900X 12-Core Processor, 24 logical processors. Release, one thread, tiered compilation off. Source revision: `07e7ad04a18a15bfe24c130426fa8f2f2352b981`. [Campaign metadata](campaign.json) pins source and measured assembly hashes; [base raw samples](base.json) and [stress raw samples](stress.json) retain all observations.

## Recorded means

| Scenario | N | SECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Churn | 1,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0887 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0110 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0185 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0206 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.005180 |
| Create | 1,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0983 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0352 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0468 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0406 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0197 |
| Destroy | 1,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0383 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0402 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0401 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0298 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0214 |
| Iterate | 1,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.000609 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.001455 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.001342 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.003515 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.002121 |
| Churn | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 9.558 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 1.062 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 1.837 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.416 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.5918 |
| Create | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 10.283 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 6.206 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 11.546 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 7.502 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.442 |
| Destroy | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.686 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.310 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 4.007 | ![Lag between 5% and 30%](../heatmap/middle.svg "Lag between 5% and 30%") 1.864 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.627 |
| Iterate | 100,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0644 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1378 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1457 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.3268 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.2265 |

| Scenario | N | SECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DenseIteration | 100,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.1202 | ![Lag between 5% and 30%](../heatmap/weak.svg "Lag between 5% and 30%") 0.1426 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1868 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.5659 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1917 |
| MixedFrames | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 217.026 | ![Lag between 5% and 30%](../heatmap/strong.svg "Lag between 5% and 30%") 87.809 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 151.712 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 160.670 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 82.631 |
| PayloadChurn | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 57.529 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 11.021 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 29.352 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 17.951 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 11.287 |
| RandomLookup | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 1.005 | ![Lag between 5% and 30%](../heatmap/poor.svg "Lag between 5% and 30%") 0.3232 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 1.150 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.6952 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.2638 |
| Recycle10Percent | 100,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 5.618 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.223 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.725 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3.734 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.262 |
| SparseIteration | 100,000 | ![Lag between 5% and 30%](../heatmap/strong.svg "Lag between 5% and 30%") 0.002599 | ![Lag between 5% and 30%](../heatmap/middle.svg "Lag between 5% and 30%") 0.002829 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.002423 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.009157 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0103 |
| WideIteration | 100,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.3323 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.9676 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.6100 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 6.555 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.3178 |
| DenseIteration | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.525 | ![Lag between 5% and 30%](../heatmap/good.svg "Lag between 5% and 30%") 1.689 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 1.533 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 13.270 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 2.693 |
| MixedFrames | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 5695.914 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 3192.232 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 5547.053 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 5332.101 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 2061.327 |
| PayloadChurn | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 726.183 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 271.415 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 662.370 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 387.762 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 149.253 |
| RandomLookup | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 67.159 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 11.325 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 79.049 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 47.062 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 7.124 |
| Recycle10Percent | 1,000,000 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 69.020 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 84.428 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 90.916 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 90.762 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 44.853 |
| SparseIteration | 1,000,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 0.0140 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0447 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.0330 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1086 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 0.1726 |
| WideIteration | 1,000,000 | ![Lag up to 5%](../heatmap/best.svg "Lag up to 5%") 8.713 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 38.970 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 20.625 | ![Lag from 30%](../heatmap/worst.svg "Lag from 30%") 131.571 | ![Lag between 5% and 30%](../heatmap/strong.svg "Lag between 5% and 30%") 9.412 |

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
