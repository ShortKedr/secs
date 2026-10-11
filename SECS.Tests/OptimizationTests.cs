using SECS;

namespace SECS.Tests;

public sealed class OptimizationTests
{
    [Fact]
    public void RandomizedStructuralChurn_ShouldPreserveComponentsAndQueries()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 32, diagnosticsEnabled: true));
        var random = new Random(7319);
        var entities = new List<Entity>();
        var expected = new Dictionary<Entity, (int Value, bool HasVelocity)>();

        for (var step = 0; step < 4_000; step++)
        {
            if (entities.Count == 0 || random.Next(100) < 58)
            {
                var value = step * 17 + random.Next(1000);
                var entity = world.CreateEntity<ValueComponent>(new ValueComponent { Value = value });
                var hasVelocity = random.Next(2) == 0;
                if (hasVelocity)
                {
                    world.Add(entity, new VelocityComponent { Value = -value });
                }

                entities.Add(entity);
                expected.Add(entity, (value, hasVelocity));
            }
            else
            {
                var index = random.Next(entities.Count);
                var entity = entities[index];
                if (random.Next(2) == 0 && expected[entity].HasVelocity)
                {
                    Assert.True(world.Remove<VelocityComponent>(entity));
                    expected[entity] = (expected[entity].Value, false);
                }
                else
                {
                    world.DestroyEntity(entity);
                    expected.Remove(entity);
                    entities.RemoveAt(index);
                }
            }

            if (step % 97 == 0)
            {
                foreach (var pair in expected)
                {
                    Assert.True(world.TryGet(pair.Key, out ValueComponent value));
                    Assert.Equal(pair.Value.Value, value.Value);
                    Assert.Equal(pair.Value.HasVelocity, world.Has<VelocityComponent>(pair.Key));
                }

                var processor = new CollectValuesProcessor();
                world.Query<ValueComponent>().ForEach(ref processor);
                Assert.Equal(expected.Count, processor.Entities.Count);
                Assert.Equal(expected.Keys.ToHashSet(), processor.Entities.ToHashSet());
            }
        }
    }

    [Fact]
    public void RepeatedFullChurn_ShouldReuseChunksAndBoundNativeBytes()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 32, diagnosticsEnabled: true));
        const int count = 2_048;
        var entities = new Entity[count];
        long firstPassNativeBytes = 0;
        int firstPassChunkCount = 0;

        for (var pass = 0; pass < 20; pass++)
        {
            for (var i = 0; i < count; i++)
            {
                entities[i] = world.CreateEntity<ValueComponent, VelocityComponent>(
                    new ValueComponent { Value = pass * count + i },
                    new VelocityComponent { Value = i });
            }

            for (var i = 0; i < count; i += 2)
            {
                world.DestroyEntity(entities[i]);
            }

            for (var i = 0; i < count / 2; i++)
            {
                var slot = count - 2 - i * 2;
                var previous = entities[slot];
                var replacement = world.CreateEntity<ValueComponent, VelocityComponent>(
                    new ValueComponent { Value = -i - pass },
                    new VelocityComponent { Value = -i });
                Assert.Equal(previous.Id, replacement.Id);
                Assert.NotEqual(previous.Version, replacement.Version);
                entities[slot] = replacement;
            }

            for (var i = 0; i < count; i++)
                world.Add(entities[i], new OtherComponent { Value = pass + i });
            for (var i = 0; i < count; i++)
                Assert.True(world.Remove<OtherComponent>(entities[i]));

            var statistics = world.GetStorageStatistics();
            Assert.Equal(count, statistics.EntityCount);
            if (pass == 0)
            {
                Assert.True(statistics.ChunkCount > 0);
                Assert.True(statistics.NativeBytes > 0);
            }
            else
            {
                Assert.Equal(firstPassChunkCount, statistics.ChunkCount);
                Assert.Equal(firstPassNativeBytes, statistics.NativeBytes);
            }

            if (pass == 0)
            {
                firstPassNativeBytes = statistics.NativeBytes;
                firstPassChunkCount = statistics.ChunkCount;
            }

            for (var i = 0; i < count; i++)
                world.DestroyEntity(entities[i]);
        }
    }

    [Fact]
    public void Accessors_ShouldBeWorldSpecificAndFollowMigrationAndRecycling()
    {
        using var first = new World(new WorldOptions(chunkCapacity: 2, diagnosticsEnabled: true));
        using var second = new World(new WorldOptions(chunkCapacity: 2, diagnosticsEnabled: true));

        _ = first.GetAccessor<OtherComponent>();
        var firstValue = first.GetAccessor<ValueComponent>();
        var secondValue = second.GetAccessor<ValueComponent>();
        var secondVelocity = second.GetAccessor<VelocityComponent>();
        var firstOther = first.GetAccessor<OtherComponent>();
        var secondOther = second.GetAccessor<OtherComponent>();
        var firstVelocity = first.GetAccessor<VelocityComponent>();
        var secondOtherEntity = second.CreateEntity<OtherComponent>(new OtherComponent { Value = -1 });
        var secondEntity = second.CreateEntity<ValueComponent>(new ValueComponent { Value = 501 });

        var entity = first.CreateEntity<ValueComponent>(new ValueComponent { Value = 41 });
        Assert.True(firstValue.Has(entity));
        Assert.False(firstVelocity.Has(entity));
        Assert.False(secondValue.Has(entity));
        Assert.False(secondValue.TryGet(entity, out _));
        Assert.True(secondValue.Has(secondEntity));
        Assert.Equal(501, secondValue.Get(secondEntity).Value);
        Assert.False(secondVelocity.Has(secondEntity));
        Assert.False(secondVelocity.TryGet(secondEntity, out _));
        Assert.Throws<InvalidOperationException>(() => ReadMissing(secondVelocity, secondEntity));
        Assert.True(firstOther.TryGet(first.CreateEntity<OtherComponent>(new OtherComponent { Value = 61 }), out var firstOtherValue));
        Assert.Equal(61, firstOtherValue.Value);
        Assert.True(secondOther.TryGet(secondOtherEntity, out var secondOtherValue));
        Assert.Equal(-1, secondOtherValue.Value);

        first.Add(entity, new VelocityComponent { Value = 9 });
        Assert.Equal(41, firstValue.Get(entity).Value);
        Assert.True(firstVelocity.TryGet(entity, out var velocity));
        Assert.Equal(9, velocity.Value);
        Assert.True(first.Remove<VelocityComponent>(entity));
        Assert.False(firstVelocity.Has(entity));
        first.Add(entity, new VelocityComponent { Value = 12 });

        var migrationNeighbor = first.CreateEntity<ValueComponent, VelocityComponent>(
            new ValueComponent { Value = 42 }, new VelocityComponent { Value = 13 });
        var migrationTarget = first.CreateEntity<ValueComponent, VelocityComponent>(
            new ValueComponent { Value = 43 }, new VelocityComponent { Value = 14 });
        Assert.True(firstValue.Has(migrationNeighbor));
        Assert.True(firstVelocity.Has(migrationNeighbor));

        first.Add(migrationTarget, new OtherComponent { Value = 430 });

        Assert.True(firstValue.Has(migrationTarget));
        Assert.True(firstOther.Has(migrationTarget));
        Assert.True(firstValue.TryGet(migrationNeighbor, out var afterMigrationValue));
        Assert.Equal(42, afterMigrationValue.Value);
        Assert.True(firstVelocity.TryGet(migrationNeighbor, out var afterMigrationVelocity));
        Assert.Equal(13, afterMigrationVelocity.Value);
        Assert.False(firstOther.Has(migrationNeighbor));

        var destroyNeighbor = first.CreateEntity<ValueComponent, VelocityComponent>(
            new ValueComponent { Value = 52 }, new VelocityComponent { Value = 23 });
        var destroyTarget = first.CreateEntity<ValueComponent, VelocityComponent>(
            new ValueComponent { Value = 53 }, new VelocityComponent { Value = 24 });
        first.DestroyEntity(destroyTarget);
        Assert.False(firstValue.Has(destroyTarget));
        Assert.False(firstVelocity.Has(destroyTarget));
        Assert.True(firstValue.TryGet(destroyNeighbor, out var afterDestroyValue));
        Assert.Equal(52, afterDestroyValue.Value);
        Assert.True(firstVelocity.TryGet(destroyNeighbor, out var afterDestroyVelocity));
        Assert.Equal(23, afterDestroyVelocity.Value);

        first.DestroyEntity(migrationTarget);
        Assert.False(firstValue.Has(migrationTarget));
        Assert.False(firstVelocity.Has(migrationTarget));
        first.DestroyEntity(migrationNeighbor);
        first.DestroyEntity(destroyNeighbor);
        Assert.Equal(41, firstValue.Get(entity).Value);
        Assert.Equal(12, firstVelocity.Get(entity).Value);

        first.DestroyEntity(entity);
        var recycled = first.CreateEntity<ValueComponent>(new ValueComponent { Value = 77 });
        Assert.Equal(entity.Id, recycled.Id);
        Assert.NotEqual(entity.Version, recycled.Version);
        Assert.False(firstValue.TryGet(entity, out _));
        Assert.False(firstVelocity.Has(entity));
        Assert.True(firstValue.TryGet(recycled, out var current));
        Assert.Equal(77, current.Value);
    }

    [Fact]
    public void Accessor_ShouldBeInvalidatedWhenWorldIsDisposed()
    {
        var world = new World(new WorldOptions(diagnosticsEnabled: true));
        var accessor = world.GetAccessor<ValueComponent>();
        var entity = world.CreateEntity<ValueComponent>(new ValueComponent { Value = 8 });
        Assert.True(accessor.Has(entity));

        world.Dispose();

        Assert.False(accessor.Has(entity));
        Assert.False(accessor.TryGet(entity, out _));
        Assert.Throws<InvalidOperationException>(() => ReadMissing(accessor, entity));
    }

    [Fact]
    public void DirectAndBatchCreation_ShouldPreserveDataQueriesAndReusedIds()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 4, diagnosticsEnabled: true));
        var one = world.CreateEntity<ValueComponent>(new ValueComponent { Value = 1 });
        var two = world.CreateEntity<ValueComponent, VelocityComponent>(
            new ValueComponent { Value = 2 }, new VelocityComponent { Value = 20 });
        var three = world.CreateEntity<ValueComponent, VelocityComponent, OtherComponent>(
            new ValueComponent { Value = 3 }, new VelocityComponent { Value = 30 }, new OtherComponent { Value = 300 });

        Span<Entity> batch = stackalloc Entity[257];
        world.CreateEntities<ValueComponent, VelocityComponent>(batch,
            new ValueComponent { Value = 10 }, new VelocityComponent { Value = 100 });

        var factory = world.PrepareEntity<ValueComponent, VelocityComponent>();
        var factoryOne = factory.Create(new ValueComponent { Value = 20 }, new VelocityComponent { Value = 200 });
        Span<Entity> factoryBatch = stackalloc Entity[3];
        factory.CreateBatch(factoryBatch, new ValueComponent { Value = 30 }, new VelocityComponent { Value = 300 });

        var expectedValues = new[] { 1, 2, 3, 20 }.Concat(Enumerable.Repeat(10, batch.Length))
            .Concat(Enumerable.Repeat(30, factoryBatch.Length)).Order().ToArray();
        Assert.Equal(expectedValues, OptimizationQueryAssertions.SelectValues(world.Query<ValueComponent>()));
        Assert.Equal(263, OptimizationQueryAssertions.CountEntities(world.Query<ValueComponent, VelocityComponent>()));
        Assert.Equal(1, OptimizationQueryAssertions.CountEntities(world.Query<ValueComponent, VelocityComponent, OtherComponent>()));
        Assert.True(world.TryGet(one, out ValueComponent first));
        Assert.Equal(1, first.Value);
        Assert.True(world.TryGet(two, out VelocityComponent second));
        Assert.Equal(20, second.Value);
        Assert.True(world.TryGet(three, out OtherComponent third));
        Assert.Equal(300, third.Value);
        Assert.True(world.TryGet(factoryOne, out VelocityComponent factoryValue));
        Assert.Equal(200, factoryValue.Value);
        foreach (var created in factoryBatch)
        {
            Assert.True(world.TryGet(created, out ValueComponent factoryBatchValue));
            Assert.Equal(30, factoryBatchValue.Value);
        }

        world.DestroyEntity(batch[0]);
        var reused = world.CreateEntity<ValueComponent>(new ValueComponent { Value = 99 });
        Assert.Equal(batch[0].Id, reused.Id);
        Assert.NotEqual(batch[0].Version, reused.Version);
        Assert.Throws<ArgumentException>(() => world.CreateEntity<ValueComponent, ValueComponent>(
            new ValueComponent { Value = 4 }, new ValueComponent { Value = 5 }));
    }

    private struct ValueComponent { public int Value; }
    private struct VelocityComponent { public int Value; }
    private struct OtherComponent { public int Value; }

    private static int ReadMissing<T>(ComponentAccessor<T> accessor, Entity entity) where T : unmanaged
        => accessor.Get(entity).GetHashCode();

    private struct CollectValuesProcessor : IQueryProcessor<ValueComponent>
    {
        public HashSet<Entity> Entities;

        public CollectValuesProcessor() => Entities = new HashSet<Entity>();

        public void Execute(Entity entity, ref ValueComponent component1) => Entities.Add(entity);
    }

    private static class OptimizationQueryAssertions
    {
        public static List<int> SelectValues(Query<ValueComponent> query)
        {
            var processor = new ValueListProcessor();
            query.ForEach(ref processor);
            return processor.Values.Order().ToList();
        }

        public static int CountEntities<T1, T2>(Query<T1, T2> query)
            where T1 : unmanaged where T2 : unmanaged
        {
            var processor = new CountProcessor<T1, T2>();
            query.ForEach(ref processor);
            return processor.Count;
        }

        public static int CountEntities<T1, T2, T3>(Query<T1, T2, T3> query)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            var processor = new CountProcessor<T1, T2, T3>();
            query.ForEach(ref processor);
            return processor.Count;
        }

        private struct ValueListProcessor : IQueryProcessor<ValueComponent>
        {
            public List<int> Values;
            public ValueListProcessor() => Values = new List<int>();
            public void Execute(Entity entity, ref ValueComponent component1) => Values.Add(component1.Value);
        }

        private struct CountProcessor<T1, T2> : IQueryProcessor<T1, T2>
            where T1 : unmanaged where T2 : unmanaged
        {
            public int Count;
            public void Execute(Entity entity, ref T1 component1, ref T2 component2) => Count++;
        }

        private struct CountProcessor<T1, T2, T3> : IQueryProcessor<T1, T2, T3>
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            public int Count;
            public void Execute(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3) => Count++;
        }
    }
}
