using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[dfm] The limits of a typical PCB fab and SMT line that DRC does not cover (it checks copper rules).</summary>
    public sealed class DfmRules
    {
        /// <summary>Narrowest solder-mask dam a fab holds between two pads (mm); under it the pads share one opening.</summary>
        public double MinMaskWeb { get; set; } = 0.1;

        /// <summary>Tracks meeting at a sharper angle than this hold etchant in the corner (an acid trap), degrees.</summary>
        public double AcidTrapAngle { get; set; } = 90;

        /// <summary>SMD pads nearer the board edge than this need a panel rail for the assembly line's conveyor (mm).</summary>
        public double SmdEdgeClearance { get; set; } = 2.0;

        /// <summary>SMD pad pitch at or under which the placement machine needs fiducials to align (mm).</summary>
        public double FinePitch { get; set; } = 0.65;

        /// <summary>Smallest silkscreen text a fab prints legibly: height and stroke (mm).</summary>
        public double MinSilkHeight { get; set; } = 0.8;

        public double MinSilkStroke { get; set; } = 0.15;

        /// <summary>Distinct drill sizes over this make the board dearer (each is a tool change).</summary>
        public int MaxDrillSizes { get; set; } = 8;

        /// <summary>Smallest pad a test probe lands on reliably (mm), and the closest two probes may sit (mm).</summary>
        public double MinTestPad { get; set; } = 0.8;

        public double MinProbePitch { get; set; } = 1.27;
    }

    /// <summary>[dfm] What the manufacturability and testability check found.</summary>
    public sealed class DfmReport
    {
        public List<DrcViolation> Issues { get; } = new List<DrcViolation>();

        /// <summary>Nets a test probe can reach (a through-hole pad, a test point, a large bottom pad), and all nets with two or more pads.</summary>
        public int TestableNets { get; internal set; }

        public int Nets { get; internal set; }

        public List<string> Untestable { get; } = new List<string>();

        public double Coverage => Nets == 0 ? 1 : (double)TestableNets / Nets;
    }

    /// <summary>
    /// [dfm] Design for manufacture and test: the board as a fab and an assembly line see it. Acid traps where tracks meet
    /// sharply, solder-mask dams too narrow to hold between pads, vias in SMD pads that wick the solder away, SMD parts
    /// too near the edge for the conveyor, fine-pitch parts with no fiducials, two-pad parts with lopsided copper that
    /// tombstone, silkscreen text too small to print, too many drill sizes; and for test, which nets a bed-of-nails or a
    /// flying probe can reach and whether two probes would sit too close. Warnings, not errors: a board that breaks them
    /// can be made, at a cost or with a lower yield.
    /// </summary>
    public static class DfmChecker
    {
        private const double Eps = 1e-6;

        private static string Mm(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public static DfmReport Check(BoardDocument board, DfmRules? rules = null)
        {
            rules ??= new DfmRules();
            var report = new DfmReport();
            var issues = report.Issues;
            var pads = board.Pads().ToList();
            void Warn(string rule, string message, double x, double y, CopperLayer? layer = null) =>
                issues.Add(new DrcViolation(DrcSeverity.Warning, rule, message, x, y, layer));

            // ---------------------------------------------------------- acid traps
            var byEnd = new Dictionary<(string, CopperLayer, long, long), List<(Track T, double Dx, double Dy)>>();
            (long, long) Key(double x, double y) => ((long)Math.Round(x * 1000), (long)Math.Round(y * 1000));
            foreach (var t in board.Tracks)
            {
                double len = Math.Sqrt((t.X2 - t.X1) * (t.X2 - t.X1) + (t.Y2 - t.Y1) * (t.Y2 - t.Y1));
                if (len < Eps) continue;
                foreach (var (x, y, dx, dy) in new[] { (t.X1, t.Y1, (t.X2 - t.X1) / len, (t.Y2 - t.Y1) / len), (t.X2, t.Y2, (t.X1 - t.X2) / len, (t.Y1 - t.Y2) / len) })
                {
                    var (kx, ky) = Key(x, y);
                    var key = (t.Net, t.Layer, kx, ky);
                    if (!byEnd.TryGetValue(key, out var list)) byEnd[key] = list = new List<(Track, double, double)>();
                    list.Add((t, dx, dy));
                }
            }

            foreach (var (key, ends) in byEnd)
            {
                // A join on a pad or a via is filled by its copper; two tracks leaving the same way (a few degrees apart)
                // overlap into one solid strip. Neither leaves a wedge for the etchant.
                double jx = key.Item3 / 1000.0, jy = key.Item4 / 1000.0;
                if (pads.Any(p => string.Equals(p.Net, key.Item1, StringComparison.OrdinalIgnoreCase) && p.OnLayer(key.Item2)
                                  && Math.Abs(jx - p.X) <= p.Width / 2 + Eps && Math.Abs(jy - p.Y) <= p.Height / 2 + Eps)) continue;
                if (board.Vias.Any(v => (v.X - jx) * (v.X - jx) + (v.Y - jy) * (v.Y - jy) <= v.Diameter * v.Diameter / 4)) continue;
                double sharpest = 180;
                for (int i = 0; i < ends.Count; i++)
                    for (int j = i + 1; j < ends.Count; j++)
                    {
                        double cos = Math.Max(-1, Math.Min(1, ends[i].Dx * ends[j].Dx + ends[i].Dy * ends[j].Dy));
                        double angle = Math.Acos(cos) * 180 / Math.PI;
                        if (angle >= 5) sharpest = Math.Min(sharpest, angle);
                    }

                if (sharpest < rules.AcidTrapAngle - 1)
                    Warn("DFM · Asit tuzağı", $"{key.Item1} yolları {sharpest:0}° ile birleşiyor: {Mm(rules.AcidTrapAngle)}°'den dar köşede aşındırma sıvısı kalır, bakır incelir",
                        key.Item3 / 1000.0, key.Item4 / 1000.0, key.Item2);
            }

            // ---------------------------------------------------------- solder-mask dams between pads
            for (int i = 0; i < pads.Count; i++)
            {
                for (int j = i + 1; j < pads.Count; j++)
                {
                    var a = pads[i];
                    var b = pads[j];
                    if (Math.Abs(a.X - b.X) > 10 || Math.Abs(a.Y - b.Y) > 10) continue;
                    bool shareLayer = a.Pad.IsThroughHole || b.Pad.IsThroughHole || a.SmdLayer == b.SmdLayer;
                    if (!shareLayer || (a.Net.Length > 0 && string.Equals(a.Net, b.Net, StringComparison.OrdinalIgnoreCase))) continue;
                    double gap = DesignRuleChecker.PadPadDistance(a, b);
                    if (gap > Eps && gap < rules.MinMaskWeb - Eps)
                        Warn("DFM · Maske köprüsü", $"{a.Component.RefDes + "." + a.Pad.Name} ↔ {b.Component.RefDes + "." + b.Pad.Name}: pedler arası {Mm(gap)} mm, lehim maskesi en az {Mm(rules.MinMaskWeb)} mm'lik bir set tutar; açıklık birleşir, lehim köprü kurabilir",
                            (a.X + b.X) / 2, (a.Y + b.Y) / 2);
                }
            }

            // ---------------------------------------------------------- vias in SMD pads
            foreach (var via in board.Vias)
            {
                foreach (var p in pads.Where(p => !p.Pad.IsThroughHole))
                {
                    if (Math.Abs(via.X - p.X) <= p.Width / 2 && Math.Abs(via.Y - p.Y) <= p.Height / 2)
                    {
                        Warn("DFM · Pedde via", $"{p.Component.RefDes + "." + p.Pad.Name} pedinde {via.Net} viası: açık via lehimi emer; dolgulu ve kapatılmış (via-in-pad) via isteyin ya da viayı pedin dışına alın", via.X, via.Y, p.SmdLayer);
                        break;
                    }
                }
            }

            // ---------------------------------------------------------- SMD parts at the edge
            var outline = board.OutlinePolygon(0.005);
            double EdgeDistance(double x, double y)
            {
                double best = double.PositiveInfinity;
                for (int i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];
                    best = Math.Min(best, Geometry.PointSegmentDistance(x, y, a.X, a.Y, b.X, b.Y));
                }

                return best;
            }

            if (outline.Count >= 3)
            {
                foreach (var group in pads.Where(p => !p.Pad.IsThroughHole).GroupBy(p => p.Component))
                {
                    var nearest = group.Select(p => (P: p, D: EdgeDistance(p.X, p.Y) - Math.Max(p.Width, p.Height) / 2)).OrderBy(x => x.D).First();
                    if (nearest.D < rules.SmdEdgeClearance - Eps)
                        Warn("DFM · Kenara yakın SMD", $"{group.Key.RefDes} pedi kart kenarına {Mm(Math.Max(0, nearest.D))} mm: dizgi hattının taşıyıcısı kenardan {Mm(rules.SmdEdgeClearance)} mm tutar; panel kenar payı (ray) ekleyin",
                            nearest.P.X, nearest.P.Y, nearest.P.SmdLayer);
                }
            }

            // ---------------------------------------------------------- fiducials for fine pitch
            double finest = double.PositiveInfinity;
            BoardComponent? finePart = null;
            foreach (var group in pads.Where(p => !p.Pad.IsThroughHole).GroupBy(p => p.Component))
            {
                var list = group.ToList();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        double pitch = Math.Sqrt((list[i].X - list[j].X) * (list[i].X - list[j].X) + (list[i].Y - list[j].Y) * (list[i].Y - list[j].Y));
                        if (pitch > Eps && pitch < finest)
                        {
                            finest = pitch;
                            finePart = group.Key;
                        }
                    }
            }

            int fiducials = board.Components.Count(c => c.FootprintId.IndexOf("fiducial", StringComparison.OrdinalIgnoreCase) >= 0 || c.RefDes.StartsWith("FID", StringComparison.OrdinalIgnoreCase));
            if (finePart != null && finest <= rules.FinePitch + Eps && fiducials < 2)
                Warn("DFM · Fidüsyel yok", $"{finePart.RefDes} {Mm(finest)} mm adımlı; dizgi makinesi kartı fidüsyellerle hizalar: köşelere en az 2 (tercihen 3) fidüsyel ekleyin ({fiducials} var)", finePart.X, finePart.Y);

            // ---------------------------------------------------------- tombstoning: lopsided copper on two-pad parts
            foreach (var group in pads.Where(p => !p.Pad.IsThroughHole).GroupBy(p => p.Component))
            {
                var two = group.ToList();
                if (two.Count != 2) continue;
                double Copper(PlacedPad p) => board.Tracks.Where(t => t.Layer == p.SmdLayer && string.Equals(t.Net, p.Net, StringComparison.OrdinalIgnoreCase)
                                                                       && (DesignRuleChecker.PadSegmentDistance(p, t.X1, t.Y1, t.X1, t.Y1) <= Eps || DesignRuleChecker.PadSegmentDistance(p, t.X2, t.Y2, t.X2, t.Y2) <= Eps))
                                                          .Select(t => t.Width).DefaultIfEmpty(0).Max()
                                                      + (board.Zones.Any(z => z.Layer == p.SmdLayer && string.Equals(z.Net, p.Net, StringComparison.OrdinalIgnoreCase) && z.PadConnection == ZonePadConnection.Solid) ? 10 : 0);
                double ca = Copper(two[0]), cb = Copper(two[1]);
                double big = Math.Max(ca, cb), small = Math.Min(ca, cb);
                if (big >= 0.8 && big >= 3 * Math.Max(small, 0.1))
                {
                    var hot = ca > cb ? two[0] : two[1];
                    Warn("DFM · Mezar taşı riski", $"{group.Key.RefDes}: {hot.Pad.Name} pedi çok daha geniş bakıra bağlı; fırında iki ped aynı anda erimez, parça bir ucunda dikilebilir (ısıl kabartma ya da eşit yollar)",
                        hot.X, hot.Y, hot.SmdLayer);
                }
            }

            // ---------------------------------------------------------- silkscreen legibility
            foreach (var text in board.Texts.Where(t => t.Layer == GraphicLayer.SilkTop || t.Layer == GraphicLayer.SilkBottom))
            {
                if (text.Size < rules.MinSilkHeight - Eps || text.Thickness < rules.MinSilkStroke - Eps)
                    Warn("DFM · Okunmaz serigrafi", $"\"{text.Text}\": {Mm(text.Size)} mm yüksek, {Mm(text.Thickness)} mm çizgi; en az {Mm(rules.MinSilkHeight)} mm ve {Mm(rules.MinSilkStroke)} mm basılır", text.X, text.Y);
            }

            // ---------------------------------------------------------- drill sizes
            var drills = board.Vias.Select(v => v.Drill).Concat(pads.Where(p => p.Pad.IsThroughHole).Select(p => p.Pad.Drill)).Concat(board.MountingHoles.Select(h => h.Drill))
                .Select(d => Math.Round(d, 2)).Distinct().OrderBy(d => d).ToList();
            if (drills.Count > rules.MaxDrillSizes)
                Warn("DFM · Delik çeşidi", $"{drills.Count} farklı delik çapı ({string.Join(", ", drills.Select(Mm))} mm): her biri bir takım değişimi; birleştirmek maliyeti düşürür", 0, 0);

            // ---------------------------------------------------------- test access (DFT)
            var nets = pads.Where(p => p.Net.Length > 0).GroupBy(p => p.Net, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2).ToList();
            report.Nets = nets.Count;
            var probes = new List<(string Net, double X, double Y)>();
            foreach (var net in nets)
            {
                // A probe lands on: a plated through-hole pad (from the bottom), a test point part, a bottom SMD pad large enough.
                var access = net.Where(p => p.Pad.IsThroughHole && !p.Pad.NonPlated)
                    .Concat(net.Where(p => p.Component.FootprintId.IndexOf("testpoint", StringComparison.OrdinalIgnoreCase) >= 0 || p.Component.RefDes.StartsWith("TP", StringComparison.OrdinalIgnoreCase)))
                    .Concat(net.Where(p => !p.Pad.IsThroughHole && p.SmdLayer == CopperLayer.Bottom && Math.Min(p.Width, p.Height) >= rules.MinTestPad - Eps))
                    .ToList();
                if (access.Count == 0)
                {
                    report.Untestable.Add(net.Key);
                    var first = net.First();
                    Warn("DFT · Test erişimi yok", $"{net.Key} hattına test probu değecek yer yok (delikli ped, test noktası ya da alt yüzde en az {Mm(rules.MinTestPad)} mm ped): test noktası ekleyin", first.X, first.Y);
                    continue;
                }

                report.TestableNets++;
                probes.Add((net.Key, access[0].X, access[0].Y));
            }

            for (int i = 0; i < probes.Count; i++)
                for (int j = i + 1; j < probes.Count; j++)
                {
                    double d = Math.Sqrt((probes[i].X - probes[j].X) * (probes[i].X - probes[j].X) + (probes[i].Y - probes[j].Y) * (probes[i].Y - probes[j].Y));
                    if (d < rules.MinProbePitch - Eps)
                        Warn("DFT · Prob aralığı", $"{probes[i].Net} ve {probes[j].Net} test noktaları {Mm(d)} mm aralıklı; iğneli fikstür en az {Mm(rules.MinProbePitch)} mm ister",
                            (probes[i].X + probes[j].X) / 2, (probes[i].Y + probes[j].Y) / 2);
                }

            return report;
        }
    }
}
