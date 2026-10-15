using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Silk.NET.OpenGL;

namespace SpaceBattle.Game;

internal sealed class FrontendProfiler : IDisposable
{
    public const double FrameBudgetMs = 16.67;
    private const int QueryRingSize = 32;
    private readonly GL gl;
    private readonly Sample[] samples;
    private readonly double[] hudSamples;
    private readonly double[][] stageSamples;
    private readonly uint[] queries = new uint[QueryRingSize];
    private readonly int[] pendingSamples = new int[QueryRingSize];
    private readonly uint[] errors = new uint[64];
    private readonly int[] errorFrames = new int[64];
    private readonly int warmup;
    private readonly bool paced;
    private readonly string output;
    private readonly string? csv;
    private int frames, activeSlot = -1, nextSlot, errorCount, errorOverflow;
    private int previousSample = -1, exhaustedQueries;
    private long previousStart, firstMeasuredStart, lastMeasuredBoundary;
    private bool disposed, finished;
    private readonly string vendor, device, version;
    public bool GpuSupported { get; private set; }
    public string? GpuUnavailableReason { get; private set; }
    public int CounterBits { get; private set; }
    public bool Complete => frames == warmup + samples.Length;
    public int SampleIndex => frames - warmup;
    public int WarmupFrames => warmup;

    private struct Sample
    {
        public int Tick, Ships, Projectiles, Debris, Particles, Turrets;
        public double SimulationCpuMs, SnapshotCaptureCpuMs, RenderSubmissionCpuMs, TotalCpuFrameMs, WallFrameIntervalMs;
        public double? GpuRenderMs;
    }

    public FrontendProfiler(GL gl, int count, int warmup, string output, string? csv, bool paced)
    {
        this.gl = gl; this.warmup = warmup; this.output = output; this.csv = csv;
        this.paced = paced;
        samples = new Sample[count];
        hudSamples = new double[count];
        stageSamples = Battle<SecsStore>.StageNames.Select(_ => new double[count]).ToArray();
        Array.Fill(pendingSamples, -2);
        vendor = gl.GetStringS(StringName.Vendor);
        device = gl.GetStringS(StringName.Renderer);
        version = gl.GetStringS(StringName.Version);
        CollectErrors(-1);
        // The frontend requests desktop GL 3.3, where ARB_timer_query is core.
        // A zero counter width is explicitly unavailable, not a zero-duration sample.
        try
        {
            gl.GetQuery(QueryTarget.TimeElapsed, QueryParameterName.QueryCounterBits, out int bits);
            var error = gl.GetError();
            CounterBits = bits;
            if (error != GLEnum.NoError || bits <= 0)
            {
                GpuUnavailableReason = $"GL_TIME_ELAPSED unavailable: counter bits={bits}, error={error}";
                if (error != GLEnum.NoError) AddError((uint)error, -1);
                return;
            }
            for (int i = 0; i < queries.Length; i++) queries[i] = gl.GenQuery();
            GpuSupported = true;
            CollectErrors(-1);
        }
        catch (Exception e) when (e is NotSupportedException or EntryPointNotFoundException)
        {
            GpuUnavailableReason = $"Timer query entry point unavailable: {e.Message}";
        }
    }

    public static long Timestamp() => Stopwatch.GetTimestamp();
    public static double Milliseconds(long start, long end) => (end - start) * 1000.0 / Stopwatch.Frequency;

    public void FrameStarted(long timestamp)
    {
        if (previousSample >= 0)
        {
            samples[previousSample].WallFrameIntervalMs = Milliseconds(previousStart, timestamp);
            lastMeasuredBoundary = timestamp;
        }
        int index = SampleIndex;
        previousSample = !Complete && index >= 0 ? index : -1;
        previousStart = timestamp;
        if (!Complete && index == 0) firstMeasuredStart = timestamp;
    }

    public void PollGpu()
    {
        if (!GpuSupported) return;
        for (int i = 0; i < queries.Length; i++)
        {
            if (pendingSamples[i] == -2) continue;
            gl.GetQueryObject(queries[i], QueryObjectParameterName.ResultAvailable, out uint available);
            if (available == 0) continue;
            gl.GetQueryObject(queries[i], QueryObjectParameterName.Result, out ulong nanoseconds);
            if (pendingSamples[i] >= 0) samples[pendingSamples[i]].GpuRenderMs = nanoseconds / 1_000_000.0;
            pendingSamples[i] = -2;
        }
    }

    public void BeginGpu()
    {
        if (!GpuSupported) return;
        for (int n = 0; n < queries.Length; n++)
        {
            int slot = (nextSlot + n) % queries.Length;
            if (pendingSamples[slot] != -2) continue;
            activeSlot = slot;
            nextSlot = (slot + 1) % queries.Length;
            pendingSamples[slot] = SampleIndex >= 0 ? SampleIndex : -1;
            gl.BeginQuery(QueryTarget.TimeElapsed, queries[slot]);
            return;
        }
        if (SampleIndex >= 0) exhaustedQueries++;
    }

    public void EndGpu()
    {
        if (activeSlot < 0) return;
        gl.EndQuery(QueryTarget.TimeElapsed);
        activeSlot = -1;
    }

    public void Record(double simulation, double snapshot, double render, double total, BattleFrame frame,
        double hudTickMs, ReadOnlySpan<double> stages)
    {
        int index = SampleIndex;
        if (index >= 0)
        {
            ref var sample = ref samples[index];
            sample.SimulationCpuMs = simulation; sample.SnapshotCaptureCpuMs = snapshot;
            sample.RenderSubmissionCpuMs = render; sample.TotalCpuFrameMs = total;
            sample.Tick = frame.Tick; sample.Ships = frame.Ships.Count;
            sample.Projectiles = frame.Projectiles.Count; sample.Debris = frame.Debris.Count; sample.Particles = frame.Particles.Count;
            sample.Turrets = frame.Turrets.Count;
            hudSamples[index] = hudTickMs;
            for (var stage = 0; stage < stageSamples.Length; stage++) stageSamples[stage][index] = stages[stage];
        }
        frames++;
    }

    private void AddError(uint error, int frame)
    {
        if (errorCount == errors.Length) { errorOverflow++; return; }
        errors[errorCount] = error; errorFrames[errorCount++] = frame;
    }

    public void CollectErrors(int frame)
    {
        for (int i = 0; i < 64; i++)
        {
            var error = gl.GetError();
            if (error == GLEnum.NoError) return;
            AddError((uint)error, frame);
        }
    }

    private static object? Statistics(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return null;
        double Percentile(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new { Count = sorted.Length, Min = sorted[0], Mean = sorted.Average(), P50 = Percentile(.5), P95 = Percentile(.95), P99 = Percentile(.99), Max = sorted[^1] };
    }

    internal static string AssemblyHash(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public void Finish(Battle<SecsStore> battle, int width, int height, float zoom, bool hidden, bool hasAtlas, bool controlsVerified, int replayStartTick)
    {
        if (finished || !Complete) throw new InvalidOperationException("Profile finish requires exactly the requested frame count.");
        finished = true;
        // All sample CPU timers and final start-to-start interval are already closed.
        // This wait only drains the query tail and is not part of measured throughput.
        gl.Finish();
        PollGpu();
        CollectErrors(frames);
        var gpu = samples.Where(s => s.GpuRenderMs.HasValue).Select(s => s.GpuRenderMs!.Value).ToArray();
        int nullGpu = samples.Length - gpu.Length;
        bool gpuDataValid = !GpuSupported || gpu.Any(value => value > 0);
        double wallMs = samples.Sum(s => s.WallFrameIntervalMs);
        var report = new
        {
            SchemaVersion = 1,
            Mode = paced ? "paced fixed replay: one simulation Step per rendered frame, 60 frames/s cap, VSync off"
                : "deterministic fixed replay: exactly one simulation Step per rendered frame, uncapped, VSync off",
            RealTimeGameplayFpsMeasured = false,
            SampleFrames = samples.Length, WarmupFrames = warmup, StepsPerFrame = 1,
            ReplayStartTick = replayStartTick, FirstMeasuredTick = samples[0].Tick, LastMeasuredTick = samples[^1].Tick,
            Width = width, Height = height, Zoom = zoom, Hidden = hidden, Atlas = hasAtlas,
            VSync = false, FrameRateCap = paced ? battle.Config.TickRate : 0, UpdateRateCap = 0, ControlsVerified = controlsVerified,
            battle.Config, Framework = battle.Framework,
            Date = DateTimeOffset.Now, Machine = BenchmarkMachineLabel.Value,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            GameplayRevision = ArmamentRules.Revision,
            Driver = new { Vendor = vendor, Renderer = device, Version = version },
            CoreDllSha256 = AssemblyHash(typeof(Battle<>).Assembly), GameDllSha256 = AssemblyHash(typeof(FrontendProfiler).Assembly),
            SecsAdapterDllSha256 = AssemblyHash(typeof(SecsStore).Assembly),
            SecsDllSha256 = AssemblyHash(typeof(SECS.World).Assembly),
            FinalBattleChecksum = battle.Checksum().ToString("X16", CultureInfo.InvariantCulture),
            Counters = JsonSerializer.SerializeToElement(battle.Counters.Copy(), new JsonSerializerOptions { IncludeFields = true }),
            Tactics = JsonSerializer.SerializeToElement(battle.Tactics, new JsonSerializerOptions { IncludeFields = true }),
            StopwatchFrequencyHz = Stopwatch.Frequency, FirstMeasuredStartTimestamp = firstMeasuredStart, LastMeasuredBoundaryTimestamp = lastMeasuredBoundary,
            Definitions = new
            {
                SimulationCPU = "Step wrapper; live rolling statistics are disabled in replay profiling; no snapshot or render",
                HudTick = "Battle.LastTickMs, the same internal Step timer used by the live HUD; consecutive 600-tick windows (live HUD rolls at every tick)",
                SnapshotCaptureCPU = "Battle.Capture only",
                RenderSubmissionCPU = "Scene/HUD generation, telemetry queries, window title and GL clear/upload/draw submission; no Step, Capture or timer-query bookkeeping",
                TotalCPUFrame = "Render callback entry through post-draw query/error bookkeeping, before sample storage and callback return; includes query polling but excludes auto-present",
                WallFrameInterval = "Render callback entry to next callback entry, including auto-present, event loop and callback bookkeeping; final interval collected without an extra Step",
                GPU = "GL_TIME_ELAPSED around clear through final scene/HUD draw; elapsed GPU timeline, can include idle gaps while CPU generates geometry; not pure device busy utilization",
                Present = "Unavailable: Silk auto-present follows Render callback. No CPU/GPU subtraction is used to infer present or VSync wait",
                Percentiles = "Nearest-rank; milliseconds unless metric name specifies frames per second",
                EntityCounts = "FinalEntities and CSV count captured render rows; docked ships and their mounts are excluded. FinalAliveEntities includes docked entities",
                Excluded = "Initial fast-forward, warmup, GL.Finish query-tail drain, framebuffer readback, checkpoint/hash calculation, aggregation and file writing"
            },
            PresentCpuMs = (double?)null, PresentWaitMs = (double?)null,
            SimulationCpuMs = Statistics(samples.Select(s => s.SimulationCpuMs)),
            HudTickMs = Statistics(hudSamples),
            Stages = Battle<SecsStore>.StageNames.Select((name, stage) => new { Name = name, Timing = Statistics(stageSamples[stage]) }).ToArray(),
            HudWindows = Enumerable.Range(0, (samples.Length + SimulationStatistics.WindowCapacity - 1) / SimulationStatistics.WindowCapacity)
                .Select(window =>
                {
                    var begin = window * SimulationStatistics.WindowCapacity;
                    var count = Math.Min(SimulationStatistics.WindowCapacity, samples.Length - begin);
                    return new { StartTick = samples[begin].Tick, EndTick = samples[begin + count - 1].Tick,
                        Timing = Statistics(hudSamples.Skip(begin).Take(count)),
                        Stages = Battle<SecsStore>.StageNames.Select((name, stage) => new { Name = name, Timing = Statistics(stageSamples[stage].Skip(begin).Take(count)) }).ToArray() };
                }).ToArray(),
            SnapshotCaptureCpuMs = Statistics(samples.Select(s => s.SnapshotCaptureCpuMs)),
            RenderSubmissionCpuMs = Statistics(samples.Select(s => s.RenderSubmissionCpuMs)),
            TotalCpuFrameMs = Statistics(samples.Select(s => s.TotalCpuFrameMs)),
            WallFrameIntervalMs = Statistics(samples.Select(s => s.WallFrameIntervalMs)),
            ReplayWallFramesPerSecond = Statistics(samples.Where(s => s.WallFrameIntervalMs > 0).Select(s => 1000 / s.WallFrameIntervalMs)),
            ReplayWallThroughputFramesPerSecond = wallMs > 0 ? samples.Length * 1000 / wallMs : (double?)null,
            MeasuredWallDurationMs = wallMs,
            Budget = new { ThresholdMs = FrameBudgetMs, CpuFrameMisses = samples.Count(s => s.TotalCpuFrameMs > FrameBudgetMs), WallIntervalMisses = samples.Count(s => s.WallFrameIntervalMs > FrameBudgetMs), WallIntervalMissPercent = samples.Count(s => s.WallFrameIntervalMs > FrameBudgetMs) * 100.0 / samples.Length },
            Gpu = new { Supported = GpuSupported, UnavailableReason = GpuUnavailableReason, CounterBits, QueryRingSize, QueryRingExhaustedFrames = exhaustedQueries, Samples = gpu.Length, NullCount = nullGpu, ZeroSamples = gpu.Count(v => v == 0), RenderMs = Statistics(gpu) },
            GlErrors = Enumerable.Range(0, errorCount).Select(i => new { Frame = errorFrames[i], Code = errors[i] }).ToArray(),
            GlErrorOverflow = errorOverflow, GpuDataValid = gpuDataValid, ValidationPass = errorCount == 0 && errorOverflow == 0 && gpuDataValid,
            FinalEntities = new { samples[^1].Ships, samples[^1].Projectiles, samples[^1].Debris, samples[^1].Particles, samples[^1].Turrets },
            FinalAliveEntities = new { Ships = battle.ShipCount, Projectiles = battle.ProjectileCount, Debris = battle.DebrisCount,
                Particles = battle.ParticleCount, Turrets = battle.TurretCount,
                Total = battle.ShipCount + battle.ProjectileCount + battle.DebrisCount + battle.ParticleCount + battle.TurretCount },
            RawCsvPath = csv is null ? null : Path.GetFileName(csv)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (csv is not null) WriteCsv();
        Console.WriteLine($"Profile complete: {samples.Length} frames; GPU samples={gpu.Length}, null={nullGpu}; GL errors={errorCount + errorOverflow}; {output}");
        if (errorCount != 0 || errorOverflow != 0) throw new InvalidOperationException("Frontend profile reported GL errors; see the saved JSON.");
        if (!gpuDataValid) throw new InvalidOperationException("Timer queries are supported but produced no positive GPU sample; see the saved JSON.");
    }

    private void WriteCsv()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(csv!)!);
        using var writer = new StreamWriter(csv!);
        writer.WriteLine("frame,tick,simulation_cpu_ms,snapshot_capture_cpu_ms,render_submission_cpu_ms,total_cpu_frame_ms,wall_frame_interval_ms,replay_wall_fps,wall_16_67ms_miss,cpu_16_67ms_miss,gpu_render_ms,present_cpu_ms,present_wait_ms,ships,projectiles,debris,particles,turrets,hud_ms," + string.Join(',', Battle<SecsStore>.StageNames));
        for (int i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            string gpu = s.GpuRenderMs?.ToString("R", CultureInfo.InvariantCulture) ?? "";
            string fps = s.WallFrameIntervalMs > 0 ? (1000 / s.WallFrameIntervalMs).ToString("R", CultureInfo.InvariantCulture) : "";
            writer.WriteLine(FormattableString.Invariant($"{i},{s.Tick},{s.SimulationCpuMs:R},{s.SnapshotCaptureCpuMs:R},{s.RenderSubmissionCpuMs:R},{s.TotalCpuFrameMs:R},{s.WallFrameIntervalMs:R},{fps},{(s.WallFrameIntervalMs > FrameBudgetMs ? 1 : 0)},{(s.TotalCpuFrameMs > FrameBudgetMs ? 1 : 0)},{gpu},,,{s.Ships},{s.Projectiles},{s.Debris},{s.Particles},{s.Turrets},{hudSamples[i]:R},") + string.Join(',', stageSamples.Select(stage => stage[i].ToString("R", CultureInfo.InvariantCulture))));
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EndGpu();
        foreach (uint query in queries) if (query != 0) gl.DeleteQuery(query);
    }
}
