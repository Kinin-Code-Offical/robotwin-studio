using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RobotWin.Studio.Theme
{
    /// <summary>
    /// [align] Where a line of text looks centred: between its baseline and the top of its capitals (CSS's text-box-trim
    /// "cap alphabetic", Figma's cap-height alignment), not the middle of its line box, which sits higher because the box
    /// keeps room for descenders and accents. An icon, a status dot or a badge beside a label is put on that middle.
    /// </summary>
    public static class TextOptics
    {
        private static readonly Dictionary<(string, double, int, int), (double Baseline, double CapTop)> Cache = new Dictionary<(string, double, int, int), (double, double)>();

        /// <summary>The cap-height middle of a text block's first line, from its top (device-independent pixels).</summary>
        public static double CapCentre(TextBlock t)
        {
            var key = (t.FontFamily.Source, t.FontSize, t.FontWeight.ToOpenTypeWeight(), t.FontStyle == FontStyles.Normal ? 0 : 1);
            if (!Cache.TryGetValue(key, out var m))
            {
                // measured with the font as it renders (fallbacks included): the baseline, and the top of a capital H's ink
                double dip = 1;
                try
                {
                    dip = VisualTreeHelper.GetDpi(t).PixelsPerDip;
                }
                catch (InvalidOperationException)
                {
                }

                var ft = new FormattedText("H", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize, Brushes.Black, dip);
                var ink = ft.BuildGeometry(new Point(0, 0)).Bounds;
                m = (ft.Baseline, ink.IsEmpty ? ft.Baseline - t.FontSize * 0.7 : ink.Top);
                Cache[key] = m;
            }

            // a taller line (LineHeight, a block strategy) moves the first line down by half the extra room
            double lineBox = t.FontFamily.LineSpacing * t.FontSize;
            double extra = !double.IsNaN(t.LineHeight) && t.LineStackingStrategy == LineStackingStrategy.BlockLineHeight ? (t.LineHeight - lineBox) / 2 : 0;
            return t.Padding.Top + extra + (m.Baseline + m.CapTop) / 2;
        }

        /// <summary>The label beside an element in its row (the nearest text of the same panel); null when there is none.</summary>
        public static TextBlock? TextBeside(FrameworkElement e)
        {
            if (VisualTreeHelper.GetParent(e) is not Panel panel) return null;
            if (panel is StackPanel { Orientation: Orientation.Vertical } || panel is WrapPanel { Orientation: Orientation.Vertical }) return null;
            int at = panel.Children.IndexOf(e);
            TextBlock? best = null;
            int bestGap = int.MaxValue;
            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is not TextBlock t || !t.IsVisible || string.IsNullOrWhiteSpace(t.Text) || t.ActualHeight <= 0) continue;
                int gap = Math.Abs(i - at);
                if (gap < bestGap)
                {
                    best = t;
                    bestGap = gap;
                }
            }

            return best;
        }

        /// <summary>
        /// How far (down, DIPs, to half a pixel) the element's middle at <paramref name="centreY"/> (its own coordinates)
        /// must move to sit on the cap-height middle of the label beside it; 0 without a label, or when they do not share
        /// a line.
        /// </summary>
        public static double CapShift(FrameworkElement e, double centreY)
        {
            var text = TextBeside(e);
            if (text == null) return 0;
            Point p;
            try
            {
                p = text.TranslatePoint(new Point(0, CapCentre(text)), e);
            }
            catch (InvalidOperationException)
            {
                return 0;
            }

            double shift = p.Y - centreY;
            if (Math.Abs(shift) > Math.Max(3, e.ActualHeight / 2)) return 0;   // a label on another line
            return Math.Round(shift * 2) / 2;
        }

        // ---------------------------------------------------------------- for shapes beside a label (a status dot)

        public static readonly DependencyProperty AlignToTextProperty = DependencyProperty.RegisterAttached(
            "AlignToText", typeof(bool), typeof(TextOptics), new PropertyMetadata(false, OnAlignToText));

        public static bool GetAlignToText(DependencyObject d) => (bool)d.GetValue(AlignToTextProperty);

        public static void SetAlignToText(DependencyObject d, bool value) => d.SetValue(AlignToTextProperty, value);

        private static void OnAlignToText(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe || e.NewValue is not true) return;
            void Align(object? sender, EventArgs args)
            {
                double shift = CapShift(fe, fe.ActualHeight / 2);
                if (fe.RenderTransform is TranslateTransform tt && Math.Abs(tt.Y - shift) < 0.01) return;
                fe.RenderTransform = new TranslateTransform(0, shift);
            }

            fe.LayoutUpdated += Align;
        }
    }
}
