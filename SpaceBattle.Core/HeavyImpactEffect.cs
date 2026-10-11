namespace SpaceBattle;

public static class HeavyImpactEffect
{
    public const int Kind = 3;
    public const int LifetimeTicks = 36;

    public static float CoreRadius(float damage) => Math.Clamp(MathF.Sqrt(MathF.Max(0, damage)) * 0.4f, 3.6f, 7.2f);

    public static void Present(in Particle particle, int tick, out float radius, out float alpha)
    {
        var age = Math.Clamp(tick - (particle.ExpiresTick - LifetimeTicks), 0, LifetimeTicks);
        var phase = age / (float)LifetimeTicks;
        var expansion = Math.Min(age / 6f, 1);
        radius = particle.Size * (0.65f + 0.45f * expansion + 0.15f * phase);
        var fade = Math.Clamp((age - 3f) / (LifetimeTicks - 3), 0, 1);
        alpha = (1 - fade) * (1 - fade);
    }
}
