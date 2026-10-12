namespace SpaceBattle;

// Reset at Step; subsequent hit/spawn queries accumulate until the next Step.
public record struct PhysicsWorkStatistics
{
    public long ContactBuilds { get; internal set; }
    public long ContactCandidates { get; internal set; }
    public long ContactTests { get; internal set; }
    public long ContactPairs { get; internal set; }
    public long NewConstraints { get; internal set; }
    public long CandidateCacheBuilds { get; internal set; }
    public long ReusedContactBuilds { get; internal set; }
    public long CandidatePairsVisited { get; internal set; }
    public long CandidateFallbacks { get; internal set; }
    public long Sorts { get; internal set; }
    public long Substeps { get; internal set; }
    public long PositionIterations { get; internal set; }
    public long VelocityPasses { get; internal set; }
    public long RepeatedVelocityPasses { get; internal set; }
    public long VelocityConstraintVisits { get; internal set; }
    public long PositionConstraintVisits { get; internal set; }
    public long CcdCandidates { get; internal set; }
    public long CcdNarrowTests { get; internal set; }
    public long PrunedCcdIntervals { get; internal set; }
    public double ContactQueryMs { get; internal set; }
    public double ContactNarrowMs { get; internal set; }
}

public record struct PhysicsGridStatistics
{
    public long Rebuilds { get; internal set; }
    public long Clears { get; internal set; }
    public long CellsCleared { get; internal set; }
    public long BoundsInserted { get; internal set; }
    public long MembershipsInserted { get; internal set; }
    public long MembershipsVisited { get; internal set; }
    public long UniqueCandidates { get; internal set; }
    public long Sorts { get; internal set; }
    public double ClearMs { get; internal set; }
    public double InsertMs { get; internal set; }
    public double SealMs { get; internal set; }
}

public readonly record struct PhysicsDiagnosticStatistics(PhysicsWorkStatistics Work, PhysicsGridStatistics Grid);
