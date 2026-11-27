using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Editor
{
    /// <summary>
    /// [layers] The circuit's simulation layers (D3), each switched on or off from the toolbar: the wires coloured by their
    /// voltage, the current's flow along the parts, how hard each part works (the highest of its ratings in use: power,
    /// current, voltage, temperature; a glow from amber near the limit to red over it, with its percentage) and the faults
    /// (a burnt part ringed in red with what burnt it, findable when zoomed out).
    /// </summary>
    public sealed partial class SchematicCanvas
    {
        /// <summary>Glows round the parts working near or over a limit.</summary>
        private void DrawStressLayer(DrawingContext dc)
        {
            foreach (var part in SheetParts)
            {
                var devices = Sim.Compiled?.DevicesOf(part.RefDes);
                if (devices == null || devices.Count == 0 || devices.Any(d => d.IsFailed)) continue;
                double ratio = 0;
                string what = string.Empty;
                foreach (var d in devices)
                {
                    foreach (var s in CircuitDiagnostics.Stresses(d))
                    {
                        if (double.IsNaN(s.Ratio) || s.Ratio <= ratio) continue;
                        ratio = s.Ratio;
                        what = s.Quantity;
                    }
                }

                if (ratio < 0.6) continue;   // comfortable: nothing drawn, so the sheet stays readable
                var r = ScreenRect(part, 0.5);
                double t = Math.Min(1, (ratio - 0.6) / 0.4);   // 0.6 → 1.0 of the limit: amber → red
                var c = ratio >= 1 ? Color.FromRgb(0xE5, 0x48, 0x4D) : Blend(Color.FromRgb(0xE8, 0xA3, 0x3D), Color.FromRgb(0xE5, 0x48, 0x4D), t);
                dc.DrawRoundedRectangle(SymbolRenderer.Brush(Color.FromArgb((byte)(0x22 + 0x30 * t), c.R, c.G, c.B)), MakePen(Color.FromArgb(0xC0, c.R, c.G, c.B), 1.2, false), r, 6, 6);
                var label = Text(Lang.T(what) + $" %{ratio * 100:0}", 10.5, c, false, false);
                dc.DrawText(label, new Point(r.Right - label.Width, r.Top - label.Height - 2));
            }
        }

        /// <summary>A red ring and the reason round each burnt part.</summary>
        private void DrawFaultLayer(DrawingContext dc)
        {
            var red = Color.FromRgb(0xE5, 0x48, 0x4D);
            foreach (var part in SheetParts)
            {
                var devices = Sim.Compiled?.DevicesOf(part.RefDes);
                var failed = devices?.FirstOrDefault(d => d.IsFailed);
                if (failed == null) continue;
                var r = ScreenRect(part, 0.7);
                dc.DrawRoundedRectangle(null, MakePen(red, 2, false), r, 8, 8);
                string why = Lang.T(string.IsNullOrWhiteSpace(failed.FailureReason) ? "hasarlı" : failed.FailureReason!);
                var label = Text("⚠ " + part.RefDes + ": " + why, 11, red, true, false);
                var at = new Point(r.Left, r.Bottom + 3);
                dc.DrawRoundedRectangle(SymbolRenderer.Brush(PanelColor), null, new Rect(at.X - 4, at.Y - 2, label.Width + 8, label.Height + 4), 4, 4);
                dc.DrawText(label, at);
            }
        }

        private Rect ScreenRect(SchematicPart part, double margin)
        {
            var b = PartBounds(part);
            b.Inflate(margin, margin);
            var a = ToScreen(b.Left, b.Top);
            var c = ToScreen(b.Right, b.Bottom);
            return new Rect(a, c);
        }

        private static Color Blend(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}
