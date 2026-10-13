English | [Русский](optimization-methodology.ru.md)

# SECS Optimization Benchmark Methodology

<!-- research-colors:start -->
### Colour scale

![Lag up to 5%](heatmap/best.svg "Lag up to 5%") up to 5% · ![Lag between 5% and 30%](heatmap/middle.svg "Lag between 5% and 30%") 15% · ![Lag from 30%](heatmap/worst.svg "Lag from 30%") from 30% · ![Neutral or insufficient separation](heatmap/equal.svg "Neutral or insufficient separation") neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](COLORS.md).


Only selected metrics within one scenario/window/environment are compared. Measured comparisons stay grey when neither the table nor its linked summary supplies independent-repeat envelopes. Stage shares and gameplay counters are not colored.
<!-- research-colors:end -->

[Project overview](../../README.md) | [Space Battle report](../space-battle/REPORT.md)

This report compares the original SECS benchmark results with the optimized
results for the same workload cases. The comparison is intentionally limited to
SECS; other frameworks are not required in the optimized JSON. The historical
`Benchmarks.Local/results.json` and `stress-results.json` remain immutable. For
a matched before/after comparison, the chart defaults to fresh original-baseline
repeats in `results-baseline-repeat.json` and
`stress-baseline-repeat.json`; optimized runs use separate
`results-optimized.json` and `stress-results-optimized.json` files. All four
paths can be overridden on the command line. Measurements were completed on
2026-10-11 using .NET 8.0.31 x64, Release, one thread, and tiered compilation off.

## Coverage and presentation

The base suite covers `Create`, `Iterate`, `Churn`, and `Destroy` at 1,000 and
100,000 entities. The stress suite covers `DenseIteration`, `SparseIteration`,
`WideIteration`, `RandomLookup`, `PayloadChurn`, `Recycle10Percent`, and
`MixedFrames` at 100,000 and 1,000,000 entities. Only cases present in both
versions are compared; unmatched cases are identified in the chart rather than
being inferred or filled in.

Each panel shows original and optimized mean elapsed time, with a one-standard-
deviation whisker when `stddevNs` exists. Base panels select microseconds or
milliseconds per operation; stress timings are in milliseconds per complete
operation. Every case has its own logarithmic time scale, so compare the paired
bars and their numeric labels within a panel, not bar lengths between panels.
Speedup is calculated as `original meanNs / optimized meanNs`: above 1 means
the optimized case is faster, below 1 means it is slower. A ratio is descriptive
of these runs, not a statistical significance test.

## Measurement context

The original comparison results come from the local Stopwatch harness, not
BenchmarkDotNet. It validates the workload before timing, warms each case, and
reports repeated samples. Setup, disposal, and forced garbage collection are
excluded from the measured interval; collection caused by the operation is
included. The stress harness uses the same fixed workload inputs and seeded
orders for each framework, with its own sample and warmup policy. See
[`SECS.Benchmarks/README.md`](../../SECS.Benchmarks/README.md) for the complete
workload definitions and harness details.

Both the original baseline and optimized implementation execute the SECS native
runtime. The original baseline repeat uses the preserved compiled
`Benchmarks.Local/baseline-bin/SECS.dll`; only `Comparison.dll` is copied there
to run the same API fixtures against that binary. `SECS.dll` must not be
replaced by a rebuild from the optimized sources. The fresh baseline repeat's
SECS revision metadata label is corrected through the runner's revision-label
environment override so it identifies the preserved baseline rather than the
current working tree. Keep this binary provenance distinct from the metadata
label.

The original source revision is `872e0df`. The preserved original DLL's SHA-256
is `D7996FF91C3DFE4414E41E79D2E0CDE636DAE0DC706C6F8A2254F82B3C8678AC`.
The optimized runtime is an uncommitted working-tree patch. Base cases retain
20 samples each; stress cases retain 8 samples each. The original fixtures and
SECS adapter operations were not replaced with the new accessor/factory APIs.

The latest optimization adds an accessor API, but the original benchmark
fixtures do not register or exercise that API. Its packed address/version entry
array is managed storage with a payload capacity of 8 + 4 bytes per ID on x64.
`AccessorBufferBytes` measures that managed array payload and excludes array
headers; `NativeBytes` describes component-column storage. These counters are
distinct, and neither should be substituted for the other. The chart therefore
does not measure accessor performance or its memory tradeoff; those require
separate fixtures and measurements. Record and verify the exact original
binary provenance, optimized revision, backend, runtime, machine, and run date
before interpreting timing differences.

Results characterize the recorded machine, runtime, fixed benchmark fixtures,
and sample policy. They do not establish performance across all workloads,
hardware, Unity/IL2CPP, or future library versions. Managed allocation counts
exclude SECS native storage and are not total-memory measurements. Standard-
deviation bars describe sample spread; they are not confidence intervals.

## Reproduction

Run from the repository root with the local matplotlib environment:

```powershell
$env:PYTHONPATH = (Resolve-Path Benchmarks.Local/python-packages).Path
python Benchmarks.Local/plot_optimization.py
```

By default, the renderer reads the two fresh baseline-repeat files and the two
optimized files named above, then writes `docs/benchmarks/optimization.png`.
Use `--base-results`, `--base-stress`, `--optimized-results`, or
`--optimized-stress` to select alternate inputs. It does not modify any result
JSON. A SECS-only optimized result is sufficient; non-SECS rows are ignored.

## Results

![Paired SECS measurements](optimization.png)

At 100,000 entities the original API's base creation improved from 15.372 to
9.701 ms, churn from 15.207 to 8.679 ms, iteration from 76.252 to 56.708 us,
and destruction from 2.952 to 2.546 ms. These do not include use of a new factory.

Stress results at 1,000,000 entities, milliseconds per complete operation:

| Case | Original | Optimized | Original / Optimized |
| --- | ---: | ---: | ---: |
| DenseIteration | ![Lag between 5% and 30%](heatmap/good.svg "Lag between 5% and 30%") 2.672 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 2.414 | 1.11x |
| SparseIteration | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 0.012648 | ![Lag between 5% and 30%](heatmap/good.svg "Lag between 5% and 30%") 0.013787 | 0.92x |
| WideIteration | ![Lag between 5% and 30%](heatmap/middle.svg "Lag between 5% and 30%") 9.302 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 7.960 | 1.17x |
| RandomLookup | ![Lag from 30%](heatmap/worst.svg "Lag from 30%") 179.296 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 60.061 | 2.99x |
| PayloadChurn | ![Lag from 30%](heatmap/worst.svg "Lag from 30%") 1060.351 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 740.947 | 1.43x |
| Recycle10Percent | ![Lag from 30%](heatmap/worst.svg "Lag from 30%") 104.031 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 74.610 | 1.39x |
| MixedFrames | ![Lag from 30%](heatmap/worst.svg "Lag from 30%") 9445.567 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 5674.716 | 1.66x |

MixedFrames includes all 120 frames, not one frame. Results are not uniformly
faster: sparse iteration at one million and base destruction at one thousand
had worse final point estimates. Sparse iteration's optimized SD is about
0.005 ms; small-batch destruction has substantial spread too. Earlier
diagnostic runs varied, especially for random access and large-world lookup.
The full sample arrays and all 22 matched cases remain available; do not infer
universal speedup or statistical significance from these single-run ratios.

### New API measurements

Separate creation fixtures initialize identical Position/Velocity values.
Every method starts with a fresh world and a prepared template outside the
timer; creation includes record growth and chunk allocation. Results at one
million entities: empty entity plus two Add calls 104.644 ms, direct component
creation 61.671 ms, prepared factory batch 44.207 ms (2.37x versus empty/Add/Add).
The original base creation fixture uses different component data and must not
be compared directly to this new fixture.

Separate lookup fixtures use the same populated world and seeded permutation
for both APIs. They use 256 base layouts but omit the stress fixture's Hot
component and use a different seed. The accessor is constructed outside timing;
it remains allocated during both methods. Thus these numbers are paired within
this fixture, not substitutions for the original RandomLookup measurements.

| Entities | World.Get | Cached Accessor.Get | Original / Cached |
| --- | ---: | ---: | ---: |
| 100,000 | ![Lag from 30%](heatmap/worst.svg "Lag from 30%") 0.870 ms | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 0.487 ms | 1.79x |
| 1,000,000 | ![Lag between 5% and 30%](heatmap/bad.svg "Lag between 5% and 30%") 56.982 ms | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 44.440 ms | 1.28x |

The million-entity lookup SDs are 9.250 and 11.044 ms respectively, so the
accessor's benefit is not stable enough here to promise a fixed speedup. Its
retained entry payload is 1.5 MiB at 100,000 IDs and 12 MiB at 1,000,000 IDs.
Structural maintenance cost and accessor construction are not measured by this
lookup-only fixture. See [API tradeoffs](../SECS_OPTIMIZATION.md).

### Retained native storage

A separate, untimed diagnostic seeds 100,000 entities and runs 20 shuffled
add/remove passes with a 256-byte payload, validating remaining data after each
pass. It reads the same internal chunk/stride fields in both runtimes through
reflection, allowing the executable to run against the original DLL.

| Checkpoint | Original Chunks | Optimized Chunks | Original Native Bytes | Optimized Native Bytes |
| --- | ---: | ---: | ---: | ---: |
| Seeded | ![Neutral or insufficient separation](heatmap/equal.svg "Neutral or insufficient separation") 393 | ![Neutral or insufficient separation](heatmap/equal.svg "Neutral or insufficient separation") 393 | ![Neutral or insufficient separation](heatmap/equal.svg "Neutral or insufficient separation") 2,405,377 | ![Neutral or insufficient separation](heatmap/equal.svg "Neutral or insufficient separation") 2,405,377 |
| After first pass | ![Lag from 30%](heatmap/worst.svg "Lag from 30%") 1,174 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 784 | ![Lag between 5% and 30%](heatmap/strong.svg "Lag between 5% and 30%") 32,828,417 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 30,432,257 |
| After 20 passes | ![Lag from 30%](heatmap/worst.svg "Lag from 30%") 15,994 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 784 | ![Lag from 30%](heatmap/worst.svg "Lag from 30%") 609,504,257 | ![Lag up to 5%](heatmap/best.svg "Lag up to 5%") 30,432,257 |

Optimized retained storage stops growing after the first pass in this fixture.
This is not immediate reclamation: empty chunks remain retained for reuse.
The counters exclude all managed storage and native allocator overhead.
Diagnostic JSON is in `storage-baseline.json` and `storage-optimized.json`.

### Verification

- 15 unit tests pass in Debug and Release, including actual migration/destroy
  swap-removal, accessor generation checks, disposal, world-local registration,
  template creation, and 20-cycle storage bounds.
- 8 base and 14 stress cases were measured against both original and optimized
  runtimes. All stress cases validate full entity state after measured operations.
- Small stress boundary fixtures (257 and 4,097 entities) pass all seven cases,
  repeated three times, plus payload round-trip.
- All 10 new API measurement cases validate checksums/data outside timing.
- BenchmarkDotNet in-process Dry smoke completes all 10 new API cases. This is
  execution validation, not the source of the comparative measurements above.
- OtherBenchmarks lifecycle validation passes 43 classes and 172 cycles.
- Sample counts, means, and sample standard deviations were recomputed from the
  saved arrays. The final paired graph was rendered and visually inspected.
