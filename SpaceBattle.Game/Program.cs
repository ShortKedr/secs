using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SpaceBattle;
using SpaceBattle.Game;

System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
try { using var app = new BattleApp(args); app.Run(); return 0; }
catch(Exception e) { Console.Error.WriteLine(e); return 1; }

internal sealed class BattleApp : IDisposable
{
    private readonly IWindow window;
    private readonly BattleConfig config;
    private Battle<SecsStore> battle;
    private readonly BattleFrame frame = new();
    private Renderer? renderer;
    private IInputContext? input;
    private GL? gl;
    private FrontendProfiler? profiler;
    private readonly int profileFrames, profileWarmup;
    private readonly string? profileOutput, profileCsv;
    private readonly bool hidden;
    private int replayStartTick;
    private readonly string? capture;
    private readonly int captureTick;
    private readonly float initialZoom;
    private readonly bool exitCapture, validate, validateControls;
    private bool controlsVerified;
    private bool captured, paused;
    private int selected = -1;
    private float zoom = 1, speed = 1;
    private Vector2 center, previousMouse;
    private double accumulator;
    private readonly Queue<double> timings = new();
    private double p99;
    private struct TeamPopulation : IProcessor<Identity>
    {
        public int Cyan, Red;
        public void Execute(EntityKey entity,ref Identity identity) {if(identity.Team==0)Cyan++;else if(identity.Team==1)Red++;}
    }
    private static readonly Vector4 White = new(.82f,.88f,.94f,1), Cyan = new(.2f,.85f,.95f,1), Red = new(1,.37f,.4f,1);
    public BattleApp(string[] args)
    {
        string? Value(string key) { int i=Array.IndexOf(args,key); if(i<0) return null; if(i+1>=args.Length) throw new ArgumentException($"Missing value for {key}"); return args[i+1]; }
        int Number(string key,int fallback) => int.TryParse(Value(key),out var n)?n:fallback;
        int ProfileNumber(string key,int fallback)
        {
            var value=Value(key);if(value is null)return fallback;
            if(!int.TryParse(value,out var n))throw new ArgumentException($"{key} requires an integer.");
            return n;
        }
        string ProjectOutput(string path)
        {
            var root=Path.GetFullPath(Environment.CurrentDirectory);
            var full=Path.GetFullPath(path);
            if(!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output files must stay inside the current project directory.");
            return full;
        }
        capture=Value("--capture"); captureTick=Number("--capture-tick",0); exitCapture=args.Contains("--exit-after-capture");validate=args.Contains("--validate-render");
        validateControls=args.Contains("--validate-controls");
        hidden=args.Contains("--hidden");
        profileFrames=ProfileNumber("--profile-frames",0);
        profileWarmup=ProfileNumber("--profile-warmup",120);
        if(profileFrames<0||profileFrames>1_000_000||profileWarmup<0||profileWarmup>1_000_000)
            throw new ArgumentException("Profile frame counts must be between 0 and 1000000.");
        if(args.Contains("--profile-frames")&&profileFrames==0)throw new ArgumentException("--profile-frames must be positive.");
        if(profileFrames>0)
        {
            if(capture is not null)throw new ArgumentException("Run profiling and framebuffer capture separately to exclude capture stalls from measurements.");
            profileOutput=ProjectOutput(Value("--profile-output")??"docs/space-battle/frontend.json");
            if(Value("--profile-csv") is { } csv)profileCsv=ProjectOutput(csv);
            if(profileOutput.Equals(profileCsv,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Profile JSON and CSV paths must differ.");
        }
        else if(args.Contains("--profile-warmup")||args.Contains("--profile-output")||args.Contains("--profile-csv"))
            throw new ArgumentException("Profile options require --profile-frames N.");
        var zoomArgument=Value("--zoom");
        initialZoom=1;
        if(zoomArgument is not null)
        {
            if(!float.TryParse(zoomArgument,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var requestedZoom) ||
               !float.IsFinite(requestedZoom) || requestedZoom<.25f || requestedZoom>24)
                throw new ArgumentException("--zoom must be a finite number from 0.25 to 24.");
            initialZoom=requestedZoom;
        }
        if(captureTick<0) throw new ArgumentOutOfRangeException("--capture-tick");
        if((exitCapture||validate)&&capture is null) throw new ArgumentException("Capture options require --capture PATH.");
        if(capture is not null)
        {
            capture=ProjectOutput(capture);
        }
        config=new BattleConfig { ShipsPerTeam=Number("--ships",Number("--ships-per-team",96)), Seed=(uint)Number("--seed",7331) };config.Validate();
        battle=new(new SecsStore(),config);
        var options=WindowOptions.Default;
        int width=Number("--width",1440), height=Number("--height",900);
        if(width<960||height<540)throw new ArgumentException("Window size must be at least 960 x 540.");
        options.Size=new Vector2D<int>(width,height);
        options.Title="Space Battle | SECS"; options.API=new GraphicsAPI(ContextAPI.OpenGL,ContextProfile.Core,ContextFlags.ForwardCompatible,new APIVersion(3,3));
        options.IsVisible=!hidden&&(capture is null || args.Contains("--visible")); options.VSync=profileFrames==0&&capture is null;
        if(profileFrames>0) {options.FramesPerSecond=0;options.UpdatesPerSecond=0;}
        window=Window.Create(options);window.Load+=Load;window.Update+=Update;window.Render+=Render;window.Closing+=DisposeGraphics;
    }
    public void Run()=>window.Run();
    private void Load()
    {
        gl=window.CreateOpenGL();renderer=new(gl);input=window.CreateInput();
        foreach(var keyboard in input.Keyboards) keyboard.KeyDown+=(_,key,_)=> {if(profileFrames==0||key==Key.Escape)HandleKey(key);};
        if(profileFrames==0)foreach(var mouse in input.Mice)
        {
            mouse.Scroll+=(_,wheel)=>ZoomAt(mouse.Position,wheel.Y);
            mouse.MouseMove+=(_,p)=>
            {
                if(mouse.IsButtonPressed(MouseButton.Right)||mouse.IsButtonPressed(MouseButton.Middle))
                {
                    PanDelta(p-previousMouse);
                }
                previousMouse=p;
            };
            mouse.MouseDown+=(_,button)=> { if(button==MouseButton.Left) Click(mouse.Position); };
        }
        battle.Capture(frame);
        if(validateControls)CheckControls();
        // Offline fast-forward is deliberately separate from the real-time accumulator.
        for(int i=0;i<captureTick;i++) Step();
        battle.Capture(frame);
        zoom=initialZoom;
        center=Vector2.Zero;
        replayStartTick=battle.Tick;
        if(profileFrames>0)profiler=new FrontendProfiler(gl,profileFrames,profileWarmup,profileOutput!,profileCsv);
        Console.WriteLine($"OpenGL {gl.GetStringS(StringName.Version)}; atlas={renderer.HasAtlas}; tick={battle.Tick}");
    }
    private void HandleKey(Key key)
    {
        switch(key)
        {
            case Key.Space: paused=!paused;break;
            case Key.R: Restart();break;
            case Key.Home: center=Vector2.Zero;zoom=1;break;
            case Key.Equal: speed=Math.Min(8,speed*2);break;
            case Key.Minus: speed=Math.Max(.25f,speed/2);break;
            case Key.Escape: window.Close();break;
        }
    }
    private void ZoomAt(Vector2 mouse,float scroll)
    {
        var before=ToWorld(mouse);zoom=Math.Clamp(zoom*MathF.Pow(1.15f,scroll),.25f,24);center+=before-ToWorld(mouse);
    }
    private void PanDelta(Vector2 delta)=>center+=new Vector2(-delta.X*window.FramebufferSize.X/window.Size.X,delta.Y*window.FramebufferSize.Y/window.Size.Y)/Scale;
    private void CheckControls()
    {
        static void Check(bool ok,string name) {if(!ok)throw new InvalidOperationException($"Control validation failed: {name}");}
        HandleKey(Key.Space);Check(paused,"pause");HandleKey(Key.Space);Check(!paused,"resume");
        HandleKey(Key.Equal);Check(speed==2,"speed up");HandleKey(Key.Minus);Check(speed==1,"speed down");
        var mouse=new Vector2(window.Size.X*.6f,window.Size.Y*.55f);
        var world=ToWorld(mouse);ZoomAt(mouse,3);Check(Vector2.Distance(world,ToWorld(mouse))<.001f,"cursor zoom anchor");
        world=ToWorld(mouse);var drag=new Vector2(20,10);PanDelta(drag);
        Check(Vector2.Distance(world,ToWorld(mouse+drag))<.001f,"drag pan anchor");
        HandleKey(Key.Home);Check(center==Vector2.Zero&&zoom==1,"fit");
        if(frame.Ships.Count>0)
        {
            var screen=Screen(frame.Ships[0].Position);
            Click(new(screen.X*window.Size.X/window.FramebufferSize.X,screen.Y*window.Size.Y/window.FramebufferSize.Y));Check(selected>=0,"selection");
        }
        Step();Check(battle.Tick==1,"step");HandleKey(Key.R);Check(battle.Tick==0&&selected==-1&&timings.Count==0,"restart");
        controlsVerified=true;Console.WriteLine("Control logic validation PASS (pause, speed, pan, cursor zoom, fit, selection, restart).");
    }
    private void Restart() { battle.Dispose();battle=new(new SecsStore(),config);accumulator=0;selected=-1;timings.Clear();p99=0;battle.Capture(frame); }
    private void Step() { battle.Step();timings.Enqueue(battle.LastTickMs);if(timings.Count>600)timings.Dequeue(); }
    private void Update(double dt)
    {
        if(profileFrames>0)return;
        if(capture is not null && !captured) return;
        if(input is not null) foreach(var k in input.Keyboards)
        {
            var d=Vector2.Zero;if(k.IsKeyPressed(Key.W)||k.IsKeyPressed(Key.Up))d.Y+=1;if(k.IsKeyPressed(Key.S)||k.IsKeyPressed(Key.Down))d.Y-=1;
            if(k.IsKeyPressed(Key.A)||k.IsKeyPressed(Key.Left))d.X-=1;if(k.IsKeyPressed(Key.D)||k.IsKeyPressed(Key.Right))d.X+=1;
            center+=d*(float)dt*200/zoom;
        }
        if(paused) return;
        accumulator+=Math.Min(dt,.25)*speed;
        int steps=0;while(accumulator>=1.0/60&&steps++<32) { Step();accumulator-=1.0/60; }
        battle.Capture(frame);
    }
    private float Scale=>Math.Max(1,Math.Min(window.FramebufferSize.X-40,window.FramebufferSize.Y-240))/(config.ArenaHalfSize*2)*zoom;
    private Vector2 Screen(Vector2 p)=>new(window.FramebufferSize.X*.5f+(p.X-center.X)*Scale,window.FramebufferSize.Y*.5f+30-(p.Y-center.Y)*Scale);
    private Vector2 ToWorld(Vector2 mouse)
    {
        var p=new Vector2(mouse.X*window.FramebufferSize.X/window.Size.X,mouse.Y*window.FramebufferSize.Y/window.Size.Y);
        return center+new Vector2(p.X-window.FramebufferSize.X*.5f,window.FramebufferSize.Y*.5f+30-p.Y)/Scale;
    }
    private void Click(Vector2 mouse)
    {
        float x=mouse.X*window.FramebufferSize.X/window.Size.X,y=mouse.Y*window.FramebufferSize.Y/window.Size.Y;
        if(y<44) { if(x<88)paused=!paused;else if(x<132)speed=Math.Max(.25f,speed/2);else if(x<180)speed=Math.Min(8,speed*2);else if(x<280)Restart();else if(x<360){center=Vector2.Zero;zoom=1;}return; }
        var world=ToWorld(mouse);var ship=frame.Ships.OrderBy(s=>Vector2.DistanceSquared(world,s.Position)).FirstOrDefault();
        selected=frame.Ships.Count>0&&Vector2.Distance(world,ship.Position)<Math.Max(ship.Radius,10/Scale)?ship.Serial:-1;
    }
    private void Render(double dt)
    {
        long profileStart=profiler is null?0:FrontendProfiler.Timestamp();
        if(renderer is null || window.FramebufferSize.X==0 || window.FramebufferSize.Y==0) return;
        if(profiler is not null) {RenderProfile(profileStart);return;}
        int w=window.FramebufferSize.X,h=window.FramebufferSize.Y;
        var (renderedShips,population)=RenderScene();
        if(capture is not null&&!captured)SaveCapture(w,h,renderedShips,population);
    }
    private void RenderProfile(long start)
    {
        var profile=profiler!;
        profile.FrameStarted(start);
        if(profile.Complete)
        {
            profile.Finish(battle,window.FramebufferSize.X,window.FramebufferSize.Y,zoom,hidden,renderer!.HasAtlas,controlsVerified,replayStartTick);
            window.Close();return;
        }
        profile.PollGpu();
        long simulationStart=FrontendProfiler.Timestamp();Step();long simulationEnd=FrontendProfiler.Timestamp();
        battle.Capture(frame);long snapshotEnd=FrontendProfiler.Timestamp();
        profile.BeginGpu();
        long renderStart=FrontendProfiler.Timestamp();RenderScene();long renderEnd=FrontendProfiler.Timestamp();
        profile.EndGpu();profile.CollectErrors(profile.SampleIndex);
        long totalEnd=FrontendProfiler.Timestamp();
        profile.Record(FrontendProfiler.Milliseconds(simulationStart,simulationEnd),FrontendProfiler.Milliseconds(simulationEnd,snapshotEnd),
            FrontendProfiler.Milliseconds(renderStart,renderEnd),FrontendProfiler.Milliseconds(start,totalEnd),frame);
    }
    private (int RenderedShips,TeamPopulation Population) RenderScene()
    {
        int w=window.FramebufferSize.X,h=window.FramebufferSize.Y;var renderer=this.renderer!;renderer.Begin(w,h);
        var rng=new Random(137);
        for(int i=0;i<800;i++) { var p=Screen(new Vector2((float)rng.NextDouble()*1600-800,(float)rng.NextDouble()*1600-800));renderer.Rect(p.X,p.Y,i%9==0?2:1,1,new(.22f,.29f,.39f,.8f)); }
        float a=config.ArenaHalfSize;
        Vector2[] corners=[Screen(new(-a,-a)),Screen(new(a,-a)),Screen(new(a,a)),Screen(new(-a,a))];for(int i=0;i<4;i++)renderer.Line(corners[i],corners[(i+1)%4],1,new(.14f,.23f,.29f,.7f));
        foreach(var e in frame.Particles)renderer.Circle(Screen(e.Position),Math.Max(1,e.Radius*Scale),e.Color,true);
        foreach(var e in frame.Debris) { var p=Screen(e.Position);renderer.Line(p,p+new Vector2(3,2),2,e.Color); }
        foreach(var e in frame.Projectiles)
        {
            var p=Screen(e.Position); var tail=Screen(e.Position-e.Velocity*.045f);
            var color=(WeaponKind)e.Kind switch
            {
                WeaponKind.Plasma=>new Vector4(.5f,1,.65f,.9f),
                WeaponKind.Cannon=>new Vector4(1,.8f,.3f,1),
                WeaponKind.Missile=>new Vector4(1,.65f,.3f,1),
                _=>new Vector4(.95f,.93f,.8f,1)
            };
            renderer.Line(tail,p,e.Kind==(int)WeaponKind.Cannon?2:1,color);
            if(e.Kind==(int)WeaponKind.Plasma)renderer.Circle(p,Math.Max(2,e.Radius*Scale),color,true);
        }
        int renderedShips=0;
        foreach(var e in frame.Ships)
        {
            var p=Screen(e.Position);float r=Math.Max(2,e.Radius*Scale);
            if(p.X < -r*3 || p.Y < -r*3 || p.X>w+r*3||p.Y>h+r*3)continue;
            renderedShips++;
            renderer.Line(Screen(e.Position-e.Velocity*.12f),p,Math.Max(1,r*.3f),new(.22f,.59f,1,.25f));
            renderer.Ship(p,r,e.Angle,e.Kind,e.Team==0?Cyan:Red);
            if(e.Shield>.01f&&r>=3)renderer.Circle(p,r*1.35f,new(.4f,.7f,1,Math.Clamp(e.Shield,0,1)*.4f));
            if(e.Heat>.65f)renderer.Circle(p,r*1.6f,new(1,.55f,.12f,Math.Clamp(e.Heat,0,1)*.7f));
            if(e.Hull<.98f&&r>=3) {renderer.Rect(p.X-r,p.Y+r+3,r*2,2,new(.18f,.2f,.23f,1));renderer.Rect(p.X-r,p.Y+r+3,r*2*Math.Clamp(e.Hull,0,1),2,new(.45f,.95f,.5f,1));}
            if(e.Serial==selected)renderer.Circle(p,r*2+4,White);
        }
        var population=new TeamPopulation();battle.Store.ForEach<Identity,TeamPopulation>(ref population);
        Hud(w,h,population);renderer.End();
        return (renderedShips,population);
    }
    private void SaveCapture(int w,int h,int renderedShips,TeamPopulation population)
    {
            var renderer=this.renderer!;
            var pixels=renderer.ReadPixels();int bright=0,sceneBright=0;var colors=new HashSet<int>();var sceneColors=new HashSet<int>();
            for(int i=0;i<pixels.Length;i+=4)
            {
                int color=pixels[i]<<16|pixels[i+1]<<8|pixels[i+2];bool isBright=pixels[i]+pixels[i+1]+pixels[i+2]>100;
                if(isBright)bright++;colors.Add(color);
                int y=h-1-i/4/w;
                if(y>=140&&y<h-80) {sceneColors.Add(color);if(isBright)sceneBright++;}
            }
            bool ok=bright>500&&colors.Count>16&&sceneBright>100&&sceneColors.Count>16;
            var checkpoint=battle.Checksum().ToString("X16",System.Globalization.CultureInfo.InvariantCulture);
            var coreHash=FrontendProfiler.AssemblyHash(typeof(Battle<>).Assembly);
            var gameHash=FrontendProfiler.AssemblyHash(typeof(BattleApp).Assembly);
            var secsHash=FrontendProfiler.AssemblyHash(typeof(SecsStore).Assembly);
            PngCapture.Save(capture!,w,h,pixels);
            var stats=new {frame.Tick,Width=w,Height=h,Zoom=zoom,ConfiguredShipsPerTeam=config.ShipsPerTeam,Ships=frame.Ships.Count,RenderedShips=renderedShips,AliveCyan=population.Cyan,AliveRed=population.Red,Projectiles=frame.Projectiles.Count,Debris=frame.Debris.Count,Particles=frame.Particles.Count,BrightPixels=bright,UniqueColors=colors.Count,SceneBrightPixels=sceneBright,SceneUniqueColors=sceneColors.Count,Nonblank=ok,Atlas=renderer.HasAtlas,ControlsVerified=controlsVerified,BattleChecksum=checkpoint,CoreDllSha256=coreHash,GameDllSha256=gameHash,SecsAdapterDllSha256=secsHash,LastTickMs=frame.LastTickMs,P99=p99};
            File.WriteAllText(capture+".json",System.Text.Json.JsonSerializer.Serialize(stats,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(stats));captured=true;
            if(validate&&!ok)throw new InvalidOperationException("Framebuffer validation failed.");
            if(exitCapture)window.Close();
    }
    private void Hud(int w,int h,TeamPopulation population)
    {
        var r=renderer!;
        r.Rect(0,0,w,44,new(.025f,.04f,.06f,1));
        r.Rect(0,44,w,82,new(.012f,.02f,.035f,.97f));
        r.Rect(0,h-70,w,70,new(.012f,.02f,.035f,.97f));
        if(paused)r.Triangle(new(22,13),new(22,31),new(38,22),White);
        else {r.Rect(22,14,5,16,White);r.Rect(32,14,5,16,White);}
        r.Text(100,14,"-",White);r.Text(148,14,"+",White);r.Text(190,14,"RESTART",White);r.Text(292,14,"FIT",White);
        r.Text(Math.Max(370,w-300),14,$"{speed:0.##}X  TICK {frame.Tick}",White);
        if(timings.Count>0) {var sorted=timings.Order().ToArray();p99=sorted[(int)Math.Floor((sorted.Length-1)*.99)];}
        for(int team=0;team<2;team++)
        {
            var ships=frame.Ships.Where(s=>s.Team==team).ToArray();float heat=ships.Length==0?0:ships.Average(s=>s.Heat),shield=ships.Length==0?0:ships.Average(s=>s.Shield);
            r.Text(14,58+team*22,$"{(team==0?"CYAN":"RED ")} ALIVE {(team==0?population.Cyan:population.Red)}  HEAT {heat*100:0}%  SHIELD {shield*100:0}%",team==0?Cyan:Red);
        }
        r.Text(14,h-58,$"SECS  TICK {frame.LastTickMs:0.00} MS  P99 {p99:0.00} MS  ZOOM {zoom:0.00}",White);
        r.Text(14,h-36,$"SHOTS {frame.Counters.Shots}  LIVE {frame.Projectiles.Count}  HITS {frame.Counters.Hits}  LOST {frame.Counters.ShipsDestroyed}  BUILT {frame.Counters.Builds}",White);
        if(!r.HasAtlas)r.Text(14,h-16,"TEMPORARY PROCEDURAL SHIPS - ATLAS PENDING",new(.9f,.73f,.38f,1),1);
        var selectedShip=frame.Ships.FirstOrDefault(s=>s.Serial==selected);
        if(selected!=-1&&selectedShip.Serial==selected)r.Text(14,110,$"SHIP {selected} {((ShipClass)selectedShip.Kind)} HULL {selectedShip.Hull*100:0}% SHIELD {selectedShip.Shield*100:0}%",White,1);
        window.Title=$"Space Battle | {battle.Framework} | tick {frame.Tick} | ships {frame.Ships.Count} | p99 {p99:0.00} ms";
    }
    private void DisposeGraphics() { profiler?.Dispose();profiler=null;renderer?.Dispose();renderer=null;input?.Dispose();input=null;gl?.Dispose();gl=null; }
    public void Dispose() { DisposeGraphics();battle.Dispose();window.Dispose(); }
}
