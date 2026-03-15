using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SECS;

BenchmarkRunner.Run<EcsBenchmarks>();

[MemoryDiagnoser]
public class EcsBenchmarks
{
    private World _world = null!;
    private Entity[] _entities = null!;

    [Params(100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World(new WorldOptions(chunkCapacity: 256));
        _entities = new Entity[EntityCount];

        for (var i = 0; i < EntityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.Add(entity, new Position { X = i, Y = i });
            _world.Add(entity, new Velocity { X = 1, Y = -1 });
            _entities[i] = entity;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world.Dispose();
    }

    [Benchmark]
    public void LinearIteration()
    {
        var processor = new MoveProcessor();
        _world.Query<Position, Velocity>().ForEach(ref processor);
    }

    [Benchmark]
    public void AddRemoveStructuralChurn()
    {
        for (var i = 0; i < _entities.Length; i++)
        {
            _world.Add(_entities[i], new Mass { Value = 10f });
        }

        for (var i = 0; i < _entities.Length; i++)
        {
            _world.Remove<Mass>(_entities[i]);
        }
    }

    private struct Position
    {
        public float X;
        public float Y;
    }

    private struct Velocity
    {
        public float X;
        public float Y;
    }

    private struct Mass
    {
        public float Value;
    }

    private struct MoveProcessor : IQueryProcessor<Position, Velocity>
    {
        public void Execute(Entity entity, ref Position component1, ref Velocity component2)
        {
            _ = entity;
            component1.X += component2.X;
            component1.Y += component2.Y;
        }
    }
}
