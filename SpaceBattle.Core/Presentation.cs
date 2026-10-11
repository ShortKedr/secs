using System.Numerics;

namespace SpaceBattle;

public readonly record struct RenderEntity(int Serial, int Team, int Kind, Vector2 Position, Vector2 Velocity,
    float Angle, float Radius, float Hull, float Shield, float Heat, Vector4 Color);
public readonly record struct RenderTurret(int Serial, int ParentSerial, int Team, WeaponKind Kind,
    Vector2 Position, float Angle, float Radius, float Heat, float Recoil);

public sealed class BattleFrame
{
    public int Tick { get; set; }
    public float ArenaHalfSize { get; set; }
    public List<RenderEntity> Ships { get; } = new();
    public List<RenderEntity> Projectiles { get; } = new();
    public List<RenderEntity> Debris { get; } = new();
    public List<RenderEntity> Particles { get; } = new();
    public List<RenderTurret> Turrets { get; } = new();
    public BattleCounters Counters { get; set; } = new();
    public double LastTickMs { get; set; }
}

public sealed class BattleCounters
{
    public long ShipsCreated, ShipsDestroyed, Shots, Hits, PlasmaShieldHits, ShieldBreaks,
        Overheats, Dockings, Repairs, Builds, ItemUses, DebrisHits, Explosions, StructuralAdds, StructuralRemoves,
        AimDecisions, ImpreciseAimDecisions, FriendlyFireHits, SelfHits, HeavyShots, TurretShots;
    public BattleCounters Copy() => (BattleCounters)MemberwiseClone();
}

public sealed record BattleConfig
{
    public uint Seed { get; init; } = 7331;
    public int ShipsPerTeam { get; init; } = 96;
    public int CarriersPerTeam { get; init; } = 3;
    public float ArenaHalfSize { get; init; } = 210;
    public float ChurnMultiplier { get; init; } = 1;
    public int TickRate { get; init; } = 60;
    public bool ReplenishCarriers { get; init; } = true;
    public int ParticleMultiplier { get; init; } = 1;
    public int MaxProjectiles { get; init; } = 24000;
    public int MaxDebris { get; init; } = 16000;
    public int MaxParticles { get; init; } = 32000;
    public void Validate()
    {
        if (TickRate != 60 || ShipsPerTeam < 8 || ShipsPerTeam > 10000 || CarriersPerTeam is < 2 or > 3 ||
            ArenaHalfSize < 80 || ChurnMultiplier is < 0.1f or > 8 || ParticleMultiplier is < 0 or > 8 ||
            MaxProjectiles < 1 || MaxDebris < 1 || MaxParticles < 1)
            throw new ArgumentOutOfRangeException(nameof(BattleConfig));
    }
}

public struct RandomStream
{
    private uint state;
    public RandomStream(uint seed) => state = seed == 0 ? 1 : seed;
    public uint Next() { var x = state; x ^= x << 13; x ^= x >> 17; x ^= x << 5; return state = x; }
    public float Unit() => (Next() >> 8) * (1f / 16777216);
    public float Range(float low, float high) => low + Unit() * (high - low);
}
