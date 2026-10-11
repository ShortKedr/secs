using System.Numerics;
using nkast.Aether.Physics2D.Collision;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using PhysicsVector = nkast.Aether.Physics2D.Common.Vector2;

namespace SpaceBattle;

public readonly record struct CollisionEvent(int FirstSerial, int SecondSerial, float RelativeSpeed, Vector2 Position);

/// <summary>Single-threaded ship physics; public distances and speeds use game units.</summary>
public sealed class PhysicsService : IDisposable
{
    private const float Scale = 0.1f;
    private const float InverseScale = 10f;
    private readonly World world;
    private readonly List<ShipBody?> ships = new();
    private readonly Stack<int> freeIds = new();
    private readonly List<CollisionEvent> contacts = new();
    private readonly HashSet<ulong> contactPairs = new();
    private readonly RayCastReportFixtureDelegate rayCallback;
    private readonly QueryReportFixtureDelegate overlapCallback;
    private readonly IReadOnlyList<CollisionEvent> contactView;
    private bool disposed;
    private int fixtureCount;
    private int sweepSerial;
    private float sweepFraction;
    private PhysicsVector sweepPoint, sweepStart;

    private sealed class ShipBody(Body body, int id, int serial)
    {
        public readonly Body Body = body;
        public readonly int Id = id, Serial = serial;
    }

    private sealed class ContactComparer : IComparer<CollisionEvent>
    {
        public static readonly ContactComparer Instance = new();
        public int Compare(CollisionEvent first, CollisionEvent second)
        {
            int order = first.FirstSerial.CompareTo(second.FirstSerial);
            return order != 0 ? order : first.SecondSerial.CompareTo(second.SecondSerial);
        }
    }

    public PhysicsService()
    {
        world = new World(PhysicsVector.Zero);
        world.ContactManager.BeginContact += BeginContact;
        rayCallback = RayHit;
        overlapCallback = StartOverlap;
        contactView = contacts.AsReadOnly();
    }

    /// <summary>Live view of unique begin contacts, ordered by canonical serial pair after each step.</summary>
    public IReadOnlyList<CollisionEvent> Contacts => contactView;
    public int Bodies { get; private set; }
    /// <summary>Includes fixtures owned by disabled (docked) ships.</summary>
    public int Fixtures => fixtureCount;

    /// <summary>
    /// Returns a reusable body slot; callers must discard its handle when removing the ship.
    /// Bodies and fixtures are created in call order, so callers should use canonical spawn order.
    /// </summary>
    public int AddShip(int serial, Transform transform, Collider collider)
    {
        ThrowIfDisposed();
        ValidateTransform(in transform);
        if (!float.IsFinite(collider.Radius) || collider.Radius <= 0 ||
            !float.IsFinite(collider.Mass) || collider.Mass <= 0)
            throw new ArgumentOutOfRangeException(nameof(collider));

        var body = world.CreateBody(ToPhysics(transform.Position), transform.Angle, BodyType.Dynamic);
        body.LinearDamping = 0;
        body.AngularDamping = 0;
        var id = freeIds.Count > 0 ? freeIds.Peek() : ships.Count;
        var ship = new ShipBody(body, id, serial);
        body.Tag = ship;
        float r = collider.Radius * Scale;
        int count = collider.ShapeCount <= 1 ? 1 : Math.Clamp(collider.ShapeCount, 4, 6);
        if (count == 1)
        {
            body.CreateCircle(r, collider.Mass / (MathF.PI * r * r));
        }
        else
        {
            // Fuselage, two wings, rounded core, then optional bow/stern caps.
            // Aether sums fixture masses, including overlapping fixture area.
            float area = r * r * (1.6f * 0.55f + 2 * 0.75f * 0.42f + MathF.PI * 0.7f * 0.7f
                + (count - 4) * MathF.PI * 0.23f * 0.23f);
            float density = collider.Mass / area;
            body.CreateRectangle(1.6f * r, 0.55f * r, density, PhysicsVector.Zero);
            body.CreateRectangle(0.75f * r, 0.42f * r, density, new PhysicsVector(-0.15f * r, 0.48f * r));
            body.CreateRectangle(0.75f * r, 0.42f * r, density, new PhysicsVector(-0.15f * r, -0.48f * r));
            body.CreateCircle(0.7f * r, density);
            if (count >= 5) body.CreateCircle(0.23f * r, density, new PhysicsVector(0.72f * r, 0));
            if (count >= 6) body.CreateCircle(0.23f * r, density, new PhysicsVector(-0.72f * r, 0));
        }
        foreach (var fixture in body.FixtureList)
        {
            fixture.Friction = 0;
            fixture.Restitution = 0.1f;
        }
        if (id == ships.Count) ships.Add(ship);
        else
        {
            freeIds.Pop();
            ships[id] = ship;
        }
        Bodies++;
        fixtureCount += count;
        return id;
    }

    public void RemoveShip(int body)
    {
        ThrowIfDisposed();
        if ((uint)body >= (uint)ships.Count) throw new ArgumentOutOfRangeException(nameof(body));
        var ship = ships[body];
        if (ship is null) return;
        fixtureCount -= ship.Body.FixtureList.Count;
        world.Remove(ship.Body);
        ship.Body.Tag = null;
        ships[body] = null;
        freeIds.Push(body);
        Bodies--;
    }

    public void SetShip(int body, in Transform transform, in Motion motion, bool enabled)
    {
        var ship = GetBody(body);
        ValidateTransform(in transform);
        if (!Finite(motion.Velocity) || !float.IsFinite(motion.AngularVelocity))
            throw new ArgumentOutOfRangeException(nameof(motion));
        if (!enabled) ship.Enabled = false;
        var position = ToPhysics(transform.Position);
        if (ship.Position != position || ship.Rotation != transform.Angle)
            ship.SetTransform(position, transform.Angle);
        ship.LinearVelocity = ToPhysics(motion.Velocity);
        ship.AngularVelocity = motion.AngularVelocity;
        ship.Enabled = enabled;
        if (enabled) ship.Awake = true;
    }

    public (Vector2 Position, float Angle, Vector2 Velocity, float AngularVelocity) GetShip(int body)
    {
        var ship = GetBody(body);
        return (FromPhysics(ship.Position), ship.Rotation, FromPhysics(ship.LinearVelocity), ship.AngularVelocity);
    }

    public void Step(float dt)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        contacts.Clear();
        contactPairs.Clear();
        if (dt > 0) world.Step(dt);
        contacts.Sort(ContactComparer.Instance);
    }

    /// <summary>
    /// Approximate projectile CCD using a center ray and four parallel rays at
    /// +/- radius and +/- half radius. Not an exact swept disk: grazing contacts
    /// between rays, rounded end caps, and target motion during a step can be missed.
    /// Impact is a point on the target fixture, not the projectile center.
    /// </summary>
    public bool Sweep(Vector2 from, Vector2 to, float radius,
        out int targetSerial, out Vector2 impact)
    {
        ThrowIfDisposed();
        if (!Finite(from) || !Finite(to) || !float.IsFinite(radius) || radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        sweepSerial = -1;
        sweepFraction = float.PositiveInfinity;
        sweepPoint = ToPhysics(to);
        var start = ToPhysics(from);
        var end = ToPhysics(to);
        Cast(start, end);
        var delta = to - from;
        if (radius > 0 && delta.LengthSquared() > 0)
        {
            var offset = ToPhysics(Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * radius);
            Cast(start + offset, end + offset);
            Cast(start - offset, end - offset);
            offset *= 0.5f;
            Cast(start + offset, end + offset);
            Cast(start - offset, end - offset);
        }
        targetSerial = sweepSerial;
        impact = FromPhysics(sweepPoint);
        return float.IsFinite(sweepFraction);
    }

    private void Cast(PhysicsVector from, PhysicsVector to)
    {
        // RayCast ignores fixtures containing its origin, so query that point first.
        sweepStart = from;
        var epsilon = new PhysicsVector(0.0001f, 0.0001f);
        var bounds = new AABB { LowerBound = from - epsilon, UpperBound = from + epsilon };
        world.QueryAABB(overlapCallback, ref bounds);
        if (from != to) world.RayCast(rayCallback, from, to);
    }

    private bool Eligible(Fixture fixture, out ShipBody ship)
    {
        ship = fixture.Body.Tag as ShipBody ?? null!;
        return ship is not null && ship.Body.Enabled;
    }

    private bool StartOverlap(Fixture fixture)
    {
        if (Eligible(fixture, out var ship) && fixture.TestPoint(ref sweepStart))
            SelectHit(ship, sweepStart, 0);
        return true;
    }

    private float RayHit(Fixture fixture, PhysicsVector point, PhysicsVector normal, float fraction)
    {
        if (!Eligible(fixture, out var ship)) return -1;
        SelectHit(ship, point, fraction);
        // Keep equal-fraction hits visible for a stable serial tie break.
        return MathF.Min(1, sweepFraction + 0.000001f);
    }

    private void SelectHit(ShipBody ship, PhysicsVector point, float fraction)
    {
        if (fraction > sweepFraction || (fraction == sweepFraction && ship.Serial >= sweepSerial)) return;
        sweepFraction = fraction;
        sweepSerial = ship.Serial;
        sweepPoint = point;
    }

    private bool BeginContact(Contact contact)
    {
        if (contact.FixtureA.Body.Tag is not ShipBody first || contact.FixtureB.Body.Tag is not ShipBody second)
            return true;
        if (first.Id > second.Id) (first, second) = (second, first);
        ulong pair = ((ulong)(uint)first.Id << 32) | (uint)second.Id;
        if (!contactPairs.Add(pair)) return true;
        if (first.Serial > second.Serial) (first, second) = (second, first);
        contact.GetWorldManifold(out _, out var points);
        var point = points[0];
        var relative = first.Body.GetLinearVelocityFromWorldPoint(point) - second.Body.GetLinearVelocityFromWorldPoint(point);
        float speed = MathF.Sqrt(relative.X * relative.X + relative.Y * relative.Y) * InverseScale;
        contacts.Add(new CollisionEvent(first.Serial, second.Serial, speed, FromPhysics(point)));
        return true;
    }

    private Body GetBody(int id)
    {
        ThrowIfDisposed();
        if ((uint)id >= (uint)ships.Count || ships[id] is not { } ship)
            throw new ArgumentOutOfRangeException(nameof(id), "Ship body does not exist.");
        return ship.Body;
    }

    private static PhysicsVector ToPhysics(Vector2 value) => new(value.X * Scale, value.Y * Scale);
    private static Vector2 FromPhysics(PhysicsVector value) => new(value.X * InverseScale, value.Y * InverseScale);
    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static void ValidateTransform(in Transform transform)
    {
        if (!Finite(transform.Position) || !float.IsFinite(transform.Angle))
            throw new ArgumentOutOfRangeException(nameof(transform));
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose()
    {
        if (disposed) return;
        world.ContactManager.BeginContact -= BeginContact;
        world.Clear();
        ships.Clear();
        freeIds.Clear();
        contacts.Clear();
        contactPairs.Clear();
        Bodies = fixtureCount = 0;
        disposed = true;
    }
}
