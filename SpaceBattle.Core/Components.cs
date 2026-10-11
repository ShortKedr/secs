using System.Numerics;
using System.Runtime.CompilerServices;

namespace SpaceBattle;

public enum ShipClass { Interceptor, Fighter, Bomber, Gunship, Corvette, Frigate, Destroyer, Support, LightCarrier, FleetCarrier, FoundryCarrier }
public enum WeaponKind { MachineGun, Cannon, Plasma, Missile }
public enum ItemKind { Empty, ShieldCell, HeatSink, RepairKit }
public enum BrainMode { Attack, Retreat, Docking, Patrol }

public struct Identity { public int Serial, Team; public ShipClass Class; }
public struct Transform { public Vector2 Position, Previous; public float Angle; }
public struct Motion { public Vector2 Velocity, Acceleration; public float AngularVelocity, MaxSpeed, Thrust, Damping; }
public struct Hull { public float Current, Maximum, Armor; }
public struct Shield { public float Current, Maximum, RegenPerSecond; public int LastHitTick, RegenDelayTicks; }
public struct Heat { public float Value, Limit, CoolingPerSecond; }
public struct Brain { public EntityKey Target, Home; public BrainMode Mode; public int NextDecisionTick; public float OrbitSign; }
public struct Weapon { public WeaponKind Kind; public int Cooldown, NextFireTick, Burst; public float Range, Damage, Speed, HeatPerShot; }
public struct SecondaryWeapon { public Weapon Value; }
public struct Collider { public float Radius, Mass; public int ShapeCount; }
public struct PhysicsLink { public int Body; }
public struct CombatState { public int LastDamageTick; }
public struct Carrier { public int NextBuildTick, BuildInterval, Capacity, Built, RepairSlots; }
public struct Docked { public EntityKey Carrier; public int ReleaseTick; }
public struct Overheated { public int SinceTick; }
public struct InCombat { public int UntilTick; }
public struct Boosting { public int UntilTick; }
public struct ShieldBroken { public int SinceTick; }
public struct Projectile { public int Serial; public EntityKey Owner, Target; public WeaponKind Kind; public float Damage, Radius; public int ExpiresTick; }
public struct Debris { public int Serial, ExpiresTick; public float Damage, Radius; }
public struct Particle { public int Serial, ExpiresTick, Kind; public float Size; public Vector4 Color; }
public struct Lifetime { public int EndTick; }
public struct InventoryItem { public ItemKind Kind; public int Count; }
[InlineArray(16)] public struct InventoryItems { private InventoryItem element; }
public struct Inventory { public int Capacity, NextUseTick; public InventoryItems Items; }

public static class ShipCatalog
{
    public static bool IsCarrier(ShipClass kind) => kind >= ShipClass.LightCarrier;
    public static float Radius(ShipClass kind) => kind switch
    {
        ShipClass.Interceptor => 1.0f, ShipClass.Fighter => 1.3f, ShipClass.Bomber => 1.7f,
        ShipClass.Gunship => 2, ShipClass.Corvette => 2.5f, ShipClass.Frigate => 3.2f,
        ShipClass.Destroyer => 4.2f, ShipClass.Support => 2.8f,
        ShipClass.LightCarrier => 8, ShipClass.FleetCarrier => 10, _ => 12
    };
    public static int Shapes(ShipClass kind) => kind <= ShipClass.Fighter ? 1 : 4 + (int)kind % 3;
    public static Weapon Primary(ShipClass kind) => ((int)kind % 4) switch
    {
        0 => new() { Kind = WeaponKind.MachineGun, Cooldown = 5, Burst = 2, Range = 80, Damage = 2.5f, Speed = 130, HeatPerShot = 1.6f },
        1 => new() { Kind = WeaponKind.Cannon, Cooldown = 42, Burst = 1, Range = 110, Damage = 28, Speed = 100, HeatPerShot = 13 },
        2 => new() { Kind = WeaponKind.Plasma, Cooldown = 18, Burst = 1, Range = 85, Damage = 13, Speed = 75, HeatPerShot = 7 },
        _ => new() { Kind = WeaponKind.Missile, Cooldown = 90, Burst = 2, Range = 140, Damage = 32, Speed = 65, HeatPerShot = 9 }
    };
}
