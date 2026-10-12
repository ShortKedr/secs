using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpaceBattle;

public readonly record struct CollisionEvent(int FirstSerial, int SecondSerial, float RelativeSpeed, Vector2 Position);

/// <summary>Circle ship dynamics and analytic projectile CCD in game units, shared by every host.</summary>
public sealed class PhysicsService : IDisposable
{
    private sealed class ShipBody(int serial, Transform transform, float radius, float mass, float inertia)
    {
        public readonly int Serial = serial;
        public readonly float Radius = radius, Mass = mass, Inertia = inertia;
        public readonly float InverseMass = 1 / mass, InverseInertia = 1 / inertia;
        public Vector2 Position = transform.Position, Velocity;
        public float Angle = transform.Angle, AngularVelocity;
        public bool Enabled = true;
        public readonly Vector2[] Path = new Vector2[9];
        public int PathSegments;
        public double MinX, MinY, MaxX, MaxY, FatMinX, FatMinY, FatMaxX, FatMaxY;
    }
    private struct Constraint
    {
        public int First, Second;
        public Vector2 Normal, FirstArm, SecondArm;
        public float Bias, NormalImpulse, TangentImpulse;
        public Vector2 Tangent;
        public float NormalMass;
        public double TangentMass;
    }
    private readonly record struct CandidatePair(int First, int Second);
    private sealed class SerialComparer(PhysicsService owner) : IComparer<int>
    {
        public int Compare(int a, int b) => owner.ships[a]!.Serial.CompareTo(owner.ships[b]!.Serial);
    }
    private readonly List<ShipBody?> ships = new();
    private readonly Stack<int> freeIds = new();
    private readonly List<int> active = new(), candidates = new();
    private readonly List<Constraint> constraints = new();
    private readonly SpatialBoundsIndex grid;
    private readonly List<CollisionEvent> contacts = new();
    private readonly IReadOnlyList<CollisionEvent> contactView;
    private readonly SerialComparer comparer;
    private readonly Comparison<int> compareSerial;
    private readonly HashSet<int> liveSerials = new();
    private HashSet<ulong> previousPairs = new(), currentPairs = new();
    private readonly HashSet<ulong> constraintPairs = new();
    private bool disposed, membershipDirty = true, gridDirty = true;
    private int sweepSegments = 1;
    private long sweeps, emptySweeps, hitCandidates, narrowTests;
    private double contactCandidatesMs, solverMs, hitCandidatesMs, hitNarrowMs;
    private PhysicsWorkStatistics diagnostics;
    private CandidatePair[] potentialPairs = Array.Empty<CandidatePair>();
    private int potentialCount;
    private bool candidateCacheValid, candidateFallback;

    public PhysicsSettings Settings { get; }
    public PhysicsService(PhysicsSettings? settings = null)
    {
        Settings = settings ?? new(); Settings.Validate();
        grid = new(cellSize: Settings.ContactCellSize, denseIds: true);
        grid.CollectDiagnostics = Settings.CollectDiagnostics;
        grid.CollectTimings = Settings.CollectTimings;
        comparer = new(this); compareSerial = comparer.Compare; contactView = contacts.AsReadOnly();
    }
    public IReadOnlyList<CollisionEvent> Contacts => contactView;
    public int Bodies { get; private set; }
    public int Fixtures => Bodies;
    public PhysicsQueryStatistics QueryStatistics => new(sweeps, emptySweeps, 0, 0, hitCandidates, narrowTests);
    public PhysicsTimingStatistics Timings => new(contactCandidatesMs, solverMs, hitCandidatesMs, hitNarrowMs);
    public PhysicsDiagnosticStatistics Diagnostics => new(diagnostics, grid.Diagnostics);

    public int AddShip(int serial, Transform transform, Collider collider)
    {
        ThrowIfDisposed(); ValidateTransform(transform);
        var inertia = (float)(0.5 * collider.Mass * collider.Radius * collider.Radius);
        if (!float.IsFinite(collider.Radius) || collider.Radius <= 0 || !float.IsFinite(collider.Mass) || collider.Mass <= 0 ||
            !float.IsFinite(inertia) || inertia <= 0 || !float.IsFinite(1 / collider.Mass) || !float.IsFinite(1 / inertia))
            throw new ArgumentOutOfRangeException(nameof(collider));
        if (!liveSerials.Add(serial)) throw new ArgumentException("Duplicate live ship serial.", nameof(serial));
        var body = new ShipBody(serial, transform, collider.Radius, collider.Mass, inertia);
        body.Path[0] = transform.Position;
        var id = freeIds.Count > 0 ? freeIds.Pop() : ships.Count;
        if (id == ships.Count) ships.Add(body); else ships[id] = body;
        Bodies++; membershipDirty = gridDirty = true;
        grid.Reserve(ships.Count);
        active.EnsureCapacity(ships.Count); candidates.EnsureCapacity(ships.Count);
        var pairCapacity = (int)Math.Min((long)ships.Count * (ships.Count - 1) / 2, Settings.MaximumContactPairs);
        constraints.EnsureCapacity(pairCapacity); contacts.EnsureCapacity(pairCapacity);
        previousPairs.EnsureCapacity(pairCapacity); currentPairs.EnsureCapacity(pairCapacity);
        constraintPairs.EnsureCapacity(pairCapacity);
        var candidateCapacity = (int)Math.Min((long)ships.Count * (ships.Count - 1) / 2, Settings.MaximumCandidatePairs);
        if (candidateCapacity > potentialPairs.Length)
            Array.Resize(ref potentialPairs, (int)Math.Min(Settings.MaximumCandidatePairs,
                Math.Max(candidateCapacity, Math.Max(16L, (long)potentialPairs.Length * 2))));
        return id;
    }
    public void RemoveShip(int body)
    {
        ThrowIfDisposed();
        if ((uint)body >= (uint)ships.Count) throw new ArgumentOutOfRangeException(nameof(body));
        if (ships[body] is not { } ship) return;
        liveSerials.Remove(ship.Serial); ships[body] = null; freeIds.Push(body); Bodies--;
        membershipDirty = gridDirty = true;
    }
    public void SetShip(int body, in Transform transform, in Motion motion, bool enabled)
    {
        var ship = GetBody(body); ValidateTransform(transform);
        if (!Finite(motion.Velocity) || !float.IsFinite(motion.AngularVelocity))
            throw new ArgumentOutOfRangeException(nameof(motion));
        ship.Position = transform.Position; ship.Angle = transform.Angle;
        ship.Velocity = motion.Velocity; ship.AngularVelocity = motion.AngularVelocity; ship.Enabled = enabled;
        ship.Path[0] = ship.Position; ship.PathSegments = 0;
        gridDirty = true;
    }
    public (Vector2 Position, float Angle, Vector2 Velocity, float AngularVelocity) GetShip(int body)
    {
        var ship = GetBody(body);
        return (ship.Position, ship.Angle, ship.Velocity, ship.AngularVelocity);
    }
    public (float Mass, float Inertia) GetMassProperties(int body)
    {
        var ship = GetBody(body); return (ship.Mass, ship.Inertia);
    }
    public void Step(float dt)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        contacts.Clear(); sweeps = emptySweeps = hitCandidates = narrowTests = 0;
        contactCandidatesMs = solverMs = hitCandidatesMs = hitNarrowMs = 0;
        diagnostics = default; grid.ResetDiagnostics();
        EnsureActive();
        float travel = 0;
        foreach (var id in active)
        {
            var body = ships[id]!;
            body.Path[0] = body.Position; body.PathSegments = 0;
            if (body.Enabled) travel = Math.Max(travel, body.Velocity.Length() * dt / body.Radius);
        }
        if (dt == 0) { sweepSegments = 1; gridDirty = true; return; }
        (previousPairs, currentPairs) = (currentPairs, previousPairs); currentPairs.Clear();
        var substeps = (int)Math.Clamp(Math.Ceiling(travel * 2.0), 1, Settings.MaximumSubsteps);
        sweepSegments = substeps;
        if (Settings.CollectDiagnostics) diagnostics.Substeps = substeps;
        var h = dt / substeps;
        for (var phase = 0; phase < substeps; phase++)
        {
            foreach (var id in active)
            {
                var body = ships[id]!;
                if (!body.Enabled) continue;
                var position = body.Position + body.Velocity * h;
                var angle = body.Angle + body.AngularVelocity * h;
                if (!Finite(position) || !float.IsFinite(angle)) throw new InvalidOperationException("Non-finite integration result.");
                body.Position = position; body.Angle = angle;
            }
            constraints.Clear(); constraintPairs.Clear();
            candidateCacheValid = candidateFallback = false;
            BuildContacts();
            SolveVelocities(0);
            for (var iteration = 0; iteration < Settings.PositionIterations; iteration++)
            {
                var start = Stamp();
                if (Settings.CollectDiagnostics) diagnostics.PositionIterations++;
                var moved = CorrectPositions(); AddSolverTime(start);
                if (!moved) break;
                var firstNew = constraints.Count;
                BuildContacts();
                if (constraints.Count != firstNew)
                {
                    if (Settings.CollectDiagnostics) diagnostics.RepeatedVelocityPasses++;
                    SolveVelocities(0);
                }
            }
            foreach (var id in active)
            {
                var body = ships[id]!; body.Path[phase + 1] = body.Position; body.PathSegments = phase + 1;
            }
        }
        contacts.Sort(static (a, b) => a.FirstSerial != b.FirstSerial
            ? a.FirstSerial.CompareTo(b.FirstSerial) : a.SecondSerial.CompareTo(b.SecondSerial));
        if (Settings.CollectDiagnostics) diagnostics.Sorts++;
        gridDirty = true;
    }
    private void BuildContacts()
    {
        if (!Settings.CacheContactCandidates) { BuildContactsUncached(); return; }
        var start = Stamp();
        if (Settings.CollectDiagnostics) diagnostics.ContactBuilds++;
        var rebuild = !candidateCacheValid || !ContactBoundsStillCovered();
        if (!candidateFallback && rebuild) BuildPotentialPairs();
        if (candidateFallback)
        {
            // Budget overflow retains the complete exact broad phase, never truncates pairs.
            if (Settings.CollectDiagnostics) { diagnostics.CandidateFallbacks++; diagnostics.ContactBuilds--; }
            if (Settings.CollectTimings) contactCandidatesMs += Elapsed(start);
            BuildContactsUncached(); return;
        }
        else if (!rebuild && Settings.CollectDiagnostics) diagnostics.ReusedContactBuilds++;

        var narrowStart = Stamp();
        var next = 0;
        for (var i = 0; i < potentialCount; i++)
        {
            var pair = potentialPairs[i];
            if (Settings.CollectDiagnostics) diagnostics.CandidatePairsVisited++;
            var a = ships[pair.First]!; var b = ships[pair.Second]!;
            // Match the original tight AABB filter as well as its float narrow geometry.
            if (a.MinX > b.MaxX || a.MaxX < b.MinX || a.MinY > b.MaxY || a.MaxY < b.MinY ||
                !TryAddContact(pair.First, pair.Second)) potentialPairs[next++] = pair;
        }
        potentialCount = next;
        if (Settings.CollectTimings)
        {
            diagnostics.ContactNarrowMs += Elapsed(narrowStart);
            contactCandidatesMs += Elapsed(start);
        }
    }
    private void CurrentContactBounds(ShipBody body) => SpatialBoundsIndex.SweptBounds(body.Position, body.Position,
        body.Radius + Settings.PositionSlop, out body.MinX, out body.MinY, out body.MaxX, out body.MaxY);

    private bool ContactBoundsStillCovered()
    {
        foreach (var id in active)
        {
            var body = ships[id]!; if (!body.Enabled) continue;
            CurrentContactBounds(body);
            if (body.MinX < body.FatMinX || body.MinY < body.FatMinY ||
                body.MaxX > body.FatMaxX || body.MaxY > body.FatMaxY) return false;
        }
        return true;
    }
    private void BuildPotentialPairs()
    {
        grid.Clear(); potentialCount = 0;
        if (Settings.CollectDiagnostics) diagnostics.CandidateCacheBuilds++;
        foreach (var id in active)
        {
            var body = ships[id]!; if (!body.Enabled) continue;
            CurrentContactBounds(body);
            body.FatMinX = Math.BitDecrement(body.MinX - Settings.ContactCandidateMargin);
            body.FatMinY = Math.BitDecrement(body.MinY - Settings.ContactCandidateMargin);
            body.FatMaxX = Math.BitIncrement(body.MaxX + Settings.ContactCandidateMargin);
            body.FatMaxY = Math.BitIncrement(body.MaxY + Settings.ContactCandidateMargin);
            grid.AddBounds(id, body.FatMinX, body.FatMinY, body.FatMaxX, body.FatMaxY);
        }
        grid.Seal();
        foreach (var firstId in active)
        {
            var a = ships[firstId]!; if (!a.Enabled) continue;
            var queryStart = Stamp();
            grid.QueryBounds(a.FatMinX, a.FatMinY, a.FatMaxX, a.FatMaxY, candidates, sort: false);
            candidates.Sort(compareSerial);
            if (Settings.CollectDiagnostics) { diagnostics.Sorts++; diagnostics.ContactCandidates += candidates.Count; }
            if (Settings.CollectTimings) diagnostics.ContactQueryMs += Elapsed(queryStart);
            foreach (var secondId in candidates)
            {
                var b = ships[secondId]!;
                if (b.Serial <= a.Serial || constraintPairs.Contains(Pair(a.Serial, b.Serial))) continue;
                if (potentialCount == potentialPairs.Length)
                {
                    candidateFallback = true; candidateCacheValid = false; return;
                }
                potentialPairs[potentialCount++] = new(firstId, secondId);
            }
        }
        candidateCacheValid = true;
    }
    private void BuildContactsUncached()
    {
        var start = Stamp(); grid.Clear();
        if (Settings.CollectDiagnostics) diagnostics.ContactBuilds++;
        foreach (var id in active)
        {
            var body = ships[id]!;
            if (body.Enabled) grid.Add(id, body.Position, body.Position, body.Radius + Settings.PositionSlop);
        }
        grid.Seal();
        foreach (var firstId in active)
        {
            var a = ships[firstId]!; if (!a.Enabled) continue;
            var queryStart = Stamp();
            grid.Query(a.Position, a.Position, a.Radius + Settings.PositionSlop, candidates, sort: false);
            candidates.Sort(compareSerial);
            if (Settings.CollectTimings) diagnostics.ContactQueryMs += Elapsed(queryStart);
            var narrowStart = Stamp();
            if (Settings.CollectDiagnostics) { diagnostics.ContactCandidates += candidates.Count; diagnostics.Sorts++; }
            foreach (var secondId in candidates)
            {
                var b = ships[secondId]!;
                if (b.Serial <= a.Serial) continue;
                TryAddContact(firstId, secondId);
            }
            if (Settings.CollectTimings) diagnostics.ContactNarrowMs += Elapsed(narrowStart);
        }
        if (Settings.CollectTimings) contactCandidatesMs += Elapsed(start);
    }
    private bool TryAddContact(int firstId, int secondId)
    {
        var a = ships[firstId]!; var b = ships[secondId]!;
        if (Settings.CollectDiagnostics) diagnostics.ContactTests++;
        var sum = (double)a.Radius + b.Radius;
        var delta = b.Position - a.Position;
        if ((double)delta.X * delta.X + (double)delta.Y * delta.Y > (sum + Settings.PositionSlop) * (sum + Settings.PositionSlop)) return false;
        var key = Pair(a.Serial, b.Serial);
        if (Settings.CollectDiagnostics) diagnostics.ContactPairs++;
        if (constraints.Count >= Settings.MaximumContactPairs && !constraintPairs.Contains(key))
            throw new InvalidOperationException("Physics contact budget exceeded; increase MaximumContactPairs or reduce density.");
        if (!constraintPairs.Add(key)) return true;
        if (Settings.CollectDiagnostics) diagnostics.NewConstraints++;
        var distance = Math.Sqrt((double)delta.X * delta.X + (double)delta.Y * delta.Y);
        var normal = distance > 0 ? new Vector2((float)(delta.X / distance), (float)(delta.Y / distance)) : Vector2.UnitX;
        var point = a.Position + normal * (float)((distance + a.Radius - b.Radius) * 0.5);
        var armA = point - a.Position; var armB = point - b.Position;
        var relative = PointVelocity(b.Velocity, b.AngularVelocity, armB) - PointVelocity(a.Velocity, a.AngularVelocity, armA);
        var speed = Vector2.Dot(relative, normal);
        var tangent = default(Vector2); double denominator = 0;
        if (Settings.Friction > 0)
        {
            tangent = new Vector2(-normal.Y, normal.X);
            var ra = Cross(armA, tangent); var rb = Cross(armB, tangent);
            denominator = (double)a.InverseMass + b.InverseMass + (double)ra * ra * a.InverseInertia + (double)rb * rb * b.InverseInertia;
        }
        constraints.Add(new() { First = firstId, Second = secondId, Normal = normal, FirstArm = armA, SecondArm = armB,
            Bias = speed < -Settings.RestitutionThreshold ? -Settings.Restitution * speed : 0,
            Tangent = tangent, NormalMass = a.InverseMass + b.InverseMass, TangentMass = denominator });
        if (!currentPairs.Contains(key) && currentPairs.Count >= Settings.MaximumContactPairs)
            throw new InvalidOperationException("Physics per-tick contact budget exceeded.");
        if (currentPairs.Add(key) && !previousPairs.Contains(key))
        {
            if (contacts.Count >= Settings.MaximumContactPairs) throw new InvalidOperationException("Physics begin-contact budget exceeded.");
            contacts.Add(new(a.Serial, b.Serial, relative.Length(), point));
        }
        return true;
    }
    private void SolveVelocities(int first)
    {
        var start = Stamp();
        if (Settings.CollectDiagnostics) { diagnostics.VelocityPasses++; diagnostics.VelocityConstraintVisits += (long)(constraints.Count - first) * Settings.VelocityIterations; }
        // This method and ApplyImpulse only mutate values; no list growth or structural calls occur.
        var values = CollectionsMarshal.AsSpan(constraints);
        var count = values.Length;
        for (var iteration = 0; iteration < Settings.VelocityIterations; iteration++)
            for (var i = first; i < count; i++)
            {
                ref var c = ref values[i]; var a = ships[c.First]!; var b = ships[c.Second]!;
                var relative = PointVelocity(b.Velocity, b.AngularVelocity, c.SecondArm) - PointVelocity(a.Velocity, a.AngularVelocity, c.FirstArm);
                var impulse = (c.Bias - Vector2.Dot(relative, c.Normal)) / c.NormalMass;
                var accumulated = Math.Max(0, c.NormalImpulse + impulse);
                ApplyImpulse(a, b, c.Normal * (accumulated - c.NormalImpulse), c.FirstArm, c.SecondArm, false);
                c.NormalImpulse = accumulated;
                if (Settings.Friction > 0)
                {
                    var tangent = c.Tangent;
                    relative = PointVelocity(b.Velocity, b.AngularVelocity, c.SecondArm) - PointVelocity(a.Velocity, a.AngularVelocity, c.FirstArm);
                    var limit = Settings.Friction * c.NormalImpulse;
                    var friction = (float)Math.Clamp(c.TangentImpulse - Vector2.Dot(relative, tangent) / c.TangentMass, -limit, limit);
                    ApplyImpulse(a, b, tangent * (friction - c.TangentImpulse), c.FirstArm, c.SecondArm, true);
                    c.TangentImpulse = friction;
                }
            }
        AddSolverTime(start);
    }
    private bool CorrectPositions()
    {
        var moved = false;
        if (Settings.CollectDiagnostics) diagnostics.PositionConstraintVisits += constraints.Count;
        foreach (ref readonly var c in CollectionsMarshal.AsSpan(constraints))
        {
            var a = ships[c.First]!; var b = ships[c.Second]!; var delta = b.Position - a.Position;
            var distance = Math.Sqrt((double)delta.X * delta.X + (double)delta.Y * delta.Y);
            var penetration = (double)a.Radius + b.Radius - distance - Settings.PositionSlop;
            if (penetration <= 0.0001f) continue;
            var normal = distance > 0 ? new Vector2((float)(delta.X / distance), (float)(delta.Y / distance)) : c.Normal;
            var correction = (float)(Math.Min(Settings.MaximumCorrection, penetration * Settings.CorrectionFraction) / ((double)a.InverseMass + b.InverseMass));
            a.Position -= normal * (correction * a.InverseMass); b.Position += normal * (correction * b.InverseMass);
            moved = true;
        }
        return moved;
    }
    public bool Sweep(Vector2 from, Vector2 to, float radius, out int targetSerial, out Vector2 impact)
        => Sweep(from, to, radius, -1, out targetSerial, out impact);
    public bool Sweep(Vector2 from, Vector2 to, float radius, int ignoredSerial, out int targetSerial, out Vector2 impact)
        => SweepCore(from, to, radius, default, 0, ignoredSerial, false, true, out targetSerial, out impact);
    internal bool SweepUnfiltered(Vector2 from, Vector2 to, float radius, out int targetSerial, out Vector2 impact, int ignoredSerial = -1)
        => SweepCore(from, to, radius, default, 0, ignoredSerial, false, false, out targetSerial, out impact);
    public bool SweepRectangle(Vector2 from, Vector2 to, Vector2 halfExtents, float angle, int ignoredSerial, out int targetSerial, out Vector2 impact)
        => SweepCore(from, to, Math.BitIncrement(Math.Sqrt((double)halfExtents.X * halfExtents.X + (double)halfExtents.Y * halfExtents.Y)),
            halfExtents, angle, ignoredSerial, true, true, out targetSerial, out impact);
    private bool SweepCore(Vector2 from, Vector2 to, double radius, Vector2 extents, float angle, int ignoredSerial,
        bool rectangle, bool filtered, out int targetSerial, out Vector2 impact)
    {
        ThrowIfDisposed();
        if (!Finite(from) || !Finite(to) || !double.IsFinite(radius) || radius < 0 ||
            rectangle && (!Finite(extents) || extents.X < 0 || extents.Y < 0 || !float.IsFinite(angle)))
            throw new ArgumentOutOfRangeException(nameof(radius));
        EnsureActive(); sweeps++;
        var start = Stamp();
        if (filtered)
        {
            EnsureSweepGrid();
            grid.QueryBounds(Math.BitDecrement(Math.Min(from.X, to.X) - radius), Math.BitDecrement(Math.Min(from.Y, to.Y) - radius),
                Math.BitIncrement(Math.Max(from.X, to.X) + radius), Math.BitIncrement(Math.Max(from.Y, to.Y) + radius), candidates);
        }
        else { candidates.Clear(); candidates.AddRange(active); }
        if (Settings.CollectTimings) hitCandidatesMs += Elapsed(start);
        start = Stamp();
        targetSerial = -1; impact = to; double best = double.PositiveInfinity;
        var eligible = 0;
        var segments = sweepSegments;
        double shotDx = 0, shotDy = 0, cosine = 0, sine = 0;
        var prepared = false;
        foreach (var id in candidates)
        {
            var body = ships[id]!;
            if (!body.Enabled || body.Serial == ignoredSerial) continue;
            hitCandidates++; eligible++;
            if (Settings.CollectDiagnostics) diagnostics.CcdCandidates++;
            if (!prepared)
            {
                shotDx = ((double)to.X - from.X) / segments;
                shotDy = ((double)to.Y - from.Y) / segments;
                if (rectangle) { cosine = Math.Cos(angle); sine = Math.Sin(angle); }
                prepared = true;
            }
            for (var phase = 0; phase < segments; phase++)
            {
                // This interval cannot precede its start; equality still participates in serial ties.
                if (phase != 0 && (double)phase / segments > best)
                {
                    if (Settings.CollectDiagnostics) diagnostics.PrunedCcdIntervals += segments - phase;
                    break;
                }
                var targetFrom = body.PathSegments == 0 ? body.Position : body.Path[phase];
                var targetTo = body.PathSegments == 0 ? body.Position : body.Path[phase + 1];
                var shotX = from.X + shotDx * phase; var shotY = from.Y + shotDy * phase;
                var rx = shotX - targetFrom.X; var ry = shotY - targetFrom.Y;
                var dx = shotDx - ((double)targetTo.X - targetFrom.X);
                var dy = shotDy - ((double)targetTo.Y - targetFrom.Y);
                narrowTests++;
                if (Settings.CollectDiagnostics) diagnostics.CcdNarrowTests++;
                double local;
                var hit = rectangle
                    ? CircleSweepMath.TryRectangleRelative(-rx, -ry, -dx, -dy, extents, cosine, sine, body.Radius, out local)
                    : CircleSweepMath.TryCircleRelative(rx, ry, dx, dy, (double)body.Radius + radius, out local);
                if (!hit) continue;
                var fraction = (phase + local) / segments;
                if (fraction > best || fraction == best && body.Serial >= targetSerial) continue;
                best = fraction; targetSerial = body.Serial;
                var centerX = from.X + ((double)to.X - from.X) * fraction;
                var centerY = from.Y + ((double)to.Y - from.Y) * fraction;
                var targetX = targetFrom.X + ((double)targetTo.X - targetFrom.X) * local;
                var targetY = targetFrom.Y + ((double)targetTo.Y - targetFrom.Y) * local;
                var nx = centerX - targetX; var ny = centerY - targetY;
                if (rectangle)
                {
                    var localX = targetX - centerX; var localY = targetY - centerY;
                    var x = Math.Clamp(localX * cosine + localY * sine, -extents.X, extents.X);
                    var y = Math.Clamp(-localX * sine + localY * cosine, -extents.Y, extents.Y);
                    var normalX = centerX + x * cosine - y * sine - targetX;
                    var normalY = centerY + x * sine + y * cosine - targetY;
                    if (normalX != 0 || normalY != 0) { nx = normalX; ny = normalY; }
                }
                var length = Math.Sqrt(nx * nx + ny * ny);
                if (length == 0) { nx = 1; ny = 0; length = 1; }
                impact = new((float)(targetX + nx / length * body.Radius), (float)(targetY + ny / length * body.Radius));
            }
        }
        if (eligible == 0) emptySweeps++;
        if (Settings.CollectTimings) hitNarrowMs += Elapsed(start);
        return double.IsFinite(best);
    }
    public bool CanSpawn(Vector2 position, float radius)
    {
        ThrowIfDisposed();
        if (!Finite(position) || !float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        EnsureSweepGrid(); grid.Query(position, position, radius, candidates);
        foreach (var id in candidates)
        {
            var body = ships[id]!; if (!body.Enabled) continue;
            var delta = body.Position - position; var sum = (double)radius + body.Radius;
            if ((double)delta.X * delta.X + (double)delta.Y * delta.Y < sum * sum) return false;
        }
        return true;
    }
    private void EnsureSweepGrid()
    {
        if (!gridDirty) return;
        EnsureActive(); grid.Clear();
        foreach (var id in active)
        {
            var body = ships[id]!; if (!body.Enabled) continue;
            double minX = body.Position.X, maxX = minX, minY = body.Position.Y, maxY = minY;
            for (var p = 0; p <= body.PathSegments; p++)
            {
                minX = Math.Min(minX, body.Path[p].X); maxX = Math.Max(maxX, body.Path[p].X);
                minY = Math.Min(minY, body.Path[p].Y); maxY = Math.Max(maxY, body.Path[p].Y);
            }
            grid.AddBounds(id, minX - body.Radius, minY - body.Radius, maxX + body.Radius, maxY + body.Radius);
        }
        grid.Seal();
        gridDirty = false;
    }
    private void EnsureActive()
    {
        if (!membershipDirty) return;
        active.Clear();
        for (var id = 0; id < ships.Count; id++) if (ships[id] is not null) active.Add(id);
        active.Sort(compareSerial); membershipDirty = false;
    }
    private ShipBody GetBody(int id)
    {
        ThrowIfDisposed();
        if ((uint)id >= (uint)ships.Count || ships[id] is not { } body) throw new ArgumentOutOfRangeException(nameof(id));
        return body;
    }
    private static void ApplyImpulse(ShipBody a, ShipBody b, Vector2 impulse, Vector2 armA, Vector2 armB, bool angular)
    {
        a.Velocity -= impulse * a.InverseMass; b.Velocity += impulse * b.InverseMass;
        if (angular) { a.AngularVelocity -= Cross(armA, impulse) * a.InverseInertia; b.AngularVelocity += Cross(armB, impulse) * b.InverseInertia; }
    }
    private static Vector2 PointVelocity(Vector2 velocity, float angular, Vector2 arm) => velocity + new Vector2(-arm.Y, arm.X) * angular;
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static ulong Pair(int a, int b) => ((ulong)(uint)a << 32) | (uint)b;
    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
    private static void ValidateTransform(Transform t)
    {
        if (!Finite(t.Position) || !float.IsFinite(t.Angle)) throw new ArgumentOutOfRangeException(nameof(t));
    }
    private long Stamp() => Settings.CollectTimings ? Stopwatch.GetTimestamp() : 0;
    private static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    private void AddSolverTime(long start) { if (Settings.CollectTimings) solverMs += Elapsed(start); }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose()
    {
        if (disposed) return;
        ships.Clear(); freeIds.Clear(); active.Clear(); candidates.Clear(); constraints.Clear(); contacts.Clear(); liveSerials.Clear();
        previousPairs.Clear(); currentPairs.Clear(); constraintPairs.Clear(); grid.Clear(); Bodies = 0; disposed = true;
        potentialPairs = Array.Empty<CandidatePair>(); potentialCount = 0;
    }
}
