using System.Numerics;
using System.Diagnostics;
using System.Reflection;
using nkast.Aether.Physics2D.Collision;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using PhysicsVector = nkast.Aether.Physics2D.Common.Vector2;

using SpaceBattle;

namespace SpaceBattle.PhysicsStudy;



/// <summary>Single-threaded ship physics; public distances and speeds use game units.</summary>
public sealed class AetherReference : IDisposable
{
    private const float Scale = 0.1f;
    private const float InverseScale = 10f;
    private const float PointQueryEpsilon = 0.0001f;
    private readonly World world;
    private readonly List<ShipBody?> ships = new();
    private readonly Stack<int> freeIds = new();
    private readonly List<CollisionEvent> contacts = new();
    private readonly HashSet<ulong> contactPairs = new();
    private readonly RayCastReportFixtureDelegate rayCallback;
    private readonly QueryReportFixtureDelegate overlapCallback;
    private readonly QueryReportFixtureDelegate candidateCallback;
    private readonly QueryReportFixtureDelegate placementCallback;
    private PhysicsVector placementPoint;
    private float placementRadius, maximumShipRadius;
    private bool placementBlocked;
    private readonly IReadOnlyList<CollisionEvent> contactView;
    private bool disposed;
    private int fixtureCount;
    private int sweepSerial, sweepIgnoredSerial;
    private float sweepFraction;
    private PhysicsVector sweepPoint, sweepStart;
    private bool sweepHasCandidate;
    private long sweepQueries, emptySweeps, rayQueries, pointQueries;
    private long candidates, narrowTests;
    private readonly bool collectTimings, previousDiagnostics;
    private readonly bool diagnosticsEnabled, controlsDiagnostics;
    private static readonly FieldInfo? DiagnosticsField = typeof(nkast.Aether.Physics2D.Settings)
        .GetField("EnableDiagnostics", BindingFlags.Public | BindingFlags.Static);
    private double candidateMs, narrowMs, combinedStepMs;
    public bool CollectTimings => collectTimings;
    public double? ContactCandidatesMs { get; private set; }
    public double? SolverMs { get; private set; }
    public double CombinedStepMs => combinedStepMs;
    public double HitCandidatesMs => candidateMs;
    public double HitNarrowMs => narrowMs;

    public static object InspectDiagnostics() => new
    {
        assembly = typeof(World).Assembly.FullName,
        requiredSwitch = "Settings.EnableDiagnostics",
        enableDiagnostics = DiagnosticsField?.GetValue(null),
        switchIsConstant = DiagnosticsField?.IsLiteral,
        switchIsReadOnly = DiagnosticsField?.IsInitOnly,
        properties = typeof(World).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.EndsWith("Time", StringComparison.Ordinal))
            .Select(p => new { p.Name, type = p.PropertyType.FullName }).ToArray(),
        search = "NewContactsTime + ContactsUpdateTime",
        solver = "SolveUpdateTime + ContinuousPhysicsTime",
        note = "Actual Aether internal timers, not a disjoint exhaustive partition of outer combinedStep."
    };

    private double? ReadDiagnostic(string name)
    {
        if (!diagnosticsEnabled) return null;
        var value = typeof(World).GetProperty(name)?.GetValue(world);
        return value is TimeSpan time ? time.TotalMilliseconds : null;
    }

    private sealed class ShipBody(Body body, int id, int serial, float radius)
    {
        public readonly Body Body = body;
        public readonly int Id = id, Serial = serial;
        public readonly float Radius = radius;
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

    public AetherReference(bool collectTimings = false)
    {
        this.collectTimings = collectTimings;
        previousDiagnostics = DiagnosticsField?.GetValue(null) is true;
        controlsDiagnostics = DiagnosticsField is { IsLiteral: false, IsInitOnly: false };
        if (controlsDiagnostics) DiagnosticsField!.SetValue(null, collectTimings);
        diagnosticsEnabled = controlsDiagnostics ? collectTimings : previousDiagnostics;
        world = new World(PhysicsVector.Zero);
        world.ContactManager.BeginContact += BeginContact;
        rayCallback = RayHit;
        overlapCallback = StartOverlap;
        candidateCallback = SweepCandidate;
        placementCallback = PlacementOverlap;
        contactView = contacts.AsReadOnly();
    }

    /// <summary>Live view of unique begin contacts, ordered by canonical serial pair after each step.</summary>
    public IReadOnlyList<CollisionEvent> Contacts => contactView;
    public int Bodies { get; private set; }
    /// <summary>Includes fixtures owned by disabled (docked) ships.</summary>
    public int Fixtures => fixtureCount;
    public PhysicsQueryStatistics QueryStatistics
    {
        get
        {
            // Preserve the public return type across the main agent's four-to-six counter API extension.
            var constructor = typeof(PhysicsQueryStatistics).GetConstructors()
                .Where(c => c.GetParameters().Length is 4 or 6)
                .OrderByDescending(c => c.GetParameters().Length).First();
            object[] values = constructor.GetParameters().Length == 6
                ? [sweepQueries, emptySweeps, rayQueries, pointQueries, candidates, narrowTests]
                : [sweepQueries, emptySweeps, rayQueries, pointQueries];
            return (PhysicsQueryStatistics)constructor.Invoke(values);
        }
    }
    public ReferenceQueryStatistics ReferenceStatistics => new(sweepQueries, emptySweeps, rayQueries, pointQueries, candidates, narrowTests);
    public readonly record struct ReferenceQueryStatistics(long Sweeps, long EmptySweeps, long Rays, long PointQueries,
        long Candidates, long NarrowTests);

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
        var ship = new ShipBody(body, id, serial, collider.Radius * Scale);
        maximumShipRadius = Math.Max(maximumShipRadius, ship.Radius);
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
        sweepQueries = emptySweeps = rayQueries = pointQueries = 0;
        candidates = narrowTests = 0;
        candidateMs = narrowMs = combinedStepMs = 0;
        ContactCandidatesMs = SolverMs = null;
        var start = collectTimings ? Stopwatch.GetTimestamp() : 0;
        if (dt > 0) world.Step(dt);
        if (collectTimings)
        {
            combinedStepMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (dt > 0)
            {
                ContactCandidatesMs = ReadDiagnostic("NewContactsTime") + ReadDiagnostic("ContactsUpdateTime");
                SolverMs = ReadDiagnostic("SolveUpdateTime") + ReadDiagnostic("ContinuousPhysicsTime");
            }
        }
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
        => SweepCore(from, to, radius, true, -1, out targetSerial, out impact);

    /// <summary>Ignores every fixture of one ship, without occluding other swept hits.</summary>
    public bool Sweep(Vector2 from, Vector2 to, float radius, int ignoredSerial,
        out int targetSerial, out Vector2 impact)
        => SweepCore(from, to, radius, true, ignoredSerial, out targetSerial, out impact);

    // Reference path for differential tests; skips only the empty broadphase rejection.
    internal bool SweepUnfiltered(Vector2 from, Vector2 to, float radius,
        out int targetSerial, out Vector2 impact, int ignoredSerial = -1)
        => SweepCore(from, to, radius, false, ignoredSerial, out targetSerial, out impact);

    private bool SweepCore(Vector2 from, Vector2 to, float radius, bool rejectEmpty, int ignoredSerial,
        out int targetSerial, out Vector2 impact)
    {
        ThrowIfDisposed();
        if (!Finite(from) || !Finite(to) || !float.IsFinite(radius) || radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        sweepSerial = -1;
        sweepIgnoredSerial = ignoredSerial;
        sweepFraction = float.PositiveInfinity;
        sweepPoint = ToPhysics(to);
        sweepQueries++;
        var start = ToPhysics(from);
        var end = ToPhysics(to);
        var delta = to - from;
        var hasOffset = radius > 0 && delta.LengthSquared() > 0;
        var offset = hasOffset ? ToPhysics(Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * radius) : PhysicsVector.Zero;
        var candidateStart = collectTimings ? Stopwatch.GetTimestamp() : 0;
        var hasCandidate = !rejectEmpty || HasSweepCandidate(start, end, offset, hasOffset);
        if (collectTimings) candidateMs += Stopwatch.GetElapsedTime(candidateStart).TotalMilliseconds;
        if (!hasCandidate)
        {
            emptySweeps++;
            targetSerial = -1;
            impact = FromPhysics(sweepPoint);
            return false;
        }
        var narrowStart = collectTimings ? Stopwatch.GetTimestamp() : 0;
        Cast(start, end);
        if (hasOffset)
        {
            Cast(start + offset, end + offset);
            Cast(start - offset, end - offset);
            offset *= 0.5f;
            Cast(start + offset, end + offset);
            Cast(start - offset, end - offset);
        }
        if (collectTimings) narrowMs += Stopwatch.GetElapsedTime(narrowStart).TotalMilliseconds;
        targetSerial = sweepSerial;
        impact = FromPhysics(sweepPoint);
        return float.IsFinite(sweepFraction);
    }

    private bool HasSweepCandidate(PhysicsVector start, PhysicsVector end, PhysicsVector offset, bool hasOffset)
    {
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) return true;
        var bounds = new AABB { LowerBound = start, UpperBound = start };
        IncludeEndpoints(ref bounds, start, end);
        if (hasOffset)
        {
            IncludeEndpoints(ref bounds, start + offset, end + offset);
            IncludeEndpoints(ref bounds, start - offset, end - offset);
            offset *= 0.5f;
            IncludeEndpoints(ref bounds, start + offset, end + offset);
            IncludeEndpoints(ref bounds, start - offset, end - offset);
        }
        // Enclose actual rounded endpoints and all five origin queries, not an idealized disk.
        var magnitude = MathF.Max(1, MathF.Max(MathF.Max(MathF.Abs(bounds.LowerBound.X), MathF.Abs(bounds.LowerBound.Y)),
            MathF.Max(MathF.Abs(bounds.UpperBound.X), MathF.Abs(bounds.UpperBound.Y))));
        var padding = PointQueryEpsilon + magnitude * 0.000001f;
        bounds.LowerBound = new(MathF.BitDecrement(bounds.LowerBound.X - padding), MathF.BitDecrement(bounds.LowerBound.Y - padding));
        bounds.UpperBound = new(MathF.BitIncrement(bounds.UpperBound.X + padding), MathF.BitIncrement(bounds.UpperBound.Y + padding));
        if (!float.IsFinite(bounds.LowerBound.X) || !float.IsFinite(bounds.LowerBound.Y) ||
            !float.IsFinite(bounds.UpperBound.X) || !float.IsFinite(bounds.UpperBound.Y)) return true;
        sweepHasCandidate = false;
        world.QueryAABB(candidateCallback, ref bounds);
        return sweepHasCandidate;
    }

    private static void IncludeEndpoints(ref AABB bounds, PhysicsVector start, PhysicsVector end)
    {
        bounds.LowerBound = new(MathF.Min(bounds.LowerBound.X, MathF.Min(start.X, end.X)),
            MathF.Min(bounds.LowerBound.Y, MathF.Min(start.Y, end.Y)));
        bounds.UpperBound = new(MathF.Max(bounds.UpperBound.X, MathF.Max(start.X, end.X)),
            MathF.Max(bounds.UpperBound.Y, MathF.Max(start.Y, end.Y)));
    }

    private bool SweepCandidate(Fixture fixture)
    {
        candidates++;
        if (!Eligible(fixture, out _)) return true;
        sweepHasCandidate = true;
        return false;
    }

    private void Cast(PhysicsVector from, PhysicsVector to)
    {
        // RayCast ignores fixtures containing its origin, so query that point first.
        sweepStart = from;
        var epsilon = new PhysicsVector(PointQueryEpsilon, PointQueryEpsilon);
        var bounds = new AABB { LowerBound = from - epsilon, UpperBound = from + epsilon };
        pointQueries++;
        world.QueryAABB(overlapCallback, ref bounds);
        if (from != to) { rayQueries++; world.RayCast(rayCallback, from, to); }
    }

    public bool CanSpawn(Vector2 position, float radius)
    {
        ThrowIfDisposed();
        if (!Finite(position) || !float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        placementPoint = ToPhysics(position); placementRadius = radius * Scale; placementBlocked = false;
        var extent = new PhysicsVector(placementRadius + maximumShipRadius, placementRadius + maximumShipRadius);
        var bounds = new AABB { LowerBound = placementPoint - extent, UpperBound = placementPoint + extent };
        world.QueryAABB(placementCallback, ref bounds);
        return !placementBlocked;
    }

    private bool PlacementOverlap(Fixture fixture)
    {
        if (fixture.Body.Tag is not ShipBody ship || !ship.Body.Enabled) return true;
        var delta = ship.Body.Position - placementPoint;
        var distance = placementRadius + ship.Radius;
        if (delta.LengthSquared() >= distance * distance) return true;
        placementBlocked = true; return false;
    }

    private bool Eligible(Fixture fixture, out ShipBody ship)
    {
        ship = fixture.Body.Tag as ShipBody ?? null!;
        return ship is not null && ship.Body.Enabled && ship.Serial != sweepIgnoredSerial;
    }

    private bool StartOverlap(Fixture fixture)
    {
        if (!Eligible(fixture, out var ship)) return true;
        narrowTests++;
        if (fixture.TestPoint(ref sweepStart))
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
        if (controlsDiagnostics) DiagnosticsField!.SetValue(null, previousDiagnostics);
    }
}
