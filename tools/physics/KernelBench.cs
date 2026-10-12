using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SpaceBattle;

namespace SpaceBattle.KernelStudy;

// Deliberately no top-level statements: PhysicsStudy.csproj also discovers this file.
internal static class KernelBench
{
    private const int Schema = 1;
    private const float Dt = 1f / 60;
    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly PropertyInfo? DiagnosticsProperty = typeof(PhysicsService).GetProperty("Diagnostics");

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("KernelBench --generate --output NEW_DIR [--frames 120 --sweeps 1024 --seed 7331]");
                Console.WriteLine("KernelBench --run --inputs inputs.json --output NEW_DIR --repeat 0 --approved-timing-window [--warmup-passes 2 --instrumented]");
                Console.WriteLine("Run from repository root; outputs must be new directories under Benchmarks.Local/tmp.");
                Console.WriteLine("Use kernel_bench.py run for >=3 fresh-process paired repeats; no Aether reference.");
                return 0;
            }
            var root = Path.GetFullPath(Directory.GetCurrentDirectory());
            if (!File.Exists(Path.Combine(root, "tools", "physics", "KernelBench.csproj")))
                throw new ArgumentException("Run from the repository root.");
            if (args.Contains("--generate") == args.Contains("--run"))
                throw new ArgumentException("Choose exactly one of --generate or --run.");
            if (args.Contains("--run") && !args.Contains("--approved-timing-window"))
                throw new ArgumentException("Main-agent timing-window approval is required.");
            var output = Path.GetFullPath(Required(args, "--output"));
            if (args.Contains("--generate"))
            {
                var frames = Number(args, "--frames", 120);
                var sweeps = Number(args, "--sweeps", 1024);
                var seed = uint.Parse(Value(args, "--seed", "7331")!, CultureInfo.InvariantCulture);
                if (frames < 1 || sweeps < 32) throw new ArgumentException("Require frames >=1 and sweeps >=32.");
                var scenarios = (from count in new[] { 198, 774 }
                                 from density in new[] { "sparse", "dense" }
                                 select Generate(count, density, seed, frames, sweeps))
                    .Append(Generate(774, "dense-slow", seed, frames, sweeps)).ToArray();
                var package = new Inputs(Schema, seed, frames, sweeps, Dt, new PhysicsSettings(), scenarios);
                ReserveOutput(root, output);
                WriteNew(Path.Combine(output, "inputs.json"), package);
                Console.WriteLine(Path.Combine(output, "inputs.json"));
            }
            else
            {
                var inputPath = Path.GetFullPath(Required(args, "--inputs"));
                RequireTmpPath(root, inputPath);
                var bytes = File.ReadAllBytes(inputPath);
                var inputs = JsonSerializer.Deserialize<Inputs>(bytes, Json) ?? throw new ArgumentException("Missing input package.");
                Validate(inputs);
                int repeat = Number(args, "--repeat", -1), warmup = Number(args, "--warmup-passes", 2);
                if (repeat < 0 || warmup < 1) throw new ArgumentException("Require repeat >=0 and warmup-passes >=1.");
                ReserveOutput(root, output);
                File.Copy(inputPath, Path.Combine(output, "inputs.json"), false);
                Archive(root, output);
                bool instrumented = args.Contains("--instrumented");
                var cellSize = Value(args, "--cell-size", null);
                var margin = Value(args, "--margin", null);
                var samples = inputs.Scenarios.Select(s => Measure(s, inputs, warmup, instrumented, cellSize, margin)).ToArray();
                WriteNew(Path.Combine(output, "run.json"), new
                {
                    schema = Schema, repeat, warmupPasses = warmup, instrumented, cellSize, margin,
                    inputSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    runtimeIdentity = RuntimeIdentity(), processId = Environment.ProcessId,
                    stopwatchFrequency = Stopwatch.Frequency,
                    diagnosticsAvailable = DiagnosticsProperty is not null,
                    collectDiagnosticsSettingAvailable = typeof(PhysicsSettings).GetProperty("CollectDiagnostics") is not null,
                    scope = "Native synthetic prescribed-state replay, not endless battle or browser. SHA256 of every exposed body state, begin-contact field and every query hit/miss serial/impact float bit. Private solver/grid layout is intentionally excluded. Timing/allocation excludes hashing, reflection, serialization and I/O.",
                    sources = "sources is the harness snapshot at execution, not proof of the source used to build a baseline DLL.",
                    samples
                });
                Console.WriteLine(Path.Combine(output, "run.json"));
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static object RuntimeIdentity() => new
    {
        framework = RuntimeInformation.FrameworkDescription, version = Environment.Version.ToString(),
        os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        cpuCount = Environment.ProcessorCount,
        environment = new[] { "DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_TC_QuickJit",
            "DOTNET_TC_QuickJitForLoops", "DOTNET_ReadyToRun", "DOTNET_GCHeapHardLimit", "COMPlus_EnableHWIntrinsic" }
            .ToDictionary(k => k, Environment.GetEnvironmentVariable)
    };

    private static void Validate(Inputs inputs)
    {
        if (inputs.Schema != Schema || inputs.Frames < 1 || inputs.Sweeps < 32 ||
            BitConverter.SingleToInt32Bits(inputs.Dt) != BitConverter.SingleToInt32Bits(Dt) ||
            inputs.Settings.CollectTimings || inputs.Scenarios.Length < 4)
            throw new ArgumentException("Invalid input schema/count/dt/settings; internal timings must be disabled.");
        inputs.Settings.Validate();
        var names = new HashSet<string>();
        foreach (var s in inputs.Scenarios)
        {
            if (!names.Add(s.Name) || s.Bodies.Length is not (198 or 774) || s.Frames.Length != inputs.Frames ||
                s.Frames.Any(f => f.Ships.Length != s.Bodies.Length || f.Queries.Length != inputs.Sweeps))
                throw new ArgumentException("Invalid scenario dimensions.");
        }
    }

    private static Sample Measure(Scenario scenario, Inputs inputs, int warmupPasses, bool instrumented, string? cellSize, string? margin)
    {
        // Each process/repeat warms its own world and all frame/query shapes before measurement.
        var settings = inputs.Settings with { CollectTimings = instrumented };
        // The baseline DLL predates this optional init property. Reflection stays outside hot loops.
        typeof(PhysicsSettings).GetProperty("CollectDiagnostics")?.SetValue(settings, instrumented);
        SetControl("ContactCellSize", cellSize); SetControl("ContactCandidateMargin", margin);
        void SetControl(string name, string? value)
        {
            if (value is null) return;
            var property = typeof(PhysicsSettings).GetProperty(name) ?? throw new ArgumentException($"Missing control {name}");
            property.SetValue(settings, double.Parse(value, CultureInfo.InvariantCulture));
        }
        using var physics = new PhysicsService(settings);
        var ids = new int[scenario.Bodies.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            var b = scenario.Bodies[i]; var ship = scenario.Frames[0].Ships[i];
            ids[i] = physics.AddShip(b.Serial, new Transform { Position = ship.Position, Angle = ship.Angle },
                new Collider { Radius = b.Radius, Mass = b.Mass, ShapeCount = 1 });
        }
        var impacts = new QueryResult[inputs.Sweeps];
        var digest = new BitBuffer(checked(64 + ids.Length * 64 + inputs.Settings.MaximumContactPairs * 20 + inputs.Sweeps * 20));
        for (var pass = 0; pass < warmupPasses; pass++)
        {
            Reset(physics, ids, inputs.Dt);
            foreach (var frame in scenario.Frames)
            {
                Set(physics, ids, frame.Ships); physics.Step(inputs.Dt); Hits(physics, frame.Queries, impacts);
                _ = Signatures(physics, ids, scenario.Bodies, frame.Ships, impacts, digest);
                _ = ReadDiagnostics(physics);
            }
        }
        // Two empty positive steps clear both pair-history sets while retaining warm capacity.
        Reset(physics, ids, inputs.Dt);
        var results = new FrameResult[inputs.Frames];
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var memoryBefore = Memory();
        var g0 = GC.CollectionCount(0); var g1 = GC.CollectionCount(1); var g2 = GC.CollectionCount(2);
        long allAllocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < results.Length; i++)
        {
            var input = scenario.Frames[i];
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            Set(physics, ids, input.Ships);
            var end = Stopwatch.GetTimestamp(); var setupBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var setupMs = Milliseconds(start, end);

            allocated = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
            physics.Step(inputs.Dt);
            end = Stopwatch.GetTimestamp(); var stepBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var stepMs = Milliseconds(start, end);
            var stepDiagnostics = ReadDiagnostics(physics);

            allocated = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
            Hits(physics, input.Queries, impacts);
            end = Stopwatch.GetTimestamp(); var hitBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var hitMs = Milliseconds(start, end);

            var signatures = Signatures(physics, ids, scenario.Bodies, input.Ships, impacts, digest);
            results[i] = new(i, setupMs, stepMs, hitMs, setupBytes, stepBytes, hitBytes,
                physics.Bodies, physics.Fixtures, physics.Contacts.Count, impacts.Count(h => h.Hit),
                signatures, physics.QueryStatistics, stepDiagnostics, ReadDiagnostics(physics));
        }
        var totalBytes = GC.GetAllocatedBytesForCurrentThread() - allAllocated;
        var collections = new[] { GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2 };
        var memoryAfter = Memory();
        return new(scenario.Name, ids.Length, results, memoryBefore, memoryAfter, collections,
            totalBytes - results.Sum(f => f.SetupAllocatedBytes + f.StepAllocatedBytes + f.HitAllocatedBytes));
    }

    private static MemorySample Memory()
    {
        using var process = Process.GetCurrentProcess();
        return new(GC.GetTotalMemory(false), GC.GetGCMemoryInfo().HeapSizeBytes,
            process.WorkingSet64, process.PrivateMemorySize64);
    }

    private static JsonElement? ReadDiagnostics(PhysicsService physics)
    {
        if (DiagnosticsProperty is null) return null;
        var value = DiagnosticsProperty.GetValue(physics);
        return value is null ? null : JsonSerializer.SerializeToElement(value, value.GetType(), Json);
    }

    private static void Reset(PhysicsService physics, int[] ids, float dt)
    {
        foreach (var id in ids) physics.SetShip(id, new Transform(), new Motion(), false);
        physics.Step(dt); physics.Step(dt);
    }

    private static void Set(PhysicsService physics, int[] ids, ShipInput[] ships)
    {
        for (var i = 0; i < ids.Length; i++)
        {
            var s = ships[i];
            physics.SetShip(ids[i], new Transform { Position = s.Position, Previous = s.Position, Angle = s.Angle },
                new Motion { Velocity = s.Velocity, AngularVelocity = s.AngularVelocity }, s.Enabled);
        }
    }

    private static void Hits(PhysicsService physics, QueryInput[] queries, QueryResult[] results)
    {
        for (var i = 0; i < queries.Length; i++)
        {
            var q = queries[i];
            bool hit; int serial; Vector2 impact;
            if (q.Rectangle)
                hit = physics.SweepRectangle(q.From, q.To, q.HalfExtents, q.Angle, q.IgnoredSerial, out serial, out impact);
            else hit = physics.Sweep(q.From, q.To, q.Radius, q.IgnoredSerial, out serial, out impact);
            results[i] = new(hit, serial, impact);
        }
    }

    private static Digests Signatures(PhysicsService physics, int[] ids, BodyInput[] bodies, ShipInput[] ships,
        QueryResult[] impacts, BitBuffer buffer)
    {
        buffer.Clear(); buffer.Int(1); buffer.Int(physics.Bodies); buffer.Int(physics.Fixtures); buffer.Int(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            var state = physics.GetShip(ids[i]); var mass = physics.GetMassProperties(ids[i]);
            buffer.Int(ids[i]); buffer.Int(bodies[i].Serial); buffer.Float(bodies[i].Radius);
            buffer.Float(mass.Mass); buffer.Float(mass.Inertia); buffer.Int(ships[i].Enabled ? 1 : 0);
            buffer.Vector(state.Position); buffer.Float(state.Angle); buffer.Vector(state.Velocity); buffer.Float(state.AngularVelocity);
        }
        var bodyDigest = buffer.Hash(0); var offset = buffer.Count;
        buffer.Int(2); buffer.Int(physics.Contacts.Count);
        for (var i = 0; i < physics.Contacts.Count; i++)
        {
            var c = physics.Contacts[i];
            buffer.Int(c.FirstSerial); buffer.Int(c.SecondSerial); buffer.Float(c.RelativeSpeed); buffer.Vector(c.Position);
        }
        var contactDigest = buffer.Hash(offset); offset = buffer.Count;
        buffer.Int(3); buffer.Int(impacts.Length);
        foreach (var impact in impacts)
        {
            buffer.Int(impact.Hit ? 1 : 0); buffer.Int(impact.Serial); buffer.Vector(impact.Impact);
        }
        return new(bodyDigest, contactDigest, buffer.Hash(offset), buffer.Hash(0));
    }

    private sealed class BitBuffer(int capacity)
    {
        private readonly byte[] bytes = new byte[capacity];
        public int Count { get; private set; }
        public void Clear() => Count = 0;
        public void Int(int value) { BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Count, 4), value); Count += 4; }
        public void Float(float value) => Int(BitConverter.SingleToInt32Bits(value));
        public void Vector(Vector2 value) { Float(value.X); Float(value.Y); }
        public string Hash(int start)
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(bytes.AsSpan(start, Count - start), hash);
            return Convert.ToHexString(hash);
        }
    }

    private static Scenario Generate(int count, string density, uint seed, int frames, int sweeps)
    {
        var dense = density.StartsWith("dense", StringComparison.Ordinal);
        var slow = density == "dense-slow";
        var random = new Seeded(seed ^ (uint)count ^ (dense ? 0x9E3779B9u : 0x85EBCA6Bu));
        var bodies = new BodyInput[count]; var initial = new Vector2[count]; var velocities = new Vector2[count];
        var columns = (int)Math.Ceiling(Math.Sqrt(count)); var spacing = dense ? 15f : 100f;
        for (var i = 0; i < count; i++)
        {
            var radius = i % 37 == 0 ? 28f : 4 + random.Next() * 12;
            bodies[i] = new(i + 1, radius, 10 + radius * 2);
            initial[i] = new((i % columns - columns / 2) * spacing, (i / columns - columns / 2) * spacing);
            // Exact-bounds fallback on both sides of the default +/-3072 world grid.
            if (i % 13 == 0) initial[i] += new Vector2(i % 2 == 0 ? 5000 : -5000, 4300);
            velocities[i] = !slow && i % 11 == 0 ? new(0, 1080) : new((random.Next() - .5f) * (slow ? 20 : 100), (random.Next() - .5f) * (slow ? 20 : 100));
        }
        var inputs = new FrameInput[frames];
        for (var tick = 0; tick < frames; tick++)
        {
            var ships = new ShipInput[count];
            for (var i = 0; i < count; i++)
            {
                // Prescribed state keeps density comparable without suppressing this tick's solver/CCD path.
                var position = initial[i] + velocities[i] * (tick % 5 * Dt);
                ships[i] = new(position, (i % 17) * .37f + tick * .003f, velocities[i],
                    ((i % 7) - 3) * .1f, i % 29 != 28);
            }
            var queries = new QueryInput[sweeps];
            for (var q = 0; q < sweeps; q++)
            {
                var target = (q * 11 + tick * 7) % count;
                var p = ships[target].Position;
                var halfMove = ships[target].Velocity * (Dt * .5f);
                Vector2 from, to;
                switch (q % 8)
                {
                    case 0: from = p + halfMove + new Vector2(-40, 0); to = p + halfMove + new Vector2(40, 0); break;
                    case 1: from = p + new Vector2(-80, bodies[target].Radius + 3); to = p + new Vector2(80, bodies[target].Radius + 3); break;
                    case 2: from = new(15000 + q, -16000); to = from + new Vector2(20, 10); break;
                    case 3: from = p; to = p; break;
                    case 4: from = p + new Vector2(-spacing * 4, -spacing); to = p + new Vector2(spacing * 4, spacing); break;
                    case 5: from = new(-5200, p.Y); to = new(5200, p.Y); break;
                    default: from = p + new Vector2((random.Next() - .5f) * spacing * 3, (random.Next() - .5f) * spacing * 3);
                        to = from + new Vector2((random.Next() - .5f) * spacing * 5, (random.Next() - .5f) * spacing * 5); break;
                }
                queries[q] = new(from, to, q % 4 == 0 ? 0 : .5f + random.Next() * 4,
                    q % 3 == 0, new Vector2(1 + random.Next() * 6, .5f + random.Next() * 2),
                    (q % 13) * .23f, q % 7 == 0 ? target + 1 : -1);
            }
            inputs[tick] = new(ships, queries);
        }
        return new($"{count}-{density}", bodies, inputs);
    }

    private sealed class Seeded(uint state)
    {
        private uint state = state == 0 ? 1u : state;
        public float Next()
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (state >> 8) * (1f / 16777216);
        }
    }

    private static void Archive(string root, string output)
    {
        var binaries = Path.Combine(output, "binaries"); Directory.CreateDirectory(binaries);
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
            File.Copy(file, Path.Combine(binaries, Path.GetFileName(file)), false);
        var sources = Path.Combine(output, "sources"); Directory.CreateDirectory(sources);
        foreach (var name in new[] { "KernelBench.cs", "KernelBench.csproj", "kernel_bench.py", "test_kernel_bench.py" })
            File.Copy(Path.Combine(root, "tools", "physics", name), Path.Combine(sources, name), false);
        WriteNew(Path.Combine(output, "assemblies.json"), Directory.GetFiles(binaries, "*.dll")
            .Order(StringComparer.Ordinal).Select(path => new
            {
                name = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            }).ToArray());
    }

    private static void RequireTmpPath(string root, string path)
    {
        var tmp = Path.GetFullPath(Path.Combine(root, "Benchmarks.Local", "tmp"));
        if (!path.StartsWith(tmp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path must be strictly inside Benchmarks.Local/tmp.");
        for (var cursor = path; cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Reparse-point paths are not accepted for benchmark artifacts.");
    }

    private static void ReserveOutput(string root, string path)
    {
        RequireTmpPath(root, path);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Refusing to overwrite an existing output.");
        Directory.CreateDirectory(path);
        using var reservation = new FileStream(Path.Combine(path, ".reserved"), FileMode.CreateNew, FileAccess.Write);
    }
    private static void WriteNew(string path, object value)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(file, value, Json);
    }
    private static double Milliseconds(long start, long end) => (end - start) * (1000.0 / Stopwatch.Frequency);
    private static int Number(string[] args, string name, int fallback) => int.Parse(Value(args, name, fallback.ToString(CultureInfo.InvariantCulture))!, CultureInfo.InvariantCulture);
    private static string Required(string[] args, string name) => Value(args, name, null) ?? throw new ArgumentException($"Missing {name}.");
    private static string? Value(string[] args, string name, string? fallback)
    {
        var i = Array.IndexOf(args, name);
        if (i < 0) return fallback;
        if (i + 1 == args.Length) throw new ArgumentException($"Missing value for {name}.");
        return args[i + 1];
    }

    private sealed record Inputs(int Schema, uint Seed, int Frames, int Sweeps, float Dt, PhysicsSettings Settings, Scenario[] Scenarios);
    private sealed record Scenario(string Name, BodyInput[] Bodies, FrameInput[] Frames);
    private readonly record struct BodyInput(int Serial, float Radius, float Mass);
    private readonly record struct ShipInput(Vector2 Position, float Angle, Vector2 Velocity, float AngularVelocity, bool Enabled);
    private readonly record struct QueryInput(Vector2 From, Vector2 To, float Radius, bool Rectangle, Vector2 HalfExtents, float Angle, int IgnoredSerial);
    private sealed record FrameInput(ShipInput[] Ships, QueryInput[] Queries);
    private readonly record struct QueryResult(bool Hit, int Serial, Vector2 Impact);
    private sealed record Digests(string Body, string Contact, string Impact, string Full);
    private sealed record FrameResult(int Frame, double SetupMs, double StepMs, double HitMs,
        long SetupAllocatedBytes, long StepAllocatedBytes, long HitAllocatedBytes,
        int Bodies, int Fixtures, int Contacts, int Hits, Digests Digests,
        PhysicsQueryStatistics Queries, JsonElement? StepDiagnostics, JsonElement? Diagnostics);
    private sealed record MemorySample(long ManagedLiveBytes, long LastGcHeapBytes, long WorkingSetBytes, long PrivateBytes);
    private sealed record Sample(string Name, int BodyCount, FrameResult[] Frames,
        MemorySample MemoryBefore, MemorySample MemoryAfter, int[] GcCollections, long ObserverAllocatedBytes);
}
