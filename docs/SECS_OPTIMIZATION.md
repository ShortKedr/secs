English | [Русский](SECS_OPTIMIZATION.ru.md)

# SECS Runtime Optimizations

[Project overview](../README.md) | [Space Battle report](space-battle/REPORT.md)

## Existing API

`World.Get<T>`, `Add<T>`, `Remove<T>`, `Set<T>`, and queries retain their
existing call patterns. Component IDs remain world-local; a generic registration
slot accelerates resolution without imposing a shared component registration order.
Archetypes use indexed column lookup. Structural edges cache the destination
archetype and a plan of source/destination column offsets and sizes.

Each archetype tracks non-full chunks. Adding an entity reuses retained free
capacity before allocating another chunk. Removal still swaps with the last
entity in the same chunk and updates that entity's location. Empty chunks are
retained for reuse, not released immediately; storage follows historical peak
occupancy per archetype rather than current occupancy alone.

## Cached Random Access

```csharp
var positions = world.GetAccessor<Position>();
ref var position = ref positions.Get(entity);
position.X += 1;
if (positions.TryGet(entity, out var copy)) { /* use the value copy */ }
```

The accessor caches component addresses and entity generations. Creation,
migration, swap-removal, and destruction maintain its table; disposing the world
invalidates it. Repeated requests for the same component share one table. `Has`
and `TryGet` reject dead generations and missing components; `Get` throws for
those cases even in Release.

This is opt-in: ordinary queries and `World.Get<T>` do not allocate address
tables. On a 64-bit runtime, each cached component costs 12 bytes per allocated
ID slot (8-byte address plus 4-byte generation), excluding array headers.
Capacity grows geometrically and includes IDs where the component is absent.
Structural operations update each registered accessor, so register only the
components needing frequent random access. Accessor construction scans existing
entity records and must not be mistaken for a free operation.

An `Entity` contains only ID and generation, not world identity. Pass handles
from the owning world only: equal IDs/generations in different worlds can alias.
Neither worlds nor accessors are thread-safe. A returned `ref` must not outlive
a structural change or world disposal; cache the accessor, not component refs.

## Direct Creation

```csharp
var entity = world.CreateEntity(new Position { X = 1 }, new Velocity { X = 2 });
var factory = world.PrepareEntity<Position, Velocity>();
var another = factory.Create(new Position { X = 3 }, new Velocity { X = 4 });
var entities = new Entity[1000];
factory.CreateBatch(entities, new Position { X = 5 }, new Velocity { X = 6 });
```

`CreateEntity` overloads accept one, two, or three unmanaged components and place
the entity directly in the final archetype. The two-component factory caches the
world, archetype, and component IDs. `World.CreateEntities` is the convenience
batch overload; a prepared factory avoids repeating template resolution.
Batch creation initializes every output entity with the same provided values.
Use individual factory calls for different values. Duplicate component types
are rejected before allocating an entity. Factories are world-bound and cannot
create after disposal.

## Storage Diagnostics

`World.GetStorageStatistics()` reports retained archetypes, chunks, live entity
count, native column-buffer bytes, and accessor address/version array payload
bytes. `NativeBytes` excludes managed entity/version arrays, metadata, and native
allocator overhead. `AccessorBufferBytes` excludes managed array headers. These
are diagnostic counters, not a complete process-memory measurement.

See [benchmark methodology](benchmarks/optimization-methodology.md) for paired
before/after measurements and separate new-API measurements. Historical
five-framework charts remain historical; they are not silently relabelled as
measurements of the optimized runtime.
