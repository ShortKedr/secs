using System.Numerics;
using SpaceBattle;

namespace SpaceBattle.Game;

internal static class SpectatorCameraValidation
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidOperationException($"Observer validation failed: {name}");
        }
        var ships = new Dictionary<int, ShipObservation>();
        bool Lookup(int serial, out ShipObservation ship) => ships.TryGetValue(serial, out ship);
        ShipObservation Ship(int serial, Vector2 position, float radius = 8, int target = -1, int carrier = -1)
            => new(serial, 0, 0, position, radius, 1, 1, 0, target, carrier);
        RenderEntity Visible(ShipObservation ship) => new(ship.Serial, ship.Team, ship.Kind, ship.Position,
            Vector2.Zero, 0, ship.Radius, ship.Hull, ship.Shield, ship.Heat, Vector4.One);
        var noDeath = new ObservedShipDeath(-1, -1, Vector2.Zero, -1);
        var camera = new SpectatorCamera();
        Vector2 Screen(ShipObservation ship, CameraViewport view)
        {
            var delta = (ship.Position - camera.Center) * camera.Scale;
            return view.Origin + new Vector2(delta.X, -delta.Y);
        }
        void Fits(ShipObservation ship, CameraViewport view)
        {
            var screen = Screen(ship, view);
            float pad = ship.Radius * 2.2f * camera.Scale + 18;
            Check(screen.X-pad>=view.Minimum.X-.02f&&screen.X+pad<=view.Maximum.X+.02f&&
                screen.Y-pad>=view.Minimum.Y-.02f&&screen.Y+pad<=view.Maximum.Y+.02f, "full sprite safe fit");
        }
        foreach(var view in new CameraViewport[]{new(1440,900,false),new(1440,900,true),new(540,960,false),new(960,540,true),new(2560,720,true)})
        foreach(var separation in new Vector2[]{new(900,0),new(0,900),new(-1200,-700),new(0,0)})
        foreach(var radii in new (float Main,float Target)[]{(4,4),(40,3),(3,60)})
        {
            ships.Clear();ships[1]=Ship(1,new(-100,70),radii.Main,2);ships[2]=Ship(2,ships[1].Position+separation,radii.Target);
            Check(camera.Start(1,Lookup),"start alive");camera.Resolve(Lookup,noDeath,[]);camera.Advance(0,view,.5f);
            Fits(ships[1],view);Fits(ships[2],view);
            var desired=ships[1].Position+separation*.28f;
            Check(Vector2.Distance(Screen(ships[1],view),view.Midpoint)<=Vector2.Distance(Screen(ships[2],view),view.Midpoint)+.01f,"primary closer to safe center");
            var screenDelta=(desired-camera.Center)*camera.Scale;
            Check(Vector2.Distance(view.Origin+new Vector2(screenDelta.X,-screenDelta.Y),view.Midpoint)<.01f,"weighted snap center and transform offsets");
            Check(camera.Scale*ships[1].Radius*2<=72.01f,"main footprint cap");
            ships[1]=ships[1] with {Position=ships[1].Position+new Vector2(500,-350)};
            ships[2]=ships[2] with {Position=ships[2].Position+new Vector2(-600,400)};
            camera.Resolve(Lookup,noDeath,[]);camera.Advance(.016f,view,.5f);
            Fits(ships[1],view);Fits(ships[2],view);
            Check(Vector2.Distance(Screen(ships[1],view),view.Midpoint)<=Vector2.Distance(Screen(ships[2],view),view.Midpoint)+.01f,"moving primary centering priority");
        }

        var viewport=new CameraViewport(1440,900,false);
        ships.Clear();ships[1]=Ship(1,Vector2.Zero,8);
        camera.Start(1,Lookup);camera.Resolve(Lookup,noDeath,[]);camera.Advance(0,viewport,.5f);
        float oldScale=camera.Scale;
        ships[1]=ships[1] with {Radius=16};camera.Resolve(Lookup,noDeath,[]);camera.Advance(.05f,viewport,.5f);
        Check(Math.Abs(camera.Scale-(oldScale+(2.25f-oldScale)*(1-MathF.Exp(-.05f/.10f))))<.001f,"zoom out tau");
        oldScale=camera.Scale;
        ships[1]=ships[1] with {Radius=8};camera.Resolve(Lookup,noDeath,[]);camera.Advance(.05f,viewport,.5f);
        Check(Math.Abs(camera.Scale-(oldScale+(4.5f-oldScale)*(1-MathF.Exp(-.05f/.6f))))<.001f,"zoom in tau");
        ships[1]=ships[1] with {Position=new(10,0)};camera.Resolve(Lookup,noDeath,[]);camera.Advance(.016f,viewport,.5f);
        var focus=camera.Center-new Vector2(viewport.Origin.X-viewport.Midpoint.X,-(viewport.Origin.Y-viewport.Midpoint.Y))/camera.Scale;
        Check(Math.Abs(focus.X-10*(1-MathF.Exp(-.016f/.16f)))<.001f,"pan tau");

        ships[2]=Ship(2,new(100,0));ships[3]=Ship(3,new(-100,0));
        ships.Remove(1);
        Check(camera.Resolve(Lookup,new(1,3,Vector2.Zero,10),ships.Values.Select(Visible).ToArray())&&camera.ObservedSerial==3,"live killer preferred (including friendly)");
        ships[1]=Ship(1,Vector2.Zero);camera.Start(1,Lookup);camera.Resolve(Lookup,noDeath,[]);ships.Remove(1);
        camera.Resolve(Lookup,new(1,99,Vector2.Zero,11),new[]{Visible(ships[3]),Visible(ships[2])});
        Check(camera.ObservedSerial==2,"nearest visible serial tie independent of list order");
        ships[1]=Ship(1,Vector2.Zero);camera.Start(1,Lookup);camera.Resolve(Lookup,noDeath,[]);ships.Remove(1);
        ships[3]=ships[3] with {Position=new(-10,0)};
        camera.Resolve(Lookup,new(1,-1,Vector2.Zero,11),new[]{Visible(ships[2]),Visible(ships[3])});
        Check(camera.ObservedSerial==3,"distance precedes serial in fallback");
        ships[1]=Ship(1,Vector2.Zero);camera.Start(1,Lookup);camera.Resolve(Lookup,noDeath,[]);ships.Remove(1);
        ships[4]=Ship(4,Vector2.Zero,8,-1,2);
        camera.Resolve(Lookup,new(1,-1,Vector2.Zero,12),new[]{Visible(ships[3])});
        Check(camera.ObservedSerial==3,"fallback excludes ships absent from visible frame");

        ships[2]=ships[2] with {TargetSerial=3};
        ships[1]=Ship(1,Vector2.Zero,8,-1,2);camera.Start(1,Lookup);camera.Resolve(Lookup,noDeath,[]);
        Check(camera.ObservedSerial==1&&camera.MainSerial==2,"docked child retains watch and frames carrier");
        Check(camera.TargetSerial==-1&&camera.Target is null,"docked watch does not inherit carrier live target");
        ships[1]=ships[1] with {DockCarrierSerial=-1,TargetSerial=3};camera.Resolve(Lookup,noDeath,[]);
        Check(camera.MainSerial==1&&camera.TargetSerial==3,"undock restores child and target");
        ships[1]=ships[1] with {TargetSerial=99};camera.Resolve(Lookup,noDeath,[]);
        Check(camera.TargetSerial==-1,"invalid target clears highlight");
        camera.Stop();Check(!camera.Following&&camera.MainSerial==-1&&camera.TargetSerial==-1,"manual stop clears observer marks");
        Check(!camera.Start(-1,Lookup)&&!camera.Start(99,Lookup),"invalid follow request");
        camera.Start(1,Lookup);camera.Resolve(Lookup,noDeath,[]);ships.Clear();
        Check(camera.Resolve(Lookup,new(1,-1,Vector2.Zero,13),[])&&!camera.Following,"no visible survivors stops");
        Console.WriteLine($"Observer/camera pure validation PASS ({checks} checks: safe fit, weighted center, footprint, smoothing, killer/fallback, docking, cancellation).");
    }
}
