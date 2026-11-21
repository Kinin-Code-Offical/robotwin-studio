using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RobotTwin.CoreSim.Analysis.Jobs;
using RobotTwin.CoreSim.Analysis.Recording;
using RobotTwin.CoreSim.Analysis.Thermal;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Analysis
{
    public static partial class AnalysisRun
    {
        /// <summary>FR-4 with typical copper: effective conductivity (W/m·K), ρ·c (J/m³·K), emissivity.</summary>
        private const double BoardK = 0.9, BoardRhoCp = 2.0e6, BoardEmissivity = 0.9;

        /// <summary>
        /// Board/part heating from the recorded dissipation of every device on it: each device's
        /// power ("P"/"PLOSS" channel, interpolated in time) heats the cells under its PCB footprint
        /// (or the part's centre for catalogue parts); the grid conducts in plane and through the
        /// thickness and loses heat by convection (h) and radiation. With <see cref="AnalysisSettings.ThermalExtend"/>
        /// the last second's average power keeps heating after the recording, e.g. to see the
        /// steady temperature of a regulator.
        /// </summary>
        public static List<ThermalResult> Thermal(WorldDocument world, PhysicsScene? scene, AnalysisResult result, AnalysisSettings s,
            CancellationToken cancel, Action<string, double>? progress = null, WorldPhysics? physics = null)
        {
            var list = new List<ThermalResult>();
            var power = PowerChannels(result);
            var ids = s.ThermalObjects.Count > 0
                ? s.ThermalObjects
                : power.Where(kv => kv.Value.Sum(c => MeanTail(c, result.Channels)) > 0.001).OrderByDescending(kv => kv.Value.Sum(c => MeanTail(c, result.Channels))).Select(kv => kv.Key).Take(4).ToList();
            foreach (var id in ids)
            {
                if (cancel.IsCancellationRequested) break;
                var o = world.Find(id);
                if (o == null || !power.TryGetValue(id, out var channels)) continue;
                var part = PartOf(world, scene, id, physics);
                list.Add(ThermalOf(world, o, part, channels, result, s, cancel, p => progress?.Invoke("Isı: " + o.Name, p)));
            }

            return list;
        }

        /// <summary>Recorded power channels per world object ("dev/O3/U1:P" → O3).</summary>
        private static Dictionary<string, List<Channel>> PowerChannels(AnalysisResult r)
        {
            var map = new Dictionary<string, List<Channel>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in r.Channels.Channels)
            {
                if (!c.Name.StartsWith("dev/", StringComparison.Ordinal) || c.Unit != "W") continue;
                string key = c.Name.Substring(4);
                int colon = key.LastIndexOf(':');
                string dev = colon > 0 ? key.Substring(0, colon) : key;
                string obj = dev.IndexOf('/') > 0 ? dev.Substring(0, dev.IndexOf('/')) : dev;
                if (!map.TryGetValue(obj, out var l)) map[obj] = l = new List<Channel>();
                l.Add(c);
            }

            return map;
        }

        private static double MeanTail(Channel c, ChannelStore store)
        {
            if (c.Count == 0) return 0;
            double t1 = store.Time(c.Count - 1);
            double sum = 0;
            int n = 0;
            for (int i = c.Count - 1; i >= 0 && store.Time(i) >= t1 - 1.0; i--)
            {
                if (!double.IsFinite(c[i])) continue;
                sum += Math.Abs(c[i]);
                n++;
            }

            return n > 0 ? sum / n : 0;
        }

        private static ThermalResult ThermalOf(WorldDocument world, WorldObject o, PartGeometry? part, List<Channel> channels, AnalysisResult r,
            AnalysisSettings s, CancellationToken cancel, Action<double>? progress)
        {
            var board = world.FindDesign(o.DesignId)?.Package.Schematic.Board;
            double sx, sy, sz;
            if (board != null)
            {
                sx = board.Width / 1000;
                sy = board.Height / 1000;
                sz = 0.0016;
            }
            else if (part != null)
            {
                sx = 2 * part.Size.X;
                sy = 2 * part.Size.Y;
                sz = Math.Max(0.001, 2 * part.Size.Z);
            }
            else
            {
                sx = Math.Max(o.SizeX, 0.01);
                sy = Math.Max(o.SizeY, 0.01);
                sz = Math.Max(o.SizeZ, 0.001);
            }

            double cell = Math.Max(s.ThermalCell, Math.Max(sx, sy) / 120);
            int nx = Math.Max(2, (int)Math.Ceiling(sx / cell)), ny = Math.Max(2, (int)Math.Ceiling(sy / cell));
            int nz = board != null ? 1 : Math.Clamp((int)Math.Round(sz / cell), 1, 12);
            var grid = new ThermalGrid(nx, ny, nz, sx / nx, sy / ny, sz / nz, Vec3.Zero)
            {
                AmbientC = world.Environment.AmbientC,
                H = s.ConvectionH
            };
            bool isBoard = board != null || o.Kind == WorldObjectKind.Board;
            if (isBoard) grid.SetMaterial(BoardK, BoardRhoCp, BoardEmissivity);
            else
            {
                var m = Fem.StructuralMaterial.For(o.Material);
                grid.SetMaterial(m.Conductivity, m.Density * m.SpecificHeat, m.Emissivity);
            }

            grid.SetUniformTemperature(world.Environment.AmbientC);
            var tr = new ThermalResult
            {
                ObjectId = o.Id,
                Name = o.Name,
                BodyOffset = part?.BodyOffset ?? Vec3.Zero,
                BodyTurn = part?.BodyTurn ?? Quat.Identity,
                Kind = isBoard ? "board" : "part",
                Nx = nx,
                Ny = ny,
                Nz = nz,
                Dx = grid.Dx,
                Dy = grid.Dy,
                Dz = grid.Dz,
                Origin = Vec3.Zero,
                Material = isBoard ? "FR-4" : o.Material
            };

            // Footprint of each heat source on the grid.
            var sources = new List<(Channel C, Vec3 Min, Vec3 Max, double Tail)>();
            foreach (var c in channels)
            {
                string key = c.Name.Substring(4);
                int colon = key.LastIndexOf(':');
                string dev = colon > 0 ? key.Substring(0, colon) : key;
                string refdes = dev.IndexOf('/') > 0 ? dev.Substring(dev.IndexOf('/') + 1) : string.Empty;
                var comp = board?.Components.FirstOrDefault(x => string.Equals(x.RefDes, refdes, StringComparison.OrdinalIgnoreCase));
                double cx = comp != null ? comp.X / 1000 : sx / 2, cy = comp != null ? sy - comp.Y / 1000 : sy / 2;
                double half = comp != null ? 0.0025 : Math.Min(sx, sy) / 6;
                var min = new Vec3(Math.Clamp(cx - half, 0, sx), Math.Clamp(cy - half, 0, sy), 0);
                var max = new Vec3(Math.Clamp(cx + half, 0, sx), Math.Clamp(cy + half, 0, sy), sz);
                double tail = MeanTail(c, r.Channels);
                sources.Add((c, min, max, tail));
                if (tail > 0.001) tr.Sources.Add((string.IsNullOrEmpty(refdes) ? dev : refdes, cx, cy));
            }

            var store = r.Channels;
            double recorded = store.Duration;
            double total = recorded + Math.Max(0, s.ThermalExtend);
            if (total <= 0) return tr;
            int frames = Math.Max(2, s.ThermalFrames);
            int steps = Math.Max(frames * 4, (int)Math.Ceiling(total / 0.05));
            steps = Math.Min(steps, 4000);
            double dt = total / steps;
            int sample = 0;
            for (int k = 1; k <= steps; k++)
            {
                if (cancel.IsCancellationRequested) break;
                double t = k * dt;
                grid.ClearSources();
                double p = 0;
                if (t <= recorded)
                {
                    while (sample + 1 < store.Count && store.Time(sample + 1) - store.Time(0) <= t) sample++;
                    foreach (var src in sources)
                    {
                        double w = sample < src.C.Count && double.IsFinite(src.C[sample]) ? Math.Abs(src.C[sample]) : 0;
                        if (w > 0) grid.AddSource(src.Min, src.Max, w);
                        p += w;
                    }
                }
                else
                {
                    foreach (var src in sources)
                    {
                        if (src.Tail > 0) grid.AddSource(src.Min, src.Max, src.Tail);
                        p += src.Tail;
                    }
                }

                grid.Step(dt, cancel);
                tr.History.Add((t, grid.MaxTemperature(), grid.MeanTemperature(), p));
                if (k % Math.Max(1, steps / frames) == 0 || k == steps)
                {
                    tr.Times.Add(t);
                    tr.Frames.Add(grid.Snapshot());
                }

                if ((k & 15) == 0) progress?.Invoke((double)k / steps);
            }

            tr.Notes.Add($"{nx}×{ny}×{nz} hücre ({grid.Dx * 1000:0.#}×{grid.Dy * 1000:0.#}×{grid.Dz * 1000:0.##} mm), {tr.Material}, h = {s.ConvectionH:0} W/m²K, ortam {world.Environment.AmbientC:0} °C, ışınım dahil.");
            if (s.ThermalExtend > 0) tr.Notes.Add($"Kayıttan sonra {s.ThermalExtend:0} s boyunca son 1 s'nin ortalama gücüyle ısıtmaya devam edildi.");
            if (board == null) tr.Notes.Add("Hazır parça: ısı kaynakları parçanın ortasına yayıldı.");
            return tr;
        }
    }
}
