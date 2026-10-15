using SpaceBattle;

internal static partial class Validation
{
    private static void StatisticsChecks()
    {
        var stats = new SimulationStatistics(2);
        stats.Refresh();
        Require(stats.SampleCount == 0 && stats.MeanMs == 0 && stats.P99Ms == 0, "empty statistics");
        double[] stage = [1, 2];
        stats.RecordTick(3, stage); stats.Refresh();
        Require(stats.SampleCount == 1 && stats.LastMs == 3 && stats.MeanMs == 3 && stats.P95Ms == 3 && stats.MaximumMs == 3, "single tick statistics");
        stats.Reset();
        for (int i = 1; i <= 600; i++) { stage[0] = i; stage[1] = i * 2; stats.RecordTick(i, stage); }
        stats.Refresh();
        Require(stats.SampleCount == 600 && stats.MeanMs == 300.5 && stats.P95Ms == 570 && stats.P99Ms == 594 && stats.MaximumMs == 600, "nearest-rank rolling tick quantiles");
        Require(stats.StageMeanMs[0] == 300.5 && stats.StageMeanMs[1] == 601, "stage means");
        stage[0] = 0; stage[1] = 0;
        for (int i = 0; i < 600; i++) stats.RecordTick(0, stage);
        stats.Refresh();
        Require(stats.SampleCount == 600 && stats.MeanMs == 0 && stats.MaximumMs == 0 && stats.StageMeanMs.All(v => v == 0), "ring evicts old samples including stages");
        stats.Reset(); stats.Refresh();
        Require(stats.SampleCount == 0 && stats.LastMs == 0 && stats.MeanMs == 0 && stats.P95Ms == 0 && stats.P99Ms == 0 && stats.MaximumMs == 0, "reset clears all tick metrics");
        foreach (var bad in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { stats.RecordTick(bad, stage); } catch (ArgumentException) { rejected = true; }
            Require(rejected && stats.SampleCount == 0, "invalid ticks are rejected atomically");
        }
        bool wrongStages = false;
        try { stats.RecordTick(1, [1]); } catch (ArgumentException) { wrongStages = true; }
        Require(wrongStages && stats.SampleCount == 0, "invalid stage count");
        stage[1] = double.NaN;
        bool invalidStage = false;
        try { stats.RecordTick(1, stage); } catch (ArgumentException) { invalidStage = true; }
        Require(invalidStage && stats.SampleCount == 0, "invalid stage does not partially record");
        stage[1] = 0;
        stats.RecordTick(1, stage); stats.Refresh();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { stats.RecordTick(1, stage); stats.Refresh(); }
        Require(GC.GetAllocatedBytesForCurrentThread() == allocationStart, "no rolling statistics sample/refresh allocations");
        using var observed = new Battle<SkecsStore>(new(), new BattleConfig { ShipsPerTeam = 8, Seed = 27 });
        using var plain = new Battle<SkecsStore>(new(), observed.Config);
        var liveStats = new SimulationStatistics(Battle<SkecsStore>.StageNames.Length);
        for (int i = 0; i < 180; i++)
        {
            observed.Step(); liveStats.RecordTick(observed.LastTickMs, observed.LastStageMs); plain.Step();
        }
        liveStats.Refresh();
        Require(observed.Checksum() == plain.Checksum(), "telemetry does not change simulation state");
        Require(observed.Store.Diagnostics().Entities == observed.ShipCount + observed.TurretCount + observed.ProjectileCount + observed.DebrisCount + observed.ParticleCount, "all live ECS categories counted");
        Require(liveStats.SampleCount == 180 && liveStats.StageMeanMs.Length == 9 && liveStats.MeanMs > 0, "live stage/tick timings");
        Console.WriteLine("PASS: rolling statistics, quantiles, eviction, zero/reset/invalid samples, allocations, live counts and checksum neutrality.");
    }
}
