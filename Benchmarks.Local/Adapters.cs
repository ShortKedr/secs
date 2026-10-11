using Scellecs.Morpeh;
using Leopotam.Ecs;
using Lite = Leopotam.EcsLite;
using Classic = Leopotam.Ecs;

interface IWorkload : IDisposable
{
    void Create();
    void Iterate();
    void Churn();
    void Destroy();
    void Validate(int iterations, bool destroyed = false);
}

sealed class SecsWorkload(int count) : IWorkload
{
    private readonly SECS.Benchmarks.ComparisonWorkload workload = new(count);
    public void Create() => workload.Create();
    public void Iterate() => workload.Iterate();
    public void Churn() => workload.Churn();
    public void Destroy() => workload.Destroy();
    public void Validate(int iterations, bool destroyed = false) => workload.Validate(iterations, destroyed);
    public void Dispose() => workload.Dispose();
}

struct Position : IComponent { public float X; public float Y; }
struct Velocity : IComponent { public float X; public float Y; }
struct Mass : IComponent { public float Value; }

static class Check
{
    public static void Data(Position p, Velocity v, int index, int iterations, bool hasMass)
    {
        if (p.X != index + iterations || p.Y != index - iterations || v.X != 1 || v.Y != -1 || hasMass)
            throw new InvalidOperationException("Component data mismatch");
    }
}

sealed class LiteWorkload : IWorkload
{
    private readonly Lite.EcsWorld world = new();
    private readonly int[] entities;
    private readonly Lite.EcsPool<Position> positions;
    private readonly Lite.EcsPool<Velocity> velocities;
    private readonly Lite.EcsPool<Mass> masses;
    private readonly Lite.EcsFilter filter;

    public LiteWorkload(int count)
    {
        entities = new int[count];
        positions = world.GetPool<Position>();
        velocities = world.GetPool<Velocity>();
        masses = world.GetPool<Mass>();
        filter = world.Filter<Position>().Inc<Velocity>().End();
    }

    public void Create()
    {
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = world.NewEntity();
            positions.Add(entity) = new Position { X = i, Y = i };
            velocities.Add(entity) = new Velocity { X = 1, Y = -1 };
            entities[i] = entity;
        }
    }

    public void Iterate()
    {
        foreach (var entity in filter)
        {
            ref var p = ref positions.Get(entity);
            ref var v = ref velocities.Get(entity);
            p.X += v.X;
            p.Y += v.Y;
        }
    }

    public void Churn()
    {
        foreach (var entity in entities) masses.Add(entity) = new Mass { Value = 10 };
        foreach (var entity in entities) masses.Del(entity);
    }
    public void Destroy() { foreach (var entity in entities) world.DelEntity(entity); }
    public void Validate(int iterations, bool destroyed = false)
    {
        if (filter.GetEntitiesCount() != (destroyed ? 0 : entities.Length)) throw new InvalidOperationException("Filter count mismatch");
        for (var i = 0; i < entities.Length; i++)
        {
            if ((world.GetEntityGen(entities[i]) > 0) == destroyed) throw new InvalidOperationException("Entity lifetime mismatch");
            if (!destroyed) Check.Data(positions.Get(entities[i]), velocities.Get(entities[i]), i, iterations, masses.Has(entities[i]));
        }
    }
    public void Dispose() => world.Destroy();
}

sealed class ClassicWorkload : IWorkload
{
    private readonly Classic.EcsWorld world = new();
    private readonly Classic.EcsEntity[] entities;
    private readonly Classic.EcsFilter<Position, Velocity> filter;

    public ClassicWorkload(int count)
    {
        entities = new Classic.EcsEntity[count];
        filter = (Classic.EcsFilter<Position, Velocity>)world.GetFilter(typeof(Classic.EcsFilter<Position, Velocity>));
    }
    public void Create()
    {
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = world.NewEntity();
            entity.Get<Position>() = new Position { X = i, Y = i };
            entity.Get<Velocity>() = new Velocity { X = 1, Y = -1 };
            entities[i] = entity;
        }
    }
    public void Iterate()
    {
        for (var i = 0; i < filter.GetEntitiesCount(); i++)
        {
            ref var p = ref filter.Get1(i);
            ref var v = ref filter.Get2(i);
            p.X += v.X;
            p.Y += v.Y;
        }
    }
    public void Churn()
    {
        foreach (var entity in entities) entity.Get<Mass>() = new Mass { Value = 10 };
        foreach (var entity in entities) entity.Del<Mass>();
    }
    public void Destroy() { foreach (var entity in entities) entity.Destroy(); }
    public void Validate(int iterations, bool destroyed = false)
    {
        if (filter.GetEntitiesCount() != (destroyed ? 0 : entities.Length)) throw new InvalidOperationException("Filter count mismatch");
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if (entity.IsAlive() == destroyed) throw new InvalidOperationException("Entity lifetime mismatch");
            if (!destroyed) Check.Data(entity.Get<Position>(), entity.Get<Velocity>(), i, iterations, entity.Has<Mass>());
        }
    }
    public void Dispose() => world.Destroy();
}

sealed class MorpehWorkload : IWorkload
{
    private readonly World world = World.Create();
    private readonly Entity[] entities;
    private readonly Stash<Position> positions;
    private readonly Stash<Velocity> velocities;
    private readonly Stash<Mass> masses;
    private readonly Filter filter;

    public MorpehWorkload(int count)
    {
        entities = new Entity[count];
        positions = world.GetStash<Position>();
        velocities = world.GetStash<Velocity>();
        masses = world.GetStash<Mass>();
        filter = world.Filter.With<Position>().With<Velocity>().Build();
    }
    public void Create()
    {
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = world.CreateEntity();
            positions.Add(entity) = new Position { X = i, Y = i };
            velocities.Add(entity) = new Velocity { X = 1, Y = -1 };
            entities[i] = entity;
        }
        world.Commit();
    }
    public void Iterate()
    {
        foreach (var entity in filter)
        {
            ref var p = ref positions.Get(entity);
            ref var v = ref velocities.Get(entity);
            p.X += v.X;
            p.Y += v.Y;
        }
    }
    public void Churn()
    {
        foreach (var entity in entities) masses.Add(entity) = new Mass { Value = 10 };
        world.Commit();
        foreach (var entity in entities) masses.Remove(entity);
        world.Commit();
    }
    public void Destroy()
    {
        foreach (var entity in entities) world.RemoveEntity(entity);
        world.Commit();
    }
    public void Validate(int iterations, bool destroyed = false)
    {
        if (filter.GetLengthSlow() != (destroyed ? 0 : entities.Length)) throw new InvalidOperationException("Filter count mismatch");
        for (var i = 0; i < entities.Length; i++)
        {
            if (world.IsDisposed(entities[i]) != destroyed) throw new InvalidOperationException("Entity lifetime mismatch");
            if (!destroyed) Check.Data(positions.Get(entities[i]), velocities.Get(entities[i]), i, iterations, masses.Has(entities[i]));
        }
    }
    public void Dispose() => world.Dispose();
}
