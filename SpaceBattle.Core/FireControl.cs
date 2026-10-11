using System.Numerics;

namespace SpaceBattle;

public readonly record struct AimSolution(Vector2 Direction, float InterceptTime);

public static class FireControl
{
    public const float MaxSpreadRadians = 0.03f;

    public static AimSolution Predict(Vector2 shooterPosition, Vector2 shooterVelocity,
        Vector2 targetPosition, Vector2 targetVelocity, float projectileSpeed, float muzzleOffset)
    {
        var offset = targetPosition - shooterPosition;
        var relativeVelocity = targetVelocity - shooterVelocity;
        double speed = projectileSpeed, muzzle = muzzleOffset;
        double a = Vector2.Dot(relativeVelocity, relativeVelocity) - speed * speed;
        double b = 2 * (Vector2.Dot(offset, relativeVelocity) - speed * muzzle);
        double c = Vector2.Dot(offset, offset) - muzzle * muzzle;
        double time = double.PositiveInfinity;
        // The muzzle advances along the firing direction; projectile velocity inherits the ship's velocity.
        if (c > 0 && speed > 0)
        {
            if (Math.Abs(a) < 1e-8)
            {
                if (b < 0) time = -c / b;
            }
            else
            {
                double discriminant = b * b - 4 * a * c;
                if (discriminant >= 0)
                {
                    double q = -0.5 * (b + Math.CopySign(Math.Sqrt(discriminant), b));
                    Select(q / a);
                    if (q != 0) Select(c / q);
                }
            }
        }
        if (!double.IsFinite(time) || time > float.MaxValue) time = 0;
        var intercept = offset + relativeVelocity * (float)time;
        var direction = intercept.LengthSquared() > 1e-12f ? Vector2.Normalize(intercept) : Vector2.UnitX;
        return new(direction, (float)time);

        void Select(double candidate)
        {
            if (candidate > 0 && candidate < time) time = candidate;
        }
    }

    public static float Spread(uint seed, int shooterSerial, int tick, int slot, out bool imprecise)
    {
        var rng = new RandomStream(seed ^ unchecked((uint)shooterSerial * 0x9E3779B9u) ^
            unchecked((uint)tick * 0x85EBCA6Bu) ^ unchecked((uint)(slot + 1) * 0xC2B2AE35u));
        imprecise = (rng.Next() & 3) == 0;
        return imprecise ? rng.Range(-MaxSpreadRadians, MaxSpreadRadians) : 0;
    }
}
