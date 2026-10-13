English | [Русский](REPORT.ru.md)

# ECS comparison — 2026-10-13

[Benchmark overview](../../../SECS.Benchmarks/README.md) | [Harness and reproduction](../../../Benchmarks.Local/README.md)

All five frameworks were measured afresh in one sequential campaign: 40 base cases and 70 stress cases. Each cell below is mean milliseconds per complete operation; lower is better. Bold marks the lowest recorded mean in that row, without claiming statistical significance.

Environment: .NET 8.0.31, X64, AMD Ryzen 9 9900X 12-Core Processor, 24 logical processors. Release, one thread, tiered compilation off. Source revision: `07e7ad04a18a15bfe24c130426fa8f2f2352b981`. [Campaign metadata](campaign.json) pins source and measured assembly hashes; [base raw samples](base.json) and [stress raw samples](stress.json) retain all observations.

## Recorded means

| Scenario | N | SECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Churn | 1,000 | 0.0887 | 0.0110 | 0.0185 | 0.0206 | **0.005180** |
| Create | 1,000 | 0.0983 | 0.0352 | 0.0468 | 0.0406 | **0.0197** |
| Destroy | 1,000 | 0.0383 | 0.0402 | 0.0401 | 0.0298 | **0.0214** |
| Iterate | 1,000 | **0.000609** | 0.001455 | 0.001342 | 0.003515 | 0.002121 |
| Churn | 100,000 | 9.558 | 1.062 | 1.837 | 2.416 | **0.5918** |
| Create | 100,000 | 10.283 | 6.206 | 11.546 | 7.502 | **1.442** |
| Destroy | 100,000 | 2.686 | 3.310 | 4.007 | 1.864 | **1.627** |
| Iterate | 100,000 | **0.0644** | 0.1378 | 0.1457 | 0.3268 | 0.2265 |

| Scenario | N | SECS | LeoECS Lite | LeoECS Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DenseIteration | 100,000 | **0.1202** | 0.1426 | 0.1868 | 0.5659 | 0.1917 |
| MixedFrames | 100,000 | 217.026 | 87.809 | 151.712 | 160.670 | **82.631** |
| PayloadChurn | 100,000 | 57.529 | **11.021** | 29.352 | 17.951 | 11.287 |
| RandomLookup | 100,000 | 1.005 | 0.3232 | 1.150 | 0.6952 | **0.2638** |
| Recycle10Percent | 100,000 | 5.618 | 3.223 | 3.725 | 3.734 | **1.262** |
| SparseIteration | 100,000 | 0.002599 | 0.002829 | **0.002423** | 0.009157 | 0.0103 |
| WideIteration | 100,000 | 0.3323 | 0.9676 | 0.6100 | 6.555 | **0.3178** |
| DenseIteration | 1,000,000 | 2.525 | 1.689 | **1.533** | 13.270 | 2.693 |
| MixedFrames | 1,000,000 | 5695.914 | 3192.232 | 5547.053 | 5332.101 | **2061.327** |
| PayloadChurn | 1,000,000 | 726.183 | 271.415 | 662.370 | 387.762 | **149.253** |
| RandomLookup | 1,000,000 | 67.159 | 11.325 | 79.049 | 47.062 | **7.124** |
| Recycle10Percent | 1,000,000 | 69.020 | 84.428 | 90.916 | 90.762 | **44.853** |
| SparseIteration | 1,000,000 | **0.0140** | 0.0447 | 0.0330 | 0.1086 | 0.1726 |
| WideIteration | 1,000,000 | **8.713** | 38.970 | 20.625 | 131.571 | 9.412 |

## Charts and interpretation

![Base comparison](comparison.png)

![Stress at 100,000 entities](stress-100k.png)

![Stress at 1,000,000 entities](stress-1m.png)

Error bars show sample standard deviation, not confidence intervals. Base timings retain 20 samples; stress retains 8. Setup, validation, disposal and forced GC are excluded; natural collections and Morpeh commits during operations are included. Create/Destroy use fresh worlds; other workloads reuse warmed storage. MixedFrames is a complete 120-frame batch, not one frame. Allocation fields measure managed thread allocations and exclude native memory.

Base validation passed for all five frameworks at 1,000/100,000 entities before timing. Stress validation passed at 257/4,097 entities (all seven scenarios repeated three times, payload round-trip, retired handles); each measured stress case also passed final full-population validation. Exact matrix keys, sample counts and mean/standard-deviation consistency were checked before report generation.

These are equivalent logical operations through the selected adapters, not identical storage or each framework's best possible API. Results apply to this machine, runtime, snapshots and workload. They do not establish a universal ECS ranking, Unity/IL2CPP performance, total-memory efficiency or cross-platform determinism. The complete samples expose variability; close means should not be treated as reliable wins without further experiments.

## Retained history

Previous five-framework runs: [base](../../../Benchmarks.Local/results-all-updated.json), [stress](../../../Benchmarks.Local/stress-all-updated.json), [charts](../updated/comparison.png). The separate [SECS optimization experiment](../optimization-methodology.md) uses preserved historical binaries. Neither is the current campaign or evidence of today's before/after improvement.
