using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using SpaceBattle;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
var docs = Path.Combine(root, "docs/space-battle");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
if (args.Contains("--selftest"))
{
    using var test = new CensusStore();
    var e = test.Create();
    test.Set(e, new Identity()); test.Set(e, new Transform()); test.Set(e, new Hull());
    test.Set(e, new Hull { Current = 3 });
    Check(test.TotalComponents == 3 && test.TotalEntities == 1, "Set replacement changed composition");
    test.Remove<Hull>(e); test.Remove<Hull>(e);
    Check(test.TotalComponents == 2, "Remove must count only an existing component");
    test.VerifyNativeCounts(); test.Destroy(e); test.Commit();
    Check(test.TotalComponents == 0 && test.TotalEntities == 0, "Destroy must remove all components");
    var next = test.Create(); test.Set(next, new Particle()); test.Set(next, new Transform());
    test.VerifyNativeCounts(); test.Destroy(next); test.Commit();
    Check(test.TotalEntities == 0, "Reused identity retained composition");
    Console.WriteLine("PASS: composition observer replacement/removal/destruction/reuse/native census");
    return;
}

Directory.CreateDirectory(Path.Combine(docs, "composition"));
foreach (var (profile, multiplier) in new[] { ("low", 0.5f), ("nominal", 1f), ("high", 2f) })
{
    var sourcePath = Path.Combine(docs, "results", profile + "-secs.json");
    using var source = JsonDocument.Parse(File.ReadAllText(sourcePath));
    var data = source.RootElement;
    var config = data.GetProperty("Metadata").GetProperty("Options").Deserialize<BattleConfig>(jsonOptions)!;
    Check(config.ChurnMultiplier == multiplier, "Wrong profile configuration");
    var binaryHashes = new Dictionary<string, string>();
    foreach (var name in new[] { "SpaceBattle.Core.dll", "SpaceBattle.SECS.dll", "SECS.dll", "Aether.Physics2D.dll" })
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name))));
        Check(hash == data.GetProperty("Provenance").GetProperty("Binaries").GetProperty(name).GetString(), "Measured DLL changed: " + name);
        binaryHashes.Add(name, hash);
    }
    using var store = new CensusStore();
    using var battle = new Battle<CensusStore>(store, config);
    store.VerifyNativeCounts();
    var initial = store.ComponentCounts.ToArray();
    store.ResetOperations();
    var ticks = data.GetProperty("Metadata").GetProperty("Ticks").GetInt32();
    var totals = new Totals(ticks);
    var checks = new List<object>();
    using var oldCsv = new StreamReader(Path.ChangeExtension(sourcePath, ".ticks.csv"));
    Check(oldCsv.ReadLine() == "tick,simulationSeconds,tickMs,allocatedBytes,gen0,gen1,gen2,ships,projectiles,debris,particles,adds,removes", "Unknown original CSV schema");
    var csvPath = Path.Combine(docs, "composition", profile + ".ticks.csv");
    using var csv = new StreamWriter(csvPath);
    csv.WriteLine("tick,entities,components," + string.Join(',', Registry.Names) + "," +
        string.Join(',', Registry.GroupNames.Select(n => n + "Entities")) + "," +
        string.Join(',', Registry.GroupNames.Select(n => n + "Components")) + "," +
        string.Join(',', Enumerable.Range(0, 24).Select(n => "CountWith" + n)));
    for (var i = 1; i <= ticks; i++)
    {
        battle.Step();
        var row = oldCsv.ReadLine()!.Split(',');
        Check(int.Parse(row[0]) == i, "Original CSV tick mismatch");
        var expectedEntities = int.Parse(row[7]) + int.Parse(row[8]) + int.Parse(row[9]) + int.Parse(row[10]);
        Check(store.TotalEntities == expectedEntities && battle.ShipCount == int.Parse(row[7]) &&
            battle.ProjectileCount == int.Parse(row[8]) && battle.DebrisCount == int.Parse(row[9]) && battle.ParticleCount == int.Parse(row[10]), "Entity trace mismatch at " + i);
        Check(battle.Counters.StructuralAdds == long.Parse(row[11]) && battle.Counters.StructuralRemoves == long.Parse(row[12]), "Structural trace mismatch at " + i);
        Check(store.TotalComponents == battle.Counters.StructuralAdds - battle.Counters.StructuralRemoves, "Component conservation failed at " + i);
        totals.Sample(store);
        csv.WriteLine(i + "," + store.TotalEntities + "," + store.TotalComponents + "," + string.Join(',', store.ComponentCounts) + "," +
            string.Join(',', store.GroupCounts) + "," + string.Join(',', store.GroupComponents) + "," + string.Join(',', store.Histogram));
        if (i % 3600 == 0)
        {
            store.RecordOperations = false;
            store.VerifyNativeCounts();
            var hash = battle.Checksum().ToString("X16");
            var point = data.GetProperty("Checkpoints")[i / 3600 - 1];
            Check(hash == point.GetProperty("Checksum").GetString(), "Checkpoint mismatch at " + i);
            checks.Add(new { Tick = i, Checksum = hash, NativeComponentCountsVerified = true });
            store.RecordOperations = true;
            if (i % 36000 == 0) Console.WriteLine($"{profile}: {i}/{ticks} ticks; entity/component/checksum/native census PASS");
        }
    }
    Check(oldCsv.ReadLine() is null, "Original CSV has extra ticks");
    store.RecordOperations = false;
    battle.Validate();
    var counters = JsonSerializer.SerializeToElement(battle.Counters, jsonOptions);
    foreach (var counter in data.GetProperty("Counters").EnumerateObject())
        Check(counters.GetProperty(counter.Name).GetInt64() == counter.Value.GetInt64(), "Final counter mismatch: " + counter.Name);
    var finalHash = battle.Checksum().ToString("X16");
    Check(finalHash == data.GetProperty("Checksum").GetString(), "Final checksum mismatch");
    csv.Close();
    var report = new
    {
        SchemaVersion = 1, Profile = profile, Config = config, Ticks = ticks, SimulatedSeconds = ticks / (double)config.TickRate,
        Scope = "Separate diagnostic replay, every end-of-Step tick 1..108000; setup excluded from sums and API operations; no performance measurement",
        SourceResult = Path.GetRelativePath(docs, sourcePath).Replace('\\', '/'),
        SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))),
        TickCsvSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(csvPath))),
        BinaryHashes = binaryHashes, Checkpoints = checks, Checksum = finalHash,
        ObserverSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(CensusStore).Assembly.Location))),
        Summary = totals.Summary(), Groups = totals.Groups(),
        Components = Registry.Names.Select((name, j) => new
        {
            Name = name, LogicalPayloadBytes = Registry.Sizes[j], Initial = initial[j], Final = store.ComponentCounts[j],
            MeanActive = totals.ComponentEntityTicks[j] / (double)ticks, MinActive = totals.ComponentMin[j], MaxActive = totals.ComponentMax[j],
            EntityTicks = totals.ComponentEntityTicks[j], EntityPresencePercent = totals.ComponentEntityTicks[j] * 100.0 / totals.EntityTicks,
            AddsDuringSimulation = store.Adds[j], RemovesDuringSimulation = store.Removes[j],
            GetCalls = store.Gets[j], HasCalls = store.HasChecks[j], QueryBindings = store.Bindings[j]
        }).ToArray(),
        ComponentCountHistogram = totals.Histogram.Select((n, j) => new { Components = j, EntityTicks = n, Percent = n * 100.0 / totals.EntityTicks }).Where(x => x.EntityTicks > 0).ToArray(),
        Compositions = totals.Signatures.OrderByDescending(x => x.Value).Select(x => new
        {
            Components = Registry.Describe(x.Key), ComponentCount = BitOperations.PopCount(x.Key),
            EntityTicks = x.Value, Percent = x.Value * 100.0 / totals.EntityTicks
        }).ToArray()
    };
    Check(store.Adds.Sum() + initial.Sum() == battle.Counters.StructuralAdds && store.Removes.Sum() == battle.Counters.StructuralRemoves, "Per-component structural totals mismatch");
    File.WriteAllText(Path.Combine(docs, "composition", profile + ".json"), JsonSerializer.Serialize(report, jsonOptions));
    Console.WriteLine($"PASS {profile}: {ticks} tick counts + 30 checksums + native censuses; mean={totals.ComponentTicks / (double)totals.EntityTicks:F6} components/entity");
}

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

internal static class Registry
{
    public static readonly Type[] Types = [typeof(Identity), typeof(Transform), typeof(Motion), typeof(Hull), typeof(Shield), typeof(Heat),
        typeof(Brain), typeof(Weapon), typeof(SecondaryWeapon), typeof(Collider), typeof(PhysicsLink), typeof(CombatState), typeof(Carrier),
        typeof(Docked), typeof(Overheated), typeof(InCombat), typeof(Boosting), typeof(ShieldBroken), typeof(Projectile), typeof(Debris), typeof(Particle), typeof(Lifetime), typeof(Inventory)];
    public static readonly string[] Names = Types.Select(t => t.Name).ToArray();
    public static readonly string[] GroupNames = ["Escorts", "Carriers", "Projectiles", "Debris", "Particles", "Unclassified"];
    public static readonly int[] Sizes = Types.Select(t => (int)typeof(Registry).GetMethod(nameof(Size))!.MakeGenericMethod(t).Invoke(null, null)!).ToArray();
    public static int Size<T>() where T : unmanaged => Unsafe.SizeOf<T>();
    public static string[] Describe(uint mask) => Names.Where((_, j) => (mask & (1u << j)) != 0).ToArray();
    public static int Kind(uint mask) => (mask & (1u << 0)) != 0 ? ((mask & (1u << 12)) != 0 ? 1 : 0) :
        (mask & (1u << 18)) != 0 ? 2 : (mask & (1u << 19)) != 0 ? 3 : (mask & (1u << 20)) != 0 ? 4 : 5;
}
internal static class Component<T> where T : unmanaged
{
    public static readonly int Index = Array.IndexOf(Registry.Types, typeof(T));
    public static readonly uint Bit = Index >= 0 ? 1u << Index : throw new InvalidOperationException("Unknown logical component: " + typeof(T));
}

// Observe composition changes, never duplicate component values or alter native query order.
internal sealed class CensusStore : IEcsStore
{
    private readonly SecsStore inner = new();
    private bool disposed;
    private readonly Dictionary<EntityKey, uint> masks = new();
    public readonly int[] ComponentCounts = new int[Registry.Types.Length];
    public readonly long[] Adds = new long[Registry.Types.Length], Removes = new long[Registry.Types.Length],
        Gets = new long[Registry.Types.Length], HasChecks = new long[Registry.Types.Length], Bindings = new long[Registry.Types.Length];
    public readonly int[] GroupCounts = new int[6], GroupComponents = new int[6], Histogram = new int[24];
    public readonly Dictionary<uint, int> Signatures = new();
    public bool RecordOperations = true;
    public int TotalEntities => masks.Count;
    public int TotalComponents { get; private set; }
    public string Name => inner.Name;
    public EntityKey Create() { var e = inner.Create(); masks.Add(e, 0); Adjust(0, 1); return e; }
    public void Destroy(EntityKey e)
    {
        var mask = masks[e]; inner.Destroy(e); masks.Remove(e); Adjust(mask, -1);
        for (var j = 0; j < ComponentCounts.Length; j++) if ((mask & (1u << j)) != 0) { ComponentCounts[j]--; Removes[j]++; TotalComponents--; }
    }
    public bool Alive(EntityKey e) => inner.Alive(e);
    public ref T Get<T>(EntityKey e) where T : unmanaged { if (RecordOperations) Gets[Component<T>.Index]++; return ref inner.Get<T>(e); }
    public bool Has<T>(EntityKey e) where T : unmanaged { if (RecordOperations) HasChecks[Component<T>.Index]++; return inner.Has<T>(e); }
    public void Set<T>(EntityKey e, in T value) where T : unmanaged
    {
        var old = masks[e]; inner.Set(e, value);
        if ((old & Component<T>.Bit) != 0) return;
        Change(e, old, old | Component<T>.Bit); ComponentCounts[Component<T>.Index]++; Adds[Component<T>.Index]++; TotalComponents++;
    }
    public void Remove<T>(EntityKey e) where T : unmanaged
    {
        var old = masks[e]; if ((old & Component<T>.Bit) == 0) return;
        inner.Remove<T>(e); Change(e, old, old & ~Component<T>.Bit); ComponentCounts[Component<T>.Index]--; Removes[Component<T>.Index]++; TotalComponents--;
    }
    private void Change(EntityKey e, uint old, uint next) { Adjust(old, -1); Adjust(next, 1); masks[e] = next; }
    private void Adjust(uint mask, int change)
    {
        var count = BitOperations.PopCount(mask); var kind = Registry.Kind(mask);
        Histogram[count] += change; GroupCounts[kind] += change; GroupComponents[kind] += count * change;
        Signatures.TryGetValue(mask, out var before); var after = before + change;
        if (after == 0) Signatures.Remove(mask); else Signatures[mask] = after;
    }
    public void Commit() => inner.Commit();
    public StoreDiagnostics Diagnostics() => inner.Diagnostics();
    public void Dispose() { if (disposed) return; disposed = true; inner.Dispose(); }
    public void ResetOperations() { Array.Clear(Adds); Array.Clear(Removes); Array.Clear(Gets); Array.Clear(HasChecks); Array.Clear(Bindings); }
    public void ForEach<T, P>(ref P p) where T : unmanaged where P : struct, IProcessor<T>
    { var observer = new Query<T, P> { Store = this, Processor = p }; inner.ForEach<T, Query<T, P>>(ref observer); p = observer.Processor; }
    public void ForEach<A, B, P>(ref P p) where A : unmanaged where B : unmanaged where P : struct, IProcessor<A, B>
    { var observer = new Query<A, B, P> { Store = this, Processor = p }; inner.ForEach<A, B, Query<A, B, P>>(ref observer); p = observer.Processor; }
    public void ForEach<A, B, C, P>(ref P p) where A : unmanaged where B : unmanaged where C : unmanaged where P : struct, IProcessor<A, B, C>
    { var observer = new Query<A, B, C, P> { Store = this, Processor = p }; inner.ForEach<A, B, C, Query<A, B, C, P>>(ref observer); p = observer.Processor; }
    private struct Query<T, P> : IProcessor<T> where T : unmanaged where P : struct, IProcessor<T>
    { public CensusStore Store; public P Processor; public void Execute(EntityKey e, ref T t) { if (Store.RecordOperations) Store.Bindings[Component<T>.Index]++; Processor.Execute(e, ref t); } }
    private struct Query<A, B, P> : IProcessor<A, B> where A : unmanaged where B : unmanaged where P : struct, IProcessor<A, B>
    { public CensusStore Store; public P Processor; public void Execute(EntityKey e, ref A a, ref B b) { if (Store.RecordOperations) { Store.Bindings[Component<A>.Index]++; Store.Bindings[Component<B>.Index]++; } Processor.Execute(e, ref a, ref b); } }
    private struct Query<A, B, C, P> : IProcessor<A, B, C> where A : unmanaged where B : unmanaged where C : unmanaged where P : struct, IProcessor<A, B, C>
    { public CensusStore Store; public P Processor; public void Execute(EntityKey e, ref A a, ref B b, ref C c) { if (Store.RecordOperations) { Store.Bindings[Component<A>.Index]++; Store.Bindings[Component<B>.Index]++; Store.Bindings[Component<C>.Index]++; } Processor.Execute(e, ref a, ref b, ref c); } }
    public void VerifyNativeCounts()
    {
        for (var j = 0; j < Registry.Types.Length; j++)
        {
            var actual = (int)typeof(CensusStore).GetMethod(nameof(NativeCount))!.MakeGenericMethod(Registry.Types[j]).Invoke(this, null)!;
            if (actual != ComponentCounts[j]) throw new InvalidOperationException("Native census mismatch: " + Registry.Names[j]);
        }
        if (inner.Diagnostics().Entities != TotalEntities || Histogram.Sum() != TotalEntities || GroupComponents.Sum() != TotalComponents)
            throw new InvalidOperationException("Observer totals diverged");
    }
    public int NativeCount<T>() where T : unmanaged { var count = new Count<T>(); inner.ForEach<T, Count<T>>(ref count); return count.Value; }
    private struct Count<T> : IProcessor<T> where T : unmanaged { public int Value; public void Execute(EntityKey e, ref T t) => Value++; }
}

internal sealed class Totals(int ticks)
{
    public long EntityTicks, ComponentTicks;
    private double meanOfTickMeans;
    private int minEntities = int.MaxValue, maxEntities, minComponents = int.MaxValue, maxComponents;
    public readonly long[] ComponentEntityTicks = new long[23], Histogram = new long[24];
    public readonly int[] ComponentMin = Enumerable.Repeat(int.MaxValue, 23).ToArray(), ComponentMax = new int[23];
    private readonly long[] groupEntities = new long[6], groupComponents = new long[6];
    private readonly int[] groupMin = Enumerable.Repeat(int.MaxValue, 6).ToArray(), groupMax = new int[6];
    public readonly Dictionary<uint, long> Signatures = new();
    public void Sample(CensusStore s)
    {
        EntityTicks += s.TotalEntities; ComponentTicks += s.TotalComponents;
        meanOfTickMeans += s.TotalComponents / (double)s.TotalEntities;
        minEntities = Math.Min(minEntities, s.TotalEntities); maxEntities = Math.Max(maxEntities, s.TotalEntities);
        minComponents = Math.Min(minComponents, s.TotalComponents); maxComponents = Math.Max(maxComponents, s.TotalComponents);
        for (var j = 0; j < 23; j++) { ComponentEntityTicks[j] += s.ComponentCounts[j]; ComponentMin[j] = Math.Min(ComponentMin[j], s.ComponentCounts[j]); ComponentMax[j] = Math.Max(ComponentMax[j], s.ComponentCounts[j]); }
        for (var j = 0; j < 24; j++) Histogram[j] += s.Histogram[j];
        for (var j = 0; j < 6; j++) { groupEntities[j] += s.GroupCounts[j]; groupComponents[j] += s.GroupComponents[j]; groupMin[j] = Math.Min(groupMin[j], s.GroupCounts[j]); groupMax[j] = Math.Max(groupMax[j], s.GroupCounts[j]); }
        foreach (var (mask, count) in s.Signatures) { Signatures.TryGetValue(mask, out var before); Signatures[mask] = before + count; }
    }
    public object Summary() => new { EntityTicks, ComponentTicks, MeanEntities = EntityTicks / (double)ticks,
        MeanComponents = ComponentTicks / (double)ticks, MeanComponentsPerEntity = ComponentTicks / (double)EntityTicks,
        MeanOfPerTickMeans = meanOfTickMeans / ticks, MinEntities = minEntities, MaxEntities = maxEntities,
        MinComponents = minComponents, MaxComponents = maxComponents, ObservedCompositions = Signatures.Count };
    public object[] Groups() => Registry.GroupNames.Select((name, j) => (object)new
    { Name = name, EntityTicks = groupEntities[j], ComponentTicks = groupComponents[j], MeanEntities = groupEntities[j] / (double)ticks,
        MeanComponentsPerEntity = groupEntities[j] == 0 ? (double?)null : groupComponents[j] / (double)groupEntities[j],
        MinEntities = groupMin[j], MaxEntities = groupMax[j], EntitySharePercent = groupEntities[j] * 100.0 / EntityTicks }).ToArray();
}
