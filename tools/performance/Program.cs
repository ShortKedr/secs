using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SpaceBattle;

if (args.Contains("--verify-sweep")) { SweepCheck.Run(); return; }

string Arg(string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index < 0 ? fallback : args[index + 1];
}
var ticks = int.Parse(Arg("--ticks", "1200"));
var phaseTicks = int.Parse(Arg("--phase-ticks", "600"));
var repeats = int.Parse(Arg("--repeats", "3"));
var output = Path.GetFullPath(Arg("--output", "Benchmarks.Local/tmp/performance-study/native.json"));
var root = Path.GetFullPath(Arg("--project-root", Directory.GetCurrentDirectory()));
if (!output.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Output must stay inside the project.");
if (ticks < 1 || phaseTicks < 1 || repeats < 1) throw new ArgumentException("Positive counts required.");
var config = new BattleConfig { Seed = 7331 };
// Warm up code on a separate battle, not by changing the measured starting state.
using (var warmup = new Battle<SkecsStore>(new(), config))
    for (var i = 0; i < ticks; i++) warmup.Step();
var runs = new List<object>();
for (var run = 0; run < repeats; run++)
{
    using var battle = new Battle<SkecsStore>(new(), config);
    var duration = new double[ticks];
    var stages = Battle<SkecsStore>.StageNames.Select(_ => new double[ticks]).ToArray();
    var allocated = new long[ticks];
    var counts = new int[ticks][];
    var phases = new List<object>();
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var gcStart = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
    for (var i = 0; i < ticks; i++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        battle.Step();
        allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        duration[i] = battle.LastTickMs;
        for (var s = 0; s < stages.Length; s++) stages[s][i] = battle.LastStageMs[s];
        counts[i] = [battle.ShipCount, battle.ProjectileCount, battle.DebrisCount, battle.ParticleCount];
        if ((i + 1) % phaseTicks != 0 && i != ticks - 1) continue;
        var start = i / phaseTicks * phaseTicks;
        var length = i - start + 1;
        phases.Add(new
        {
            StartTick = start + 1, EndTick = i + 1, CoreStep = Stats(duration.Skip(start).Take(length)),
            Stages = Battle<SkecsStore>.StageNames.Select((name, s) => new { Name = name, Timing = Stats(stages[s].Skip(start).Take(length)) }).ToArray(),
            AllocatedBytes = allocated.Skip(start).Take(length).Sum(),
            MeanCounts = Enumerable.Range(0, 4).Select(c => counts.Skip(start).Take(length).Average(row => row[c])).ToArray(),
            FinalCounts = counts[i], Store = battle.Store.Diagnostics(),
            Checksum = battle.Checksum().ToString("X16"), Counters = battle.Counters.Copy()
        });
    }
    battle.Validate();
    runs.Add(new { Run = run + 1, Phases = phases, GcCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - gcStart[g]).ToArray() });
    Console.WriteLine($"Completed independent native replay {run + 1}/{repeats}");
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    Revision = Arg("--revision", "working-tree"), MeasuredAt = DateTimeOffset.UtcNow,
    Runtime = RuntimeInformation.FrameworkDescription, Os = RuntimeInformation.OSDescription,
    LogicalProcessors = Environment.ProcessorCount, Ticks = ticks, PhaseTicks = phaseTicks, Repeats = repeats, Config = config,
    Scope = "Core Step only; no snapshots, rendering or frontend statistics. Independent fresh seeded replays after separate warmup.",
    Runs = runs
}, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
Console.WriteLine(output);

static object Stats(IEnumerable<double> samples)
{
    var values = samples.Order().ToArray();
    double Percentile(double p) => values[(int)Math.Ceiling((values.Length - 1) * p)];
    return new { MeanMs = values.Average(), P50Ms = Percentile(.5), P95Ms = Percentile(.95), P99Ms = Percentile(.99), MaxMs = values[^1],
        BudgetMissPercent = 100.0 * values.Count(v => v > 1000.0 / 60) / values.Length };
}
