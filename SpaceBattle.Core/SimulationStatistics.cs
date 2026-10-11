namespace SpaceBattle;

// Frontends record existing Step timings outside the simulation's measured scope.
public sealed class SimulationStatistics
{
    public const int WindowCapacity = 600;
    private readonly double[] samples = new double[WindowCapacity], sorted = new double[WindowCapacity];
    private readonly double[,] stages;
    private readonly double[] stageSums;
    private int cursor;
    private double sum;
    public int SampleCount { get; private set; }
    public double LastMs { get; private set; }
    public double MeanMs => SampleCount == 0 ? 0 : sum / SampleCount;
    public double P95Ms { get; private set; }
    public double P99Ms { get; private set; }
    public double MaximumMs { get; private set; }
    public double[] StageMeanMs { get; }

    public SimulationStatistics(int stageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stageCount);
        stages = new double[WindowCapacity, stageCount];
        stageSums = new double[stageCount];
        StageMeanMs = new double[stageCount];
    }

    public void RecordTick(double milliseconds, ReadOnlySpan<double> stageMilliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0 || stageMilliseconds.Length != stageSums.Length)
            throw new ArgumentException("Invalid tick statistics.");
        foreach (var value in stageMilliseconds)
            if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Invalid stage statistics.");
        sum += milliseconds - samples[cursor];
        samples[cursor] = milliseconds;
        for (int i = 0; i < stageSums.Length; i++)
        {
            stageSums[i] += stageMilliseconds[i] - stages[cursor, i];
            stages[cursor, i] = stageMilliseconds[i];
        }
        cursor = (cursor + 1) % WindowCapacity;
        SampleCount = Math.Min(WindowCapacity, SampleCount + 1);
        LastMs = milliseconds;
    }

    public void Refresh()
    {
        if (SampleCount == 0) return;
        Array.Copy(samples, sorted, SampleCount);
        Array.Sort(sorted, 0, SampleCount);
        P95Ms = sorted[(int)Math.Ceiling(SampleCount * .95) - 1];
        P99Ms = sorted[(int)Math.Ceiling(SampleCount * .99) - 1];
        MaximumMs = sorted[SampleCount - 1];
        for (int i = 0; i < stageSums.Length; i++) StageMeanMs[i] = stageSums[i] / SampleCount;
    }

    public void Reset()
    {
        Array.Clear(samples); Array.Clear(sorted); Array.Clear(stages);
        Array.Clear(stageSums); Array.Clear(StageMeanMs);
        cursor = SampleCount = 0;
        sum = LastMs = P95Ms = P99Ms = MaximumMs = 0;
    }
}
