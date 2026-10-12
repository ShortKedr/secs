using System.Numerics;

namespace SpaceBattle;

public static class WarpEffect
{
    public const int PortalKind = 4, ArrivalKind = 6, LastKind = 7, CloseTicks = 45, ArrivalTicks = 30;
    public static bool Foreground(int kind) => kind == HeavyImpactEffect.Kind || kind is >= ArrivalKind and <= LastKind;

    public static void Present(in Particle particle, int tick, out float radius, out float alpha)
    {
        var age = Math.Max(0, tick - particle.StartTick);
        if (particle.Kind < ArrivalKind)
        {
            var opening = Math.Clamp(age / 30f, 0, 1);
            var closing = Math.Clamp((particle.ExpiresTick - tick) / (float)CloseTicks, 0, 1);
            radius = particle.Size * (0.55f + 0.45f * opening) * closing;
            alpha = opening * closing * 0.7f * (0.9f + 0.1f * MathF.Sin(age * 0.12f));
        }
        else
        {
            var phase = Math.Clamp(age / (float)ArrivalTicks, 0, 1);
            radius = particle.Size * (0.5f + 0.7f * Math.Min(age / 8f, 1));
            alpha = (1 - phase) * (1 - phase) * 0.85f;
        }
    }
}

public static class WarpReinforcementRules
{
    public const string Revision = "corner-warp-v1";
    public static float ClassStrength(ShipClass kind) => kind switch
    {
        ShipClass.Interceptor => 0.75f, ShipClass.Fighter => 1, ShipClass.Bomber => 1.5f,
        ShipClass.Gunship => 2, ShipClass.Corvette => 3, ShipClass.Frigate => 4,
        ShipClass.Destroyer => 6, ShipClass.Support => 2,
        ShipClass.LightCarrier => 12, ShipClass.FleetCarrier => 18, _ => 24
    };
}

public readonly record struct ReinforcementStatus(int Wave, int Corner, int Composition, int PendingShips,
    int ArrivalTick, int NextSpawnTick, int EndTick, Vector2 Position, bool Active);

public sealed partial class Battle<TStore>
{
    private sealed class WarpWave
    {
        public int Number, Corner, Composition, NextWaveTick, ArrivalTick, NextSpawnTick, EndTick, NextShip;
        public Vector2 Position;
        public ShipClass[] Ships = [];
        public EntityKey Home = EntityKey.None, Gate = EntityKey.None;
        public bool Active;
    }
    private readonly WarpWave[] warpWaves = [new(), new()];
    private readonly float[] initialWarpStrength = new float[2];
    private int nextWarpCheckTick;
    private static readonly ShipClass[][] warpCompositions =
    [
        [ShipClass.Interceptor, ShipClass.Fighter, ShipClass.Fighter, ShipClass.Gunship, ShipClass.Support],
        [ShipClass.Corvette, ShipClass.Frigate, ShipClass.Fighter, ShipClass.Support, ShipClass.Destroyer],
        [ShipClass.Bomber, ShipClass.Bomber, ShipClass.Interceptor, ShipClass.Gunship, ShipClass.Support],
        [ShipClass.Fighter, ShipClass.Corvette, ShipClass.Gunship, ShipClass.Frigate, ShipClass.Support]
    ];

    public ReinforcementStatus Reinforcement(int team)
    {
        if (team is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(team));
        var wave = warpWaves[team];
        return new(wave.Number, wave.Corner, wave.Composition, wave.Active ? wave.Ships.Length - wave.NextShip : 0,
            wave.ArrivalTick, wave.NextSpawnTick, wave.EndTick, wave.Position, wave.Active);
    }

    private void InitializeWarpReinforcements()
    {
        if (!Config.EndlessReinforcements) return;
        foreach (var row in ships) initialWarpStrength[row.Identity.Team] += WarpReinforcementRules.ClassStrength(row.Identity.Class);
        nextWarpCheckTick = Config.ReinforcementCheckIntervalTicks;
    }

    private void UpdateWarpReinforcements()
    {
        if (!Config.EndlessReinforcements) return;
        var changed = false; var spawned = false;
        var cap = Config.ShipsPerTeam + Config.CarriersPerTeam;
        for (var team = 0; team < 2; team++)
        {
            var wave = warpWaves[team];
            if (!wave.Active || Tick < wave.NextSpawnTick) continue;
            if (wave.NextShip >= wave.Ships.Length || teamCounts[team] >= cap || Tick >= wave.EndTick)
            {
                CloseWarp(wave); changed = true; continue;
            }
            var kind = wave.Ships[wave.NextShip];
            if (ShipCatalog.IsCarrier(kind) && CountCarriers(team) >= Config.CarriersPerTeam)
            {
                wave.NextShip++; wave.NextSpawnTick = Tick + Config.ReinforcementSpawnIntervalTicks;
                continue;
            }
            wave.NextSpawnTick = Tick + Config.ReinforcementSpawnIntervalTicks;
            if (!WarpSpawnPosition(wave, kind, out var position)) continue;
            if (!AliveShip(wave.Home)) wave.Home = FirstWarpHome(team);
            var ship = SpawnShip(team, kind, position, ShipCatalog.IsCarrier(kind) ? EntityKey.None : wave.Home);
            var inward = Direction(-position);
            ref var transform = ref Store.Get<Transform>(ship);
            transform.Angle = MathF.Atan2(inward.Y, inward.X);
            ref var motion = ref Store.Get<Motion>(ship);
            motion.Velocity = inward * Math.Min(motion.MaxSpeed, ShipCatalog.IsCarrier(kind) ? 5 : 28);
            if (ShipCatalog.IsCarrier(kind)) wave.Home = ship;
            SpawnWarpParticle(position, WarpEffect.ArrivalKind + team, Math.Clamp(ShipCatalog.Radius(kind) * 1.8f, 5, 16), Tick + WarpEffect.ArrivalTicks);
            wave.NextShip++; teamCounts[team]++; Counters.ReinforcementShips++;
            spawned = changed = true;
            if (wave.NextShip >= wave.Ships.Length) CloseWarp(wave);
        }
        if (spawned) { Store.Commit(); CollectShips(); SyncTurrets(); }
        if (Tick >= nextWarpCheckTick)
        {
            nextWarpCheckTick = Tick + Config.ReinforcementCheckIntervalTicks;
            for (var team = 0; team < 2; team++)
            {
                var wave = warpWaves[team];
                if (wave.Active || Tick < wave.NextWaveTick || teamCounts[team] >= cap) continue;
                float strength = 0;
                foreach (var row in ships)
                    if (row.Identity.Team == team)
                        strength += WarpReinforcementRules.ClassStrength(row.Identity.Class) *
                            (Math.Clamp(float.IsFinite(row.Hull) ? row.Hull : 0, 0, 1) * 0.65f +
                             Math.Clamp(float.IsFinite(row.Shield) ? row.Shield : 0, 0, 1) * 0.35f);
                if (teamCounts[team] > cap * Config.ReinforcementThresholdFraction &&
                    strength > initialWarpStrength[team] * Config.ReinforcementThresholdFraction) continue;
                PlanWarp(team, wave, cap); changed = true;
            }
        }
        if (changed) Store.Commit();
    }

    private void PlanWarp(int team, WarpWave wave, int cap)
    {
        wave.Number++;
        var seedOffset = (int)((Config.Seed ^ (uint)(team * 7919)) & 3);
        wave.Corner = (seedOffset + wave.Number - 1) % 4;
        wave.Composition = (seedOffset + wave.Number - 1) % warpCompositions.Length;
        var edge = Config.ArenaHalfSize - 44;
        wave.Position = new((wave.Corner & 1) == 0 ? -edge : edge, (wave.Corner & 2) == 0 ? -edge : edge);
        var count = Math.Min(cap - teamCounts[team], Math.Max(3, (int)MathF.Ceiling(cap * Config.ReinforcementWaveFraction)));
        var kinds = new ShipClass[count];
        var carriers = CountCarriers(team);
        var addCarrier = carriers == 0 || carriers < Config.CarriersPerTeam && wave.Composition is 1 or 3;
        var carrierCount = addCarrier ? Math.Min(carriers == 0 && wave.Composition == 3 ? 2 : 1, Config.CarriersPerTeam - carriers) : 0;
        var composition = warpCompositions[wave.Composition];
        for (var i = 0; i < count; i++)
            kinds[i] = i < carrierCount ? ShipClass.LightCarrier + (wave.Number + i - 1) % Config.CarriersPerTeam
                : Config.FleetComposition == FleetComposition.LightSwarm
                    ? FleetCompositionRules.Escort(Config.FleetComposition, i - carrierCount, wave.Composition)
                    : composition[(i - carrierCount) % composition.Length];
        wave.Ships = kinds; wave.NextShip = 0; wave.Active = true;
        wave.ArrivalTick = wave.NextSpawnTick = Tick + Config.ReinforcementWarmupTicks;
        wave.EndTick = wave.ArrivalTick + count * Config.ReinforcementSpawnIntervalTicks + 300;
        wave.Home = FirstWarpHome(team);
        wave.Gate = SpawnWarpParticle(wave.Position, WarpEffect.PortalKind + team, 40, wave.EndTick + WarpEffect.CloseTicks);
        Counters.ReinforcementWaves++;
    }

    private void CloseWarp(WarpWave wave)
    {
        wave.Active = false; wave.EndTick = Tick;
        wave.NextWaveTick = Tick + Config.ReinforcementCooldownTicks;
        if (Store.Alive(wave.Gate)) Store.Get<Particle>(wave.Gate).ExpiresTick = Tick + WarpEffect.CloseTicks;
    }

    private int CountCarriers(int team)
    {
        var count = 0;
        foreach (var row in ships) if (row.Identity.Team == team && ShipCatalog.IsCarrier(row.Identity.Class)) count++;
        return count;
    }

    private EntityKey FirstWarpHome(int team)
    {
        foreach (var row in ships)
            if (row.Identity.Team == team && ShipCatalog.IsCarrier(row.Identity.Class)) return row.Entity;
        return EntityKey.None;
    }

    private bool WarpSpawnPosition(WarpWave wave, ShipClass kind, out Vector2 position)
    {
        var radius = ShipCatalog.Radius(kind) + 1;
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var slot = (wave.NextShip + attempt) % 32;
            var angle = slot < 8 ? slot * MathF.Tau / 8 : (slot - 8) * MathF.Tau / 24;
            var distance = slot == 0 ? 0 : slot < 8 ? 16 : 28;
            position = wave.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            Counters.ReinforcementPlacementChecks++;
            if (physics.CanSpawn(position, radius)) return true;
        }
        position = default; return false;
    }

    private EntityKey SpawnWarpParticle(Vector2 position, int kind, float radius, int expiry)
    {
        if (Config.ParticleMultiplier == 0 || particles.Count >= Config.MaxParticles) return EntityKey.None;
        var e = Store.Create(); var number = ++serial;
        Store.Set(e, new Transform { Position = position, Previous = position });
        Store.Set(e, new Motion());
        Store.Set(e, new Particle { Serial = number, Kind = kind, Size = radius, StartTick = Tick, ExpiresTick = expiry, Color = Vector4.One });
        particles.Add(new(e, number)); Counters.StructuralAdds += 3;
        return e;
    }
}
