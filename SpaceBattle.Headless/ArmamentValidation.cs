using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void ArmamentChecks()
    {
        ArmamentCatalogChecks();
        ArmamentSpawnChargeChecks();
        ArmamentInitialChargeChecks();
        ArmamentEntityChecks();
        ArmamentPoseChecks();
        ArmamentIndependentChecks();
        ArmamentLaunchChecks();
        ArmamentNeutralCollisionChecks();
        ArmamentHeavyChecks();
        ArmamentCatalogTurretChecks();
        ArmamentEscortImpactChecks();
        ArmamentDockAndDeathChecks();
        Require(ArmamentReplay() == ArmamentReplay(), "mounted armament deterministic replay");
        Console.WriteLine("PASS: armament catalog/eighth-DPS mounts, seeded spawn charge/first-shot deadlines, mount ECS/physics isolation, rotating poses, independent targets/heat/cooldowns, swivel/alignment, safe inherited launch, heavy power/repeat, catalog turret cadence/damage, escort/carrier impact separation, docking/death and replay.");
    }

    private static void ArmamentCatalogChecks()
    {
        Require((int)WeaponKind.HeavyCannon == 4 && ArmamentRules.Revision == "mounted-armament-v7", "armament wire kind and revision");
        Require(TacticalRules.Revision == "layered-squads-v3", "armament uses current tactical revision");
        foreach (var kind in Enum.GetValues<ShipClass>())
        {
            var (count, damage, cooldown) = kind switch
            {
                ShipClass.Frigate => (2, 60f, 720), ShipClass.Destroyer => (2, 80f, 840),
                ShipClass.LightCarrier => (4, 480f, 1080), ShipClass.FleetCarrier => (6, 640f, 1320),
                ShipClass.FoundryCarrier => (8, 800f, 1560), _ => (0, 0f, 0)
            };
            Require(ShipCatalog.IsWeaponsPlatform(kind) == (count > 0) && ShipCatalog.TurretCount(kind) == count, kind + " platform/count");
            var primary = ShipCatalog.Primary(kind);
            if (count == 0)
            {
                var (weaponKind, power, delay, burst, range, speed, heat) = ((int)kind % 4) switch
                {
                    0 => (WeaponKind.MachineGun, 2.5f, 5, 2, 80f, 130f, 1.6f),
                    1 => (WeaponKind.Cannon, 28f, 42, 1, 110f, 100f, 13f),
                    2 => (WeaponKind.Plasma, 13f, 18, 1, 85f, 75f, 7f),
                    _ => (WeaponKind.Missile, 32f, 90, 2, 140f, 65f, 9f)
                };
                Require(primary.Kind == weaponKind && primary.Damage == power && primary.Cooldown == delay &&
                    primary.Burst == burst && primary.Range == range && primary.Speed == speed && primary.HeatPerShot == heat, kind + " unchanged primary");
                continue;
            }
            Require(primary.Kind == WeaponKind.HeavyCannon && primary.Damage == damage && primary.Cooldown == cooldown && primary.Burst == 1, kind + " heavy progression");
            Require(primary.Speed == 170 && primary.Range == (ShipCatalog.IsCarrier(kind) ? 220 : 170) &&
                primary.HeatPerShot == (ShipCatalog.IsCarrier(kind) ? 40 : 25), kind + " unchanged heavy ballistics/heat");
            var kinds = Enumerable.Range(0, count).Select(slot => ShipCatalog.TurretWeapon(kind, slot)).ToArray();
            Require(kinds.All(w => (w.Kind is WeaponKind.Cannon or WeaponKind.Plasma) && w.Damage > 0 && w.Cooldown > 0 && w.Range > 0 && w.Speed > 0), kind + " regular mount weapons");
            Require(kinds.Select(w => w.Kind).Distinct().Count() == 2, kind + " mixed cannon/plasma mounts");
            Require(kinds.All(w => w.Damage == (w.Kind == WeaponKind.Cannon ? 7f : 3.25f) &&
                w.Cooldown == (w.Kind == WeaponKind.Cannon ? 84 : 36)), kind + " quarter-power double-cooldown mounts");
            Require(kinds.All(w => w.Range == (ShipCatalog.IsCarrier(kind) ? 150 : 120) && w.Burst == 1 &&
                w.Speed == (w.Kind == WeaponKind.Cannon ? 100 : 75) &&
                w.HeatPerShot == (w.Kind == WeaponKind.Cannon ? 6.5f : 3.5f)), kind + " unchanged mount ballistics/heat");
            var v5BatteryDps = kinds.Sum(w => w.Kind == WeaponKind.Cannon ? 28d / 42 : 13d / 18) * 60;
            var batteryDps = kinds.Sum(w => (double)w.Damage * w.Burst / w.Cooldown) * 60;
            Require(Math.Abs(batteryDps * 8 - v5BatteryDps) < 0.000001, kind + " mounted battery DPS is one eighth of v5");
        }
        var frigate = ShipCatalog.Primary(ShipClass.Frigate).Damage;
        var destroyer = ShipCatalog.Primary(ShipClass.Destroyer).Damage;
        var lightCarrier = ShipCatalog.Primary(ShipClass.LightCarrier).Damage;
        Require(frigate > 0 && frigate < destroyer && destroyer * 6 <= lightCarrier,
            "escort heavy guns retained below carrier class power");
    }

    private static void ArmamentSpawnChargeChecks()
    {
        foreach (var churn in new[] { 0.5f, 1f, 2f, 8f })
        {
            var first = ArmamentFleetCharge(27, churn);
            Require(first.SequenceEqual(ArmamentFleetCharge(27, churn)), $"same-seed initial fleet charge/placement churn={churn}");
            Require(first.GroupBy(s => s.Kind).All(group => group.Select(s => s.Ready).Distinct().Count() > 1),
                $"initial fleet charge varies within each heavy class churn={churn}");
            var other = ArmamentFleetCharge(28, churn);
            Require(first.Select(s => s.Ready).Where((ready, i) => ready != other[i].Ready).Any(),
                $"different seed changes initial charge churn={churn}");
        }
        // Odd cooldowns round the lower bound upward; the one-tick clamp stays ready after one tick.
        foreach (var cooldown in new[] { 0, 1, 5 })
        {
            var weapon = new Weapon { Kind = WeaponKind.HeavyCannon, Cooldown = cooldown };
            var charges = Enumerable.Range(1, 128).Select(serial => ArmamentRules.InitialHeavyChargeTicks(weapon, 1, 27, serial)).ToArray();
            var maximum = Math.Max(1, cooldown);
            Require(charges.All(charge => charge >= (maximum + 1) / 2 && charge <= maximum) &&
                charges.Min() == (maximum + 1) / 2 && charges.Max() == maximum, "charge rounded-half bounds and clamp");
        }
    }

    private static (int Serial, ShipClass Kind, Vector2 Position, int Ready)[] ArmamentFleetCharge(uint seed, float churn)
    {
        using var battle = new Battle<SkecsStore>(new(), new BattleConfig
        { ShipsPerTeam = 16, Seed = seed, ChurnMultiplier = churn, ParticleMultiplier = 0, ReplenishCarriers = false });
        var frame = new BattleFrame(); battle.Capture(frame);
        var placement = new RandomStream(seed);
        foreach (var ship in frame.Ships.OrderBy(s => s.Team).ThenBy(s => s.Serial))
        {
            Require(battle.TryFindShip(ship.Serial, out var e), "spawn charge ship lookup");
            var weapon = battle.Store.Get<Weapon>(e);
            if (weapon.Kind == WeaponKind.HeavyCannon)
            {
                var maximum = Math.Max(1, (int)(weapon.Cooldown / churn));
                Require(weapon.NextFireTick >= (maximum + 1) / 2 && weapon.NextFireTick <= maximum,
                    $"spawned heavy half/full charge bounds serial={ship.Serial} churn={churn}");
                Require(weapon.NextFireTick == ArmamentExpectedCharge(weapon, churn, seed, ship.Serial),
                    "spawned heavy uses independent seed/serial charge stream");
            }
            else Require(weapon.NextFireTick == 0, "normal spawned primary remains ready at zero");
            if (battle.Store.Has<SecondaryWeapon>(e))
                Require(battle.Store.Get<SecondaryWeapon>(e).Value.NextFireTick == 0, "normal spawned secondary remains ready at zero");
            Require(MountedGuns(battle, e).All(g => battle.Store.Get<Weapon>(g).NextFireTick == 0), "normal spawned turrets remain ready at zero");
            if (!ShipCatalog.IsCarrier((ShipClass)ship.Kind))
            {
                var expected = new Vector2((ship.Team == 0 ? -1 : 1) * placement.Range(40, 72), placement.Range(-65, 65));
                Require(ship.Position == expected, "heavy charge does not consume fleet placement RNG");
            }
        }
        battle.Validate();
        return frame.Ships.Where(s => ShipCatalog.IsWeaponsPlatform((ShipClass)s.Kind)).OrderBy(s => s.Serial)
            .Select(s => { battle.TryFindShip(s.Serial, out var e); return (s.Serial, (ShipClass)s.Kind, s.Position, battle.Store.Get<Weapon>(e).NextFireTick); }).ToArray();
    }

    private static int ArmamentExpectedCharge(Weapon weapon, float churn, uint seed, int serial)
    {
        var maximum = Math.Max(1, (int)(weapon.Cooldown / churn));
        var minimum = (maximum + 1) / 2;
        var stream = new RandomStream(seed ^ unchecked((uint)serial * 0x9E3779B9u) ^ 0xD1B54A35u);
        return minimum + (int)(stream.Next() % (uint)(maximum - minimum + 1));
    }

    private static void ArmamentInitialChargeChecks()
    {
        foreach (var churn in new[] { 0.5f, 1f, 2f, 8f })
        foreach (var kind in new[] { ShipClass.Frigate, ShipClass.Destroyer, ShipClass.LightCarrier, ShipClass.FleetCarrier, ShipClass.FoundryCarrier })
        {
            var first = ArmamentChargeScene(kind, churn);
            Require(first == ArmamentChargeScene(kind, churn), $"fixed-scene charge/deadline replay kind={kind} churn={churn}");
        }
    }

    private static (int Ready, ulong Checksum) ArmamentChargeScene(ShipClass kind, float churn)
    {
        using var battle = ArmamentBattle(churn);
        for (var i = 0; i < 37; i++) battle.Step();
        var birth = battle.Tick;
        // Capture real SpawnShip readiness before the tactical helpers can replace it with int.MaxValue.
        var parent = battle.SpawnShip(0, kind, Vector2.Zero, EntityKey.None);
        battle.Store.Commit();
        var weapon = battle.Store.Get<Weapon>(parent);
        var serial = battle.Store.Get<Identity>(parent).Serial;
        var deadline = weapon.NextFireTick;
        Require(deadline == birth + ArmamentExpectedCharge(weapon, churn, battle.Config.Seed, serial),
            $"late spawn absolute heavy readiness kind={kind} churn={churn}");
        battle.Store.Get<Motion>(parent) = default;
        battle.Store.Get<Brain>(parent) = new Brain { Mode = BrainMode.Patrol, NextDecisionTick = int.MaxValue,
            Target = EntityKey.None, Home = EntityKey.None, SquadLeader = EntityKey.None, CoverAlly = EntityKey.None };
        battle.Store.Get<Inventory>(parent).NextUseTick = int.MaxValue;
        if (battle.Store.Has<Carrier>(parent)) battle.Store.Get<Carrier>(parent).NextBuildTick = int.MaxValue;
        TacticalDurability(battle, parent, 10000);
        var target = ArmamentActor(battle, 1, ShipClass.FleetCarrier, new(80, 0));
        ArmamentAttack(battle, parent, target);
        var guns = MountedGuns(battle, parent);
        foreach (var gun in guns)
        {
            Require(battle.Store.Get<Weapon>(gun).NextFireTick == 0, "late spawned mounted gun initially ready");
            battle.Store.Get<MountedTurret>(gun).Target = target;
            battle.Store.Get<MountedTurret>(gun).NextDecisionTick = int.MaxValue;
            ArmamentAim(battle, gun);
        }
        battle.Step();
        Require(battle.Counters.HeavyShots == 0 && battle.Counters.TurretShots == guns.Count &&
            battle.Store.Get<Weapon>(parent).NextFireTick == deadline, "regular mounted guns fire while heavy charges");
        foreach (var gun in guns)
        {
            var regular = battle.Store.Get<Weapon>(gun);
            Require(regular.NextFireTick == battle.Tick + Math.Max(1, (int)(regular.Cooldown / churn)),
                "charging scene retains regular mounted churn scaling");
        }
        FreezeMountedGuns(battle, parent);
        while (battle.Tick < deadline - 1)
        {
            battle.Store.Get<Transform>(parent).Angle = 0;
            battle.Step();
            Require(battle.Counters.HeavyShots == 0 && battle.Store.Get<Weapon>(parent).NextFireTick == deadline,
                "heavy with aligned in-range target blocked until initial deadline");
        }
        battle.Store.Get<Transform>(parent).Angle = 0;
        battle.Step();
        Require(battle.Tick == deadline && battle.Counters.HeavyShots == 1 &&
            battle.Store.Get<Weapon>(parent).NextFireTick == deadline + Math.Max(1, (int)(weapon.Cooldown / churn)),
            "heavy first shot exactly at initial deadline then full churn-scaled cooldown");
        Require(ArmamentProjectiles(battle).Count(e => battle.Store.Get<Projectile>(e).Kind == WeaponKind.HeavyCannon &&
            battle.Store.Get<Projectile>(e).Owner == parent) == 1, "initial deadline emits actual heavy projectile");
        battle.Validate();
        return (deadline, battle.Checksum());
    }

    private static void ArmamentEntityChecks()
    {
        using var battle = new Battle<SkecsStore>(new(), new BattleConfig { ShipsPerTeam = 16, Seed = 27 });
        var frame = new BattleFrame(); battle.Capture(frame);
        var expected = frame.Ships.Sum(s => ShipCatalog.TurretCount((ShipClass)s.Kind));
        var mounts = MountedGuns(battle);
        Require(mounts.Count == expected && battle.TurretCount == expected && frame.Turrets.Count == expected, "all mounts counted/rendered");
        Require(battle.Store.Diagnostics().Entities == battle.ShipCount + expected, "mounts are separate ECS entities");
        Require(battle.PhysicsBodies == battle.ShipCount && battle.PhysicsFixtures == frame.Ships.Sum(s => ShipCatalog.Shapes((ShipClass)s.Kind)), "mounts create no pseudo-ships or physics fixtures");
        var serials = new HashSet<int>(frame.Ships.Select(s => s.Serial));
        foreach (var ship in frame.Ships)
        {
            Require(battle.TryFindShip(ship.Serial, out var parent), "armament parent lookup");
            var guns = MountedGuns(battle, parent);
            Require(guns.Select(e => battle.Store.Get<MountedTurret>(e).Slot).SequenceEqual(Enumerable.Range(0, ShipCatalog.TurretCount((ShipClass)ship.Kind))), "contiguous mount slots per parent");
            if (ShipCatalog.IsWeaponsPlatform((ShipClass)ship.Kind)) Require(!battle.Store.Has<SecondaryWeapon>(parent), "platform has no legacy secondary");
            else Require(battle.Store.Has<SecondaryWeapon>(parent) == ((ShipClass)ship.Kind >= ShipClass.Corvette), "nonplatform secondary unchanged");
        }
        foreach (var gun in mounts)
        {
            var mount = battle.Store.Get<MountedTurret>(gun);
            Require(serials.Add(mount.Serial) && !battle.TryFindShip(mount.Serial, out _), "mount serial unique and not a ship");
            Require(battle.Store.Has<Transform>(gun) && battle.Store.Has<Weapon>(gun) && battle.Store.Has<Heat>(gun), "mount required components");
            Require(!battle.Store.Has<Identity>(gun) && !battle.Store.Has<Brain>(gun) && !battle.Store.Has<Motion>(gun) &&
                !battle.Store.Has<Collider>(gun) && !battle.Store.Has<PhysicsLink>(gun) && !battle.Store.Has<Hull>(gun) &&
                !battle.Store.Has<Shield>(gun) && !battle.Store.Has<Inventory>(gun) && !battle.Store.Has<SecondaryWeapon>(gun) &&
                !battle.Store.Has<Carrier>(gun) && !battle.Store.Has<CombatState>(gun) && !battle.Store.Has<Projectile>(gun) &&
                !battle.Store.Has<Debris>(gun) && !battle.Store.Has<Particle>(gun) && !battle.Store.Has<Lifetime>(gun) &&
                !battle.Store.Has<Docked>(gun) && !battle.Store.Has<Overheated>(gun) && !battle.Store.Has<InCombat>(gun) &&
                !battle.Store.Has<Boosting>(gun) && !battle.Store.Has<ShieldBroken>(gun), "mount has only its four components");
            var heat = battle.Store.Get<Heat>(gun);
            Require(heat.Value == 0 && heat.Limit == 60 && heat.CoolingPerSecond == 12, "mount owns initial thermal budget");
            var render = frame.Turrets.Single(t => t.Serial == mount.Serial);
            var parent = battle.Store.Get<Identity>(mount.Parent);
            Require(render.ParentSerial == parent.Serial && render.Team == parent.Team && render.Kind == battle.Store.Get<Weapon>(gun).Kind &&
                render.Radius > 0 && float.IsFinite(render.Angle + render.Heat + render.Recoil), "render mount attribution/state");
        }
        battle.Validate();
    }

    private static void ArmamentPoseChecks()
    {
        using var battle = ArmamentBattle();
        var parent = ArmamentActor(battle, 0, ShipClass.Destroyer, new(-20, -15));
        battle.Store.Get<Transform>(parent).Angle = 0.7f;
        battle.Store.Get<Motion>(parent) = new Motion { Velocity = new(8, 4), MaxSpeed = 20, AngularVelocity = 0.5f };
        var start = battle.Store.Get<Transform>(parent);
        for (var i = 0; i < 12; i++)
        {
            battle.Step();
            var pose = battle.Store.Get<Transform>(parent);
            var frame = new BattleFrame(); battle.Capture(frame);
            foreach (var gun in MountedGuns(battle, parent))
            {
                var mount = battle.Store.Get<MountedTurret>(gun);
                var expected = pose.Position + ArmamentRotate(mount.LocalOffset, pose.Angle);
                Require(Vector2.Distance(battle.Store.Get<Transform>(gun).Position, expected) < 0.001f, "mount follows rotated moving parent after physics");
                Require(Vector2.Distance(frame.Turrets.Single(t => t.Serial == mount.Serial).Position, expected) < 0.001f, "render uses synchronized mount pose");
            }
        }
        Require(Vector2.Distance(start.Position, battle.Store.Get<Transform>(parent).Position) > 1 &&
            MathF.Abs(start.Angle - battle.Store.Get<Transform>(parent).Angle) > 0.001f, "pose scene actually translates and rotates");
        Require(battle.Counters.Shots == 0, "frozen mount pose scene has no fire");
        battle.Validate();
    }

    private static void ArmamentIndependentChecks()
    {
        using var battle = ArmamentBattle();
        var parent = ArmamentActor(battle, 0, ShipClass.Frigate, Vector2.Zero);
        var left = ArmamentActor(battle, 1, ShipClass.FleetCarrier, new(-60, 0));
        var above = ArmamentActor(battle, 1, ShipClass.FleetCarrier, new(0, 60));
        ArmamentAttack(battle, parent, left);
        var guns = MountedGuns(battle, parent);
        ArmamentGun(battle, guns[0], left, 6, 0);
        ArmamentGun(battle, guns[1], above, 11, 4);
        battle.Store.Get<Heat>(guns[1]).Value = 100;
        ArmamentAim(battle, guns[0]); ArmamentAim(battle, guns[1]);
        battle.Step();
        Require(battle.Store.Get<MountedTurret>(guns[0]).LastFireTick == 1 && battle.Store.Get<Weapon>(guns[0]).NextFireTick == 7, "first independent cooldown");
        Require(battle.Store.Get<Weapon>(guns[1]).NextFireTick == 4 && battle.Store.Get<MountedTurret>(guns[1]).LastFireTick < 0, "other hot gun does not fire or consume cooldown");
        Require(battle.Store.Get<Heat>(guns[0]).Value == 2 && battle.Store.Get<Heat>(guns[1]).Value == 100 && battle.Store.Get<Heat>(parent).Value == 0, "mount heat is independent of sibling and parent");
        battle.Store.Get<Heat>(guns[1]).Value = 0;
        for (var tick = 2; tick <= 7; tick++)
        {
            ArmamentAim(battle, guns[0]); ArmamentAim(battle, guns[1]); battle.Step();
            Require(battle.Counters.Shots == (tick < 4 ? 1 : tick < 7 ? 2 : 3), "independent exact cooldown schedule");
            Require(battle.Store.Get<MountedTurret>(guns[0]).Target == left && battle.Store.Get<MountedTurret>(guns[1]).Target == above &&
                battle.Store.Get<Brain>(parent).Target == left, "sticky gun target differs from parent target");
        }
        Require(battle.Store.Get<Weapon>(guns[0]).NextFireTick == 13 && battle.Store.Get<Weapon>(guns[1]).NextFireTick == 15, "sibling cooldowns remain independent");
        FreezeMountedGuns(battle, parent);
        battle.Store.Get<Brain>(parent).Target = EntityKey.None;
        battle.Store.Get<MountedTurret>(guns[0]).Target = EntityKey.None;
        battle.Store.Get<MountedTurret>(guns[0]).NextDecisionTick = 0;
        battle.Step();
        Require(!battle.Store.Get<MountedTurret>(guns[0]).Target.IsNone && battle.Store.Get<Brain>(parent).Target.IsNone,
            "turret acquires enemy despite frozen parent decision and no parent target");

        // A rear target requires full swivel; readiness alone must not consume a shot.
        ArmamentGun(battle, guns[0], left, 1000, 0);
        battle.Store.Get<Transform>(guns[0]).Angle = 0;
        var shots = battle.Counters.Shots;
        battle.Step();
        Require(battle.Counters.Shots == shots && battle.Store.Get<Weapon>(guns[0]).NextFireTick == 0, "misaligned turret withholds fire");
        for (var i = 0; i < 50 && battle.Counters.Shots == shots; i++) battle.Step();
        Require(battle.Counters.Shots == shots + 1, "full swivel reaches rear target without turning parent");
        var angle = battle.Store.Get<Transform>(guns[0]).Angle;
        var direct = battle.Store.Get<Transform>(left).Position - battle.Store.Get<Transform>(guns[0]).Position;
        Require(MathF.Abs(MathF.IEEERemainder(angle - MathF.Atan2(direct.Y, direct.X), MathF.Tau)) <= 0.08f, "turret fires only within alignment tolerance");
    }

    private static void ArmamentLaunchChecks()
    {
        using var battle = ArmamentBattle();
        var parent = ArmamentActor(battle, 0, ShipClass.FleetCarrier, Vector2.Zero);
        var target = ArmamentActor(battle, 1, ShipClass.FleetCarrier, new(-70, 0));
        ArmamentAttack(battle, parent, target);
        battle.Store.Get<Motion>(parent) = new Motion { Velocity = new(0, 12), MaxSpeed = 20 };
        var gun = MountedGuns(battle, parent)[0];
        ArmamentGun(battle, gun, target, 1000, 0);
        // Cannon fragments can overwrite LastAttacker in the same hit pass; plasma isolates shot attribution.
        battle.Store.Get<Weapon>(gun).Kind = WeaponKind.Plasma;
        battle.Store.Get<Transform>(gun).Angle = MathF.PI;
        var origin = battle.Store.Get<Transform>(parent).Position;
        var offset = ArmamentRotate(battle.Store.Get<MountedTurret>(gun).LocalOffset, battle.Store.Get<Transform>(parent).Angle);
        for (var i = 0; i < 20 && battle.Counters.Shots == 0; i++)
        {
            origin = battle.Store.Get<Transform>(parent).Position;
            offset = ArmamentRotate(battle.Store.Get<MountedTurret>(gun).LocalOffset, battle.Store.Get<Transform>(parent).Angle);
            battle.Step();
        }
        var projectile = ArmamentProjectiles(battle).Single();
        var shot = battle.Store.Get<Projectile>(projectile);
        var pose = battle.Store.Get<Transform>(projectile);
        var motion = battle.Store.Get<Motion>(parent);
        var inherited = motion.Velocity + new Vector2(-offset.Y, offset.X) * motion.AngularVelocity;
        var relative = battle.Store.Get<Motion>(projectile).Velocity - inherited;
        Require(shot.Owner == parent && shot.Target == target, "turret projectile owner is parent, not gun");
        Require(MathF.Abs(relative.Length() - battle.Store.Get<Weapon>(gun).Speed) < 0.002f, "projectile inherits parent mountpoint velocity");
        var mount = battle.Store.Get<MountedTurret>(gun);
        var spread = FireControl.Spread(battle.Config.Seed, mount.Serial, mount.LastFireTick, mount.Slot + 2, out var imprecise);
        var shotAngle = battle.Store.Get<Transform>(gun).Angle + spread;
        Require(Vector2.Distance(Vector2.Normalize(relative), new(MathF.Cos(shotAngle), MathF.Sin(shotAngle))) < 0.0001f &&
            battle.Counters.AimDecisions == 1 && battle.Counters.ImpreciseAimDecisions == (imprecise ? 1 : 0), "turret uses normal deterministic spread and one aim decision");
        var radius = battle.Store.Get<Collider>(parent).Radius;
        Require(Vector2.Distance(pose.Previous, origin) > radius + shot.Radius, "ray-circle exit puts rear shot outside parent hull");
        Require(Vector2.Distance(pose.Previous - (origin + offset), Vector2.Normalize(relative) * Vector2.Distance(pose.Previous, origin + offset)) < 0.002f, "muzzle stays on actual firing ray");
        Require(battle.Counters.SelfHits == 0, "safe muzzle prevents immediate self collision");
        var frame = new BattleFrame(); battle.Capture(frame);
        Require(frame.Turrets.Single(t => t.Serial == battle.Store.Get<MountedTurret>(gun).Serial).Recoil > 0, "actual turret shot renders recoil");
        for (var i = 0; i < 90 && battle.Counters.Hits == 0; i++) battle.Step();
        Require(battle.Counters.Hits == 1 && battle.Store.Get<CombatState>(target).LastAttacker == parent,
            $"turret damage credits parent ship (hits={battle.Counters.Hits}, attacker={battle.Store.Get<CombatState>(target).LastAttacker}, parent={parent})");
    }

    private static void ArmamentNeutralCollisionChecks()
    {
        using var battle = ArmamentBattle();
        var parent = ArmamentActor(battle, 0, ShipClass.Frigate, Vector2.Zero);
        var target = ArmamentActor(battle, 1, ShipClass.FleetCarrier, new(80, 0));
        var blocker = ArmamentActor(battle, 0, ShipClass.FleetCarrier, new(40, 0));
        ArmamentAttack(battle, parent, target);
        var gun = MountedGuns(battle, parent)[0];
        ArmamentGun(battle, gun, target, 1000, 0);
        battle.Store.Get<Weapon>(gun).Kind = WeaponKind.Plasma;
        ArmamentAim(battle, gun);
        for (var i = 0; i < 60 && battle.Counters.Hits == 0; i++) battle.Step();
        Require(battle.Counters.Hits == 1 && battle.Counters.FriendlyFireHits == 1 && battle.Counters.SelfHits == 0,
            "mounted projectiles preserve neutral friendly collision");
        Require(battle.Store.Get<Hull>(blocker).Current == 9999 && battle.Store.Get<Hull>(target).Current == 10000,
            "turret damages actual blocker, not telemetry target");
    }

    private static void ArmamentHeavyChecks()
    {
        foreach (var kind in new[] { ShipClass.Frigate, ShipClass.Destroyer, ShipClass.LightCarrier, ShipClass.FleetCarrier, ShipClass.FoundryCarrier })
        {
            using var battle = ArmamentBattle(2);
            var parent = ArmamentActor(battle, 0, kind, Vector2.Zero);
            var target = ArmamentActor(battle, 1, ShipClass.FleetCarrier, new(80, 0));
            ArmamentAttack(battle, parent, target);
            battle.Store.Get<Transform>(parent).Angle = 0.3f;
            battle.Store.Get<Weapon>(parent).NextFireTick = 0;
            var before = battle.Store.Get<Hull>(target).Current;
            battle.Step();
            Require(battle.Counters.Shots == 0 && battle.Store.Get<Weapon>(parent).NextFireTick == 0, kind + " heavy blocks off-axis fire");
            battle.Store.Get<Transform>(parent).Angle = 0.1f;
            battle.Step();
            var weapon = battle.Store.Get<Weapon>(parent);
            var fired = battle.Tick;
            var deadline = fired + Math.Max(1, (int)(weapon.Cooldown / battle.Config.ChurnMultiplier));
            Require(battle.Counters.Shots == 1 && weapon.NextFireTick == deadline, kind + " aligned heavy single shot and churn-scaled cooldown");
            var projectile = ArmamentProjectiles(battle).Single();
            var shot = battle.Store.Get<Projectile>(projectile);
            Require(shot.Kind == WeaponKind.HeavyCannon && shot.Damage == ShipCatalog.Primary(kind).Damage && shot.Owner == parent, kind + " actual heavy projectile power");
            while (battle.Counters.Hits == 0 && battle.Tick < fired + 90)
            {
                battle.Store.Get<Transform>(parent).Angle = 0; battle.Step();
            }
            Require(battle.Counters.Hits == 1 && MathF.Abs(before - battle.Store.Get<Hull>(target).Current - weapon.Damage * 1.5f) < 0.01f, kind + " heavy impact applies specialized unarmored power");
            while (battle.Tick < deadline - 1)
            {
                battle.Store.Get<Transform>(parent).Angle = 0; battle.Step();
                Require(battle.Counters.Shots == 1, kind + " heavy cannot fire early");
            }
            battle.Store.Get<Transform>(parent).Angle = 0; battle.Step();
            Require(battle.Tick == deadline && battle.Counters.Shots == 2 && battle.Store.Get<Weapon>(parent).NextFireTick == deadline + deadline - fired,
                kind + " heavy repeats exactly at cooldown deadline");
            battle.Validate();
        }
    }

    private static void ArmamentCatalogTurretChecks()
    {
        foreach (var churn in new[] { 0.5f, 1f, 2f })
        foreach (var kind in new[] { ShipClass.Frigate, ShipClass.Destroyer, ShipClass.LightCarrier, ShipClass.FleetCarrier, ShipClass.FoundryCarrier })
        foreach (var weaponKind in new[] { WeaponKind.Cannon, WeaponKind.Plasma })
        {
            var checksum = ArmamentCatalogTurretScene(kind, weaponKind, churn);
            Require(checksum == ArmamentCatalogTurretScene(kind, weaponKind, churn),
                $"catalog turret cadence/impact deterministic replay kind={kind} weapon={weaponKind} churn={churn}");
        }
    }

    private static ulong ArmamentCatalogTurretScene(ShipClass kind, WeaponKind weaponKind, float churn)
    {
        using var battle = ArmamentBattle(churn);
        var parent = ArmamentActor(battle, 0, kind, Vector2.Zero);
        var target = ArmamentActor(battle, 1, ShipClass.FleetCarrier, new(30, 0));
        ArmamentAttack(battle, parent, target);
        var guns = MountedGuns(battle, parent);
        var gun = guns.First(g => battle.Store.Get<Weapon>(g).Kind == weaponKind);
        var mount = battle.Store.Get<MountedTurret>(gun);
        var catalog = ShipCatalog.TurretWeapon(kind, mount.Slot);
        var damage = weaponKind == WeaponKind.Cannon ? 7f : 3.25f;
        var interval = (int)((weaponKind == WeaponKind.Cannon ? 84 : 36) / churn);
        Require(battle.Store.Get<Weapon>(gun).Equals(catalog with { NextFireTick = int.MaxValue }),
            "spawned isolated turret retains every catalog weapon field");
        // Absorb exactly one cannon shell; armor rejects its same-tick and later fragments.
        if (weaponKind == WeaponKind.Cannon)
        {
            TacticalDurability(battle, target, 10000, damage, 1);
            TacticalDurability(battle, parent, 10000, 0, 1);
        }
        battle.Store.Get<Weapon>(gun).NextFireTick = 0;
        battle.Store.Get<MountedTurret>(gun).Target = target;
        battle.Store.Get<MountedTurret>(gun).NextDecisionTick = int.MaxValue;
        ArmamentAim(battle, gun);
        var context = $"kind={kind} weapon={weaponKind} churn={churn}";
        var impacts = 0;
        // Include the third impact but stop before the fourth catalog deadline.
        for (var tick = 1; tick < 1 + 3 * interval; tick++)
        {
            battle.Step();
            var shots = 1 + (tick - 1) / interval;
            var fired = 1 + (shots - 1) * interval;
            Require(battle.Counters.Shots == shots && battle.Counters.TurretShots == shots && battle.Counters.HeavyShots == 0 &&
                battle.Store.Get<Weapon>(gun).NextFireTick == fired + interval &&
                battle.Store.Get<MountedTurret>(gun).LastFireTick == fired,
                $"catalog turret exact cadence/no early shots tick={tick} {context}");
            if (tick == fired)
            {
                var projectile = ArmamentProjectiles(battle).Single();
                var shot = battle.Store.Get<Projectile>(projectile);
                Require(shot.Kind == weaponKind && shot.Damage == damage && shot.Owner == parent && shot.Target == target &&
                    MathF.Abs(battle.Store.Get<Motion>(projectile).Velocity.Length() - catalog.Speed) < 0.002f,
                    $"real catalog turret projectile damage/ballistics {context}");
            }
            Require(battle.Counters.Hits == impacts || battle.Counters.Hits == impacts + 1,
                $"catalog turret isolated single impacts {context}");
            impacts = (int)battle.Counters.Hits;
            Require(impacts <= shots && (tick != fired || impacts == shots - 1),
                $"catalog turret impact follows each launch {context}");
            if (tick == fired + interval - 1)
                Require(impacts == shots, $"catalog turret each shot impacts before next deadline {context}");
            var hull = battle.Store.Get<Hull>(target).Current;
            if (weaponKind == WeaponKind.Cannon)
                Require(hull == 10000 && battle.Store.Get<Shield>(target).Current == (impacts == 0 ? damage : 0),
                    $"catalog cannon applies exactly seven direct shield damage without fragment contamination {context}");
            else
                Require(MathF.Abs(10000 - hull - impacts * damage) < 0.001f && battle.Counters.PlasmaShieldHits == 0,
                    $"catalog plasma applies exact unshielded impact damage {context}");
            Require(battle.Store.Get<Weapon>(parent).NextFireTick == int.MaxValue &&
                guns.Where(g => g != gun).All(g => battle.Store.Get<Weapon>(g).NextFireTick == int.MaxValue) &&
                battle.Store.Get<Hull>(parent).Current == 10000 && battle.Counters.SelfHits == 0 && battle.Counters.FriendlyFireHits == 0,
                $"catalog turret scene keeps main/sibling guns frozen and damage isolated {context}");
        }
        Require(impacts == 3, $"catalog turret three real impacts {context}");
        battle.Validate();
        return battle.Checksum();
    }

    private static void ArmamentEscortImpactChecks()
    {
        foreach (var kind in new[] { ShipClass.Frigate, ShipClass.Destroyer, ShipClass.LightCarrier, ShipClass.FleetCarrier, ShipClass.FoundryCarrier })
        {
            using var battle = ArmamentBattle();
            var parent = ArmamentActor(battle, 0, kind, Vector2.Zero);
            var target = TacticalActor(battle, 1, ShipClass.Frigate, new(80, 0), false);
            // The same damaged, unshielded target must survive escorts but not carrier guns.
            battle.Store.Get<Hull>(target).Current = 180;
            battle.Store.Get<Shield>(target).Current = 0;
            var armor = battle.Store.Get<Hull>(target).Armor;
            ArmamentAttack(battle, parent, target);
            battle.Store.Get<Weapon>(parent).NextFireTick = 0;
            for (var i = 0; i < 90 && battle.Counters.Hits == 0; i++) battle.Step();
            Require(battle.Counters.Shots == 1 && battle.Counters.Hits == 1, kind + " single real heavy impact on damaged frigate");
            Require(battle.Store.Alive(target) != ShipCatalog.IsCarrier(kind), kind + " escort/carrier kill separation");
            if (battle.Store.Alive(target))
            {
                var expected = 180 - ShipCatalog.Primary(kind).Damage * 1.5f * (1 - armor);
                Require(MathF.Abs(battle.Store.Get<Hull>(target).Current - expected) < 0.01f,
                    kind + " reduced heavy power still applies armored hull damage");
            }
            battle.Validate();
        }
    }

    private static void ArmamentDockAndDeathChecks()
    {
        using var battle = ArmamentBattle();
        var home = ArmamentActor(battle, 0, ShipClass.LightCarrier, new(-60, 0));
        var parent = ArmamentActor(battle, 0, ShipClass.Frigate, Vector2.Zero);
        var target = ArmamentActor(battle, 1, ShipClass.FleetCarrier, new(60, 0));
        ArmamentAttack(battle, parent, target);
        var guns = MountedGuns(battle, parent);
        foreach (var gun in guns) { ArmamentGun(battle, gun, target, 10, 0); ArmamentAim(battle, gun); }
        battle.Store.Get<Weapon>(parent).NextFireTick = 0;
        battle.Store.Set(parent, new Docked { Carrier = home, ReleaseTick = int.MaxValue }); battle.Store.Commit();
        var frame = new BattleFrame(); battle.Capture(frame);
        var serial = battle.Store.Get<Identity>(parent).Serial;
        Require(frame.Turrets.All(t => t.ParentSerial != serial), "docking immediately hides parent guns");
        for (var i = 0; i < 8; i++) battle.Step();
        Require(battle.Counters.Shots == 0 && guns.All(g => battle.Store.Get<Weapon>(g).NextFireTick == 0 && battle.Store.Get<MountedTurret>(g).LastFireTick < 0), "docked ready guns do not fire or consume cooldown");
        battle.Capture(frame);
        Require(frame.Turrets.All(t => t.ParentSerial != serial) && guns.All(battle.Store.Alive), "docking hides but preserves ECS mounts");
        FreezeMountedGuns(battle, parent);
        var count = battle.TurretCount; var bodies = battle.PhysicsBodies; var fixtures = battle.PhysicsFixtures;
        battle.Damage(parent, 1000000, false, false);
        battle.Step(); battle.Capture(frame);
        Require(!battle.Store.Alive(parent) && guns.All(g => !battle.Store.Alive(g)) && MountedGuns(battle, parent).Count == 0, "parent death destroys all mounts in same flush");
        Require(battle.TurretCount == count - guns.Count && frame.Turrets.All(t => t.ParentSerial != serial), "parent death removes render/count mounts");
        Require(battle.PhysicsBodies == bodies - 1 && battle.PhysicsFixtures == fixtures - ShipCatalog.Shapes(ShipClass.Frigate), "parent death leaves no orphan physics bodies");
        battle.Validate();
    }

    private static ulong ArmamentReplay()
    {
        using var battle = ArmamentBattle();
        var parent = ArmamentActor(battle, 0, ShipClass.Destroyer, Vector2.Zero);
        var target = ArmamentActor(battle, 1, ShipClass.FleetCarrier, new(70, 0));
        ArmamentAttack(battle, parent, target);
        foreach (var gun in MountedGuns(battle, parent)) { ArmamentGun(battle, gun, target, 17, 0); ArmamentAim(battle, gun); }
        for (var i = 0; i < 120; i++) battle.Step();
        Require(battle.Counters.Shots > 0 && battle.Counters.Hits > 0, "armament replay exercises real turret shots/hits");
        battle.Validate();
        return battle.Checksum();
    }

    private static Battle<SkecsStore> ArmamentBattle(float churn = 1)
    {
        var battle = new Battle<SkecsStore>(new(), new BattleConfig { ShipsPerTeam = 8, Seed = 27, ChurnMultiplier = churn, ParticleMultiplier = 0, ReplenishCarriers = false });
        var frame = FreezeBattle(battle);
        for (var i = 0; i < frame.Ships.Count; i++)
        {
            battle.TryFindShip(frame.Ships[i].Serial, out var e);
            var position = new Vector2(1000 + i * 40, 1000);
            battle.Store.Get<Transform>(e) = new Transform { Position = position, Previous = position };
        }
        FreezeMountedGuns(battle);
        return battle;
    }

    private static EntityKey ArmamentActor(Battle<SkecsStore> battle, int team, ShipClass kind, Vector2 position)
    {
        var e = TacticalActor(battle, team, kind, position, false);
        battle.Store.Get<Motion>(e) = default;
        battle.Store.Get<Transform>(e).Angle = 0;
        TacticalDurability(battle, e, 10000);
        return e;
    }

    private static void ArmamentAttack(Battle<SkecsStore> battle, EntityKey parent, EntityKey target)
    {
        battle.Store.Get<Brain>(parent).Mode = BrainMode.Attack;
        battle.Store.Get<Brain>(parent).Target = target;
        battle.Store.Get<Brain>(parent).TargetLockUntilTick = int.MaxValue;
    }

    private static void ArmamentGun(Battle<SkecsStore> battle, EntityKey gun, EntityKey target, int cooldown, int ready)
    {
        battle.Store.Get<Weapon>(gun) = new Weapon { Kind = WeaponKind.Cannon, Burst = 1, Damage = 1, Speed = 120, Range = 200, HeatPerShot = 2, Cooldown = cooldown, NextFireTick = ready };
        battle.Store.Get<Heat>(gun) = new Heat { Limit = 100 };
        battle.Store.Get<MountedTurret>(gun).Target = target;
        battle.Store.Get<MountedTurret>(gun).NextDecisionTick = int.MaxValue;
    }

    private static void ArmamentAim(Battle<SkecsStore> battle, EntityKey gun)
    {
        var mount = battle.Store.Get<MountedTurret>(gun);
        var parent = battle.Store.Get<Transform>(mount.Parent);
        var position = parent.Position + ArmamentRotate(mount.LocalOffset, parent.Angle);
        var direction = battle.Store.Get<Transform>(mount.Target).Position - position;
        battle.Store.Get<Transform>(gun).Angle = MathF.Atan2(direction.Y, direction.X);
    }

    private static Vector2 ArmamentRotate(Vector2 v, float angle) => new(v.X * MathF.Cos(angle) - v.Y * MathF.Sin(angle), v.X * MathF.Sin(angle) + v.Y * MathF.Cos(angle));

    private static List<EntityKey> MountedGuns(Battle<SkecsStore> battle, EntityKey? parent = null)
    {
        var query = new ArmamentMountCollector { Parent = parent, Entities = new() };
        battle.Store.ForEach<MountedTurret, ArmamentMountCollector>(ref query);
        query.Entities.Sort((a, b) => battle.Store.Get<MountedTurret>(a).Slot.CompareTo(battle.Store.Get<MountedTurret>(b).Slot));
        return query.Entities;
    }

    private static void FreezeMountedGuns(Battle<SkecsStore> battle, EntityKey? parent = null)
    {
        foreach (var gun in MountedGuns(battle, parent)) battle.Store.Get<Weapon>(gun).NextFireTick = int.MaxValue;
    }

    private struct ArmamentMountCollector : IProcessor<MountedTurret>
    {
        public EntityKey? Parent;
        public List<EntityKey> Entities;
        public void Execute(EntityKey e, ref MountedTurret mount) { if (!Parent.HasValue || mount.Parent == Parent.Value) Entities.Add(e); }
    }

    private static List<EntityKey> ArmamentProjectiles(Battle<SkecsStore> battle)
    {
        var query = new ArmamentProjectileCollector { Entities = new() };
        battle.Store.ForEach<Projectile, ArmamentProjectileCollector>(ref query);
        return query.Entities;
    }

    private struct ArmamentProjectileCollector : IProcessor<Projectile>
    {
        public List<EntityKey> Entities;
        public void Execute(EntityKey e, ref Projectile projectile) => Entities.Add(e);
    }
}
