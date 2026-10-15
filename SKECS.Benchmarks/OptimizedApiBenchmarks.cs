using BenchmarkDotNet.Attributes;
using SKECS;

namespace SKECS.Benchmarks;

[MemoryDiagnoser]
public class OptimizedApiCreationBenchmarks
{
    private static readonly StressPosition Position = new() { X = 17, Y = -23, Z = 41 };
    private static readonly StressVelocity Velocity = new() { X = 1, Y = -1, Z = 2 };
    private World _world = null!;
    private Entity[] _entities = null!;
    private EntityFactory<StressPosition, StressVelocity> _factory;

    [Params(100_000, 1_000_000)]
    public int EntityCount { get; set; }

    [IterationSetup]
    public void Setup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _world = new World(new WorldOptions(chunkCapacity: 256));
        _entities = new Entity[EntityCount];
        _factory = _world.PrepareEntity<StressPosition, StressVelocity>();
    }

    [IterationCleanup]
    public void Cleanup()
    {
        ValidateCreated(_world, _entities);
        _world.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void EmptyAddAdd()
    {
        for (var i = 0; i < _entities.Length; i++)
        {
            var entity = _world.CreateEntity();
            _world.Add(entity, Position);
            _world.Add(entity, Velocity);
            _entities[i] = entity;
        }
    }

    [Benchmark]
    public void CreateEntityComponents()
    {
        for (var i = 0; i < _entities.Length; i++)
            _entities[i] = _world.CreateEntity(Position, Velocity);
    }

    [Benchmark]
    public void PreparedFactoryBatch()
    {
        _factory.CreateBatch(_entities, Position, Velocity);
    }

    private static void ValidateCreated(World world, Entity[] entities)
    {
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if (!world.IsAlive(entity)) throw new InvalidOperationException($"Created entity {i} is not alive.");
            var position = world.Get<StressPosition>(entity);
            var velocity = world.Get<StressVelocity>(entity);
            if (position.X != Position.X || position.Y != Position.Y || position.Z != Position.Z ||
                velocity.X != Velocity.X || velocity.Y != Velocity.Y || velocity.Z != Velocity.Z)
                throw new InvalidOperationException($"Created entity {i} has invalid components.");
        }
    }
}

[MemoryDiagnoser]
public class OptimizedApiLookupBenchmarks
{
    private World _world = null!;
    private Entity[] _entities = null!;
    private int[] _order = null!;
    private ComponentAccessor<StressPosition> _accessor;
    private long _expectedChecksum;
    private long _lastChecksum;

    [Params(100_000, 1_000_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World(new WorldOptions(chunkCapacity: 256));
        _entities = new Entity[EntityCount];
        _order = Enumerable.Range(0, EntityCount).ToArray();
        new Random(2052513957).Shuffle(_order);
        for (var i = 0; i < EntityCount; i++)
        {
            var entity = _world.CreateEntity();
            _entities[i] = entity;
            _world.Add(entity, StressData.Position(i));
            _world.Add(entity, StressData.Velocity);
            var shape = StressData.Shape(i);
            if ((shape & 1) != 0) _world.Add(entity, new StressTag1 { Value = 1 });
            if ((shape & 2) != 0) _world.Add(entity, new StressTag2 { Value = 1 });
            if ((shape & 4) != 0) _world.Add(entity, new StressTag3 { Value = 1 });
            if ((shape & 8) != 0) _world.Add(entity, new StressTag4 { Value = 1 });
            if ((shape & 16) != 0) _world.Add(entity, new StressTag5 { Value = 1 });
            if ((shape & 32) != 0) _world.Add(entity, new StressTag6 { Value = 1 });
            if ((shape & 64) != 0) _world.Add(entity, new StressTag7 { Value = 1 });
            if ((shape & 128) != 0) _world.Add(entity, new StressTag8 { Value = 1 });
        }
        _accessor = _world.GetAccessor<StressPosition>();
        for (var i = 0; i < _order.Length; i++)
        {
            var p = StressData.Position(_order[i]);
            _expectedChecksum += (long)p.X + p.Y + p.Z;
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [IterationCleanup]
    public void ValidateSample()
    {
        if (_lastChecksum != _expectedChecksum) throw new InvalidOperationException("Lookup checksum mismatch.");
    }

    [Benchmark(Baseline = true)]
    public long WorldGet()
    {
        long checksum = 0;
        foreach (var index in _order)
        {
            ref var position = ref _world.Get<StressPosition>(_entities[index]);
            checksum += (long)position.X + position.Y + position.Z;
        }
        _lastChecksum = checksum;
        return checksum;
    }

    [Benchmark]
    public long CachedAccessorGet()
    {
        long checksum = 0;
        foreach (var index in _order)
        {
            ref var position = ref _accessor.Get(_entities[index]);
            checksum += (long)position.X + position.Y + position.Z;
        }
        _lastChecksum = checksum;
        return checksum;
    }
}
