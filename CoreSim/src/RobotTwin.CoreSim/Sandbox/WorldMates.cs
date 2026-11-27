using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[mate] What a mate takes hold of on a part.</summary>
    public enum MateFeatureKind
    {
        /// <summary>A flat face: a point on it and its outward normal.</summary>
        Plane,

        /// <summary>A shaft's or a hole's axis: a point on it and its direction.</summary>
        Axis,

        /// <summary>A point.</summary>
        Point
    }

    /// <summary>[mate] A face, axis or point of an object, in the object's own frame (metres).</summary>
    public sealed class MateFeature
    {
        public string Object { get; set; } = "";
        public MateFeatureKind Kind { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double DX { get; set; }
        public double DY { get; set; }
        public double DZ { get; set; } = 1;

        [JsonIgnore]
        public Vec3 Origin => new Vec3(X, Y, Z);

        [JsonIgnore]
        public Vec3 Direction => new Vec3(DX, DY, DZ);

        public MateFeature Clone() => (MateFeature)MemberwiseClone();

        /// <summary>A feature from where it is in the world now (a picked point and its normal or axis).</summary>
        public static MateFeature At(WorldObject o, MateFeatureKind kind, Vec3 worldPoint, Vec3 worldDirection)
        {
            var q = o.Pose.Orientation;
            var local = q.InverseRotate(worldPoint - o.Pose.Position);
            var dir = q.InverseRotate(worldDirection).Normalized();
            return new MateFeature { Object = o.Id, Kind = kind, X = local.X, Y = local.Y, Z = local.Z, DX = dir.X, DY = dir.Y, DZ = dir.Z };
        }
    }

    /// <summary>[mate] The assembly relations, as CAD programs name them.</summary>
    public enum MateKind
    {
        /// <summary>Two faces against each other (normals opposite), <see cref="WorldMate.Value"/> apart.</summary>
        Mate,

        /// <summary>Two faces level with each other (normals the same way), <see cref="WorldMate.Value"/> apart.</summary>
        Flush,

        /// <summary>Two axes on one line: a shaft in its hole.</summary>
        Concentric,

        /// <summary>Two faces or axes parallel.</summary>
        Parallel,

        /// <summary>Two faces or axes at a right angle.</summary>
        Perpendicular,

        /// <summary>A point or face <see cref="WorldMate.Value"/> from a face, along its normal.</summary>
        Distance,

        /// <summary>The angle between two faces' normals or two axes (<see cref="WorldMate.Value"/> degrees).</summary>
        Angle,

        /// <summary>[mate-limit] SolidWorks' limit distance: a point or face between <see cref="WorldMate.Value"/> and <see cref="WorldMate.Value2"/> from a face (m).</summary>
        LimitDistance,

        /// <summary>[mate-limit] SolidWorks' limit angle: two faces or axes between <see cref="WorldMate.Value"/> and <see cref="WorldMate.Value2"/> degrees apart.</summary>
        LimitAngle,

        /// <summary>
        /// [mate-adv] SolidWorks' symmetric: A and B (two faces, two axes or two points) mirror images of each other in the
        /// plane <see cref="WorldMate.C"/>, a face of any part.
        /// </summary>
        Symmetric,

        /// <summary>
        /// [mate-adv] SolidWorks' width (centred): a tab between its faces B and D held in the middle of a groove between the
        /// faces A and C, its middle plane on the groove's (the groove's faces on one part or two, as two walls make one;
        /// <see cref="WorldMate.Value"/> moves the tab off the middle, m).
        /// </summary>
        Width
    }

    public sealed class WorldMate
    {
        public string Id { get; set; } = "";
        public MateKind Kind { get; set; }
        public MateFeature A { get; set; } = new MateFeature();
        public MateFeature B { get; set; } = new MateFeature();

        /// <summary>Offset (metres) for Mate, Flush and Distance; degrees for Angle.</summary>
        public double Value { get; set; }

        /// <summary>[mate-limit] A limit's other end (m or degrees); <see cref="Value"/> is the first.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public double Value2 { get; set; }

        /// <summary>[mate-adv] Symmetric: the symmetry plane. Width: the groove's other face.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MateFeature? C { get; set; }

        /// <summary>[mate-adv] Width: the tab's other face.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MateFeature? D { get; set; }

        /// <summary>Every feature the mate names: A, B and, where it has them, C and D.</summary>
        [JsonIgnore]
        public IEnumerable<MateFeature> Features
        {
            get
            {
                yield return A;
                yield return B;
                if (C != null) yield return C;
                if (D != null) yield return D;
            }
        }

        public WorldMate Clone() => new WorldMate { Id = Id, Kind = Kind, A = A.Clone(), B = B.Clone(), C = C?.Clone(), D = D?.Clone(), Value = Value, Value2 = Value2 };
    }

    public enum MateState
    {
        /// <summary>Parts can still move without breaking a mate (<see cref="MateResult.Freedom"/> > 0).</summary>
        UnderConstrained,

        /// <summary>Every moving part is placed.</summary>
        FullyConstrained,

        /// <summary>Placed, but some mates repeat what others already hold.</summary>
        Redundant,

        /// <summary>The mates contradict each other; nothing was moved.</summary>
        Conflicting
    }

    public sealed class MateResult
    {
        public MateState State { get; internal set; }

        /// <summary>The ways the mated parts can still move (6 for each free part, less what the mates hold).</summary>
        public int Freedom { get; internal set; }

        /// <summary>The largest mate error after solving (metres, or the sine of an angle).</summary>
        public double Residual { get; internal set; }

        public int Iterations { get; internal set; }

        /// <summary>Mates that repeat others (Redundant), or that cannot all hold (Conflicting).</summary>
        public List<string> Culprits { get; } = new List<string>();

        /// <summary>The objects the solve moved.</summary>
        public List<string> Moved { get; } = new List<string>();

        /// <summary>Freedom of each set of parts the mates join, by object.</summary>
        internal Dictionary<string, int> FreedomByObject { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The freedom left to the parts mated with this one (its own set, not the whole world's); null when unmated.</summary>
        public int? FreedomOf(string objectId) => FreedomByObject.TryGetValue(objectId, out int f) ? f : (int?)null;

        public bool Solved => State != MateState.Conflicting;
    }

    /// <summary>
    /// [mate] Assembly mates: parts placed by relations between their faces and axes (against, level, on one axis,
    /// parallel, square, at a distance, at an angle), solved together as CAD assemblies do. A part screwed or grouped to
    /// others moves with them; a fixed part, and in each set of mated parts with none fixed the first mate's first part,
    /// stays put. The solver moves the rest as little as it can, counts the freedom left (a shaft in a hole keeps two:
    /// it slides and turns), names mates that repeat others, and leaves everything where it was when they contradict.
    /// </summary>
    public static class WorldMates
    {
        /// <summary>Whether a relation can join these two kinds of feature.</summary>
        public static bool Fits(MateKind kind, MateFeatureKind a, MateFeatureKind b) => kind switch
        {
            MateKind.Mate or MateKind.Flush => a == MateFeatureKind.Plane && b == MateFeatureKind.Plane,
            MateKind.Concentric => a == MateFeatureKind.Axis && b == MateFeatureKind.Axis,
            MateKind.Parallel or MateKind.Perpendicular or MateKind.Angle or MateKind.LimitAngle => a != MateFeatureKind.Point && b != MateFeatureKind.Point,
            MateKind.Distance or MateKind.LimitDistance => a == MateFeatureKind.Plane,
            MateKind.Symmetric => a == b,   // [mate-adv] its plane comes third
            MateKind.Width => a == MateFeatureKind.Plane && b == MateFeatureKind.Plane,
            _ => false
        };

        /// <summary>Whether a whole mate fits: its first two features, and the plane or faces that symmetric and width take besides.</summary>
        public static bool Fits(WorldMate m) => Fits(m.Kind, m.A.Kind, m.B.Kind) && m.Kind switch
        {
            MateKind.Symmetric => m.C?.Kind == MateFeatureKind.Plane,
            MateKind.Width => m.C?.Kind == MateFeatureKind.Plane && m.D?.Kind == MateFeatureKind.Plane,
            _ => true
        };

        /// <summary>Adds a mate between two features and solves; one that contradicts the others is taken back.</summary>
        /// <param name="c">[mate-adv] Symmetric: the symmetry plane. Width: the groove's other face (on <paramref name="a"/>'s part).</param>
        /// <param name="d">[mate-adv] Width: the tab's other face (on <paramref name="b"/>'s part).</param>
        public static (WorldMate Mate, MateResult Result) Add(WorldDocument w, MateKind kind, MateFeature a, MateFeature b, double value = 0, double value2 = 0, MateFeature? c = null, MateFeature? d = null)
        {
            w.Mates ??= new List<WorldMate>();
            var used = new HashSet<string>(w.Mates.Select(m => m.Id), StringComparer.Ordinal);
            string id = "";
            for (int i = 1; ; i++)
            {
                id = "MT" + i;
                if (!used.Contains(id)) break;
            }

            var mate = new WorldMate { Id = id, Kind = kind, A = a, B = b, C = c, D = d, Value = value, Value2 = value2 };
            w.Mates.Add(mate);
            var r = Solve(w);
            if (r.State == MateState.Conflicting)
            {
                w.Mates.Remove(mate);
                if (w.Mates.Count == 0) w.Mates = null;
            }

            return (mate, r);
        }

        /// <summary>
        /// A part's own axis in its frame, where it has one: a cylinder's, a wheel's, a drive part's (gear, pulley,
        /// bearing, shaft, lead screw, nut, coupler).
        /// </summary>
        public static (Vec3 Origin, Vec3 Direction)? AxisOf(WorldObject o)
        {
            if (DriveParts.Of(o) is { } s) return (s.AxisOrigin, s.AxisLocal.Normalized());
            if (o.Kind != WorldObjectKind.Body) return null;
            return o.Shape switch
            {
                "cylinder" => (new Vec3(0, 0, 0), new Vec3(0, 0, 1)),
                "wheel" => (new Vec3(0, 0, o.SizeX / 2), new Vec3(0, 1, 0)),
                _ => ((Vec3, Vec3)?)null
            };
        }

        /// <summary>Drops the mates whose parts are gone; an empty list becomes null.</summary>
        public static void Prune(WorldDocument w)
        {
            if (w.Mates == null) return;
            w.Mates.RemoveAll(m => m.Features.Any(f => w.Find(f.Object) == null) || m.A.Object == m.B.Object || !Fits(m));
            if (w.Mates.Count == 0) w.Mates = null;
        }

        /// <summary>The mates naming an object.</summary>
        public static IEnumerable<WorldMate> Of(WorldDocument w, string objectId) =>
            w.Mates?.Where(m => m.Features.Any(f => f.Object == objectId)) ?? Enumerable.Empty<WorldMate>();

        // ------------------------------------------------------------------ solving

        private sealed class Group
        {
            public readonly List<WorldObject> Members = new List<WorldObject>();
            public readonly List<(Vec3 Position, Quat Orientation)> Start = new List<(Vec3, Quat)>();
            public Vec3 Pivot;
            public bool Grounded;
            public int Index = -1;   // first of its six unknowns
        }

        /// <param name="explain">Find the mates a contradiction comes from (a solve per mate): off while dragging.</param>
        public static MateResult Solve(WorldDocument w, bool explain = true)
        {
            Prune(w);
            var mates = w.Mates ?? new List<WorldMate>();
            var result = new MateResult();
            if (mates.Count == 0)
            {
                result.State = MateState.FullyConstrained;
                return result;
            }

            var groups = Groups(w, mates);
            var movable = groups.Values.Distinct().Where(g => !g.Grounded).ToList();
            for (int i = 0; i < movable.Count; i++) movable[i].Index = 6 * i;
            int n = 6 * movable.Count;
            var start = new double[n];
            var owners = new List<int>();
            var active = new HashSet<int>();   // [mate-limit] the limits standing at one of their ends: those hold like an equation
            double[] F(double[] x) => Residuals(mates, groups, x, rank: false, null);
            double[] G(double[] x) => Residuals(mates, groups, x, rank: true, null, active);
            var (sol, r, it) = LeastSquares.MinNorm(F, start, 1e-10);
            result.Iterations = it;
            result.Residual = r.Length == 0 ? 0 : LeastSquares.Max(r);
            if (result.Residual > 1e-7)
            {
                result.State = MateState.Conflicting;
                if (explain && mates.Count <= 40)
                {
                    foreach (var m in mates)
                    {
                        var trial = WorldDocument.FromJson(w.ToJson());
                        trial.Mates!.RemoveAll(x => x.Id == m.Id);
                        if (Solve(trial, explain: false).State != MateState.Conflicting) result.Culprits.Add(m.Id);
                    }
                }

                return result;
            }

            // The freedom left and the mates that add nothing, from the rank of the equations at the answer. A limit inside
            // its range holds nothing and gives no row (nor is it a repeat); at an end it holds like an equation there.
            for (int k = 0; k < mates.Count; k++)
            {
                if (Limit(mates[k], groups, sol) is { } l && Math.Min(Math.Abs(l.Value - l.Lo), Math.Abs(l.Value - l.Hi)) <= 1e-7) active.Add(k);
            }

            var rows = Residuals(mates, groups, sol, rank: true, owners, active);
            var j = rows.Length == 0 ? new double[0, n] : LeastSquares.Jacobian(G, sol, rows.Length);
            var dependent = LeastSquares.DependentRows(j, out int rank);
            result.Freedom = n - rank;
            Components(mates, groups, j, owners, result);
            Apply(movable, sol);
            foreach (var g in movable)
                foreach (var o in g.Members)
                    result.Moved.Add(o.Id);
            // A mate repeats the others when none of its rows adds anything (a face against a face on a shaft already
            // square to it only adds the distance: that is the usual way to seat a shaft, not a repeat).
            var dependentSet = new HashSet<int>(dependent);
            var redundant = Enumerable.Range(0, mates.Count)
                .Where(i => owners.Contains(i) && Enumerable.Range(0, owners.Count).Where(row => owners[row] == i).All(dependentSet.Contains))
                .Select(i => mates[i].Id).ToList();
            if (redundant.Count > 0)
            {
                result.State = MateState.Redundant;
                result.Culprits.AddRange(redundant);
            }
            else result.State = result.Freedom == 0 ? MateState.FullyConstrained : MateState.UnderConstrained;

            return result;
        }

        /// <summary>
        /// Each mated object's rigid group (screwed together by fixed joints, or put in one group), which ones stay put,
        /// and where each group turns about.
        /// </summary>
        private static Dictionary<string, Group> Groups(WorldDocument w, List<WorldMate> mates)
        {
            var parent = new Dictionary<string, string>(StringComparer.Ordinal);
            string Root(string id)
            {
                while (parent.TryGetValue(id, out var p) && p != id) id = p;
                return id;
            }

            void Union(string a, string b)
            {
                string ra = Root(a), rb = Root(b);
                if (ra != rb) parent[ra] = rb;
            }

            foreach (var o in w.Objects) parent[o.Id] = o.Id;
            foreach (var j in w.Joints)
                if (j.Kind == WorldJointKind.Fixed && j.ObjectA != null && j.ObjectB != null && parent.ContainsKey(j.ObjectA) && parent.ContainsKey(j.ObjectB))
                    Union(j.ObjectA, j.ObjectB);
            foreach (var o in w.Objects)
                if (WorldBuilding.GroupOf(o) != null)
                    foreach (var mateO in WorldBuilding.GroupMates(w, o))
                        Union(o.Id, mateO.Id);

            var byRoot = new Dictionary<string, Group>(StringComparer.Ordinal);
            var result = new Dictionary<string, Group>(StringComparer.Ordinal);
            var involved = new HashSet<string>(mates.SelectMany(m => m.Features.Select(f => Root(f.Object))), StringComparer.Ordinal);
            foreach (var o in w.Objects)
            {
                string root = Root(o.Id);
                if (!involved.Contains(root)) continue;
                if (!byRoot.TryGetValue(root, out var g)) byRoot[root] = g = new Group();
                g.Members.Add(o);
                g.Start.Add((o.Pose.Position, o.Pose.Orientation));
                if (o.IsStatic) g.Grounded = true;
                result[o.Id] = g;
            }

            foreach (var g in byRoot.Values)
            {
                var sum = new Vec3(0, 0, 0);
                foreach (var (p, _) in g.Start) sum += p;
                g.Pivot = sum / g.Start.Count;
            }

            // In each set of groups the mates join with none fixed, the first mate's first part holds still.
            var seen = new HashSet<Group>();
            foreach (var m in mates)
            {
                var ga = result[m.A.Object];
                if (seen.Contains(ga)) continue;
                var component = new List<Group>();
                var stack = new Stack<Group>();
                stack.Push(ga);
                seen.Add(ga);
                while (stack.Count > 0)
                {
                    var g = stack.Pop();
                    component.Add(g);
                    foreach (var k in mates)
                    {
                        var joined = k.Features.Select(f => result[f.Object]).ToList();   // [mate-adv] three or four parts
                        if (!joined.Contains(g)) continue;
                        foreach (var other in joined)
                            if (seen.Add(other)) stack.Push(other);
                    }
                }

                if (!component.Any(g => g.Grounded)) ga.Grounded = true;
            }

            return result;
        }

        /// <summary>
        /// The joint a part's mates leave it, for the physics: none left, a fixed joint; one turning left, a hinge about that
        /// axis; one sliding left, a slider along it. Null (with why) when more is left, the part does not move, or a screw
        /// motion is left. The joint holds the part to the part its first mate names.
        /// </summary>
        public static (WorldJoint? Joint, string? Why) JointFor(WorldDocument w, string objectId)
        {
            var mates = w.Mates?.Where(m => m.Features.All(f => w.Find(f.Object) != null)).ToList() ?? new List<WorldMate>();
            var mine = mates.FirstOrDefault(m => m.Features.Any(f => f.Object == objectId));
            if (mine == null) return (null, "ilişkisi yok");
            var groups = Groups(w, mates);
            var g = groups[objectId];
            if (g.Grounded) return (null, "bu parça yerinde duruyor; eklemi ona bağlanan parçada kurun");
            var movable = groups.Values.Distinct().Where(x => !x.Grounded).ToList();
            for (int i = 0; i < movable.Count; i++) movable[i].Index = 6 * i;
            int n = 6 * movable.Count;
            var x0 = new double[n];
            var owners = new List<int>();
            var rows = Residuals(mates, groups, x0, rank: true, owners);
            double[] G(double[] x) => Residuals(mates, groups, x, rank: true, null);
            var j = LeastSquares.Jacobian(G, x0, rows.Length);
            // This part's six columns, over the rows of the mates that touch its group.
            var cols = Enumerable.Range(g.Index, 6).ToArray();
            var mineRows = Enumerable.Range(0, owners.Count).Where(r => mates[owners[r]].Features.Any(f => groups[f.Object] == g)).ToList();
            var basis = new List<double[]>();
            foreach (int r in mineRows)
            {
                var v = cols.Select(c => j[r, c]).ToArray();
                foreach (var b in basis)
                {
                    double d = Dot(v, b);
                    for (int k = 0; k < 6; k++) v[k] -= d * b[k];
                }

                double norm = Math.Sqrt(Dot(v, v));
                if (norm > 1e-7) basis.Add(v.Select(e => e / norm).ToArray());
            }

            int freedom = 6 - basis.Count;
            string other = mine.A.Object == objectId ? mine.B.Object : mine.A.Object;
            var o = w.Find(objectId)!;
            var joint = new WorldJoint { Id = w.NextId("J"), ObjectA = objectId, ObjectB = other, AnchorX = o.Pose.X, AnchorY = o.Pose.Y, AnchorZ = o.Pose.Z };
            if (freedom == 0)
            {
                joint.Kind = WorldJointKind.Fixed;
                return (joint, null);
            }

            if (freedom > 1) return (null, $"{freedom} serbestlik kaldı; eklem için bir ya da hiç kalmamalı");
            // The one way it can move: the unit vector the rows leave out.
            double[] best = new double[6];
            double bestNorm = 0;
            for (int i = 0; i < 6; i++)
            {
                var e = new double[6];
                e[i] = 1;
                foreach (var b in basis)
                {
                    double d = Dot(e, b);
                    for (int k = 0; k < 6; k++) e[k] -= d * b[k];
                }

                double norm = Math.Sqrt(Dot(e, e));
                if (norm > bestNorm)
                {
                    bestNorm = norm;
                    best = e.Select(v => v / norm).ToArray();
                }
            }

            var omega = new Vec3(best[0], best[1], best[2]);
            var tau = new Vec3(best[3], best[4], best[5]);
            if (omega.Length < 1e-6 * Math.Max(1, tau.Length))
            {
                var along = tau.Normalized();
                joint.Kind = WorldJointKind.Slider;
                (joint.AxisX, joint.AxisY, joint.AxisZ) = (along.X, along.Y, along.Z);
                return (joint, null);
            }

            var axis = omega.Normalized();
            // A point moves by w x (x - pivot) + t: about an axis through q when t = w x (pivot - q); any pitch is a screw.
            if (Math.Abs(tau.Dot(axis)) > 1e-6 * Math.Max(1e-3, omega.Length)) return (null, "kalan hareket vida gibi; menteşe ya da kızak değil");
            var q = g.Pivot + omega.Cross(tau) / omega.Dot(omega);
            joint.Kind = WorldJointKind.Hinge;
            (joint.AxisX, joint.AxisY, joint.AxisZ) = (axis.X, axis.Y, axis.Z);
            (joint.AnchorX, joint.AnchorY, joint.AnchorZ) = (q.X, q.Y, q.Z);
            return (joint, null);
        }

        private static double Dot(double[] a, double[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
            return s;
        }

        /// <summary>Each set of parts the mates join gets its own freedom: the rank of its rows over its parts' unknowns.</summary>
        private static void Components(List<WorldMate> mates, Dictionary<string, Group> groups, double[,] j, List<int> owners, MateResult result)
        {
            var done = new HashSet<Group>();
            foreach (var m in mates)
            {
                var start = groups[m.A.Object];
                if (done.Contains(start)) continue;
                var set = new HashSet<Group> { start };
                var stack = new Stack<Group>();
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var g = stack.Pop();
                    foreach (var k in mates)
                    {
                        var joined = k.Features.Select(f => groups[f.Object]).ToList();
                        if (!joined.Contains(g)) continue;
                        foreach (var other in joined)
                            if (set.Add(other)) stack.Push(other);
                    }
                }

                done.UnionWith(set);
                var cols = set.Where(g => !g.Grounded && g.Index >= 0).SelectMany(g => Enumerable.Range(g.Index, 6)).ToList();
                var rows = Enumerable.Range(0, owners.Count).Where(r => set.Contains(groups[mates[owners[r]].A.Object])).ToList();
                var sub = new double[rows.Count, cols.Count];
                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < cols.Count; c++) sub[r, c] = j[rows[r], cols[c]];
                LeastSquares.DependentRows(sub, out int rank);
                int freedom = cols.Count - rank;
                foreach (var g in set)
                    foreach (var o in g.Members) result.FreedomByObject[o.Id] = freedom;
            }
        }

        /// <summary>A group's rotation and shift for its six unknowns: a rotation vector and a translation.</summary>
        private static (Quat R, Vec3 T) Motion(Group g, double[] x)
        {
            if (g.Grounded || g.Index < 0) return (Quat.Identity, new Vec3(0, 0, 0));
            var rv = new Vec3(x[g.Index], x[g.Index + 1], x[g.Index + 2]);
            double angle = rv.Length;
            var q = angle > 1e-15 ? Quat.FromAxisAngle(rv / angle, angle) : Quat.Identity;
            return (q, new Vec3(x[g.Index + 3], x[g.Index + 4], x[g.Index + 5]));
        }

        /// <summary>Where a feature is (point and direction, world) for the unknowns.</summary>
        private static (Vec3 P, Vec3 D) Place(MateFeature f, Dictionary<string, Group> groups, double[] x)
        {
            var g = groups[f.Object];
            int i = g.Members.FindIndex(o => o.Id == f.Object);
            var (p0, q0) = g.Start[i];
            var (r, t) = Motion(g, x);
            var position = g.Pivot + r.Rotate(p0 - g.Pivot) + t;
            var orientation = r * q0;
            return (position + orientation.Rotate(f.Origin), orientation.Rotate(f.Direction).Normalized());
        }

        /// <summary>Two unit vectors square to <paramref name="d"/> and to each other.</summary>
        private static (Vec3 U, Vec3 V) Square(Vec3 d)
        {
            var u = (Math.Abs(d.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0)).Cross(d).Normalized();
            return (u, d.Cross(u).Normalized());
        }

        /// <summary>
        /// The equations of the mates, each zero when it holds. Solving, a face against a face is n_A + n_B = 0 (one
        /// answer); for the rank it is the two independent rows of it, so the freedom count stays honest.
        /// </summary>
        /// <summary>[mate-limit] A limit mate's present value and its range (cosines for an angle, so the larger angle is the lower end); null for other mates.</summary>
        private static (double Value, double Lo, double Hi)? Limit(WorldMate m, Dictionary<string, Group> groups, double[] x)
        {
            if (m.Kind is not (MateKind.LimitDistance or MateKind.LimitAngle)) return null;
            var (pa, da) = Place(m.A, groups, x);
            var (pb, db) = Place(m.B, groups, x);
            if (m.Kind == MateKind.LimitDistance) return ((pb - pa).Dot(da), Math.Min(m.Value, m.Value2), Math.Max(m.Value, m.Value2));
            double c1 = Math.Cos(m.Value * Math.PI / 180), c2 = Math.Cos(m.Value2 * Math.PI / 180);
            return (da.Dot(db), Math.Min(c1, c2), Math.Max(c1, c2));
        }

        private static double[] Residuals(List<WorldMate> mates, Dictionary<string, Group> groups, double[] x, bool rank, List<int>? owners, HashSet<int>? active = null)
        {
            var rows = new List<double>();
            owners?.Clear();
            for (int k = 0; k < mates.Count; k++)
            {
                int before = rows.Count;
                var m = mates[k];
                var (pa, da) = Place(m.A, groups, x);
                var (pb, db) = Place(m.B, groups, x);
                var (u, v) = Square(da);
                switch (m.Kind)
                {
                    case MateKind.Mate:
                    case MateKind.Flush:
                    {
                        double sign = m.Kind == MateKind.Mate ? 1 : -1;
                        if (rank)
                        {
                            rows.Add(u.Dot(db));
                            rows.Add(v.Dot(db));
                        }
                        else
                        {
                            var e = db + da * sign;
                            rows.Add(e.X);
                            rows.Add(e.Y);
                            rows.Add(e.Z);
                        }

                        rows.Add((pb - pa).Dot(da) - m.Value);
                        break;
                    }

                    case MateKind.Concentric:
                        rows.Add(u.Dot(db));
                        rows.Add(v.Dot(db));
                        rows.Add((pb - pa).Dot(u));
                        rows.Add((pb - pa).Dot(v));
                        break;
                    case MateKind.Parallel:
                        rows.Add(u.Dot(db));
                        rows.Add(v.Dot(db));
                        break;
                    case MateKind.Perpendicular:
                        rows.Add(da.Dot(db));
                        break;
                    case MateKind.Distance:
                        rows.Add((pb - pa).Dot(da) - m.Value);
                        break;
                    case MateKind.Angle:
                        rows.Add(da.Dot(db) - Math.Cos(m.Value * Math.PI / 180));
                        break;
                    case MateKind.Symmetric:
                    {
                        // [mate-adv] B is A mirrored in the plane C: its point, and its direction for a face or an axis
                        var (pc, dc) = Place(m.C!, groups, x);
                        var nc = dc.Normalized();
                        var ra = pa - nc * (2 * (pa - pc).Dot(nc));
                        var rd = da - nc * (2 * da.Dot(nc));
                        if (m.A.Kind == MateFeatureKind.Point)
                        {
                            var e = pb - ra;
                            rows.Add(e.X);
                            rows.Add(e.Y);
                            rows.Add(e.Z);
                            break;
                        }

                        var (ru, rv) = Square(rd);
                        if (m.A.Kind == MateFeatureKind.Plane)
                        {
                            if (rank)
                            {
                                rows.Add(ru.Dot(db));
                                rows.Add(rv.Dot(db));
                            }
                            else
                            {
                                var e = db - rd;
                                rows.Add(e.X);
                                rows.Add(e.Y);
                                rows.Add(e.Z);
                            }

                            rows.Add((pb - ra).Dot(rd));
                        }
                        else
                        {
                            rows.Add(ru.Dot(db));
                            rows.Add(rv.Dot(db));
                            rows.Add((pb - ra).Dot(ru));
                            rows.Add((pb - ra).Dot(rv));
                        }

                        break;
                    }

                    case MateKind.Width:
                    {
                        // [mate-adv] the tab's middle plane on the groove's: square to the groove's faces, half way between them
                        var (pc, dc) = Place(m.C!, groups, x);
                        var (pd, dd) = Place(m.D!, groups, x);
                        var across = da - dc;
                        var n = across.Length > 1e-9 ? across.Normalized() : da;
                        var tab = db - dd;
                        var t = tab.Length > 1e-9 ? tab.Normalized() : db;
                        var (nu, nv) = Square(n);
                        rows.Add(nu.Dot(t));
                        rows.Add(nv.Dot(t));
                        rows.Add(((pb + pd) * 0.5 - (pa + pc) * 0.5).Dot(n) - m.Value);
                        break;
                    }

                    case MateKind.LimitDistance:
                    case MateKind.LimitAngle:
                    {
                        var (value, lo, hi) = Limit(m, groups, x)!.Value;
                        if (!rank) rows.Add(value < lo ? value - lo : value > hi ? value - hi : 0);   // zero anywhere inside
                        else if (active != null && active.Contains(k)) rows.Add(value - (Math.Abs(value - lo) <= Math.Abs(value - hi) ? lo : hi));
                        break;
                    }
                }

                if (owners != null)
                    for (int r = before; r < rows.Count; r++) owners.Add(k);
            }

            return rows.ToArray();
        }

        private static void Apply(List<Group> movable, double[] x)
        {
            foreach (var g in movable)
            {
                var (r, t) = Motion(g, x);
                for (int i = 0; i < g.Members.Count; i++)
                {
                    var o = g.Members[i];
                    var (p0, q0) = g.Start[i];
                    var p = g.Pivot + r.Rotate(p0 - g.Pivot) + t;
                    var (roll, pitch, yaw) = (r * q0).Normalized().ToEuler();
                    o.Pose.X = Clean(p.X);
                    o.Pose.Y = Clean(p.Y);
                    o.Pose.Z = Clean(p.Z);
                    o.Pose.Roll = Clean(roll * 180 / Math.PI);
                    o.Pose.Pitch = Clean(pitch * 180 / Math.PI);
                    o.Pose.Yaw = Clean(yaw * 180 / Math.PI);
                }
            }
        }

        private static double Clean(double v)
        {
            double r = Math.Round(v, 9);
            return r == 0 ? 0 : r;
        }
    }
}
