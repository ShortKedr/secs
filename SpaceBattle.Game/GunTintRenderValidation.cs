using System.Numerics;
using System.Text.Json;
using StbImageSharp;

namespace SpaceBattle.Game;

internal static class GunTintRenderValidation
{
    private const int Cell = 320, Top = 32, Rows = 4, Columns = 6;
    private const int ProbeExtent = Cell / 2 - 4;
    private const float Radius = 40, Angle = MathF.PI / 2, Threshold = 32.0025f;
    private const float Recoil = .25f;
    private const float Tolerance = 3;
    private static readonly Vector4 Cyan = new(.2f, .85f, .95f, 1);
    private static readonly Vector4 Red = new(1, .37f, .4f, 1);
    private static readonly Vector4 Orange = new(.82f, .48f, .24f, .8f);
    private readonly record struct Crop(int X, int Y, int Width, int Height, float PivotX, float PivotY);
    private readonly record struct Probe(Vector4 Texel, bool Safe);

    // Requires the current native GL context, before swap, and actual framebuffer dimensions.
    public static void Run(Renderer renderer, int width, int height, string? capturePath = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (width < Columns * Cell || height < Top + Rows * Cell)
            throw new ArgumentException($"Gun tint validation needs at least {Columns * Cell} x {Top + Rows * Cell} framebuffer pixels.");
        if (!renderer.HasAtlas || !renderer.HasGunAtlas)
            throw new InvalidOperationException("Gun tint validation requires both ship and gun atlases, not fallback geometry.");
        if (capturePath is not null)
        {
            string root = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            capturePath = Path.GetFullPath(capturePath);
            if (!capturePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Gun tint gallery capture must stay inside the current project directory.", nameof(capturePath));
        }

        var guns = Load("guns-atlas.png");
        var ships = Load("ships-atlas.png");
        Crop[] gunCrops = ReadCrops("guns-atlas.json", guns, true);
        Crop hull = ReadCrops("ships-atlas.json", ships, false)[0];
        var errors = new List<string>();
        byte[][] frames = new byte[3][];
        for (int pass = 0; pass < frames.Length; pass++)
        {
            renderer.Begin(width, height);
            renderer.Rect(0, 0, width, height, new Vector4(0, 0, 0, 1));
            renderer.Text(8, 8, $"GUN TINTS / {(pass == 0 ? "REST" : pass == 1 ? "RECOIL" : "RECOIL + HEAT")}", Vector4.One, 1);
            for (int row = 0; row < Rows; row++)
            {
                Vector4 color = Tint(row);
                renderer.Ship(Center(row, 0), Radius, Angle, 0, color);
                for (int kind = 0; kind < 5; kind++)
                    renderer.Gun(Center(row, kind + 1), Radius, 0, pass == 0 ? 0 : Recoil, pass == 2 ? .9f : .5f, color, kind);
            }
            renderer.End();
            frames[pass] = renderer.ReadPixels();
            if (frames[pass].Length != checked(width * height * 4))
                throw new InvalidOperationException("Gun tint validation: invalid framebuffer readback length.");
        }
        if (capturePath is not null) PngCapture.Save(capturePath, width, height, frames[0]);

        int total = 0;
        for (int row = 0; row < Rows; row++)
        {
            Vector4 tint = Tint(row);
            Vector2 hullCenter = Center(row, 0);
            float hullHeight = Radius * 3, hullWidth = hullHeight * hull.Width / hull.Height;
            int hullSamples = 0, hullBad = 0;
            for (int y = -60; y < 60; y++)
            for (int x = -60; x < 60; x++)
            {
                var source = Sample(ships, hull, x + .5f, y + .5f, hullWidth, hullHeight, .5f, .5f);
                if (!source.Safe || !Visible(source.Texel)) continue;
                Vector3 expected = Over(Vector3.Zero, source.Texel, tint);
                if (Error(expected, Pixel(frames[0], width, height, hullCenter, x, y)) > Tolerance) hullBad++;
                hullSamples++;
            }
            Report($"hull team={row % 2} alpha={tint.W:0.0}: samples={hullSamples}, mismatches={hullBad}",
                hullSamples >= 40 && hullBad == 0, errors);
            total += hullSamples;

            for (int kind = 0; kind < 5; kind++)
            {
                int tested = 0, bad = 0, baseOnly = 0, headOnly = 0, fixedMount = 0, movedHead = 0, hot = 0;
                string? firstMismatch = null;
                Vector2 center = Center(row, kind + 1);
                for (int y = -ProbeExtent; y < ProbeExtent; y++)
                for (int x = -ProbeExtent; x < ProbeExtent; x++)
                {
                    var mount = GunSample(guns, gunCrops[0], x + .5f, y + .5f);
                    // Head points right: source X is screen-down, source Y screen-left.
                    // Mount keeps its normal PI/2 orientation, exposing stationary pixels.
                    var rest = GunSample(guns, gunCrops[kind + 1], y + .5f, -x - .5f);
                    var recoil = GunSample(guns, gunCrops[kind + 1], y + .5f, -x - .5f - Recoil * Radius * .7f);
                    float distance = new Vector2(x + .5f, y + .5f).Length();
                    if (!mount.Safe || !rest.Safe || !recoil.Safe) continue;
                    bool indicator = distance < Radius * .15f * .65f;
                    if (distance <= Radius * .15f + 2 && !indicator) continue;
                    Vector3 underRest = Over(Over(Vector3.Zero, mount.Texel, tint), rest.Texel, tint);
                    Vector3 underRecoil = Over(Over(Vector3.Zero, mount.Texel, tint), recoil.Texel, tint);
                    for (int pass = 0; pass < 3; pass++)
                    {
                        Vector3 expected = pass == 0 ? underRest : underRecoil;
                        if (indicator) expected = SolidOver(expected, pass == 2 ? Orange : tint);
                        var actual = Pixel(frames[pass], width, height, center, x, y);
                        if (Error(expected, actual) > Tolerance)
                        {
                            bad++;
                            firstMismatch ??= $"pass={pass} local=({x},{y}) expected={expected} actual={actual}";
                        }
                        tested++;
                    }
                    if (indicator) { hot++; continue; }
                    if (Visible(mount.Texel) && Discarded(rest.Texel)) baseOnly++;
                    if (Visible(rest.Texel) && Discarded(mount.Texel)) headOnly++;
                    // Dim head texels still blend. Require genuinely exposed mount pixels in
                    // both reference poses, then verify their framebuffer values stay fixed.
                    if (Visible(mount.Texel) && Discarded(rest.Texel) && Discarded(recoil.Texel))
                    {
                        if (Error(Pixel(frames[0], width, height, center, x, y), Pixel(frames[1], width, height, center, x, y)) <= 1)
                            fixedMount++;
                    }
                    if (Discarded(mount.Texel) && Error(underRest, underRecoil) > 8 &&
                        Error(Pixel(frames[0], width, height, center, x, y), Pixel(frames[1], width, height, center, x, y)) > 5)
                        movedHead++;
                }
                string summary = $"gun kind={kind} team={row % 2} alpha={tint.W:0.0}: samples={tested}, mismatches={bad}, baseOnly={baseOnly}, headOnly={headOnly}, fixedMount={fixedMount}, movedHead={movedHead}, heat={hot}";
                bool ok = tested >= 1000 && bad == 0 && baseOnly >= 12 && headOnly >= 12 && fixedMount >= 12 && movedHead >= 12 && hot >= 8;
                Report(summary + (ok || firstMismatch is null ? "" : $"; first {firstMismatch}"), ok, errors);
                total += tested;
            }
        }
        if (errors.Count != 0)
            throw new InvalidOperationException("Gun tint native GL validation failed:\n" + string.Join("\n", errors));
        Console.WriteLine($"PASS: gun tint native GL validation, 5 kinds, both teams, alpha 1/0.4, hull/base/head, recoil and orange heat; {total} RGB probes." +
            (capturePath is null ? "" : $" Gallery: {capturePath}"));
    }

    private static Vector4 Tint(int row)
    {
        var color = row % 2 == 0 ? Cyan : Red;
        color.W = row < 2 ? 1 : .4f;
        return color;
    }

    private static Vector2 Center(int row, int column) => new(column * Cell + Cell / 2, Top + row * Cell + Cell / 2);
    private static ImageResult Load(string name)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", name));
        return ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
    }

    private static Crop[] ReadCrops(string name, ImageResult image, bool gun)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", name)));
        var root = manifest.RootElement;
        if (root.GetProperty("width").GetInt32() != image.Width || root.GetProperty("height").GetInt32() != image.Height ||
            root.GetProperty("alphaThreshold").GetInt32() != 32)
            throw new InvalidDataException($"{name}: reference dimensions or alpha threshold differ from the contract.");
        var crops = new Crop[gun ? 6 : 12];
        foreach (var entry in root.GetProperty("sprites").EnumerateArray())
        {
            int index = entry.GetProperty(gun ? "kind" : "class").GetInt32() + (gun ? 1 : 0);
            int x = entry.GetProperty("x").GetInt32(), y = entry.GetProperty("y").GetInt32();
            int w = entry.GetProperty("width").GetInt32(), h = entry.GetProperty("height").GetInt32();
            float px = gun ? entry.GetProperty("pivotX").GetSingle() : w * .5f;
            float py = gun ? entry.GetProperty("pivotY").GetSingle() : h * .5f;
            if (index < 0 || index >= crops.Length || crops[index].Width != 0 || w < 2 || h < 2 || x < 0 || y < 0 ||
                (long)x + w > image.Width || (long)y + h > image.Height || !float.IsFinite(px) || !float.IsFinite(py) ||
                px < 0 || px > w || py < 0 || py > h)
                throw new InvalidDataException($"{name}: invalid or duplicate reference crop {index}.");
            crops[index] = new(x, y, w, h, px / w, py / h);
        }
        if (crops.Any(c => c.Width == 0)) throw new InvalidDataException($"{name}: missing reference crops.");
        return crops;
    }

    private static Probe GunSample(ImageResult image, Crop crop, float x, float y) =>
        Sample(image, crop, x, y, Radius * 2, Radius * 2 * crop.Height / crop.Width, crop.PivotX, crop.PivotY);

    private static Probe Sample(ImageResult image, Crop crop, float x, float y, float w, float h, float px, float py)
    {
        // At angle PI/2 source X is screen-right and source Y is screen-down.
        // Keep original aspect and crop-relative pivot; UV endpoints are texel centers.
        float dx = x + px * w, dy = y + py * h;
        if (dx < -1 || dx > w + 1 || dy < -1 || dy > h + 1) return new(Vector4.Zero, true);
        if (dx <= 1 || dx >= w - 1 || dy <= 1 || dy >= h - 1) return new(Vector4.Zero, false);
        float tx = crop.X + dx / w * (crop.Width - 1), ty = crop.Y + dy / h * (crop.Height - 1);
        int ix = (int)MathF.Floor(tx), iy = (int)MathF.Floor(ty);
        Vector4 Texel(int sx, int sy)
        {
            int offset = (sy * image.Width + sx) * 4;
            return new(image.Data[offset], image.Data[offset + 1], image.Data[offset + 2], image.Data[offset + 3]);
        }
        Vector4 sample = Vector4.Lerp(Vector4.Lerp(Texel(ix, iy), Texel(ix + 1, iy), tx - ix),
            Vector4.Lerp(Texel(ix, iy + 1), Texel(ix + 1, iy + 1), tx - ix), ty - iy);
        return new(sample.W <= Threshold ? Vector4.Zero : sample, Math.Abs(sample.W - Threshold) > 2);
    }

    private static bool Discarded(Vector4 source) => source.W <= Threshold;
    private static bool Visible(Vector4 source) => !Discarded(source) && Math.Max(source.X, Math.Max(source.Y, source.Z)) > 48;
    private static Vector3 Over(Vector3 background, Vector4 source, Vector4 tint)
    {
        float alpha = source.W / 255 * tint.W;
        return new Vector3(source.X * tint.X, source.Y * tint.Y, source.Z * tint.Z) * alpha + background * (1 - alpha);
    }
    private static Vector3 SolidOver(Vector3 background, Vector4 color) =>
        new Vector3(color.X, color.Y, color.Z) * (255 * color.W) + background * (1 - color.W);
    private static Vector3 Pixel(byte[] pixels, int width, int height, Vector2 center, int x, int y)
    {
        int offset = ((height - 1 - ((int)center.Y + y)) * width + (int)center.X + x) * 4;
        return new(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }
    private static float Error(Vector3 a, Vector3 b)
    {
        var delta = Vector3.Abs(a - b);
        return Math.Max(delta.X, Math.Max(delta.Y, delta.Z));
    }
    private static void Report(string summary, bool ok, List<string> errors)
    {
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + summary);
        if (!ok) errors.Add(summary);
    }
}
