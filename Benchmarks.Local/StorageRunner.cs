using System.Collections;
using System.Reflection;
using System.Text.Json;
using SKECS;
using SKECS.Benchmarks;

static class StorageRunner
{
    public static bool Handle(string[] args)
    {
        if (!args.Contains("--storage")) return false;
        using var world = new World(new WorldOptions(chunkCapacity: 256));
        var entities = new Entity[100_000];
        for (var i = 0; i < entities.Length; i++)
        {
            entities[i] = world.CreateEntity();
            world.Add(entities[i], StressData.Position(i));
            world.Add(entities[i], StressData.Velocity);
        }
        var order = Enumerable.Range(0, entities.Length).ToArray();
        new Random(2048).Shuffle(order);
        var samples = new List<object>();
        for (var pass = 0; pass <= 20; pass++)
        {
            if (pass > 0)
            {
                foreach (var i in order) world.Add(entities[i], StressData.Payload(i));
                foreach (var i in order) world.Remove<StressPayload>(entities[i]);
            }
            foreach (var i in order)
                if (world.Get<StressPosition>(entities[i]).X != i || world.Has<StressPayload>(entities[i]))
                    throw new InvalidOperationException("Storage churn data mismatch");
            // Reflection keeps this diagnostic executable compatible with the original preserved binary.
            var archetypes = (IEnumerable)typeof(World).GetField("_archetypeList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            var chunkCount = 0;
            long nativeBytes = 0;
            foreach (var archetype in archetypes)
            {
                var chunks = (IList)archetype.GetType().GetProperty("Chunks")!.GetValue(archetype)!;
                var stride = (int)archetype.GetType().GetField("_stride", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(archetype)!;
                chunkCount += chunks.Count;
                nativeBytes += (long)stride * chunks.Count;
            }
            samples.Add(new { pass, chunkCount, nativeBytes });
            Console.WriteLine($"Storage pass {pass}: {chunkCount} chunks, {nativeBytes:N0} native bytes");
        }
        var output = args.FirstOrDefault(a => a.EndsWith(".json")) ?? "Benchmarks.Local/storage-optimized.json";
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            metadata = new { entityCount = entities.Length, passes = 20, payloadBytes = 256,
                revision = Environment.GetEnvironmentVariable("SKECS_BENCHMARK_REVISION") ?? "optimized working tree",
                note = "Retained native column storage only, excluding managed arrays and allocator overhead; not timed" }, samples
        }, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
