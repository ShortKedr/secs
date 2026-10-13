English | [Русский](SECS_DETERMINISM_PLAN.ru.md)

# SECS Determinism Test Plan

For the current CPU SECS, repeat runs and result comparisons across Windows, Linux, macOS, x64, and ARM64 can be automated in GitHub Actions. Start with integer ECS scenarios, an independent reference model, and a shared state format. Equality for arbitrary game logic using floats, as well as CPU/GPU equality, requires additional contracts and executors.

Status as of October 12, 2026: implementation plan. The [workflow example](secs-determinism.example.yml) runs existing tests and is outside `.github/workflows`; it does not yet verify that traces match across machines. The replay runner, reference traces, and comparer described below still need to be implemented.

[Documentation map](README.md) · [Current API](secs/README.md)

Read in order: [current coverage](#what-is-already-tested), [proposed contract](#proposed-contract), [canonical state](#canonical-state-and-reference), [test suite](#test-suite), [CI design](#github-actions), and [implementation stages](#implementation-stages).

## What Is Already Tested

[SECS](../SECS/SECS.csproj) and [SECS.Tests](../SECS.Tests/SECS.Tests.csproj) target `net8.0`. The library has no GPU backend or parallel scheduler. The current suite contains 16 `[Fact]` tests and no `[Theory]` tests:

| File | Tests | Coverage |
| --- | ---: | --- |
| [WorldTests](../SECS.Tests/WorldTests.cs) | 7 | Components, destroy and ID versions, query cache updates, row and chunk iteration, system stages, migrations |
| [AccessorSwapTests](../SECS.Tests/AccessorSwapTests.cs) | 3 | Accessors after migrations and swap removal, destruction, factory lifetime, and duplicate types |
| [DefaultWorldTests](../SECS.Tests/DefaultWorldTests.cs) | 1 | Default options and multiple chunks at capacity 256 |
| [OptimizationTests](../SECS.Tests/OptimizationTests.cs) | 5 | Churn with seed 7319, chunk reuse, accessors, direct and batch creation |

Recorded local verification on October 12, 2026: Release 16/16 and Debug 16/16, with no skips; Windows x64, SDK 10.0.204, target net8.0. TRX files are in `Benchmarks.Local/tmp/secs-determinism-plan/tests`. This verifies existing contracts on one machine. The suite does not yet compare independent processes, different operating systems, or architectures.

[SpaceBattle.Headless](../SpaceBattle.Headless/Validation.cs) repeats two battles in one process with a final checksum after 1800 ticks and compares five ECS adapters every 300 ticks through 3000. This is a useful integration layer. [Battle.Checksum](../SpaceBattle.Core/BattleInspection.cs) hashes selected fields, including float bits; it is not a complete snapshot of the ECS, physics solver, or all state that affects continuation of the simulation. The headless report also contains timing, memory, date, and machine name, so entire JSON files cannot be compared byte-for-byte.

## Proposed Contract

| Level | What must match | Conditions |
| --- | --- | --- |
| D1 Repeat one log | API results, IDs and versions, component membership, values, iteration trace, and system calls | Same sequential operations, type and system registrations, options, and environment |
| D2 ECS portability | Canonical states and observable D1 results across selected operating systems and CPUs | Fixed inputs, serialization schema, and defined integer arithmetic |
| D3 Storage independence | Logical state across different capacities, query preparation, and type registration | Only systems explicitly independent of iteration order; exclude internal type IDs and addresses |
| D4 Game replay | Game scenario state after every tick | Own contracts for numbers, RNG, events, physics, and all hidden state |
| D5 CPU/GPU | D2 or D4 for the same system source and different executors | GPU backend, write-conflict rules, structural commands, and arithmetic |

For the first required CI, choose D1 and D2 on integer components, then D3 for independent per-entity updates. Start D4 as a separate SpaceBattle diagnostic; connect D5 after an executable GPU backend exists. A successful final matrix confirms a specific workload and set of environments; it does not prove determinism for all C# systems on any hardware.

Decisions required before implementation:

1. Fix sequential execution and prohibit structural changes during an active iteration in the test-scenario contract. [Query](../SECS/Query.cs) currently reads live lists and passes `ref`; there is no deferred-change queue or snapshot guarantee. Perform add and remove operations between iterations. Supporting different semantics publicly would first require a separate decision and API tests.
2. Fix capacity and registration order for D1. [World](../SECS/World.cs) stores archetypes in creation order, systems in registration order, and free entity IDs in a LIFO stack. [Archetype](../SECS/Archetype.cs) uses swap removal within a chunk. Query visits archetypes, chunks, and rows; it does not sort entity IDs. Order depends on operation history; do not promise independence from that history.
3. For D3, compare entity sets and fields separately from the iteration trace. For example, `value += velocity` independently per entity is suitable, while changing a shared accumulator based on the previous result requires an explicitly defined order. When entity creation order is permuted, match entities by a stable logical scenario key instead of expecting the same entity IDs.
4. Specify the scenario's arithmetic overflow semantics (`checked` or `unchecked`), widths, and division. Leave Entity generation overflow and operations on invalid or foreign handles as separate API questions. In particular, diagnostics for some invalid handles in World depend on `DEBUG`; compare Debug and Release only for valid operations.
5. Do not include multithreading on one World, changing system registration during Run, or re-entering Run in the D1 guarantee. First decide whether these are allowed; the current live lists provide no snapshot behavior. Add negative tests only after choosing expected behavior, so tests do not codify an accidental result.

## Canonical State and Reference

Create a small `SECS.Determinism` console project that depends only on SECS, and share its scenarios with xUnit. It must run without a window, OpenGL, SpaceBattle, vendor adapters, or a WASM toolchain. Store workload commands as versioned fixtures; separate fixture creation from fixture execution. A seed alone does not describe the full input.

Two result representations are needed:

- **Logical snapshot:** all live entities, including entities without components; IDs and versions for the same log, stable schema type IDs, component membership, and every defined field. Sort by identity and schema ID; use section lengths, fixed widths, and little endian. Serialize entity references as identities. Use logical keys for permutation scenarios. The harness keeps a log of issued handles because SECS has no public universal serializer or enumeration of all fields.
- **Observable behavior trace:** command results, newly issued handles, query visits, stages, and system calls. Compare this separately from the sorted state when D1 order is fixed. To test hidden storage history, continue with the same command suffix, including creation after destroy and migrations.

Do not hash pointers, accessor addresses, `Entity.GetHashCode()`, internal numeric type IDs, an entire chunk, or raw `sizeof(T)` bytes. [ComponentTypeRegistry](../SECS/ComponentTypeRegistry.cs) assigns world type IDs on first access; unmanaged structs can contain padding, and chunk memory is allocated without general initialization. The canonical writer must write defined fields of live objects. For float storage, separately choose exact IEEE bits, including the sign of zero and NaN payload; normalization is allowed only as a different, explicit contract. Do not introduce an epsilon into strict equality checks.

Hash canonical bytes with SHA-256. A hash after every operation or tick helps locate divergence quickly, and small scenarios should also be compared in full. On failure, report the first operation or tick, entity, component, field, expected and actual values, input fixture, and both snapshots. Test the writer and comparer themselves: permuting an allowed serialization order must not change a snapshot; changing one field, membership, version, or command result must always be detected.

The reference consists of small, manually verifiable fixtures and a simple managed model using dictionaries and sets, without archetypes, unmanaged storage, or a query cache. The model checks component membership and values, command results, and system stage order. Check query visitation order separately: use manually specified golden traces for small fixtures and compare repeats, processes, and platforms for the others. The managed model must not copy internal chunk organization to predict this order. Two SECS runs can detect instability but may reproduce the same bug; both logical states must match the independent model. For an intentional contract change, update expected results in a separate reviewed change; CI must not overwrite golden files automatically.

For randomized scenarios, use a saved operation log or a versioned custom generator with known vectors. The current `System.Random(7319)` is convenient for local churn, but Microsoft does not guarantee the same sequence across major .NET versions. [System.Random contract](https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-10.0).

## Test Suite

| Priority | Scenario | Check |
| --- | --- | --- |
| P0 | One fixture in two fresh Worlds, then two new-process runs | Full command results and states at every step match the reference |
| P0 | Create, Add, Remove, Destroy, recreate | Same IDs and versions for the same log, survivor data and membership; include empty entities |
| P0 | Chunk boundaries | Capacities 1, 2, 3, 32, 256, 257; unique counts from 0, 1, C−1, C, C+1, 2C+1; migrate and swap-remove at the beginning, middle, and end |
| P0 | Query cache before and after new archetypes | Typed Query with 1, 2, 3 components and QueryBuilder With/Without, empty match, cache update; check full membership |
| P0 | Systems and stages | PreUpdate, Update, PostUpdate; multiple systems per stage, repeated RunAll, order effects, and data after every system |
| P1 | Accessor after swap, migration, and recycle | Re-read values and lifetime match; do not retain a ref across a structural change |
| P1 | Direct, factory, batch | Equivalent final fields, membership, and handles; compare intermediate traces only for equivalent commands |
| P1 | Type registration and options | First-access type order, more than 8 types to expand slots, default and capacity <=0, diagnostics on/off for valid operations |
| P1 | Independent systems at different capacities | Integer Transform and Motion: same per-component update through ForEach and Chunks, D3 equality |
| P1 | Type with padding and memory reuse | `byte` and `int` fields after create/migrate/destroy; equality of defined fields without reading padding |
| P1 | Number and serialization portability | Integer minima/maxima, overflow under chosen policy, negative division, byte order; float copy without arithmetic using ±0, subnormal, Infinity, NaN |
| P2 | Long churn and growth | Multiple fixtures, registry/query-cache expansion, chunk exhaustion and reuse, state and observable continuation |

Proposed PR workload: 4 fixtures with seeds 1, 27, 7331, 4294967295, up to 2000 operations each, and two separate processes; check boundaries and options with short parameterized tests. Nightly: 16 fixtures of up to 100000 operations each and additional JIT variants. This is an initial workload budget; measure runtime and artifact size after implementing the runner. If the generator supports seed 0, define its behavior and vector separately.

## GitHub Actions

CPU tests need only the SDK, NuGet restore, and standard runners. Build the minimum matrix as follows:

| Label | OS | CPU | Purpose |
| --- | --- | --- | --- |
| `ubuntu-24.04` | Linux | x64 | Main matrix and reference job |
| `windows-2022` | Windows | x64 | Main matrix |
| `ubuntu-24.04-arm` | Linux | ARM64 | Architecture portability |
| `macos-15` | macOS | ARM64 | Different OS on ARM64 |
| `macos-15-intel` | macOS | x64 | Extended nightly matrix |

These labels are listed for public and private repositories in the [GitHub runners reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners). An OS label does not pin the exact CPU model or an immutable image. Each runner must record the actual OS, process architecture, SDK, runtime, JIT options, commit, fixture digest, and schema version. Check process architecture explicitly: an x64 process running through emulation is not an ARM64 check.

A project targeting `net8.0` runs on .NET 8 even when built with a newer SDK. To check .NET 10 separately, ensure the test host runs on that runtime, for example with a separate test-host target, and record the actual version; installing a second SDK alone does not create a runtime matrix. During D2, pin the exact SDK and allowed runtime patch. `setup-dotnet` without `global.json` does not rule out selection of a newer preinstalled SDK; the example pins the selection of an installed SDK using a temporary global.json. [setup-dotnet documentation](https://github.com/actions/setup-dotnet).

As of October 12, 2026, .NET 8 is supported through November 10, 2026, and .NET 10 through November 14, 2028. Start with the current net8 target, but plan compatibility checks for the test host and library on .NET 10 immediately; changing the target is a separate change. [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

CI sequence after implementing replay:

1. Each OS and configuration combination restores and builds the specific SECS projects, runs xUnit for its selected configuration, then runs the same replay in two separate processes of that configuration. Also compare Debug and Release for valid integer scenarios. `fail-fast: false` preserves diagnostics from the other environments.
2. Each job checks both results locally and against the managed reference, then saves the manifest, fixtures, per-step hashes, canonical snapshots, and TRX. The artifact name includes the label, configuration, and run variant; upload runs with `if: always()`.
3. A separate `compare` job with `needs` on all producer jobs and `if: always()` downloads results and checks the expected complete inventory. A missing, empty, extra, schema/input/runtime-incompatible result, or producer failure must fail the gate. First compare metadata, then compare per-step hashes and full snapshots with the golden results and across environments.
4. The PR gate compares integer D1/D2 results. For D3, compare only logical state for independent scenarios. A separate SpaceBattle diagnostic job does not affect the ECS portability claim.
5. The required status check has a stable name, for example `SECS determinism`, and is green only if the entire required matrix succeeds. If merge queue is enabled, `merge_group` is also needed. Do not hide divergences with `continue-on-error` or retries until the first success.

[upload-artifact](https://github.com/actions/upload-artifact) and [download-artifact](https://github.com/actions/download-artifact) transfer data between jobs. Compare simulation fields, not archives or entire manifests: timestamps, timings, OS, and CPU are expected to differ.

Triggers: `pull_request`, `push` to main, and `workflow_dispatch`; use a separate `schedule` for long scenarios. Prefer running the required PR workflow without a paths filter: GitHub warns that a workflow skipped by a paths filter leaves a required check Pending. [Behavior of skipped workflows](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/skip-workflow-runs).

`permissions: contents: read`, checkout without credential persistence, and standard artifact transport are sufficient. Secrets, Pages publishing, GPU, workload installation, building the entire solution, and benchmark runs are not needed for the CPU suite. Changes on GitHub for future activation: allow Actions and the actions used, add the workflow, and, if needed, make the gate required in branch rules. These are repository-owner settings; the example does not activate them.

NuGet caching is optional. Before `setup-dotnet cache: true` and `restore --locked-mode`, create and verify packages.lock.json for the test dependency graph; the test project has none today. The first run example uses ordinary restore without caching. Do not reuse build outputs across architectures.

Under the current [GitHub Actions billing policy](https://docs.github.com/en/billing/concepts/product-billing/github-actions), standard hosted compute is free for public repositories; private repositories use plan quotas, artifacts have retention limits, and larger runners are billed separately. The initial matrix of 4 environments and 2 configurations gives 8 jobs. For example, assuming 3 minutes per job gives 24 minutes of total execution, excluding billing rules; this is an estimate, not a measured CI result. Start with 7-day artifact retention, keep full snapshots for small fixtures, and retain first-divergence snapshots for stress tests.

## Game Math and GPU

SECS stores unmanaged components and invokes a user processor; the library does not define the arithmetic in those systems. Therefore, portable float copying and portable simulation with trigonometry are different tests. Even the [Math.Sin documentation](https://learn.microsoft.com/en-us/dotnet/api/system.math.sin?view=net-8.0) allows exact results to differ across operating systems and architectures. For a strict game contract, choose integer/fixed point with defined rounding and intermediate width, or a carefully limited and verified float profile. Run float systems diagnostically first and preserve divergences.

Before full SpaceBattle D4, extend the snapshot to include all state that determines the next tick: spawn/destroy queues, events, RNG, timers, and physics state. Existing validation and checksums are useful smoke tests; the renderer and screenshots do not verify simulation determinism.

GitHub offers GPU larger runners with NVIDIA Tesla T4 on Ubuntu and Windows. They are available to organizations with GitHub Team or Enterprise Cloud and are billed; this does not cover AMD, Intel, Apple, Adreno, or Mali. [Specifications](https://docs.github.com/en/actions/reference/runners/larger-runners), [larger runner availability](https://docs.github.com/en/actions/concepts/runners/larger-runners).

The first D5 workload: one source for an independent integer Transform plus Motion system, a CPU reference, and one GPU backend, with the same inputs and state after every tick. This requires a compiler or translation for supported systems, buffers and readback, barriers and write semantics, deterministic events, and structural commits. In the GPU job, explicitly select the GPU backend and record the actual device, API, and driver; a silent CPU fallback must not count as GPU success.

Checking other vendors and mobile APIs will require dedicated self-hosted machines or a device fleet. Android and iOS are not included in the hosted desktop runner matrix above. Connect GPU and device jobs as a separate stage on validated code; a missing device means that combination remains unverified. A new backend matrix cannot be replaced by a few CPU architectures.

## Implementation Stages

| Stage | Local changes | Acceptance criterion |
| --- | --- | --- |
| 1 | Parameterized `SECS.Tests/DeterminismTests.cs`, schema, managed oracle, and small fixtures | D1 tests pass in Debug and Release; writer/comparer detects intentional corruption |
| 2 | `SECS.Determinism/` console host, shared workload, `tools/determinism/compare.py`, reference traces | Two independent processes match the reference; the first mismatching step produces a useful diff |
| 3 | `.github/workflows/secs-determinism.yml`, matrix, and aggregate gate | All required artifacts are received; D2 results match; missing artifact or mutation fails the gate |
| 4 | Nightly stress, D3 storage variants, separate SpaceBattle checks | Long workloads are reproducible; the scope of the confirmed contract is stated explicitly |
| 5 | Shared CPU/GPU contract and one backend, then one device at a time | Executors match on every tick; fallback and unverified devices are clearly marked |

Start with stages 1–3. The [workflow example](secs-determinism.example.yml) already describes a multi-platform run of the existing 16 tests, but its aggregate check is named `SECS baseline`: rename it to a determinism gate after replay and mandatory artifact comparison are added.
