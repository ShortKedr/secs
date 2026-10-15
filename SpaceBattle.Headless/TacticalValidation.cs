using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    public static void LegacyTacticsChecks() { TacticalChecks(); TacticalSpatialChecks(); }
    private static void TacticalChecks()
    {
        Require(TacticalRules.Revision == "layered-squads-v3", "tactical revision");
        Require(TacticalRules.MinimumTargetLockTicks == 120 && TacticalRules.MaxSquadSize == 4 &&
            TacticalRules.FormationRange == 48 && TacticalRules.SquadBreakRange == 96 &&
            MathF.Abs(TacticalRules.SwitchImprovement - 0.35f) < 0.00001f, "tactical constants");
        TacticalLockChecks();
        TacticalCarrierChecks();
        TacticalGroupChecks();
        TacticalScreenChecks();
        TacticalCoverRepairChecks();
        Console.WriteLine("PASS: tactical leases, meaningful upgrades, dead targets, carrier scoring/hits, bounded squads, cover steering/no fire/recovery and leader repair.");
    }

    private static Battle<SkecsStore> TacticalBattle()
    {
        var battle = new Battle<SkecsStore>(new(), new BattleConfig { ShipsPerTeam = 16, Seed = 27 });
        var frame = FreezeBattle(battle);
        // Keep unrelated frozen ships out of both squad formation and target scoring.
        for (var i = 0; i < frame.Ships.Count; i++)
        {
            Require(battle.TryFindShip(frame.Ships[i].Serial, out var e), "tactical frozen ship lookup");
            TacticalPosition(battle, e, new(1000 + i * 40, 1000));
        }
        return battle;
    }

    private static EntityKey TacticalActor(Battle<SkecsStore> battle, int team, ShipClass kind, Vector2 position, bool active)
    {
        var e = battle.SpawnShip(team, kind, position, EntityKey.None);
        battle.Store.Commit();
        battle.Store.Get<Brain>(e) = new Brain
        {
            Mode = active ? BrainMode.Attack : BrainMode.Patrol,
            NextDecisionTick = active ? 0 : int.MaxValue,
            Target = EntityKey.None, Home = EntityKey.None,
            SquadLeader = EntityKey.None, CoverAlly = EntityKey.None, OrbitSign = 1
        };
        battle.Store.Get<Motion>(e).Velocity = Vector2.Zero;
        battle.Store.Get<Motion>(e).Thrust = 0;
        battle.Store.Get<Weapon>(e).NextFireTick = active ? 0 : int.MaxValue;
        if (active) battle.Store.Get<Weapon>(e).HeatPerShot = 0;
        battle.Store.Get<Inventory>(e).NextUseTick = int.MaxValue;
        battle.Store.Get<Shield>(e).RegenPerSecond = 0;
        if (battle.Store.Has<SecondaryWeapon>(e)) battle.Store.Get<SecondaryWeapon>(e).Value.NextFireTick = int.MaxValue;
        if (battle.Store.Has<Carrier>(e)) battle.Store.Get<Carrier>(e).NextBuildTick = int.MaxValue;
        FreezeMountedGuns(battle, e);
        return e;
    }

    private static void TacticalPosition(Battle<SkecsStore> battle, EntityKey e, Vector2 position)
    {
        battle.Store.Get<Transform>(e).Position = position;
        battle.Store.Get<Transform>(e).Previous = position;
        battle.Store.Get<Motion>(e).Velocity = Vector2.Zero;
    }

    private static void TacticalDurability(Battle<SkecsStore> battle, EntityKey e, float hull, float shield = 0, float armor = 0)
    {
        battle.Store.Get<Hull>(e) = new Hull { Current = hull, Maximum = hull, Armor = armor };
        battle.Store.Get<Shield>(e).Current = shield;
        battle.Store.Get<Shield>(e).Maximum = MathF.Max(1, shield);
        battle.Store.Get<Shield>(e).RegenPerSecond = 0;
    }

    private static void TacticalDecide(Battle<SkecsStore> battle, params EntityKey[] actors)
    {
        foreach (var e in actors) battle.Store.Get<Brain>(e).NextDecisionTick = 0;
        battle.Step();
    }

    private static void TacticalAdvanceTo(Battle<SkecsStore> battle, int tick)
    {
        while (battle.Tick < tick) battle.Step();
    }

    private static void TacticalLockChecks()
    {
        using var battle = TacticalBattle();
        var actor = TacticalActor(battle, 0, ShipClass.Corvette, Vector2.Zero, true);
        var first = TacticalActor(battle, 1, ShipClass.Frigate, new(40, -8), false);
        var other = TacticalActor(battle, 1, ShipClass.Frigate, new(41, 8), false);
        TacticalDurability(battle, first, 1000);
        TacticalDurability(battle, other, 1000);
        TacticalDecide(battle, actor);
        var held = battle.Store.Get<Brain>(actor).Target;
        Require(held == first || held == other, "lock chooses controlled target");
        var alternate = held == first ? other : first;
        var lease = battle.Store.Get<Brain>(actor).TargetLockUntilTick;
        Require(lease >= battle.Tick + TacticalRules.MinimumTargetLockTicks, "initial target has full lease");
        Require(float.IsFinite(battle.Store.Get<Brain>(actor).EstimatedKillSeconds) &&
            battle.Store.Get<Brain>(actor).EstimatedKillSeconds > 0, "finite positive kill estimate");
        var switches = battle.Tactics.TargetSwitches;
        for (var i = 0; i < 24; i++)
        {
            TacticalPosition(battle, held, new(i % 2 == 0 ? 41 : 40, -8));
            TacticalPosition(battle, alternate, new(i % 2 == 0 ? 40 : 41, 8));
            TacticalDecide(battle, actor);
            Require(battle.Store.Get<Brain>(actor).Target == held, "alternating nearest enemy does not break lease");
        }
        Require(battle.Tactics.TargetSwitches == switches && battle.Tactics.TargetRetentions > 0, "retention telemetry without switches");
        TacticalPosition(battle, held, new(41, -8));
        TacticalPosition(battle, alternate, new(40, 8));
        TacticalAdvanceTo(battle, lease);
        TacticalDecide(battle, actor);
        Require(battle.Store.Get<Brain>(actor).Target == held, "expired lease rejects marginal improvement");
        TacticalAdvanceTo(battle, battle.Store.Get<Brain>(actor).TargetLockUntilTick);
        TacticalDurability(battle, alternate, 10);
        TacticalDecide(battle, actor);
        Require(battle.Store.Get<Brain>(actor).Target == alternate, "expired lease accepts clear kill-cost improvement");
        Require(battle.Tactics.TargetSwitches > switches, "upgrade increments switch telemetry");
        Require(battle.Store.Get<Brain>(actor).TargetLockUntilTick > battle.Tick, "upgrade renews lease");
        battle.Damage(alternate, 100000, false, false);
        TacticalDecide(battle, actor);
        Require(battle.Store.Get<Brain>(actor).Target == held, "dead target releases immediately during lease");
        var reachable = TacticalActor(battle, 1, ShipClass.Frigate, new(55, 20), false);
        TacticalDurability(battle, reachable, 1000);
        battle.Store.Get<Brain>(actor).TargetLockUntilTick = battle.Tick + TacticalRules.MinimumTargetLockTicks;
        battle.Store.Get<Brain>(actor).NextDecisionTick = battle.Tick + 100;
        TacticalPosition(battle, held, new(80, -8));
        battle.Store.Get<Motion>(held).Velocity = new(500, 0);
        battle.Step();
        Require(battle.Store.Get<Brain>(actor).Target == reachable, "unreachable escaping target releases immediately before decision and lease clocks");
    }

    private static void TacticalCarrierChecks()
    {
        using var battle = TacticalBattle();
        var actor = TacticalActor(battle, 0, ShipClass.FleetCarrier, Vector2.Zero, true);
        var tank = TacticalActor(battle, 1, ShipClass.Frigate, new(25, 18), false);
        var weak = TacticalActor(battle, 1, ShipClass.Frigate, new(48, 0), false);
        TacticalDurability(battle, tank, 20000, 10000, 0.8f);
        TacticalDurability(battle, weak, 200);
        TacticalDecide(battle, actor);
        Require(battle.Store.Get<Brain>(actor).Target == weak, "carrier chooses killable weak target over nearer shielded armored tank");
        TacticalPosition(battle, weak, new(100, 0));
        battle.Store.Get<Motion>(weak).Velocity = new(35, 0);
        TacticalDecide(battle, actor);
        Require(battle.Store.Get<Brain>(actor).Target == weak, "carrier retains an interceptable target moving outward inside firing range");
        TacticalPosition(battle, weak, new(48, 0));
        var before = battle.Store.Get<Hull>(weak).Current;
        var hits = battle.Counters.Hits;
        battle.Store.Get<Weapon>(actor).NextFireTick = 0;
        if (battle.Store.Has<SecondaryWeapon>(actor)) battle.Store.Get<SecondaryWeapon>(actor).Value.NextFireTick = 0;
        for (var i = 0; i < 180 && battle.Store.Alive(weak) && battle.Store.Get<Hull>(weak).Current >= before; i++) battle.Step();
        Require(battle.Counters.Shots > 0 && battle.Counters.Hits > hits, "carrier selected target produces real shots and swept hits");
        Require(!battle.Store.Alive(weak) || battle.Store.Get<Hull>(weak).Current < before, "carrier projectiles damage selected weak target");
    }

    private static void TacticalGroupChecks()
    {
        using var battle = TacticalBattle();
        var allies = Enumerable.Range(0, 6).Select(i => TacticalActor(battle, 0, ShipClass.Fighter, new(0, i * 5 - 12.5f), true)).ToArray();
        var first = TacticalActor(battle, 1, ShipClass.Frigate, new(50, -20), false);
        var second = TacticalActor(battle, 1, ShipClass.Frigate, new(50, 20), false);
        TacticalDurability(battle, first, 1000);
        TacticalDurability(battle, second, 1000);
        TacticalDecide(battle, allies);
        var groups = allies.GroupBy(e => battle.Store.Get<Brain>(e).SquadLeader).ToArray();
        if (!groups.Any(g => !g.Key.IsNone && g.Count() >= 2))
        {
            foreach (var e in allies)
                Console.WriteLine($"Group fixture: {battle.Store.Get<Identity>(e).Serial} mode={battle.Store.Get<Brain>(e).Mode} speed={battle.Store.Get<Motion>(e).MaxSpeed} range={battle.Store.Get<Weapon>(e).Range} next={battle.Store.Get<Weapon>(e).NextFireTick} heat={battle.Store.Get<Heat>(e).Value} target={battle.Store.Get<Brain>(e).Target}");
            Console.WriteLine($"Work: scores={battle.Tactics.ScoreEvaluations} spatial={battle.Tactics.SpatialWork} compatibility={battle.Tactics.CompatibilityChecks}");
        }
        Require(groups.Any(g => !g.Key.IsNone && g.Count() >= 2), "nearby small ships form a squad");
        foreach (var group in groups.Where(g => !g.Key.IsNone))
        {
            Require(group.Count() <= TacticalRules.MaxSquadSize, "squad never exceeds four ships");
            Require(group.Contains(group.Key), "squad leader is a controlled member, not frozen patrol");
            Require(group.Select(e => battle.Store.Get<Brain>(e).Target).Distinct().Count() == 1 &&
                !battle.Store.Get<Brain>(group.First()).Target.IsNone, "squad shares one enemy focus");
        }
        var leaders = allies.Select(e => battle.Store.Get<Brain>(e).SquadLeader).ToArray();
        for (var i = 0; i < 8; i++) TacticalDecide(battle, allies);
        Require(allies.Select(e => battle.Store.Get<Brain>(e).SquadLeader).SequenceEqual(leaders), "squads remain stable between decisions");
        Require(battle.Tactics.GroupOrders > 0, "group order telemetry");
        var frame = new BattleFrame();
        battle.Capture(frame);
        foreach (var ship in frame.Ships)
        {
            Require(battle.TryFindShip(ship.Serial, out var e), "group freeze lookup");
            if (allies.Contains(e)) continue;
            var brain = battle.Store.Get<Brain>(e);
            Require(brain.Mode == BrainMode.Patrol && brain.NextDecisionTick == int.MaxValue &&
                brain.SquadLeader.IsNone && brain.CoverAlly.IsNone, "frozen patrol never joins tactical reasoning");
        }

        var squad = groups.First(g => !g.Key.IsNone && g.Count() >= 2).ToArray();
        var deadLeader = battle.Store.Get<Brain>(squad[0]).SquadLeader;
        battle.Damage(deadLeader, 100000, false, false);
        var survivors = allies.Where(e => e != deadLeader).ToArray();
        TacticalDecide(battle, survivors);
        foreach (var e in survivors)
            Require(battle.Store.Get<Brain>(e).SquadLeader != deadLeader && battle.Store.Get<Brain>(e).CoverAlly != deadLeader, "dead leader references repaired");

        var repaired = survivors.GroupBy(e => battle.Store.Get<Brain>(e).SquadLeader).First(g => !g.Key.IsNone && g.Count() >= 2);
        var dockedLeader = repaired.Key;
        var home = TacticalActor(battle, 0, ShipClass.LightCarrier, new(-80, 0), false);
        battle.Store.Set(dockedLeader, new Docked { Carrier = home, ReleaseTick = int.MaxValue });
        battle.Store.Commit();
        TacticalDecide(battle, survivors.Where(e => e != dockedLeader).ToArray());
        foreach (var e in survivors.Where(e => e != dockedLeader))
            Require(battle.Store.Get<Brain>(e).SquadLeader != dockedLeader && battle.Store.Get<Brain>(e).CoverAlly != dockedLeader, "docked leader references repaired");
        var split = survivors.First(e => e != dockedLeader);
        TacticalPosition(battle, split, new(-180, -100));
        TacticalDecide(battle, survivors.Where(e => e != dockedLeader).ToArray());
        var splitLeader = battle.Store.Get<Brain>(split).SquadLeader;
        Require(splitLeader.IsNone || splitLeader == split, "separated ship leaves old squad");
        var mismatch = survivors.First(e => e != dockedLeader && e != split);
        battle.Store.Get<Identity>(mismatch).Class = ShipClass.Corvette;
        TacticalDecide(battle, survivors.Where(e => e != dockedLeader).ToArray());
        Require(battle.Store.Get<Brain>(mismatch).SquadLeader.IsNone, "large-class mismatch leaves small-ship squad");
        foreach (var e in survivors.Where(e => e != dockedLeader && e != mismatch))
            Require(battle.Store.Get<Brain>(e).SquadLeader != mismatch && battle.Store.Get<Brain>(e).CoverAlly != mismatch,
                "class mismatch references repaired");
    }

    private static void TacticalScreenChecks()
    {
        using var battle = TacticalBattle();
        var wounded = TacticalActor(battle, 0, ShipClass.Fighter, new(-4, 0), true);
        var healthy = TacticalActor(battle, 0, ShipClass.Fighter, Vector2.Zero, true);
        var threat = TacticalActor(battle, 1, ShipClass.Frigate, new(-40, 0), false);
        TacticalDurability(battle, threat, 1000);
        TacticalDecide(battle, wounded, healthy);
        battle.Store.Get<Hull>(wounded).Current = battle.Store.Get<Hull>(wounded).Maximum * 0.5f;
        battle.Store.Get<Shield>(wounded).Current = battle.Store.Get<Shield>(wounded).Maximum * 0.1f;
        battle.Store.Get<CombatState>(wounded).LastAttacker = threat;
        battle.Store.Get<CombatState>(wounded).LastDamageTick = battle.Tick;
        battle.Store.Get<Shield>(wounded).LastHitTick = battle.Tick;
        battle.Store.Get<Motion>(wounded).Thrust = 20;
        battle.Store.Get<Weapon>(wounded).NextFireTick = 0;
        var shots = battle.Counters.Shots;
        TacticalDecide(battle, wounded, healthy);
        Require(battle.Store.Get<Brain>(wounded).Mode == BrainMode.Screen &&
            battle.Store.Get<Brain>(wounded).CoverAlly == healthy, "wounded threatened squad member screens behind healthy ally");
        var away = Vector2.Normalize(battle.Store.Get<Transform>(healthy).Position - battle.Store.Get<Transform>(threat).Position);
        var coverPoint = battle.Store.Get<Transform>(healthy).Position + away *
            (battle.Store.Get<Collider>(healthy).Radius + battle.Store.Get<Collider>(wounded).Radius + 6);
        Require(Vector2.Dot(coverPoint - battle.Store.Get<Transform>(healthy).Position, away) > 0, "cover point lies behind ally relative to attacker");
        Require(Vector2.Dot(battle.Store.Get<Motion>(wounded).Acceleration, away) > 0 &&
            Vector2.Dot(battle.Store.Get<Motion>(wounded).Acceleration, coverPoint - battle.Store.Get<Transform>(wounded).Position) > 0,
            "screen steering moves away from threat toward rear cover point");
        Require(battle.Counters.Shots == shots && battle.Store.Get<Weapon>(wounded).NextFireTick == 0, "screen does not fire ready weapon");
        Require(battle.Tactics.CoverOrders > 0, "cover order telemetry");
        TacticalPosition(battle, wounded, coverPoint);
        battle.Store.Get<Brain>(wounded).NextDecisionTick = battle.Tick + 100;
        battle.Step();
        Require(battle.Store.Get<Motion>(wounded).Acceleration.LengthSquared() < 0.0001f,
            "screen settles at stationary rear cover point without full-speed overshoot");
        battle.Store.Get<Weapon>(wounded).NextFireTick = int.MaxValue;
        battle.Store.Get<Motion>(wounded).Thrust = 0;
        battle.Store.Get<Motion>(wounded).Velocity = Vector2.Zero;
        TacticalAdvanceTo(battle, battle.Tick + TacticalRules.MinimumTargetLockTicks + 20);
        battle.Store.Get<Hull>(wounded).Current = battle.Store.Get<Hull>(wounded).Maximum * 0.8f;
        battle.Store.Get<Shield>(wounded).Current = battle.Store.Get<Shield>(wounded).Maximum * 0.5f;
        battle.Store.Get<Heat>(wounded).Value = 0;
        TacticalDecide(battle, wounded, healthy);
        Require(battle.Store.Get<Brain>(wounded).Mode == BrainMode.Attack &&
            battle.Store.Get<Brain>(wounded).CoverAlly.IsNone, "recovered member exits screen and clears cover ally");
        var home = TacticalActor(battle, 0, ShipClass.LightCarrier, new(-60, 40), false);
        battle.Store.Get<Brain>(wounded).Home = home;
        battle.Store.Get<Hull>(wounded).Current = battle.Store.Get<Hull>(wounded).Maximum * 0.2f;
        battle.Store.Get<CombatState>(wounded).LastAttacker = threat;
        battle.Store.Get<CombatState>(wounded).LastDamageTick = battle.Tick;
        TacticalDecide(battle, wounded, healthy);
        Require(battle.Store.Get<Brain>(wounded).Mode == BrainMode.Retreat, "critical hull retreats to available home instead of screen");
        battle.Store.Get<Hull>(wounded).Current = battle.Store.Get<Hull>(wounded).Maximum;
        battle.Store.Get<Shield>(wounded).Current = battle.Store.Get<Shield>(wounded).Maximum;
        battle.Store.Get<Heat>(wounded).Value = battle.Store.Get<Heat>(wounded).Limit * 0.95f;
        TacticalDecide(battle, wounded, healthy);
        Require(battle.Store.Get<Brain>(wounded).Mode == BrainMode.Retreat, "critical heat retreats to available home");
        battle.Damage(healthy, 1, false, false, threat);
        Require(battle.Store.Get<CombatState>(healthy).LastAttacker == threat, "enemy damage records attacker");
        battle.Damage(healthy, 1, false, false, wounded);
        Require(battle.Store.Get<CombatState>(healthy).LastAttacker.IsNone, "friendly damage does not refresh an old enemy attribution");
    }

    private static void TacticalCoverRepairChecks()
    {
        using var battle = TacticalBattle();
        var wounded = TacticalActor(battle, 0, ShipClass.Fighter, Vector2.Zero, true);
        var otherWounded = TacticalActor(battle, 0, ShipClass.Fighter, new(6, 0), true);
        var cover = TacticalActor(battle, 0, ShipClass.Fighter, new(12, 0), true);
        var threat = TacticalActor(battle, 1, ShipClass.Frigate, new(-40, 0), false);
        battle.Store.Get<Hull>(wounded).Current *= 0.5f;
        battle.Store.Get<Hull>(otherWounded).Current *= 0.9f;
        foreach (var e in new[] { wounded, otherWounded })
        {
            battle.Store.Get<Shield>(e).Current *= 0.1f;
            battle.Store.Get<Shield>(e).LastHitTick = battle.Tick;
            battle.Store.Get<CombatState>(e).LastAttacker = threat;
            battle.Store.Get<CombatState>(e).LastDamageTick = battle.Tick;
        }
        TacticalDecide(battle, wounded, otherWounded, cover);
        Require(battle.Store.Get<Brain>(wounded).CoverAlly == cover &&
            battle.Store.Get<Brain>(otherWounded).CoverAlly == cover, "cover is not another ally that also needs screening");
        battle.Damage(cover, 100000, false, false);
        battle.Store.Get<Brain>(wounded).NextDecisionTick = battle.Tick + 100;
        battle.Step();
        Require(battle.Store.Get<Brain>(wounded).CoverAlly != cover &&
            battle.Store.Get<Brain>(wounded).Mode != BrainMode.Screen, "dead cover is released before the decision clock");
    }
}
