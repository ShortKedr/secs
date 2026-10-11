using SECS;

namespace SECS.Tests;

public sealed class DefaultWorldTests
{
    private struct Value { public int Number; }
    [Fact]
    public void DefaultWorldAndOptionsHaveUsableChunks()
    {
        Assert.Equal(256, new WorldOptions().ChunkCapacity);
        using var world = new World();
        Assert.Equal(256, world.ChunkCapacity);
        for (var i = 0; i < 600; i++)
        {
            var e = world.CreateEntity(); world.Add(e, new Value { Number = i });
            Assert.Equal(i, world.Get<Value>(e).Number);
        }
        Assert.Equal(600, world.GetStorageStatistics().EntityCount);
        using var explicitDefault = new World(default);
        Assert.True(explicitDefault.IsAlive(explicitDefault.CreateEntity()));
    }
}
