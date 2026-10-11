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
    private int atlasWidth, atlasHeight;
    private readonly record struct SpriteRect(int X, int Y, int Width, int Height);
    public bool HasAtlas { get; }
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
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ships-atlas.png");
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
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
            fixed (byte* data = img.Data) gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)img.Width, (uint)img.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            HasAtlas = true;
        }
        else { uint white = uint.MaxValue; gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, &white); }
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.Enable(EnableCap.Blend); gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }
    private int width, height;
    public void Begin(int w, int h) { width = w; height = h; vertices.Clear(); gl.Viewport(0, 0, (uint)w, (uint)h); gl.ClearColor(.012f, .02f, .035f, 1); gl.Clear(ClearBufferMask.ColorBufferBit); }
    private void Add(Vector2 p, Vector4 c, Vector2 uv) => vertices.Add(new(new(p.X / width * 2 - 1, 1 - p.Y / height * 2), uv, c));
    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color) { var uv = new Vector2(-1); Add(a, color, uv); Add(b, color, uv); Add(c, color, uv); }
    public void Line(Vector2 a, Vector2 b, float thickness, Vector4 color)
    {
        var d = b - a; if (d.LengthSquared() < .001f) return;
        var n = Vector2.Normalize(new Vector2(-d.Y, d.X)) * thickness * .5f;
        Triangle(a - n, a + n, b + n, color); Triangle(a - n, b + n, b - n, color);
    }
    public void Rect(float x, float y, float w, float h, Vector4 color) { Triangle(new(x,y),new(x+w,y),new(x+w,y+h),color); Triangle(new(x,y),new(x+w,y+h),new(x,y+h),color); }
    public void Circle(Vector2 p, float radius, Vector4 color, bool filled = false)
    {
        const int n = 20;
        for (int i = 0; i < n; i++) { float a = i * MathF.Tau/n, b = (i+1)*MathF.Tau/n; var v = p + new Vector2(MathF.Cos(a),MathF.Sin(a))*radius; var w = p + new Vector2(MathF.Cos(b),MathF.Sin(b))*radius; if(filled) Triangle(p,v,w,color); else Line(v,w,1,color); }
    }
    public void Ship(Vector2 p, float radius, float angle, int kind, Vector4 color)
    {
        var forward = new Vector2(MathF.Cos(angle), -MathF.Sin(angle)); var side = new Vector2(-forward.Y, forward.X);
        if (!HasAtlas) { Triangle(p+forward*radius*1.6f,p-forward*radius+side*radius,p-forward*radius-side*radius,color); Line(p-forward*radius*.7f,p+forward*radius,1,new(.85f,.93f,1,1)); return; }
        var sprite=sprites[Math.Clamp(kind,0,11)];
        float u=(sprite.X+.5f)/atlasWidth,v=(sprite.Y+.5f)/atlasHeight,u2=(sprite.X+sprite.Width-.5f)/atlasWidth,v2=(sprite.Y+sprite.Height-.5f)/atlasHeight;
        float largest=Math.Max(sprite.Width,sprite.Height), halfWidth=radius*sprite.Width/largest, halfHeight=radius*sprite.Height/largest;
        var a=p-forward*halfHeight-side*halfWidth; var b=p+forward*halfHeight-side*halfWidth; var c=p+forward*halfHeight+side*halfWidth; var d=p-forward*halfHeight+side*halfWidth;
        Add(a,color,new(u,v2)); Add(b,color,new(u,v)); Add(c,color,new(u2,v)); Add(a,color,new(u,v2)); Add(c,color,new(u2,v)); Add(d,color,new(u2,v2));
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
