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
        private int[] _id = Array.Empty<int>();   // each particle's lasting number (memory order changes, this does not)
        private double[] _rho = Array.Empty<double>(), _lam = Array.Empty<double>(), _q = Array.Empty<double>(), _vsrc = Array.Empty<double>();
        private int[] _nb = Array.Empty<int>(), _nbCount = Array.Empty<int>();
        private double[] _nbw = Array.Empty<double>();   // per neighbour pair: W'(r)/r · m/ρ0 at this iteration (0 = out of reach)
        private int[] _wb = Array.Empty<int>();   // body slot (index into _near), -2 = ground, -1 = none
        private double[] _wd = Array.Empty<double>(), _wn = Array.Empty<double>(), _wv = Array.Empty<double>();
        private double[] _wphi = Array.Empty<double>(), _wg = Array.Empty<double>();   // wall's kernel share Φ and ∇Φ (world)
        private bool[] _wfield = Array.Empty<bool>();   // Φ from the body's precomputed density map (else a flat face)
        private double[] _wangle = Array.Empty<double>();   // the liquid's contact angle on that wall (degrees)
        private double[] _imp = Array.Empty<double>();   // positional push each slot gave its wall this substep (m)
        private int[] _cell = Array.Empty<int>(), _cellStart = Array.Empty<int>(), _sorted = Array.Empty<int>();
        private readonly List<SceneBody> _near = new List<SceneBody>();
        private readonly Dictionary<SceneBody, (Vec3 X, Quat Q)> _poses = new Dictionary<SceneBody, (Vec3, Quat)>();
        private (Vec3 X, Quat Q)[] _start = Array.Empty<(Vec3, Quat)>();
        private int _substeps;

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

            // Wall adhesion profile: G(z) = ∫_z^h sin(π r/h) dr = (h/π)(1 + cos(π z/h)); pull(d) = 2π ∫_d^h z G(z) dz.
            for (int t = Table; t >= 0; t--)
            {
                double z = _h * t / Table, z1 = _h * (t + 1) / Table;
                double g0 = z * _h / Math.PI * (1 + Math.Cos(Math.PI * z / _h)), g1 = z1 * _h / Math.PI * (1 + Math.Cos(Math.PI * z1 / _h));
                _adh[t] = t == Table ? 0 : _adh[t + 1] + 2 * Math.PI * 0.5 * (g0 + g1) * (_h / Table);
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
        public double Mass { get; private set; }

        /// <summary>
        /// Shares a total mass (kg) equally over the particles: a lattice counts the cells of a round volume only to within
        /// a few per cent, the liquid's mass is exact. The solver then spaces the particles to the real density.
        /// </summary>
        public void SetTotalMass(double kg)
        {
            if (Count > 0 && kg > 0) Mass = kg / Count;
        }

        public double LatticeSum { get; }
        public int Count { get; private set; }

        /// <summary>Solver iterations per substep (more = stiffer, less compression); the minimum when a tolerance is set.</summary>
        public int Iterations { get; set; } = 4;

        /// <summary>
        /// When above 0, the pressure solve is iterated until no particle is compressed by more than this (0.002 = 0.2 %),
        /// up to <see cref="MaxIterations"/>: incompressible to a stated accuracy rather than to a fixed effort (renders).
        /// </summary>
        public double CompressionTolerance { get; set; }

        public int MaxIterations { get; set; } = 40;

        /// <summary>Pressure iterations the last substep used.</summary>
        public int LastIterations { get; private set; }

        /// <summary>
        /// Air drag on spray, drops and thin streams (particles with few neighbours, i.e. exposed to the air): a sphere of the
        /// particle's volume, Schiller–Naumann drag, the world's air density and wind. The bulk is shielded by itself.
        /// </summary>
        public bool AirDrag { get; set; } = true;

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

        /// <summary>Fastest particle at the start of the last <see cref="Step"/> (m/s).</summary>
        public double LastMaxSpeed { get; private set; }

        /// <summary>[diagnostics] Called at each window of <see cref="Settle"/>: steps so far, the most any particle moved in the window and the mean (m).</summary>
        public static Action<int, double, double>? SettleTrace { get; set; }

        /// <summary>How long the last <see cref="Settle"/> ran the liquid before it lay still (seconds of its own time).</summary>
        public double LastSettleSeconds { get; private set; }

        /// <summary>Fastest motion of a body around the liquid at the last <see cref="Step"/> (m/s).</summary>
        public double LastBodyMotion { get; private set; }

        private readonly Dictionary<SceneBody, Vec3> _forces = new Dictionary<SceneBody, Vec3>();
        private readonly Dictionary<SceneBody, Vec3> _torques = new Dictionary<SceneBody, Vec3>();

        /// <summary>Force (N) the liquid puts on each body it touches, averaged over its last step.</summary>
        public IReadOnlyDictionary<SceneBody, Vec3> LastForces => _forces;

        public Vec3 Position(int i) => new Vec3(_x[3 * i], _x[3 * i + 1], _x[3 * i + 2]);
        public Vec3 Velocity(int i) => new Vec3(_v[3 * i], _v[3 * i + 1], _v[3 * i + 2]);

        /// <summary>Particle i's lasting number (the memory order changes as the liquid moves; this does not).</summary>
        public int ParticleId(int i) => _id[i];

        /// <summary>
        /// All positions as x, y, z floats (for drawing and recording), each particle at its lasting number: two snapshots
        /// line up particle by particle, so their difference is each particle's motion.
        /// </summary>
        public float[] Snapshot()
        {
            var a = new float[3 * Count];
            for (int i = 0; i < Count; i++)
            {
                int j = 3 * _id[i];
                a[j] = (float)_x[3 * i];
                a[j + 1] = (float)_x[3 * i + 1];
                a[j + 2] = (float)_x[3 * i + 2];
            }

            return a;
        }

        /// <summary>All velocities as vx, vy, vz floats (for drawing).</summary>
        public float[] VelocitySnapshot()
        {
            var a = new float[3 * Count];
            for (int i = 0; i < Count; i++)
            {
                int j = 3 * _id[i];
                a[j] = (float)_v[3 * i];
                a[j + 1] = (float)_v[3 * i + 1];
                a[j + 2] = (float)_v[3 * i + 2];
            }

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
                _id[n0 + i] = n0 + i;
            }

            Count = n;
            Asleep = false;
            _restTime = -1;
        }

        private void Grow(int n)
        {
            if (_x.Length >= 3 * n) return;
            int cap = Math.Max(n, 2 * Count);
            Array.Resize(ref _x, 3 * cap);
            Array.Resize(ref _v, 3 * cap);
            Array.Resize(ref _id, cap);
            _p = new double[3 * cap];
            _q = new double[3 * cap];
            _vsrc = new double[3 * cap];
            _rho = new double[cap];
            _lam = new double[cap];
            _nb = new int[cap * MaxNeighbours];
            _nbw = new double[cap * MaxNeighbours];
            _nbCount = new int[cap];
            _wb = new int[cap * Slots];
            _wd = new double[cap * Slots];
            _wn = new double[3 * cap * Slots];
            _wv = new double[3 * cap * Slots];
            _wphi = new double[cap * Slots];
            _wg = new double[3 * cap * Slots];
            _wfield = new bool[cap * Slots];
            _wangle = new double[cap * Slots];
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

        /// <summary>
        /// Longest liquid (sub)step (s). 1 ms keeps a resting column within 1 % of its density with 4 iterations; 3 ms let
        /// it compress by 8 %, so the liquid is never stepped coarser than this, slow or not.
        /// </summary>
        public double MaxStep { get; set; } = 1e-3;

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
            // How fast the walls move: their own speed, or how far they went since the last step (a body moved by hand
            // has no speed of its own).
            double bodyMove = 0;
            foreach (var b in _near)
            {
                if (b.IsStatic) continue;
                double r = b.EffectiveShape?.BoundingRadius ?? 0;
                bodyMove = Math.Max(bodyMove, b.Velocity.Length + b.AngularVelocity.Length * r);
                if (_poses.TryGetValue(b, out var pose)) bodyMove = Math.Max(bodyMove, Moved(pose, b) / dt);
            }

            LastMaxSpeed = vmax;
            LastBodyMotion = bodyMove;
            if (SleepEnabled && Rest(dt, bodyMove))
            {
                foreach (var b in _near) _poses[b] = (b.Position, b.Orientation);   // the next step's motion is from here
                return;
            }

            Asleep = false;
            _forces.Clear();
            _torques.Clear();
            double speed = vmax + bodyMove;
            // Substeps: no particle moves more than 0.3 spacing (CFL), and none is longer than MaxStep (a render's 0.1 ms
            // accuracy means the liquid too is solved every 0.1 ms).
            int sub = Math.Max(1, Math.Min(64, Math.Max((int)Math.Ceiling(speed * dt / (0.3 * Spacing)), (int)Math.Ceiling(dt / MaxStep - 1e-9))));
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

        /// <summary>How far a body's surface went from a pose: its centre's move plus the turn at its bounding radius.</summary>
        private static double Moved((Vec3 X, Quat Q) pose, SceneBody b)
        {
            double dot = Math.Min(1, Math.Abs(pose.Q.W * b.Orientation.W + pose.Q.X * b.Orientation.X + pose.Q.Y * b.Orientation.Y + pose.Q.Z * b.Orientation.Z));
            return (pose.X - b.Position).Length + 2 * Math.Acos(dot) * (b.EffectiveShape?.BoundingRadius ?? 0);
        }

        // [liquid] Rest is judged by where the particles are, not by their speed. A position-based liquid standing still
        // quivers about fixed places by about g·h² each step (10 µm at 1 ms), which reads as a speed of about g·h
        // (10 mm/s) that never dies away, so a still liquid was never found slower than 1.5 mm/s and never slept. Over a
        // window of 0.2 s the quiver cancels and only real flow adds up.
        private double[] _restX = Array.Empty<double>();
        private readonly Dictionary<SceneBody, (Vec3 X, Quat Q)> _restPoses = new Dictionary<SceneBody, (Vec3 X, Quat Q)>();
        private double _restTime = -1;

        /// <summary>The window over which the liquid must stand still before it sleeps (s).</summary>
        public double RestWindow { get; set; } = 0.2;

        /// <summary>
        /// True when the liquid sleeps through this step. It falls asleep when, over <see cref="RestWindow"/>, no particle
        /// went a quarter of the spacing and on average they went under 3 % of it, while the bodies around it stayed within
        /// 2 % of the spacing of where they stood. Asleep, its containers keep carrying its last push; it wakes when a body
        /// moves that far from where it stood when the liquid fell asleep, or faster than two spacings a second.
        /// </summary>
        private bool Rest(double dt, double bodyMove)
        {
            bool bodiesStill = bodyMove < 2 * Spacing && !BodiesMoved();
            if (Asleep)
            {
                if (bodiesStill)
                {
                    SuggestedStep = MaxStep;
                    return true;
                }

                _restTime = -1;
                return false;
            }

            if (!bodiesStill || _restTime < 0)
            {
                BeginRest();
                return false;
            }

            _restTime += dt;
            if (_restTime < RestWindow) return false;
            double worst = 0, sum = 0;
            for (int i = 0; i < Count; i++)
            {
                int k = 3 * i, r = 3 * _id[i];
                double dx = _x[k] - _restX[r], dy = _x[k + 1] - _restX[r + 1], dz = _x[k + 2] - _restX[r + 2];
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                worst = Math.Max(worst, d);
                sum += d;
            }

            if (worst < 0.25 * Spacing && sum / Count < 0.03 * Spacing)
            {
                Asleep = true;
                Array.Clear(_v, 0, 3 * Count);   // at rest: the quiver is not motion
                SuggestedStep = MaxStep;
                return true;
            }

            BeginRest();
            return false;
        }

        private void BeginRest()
        {
            _restTime = 0;
            if (_restX.Length < 3 * Count) _restX = new double[_x.Length];
            for (int i = 0; i < Count; i++)
            {
                int k = 3 * i, r = 3 * _id[i];
                _restX[r] = _x[k];
                _restX[r + 1] = _x[k + 1];
                _restX[r + 2] = _x[k + 2];
            }

            _restPoses.Clear();
            foreach (var b in _near)
                if (!b.IsStatic)
                    _restPoses[b] = (b.Position, b.Orientation);
        }

        private bool BodiesMoved()
        {
            int moving = 0;
            foreach (var b in _near)
            {
                if (b.IsStatic) continue;
                moving++;
                if (!_restPoses.TryGetValue(b, out var pose) || Moved(pose, b) > 0.02 * Spacing) return true;
            }

            return moving != _restPoses.Count;
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

            int nn = _near.Count;
            if (_start.Length < nn)
            {
                _start = new (Vec3, Quat)[nn];
                _nearShape = new CollisionShape[nn];
                _nearField = new Field?[nn];
                _nearLo = new Vec3[nn];
                _nearHi = new Vec3[nn];
            }

            // Built here, read in parallel below: per particle and body only array reads.
            for (int b = 0; b < nn; b++)
            {
                var shape = _near[b].EffectiveShape!;
                var field = FieldOf(_near[b]);
                _nearShape[b] = shape;
                _nearField[b] = field;
                (_nearLo[b], _nearHi[b]) = LocalBounds(shape, field);
            }

            if (before.Count != nn) _restTime = -1;
        }

        // [perf] Per near body, gathered once per step: its shape, its distance field and a box in its own frame that
        // holds its solid. A particle farther from that box than the kernel's reach plus its move cannot touch the body:
        // the long wall of a channel is skipped by the particles across it, where its bounding sphere took them all in.
        private CollisionShape[] _nearShape = Array.Empty<CollisionShape>();
        private Field?[] _nearField = Array.Empty<Field?>();
        private Vec3[] _nearLo = Array.Empty<Vec3>(), _nearHi = Array.Empty<Vec3>();

        private static (Vec3 Lo, Vec3 Hi) LocalBounds(CollisionShape s, Field? f)
        {
            if (f != null) return (f.Lo, f.Lo + new Vec3((f.Nx - 1) * f.Cell, (f.Ny - 1) * f.Cell, (f.Nz - 1) * f.Cell));
            var h = s.Kind switch
            {
                ShapeKind.Box => s.HalfExtents,
                ShapeKind.Sphere => new Vec3(s.Radius, s.Radius, s.Radius),
                ShapeKind.Cylinder => new Vec3(s.Radius, s.Radius, s.HalfHeight),
                _ => new Vec3(s.BoundingRadius, s.BoundingRadius, s.BoundingRadius)
            };
            return (-h, h);
        }

        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        private readonly double[] _phase = new double[6];

        /// <summary>Wall-clock seconds spent per phase since the start: walls, neighbours, pressure, viscosity and surface tension, air drag, rest.</summary>
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

            // [perf] Each pass over the particles wakes every core; at a few thousand particles that costs about as much
            // as the work, so passes that touch only the particle's own data are done in one.
            // 1. Predict, then 2. walls: no crossing within the step, the walls within reach recorded for the pressure.
            For(n, (from, to) =>
            {
                for (int i = from; i < to; i++)
                {
                    int k = 3 * i;
                    _v[k] += g.X * h;
                    _v[k + 1] += g.Y * h;
                    _v[k + 2] += g.Z * h;
                    _p[k] = _x[k] + _v[k] * h;
                    _p[k + 1] = _x[k + 1] + _v[k + 1] * h;
                    _p[k + 2] = _x[k + 2] + _v[k + 2] * h;
                    Array.Clear(_imp, 3 * Slots * i, 3 * Slots);
                    Walls(scene, i, h, true);
                }
            });

            Lap(0);
            // 3. Neighbours.
            Neighbours(n);
            Lap(1);

            // 4. Incompressibility.
            Project(n, Iterations);

            Lap(2);
            // 5. Exact non-penetration against the true surfaces (the iterations tracked them to first order), and the
            // velocities from the move.
            For(n, (from, to) =>
            {
                for (int i = from; i < to; i++)
                {
                    Walls(scene, i, h, false);
                    int k = 3 * i;
                    _v[k] = (_p[k] - _x[k]) / h;
                    _v[k + 1] = (_p[k + 1] - _x[k + 1]) / h;
                    _v[k + 2] = (_p[k + 2] - _x[k + 2]) / h;
                }
            });

            Lap(0);
            // 6. The real viscosity and surface tension.
            ViscosityAndTension(h);
            Lap(3);
            if (AirDrag) Drag(scene, h);
            Lap(4);
            double rho0 = Kind.Density, maxComp = 0, sumRho = 0;
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

        /// <summary>The density constraint, solved <paramref name="iterations"/> times on the predicted positions (Jacobi).</summary>
        private void Project(int n, int iterations)
        {
            double rho0 = Kind.Density, m = Mass, eps = 1e-2 / (Spacing * Spacing);
            double wallScale = m / (_s3 * rho0);
            double w0 = W(0);
            double tol = CompressionTolerance;
            int cap = tol > 0 ? Math.Max(iterations, MaxIterations) : iterations;
            LastIterations = cap;
            for (int it = 0; it < cap; it++)
            {
                bool last = it == cap - 1;
                For(n, (from, to) =>
                {
                    for (int i = from; i < to; i++)
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
                            _nbw[b0 + a] = 0;
                            if (r >= _h) continue;
                            sum += W(r);
                            if (r < 1e-12) continue;
                            double f = Wd(r) / r * m / rho0;
                            _nbw[b0 + a] = f;   // the correction below is at the same positions
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
                            rho += m / _s3 * _wphi[ws];
                            gx += wallScale * _wg[3 * ws];
                            gy += wallScale * _wg[3 * ws + 1];
                            gz += wallScale * _wg[3 * ws + 2];
                        }

                        _rho[i] = rho;
                        double c = Math.Max(0, rho / rho0 - 1);
                        _lam[i] = -c / (g2 + gx * gx + gy * gy + gz * gz + eps);
                    }
                });

                if (tol > 0 && it >= iterations - 1)
                {
                    double worst = 0;
                    for (int i = 0; i < n; i++) worst = Math.Max(worst, _rho[i] / rho0 - 1);
                    if (worst < tol)
                    {
                        LastIterations = it + 1;
                        break;
                    }
                }

                // The correction, applied to a second copy of the positions (every particle's is computed from where the
                // others stood), so the walls are kept up in the same pass.
                For(n, (from, to) =>
                {
                    for (int i = from; i < to; i++)
                    {
                        int k = 3 * i;
                        double px = _p[k], py = _p[k + 1], pz = _p[k + 2], li = _lam[i];
                        double ux = 0, uy = 0, uz = 0;
                        int cnt = _nbCount[i], b0 = i * MaxNeighbours;
                        for (int a = 0; a < cnt; a++)
                        {
                            double w = _nbw[b0 + a];
                            if (w == 0) continue;
                            int j = _nb[b0 + a], kj = 3 * j;
                            double f = (li + _lam[j]) * w;
                            ux += f * (px - _p[kj]);
                            uy += f * (py - _p[kj + 1]);
                            uz += f * (pz - _p[kj + 2]);
                        }

                        for (int s = 0; s < Slots; s++)
                        {
                            int ws = i * Slots + s;
                            if (_wb[ws] == -1 || li == 0) continue;
                            // Δx = λ ∇C with ∇C = (m / (s³ρ0)) ∇Φ: away from the solid (λ < 0).
                            double px2 = li * wallScale * _wg[3 * ws], py2 = li * wallScale * _wg[3 * ws + 1], pz2 = li * wallScale * _wg[3 * ws + 2];
                            ux += px2;
                            uy += py2;
                            uz += pz2;
                            _imp[3 * ws] += px2;
                            _imp[3 * ws + 1] += py2;
                            _imp[3 * ws + 2] += pz2;
                        }

                        double qx = px + ux, qy = py + uy, qz = pz + uz;
                        // The walls moved relative to the particle by its correction; keep it off them.
                        for (int s = 0; s < Slots; s++)
                        {
                            int ws = i * Slots + s;
                            if (_wb[ws] == -1) continue;
                            double nx = _wn[3 * ws], ny = _wn[3 * ws + 1], nz = _wn[3 * ws + 2];
                            double along = nx * ux + ny * uy + nz * uz;
                            double d = _wd[ws] + along;
                            if (d < Radius)
                            {
                                double push = Radius - d;
                                qx += nx * push;
                                qy += ny * push;
                                qz += nz * push;
                                _imp[3 * ws] += nx * push;
                                _imp[3 * ws + 1] += ny * push;
                                _imp[3 * ws + 2] += nz * push;
                                d = Radius;
                            }

                            double moved = d - _wd[ws];
                            _wd[ws] = d;
                            if (_wfield[ws])
                            {
                                // First order along the move: Φ changes by ∇Φ·Δx (the push-out moved it along n).
                                _wphi[ws] += _wg[3 * ws] * ux + _wg[3 * ws + 1] * uy + _wg[3 * ws + 2] * uz
                                             + (_wg[3 * ws] * nx + _wg[3 * ws + 1] * ny + _wg[3 * ws + 2] * nz) * (moved - along);
                                if (_wphi[ws] < 0) _wphi[ws] = 0;
                            }
                            else PlanarSlot(ws);
                        }

                        _q[k] = qx;
                        _q[k + 1] = qy;
                        _q[k + 2] = qz;
                    }
                });

                (_p, _q) = (_q, _p);
            }
        }

        /// <summary>
        /// Brings a freshly poured liquid to rest before the world starts, as if it had been poured a moment earlier: it is
        /// stepped under gravity against its containers (held where they stand) with its motion damped each step, until over
        /// the last 25 ms no particle went a quarter of the spacing and on average they went under 1 % of it (at most 0.4 s).
        /// It then starts still, at its real density, carrying its own weight in hydrostatic balance.
        /// </summary>
        /// <remarks>
        /// The rest is judged by how far the particles went, as the running liquid's sleep is: at rest each step's gravity
        /// kick (g·h) and the incompressibility projection that takes it back leave a step-to-step speed of ~25 mm/s that
        /// never falls below a speed threshold, and a few particles at the free surface and in corners keep quivering by
        /// up to a millimetre, while the body of the liquid stays where it is.
        /// </remarks>
        public void Settle(PhysicsScene scene, double maxTime = 0.4)
        {
            if (Count == 0) return;
            // At the step it will run with: the resting state of a 0.1 ms liquid is not that of a 1 ms one.
            double h = Math.Min(1e-3, MaxStep);
            int maxSteps = (int)Math.Ceiling(maxTime / h), minSteps = (int)Math.Ceiling(0.03 / h);
            int window = Math.Max(1, (int)Math.Ceiling(0.025 / h));
            double keep = Math.Pow(0.6, h / 1e-3);   // the same damping per millisecond at any step
            int ids = 0;
            for (int i = 0; i < Count; i++) ids = Math.Max(ids, _id[i] + 1);
            var mark = new double[3 * ids];   // positions at the window's start, by lasting number
            void Mark()
            {
                for (int i = 0; i < Count; i++)
                {
                    int j = 3 * _id[i], k = 3 * i;
                    mark[j] = _x[k];
                    mark[j + 1] = _x[k + 1];
                    mark[j + 2] = _x[k + 2];
                }
            }

            (double Worst, double Mean) Moved()
            {
                double most = 0, sum = 0;
                for (int i = 0; i < Count; i++)
                {
                    int j = 3 * _id[i], k = 3 * i;
                    double dx = _x[k] - mark[j], dy = _x[k + 1] - mark[j + 1], dz = _x[k + 2] - mark[j + 2];
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    most = Math.Max(most, d);
                    sum += d;
                }

                return (most, sum / Math.Max(1, Count));
            }

            Gather(scene, h);
            Mark();
            int steps = 0;
            for (int k = 0; k < maxSteps; k++)
            {
                for (int b = 0; b < _near.Count; b++) _start[b] = (_near[b].Position, _near[b].Orientation);
                Substep(scene, h, h);
                steps = k + 1;
                for (int i = 0; i < 3 * Count; i++) _v[i] *= keep;   // quasi-static: the pour's sloshing is gone by the time the world starts
                if ((k + 1) % window != 0) continue;
                var (worst, mean) = Moved();
                SettleTrace?.Invoke(k + 1, worst, mean);
                if (k + 1 >= minSteps && worst < 0.25 * Spacing && mean < 0.01 * Spacing) break;
                Mark();
            }

            LastSettleSeconds = steps * h;
            Array.Clear(_v, 0, 3 * Count);
            _forces.Clear();
            _torques.Clear();
            foreach (var b in _near) _poses[b] = (b.Position, b.Orientation);
        }

        private static int Chunk(int n) => Math.Max(64, n / (4 * Environment.ProcessorCount) + 1);

        /// <summary>
        /// A pass over the particles [0, n) on every core: on the solver's work team, which stays awake between the
        /// passes of a step (Mechanics/WorkTeam.cs), or on the thread pool when the team is busy with another liquid.
        /// </summary>
        private static void For(int n, Action<int, int> body)
        {
            int chunk = Chunk(n);
            if (WorkTeam.Shared.TryRun(n, chunk, body)) return;
            Parallel.ForEach(Partitioner.Create(0, n, chunk), range => body(range.Item1, range.Item2));
        }

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

                if (sweep && d < reach)
                {
                    SetSlot(s0, -2, d, gn, Vec3.Zero);
                    PlanarSlot(s0);
                }
            }

            var from = new Vec3(_x[k], _x[k + 1], _x[k + 2]);
            double moved = (p - from).Length;
            for (int b = 0; b < _near.Count; b++)
            {
                var body = _near[b];
                var shape = _nearShape[b];
                var field = _nearField[b];
                // Far from this body (the box of its solid in its own frame, plus the kernel's reach and this move):
                // nothing to do.
                var le = body.Orientation.InverseRotate(p - body.Position);
                double m = reach + moved;
                Vec3 blo = _nearLo[b], bhi = _nearHi[b];
                if (le.X < blo.X - m || le.Y < blo.Y - m || le.Z < blo.Z - m || le.X > bhi.X + m || le.Y > bhi.Y + m || le.Z > bhi.Z + m)
                    continue;

                var push = Vec3.Zero;
                if (sweep)
                {
                    // Continuous: from where it was (in the body as it stood then) to where it would go (in the body now).
                    var (sx, sq) = _start[b];
                    var ls = sq.InverseRotate(new Vec3(_x[k], _x[k + 1], _x[k + 2]) - sx);
                    var dl = le - ls;
                    double len = dl.Length;
                    // A solid can only be crossed when the clearances at both ends together are shorter than the move (the
                    // distance changes by at most the distance moved): most particles skip the exact ray.
                    bool maybe = len > 1e-9 && (shape.Kind == ShapeKind.Mesh || ClosestOnSegment(ls, dl, len) < shape.BoundingRadius + Radius);
                    if (maybe)
                    {
                        double slack = 2 * Radius + 4 * (field?.Cell ?? 0);
                        double ds = Distance(field, body, shape, ls, double.MaxValue, out double dsv, out _) ? dsv : double.MaxValue;
                        double de = Distance(field, body, shape, le, double.MaxValue, out double dev, out _) ? dev : double.MaxValue;
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
                    if (Distance(field, body, shape, local, reach, out double sd, out var nlocal) && sd < reach)
                    {
                        // [liquid] The contact itself is with the true solid. A field's interpolated distance runs short in
                        // a concave corner (a tank's floor meeting its walls) and its gradient turns from cell to cell
                        // there, so a particle resting in the corner was pushed off a wall it did not touch, a different
                        // way every step: the liquid never came to rest. The field still gives the pressure (Φ).
                        bool exact = field != null && sd < Radius + 2 * field.Cell;
                        if (exact)
                        {
                            if (body.SignedDistanceSolid(shape, local, reach, out double ed, out var en) && ed < reach)
                            {
                                sd = ed;
                                nlocal = en;
                            }
                            else sd = reach;
                        }

                        dist = sd;
                        nw = body.Orientation.Rotate(nlocal);
                        // A corner touches on two or three faces: each push frees it from the nearest one in turn.
                        for (int pass = 0; dist < Radius && pass < 3; pass++)
                        {
                            var np = p + nw * (Radius - dist);
                            push += np - p;
                            p = np;
                            dist = Radius;
                            if (!exact) break;
                            local = body.Orientation.InverseRotate(p - body.Position);
                            if (!body.SignedDistanceSolid(shape, local, reach, out double ed, out var en) || ed >= Radius) break;
                            dist = ed;
                            nw = body.Orientation.Rotate(en);
                        }

                        dist = Math.Max(dist, Radius);
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
                if (field != null && SamplePhi(field, body.Orientation.InverseRotate(p - body.Position), out double fphi, out var fg))
                {
                    var gw = body.Orientation.Rotate(fg);
                    _wfield[slot] = true;
                    _wphi[slot] = fphi;
                    _wg[3 * slot] = gw.X;
                    _wg[3 * slot + 1] = gw.Y;
                    _wg[3 * slot + 2] = gw.Z;
                }
                else PlanarSlot(slot);
                _imp[3 * slot] = push.X;
                _imp[3 * slot + 1] = push.Y;
                _imp[3 * slot + 2] = push.Z;
            }

            _p[k] = p.X;
            _p[k + 1] = p.Y;
            _p[k + 2] = p.Z;
            if (sweep) Shadow(s0);
        }

        /// <summary>
        /// A wall's density map counts the whole half-space behind it as solid. A second wall facing the same way further
        /// back (the arm behind a glass's 2.5 mm wall, the table under a bowl's floor) lies inside that half-space: counting
        /// it too would double the wall's push. It keeps its slot for the reaction of contacts, without pressure. Walls at
        /// an angle to each other (a corner) both count.
        /// </summary>
        private void Shadow(int s0)
        {
            for (int a = 0; a < Slots; a++)
            {
                int wa = s0 + a;
                if (_wb[wa] == -1 || _wd[wa] >= _h) continue;
                for (int b = 0; b < Slots; b++)
                {
                    int wb = s0 + b;
                    if (a == b || _wb[wb] == -1 || _wb[wb] == _wb[wa] || _wfield[wb] || _wd[wb] >= _h || _wd[wb] < _wd[wa]) continue;
                    double dot = _wn[3 * wa] * _wn[3 * wb] + _wn[3 * wa + 1] * _wn[3 * wb + 1] + _wn[3 * wa + 2] * _wn[3 * wb + 2];
                    if (dot > 0.5)
                    {
                        _wd[wb] = _h;
                        PlanarSlot(wb);
                    }
                }
            }
        }

        private static double ClosestOnSegment(Vec3 a, Vec3 d, double len)
        {
            double f = Math.Max(0, Math.Min(1, -a.Dot(d) / (len * len)));
            return (a + d * f).Length;
        }

        /// <summary>Diagnostics: particle i's wall slots after the last substep (body, distance, normal, Φ, field).</summary>
        internal string DebugSlots(int i)
        {
            var sb = new System.Text.StringBuilder();
            for (int s = 0; s < Slots; s++)
            {
                int ws = i * Slots + s;
                if (_wb[ws] == -1) continue;
                sb.Append($"[{(_wb[ws] == -2 ? "ground" : "b" + _wb[ws])} d={_wd[ws] * 1000:F2} n=({_wn[3 * ws]:F2},{_wn[3 * ws + 1]:F2},{_wn[3 * ws + 2]:F2}) Φ={_wphi[ws]:F3}{(_wfield[ws] ? " field" : "")}] ");
            }

            sb.Append($"ρ/ρ0={_rho[i] / Kind.Density:F3} nb={_nbCount[i]}");
            return sb.ToString();
        }

        /// <summary>A flat face at the slot's distance: Φ(d) from the half-space table, ∇Φ = −A(d) n.</summary>
        private void PlanarSlot(int ws)
        {
            WallKernel(_wd[ws], out double phi, out double area);
            _wfield[ws] = false;
            _wphi[ws] = phi;
            _wg[3 * ws] = -area * _wn[3 * ws];
            _wg[3 * ws + 1] = -area * _wn[3 * ws + 1];
            _wg[3 * ws + 2] = -area * _wn[3 * ws + 2];
        }

        private void SetSlot(int ws, int body, double d, Vec3 n, Vec3 v)
        {
            _wangle[ws] = ContactAngle(body >= 0 ? _near[body].Material : "concrete", Kind);
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
                    _wfield[ws] = false;
                    _wphi[ws] = 0;
                    _wg[3 * ws] = _wg[3 * ws + 1] = _wg[3 * ws + 2] = 0;
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

        private int[] _fill = Array.Empty<int>();

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
            if (_fill.Length < size) _fill = new int[size];
            var fill = _fill;
            Array.Clear(fill, 0, size);
            for (int i = 0; i < n; i++)
            {
                int c = _cell[i];
                _sorted[_cellStart[c] + fill[c]++] = i;
            }

            double h2 = _h * _h;
            For(n, (from, to) =>
            {
                Span<int> seen = stackalloc int[27];
                for (int i = from; i < to; i++)
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
            var ids = new int[_id.Length];
            for (int a = 0; a < n; a++)
            {
                int i = idx[a];
                ids[a] = _id[i];
                for (int c = 0; c < 3; c++)
                {
                    x[3 * a + c] = _x[3 * i + c];
                    v[3 * a + c] = _v[3 * i + c];
                }
            }

            _x = x;
            _v = v;
            _id = ids;
        }

        // ------------------------------------------------------------------ viscosity, surface tension

        /// <summary>
        /// The real viscosity and surface tension, in one pass over the neighbours (each reads the velocities as they
        /// were before it, so the order of the particles does not matter).
        /// </summary>
        private void ViscosityAndTension(double h)
        {
            int n = Count;
            double nu = Kind.KinematicViscosity, sigma = Kind.SurfaceTension;
            if (nu <= 0 && sigma <= 0) return;
            if (nu > 0) Array.Copy(_v, _vsrc, 3 * n);
            double nd = Kind.Density / Mass;   // particles per m³ at rest
            double eps = sigma > 0 ? 8 * sigma / (Math.PI * nd * nd * Math.Pow(_h, 5) * CohesionMoment) : 0;
            double a0 = eps / Mass;
            double wallA = nd * a0;   // the half-space pull per unit of its tabulated profile
            For(n, (from, to) =>
            {
                for (int i = from; i < to; i++) ViscosityAndTension(i, h, nu, a0, wallA);
            });
        }

        /// <summary>
        /// The real viscosity: Morris' SPH Laplacian between neighbours and a no-slip wall moving with each body, taken
        /// implicitly (one Jacobi sweep of v = v* + h·ν∇²v) so even honey (ν = 7·10⁻³ m²/s) is stable at this step.
        /// </summary>
        /// <remarks>
        /// The real surface tension as cohesion between particles: a pair attraction F(r) = ε sin(π r/h) for r &lt; h, whose
        /// strength follows from the liquid's σ through the Kirkwood–Buff (Fowler) relation for the work of a flat cut,
        /// σ = (π n²/8) ∫ F(r) r⁴ dr with n the particles per m³. Inside the liquid the pulls cancel; at a surface, in a thin
        /// sheet and along a stream they hold the liquid together, at any resolution: a film spreading over a floor stays a
        /// film and a falling stream stays a stream.
        /// Wetting: each wall pulls like a liquid-filled half-space scaled by (1 + cos θ)/2, which gives the work of
        /// adhesion σ(1 + cos θ) of Young–Dupré with θ the contact angle on that body's material: water spreads over glass
        /// (25°), less over PLA (75°), beads on PTFE (110°).
        /// </remarks>
        private void ViscosityAndTension(int i, double h, double nu, double a0, double wallA)
        {
            double m = Mass, eta2 = 0.01 * _l * _l;
            var src = _vsrc;
            int k = 3 * i;
            double px = _p[k], py = _p[k + 1], pz = _p[k + 2], rhoi = Math.Max(_rho[i], 1e-9);
            double diag = 0, sx = 0, sy = 0, sz = 0, ax = 0, ay = 0, az = 0;
            int cnt = _nbCount[i], b0 = i * MaxNeighbours;
            for (int a = 0; a < cnt; a++)
            {
                int j = _nb[b0 + a], kj = 3 * j;
                double dx = px - _p[kj], dy = py - _p[kj + 1], dz = pz - _p[kj + 2];
                double r2 = dx * dx + dy * dy + dz * dz, r = Math.Sqrt(r2);
                if (r >= _h || r < 1e-12) continue;
                if (nu > 0)
                {
                    // c_ij = 2ν m/ρ_j · (r·∇W)/(r² + η²) is negative; the term is −c_ij (v_i − v_j)… written as a weight.
                    double c = -2 * nu * m / Math.Max(_rho[j], 1e-9) * (Wd(r) * r) / (r2 + eta2);
                    diag += c;
                    sx += c * src[kj];
                    sy += c * src[kj + 1];
                    sz += c * src[kj + 2];
                }

                if (a0 > 0)
                {
                    // Cohesion: towards the neighbour (d points away from it).
                    double f = a0 * Math.Sin(Math.PI * r / _h) / r;
                    ax -= f * dx;
                    ay -= f * dy;
                    az -= f * dz;
                }
            }

            for (int s = 0; s < Slots; s++)
            {
                int ws = i * Slots + s;
                if (_wb[ws] == -1) continue;
                if (nu > 0)
                {
                    // The wall as liquid moving with it: the same Laplacian summed over the half-space beyond it,
                    // ∫ −W'(r)/r dV = 2π ∫_d^h W(z) dz.
                    double c = 2 * nu * WallViscous(_wd[ws]) / (LatticeSum * _s3) * (Kind.Density / rhoi);
                    diag += c;
                    sx += c * _wv[3 * ws];
                    sy += c * _wv[3 * ws + 1];
                    sz += c * _wv[3 * ws + 2];
                }

                if (a0 > 0 && _wd[ws] < _h)
                {
                    double wet = (1 + Math.Cos(_wangle[ws] * Math.PI / 180)) / 2;
                    double pull = wallA * wet * WallAdhesion(_wd[ws]);   // towards the wall: −n
                    ax -= pull * _wn[3 * ws];
                    ay -= pull * _wn[3 * ws + 1];
                    az -= pull * _wn[3 * ws + 2];
                    // Newton's third law: the liquid pulls the wall as much (booked like the wall's pushes).
                    _imp[3 * ws] -= pull * _wn[3 * ws] * h * h;
                    _imp[3 * ws + 1] -= pull * _wn[3 * ws + 1] * h * h;
                    _imp[3 * ws + 2] -= pull * _wn[3 * ws + 2] * h * h;
                }
            }

            if (nu > 0 && diag > 0)
            {
                double inv = 1 / (1 + h * diag);
                _v[k] = (src[k] + h * sx) * inv;
                _v[k + 1] = (src[k + 1] + h * sy) * inv;
                _v[k + 2] = (src[k + 2] + h * sz) * inv;
            }

            _v[k] += h * ax;
            _v[k + 1] += h * ay;
            _v[k + 2] += h * az;
        }

        /// <summary>∫₀¹ sin(πq) q⁴ dq: the cohesion profile's moment in the surface-tension relation.</summary>
        private static readonly double CohesionMoment = Integrate(q => Math.Sin(Math.PI * q) * Math.Pow(q, 4));

        private static double Integrate(Func<double, double> f)
        {
            const int n = 4000;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += f((i + 0.5) / n);
            return sum / n;
        }

        /// <summary>
        /// Water's contact angle (degrees) on a body's material: how much it wets it (Young). Handbook values for clean
        /// surfaces; other liquids than water wet most solids better (oils, alcohol ≈ 20°).
        /// </summary>
        public static double ContactAngle(string? material, LiquidKind kind)
        {
            if (kind == LiquidKind.Ethanol || kind == LiquidKind.Oil) return 20;
            return (material ?? string.Empty) switch
            {
                "glass" => 25,
                "steel" => 70,
                "aluminium" => 80,
                "pla" => 75,
                "petg" => 72,
                "abs" => 80,
                "acrylic" => 70,
                "nylon" => 63,
                "wood" or "mdf" or "paper" => 60,
                "concrete" or "tile" => 40,
                "rubber" => 100,
                "ptfe" => 110,
                "carbon" or "fr4" => 75,
                _ => 70
            };
        }

        private readonly double[] _adh = new double[Table + 1];

        /// <summary>
        /// The normal pull of a half-space of unit particle density on a particle at distance d, per unit ε:
        /// 2π ∫_d^h z G(z) dz with G(z) = ∫_z^h sin(π r/h) dr.
        /// </summary>
        private double WallAdhesion(double d)
        {
            double u = Math.Max(0, Math.Min(_h, d)) / _h * Table;
            int t = Math.Min(Table - 1, (int)u);
            return _adh[t] + (_adh[t + 1] - _adh[t]) * (u - t);
        }

        /// <summary>
        /// Air drag on exposed particles: a = (3/8)(ρ_air/ρ)(C_d/r)|u|u with u the velocity relative to the wind, r the radius
        /// of a sphere of the particle's volume and C_d = 24/Re (1 + 0.15 Re^0.687) up to Re 1000, 0.44 above; integrated
        /// implicitly (u ← u / (1 + k|u|h)), so it never overshoots.
        /// </summary>
        private void Drag(PhysicsScene scene, double h)
        {
            double rhoAir = scene.AirDensity;
            if (rhoAir <= 0) return;
            const double nuAir = 1.5e-5;
            double r = Math.Pow(3 * Mass / (4 * Math.PI * Kind.Density), 1.0 / 3);
            double ratio = 0.375 * rhoAir / Kind.Density / r;
            for (int i = 0; i < Count; i++)
            {
                if (_nbCount[i] >= 12) continue;
                int k = 3 * i;
                var p = new Vec3(_x[k], _x[k + 1], _x[k + 2]);
                var wind = scene.WindAt(p);
                double ux = _v[k] - wind.X, uy = _v[k + 1] - wind.Y, uz = _v[k + 2] - wind.Z;
                double u = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                if (u < 1e-6) continue;
                double re = 2 * r * u / nuAir;
                double cd = re < 1000 ? 24 / re * (1 + 0.15 * Math.Pow(re, 0.687)) : 0.44;
                double f = 1 / (1 + ratio * cd * u * h);
                _v[k] = wind.X + ux * f;
                _v[k + 1] = wind.Y + uy * f;
                _v[k + 2] = wind.Z + uz * f;
            }
        }

        /// <summary>Particles that fell far below the ground (off the world) are dropped.</summary>
        private void Cull(PhysicsScene scene)
        {
            int n = Count, w = 0;
            for (int i = 0; i < n; i++)
            {
                if (_x[3 * i + 2] < -20) continue;
                if (w != i)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        _x[3 * w + c] = _x[3 * i + c];
                        _v[3 * w + c] = _v[3 * i + c];
                    }

                    _id[w] = _id[i];
                }

                w++;
            }

            if (w == n) return;
            Count = w;
            _restTime = -1;   // the numbers change: the rest window starts again
            // Lasting numbers stay 0 … Count − 1 (in their old order), so snapshots keep lining up from here on.
            var order = new int[w];
            for (int i = 0; i < w; i++) order[i] = i;
            var keys = new int[w];
            Array.Copy(_id, keys, w);
            Array.Sort(keys, order);
            for (int r = 0; r < w; r++) _id[order[r]] = r;
        }
    }
}
