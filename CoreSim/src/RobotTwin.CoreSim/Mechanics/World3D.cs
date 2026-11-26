using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// Rigid body with principal inertia (and principal axes), integrated with symplectic
    /// (semi-implicit) Euler and an implicit gyroscopic term. The origin is the centre of mass.
    /// </summary>
    public class RigidBody
    {
        public double Mass { get; set; } = 1.0;

        /// <summary>Principal moments of inertia (kg·m²) about the axes given by <see cref="PrincipalAxes"/>.</summary>
        public Vec3 Inertia { get; set; } = new Vec3(0.01, 0.01, 0.01);

        /// <summary>
        /// Rotation from the principal-axis frame to the body frame (identity = body axes are
        /// principal, the classic diagonal-inertia case). Lets a full inertia tensor be used
        /// while the body frame stays the chassis frame (x forward, z up).
        /// </summary>
        public Quat PrincipalAxes { get; set; } = Quat.Identity;

        public Vec3 Position { get; set; }
        public Quat Orientation { get; set; } = Quat.Identity;
        public Vec3 Velocity { get; set; }

        /// <summary>Angular velocity in the world frame.</summary>
        public Vec3 AngularVelocity { get; set; }

        /// <summary>Linear damping rate (1/s) — lumped drag of legacy vehicles; prefer <see cref="DragArea"/>.</summary>
        public double LinearDrag { get; set; } = 0.02;

        /// <summary>Rotational damping torque per rad/s (N·m·s).</summary>
        public double AngularDrag { get; set; } = 2e-6;

        private Vec3 _dragArea;

        /// <summary>Aerodynamic drag area Cd·A (m²) for flow along the body x, y and z axes.</summary>
        /// <remarks>Setting it clears the directional split (<see cref="DirectionalDrag"/>).</remarks>
        public Vec3 DragArea
        {
            get => _dragArea;
            set
            {
                _dragArea = value;
                DirectionalDrag = false;
            }
        }

        /// <summary>
        /// [drag] Cd·A (m²) when the body moves towards +x, +y, +z (that face leads) and towards −x, −y, −z,
        /// with the faces that joined parts cover taken out and skin friction on the exposed sides added
        /// (see <see cref="AssemblyDrag"/>). Used instead of <see cref="DragArea"/> while
        /// <see cref="DirectionalDrag"/> is set.
        /// </summary>
        public Vec3 DragForward { get; private set; }

        public Vec3 DragBackward { get; private set; }

        public bool DirectionalDrag { get; private set; }

        public void SetDirectionalDrag(Vec3 forward, Vec3 backward)
        {
            DragForward = forward;
            DragBackward = backward;
            DirectionalDrag = true;
        }

        /// <summary>
        /// [drag] Round cross-section for oblique flow: −1 none (a box: the projected areas add up),
        /// 0/1/2 round about that body axis (a cylinder: circular in the other two), 3 round every way (a sphere).
        /// </summary>
        public int DragRound { get; set; } = -1;

        /// <summary>
        /// Cd·A (m²) for motion along the body-frame unit direction <paramref name="u"/>. The per-axis values
        /// combine by the body's real projected area: added for flat faces (|u·x|·A_x + …, exact for a box),
        /// as a circle where the cross-section is round (√((u_x·A_x)² + (u_y·A_y)²) for a cylinder about z).
        /// </summary>
        public double DragAreaAlong(Vec3 u)
        {
            double x, y, z;
            if (DirectionalDrag)
            {
                x = u.X >= 0 ? u.X * DragForward.X : -u.X * DragBackward.X;
                y = u.Y >= 0 ? u.Y * DragForward.Y : -u.Y * DragBackward.Y;
                z = u.Z >= 0 ? u.Z * DragForward.Z : -u.Z * DragBackward.Z;
            }
            else
            {
                x = Math.Abs(u.X) * _dragArea.X;
                y = Math.Abs(u.Y) * _dragArea.Y;
                z = Math.Abs(u.Z) * _dragArea.Z;
            }

            return DragRound switch
            {
                3 => Math.Sqrt(x * x + y * y + z * z),
                0 => x + Math.Sqrt(y * y + z * z),
                1 => y + Math.Sqrt(x * x + z * z),
                2 => z + Math.Sqrt(x * x + y * y),
                _ => x + y + z
            };
        }

        /// <summary>
        /// [drag] Σ Cd·A·|v|·v over the body axes for the airspeed <paramref name="v"/> (body frame, m/s); the force is
        /// −½ρ times it. Each axis sees the dynamic pressure of its own velocity component — the cross-flow
        /// (independence) principle of Allen &amp; Perkins — so a slender body at a small angle of attack adds
        /// cross-flow drag as sin²α rather than as sin α, a plate square to the flow keeps its full drag, a round
        /// section treats its two cross axes as one cross-flow, and a sphere sees the whole speed.
        /// </summary>
        public Vec3 DragVector(Vec3 v)
        {
            double A(int k, double c)
            {
                if (DirectionalDrag)
                {
                    var f = c >= 0 ? DragForward : DragBackward;
                    return k == 0 ? f.X : k == 1 ? f.Y : f.Z;
                }

                return k == 0 ? _dragArea.X : k == 1 ? _dragArea.Y : _dragArea.Z;
            }

            double sp = v.Length;
            if (sp < 1e-12) return Vec3.Zero;
            if (DragRound == 3) return v * (DragAreaAlong(v / sp) * sp);
            double[] c = { v.X, v.Y, v.Z };
            double[] r = new double[3];
            int round = DragRound;
            for (int k = 0; k < 3; k++)
                if (k != round || round < 0) r[k] = A(k, c[k]) * Math.Abs(c[k]) * c[k];
            if (round is >= 0 and <= 2)
            {
                // Round about axis `round`: the axial component on its own; the cross-flow in the other two as one.
                int i = (round + 1) % 3, j = (round + 2) % 3;
                double cross = Math.Sqrt(c[i] * c[i] + c[j] * c[j]);
                r[round] = A(round, c[round]) * Math.Abs(c[round]) * c[round];
                if (cross > 1e-12)
                {
                    double side = (A(i, c[i]) * Math.Abs(c[i]) + A(j, c[j]) * Math.Abs(c[j])) / (Math.Abs(c[i]) + Math.Abs(c[j]));
                    r[i] = side * cross * c[i];
                    r[j] = side * cross * c[j];
                }
                else
                {
                    r[i] = 0;
                    r[j] = 0;
                }
            }

            return new Vec3(r[0], r[1], r[2]);
        }

        public bool HasDrag => DirectionalDrag
            ? DragForward.LengthSquared > 0 || DragBackward.LengthSquared > 0
            : _dragArea.X > 0 || _dragArea.Y > 0 || _dragArea.Z > 0;

        /// <summary>Contact points in the body frame (chassis corners, landing legs).</summary>
        public List<Vec3> ContactPoints { get; } = new List<Vec3>();

        /// <summary>Low-friction contact points (ball casters, skids).</summary>
        public List<Vec3> CasterPoints { get; } = new List<Vec3>();

        /// <summary>Static bodies never move (infinite mass).</summary>
        public bool IsStatic { get; set; }

        /// <summary>[rigidbody] Unity's Linear Damping: the share of its speed a body loses each second (1/s): grease, a viscous fluid, a brake.</summary>
        public double LinearDamping { get; set; }

        /// <summary>[rigidbody] Unity's Angular Damping: the share of its spin lost each second (1/s).</summary>
        public double AngularDamping { get; set; }

        /// <summary>[rigidbody] World axes the body may not move along (Unity's Freeze Position): X = 1, Y = 2, Z = 4.</summary>
        public int LockPosition { get; set; }

        /// <summary>[rigidbody] World axes the body may not turn about (Unity's Freeze Rotation): X = 1, Y = 2, Z = 4.</summary>
        public int LockRotation { get; set; }

        /// <summary>A vector with the locked axes' parts taken out.</summary>
        public static Vec3 Unlocked(Vec3 v, int mask) => mask == 0 ? v : new Vec3((mask & 1) != 0 ? 0 : v.X, (mask & 2) != 0 ? 0 : v.Y, (mask & 4) != 0 ? 0 : v.Z);

        /// <summary>Collision geometry; when null the contact/caster points are used.</summary>
        public CollisionShape? Shape { get; set; }

        /// <summary>
        /// Convex pieces of a concave body (body frame, see <see cref="ConvexDecomposition"/>): the solid other
        /// bodies hit is their union, so a tray or a tube can hold things. <see cref="Shape"/> stays their common
        /// hull and is used for bounds, the ground and drag.
        /// </summary>
        public CollisionShape[]? Pieces { get; set; }

        /// <summary>Ray against the body's solid (its pieces when it has them), in the body frame.</summary>
        internal bool RaycastSolid(CollisionShape shape, Vec3 localOrigin, Vec3 localDir, double maxDistance, out double t, out Vec3 normal)
        {
            var pieces = Pieces;
            if (pieces == null) return shape.RaycastLocal(localOrigin, localDir, maxDistance, out t, out normal);
            t = maxDistance;
            normal = default;
            bool hit = false;
            foreach (var p in pieces)
            {
                if (p.RaycastLocal(localOrigin, localDir, t, out double tp, out var np) && tp < t)
                {
                    t = tp;
                    normal = np;
                    hit = true;
                }
            }

            return hit;
        }

        /// <summary>Signed distance to the body's solid (the union of its pieces when it has them), in the body frame.</summary>
        internal bool SignedDistanceSolid(CollisionShape shape, Vec3 local, double maxDistance, out double distance, out Vec3 normal)
        {
            var pieces = Pieces;
            if (pieces == null) return shape.SignedDistance(local, maxDistance, out distance, out normal);
            distance = double.MaxValue;
            normal = default;
            bool any = false;
            foreach (var p in pieces)
            {
                if (p.SignedDistance(local, maxDistance, out double dp, out var np) && dp < distance)
                {
                    distance = dp;
                    normal = np;
                    any = true;
                }
            }

            return any;
        }

        private string _material = Materials.Default;

        /// <summary>Surface material (see <see cref="Materials"/>); stored in canonical form.</summary>
        public string Material
        {
            get => _material;
            set
            {
                _material = Materials.Canonical(value);
                MaterialIndex = Materials.IndexOf(_material);
            }
        }

        internal int MaterialIndex { get; private set; } = Materials.IndexOf(Materials.Default);

        public bool Collides { get; set; } = true;

        /// <summary>Seen by range sensors.</summary>
        public bool SensorVisible { get; set; } = true;

        public object? Tag { get; set; }

        /// <summary>Largest contact force (N) on the body in the last step (impacts spread over the material contact time).</summary>
        public double PeakContactForce { get; internal set; }

        /// <summary>Largest impact force (N) in the last step.</summary>
        public double PeakImpactForce { get; internal set; }

        /// <summary>Largest contact acceleration (m/s²) in the last step.</summary>
        public double PeakAcceleration { get; internal set; }

        /// <summary>Where (world), how fast (approach speed, m/s) and with what effective mass (kg) the strongest impact of the last step happened.</summary>
        public Vec3 PeakImpactPoint { get; internal set; }

        public double PeakImpactSpeed { get; internal set; }
        public double PeakImpactMass { get; internal set; }

        /// <summary>Duration of that impact (s), π·√(m/k) of the material pair.</summary>
        public double PeakImpactDuration { get; internal set; }

        internal Vec3 Force;
        internal Vec3 Torque;
        internal Vec3 ExternalForce, ExternalTorque, StepContactImpulse;

        /// <summary>[weld] Impulses on this part over the step (applied forces, contact torque, other joints), for the loads of the welds it sits in.</summary>
        internal Vec3 StepApplied, StepAppliedAngular, StepContactAngular, StepJointImpulse, StepJointAngular;
        internal int Slot = -1;
        internal Aabb CachedBounds;
        internal Vec3 CachedBoundsPos;
        internal Quat CachedBoundsRot;
        internal CollisionShape? CachedBoundsShape;
        private CollisionShape? _autoShape;
        private double _autoKey = double.NaN;

        public Vec3 ToWorld(Vec3 bodyPoint) => Position + Orientation.Rotate(bodyPoint);

        public Vec3 PointVelocity(Vec3 worldPoint) => Velocity + AngularVelocity.Cross(worldPoint - Position);

        public void AddForceAtPoint(Vec3 force, Vec3 worldPoint)
        {
            Force += force;
            Torque += (worldPoint - Position).Cross(force);
        }

        public void AddForce(Vec3 force) => Force += force;

        public void AddTorque(Vec3 worldTorque) => Torque += worldTorque;

        /// <summary>Angular velocity expressed in the body frame (what a gyro measures).</summary>
        public Vec3 BodyRates => Orientation.InverseRotate(AngularVelocity);

        /// <summary>Shape used for collisions: <see cref="Shape"/>, or the contact and caster points.</summary>
        public CollisionShape? EffectiveShape
        {
            get
            {
                if (Shape != null) return Shape;
                int n = ContactPoints.Count + CasterPoints.Count;
                if (n == 0) return null;
                double key = n * 1e6;
                for (int i = 0; i < ContactPoints.Count; i++) key += (i + 1) * (ContactPoints[i].X * 3.1 + ContactPoints[i].Y * 5.7 + ContactPoints[i].Z * 7.3);
                for (int i = 0; i < CasterPoints.Count; i++) key += (i + 101) * (CasterPoints[i].X * 3.1 + CasterPoints[i].Y * 5.7 + CasterPoints[i].Z * 7.3);
                if (_autoShape == null || key != _autoKey)
                {
                    _autoShape = CollisionShape.Points(ContactPoints, CasterPoints);
                    _autoKey = key;
                }

                return _autoShape;
            }
        }

        /// <summary>World-frame inverse inertia tensor.</summary>
        public Mat3 InverseInertiaWorld()
        {
            if (IsStatic) return Mat3.Zero;
            var inv = Mat3.RotatedDiagonal(Orientation * PrincipalAxes, new Vec3(1 / Inertia.X, 1 / Inertia.Y, 1 / Inertia.Z));
            if (LockRotation == 0) return inv;
            // [rigidbody] a frozen axis has no give: no impulse turns the body about it (an infinite inertia there)
            var keep = Mat3.Diagonal(new Vec3((LockRotation & 1) != 0 ? 0 : 1, (LockRotation & 2) != 0 ? 0 : 1, (LockRotation & 4) != 0 ? 0 : 1));
            return keep * inv * keep;
        }

        /// <summary>Forces → velocities (symplectic Euler, implicit gyroscopic and drag terms); clears the accumulators.</summary>
        internal void IntegrateVelocity(double dt)
        {
            if (IsStatic)
            {
                Force = Vec3.Zero;
                Torque = Vec3.Zero;
                return;
            }

            Velocity += Force / Mass * dt;
            if (LinearDrag > 0) Velocity *= Math.Max(0.0, 1.0 - LinearDrag * dt);

            var frame = Orientation * PrincipalAxes;
            var w = frame.InverseRotate(AngularVelocity);
            var tBody = frame.InverseRotate(Torque);
            var I = Inertia;
            // Implicit gyroscopic term: one Newton step on I(ω − ω₀) + h·ω × Iω = 0 (Catto 2015),
            // unconditionally stable for fast spinning asymmetric bodies.
            var iw = I.Mul(w);
            if (iw.LengthSquared > 0)
            {
                var f = w.Cross(iw) * dt;
                var jac = Mat3.Diagonal(I) + (Mat3.Skew(w) * Mat3.Diagonal(I) - Mat3.Skew(iw)) * dt;
                w -= jac.Inverse() * f;
            }

            w += new Vec3(tBody.X / I.X, tBody.Y / I.Y, tBody.Z / I.Z) * dt;
            if (AngularDrag > 0)
            {
                // Rotational air drag, applied implicitly so it is stable for any inertia.
                w = new Vec3(w.X / (1.0 + AngularDrag / I.X * dt), w.Y / (1.0 + AngularDrag / I.Y * dt), w.Z / (1.0 + AngularDrag / I.Z * dt));
            }

            AngularVelocity = frame.Rotate(w);
            Force = Vec3.Zero;
            Torque = Vec3.Zero;
        }

        /// <summary>Velocities → pose.</summary>
        internal void IntegratePosition(double dt)
        {
            if (IsStatic) return;
            var w = AngularVelocity;
            var dq = new Quat(0, w.X, w.Y, w.Z) * Orientation;
            Orientation = new Quat(
                Orientation.W + 0.5 * dt * dq.W,
                Orientation.X + 0.5 * dt * dq.X,
                Orientation.Y + 0.5 * dt * dq.Y,
                Orientation.Z + 0.5 * dt * dq.Z).Normalized();
            Position += Velocity * dt;
        }

        internal void Integrate(double dt)
        {
            IntegrateVelocity(dt);
            IntegratePosition(dt);
        }
    }

    /// <summary>Static axis-aligned box obstacle resting on the floor.</summary>
    public readonly struct Obstacle
    {
        public Obstacle(double x, double y, double sizeX, double sizeY, double height)
        {
            X = x;
            Y = y;
            SizeX = sizeX;
            SizeY = sizeY;
            Height = height;
        }

        public double X { get; }
        public double Y { get; }
        public double SizeX { get; }
        public double SizeY { get; }
        public double Height { get; }

        public bool Contains(Vec3 p) =>
            p.X > X - SizeX / 2 && p.X < X + SizeX / 2 && p.Y > Y - SizeY / 2 && p.Y < Y + SizeY / 2 && p.Z < Height && p.Z > -0.01;
    }

    public enum ArenaShapeKind
    {
        Box,

        /// <summary>Wedge rising along its local +x from 0 to SizeZ (a ramp).</summary>
        Ramp,

        /// <summary>Upright cylinder (diameter SizeX, height SizeZ).</summary>
        Cylinder,

        /// <summary>Ball (diameter SizeX).</summary>
        Sphere
    }

    /// <summary>A static or movable object of an arena (ramp, post, pushable box, ball …).</summary>
    public sealed class ArenaShape
    {
        public string Name { get; set; } = string.Empty;
        public ArenaShapeKind Kind { get; set; }

        /// <summary>Centre of the footprint (m).</summary>
        public double X { get; set; }

        public double Y { get; set; }

        /// <summary>Height of the bottom above the ground (m).</summary>
        public double Elevation { get; set; }

        /// <summary>Radians about the vertical axis.</summary>
        public double Yaw { get; set; }

        public double SizeX { get; set; } = 0.1;
        public double SizeY { get; set; } = 0.1;
        public double SizeZ { get; set; } = 0.1;

        /// <summary>Dynamic objects can be pushed, knocked over and rolled.</summary>
        public bool Dynamic { get; set; }

        /// <summary>Mass of a dynamic object (kg); 0 = from the material density and volume.</summary>
        public double Mass { get; set; }

        public string Material { get; set; } = "wood";
    }

    /// <summary>Floor with a printed track (black tape on white) and box obstacles, inside walls.</summary>
    public sealed class Arena
    {
        public string Name { get; set; } = "Boş zemin";
        public double Width { get; set; } = 3.0;
        public double Depth { get; set; } = 2.0;
        public double LineWidth { get; set; } = 0.019;
        public List<List<(double X, double Y)>> Lines { get; } = new List<List<(double, double)>>();
        public List<Obstacle> Obstacles { get; } = new List<Obstacle>();

        /// <summary>Static obstacles of arbitrary shape imported from 3D models (world frame).</summary>
        public List<MeshObstacle> MeshObstacles { get; } = new List<MeshObstacle>();

        /// <summary>Ramps, posts and movable objects.</summary>
        public List<ArenaShape> Shapes { get; } = new List<ArenaShape>();

        /// <summary>Single grip coefficient of the floor, used when <see cref="FloorMaterial"/> is not set.</summary>
        public double Friction { get; set; } = 0.9;

        /// <summary>Floor material (wood, concrete, carpet, ice …); null = the legacy single <see cref="Friction"/> value.</summary>
        public string? FloorMaterial { get; set; }

        /// <summary>Patches of the floor made of another material.</summary>
        public List<SurfaceRegion> Regions { get; } = new List<SurfaceRegion>();

        /// <summary>Uneven ground (slopes, hills); null = flat floor at z = 0.</summary>
        public Terrain? Terrain { get; set; }

        public bool Walls { get; set; } = true;
        public double WallHeight { get; set; } = 0.08;
        public string ObstacleMaterial { get; set; } = "wood";

        public double AirDensity { get; set; } = 1.225;
        public Vec3 Wind { get; set; }
        public double Gust { get; set; }

        /// <summary>Default start pose for a robot on this arena (metres, radians).</summary>
        public double StartX { get; set; }

        public double StartY { get; set; }
        public double StartYaw { get; set; }

        public double GroundHeight(double x, double y) => Terrain?.Height(x, y) ?? 0.0;

        /// <summary>Infrared reflectance of the floor at (x, y): ~0.85 white paper, ~0.06 black tape.</summary>
        public double Reflectance(double x, double y)
        {
            if (Math.Abs(x) > Width / 2 || Math.Abs(y) > Depth / 2) return 0.35;
            double half = LineWidth / 2;
            foreach (var line in Lines)
            {
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    if (Pcb.Geometry.PointSegmentDistance(x, y, line[i].X, line[i].Y, line[i + 1].X, line[i + 1].Y) <= half) return 0.06;
                }
            }

            foreach (var r in Regions)
            {
                if (!r.Contains(x, y)) continue;
                switch (r.Material)
                {
                    case "carpet": return 0.25;
                    case "ice": return 0.55;
                    case "asphalt": return 0.12;
                    case "concrete": return 0.35;
                    case "sand": return 0.45;
                    case "grass": return 0.2;
                }
            }

            return 0.85;
        }

        /// <summary>Distance along a horizontal ray to the first obstacle or wall.</summary>
        public double Raycast(Vec3 origin, Vec3 direction, double maxRange)
        {
            var d = new Vec3(direction.X, direction.Y, 0).Normalized();
            if (d.Length < 1e-9) return maxRange;
            double best = maxRange;
            foreach (var o in Obstacles)
            {
                if (origin.Z > o.Height) continue;
                double t = RayBox(origin.X, origin.Y, d.X, d.Y, o.X - o.SizeX / 2, o.Y - o.SizeY / 2, o.X + o.SizeX / 2, o.Y + o.SizeY / 2);
                if (t >= 0 && t < best) best = t;
            }

            foreach (var mo in MeshObstacles) best = mo.Raycast(origin, d, best);

            if (Walls)
            {
                if (d.X > 1e-9) best = Math.Min(best, (Width / 2 - origin.X) / d.X);
                if (d.X < -1e-9) best = Math.Min(best, (-Width / 2 - origin.X) / d.X);
                if (d.Y > 1e-9) best = Math.Min(best, (Depth / 2 - origin.Y) / d.Y);
                if (d.Y < -1e-9) best = Math.Min(best, (-Depth / 2 - origin.Y) / d.Y);
            }

            return Math.Max(0, best);
        }

        private static double RayBox(double ox, double oy, double dx, double dy, double minX, double minY, double maxX, double maxY)
        {
            double tmin = 0, tmax = double.PositiveInfinity;
            if (Math.Abs(dx) < 1e-12) { if (ox < minX || ox > maxX) return -1; }
            else
            {
                double t1 = (minX - ox) / dx, t2 = (maxX - ox) / dx;
                tmin = Math.Max(tmin, Math.Min(t1, t2));
                tmax = Math.Min(tmax, Math.Max(t1, t2));
            }

            if (Math.Abs(dy) < 1e-12) { if (oy < minY || oy > maxY) return -1; }
            else
            {
                double t1 = (minY - oy) / dy, t2 = (maxY - oy) / dy;
                tmin = Math.Max(tmin, Math.Min(t1, t2));
                tmax = Math.Min(tmax, Math.Max(t1, t2));
            }

            return tmax >= tmin ? tmin : -1;
        }

        // ------------------------------------------------------------ presets

        public static IReadOnlyList<string> PresetNames { get; } = new[] { "oval", "eight", "obstacles", "empty", "ramps" };

        public static Arena Create(string preset)
        {
            var a = new Arena();
            switch (preset)
            {
                case "oval":
                {
                    a.Name = "Oval pist";
                    var pts = new List<(double, double)>();
                    double r = 0.45, straight = 0.9;
                    for (int i = 0; i <= 160; i++)
                    {
                        double s = i / 160.0 * (2 * straight + 2 * Math.PI * r);
                        pts.Add(StadiumPoint(s, straight, r));
                    }

                    a.Lines.Add(pts);
                    a.StartY = -r;
                    break;
                }

                case "eight":
                {
                    a.Name = "Sekiz pist";
                    var pts = new List<(double, double)>();
                    for (int i = 0; i <= 240; i++)
                    {
                        double t = i / 240.0 * 2 * Math.PI;
                        pts.Add((1.0 * Math.Sin(t), 0.55 * Math.Sin(2 * t)));
                    }

                    a.Lines.Add(pts);
                    a.StartYaw = Math.Atan2(1.1, 1.0);
                    break;
                }

                case "obstacles":
                    a.Name = "Engelli alan";
                    a.StartX = -1.0;
                    a.StartY = -0.6;
                    a.Obstacles.Add(new Obstacle(0.6, 0.3, 0.2, 0.4, 0.15));
                    a.Obstacles.Add(new Obstacle(-0.5, -0.4, 0.3, 0.2, 0.15));
                    a.Obstacles.Add(new Obstacle(0.1, -0.6, 0.15, 0.15, 0.3));
                    a.Obstacles.Add(new Obstacle(-0.9, 0.5, 0.25, 0.25, 0.2));
                    break;
                case "ramps":
                    // Rampa ve itilebilir nesneler: eğim, farklı zeminler, hareketli kutu ve top.
                    a.Name = "Rampalı alan";
                    a.Width = 4;
                    a.Depth = 3;
                    a.FloorMaterial = "wood";
                    a.StartX = -1.4;
                    a.StartY = 0;
                    a.Shapes.Add(new ArenaShape { Name = "rampa", Kind = ArenaShapeKind.Ramp, X = -0.3, Y = 0, SizeX = 0.6, SizeY = 0.9, SizeZ = 0.08, Material = "wood" });
                    a.Shapes.Add(new ArenaShape { Name = "platform", Kind = ArenaShapeKind.Box, X = 0.25, Y = 0, SizeX = 0.5, SizeY = 0.9, SizeZ = 0.08, Material = "wood" });
                    a.Shapes.Add(new ArenaShape { Name = "kutu", Kind = ArenaShapeKind.Box, X = 1.0, Y = 0.8, SizeX = 0.12, SizeY = 0.12, SizeZ = 0.12, Dynamic = true, Mass = 0.15, Material = "paper" });
                    a.Shapes.Add(new ArenaShape { Name = "top", Kind = ArenaShapeKind.Sphere, X = 1.2, Y = -0.8, SizeX = 0.065, Dynamic = true, Mass = 0.058, Material = "rubber" });
                    a.Regions.Add(new SurfaceRegion(1.3, 0, 0.6, 3.0, "carpet"));
                    a.Regions.Add(new SurfaceRegion(-1.3, -1.0, 0.8, 0.8, "ice"));
                    break;
                default:
                    a.Name = "Boş zemin";
                    a.Width = 6;
                    a.Depth = 6;
                    break;
            }

            return a;
        }

        private static (double, double) StadiumPoint(double s, double straight, double r)
        {
            double half = straight / 2;
            if (s < straight) return (-half + s, -r);
            s -= straight;
            double arc = Math.PI * r;
            if (s < arc)
            {
                double a = -Math.PI / 2 + s / r;
                return (half + r * Math.Cos(a), r * Math.Sin(a));
            }

            s -= arc;
            if (s < straight) return (half - s, r);
            s -= straight;
            double b = Math.PI / 2 + s / r;
            return (-half + r * Math.Cos(b), r * Math.Sin(b));
        }
    }

    public enum WheelDrive
    {
        /// <summary>Spin is imposed from outside every step (a motor co-simulated in the circuit).</summary>
        Kinematic,

        /// <summary>The wheel integrates its own spin from <see cref="Wheel.DriveTorque"/>, brake and ground reaction.</summary>
        Torque
    }

    /// <summary>
    /// Ray-cast wheel on any body: suspension spring–damper along the contact normal against
    /// the ground, ramps and other bodies (a fan of rays in the wheel plane lets it climb
    /// edges), a <see cref="BrushTyre"/> for longitudinal/lateral slip, rolling resistance
    /// from the material pair, and the reaction torque that loads the driving motor.
    /// </summary>
    public sealed class Wheel
    {
        /// <summary>Body the wheel is mounted on (World3D fills in its vehicle body when null).</summary>
        public RigidBody? Body { get; set; }

        /// <summary>Hub position in the body frame.</summary>
        public Vec3 Mount { get; set; }

        /// <summary>Axle direction in the body frame; positive spin rolls toward axle × up (+x for +y).</summary>
        public Vec3 Axle { get; set; } = Vec3.UnitY;

        public double Radius { get; set; } = 0.033;
        public double Width { get; set; } = 0.025;
        public double SuspensionStiffness { get; set; } = 900;
        public double SuspensionDamping { get; set; } = 12;

        /// <summary>Legacy linear tyre gain (no longer used; see <see cref="Tyre"/>).</summary>
        public double LongitudinalStiffness { get; set; } = 40;

        /// <summary>Legacy linear tyre gain (no longer used; see <see cref="Tyre"/>).</summary>
        public double LateralStiffness { get; set; } = 60;

        private string _material = "rubber";
        private int _materialIndex = Materials.IndexOf("rubber");

        /// <summary>Tread material (rubber by default).</summary>
        public string Material
        {
            get => _material;
            set
            {
                _material = Materials.Canonical(value);
                _materialIndex = Materials.IndexOf(_material);
            }
        }

        public BrushTyre Tyre { get; } = new BrushTyre();

        public WheelDrive Drive { get; set; } = WheelDrive.Kinematic;

        /// <summary>Spin inertia (kg·m²) used in <see cref="WheelDrive.Torque"/> mode.</summary>
        public double SpinInertia { get; set; } = 1.4e-5;

        /// <summary>Driving torque on the axle (N·m), Torque mode.</summary>
        public double DriveTorque { get; set; }

        /// <summary>Wheel spin (rad/s), positive = rolling the body forward (+x body).</summary>
        public double AngularVelocity { get; set; }

        /// <summary>Accumulated wheel rotation for drawing.</summary>
        public double Angle { get; private set; }

        public double NormalForce { get; private set; }

        /// <summary>Longitudinal tyre force on the body (N).</summary>
        public double DriveForce { get; private set; }

        public double LateralForce { get; private set; }

        /// <summary>Rolling-resistance torque on the axle (N·m), opposing spin.</summary>
        public double RollingTorque { get; private set; }

        /// <summary>Torque the ground exerts back on the wheel axle (opposes positive spin when positive).</summary>
        public double ReactionTorque => DriveForce * Radius + RollingTorque;

        public bool InContact => NormalForce > 0;
        public Vec3 ContactPoint { get; private set; }
        public Vec3 ContactNormal { get; private set; } = Vec3.UnitZ;
        public string ContactMaterial { get; private set; } = string.Empty;
        public double SlipRatio => Tyre.SlipRatio;
        public double SlipAngle => Tyre.SlipAngle;

        /// <summary>Largest radial (in the wheel plane) contact load in the last step (N).</summary>
        public double PeakLoad { get; internal set; }

        /// <summary>Largest axial (along the axle) contact load in the last step (N) — what pulls a wheel off its shaft.</summary>
        public double PeakAxialLoad { get; internal set; }

        /// <summary>The wheel came off (structural failure); it no longer touches anything.</summary>
        public bool Detached { get; set; }

        internal void Apply(PhysicsScene scene, double h)
        {
            var body = Body!;
            if (Drive == WheelDrive.Kinematic) Angle += AngularVelocity * h;
            var q = body.Orientation;
            var hub = body.ToWorld(Mount);
            var axle = q.Rotate(Axle).Normalized();
            var down0 = q.Rotate(-Vec3.UnitZ);
            var down = (down0 - axle * axle.Dot(down0)).Normalized();
            var forward = axle.Cross(-down);
            bool contact = scene.ProbeWheel(body, hub, down, forward, Radius, out var hit, out var rayDir);
            double muS = 0, muK = 0, crr = 0;
            double n = 0;
            var nrm = Vec3.UnitZ;
            if (contact)
            {
                nrm = hit.Normal;
                if (nrm.Dot(hub - hit.Point) < 0) nrm = -nrm;
                var v = body.PointVelocity(hit.Point);
                if (hit.Body != null && !hit.Body.IsStatic) v -= hit.Body.PointVelocity(hit.Point);
                double pen = (Radius - hit.Distance) * Math.Max(0.2, -rayDir.Dot(nrm));
                n = Math.Max(0, SuspensionStiffness * pen - SuspensionDamping * v.Dot(nrm));
                var props = hit.Body != null ? Materials.Pair(_materialIndex, hit.Body.MaterialIndex) : scene.GroundProperties(_materialIndex, hit.Point.X, hit.Point.Y);
                muS = props.StaticFriction;
                muK = Math.Min(props.KineticFriction, 0.9 * muS);
                crr = props.RollingResistance;
                if (hit.Body == null && scene.Terrain is TerrainSurface soil && soil.WheelSoil(this, hit.Point, ref n, ref muS, ref muK, out double soilCrr)) crr = Math.Max(crr, soilCrr); // [terrain] terramechanics: sinkage, soil shear traction vs slip
                var f = axle.Cross(nrm);
                if (f.Length < 0.2) f = forward - nrm * forward.Dot(nrm);
                f = f.Normalized();
                var l = nrm.Cross(f);
                double vx = v.Dot(f), vy = v.Dot(l);
                _vx = vx;
                var (fx, fy) = Tyre.Update(n, vx, vy, AngularVelocity * Radius, muS, muK, h);
                var force = f * fx + l * fy + nrm * n;
                double axial = Math.Abs(force.Dot(axle));
                double radial = (force - axle * force.Dot(axle)).Length;
                if (axial > PeakAxialLoad) PeakAxialLoad = axial;
                if (radial > PeakLoad) PeakLoad = radial;
                body.AddForceAtPoint(force, hit.Point);
                if (hit.Body != null && !hit.Body.IsStatic) hit.Body.AddForceAtPoint(-force, hit.Point);
                DriveForce = fx;
                LateralForce = fy;
                ContactPoint = hit.Point;
                ContactMaterial = hit.Material;
            }
            else
            {
                Tyre.Update(0, 0, 0, 0, 0, 0, h);
                DriveForce = LateralForce = 0;
            }

            NormalForce = n;
            ContactNormal = nrm;
            RollingTorque = crr * n * Radius * Math.Tanh(AngularVelocity * Radius / 0.005);

            if (Drive == WheelDrive.Torque)
            {
                double before = AngularVelocity;
                double net = DriveTorque - DriveForce * Radius - RollingTorque;
                double w = before + net / Math.Max(SpinInertia, 1e-9) * h;
                if (contact)
                {
                    // The ground reaction alone cannot push the tread past free rolling within a step (prevents chatter).
                    double roll = _vx / Radius;
                    double reaction = (-DriveForce * Radius - RollingTorque) / Math.Max(SpinInertia, 1e-9) * h;
                    if (Math.Abs(DriveTorque) < 1e-12 && (before - roll) * (w - roll) < 0 && Math.Abs(reaction) > 0) w = roll;
                }

                AngularVelocity = w;
                Angle += AngularVelocity * h;
            }
        }

        private double _vx;
    }

    /// <summary>
    /// Propeller on any body: thrust kT·ω² along the mount axis with ground effect, reaction
    /// torque kQ·ω², and rotor drag (blade flapping and induced drag, H-force ∝ T·v⊥/(ωR))
    /// against the in-plane air velocity, including wind. With a geometric <see cref="Pitch"/> the
    /// thrust and torque follow the advance ratio J = V/(n·D) of the axial inflow
    /// (<see cref="RobotTwin.CoreSim.Aero.PropellerPerformance"/>): thrust fades as the aircraft
    /// approaches the pitch speed and turns into drag beyond it (windmilling).
    /// </summary>
    public sealed class Propeller
    {
        /// <summary>Body the propeller is mounted on (World3D fills in its vehicle body when null).</summary>
        public RigidBody? Body { get; set; }

        public Vec3 Mount { get; set; }

        /// <summary>Thrust direction in the body frame.</summary>
        public Vec3 Axis { get; set; } = Vec3.UnitZ;

        public double Diameter { get; set; } = 0.055;

        /// <summary>+1 counter-clockwise seen from above, −1 clockwise.</summary>
        public int SpinDirection { get; set; } = 1;

        public double ThrustCoefficient { get; set; } = 2.95e-8;
        public double TorqueCoefficient { get; set; } = 1.4e-10;

        /// <summary>Geometric pitch (m); 0 = static coefficients only (no forward-flight falloff).</summary>
        public double Pitch { get; set; }

        /// <summary>Advance ratio J = V/(n·D) seen in the last step (0 without a pitch).</summary>
        public double AdvanceRatio { get; private set; }

        /// <summary>Axial inflow speed into the disc in the last step (m/s, + = air arriving from ahead).</summary>
        public double AxialInflow { get; private set; }

        private double _torqueRatio = 1.0;

        /// <summary>Rotor drag factor: H = factor·T·v⊥/(ω·R) (≈1 for small fixed-pitch props).</summary>
        public double RotorDragFactor { get; set; } = 1.0;

        /// <summary>Spin rate (rad/s, ≥ 0 produces lift).</summary>
        public double AngularVelocity { get; set; }

        public double Angle { get; private set; }
        public double Thrust { get; private set; }

        /// <summary>In-plane rotor drag force magnitude (N).</summary>
        public double RotorDrag { get; private set; }

        public bool Detached { get; set; }

        /// <summary>Air density relative to sea level seen in the last step (thrust and torque scale with it).</summary>
        public double DensityRatio { get; private set; } = 1.0;

        /// <summary>Aerodynamic drag torque on the motor shaft.</summary>
        public double DragTorque => TorqueCoefficient * DensityRatio * _torqueRatio * AngularVelocity * Math.Abs(AngularVelocity);

        internal void Apply(PhysicsScene scene, double dt)
        {
            var body = Body!;
            Angle += AngularVelocity * SpinDirection * dt;
            double w = AngularVelocity;
            DensityRatio = Math.Max(0, scene.AirDensity / 1.225);
            var up = body.Orientation.Rotate(Axis).Normalized();
            var at = body.ToWorld(Mount);
            double thrustRatio = 1.0;
            _torqueRatio = 1.0;
            AdvanceRatio = 0;
            AxialInflow = 0;
            if (Pitch > 0)
            {
                // Air arriving at the disc from ahead (the side the thrust points to) at the aircraft's speed.
                AxialInflow = (body.PointVelocity(at) - scene.WindAt(at)).Dot(up);
                double j0 = RobotTwin.CoreSim.Aero.PropellerPerformance.ZeroThrustAdvance(Pitch, Diameter);
                AdvanceRatio = RobotTwin.CoreSim.Aero.PropellerPerformance.AdvanceRatio(AxialInflow, w, Diameter);
                thrustRatio = RobotTwin.CoreSim.Aero.PropellerPerformance.ThrustRatio(AdvanceRatio, j0);
                _torqueRatio = RobotTwin.CoreSim.Aero.PropellerPerformance.PowerRatio(AdvanceRatio, j0);
            }

            Thrust = ThrustCoefficient * DensityRatio * thrustRatio * w * Math.Abs(w);
            // Ground effect: up to 25 % more thrust just above the floor.
            double h = Math.Max(0.0, at.Z - scene.GroundHeight(at.X, at.Y));
            double groundEffect = 1.0 + 0.25 * Math.Exp(-h / (Diameter * 0.6));
            var force = up * (Thrust * groundEffect);
            if (scene.AirDensity > 0 && Math.Abs(w) > 1)
            {
                var air = body.PointVelocity(at) - scene.WindAt(at);
                var inPlane = air - up * air.Dot(up);
                double tip = Math.Abs(w) * Diameter / 2;
                double k = RotorDragFactor * Math.Abs(Thrust) / tip;
                force -= inPlane * k;
                RotorDrag = inPlane.Length * k;
            }
            else RotorDrag = 0;

            body.AddForceAtPoint(force, at);
            body.AddTorque(up * (-SpinDirection * DragTorque));
        }
    }

    /// <summary>
    /// The vehicle's world: an arena (floor, terrain, walls, obstacles, ramps, movable objects)
    /// turned into a <see cref="PhysicsScene"/> with the vehicle as its first body.
    /// </summary>
    public sealed class World3D
    {
        private readonly List<SceneBody> _arenaBodies = new List<SceneBody>();
        private readonly List<SceneBody> _movables = new List<SceneBody>();
        private readonly List<(SceneBody Body, Vec3 Position, Quat Orientation)> _movableHome = new List<(SceneBody, Vec3, Quat)>();
        private (int, int, int, int, bool, double, double) _arenaKey = (-1, -1, -1, -1, false, 0, 0);

        public World3D(Arena arena)
        {
            Arena = arena;
            var body = new SceneBody("gövde") { LinearDrag = 0.02, AngularDrag = 2e-6 };
            Body = body;
            Scene.AddBody(body);
        }

        public Arena Arena { get; }
        public PhysicsScene Scene { get; } = new PhysicsScene();
        public RigidBody Body { get; }

        /// <summary>The vehicle body as a scene body.</summary>
        public SceneBody VehicleBody => (SceneBody)Body;

        public List<Wheel> Wheels => Scene.Wheels;
        public List<Propeller> Propellers => Scene.Propellers;

        public Vec3 Gravity
        {
            get => Scene.Gravity;
            set => Scene.Gravity = value;
        }

        public double Time => Scene.Time;

        public double MaxSubstep
        {
            get => Scene.MaxSubstep;
            set => Scene.MaxSubstep = value;
        }

        /// <summary>Largest contact force felt by the vehicle in the last step (for crash detection).</summary>
        public double PeakImpact => Body.PeakContactForce;

        /// <summary>Movable arena objects (pushable boxes, balls).</summary>
        public IReadOnlyList<SceneBody> Movables
        {
            get
            {
                SyncArena();
                return _movables;
            }
        }

        public void Step(double dt)
        {
            SyncArena();
            foreach (var w in Wheels) w.Body ??= Body;
            foreach (var p in Propellers) p.Body ??= Body;
            foreach (var w in Wheels)
            {
                w.PeakLoad = 0;
                w.PeakAxialLoad = 0;
            }
            Scene.Step(dt);
        }

        /// <summary>Distance to the first thing a horizontal range sensor sees (arena, ramps, movable objects).</summary>
        public double Raycast(Vec3 origin, Vec3 direction, double maxRange, Func<SceneBody, bool>? ignore = null)
        {
            SyncArena();
            double best = Arena.Raycast(origin, direction, maxRange);
            var d = new Vec3(direction.X, direction.Y, 0).Normalized();
            if (d.Length < 1e-9) return best;
            if (Scene.Raycast(origin, d, best, out var hit, b => b != Body && b.SensorVisible && (ignore == null || !ignore(b)), Arena.Terrain != null))
                best = Math.Min(best, hit.Distance);
            return best;
        }

        /// <summary>Puts the movable objects back where the arena places them, at rest.</summary>
        public void ResetMovables()
        {
            SyncArena();
            foreach (var (b, p, q) in _movableHome)
            {
                b.Position = p;
                b.Orientation = q;
                b.Velocity = Vec3.Zero;
                b.AngularVelocity = Vec3.Zero;
            }
        }

        /// <summary>The point is inside an obstacle, wall, ramp, movable object or under the ground.</summary>
        public bool IsInsideSolid(Vec3 p, double margin = 0)
        {
            SyncArena();
            if (Arena.Obstacles.Exists(o => o.Contains(p))) return true;
            foreach (var mo in Arena.MeshObstacles)
                if (mo.Contains(p))
                    return true;
            return Scene.IsInsideSolid(p, margin, Body);
        }

        private void SyncArena()
        {
            var a = Arena;
            Scene.Terrain = a.Terrain;
            Scene.GroundFriction = string.IsNullOrEmpty(a.FloorMaterial) ? a.Friction : (double?)null;
            Scene.GroundMaterial = Materials.Canonical(string.IsNullOrEmpty(a.FloorMaterial) ? "wood" : a.FloorMaterial);
            if (!ReferenceEquals(Scene.Regions, a.Regions))
            {
                Scene.Regions.Clear();
                Scene.Regions.AddRange(a.Regions);
            }

            Scene.AirDensity = a.AirDensity;
            Scene.Wind = a.Wind;
            Scene.GustIntensity = a.Gust;

            var key = (a.Obstacles.Count, a.MeshObstacles.Count, a.Shapes.Count, a.Regions.Count, a.Walls, a.Width, a.Depth);
            if (key.Equals(_arenaKey)) return;
            _arenaKey = key;
            foreach (var b in _arenaBodies) Scene.RemoveBody(b);
            foreach (var b in _movables) Scene.RemoveBody(b);
            _arenaBodies.Clear();
            _movables.Clear();
            _movableHome.Clear();

            string mat = a.ObstacleMaterial;
            if (a.Walls)
            {
                double t = 0.2, hgt = a.WallHeight;
                AddStatic(SceneBody.StaticBox("duvar+x", new Vec3(t, a.Depth + 2 * t, hgt), new Vec3(a.Width / 2 + t / 2, 0, hgt / 2), Quat.Identity, mat), true);
                AddStatic(SceneBody.StaticBox("duvar-x", new Vec3(t, a.Depth + 2 * t, hgt), new Vec3(-a.Width / 2 - t / 2, 0, hgt / 2), Quat.Identity, mat), true);
                AddStatic(SceneBody.StaticBox("duvar+y", new Vec3(a.Width, t, hgt), new Vec3(0, a.Depth / 2 + t / 2, hgt / 2), Quat.Identity, mat), true);
                AddStatic(SceneBody.StaticBox("duvar-y", new Vec3(a.Width, t, hgt), new Vec3(0, -a.Depth / 2 - t / 2, hgt / 2), Quat.Identity, mat), true);
            }

            int k = 0;
            foreach (var o in a.Obstacles)
                AddStatic(SceneBody.StaticBox("engel" + ++k, new Vec3(o.SizeX, o.SizeY, o.Height), new Vec3(o.X, o.Y, o.Height / 2), Quat.Identity, mat), true);

            k = 0;
            foreach (var mo in a.MeshObstacles)
            {
                var c = mo.Bounds.Center;
                var local = new List<Vec3>(mo.Hull.Vertices.Count);
                foreach (var v in mo.Hull.Vertices) local.Add(v - c);
                if (local.Count < 4 || mo.Hull.IsDegenerate) continue;
                var body = new SceneBody(string.IsNullOrEmpty(mo.Name) ? "model" + ++k : mo.Name)
                {
                    IsStatic = true, Shape = CollisionShape.ConvexHull(local, 64), Material = Materials.Canonical("plastic"), Position = c
                };
                body.Shape.MarkFloorFaces(c, Quat.Identity, a.GroundHeight(c.X, c.Y), 2e-3);
                AddStatic(body, true);
            }

            k = 0;
            foreach (var s in a.Shapes)
            {
                var b = CreateShapeBody(s, ++k);
                if (b == null) continue;
                if (s.Dynamic)
                {
                    _movables.Add(b);
                    _movableHome.Add((b, b.Position, b.Orientation));
                    Scene.AddBody(b);
                }
                else
                {
                    AddStatic(b);
                }
            }
        }

        /// <summary>Tag of static bodies made from walls, box obstacles and mesh obstacles (drawn and ray-cast by the arena itself).</summary>
        public const string LegacyArenaTag = "arena-legacy";

        private void AddStatic(SceneBody b, bool legacy = false)
        {
            // Walls and box/mesh obstacles are already seen by Arena.Raycast.
            if (legacy) b.SensorVisible = false;
            b.Tag ??= legacy ? LegacyArenaTag : "arena";
            _arenaBodies.Add(b);
            Scene.AddBody(b);
        }

        private SceneBody? CreateShapeBody(ArenaShape s, int index)
        {
            string id = string.IsNullOrEmpty(s.Name) ? "nesne" + index : s.Name;
            double ground = Arena.GroundHeight(s.X, s.Y) + Math.Max(0, s.Elevation);
            var yaw = Quat.FromYaw(s.Yaw);
            double sx = Math.Max(0.005, s.SizeX), sy = Math.Max(0.005, s.SizeY), sz = Math.Max(0.002, s.SizeZ);
            string mat = Materials.Canonical(s.Material);
            double density = Materials.Get(mat).Density;
            switch (s.Kind)
            {
                case ArenaShapeKind.Ramp:
                {
                    // Wedge: low edge at local x = −L/2, rising to SizeZ at +L/2.
                    var pts = new List<Vec3>();
                    foreach (double yy in new[] { -sy / 2, sy / 2 })
                    {
                        pts.Add(new Vec3(-sx / 2, yy, 0));
                        pts.Add(new Vec3(sx / 2, yy, 0));
                        pts.Add(new Vec3(sx / 2, yy, sz));
                    }

                    var world = new List<Vec3>();
                    foreach (var p in pts) world.Add(new Vec3(s.X, s.Y, ground) + yaw.Rotate(p));
                    if (s.Dynamic)
                    {
                        var mesh = ConvexHull.Compute(pts).ToMesh();
                        double m = s.Mass > 0 ? s.Mass : density * sx * sy * sz / 2;
                        var b = SceneBody.FromMesh(id, mesh, massKg: m, material: mat);
                        b.PlaceModel(new Vec3(s.X, s.Y, ground), yaw);
                        b.DragArea = new Vec3(sy * sz, sx * sz, sx * sy) * 1.05;
                        return b;
                    }

                    return SceneBody.StaticHull(id, world, mat);
                }

                case ArenaShapeKind.Cylinder:
                {
                    double r = sx / 2;
                    var center = new Vec3(s.X, s.Y, ground + sz / 2);
                    if (s.Dynamic)
                    {
                        double m = s.Mass > 0 ? s.Mass : density * Math.PI * r * r * sz;
                        var b = SceneBody.Cylinder(id, r, sz, m, center, mat);
                        b.Orientation = yaw;
                        b.DragArea = new Vec3(2 * r * sz, 2 * r * sz, Math.PI * r * r) * 0.9;
                        return b;
                    }

                    return new SceneBody(id) { IsStatic = true, Shape = CollisionShape.Cylinder(r, sz), Material = mat, Position = center, Orientation = yaw };
                }

                case ArenaShapeKind.Sphere:
                {
                    double r = sx / 2;
                    var center = new Vec3(s.X, s.Y, ground + r);
                    if (s.Dynamic)
                    {
                        double m = s.Mass > 0 ? s.Mass : density * 4.0 / 3 * Math.PI * r * r * r;
                        return SceneBody.Sphere(id, r, m, center, mat);
                    }

                    return new SceneBody(id) { IsStatic = true, Shape = CollisionShape.Sphere(r), Material = mat, Position = center };
                }

                default:
                {
                    var size = new Vec3(sx, sy, sz);
                    var center = new Vec3(s.X, s.Y, ground + sz / 2);
                    if (s.Dynamic)
                    {
                        double m = s.Mass > 0 ? s.Mass : density * sx * sy * sz;
                        var b = SceneBody.Box(id, size, m, center, mat);
                        b.Orientation = yaw;
                        b.DragArea = new Vec3(sy * sz, sx * sz, sx * sy) * 1.05;
                        return b;
                    }

                    return SceneBody.StaticBox(id, size, center, yaw, mat);
                }
            }
        }
    }
}
