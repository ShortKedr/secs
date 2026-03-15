using System.Reflection;
using BenchmarkDotNet.Attributes;
using SECS;

namespace SECS.OtherBenchmarks;

public static class BenchmarkConstants
{
    public const int Seed = 2052513957;
    public const int SmallEntityCount = 1_000;
    public const int MidEntityCount = 100_000;
    public const int LargeEntityCount = 500_000;
    public const int EntityCount = MidEntityCount;
    public const int SystemEntityCount = MidEntityCount;
}

public readonly struct ComponentRecipe
{
    public ComponentRecipe(params int[] componentIds)
    {
        ComponentIds = componentIds;
    }

    public int[] ComponentIds { get; }
}

public static class BenchmarkRecipes
{
    public static readonly ComponentRecipe Empty = new(Array.Empty<int>());
    public static readonly ComponentRecipe C1 = new(1);
    public static readonly ComponentRecipe C2 = new(2);
    public static readonly ComponentRecipe C3 = new(3);
    public static readonly ComponentRecipe C4 = new(4);
    public static readonly ComponentRecipe C1C2 = new(1, 2);
    public static readonly ComponentRecipe C2C3 = new(2, 3);
    public static readonly ComponentRecipe C3C4 = new(3, 4);
    public static readonly ComponentRecipe C4C1 = new(4, 1);
    public static readonly ComponentRecipe C1C2C3 = new(1, 2, 3);
    public static readonly ComponentRecipe C2C3C4 = new(2, 3, 4);
    public static readonly ComponentRecipe C3C4C1 = new(3, 4, 1);
    public static readonly ComponentRecipe C4C1C2 = new(4, 1, 2);
    public static readonly ComponentRecipe C1C2C3C4 = new(1, 2, 3, 4);

    public static readonly ComponentRecipe[] Random1 = [C1, C2, C3, C4];
    public static readonly ComponentRecipe[] Random2 = [C1C2, C2C3, C3C4, C4C1];
    public static readonly ComponentRecipe[] Random3 = [C1C2C3, C2C3C4, C3C4C1, C4C1C2];
}

public static class ComponentCatalog
{
    public static readonly Type[] Types =
    [
        typeof(void),
        typeof(Component1), typeof(Component2), typeof(Component3), typeof(Component4), typeof(Component5),
        typeof(Component6), typeof(Component7), typeof(Component8), typeof(Component9), typeof(Component10),
        typeof(Component11), typeof(Component12), typeof(Component13), typeof(Component14), typeof(Component15),
        typeof(Component16), typeof(Component17), typeof(Component18), typeof(Component19), typeof(Component20),
        typeof(Component21), typeof(Component22), typeof(Component23), typeof(Component24), typeof(Component25),
        typeof(Component26), typeof(Component27), typeof(Component28), typeof(Component29), typeof(Component30),
        typeof(Component31), typeof(Component32), typeof(Component33), typeof(Component34), typeof(Component35),
        typeof(Component36), typeof(Component37), typeof(Component38), typeof(Component39), typeof(Component40),
        typeof(Component41), typeof(Component42), typeof(Component43), typeof(Component44), typeof(Component45),
        typeof(Component46), typeof(Component47), typeof(Component48), typeof(Component49), typeof(Component50),
        typeof(Component51), typeof(Component52), typeof(Component53), typeof(Component54), typeof(Component55),
        typeof(Component56), typeof(Component57), typeof(Component58), typeof(Component59), typeof(Component60),
        typeof(Component61), typeof(Component62), typeof(Component63), typeof(Component64), typeof(Component65),
        typeof(Component66), typeof(Component67), typeof(Component68), typeof(Component69), typeof(Component70),
        typeof(Component71), typeof(Component72), typeof(Component73), typeof(Component74), typeof(Component75),
        typeof(Component76), typeof(Component77), typeof(Component78), typeof(Component79), typeof(Component80),
        typeof(Component81), typeof(Component82), typeof(Component83), typeof(Component84), typeof(Component85),
        typeof(Component86), typeof(Component87), typeof(Component88), typeof(Component89), typeof(Component90),
        typeof(Component91), typeof(Component92), typeof(Component93), typeof(Component94), typeof(Component95),
        typeof(Component96), typeof(Component97), typeof(Component98), typeof(Component99), typeof(Component100),
    ];

    private static readonly Action<World, Entity>[] Adders;
    private static readonly Action<World, Entity>[] Removers;

    static ComponentCatalog()
    {
        Adders = new Action<World, Entity>[Types.Length];
        Removers = new Action<World, Entity>[Types.Length];

        for (var i = 1; i < Types.Length; i++)
        {
            Adders[i] = CreateAddAction(Types[i]);
            Removers[i] = CreateRemoveAction(Types[i]);
        }
    }

    public static void Add(World world, Entity entity, int componentId) => Adders[componentId](world, entity);

    public static void Remove(World world, Entity entity, int componentId) => Removers[componentId](world, entity);

    public static Action<World> CreateMismatchSystem(World world, int component1Id, int component2Id)
    {
        return (Action<World>)typeof(ComponentCatalog)
            .GetMethod(nameof(CreateMismatchSystemGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(Types[component1Id], Types[component2Id])
            .Invoke(null, [world])!;
    }

    private static Action<World, Entity> CreateAddAction(Type componentType)
    {
        return (Action<World, Entity>)typeof(ComponentCatalog)
            .GetMethod(nameof(CreateAddActionGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(componentType)
            .Invoke(null, null)!;
    }

    private static Action<World, Entity> CreateRemoveAction(Type componentType)
    {
        return (Action<World, Entity>)typeof(ComponentCatalog)
            .GetMethod(nameof(CreateRemoveActionGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(componentType)
            .Invoke(null, null)!;
    }

    private static Action<World, Entity> CreateAddActionGeneric<T>()
        where T : unmanaged
        => static (world, entity) => world.Add(entity, default(T));

    private static Action<World, Entity> CreateRemoveActionGeneric<T>()
        where T : unmanaged
        => static (world, entity) => world.Remove<T>(entity);

    private static Action<World> CreateMismatchSystemGeneric<T1, T2>(World world)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        var query = world.QueryBuilder().With<T1>().With<T2>().Build();
        return worldInstance =>
        {
            foreach (var chunk in query.Chunks())
            {
                _ = worldInstance;
                _ = chunk.Count;
            }
        };
    }
}

public sealed class BenchmarkWorldContext : IDisposable
{
    public BenchmarkWorldContext()
    {
        World = new World(new WorldOptions(chunkCapacity: 256));
    }

    public World World { get; }

    public Entity[] PrepareSet(int count) => new Entity[count];

    public void CreateEmptyEntities(Entity[] entities)
    {
        for (var i = 0; i < entities.Length; i++)
        {
            entities[i] = World.CreateEntity();
        }
    }

    public void CreateEntities(Entity[] entities, ComponentRecipe recipe)
    {
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = World.CreateEntity();
            ApplyRecipe(entity, recipe);
            entities[i] = entity;
        }
    }

    public void AddComponents(Entity[] entities, ComponentRecipe recipe)
    {
        for (var i = 0; i < entities.Length; i++)
        {
            ApplyRecipe(entities[i], recipe);
        }
    }

    public void RemoveComponents(Entity[] entities, ComponentRecipe recipe)
    {
        for (var i = 0; i < entities.Length; i++)
        {
            RemoveRecipe(entities[i], recipe);
        }
    }

    public void DeleteEntities(Entity[] entities)
    {
        for (var i = 0; i < entities.Length; i++)
        {
            if (World.IsAlive(entities[i]))
            {
                World.DestroyEntity(entities[i]);
            }
        }
    }

    public void Shuffle(Entity[] entities, Random random)
    {
        for (var n = entities.Length; n > 1; n--)
        {
            var k = random.Next(n);
            (entities[n - 1], entities[k]) = (entities[k], entities[n - 1]);
        }
    }

    public void Dispose() => World.Dispose();

    private void ApplyRecipe(Entity entity, ComponentRecipe recipe)
    {
        var ids = recipe.ComponentIds;
        for (var i = 0; i < ids.Length; i++)
        {
            ComponentCatalog.Add(World, entity, ids[i]);
        }
    }

    private void RemoveRecipe(Entity entity, ComponentRecipe recipe)
    {
        var ids = recipe.ComponentIds;
        for (var i = 0; i < ids.Length; i++)
        {
            ComponentCatalog.Remove(World, entity, ids[i]);
        }
    }
}

public abstract class CreateEntityBenchmarkBase
{
    [Params(BenchmarkConstants.EntityCount)]
    public int EntityCount { get; set; }

    protected BenchmarkWorldContext Context = null!;
    protected Entity[] Entities = null!;

    protected abstract ComponentRecipe[] Recipes { get; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        Context = new BenchmarkWorldContext();
        Entities = Context.PrepareSet(EntityCount);
    }

    [Benchmark]
    public void Run()
    {
        if (Recipes.Length == 1 && Recipes[0].ComponentIds.Length == 0)
        {
            Context.CreateEmptyEntities(Entities);
            return;
        }

        var random = new Random(BenchmarkConstants.Seed);
        for (var i = 0; i < Entities.Length; i++)
        {
            var entity = Context.World.CreateEntity();
            var recipe = Recipes.Length == 1 ? Recipes[0] : Recipes[random.Next(Recipes.Length)];
            var ids = recipe.ComponentIds;
            for (var j = 0; j < ids.Length; j++)
            {
                ComponentCatalog.Add(Context.World, entity, ids[j]);
            }

            Entities[i] = entity;
        }
    }

    [IterationCleanup]
    public void IterationCleanup() => Context.DeleteEntities(Entities);

    [GlobalCleanup]
    public void GlobalCleanup() => Context.Dispose();
}

public abstract class AddComponentBenchmarkBase
{
    [Params(BenchmarkConstants.EntityCount)]
    public int EntityCount { get; set; }

    protected BenchmarkWorldContext Context = null!;
    protected Entity[] Entities = null!;
    protected Random Random = null!;

    protected abstract ComponentRecipe[] Recipes { get; }
    protected virtual bool ShuffleBeforeRun => false;

    [GlobalSetup]
    public void GlobalSetup()
    {
        Context = new BenchmarkWorldContext();
        Entities = Context.PrepareSet(EntityCount);
        Random = new Random(BenchmarkConstants.Seed);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        Context.CreateEmptyEntities(Entities);
        if (ShuffleBeforeRun)
        {
            Context.Shuffle(Entities, Random);
        }
    }

    [Benchmark]
    public void Run()
    {
        if (Recipes.Length == 1)
        {
            Context.AddComponents(Entities, Recipes[0]);
            return;
        }

        for (var i = 0; i < Entities.Length; i++)
        {
            var recipe = Recipes[Random.Next(Recipes.Length)];
            var ids = recipe.ComponentIds;
            for (var j = 0; j < ids.Length; j++)
            {
                ComponentCatalog.Add(Context.World, Entities[i], ids[j]);
            }
        }
    }

    [IterationCleanup]
    public void IterationCleanup() => Context.DeleteEntities(Entities);

    [GlobalCleanup]
    public void GlobalCleanup() => Context.Dispose();
}

public abstract class RemoveComponentBenchmarkBase
{
    [Params(BenchmarkConstants.EntityCount)]
    public int EntityCount { get; set; }

    protected BenchmarkWorldContext Context = null!;
    protected Entity[] Entities = null!;
    protected Random Random = null!;

    protected abstract ComponentRecipe Recipe { get; }
    protected virtual bool ShuffleBeforeRun => false;

    [GlobalSetup]
    public void GlobalSetup()
    {
        Context = new BenchmarkWorldContext();
        Entities = Context.PrepareSet(EntityCount);
        Random = new Random(BenchmarkConstants.Seed);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        Context.CreateEntities(Entities, Recipe);
        if (ShuffleBeforeRun)
        {
            Context.Shuffle(Entities, Random);
        }
    }

    [Benchmark]
    public void Run() => Context.RemoveComponents(Entities, Recipe);

    [IterationCleanup]
    public void IterationCleanup() => Context.DeleteEntities(Entities);

    [GlobalCleanup]
    public void GlobalCleanup() => Context.Dispose();
}

public abstract class StructuralChangeBenchmarkBase
{
    [Params(BenchmarkConstants.EntityCount)]
    public int EntityCount { get; set; }

    protected BenchmarkWorldContext Context = null!;
    protected Entity[] Entities = null!;

    protected abstract ComponentRecipe InitialRecipe { get; }
    protected abstract ComponentRecipe DeltaRecipe { get; }
    protected abstract bool IsAddOperation { get; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        Context = new BenchmarkWorldContext();
        Entities = Context.PrepareSet(EntityCount);
    }

    [IterationSetup]
    public void IterationSetup() => Context.CreateEntities(Entities, InitialRecipe);

    [Benchmark]
    public void Run()
    {
        if (IsAddOperation)
        {
            Context.AddComponents(Entities, DeltaRecipe);
        }
        else
        {
            Context.RemoveComponents(Entities, DeltaRecipe);
        }
    }

    [IterationCleanup]
    public void IterationCleanup() => Context.DeleteEntities(Entities);

    [GlobalCleanup]
    public void GlobalCleanup() => Context.Dispose();
}

public class DeleteEntity : IDisposable
{
    [Params(BenchmarkConstants.EntityCount)]
    public int EntityCount { get; set; }

    private BenchmarkWorldContext _context = null!;
    private Entity[] _entities = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _context = new BenchmarkWorldContext();
        _entities = _context.PrepareSet(EntityCount);
    }

    [IterationSetup]
    public void IterationSetup() => _context.CreateEmptyEntities(_entities);

    [Benchmark]
    public void Run()
    {
        _context.DeleteEntities(_entities);
        _entities = Array.Empty<Entity>();
    }

    [IterationCleanup]
    public void IterationCleanup() => _context.DeleteEntities(_entities);

    [GlobalCleanup]
    public void GlobalCleanup() => _context.Dispose();

    public void Dispose() => _context.Dispose();
}

public abstract class SystemBenchmarkBase
{
    [Params(BenchmarkConstants.SystemEntityCount)]
    public int EntityCount { get; set; }

    [Params(0, 10)]
    public int Padding { get; set; }

    protected BenchmarkWorldContext Context = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        Context = new BenchmarkWorldContext();
        ConfigureSystems();
        SeedEntities();
    }

    protected abstract void ConfigureSystems();

    protected abstract void SeedEntities();

    [Benchmark]
    public void Run() => Context.World.Run(SystemStage.Update);

    [GlobalCleanup]
    public void GlobalCleanup() => Context.Dispose();
}

public class MultiSystems : IDisposable
{
    [Params(BenchmarkConstants.SystemEntityCount)]
    public int EntityCount { get; set; }

    private BenchmarkWorldContext _context = null!;
    private Entity[] _set1 = null!;
    private Entity[] _set2 = null!;
    private Entity[] _set3 = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _context = new BenchmarkWorldContext();
        _set1 = _context.PrepareSet(EntityCount / 3);
        _set2 = _context.PrepareSet(EntityCount / 3);
        _set3 = _context.PrepareSet(EntityCount - _set1.Length - _set2.Length);

        var q1 = _context.World.Query<Component1>();
        var q2 = _context.World.Query<Component2>();
        var q3 = _context.World.Query<Component1, Component2>();

        _context.World.AddSystem(SystemStage.Update, _ =>
        {
            var processor = new IncrementSingleProcessor<Component1>();
            q1.ForEach(ref processor);
        });
        _context.World.AddSystem(SystemStage.Update, _ =>
        {
            var processor = new IncrementSingleProcessor<Component2>();
            q2.ForEach(ref processor);
        });
        _context.World.AddSystem(SystemStage.Update, _ =>
        {
            var processor = new AddPairProcessor<Component1, Component2>();
            q3.ForEach(ref processor);
        });

        _context.CreateEntities(_set1, BenchmarkRecipes.C1);
        _context.CreateEntities(_set2, BenchmarkRecipes.C2);
        _context.CreateEntities(_set3, BenchmarkRecipes.C1C2);
    }

    [Benchmark]
    public void Run() => _context.World.Run(SystemStage.Update);

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _context.DeleteEntities(_set1);
        _context.DeleteEntities(_set2);
        _context.DeleteEntities(_set3);
        _context.Dispose();
    }

    public void Dispose() => _context.Dispose();
}

public class FilterMismatchSystems : IDisposable
{
    [Params(BenchmarkConstants.SystemEntityCount)]
    public int EntityCount { get; set; }

    private BenchmarkWorldContext _context = null!;
    private Entity[][] _sets = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _context = new BenchmarkWorldContext();
        _sets = new Entity[100][];
        var portion = EntityCount / 100;

        for (var i = 0; i < 100; i++)
        {
            _context.World.AddSystem(SystemStage.Update, ComponentCatalog.CreateMismatchSystem(_context.World, i + 1, ((i + 1) % 100) + 1));
            var count = i == 99 ? EntityCount - (portion * 99) : portion;
            _sets[i] = _context.PrepareSet(count);
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        for (var i = 0; i < _sets.Length; i++)
        {
            _context.CreateEntities(_sets[i], new ComponentRecipe(i + 1));
        }
    }

    [Benchmark]
    public void Run() => _context.World.Run(SystemStage.Update);

    [IterationCleanup]
    public void IterationCleanup()
    {
        for (var i = 0; i < _sets.Length; i++)
        {
            _context.DeleteEntities(_sets[i]);
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _context.Dispose();

    public void Dispose() => _context.Dispose();
}

public struct IncrementSingleProcessor<T> : IQueryProcessor<T>
    where T : unmanaged, IIntComponent
{
    public void Execute(Entity entity, ref T component1)
    {
        _ = entity;
        component1.Value++;
    }
}

public struct AddPairProcessor<T1, T2> : IQueryProcessor<T1, T2>
    where T1 : unmanaged, IIntComponent
    where T2 : unmanaged, IIntComponent
{
    public void Execute(Entity entity, ref T1 component1, ref T2 component2)
    {
        _ = entity;
        component1.Value += component2.Value;
    }
}

public struct AddTripleProcessor<T1, T2, T3> : IQueryProcessor<T1, T2, T3>
    where T1 : unmanaged, IIntComponent
    where T2 : unmanaged, IIntComponent
    where T3 : unmanaged, IIntComponent
{
    public void Execute(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3)
    {
        _ = entity;
        component1.Value += component2.Value + component3.Value;
    }
}
