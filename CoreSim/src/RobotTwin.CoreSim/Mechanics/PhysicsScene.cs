using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    public enum JointKind
    {
        /// <summary>Welds the bodies together.</summary>
        Fixed,

        /// <summary>Revolute joint: one rotational degree of freedom about the axis.</summary>
        Hinge,

        /// <summary>Prismatic joint: one translational degree of freedom along the axis.</summary>
        Slider,

        /// <summary>Spherical joint: anchors coincide, rotation is free.</summary>
        Ball
    }

    public enum JointDrive
    {
        None,

        /// <summary>Spring–damper toward <see cref="SceneJoint.TargetAngle"/> (PD position control).</summary>
        Position,

        /// <summary>Toward <see cref="SceneJoint.TargetSpeed"/>: rigid (Damping = 0) or a damper with gain Damping (DC-motor-like).</summary>
        Velocity
    }

    public enum PhysicsEventKind
    {
        Impact,
        BodyBroken,
        JointBroken,
        Detached
    }

    public sealed class PhysicsEvent
    {
        public PhysicsEvent(double time, string source, PhysicsEventKind kind, string message, double value, bool severe)
        {
            Time = time;
            Source = source;
            Kind = kind;
            Message = message;
            Value = value;
            Severe = severe;
        }

        public double Time { get; }
        public string Source { get; }
        public PhysicsEventKind Kind { get; }
        public string Message { get; }

        /// <summary>Force (N) or torque (N·m) that caused the event.</summary>
        public double Value { get; }

        public bool Severe { get; }
        public override string ToString() => $"{Time:F3} {Source}: {Message}";
    }

    /// <summary>Result of <see cref="PhysicsScene.Raycast"/>.</summary>
    public struct SceneRayHit
    {
        public double Distance;
        public Vec3 Point;
        public Vec3 Normal;

        /// <summary>The body hit, or null for the ground.</summary>
        public SceneBody? Body;

        public string Material;
    }

    /// <summary>
    /// A rigid body of a <see cref="PhysicsScene"/>: its origin is the centre of mass, the
    /// collision <see cref="RigidBody.Shape"/> and the mass properties are in that frame.
    /// </summary>
    public sealed class SceneBody : RigidBody
    {
        public SceneBody(string id = "body")
        {
            Id = id;
            LinearDrag = 0;
            AngularDrag = 0;
        }

        public string Id { get; set; }

        /// <summary>Centre of mass in the source model's frame (meshes); world = Position + R·(p − ModelOffset).</summary>
        public Vec3 ModelOffset { get; set; }

        /// <summary>Contact force (N) above which the body fractures (raises an incident, sets <see cref="Broken"/>).</summary>
        public double BreakImpactForce { get; set; } = double.PositiveInfinity;

        public bool Broken { get; internal set; }

        /// <summary>Places the source model's origin at <paramref name="modelOrigin"/> with the given orientation.</summary>
        public void PlaceModel(Vec3 modelOrigin, Quat orientation)
        {
            Orientation = orientation;
            Position = modelOrigin + orientation.Rotate(ModelOffset);
        }

        public Vec3 ModelToWorld(Vec3 modelPoint) => Position + Orientation.Rotate(modelPoint - ModelOffset);

        public double KineticEnergy
        {
            get
            {
                if (IsStatic) return 0;
                var frame = Orientation * PrincipalAxes;
                var w = frame.InverseRotate(AngularVelocity);
                return 0.5 * Mass * Velocity.LengthSquared + 0.5 * w.Dot(Inertia.Mul(w));
            }
        }

        public Vec3 LinearMomentum => IsStatic ? Vec3.Zero : Velocity * Mass;

        /// <summary>Angular momentum about the body's own centre of mass (world frame).</summary>
        public Vec3 SpinMomentum
        {
            get
            {
                var frame = Orientation * PrincipalAxes;
                return frame.Rotate(Inertia.Mul(frame.InverseRotate(AngularVelocity)));
            }
        }

        // ------------------------------------------------------------------ factories

        private static SceneBody Make(string id, MassProperties mp, CollisionShape shape, string material, Vec3 position)
        {
            var b = new SceneBody(id) { Shape = shape, Material = Materials.Canonical(material), Position = position };
            b.SetMassProperties(mp);
            return b;
        }

        public static SceneBody Box(string id, Vec3 size, double mass, Vec3 position = default, string material = Materials.Default) =>
            Make(id, MassProperties.Box(mass, size), CollisionShape.Box(size), material, position);

        public static SceneBody Sphere(string id, double radius, double mass, Vec3 position = default, string material = Materials.Default)
        {
            var b = Make(id, MassProperties.Sphere(mass, radius), CollisionShape.Sphere(radius), material, position);
            b.DragArea = new Vec3(1, 1, 1) * (0.47 * Math.PI * radius * radius);
            return b;
        }

        /// <summary>Solid cylinder along the body z axis (rotate it for a wheel).</summary>
        public static SceneBody Cylinder(string id, double radius, double height, double mass, Vec3 position = default, string material = Materials.Default) =>
            Make(id, MassProperties.Cylinder(mass, radius, height), CollisionShape.Cylinder(radius, height), material, position);

        /// <summary>
        /// Dynamic body from a model: exact mass properties (closed mesh) from density or a weighed
        /// mass, collision from the convex hull. Placed with its model origin at
        /// <paramref name="modelOrigin"/>.
        /// </summary>
        public static SceneBody FromMesh(string id, MeshModel mesh, double? densityKgPerM3 = null, double? massKg = null, string material = Materials.Default,
            Vec3 modelOrigin = default, int maxHullVertices = 32)
        {
            string mat = Materials.Canonical(material);
            var mp = massKg is double m && m > 0
                ? MassProperties.FromMeshWithMass(mesh, m)
                : MassProperties.FromMesh(mesh, densityKgPerM3 ?? Materials.Get(mat).Density);
            var com = mp.CenterOfMass;
            var pts = new List<Vec3>(mesh.Vertices.Count);
            foreach (var v in mesh.Vertices) pts.Add(v - com);
            var b = Make(id, mp, CollisionShape.ConvexHull(pts, maxHullVertices), mat, modelOrigin + com);
            b.ModelOffset = com;
            return b;
        }

        /// <summary>Static box; <paramref name="center"/> is the box centre.</summary>
        public static SceneBody StaticBox(string id, Vec3 size, Vec3 center, Quat orientation, string material = "wood")
        {
            var b = new SceneBody(id) { IsStatic = true, Shape = CollisionShape.Box(size), Material = Materials.Canonical(material), Position = center, Orientation = orientation };
            b.Shape.MarkFloorFaces(center, orientation);
            return b;
        }

        /// <summary>Static convex solid from world-space points (ramps, imported convex parts).</summary>
        public static SceneBody StaticHull(string id, IReadOnlyList<Vec3> worldPoints, string material = "wood")
        {
            var c = Vec3.Zero;
            foreach (var p in worldPoints) c += p;
            c /= Math.Max(1, worldPoints.Count);
            var local = new List<Vec3>(worldPoints.Count);
            foreach (var p in worldPoints) local.Add(p - c);
            var b = new SceneBody(id) { IsStatic = true, Shape = CollisionShape.ConvexHull(local, 64), Material = Materials.Canonical(material), Position = c };
            b.Shape.MarkFloorFaces(c, Quat.Identity);
            return b;
        }

        /// <summary>Static arbitrary (concave) mesh given in world coordinates, collided through its BVH.</summary>
        public static SceneBody StaticMesh(string id, MeshModel worldMesh, string material = "wood") =>
            new SceneBody(id) { IsStatic = true, Shape = CollisionShape.TriangleMesh(worldMesh), Material = Materials.Canonical(material) };
    }

    /// <summary>
    /// Joint between body A and body B (B = null: the world). Internally the parent is A and
    /// the child B; with no B the world is the parent and A the child, so <see cref="Angle"/>
    /// always measures the moving part relative to what it hangs on.
    /// </summary>
    public sealed class SceneJoint
    {
        internal int ParentSlot = -1, ChildSlot = -1;
        internal SceneBody? ParentBody;
        internal SceneBody ChildBody;
        internal Vec3 AnchorP, AnchorC; // parent frame (or world), child frame
        internal Vec3 AxisP, AxisC;
        internal Quat Rest;             // qP⁻¹·qC at creation
        internal double PrevWrapped;
        internal readonly double[] Impulse = new double[11];
        internal Vec3 PointImpulse;
        internal double ForceFilter, TorqueFilter, StepDriveImpulse, SubDrive;
        internal Vec3 StepForceImpulse, StepTorqueImpulse, SubForce, SubTorque;

        public SceneJoint(string id, JointKind kind, SceneBody a, SceneBody? b, Vec3 worldAnchor, Vec3 worldAxis)
        {
            Id = id;
            Kind = kind;
            A = a;
            B = b;
            ParentBody = b != null ? a : null;
            ChildBody = b ?? a;
            var axis = worldAxis.Length > 1e-12 ? worldAxis.Normalized() : Vec3.UnitZ;
            var qP = ParentBody?.Orientation ?? Quat.Identity;
            var xP = ParentBody?.Position ?? Vec3.Zero;
            AnchorP = qP.InverseRotate(worldAnchor - xP);
            AnchorC = ChildBody.Orientation.InverseRotate(worldAnchor - ChildBody.Position);
            AxisP = qP.InverseRotate(axis);
            AxisC = ChildBody.Orientation.InverseRotate(axis);
            Rest = qP.Conjugate * ChildBody.Orientation;
        }

        public string Id { get; set; }
        public JointKind Kind { get; }
        public SceneBody A { get; }
        public SceneBody? B { get; }

        /// <summary>Anchor on A in A's frame.</summary>
        public Vec3 LocalAnchorA => B != null ? AnchorP : AnchorC;

        /// <summary>Anchor on B in B's frame (world point when B is null).</summary>
        public Vec3 LocalAnchorB => B != null ? AnchorC : AnchorP;

        public Vec3 LocalAxisA => B != null ? AxisP : AxisC;
        public bool Enabled { get; set; } = true;
        public bool CollideConnected { get; set; }

        /// <summary>Lower/upper limit of the angle (rad) or position (m); null = free.</summary>
        public double? Lower { get; set; }

        public double? Upper { get; set; }

        /// <summary>Hinge angle (rad, continuous over turns) of the child relative to the parent.</summary>
        public double Angle { get; internal set; }

        /// <summary>Slider position along the axis (m); same value as <see cref="Angle"/> for a slider.</summary>
        public double Position => Angle;

        /// <summary>Relative angular (rad/s) or linear (m/s) speed along the axis.</summary>
        public double Speed { get; internal set; }

        /// <summary>External torque (N·m; force in N for a slider) applied along the axis: +to the child, −to the parent.</summary>
        public double MotorTorque { get; set; }

        /// <summary>Alias of <see cref="MotorTorque"/> for sliders.</summary>
        public double MotorForce
        {
            get => MotorTorque;
            set => MotorTorque = value;
        }

        public JointDrive Drive { get; set; }
        public double TargetAngle { get; set; }

        public double TargetPosition
        {
            get => TargetAngle;
            set => TargetAngle = value;
        }

        public double TargetSpeed { get; set; }

        /// <summary>Position gain Kp (N·m/rad or N/m).</summary>
        public double Stiffness { get; set; }

        /// <summary>Velocity gain Kd (N·m·s/rad or N·s/m).</summary>
        public double Damping { get; set; }

        public double MaxTorque { get; set; } = double.PositiveInfinity;

        /// <summary>Coulomb friction torque (force) in the joint.</summary>
        public double Friction { get; set; }

        /// <summary>Torque/force the built-in drive delivered, averaged over the last step.</summary>
        public double DriveTorque { get; internal set; }

        /// <summary>Constraint force on the child at the anchor (world, N), averaged over the last step.</summary>
        public Vec3 ReactionForce { get; internal set; }

        /// <summary>Constraint torque on the child (world, N·m), averaged over the last step.</summary>
        public Vec3 ReactionTorque { get; internal set; }

        /// <summary>|reaction force| filtered over ~1 ms (what a strain gauge would read).</summary>
        public double ReactionLoad => ForceFilter;

        public double BreakForce { get; set; } = double.PositiveInfinity;
        public double BreakTorque { get; set; } = double.PositiveInfinity;
        public bool Broken { get; internal set; }

        /// <summary>World anchor on the child.</summary>
        public Vec3 WorldAnchor => ChildBody.ToWorld(AnchorC);

        public Vec3 WorldAxis => ParentBody != null ? ParentBody.Orientation.Rotate(AxisP) : AxisP;
    }

    /// <summary>
    /// General rigid multi-body world: bodies with shapes and materials, joints with limits,
    /// drives and breakage, ground (flat or terrain, material regions), ray-cast tyres and
    /// propellers on any body, air drag and wind. Each <see cref="Step"/> runs fixed substeps of
    /// at most <see cref="MaxSubstep"/>: symplectic (semi-implicit) Euler with an implicit
    /// gyroscopic term, then a soft-step sequential-impulse solve (Catto's "soft step":
    /// soft position correction, relax pass, restitution, warm starting) of contacts with
    /// Coulomb cone friction (static/kinetic), rolling resistance and joints. Deterministic.
    /// </summary>
    public sealed partial class PhysicsScene // [phys] partial: sleeping, CCD, torsional friction, grip in PhysicsScene.Phys.cs
    {
        private struct BodyState
        {
            public Vec3 V, W, X, DX, DTheta;
            public double InvMass;
            public Mat3 InvI;
        }

        private struct Contact
        {
            public int A, B;
            public Vec3 RA, RB, N, T1, T2;
            public double Sep, MassN, MassT1, MassT2, MassR1, MassR2;
            public double Pn, Pt1, Pt2, Pr1, Pr2, MaxPn;
            public double Mu, E, Crr, RollR, Stiff, Vn0;
            public bool Rolling;
            public long Key;
            public double Ps, MassS, SpinR; // [phys] torsional (spin) friction
        }

        private struct WarmImpulse
        {
            public double Pn;
            public Vec3 Pt, Pr;
            public double Ps; // [phys]
        }

        private enum RowType : byte
        {
            Equality,
            Limit,
            Motor,
            Spring
        }

        private struct Row
        {
            public int P, C, Joint, Slot;
            public Vec3 LP, AP, LC, AC;
            public double Mass, Err, Target, Lo, Hi, Gamma, BiasCoef;
            public RowType Type;
            public bool Angular;

            /// <summary>Point block (index + 1) this hinge-axis row is solved together with, and the coupling.</summary>
            public int PointPlus1;

            public Vec3 U;
        }

        private struct PointRow
        {
            public int P, C, Joint;
            public Vec3 RP, RC, Err;
            public Mat3 Mass;
        }

        private const int RolePerp0 = 3, RolePerp1 = 4, RoleAng0 = 5, RoleAng1 = 6, RoleAng2 = 7, RoleLower = 8, RoleUpper = 9, RoleDrive = 10, RoleFriction = 2;

        private BodyState[] _state = new BodyState[16];
        private Contact[] _contacts = new Contact[64];
        private int _contactCount;
        private Row[] _rows = new Row[32];
        private int _rowCount;
        private PointRow[] _points = new PointRow[16];
        private int _pointCount;
        private Dictionary<long, WarmImpulse> _warm = new Dictionary<long, WarmImpulse>();
        private Dictionary<long, WarmImpulse> _warmNext = new Dictionary<long, WarmImpulse>();
        private readonly HashSet<long> _noCollide = new HashSet<long>();
        private int[] _order = new int[16];
        private double[] _minX = new double[16];
        private Vec3[] _lo = new Vec3[16], _hi = new Vec3[16];
        private CollisionShape?[] _shapes = new CollisionShape?[16];
        private readonly List<PhysicsEvent> _events = new List<PhysicsEvent>();
        private readonly Dictionary<RigidBody, double> _lastImpactReport = new Dictionary<RigidBody, double>();
        private readonly List<int> _nearby = new List<int>();
        private readonly Vec3[] _candidates = new Vec3[16];
        private double _h;

        public Vec3 Gravity { get; set; } = new Vec3(0, 0, -9.81);

        /// <summary>Air density (kg/m³): 1.225 sea level, ~0.02 Mars, 0 Moon.</summary>
        public double AirDensity { get; set; } = 1.225;

        /// <summary>Mean wind (m/s, world).</summary>
        public Vec3 Wind { get; set; }

        /// <summary>RMS amplitude of deterministic gusts (m/s).</summary>
        public double GustIntensity { get; set; }

        public bool GroundEnabled { get; set; } = true;

        /// <summary>Ground surface; null = the plane z = 0.</summary>
        public Terrain? Terrain { get; set; }

        public string GroundMaterial
        {
            get => _groundMaterial;
            set
            {
                _groundMaterial = Materials.Canonical(value);
                _groundIndex = Materials.IndexOf(_groundMaterial);
            }
        }

        /// <summary>
        /// When set, every non-caster contact with the ground uses this single friction
        /// coefficient (legacy arenas with one "grip" number) instead of the material table.
        /// </summary>
        public double? GroundFriction { get; set; }

        public List<SurfaceRegion> Regions { get; } = new List<SurfaceRegion>();
        public List<SceneBody> Bodies { get; } = new List<SceneBody>();
        public List<SceneJoint> Joints { get; } = new List<SceneJoint>();
        public List<Wheel> Wheels { get; } = new List<Wheel>();
        public List<Propeller> Propellers { get; } = new List<Propeller>();

        public double MaxSubstep { get; set; } = 5e-4;
        public int Iterations { get; set; } = 4;
        public int RelaxIterations { get; set; } = 2;

        /// <summary>Penetration allowed without push-out (m).</summary>
        public double LinearSlop { get; set; } = 0.0005;

        /// <summary>Contact stiffness of the soft step (clamped to a quarter of the substep rate).</summary>
        public double ContactHertz { get; set; } = 480;
        public double ContactDampingRatio { get; set; } = 10;
        public double JointHertz { get; set; } = 240;
        public double JointDampingRatio { get; set; } = 2;
        public double MaxPushout { get; set; } = 1.0;

        /// <summary>Approach speeds below this do not bounce (m/s).</summary>
        public double RestitutionThreshold { get; set; } = 0.1;

        /// <summary>Tangential speeds below this use static friction (m/s).</summary>
        public double StickVelocity { get; set; } = 0.005;

        /// <summary>Impacts above this many times a body's weight are reported as events.</summary>
        public double ImpactReportG { get; set; } = 15;

        public double Time { get; private set; }
        public int ContactCount => _contactCount;

        /// <summary>Largest contact force (N) in the last step, any body.</summary>
        public double PeakContactForce { get; private set; }

        public IReadOnlyList<PhysicsEvent> PendingEvents => _events;

        public IReadOnlyList<PhysicsEvent> DrainEvents()
        {
            var copy = _events.ToArray();
            _events.Clear();
            return copy;
        }

        internal void Report(PhysicsEvent e) => _events.Add(e);

        /// <summary>Extra body pairs that never collide (parts of one assembly modelled with approximate shapes).</summary>
        public List<(SceneBody A, SceneBody B)> NoCollide { get; } = new List<(SceneBody A, SceneBody B)>();

        public SceneBody AddBody(SceneBody body)
        {
            if (!Bodies.Contains(body)) Bodies.Add(body);
            return body;
        }

        public bool RemoveBody(SceneBody body)
        {
            Joints.RemoveAll(j => j.A == body || j.B == body);
            return Bodies.Remove(body);
        }

        public SceneJoint AddJoint(SceneJoint joint)
        {
            if (!Joints.Contains(joint)) Joints.Add(joint);
            return joint;
        }

        /// <summary>Creates a joint at a world anchor/axis using the bodies' current poses as the rest pose.</summary>
        public SceneJoint AddJoint(JointKind kind, SceneBody a, SceneBody? b, Vec3 worldAnchor, Vec3 worldAxis, string? id = null) =>
            AddJoint(new SceneJoint(id ?? $"J{Joints.Count + 1}", kind, a, b, worldAnchor, worldAxis));

        public SceneBody? FindBody(string id)
        {
            foreach (var b in Bodies)
                if (b.Id == id)
                    return b;
            return null;
        }

        // ================================================================== environment

        public double GroundHeight(double x, double y) => Terrain?.Height(x, y) ?? 0.0;

        public Vec3 GroundNormal(double x, double y) => Terrain?.Normal(x, y) ?? Vec3.UnitZ;

        public string GroundMaterialAt(double x, double y)
        {
            for (int i = Regions.Count - 1; i >= 0; i--)
                if (Regions[i].Contains(x, y))
                    return Regions[i].Material;
            return GroundMaterial;
        }

        /// <summary>Contact properties of a body material against the ground at (x, y).</summary>
        public ContactProperties GroundProperties(string bodyMaterial, double x, double y, bool caster = false) =>
            GroundProperties(Materials.IndexOf(bodyMaterial), x, y, caster);

        private static readonly int SteelIndex = Materials.IndexOf("steel");
        private static readonly int WoodIndex = Materials.IndexOf("wood");
        private string _groundMaterial = "concrete";
        private int _groundIndex = Materials.IndexOf("concrete");

        internal ContactProperties GroundProperties(int bodyMaterial, double x, double y, bool caster = false)
        {
            bool inRegion = false;
            int floor = _groundIndex;
            for (int i = Regions.Count - 1; i >= 0; i--)
            {
                if (!Regions[i].Contains(x, y)) continue;
                floor = Regions[i].MaterialIndex;
                inRegion = true;
                break;
            }

            if (caster)
            {
                if (GroundFriction.HasValue && !inRegion) return new ContactProperties(0.04, 0.04, 0.05, 0, 2e5);
                // A ball caster rolls in every direction; its resistance grows on soft floors.
                var steel = Materials.Pair(SteelIndex, floor);
                double mu = 0.03 + 2 * steel.RollingResistance;
                return new ContactProperties(mu, mu, 0.05, 0, steel.Stiffness);
            }

            if (GroundGrip is double grip && !inRegion) return GripScaled(bodyMaterial, floor, grip); // [phys] material pairs scaled to the floor's tyre grip

            if (GroundFriction.HasValue && !inRegion)
            {
                double mu = GroundFriction.Value;
                var p = Materials.Pair(bodyMaterial, WoodIndex);
                return new ContactProperties(mu, mu, 0.05, p.RollingResistance, p.Stiffness);
            }

            return Materials.Pair(bodyMaterial, floor);
        }

        /// <summary>Wind at a point including deterministic gusts.</summary>
        public Vec3 WindAt(Vec3 p)
        {
            if (GustIntensity <= 0) return Wind;
            double t = Time, k = GustIntensity;
            double phase = 0.7 * p.X + 0.4 * p.Y;
            double gx = Math.Sin(1.3 * t + phase) * 0.6 + Math.Sin(3.7 * t + 1.1 + phase) * 0.35 + Math.Sin(7.9 * t + 2.3) * 0.2;
            double gy = Math.Sin(1.1 * t + 0.5 + phase) * 0.6 + Math.Sin(4.3 * t + 2.0) * 0.35 + Math.Sin(8.7 * t + 0.3 + phase) * 0.2;
            double gz = Math.Sin(2.1 * t + 1.7) * 0.3 + Math.Sin(5.9 * t + 0.9 + phase) * 0.15;
            return Wind + new Vec3(gx, gy, gz) * (k / 0.75);
        }

        // ================================================================== stepping

        public void Step(double dt)
        {
            if (dt <= 0) return;
            int n = Math.Max(1, (int)Math.Ceiling(dt / MaxSubstep - 1e-9));
            n = CcdSubsteps(dt, n); // [phys] more substeps for fast thin bodies
            double h = dt / n;
            _h = h;
            int count = Bodies.Count;
            EnsureBodyArrays(count);
            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                b.Slot = i;
                b.ExternalForce = b.Force;
                b.ExternalTorque = b.Torque;
                b.Force = Vec3.Zero;
                b.Torque = Vec3.Zero;
                b.PeakContactForce = 0;
                b.PeakImpactForce = 0;
                b.PeakAcceleration = 0;
            }

            _noCollide.Clear();
            foreach (var j in Joints)
            {
                j.ChildSlot = j.ChildBody.Slot;
                j.ParentSlot = j.ParentBody?.Slot ?? -1;
                j.StepDriveImpulse = 0;
                j.StepForceImpulse = Vec3.Zero;
                j.StepTorqueImpulse = Vec3.Zero;
                if (!j.Enabled || j.Broken || j.CollideConnected || j.ParentSlot < 0) continue;
                _noCollide.Add(PairKey(j.ParentSlot, j.ChildSlot));
            }

            foreach (var (a, b) in NoCollide)
            {
                if (a.Slot >= 0 && b.Slot >= 0 && a.Slot != b.Slot) _noCollide.Add(PairKey(a.Slot, b.Slot));
            }

            PeakContactForce = 0;
            BeginSleep(dt); // [phys]
            for (int s = 0; s < n; s++) Substep(h);

            foreach (var j in Joints)
            {
                if (!j.Enabled || j.Broken || JointAsleep(j)) continue; // [phys] sleeping joints keep their last loads
                j.DriveTorque = j.StepDriveImpulse / dt;
                j.ReactionForce = j.StepForceImpulse / dt;
                j.ReactionTorque = j.StepTorqueImpulse / dt;
            }

            foreach (var b in Bodies)
            {
                b.ExternalForce = Vec3.Zero;
                b.ExternalTorque = Vec3.Zero;
                if (!b.IsStatic)
                {
                    // Net contact force averaged over the step (what an accelerometer would feel).
                    double f = b.StepContactImpulse.Length / dt;
                    if (f > b.PeakContactForce) b.PeakContactForce = f;
                    double acc = f / b.Mass;
                    if (acc > b.PeakAcceleration) b.PeakAcceleration = acc;
                }

                b.StepContactImpulse = Vec3.Zero;
                PeakContactForce = Math.Max(PeakContactForce, b.PeakContactForce);
            }

            EndSleep(dt); // [phys]
        }

        private static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private void EnsureBodyArrays(int count)
        {
            if (_state.Length < count)
            {
                int cap = Math.Max(count, _state.Length * 2);
                Array.Resize(ref _state, cap);
                Array.Resize(ref _order, cap);
                Array.Resize(ref _minX, cap);
                Array.Resize(ref _lo, cap);
                Array.Resize(ref _hi, cap);
                Array.Resize(ref _shapes, cap);
            }
        }

        private void Substep(double h)
        {
            int count = Bodies.Count;
            var g = Gravity;
            ComputeBounds(h);

            // ---------------------------------------------------------- forces
            foreach (var j in Joints)
            {
                if (!j.Enabled || j.Broken || j.MotorTorque == 0) continue;
                ApplyJointMotor(j);
            }

            foreach (var w in Wheels)
                if (w.Body != null && !w.Detached)
                    w.Apply(this, h);
            foreach (var p in Propellers)
                if (p.Body != null && !p.Detached)
                    p.Apply(this, h);

            bool air = AirDensity > 0;
            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                if (b.IsStatic || Asleep(i)) // [phys]
                {
                    b.Force = Vec3.Zero;
                    b.Torque = Vec3.Zero;
                    continue;
                }

                b.Force += b.ExternalForce + g * b.Mass;
                b.Torque += b.ExternalTorque;
                if (air && (b.DragArea.X > 0 || b.DragArea.Y > 0 || b.DragArea.Z > 0))
                {
                    var rel = b.Velocity - WindAt(b.Position);
                    double sp = rel.Length;
                    if (sp > 1e-9)
                    {
                        var lb = b.Orientation.InverseRotate(rel) / sp;
                        double cda = Math.Abs(lb.X) * b.DragArea.X + Math.Abs(lb.Y) * b.DragArea.Y + Math.Abs(lb.Z) * b.DragArea.Z;
                        // Quadratic drag ½ρ·CdA·|v|·v, limited so that it can never reverse the velocity within a step.
                        double k = 0.5 * AirDensity * cda * sp;
                        k = Math.Min(k, b.Mass / h);
                        b.Force -= rel * k;
                    }
                }

                b.IntegrateVelocity(h);
            }

            // ---------------------------------------------------------- state
            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                ref var st = ref _state[i];
                st.X = b.Position;
                st.DX = Vec3.Zero;
                st.DTheta = Vec3.Zero;
                if (b.IsStatic || Asleep(i)) // [phys]
                {
                    st.V = Vec3.Zero;
                    st.W = Vec3.Zero;
                    st.InvMass = 0;
                    st.InvI = Mat3.Zero;
                }
                else
                {
                    st.V = b.Velocity;
                    st.W = b.AngularVelocity;
                    st.InvMass = 1.0 / b.Mass;
                    st.InvI = b.InverseInertiaWorld();
                }
            }

            // ---------------------------------------------------------- contacts & joints
            _contactCount = 0;
            Detect(h);
            PrepareJoints(h);

            Soft(Math.Min(ContactHertz, 0.25 / h), ContactDampingRatio, h, out double cBias, out double cMass, out double cImp);
            Soft(Math.Min(JointHertz, 0.25 / h), JointDampingRatio, h, out double jBias, out double jMass, out double jImp);

            WarmStart();
            for (int it = 0; it < Iterations; it++)
            {
                SolveJoints(true, jBias, jMass, jImp, h);
                SolveContacts(true, cBias, cMass, cImp, h, false);
            }

            // ---------------------------------------------------------- positions
            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                if (b.IsStatic || Asleep(i)) continue; // [phys]
                ref var st = ref _state[i];
                b.Velocity = st.V;
                b.AngularVelocity = st.W;
                var x0 = b.Position;
                b.IntegratePosition(h);
                st.DX = b.Position - x0;
                st.DTheta = st.W * h;
            }

            for (int it = 0; it < RelaxIterations; it++)
            {
                SolveJoints(false, jBias, jMass, jImp, h);
                SolveContacts(false, cBias, cMass, cImp, h, true);
            }

            ApplyRestitution(h);

            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                if (b.IsStatic || Asleep(i)) continue; // [phys]
                b.Velocity = _state[i].V;
                b.AngularVelocity = _state[i].W;
            }

            StoreAndReport(h);
            FinishJoints(h);
            Time += h;
        }

        private static void Soft(double hertz, double zeta, double h, out double biasRate, out double massScale, out double impulseScale)
        {
            if (hertz <= 0)
            {
                biasRate = 0;
                massScale = 1;
                impulseScale = 0;
                return;
            }

            double omega = 2 * Math.PI * hertz;
            double a1 = 2 * zeta + h * omega;
            double a2 = h * omega * a1;
            double a3 = 1 / (1 + a2);
            biasRate = omega / a1;
            massScale = a2 * a3;
            impulseScale = a3;
        }

        private void ApplyJointMotor(SceneJoint j)
        {
            var axis = j.WorldAxis;
            if (j.Kind == JointKind.Slider)
            {
                var f = axis * j.MotorTorque;
                var at = j.WorldAnchor;
                if (!j.ChildBody.IsStatic) j.ChildBody.AddForceAtPoint(f, at);
                if (j.ParentBody != null && !j.ParentBody.IsStatic) j.ParentBody.AddForceAtPoint(-f, j.ParentBody.ToWorld(j.AnchorP));
            }
            else if (j.Kind == JointKind.Hinge)
            {
                if (!j.ChildBody.IsStatic) j.ChildBody.AddTorque(axis * j.MotorTorque);
                if (j.ParentBody != null && !j.ParentBody.IsStatic) j.ParentBody.AddTorque(axis * -j.MotorTorque);
            }
        }

        // ================================================================== collision detection

        private double Margin(int a, int b, double h)
        {
            double v = _state[a].V.Length + (b >= 0 ? _state[b].V.Length : 0);
            return 0.002 + 1.5 * v * h;
        }

        /// <summary>World bounding boxes of all bodies for this substep (static ones cached while they do not move).</summary>
        private void ComputeBounds(double h)
        {
            int count = Bodies.Count;
            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                var shape = b.EffectiveShape;
                _shapes[i] = b.Collides ? shape : null;
                if (shape == null) continue;
                if (b.IsStatic)
                {
                    var q = b.Orientation;
                    if (!ReferenceEquals(b.CachedBoundsShape, shape) || b.CachedBoundsPos.X != b.Position.X || b.CachedBoundsPos.Y != b.Position.Y ||
                        b.CachedBoundsPos.Z != b.Position.Z || b.CachedBoundsRot.W != q.W || b.CachedBoundsRot.X != q.X || b.CachedBoundsRot.Y != q.Y ||
                        b.CachedBoundsRot.Z != q.Z)
                    {
                        b.CachedBounds = shape.WorldBounds(b.Position, q).Expanded(0.002);
                        b.CachedBoundsShape = shape;
                        b.CachedBoundsPos = b.Position;
                        b.CachedBoundsRot = q;
                    }

                    _lo[i] = b.CachedBounds.Min;
                    _hi[i] = b.CachedBounds.Max;
                }
                else
                {
                    double r = shape.BoundingRadius + 0.003 + b.Velocity.Length * h * 2;
                    var e = new Vec3(r, r, r);
                    _lo[i] = b.Position - e;
                    _hi[i] = b.Position + e;
                }
            }
        }

        private bool Overlap(int a, int b) =>
            _lo[a].X <= _hi[b].X && _hi[a].X >= _lo[b].X && _lo[a].Y <= _hi[b].Y && _hi[a].Y >= _lo[b].Y && _lo[a].Z <= _hi[b].Z && _hi[a].Z >= _lo[b].Z;

        private void Detect(double h)
        {
            int count = Bodies.Count;
            // Sweep and prune along x on the bounding boxes.
            int m = 0;
            for (int i = 0; i < count; i++)
            {
                if (_shapes[i] == null) continue;
                _order[m++] = i;
                _minX[i] = _lo[i].X;
            }

            for (int i = 1; i < m; i++)
            {
                int key = _order[i];
                double kx = _minX[key];
                int j = i - 1;
                while (j >= 0 && _minX[_order[j]] > kx)
                {
                    _order[j + 1] = _order[j];
                    j--;
                }

                _order[j + 1] = key;
            }

            for (int ii = 0; ii < m; ii++)
            {
                int a = _order[ii];
                var ba = Bodies[a];
                if (!ba.IsStatic && GroundEnabled && !Asleep(a)) CollideGround(a, h); // [phys]
                double maxX = _hi[a].X;
                for (int jj = ii + 1; jj < m; jj++)
                {
                    int b = _order[jj];
                    if (_minX[b] > maxX) break;
                    var bb = Bodies[b];
                    if ((ba.IsStatic || Asleep(a)) && (bb.IsStatic || Asleep(b))) continue; // [phys]
                    if (!Overlap(a, b)) continue;
                    if (_noCollide.Count > 0 && _noCollide.Contains(PairKey(a, b))) continue;
                    if (ba.IsStatic || (Asleep(a) && !bb.IsStatic)) CollidePair(b, a, h); // [phys]
                    else CollidePair(a, b, h);
                }
            }
        }

        private double TerrainSeparation(Vec3 p, out Vec3 normal)
        {
            if (Terrain == null)
            {
                normal = Vec3.UnitZ;
                return p.Z;
            }

            return Terrain.Separation(p, out normal, out _);
        }

        private void CollideGround(int a, double h)
        {
            var body = Bodies[a];
            var shape = _shapes[a]!;
            double margin = Margin(a, -1, h);
            double top = Terrain == null ? 0 : Terrain.HeightRange.Max;
            if (body.Position.Z - shape.BoundingRadius > top + margin) return;
            var q = body.Orientation;
            var x = body.Position;
            switch (shape.Kind)
            {
                case ShapeKind.Sphere:
                {
                    double sep = TerrainSeparation(x, out var n) - shape.Radius;
                    if (sep < margin)
                    {
                        var props = GroundProperties(body.MaterialIndex, x.X, x.Y);
                        AddContact(a, -1, x - n * shape.Radius, n, sep, 0, props, shape.Radius, h);
                    }

                    return;
                }

                case ShapeKind.Cylinder:
                {
                    TerrainSeparation(x, out var n0);
                    var dirLocal = q.InverseRotate(-n0);
                    int k = shape.CylinderCandidates(dirLocal, _candidates);
                    bool support = Math.Sqrt(dirLocal.X * dirLocal.X + dirLocal.Y * dirLocal.Y) > 1e-6 * dirLocal.Length;
                    for (int i = 0; i < k; i++)
                    {
                        var p = x + q.Rotate(_candidates[i]);
                        double sep = TerrainSeparation(p, out var n);
                        if (sep >= margin) continue;
                        var props = GroundProperties(body.MaterialIndex, p.X, p.Y);
                        AddContact(a, -1, p, n, sep, i, props, support && i < 2 ? shape.Radius : 0, h);
                    }

                    return;
                }

                default:
                {
                    var verts = shape.Verts;
                    var caster = shape.Caster;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var p = x + q.Rotate(verts[i]);
                        if (Terrain == null)
                        {
                            if (p.Z >= margin) continue;
                            var props = GroundProperties(body.MaterialIndex, p.X, p.Y, caster[i]);
                            AddContact(a, -1, p, Vec3.UnitZ, p.Z, i, props, 0, h);
                        }
                        else
                        {
                            double sep = TerrainSeparation(p, out var n);
                            if (sep >= margin) continue;
                            var props = GroundProperties(body.MaterialIndex, p.X, p.Y, caster[i]);
                            AddContact(a, -1, p, n, sep, i, props, 0, h);
                        }
                    }

                    return;
                }
            }
        }

        /// <summary>Narrow phase between dynamic body a and body b (dynamic or static). Normals point from b to a.</summary>
        private void CollidePair(int a, int b, double h)
        {
            var A = Bodies[a];
            var B = Bodies[b];
            var sa = _shapes[a]!;
            var sb = _shapes[b]!;
            double margin = Margin(a, b, h);
            var props = Materials.Pair(A.MaterialIndex, B.MaterialIndex);
            var xa = A.Position;
            var xb = B.Position;
            var qa = A.Orientation;
            var qb = B.Orientation;

            if (sa.Kind == ShapeKind.Sphere)
            {
                if (sb.Kind == ShapeKind.Sphere)
                {
                    var d = xa - xb;
                    double len = d.Length;
                    double sep = len - sa.Radius - sb.Radius;
                    if (sep >= margin) return;
                    var n = len > 1e-12 ? d / len : Vec3.UnitZ;
                    AddContact(a, b, xa - n * sa.Radius, n, sep, 0, props, sa.Radius, h);
                    return;
                }

                if (!sb.HasSolid) return;
                if (sb.SignedDistance(qb.InverseRotate(xa - xb), margin + sa.Radius, out double dist, out var nl))
                {
                    double sep = dist - sa.Radius;
                    if (sep < margin)
                    {
                        var n = qb.Rotate(nl);
                        AddContact(a, b, xa - n * sa.Radius, n, sep, 0, props, sa.Radius, h);
                    }
                }

                return;
            }

            if (sb.Kind == ShapeKind.Sphere)
            {
                if (!sa.HasSolid) return;
                if (sa.SignedDistance(qa.InverseRotate(xb - xa), margin + sb.Radius, out double dist, out var nl))
                {
                    double sep = dist - sb.Radius;
                    if (sep < margin)
                    {
                        var na = qa.Rotate(nl);
                        AddContact(a, b, xb - na * sb.Radius, -na, sep, 0, props, sb.Radius, h);
                    }
                }

                return;
            }

            if (IsPolyhedral(sa) && IsPolyhedral(sb) && (sa.Planes.Length > 0 || sb.Planes.Length > 0))
            {
                PolyPoly(a, b, sa, A, sb, B, margin, props, h);
                return;
            }

            // A's candidates against B's solid.
            if (sb.HasSolid) Candidates(a, b, sa, A, sb, B, margin, props, h, false);
            // B's candidates against A's solid (corners of B poking into A's faces).
            if (sa.HasSolid && sb.Kind != ShapeKind.Mesh) Candidates(b, a, sb, B, sa, A, margin, props, h, true);
            else if (sa.HasSolid && sb.Kind == ShapeKind.Mesh && sb.Verts.Length > 0) Candidates(b, a, sb, B, sa, A, margin, props, h, true);
        }

        private static bool IsPolyhedral(CollisionShape s) => s.Kind == ShapeKind.Box || s.Kind == ShapeKind.ConvexHull || s.Kind == ShapeKind.Points;

        private Vec3[] _va = new Vec3[32], _vb = new Vec3[32];
        private Vec3[] _na = new Vec3[32], _nb = new Vec3[32];
        private double[] _oa = new double[32], _ob = new double[32];

        private static int Transform(CollisionShape s, SceneBody b, ref Vec3[] verts, ref Vec3[] normals, ref double[] offsets, out int planes)
        {
            if (verts.Length < s.Verts.Length) Array.Resize(ref verts, s.Verts.Length * 2);
            if (normals.Length < s.Planes.Length)
            {
                Array.Resize(ref normals, s.Planes.Length * 2);
                Array.Resize(ref offsets, s.Planes.Length * 2);
            }

            var q = b.Orientation;
            var x = b.Position;
            for (int i = 0; i < s.Verts.Length; i++) verts[i] = x + q.Rotate(s.Verts[i]);
            planes = s.Planes.Length;
            for (int i = 0; i < planes; i++)
            {
                var n = q.Rotate(s.Planes[i].Normal);
                normals[i] = n;
                offsets[i] = s.Planes[i].Offset + n.Dot(x);
            }

            return s.Verts.Length;
        }

        /// <summary>
        /// Convex polyhedra (boxes, hulls, chassis point clouds): separating-axis test over the
        /// face normals of both, then every vertex of either body that lies behind the reference
        /// face and within the other's side faces becomes a contact with that one normal. Face–face
        /// contact (stacking) therefore gets a consistent manifold even when edges coincide.
        /// </summary>
        private void PolyPoly(int a, int b, CollisionShape sa, SceneBody A, CollisionShape sb, SceneBody B, double margin, in ContactProperties props, double h)
        {
            int nva = Transform(sa, A, ref _va, ref _na, ref _oa, out int npa);
            int nvb = Transform(sb, B, ref _vb, ref _nb, ref _ob, out int npb);
            double best = double.NegativeInfinity;
            var n = Vec3.UnitZ;
            // B's faces: axis from B to A along +n_B.
            for (int f = 0; f < npb; f++)
            {
                if (sb.FloorPlane[f]) continue;
                var nf = _nb[f];
                double min = double.PositiveInfinity;
                for (int i = 0; i < nva; i++) min = Math.Min(min, nf.Dot(_va[i]));
                double sep = min - _ob[f];
                if (sep > margin) return;
                if (sep > best)
                {
                    best = sep;
                    n = nf;
                }
            }

            // A's faces: axis from B to A along −n_A (a small bias keeps B's faces on ties, e.g. resting on a static body).
            for (int f = 0; f < npa; f++)
            {
                if (sa.FloorPlane[f]) continue;
                var nf = _na[f];
                double min = double.PositiveInfinity;
                for (int i = 0; i < nvb; i++) min = Math.Min(min, nf.Dot(_vb[i]));
                double sep = min - _oa[f];
                if (sep > margin) return;
                if (sep > best + 1e-5)
                {
                    best = sep;
                    n = -nf;
                }
            }

            if (double.IsNegativeInfinity(best)) return;
            double supB = double.NegativeInfinity, supA = double.PositiveInfinity;
            for (int i = 0; i < nvb; i++) supB = Math.Max(supB, n.Dot(_vb[i]));
            for (int i = 0; i < nva; i++) supA = Math.Min(supA, n.Dot(_va[i]));
            const double lateral = 5e-4;
            int first = _contactCount;
            for (int i = 0; i < nva; i++)
            {
                var v = _va[i];
                double s = n.Dot(v) - supB;
                if (s >= margin || !WithinSides(v, n, _nb, _ob, npb, sb.FloorPlane, lateral)) continue;
                AddContact(a, b, v, n, s, i, props, 0, h);
            }

            int fromA = _contactCount;
            for (int i = 0; i < nvb; i++)
            {
                var u = _vb[i];
                double s = supA - n.Dot(u);
                if (s >= margin || !WithinSides(u, n, _na, _oa, npa, sa.FloorPlane, lateral)) continue;
                bool duplicate = false;
                for (int c = first; c < fromA; c++)
                {
                    if ((_state[a].X + _contacts[c].RA - u).LengthSquared < 1e-6)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate) AddContact(a, b, u, n, s, (1 << 12) + i, props, 0, h);
            }
        }

        private static bool WithinSides(Vec3 p, Vec3 axis, Vec3[] normals, double[] offsets, int count, bool[] floor, double tol)
        {
            if (count == 0) return true;
            for (int f = 0; f < count; f++)
            {
                if (floor[f]) continue;
                var nf = normals[f];
                if (Math.Abs(nf.Dot(axis)) > 0.95) continue;
                if (nf.Dot(p) - offsets[f] > tol) return false;
            }

            return true;
        }

        /// <summary>Contact candidates of body i (shape si) against the solid of body k; flip = normals reversed (i is the "B" side).</summary>
        private void Candidates(int i, int k, CollisionShape si, SceneBody bi, CollisionShape sk, SceneBody bk, double margin, in ContactProperties props, double h, bool flip)
        {
            var xi = bi.Position;
            var qi = bi.Orientation;
            var xk = bk.Position;
            var qk = bk.Orientation;
            int featureBase = flip ? 1 << 12 : 0;
            if (si.Kind == ShapeKind.Cylinder)
            {
                // Direction from the cylinder into the other solid.
                Vec3 dirW;
                if (sk.Planes.Length > 0 && sk.Kind != ShapeKind.Mesh)
                {
                    // Separating-axis test over the other solid's faces.
                    double best = double.NegativeInfinity;
                    var bn = Vec3.UnitZ;
                    for (int f = 0; f < sk.Planes.Length; f++)
                    {
                        if (sk.FloorPlane.Length > f && sk.FloorPlane[f]) continue;
                        var nW = qk.Rotate(sk.Planes[f].Normal);
                        var sup = xi + qi.Rotate(si.Support(qi.InverseRotate(-nW)));
                        double d = sk.Planes[f].Distance(qk.InverseRotate(sup - xk));
                        if (d > best)
                        {
                            best = d;
                            bn = nW;
                        }
                    }

                    if (best >= margin) return;
                    dirW = -bn;
                }
                else if (sk.Kind == ShapeKind.Mesh)
                {
                    if (!sk.SignedDistance(qk.InverseRotate(xi - xk), si.BoundingRadius + margin, out _, out var nm)) return;
                    dirW = -qk.Rotate(nm);
                }
                else
                {
                    dirW = (xk - xi).Normalized();
                }

                var dl = qi.InverseRotate(dirW);
                int nc = si.CylinderCandidates(dl, _candidates);
                bool support = Math.Sqrt(dl.X * dl.X + dl.Y * dl.Y) > 1e-6;
                for (int c = 0; c < nc; c++)
                {
                    var p = xi + qi.Rotate(_candidates[c]);
                    if (!sk.SignedDistance(qk.InverseRotate(p - xk), margin, out double d, out var nl)) continue;
                    if (d >= margin) continue;
                    var n = qk.Rotate(nl);
                    double rr = support && c < 2 ? si.Radius : 0;
                    if (flip) AddContact(k, i, p, -n, d, featureBase + c, props, rr, h);
                    else AddContact(i, k, p, n, d, featureBase + c, props, rr, h);
                }

                return;
            }

            if (sk.Kind == ShapeKind.Cylinder || sk.Kind == ShapeKind.Mesh || sk.Kind == ShapeKind.Box || sk.Planes.Length > 0)
            {
                var verts = si.Verts;
                // Quick reject against the other's bounding sphere.
                double rk = sk.BoundingRadius + margin;
                for (int c = 0; c < verts.Length; c++)
                {
                    var p = xi + qi.Rotate(verts[c]);
                    var local = qk.InverseRotate(p - xk);
                    if (sk.Kind != ShapeKind.Mesh && local.LengthSquared > rk * rk) continue;
                    if (!sk.SignedDistance(local, margin, out double d, out var nl)) continue;
                    if (d >= margin) continue;
                    var n = qk.Rotate(nl);
                    if (flip) AddContact(k, i, p, -n, d, featureBase + c, props, 0, h);
                    else AddContact(i, k, p, n, d, featureBase + c, props, 0, h);
                }
            }
        }

        private void AddContact(int a, int b, Vec3 p, Vec3 n, double sep, int feature, in ContactProperties props, double rollRadius, double h)
        {
            if (_contactCount == _contacts.Length) Array.Resize(ref _contacts, _contacts.Length * 2);
            ref var c = ref _contacts[_contactCount++];
            ref var sa = ref _state[a];
            c.A = a;
            c.B = b;
            c.N = n;
            c.Sep = sep;
            c.RA = p - sa.X;
            double k = sa.InvMass;
            var rnA = c.RA.Cross(n);
            k += rnA.Dot(sa.InvI * rnA);
            var dv = sa.V + sa.W.Cross(c.RA);
            Mat3 invIB = Mat3.Zero;
            double invMB = 0;
            if (b >= 0)
            {
                ref var sb = ref _state[b];
                c.RB = p - sb.X;
                var rnB = c.RB.Cross(n);
                k += sb.InvMass + rnB.Dot(sb.InvI * rnB);
                dv -= sb.V + sb.W.Cross(c.RB);
                invIB = sb.InvI;
                invMB = sb.InvMass;
            }
            else
            {
                c.RB = Vec3.Zero;
            }

            c.MassN = k > 0 ? 1 / k : 0;
            double vn = dv.Dot(n);
            var vt = dv - n * vn;
            double vtl = vt.Length;
            c.T1 = vtl > 1e-9 ? vt / vtl : n.AnyPerpendicular();
            c.T2 = n.Cross(c.T1);
            c.MassT1 = TangentMass(sa.InvMass, sa.InvI, c.RA, invMB, invIB, c.RB, c.T1, b >= 0);
            c.MassT2 = TangentMass(sa.InvMass, sa.InvI, c.RA, invMB, invIB, c.RB, c.T2, b >= 0);
            c.Mu = vtl < StickVelocity ? props.StaticFriction : props.KineticFriction;
            c.E = props.Restitution;
            c.Vn0 = vn;
            c.Crr = props.RollingResistance;
            c.RollR = rollRadius;
            c.Stiff = props.Stiffness;
            c.Rolling = rollRadius > 0 && c.Crr > 0;
            if (c.Rolling)
            {
                var invI = sa.InvI + invIB;
                double r1 = c.T1.Dot(invI * c.T1), r2 = c.T2.Dot(invI * c.T2);
                c.MassR1 = r1 > 0 ? 1 / r1 : 0;
                c.MassR2 = r2 > 0 ? 1 / r2 : 0;
            }

            SetupSpin(ref c, sa.InvI, invIB, props, rollRadius); // [phys]
            c.MaxPn = 0;
            c.Key = ((long)(a + 1) << 42) | ((long)(b + 2) << 21) | (uint)(feature & 0x1FFFFF);
            if (_warm.TryGetValue(c.Key, out var ws))
            {
                c.Pn = ws.Pn;
                c.Pt1 = ws.Pt.Dot(c.T1);
                c.Pt2 = ws.Pt.Dot(c.T2);
                c.Pr1 = c.Rolling ? ws.Pr.Dot(c.T1) : 0;
                c.Pr2 = c.Rolling ? ws.Pr.Dot(c.T2) : 0;
                c.Ps = c.SpinR > 0 ? ws.Ps : 0; // [phys]
            }
            else
            {
                c.Pn = c.Pt1 = c.Pt2 = c.Pr1 = c.Pr2 = 0;
                c.Ps = 0; // [phys]
            }
        }

        private static double TangentMass(double invMA, Mat3 invIA, Vec3 rA, double invMB, Mat3 invIB, Vec3 rB, Vec3 t, bool hasB)
        {
            var ra = rA.Cross(t);
            double k = invMA + ra.Dot(invIA * ra);
            if (hasB)
            {
                var rb = rB.Cross(t);
                k += invMB + rb.Dot(invIB * rb);
            }

            return k > 0 ? 1 / k : 0;
        }

        // ================================================================== contact solver

        private void ApplyImpulse(int a, int b, Vec3 rA, Vec3 rB, Vec3 p)
        {
            ref var sa = ref _state[a];
            sa.V += p * sa.InvMass;
            sa.W += sa.InvI * rA.Cross(p);
            if (b >= 0)
            {
                ref var sb = ref _state[b];
                sb.V -= p * sb.InvMass;
                sb.W -= sb.InvI * rB.Cross(p);
            }
        }

        private void ApplyAngular(int a, int b, Vec3 l)
        {
            ref var sa = ref _state[a];
            sa.W += sa.InvI * l;
            if (b >= 0)
            {
                ref var sb = ref _state[b];
                sb.W -= sb.InvI * l;
            }
        }

        private Vec3 RelativeVelocity(in Contact c)
        {
            ref var sa = ref _state[c.A];
            var v = sa.V + sa.W.Cross(c.RA);
            if (c.B >= 0)
            {
                ref var sb = ref _state[c.B];
                v -= sb.V + sb.W.Cross(c.RB);
            }

            return v;
        }

        private void WarmStart()
        {
            for (int i = 0; i < _contactCount; i++)
            {
                ref var c = ref _contacts[i];
                if (c.Pn == 0 && c.Pt1 == 0 && c.Pt2 == 0) continue;
                ApplyImpulse(c.A, c.B, c.RA, c.RB, c.N * c.Pn + c.T1 * c.Pt1 + c.T2 * c.Pt2);
                if (c.Rolling && (c.Pr1 != 0 || c.Pr2 != 0)) ApplyAngular(c.A, c.B, c.T1 * c.Pr1 + c.T2 * c.Pr2);
                if (c.Ps != 0) ApplyAngular(c.A, c.B, c.N * c.Ps); // [phys]
            }

            for (int i = 0; i < _pointCount; i++)
            {
                ref var pr = ref _points[i];
                var j = Joints[pr.Joint];
                ApplyJointImpulse(pr.P, pr.C, pr.RP, pr.RC, j.PointImpulse);
            }

            for (int i = 0; i < _rowCount; i++)
            {
                ref var r = ref _rows[i];
                double imp = Joints[r.Joint].Impulse[r.Slot];
                if (imp != 0) ApplyRow(ref r, imp);
            }
        }

        private void SolveContacts(bool useBias, double biasRate, double massScale, double impulseScale, double h, bool relax)
        {
            double invH = 1 / h;
            for (int i = 0; i < _contactCount; i++)
            {
                ref var c = ref _contacts[i];
                // Separation, updated by the position step for the relax pass.
                double s = c.Sep;
                if (relax)
                {
                    ref var sa = ref _state[c.A];
                    var d = sa.DX + sa.DTheta.Cross(c.RA);
                    if (c.B >= 0)
                    {
                        ref var sb = ref _state[c.B];
                        d -= sb.DX + sb.DTheta.Cross(c.RB);
                    }

                    s += d.Dot(c.N);
                }

                // Normal.
                var dv = RelativeVelocity(c);
                double vn = dv.Dot(c.N);
                double bias = 0, ms = 1, isc = 0;
                if (s > 0) bias = s * invH; // real gap: speculative, may close exactly
                else if (useBias && s < -LinearSlop)
                {
                    // Penetration within the slop is held rigidly without push-out (no jitter, no
                    // creeping); deeper penetration is pushed out softly.
                    bias = Math.Max(biasRate * (s + LinearSlop), -MaxPushout);
                    ms = massScale;
                    isc = impulseScale;
                }

                double lambda = -c.MassN * ms * (vn + bias) - isc * c.Pn;
                double pn = Math.Max(c.Pn + lambda, 0);
                lambda = pn - c.Pn;
                c.Pn = pn;
                if (pn > c.MaxPn) c.MaxPn = pn;
                if (lambda != 0) ApplyImpulse(c.A, c.B, c.RA, c.RB, c.N * lambda);

                // Friction (cone).
                if (c.Mu > 0)
                {
                    dv = RelativeVelocity(c);
                    double l1 = c.Pt1 - c.MassT1 * dv.Dot(c.T1);
                    double l2 = c.Pt2 - c.MassT2 * dv.Dot(c.T2);
                    double max = c.Mu * c.Pn, len = Math.Sqrt(l1 * l1 + l2 * l2);
                    if (len > max)
                    {
                        double f = len > 0 ? max / len : 0;
                        l1 *= f;
                        l2 *= f;
                    }

                    double d1 = l1 - c.Pt1, d2 = l2 - c.Pt2;
                    c.Pt1 = l1;
                    c.Pt2 = l2;
                    if (d1 != 0 || d2 != 0) ApplyImpulse(c.A, c.B, c.RA, c.RB, c.T1 * d1 + c.T2 * d2);
                }

                // Rolling resistance: bounded torque against relative rolling.
                if (c.Rolling)
                {
                    ref var sa = ref _state[c.A];
                    var dw = sa.W;
                    if (c.B >= 0) dw -= _state[c.B].W;
                    double r1 = c.Pr1 - c.MassR1 * dw.Dot(c.T1);
                    double r2 = c.Pr2 - c.MassR2 * dw.Dot(c.T2);
                    double max = c.Crr * c.RollR * c.Pn, len = Math.Sqrt(r1 * r1 + r2 * r2);
                    if (len > max)
                    {
                        double f = len > 0 ? max / len : 0;
                        r1 *= f;
                        r2 *= f;
                    }

                    double d1 = r1 - c.Pr1, d2 = r2 - c.Pr2;
                    c.Pr1 = r1;
                    c.Pr2 = r2;
                    if (d1 != 0 || d2 != 0) ApplyAngular(c.A, c.B, c.T1 * d1 + c.T2 * d2);
                }

                if (c.SpinR > 0) SolveSpin(ref c); // [phys] torsional friction of the contact patch
            }
        }

        private void ApplyRestitution(double h)
        {
            // [phys] several Gauss-Seidel passes: one pass over a face's corners is order-dependent
            // and made flat-dropped boxes spin and tumble on landing.
            for (int pass = 0; pass < Math.Max(1, RestitutionPasses); pass++)
            for (int i = 0; i < _contactCount; i++)
            {
                ref var c = ref _contacts[i];
                if (c.E <= 0 || c.Vn0 > -RestitutionThreshold || c.MaxPn == 0) continue;
                var dv = RelativeVelocity(c);
                double vn = dv.Dot(c.N);
                double lambda = -c.MassN * (vn + c.E * c.Vn0);
                double pn = Math.Max(c.Pn + lambda, 0);
                lambda = pn - c.Pn;
                c.Pn = pn;
                if (lambda != 0) ApplyImpulse(c.A, c.B, c.RA, c.RB, c.N * lambda);
            }
        }

        private void StoreAndReport(double h)
        {
            _warmNext.Clear();
            for (int i = 0; i < _contactCount; i++)
            {
                ref var c = ref _contacts[i];
                _warmNext[c.Key] = new WarmImpulse { Pn = c.Pn, Pt = c.T1 * c.Pt1 + c.T2 * c.Pt2, Pr = c.T1 * c.Pr1 + c.T2 * c.Pr2, Ps = c.Ps }; // [phys] Ps

                // Net contact impulse per body (averaged over the step later); an impact is
                // spread over the physical contact time π·√(m/k) of the material pair.
                var p = c.N * c.Pn + c.T1 * c.Pt1 + c.T2 * c.Pt2;
                Bodies[c.A].StepContactImpulse += p;
                if (c.B >= 0) Bodies[c.B].StepContactImpulse -= p;
                if (c.Vn0 < -RestitutionThreshold && c.Pn > 0)
                {
                    double tau = Math.Max(h, Math.PI * Math.Sqrt(c.MassN / c.Stiff));
                    double force = c.Pn / tau;
                    var at = _state[c.A].X + c.RA;
                    Record(Bodies[c.A], force, at, -c.Vn0, c.MassN, tau);
                    if (c.B >= 0 && !Bodies[c.B].IsStatic) Record(Bodies[c.B], force, at, -c.Vn0, c.MassN, tau);
                }
            }

            // This substep's impulses seed the next one.
            (_warm, _warmNext) = (_warmNext, _warm);
        }

        private void Record(SceneBody b, double force, Vec3 point, double speed, double mass, double duration)
        {
            if (force > b.PeakContactForce) b.PeakContactForce = force;
            double acc = force / b.Mass;
            if (acc > b.PeakAcceleration) b.PeakAcceleration = acc;
            if (force > b.PeakImpactForce)
            {
                b.PeakImpactForce = force;
                b.PeakImpactPoint = point;
                b.PeakImpactSpeed = speed;
                b.PeakImpactMass = mass;
                b.PeakImpactDuration = duration;
            }

            double weight = b.Mass * Math.Max(1e-3, Gravity.Length);
            if (!b.Broken && force > b.BreakImpactForce)
            {
                b.Broken = true;
                Report(new PhysicsEvent(Time, b.Id, PhysicsEventKind.BodyBroken, $"kırıldı ({force:F0} N darbe)", force, true));
            }
            else if (force > ImpactReportG * weight)
            {
                if (_lastImpactReport.TryGetValue(b, out double last) && Time - last < 0.25) return;
                _lastImpactReport[b] = Time;
                Report(new PhysicsEvent(Time, b.Id, PhysicsEventKind.Impact, $"darbe {force:F1} N ({force / weight:F0} g)", force, force > 4 * ImpactReportG * weight));
            }
        }

        // ================================================================== joints

        private void AddRow(in Row r)
        {
            if (_rowCount == _rows.Length) Array.Resize(ref _rows, _rows.Length * 2);
            _rows[_rowCount++] = r;
        }

        private double RowMass(int p, int c, Vec3 lp, Vec3 ap, Vec3 lc, Vec3 ac)
        {
            double k = 0;
            if (p >= 0)
            {
                ref var sp = ref _state[p];
                k += lp.LengthSquared * sp.InvMass + ap.Dot(sp.InvI * ap);
            }

            ref var sc = ref _state[c];
            k += lc.LengthSquared * sc.InvMass + ac.Dot(sc.InvI * ac);
            return k > 1e-300 ? 1 / k : 0;
        }

        private void PrepareJoints(double h)
        {
            _rowCount = 0;
            _pointCount = 0;
            for (int ji = 0; ji < Joints.Count; ji++)
            {
                var j = Joints[ji];
                if (!j.Enabled || j.Broken) continue;
                int p = j.ParentSlot, c = j.ChildSlot;
                if (c < 0 || (p >= 0 && Bodies[p].IsStatic && Bodies[c].IsStatic)) continue;
                if (JointAsleep(j)) continue; // [phys]
                var qP = p >= 0 ? Bodies[p].Orientation : Quat.Identity;
                var xP = p >= 0 ? Bodies[p].Position : Vec3.Zero;
                var qC = Bodies[c].Orientation;
                var xC = Bodies[c].Position;
                var rP = qP.Rotate(j.AnchorP);
                var rC = qC.Rotate(j.AnchorC);
                var pP = xP + rP;
                var pC = xC + rC;
                var d = pC - pP;
                var axis = qP.Rotate(j.AxisP);

                // Joint coordinate along the axis.
                double value, speed;
                ref var stC = ref _state[c];
                var wRel = stC.W - (p >= 0 ? _state[p].W : Vec3.Zero);
                if (j.Kind == JointKind.Slider)
                {
                    value = d.Dot(axis);
                    var vRel = stC.V + stC.W.Cross(rC) - (p >= 0 ? _state[p].V + _state[p].W.Cross(rP + d) : Vec3.Zero);
                    speed = vRel.Dot(axis);
                    j.Angle = value;
                }
                else
                {
                    var qr = (qP * j.Rest).Conjugate * qC;
                    double wrapped = 2 * Math.Atan2(new Vec3(qr.X, qr.Y, qr.Z).Dot(j.AxisC), qr.W);
                    if (wrapped > Math.PI) wrapped -= 2 * Math.PI;
                    if (wrapped < -Math.PI) wrapped += 2 * Math.PI;
                    double delta = wrapped - j.PrevWrapped;
                    if (delta > Math.PI) delta -= 2 * Math.PI;
                    if (delta < -Math.PI) delta += 2 * Math.PI;
                    j.Angle += delta;
                    j.PrevWrapped = wrapped;
                    value = j.Angle;
                    speed = wRel.Dot(axis);
                }

                j.Speed = speed;

                // Point constraint.
                if (j.Kind != JointKind.Slider)
                {
                    if (_pointCount == _points.Length) Array.Resize(ref _points, _points.Length * 2);
                    ref var pr = ref _points[_pointCount++];
                    pr.P = p;
                    pr.C = c;
                    pr.Joint = ji;
                    pr.RP = rP;
                    pr.RC = rC;
                    pr.Err = d;
                    var K = Mat3.Identity * stC.InvMass;
                    var sk = Mat3.Skew(rC);
                    K = K - sk * stC.InvI * sk;
                    if (p >= 0)
                    {
                        ref var stP = ref _state[p];
                        var skp = Mat3.Skew(rP);
                        K = K + Mat3.Identity * stP.InvMass - skp * stP.InvI * skp;
                    }

                    pr.Mass = K.Inverse();
                }
                else
                {
                    var b1 = axis.AnyPerpendicular();
                    var b2 = axis.Cross(b1);
                    var rPd = rP + d;
                    AddLinear(ji, RolePerp0, p, c, rPd, rC, b1, d.Dot(b1));
                    AddLinear(ji, RolePerp1, p, c, rPd, rC, b2, d.Dot(b2));
                }

                // Angular constraints.
                if (j.Kind == JointKind.Fixed || j.Kind == JointKind.Slider)
                {
                    var qe = qC * (qP * j.Rest).Conjugate;
                    double sgn = qe.W < 0 ? -1 : 1;
                    var e = new Vec3(qe.X, qe.Y, qe.Z) * (2 * sgn);
                    AddAngular(ji, RoleAng0, p, c, Vec3.UnitX, e.X);
                    AddAngular(ji, RoleAng1, p, c, Vec3.UnitY, e.Y);
                    AddAngular(ji, RoleAng2, p, c, Vec3.UnitZ, e.Z);
                }
                else if (j.Kind == JointKind.Hinge)
                {
                    var aC = qC.Rotate(j.AxisC);
                    var b1 = axis.AnyPerpendicular();
                    var b2 = axis.Cross(b1);
                    var err = axis.Cross(aC);
                    AddAngular(ji, RolePerp0, p, c, b1, err.Dot(b1));
                    AddAngular(ji, RolePerp1, p, c, b2, err.Dot(b2));
                }

                if (j.Kind != JointKind.Hinge && j.Kind != JointKind.Slider) continue;

                // Axis rows: Jacobian of the joint coordinate.
                Vec3 lp, ap, lc, ac;
                if (j.Kind == JointKind.Slider)
                {
                    lp = -axis;
                    ap = -(rP + d).Cross(axis);
                    lc = axis;
                    ac = rC.Cross(axis);
                }
                else
                {
                    lp = Vec3.Zero;
                    ap = -axis;
                    lc = Vec3.Zero;
                    ac = axis;
                }

                double mass = RowMass(p, c, lp, ap, lc, ac);
                bool angular = j.Kind == JointKind.Hinge;
                int lastRow = _rowCount;
                if (j.Lower.HasValue)
                {
                    AddRow(new Row
                    {
                        P = p, C = c, Joint = ji, Slot = RoleLower, LP = lp, AP = ap, LC = lc, AC = ac, Mass = mass, Err = value - j.Lower.Value,
                        Type = RowType.Limit, Angular = angular
                    });
                }
                else j.Impulse[RoleLower] = 0;

                if (j.Upper.HasValue)
                {
                    AddRow(new Row
                    {
                        P = p, C = c, Joint = ji, Slot = RoleUpper, LP = -lp, AP = -ap, LC = -lc, AC = -ac, Mass = mass, Err = j.Upper.Value - value,
                        Type = RowType.Limit, Angular = angular
                    });
                }
                else j.Impulse[RoleUpper] = 0;

                double maxImp = double.IsPositiveInfinity(j.MaxTorque) ? double.PositiveInfinity : Math.Max(0, j.MaxTorque) * h;
                if (j.Drive == JointDrive.Position && (j.Stiffness > 0 || j.Damping > 0))
                {
                    double kp = Math.Max(0, j.Stiffness), kd = Math.Max(0, j.Damping);
                    double den = kd + h * kp;
                    AddRow(new Row
                    {
                        P = p, C = c, Joint = ji, Slot = RoleDrive, LP = lp, AP = ap, LC = lc, AC = ac, Mass = mass, Err = value - j.TargetAngle,
                        Target = j.TargetSpeed, Lo = -maxImp, Hi = maxImp, Type = RowType.Spring, Gamma = 1 / (h * den), BiasCoef = kp / den, Angular = angular
                    });
                }
                else if (j.Drive == JointDrive.Velocity && j.Damping > 0)
                {
                    AddRow(new Row
                    {
                        P = p, C = c, Joint = ji, Slot = RoleDrive, LP = lp, AP = ap, LC = lc, AC = ac, Mass = mass, Err = 0, Target = j.TargetSpeed,
                        Lo = -maxImp, Hi = maxImp, Type = RowType.Spring, Gamma = 1 / (h * j.Damping), BiasCoef = 0, Angular = angular
                    });
                }
                else if (j.Drive == JointDrive.Velocity)
                {
                    AddRow(new Row
                    {
                        P = p, C = c, Joint = ji, Slot = RoleDrive, LP = lp, AP = ap, LC = lc, AC = ac, Mass = mass, Target = j.TargetSpeed, Lo = -maxImp,
                        Hi = maxImp, Type = RowType.Motor, Angular = angular
                    });
                }
                else j.Impulse[RoleDrive] = 0;

                if (j.Friction > 0)
                {
                    double f = j.Friction * h;
                    AddRow(new Row
                    {
                        P = p, C = c, Joint = ji, Slot = RoleFriction, LP = lp, AP = ap, LC = lc, AC = ac, Mass = mass, Target = 0, Lo = -f, Hi = f,
                        Type = RowType.Motor, Angular = angular
                    });
                }
                else j.Impulse[RoleFriction] = 0;

                // Hinge axis rows are solved on the point-constraint manifold: Schur-complement
                // mass and a compensating point impulse, so limits and drives on slender links
                // converge in a few iterations instead of fighting the pivot.
                if (j.Kind == JointKind.Hinge && _pointCount > 0 && _points[_pointCount - 1].Joint == ji)
                {
                    ref var pr = ref _points[_pointCount - 1];
                    for (int k = lastRow; k < _rowCount; k++)
                    {
                        ref var r = ref _rows[k];
                        var u = PointCoupling(p, c, rP, rC, r.LP, r.AP, r.LC, r.AC);
                        double kaa = r.Mass > 0 ? 1 / r.Mass : 0;
                        double keff = kaa - u.Dot(pr.Mass * u);
                        if (kaa <= 0 || keff <= 1e-6 * kaa) continue;
                        r.Mass = 1 / keff;
                        r.U = u;
                        r.PointPlus1 = _pointCount;
                    }
                }
            }
        }

        /// <summary>Change of the point-constraint velocity per unit impulse along a row.</summary>
        private Vec3 PointCoupling(int p, int c, Vec3 rP, Vec3 rC, Vec3 lp, Vec3 ap, Vec3 lc, Vec3 ac)
        {
            ref var sc = ref _state[c];
            var u = lc * sc.InvMass + (sc.InvI * ac).Cross(rC);
            if (p >= 0)
            {
                ref var sp = ref _state[p];
                u -= lp * sp.InvMass + (sp.InvI * ap).Cross(rP);
            }

            return u;
        }

        private void AddAngular(int ji, int slot, int p, int c, Vec3 dir, double err)
        {
            double mass = RowMass(p, c, Vec3.Zero, -dir, Vec3.Zero, dir);
            AddRow(new Row { P = p, C = c, Joint = ji, Slot = slot, AP = -dir, AC = dir, Mass = mass, Err = err, Type = RowType.Equality, Angular = true });
        }

        private void AddLinear(int ji, int slot, int p, int c, Vec3 rPd, Vec3 rC, Vec3 dir, double err)
        {
            var lp = -dir;
            var ap = -rPd.Cross(dir);
            var lc = dir;
            var ac = rC.Cross(dir);
            double mass = RowMass(p, c, lp, ap, lc, ac);
            AddRow(new Row { P = p, C = c, Joint = ji, Slot = slot, LP = lp, AP = ap, LC = lc, AC = ac, Mass = mass, Err = err, Type = RowType.Equality });
        }

        private void ApplyRow(ref Row r, double imp)
        {
            if (r.P >= 0)
            {
                ref var sp = ref _state[r.P];
                sp.V += r.LP * (imp * sp.InvMass);
                sp.W += sp.InvI * (r.AP * imp);
            }

            ref var sc = ref _state[r.C];
            sc.V += r.LC * (imp * sc.InvMass);
            sc.W += sc.InvI * (r.AC * imp);
        }

        private void ApplyJointImpulse(int p, int c, Vec3 rP, Vec3 rC, Vec3 imp)
        {
            ref var sc = ref _state[c];
            sc.V += imp * sc.InvMass;
            sc.W += sc.InvI * rC.Cross(imp);
            if (p >= 0)
            {
                ref var sp = ref _state[p];
                sp.V -= imp * sp.InvMass;
                sp.W -= sp.InvI * rP.Cross(imp);
            }
        }

        private void SolveJoints(bool useBias, double biasRate, double massScale, double impulseScale, double h)
        {
            double invH = 1 / h;
            // Friction and limits, then the drives (so a drive sees the velocity the limits leave),
            // then the rigid equality rows and the point blocks.
            for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < _rowCount; i++)
            {
                ref var r = ref _rows[i];
                int rowPass = r.Type == RowType.Equality ? 2 : r.Slot == RoleDrive ? 1 : 0;
                if (rowPass != pass) continue;
                var j = Joints[r.Joint];
                ref var sc = ref _state[r.C];
                double cdot = r.LC.Dot(sc.V) + r.AC.Dot(sc.W);
                if (r.P >= 0)
                {
                    ref var sp = ref _state[r.P];
                    cdot += r.LP.Dot(sp.V) + r.AP.Dot(sp.W);
                }

                double acc = j.Impulse[r.Slot], lambda, next;
                switch (r.Type)
                {
                    case RowType.Equality:
                        lambda = useBias ? -r.Mass * massScale * (cdot + biasRate * r.Err) - impulseScale * acc : -r.Mass * cdot;
                        next = acc + lambda;
                        break;
                    case RowType.Limit:
                    {
                        double bias = 0, ms = 1, isc = 0;
                        if (r.Err > 0) bias = r.Err * invH;
                        else if (useBias)
                        {
                            bias = biasRate * r.Err;
                            ms = massScale;
                            isc = impulseScale;
                        }

                        lambda = -r.Mass * ms * (cdot + bias) - isc * acc;
                        next = Math.Max(acc + lambda, 0);
                        break;
                    }

                    case RowType.Motor:
                        lambda = -r.Mass * (cdot - r.Target);
                        next = Math.Max(r.Lo, Math.Min(r.Hi, acc + lambda));
                        break;
                    default:
                    {
                        double k = r.Mass > 0 ? 1 / r.Mass : 0;
                        lambda = -(cdot - r.Target + r.BiasCoef * r.Err + r.Gamma * acc) / (k + r.Gamma);
                        next = Math.Max(r.Lo, Math.Min(r.Hi, acc + lambda));
                        break;
                    }
                }

                double delta = next - acc;
                j.Impulse[r.Slot] = next;
                if (delta != 0)
                {
                    ApplyRow(ref r, delta);
                    if (r.PointPlus1 > 0)
                    {
                        ref var pr = ref _points[r.PointPlus1 - 1];
                        var dp = -(pr.Mass * r.U) * delta;
                        j.PointImpulse += dp;
                        ApplyJointImpulse(pr.P, pr.C, pr.RP, pr.RC, dp);
                    }
                }
            }

            for (int i = 0; i < _pointCount; i++)
            {
                ref var pr = ref _points[i];
                var j = Joints[pr.Joint];
                ref var sc = ref _state[pr.C];
                var cdot = sc.V + sc.W.Cross(pr.RC);
                if (pr.P >= 0)
                {
                    ref var sp = ref _state[pr.P];
                    cdot -= sp.V + sp.W.Cross(pr.RP);
                }

                Vec3 imp;
                if (useBias) imp = -(pr.Mass * (cdot + pr.Err * biasRate)) * massScale - j.PointImpulse * impulseScale;
                else imp = -(pr.Mass * cdot);
                j.PointImpulse += imp;
                ApplyJointImpulse(pr.P, pr.C, pr.RP, pr.RC, imp);
            }
        }

        private void FinishJoints(double h)
        {
            // Reaction loads, drive torque and breakage.
            for (int ji = 0; ji < Joints.Count; ji++)
            {
                var j = Joints[ji];
                j.SubForce = j.Enabled && !j.Broken ? j.PointImpulse : Vec3.Zero;
                j.SubTorque = Vec3.Zero;
                j.SubDrive = 0;
            }

            for (int i = 0; i < _rowCount; i++)
            {
                ref var r = ref _rows[i];
                var j = Joints[r.Joint];
                double imp = j.Impulse[r.Slot];
                if (r.Slot == RoleDrive) j.SubDrive = imp;
                if (r.Angular) j.SubTorque += r.AC * imp;
                else j.SubForce += r.LC * imp;
            }

            for (int ji = 0; ji < Joints.Count; ji++)
            {
                var j = Joints[ji];
                if (!j.Enabled || j.Broken || JointAsleep(j)) continue; // [phys]
                var force = j.SubForce;
                var torque = j.SubTorque;
                j.StepDriveImpulse += j.SubDrive;
                j.StepForceImpulse += force;
                j.StepTorqueImpulse += torque;
                double f = force.Length / h, t = torque.Length / h;
                double a = h / (1e-3 + h);
                j.ForceFilter += (f - j.ForceFilter) * a;
                j.TorqueFilter += (t - j.TorqueFilter) * a;
                if (j.ForceFilter > j.BreakForce || j.TorqueFilter > j.BreakTorque)
                {
                    j.Broken = true;
                    bool byForce = j.ForceFilter > j.BreakForce;
                    double value = byForce ? j.ForceFilter : j.TorqueFilter;
                    Report(new PhysicsEvent(Time, j.Id, PhysicsEventKind.JointBroken,
                        byForce ? $"bağlantı koptu ({value:F1} N)" : $"bağlantı koptu ({value:F2} N·m)", value, true));
                }
            }
        }

        // ================================================================== queries

        /// <summary>Nearest hit along a ray (bodies and ground). <paramref name="filter"/> may exclude bodies.</summary>
        public bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, out SceneRayHit hit, Func<SceneBody, bool>? filter = null, bool ground = true)
        {
            hit = default;
            var d = direction.Normalized();
            if (d.LengthSquared < 0.5) return false;
            double best = maxDistance;
            bool found = false;
            foreach (var b in Bodies)
            {
                var shape = b.EffectiveShape;
                if (shape == null || (filter != null && !filter(b))) continue;
                if (shape.Kind == ShapeKind.Points && shape.Planes.Length == 0) continue;
                var oc = b.Position - origin;
                double tc = oc.Dot(d);
                double r = shape.BoundingRadius;
                if (tc < -r || tc - r > best) continue;
                if ((oc - d * tc).LengthSquared > r * r) continue;
                var lo = b.Orientation.InverseRotate(origin - b.Position);
                var ld = b.Orientation.InverseRotate(d);
                if (!shape.RaycastLocal(lo, ld, best, out double t, out var nl) || t >= best) continue;
                best = t;
                found = true;
                hit = new SceneRayHit { Distance = t, Point = origin + d * t, Normal = b.Orientation.Rotate(nl), Body = b, Material = b.Material };
            }

            if (ground && GroundEnabled)
            {
                double t = Terrain != null ? Terrain.Raycast(origin, d, best) : (d.Z < -1e-12 && origin.Z >= 0 ? -origin.Z / d.Z : origin.Z < 0 ? 0 : -1);
                if (t >= 0 && t < best)
                {
                    best = t;
                    found = true;
                    var p = origin + d * t;
                    hit = new SceneRayHit { Distance = t, Point = p, Normal = GroundNormal(p.X, p.Y), Body = null, Material = GroundMaterialAt(p.X, p.Y) };
                }
            }

            return found;
        }

        /// <summary>True when the point lies inside a solid body (other than <paramref name="ignore"/>) or under the ground.</summary>
        public bool IsInsideSolid(Vec3 p, double margin = 0, RigidBody? ignore = null)
        {
            if (GroundEnabled && TerrainSeparation(p, out _) < margin) return true;
            foreach (var b in Bodies)
            {
                if (b == ignore || !b.Collides) continue;
                var s = b.EffectiveShape;
                if (s == null || !s.HasSolid) continue;
                var local = b.Orientation.InverseRotate(p - b.Position);
                if (local.LengthSquared > (s.BoundingRadius + margin) * (s.BoundingRadius + margin)) continue;
                if (s.SignedDistance(local, margin, out double d, out _) && d < margin) return true;
            }

            return false;
        }

        /// <summary>
        /// Ground probe for a tyre: the deepest of a fan of rays in the wheel plane (straight down
        /// plus ±30° and ±60° when there are slopes or obstacles nearby).
        /// </summary>
        internal bool ProbeWheel(RigidBody owner, Vec3 hub, Vec3 down, Vec3 forward, double radius, out SceneRayHit hit, out Vec3 rayDir)
        {
            hit = default;
            rayDir = down;
            _nearby.Clear();
            double reach = radius + 0.02;
            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                var s = i < _shapes.Length ? _shapes[i] : null;
                if (b == owner || s == null || !s.HasSolid || b.Slot != i) continue;
                if (_lo[i].X > hub.X + reach || _hi[i].X < hub.X - reach || _lo[i].Y > hub.Y + reach || _hi[i].Y < hub.Y - reach ||
                    _lo[i].Z > hub.Z + reach || _hi[i].Z < hub.Z - reach) continue;
                _nearby.Add(i);
            }

            bool flat = Terrain == null || (Terrain is PlaneTerrain pt && pt.IsFlat);
            int rays = _nearby.Count == 0 && flat ? 1 : 5;
            double bestPen = double.NegativeInfinity;
            bool found = false;
            for (int k = 0; k < rays; k++)
            {
                double ang = rays == 1 ? 0 : (k - 2) * (Math.PI / 6);
                var dir = rays == 1 ? down : (down * Math.Cos(ang) + forward * Math.Sin(ang)).Normalized();
                if (!ProbeRay(hub, dir, radius, out var h)) continue;
                double pen = radius - h.Distance;
                if (pen > bestPen)
                {
                    bestPen = pen;
                    hit = h;
                    rayDir = dir;
                    found = true;
                }
            }

            return found;
        }

        private bool ProbeRay(Vec3 origin, Vec3 d, double maxDistance, out SceneRayHit hit)
        {
            hit = default;
            double best = maxDistance;
            bool found = false;
            foreach (int i in _nearby)
            {
                var b = Bodies[i];
                var shape = _shapes[i]!;
                var lo = b.Orientation.InverseRotate(origin - b.Position);
                var ld = b.Orientation.InverseRotate(d);
                if (!shape.RaycastLocal(lo, ld, best, out double t, out var nl) || t >= best) continue;
                best = t;
                found = true;
                hit = new SceneRayHit { Distance = t, Point = origin + d * t, Normal = b.Orientation.Rotate(nl), Body = b, Material = b.Material };
            }

            if (GroundEnabled)
            {
                double t;
                if (Terrain == null) t = d.Z < -1e-12 ? (origin.Z <= 0 ? 0 : -origin.Z / d.Z) : (origin.Z < 0 ? 0 : -1);
                else t = Terrain.Raycast(origin, d, best);
                if (t >= 0 && t < best)
                {
                    found = true;
                    var p = origin + d * t;
                    hit = new SceneRayHit { Distance = t, Point = p, Normal = GroundNormal(p.X, p.Y), Body = null, Material = GroundMaterialAt(p.X, p.Y) };
                }
            }

            return found;
        }

        /// <summary>Total kinetic energy of the dynamic bodies (J).</summary>
        public double KineticEnergy()
        {
            double e = 0;
            foreach (var b in Bodies) e += b.KineticEnergy;
            return e;
        }

        /// <summary>Total gravitational potential energy relative to z = 0 (J).</summary>
        public double PotentialEnergy()
        {
            double e = 0;
            foreach (var b in Bodies)
                if (!b.IsStatic)
                    e -= b.Mass * Gravity.Dot(b.Position);
            return e;
        }
    }
}
