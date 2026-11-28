using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>One end of a <see cref="CableRope"/>: plugged into a body (or fixed in the world) at a pin.</summary>
    public sealed class RopeEnd
    {
        /// <summary>Body the plug sits in; null = fixed in the world at <see cref="Local"/>.</summary>
        public SceneBody? Body { get; set; }

        /// <summary>Pin position in the body frame (world when <see cref="Body"/> is null).</summary>
        public Vec3 Local { get; set; }

        /// <summary>Direction the plug housing leaves the pin, body frame (headers: up).</summary>
        public Vec3 Exit { get; set; } = Vec3.UnitZ;

        /// <summary>False once the plug has been pulled out of its pin.</summary>
        public bool Attached { get; internal set; } = true;

        /// <summary>Axial cable force at this plug, low-pass filtered over a few ms (N).</summary>
        public double Tension { get; internal set; }

        public Vec3 World => Body != null ? Body.ToWorld(Local) : Local;

        public Vec3 ExitWorld => Body != null ? Body.Orientation.Rotate(Exit) : Exit;
    }

    /// <summary>
    /// A cable as a physical rope (extended position based dynamics, XPBD): a chain of particles
    /// with the cable's real length and mass per metre, axial and bending stiffness from its
    /// gauge, gravity and air damping, collision with every body shape of a
    /// <see cref="PhysicsScene"/> and the ground (with Coulomb friction against the surface it
    /// lies on), both plugs pinned to their pins and following the bodies. A taut cable pulls on
    /// its connectors; above <see cref="PullOutForce"/> a plug comes out of its pin.
    /// </summary>
    public sealed class CableRope
    {
        private Vec3[] _x, _p, _v;
        private double[] _w, _lambda;
        private Vec3[] _bendLambda;
        private readonly List<SceneBody> _near = new List<SceneBody>();
        private readonly int _pinned = 2;   // the plug housing (pin + one segment) is rigid
        private double _bendScale = 1;

        public CableRope(string id, RopeEnd a, RopeEnd b, double length, double awg)
        {
            Id = id;
            A = a;
            B = b;
            Awg = awg;
            double span = (a.World - b.World).Length;
            Length = Math.Max(Math.Max(0.02, length), span * 1.001);
            int n = (int)Math.Ceiling(Length / 0.010);
            Segments = Math.Max(6, Math.Min(64, n));
            SegmentLength = Length / Segments;

            double area = CrossSection(awg);
            double rCu = Math.Sqrt(area / Math.PI);
            Radius = RadiusOf(awg);
            // Copper (8960 kg/m³) plus soft-PVC insulation (1400 kg/m³).
            LinearDensity = area * 8960 + Math.PI * (Radius * Radius - rCu * rCu) * 1400;
            // Elastic bending comes from the soft-PVC jacket (E ≈ 10 MPa for flexible jumper insulation);
            // annealed copper strands yield beyond a tiny curvature (κ ≈ 6 m⁻¹ for a 0.17 mm strand), so
            // they add only a little springback: 2 % of seven free strands (strand = d/3). Plastic set
            // (a wire keeping a bend) is not modelled.
            double rs = rCu / 3;
            BendStiffness = 0.02 * 7 * 117e9 * Math.PI * Math.Pow(rs, 4) / 4 + 10e6 * Math.PI * Math.Pow(Radius, 4) / 4;
            // Effective axial stiffness of a laid, slightly curled stranded wire (≈ 0.2 % of solid copper EA).
            AxialStiffness = Math.Max(15, 2.4e8 * area);
            // Dupont / header contacts let go at a few newtons; heavier leads are screwed or crimped.
            PullOutForce = awg >= 21 ? 3.5 : awg >= 19 ? 12 : 40;

            int count = Segments + 1;
            _x = new Vec3[count];
            _p = new Vec3[count];
            _v = new Vec3[count];
            _w = new double[count];
            _lambda = new double[Segments];
            _bendLambda = new Vec3[count];
            double m = LinearDensity * SegmentLength;
            for (int i = 0; i < count; i++) _w[i] = 1.0 / m;
            Lay(null);
        }

        public string Id { get; }
        public RopeEnd A { get; }
        public RopeEnd B { get; }
        public double Awg { get; }

        /// <summary>Rest length (m).</summary>
        public double Length { get; }

        public int Segments { get; }
        public double SegmentLength { get; }

        /// <summary>Outer (jacket) radius (m).</summary>
        public double Radius { get; }

        /// <summary>Mass per metre (kg/m).</summary>
        public double LinearDensity { get; }

        /// <summary>Bending stiffness E·I (N·m²).</summary>
        public double BendStiffness { get; }

        /// <summary>Effective axial stiffness E·A (N).</summary>
        public double AxialStiffness { get; }

        /// <summary>Plug retention force (N): beyond it a plug comes out of its pin.</summary>
        public double PullOutForce { get; set; }

        /// <summary>Velocity damping (1/s): air and internal friction of the strands.</summary>
        public double Damping { get; set; } = 3.0;

        /// <summary>Jacket friction against parts and floor.</summary>
        public double Friction { get; set; } = 0.5;

        /// <summary>Substeps per <see cref="Step"/> call.</summary>
        public int Substeps { get; set; } = 1;

        public int Iterations { get; set; } = 4;

        /// <summary>Distance along the cable from each plug within which it may touch the part it is plugged into (m).</summary>
        public double PlugClearance { get; set; } = 0.025;

        /// <summary>Particle positions from end A to end B (world).</summary>
        public IReadOnlyList<Vec3> Points => _x;

        /// <summary>Largest penetration of a particle into a body or the floor after the last step (m).</summary>
        public double MaxPenetration => _scene == null ? 0 : Penetration(_scene);

        /// <summary>True while the cable lies still and nothing near it moves (it is then not stepped).</summary>
        public bool Resting => _asleep;

        /// <summary>[phys] Carried with the rigid body it lies in (see <see cref="Carried"/>).</summary>
        public bool Carrying => _carry;

        /// <summary>Bodies close enough to touch the wire at its last step.</summary>
        public IReadOnlyList<SceneBody> NearBodies => _near;

        private PhysicsScene? _scene;
        private bool _asleep;
        private double _still;
        private Vec3 _lastA, _lastB;

        /// <summary>Raised when a plug is pulled out (end, force in N).</summary>
        public event Action<RopeEnd, double>? Unplugged;

        /// <summary>
        /// [perf] While on (the cables stepped side by side on the cores), what the rope does to the scene waits, in order,
        /// for <see cref="Flush"/>: the forces on bodies and the plugs it pulls out. Its own motion is the same either way,
        /// and flushed rope by rope in the cables' order, every body gets the same forces added in the same order.
        /// </summary>
        internal bool Deferred { get; set; }

        private readonly List<(SceneBody Body, Vec3 Force, Vec3 At)> _forces = new List<(SceneBody Body, Vec3 Force, Vec3 At)>();
        private readonly List<(RopeEnd End, double Force)> _releases = new List<(RopeEnd End, double Force)>();

        /// <summary>[perf] Hands the scene what waited while <see cref="Deferred"/>: the forces, then the plugs that came out.</summary>
        internal void Flush()
        {
            foreach (var (body, force, at) in _forces) body.AddForceAtPoint(force, at);
            _forces.Clear();
            foreach (var (end, force) in _releases) Unplugged?.Invoke(end, force);
            _releases.Clear();
        }

        private void Push(SceneBody body, Vec3 force, Vec3 at)
        {
            if (Deferred) _forces.Add((body, force, at));
            else body.AddForceAtPoint(force, at);
        }

        public static double CrossSection(double awg)
        {
            double d = 0.127e-3 * Math.Pow(92.0, (36.0 - awg) / 39.0);
            return Math.PI * d * d / 4.0;
        }

        /// <summary>Default cable length when none is given: the distance between the pins plus slack (m).</summary>
        /// <summary>Outer radius of an insulated wire of the gauge (m): the copper plus 0.3 mm of PVC.</summary>
        public static double RadiusOf(double awg) => Math.Sqrt(CrossSection(awg) / Math.PI) + 0.0003;

        /// <summary>
        /// A wire cut to its run (the way between the pins, see <see cref="CableRouter"/>) with 10 % and 15 mm to spare:
        /// a few centimetres of extra lead bunched up in a crowded fuselage would be pressed into the parts around it.
        /// </summary>
        public static double DefaultLength(double span) => Math.Max(0.04, 1.1 * span + 0.015);

        /// <summary>A cable cut to its way and tied along it: 2 % and 4 mm of slack.</summary>
        public static double TiedLength(double run) => Math.Max(0.02, 1.02 * run + 0.004);

        // ================================================================ layout

        /// <summary>
        /// Lays the cable out as an arch above its pins (so it can fall and drape over whatever
        /// lies below) with the given length; <paramref name="previous"/> reuses an earlier shape.
        /// </summary>
        public void Lay(IReadOnlyList<Vec3>? previous)
        {
            int count = Segments + 1;
            if (previous != null && previous.Count >= 2)
            {
                for (int i = 0; i < count; i++)
                {
                    double t = i / (double)Segments * (previous.Count - 1);
                    int k = Math.Min(previous.Count - 2, (int)Math.Floor(t));
                    double f = t - k;
                    _x[i] = previous[k] * (1 - f) + previous[k + 1] * f;
                    _v[i] = Vec3.Zero;
                }

                Pin();
                return;
            }

            LayBow(Vec3.UnitZ);
        }

        /// <summary>
        /// [route] Lays the wire along a path found through free space (world points from just past plug A to just past
        /// plug B, see <see cref="CableRouter"/>): its particles spread evenly along it, the slack left to settle.
        /// </summary>
        public void LayAlong(IReadOnlyList<Vec3> path)
        {
            Pin();
            int count = Segments + 1;
            var pts = new List<Vec3>(path.Count + 2) { _x[1] };
            pts.AddRange(path);
            pts.Add(_x[count - 2]);
            var cum = new double[pts.Count];
            for (int k = 1; k < pts.Count; k++) cum[k] = cum[k - 1] + (pts[k] - pts[k - 1]).Length;
            double total = cum[pts.Count - 1];
            int seg = 0;
            for (int i = 2; i <= count - 3; i++)
            {
                double target = total * (i - 1) / (count - 2);
                while (seg < pts.Count - 2 && cum[seg + 1] < target) seg++;
                double l = cum[seg + 1] - cum[seg];
                double f = l > 1e-12 ? (target - cum[seg]) / l : 0;
                _x[i] = pts[seg] + (pts[seg + 1] - pts[seg]) * f;
            }

            for (int i = 0; i < count; i++) _v[i] = Vec3.Zero;
        }

        /// <summary>
        /// [route] Solids the wire passes through as laid: a segment that enters a body and leaves it again, or a point
        /// deeper in one than the wire's radius, with how much wire is inside (m). The plug's own part near its pin
        /// (the plug housing) and propeller discs do not count.
        /// </summary>
        public List<(SceneBody Body, double Inside)> Crossings(PhysicsScene scene)
        {
            Gather(scene, 0);
            var found = new Dictionary<SceneBody, double>();
            for (int k = 0; k < Segments; k++)
            {
                var p = _x[k];
                var q = _x[k + 1];
                double len = (q - p).Length;
                if (len < 1e-9) continue;
                foreach (var b in _near)
                {
                    if (Ignored(b, k) || Ignored(b, k + 1)) continue;
                    var shape = b.EffectiveShape!;
                    var lp = b.Orientation.InverseRotate(p - b.Position);
                    var lq = b.Orientation.InverseRotate(q - b.Position);
                    var dir = (lq - lp) / len;
                    if (!b.RaycastSolid(shape, lp, dir, len, out double t1, out _)) continue;
                    if (!b.RaycastSolid(shape, lq, -dir, len, out double t2, out _)) continue;
                    double inside = len - t1 - t2;
                    if (inside <= Radius) continue;
                    found[b] = (found.TryGetValue(b, out double sum) ? sum : 0) + inside;
                }
            }

            return found.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        /// <summary>The slack as a parabolic bow towards <paramref name="bow"/> (perpendicular to the pins' line).</summary>
        private void LayBow(Vec3 bow)
        {
            int count = Segments + 1;
            var a = A.World + A.ExitWorld * SegmentLength;
            var b = B.World + B.ExitWorld * SegmentLength;
            double span = (b - a).Length;
            double extra = Math.Max(0, Length - 2 * SegmentLength - span);
            // Parabolic arch whose arc length takes up the slack: s ≈ L + 8/3·d²/L.
            double rise = Math.Sqrt(Math.Max(0, 3.0 / 8.0 * Math.Max(span, 0.01) * extra));
            for (int i = 0; i < count; i++)
            {
                double t = i / (double)Segments;
                var p = a + (b - a) * t;
                _x[i] = p + bow * (4 * rise * t * (1 - t));
                _v[i] = Vec3.Zero;
            }

            Pin();
        }

        /// <summary>
        /// Lays the cable the way a person would route it: its slack bowed up, down or to either side, whichever runs
        /// through the fewest solids (a wire between two parts inside a closed fuselage stays inside it instead of
        /// arching through the lid and being pushed out over the wing).
        /// </summary>
        public void LayClear(PhysicsScene scene)
        {
            var along = B.World - A.World;
            var side = along.Cross(Vec3.UnitZ);
            side = side.Length > 1e-9 ? side.Normalized() : Vec3.UnitX;
            Vec3 best = Vec3.UnitZ;
            double bestPen = double.MaxValue;
            foreach (var bow in new[] { Vec3.UnitZ, side, -side, -Vec3.UnitZ })
            {
                LayBow(bow);
                Gather(scene, 0.01);
                double pen = Penetration(scene) + Depth(scene) + 10 * KeepOutDepth();   // through a prop disc counts as much as through a solid
                if (pen < bestPen - 1e-6)
                {
                    bestPen = pen;
                    best = bow;
                }
            }

            LayBow(best);
        }

        /// <summary>Sum of how deep the free particles sit inside bodies and the floor (m), for choosing a lay.</summary>
        private double Depth(PhysicsScene scene)
        {
            double sum = 0;
            for (int i = 0; i < _x.Length; i++)
            {
                if (IsPinned(i)) continue;
                var x = _x[i];
                if (scene.GroundEnabled) sum += Math.Max(0, scene.GroundHeight(x.X, x.Y) + Radius - x.Z);
                foreach (var b in _near)
                {
                    if (Ignored(b, i)) continue;
                    var shape = b.EffectiveShape!;
                    var local = b.Orientation.InverseRotate(x - b.Position);
                    if (local.LengthSquared > (shape.BoundingRadius + Radius) * (shape.BoundingRadius + Radius)) continue;
                    if (b.SignedDistanceSolid(shape, local, Radius, out double d, out _) && d < Radius) sum += Radius - d;
                }
            }

            return sum;
        }

        private void Pin()
        {
            int last = Segments;
            if (A.Attached)
            {
                _x[0] = A.World;
                if (_pinned > 1) _x[1] = A.World + A.ExitWorld.Normalized() * SegmentLength;
            }

            if (B.Attached)
            {
                _x[last] = B.World;
                if (_pinned > 1) _x[last - 1] = B.World + B.ExitWorld.Normalized() * SegmentLength;
            }
        }

        private bool IsPinned(int i) =>
            (A.Attached && i < _pinned) || (B.Attached && i > Segments - _pinned);

        // ================================================================ stepping

        /// <summary>
        /// Advances the rope by <paramref name="dt"/> against the scene's current body poses.
        /// With <paramref name="couple"/> the plug tension is applied to the bodies (call before
        /// <see cref="PhysicsScene.Step"/>) and plugs pull out above <see cref="PullOutForce"/>.
        /// </summary>
        public void Step(PhysicsScene scene, double dt, bool couple)
        {
            if (dt <= 0) return;
            _scene = scene;
            _coupleDt = couple ? dt : 0;
            if (couple && Sleeping(scene, dt))
            {
                // A cable at rest keeps pulling with the force it had (its weight on the plugs).
                Couple(A, 0, _pinned);
                Couple(B, Segments, Segments - _pinned);
                return;
            }

            if (couple && Carried(scene, dt)) return;   // [phys] at rest inside a moving rigid body

            Gather(scene, dt);
            int subs = Math.Max(1, Substeps);
            double h = dt / subs;
            var g = scene.Gravity;
            double keep = Math.Exp(-Damping * h);
            int count = Segments + 1;
            double tensionA = 0, tensionB = 0;
            // [phys] Damping is the jacket's own (material) loss: it acts on the wire's motion relative to its plugged ends,
            // not relative to the world, which would act as a drag of 3/s × speed (≈ 4 g on a wire inside a plane at
            // 12 m/s). The air's drag is separate and real: a cylinder across the flow (Cd ≈ 1.2) against the wind,
            // or against the air carried inside a closed fuselage when both plugs sit in it.
            var frame = InternalTo != null && A.Attached ? A.Body : null;
            var vA = A.Attached && A.Body != null ? A.Body.PointVelocity(A.World) : Vec3.Zero;
            var vB = B.Attached && B.Body != null ? B.Body.PointVelocity(B.World) : Vec3.Zero;
            double kAir = LinearDensity > 0 ? 0.5 * scene.AirDensity * 1.2 * (2 * Radius) / LinearDensity : 0;
            for (int s = 0; s < subs; s++)
            {
                for (int i = 0; i < count; i++)
                {
                    _p[i] = _x[i];
                    if (IsPinned(i)) continue;
                    double t = i / (double)Segments;
                    var u = vA * (1 - t) + vB * t;
                    var v = _v[i] + g * h;
                    v = u + (v - u) * keep;
                    var air = frame != null ? frame.PointVelocity(_x[i]) : scene.Wind;
                    var rel = v - air;
                    double rl = rel.Length;
                    if (rl > 1e-9 && kAir > 0) v = air + rel / (1 + kAir * rl * h);
                    _v[i] = v;
                    _x[i] += _v[i] * h;
                }

                Pin();
                Array.Clear(_lambda, 0, _lambda.Length);
                Array.Clear(_bendLambda, 0, _bendLambda.Length);
                for (int it = 0; it < Iterations; it++)
                {
                    SolveStretch(h);
                    SolveBend(h);
                }

                Collide(scene, h);
                // Collisions shift particles; one more length pass keeps the chain inextensible.
                SolveStretch(h);
                // That pass pulls a taut wire's chords back across the corners it wraps: the segments are kept out once
                // more (a hair of stretch is left to the next substep instead of a wire cutting through a part).
                CollideSegments(h);
                SweepAll(h);
                NotePoses();
                for (int i = 0; i < count; i++)
                {
                    if (IsPinned(i)) _v[i] = Vec3.Zero;
                    else _v[i] = (_x[i] - _p[i]) / h;
                }

                // Axial force next to each plug: −λ/h² of the first free segments.
                tensionA += SegmentTension(_pinned - 1, h);
                tensionB += SegmentTension(Segments - _pinned, h);
            }

            tensionA /= subs;
            tensionB /= subs;
            if (couple) NoteRest(dt);
            double a = Math.Min(1, dt / 0.004);
            A.Tension += ((A.Attached ? tensionA : 0) - A.Tension) * a;
            B.Tension += ((B.Attached ? tensionB : 0) - B.Tension) * a;
            if (!couple) return;

            Couple(A, 0, _pinned);
            Couple(B, Segments, Segments - _pinned);
            if (InternalTo != null) return;   // [weld] its tension is internal to the frame both plugs sit on
            if (A.Attached && A.Tension > PullOutForce) Release(A, A.Tension);
            if (B.Attached && B.Tension > PullOutForce) Release(B, B.Tension);
        }

        private bool _carry, _frameSeen;
        private double _relStill;
        private Vec3 _frameV, _gLocal, _gRest, _carryX;
        private Quat _carryQ = Quat.Identity;

        /// <summary>
        /// [phys] The resting wire of a moving rigid body. A wire with both plugs on one rigid body (a fuselage and
        /// everything welded into it) and nothing else around it, lying still relative to that body, stays where it is as
        /// long as what it feels there does not change: gravity less the body's own acceleration, seen in the body's
        /// frame. While that holds within 15 % of g it is carried rigidly with the body instead of being stepped; a
        /// change beyond it (a pull-up, a landing, a crash) lets it move again until it settles anew. In steady flight
        /// the wiring of an aircraft costs nothing, as a still cable on a still bench already does.
        /// </summary>
        private bool Carried(PhysicsScene scene, double dt)
        {
            var frame = A.Body;
            if (InternalTo == null || frame == null || !A.Attached || !B.Attached)
            {
                _carry = false;
                return false;
            }

            var acc = _frameSeen ? (frame.Velocity - _frameV) / dt : Vec3.Zero;
            _frameV = frame.Velocity;
            _frameSeen = true;
            _gLocal = frame.Orientation.InverseRotate(scene.Gravity - acc);
            if (!_carry) return false;
            if ((_gLocal - _gRest).Length > 0.15 * Math.Max(1, _gRest.Length) || !OnlyFrameNear(scene))
            {
                _carry = false;
                _relStill = 0;
                return false;
            }

            var turn = frame.Orientation * _carryQ.Conjugate;
            for (int i = 0; i < _x.Length; i++)
            {
                _x[i] = frame.Position + turn.Rotate(_x[i] - _carryX);
                _v[i] = frame.PointVelocity(_x[i]);
            }

            _carryX = frame.Position;
            _carryQ = frame.Orientation;
            Pin();
            NotePoses();
            return true;
        }

        /// <summary>After a stepped move: once the wire has lain still relative to its rigid body for 0.2 s, carry it.</summary>
        private void NoteRest(double dt)
        {
            var frame = A.Body;
            if (InternalTo == null || frame == null || !A.Attached || !B.Attached || !_frameSeen) return;
            double vmax = 0;
            for (int i = 0; i < _x.Length; i++)
            {
                if (IsPinned(i)) continue;
                vmax = Math.Max(vmax, (_v[i] - frame.PointVelocity(_x[i])).LengthSquared);
            }

            _relStill = vmax < 1e-4 ? _relStill + dt : 0;   // under 1 cm/s
            if (_relStill < 0.2 || !OnlyFrameNear(_scene!)) return;
            _carry = true;
            _gRest = _gLocal;
            _carryX = frame.Position;
            _carryQ = frame.Orientation;
        }

        /// <summary>
        /// Whether nothing but the rigid body it is carried with can touch the wire: any other part near it (a servo horn
        /// swinging, a motor's rotor) is at least 5 mm clear of it, and so is the floor.
        /// </summary>
        private bool OnlyFrameNear(PhysicsScene scene)
        {
            double clear = Radius + 0.005;
            foreach (var b in _near)
            {
                if (InternalTo!.Contains(b) || (Ignore != null && Ignore.Contains(b))) continue;
                var shape = b.EffectiveShape!;
                double reach = shape.BoundingRadius + clear;
                foreach (var x in _x)
                {
                    if ((x - b.Position).LengthSquared > reach * reach) continue;
                    if (b.SignedDistanceSolid(shape, b.Orientation.InverseRotate(x - b.Position), clear, out double d, out _) && d < clear) return false;
                }
            }

            if (!scene.GroundEnabled) return true;
            foreach (var x in _x)
                if (x.Z < scene.GroundHeight(x.X, x.Y) + Radius + 0.01) return false;
            return true;
        }

        /// <summary>
        /// Lets the cable settle against frozen bodies (edit mode): gravity, contacts and strong
        /// damping, no forces on the bodies, no unplugging.
        /// </summary>
        public void Relax(PhysicsScene scene, int steps, double dt = 0.002) => RelaxPart(scene, 0, steps, steps, dt);

        /// <summary>
        /// [perf] Steps <paramref name="from"/> … <paramref name="from"/> + <paramref name="count"/> of a
        /// <paramref name="total"/>-step <see cref="Relax"/> (the same schedule), so a layout can settle over several
        /// frames instead of holding one; the chunk that reaches <paramref name="total"/> leaves the rope at rest.
        /// </summary>
        public void RelaxPart(PhysicsScene scene, int from, int count, int total, double dt = 0.002)
        {
            double damping = Damping;
            int subs = Substeps;
            Substeps = 2;
            int end = Math.Min(total, from + count);
            for (int k = from; k < end; k++)
            {
                double f = k / (double)Math.Max(1, total - 1);
                Damping = 6 + 40 * f * f;
                // Dropped limp first (so it drapes instead of standing as a buckled arch), then stiffened.
                _bendScale = f < 0.5 ? 0.02 : Math.Min(1, 0.02 + (f - 0.5) / 0.3);
                Step(scene, dt, false);
            }

            _bendScale = 1;
            if (end >= total)
            {
                for (int i = 0; i < _v.Length; i++) _v[i] = Vec3.Zero;
            }

            Damping = damping;
            Substeps = subs;
        }

        /// <summary>
        /// Rope sleeping: once it has lain still for 0.2 s it is not stepped until a plug moves or
        /// a body next to it moves.
        /// </summary>
        private bool Sleeping(PhysicsScene scene, double dt)
        {
            var a = A.Attached ? A.World : _x[0];
            var b = B.Attached ? B.World : _x[Segments];
            bool endsStill = (a - _lastA).LengthSquared < 1e-12 && (b - _lastB).LengthSquared < 1e-12;
            _lastA = a;
            _lastB = b;
            bool nearStill = true;
            foreach (var body in _near)
            {
                if (body.IsStatic) continue;
                double r = body.EffectiveShape?.BoundingRadius ?? 0.05;
                if (body.Velocity.LengthSquared > 1e-6 || body.AngularVelocity.Length * r > 1e-3)
                {
                    nearStill = false;
                    break;
                }
            }

            if (!endsStill || !nearStill)
            {
                _asleep = false;
                _still = 0;
                return false;
            }

            if (_asleep) return true;
            double vmax = 0;
            foreach (var v in _v) vmax = Math.Max(vmax, v.LengthSquared);
            _still = vmax < 4e-6 ? _still + dt : 0;
            if (_still < 0.2) return false;
            _asleep = true;
            for (int i = 0; i < _v.Length; i++) _v[i] = Vec3.Zero;
            return true;
        }

        private double SegmentTension(int seg, double h)
        {
            if (seg < 0 || seg >= Segments) return 0;
            // Average over three segments to smooth the per-particle noise.
            double sum = 0;
            int n = 0;
            int dir = seg < Segments / 2 ? 1 : -1;
            for (int k = 0; k < 3; k++)
            {
                int s = seg + dir * k;
                if (s < 0 || s >= Segments) break;
                sum += Math.Max(0, -_lambda[s]) / (h * h);
                n++;
            }

            return n > 0 ? sum / n : 0;
        }

        private void Couple(RopeEnd end, int pin, int next)
        {
            if (!end.Attached || end.Body == null || end.Body.IsStatic || end.Tension <= 0) return;
            if (InternalTo != null && InternalTo.Contains(end.Body)) return;   // [weld] internal to one rigid body
            var d = _x[next] - _x[pin];
            double len = d.Length;
            if (len < 1e-9) return;
            double f = Math.Min(end.Tension, PullOutForce * 1.2);
            Push(end.Body, d / len * f, end.World);
        }

        private void Release(RopeEnd end, double force)
        {
            end.Attached = false;
            end.Tension = 0;
            if (Deferred) _releases.Add((end, force));
            else Unplugged?.Invoke(end, force);
        }

        /// <summary>Pulls a plug out by hand (or by a failure elsewhere).</summary>
        public void Detach(RopeEnd end)
        {
            if (end.Attached) Release(end, 0);
        }

        private void SolveStretch(double h)
        {
            double alpha = SegmentLength / AxialStiffness / (h * h);
            for (int s = 0; s < Segments; s++)
            {
                int i = s, j = s + 1;
                double wi = IsPinned(i) ? 0 : _w[i], wj = IsPinned(j) ? 0 : _w[j];
                double ws = wi + wj;
                if (ws <= 0) continue;
                var d = _x[j] - _x[i];
                double len = d.Length;
                if (len < 1e-12) continue;
                double c = len - SegmentLength;
                double dl = (-c - alpha * _lambda[s]) / (ws + alpha);
                _lambda[s] += dl;
                var n = d / len;
                _x[i] -= n * (dl * wi);
                _x[j] += n * (dl * wj);
            }
        }

        private void SolveBend(double h)
        {
            if (BendStiffness <= 0) return;
            double l = SegmentLength;
            double alpha = l * l * l / (BendStiffness * _bendScale) / (h * h);
            for (int i = 1; i < Segments; i++)
            {
                double w0 = IsPinned(i - 1) ? 0 : _w[i - 1], w1 = IsPinned(i) ? 0 : _w[i], w2 = IsPinned(i + 1) ? 0 : _w[i + 1];
                double ws = w0 + 4 * w1 + w2;
                if (ws <= 0) continue;
                var c = _x[i - 1] - _x[i] * 2 + _x[i + 1];
                var dl = (c * -1 - _bendLambda[i] * alpha) / (ws + alpha);
                _bendLambda[i] += dl;
                _x[i - 1] += dl * w0;
                _x[i] -= dl * (2 * w1);
                _x[i + 1] += dl * w2;
            }
        }

        // ================================================================ collision

        private void Gather(PhysicsScene scene, double dt)
        {
            _near.Clear();
            var lo = _x[0];
            var hi = _x[0];
            foreach (var p in _x)
            {
                lo = new Vec3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
                hi = new Vec3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
            }

            double pad = Radius + SegmentLength + 0.01;
            lo -= new Vec3(pad, pad, pad);
            hi += new Vec3(pad, pad, pad);
            foreach (var b in scene.Bodies)
            {
                if (!b.Collides || (Ignore != null && Ignore.Contains(b))) continue;
                var s = b.EffectiveShape;
                if (s == null || !s.HasSolid) continue;
                // [perf] a mesh's BoundingRadius encloses its vertices too: a far mesh (a wing, the terrain's far side,
                // another part) is left out like any far body instead of being tested at every node and substep.
                double r = s.BoundingRadius + b.Velocity.Length * dt;
                var c = b.Position;
                if (c.X + r < lo.X || c.X - r > hi.X || c.Y + r < lo.Y || c.Y - r > hi.Y || c.Z + r < lo.Z || c.Z - r > hi.Z) continue;
                _near.Add(b);
            }

            // [perf] Each near body's tightest enclosing sphere in the world (its shape's own middle, not the body's
            // origin, which lies at a corner of many parts): the bodies stand still while the rope steps.
            if (_nearCentre.Length < _near.Count)
            {
                _nearCentre = new Vec3[_near.Count * 2];
                _nearRadius = new double[_near.Count * 2];
            }

            for (int k = 0; k < _near.Count; k++)
            {
                var b = _near[k];
                var shape = b.EffectiveShape!;
                _nearCentre[k] = b.Position + b.Orientation.Rotate(shape.LocalCentre);
                _nearRadius[k] = shape.LocalRadius;
            }
        }

        private Vec3[] _nearCentre = new Vec3[8];
        private double[] _nearRadius = new double[8];

        /// <summary>Whether a point (world) is farther than <paramref name="margin"/> from near body <paramref name="k"/>'s solid, by its sphere.</summary>
        private bool Beyond(int k, Vec3 x, double margin)
        {
            double reach = _nearRadius[k] + margin;
            return (x - _nearCentre[k]).LengthSquared > reach * reach;
        }

        /// <summary>
        /// Bodies the wire does not touch: propellers, whose body is the disc the blades sweep, not a solid. (A wire in a
        /// real prop disc would be struck and cut; that is not modelled: <see cref="KeepOut"/> keeps it out instead.)
        /// </summary>
        public HashSet<SceneBody>? Ignore { get; set; }

        /// <summary>
        /// Space the wire is kept out of although nothing solid is there (a propeller's swept disc, as cable ties keep a
        /// quad's leads under its props): the move that takes a point, with the wire's clearance, out of it; null when it
        /// is not in it. The wire is moved, nothing is pushed.
        /// </summary>
        public Func<Vec3, double, Vec3?>? KeepOut { get; set; }

        /// <summary>How deep the wire lies in its keep-out space, summed over its points (m; 0: clear).</summary>
        public double KeepOutDepth()
        {
            if (KeepOut == null) return 0;
            double sum = 0;
            for (int i = 0; i <= Segments; i++)
                if (KeepOut(_x[i], Radius) is { } push) sum += push.Length;
            return sum;
        }

        /// <summary>
        /// [weld] The rigid body both plugs sit on (its welded parts), when they do. The wire still lies on it, but the
        /// forces between them are internal: its pull on the plugs and its push on the frame cancel, so neither is
        /// applied (a lagging, filtered plug tension against an instant contact push used to kick the frame), and a
        /// fight between the wire's length and the frame it is laid through does not pull its plugs out.
        /// </summary>
        public HashSet<SceneBody>? InternalTo { get; set; }

        private double _coupleDt;

        /// <summary>[phys] Each nearby body's pose at the last collision pass, to see the wire's motion from the body.</summary>
        private readonly Dictionary<SceneBody, (Vec3 X, Quat Q, int Pass)> _poses = new Dictionary<SceneBody, (Vec3, Quat, int)>();
        private int _pass;

        private bool Ignored(SceneBody body, int i)
        {
            double arc = i * SegmentLength;
            if (A.Attached && body == A.Body && arc < PlugClearance) return true;
            if (B.Attached && body == B.Body && Length - arc < PlugClearance) return true;
            return false;
        }

        private void Collide(PhysicsScene scene, double h)
        {
            int count = Segments + 1;
            double r = Radius;
            for (int i = 0; i < count; i++)
            {
                if (IsPinned(i)) continue;
                ref var x = ref _x[i];
                // Floor.
                if (scene.GroundEnabled)
                {
                    double gz = scene.GroundHeight(x.X, x.Y);
                    double pen = gz + r - x.Z;
                    if (pen > 0)
                    {
                        var n = scene.GroundNormal(x.X, x.Y);
                        x += n * pen;
                        ApplyFriction(i, n, pen, Vec3.Zero, h);
                    }
                }

                for (int k = 0; k < _near.Count; k++)
                {
                    var b = _near[k];
                    if (Ignored(b, i)) continue;
                    Sweep(b, ref x, i, h);
                    if (!Beyond(k, x, r)) PushOut(b, ref x, i, h, 1.0);
                }

                if (KeepOut != null && KeepOut(x, r) is { } push) x += push;
            }

            CollideSegments(h);
        }

        /// <summary>
        /// Segments as capsules of the wire's radius, so a thin part (a wall's edge, a plate) cannot slip in between two
        /// particles, and is pushed off the way that frees the segment soonest: a wire drawn up against the top edge of a
        /// cable hole slides back into the hole instead of being squeezed through the wall above it.
        /// </summary>
        private void CollideSegments(double h)
        {
            double r = Radius;
            for (int s = 0; s < Segments; s++)
            {
                int i = s, j = s + 1;
                bool pi = IsPinned(i), pj = IsPinned(j);
                if (pi && pj) continue;
                for (int k = 0; k < _near.Count; k++)
                {
                    var b = _near[k];
                    if (Ignored(b, i) || Ignored(b, j)) continue;
                    var shape = b.EffectiveShape!;
                    var mid = (_x[i] + _x[j]) * 0.5;
                    double half = 0.5 * (_x[j] - _x[i]).Length;
                    if (Beyond(k, mid, r + half)) continue;
                    if (b.Pieces is { Length: > 0 } pieces)
                    {
                        foreach (var piece in pieces) SegmentContact(b, piece, i, j, pi, pj, h);
                    }
                    else
                    {
                        SegmentContact(b, shape, i, j, pi, pj, h);
                    }
                }
            }
        }

        /// <summary>
        /// The least push that takes a segment, a capsule of the wire's radius, out of one convex solid (body frame):
        /// separating axes of the solid's faces and of the segment crossed with its edges for polyhedra; the deepest point
        /// along the segment for round shapes. Applied to the segment's free particles; the body gets the reaction.
        /// </summary>
        private void SegmentContact(SceneBody b, CollisionShape shape, int i, int j, bool pi, bool pj, double h)
        {
            double r = Radius;
            var la = b.Orientation.InverseRotate(_x[i] - b.Position);
            var lb = b.Orientation.InverseRotate(_x[j] - b.Position);
            // Every point of the segment is within half its length of its middle: a solid farther than that (plus the
            // wire's radius) from the middle cannot touch it, which settles most pairs before the separating axes.
            double reach = 0.5 * (lb - la).Length + r;
            if (shape.Kind != ShapeKind.Mesh && !shape.SignedDistance((la + lb) * 0.5, reach, out _, out _)) return;
            Vec3 pushLocal;
            if (shape.Kind == ShapeKind.Box || shape.Kind == ShapeKind.ConvexHull || shape.Kind == ShapeKind.Points)
            {
                if (shape.Planes.Length == 0 && shape.Kind != ShapeKind.Box) return;
                var d = lb - la;
                double len = d.Length;
                if (len < 1e-12) return;
                var u = d / len;
                double best = double.MaxValue;
                var bestN = Vec3.Zero;
                var spans = shape.Kind == ShapeKind.Box ? null : shape.PlaneSpans;   // [perf] the face axes' extents, kept
                bool Overlaps(Vec3 n, int face = -1)
                {
                    double nl = n.Length;
                    if (nl < 1e-9) return true;
                    n /= nl;
                    double lo, hi;
                    if (shape.Kind == ShapeKind.Box)
                    {
                        var e = shape.HalfExtents;
                        hi = e.X * Math.Abs(n.X) + e.Y * Math.Abs(n.Y) + e.Z * Math.Abs(n.Z);
                        lo = -hi;
                    }
                    else if (face >= 0)
                    {
                        (lo, hi) = spans![face];
                    }
                    else
                    {
                        lo = double.MaxValue;
                        hi = double.MinValue;
                        foreach (var v in shape.Vertices)
                        {
                            double q = v.Dot(n);
                            if (q < lo) lo = q;
                            if (q > hi) hi = q;
                        }
                    }

                    double pa = la.Dot(n), pb = lb.Dot(n);
                    double smin = Math.Min(pa, pb) - r, smax = Math.Max(pa, pb) + r;
                    double up = hi - smin, down = smax - lo;
                    if (up <= 0 || down <= 0) return false;
                    if (up < best)
                    {
                        best = up;
                        bestN = n;
                    }

                    if (down < best)
                    {
                        best = down;
                        bestN = -n;
                    }

                    return true;
                }

                if (shape.Kind == ShapeKind.Box)
                {
                    if (!Overlaps(Vec3.UnitX) || !Overlaps(Vec3.UnitY) || !Overlaps(Vec3.UnitZ)) return;
                }
                else
                {
                    var planes = shape.Planes;
                    for (int k = 0; k < planes.Length; k++)
                        if (!Overlaps(planes[k].Normal, k)) return;
                }

                if (!Overlaps(u.Cross(Vec3.UnitX)) || !Overlaps(u.Cross(Vec3.UnitY)) || !Overlaps(u.Cross(Vec3.UnitZ))) return;
                pushLocal = bestN * best;
            }
            else if (shape.Kind == ShapeKind.Mesh)
            {
                var mid = (la + lb) * 0.5;
                if (!shape.SignedDistance(mid, r, out double dm, out var nm) || dm >= r) return;
                pushLocal = nm * (r - dm);
            }
            else
            {
                // Round solid: the signed distance along a segment is convex, so its deepest point is found by bisection.
                double Dist(double t) => shape.SignedDistance(la + (lb - la) * t, double.MaxValue, out double dd, out _) ? dd : double.MaxValue;
                double a0 = 0, a1 = 1;
                for (int k = 0; k < 24; k++)
                {
                    double m1 = a0 + (a1 - a0) / 3, m2 = a1 - (a1 - a0) / 3;
                    if (Dist(m1) < Dist(m2)) a1 = m2;
                    else a0 = m1;
                }

                double tm = 0.5 * (a0 + a1);
                if (!shape.SignedDistance(la + (lb - la) * tm, r, out double dr, out var nr) || dr >= r) return;
                pushLocal = nr * (r - dr);
            }

            var push = b.Orientation.Rotate(pushLocal);
            var moved = Vec3.Zero;   // mass-weighted push given to the wire
            if (!pi)
            {
                var di = pj ? push * 2 : push;
                _x[i] += di;
                moved += di * (1.0 / _w[i]);
            }

            if (!pj)
            {
                var dj = pi ? push * 2 : push;
                _x[j] += dj;
                moved += dj * (1.0 / _w[j]);
            }

            if (_coupleDt > 0 && !b.IsStatic && (InternalTo == null || !InternalTo.Contains(b)))
                Push(b, moved * (-1.0 / (h * h) * (h / _coupleDt)), (_x[i] + _x[j]) * 0.5);   // [phys] reaction on the body
        }

        /// <summary>[phys] Continuous collision again after the last length pass, which also moves particles.</summary>
        private void SweepAll(double h)
        {
            for (int i = 0; i < _x.Length; i++)
            {
                if (IsPinned(i)) continue;
                var x = _x[i];
                foreach (var b in _near)
                    if (!Ignored(b, i)) Sweep(b, ref x, i, h);
            }
        }

        /// <summary>Where each nearby body stood at the end of this substep (the next one's start).</summary>
        private void NotePoses()
        {
            _pass++;
            foreach (var b in _near) _poses[b] = (b.Position, b.Orientation, _pass);
        }

        /// <summary>
        /// [phys] Continuous collision. A bit of wire may not move into or through a solid within a substep, judged in the
        /// body's frame (the body may have moved too): a 1 cm move crosses a 2 mm wall in one step, and the push-out alone
        /// would then put the wire out on the far side. The move stops on the face it reached and keeps its slide along it;
        /// the push that stopped it acts back on the body.
        /// </summary>
        private void Sweep(SceneBody b, ref Vec3 x, int i, double h)
        {
            var shape = b.EffectiveShape!;
            var start = _p[i];
            var ls = _poses.TryGetValue(b, out var pose) && pose.Pass == _pass
                ? pose.Q.InverseRotate(start - pose.X)
                : b.Orientation.InverseRotate(start - b.Position);
            var le = b.Orientation.InverseRotate(x - b.Position);
            var d = le - ls;
            double len = d.Length;
            if (len < 1e-7) return;
            // [perf] The sphere about the shape's own middle holds all of it (a mesh's too): a move that stays outside
            // that sphere cannot reach it.
            var mid = shape.LocalCentre;
            double reach = shape.LocalRadius + Radius;
            double f = Math.Max(0, Math.Min(1, -(ls - mid).Dot(d) / (len * len)));
            if ((ls - mid + d * f).LengthSquared > reach * reach) return;

            // A bit of wire that already started inside is pushed out by its nearest face instead.
            if (b.Pieces == null && !shape.HasFloorFace)
            {
                // [perf] Asked up to the move's length: a start farther than that from the solid cannot reach it (the
                // distance a convex shape reports outside it never exceeds the true one), and the ray is not cast.
                if (!b.SignedDistanceSolid(shape, ls, len + 1e-9, out double far, out _) || far < 0) return;
            }
            else if (b.SignedDistanceSolid(shape, ls, 0, out double ds, out _) && ds < 0)
            {
                return;
            }
            var dir = d / len;
            if (!b.RaycastSolid(shape, ls, dir, len, out double t, out var nl)) return;
            var hit = ls + dir * t;
            var stopped = le + nl * ((hit - le).Dot(nl) + Radius);   // on the face it reached, its radius off it
            var before = x;
            x = b.Position + b.Orientation.Rotate(stopped);
            _x[i] = x;
            if (_coupleDt > 0 && !b.IsStatic && _w[i] > 0 && (InternalTo == null || !InternalTo.Contains(b)))
                Push(b, (before - x) * (1.0 / _w[i] / (h * h) * (h / _coupleDt)), x);
        }

        private void PushOut(SceneBody b, ref Vec3 x, int i, double h, double scale)
        {
            var shape = b.EffectiveShape!;
            double r = Radius;
            if ((x - b.Position).LengthSquared > (shape.BoundingRadius + r) * (shape.BoundingRadius + r)) return;
            var local = b.Orientation.InverseRotate(x - b.Position);
            if (!b.SignedDistanceSolid(shape, local, r, out double d, out var nl) || d >= r) return;
            var n = b.Orientation.Rotate(nl);
            double pen = (r - d) * scale;
            var before = x;
            x += n * pen;
            _x[i] = x;
            ApplyFriction(i, n, pen, b.IsStatic ? Vec3.Zero : b.PointVelocity(x) * h, h);
            x = _x[i];
            // [phys] Newton's third law: the push that kept this bit of wire out of the body (and the friction that
            // dragged it along) pushes the body back, as an average force over the outer step.
            if (_coupleDt > 0 && !b.IsStatic && _w[i] > 0 && (InternalTo == null || !InternalTo.Contains(b)))
                Push(b, (before - x) * (1.0 / _w[i] / (h * h) * (h / _coupleDt)), x);
        }

        /// <summary>Positional Coulomb friction: tangential slip relative to the surface is cancelled up to μ·penetration.</summary>
        private void ApplyFriction(int i, Vec3 n, double pen, Vec3 surfaceMove, double h)
        {
            var move = _x[i] - _p[i] - surfaceMove;
            var t = move - n * move.Dot(n);
            double tl = t.Length;
            if (tl < 1e-12) return;
            double max = Friction * pen;
            _x[i] -= tl <= max ? t : t * (max / tl);
        }

        private double Penetration(PhysicsScene scene)
        {
            double worst = 0;
            for (int i = 0; i < _x.Length; i++)
            {
                if (IsPinned(i)) continue;
                var x = _x[i];
                if (scene.GroundEnabled) worst = Math.Max(worst, scene.GroundHeight(x.X, x.Y) + Radius - x.Z);
                foreach (var b in _near)
                {
                    if (Ignored(b, i)) continue;
                    var shape = b.EffectiveShape!;
                    var local = b.Orientation.InverseRotate(x - b.Position);
                    if (local.LengthSquared > (shape.BoundingRadius + Radius) * (shape.BoundingRadius + Radius)) continue;
                    if (b.SignedDistanceSolid(shape, local, Radius, out double d, out _) && d < Radius) worst = Math.Max(worst, Radius - d);
                }
            }

            return worst;
        }

        /// <summary>Momentum of the wire's free particles (kg·m/s), for checking that its contacts act and react.</summary>
        public Vec3 Momentum
        {
            get
            {
                var sum = Vec3.Zero;
                for (int i = 0; i < _v.Length; i++)
                    if (!IsPinned(i) && _w[i] > 0) sum += _v[i] * (1.0 / _w[i]);
                return sum;
            }
        }

        /// <summary>Current length of the polyline (m); above <see cref="Length"/> when stretched.</summary>
        public double CurrentLength()
        {
            double len = 0;
            for (int i = 1; i < _x.Length; i++) len += (_x[i] - _x[i - 1]).Length;
            return len;
        }
    }
}
