using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace RobotWin.Studio.Theme
{
    /// <summary>
    /// Draws a 24×24 line-icon geometry with rounded strokes in the inherited
    /// foreground colour, scaled to <see cref="Size"/>.
    /// </summary>
    public sealed class Icon : FrameworkElement
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(Geometry), typeof(Icon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
            nameof(Size), typeof(double), typeof(Icon),
            new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness), typeof(double), typeof(Icon),
            new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
            nameof(Filled), typeof(bool), typeof(Icon),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner(typeof(Icon),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        public Geometry? Data
        {
            get => (Geometry?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        public double Thickness
        {
            get => (double)GetValue(ThicknessProperty);
            set => SetValue(ThicknessProperty, value);
        }

        public bool Filled
        {
            get => (bool)GetValue(FilledProperty);
            set => SetValue(FilledProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public Icon()
        {
            // [align] drawn again once the row is laid out, and whenever its label changes size, so it follows the label
            Loaded += (_, _) =>
            {
                Dispatcher.BeginInvoke(new System.Action(InvalidateVisual), System.Windows.Threading.DispatcherPriority.Loaded);
                if (TextOptics.TextBeside(this) is { } label && !ReferenceEquals(label, _label))
                {
                    _label = label;
                    label.SizeChanged += (_, _) => InvalidateVisual();
                }
            };
        }

        private System.Windows.Controls.TextBlock? _label;

        protected override Size MeasureOverride(Size availableSize) => new Size(Size, Size);

        /// <summary>[align] Where the drawing's middle is, down from the element's top (the alignment audit measures this).</summary>
        public double DrawnCentreY { get; private set; }

        protected override void OnRender(DrawingContext dc)
        {
            var data = Data;
            if (data == null) return;
            double scale = Size / 24.0;
            // Stroke width is specified in screen pixels; the geometry is drawn scaled.
            var pen = new Pen(Foreground, Thickness / scale)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            // [align] the icon in the middle of the room it is given (a row taller than the icon stretches it; drawn from the
            // top it sat above its label), then on the cap-height middle of the label beside it (TextOptics)
            double dx = Math.Max(0, (RenderSize.Width - Size) / 2), dy = Math.Max(0, (RenderSize.Height - Size) / 2);
            dy = Math.Round(dy * 2) / 2;
            dy += TextOptics.CapShift(this, dy + Size / 2);
            DrawnCentreY = dy + Size / 2;
            dc.PushTransform(new TranslateTransform(Math.Round(dx * 2) / 2, dy));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(Filled ? Foreground : null, pen, data);
            dc.Pop();
            dc.Pop();
        }
    }
}
