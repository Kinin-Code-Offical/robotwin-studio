using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Robotics
{
    /// <summary>[kin] What turns or slides at one axis of an arm built in a world.</summary>
    public enum WorldAxisKind
    {
        /// <summary>A servo's horn: the firmware sets its angle (0–180°, 90° = as built).</summary>
        Servo,

        /// <summary>A motor's shaft: it spins at a speed, it does not hold an angle.</summary>
        Shaft,

        /// <summary>A hinge joint the user made (a bearing): free, physics decides its angle.</summary>
        Hinge,

        /// <summary>A slider joint the user made (a rail): free, physics decides its travel.</summary>
        Slider
    }

    /// <summary>[kin] One movable axis on the way from an arm's base to its tip.</summary>
    public sealed class WorldArmAxis
    {
        public string Name { get; internal set; } = string.Empty;
        public WorldAxisKind Kind { get; internal set; }

        /// <summary>The servo or motor whose output this is; for a hinge or slider, the object the joint moves.</summary>
        public string ObjectId { get; internal set; } = string.Empty;

        /// <summary>The world joint of a hinge or slider; null for a servo horn or a motor shaft.</summary>
        public string? JointId { get; internal set; }

        /// <summary>
        /// The axis in the base frame as built (product-of-exponentials form). Its value is the physics' own joint value
        /// (<see cref="WorldPhysics.PortAngle"/>, a scene joint's angle), so the direction is flipped where the chain runs
        /// through a joint from child to parent.
        /// </summary>
        public ArmJointSpec Spec { get; internal set; } = new ArmJointSpec();

        /// <summary>
        /// [drive] A hinge or slider with a position motor (an imported robot's axis, <see cref="WorldJoint.Drive"/>): it holds
        /// the value it is told, like a servo; <see cref="WorldPhysics.SetJointTarget"/> tells it.
        /// </summary>
        public bool Motor { get; internal set; }

        /// <summary>
        /// Only a servo or a motorised joint holds an angle it is told: inverse kinematics solves those, the other axes stay
        /// where they are.
        /// </summary>
        public bool Actuated => Kind == WorldAxisKind.Servo || Motor;

        /// <summary>The drive's stall torque (N·m) for a servo; 0 for a free axis.</summary>
        public double StallTorque { get; internal set; }

        /// <summary>The servo command (°) for a horn angle (rad): the horn is built at the servo's centre, 90°.</summary>
        public static double ServoDegrees(double q) => 90 + q * 180 / Math.PI;

        /// <summary>The horn angle (rad) of a servo command (°).</summary>
        public static double FromServoDegrees(double degrees) => (degrees - 90) * Math.PI / 180;

        /// <summary>The value as people read it: a servo's command in degrees, a hinge in degrees, a slider in millimetres.</summary>
        public double Display(double q) => Kind == WorldAxisKind.Servo ? ServoDegrees(q) : Spec.ToDisplay(q);

        public string Unit => Spec.Unit;
    }

    /// <summary>[kin] An inverse-kinematics answer for a world arm.</summary>
    public sealed class WorldArmSolution
    {
        public bool Success { get; internal set; }

        /// <summary>Every axis' value (rad / m): solved servos, the free axes as they were.</summary>
        public double[] Q { get; internal set; } = Array.Empty<double>();

        /// <summary>Where the tip gets (world, the base as built) and how far that is from the target (m).</summary>
        public Vec3 Reached { get; internal set; }

        public double Error { get; internal set; }

        /// <summary>Distance from a singular pose, 0–1 (see <see cref="WorldArm.Dexterity"/>).</summary>
        public double Dexterity { get; internal set; }

        /// <summary>
        /// How far the target lay off the plane the tip of an arm with parallel servos moves in (m): such an arm is sent to
        /// the target's foot on that plane, and <see cref="Success"/> tells whether it gets there.
        /// </summary>
        public double OffPlane { get; internal set; }

        /// <summary>Why it failed (or how the target was taken), in Turkish; empty when it simply got there.</summary>
        public string Message { get; internal set; } = string.Empty;

        /// <summary>
        /// [kin] Why the target was missed: "sınır" (inside the arm's span, but an axis would have to go past its limit —
        /// <see cref="LimitAxis"/>), "uzak" (beyond the farthest the arm stretches), "yakın" (nearer the first axis than the
        /// arm can fold), or empty when it got there or the reason is not one of these.
        /// </summary>
        public string Cause { get; internal set; } = string.Empty;

        /// <summary>The axis whose limit stood in the way (index into <see cref="WorldArm.Axes"/>), or -1.</summary>
        public int LimitAxis { get; internal set; } = -1;
    }

    /// <summary>[kin] Where a world arm's tip can get: sampled over the servos' travel.</summary>
    public sealed class WorldArmReach
    {
        public List<Vec3> Points { get; } = new List<Vec3>();
        public Vec3 Min { get; internal set; }
        public Vec3 Max { get; internal set; }

        /// <summary>Nearest and farthest tip distance (m) from the first servo's axis.</summary>
        public double Nearest { get; internal set; }

        public double Farthest { get; internal set; }

        /// <summary>True when every servo axis is parallel: the tip moves in one plane (normal <see cref="PlaneNormal"/>).</summary>
        public bool Planar { get; internal set; }

        public Vec3 PlaneNormal { get; internal set; }
    }

    /// <summary>
    /// [kin] Kinematics of an arm the user built in a world: servos whose horns carry links, hinges and sliders, found by
    /// following the world's joints from a base to a tip object. The axes are taken as built (the world document's poses
    /// are the zero configuration), so forward and inverse kinematics, the Jacobian, the static load on each servo and
    /// the reach work on any chain with the same <see cref="ArmKinematics"/> the industrial cells use, and its values are
    /// the physics' own joint values, so a measurement and a prediction can be compared.
    /// </summary>
    public sealed class WorldArm
    {
        private const string Ground = "\u0000ground";

        private readonly List<(Vec3 Com, double Mass, int Level)> _masses = new List<(Vec3, double, int)>();

        private WorldArm()
        {
        }

        /// <summary>The base object (fixed to the ground or the heaviest of a free assembly).</summary>
        public string BaseId { get; private set; } = string.Empty;

        /// <summary>True when the base is held by the ground (a fixed object or a ground joint): its pose never changes.</summary>
        public bool Grounded { get; private set; }

        public string TipId { get; private set; } = string.Empty;

        /// <summary>The tool point in the tip object's frame (m).</summary>
        public Vec3 TipLocal { get; private set; }

        /// <summary>The objects from the base to the tip.</summary>
        public IReadOnlyList<string> Path { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<WorldArmAxis> Axes { get; private set; } = Array.Empty<WorldArmAxis>();

        /// <summary>The chain as an arm model in the world frame as built (flange = the tool point, the tip's orientation).</summary>
        public ArmModel Model { get; private set; } = new ArmModel();

        /// <summary>The base object's pose as built.</summary>
        public Transform3 BaseBuilt { get; private set; } = Transform3.Identity;

        public double Gravity { get; private set; } = 9.81;

        public int ActuatedCount => Axes.Count(a => a.Actuated);

        // ================================================================ building the chain

        /// <summary>
        /// The chain from a base to <paramref name="tipId"/>, or null when the tip is not joined to anything that moves
        /// it. Without <paramref name="baseId"/> the base is the nearest object held by the ground, else the heaviest
        /// object of the assembly. Without <paramref name="tipLocal"/> the tool point is the middle of the tip's face
        /// farthest from its last axis (the end of an arm link).
        /// </summary>
        public static WorldArm? Build(WorldDocument world, Func<WorldObject, WorldBodyGeometry> geometry, string tipId, string? baseId = null, Vec3? tipLocal = null)
        {
            var tip = world.Find(tipId);
            if (tip == null) return null;
            var geo = new Dictionary<string, WorldBodyGeometry>(StringComparer.OrdinalIgnoreCase);
            WorldBodyGeometry G(WorldObject o)
            {
                if (!geo.TryGetValue(o.Id, out var g)) geo[o.Id] = g = geometry(o);
                return g;
            }

            // The joints as an undirected graph; the ground is one more node, holding fixed objects and ground joints.
            var edges = new Dictionary<string, List<(WorldJoint? Joint, string Other)>>(StringComparer.OrdinalIgnoreCase);
            void Link(string a, WorldJoint? j, string b)
            {
                if (!edges.TryGetValue(a, out var l)) edges[a] = l = new List<(WorldJoint?, string)>();
                l.Add((j, b));
            }

            foreach (var j in world.Joints)
            {
                if (world.Find(j.ObjectA) == null) continue;
                if (j.ObjectB == null)
                {
                    Link(j.ObjectA, j, Ground);
                    Link(Ground, j, j.ObjectA);
                }
                else if (world.Find(j.ObjectB) != null && !string.Equals(j.ObjectA, j.ObjectB, StringComparison.OrdinalIgnoreCase))
                {
                    Link(j.ObjectA, j, j.ObjectB);
                    Link(j.ObjectB, j, j.ObjectA);
                }
            }

            foreach (var o in world.Objects.Where(o => o.IsStatic))
            {
                Link(o.Id, null, Ground);
                Link(Ground, null, o.Id);
            }

            // What the tip is joined to.
            var hops = Bfs(edges, tipId, out _);
            string root;
            if (baseId != null)
            {
                if (!hops.ContainsKey(baseId)) return null;
                root = baseId;
            }
            else if (hops.ContainsKey(Ground))
            {
                root = Ground;
            }
            else
            {
                // Free-standing: the base is what everything else is mounted on (a part no joint hangs on anything here),
                // the heaviest such one; the selected part itself when it is that base (then it carries no arm).
                var objects = hops.Keys.Select(world.Find).Where(o => o != null).Select(o => o!).ToList();
                if (objects.Count < 2) return null;
                bool Mounted(WorldObject o) => world.Joints.Any(j => string.Equals(j.ObjectA, o.Id, StringComparison.OrdinalIgnoreCase) &&
                                                                     j.ObjectB != null && hops.ContainsKey(j.ObjectB));
                var bases = objects.Where(o => !Mounted(o)).ToList();
                if (bases.Count == 0) bases = objects;
                root = bases.OrderByDescending(o => o.MassKg ?? G(o).MassKg).ThenByDescending(o => hops[o.Id]).First().Id;
                if (string.Equals(root, tipId, StringComparison.OrdinalIgnoreCase)) return null;
            }

            // The path from the base to the tip.
            Bfs(edges, root, out var pred);
            var steps = new List<(string From, WorldJoint? Joint, string To)>();
            for (string at = tipId; !string.Equals(at, root, StringComparison.OrdinalIgnoreCase);)
            {
                if (!pred.TryGetValue(at, out var p)) return null;
                steps.Add((p.From, p.Joint, at));
                at = p.From;
            }

            steps.Reverse();
            var arm = new WorldArm { TipId = tipId, Gravity = world.Environment?.Gravity ?? 9.81 };
            arm.Grounded = root == Ground;
            arm.BaseId = arm.Grounded ? (steps.Count > 0 ? steps[0].To : tipId) : root;
            var baseObject = world.Find(arm.BaseId);
            arm.BaseBuilt = arm.Grounded || baseObject == null ? Transform3.Identity : new Transform3(baseObject.Pose.Position, baseObject.Pose.Orientation);
            var path = new List<string>();
            if (!arm.Grounded) path.Add(root);
            path.AddRange(steps.Select(s => s.To));
            arm.Path = path;

            // The axes along the path, in order from the base.
            var axes = new List<WorldArmAxis>();
            var stepAxes = new Dictionary<(string, string), int>();   // (from, to) of a path step → index of its last axis
            foreach (var (from, j, to) in steps)
            {
                if (j == null) continue;
                bool forward = string.Equals(j.ObjectA, to, StringComparison.OrdinalIgnoreCase);   // parent (B or the ground) → child (A)
                var found = new List<WorldArmAxis>();
                // A servo horn or a motor shaft: the joint holds A to B's moving output.
                var parent = j.ObjectB != null ? world.Find(j.ObjectB) : null;
                if (parent != null && !string.IsNullOrEmpty(j.PortB) && G(parent) is { PortName: { } port } pg && port == j.PortB)
                {
                    bool servo = port == "horn";
                    var axis = new WorldArmAxis
                    {
                        Name = parent.Name,
                        Kind = servo ? WorldAxisKind.Servo : WorldAxisKind.Shaft,
                        ObjectId = parent.Id,
                        Spec = new ArmJointSpec
                        {
                            Name = parent.Name,
                            Axis = parent.Pose.Orientation.Rotate(pg.PortAxis).Normalized(),
                            Point = parent.Pose.ToWorld(pg.PortPivot),
                            Min = servo ? -Math.PI / 2 : -1e9, Max = servo ? Math.PI / 2 : 1e9,
                        }
                    };
                    if (servo)
                    {
                        parent.Parameters.TryGetValue("model", out var servoModel);
                        var drive = ServoMotor.ForModel(servoModel ?? parent.PresetId);
                        axis.StallTorque = drive.StallTorque;
                        axis.Spec.MaxTorque = drive.StallTorque;
                        axis.Spec.MaxSpeed = drive.NoLoadSpeed;
                    }

                    found.Add(axis);
                }

                if (j.Kind is WorldJointKind.Hinge or WorldJointKind.Slider)
                {
                    var child = world.Find(j.ObjectA)!;
                    var dir = new Vec3(j.AxisX, j.AxisY, j.AxisZ);
                    if (dir.Length < 1e-9) dir = Vec3.UnitZ;
                    var cg = G(child);
                    var anchor = j.AnchorX == 0 && j.AnchorY == 0 && j.AnchorZ == 0 ? child.Pose.ToWorld((cg.Min + cg.Max) * 0.5) : new Vec3(j.AnchorX, j.AnchorY, j.AnchorZ);
                    bool hinge = j.Kind == WorldJointKind.Hinge;
                    double lo = j.Lower ?? (hinge ? -180 : -0.1), hi = j.Upper ?? (hinge ? 180 : 0.1);
                    // [drive] A position motor makes it an axis inverse kinematics can set, as strong as its drive.
                    bool motor = j.Driven && j.Drive == WorldJoint.DrivePosition && j.MimicOf == null;   // a mimic follows, it is not set
                    double torque = j.DriveForce > 0 ? j.DriveForce : hinge ? 10 : 200;
                    double speed = j.DriveSpeed > 0 ? j.DriveSpeed : hinge ? 180 : 0.5;
                    found.Add(new WorldArmAxis
                    {
                        Name = motor ? child.Name : (hinge ? "Menteşe " : "Ray ") + child.Name,   // a robot's axis by its link's name
                        Kind = hinge ? WorldAxisKind.Hinge : WorldAxisKind.Slider,
                        Motor = motor,
                        StallTorque = motor ? torque : 0,
                        ObjectId = child.Id,
                        JointId = j.Id,
                        Spec = new ArmJointSpec
                        {
                            Name = child.Name,
                            Type = hinge ? ArmJointType.Revolute : ArmJointType.Prismatic,
                            Axis = dir.Normalized(),
                            Point = anchor,
                            Min = hinge ? lo * Math.PI / 180 : lo, Max = hinge ? hi * Math.PI / 180 : hi,
                            MaxTorque = motor ? torque : 0,
                            MaxSpeed = motor ? (hinge ? speed * Math.PI / 180 : speed) : Math.PI
                        }
                    });
                }

                // From the child's side the horn turns the other way round (and comes after the hinge).
                if (!forward)
                {
                    found.Reverse();
                    foreach (var a in found) a.Spec.Axis = -a.Spec.Axis;
                }

                axes.AddRange(found);
                stepAxes[(from, to)] = axes.Count - 1;
            }

            arm.Axes = axes;

            // The tool point: the middle of the tip's face farthest from the last axis.
            var tg = G(tip);
            if (tipLocal is { } given)
            {
                arm.TipLocal = given;
            }
            else
            {
                var c = (tg.Min + tg.Max) * 0.5;
                var from = axes.Count > 0 ? axes[^1].Spec.Point : tip.Pose.ToWorld(c);
                var faces = new[]
                {
                    new Vec3(tg.Min.X, c.Y, c.Z), new Vec3(tg.Max.X, c.Y, c.Z), new Vec3(c.X, tg.Min.Y, c.Z),
                    new Vec3(c.X, tg.Max.Y, c.Z), new Vec3(c.X, c.Y, tg.Min.Z), new Vec3(c.X, c.Y, tg.Max.Z)
                };
                arm.TipLocal = faces.OrderByDescending(f => (tip.Pose.ToWorld(f) - from).Length).First();
            }

            var model = new ArmModel
            {
                Id = "world:" + tipId,
                Name = tip.Name,
                Ik = ArmIkKind.Numeric,
                PositionOnly = true,
                ServoDriven = true,
                Flange = new Transform3(tip.Pose.ToWorld(arm.TipLocal), tip.Pose.Orientation),
                Home = new double[axes.Count],
            };
            foreach (var a in axes)
            {
                model.Joints.Add(a.Spec);
                model.Links.Add(new ArmLinkSpec { Name = a.Name });
            }

            arm.Model = model;

            // Every object of the assembly moves with the last path axis between it and the base (side branches as built).
            var level = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [root] = -1 };
            var queue = new Queue<string>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                string at = queue.Dequeue();
                if (!edges.TryGetValue(at, out var next)) continue;
                foreach (var (_, other) in next)
                {
                    if (level.ContainsKey(other) || other == Ground) continue;
                    level[other] = stepAxes.TryGetValue((at, other), out int idx) ? idx : level[at];
                    queue.Enqueue(other);
                }
            }

            foreach (var (id, lv) in level)
            {
                if (id == Ground || world.Find(id) is not { } o) continue;
                if (arm.Grounded && o.IsStatic) continue;   // held by the ground: no load on any axis
                var g = G(o);
                arm._masses.Add((o.Pose.ToWorld((g.Min + g.Max) * 0.5), o.MassKg ?? g.MassKg, lv));
            }

            return arm;
        }

        /// <summary>Breadth-first hops from <paramref name="start"/> and the step each node was reached by.</summary>
        private static Dictionary<string, int> Bfs(Dictionary<string, List<(WorldJoint? Joint, string Other)>> edges, string start, out Dictionary<string, (string From, WorldJoint? Joint)> pred)
        {
            var hops = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [start] = 0 };
            pred = new Dictionary<string, (string, WorldJoint?)>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                string at = queue.Dequeue();
                if (!edges.TryGetValue(at, out var next)) continue;
                foreach (var (j, other) in next)
                {
                    if (hops.ContainsKey(other)) continue;
                    hops[other] = hops[at] + 1;
                    pred[other] = (at, j);
                    // Nothing passes through the ground: two parts both held by it are not a chain.
                    if (other != Ground) queue.Enqueue(other);
                }
            }

            return hops;
        }

        // ================================================================ forward kinematics

        /// <summary>The tool point (world, with the base where it was built) for axis values <paramref name="q"/>.</summary>
        public Vec3 Tip(IReadOnlyList<double> q) => ArmKinematics.Forward(Model, q).P;

        /// <summary>How the base has moved since it was built (identity for a grounded base or before a run).</summary>
        public Transform3 BaseMotion(WorldPhysics? physics)
        {
            if (Grounded || physics?.PoseOf(BaseId) is not { } pose) return Transform3.Identity;
            return new Transform3(pose.Position, pose.Orientation) * BaseBuilt.Inverse();
        }

        /// <summary>The axis values the physics has now (rad / m; zeros before a run).</summary>
        public double[] Measure(WorldPhysics? physics)
        {
            var q = new double[Axes.Count];
            if (physics == null) return q;
            for (int i = 0; i < Axes.Count; i++)
            {
                var a = Axes[i];
                double v = a.JointId == null ? physics.PortAngle(a.ObjectId) : physics.Scene.Joints.FirstOrDefault(x => x.Id == a.JointId)?.Angle ?? 0;
                // A joint met from its child's side was given the opposite axis, so the same value turns it the same way.
                q[i] = v;
            }

            return q;
        }

        /// <summary>Where the physics has the tool point now (world), or null before a run.</summary>
        public Vec3? MeasuredTip(WorldPhysics? physics) =>
            physics?.PoseOf(TipId) is { } pose ? pose.Position + pose.Orientation.Rotate(TipLocal) : (Vec3?)null;

        /// <summary>The tool point the model predicts in the world now: the measured axis values on the base as it stands.</summary>
        public Vec3 PredictedTip(WorldPhysics? physics) => BaseMotion(physics).Apply(Tip(Measure(physics)));

        /// <summary>
        /// Distance from a singular pose, 0–1: the volume the servo axes' tip velocities span over the product of their
        /// lengths (1 = they push the tip in perpendicular directions, 0 = two of them push it the same way, or one cannot
        /// move it at all).
        /// </summary>
        public double Dexterity(IReadOnlyList<double> q)
        {
            var cols = new List<Vec3>();
            var tip = Tip(q);
            var jac = ArmKinematics.Jacobian(Model, q, tip);
            for (int i = 0; i < Axes.Count; i++)
            {
                if (Axes[i].Actuated) cols.Add(new Vec3(jac[0, i], jac[1, i], jac[2, i]));
            }

            if (cols.Count == 0) return 0;
            if (cols.Count <= 3) return Hadamard(cols);
            // More than three servos: the best three of them.
            double best = 0;
            for (int a = 0; a < cols.Count; a++)
            {
                for (int b = a + 1; b < cols.Count; b++)
                {
                    for (int c = b + 1; c < cols.Count; c++) best = Math.Max(best, Hadamard(new List<Vec3> { cols[a], cols[b], cols[c] }));
                }
            }

            return best;
        }

        /// <summary>The volume the vectors span over the product of their lengths (Hadamard ratio): 1 = perpendicular, 0 = dependent.</summary>
        private static double Hadamard(List<Vec3> cols)
        {
            int n = cols.Count;
            double norms = 1;
            foreach (var c in cols) norms *= c.LengthSquared;
            if (norms < 1e-30) return 0;
            var gram = new double[n, n];
            for (int a = 0; a < n; a++)
            {
                for (int b = 0; b < n; b++) gram[a, b] = cols[a].Dot(cols[b]);
            }

            return Math.Sqrt(Math.Max(0, Math.Min(1, ArmKinematics.Determinant(gram) / norms)));
        }

        /// <summary>
        /// Static load on each axis at <paramref name="q"/>: the torque (N·m) or force (N) the weight of everything it
        /// carries puts on it, with the base as built (gravity along −Z). A servo holds the arm still by giving the opposite.
        /// </summary>
        public double[] GravityLoad(IReadOnlyList<double> q)
        {
            var load = new double[Axes.Count];
            var moves = ArmKinematics.LinkTransforms(Model, q);
            for (int i = 0; i < Axes.Count; i++)
            {
                var before = i == 0 ? Transform3.Identity : moves[i - 1];
                var spec = Axes[i].Spec;
                var axis = before.ApplyVector(spec.Axis);
                var point = before.Apply(spec.Point);
                double sum = 0;
                foreach (var (com, mass, lv) in _masses)
                {
                    if (lv < i) continue;
                    var c = moves[lv].Apply(com);
                    var weight = new Vec3(0, 0, -mass * Gravity);
                    sum += spec.IsPrismatic ? weight.Dot(axis) : (c - point).Cross(weight).Dot(axis);
                }

                load[i] = sum;
            }

            return load;
        }

        // ================================================================ inverse kinematics

        /// <summary>
        /// The servo angles that put the tool point at <paramref name="target"/> (world, the base where it was built),
        /// nearest to <paramref name="current"/>: free axes stay as they are (physics decides them), a target out of reach
        /// gives the nearest point the arm gets to.
        /// </summary>
        public WorldArmSolution Solve(Vec3 target, IReadOnlyList<double> current)
        {
            var result = new WorldArmSolution { Q = current.ToArray() };
            if (ActuatedCount == 0)
            {
                result.Message = "Bu zincirde servo ya da motorlu eklem yok: açısı söylenebilen bir eksen gerekir.";
                result.Reached = Tip(current);
                result.Error = (result.Reached - target).Length;
                return result;
            }

            // Parallel servos keep the tip in one plane: aim at the target's foot on it (a z typed 0.1 mm off is no failure).
            var plane = Reach(current, 64);
            var aim = target;
            if (plane.Planar)
            {
                double off = (target - Tip(current)).Dot(plane.PlaneNormal);
                aim = target - plane.PlaneNormal * off;
                result.OffPlane = Math.Abs(off);
            }

            var (model, map) = Reduced(current);
            var goal = new Transform3(aim, Quat.Identity);
            var seeds = new List<double[]> { map.Select(i => current[i]).ToArray(), new double[model.Dof] };
            var rng = new Random(7);
            for (int s = 0; s < 10; s++)
            {
                seeds.Add(model.Joints.Select(j => j.Min + (j.Max - j.Min) * rng.NextDouble()).ToArray());
            }

            IkResult? best = null;
            double bestMove = double.PositiveInfinity;
            foreach (var seed in seeds)
            {
                var r = ArmKinematics.SolveDls(model, goal, seed, Transform3.Identity, 300);
                double move = 0;
                for (int k = 0; k < map.Length; k++) move += Math.Abs(r.Joints[k] - current[map[k]]);
                bool better = best == null
                              || (r.Success && !best.Success)
                              || (r.Success == best.Success && (r.Success ? move < bestMove - 1e-9 : r.PositionError < best.PositionError - 1e-9));
                if (better)
                {
                    best = r;
                    bestMove = move;
                }
            }

            var q = current.ToArray();
            for (int k = 0; k < map.Length; k++) q[map[k]] = best!.Joints[k];
            result.Q = q;
            result.Reached = Tip(q);
            result.Error = (result.Reached - target).Length;
            double miss = (result.Reached - aim).Length;
            result.Success = miss < 1e-4;
            result.Dexterity = Dexterity(q);
            string where = Math.Abs(Math.Abs(plane.PlaneNormal.Z) - 1) < 1e-6 ? $"z = {result.Reached.Z * 1000:0.#} mm düzleminde" : "tek bir düzlemde";
            if (!result.Success)
            {
                WhyNot(result, model, map, goal, aim, current, miss);
                if (result.OffPlane > 1e-3) result.Message += $" Eksenleri paralel olduğundan uç {where} gezer; hedef bu düzlemin {result.OffPlane * 1000:0.#} mm dışındaydı.";
            }
            else if (result.OffPlane > 1e-3)
            {
                result.Message = $"Eksenleri paralel olduğundan uç {where} gezer: hedefin {result.OffPlane * 1000:0.#} mm dışındaki düzleme izdüşümüne gidildi.";
            }

            return result;
        }

        /// <summary>
        /// [kin] Why a target was missed, said plainly (the roadmap's "kolun hedefe neden ulaşamadığı"): solved again with every
        /// turning axis free all round, a target it then reaches lies inside the arm's span and an axis' limit is in the way
        /// (which one, the angle it would need and its range); otherwise the target is beyond the farthest the arm stretches,
        /// or nearer its first axis than it can fold, measured as the reach is.
        /// </summary>
        private void WhyNot(WorldArmSolution result, ArmModel model, int[] map, Transform3 goal, Vec3 aim, IReadOnlyList<double> current, double miss)
        {
            string nearest = $" Ulaşabildiği en yakın nokta hedeften {miss * 1000:0.#} mm uzakta.";
            var free = new ArmModel { Id = model.Id, Name = model.Name, Ik = model.Ik, PositionOnly = model.PositionOnly, ServoDriven = model.ServoDriven, Flange = model.Flange };
            foreach (var j in model.Joints)
            {
                free.Joints.Add(new ArmJointSpec
                {
                    Name = j.Name, Type = j.Type, Axis = j.Axis, Point = j.Point, MaxSpeed = j.MaxSpeed, MaxTorque = j.MaxTorque,
                    Min = j.Type == ArmJointType.Prismatic ? j.Min : -Math.PI, Max = j.Type == ArmJointType.Prismatic ? j.Max : Math.PI
                });
                free.Links.Add(new ArmLinkSpec { Name = j.Name });
            }

            var rng = new Random(11);
            var seeds = new List<double[]> { map.Select(i => current[i]).ToArray(), new double[free.Dof] };
            for (int s = 0; s < 8; s++) seeds.Add(free.Joints.Select(j => j.Min + (j.Max - j.Min) * rng.NextDouble()).ToArray());
            IkResult? reached = null;
            foreach (var seed in seeds)
            {
                var r = ArmKinematics.SolveDls(free, goal, seed, Transform3.Identity, 300);
                if (r.Success)
                {
                    reached = r;
                    break;
                }
            }

            if (reached != null)
            {
                // the axis farthest past its range
                int worst = -1;
                double over = 0;
                for (int k = 0; k < model.Joints.Count; k++)
                {
                    var j = model.Joints[k];
                    double q = reached.Joints[k];
                    if (j.Type != ArmJointType.Prismatic) q = Math.IEEERemainder(q, 2 * Math.PI);
                    double past = q < j.Min ? j.Min - q : q > j.Max ? q - j.Max : 0;
                    if (past > over)
                    {
                        over = past;
                        worst = k;
                    }
                }

                if (worst >= 0)
                {
                    var axis = Axes[map[worst]];
                    var spec = model.Joints[worst];
                    double need = spec.Type == ArmJointType.Prismatic ? reached.Joints[worst] : Math.IEEERemainder(reached.Joints[worst], 2 * Math.PI);
                    result.Cause = "sınır";
                    result.LimitAxis = map[worst];
                    result.Message = $"Hedef kolun erişim alanında ama bir eksenin sınırı engel: {axis.Name} oraya ancak {axis.Display(need):0.#}{axis.Unit} ile gider, aralığı {axis.Display(spec.Min):0.#}…{axis.Display(spec.Max):0.#}{axis.Unit}." + nearest;
                    return;
                }
            }

            // beyond or inside the span, measured from the first axis as the reach is
            var span = Reach(current, 768);
            var first = model.Joints[0];
            var d = aim - first.Point;
            double radius = (d - first.Axis * d.Dot(first.Axis)).Length;
            if (radius > span.Farthest)
            {
                result.Cause = "uzak";
                result.Message = $"Hedef erişim dışında: ilk eksenden {radius * 1000:0} mm uzakta, kol en çok {span.Farthest * 1000:0} mm uzanır." + nearest;
            }
            else if (radius < span.Nearest)
            {
                result.Cause = "yakın";
                result.Message = $"Hedef ilk eksene çok yakın: {radius * 1000:0} mm; kol {span.Nearest * 1000:0} mm'den içeri katlanamaz." + nearest;
            }
            else result.Message = "Hedef erişim dışında ya da bir eksenin sınırının ötesinde." + nearest;
        }

        /// <summary>The servo axes alone, with the free axes fixed at their values in <paramref name="q"/> (their motion moved into the later axes).</summary>
        private (ArmModel Model, int[] Map) Reduced(IReadOnlyList<double> q)
        {
            var m = new ArmModel { Id = Model.Id, Name = Model.Name, Ik = ArmIkKind.Numeric, PositionOnly = true, ServoDriven = true };
            var map = new List<int>();
            var held = Transform3.Identity;
            for (int i = 0; i < Axes.Count; i++)
            {
                var spec = Axes[i].Spec;
                if (!Axes[i].Actuated)
                {
                    held = held * ArmKinematics.JointTransform(spec, q[i]);
                    continue;
                }

                m.Joints.Add(new ArmJointSpec
                {
                    Name = spec.Name, Type = spec.Type, Axis = held.ApplyVector(spec.Axis), Point = held.Apply(spec.Point),
                    Min = spec.Min, Max = spec.Max, MaxSpeed = spec.MaxSpeed, MaxTorque = spec.MaxTorque
                });
                m.Links.Add(new ArmLinkSpec { Name = spec.Name });
                map.Add(i);
            }

            m.Flange = held * Model.Flange;
            return (m, map.ToArray());
        }

        // ================================================================ reach

        /// <summary>The tip's reach over the servos' travel (free axes as in <paramref name="current"/>), about <paramref name="samples"/> poses.</summary>
        public WorldArmReach Reach(IReadOnlyList<double> current, int samples = 3000)
        {
            var reach = new WorldArmReach();
            var servo = Enumerable.Range(0, Axes.Count).Where(i => Axes[i].Actuated).ToArray();
            var q = current.ToArray();
            if (servo.Length == 0)
            {
                var p = Tip(q);
                reach.Points.Add(p);
                reach.Min = reach.Max = p;
                return reach;
            }

            var normal = Axes[servo[0]].Spec.Axis;
            reach.Planar = Axes.All(a => a.Kind != WorldAxisKind.Slider || !a.Actuated)
                           && servo.All(i => Math.Abs(Math.Abs(Axes[i].Spec.Axis.Dot(normal)) - 1) < 1e-6);
            reach.PlaneNormal = normal;
            int n = servo.Length;
            if (n <= 3)
            {
                int k = Math.Max(2, Math.Min(64, (int)Math.Round(Math.Pow(samples, 1.0 / n))));
                var idx = new int[n];
                while (true)
                {
                    for (int a = 0; a < n; a++)
                    {
                        var s = Axes[servo[a]].Spec;
                        q[servo[a]] = s.Min + (s.Max - s.Min) * idx[a] / (k - 1);
                    }

                    reach.Points.Add(Tip(q));
                    int c = 0;
                    while (c < n && ++idx[c] == k) idx[c++] = 0;
                    if (c == n) break;
                }
            }
            else
            {
                var rng = new Random(11);
                for (int s = 0; s < samples; s++)
                {
                    foreach (int i in servo) q[i] = Axes[i].Spec.Min + (Axes[i].Spec.Max - Axes[i].Spec.Min) * rng.NextDouble();
                    reach.Points.Add(Tip(q));
                }
            }

            var first = Axes[servo[0]].Spec;
            var min = reach.Points[0];
            var max = reach.Points[0];
            double near = double.PositiveInfinity, far = 0;
            foreach (var p in reach.Points)
            {
                min = Vec3.Min(min, p);
                max = Vec3.Max(max, p);
                // Distance from the first servo's axis line.
                var d = p - first.Point;
                double r = (d - first.Axis * d.Dot(first.Axis)).Length;
                near = Math.Min(near, r);
                far = Math.Max(far, r);
            }

            reach.Min = min;
            reach.Max = max;
            reach.Nearest = near;
            reach.Farthest = far;
            return reach;
        }
    }
}
