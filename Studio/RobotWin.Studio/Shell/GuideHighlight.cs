using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [guide] A pulsing accent outline around the control the guide is talking about ("press this"). Lives in the
    /// element's adorner layer, so it follows the element and never takes its clicks. Flat colour, no gradient.
    /// </summary>
    public sealed class GuideHighlight : Adorner
    {
        private readonly Pen _pen;

        private GuideHighlight(UIElement element) : base(element)
        {
            IsHitTestVisible = false;
            var brush = new SolidColorBrush(Color.FromRgb(0xF0, 0x8A, 0x3C));
            _pen = new Pen(brush, 2.5);
            var pulse = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            BeginAnimation(OpacityProperty, pulse);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            dc.DrawRoundedRectangle(null, _pen, new Rect(-4, -4, size.Width + 8, size.Height + 8), 7, 7);
        }

        /// <summary>Outlines an element (null when it has no adorner layer yet); <see cref="Remove"/> takes it away.</summary>
        public static GuideHighlight? Show(FrameworkElement? element)
        {
            if (element == null || !element.IsVisible) return null;
            var layer = AdornerLayer.GetAdornerLayer(element);
            if (layer == null) return null;
            var h = new GuideHighlight(element);
            layer.Add(h);
            return h;
        }

        public void Remove()
        {
            BeginAnimation(OpacityProperty, null);
            AdornerLayer.GetAdornerLayer(AdornedElement)?.Remove(this);
        }
    }
}
