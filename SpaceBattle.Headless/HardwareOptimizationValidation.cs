using System.Numerics;
using System.Reflection;
using SpaceBattle;

internal static class HardwareOptimizationValidation
{
    public static void Run()
    {
        var map = new EntityIndexTable();
        var first = new EntityKey(0, 1); var reused = new EntityKey(0, 2);
        map.Add(first, 17);
        Check(map[first] == 17 && !map.TryGetValue(reused, out _), "generation guards");
        map.Remove(reused); Check(map[first] == 17, "stale remove cannot invalidate current slot");
        map.Add(reused, 23);
        Check(!map.TryGetValue(first, out _) && map[reused] == 23, "slot reuse");
        map.Add(new(8192, 3), 31); Check(map[reused] == 23, "resize preserves keys");
        var huge = new EntityKey(int.MaxValue, 9); map.Add(huge, 42);
        Check(map[huge] == 42 && !map.TryGetValue(EntityKey.None, out _), "bounded sparse fallback");
        map.Clear(); Check(!map.TryGetValue(reused, out _) && !map.TryGetValue(huge, out _), "phase invalidation");
        map.Add(first, 11);
        typeof(EntityIndexTable).GetField("epoch", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(map, uint.MaxValue);
        map.Clear(); Check(!map.TryGetValue(first, out _), "epoch wrap clears old generation");
        ExternalEdits(static () => new SkecsStore()); ExternalEdits(static () => new LeoEcsLiteStore());
        ExternalEdits(static () => new LeoEcsClassicStore()); ExternalEdits(static () => new MorpehStore());
        ExternalEdits(static () => new StaticEcsStore());
        Console.WriteLine("PASS: generation-aware snapshot IDs, reuse, growth, epoch wrap, bounded fallback and external/ref/transient edits on five ports.");
    }

    private static void ExternalEdits<T>(Func<T> factory) where T : IEcsStore
    {
        using var battle = new Battle<T>(factory(), new BattleConfig { ShipsPerTeam = 16, Seed = 27 });
        var frame = new BattleFrame(); battle.Capture(frame);
        var ship = frame.Ships[0]; Check(battle.TryFindShip(ship.Serial, out var e), "external fixture ship");
        var position = new Vector2(-250, 275);
        battle.Store.Get<Transform>(e).Position = position;
        battle.Capture(frame);
        Check(frame.Ships.First(row => row.Serial == ship.Serial).Position == position, "ref transform visible to refresh");
        var particle = battle.Store.Create();
        battle.Store.Set(particle, new Transform { Position = position, Previous = position });
        battle.Store.Set(particle, new Motion());
        battle.Store.Set(particle, new Particle { Serial = 100000001, ExpiresTick = 10000, Size = 1 });
        battle.Store.Commit();
        battle.Capture(frame); Check(frame.Particles.Any(row => row.Serial == 100000001), "external creation visible");
        battle.Store.Get<Particle>(particle).Serial = 100000002;
        battle.Step(); battle.Capture(frame, false);
        Check(frame.Particles.Any(row => row.Serial == 100000002) && !frame.Particles.Any(row => row.Serial == 100000001),
            "ref serial edit visible without mutation counter or serial-indexed allocation");
        battle.Store.Remove<Particle>(particle);
        battle.Store.Commit();
        battle.Step(); battle.Capture(frame, false);
        Check(!frame.Particles.Any(row => row.Serial == 100000002), "external membership removal visible");
        battle.Store.Destroy(particle);
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
