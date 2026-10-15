using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void ObservationChecks()
    {
        ObservationReplay(static () => new SkecsStore());
        ObservationReplay(static () => new LeoEcsLiteStore());
        ObservationReplay(static () => new LeoEcsClassicStore());
        ObservationReplay(static () => new MorpehStore());
        ObservationReplay(static () => new StaticEcsStore());
        foreach (var friendly in new[] { false, true })
        {
            using var battle = KernelBattle();
            var owner = OwnerActor(battle, 0, ShipClass.Fighter, Vector2.Zero);
            var victim = OwnerActor(battle, friendly ? 0 : 1, ShipClass.Fighter, new(12, 0));
            var victimSerial = battle.Store.Get<Identity>(victim).Serial;
            battle.ObserveShip(victimSerial);
            var round = OwnerRound(battle, owner, WeaponKind.HeavyCannon, new(-20, 0), new(2400, 0));
            battle.Store.Get<Projectile>(round).Damage = 20000;
            battle.Step();
            Require(!battle.Store.Alive(victim) && battle.ObservedDeath.Serial == victimSerial &&
                battle.ObservedDeath.KillerSerial == battle.Store.Get<Identity>(owner).Serial &&
                battle.ObservedDeath.Position == new Vector2(12, 0), "fatal projectile owner includes friendly fire");
            Require(!battle.TryGetShipObservation(victimSerial, out _), "dead serial has no live view");
            var death = battle.ObservedDeath;
            battle.Step();
            Require(battle.ObservedDeath == death, "notice survives batched ticks and capture until next watch");
            battle.ObserveShip(-1);
            Require(battle.ObservedDeath.Serial == -1, "unwatch clears notice");
        }
        using (var battle = KernelBattle())
        {
            var victim = KernelBattleActor(battle, Vector2.Zero);
            var target = KernelBattleActor(battle, new(20, 0));
            var serial = battle.Store.Get<Identity>(victim).Serial;
            battle.Store.Get<Brain>(victim).Target = target;
            battle.Capture(new BattleFrame());
            Require(battle.TryGetShipObservation(serial, out var view) &&
                view.TargetSerial == battle.Store.Get<Identity>(target).Serial && float.IsFinite(view.Shield), "read-only target and zero-shield view");
            battle.Store.Set(victim, new Docked { Carrier = target });
            Require(battle.TryGetShipObservation(serial, out view) && view.TargetSerial == -1 &&
                view.DockCarrierSerial == battle.Store.Get<Identity>(target).Serial, "docked ship remains alive with carrier anchor and no stale target");
            battle.Store.Remove<Docked>(victim);
            battle.ObserveShip(serial);
            battle.Damage(victim, 10000, false, true);
            Require(battle.ObservedDeath.Serial == serial && battle.ObservedDeath.KillerSerial == -1, "unattributed debris uses fallback");
        }
        Console.WriteLine("PASS: observation checksum/counter neutrality across five stores, exact fatal owner including friendly fire, retained death notice, target and docked views.");
    }

    private static void ObservationReplay<T>(Func<T> factory) where T : IEcsStore
    {
        var config = new BattleConfig { ShipsPerTeam = 16, Seed = 27 };
        var expected = new ulong[30];
        using (var plain = new Battle<T>(factory(), config))
        {
            for (var i = 0; i < 900; i++)
            {
                plain.Step();
                if (i % 30 == 0) expected[i / 30] = plain.Checksum();
            }
        }
        // StaticEcs has a single live-world contract; independent replays must run sequentially.
        using (var watched = new Battle<T>(factory(), config))
        {
            var frame = new BattleFrame();
            for (var i = 0; i < 900; i++)
            {
                if (i % 47 == 0)
                {
                    watched.Capture(frame);
                    watched.ObserveShip(frame.Ships.Count == 0 ? -1 : frame.Ships[i % frame.Ships.Count].Serial);
                }
                watched.Step(); watched.Capture(frame);
                foreach (var ship in frame.Ships) Require(watched.TryGetShipObservation(ship.Serial, out _), "visible observation lookup");
                if (i % 30 == 0) Require(expected[i / 30] == watched.Checksum(), "frontend observation never alters gameplay or replay");
            }
        }
    }
}
