using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Editor
{
    /// <summary>Live state of a part while the simulation runs (all optional).</summary>
    public struct PartVisual
    {
        public double Brightness;
        public double AngleDeg;
        public bool Closed;
        public bool Energized;
        public bool Failed;
        public double Loudness;
        public double FlowPhase;
        public double Current;
        public bool HasState;

        /// <summary>Live display model (SSD1306 / character LCD) to draw inside the symbol.</summary>
        public object? Display;

        /// <summary>0 = cool, 1 = at its maximum temperature (drives the red-hot tint).</summary>
        public double Heat;

        public double TemperatureC;

        /// <summary>The failure is a short (cracked/conducting) rather than an open (burnt).</summary>
        public bool Shorted;

        /// <summary>Seconds since the part failed (fresh damage still glows).</summary>
        public double FailedAge;

        /// <summary>Multimeter: digits, unit and annunciators on its LCD.</summary>
        public string? MeterText;
        public string? MeterUnit;
        public string? MeterMode;
        public bool MeterBeep;
        public bool FuseBlown;

        /// <summary>Bench supply: output volts / amps and CV-CC state.</summary>
        public string? SupplyText;
        public string? SupplyMode;
    }

    /// <summary>Draws schematic symbols in symbol coordinates (1 unit = one 0.1" grid step).</summary>
    public static class SymbolRenderer
    {
        // [theme] Symbols are drawn in the paper's ink (Shell.CanvasColors): light lines on dark themes, dark on light ones.
        public static Color SymbolColor => Shell.CanvasColors.Symbol;
        public static Color PinColor => Shell.CanvasColors.Pin;
        public static Color FillColor => Shell.CanvasColors.Paper;
        public static Color BoardColor => Shell.CanvasColors.Board;
        private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new Dictionary<Color, SolidColorBrush>();

        public static SolidColorBrush Brush(Color c)
        {
            if (!BrushCache.TryGetValue(c, out var b))
            {
                b = new SolidColorBrush(c);
                b.Freeze();
                BrushCache[c] = b;
            }

            return b;
        }

        public static Color LedColor(string? name) => (name ?? "red").ToLowerInvariant() switch
        {
            "green" => Color.FromRgb(0x3D, 0xFF, 0x7A),
            "blue" => Color.FromRgb(0x3D, 0x8B, 0xFF),
            "yellow" => Color.FromRgb(0xFF, 0xE1, 0x3D),
            "orange" => Color.FromRgb(0xFF, 0x9A, 0x2E),
            "white" => Color.FromRgb(0xF4, 0xF8, 0xFF),
            "uv" => Color.FromRgb(0xA6, 0x5B, 0xFF),
            "ir" => Color.FromRgb(0xB0, 0x30, 0x40),
            _ => Color.FromRgb(0xFF, 0x3D, 0x3D)
        };

        /// <summary>Matrix from symbol coordinates to screen pixels.</summary>
        public static Matrix PartMatrix(SchematicPart part, double scale, Point origin)
        {
            double a = 1, b = 0, c = 0, d = 1;
            switch (((part.Rotation % 360) + 360) % 360)
            {
                case 90: a = 0; b = -1; c = 1; d = 0; break;
                case 180: a = -1; d = -1; break;
                case 270: a = 0; b = 1; c = -1; d = 0; break;
            }

            if (part.Mirror)
            {
                a = -a;
                b = -b;
            }

            return new Matrix(a * scale, b * scale, c * scale, d * scale,
                origin.X + part.Position.X * scale, origin.Y + part.Position.Y * scale);
        }

        private sealed class Ctx
        {
            public DrawingContext Dc = null!;
            public Pen Line = null!;
            public Pen Thin = null!;
            public Pen Thick = null!;
            public Brush Fill = null!;
            public Brush Ink = null!;
            public double Scale;
        }

        /// <summary>Draws the symbol body. The caller has pushed <see cref="PartMatrix"/>.</summary>
        public static void Draw(DrawingContext dc, PartDefinition def, IReadOnlyDictionary<string, string> parameters,
            double scale, Color ink, PartVisual visual)
        {
            if (visual.Failed) ink = Lerp(ink, Color.FromRgb(0x4A, 0x40, 0x3A), 0.55);
            var inkBrush = Brush(ink);
            var fill = FillColor;
            if (visual.Failed) fill = Color.FromRgb(0x16, 0x12, 0x10);
            else if (visual.Heat > 0.02) fill = HeatColor(visual.Heat);
            var c = new Ctx
            {
                Dc = dc,
                Scale = scale,
                Ink = inkBrush,
                Fill = Brush(fill),
                Line = Pen(inkBrush, 1.6 / scale),
                Thin = Pen(inkBrush, 1.1 / scale),
                Thick = Pen(inkBrush, 2.6 / scale)
            };

            // Leads from each pin tip towards the body.
            foreach (var pin in def.Pins) DrawLead(c, def, pin);

            switch (def.Symbol)
            {
                case SymbolKind.Resistor: Resistor(c); break;
                case SymbolKind.Potentiometer:
                    Resistor(c);
                    Arrow(c, new Point(0, -2.5), new Point(0, -0.7));
                    break;
                case SymbolKind.Thermistor:
                    Resistor(c);
                    Poly(c, c.Thin, false, (-1.6, 1.3), (-1.0, 1.3), (1.4, -1.3));
                    Text(c, "t°", 1.6, -1.2, 0.8);
                    break;
                case SymbolKind.Photoresistor:
                    Resistor(c);
                    Arrow(c, new Point(-1.6, -2.4), new Point(-0.6, -1.0));
                    Arrow(c, new Point(-0.4, -2.4), new Point(0.6, -1.0));
                    break;
                case SymbolKind.Fuse:
                    c.Dc.DrawRoundedRectangle(c.Fill, c.Line, new Rect(-2, -0.55, 4, 1.1), 0.5, 0.5);
                    c.Dc.DrawLine(c.Thin, new Point(-2, 0), new Point(2, 0));
                    break;
                case SymbolKind.Capacitor:
                    c.Dc.DrawLine(c.Thick, new Point(-0.4, -1.3), new Point(-0.4, 1.3));
                    c.Dc.DrawLine(c.Thick, new Point(0.4, -1.3), new Point(0.4, 1.3));
                    break;
                case SymbolKind.PolarCapacitor:
                    c.Dc.DrawLine(c.Thick, new Point(-0.4, -1.3), new Point(-0.4, 1.3));
                    Arc(c, c.Thick, new Point(0.9, -1.3), new Point(0.9, 1.3), 2.2, false);
                    Text(c, "+", -1.4, -1.6, 1.0);
                    break;
                case SymbolKind.Inductor:
                    for (int i = 0; i < 4; i++) Arc(c, c.Line, new Point(-2 + i, 0), new Point(-1 + i, 0), 0.5, true);
                    break;
                case SymbolKind.Diode:
                case SymbolKind.Zener:
                case SymbolKind.Schottky:
                case SymbolKind.Led:
                    DiodeBody(c, def.Symbol, parameters, visual);
                    break;
                case SymbolKind.Npn:
                case SymbolKind.Pnp:
                case SymbolKind.Darlington:
                    Bjt(c, def.Symbol != SymbolKind.Pnp, def.Symbol == SymbolKind.Darlington);
                    break;
                case SymbolKind.Nmos:
                case SymbolKind.Pmos:
                    Mosfet(c, def.Symbol == SymbolKind.Nmos);
                    break;
                case SymbolKind.OpAmp:
                    Poly(c, c.Line, true, (-2.6, -2.3), (-2.6, 2.3), (2.9, 0));
                    Text(c, "−", -2.2, -1.65, 1.1);
                    Text(c, "+", -2.2, 0.35, 1.1);
                    break;
                case SymbolKind.Regulator:
                    c.Dc.DrawRoundedRectangle(c.Fill, c.Line, new Rect(-2.6, -1.4, 5.2, 2.8), 0.3, 0.3);
                    Text(c, "IN", -2.3, -0.45, 0.75);
                    Text(c, "OUT", 0.8, -0.45, 0.75);
                    Text(c, "GND", -0.7, 0.2, 0.65);
                    break;
                case SymbolKind.Gate:
                    Gate(c, parameters.TryGetValue("function", out var fn) ? fn : "Nand");
                    break;
                case SymbolKind.Battery:
                    for (int k = 0; k < 2; k++)
                    {
                        double y = -0.9 + k * 1.2;
                        c.Dc.DrawLine(c.Line, new Point(-1.3, y), new Point(1.3, y));
                        c.Dc.DrawLine(c.Thick, new Point(-0.6, y + 0.5), new Point(0.6, y + 0.5));
                    }

                    Text(c, "+", 0.9, -2.2, 1.0);
                    break;
                case SymbolKind.VoltageSource:
                    c.Dc.DrawEllipse(c.Fill, c.Line, new Point(0, 0), 1.7, 1.7);
                    Text(c, "+", -0.35, -1.45, 1.0);
                    Text(c, "−", -0.3, 0.2, 1.0);
                    break;
                case SymbolKind.CurrentSource:
                    c.Dc.DrawEllipse(c.Fill, c.Line, new Point(0, 0), 1.7, 1.7);
                    Arrow(c, new Point(0, 1.1), new Point(0, -1.1));
                    break;
                case SymbolKind.SignalSource:
                    c.Dc.DrawEllipse(c.Fill, c.Line, new Point(0, 0), 1.7, 1.7);
                    switch (parameters.TryGetValue("shape", out var shape) ? shape : "square")
                    {
                        case "sine":
                            Sine(c, -1.1, 1.1, 0.55);
                            break;
                        case "triangle":
                            Poly(c, c.Line, false, (-1.1, 0.5), (-0.55, -0.5), (0, 0.5), (0.55, -0.5), (1.1, 0.5));
                            break;
                        default:
                            Poly(c, c.Line, false, (-1.1, 0.5), (-1.1, -0.5), (-0.2, -0.5), (-0.2, 0.5), (0.7, 0.5), (0.7, -0.5), (1.1, -0.5));
                            break;
                    }

                    break;
                case SymbolKind.Ground:
                    c.Dc.DrawLine(c.Line, new Point(-1.3, 0), new Point(1.3, 0));
                    c.Dc.DrawLine(c.Line, new Point(-0.8, 0.45), new Point(0.8, 0.45));
                    c.Dc.DrawLine(c.Line, new Point(-0.3, 0.9), new Point(0.3, 0.9));
                    break;
                case SymbolKind.PowerRail:
                    Poly(c, c.Line, true, (-0.9, 0.2), (0, -0.7), (0.9, 0.2));
                    break;
                case SymbolKind.PushButton:
                    PushButton(c, visual.HasState && visual.Closed);
                    break;
                case SymbolKind.Switch:
                    ToggleSwitch(c, visual.HasState ? visual.Closed : IsTrue(parameters, "closed"));
                    break;
                case SymbolKind.Relay:
                    Relay(c, visual.HasState && visual.Energized);
                    break;
                case SymbolKind.Motor:
                    c.Dc.DrawEllipse(c.Fill, c.Line, new Point(0, 0), 1.8, 1.8);
                    Text(c, "M", -0.55, -0.7, 1.3);
                    if (visual.HasState)
                    {
                        double a = visual.AngleDeg * Math.PI / 180.0;
                        var tip = new Point(Math.Cos(a) * 1.45, Math.Sin(a) * 1.45);
                        c.Dc.DrawEllipse(c.Ink, null, tip, 0.25, 0.25);
                    }

                    break;
                case SymbolKind.Servo:
                    Box(c, def, "SERVO");
                    ServoHorn(c, visual);
                    break;
                case SymbolKind.Buzzer:
                    Poly(c, c.Line, true, (-1.2, -0.6), (-0.4, -0.6), (0.6, -1.4), (0.6, 1.4), (-0.4, 0.6), (-1.2, 0.6));
                    if (visual.Loudness > 0.05)
                    {
                        for (int k = 1; k <= 2; k++)
                        {
                            Arc(c, c.Thin, new Point(1.0 + 0.4 * k, -0.6 - 0.3 * k), new Point(1.0 + 0.4 * k, 0.6 + 0.3 * k), 1.0 + 0.4 * k, false);
                        }
                    }

                    break;
                case SymbolKind.Lamp:
                    c.Dc.DrawEllipse(c.Fill, c.Line, new Point(0, 0), 1.5, 1.5);
                    c.Dc.DrawLine(c.Thin, new Point(-1.05, -1.05), new Point(1.05, 1.05));
                    c.Dc.DrawLine(c.Thin, new Point(-1.05, 1.05), new Point(1.05, -1.05));
                    break;
                case SymbolKind.Ammeter:
                    c.Dc.DrawEllipse(c.Fill, c.Line, new Point(0, 0), 1.6, 1.6);
                    Text(c, "A", -0.5, -0.75, 1.3);
                    break;
                case SymbolKind.Voltmeter:
                    c.Dc.DrawEllipse(c.Fill, c.Line, new Point(0, 0), 1.6, 1.6);
                    Text(c, "V", -0.5, -0.75, 1.3);
                    break;
                case SymbolKind.Connector:
                    Connector(c, def);
                    break;
                case SymbolKind.Module when def.SymbolVariant == "OLED" || def.SymbolVariant == "LCD":
                    DisplayModule(c, def, parameters, visual.Display);
                    break;
                case SymbolKind.Module:
                    Box(c, def, parameters.TryGetValue("model", out var model) ? model : def.Name);
                    break;
                case SymbolKind.Board:
                    Board(c, def);
                    break;
                case SymbolKind.Multimeter:
                    MultimeterSymbol(c, parameters, visual);
                    break;
                case SymbolKind.BenchSupply:
                    BenchSupplySymbol(c, parameters, visual);
                    break;
                default:
                    Box(c, def, def.Name);
                    break;
            }

            if (visual.Failed) Damage(c, def, visual);
        }

        /// <summary>Body colour of a part heating up: dark → deep red → orange → yellow-white.</summary>
        public static Color HeatColor(double heat)
        {
            double h = Math.Max(0.0, Math.Min(1.0, heat));
            if (h < 0.5) return Lerp(FillColor, Color.FromRgb(0x5C, 0x10, 0x08), h / 0.5);
            if (h < 0.85) return Lerp(Color.FromRgb(0x5C, 0x10, 0x08), Color.FromRgb(0xC8, 0x3C, 0x0C), (h - 0.5) / 0.35);
            return Lerp(Color.FromRgb(0xC8, 0x3C, 0x0C), Color.FromRgb(0xFF, 0xB0, 0x40), (h - 0.85) / 0.15);
        }

        public static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        // ---------------------------------------------------------------- primitives

        private static Pen Pen(Brush brush, double thickness)
        {
            var p = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            p.Freeze();
            return p;
        }

        private static bool IsTrue(IReadOnlyDictionary<string, string> p, string key) =>
            p.TryGetValue(key, out var v) && (v == "true" || v == "1" || v == "closed");

        private static void Poly(Ctx c, Pen pen, bool closed, params (double X, double Y)[] pts)
        {
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(new Point(pts[0].X, pts[0].Y), closed, closed);
                for (int i = 1; i < pts.Length; i++) s.LineTo(new Point(pts[i].X, pts[i].Y), true, true);
            }

            g.Freeze();
            c.Dc.DrawGeometry(closed ? c.Fill : null, pen, g);
        }

        private static void Arc(Ctx c, Pen pen, Point a, Point b, double radius, bool clockwise)
        {
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(a, false, false);
                s.ArcTo(b, new Size(radius, radius), 0, false, clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, true);
            }

            g.Freeze();
            c.Dc.DrawGeometry(null, pen, g);
        }

        private static void Arrow(Ctx c, Point from, Point to)
        {
            c.Dc.DrawLine(c.Thin, from, to);
            var dir = to - from;
            dir.Normalize();
            var n = new Vector(-dir.Y, dir.X);
            var p1 = to - dir * 0.5 + n * 0.3;
            var p2 = to - dir * 0.5 - n * 0.3;
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(to, true, true);
                s.LineTo(p1, true, true);
                s.LineTo(p2, true, true);
            }

            g.Freeze();
            c.Dc.DrawGeometry(c.Ink, null, g);
        }

        private static void Sine(Ctx c, double x0, double x1, double amp)
        {
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(new Point(x0, 0), false, false);
                for (int i = 1; i <= 24; i++)
                {
                    double x = x0 + (x1 - x0) * i / 24.0;
                    s.LineTo(new Point(x, -Math.Sin((x - x0) / (x1 - x0) * 2 * Math.PI) * amp), true, true);
                }
            }

            g.Freeze();
            c.Dc.DrawGeometry(null, c.Line, g);
        }

        /// <summary>Text inside the symbol, in symbol units; flipped parts keep it readable.</summary>
        private static void Text(Ctx c, string text, double x, double y, double size, Brush? ink = null)
        {
            var ft = new FormattedText(RobotWin.Studio.Shell.Lang.T(text), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typefaces.Ui, size, ink ?? c.Ink, 1.0);
            c.Dc.DrawText(ft, new Point(x, y));
        }

        private static void DrawLead(Ctx c, PartDefinition def, PinDefinition pin)
        {
            var tip = new Point(pin.X, pin.Y);
            Point inner;
            switch (def.Symbol)
            {
                case SymbolKind.Ground:
                case SymbolKind.PowerRail:
                    inner = new Point(0, 0);
                    break;
                case SymbolKind.Board:
                case SymbolKind.Connector:
                case SymbolKind.Module:
                case SymbolKind.Servo:
                case SymbolKind.Relay when pin.X > 0:
                    inner = new Point(pin.X - Math.Sign(pin.X) * 1.0, pin.Y);
                    break;
                case SymbolKind.Relay:
                    inner = new Point(-3.0, pin.Y * 0.5);
                    break;
                case SymbolKind.OpAmp:
                    inner = Math.Abs(pin.X) > 0.1 ? new Point(Math.Sign(pin.X) * 2.6, pin.Y) : new Point(0, Math.Sign(pin.Y) * 1.25);
                    if (pin.Name == "OUT") inner = new Point(2.9, 0);
                    break;
                case SymbolKind.Gate:
                    inner = Math.Abs(pin.Y) > 2.5 ? new Point(0, Math.Sign(pin.Y) * 1.6) : new Point(Math.Sign(pin.X) * 1.8, pin.Y);
                    if (pin.X > 0) inner = new Point(2.2, 0);
                    break;
                case SymbolKind.Regulator:
                    inner = Math.Abs(pin.Y) > 0.1 ? new Point(0, 1.4) : new Point(Math.Sign(pin.X) * 2.6, 0);
                    break;
                case SymbolKind.Npn:
                case SymbolKind.Pnp:
                case SymbolKind.Darlington:
                case SymbolKind.Nmos:
                case SymbolKind.Pmos:
                    inner = pin.X < 0 ? new Point(-0.6, 0) : new Point(1, Math.Sign(pin.Y) * 1.3);
                    if (def.Symbol is SymbolKind.Nmos or SymbolKind.Pmos && pin.X < 0) inner = new Point(-0.9, 0);
                    break;
                case SymbolKind.Potentiometer when pin.Name == "W":
                    return;
                case SymbolKind.Multimeter:
                    inner = new Point(pin.X, 4.5);
                    break;
                case SymbolKind.BenchSupply:
                    inner = new Point(0, Math.Sign(pin.Y) * def.HalfHeight);
                    break;
                case SymbolKind.Battery:
                case SymbolKind.VoltageSource:
                case SymbolKind.CurrentSource:
                case SymbolKind.SignalSource:
                    inner = new Point(0, Math.Sign(pin.Y) * (def.Symbol == SymbolKind.Battery ? 0.9 : 1.7));
                    if (def.Symbol == SymbolKind.Battery && pin.Y > 0) inner = new Point(0, 0.8);
                    break;
                case SymbolKind.Motor:
                case SymbolKind.Lamp:
                case SymbolKind.Ammeter:
                case SymbolKind.Voltmeter:
                    inner = new Point(Math.Sign(pin.X) * 1.6, 0);
                    break;
                case SymbolKind.Buzzer:
                    inner = new Point(pin.X < 0 ? -1.2 : 0.6, 0);
                    break;
                case SymbolKind.Capacitor:
                case SymbolKind.PolarCapacitor:
                    inner = new Point(Math.Sign(pin.X) * 0.4, 0);
                    break;
                case SymbolKind.Diode:
                case SymbolKind.Zener:
                case SymbolKind.Schottky:
                case SymbolKind.Led:
                    inner = new Point(Math.Sign(pin.X) * 1.0, 0);
                    break;
                case SymbolKind.PushButton:
                case SymbolKind.Switch:
                    inner = new Point(Math.Sign(pin.X) * 1.5, 0);
                    break;
                default:
                    inner = new Point(Math.Sign(pin.X) * 2.0, pin.Y);
                    break;
            }

            c.Dc.DrawLine(c.Line, tip, inner);
        }

        // ---------------------------------------------------------------- bodies

        private static void Resistor(Ctx c) =>
            c.Dc.DrawRoundedRectangle(c.Fill, c.Line, new Rect(-2, -0.65, 4, 1.3), 0.15, 0.15);

        private static void DiodeBody(Ctx c, SymbolKind kind, IReadOnlyDictionary<string, string> parameters, PartVisual visual)
        {
            bool led = kind == SymbolKind.Led;
            var ledColor = LedColor(parameters.TryGetValue("color", out var col) ? col : null);
            Brush fill = c.Fill;
            if (led)
            {
                double b = visual.HasState ? Math.Min(1.0, visual.Brightness) : 0.0;
                if (b > 0.02) fill = Brush(Color.FromArgb((byte)(40 + 200 * b), ledColor.R, ledColor.G, ledColor.B));
            }

            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(new Point(-1, -1.1), true, true);
                s.LineTo(new Point(-1, 1.1), true, true);
                s.LineTo(new Point(1, 0), true, true);
            }

            g.Freeze();
            c.Dc.DrawGeometry(fill, c.Line, g);

            switch (kind)
            {
                case SymbolKind.Zener:
                    Poly(c, c.Line, false, (0.6, -1.4), (1, -1.1), (1, 1.1), (1.4, 1.4));
                    break;
                case SymbolKind.Schottky:
                    Poly(c, c.Line, false, (0.6, -0.8), (0.6, -1.1), (1, -1.1), (1, 1.1), (1.4, 1.1), (1.4, 0.8));
                    break;
                default:
                    c.Dc.DrawLine(c.Line, new Point(1, -1.1), new Point(1, 1.1));
                    break;
            }

            if (led)
            {
                Arrow(c, new Point(0.1, -1.4), new Point(0.9, -2.3));
                Arrow(c, new Point(0.9, -1.2), new Point(1.7, -2.1));
            }
        }

        private static void Bjt(Ctx c, bool npn, bool darlington)
        {
            c.Dc.DrawEllipse(c.Fill, c.Line, new Point(0.3, 0), 2.1, 2.1);
            c.Dc.DrawLine(c.Thick, new Point(-0.6, -1.2), new Point(-0.6, 1.2));
            if (npn)
            {
                Poly(c, c.Line, false, (-0.6, -0.55), (1, -1.3));
                c.Dc.DrawLine(c.Line, new Point(-0.6, 0.55), new Point(1, 1.3));
                Arrow(c, new Point(0.1, 0.9), new Point(1, 1.3));
            }
            else
            {
                Poly(c, c.Line, false, (-0.6, 0.55), (1, 1.3));
                c.Dc.DrawLine(c.Line, new Point(1, -1.3), new Point(-0.6, -0.55));
                Arrow(c, new Point(0.6, -1.1), new Point(-0.4, -0.65));
            }

            if (darlington) Text(c, "2", 0.9, -0.5, 0.9);
        }

        private static void Mosfet(Ctx c, bool nChannel)
        {
            c.Dc.DrawEllipse(c.Fill, c.Line, new Point(0.3, 0), 2.1, 2.1);
            c.Dc.DrawLine(c.Line, new Point(-0.9, -1.1), new Point(-0.9, 1.1));
            foreach (double y in new[] { -1.0, 0.0, 1.0 }) c.Dc.DrawLine(c.Thick, new Point(-0.4, y - 0.35), new Point(-0.4, y + 0.35));
            double top = nChannel ? -1.3 : 1.3;
            c.Dc.DrawLine(c.Line, new Point(-0.4, -1.0), new Point(1, -1.0));
            c.Dc.DrawLine(c.Line, new Point(1, -1.0), new Point(1, -1.3));
            c.Dc.DrawLine(c.Line, new Point(-0.4, 1.0), new Point(1, 1.0));
            c.Dc.DrawLine(c.Line, new Point(1, 1.0), new Point(1, 1.3));
            c.Dc.DrawLine(c.Line, new Point(1, 0), new Point(1, nChannel ? 1.0 : -1.0));
            if (nChannel) Arrow(c, new Point(1, 0), new Point(-0.3, 0));
            else Arrow(c, new Point(-0.3, 0), new Point(0.9, 0));
            _ = top;
        }

        private static void Gate(Ctx c, string function)
        {
            string f = function.ToLowerInvariant();
            bool invert = f == "nand" || f == "nor" || f == "not" || f == "xnor";
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                if (f == "and" || f == "nand")
                {
                    s.BeginFigure(new Point(-1.8, -1.6), true, true);
                    s.LineTo(new Point(0.2, -1.6), true, true);
                    s.ArcTo(new Point(0.2, 1.6), new Size(1.6, 1.6), 0, false, SweepDirection.Clockwise, true, true);
                    s.LineTo(new Point(-1.8, 1.6), true, true);
                }
                else if (f == "not" || f == "buffer")
                {
                    s.BeginFigure(new Point(-1.6, -1.5), true, true);
                    s.LineTo(new Point(1.4, 0), true, true);
                    s.LineTo(new Point(-1.6, 1.5), true, true);
                }
                else
                {
                    s.BeginFigure(new Point(-1.9, -1.6), true, true);
                    s.QuadraticBezierTo(new Point(0.6, -1.6), new Point(1.8, 0), true, true);
                    s.QuadraticBezierTo(new Point(0.6, 1.6), new Point(-1.9, 1.6), true, true);
                    s.QuadraticBezierTo(new Point(-1.0, 0), new Point(-1.9, -1.6), true, true);
                }
            }

            g.Freeze();
            c.Dc.DrawGeometry(c.Fill, c.Line, g);
            if (f == "xor" || f == "xnor")
            {
                var x = new StreamGeometry();
                using (var s = x.Open())
                {
                    s.BeginFigure(new Point(-2.4, -1.6), false, false);
                    s.QuadraticBezierTo(new Point(-1.5, 0), new Point(-2.4, 1.6), true, true);
                }

                x.Freeze();
                c.Dc.DrawGeometry(null, c.Line, x);
            }

            if (invert) c.Dc.DrawEllipse(c.Fill, c.Line, new Point(2.05, 0), 0.3, 0.3);
            else c.Dc.DrawLine(c.Line, new Point(1.8, 0), new Point(2.3, 0));
        }

        private static void PushButton(Ctx c, bool pressed)
        {
            c.Dc.DrawEllipse(c.Ink, null, new Point(-1.5, 0), 0.22, 0.22);
            c.Dc.DrawEllipse(c.Ink, null, new Point(1.5, 0), 0.22, 0.22);
            double y = pressed ? -0.25 : -0.9;
            c.Dc.DrawLine(c.Line, new Point(-1.7, y), new Point(1.7, y));
            c.Dc.DrawLine(c.Line, new Point(0, y), new Point(0, y - 0.9));
            c.Dc.DrawLine(c.Line, new Point(-0.6, y - 0.9), new Point(0.6, y - 0.9));
        }

        private static void ToggleSwitch(Ctx c, bool closed)
        {
            c.Dc.DrawEllipse(c.Ink, null, new Point(-1.5, 0), 0.22, 0.22);
            c.Dc.DrawEllipse(c.Fill, c.Thin, new Point(1.5, 0), 0.22, 0.22);
            c.Dc.DrawLine(c.Line, new Point(-1.5, 0), closed ? new Point(1.4, 0) : new Point(1.2, -1.3));
        }

        private static void Relay(Ctx c, bool energized)
        {
            // Coil
            c.Dc.DrawRectangle(c.Fill, c.Line, new Rect(-3.6, -1.3, 1.4, 2.6));
            c.Dc.DrawLine(c.Thin, new Point(-3.6, 1.3), new Point(-2.2, -1.3));
            c.Dc.DrawLine(c.Line, new Point(-4, -2), new Point(-2.9, -2));
            c.Dc.DrawLine(c.Line, new Point(-2.9, -2), new Point(-2.9, -1.3));
            c.Dc.DrawLine(c.Line, new Point(-4, 2), new Point(-2.9, 2));
            c.Dc.DrawLine(c.Line, new Point(-2.9, 2), new Point(-2.9, 1.3));
            // Mechanical link
            var dash = new Pen(c.Ink, 1.0 / c.Scale) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) };
            dash.Freeze();
            c.Dc.DrawLine(dash, new Point(-2.2, 0), new Point(1.6, energized ? -0.7 : 0.7));
            // Contacts
            c.Dc.DrawLine(c.Line, new Point(3, 0), new Point(1.4, 0));
            c.Dc.DrawLine(c.Line, new Point(1.4, 0), energized ? new Point(2.7, -1.3) : new Point(2.7, 1.3));
            c.Dc.DrawLine(c.Line, new Point(3, -2), new Point(2.8, -2));
            c.Dc.DrawLine(c.Line, new Point(2.8, -2), new Point(2.8, -1.3));
            c.Dc.DrawLine(c.Line, new Point(3, 2), new Point(2.8, 2));
            c.Dc.DrawLine(c.Line, new Point(2.8, 2), new Point(2.8, 1.3));
            Text(c, "NO", 1.2, -2.6, 0.7);
            Text(c, "NC", 1.2, 1.9, 0.7);
        }

        private static void ServoHorn(Ctx c, PartVisual visual)
        {
            double angle = visual.HasState ? visual.AngleDeg : 90;
            var center = new Point(0.8, 0.2);
            double a = (180 - angle) * Math.PI / 180.0;
            var tip = new Point(center.X + Math.Cos(a) * 1.6, center.Y - Math.Sin(a) * 1.6);
            var pen = Pen(c.Ink, 2.2 / c.Scale);
            c.Dc.DrawLine(pen, center, tip);
            c.Dc.DrawEllipse(c.Fill, c.Line, center, 0.45, 0.45);
        }

        private static void Box(Ctx c, PartDefinition def, string title)
        {
            double w = def.HalfWidth, h = def.HalfHeight;
            c.Dc.DrawRoundedRectangle(c.Fill, c.Line, new Rect(-w, -h, 2 * w, 2 * h), 0.3, 0.3);
            string shown = Shorten(title, (int)Math.Max(3, (2 * w - 2.6) / 0.42));
            Text(c, shown, 0.6 - 0.21 * shown.Length, -0.45, 0.75);
            foreach (var pin in def.Pins)
            {
                double x = pin.X < 0 ? -w + 0.35 : w - 0.35 - 0.48 * pin.Name.Length;
                Text(c, pin.Name, x, pin.Y - 0.45, 0.75);
            }
        }

        private static void Connector(Ctx c, PartDefinition def)
        {
            double w = def.HalfWidth, h = def.HalfHeight;
            c.Dc.DrawRoundedRectangle(c.Fill, c.Line, new Rect(-w, -h, 2 * w, 2 * h), 0.3, 0.3);
            foreach (var pin in def.Pins)
            {
                c.Dc.DrawEllipse(c.Ink, null, new Point(-w + 0.6, pin.Y), 0.22, 0.22);
                Text(c, pin.Name, -w + 1.0, pin.Y - 0.42, 0.75);
            }
        }

        private static void Board(Ctx c, PartDefinition def)
        {
            double w = def.HalfWidth, h = def.HalfHeight;
            var board = Brush(BoardColor);
            c.Dc.DrawRoundedRectangle(board, c.Line, new Rect(-w, -h, 2 * w, 2 * h), 0.8, 0.8);
            var chip = Brush(Color.FromRgb(0x0E, 0x0E, 0x10));
            c.Dc.DrawRoundedRectangle(chip, null, new Rect(-1.8, -2.8, 3.6, 5.6), 0.2, 0.2);
            string label = string.IsNullOrEmpty(def.SymbolVariant) ? "UNO" : def.SymbolVariant;
            // Long names ("ESP32-DEVKIT-V1") go on two lines, sized to stay between the pin names.
            int cut = label.Length > 6 ? label.IndexOf('-') : -1;
            var lines = cut > 0 ? new[] { label.Substring(0, cut), label.Substring(cut + 1) } : new[] { label };
            double room = Math.Max(3.0, 2.0 * w - 8.0);
            double size = Math.Min(1.0, room / (0.6 * System.Linq.Enumerable.Max(lines, l => l.Length)));
            for (int i = 0; i < lines.Length; i++)
            {
                double y = -0.6 * size + (i - (lines.Length - 1) / 2.0) * 1.2 * size;
                // Printed on the black chip itself, in light grey whatever the paper (dark ink vanished on it in light themes).
                Text(c, lines[i], -0.3 * size * lines[i].Length, y, size, Brush(Color.FromRgb(0xC8, 0xC8, 0xCE)));
            }
            foreach (var pin in def.Pins)
            {
                double x = pin.X < 0 ? -w + 0.4 : w - 0.4 - 0.5 * pin.Name.Length;
                Text(c, pin.Name, x, pin.Y - 0.45, 0.8);
            }
        }

        /// <summary>OLED / LCD module: board outline, pin names and the live screen.</summary>
        private static void DisplayModule(Ctx c, PartDefinition def, IReadOnlyDictionary<string, string> parameters, object? display)
        {
            double w = def.HalfWidth, h = def.HalfHeight;
            c.Dc.DrawRoundedRectangle(Brush(Color.FromRgb(0x14, 0x2C, 0x52)), c.Line, new Rect(-w, -h, 2 * w, 2 * h), 0.5, 0.5);
            foreach (var pin in def.Pins) Text(c, pin.Name, -w + 0.35, pin.Y - 0.42, 0.7);
            bool oled = def.SymbolVariant == "OLED";
            double left = -w + 2.6, right = w - 0.7;
            double sw = right - left;
            double sh;
            if (oled)
            {
                int rows = parameters.TryGetValue("height", out var hh) && hh == "32" ? 32 : 64;
                sh = sw * rows / 128.0;
            }
            else
            {
                int cols = int.TryParse(parameters.TryGetValue("columns", out var cs) ? cs : "16", out var cc) ? cc : 16;
                int rows = int.TryParse(parameters.TryGetValue("rows", out var rs) ? rs : "2", out var rr) ? rr : 2;
                sh = sw * (rows * 9 + 1) / (cols * 6 + 1.0);
            }

            sh = Math.Min(sh, 2 * h - 1.2);
            var screen = new Rect(left, -sh / 2, sw, sh);
            var bezel = screen;
            bezel.Inflate(0.25, 0.25);
            c.Dc.DrawRectangle(Brush(Color.FromRgb(0x05, 0x05, 0x06)), null, bezel);
            switch (display)
            {
                case RobotTwin.CoreSim.Firmware.Devices.Ssd1306 o:
                    DisplayRenderer.DrawCrisp(c.Dc, DisplayRenderer.Oled(o, parameters.TryGetValue("color", out var col) ? col : "white"), screen);
                    break;
                case RobotTwin.CoreSim.Firmware.Devices.Pcf8574Lcd l:
                    DisplayRenderer.DrawCrisp(c.Dc, DisplayRenderer.Lcd(l, parameters.TryGetValue("backlight", out var bl) ? bl : "blue"), screen);
                    break;
                default:
                    if (!oled) c.Dc.DrawRectangle(Brush(Color.FromRgb(0x12, 0x20, 0x4A)), null, screen);
                    string model = parameters.TryGetValue("model", out var m) ? m : def.Name;
                    Text(c, model, left + 0.4, -0.45, 0.8);
                    break;
            }
        }

        // ---------------------------------------------------------------- damage

        private static readonly Color Char = Color.FromRgb(0x0C, 0x09, 0x07);
        private static readonly Color Soot = Color.FromRgb(0x2A, 0x22, 0x1C);

        /// <summary>
        /// What a failed part looks like: scorched and blackened bodies with soot, a fuse with its
        /// wire melted through, a burst and bulged electrolytic, a dark cracked LED, a cracked IC.
        /// Fresh damage still glows for a few seconds.
        /// </summary>
        private static void Damage(Ctx c, PartDefinition def, PartVisual visual)
        {
            double w = Math.Max(1.2, Math.Min(def.HalfWidth, 3.2)), h = Math.Max(1.0, Math.Min(def.HalfHeight, 3.2));
            double glow = Math.Max(0.0, 1.0 - visual.FailedAge / 6.0);
            var crack = Pen(Brush(Color.FromRgb(0x05, 0x04, 0x03)), 1.4 / c.Scale);
            var hot = glow > 0 ? Pen(Brush(Color.FromArgb((byte)(220 * glow), 0xFF, 0x7A, 0x1E)), 1.2 / c.Scale) : null;

            void Scorch(double rx, double ry, double alpha = 0.85)
            {
                var g = new RadialGradientBrush(Color.FromArgb((byte)(255 * alpha), Char.R, Char.G, Char.B), Color.FromArgb(0, Soot.R, Soot.G, Soot.B));
                g.GradientStops.Insert(1, new GradientStop(Color.FromArgb((byte)(200 * alpha), Soot.R, Soot.G, Soot.B), 0.55));
                g.Freeze();
                c.Dc.DrawEllipse(g, null, new Point(0, 0), rx, ry);
            }

            void Crack(params (double X, double Y)[] pts)
            {
                var g = new StreamGeometry();
                using (var s = g.Open())
                {
                    s.BeginFigure(new Point(pts[0].X, pts[0].Y), false, false);
                    for (int i = 1; i < pts.Length; i++) s.LineTo(new Point(pts[i].X, pts[i].Y), true, true);
                }

                g.Freeze();
                if (hot != null) c.Dc.DrawGeometry(null, Pen(Brush(Color.FromArgb((byte)(120 * glow), 0xFF, 0x60, 0x10)), 3.2 / c.Scale), g);
                c.Dc.DrawGeometry(null, crack, g);
                if (hot != null) c.Dc.DrawGeometry(null, hot, g);
            }

            switch (def.Symbol)
            {
                case SymbolKind.Fuse:
                {
                    // Smoked glass, wire melted through with beads at the broken ends.
                    c.Dc.DrawRoundedRectangle(Brush(Color.FromArgb(0xC0, 0x2A, 0x26, 0x22)), c.Line, new Rect(-2, -0.55, 4, 1.1), 0.5, 0.5);
                    var wire = Pen(Brush(Color.FromRgb(0xA0, 0xA0, 0xA6)), 1.1 / c.Scale);
                    c.Dc.DrawLine(wire, new Point(-2, 0), new Point(-0.45, 0.05));
                    c.Dc.DrawLine(wire, new Point(0.5, -0.08), new Point(2, 0));
                    c.Dc.DrawEllipse(Brush(Color.FromRgb(0xB8, 0xB8, 0xBE)), null, new Point(-0.4, 0.06), 0.16, 0.16);
                    c.Dc.DrawEllipse(Brush(Color.FromRgb(0xB8, 0xB8, 0xBE)), null, new Point(0.45, -0.08), 0.14, 0.14);
                    c.Dc.DrawEllipse(Brush(Color.FromArgb(0x70, 0x10, 0x0C, 0x08)), null, new Point(0, 0), 0.9, 0.45);
                    break;
                }

                case SymbolKind.PolarCapacitor:
                {
                    // Bulged can: the plates are pushed apart, the vent has split and electrolyte leaked.
                    Scorch(1.9, 1.7, 0.75);
                    var bulge = new StreamGeometry();
                    using (var s = bulge.Open())
                    {
                        s.BeginFigure(new Point(-0.45, -1.3), false, false);
                        s.QuadraticBezierTo(new Point(-1.25, 0), new Point(-0.45, 1.3), true, true);
                    }

                    bulge.Freeze();
                    c.Dc.DrawGeometry(null, c.Thick, bulge);
                    Crack((0.6, -1.7), (0.95, -1.05), (0.7, -0.6));
                    Crack((1.3, -1.7), (0.95, -1.05), (1.3, -0.5));
                    c.Dc.DrawEllipse(Brush(Color.FromArgb(0x90, 0x6A, 0x4A, 0x1C)), null, new Point(0.9, 1.5), 0.9, 0.35);
                    break;
                }

                case SymbolKind.Capacitor:
                    Scorch(1.4, 1.5);
                    Crack((-0.4, -1.3), (0.1, -0.2), (-0.2, 0.4), (0.4, 1.3));
                    break;
                case SymbolKind.Led:
                case SymbolKind.Diode:
                case SymbolKind.Zener:
                case SymbolKind.Schottky:
                {
                    // Dark, dead junction with a crack through the body.
                    var g = new StreamGeometry();
                    using (var s = g.Open())
                    {
                        s.BeginFigure(new Point(-1, -1.1), true, true);
                        s.LineTo(new Point(-1, 1.1), true, true);
                        s.LineTo(new Point(1, 0), true, true);
                    }

                    g.Freeze();
                    c.Dc.DrawGeometry(Brush(Color.FromRgb(0x1C, 0x18, 0x16)), c.Line, g);
                    Scorch(1.5, 1.3, 0.55);
                    Crack((-0.8, -0.9), (-0.2, -0.1), (-0.5, 0.3), (0.2, 0.8));
                    break;
                }

                case SymbolKind.Npn:
                case SymbolKind.Pnp:
                case SymbolKind.Darlington:
                case SymbolKind.Nmos:
                case SymbolKind.Pmos:
                    Scorch(2.0, 2.0, 0.8);
                    Crack((-0.4, -1.6), (0.2, -0.6), (-0.2, 0.1), (0.6, 0.9), (0.3, 1.6));
                    c.Dc.DrawEllipse(Brush(Char), null, new Point(0.3, 0.1), 0.35, 0.3);
                    break;
                case SymbolKind.OpAmp:
                case SymbolKind.Regulator:
                case SymbolKind.Gate:
                case SymbolKind.Module:
                case SymbolKind.Board:
                case SymbolKind.Servo:
                    // Cracked package with a blown hole in the die.
                    Scorch(Math.Min(w, 2.4), Math.Min(h, 1.8), 0.85);
                    c.Dc.DrawEllipse(Brush(Char), Pen(Brush(Color.FromRgb(0x3A, 0x2E, 0x24)), 0.8 / c.Scale), new Point(0.1, 0), 0.45, 0.4);
                    Crack((0.1, 0), (-1.2, -1.0), (-1.8, -1.2));
                    Crack((0.1, 0), (1.3, 0.7), (1.9, 1.3));
                    Crack((0.1, 0), (0.6, -1.1));
                    break;
                case SymbolKind.Battery:
                    Scorch(2.0, 2.2, 0.8);
                    Crack((-1.1, -1.5), (-0.3, -0.5), (0.4, -0.9), (1.1, 0.2));
                    break;
                case SymbolKind.Multimeter:
                    break;
                default:
                    // Resistors, lamps, coils, motors: charred body, soot and a crack.
                    Scorch(Math.Max(1.6, w * 0.9), Math.Max(1.1, h * 0.9));
                    Crack((-w * 0.45, -0.5), (-0.3, 0.15), (0.3, -0.25), (w * 0.45, 0.45));
                    break;
            }

            if (glow > 0.05 && def.Symbol != SymbolKind.Multimeter)
            {
                var ember = new RadialGradientBrush(Color.FromArgb((byte)(150 * glow), 0xFF, 0x70, 0x18), Color.FromArgb(0, 0xFF, 0x40, 0x00));
                ember.Freeze();
                c.Dc.DrawEllipse(ember, null, new Point(0, 0), w * 0.6, h * 0.6);
            }
        }

        // ---------------------------------------------------------------- instruments

        private static readonly Color MeterBumper = Color.FromRgb(0xB8, 0x93, 0x2A);
        private static readonly Color MeterLcd = Color.FromRgb(0x9C, 0xA6, 0x8C);
        private static readonly Color MeterInk = Color.FromRgb(0x17, 0x1C, 0x14);

        private static void MonoText(Ctx c, string text, double x, double y, double size, Color color, bool alignRight = false, double width = 0)
        {
            var ft = new FormattedText(RobotWin.Studio.Shell.Lang.T(text), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typefaces.Mono, size, Brush(color), 1.0);
            double left = alignRight ? x + width - ft.Width : x;
            c.Dc.DrawText(ft, new Point(left, y));
        }

        /// <summary>Handheld DMM: holster, live LCD, dial and the four jacks at the bottom.</summary>
        private static void MultimeterSymbol(Ctx c, IReadOnlyDictionary<string, string> parameters, PartVisual visual)
        {
            c.Dc.DrawRoundedRectangle(Brush(MeterBumper), null, new Rect(-4, -4.5, 8, 9), 0.9, 0.9);
            c.Dc.DrawRoundedRectangle(Brush(Color.FromRgb(0x1A, 0x1B, 0x1D)), null, new Rect(-3.6, -4.1, 7.2, 8.2), 0.6, 0.6);
            var lcd = new Rect(-3.2, -3.7, 6.4, 2.6);
            bool on = visual.HasState;
            c.Dc.DrawRoundedRectangle(Brush(on ? MeterLcd : Color.FromRgb(0x6E, 0x76, 0x63)), null, lcd, 0.2, 0.2);
            string mode = visual.MeterMode ?? (parameters.TryGetValue("mode", out var m) ? m.ToUpperInvariant() : "DCV");
            MonoText(c, mode, lcd.Left + 0.2, lcd.Top + 0.1, 0.55, MeterInk);
            if (visual.MeterBeep) MonoText(c, "·)))", lcd.Left + 2.4, lcd.Top + 0.1, 0.55, MeterInk);
            if (visual.FuseBlown) MonoText(c, "FUSE", lcd.Left + 4.4, lcd.Top + 0.1, 0.55, Color.FromRgb(0x8A, 0x10, 0x10));
            if (on)
            {
                string digits = visual.MeterText ?? "----";
                MonoText(c, digits, lcd.Left + 0.2, lcd.Top + 0.75, 1.35, MeterInk, true, lcd.Width - 1.6);
                MonoText(c, visual.MeterUnit ?? string.Empty, lcd.Right - 1.35, lcd.Top + 1.35, 0.62, MeterInk);
            }

            // Dial
            var knob = new Point(0, 0.9);
            c.Dc.DrawEllipse(Brush(Color.FromRgb(0x2A, 0x2B, 0x2E)), Pen(Brush(Color.FromRgb(0x46, 0x47, 0x4C)), 1.0 / c.Scale), knob, 1.15, 1.15);
            c.Dc.DrawLine(Pen(Brush(Color.FromRgb(0xEE, 0xEE, 0xF0)), 1.6 / c.Scale), knob, new Point(knob.X - 0.6, knob.Y - 0.85));

            // Jacks (A, mA, COM, VΩ)
            string[] names = { "A", "mA", "COM", "VΩ" };
            for (int i = 0; i < 4; i++)
            {
                double x = -3 + 2 * i;
                bool com = i == 2;
                c.Dc.DrawEllipse(Brush(Color.FromRgb(0x08, 0x08, 0x09)), Pen(Brush(com ? Color.FromRgb(0x60, 0x60, 0x66) : Color.FromRgb(0xD9, 0x3A, 0x3A)), 1.6 / c.Scale), new Point(x, 3.45), 0.42, 0.42);
                var ft = new FormattedText(names[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typefaces.Ui, 0.5, Brush(Color.FromRgb(0xA0, 0xA0, 0xA8)), 1.0);
                c.Dc.DrawText(ft, new Point(x - ft.Width / 2, 2.45));
            }
        }

        /// <summary>Bench supply: front panel with V and A readouts, CV/CC lamps and the output terminals.</summary>
        private static void BenchSupplySymbol(Ctx c, IReadOnlyDictionary<string, string> parameters, PartVisual visual)
        {
            var body = new Rect(-3, -2.4, 6, 4.8);
            c.Dc.DrawRoundedRectangle(c.Fill, c.Line, body, 0.35, 0.35);
            var lcd = new Rect(-2.6, -2.0, 5.2, 2.6);
            c.Dc.DrawRoundedRectangle(Brush(Color.FromRgb(0x06, 0x0A, 0x07)), null, lcd, 0.15, 0.15);
            var green = Color.FromRgb(0x5B, 0xE0, 0x8B);
            var amber = Color.FromRgb(0xF2, 0xB0, 0x3A);
            string v = parameters.TryGetValue("voltage", out var vs) ? vs : "12";
            string a = parameters.TryGetValue("ilimit", out var a0) ? a0 : "1";
            string line1, line2;
            if (visual.SupplyText != null)
            {
                var parts = visual.SupplyText.Split('|');
                line1 = parts[0];
                line2 = parts.Length > 1 ? parts[1] : string.Empty;
            }
            else
            {
                // [psu] AC shown as its RMS with "~" (and the frequency), AC on DC as both.
                string mode = parameters.TryGetValue("mode", out var md) ? md : "dc";
                string rms = parameters.TryGetValue("vrms", out var r0) ? r0 : "12";
                string hz = parameters.TryGetValue("frequency", out var f0) ? f0 : "50";
                line1 = mode == "ac" ? (SiValue(rms) ?? rms) + "V~"
                    : mode == "acdc" ? (SiValue(v) ?? v) + "+" + (SiValue(rms) ?? rms) + "~"
                    : (SiValue(v) ?? v) + "V";
                line2 = (SiValue(a) ?? a) + "A" + (mode == "dc" ? string.Empty : " " + (SiValue(hz) ?? hz) + "Hz");
            }

            MonoText(c, line1, lcd.Left + 0.2, lcd.Top + 0.15, 1.0, green, true, lcd.Width - 0.4);
            MonoText(c, line2, lcd.Left + 0.2, lcd.Top + 1.3, 1.0, amber, true, lcd.Width - 0.4);
            bool cc = visual.SupplyMode == "CC";
            bool off = visual.SupplyMode == "OFF";
            void Lamp(string text, double x, bool lit, Color col)
            {
                c.Dc.DrawEllipse(Brush(lit ? col : Color.FromRgb(0x2A, 0x2A, 0x2E)), null, new Point(x, 1.25), 0.22, 0.22);
                MonoText(c, text, x + 0.3, 0.95, 0.55, Color.FromRgb(0xA0, 0xA0, 0xA8));
            }

            Lamp("CV", -2.4, visual.HasState && !cc && !off, green);
            Lamp("CC", -0.6, visual.HasState && cc, Color.FromRgb(0xE5, 0x48, 0x4D));
            MonoText(c, off ? "OFF" : "ON", 1.3, 0.95, 0.55, off ? Color.FromRgb(0x6C, 0x6C, 0x74) : green);
            Text(c, "+", 0.4, -2.95, 0.9);
            Text(c, "−", 0.4, 2.15, 0.9);
        }

        private static string? SiValue(string raw) =>
            RobotTwin.CoreSim.Design.SiValue.TryParse(raw, out double v) ? v.ToString("0.##", CultureInfo.InvariantCulture) : null;

        private static string Shorten(string s, int max) => s.Length <= max ? s : s.Substring(0, Math.Max(1, max - 1)) + "…";
    }

    internal static class Typefaces
    {
        public static readonly Typeface Ui = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public static readonly Typeface UiSemi = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        public static readonly Typeface Mono = new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }
}
