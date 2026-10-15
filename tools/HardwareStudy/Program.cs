using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SpaceBattle;

string Arg(string name, string fallback) => Array.IndexOf(args, name) is var i && i >= 0 ? args[i + 1] : fallback;
var ticks = int.Parse(Arg("--ticks", "1200"));
var phaseTicks = int.Parse(Arg("--phase-ticks", "300"));
var repeats = int.Parse(Arg("--repeats", "5"));
var game = args.Contains("--game");
var config = new BattleConfig { Seed = uint.Parse(Arg("--seed", "7331")), ShipsPerTeam = int.Parse(Arg("--ships", "96")),
    FleetComposition = game ? FleetComposition.LightSwarm : FleetComposition.Uniform,
    EndlessReinforcements = game, ReplenishCarriers = !game,
    ChurnMultiplier = float.Parse(Arg("--churn", "1"), System.Globalization.CultureInfo.InvariantCulture),
    Physics = new PhysicsSettings { CollectTimings = args.Contains("--physics-timings") } };
var root = Path.GetFullPath(Directory.GetCurrentDirectory());
var output = Path.GetFullPath(Arg("--output", "Benchmarks.Local/tmp/hardware-opt/study.json"));
if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || File.Exists(output))
    throw new ArgumentException("Use a new output path inside the project.");
if (ticks <= 0 || phaseTicks <= 0 || repeats <= 0) throw new ArgumentException("Positive counts required.");
var runs = new List<object>();
if (args.Contains("--store-probes")) { StoreProbes.Run(output); return; }
var port = Arg("--port", "all");
if (port is "all" or "skecs") Run(static () => new SkecsStore());
if (port is "all" or "lite") Run(static () => new LeoEcsLiteStore());
if (port is "all" or "classic") Run(static () => new LeoEcsClassicStore());
if (port is "all" or "morpeh") Run(static () => new MorpehStore());
if (port is "all" or "static") Run(static () => new StaticEcsStore());
if (runs.Count == 0) throw new ArgumentException("Unknown port.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new { Runtime = RuntimeInformation.FrameworkDescription,
    Os = RuntimeInformation.OSDescription, Config = config, Ticks = ticks, PhaseTicks = phaseTicks, Repeats = repeats,
    Scope = "Independent fresh replays after separate full-length warmup. Step/stages exclude export/rendering/checkpoint reads. Capture(false) measured separately at phase end; no renderer. GC/managed/native storage bytes are distinct. StructuralAdds/Removes are gameplay counters, not backend migrations.",
    Runs = runs }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
Console.WriteLine(output);

void Run<T>(Func<T> factory) where T : IEcsStore
{
    using (var warm = new Battle<T>(factory(), config)) for (var i = 0; i < ticks; i++) warm.Step();
    for (var run = 0; run < repeats; run++)
    {
        using var battle = new Battle<T>(factory(), config);
        var frame = new BattleFrame(); battle.Capture(frame, false);
        var step = new double[phaseTicks];
        var stages = Battle<T>.StageNames.Select(_ => new double[phaseTicks]).ToArray();
        var counts = new long[5]; long bytes = 0;
        var physics = Enumerable.Range(0, 4).Select(_ => new double[phaseTicks]).ToArray();
        var phases = new List<object>();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var gcStart = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        for (var i = 0; i < ticks; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            battle.Step();
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            var slot = i % phaseTicks;
            step[slot] = battle.LastTickMs;
            for (var s = 0; s < stages.Length; s++) stages[s][slot] = battle.LastStageMs[s];
            var timing = battle.PhysicsTimings;
            physics[0][slot] = timing.ContactCandidatesMs; physics[1][slot] = timing.SolverMs;
            physics[2][slot] = timing.HitCandidatesMs; physics[3][slot] = timing.HitNarrowMs;
            counts[0] += battle.ShipCount; counts[1] += battle.ProjectileCount; counts[2] += battle.DebrisCount;
            counts[3] += battle.ParticleCount; counts[4] += battle.TurretCount;
            if (slot != phaseTicks - 1 && i != ticks - 1) continue;
            var count = slot + 1;
            var export = new double[20]; long exportBytes = 0;
            for (var k = 0; k < export.Length; k++)
            {
                before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                battle.Capture(frame, false);
                export[k] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                exportBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            battle.Validate();
            phases.Add(new { StartTick = i - slot + 1, EndTick = i + 1, Step = Stats(step, count),
                StepSamplesMs = step.AsSpan(0, count).ToArray(),
                Stages = Battle<T>.StageNames.Select((name, s) => new { Name = name, Timing = Stats(stages[s], count) }).ToArray(),
                AllocatedBytes = bytes, MeanCounts = counts.Select(c => (double)c / count).ToArray(),
                FinalCounts = new[] { battle.ShipCount, battle.ProjectileCount, battle.DebrisCount, battle.ParticleCount, battle.TurretCount },
                Checksum = battle.Checksum().ToString("X16"), Counters = battle.Counters.Copy(),
                Tactics = new { battle.Tactics.TargetSwitches, battle.Tactics.TargetRetentions, battle.Tactics.GroupOrders, battle.Tactics.CoverOrders },
                Export = Stats(export, export.Length), ExportAllocatedBytes = exportBytes,
                ManagedHeapBytes = GC.GetTotalMemory(false), Store = battle.Store.Diagnostics(),
                PhysicsQueries = battle.PhysicsQueries,
                PhysicsTimings = new[] { "contact-candidates", "solver", "hit-candidates", "hit-narrow" }.Select((name, s) =>
                    new { Name = name, Timing = Stats(physics[s], count) }).ToArray() });
            bytes = 0; Array.Clear(counts);
        }
        runs.Add(new { Port = battle.Framework, Run = run + 1, Phases = phases,
            GcCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - gcStart[g]).ToArray() });
        Console.WriteLine($"{battle.Framework}: independent replay {run + 1}/{repeats}");
    }
}

static object Stats(double[] values, int count)
{
    var sorted = values.AsSpan(0, count).ToArray(); Array.Sort(sorted);
    return new { MeanMs = sorted.Average(), P50Ms = sorted[(int)Math.Ceiling((count - 1) * .5)],
        P95Ms = sorted[(int)Math.Ceiling((count - 1) * .95)], P99Ms = sorted[(int)Math.Ceiling((count - 1) * .99)], MaxMs = sorted[^1] };
}
