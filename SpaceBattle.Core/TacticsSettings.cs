namespace SpaceBattle;

public sealed record TacticsSettings
{
    public int FormationReviewTicks { get; init; } = 120;
    public float MinimumSpeedRatio { get; init; } = 0.45f;
    public float MinimumRangeRatio { get; init; } = 0.35f;
    public float MaximumJoinDelaySeconds { get; init; } = 2.5f;
    public float RegroupImprovement { get; init; } = 0.2f;
    public float MaximumEngageSeconds { get; init; } = 8;
    public float ScoringHorizonSeconds { get; init; } = 6;
    public float MaximumSuppressionBenefit { get; init; } = 0.25f;
    public float ThreatWeight { get; init; } = 0.25f;
    public float IncomingHitConfidence { get; init; } = 0.35f;
    public int LocalTargetCandidates { get; init; } = 16;
    public int GlobalTargetProbes { get; init; } = 8;
    public int FormationTargetCandidates { get; init; } = 6;
    public int MemberCandidates { get; init; } = 16;
    public int MaximumSpatialWorkPerTick { get; init; } = 16384;
    public int MaximumScoreEvaluationsPerTick { get; init; } = 8192;
    public int MaximumFormationEvaluationsPerReview { get; init; } = 2048;
    public int MaximumIntentProjectiles { get; init; } = 4096;

    public void Validate()
    {
        if (FormationReviewTicks is < 1 or > 36000 ||
            !Unit(MinimumSpeedRatio) || !Unit(MinimumRangeRatio) || !Unit(RegroupImprovement) ||
            !Fraction(MaximumSuppressionBenefit) || !Fraction(IncomingHitConfidence) ||
            !Positive(MaximumJoinDelaySeconds) || !Positive(MaximumEngageSeconds) || !Positive(ScoringHorizonSeconds) ||
            !float.IsFinite(ThreatWeight) || ThreatWeight is < 0 or > 2 ||
            LocalTargetCandidates is < 1 or > 64 || GlobalTargetProbes is < 0 or > 64 ||
            FormationTargetCandidates is < 1 or > 16 || MemberCandidates is < 1 or > 64 ||
            MaximumSpatialWorkPerTick is < 1 or > 1048576 || MaximumScoreEvaluationsPerTick is < 1 or > 1048576 ||
            MaximumFormationEvaluationsPerReview is < 1 or > 1048576 || MaximumIntentProjectiles is < 0 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(TacticsSettings));
        static bool Unit(float x) => float.IsFinite(x) && x is > 0 and < 1;
        static bool Fraction(float x) => float.IsFinite(x) && x is >= 0 and < 1;
        static bool Positive(float x) => float.IsFinite(x) && x is > 0 and <= 60;
    }
}
