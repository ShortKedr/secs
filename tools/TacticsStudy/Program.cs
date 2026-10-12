using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceBattle;

namespace TacticsStudy;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    { WriteIndented = true, IncludeFields = true, Converters = { new JsonStringEnumConverter() } };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") { Observer.SelfTest(); Console.WriteLine("PASS: observation/death/exposure/group accounting."); return 0; }
            var options = Options.Parse(args);
            var root = FindRoot(); var output = FreshOutput(root, options.Output);
            var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x => x.Key, x => x.Value);
            var mode = metadata["StudyReferenceMode"]!;
            if (options.Variant != mode) throw new ArgumentException("--variant must match the compiled before/after reference mode.");
            var baseline = metadata["StudyBaselineDirectory"];
            if (mode == "before")
            {
                if (baseline == null || !Path.IsPathFullyQualified(baseline)) throw new ArgumentException("Unchecked baseline directory.");
                baseline = Path.GetFullPath(baseline);
                if (!baseline.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new ArgumentException("Baseline references must reside inside this project.");
            }
            var sourceRoot = mode == "before" ? Path.Combine(Path.GetDirectoryName(baseline!)!, "source-before") : root;
            if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException("Source identity directory: " + sourceRoot);
            var source = SourceIdentity(sourceRoot);
            var harness = new[] { FileIdentity(root, "tools/TacticsStudy/Program.cs"), FileIdentity(root, "tools/TacticsStudy/TacticsStudy.csproj") };
            var config = new BattleConfig { Seed = options.Seed, ShipsPerTeam = options.Ships, CarriersPerTeam = 3,
                FleetComposition = FleetComposition.LightSwarm, EndlessReinforcements = false, ReplenishCarriers = false,
                ChurnMultiplier = 1, ParticleMultiplier = 1 };
            config.Validate();
            var configHash = HashBytes(JsonSerializer.SerializeToUtf8Bytes(config, Json));
            var (comparisonConfig, comparisonHash, aiSettings) = ComparisonConfig(config);
            var binaries = BinaryIdentities();
            SourceFile? baselineManifest = null;
            if (mode == "before")
            {
                foreach (var binary in binaries.Where(binary => binary.Name != Assembly.GetExecutingAssembly().GetName().Name))
                {
                    var original = Path.Combine(baseline!, binary.Name + ".dll");
                    if (!File.Exists(original) || HashFile(original) != binary.Sha256)
                        throw new InvalidOperationException("Loaded binary does not match its frozen baseline: " + binary.Name);
                }
                var manifest = Path.Combine(Path.GetDirectoryName(baseline!)!, "baseline-source-identity.json");
                using var identity = JsonDocument.Parse(File.ReadAllText(manifest));
                var expected = identity.RootElement.GetProperty("files").EnumerateArray().ToDictionary(
                    file => file.GetProperty("path").GetString()!.Replace('\\', '/'), file => file.GetProperty("sha256").GetString());
                if (source.Any(file => !expected.TryGetValue(file.Path, out var hash) || hash != file.Sha256))
                    throw new InvalidOperationException("Frozen source does not match the recorded baseline manifest.");
                baselineManifest = FileIdentity(root, Path.GetRelativePath(root, manifest));
            }
            var started = DateTimeOffset.UtcNow;
            var run = options.Port switch
            {
                "secs" => Run(static () => new SecsStore(), config, options),
                "lite" => Run(static () => new LeoEcsLiteStore(), config, options),
                "classic" => Run(static () => new LeoEcsClassicStore(), config, options),
                "morpeh" => Run(static () => new MorpehStore(), config, options),
                "static" => Run(static () => new StaticEcsStore(), config, options),
                _ => throw new ArgumentException("Unknown --port. Use secs, lite, classic, morpeh or static.")
            };
            if (!source.SequenceEqual(SourceIdentity(sourceRoot))) throw new InvalidOperationException("Simulation source changed during this process; discard the run.");
            if (!binaries.SequenceEqual(BinaryIdentities())) throw new InvalidOperationException("Loaded binary files changed during this process; discard the run.");
            if (!harness.SequenceEqual(new[] { FileIdentity(root, "tools/TacticsStudy/Program.cs"), FileIdentity(root, "tools/TacticsStudy/TacticsStudy.csproj") }))
                throw new InvalidOperationException("Harness source changed during this process; discard the run.");
            var payload = new StudyReport("tactics-study-v1", options.Variant, options.Port, options.Repetition, options.Seed,
                started, DateTimeOffset.UtcNow, Environment.ProcessId, RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
                Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
                root, sourceRoot, baseline, config, configHash, comparisonConfig, comparisonHash,
                "Canonical full config excluding only root Tactics. Full Config/ConfigSha256 retained; optional AI settings and TacticsRevision intentionally differ. Physics, Damage, Fleet, seed, particles and every other config field must match.",
                aiSettings, mode == "before" ? true : null, baselineManifest, source, harness, binaries,
                Revision(typeof(TacticalRules)), Revision(typeof(DamageRules)), options.WarmupTicks, options.Ticks,
                options.CheckpointTicks, run, null);
            var bytesBefore = GC.GetAllocatedBytesForCurrentThread(); var exportStart = Stopwatch.GetTimestamp();
            var probe = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
            var export = new ExportProbe(Stopwatch.GetElapsedTime(exportStart).TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - bytesBefore, probe.Length,
                "One serialization probe outside all Step/AI/observer intervals; final serialization and file write are also untimed.");
            var final = JsonSerializer.SerializeToUtf8Bytes(payload with { Export = export }, Json);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(final);
            Console.WriteLine(output);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static RunReport Run<T>(Func<T> factory, BattleConfig config, Options options) where T : IEcsStore
    {
        // Warm runtime/code, then dispose the entire world. No warm simulation state is measured.
        using (var warm = new Battle<T>(factory(), config)) for (var tick = 0; tick < options.WarmupTicks; tick++) warm.Step();
        var capacity = Math.Max(4096, checked((config.ShipsPerTeam + config.CarriersPerTeam) * 2 * 32));
        var checkpoints = new List<Checkpoint>(options.Ticks / options.CheckpointTicks + 2);
        var step = new double[options.Ticks]; var ai = new double[options.Ticks]; var observation = new double[options.Ticks];
        var population = new TickPopulation[options.Ticks];
        var observer = new Observer(capacity);
        long stepBytes = 0, observationBytes = 0;
        Dictionary<string, long> aiCounters;
        BattleCounters counters;
        int[] collections;
        using (var battle = new Battle<T>(factory(), config))
        {
            var aiStage = Array.IndexOf(Battle<T>.StageNames, "ai");
            if (aiStage < 0) throw new InvalidOperationException("No shared AI stage.");
            // Resolve new optional fields once, outside the measured loop. No v3-only API calls.
            var counterFields = battle.Tactics.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(long) || field.FieldType == typeof(int)).OrderBy(field => field.Name).ToArray();
            Observe(battle, observer); checkpoints.Add(new(0, battle.Checksum().ToString("X16")));
            var gcBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            for (var tick = 0; tick < options.Ticks; tick++)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                battle.Step();
                step[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                stepBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
                ai[tick] = battle.LastStageMs[aiStage];
                allocated = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
                Observe(battle, observer);
                observation[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                observationBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
                population[tick] = observer.Population(battle.Tick, battle.Counters.FriendlyFireHits);
                if (battle.Tick % options.CheckpointTicks == 0 || battle.Tick == options.Ticks)
                    checkpoints.Add(new(battle.Tick, battle.Checksum().ToString("X16")));
            }
            observer.AssertConservation(battle.Counters.ShipsCreated, battle.Counters.ShipsDestroyed);
            aiCounters = counterFields.ToDictionary(field => field.Name, field => Convert.ToInt64(field.GetValue(battle.Tactics), CultureInfo.InvariantCulture));
            counters = battle.Counters.Copy();
            collections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - gcBefore[g]).ToArray();
        }
        var units = observer.ExportUnits(config.TickRate, options.Ticks);
        var digest = Observer.Digest(units);
        // Same-environment replay, not an old-vs-new equality requirement. Match observation and
        // checkpoint cadence so the extra public queries/checksums cannot silently alter state.
        using (var replay = new Battle<T>(factory(), config))
        {
            var replayObserver = new Observer(capacity); var checkpoint = 0;
            Observe(replay, replayObserver); MatchCheckpoint(replay);
            for (var tick = 0; tick < options.Ticks; tick++)
            {
                replay.Step(); Observe(replay, replayObserver);
                if (replay.Tick % options.CheckpointTicks == 0 || replay.Tick == options.Ticks) MatchCheckpoint(replay);
            }
            replayObserver.AssertConservation(replay.Counters.ShipsCreated, replay.Counters.ShipsDestroyed);
            if (checkpoint != checkpoints.Count || Observer.Digest(replayObserver.ExportUnits(config.TickRate, options.Ticks)) != digest)
                throw new InvalidOperationException("Same-environment replay observation mismatch.");
            void MatchCheckpoint(Battle<T> battle)
            {
                var expected = checkpoints[checkpoint++];
                if (battle.Tick != expected.Tick || battle.Checksum().ToString("X16") != expected.Checksum)
                    throw new InvalidOperationException("Same-environment replay checksum mismatch at tick " + battle.Tick);
            }
        }
        return new RunReport(
            "One process/repetition: separate warmup world, fresh timed world, fresh untimed verification world. Step is the external Step call only; AI is LastStageMs[ai]. Queries, checkpoints, export and verification are outside Step/AI timing. GC collections cover the measured loop including observer/checkpoints. Finite-horizon timings include outcome-dependent populations, not an isolated kernel throughput comparison.",
            true, digest.ToString("X16"), capacity, Stats(step), Stats(ai), Stats(observation), step, ai, observation,
            stepBytes, observationBytes, collections, checkpoints.ToArray(), counters, aiCounters, observer.Outcomes(), population, units);
    }

    private static void Observe<T>(Battle<T> battle, Observer observer) where T : IEcsStore
    {
        observer.Begin(battle.Tick);
        var query = new ShipObserver<T> { Store = battle.Store, Observer = observer };
        battle.Store.ForEach<Identity, Brain, Hull, ShipObserver<T>>(ref query);
        observer.Complete();
    }

    private struct ShipObserver<T> : IProcessor<Identity, Brain, Hull> where T : IEcsStore
    {
        public T Store; public Observer Observer;
        public void Execute(EntityKey e, ref Identity id, ref Brain brain, ref Hull hull)
            => Observer.Accept(e, id, brain, hull, Store.Get<Shield>(e), Store.Get<CombatState>(e), Store.Has<Docked>(e));
    }

    private static Timing Stats(double[] values)
    {
        var sorted = (double[])values.Clone(); Array.Sort(sorted);
        return new(values.Average(), sorted[(int)Math.Ceiling((sorted.Length - 1) * .5)],
            sorted[(int)Math.Ceiling((sorted.Length - 1) * .95)], sorted[(int)Math.Ceiling((sorted.Length - 1) * .99)], sorted[^1]);
    }

    private static string Revision(Type type) => (string?)type.GetField("Revision", BindingFlags.Static | BindingFlags.Public)?.GetRawConstantValue() ?? "unavailable";
    private static (JsonElement Config, string Hash, JsonElement? AiSettings) ComparisonConfig(BattleConfig config)
    {
        var full = JsonSerializer.SerializeToElement(config, Json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in full.EnumerateObject().Where(property => property.Name != "Tactics").OrderBy(property => property.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); CanonicalValue(writer, property.Value); }
            writer.WriteEndObject();
        }
        var bytes = stream.ToArray(); using var document = JsonDocument.Parse(bytes);
        return (document.RootElement.Clone(), HashBytes(bytes), full.TryGetProperty("Tactics", out var tactics) ? tactics.Clone() : null);
    }
    private static void CanonicalValue(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); CanonicalValue(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) CanonicalValue(writer, item); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashFile(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static BinaryIdentity[] BinaryIdentities()
    {
        var primary = new[] { Assembly.GetExecutingAssembly(), typeof(BattleConfig).Assembly, typeof(SecsStore).Assembly,
            typeof(SECS.World).Assembly, typeof(LeoEcsLiteStore).Assembly }.Distinct()
            .Select(assembly => new BinaryIdentity(assembly.GetName().Name!, assembly.Location, HashFile(assembly.Location),
                assembly.ManifestModule.ModuleVersionId.ToString(), assembly.GetName().Version?.ToString(),
                assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration)).ToList();
        // Fingerprint copied backend dependencies even when the selected port has not loaded them.
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "*.dll"))
            if (!primary.Any(binary => string.Equals(binary.Path, file, StringComparison.OrdinalIgnoreCase)))
                primary.Add(new(Path.GetFileNameWithoutExtension(file), file, HashFile(file), "not inspected",
                    AssemblyName.GetAssemblyName(file).Version?.ToString(), null));
        return primary.OrderBy(binary => binary.Name, StringComparer.Ordinal).ToArray();
    }
    private static SourceFile FileIdentity(string root, string relative) => new(relative.Replace('\\', '/'), HashFile(Path.Combine(root, relative)));
    private static SourceFile[] SourceIdentity(string root)
    {
        var paths = new List<string>();
        foreach (var folder in new[] { "SpaceBattle.Core", "SpaceBattle.SECS", "SpaceBattle.Adapters", "SECS" })
        {
            var directory = Path.Combine(root, folder);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            paths.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(path =>
                !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj") &&
                Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets"));
        }
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.config" })
            if (File.Exists(Path.Combine(root, name))) paths.Add(Path.Combine(root, name));
        return paths.Select(path => FileIdentity(root, Path.GetRelativePath(root, path))).OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    private static string FindRoot()
    {
        for (var current = new DirectoryInfo(Environment.CurrentDirectory); current != null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "SpaceBattle.Core", "SpaceBattle.Core.csproj")) && Directory.Exists(Path.Combine(current.FullName, "Benchmarks.Local"))) return current.FullName;
        throw new ArgumentException("Run from the repository root or a directory inside it.");
    }

    private static string FreshOutput(string root, string output)
    {
        if (!Path.IsPathFullyQualified(output)) throw new ArgumentException("--output must be absolute.");
        var path = Path.GetFullPath(output); var allowed = Path.GetFullPath(Path.Combine(root, "Benchmarks.Local", "tmp")) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(allowed, comparison) || Path.GetExtension(path) != ".json" || File.Exists(path) || Directory.Exists(path))
            throw new ArgumentException("Use a fresh .json path under ignored Benchmarks.Local/tmp.");
        // Resolve existing parent links, not only lexical '..', before allowing any write.
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(path)!); parent != null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Output ancestors must not be symlinks or junctions.");
        return path;
    }
}

internal sealed record Options(string Variant, string Port, uint Seed, int Repetition, int Ships, int Ticks,
    int WarmupTicks, int CheckpointTicks, string Output)
{
    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
            if (i + 1 == args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Use unique --name value pairs.");
        var known = new[] { "--variant", "--port", "--seed", "--rep", "--ships", "--ticks", "--warmup", "--checkpoint", "--output" };
        if (values.Keys.Any(key => !known.Contains(key))) throw new ArgumentException("Unknown option.");
        string Value(string name, string fallback) => values.GetValueOrDefault(name, fallback);
        int Number(string name, string fallback) => int.Parse(Value(name, fallback), CultureInfo.InvariantCulture);
        var result = new Options(Value("--variant", "after"), Value("--port", "secs"), uint.Parse(Value("--seed", "27"), CultureInfo.InvariantCulture),
            Number("--rep", "1"), Number("--ships", "96"), Number("--ticks", "2400"), Number("--warmup", "2400"), Number("--checkpoint", "240"), Value("--output", ""));
        if (result.Variant is not ("before" or "after") || result.Repetition < 1 || result.Ticks < 1 || result.WarmupTicks < 1 ||
            result.CheckpointTicks < 1 || result.Ships is < 8 or > 10000) throw new ArgumentException("Invalid mode/counts.");
        return result;
    }
}

internal sealed class Observer
{
    private readonly Dictionary<EntityKey, int> indexes;
    private readonly List<UnitState> units;
    private readonly HashSet<EntityKey>[] leaders;
    private readonly TeamState[] teams = [new(), new()];
    private int[] current, previous;
    private int currentCount, previousCount, tick;
    public Observer(int capacity)
    {
        indexes = new(capacity); units = new(capacity); current = new int[capacity]; previous = new int[capacity];
        leaders = [new(capacity), new(capacity)];
    }
    public void Begin(int now)
    {
        tick = now; currentCount = 0;
        for (var team = 0; team < 2; team++) { teams[team].Population = teams[team].Docked = teams[team].Grouped = 0; leaders[team].Clear(); }
    }
    public void Accept(EntityKey entity, Identity id, Brain brain, Hull hull, Shield shield, CombatState combat, bool docked)
    {
        if (id.Team is < 0 or > 1) throw new InvalidOperationException("Unsupported team.");
        var exists = indexes.TryGetValue(entity, out var index);
        if (!exists)
        {
            if (units.Count == current.Length) throw new InvalidOperationException("Preallocated observer history exhausted; increase its capacity before collecting a fresh series, not the nominal fleet size.");
            index = units.Count; indexes.Add(entity, index);
            units.Add(new UnitState { Entity = entity, Id = id, BirthTick = tick, SeenTick = -1, DeathTick = -1,
                FirstTargetedTick = -1, FirstDamageTick = -1, Target = brain.Target, Leader = brain.SquadLeader,
                Hull = hull.Current, Shield = shield.Current, LastDamageTick = combat.LastDamageTick });
            teams[id.Team].Births++;
            if (tick == 0) teams[id.Team].Initial++;
        }
        ref var unit = ref CollectionsMarshal.AsSpan(units)[index];
        if (unit.SeenTick == tick || unit.DeathTick >= 0 || unit.Id.Serial != id.Serial || unit.Id.Team != id.Team || unit.Id.Class != id.Class)
            throw new InvalidOperationException("Duplicate, resurrected, or changed public ship identity.");
        var team = teams[id.Team];
        if (exists)
        {
            if (brain.Target != unit.Target)
            {
                unit.TargetChanges++; team.TargetChanges++;
                if (unit.Target.IsNone) { unit.Acquisitions++; team.Acquisitions++; }
                else if (brain.Target.IsNone) { unit.Releases++; team.Releases++; }
                else { unit.Retargets++; team.Retargets++; }
            }
            if (brain.SquadLeader != unit.Leader)
            {
                unit.GroupChanges++; team.GroupChanges++;
                if (unit.Leader.IsNone) { unit.GroupJoins++; team.GroupJoins++; }
                else if (brain.SquadLeader.IsNone) { unit.GroupExits++; team.GroupExits++; }
                else { unit.GroupReassignments++; team.GroupReassignments++; }
            }
            if (combat.LastDamageTick > unit.LastDamageTick || hull.Current < unit.Hull || shield.Current < unit.Shield)
                if (unit.FirstDamageTick < 0) unit.FirstDamageTick = combat.LastDamageTick > unit.SeenTick && combat.LastDamageTick <= tick ? combat.LastDamageTick : tick;
        }
        else if (combat.LastDamageTick >= 0 && combat.LastDamageTick <= tick) unit.FirstDamageTick = combat.LastDamageTick;
        unit.SeenTick = tick; unit.Target = brain.Target; unit.Leader = brain.SquadLeader;
        unit.Hull = hull.Current; unit.Shield = shield.Current; unit.LastDamageTick = combat.LastDamageTick;
        team.Population++;
        if (docked) team.Docked++;
        if (!brain.SquadLeader.IsNone)
        {
            team.Grouped++; leaders[id.Team].Add(brain.SquadLeader);
            if (tick > unit.BirthTick) unit.GroupedSamples++;
        }
        current[currentCount++] = index;
    }
    public void Complete()
    {
        var rows = CollectionsMarshal.AsSpan(units);
        for (var i = 0; i < previousCount; i++)
        {
            ref var unit = ref rows[previous[i]];
            if (unit.SeenTick == tick) continue;
            unit.DeathTick = tick; var team = teams[unit.Id.Team]; team.Deaths++;
            if (unit.BirthTick == 0) team.InitialDeaths++; else team.NewbornDeaths++;
        }
        // Target exposure is applied only after the full query, independent of native row order.
        for (var i = 0; i < currentCount; i++)
        {
            var target = rows[current[i]].Target;
            if (target.IsNone || !indexes.TryGetValue(target, out var index)) continue;
            ref var victim = ref rows[index];
            if (victim.SeenTick == tick && victim.FirstTargetedTick < 0) victim.FirstTargetedTick = tick;
        }
        (current, previous) = (previous, current); previousCount = currentCount;
    }
    public TickPopulation Population(int now, long friendlyFire) => new(now, teams[0].Population, teams[1].Population,
        teams[0].Deaths, teams[1].Deaths, teams[0].Births - teams[0].Initial, teams[1].Births - teams[1].Initial,
        teams[0].Docked, teams[1].Docked, teams[0].Grouped, teams[1].Grouped, leaders[0].Count, leaders[1].Count, friendlyFire);
    public void AssertConservation(long births, long deaths)
    {
        if (teams.Sum(team => team.Births) != births || teams.Sum(team => team.Deaths) != deaths ||
            teams.Any(team => team.Births - team.Deaths != team.Population || team.InitialDeaths + team.NewbornDeaths != team.Deaths))
            throw new InvalidOperationException("Observer birth/death conservation does not match core counters; do not infer losses from survivors.");
    }
    public TeamOutcome[] Outcomes() => teams.Select((team, i) => new TeamOutcome(i, team.Initial, team.Births - team.Initial,
        team.Births, team.Deaths, team.InitialDeaths, team.NewbornDeaths, team.Population, team.TargetChanges,
        team.Acquisitions, team.Releases, team.Retargets, team.GroupChanges, team.GroupJoins, team.GroupExits, team.GroupReassignments)).ToArray();
    public UnitOutcome[] ExportUnits(int tickRate, int endTick) => units.OrderBy(unit => unit.Id.Serial).Select(unit =>
    {
        var exposure = unit.FirstTargetedTick < 0 ? unit.FirstDamageTick : unit.FirstDamageTick < 0 ? unit.FirstTargetedTick : Math.Min(unit.FirstTargetedTick, unit.FirstDamageTick);
        var end = unit.DeathTick >= 0 ? unit.DeathTick : endTick;
        return new UnitOutcome(unit.Entity, unit.Id.Serial, unit.Id.Team, unit.Id.Class, unit.BirthTick == 0, unit.BirthTick,
            unit.DeathTick >= 0 ? unit.DeathTick : null, unit.FirstTargetedTick >= 0 ? unit.FirstTargetedTick : null,
            unit.FirstDamageTick >= 0 ? unit.FirstDamageTick : null, exposure >= 0 ? exposure : null,
            unit.BirthTick == 0 && exposure >= 0, exposure >= 0 && unit.DeathTick < 0, exposure < 0,
            exposure >= 0 ? end - exposure : null, exposure >= 0 ? (double)(end - exposure) / tickRate : null,
            unit.TargetChanges, unit.Acquisitions, unit.Releases, unit.Retargets, unit.GroupChanges,
            unit.GroupJoins, unit.GroupExits, unit.GroupReassignments, unit.GroupedSamples, unit.Target, unit.Leader);
    }).ToArray();
    public static ulong Digest(UnitOutcome[] units)
    {
        ulong hash = 14695981039346656037;
        foreach (var unit in units)
        {
            Add(unit.Serial); Add(unit.Team); Add(unit.BirthTick); Add(unit.DeathTick ?? -1); Add(unit.FirstTargetedTick ?? -1);
            Add(unit.FirstDamageTick ?? -1); Add(unit.TargetChanges); Add(unit.GroupMembershipChanges); Add(unit.GroupedTickSamples);
            Add(unit.FinalTarget.Id); Add(unit.FinalTarget.Generation); Add(unit.FinalSquadLeader.Id); Add(unit.FinalSquadLeader.Generation);
        }
        return hash;
        void Add(long value) => hash = unchecked((hash ^ (ulong)value) * 1099511628211UL);
    }
    public static void SelfTest()
    {
        var observer = new Observer(16); var a = new EntityKey(0, 1); var b = new EntityKey(1, 1); var born = new EntityKey(2, 1);
        void Add(EntityKey e, int serial, int team, EntityKey target, EntityKey leader, int damage = -1000, float hull = 100)
            => observer.Accept(e, new Identity { Serial = serial, Team = team, Class = ShipClass.Fighter },
                new Brain { Target = target, SquadLeader = leader }, new Hull { Current = hull }, new Shield { Current = 50 },
                new CombatState { LastDamageTick = damage }, docked: true);
        observer.Begin(0); Add(b, 2, 1, EntityKey.None, EntityKey.None); Add(a, 1, 0, EntityKey.None, EntityKey.None); observer.Complete();
        observer.Begin(1); Add(b, 2, 1, EntityKey.None, EntityKey.None, 1, 90); Add(a, 1, 0, b, a); Add(born, 3, 1, a, EntityKey.None); observer.Complete();
        observer.Begin(2); Add(a, 1, 0, EntityKey.None, EntityKey.None); observer.Complete(); observer.AssertConservation(3, 2);
        var outcomes = observer.Outcomes(); var units = observer.ExportUnits(60, 2);
        if (outcomes[1].InitialDeaths != 1 || outcomes[1].NewbornDeaths != 1 || outcomes[0].DeathsCumulative != 0 ||
            units.Single(unit => unit.Serial == 2).ElapsedExposureTicks != 1 ||
            !units.Single(unit => unit.Serial == 1).RightCensored || units.Single(unit => unit.Serial == 1).GroupMembershipChanges != 2)
            throw new InvalidOperationException("Observer synthetic regression failed.");
        observer.Begin(3); Add(a, 1, 0, EntityKey.None, EntityKey.None); Add(new(2, 2), 4, 1, EntityKey.None, EntityKey.None); observer.Complete();
        observer.AssertConservation(4, 2);
    }
    private struct UnitState
    {
        public EntityKey Entity, Target, Leader; public Identity Id;
        public int BirthTick, SeenTick, DeathTick, FirstTargetedTick, FirstDamageTick, LastDamageTick;
        public float Hull, Shield;
        public long TargetChanges, Acquisitions, Releases, Retargets, GroupChanges, GroupJoins, GroupExits, GroupReassignments, GroupedSamples;
    }
    private sealed class TeamState
    {
        public int Initial, Births, Deaths, InitialDeaths, NewbornDeaths, Population, Docked, Grouped;
        public long TargetChanges, Acquisitions, Releases, Retargets, GroupChanges, GroupJoins, GroupExits, GroupReassignments;
    }
}

internal sealed record StudyReport(string Schema, string Variant, string Port, int Repetition, uint Seed,
    DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, int ProcessId, string Runtime, string Os, string Architecture,
    int LogicalProcessors, string? BuildConfiguration, string Root, string SourceRoot, string? BaselineDirectory,
    BattleConfig Config, string ConfigSha256, JsonElement SharedComparisonConfig, string SharedComparisonConfigSha256,
    string ComparisonDefinition, JsonElement? IntentionalAiSettings, bool? BaselineReferencesVerified, SourceFile? BaselineManifest,
    SourceFile[] SimulationSource, SourceFile[] HarnessSource, BinaryIdentity[] Binaries,
    string TacticsRevision, string DamageRevision, int WarmupTicks, int Ticks, int CheckpointTicks, RunReport Run, ExportProbe? Export);
internal sealed record RunReport(string TimingDefinition, bool ReplayVerified, string ObservationDigest, int ObserverCapacity,
    Timing Step, Timing Ai, Timing Observation, double[] StepSamplesMs, double[] AiSamplesMs, double[] ObservationSamplesMs,
    long StepAllocatedBytes, long ObservationAllocatedBytes, int[] MeasuredWindowGcCollections, Checkpoint[] Checkpoints,
    BattleCounters Counters, Dictionary<string, long> AiCounters, TeamOutcome[] Teams, TickPopulation[] Populations, UnitOutcome[] Units)
{
    public string DeathDefinition => "Cumulative per-team full-EntityKey disappearances from consecutive post-Step ship queries, including newborns; verified against ShipsCreated/ShipsDestroyed and birth-minus-death conservation. Docked ships are never filtered.";
    public string TtkDefinition => "TrackedInitial ships only for cohort TTK: elapsed simulation ticks/seconds from earliest observed incoming Brain.Target or damage event/drop until observed death. Surviving exposed targets are right-censored at final tick; unexposed ships/deaths have no inferred TTK. Within-tick targeting switches and lethal-first hits can be unobserved.";
    public string ChurnDefinition => "Per observed unit: full-handle Target transitions, split into acquisitions/releases/retargets; SquadLeader transitions, split into joins/exits/reassignments. Birth samples establish a baseline, not a transition. These observations are distinct from Core AI decision counters.";
}
internal readonly record struct SourceFile(string Path, string Sha256);
internal readonly record struct BinaryIdentity(string Name, string Path, string Sha256, string Mvid, string? Version, string? BuildConfiguration);
internal readonly record struct Timing(double MeanMs, double P50Ms, double P95Ms, double P99Ms, double MaxMs);
internal readonly record struct ExportProbe(double SerializationMs, long AllocatedBytes, int ProbeBytes, string Definition);
internal readonly record struct Checkpoint(int Tick, string Checksum);
internal readonly record struct TickPopulation(int Tick, int Team0Alive, int Team1Alive, int Team0Deaths, int Team1Deaths,
    int Team0Newborns, int Team1Newborns, int Team0Docked, int Team1Docked, int Team0Grouped, int Team1Grouped,
    int Team0LeaderHandles, int Team1LeaderHandles, long FriendlyFireHitsCumulative);
internal readonly record struct TeamOutcome(int Team, int InitialShips, int NewbornsObserved, int BirthsCumulative, int DeathsCumulative,
    int InitialDeaths, int NewbornDeaths, int AliveAtEnd, long TargetChanges, long TargetAcquisitions, long TargetReleases,
    long TargetRetargets, long GroupMembershipChanges, long GroupJoins, long GroupExits, long GroupReassignments);
internal readonly record struct UnitOutcome(EntityKey Entity, int Serial, int Team, ShipClass Class, bool TrackedInitial, int BirthTick,
    int? DeathTick, int? FirstTargetedTick, int? FirstDamageTick, int? FirstExposureTick, bool TtkEligible, bool RightCensored,
    bool Unexposed, int? ElapsedExposureTicks, double? ElapsedExposureSeconds, long TargetChanges, long TargetAcquisitions,
    long TargetReleases, long TargetRetargets, long GroupMembershipChanges, long GroupJoins, long GroupExits,
    long GroupReassignments, long GroupedTickSamples, EntityKey FinalTarget, EntityKey FinalSquadLeader);
