using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>Physical description of a world object, in its own frame (metres; origin = pose point).</summary>
    public sealed class WorldBodyGeometry
    {
        public Vec3 Min { get; set; }
        public Vec3 Max { get; set; }
        public double MassKg { get; set; } = 0.01;

        /// <summary>"box", "sphere", "cylinder-x/y/z" (rolls about that local axis), or "mesh".</summary>
        public string Shape { get; set; } = "box";

        public MeshModel? Mesh { get; set; }

        /// <summary>Moving output (servo horn, motor shaft): name, pivot and axis in the object frame.</summary>
        public string? PortName { get; set; }
        public Vec3 PortPivot { get; set; }
        public Vec3 PortAxis { get; set; } = Vec3.UnitZ;

        /// <summary>Ranging sensor (ultrasonic): where its beam leaves the part and which way it looks (object frame).</summary>
        public Vec3? SensorOrigin { get; set; }
        public Vec3 SensorAxis { get; set; } = Vec3.UnitX;
    }

    /// <summary>
    /// The mechanical side of a world: one rigid body per object (plus a small rotor body for every
    /// servo horn and motor shaft), joints for the user's mechanical connections, gravity, ground and
    /// collisions — all in one <see cref="PhysicsScene"/>. Actuators are coupled to the circuit both
    /// ways: servos read their pulse command and report their supply current; DC motors drive their
    /// shaft at the electrically computed speed and feel the shaft's load torque.
    /// </summary>
    public sealed partial class WorldPhysics // [phys] partial: cables as ropes, floor materials, sleeping in WorldPhysics.Cables.cs
    {
        private sealed class Item
        {
            public WorldObject Object = null!;
            public SceneBody Body = null!;

            /// <summary>Rotation from the object frame to the body frame (cylinders roll about the body z axis).</summary>
            public Quat ShapeTurn = Quat.Identity;

            public Vec3 Centre;
            public SceneBody? Rotor;
            public SceneJoint? RotorJoint;
            public string? PortName;
            public Servo? Servo;
            public ServoMotor? ServoMotor;
            public DcMotor? Motor;
            public string? ServoRef;
            public UltrasonicSensor? Sonar;
            public Vec3 SonarOrigin;
            public Vec3 SonarAxis;
            public double SonarRange = double.NaN;
            public Vec3 SonarHit;
        }

        private readonly Dictionary<string, Item> _items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private readonly WorldCircuit? _circuit;
        private double _pending;

        public PhysicsScene Scene { get; } = new PhysicsScene();

        /// <summary>Wind, drag, force fields and user behaviours of the world (Sdk/WorldSdkRuntime.cs).</summary>
        public RobotTwin.CoreSim.Sdk.WorldSdkRuntime? Sdk { get; private set; }   // [sdk]

        /// <summary>Simulated mechanical time step (s).</summary>
        public double Step { get; set; } = 1e-3;

        /// <summary>[api] Automation hook: called for every moving port (object id, its hinge) before the circuit drives it; return true when it was driven.</summary>
        public Func<string, SceneJoint, bool>? DriveOverride { get; set; }   // [api]

        private WorldPhysics(WorldCircuit? circuit)
        {
            _circuit = circuit;
        }

        public static WorldPhysics Build(WorldDocument world, Func<WorldObject, WorldBodyGeometry> geometry, WorldCircuit? circuit)
        {
            var p = new WorldPhysics(circuit);
            var env = world.Environment;
            p.Scene.Gravity = new Vec3(0, 0, -env.Gravity);
            p.Scene.GroundEnabled = true;
            // Assemblies are chains of welded parts: more solver passes keep them rigid.
            p.Scene.Iterations = 16;
            p.Scene.RelaxIterations = 4;
            // Small light parts see large accelerations on ordinary touches; report real knocks only.
            p.Scene.ImpactReportG = 40;
            p.ConfigureGround(env); // [phys] material-pair floor friction scaled to the tyre grip μ, regions, sleeping
            foreach (var ob in env.Obstacles)
            {
                p.Scene.AddBody(SceneBody.StaticBox("obstacle", new Vec3(ob.SizeX, ob.SizeY, ob.Height), new Vec3(ob.X, ob.Y, ob.Height / 2), Quat.Identity));
            }

            p.ConfigureTerrain(world); // [terrain] sculpted ground, painted layers
            foreach (var o in world.Objects)
                if (!p.TryAddProp(o)) p.AddObject(o, geometry(o)); // [terrain] static props collide piecewise
            p.BuildMachines(world);   // [arm] robot arms, tools, conveyors, light curtains
            foreach (var j in world.Joints) p.AddJoint(world, j);
            p.FinishReady(world);   // [objs] propellers on shafts, light rotors
            p.FilterAssemblyCollisions(); // only parts within two joints of each other skip collisions (WorldPhysics.Overlap.cs)
            p.Sdk = RobotTwin.CoreSim.Sdk.WorldSdkRuntime.Attach(world, p, circuit);   // [sdk] environment, force fields, behaviours
            p.BuildWings();   // [aero] lifting surfaces replace the generic drag the environment just gave every body (Sandbox/WorldPhysics.Wings.cs)
            p.ApplyLaunches(world);   // [aero] hand-launched aircraft
            return p;
        }

        private static string MaterialOf(WorldObject o) => (o.Material ?? string.Empty).ToUpperInvariant() switch
        {
            "KAUÇUK" or "KAUCUK" => "rubber",
            "AHŞAP" or "AHSAP" or "MDF" or "KONTRPLAK" => "wood",
            "ALÜMİNYUM" or "ALUMINYUM" => "aluminium",
            "ÇELİK" or "CELIK" => "steel",
            "ABS" => "abs",
            "PETG" => "petg",
            "FR4" => "fr4",
            _ => "pla"
        };

        private void AddObject(WorldObject o, WorldBodyGeometry g)
        {
            var size = g.Max - g.Min;
            size = new Vec3(Math.Max(size.X, 0.002), Math.Max(size.Y, 0.002), Math.Max(size.Z, 0.001));
            var centre = (g.Min + g.Max) * 0.5;
            double mass = Math.Max(1e-4, o.MassKg ?? g.MassKg);
            string material = MaterialOf(o);
            var it = new Item { Object = o, Centre = centre };
            SceneBody body;
            switch (g.Shape)
            {
                case "omni-y" or "mecanum-y" or "caster" or "prop-z":   // [objs]
                    body = ReadyBody(o, g, size, mass, it);
                    break;
                case "sphere":
                    body = SceneBody.Sphere(o.Id, Math.Max(size.X, Math.Max(size.Y, size.Z)) / 2, mass, default, material);
                    break;
                case "cylinder-y":
                    // Body z = object y: a wheel on a sideways axle.
                    body = SceneBody.Cylinder(o.Id, Math.Max(size.X, size.Z) / 2, size.Y, mass, default, material);
                    it.ShapeTurn = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);
                    break;
                case "cylinder-x":
                    body = SceneBody.Cylinder(o.Id, Math.Max(size.Y, size.Z) / 2, size.X, mass, default, material);
                    it.ShapeTurn = Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2);
                    break;
                case "cylinder-z":
                    body = SceneBody.Cylinder(o.Id, Math.Max(size.X, size.Y) / 2, size.Z, mass, default, material);
                    break;
                case "mesh" when g.Mesh != null:
                    body = SceneBody.FromMesh(o.Id, g.Mesh, null, mass, material);
                    it.Centre = body.ModelOffset;
                    break;
                default:
                    body = SceneBody.Box(o.Id, size, mass, default, material);
                    break;
            }

            if (g.SensorOrigin is { } so && _circuit?.ComponentRef(o.Id) is { } sref)
            {
                it.Sonar = _circuit.Compiled.DevicesOf(sref).OfType<UltrasonicSensor>().FirstOrDefault();
                it.SonarOrigin = so;
                it.SonarAxis = g.SensorAxis.Normalized();
            }

            var q = o.Pose.Orientation;
            body.Orientation = q * it.ShapeTurn;
            body.Position = o.Pose.Position + q.Rotate(it.Centre);
            body.IsStatic = o.IsStatic;
            body.Tag = o;
            Scene.AddBody(body);
            it.Body = body;

            // A moving output becomes its own small rotor on a hinge.
            if (g.PortName != null)
            {
                var axis = q.Rotate(g.PortAxis).Normalized();
                var pivot = o.Pose.ToWorld(g.PortPivot);
                // A hub heavy enough not to starve the joint solver between a motor and a wheel.
                var rotor = SceneBody.Cylinder(o.Id + ":" + g.PortName, 0.006, 0.006, 0.01, pivot, "abs");
                rotor.Orientation = FromTo(Vec3.UnitZ, axis);
                Scene.AddBody(rotor);
                var hinge = Scene.AddJoint(JointKind.Hinge, body, rotor, pivot, axis, o.Id + ":" + g.PortName);
                it.Rotor = rotor;
                it.RotorJoint = hinge;
                it.PortName = g.PortName;
            }

            // Actuators from the circuit.
            if (_circuit != null && o.Kind == WorldObjectKind.Component && _circuit.ComponentRef(o.Id) is { } r)
            {
                foreach (var d in _circuit.Compiled.DevicesOf(r))
                {
                    if (d is Servo s && it.RotorJoint != null)
                    {
                        it.Servo = s;
                        it.ServoRef = r;
                        o.Parameters.TryGetValue("model", out var model);
                        it.ServoMotor = ServoMotor.ForModel(model ?? o.PresetId);
                        s.MechanicallyDriven = true;
                    }
                    else if (d is DcMotor m && it.RotorJoint != null)
                    {
                        it.Motor = m;
                    }
                }
            }

            _items[o.Id] = it;
            RegisterReady(o, it);   // [objs]
        }

        /// <summary>Parts joined into one assembly do not collide with each other (their shapes are approximate boxes).</summary>
        private void IgnoreSelfCollisions()
        {
            var bodies = Scene.Bodies.ToList();
            var index = bodies.Select((b, i) => (b, i)).ToDictionary(x => x.b, x => x.i);
            var parent = Enumerable.Range(0, bodies.Count).ToArray();
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            foreach (var j in Scene.Joints)
            {
                if (j.B == null || !index.TryGetValue(j.A, out int a) || !index.TryGetValue(j.B, out int b)) continue;
                parent[Find(a)] = Find(b);
            }

            foreach (var group in Enumerable.Range(0, bodies.Count).GroupBy(Find).Where(g => g.Count() > 1))
            {
                var members = group.ToList();
                for (int x = 0; x < members.Count; x++)
                {
                    for (int y = x + 1; y < members.Count; y++) Scene.NoCollide.Add((bodies[members[x]], bodies[members[y]]));
                }
            }
        }

        private void AddJoint(WorldDocument world, WorldJoint j)
        {
            if (!_items.TryGetValue(j.ObjectA, out var child)) return;
            SceneBody? parent = null;
            if (j.ObjectB != null && _items.TryGetValue(j.ObjectB, out var p))
            {
                parent = !string.IsNullOrEmpty(j.PortB) && p.PortName == j.PortB && p.Rotor != null ? p.Rotor : p.Body;
            }

            var anchor = j.AnchorX == 0 && j.AnchorY == 0 && j.AnchorZ == 0 ? child.Body.Position : new Vec3(j.AnchorX, j.AnchorY, j.AnchorZ);
            var axis = new Vec3(j.AxisX, j.AxisY, j.AxisZ);
            if (axis.Length < 1e-9) axis = Vec3.UnitZ;
            var kind = j.Kind switch { WorldJointKind.Hinge => JointKind.Hinge, WorldJointKind.Slider => JointKind.Slider, _ => JointKind.Fixed };
            kind = CasterKind(kind, child, parent); // [phys] a ball held in a socket rolls freely
            // Parent first so Angle measures the child.
            var joint = parent != null ? Scene.AddJoint(kind, parent, child.Body, anchor, axis.Normalized(), j.Id) : Scene.AddJoint(kind, child.Body, null, anchor, axis.Normalized(), j.Id);
            if (j.Lower.HasValue) joint.Lower = kind == JointKind.Hinge ? j.Lower.Value * Math.PI / 180 : j.Lower.Value;
            if (j.Upper.HasValue) joint.Upper = kind == JointKind.Hinge ? j.Upper.Value * Math.PI / 180 : j.Upper.Value;
            if (j.BreakForce > 0) joint.BreakForce = j.BreakForce;
        }

        /// <summary>Advances the mechanics by <paramref name="dt"/> (accumulated into fixed steps) and couples actuators to the circuit.</summary>
        public void Advance(double dt, AnalogSimulator? sim)
        {
            _pending += dt;
            while (_pending >= Step)
            {
                _pending -= Step;
                DriveActuators(sim);
                StepCables(Step); // [phys] ropes pull on their plugs before the bodies move
                DriveReady();   // [objs] propeller speeds, omni/mecanum roller traction
                StepMachines(sim);   // [arm]
                Sdk?.BeforeStep(Step, sim);   // [sdk]
                StepWings();   // [aero] wings, tails, fins (Sandbox/WorldPhysics.Wings.cs)
                Scene.Step(Step);
                FeedBack();
                FeedBackReady();   // [objs] propeller load without the hub's inertia
                Sdk?.AfterStep(Step, sim);   // [sdk]
                if (++_sonarTick % 5 == 0) Ranging();
            }
        }

        private long _sonarTick;

        /// <summary>
        /// Ultrasonic sensors measure the distance to whatever their beam meets in the world
        /// (other objects, obstacles, the floor when tilted); their own assembly is ignored.
        /// </summary>
        private void Ranging()
        {
            foreach (var it in _items.Values)
            {
                if (it.Sonar == null) continue;
                var pose = PoseOf(it.Object.Id);
                if (pose == null) continue;
                var (pos, q) = pose.Value;
                var origin = pos + q.Rotate(it.SonarOrigin);
                var dir = q.Rotate(it.SonarAxis);
                var own = it.Body;
                var rotor = it.Rotor;
                double max = it.Sonar.MaxRange + 0.5;
                if (Scene.Raycast(origin, dir, max, out var hit, b => b != own && b != rotor && !SameAssembly(own, b)))
                {
                    it.SonarRange = hit.Distance;
                    it.SonarHit = hit.Point;
                }
                else
                {
                    it.SonarRange = double.PositiveInfinity;
                    it.SonarHit = origin + dir * max;
                }

                it.Sonar.Distance = Math.Min(it.SonarRange, max);
            }
        }

        private bool SameAssembly(SceneBody a, SceneBody b)
        {
            foreach (var (x, y) in Scene.NoCollide)
            {
                if ((x == a && y == b) || (x == b && y == a)) return true;
            }

            return false;
        }

        /// <summary>Beam of an object's ultrasonic sensor: start, end (hit or max range) and the measured distance.</summary>
        public (Vec3 From, Vec3 To, double Range)? SonarBeam(string objectId)
        {
            if (!_items.TryGetValue(objectId, out var it) || it.Sonar == null || double.IsNaN(it.SonarRange)) return null;
            var pose = PoseOf(objectId);
            if (pose == null) return null;
            var (pos, q) = pose.Value;
            return (pos + q.Rotate(it.SonarOrigin), it.SonarHit, it.SonarRange);
        }

        private void DriveActuators(AnalogSimulator? sim)
        {
            foreach (var it in _items.Values)
            {
                if (it.RotorJoint == null) continue;
                if (DriveOverride != null && DriveOverride(it.Object.Id, it.RotorJoint)) continue;   // [api] automation drives this port directly
                if (it.Servo != null && it.ServoMotor != null)
                {
                    var m = it.ServoMotor;
                    m.SupplyVoltage = sim == null || _circuit == null ? 5.0
                        : _circuit.Compiled.PinVoltage(sim, it.ServoRef!, "V+") - _circuit.Compiled.PinVoltage(sim, it.ServoRef!, "GND");
                    double target = it.Servo.TargetAngleDeg;
                    // Horn angle 0 = the servo's centre (90°).
                    m.CommandAngle = double.IsNaN(target) ? double.NaN : (target - 90.0) * Math.PI / 180;
                    m.Drive(it.RotorJoint);
                }
                else if (it.Motor != null)
                {
                    // The shaft turns at the electrically computed output speed; the drive reports what it costs.
                    it.RotorJoint.Drive = JointDrive.Velocity;
                    it.RotorJoint.TargetSpeed = it.Motor.AngularVelocity / Math.Max(1e-9, it.Motor.GearRatio);
                    it.RotorJoint.Damping = 0;
                    it.RotorJoint.MaxTorque = Math.Abs(it.Motor.MotorConstant * 3.0 * it.Motor.GearRatio) + 1e-3;
                }
                else
                {
                    it.RotorJoint.Drive = JointDrive.None;
                    it.RotorJoint.Friction = 0.002;
                }
            }
        }

        private void FeedBack()
        {
            foreach (var it in _items.Values)
            {
                if (it.RotorJoint == null) continue;
                if (it.Servo != null && it.ServoMotor != null)
                {
                    it.Servo.DrivenAngleDeg = 90.0 + it.RotorJoint.Angle * 180 / Math.PI;
                    it.Servo.DrivenCurrent = it.ServoMotor.UpdateCurrent(it.RotorJoint);
                }
                else if (it.Motor != null)
                {
                    // Torque the shaft needed = load on the gearbox output.
                    it.Motor.LoadTorque = it.RotorJoint.DriveTorque;
                }
            }
        }

        /// <summary>Shortest rotation taking unit vector a to unit vector b.</summary>
        private static Quat FromTo(Vec3 a, Vec3 b)
        {
            var c = a.Cross(b);
            double d = a.Dot(b);
            if (d < -0.999999) return Quat.FromAxisAngle(Math.Abs(a.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY, Math.PI);
            return new Quat(1 + d, c.X, c.Y, c.Z).Normalized();
        }

        // ================================================================ readouts

        /// <summary>Current pose of an object's frame (origin = its pose point).</summary>
        public (Vec3 Position, Quat Orientation)? PoseOf(string objectId)
        {
            if (!_items.TryGetValue(objectId, out var it)) return null;
            var q = it.Body.Orientation * it.ShapeTurn.Conjugate;
            return (it.Body.Position - q.Rotate(it.Centre), q);
        }

        /// <summary>Angle of an object's servo horn / motor shaft relative to its body (rad).</summary>
        public double PortAngle(string objectId) =>
            _items.TryGetValue(objectId, out var it) && it.RotorJoint != null ? it.RotorJoint.Angle : 0;

        public IEnumerable<PhysicsEvent> DrainEvents() => Scene.DrainEvents();
    }
}
