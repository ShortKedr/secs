English | [Русский](README.ru.md)

# Using SECS

[Project overview](../../README.md) · [Documentation map](../README.md)

SECS builds for .NET Standard 2.0/2.1 and .NET 8. Components must satisfy C# `unmanaged`; systems and queries
execute sequentially. Add a reference to [SECS.csproj](../../SECS/SECS.csproj)
and use the `SECS` namespace. The [sample](../../SECS.Sample/Program.cs) is a
complete application showing systems and typed chunk iteration.

`new World()` uses a chunk capacity of 256 with diagnostics disabled. Configure it with `new World(new WorldOptions(chunkCapacity: 128, diagnosticsEnabled: true))`. Nonpositive capacities fall back to 256. Component type IDs are assigned locally to each world in first-access order; they are not stable serialization identifiers.

## A minimal query

```csharp
using SECS;

using var world = new World();
var entity = world.CreateEntity(new Position { X = 0, Y = 0 },
                                new Velocity { X = 3, Y = 1 });
var movement = new Movement();
world.Query<Position, Velocity>().ForEach(ref movement);

var position = world.Get<Position>(entity);
Console.WriteLine($"Position: ({position.X}, {position.Y})");

struct Position { public float X, Y; }
struct Velocity { public float X, Y; }
struct Movement : IQueryProcessor<Position, Velocity>
{
    public void Execute(Entity entity, ref Position position, ref Velocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
    }
}
```

The processor is a struct passed by reference. Queries with one, two, or three
component types visit all entities containing those types; extra components do
not exclude an entity. Changes through the component `ref` update stored values.

## Entities and components

| Operation | Use |
| --- | --- |
| `CreateEntity()` / `DestroyEntity(entity)` | Create an empty entity or destroy a live one. |
| `CreateEntity(component1, ...)` | Create directly with one, two, or three distinct component types. |
| `Add(entity, component)` | Add a missing component; an existing value is left unchanged. |
| `Set(entity, component)` | Replace a value, or add the component when absent. |
| `Get<T>(entity)` | Access an existing component by `ref`. |
| `TryGet<T>(entity, out value)` | Get a value copy; returns false for a stale handle or absent component. |
| `Has<T>(entity)` / `Remove<T>(entity)` | Check membership or remove a component from a live entity. |
| `IsAlive(entity)` | Check the ID and generation. |

An `Entity` holds an ID and generation, without world identity. Use it only with
its owning world. Destroyed IDs may be reused with a new generation. Do not
rely on invalid-handle exceptions from ordinary World operations: some checks
are Debug-only and require `DiagnosticsEnabled`.

Creation, destruction, and changes to component membership take effect
immediately. Perform them between query iterations. Queries traverse live
storage, so structural mutation inside a processor has no safe deferred-command
or snapshot contract.

A component `ref` or chunk pointer must not survive a structural change or
world disposal. Cache handles and queries instead of component references.
Worlds, queries, factories, and accessors are bound to the owning world;
concurrent use of a World is unsupported. Dispose it to release native buffers.

## Systems and queries

Register an `Action<World>` using `AddSystem(stage, system)`. `Run(stage)` executes
that stage in registration order; `RunAll()` executes `PreUpdate`, `Update`, and
`PostUpdate` in that order. The host supplies its own tick loop and time inputs.

`QueryBuilder().With<T>().Without<U>().Build()` creates an include/exclude
filter. Its `MatchCount` counts matching archetypes, not entities. Typed queries
expose `Chunks()` with component pointers and `Count`; access requires an
`unsafe` context. Iterate rows below `Count`, as in the sample. Component types
have separate columns, but fields inside a component struct retain their layout.

Iteration follows archetype creation order, chunk order, and current rows.
Swap removal can change row order. The API does not promise sorted entity IDs
or order independent of storage history.

## Creation throughput and random access

For repeated two-component creation, use `PrepareEntity<T1, T2>()` and the
resulting factory's `Create` or `CreateBatch`. Batch creation assigns the same
input values to every entity. For frequent random access, `GetAccessor<T>()`
maintains an address/generation table across structural changes. Both features
are opt-in and have costs; see [runtime optimizations](../SECS_OPTIMIZATION.md)
for examples and storage diagnostics.

## Working on the library

[Tests](../../SECS.Tests) cover the current API. [Benchmarks](../../SECS.Benchmarks/README.md)
measure specific workloads.
