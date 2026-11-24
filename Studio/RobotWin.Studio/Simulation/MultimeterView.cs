using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Instruments;
using RobotWin.Studio.Editor;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Simulation
{
    /// <summary>
    /// Front of the handheld digital multimeter: reflective LCD with 6000-count digits,
    /// annunciators and bargraph, RANGE/HOLD keys, the rotary function switch and the four
    /// input jacks. Clicking a function on the dial or a jack operates the meter.
    /// </summary>
    public sealed class MultimeterView : FrameworkElement
    {
        private static readonly Color Body = Color.FromRgb(0x1A, 0x1B, 0x1D);
        private static readonly Color Bumper = Color.FromRgb(0xB8, 0x93, 0x2A);
        private static readonly Color LcdBack = Color.FromRgb(0x9C, 0xA6, 0x8C);
        private static readonly Color LcdOff = Color.FromRgb(0x6E, 0x76, 0x63);
        private static readonly Color LcdInk = Color.FromRgb(0x17, 0x1C, 0x14);
        private static readonly Color LcdGhost = Color.FromArgb(0x22, 0x17, 0x1C, 0x14);
        private static readonly Color Label = Color.FromRgb(0xC8, 0xC8, 0xCE);
        private static readonly Color Muted = Color.FromRgb(0x6C, 0x6C, 0x74);
        private static readonly Color Red = Color.FromRgb(0xD9, 0x3A, 0x3A);
        private static readonly Color KnobColor = Color.FromRgb(0x2A, 0x2B, 0x2E);

        private static readonly MultimeterMode[] DialOrder =
        {
            MultimeterMode.DcVoltage, MultimeterMode.AcVoltage, MultimeterMode.Resistance, MultimeterMode.Continuity,
            MultimeterMode.Diode, MultimeterMode.Capacitance, MultimeterMode.Frequency, MultimeterMode.AcCurrent, MultimeterMode.DcCurrent
        };

        private readonly List<(Rect Hit, Action Click)> _hits = new List<(Rect, Action)>();

        public MultimeterView()
        {
            ClipToBounds = true;
            var sim = Workspace.Current.Simulation;
            sim.Updated += (_, _) => InvalidateVisual();
            sim.InstrumentsChanged += (_, _) => InvalidateVisual();
            Cursor = Cursors.Arrow;
        }

        private static SimulationSession Sim => Workspace.Current.Simulation;

        private double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        private FormattedText Text(string s, Color c, double size, bool mono = false, bool bold = false) =>
            new FormattedText(RobotWin.Studio.Shell.Lang.T(s), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                mono ? Typefaces.Mono : bold ? Typefaces.UiSemi : Typefaces.Ui, size, SymbolRenderer.Brush(c), Dpi);

        private static Pen Pen(Color c, double w)
        {
            var p = new Pen(SymbolRenderer.Brush(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            p.Freeze();
            return p;
        }

        protected override Size MeasureOverride(Size availableSize) => new Size(300, 300);

        protected override void OnRender(DrawingContext dc)
        {
            _hits.Clear();
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            bool wide = h < 400 && w > 420;
            double bw = Math.Min(w - 8, wide ? 470 : 300), bh = h - 8;
            var body = new Rect((w - bw) / 2, 4, bw, bh);
            dc.DrawRoundedRectangle(SymbolRenderer.Brush(Bumper), null, body, 18, 18);
            var inner = body;
            inner.Inflate(-7, -7);
            dc.DrawRoundedRectangle(SymbolRenderer.Brush(Body), null, inner, 12, 12);

            var meter = Sim.ActiveMeter;
            var mode = meter?.Mode ?? (Sim.MeterPart == null ? Sim.MeterMode : MultimeterMode.DcVoltage);
            bool on = Sim.IsActive && meter != null;
            var reading = meter?.Reading;

            // Landscape (bench-style) layout when the dock is short, portrait (handheld) otherwise.
            bool landscape = wide;
            Rect lcd, keys, jacks;
            Point center;
            double r;
            if (landscape)
            {
                double leftW = Math.Min(260, inner.Width * 0.56);
                lcd = new Rect(inner.Left + 12, inner.Top + 12, leftW - 12, Math.Min(112, inner.Height * 0.48));
                keys = new Rect(lcd.Left, lcd.Bottom + 10, lcd.Width, 22);
                jacks = new Rect(lcd.Left, keys.Bottom + 10, lcd.Width, Math.Max(40, inner.Bottom - keys.Bottom - 12));
                double dialW = inner.Right - lcd.Right - 12;
                r = Math.Max(24, Math.Min(dialW / 2 - 34, inner.Height / 2 - 30));
                center = new Point(lcd.Right + 12 + dialW / 2, inner.Top + inner.Height / 2 + 8);
            }
            else
            {
                lcd = new Rect(inner.Left + 12, inner.Top + 12, inner.Width - 24, Math.Min(118, inner.Height * 0.32));
                keys = new Rect(lcd.Left + 2, lcd.Bottom + 10, lcd.Width - 4, 22);
                double jackH = 54;
                double dialTop = keys.Bottom + 32;
                double dialSpace = inner.Bottom - jackH - dialTop - 6;
                r = Math.Max(24, Math.Min(dialSpace / 2 - 14, inner.Width / 2 - 64));
                center = new Point(inner.Left + inner.Width / 2, dialTop + dialSpace / 2);
                jacks = new Rect(inner.Left + 10, inner.Bottom - jackH, inner.Width - 20, jackH - 6);
            }

            dc.DrawRoundedRectangle(SymbolRenderer.Brush(Color.FromRgb(0x0C, 0x0D, 0x0E)), null, Inflate(lcd, 3), 6, 6);
            dc.DrawRoundedRectangle(SymbolRenderer.Brush(on ? LcdBack : LcdOff), null, lcd, 4, 4);
            DrawLcd(dc, lcd, mode, reading, on, meter);

            double kw = (keys.Width - 16) / 3;
            Key(dc, new Rect(keys.Left, keys.Top, kw, keys.Height), "RANGE", () => meter?.StepRange());
            Key(dc, new Rect(keys.Left + kw + 8, keys.Top, kw, keys.Height), meter?.Hold == true ? "HOLD ●" : "HOLD", () =>
            {
                if (meter != null) meter.Hold = !meter.Hold;
            });
            Key(dc, new Rect(keys.Left + 2 * (kw + 8), keys.Top, kw, keys.Height), "AUTO", () => meter?.SetAutoRange());

            DrawDial(dc, center, r, mode);
            DrawJacks(dc, jacks, meter);
        }

        private static Rect Inflate(Rect r, double d)
        {
            r.Inflate(d, d);
            return r;
        }

        private void DrawLcd(DrawingContext dc, Rect lcd, MultimeterMode mode, MultimeterReading? reading, bool on, Multimeter? meter)
        {
            var ink = on ? LcdInk : Color.FromArgb(0x50, LcdInk.R, LcdInk.G, LcdInk.B);
            // Annunciators.
            double ax = lcd.Left + 8, ay = lcd.Top + 5;
            void Ann(string s, bool lit)
            {
                var ft = Text(s, lit && on ? ink : LcdGhost, 10.5, false, true);
                dc.DrawText(ft, new Point(ax, ay));
                ax += ft.Width + 9;
            }

            bool ac = mode == MultimeterMode.AcVoltage || mode == MultimeterMode.AcCurrent;
            Ann("AUTO", reading?.AutoRange ?? meter?.AutoRange ?? true);
            Ann("DC", mode == MultimeterMode.DcVoltage || mode == MultimeterMode.DcCurrent);
            Ann("AC", ac);
            Ann("TRMS", ac);
            Ann("HOLD", meter?.Hold == true);
            Ann("·)))", reading?.Beep == true || mode == MultimeterMode.Continuity && reading?.Beep == true);
            Ann("→|", mode == MultimeterMode.Diode);
            bool warn = reading?.Warning != null || meter != null && (meter.MilliAmpFuseBlown || meter.AmpFuseBlown);
            Ann("⚠", warn);

            // Digits.
            string digits = !on ? "" : reading == null || !reading.Valid && !reading.Overload ? "----" : reading.Overload ? "OL" : reading.Digits;
            if (on && meter != null && !meter.IsConnected && Sim.MeterPart == null) digits = "----";
            string unit = on && reading != null && reading.Valid ? reading.Unit : on && meter != null ? meter.Range.Unit : "";
            double size = Math.Min(46, lcd.Height * 0.44);
            string ghostText = System.Text.RegularExpressions.Regex.Replace(digits.Length == 0 || digits == "OL" || digits == "----" ? "8888" : digits.TrimStart('-'), "[0-9]", "8");
            while (ghostText.Count(char.IsDigit) < 4) ghostText = "8" + ghostText;
            var ghost = Text(ghostText, LcdGhost, size, true);
            var big = Text(digits, ink, size, true);
            var unitText = Text(unit, ink, Math.Max(13, size * 0.38), false, true);
            double right = lcd.Right - 12 - unitText.Width - 8;
            double dy = lcd.Top + lcd.Height * 0.20;
            dc.DrawText(ghost, new Point(right - ghost.Width, dy));
            dc.DrawText(big, new Point(right - big.Width, dy));
            dc.DrawText(unitText, new Point(lcd.Right - 12 - unitText.Width, dy + big.Height - unitText.Height - 4));

            if (on && reading != null && !double.IsNaN(reading.Secondary) && mode == MultimeterMode.Frequency)
            {
                var duty = Text("%" + reading.Secondary.ToString("0.0", CultureInfo.InvariantCulture), ink, 12, true);
                dc.DrawText(duty, new Point(lcd.Left + 10, dy + big.Height - duty.Height - 4));
            }

            // Bargraph (analog pointer) and range.
            double barY = lcd.Bottom - 16;
            int segments = 30;
            double segW = (lcd.Width - 70) / segments;
            double fill = 0;
            if (on && reading != null && reading.Valid && meter != null && !reading.Overload)
            {
                fill = Math.Min(1.0, Math.Abs(reading.Value) / Math.Max(1e-30, meter.Range.FullScale));
            }
            else if (on && reading != null && reading.Overload)
            {
                fill = 1.0;
            }

            for (int i = 0; i < segments; i++)
            {
                bool lit = on && (i + 0.5) / segments <= fill;
                var seg = new Rect(lcd.Left + 10 + i * segW, barY + (i % 5 == 0 ? 0 : 3), Math.Max(1, segW - 1.5), i % 5 == 0 ? 9 : 6);
                dc.DrawRectangle(SymbolRenderer.Brush(lit ? ink : LcdGhost), null, seg);
            }

            if (on && meter != null)
            {
                var range = Text(meter.Range.Label, ink, 10, true);
                dc.DrawText(range, new Point(lcd.Right - 8 - range.Width, barY - 2));
            }

            if (!Sim.IsActive)
            {
                var off = Text("KAPALI · simülasyonu başlatın", Color.FromArgb(0x90, LcdInk.R, LcdInk.G, LcdInk.B), 11);
                dc.DrawText(off, new Point(lcd.Left + 10, lcd.Top + lcd.Height / 2 - off.Height / 2));
            }
        }

        private void Key(DrawingContext dc, Rect r, string label, Action click)
        {
            dc.DrawRoundedRectangle(SymbolRenderer.Brush(KnobColor), Pen(Color.FromRgb(0x3A, 0x3B, 0x40), 1), r, 4, 4);
            var ft = Text(label, Label, 10.5, false, true);
            dc.DrawText(ft, new Point(r.Left + (r.Width - ft.Width) / 2, r.Top + (r.Height - ft.Height) / 2));
            _hits.Add((r, () =>
            {
                click();
                Sim.NotifyInstrumentsChanged();
            }));
        }

        private void DrawDial(DrawingContext dc, Point c, double r, MultimeterMode mode)
        {
            dc.DrawEllipse(SymbolRenderer.Brush(Color.FromRgb(0x10, 0x11, 0x12)), Pen(Color.FromRgb(0x33, 0x34, 0x38), 1), c, r + 6, r + 6);
            dc.DrawEllipse(SymbolRenderer.Brush(KnobColor), Pen(Color.FromRgb(0x46, 0x47, 0x4C), 1.5), c, r, r);
            int n = DialOrder.Length;
            for (int i = 0; i < n; i++)
            {
                // Positions spread over the upper 260° of the dial.
                double ang = (-220 + 260.0 * i / (n - 1)) * Math.PI / 180.0;
                var dir = new Vector(Math.Cos(ang), Math.Sin(ang));
                bool sel = DialOrder[i] == mode;
                var ft = Text(MultimeterSpec.ModeName(DialOrder[i]), sel ? Color.FromRgb(0xF2, 0xD1, 0x3A) : Label, sel ? 12.5 : 11.5, false, sel);
                var at = c + dir * (r + 22);
                var hit = new Rect(at.X - ft.Width / 2 - 4, at.Y - ft.Height / 2 - 2, ft.Width + 8, ft.Height + 4);
                dc.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
                dc.DrawLine(Pen(Muted, 1), c + dir * (r + 7), c + dir * (r + 11));
                var m = DialOrder[i];
                _hits.Add((hit, () => Sim.SetMeterMode(m)));
                if (sel)
                {
                    dc.DrawLine(Pen(Color.FromRgb(0xEE, 0xEE, 0xF0), 3), c + dir * (r * 0.25), c + dir * (r * 0.92));
                }
            }

            dc.DrawEllipse(SymbolRenderer.Brush(Color.FromRgb(0x22, 0x23, 0x26)), null, c, r * 0.3, r * 0.3);
        }

        private void DrawJacks(DrawingContext dc, Rect area, Multimeter? meter)
        {
            var names = new[] { ("10A", MultimeterJack.Amp), ("mA µA", MultimeterJack.MilliAmp), ("COM", (MultimeterJack)(-1)), ("VΩ→|", MultimeterJack.VoltOhm) };
            double step = area.Width / names.Length;
            bool probeMeter = Sim.MeterPart == null;
            for (int i = 0; i < names.Length; i++)
            {
                var (label, jack) = names[i];
                bool com = (int)jack < 0;
                var c = new Point(area.Left + step * (i + 0.5), area.Top + 14);
                bool active = probeMeter ? com || Sim.MeterJack == jack : true;
                var rim = com ? Color.FromRgb(0x50, 0x50, 0x55) : Red;
                dc.DrawEllipse(SymbolRenderer.Brush(Color.FromRgb(0x08, 0x08, 0x09)), Pen(rim, 2.5), c, 10, 10);
                if (active && probeMeter)
                {
                    bool placed = com ? Sim.MeterBlackNet != null : Sim.MeterRedNet != null;
                    dc.DrawEllipse(SymbolRenderer.Brush(com ? Color.FromRgb(0x2C, 0x2C, 0x30) : Red), null, c, placed ? 6.5 : 4, placed ? 6.5 : 4);
                }

                bool blown = meter != null && (jack == MultimeterJack.Amp && meter.AmpFuseBlown || jack == MultimeterJack.MilliAmp && meter.MilliAmpFuseBlown);
                var ft = Text(label + (blown ? " ✕" : string.Empty), blown ? Red : Label, 10);
                dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y + 13));
                if (jack == MultimeterJack.Amp || jack == MultimeterJack.MilliAmp)
                {
                    var fuse = Text(jack == MultimeterJack.Amp ? "F 11A" : "F 440mA", Muted, 8.5);
                    dc.DrawText(fuse, new Point(c.X - fuse.Width / 2, c.Y + 25));
                }

                if (!com && probeMeter)
                {
                    var j = jack;
                    _hits.Add((new Rect(c.X - 16, c.Y - 14, 32, 40), () => Sim.SetMeterJack(j)));
                }
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            var p = e.GetPosition(this);
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Hit.Contains(p))
                {
                    _hits[i].Click();
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            bool over = false;
            foreach (var (hit, _) in _hits) over |= hit.Contains(p);
            Cursor = over ? Cursors.Hand : Cursors.Arrow;
        }
    }
}
