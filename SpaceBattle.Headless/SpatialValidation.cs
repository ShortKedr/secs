using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void SpatialChecks()
    {
        using var physics = new PhysicsService();
        var random = new Random(8417);
        var comparisons = 0;
        var bodies = new List<int>();
        for (var i = 0; i < 40; i++)
            bodies.Add(physics.AddShip(100 - i, new Transform { Position = Point(), Angle = Scalar(-3, 3) },
                new Collider { Radius = Scalar(0.5f, 9), Mass = 30, ShapeCount = i % 3 == 0 ? 1 : 4 + i % 3 }));
        for (var epoch = 0; epoch < 5; epoch++)
        {
            for (var i = 0; i < bodies.Count; i++)
                physics.SetShip(bodies[i], new Transform { Position = Point(), Angle = Scalar(-3, 3) },
                    new Motion { Velocity = Point() * 0.1f }, (i + epoch) % 5 != 0);
            physics.Step(1f / 60);
            for (var i = 0; i < 6000; i++)
            {
                var from = Point();
                var to = i % 10 == 0 ? from : i % 3 == 0 ? Point() : from + Point() * 0.05f;
                var radius = i % 7 == 0 ? 0 : Scalar(0.01f, 4);
                Compare(from, to, radius);
            }
        }
        foreach (var body in bodies) physics.RemoveShip(body);
        // Same-fraction ties must retain serial ordering, including rays starting inside fixtures.
        var collider = new Collider { Radius = 2, Mass = 20, ShapeCount = 6 };
        physics.AddShip(9, new Transform { Position = Vector2.Zero }, collider);
        var lowerSerial = physics.AddShip(3, new Transform { Position = Vector2.Zero }, collider);
        Compare(Vector2.Zero, new(20, 0), 0.9f);
        Require(physics.Sweep(Vector2.Zero, new(20, 0), 0.9f, out var serial, out _) && serial == 3, "sweep serial tie preserved");
        for (var i = 0; i < 200; i++)
            Compare(new(-20, -3 + i * 0.03f), new(20, -3 + i * 0.03f), 0.9f);
        physics.SetShip(lowerSerial, new Transform { Position = new(-48, -48), Angle = 0.7f }, default, false);
        Compare(new(-50, -48), new(-45, -48), 0.9f);
        physics.SetShip(lowerSerial, new Transform { Position = new(-48, -48), Angle = 0.7f }, default, true);
        Compare(new(-50, -48), new(-45, -48), 0.9f);
        physics.Step(0);
        Require(!physics.Sweep(new(500, 500), new(501, 500), 0.9f, out _, out _), "empty sweep rejected");
        var work = physics.QueryStatistics;
        Require(work.Sweeps == 1 && work.EmptySweeps == 1 && work.Rays == 0 && work.PointQueries == 0, "empty broadphase skips all five ray/point queries");
        physics.Step(0);
        Require(physics.QueryStatistics == default, "query work resets each physics step");
        CollectionChecks();
        OwnerSweepChecks();
        OwnerDifferentialChecks();
        Console.WriteLine($"PASS: {comparisons} differential sweeps, disabled/moving/compound fixtures, serial ties, empty query work and cached frame parity.");

        float Scalar(float min, float max) => min + (float)random.NextDouble() * (max - min);
        Vector2 Point() => new(Scalar(-150, 150), Scalar(-150, 150));
        void Compare(Vector2 from, Vector2 to, float radius)
        {
            comparisons++;
            var actual = physics.Sweep(from, to, radius, out var serial, out var impact);
            var expected = physics.SweepUnfiltered(from, to, radius, out var referenceSerial, out var referenceImpact);
            Require(actual == expected && serial == referenceSerial && impact == referenceImpact, "broadphase rejection preserves exact reference hit/impact");
        }
    }

    private static void OwnerDifferentialChecks()
    {
        using var physics = new PhysicsService();
        var random = new Random(8418);
        var bodies = new int[40];
        var serials = new int[40];
        var comparisons = 0;
        for (var epoch = 0; epoch < 5; epoch++)
        {
            for (var i = 0; i < bodies.Length; i++)
            {
                if (epoch == 0 || i % 7 == 0)
                {
                    var oldBody = bodies[i];
                    if (epoch > 0) physics.RemoveShip(oldBody);
                    serials[i] = 1000 + epoch * 100 + i;
                    bodies[i] = physics.AddShip(serials[i], new Transform { Position = Point() },
                        new Collider { Radius = Scalar(0.5f, 9), Mass = 30, ShapeCount = i % 3 == 0 ? 1 : 4 + i % 3 });
                    if (epoch > 0) Require(bodies[i] == oldBody, "filtered fixture exercises recycled physical body ID");
                }
                physics.SetShip(bodies[i], new Transform { Position = Point(), Angle = Scalar(-3, 3) },
                    new Motion { Velocity = Point() * 0.1f }, (i + epoch) % 5 != 0);
            }
            physics.Step(1f / 60);
            for (var sample = 0; sample < 1200; sample++)
            {
                var index = sample % bodies.Length;
                var from = sample % 4 == 0 ? physics.GetShip(bodies[index]).Position : Point();
                var to = sample % 10 == 0 ? from : sample % 3 == 0 ? Point() : from + Point() * 0.05f;
                var radius = sample % 7 == 0 ? 0 : Scalar(0.01f, 4);
                var ignored = sample % 11 == 0 ? -1 : sample % 13 == 0 ? bodies[index] : serials[index];
                var actual = physics.Sweep(from, to, radius, ignored, out var serial, out var impact);
                var expected = physics.SweepUnfiltered(from, to, radius, out var referenceSerial, out var referenceImpact, ignored);
                Require(actual == expected && serial == referenceSerial && impact == referenceImpact,
                    $"filtered differential hit/impact epoch={epoch}, sample={sample}, ignore={ignored}");
                Require(!actual || serial != ignored, "filtered differential never returns ignored serial");
                var neutral = physics.Sweep(from, to, radius, out serial, out impact);
                var neutralReference = physics.SweepUnfiltered(from, to, radius, out referenceSerial, out referenceImpact);
                Require(neutral == neutralReference && serial == referenceSerial && impact == referenceImpact,
                    "filtered differential query cannot leak exclusion into next neutral query");
                comparisons++;
            }
        }
        Console.WriteLine($"PASS: {comparisons} owner-filtered exact-reference sweeps and subsequent neutral parity, disabled/recycled bodies, compound starts and zero-length segments.");
        float Scalar(float min, float max) => min + (float)random.NextDouble() * (max - min);
        Vector2 Point() => new(Scalar(-150, 150), Scalar(-150, 150));
    }

    private static void OwnerSweepChecks()
    {
        using var physics = new PhysicsService();
        const int owner = 701, other = 903;
        var compound = new Collider { Radius = 3, Mass = 30, ShapeCount = 6 };
        var ownerBody = physics.AddShip(owner, new Transform { Position = Vector2.Zero }, compound);
        Require(ownerBody != owner, "owner serial deliberately differs from body ID");
        physics.Step(0);
        Require(!physics.Sweep(new(-10, 0), new(10, 0), 2, owner, out var serial, out _)
            && serial == -1, "only ignored owner is an empty candidate set");
        var work = physics.QueryStatistics;
        Require(work.Sweeps == 1 && work.EmptySweeps == 1 && work.Rays == 0 && work.PointQueries == 0,
            "owner-only broadphase skips ray and overlap work");
        Require(physics.Sweep(new(-10, 0), new(10, 0), 2, out serial, out _) && serial == owner,
            "neutral sweep immediately after ignored sweep still hits owner");
        Require(physics.Sweep(new(-10, 0), new(10, 0), 2, ownerBody, out serial, out _) && serial == owner,
            "ignored serial is not interpreted as body ID");
        Require(!physics.Sweep(Vector2.Zero, Vector2.Zero, 1, owner, out serial, out _) && serial == -1,
            "zero-length start inside compound owner is ignored");
        var otherBody = physics.AddShip(other, new Transform { Position = new(20, 0) }, compound);
        Check(new(-10, 0), new(30, 0), 0, "ignored ray hit cannot clip the ship behind");
        Check(Vector2.Zero, new(30, 0), 0.9f, "compound owner start overlap cannot shield another ship");
        physics.SetShip(otherBody, new Transform { Position = Vector2.Zero }, default, true);
        Check(Vector2.Zero, new(30, 0), 0.9f, "overlapping owner and other ship preserve other start hit");
        Check(Vector2.Zero, Vector2.Zero, 0.9f, "zero-length overlapping other ship remains hittable");
        physics.RemoveShip(otherBody);
        var circle = new Collider { Radius = 0.3f, Mass = 10, ShapeCount = 1 };
        foreach (var y in new[] { -2f, -1f, 1f, 2f })
        {
            otherBody = physics.AddShip(other, new Transform { Position = new(20, y) }, circle);
            Require(!physics.Sweep(new(-10, 0), new(30, 0), 0, owner, out _, out _), "offset fixture misses center ray");
            Check(new(-10, 0), new(30, 0), 2, "all full/half offset rays continue beyond owner");
            physics.RemoveShip(otherBody);
        }
        otherBody = physics.AddShip(other, new Transform { Position = new(20, 0) }, compound);
        physics.SetShip(otherBody, new Transform { Position = new(20, 0) }, default, false);
        Require(!physics.Sweep(new(-10, 0), new(30, 0), 0.9f, owner, out _, out _), "disabled other plus ignored owner cannot hit");
        physics.SetShip(otherBody, new Transform { Position = new(20, 0) }, default, true);
        Check(new(-10, 0), new(30, 0), 0.9f, "reenabled other remains hittable");
        physics.RemoveShip(ownerBody);
        const int replacement = 1101;
        Require(physics.AddShip(replacement, new Transform { Position = Vector2.Zero }, compound) == ownerBody,
            "owner body slot reused");
        Require(physics.Sweep(new(-10, 0), new(30, 0), 0, owner, out serial, out _) && serial == replacement,
            "old owner serial does not exempt replacement in recycled physical slot");
        physics.SetShip(otherBody, new Transform { Position = Vector2.Zero }, default, true);
        physics.Sweep(Vector2.Zero, new(30, 0), 0.9f, replacement, out _, out _);
        physics.Step(1f / 60);
        Require(physics.Contacts.Any(c => c.FirstSerial == other && c.SecondSerial == replacement),
            "last ignored sweep cannot filter neutral ship contacts");

        void Check(Vector2 from, Vector2 to, float radius, string name)
        {
            Require(physics.Sweep(from, to, radius, owner, out var hit, out var impact) && hit == other, name);
            Require(physics.SweepUnfiltered(from, to, radius, out var expected, out var expectedImpact, owner)
                && expected == hit && expectedImpact == impact, name + " exact reference impact");
        }
    }

    private static void CollectionChecks()
    {
        using var battle = new Battle<SkecsStore>(new(), new BattleConfig { ShipsPerTeam = 16, Seed = 27 });
        var cached = new BattleFrame(); var queried = new BattleFrame();
        for (var i = 0; i < 900; i++)
        {
            battle.Step(); battle.Capture(cached, refreshEntities: false); battle.Capture(queried);
            Require(cached.Ships.SequenceEqual(queried.Ships) && cached.Turrets.SequenceEqual(queried.Turrets) &&
                cached.Projectiles.SequenceEqual(queried.Projectiles) && cached.Debris.SequenceEqual(queried.Debris) &&
                cached.Particles.SequenceEqual(queried.Particles), "incremental finalized lists match full ECS collection after churn");
        }
        var injected = battle.Store.Create();
        battle.Store.Set(injected, new Transform { Position = new(400, 400), Previous = new(400, 400) });
        battle.Store.Set(injected, new Motion { Velocity = Vector2.UnitX });
        battle.Store.Set(injected, new Projectile { Serial = 1000000, Owner = EntityKey.None, Target = EntityKey.None,
            Kind = WeaponKind.MachineGun, Radius = 0.1f, ExpiresTick = battle.Tick + 2 });
        battle.Store.Commit(); battle.Capture(queried);
        Require(queried.Projectiles.Any(p => p.Serial == 1000000), "default capture sees direct external store creation");
        battle.Step(); battle.Capture(cached, refreshEntities: false); battle.Capture(queried);
        Require(cached.Projectiles.SequenceEqual(queried.Projectiles), "externally injected high serial keeps canonical order after normal spawns");
        battle.Step();
        Require(!battle.Store.Alive(injected), "externally injected transient expires through ordinary lifecycle");
        battle.Capture(cached, refreshEntities: false); battle.Capture(queried);
        Require(cached.Projectiles.SequenceEqual(queried.Projectiles), "expired external row is compacted");
    }
}
