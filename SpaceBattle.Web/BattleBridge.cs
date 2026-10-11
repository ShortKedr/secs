using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Diagnostics;
using SpaceBattle;

namespace SpaceBattle.Web;

[SupportedOSPlatform("browser")]
public static unsafe partial class BattleBridge
{
    private const int HeaderValues = 80;
    private const int RowValues = 16;
    private static Battle<SecsStore>? battle;
    private static readonly BattleFrame frame = new();
    private static double* buffer;
    private static int capacity;
    private static int profile;
    private static readonly SimulationStatistics statistics = new(Battle<SecsStore>.StageNames.Length);
    private static long nextStatisticsRefresh;

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
                EndlessReinforcements = true,
                ReplenishCarriers = false,
                ChurnMultiplier = requested == 0 ? 0.5f : requested == 1 ? 1f : 2f
            });
        }
        catch { store.Dispose(); throw; }
        profile = requested;
        statistics.Reset();
        nextStatisticsRefresh = 0;
        battle.Validate();
        return Snapshot();
    }

    [JSExport]
    public static int Advance(int ticks)
    {
        if (ticks is < 0 or > 600) throw new ArgumentOutOfRangeException(nameof(ticks));
        var current = Current;
        for (int i = 0; i < ticks; i++)
        {
            current.Step();
            statistics.RecordTick(current.LastTickMs, current.LastStageMs);
        }
        return Snapshot();
    }

    [JSExport]
    public static string Checksum() => Current.Checksum().ToString("X16");

    [JSExport]
    public static string Revision() => ArmamentRules.Revision;

    [JSExport]
    public static double Reinforcement(int team, int field)
    {
        var wave = Current.Reinforcement(team);
        return field switch
        {
            0 => wave.Wave, 1 => wave.Corner, 2 => wave.Composition, 3 => wave.PendingShips,
            4 => wave.ArrivalTick, 5 => wave.NextSpawnTick, 6 => wave.EndTick,
            7 => wave.Position.X, 8 => wave.Position.Y, 9 => wave.Active ? 1 : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    // On-demand work diagnostics stay outside Step timing and the rendering protocol.
    [JSExport]
    public static double DiagnosticStage(int stage) => stage >= 0 && stage < Current.LastStageMs.Length
        ? Current.LastStageMs[stage] : throw new ArgumentOutOfRangeException(nameof(stage));

    [JSExport]
    public static double DiagnosticPhysicsQuery(int field)
    {
        var work = Current.PhysicsQueries;
        return field switch
        {
            0 => work.Sweeps, 1 => work.EmptySweeps, 2 => work.Rays, 3 => work.PointQueries,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

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
#if WEB_CORECLR_R2R
        return "coreclr-r2r-binary";
#elif WEB_CORECLR_INTERPRETER
        return "coreclr-interpreter-binary";
#elif WEB_INTERPRETER
        return "interpreter-binary";
#else
        return "aot-binary";
#endif
    }

    [JSExport]
    public static int Snapshot()
    {
        var started = Stopwatch.GetTimestamp();
        var current = Current;
        current.Capture(frame, refreshEntities: false);
        int rows = frame.Ships.Count + frame.Projectiles.Count + frame.Debris.Count + frame.Particles.Count + frame.Turrets.Count;
        EnsureCapacity(rows);
        buffer[0] = 3;
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
        if (started >= nextStatisticsRefresh || statistics.SampleCount == 1)
        {
            statistics.Refresh();
            var storage = current.Store.Diagnostics();
            buffer[32] = statistics.SampleCount;
            buffer[33] = statistics.MeanMs;
            buffer[34] = statistics.P95Ms;
            buffer[35] = statistics.P99Ms;
            buffer[36] = statistics.MaximumMs;
            buffer[38] = storage.NativeBytes ?? double.NaN;
            buffer[39] = storage.AccessorBytes ?? double.NaN;
            buffer[40] = storage.Archetypes ?? double.NaN;
            buffer[41] = storage.Chunks ?? double.NaN;
            buffer[44] = GC.GetTotalMemory(false);
            for (int i = 0; i < statistics.StageMeanMs.Length; i++) buffer[46 + i] = statistics.StageMeanMs[i];
            for (int i = 0; i < 3; i++) buffer[55 + i] = GC.CollectionCount(i);
            nextStatisticsRefresh = started + Stopwatch.Frequency / 4;
        }
        buffer[37] = current.ShipCount + current.ProjectileCount + current.DebrisCount + current.ParticleCount + current.TurretCount;
        buffer[42] = current.PhysicsBodies;
        buffer[43] = current.PhysicsFixtures;
        buffer[58] = current.Tactics.TargetSwitches;
        buffer[59] = current.Tactics.TargetRetentions;
        buffer[60] = current.Tactics.GroupOrders;
        buffer[61] = current.Tactics.CoverOrders;
        buffer[62] = current.ShipCount - frame.Ships.Count;
        buffer[63] = current.TurretCount;
        buffer[64] = frame.Turrets.Count;
        buffer[65] = c.HeavyShots;
        buffer[66] = c.TurretShots;
        buffer[67] = c.ReinforcementWaves;
        buffer[68] = c.ReinforcementShips;
        buffer[69] = current.Reinforcement(0).PendingShips + current.Reinforcement(1).PendingShips;
        buffer[70] = current.TeamShipCount(0);
        buffer[71] = current.TeamShipCount(1);
        for (int i = 72; i < HeaderValues; i++) buffer[i] = 0;
        double* destination = buffer + HeaderValues;
        CopyRows(frame.Ships, ref destination);
        CopyRows(frame.Projectiles, ref destination);
        CopyRows(frame.Debris, ref destination);
        CopyRows(frame.Particles, ref destination);
        foreach (var turret in frame.Turrets)
        {
            destination[0] = turret.Serial;
            destination[1] = turret.Team;
            destination[2] = (int)turret.Kind;
            destination[3] = turret.Position.X;
            destination[4] = turret.Position.Y;
            destination[5] = destination[6] = 0;
            destination[7] = turret.Angle;
            destination[8] = turret.Radius;
            destination[9] = turret.ParentSerial;
            destination[10] = turret.Recoil;
            destination[11] = turret.Heat;
            destination[12] = .28;
            destination[13] = .32;
            destination[14] = .34;
            destination[15] = 1;
            destination += RowValues;
        }
        buffer[45] = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        return checked((int)(nint)buffer);
    }

    private static Battle<SecsStore> Current => battle ?? throw new InvalidOperationException("Reset the battle first.");

    private static void EnsureCapacity(int rows)
    {
        if (buffer != null && rows <= capacity) return;
        int nextCapacity = Math.Max(rows, Math.Max(256, capacity * 2));
        var replacement = (double*)NativeMemory.Alloc((nuint)((HeaderValues + nextCapacity * RowValues) * sizeof(double)));
        if (replacement == null) throw new OutOfMemoryException();
        if (buffer != null) Buffer.MemoryCopy(buffer, replacement, HeaderValues * sizeof(double), HeaderValues * sizeof(double));
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
