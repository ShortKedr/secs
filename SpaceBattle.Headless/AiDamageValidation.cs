using System.Numerics;
using SpaceBattle;

internal static partial class AiLocalityValidation
{
    public static void RunDamage()
    {
        Check(DamageRules.Revision == "layered-weapon-damage-v1", "layer damage revision");
        RunDamage(static () => new SkecsStore()); RunDamage(static () => new LeoEcsLiteStore());
        RunDamage(static () => new LeoEcsClassicStore()); RunDamage(static () => new MorpehStore());
        RunDamage(static () => new StaticEcsStore());
        Console.WriteLine("PASS: v3 layer preferences, heterogeneous readiness, armor, secondary heat, solo catalog mounts and configurable missiles (five adapters).");
    }
    public static void RunDamage<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        LayerPreferenceChecks(factory); HeterogeneousLayerChecks(factory); SecondaryLayerChecks(factory);
        CatalogMountLayerChecks(factory); ArmorLayerChecks(factory); MissileLayerChecks(factory);
    }
    private static BattleConfig LayerConfig(DamageSettings? settings = null) => new()
    {
        ShipsPerTeam = 8, Seed = 61012, ChurnMultiplier = 1, Damage = settings ?? new DamageSettings()
    };
    private static Weapon LayerWeapon(WeaponKind kind, float damage = 6, float heat = 0) => new()
    {
        Kind = kind, Damage = damage, Cooldown = 60, Burst = 1, Range = 100, Speed = 100, HeatPerShot = heat
    };
    private static void LayerHealth<TStore>(Fixture<TStore> f, EntityKey e, float shield, float hull, float armor = 0)
        where TStore : IEcsStore
    {
        f.Battle.Store.Get<Shield>(e) = new Shield { Current = shield, Maximum = MathF.Max(1, shield) };
        f.Battle.Store.Get<Hull>(e) = new Hull { Current = hull, Maximum = hull, Armor = armor };
        // These analytical targets intentionally do not return fire or offer a suppression opportunity.
        f.Battle.Store.Get<Weapon>(e).NextFireTick = int.MaxValue;
    }
    private static void LayerPreferenceChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero);
        var shielded = f.Spawn(1, ShipClass.Fighter, Vector2.Zero); var exposed = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        LayerHealth(f, shielded, 90, 30); LayerHealth(f, exposed, 0, 120);
        f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Plasma);
        f.Prepare(); var a = f.Index(actor); var oracle = f.Oracle();
        Parity(f.Cost(a, null, f.Index(shielded)), 14 + 1f / 60, "ready plasma first volley then shield/hull cadence");
        Parity(f.Cost(a, null, f.Index(exposed)), 19 + 1f / 60, "ready plasma exposed hull");
        CheckSelection(f, oracle, a, null, new Brain { Target = EntityKey.None }, shielded);
        var snapshotCost = f.Cost(a, null, f.Index(shielded));
        f.Battle.Store.Get<Weapon>(actor).Kind = WeaponKind.Cannon;
        Bits(f.Cost(a, null, f.Index(shielded)), snapshotCost, "live primary kind edit preserves snapshot");
        f.Prepare(); a = f.Index(actor);
        Parity(f.Cost(a, null, f.Index(shielded)), 84f / 6 + 30f / 9 + 1f / 60, "kinetic first volley consumes only shield");
        Parity(f.Cost(a, null, f.Index(exposed)), 111f / 9 + 1f / 60, "kinetic first volley and cadence on hull");
        CheckSelection(f, f.Oracle(), a, null, new Brain { Target = EntityKey.None }, exposed);
        foreach (var kind in new[] { WeaponKind.MachineGun, WeaponKind.HeavyCannon })
        {
            f.Battle.Store.Get<Weapon>(actor).Kind = kind; f.Prepare(); a = f.Index(actor);
            CheckSelection(f, f.Oracle(), a, null, new Brain { Target = EntityKey.None }, exposed);
        }
    }
    private static void HeterogeneousLayerChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var plasma = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var kinetic = f.Spawn(0, ShipClass.Fighter, Vector2.Zero);
        var late = f.Spawn(0, ShipClass.Fighter, new(1000, 0)); var screen = f.Spawn(0, ShipClass.Fighter, Vector2.Zero);
        var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        f.Battle.Store.Get<Weapon>(plasma) = LayerWeapon(WeaponKind.Plasma);
        f.Battle.Store.Get<Weapon>(kinetic) = LayerWeapon(WeaponKind.Cannon);
        f.Battle.Store.Get<Weapon>(late) = LayerWeapon(WeaponKind.Plasma, 600); f.Battle.Store.Get<Motion>(late).MaxSpeed = 0.5f;
        f.Battle.Store.Get<Weapon>(screen) = LayerWeapon(WeaponKind.Cannon, 600);
        foreach (var e in new[] { plasma, kinetic, late }) f.Battle.Store.Get<Brain>(e).Mode = BrainMode.Attack;
        f.Battle.Store.Get<Brain>(screen).Mode = BrainMode.Screen; LayerHealth(f, target, 90, 60, 0.5f); f.Prepare();
        var members = new[] { plasma, kinetic, late, screen }.Select(f.Index).ToArray();
        var squad = new Squad { Count = 4, Leader = members[0] };
        for (var s = 0; s < 4; s++) squad.Members[s] = members[s];
        var t = f.Index(target);
        Parity(f.Cost(members[0], squad, t), 13 + 1f / 60, "mixed ready volleys then aggregate layer rates");
        Parity(f.Cost(members[0], squad, t), f.Oracle().Cost(members[0], squad, t), "independent mixed reference");
        Check(f.Cost(members[0], squad, t) != (90f + 60f / 0.5f) / 12, "not unified health/raw DPS");
        CheckSelection(f, f.Oracle(), members[0], squad, new Brain { Target = EntityKey.None }, target);
        f.Battle.Store.Get<Weapon>(kinetic).Kind = WeaponKind.Plasma; f.Prepare();
        Parity(f.Cost(members[0], squad, t), 14 + 1f / 60, "homogeneous plasma roster");
        f.Battle.Store.Get<Weapon>(plasma).Kind = WeaponKind.Cannon;
        f.Battle.Store.Get<Weapon>(kinetic).Kind = WeaponKind.Cannon; f.Prepare();
        Parity(f.Cost(members[0], squad, t), 78f / 12 + 60f / 9 + 1f / 60, "homogeneous kinetic roster");
        f.Battle.Store.Get<Weapon>(kinetic).NextFireTick = int.MaxValue; f.Prepare();
        Parity(f.Cost(members[0], squad, t), f.Cost(members[0], null, t), "unavailable member does not add full DPS");
    }
    private static void SecondaryLayerChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Cannon, 6, 6);
        f.Battle.Store.Set(actor, new SecondaryWeapon { Value = LayerWeapon(WeaponKind.Plasma, 3, 4) });
        f.Battle.Store.Get<Heat>(actor) = new Heat { Limit = 100, CoolingPerSecond = 5 };
        LayerHealth(f, target, 21, 12); f.Prepare(); var a = f.Index(actor); var t = f.Index(target);
        Parity(f.Cost(a, null, t), 2 + 1f / 60, "mixed secondary first volleys and own profile");
        var cold = f.Cost(a, null, t);
        f.Battle.Store.Get<Heat>(actor).Value = 98; f.Prepare();
        var hot = f.Cost(a, null, t);
        Check(hot > cold, "shared heat reserve delays secondary volley and throttles cadence");
        Parity(hot, f.Oracle().Cost(a, null, t), "secondary readiness independent reference");
        Check(f.Field<ActorScoring[]>("actorScoring")[a].HeatScale < 1, "hot main sustainable cadence below full DPS");
        f.Battle.Store.Get<SecondaryWeapon>(actor).Value.Kind = WeaponKind.Cannon;
        Bits(f.Cost(a, null, t), hot, "live secondary edit preserves snapshot");
        f.Prepare();
        Parity(f.Cost(a, null, t), f.Oracle().Cost(a, null, t), "secondary kind refresh");
        f.Battle.Store.Get<Heat>(actor).Value = 100; f.Battle.Store.Get<Heat>(actor).CoolingPerSecond = 0; f.Prepare();
        Check(float.IsPositiveInfinity(f.Cost(a, null, t)), "no cooling and no ready heat budget means unavailable");
    }
    private static void CatalogMountLayerChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        foreach (var kind in new[] { ShipClass.Frigate, ShipClass.Destroyer, ShipClass.LightCarrier, ShipClass.FleetCarrier, ShipClass.FoundryCarrier })
        {
            using var f = new Fixture<TStore>(factory(), LayerConfig());
            var actor = f.Spawn(0, kind, Vector2.Zero); var wing = f.Spawn(0, ShipClass.Fighter, Vector2.Zero);
            var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
            f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Cannon, 6, 100);
            f.Battle.Store.Set(actor, new SecondaryWeapon { Value = LayerWeapon(WeaponKind.Plasma, 3, 100) });
            f.Battle.Store.Get<Heat>(actor) = new Heat { Value = 99, Limit = 100 };
            f.Battle.Store.Get<Weapon>(wing).NextFireTick = int.MaxValue;
            f.Battle.Store.Get<Brain>(actor).Mode = BrainMode.Attack; f.Battle.Store.Get<Brain>(wing).Mode = BrainMode.Attack;
            LayerHealth(f, target, 100, 80, 0.3f); f.Prepare(); var a = f.Index(actor); var t = f.Index(target);
            var solo = f.Cost(a, null, t);
            Parity(solo, f.Oracle().Cost(a, null, t), "catalog count/cannon/plasma layered reference");
            var group = new Squad { Count = 2, Leader = a }; group.Members[0] = a; group.Members[1] = f.Index(wing);
            Check(solo < f.Cost(a, group, t), "mount streams are solo-only, independent of main heat cadence");
            var scoring = f.Field<ActorScoring[]>("actorScoring")[a];
            Check(scoring.HeatScale < 0.01f && scoring.CannonHeatScale == 1 && scoring.PlasmaHeatScale == 1,
                "catalog mount independent heat budgets survive hot main weapon");
        }
        using var ranged = new Fixture<TStore>(factory(), LayerConfig());
        var ship = ranged.Spawn(0, ShipClass.Frigate, Vector2.Zero); var victim = ranged.Spawn(1, ShipClass.Fighter, new(130, 0));
        var weapon = LayerWeapon(WeaponKind.Cannon); weapon.Range = 170; ranged.Battle.Store.Get<Weapon>(ship) = weapon;
        LayerHealth(ranged, victim, 90, 60); ranged.Prepare(); var ai = ranged.Index(ship); var ti = ranged.Index(victim);
        var mobile = ranged.Cost(ai, null, ti);
        Parity(mobile, ranged.Oracle().Cost(ai, null, ti), "off-range mount arrival, not instantaneous full DPS");
        ranged.Battle.Store.Get<Motion>(ship).MaxSpeed = 0.5f; ranged.Prepare();
        Check(ranged.Cost(ai, null, ti) > mobile, "off-range mounts without closing speed are excluded");
        Check(!ranged.Field<ActorScoring[]>("actorScoring")[ai].MountHeatReady, "unreachable mount heat cache remains lazy");
    }
    private static void ArmorLayerChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using var f = new Fixture<TStore>(factory(), LayerConfig());
        var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero); var target = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
        f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Cannon); LayerHealth(f, target, 0, 9, 0.99f);
        f.Prepare(); var a = f.Index(actor); var t = f.Index(target);
        Check(f.Cost(a, null, t) > 90, "actual armor .99, not former .05 floor");
        Parity(f.Cost(a, null, t), f.Oracle().Cost(a, null, t), "armor/ready volley reference");
        f.Battle.Store.Get<Hull>(target).Armor = 1; f.Prepare();
        Check(float.IsPositiveInfinity(f.Cost(a, null, t)), "zero effective hull DPS");
        CheckSelection(f, f.Oracle(), a, null, new Brain { Target = target, TargetLockUntilTick = 120 }, EntityKey.None);
        var hull = f.Rows[t].Hull; hull.Current = 0; var shield = f.Rows[t].Shield; shield.Current = 90;
        f.Rows[t] = f.Rows[t] with { Hull = hull, Shield = shield };
        Parity(f.Cost(a, null, t), 14 + 1f / 60, "absent hull skips zero armored DPS");
        Parity(f.Cost(a, null, t), f.Oracle().Cost(a, null, t), "zero layer reference");
        f.Battle.Store.Get<Hull>(target).Armor = 0; f.Battle.Store.Get<Weapon>(actor).Burst = 0; f.Prepare();
        Check(float.IsPositiveInfinity(f.Cost(a, null, t)), "zero volley means unavailable");
    }
    private static void MissileLayerChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        foreach (var profile in new[] { new DamageProfile(1, 1), new DamageProfile(3, 0.5f), new DamageProfile(0.5f, 3) })
        {
            using var f = new Fixture<TStore>(factory(), LayerConfig(new DamageSettings { Missile = profile }));
            var actor = f.Spawn(0, ShipClass.Fighter, Vector2.Zero);
            var exposed = f.Spawn(1, ShipClass.Fighter, Vector2.Zero); var shielded = f.Spawn(1, ShipClass.Fighter, Vector2.Zero);
            f.Battle.Store.Get<Weapon>(actor) = LayerWeapon(WeaponKind.Missile);
            LayerHealth(f, exposed, 0, 120); LayerHealth(f, shielded, 90, 30); f.Prepare(); var a = f.Index(actor);
            var shieldedCost = 90 / (6 * profile.ShieldMultiplier) + 30 / (6 * profile.HullMultiplier) - 1 + 1f / 60;
            var exposedCost = 120 / (6 * profile.HullMultiplier) - 1 + 1f / 60;
            Parity(f.Cost(a, null, f.Index(shielded)), shieldedCost, "custom missile layers/ready volley");
            Parity(f.Cost(a, null, f.Index(exposed)), exposedCost, "custom missile hull/ready volley");
            CheckSelection(f, f.Oracle(), a, null, new Brain { Target = EntityKey.None }, shieldedCost < exposedCost ? shielded : exposed);
        }
    }
}
