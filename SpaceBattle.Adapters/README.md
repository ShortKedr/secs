English | [Русский](README.ru.md)

# SpaceBattle Native Adapters

[Benchmark report](../docs/space-battle/REPORT.md) |
[Repository overview](../README.md)

Library project: `SpaceBattle.Adapters.csproj`. Public stores in namespace
`SpaceBattle`: `LeoEcsLiteStore`, `LeoEcsClassicStore`, `MorpehStore`,
`StaticEcsStore`. All implement `IEcsStore` through the shared
`SpaceBattle.Core/NativeStore.cs` identity layer.

## Dependencies

Classic and Lite compile the existing vendor source files as linked sources,
matching `Benchmarks.Local/Comparison.csproj`, without referencing its executable.
Morpeh references its existing vendor project. StaticEcs references
`Benchmarks.Local/StaticLibrary.csproj` (1.2.01). No vendor versions are changed.

## Contract

- Components and query membership are stored only by the native frameworks.
- `ForEach` uses native filters or StaticEcs query iterators for queries with one,
  two, or three component types.
- Component arguments and `Get<T>` are live native storage references.
- `Get<T>` throws on absent components or stale keys; it does not add components.
- `Set<T>` adds or replaces; `Remove<T>` and `Destroy` ignore stale keys.
- Logical keys are store-local reusable slot indices with independent generations.
  Slots contain native Classic entities, Lite packed entities, Morpeh entities,
  or StaticEcs GIDs (including generation). The reverse dictionary contains only
  live identities, not a history of destroyed entities. Storage is proportional
  to peak live entity count, except slots retired at `int.MaxValue` generation.
- Classic, Lite and Morpeh have a private lifetime marker so removing the final
  public component does not implicitly destroy the entity (including at commit).
- Structural operations, commit, and disposal are rejected during queries.
  Ref writes are permitted. Stores are single-threaded.
- Call `Commit` after structural changes and before querying. Morpeh applies its
  native staged changes here; other implementations have immediate changes.
- Never retain component refs across structural operations or disposal.
- Deterministic game serials remain component data, independent of native IDs.

StaticEcs allows exactly one active store with its fixed world tag. Dispose the
old instance before constructing another. The built-in component types are explicitly registered in
[StaticEcsStore](StaticEcsStore.cs) before initialization. Additional unmanaged types
must be registered in the constructor callback, for example:

```csharp
using var store = new StaticEcsStore(r => r.Register<MyComponent>());
```

Diagnostics reports the live entity count. Vendor archetype, chunk and byte
counters are not measured; the central report shows them as unknown.

## Build and Validation

Run from the repository root. Do not start a competing restore while another
restore is active:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"
dotnet restore SpaceBattle.Adapters/SpaceBattle.Adapters.csproj
dotnet build SpaceBattle.Adapters/SpaceBattle.Adapters.csproj -c Release --no-restore -m:1
```

Build success alone does not validate adapter behavior. After building the
headless host, run from the repository root, outside timing:

```powershell
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-adapters
```

The library provides `AdapterValidation.RunAll()` for the existing validation
host; no additional executable is introduced. It validates all component types,
empty entity lifetime, missing components, 1/2/3 native query membership and ref
writes, processor state, cached-query removal, mutation rejection, query exception
unwinding, stale-key rejection, reusable slots over 70,000 lifecycles (crossing
native 16-bit generation rollover), disposal, StaticEcs exclusivity, additional
registration, and constructor failure recovery. It throws on failure and returns
the four successful store names. `Validate(IEcsStore, int)` takes ownership of an
empty store and disposes it after validation. `RunAll` prints each framework name
before validation to identify failures.

The original adapter validation is recorded in the [2026-10-11 acceptance ledger](../docs/space-battle/PLAN.md). For current campaign parity and provenance see the [current report](../docs/space-battle/REPORT.md).
