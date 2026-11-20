using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Robotics
{
    /// <summary>A taught position: joint values, or a TCP pose in the arm's base frame.</summary>
    public sealed class ArmPoint
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Joint values (rad / m) when taught in joint space.</summary>
        public double[]? Joints { get; set; }

        /// <summary>TCP pose (base frame) when given in Cartesian form.</summary>
        public Transform3? Pose { get; set; }

        public override string ToString() => ArmProgram.FormatPoint(this, null);
    }

    public enum ArmOp
    {
        MoveJ,
        MoveL,
        Grip,
        Release,
        Wait,
        WaitDi,
        SetDo,
        WaitGrip,
        Pallet,
        PalletNext,
        Speed,
        End
    }

    public sealed class ArmInstruction
    {
        public int Line { get; set; }
        public ArmOp Op { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? Point { get; set; }

        /// <summary>Speed: % of rated joint speed for MoveJ, mm/s for MoveL (null = default).</summary>
        public double? Speed { get; set; }

        /// <summary>Base-frame TCP offset added to the target (m).</summary>
        public Vec3 Offset { get; set; }

        /// <summary>Add the current pallet slot offset.</summary>
        public bool UsePallet { get; set; }

        public double[] Args { get; set; } = Array.Empty<double>();
    }

    /// <summary>
    /// Robot program in a small teach-pendant language (Turkish and English keywords):
    /// <code>
    /// MOVEJ BEKLE HIZ=60          ; eksen hareketi, %60 hız
    /// MOVEL ALMA OFS=0,0,100      ; doğrusal, noktanın 100 mm üstü
    /// KAVRA / BIRAK               ; tutucu kapat / aç (vakum aç / kapat)
    /// TUTUSBEKLE 1.5              ; parça tutulana kadar bekle (en çok 1.5 s)
    /// BEKLE 0.5 · GIRISBEKLE 1 1 · CIKIS 2 1
    /// PALET 3 2 2 180 140 105     ; nx ny nz ve aralıklar (mm)
    /// MOVEL YER PAL               ; palet gözüne göre kaydırılmış nokta
    /// PALETSONRAKI · HIZ 80 · SON
    /// </code>
    /// Points: <c>AD = J(j1, j2, …)</c> (° / mm) or <c>AD = C(x, y, z, rx, ry, rz)</c> (mm / °).
    /// </summary>
    public sealed class ArmProgram
    {
        public List<ArmInstruction> Instructions { get; } = new List<ArmInstruction>();
        public Dictionary<string, ArmPoint> Points { get; } = new Dictionary<string, ArmPoint>(StringComparer.OrdinalIgnoreCase);
        public List<string> Errors { get; } = new List<string>();

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Upper-case ASCII form of a Turkish keyword ("BIRAK" for "Bırak").</summary>
        public static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                char c = ch switch
                {
                    'ı' or 'İ' or 'i' => 'I',
                    'ş' or 'Ş' => 'S',
                    'ğ' or 'Ğ' => 'G',
                    'ü' or 'Ü' => 'U',
                    'ö' or 'Ö' => 'O',
                    'ç' or 'Ç' => 'C',
                    _ => char.ToUpperInvariant(ch)
                };
                sb.Append(c);
            }

            return sb.ToString();
        }

        public static ArmProgram Parse(string? program, string? points, ArmModel model)
        {
            var p = new ArmProgram();
            ParsePoints(p, points ?? string.Empty, model);
            var lines = (program ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                int comment = raw.IndexOfAny(new[] { ';', '#' });
                string text = (comment >= 0 ? raw.Substring(0, comment) : raw).Trim();
                if (text.Length == 0) continue;
                try
                {
                    var ins = ParseLine(text, i + 1);
                    if (ins.Point != null && !p.Points.ContainsKey(ins.Point)) p.Errors.Add($"Satır {i + 1}: '{ins.Point}' noktası tanımlı değil.");
                    p.Instructions.Add(ins);
                }
                catch (FormatException ex)
                {
                    p.Errors.Add($"Satır {i + 1}: {ex.Message}");
                }
            }

            return p;
        }

        private static ArmInstruction ParseLine(string text, int line)
        {
            var tokens = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string op = Normalize(tokens[0]);
            var ins = new ArmInstruction { Line = line, Text = text };
            ins.Op = op switch
            {
                "MOVEJ" or "MOVJ" or "EKSEN" or "PTP" => ArmOp.MoveJ,
                "MOVEL" or "MOVL" or "DOGRUSAL" or "LIN" => ArmOp.MoveL,
                "GRIP" or "KAVRA" or "TUT" or "VAKUMAC" => ArmOp.Grip,
                "RELEASE" or "BIRAK" or "AC" or "VAKUMKAPAT" => ArmOp.Release,
                "WAIT" or "BEKLE" => ArmOp.Wait,
                "WAITDI" or "GIRISBEKLE" => ArmOp.WaitDi,
                "SETDO" or "CIKIS" => ArmOp.SetDo,
                "WAITGRIP" or "TUTUSBEKLE" => ArmOp.WaitGrip,
                "PALLET" or "PALET" => ArmOp.Pallet,
                "PALLETNEXT" or "PALETSONRAKI" => ArmOp.PalletNext,
                "SPEED" or "HIZ" => ArmOp.Speed,
                "END" or "SON" => ArmOp.End,
                _ => throw new FormatException($"bilinmeyen komut '{tokens[0]}'")
            };

            if (ins.Op == ArmOp.MoveJ || ins.Op == ArmOp.MoveL)
            {
                if (tokens.Length < 2) throw new FormatException("hedef nokta adı eksik");
                ins.Point = tokens[1];
                for (int k = 2; k < tokens.Length; k++)
                {
                    string t = Normalize(tokens[k]);
                    if (t == "PAL")
                    {
                        ins.UsePallet = true;
                    }
                    else if (t.StartsWith("HIZ=", StringComparison.Ordinal) || t.StartsWith("V=", StringComparison.Ordinal))
                    {
                        ins.Speed = Number(t.Substring(t.IndexOf('=') + 1).TrimEnd('%'));
                    }
                    else if (t.StartsWith("OFS=", StringComparison.Ordinal))
                    {
                        var v = t.Substring(4).Split(',').Select(Number).ToArray();
                        if (v.Length != 3) throw new FormatException("OFS=x,y,z (mm) bekleniyor");
                        ins.Offset = new Vec3(v[0], v[1], v[2]) * 0.001;
                    }
                    else
                    {
                        throw new FormatException($"anlaşılmayan parametre '{tokens[k]}'");
                    }
                }

                return ins;
            }

            ins.Args = tokens.Skip(1).Select(t =>
            {
                string n = Normalize(t);
                return n == "ON" || n == "ACIK" ? 1.0 : n == "OFF" || n == "KAPALI" ? 0.0 : Number(t);
            }).ToArray();
            switch (ins.Op)
            {
                case ArmOp.Wait when ins.Args.Length != 1:
                    throw new FormatException("BEKLE süre (s) ister");
                case ArmOp.WaitDi when ins.Args.Length != 2:
                case ArmOp.SetDo when ins.Args.Length != 2:
                    throw new FormatException("giriş/çıkış numarası ve değer (1/0) ister");
                case ArmOp.Pallet when ins.Args.Length != 6:
                    throw new FormatException("PALET nx ny nz dx dy dz (mm) ister");
                case ArmOp.Speed when ins.Args.Length != 1:
                    throw new FormatException("HIZ yüzde ister");
            }

            return ins;
        }

        private static double Number(string s)
        {
            if (!double.TryParse(s.Replace(',', '.'), NumberStyles.Float, Inv, out double v)) throw new FormatException($"sayı bekleniyor: '{s}'");
            return v;
        }

        private static void ParsePoints(ArmProgram p, string text, ArmModel model)
        {
            foreach (var raw in text.Replace("\r", string.Empty).Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    p.Errors.Add($"Nokta tanımı anlaşılamadı: '{line}'");
                    continue;
                }

                string name = line.Substring(0, eq).Trim();
                string body = line.Substring(eq + 1).Trim();
                if (body.Length < 3 || body[1] != '(' || !body.EndsWith(")", StringComparison.Ordinal))
                {
                    p.Errors.Add($"{name}: J(...) veya C(...) bekleniyor");
                    continue;
                }

                char kind = char.ToUpperInvariant(body[0]);
                double[] v;
                try
                {
                    // Values are separated by commas; decimals use a dot.
                    v = body.Substring(2, body.Length - 3).Split(',').Select(s => Number(s.Trim())).ToArray();
                }
                catch (FormatException ex)
                {
                    p.Errors.Add($"{name}: {ex.Message}");
                    continue;
                }

                if (kind == 'J')
                {
                    if (v.Length != model.Dof)
                    {
                        p.Errors.Add($"{name}: {model.Dof} eksen değeri gerekli");
                        continue;
                    }

                    var q = new double[v.Length];
                    for (int i = 0; i < q.Length; i++) q[i] = model.Joints[i].FromDisplay(v[i]);
                    p.Points[name] = new ArmPoint { Name = name, Joints = q };
                }
                else if (kind == 'C')
                {
                    if (v.Length != 6 && v.Length != 4)
                    {
                        p.Errors.Add($"{name}: C(x, y, z, rx, ry, rz) bekleniyor");
                        continue;
                    }

                    double d = Math.PI / 180;
                    var pose = v.Length == 6
                        ? Transform3.FromRpy(v[0] / 1000, v[1] / 1000, v[2] / 1000, v[3] * d, v[4] * d, v[5] * d)
                        : Transform3.FromRpy(v[0] / 1000, v[1] / 1000, v[2] / 1000, Math.PI, 0, v[3] * d);
                    p.Points[name] = new ArmPoint { Name = name, Pose = pose };
                }
                else
                {
                    p.Errors.Add($"{name}: J(...) veya C(...) bekleniyor");
                }
            }
        }

        /// <summary>Text form of a point ("P1 = J(0, -10, 25, 0, 75, 0)").</summary>
        public static string FormatPoint(ArmPoint pt, ArmModel? model)
        {
            if (pt.Joints != null)
            {
                var vals = pt.Joints.Select((q, i) => (model != null && i < model.Dof ? model.Joints[i].ToDisplay(q) : q * 180 / Math.PI).ToString("0.##", Inv));
                return $"{pt.Name} = J({string.Join(", ", vals)})";
            }

            if (pt.Pose is { } pose)
            {
                var (r, pi, y) = pose.ToRpy();
                double d = 180 / Math.PI;
                return string.Format(Inv, "{0} = C({1:0.#}, {2:0.#}, {3:0.#}, {4:0.##}, {5:0.##}, {6:0.##})", pt.Name, pose.P.X * 1000, pose.P.Y * 1000, pose.P.Z * 1000, r * d, pi * d, y * d);
            }

            return pt.Name + " = J()";
        }

        /// <summary>Adds or replaces one point in a points text, keeping the other lines.</summary>
        public static string SetPoint(string? points, ArmPoint pt, ArmModel model)
        {
            var lines = (points ?? string.Empty).Replace("\r", string.Empty).Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
            string newLine = FormatPoint(pt, model);
            int idx = lines.FindIndex(l => l.Contains('=') && string.Equals(l.Substring(0, l.IndexOf('=')).Trim(), pt.Name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) lines[idx] = newLine;
            else lines.Add(newLine);
            return string.Join("\n", lines);
        }

        /// <summary>Next free point name "P1", "P2" …</summary>
        public static string NextPointName(string? points)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in (points ?? string.Empty).Split('\n'))
            {
                int eq = l.IndexOf('=');
                if (eq > 0) used.Add(l.Substring(0, eq).Trim());
            }

            for (int i = 1; ; i++)
            {
                if (!used.Contains("P" + i.ToString(Inv))) return "P" + i.ToString(Inv);
            }
        }

        /// <summary>Joint values as text in display units ("0, -10, 25, …").</summary>
        public static string FormatJoints(ArmModel m, IReadOnlyList<double> q) =>
            string.Join(", ", Enumerable.Range(0, m.Dof).Select(i => m.Joints[i].ToDisplay(q[i]).ToString("0.##", Inv)));

        /// <summary>Parses "0, -10, 25 …" (display units); null if malformed.</summary>
        public static double[]? ParseJoints(ArmModel m, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var parts = text!.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != m.Dof) return null;
            var q = new double[m.Dof];
            for (int i = 0; i < q.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, Inv, out double v)) return null;
                q[i] = m.Joints[i].FromDisplay(v);
            }

            return m.Clamp(q);
        }
    }
}
