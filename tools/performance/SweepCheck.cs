using System.Numerics;
using SpaceBattle;

internal static class SweepCheck
{
    public static void Run()
    {
        // Present only in the isolated optimization experiment, never the production API.
        var reference = typeof(PhysicsService).GetMethod("SweepUnfiltered")
            ?? throw new InvalidOperationException("Build against the isolated sweep experiment.");
        using var physics = new PhysicsService();
        var rng = new Random(7331);
        var bodyIds = new List<int>();
        var checkedCases = 0;
        Check(Vector2.Zero, new Vector2(100, 0), 4);
        Check(Vector2.Zero, Vector2.Zero, 0);
        for (var serial = 1; serial <= 16; serial++)
        {
            var position = new Vector2((serial % 4 - 2) * 18, (serial / 4 - 2) * 17);
            var transform = new Transform { Position = position, Previous = position, Angle = serial * .43f };
            var collider = new Collider { Radius = 2 + serial % 5, ShapeCount = serial % 3 == 0 ? 6 : 1, Mass = 30 };
            bodyIds.Add(physics.AddShip(serial, transform, collider));
        }
        var cases = new List<(Vector2 From, Vector2 To, float Radius)>();
        foreach (var point in new[] { Vector2.Zero, new Vector2(-18, -17), new Vector2(80, 80), new Vector2(-36, -34) })
        {
            cases.Add((point, point, 0)); cases.Add((point, point, 4));
            cases.Add((point, point + new Vector2(100, 0), 0));
            cases.Add((point, point + new Vector2(100, 0), 8));
        }
        for (var i = 0; i < 20000; i++)
        {
            Vector2 Point() => new((float)(rng.NextDouble() * 240 - 120), (float)(rng.NextDouble() * 240 - 120));
            var start = Point();
            cases.Add((start, i % 17 == 0 ? start : Point(), i % 7 == 0 ? 0 : (float)rng.NextDouble() * 12));
        }
        foreach (var (from, to, radius) in cases)
            Check(from, to, radius);
        for (var i = 0; i < bodyIds.Count; i++)
        {
            var ship = physics.GetShip(bodyIds[i]);
            var transform = new Transform { Position = ship.Position, Previous = ship.Position, Angle = ship.Angle };
            var motion = new Motion();
            physics.SetShip(bodyIds[i], in transform, in motion, enabled: false);
        }
        foreach (var (from, to, radius) in cases.Take(500)) Check(from, to, radius);
        physics.RemoveShip(bodyIds[2]);
        var large = new Transform { Position = new Vector2(1_000_000, -1_000_000), Previous = new Vector2(1_000_000, -1_000_000) };
        physics.AddShip(100, large, new Collider { Radius = 100, ShapeCount = 6, Mass = 30 });
        for (var i = 0; i < 1000; i++)
            Check(large.Position + new Vector2(-150, i * .3f - 150), large.Position + new Vector2(150, i * .3f - 150), i % 5);
        Console.WriteLine($"PASS: {checkedCases} sweep result/serial/impact-bit differential cases, including stationary, zero-radius, origins inside compound fixtures, disabled fixtures, removed/reused slots, large coordinates and empty space.");

        void Check(Vector2 from, Vector2 to, float radius)
        {
            var result = physics.Sweep(from, to, radius, out var serial, out var impact);
            object?[] parameters = [from, to, radius, 0, Vector2.Zero];
            var expected = (bool)reference.Invoke(physics, parameters)!;
            var expectedImpact = (Vector2)parameters[4]!;
            if (result != expected || serial != (int)parameters[3]! ||
                BitConverter.SingleToInt32Bits(impact.X) != BitConverter.SingleToInt32Bits(expectedImpact.X) ||
                BitConverter.SingleToInt32Bits(impact.Y) != BitConverter.SingleToInt32Bits(expectedImpact.Y))
                throw new InvalidOperationException($"Sweep mismatch at {checkedCases}: {from}->{to}, r={radius}, actual={result}/{serial}/{impact}, expected={expected}/{parameters[3]}/{parameters[4]}");
            checkedCases++;
        }
    }
}
