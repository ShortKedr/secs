using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SECS.Benchmarks;

static class StressRunner
{
    private static readonly (string Name, Func<int, IStressBackend> Factory)[] Backends =
    [
        ("SECS", n => new SecsStressBackend(n)),
        ("LeoECS Lite", n => new LiteStressBackend(n)),
        ("LeoECS Classic", n => new ClassicStressBackend(n)),
        ("Morpeh", n => new MorpehStressBackend(n)),
        ("StaticEcs", n => new StaticStressBackend(n))
    ];

    public static bool Handle(string[] args)
    {
        if (!args.Contains("--stress") && !args.Contains("--validate-stress")) return false;
        if (Marshal.SizeOf<StressPayload>() != 256 || Marshal.SizeOf<StressPosition>() != 12 || Marshal.SizeOf<StressVelocity>() != 12)
            throw new InvalidOperationException("Stress component layout mismatch");
        var backends = args.Contains("--secs-only") ? Backends.Where(b => b.Name == "SECS").ToArray() : Backends;
        foreach (var backend in backends)
        foreach (var count in new[] { 257, 4_097 })
        {
            foreach (var scenario in StressWorkload.Scenarios)
            {
                using var workload = new StressWorkload(count, backend.Factory, scenario == "WideIteration");
                workload.Validate(scenario, 0);
                for (var invocation = 1; invocation <= 3; invocation++)
                {
                    workload.Execute(scenario);
                    workload.Validate(scenario, invocation);
                }
            }
            using (var workload = new StressWorkload(count, backend.Factory)) workload.ValidatePayloadRoundTrip();
            Console.WriteLine($"Stress validation passed: {backend.Name}, N={count}, 7 scenarios x 3 repetitions + payload round-trip");
        }
        if (args.Contains("--validate-stress")) return true;
        if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0")
            throw new InvalidOperationException("Set DOTNET_TieredCompilation=0 before comparison measurements");

        const int sampleCount = 8;
        var results = new List<object>();
        var scenarios = Environment.GetEnvironmentVariable("SECS_BENCHMARK_SCENARIOS")?.Split(',') ?? StressWorkload.Scenarios;
        if (scenarios.Any(s => !StressWorkload.Scenarios.Contains(s))) throw new ArgumentException("Unknown stress scenario filter");
        var cases = (from backend in backends from count in new[] { 100_000, 1_000_000 }
                     from scenario in scenarios select (backend, count, scenario)).ToArray();
        new Random(2048).Shuffle(cases);
        foreach (var (backend, count, scenario) in cases)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var workload = new StressWorkload(count, backend.Factory, scenario == "WideIteration");
            workload.Validate(scenario, 0);
            var invocations = 0;
            double Measure(int repetitions, out double allocatedBytes)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < repetitions; i++) workload.Execute(scenario);
                var elapsed = Stopwatch.GetTimestamp() - start;
                allocatedBytes = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / repetitions;
                invocations += repetitions;
                return elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / repetitions;
            }
            var warmup = Stopwatch.StartNew();
            do { Measure(1, out _); } while (warmup.ElapsedMilliseconds < 300);
            var pilot = Measure(1, out _);
            var repetitions = Math.Clamp((int)Math.Ceiling(30_000_000 / pilot), 1, 16);
            if (scenario == "MixedFrames") repetitions = 1;
            var samples = new double[sampleCount];
            var allocations = new double[sampleCount];
            for (var sample = 0; sample < sampleCount; sample++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                samples[sample] = Measure(repetitions, out allocations[sample]);
            }
            workload.Validate(scenario, invocations);
            var mean = samples.Average();
            var stddev = Math.Sqrt(samples.Sum(x => (x - mean) * (x - mean)) / (sampleCount - 1));
            results.Add(new { framework = backend.Name, scenario, entityCount = count,
                meanNs = mean, stddevNs = stddev, samplesNs = samples,
                allocatedBytes = allocations.Average(), repetitions, invocations, checksum = workload.Checksum });
            Console.WriteLine($"Stress {backend.Name,-16} {scenario,-16} N={count,7}: {mean / 1e6:F3} ms, SD={stddev / 1e6:F3}, validated {invocations} operations");
            Save(results, sampleCount, args);
        }
        return true;
    }

    private static void Save(List<object> results, int sampleCount, string[] args)
    {
        var metadata = new
        {
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            machine = Environment.MachineName, date = DateTimeOffset.Now.ToString("O"), sampleCount,
            layouts = StressData.LayoutCount, payloadBytes = Marshal.SizeOf<StressPayload>(),
            hotStride = StressData.HotStride, mixedFrames = StressWorkload.MixedFrameCount,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            footer = "Release; single thread; 8 samples; setup/forced GC excluded; Morpeh Commit included",
            versions = new { SECS = Environment.GetEnvironmentVariable("SECS_BENCHMARK_REVISION") ?? "working tree: lookup, chunk reuse, transition cache", lite = "2024.5.22", classic = "2023.6.22",
                morpeh = "2024.1.1", staticEcs = "2.2.9", staticPack = "1.2.6" },
            revisions = new { classic = "1213c31c9ef8325d48e74a52b345c4db242d038b", lite = "5053141d7d663c6b1855c8409ac93dfac807b239",
                morpeh = "9f6369384792a01d74df24efd35b08ae313e4f12", staticEcs = "23b1cf36d89497e8a126d38459d688e2b0a5dea3",
                staticPack = "7bfb6cf925901b9366dda884258d727a89fff76e" }
        };
        var output = args.FirstOrDefault(a => a.EndsWith(".json")) ?? "Benchmarks.Local/stress-results.json";
        File.WriteAllText(output, JsonSerializer.Serialize(new { metadata, results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
