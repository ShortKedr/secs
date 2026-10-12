using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SpaceBattle;
using SpaceBattle.PhysicsStudy;

return Study.Run(args);

internal static class Study
{
    internal const float Dt = 1f / 60;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("PhysicsStudy --inspect | --verify-baseline | --verify | --approved-timing-window [--ticks 120 --warmup 20 --sweeps 1024 --repeats 3 --seed 7331 --run NAME]");
                Console.WriteLine("Run from repository root. Build first; execute the DLL, not dotnet run. Timing requires explicit main-agent approval.");
                return 0;
            }
            if (args.Contains("--inspect"))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { baseline = AetherReference.InspectDiagnostics(), candidate = Candidate.Inspect() }, Json));
                return 0;
            }
            if (args.Contains("--verify-baseline")) { Verify(true); return 0; }
            if (args.Contains("--verify")) { Verify(false); return 0; }
            if (!args.Contains("--approved-timing-window")) throw new ArgumentException("Timing window has not been acknowledged.");
            int Number(string name, int fallback) => int.Parse(Value(args, name, fallback.ToString()), System.Globalization.CultureInfo.InvariantCulture);
            int ticks = Number("--ticks", 120), warmup = Number("--warmup", 20), sweeps = Number("--sweeps", 1024), repeats = Number("--repeats", 3), seed = Number("--seed", 7331);
            if (ticks < 1 || warmup < 1 || sweeps < 1000 || repeats < 3) throw new ArgumentException("Require ticks/warmup >=1, sweeps >=1000 and fresh repeats >=3.");
            Candidate.RequireApi();
            var root = Directory.GetCurrentDirectory();
            if (!File.Exists(Path.Combine(root, "tools", "physics", "PhysicsStudy.csproj"))) throw new ArgumentException("Run from repository root.");
            var runName = Value(args, "--run", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N")[..8]);
            if (runName.Length == 0 || runName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) throw new ArgumentException("Run name must be a simple ASCII identifier.");
            var output = Path.Combine(root, "Benchmarks.Local", "tmp", "physics-study", runName);
            if (Directory.Exists(output)) throw new ArgumentException("Run directory already exists; choose a fresh name.");
            Directory.CreateDirectory(output);
            var archive = Path.Combine(output, "binaries");
            Directory.CreateDirectory(archive);
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
                File.Copy(file, Path.Combine(archive, Path.GetFileName(file)), false);
            var source = Path.Combine(output, "sources");
            Directory.CreateDirectory(source);
            foreach (var name in new[] { "Program.cs", "AetherReference.cs", "PhysicsStudy.csproj", "README.md", "README.ru.md" })
                File.Copy(Path.Combine(root, "tools", "physics", name), Path.Combine(source, name), false);
            foreach (var file in Directory.GetFiles(Path.Combine(root, "SpaceBattle.Core"), "*.cs"))
                File.Copy(file, Path.Combine(source, "Core-" + Path.GetFileName(file)), false);
            Write(Path.Combine(output, "manifest.json"), new
            {
                schema = 1, seed, ticks, warmup, sweeps, repeats, dt = Dt,
                runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(), cpuCount = Environment.ProcessorCount,
                runtimeEnvironment = new Dictionary<string, string?>
                {
                    ["DOTNET_TieredCompilation"] = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                    ["DOTNET_TieredPGO"] = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                    ["DOTNET_TC_QuickJit"] = Environment.GetEnvironmentVariable("DOTNET_TC_QuickJit"),
                    ["DOTNET_TC_QuickJitForLoops"] = Environment.GetEnvironmentVariable("DOTNET_TC_QuickJitForLoops"),
                    ["DOTNET_ReadyToRun"] = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun")
                },
                stopwatchFrequency = Stopwatch.Frequency, settings = Candidate.DefaultSettings(), candidate = Candidate.Inspect(), baseline = AetherReference.InspectDiagnostics(),
                baselineSettings = new { friction = 0f, restitution = .1f, aetherDefaultIterations = true, scale = .1f },
                assemblies = Directory.GetFiles(archive, "*.dll").Select(p => new { name = Path.GetFileName(p), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) }).ToArray(),
                note = "Externally prescribed synthetic scenarios at historical ship counts, not historical match replay. No production/gameplay or browser performance claims."
            });
            foreach (int count in new[] { 198, 774 })
            foreach (var kind in new[] { "sparse-round", "mixed-round", "legacy-compound", "dense-round", "moving-target-round" })
            {
                var scenario = Generate(count, kind, seed, ticks, sweeps);
                var inputPath = Path.Combine(output, scenario.Name + "-inputs.json");
                Write(inputPath, scenario);
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(inputPath)));
                // Each warmup and each measured sample owns a fresh world. Input generation and I/O are outside timing.
                foreach (bool instrumented in new[] { false, true })
                foreach (bool baseline in new[] { true, false })
                    using (var world = Create(baseline, instrumented)) Execute(world, scenario, warmup, false);
                var samples = new List<Sample>();
                for (int repeat = 0; repeat < repeats; repeat++)
                foreach (bool instrumented in new[] { false, true })
                foreach (bool baseline in repeat % 2 == 0 ? new[] { true, false } : new[] { false, true })
                {
                    using var world = Create(baseline, instrumented);
                    Populate(world, scenario);
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    var frames = Execute(world, scenario, ticks, true, populated: true);
                    samples.Add(new(repeat, baseline ? "aether-2.2.0" : "circles-relativeCCD", instrumented, frames));
                }
                Write(Path.Combine(output, scenario.Name + "-raw.json"), new { inputSha256 = hash, samples });
                var reproducibility = samples.GroupBy(s => s.Engine).Select(group => new
                {
                    engine = group.Key,
                    stableWithinEngine = group.All(sample => sample.Frames.Select(Signature).SequenceEqual(group.First().Frames.Select(Signature)))
                }).ToArray();
                var comparisons = Enumerable.Range(0, repeats).Select(repeat =>
                {
                    var old = samples.Single(s => s.Repeat == repeat && !s.Instrumented && s.Engine == "aether-2.2.0");
                    var current = samples.Single(s => s.Repeat == repeat && !s.Instrumented && s.Engine == "circles-relativeCCD");
                    return new { repeat, baselineHits = old.Frames.Sum(f => f.Hits), candidateHits = current.Frames.Sum(f => f.Hits),
                        baselineMovingHits = old.Frames.Sum(f => f.MovingProbeHits), candidateMovingHits = current.Frames.Sum(f => f.MovingProbeHits),
                        sameHitDigestFrames = old.Frames.Zip(current.Frames).Count(pair => pair.First.HitDigest == pair.Second.HitDigest) };
                }).ToArray();
                var overhead = samples.GroupBy(s => s.Engine).Select(group => new
                {
                    engine = group.Key,
                    uninstrumentedMedianTotalMs = Median(group.Where(s => !s.Instrumented).Select(s => s.Frames.Sum(f => f.TotalMs))),
                    instrumentedMedianTotalMs = Median(group.Where(s => s.Instrumented).Select(s => s.Frames.Sum(f => f.TotalMs)))
                }).ToArray();
                Write(Path.Combine(output, scenario.Name + "-comparison.json"), new { reproducibility, comparisons, overhead,
                    note = "Hit differences are observations, not failures: sampled Aether rays vs exact swept circles/relative target motion; compound geometry intentionally differs." });
                if (reproducibility.Any(r => !r.stableWithinEngine)) throw new InvalidOperationException($"Within-engine results changed for {scenario.Name}; raw evidence preserved.");
                Console.WriteLine($"Completed {scenario.Name}");
            }
            Console.WriteLine(output);
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static string Value(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        if (i < 0) return fallback;
        if (i + 1 == args.Length) throw new ArgumentException($"Missing value for {name}");
        return args[i + 1];
    }
    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    private static string Signature(FrameResult frame) => $"{frame.Hits}:{frame.MovingProbeHits}:{frame.HitDigest}:{frame.Contacts}:{frame.Bodies}:{frame.Fixtures}";
    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 0 ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2];
    }
    private static Engine Create(bool baseline, bool timings) => baseline ? new Reference(timings) : new Candidate(timings);
    private static void Populate(Engine engine, Scenario scenario)
    {
        for (int i = 0; i < scenario.Colliders.Length; i++)
            engine.Add(i + 1, scenario.Frames[0].Ships[i].Transform, scenario.Colliders[i]);
    }

    private static FrameResult[] Execute(Engine engine, Scenario scenario, int ticks, bool measure, bool populated = false)
    {
        if (!populated) Populate(engine, scenario);
        var results = measure ? new FrameResult[ticks] : [];
        for (int tick = 0; tick < ticks; tick++)
        {
            var input = scenario.Frames[tick % scenario.Frames.Length];
            long allocated = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
            long start = measure ? Stopwatch.GetTimestamp() : 0;
            for (int i = 0; i < input.Ships.Length; i++) engine.Set(i, input.Ships[i]);
            long stepStart = measure ? Stopwatch.GetTimestamp() : 0;
            engine.Step(Dt);
            double stepMs = measure ? Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds : 0;
            // SetShip clears the replacement's recorded CCD path. Preserve it only in the isolated,
            // collision-free moving-target scene; other hit scenes use prescribed static end geometry.
            bool preserveMotionPath = scenario.Name.EndsWith("moving-target-round", StringComparison.Ordinal);
            for (int i = 0; !preserveMotionPath && i < input.Ships.Length; i++)
            {
                var ship = input.Ships[i];
                var end = ship.Transform;
                end.Previous = end.Position;
                end.Position += ship.Motion.Velocity * Dt;
                end.Angle += ship.Motion.AngularVelocity * Dt;
                engine.Set(i, new(end, ship.Motion));
            }
            long hitStart = measure ? Stopwatch.GetTimestamp() : 0;
            int hits = 0, movingHits = 0;
            ulong digest = 14695981039346656037;
            foreach (var sweep in input.Sweeps)
            {
                bool hit = engine.Sweep(sweep.From, sweep.To, sweep.Radius, sweep.IgnoredSerial, out int serial, out _);
                if (hit) { hits++; if (sweep.MovingProbe) movingHits++; }
                digest = unchecked((digest ^ (uint)(hit ? serial : 0)) * 1099511628211);
            }
            double hitMs = measure ? Stopwatch.GetElapsedTime(hitStart).TotalMilliseconds : 0;
            double totalMs = measure ? Stopwatch.GetElapsedTime(start).TotalMilliseconds : 0;
            if (preserveMotionPath && engine.ContactCount != 0)
                throw new InvalidOperationException("Moving-target scene must remain contact-free: solver response would bias the prescribed path comparison.");
            if (measure) results[tick] = new(tick, totalMs, stepMs, hitMs,
                GC.GetAllocatedBytesForCurrentThread() - allocated, hits, movingHits, digest.ToString("X16"),
                engine.Bodies, engine.Fixtures, engine.ContactCount, engine.QueryStatistics, engine.Phases);
        }
        return results;
    }

    private static Scenario Generate(int count, string kind, int seed, int ticks, int sweeps)
    {
        var random = new Seeded(unchecked((uint)seed) ^ (uint)count);
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        float spacing = kind == "dense-round" ? 13 : 100;
        var positions = new Vector2[count];
        var velocities = new Vector2[count];
        var colliders = new Collider[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = new((i % columns) * spacing, (i / columns) * spacing);
            float radius = kind == "moving-target-round" ? 4 : kind == "sparse-round" ? 8 : 4 + random.Next() * 12;
            colliders[i] = new() { Radius = radius, Mass = 10 + radius * 2, ShapeCount = kind == "legacy-compound" ? 4 + i % 3 : 1 };
            velocities[i] = kind == "moving-target-round" ? new(0, 1080) : kind == "dense-round" ? new((random.Next() - .5f) * 20, (random.Next() - .5f) * 20) : Vector2.Zero;
        }
        var frames = new FrameInput[ticks];
        for (int tick = 0; tick < ticks; tick++)
        {
            var ships = new ShipInput[count];
            for (int i = 0; i < count; i++)
            {
                var position = positions[i] + new Vector2(MathF.Sin(tick * .03f + i) * 2, MathF.Cos(tick * .03f + i) * 2);
                ships[i] = new(new() { Position = position, Previous = position - velocities[i] * Dt, Angle = kind == "legacy-compound" ? i * .37f : 0 },
                    new() { Velocity = velocities[i] });
            }
            var queries = new SweepInput[sweeps];
            for (int q = 0; q < sweeps; q++)
            {
                int target = q % count;
                var position = ships[target].Transform.Position;
                bool moving = kind == "moving-target-round" && q % 2 == 0;
                var from = moving ? position + new Vector2(-30, 9) : new Vector2((random.Next() * (columns + 2) - 1) * spacing, (random.Next() * (columns + 2) - 1) * spacing);
                var to = moving ? position + new Vector2(30, 9) : from + new Vector2((random.Next() - .5f) * spacing * 3, (random.Next() - .5f) * spacing * 3);
                queries[q] = new(from, q % 31 == 0 && !moving ? from : to, q % 4 == 0 ? 0 : 1 + random.Next() * 3,
                    q % 7 == 0 ? target + 1 : -1, moving);
            }
            frames[tick] = new(ships, queries);
        }
        return new($"{count}-{kind}", kind == "legacy-compound" ? "intentionally unequal fixture geometry" : "equal round geometry", colliders, frames);
    }

    private static void Verify(bool baselineOnly)
    {
        var a = Generate(198, "mixed-round", 7331, 2, 1024);
        var b = Generate(198, "mixed-round", 7331, 2, 1024);
        if (JsonSerializer.Serialize(a, Json) != JsonSerializer.Serialize(b, Json)) throw new Exception("Input reproducibility failed.");
        using (var baseline = new AetherReference())
        {
            int id = baseline.AddShip(1, new() { Position = Vector2.Zero }, new() { Radius = 8, Mass = 10, ShapeCount = 1 });
            baseline.SetShip(id, new Transform(), new Motion(), true);
            if (!baseline.Sweep(new(-20, 0), new(20, 0), 0, out int serial, out _) || serial != 1) throw new Exception("Baseline hit failed.");
            if (baseline.Sweep(new(-20, 0), new(20, 0), 0, 1, out _, out _)) throw new Exception("Ignored serial failed.");
            baseline.RemoveShip(id);
            if (baseline.Bodies != 0 || baseline.Fixtures != 0) throw new Exception("Baseline lifetime failed.");
        }
        if (baselineOnly) { Console.WriteLine("Baseline lifetime/hit checks and seeded input reproducibility passed. Candidate API not checked. No timing run performed."); return; }
        Candidate.RequireApi();
        using var candidate = new Candidate(false);
        candidate.Add(1, new Transform(), new() { Radius = 8, Mass = 10, ShapeCount = 1 });
        candidate.Set(0, new(new Transform(), new Motion()));
        if (!candidate.Sweep(new(-20, 0), new(20, 0), 0, -1, out int target, out _) || target != 1) throw new Exception("Candidate hit failed.");
        if (!candidate.Rectangle(new(-20, 0), new(20, 0), new(1, 2), 0, -1, out _, out _)) throw new Exception("Rectangle API failed.");
        using var oldMoving = new Reference(false);
        using var newMoving = new Candidate(false);
        foreach (var world in new Engine[] { oldMoving, newMoving })
        {
            world.Add(1, new Transform(), new() { Radius = 4, Mass = 10, ShapeCount = 1 });
            world.Set(0, new(new Transform(), new() { Velocity = new(0, 1080) }));
            world.Step(Dt);
            if (Vector2.Distance(world.Position(0), new(0, 18)) > .001f) throw new Exception("Moving-probe prescribed endpoint changed.");
        }
        if (oldMoving.Sweep(new(-30, 9), new(30, 9), 0, -1, out _, out _)) throw new Exception("Baseline moving-probe control unexpectedly hit.");
        if (!newMoving.Sweep(new(-30, 9), new(30, 9), 0, -1, out int movingSerial, out _) || movingSerial != 1)
            throw new Exception("Replacement relative CCD moving-probe check failed.");
        Console.WriteLine("Non-benchmark verification passed: seeded inputs, baseline lifetime/hits, candidate API/hits/rectangle, intentional moving-target hit difference. No timing run performed.");
    }

    private sealed class Seeded(uint state)
    {
        public float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state >> 8) * (1f / 16777216); }
    }
    private sealed record Scenario(string Name, string GeometryScope, Collider[] Colliders, FrameInput[] Frames);
    private sealed record FrameInput(ShipInput[] Ships, SweepInput[] Sweeps);
    internal readonly record struct ShipInput(Transform Transform, Motion Motion);
    private readonly record struct SweepInput(Vector2 From, Vector2 To, float Radius, int IgnoredSerial, bool MovingProbe);
    private sealed record FrameResult(int Tick, double TotalMs, double CombinedStepMs, double HitsOuterMs, long AllocatedBytes,
        int Hits, int MovingProbeHits, string HitDigest, int Bodies, int Fixtures, int Contacts, object Queries, object? Phases);
    private sealed record Sample(int Repeat, string Engine, bool Instrumented, FrameResult[] Frames);

    private abstract class Engine : IDisposable
    {
        public abstract void Add(int serial, Transform transform, Collider collider);
        public abstract void Set(int id, ShipInput input);
        public abstract void Step(float dt);
        public abstract Vector2 Position(int id);
        public abstract bool Sweep(Vector2 from, Vector2 to, float radius, int ignored, out int serial, out Vector2 impact);
        public abstract int Bodies { get; }
        public abstract int Fixtures { get; }
        public abstract int ContactCount { get; }
        public abstract object QueryStatistics { get; }
        public abstract object? Phases { get; }
        public abstract void Dispose();
    }
    private sealed class Reference(bool timings) : Engine
    {
        private readonly AetherReference physics = new(timings);
        public override void Add(int serial, Transform transform, Collider collider) => physics.AddShip(serial, transform, collider);
        public override void Set(int id, ShipInput input) => physics.SetShip(id, input.Transform, input.Motion, true);
        public override void Step(float dt) => physics.Step(dt);
        public override Vector2 Position(int id) => physics.GetShip(id).Position;
        public override bool Sweep(Vector2 from, Vector2 to, float radius, int ignored, out int serial, out Vector2 impact) => physics.Sweep(from, to, radius, ignored, out serial, out impact);
        public override int Bodies => physics.Bodies;
        public override int Fixtures => physics.Fixtures;
        public override int ContactCount => physics.Contacts.Count;
        public override object QueryStatistics => physics.ReferenceStatistics;
        public override object? Phases => !timings ? null : new { physics.ContactCandidatesMs, physics.SolverMs, physics.HitCandidatesMs, physics.HitNarrowMs, physics.CombinedStepMs };
        public override void Dispose() => physics.Dispose();
    }
    private sealed class Candidate : Engine
    {
        private readonly PhysicsService physics;
        private readonly bool timings;
        private readonly RectangleSweep rectangle;
        private delegate bool RectangleSweep(Vector2 from, Vector2 to, Vector2 halfExtents, float angle, int ignored, out int serial, out Vector2 impact);
        private static Type Settings => typeof(PhysicsService).Assembly.GetType("SpaceBattle.PhysicsSettings") ?? throw new InvalidOperationException("Main's PhysicsSettings API is not built yet.");
        public static object DefaultSettings() => Activator.CreateInstance(Settings)!;
        public static object Inspect() => new
        {
            assembly = typeof(PhysicsService).Assembly.FullName,
            settings = typeof(PhysicsService).Assembly.GetType("SpaceBattle.PhysicsSettings")?.FullName,
            timings = typeof(PhysicsService).GetProperty("Timings")?.PropertyType.FullName,
            rectangle = typeof(PhysicsService).GetMethod("SweepRectangle")?.ToString(),
            massProperties = typeof(PhysicsService).GetMethod("GetMassProperties")?.ToString()
        };
        public static void RequireApi()
        {
            _ = Settings;
            foreach (var name in new[] { "Timings", "QueryStatistics" })
                if (typeof(PhysicsService).GetProperty(name) is null) throw new InvalidOperationException($"Missing PhysicsService.{name}");
            foreach (var name in new[] { "SweepRectangle", "GetMassProperties" })
                if (typeof(PhysicsService).GetMethod(name) is null) throw new InvalidOperationException($"Missing PhysicsService.{name}");
            foreach (var name in new[] { "ContactCandidatesMs", "SolverMs", "HitCandidatesMs", "HitNarrowMs" })
                if (typeof(PhysicsService).GetProperty("Timings")!.PropertyType.GetProperty(name) is null) throw new InvalidOperationException($"Missing timing field {name}");
            foreach (var name in new[] { "Candidates", "NarrowTests" })
                if (typeof(PhysicsService).GetProperty("QueryStatistics")!.PropertyType.GetProperty(name) is null) throw new InvalidOperationException($"Missing query field {name}");
        }
        public Candidate(bool timings)
        {
            this.timings = timings;
            object settings = Activator.CreateInstance(Settings)!;
            var property = Settings.GetProperty("CollectTimings");
            if (property is not null) property.SetValue(settings, timings);
            else (Settings.GetField("CollectTimings") ?? throw new InvalidOperationException("Missing CollectTimings")).SetValue(settings, timings);
            physics = (PhysicsService)Activator.CreateInstance(typeof(PhysicsService), [settings])!;
            rectangle = typeof(PhysicsService).GetMethod("SweepRectangle")!.CreateDelegate<RectangleSweep>(physics);
        }
        public bool Rectangle(Vector2 from, Vector2 to, Vector2 size, float angle, int ignored, out int serial, out Vector2 impact) => rectangle(from, to, size, angle, ignored, out serial, out impact);
        public override void Add(int serial, Transform transform, Collider collider) => physics.AddShip(serial, transform, collider);
        public override void Set(int id, ShipInput input) => physics.SetShip(id, input.Transform, input.Motion, true);
        public override void Step(float dt) => physics.Step(dt);
        public override Vector2 Position(int id) => physics.GetShip(id).Position;
        public override bool Sweep(Vector2 from, Vector2 to, float radius, int ignored, out int serial, out Vector2 impact) => physics.Sweep(from, to, radius, ignored, out serial, out impact);
        public override int Bodies => physics.Bodies;
        public override int Fixtures => physics.Fixtures;
        public override int ContactCount => physics.Contacts.Count;
        public override object QueryStatistics => physics.QueryStatistics;
        public override object? Phases => timings ? typeof(PhysicsService).GetProperty("Timings")!.GetValue(physics) : null;
        public override void Dispose() => physics.Dispose();
    }
}
