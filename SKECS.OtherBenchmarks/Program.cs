using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using SKECS;
using SKECS.OtherBenchmarks;

var config = DefaultConfig.Instance;
if (args.Length == 1 && args[0] == "--validate")
{
    ValidateBenchmarkLifecycles();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

static void ValidateBenchmarkLifecycles()
{
    var benchmarkTypes = typeof(Program).Assembly.GetTypes()
        .Where(type => !type.IsAbstract &&
            (typeof(CreateEntityBenchmarkBase).IsAssignableFrom(type) ||
             typeof(AddComponentBenchmarkBase).IsAssignableFrom(type) ||
             typeof(RemoveComponentBenchmarkBase).IsAssignableFrom(type) ||
             typeof(StructuralChangeBenchmarkBase).IsAssignableFrom(type) ||
             type == typeof(DeleteEntity)))
        .OrderBy(type => type.Name)
        .ToArray();

    var cycles = 0;
    foreach (var entityCount in new[] { 257, 1_000 })
    {
        foreach (var type in benchmarkTypes)
        {
            var benchmark = Activator.CreateInstance(type)!;
            type.GetProperty("EntityCount")!.SetValue(benchmark, entityCount);
            Invoke(benchmark, "GlobalSetup");
            try
            {
                var entities = FindEntityArray(benchmark);
                var world = FindContext(benchmark).World;
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    InvokeIfPresent(benchmark, "IterationSetup");
                    if (!typeof(CreateEntityBenchmarkBase).IsAssignableFrom(type))
                    {
                        AssertAlive(world, entities, entityCount, $"{type.Name} setup");
                    }

                    Invoke(benchmark, "Run");
                    if (type == typeof(DeleteEntity))
                    {
                        AssertAlive(world, entities, 0, $"{type.Name} run");
                    }
                    else
                    {
                        AssertAlive(world, entities, entityCount, $"{type.Name} run");
                    }

                    InvokeIfPresent(benchmark, "IterationCleanup");
                    AssertAlive(world, entities, 0, $"{type.Name} cleanup");
                    cycles++;
                }
            }
            finally
            {
                Invoke(benchmark, "GlobalCleanup");
                (benchmark as IDisposable)?.Dispose();
            }
        }
    }

    Console.WriteLine($"Lifecycle validation passed: {benchmarkTypes.Length} benchmarks, {cycles} cycles, N=257 and N=1000.");
}

static void Invoke(object instance, string methodName) =>
    instance.GetType().GetMethod(methodName)!.Invoke(instance, null);

static void InvokeIfPresent(object instance, string methodName) =>
    instance.GetType().GetMethod(methodName)?.Invoke(instance, null);

static Entity[] FindEntityArray(object instance) =>
    FindFieldValue<Entity[]>(instance, "Entities") ?? FindFieldValue<Entity[]>(instance, "_entities")
    ?? throw new InvalidOperationException($"No tracked entity array found on {instance.GetType().Name}.");

static BenchmarkWorldContext FindContext(object instance) =>
    FindFieldValue<BenchmarkWorldContext>(instance, "Context") ?? FindFieldValue<BenchmarkWorldContext>(instance, "_context")
    ?? throw new InvalidOperationException($"No world context found on {instance.GetType().Name}.");

static T? FindFieldValue<T>(object instance, string name) where T : class
{
    for (var type = instance.GetType(); type is not null; type = type.BaseType)
    {
        var field = type.GetField(name, System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
        if (field?.GetValue(instance) is T value)
        {
            return value;
        }
    }

    return null;
}

static void AssertAlive(World world, Entity[] entities, int expected, string phase)
{
    var alive = entities.Count(world.IsAlive);
    if (alive != expected)
    {
        throw new InvalidOperationException($"{phase}: expected {expected} live tracked entities, found {alive}.");
    }
}
