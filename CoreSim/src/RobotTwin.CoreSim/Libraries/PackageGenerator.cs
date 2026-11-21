using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Pcb;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] Parametric footprints for the part creator: DIP, SOIC/SSOP/TSSOP, QFN, QFP,
    // pin headers (1×N, 2×N) and SOT-23. Dimensions follow the usual IPC-7351 nominal
    // land patterns; ids are descriptive ("RW_SOIC-8_P1.27mm") and never clash with
    // the built-in parametric ids.

    public enum PackageFamily
    {
        Dip,
        Soic,
        Qfn,
        Qfp,
        Header,
        Sot23
    }

    public sealed class PackageSpec
    {
        public PackageFamily Family { get; set; } = PackageFamily.Dip;

        /// <summary>Total pin count (QFN/QFP: without the exposed pad; header: pins per row × rows).</summary>
        public int Pins { get; set; } = 8;

        /// <summary>Pin pitch, mm.</summary>
        public double Pitch { get; set; } = 2.54;

        /// <summary>DIP: row spacing; SOIC: pad centre-to-centre span across the body; QFN/QFP: body size.</summary>
        public double Span { get; set; } = 7.62;

        /// <summary>Header rows (1 or 2).</summary>
        public int Rows { get; set; } = 1;

        /// <summary>QFN/QFP exposed (thermal) pad side, mm; 0 = none.</summary>
        public double ExposedPad { get; set; }

        /// <summary>Component height above the board, mm (3D model).</summary>
        public double Height { get; set; } = 4.5;

        public static PackageSpec Default(PackageFamily f) => f switch
        {
            PackageFamily.Dip => new PackageSpec { Family = f, Pins = 8, Pitch = 2.54, Span = 7.62, Height = 4.5 },
            PackageFamily.Soic => new PackageSpec { Family = f, Pins = 8, Pitch = 1.27, Span = 5.4, Height = 1.75 },
            PackageFamily.Qfn => new PackageSpec { Family = f, Pins = 16, Pitch = 0.5, Span = 3.0, ExposedPad = 1.7, Height = 0.9 },
            PackageFamily.Qfp => new PackageSpec { Family = f, Pins = 32, Pitch = 0.8, Span = 7.0, Height = 1.2 },
            PackageFamily.Header => new PackageSpec { Family = f, Pins = 4, Pitch = 2.54, Rows = 1, Height = 8.5 },
            PackageFamily.Sot23 => new PackageSpec { Family = f, Pins = 3, Pitch = 0.95, Span = 2.2, Height = 1.1 },
            _ => new PackageSpec()
        };

        public static string FamilyName(PackageFamily f) => f switch
        {
            PackageFamily.Dip => "DIP (delikli, iki sıra)",
            PackageFamily.Soic => "SOIC / SSOP / TSSOP (SMD, iki sıra)",
            PackageFamily.Qfn => "QFN / DFN (bacaksız, dört kenar)",
            PackageFamily.Qfp => "QFP / TQFP (dört kenar, bacaklı)",
            PackageFamily.Header => "Erkek header (1×N / 2×N)",
            PackageFamily.Sot23 => "SOT-23 (3, 5, 6 bacak)",
            _ => f.ToString()
        };
    }

    public static class PackageGenerator
    {
        private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static string IdFor(PackageSpec s) => s.Family switch
        {
            PackageFamily.Dip => $"RW_DIP-{s.Pins}_W{F(s.Span)}mm",
            PackageFamily.Soic => $"RW_SOIC-{s.Pins}_P{F(s.Pitch)}mm_S{F(s.Span)}mm",
            PackageFamily.Qfn => $"RW_QFN-{s.Pins}_{F(s.Span)}x{F(s.Span)}mm_P{F(s.Pitch)}mm" + (s.ExposedPad > 0 ? "_EP" + F(s.ExposedPad) : string.Empty),
            PackageFamily.Qfp => $"RW_QFP-{s.Pins}_{F(s.Span)}x{F(s.Span)}mm_P{F(s.Pitch)}mm",
            PackageFamily.Header => $"RW_Header-{s.Rows}x{Math.Max(1, s.Pins / Math.Max(1, s.Rows))}_P{F(s.Pitch)}mm",
            PackageFamily.Sot23 => $"RW_SOT-23-{s.Pins}",
            _ => "RW_PKG"
        };

        /// <summary>Checks the numbers; returns a Turkish reason or null when the package can be generated.</summary>
        public static string? Check(PackageSpec s)
        {
            if (s.Pitch <= 0.1 || s.Pitch > 10) return "bacak aralığı 0.1–10 mm olmalı.";
            switch (s.Family)
            {
                case PackageFamily.Dip:
                case PackageFamily.Soic:
                    if (s.Pins < 2 || s.Pins % 2 != 0 || s.Pins > 128) return "bacak sayısı 2–128 arası çift sayı olmalı.";
                    if (s.Span <= s.Pitch) return "sıra aralığı bacak aralığından büyük olmalı.";
                    return null;
                case PackageFamily.Qfn:
                case PackageFamily.Qfp:
                    if (s.Pins < 4 || s.Pins % 4 != 0 || s.Pins > 256) return "bacak sayısı 4–256 arası, 4'ün katı olmalı.";
                    if ((s.Pins / 4 - 1) * s.Pitch >= s.Span) return "gövde bu kadar bacağa göre küçük (kenar başına bacak × aralık ≥ gövde).";
                    if (s.ExposedPad >= s.Span) return "ısı pedi gövdeden büyük olamaz.";
                    return null;
                case PackageFamily.Header:
                    if (s.Rows < 1 || s.Rows > 2) return "sıra sayısı 1 veya 2 olmalı.";
                    if (s.Pins < 1 || s.Pins > 200 || s.Pins % s.Rows != 0) return "bacak sayısı sıra sayısının katı olmalı (en çok 200).";
                    return null;
                case PackageFamily.Sot23:
                    return s.Pins == 3 || s.Pins == 5 || s.Pins == 6 ? null : "SOT-23 için 3, 5 veya 6 bacak seçin.";
                default:
                    return "bilinmeyen kılıf ailesi.";
            }
        }

        public static Footprint Generate(PackageSpec s)
        {
            var reason = Check(s);
            if (reason != null) throw new ArgumentException(reason);
            var fp = new Footprint { Id = IdFor(s), Height = s.Height };
            switch (s.Family)
            {
                case PackageFamily.Dip: Dip(fp, s); break;
                case PackageFamily.Soic: Soic(fp, s); break;
                case PackageFamily.Qfn: Quad(fp, s, leaded: false); break;
                case PackageFamily.Qfp: Quad(fp, s, leaded: true); break;
                case PackageFamily.Header: Header(fp, s); break;
                case PackageFamily.Sot23: Sot23(fp, s); break;
            }

            fp.FitCourtyard(0.25);
            return fp;
        }

        private static string N(int i) => i.ToString(CultureInfo.InvariantCulture);

        private static void Box(Footprint fp, double x0, double y0, double x1, double y1)
        {
            fp.Silkscreen.Add(new Segment2(x0, y0, x1, y0));
            fp.Silkscreen.Add(new Segment2(x1, y0, x1, y1));
            fp.Silkscreen.Add(new Segment2(x1, y1, x0, y1));
            fp.Silkscreen.Add(new Segment2(x0, y1, x0, y0));
        }

        private static void Dip(Footprint fp, PackageSpec s)
        {
            int half = s.Pins / 2;
            double drill = 0.8, size = 1.6;
            double y0 = -(half - 1) * s.Pitch / 2;
            for (int i = 0; i < half; i++)
            {
                double y = y0 + i * s.Pitch;
                fp.Pads.Add(new Pad { Name = N(i + 1), X = -s.Span / 2, Y = y, Width = size, Height = size, Drill = drill, Shape = i == 0 ? PadShape.Rect : PadShape.Oval });
                fp.Pads.Add(new Pad { Name = N(s.Pins - i), X = s.Span / 2, Y = y, Width = size, Height = size, Drill = drill, Shape = PadShape.Oval });
            }

            double bx = s.Span / 2 - 1.27, by = -y0 + 1.27;
            Box(fp, -bx, -by, bx, by);
            fp.Silkscreen.Add(new Segment2(-0.8, -by, 0, -by + 0.8));
            fp.Silkscreen.Add(new Segment2(0, -by + 0.8, 0.8, -by));
            fp.Description = $"DIP-{s.Pins}, {F(s.Span)} mm sıra aralığı, {F(s.Pitch)} mm adım";
        }

        private static void Soic(Footprint fp, PackageSpec s)
        {
            int half = s.Pins / 2;
            double padW = Math.Max(0.8, Math.Min(2.0, s.Span * 0.29)), padH = Math.Min(0.6, s.Pitch * 0.55);
            double y0 = -(half - 1) * s.Pitch / 2;
            for (int i = 0; i < half; i++)
            {
                double y = y0 + i * s.Pitch;
                fp.Pads.Add(new Pad { Name = N(i + 1), X = -s.Span / 2, Y = y, Width = padW, Height = padH, Shape = PadShape.RoundRect, Layer = CopperLayer.Top });
                fp.Pads.Add(new Pad { Name = N(s.Pins - i), X = s.Span / 2, Y = y, Width = padW, Height = padH, Shape = PadShape.RoundRect, Layer = CopperLayer.Top });
            }

            double bx = Math.Max(0.6, s.Span / 2 - padW / 2 - 0.2), by = -y0 + s.Pitch / 2 + 0.2;
            Box(fp, -bx, -by, bx, by);
            fp.SilkCircles.Add((-s.Span / 2, -by - 0.5, 0.15));
            fp.Description = $"SOIC-{s.Pins}, {F(s.Pitch)} mm adım, {F(s.Span)} mm ped açıklığı";
        }

        private static void Quad(Footprint fp, PackageSpec s, bool leaded)
        {
            int per = s.Pins / 4;
            double body = s.Span;
            double padLen = leaded ? 1.5 : 0.8, padWid = Math.Min(0.3 + s.Pitch * 0.25, s.Pitch * 0.6);
            double c = leaded ? body / 2 + 0.75 : body / 2 - padLen / 2 + 0.35;
            double start = -(per - 1) * s.Pitch / 2;
            int n = 1;
            // KiCad order: left side top→bottom, bottom left→right, right bottom→top, top right→left.
            for (int i = 0; i < per; i++) fp.Pads.Add(Smd(N(n++), -c, start + i * s.Pitch, padLen, padWid));
            for (int i = 0; i < per; i++) fp.Pads.Add(Smd(N(n++), start + i * s.Pitch, c, padWid, padLen));
            for (int i = 0; i < per; i++) fp.Pads.Add(Smd(N(n++), c, -(start + i * s.Pitch), padLen, padWid));
            for (int i = 0; i < per; i++) fp.Pads.Add(Smd(N(n++), -(start + i * s.Pitch), -c, padWid, padLen));
            if (s.ExposedPad > 0) fp.Pads.Add(Smd(N(n), 0, 0, s.ExposedPad, s.ExposedPad));
            double b = body / 2;
            double corner = Math.Max(0.2, b - (per - 1) * s.Pitch / 2 - padWid);
            // Corner marks only (the pads cover the edges).
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                fp.Silkscreen.Add(new Segment2(sx * b, sy * b, sx * (b - corner), sy * b));
                fp.Silkscreen.Add(new Segment2(sx * b, sy * b, sx * b, sy * (b - corner)));
            }

            fp.SilkCircles.Add((-c - padLen / 2 - 0.4, start, 0.15));
            fp.FabLines.Add(new Segment2(-b, -b, b, -b));
            fp.FabLines.Add(new Segment2(b, -b, b, b));
            fp.FabLines.Add(new Segment2(b, b, -b, b));
            fp.FabLines.Add(new Segment2(-b, b, -b, -b));
            fp.Description = $"{(leaded ? "QFP" : "QFN")}-{s.Pins}, {F(body)}×{F(body)} mm, {F(s.Pitch)} mm adım" + (s.ExposedPad > 0 ? $", ısı pedi {F(s.ExposedPad)} mm (pad {s.Pins + 1})" : string.Empty);
        }

        private static Pad Smd(string name, double x, double y, double w, double h) =>
            new Pad { Name = name, X = x, Y = y, Width = w, Height = h, Shape = PadShape.RoundRect, Layer = CopperLayer.Top };

        private static void Header(Footprint fp, PackageSpec s)
        {
            int cols = s.Pins / s.Rows;
            for (int i = 0; i < cols; i++)
            {
                for (int r = 0; r < s.Rows; r++)
                {
                    int number = s.Rows == 1 ? i + 1 : i * 2 + r + 1;
                    fp.Pads.Add(new Pad
                    {
                        Name = N(number), X = r * s.Pitch, Y = i * s.Pitch, Width = 1.7, Height = 1.7, Drill = 1.0,
                        Shape = number == 1 ? PadShape.Rect : PadShape.Oval
                    });
                }
            }

            Box(fp, -s.Pitch / 2, -s.Pitch / 2, (s.Rows - 0.5) * s.Pitch, (cols - 0.5) * s.Pitch);
            fp.Description = $"Erkek header {s.Rows}×{cols}, {F(s.Pitch)} mm";
        }

        private static void Sot23(Footprint fp, PackageSpec s)
        {
            double y = 1.1;
            if (s.Pins == 3)
            {
                fp.Pads.Add(Smd("1", -0.95, y, 0.6, 1.0));
                fp.Pads.Add(Smd("2", 0.95, y, 0.6, 1.0));
                fp.Pads.Add(Smd("3", 0, -y, 0.6, 1.0));
            }
            else
            {
                fp.Pads.Add(Smd("1", -0.95, y, 0.6, 1.0));
                fp.Pads.Add(Smd("2", 0, y, 0.6, 1.0));
                fp.Pads.Add(Smd("3", 0.95, y, 0.6, 1.0));
                if (s.Pins == 6)
                {
                    fp.Pads.Add(Smd("4", 0.95, -y, 0.6, 1.0));
                    fp.Pads.Add(Smd("5", 0, -y, 0.6, 1.0));
                    fp.Pads.Add(Smd("6", -0.95, -y, 0.6, 1.0));
                }
                else
                {
                    fp.Pads.Add(Smd("4", 0.95, -y, 0.6, 1.0));
                    fp.Pads.Add(Smd("5", -0.95, -y, 0.6, 1.0));
                }
            }

            fp.Silkscreen.Add(new Segment2(-1.5, -0.4, -1.5, 0.4));
            fp.Silkscreen.Add(new Segment2(1.5, -0.4, 1.5, 0.4));
            fp.SilkCircles.Add((-1.6, 1.2, 0.12));
            fp.Description = $"SOT-23-{s.Pins}";
        }
    }
}
