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

        /// <summary>"box", "sphere", "cylinder-x/y/z" (rolls about that local axis), "hull" or "mesh".</summary>
        public string Shape { get; set; } = "box";

        public MeshModel? Mesh { get; set; }

        /// <summary>
        /// Shape "hull": points of the part's own 3D model (object frame, metres) whose convex hull is
        /// the rigid body, so a round motor rolls and a tapered wing is tapered instead of a bounding box.
        /// </summary>
        public List<Vec3>? Hull { get; set; }

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
            public List<SceneBody>? Walls;   // [shell] Sandbox/WorldPhysics.Shells.cs
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

        private readonly Dictionary<string, double> _servoHold = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// [kin] A servo command (°, 0–180) that replaces the firmware's pulses, as a servo tester on the signal wire does;
        /// null gives the signal back. The servo still moves with its own motor, its speed and torque limits and its supply.
        /// </summary>
        public void HoldServo(string objectId, double? degrees)
        {
            if (degrees is { } d) _servoHold[objectId] = Math.Max(0, Math.Min(180, d));
            else _servoHold.Remove(objectId);
        }

        /// <summary>[kin] The servos whose command is held (object ids).</summary>
        public IReadOnlyCollection<string> HeldServos => _servoHold.Keys;

        private WorldPhysics(WorldCircuit? circuit)
        {
            _circuit = circuit;
        }

        public static WorldPhysics Build(WorldDocument world, Func<WorldObject, WorldBodyGeometry> geometry, WorldCircuit? circuit, LiquidQuality? liquid = null)
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
            {
                if (IsLiquid(o)) continue;   // [liquid] particles, built once every solid stands (Sandbox/WorldPhysics.Liquids.cs)
                if (!p.TryAddProp(o)) p.AddObject(o, geometry(o)); // [terrain] static props collide piecewise
            }
            p.BuildMachines(world);   // [arm] robot arms, tools, conveyors, light curtains
            foreach (var j in world.Joints) p.AddJoint(world, j);
            p.CoupleMimics(world);   // [urdf] mimic joints follow their masters
            p.CoupleJoints(world);   // [couple] gears, belts, lead screws (Sandbox/WorldPhysics.Couplings.cs)
            p.FinishReady(world);   // [objs] propellers on shafts, light rotors
            p.Liquid = liquid ?? LiquidQuality.Live;
            p.BuildLiquids(world, geometry);   // [liquid] water, oil, honey … as particles (Sandbox/WorldPhysics.Liquids.cs)
            p.FilterAssemblyCollisions(); // only parts within two joints of each other skip collisions (WorldPhysics.Overlap.cs)
            p.ScrewLids();   // [shell] lids seated on their shell's rim are screwed down (Sandbox/WorldPhysics.Shells.cs)
            p.Sdk = RobotTwin.CoreSim.Sdk.WorldSdkRuntime.Attach(world, p, circuit);   // [sdk] environment, force fields, behaviours
            p.BuildWings();   // [aero] lifting surfaces replace the generic drag the environment just gave every body (Sandbox/WorldPhysics.Wings.cs)
            p.ShieldEnclosedDrag();   // [drag] parts inside a closed shell are out of the airflow (Sandbox/WorldPhysics.Drag.cs)
            p.ApplyLaunches(world);   // [aero] hand-launched aircraft
            p.BuildThermal(world);   // [thermal] part temperatures, softening, melting (Sandbox/WorldPhysics.Thermal.cs)
            p.BuildGas(world);   // [gas] pressure vessels, propellant grains, nozzles, water (Sandbox/WorldPhysics.Gas.cs)
            p.BuildChutes();   // [launch] recovery parachutes (Sandbox/WorldPhysics.Launch.cs)
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
            "CAM" or "GLASS" => "glass",
            "AKRİLİK" or "AKRILIK" => "acrylic",
            // Any other name the material table knows ("rubber", "kağıt", "steel" from the API or an import) is that
            // material; it used to become PLA, with PLA's friction, density and reflectance.
            _ => Materials.IsKnown(o.Material) ? Materials.Canonical(o.Material) : "pla"
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
                case var _ when IsShell(o):   // [shell] hollow body: floor here, walls below
                    body = ShellFloor(o, size, centre, mass, material, it);
                    break;
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
                    body = SceneBody.FromMesh(o.Id, g.Mesh, null, mass, material, concave: true);   // hollow models hold things
                    it.Centre = body.ModelOffset;
                    break;
                case "hull" when HullBody(o.Id, g.Hull, mass, material) is { } hull:
                    body = hull;
                    it.Centre = body.ModelOffset;
                    break;
                default:
                    body = SceneBody.Box(o.Id, size, mass, default, material);
                    break;
            }

            if (g.Shape is "cylinder-z" or "cylinder-x" or "cylinder-y" && Param(o, "tube.wall", 0) > 0) MakeTube(body, Param(o, "tube.wall", 0), Param(o, "tube.bottom", 0));   // [tube]

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
            if (IsShell(o)) AddShellWalls(it, size, mass, material);   // [shell]

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

        /// <summary>
        /// Rigid body whose collision shape is the convex hull of <paramref name="points"/> (object frame),
        /// mass spread uniformly through it; null when the points span no volume.
        /// </summary>
        public static SceneBody? HullBody(string id, IReadOnlyList<Vec3>? points, double mass, string material)
        {
            if (points == null || points.Count < 4) return null;
            var hull = ConvexHull.Compute(points);
            if (hull.IsDegenerate || hull.Vertices.Count < 4) return null;
            try
            {
                // Round parts need enough contact points to roll smoothly rather than as a coarse polygon.
                return SceneBody.FromMesh(id, hull.ToMesh(), null, mass, material, default, 64);
            }
            catch (ArgumentException)
            {
                return null;
            }
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
            if (j.Friction > 0 && kind != JointKind.Fixed) joint.Friction = j.Friction;   // [phys] bushing / bearing friction
            AddSpring(j, joint);   // [spring] torsion / coil spring and damper (Sandbox/WorldPhysics.Springs.cs)
            if (kind is JointKind.Hinge or JointKind.Slider) joint.LoadFriction = Bearings.LoadFriction(j);   // [bearing] Sandbox/Bearings.cs
            if (j.BreakForce > 0) joint.BreakForce = j.BreakForce;
            else if (kind == JointKind.Fixed && j.Fastening.Length > 0)
            {
                // [fasten] screws by count, adhesives by strength over the bonded area (Sandbox/Fastening.cs)
                var spec = Fastening.For(j.Fastening);
                double area = Math.Min(FaceOf(child.Body), parent != null ? FaceOf(parent) : double.MaxValue);
                double force = spec.BreakForce(area);
                if (force > 0)
                {
                    joint.BreakForce = force;
                    _fastened.Add(new Fastened { Joint = joint, Spec = spec, Force = force, A = j.ObjectA, B = j.ObjectB });
                }
            }

            // [weld] A fixed joint that cannot break is a rigid bond: both parts are solved as one rigid body.
            joint.Weld = kind == JointKind.Fixed && double.IsPositiveInfinity(joint.BreakForce);
            if (j.Driven && kind != JointKind.Fixed) AddDrive(j, joint, kind == JointKind.Hinge);
        }

        // ================================================================ [drive] motors on hinges and sliders

        /// <summary>A motor on a world joint: its target in the joint's own units (rad or m; rad/s or m/s for a speed drive).</summary>
        private sealed class JointMotor
        {
            public SceneJoint Joint = null!;
            public bool Hinge, Speed;
            public double Target, MaxSpeed;
            public SceneJoint? Master;   // [urdf] a mimic joint: Target = Factor × Master + Offset
            public double Factor = 1, Offset;
        }

        /// <summary>[urdf] Mimic joints get their master's scene joint once every joint is built.</summary>
        private void CoupleMimics(WorldDocument world)
        {
            foreach (var j in world.Joints.Where(j => j.MimicOf != null))
            {
                if (!_motors.TryGetValue(j.Id, out var m) || m.Speed) continue;
                m.Master = Scene.Joints.FirstOrDefault(s => s.Id == j.MimicOf);
                m.Factor = j.MimicFactor ?? 1;
                m.Offset = j.MimicOffset;
            }
        }

        private readonly Dictionary<string, JointMotor> _motors = new Dictionary<string, JointMotor>(StringComparer.Ordinal);

        private void AddDrive(WorldJoint j, SceneJoint joint, bool hinge)
        {
            double unit = hinge ? Math.PI / 180 : 1;   // the document keeps degrees for hinges
            // Like a servo axis (and the cell's robot arms): a rigid velocity constraint, as strong as the drive's peak torque,
            // whose speed a position loop sets every step.
            joint.Drive = JointDrive.Velocity;
            joint.Damping = 0;
            joint.TargetSpeed = 0;
            joint.MaxTorque = j.DriveForce > 0 ? j.DriveForce : hinge ? 10 : 200;
            _motors[j.Id] = new JointMotor
            {
                Joint = joint, Hinge = hinge, Speed = j.Drive == WorldJoint.DriveSpeedMode,
                Target = j.DriveTarget * unit,
                MaxSpeed = (j.DriveSpeed > 0 ? j.DriveSpeed : hinge ? 180 : 0.5) * unit
            };
        }

        /// <summary>Position gain of the drives' loop (1/s): an error of 1° asks for 50 °/s, up to the drive's top speed.</summary>
        private const double DriveGain = 50;

        private void DriveJoints()
        {
            foreach (var m in _motors.Values)
            {
                if (m.Speed)
                {
                    m.Joint.TargetSpeed = m.Target;
                    continue;
                }

                if (m.Master != null) m.Target = m.Factor * m.Master.Angle + m.Offset;
                double v = DriveGain * (m.Target - m.Joint.Angle);
                m.Joint.TargetSpeed = Math.Max(-m.MaxSpeed, Math.Min(m.MaxSpeed, v));
            }
        }

        /// <summary>The world joints that have a drive.</summary>
        public IEnumerable<string> DrivenJoints => _motors.Keys;

        /// <summary>
        /// [drive] Sets a joint drive's target while running: an angle (°) or travel (m) for a position drive, a speed (°/s or
        /// m/s) for a speed drive. False when the joint has no drive.
        /// </summary>
        public bool SetJointTarget(string jointId, double value)
        {
            if (!_motors.TryGetValue(jointId, out var m)) return false;
            m.Target = m.Hinge ? value * Math.PI / 180 : value;
            return true;
        }

        /// <summary>A driven joint's target and where it is now, in the document's units (° or m; °/s or m/s); null without a drive.</summary>
        public (double Target, double Now)? JointDriveState(string jointId)
        {
            if (!_motors.TryGetValue(jointId, out var m)) return null;
            double unit = m.Hinge ? 180 / Math.PI : 1;
            return (m.Target * unit, m.Joint.Angle * unit);
        }

        private sealed class Fastened
        {
            public SceneJoint Joint = null!;
            public Fastening Spec = null!;
            public double Force;
            public string A = string.Empty;
            public string? B;
            public bool Softening, Gone;
        }

        private readonly List<Fastened> _fastened = new List<Fastened>();

        private static double FaceOf(SceneBody body)
        {
            var s = SizeOf(body);
            return Math.Max(s.X * s.Y, Math.Max(s.X * s.Z, s.Y * s.Z));
        }

        /// <summary>[fasten] Glues and plastics soften as the joined parts warm up; past the failure temperature the joint lets go.</summary>
        private void WeakenFastenings()
        {
            foreach (var f in _fastened)
            {
                if (f.Joint.Broken || f.Gone || double.IsNaN(f.Spec.SoftenC)) continue;
                double t = f.B != null ? (TemperatureOf(f.A) + TemperatureOf(f.B)) / 2 : TemperatureOf(f.A);
                double k = f.Spec.StrengthFactor(t);
                f.Joint.BreakForce = Math.Max(1e-3, f.Force * k);
                if (k < 1)
                {
                    // A weakening joint is loaded: keep its bodies awake so the solver measures the load on it.
                    Scene.Wake(f.Joint.ChildBody);
                    if (f.Joint.ParentBody != null) Scene.Wake(f.Joint.ParentBody);
                }
                if (k < 1 && !f.Softening)
                {
                    f.Softening = true;
                    Scene.Report(new PhysicsEvent(Scene.Time, f.A, PhysicsEventKind.Impact, $"{f.Spec.Name}: {t:0} °C, bağlantı zayıflıyor (dayanım %{k * 100:0})", t, false));
                }

                if (k <= 0)
                {
                    f.Gone = true;
                    f.Joint.Broken = true;   // nothing left to hold
                    Scene.Report(new PhysicsEvent(Scene.Time, f.A, PhysicsEventKind.BodyBroken, $"{f.Spec.Name} {t:0} °C'de tuttuğunu bıraktı", t, true));
                }
            }
        }

        /// <summary>Advances the mechanics by <paramref name="dt"/> (accumulated into fixed steps) and couples actuators to the circuit.</summary>
        public void Advance(double dt, AnalogSimulator? sim)
        {
            _pending += dt;
            while (_pending >= Step)
            {
                _pending -= Step;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                StepSwitches();   // [launch] timed and clicked switches (Sandbox/WorldPhysics.Launch.cs)
                DriveJoints();   // [drive] motors on hinges and sliders (imported robots' axes)
                DriveActuators(sim);
                DriveReady();   // [objs] propeller speeds, omni/mecanum roller traction
                StepMachines(sim);   // [arm]
                long t1 = Lap(ref Times.Drives, t0);
                Sdk?.BeforeStep(Step, sim);   // [sdk]
                t1 = Lap(ref Times.Scripts, t1);
                ReleaseLaunches();   // [uav] hand launch after a hold
                StepWings(Step);   // [aero] wings, tails, fins (Sandbox/WorldPhysics.Wings.cs)
                StepGas(Step, sim);   // [gas] vessels: burn, flow, thrust (Sandbox/WorldPhysics.Gas.cs)
                StepChutes();   // [launch] recovery parachutes
                t1 = Lap(ref Times.Air, t1);
                Scene.Step(Step);
                t1 = Lap(ref Times.Bodies, t1);
                // [phys] The ropes after the bodies: their contacts are solved against where the parts are now (stepped
                // first, a wire inside a plane at 12 m/s trailed its fuselage by 12 mm), and their pull on the plugs is
                // applied in the next step.
                StepCables(Step);
                t1 = Lap(ref Times.Cables, t1);
                StepLiquids(Step);   // [liquid] after the bodies too; its push on them acts in their next step
                t1 = Lap(ref Times.Liquids, t1);
                FeedBack();
                FeedBackReady();   // [objs] propeller load without the hub's inertia
                t1 = Lap(ref Times.Drives, t1);
                Sdk?.AfterStep(Step, sim);   // [sdk]
                t1 = Lap(ref Times.Scripts, t1);
                StepThermal(Step, sim);   // [thermal]
                t1 = Lap(ref Times.Thermal, t1);
                if (++_sonarTick % 5 == 0) Ranging();
                Lap(ref Times.Sensors, t1);
                Times.Steps++;
            }
        }

        private static long Lap(ref double seconds, long since)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            seconds += (now - since) / (double)System.Diagnostics.Stopwatch.Frequency;
            return now;
        }

        /// <summary>[perf] Wall-clock seconds the mechanics spent in each of its parts since the world was built.</summary>
        public PhysicsTimes Times { get; } = new PhysicsTimes();

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
                    double target = _servoHold.TryGetValue(it.Object.Id, out double held) ? held : it.Servo.TargetAngleDeg;   // [kin]
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
                    it.RotorJoint.MaxTorque = Math.Abs(it.Motor.MotorConstant * it.Motor.GearRatio) * Math.Max(3.0, 1.5 * Math.Abs(it.Motor.Current) + 1.0) + 1e-3;   // [esc] what the winding current can deliver (a brushless motor draws tens of amps)
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

        /// <summary>
        /// [ros] An object's frame in motion (world): its pose, the velocity of its origin and the angular velocity (rad/s).
        /// What odometry from ground truth reports.
        /// </summary>
        public (Vec3 Position, Quat Orientation, Vec3 Velocity, Vec3 AngularVelocity)? MotionOf(string objectId)
        {
            if (!_items.TryGetValue(objectId, out var it)) return null;
            var q = it.Body.Orientation * it.ShapeTurn.Conjugate;
            var p = it.Body.Position - q.Rotate(it.Centre);
            return (p, q, it.Body.PointVelocity(p), it.Body.AngularVelocity);
        }

        /// <summary>
        /// [ros] How far an object's solid reaches from an axis through its centre (a world direction): a wheel's rolling
        /// radius (m), as a drive controller is told it. Null when the object has no solid.
        /// </summary>
        public double? RollingRadius(string objectId, Vec3 worldAxis)
        {
            if (!_items.TryGetValue(objectId, out var it) || it.Body.EffectiveShape is not { } s) return null;
            var a = it.Body.Orientation.InverseRotate(worldAxis).Normalized();
            if (s.Kind == ShapeKind.Sphere) return s.Radius;
            if (s.Kind == ShapeKind.Cylinder && Math.Abs(a.Z) > 0.9) return s.Radius;
            double reach = 0;
            if (s.Kind == ShapeKind.Box)
            {
                var h = s.HalfExtents;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vec3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
                    reach = Math.Max(reach, (c - a * c.Dot(a)).Length);
                }

                return reach;
            }

            foreach (var v in s.Vertices) reach = Math.Max(reach, (v - a * v.Dot(a)).Length);
            return reach > 0 ? reach : s.BoundingRadius;
        }

        /// <summary>Angle of an object's servo horn / motor shaft relative to its body (rad).</summary>
        public double PortAngle(string objectId) =>
            _items.TryGetValue(objectId, out var it) && it.RotorJoint != null ? it.RotorJoint.Angle : 0;

        /// <summary>[kin] The hinge of an object's servo horn / motor shaft (its angle over turns and its speed), or null.</summary>
        public SceneJoint? PortJoint(string objectId) => _items.TryGetValue(objectId, out var it) ? it.RotorJoint : null;

        public IEnumerable<PhysicsEvent> DrainEvents() => Scene.DrainEvents();
    }
}
