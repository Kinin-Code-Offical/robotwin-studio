using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// [liquid] A body of liquid as particles: position-based fluids (Macklin &amp; Müller 2013), i.e. incompressibility as
    /// a density constraint per particle solved like every other XPBD constraint of the scene (the cables' rope uses the
    /// same scheme). Each particle carries an equal share of the liquid's mass at the real density ρ0.
    /// <list type="bullet">
    /// <item>Density: cubic-spline SPH sum over neighbours within 2 spacings; the solver drives ρ/ρ0 − 1 to zero (pressure
    /// only pushes, the free surface is free).</item>
    /// <item>Walls: every solid and the ground contribute the density of the liquid a wall would hold back, as a function of
    /// the particle's distance to it (density maps, Koschier &amp; Bender 2017, for a locally flat wall). The wall therefore
    /// carries the hydrostatic pressure of the liquid, the particle never needs to touch it, and the push on the wall is the
    /// force on that body: a glass feels the weight of its water, a block in it feels buoyancy.</item>
    /// <item>No tunnelling: a particle may not cross a solid within a step, judged in the body's own frame (the glass may
    /// be turning), as for the cables.</item>
    /// <item>Viscosity: the real one, ν = μ/ρ, with the SPH Laplacian of Morris et al. (1997), solved implicitly so honey
    /// stays stable at the same step; the wall is a no-slip boundary moving with the body.</item>
    /// <item>Surface tension: the real σ as the continuum surface force σκn (Brackbill et al. 1992; Müller et al. 2003).</item>
    /// </list>
    /// Steps are subdivided so no particle moves more than 0.3 spacing per substep. Everything runs in parallel and is
    /// deterministic for a given particle order.
    /// </summary>
    public sealed partial class LiquidBody
    {
        private const int MaxNeighbours = 64;
        private const int Slots = 3;   // wall slots per particle: ground + the two nearest bodies

        private readonly double _l, _h, _sigma, _s3;
        private readonly double[] _phi, _area;   // half-space kernel integrals, tabulated over [0, h]
        private const int Table = 128;

        private double[] _x = Array.Empty<double>(), _v = Array.Empty<double>(), _p = Array.Empty<double>();
        private double[] _rho = Array.Empty<double>(), _lam = Array.Empty<double>(), _dp = Array.Empty<double>(), _c = Array.Empty<double>();
        private int[] _nb = Array.Empty<int>(), _nbCount = Array.Empty<int>();
        private int[] _wb = Array.Empty<int>();   // body slot (index into _near), -2 = ground, -1 = none
        private double[] _wd = Array.Empty<double>(), _wn = Array.Empty<double>(), _wv = Array.Empty<double>();
        private double[] _imp = Array.Empty<double>();   // positional push each slot gave its wall this substep (m)
        private int[] _cell = Array.Empty<int>(), _cellStart = Array.Empty<int>(), _sorted = Array.Empty<int>();
        private readonly List<SceneBody> _near = new List<SceneBody>();
        private readonly Dictionary<SceneBody, (Vec3 X, Quat Q)> _poses = new Dictionary<SceneBody, (Vec3, Quat)>();
        private (Vec3 X, Quat Q)[] _start = Array.Empty<(Vec3, Quat)>();
        private int _substeps, _still;

        public LiquidBody(string id, LiquidKind kind, double spacing)
        {
            Id = id;
            Kind = kind;
            Spacing = spacing;
            Radius = 0.5 * spacing;
            _l = spacing;
            _h = 2 * spacing;
            _sigma = 1 / (Math.PI * _l * _l * _l);
            _s3 = spacing * spacing * spacing;
            // Rest-lattice sum: a perfect cubic lattice at this spacing is exactly at the real density.
            double sum = 0;
            for (int i = -3; i <= 3; i++)
            for (int j = -3; j <= 3; j++)
            for (int k = -3; k <= 3; k++)
                sum += W(Math.Sqrt(i * i + j * j + k * k) * spacing);
            LatticeSum = sum;
            Mass = kind.Density / sum;
            // A(z) = ∫∫ W over the plane at height z = 2π ∫_z^h W(r) r dr;  Φ(d) = ∫_d^h A(z) dz (the share of the
            // kernel beyond a flat wall at distance d).
            _area = new double[Table + 1];
            _phi = new double[Table + 1];
            const int sub = 64;
            for (int t = Table; t >= 0; t--)
            {
                double z = _h * t / Table, a = 0, dr = (_h - z) / sub;
                for (int q = 0; q < sub; q++)
                {
                    double r = z + (q + 0.5) * dr;
                    a += W(r) * r * dr;
                }

                _area[t] = 2 * Math.PI * a;
                _phi[t] = t == Table ? 0 : _phi[t + 1] + 0.5 * (_area[t] + _area[t + 1]) * (_h / Table);
                double z1 = _h * (t + 1) / Table;
                _visc[t] = t == Table ? 0 : _visc[t + 1] + 2 * Math.PI * 0.5 * (W(z) + W(z1)) * (_h / Table);
            }
        }

        private readonly double[] _visc = new double[Table + 1];

        /// <summary>∫ over the half-space beyond a wall at distance d of −W'(r)/r: the wall's share of the viscous Laplacian.</summary>
        private double WallViscous(double d)
        {
            double u = Math.Max(0, Math.Min(_h, d)) / _h * Table;
            int t = Math.Min(Table - 1, (int)u);
            return _visc[t] + (_visc[t + 1] - _visc[t]) * (u - t);
        }

        public string Id { get; }
        public LiquidKind Kind { get; }

        /// <summary>Particle spacing at rest (m).</summary>
        public double Spacing { get; }

        /// <summary>Contact radius of a particle against solids (half the spacing).</summary>
        public double Radius { get; }

        /// <summary>Mass of one particle (kg).</summary>
        public double Mass { get; }

        public double LatticeSum { get; }
        public int Count { get; private set; }

        /// <summary>Solver iterations per substep (more = stiffer, less compression).</summary>
        public int Iterations { get; set; } = 4;

        /// <summary>Bodies the liquid passes through (propeller discs: the disc is not a solid).</summary>
        public HashSet<SceneBody>? Ignore { get; set; }

        /// <summary>Largest compression ρ/ρ0 − 1 after the last substep.</summary>
        public double MaxCompression { get; private set; }

        /// <summary>Mean density of the particles with a full neighbourhood after the last substep (kg/m³).</summary>
        public double MeanInteriorDensity { get; private set; }

        public double TotalMass => Count * Mass;

        /// <summary>Volume at rest density (m³).</summary>
        public double Volume => TotalMass / Kind.Density;

        /// <summary>Substeps the last <see cref="Step"/> used.</summary>
        public int LastSubsteps { get; private set; }

        /// <summary>The liquid stood still and did not need stepping (bodies around it at rest too).</summary>
        public bool Asleep { get; private set; }

        private readonly Dictionary<SceneBody, Vec3> _forces = new Dictionary<SceneBody, Vec3>();
        private readonly Dictionary<SceneBody, Vec3> _torques = new Dictionary<SceneBody, Vec3>();

        /// <summary>Force (N) the liquid puts on each body it touches, averaged over its last step.</summary>
        public IReadOnlyDictionary<SceneBody, Vec3> LastForces => _forces;

        public Vec3 Position(int i) => new Vec3(_x[3 * i], _x[3 * i + 1], _x[3 * i + 2]);
        public Vec3 Velocity(int i) => new Vec3(_v[3 * i], _v[3 * i + 1], _v[3 * i + 2]);

        /// <summary>All positions as x, y, z floats (for drawing and recording).</summary>
        public float[] Snapshot()
        {
            var a = new float[3 * Count];
            for (int i = 0; i < a.Length; i++) a[i] = (float)_x[i];
            return a;
        }

        /// <summary>All velocities as vx, vy, vz floats (for drawing).</summary>
        public float[] VelocitySnapshot()
        {
            var a = new float[3 * Count];
            for (int i = 0; i < a.Length; i++) a[i] = (float)_v[i];
            return a;
        }

        public (Vec3 Lo, Vec3 Hi) Bounds()
        {
            if (Count == 0) return (Vec3.Zero, Vec3.Zero);
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            for (int i = 0; i < Count; i++)
            {
                x0 = Math.Min(x0, _x[3 * i]);
                x1 = Math.Max(x1, _x[3 * i]);
                y0 = Math.Min(y0, _x[3 * i + 1]);
                y1 = Math.Max(y1, _x[3 * i + 1]);
                z0 = Math.Min(z0, _x[3 * i + 2]);
                z1 = Math.Max(z1, _x[3 * i + 2]);
            }

            return (new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
        }

        public double KineticEnergy()
        {
            double e = 0;
            for (int i = 0; i < 3 * Count; i++) e += _v[i] * _v[i];
            return 0.5 * Mass * e;
        }

        public Vec3 CentreOfMass()
        {
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < Count; i++)
            {
                x += _x[3 * i];
                y += _x[3 * i + 1];
                z += _x[3 * i + 2];
            }

            return Count == 0 ? Vec3.Zero : new Vec3(x, y, z) / Count;
        }

        /// <summary>Lattice points at this spacing inside a region (world frame), to fill a volume at rest density.</summary>
        public static List<Vec3> Lattice(Vec3 lo, Vec3 hi, double spacing, Func<Vec3, bool> inside)
        {
            var list = new List<Vec3>();
            int nx = (int)Math.Floor((hi.X - lo.X) / spacing), ny = (int)Math.Floor((hi.Y - lo.Y) / spacing), nz = (int)Math.Floor((hi.Z - lo.Z) / spacing);
            // Centred in the region so the layer against each face is half a spacing from it.
            var o = lo + new Vec3((hi.X - lo.X - nx * spacing) / 2, (hi.Y - lo.Y - ny * spacing) / 2, (hi.Z - lo.Z - nz * spacing) / 2);
            for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                var p = o + new Vec3((i + 0.5) * spacing, (j + 0.5) * spacing, (k + 0.5) * spacing);
                if (inside(p)) list.Add(p);
            }

            return list;
        }

        public void Add(IReadOnlyList<Vec3> points, Vec3 velocity = default)
        {
            int n0 = Count, n = n0 + points.Count;
            Grow(n);
            for (int i = 0; i < points.Count; i++)
            {
                int k = 3 * (n0 + i);
                _x[k] = points[i].X;
                _x[k + 1] = points[i].Y;
                _x[k + 2] = points[i].Z;
                _v[k] = velocity.X;
                _v[k + 1] = velocity.Y;
                _v[k + 2] = velocity.Z;
            }

            Count = n;
            Asleep = false;
            _still = 0;
        }

        private void Grow(int n)
        {
            if (_x.Length >= 3 * n) return;
            int cap = Math.Max(n, 2 * Count);
            Array.Resize(ref _x, 3 * cap);
            Array.Resize(ref _v, 3 * cap);
            _p = new double[3 * cap];
            _dp = new double[3 * cap];
            _c = new double[3 * cap];
            _rho = new double[cap];
            _lam = new double[cap];
            _nb = new int[cap * MaxNeighbours];
            _nbCount = new int[cap];
            _wb = new int[cap * Slots];
            _wd = new double[cap * Slots];
            _wn = new double[3 * cap * Slots];
            _wv = new double[3 * cap * Slots];
            _imp = new double[3 * cap * Slots];
            _cell = new int[cap];
            _sorted = new int[cap];
        }

        // ------------------------------------------------------------------ kernel

        private double W(double r)
        {
            double q = r / _l;
            if (q >= 2) return 0;
            if (q < 1) return _sigma * (1 - 1.5 * q * q + 0.75 * q * q * q);
            double t = 2 - q;
            return _sigma * 0.25 * t * t * t;
        }

        /// <summary>dW/dr.</summary>
        private double Wd(double r)
        {
            double q = r / _l;
            if (q >= 2) return 0;
            if (q < 1) return _sigma / _l * (-3 * q + 2.25 * q * q);
            double t = 2 - q;
            return -_sigma / _l * 0.75 * t * t;
        }

        /// <summary>Share of the kernel beyond a flat wall at distance d, and its rate of change (−A).</summary>
        private void WallKernel(double d, out double phi, out double area)
        {
            if (d >= _h)
            {
                phi = area = 0;
                return;
            }

            double u = Math.Max(0, d) / _h * Table;
            int t = Math.Min(Table - 1, (int)u);
            double f = u - t;
            phi = _phi[t] + (_phi[t + 1] - _phi[t]) * f;
            area = _area[t] + (_area[t + 1] - _area[t]) * f;
            if (d < 0)
            {
                // Inside the wall (only transiently): keep growing so the particle is pushed out.
                phi = _phi[0] + _area[0] * -d;
                area = _area[0];
            }
        }

        // ------------------------------------------------------------------ step

        /// <summary>Longest step (s) the liquid takes on its own clock (it is stepped less often than the bodies when slow).</summary>
        public double MaxStep { get; set; } = 3e-3;

        /// <summary>A liquid at rest in resting containers stops being stepped until something moves (live view only).</summary>
        public bool SleepEnabled { get; set; } = true;

        /// <summary>
        /// The step the liquid wants next: no particle may move more than 0.3 spacing in a substep, and with at most
        /// four substeps per step a slow liquid is stepped every <see cref="MaxStep"/> instead of every physics step.
        /// </summary>
        public double SuggestedStep { get; private set; } = 1e-3;

        /// <summary>
        /// Advances the liquid by <paramref name="dt"/> against the scene as it is now (bodies already stepped). Its push on
        /// each body over the step is kept as an average force (<see cref="LastForces"/>); <see cref="ApplyForces"/> applies
        /// it on every physics step until the next liquid step.
        /// </summary>
        public void Step(PhysicsScene scene, double dt)
        {
            if (Count == 0 || dt <= 0) return;
            Gather(scene, dt);
            double vmax = 0;
            for (int i = 0; i < Count; i++)
            {
                double v2 = _v[3 * i] * _v[3 * i] + _v[3 * i + 1] * _v[3 * i + 1] + _v[3 * i + 2] * _v[3 * i + 2];
                if (v2 > vmax) vmax = v2;
            }

            vmax = Math.Sqrt(vmax);
            double bodyMove = 0;
            foreach (var b in _near)
            {
                if (b.IsStatic) continue;
                bodyMove = Math.Max(bodyMove, b.Velocity.Length + b.AngularVelocity.Length * (b.EffectiveShape?.BoundingRadius ?? 0));
                if (_poses.TryGetValue(b, out var pose) && ((pose.X - b.Position).LengthSquared > 1e-14 || Math.Abs(pose.Q.W * b.Orientation.W + pose.Q.X * b.Orientation.X + pose.Q.Y * b.Orientation.Y + pose.Q.Z * b.Orientation.Z) < 1 - 1e-12))
                    bodyMove = Math.Max(bodyMove, 1e-3);
            }

            // At rest in a resting container: nothing changes until something moves (the solver ran 50 steps to settle,
            // and the container keeps carrying the liquid's last push while it sleeps).
            if (SleepEnabled && vmax < 1.5e-3 && bodyMove < 2e-4 && ++_still > 50)
            {
                Asleep = true;
                SuggestedStep = MaxStep;
                return;
            }

            if (vmax >= 1.5e-3 || bodyMove >= 2e-4) _still = 0;
            Asleep = false;
            _forces.Clear();
            _torques.Clear();
            double speed = vmax + bodyMove;
            int sub = Math.Max(1, Math.Min(16, (int)Math.Ceiling(speed * dt / (0.3 * Spacing))));
            LastSubsteps = sub;
            double h = dt / sub;
            for (int k = 0; k < sub; k++)
            {
                for (int b = 0; b < _near.Count; b++)
                {
                    var body = _near[b];
                    _start[b] = k == 0 && _poses.TryGetValue(body, out var pose) ? pose : (body.Position, body.Orientation);
                }

                Substep(scene, h, dt);
            }

            foreach (var b in _near) _poses[b] = (b.Position, b.Orientation);
            double cfl = 0.3 * Spacing / Math.Max(speed, 1e-9);
            SuggestedStep = Math.Max(1e-4, Math.Min(MaxStep, 4 * cfl));
        }

        /// <summary>Applies the liquid's last average push to each body it touched (call once per physics step).</summary>
        public void ApplyForces()
        {
            foreach (var kv in _forces)
            {
                if (kv.Key.IsStatic) continue;
                kv.Key.AddForce(kv.Value);
                if (_torques.TryGetValue(kv.Key, out var t)) kv.Key.AddTorque(t);
            }
        }

        private void Gather(PhysicsScene scene, double dt)
        {
            var (lo, hi) = Bounds();
            double pad = _h + 0.02;
            lo -= new Vec3(pad, pad, pad);
            hi += new Vec3(pad, pad, pad);
            var before = new List<SceneBody>(_near);
            _near.Clear();
            foreach (var b in scene.Bodies)
            {
                if (!b.Collides || (Ignore != null && Ignore.Contains(b))) continue;
                var s = b.EffectiveShape;
                if (s == null || !s.HasSolid) continue;
                double r = s.BoundingRadius + b.Velocity.Length * dt;
                var c = b.Position;
                if (s.Kind != ShapeKind.Mesh && (c.X + r < lo.X || c.X - r > hi.X || c.Y + r < lo.Y || c.Y - r > hi.Y || c.Z + r < lo.Z || c.Z - r > hi.Z)) continue;
                _near.Add(b);
            }

            foreach (var b in _near) FieldOf(b);   // built here, read in parallel below
            if (_start.Length < _near.Count) _start = new (Vec3, Quat)[_near.Count];
            if (before.Count != _near.Count) _still = 0;
        }

        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        private readonly double[] _phase = new double[6];

        /// <summary>Wall-clock seconds spent per phase since the start: walls, neighbours, pressure, viscosity, surface tension, rest.</summary>
        public IReadOnlyList<double> PhaseSeconds => _phase;

        private void Lap(int phase)
        {
            _phase[phase] += _clock.Elapsed.TotalSeconds;
            _clock.Restart();
        }

        private void Substep(PhysicsScene scene, double h, double stepDt)
        {
            _clock.Restart();
            int n = Count;
            var g = scene.Gravity;
            if (++_substeps % 16 == 1) Reorder(scene);

            // 1. Predict.
            Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    int k = 3 * i;
                    _v[k] += g.X * h;
                    _v[k + 1] += g.Y * h;
                    _v[k + 2] += g.Z * h;
                    _p[k] = _x[k] + _v[k] * h;
                    _p[k + 1] = _x[k + 1] + _v[k + 1] * h;
                    _p[k + 2] = _x[k + 2] + _v[k + 2] * h;
                }
            });

            Array.Clear(_imp, 0, 3 * n * Slots);
            // 2. Walls: no crossing within the step, the walls within reach recorded for the pressure.
            Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++) Walls(scene, i, h, true);
            });

            Lap(0);
            // 3. Neighbours.
            Neighbours(n);
            Lap(1);

            // 4. Incompressibility.
            double rho0 = Kind.Density, m = Mass, eps = 1e-2 / (Spacing * Spacing);
            double wallScale = m / (_s3 * rho0);
            double w0 = W(0);
            double maxC = 0;
            for (int it = 0; it < Iterations; it++)
            {
                bool last = it == Iterations - 1;
                Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        int k = 3 * i;
                        double px = _p[k], py = _p[k + 1], pz = _p[k + 2];
                        double sum = w0, gx = 0, gy = 0, gz = 0, g2 = 0;
                        int cnt = _nbCount[i], b0 = i * MaxNeighbours;
                        for (int a = 0; a < cnt; a++)
                        {
                            int j = _nb[b0 + a], kj = 3 * j;
                            double dx = px - _p[kj], dy = py - _p[kj + 1], dz = pz - _p[kj + 2];
                            double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (r >= _h) continue;
                            sum += W(r);
                            if (r < 1e-12) continue;
                            double f = Wd(r) / r * m / rho0;
                            double ex = f * dx, ey = f * dy, ez = f * dz;
                            gx += ex;
                            gy += ey;
                            gz += ez;
                            g2 += ex * ex + ey * ey + ez * ez;
                        }

                        double rho = m * sum;
                        for (int s = 0; s < Slots; s++)
                        {
                            int ws = i * Slots + s;
                            if (_wb[ws] == -1) continue;
                            WallKernel(_wd[ws], out double phi, out double area);
                            rho += m / _s3 * phi;
                            gx -= wallScale * area * _wn[3 * ws];
                            gy -= wallScale * area * _wn[3 * ws + 1];
                            gz -= wallScale * area * _wn[3 * ws + 2];
                        }

                        _rho[i] = rho;
                        double c = Math.Max(0, rho / rho0 - 1);
                        _lam[i] = -c / (g2 + gx * gx + gy * gy + gz * gz + eps);
                    }
                });

                Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        int k = 3 * i;
                        double px = _p[k], py = _p[k + 1], pz = _p[k + 2], li = _lam[i];
                        double ux = 0, uy = 0, uz = 0;
                        int cnt = _nbCount[i], b0 = i * MaxNeighbours;
                        for (int a = 0; a < cnt; a++)
                        {
                            int j = _nb[b0 + a], kj = 3 * j;
                            double dx = px - _p[kj], dy = py - _p[kj + 1], dz = pz - _p[kj + 2];
                            double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (r >= _h || r < 1e-12) continue;
                            double f = (li + _lam[j]) * Wd(r) / r * m / rho0;
                            ux += f * dx;
                            uy += f * dy;
                            uz += f * dz;
                        }

                        for (int s = 0; s < Slots; s++)
                        {
                            int ws = i * Slots + s;
                            if (_wb[ws] == -1 || li == 0) continue;
                            WallKernel(_wd[ws], out _, out double area);
                            double push = -li * wallScale * area;   // along the wall normal, away from the wall
                            ux += push * _wn[3 * ws];
                            uy += push * _wn[3 * ws + 1];
                            uz += push * _wn[3 * ws + 2];
                            _imp[3 * ws] += push * _wn[3 * ws];
                            _imp[3 * ws + 1] += push * _wn[3 * ws + 1];
                            _imp[3 * ws + 2] += push * _wn[3 * ws + 2];
                        }

                        _dp[k] = ux;
                        _dp[k + 1] = uy;
                        _dp[k + 2] = uz;
                    }
                });

                Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        int k = 3 * i;
                        _p[k] += _dp[k];
                        _p[k + 1] += _dp[k + 1];
                        _p[k + 2] += _dp[k + 2];
                        // The walls moved relative to the particle by its correction; keep it off them.
                        for (int s = 0; s < Slots; s++)
                        {
                            int ws = i * Slots + s;
                            if (_wb[ws] == -1) continue;
                            double nx = _wn[3 * ws], ny = _wn[3 * ws + 1], nz = _wn[3 * ws + 2];
                            double d = _wd[ws] + nx * _dp[k] + ny * _dp[k + 1] + nz * _dp[k + 2];
                            if (d < Radius)
                            {
                                double push = Radius - d;
                                _p[k] += nx * push;
                                _p[k + 1] += ny * push;
                                _p[k + 2] += nz * push;
                                _imp[3 * ws] += nx * push;
                                _imp[3 * ws + 1] += ny * push;
                                _imp[3 * ws + 2] += nz * push;
                                d = Radius;
                            }

                            _wd[ws] = d;
                        }
                    }
                });
            }

            Lap(2);
            // 5. Exact non-penetration against the true surfaces (the iterations tracked them to first order).
            Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++) Walls(scene, i, h, false);
            });

            // 6. Velocities, then the real viscosity and surface tension.
            Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    int k = 3 * i;
                    _v[k] = (_p[k] - _x[k]) / h;
                    _v[k + 1] = (_p[k + 1] - _x[k + 1]) / h;
                    _v[k + 2] = (_p[k + 2] - _x[k + 2]) / h;
                }
            });

            Lap(0);
            Viscosity(h);
            Lap(3);
            SurfaceTension(h);
            Lap(4);
            double maxComp = 0, sumRho = 0;
            int interior = 0;
            for (int i = 0; i < n; i++)
            {
                maxComp = Math.Max(maxComp, _rho[i] / rho0 - 1);
                if (_nbCount[i] >= 26)
                {
                    sumRho += _rho[i];
                    interior++;
                }
            }

            MaxCompression = maxComp;
            MeanInteriorDensity = interior > 0 ? sumRho / interior : rho0;
            Array.Copy(_p, _x, 3 * n);
            Couple(h, stepDt);
            Cull(scene);
            Lap(5);
        }

        private static int Chunk(int n) => Math.Max(64, n / (4 * Environment.ProcessorCount) + 1);

        /// <summary>
        /// The walls near particle i: ground, and the nearest two bodies within the kernel's reach, each with its distance,
        /// outward normal and surface velocity. With <paramref name="sweep"/>, the move from the last position may not cross
        /// a solid (continuous collision in the body's frame).
        /// </summary>
        private void Walls(PhysicsScene scene, int i, double h, bool sweep)
        {
            int k = 3 * i;
            var p = new Vec3(_p[k], _p[k + 1], _p[k + 2]);
            int s0 = i * Slots;
            if (sweep)
                for (int s = 0; s < Slots; s++) _wb[s0 + s] = -1;
            double reach = _h;
            // Ground.
            if (scene.GroundEnabled)
            {
                double gz = scene.GroundHeight(p.X, p.Y);
                var gn = scene.GroundNormal(p.X, p.Y);
                double d = (p.Z - gz) * gn.Z;
                if (d < Radius)
                {
                    double push = Radius - d;
                    p += gn * push;
                    d = Radius;
                }

                if (sweep && d < reach) SetSlot(s0, -2, d, gn, Vec3.Zero);
            }

            var from = new Vec3(_x[k], _x[k + 1], _x[k + 2]);
            double moved = (p - from).Length;
            for (int b = 0; b < _near.Count; b++)
            {
                var body = _near[b];
                var shape = body.EffectiveShape!;
                // Far from this body (its bounding sphere, plus the kernel's reach and this move): nothing to do.
                if (shape.Kind != ShapeKind.Mesh && body.Pieces == null)
                {
                    double rr = shape.BoundingRadius + reach + moved;
                    if ((p - body.Position).LengthSquared > rr * rr) continue;
                }
                else if (FieldOf(body) is { } fb)
                {
                    var lp = body.Orientation.InverseRotate(p - body.Position);
                    double m = reach + moved;
                    if (lp.X < fb.Lo.X - m || lp.Y < fb.Lo.Y - m || lp.Z < fb.Lo.Z - m
                        || lp.X > fb.Lo.X + (fb.Nx - 1) * fb.Cell + m || lp.Y > fb.Lo.Y + (fb.Ny - 1) * fb.Cell + m || lp.Z > fb.Lo.Z + (fb.Nz - 1) * fb.Cell + m)
                        continue;
                }

                var push = Vec3.Zero;
                if (sweep)
                {
                    // Continuous: from where it was (in the body as it stood then) to where it would go (in the body now).
                    var (sx, sq) = _start[b];
                    var ls = sq.InverseRotate(new Vec3(_x[k], _x[k + 1], _x[k + 2]) - sx);
                    var le = body.Orientation.InverseRotate(p - body.Position);
                    var dl = le - ls;
                    double len = dl.Length;
                    // A solid can only be crossed when the clearances at both ends together are shorter than the move (the
                    // distance changes by at most the distance moved): most particles skip the exact ray.
                    bool maybe = len > 1e-9 && (shape.Kind == ShapeKind.Mesh || ClosestOnSegment(ls, dl, len) < shape.BoundingRadius + Radius);
                    if (maybe)
                    {
                        double slack = 2 * Radius + 4 * FieldCell(body);
                        double ds = Distance(body, shape, ls, double.MaxValue, out double dsv, out _) ? dsv : double.MaxValue;
                        double de = Distance(body, shape, le, double.MaxValue, out double dev, out _) ? dev : double.MaxValue;
                        // Started inside: the push-out below frees it by the nearest face instead.
                        maybe = ds >= 0 && ds + de <= len + slack;
                    }

                    if (maybe)
                    {
                        var dir = dl / len;
                        if (body.RaycastSolid(shape, ls, dir, len, out double t, out var nl))
                        {
                            var hit = ls + dir * t;
                            var stopped = le + nl * ((hit - le).Dot(nl) + Radius);
                            var np = body.Position + body.Orientation.Rotate(stopped);
                            push += np - p;
                            p = np;
                        }
                    }
                }

                double dist = double.PositiveInfinity;
                var nw = Vec3.UnitZ;
                bool near = shape.Kind == ShapeKind.Mesh || (p - body.Position).LengthSquared <= (shape.BoundingRadius + reach) * (shape.BoundingRadius + reach);
                if (near)
                {
                    var local = body.Orientation.InverseRotate(p - body.Position);
                    if (Distance(body, shape, local, reach, out double sd, out var nlocal) && sd < reach)
                    {
                        dist = sd;
                        nw = body.Orientation.Rotate(nlocal);
                        if (dist < Radius)
                        {
                            var np = p + nw * (Radius - dist);
                            push += np - p;
                            p = np;
                            dist = Radius;
                        }
                    }
                }

                if (dist >= reach && push.LengthSquared == 0) continue;
                if (!sweep)
                {
                    BookPush(s0, b, push);
                    continue;
                }

                // The two nearest bodies keep a slot (pressure, viscosity and the reaction of their pushes).
                int slot = -1;
                if (_wb[s0 + 1] == -1) slot = s0 + 1;
                else if (_wb[s0 + 2] == -1) slot = s0 + 2;
                else
                {
                    int far = _wd[s0 + 1] > _wd[s0 + 2] ? s0 + 1 : s0 + 2;
                    if (Math.Min(dist, reach) < _wd[far]) slot = far;
                }

                if (slot < 0) continue;
                var surface = body.IsStatic || dist >= reach ? Vec3.Zero : body.PointVelocity(p - nw * dist);
                SetSlot(slot, b, Math.Min(dist, reach), nw, surface);
                _imp[3 * slot] = push.X;
                _imp[3 * slot + 1] = push.Y;
                _imp[3 * slot + 2] = push.Z;
            }

            _p[k] = p.X;
            _p[k + 1] = p.Y;
            _p[k + 2] = p.Z;
        }

        private static double ClosestOnSegment(Vec3 a, Vec3 d, double len)
        {
            double f = Math.Max(0, Math.Min(1, -a.Dot(d) / (len * len)));
            return (a + d * f).Length;
        }

        private void SetSlot(int ws, int body, double d, Vec3 n, Vec3 v)
        {
            _wb[ws] = body;
            _wd[ws] = d;
            _wn[3 * ws] = n.X;
            _wn[3 * ws + 1] = n.Y;
            _wn[3 * ws + 2] = n.Z;
            _wv[3 * ws] = v.X;
            _wv[3 * ws + 1] = v.Y;
            _wv[3 * ws + 2] = v.Z;
        }

        /// <summary>A push from body b in the final contact pass: booked on that body's slot (or a free one) for the reaction.</summary>
        private void BookPush(int s0, int b, Vec3 push)
        {
            if (push.LengthSquared == 0) return;
            for (int s = 1; s < Slots; s++)
            {
                int ws = s0 + s;
                if (_wb[ws] != b && _wb[ws] != -1) continue;
                if (_wb[ws] == -1)
                {
                    _wb[ws] = b;
                    _wd[ws] = _h;   // no pressure from it: it was not within reach when the pressure was solved
                }

                _imp[3 * ws] += push.X;
                _imp[3 * ws + 1] += push.Y;
                _imp[3 * ws + 2] += push.Z;
                return;
            }
        }

        /// <summary>
        /// [phys] Newton's third law: every push a wall gave a particle (pressure and contact) pushes the body back. Summed
        /// over the substeps as an average force and moment over the liquid's step.
        /// </summary>
        private void Couple(double h, double stepDt)
        {
            if (_near.Count == 0) return;
            int nb = _near.Count;
            var force = new Vec3[nb];
            var moment = new Vec3[nb];
            var touched = new bool[nb];
            double scale = Mass / (h * stepDt);
            for (int i = 0; i < Count; i++)
            {
                for (int s = 1; s < Slots; s++)
                {
                    int ws = i * Slots + s, b = _wb[ws];
                    if (b < 0) continue;
                    var imp = new Vec3(_imp[3 * ws], _imp[3 * ws + 1], _imp[3 * ws + 2]);
                    if (imp.LengthSquared == 0) continue;
                    var f = imp * -scale;
                    var at = new Vec3(_x[3 * i], _x[3 * i + 1], _x[3 * i + 2]);
                    force[b] += f;
                    moment[b] += (at - _near[b].Position).Cross(f);
                    touched[b] = true;
                }
            }

            for (int b = 0; b < nb; b++)
            {
                if (!touched[b]) continue;
                var body = _near[b];
                _forces.TryGetValue(body, out var f0);
                _forces[body] = f0 + force[b];
                _torques.TryGetValue(body, out var t0);
                _torques[body] = t0 + moment[b];
            }
        }

        // ------------------------------------------------------------------ neighbours

        private int[] _visit = new int[27];

        private void Neighbours(int n)
        {
            int size = 1;
            while (size < 2 * n) size <<= 1;
            if (_cellStart.Length < size + 1) _cellStart = new int[size + 1];
            Array.Clear(_cellStart, 0, size + 1);
            double inv = 1 / _h;
            int mask = size - 1;
            for (int i = 0; i < n; i++)
            {
                int c = Hash((int)Math.Floor(_p[3 * i] * inv), (int)Math.Floor(_p[3 * i + 1] * inv), (int)Math.Floor(_p[3 * i + 2] * inv)) & mask;
                _cell[i] = c;
                _cellStart[c + 1]++;
            }

            for (int c = 0; c < size; c++) _cellStart[c + 1] += _cellStart[c];
            var fill = new int[size];
            for (int i = 0; i < n; i++)
            {
                int c = _cell[i];
                _sorted[_cellStart[c] + fill[c]++] = i;
            }

            double h2 = _h * _h;
            Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
            {
                Span<int> seen = stackalloc int[27];
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    int k = 3 * i, cnt = 0, b0 = i * MaxNeighbours;
                    double px = _p[k], py = _p[k + 1], pz = _p[k + 2];
                    int cx = (int)Math.Floor(px * inv), cy = (int)Math.Floor(py * inv), cz = (int)Math.Floor(pz * inv);
                    int nseen = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int c = Hash(cx + dx, cy + dy, cz + dz) & mask;
                        bool dup = false;
                        for (int q = 0; q < nseen; q++)
                            if (seen[q] == c)
                            {
                                dup = true;
                                break;
                            }

                        if (dup) continue;
                        seen[nseen++] = c;
                        for (int a = _cellStart[c]; a < _cellStart[c + 1]; a++)
                        {
                            int j = _sorted[a];
                            if (j == i) continue;
                            int kj = 3 * j;
                            double ex = px - _p[kj], ey = py - _p[kj + 1], ez = pz - _p[kj + 2];
                            if (ex * ex + ey * ey + ez * ez >= h2 || cnt >= MaxNeighbours) continue;
                            _nb[b0 + cnt++] = j;
                        }
                    }

                    _nbCount[i] = cnt;
                }
            });
        }

        private static int Hash(int x, int y, int z) => (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);

        /// <summary>Puts particles that are close in space close in memory (every 16 substeps), for the caches.</summary>
        private void Reorder(PhysicsScene scene)
        {
            int n = Count;
            if (n < 2) return;
            var key = new long[n];
            var idx = new int[n];
            double inv = 1 / _h;
            for (int i = 0; i < n; i++)
            {
                long cx = (long)Math.Floor(_x[3 * i] * inv) & 0x1FFFFF, cy = (long)Math.Floor(_x[3 * i + 1] * inv) & 0x1FFFFF, cz = (long)Math.Floor(_x[3 * i + 2] * inv) & 0x1FFFFF;
                key[i] = (cz << 42) | (cy << 21) | cx;
                idx[i] = i;
            }

            Array.Sort(key, idx);
            var x = new double[_x.Length];
            var v = new double[_v.Length];
            for (int a = 0; a < n; a++)
            {
                int i = idx[a];
                for (int c = 0; c < 3; c++)
                {
                    x[3 * a + c] = _x[3 * i + c];
                    v[3 * a + c] = _v[3 * i + c];
                }
            }

            _x = x;
            _v = v;
        }

        // ------------------------------------------------------------------ viscosity, surface tension

        /// <summary>
        /// The real viscosity: Morris' SPH Laplacian between neighbours and a no-slip wall moving with each body, taken
        /// implicitly (one Jacobi sweep of v = v* + h·ν∇²v) so even honey (ν = 7·10⁻³ m²/s) is stable at this step.
        /// </summary>
        private void Viscosity(double h)
        {
            int n = Count;
            double nu = Kind.KinematicViscosity, m = Mass, eta2 = 0.01 * _l * _l;
            if (nu <= 0) return;
            var src = (double[])_v.Clone();
            Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    int k = 3 * i;
                    double px = _p[k], py = _p[k + 1], pz = _p[k + 2], rhoi = Math.Max(_rho[i], 1e-9);
                    double diag = 0, sx = 0, sy = 0, sz = 0;
                    int cnt = _nbCount[i], b0 = i * MaxNeighbours;
                    for (int a = 0; a < cnt; a++)
                    {
                        int j = _nb[b0 + a], kj = 3 * j;
                        double dx = px - _p[kj], dy = py - _p[kj + 1], dz = pz - _p[kj + 2];
                        double r2 = dx * dx + dy * dy + dz * dz, r = Math.Sqrt(r2);
                        if (r >= _h || r < 1e-12) continue;
                        // c_ij = 2ν m/ρ_j · (r·∇W)/(r² + η²) is negative; the term is −c_ij (v_i − v_j)… written as a weight.
                        double c = -2 * nu * m / Math.Max(_rho[j], 1e-9) * (Wd(r) * r) / (r2 + eta2);
                        diag += c;
                        sx += c * src[kj];
                        sy += c * src[kj + 1];
                        sz += c * src[kj + 2];
                    }

                    for (int s = 0; s < Slots; s++)
                    {
                        int ws = i * Slots + s;
                        if (_wb[ws] == -1) continue;
                        // The wall as liquid moving with it: the same Laplacian summed over the half-space beyond it,
                        // ∫ −W'(r)/r dV = 2π ∫_d^h W(z) dz.
                        double c = 2 * nu * WallViscous(_wd[ws]) / (LatticeSum * _s3) * (Kind.Density / rhoi);
                        diag += c;
                        sx += c * _wv[3 * ws];
                        sy += c * _wv[3 * ws + 1];
                        sz += c * _wv[3 * ws + 2];
                    }

                    if (diag <= 0) continue;
                    double inv = 1 / (1 + h * diag);
                    _v[k] = (src[k] + h * sx) * inv;
                    _v[k + 1] = (src[k + 1] + h * sy) * inv;
                    _v[k + 2] = (src[k + 2] + h * sz) * inv;
                }
            });
        }

        /// <summary>
        /// The real surface tension as a continuum surface force: f = σ κ ∇c, with the colour gradient ∇c (non-zero only at
        /// the surface) and the curvature κ = −∇·n̂ from the neighbours.
        /// </summary>
        private void SurfaceTension(double h)
        {
            int n = Count;
            double sigma = Kind.SurfaceTension, m = Mass;
            if (sigma <= 0) return;
            Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    int k = 3 * i;
                    double px = _p[k], py = _p[k + 1], pz = _p[k + 2];
                    double cx = 0, cy = 0, cz = 0;
                    int cnt = _nbCount[i], b0 = i * MaxNeighbours;
                    for (int a = 0; a < cnt; a++)
                    {
                        int j = _nb[b0 + a], kj = 3 * j;
                        double dx = px - _p[kj], dy = py - _p[kj + 1], dz = pz - _p[kj + 2];
                        double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (r >= _h || r < 1e-12) continue;
                        double f = m / Math.Max(_rho[j], 1e-9) * Wd(r) / r;
                        cx += f * dx;
                        cy += f * dy;
                        cz += f * dz;
                    }

                    // The walls are liquid-filled for the colour field too: no false surface against a glass.
                    for (int s = 0; s < Slots; s++)
                    {
                        int ws = i * Slots + s;
                        if (_wb[ws] == -1) continue;
                        WallKernel(_wd[ws], out _, out double area);
                        double f = area / (LatticeSum * _s3);
                        cx -= f * _wn[3 * ws];
                        cy -= f * _wn[3 * ws + 1];
                        cz -= f * _wn[3 * ws + 2];
                    }

                    // ∇c points into the liquid (c = 1 inside); the sum above is −∇c.
                    _c[k] = -cx;
                    _c[k + 1] = -cy;
                    _c[k + 2] = -cz;
                }
            });

            double threshold = 0.25 / _l;
            Parallel.ForEach(Partitioner.Create(0, n, Chunk(n)), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    int k = 3 * i;
                    double ci = Math.Sqrt(_c[k] * _c[k] + _c[k + 1] * _c[k + 1] + _c[k + 2] * _c[k + 2]);
                    if (ci < threshold) continue;
                    double nix = _c[k] / ci, niy = _c[k + 1] / ci, niz = _c[k + 2] / ci;
                    double px = _p[k], py = _p[k + 1], pz = _p[k + 2], kappa = 0;
                    int cnt = _nbCount[i], b0 = i * MaxNeighbours;
                    for (int a = 0; a < cnt; a++)
                    {
                        int j = _nb[b0 + a], kj = 3 * j;
                        double cj = Math.Sqrt(_c[kj] * _c[kj] + _c[kj + 1] * _c[kj + 1] + _c[kj + 2] * _c[kj + 2]);
                        if (cj < threshold) continue;
                        double dx = px - _p[kj], dy = py - _p[kj + 1], dz = pz - _p[kj + 2];
                        double r = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (r >= _h || r < 1e-12) continue;
                        double f = m / Math.Max(_rho[j], 1e-9) * Wd(r) / r;
                        // With m̂ = ∇c/|∇c| (pointing into the liquid), κ = −∇·m̂ ≈ −Σ V_j (m̂_j − m̂_i)·∇W_ij: +2/R on a
                        // drop of radius R, so σκ∇c pulls the surface in.
                        kappa -= f * ((_c[kj] / cj - nix) * dx + (_c[kj + 1] / cj - niy) * dy + (_c[kj + 2] / cj - niz) * dz);
                    }

                    double a2 = sigma * kappa / Math.Max(_rho[i], 1e-9);
                    _v[k] += h * a2 * _c[k];
                    _v[k + 1] += h * a2 * _c[k + 1];
                    _v[k + 2] += h * a2 * _c[k + 2];
                }
            });
        }

        /// <summary>Particles that fell far below the ground (off the world) are dropped.</summary>
        private void Cull(PhysicsScene scene)
        {
            int n = Count, w = 0;
            for (int i = 0; i < n; i++)
            {
                if (_x[3 * i + 2] < -20) continue;
                if (w != i)
                    for (int c = 0; c < 3; c++)
                    {
                        _x[3 * w + c] = _x[3 * i + c];
                        _v[3 * w + c] = _v[3 * i + c];
                    }

                w++;
            }

            Count = w;
        }
    }
}
