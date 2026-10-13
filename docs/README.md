English | [Русский](README.ru.md)

# Documentation map

[Project overview](../README.md)

Choose a route below. API guides describe the current code; plans describe
requirements for future work; historical reports describe the revision and
measurements named in the report.

## Using SECS

- [Library guide](secs/README.md): queries, systems, ownership, and lifetime rules.
- [Runtime optimizations](SECS_OPTIMIZATION.md): direct creation, accessors, retained storage, and diagnostics already implemented.
- [Sample](../SECS.Sample/Program.cs) and [tests](../SECS.Tests): executable examples and current contracts.

## Measurements

| Workload | Method and results |
| --- | --- |
| Current five-framework microbenchmarks | [Results and scope](benchmarks/current/REPORT.md) |
| All SECS BenchmarkDotNet suites and API diagnostics | [Supplemental results](benchmarks/current/SUPPLEMENT.md) |
| Reproduce microbenchmarks | [Benchmark entrypoint](../SECS.Benchmarks/README.md), [local harness](../Benchmarks.Local/README.md) |
| Before/after runtime optimizations | [Paired measurement methodology](benchmarks/optimization-methodology.md) |
| Finite battle | [Space Battle report](space-battle/REPORT.md) |
| Battle matching desktop defaults | [Game preset, stages and HUD windows](space-battle/GAME_REPORT.md) |
| Desktop startup and HUD Mean | [Matched Release/headless study](space-battle/DESKTOP_TIMING.md) |
| Colour markers in reports and charts | [Shared comparison scale](benchmarks/COLORS.md) |

Read the date, source revisions, hardware, and configuration before comparing
results. A chart from an older runtime remains evidence for that runtime.

## Library development

| Document | Status and purpose |
| --- | --- |
| [Determinism test plan](SECS_DETERMINISM_PLAN.md) | Proposed canonical snapshots, replay scenarios, and CI matrix. The [workflow example](secs-determinism.example.yml) is inactive and runs existing tests only. |
| [Core development plan](secs/DEVELOPMENT_PLAN.ru.md) (Russian) | Proposed Fixed, time slicing, SIMD, and parallel runner contracts. |
| [Related changes](secs/RELATED_CHANGES.ru.md) (Russian) | Scope guidance for tests, benchmarks, and separately justified consumer migration. |
| [GA specification package](secs/ga/README.ru.md) (Russian) | Proposed common CPU/GPU contract, implementation stages, and qualification gates. |
| [Internal task templates](secs/prompts/README.ru.md) (Russian) | Starting briefs for future implementation tasks; not installed API or end-user setup instructions. |

Current SECS has sequential queries and system calls. These plans do not imply
that the numeric types, schedulers, compiler, or GPU backend already exist.
The development and GA packages currently have Russian originals only.

## Space Battle

[Game documentation map](space-battle/README.md) covers simulation systems,
physics, fleets, assets, desktop/Web hosts, and reproduction. Start there for
game-specific implementation and diagnostic history.

## Source and licenses

[Third-party notices](../THIRD_PARTY_NOTICES.md) identify licenses and pinned
sources. The four comparison frameworks are included in `Benchmarks.Local/vendor`.
Consult the inventory before reuse, including LeoECS conditional MIT-Red terms.
