namespace SpaceBattle;

public sealed record PhysicsSettings
{
    public const string Revision = "circle-kernel-v1";
    public float Restitution { get; init; } = 0.1f;
    public float Friction { get; init; } = 0.08f;
    public float RestitutionThreshold { get; init; } = 10;
    public int VelocityIterations { get; init; } = 8;
    public int PositionIterations { get; init; } = 8;
    public int MaximumSubsteps { get; init; } = 8;
    public int MaximumContactPairs { get; init; } = 32768;
    public float PositionSlop { get; init; } = 0.01f;
    public float CorrectionFraction { get; init; } = 0.8f;
    public float MaximumCorrection { get; init; } = 0.5f;
    public bool CollectTimings { get; init; }

    public void Validate()
    {
        if (!float.IsFinite(Restitution) || Restitution is < 0 or > 1 ||
            !float.IsFinite(Friction) || Friction is < 0 or > 2 ||
            !float.IsFinite(RestitutionThreshold) || RestitutionThreshold < 0 ||
            VelocityIterations is < 1 or > 32 || PositionIterations is < 1 or > 32 || MaximumSubsteps is < 1 or > 8 ||
            MaximumContactPairs is < 1 or > 1048576 ||
            !float.IsFinite(PositionSlop) || PositionSlop < 0 ||
            !float.IsFinite(CorrectionFraction) || CorrectionFraction is <= 0 or > 1 ||
            !float.IsFinite(MaximumCorrection) || MaximumCorrection <= 0)
            throw new ArgumentOutOfRangeException(nameof(PhysicsSettings));
    }
}

public readonly record struct PhysicsTimingStatistics(double ContactCandidatesMs, double SolverMs,
    double HitCandidatesMs, double HitNarrowMs);
