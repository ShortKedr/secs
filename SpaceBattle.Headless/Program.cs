using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SpaceBattle;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
try
{
    if (args.Contains("--validate-deployment")) { FleetDeploymentValidation.Run(); return 0; }
    if (args.Contains("--validate-squads")) { SquadCompositionValidation.Run(); TacticalScoringValidation.Run(); return 0; }
    if (args.Contains("--validate-legacy-tactics")) { Validation.LegacyTacticsChecks(); return 0; }
    if (args.Contains("--validate-damage")) { WeaponDamageValidation.Run(); AiLocalityValidation.RunDamage(); return 0; }
    if (args.Contains("--damage-study"))
    {
        var index = Array.IndexOf(args, "--output");
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("--damage-study requires --output inside Benchmarks.Local/tmp.");
        var output = Path.GetFullPath(args[index + 1]);
        var local = Path.GetFullPath("Benchmarks.Local/tmp").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(local, StringComparison.OrdinalIgnoreCase) || File.Exists(output))
            throw new ArgumentException("Use a fresh output file in this project's Benchmarks.Local/tmp (run from the project root).");
        var study = WeaponDamageValidation.CreateControlledStudy();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(study, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote {study.Revision}: {study.Results.Length} equal-base-DPS controlled layer comparisons to {output}");
        return 0;
    }
    if (args.Contains("--validate")) { Validation.Run(); return 0; }
    if (args.Contains("--validate-adapters")) { Console.WriteLine("PASS: " + string.Join(", ", AdapterValidation.RunAll())); return 0; }
    if (args.Contains("--validate-ports")) { Validation.ComparePorts(); return 0; }
    var options = RunOptions.Parse(args);
    var frameworkIndex = Array.IndexOf(args, "--framework");
    var framework = frameworkIndex < 0 ? "secs" : args[frameworkIndex + 1];
    switch (framework)
    {
        case "secs": Runner.Run(static () => new SecsStore(), options); break;
        case "lite": Runner.Run(static () => new LeoEcsLiteStore(), options); break;
        case "classic": Runner.Run(static () => new LeoEcsClassicStore(), options); break;
        case "morpeh": Runner.Run(static () => new MorpehStore(), options); break;
        case "static": Runner.Run(static () => new StaticEcsStore(), options); break;
        default: throw new ArgumentException("--framework must be secs, lite, classic, morpeh, or static");
    }
    return 0;
}
catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }

internal sealed record RunOptions(string Output, int Ticks, int WarmupTicks, BattleConfig Config, bool Realtime, string Preset)
{
    public static RunOptions Parse(string[] args)
    {
        string Value(string name, string fallback)
        {
            var index = Array.IndexOf(args, name);
            if (index < 0) return fallback;
            if (index + 1 >= args.Length) throw new ArgumentException($"Missing value: {name}");
            return args[index + 1];
        }
        var seconds = double.Parse(Value("--seconds", "1800"));
        var ticks = int.Parse(Value("--ticks", ((int)(seconds * 60)).ToString()));
        var warmup = int.Parse(Value("--warmup", "600"));
        if (ticks < 1 || warmup < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        var preset = Value("--preset", "benchmark");
        if (preset is not ("benchmark" or "game")) throw new ArgumentException("--preset must be benchmark or game");
        var game = preset == "game";
        var config = new BattleConfig
        {
            Seed = uint.Parse(Value("--seed", "7331")), ShipsPerTeam = int.Parse(Value("--ships-per-team", "96")),
            ChurnMultiplier = float.Parse(Value("--churn", "1")), ParticleMultiplier = int.Parse(Value("--particles", "1")),
            FleetComposition = game ? FleetComposition.LightSwarm : FleetComposition.Uniform,
            Deployment = game ? FleetDeployment.Tactical : FleetDeployment.LegacyRandom,
            EndlessReinforcements = game, ReplenishCarriers = !game
        };
        config.Validate();
        var output = Path.GetFullPath(Value("--output", "Benchmarks.Local/tmp/circle-kernel/secs.json"));
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must stay inside the current project.");
        return new(output, ticks, warmup, config, args.Contains("--realtime"), preset);
    }
}

internal static class Runner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IncludeFields = true };
    public static void Run<T>(Func<T> factory, RunOptions options) where T : IEcsStore
    {
        // Warmup uses a separate battle so measured gameplay starts at tick zero.
        using (var warmup = new Battle<T>(factory(), options.Config))
            for (var i = 0; i < options.WarmupTicks; i++) warmup.Step();
        var fresh = factory();
        using var battle = new Battle<T>(fresh, options.Config);
        var timing = new double[options.Ticks]; var hudTiming = new double[options.Ticks]; var allocation = new long[options.Ticks];
        var stages = Battle<T>.StageNames.Select(_ => new double[options.Ticks]).ToArray();
        var entityCounts = new (int Ships, int Projectiles, int Debris, int Particles, int Turrets)[options.Ticks];
        var structuralCounts = new (long Adds, long Removes)[options.Ticks];
        var collections = new (int Gen0, int Gen1, int Gen2)[options.Ticks];
        var windows = new List<object>(); var hudWindows = new List<object>(); var checkpoints = new List<object>();
        using var process = Process.GetCurrentProcess();
        Directory.CreateDirectory(Path.GetDirectoryName(options.Output)!);
        var csvPath = Path.ChangeExtension(options.Output, ".ticks.csv");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var initialCollections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        var started = Stopwatch.StartNew(); var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < options.Ticks; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp(); battle.Step();
            timing[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            hudTiming[i] = battle.LastTickMs;
            allocation[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            for (var stage = 0; stage < stages.Length; stage++) stages[stage][i] = battle.LastStageMs[stage];
            entityCounts[i] = (battle.ShipCount, battle.ProjectileCount, battle.DebrisCount, battle.ParticleCount, battle.TurretCount);
            structuralCounts[i] = (battle.Counters.StructuralAdds, battle.Counters.StructuralRemoves);
            collections[i] = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            if ((i + 1) % SimulationStatistics.WindowCapacity == 0 || i == options.Ticks - 1)
            {
                var begin = i / SimulationStatistics.WindowCapacity * SimulationStatistics.WindowCapacity;
                var count = i - begin + 1;
                var sums = new long[5];
                for (var tick = begin; tick <= i; tick++)
                {
                    var c = entityCounts[tick];
                    sums[0] += c.Ships; sums[1] += c.Projectiles; sums[2] += c.Debris; sums[3] += c.Particles; sums[4] += c.Turrets;
                }
                hudWindows.Add(new { StartTick = begin + 1, EndTick = i + 1, Timing = Stats(hudTiming.AsSpan(begin, count)),
                    Stages = Battle<T>.StageNames.Select((name, s) => new { Name = name, Timing = Stats(stages[s].AsSpan(begin, count)) }).ToArray(),
                    MeanShips = (double)sums[0] / count, MeanProjectiles = (double)sums[1] / count,
                    MeanDebris = (double)sums[2] / count, MeanParticles = (double)sums[3] / count, MeanTurrets = (double)sums[4] / count });
            }
            if ((i + 1) % 3600 == 0 || i == options.Ticks - 1)
            {
                process.Refresh();
                var begin = i / 3600 * 3600; var count = i - begin + 1;
                windows.Add(new { StartTick = begin + 1, EndTick = i + 1, Timing = Stats(timing.AsSpan(begin, count)), AllocatedBytes = allocation.AsSpan(begin, count).ToArray().Sum(),
                    Ships = battle.ShipCount, Projectiles = battle.ProjectileCount, Debris = battle.DebrisCount, Particles = battle.ParticleCount, Turrets = battle.TurretCount,
                    Gc = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
                    ManagedHeapBytes = GC.GetTotalMemory(false), WorkingSetBytes = process.WorkingSet64, PrivateBytes = process.PrivateMemorySize64,
                    Store = fresh.Diagnostics(), Counters = battle.Counters.Copy() });
                checkpoints.Add(new { Tick = battle.Tick, Checksum = battle.Checksum().ToString("X16") });
                Console.WriteLine($"{fresh.Name}: {battle.Tick}/{options.Ticks} ticks; {Stats(timing.AsSpan(begin, count))}; ships={battle.ShipCount}; hits={battle.Counters.Hits}; built={battle.Counters.Builds}");
            }
            if (options.Realtime)
            {
                var due = (i + 1) / 60.0;
                while (started.Elapsed.TotalSeconds < due)
                {
                    var remaining = due - started.Elapsed.TotalSeconds;
                    if (remaining > 0.002) Thread.Sleep(1); else Thread.SpinWait(50);
                }
            }
        }
        var elapsed = started.Elapsed.TotalSeconds;
        battle.Validate();
        var measuredAllocation = allocation.Sum();
        var report = new
        {
            Metadata = new { Framework = fresh.Name, Runtime = RuntimeInformation.FrameworkDescription, Os = RuntimeInformation.OSDescription,
                Machine = Environment.MachineName, Date = DateTimeOffset.Now, TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                TargetTickRate = 60, BudgetMs = 1000.0 / 60, options.Ticks, options.WarmupTicks, Realtime = options.Realtime,
                SimulatedSeconds = options.Ticks / 60.0, WallSeconds = elapsed, Options = options.Config, options.Preset,
                TimingScope = "Step includes all systems/physics/structural commits; excludes setup, snapshots, validation and telemetry serialization",
                HudTimingScope = "Battle.LastTickMs as displayed by desktop HUD; consecutive 600-tick windows, excluding outer call timer overhead",
                MemoryScope = "Managed tick allocation, managed heap, process working/private bytes; SECS native storage separately; process includes harness buffers",
                Collision = "circle-kernel-v1; homogeneous disk ships; relative-motion analytic disk/rectangle projectile CCD",
                GameplayRules = new { Revision = ArmamentRules.Revision, TacticalRevision = TacticalRules.Revision, PhysicsRevision = PhysicsSettings.Revision,
                    DamageRevision = DamageRules.Revision, DamageProfiles = options.Config.Damage,
                    Armament = "independent attached cannon/plasma turrets; aligned hull heavy cannon with class-scaled damage and cooldown",
                    Ai = "kill-cost target leases, stable squads of 2-4, focus fire and wounded-ship screening",
                    TargetLockTicks = TacticalRules.MinimumTargetLockTicks, SwitchImprovement = TacticalRules.SwitchImprovement,
                    MaxSquadSize = TacticalRules.MaxSquadSize,
                    Aim = "constant-velocity intercept including inherited shooter velocity and muzzle offset",
                    ImpreciseAimProbability = 0.25, MaxSpreadRadians = FireControl.MaxSpreadRadians,
                    ProjectileTeam = "none", CollisionImmunity = "owner ship only (parent for mounts); disabled/docked bodies excluded",
                    FriendlyFireCounter = "observed hits with a still-live owner identity; excludes attribution after owner destruction" } },
            Provenance = new
            {
                Sources = new { SECS = "optimized working tree + default WorldOptions fix", Lite = "5053141d7d663c6b1855c8409ac93dfac807b239", Classic = "1213c31c9ef8325d48e74a52b345c4db242d038b",
                    Morpeh = "9f6369384792a01d74df24efd35b08ae313e4f12", StaticEcs = "6323d710c22cfff70100fe3748de432cb5fc70c8", StaticPack = "72ce8b993593e5cfd24c6a9289a4b132305ef493" },
                Binaries = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Order().ToDictionary(path => Path.GetFileName(path)!, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            },
            Timing = Stats(timing), AllocatedBytes = measuredAllocation,
            HarnessAndSimulationAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart,
            GcCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - initialCollections[g]).ToArray(),
            Stages = Battle<T>.StageNames.Select((name, i) => new { Name = name, Timing = Stats(stages[i]) }).ToArray(),
            HudTiming = Stats(hudTiming), HudWindows = hudWindows,
            Windows = windows, Checkpoints = checkpoints, Counters = battle.Counters.Copy(), Tactics = battle.Tactics, Checksum = battle.Checksum().ToString("X16")
        };
        File.WriteAllText(options.Output, JsonSerializer.Serialize(report, JsonOptions));
        using var csv = new StreamWriter(csvPath);
        csv.WriteLine("tick,simulationSeconds,tickMs,allocatedBytes,gen0,gen1,gen2,ships,projectiles,debris,particles,adds,removes,turrets,hudMs," + string.Join(',', Battle<T>.StageNames));
        for (var i = 0; i < options.Ticks; i++)
        {
            var c = entityCounts[i]; var s = structuralCounts[i]; var g = collections[i];
            csv.WriteLine($"{i + 1},{(i + 1) / 60.0:R},{timing[i]:R},{allocation[i]},{g.Gen0},{g.Gen1},{g.Gen2},{c.Ships},{c.Projectiles},{c.Debris},{c.Particles},{s.Adds},{s.Removes},{c.Turrets},{hudTiming[i]:R}," + string.Join(',', stages.Select(stage => stage[i].ToString("R", CultureInfo.InvariantCulture))));
        }
        Console.WriteLine($"Wrote {options.Output} and {csvPath}");
    }
    public static TickStats Stats(ReadOnlySpan<double> values)
    {
        var sorted = values.ToArray(); Array.Sort(sorted); var sum = 0.0; var sumSquared = 0.0; var misses = 0;
        foreach (var v in sorted) { sum += v; sumSquared += v * v; if (v > 1000.0 / 60) misses++; }
        var mean = sum / sorted.Length;
        double Percentile(double p) => sorted[(int)Math.Ceiling((sorted.Length - 1) * p)];
        return new(mean, sorted.Length > 1 ? Math.Sqrt(Math.Max(0, (sumSquared - sum * sum / sorted.Length) / (sorted.Length - 1))) : 0,
            Percentile(0.5), Percentile(0.95), Percentile(0.99), sorted[^1], misses, misses * 100.0 / sorted.Length);
    }
    public sealed record TickStats(double MeanMs, double StddevMs, double MedianMs, double P95Ms, double P99Ms, double MaxMs, int BudgetMisses, double BudgetMissPercent);
}
