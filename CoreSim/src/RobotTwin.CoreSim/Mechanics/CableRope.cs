using System;
using System.Collections.Generic;

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
            int n = (int)Math.Ceiling(Length / 0.012);
            Segments = Math.Max(6, Math.Min(48, n));
            SegmentLength = Length / Segments;

            double area = CrossSection(awg);
            double rCu = Math.Sqrt(area / Math.PI);
            Radius = rCu + 0.0003;
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

        private PhysicsScene? _scene;
        private bool _asleep;
        private double _still;
        private Vec3 _lastA, _lastB;

        /// <summary>Raised when a plug is pulled out (end, force in N).</summary>
        public event Action<RopeEnd, double>? Unplugged;

        public static double CrossSection(double awg)
        {
            double d = 0.127e-3 * Math.Pow(92.0, (36.0 - awg) / 39.0);
            return Math.PI * d * d / 4.0;
        }

        /// <summary>Default cable length when none is given: the distance between the pins plus slack (m).</summary>
        public static double DefaultLength(double span) => Math.Max(0.05, 1.15 * span + 0.03);

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
                double pen = Penetration(scene) + Depth(scene);
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
                    if (shape.Kind != ShapeKind.Mesh && local.LengthSquared > (shape.BoundingRadius + Radius) * (shape.BoundingRadius + Radius)) continue;
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

            Gather(scene, dt);
            int subs = Math.Max(1, Substeps);
            double h = dt / subs;
            var g = scene.Gravity;
            double keep = Math.Exp(-Damping * h);
            int count = Segments + 1;
            double tensionA = 0, tensionB = 0;
            for (int s = 0; s < subs; s++)
            {
                for (int i = 0; i < count; i++)
                {
                    _p[i] = _x[i];
                    if (IsPinned(i)) continue;
                    _v[i] = (_v[i] + g * h) * keep;
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

        /// <summary>
        /// Lets the cable settle against frozen bodies (edit mode): gravity, contacts and strong
        /// damping, no forces on the bodies, no unplugging.
        /// </summary>
        public void Relax(PhysicsScene scene, int steps, double dt = 0.002)
        {
            double damping = Damping;
            int subs = Substeps;
            Substeps = 2;
            for (int k = 0; k < steps; k++)
            {
                double f = k / (double)Math.Max(1, steps - 1);
                Damping = 6 + 40 * f * f;
                // Dropped limp first (so it drapes instead of standing as a buckled arch), then stiffened.
                _bendScale = f < 0.5 ? 0.02 : Math.Min(1, 0.02 + (f - 0.5) / 0.3);
                Step(scene, dt, false);
            }

            _bendScale = 1;

            for (int i = 0; i < _v.Length; i++) _v[i] = Vec3.Zero;
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
            end.Body.AddForceAtPoint(d / len * f, end.World);
        }

        private void Release(RopeEnd end, double force)
        {
            end.Attached = false;
            end.Tension = 0;
            Unplugged?.Invoke(end, force);
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
                if (s.Kind == ShapeKind.Mesh)
                {
                    _near.Add(b);
                    continue;
                }

                double r = s.BoundingRadius + b.Velocity.Length * dt;
                var c = b.Position;
                if (c.X + r < lo.X || c.X - r > hi.X || c.Y + r < lo.Y || c.Y - r > hi.Y || c.Z + r < lo.Z || c.Z - r > hi.Z) continue;
                _near.Add(b);
            }
        }

        /// <summary>
        /// Bodies the wire does not touch: propellers, whose body is the disc the blades sweep, not a solid. (A wire in a
        /// real prop disc would be struck and cut; that is not modelled.)
        /// </summary>
        public HashSet<SceneBody>? Ignore { get; set; }

        /// <summary>
        /// [weld] The rigid body both plugs sit on (its welded parts), when they do. The wire still lies on it, but the
        /// forces between them are internal: its pull on the plugs and its push on the frame cancel, so neither is
        /// applied (a lagging, filtered plug tension against an instant contact push used to kick the frame), and a
        /// fight between the wire's length and the frame it is laid through does not pull its plugs out.
        /// </summary>
        public HashSet<SceneBody>? InternalTo { get; set; }

        private double _coupleDt;

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

                foreach (var b in _near)
                {
                    if (Ignored(b, i)) continue;
                    PushOut(b, ref x, i, h, 1.0);
                }
            }

            // Segment midpoints, so a thin part cannot slip between two particles.
            for (int s = 0; s < Segments; s++)
            {
                int i = s, j = s + 1;
                bool pi = IsPinned(i), pj = IsPinned(j);
                if (pi && pj) continue;
                var mid = (_x[i] + _x[j]) * 0.5;
                foreach (var b in _near)
                {
                    if (Ignored(b, i) || Ignored(b, j)) continue;
                    var shape = b.EffectiveShape!;
                    if (shape.Kind != ShapeKind.Mesh && (mid - b.Position).LengthSquared > (shape.BoundingRadius + r) * (shape.BoundingRadius + r)) continue;
                    var local = b.Orientation.InverseRotate(mid - b.Position);
                    if (!b.SignedDistanceSolid(shape, local, r, out double d, out var nl) || d >= r) continue;
                    var n = b.Orientation.Rotate(nl);
                    var push = n * (r - d);
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

                    if (_coupleDt > 0 && !b.IsStatic && (InternalTo == null || !InternalTo.Contains(b))) b.AddForceAtPoint(moved * (-1.0 / (h * h) * (h / _coupleDt)), mid);   // [phys] reaction on the body
                }
            }
        }

        private void PushOut(SceneBody b, ref Vec3 x, int i, double h, double scale)
        {
            var shape = b.EffectiveShape!;
            double r = Radius;
            if (shape.Kind != ShapeKind.Mesh && (x - b.Position).LengthSquared > (shape.BoundingRadius + r) * (shape.BoundingRadius + r)) return;
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
                b.AddForceAtPoint((before - x) * (1.0 / _w[i] / (h * h) * (h / _coupleDt)), x);
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
                    if (shape.Kind != ShapeKind.Mesh && local.LengthSquared > (shape.BoundingRadius + Radius) * (shape.BoundingRadius + Radius)) continue;
                    if (b.SignedDistanceSolid(shape, local, Radius, out double d, out _) && d < Radius) worst = Math.Max(worst, Radius - d);
                }
            }

            return worst;
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
