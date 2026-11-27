using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[impedance] A track's characteristic impedance on its layer of the stackup.</summary>
    public readonly struct TrackImpedance
    {
        public TrackImpedance(double z0, double epsEff, bool stripline, double height, double heightOther)
        {
            Z0 = z0;
            EpsEff = epsEff;
            Stripline = stripline;
            Height = height;
            HeightOther = heightOther;
        }

        /// <summary>Single-ended characteristic impedance (Ω).</summary>
        public double Z0 { get; }

        /// <summary>The permittivity the signal sees (εeff; εr for a stripline).</summary>
        public double EpsEff { get; }

        /// <summary>Between two planes (an inner layer), or over one (an outer layer, a microstrip).</summary>
        public bool Stripline { get; }

        /// <summary>Dielectric to the reference plane (mm): under a microstrip, above a stripline.</summary>
        public double Height { get; }

        /// <summary>A stripline's dielectric to the plane below (mm); 0 for a microstrip.</summary>
        public double HeightOther { get; }

        public string Kind => Stripline ? "şerit hat" : "mikroşerit";
    }

    /// <summary>
    /// [impedance] Characteristic impedance from the board's stackup, by the closed forms fab calculators use:
    /// <list type="bullet">
    /// <item>an outer-layer track over the next copper layer (a microstrip): Hammerstad and Jensen (1980), with the
    /// copper's thickness folded into an effective width; within about 1 % for 0.1 ≤ w/h ≤ 10;</item>
    /// <item>an inner-layer track between the copper layers above and below it (a stripline): Wheeler's (1978) formula
    /// with thickness; when the two dielectrics differ (prepreg on one side, core on the other) the two symmetric lines
    /// in parallel (the offset-stripline approximation);</item>
    /// <item>an edge-coupled pair: IPC-2141's coupling factors on the single-ended value.</item>
    /// </list>
    /// The solder mask over an outer track lowers it by 1–3 Ω and is not included; the layers next to a track are taken
    /// as solid reference planes (a pour), as a controlled-impedance stackup has them.
    /// </summary>
    public static class Impedance
    {
        private const double Eta0 = 376.730313668;   // Ω, free space

        /// <summary>Microstrip: width <paramref name="w"/>, height <paramref name="h"/> over the plane, copper thickness <paramref name="t"/> (mm).</summary>
        public static (double Z0, double EpsEff) Microstrip(double w, double h, double t, double er)
        {
            w = Math.Max(1e-6, w);
            h = Math.Max(1e-6, h);
            double u = w / h, tn = Math.Max(0, t) / h, u1 = u, ur = u;
            if (tn > 0)
            {
                double du1 = tn / Math.PI * Math.Log(1 + 4 * Math.E / (tn * Coth2(Math.Sqrt(6.517 * u))));
                double dur = 0.5 * (1 + 1 / Math.Cosh(Math.Sqrt(Math.Max(0, er - 1)))) * du1;
                u1 = u + du1;
                ur = u + dur;
            }

            double eEff = EpsEff0(ur, er) * Math.Pow(Z01(u1) / Z01(ur), 2);
            double z0 = Z01(ur) / Math.Sqrt(EpsEff0(ur, er));
            return (z0, eEff);
        }

        /// <summary>The zero-thickness microstrip's impedance in air at width ratio u (Hammerstad–Jensen).</summary>
        private static double Z01(double u)
        {
            double f = 6 + (2 * Math.PI - 6) * Math.Exp(-Math.Pow(30.666 / u, 0.7528));
            return Eta0 / (2 * Math.PI) * Math.Log(f / u + Math.Sqrt(1 + 4 / (u * u)));
        }

        private static double EpsEff0(double u, double er)
        {
            double a = 1 + Math.Log((Math.Pow(u, 4) + Math.Pow(u / 52, 2)) / (Math.Pow(u, 4) + 0.432)) / 49 + Math.Log(1 + Math.Pow(u / 18.1, 3)) / 18.7;
            double b = 0.564 * Math.Pow((er - 0.9) / (er + 3), 0.053);
            return (er + 1) / 2 + (er - 1) / 2 * Math.Pow(1 + 10 / u, -a * b);
        }

        private static double Coth2(double x)
        {
            double c = 1 / Math.Tanh(x);
            return c * c;
        }

        /// <summary>Symmetric stripline: width <paramref name="w"/>, plane-to-plane spacing <paramref name="b"/>, thickness <paramref name="t"/> (mm); Wheeler.</summary>
        public static double Stripline(double w, double b, double t, double er)
        {
            w = Math.Max(1e-6, w);
            t = Math.Max(0, Math.Min(t, b * 0.9));
            double x = t / b, bt = b - t;
            double dw = 0;
            if (x > 1e-9)
            {
                double m = 2 / (1 + 2.0 / 3.0 * x / (1 - x));
                dw = x / (Math.PI * (1 - x)) * (1 - 0.5 * Math.Log(Math.Pow(x / (2 - x), 2) + Math.Pow(0.0796 * x / (w / b + 1.1 * x), m)));
            }

            double wp = w / bt + dw;   // W'/(b − t)
            double k = 8 / Math.PI / wp;
            return 30 / Math.Sqrt(er) * Math.Log(1 + 4 / Math.PI / wp * (k + Math.Sqrt(k * k + 6.27)));
        }

        /// <summary>
        /// An offset stripline (dielectric <paramref name="h1"/> to the plane above, <paramref name="h2"/> to the one below):
        /// the symmetric lines of 2·h1 + t and 2·h2 + t in parallel.
        /// </summary>
        public static double OffsetStripline(double w, double h1, double h2, double t, double er)
        {
            double z1 = Stripline(w, 2 * h1 + t, t, er), z2 = Stripline(w, 2 * h2 + t, t, er);
            return 2 * z1 * z2 / (z1 + z2);
        }

        /// <summary>An edge-coupled pair's differential impedance from the single-ended one and the gap <paramref name="s"/> (IPC-2141).</summary>
        public static double Differential(double z0, double s, TrackImpedance line) => line.Stripline
            ? 2 * z0 * (1 - 0.347 * Math.Exp(-2.9 * s / (line.Height + line.HeightOther)))
            : 2 * z0 * (1 - 0.48 * Math.Exp(-0.96 * s / line.Height));

        /// <summary>A track of width <paramref name="width"/> (mm) on <paramref name="layer"/> of <paramref name="board"/>'s stackup.</summary>
        public static TrackImpedance OfTrack(BoardDocument board, CopperLayer layer, double width)
        {
            var stack = board.Stackup;
            var built = stack.Build(board.Rules.CopperThicknessUm);
            int i = built.FindIndex(l => l.Copper == layer);
            if (i < 0) i = 0;
            double t = built[i].Thickness, er = stack.DielectricConstant;
            double above = i - 1 >= 0 ? built[i - 1].Thickness : 0, below = i + 1 < built.Count ? built[i + 1].Thickness : 0;
            if (PcbLayers.IsOuter(layer))
            {
                double h = layer == CopperLayer.Top ? below : above;
                if (h <= 0) h = stack.Thickness;
                var (z0, ee) = Microstrip(width, h, t, er);
                return new TrackImpedance(z0, ee, false, h, 0);
            }

            return new TrackImpedance(OffsetStripline(width, above, below, t, er), er, true, above, below);
        }

        /// <summary>The width that gives <paramref name="target"/> Ω on a layer (bisection; the impedance falls as the track widens).</summary>
        public static double WidthFor(BoardDocument board, CopperLayer layer, double target)
        {
            double lo = 0.02, hi = 20;
            for (int k = 0; k < 60; k++)
            {
                double mid = Math.Sqrt(lo * hi);
                if (OfTrack(board, layer, mid).Z0 > target) lo = mid;
                else hi = mid;
            }

            return Math.Sqrt(lo * hi);
        }

        /// <summary>
        /// The gap that gives a pair of <paramref name="width"/> tracks <paramref name="target"/> Ω differential on a layer; NaN
        /// when no gap does (the pair is two independent lines at best: 2·Z0 is its ceiling, coupling only lowers it).
        /// </summary>
        public static double GapFor(BoardDocument board, CopperLayer layer, double width, double target)
        {
            var line = OfTrack(board, layer, width);
            if (Differential(line.Z0, 0.02, line) > target || Differential(line.Z0, 50, line) < target) return double.NaN;
            double lo = 0.02, hi = 50;
            for (int k = 0; k < 60; k++)
            {
                double mid = Math.Sqrt(lo * hi);
                if (Differential(line.Z0, mid, line) < target) lo = mid;
                else hi = mid;
            }

            return Math.Sqrt(lo * hi);
        }

        /// <summary>Fab houses hold controlled impedance to ±10 %; past that the design misses, not the fab.</summary>
        public const double Tolerance = 0.10;

        /// <summary>
        /// [impedance] DRC: each track of a class with a target, at its width on its layer, and each pair with a target, at its
        /// width and gap on the layers it runs; one finding per net, layer and width, at the first such track.
        /// </summary>
        public static List<DrcViolation> Check(BoardDocument board)
        {
            var issues = new List<DrcViolation>();
            if (!board.NetClasses.Any(c => c.Impedance > 0) && !board.DiffPairs.Any(p => p.Impedance > 0)) return issues;
            var seen = new HashSet<(string, CopperLayer, double)>();
            foreach (var t in board.Tracks)
            {
                var cls = board.ClassFor(t.Net);
                if (cls.Impedance <= 0 || !seen.Add((t.Net, t.Layer, Math.Round(t.Width, 4)))) continue;
                var z = OfTrack(board, t.Layer, t.Width);
                if (Math.Abs(z.Z0 - cls.Impedance) <= Tolerance * cls.Impedance) continue;
                double w = WidthFor(board, t.Layer, cls.Impedance);
                issues.Add(new DrcViolation(DrcSeverity.Warning, "Empedans",
                    string.Format(CultureInfo.CurrentCulture, "{0}: {1:0.###} mm yol {2} katmanında {3:0.#} Ω ({4} sınıfı {5:0.#} Ω ister: {6:0.###} mm)",
                        t.Net, t.Width, PcbLayers.DisplayName(t.Layer), z.Z0, cls.Name, cls.Impedance, w),
                    (t.X1 + t.X2) / 2, (t.Y1 + t.Y2) / 2, t.Layer) { Net = t.Net });
            }

            foreach (var p in board.DiffPairs.Where(p => p.Impedance > 0))
            {
                foreach (var t in board.Tracks.Where(t => string.Equals(t.Net, p.Positive, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!seen.Add(("pair:" + p.Positive, t.Layer, Math.Round(t.Width, 4)))) continue;
                    var line = OfTrack(board, t.Layer, t.Width);
                    double zd = Differential(line.Z0, p.Gap, line);
                    if (Math.Abs(zd - p.Impedance) <= Tolerance * p.Impedance) continue;
                    double gap = GapFor(board, t.Layer, t.Width, p.Impedance);
                    string fix = double.IsNaN(gap)
                        ? string.Format(CultureInfo.CurrentCulture, "bu genişlikle aralık yetmez; {0:0.###} mm yol gerekir", PairWidthFor(board, t.Layer, p.Gap, p.Impedance))
                        : string.Format(CultureInfo.CurrentCulture, "{0:0.###} mm aralık gerekir", gap);
                    issues.Add(new DrcViolation(DrcSeverity.Warning, "Fark empedansı",
                        string.Format(CultureInfo.CurrentCulture, "{0}: {1:0.###} mm yol, {2:0.###} mm aralık {3} katmanında {4:0.#} Ω (hedef {5:0.#} Ω; {6})",
                            p, t.Width, p.Gap, PcbLayers.DisplayName(t.Layer), zd, p.Impedance, fix),
                        (t.X1 + t.X2) / 2, (t.Y1 + t.Y2) / 2, t.Layer) { Net = t.Net });
                }
            }

            return issues;
        }

        /// <summary>The track width that gives a pair <paramref name="target"/> Ω differential at gap <paramref name="gap"/>.</summary>
        public static double PairWidthFor(BoardDocument board, CopperLayer layer, double gap, double target)
        {
            double lo = 0.02, hi = 20;
            for (int k = 0; k < 60; k++)
            {
                double mid = Math.Sqrt(lo * hi);
                var line = OfTrack(board, layer, mid);
                if (Differential(line.Z0, gap, line) > target) lo = mid;
                else hi = mid;
            }

            return Math.Sqrt(lo * hi);
        }

        /// <summary>[fab-ready] Targets met (or the computed values, when the board asks for none).</summary>
        public static ReadinessItem Readiness(BoardDocument board)
        {
            const string name = "Empedans kontrolü";
            bool targets = board.NetClasses.Any(c => c.Impedance > 0) || board.DiffPairs.Any(p => p.Impedance > 0);
            string computed = Summary(board);
            if (!targets)
                return new ReadinessItem(name, ReadinessState.Pass, "Kontrollü empedans istenmedi; katman yapısından hesaplanan: " + computed);
            var misses = Check(board);
            string asked = string.Join(" · ", board.NetClasses.Where(c => c.Impedance > 0).Select(c => string.Format(CultureInfo.CurrentCulture, "{0}: {1:0} Ω", c.Name, c.Impedance))
                .Concat(board.DiffPairs.Where(p => p.Impedance > 0).Select(p => string.Format(CultureInfo.CurrentCulture, "{0}: {1:0} Ω fark", p, p.Impedance))));
            return misses.Count == 0
                ? new ReadinessItem(name, ReadinessState.Pass, $"Hedefler tutuyor (±%{Tolerance * 100:0}): {asked}. Üreticiye kontrollü empedans siparişinde bu değerleri ve katman yapısını bildirin")
                : new ReadinessItem(name, ReadinessState.Warning, $"{misses.Count} yerde hedef tutmuyor ({asked}); ilki: {misses[0].Message}");
        }

        /// <summary>"52,3 Ω mikroşerit (εeff 3,4)" / "48,1 Ω şerit hat".</summary>
        public static string Describe(TrackImpedance z) => z.Stripline
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} Ω {1}", z.Z0, z.Kind)
            : string.Format(CultureInfo.CurrentCulture, "{0:0.0} Ω {1} (εeff {2:0.00})", z.Z0, z.Kind, z.EpsEff);

        /// <summary>[fab-ready] Each net class's impedance on the outer and the first inner layer, and each pair's differential one.</summary>
        public static string Summary(BoardDocument board)
        {
            var parts = new List<string>();
            var layers = board.CopperLayers().ToList();
            var inner = layers.FirstOrDefault(l => !PcbLayers.IsOuter(l));
            foreach (var c in board.NetClasses)
            {
                var top = OfTrack(board, CopperLayer.Top, c.TrackWidth);
                string text = string.Format(CultureInfo.CurrentCulture, "{0} ({1:0.###} mm): dış {2:0} Ω", c.Name, c.TrackWidth, top.Z0);
                if (layers.Count > 2) text += string.Format(CultureInfo.CurrentCulture, ", iç {0:0} Ω", OfTrack(board, inner, c.TrackWidth).Z0);
                parts.Add(text);
            }

            foreach (var p in board.DiffPairs)
            {
                var cls = board.ClassFor(p.Positive);
                var line = OfTrack(board, CopperLayer.Top, cls.TrackWidth);
                parts.Add(string.Format(CultureInfo.CurrentCulture, "{0}: fark {1:0} Ω ({2:0.###} mm aralık)", p, Differential(line.Z0, p.Gap, line), p.Gap));
            }

            return string.Join(" · ", parts);
        }
    }
}
