using System;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Editor;

namespace RobotWin.Studio.Pcb
{
    /// <summary>Fitted drawing of a footprint: pads, drills and silkscreen.</summary>
    public sealed class FootprintPreview : FrameworkElement
    {
        public static readonly DependencyProperty FootprintIdProperty = DependencyProperty.Register(
            nameof(FootprintId), typeof(string), typeof(FootprintPreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public string? FootprintId
        {
            get => (string?)GetValue(FootprintIdProperty);
            set => SetValue(FootprintIdProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(SymbolRenderer.Brush(Color.FromRgb(0x15, 0x16, 0x18)), null, new Rect(0, 0, ActualWidth, ActualHeight));
            var fp = FootprintId != null ? FootprintLibrary.Get(FootprintId) : null;
            if (fp == null) return;
            double w = fp.CourtyardMaxX - fp.CourtyardMinX + 2, h = fp.CourtyardMaxY - fp.CourtyardMinY + 2;
            double s = Math.Min(ActualWidth / w, ActualHeight / h);
            double ox = ActualWidth / 2 - (fp.CourtyardMinX + fp.CourtyardMaxX) / 2 * s;
            double oy = ActualHeight / 2 - (fp.CourtyardMinY + fp.CourtyardMaxY) / 2 * s;
            Point P(double x, double y) => new Point(ox + x * s, oy + y * s);

            var silk = new Pen(SymbolRenderer.Brush(Color.FromRgb(0xF1, 0xF3, 0xF5)), Math.Max(1, 0.15 * s));
            silk.Freeze();
            foreach (var seg in fp.Silkscreen) dc.DrawLine(silk, P(seg.X1, seg.Y1), P(seg.X2, seg.Y2));
            foreach (var (x, y, r) in fp.SilkCircles) dc.DrawEllipse(null, silk, P(x, y), r * s, r * s);

            var gold = SymbolRenderer.Brush(Color.FromRgb(0xB8, 0xB8, 0xBE));
            var hole = SymbolRenderer.Brush(Color.FromRgb(0x0E, 0x0E, 0x10));
            foreach (var pad in fp.Pads)
            {
                var c = P(pad.X, pad.Y);
                double pw = pad.Width * s, ph = pad.Height * s;
                if (pad.Shape == PadShape.Circle) dc.DrawEllipse(gold, null, c, pw / 2, ph / 2);
                else if (pad.Shape == PadShape.Rect) dc.DrawRectangle(gold, null, new Rect(c.X - pw / 2, c.Y - ph / 2, pw, ph));
                else dc.DrawRoundedRectangle(gold, null, new Rect(c.X - pw / 2, c.Y - ph / 2, pw, ph), Math.Min(pw, ph) / 2, Math.Min(pw, ph) / 2);
                if (pad.IsThroughHole) dc.DrawEllipse(hole, null, c, pad.Drill / 2 * s, pad.Drill / 2 * s);
            }
        }
    }
}
