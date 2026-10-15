using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void ProjectileOwnerChecks()
    {
        foreach (var kind in Enum.GetValues<WeaponKind>())
        {
            OwnerLifetimeCheck(kind);
            OwnerBehindCheck(kind, friendly: false);
            OwnerBehindCheck(kind, friendly: true);
        }
        OwnerMissingCheck(liveWithoutIdentity: false);
        OwnerMissingCheck(liveWithoutIdentity: true);
        OwnerMountedCheck();
        OwnerDebrisCheck();
        OwnerStoreChecks(static () => new SkecsStore());
        OwnerStoreChecks(static () => new LeoEcsLiteStore());
        OwnerStoreChecks(static () => new LeoEcsClassicStore());
        OwnerStoreChecks(static () => new MorpehStore());
        OwnerStoreChecks(static () => new StaticEcsStore());
        Console.WriteLine("PASS: all five projectile kinds exclude only owner, retain ally/enemy hits and lifetime; mounts own parent, debris stays neutral, absent/dead/stale owners and all-store generation reuse.");
    }

    private static EntityKey OwnerRound<TStore>(Battle<TStore> battle, EntityKey owner, WeaponKind kind,
        Vector2 position, Vector2 velocity, int expires = 100) where TStore : IEcsStore
    {
        // Synthetic serials belong only to disposable fixtures, not canonical replay validation.
        var e = battle.Store.Create();
        battle.Store.Set(e, new Transform { Position = position, Previous = position });
        battle.Store.Set(e, new Motion { Velocity = velocity });
        battle.Store.Set(e, new Projectile { Serial = 1000000, Owner = owner, Target = EntityKey.None,
            Kind = kind, Damage = 10, Radius = kind == WeaponKind.MachineGun ? 0.12f : kind == WeaponKind.HeavyCannon ? 0.9f : 0.45f,
            ExpiresTick = expires });
        battle.Store.Commit();
        return e;
    }

    private static EntityKey OwnerActor<TStore>(Battle<TStore> battle, int team, ShipClass kind, Vector2 position)
        where TStore : IEcsStore
    {
        var e = battle.SpawnShip(team, kind, position, EntityKey.None);
        LifecycleFreezeShip(battle, e);
        battle.Store.Get<Transform>(e).Angle = 0;
        battle.Store.Get<Inventory>(e).NextUseTick = int.MaxValue;
        battle.Store.Get<Hull>(e) = new Hull { Current = 10000, Maximum = 10000 };
        battle.Store.Get<Shield>(e) = default;
        return e;
    }

    private static void OwnerLifetimeCheck(WeaponKind kind)
    {
        using var battle = ArmamentBattle();
        var owner = ArmamentActor(battle, 0, ShipClass.Frigate, Vector2.Zero);
        var hull = battle.Store.Get<Hull>(owner).Current;
        var shield = battle.Store.Get<Shield>(owner).Current;
        var round = OwnerRound(battle, owner, kind, new(-20, 0), new(2400, 0), expires: 3);
        battle.Step();
        Require(battle.Store.Alive(round) && battle.ProjectileCount == 1, kind + " owner crossing does not retire round");
        Require(battle.Store.Get<Transform>(round).Position.X > 0, kind + " round advances past owner");
        battle.Store.Get<Transform>(round) = default;
        battle.Store.Get<Motion>(round) = default;
        battle.Step();
        Require(battle.Store.Alive(round), kind + " stationary round inside compound owner survives until expiry");
        Require(battle.Store.Get<Hull>(owner).Current == hull && battle.Store.Get<Shield>(owner).Current == shield,
            kind + " own hull and shield remain unchanged");
        Require(battle.Counters.Hits == 0 && battle.Counters.FriendlyFireHits == 0 && battle.Counters.SelfHits == 0,
            kind + " ignored owner does not increment telemetry");
        battle.Step();
        Require(!battle.Store.Alive(round) && battle.ProjectileCount == 0, kind + " ignored round expires at original deadline");
    }

    private static void OwnerBehindCheck(WeaponKind kind, bool friendly)
    {
        using var battle = ArmamentBattle();
        var owner = ArmamentActor(battle, 0, ShipClass.Frigate, Vector2.Zero);
        var victim = ArmamentActor(battle, friendly ? 0 : 1, ShipClass.Fighter, new(12, 0));
        var farther = ArmamentActor(battle, friendly ? 1 : 0, ShipClass.Fighter, new(28, 0));
        var round = OwnerRound(battle, owner, kind, new(-20, 0), new(2400, 0));
        battle.Step();
        Require(battle.Store.Get<Hull>(owner).Current == 10000, kind + " owner in front cannot shield victim");
        Require(battle.Store.Get<Hull>(victim).Current <= 9990 && battle.Store.Get<Hull>(farther).Current == 10000,
            kind + " nearest nonowner hull is damaged, not farther ship");
        Require(!battle.Store.Alive(round) && battle.ProjectileCount == 0 && battle.Counters.Hits == 1,
            kind + " round consumed exactly once on actual other ship");
        Require(battle.Counters.FriendlyFireHits == (friendly ? 1 : 0) && battle.Counters.SelfHits == 0,
            kind + " friendly telemetry counts only actual other ship");
    }

    private static void OwnerMissingCheck(bool liveWithoutIdentity)
    {
        using var battle = ArmamentBattle();
        var victim = ArmamentActor(battle, 0, ShipClass.Fighter, Vector2.Zero);
        var owner = EntityKey.None;
        if (liveWithoutIdentity) owner = battle.Store.Create();
        var round = OwnerRound(battle, owner, WeaponKind.MachineGun, new(-20, 0), new(2400, 0));
        battle.Step();
        Require(!battle.Store.Alive(round) && battle.Store.Get<Hull>(victim).Current == 9985 && battle.Counters.Hits == 1,
            liveWithoutIdentity ? "live owner without Identity grants no immunity" : "Owner None grants no immunity");
        Require(battle.Counters.FriendlyFireHits == 0 && battle.Counters.SelfHits == 0,
            "missing owner identity cannot fabricate friendly/self telemetry");
    }

    private static void OwnerMountedCheck()
    {
        using var battle = ArmamentBattle();
        var parent = ArmamentActor(battle, 0, ShipClass.Frigate, Vector2.Zero);
        var target = ArmamentActor(battle, 1, ShipClass.Fighter, new(80, 0));
        ArmamentAttack(battle, parent, target);
        var gun = MountedGuns(battle, parent)[0];
        ArmamentGun(battle, gun, target, 1000, 0);
        battle.Store.Get<Weapon>(gun).Kind = WeaponKind.Plasma;
        ArmamentAim(battle, gun);
        battle.Step();
        var round = ArmamentProjectiles(battle).Single();
        Require(battle.Store.Get<Projectile>(round).Owner == parent && parent != gun && battle.Counters.TurretShots == 1,
            "real mounted emission assigns ship parent, not gun, as owner");
        FreezeMountedGuns(battle, parent);
        battle.Store.Get<Brain>(parent).Mode = BrainMode.Patrol;
        var ally = ArmamentActor(battle, 0, ShipClass.Frigate, new(12, 0));
        battle.Store.Get<MountedTurret>(gun).Parent = ally;
        Require(battle.Store.Get<Projectile>(round).Owner == parent,
            "reparenting gun does not change already emitted round owner");
        // Re-enter the hull with the real emitted round, rather than relying on safe muzzle placement.
        battle.Store.Get<Transform>(round) = new Transform { Position = new(-20, 0), Previous = new(-20, 0) };
        battle.Store.Get<Motion>(round) = new Motion { Velocity = new(2400, 0) };
        battle.Step();
        Require(battle.Store.Get<Hull>(parent).Current == 10000 && !battle.Store.Alive(round),
            "mounted round traverses own parent and consumes on another ship");
        Require(battle.Store.Get<Hull>(ally).Current == 9999 && battle.Store.Get<Hull>(target).Current == 10000 &&
            battle.Counters.Hits == 1 && battle.Counters.FriendlyFireHits == 1 && battle.Counters.SelfHits == 0,
            "emitted round excludes original parent, not gun's new ally parent");
    }

    private static void OwnerDebrisCheck()
    {
        using var battle = ArmamentBattle();
        var owner = ArmamentActor(battle, 0, ShipClass.Frigate, Vector2.Zero);
        var round = OwnerRound(battle, owner, WeaponKind.Plasma, new(-20, 0), new(2400, 0));
        var debris = battle.Store.Create();
        battle.Store.Set(debris, new Transform { Position = new(-20, 0), Previous = new(-20, 0) });
        battle.Store.Set(debris, new Motion { Velocity = new(2400, 0) });
        battle.Store.Set(debris, new Debris { Serial = 1000001, Damage = 1, Radius = 0.1f, ExpiresTick = 100 });
        battle.Store.Commit();
        battle.Step();
        Require(battle.Store.Alive(round) && !battle.Store.Alive(debris) && battle.Store.Get<Hull>(owner).Current == 9940,
            "neutral debris hits same owner immediately after projectile ignored sweep");
        Require(battle.Counters.DebrisHits == 1 && battle.Counters.Hits == 0 &&
            battle.Counters.FriendlyFireHits == 0 && battle.Counters.SelfHits == 0, "debris keeps separate neutral counters");
    }

    private static void OwnerStoreChecks<TStore>(Func<TStore> factory) where TStore : IEcsStore
    {
        using (var battle = LifecycleBattle(factory))
        {
            var stale = battle.Store.Create();
            battle.Store.Set(stale, new Identity { Serial = 777777, Team = 0, Class = ShipClass.Fighter });
            var round = OwnerRound(battle, stale, WeaponKind.MachineGun, new(-20, 0), new(2400, 0));
            battle.Store.Destroy(stale);
            battle.Store.Commit();
            var replacement = OwnerActor(battle, 0, ShipClass.Fighter, Vector2.Zero);
            Require(replacement.Id == stale.Id && replacement.Generation != stale.Generation && !battle.Store.Alive(stale),
                battle.Framework + " owner EntityKey slot reused with distinct generation");
            battle.Step();
            Require(!battle.Store.Alive(round) && battle.Store.Get<Hull>(replacement).Current == 9985 && battle.Counters.Hits == 1,
                battle.Framework + " stale owner cannot immunize replacement ship");
            Require(battle.Counters.FriendlyFireHits == 0 && battle.Counters.SelfHits == 0,
                battle.Framework + " stale owner cannot credit replacement team");
        }
        using (var battle = LifecycleBattle(factory))
        {
            var owner = OwnerActor(battle, 0, ShipClass.Fighter, new(-100, 0));
            var victim = OwnerActor(battle, 1, ShipClass.Fighter, new(12, 0));
            var round = OwnerRound(battle, owner, WeaponKind.Plasma, new(-40, 0), new(1800, 0));
            battle.Damage(owner, 10001, false, false);
            battle.Step();
            Require(!battle.Store.Alive(owner) && battle.Store.Alive(round) && battle.Counters.ShipsDestroyed == 1,
                battle.Framework + " owner death leaves emitted projectile alive");
            Require(battle.Store.Get<Transform>(round).Position.X > -40 && battle.Counters.Hits == 0,
                battle.Framework + " emitted round keeps moving during owner death flush");
            battle.Step();
            Require(!battle.Store.Alive(round) && battle.Store.Get<Hull>(victim).Current == 9990 && battle.Counters.Hits == 1,
                battle.Framework + " dead-owner projectile continues and hits another ship");
            Require(battle.Counters.FriendlyFireHits == 0 && battle.Counters.SelfHits == 0,
                battle.Framework + " dead owner does not produce friendly/self telemetry");
        }
    }
}
