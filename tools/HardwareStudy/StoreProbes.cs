using System.Diagnostics;
using System.Text.Json;
using SpaceBattle;

internal static class StoreProbes
{
    public static void Run(string output)
    {
        const int count = 4096, cycles = 20;
        var results = new List<object>();
        Probe(-1);
        for (var run = 0; run < 5; run++) Probe(run);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { Count = count, Cycles = cycles,
            Scope = "Native isolated SECS microprobes, not Battle.Step and not AOT. Fresh worlds per variant/run, separate warmup. Structural row operations are code-audited counts, not instrumented copy-byte/repair counters. No gameplay creation contract changed.",
            Results = results }, new JsonSerializerOptions { WriteIndented = true }));

        void Probe(int run)
        {
            foreach (var accessors in new[] { false, true })
            foreach (var direct in new[] { false, true })
            {
                using var world = new SECS.World();
                var entities = new SECS.Entity[count];
                var accessor = accessors ? world.GetAccessor<Transform>() : default;
                var motion = new Motion(); var projectile = new Projectile();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var cycle = 0; cycle < cycles; cycle++)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var transform = new Transform { Angle = i };
                        var e = direct ? world.CreateEntity(transform, motion, projectile) : world.CreateEntity();
                        if (!direct) { world.Set(e, transform); world.Set(e, motion); world.Set(e, projectile); }
                        entities[i] = e;
                    }
                    for (var i = 0; i < count; i++) world.DestroyEntity(entities[i]);
                }
                var structuralMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var structuralBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                for (var i = 0; i < count; i++) entities[i] = world.CreateEntity(new Transform { Angle = i }, motion, projectile);
                start = Stopwatch.GetTimestamp(); float sum = 0;
                for (var cycle = 0; cycle < 100; cycle++)
                    for (var i = 0; i < count; i++) sum += accessors ? accessor.Get(entities[i]).Angle : world.Get<Transform>(entities[i]).Angle;
                var getMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var storage = world.GetStorageStatistics();
                if (run >= 0) results.Add(new { Run = run + 1, Accessors = accessors, DirectCreation = direct,
                    StructuralMs = structuralMs, StructuralAllocatedBytes = structuralBytes, GetMs = getMs, Sum = sum,
                    AccessorBytes = storage.AccessorBufferBytes,
                    RowInsertionsPerBirth = direct ? 1 : 4, RowRemovalsDuringBirth = direct ? 0 : 3,
                    ComponentAddsPerBirth = 3, EntityDestroys = count * cycles });
            }
        }
    }
}
