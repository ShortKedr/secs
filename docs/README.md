English | [Русский](README.ru.md)

# Documentation map

[Project overview](../README.md)

Choose a route below. API guides describe the current code; historical reports
describe the revision and measurements named in the report.

## Using SKECS

- [Package installation](skecs/INSTALL.md): local NuGet package, Unity DLLs, and Godot C# setup.
- [Library guide](skecs/README.md): queries, systems, ownership, and lifetime rules.
- [Runtime optimizations](SKECS_OPTIMIZATION.md): direct creation, accessors, retained storage, and diagnostics already implemented.
- [Sample](../SKECS.Sample/Program.cs) and [tests](../SKECS.Tests): executable examples and current contracts.

## Measurements

| Workload | Method and results |
| --- | --- |
| Current five-framework microbenchmarks | [Results and scope](benchmarks/current/REPORT.md) |
| All SKECS BenchmarkDotNet suites and API diagnostics | [Supplemental results](benchmarks/current/SUPPLEMENT.md) |
| Reproduce microbenchmarks | [Benchmark entrypoint](../SKECS.Benchmarks/README.md), [local harness](../Benchmarks.Local/README.md) |
| Before/after runtime optimizations | [Paired measurement methodology](benchmarks/optimization-methodology.md) |
| Finite battle, including low/nominal/high activity profiles | [Space Battle report](space-battle/REPORT.md) |
| Battle matching desktop defaults | [Game preset, stages and HUD windows](space-battle/GAME_REPORT.md) |
| Desktop startup and HUD Mean | [Matched Release/headless study](space-battle/DESKTOP_TIMING.md) |
| Colour markers in reports and charts | [Shared comparison scale](benchmarks/COLORS.md) |

Read the date, source revisions, hardware, and configuration before comparing
results. A chart from an older runtime remains evidence for that runtime.

Internal drafts and development plans are kept in the
[restricted SKECS repository](https://github.com/ShortKedr/secs-private).

## Historical names

The project was previously called SECS. Preserved benchmark reports, raw result
files, screenshots, and hashes retain their original `SECS` labels so their
recorded identities and audit trail remain intact. Those labels identify the
measured historical revision; current product documentation and APIs use SKECS.

## Space Battle

[Game documentation map](space-battle/README.md) covers simulation systems,
physics, fleets, assets, desktop/Web hosts, and reproduction. Start there for
game-specific implementation and diagnostic history.

## Source and licenses

[Original project code, documentation, data, and artwork](../LICENSE) are under MIT.
[Third-party notices](../THIRD_PARTY_NOTICES.md) identify licenses and pinned
sources. The four comparison frameworks are included in `Benchmarks.Local/vendor`.
Consult the inventory before reuse, including LeoECS conditional MIT-Red terms.
