using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [grid] Unity's Grid and Snap for the world editor: whether moves, turns and sizes snap, the size step, and the
    /// floor's grid drawn at the move step (a major line every ten), shown or not and how strongly.
    /// </summary>
    public sealed partial class WorldScene
    {
        /// <summary>Whether drags snap (Unity's Grid Snapping); Alt frees one drag either way.</summary>
        public bool SnapOn { get; set; } = true;

        /// <summary>The scale handle's size step in metres (0 = the move step, <see cref="Grid"/>).</summary>
        public double SizeStep { get; set; }

        private bool _gridShown = true;
        private double _gridOpacity = 1;

        /// <summary>Whether the floor shows the grid's lines.</summary>
        public bool GridShown
        {
            get => _gridShown;
            set
            {
                if (_gridShown == value) return;
                _gridShown = value;
                BuildEnvironment();
            }
        }

        /// <summary>How strongly the grid's lines show, 0–1.</summary>
        public double GridOpacity
        {
            get => _gridOpacity;
            set
            {
                value = Math.Max(0, Math.Min(1, value));
                if (Math.Abs(_gridOpacity - value) < 1e-6) return;
                _gridOpacity = value;
                BuildEnvironment();
            }
        }

        /// <summary>Draws the floor again after the step changed (its lines follow it).</summary>
        public void RefreshGrid() => BuildEnvironment();

        /// <summary>The minor line spacing drawn for a snap step: the step itself, doubled while lines would crowd (under 2 mm).</summary>
        internal static double DrawnStep(double grid)
        {
            double s = grid > 0 ? grid : 0.01;
            while (s < 0.002) s *= 2;
            return Math.Min(s, 0.1);
        }

        private static readonly Dictionary<int, BitmapSource> GridTiles = new Dictionary<int, BitmapSource>();

        /// <summary>The floor: one tile is ten minor steps with a major line on its edge, tiled at its true size.</summary>
        private ImageBrush GridBrush()
        {
            double minor = DrawnStep(Grid);
            var brush = new ImageBrush(GridTile(_gridShown ? _gridOpacity : 0))
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, minor * 10, minor * 10)
            };
            return brush;
        }

        private static BitmapSource GridTile(double opacity)
        {
            int key = (int)Math.Round(opacity * 100);
            lock (GridTiles)
                if (GridTiles.TryGetValue(key, out var cached))
                    return cached;

            const int px = 200;
            var floor = Color.FromRgb(0x15, 0x15, 0x18);
            Color Toward(Color c) => Color.FromRgb(
                (byte)(floor.R + (c.R - floor.R) * opacity), (byte)(floor.G + (c.G - floor.G) * opacity), (byte)(floor.B + (c.B - floor.B) * opacity));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(floor), null, new Rect(0, 0, px, px));
                if (opacity > 0)
                {
                    var minor = new Pen(new SolidColorBrush(Toward(Color.FromRgb(0x1F, 0x1F, 0x24))), 1);
                    var major = new Pen(new SolidColorBrush(Toward(Color.FromRgb(0x2E, 0x2E, 0x35))), 2);
                    for (int i = 1; i < 10; i++)
                    {
                        double x = i * px / 10.0 + 0.5;
                        dc.DrawLine(minor, new Point(x, 0), new Point(x, px));
                        dc.DrawLine(minor, new Point(0, x), new Point(px, x));
                    }

                    // The major line straddles the tile's edge: half on each side, so tiles meet in one line.
                    dc.DrawLine(major, new Point(0.5, 0), new Point(0.5, px));
                    dc.DrawLine(major, new Point(0, 0.5), new Point(px, 0.5));
                    dc.DrawLine(major, new Point(px - 0.5, 0), new Point(px - 0.5, px));
                    dc.DrawLine(major, new Point(0, px - 0.5), new Point(px, px - 0.5));
                }
            }

            var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            lock (GridTiles) GridTiles[key] = bmp;
            return bmp;
        }
    }
}
