using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [multiselect] Unity's rectangle selection in the Montaj and Dünya view: Shift+drag on empty space draws a
    /// rectangle and selects every visible object whose middle falls inside it (Ctrl+Shift adds them to the
    /// selection). A plain drag on empty space still turns the camera.
    /// </summary>
    public sealed partial class WorldScene
    {
        private Point? _marquee;
        private bool _marqueeAdd;
        private Rectangle? _marqueeRect;

        /// <summary>Starts a rectangle on empty space when Shift is held; true when it did.</summary>
        private bool MarqueeDown(Point p)
        {
            var mods = Keyboard.Modifiers;
            if (!mods.HasFlag(ModifierKeys.Shift) || World == null || Tool != WorldTool.Select) return false;
            _marquee = p;
            _marqueeAdd = mods.HasFlag(ModifierKeys.Control);
            if (_marqueeRect == null)
            {
                _marqueeRect = new Rectangle
                {
                    StrokeThickness = 1,
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x6E, 0x9B, 0xFF)),
                    Fill = new SolidColorBrush(Color.FromArgb(0x24, 0x6E, 0x9B, 0xFF))   // one flat tint
                };
                Children.Add(_marqueeRect);
            }

            PlaceMarquee(p);
            _marqueeRect.Visibility = Visibility.Visible;
            CaptureMouse();
            return true;
        }

        private bool MarqueeMove(Point p)
        {
            if (_marquee == null) return false;
            PlaceMarquee(p);
            return true;
        }

        private void PlaceMarquee(Point p)
        {
            if (_marqueeRect == null || _marquee is not { } a) return;
            var r = new Rect(a, p);
            _marqueeRect.Margin = new Thickness(r.X, r.Y, 0, 0);
            _marqueeRect.Width = Math.Max(1, r.Width);
            _marqueeRect.Height = Math.Max(1, r.Height);
        }

        /// <summary>Ends the rectangle: selects what is inside (a click without a drag clears the selection).</summary>
        private bool MarqueeUp(Point p)
        {
            if (_marquee is not { } a) return false;
            _marquee = null;
            if (_marqueeRect != null) _marqueeRect.Visibility = Visibility.Collapsed;
            ReleaseMouseCapture();
            var r = new Rect(a, p);
            var inside = r.Width < 3 && r.Height < 3 ? new List<string>() : ObjectsInside(r);
            var ids = _marqueeAdd ? SelectedAll.Concat(inside).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : inside;
            SelectMany(ids);
            return true;
        }

        /// <summary>A rectangle in fractions of the view (0–1), drawn and selected as if dragged (the snapshot's --marquee).</summary>
        public void MarqueeFraction(double x0, double y0, double x1, double y1)
        {
            var a = new Point(x0 * ActualWidth, y0 * ActualHeight);
            var b = new Point(x1 * ActualWidth, y1 * ActualHeight);
            _marquee = a;
            _marqueeAdd = false;
            if (_marqueeRect == null) MarqueeDownForced(a);
            PlaceMarquee(b);
            _marqueeRect!.Visibility = Visibility.Visible;
            SelectMany(ObjectsInside(new Rect(a, b)));
            _marquee = null;
        }

        private void MarqueeDownForced(Point p)
        {
            _marqueeRect = new Rectangle
            {
                StrokeThickness = 1,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Stroke = new SolidColorBrush(Color.FromRgb(0x6E, 0x9B, 0xFF)),
                Fill = new SolidColorBrush(Color.FromArgb(0x24, 0x6E, 0x9B, 0xFF))
            };
            Children.Add(_marqueeRect);
        }

        /// <summary>The objects shown in the view whose middle projects inside a screen rectangle, nearest first.</summary>
        private List<string> ObjectsInside(Rect r)
        {
            var found = new List<(string Id, double Depth)>();
            if (World == null) return new List<string>();
            foreach (var it in _items.Values)
            {
                if (EditorHidden(it.Object) || it.Visual.Content == null) continue;
                var b = it.Visual.Content.Bounds;
                if (b.IsEmpty) continue;
                var mid = it.Visual.Transform.Transform(new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2));
                var (screen, depth, visible) = Project(mid);
                if (visible && r.Contains(screen)) found.Add((it.Object.Id, depth));
            }

            return found.OrderBy(f => f.Depth).Select(f => f.Id).ToList();
        }
    }
}
