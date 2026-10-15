using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void ColliderChecks()
    {
        using (var battle = KernelBattle())
        {
            var actor = KernelBattleActor(battle, new(5, 9));
            var serial = battle.Store.Get<Identity>(actor).Serial;
            var round = OwnerRound(battle, actor, WeaponKind.MachineGun, new(3, -7), Vector2.Zero);
            battle.Store.Get<Transform>(round).Angle = 0.73f;
            var fragment = battle.Store.Create();
            battle.Store.Set(fragment, new Transform { Position = new(-11, 4), Angle = 0.2f });
            battle.Store.Set(fragment, new Motion());
            battle.Store.Set(fragment, new Debris { Serial = 1000001, Radius = 1.75f, ExpiresTick = 100 });
            var particle = battle.Store.Create();
            battle.Store.Set(particle, new Transform()); battle.Store.Set(particle, new Motion());
            battle.Store.Set(particle, new Particle { Serial = 1000002, Size = 20, ExpiresTick = 100 });
            battle.Store.Commit();
            var rows = new List<DebugCollider>();
            var before = battle.Checksum();
            battle.CaptureColliders(rows);
            Require(before == battle.Checksum(), "collider inspection is read-only");
            var ship = rows.Single(r => r.Serial == serial);
            Require(ship.Kind == DebugColliderKind.Ship && ship.Shape == ProjectileShape.Disk &&
                ship.Position == new Vector2(5, 9) && ship.Radius == battle.Store.Get<Collider>(actor).Radius,
                "ship collider uses real world center and physical radius");
            var disk = rows.Single(r => r.Serial == 1000000);
            Require(disk.Kind == DebugColliderKind.Projectile && disk.Shape == ProjectileShape.Disk &&
                disk.Position == new Vector2(3, -7) && disk.Angle == 0.73f && disk.Radius == 0.12f,
                "shape comes from projectile component, not weapon kind or visual trail");
            battle.Store.Get<Projectile>(round).Shape = ProjectileShape.Rectangle;
            battle.Store.Get<Projectile>(round).HalfLength = 7.25f;
            battle.CaptureColliders(rows, refreshEntities: false);
            var rectangle = rows.Single(r => r.Serial == 1000000);
            Require(rectangle.Shape == ProjectileShape.Rectangle && rectangle.HalfExtents == new Vector2(7.25f, 0.12f),
                "rectangular collider exports actual half length and thickness");
            Require(rows.Single(r => r.Serial == 1000001).Radius == 1.75f &&
                rows.All(r => r.Serial != 1000002),
                "only ships, projectiles and debris: no particle geometry");
            var frame = new BattleFrame(); battle.Capture(frame);
            Require(rows.Count == frame.Ships.Count + frame.Projectiles.Count + frame.Debris.Count && frame.Turrets.Count > 0,
                "mounted guns have no independent physical geometry");
            battle.Store.Set(actor, new Docked()); battle.Store.Commit();
            battle.CaptureColliders(rows);
            Require(rows.All(r => r.Serial != serial), "docked ship collider disabled");
            battle.CaptureColliders(rows, refreshEntities: false);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) battle.CaptureColliders(rows, refreshEntities: false);
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Require(allocated == 0, $"warmed reusable collider capture allocates zero bytes, got {allocated}");
        }
        ColliderReplay(static () => new SkecsStore());
        ColliderReplay(static () => new LeoEcsLiteStore());
        ColliderReplay(static () => new LeoEcsClassicStore());
        ColliderReplay(static () => new MorpehStore());
        ColliderReplay(static () => new StaticEcsStore());
        Console.WriteLine("PASS: exact collider geometry, docking/particle/turret exclusions, zero-allocation warmed capture and replay neutrality on five stores.");
    }

    private static void ColliderReplay<T>(Func<T> factory) where T : IEcsStore
    {
        var config = new BattleConfig { ShipsPerTeam = 16, Seed = 27 };
        ulong expected;
        using (var plain = new Battle<T>(factory(), config))
        {
            for (var i = 0; i < 300; i++) plain.Step();
            expected = plain.Checksum();
        }
        using var debug = new Battle<T>(factory(), config);
        var rows = new List<DebugCollider>();
        for (var i = 0; i < 300; i++) { debug.Step(); debug.CaptureColliders(rows, refreshEntities: false); }
        Require(debug.Checksum() == expected, "collider debug leaves replay unchanged: " + debug.Framework);
    }
}
