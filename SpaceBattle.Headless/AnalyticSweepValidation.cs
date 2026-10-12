using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void AnalyticSweepChecks()
    {
        AnalyticCircleChecks();
        AnalyticRectangleChecks();
        AnalyticRelativeChecks();
        AnalyticOracleChecks();
        BoundsGridChecks();
        AnalyticAllocationChecks();
        Console.WriteLine("PASS: analytic circle/rounded-rectangle CCD, double-relative precision and vector parity, 12000 independent distance-oracle sweeps, 3000 brute-force bounds queries, finite-input guards and zero reserved allocation with new cells/IDs/distributions.");
    }

    private static void AnalyticCircleChecks()
    {
        Hit(new(-5, 0), new(5, 0), Vector2.Zero, Vector2.Zero, 1, 0.4, "circle entry");
        Hit(new(-5, 1), new(5, 1), Vector2.Zero, Vector2.Zero, 1, 0.5, "earliest tangent");
        Hit(new(-5, 0), new(-1, 0), Vector2.Zero, Vector2.Zero, 1, 1, "end contact");
        Hit(Vector2.Zero, Vector2.Zero, new(1, 0), new(1, 0), 1, 0, "stationary tangent start");
        Hit(Vector2.Zero, new(10, 0), new(0.5f, 0), new(20, 0), 1, 0, "start inside moving apart");
        Hit(Vector2.Zero, Vector2.Zero, new(5, 0), new(-5, 0), 1, 0.4, "moving target");
        Hit(new(-5, 0), new(15, 0), Vector2.Zero, new(10, 0), 1, 0.4, "relative motion");
        Hit(new(-1e30f, 1), new(1e30f, 1), Vector2.Zero, Vector2.Zero, 1, 0.5, "large-speed tangent");
        Hit(new(-1e30f, 0), new(1e30f, 0), Vector2.Zero, Vector2.Zero, 1e24, (1 - 1e24 / (double)1e30f) / 2, "large-speed entry");
        Hit(new(float.MaxValue, float.MaxValue), Vector2.Zero, Vector2.Zero, Vector2.Zero, double.MaxValue, 0, "finite extreme radius");
        Hit(new(-2, 0), new(2, 0), Vector2.Zero, Vector2.Zero, 0, 0.5, "point contact");
        Require(!CircleSweepMath.TryCircle(new(-5, 1.001f), new(5, 1.001f), Vector2.Zero, Vector2.Zero, 1, out _), "near tangent miss");
        Require(!CircleSweepMath.TryCircle(new(-1e30f, 1.001f), new(1e30f, 1.001f), Vector2.Zero, Vector2.Zero, 1, out _), "large-speed near tangent miss");
        Require(!CircleSweepMath.TryCircle(Vector2.Zero, new(10, 0), new(5, 0), new(15, 0), 1, out _), "zero relative motion outside");
        Require(!CircleSweepMath.TryCircle(new(2, 0), new(3, 0), Vector2.Zero, Vector2.Zero, 1, out _), "moving away");
        Require(!CircleSweepMath.TryCircle(new(-3, 0), new(-2, 0), Vector2.Zero, Vector2.Zero, 1, out _), "entry after tick");
        Reject(() => CircleSweepMath.TryCircle(new(float.NaN, 0), Vector2.Zero, Vector2.Zero, Vector2.Zero, 1, out _));
        Reject(() => CircleSweepMath.TryCircle(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, double.PositiveInfinity, out _));
        Reject(() => CircleSweepMath.TryCircle(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, -1, out _));

        static void Hit(Vector2 from, Vector2 to, Vector2 targetFrom, Vector2 targetTo, double radius, double expected, string name)
        {
            Require(CircleSweepMath.TryCircle(from, to, targetFrom, targetTo, radius, out var fraction)
                && Math.Abs(fraction - expected) <= 2e-12, name);
        }
    }

    private static void AnalyticRectangleChecks()
    {
        var extents = new Vector2(2, 1);
        Hit(new(-5, 0), new(5, 0), Vector2.Zero, Vector2.Zero, extents, 0, 1, 0.2, "rectangle long side");
        Hit(new(0, -5), new(0, 5), Vector2.Zero, Vector2.Zero, extents, 0, 1, 0.3, "rectangle short side");
        Hit(Vector2.Zero, Vector2.Zero, new(-5, 1), new(5, 1), extents, 0, 0, 0.3, "zero-radius side grazing");
        Hit(Vector2.Zero, Vector2.Zero, new(-5, 2), new(5, 2), extents, 0, 1, 0.3, "earliest corner tangent then side");
        Hit(Vector2.Zero, Vector2.Zero, new(4, 3), new(2, 1), extents, 0, 1, 1 - Math.Sqrt(0.5) / 2, "circular corner entry");
        Hit(Vector2.Zero, Vector2.Zero, new(13, 5.25f), new(-3, 17.25f), extents, 0, 10, 0.3125, "isolated corner arc tangency");
        Hit(Vector2.Zero, Vector2.Zero, new(2.5f, 1.5f), new(2.5f, 1.5f), extents, 0, 1, 0, "rounded corner start inside");
        Hit(new(-5, 0), new(15, 0), Vector2.Zero, new(10, 0), extents, 0, 1, 0.2, "translating rectangle and disk");
        Hit(Vector2.Zero, Vector2.Zero, new(0, 5), new(0, -5), extents, MathF.PI / 2, 1, 0.2, "rotated rectangle", 1e-8);
        Hit(new(-1e30f, 0), new(1e30f, 0), Vector2.Zero, Vector2.Zero, extents, 0, 1, 0.5, "large rectangle sweep");
        Hit(Vector2.Zero, Vector2.Zero, new(-2, 0), new(2, 0), Vector2.Zero, 0, 1, 0.25, "degenerate rectangle disk");
        Require(!CircleSweepMath.TryRectangle(Vector2.Zero, Vector2.Zero, new(2.9f, 1.9f), new(2.9f, 1.9f), extents, 0, 1, out _), "expanded-box corner false positive rejected");
        Require(!CircleSweepMath.TryRectangle(Vector2.Zero, Vector2.Zero, new(-4, 1.9f), new(-2.9f, 1.9f), extents, 0, 1, out _), "swept expanded-box false positive rejected");
        Hit(Vector2.Zero, Vector2.Zero, new(1.9f, 0.9f), new(1.9f, 0.9f), extents, 0, 0, 0, "flat corner differs from inscribed capsule");
        Require(!CircleSweepMath.TryRectangle(Vector2.Zero, Vector2.Zero, new(2.8f, 0), new(2.8f, 0), extents, 0, 0, out _), "flat end differs from endpoint capsule");
        Require(!CircleSweepMath.TryRectangle(Vector2.Zero, new(5, 0), new(5, 3), new(10, 3), extents, 0, 1, out _), "rectangle zero relative motion outside");
        Reject(() => CircleSweepMath.TryRectangle(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, extents, float.NaN, 1, out _));
        Reject(() => CircleSweepMath.TryRectangle(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, new(-1, 1), 0, 1, out _));
        Reject(() => CircleSweepMath.TryRectangle(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, extents, 0, float.PositiveInfinity, out _));

        static void Hit(Vector2 from, Vector2 to, Vector2 targetFrom, Vector2 targetTo, Vector2 halfExtents,
            float angle, float radius, double expected, string name, double tolerance = 2e-12)
        {
            Require(CircleSweepMath.TryRectangle(from, to, targetFrom, targetTo, halfExtents, angle, radius, out var fraction)
                && Math.Abs(fraction - expected) <= tolerance, name);
        }
    }

    private static void AnalyticRelativeChecks()
    {
        Require(CircleSweepMath.TryCircleRelative(-1 - 1e-10, 0, 1, 0, 1, out var fraction)
            && fraction > 0 && Math.Abs(fraction - 1e-10) < 2e-16, "double circle offset is not rounded to float start-inside");
        Require(CircleSweepMath.TryRectangleRelative(3 + 1e-10, 0, -1, 0, new(2, 1), 0, 1, out fraction)
            && fraction > 0 && Math.Abs(fraction - 1e-10) < 2e-16, "double rectangle offset is not rounded to float start-inside");
        Require(CircleSweepMath.TryCircleRelative(-1e100, 1, 2e100, 0, 1, out fraction)
            && Math.Abs(fraction - 0.5) < 2e-12, "relative double large-speed tangent");
        Require(!CircleSweepMath.TryCircleRelative(-1e100, 1.001, 2e100, 0, 1, out _), "relative double large-speed near-tangent miss");
        Require(CircleSweepMath.TryRectangleRelative(4, 3, -2, -2, new(2, 1), 0, 1, out fraction)
            && Math.Abs(fraction - (1 - Math.Sqrt(0.5) / 2)) < 2e-12, "relative rectangle exact corner entry");
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Reject(() => CircleSweepMath.TryCircleRelative(invalid, 0, 0, 0, 1, out _));
            Reject(() => CircleSweepMath.TryCircleRelative(0, invalid, 0, 0, 1, out _));
            Reject(() => CircleSweepMath.TryCircleRelative(0, 0, invalid, 0, 1, out _));
            Reject(() => CircleSweepMath.TryCircleRelative(0, 0, 0, invalid, 1, out _));
            Reject(() => CircleSweepMath.TryCircleRelative(0, 0, 0, 0, invalid, out _));
            Reject(() => CircleSweepMath.TryRectangleRelative(invalid, 0, 0, 0, new(2, 1), 0, 1, out _));
            Reject(() => CircleSweepMath.TryRectangleRelative(0, invalid, 0, 0, new(2, 1), 0, 1, out _));
            Reject(() => CircleSweepMath.TryRectangleRelative(0, 0, invalid, 0, new(2, 1), 0, 1, out _));
            Reject(() => CircleSweepMath.TryRectangleRelative(0, 0, 0, invalid, new(2, 1), 0, 1, out _));
        }
        Reject(() => CircleSweepMath.TryCircleRelative(0, 0, 0, 0, -1, out _));
        Reject(() => CircleSweepMath.TryRectangleRelative(0, 0, 0, 0, new(-1, 1), 0, 1, out _));
        Reject(() => CircleSweepMath.TryRectangleRelative(0, 0, 0, 0, new(2, 1), float.NaN, 1, out _));
        Reject(() => CircleSweepMath.TryRectangleRelative(0, 0, 0, 0, new(2, 1), 0, -1, out _));
    }

    private static void AnalyticOracleChecks()
    {
        var random = new Random(99431);
        for (var i = 0; i < 6000; i++)
        {
            var from = Point(); var to = i % 13 == 0 ? from : Point();
            var targetFrom = Point(); var targetTo = i % 7 == 0 ? targetFrom : Point();
            var radius = Scalar(0.01f, 6);
            var x = (double)targetFrom.X - from.X; var y = (double)targetFrom.Y - from.Y;
            var dx = ((double)targetTo.X - targetFrom.X) - ((double)to.X - from.X);
            var dy = ((double)targetTo.Y - targetFrom.Y) - ((double)to.Y - from.Y);
            var expected = DistanceOracle(x, y, dx, dy, 0, 0, radius, out var expectedFraction);
            var actual = CircleSweepMath.TryCircle(from, to, targetFrom, targetTo, radius, out var fraction);
            Require(actual == expected && (!actual || Math.Abs(fraction - expectedFraction) < 2e-9), $"circle independent oracle {i}");
            var relative = CircleSweepMath.TryCircleRelative(-x, -y, -dx, -dy, radius, out var relativeFraction);
            Require(relative == actual && relativeFraction == fraction, $"circle vector/relative exact parity {i}");
            var halfExtents = new Vector2(Scalar(0, 6), Scalar(0, 3)); var angle = Scalar(-MathF.PI, MathF.PI);
            var cos = Math.Cos(angle); var sin = Math.Sin(angle);
            expected = DistanceOracle(x * cos + y * sin, -x * sin + y * cos,
                dx * cos + dy * sin, -dx * sin + dy * cos, halfExtents.X, halfExtents.Y, radius, out expectedFraction);
            actual = CircleSweepMath.TryRectangle(from, to, targetFrom, targetTo, halfExtents, angle, radius, out fraction);
            Require(actual == expected && (!actual || Math.Abs(fraction - expectedFraction) < 2e-9), $"rectangle independent oracle {i}");
            relative = CircleSweepMath.TryRectangleRelative(x, y, dx, dy, halfExtents, angle, radius, out relativeFraction);
            Require(relative == actual && relativeFraction == fraction, $"rectangle vector/relative exact parity {i}");
        }
        float Scalar(float min, float max) => min + (float)random.NextDouble() * (max - min);
        Vector2 Point() => new(Scalar(-60, 60), Scalar(-60, 60));
    }

    // Convex point-to-box distance minimization and bisection: no slab or quadratic sweep logic.
    private static bool DistanceOracle(double x, double y, double dx, double dy, double hx, double hy, double radius, out double fraction)
    {
        fraction = 0; var squaredRadius = radius * radius;
        if (Distance(0) <= squaredRadius) return true;
        double low = 0, high = 1;
        for (var i = 0; i < 120; i++)
        {
            var a = (2 * low + high) / 3; var b = (low + 2 * high) / 3;
            if (Distance(a) <= Distance(b)) high = b; else low = a;
        }
        var minimum = (low + high) / 2;
        if (Distance(1) < Distance(minimum)) minimum = 1;
        if (Distance(minimum) > squaredRadius) return false;
        low = 0; high = minimum;
        for (var i = 0; i < 80; i++)
        {
            var middle = (low + high) / 2;
            if (Distance(middle) <= squaredRadius) high = middle; else low = middle;
        }
        fraction = high;
        return true;

        double Distance(double t)
        {
            var ex = Math.Max(Math.Abs(x + dx * t) - hx, 0);
            var ey = Math.Max(Math.Abs(y + dy * t) - hy, 0);
            return ex * ex + ey * ey;
        }
    }

    private static void BoundsGridChecks()
    {
        var grid = new SpatialBoundsIndex(); var output = new List<int>();
        grid.AddBounds(9, -48, -48, 0, 0); grid.AddBounds(-7, 0, 0, 48, 48);
        grid.AddBounds(9, -1, -1, 1, 1); grid.Add(int.MaxValue, new(-96, 48), new(96, 48), 2);
        grid.QueryBounds(0, 0, 0, 0, output);
        Require(output.SequenceEqual(new[] { -7, 9 }), "closed boundary sorted IDs without duplicates");
        typeof(SpatialBoundsIndex).GetField("generation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(grid, uint.MaxValue);
        grid.QueryBounds(0, 0, 0, 0, output);
        Require(output.SequenceEqual(new[] { -7, 9 }), "generation wrap preserves de-duplication");
        grid.QueryBounds(-48, -48, -48, -48, output);
        Require(output.SequenceEqual(new[] { 9 }), "negative cell boundary contact");
        grid.Query(new(-96, 50), new(96, 50), 0, output);
        Require(output.Contains(int.MaxValue), "swept targets and radius padding");
        grid.Clear();
        Require(grid.CellCount == 0, "clear removes occupied cells");
        grid.QueryBounds(-1e300, -1e300, 1e300, 1e300, output);
        Require(output.Count == 0, "clear removes candidates");
        grid.AddBounds(1, -1e300, -1e300, 1e300, 1e300);
        grid.AddBounds(2, -10, -10, 10, 10);
        grid.AddBounds(3, 1e300, 1e300, 1e300, 1e300);
        grid.QueryBounds(-1, -1, 1, 1, output);
        Require(output.SequenceEqual(new[] { 1, 2 }) && grid.CellCount < 20, "large-AABB bounded insertion fallback");
        grid.QueryBounds(-1e300, -1e300, 1e300, 1e300, output);
        Require(output.SequenceEqual(new[] { 1, 2, 3 }), "occupied-cell bounded query fallback and extreme coordinates");
        grid.QueryBounds(1e300, 1e300, 1e300, 1e300, output);
        Require(output.SequenceEqual(new[] { 1, 3 }), "clamped extreme cell retains exact AABB rejection");
        grid.Clear(); grid.Reserve(2);
        grid.AddBounds(7, 1, 1, 383, 383);
        Require(grid.CellCount == 64, "64-cell bounds use reserved memberships");
        grid.Clear();
        grid.AddBounds(7, 1, 1, 431, 431);
        Require(grid.CellCount == 0, "more than 64 cells uses separate bounded large list");
        grid.QueryBounds(420, 420, 421, 421, output);
        Require(output.SequenceEqual(new[] { 7 }), "large-list bounds remain queryable");
        grid.Clear();
        // A target can leave its endpoint AABB and return within the same tick.
        grid.AddBounds(8, -2, -2, 102, 52);
        grid.QueryBounds(99, 49, 101, 51, output);
        Require(output.SequenceEqual(new[] { 8 }), "caller-provided full path union covers intermediate excursion");

        var random = new Random(48129);
        var reference = new List<(int Id, double MinX, double MinY, double MaxX, double MaxY)>();
        for (var epoch = 0; epoch < 5; epoch++)
        {
            grid.Clear(); reference.Clear();
            for (var i = 0; i < 250; i++)
            {
                var from = Point(); var to = i % 19 == 0 ? Point() * 10000 : i % 11 == 0 ? from : Point();
                var radius = i % 7 == 0 ? 0 : Scalar(0, 25);
                var id = i % 23 == 0 ? -1 : i;
                grid.Add(id, from, to, radius);
                reference.Add((id, (double)Math.Min(from.X, to.X) - radius, (double)Math.Min(from.Y, to.Y) - radius,
                    (double)Math.Max(from.X, to.X) + radius, (double)Math.Max(from.Y, to.Y) + radius));
            }
            for (var sample = 0; sample < 600; sample++)
            {
                var from = Point(); var to = sample % 13 == 0 ? Point() * 10000 : sample % 11 == 0 ? from : Point();
                var radius = sample % 7 == 0 ? 0 : Scalar(0, 50);
                double minX = Math.Min(from.X, to.X) - (double)radius, minY = Math.Min(from.Y, to.Y) - (double)radius;
                double maxX = Math.Max(from.X, to.X) + (double)radius, maxY = Math.Max(from.Y, to.Y) + (double)radius;
                grid.Query(from, to, radius, output);
                var expected = reference.Where(b => b.MinX <= maxX && b.MaxX >= minX && b.MinY <= maxY && b.MaxY >= minY)
                    .Select(b => b.Id).Distinct().Order().ToArray();
                Require(output.SequenceEqual(expected), $"bounds brute-force oracle epoch={epoch}, query={sample}");
            }
        }
        Reject(() => grid.Add(1, Vector2.Zero, new(float.NaN, 0), 0));
        Reject(() => grid.Query(Vector2.Zero, Vector2.Zero, -1, output));
        Reject(() => grid.AddBounds(1, double.NaN, 0, 1, 1));
        Reject(() => grid.AddBounds(1, 2, 0, 1, 1));
        Reject(() => grid.QueryBounds(0, 0, double.PositiveInfinity, 1, output));
        Reject(() => grid.Reserve(-1));
        Reject(() => grid.Reserve(int.MaxValue));
        float Scalar(float min, float max) => min + (float)random.NextDouble() * (max - min);
        Vector2 Point() => new(Scalar(-400, 400), Scalar(-400, 400));
    }

    private static void AnalyticAllocationChecks()
    {
        var grid = new SpatialBoundsIndex(); var output = new List<int>(256);
        grid.Reserve(201);
        for (var i = 0; i < 10; i++) Cycle(i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 10; i < 1010; i++) Cycle(i);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"reserved Add/Clear/Query with unseen cells/IDs/distributions and analytic sweeps allocate zero bytes, actual={allocated}");

        void Cycle(int cycle)
        {
            grid.Clear();
            var offset = (double)cycle * 960000;
            for (var i = 0; i < 200; i++)
            {
                var id = cycle % 2 == 0 ? i : 199 - i;
                var x = offset + (cycle % 3 == 0 ? 0 : id * 1008) + 1;
                var y = -offset + (cycle % 3 == 1 ? id * 1008 : 0) + 1;
                if (i == 199)
                {
                    var position = new Vector2((float)x, (float)y);
                    grid.Add(id + cycle * 200, position, position + new Vector2(60, -60), 3);
                }
                else grid.AddBounds(id + cycle * 200, x, y, x + 382, y + 382);
            }
            grid.AddBounds(int.MinValue, -1e30, -1e30, 1e30, 1e30);
            var from = new Vector2((float)offset, (float)-offset);
            grid.Query(from, from + new Vector2(80, 80), 3, output);
            grid.QueryBounds(-1e30, -1e30, 1e30, 1e30, output);
            grid.QueryBounds(10000, 10000, 10001, 10001, output);
            CircleSweepMath.TryCircle(new(-5, 1), new(5, 1), Vector2.Zero, Vector2.Zero, 1, out _);
            CircleSweepMath.TryRectangle(new(-5, 2), new(5, 2), Vector2.Zero, Vector2.Zero, new(2, 1), 0.7f, 1, out _);
            CircleSweepMath.TryCircleRelative(-5, 1, 10, 0, 1, out _);
            CircleSweepMath.TryRectangleRelative(5, -2, -10, 0, new(2, 1), 0.7f, 1, out _);
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("Validation failed: nonfinite or invalid geometry accepted.");
    }
}
