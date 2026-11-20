using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RobotTwin.CoreSim.Analysis.Cfd;
using RobotTwin.CoreSim.Analysis.Geometry;
using RobotTwin.CoreSim.Analysis.Jobs;
using RobotTwin.CoreSim.Analysis.Recording;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Analysis
{
    /// <summary>
    /// "Render" side of the analysis: what a replay needs frame by frame — the pose and shaft /
    /// horn angle of every object exactly as the world scene draws it — and the flow around the
    /// whole assembly (all moving parts together, as they really sit at that instant).
    /// </summary>
    public static partial class AnalysisRun
    {
        private sealed class PoseChannels
        {
            public string Id = string.Empty;
            public Channel Px = null!, Py = null!, Pz = null!, Qw = null!, Qx = null!, Qy = null!, Qz = null!, Port = null!, Temp = null!;
            public Channel? Lift, Drag, Deflection;
        }

        private static List<PoseChannels> PoseRecorder(WorldDocument world, ChannelStore store)
        {
            var list = new List<PoseChannels>();
            foreach (var o in world.Objects)
            {
                string id = o.Id;
                const string g = "Sahne/Poz (oynatma)";
                Channel C(string key, string label, string unit) => store.Get("pose/" + id + "/" + key, o.Name + " · " + label, unit, g);
                list.Add(new PoseChannels
                {
                    Id = id,
                    Px = C("px", "x", "m"), Py = C("py", "y", "m"), Pz = C("pz", "z", "m"),
                    Qw = C("qw", "qw", ""), Qx = C("qx", "qx", ""), Qy = C("qy", "qy", ""), Qz = C("qz", "qz", ""),
                    Port = C("port", "mil / kol açısı", "rad"),
                    Temp = store.Get("pose/" + id + "/temp", o.Name + " · sıcaklık", "°C", "Isı/Parça sıcaklıkları"),
                    Lift = WorldPhysics.IsWing(o) ? store.Get("aero/" + id + "/lift", o.Name + " · kaldırma (yüzey normali)", "N", "Uçuş/Kanat kuvvetleri") : null,
                    Drag = WorldPhysics.IsWing(o) ? store.Get("aero/" + id + "/drag", o.Name + " · sürükleme (akış yönü)", "N", "Uçuş/Kanat kuvvetleri") : null,
                    Deflection = WorldPhysics.IsWing(o) ? store.Get("aero/" + id + "/delta", o.Name + " · kumanda yüzeyi açısı", "°", "Uçuş/Kanat kuvvetleri") : null
                });
            }

            return list;
        }

        private static void RecordPoses(WorldPhysics? physics, ChannelStore store, List<PoseChannels> list, WorldDocument world)
        {
            foreach (var p in list)
            {
                Vec3 pos;
                Quat q;
                if (physics?.PoseOf(p.Id) is { } pose)
                {
                    pos = pose.Position;
                    q = pose.Orientation;
                }
                else if (world.Find(p.Id) is { } o)
                {
                    pos = new Vec3(o.Pose.X, o.Pose.Y, o.Pose.Z);
                    q = Automation.J.FromRpy(o.Pose.Roll * Math.PI / 180, o.Pose.Pitch * Math.PI / 180, o.Pose.Yaw * Math.PI / 180);
                }
                else continue;

                store.Set(p.Px, pos.X);
                store.Set(p.Py, pos.Y);
                store.Set(p.Pz, pos.Z);
                store.Set(p.Qw, q.W);
                store.Set(p.Qx, q.X);
                store.Set(p.Qy, q.Y);
                store.Set(p.Qz, q.Z);
                store.Set(p.Port, physics?.PortAngle(p.Id) ?? 0);
                store.Set(p.Temp, physics?.TemperatureOf(p.Id) ?? world.Environment.AmbientC);
                if (p.Lift != null && physics?.WingLoads(p.Id) is { } wl)
                {
                    // Wind axes from the reference angle of attack (sideslip is small for these parts).
                    double a = wl.Alpha;
                    var flow = new Vec3(-Math.Cos(a), 0, Math.Sin(a));
                    double drag = wl.Force.Dot(flow);
                    store.Set(p.Lift, (wl.Force - flow * drag).Length * Math.Sign(wl.Force.Z));
                    store.Set(p.Drag!, drag);
                    store.Set(p.Deflection!, physics.WingDeflection(p.Id));
                }
            }
        }

        /// <summary>Recorded pose of an object at sample <paramref name="i"/> (null when it was not recorded).</summary>
        public static (Vec3 Position, Quat Orientation, double Port)? RecordedPose(ChannelStore store, string objectId, int i)
        {
            var px = store.Find("pose/" + objectId + "/px");
            if (px == null || i < 0 || i >= px.Count) return null;
            double V(string k) => store.Find("pose/" + objectId + "/" + k) is { } c && i < c.Count ? c[i] : 0;
            var q = new Quat(V("qw"), V("qx"), V("qy"), V("qz"));
            return (new Vec3(px[i], V("py"), V("pz")), q, V("port"));
        }

        /// <summary>Sample index at (or just before) a time.</summary>
        public static int SampleAt(ChannelStore store, double time)
        {
            int lo = 0, hi = store.Count - 1;
            if (hi < 0) return -1;
            double t0 = store.Time(0);
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (store.Time(mid) - t0 <= time) lo = mid;
                else hi = mid - 1;
            }

            return lo;
        }

        /// <summary>
        /// 3D lattice-Boltzmann flow around every moving part together, at a few instants of the
        /// recording: the parts sit where they were, the air comes at the assembly with the
        /// world's wind minus the assembly's own velocity (or the test speed along the chosen axis
        /// when it barely moves). Result per keyframe: force on the assembly (N, world frame), its
        /// drag coefficient and the flow speed field for the replay.
        /// </summary>
        public static List<CfdResult> AeroAssembly(WorldDocument world, PhysicsScene? scene, AnalysisResult r, AnalysisSettings s,
            CancellationToken cancel, Action<double>? progress = null, WorldPhysics? physics = null)
        {
            var frames = new List<CfdResult>();
            var parts = world.Objects
                .Where(o => !o.IsStatic)
                .Select(o => PartOf(world, scene, o.Id, physics))
                .Where(p => p != null && !p.IsStatic)
                .Cast<PartGeometry>()
                .ToList();
            if (parts.Count == 0 || r.Channels.Count == 0) return frames;
            int keys = Math.Max(1, Math.Min(6, (int)Math.Ceiling(r.Channels.Duration * 2)));
            for (int k = 0; k < keys; k++)
            {
                if (cancel.IsCancellationRequested) break;
                double t = keys == 1 ? r.Channels.Duration / 2 : r.Channels.Duration * (k + 0.5) / keys;
                int i = SampleAt(r.Channels, t);
                var cfd = AeroFrame(world, parts, r, s, i, cancel, p => progress?.Invoke((k + p) / keys));
                if (cfd != null) frames.Add(cfd);
            }

            return frames;
        }

        private static CfdResult? AeroFrame(WorldDocument world, List<PartGeometry> parts, AnalysisResult r, AnalysisSettings s, int sample,
            CancellationToken cancel, Action<double>? progress)
        {
            var store = r.Channels;
            var solids = new List<Solid>();
            Vec3 momentum = Vec3.Zero;
            double mass = 0;
            foreach (var p in parts)
            {
                var pose = RecordedPose(store, p.ObjectId, sample);
                var pos = pose?.Position ?? p.Position;
                var q = pose?.Orientation ?? p.Orientation;
                // The part's solid is in its body frame; the body centre is offset from the object origin.
                solids.Add(p.Solid.Transformed(BodyCentre(p, pos, q), q));
                double V(string key) => store.Find("body/" + p.ObjectId + "/" + key) is { } c && sample < c.Count ? c[sample] : 0;
                momentum += new Vec3(V("vx"), V("vy"), V("vz")) * p.Mass;
                mass += p.Mass;
            }

            var velocity = mass > 0 ? momentum / mass : Vec3.Zero;
            var wind = new Vec3(world.Environment.WindX, world.Environment.WindY, world.Environment.WindZ);
            var rel = wind - velocity;
            if (rel.Length < 0.2) rel = AxisVector(s.AeroAxis) * s.AeroSpeed;
            double speed = rel.Length;
            var dir = rel / speed;

            // Grid frame: x along the relative wind, z as close to world up as possible.
            var up = Math.Abs(dir.Dot(Vec3.UnitZ)) > 0.95 ? Vec3.UnitX : Vec3.UnitZ;
            var gy = up.Cross(dir).Normalized();
            var gz = dir.Cross(gy);
            Vec3 lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue), hi = new Vec3(double.MinValue, double.MinValue, double.MinValue);
            foreach (var so in solids)
            {
                var b = so.Bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vec3((c & 1) != 0 ? b.Max.X : b.Min.X, (c & 2) != 0 ? b.Max.Y : b.Min.Y, (c & 4) != 0 ? b.Max.Z : b.Min.Z);
                    var l = new Vec3(corner.Dot(dir), corner.Dot(gy), corner.Dot(gz));
                    lo = new Vec3(Math.Min(lo.X, l.X), Math.Min(lo.Y, l.Y), Math.Min(lo.Z, l.Z));
                    hi = new Vec3(Math.Max(hi.X, l.X), Math.Max(hi.Y, l.Y), Math.Max(hi.Z, l.Z));
                }
            }

            double length = hi.X - lo.X, width = hi.Y - lo.Y, height = hi.Z - lo.Z;
            double d = Math.Max(Math.Max(width, height), 1e-3);
            int res = Math.Max(10, Math.Min(40, s.AeroResolution));
            double dx = d / res;
            int nx = (int)Math.Ceiling((length + 4 * d) / dx), ny = (int)Math.Ceiling(2.4 * d / dx), nz = (int)Math.Ceiling(2.4 * d / dx);
            nx = Math.Min(nx, 360);
            double u = 0.06;
            double re = speed * d / AirViscosity;
            double reLat = Math.Min(re, 1500);
            double tau = 3 * u * res / reLat + 0.5;
            var lbm = new Lbm3D(nx, ny, nz, tau, u) { Smagorinsky = re > reLat ? 0.17 : 0 };
            // Domain origin (grid frame): one diameter upstream, centred across.
            var origin = new Vec3(lo.X - d, (lo.Y + hi.Y) / 2 - ny * dx / 2, (lo.Z + hi.Z) / 2 - nz * dx / 2);
            int frontal = 0;
            var proj = new bool[ny * nz];
            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        var l = origin + new Vec3((x + 0.5) * dx, (y + 0.5) * dx, (z + 0.5) * dx);
                        var w = dir * l.X + gy * l.Y + gz * l.Z;
                        bool solid = false;
                        foreach (var so in solids)
                        {
                            // Conservative voxels: a cell is solid when the part passes through it, so thin
                            // plates and wings thinner than a cell are still seen by the flow (one cell thick).
                            if (so.Distance(w) < 0.5 * dx)
                            {
                                solid = true;
                                break;
                            }
                        }

                        if (!solid) continue;
                        lbm.Solid[lbm.Index(x, y, z)] = true;
                        if (!proj[z * ny + y])
                        {
                            proj[z * ny + y] = true;
                            frontal++;
                        }
                    }
                }
            }

            int steps = Math.Max(600, (int)(Math.Max(2, s.AeroPasses / 5) * (nx) / u / 4));
            steps = Math.Min(steps, 6000);
            int record = steps * 2 / 3;
            Vec3 fsum = Vec3.Zero;
            int fn = 0;
            for (int step = 0; step < steps; step++)
            {
                if (cancel.IsCancellationRequested) return null;
                lbm.Step();
                if (step >= record)
                {
                    fsum += new Vec3(lbm.BodyFx, lbm.BodyFy, lbm.BodyFz);
                    fn++;
                }

                if ((step & 63) == 0) progress?.Invoke((double)step / steps);
            }

            var fl = fn > 0 ? fsum / fn : Vec3.Zero;
            double rho = world.Environment.AirDensity ?? 1.2;
            double scale = rho * speed * speed * dx * dx / (u * u);   // lattice force → newtons
            var force = (dir * fl.X + gy * fl.Y + gz * fl.Z) * scale;
            double area = Math.Max(1, frontal) * dx * dx;
            var speedField = new float[lbm.Ux.Length];
            for (int c = 0; c < speedField.Length; c++)
            {
                speedField[c] = lbm.Solid[c] ? 0f : (float)(Math.Sqrt(lbm.Ux[c] * lbm.Ux[c] + lbm.Uy[c] * lbm.Uy[c] + lbm.Uz[c] * lbm.Uz[c]) / u * speed);
            }

            var result = new CfdResult
            {
                Name = "Montaj (tüm hareketli parçalar)",
                Is3D = true,
                Time = store.Time(sample) - store.Time(0),
                Nx = nx,
                Ny = ny,
                Nz = nz,
                Dx = dx,
                Speed = speed,
                ReferenceLength = d,
                ReferenceArea = area,
                ReynoldsPhysical = re,
                ReynoldsLattice = reLat,
                Tau = tau,
                LatticeSpeed = u,
                AirDensity = rho,
                TimeStep = dx * u / speed,
                MeanCd = fl.X / (0.5 * u * u * Math.Max(1, frontal)),
                MeanCl = fl.Z / (0.5 * u * u * Math.Max(1, frontal)),
                Speed3D = speedField,
                Solid = (bool[])lbm.Solid.Clone(),
                Origin = dir * origin.X + gy * origin.Y + gz * origin.Z,
                AxisX = dir,
                AxisY = gy,
                AxisZ = gz,
                Force = force
            };
            result.Notes.Add($"{parts.Count} parça birlikte, bağıl hava {speed:0.##} m/s, Re = {re:0}" + (re > reLat ? $" (ağda {reLat:0} + Smagorinsky LES)" : string.Empty) + $", hücre {dx * 1000:0.#} mm"
                + (parts.Any(p => Math.Min(p.Size.Z, Math.Min(p.Size.X, p.Size.Y)) * 2 < dx) ? "; hücreden ince yüzeyler bir hücre kalınlığında temsil edildi (kanat kuvvetleri kanat modelinden gelir)." : "."));
            Streamlines(lbm, result, (lo.Y + hi.Y) / 2 - origin.Y, (lo.Z + hi.Z) / 2 - origin.Z, d, u, speed);
            return result;
        }

        /// <summary>
        /// Streamlines through the solved 3D velocity field (RK2, trilinear interpolation), seeded on a
        /// grid across the oncoming flow around the assembly. Stored in world coordinates with the
        /// local speed (m/s): x, y, z, speed, x, y, z, speed, …
        /// </summary>
        private static void Streamlines(Lbm3D lbm, CfdResult r, double cy, double cz, double d, double u, double speed)
        {
            int nx = lbm.Nx, ny = lbm.Ny, nz = lbm.Nz;
            double dx = r.Dx;
            bool Inside(double x, double y, double z) => x >= 0 && y >= 0 && z >= 0 && x < nx - 1 && y < ny - 1 && z < nz - 1;
            Vec3 Vel(double x, double y, double z)
            {
                int i = (int)x, j = (int)y, k = (int)z;
                double fx = x - i, fy = y - j, fz = z - k;
                double vx = 0, vy = 0, vz = 0;
                for (int c = 0; c < 8; c++)
                {
                    int ii = i + (c & 1), jj = j + ((c >> 1) & 1), kk = k + ((c >> 2) & 1);
                    double w = ((c & 1) != 0 ? fx : 1 - fx) * (((c >> 1) & 1) != 0 ? fy : 1 - fy) * (((c >> 2) & 1) != 0 ? fz : 1 - fz);
                    int n = lbm.Index(ii, jj, kk);
                    if (lbm.Solid[n]) continue;
                    vx += w * lbm.Ux[n];
                    vy += w * lbm.Uy[n];
                    vz += w * lbm.Uz[n];
                }

                return new Vec3(vx, vy, vz);
            }

            int seedsY = 9, seedsZ = 7;
            double spanY = 1.4 * d / dx, spanZ = 1.1 * d / dx;
            for (int a = 0; a < seedsY; a++)
            {
                for (int b = 0; b < seedsZ; b++)
                {
                    double y = cy / dx + (a / (seedsY - 1.0) - 0.5) * spanY, z = cz / dx + (b / (seedsZ - 1.0) - 0.5) * spanZ;
                    double x = 1.5;
                    if (!Inside(x, y, z)) continue;
                    var pts = new List<float>();
                    for (int step = 0; step < 6 * nx && Inside(x, y, z); step++)
                    {
                        var v1 = Vel(x, y, z);
                        double m1 = v1.Length;
                        if (m1 < 1e-5 * u) break;
                        if ((step & 1) == 0)
                        {
                            var w = r.Origin + r.AxisX * (x * dx) + r.AxisY * (y * dx) + r.AxisZ * (z * dx);
                            pts.Add((float)w.X);
                            pts.Add((float)w.Y);
                            pts.Add((float)w.Z);
                            pts.Add((float)(m1 / u * speed));
                        }

                        double h = 0.6 / m1;   // ~0.6 cells per step
                        var mid = Vel(x + 0.5 * h * v1.X, y + 0.5 * h * v1.Y, z + 0.5 * h * v1.Z);
                        x += h * mid.X;
                        y += h * mid.Y;
                        z += h * mid.Z;
                        if (Inside(x, y, z) && lbm.Solid[lbm.Index((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(z))]) break;
                    }

                    if (pts.Count >= 16) r.Streamlines.Add(pts.ToArray());
                }
            }
        }

        /// <summary>World position of a part's body centre from the object pose (geometry offset in the object frame).</summary>
        private static Vec3 BodyCentre(PartGeometry p, Vec3 objectPosition, Quat q) => objectPosition + q.Rotate(p.BodyOffset);
    }
}
