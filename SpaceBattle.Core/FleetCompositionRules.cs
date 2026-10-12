namespace SpaceBattle;

public enum FleetComposition { Uniform, LightSwarm }

public static class FleetCompositionRules
{
    public const string Revision = "light-swarm-v1";
    public const int CycleLength = 48;
    private static readonly ShipClass[][] swarmCycles =
    [
        BuildCycle([27, 9, 3, 3, 3, 1, 1, 1]),
        BuildCycle([24, 9, 6, 3, 3, 1, 1, 1]),
        BuildCycle([30, 9, 3, 2, 2, 0, 1, 1]),
        BuildCycle([26, 10, 2, 4, 3, 1, 1, 1])
    ];

    public static ShipClass Escort(FleetComposition composition, int ordinal, int waveComposition = 0)
    {
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (waveComposition is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(waveComposition));
        return composition switch
        {
            FleetComposition.Uniform => (ShipClass)(ordinal % 8),
            FleetComposition.LightSwarm => swarmCycles[waveComposition][ordinal % CycleLength],
            _ => throw new ArgumentOutOfRangeException(nameof(composition))
        };
    }

    private static ShipClass[] BuildCycle(ReadOnlySpan<int> weights)
    {
        var cycle = new ShipClass[CycleLength];
        Span<int> credits = stackalloc int[8];
        credits.Clear();
        // Smooth weighted round-robin distributes rare escorts between light ships.
        for (var i = 0; i < cycle.Length; i++)
        {
            var chosen = 0;
            for (var kind = 0; kind < weights.Length; kind++)
            {
                credits[kind] += weights[kind];
                if (credits[kind] > credits[chosen]) chosen = kind;
            }
            cycle[i] = (ShipClass)chosen;
            credits[chosen] -= CycleLength;
        }
        return cycle;
    }
}
