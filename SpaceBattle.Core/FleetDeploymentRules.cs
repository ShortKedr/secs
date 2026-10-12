using System.Numerics;

namespace SpaceBattle;

public enum FleetDeployment { LegacyRandom, Tactical }

public sealed record FleetDeploymentPlan(Vector2[] Carriers, Vector2[] Escorts);

public static class FleetDeploymentRules
{
    public const string Revision = "dispersed-front-v2";
    public const float SlotSpacing = 12, CarrierClearance = 4, EscortClearance = 2.4f;
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

        // Placement has its own stream: it never consumes the battle's combat/spawn RNG.
        var placement = new RandomStream(config.Seed ^ 0xD1B54A35u);
        var carriers = new Vector2[config.CarriersPerTeam];
        for (var i = 0; i < carriers.Length; i++)
        {
            var radius = ShipCatalog.Radius(ShipClass.LightCarrier + i);
            carriers[i] = new(Math.Clamp(half * 0.84f + placement.Range(-half * 0.04f, half * 0.04f),
                    front, half - radius - CarrierClearance),
                (i - (carriers.Length - 1) * 0.5f) * half * 0.45f +
                    placement.Range(-half * 0.04f, half * 0.04f));
        }

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
        var radii = new float[escorts.Length];
        var lanes = Math.Min(7, (int)MathF.Floor(half * 0.72f / (SlotSpacing * 3)) * 2 + 1);
        var laneOffsets = new float[lanes];
        for (var i = 0; i < lanes; i++) laneOffsets[i] = placement.Range(-8, 8);
        var light = 0;
        var heavy = 0;
        var packetCenter = Vector2.Zero;
        var packetAngle = 0f;
        for (var i = 0; i < escorts.Length; i++)
        {
            var kind = FleetCompositionRules.Escort(config.FleetComposition, i);
            radii[i] = ShipCatalog.Radius(kind);
            if (kind <= ShipClass.Gunship)
            {
                var packet = light / 4;
                var slot = light++ % 4;
                var lane = packet % lanes;
                var depth = packet / lanes;
                if (slot == 0)
                {
                    packetCenter = new(front + 8 + depth * 26 + placement.Range(-8, 10),
                        Lane(lane) + laneOffsets[lane] + placement.Range(-9, 9));
                    packetAngle = placement.Range(-0.65f, 0.65f);
                }
                var local = new Vector2((slot % 2 == 0 ? -6 : 6) + placement.Range(-4, 4),
                    (slot < 2 ? -6 : 6) + placement.Range(-4, 4));
                var c = MathF.Cos(packetAngle); var s = MathF.Sin(packetAngle);
                preferred[i] = packetCenter + new Vector2(local.X * c - local.Y * s, local.X * s + local.Y * c);
            }
            else
            {
                var lane = heavy % lanes;
                var depth = heavy++ / lanes;
                preferred[i] = new(half * (ShipCatalog.IsWeaponsPlatform(kind) ? 0.73f : 0.62f) + depth * 12 +
                    placement.Range(-9, 9), Lane(lane) + laneOffsets[lane] +
                    (kind == ShipClass.Support ? 12 : 0) + placement.Range(-10, 10));
            }
        }

        // The lattice is a safe fallback, not the visible formation. Reserve the rear screen
        // first, then light packets; neither changes actual AI squad membership.
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

        // Move off the lattice only when the complete fleet still has hull clearance.
        // Bounded rejection keeps crowded/small arenas valid without shrinking colliders.
        for (var i = 0; i < escorts.Length; i++)
        {
            var anchor = escorts[i];
            var bias = Vector2.Clamp(preferred[i] - anchor, new(-3), new(3));
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var scale = attempt < 6 ? 1f : 0.45f;
                var candidate = anchor + (bias + new Vector2(placement.Range(-7, 7), placement.Range(-7, 7))) * scale;
                candidate.X = Math.Clamp(candidate.X, front, back);
                candidate.Y = Math.Clamp(candidate.Y, -half * 0.76f, half * 0.76f);
                if (!Clear(candidate, radii[i], i)) continue;
                escorts[i] = candidate;
                break;
            }
        }
        return new(carriers, escorts);

        bool Clear(Vector2 position, float radius, int own)
        {
            for (var i = 0; i < carriers.Length; i++)
                if (!Separated(position, carriers[i], radius +
                    ShipCatalog.Radius(ShipClass.LightCarrier + i) + CarrierClearance)) return false;
            for (var i = 0; i < escorts.Length; i++)
                if (i != own && !Separated(position, escorts[i], radius + radii[i] + EscortClearance)) return false;
            return true;
        }
        static bool Separated(Vector2 a, Vector2 b, float clearance)
        {
            var dx = (double)a.X - b.X; var dy = (double)a.Y - b.Y;
            return dx * dx + dy * dy >= (double)clearance * clearance;
        }
        Vector2 Slot(int cell) => new(front + cell % width * SlotSpacing,
            (cell / width - halfRows) * SlotSpacing);
        float Lane(int lane) => (lane % 2 == 0 ? lane / 2 : -(lane + 1) / 2) * SlotSpacing * 3;
        static ArgumentException TooDense() => new(
            "The tactical deployment cannot fit this fleet with safe clearance. Increase ArenaHalfSize, " +
            "reduce ShipsPerTeam, or use LegacyRandom for a deliberately dense benchmark scene.", nameof(config));
    }
}
