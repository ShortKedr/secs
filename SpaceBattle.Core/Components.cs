using System.Numerics;
using System.Runtime.CompilerServices;

namespace SpaceBattle;

public enum ShipClass { Interceptor, Fighter, Bomber, Gunship, Corvette, Frigate, Destroyer, Support, LightCarrier, FleetCarrier, FoundryCarrier }
public enum WeaponKind { MachineGun, Cannon, Plasma, Missile, HeavyCannon }
public enum ProjectileShape { Disk, Rectangle }
public enum ItemKind { Empty, ShieldCell, HeatSink, RepairKit }
public enum BrainMode { Attack, Retreat, Docking, Patrol, Screen }

public struct Identity { public int Serial, Team; public ShipClass Class; }
public struct Transform { public Vector2 Position, Previous; public float Angle; }
public struct Motion { public Vector2 Velocity, Acceleration; public float AngularVelocity, MaxSpeed, Thrust, Damping; }
public struct Hull { public float Current, Maximum, Armor; }
public struct Shield { public float Current, Maximum, RegenPerSecond; public int LastHitTick, RegenDelayTicks; }
public struct Heat { public float Value, Limit, CoolingPerSecond; }
public struct Brain
{
    public EntityKey Target, Home, SquadLeader, CoverAlly;
    public BrainMode Mode;
    public int NextDecisionTick, TargetLockUntilTick, CoverUntilTick, SquadSize;
    public float OrbitSign, EstimatedKillSeconds;
}
public struct Weapon { public WeaponKind Kind; public int Cooldown, NextFireTick, Burst; public float Range, Damage, Speed, HeatPerShot; }
public struct SecondaryWeapon { public Weapon Value; }
public struct MountedTurret
{
    public int Serial, Slot, NextDecisionTick, LastFireTick;
    public EntityKey Parent, Target;
    public Vector2 LocalOffset;
}
public struct Collider { public float Radius, Mass; public int ShapeCount; }
public struct PhysicsLink { public int Body; }
public struct CombatState { public int LastDamageTick; public EntityKey LastAttacker; }
public struct Carrier { public int NextBuildTick, BuildInterval, Capacity, Built, RepairSlots; }
public struct Docked { public EntityKey Carrier; public int ReleaseTick; }
public struct Overheated { public int SinceTick; }
public struct InCombat { public int UntilTick; }
public struct Boosting { public int UntilTick; }
public struct ShieldBroken { public int SinceTick; }
public struct Projectile { public int Serial; public EntityKey Owner, Target; public WeaponKind Kind; public float Damage, Radius, HalfLength; public int ExpiresTick; public ProjectileShape Shape; }
public struct Debris { public int Serial, ExpiresTick; public float Damage, Radius; }
public struct Particle { public int Serial, ExpiresTick, Kind, StartTick; public float Size; public Vector4 Color; }
public struct Lifetime { public int EndTick; }
public struct InventoryItem { public ItemKind Kind; public int Count; }
[InlineArray(16)] public struct InventoryItems { private InventoryItem element; }
public struct Inventory { public int Capacity, NextUseTick; public InventoryItems Items; }

public static class ShipCatalog
{
    public static bool IsCarrier(ShipClass kind) => kind >= ShipClass.LightCarrier;
    public static bool IsWeaponsPlatform(ShipClass kind) => kind is ShipClass.Frigate or ShipClass.Destroyer || IsCarrier(kind);
    public static int TurretCount(ShipClass kind) => kind switch
    {
        ShipClass.Frigate or ShipClass.Destroyer => 2,
        ShipClass.LightCarrier => 4, ShipClass.FleetCarrier => 6, ShipClass.FoundryCarrier => 8, _ => 0
    };
    public static Weapon TurretWeapon(ShipClass kind, int slot)
    {
        var weapon = Primary(slot % 2 == 0 ? ShipClass.Fighter : ShipClass.Bomber);
        weapon.Damage *= 0.25f;
        weapon.Cooldown *= 2;
        weapon.Range = IsCarrier(kind) ? 150 : 120;
        weapon.HeatPerShot *= 0.5f;
        return weapon;
    }
    public static float Radius(ShipClass kind) => kind switch
    {
        ShipClass.Interceptor => 1.0f, ShipClass.Fighter => 1.3f, ShipClass.Bomber => 1.7f,
        ShipClass.Gunship => 2, ShipClass.Corvette => 2.5f, ShipClass.Frigate => 3.2f,
        ShipClass.Destroyer => 4.2f, ShipClass.Support => 2.8f,
        ShipClass.LightCarrier => 8, ShipClass.FleetCarrier => 10, _ => 12
    };
    public static int Shapes(ShipClass kind) => 1;
    public static Weapon Primary(ShipClass kind) => IsWeaponsPlatform(kind) ? new()
    {
        Kind = WeaponKind.HeavyCannon, Burst = 1, Speed = 170,
        Cooldown = kind == ShipClass.Frigate ? 720 : kind == ShipClass.Destroyer ? 840 : 1080 + ((int)kind - 8) * 240,
        Damage = kind == ShipClass.Frigate ? 60 : kind == ShipClass.Destroyer ? 80 : 480 + ((int)kind - 8) * 160,
        Range = IsCarrier(kind) ? 220 : 170, HeatPerShot = IsCarrier(kind) ? 40 : 25
    } : ((int)kind % 4) switch
    {
        0 => new() { Kind = WeaponKind.MachineGun, Cooldown = 5, Burst = 2, Range = 80, Damage = 2.5f, Speed = 130, HeatPerShot = 1.6f },
        1 => new() { Kind = WeaponKind.Cannon, Cooldown = 42, Burst = 1, Range = 110, Damage = 28, Speed = 100, HeatPerShot = 13 },
        2 => new() { Kind = WeaponKind.Plasma, Cooldown = 18, Burst = 1, Range = 85, Damage = 13, Speed = 75, HeatPerShot = 7 },
        _ => new() { Kind = WeaponKind.Missile, Cooldown = 90, Burst = 2, Range = 140, Damage = 32, Speed = 65, HeatPerShot = 9 }
    };
}
