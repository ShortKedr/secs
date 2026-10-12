using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void PreparedCcdChecks()
    {
        PreparedCcdGeometryChecks();
        PreparedCcdCorpusChecks();
        PreparedCcdArgumentChecks();
        PreparedCcdAllocationCheck();
        Console.WriteLine("PASS: prepared rectangle CCD frozen-baseline bit parity, strips/corners/tangencies, degenerate/extreme/moving cases, validation and warmed zero allocation.");
    }

    private static void PreparedCcdGeometryChecks()
    {
        var extents = new Vector2(2, 1);
        Hit(5, 0, -10, 0, extents, 1, 0.2, "horizontal strip");
        Hit(0, 5, 0, -10, extents, 1, 0.3, "vertical strip");
        Hit(3, 0, 0, 0, extents, 1, 0, "start touching");
        Hit(0, 0, 0, 0, extents, 0, 0, "start inside zero radius");
        Hit(4, 0, -1, 0, extents, 1, 1, "end touching");
        Hit(3 + 1e-10, 0, -1, 0, extents, 1, 1e-10, "double offset outside");
        Hit(-2, 0, 4, 0, Vector2.Zero, 1, 0.25, "point rectangle");
        Hit(-5, 0, 10, 0, new(0, 1), 0, 0.5, "vertical line rectangle");
        Hit(0, -5, 0, 10, new(2, 0), 0, 0.5, "horizontal line rectangle");
        Hit(-1, 0, 1, 0, Vector2.Zero, 0, 1, "point against point");
        Hit(-1e100, 0, 2e100, 0, extents, 1, 0.5, "extreme displacement");
        Hit(-1e100, 1, 2e100, 0, Vector2.Zero, 1, 0.5, "extreme disk tangent");
        Miss(-1e100, 1.001, 2e100, 0, Vector2.Zero, 1, "extreme tangent miss");
        Miss(4, 0, 0, 0, extents, 1, "zero motion outside");
        Miss(4, 0, 1, 0, extents, 1, "moving away");
        Miss(5, 0, -1, 0, extents, 1, "entry after interval");
        Miss(2.9, 1.9, 0, 0, extents, 1, "rounded box false positive");
        Miss(-4, 1.9, 1.1, 0, extents, 1, "swept rounded box false positive");
        Miss(2.8, 0, 0, 0, extents, 0, "flat end");

        for (var sx = -1; sx <= 1; sx += 2)
            for (var sy = -1; sy <= 1; sy += 2)
            {
                var name = $"corner {sx}/{sy}";
                Hit(sx * 4, sy * 3, sx * -2, sy * -2, extents, 1,
                    1 - Math.Sqrt(0.5) / 2, name + " entry");
                Hit(sx * 2.5, sy * 1.5, 0, 0, extents, 1, 0, name + " start inside");
                Hit(sx * 13, sy * 5.25, sx * -16, sy * 12, extents, 10, 0.3125,
                    name + " isolated arc tangent");
                Hit(-5, sy * 2, 10, 0, extents, 1, 0.3, name + " horizontal tangent");
                Hit(sx * 3, -5, 0, 10, extents, 1, 0.4, name + " vertical tangent");
                Hit(-5, sy, 10, 0, extents, 0, 0.3, name + " zero-radius grazing");
            }

        foreach (var angle in new[] { 0f, -0f, 0.7f, -1.2f, MathF.PI / 2, MathF.PI,
                     -MathF.PI, MathF.Tau, 100000f, float.MaxValue, -float.MaxValue })
        {
            PreparedCcdCompare(4, 3, -8, -6, extents, angle, 1, $"angle {angle}");
            PreparedCcdMovingCompare(new(-5, 0), new(15, 0), Vector2.Zero, new(10, 0),
                extents, angle, 1, $"moving relative angle {angle}");
            PreparedCcdMovingCompare(new(100, -50), new(105, -47), new(104, -47), new(109, -44),
                extents, angle, 1, $"equal velocities angle {angle}");
        }

        void Hit(double x, double y, double dx, double dy, Vector2 halfExtents, float radius,
            double expected, string name)
        {
            PreparedCcdCompare(x, y, dx, dy, halfExtents, 0, radius, name);
            Require(CircleSweepMath.TryRectangleRelative(x, y, dx, dy, halfExtents, 1.0, 0.0,
                radius, out var fraction) && Math.Abs(fraction - expected) <= 2e-12,
                "prepared CCD analytic " + name);
        }

        void Miss(double x, double y, double dx, double dy, Vector2 halfExtents, float radius, string name)
        {
            PreparedCcdCompare(x, y, dx, dy, halfExtents, 0, radius, name);
            Require(!CircleSweepMath.TryRectangleRelative(x, y, dx, dy, halfExtents, 1.0, 0.0,
                radius, out _), "prepared CCD analytic " + name);
        }
    }

    private static void PreparedCcdCorpusChecks()
    {
        var random = new Random(812731);
        for (var i = 0; i < 6000; i++)
        {
            var from = Point(); var to = i % 13 == 0 ? from : Point();
            var targetFrom = Point(); var targetTo = i % 7 == 0 ? targetFrom : Point();
            var extents = new Vector2(i % 11 == 0 ? 0 : Scalar(0, 6), i % 17 == 0 ? 0 : Scalar(0, 3));
            var angle = Scalar(-MathF.PI, MathF.PI);
            var radius = i % 19 == 0 ? 0 : Scalar(0, 6);
            PreparedCcdMovingCompare(from, to, targetFrom, targetTo, extents, angle, radius,
                $"moving corpus {i}");
        }

        for (var i = 0; i < 4096; i++)
        {
            var scale = Math.Pow(10, random.Next(-300, 301));
            var extentScale = (float)Math.Pow(10, random.Next(-30, 31));
            var extents = new Vector2(i % 11 == 0 ? 0 : extentScale * 2, i % 17 == 0 ? 0 : extentScale);
            var angle = i % 3 == 0 ? (float)((random.NextDouble() * 2 - 1) * float.MaxValue)
                : Scalar(-MathF.PI, MathF.PI);
            PreparedCcdCompare(Signed() * scale, Signed() * scale,
                i % 13 == 0 ? 0 : Signed() * scale, i % 7 == 0 ? 0 : Signed() * scale,
                extents, angle, i % 19 == 0 ? 0 : extentScale, $"extreme corpus {i}");
        }

        foreach (var magnitude in new[] { double.Epsilon, 1e-300, 1e-100, 1e100, 1e300, double.MaxValue })
            foreach (var angle in new[] { 0f, 0.7f, -1.2f, float.MaxValue })
            {
                PreparedCcdCompare(magnitude, -magnitude, -magnitude, magnitude,
                    new(float.MaxValue, float.Epsilon), angle, float.MaxValue, "finite limits");
                PreparedCcdCompare(-magnitude, 0, magnitude, 0,
                    Vector2.Zero, angle, float.Epsilon, "degenerate finite limits");
            }
        PreparedCcdCompare(-0.0, 0.0, 0.0, -0.0, Vector2.Zero, -0f, 0, "signed zero");

        double Signed() => random.NextDouble() * 2 - 1;
        float Scalar(float min, float max) => min + (float)random.NextDouble() * (max - min);
        Vector2 Point() => new(Scalar(-60, 60), Scalar(-60, 60));
    }

    private static void PreparedCcdMovingCompare(Vector2 from, Vector2 to, Vector2 targetFrom,
        Vector2 targetTo, Vector2 halfExtents, float angle, float radius, string name)
    {
        var x = (double)targetFrom.X - from.X; var y = (double)targetFrom.Y - from.Y;
        var dx = ((double)targetTo.X - targetFrom.X) - ((double)to.X - from.X);
        var dy = ((double)targetTo.Y - targetFrom.Y) - ((double)to.Y - from.Y);
        PreparedCcdCompare(x, y, dx, dy, halfExtents, angle, radius, name);
        var expected = PreparedCcdBaseline.TryRectangleRelative(x, y, dx, dy, halfExtents,
            angle, radius, out var expectedFraction);
        var actual = CircleSweepMath.TryRectangle(from, to, targetFrom, targetTo, halfExtents,
            angle, radius, out var fraction);
        Require(actual == expected && PreparedCcdSameBits(fraction, expectedFraction),
            "prepared CCD vector wrapper " + name);
    }

    private static void PreparedCcdCompare(double x, double y, double dx, double dy,
        Vector2 halfExtents, float angle, float radius, string name)
    {
        var expected = PreparedCcdBaseline.TryRectangleRelative(x, y, dx, dy, halfExtents,
            angle, radius, out var expectedFraction);
        var legacy = CircleSweepMath.TryRectangleRelative(x, y, dx, dy, halfExtents,
            angle, radius, out var legacyFraction);
        var prepared = CircleSweepMath.TryRectangleRelative(x, y, dx, dy, halfExtents,
            Math.Cos(angle), Math.Sin(angle), radius, out var preparedFraction);
        Require(legacy == expected && prepared == expected && PreparedCcdSameBits(legacyFraction, expectedFraction)
            && PreparedCcdSameBits(preparedFraction, expectedFraction), "prepared CCD frozen bit parity " + name);
    }

    private static bool PreparedCcdSameBits(double first, double second) =>
        BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);

    private static void PreparedCcdArgumentChecks()
    {
        var extents = new Vector2(2, 1);
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Both(invalid, 0, 0, 0, extents, 0, 1, "x");
            Both(0, invalid, 0, 0, extents, 0, 1, "y");
            Both(0, 0, invalid, 0, extents, 0, 1, "dx");
            Both(0, 0, 0, invalid, extents, 0, 1, "dy");
            PreparedCcdReject(() => CircleSweepMath.TryRectangleRelative(0, 0, 0, 0,
                extents, invalid, 0, 1, out _), "cos");
            PreparedCcdReject(() => CircleSweepMath.TryRectangleRelative(0, 0, 0, 0,
                extents, 1, invalid, 1, out _), "sin");
        }
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            Both(0, 0, 0, 0, new(invalid, 1), 0, 1, "halfExtents");
            Both(0, 0, 0, 0, new(2, invalid), 0, 1, "halfExtents");
            Both(0, 0, 0, 0, extents, 0, invalid, "targetRadius");
            if (!float.IsFinite(invalid))
            {
                PreparedCcdReject(() => PreparedCcdBaseline.TryRectangleRelative(0, 0, 0, 0,
                    extents, invalid, 1, out _), "angle");
                PreparedCcdReject(() => CircleSweepMath.TryRectangleRelative(0, 0, 0, 0,
                    extents, invalid, 1, out _), "angle");
            }
        }
        Both(double.NaN, double.NaN, double.NaN, double.NaN, new(-1, -1), float.NaN, -1, "x");
        Both(0, 0, 0, 0, new(-1, -1), float.NaN, -1, "halfExtents");
        PreparedCcdReject(() => CircleSweepMath.TryRectangleRelative(0, 0, 0, 0,
            extents, float.NaN, -1, out _), "angle");
        PreparedCcdReject(() => CircleSweepMath.TryRectangleRelative(0, 0, 0, 0,
            extents, double.NaN, double.NaN, -1, out _), "cos");
        PreparedCcdReject(() => CircleSweepMath.TryRectangle(Vector2.Zero, new(float.NaN, 0),
            Vector2.Zero, Vector2.Zero, extents, 0, 1, out _), "to");

        static void Both(double x, double y, double dx, double dy, Vector2 halfExtents,
            float angle, float radius, string parameter)
        {
            PreparedCcdReject(() => PreparedCcdBaseline.TryRectangleRelative(x, y, dx, dy,
                halfExtents, angle, radius, out _), parameter);
            PreparedCcdReject(() => CircleSweepMath.TryRectangleRelative(x, y, dx, dy,
                halfExtents, angle, radius, out _), parameter);
            PreparedCcdReject(() => CircleSweepMath.TryRectangleRelative(x, y, dx, dy,
                halfExtents, Math.Cos(angle), Math.Sin(angle), radius, out _), parameter);
        }
    }

    private static void PreparedCcdReject(Action action, string parameter)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException exception)
        {
            Require(exception.ParamName == parameter, "prepared CCD validation parameter " + parameter);
            return;
        }
        throw new InvalidOperationException("Validation failed: prepared CCD accepted invalid " + parameter);
    }

    private static void PreparedCcdAllocationCheck()
    {
        const float angle = 0.7f;
        var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        var extents = new Vector2(2, 1);
        for (var i = 0; i < 10000; i++) Cycle(i, extents, cos, sin);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var hits = 0;
        for (var i = 0; i < 10000; i++) hits += Cycle(i, extents, cos, sin);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(hits > 0 && allocated == 0, $"prepared CCD warmed allocation bytes={allocated}");

        static int Cycle(int i, Vector2 halfExtents, double cos, double sin)
        {
            var hits = 0;
            if (CircleSweepMath.TryRectangleRelative(4, 3, -8, -6, halfExtents, cos, sin, 1, out _)) hits++;
            if (CircleSweepMath.TryRectangleRelative(0, 0, 0, 0, halfExtents, cos, sin, 0, out _)) hits++;
            if (CircleSweepMath.TryRectangleRelative(50 + i % 7, 30, 1, 0, halfExtents, cos, sin, 1, out _)) hits++;
            return hits;
        }
    }

    // Frozen formula from Benchmarks.Local/tmp/kernel-opt-20261012/before-source/SpaceBattle.Core/CircleSweepMath.cs.
    // Standalone helpers intentionally do not call the production kernel: wrapper-to-wrapper parity alone is circular.
    private static class PreparedCcdBaseline
    {
        public static bool TryRectangleRelative(double x, double y, double dx, double dy,
            Vector2 halfExtents, float angle, float targetRadius, out double fraction)
        {
            ValidateRelative(x, y, dx, dy);
            Validate(halfExtents, nameof(halfExtents));
            if (halfExtents.X < 0 || halfExtents.Y < 0) throw new ArgumentOutOfRangeException(nameof(halfExtents));
            if (!float.IsFinite(angle)) throw new ArgumentOutOfRangeException(nameof(angle));
            if (!float.IsFinite(targetRadius) || targetRadius < 0) throw new ArgumentOutOfRangeException(nameof(targetRadius));
            var cos = Math.Cos(angle); var sin = Math.Sin(angle);
            var localX = x * cos + y * sin; var localY = -x * sin + y * cos;
            var localDx = dx * cos + dy * sin; var localDy = -dx * sin + dy * cos;
            x = localX; y = localY; dx = localDx; dy = localDy;
            double hx = halfExtents.X, hy = halfExtents.Y, radius = targetRadius;
            fraction = 0;
            if (InsideCircle(Math.Max(Math.Abs(x) - hx, 0), Math.Max(Math.Abs(y) - hy, 0), radius)) return true;

            var earliest = double.PositiveInfinity;
            if (Strip(x, y, dx, dy, hx + radius, hy, out var t)) earliest = t;
            if (Strip(x, y, dx, dy, hx, hy + radius, out t)) earliest = Math.Min(earliest, t);
            for (var sx = -1; sx <= 1; sx += 2)
                for (var sy = -1; sy <= 1; sy += 2)
                {
                    var cx = sx * hx; var cy = sy * hy;
                    if (!CircleRoots(x - cx, y - cy, dx, dy, radius, out var enter, out var exit)) continue;
                    if (Corner(enter, x - cx, y - cy, dx, dy, sx, sy)) earliest = Math.Min(earliest, enter);
                    if (Corner(exit, x - cx, y - cy, dx, dy, sx, sy)) earliest = Math.Min(earliest, exit);
                }
            if (!double.IsFinite(earliest)) return false;
            fraction = earliest;
            return true;
        }

        private static bool Corner(double t, double x, double y, double dx, double dy, int sx, int sy) =>
            InTick(t) && sx * Math.FusedMultiplyAdd(dx, t, x) >= 0 && sy * Math.FusedMultiplyAdd(dy, t, y) >= 0;

        private static bool Strip(double x, double y, double dx, double dy, double hx, double hy, out double t)
        {
            t = 0; var exit = 1.0;
            return Slab(x, dx, hx, ref t, ref exit) && Slab(y, dy, hy, ref t, ref exit);
        }

        private static bool Slab(double position, double velocity, double extent, ref double enter, ref double exit)
        {
            if (velocity == 0) return Math.Abs(position) <= extent;
            var first = (-extent - position) / velocity; var last = (extent - position) / velocity;
            if (first > last) (first, last) = (last, first);
            enter = Math.Max(enter, first); exit = Math.Min(exit, last);
            return enter <= exit;
        }

        private static bool InsideCircle(double x, double y, double radius)
        {
            var scale = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), radius);
            if (scale == 0) return true;
            x /= scale; y /= scale; radius /= scale;
            return x * x + y * y <= radius * radius;
        }

        private static bool CircleRoots(double x, double y, double dx, double dy, double radius,
            out double enter, out double exit)
        {
            enter = exit = 0;
            var scale = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)),
                Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), radius));
            if (scale == 0) return false;
            x /= scale; y /= scale; dx /= scale; dy /= scale; radius /= scale;
            var a = dx * dx + dy * dy;
            if (a == 0) return false;
            var b = x * dx + y * dy;
            var distance = Math.Sqrt(x * x + y * y);
            var c = (distance - radius) * (distance + radius);
            var cross = Math.FusedMultiplyAdd(x, dy, -y * dx);
            var ra = radius * radius * a;
            var crossSquared = cross * cross;
            var discriminant = Math.FusedMultiplyAdd(radius * radius, a, -crossSquared);
            if (discriminant < 0)
            {
                if (discriminant < -8 * 2.2204460492503131e-16 * (ra + crossSquared)) return false;
                discriminant = 0;
            }
            var root = Math.Sqrt(discriminant);
            var q = -b - Math.CopySign(root, b);
            if (q == 0) { enter = exit = -b / a; return true; }
            enter = q / a; exit = c / q;
            if (enter > exit) (enter, exit) = (exit, enter);
            return true;
        }

        private static bool InTick(double t) => t >= 0 && t <= 1;

        private static void ValidateRelative(double x, double y, double dx, double dy)
        {
            if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
            if (!double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
            if (!double.IsFinite(dx)) throw new ArgumentOutOfRangeException(nameof(dx));
            if (!double.IsFinite(dy)) throw new ArgumentOutOfRangeException(nameof(dy));
        }

        private static void Validate(Vector2 value, string name)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) throw new ArgumentOutOfRangeException(name);
        }
    }
}
