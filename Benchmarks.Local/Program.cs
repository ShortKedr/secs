using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

if (StorageRunner.Handle(args)) return;
if (StressRunner.Handle(args)) return;
if (OptimizedApiRunner.Handle(args)) return;

var factories = new (string Name, Func<int, IWorkload> Create)[]
{
    ("SECS", n => new SecsWorkload(n)),
    ("LeoECS Lite", n => new LiteWorkload(n)),
    ("LeoECS Classic", n => new ClassicWorkload(n)),
    ("Morpeh", n => new MorpehWorkload(n)),
    ("StaticEcs", n => new StaticWorkload(n))
};
if (args.Contains("--secs-only")) factories = factories.Where(f => f.Name == "SECS").ToArray();
var counts = new[] { 1_000, 100_000 };
var scenarios = new[] { "Create", "Iterate", "Churn", "Destroy" };
foreach (var factory in factories)
foreach (var count in counts)
{
    using var workload = factory.Create(count);
    workload.Create();
    workload.Validate(0);
    workload.Iterate();
    workload.Validate(1);
    workload.Churn();
    workload.Validate(1);
    workload.Iterate();
    workload.Validate(2);
    workload.Destroy();
    workload.Validate(0, destroyed: true);
    Console.WriteLine($"Validated {factory.Name}, N={count}");
}
if (args.Contains("--validate")) return;

const int sampleCount = 20;
const double targetSampleNs = 30_000_000;
var results = new List<object>();
var cases = (from factory in factories from count in counts from scenario in scenarios
             select (factory, count, scenario)).ToArray();
// Deterministic shuffle reduces correlation between framework order and machine drift.
var random = new Random(2048);
random.Shuffle(cases);
foreach (var (factory, count, scenario) in cases)
{
    var reset = scenario is "Create" or "Destroy";
    IWorkload? workload = null;
    void Prepare()
    {
        if (workload != null && !reset) return;
        workload?.Dispose();
        workload = factory.Create(count);
        if (scenario != "Create") workload.Create();
    }
    void Execute()
    {
        switch (scenario)
        {
            case "Create": workload!.Create(); break;
            case "Iterate": workload!.Iterate(); break;
            case "Churn": workload!.Churn(); break;
            case "Destroy": workload!.Destroy(); break;
        }
    }
    double Measure(int repetitions, out double allocations)
    {
        long ticks = 0, bytes = 0;
        for (var i = 0; i < repetitions; i++)
        {
            Prepare();
            if (reset)
            {
                // Seeding/disposal garbage must not be charged to the next lifecycle operation.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            Execute();
            ticks += Stopwatch.GetTimestamp() - start;
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        allocations = (double)bytes / repetitions;
        return ticks * (1_000_000_000.0 / Stopwatch.Frequency) / repetitions;
    }
    // Warm both JIT and steady-state structural storage before recording samples.
    var warmup = Stopwatch.StartNew();
    do { Measure(1, out _); } while (warmup.ElapsedMilliseconds < 400);
    var pilot = Measure(3, out _);
    var repetitions = reset ? 1 : Math.Clamp((int)Math.Ceiling(targetSampleNs / pilot), 1, 1000);
    var samples = new double[sampleCount];
    var allocations = new double[sampleCount];
    for (var sample = 0; sample < sampleCount; sample++)
    {
        if (!reset) GC.Collect();
        samples[sample] = Measure(repetitions, out allocations[sample]);
    }
    workload?.Dispose();
    var mean = samples.Average();
    var stddev = Math.Sqrt(samples.Sum(x => (x - mean) * (x - mean)) / (sampleCount - 1));
    results.Add(new { framework = factory.Name, scenario, entityCount = count, meanNs = mean,
        stddevNs = stddev, samplesNs = samples, allocatedBytes = allocations.Average(), repetitions });
    Console.WriteLine($"{factory.Name,-16} {scenario,-8} N={count,6}: {mean / count:F2} ns/entity, SD={stddev / count:F2}");
}
var metadata = new
{
    runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    machine = BenchmarkMachineLabel.Value,
    date = DateTimeOffset.Now.ToString("O"),
    stopwatchFrequency = Stopwatch.Frequency,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
    sampleCount,
    targetSampleNs,
    footer = "Release; tiering off; 20 samples; setup/forced GC excluded; Morpeh Commit included",
    revisions = new { SECS = Environment.GetEnvironmentVariable("SECS_BENCHMARK_REVISION") ?? "working tree: lookup, chunk reuse, transition cache", classic = "1213c31c9ef8325d48e74a52b345c4db242d038b",
        lite = "5053141d7d663c6b1855c8409ac93dfac807b239", morpeh = "9f6369384792a01d74df24efd35b08ae313e4f12",
        staticEcs = "23b1cf36d89497e8a126d38459d688e2b0a5dea3", staticPack = "7bfb6cf925901b9366dda884258d727a89fff76e" }
};
var output = args.FirstOrDefault(a => a.EndsWith(".json")) ?? "Benchmarks.Local/results.json";
File.WriteAllText(output, JsonSerializer.Serialize(new { metadata, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Saved {output}");
