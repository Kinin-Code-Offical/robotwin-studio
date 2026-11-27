using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

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

        /// <summary>[grid] The move step along Y and Z (metres); 0: the X step (<see cref="Grid"/>), Unity's linked step.</summary>
        public double GridY { get; set; }

        public double GridZ { get; set; }

        /// <summary>[grid] Where the grid's lines start in X and Y (metres): Unity's Grid Position.</summary>
        public Point GridOrigin { get; set; }

        /// <summary>[grid] The plane the grid shows on besides the floor: "xy" (the floor only), "xz" or "yz" (upright, through the grid's position).</summary>
        public string GridPlane { get; set; } = "xy";

        /// <summary>
        /// [grid] The grid's turn about the vertical through its position (°): its lines, its upright plane and the X / Y
        /// snapping follow it, so parts line up along a wall or a machine set at an angle.
        /// </summary>
        public double GridAngle { get; set; }

        private bool GridTurned => Math.Abs(Math.IEEERemainder(GridAngle, 360)) > 1e-9;

        /// <summary>[grid] A place in X and Y onto the grid: in the grid's own turned frame, each axis its own step.</summary>
        private (double X, double Y) SnapXY(double x, double y)
        {
            if (!GridTurned) return (SnapX(x), SnapY(y));
            if (!SnapOn || System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftAlt)) return (x, y);
            return GridFrame.Nearest(x, y, GridOrigin.X, GridOrigin.Y, GridAngle, StepX, StepY);
        }

        /// <summary>[grid] A move along one direction in whole steps from where it started (a turned grid's arrows).</summary>
        private double SnapAlong(double distance, double step) =>
            step > 0 && SnapOn && !System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftAlt) ? Math.Round(distance / step) * step : distance;

        private double StepX => Grid;
        private double StepY => GridY > 0 ? GridY : Grid;
        private double StepZ => GridZ > 0 ? GridZ : Grid;

        private double SnapTo(double v, double step, double origin) =>
            step > 0 && SnapOn && !System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftAlt) ? origin + Math.Round((v - origin) / step) * step : v;

        /// <summary>[grid] A coordinate onto its axis' grid (its own step, from the grid's position).</summary>
        private double SnapX(double v) => SnapTo(v, StepX, GridOrigin.X);

        private double SnapY(double v) => SnapTo(v, StepY, GridOrigin.Y);
        private double SnapZ(double v) => SnapTo(v, StepZ, 0);

        /// <summary>[grid] The nearest grid point in X and Y to a place (what "align to grid" puts a part on).</summary>
        public Point NearestGridPoint(double x, double y)
        {
            var (nx, ny) = GridFrame.Nearest(x, y, GridOrigin.X, GridOrigin.Y, GridAngle, StepX, StepY);   // [grid] in the grid's turned frame
            return new Point(nx, ny);
        }

        private static double Wrap(double v, double period) => period <= 0 ? 0 : ((v % period) + period) % period;

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
        private ImageBrush GridBrush(double w, double d)
        {
            // the floor's texture runs u = x + w/2, v = w − d/2 − y: the tiles start where the grid's position puts a major line
            double tx = DrawnStep(StepX) * 10, ty = DrawnStep(StepY) * 10;
            double cu = GridOrigin.X + w / 2, cv = w - d / 2 - GridOrigin.Y;
            var brush = new ImageBrush(GridTile(_gridShown ? _gridOpacity : 0))
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(Wrap(cu, tx), Wrap(cv, ty), tx, ty)
            };
            // [grid] turned about the grid's position; the texture's v runs against y, so the turn is the other way
            if (GridTurned) brush.Transform = new RotateTransform(-GridAngle, cu, cv);
            return brush;
        }

        /// <summary>
        /// [grid] Unity's grid plane: an upright grid through the grid's position, XZ (square to Y) or YZ (square to X), its
        /// lines on a clear ground so the scene shows through, as high as half the floor is wide.
        /// </summary>
        private GeometryModel3D? UprightGrid(double w, double d)
        {
            if (!_gridShown || GridPlane is not ("xz" or "yz")) return null;
            double h = Math.Max(0.2, w / 2);
            bool xz = GridPlane == "xz";
            double span = xz ? w : d, along = xz ? StepX : StepY, start = xz ? -w / 2 : -d / 2;
            double ta = DrawnStep(along) * 10, tz = DrawnStep(StepZ) * 10;
            Point3D P(double a, double z) => xz ? new Point3D(a, GridOrigin.Y, z) : new Point3D(GridOrigin.X, a, z);
            var quad = new System.Windows.Media.Media3D.MeshGeometry3D
            {
                Positions = new System.Windows.Media.Media3D.Point3DCollection { P(start, 0), P(start + span, 0), P(start + span, h), P(start, h) },
                TextureCoordinates = new PointCollection { new Point(0, h), new Point(span, h), new Point(span, 0), new Point(0, 0) },
                TriangleIndices = new System.Windows.Media.Int32Collection { 0, 1, 2, 0, 2, 3 }
            };
            double origin = xz ? GridOrigin.X : GridOrigin.Y;
            var brush = new ImageBrush(GridTile(_gridOpacity, clear: true))
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(Wrap(origin - start, ta), Wrap(h, tz), ta, tz)
            };
            var material = new System.Windows.Media.Media3D.DiffuseMaterial(brush);
            var model = new GeometryModel3D(quad, material) { BackMaterial = material };
            if (GridTurned)   // [grid] the upright plane turns with the grid about the vertical through its position
                model.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), GridAngle), new Point3D(GridOrigin.X, GridOrigin.Y, 0));
            return model;
        }

        private static BitmapSource GridTile(double opacity, bool clear = false)
        {
            int key = (int)Math.Round(opacity * 100) + (clear ? 1000 : 0);
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
                if (!clear) dc.DrawRectangle(new SolidColorBrush(floor), null, new Rect(0, 0, px, px));
                if (opacity > 0)
                {
                    // an upright grid's lines stand on nothing: lighter, see-through ink
                    var minor = new Pen(new SolidColorBrush(clear ? Color.FromArgb((byte)(0x50 * opacity), 0x9A, 0xA4, 0xB2) : Toward(Color.FromRgb(0x1F, 0x1F, 0x24))), 1);
                    var major = new Pen(new SolidColorBrush(clear ? Color.FromArgb((byte)(0x90 * opacity), 0x9A, 0xA4, 0xB2) : Toward(Color.FromRgb(0x2E, 0x2E, 0x35))), 2);
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
