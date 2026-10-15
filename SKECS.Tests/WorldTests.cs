using System.Collections.Generic;
using SKECS;

namespace SKECS.Tests;

public sealed class WorldTests
{
    [Fact]
    public void AddAndGet_ShouldRoundTripComponents()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 4, diagnosticsEnabled: true));
        var entity = world.CreateEntity();

        world.Add(entity, new Position { X = 2, Y = 4 });

        ref var position = ref world.Get<Position>(entity);
        Assert.Equal(2, position.X);
        Assert.Equal(4, position.Y);
    }

    [Fact]
    public void Remove_ShouldPreserveRemainingComponents()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 2, diagnosticsEnabled: true));
        var entity = world.CreateEntity();
        world.Add(entity, new Position { X = 10, Y = 20 });
        world.Add(entity, new Velocity { X = 1, Y = 2 });

        var removed = world.Remove<Velocity>(entity);

        Assert.True(removed);
        Assert.True(world.Has<Position>(entity));
        Assert.False(world.Has<Velocity>(entity));
        Assert.True(world.TryGet(entity, out Position position));
        Assert.Equal(10, position.X);
        Assert.Equal(20, position.Y);
    }

    [Fact]
    public void Destroy_ShouldInvalidateEntityVersion()
    {
        using var world = new World(new WorldOptions(diagnosticsEnabled: true));
        var entity = world.CreateEntity();

        world.DestroyEntity(entity);
        var recycled = world.CreateEntity();

        Assert.False(world.IsAlive(entity));
        Assert.True(world.IsAlive(recycled));
        Assert.Equal(entity.Id, recycled.Id);
        Assert.NotEqual(entity.Version, recycled.Version);
    }

    [Fact]
    public void Query_ShouldTrackNewArchetypesWithoutRebuild()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 2, diagnosticsEnabled: true));
        var query = world.Query<Position, Velocity>();

        var first = world.CreateEntity();
        world.Add(first, new Position { X = 1, Y = 1 });
        world.Add(first, new Velocity { X = 2, Y = 3 });

        var second = world.CreateEntity();
        world.Add(second, new Position { X = 5, Y = 8 });
        world.Add(second, new Velocity { X = -1, Y = -1 });

        var processor = new CollectProcessor();
        query.ForEach(ref processor);

        Assert.Equal(2, processor.Entities.Count);
        Assert.Contains(first, processor.Entities);
        Assert.Contains(second, processor.Entities);
    }

    [Fact]
    public unsafe void ChunkIterationAndForEach_ShouldSeeSameData()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 2, diagnosticsEnabled: true));
        for (var i = 0; i < 5; i++)
        {
            var entity = world.CreateEntity();
            world.Add(entity, new Position { X = i, Y = i * 2 });
            world.Add(entity, new Velocity { X = 10, Y = -10 });
        }

        var query = world.Query<Position, Velocity>();
        var foreachProcessor = new SumProcessor();
        query.ForEach(ref foreachProcessor);

        var chunkSum = 0;
        foreach (var chunk in query.Chunks())
        {
            for (var row = 0; row < chunk.Count; row++)
            {
                chunkSum += chunk.Component1[row].X + chunk.Component1[row].Y;
            }
        }

        Assert.Equal(foreachProcessor.Sum, chunkSum);
    }

    [Fact]
    public void Systems_ShouldRunInStageOrder()
    {
        using var world = new World(new WorldOptions(diagnosticsEnabled: true));
        var calls = new List<string>();

        world.AddSystem(SystemStage.PreUpdate, _ => calls.Add("pre"));
        world.AddSystem(SystemStage.Update, _ => calls.Add("update"));
        world.AddSystem(SystemStage.PostUpdate, _ => calls.Add("post"));

        world.RunAll();

        Assert.Equal(new[] { "pre", "update", "post" }, calls);
    }

    [Fact]
    public void RepeatedStructuralChurn_ShouldKeepEntityLocationsValid()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 64, diagnosticsEnabled: true));
        var entities = new Entity[10_000];
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = world.CreateEntity();
            world.Add(entity, new Position { X = i, Y = i });
            world.Add(entity, new Velocity { X = 1, Y = -1 });
            entities[i] = entity;
        }

        for (var iteration = 0; iteration < 10; iteration++)
        {
            for (var i = 0; i < entities.Length; i++)
            {
                world.Add(entities[i], new Mass { Value = 10 });
            }

            for (var i = 0; i < entities.Length; i++)
            {
                Assert.True(world.Remove<Mass>(entities[i]));
            }
        }
    }

    private struct Position
    {
        public int X;
        public int Y;
    }

    private struct Velocity
    {
        public int X;
        public int Y;
    }

    private struct Mass
    {
        public int Value;
    }

    private struct CollectProcessor : IQueryProcessor<Position, Velocity>
    {
        public List<Entity> Entities;

        public CollectProcessor()
        {
            Entities = new List<Entity>();
        }

        public void Execute(Entity entity, ref Position component1, ref Velocity component2)
        {
            Entities.Add(entity);
        }
    }

    private struct SumProcessor : IQueryProcessor<Position, Velocity>
    {
        public int Sum;

        public void Execute(Entity entity, ref Position component1, ref Velocity component2)
        {
            _ = entity;
            Sum += component1.X + component1.Y;
        }
    }
}
