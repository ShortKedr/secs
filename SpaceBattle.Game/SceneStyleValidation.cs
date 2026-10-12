using System.Numerics;
using System.Text.Json;

namespace SpaceBattle.Game;

internal static class SceneStyleValidation
{
    public static void Run(Renderer renderer, int width, int height, string? capturePath)
    {
        if (width < 1440 || height < 900) throw new ArgumentException("Scene style gallery needs at least 1440 x 900 pixels.");
        if (!renderer.HasAtlas || !renderer.HasGunAtlas || !renderer.HasEffectsAtlas || !renderer.HasWarpAtlas)
            throw new InvalidOperationException("Scene style validation requires all four production atlases.");
        var camera = new SceneCamera(width, height, 3, Vector2.Zero, height * .5f + 15);
        var frame = new BattleFrame { ArenaHalfSize = 210 };
        Vector2 World(float x, float y) => new((x - width * .5f) / camera.Scale, (camera.OriginY - y) / camera.Scale);
        RenderEntity Entity(int serial, int team, int kind, float x, float y, float radius, float angle = 0,
            Vector2 velocity = default, float alpha = 1) => new(serial, team, kind, World(x, y), velocity,
                angle, radius, 1, .85f, .2f, new(1, 1, 1, alpha));
        for (int i = 0; i < 11; i++)
            frame.Ships.Add(Entity(i + 1, i % 2, i, 105 + i * 122, 210, ShipCatalog.Radius((ShipClass)i), i * .2f));
        float[] radii = [.12f, .6f, 1.8f, 3, 6];
        for (int i = 0; i < radii.Length; i++)
            frame.Debris.Add(Entity(100 + i, -1, 0, 140.5f + i * 160, 380.5f, radii[i], alpha: i % 2 == 0 ? 1 : .1f));
        for (int i = 0; i < 5; i++)
            frame.Projectiles.Add(Entity(200 + i, -1, i, 140 + i * 245, 490, i == 4 ? .9f : .45f,
                angle: MathF.PI / 3, velocity: i == 3 ? Vector2.Zero : new(160, 60)));
        for (int i = 0; i < 8; i++)
            frame.Particles.Add(Entity(300 + i, 0, i, 110 + i * 172, 650, 10, i * .1f, alpha: i % 2 == 0 ? 1 : .5f));
        for (int i = 0; i < 5; i++)
            frame.Turrets.Add(new(400 + i, 1, i % 2, (WeaponKind)i, World(140 + i * 245, 785),
                i * .3f, 1.8f, i == 4 ? .9f : .2f, i % 2 == 0 ? 0 : .6f));

        Require(BattleSceneRenderer.ShipSize(0, 1, .1f) == 5 && BattleSceneRenderer.ShipSize(8, 8, .1f) == 13,
            "web ship minimum sizes by class");
        Require(BattleSceneRenderer.ShipSize(1, 2, 3) == 12 && BattleSceneRenderer.DebrisRadius(.12f, 3) == 1 &&
            BattleSceneRenderer.DebrisRadius(3, 3) == 9, "world-scale size mapping, no fixed debris stroke");
        renderer.Begin(width, height);
        Require(BattleSceneRenderer.Draw(renderer, frame, camera, selected: 2) == 11, "all eleven gallery hulls visible");
        renderer.End();
        var pixels = renderer.ReadPixels();
        var probes = new List<object>();
        foreach (var e in frame.Debris)
        {
            var p = camera.Screen(e.Position);
            float radius = BattleSceneRenderer.DebrisRadius(e.Radius, camera.Scale);
            var rgb = Pixel((int)p.X, (int)p.Y);
            Require(rgb.X == 96 && rgb.Y == 96 && rgb.Z == 96, "debris is opaque neutral #606060 regardless of ECS tint/alpha");
            int extent = (int)MathF.Ceiling(radius) + 2, area = 0, left = width, right = -1, top = height, bottom = -1;
            for (int y = (int)p.Y - extent; y <= (int)p.Y + extent; y++)
            for (int x = (int)p.X - extent; x <= (int)p.X + extent; x++)
            {
                var color = Pixel(x, y);
                if (color.X != 96 || color.Y != 96 || color.Z != 96) continue;
                area++; left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            Require(area > 0 && Math.Abs((right - left) - (bottom - top)) <= 1 &&
                right - left <= radius * 2 + 1 && right - left >= radius * 2 - 3, "round debris pixels track zoomed radius");
            if (radius >= 5) Require(Math.Abs(area / (Math.PI * radius * radius) - 1) < .15, "debris disk area, not a bright line");
            probes.Add(new { e.Serial, X = p.X, Y = p.Y, Radius = radius, Area = area, Rgb = new[] { rgb.X, rgb.Y, rgb.Z } });
        }
        renderer.Begin(width, height); BattleSceneRenderer.Draw(renderer, frame, camera, selected: 2);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) { renderer.Begin(width, height); BattleSceneRenderer.Draw(renderer, frame, camera, selected: 2); }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Require(allocated == 0, $"warmed native scene composition allocates zero bytes, got {allocated}");
        if (capturePath is not null)
        {
            PngCapture.Save(capturePath, width, height, pixels);
            var fixture = new { Schema = 1, Revision = BattleSceneRenderer.Revision, Width = width, Height = height,
                camera.Scale, CenterX = camera.Center.X, CenterY = camera.Center.Y, camera.OriginY, frame.ArenaHalfSize, Selected = 2,
                Ships = Rows(frame.Ships), Projectiles = Rows(frame.Projectiles), Debris = Rows(frame.Debris), Particles = Rows(frame.Particles),
                Turrets = frame.Turrets.Select(t => new double[] { t.Serial, t.Team, (int)t.Kind, t.Position.X, t.Position.Y, 0, 0,
                    t.Angle, t.Radius, t.ParentSerial, t.Recoil, t.Heat, 0, 0, 0, 1 }).ToArray(), DebrisProbes = probes, AllocatedBytes = allocated };
            File.WriteAllText(capturePath + ".json", JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine("PASS: native scene style, exact #606060 debris pixels/zoomed disk areas, web hull-size rules, all atlases and zero-allocation composition." +
            (capturePath is null ? "" : " Gallery: " + capturePath));

        Vector3 Pixel(int x, int y)
        {
            int i = ((height - 1 - y) * width + x) * 4;
            return new(pixels[i], pixels[i + 1], pixels[i + 2]);
        }
    }
    private static double[][] Rows(List<RenderEntity> rows) => rows.Select(e => new double[] {
        e.Serial, e.Team, e.Kind, e.Position.X, e.Position.Y, e.Velocity.X, e.Velocity.Y, e.Angle, e.Radius,
        e.Hull, e.Shield, e.Heat, e.Color.X, e.Color.Y, e.Color.Z, e.Color.W }).ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Scene style validation failed: " + message);
    }
}
