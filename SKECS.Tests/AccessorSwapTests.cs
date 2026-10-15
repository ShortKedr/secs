using SKECS;

namespace SKECS.Tests;

public sealed class AccessorSwapTests
{
    [Fact]
    public void LazyAccessors_ShouldFollowMigrationAndDestroySwapRemoval()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 4, diagnosticsEnabled: true));
        var a = world.CreateEntity(new Position { Value = 10 }, new Velocity { Value = 100 });
        var b = world.CreateEntity(new Position { Value = 20 }, new Velocity { Value = 200 });
        var c = world.CreateEntity(new Position { Value = 30 }, new Velocity { Value = 300 });
        var d = world.CreateEntity(new Position { Value = 40 }, new Velocity { Value = 400 });
        var positions = world.GetAccessor<Position>();
        var velocities = world.GetAccessor<Velocity>();
        AssertValues(a, 10, 100);
        AssertValues(b, 20, 200);
        AssertValues(c, 30, 300);
        AssertValues(d, 40, 400);

        // The full source chunk is [A,B,C,D]; migrating A swaps D into row 0.
        world.Add(a, new Other { Value = 1 });
        AssertValues(d, 40, 400);
        positions.Get(d).Value = 41;
        velocities.Get(d).Value = 401;
        Assert.Equal(41, world.Get<Position>(d).Value);
        Assert.Equal(401, world.Get<Velocity>(d).Value);
        AssertValues(a, 10, 100);

        // The source is now [D,B,C]; destroying B swaps C from row 2 to row 1.
        world.DestroyEntity(b);
        AssertValues(c, 30, 300);
        positions.Get(c).Value = 31;
        velocities.Get(c).Value = 301;
        Assert.Equal(31, world.Get<Position>(c).Value);
        Assert.Equal(301, world.Get<Velocity>(c).Value);
        Assert.False(positions.Has(b));
        Assert.False(velocities.Has(b));
        Assert.False(positions.TryGet(b, out _));
        Assert.False(velocities.TryGet(b, out _));
        Assert.Throws<InvalidOperationException>(() => ReadPosition(positions, b));
        Assert.Throws<InvalidOperationException>(() => ReadVelocity(velocities, b));

        Assert.True(world.Remove<Velocity>(a));
        Assert.Equal(10, positions.Get(a).Value);
        Assert.False(velocities.Has(a));
        Assert.False(velocities.TryGet(a, out _));
        Assert.Throws<InvalidOperationException>(() => ReadVelocity(velocities, a));
        world.Add(a, new Velocity { Value = 111 });
        AssertValues(a, 10, 111);
        velocities.Get(a).Value = 112;
        Assert.Equal(112, world.Get<Velocity>(a).Value);
        AssertValues(c, 31, 301);
        AssertValues(d, 41, 401);

        void AssertValues(Entity entity, int position, int velocity)
        {
            Assert.Equal(position, positions.Get(entity).Value);
            Assert.Equal(velocity, velocities.Get(entity).Value);
        }
    }

    [Fact]
    public void PreparedFactory_ShouldRejectCreationAfterDispose()
    {
        var world = new World(new WorldOptions(chunkCapacity: 4));
        var factory = world.PrepareEntity<Position, Velocity>();
        factory.Create(new Position { Value = 1 }, new Velocity { Value = 2 });
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => factory.Create(default, default));
        Assert.Throws<ObjectDisposedException>(() => factory.CreateBatch(new Entity[1], default, default));
    }

    [Fact]
    public void DuplicateFactoryTypes_ShouldNotGrowEntityCount()
    {
        using var world = new World(new WorldOptions(chunkCapacity: 4));
        world.CreateEntity(new Position { Value = 1 });
        var count = world.GetStorageStatistics().EntityCount;
        Assert.Throws<ArgumentException>(() => world.PrepareEntity<Position, Position>());
        Assert.Throws<ArgumentException>(() => world.CreateEntity<Position, Position>(default, default));
        Assert.Equal(count, world.GetStorageStatistics().EntityCount);
        Assert.Throws<ArgumentException>(() => world.CreateEntity<Position, Position, Velocity>(default, default, default));
        Assert.Equal(count, world.GetStorageStatistics().EntityCount);
        Assert.Throws<ArgumentException>(() => world.CreateEntity<Position, Velocity, Position>(default, default, default));
        Assert.Equal(count, world.GetStorageStatistics().EntityCount);
        Assert.Throws<ArgumentException>(() => world.CreateEntity<Velocity, Position, Position>(default, default, default));
        Assert.Equal(count, world.GetStorageStatistics().EntityCount);
    }

    private static int ReadPosition(ComponentAccessor<Position> accessor, Entity entity) => accessor.Get(entity).Value;
    private static int ReadVelocity(ComponentAccessor<Velocity> accessor, Entity entity) => accessor.Get(entity).Value;

    private struct Position { public int Value; }
    private struct Velocity { public int Value; }
    private struct Other { public int Value; }
}
