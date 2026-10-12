using System.Numerics;

namespace SpaceBattle.Game;

internal readonly record struct SceneCamera(int Width, int Height, float Scale, Vector2 Center, float OriginY)
{
    public Vector2 Screen(Vector2 world) => new(Width * .5f + (world.X - Center.X) * Scale,
        OriginY - (world.Y - Center.Y) * Scale);
    public bool Visible(Vector2 p, float extent) => p.X + extent >= 0 && p.Y + extent >= 0 &&
        p.X - extent <= Width && p.Y - extent <= Height;
}

internal static class BattleSceneRenderer
{
    public const string Revision = "web-scene-v1";
    public static readonly Vector4 Background = Rgb(8, 10, 12), DebrisColor = Rgb(96, 96, 96);
    private static readonly Vector4 Star = Rgb(38, 49, 53), BrightStar = Rgb(100, 117, 122), Border = Rgb(38, 51, 55);
    private static readonly Vector4 Cyan = new(.2f, .85f, .95f, 1), Red = new(1, .37f, .4f, 1);
    private static readonly Vector4 CyanArc = Rgb(93, 228, 241), RedArc = Rgb(255, 104, 111);
    private static readonly Vector4 Selection = Rgb(243, 250, 251), Target = Rgb(255, 195, 102);
    private static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1);
    public static float ShipSize(int kind, float radius, float scale) => Math.Max(kind >= 8 ? 13 : 5, radius * 2 * scale);
    public static float DebrisRadius(float radius, float scale) => Math.Max(1, radius * scale);

    // Scene-only presentation, also used by the native/Canvas comparison fixture. No ECS access.
    public static int Draw(Renderer renderer, BattleFrame frame, SceneCamera camera, int selected = -1,
        int observed = -1, int target = -1)
    {
        for (int i = 0; i < 170; i++)
        {
            float x = ((i * 7919 + 113) % 10007) / 10007f * camera.Width;
            float y = ((i * 3571 + 317) % 9973) / 9973f * camera.Height;
            bool bright = i % 7 == 0;
            renderer.Rect(x, y, bright ? 1.5f : 1, 1, bright ? BrightStar : Star);
        }
        float half = frame.ArenaHalfSize;
        var origin = camera.Screen(new(-half, half));
        float arenaSize = half * 2 * camera.Scale;
        renderer.Line(origin, origin + new Vector2(arenaSize, 0), 1, Border);
        renderer.Line(origin + new Vector2(arenaSize, 0), origin + new Vector2(arenaSize), 1, Border);
        renderer.Line(origin + new Vector2(arenaSize), origin + new Vector2(0, arenaSize), 1, Border);
        renderer.Line(origin + new Vector2(0, arenaSize), origin, 1, Border);
        foreach (var e in frame.Debris)
        {
            var p = camera.Screen(e.Position);
            float radius = DebrisRadius(e.Radius, camera.Scale);
            if (camera.Visible(p, radius)) renderer.Circle(p, radius, DebrisColor, filled: true);
        }
        foreach (var e in frame.Particles) if (!WarpEffect.Foreground(e.Kind)) Particle(e);
        foreach (var e in frame.Projectiles)
            renderer.Projectile(camera.Screen(e.Position), new Vector2(e.Velocity.X, -e.Velocity.Y) * camera.Scale,
                e.Radius * camera.Scale, e.Kind, e.Angle);
        int renderedShips = 0;
        foreach (var e in frame.Ships)
        {
            var p = camera.Screen(e.Position);
            float size = ShipSize(e.Kind, e.Radius, camera.Scale), radius = size * .5f;
            if (!camera.Visible(p, size * 1.5f)) continue;
            renderedShips++;
            var tint = e.Team == 0 ? Cyan : Red;
            renderer.Ship(p, radius, e.Angle, e.Kind, tint);
            if (ShipCatalog.IsWeaponsPlatform((ShipClass)e.Kind))
                renderer.Gun(p, Math.Max(1.5f, size * .12f), e.Angle, 0, e.Heat, tint, main: true);
            renderer.Arc(p, size * .55f, MathF.PI * .15f, MathF.PI * .85f, e.Team == 0 ? CyanArc : RedArc, 1.2f);
            if (e.Serial == selected) renderer.Circle(p, size * .75f + 5, Selection);
            if (e.Serial == observed) renderer.Brackets(p, size * .82f + 8, Selection, 2);
            if (e.Serial == target) renderer.Brackets(p, size * .82f + 8, Target, 1.6f);
        }
        foreach (var e in frame.Turrets)
        {
            var p = camera.Screen(e.Position);
            float radius = Math.Max(1.4f, e.Radius * camera.Scale);
            if (camera.Visible(p, radius * 4)) renderer.Gun(p, radius, e.Angle, e.Recoil, e.Heat,
                e.Team == 0 ? Cyan : Red, (int)e.Kind);
        }
        foreach (var e in frame.Particles) if (WarpEffect.Foreground(e.Kind)) Particle(e);
        return renderedShips;

        void Particle(RenderEntity e)
        {
            var p = camera.Screen(e.Position);
            float size = Math.Max(2, e.Radius * camera.Scale * 2);
            if (camera.Visible(p, size)) renderer.Particle(p, size, e.Angle, e.Kind, e.Color.W);
        }
    }
}
