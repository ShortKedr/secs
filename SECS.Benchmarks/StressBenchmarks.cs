using BenchmarkDotNet.Attributes;

namespace SECS.Benchmarks;

[MemoryDiagnoser]
public class StressBenchmarks
{
    private StressWorkload _workload = null!;
    public IEnumerable<int> EntityCounts => Environment.GetEnvironmentVariable("SECS_STRESS_EXTREME") == "1"
        ? [5_000_000] : [100_000, 1_000_000];
    [ParamsSource(nameof(EntityCounts))] public int EntityCount { get; set; }

    [GlobalSetup(Targets = [nameof(DenseIteration), nameof(SparseIteration), nameof(RandomLookup), nameof(PayloadChurn), nameof(Recycle10Percent), nameof(MixedFrames)])]
    public void Setup() => _workload = new StressWorkload(EntityCount);
    [GlobalSetup(Target = nameof(WideIteration))]
    public void SetupWide() => _workload = new StressWorkload(EntityCount, wide: true);
    [IterationSetup(Target = nameof(MixedFrames))]
    public void ResetMixed()
    {
        _workload.Dispose();
        Setup();
    }
    [GlobalCleanup] public void Cleanup() => _workload.Dispose();
    [Benchmark] public void DenseIteration() => _workload.Execute(nameof(DenseIteration));
    [Benchmark] public void SparseIteration() => _workload.Execute(nameof(SparseIteration));
    [Benchmark] public void WideIteration() => _workload.Execute(nameof(WideIteration));
    [Benchmark] public long RandomLookup() { _workload.Execute(nameof(RandomLookup)); return _workload.Checksum; }
    [Benchmark] public void PayloadChurn() => _workload.Execute(nameof(PayloadChurn));
    [Benchmark] public void Recycle10Percent() => _workload.Execute(nameof(Recycle10Percent));
    [Benchmark] public long MixedFrames() { _workload.Execute(nameof(MixedFrames)); return _workload.Checksum; }
}
