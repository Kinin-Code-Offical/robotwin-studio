using System;
using System.Collections.Generic;
using System.Globalization;

namespace RobotTwin.CoreSim.Pcb
{
    public enum PadShape
    {
        Circle,
        Rect,
        Oval,
        RoundRect
    }

    /// <summary>
    /// Copper layers. Top/Bottom keep their historic values (files store the names);
    /// inner layers exist on boards of 4 to 32 layers (KiCad's range). Stack order is Top,
    /// In1..In30, Bottom (see <see cref="PcbLayers"/>). Also used for the mounting side of a component.
    /// </summary>
    public enum CopperLayer
    {
        Top = 0,
        Bottom = 1,
        In1 = 2,
        In2 = 3,
        In3 = 4,
        In4 = 5,
        In5 = 6,
        In6 = 7,
        In7 = 8,
        In8 = 9,
        In9 = 10,
        In10 = 11,
        In11 = 12,
        In12 = 13,
        In13 = 14,
        In14 = 15,
        In15 = 16,
        In16 = 17,
        In17 = 18,
        In18 = 19,
        In19 = 20,
        In20 = 21,
        In21 = 22,
        In22 = 23,
        In23 = 24,
        In24 = 25,
        In25 = 26,
        In26 = 27,
        In27 = 28,
        In28 = 29,
        In29 = 30,
        In30 = 31
    }

    /// <summary>A copper pad. Positions in millimetres relative to the footprint origin, Y pointing down.</summary>
    public sealed class Pad
    {
        public string Name { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public PadShape Shape { get; set; } = PadShape.Circle;

        /// <summary>Drill diameter; 0 for a surface-mount pad.</summary>
        public double Drill { get; set; }

        /// <summary>Layer of an SMD pad (through-hole pads are on every copper layer).</summary>
        public CopperLayer Layer { get; set; } = CopperLayer.Top;

        /// <summary>Corner radius of a <see cref="PadShape.RoundRect"/> pad as a fraction of the shorter side.</summary>
        public double CornerRatio { get; set; } = 0.25;

        /// <summary>A through-hole pad without copper (non-plated hole).</summary>
        public bool NonPlated { get; set; }

        public bool IsThroughHole => Drill > 0.0;

        public Pad Clone() => (Pad)MemberwiseClone();
    }

    /// <summary>Graphic layer of footprint and board drawings.</summary>
    public enum GraphicLayer
    {
        SilkTop,
        SilkBottom,
        FabTop,
        FabBottom,
        Drawings
    }

    /// <summary>Text drawn by a footprint (footprint-local position).</summary>
    public sealed class FootprintText
    {
        public string Text { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Size { get; set; } = 1.0;

        /// <summary>SilkTop or FabTop (mirrored with the part when it sits on the bottom).</summary>
        public GraphicLayer Layer { get; set; } = GraphicLayer.SilkTop;
    }

    public struct Segment2
    {
        public Segment2(double x1, double y1, double x2, double y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
    }

    public sealed class Footprint
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<Pad> Pads { get; } = new List<Pad>();

        /// <summary>Keep-out rectangle around the part, used for placement and courtyard DRC.</summary>
        public double CourtyardMinX { get; set; }
        public double CourtyardMinY { get; set; }
        public double CourtyardMaxX { get; set; }
        public double CourtyardMaxY { get; set; }

        public List<Segment2> Silkscreen { get; } = new List<Segment2>();

        /// <summary>Silkscreen circles: centre and radius.</summary>
        public List<(double X, double Y, double R)> SilkCircles { get; } = new List<(double, double, double)>();

        /// <summary>Fabrication-layer outline (body drawing); empty means "courtyard inset".</summary>
        public List<Segment2> FabLines { get; } = new List<Segment2>();

        /// <summary>Extra silkscreen/fab texts drawn by the footprint (the reference designator is always drawn).</summary>
        public List<FootprintText> Texts { get; } = new List<FootprintText>();

        /// <summary>Component body height in mm (for the 3D view and clearance).</summary>
        public double Height { get; set; } = 3.0;

        /// <summary>Deep copy (pads, graphics) with a new id, for editing a library footprint.</summary>
        public Footprint Clone(string newId)
        {
            var fp = new Footprint
            {
                Id = newId, Description = Description, Height = Height,
                CourtyardMinX = CourtyardMinX, CourtyardMinY = CourtyardMinY, CourtyardMaxX = CourtyardMaxX, CourtyardMaxY = CourtyardMaxY
            };
            foreach (var p in Pads) fp.Pads.Add(p.Clone());
            fp.Silkscreen.AddRange(Silkscreen);
            fp.SilkCircles.AddRange(SilkCircles);
            fp.FabLines.AddRange(FabLines);
            foreach (var t in Texts) fp.Texts.Add(new FootprintText { Text = t.Text, X = t.X, Y = t.Y, Size = t.Size, Layer = t.Layer });
            return fp;
        }

        /// <summary>Sets the courtyard to the bounding box of pads and graphics plus a margin (IPC-7351: 0.25 mm).</summary>
        public void FitCourtyard(double margin = 0.25)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            void Grow(double x0, double y0, double x1, double y1)
            {
                minX = Math.Min(minX, Math.Min(x0, x1));
                minY = Math.Min(minY, Math.Min(y0, y1));
                maxX = Math.Max(maxX, Math.Max(x0, x1));
                maxY = Math.Max(maxY, Math.Max(y0, y1));
            }

            foreach (var p in Pads) Grow(p.X - p.Width / 2, p.Y - p.Height / 2, p.X + p.Width / 2, p.Y + p.Height / 2);
            foreach (var s in Silkscreen) Grow(s.X1, s.Y1, s.X2, s.Y2);
            foreach (var s in FabLines) Grow(s.X1, s.Y1, s.X2, s.Y2);
            foreach (var (x, y, r) in SilkCircles) Grow(x - r, y - r, x + r, y + r);
            if (double.IsInfinity(minX))
            {
                minX = minY = -0.5;
                maxX = maxY = 0.5;
            }

            CourtyardMinX = Math.Round(minX - margin, 4);
            CourtyardMinY = Math.Round(minY - margin, 4);
            CourtyardMaxX = Math.Round(maxX + margin, 4);
            CourtyardMaxY = Math.Round(maxY + margin, 4);
        }

        public Pad? FindPad(string name)
        {
            foreach (var pad in Pads)
            {
                if (string.Equals(pad.Name, name, StringComparison.OrdinalIgnoreCase)) return pad;
            }

            return null;
        }

        internal Footprint Rect(double minX, double minY, double maxX, double maxY, bool silk = true, double margin = 0.25)
        {
            CourtyardMinX = minX - margin;
            CourtyardMinY = minY - margin;
            CourtyardMaxX = maxX + margin;
            CourtyardMaxY = maxY + margin;
            if (silk)
            {
                Silkscreen.Add(new Segment2(minX, minY, maxX, minY));
                Silkscreen.Add(new Segment2(maxX, minY, maxX, maxY));
                Silkscreen.Add(new Segment2(maxX, maxY, minX, maxY));
                Silkscreen.Add(new Segment2(minX, maxY, minX, minY));
            }

            return this;
        }

        internal Footprint Tht(string name, double x, double y, double size, double drill, PadShape shape = PadShape.Circle, double height = 0)
        {
            Pads.Add(new Pad { Name = name, X = x, Y = y, Width = size, Height = height > 0 ? height : size, Drill = drill, Shape = shape });
            return this;
        }

        internal Footprint Smd(string name, double x, double y, double w, double h)
        {
            Pads.Add(new Pad { Name = name, X = x, Y = y, Width = w, Height = h, Shape = PadShape.RoundRect, Layer = CopperLayer.Top });
            return this;
        }
    }

    /// <summary>
    /// Parametric footprint generator. Ids encode the package: "AXIAL-400" (pitch in mil),
    /// "RADIAL-2.5", "CP-6.3-2.5" (diameter-pitch, mm), "DIP-8", "HDR-1x4", "TERM-2", "TO92",
    /// "TO220", "TO126", "SOT23", "SOT223", "R0805", "LED-5MM", "ARDUINO-UNO"...
    /// </summary>
    public static class FootprintLibrary
    {
        private const double Mil = 0.0254;
        private static readonly Dictionary<string, Footprint> Cache = new Dictionary<string, Footprint>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        public static IEnumerable<string> KnownIds => new[]
        {
            "AXIAL-300", "AXIAL-400", "AXIAL-500", "AXIAL-600", "AXIAL-800", "RADIAL-2.5", "RADIAL-5.0",
            "CP-5-2.0", "CP-6.3-2.5", "CP-8-3.5", "CP-10-5.0", "CP-13-5.0", "DO35", "DO41", "DO201",
            "LED-3MM", "LED-5MM", "LED0805", "R0805", "C0805", "TO92", "TO126", "TO220", "SOT23", "SOT223",
            "DIP-8", "DIP-14", "DIP-16", "HDR-1x2", "HDR-1x3", "HDR-1x4", "HDR-1x6", "HDR-1x8", "TERM-2", "TERM-3",
            "TACT-6", "SW-2", "POT-RV09", "TRIM-3296", "RELAY-SRD", "BUZZER-12", "WIRE-2", "ARDUINO-UNO", "ARDUINO-NANO",
            "ESP32-DEVKITC", "ESP32-DEVKIT-V1"
        };

        public static Footprint? Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (Gate)
            {
                if (Cache.TryGetValue(id, out var cached)) return cached;
                var fp = Create(id.Trim().ToUpperInvariant());
                if (fp != null)
                {
                    fp.Id = id;
                    Cache[id] = fp;
                }

                return fp;
            }
        }

        /// <summary>Adds or replaces a footprint (user libraries, imported parts).</summary>
        public static void Register(Footprint footprint)
        {
            if (string.IsNullOrWhiteSpace(footprint.Id)) throw new ArgumentException("Footprint'in bir kimliği (adı) olmalı.");
            lock (Gate)
            {
                Cache[footprint.Id] = footprint;
                UserIds.Add(footprint.Id);
            }
        }

        /// <summary>Footprints registered at run time.</summary>
        public static HashSet<string> UserIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Built-in and registered footprint ids (built-in first), for pickers.</summary>
        public static IReadOnlyList<string> AllIds
        {
            get
            {
                lock (Gate)
                {
                    var list = new List<string>(KnownIds);
                    foreach (var id in UserIds)
                    {
                        if (!list.Contains(id, StringComparer.OrdinalIgnoreCase)) list.Add(id);
                    }

                    return list;
                }
            }
        }

        /// <summary>True for ids generated by this library (they cannot be overwritten by the footprint editor).</summary>
        public static bool IsBuiltIn(string id) => KnownIds.Contains(id, StringComparer.OrdinalIgnoreCase) && !UserIds.Contains(id);

        private static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static Footprint? Create(string id)
        {
            string[] parts = id.Split('-');
            switch (parts[0])
            {
                case "AXIAL":
                {
                    double pitch = Num(parts[1]) * Mil;
                    double bodyHalf = Math.Min(pitch / 2 - 1.2, 3.2);
                    var fp = new Footprint { Description = $"Eksenel, {pitch:0.##} mm aralık", Height = 2.5 }
                        .Tht("1", 0, 0, 1.6, 0.8, PadShape.Rect).Tht("2", pitch, 0, 1.6, 0.8);
                    fp.Rect(pitch / 2 - bodyHalf, -1.25, pitch / 2 + bodyHalf, 1.25);
                    fp.CourtyardMinX = -1.05;
                    fp.CourtyardMaxX = pitch + 1.05;
                    return fp;
                }

                case "RADIAL":
                {
                    double pitch = Num(parts[1]);
                    var fp = new Footprint { Description = $"Radyal, {pitch} mm", Height = 5 }
                        .Tht("1", 0, 0, 1.6, 0.8, PadShape.Rect).Tht("2", pitch, 0, 1.6, 0.8);
                    return fp.Rect(-1.3, -1.6, pitch + 1.3, 1.6);
                }

                case "CP":
                {
                    double d = Num(parts[1]), pitch = Num(parts[2]);
                    double drill = d >= 10 ? 1.0 : 0.8, pad = d >= 10 ? 2.0 : 1.6;
                    var fp = new Footprint { Description = $"Elektrolitik Ø{d} mm", Height = d * 1.6 }
                        .Tht("1", 0, 0, pad, drill, PadShape.Rect).Tht("2", pitch, 0, pad, drill);
                    double cx = pitch / 2, r = d / 2;
                    fp.SilkCircles.Add((cx, 0, r));
                    fp.Silkscreen.Add(new Segment2(cx - r - 0.8, -r * 0.6, cx - r - 0.8, -r * 0.6 + 1.2));
                    fp.Silkscreen.Add(new Segment2(cx - r - 1.4, -r * 0.6 + 0.6, cx - r - 0.2, -r * 0.6 + 0.6));
                    fp.CourtyardMinX = cx - r - 0.5;
                    fp.CourtyardMaxX = cx + r + 0.5;
                    fp.CourtyardMinY = -r - 0.5;
                    fp.CourtyardMaxY = r + 0.5;
                    return fp;
                }

                case "DO35":
                    return Diode(7.62, 0.8, 1.8, 4.0, 1.0);
                case "DO41":
                    return Diode(10.16, 1.0, 2.2, 5.2, 1.35);
                case "DO201":
                    return Diode(15.24, 1.4, 3.0, 9.5, 2.7);

                case "LED":
                {
                    double d = parts[1] == "3MM" ? 3.0 : 5.0;
                    var fp = new Footprint { Description = $"LED {d} mm", Height = d * 1.7 }
                        .Tht("1", 0, 0, 1.8, 0.9, PadShape.Rect).Tht("2", 2.54, 0, 1.8, 0.9);
                    fp.SilkCircles.Add((1.27, 0, d / 2 + 0.4));
                    fp.Silkscreen.Add(new Segment2(1.27 - d / 2 - 0.4, -1.0, 1.27 - d / 2 - 0.4, 1.0));
                    fp.CourtyardMinX = 1.27 - d / 2 - 0.9;
                    fp.CourtyardMaxX = 1.27 + d / 2 + 0.9;
                    fp.CourtyardMinY = -d / 2 - 0.9;
                    fp.CourtyardMaxY = d / 2 + 0.9;
                    return fp;
                }

                case "LED0805":
                case "R0805":
                case "C0805":
                {
                    var fp = new Footprint { Description = "SMD 0805", Height = 0.6 }
                        .Smd("1", -0.95, 0, 1.0, 1.25).Smd("2", 0.95, 0, 1.0, 1.25);
                    fp.Silkscreen.Add(new Segment2(-0.3, -0.85, 0.3, -0.85));
                    fp.Silkscreen.Add(new Segment2(-0.3, 0.85, 0.3, 0.85));
                    fp.CourtyardMinX = -1.7;
                    fp.CourtyardMaxX = 1.7;
                    fp.CourtyardMinY = -1.0;
                    fp.CourtyardMaxY = 1.0;
                    return fp;
                }

                case "TO92":
                {
                    var fp = new Footprint { Description = "TO-92", Height = 5 }
                        .Tht("1", 0, 0, 1.5, 0.75, PadShape.Rect).Tht("2", 2.54, 0, 1.5, 0.75).Tht("3", 5.08, 0, 1.5, 0.75);
                    fp.Silkscreen.Add(new Segment2(-0.5, 1.6, 5.58, 1.6));
                    fp.SilkCircles.Add((2.54, 0.3, 2.6));
                    return fp.Rect(-0.8, -2.4, 5.88, 1.8, silk: false);
                }

                case "TO126":
                    return TransistorRow(2.28, 1.1, 8.0, 3.2, "TO-126");
                case "TO220":
                    return TransistorRow(2.54, 1.2, 10.2, 4.6, "TO-220");

                case "SOT23":
                {
                    var fp = new Footprint { Description = "SOT-23", Height = 1.1 }
                        .Smd("1", -0.95, 1.0, 0.6, 0.8).Smd("2", 0.95, 1.0, 0.6, 0.8).Smd("3", 0, -1.0, 0.6, 0.8);
                    return fp.Rect(-1.5, -1.6, 1.5, 1.6, silk: false);
                }

                case "SOT223":
                {
                    var fp = new Footprint { Description = "SOT-223", Height = 1.8 }
                        .Smd("1", -2.3, 3.15, 1.0, 1.6).Smd("2", 0, 3.15, 1.0, 1.6).Smd("3", 2.3, 3.15, 1.0, 1.6)
                        .Smd("4", 0, -3.15, 3.4, 1.6);
                    return fp.Rect(-3.4, -1.8, 3.4, 1.8).Rect(-3.6, -4.2, 3.6, 4.2, silk: false);
                }

                case "DIP":
                {
                    int n = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    return Dip(n, 7.62);
                }

                case "HDR":
                {
                    int n = int.Parse(parts[1].Substring(2), CultureInfo.InvariantCulture);
                    var fp = new Footprint { Description = $"Erkek header 1×{n}", Height = 8.5 };
                    for (int i = 0; i < n; i++)
                    {
                        fp.Tht((i + 1).ToString(CultureInfo.InvariantCulture), 0, i * 2.54, 1.7, 1.0, i == 0 ? PadShape.Rect : PadShape.Oval);
                    }

                    return fp.Rect(-1.27, -1.27, 1.27, (n - 1) * 2.54 + 1.27);
                }

                case "TERM":
                {
                    int n = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    var fp = new Footprint { Description = $"Vidalı klemens {n}'li, 5.08 mm", Height = 10 };
                    for (int i = 0; i < n; i++)
                    {
                        fp.Tht((i + 1).ToString(CultureInfo.InvariantCulture), i * 5.08, 0, 2.6, 1.3, i == 0 ? PadShape.Rect : PadShape.Circle);
                    }

                    return fp.Rect(-2.54, -4.0, (n - 1) * 5.08 + 2.54, 4.0);
                }

                case "TACT":
                {
                    var fp = new Footprint { Description = "Tactile buton 6×6 mm", Height = 5 }
                        .Tht("1", 0, 0, 2.0, 1.1).Tht("2", 6.5, 0, 2.0, 1.1).Tht("3", 0, 4.5, 2.0, 1.1).Tht("4", 6.5, 4.5, 2.0, 1.1);
                    fp.SilkCircles.Add((3.25, 2.25, 1.75));
                    return fp.Rect(0.25, -0.75, 6.25, 5.25);
                }

                case "SW":
                {
                    var fp = new Footprint { Description = "Anahtar, 2 bacak 5 mm", Height = 8 }
                        .Tht("1", 0, 0, 2.2, 1.2, PadShape.Rect).Tht("2", 5.0, 0, 2.2, 1.2);
                    return fp.Rect(-2.0, -3.0, 7.0, 3.0);
                }

                case "POT":
                {
                    var fp = new Footprint { Description = "Potansiyometre RV09", Height = 7 }
                        .Tht("1", 0, 0, 1.8, 1.0, PadShape.Rect).Tht("2", 2.5, 0, 1.8, 1.0).Tht("3", 5.0, 0, 1.8, 1.0);
                    return fp.Rect(-2.4, -9.5, 7.4, 1.5);
                }

                case "TRIM":
                {
                    var fp = new Footprint { Description = "Trimpot 3296W", Height = 10 }
                        .Tht("1", 0, 0, 1.6, 0.8, PadShape.Rect).Tht("2", 2.54, 0, 1.6, 0.8).Tht("3", 5.08, 0, 1.6, 0.8);
                    return fp.Rect(-2.3, -2.5, 7.4, 2.5);
                }

                case "RELAY":
                {
                    // Songle SRD-xxVDC-SL-C: coil 1/2, COM 3, NO 4, NC 5 (mm grid of the real part).
                    var fp = new Footprint { Description = "Röle SRD (Songle)", Height = 15.5 }
                        .Tht("3", 0, 0, 2.6, 1.3, PadShape.Rect).Tht("1", 2.0, 6.0, 2.4, 1.2).Tht("2", 2.0, -6.0, 2.4, 1.2)
                        .Tht("4", 14.2, -6.0, 2.6, 1.3).Tht("5", 14.2, 6.0, 2.6, 1.3);
                    return fp.Rect(-1.8, -7.8, 17.2, 7.8);
                }

                case "BUZZER":
                {
                    var fp = new Footprint { Description = "Buzzer Ø12 mm", Height = 9.5 }
                        .Tht("1", 0, 0, 2.0, 1.0, PadShape.Rect).Tht("2", 7.6, 0, 2.0, 1.0);
                    fp.SilkCircles.Add((3.8, 0, 6.2));
                    fp.CourtyardMinX = -2.6;
                    fp.CourtyardMaxX = 10.2;
                    fp.CourtyardMinY = -6.4;
                    fp.CourtyardMaxY = 6.4;
                    return fp;
                }

                case "WIRE":
                {
                    var fp = new Footprint { Description = "Kablo bağlantısı, 2 ped", Height = 2 }
                        .Tht("1", 0, 0, 2.4, 1.2, PadShape.Rect).Tht("2", 3.96, 0, 2.4, 1.2);
                    return fp.Rect(-1.8, -1.8, 5.76, 1.8);
                }

                case "ARDUINO":
                    return parts[1] == "NANO" ? ArduinoNano() : ArduinoUnoShield();

                case "ESP32":
                    return parts.Length > 1 && parts[1] == "DEVKITC" ? Esp32DevKitC() : Esp32DevKitV1();
            }

            return null;
        }

        private static Footprint Diode(double pitch, double drill, double bodyD, double bodyL, double pad)
        {
            var fp = new Footprint { Description = $"Diyot, {pitch:0.##} mm", Height = bodyD }
                .Tht("1", 0, 0, pad * 1.6, drill, PadShape.Rect).Tht("2", pitch, 0, pad * 1.6, drill);
            double x0 = (pitch - bodyL) / 2;
            fp.Rect(x0, -bodyD / 2, x0 + bodyL, bodyD / 2);
            // Cathode band.
            fp.Silkscreen.Add(new Segment2(x0 + 0.6, -bodyD / 2, x0 + 0.6, bodyD / 2));
            fp.Silkscreen.Add(new Segment2(x0 + 0.8, -bodyD / 2, x0 + 0.8, bodyD / 2));
            fp.CourtyardMinX = -pad;
            fp.CourtyardMaxX = pitch + pad;
            return fp;
        }

        private static Footprint TransistorRow(double pitch, double drill, double bodyW, double bodyH, string name)
        {
            var fp = new Footprint { Description = name, Height = name == "TO-220" ? 15 : 10 }
                .Tht("1", 0, 0, drill * 1.8, drill, PadShape.Rect, drill * 2.4)
                .Tht("2", pitch, 0, drill * 1.8, drill, PadShape.Oval, drill * 2.4)
                .Tht("3", 2 * pitch, 0, drill * 1.8, drill, PadShape.Oval, drill * 2.4);
            double cx = pitch;
            fp.Rect(cx - bodyW / 2, -bodyH + 1.5, cx + bodyW / 2, 1.5);
            if (name == "TO-220") fp.Silkscreen.Add(new Segment2(cx - bodyW / 2, -bodyH + 2.6, cx + bodyW / 2, -bodyH + 2.6));
            return fp;
        }

        private static Footprint Dip(int pins, double rowSpacing)
        {
            var fp = new Footprint { Description = $"DIP-{pins}", Height = 4.5 };
            int half = pins / 2;
            for (int i = 0; i < half; i++)
            {
                fp.Tht((i + 1).ToString(CultureInfo.InvariantCulture), 0, i * 2.54, 1.6, 0.8, i == 0 ? PadShape.Rect : PadShape.Oval, 2.0);
                fp.Tht((pins - i).ToString(CultureInfo.InvariantCulture), rowSpacing, i * 2.54, 1.6, 0.8, PadShape.Oval, 2.0);
            }

            double len = (half - 1) * 2.54;
            fp.Rect(1.0, -1.3, rowSpacing - 1.0, len + 1.3);
            fp.Silkscreen.Add(new Segment2(rowSpacing / 2 - 0.8, -1.3, rowSpacing / 2, -0.5));
            fp.Silkscreen.Add(new Segment2(rowSpacing / 2, -0.5, rowSpacing / 2 + 0.8, -1.3));
            fp.CourtyardMinX = -1.1;
            fp.CourtyardMaxX = rowSpacing + 1.1;
            return fp;
        }

        /// <summary>Arduino Uno R3 shield: real header positions (0.1 in grid, 160 mil offset on the digital header).</summary>
        private static Footprint ArduinoUnoShield()
        {
            var fp = new Footprint { Description = "Arduino Uno R3 shield başlıkları", Height = 8.5 };
            const double H = 53.34; // 2.1 in: header Y measured from the bottom edge, PCB Y points down.
            void Hole(string name, double xIn, double yIn) =>
                fp.Tht(name, xIn * 25.4, H - yIn * 25.4, 1.7, 1.0, name == "D0" || name == "A0" || name == "VIN" ? PadShape.Rect : PadShape.Circle);

            string[] power = { "NC", "IOREF", "RESET", "3V3", "5V", "GND2", "GND3", "VIN" };
            for (int i = 0; i < power.Length; i++) Hole(power[i], 1.0 + 0.1 * i, 0.1);
            for (int i = 0; i < 6; i++) Hole("A" + i.ToString(CultureInfo.InvariantCulture), 1.9 + 0.1 * i, 0.1);
            for (int i = 0; i <= 7; i++) Hole("D" + i.ToString(CultureInfo.InvariantCulture), 2.6 - 0.1 * i, 2.0);
            for (int i = 8; i <= 13; i++) Hole("D" + i.ToString(CultureInfo.InvariantCulture), 1.74 - 0.1 * (i - 8), 2.0);
            Hole("GND1", 1.14, 2.0);
            Hole("AREF", 1.04, 2.0);
            Hole("SDA", 0.94, 2.0);
            Hole("SCL", 0.84, 2.0);

            // Board outline 2.7 x 2.1 in.
            fp.Rect(0, 0, 68.58, 53.34, margin: 0);
            return fp;
        }

        /// <summary>Arduino Nano as a 0.6 in DIP-30 module.</summary>
        /// <summary>ESP32-DevKitC V4: two 1×19 headers, 2.54 mm pitch, rows 25.4 mm apart; board 54.4 × 27.9 mm, antenna end up.</summary>
        private static Footprint Esp32DevKitC()
        {
            var fp = new Footprint { Description = "ESP32-DevKitC V4 (2×19, 25.4 mm)", Height = 11 };
            string[] left = { "3V3", "EN", "VP", "VN", "IO34", "IO35", "IO32", "IO33", "IO25", "IO26", "IO27", "IO14", "IO12", "GND1", "IO13", "SD2", "SD3", "CMD", "5V" };
            string[] right = { "GND2", "IO23", "IO22", "TX", "RX", "IO21", "GND3", "IO19", "IO18", "IO5", "IO17", "IO16", "IO4", "IO0", "IO2", "IO15", "SD1", "SD0", "CLK" };
            for (int i = 0; i < 19; i++)
            {
                fp.Tht(left[i], 0, i * 2.54, 1.7, 1.0, i == 0 ? PadShape.Rect : PadShape.Circle);
                fp.Tht(right[i], 25.4, i * 2.54, 1.7, 1.0);
            }

            // Board outline; the WROOM module (antenna) overhangs the first header pin.
            fp.Rect(-1.25, -5.5, 26.65, 48.9, margin: 0.25);
            fp.Silkscreen.Add(new Segment2(3.5, -5.5, 3.5, 19.0));
            fp.Silkscreen.Add(new Segment2(21.9, -5.5, 21.9, 19.0));
            fp.Silkscreen.Add(new Segment2(3.5, 19.0, 21.9, 19.0));
            return fp;
        }

        /// <summary>DOIT ESP32 DevKit V1 (30 pin): two 1×15 headers, 2.54 mm pitch, rows 25.4 mm apart; board ≈ 51.5 × 28.3 mm.</summary>
        private static Footprint Esp32DevKitV1()
        {
            var fp = new Footprint { Description = "ESP32 DevKit V1 DOIT (2×15, 25.4 mm)", Height = 11 };
            string[] left = { "EN", "VP", "VN", "IO34", "IO35", "IO32", "IO33", "IO25", "IO26", "IO27", "IO14", "IO12", "IO13", "GND1", "VIN" };
            string[] right = { "IO23", "IO22", "TX", "RX", "IO21", "IO19", "IO18", "IO5", "IO17", "IO16", "IO4", "IO2", "IO15", "GND2", "3V3" };
            for (int i = 0; i < 15; i++)
            {
                fp.Tht(left[i], 0, i * 2.54, 1.7, 1.0, i == 0 ? PadShape.Rect : PadShape.Circle);
                fp.Tht(right[i], 25.4, i * 2.54, 1.7, 1.0);
            }

            fp.Rect(-1.45, -12.7, 26.85, 38.8, margin: 0.25);
            fp.Silkscreen.Add(new Segment2(3.5, -12.7, 3.5, 12.0));
            fp.Silkscreen.Add(new Segment2(21.9, -12.7, 21.9, 12.0));
            fp.Silkscreen.Add(new Segment2(3.5, 12.0, 21.9, 12.0));
            return fp;
        }

        private static Footprint ArduinoNano()
        {
            var fp = new Footprint { Description = "Arduino Nano (DIP-30, 15.24 mm)", Height = 8 };
            string[] left = { "D1", "D0", "RESET", "GND1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D10", "D11", "D12" };
            string[] right = { "VIN", "GND2", "RESET2", "5V", "A7", "A6", "A5", "A4", "A3", "A2", "A1", "A0", "AREF", "3V3", "D13" };
            for (int i = 0; i < 15; i++)
            {
                fp.Tht(left[i], 0, i * 2.54, 1.7, 1.0, i == 0 ? PadShape.Rect : PadShape.Circle);
                fp.Tht(right[i], 15.24, i * 2.54, 1.7, 1.0);
            }

            return fp.Rect(-1.3, -6.0, 16.54, 14 * 2.54 + 4.0);
        }
    }
}
