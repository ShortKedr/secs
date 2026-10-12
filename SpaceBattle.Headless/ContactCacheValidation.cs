using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void ContactCacheChecks()
    {
        foreach (var cell in new[] { 24.0, 32, 48, 64 })
        foreach (var margin in new[] { 0.0, 0.01, 2, 8 })
        foreach (var budget in new[] { 1, 262144 })
            CompareContactCache(cell, margin, budget);
        ContactCacheDiscoveryCheck();
        ContactGraphResidualCheck();
        DenseStampWrapCheck();
        Console.WriteLine("PASS: cached/uncached bitwise contacts, state and CCD across cell sizes, fat-bound exits, dense/mixed radii, grid limits, extreme coordinates, disabled/recycled slots and budget fallback.");
    }

    private static void CompareContactCache(double cell, double margin, int budget)
    {
        var settings = new PhysicsSettings { ContactCellSize = cell, ContactCandidateMargin = margin,
            MaximumCandidatePairs = budget, CollectDiagnostics = true };
        using var fast = new PhysicsService(settings);
        using var reference = new PhysicsService(settings with { CacheContactCandidates = false });
        var ids = new int[48];
        for (var i = 0; i < ids.Length; i++)
        {
            var radius = i % 7 == 0 ? 8 : 0.5f + i % 4;
            var t = new Transform { Position = new(i % 8 * 3, i / 8 * 3) };
            var collider = new Collider { Radius = radius, Mass = 1 + i % 11 };
            ids[i] = fast.AddShip(100 - i, t, collider);
            Require(reference.AddShip(100 - i, t, collider) == ids[i], "cache body slots");
        }
        for (var tick = 0; tick < 24; tick++)
        {
            var offset = tick < 8 ? -48 : tick < 16 ? 3065 : 1e20f;
            for (var i = 0; i < ids.Length; i++)
            {
                var t = new Transform { Position = new(offset + i % 8 * 3, i / 8 * 3), Angle = i * .2f };
                var motion = new Motion { Velocity = new(i % 3 * 40 - 40, i % 2 * 30 - 15), AngularVelocity = i % 3 - 1 };
                fast.SetShip(ids[i], t, motion, i % 9 != tick % 9);
                reference.SetShip(ids[i], t, motion, i % 9 != tick % 9);
            }
            if (tick == 10)
            {
                fast.RemoveShip(ids[7]); reference.RemoveShip(ids[7]);
                var t = new Transform { Position = new(offset, 15) };
                var c = new Collider { Radius = 3, Mass = 10 };
                ids[7] = fast.AddShip(-7, t, c);
                Require(reference.AddShip(-7, t, c) == ids[7], "cache recycled slot");
            }
            fast.Step(1f / 60); reference.Step(1f / 60);
            Require(fast.Contacts.SequenceEqual(reference.Contacts), "canonical contact cache parity");
            foreach (var id in ids)
            {
                var a = fast.GetShip(id); var b = reference.GetShip(id);
                CacheSame(a.Position, b.Position); CacheSame(a.Velocity, b.Velocity);
                CacheSame(a.Angle, b.Angle); CacheSame(a.AngularVelocity, b.AngularVelocity);
            }
            for (var i = 0; i < 12; i++)
            {
                var from = new Vector2(offset - 12, i * 2); var to = new Vector2(offset + 50, 24 - i * 2);
                var hit = fast.Sweep(from, to, .2f, 95, out var serial, out var impact);
                Require(hit == reference.Sweep(from, to, .2f, 95, out var other, out var expected) && serial == other, "cache circle earliest hit");
                CacheSame(impact, expected);
                hit = fast.SweepRectangle(from, to, new(1, .2f), i * .3f, 95, out serial, out impact);
                Require(hit == reference.SweepRectangle(from, to, new(1, .2f), i * .3f, 95, out other, out expected) && serial == other, "cache rectangle earliest hit");
                CacheSame(impact, expected);
            }
            Require(fast.Diagnostics.Work.NewConstraints == reference.Diagnostics.Work.NewConstraints &&
                fast.Diagnostics.Work.VelocityConstraintVisits == reference.Diagnostics.Work.VelocityConstraintVisits,
                "cache does not reduce iterations or lose constraints");
        }
    }

    private static void ContactCacheDiscoveryCheck()
    {
        using var physics = new PhysicsService(new() { CollectDiagnostics = true });
        KernelDisk(physics, 1, Vector2.Zero, 4, 1000);
        KernelDisk(physics, 2, new(4.8f, 0), 1, 1);
        KernelDisk(physics, 3, new(6.91f, 0), 1, 1);
        physics.Step(1f / 60);
        Require(physics.Contacts.Any(c => c.FirstSerial == 2 && c.SecondSerial == 3), "cache discovers correction-created neighbor contact");
        Require(physics.Diagnostics.Work.CandidateCacheBuilds == 1 && physics.Diagnostics.Work.ContactBuilds > 1,
            "covered correction reuses conservative candidate cache");
        using var silent = new PhysicsService();
        KernelDisk(silent, 1, Vector2.Zero); silent.Step(1f / 60);
        silent.Sweep(new(-5, 0), new(5, 0), .1f, out _, out _);
        Require(silent.Diagnostics == default, "new diagnostics disabled by default");
    }

    private static void CacheSame(Vector2 a, Vector2 b) { CacheSame(a.X, b.X); CacheSame(a.Y, b.Y); }
    private static void CacheSame(float a, float b) => Require(BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b), "cache bitwise float parity");

    private static void ContactGraphResidualCheck()
    {
        using var physics = new PhysicsService(new() { Friction = 0, Restitution = 0, PositionIterations = 1 });
        var ids = new int[12];
        for (var i = 0; i < ids.Length; i++) ids[i] = KernelDisk(physics, i + 1, new(i * 1.8f, 0),
            velocity: i == 0 ? new(12, 0) : Vector2.Zero);
        physics.Step(1f / 60);
        var before = ids.Select(id => physics.GetShip(id).Velocity).ToArray();
        var pairs = physics.Contacts.ToArray();
        var solve = typeof(PhysicsService).GetMethod("SolveVelocities", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        solve.Invoke(physics, new object[] { 0 });
        Require(pairs.SequenceEqual(physics.Contacts), "residual test graph stays unchanged");
        Require(ids.Where((id, i) => before[i] != physics.GetShip(id).Velocity).Any(),
            "unchanged coupled graph can still change in another full velocity pass; cannot prune without fixed-point proof");
    }

    private static void DenseStampWrapCheck()
    {
        var grid = new SpatialBoundsIndex(denseIds: true);
        grid.Reserve(4); grid.Add(3, Vector2.Zero, Vector2.Zero, 1); grid.Seal();
        var output = new List<int>(4);
        grid.Query(Vector2.Zero, Vector2.Zero, 1, output);
        typeof(SpatialBoundsIndex).GetField("generation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(grid, uint.MaxValue);
        grid.Query(Vector2.Zero, Vector2.Zero, 1, output);
        Require(output.SequenceEqual(new[] { 3 }), "dense marks clear full reserved ID range on generation wrap");
        grid.Clear(); grid.Add(3, new(5000, 0), new(5000, 0), 1);
        grid.Query(new(5000, 0), new(5000, 0), 1, output);
        Require(output.SequenceEqual(new[] { 3 }), "dense mark reuse after Clear and fallback");
    }
}
