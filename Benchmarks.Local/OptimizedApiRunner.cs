using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SECS;
using SECS.Benchmarks;

static class OptimizedApiRunner
{
    private const int SampleCount = 8;
    private const double TargetSampleNs = 30_000_000;
    private static readonly StressPosition Position = new() { X = 17, Y = -23, Z = 41 };
    private static readonly StressVelocity Velocity = new() { X = 1, Y = -1, Z = 2 };
    private static readonly string[] CreationMethods = ["EmptyAddAdd", "CreateEntityComponents", "PreparedFactoryBatch"];

    public static bool Handle(string[] args)
    {
        if (!args.Contains("--optimized-api")) return false;
        if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0")
            throw new InvalidOperationException("Set DOTNET_TieredCompilation=0 before optimized API measurements.");

        var output = args.FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            ?? "Benchmarks.Local/optimized-api-results.json";
        var results = new List<object>();
        foreach (var count in new[] { 100_000, 1_000_000 })
        foreach (var method in CreationMethods)
            results.Add(MeasureCreation(method, count));
        foreach (var count in new[] { 100_000, 1_000_000 })
        {
            using var lookup = new LookupWorkload(count);
            results.Add(MeasureLookup("WorldGet", lookup, count));
            results.Add(MeasureLookup("CachedAccessorGet", lookup, count));
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
            sampleCount = SampleCount,
            targetSampleNs = TargetSampleNs,
            warmupMinimumMs = 300,
            maximumRepetitions = 16,
            footer = "Release; tiering off; exactly 8 samples; creation setup/disposal/forced GC excluded; warmup duration accumulates timed creation operations only; validation outside timer; creation templates prewarmed for every method; lookup seed 2052513957 is paired within this benchmark, not claimed to match StressWorkload.RandomLookup",
            layouts = StressData.LayoutCount,
            accessorBufferBytesNote = "Exact managed accessor array payload bytes from StorageStatistics; excludes array headers and other managed storage.",
            creationComponents = new { position = new { Position.X, Position.Y, Position.Z },
                velocity = new { Velocity.X, Velocity.Y, Velocity.Z } }
        };
        File.WriteAllText(output, JsonSerializer.Serialize(new { metadata, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Saved {output}");
        return true;
    }

    private static object MeasureCreation(string method, int count)
    {
        double warmupNs = 0;
        while (warmupNs < 300_000_000)
            warmupNs += TimeCreation(method, count, out _, out _);

        var pilot = TimeCreation(method, count, out _, out _);
        var repetitions = Math.Clamp((int)Math.Ceiling(TargetSampleNs / pilot), 1, 16);
        var samples = new double[SampleCount];
        var allocations = new double[SampleCount];
        var nativeBytes = new long[SampleCount];
        for (var sample = 0; sample < SampleCount; sample++)
            samples[sample] = TimeCreation(method, count, out allocations[sample], out nativeBytes[sample], repetitions);
        var mean = samples.Average();
        var stddev = Math.Sqrt(samples.Sum(x => (x - mean) * (x - mean)) / (SampleCount - 1));
        Console.WriteLine($"Optimized API create {method,-24} N={count,7}: {mean / 1e6:F3} ms, SD={stddev / 1e6:F3}, {repetitions} repetitions/sample");
        return new { operation = "Create", method, entityCount = count, meanNs = mean, stddevNs = stddev,
            samplesNs = samples, allocatedBytes = allocations.Average(), storageNativeBytes = nativeBytes.Average(), repetitions };
    }

    private static double TimeCreation(string method, int count, out double allocatedBytes, out long nativeBytes, int repetitions = 1)
    {
        long ticks = 0, allocations = 0;
        nativeBytes = 0;
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var workload = new CreationWorkload(count);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            workload.Create(method);
            ticks += Stopwatch.GetTimestamp() - start;
            allocations += GC.GetAllocatedBytesForCurrentThread() - before;
            workload.Validate();
            nativeBytes += workload.NativeBytes;
        }
        allocatedBytes = (double)allocations / repetitions;
        nativeBytes /= repetitions;
        return ticks * (1_000_000_000.0 / Stopwatch.Frequency) / repetitions;
    }

    private static object MeasureLookup(string method, LookupWorkload workload, int count)
    {
        Func<long> read = method == "WorldGet" ? workload.ReadWorld : workload.ReadAccessor;
        var expected = workload.ExpectedChecksum;
        var warmup = Stopwatch.StartNew();
        do
        {
            read();
            if (workload.Checksum != expected) throw new InvalidOperationException($"{method} checksum mismatch during warmup.");
        } while (warmup.ElapsedMilliseconds < 300);
        var pilot = TimeLookup(read, out _);
        var repetitions = Math.Clamp((int)Math.Ceiling(TargetSampleNs / pilot), 1, 16);
        var samples = new double[SampleCount];
        var allocations = new double[SampleCount];
        for (var sample = 0; sample < SampleCount; sample++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            samples[sample] = TimeLookup(read, out allocations[sample], repetitions);
            if (workload.Checksum != expected) throw new InvalidOperationException($"{method} checksum mismatch after sample {sample}.");
        }
        var mean = samples.Average();
        var stddev = Math.Sqrt(samples.Sum(x => (x - mean) * (x - mean)) / (SampleCount - 1));
        Console.WriteLine($"Optimized API lookup {method,-24} N={count,7}: {mean / 1e6:F3} ms, SD={stddev / 1e6:F3}, checksum={workload.Checksum}");
        return new { operation = "Lookup", method, entityCount = count, meanNs = mean, stddevNs = stddev,
            samplesNs = samples, allocatedBytes = allocations.Average(), checksum = workload.Checksum, repetitions,
            storageNativeBytes = workload.NativeBytes,
            accessorBufferBytes = workload.AccessorBufferBytes };
    }

    private static double TimeLookup(Func<long> read, out double allocatedBytes, int repetitions = 1)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < repetitions; i++) read();
        var ticks = Stopwatch.GetTimestamp() - start;
        allocatedBytes = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / repetitions;
        return ticks * (1_000_000_000.0 / Stopwatch.Frequency) / repetitions;
    }

    private sealed class CreationWorkload : IDisposable
    {
        private readonly World _world;
        private readonly Entity[] _entities;
        private readonly EntityFactory<StressPosition, StressVelocity> _factory;
        public long NativeBytes => _world.GetStorageStatistics().NativeBytes;

        public CreationWorkload(int count)
        {
            _world = new World(new WorldOptions(chunkCapacity: 256));
            _entities = new Entity[count];
            _factory = _world.PrepareEntity<StressPosition, StressVelocity>();
        }

        public void Create(string method)
        {
            switch (method)
            {
                case "EmptyAddAdd":
                    for (var i = 0; i < _entities.Length; i++)
                    {
                        var entity = _world.CreateEntity();
                        _world.Add(entity, Position);
                        _world.Add(entity, Velocity);
                        _entities[i] = entity;
                    }
                    break;
                case "CreateEntityComponents":
                    for (var i = 0; i < _entities.Length; i++) _entities[i] = _world.CreateEntity(Position, Velocity);
                    break;
                case "PreparedFactoryBatch": _factory.CreateBatch(_entities, Position, Velocity); break;
                default: throw new ArgumentOutOfRangeException(nameof(method));
            }
        }

        public void Validate()
        {
            for (var i = 0; i < _entities.Length; i++)
            {
                var entity = _entities[i];
                var position = _world.Get<StressPosition>(entity);
                var velocity = _world.Get<StressVelocity>(entity);
                if (!_world.IsAlive(entity) || position.X != Position.X || position.Y != Position.Y || position.Z != Position.Z ||
                    velocity.X != Velocity.X || velocity.Y != Velocity.Y || velocity.Z != Velocity.Z)
                    throw new InvalidOperationException($"Creation output validation failed at entity {i}.");
            }
        }

        public void Dispose() => _world.Dispose();
    }

    private sealed class LookupWorkload : IDisposable
    {
        private readonly World _world;
        private readonly Entity[] _entities;
        private readonly int[] _order;
        private readonly ComponentAccessor<StressPosition> _accessor;
        public long ExpectedChecksum { get; }
        public long Checksum { get; private set; }
        private StorageStatistics Statistics => _world.GetStorageStatistics();
        public long NativeBytes => Statistics.NativeBytes;
        public long AccessorBufferBytes => Statistics.AccessorBufferBytes;

        public LookupWorkload(int count)
        {
            _world = new World(new WorldOptions(chunkCapacity: 256));
            _entities = new Entity[count];
            _order = Enumerable.Range(0, count).ToArray();
            new Random(2052513957).Shuffle(_order);
            long expected = 0;
            for (var i = 0; i < count; i++)
            {
                var entity = _world.CreateEntity();
                _entities[i] = entity;
                var position = StressData.Position(i);
                _world.Add(entity, position);
                _world.Add(entity, StressData.Velocity);
                expected += (long)position.X + position.Y + position.Z;
                var shape = StressData.Shape(i);
                if ((shape & 1) != 0) _world.Add(entity, new StressTag1 { Value = 1 });
                if ((shape & 2) != 0) _world.Add(entity, new StressTag2 { Value = 1 });
                if ((shape & 4) != 0) _world.Add(entity, new StressTag3 { Value = 1 });
                if ((shape & 8) != 0) _world.Add(entity, new StressTag4 { Value = 1 });
                if ((shape & 16) != 0) _world.Add(entity, new StressTag5 { Value = 1 });
                if ((shape & 32) != 0) _world.Add(entity, new StressTag6 { Value = 1 });
                if ((shape & 64) != 0) _world.Add(entity, new StressTag7 { Value = 1 });
                if ((shape & 128) != 0) _world.Add(entity, new StressTag8 { Value = 1 });
            }
            ExpectedChecksum = expected;
            _accessor = _world.GetAccessor<StressPosition>();
        }

        public long ReadWorld()
        {
            long checksum = 0;
            foreach (var index in _order)
            {
                ref var position = ref _world.Get<StressPosition>(_entities[index]);
                checksum += (long)position.X + position.Y + position.Z;
            }
            return Checksum = checksum;
        }

        public long ReadAccessor()
        {
            long checksum = 0;
            foreach (var index in _order)
            {
                ref var position = ref _accessor.Get(_entities[index]);
                checksum += (long)position.X + position.Y + position.Z;
            }
            return Checksum = checksum;
        }

        public void Dispose() => _world.Dispose();
    }
}
