using System.Numerics;

namespace SpaceBattle;

internal static class CircleSweepMath
{
    public static bool TryCircle(Vector2 from, Vector2 to, Vector2 targetFrom, Vector2 targetTo,
        double sumRadius, out double fraction)
    {
        Validate(from, nameof(from)); Validate(to, nameof(to));
        Validate(targetFrom, nameof(targetFrom)); Validate(targetTo, nameof(targetTo));
        if (!double.IsFinite(sumRadius) || sumRadius < 0) throw new ArgumentOutOfRangeException(nameof(sumRadius));
        var x = (double)from.X - targetFrom.X;
        var y = (double)from.Y - targetFrom.Y;
        var dx = ((double)to.X - from.X) - ((double)targetTo.X - targetFrom.X);
        var dy = ((double)to.Y - from.Y) - ((double)targetTo.Y - targetFrom.Y);
        return TryCircleRelative(x, y, dx, dy, sumRadius, out fraction);
    }

    // World-space shot-minus-target offset and displacement over the supplied interval.
    public static bool TryCircleRelative(double x, double y, double dx, double dy,
        double sumRadius, out double fraction)
    {
        ValidateRelative(x, y, dx, dy);
        if (!double.IsFinite(sumRadius) || sumRadius < 0) throw new ArgumentOutOfRangeException(nameof(sumRadius));
        fraction = 0;
        if (InsideCircle(x, y, sumRadius)) return true;
        if (!CircleRoots(x, y, dx, dy, sumRadius, out var enter, out _) || !InTick(enter)) return false;
        fraction = enter;
        return true;
    }

    // The rectangle is centered on from/to; angle is fixed, in radians. The target is a disk.
    public static bool TryRectangle(Vector2 from, Vector2 to, Vector2 targetFrom, Vector2 targetTo,
        Vector2 halfExtents, float angle, float targetRadius, out double fraction)
    {
        Validate(from, nameof(from)); Validate(to, nameof(to));
        Validate(targetFrom, nameof(targetFrom)); Validate(targetTo, nameof(targetTo));
        var x = (double)targetFrom.X - from.X; var y = (double)targetFrom.Y - from.Y;
        var dx = ((double)targetTo.X - targetFrom.X) - ((double)to.X - from.X);
        var dy = ((double)targetTo.Y - targetFrom.Y) - ((double)to.Y - from.Y);
        return TryRectangleRelative(x, y, dx, dy, halfExtents, angle, targetRadius, out fraction);
    }

    // World-space target-minus-shot offset/displacement; the rectangle's orientation is fixed.
    public static bool TryRectangleRelative(double x, double y, double dx, double dy,
        Vector2 halfExtents, float angle, float targetRadius, out double fraction)
    {
        ValidateRelative(x, y, dx, dy);
        Validate(halfExtents, nameof(halfExtents));
        if (halfExtents.X < 0 || halfExtents.Y < 0) throw new ArgumentOutOfRangeException(nameof(halfExtents));
        if (!float.IsFinite(angle)) throw new ArgumentOutOfRangeException(nameof(angle));
        if (!float.IsFinite(targetRadius) || targetRadius < 0) throw new ArgumentOutOfRangeException(nameof(targetRadius));
        var cos = Math.Cos(angle); var sin = Math.Sin(angle);
        return TryRectangleRelative(x, y, dx, dy, halfExtents, cos, sin, targetRadius, out fraction);
    }

    // Prepare with Math.Cos(angle)/Math.Sin(angle) in double precision once per fixed orientation.
    public static bool TryRectangleRelative(double x, double y, double dx, double dy,
        Vector2 halfExtents, double cos, double sin, float targetRadius, out double fraction)
    {
        ValidateRelative(x, y, dx, dy);
        Validate(halfExtents, nameof(halfExtents));
        if (halfExtents.X < 0 || halfExtents.Y < 0) throw new ArgumentOutOfRangeException(nameof(halfExtents));
        if (!double.IsFinite(cos)) throw new ArgumentOutOfRangeException(nameof(cos));
        if (!double.IsFinite(sin)) throw new ArgumentOutOfRangeException(nameof(sin));
        if (!float.IsFinite(targetRadius) || targetRadius < 0) throw new ArgumentOutOfRangeException(nameof(targetRadius));
        var localX = x * cos + y * sin; var localY = -x * sin + y * cos;
        var localDx = dx * cos + dy * sin; var localDy = -dx * sin + dy * cos;
        x = localX; y = localY; dx = localDx; dy = localDy;
        double hx = halfExtents.X, hy = halfExtents.Y, radius = targetRadius;
        fraction = 0;
        if (InsideCircle(Math.Max(Math.Abs(x) - hx, 0), Math.Max(Math.Abs(y) - hy, 0), radius)) return true;

        // Exact Minkowski sum: two side strips plus four quadrant-constrained corner arcs.
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
        // Cross-product discriminant avoids subtracting two enormous longitudinal terms.
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
