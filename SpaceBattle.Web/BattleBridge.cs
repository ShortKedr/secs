using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using SpaceBattle;

namespace SpaceBattle.Web;

[SupportedOSPlatform("browser")]
public static unsafe partial class BattleBridge
{
    private const int HeaderValues = 32;
    private const int RowValues = 16;
    private static Battle<SecsStore>? battle;
    private static readonly BattleFrame frame = new();
    private static double* buffer;
    private static int capacity;
    private static int profile;

    [JSExport]
    public static int Reset(string requestedProfile, double seed)
    {
        if (!double.IsFinite(seed) || seed != Math.Truncate(seed) || seed < 0 || seed > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(seed));
        int requested = requestedProfile switch
        {
            "low" => 0,
            "nominal" => 1,
            "high" => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(requestedProfile))
        };
        battle?.Dispose();
        battle = null;
        var store = new SecsStore();
        try
        {
            battle = new Battle<SecsStore>(store, new BattleConfig
            {
                Seed = (uint)seed,
                ChurnMultiplier = requested == 0 ? 0.5f : requested == 1 ? 1f : 2f
            });
        }
        catch { store.Dispose(); throw; }
        profile = requested;
        battle.Validate();
        return Snapshot();
    }

    [JSExport]
    public static int Advance(int ticks)
    {
        if (ticks is < 0 or > 600) throw new ArgumentOutOfRangeException(nameof(ticks));
        var current = Current;
        for (int i = 0; i < ticks; i++) current.Step();
        return Snapshot();
    }

    [JSExport]
    public static string Checksum() => Current.Checksum().ToString("X16");

    [JSExport]
    public static string Revision() => TacticalRules.Revision;

    // On-demand diagnostics do not enlarge the per-frame rendering buffer.
    [JSExport]
    public static double TacticCounter(int field) => field switch
    {
        0 => Current.Tactics.TargetSwitches, 1 => Current.Tactics.TargetRetentions,
        2 => Current.Tactics.GroupOrders, 3 => Current.Tactics.CoverOrders,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    [JSExport]
    public static int ShipTactic(int serial, int field)
    {
        if (!Current.TryFindShip(serial, out var entity)) return -1;
        var brain = Current.Store.Get<Brain>(entity);
        return field switch
        {
            0 => (int)brain.Mode, 1 => Serial(brain.Target), 2 => Serial(brain.SquadLeader),
            3 => Serial(brain.CoverAlly), 4 => brain.TargetLockUntilTick,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        static int Serial(EntityKey e) => Current.Store.Alive(e) && Current.Store.Has<Identity>(e)
            ? Current.Store.Get<Identity>(e).Serial : -1;
    }

    [JSExport]
    public static string Validate()
    {
        Current.Validate();
        return Checksum();
    }

    [JSExport]
    public static string Mode()
    {
#if WEB_INTERPRETER
        return "interpreter-binary";
#else
        return "aot-binary";
#endif
    }

    [JSExport]
    public static int Snapshot()
    {
        var current = Current;
        current.Capture(frame);
        int rows = frame.Ships.Count + frame.Projectiles.Count + frame.Debris.Count + frame.Particles.Count;
        EnsureCapacity(rows);
        buffer[0] = 1;
        buffer[1] = frame.Tick;
        buffer[2] = frame.ArenaHalfSize;
        buffer[3] = current.Config.Seed;
        buffer[4] = profile;
        buffer[5] = frame.LastTickMs;
        buffer[6] = current.ShipCount;
        buffer[7] = current.ProjectileCount;
        buffer[8] = current.DebrisCount;
        buffer[9] = current.ParticleCount;
        buffer[10] = frame.Ships.Count;
        buffer[11] = frame.Projectiles.Count;
        buffer[12] = frame.Debris.Count;
        buffer[13] = frame.Particles.Count;
        var c = frame.Counters;
        buffer[14] = c.Shots;
        buffer[15] = c.Hits;
        buffer[16] = c.ShipsCreated;
        buffer[17] = c.ShipsDestroyed;
        buffer[18] = c.AimDecisions;
        buffer[19] = c.ImpreciseAimDecisions;
        buffer[20] = c.FriendlyFireHits;
        buffer[21] = c.SelfHits;
        buffer[22] = c.Dockings;
        buffer[23] = c.Builds;
        buffer[24] = c.Repairs;
        buffer[25] = c.Overheats;
        buffer[26] = c.ShieldBreaks;
        buffer[27] = c.DebrisHits;
        buffer[28] = c.StructuralAdds;
        buffer[29] = c.StructuralRemoves;
        buffer[30] = rows;
        buffer[31] = (HeaderValues + rows * RowValues) * sizeof(double);
        double* destination = buffer + HeaderValues;
        CopyRows(frame.Ships, ref destination);
        CopyRows(frame.Projectiles, ref destination);
        CopyRows(frame.Debris, ref destination);
        CopyRows(frame.Particles, ref destination);
        return checked((int)(nint)buffer);
    }

    private static Battle<SecsStore> Current => battle ?? throw new InvalidOperationException("Reset the battle first.");

    private static void EnsureCapacity(int rows)
    {
        if (buffer != null && rows <= capacity) return;
        int nextCapacity = Math.Max(rows, Math.Max(256, capacity * 2));
        var replacement = (double*)NativeMemory.Alloc((nuint)((HeaderValues + nextCapacity * RowValues) * sizeof(double)));
        if (replacement == null) throw new OutOfMemoryException();
        NativeMemory.Free(buffer);
        buffer = replacement;
        capacity = nextCapacity;
    }

    // JavaScript reads reusable views over this WASM memory, without JSON or per-frame checksums.
    private static void CopyRows(List<RenderEntity> entities, ref double* destination)
    {
        foreach (var e in entities)
        {
            destination[0] = e.Serial;
            destination[1] = e.Team;
            destination[2] = e.Kind;
            destination[3] = e.Position.X;
            destination[4] = e.Position.Y;
            destination[5] = e.Velocity.X;
            destination[6] = e.Velocity.Y;
            destination[7] = e.Angle;
            destination[8] = e.Radius;
            destination[9] = e.Hull;
            destination[10] = e.Shield;
            destination[11] = e.Heat;
            destination[12] = e.Color.X;
            destination[13] = e.Color.Y;
            destination[14] = e.Color.Z;
            destination[15] = e.Color.W;
            destination += RowValues;
        }
    }
}
