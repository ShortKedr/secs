using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SpaceBattle;
using SpaceBattle.Game;

System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
try { if(args.Contains("--validate-observer")) {SpectatorCameraValidation.Run();return 0;} using var app = new BattleApp(args); app.Run(); return 0; }
catch(Exception e) { Console.Error.WriteLine(e); return 1; }

internal sealed class BattleApp : IDisposable
{
    private readonly IWindow window;
    private readonly BattleConfig config;
    private Battle<SecsStore> battle;
    private readonly BattleFrame frame = new();
    private readonly List<DebugCollider> colliders = new();
    private bool collidersVisible;
    private Renderer? renderer;
    private IInputContext? input;
    private GL? gl;
    private FrontendProfiler? profiler;
    private readonly int profileFrames, profileWarmup;
    private readonly string? profileOutput, profileCsv;
    private readonly bool hidden, profilePaced;
    private int replayStartTick;
    private readonly string? capture;
    private readonly string? effectsCapture;
    private readonly string? gunTintsCapture;
    private readonly string? sceneStyleCapture;
    private readonly int captureTick;
    private readonly float initialZoom;
    private readonly bool exitCapture, validate, validateControls, validateLiveStatistics, validateEffects, validateGunTints;
    private readonly bool validateSceneStyle;
    private bool controlsVerified;
    private bool captured, paused;
    private int selected = -1;
    private readonly SpectatorCamera spectator = new();
    private readonly int followShip;
    private float zoom = 1, speed = 1;
    private Vector2 center, previousMouse;
    private double accumulator;
    private readonly SimulationStatistics statistics = new(Battle<SecsStore>.StageNames.Length);
    private bool statisticsVisible;
    private long rateStart;
    private long statisticsRefreshAt;
    private int wallTicks, wallFrames;
    private double ticksPerSecond, renderFps, renderCpuMs;
    private StoreDiagnostics storage;
    private long managedBytes;
    private long liveValidationStart;
    private int liveValidationPhase, liveValidationTick;
    private static long Timestamp()=>System.Diagnostics.Stopwatch.GetTimestamp();
    private static double Elapsed(long start)=> (Timestamp()-start)/(double)System.Diagnostics.Stopwatch.Frequency;
    private int AliveEntities=>battle.ShipCount+battle.ProjectileCount+battle.DebrisCount+battle.ParticleCount+battle.TurretCount;
    private double TickBudgetMs=>1000.0/(config.TickRate*speed);
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
        followShip=ProfileNumber("--follow-ship",-1);
        if(followShip < -1)throw new ArgumentException("--follow-ship must be a nonnegative serial.");
        validateLiveStatistics=args.Contains("--validate-live-statistics");
        validateEffects=args.Contains("--validate-effects");
        if(Value("--effects-capture") is { } effectsPath)effectsCapture=ProjectOutput(effectsPath);
        if(effectsCapture is not null&&!validateEffects)throw new ArgumentException("--effects-capture requires --validate-effects.");
        validateGunTints=args.Contains("--validate-gun-tints");
        if(Value("--gun-tints-capture") is { } gunPath)gunTintsCapture=ProjectOutput(gunPath);
        if(gunTintsCapture is not null&&!validateGunTints)throw new ArgumentException("--gun-tints-capture requires --validate-gun-tints.");
        if(validateEffects&&validateGunTints)throw new ArgumentException("Run effect and gun tint galleries separately.");
        validateSceneStyle=args.Contains("--validate-scene-style");
        if(validateSceneStyle&&(validateEffects||validateGunTints))throw new ArgumentException("Run scene, effect and gun tint galleries separately.");
        if(Value("--scene-style-capture") is { } scenePath)sceneStyleCapture=ProjectOutput(scenePath);
        if(sceneStyleCapture is not null&&!validateSceneStyle)throw new ArgumentException("--scene-style-capture requires --validate-scene-style.");
        statisticsVisible=args.Contains("--statistics");
        collidersVisible=args.Contains("--colliders");
        hidden=args.Contains("--hidden");
        profileFrames=ProfileNumber("--profile-frames",0);
        profileWarmup=ProfileNumber("--profile-warmup",120);
        profilePaced=args.Contains("--profile-paced");
        if(profilePaced&&profileFrames==0)throw new ArgumentException("--profile-paced requires --profile-frames N.");
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
        if(validateLiveStatistics&&(profileFrames>0||capture is not null))
            throw new ArgumentException("Live statistics validation must run without capture or replay profiling.");
        if((validateEffects||validateGunTints||validateSceneStyle)&&(profileFrames>0||validateLiveStatistics))
            throw new ArgumentException("Visual validation must run separately from profiling or live statistics validation.");
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
        config=new BattleConfig { ShipsPerTeam=Number("--ships",Number("--ships-per-team",96)), Seed=(uint)Number("--seed",7331),
            EndlessReinforcements=!args.Contains("--finite"), ReplenishCarriers=false,
            FleetComposition=FleetComposition.LightSwarm,
            Deployment=args.Contains("--legacy-deployment")?FleetDeployment.LegacyRandom:FleetDeployment.Tactical };config.Validate();
        battle=new(new SecsStore(),config);
        var options=WindowOptions.Default;
        int width=Number("--width",1440), height=Number("--height",900);
        if(width<960||height<540)throw new ArgumentException("Window size must be at least 960 x 540.");
        options.Size=new Vector2D<int>(width,height);
        options.Title="Space Battle | SECS"; options.API=new GraphicsAPI(ContextAPI.OpenGL,ContextProfile.Core,ContextFlags.ForwardCompatible,new APIVersion(3,3));
        options.IsVisible=!hidden&&(!(validateEffects||validateGunTints||validateSceneStyle)||args.Contains("--visible"))&&(capture is null || args.Contains("--visible")); options.VSync=profileFrames==0&&capture is null;
        if(profileFrames>0) {options.FramesPerSecond=profilePaced?config.TickRate:0;options.UpdatesPerSecond=0;}
        window=Window.Create(options);window.Load+=Load;window.Update+=Update;window.Render+=Render;window.Closing+=DisposeGraphics;
    }
    public void Run()=>window.Run();
    private void Load()
    {
        gl=window.CreateOpenGL();renderer=new(gl);input=window.CreateInput();
        if(validateSceneStyle)
        {
            SceneStyleValidation.Run(renderer,window.FramebufferSize.X,window.FramebufferSize.Y,sceneStyleCapture);
            if(capture is null) {window.Close();return;}
        }
        if(validateGunTints)
        {
            GunTintRenderValidation.Run(renderer,window.FramebufferSize.X,window.FramebufferSize.Y,gunTintsCapture);
            if(capture is null) {window.Close();return;}
        }
        if(validateEffects)
        {
            EffectRenderValidation.Run(renderer,window.FramebufferSize.X,window.FramebufferSize.Y,effectsCapture);
            if(capture is null) {window.Close();return;}
        }
        if(validate&&(!renderer.HasAtlas||!renderer.HasGunAtlas||!renderer.HasEffectsAtlas||config.EndlessReinforcements&&!renderer.HasWarpAtlas))
            throw new InvalidOperationException("Render validation requires ship, gun, effects and enabled warp atlases.");
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
        if(followShip>=0)StartFollow(followShip);
        // Offline fast-forward is deliberately separate from the real-time accumulator.
        for(int i=0;i<captureTick;i++) Step();
        battle.Capture(frame);
        zoom=initialZoom;
        center=Vector2.Zero;
        UpdateCamera(0,true);
        replayStartTick=battle.Tick;
        ResetRates();
        liveValidationStart=Timestamp();
        if(profileFrames>0)profiler=new FrontendProfiler(gl,profileFrames,profileWarmup,profileOutput!,profileCsv,profilePaced);
        Console.WriteLine($"OpenGL {gl.GetStringS(StringName.Version)}; atlas={renderer.HasAtlas}; gunAtlas={renderer.HasGunAtlas}; effectsAtlas={renderer.HasEffectsAtlas}; warpAtlas={renderer.HasWarpAtlas}; tick={battle.Tick}");
    }
    private void HandleKey(Key key)
    {
        switch(key)
        {
            case Key.Space: TogglePause();break;
            case Key.F2: ToggleColliders();break;
            case Key.F3: statisticsVisible=!statisticsVisible;break;
            case Key.R: Restart();break;
            case Key.Home: StopFollow();center=Vector2.Zero;zoom=1;break;
            case Key.C: ToggleFollow();break;
            case Key.Equal: speed=Math.Min(8,speed*2);break;
            case Key.Minus: speed=Math.Max(.25f,speed/2);break;
            case Key.Escape: window.Close();break;
        }
    }
    private void ZoomAt(Vector2 mouse,float scroll)
    {
        StopFollow();
        var before=ToWorld(mouse);zoom=Math.Clamp(zoom*MathF.Pow(1.15f,scroll),.25f,24);center+=before-ToWorld(mouse);
    }
    private void PanDelta(Vector2 delta) {StopFollow();center+=new Vector2(-delta.X*window.FramebufferSize.X/window.Size.X,delta.Y*window.FramebufferSize.Y/window.Size.Y)/Scale;}
    private void StopFollow() {spectator.Stop();battle.ObserveShip(-1);}
    private void StartFollow(int serial)
    {
        if(!spectator.Start(serial,battle.TryGetShipObservation))return;
        selected=serial;battle.ObserveShip(serial);UpdateCamera(0,true);
    }
    private void ToggleFollow() {if(spectator.Following)StopFollow();else StartFollow(selected);}
    private void ResolveObserver()
    {
        if(!spectator.Following)return;
        if(!battle.TryGetShipObservation(spectator.ObservedSerial,out _))battle.Capture(frame,refreshEntities:false);
        if(spectator.Resolve(battle.TryGetShipObservation,battle.ObservedDeath,frame.Ships))
        {battle.ObserveShip(spectator.ObservedSerial);selected=spectator.ObservedSerial;}
    }
    private void UpdateCamera(double dt,bool snap=false)
    {
        ResolveObserver();
        spectator.Advance((float)dt,new(window.FramebufferSize.X,window.FramebufferSize.Y,statisticsVisible),BaseScale,snap);
        if(spectator.Following) {center=spectator.Center;zoom=spectator.Scale/BaseScale;}
    }
    private void CheckControls()
    {
        static void Check(bool ok,string name) {if(!ok)throw new InvalidOperationException($"Control validation failed: {name}");}
        bool initialStatisticsVisible=statisticsVisible;statisticsVisible=false;
        bool initialCollidersVisible=collidersVisible;
        var colliderChecksum=battle.Checksum();
        HandleKey(Key.F2);Check(collidersVisible!=initialCollidersVisible,"collider key toggle");
        Click(new(500f*window.Size.X/window.FramebufferSize.X,22f*window.Size.Y/window.FramebufferSize.Y));
        Check(collidersVisible==initialCollidersVisible,"collider button toggle");
        collidersVisible=true;CaptureColliders();Check(colliders.Count>0,"enabled collider capture");
        collidersVisible=false;CaptureColliders();Check(colliders.Count==0,"disabled collider capture cleared");
        Check(battle.Checksum()==colliderChecksum,"collider toggles/capture preserve gameplay checksum");
        collidersVisible=initialCollidersVisible;
        ColliderOverlay.ValidateGeometry();
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
            Check(!spectator.Following,"ordinary selection does not follow");
            HandleKey(Key.C);Check(spectator.Following&&spectator.ObservedSerial==selected,"follow key");
            HandleKey(Key.Space);int followTick=battle.Tick;UpdateCamera(.1);Check(spectator.Following&&battle.Tick==followTick,"paused view follows without ticks");HandleKey(Key.Space);
            screen=Screen(spectator.Main.Position);
            Click(new(screen.X*window.Size.X/window.FramebufferSize.X,screen.Y*window.Size.Y/window.FramebufferSize.Y));
            Check(!spectator.Following&&spectator.TargetSerial==-1&&selected>=0,"normal canvas selection cancels follow");
            HandleKey(Key.C);
            PanDelta(Vector2.One);Check(!spectator.Following&&spectator.TargetSerial==-1,"pan cancels follow");
            HandleKey(Key.C);ZoomAt(mouse,1);Check(!spectator.Following,"zoom cancels follow");
            HandleKey(Key.C);HandleKey(Key.Home);Check(!spectator.Following,"home cancels follow");
            StartFollow(selected);Click(new(448f*window.Size.X/window.FramebufferSize.X,22f*window.Size.Y/window.FramebufferSize.Y));Check(!spectator.Following,"follow button");
        }
        statisticsVisible=initialStatisticsVisible;
        HandleKey(Key.F3);Check(statisticsVisible!=initialStatisticsVisible,"statistics key toggle");
        Click(new(390f*window.Size.X/window.FramebufferSize.X,22f*window.Size.Y/window.FramebufferSize.Y));
        Check(statisticsVisible==initialStatisticsVisible,"statistics click toggle");
        Step();
        battle.Capture(frame);
        if(profileFrames>0)statistics.RecordTick(battle.LastTickMs,battle.LastStageMs);
        statistics.Refresh();Check(battle.Tick==1&&statistics.SampleCount==1,"statistics step");
        Check(frame.Tick==battle.Tick,"snapshot matches stepped tick");
        Check(AliveEntities==battle.Store.Diagnostics().Entities,"alive ECS count including docked ships");
        Check(frame.Ships.Count<=battle.ShipCount,"snapshot ships bounded by alive ships");
        Check(frame.Turrets.Count<=battle.TurretCount,"snapshot turrets bounded by alive turrets");
        HandleKey(Key.Space);int tick=battle.Tick;Update(.1);
        Check(battle.Tick==tick&&ticksPerSecond==0&&wallTicks==0,"paused tick rate");HandleKey(Key.Space);
        HandleKey(Key.R);statistics.Refresh();
        Check(frame.Tick==battle.Tick&&frame.Ships.Count==battle.ShipCount&&frame.Turrets.Count==battle.TurretCount,"restart snapshot reset");
        Check(battle.Tick==0&&selected==-1&&!spectator.Following&&statistics.SampleCount==0&&statistics.LastMs==0&&statistics.MeanMs==0&&statistics.P99Ms==0,"restart statistics reset");
        Check(statistics.P95Ms==0&&statistics.MaximumMs==0&&statistics.StageMeanMs.All(ms=>ms==0),"restart stage reset");
        var bounded=new SimulationStatistics(1);double[] stage=[1];
        for(int i=0;i<=SimulationStatistics.WindowCapacity;i++)bounded.RecordTick(1,stage);
        bounded.Refresh();Check(bounded.SampleCount==SimulationStatistics.WindowCapacity&&bounded.MeanMs==1&&bounded.P99Ms==1&&bounded.StageMeanMs[0]==1,"bounded rolling statistics");
        controlsVerified=true;Console.WriteLine("Control logic validation PASS (pause, speed, pan, cursor zoom, fit, selection, follow key/button, manual cancellation, paused camera, restart, statistics toggles/counts/reset/window, collider toggles/geometry/checksum).");
    }
    private void ResetRates() {rateStart=Timestamp();wallTicks=wallFrames=0;ticksPerSecond=renderFps=renderCpuMs=0;}
    private void TogglePause() {paused=!paused;ResetRates();}
    private void ToggleColliders() {collidersVisible=!collidersVisible;if(!collidersVisible)colliders.Clear();}
    private void CaptureColliders()
    {
        if(collidersVisible)battle.CaptureColliders(colliders,refreshEntities:false);
        else colliders.Clear();
    }
    private void Restart() { StopFollow();battle.Dispose();battle=new(new SecsStore(),config);accumulator=0;selected=-1;statistics.Reset();statisticsRefreshAt=0;ResetRates();battle.Capture(frame); }
    private void Step(bool live=false)
    {
        battle.Step();
        ResolveObserver();
        if(profileFrames==0)statistics.RecordTick(battle.LastTickMs,battle.LastStageMs);
        if(live)wallTicks++;
    }
    private void Update(double dt)
    {
        if(profileFrames>0)return;
        if(capture is not null && !captured) return;
        if(input is not null) foreach(var k in input.Keyboards)
        {
            var d=Vector2.Zero;if(k.IsKeyPressed(Key.W)||k.IsKeyPressed(Key.Up))d.Y+=1;if(k.IsKeyPressed(Key.S)||k.IsKeyPressed(Key.Down))d.Y-=1;
            if(k.IsKeyPressed(Key.A)||k.IsKeyPressed(Key.Left))d.X-=1;if(k.IsKeyPressed(Key.D)||k.IsKeyPressed(Key.Right))d.X+=1;
            if(d!=Vector2.Zero) {StopFollow();center+=d*(float)dt*200/zoom;}
        }
        if(paused) return;
        accumulator+=Math.Min(dt,.25)*speed;
        int steps=0;while(accumulator>=1.0/config.TickRate&&steps++<32) { Step(true);accumulator-=1.0/config.TickRate; }
        battle.Capture(frame, refreshEntities: false);
    }
    private float BaseScale=>Math.Max(1,Math.Min(window.FramebufferSize.X-40,window.FramebufferSize.Y-240))/(config.ArenaHalfSize*2);
    private float Scale=>spectator.Following?spectator.Scale:BaseScale*zoom;
    private Vector2 Screen(Vector2 p)=>new(window.FramebufferSize.X*.5f+(p.X-center.X)*Scale,window.FramebufferSize.Y*.5f+30-(p.Y-center.Y)*Scale);
    private Vector2 ToWorld(Vector2 mouse)
    {
        var p=new Vector2(mouse.X*window.FramebufferSize.X/window.Size.X,mouse.Y*window.FramebufferSize.Y/window.Size.Y);
        return center+new Vector2(p.X-window.FramebufferSize.X*.5f,window.FramebufferSize.Y*.5f+30-p.Y)/Scale;
    }
    private void Click(Vector2 mouse)
    {
        float x=mouse.X*window.FramebufferSize.X/window.Size.X,y=mouse.Y*window.FramebufferSize.Y/window.Size.Y;
        if(y<44) { if(x<88)TogglePause();else if(x<132)speed=Math.Max(.25f,speed/2);else if(x<180)speed=Math.Min(8,speed*2);else if(x<280)Restart();else if(x<360){HandleKey(Key.Home);}else if(x<420)statisticsVisible=!statisticsVisible;else if(x<476)ToggleFollow();else if(x<524)ToggleColliders();return; }
        if(statisticsVisible&&x>=window.FramebufferSize.X-374&&y>=138&&y<446)return;
        StopFollow();
        var world=ToWorld(mouse);var ship=frame.Ships.OrderBy(s=>Vector2.DistanceSquared(world,s.Position)).FirstOrDefault();
        selected=frame.Ships.Count>0&&Vector2.Distance(world,ship.Position)<Math.Max(ship.Radius,10/Scale)?ship.Serial:-1;
    }
    private void Render(double dt)
    {
        long profileStart=profiler is null?0:FrontendProfiler.Timestamp();
        if(renderer is null || window.FramebufferSize.X==0 || window.FramebufferSize.Y==0) return;
        if(profiler is not null) {RenderProfile(profileStart);return;}
        if(Elapsed(statisticsRefreshAt)>=.25) {statistics.Refresh();storage=battle.Store.Diagnostics();managedBytes=GC.GetTotalMemory(false);statisticsRefreshAt=Timestamp();}
        wallFrames++;
        double elapsed=Elapsed(rateStart);
        if(elapsed>=.5)
        {
            ticksPerSecond=paused?0:wallTicks/elapsed;renderFps=wallFrames/elapsed;
            rateStart=Timestamp();wallTicks=wallFrames=0;
        }
        int w=window.FramebufferSize.X,h=window.FramebufferSize.Y;
        long renderStart=Timestamp();
        UpdateCamera(dt);
        var (renderedShips,population)=RenderScene();
        renderCpuMs=Elapsed(renderStart)*1000;
        if(capture is not null&&!captured)SaveCapture(w,h,renderedShips,population);
        if(validateLiveStatistics)CheckLiveStatistics();
    }
    private void CheckLiveStatistics()
    {
        static void Check(bool condition,string name) {if(!condition)throw new InvalidOperationException($"Live statistics validation failed: {name}");}
        if(liveValidationPhase==0&&Elapsed(liveValidationStart)>=2)
        {
            Check(battle.Tick>0&&statistics.SampleCount>0&&statistics.MeanMs>0,"executed live ticks");
            Check(ticksPerSecond>0&&renderFps>0&&renderCpuMs>0,"separate wall tick/render rates");
            Check(AliveEntities==battle.Store.Diagnostics().Entities,"live ECS counts");
            liveValidationTick=battle.Tick;TogglePause();liveValidationStart=Timestamp();liveValidationPhase=1;
        }
        else if(liveValidationPhase==1&&Elapsed(liveValidationStart)>=.75)
        {
            Check(battle.Tick==liveValidationTick&&ticksPerSecond==0&&renderFps>0,"pause stops simulation but not render cadence");
            Check(statistics.SampleCount>0&&statistics.MeanMs>0,"pause retains timing history");
            double budget=TickBudgetMs;HandleKey(Key.Equal);Check(Math.Abs(TickBudgetMs-budget/2)<1e-9,"speed budget");
            Restart();Check(statistics.SampleCount==0&&ticksPerSecond==0,"live reset");
            Console.WriteLine("PASS: live statistics tick/render rates, counts, pause/history, speed budget and reset.");
            window.Close();
        }
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
        battle.Capture(frame, refreshEntities: false);long snapshotEnd=FrontendProfiler.Timestamp();
        UpdateCamera(1.0/config.TickRate);
        profile.BeginGpu();
        long renderStart=FrontendProfiler.Timestamp();RenderScene();long renderEnd=FrontendProfiler.Timestamp();
        profile.EndGpu();profile.CollectErrors(profile.SampleIndex);
        long totalEnd=FrontendProfiler.Timestamp();
        profile.Record(FrontendProfiler.Milliseconds(simulationStart,simulationEnd),FrontendProfiler.Milliseconds(simulationEnd,snapshotEnd),
            FrontendProfiler.Milliseconds(renderStart,renderEnd),FrontendProfiler.Milliseconds(start,totalEnd),frame,battle.LastTickMs,battle.LastStageMs);
    }
    private (int RenderedShips,TeamPopulation Population) RenderScene()
    {
        int w=window.FramebufferSize.X,h=window.FramebufferSize.Y;var renderer=this.renderer!;renderer.Begin(w,h);
        var camera=new SceneCamera(w,h,Scale,center,h*.5f+30);
        int renderedShips=BattleSceneRenderer.Draw(renderer,frame,camera,selected,
            spectator.Following?spectator.MainSerial:-1,spectator.Following?spectator.TargetSerial:-1);
        CaptureColliders();
        if(collidersVisible)ColliderOverlay.Draw(renderer,colliders,Screen,Scale);
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
            var parentSerials=frame.Ships.Select(ship=>ship.Serial).ToHashSet();
            bool countsValid=AliveEntities==battle.Store.Diagnostics().Entities&&population.Cyan+population.Red==battle.ShipCount&&renderedShips<=frame.Ships.Count&&frame.Ships.Count<=battle.ShipCount&&frame.Turrets.Count<=battle.TurretCount&&frame.Turrets.All(t=>parentSerials.Contains(t.ParentSerial));
            bool statisticsValid=statistics.SampleCount==Math.Min(battle.Tick,SimulationStatistics.WindowCapacity)&&
                statistics.StageMeanMs.Length==Battle<SecsStore>.StageNames.Length&&ticksPerSecond==0&&
                double.IsFinite(statistics.MeanMs)&&statistics.MeanMs>=0&&statistics.P95Ms<=statistics.P99Ms&&statistics.P99Ms<=statistics.MaximumMs;
            var stats=new {frame.Tick,Width=w,Height=h,Zoom=zoom,ConfiguredShipsPerTeam=config.ShipsPerTeam,Ships=frame.Ships.Count,RenderedShips=renderedShips,AliveCyan=population.Cyan,AliveRed=population.Red,Projectiles=frame.Projectiles.Count,Debris=frame.Debris.Count,Particles=frame.Particles.Count,BrightPixels=bright,UniqueColors=colors.Count,SceneBrightPixels=sceneBright,SceneUniqueColors=sceneColors.Count,Nonblank=ok,Atlas=renderer.HasAtlas,ControlsVerified=controlsVerified,BattleChecksum=checkpoint,CoreDllSha256=coreHash,GameDllSha256=gameHash,SecsAdapterDllSha256=secsHash,LastTickMs=frame.LastTickMs,P99=statistics.P99Ms,
                GunAtlas=renderer.HasGunAtlas,EffectsAtlas=renderer.HasEffectsAtlas,AliveEntities,AliveShips=battle.ShipCount,DockedShips=battle.ShipCount-frame.Ships.Count,Turrets=frame.Turrets.Count,AliveTurrets=battle.TurretCount,DockedTurrets=battle.TurretCount-frame.Turrets.Count,
                TurretPoses=frame.Turrets.Select(t=>new {t.Serial,t.ParentSerial,t.Team,t.Kind,X=t.Position.X,Y=t.Position.Y,t.Angle,t.Radius,t.Heat,t.Recoil}),
                HeavyShots=frame.Counters.HeavyShots,TurretShots=frame.Counters.TurretShots,GameplayRevision=ArmamentRules.Revision,CountsValid=countsValid,StatisticsValid=statisticsValid,
                WarpAtlas=renderer.HasWarpAtlas,config.EndlessReinforcements,ReinforcementRevision=WarpReinforcementRules.Revision,
                FleetComposition=config.FleetComposition.ToString(),FleetRevision=FleetCompositionRules.Revision,
                DeploymentRevision=config.Deployment==FleetDeployment.Tactical?FleetDeploymentRules.Revision:"legacy-random-v1",
                PhysicsRevision=PhysicsSettings.Revision,PhysicsSettings=config.Physics,DamageRevision=DamageRules.Revision,DamageProfiles=config.Damage,TacticalRevision=TacticalRules.Revision,TacticsSettings=config.Tactics,SceneStyleRevision=BattleSceneRenderer.Revision,
                Colliders=new {Enabled=collidersVisible,Count=colliders.Count,Ships=colliders.Count(c=>c.Kind==DebugColliderKind.Ship),Projectiles=colliders.Count(c=>c.Kind==DebugColliderKind.Projectile),Debris=colliders.Count(c=>c.Kind==DebugColliderKind.Debris),Disks=colliders.Count(c=>c.Shape==ProjectileShape.Disk),Rectangles=colliders.Count(c=>c.Shape==ProjectileShape.Rectangle)},
                Observer=new {spectator.Following,spectator.ObservedSerial,spectator.MainSerial,spectator.TargetSerial,SelectedSerial=selected,Death=new {battle.ObservedDeath.Serial,battle.ObservedDeath.KillerSerial,battle.ObservedDeath.Tick,X=battle.ObservedDeath.Position.X,Y=battle.ObservedDeath.Position.Y}},
                Camera=new {CenterX=center.X,CenterY=center.Y,Scale,Zoom=zoom,SafeViewport=new {Left=14,Top=140,Right=w-(statisticsVisible?392:14),Bottom=h-80},Main=spectator.Following?CameraShip(spectator.Main):null,Target=spectator.Target is { } cameraTarget?CameraShip(cameraTarget):null},
                battle.Counters.ReinforcementWaves,battle.Counters.ReinforcementShips,
                Reinforcements=Enumerable.Range(0,2).Select(team=> {var wave=battle.Reinforcement(team);return new {Team=team,wave.Wave,wave.Corner,wave.Composition,wave.PendingShips,wave.ArrivalTick,wave.NextSpawnTick,wave.EndTick,X=wave.Position.X,Y=wave.Position.Y,wave.Active};}).ToArray(),
                Statistics=new {statistics.SampleCount,Capacity=SimulationStatistics.WindowCapacity,statistics.LastMs,statistics.MeanMs,statistics.P95Ms,statistics.P99Ms,statistics.MaximumMs,StageNames=Battle<SecsStore>.StageNames,statistics.StageMeanMs,WallTicksPerSecond=ticksPerSecond,RenderFps=renderFps,RenderSubmissionCpuMs=renderCpuMs,TickBudgetMs,MeanBudgetPercent=statistics.MeanMs/TickBudgetMs*100,Paused=paused,PanelVisible=statisticsVisible,OfflineCapture=true}};
            File.WriteAllText(capture+".json",System.Text.Json.JsonSerializer.Serialize(stats,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(stats));captured=true;ResetRates();
            if(validate&&!ok)throw new InvalidOperationException("Framebuffer validation failed.");
            if(validate&&(!countsValid||!statisticsValid))throw new InvalidOperationException("Capture statistics validation failed.");
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
        var chartColor=statisticsVisible?Cyan:White;
        r.Rect(378,24,5,8,chartColor);r.Rect(387,19,5,13,chartColor);r.Rect(396,12,5,20,chartColor);
        var pointer=input?.Mice.FirstOrDefault()?.Position;
        if(pointer is { } pos&&pos.X* w/window.Size.X>=360&&pos.X*w/window.Size.X<420&&pos.Y*h/window.Size.Y<44)
            r.Text(365,47,"STATISTICS",White,1);
        var followColor=spectator.Following?Cyan:White;
        r.Circle(new(448,22),7,followColor);
        r.Line(new(435,22),new(444,22),1,followColor);r.Line(new(452,22),new(461,22),1,followColor);
        r.Line(new(448,9),new(448,18),1,followColor);r.Line(new(448,26),new(448,35),1,followColor);
        if(pointer is { } followPointer&&followPointer.X*w/window.Size.X>=420&&followPointer.X*w/window.Size.X<476&&followPointer.Y*h/window.Size.Y<44)
            r.Text(420,47,"FOLLOW SHIP",White,1);
        var colliderColor=collidersVisible?Cyan:White;
        DrawBrackets(new(500,22),9,colliderColor);
        r.Line(new(495,22),new(505,22),1,colliderColor);r.Line(new(500,17),new(500,27),1,colliderColor);
        if(pointer is { } colliderPointer&&colliderPointer.X*w/window.Size.X>=476&&colliderPointer.X*w/window.Size.X<524&&colliderPointer.Y*h/window.Size.Y<44)
            r.Text(476,47,"COLLIDERS (F2)",White,1);
        r.Text(Math.Max(538,w-300),14,$"{speed:0.##}X  TICK {frame.Tick}",White);
        for(int team=0;team<2;team++)
        {
            int count=0;float heat=0,shield=0;
            foreach(var ship in frame.Ships)if(ship.Team==team){count++;heat+=ship.Heat;shield+=ship.Shield;}
            if(count>0){heat/=count;shield/=count;}
            r.Text(14,58+team*22,$"{(team==0?"CYAN":"RED ")} ALIVE {(team==0?population.Cyan:population.Red)}  HEAT {heat*100:0}%  SHIELD {shield*100:0}%",team==0?Cyan:Red);
        }
        r.Text(14,98,$"ECS ALIVE {AliveEntities}  SHIPS {battle.ShipCount}  DOCKED {battle.ShipCount-frame.Ships.Count}  TURRETS {battle.TurretCount}  PROJECTILES {battle.ProjectileCount}  DEBRIS {battle.DebrisCount}  PARTICLES {battle.ParticleCount}",White,1);
        r.Text(14,h-58,$"TICK LAST {statistics.LastMs:0.00} MEAN {statistics.MeanMs:0.00} P99 {statistics.P99Ms:0.00} MS",White);
        r.Text(14,h-36,$"TICKS/S {ticksPerSecond:0.0}  FPS {renderFps:0.0}  RENDER CPU {(renderCpuMs>0?renderCpuMs.ToString("0.00"):"--")} MS",White);
        if(statisticsVisible)
        {
            float x=w-374,y=138;
            r.Rect(x,y,360,308,new(.012f,.02f,.035f,.97f));
            r.Text(x+12,y+12,"SIMULATION",Cyan);
            r.Text(x+12,y+36,$"P95 {statistics.P95Ms:0.00} MAX {statistics.MaximumMs:0.00} MS",White);
            r.Text(x+12,y+54,$"SAMPLES {statistics.SampleCount}/{SimulationStatistics.WindowCapacity}",White);
            r.Text(x+12,y+72,$"BUDGET {TickBudgetMs:0.00} MS {statistics.MeanMs/TickBudgetMs*100:0.0}%",White);
            r.Text(x+12,y+96,"STAGE MEAN MS",Cyan);
            for(int i=0;i<Battle<SecsStore>.StageNames.Length;i++)
                r.Text(x+12,y+116+i*18,$"{Battle<SecsStore>.StageNames[i]} {statistics.StageMeanMs[i]:0.000}",White);
            r.Text(x+12,y+284,$"SHOTS {frame.Counters.Shots} HITS {frame.Counters.Hits}",White,1);
            r.Text(x+12,y+296,$"HEAVY {frame.Counters.HeavyShots} TURRET {frame.Counters.TurretShots}",White,1);
        }
        if(r.HasAtlas)r.Text(14,h-16,$"BODIES {battle.PhysicsBodies} FIXTURES {battle.PhysicsFixtures} ARCH {storage.Archetypes} CHUNKS {storage.Chunks} NATIVE {storage.NativeBytes/1048576.0:0.00} MIB ACCESSORS {storage.AccessorBytes/1048576.0:0.00} MIB HEAP {managedBytes/1048576.0:0.00} MIB",White,1);
        else r.Text(14,h-16,"TEMPORARY PROCEDURAL SHIPS - ATLAS PENDING",new(.9f,.73f,.38f,1),1);
        var selectedShip=frame.Ships.FirstOrDefault(s=>s.Serial==selected);
        if(selected!=-1&&selectedShip.Serial==selected)r.Text(14,110,$"SHIP {selected} {((ShipClass)selectedShip.Kind)} HULL {selectedShip.Hull*100:0}% SHIELD {selectedShip.Shield*100:0}%",White,1);
        window.Title=$"Space Battle | {battle.Framework} | tick {frame.Tick} | alive {AliveEntities} | p99 {statistics.P99Ms:0.00} ms";
    }
    private void DisposeGraphics() { profiler?.Dispose();profiler=null;renderer?.Dispose();renderer=null;input?.Dispose();input=null;gl?.Dispose();gl=null; }
    private object CameraShip(ShipObservation ship)
    {
        var screen=Screen(ship.Position);
        return new {ship.Serial,ship.Team,ship.Kind,ship.Hull,ship.Shield,ship.Heat,ship.DockCarrierSerial,WorldX=ship.Position.X,WorldY=ship.Position.Y,X=screen.X,Y=screen.Y,ship.Radius,Padding=ship.Radius*2.2f*Scale+18};
    }
    private void DrawBrackets(Vector2 p,float radius,Vector4 color)
    {
        float length=Math.Min(12,radius*.4f);
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)
        {
            var corner=p+new Vector2(x,y)*radius;
            renderer!.Line(corner,corner-new Vector2(x*length,0),1.5f,color);
            renderer.Line(corner,corner-new Vector2(0,y*length),1.5f,color);
        }
    }
    public void Dispose() { DisposeGraphics();battle.Dispose();window.Dispose(); }
}
