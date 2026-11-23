using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    // [phys] Engine extensions: island sleeping, adaptive substeps for fast thin bodies (CCD guard),
    // torsional friction of round contact patches and floor grip that keeps the material pairs.
    public sealed partial class PhysicsScene
    {
        // ================================================================== floor grip

        /// <summary>
        /// When set, ground contacts use the measured material pair (body material on
        /// <see cref="GroundMaterial"/>) scaled so that a rubber tyre gets this static friction
        /// coefficient. Unlike <see cref="GroundFriction"/> a PLA chassis then still slides more
        /// easily than a rubber wheel and a steel caster than both.
        /// </summary>
        public double? GroundGrip { get; set; }

        private static readonly int RubberIndex = Materials.IndexOf("rubber");

        private static ContactProperties GripScaled(int bodyMaterial, int floor, double grip)
        {
            var pair = Materials.Pair(bodyMaterial, floor);
            var tyre = Materials.Pair(RubberIndex, floor);
            double k = tyre.StaticFriction > 1e-6 ? Math.Max(0, grip) / tyre.StaticFriction : 1;
            return new ContactProperties(pair.StaticFriction * k, pair.KineticFriction * k, pair.Restitution, pair.RollingResistance, pair.Stiffness);
        }

        /// <summary>Gauss-Seidel passes of the restitution solve (symmetric bounces of multi-point contacts).</summary>
        public int RestitutionPasses { get; set; } = 6;

        // ================================================================== torsional friction

        /// <summary>
        /// Radius of the contact patch of round bodies as a fraction of their radius (spin about
        /// the contact normal is resisted by μ·N·r_patch·2/3, the uniform-pressure disc result).
        /// Flat faces resolve spin through their several contact points.
        /// </summary>
        public double SpinPatchFraction { get; set; } = 0.06;

        private void SetupSpin(ref Contact c, Mat3 invIA, Mat3 invIB, in ContactProperties props, double rollRadius)
        {
            c.SpinR = 0;
            c.MassS = 0;
            if (rollRadius <= 0 || SpinPatchFraction <= 0) return;
            // A cylinder rolling on its tread already has two points across its width.
            if (_shapes[c.A] is { Kind: ShapeKind.Cylinder }) return;
            double k = c.N.Dot((invIA + invIB) * c.N);
            if (k <= 0) return;
            c.MassS = 1 / k;
            c.SpinR = SpinPatchFraction * rollRadius * (2.0 / 3.0);
        }

        private void SolveSpin(ref Contact c)
        {
            ref var sa = ref _state[c.SA];
            var dw = sa.W;
            if (c.B >= 0) dw -= _state[c.SB].W;
            double lambda = -c.MassS * dw.Dot(c.N);
            double max = c.Mu * c.Pn * c.SpinR;
            double next = Math.Max(-max, Math.Min(max, c.Ps + lambda));
            double d = next - c.Ps;
            c.Ps = next;
            if (d != 0) ApplyAngular(c.SA, c.SB, c.N * d);
        }

        // ================================================================== CCD guard

        /// <summary>Largest fraction of its thinnest half-size a body may travel in one substep.</summary>
        public double MaxTravelFraction { get; set; } = 0.5;

        /// <summary>Upper bound of substeps per <see cref="Step"/> added by the CCD guard.</summary>
        public int MaxCcdSubsteps { get; set; } = 64;

        /// <summary>
        /// Speculative contacts catch most fast bodies; this guard additionally shortens the
        /// substep when a small or thin body would otherwise move more than half its thickness
        /// per substep (bullets, flicked parts, a 3 mm arm swung by a servo).
        /// </summary>
        private int CcdSubsteps(double dt, int n)
        {
            if (MaxTravelFraction <= 0) return n;
            int need = n;
            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                if (b.IsStatic || !b.Collides) continue;
                var s = b.EffectiveShape;
                if (s == null || s.Kind == ShapeKind.Mesh) continue;
                // Linear speed only: a body spinning in place (propeller, wheel, rotor) does not tunnel,
                // and counting its tip speed forced up to MaxCcdSubsteps for the whole scene.
                double speed = b.Velocity.Length;
                if (speed < 1e-6) continue;
                double thin = Thickness(s);
                double allowed = Math.Max(2e-4, MaxTravelFraction * thin);
                int k = (int)Math.Ceiling(speed * dt / allowed);
                if (k > need) need = k;
            }

            return Math.Min(Math.Max(n, need), Math.Max(n, MaxCcdSubsteps));
        }

        private static double Thickness(CollisionShape s)
        {
            switch (s.Kind)
            {
                case ShapeKind.Box:
                    return Math.Min(s.HalfExtents.X, Math.Min(s.HalfExtents.Y, s.HalfExtents.Z));
                case ShapeKind.Sphere:
                    return s.Radius;
                case ShapeKind.Cylinder:
                    return Math.Min(s.Radius, s.HalfHeight);
                default:
                    return Math.Max(1e-3, 0.3 * s.BoundingRadius);
            }
        }

        // ================================================================== sleeping

        private sealed class SleepInfo
        {
            public double Timer;
            public bool Asleep;
            public Vec3 Position;
            public Quat Orientation;
            public Vec3 Force, Torque;
        }

        /// <summary>
        /// Islands of touching/jointed bodies that stay below <see cref="SleepLinearSpeed"/> and
        /// <see cref="SleepAngularSpeed"/> for <see cref="SleepTime"/> are frozen (no gravity, no
        /// solving, no creeping) until something moving reaches them, a force or a drive acts, or
        /// they are moved by hand. Islands with active drives, motors, wheels or propellers never sleep.
        /// </summary>
        public bool SleepEnabled { get; set; }

        public double SleepLinearSpeed { get; set; } = 0.002;
        public double SleepAngularSpeed { get; set; } = 0.03;
        public double SleepTime { get; set; } = 0.4;

        private readonly Dictionary<SceneBody, SleepInfo> _sleep = new Dictionary<SceneBody, SleepInfo>();
        private bool[] _asleep = new bool[16];
        private int[] _island = new int[16];
        private Vec3 _sleepGravity;
        private Vec3[] _extF = new Vec3[16], _extT = new Vec3[16];

        private bool Asleep(int slot) => slot >= 0 && slot < _asleep.Length && _asleep[slot];

        private bool JointAsleep(SceneJoint j)
        {
            int c = j.ChildBody.Slot;
            if (c < 0 || c >= _asleep.Length || c >= Bodies.Count || !ReferenceEquals(Bodies[c], j.ChildBody)) return false;
            bool child = _asleep[c] || j.ChildBody.IsStatic;
            if (!child) return false;
            if (j.ParentBody == null) return _asleep[c];
            int p = j.ParentBody.Slot;
            if (p < 0 || p >= Bodies.Count || !ReferenceEquals(Bodies[p], j.ParentBody)) return false;
            return (_asleep[p] || j.ParentBody.IsStatic) && (_asleep[c] || _asleep[p]);
        }

        /// <summary>True when the body is frozen by the sleep system.</summary>
        public bool IsSleeping(SceneBody body) => _sleep.TryGetValue(body, out var s) && s.Asleep;

        /// <summary>Wakes a body (and with it, at the next step, its whole island).</summary>
        public void Wake(SceneBody body)
        {
            if (_sleep.TryGetValue(body, out var s))
            {
                s.Asleep = false;
                s.Timer = 0;
            }
        }

        private static bool ActiveJoint(SceneJoint j) =>
            j.MotorTorque != 0 || j.Drive != JointDrive.None;

        private void BeginSleep(double dt)
        {
            int count = Bodies.Count;
            if (_asleep.Length < count)
            {
                Array.Resize(ref _asleep, Math.Max(count, _asleep.Length * 2));
                Array.Resize(ref _island, Math.Max(count, _island.Length * 2));
                Array.Resize(ref _extF, _asleep.Length);
                Array.Resize(ref _extT, _asleep.Length);
            }

            for (int i = 0; i < count; i++)
            {
                _extF[i] = Bodies[i].ExternalForce;
                _extT[i] = Bodies[i].ExternalTorque;
            }

            Array.Clear(_asleep, 0, _asleep.Length);
            if (!SleepEnabled)
            {
                if (_sleep.Count > 0) _sleep.Clear();
                return;
            }

            bool gravityChanged = !(Gravity.X == _sleepGravity.X && Gravity.Y == _sleepGravity.Y && Gravity.Z == _sleepGravity.Z);
            _sleepGravity = Gravity;

            // Wake bodies that were disturbed from outside since they fell asleep.
            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                if (b.IsStatic || !_sleep.TryGetValue(b, out var s) || !s.Asleep) continue;
                double w = b.Mass * Math.Max(0.1, Gravity.Length);
                bool moved = (b.Position - s.Position).LengthSquared > 1e-12 || Math.Abs(QDot(b.Orientation, s.Orientation)) < 1 - 1e-10;
                bool pushed = (b.ExternalForce - s.Force).Length > 0.05 * w + 1e-3 ||
                              (b.ExternalTorque - s.Torque).Length > 0.05 * w * Math.Max(0.01, b.EffectiveShape?.BoundingRadius ?? 0.05) + 1e-5;
                bool kicked = b.Velocity.LengthSquared > 1e-12 || b.AngularVelocity.LengthSquared > 1e-12;
                if (moved || pushed || kicked || gravityChanged) WakeIsland(i);
            }

            // Islands that contain anything actively driven stay awake.
            foreach (var j in Joints)
            {
                if (!j.Enabled || j.Broken || !ActiveJoint(j)) continue;
                WakeBody(j.ChildBody);
                if (j.ParentBody != null) WakeBody(j.ParentBody);
            }

            foreach (var wh in Wheels)
                if (wh.Body is SceneBody sb)
                    WakeBody(sb);
            foreach (var pr in Propellers)
                if (pr.Body is SceneBody sb)
                    WakeBody(sb);

            // A moving awake body that reaches a sleeping one wakes its island.
            for (int i = 0; i < count; i++)
            {
                var a = Bodies[i];
                if (a.IsStatic || IsAsleep(a)) continue;
                var sa = a.EffectiveShape;
                if (sa == null || !a.Collides) continue;
                double va = a.Velocity.Length + a.AngularVelocity.Length * sa.BoundingRadius;
                if (va < SleepLinearSpeed) continue;
                double ra = sa.BoundingRadius + va * dt * 2 + 0.003;
                for (int k = 0; k < count; k++)
                {
                    var b = Bodies[k];
                    if (k == i || b.IsStatic || !IsAsleep(b)) continue;
                    var sb = b.EffectiveShape;
                    if (sb == null) continue;
                    double r = ra + sb.BoundingRadius;
                    if ((a.Position - b.Position).LengthSquared <= r * r) WakeIsland(k);
                }
            }

            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                _asleep[i] = !b.IsStatic && IsAsleep(b);
                if (_asleep[i])
                {
                    b.Velocity = Vec3.Zero;
                    b.AngularVelocity = Vec3.Zero;
                }
            }
        }

        private static double QDot(Quat a, Quat b) => a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private bool IsAsleep(SceneBody b) => _sleep.TryGetValue(b, out var s) && s.Asleep;

        private void WakeBody(SceneBody b)
        {
            if (b.Slot >= 0 && b.Slot < Bodies.Count && ReferenceEquals(Bodies[b.Slot], b)) WakeIsland(b.Slot);
            else Wake(b);
        }

        private void WakeIsland(int slot)
        {
            int id = slot < _island.Length ? _island[slot] : -1;
            for (int i = 0; i < Bodies.Count; i++)
            {
                if (i != slot && (id < 0 || i >= _island.Length || _island[i] != id)) continue;
                if (_sleep.TryGetValue(Bodies[i], out var s))
                {
                    s.Asleep = false;
                    s.Timer = 0;
                }
            }
        }

        private int[] _uf = new int[16];

        private int Find(int x)
        {
            while (_uf[x] != x) x = _uf[x] = _uf[_uf[x]];
            return x;
        }

        private void EndSleep(double dt)
        {
            if (!SleepEnabled) return;
            int count = Bodies.Count;
            if (_uf.Length < count) Array.Resize(ref _uf, Math.Max(count, _uf.Length * 2));
            for (int i = 0; i < count; i++) _uf[i] = i;

            // Islands: last substep's contacts between dynamic bodies and every live joint.
            for (int i = 0; i < _contactCount; i++)
            {
                ref var c = ref _contacts[i];
                if (c.B < 0 || Bodies[c.B].IsStatic || Bodies[c.A].IsStatic) continue;
                _uf[Find(c.A)] = Find(c.B);
            }

            foreach (var j in Joints)
            {
                if (!j.Enabled || j.Broken || j.ParentBody == null) continue;
                int a = j.ChildBody.Slot, b = j.ParentBody.Slot;
                if (a < 0 || b < 0 || a >= count || b >= count || Bodies[a].IsStatic || Bodies[b].IsStatic) continue;
                _uf[Find(a)] = Find(b);
            }

            // Bodies that must stay awake (driven, wheels, propellers) taint their islands.
            var awakeRoots = new HashSet<int>();
            foreach (var j in Joints)
            {
                if (!j.Enabled || j.Broken || !ActiveJoint(j)) continue;
                int c = j.ChildBody.Slot;
                if (c >= 0 && c < count) awakeRoots.Add(Find(c));
                if (j.ParentBody is { } pb && pb.Slot >= 0 && pb.Slot < count) awakeRoots.Add(Find(pb.Slot));
            }

            foreach (var wh in Wheels)
                if (wh.Body is SceneBody sb && sb.Slot >= 0 && sb.Slot < count)
                    awakeRoots.Add(Find(sb.Slot));
            foreach (var pr in Propellers)
                if (pr.Body is SceneBody sb && sb.Slot >= 0 && sb.Slot < count)
                    awakeRoots.Add(Find(sb.Slot));

            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                if (b.IsStatic) continue;
                if (!_sleep.TryGetValue(b, out var s)) _sleep[b] = s = new SleepInfo();
                if (s.Asleep) continue;
                var shape = b.EffectiveShape;
                double r = shape?.BoundingRadius ?? 0.05;
                bool slow = b.Velocity.Length < SleepLinearSpeed && b.AngularVelocity.Length < SleepAngularSpeed &&
                            b.AngularVelocity.Length * r < SleepLinearSpeed;
                s.Timer = slow ? s.Timer + dt : 0;
            }

            // Island = min timer of its members.
            var islandReady = new Dictionary<int, bool>();
            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                if (b.IsStatic) continue;
                int root = Find(i);
                _island[i] = root;
                bool ready = !awakeRoots.Contains(root) && _sleep[b].Timer >= SleepTime || _sleep[b].Asleep;
                islandReady[root] = islandReady.TryGetValue(root, out var prev) ? prev && ready : ready;
            }

            for (int i = 0; i < count; i++)
            {
                var b = Bodies[i];
                if (b.IsStatic) continue;
                var s = _sleep[b];
                if (s.Asleep || !islandReady[_island[i]]) continue;
                s.Asleep = true;
                s.Position = b.Position;
                s.Orientation = b.Orientation;
                s.Force = _extF[i];
                s.Torque = _extT[i];
                b.Velocity = Vec3.Zero;
                b.AngularVelocity = Vec3.Zero;
            }

            if (_sleep.Count > 2 * count + 16)
            {
                var live = new HashSet<SceneBody>(Bodies);
                foreach (var k in new List<SceneBody>(_sleep.Keys))
                    if (!live.Contains(k))
                        _sleep.Remove(k);
            }
        }
    }
}
