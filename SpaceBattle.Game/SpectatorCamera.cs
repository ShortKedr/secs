using System.Numerics;
using SpaceBattle;

namespace SpaceBattle.Game;

internal delegate bool ShipLookup(int serial, out ShipObservation observation);

internal readonly record struct CameraViewport(float Width, float Height, bool StatisticsVisible)
{
    public Vector2 Origin => new(Width * .5f, Height * .5f + 30);
    public Vector2 Minimum => new(14, 140);
    public Vector2 Maximum => new(Width - (StatisticsVisible ? 392 : 14), Height - 80);
    public Vector2 Midpoint => (Minimum + Maximum) * .5f;
    public Vector2 HalfSize => Vector2.Max(Vector2.One, (Maximum - Minimum) * .5f);
}

internal sealed class SpectatorCamera
{
    public bool Following => ObservedSerial >= 0;
    public int ObservedSerial { get; private set; } = -1;
    public int MainSerial { get; private set; } = -1;
    public int TargetSerial { get; private set; } = -1;
    public Vector2 Center { get; private set; }
    public float Scale { get; private set; } = 1;
    public ShipObservation Main { get; private set; }
    public ShipObservation? Target { get; private set; }
    private Vector2 focus, lastPosition;
    private bool snap;

    public void Stop()
    {
        ObservedSerial = MainSerial = TargetSerial = -1;
        Target = null;
    }

    public bool Start(int serial, ShipLookup lookup)
    {
        if (!lookup(serial, out var ship)) return false;
        ObservedSerial = serial;
        lastPosition = ship.Position;
        snap = true;
        return true;
    }

    // Returns true when the watch changes, so the caller can reset the Core death notice.
    public bool Resolve(ShipLookup lookup, ObservedShipDeath death, IReadOnlyList<RenderEntity> visible)
    {
        if (!Following) return false;
        bool changed = false;
        if (!lookup(ObservedSerial, out var ship))
        {
            Vector2 position = death.Serial == ObservedSerial ? death.Position : lastPosition;
            int replacement = -1;
            if (death.Serial == ObservedSerial && lookup(death.KillerSerial, out var killer)) replacement = killer.Serial;
            else
            {
                float nearest = float.PositiveInfinity;
                foreach (var candidate in visible)
                {
                    if (!lookup(candidate.Serial, out var alive) || alive.DockCarrierSerial >= 0) continue;
                    float distance = Vector2.DistanceSquared(position, alive.Position);
                    if (distance < nearest || distance == nearest && (replacement < 0 || alive.Serial < replacement))
                    { nearest = distance; replacement = alive.Serial; }
                }
            }
            if (replacement < 0 || !lookup(replacement, out ship)) { Stop(); return true; }
            ObservedSerial = replacement;
            snap = true;
            changed = true;
        }
        lastPosition = ship.Position;
        Main = ship;
        if (ship.DockCarrierSerial >= 0 && lookup(ship.DockCarrierSerial, out var carrier)) Main = carrier;
        MainSerial = Main.Serial;
        Target = ship.TargetSerial >= 0 && lookup(ship.TargetSerial, out var target) && target.DockCarrierSerial < 0
            && target.Serial != MainSerial ? target : null;
        TargetSerial = Target?.Serial ?? -1;
        return changed;
    }

    public void Advance(float dt, CameraViewport viewport, float baseScale, bool forceSnap = false)
    {
        if (!Following || MainSerial < 0) return;
        var desired = Main.Position + .28f * ((Target?.Position ?? Main.Position) - Main.Position);
        float wantedScale = Math.Min(baseScale * 24, 72 / Math.Max(.01f, Main.Radius * 2));
        wantedScale = FitScale(desired, wantedScale, viewport);
        bool immediate = snap || forceSnap;
        focus = immediate ? desired : Vector2.Lerp(focus, desired, Blend(dt, .16f));
        // Preserve primary centering priority even when a new target crosses the old focus.
        if (Target is { } t)
        {
            var axis = t.Position - Main.Position;
            float length = axis.LengthSquared();
            float along = length > 0 ? Vector2.Dot(focus - Main.Position, axis) / length : 0;
            if (along > .28f) focus -= axis * (along - .28f);
        }
        Scale = immediate ? wantedScale : Scale + (wantedScale - Scale) * Blend(dt, wantedScale < Scale ? .10f : .6f);
        Scale = FitScale(focus, Scale, viewport);
        var offset = viewport.Origin - viewport.Midpoint;
        Center = focus + new Vector2(offset.X, -offset.Y) / Scale;
        snap = false;
    }

    private static float Blend(float dt, float tau) => 1 - MathF.Exp(-Math.Max(0, dt) / tau);
    private float FitScale(Vector2 at, float limit, CameraViewport viewport)
    {
        var half = Vector2.Max(Vector2.One, viewport.HalfSize - new Vector2(18));
        float Fit(ShipObservation ship)
        {
            var delta = Vector2.Abs(ship.Position - at) + new Vector2(Math.Max(.01f, ship.Radius) * 2.2f);
            return Math.Min(half.X / delta.X, half.Y / delta.Y);
        }
        limit = Math.Min(limit, Fit(Main));
        if (Target is { } target) limit = Math.Min(limit, Fit(target));
        return Math.Max(.000001f, limit);
    }
}
