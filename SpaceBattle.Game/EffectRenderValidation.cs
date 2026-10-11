using System.Numerics;
using System.Text.Json;
using StbImageSharp;

namespace SpaceBattle.Game;

internal static class EffectRenderValidation
{
    private const int Diameter = 96, Cell = 128, Top = 48, RowStride = Cell + 20;
    private const int ChannelTolerance = 3;
    private readonly record struct Crop(int X, int Y, int Size);
    private readonly record struct Bounds(int Left, int Top, int Right, int Bottom)
    {
        public static Bounds Empty => new(int.MaxValue, int.MaxValue, -1, -1);
        public Bounds Include(int x, int y) => new(Math.Min(Left, x), Math.Min(Top, y), Math.Max(Right, x), Math.Max(Bottom, y));
        public bool HasPixels => Right >= Left;
        public int Distance(Bounds other) => Math.Max(Math.Max(Math.Abs(Left - other.Left), Math.Abs(Top - other.Top)),
            Math.Max(Math.Abs(Right - other.Right), Math.Abs(Bottom - other.Bottom)));
    }

    // Run on the current GL context before buffer swap; width/height must be framebuffer dimensions.
    public static void Run(Renderer renderer, int width, int height, string? capturePath = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (width < Cell * 4 || height < Top + RowStride + Cell + 16)
            throw new ArgumentException($"Effects validation needs a framebuffer of at least {Cell * 4} x {Top + RowStride + Cell + 16}.");
        if (!renderer.HasEffectsAtlas)
            throw new InvalidOperationException("Effects pixel validation requires the effects atlas, not procedural fallback.");
        if (capturePath is not null)
        {
            var root = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            capturePath = Path.GetFullPath(capturePath);
            if (!capturePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Effects gallery capture must stay inside the current project directory.", nameof(capturePath));
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "effects-atlas.png");
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var crops = ReadCrops(Path.ChangeExtension(path, ".json"), image);
        renderer.Begin(width, height);
        renderer.Rect(0, 0, width, height, new Vector4(0, 0, 0, 1));
        renderer.Text(8, 8, "PARTICLES: ALPHA 1 / 0.25", Vector4.One, 1);
        for (int row = 0; row < 2; row++)
        for (int kind = 0; kind < 4; kind++)
        {
            int x = kind * Cell, y = Top + row * RowStride;
            renderer.Text(x + 8, y - 12, $"KIND {kind} A {(row == 0 ? "1" : "0.25")}", Vector4.One, 1);
            renderer.Particle(new Vector2(x + Cell / 2, y + Cell / 2), Diameter, 0, kind, row == 0 ? 1 : .25f);
        }
        renderer.End();
        var pixels = renderer.ReadPixels();
        if (pixels.Length != checked(width * height * 4))
            throw new InvalidOperationException("Effects validation: framebuffer readback length is invalid.");
        if (capturePath is not null) PngCapture.Save(capturePath, width, height, pixels);

        var errors = new List<string>();
        int passed = 0, checkedPixels = 0;
        for (int row = 0; row < 2; row++)
        for (int kind = 0; kind < 4; kind++)
        {
            float fade = row == 0 ? 1 : .25f;
            int tested = 0, mismatched = 0, skipped = 0, visible = 0, actualVisible = 0, maxError = 0;
            double absoluteError = 0, expectedEnergy = 0, actualEnergy = 0;
            var expectedBounds = Bounds.Empty;
            var actualBounds = Bounds.Empty;
            int firstX = -1, firstY = -1;
            Vector3 firstExpected = default, firstActual = default;
            for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                int sx = x - (Cell - Diameter) / 2, sy = y - (Cell - Diameter) / 2;
                var sample = sx >= 0 && sx < Diameter && sy >= 0 && sy < Diameter
                    ? Sample(image, crops[kind], sx, sy) : Vector4.Zero;
                // Only discard-boundary pixels are ambiguous across GL filtering precision.
                if (Math.Abs(sample.W - 32.0025f) <= 1) { skipped++; continue; }
                Vector3 expected = sample.W <= 32.0025f ? Vector3.Zero
                    : new Vector3(sample.X, sample.Y, sample.Z) * (sample.W / 255 * fade);
                int offset = ((height - 1 - (Top + row * RowStride + y)) * width + kind * Cell + x) * 4;
                var actual = new Vector3(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
                var delta = Vector3.Abs(actual - expected);
                int error = (int)Math.Ceiling(Math.Max(delta.X, Math.Max(delta.Y, delta.Z)));
                maxError = Math.Max(maxError, error);
                absoluteError += delta.X + delta.Y + delta.Z;
                expectedEnergy += expected.X + expected.Y + expected.Z;
                actualEnergy += actual.X + actual.Y + actual.Z;
                tested++;
                if (error > ChannelTolerance)
                {
                    mismatched++;
                    if (firstX < 0) { firstX = x; firstY = y; firstExpected = expected; firstActual = actual; }
                }
                float shapeThreshold = 12 * fade;
                if (Math.Max(expected.X, Math.Max(expected.Y, expected.Z)) > shapeThreshold + .5f)
                {
                    visible++;
                    expectedBounds = expectedBounds.Include(x, y);
                }
                if (Math.Max(actual.X, Math.Max(actual.Y, actual.Z)) > shapeThreshold)
                {
                    actualVisible++;
                    actualBounds = actualBounds.Include(x, y);
                }
            }
            checkedPixels += tested;
            double meanError = absoluteError / (tested * 3);
            double energyError = Math.Abs(actualEnergy - expectedEnergy) / Math.Max(1, expectedEnergy);
            bool shapeOk = visible >= 32 && actualBounds.HasPixels && expectedBounds.HasPixels &&
                expectedBounds.Distance(actualBounds) <= 2 && Math.Abs(actualVisible - visible) <= Math.Max(12, visible * .03);
            // Guard pixels outside the centered square are included, so misplaced/oversized quads also fail.
            bool ok = skipped <= Cell * Cell / 10 && mismatched <= Math.Max(4, tested / 200) &&
                meanError <= .65 && energyError <= .03 && shapeOk;
            string summary = FormattableString.Invariant($"particle kind={kind} alpha={fade:0.##}: pixels={tested}, discarded-boundary={skipped}, mismatches={mismatched}, maxChannelError={maxError}, meanError={meanError:0.000}, energyError={energyError:P2}, visible={actualVisible}/{visible}, bounds={actualBounds}/{expectedBounds}");
            Console.WriteLine((ok ? "PASS: " : "FAIL: ") + summary);
            if (ok) passed++;
            else errors.Add(summary + (firstX < 0 ? "" : FormattableString.Invariant($"; first mismatch local=({firstX},{firstY}) expected RGB=({firstExpected.X:0.0},{firstExpected.Y:0.0},{firstExpected.Z:0.0}) actual RGB=({firstActual.X},{firstActual.Y},{firstActual.Z})")));
        }
        if (errors.Count != 0)
            throw new InvalidOperationException($"Effects pixel validation failed: {passed}/8 cases passed.\n" + string.Join("\n", errors));
        Console.WriteLine($"PASS: effects pixel validation, {passed}/8 cases, 4 particle kinds, 2 opacities, {checkedPixels} RGB pixels checked." +
            (capturePath is null ? "" : $" Gallery: {capturePath}"));
    }

    private static Crop[] ReadCrops(string path, ImageResult image)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(path));
        var root = manifest.RootElement;
        if (root.GetProperty("width").GetInt32() != image.Width || root.GetProperty("height").GetInt32() != image.Height ||
            root.GetProperty("alphaThreshold").GetInt32() != 32)
            throw new InvalidDataException("Effects reference requires matching PNG dimensions and alphaThreshold 32.");
        var crops = new Crop[4];
        foreach (var entry in root.GetProperty("sprites").EnumerateArray())
        {
            if (entry.GetProperty("family").GetString() != "particle") continue;
            int kind = entry.GetProperty("kind").GetInt32();
            int x = entry.GetProperty("x").GetInt32(), y = entry.GetProperty("y").GetInt32();
            int w = entry.GetProperty("width").GetInt32(), h = entry.GetProperty("height").GetInt32();
            float px = entry.GetProperty("pivotX").GetSingle(), py = entry.GetProperty("pivotY").GetSingle();
            if (kind < 0 || kind >= 4 || w < 2 || w != h || x < 0 || y < 0 ||
                (long)x + w > image.Width || (long)y + h > image.Height ||
                !float.IsFinite(px) || !float.IsFinite(py) || Math.Abs(px - w * .5f) > .001f || Math.Abs(py - h * .5f) > .001f)
                throw new InvalidDataException($"Effects reference particle kind {kind} requires an in-bounds square crop and centered pivot.");
            if (crops[kind].Size != 0) throw new InvalidDataException($"Duplicate reference particle kind {kind}.");
            crops[kind] = new Crop(x, y, w);
        }
        if (crops.Any(c => c.Size == 0)) throw new InvalidDataException("Effects reference needs all four particle kinds (0 through 3).");
        return crops;
    }

    private static Vector4 Sample(ImageResult image, Crop crop, int x, int y)
    {
        // UV endpoints are texel centers; GL maps u to u * atlasWidth - 0.5.
        // At each destination pixel center this reduces to cropOrigin + (p + 0.5) / diameter * (size - 1).
        float tx = crop.X + (x + .5f) / Diameter * (crop.Size - 1);
        float ty = crop.Y + (y + .5f) / Diameter * (crop.Size - 1);
        int ix = (int)MathF.Floor(tx), iy = (int)MathF.Floor(ty);
        Vector4 Texel(int sx, int sy)
        {
            int offset = (sy * image.Width + sx) * 4;
            return new Vector4(image.Data[offset], image.Data[offset + 1], image.Data[offset + 2], image.Data[offset + 3]);
        }
        return Vector4.Lerp(Vector4.Lerp(Texel(ix, iy), Texel(ix + 1, iy), tx - ix),
            Vector4.Lerp(Texel(ix, iy + 1), Texel(ix + 1, iy + 1), tx - ix), ty - iy);
    }
}
