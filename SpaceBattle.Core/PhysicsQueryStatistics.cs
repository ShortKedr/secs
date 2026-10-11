namespace SpaceBattle;

/// <summary>Work performed since the last physics step; independent of gameplay counters.</summary>
public readonly record struct PhysicsQueryStatistics(long Sweeps, long EmptySweeps, long Rays, long PointQueries);
