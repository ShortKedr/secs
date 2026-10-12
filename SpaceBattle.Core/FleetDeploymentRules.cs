using System.Numerics;

namespace SpaceBattle;

public enum FleetDeployment { LegacyRandom, Tactical }

public sealed record FleetDeploymentPlan(Vector2[] Carriers, Vector2[] Escorts);

public static class FleetDeploymentRules
{
    public const string Revision = "dispersed-front-v1";
    public const float SlotSpacing = 12, CarrierClearance = 4;
    public const int MaximumSlots = 4096;

    // Startup-only plan, in the positive-X half. The other fleet reflects X, not birth order.
    public static FleetDeploymentPlan Create(BattleConfig config)
    {
        config.Validate();
        var half = MathF.Min(config.ArenaHalfSize, 600);
        var front = MathF.Max(16, half * 0.28f);
        var back = half - 18;
        var halfRows = (int)MathF.Floor(half * 0.72f / SlotSpacing);
        var width = (int)MathF.Floor((back - front) / SlotSpacing) + 1;
        var height = halfRows * 2 + 1;
        var cells = checked(width * height);
        if (cells > MaximumSlots || config.ShipsPerTeam > cells)
            throw TooDense();

        var carriers = new Vector2[config.CarriersPerTeam];
        for (var i = 0; i < carriers.Length; i++)
            carriers[i] = new(MathF.Min(half * 0.84f, half - 16),
                (i - (carriers.Length - 1) * 0.5f) * half * 0.45f);

        var occupied = new bool[cells];
        var available = 0;
        for (var cell = 0; cell < cells; cell++)
        {
            var position = Slot(cell);
            for (var i = 0; i < carriers.Length; i++)
            {
                var clearance = ShipCatalog.Radius(ShipClass.LightCarrier + i) +
                    ShipCatalog.Radius(ShipClass.Destroyer) + CarrierClearance;
                if (Vector2.DistanceSquared(position, carriers[i]) < clearance * clearance)
                    occupied[cell] = true;
            }
            if (!occupied[cell]) available++;
        }
        if (config.ShipsPerTeam > available) throw TooDense();

        var escorts = new Vector2[config.ShipsPerTeam];
        var preferred = new Vector2[escorts.Length];
        var lanes = Math.Min(7, (int)MathF.Floor(half * 0.72f / (SlotSpacing * 3)) * 2 + 1);
        var light = 0;
        var heavy = 0;
        for (var i = 0; i < escorts.Length; i++)
        {
            var kind = FleetCompositionRules.Escort(config.FleetComposition, i);
            if (kind <= ShipClass.Gunship)
            {
                var packet = light / 4;
                var slot = light++ % 4;
                var lane = packet % lanes;
                var depth = packet / lanes;
                preferred[i] = new(front + 6 + depth * 24 + (slot % 2 == 0 ? -6 : 6),
                    Lane(lane) + (slot < 2 ? -6 : 6));
            }
            else
            {
                var lane = heavy % lanes;
                var depth = heavy++ / lanes;
                preferred[i] = new(half * (ShipCatalog.IsWeaponsPlatform(kind) ? 0.73f : 0.62f) + depth * 12,
                    Lane(lane) + (kind == ShipClass.Support ? 12 : 0));
            }
        }

        // Reserve the rear screen first, then the four-ship light packets. Actual AI groups
        // still form normally; these are deployment slots, not new squad membership rules.
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < escorts.Length; i++)
            {
                var kind = FleetCompositionRules.Escort(config.FleetComposition, i);
                if ((kind > ShipClass.Gunship) != (pass == 0)) continue;
                var best = -1;
                var cost = float.PositiveInfinity;
                for (var cell = 0; cell < cells; cell++)
                {
                    if (occupied[cell]) continue;
                    var delta = Slot(cell) - preferred[i];
                    var candidateCost = delta.X * delta.X * 1.5f + delta.Y * delta.Y;
                    if (candidateCost < cost) { best = cell; cost = candidateCost; }
                }
                occupied[best] = true;
                escorts[i] = Slot(best);
            }
        return new(carriers, escorts);

        Vector2 Slot(int cell) => new(front + cell % width * SlotSpacing,
            (cell / width - halfRows) * SlotSpacing);
        float Lane(int lane) => (lane % 2 == 0 ? lane / 2 : -(lane + 1) / 2) * SlotSpacing * 3;
        static ArgumentException TooDense() => new(
            "The tactical deployment cannot fit this fleet with safe clearance. Increase ArenaHalfSize, " +
            "reduce ShipsPerTeam, or use LegacyRandom for a deliberately dense benchmark scene.", nameof(config));
    }
}
