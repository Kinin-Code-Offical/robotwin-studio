using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Analysis.Cfd;
using RobotTwin.CoreSim.Analysis.Geometry;
using RobotTwin.CoreSim.Analysis.Jobs;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Analysis
{
    /// <summary>
    /// [air] The real air around the assembly over the whole recording, solved in time: one 3D lattice-Boltzmann run in a
    /// world-fixed box. The parts move through it exactly as they were recorded and push and drag the air with their own
    /// surface velocity; the world's wind (with its gusts) blows in at the faces; the ground is a wall. Nothing else
    /// drives the air: with still air and parts that do not move, there is no flow. Smoke particles carried by the air
    /// show it in 3D.
    /// </summary>
    public static partial class AnalysisRun
    {
        private sealed class AirPart
        {
            public PartGeometry Part = null!;
            public Vec3[] C = null!;
            public Quat[] Q = null!;
            public Aabb Local;
            public double Radius;
            public bool Thin;
            public double Fastest, FastestAt;
            public Vec3 Cn, V, W, LastC;
            public Quat Qn = Quat.Identity, LastQ = Quat.Identity;
            public int X0 = 1, X1, Y0, Y1, Z0, Z1;   // cell box of the last voxelisation (X1 < X0: none)
        }

        /// <summary>Lattice cell updates a render may spend on the air, by accuracy (the cell size grows to fit).</summary>
        private static double AirBudget(AccuracyPreset a) => a switch
        {
            AccuracyPreset.Fast => 4e8,
            AccuracyPreset.Standard => 1.5e9,
            AccuracyPreset.Reference => 2e10,
            _ => 5e9
        };

        public static List<CfdResult> AirFlow(WorldDocument world, PhysicsScene? scene, AnalysisResult r, AnalysisSettings s,
            CancellationToken cancel, Action<double>? progress = null, WorldPhysics? physics = null)
        {
            var frames = new List<CfdResult>();
            var store = r.Channels;
            int ns = store.Count;
            if (scene == null || ns < 2) return frames;
            double t0 = store.Time(0), duration = store.Duration;
            if (duration <= 0) return frames;

            // ---------------------------------------------------------------- the parts and their recorded motion
            var moving = new List<AirPart>();
            var still = new List<AirPart>();
            var fixedSolids = new List<Solid>();
            foreach (var o in world.Objects)
            {
                if (WorldPhysics.IsLiquid(o)) continue;
                var p = PartOf(world, scene, o.Id, physics);
                if (p == null) continue;
                if (p.IsStatic)
                {
                    fixedSolids.Add(p.Solid.Transformed(p.Position, p.Orientation));
                    continue;
                }

                var ap = new AirPart { Part = p, C = new Vec3[ns], Q = new Quat[ns], Local = p.Solid.Bounds };
                for (int i = 0; i < ns; i++)
                {
                    if (RecordedPose(store, p.ObjectId, i) is { } pose)
                    {
                        ap.C[i] = BodyCentre(p, pose.Position, pose.Orientation);
                        ap.Q[i] = (pose.Orientation * p.BodyTurn).Normalized();
                    }
                    else
                    {
                        ap.C[i] = p.Position;
                        ap.Q[i] = p.Orientation;
                    }
                }

                var b = ap.Local;
                ap.Radius = Math.Sqrt(Math.Max(b.Max.X * b.Max.X, b.Min.X * b.Min.X) + Math.Max(b.Max.Y * b.Max.Y, b.Min.Y * b.Min.Y) + Math.Max(b.Max.Z * b.Max.Z, b.Min.Z * b.Min.Z));
                moving.Add(ap);
            }

            // Parts that do not move over the recording (a board lying on the floor, a free base that stays put) are fixed
            // solids at their recorded pose: the box is fitted around what really moves.
            for (int k = moving.Count - 1; k >= 0; k--)
            {
                var ap = moving[k];
                double reach = 0;
                for (int i = 1; i < ns; i++)
                {
                    var dq = (ap.Q[i] * ap.Q[0].Conjugate).Normalized();
                    double angle = 2 * Math.Acos(Math.Min(1, Math.Abs(dq.W)));
                    reach = Math.Max(reach, (ap.C[i] - ap.C[0]).Length + angle * ap.Radius);
                }

                if (reach > 5e-4) continue;
                fixedSolids.Add(ap.Part.Solid.Transformed(ap.C[0], ap.Q[0]));
                still.Add(ap);
                moving.RemoveAt(k);
            }

            // With nothing moving only the wind can stir the air: then the box is fitted around the parts that stand in it.
            var shaped = moving.Count > 0 ? moving : still;
            if (shaped.Count == 0) return frames;

            // Region swept by the parts, the assembly's size and the fastest surface speed.
            var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = new Vec3(double.MinValue, double.MinValue, double.MinValue);
            var lo0 = lo;
            var hi0 = hi;
            int stride = Math.Max(1, ns / 400);
            double partSpeed = 0;
            double finest = double.MaxValue;   // the smallest part's middle extent: the cell must resolve it
            foreach (var ap in shaped)
            {
                var b = ap.Local;
                var ext3 = new[] { b.Max.X - b.Min.X, b.Max.Y - b.Min.Y, b.Max.Z - b.Min.Z };
                Array.Sort(ext3);
                finest = Math.Min(finest, ext3[1]);
                for (int i = 0; i < ns; i++)
                {
                    if (i > 0)
                    {
                        double dt = store.Time(i) - store.Time(i - 1);
                        if (dt > 0)
                        {
                            var (v, w) = Rates(ap, i - 1, i, dt);
                            double sp = v.Length + w.Length * ap.Radius;
                            partSpeed = Math.Max(partSpeed, sp);
                            if (sp > ap.Fastest)
                            {
                                ap.Fastest = sp;
                                ap.FastestAt = store.Time(i) - t0;
                            }
                        }
                    }

                    if (i % stride != 0 && i != ns - 1) continue;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = ap.C[i] + ap.Q[i].Rotate(new Vec3((c & 1) != 0 ? b.Max.X : b.Min.X, (c & 2) != 0 ? b.Max.Y : b.Min.Y, (c & 4) != 0 ? b.Max.Z : b.Min.Z));
                        lo = Vec3.Min(lo, corner);
                        hi = Vec3.Max(hi, corner);
                        if (i == 0)
                        {
                            lo0 = Vec3.Min(lo0, corner);
                            hi0 = Vec3.Max(hi0, corner);
                        }
                    }
                }
            }

            var size0 = hi0 - lo0;
            double d = Math.Max(0.02, Math.Max(size0.X, Math.Max(size0.Y, size0.Z)));
            var mid = (lo + hi) * 0.5;
            Vec3 meanWind = Vec3.Zero;
            double windSpeed = 0;
            int windSamples = 0;
            for (int i = 0; i < ns; i += stride)
            {
                var wnd = scene.WindAt(mid, store.Time(i));
                meanWind += wnd;
                windSpeed = Math.Max(windSpeed, wnd.Length);
                windSamples++;
            }

            meanWind /= Math.Max(1, windSamples);
            double speed = Math.Max(partSpeed, windSpeed);
            if (speed < 0.01)
            {
                r.Note(moving.Count == 0
                    ? "Hava akışı yok: dünyada rüzgâr yok ve kayıt boyunca hiçbir parça hareket etmiyor."
                    : $"Hava akışı yok: dünyada rüzgâr yok ve parçaların en hızlı yüzeyi {partSpeed * 1000:0.#} mm/s ile havayı fark edilir biçimde itmiyor.");
                return frames;
            }

            // ---------------------------------------------------------------- the box and the lattice
            double margin = Math.Max(0.5 * d, 0.03);
            lo -= new Vec3(margin, margin, margin);
            hi += new Vec3(margin, margin, margin);
            if (meanWind.Length > 0.05)
            {
                // Room for the wake downstream of the assembly.
                var dn = meanWind.Normalized() * (1.5 * d);
                lo = Vec3.Min(lo, lo + dn);
                hi = Vec3.Max(hi, hi + dn);
            }

            int res = Math.Max(10, Math.Min(40, s.AeroResolution));
            // A quarter of the smallest moving part's width (an 18 mm servo arm: 4.5 mm), unless the budget says coarser.
            double dx = Math.Max(1e-3, Math.Min(d / res, finest / 4));
            const double u = 0.08;   // lattice speed of the fastest air: Mach 0.14
            var ext = hi - lo;
            double volume = ext.X * ext.Y * ext.Z;
            double work = volume * duration * speed / (u * Math.Pow(dx, 4));
            double budget = AirBudget(s.Accuracy);
            if (work > budget) dx *= Math.Pow(work / budget, 0.25);
            if (volume / (dx * dx * dx) > 3e6) dx = Math.Pow(volume / 3e6, 1.0 / 3);
            bool floor = lo.Z < 0;
            if (floor) lo = new Vec3(lo.X, lo.Y, -2 * dx);
            ext = hi - lo;
            int nx = Math.Max(8, (int)Math.Ceiling(ext.X / dx)), ny = Math.Max(8, (int)Math.Ceiling(ext.Y / dx)), nz = Math.Max(8, (int)Math.Ceiling(ext.Z / dx));
            int n = nx * ny * nz;
            var origin = lo;
            double dtL = u * dx / speed;
            int steps = (int)Math.Ceiling(duration / dtL);
            double nuLat = AirViscosity * dtL / (dx * dx);
            double tau = 3 * nuLat + 0.5;
            double re = speed * d / AirViscosity;
            double rho = world.Environment.AirDensity ?? 1.2;
            int sponge = Math.Max(3, (int)Math.Round(0.4 * margin / dx));

            var lbm = new Lbm3D(nx, ny, nz, tau, 0)
            {
                WorldBox = true,
                FarField = LbmFarField.Open,
                Smagorinsky = s.AeroSmagorinsky,
                Sponge = sponge
            };
            var wv = new float[3 * n];
            var dist = new float[n];
            var mask = new bool[n];
            var owner = new int[n];
            lbm.WallVelocity = wv;
            lbm.WallDistance = dist;
            lbm.ForceMask = mask;
            double toLat = dtL / dx;   // m/s → lattice
            var amb0 = scene.WindAt(mid, t0) * toLat;
            if (amb0.Length > 0)
                for (int z = 0; z < nz; z++)
                for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                    lbm.SetCell(x, y, z, amb0.X, amb0.Y, amb0.Z);

            Vec3 Centre(int i) => origin + new Vec3((i % nx + 0.5) * dx, (i / nx % ny + 0.5) * dx, (i / (nx * ny) + 0.5) * dx);

            // Fixed parts and the ground, once.
            var staticDist = new float[n];
            var staticSolid = new bool[n];
            var near = new List<Solid>();
            foreach (var so in fixedSolids)
            {
                var b = so.Bounds;
                if (b.Max.X < lo.X - 2 * dx || b.Min.X > hi.X + 2 * dx || b.Max.Y < lo.Y - 2 * dx || b.Min.Y > hi.Y + 2 * dx || b.Max.Z < lo.Z - 2 * dx || b.Min.Z > hi.Z + 2 * dx) continue;
                near.Add(so);
            }

            Parallel.For(0, nz, z =>
            {
                for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int i = (z * ny + y) * nx + x;
                    var w = Centre(i);
                    double best = floor ? w.Z / dx : 1e9;
                    foreach (var so in near)
                    {
                        var b = so.Bounds;
                        double ox = Math.Max(0, Math.Max(b.Min.X - w.X, w.X - b.Max.X));
                        double oy = Math.Max(0, Math.Max(b.Min.Y - w.Y, w.Y - b.Max.Y));
                        double oz = Math.Max(0, Math.Max(b.Min.Z - w.Z, w.Z - b.Max.Z));
                        double boxDist = Math.Sqrt(ox * ox + oy * oy + oz * oz);
                        double dk = (boxDist > 2 * dx ? boxDist : so.Distance(w)) / dx;
                        if (dk < best) best = dk;
                    }

                    staticDist[i] = (float)best;
                    staticSolid[i] = best < 0;
                }
            });
            var next = (bool[])staticSolid.Clone();
            Array.Copy(staticDist, dist, n);
            Array.Copy(staticSolid, lbm.Solid, n);
            for (int i = 0; i < n; i++) owner[i] = -1;
            foreach (var ap in moving) ap.Thin = 2 * Math.Min(ap.Part.Size.X, Math.Min(ap.Part.Size.Y, ap.Part.Size.Z)) < 2 * dx;

            // ---------------------------------------------------------------- motion of the parts through the lattice
            int sample = 0;
            void Pose(double t)
            {
                while (sample < ns - 2 && store.Time(sample + 1) - t0 <= t) sample++;
                double ta = store.Time(sample) - t0, tb = store.Time(sample + 1) - t0, span = tb - ta;
                double f = span > 0 ? Math.Max(0, Math.Min(1, (t - ta) / span)) : 0;
                foreach (var ap in moving)
                {
                    ap.Cn = ap.C[sample] + (ap.C[sample + 1] - ap.C[sample]) * f;
                    var qa = ap.Q[sample];
                    var qb = ap.Q[sample + 1];
                    if (qa.W * qb.W + qa.X * qb.X + qa.Y * qb.Y + qa.Z * qb.Z < 0) qb = new Quat(-qb.W, -qb.X, -qb.Y, -qb.Z);
                    ap.Qn = new Quat(qa.W + (qb.W - qa.W) * f, qa.X + (qb.X - qa.X) * f, qa.Y + (qb.Y - qa.Y) * f, qa.Z + (qb.Z - qa.Z) * f).Normalized();
                    (ap.V, ap.W) = span > 0 ? Rates(ap, sample, sample + 1, span) : (Vec3.Zero, Vec3.Zero);
                }
            }

            void Box(AirPart ap, out int x0, out int x1, out int y0, out int y1, out int z0, out int z1)
            {
                var b = ap.Local;
                var blo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
                var bhi = new Vec3(double.MinValue, double.MinValue, double.MinValue);
                for (int c = 0; c < 8; c++)
                {
                    var corner = ap.Cn + ap.Qn.Rotate(new Vec3((c & 1) != 0 ? b.Max.X : b.Min.X, (c & 2) != 0 ? b.Max.Y : b.Min.Y, (c & 4) != 0 ? b.Max.Z : b.Min.Z));
                    blo = Vec3.Min(blo, corner);
                    bhi = Vec3.Max(bhi, corner);
                }

                x0 = Math.Max(0, (int)Math.Floor((blo.X - origin.X) / dx) - 2);
                y0 = Math.Max(0, (int)Math.Floor((blo.Y - origin.Y) / dx) - 2);
                z0 = Math.Max(0, (int)Math.Floor((blo.Z - origin.Z) / dx) - 2);
                x1 = Math.Min(nx - 1, (int)Math.Ceiling((bhi.X - origin.X) / dx) + 2);
                y1 = Math.Min(ny - 1, (int)Math.Ceiling((bhi.Y - origin.Y) / dx) + 2);
                z1 = Math.Min(nz - 1, (int)Math.Ceiling((bhi.Z - origin.Z) / dx) + 2);
            }

            void ForBox(int x0, int x1, int y0, int y1, int z0, int z1, Action<int> body)
            {
                if (x1 < x0 || y1 < y0 || z1 < z0) return;
                Parallel.For(z0, z1 + 1, z =>
                {
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        body((z * ny + y) * nx + x);
                });
            }

            void ForBoxInOrder(int x0, int x1, int y0, int y1, int z0, int z1, Action<int> body)
            {
                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    body((z * ny + y) * nx + x);
            }

            int revoxels = 0, uncovered = 0;
            void Voxelize()
            {
                revoxels++;
                // Back to the fixed geometry where the parts were …
                foreach (var ap in moving)
                    ForBox(ap.X0, ap.X1, ap.Y0, ap.Y1, ap.Z0, ap.Z1, i =>
                    {
                        next[i] = staticSolid[i];
                        dist[i] = staticDist[i];
                        owner[i] = -1;
                        mask[i] = false;
                        if (staticSolid[i]) wv[3 * i] = wv[3 * i + 1] = wv[3 * i + 2] = 0;   // the ground and fixed parts stand still
                    });
                var old = new (int, int, int, int, int, int)[moving.Count];
                for (int k = 0; k < moving.Count; k++)
                {
                    var ap = moving[k];
                    old[k] = (ap.X0, ap.X1, ap.Y0, ap.Y1, ap.Z0, ap.Z1);
                    Box(ap, out ap.X0, out ap.X1, out ap.Y0, out ap.Y1, out ap.Z0, out ap.Z1);
                    ap.LastC = ap.Cn;
                    ap.LastQ = ap.Qn;
                }

                // … then every part where it is now.
                for (int k = 0; k < moving.Count; k++)
                {
                    var ap = moving[k];
                    var solid = ap.Part.Solid.Transformed(ap.Cn, ap.Qn);
                    bool thin = ap.Thin;
                    int kk = k;
                    ForBox(ap.X0, ap.X1, ap.Y0, ap.Y1, ap.Z0, ap.Z1, i =>
                    {
                        double dk = solid.Distance(Centre(i)) / dx;
                        if (dk < dist[i]) dist[i] = (float)dk;
                        if (dk < 0 || (thin && dk < 0.5))
                        {
                            next[i] = true;
                            owner[i] = kk;
                            mask[i] = true;
                        }
                    });
                }

                // Cells a part has left become air with the wall's velocity; cells it entered become solid.
                for (int k = 0; k < moving.Count; k++)
                {
                    var (ox0, ox1, oy0, oy1, oz0, oz1) = old[k];
                    var ap = moving[k];
                    // In order, not in parallel: a cell that becomes air takes its neighbours' density (deterministic).
                    ForBoxInOrder(ox0, ox1, oy0, oy1, oz0, oz1, Settle);
                    ForBoxInOrder(ap.X0, ap.X1, ap.Y0, ap.Y1, ap.Z0, ap.Z1, Settle);
                }
            }

            void Settle(int i)
            {
                bool now = next[i];
                if (lbm.Solid[i] == now) return;
                if (!now)
                {
                    lbm.Uncover(i, wv[3 * i], wv[3 * i + 1], wv[3 * i + 2]);
                    uncovered++;
                }

                lbm.Solid[i] = now;
            }

            void WallVelocities()
            {
                for (int k = 0; k < moving.Count; k++)
                {
                    var ap = moving[k];
                    int kk = k;
                    ForBox(ap.X0, ap.X1, ap.Y0, ap.Y1, ap.Z0, ap.Z1, i =>
                    {
                        if (owner[i] != kk) return;
                        var v = (ap.V + ap.W.Cross(Centre(i) - ap.Cn)) * toLat;
                        wv[3 * i] = (float)v.X;
                        wv[3 * i + 1] = (float)v.Y;
                        wv[3 * i + 2] = (float)v.Z;
                    });
                }
            }

            // ---------------------------------------------------------------- smoke
            var rng = new Random(s.Seed ^ 0x5A17);
            int inner0 = sponge;
            double spanX = nx - 1 - 2 * inner0, spanY = ny - 1 - 2 * inner0, spanZ = nz - 1 - 2 * inner0;
            bool hasInterior = spanX > 2 && spanY > 2 && spanZ > 2;
            int nt = hasInterior ? Math.Min(8000, Math.Max(500, n / 8)) : 0;
            var tx = new double[nt];
            var ty = new double[nt];
            var tz = new double[nt];

            void SpawnInside(int k)
            {
                for (int tries = 0; tries < 20; tries++)
                {
                    tx[k] = inner0 + rng.NextDouble() * spanX;
                    ty[k] = inner0 + rng.NextDouble() * spanY;
                    tz[k] = inner0 + rng.NextDouble() * spanZ;
                    if (!lbm.Solid[lbm.Index((int)Math.Round(tx[k]), (int)Math.Round(ty[k]), (int)Math.Round(tz[k]))]) return;
                }
            }

            void SpawnUpwind(int k, Vec3 amb)
            {
                // On a face the ambient air blows in through, chosen in proportion to the air it lets in.
                double fx = Math.Abs(amb.X) * spanY * spanZ, fy = Math.Abs(amb.Y) * spanX * spanZ, fz = Math.Abs(amb.Z) * spanX * spanY;
                double pick = rng.NextDouble() * (fx + fy + fz);
                SpawnInside(k);
                if (pick < fx) tx[k] = amb.X > 0 ? inner0 + 0.5 : nx - 1 - inner0 - 0.5;
                else if (pick < fx + fy) ty[k] = amb.Y > 0 ? inner0 + 0.5 : ny - 1 - inner0 - 0.5;
                else tz[k] = amb.Z > 0 ? inner0 + 0.5 : nz - 1 - inner0 - 0.5;
            }

            Vec3 AirAtCell(double x, double y, double z)
            {
                int i0 = Math.Max(0, Math.Min(nx - 2, (int)x)), j0 = Math.Max(0, Math.Min(ny - 2, (int)y)), k0 = Math.Max(0, Math.Min(nz - 2, (int)z));
                double fx = x - i0, fy = y - j0, fz = z - k0;
                double vx = 0, vy = 0, vz = 0;
                for (int c = 0; c < 8; c++)
                {
                    int ii = i0 + (c & 1), jj = j0 + ((c >> 1) & 1), kk = k0 + ((c >> 2) & 1);
                    double w = ((c & 1) != 0 ? fx : 1 - fx) * (((c >> 1) & 1) != 0 ? fy : 1 - fy) * (((c >> 2) & 1) != 0 ? fz : 1 - fz);
                    int m = (kk * ny + jj) * nx + ii;
                    if (lbm.Solid[m])
                    {
                        // Inside a part the smoke goes with the part's surface.
                        vx += w * wv[3 * m];
                        vy += w * wv[3 * m + 1];
                        vz += w * wv[3 * m + 2];
                    }
                    else
                    {
                        vx += w * lbm.Ux[m];
                        vy += w * lbm.Uy[m];
                        vz += w * lbm.Uz[m];
                    }
                }

                return new Vec3(vx, vy, vz);
            }

            for (int k = 0; k < nt; k++) SpawnInside(k);

            // ---------------------------------------------------------------- run
            double frameDt = Math.Max(1.0 / 30, duration / 300);
            double nextFrame = 0;
            double forceScale = rho * Math.Pow(dx, 4) / (dtL * dtL);   // lattice force → N
            Vec3 forceAcc = Vec3.Zero;
            int forceCount = 0;
            double slow = 0.03 * speed;
            var notes = new List<string>
            {
                $"Gerçek hava, zamanla: dünyaya sabit {nx}×{ny}×{nz} kutu, hücre {dx * 1000:0.#} mm, adım {dtL * 1e6:0.#} µs ({steps} adım). Parçalar kayıttaki gibi hareket eder ve havayı kendi yüzey hızlarıyla iter; rüzgâr kutunun yüzlerinden girer{(floor ? ", zemin duvardır" : string.Empty)}.",
                $"En hızlı hava {speed:0.##} m/s (parçalar {partSpeed:0.##} m/s, rüzgâr {windSpeed:0.##} m/s), Re ≈ {re:0}; düzenlenmiş çarpışma + Smagorinsky LES, eğri ve hareketli duvar.",
                "Havayı en hızlı iten parçalar: " + string.Join(", ", moving.OrderByDescending(a => a.Fastest).Take(3).Select(a => $"{a.Part.Name} {a.Fastest:0.##} m/s (t = {a.FastestAt:0.###} s)")) + "."
            };
            bool unstable = false;
            Pose(0);
            Voxelize();
            WallVelocities();
            for (int step = 0; step <= steps; step++)
            {
                if (cancel.IsCancellationRequested) break;
                double t = step * dtL;
                if (t >= nextFrame - 1e-12)
                {
                    var f = forceCount > 0 ? forceAcc / forceCount * forceScale : Vec3.Zero;
                    if (double.IsNaN(f.X) || double.IsInfinity(f.X))
                    {
                        unstable = true;
                        break;
                    }

                    forceAcc = Vec3.Zero;
                    forceCount = 0;
                    var smoke = new List<float>(nt);
                    for (int k = 0; k < nt; k++)
                    {
                        var v = AirAtCell(tx[k], ty[k], tz[k]) / toLat;
                        if (v.Length < slow) continue;
                        if (lbm.Solid[lbm.Index((int)Math.Round(tx[k]), (int)Math.Round(ty[k]), (int)Math.Round(tz[k]))]) continue;
                        smoke.Add((float)(origin.X + (tx[k] + 0.5) * dx));
                        smoke.Add((float)(origin.Y + (ty[k] + 0.5) * dx));
                        smoke.Add((float)(origin.Z + (tz[k] + 0.5) * dx));
                        smoke.Add((float)v.X);
                        smoke.Add((float)v.Y);
                        smoke.Add((float)v.Z);
                    }

                    var frame = new CfdResult
                    {
                        Name = "Hava (gerçek, zamanla)",
                        Is3D = true,
                        Time = t,
                        Nx = nx,
                        Ny = ny,
                        Nz = nz,
                        Dx = dx,
                        Speed = speed,
                        ReferenceLength = d,
                        ReynoldsPhysical = re,
                        ReynoldsLattice = re,
                        Tau = tau,
                        LatticeSpeed = u,
                        AirDensity = rho,
                        TimeStep = dtL,
                        Origin = origin,
                        Force = f,
                        Converged = true,
                        Tracers = smoke.ToArray()
                    };
                    if (frames.Count == 0) frame.Notes.AddRange(notes);
                    frames.Add(frame);
                    nextFrame += frameDt;
                }

                if (step == steps) break;
                Pose(t);
                bool moved = false;
                foreach (var ap in moving)
                {
                    var dq = (ap.Qn * ap.LastQ.Conjugate).Normalized();
                    double angle = 2 * Math.Acos(Math.Min(1, Math.Abs(dq.W)));
                    if ((ap.Cn - ap.LastC).Length + angle * ap.Radius > 0.35 * dx) moved = true;
                }

                if (moved) Voxelize();
                WallVelocities();
                var amb = scene.WindAt(mid, t0 + t) * toLat;
                lbm.AmbientX = amb.X;
                lbm.AmbientY = amb.Y;
                lbm.AmbientZ = amb.Z;
                lbm.Step();
                forceAcc += new Vec3(lbm.BodyFx, lbm.BodyFy, lbm.BodyFz);
                forceCount++;

                // Smoke moves with the air; what leaves the box comes back in with the wind (or anywhere in still air).
                bool wind = amb.Length > 1e-5;
                for (int k = 0; k < nt; k++)
                {
                    var v = AirAtCell(tx[k], ty[k], tz[k]);
                    tx[k] += v.X;
                    ty[k] += v.Y;
                    tz[k] += v.Z;
                    if (tx[k] < inner0 || ty[k] < inner0 || tz[k] < inner0 || tx[k] > nx - 1 - inner0 || ty[k] > ny - 1 - inner0 || tz[k] > nz - 1 - inner0
                        || double.IsNaN(tx[k]))
                    {
                        if (wind) SpawnUpwind(k, amb);
                        else SpawnInside(k);
                    }
                }

                if ((step & 31) == 0) progress?.Invoke((double)step / steps);
            }

            if (unstable)
            {
                r.Note("Hava akışı kararsız oldu ve durduruldu (sayısal taşma); daha düşük doğrulukla ya da daha kısa sürede deneyin.");
                return frames;
            }

            if (frames.Count > 0)
                frames[0].Notes.Add($"Parçalar ızgarada {revoxels} kez yeniden yerleştirildi; geride bıraktıkları {uncovered} hücre duvar hızıyla havaya katıldı. Duman: {nt} parçacık, en hızlı havanın %3'ünden yavaş olanlar çizilmez.");
            return frames;
        }

        /// <summary>Linear and angular velocity of a part between two recorded samples.</summary>
        private static (Vec3 V, Vec3 W) Rates(AirPart ap, int a, int b, double dt)
        {
            var v = (ap.C[b] - ap.C[a]) / dt;
            var dq = (ap.Q[b] * ap.Q[a].Conjugate).Normalized();
            if (dq.W < 0) dq = new Quat(-dq.W, -dq.X, -dq.Y, -dq.Z);
            var axis = new Vec3(dq.X, dq.Y, dq.Z);
            double sin = axis.Length;
            if (sin < 1e-12) return (v, Vec3.Zero);
            double angle = 2 * Math.Atan2(sin, dq.W);
            return (v, axis / sin * (angle / dt));
        }
    }
}
