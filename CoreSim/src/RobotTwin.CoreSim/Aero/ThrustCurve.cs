using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace RobotTwin.CoreSim.Aero
{
    /// <summary>
    /// [aero] A solid rocket motor thrust curve with its masses, read from the two standard hobby
    /// formats: RASP .eng (text) and RockSim .rse (XML). Propellant burn-down follows the delivered
    /// impulse, m_p(t) = m_p0·(1 − I(t)/I_total) (the RASP/OpenRocket convention), unless the .rse file
    /// carries its own mass column.
    /// </summary>
    public sealed class ThrustCurve
    {
        private double[] _t = Array.Empty<double>();
        private double[] _f = Array.Empty<double>();
        private double[] _impulse = Array.Empty<double>();
        private double[]? _mass;

        public string Designation { get; private set; } = string.Empty;
        public string Manufacturer { get; private set; } = string.Empty;

        /// <summary>Case diameter and length (m).</summary>
        public double Diameter { get; private set; }

        public double Length { get; private set; }

        /// <summary>Available ejection delays (s); 0 / "P" = plugged (no ejection charge).</summary>
        public IReadOnlyList<double> Delays { get; private set; } = Array.Empty<double>();

        public double PropellantMass { get; private set; }

        /// <summary>Loaded (initial) motor mass (kg).</summary>
        public double TotalMass { get; private set; }

        public double CaseMass => Math.Max(0, TotalMass - PropellantMass);

        /// <summary>Where the data came from (shown with the motor).</summary>
        public string Source { get; set; } = string.Empty;

        public double BurnTime => _t.Length == 0 ? 0 : _t[_t.Length - 1];
        public double TotalImpulse => _impulse.Length == 0 ? 0 : _impulse[_impulse.Length - 1];
        public double MaxThrust => _f.Length == 0 ? 0 : _f.Max();
        public double AverageThrust => BurnTime > 0 ? TotalImpulse / BurnTime : 0;

        /// <summary>Delivered specific impulse I/(m_p·g₀) (s).</summary>
        public double SpecificImpulse => PropellantMass > 0 ? TotalImpulse / (PropellantMass * 9.80665) : 0;

        /// <summary>NAR/TRA impulse class letter from the total impulse (A: 1.26–2.5 N·s, each next letter doubles).</summary>
        public string ImpulseClass
        {
            get
            {
                double i = TotalImpulse;
                if (i <= 0.3125) return "1/4A";
                if (i <= 0.625) return "1/2A";
                double top = 2.5;
                for (char c = 'A'; c <= 'O'; c++, top *= 2) if (i <= top) return c.ToString();
                return "O+";
            }
        }

        public IReadOnlyList<(double T, double F)> Points => _t.Select((t, i) => (t, _f[i])).ToList();

        /// <summary>Thrust (N) at time since ignition (s), linear between points.</summary>
        public double Thrust(double t)
        {
            if (t <= 0 || _t.Length == 0 || t >= _t[_t.Length - 1]) return 0;
            int i = Index(t);
            double u = (t - _t[i]) / Math.Max(1e-12, _t[i + 1] - _t[i]);
            return _f[i] + u * (_f[i + 1] - _f[i]);
        }

        /// <summary>Impulse delivered up to time t (N·s), exact for the piecewise-linear curve.</summary>
        public double ImpulseAt(double t)
        {
            if (t <= 0 || _t.Length == 0) return 0;
            if (t >= _t[_t.Length - 1]) return TotalImpulse;
            int i = Index(t);
            double dt = t - _t[i];
            double f1 = Thrust(t);
            return _impulse[i] + 0.5 * (_f[i] + f1) * dt;
        }

        /// <summary>Remaining propellant mass (kg) at time since ignition.</summary>
        public double PropellantAt(double t)
        {
            if (t <= 0) return PropellantMass;
            if (_mass != null && _t.Length > 1)
            {
                if (t >= _t[_t.Length - 1]) return _mass[_mass.Length - 1];
                int i = Index(t);
                double u = (t - _t[i]) / Math.Max(1e-12, _t[i + 1] - _t[i]);
                return _mass[i] + u * (_mass[i + 1] - _mass[i]);
            }

            return TotalImpulse <= 0 ? 0 : PropellantMass * (1 - ImpulseAt(t) / TotalImpulse);
        }

        /// <summary>Motor mass (case + remaining propellant) at time since ignition.</summary>
        public double MassAt(double t) => CaseMass + PropellantAt(t);

        private int Index(double t)
        {
            int lo = 0, hi = _t.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_t[mid] <= t) lo = mid;
                else hi = mid;
            }

            return lo;
        }

        private void SetPoints(List<(double T, double F)> pts, List<double>? mass = null)
        {
            if (pts.Count == 0 || pts[0].T > 0)
            {
                pts.Insert(0, (0, 0));
                mass?.Insert(0, mass.Count > 0 ? mass[0] : PropellantMass);
            }

            _t = pts.Select(p => p.T).ToArray();
            _f = pts.Select(p => Math.Max(0, p.F)).ToArray();
            _impulse = new double[_t.Length];
            for (int i = 1; i < _t.Length; i++) _impulse[i] = _impulse[i - 1] + 0.5 * (_f[i] + _f[i - 1]) * (_t[i] - _t[i - 1]);
            _mass = mass != null && mass.Count == _t.Length ? mass.ToArray() : null;
        }

        private static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static IReadOnlyList<double> ParseDelays(string s) =>
            s.Split(new[] { '-', ',' }, StringSplitOptions.RemoveEmptyEntries)
             .Select(x => x.Trim().ToUpperInvariant() == "P" ? 0 : double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0)
             .ToList();

        /// <summary>
        /// RASP .eng: ';' comments, a header "designation diameter(mm) length(mm) delays propellant(kg) total(kg) manufacturer",
        /// then "time thrust" pairs (s, N). The first motor of the file is read.
        /// </summary>
        public static ThrustCurve ParseEng(string text)
        {
            var c = new ThrustCurve();
            var pts = new List<(double, double)>();
            bool header = false;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw;
                int sc = line.IndexOf(';');
                if (sc >= 0) line = line.Substring(0, sc);
                line = line.Trim();
                if (line.Length == 0) continue;
                var f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (!header)
                {
                    if (f.Length < 7) throw new FormatException("ENG başlık satırı 7 alan içermeli: ad çap uzunluk gecikmeler yakıt_kg toplam_kg üretici");
                    c.Designation = f[0];
                    c.Diameter = D(f[1]) / 1000;
                    c.Length = D(f[2]) / 1000;
                    c.Delays = ParseDelays(f[3]);
                    c.PropellantMass = D(f[4]);
                    c.TotalMass = D(f[5]);
                    c.Manufacturer = string.Join(" ", f.Skip(6));
                    header = true;
                    continue;
                }

                if (f.Length < 2) continue;
                if (!double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double t)) break;   // next motor
                double thrust = D(f[1]);
                pts.Add((t, thrust));
                if (thrust == 0 && t > 0) break;
            }

            if (!header || pts.Count < 2) throw new FormatException("ENG dosyasında itki eğrisi bulunamadı.");
            c.SetPoints(pts);
            return c;
        }

        /// <summary>
        /// RockSim .rse: &lt;engine code mfg dia(mm) len(mm) initWt(g) propWt(g) delays&gt; with
        /// &lt;eng-data t f [m]&gt; rows (s, N, propellant g).
        /// </summary>
        public static ThrustCurve ParseRse(string xml)
        {
            var doc = XDocument.Parse(xml);
            var e = doc.Descendants("engine").FirstOrDefault() ?? throw new FormatException("RSE dosyasında <engine> yok.");
            string A(string n) => (string?)e.Attribute(n) ?? string.Empty;
            double Ad(string n) => double.TryParse(A(n), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
            var c = new ThrustCurve
            {
                Designation = A("code"),
                Manufacturer = A("mfg"),
                Diameter = Ad("dia") / 1000,
                Length = Ad("len") / 1000,
                PropellantMass = Ad("propWt") / 1000,
                TotalMass = Ad("initWt") / 1000,
                Delays = ParseDelays(A("delays"))
            };
            var pts = new List<(double, double)>();
            var mass = new List<double>();
            bool hasMass = true;
            foreach (var d in e.Descendants("eng-data"))
            {
                double t = double.Parse((string?)d.Attribute("t") ?? "0", CultureInfo.InvariantCulture);
                double f = double.Parse((string?)d.Attribute("f") ?? "0", CultureInfo.InvariantCulture);
                pts.Add((t, f));
                if (d.Attribute("m") is { } m) mass.Add(double.Parse(m.Value, CultureInfo.InvariantCulture) / 1000);
                else hasMass = false;
            }

            if (pts.Count < 2) throw new FormatException("RSE dosyasında itki verisi yok.");
            c.SetPoints(pts, hasMass ? mass : null);
            return c;
        }

        /// <summary>Reads .eng or .rse by content.</summary>
        public static ThrustCurve Parse(string text) => text.TrimStart().StartsWith("<", StringComparison.Ordinal) ? ParseRse(text) : ParseEng(text);

        public static ThrustCurve Load(string path)
        {
            var c = Parse(System.IO.File.ReadAllText(path));
            c.Source = System.IO.Path.GetFileName(path);
            return c;
        }
    }

    /// <summary>
    /// [aero] Built-in thrust curves. The Estes A8, C6 and D12 curves are the RASP .eng data published by
    /// Estes / NAR certification and redistributed on ThrustCurve.org, entered here from that public data
    /// (verify against the official files for anything beyond simulation). The 38 mm reload is a
    /// SYNTHETIC example (not a real product): a BATES-grain-like APCP curve, ≈ 350 N·s, Isp ≈ 190 s,
    /// shipped as .rse to exercise that format.
    /// </summary>
    public static class MotorLibrary
    {
        public const string EstesA8 = @"; Estes A8 (RASP format, NAR certification data, via ThrustCurve.org)
A8 18 70 3-5 0.0033 0.0163 Estes
0.041 0.512
0.084 2.115
0.127 4.358
0.166 6.794
0.192 8.588
0.206 9.294
0.226 9.730
0.236 8.845
0.247 7.179
0.261 5.063
0.277 3.717
0.306 3.205
0.351 2.884
0.405 2.499
0.467 2.371
0.532 2.307
0.589 2.371
0.632 2.371
0.652 2.243
0.668 1.794
0.684 1.153
0.703 0.448
0.730 0.000
";

        public const string EstesC6 = @"; Estes C6 (RASP format, NAR certification data, via ThrustCurve.org)
C6 18 70 0-3-5-7 0.0108 0.0231 Estes
0.031 0.946
0.092 4.826
0.139 9.936
0.192 14.090
0.209 11.446
0.231 7.381
0.248 6.151
0.292 5.489
0.370 4.921
0.475 4.448
0.671 4.258
0.702 4.542
0.723 4.164
0.850 4.448
1.063 4.353
1.211 4.353
1.242 4.069
1.303 4.258
1.468 4.353
1.656 4.448
1.821 4.448
1.834 2.933
1.847 1.325
1.860 0.000
";

        public const string EstesD12 = @"; Estes D12 (RASP format, NAR certification data, via ThrustCurve.org)
D12 24 70 0-3-5-7 0.0211 0.0426 Estes
0.049 2.569
0.116 9.369
0.184 17.275
0.237 24.258
0.282 29.730
0.297 27.010
0.311 22.204
0.322 17.258
0.348 14.497
0.386 12.733
0.442 11.687
0.546 10.889
0.718 10.400
0.879 10.123
1.066 9.920
1.257 9.827
1.436 9.987
1.590 9.858
1.612 9.620
1.637 9.400
1.655 2.870
1.667 0.000
";

        public const string Synthetic38 = @"<engine-database>
  <!-- SYNTHETIC example 38 mm reload (not a real product): BATES-like APCP curve for format/feature tests -->
  <engine-list>
    <engine code=""RW38-I250"" mfg=""RobotWin (sentetik)"" dia=""38"" len=""250"" initWt=""420"" propWt=""190"" delays=""6-8-10-14"" Itot=""352"" avgThrust=""251"" burn-time=""1.40"">
      <data>
        <eng-data t=""0.000"" f=""0.0"" m=""190.0""/>
        <eng-data t=""0.020"" f=""280.0"" m=""189.2""/>
        <eng-data t=""0.050"" f=""300.0"" m=""185.6""/>
        <eng-data t=""0.200"" f=""285.0"" m=""162.8""/>
        <eng-data t=""0.500"" f=""270.0"" m=""120.4""/>
        <eng-data t=""0.800"" f=""255.0"" m=""79.6""/>
        <eng-data t=""1.100"" f=""240.0"" m=""40.8""/>
        <eng-data t=""1.250"" f=""220.0"" m=""21.9""/>
        <eng-data t=""1.320"" f=""120.0"" m=""10.6""/>
        <eng-data t=""1.400"" f=""0.0"" m=""0.0""/>
      </data>
    </engine>
  </engine-list>
</engine-database>
";

        public static IReadOnlyList<string> Ids { get; } = new[] { "estes-a8", "estes-c6", "estes-d12", "rw38-i250" };

        public static string NameOf(string id) => id switch
        {
            "estes-a8" => "Estes A8 (18 mm, 2.3 N·s)",
            "estes-c6" => "Estes C6 (18 mm, 8.8 N·s)",
            "estes-d12" => "Estes D12 (24 mm, 18 N·s)",
            "rw38-i250" => "RW38-I250 (38 mm yeniden yüklenebilir, sentetik örnek)",
            _ => id
        };

        /// <summary>A built-in curve by id ("estes-c6" …), or a .eng/.rse file path.</summary>
        public static ThrustCurve Get(string? id)
        {
            string key = (id ?? "estes-c6").Trim();
            ThrustCurve c;
            switch (key.ToLowerInvariant())
            {
                case "estes-a8": c = ThrustCurve.ParseEng(EstesA8); break;
                case "estes-d12": c = ThrustCurve.ParseEng(EstesD12); break;
                case "rw38-i250": c = ThrustCurve.ParseRse(Synthetic38); c.Source = "sentetik örnek (gerçek ürün değil)"; return c;
                case "estes-c6": c = ThrustCurve.ParseEng(EstesC6); break;
                default:
                    if (System.IO.File.Exists(key)) return ThrustCurve.Load(key);
                    c = ThrustCurve.ParseEng(EstesC6);
                    break;
            }

            c.Source = "Estes/NAR sertifika verisi (ThrustCurve.org RASP)";
            return c;
        }
    }

    /// <summary>
    /// [aero] A motor in flight: ignition, thrust from the curve with combustion roughness, mass
    /// burn-down and the delay/ejection charge. Roughness is a first-order Gauss–Markov fluctuation of
    /// the thrust, σ = <see cref="Roughness"/>·F, correlation 4 ms (an assumption standing in for
    /// combustion instability/grain erosion noise; it shakes the airframe and so the accelerometers).
    /// </summary>
    public sealed class RocketMotorState
    {
        private readonly Random _rng;
        private double _noise;

        public RocketMotorState(ThrustCurve curve, double delay = double.NaN, int seed = 1)
        {
            Curve = curve;
            Delay = double.IsNaN(delay) ? (curve.Delays.Count > 0 ? curve.Delays[curve.Delays.Count / 2] : 0) : delay;
            _rng = new Random(seed);
        }

        public ThrustCurve Curve { get; }

        /// <summary>Delay after burnout before the ejection charge fires (s); 0 = plugged.</summary>
        public double Delay { get; set; }

        public double Roughness { get; set; } = 0.02;

        public bool Ignited { get; private set; }
        public double IgnitionTime { get; private set; } = double.NaN;
        public double Time { get; private set; }
        public double Thrust { get; private set; }
        public double Mass => Curve.MassAt(Ignited ? Time - IgnitionTime : 0);
        public bool BurnedOut => Ignited && Time - IgnitionTime >= Curve.BurnTime;
        public bool Ejected { get; private set; }

        /// <summary>Raised once when the ejection charge fires.</summary>
        public event Action? Ejection;

        public void Ignite(double time)
        {
            if (Ignited) return;
            Ignited = true;
            IgnitionTime = time;
        }

        public void Advance(double time, double dt)
        {
            Time = time;
            if (!Ignited)
            {
                Thrust = 0;
                return;
            }

            double t = time - IgnitionTime;
            double f = Curve.Thrust(t);
            if (f > 0 && Roughness > 0)
            {
                double a = Math.Exp(-dt / 0.004);
                _noise = a * _noise + Math.Sqrt(1 - a * a) * Gauss();
                f *= 1 + Roughness * _noise;
            }

            Thrust = Math.Max(0, f);
            if (!Ejected && Delay > 0 && t >= Curve.BurnTime + Delay)
            {
                Ejected = true;
                Ejection?.Invoke();
            }
        }

        private double Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
