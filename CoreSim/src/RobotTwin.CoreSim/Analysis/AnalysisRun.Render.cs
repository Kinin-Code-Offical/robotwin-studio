using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        internal sealed class PoseChannels
        {
            public string Id = string.Empty;
            public Channel Px = null!, Py = null!, Pz = null!, Qw = null!, Qx = null!, Qy = null!, Qz = null!, Port = null!, Temp = null!;
            public Channel? Lift, Drag, Deflection, Horn, ServoLoad, RodForce;
            public Channel? GasP, GasT, Thrust, MassFlow, ExitV, Lit, Surface, Grain, Smoke, Hot, Water;   // [gas]
            public Channel? ChuteOpen, ChuteX, ChuteY, ChuteZ;   // [launch]
        }

        internal static List<PoseChannels> PoseRecorder(WorldDocument world, ChannelStore store)
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
                    Deflection = WorldPhysics.IsWing(o) ? store.Get("aero/" + id + "/delta", o.Name + " · kumanda yüzeyi açısı", "°", "Uçuş/Kanat kuvvetleri") : null,
                    // [linkage] the pushrod: where the servo horn really is, what the air puts on it
                    Horn = WorldPhysics.HasLinkage(o) ? store.Get("aero/" + id + "/horn", o.Name + " · servo kolu açısı (yük altında)", "°", "Uçuş/İtme çubuğu") : null,
                    ServoLoad = WorldPhysics.HasLinkage(o) ? store.Get("aero/" + id + "/servo-load", o.Name + " · servoya binen menteşe yükü", "N·m", "Uçuş/İtme çubuğu") : null,
                    RodForce = WorldPhysics.HasLinkage(o) ? store.Get("aero/" + id + "/rod", o.Name + " · itme çubuğu kuvveti (+ çekme)", "N", "Uçuş/İtme çubuğu") : null
                });
                var pc = list[list.Count - 1];
                if (WorldPhysics.IsVessel(o))
                {
                    // [gas] the chamber and the jet, for graphs and for drawing the plume in the replay
                    Channel Gas(string key, string label, string unit) => store.Get("gas/" + id + "/" + key, o.Name + " · " + label, unit, "Gaz ve roket/" + o.Name);
                    pc.GasP = Gas("p", "oda basıncı (gösterge)", "bar");
                    pc.GasT = Gas("t", "gaz sıcaklığı", "°C");
                    pc.Thrust = Gas("thrust", "itki", "N");
                    pc.MassFlow = Gas("mdot", "kütle akışı", "g/s");
                    pc.ExitV = Gas("ve", "çıkış hızı", "m/s");
                    pc.Smoke = Gas("smoke", "duman (yoğuşmuş ürün) akışı", "g/s");
                    pc.Hot = Gas("hot", "sıcak jet (1 = yanıyor)", "");
                    pc.Water = Gas("water", "kalan su", "L");
                    if (o.Parameters.ContainsKey("grain"))
                    {
                        pc.Lit = Gas("lit", "yanan yüzey", "%");
                        pc.Surface = Gas("surface", "yakıt yüzeyi sıcaklığı", "°C");
                        pc.Grain = Gas("grain", "kalan yakıt", "%");
                    }
                }

                if (o.Parameters.ContainsKey("parachute"))
                {
                    Channel Chute(string key, string label, string unit) => store.Get("chute/" + id + "/" + key, o.Name + " · " + label, unit, "Paraşüt/" + o.Name);
                    pc.ChuteOpen = Chute("open", "paraşüt açıklığı", "");
                    pc.ChuteX = Chute("px", "çekme yönü x", "");
                    pc.ChuteY = Chute("py", "çekme yönü y", "");
                    pc.ChuteZ = Chute("pz", "çekme yönü z", "");
                }
            }

            return list;
        }

        internal static void RecordPoses(WorldPhysics? physics, ChannelStore store, List<PoseChannels> list, WorldDocument world)
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
                if (p.GasP != null)
                {
                    var g = physics?.GasOf(p.Id);
                    store.Set(p.GasP, g != null ? g.GaugePressure / 1e5 : 0);
                    store.Set(p.GasT!, g != null ? g.TemperatureK - 273.15 : world.Environment.AmbientC);
                    store.Set(p.Thrust!, g?.Thrust ?? 0);
                    store.Set(p.MassFlow!, (g?.MassFlow ?? 0) * 1000);
                    store.Set(p.ExitV!, g?.ExitVelocity ?? 0);
                    store.Set(p.Smoke!, (g?.SmokeRate ?? 0) * 1000);
                    store.Set(p.Hot!, g != null && (g.Burning || g.IgniterBurning) ? 1 : 0);
                    store.Set(p.Water!, g?.WaterLitres ?? 0);
                    if (p.Lit != null)
                    {
                        store.Set(p.Lit, (g?.LitFraction ?? 0) * 100);
                        store.Set(p.Surface!, g != null && g.SurfaceK > 0 ? g.SurfaceK - 273.15 : world.Environment.AmbientC);
                        store.Set(p.Grain!, (g?.GrainLeft ?? 1) * 100);
                    }
                }

                if (p.ChuteOpen != null)
                {
                    var c = physics?.ParachuteOf(p.Id);
                    store.Set(p.ChuteOpen, c is { Deployed: true } ? Math.Max(0.05, c.Value.Open) : 0);
                    store.Set(p.ChuteX!, c?.Pull.X ?? 0);
                    store.Set(p.ChuteY!, c?.Pull.Y ?? 0);
                    store.Set(p.ChuteZ!, c?.Pull.Z ?? 1);
                }
                if (p.Lift != null && physics?.WingLoads(p.Id) is { } wl)
                {
                    // Wind axes from the reference angle of attack (sideslip is small for these parts).
                    double a = wl.Alpha;
                    var flow = new Vec3(-Math.Cos(a), 0, Math.Sin(a));
                    double drag = wl.Force.Dot(flow);
                    store.Set(p.Lift, (wl.Force - flow * drag).Length * Math.Sign(wl.Force.Z));
                    store.Set(p.Drag!, drag);
                    store.Set(p.Deflection!, physics.WingDeflection(p.Id));
                    if (p.Horn != null && physics.LinkageOf(p.Id) is { } lk)
                    {
                        store.Set(p.Horn, lk.Horn * 180 / Math.PI);
                        store.Set(p.ServoLoad!, lk.ServoLoad);
                        store.Set(p.RodForce!, lk.RodForce);
                    }
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
        /// Wind-tunnel test: 3D lattice-Boltzmann flow around every moving part together, at a few instants of the
        /// recording. The parts are held where they were and air of the test speed is blown at them from the chosen
        /// direction (<see cref="AnalysisSettings.TunnelAir"/>). Result per keyframe: force on the assembly (N, world
        /// frame), its drag coefficient, the grid uncertainty and streamlines. The real air over time is <see cref="AirFlow"/>.
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
                // The first frame is also solved on a coarser grid for the grid-convergence estimate.
                var cfd = AeroFrame(world, parts, r, s, i, k == 0, cancel, p => progress?.Invoke((k + p) / keys));
                if (cfd != null) frames.Add(cfd);
            }

            return frames;
        }

        private static CfdResult? AeroFrame(WorldDocument world, List<PartGeometry> parts, AnalysisResult r, AnalysisSettings s, int sample,
            bool gridStudy, CancellationToken cancel, Action<double>? progress)
        {
            var store = r.Channels;
            var solids = new List<Solid>();
            foreach (var p in parts)
            {
                var pose = RecordedPose(store, p.ObjectId, sample);
                var pos = pose?.Position ?? p.Position;
                var q = pose?.Orientation ?? p.Orientation;
                // The part's solid is in its body frame; the body centre is offset from the object origin.
                solids.Add(p.Solid.Transformed(BodyCentre(p, pos, q), q * p.BodyTurn));
            }

            // Wind tunnel: the assembly is held at this instant's pose and the chosen air is blown at it.
            var rel = s.TunnelAir;
            double speed = rel.Length;
            if (speed < 1e-3) return null;
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

            double width = hi.Y - lo.Y, height = hi.Z - lo.Z;
            double d = Math.Max(Math.Max(width, height), 1e-3);
            int res = Math.Max(10, Math.Min(40, s.AeroResolution));
            double re = speed * d / AirViscosity;
            var thickness = parts.Select(p => Math.Min(p.Size.X, Math.Min(p.Size.Y, p.Size.Z))).ToList();
            double share = gridStudy ? 0.7 : 1;
            var flow = SolveAssembly(solids, thickness, dir, gy, gz, lo, hi, d, res, re, s, cancel, p => progress?.Invoke(p * share));
            if (flow == null) return null;
            double gci = double.NaN;
            Numerics.ConvergenceStudy? study = null;
            if (gridStudy)
            {
                // [convergence] two coarser grids (1.5× and 2.25× the cell): a three-grid study of Cd when the coarsest still
                // resolves the body, else Roache's two-grid index (safety factor 3, the lattice-Boltzmann method's order 2)
                int midRes = Math.Max(8, (int)Math.Round(res / 1.5)), lowRes = (int)Math.Round(res / 2.25);
                var coarse = SolveAssembly(solids, thickness, dir, gy, gz, lo, hi, d, midRes, re, s, cancel,
                    p => progress?.Invoke(share + p * (1 - share) * 0.6));
                var coarsest = coarse != null && lowRes >= 8 && lowRes < midRes
                    ? SolveAssembly(solids, thickness, dir, gy, gz, lo, hi, d, lowRes, re, s, cancel, p => progress?.Invoke(share + (1 - share) * (0.6 + 0.4 * p)))
                    : null;
                if (coarse != null && Math.Abs(flow.Cd) > 1e-9)
                {
                    if (coarsest != null && coarsest.Dx > coarse.Dx * 1.05 && coarse.Dx > flow.Dx * 1.05)
                    {
                        study = Numerics.GridConvergence.ThreeGrids(flow.Dx, flow.Cd, coarse.Dx, coarse.Cd, coarsest.Dx, coarsest.Cd);
                        gci = study.Kind is Numerics.ConvergenceKind.Monotonic or Numerics.ConvergenceKind.Oscillatory or Numerics.ConvergenceKind.Converged
                            ? study.GciFine
                            : Numerics.GridConvergence.TwoGrids(flow.Dx, flow.Cd, coarse.Dx, coarse.Cd);
                    }
                    else gci = Numerics.GridConvergence.TwoGrids(flow.Dx, flow.Cd, coarse.Dx, coarse.Cd);
                }
            }

            var lbm = flow.Lbm;
            double dx = flow.Dx, u = flow.U;
            var origin = flow.Origin;
            int nx = lbm.Nx, ny = lbm.Ny, nz = lbm.Nz;
            var fl = flow.Force;
            double rho = world.Environment.AirDensity ?? 1.2;
            double scale = rho * speed * speed * dx * dx / (u * u);   // lattice force → newtons
            var force = (dir * fl.X + gy * fl.Y + gz * fl.Z) * scale;
            double area = flow.Area * dx * dx;
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
                ReynoldsLattice = flow.ReLattice,
                Tau = flow.Tau,
                LatticeSpeed = u,
                AirDensity = rho,
                TimeStep = dx * u / speed,
                MeanCd = flow.Cd,
                MeanCl = flow.Cl,
                BlockageRatio = flow.Area / ((double)ny * nz),
                GridUncertainty = gci,
                Converged = flow.Converged,
                Speed3D = speedField,
                Solid = (bool[])lbm.Solid.Clone(),
                Origin = dir * origin.X + gy * origin.Y + gz * origin.Z,
                AxisX = dir,
                AxisY = gy,
                AxisZ = gz,
                Force = force
            };
            result.Notes.Add($"{parts.Count} parça birlikte, bağıl hava {speed:0.##} m/s, Re = {re:0}; hücre {dx * 1000:0.#} mm (boyda {d / dx:0} hücre), ağ {nx}×{ny}×{nz}.");
            result.Notes.Add(flow.CappedRe
                ? $"Gerçek Re'de çözüm kararsız kaldı; ağda Re {flow.ReLattice:0} + Smagorinsky ile çözüldü: sürükleme yaklaşıktır."
                : re > 1500 ? "Gerçek Reynolds sayısıyla çözüldü (düzenlenmiş çarpışma + Smagorinsky LES)." : "Gerçek Reynolds sayısıyla, türbülans modeli olmadan çözüldü.");
            result.Notes.Add($"Eğri duvar: {lbm.CurvedLinks} bağlantıda gerçek yüzey konumu kullanıldı (ikinci derece sınır). Tıkanma oranı %{result.BlockageRatio * 100:0.#}, yanlar açık sınır (ortam basıncı).");
            // Boundary layer against the cell: laminar Blasius below Re 5·10⁵, turbulent 1/7 power law above.
            bool turbulent = re > 5e5;
            double delta = turbulent ? 0.37 * d * Math.Pow(re, -0.2) : 5 * d / Math.Sqrt(Math.Max(re, 1));
            double cf = turbulent ? 0.0576 * Math.Pow(re, -0.2) : 0.664 / Math.Sqrt(Math.Max(re, 1));
            double yPlus = 0.5 * dx * speed * Math.Sqrt(cf / 2) / AirViscosity;
            result.WallYPlus = yPlus;
            result.Notes.Add(delta < 2 * dx
                ? $"Sınır tabaka ≈ {delta * 1000:0.##} mm, hücre {dx * 1000:0.#} mm (y+ ≈ {yPlus:0}): sınır tabaka çözülmüyor; yüzey sürtünmesi eksik, keskin kenarlı küt cisimlerin basınç sürüklemesi geçerlidir."
                : $"Sınır tabaka ≈ {delta * 1000:0.##} mm, en az iki hücre (y+ ≈ {yPlus:0}).");
            result.Notes.Add(flow.Converged ? "Kuvvet oturdu (son pencereler %1 içinde)." : "Kuvvet salınımlı (girdap kopması ya da süre yetmedi): son pencerelerin ortalaması.");
            if (study is { Kind: Numerics.ConvergenceKind.Monotonic } st)
                result.Notes.Add($"Izgara yakınsaması (üç ızgara): Cd {st.F3:0.###} → {st.F2:0.###} → {st.F1:0.###}, gözlenen mertebe {st.Order:0.0}, ızgaradan bağımsız Cd ≈ {st.Extrapolated:0.###}, belirsizlik (GCI) ±%{gci * 100:0.#}.");
            else if (study is { Kind: Numerics.ConvergenceKind.Divergent } sd)
                result.Notes.Add($"Izgara yakınsaması (üç ızgara): Cd {sd.F3:0.###} → {sd.F2:0.###} → {sd.F1:0.###}, fark ızgara inceldikçe büyüyor: henüz yakınsama bölgesinde değil (çözünürlüğü artırın); iki ızgaradan GCI ±%{gci * 100:0.#}.");
            else if (!double.IsNaN(gci)) result.Notes.Add($"Izgara belirsizliği (GCI{(study != null ? ", üç ızgara" : ", iki ızgara")}): Cd ±%{gci * 100:0.#}.");
            if (thickness.Any(t => t < 2 * dx)) result.Notes.Add("Hücreden ince yüzeyler bir hücre kalınlığında temsil edildi (kanat kuvvetleri kanat modelinden gelir).");
            Streamlines(lbm, result, (lo.Y + hi.Y) / 2 - origin.Y, (lo.Z + hi.Z) / 2 - origin.Z, d, u, speed);
            return result;
        }

        private sealed class AssemblyFlow
        {
            public Lbm3D Lbm = null!;
            public Vec3 Origin, Force;
            public double Dx, U, Tau, ReLattice, Area, Cd, Cl;
            public bool Converged, CappedRe;
        }

        /// <summary>
        /// One 3D flow solution around the assembly: cell-centred grid (x along the wind), solids by their signed distance
        /// (cells whose centre is inside; parts thinner than two cells conservatively), the true surface position on every
        /// fluid–solid link for the curved wall, an open far field 3.2 d across, 1.5 d upstream and 4 d behind, an absorbing
        /// layer along the edges. Runs in windows until the force settles. At the real Reynolds number; if that goes
        /// unstable, once more at Re 1500.
        /// </summary>
        private static AssemblyFlow? SolveAssembly(List<Solid> solids, List<double> thickness, Vec3 dir, Vec3 gy, Vec3 gz, Vec3 lo, Vec3 hi,
            double d, int res, double re, AnalysisSettings s, CancellationToken cancel, Action<double>? progress)
        {
            double length = hi.X - lo.X;
            double dx = d / res;
            const double maxCells = 3.2e6;
            int nx = (int)Math.Ceiling((length + 5.5 * d) / dx), ny = (int)Math.Ceiling(3.2 * d / dx);
            double cells = (double)nx * ny * ny;
            if (cells > maxCells)
            {
                dx *= Math.Pow(cells / maxCells, 1.0 / 3) * 1.01;
                nx = (int)Math.Ceiling((length + 5.5 * d) / dx);
                ny = (int)Math.Ceiling(3.2 * d / dx);
            }

            int nz = ny, n = nx * ny * nz;
            double u = 0.06;
            var origin = new Vec3(lo.X - 1.5 * d, (lo.Y + hi.Y) / 2 - ny * dx / 2, (lo.Z + hi.Z) / 2 - nz * dx / 2);
            var bounds = solids.Select(so => so.Bounds).ToArray();
            var solid = new bool[n];
            var dist = new float[n];
            Parallel.For(0, nz, z =>
            {
                for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    var l = origin + new Vec3((x + 0.5) * dx, (y + 0.5) * dx, (z + 0.5) * dx);
                    var w = dir * l.X + gy * l.Y + gz * l.Z;
                    double best = double.MaxValue;
                    bool inside = false;
                    for (int k = 0; k < solids.Count; k++)
                    {
                        // Far from a part's box only "outside" matters: the box distance stands in for the surface distance.
                        var b = bounds[k];
                        double ox = Math.Max(0, Math.Max(b.Min.X - w.X, w.X - b.Max.X));
                        double oy = Math.Max(0, Math.Max(b.Min.Y - w.Y, w.Y - b.Max.Y));
                        double oz = Math.Max(0, Math.Max(b.Min.Z - w.Z, w.Z - b.Max.Z));
                        double boxDist = Math.Sqrt(ox * ox + oy * oy + oz * oz);
                        double dk = boxDist > 2 * dx ? boxDist : solids[k].Distance(w);
                        if (dk < best) best = dk;
                        if (dk < 0 || (thickness[k] < 2 * dx && dk < 0.5 * dx)) inside = true;
                    }

                    int i = (z * ny + y) * nx + x;
                    solid[i] = inside;
                    dist[i] = (float)(best / dx);
                }
            });

            // Frontal area (cells): per column across the wind, the share of the cell the silhouette covers.
            double frontal = 0;
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            {
                double min = double.MaxValue;
                bool any = false;
                for (int x = 0; x < nx; x++)
                {
                    int i = (z * ny + y) * nx + x;
                    min = Math.Min(min, dist[i]);
                    any |= solid[i];
                }

                frontal += any && min >= 0 ? 1 : Math.Max(0, Math.Min(1, 0.5 - min));
            }

            frontal = Math.Max(frontal, 1);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                double reLat = attempt == 0 ? re : Math.Min(re, 1500);
                if (attempt == 1 && reLat >= re) break;
                double tau = 3 * u * (d / dx) / reLat + 0.5;
                var lbm = new Lbm3D(nx, ny, nz, tau, u)
                {
                    Smagorinsky = re > 1500 ? s.AeroSmagorinsky : 0,
                    FarField = LbmFarField.Open,
                    // Absorbing layer 0.2 d deep: at high Re the air barely damps pressure waves and eddies by itself.
                    Sponge = Math.Max(4, (int)Math.Round(0.2 * d / dx))
                };
                Array.Copy(solid, lbm.Solid, n);
                lbm.SetWallFractions((x, y, z, q) =>
                {
                    double d0 = dist[(z * ny + y) * nx + x];
                    double d1 = dist[((z + Lbm3D.Cz[q]) * ny + y + Lbm3D.Cy[q]) * nx + x + Lbm3D.Cx[q]];
                    // The signed distance is linear along a link across a flat face: Δ where it crosses zero.
                    return d0 > 0 && d1 < 0 ? d0 / (d0 - d1) : double.NaN;
                });

                int maxSteps = Math.Min(6000, Math.Max(600, (int)(Math.Max(2, s.AeroPasses / 5) * nx / u / 4)));
                int window = Math.Max(60, maxSteps / 10);
                var means = new List<Vec3>();
                Vec3 acc = Vec3.Zero;
                int count = 0;
                bool unstable = false, converged = false;
                for (int step = 0; step < maxSteps; step++)
                {
                    if (cancel.IsCancellationRequested) return null;
                    lbm.Step();
                    acc += new Vec3(lbm.BodyFx, lbm.BodyFy, lbm.BodyFz);
                    if (++count == window)
                    {
                        var m = acc / count;
                        acc = Vec3.Zero;
                        count = 0;
                        if (double.IsNaN(m.X) || double.IsInfinity(m.X) || m.Length > 10 * frontal)
                        {
                            unstable = true;
                            break;
                        }

                        means.Add(m);
                        int c = means.Count;
                        if (c >= 4 && step >= maxSteps / 3)
                        {
                            double tol = 0.01 * Math.Max(Math.Abs(means[c - 1].X), 1e-12);
                            if ((means[c - 1] - means[c - 2]).Length < tol && (means[c - 2] - means[c - 3]).Length < tol)
                            {
                                converged = true;
                                break;
                            }
                        }
                    }

                    if ((step & 63) == 0) progress?.Invoke((double)step / maxSteps);
                }

                if (unstable || means.Count == 0) continue;
                int take = Math.Max(2, means.Count / 3);
                take = Math.Min(take, means.Count);
                var f = Vec3.Zero;
                for (int k = means.Count - take; k < means.Count; k++) f += means[k];
                f /= take;
                return new AssemblyFlow
                {
                    Lbm = lbm,
                    Origin = origin,
                    Force = f,
                    Dx = dx,
                    U = u,
                    Tau = tau,
                    ReLattice = reLat,
                    Area = frontal,
                    Cd = f.X / (0.5 * u * u * frontal),
                    Cl = f.Z / (0.5 * u * u * frontal),
                    Converged = converged,
                    CappedRe = attempt == 1
                };
            }

            return null;
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
