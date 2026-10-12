namespace SpaceBattle;

public readonly record struct DamageProfile(float ShieldMultiplier, float HullMultiplier, float ShieldHeatPerDamage = 0);
public readonly record struct LayerDamage(float Shield, float Hull, float RemainingBase);

public sealed record DamageSettings
{
    public DamageProfile Plasma { get; init; } = new(1.5f, 1, DamageRules.PlasmaShieldHeat);
    public DamageProfile Kinetic { get; init; } = new(1, 1.5f);
    public DamageProfile Missile { get; init; } = new(1, 1);

    public void Validate()
    {
        Check(Plasma); Check(Kinetic); Check(Missile);
        if (Plasma.ShieldHeatPerDamage != DamageRules.PlasmaShieldHeat ||
            Kinetic.ShieldHeatPerDamage != 0 || Missile.ShieldHeatPerDamage != 0)
            throw new ArgumentOutOfRangeException(nameof(DamageSettings), "Only plasma transfers shield damage to heat, at the existing rate.");
        static void Check(DamageProfile profile)
        {
            if (!float.IsFinite(profile.ShieldMultiplier) || profile.ShieldMultiplier <= 0 ||
                !float.IsFinite(profile.HullMultiplier) || profile.HullMultiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(DamageSettings), "Layer multipliers must be finite and positive.");
        }
    }
}

public static class DamageRules
{
    public const string Revision = "layered-weapon-damage-v1";
    public const float PlasmaShieldHeat = 1.8f;
    public static readonly DamageProfile Neutral = new(1, 1);

    public static DamageProfile ForWeapon(WeaponKind kind, DamageSettings settings) => kind switch
    {
        WeaponKind.Plasma => settings.Plasma,
        WeaponKind.MachineGun or WeaponKind.Cannon or WeaponKind.HeavyCannon => settings.Kinetic,
        WeaponKind.Missile => settings.Missile,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static LayerDamage Resolve(float baseDamage, float shield, float armor, in DamageProfile profile)
    {
        var spent = MathF.Min(baseDamage, shield / profile.ShieldMultiplier);
        var removed = MathF.Min(shield, spent * profile.ShieldMultiplier);
        var remaining = baseDamage - spent;
        return new(removed, remaining * profile.HullMultiplier * (1 - armor), remaining);
    }

    // Continuous two-layer DPS estimate, not a discrete volley simulation.
    public static float EstimateKillSeconds(float shield, float hull, float armor, float shieldDps, float hullDps)
    {
        var shieldTime = shield > 0 ? shieldDps > 0 ? shield / shieldDps : float.PositiveInfinity : 0;
        var armoredDps = hullDps * (1 - armor);
        var hullTime = hull > 0 ? armoredDps > 0 ? hull / armoredDps : float.PositiveInfinity : 0;
        return shieldTime + hullTime;
    }
}
