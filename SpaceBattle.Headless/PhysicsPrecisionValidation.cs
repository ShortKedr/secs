using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void PhysicsPrecisionChecks()
    {
        using (var physics = new PhysicsService(new PhysicsSettings { Restitution = 0, Friction = 0 }))
        {
            var a = KernelDisk(physics, 1, Vector2.Zero, mass: 1, velocity: Vector2.UnitX);
            var b = KernelDisk(physics, 2, new(1, 0), mass: 1);
            var c = KernelDisk(physics, 3, new(3.5f, 0), mass: 1);
            physics.Step(0.0001f);
            Require(physics.GetShip(a).Velocity.X - physics.GetShip(b).Velocity.X < 0.002f,
                "new B-C contact must revisit the coupled A-B velocity constraint");
            KernelNear(physics.GetShip(a).Velocity.X + physics.GetShip(b).Velocity.X + physics.GetShip(c).Velocity.X,
                1, 0.00001f, "coupled contact momentum preserved");
        }
        using (var physics = new PhysicsService(new PhysicsSettings { Restitution = 0, Friction = 0 }))
        {
            KernelDisk(physics, 1, Vector2.Zero, mass: 1, velocity: new(30, 0));
            KernelDisk(physics, 2, new(1, 0), mass: 1);
            KernelDisk(physics, 3, new(3.5f, 0), mass: 1);
            physics.Step(0.0001f);
            var transfer = physics.Contacts.Single(c => c.FirstSerial == 2 && c.SecondSerial == 3);
            KernelNear(transfer.RelativeSpeed, 15, 0.01f,
                "new chain contact samples actual pre-solve velocity after prior impulse, not start-of-tick zero");
        }
        using (var physics = new PhysicsService())
        {
            KernelDisk(physics, 9, Vector2.Zero);
            KernelDisk(physics, 20, new(1000, 1000), velocity: new(1000, 0));
            physics.Step(1f / 60);
            KernelDisk(physics, 3, Vector2.Zero);
            Require(physics.Sweep(new(-6.49602f, 0), new(13.137185f, 0), 0.1f, out var serial, out _) && serial == 3,
                "identical stationary targets use equal temporal subdivision and lower-serial tie after late spawn");
        }
        using (var physics = new PhysicsService())
        {
            KernelDisk(physics, 1, new(4.3596606f, 0), radius: 0.3f);
            Require(physics.SweepRectangle(Vector2.Zero, Vector2.Zero, new(3.6310709f, 1.8155354f),
                -0.4636476f, -1, out _, out _), "rectangle corner bounding radius cannot round inward");
        }
        using (var physics = new PhysicsService())
        {
            const float radius = 0.0001f, y = 1.00005f;
            KernelDisk(physics, 1, Vector2.Zero, radius: radius);
            Require(physics.SweepRectangle(new(-5, y), new(5, y), new(2, 1), 0, -1, out _, out var point),
                "tiny disk rectangle-corner tangency detected");
            var offset = (double)y - 1;
            var expected = new Vector2((float)-Math.Sqrt((double)radius * radius - offset * offset), (float)offset);
            Require(Vector2.Distance(expected, point) <= radius * 0.005f, "contact normal has no arbitrary world-scale cutoff");
        }
        using (var physics = new PhysicsService())
        {
            KernelDisk(physics, 1, Vector2.Zero, radius: 1e20f, mass: 1e-10f);
            Require(physics.Sweep(new(-2e20f, 0), new(2e20f, 0), 0, out _, out var point) &&
                float.IsFinite(point.X) && Math.Abs((double)point.X / 1e20f + 1) < 0.000001,
                "large finite disk impact uses a scale-safe normal instead of overflowing float length");
        }
        using (var battle = KernelBattle())
        {
            var owner = KernelBattleActor(battle, new(-50, 10));
            var victim = KernelBattleActor(battle, Vector2.Zero);
            var round = OwnerRound(battle, owner, WeaponKind.MachineGun, new(-100, 0), new(60000, 0));
            ref var projectile = ref battle.Store.Get<Projectile>(round);
            projectile.Shape = ProjectileShape.Rectangle; projectile.HalfLength = projectile.Radius * 2;
            var hull = battle.Store.Get<Hull>(victim).Current;
            battle.Store.Commit(); battle.Step();
            Require(battle.Counters.Hits == 1 && !battle.Store.Alive(round) && battle.Store.Get<Hull>(victim).Current < hull,
                "fast rectangle hits crossed victim before outside-map retirement");
        }
        using (var physics = new PhysicsService())
        {
            KernelDisk(physics, 1, Vector2.Zero, radius: 1e20f, mass: 1e-10f);
            var second = KernelDisk(physics, 2, new(1.5e20f, 0), radius: 1e20f, mass: 1e-10f);
            physics.Step(1f / 60);
            Require(KernelFinite(physics.GetShip(second).Position) && KernelFinite(physics.GetShip(second).Velocity) &&
                float.IsFinite(physics.GetShip(second).AngularVelocity), "large finite overlapping disks do not overflow contact normalization");
        }
        Console.WriteLine("PASS: coupled new contacts, consistent TOI ties, outward rectangle bounds, tiny/large contact normals and fast outside-map impacts.");
    }
}
