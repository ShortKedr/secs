using SECS;

namespace SECS.Benchmarks;

// Shared with the local comparison runner so SECS uses exactly the same workload.
public sealed class ComparisonWorkload : IDisposable
{
    private readonly World _world = new(new WorldOptions(chunkCapacity: 256));
    private readonly Entity[] _entities;
    private readonly Query<Position, Velocity> _query;

    public ComparisonWorkload(int entityCount)
    {
        _entities = new Entity[entityCount];
        _query = _world.Query<Position, Velocity>();
    }

    public void Create()
    {
        for (var i = 0; i < _entities.Length; i++)
        {
            var entity = _world.CreateEntity();
            _world.Add(entity, new Position { X = i, Y = i });
            _world.Add(entity, new Velocity { X = 1, Y = -1 });
            _entities[i] = entity;
        }
    }

    public void Iterate()
    {
        var processor = new MoveProcessor();
        _query.ForEach(ref processor);
    }

    public void Churn()
    {
        foreach (var entity in _entities) _world.Add(entity, new Mass { Value = 10 });
        foreach (var entity in _entities) _world.Remove<Mass>(entity);
    }

    public void Destroy()
    {
        foreach (var entity in _entities) _world.DestroyEntity(entity);
    }

    public void Validate(int iterations, bool destroyed = false)
    {
        for (var i = 0; i < _entities.Length; i++)
        {
            var entity = _entities[i];
            if (_world.IsAlive(entity) == destroyed) throw new InvalidOperationException("Entity lifetime mismatch");
            if (destroyed) continue;
            var position = _world.Get<Position>(entity);
            var velocity = _world.Get<Velocity>(entity);
            if (position.X != i + iterations || position.Y != i - iterations ||
                velocity.X != 1 || velocity.Y != -1 || _world.TryGet<Mass>(entity, out _))
                throw new InvalidOperationException("Component data mismatch");
        }
    }

    public void Dispose() => _world.Dispose();

    private struct Position { public float X; public float Y; }
    private struct Velocity { public float X; public float Y; }
    private struct Mass { public float Value; }

    private struct MoveProcessor : IQueryProcessor<Position, Velocity>
    {
        public void Execute(Entity entity, ref Position position, ref Velocity velocity)
        {
            position.X += velocity.X;
            position.Y += velocity.Y;
        }
    }
}
