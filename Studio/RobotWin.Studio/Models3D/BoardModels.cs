using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Assembly3D;

namespace RobotWin.Studio.Models3D
{
    /// <summary>A part model placed on a board, with its transforms for live feedback.</summary>
    public sealed class PlacedComponent
    {
        internal PlacedComponent(string refDes, ComponentInstance instance, PartDefinition? def, IReadOnlyDictionary<string, string>? parameters, Matrix3D toBoard)
        {
            RefDes = refDes;
            Instance = instance;
            Definition = def;
            Parameters = parameters;
            ToBoard = toBoard;
        }

        /// <summary>Reference designator; parts inside a module board are "M1/R3".</summary>
        public string RefDes { get; }

        public ComponentInstance Instance { get; }
        public PartDefinition? Definition { get; }
        public IReadOnlyDictionary<string, string>? Parameters { get; }

        /// <summary>Model frame → frame of the scene returned by <see cref="BoardModels.BuildScene"/>.</summary>
        public Matrix3D ToBoard { get; }

        /// <summary>Hot spot of the part (smoke, fire) in the board scene frame (mm).</summary>
        public Point3D Centre => ToBoard.Transform(Instance.HeatCentre);
    }

    /// <summary>A populated board: scene graph plus the parts for live feedback.</summary>
    public sealed class BoardScene
    {
        internal BoardScene(Model3DGroup root, List<PlacedComponent> parts, double width, double height)
        {
            Root = root;
            Parts = parts;
            Width = width;
            Height = height;
        }

        /// <summary>Board centred on the origin, millimetres, Z up, bottom face at z = 0, top at z = 1.6.</summary>
        public Model3DGroup Root { get; }

        public IReadOnlyList<PlacedComponent> Parts { get; }
        public double Width { get; }
        public double Height { get; }
    }

    /// <summary>
    /// Complete populated printed circuit boards: FR4, solder mask with copper and
    /// silkscreen from the layout, every part model at its footprint (top or bottom side),
    /// solder fillets under through-hole leads, and module boards stacked on their pins.
    /// Frame: millimetres, board centred on the origin, Z up, bottom face at z = 0.
    /// </summary>
    public static class BoardModels
    {
        public const double Thickness = ComponentModels.BoardThickness;

        /// <summary>The populated board of a design (its layout, or an auto-placed one when it has none).</summary>
        public static Model3DGroup Build(Schematic design, PartLibrary? library = null)
        {
            library ??= PartLibrary.Default;
            return BuildScene(BoardOf(design, library), design, library).Root;
        }

        public static Model3DGroup Build(BoardDocument board, Schematic design, PartLibrary library) => BuildScene(board, design, library).Root;

        /// <summary>The design's board, or an auto-placed copy when it has no layout yet.</summary>
        public static BoardDocument BoardOf(Schematic design, PartLibrary library)
        {
            if (design.Board != null && design.Board.Components.Count > 0) return design.Board;
            var board = BoardDocument.Synchronize(design, library);
            AutoPlacer.Place(board);
            return board;
        }

        /// <summary>
        /// Builds the board scene. <paramref name="skip"/> leaves out part models (their pads stay),
        /// e.g. parts the robot assembly draws itself.
        /// </summary>
        public static BoardScene BuildScene(BoardDocument board, Schematic design, PartLibrary library, Func<string, bool>? skip = null) =>
            BuildScene(board, design, library, skip, string.Empty, 0, Matrix3D.Identity);

        private static BoardScene BuildScene(BoardDocument board, Schematic design, PartLibrary library, Func<string, bool>? skip, string prefix, int depth, Matrix3D toRoot)
        {
            var root = new Model3DGroup();
            var parts = new List<PlacedComponent>();
            double w = board.Width, h = board.Height, t = Thickness;
            root.Children.Add(Substrate(board));

            var solder = new MeshBuilder(new Frame());
            foreach (var comp in board.Components)
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                if (fp == null) continue;
                var part = design.FindPart(comp.RefDes);
                var def = part != null ? library.Find(part.PartId) : null;
                IReadOnlyDictionary<string, string>? p = part != null && def != null ? design.ResolveParameters(library, part) : null;
                var placement = Placement(comp, w, h, t);
                bool bottom = comp.Side == CopperLayer.Bottom;

                // Solder joints on the far side of every through-hole pad.
                foreach (var pad in fp.Pads.Where(x => x.IsThroughHole))
                {
                    var (bx, by) = comp.ToBoard(pad.X, pad.Y);
                    double x = bx - w / 2, y = h / 2 - by, r = Math.Max(0.5, Math.Min(pad.Width, pad.Height) / 2 * 0.85);
                    if (bottom) using (solder.Frame.At(Mx.T(x, y, t))) solder.Frustum(r, 0.36, 0, 0.75, 14, false, true);
                    else using (solder.Frame.At(Mx.Rx(180) * Mx.T(x, y, 0))) solder.Frustum(r, 0.36, 0, 0.75, 14, false, true);
                }

                if (skip != null && skip(comp.RefDes)) continue;

                var module = depth < 3 ? ModuleBuilder.SpecForFootprint(fp.Id) : null;
                var inner = module?.Inner();
                var group = new Model3DGroup { Transform = new MatrixTransform3D(placement) };
                if (inner?.Board != null && inner.Board.Components.Count > 0)
                {
                    // Module: its own board standing 11 mm up on pin headers.
                    var pins = new PartBuilder();
                    foreach (var pad in fp.Pads)
                    {
                        pins[Mat.Plastic(0x1C1C1F, 0.08)].Tapered(pad.X, pad.Y, 0, 2.5, 2.5, 2.5, 0.1);
                        pins[Mat.Gold].Box(pad.X, pad.Y, (ComponentModels.LeadTail + 11.6) / 2, 0.64, 0.64, 11.6 - ComponentModels.LeadTail);
                    }

                    var pinModel = pins.Build().Instantiate();
                    group.Children.Add(pinModel.Root);
                    var lift = Mx.T(0, 0, 11);
                    var daughter = BuildScene(inner.Board, inner, library, null, prefix + comp.RefDes + "/", depth + 1, lift * placement * toRoot);
                    daughter.Root.Transform = new MatrixTransform3D(lift);
                    group.Children.Add(daughter.Root);
                    parts.AddRange(daughter.Parts);
                    parts.Add(new PlacedComponent(prefix + comp.RefDes, pinModel, def, p, placement * toRoot));
                }
                else
                {
                    var inst = ComponentModels.CreateOnBoard(fp, def, p, comp.Value, comp.RefDes);
                    if (p != null && p.TryGetValue("position", out var pos) && SiValue.TryParse(pos, out var k))
                    {
                        var knob = inst.Find("knob");
                        if (knob != null) knob.Angle = (0.5 - Math.Max(0, Math.Min(1, k))) * 270;
                    }

                    group.Children.Add(inst.Root);
                    parts.Add(new PlacedComponent(prefix + comp.RefDes, inst, def, p, placement * toRoot));
                }

                root.Children.Add(group);
            }

            if (!solder.IsEmpty) root.Children.Add(new GeometryModel3D(solder.ToGeometry(), Mat.Solder));
            return new BoardScene(root, parts, w, h);
        }

        /// <summary>Model frame of a part (see <see cref="ComponentModels"/>) → board frame (centred, z = 0 at the bottom face).</summary>
        public static Matrix3D Placement(BoardComponent comp, double w, double h, double t = Thickness)
        {
            var o = comp.ToBoard(0, 0);
            var ex = comp.ToBoard(1, 0);
            var ey = comp.ToBoard(0, 1);
            // Footprint (x, y-down) → board: x' = x − w/2, y' = h/2 − y, z on the mounting side.
            var ax = new Vector3D(ex.X - o.X, -(ex.Y - o.Y), 0);
            var ay = new Vector3D(ey.X - o.X, -(ey.Y - o.Y), 0);
            bool bottom = comp.Side == CopperLayer.Bottom;
            var fpToBoard = new Matrix3D(ax.X, ax.Y, ax.Z, 0, ay.X, ay.Y, ay.Z, 0, 0, 0, bottom ? -1 : 1, 0, o.X - w / 2, h / 2 - o.Y, bottom ? 0 : t, 1);
            // Model frame is (x, −y_fp, z).
            return Mx.S(1, -1, 1) * fpToBoard;
        }

        /// <summary>
        /// Cable attachment points of every connector-type part (pin headers, terminal blocks)
        /// and board-type part (Arduino, ESP32, flight controller) of the design's board, in the
        /// frame of <see cref="Build(Schematic, PartLibrary?)"/>.
        /// </summary>
        public static IReadOnlyList<(string RefDes, string Pin, Point3D Position, Vector3D Direction)> ConnectorAnchors(Schematic design, PartLibrary? library = null)
        {
            library ??= PartLibrary.Default;
            return ConnectorAnchors(BoardOf(design, library), design, library);
        }

        public static IReadOnlyList<(string RefDes, string Pin, Point3D Position, Vector3D Direction)> ConnectorAnchors(BoardDocument board, Schematic design, PartLibrary library)
        {
            var result = new List<(string, string, Point3D, Vector3D)>();
            foreach (var comp in board.Components)
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                var part = design.FindPart(comp.RefDes);
                var def = part != null ? library.Find(part.PartId) : null;
                if (fp == null || def == null || part == null) continue;
                if (def.Symbol != SymbolKind.Connector && def.Symbol != SymbolKind.Board) continue;
                var p = design.ResolveParameters(library, part);
                var inst = ComponentModels.CreateOnBoard(fp, def, p, comp.Value, comp.RefDes);
                var m = Placement(comp, board.Width, board.Height);
                foreach (var (pin, pos, dir) in ComponentModels.MapPins(def, p, inst.PadAnchors))
                {
                    var d = m.Transform(dir);
                    if (d.LengthSquared > 1e-18) d.Normalize();
                    result.Add((comp.RefDes, pin, m.Transform(pos), d));
                }
            }

            return result;
        }

        // ------------------------------------------------------------------ substrate

        private static Model3DGroup Substrate(BoardDocument board)
        {
            double w = board.Width, h = board.Height, t = Thickness;
            var g = new Model3DGroup();
            // The board's real outline (straight edges, arcs, per-corner radii, cut-outs drawn in the
            // PCB editor) — not a rectangle.
            var outline = OutlinePoints(board, 1.0);
            var core = new MeshBuilder(new Frame());
            core.Extrude(outline, 0, t, false, false, false);
            g.Children.Add(new GeometryModel3D(core.ToGeometry(), Mat.Plastic(0xB9AE7E, 0.15)));
            var top = new ImageBrush(PcbTexture.Render(board)) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
            top.Freeze();
            var topMat = new MaterialGroup();
            topMat.Children.Add(new DiffuseMaterial(top));
            topMat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 25));
            g.Children.Add(new GeometryModel3D(OutlineCap(outline, w, h, t + 0.002, true, 1.0), topMat));
            var bottom = new ImageBrush(BottomTexture(board)) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
            bottom.Freeze();
            g.Children.Add(new GeometryModel3D(OutlineCap(outline, w, h, -0.002, false, 1.0), new DiffuseMaterial(bottom)));
            return g;
        }

        /// <summary>
        /// Board outline centred on the board (x right, y up, units × <paramref name="scale"/>), counter-clockwise
        /// seen from above. Layout coordinates have y down, so y flips.
        /// </summary>
        public static List<Point> OutlinePoints(BoardDocument board, double scale)
        {
            var pts = board.OutlinePolygon(0.02).Select(p => new Point((p.X - board.Width / 2) * scale, (board.Height / 2 - p.Y) * scale)).ToList();
            if (pts.Count < 3)
                pts = new List<Point> { new Point(-board.Width / 2 * scale, -board.Height / 2 * scale), new Point(board.Width / 2 * scale, -board.Height / 2 * scale), new Point(board.Width / 2 * scale, board.Height / 2 * scale), new Point(-board.Width / 2 * scale, board.Height / 2 * scale) };
            double area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                area += a.X * b.Y - b.X * a.Y;
            }

            if (area < 0) pts.Reverse();
            return pts;
        }

        /// <summary>The board face inside its outline, textured like <see cref="Quad"/> (u along x, v down the layout).</summary>
        public static MeshGeometry3D OutlineCap(List<Point> outline, double w, double h, double z, bool up, double scale)
        {
            var m = new MeshGeometry3D();
            var n = new Vector3D(0, 0, up ? 1 : -1);
            foreach (var p in outline)
            {
                m.Positions.Add(new Point3D(p.X, p.Y, z));
                m.Normals.Add(n);
                m.TextureCoordinates.Add(new Point(p.X / scale / w + 0.5, 0.5 - p.Y / scale / h));
            }

            foreach (var (a, b, c) in MeshBuilder.Triangulate(outline))
            {
                m.TriangleIndices.Add(a);
                m.TriangleIndices.Add(up ? b : c);
                m.TriangleIndices.Add(up ? c : b);
            }

            return m;
        }

        /// <summary>Board-sized quad; texture u along x, v down the board (layout Y).</summary>
        private static MeshGeometry3D Quad(double w, double h, double z, bool up)
        {
            var m = new MeshGeometry3D();
            double x0 = -w / 2, x1 = w / 2, y0 = -h / 2, y1 = h / 2;
            m.Positions.Add(new Point3D(x0, y1, z));
            m.Positions.Add(new Point3D(x1, y1, z));
            m.Positions.Add(new Point3D(x1, y0, z));
            m.Positions.Add(new Point3D(x0, y0, z));
            var n = new Vector3D(0, 0, up ? 1 : -1);
            for (int i = 0; i < 4; i++) m.Normals.Add(n);
            m.TextureCoordinates.Add(new Point(0, 0));
            m.TextureCoordinates.Add(new Point(1, 0));
            m.TextureCoordinates.Add(new Point(1, 1));
            m.TextureCoordinates.Add(new Point(0, 1));
            if (up)
            {
                m.TriangleIndices = new Int32Collection(new[] { 0, 3, 2, 0, 2, 1 });
            }
            else
            {
                m.TriangleIndices = new Int32Collection(new[] { 0, 2, 3, 0, 1, 2 });
            }

            m.Freeze();
            return m;
        }

        /// <summary>Bottom side: mask, bottom copper, through-hole pads and vias (in board coordinates).</summary>
        private static BitmapSource BottomTexture(BoardDocument board)
        {
            const double ppmm = 8;
            int w = Math.Max(8, (int)(board.Width * ppmm)), h = Math.Max(8, (int)(board.Height * ppmm));
            var dv = new DrawingVisual();
            using (var g = dv.RenderOpen())
            {
                g.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x4E, 0x2C)), null, new Rect(0, 0, w, h));
                var copper = new SolidColorBrush(Color.FromRgb(0x2E, 0x77, 0x47));
                foreach (var tr in board.Tracks.Where(x => x.Layer == CopperLayer.Bottom))
                {
                    var pen = new Pen(copper, tr.Width * ppmm) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    g.DrawLine(pen, new Point(tr.X1 * ppmm, tr.Y1 * ppmm), new Point(tr.X2 * ppmm, tr.Y2 * ppmm));
                }

                var pad = new SolidColorBrush(Color.FromRgb(0xD8, 0xB2, 0x6A));
                foreach (var p in board.Pads())
                {
                    if (!p.OnLayer(CopperLayer.Bottom)) continue;
                    var r = new Rect((p.X - p.Width / 2) * ppmm, (p.Y - p.Height / 2) * ppmm, p.Width * ppmm, p.Height * ppmm);
                    if (p.Pad.Shape == PadShape.Circle) g.DrawEllipse(pad, null, new Point(p.X * ppmm, p.Y * ppmm), r.Width / 2, r.Height / 2);
                    else g.DrawRectangle(pad, null, r);
                }

                foreach (var v in board.Vias)
                {
                    g.DrawEllipse(pad, null, new Point(v.X * ppmm, v.Y * ppmm), v.Diameter * ppmm / 2, v.Diameter * ppmm / 2);
                    g.DrawEllipse(Brushes.Black, null, new Point(v.X * ppmm, v.Y * ppmm), v.Drill * ppmm / 2, v.Drill * ppmm / 2);
                }
            }

            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }
}
