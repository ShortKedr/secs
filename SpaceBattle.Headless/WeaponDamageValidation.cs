using System.Collections;
using System.Numerics;
using System.Reflection;
using SpaceBattle;

internal static class WeaponDamageValidation
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly WeaponKind[] Kinds = Enum.GetValues<WeaponKind>();
    private static readonly DamageSettings Custom = new()
    {
        Plasma = new(2, 0.5f, DamageRules.PlasmaShieldHeat), Kinetic = new(0.75f, 2), Missile = new(2, 0.5f)
    };

    public static void Run()
    {
        PureChecks();
        foreach (var settings in new[] { new DamageSettings(), Custom })
        {
            var expected = Replay(static () => new SkecsStore(), settings);
            CheckReplay(expected, Replay(static () => new SkecsStore(), settings), "SKECS repeat");
            CheckPort(static () => new LeoEcsLiteStore());
            CheckPort(static () => new LeoEcsClassicStore());
            CheckPort(static () => new MorpehStore());
            CheckPort(static () => new StaticEcsStore());
            void CheckPort<T>(Func<T> factory) where T : IEcsStore
            {
                var first = Replay(factory, settings);
                CheckReplay(expected, first, typeof(T).Name + " parity");
                CheckReplay(first, Replay(factory, settings), typeof(T).Name + " repeat");
            }
        }
        RunAllocationChecks();
        StudyChecks();
        Console.WriteLine("PASS: layered weapon rules, actual shield heat, legacy neutral/plasma semantics, all-store repeats, projectile/turret kind routing and warmed direct-damage allocations.");
    }

    private static void PureChecks()
    {
        Check(DamageRules.Revision == "layered-weapon-damage-v1", "damage revision");
        var settings = new DamageSettings(); settings.Validate(); Custom.Validate();
        Check(settings.Plasma == new DamageProfile(1.5f, 1, 1.8f) && settings.Kinetic == new DamageProfile(1, 1.5f) &&
            settings.Missile == new DamageProfile(1, 1), "default profiles");
        foreach (var kind in Kinds)
        {
            var profile = DamageRules.ForWeapon(kind, settings);
            var expected = kind == WeaponKind.Plasma ? settings.Plasma : kind == WeaponKind.Missile ? settings.Missile : settings.Kinetic;
            Check(profile == expected, "weapon profile mapping " + kind);
            Layers(0, 75, 0.25f, profile, new(0, 0, 0), "zero base damage " + kind);
            Layers(100, 1000, 0.25f, profile, new(100 * profile.ShieldMultiplier, 0, 0), "full shield " + kind);
        }
        Layers(100, 75, 0, settings.Plasma, new(75, 50, 50), "100/75 plasma spillover");
        Layers(100, 75, 0.25f, settings.Plasma, new(75, 37.5f, 50), "plasma hull armor after spillover");
        Layers(100, 150, 0, settings.Plasma, new(150, 0, 0), "exact plasma shield depletion");
        Layers(100, 0, 0.25f, settings.Plasma, new(0, 75, 100), "plasma zero shield");
        Same(75 * settings.Plasma.ShieldHeatPerDamage, 135, "plasma heat from actual 75 shield damage");
        foreach (var kind in new[] { WeaponKind.MachineGun, WeaponKind.Cannon, WeaponKind.HeavyCannon })
        {
            var profile = DamageRules.ForWeapon(kind, settings);
            Layers(100, 75, 0.25f, profile, new(75, 28.125f, 25), "partial kinetic shield " + kind);
            Layers(100, 0, 0.25f, profile, new(0, 112.5f, 100), "unshielded kinetic " + kind);
            Layers(100, 75, 1, profile, new(75, 0, 25), "armor one suppresses hull only " + kind);
        }
        Layers(100, 75, 0.25f, settings.Missile, new(75, 18.75f, 25), "default missile neutral");
        Layers(100, 75, 0.25f, Custom.Missile, new(75, 23.4375f, 62.5f), "configured missile spillover");
        Layers(100, 0, 0.25f, Custom.Missile, new(0, 37.5f, 100), "configured missile zero shield");
        Layers(100, 75, 0.25f, DamageRules.Neutral, new(75, 18.75f, 25), "neutral damage unaffected by weapon settings");
        Expect<ArgumentOutOfRangeException>(() => DamageRules.ForWeapon((WeaponKind)int.MaxValue, settings));
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Expect<ArgumentOutOfRangeException>(() => new DamageSettings { Missile = new(invalid, 1) }.Validate());
            Expect<ArgumentOutOfRangeException>(() => new DamageSettings { Kinetic = new(1, invalid) }.Validate());
        }
        // Division followed by multiplication can overshoot S by one ULP. Verify the clamp,
        // while keeping RemainingBase based on spent base damage rather than rounded removal.
        foreach (var multiplier in new[] { 1.1f, 1.5f, 1.7f, 2.3f })
        for (var i = 0; i < 1024; i++)
        {
            var shield = BitConverter.Int32BitsToSingle(0x3E800001 + i * 7919);
            var profile = new DamageProfile(multiplier, 1);
            var result = DamageRules.Resolve(100, shield, 0, profile);
            Check(result.Shield >= 0 && result.Shield <= shield, "rounded shield damage never exceeds shield");
            Same(result.RemainingBase, 100 - MathF.Min(100, shield / multiplier), "remaining base keeps spent formula");
        }
    }

    private static void Layers(float damage, float shield, float armor, DamageProfile profile, LayerDamage expected, string label)
    {
        var actual = DamageRules.Resolve(damage, shield, armor, profile);
        Same(actual.Shield, expected.Shield, label + " shield"); Same(actual.Hull, expected.Hull, label + " hull");
        Same(actual.RemainingBase, expected.RemainingBase, label + " remaining base");
    }

    private readonly record struct Snapshot(string Label, int ShieldBits, int HullBits, int HeatBits,
        int LastHitTick, int LastDamageTick, EntityKey Attacker, long Hits, long PlasmaShieldHits, ulong Checksum);

    private static Snapshot[] Replay<T>(Func<T> factory, DamageSettings settings) where T : IEcsStore
    {
        var trace = new List<Snapshot>();
        DirectChecks(factory, settings, trace);
        ProjectileChecks(factory, settings, trace);
        TurretCheck(factory, settings, WeaponKind.Plasma, WeaponKind.HeavyCannon, trace);
        TurretCheck(factory, settings, WeaponKind.Cannon, WeaponKind.Plasma, trace);
        return trace.ToArray();
    }

    private static void DirectChecks<T>(Func<T> factory, DamageSettings settings, List<Snapshot> trace) where T : IEcsStore
    {
        using var battle = Fixture(factory, settings);
        var enemy = Actor(battle, 0, ShipClass.Fighter, new(-60, 0));
        var victim = Actor(battle, 1, ShipClass.Fighter, Vector2.Zero);
        Call(battle, "Collect");
        foreach (var kind in Kinds)
        foreach (var shield in new[] { 1000f, 75f, 0f })
        {
            ResetTarget(battle, victim, shield);
            var profile = DamageRules.ForWeapon(kind, settings);
            var expected = DamageRules.Resolve(100, shield, 0.25f, profile);
            var before = battle.Counters.PlasmaShieldHits;
            battle.DamageWeapon(victim, 100, kind, enemy);
            AssertDamage(battle, victim, shield, expected, profile, before, enemy);
            trace.Add(Read(battle, victim, "direct-" + kind + "-" + shield));
            States(battle).Clear();
        }
        foreach (var fragment in new[] { false, true })
        {
            ResetTarget(battle, victim, 75);
            var before = battle.Counters.PlasmaShieldHits;
            battle.Damage(victim, 100, plasma: false, fragment: fragment);
            AssertDamage(battle, victim, 75, new(75, 18.75f, 25), DamageRules.Neutral, before, EntityKey.None);
            trace.Add(Read(battle, victim, "legacy-neutral-" + fragment)); States(battle).Clear();
        }
        foreach (var shield in new[] { 75f, 0f })
        {
            ResetTarget(battle, victim, shield);
            var before = battle.Counters.PlasmaShieldHits;
            battle.Damage(victim, 100, plasma: true, fragment: false);
            AssertDamage(battle, victim, shield, DamageRules.Resolve(100, shield, 0.25f, settings.Plasma),
                settings.Plasma, before, EntityKey.None);
            trace.Add(Read(battle, victim, "legacy-plasma-" + shield)); States(battle).Clear();
        }
        ResetTarget(battle, victim, 75);
        battle.DamageWeapon(victim, 100, WeaponKind.Plasma);
        Check(battle.Store.Get<CombatState>(victim).LastAttacker == EntityKey.None, "three-argument weapon overload uses None");
        States(battle).Clear();
        ResetTarget(battle, victim, 0);
        battle.Store.Get<Hull>(victim).Current = 10;
        battle.DamageWeapon(victim, 100, WeaponKind.Cannon, enemy);
        Same(battle.Store.Get<Hull>(victim).Current, 0, "lethal hull damage clamps to zero");
        Check(States(battle).Count == 3, "lethal unshielded hit queues combat, shield break and destroy");
        Call(battle, "FlushLifecycle"); battle.Store.Commit();
        Check(!battle.Store.Alive(victim), "lethal weapon damage reaches lifecycle destruction");
        var plasmaHits = battle.Counters.PlasmaShieldHits;
        battle.DamageWeapon(victim, 100, WeaponKind.Plasma);
        Check(battle.Counters.PlasmaShieldHits == plasmaHits && States(battle).Count == 0, "stale victim remains a no-op");
    }

    private static void AssertDamage<T>(Battle<T> battle, EntityKey e, float shield, LayerDamage damage,
        DamageProfile profile, long plasmaBefore, EntityKey attacker) where T : IEcsStore
    {
        Same(battle.Store.Get<Shield>(e).Current, shield - damage.Shield, "integrated shield removal");
        Same(battle.Store.Get<Hull>(e).Current, MathF.Max(0, 10000 - damage.Hull), "integrated hull removal");
        Same(battle.Store.Get<Heat>(e).Value, 7 + damage.Shield * profile.ShieldHeatPerDamage, "heat from actual shield damage only");
        Check(battle.Counters.PlasmaShieldHits == plasmaBefore + (profile.ShieldHeatPerDamage > 0 && damage.Shield > 0 ? 1 : 0),
            "plasma shield counter excludes unshielded and nonplasma hits");
        var combat = battle.Store.Get<CombatState>(e);
        Check(combat.LastDamageTick == battle.Tick && combat.LastAttacker == attacker &&
            battle.Store.Get<Shield>(e).LastHitTick == battle.Tick, "damage tick and hostile attribution");
    }

    private static void ProjectileChecks<T>(Func<T> factory, DamageSettings settings, List<Snapshot> trace) where T : IEcsStore
    {
        using var battle = Fixture(factory, settings);
        var owner = Actor(battle, 0, ShipClass.Fighter, new(-60, 0));
        var victim = Actor(battle, 1, ShipClass.Fighter, Vector2.Zero);
        FillDebrisBudget(battle);
        var serial = 1000000;
        foreach (var kind in Kinds)
        foreach (var shield in new[] { 1000f, 75f, 0f })
        {
            ResetTarget(battle, victim, shield);
            var e = battle.Store.Create();
            battle.Store.Set(e, new Transform { Position = new(-20, 0), Previous = new(-20, 0) });
            battle.Store.Set(e, new Motion { Velocity = new(2400, 0) });
            var rectangle = kind is WeaponKind.MachineGun or WeaponKind.Cannon;
            battle.Store.Set(e, new Projectile { Serial = serial++, Owner = owner, Target = EntityKey.None, Kind = kind,
                Damage = 100, Radius = 0.12f, HalfLength = rectangle ? 0.24f : 0,
                Shape = rectangle ? ProjectileShape.Rectangle : ProjectileShape.Disk, ExpiresTick = int.MaxValue });
            battle.Store.Commit();
            var beforeHits = battle.Counters.Hits; var beforePlasma = battle.Counters.PlasmaShieldHits;
            Call(battle, "Collect"); Call(battle, "StepPhysics"); Call(battle, "ResolveHits");
            var profile = DamageRules.ForWeapon(kind, settings);
            AssertDamage(battle, victim, shield, DamageRules.Resolve(100, shield, 0.25f, profile), profile, beforePlasma, owner);
            Check(battle.Counters.Hits == beforeHits + 1 && battle.Counters.DebrisHits == 0,
                "real sweep routes exactly one primary hit through projectile kind");
            trace.Add(Read(battle, victim, "projectile-" + kind + "-" + shield));
            Call(battle, "FlushLifecycle"); battle.Store.Commit(); Call(battle, "CompactMovingRows");
            Check(!battle.Store.Alive(e), "hit round retires through real lifecycle");
        }
    }

    private static void TurretCheck<T>(Func<T> factory, DamageSettings settings, WeaponKind gunKind,
        WeaponKind parentKind, List<Snapshot> trace) where T : IEcsStore
    {
        using var battle = Fixture(factory, settings);
        var parent = Actor(battle, 0, ShipClass.Frigate, new(-60, 0));
        var victim = Actor(battle, 1, ShipClass.Fighter, Vector2.Zero);
        ResetTarget(battle, victim, 75); FillDebrisBudget(battle);
        var mounts = Mounts(battle);
        foreach (var mount in mounts) battle.Store.Get<Weapon>(mount).NextFireTick = int.MaxValue;
        var gun = mounts.First(e => battle.Store.Get<MountedTurret>(e).Parent == parent);
        battle.Store.Get<Weapon>(parent).Kind = parentKind;
        battle.Store.Get<Brain>(parent).Mode = BrainMode.Attack; battle.Store.Get<Brain>(parent).Target = victim;
        battle.Store.Get<Weapon>(gun) = new Weapon { Kind = gunKind, Burst = 1, Damage = 100, Speed = 120,
            Range = 200, Cooldown = 1000, NextFireTick = 0 };
        battle.Store.Get<Heat>(gun) = new Heat { Limit = 1000 };
        battle.Store.Get<MountedTurret>(gun).Target = victim;
        battle.Store.Get<MountedTurret>(gun).NextDecisionTick = int.MaxValue;
        Call(battle, "Collect"); Call(battle, "SyncTurrets");
        var offset = battle.Store.Get<Transform>(victim).Position - battle.Store.Get<Transform>(gun).Position;
        battle.Store.Get<Transform>(gun).Angle = MathF.Atan2(offset.Y, offset.X);
        Call(battle, "UpdateTurrets"); Call(battle, "FlushShots");
        var rounds = new CollectProjectiles { Rows = new() }; battle.Store.ForEach<Projectile, CollectProjectiles>(ref rounds);
        Check(rounds.Rows.Count == 1 && battle.Counters.TurretShots == 1, "one actual turret emission");
        var round = rounds.Rows.Single(); var projectile = battle.Store.Get<Projectile>(round);
        Check(projectile.Kind == gunKind && projectile.Owner == parent && projectile.Damage == 100 && gunKind != parentKind,
            "emitted round owns mount weapon kind, not parent primary kind");
        // Retain the actual emission's damage/kind/owner; control only its swept segment.
        battle.Store.Get<Transform>(round) = new Transform { Position = new(-20, 0), Previous = new(-20, 0) };
        battle.Store.Get<Motion>(round) = new Motion { Velocity = new(2400, 0) };
        Call(battle, "StepPhysics"); Call(battle, "ResolveHits");
        var profile = DamageRules.ForWeapon(gunKind, settings);
        AssertDamage(battle, victim, 75, DamageRules.Resolve(100, 75, 0.25f, profile), profile, 0, parent);
        Check(battle.Counters.Hits == 1 && battle.Counters.DebrisHits == 0, "real turret round applies its own layer profile");
        trace.Add(Read(battle, victim, "turret-" + gunKind));
    }

    public static void RunAllocationChecks()
    {
        AllocationCheck(static () => new SkecsStore()); AllocationCheck(static () => new LeoEcsLiteStore());
        AllocationCheck(static () => new LeoEcsClassicStore()); AllocationCheck(static () => new MorpehStore());
        AllocationCheck(static () => new StaticEcsStore());
    }

    private static void AllocationCheck<T>(Func<T> factory) where T : IEcsStore
    {
        using var battle = Fixture(factory, new DamageSettings());
        var attacker = Actor(battle, 0, ShipClass.Fighter, new(-60, 0));
        var victim = Actor(battle, 1, ShipClass.Fighter, Vector2.Zero);
        var states = States(battle);
        const int count = 1024;
        for (var warm = 0; warm < 4; warm++) { states.Clear(); DamageBatch(battle, victim, attacker, count); }
        var capacity = (int)states.GetType().GetProperty("Capacity")!.GetValue(states)!;
        Check(capacity >= count * 2, "damage state list capacity warmed before allocation sampling");
        states.Clear();
        var before = GC.GetAllocatedBytesForCurrentThread();
        DamageBatch(battle, victim, attacker, count);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, typeof(T).Name + " warmed DamageWeapon allocated " + bytes + " bytes");
        Check(states.Count == count + count / 2, "measured batch actually applies all hits and queues states");
    }

    private static void DamageBatch<T>(Battle<T> battle, EntityKey victim, EntityKey attacker, int count) where T : IEcsStore
    {
        for (var i = 0; i < count; i++)
        {
            battle.Store.Get<Hull>(victim).Current = 10000;
            battle.Store.Get<Hull>(victim).Armor = 0.25f;
            battle.Store.Get<Shield>(victim).Current = (i & 1) == 0 ? 75 : 0;
            battle.Store.Get<Heat>(victim).Value = 7;
            battle.DamageWeapon(victim, 1, (WeaponKind)(i % 5), attacker);
        }
    }

    private static Battle<T> Fixture<T>(Func<T> factory, DamageSettings settings) where T : IEcsStore
    {
        var battle = new Battle<T>(factory(), new BattleConfig { ShipsPerTeam = 8, Seed = 27,
            Damage = settings, ParticleMultiplier = 0, MaxDebris = 1, ReplenishCarriers = false });
        var frame = new BattleFrame(); battle.Capture(frame);
        for (var i = 0; i < frame.Ships.Count; i++)
        {
            Check(battle.TryFindShip(frame.Ships[i].Serial, out var e), "fixture ship lookup");
            Freeze(battle, e);
            var position = new Vector2(1000 + i * 60, 1000);
            battle.Store.Get<Transform>(e) = new Transform { Position = position, Previous = position };
        }
        foreach (var gun in Mounts(battle)) battle.Store.Get<Weapon>(gun).NextFireTick = int.MaxValue;
        return battle;
    }

    private static EntityKey Actor<T>(Battle<T> battle, int team, ShipClass kind, Vector2 position) where T : IEcsStore
    {
        var e = battle.SpawnShip(team, kind, position, EntityKey.None); Freeze(battle, e); ResetTarget(battle, e, 0);
        battle.Store.Commit(); return e;
    }

    private static void Freeze<T>(Battle<T> battle, EntityKey e) where T : IEcsStore
    {
        battle.Store.Get<Motion>(e) = default;
        battle.Store.Get<Brain>(e) = new Brain { Mode = BrainMode.Patrol, NextDecisionTick = int.MaxValue,
            Home = EntityKey.None, Target = EntityKey.None, CoverAlly = EntityKey.None, SquadLeader = EntityKey.None };
        battle.Store.Get<Weapon>(e).NextFireTick = int.MaxValue;
        battle.Store.Get<Inventory>(e).NextUseTick = int.MaxValue;
        if (battle.Store.Has<SecondaryWeapon>(e)) battle.Store.Get<SecondaryWeapon>(e).Value.NextFireTick = int.MaxValue;
        if (battle.Store.Has<Carrier>(e)) battle.Store.Get<Carrier>(e).NextBuildTick = int.MaxValue;
    }

    private static void ResetTarget<T>(Battle<T> battle, EntityKey e, float shield) where T : IEcsStore
    {
        battle.Store.Get<Hull>(e) = new Hull { Current = 10000, Maximum = 10000, Armor = 0.25f };
        battle.Store.Get<Shield>(e) = new Shield { Current = shield, Maximum = 1000, LastHitTick = -1000, RegenDelayTicks = int.MaxValue };
        battle.Store.Get<Heat>(e) = new Heat { Value = 7, Limit = 10000 };
        battle.Store.Get<CombatState>(e) = new CombatState { LastDamageTick = -1000, LastAttacker = EntityKey.None };
    }

    private static void FillDebrisBudget<T>(Battle<T> battle) where T : IEcsStore
    {
        // A stationary, distant fragment occupies the one-slot budget so explosive primary
        // rounds cannot contaminate this layer-routing fixture with secondary neutral damage.
        var e = battle.Store.Create(); var position = new Vector2(-500, 500);
        battle.Store.Set(e, new Transform { Position = position, Previous = position }); battle.Store.Set(e, new Motion());
        battle.Store.Set(e, new Debris { Serial = 2000000, ExpiresTick = int.MaxValue, Radius = 0.1f }); battle.Store.Commit();
    }

    private static Snapshot Read<T>(Battle<T> battle, EntityKey e, string label) where T : IEcsStore
    {
        var shield = battle.Store.Get<Shield>(e); var combat = battle.Store.Get<CombatState>(e);
        return new(label, Bits(shield.Current), Bits(battle.Store.Get<Hull>(e).Current), Bits(battle.Store.Get<Heat>(e).Value),
            shield.LastHitTick, combat.LastDamageTick, combat.LastAttacker, battle.Counters.Hits, battle.Counters.PlasmaShieldHits, battle.Checksum());
    }

    private static IList States<T>(Battle<T> battle) where T : IEcsStore =>
        (IList)typeof(Battle<T>).GetField("states", Hidden)!.GetValue(battle)!;
    private static void Call<T>(Battle<T> battle, string method) where T : IEcsStore =>
        typeof(Battle<T>).GetMethod(method, Hidden)!.Invoke(battle, null);
    private static List<EntityKey> Mounts<T>(Battle<T> battle) where T : IEcsStore
    { var query = new CollectMounts { Rows = new() }; battle.Store.ForEach<MountedTurret, CollectMounts>(ref query); return query.Rows; }
    private struct CollectMounts : IProcessor<MountedTurret>
    { public List<EntityKey> Rows; public void Execute(EntityKey e, ref MountedTurret mount) => Rows.Add(e); }
    private struct CollectProjectiles : IProcessor<Projectile>
    { public List<EntityKey> Rows; public void Execute(EntityKey e, ref Projectile p) => Rows.Add(e); }
    private static void CheckReplay(Snapshot[] expected, Snapshot[] actual, string label) =>
        Check(expected.SequenceEqual(actual), label + " damage trace/checksum repeat");

    public readonly record struct ControlledHit(WeaponKind Kind, float BaseDamage);
    public sealed record ControlledComposition(string Name, ControlledHit[] Hits);
    public readonly record struct ControlledResult(string Composition, float InitialShield, float InitialHull, float Armor,
        float ScheduledBaseDps, bool Killed, int Volleys, int Hits, int? TimeToKillTicks, double? TimeToKillSeconds,
        float BaseDamageDelivered, float ShieldDamage, float HullDamage, float ShieldHeat, float HullOverkill,
        float FinalShield, float FinalHull, ulong TraceHash);
    public sealed record ControlledStudy(string Revision, string Scope, DamageSettings Settings, int TickRate,
        int FirstVolleyTick, int VolleyIntervalTicks, int MaxVolleys, ControlledComposition[] Compositions, ControlledResult[] Results);

    // Simulation time, not wall-clock performance: identical base damage/cadence and target,
    // discrete ordered hits, no travel, misses, regeneration, cooling, heat lockout or fragments.
    public static ControlledStudy CreateControlledStudy(DamageSettings? settings = null)
    {
        settings ??= new DamageSettings(); settings.Validate();
        const int tickRate = 60, interval = 60, maximum = 10000;
        var compositions = Kinds.Select(kind => new ControlledComposition(kind.ToString(), [new(kind, 100)]))
            .Concat(new[]
            {
                new ControlledComposition("plasma-then-cannon", [new(WeaponKind.Plasma, 50), new(WeaponKind.Cannon, 50)]),
                new ControlledComposition("cannon-then-plasma", [new(WeaponKind.Cannon, 50), new(WeaponKind.Plasma, 50)]),
                new ControlledComposition("all-five", Kinds.Select(kind => new ControlledHit(kind, 20)).ToArray())
            }).ToArray();
        var rows = new List<ControlledResult>();
        foreach (var initialShield in new[] { 0f, 750f, 3000f })
        foreach (var composition in compositions)
        {
            var basePerVolley = composition.Hits.Sum(hit => hit.BaseDamage);
            Check(basePerVolley == 100, "study compositions have equal scheduled base DPS");
            var shield = initialShield; var hull = 1000f; const float armor = 0.25f;
            var volleys = 0; var hits = 0; var delivered = 0f; var shieldDamage = 0f; var hullDamage = 0f;
            var heat = 0f; var overkill = 0f; var hash = 14695981039346656037UL;
            while (hull > 0 && volleys < maximum)
            {
                volleys++;
                foreach (var hit in composition.Hits)
                {
                    var profile = DamageRules.ForWeapon(hit.Kind, settings);
                    var layer = DamageRules.Resolve(hit.BaseDamage, shield, armor, profile);
                    var actualHull = MathF.Min(hull, layer.Hull);
                    shield -= layer.Shield; hull = MathF.Max(0, hull - layer.Hull);
                    hits++; delivered += hit.BaseDamage; shieldDamage += layer.Shield; hullDamage += actualHull;
                    heat += layer.Shield * profile.ShieldHeatPerDamage; overkill += layer.Hull - actualHull;
                    Hash(ref hash, (uint)(volleys * interval)); Hash(ref hash, (uint)hit.Kind); Hash(ref hash, (uint)Bits(hit.BaseDamage));
                    Hash(ref hash, (uint)Bits(shield)); Hash(ref hash, (uint)Bits(hull)); Hash(ref hash, (uint)Bits(heat));
                    if (hull <= 0) break;
                }
            }
            var killed = hull <= 0; int? ticks = killed ? volleys * interval : null;
            rows.Add(new(composition.Name, initialShield, 1000, armor, basePerVolley * tickRate / interval, killed,
                volleys, hits, ticks, ticks is { } value ? (double)value / tickRate : null,
                delivered, shieldDamage, hullDamage, heat, overkill, shield, hull, hash));
        }
        return new(DamageRules.Revision, "controlled discrete-hit layer comparison; not fleet balance or wall-clock performance",
            settings, tickRate, interval, interval, maximum, compositions, rows.ToArray());
    }

    private static void StudyChecks()
    {
        var first = CreateControlledStudy(); var second = CreateControlledStudy();
        Check(first.Results.SequenceEqual(second.Results), "controlled study exact repeat");
        Check(first.Results.Length == 24 && first.Results.All(row => row.Killed && row.ScheduledBaseDps == 100 &&
            row.ShieldDamage <= row.InitialShield && row.FinalShield >= 0 && row.FinalHull == 0), "study bounded equal-DPS rows");
        foreach (var kind in new[] { WeaponKind.MachineGun, WeaponKind.Cannon, WeaponKind.HeavyCannon })
            Check(first.Results.Single(row => row.Composition == kind.ToString() && row.InitialShield == 0).TimeToKillTicks == 9 * 60,
                "unshielded kinetic controlled discrete TTK");
        foreach (var kind in new[] { WeaponKind.Plasma, WeaponKind.Missile })
            Check(first.Results.Single(row => row.Composition == kind.ToString() && row.InitialShield == 0).TimeToKillTicks == 14 * 60,
                "unshielded neutral-hull controlled discrete TTK");
        var custom = CreateControlledStudy(Custom);
        Check(custom.Results.SequenceEqual(CreateControlledStudy(Custom).Results), "configured-profile study exact repeat");
    }

    private static void Hash(ref ulong hash, uint value) => hash = unchecked((hash ^ value) * 1099511628211UL);
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static void Same(float actual, float expected, string label) => Check(Bits(actual) == Bits(expected), label + " bit-exact");
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
