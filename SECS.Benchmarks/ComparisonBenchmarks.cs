using BenchmarkDotNet.Attributes;

namespace SECS.Benchmarks;

[MemoryDiagnoser]
public class ComparisonBenchmarks
{
    private ComparisonWorkload _workload = null!;

    [Params(1_000, 100_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _workload = new ComparisonWorkload(EntityCount);
        _workload.Create();
    }

    [IterationSetup(Target = nameof(Create))]
    public void SetupCreate()
    {
        _workload.Dispose();
        _workload = new ComparisonWorkload(EntityCount);
    }

    [IterationSetup(Target = nameof(Destroy))]
    public void SetupDestroy()
    {
        SetupCreate();
        _workload.Create();
    }

    [GlobalCleanup]
    public void Cleanup() => _workload.Dispose();

    [Benchmark] public void Create() => _workload.Create();
    [Benchmark] public void Iterate() => _workload.Iterate();
    [Benchmark] public void Churn() => _workload.Churn();
    [Benchmark] public void Destroy() => _workload.Destroy();
}
