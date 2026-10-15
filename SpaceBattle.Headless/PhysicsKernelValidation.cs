using System.Numerics;
using SpaceBattle;

internal static partial class Validation
{
    private static void PhysicsKernelChecks()
    {
        KernelMassChecks();
        KernelHeadOnCheck(2, 2, 1, 1);
        KernelHeadOnCheck(2, 6, 1, 2);
        KernelAngularIntegrationCheck();
        KernelFrictionCheck();
        KernelContactLifecycleCheck();
        KernelDisabledAndQueryCheck();
        KernelArgumentCheck();
        KernelDenseCheck();
        KernelAllocationCheck();
        KernelMovingAllocationCheck();
        KernelBattleCollisionChecks();
        Console.WriteLine("PASS: homogeneous disk mass/inertia, elastic impulses, spin integration, friction torque/conservation, canonical begin contacts, recycled/disabled slots, analytic sweeps, arguments, dense stability, warmed zero-allocation physics and collision damage integration.");
    }

    private static PhysicsSettings KernelElasticSettings() => new()
    {
        Friction = 0, Restitution = 1, RestitutionThreshold = 0
    };

    private static int KernelDisk(PhysicsService physics, int serial, Vector2 position,
        float radius = 1, float mass = 2, Vector2 velocity = default, float spin = 0, float angle = 0)
    {
        var transform = new Transform { Position = position, Previous = position, Angle = angle };
        var body = physics.AddShip(serial, transform, new Collider { Radius = radius, Mass = mass, ShapeCount = 6 });
        physics.SetShip(body, transform, new Motion { Velocity = velocity, AngularVelocity = spin }, true);
        return body;
    }

    private static void KernelMassChecks()
    {
        var defaults = new PhysicsSettings();
        Require(defaults.Restitution == 0.1f && defaults.Friction == 0.08f &&
            defaults.VelocityIterations == 8 && defaults.PositionIterations == 8 &&
            defaults.PositionSlop == 0.01f && defaults.CorrectionFraction == 0.8f &&
            defaults.MaximumCorrection == 0.5f && defaults.RestitutionThreshold == 10 && !defaults.CollectTimings,
            "kernel default solver settings");
        using var physics = new PhysicsService();
        foreach (var shapes in new[] { 1, 4, 6 })
        {
            var body = physics.AddShip(shapes, new Transform { Position = new(shapes * 10, 0) },
                new Collider { Radius = 3, Mass = 12, ShapeCount = shapes });
            var properties = physics.GetMassProperties(body);
            KernelNear(properties.Mass, 12, 0.0001f, "disk mass independent of visual ShapeCount");
            KernelNear(properties.Inertia, 54, 0.0005f, "homogeneous disk inertia = half mass times radius squared");
        }
        Require(physics.Bodies == 3 && physics.Fixtures == 3, "one physical disk per ship regardless of visual shape count");
    }

    private static void KernelHeadOnCheck(float firstMass, float secondMass, float firstRadius, float secondRadius)
    {
        using var physics = new PhysicsService(KernelElasticSettings());
        var normal = new Vector2(0.6f, 0.8f);
        var tangent = new Vector2(-normal.Y, normal.X);
        var origin = new Vector2(7, -3);
        var firstVelocity = normal * 4 + tangent * 0.75f;
        var secondVelocity = normal * -2 + tangent * 0.75f;
        var first = KernelDisk(physics, 90, origin - normal * firstRadius, firstRadius, firstMass,
            firstVelocity, angle: 0.9f);
        var second = KernelDisk(physics, 10, origin + normal * secondRadius, secondRadius, secondMass,
            secondVelocity, angle: -1.2f);
        var beforeMomentum = firstMass * firstVelocity + secondMass * secondVelocity;
        var beforeEnergy = KernelEnergy(physics, first) + KernelEnergy(physics, second);
        // The tiny step creates less than PositionSlop penetration, so correction does not affect the oracle.
        physics.Step(0.0001f);
        var a = physics.GetShip(first); var b = physics.GetShip(second);
        var sum = firstMass + secondMass;
        var expectedFirst = ((firstMass - secondMass) * 4 + 2 * secondMass * -2) / sum;
        var expectedSecond = (2 * firstMass * 4 + (secondMass - firstMass) * -2) / sum;
        var label = $"elastic disk masses {firstMass}/{secondMass}";
        KernelNear(a.Velocity, normal * expectedFirst + tangent * 0.75f, 0.002f, label + " first velocity");
        KernelNear(b.Velocity, normal * expectedSecond + tangent * 0.75f, 0.002f, label + " second velocity");
        KernelNear(firstMass * a.Velocity + secondMass * b.Velocity, beforeMomentum, 0.002f, label + " linear momentum");
        KernelNear(KernelEnergy(physics, first) + KernelEnergy(physics, second), beforeEnergy,
            beforeEnergy * 0.0005f, label + " kinetic energy");
        KernelNear(a.AngularVelocity, 0, 0.00001f, label + " radial impulse cannot spin first disk");
        KernelNear(b.AngularVelocity, 0, 0.00001f, label + " radial impulse cannot spin second disk");
        Require(physics.Contacts.Count == 1 && physics.Contacts[0].FirstSerial == 10 &&
            physics.Contacts[0].SecondSerial == 90, label + " single canonical begin contact");
        KernelNear(physics.Contacts[0].RelativeSpeed, 6, 0.002f, label + " pre-solve relative speed");
        var afterEnergy = KernelEnergy(physics, first) + KernelEnergy(physics, second);
        for (var tick = 0; tick < 10; tick++)
        {
            physics.Step(1f / 60);
            Require(physics.Contacts.Count == 0, label + " separating disks do not retrigger contact");
        }
        KernelNear(KernelEnergy(physics, first) + KernelEnergy(physics, second), afterEnergy,
            beforeEnergy * 0.0005f, label + " free flight has no physical damping");
    }

    private static void KernelAngularIntegrationCheck()
    {
        using var physics = new PhysicsService();
        const float angle = -0.7f, spin = 1.75f, dt = 1f / 120;
        var start = new Vector2(3, 4); var velocity = new Vector2(2, -3);
        var body = KernelDisk(physics, 1, start, velocity: velocity, spin: spin, angle: angle);
        for (var step = 1; step <= 90; step++)
        {
            physics.Step(dt);
            var state = physics.GetShip(body);
            KernelNear(state.Angle, angle + step * dt * spin, 0.00003f, "angular velocity integrated over multiple steps");
            KernelNear(state.AngularVelocity, spin, 0.000001f, "physics preserves spin without damping");
            KernelNear(state.Position, start + velocity * (step * dt), 0.00005f, "free translation over multiple steps");
            KernelNear(state.Velocity, velocity, 0.000001f, "physics preserves free-flight linear velocity");
        }
    }

    private static void KernelFrictionCheck()
    {
        using var physics = new PhysicsService(new PhysicsSettings
        {
            Restitution = 0, RestitutionThreshold = 0, Friction = 0.5f
        });
        var first = KernelDisk(physics, 71, new(-1, 0), velocity: new(2, 3));
        var second = KernelDisk(physics, 12, new(2, 0), radius: 2, mass: 6, velocity: new(-1, 0));
        var initialA = physics.GetShip(first); var initialB = physics.GetShip(second);
        var momentum = initialA.Velocity * 2 + initialB.Velocity * 6;
        var angularMomentum = KernelAngularMomentum(physics, first) + KernelAngularMomentum(physics, second);
        var energy = KernelEnergy(physics, first) + KernelEnergy(physics, second);
        // Initially just touching; integration depth is below slop. True shared-point torque conserves L.
        physics.Step(0.000001f);
        var a = physics.GetShip(first); var b = physics.GetShip(second);
        // jt = -vt / (1/mA + 1/mB + rA^2/IA + rB^2/IB) = 1.5, inside mu*jn = 2.25.
        KernelNear(a.Velocity, new(-0.25f, 2.25f), 0.0003f, "friction first translational impulse");
        KernelNear(b.Velocity, new(-0.25f, 0.25f), 0.0003f, "friction second translational impulse");
        KernelNear(a.AngularVelocity, -1.5f, 0.0003f, "friction first true-arm inertia torque");
        KernelNear(b.AngularVelocity, -0.25f, 0.0003f, "friction second true-arm inertia torque");
        KernelNear(a.Velocity * 2 + b.Velocity * 6, momentum, 0.0005f, "friction conserves linear momentum");
        KernelNear(KernelAngularMomentum(physics, first) + KernelAngularMomentum(physics, second),
            angularMomentum, 0.0005f, "shared friction contact conserves total angular momentum without correction");
        var slip = b.Velocity.Y - 2 * b.AngularVelocity - (a.Velocity.Y + a.AngularVelocity);
        KernelNear(slip, 0, 0.001f, "friction resolves relative contact-point tangential speed");
        Require(KernelEnergy(physics, first) + KernelEnergy(physics, second) <= energy + 0.0005f,
            "friction cannot create kinetic energy");
        Require(physics.Contacts.Count == 1, "friction reports one begin contact");
        KernelNear(physics.Contacts[0].RelativeSpeed, MathF.Sqrt(18), 0.0003f,
            "contact point-speed captured before friction and restitution solve");

        using var spinning = new PhysicsService(KernelElasticSettings());
        KernelDisk(spinning, 2, new(-1, 0), spin: 2);
        KernelDisk(spinning, 1, new(1.995f, 0), radius: 2, spin: 0.5f);
        spinning.Step(0.000001f);
        Require(spinning.Contacts.Count == 1, "stationary rotating disks begin touching");
        KernelNear(spinning.Contacts[0].RelativeSpeed, 3, 0.01f,
            "relative point-speed includes angular motion rather than center speed only");
    }

    private static void KernelContactLifecycleCheck()
    {
        using var physics = new PhysicsService();
        const float side = 1.995f;
        var top = new Vector2(side / 2, side * MathF.Sqrt(3) / 2);
        KernelDisk(physics, 30, Vector2.Zero);
        var low = KernelDisk(physics, 10, new(side, 0));
        var middle = KernelDisk(physics, 20, top);
        physics.Step(1f / 60);
        KernelPairs(physics, (10, 20), (10, 30), (20, 30));
        foreach (var contact in physics.Contacts)
        {
            KernelNear(contact.RelativeSpeed, 0, 0.000001f, "stationary overlap reports no correction-generated speed");
            Require(KernelFinite(contact.Position), "contact position finite");
        }
        for (var tick = 0; tick < 12; tick++)
        {
            physics.Step(1f / 60);
            Require(physics.Contacts.Count == 0, "persistent touching emits begin contact only once");
        }
        physics.SetShip(low, new Transform { Position = new(side, 0) }, default, false);
        physics.Step(1f / 60);
        Require(physics.Contacts.Count == 0, "disabling one disk does not recreate another persistent pair");
        physics.SetShip(low, new Transform { Position = new(side, 0) }, default, true);
        physics.Step(1f / 60);
        KernelPairs(physics, (10, 20), (10, 30));
        physics.RemoveShip(middle);
        physics.RemoveShip(middle);
        Require(physics.Bodies == 2 && physics.Fixtures == 2, "repeated removal does not duplicate free slots");
        var replacement = KernelDisk(physics, 5, top);
        Require(replacement == middle, "removed disk slot reused");
        physics.Step(1f / 60);
        KernelPairs(physics, (5, 10), (5, 30));
        physics.Step(1f / 60);
        Require(physics.Contacts.Count == 0, "recycled slot keeps new serial-pair lifetime without stale contacts");
        physics.SetShip(replacement, new Transform { Position = new(50, 50) }, default, true);
        physics.Step(1f / 60);
        physics.SetShip(replacement, new Transform { Position = top }, default, true);
        physics.Step(1f / 60);
        KernelPairs(physics, (5, 10), (5, 30));
    }

    private static void KernelDisabledAndQueryCheck()
    {
        using var physics = new PhysicsService();
        var body = KernelDisk(physics, 41, Vector2.Zero, radius: 2);
        Require(physics.Sweep(new(-10, 0), new(10, 0), 0.5f, out var serial, out var impact) && serial == 41,
            "analytic expanded-circle sweep hits disk");
        KernelNear(impact, new(-2, 0), 0.0002f, "circle sweep contact on target surface");
        Require(physics.Sweep(new(-10, 2.4f), new(10, 2.4f), 0.5f, out serial, out impact) && serial == 41,
            "analytic circle sweep catches off-center grazing hit");
        KernelNear(impact, new(-0.56f, 1.92f), 0.0003f, "grazing quadratic target-surface contact");
        Require(physics.Sweep(Vector2.Zero, Vector2.Zero, 0, out serial, out impact) && serial == 41 &&
            KernelFinite(impact), "zero-length sweep includes start overlap with finite contact");
        KernelNear(impact.Length(), 2, 0.0002f, "start-overlap contact lies on target disk surface");
        Require(!physics.Sweep(new(-10, 0), new(10, 0), 0.5f, 41, out serial, out _) && serial == -1,
            "owner ignored by serial");
        Require(!physics.CanSpawn(new(1, 0), 1) && physics.CanSpawn(new(10, 0), 1), "spawn placement disk overlap");

        var transform = new Transform { Position = Vector2.Zero, Angle = 0.3f };
        physics.SetShip(body, transform, new Motion { Velocity = new(100, 0), AngularVelocity = 2 }, false);
        var disabled = physics.GetShip(body);
        physics.Step(0.25f);
        Require(physics.GetShip(body) == disabled && physics.Contacts.Count == 0,
            "disabled disk neither integrates nor contacts");
        Require(physics.Bodies == 1 && physics.Fixtures == 1 && physics.CanSpawn(Vector2.Zero, 1),
            "disabled disk retains slot/counts but cannot block placement");
        Require(!physics.Sweep(new(-10, 0), new(10, 0), 1, out _, out _) &&
            !physics.SweepRectangle(new(-10, 0), new(10, 0), new(2, 0.5f), 0, -1, out _, out _),
            "disabled disk excluded from all swept queries");
        physics.SetShip(body, transform, default, true);
        Require(!physics.CanSpawn(Vector2.Zero, 1) && physics.Sweep(new(-10, 0), new(10, 0), 0, out serial, out _) && serial == 41,
            "reenabled disk immediately participates in queries");
        physics.RemoveShip(body);
        Require(physics.CanSpawn(Vector2.Zero, 1) && !physics.Sweep(Vector2.Zero, Vector2.Zero, 1, out _, out _),
            "removed disk has no stale placement or sweep proxy");
        var recycled = KernelDisk(physics, 7, Vector2.Zero);
        Require(recycled == body && physics.Sweep(new(-10, 0), new(10, 0), 0, 41, out serial, out _) && serial == 7,
            "old ignored serial cannot exempt replacement disk");
        Require(physics.SweepRectangle(new(-10, 0), new(10, 0), new(2, 0.5f), 0, -1, out serial, out impact) && serial == 7,
            "axis-aligned rectangle sweep");
        KernelNear(impact, new(-1, 0), 0.0003f, "rectangle face contact on target disk");
        Require(physics.SweepRectangle(new(-10, 0), new(10, 0), new(2, 0.5f), MathF.PI / 2, -1, out serial, out impact) && serial == 7,
            "rotated rectangle sweep");
        KernelNear(impact, new(-1, 0), 0.0003f, "rotated rectangle face contact on target disk");
        Require(!physics.SweepRectangle(new(-10, 1.75f), new(10, 1.75f), new(2, 0.5f), 0, -1, out _, out _) &&
            physics.SweepRectangle(new(-10, 1.75f), new(10, 1.75f), new(2, 0.5f), MathF.PI / 2, -1, out serial, out impact) && serial == 7,
            "rectangle angle swaps long and short extents for an off-axis hit");
        KernelNear(impact, new(-1, 0), 0.0003f, "rotated off-axis face contact");
        Require(physics.SweepRectangle(new(-10, 1.25f), new(10, 1.25f), new(2, 0.5f), 0, -1, out serial, out impact) && serial == 7,
            "rectangle rounded Minkowski corner hit");
        KernelNear(impact, new(-MathF.Sqrt(1 - 0.75f * 0.75f), 0.75f), 0.0003f,
            "rectangle corner is disk-rounded, not bounding box");
        Require(!physics.SweepRectangle(new(-10, 1.6f), new(10, 1.6f), new(2, 0.5f), 0, -1, out _, out _) &&
            !physics.SweepRectangle(new(-10, 0), new(10, 0), new(2, 0.5f), 0, 7, out _, out _),
            "rectangle sweep misses outside geometry and respects ignored serial");
        Require(physics.SweepRectangle(Vector2.Zero, Vector2.Zero, new(2, 0.5f), 0.7f, -1, out serial, out impact) &&
            serial == 7 && KernelFinite(impact), "rectangle zero-length start overlap");
        KernelNear(impact.Length(), 1, 0.0002f, "rectangle start-overlap contact lies on disk surface");
    }

    private static void KernelArgumentCheck()
    {
        using var physics = new PhysicsService();
        var body = KernelDisk(physics, 1, new(4, -2), velocity: new(3, 1), spin: 2, angle: 0.6f);
        var initial = physics.GetShip(body);
        physics.Step(0);
        Require(physics.GetShip(body) == initial && physics.Contacts.Count == 0, "zero dt does not integrate or solve");
        foreach (var value in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            KernelArgument(() => physics.Step(value), "invalid dt rejected");
            KernelArgument(() => physics.Sweep(Vector2.Zero, Vector2.One, value, out _, out _), "invalid sweep radius rejected");
            KernelArgument(() => physics.CanSpawn(Vector2.Zero, value), "invalid spawn radius rejected");
            KernelArgument(() => physics.AddShip(2, default, new Collider { Radius = value, Mass = 1 }), "invalid disk radius rejected");
            KernelArgument(() => physics.AddShip(2, default, new Collider { Radius = 1, Mass = value }), "invalid disk mass rejected");
        }
        KernelArgument(() => physics.AddShip(2, default, new Collider { Radius = 0, Mass = 1 }), "zero disk radius rejected");
        KernelArgument(() => physics.AddShip(2, default, new Collider { Radius = 1, Mass = 0 }), "zero disk mass rejected");
        KernelArgument(() => physics.CanSpawn(Vector2.Zero, 0), "zero spawn radius rejected");
        KernelArgument(() => physics.AddShip(2, new Transform { Angle = float.NaN }, new Collider { Radius = 1, Mass = 1 }),
            "nonfinite initial transform rejected");
        KernelArgument(() => physics.SetShip(body, new Transform { Position = new(float.NaN, 0) }, default, true), "nonfinite set position rejected");
        KernelArgument(() => physics.SetShip(body, new Transform { Angle = float.PositiveInfinity }, default, true), "nonfinite set angle rejected");
        KernelArgument(() => physics.SetShip(body, default, new Motion { Velocity = new(0, float.NaN) }, true), "nonfinite velocity rejected");
        KernelArgument(() => physics.SetShip(body, default, new Motion { AngularVelocity = float.NaN }, true), "nonfinite spin rejected");
        KernelArgument(() => physics.Sweep(new(float.NaN, 0), Vector2.One, 0, out _, out _), "nonfinite sweep start rejected");
        KernelArgument(() => physics.Sweep(Vector2.Zero, new(0, float.PositiveInfinity), 0, out _, out _), "nonfinite sweep end rejected");
        KernelArgument(() => physics.CanSpawn(new(float.NaN, 0), 1), "nonfinite spawn position rejected");
        KernelArgument(() => physics.SweepRectangle(Vector2.Zero, Vector2.One, new(-1, 1), 0, -1, out _, out _), "negative rectangle extent rejected");
        KernelArgument(() => physics.SweepRectangle(Vector2.Zero, Vector2.One, new(1, float.NaN), 0, -1, out _, out _), "nonfinite rectangle extent rejected");
        KernelArgument(() => physics.SweepRectangle(Vector2.Zero, Vector2.One, Vector2.One, float.NaN, -1, out _, out _), "nonfinite rectangle angle rejected");
        KernelArgument(() => physics.GetShip(-1), "invalid body lookup rejected");
        KernelArgument(() => physics.GetMassProperties(-1), "invalid mass lookup rejected");
        KernelArgument(() => physics.RemoveShip(-1), "invalid body removal rejected");
        foreach (var settings in new[]
        {
            new PhysicsSettings { Restitution = -0.1f },
            new PhysicsSettings { Friction = float.NaN },
            new PhysicsSettings { VelocityIterations = -1 },
            new PhysicsSettings { PositionIterations = -1 },
            new PhysicsSettings { PositionSlop = -1 },
            new PhysicsSettings { CorrectionFraction = float.PositiveInfinity },
            new PhysicsSettings { MaximumCorrection = -1 },
            new PhysicsSettings { RestitutionThreshold = float.NaN }
        })
            KernelArgument(() => { using var invalid = new PhysicsService(settings); }, "invalid physics settings rejected");
        Require(physics.Bodies == 1 && physics.Fixtures == 1 && physics.GetShip(body) == initial,
            "invalid operations leave existing disk state and counts intact");
        physics.RemoveShip(body);
        KernelArgument(() => physics.GetShip(body), "removed body lookup rejected");
        KernelArgument(() => physics.GetMassProperties(body), "removed mass lookup rejected");
        KernelArgument(() => physics.SetShip(body, default, default, true), "removed body update rejected");
        physics.Dispose();
        KernelDisposed(() => physics.Step(0));
        KernelDisposed(() => physics.Sweep(Vector2.Zero, Vector2.One, 0, out _, out _));
    }

    private static void KernelDenseCheck()
    {
        using var physics = new PhysicsService();
        const int count = 48;
        var bodies = new int[count]; var radii = new float[count];
        for (var i = 0; i < count; i++)
        {
            radii[i] = 0.75f + (i * 7 % 5) * 0.2f;
            var position = new Vector2((i % 8 - 3.5f) * 1.6f, (i / 8 - 2.5f) * 1.6f);
            bodies[i] = KernelDisk(physics, 500 - i * 3, position, radii[i], 2 * radii[i] * radii[i],
                -position * 0.08f, (i % 3 - 1) * 0.1f);
        }
        var initialEnergy = bodies.Sum(body => KernelEnergy(physics, body));
        var states = new (Vector2 Position, float Angle, Vector2 Velocity, float AngularVelocity)[count];
        for (var tick = 0; tick < 600; tick++)
        {
            physics.Step(1f / 60);
            var energy = 0f;
            for (var i = 0; i < count; i++)
            {
                states[i] = physics.GetShip(bodies[i]);
                var state = states[i];
                Require(KernelFinite(state.Position) && KernelFinite(state.Velocity) && float.IsFinite(state.Angle) &&
                    float.IsFinite(state.AngularVelocity), $"dense mixed-radius finite state at tick {tick}, disk {i}");
                Require(state.Position.LengthSquared() < 10000 && state.Velocity.LengthSquared() < 625 &&
                    MathF.Abs(state.AngularVelocity) < 25, $"dense mixed-radius bounded state at tick {tick}, disk {i}");
                energy += KernelEnergy(physics, bodies[i]);
            }
            Require(energy <= initialEnergy * 1.01f + 0.01f, $"dense inelastic/friction solve creates no material energy at tick {tick}");
            for (var i = 1; i < physics.Contacts.Count; i++)
            {
                var previous = physics.Contacts[i - 1]; var current = physics.Contacts[i];
                Require(previous.FirstSerial < current.FirstSerial || previous.FirstSerial == current.FirstSerial &&
                    previous.SecondSerial < current.SecondSerial, "dense contacts sorted without duplicate pairs");
            }
            if (tick < 60) continue;
            var maximumPenetration = 0f;
            for (var i = 0; i < count; i++)
                for (var j = i + 1; j < count; j++)
                    maximumPenetration = MathF.Max(maximumPenetration,
                        radii[i] + radii[j] - Vector2.Distance(states[i].Position, states[j].Position));
            Require(maximumPenetration <= 0.08f, $"dense mixed-radius penetration bounded after settling at tick {tick}: {maximumPenetration}");
        }
    }

    private static void KernelAllocationCheck()
    {
        using var physics = new PhysicsService();
        for (var i = 0; i < 32; i++) KernelDisk(physics, i + 1, new(i % 8 * 6, i / 8 * 6));
        // Include a stationary persistent pair to exercise warmed contact bookkeeping as well as empty space.
        KernelDisk(physics, 100, new(100, 0));
        KernelDisk(physics, 101, new(101.995f, 0));
        physics.Step(1f / 60);
        var valid = true;
        for (var i = 0; i < 256; i++) valid &= KernelAllocationWork(physics);
        Require(valid, "allocation fixture query results before measurement");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 512; i++) valid &= KernelAllocationWork(physics);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(valid, "allocation fixture query results during measurement");
        Require(allocated == 0, $"warmed stationary Step/Sweep/CanSpawn allocates zero bytes, observed {allocated}");
    }

    private static bool KernelAllocationWork(PhysicsService physics)
    {
        physics.Step(1f / 60);
        var hit = physics.Sweep(new(-4, 0), new(4, 0), 0.25f, out var serial, out var impact);
        var miss = physics.Sweep(new(-100, -100), new(-90, -90), 0.25f, out _, out _);
        var rectangle = physics.SweepRectangle(new(-4, 0), new(4, 0), new(0.5f, 0.25f), 0, -1, out var rectangleSerial, out _);
        return hit && serial == 1 && KernelFinite(impact) && !miss && rectangle && rectangleSerial == 1 &&
            !physics.CanSpawn(Vector2.Zero, 0.5f) && physics.CanSpawn(new(-100, -100), 0.5f) && physics.Contacts.Count == 0;
    }

    private static void KernelMovingAllocationCheck()
    {
        using var physics = new PhysicsService();
        var bodies = new int[24];
        for (var i = 0; i < bodies.Length; i++) bodies[i] = KernelDisk(physics, 100 - i, new(i % 6 * 1.5f, i / 6 * 1.5f));
        for (var frame = 0; frame < 20; frame++) Work(frame);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 20; frame < 120; frame++) Work(frame);
        Require(GC.GetAllocatedBytesForCurrentThread() - before == 0,
            "reserved moving dense scene with non-slot serial ordering allocates no sort delegates or buffers");

        void Work(int frame)
        {
            for (var i = 0; i < bodies.Length; i++)
                physics.SetShip(bodies[i], new Transform { Position = new(i % 6 * 1.5f + frame * 50, i / 6 * 1.5f) },
                    new Motion { Velocity = new(i % 2 == 0 ? 2 : -2, 0) }, true);
            physics.Step(1f / 60);
            physics.Sweep(new(frame * 50 - 5, 0), new(frame * 50 + 20, 0), 0.2f, out _, out _);
        }
    }

    private static void KernelBattleCollisionChecks()
    {
        KernelBattleSpeedCheck(9.5f, false);
        KernelBattleSpeedCheck(10.5f, true);
        using (var battle = KernelBattle())
        {
            var a = KernelBattleActor(battle, new(-0.15f, 0));
            var b = KernelBattleActor(battle, new(0.15f, 0));
            // Fragile hulls make even small correction-derived damage visible as an erroneous destruction.
            battle.Store.Get<Hull>(a).Current = battle.Store.Get<Hull>(b).Current = 0.01f;
            battle.Store.Commit();
            for (var tick = 0; tick < 90; tick++)
            {
                battle.Step();
                Require(battle.Store.Alive(a) && battle.Store.Alive(b), "stationary deep overlap correction cannot destroy ships");
                KernelNear(battle.Store.Get<Hull>(a).Current, 0.01f, 0.000001f, "correction leaves first hull untouched");
                KernelNear(battle.Store.Get<Hull>(b).Current, 0.01f, 0.000001f, "correction leaves second hull untouched");
                Require(battle.Counters.Explosions == 0 && battle.Counters.ShipsDestroyed == 0 &&
                    battle.DebrisCount == 0 && battle.Counters.Hits == 0, "correction generates neither damage nor explosion/debris");
            }
        }
        using (var battle = KernelBattle())
        {
            var home = battle.SpawnShip(0, ShipClass.LightCarrier, Vector2.Zero, EntityKey.None);
            LifecycleFreezeShip(battle, home);
            battle.Store.Get<Inventory>(home).NextUseTick = int.MaxValue;
            battle.Store.Get<Shield>(home) = default;
            battle.Store.Get<Hull>(home).Armor = 0;
            var docked = KernelBattleActor(battle, Vector2.Zero);
            battle.Store.Set(docked, new Docked { Carrier = home, ReleaseTick = int.MaxValue });
            var radius = battle.Store.Get<Collider>(home).Radius;
            var visitor = KernelBattleActor(battle, new(radius + ShipCatalog.Radius(ShipClass.Fighter) - 0.01f, 0), new(-24, 0));
            battle.Store.Commit();
            LifecycleFreezeMounts(battle);
            var round = OwnerRound(battle, home, WeaponKind.MachineGun, new(-5, 0), new(600, 0));
            var dockedHull = battle.Store.Get<Hull>(docked).Current;
            var visitorHull = battle.Store.Get<Hull>(visitor).Current;
            var homeHull = battle.Store.Get<Hull>(home).Current;
            battle.Step();
            Require(battle.Store.Has<Docked>(docked) && battle.Store.Alive(docked) &&
                battle.Store.Get<Hull>(docked).Current == dockedHull, "docked ship excluded from overlap contacts inside carrier");
            Require(battle.Store.Alive(round) && battle.Counters.Hits == 0 &&
                battle.Store.Get<Transform>(round).Position.X > 0,
                "owner-filtered projectile passes docked ship inside carrier without hit or damage");
            Require(battle.Store.Get<Hull>(visitor).Current < visitorHull && battle.Store.Get<Hull>(home).Current < homeHull,
                "docking exclusion does not disable ordinary visitor/carrier collision damage");
            Require(battle.Counters.Explosions == 0 && battle.Counters.ShipsDestroyed == 0, "docking overlap creates no false explosion");
        }
    }

    private static Battle<SkecsStore> KernelBattle()
    {
        var battle = LifecycleBattle(static () => new SkecsStore());
        FreezeBattle(battle);
        LifecycleFreezeMounts(battle);
        return battle;
    }

    private static EntityKey KernelBattleActor(Battle<SkecsStore> battle, Vector2 position, Vector2 velocity = default)
    {
        var e = battle.SpawnShip(0, ShipClass.Fighter, position, EntityKey.None);
        LifecycleFreezeShip(battle, e);
        battle.Store.Get<Inventory>(e).NextUseTick = int.MaxValue;
        battle.Store.Get<Motion>(e) = new Motion { Velocity = velocity, MaxSpeed = 100, Thrust = 0, Damping = 0 };
        battle.Store.Get<Shield>(e) = default;
        battle.Store.Get<Hull>(e) = new Hull { Current = 100, Maximum = 100, Armor = 0 };
        return e;
    }

    private static void KernelBattleSpeedCheck(float relativeSpeed, bool damage)
    {
        using var battle = KernelBattle();
        var radius = ShipCatalog.Radius(ShipClass.Fighter);
        var a = KernelBattleActor(battle, new(-radius + 0.002f, 0), new(relativeSpeed / 2, 0));
        var b = KernelBattleActor(battle, new(radius - 0.002f, 0), new(-relativeSpeed / 2, 0));
        battle.Store.Commit();
        battle.Step();
        var expected = 100 - (damage ? (relativeSpeed - 10) * 0.35f : 0);
        KernelNear(battle.Store.Get<Hull>(a).Current, expected, 0.003f, "battle first hull uses pre-solve speed and ten-unit threshold");
        KernelNear(battle.Store.Get<Hull>(b).Current, expected, 0.003f, "battle second hull uses pre-solve speed and ten-unit threshold");
        Require(battle.Counters.Explosions == 0 && battle.Counters.ShipsDestroyed == 0 && battle.Counters.Hits == 0,
            "controlled nonlethal collision is neither a projectile hit nor an explosion");
        battle.Step();
        KernelNear(battle.Store.Get<Hull>(a).Current, expected, 0.003f, "battle contact does not damage first hull twice");
        KernelNear(battle.Store.Get<Hull>(b).Current, expected, 0.003f, "battle contact does not damage second hull twice");
    }

    private static float KernelEnergy(PhysicsService physics, int body)
    {
        var state = physics.GetShip(body); var properties = physics.GetMassProperties(body);
        return 0.5f * (properties.Mass * state.Velocity.LengthSquared() +
            properties.Inertia * state.AngularVelocity * state.AngularVelocity);
    }

    private static float KernelAngularMomentum(PhysicsService physics, int body)
    {
        var state = physics.GetShip(body); var properties = physics.GetMassProperties(body);
        return properties.Mass * (state.Position.X * state.Velocity.Y - state.Position.Y * state.Velocity.X) +
            properties.Inertia * state.AngularVelocity;
    }

    private static void KernelPairs(PhysicsService physics, params (int First, int Second)[] expected)
    {
        Require(physics.Contacts.Count == expected.Length, "canonical begin contact count");
        for (var i = 0; i < expected.Length; i++)
            Require(physics.Contacts[i].FirstSerial == expected[i].First && physics.Contacts[i].SecondSerial == expected[i].Second,
                $"canonical sorted unique contact pair {i}: expected {expected[i]}");
    }

    private static bool KernelFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static void KernelNear(float actual, float expected, float tolerance, string name) =>
        Require(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance,
            $"{name}: expected {expected}, actual {actual}, tolerance {tolerance}");

    private static void KernelNear(Vector2 actual, Vector2 expected, float tolerance, string name) =>
        Require(KernelFinite(actual) && Vector2.Distance(actual, expected) <= tolerance,
            $"{name}: expected {expected}, actual {actual}, tolerance {tolerance}");

    private static void KernelArgument(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        Require(false, name);
    }

    private static void KernelDisposed(Action action)
    {
        try { action(); }
        catch (ObjectDisposedException) { return; }
        Require(false, "disposed physics operation rejected");
    }
}
