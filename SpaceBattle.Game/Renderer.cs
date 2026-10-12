using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace SpaceBattle.Game;

internal sealed unsafe class Renderer : IDisposable
{
    private readonly GL gl;
    private readonly uint program, vao, buffer, texture;
    private readonly List<Vertex> vertices = new(200000);
    private readonly SpriteRect[] sprites = new SpriteRect[12];
    private readonly GunSprite[] gunSprites = new GunSprite[6];
    private readonly GunSprite[] projectileSprites = new GunSprite[5];
    private readonly GunSprite[] particleSprites = new GunSprite[4];
    private readonly GunSprite[] warpSprites = new GunSprite[4];
    private int atlasWidth, atlasHeight;
    private readonly record struct SpriteRect(int X, int Y, int Width, int Height);
    private readonly record struct GunSprite(SpriteRect Rect, Vector2 Pivot);
    public bool HasAtlas { get; }
    public bool HasGunAtlas { get; }
    public bool HasEffectsAtlas { get; }
    public bool HasWarpAtlas { get; }
    private readonly record struct Vertex(Vector2 Position, Vector2 Uv, Vector4 Color);
    public Renderer(GL gl)
    {
        this.gl = gl;
        uint Compile(ShaderType type, string source)
        {
            var shader = gl.CreateShader(type); gl.ShaderSource(shader, source); gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
            if (ok == 0) throw new InvalidOperationException(gl.GetShaderInfoLog(shader));
            return shader;
        }
        var vs = Compile(ShaderType.VertexShader, "#version 330 core\nlayout(location=0) in vec2 p;layout(location=1) in vec2 uv;layout(location=2) in vec4 c;out vec2 t;out vec4 color;void main(){gl_Position=vec4(p,0,1);t=uv;color=c;}");
        var fs = Compile(ShaderType.FragmentShader, "#version 330 core\nin vec2 t;in vec4 color;out vec4 result;uniform sampler2D atlas;void main(){vec4 s=t.x<0.0?vec4(1):texture(atlas,t);if(t.x>=0.0&&s.a<=0.1255)discard;result=color*s;}");
        program = gl.CreateProgram(); gl.AttachShader(program, vs); gl.AttachShader(program, fs); gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0) throw new InvalidOperationException(gl.GetProgramInfoLog(program));
        gl.DeleteShader(vs); gl.DeleteShader(fs);
        vao = gl.GenVertexArray(); buffer = gl.GenBuffer(); gl.BindVertexArray(vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
        for (uint i = 0; i < 3; i++) gl.EnableVertexAttribArray(i);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 32, (void*)0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 32, (void*)8);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 32, (void*)16);
        texture = gl.GenTexture(); gl.BindTexture(TextureTarget.Texture2D, texture);
        ImageResult? shipsImage = null, gunsImage = null, effectsImage = null, warpImage = null;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ships-atlas.png");
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            shipsImage = img;
            atlasWidth = img.Width; atlasHeight = img.Height;
            var manifestPath = Path.ChangeExtension(path, ".json");
            if (File.Exists(manifestPath))
            {
                using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = manifest.RootElement;
                if(root.GetProperty("width").GetInt32()!=img.Width || root.GetProperty("height").GetInt32()!=img.Height)
                    throw new InvalidDataException("Atlas dimensions do not match ships-atlas.json.");
                foreach(var entry in root.GetProperty("sprites").EnumerateArray())
                {
                    int kind=entry.GetProperty("class").GetInt32();
                    var rect=new SpriteRect(entry.GetProperty("x").GetInt32(),entry.GetProperty("y").GetInt32(),entry.GetProperty("width").GetInt32(),entry.GetProperty("height").GetInt32());
                    if(kind<0 || kind>=12 || rect.X<0 || rect.Y<0 || rect.Width<1 || rect.Height<1 || rect.X+rect.Width>img.Width || rect.Y+rect.Height>img.Height)
                        throw new InvalidDataException("Invalid atlas sprite rectangle.");
                    sprites[kind]=rect;
                }
                if(sprites.Any(s=>s.Width==0))throw new InvalidDataException("Atlas manifest needs all 12 cells.");
            }
            else
            {
                if(img.Width%4!=0 || img.Height%3!=0)throw new InvalidDataException("Nonuniform atlas requires ships-atlas.json.");
                for(int i=0;i<12;i++)sprites[i]=new(i%4*img.Width/4,i/4*img.Height/3,img.Width/4,img.Height/3);
            }
            HasAtlas = true;
        }
        var gunPath = Path.Combine(AppContext.BaseDirectory, "Assets", "guns-atlas.png");
        var gunManifestPath = Path.ChangeExtension(gunPath, ".json");
        if (File.Exists(gunPath) || File.Exists(gunManifestPath))
        {
            try
            {
                if (!File.Exists(gunPath) || !File.Exists(gunManifestPath))
                    throw new InvalidDataException("Both guns-atlas.png and guns-atlas.json are required.");
                using var stream = File.OpenRead(gunPath);
                gunsImage = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(gunManifestPath));
                var root = manifest.RootElement;
                if (root.GetProperty("width").GetInt32() != gunsImage.Width || root.GetProperty("height").GetInt32() != gunsImage.Height ||
                    root.GetProperty("alphaThreshold").GetInt32() != 32)
                    throw new InvalidDataException("Gun atlas dimensions or alphaThreshold (32) do not match the contract.");
                foreach (var entry in root.GetProperty("sprites").EnumerateArray())
                {
                    int kind = entry.GetProperty("kind").GetInt32();
                    var rect = new SpriteRect(entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32(),
                        entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32());
                    float pivotX = entry.GetProperty("pivotX").GetSingle(), pivotY = entry.GetProperty("pivotY").GetSingle();
                    if (kind < -1 || kind > 4 || rect.X < 0 || rect.Y < 0 || rect.Width < 1 || rect.Height < 1 ||
                        (long)rect.X + rect.Width > gunsImage.Width || (long)rect.Y + rect.Height > gunsImage.Height ||
                        !float.IsFinite(pivotX) || !float.IsFinite(pivotY) || pivotX < 0 || pivotY < 0 || pivotX > rect.Width || pivotY > rect.Height)
                        throw new InvalidDataException("Invalid gun kind, sprite rectangle or crop-relative pivot.");
                    if (gunSprites[kind + 1].Rect.Width != 0)
                        throw new InvalidDataException($"Duplicate gun atlas kind {kind}.");
                    gunSprites[kind + 1] = new(rect with { Y = checked(rect.Y + (shipsImage?.Height ?? 0)) }, new(pivotX / rect.Width, pivotY / rect.Height));
                }
                if (gunSprites.Any(s => s.Rect.Width == 0))
                    throw new InvalidDataException("Gun atlas needs mounting base kind -1 and all weapon kinds 0 through 4.");
                HasGunAtlas = true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                throw new InvalidDataException("Failed to load guns-atlas.png / guns-atlas.json: " + e.Message, e);
            }
        }
        var effectsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "effects-atlas.png");
        var effectsManifestPath = Path.ChangeExtension(effectsPath, ".json");
        if (File.Exists(effectsPath) || File.Exists(effectsManifestPath))
        {
            try
            {
                if (!File.Exists(effectsPath) || !File.Exists(effectsManifestPath))
                    throw new InvalidDataException("Both effects-atlas.png and effects-atlas.json are required.");
                using var stream = File.OpenRead(effectsPath);
                effectsImage = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(effectsManifestPath));
                var root = manifest.RootElement;
                if (root.GetProperty("width").GetInt32() != effectsImage.Width || root.GetProperty("height").GetInt32() != effectsImage.Height ||
                    root.GetProperty("alphaThreshold").GetInt32() != 32)
                    throw new InvalidDataException("Effects atlas dimensions or alphaThreshold (32) do not match the contract.");
                int yOffset = checked((shipsImage?.Height ?? 0) + (gunsImage?.Height ?? 0));
                foreach (var entry in root.GetProperty("sprites").EnumerateArray())
                {
                    string? family = entry.GetProperty("family").GetString();
                    int kind = entry.GetProperty("kind").GetInt32();
                    int count = family switch { "projectile" => 5, "particle" => 4, _ => 0 };
                    if (count == 0 || kind < 0 || kind >= count)
                        throw new InvalidDataException("Invalid effects atlas family or kind.");
                    var familySprites = family == "particle" ? particleSprites : projectileSprites;
                    var rect = new SpriteRect(entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32(),
                        entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32());
                    float pivotX = entry.GetProperty("pivotX").GetSingle(), pivotY = entry.GetProperty("pivotY").GetSingle();
                    if (rect.X < 0 || rect.Y < 0 || rect.Width < 1 || rect.Height < 1 ||
                        (long)rect.X + rect.Width > effectsImage.Width || (long)rect.Y + rect.Height > effectsImage.Height ||
                        !float.IsFinite(pivotX) || !float.IsFinite(pivotY) || pivotX < 0 || pivotY < 0 || pivotX >= rect.Width || pivotY >= rect.Height)
                        throw new InvalidDataException("Invalid effects sprite rectangle or crop-relative pivot.");
                    if (family == "particle" && (rect.Width != rect.Height || pivotX != rect.Width * .5f || pivotY != rect.Height * .5f))
                        throw new InvalidDataException("Particle sprites need square cells and centered pivots.");
                    if (familySprites[kind].Rect.Width != 0)
                        throw new InvalidDataException($"Duplicate effects atlas {family} kind {kind}.");
                    familySprites[kind] = new(rect with { Y = checked(rect.Y + yOffset) }, new(pivotX / rect.Width, pivotY / rect.Height));
                }
                if (projectileSprites.Any(s => s.Rect.Width == 0) || particleSprites.Any(s => s.Rect.Width == 0))
                    throw new InvalidDataException("Effects atlas needs projectile kinds 0 through 4 and particle kinds 0 through 3.");
                HasEffectsAtlas = true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                throw new InvalidDataException("Failed to load effects-atlas.png / effects-atlas.json: " + e.Message, e);
            }
        }
        int baseWidth = Math.Max(Math.Max(shipsImage?.Width ?? 0, gunsImage?.Width ?? 0), effectsImage?.Width ?? 0);
        int baseHeight = checked((shipsImage?.Height ?? 0) + (gunsImage?.Height ?? 0) + (effectsImage?.Height ?? 0));
        var warpPath = Path.Combine(AppContext.BaseDirectory, "Assets", "warp-atlas.png");
        var warpManifestPath = Path.ChangeExtension(warpPath, ".json");
        if (File.Exists(warpPath) || File.Exists(warpManifestPath))
        {
            try
            {
                if (!File.Exists(warpPath) || !File.Exists(warpManifestPath))
                    throw new InvalidDataException("Both warp-atlas.png and warp-atlas.json are required.");
                using var stream = File.OpenRead(warpPath);
                warpImage = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(warpManifestPath));
                var root = manifest.RootElement;
                if (root.GetProperty("width").GetInt32() != warpImage.Width || root.GetProperty("height").GetInt32() != warpImage.Height ||
                    root.GetProperty("alphaThreshold").GetInt32() != 32 || warpImage.Width != warpImage.Height || warpImage.Width % 2 != 0)
                    throw new InvalidDataException("Warp atlas needs matching dimensions, a square 2x2 grid and alphaThreshold 32.");
                int cell = warpImage.Width / 2;
                foreach (var entry in root.GetProperty("sprites").EnumerateArray())
                {
                    int kind = entry.GetProperty("kind").GetInt32();
                    if (kind < 0 || kind > 3 || (entry.TryGetProperty("family", out var family) && family.GetString() != "warp"))
                        throw new InvalidDataException("Invalid warp atlas family or kind.");
                    var rect = new SpriteRect(entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32(),
                        entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32());
                    float pivotX = entry.GetProperty("pivotX").GetSingle(), pivotY = entry.GetProperty("pivotY").GetSingle();
                    int cellX = kind % 2 * cell, cellY = kind / 2 * cell;
                    if (rect.Width < 1 || rect.Width != rect.Height || rect.X < cellX || rect.Y < cellY ||
                        (long)rect.X + rect.Width > cellX + cell || (long)rect.Y + rect.Height > cellY + cell ||
                        pivotX != rect.Width * .5f || pivotY != rect.Height * .5f)
                        throw new InvalidDataException("Warp sprites need square crops inside their own quadrant and centered pivots.");
                    if (warpSprites[kind].Rect.Width != 0)
                        throw new InvalidDataException($"Duplicate warp atlas kind {kind}.");
                    warpSprites[kind] = new(rect with { X = checked(rect.X + baseWidth) }, new(.5f, .5f));
                }
                if (warpSprites.Any(s => s.Rect.Width == 0))
                    throw new InvalidDataException("Warp atlas needs all four kinds 0 through 3.");
                HasWarpAtlas = true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                throw new InvalidDataException("Failed to load warp-atlas.png / warp-atlas.json: " + e.Message, e);
            }
        }
        if (shipsImage is not null || gunsImage is not null || effectsImage is not null || warpImage is not null)
        {
            // Keep the existing vertical stack unchanged; warp occupies a new right-hand strip.
            atlasWidth = checked(baseWidth + (warpImage?.Width ?? 0));
            atlasHeight = Math.Max(baseHeight, warpImage?.Height ?? 0);
            gl.GetInteger(GetPName.MaxTextureSize, out int maxTextureSize);
            if (atlasWidth > maxTextureSize || atlasHeight > maxTextureSize)
                throw new InvalidDataException("Combined ship/gun/effects/warp atlas exceeds OpenGL maximum texture size.");
            var pixels = new byte[checked(atlasWidth * atlasHeight * 4)];
            void CopyImage(ImageResult? img, int xOffset, int yOffset)
            {
                if (img is null) return;
                for (int y = 0; y < img.Height; y++)
                    Array.Copy(img.Data, y * img.Width * 4, pixels, ((y + yOffset) * atlasWidth + xOffset) * 4, img.Width * 4);
            }
            CopyImage(shipsImage, 0, 0);
            CopyImage(gunsImage, 0, shipsImage?.Height ?? 0);
            CopyImage(effectsImage, 0, checked((shipsImage?.Height ?? 0) + (gunsImage?.Height ?? 0)));
            CopyImage(warpImage, baseWidth, 0);
            fixed (byte* data = pixels) gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)atlasWidth, (uint)atlasHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }
        else { uint white = uint.MaxValue; gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, &white); }
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.Enable(EnableCap.Blend); gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }
    private int width, height;
    public void Begin(int w, int h) { width = w; height = h; vertices.Clear(); gl.Viewport(0, 0, (uint)w, (uint)h); var background = BattleSceneRenderer.Background; gl.ClearColor(background.X, background.Y, background.Z, 1); gl.Clear(ClearBufferMask.ColorBufferBit); }
    private void Add(Vector2 p, Vector4 c, Vector2 uv) => vertices.Add(new(new(p.X / width * 2 - 1, 1 - p.Y / height * 2), uv, c));
    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color) { var uv = new Vector2(-1); Add(a, color, uv); Add(b, color, uv); Add(c, color, uv); }
    public void Line(Vector2 a, Vector2 b, float thickness, Vector4 color, bool preserveSmallSegments = false)
    {
        var d = b - a; if (preserveSmallSegments ? d.LengthSquared() == 0 : d.LengthSquared() < .001f) return;
        var n = Vector2.Normalize(new Vector2(-d.Y, d.X)) * thickness * .5f;
        Triangle(a - n, a + n, b + n, color); Triangle(a - n, b + n, b - n, color);
    }
    public void Rect(float x, float y, float w, float h, Vector4 color) { Triangle(new(x,y),new(x+w,y),new(x+w,y+h),color); Triangle(new(x,y),new(x+w,y+h),new(x,y+h),color); }
    public void Circle(Vector2 p, float radius, Vector4 color, bool filled = false)
    {
        int n = (int)Math.Clamp(MathF.Ceiling(radius * 2), 20, 128);
        for (int i = 0; i < n; i++) { float a = i * MathF.Tau/n, b = (i+1)*MathF.Tau/n; var v = p + new Vector2(MathF.Cos(a),MathF.Sin(a))*radius; var w = p + new Vector2(MathF.Cos(b),MathF.Sin(b))*radius; if(filled) Triangle(p,v,w,color); else Line(v,w,1,color); }
    }
    public void Arc(Vector2 p, float radius, float start, float end, Vector4 color, float thickness = 1)
    {
        int segments = (int)Math.Clamp(MathF.Ceiling(radius * (end - start) / 3), 8, 128);
        for (int i = 0; i < segments; i++)
        {
            float a = start + (end - start) * i / segments, b = start + (end - start) * (i + 1) / segments;
            Line(p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius,
                p + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, thickness, color);
        }
    }
    public void Brackets(Vector2 p, float radius, Vector4 color, float thickness)
    {
        float arm = Math.Min(14, radius * .4f);
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
        {
            var corner = p + new Vector2(x, y) * radius;
            Line(corner, corner - new Vector2(x * arm, 0), thickness, color);
            Line(corner, corner - new Vector2(0, y * arm), thickness, color);
        }
    }
    public void Ship(Vector2 p, float radius, float angle, int kind, Vector4 color)
    {
        var forward = new Vector2(MathF.Cos(angle), -MathF.Sin(angle)); var side = new Vector2(-forward.Y, forward.X);
        if (!HasAtlas) { Triangle(p+forward*radius*1.6f,p-forward*radius+side*radius,p-forward*radius-side*radius,color); Line(p-forward*radius*.7f,p+forward*radius,1,new(.85f,.93f,1,1)); return; }
        var sprite=sprites[Math.Clamp(kind,0,11)];
        float u=(sprite.X+.5f)/atlasWidth,v=(sprite.Y+.5f)/atlasHeight,u2=(sprite.X+sprite.Width-.5f)/atlasWidth,v2=(sprite.Y+sprite.Height-.5f)/atlasHeight;
        float halfHeight = radius * 1.5f, halfWidth = halfHeight * sprite.Width / sprite.Height;
        var a=p-forward*halfHeight-side*halfWidth; var b=p+forward*halfHeight-side*halfWidth; var c=p+forward*halfHeight+side*halfWidth; var d=p-forward*halfHeight+side*halfWidth;
        Add(a,color,new(u,v2)); Add(b,color,new(u,v)); Add(c,color,new(u2,v)); Add(a,color,new(u,v2)); Add(c,color,new(u2,v)); Add(d,color,new(u2,v2));
    }
    private void EffectSpriteQuad(Vector2 p, Vector2 forward, float w, float h, GunSprite sprite, float alpha)
    {
        var side = new Vector2(-forward.Y, forward.X);
        var rect = sprite.Rect;
        // Source X points right; the crop pivot stays on the entity, including the projectile head.
        var a = p - forward * (sprite.Pivot.X * w) - side * (sprite.Pivot.Y * h);
        var b = a + forward * w; var c = b + side * h; var d = a + side * h;
        float u = (rect.X + .5f) / atlasWidth, v = (rect.Y + .5f) / atlasHeight;
        float u2 = (rect.X + rect.Width - .5f) / atlasWidth, v2 = (rect.Y + rect.Height - .5f) / atlasHeight;
        var color = new Vector4(1, 1, 1, alpha);
        Add(a,color,new(u,v)); Add(b,color,new(u2,v)); Add(c,color,new(u2,v2));
        Add(a,color,new(u,v)); Add(c,color,new(u2,v2)); Add(d,color,new(u,v2));
    }
    public void Projectile(Vector2 p, Vector2 screenVelocity, float radius, int kind, float angle = 0)
    {
        if (kind < 0 || kind > 4) throw new ArgumentOutOfRangeException(nameof(kind));
        float speed = screenVelocity.Length();
        var forward = speed > 0 ? screenVelocity / speed : new Vector2(MathF.Cos(angle), -MathF.Sin(angle));
        float length = Math.Max(kind == 4 ? 18 : kind == 3 ? 10 : 6,
            Math.Max(speed * (kind == 4 ? .07f : .025f), radius * (kind == 4 ? 4 : 2)));
        if (p.X < -length || p.Y < -length || p.X > width + length || p.Y > height + length) return;
        if (!HasEffectsAtlas) { Line(p-forward*length,p,Math.Max(1,radius*2),Vector4.One); return; }
        var sprite = projectileSprites[kind];
        EffectSpriteQuad(p,forward,length,length*sprite.Rect.Height/sprite.Rect.Width,sprite,1);
    }
    public void Particle(Vector2 p, float diameter, float angle, int kind, float alpha)
    {
        if (kind < 0 || kind > 7) throw new ArgumentOutOfRangeException(nameof(kind));
        diameter = Math.Max(2,diameter);
        var forward = new Vector2(MathF.Cos(angle), -MathF.Sin(angle));
        if (kind >= 4 ? !HasWarpAtlas : !HasEffectsAtlas)
        {
            var side = new Vector2(-forward.Y, forward.X);
            var a = p - (forward + side) * (diameter * .5f);
            var b = a + forward * diameter; var c = b + side * diameter; var d = a + side * diameter;
            var color = new Vector4(1, 1, 1, alpha);
            Triangle(a,b,c,color); Triangle(a,c,d,color);
            return;
        }
        EffectSpriteQuad(p,forward,diameter,diameter,kind >= 4 ? warpSprites[kind - 4] : particleSprites[kind],alpha);
    }
    private void GunSpriteQuad(Vector2 p, float radius, float angle, GunSprite sprite, Vector2 pivot, Vector4 color)
    {
        var forward = new Vector2(MathF.Cos(angle), -MathF.Sin(angle));
        var side = new Vector2(-forward.Y, forward.X);
        var rect = sprite.Rect;
        float w = radius * 2, h = w * rect.Height / rect.Width;
        var a = p - side * (pivot.X * w) + forward * ((pivot.Y - 1) * h);
        var b = p - side * (pivot.X * w) + forward * (pivot.Y * h);
        var c = b + side * w; var d = a + side * w;
        float u = (rect.X + .5f) / atlasWidth, v = (rect.Y + .5f) / atlasHeight;
        float u2 = (rect.X + rect.Width - .5f) / atlasWidth, v2 = (rect.Y + rect.Height - .5f) / atlasHeight;
        Add(a,color,new(u,v2)); Add(b,color,new(u,v)); Add(c,color,new(u2,v));
        Add(a,color,new(u,v2)); Add(c,color,new(u2,v)); Add(d,color,new(u2,v2));
    }
    public void Gun(Vector2 p, float radius, float angle, float recoil, float heat, Vector4 color, int kind = 4, bool main = false)
    {
        var forward = new Vector2(MathF.Cos(angle), -MathF.Sin(angle));
        var tint = color;
        var start = p - forward * (Math.Clamp(recoil,0,1)*radius*.7f);
        if (HasGunAtlas)
        {
            if (kind < 0 || kind > 4) throw new ArgumentOutOfRangeException(nameof(kind));
            var mount = gunSprites[0];
            GunSpriteQuad(p,radius,MathF.PI*.5f,mount,mount.Pivot,color);
            var head = gunSprites[kind + 1];
            GunSpriteQuad(start,radius,angle,head,head.Pivot,color);
            Circle(p,Math.Max(.5f,radius*.15f),heat>.75f?new(.82f,.48f,.24f,.8f):tint,true);
            return;
        }
        if (!main) Circle(p,radius,new Vector4(.28f,.32f,.34f,1)*color,true);
        var end = start + forward*radius*(main?4.5f:2.7f);
        Line(start,end,Math.Max(1,radius*.64f),heat>.75f?new(.67f,.46f,.3f,1):new Vector4(.39f,.47f,.48f,1)*color);
        Line(end-forward*radius*.55f,end,Math.Max(1.3f,radius*.84f),tint);
    }
    public void Text(float x, float y, string text, Vector4 color, int scale = 2)
    {
        foreach (char ch in text.ToUpperInvariant()) { var rows = BitmapFont.Get(ch); for(int row=0;row<7;row++) for(int col=0;col<5;col++) if((rows[row] & (1<<(4-col)))!=0) Rect(x+col*scale,y+row*scale,scale,scale,color); x+=6*scale; }
    }
    public void End()
    {
        gl.UseProgram(program); gl.BindVertexArray(vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer,buffer); gl.ActiveTexture(TextureUnit.Texture0); gl.BindTexture(TextureTarget.Texture2D,texture);
        var span=CollectionsMarshal.AsSpan(vertices); fixed(Vertex* ptr=span) gl.BufferData(BufferTargetARB.ArrayBuffer,(nuint)(span.Length*32),ptr,BufferUsageARB.StreamDraw);
        gl.DrawArrays(PrimitiveType.Triangles,0,(uint)span.Length);
    }
    public byte[] ReadPixels() { var pixels=new byte[width*height*4]; gl.Finish(); fixed(byte* p=pixels) gl.ReadPixels(0,0,(uint)width,(uint)height,PixelFormat.Rgba,PixelType.UnsignedByte,p); return pixels; }
    public void Dispose() { gl.DeleteBuffer(buffer); gl.DeleteVertexArray(vao); gl.DeleteProgram(program); gl.DeleteTexture(texture); }
}

internal static class ColliderOverlay
{
    private static Vector2 RectangleCorner(DebugCollider collider, int corner)
    {
        var local = new Vector2(corner is 0 or 3 ? -collider.HalfExtents.X : collider.HalfExtents.X,
            corner < 2 ? -collider.HalfExtents.Y : collider.HalfExtents.Y);
        float c = MathF.Cos(collider.Angle), s = MathF.Sin(collider.Angle);
        return collider.Position + new Vector2(local.X*c-local.Y*s,local.X*s+local.Y*c);
    }

    public static void Draw(Renderer renderer, List<DebugCollider> colliders, Func<Vector2, Vector2> screen, float scale)
    {
        foreach(var collider in colliders)
        {
            var color = collider.Kind switch
            {
                DebugColliderKind.Ship => new Vector4(.25f,1,.4f,1),
                DebugColliderKind.Projectile => new Vector4(1,.2f,.85f,1),
                _ => new Vector4(.55f,.8f,.85f,1)
            };
            if(collider.Shape==ProjectileShape.Rectangle)
            {
                for(int i=0;i<4;i++)
                    renderer.Line(screen(RectangleCorner(collider,i)),screen(RectangleCorner(collider,(i+1)%4)),1,color,true);
            }
            else
            {
                // Only tessellation varies with zoom; collider dimensions are never inflated.
                int segments=(int)Math.Clamp(MathF.Ceiling(collider.Radius*scale),32,192);
                for(int i=0;i<segments;i++)
                {
                    float a=i*MathF.Tau/segments,b=(i+1)*MathF.Tau/segments;
                    renderer.Line(screen(collider.Position+new Vector2(MathF.Cos(a),MathF.Sin(a))*collider.Radius),
                        screen(collider.Position+new Vector2(MathF.Cos(b),MathF.Sin(b))*collider.Radius),1,color,true);
                }
            }
        }
    }

    public static void ValidateGeometry()
    {
        var collider=new DebugCollider(1,DebugColliderKind.Projectile,ProjectileShape.Rectangle,new(10,20),MathF.PI/2,.25f,new(2,.25f));
        Vector2[] expected=[new(10.25f,18),new(10.25f,22),new(9.75f,22),new(9.75f,18)];
        for(int i=0;i<4;i++)
            if(Vector2.Distance(RectangleCorner(collider,i),expected[i])>.00001f)
                throw new InvalidOperationException("Collider geometry validation failed: rotated rectangle half-extents.");
    }
}
