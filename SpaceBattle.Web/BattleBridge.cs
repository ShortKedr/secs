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
    private const int ColliderHeaderValues = 4, ColliderRowValues = 9;
    private static readonly List<DebugCollider> colliders = new();
    private static double* colliderBuffer;
    private static int colliderCapacity;
    private static bool colliderDebug;
    private static int profile;
    private static readonly SimulationStatistics statistics = new(Battle<SecsStore>.StageNames.Length);
    private static long nextStatisticsRefresh;

    [JSExport]
    public static int Reset(string requestedProfile, double seed)
        => ResetCore(requestedProfile, seed, false);

    [JSExport]
    public static int ResetPhysicsProfile(string requestedProfile, double seed, bool collectTimings)
        => ResetCore(requestedProfile, seed, collectTimings);

    private static int ResetCore(string requestedProfile, double seed, bool collectTimings)
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
                FleetComposition = FleetComposition.LightSwarm,
                Deployment = FleetDeployment.Tactical,
                Physics = new PhysicsSettings { CollectTimings = collectTimings },
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
    public static string BrowserContract() => "png-assets-v1";

    // Bounded diagnostic Step only; no snapshot/export and no alternate game rules.
    [JSExport]
    public static double DiagnosticStep(int ticks)
    {
        if (ticks is < 0 or > 120) throw new ArgumentOutOfRangeException(nameof(ticks));
        var started = Stopwatch.GetTimestamp();
        var current = Current;
        for (int i = 0; i < ticks; i++)
        {
            current.Step();
            statistics.RecordTick(current.LastTickMs, current.LastStageMs);
        }
        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    [JSExport]
    public static string FleetRevision() => Current.Config.FleetComposition == FleetComposition.LightSwarm
        ? FleetCompositionRules.Revision : "uniform-v1";

    [JSExport]
    public static string DeploymentRevision() => Current.Config.Deployment == FleetDeployment.Tactical
        ? FleetDeploymentRules.Revision : "legacy-random-v1";

    [JSExport]
    public static string PhysicsRevision() => PhysicsSettings.Revision;

    [JSExport]
    public static int DebugColliders(bool enabled)
    {
        colliderDebug = enabled;
        if (!enabled) colliders.Clear();
        return Snapshot();
    }

    // Snapshot prepares this optional buffer before publishing any heap views to JavaScript.
    [JSExport]
    public static int ColliderSnapshot() => colliderDebug ? checked((int)(nint)colliderBuffer) : 0;

    [JSExport]
    public static double PhysicsTiming(int field)
    {
        var time = Current.PhysicsTimings;
        return field switch { 0 => time.ContactCandidatesMs, 1 => time.SolverMs, 2 => time.HitCandidatesMs,
            3 => time.HitNarrowMs, _ => throw new ArgumentOutOfRangeException(nameof(field)) };
    }

    [JSExport]
    public static string Revision() => ArmamentRules.Revision;

    [JSExport]
    public static string DamageRevision() => DamageRules.Revision;
    [JSExport]
    public static string TacticalRevision() => TacticalRules.Revision;
    [JSExport]
    public static double DiagnosticTactics(int field)
    {
        var counters = Current.Tactics;
        return field switch { 0 => counters.GroupChanges, 1 => counters.FormationReviews,
            2 => counters.CompatibilityChecks, 3 => counters.ScoreEvaluations, 4 => counters.SpatialWork,
            5 => counters.BudgetFallbacks, 6 => counters.IntentProjectiles,
            _ => throw new ArgumentOutOfRangeException(nameof(field)) };
    }

    // On-demand layer inspection; never called by Step or rendering.
    [JSExport]
    public static double DiagnosticWeaponDamage(double amount, double shield, double armor, int weapon, int field)
    {
        var profile = DamageRules.ForWeapon((WeaponKind)weapon, Current.Config.Damage);
        var damage = DamageRules.Resolve((float)amount, (float)shield, (float)armor, profile);
        return field switch { 0 => damage.Shield, 1 => damage.Hull,
            2 => damage.Shield * profile.ShieldHeatPerDamage, 3 => damage.RemainingBase,
            _ => throw new ArgumentOutOfRangeException(nameof(field)) };
    }

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
    public static void ObserveShip(int serial) => Current.ObserveShip(serial);

    [JSExport]
    public static double ShipObservation(int serial, int field)
    {
        if (!Current.TryGetShipObservation(serial, out var ship)) return field == 0 ? 0 : -1;
        return field switch
        {
            0 => 1, 1 => ship.TargetSerial, 2 => ship.DockCarrierSerial,
            3 => ship.Position.X, 4 => ship.Position.Y, 5 => ship.Radius,
            6 => ship.Team, 7 => ship.Kind, 8 => ship.Hull, 9 => ship.Shield, 10 => ship.Heat,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    [JSExport]
    public static double ObservedDeath(int field)
    {
        var death = Current.ObservedDeath;
        return field switch
        {
            0 => death.Serial, 1 => death.KillerSerial, 2 => death.Position.X, 3 => death.Position.Y,
            4 => death.Tick, _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

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
            3 => Serial(brain.CoverAlly), 4 => brain.TargetLockUntilTick, 5 => brain.SquadSize,
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
        var started = Stopwatch.GetTimestamp();
        var current = Current;
        current.Capture(frame, refreshEntities: false);
        if (colliderDebug) CaptureColliders(current);
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
        buffer[72] = (int)current.Config.FleetComposition;
        for (int i = 73; i < HeaderValues; i++) buffer[i] = 0;
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

    private static void CaptureColliders(Battle<SecsStore> current)
    {
        current.CaptureColliders(colliders, refreshEntities: false);
        if (colliderBuffer == null || colliders.Count > colliderCapacity)
        {
            int next = Math.Max(colliders.Count, Math.Max(256, colliderCapacity * 2));
            var replacement = (double*)NativeMemory.Alloc((nuint)((ColliderHeaderValues + next * ColliderRowValues) * sizeof(double)));
            if (replacement == null) throw new OutOfMemoryException();
            NativeMemory.Free(colliderBuffer);
            colliderBuffer = replacement;
            colliderCapacity = next;
        }
        colliderBuffer[0] = 1;
        colliderBuffer[1] = colliders.Count;
        colliderBuffer[2] = ColliderRowValues;
        colliderBuffer[3] = (ColliderHeaderValues + colliders.Count * ColliderRowValues) * sizeof(double);
        double* destination = colliderBuffer + ColliderHeaderValues;
        foreach (var c in colliders)
        {
            destination[0] = c.Serial; destination[1] = (int)c.Kind; destination[2] = (int)c.Shape;
            destination[3] = c.Position.X; destination[4] = c.Position.Y; destination[5] = c.Angle;
            destination[6] = c.Radius; destination[7] = c.HalfExtents.X; destination[8] = c.HalfExtents.Y;
            destination += ColliderRowValues;
        }
    }

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
