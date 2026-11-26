using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Modeling
{
    /// <summary>
    /// Blender-style 3D viewport of a <see cref="ModelEditor"/>: turntable camera, grid floor,
    /// object/edit rendering, picking, box selection, transform gizmo and modal G/R/S, E, I operators.
    /// </summary>
    public sealed partial class ModelViewport : Grid
    {
        // ------------------------------------------------------------------ colours
        internal static readonly Color BackColor = Color.FromRgb(0x15, 0x15, 0x18);
        internal static readonly Color GridMinor = Color.FromRgb(0x1F, 0x1F, 0x24);
        internal static readonly Color GridMajor = Color.FromRgb(0x2B, 0x2B, 0x31);
        internal static readonly Color AxisXColor = Color.FromRgb(0xE5, 0x4B, 0x55);
        internal static readonly Color AxisYColor = Color.FromRgb(0x7C, 0xC0, 0x48);
        internal static readonly Color AxisZColor = Color.FromRgb(0x4C, 0x8D, 0xF6);
        internal static readonly Color GridAxisX = Color.FromRgb(0x7A, 0x2E, 0x36);
        internal static readonly Color GridAxisY = Color.FromRgb(0x46, 0x6A, 0x2C);
        internal static readonly Color ActiveColor = Color.FromRgb(0xC4, 0xD6, 0xFF);
        internal static readonly Color SelectColor = Color.FromRgb(0x6E, 0x9B, 0xFF);
        internal static readonly Color WireColor = Color.FromRgb(0x0C, 0x0C, 0x0E);
        internal static readonly Color WireLight = Color.FromRgb(0x8A, 0x8E, 0x98);
        internal static readonly Color EditMeshColor = Color.FromRgb(0x9A, 0x9E, 0xA8);
        internal static readonly Color EditFaceColor = Color.FromRgb(0x8F, 0xA9, 0xE6);

        private sealed class Entry
        {
            public ModelObject Obj = null!;
            public PolyMesh Mesh = null!;
            public MeshModel Tri = null!;
            public int[] TriFace = Array.Empty<int>();
            public MeshBvh? Bvh;
            public Affine World;
            public Affine Inverse;
            public ModelVisual3D Visual = null!;
            public bool IsEdit;

            /// <summary>[section] The faces the section view's plane made (drawn in the cut colour); null when not cut.</summary>
            public HashSet<int>? Caps;

            // world-space caches for outlines
            public Vec3[]? WV;
            public Vec3[]? WN;
            public Vec3[]? WC;
            public List<(int A, int B)>? Edges;
            public Dictionary<long, List<int>>? EdgeFaces;

            public MeshBvh Bvh_() => Bvh ??= new MeshBvh(Tri);

            public void EnsureWorld()
            {
                if (WV != null) return;
                WV = Mesh.Vertices.Select(World.Apply).ToArray();
                WN = new Vec3[Mesh.Faces.Count];
                WC = new Vec3[Mesh.Faces.Count];
                for (int f = 0; f < Mesh.Faces.Count; f++)
                {
                    WN[f] = PolyMesh.NewellNormal(Mesh.Faces[f], WV).Normalized();
                    var c = Vec3.Zero;
                    foreach (int v in Mesh.Faces[f]) c += WV[v];
                    WC[f] = c / Math.Max(1, Mesh.Faces[f].Length);
                }

                Edges ??= Mesh.Edges();
                EdgeFaces ??= Mesh.EdgeFaces();
            }

            public void InvalidateWorld()
            {
                WV = null;
                WN = null;
                WC = null;
            }
        }

        private readonly ModelEditor _ed;
        private readonly Viewport3D _view = new Viewport3D { ClipToBounds = true, IsHitTestVisible = false };
        private readonly PerspectiveCamera _persp = new PerspectiveCamera();
        private readonly OrthographicCamera _ortho = new OrthographicCamera();
        private readonly ModelVisual3D _lights = new ModelVisual3D();
        private readonly DirectionalLight _key = new DirectionalLight(Color.FromRgb(0xC8, 0xC8, 0xCC), new Vector3D(0, 0, -1));
        private readonly DirectionalLight _fill = new DirectionalLight(Color.FromRgb(0x4A, 0x4C, 0x55), new Vector3D(0, 0, 1));
        private readonly DirectionalLight _rim = new DirectionalLight(Color.FromRgb(0x30, 0x32, 0x3A), new Vector3D(0, 0, 1));
        private readonly ModelVisual3D _grid = new ModelVisual3D();
        private readonly ModelVisual3D _objects = new ModelVisual3D();
        private readonly ModelVisual3D _overlay3d = new ModelVisual3D();
        private readonly ModelOverlay _overlay;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        public ModelViewport(ModelEditor editor)
        {
            _ed = editor;
            Cam = new ViewCamera();
            Background = new SolidColorBrush(BackColor);
            ClipToBounds = true;
            Focusable = true;
            FocusVisualStyle = null;
            _view.Camera = _persp;
            var lightGroup = new Model3DGroup();
            lightGroup.Children.Add(new AmbientLight(Color.FromRgb(0x3C, 0x3D, 0x44)));
            lightGroup.Children.Add(_key);
            lightGroup.Children.Add(_fill);
            lightGroup.Children.Add(_rim);
            _lights.Content = lightGroup;
            _view.Children.Add(_lights);
            _view.Children.Add(_grid);
            _view.Children.Add(_objects);
            _view.Children.Add(_overlay3d);
            Children.Add(_view);
            // [render] our GPU renderer draws the view (an orthographic eye stands 20 orbit distances back).
            Render.GpuHost.Attach(this, _view, focus: () => Cam.Ortho ? Cam.Distance * 20 : Cam.Distance);
            _overlay = new ModelOverlay(this);
            Children.Add(_overlay);
            RenderOptions.SetEdgeMode(_view, EdgeMode.Unspecified);

            SizeChanged += (_, _) =>
            {
                Cam.Width = Math.Max(1, ActualWidth);
                Cam.Height = Math.Max(1, ActualHeight);
                UpdateCamera();
            };
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseWheel += OnWheel;
            MouseEnter += (_, _) =>
            {
                if (!(Keyboard.FocusedElement is TextBox)) Focus();
            };
            _ed.Changed += OnEditorChanged;
            Rebuild();
            UpdateCamera();
        }

        public ViewCamera Cam { get; }
        internal ModelEditor Editor => _ed;

        /// <summary>Right click without dragging (the page opens a context menu).</summary>
        public event Action<Point>? ContextMenuRequested;

        /// <summary>Raised after the camera moved (for the page's view label).</summary>
        public event Action? ViewChanged;

        public Point MousePosition => Mouse.GetPosition(this);

        private void OnEditorChanged(ModelChange change)
        {
            if ((change & (ModelChange.Geometry | ModelChange.Mode)) != 0) Rebuild();
            else if (_ed.Mode == EditorMode.Edit && (change & ModelChange.Selection) != 0) RefreshEditMesh();
            else RefreshOverlays();
        }

        // ================================================================ camera

        public string ViewName { get; private set; } = "Kullanıcı Perspektif";

        public void UpdateCamera()
        {
            var f = Cam.Forward;
            var up = Cam.Up;
            var pos = Cam.Position;
            if (Cam.Ortho)
            {
                var far = pos - f * Cam.Distance * 20;
                _ortho.Position = new Point3D(far.X, far.Y, far.Z);
                _ortho.LookDirection = new Vector3D(f.X, f.Y, f.Z);
                _ortho.UpDirection = new Vector3D(up.X, up.Y, up.Z);
                _ortho.Width = Cam.OrthoWidth;
                _ortho.NearPlaneDistance = Cam.Distance * 0.5;
                _ortho.FarPlaneDistance = Cam.Distance * 60;
                if (_view.Camera != _ortho) _view.Camera = _ortho;
            }
            else
            {
                _persp.Position = new Point3D(pos.X, pos.Y, pos.Z);
                _persp.LookDirection = new Vector3D(f.X, f.Y, f.Z);
                _persp.UpDirection = new Vector3D(up.X, up.Y, up.Z);
                _persp.FieldOfView = Cam.FieldOfView;
                _persp.NearPlaneDistance = Math.Max(0.01, Cam.Distance * 0.01);
                _persp.FarPlaneDistance = Cam.Distance * 60 + 2000;
                if (_view.Camera != _persp) _view.Camera = _persp;
            }

            // key light from the upper left of the eye, fill from the lower right, rim from behind
            var k = (f + Cam.Right * 0.55 - up * 0.75).Normalized();
            _key.Direction = new Vector3D(k.X, k.Y, k.Z);
            var fl = (f - Cam.Right * 0.6 + up * 0.5).Normalized();
            _fill.Direction = new Vector3D(fl.X, fl.Y, fl.Z);
            var r = (-f * 0.6 - up * 0.8).Normalized();
            _rim.Direction = new Vector3D(r.X, r.Y, r.Z);
            RefreshOverlays();
            ViewChanged?.Invoke();
        }

        public void SetView(double yaw, double pitch, string name, bool ortho = true)
        {
            Cam.Yaw = yaw;
            Cam.Pitch = pitch;
            Cam.Ortho = ortho;
            ViewName = name + (ortho ? " Ortografik" : " Perspektif");
            UpdateCamera();
        }

        public void ViewFront(bool opposite = false) => SetView(opposite ? -Math.PI / 2 : Math.PI / 2, 0, opposite ? "Arkadan" : "Önden");
        public void ViewRight(bool opposite = false) => SetView(opposite ? 0 : Math.PI, 0, opposite ? "Soldan" : "Sağdan");
        public void ViewTop(bool opposite = false) => SetView(Math.PI / 2, opposite ? -Math.PI / 2 + 1e-6 : Math.PI / 2 - 1e-6, opposite ? "Alttan" : "Üstten");

        public void ToggleOrtho()
        {
            Cam.Ortho = !Cam.Ortho;
            ViewName = Cam.Ortho ? ViewName.Replace("Perspektif", "Ortografik") : ViewName.Replace("Ortografik", "Perspektif");
            UpdateCamera();
        }

        /// <summary>Looks along the axis bubble that was clicked (from that side towards the centre).</summary>
        internal void ViewAlong(Vec3 axisFromCenter)
        {
            if (axisFromCenter.Z > 0.5) ViewTop();
            else if (axisFromCenter.Z < -0.5) ViewTop(true);
            else if (axisFromCenter.X > 0.5) ViewRight();
            else if (axisFromCenter.X < -0.5) ViewRight(true);
            else if (axisFromCenter.Y < -0.5) ViewFront();
            else ViewFront(true);
        }

        public void FrameAll() => FrameBounds(VisibleBounds());

        public void FrameSelected()
        {
            var b = Aabb.Empty;
            if (_ed.Mode == EditorMode.Edit && _ed.EditMesh != null && _ed.Active != null)
            {
                var w = _ed.Doc.WorldMatrix(_ed.Active);
                var verts = _ed.SelectedVertexSet();
                if (verts.Count == 0) verts = new HashSet<int>(Enumerable.Range(0, _ed.EditMesh.Vertices.Count));
                foreach (int v in verts) b = b.Include(w.Apply(_ed.EditMesh.Vertices[v]));
            }
            else
            {
                foreach (var o in _ed.SelectedObjects)
                {
                    if (_entries.TryGetValue(o.Id, out var e)) b = b.Union(Aabb.FromPoints(e.Mesh.Vertices.Select(e.World.Apply)));
                }
            }

            FrameBounds(b.IsEmpty ? VisibleBounds() : b);
        }

        private Aabb VisibleBounds()
        {
            var b = Aabb.Empty;
            foreach (var e in _entries.Values)
            {
                if (e.Mesh.Vertices.Count > 0) b = b.Union(Aabb.FromPoints(e.Mesh.Vertices.Select(e.World.Apply)));
            }

            return b;
        }

        private void FrameBounds(Aabb b)
        {
            if (b.IsEmpty)
            {
                Cam.Target = new Vec3(0, 0, 10);
                Cam.Distance = 220;
                UpdateCamera();
                return;
            }

            Cam.Target = b.Center;
            double radius = Math.Max(2, b.Size.Length / 2);
            double fov = Cam.FieldOfView * Math.PI / 180 * Math.Min(1, Cam.Height / Math.Max(1, Cam.Width));
            Cam.Distance = radius / Math.Sin(Math.Max(0.2, fov / 2)) * 1.05;
            UpdateCamera();
        }

        // ================================================================ scene

        /// <summary>[section] The section view's plane (every object cut along it, the front taken away); null: whole objects.</summary>
        public SectionPlane? Section { get; set; }

        /// <summary>[section] The colour of the cut faces: flat, apart from every body colour.</summary>
        internal static readonly Color CapColor = Color.FromRgb(0xE2, 0x6D, 0x5A);

        /// <summary>Re-creates the object visuals from the document.</summary>
        public void Rebuild()
        {
            _entries.Clear();
            var group = new List<Visual3D>();
            _objects.Children.Clear();
            foreach (var o in _ed.Doc.Objects)
            {
                bool isEdit = _ed.Mode == EditorMode.Edit && o.Id == _ed.ActiveId && _ed.EditMesh != null;
                if (!o.Visible && !isEdit) continue;
                var e = new Entry { Obj = o, IsEdit = isEdit, Visual = new ModelVisual3D() };
                e.Mesh = isEdit ? _ed.EditMesh! : o.LocalMesh();
                if (!isEdit && Section != null)
                {
                    // [section] the object as the plane leaves it: wholly in front, it is not shown (nor picked)
                    var (cut, caps) = SectionView.Cut(e.Mesh, _ed.Doc.WorldMatrix(o), Section);
                    if (cut.IsEmpty) continue;
                    e.Mesh = cut;
                    e.Caps = caps.Count > 0 ? caps : null;
                }

                BuildEntry(e);
                _entries[o.Id] = e;
                _objects.Children.Add(e.Visual);
            }

            RefreshOverlays();
        }

        private void BuildEntry(Entry e)
        {
            e.Tri = e.Mesh.Triangulate(out e.TriFace);
            e.Bvh = null;
            e.Edges = null;
            e.EdgeFaces = null;
            e.World = _ed.Doc.WorldMatrix(e.Obj);
            e.Inverse = e.World.Inverse();
            e.InvalidateWorld();
            e.Visual.Content = BuildModel(e);
            e.Visual.Transform = new MatrixTransform3D(ModelGeometry.ToMatrix(e.World));
        }

        private Model3D? BuildModel(Entry e)
        {
            if (_ed.Shading == ShadingMode.Wireframe && !e.IsEdit) return null;
            bool xray = _ed.Shading != ShadingMode.Solid;
            var o = e.Obj;
            if (e.IsEdit)
            {
                var g = new Model3DGroup();
                var faceSel = new HashSet<int>(_ed.SelectedFaceList());
                var plain = ModelGeometry.BuildSolid(e.Mesh, e.Tri, e.TriFace, o.SmoothShading, o.AutoSmoothAngle, f => !faceSel.Contains(f));
                var m1 = ModelGeometry.Solid(EditMeshColor, xray);
                g.Children.Add(new GeometryModel3D(plain, m1) { BackMaterial = xray ? m1 : ModelGeometry.Solid(Color.FromRgb(0x50, 0x3A, 0x3A), false) });
                if (faceSel.Count > 0)
                {
                    var sel = ModelGeometry.BuildSolid(e.Mesh, e.Tri, e.TriFace, o.SmoothShading, o.AutoSmoothAngle, faceSel.Contains);
                    var m2 = ModelGeometry.Solid(EditFaceColor, xray);
                    g.Children.Add(new GeometryModel3D(sel, m2) { BackMaterial = m2 });
                }

                return g;
            }

            var color = ModelGeometry.ParseColor(o.Color, Color.FromRgb(0xB9, 0xBF, 0xC9));
            var caps = e.Caps;
            var geo = ModelGeometry.BuildSolid(e.Mesh, e.Tri, e.TriFace, o.SmoothShading, o.AutoSmoothAngle, caps == null ? null : f => !caps.Contains(f));
            var mat = ModelGeometry.Solid(color, xray);
            var back = xray ? mat : ModelGeometry.Solid(Color.FromRgb(0x60, 0x30, 0x30), false);
            var body = new GeometryModel3D(geo, mat) { BackMaterial = back };
            if (caps == null) return body;
            // [section] the cut faces in their own flat colour, lit from both sides
            var capMat = ModelGeometry.Solid(CapColor, xray);
            var group = new Model3DGroup();
            group.Children.Add(body);
            group.Children.Add(new GeometryModel3D(ModelGeometry.BuildSolid(e.Mesh, e.Tri, e.TriFace, false, 0, caps.Contains), capMat) { BackMaterial = capMat });
            return group;
        }

        /// <summary>Updates object placements after transform edits (no geometry rebuild).</summary>
        public void RefreshTransforms()
        {
            foreach (var e in _entries.Values)
            {
                var w = _ed.Doc.WorldMatrix(e.Obj);
                e.World = w;
                e.Inverse = w.Inverse();
                e.InvalidateWorld();
                e.Visual.Transform = new MatrixTransform3D(ModelGeometry.ToMatrix(w));
            }

            RefreshOverlays();
        }

        /// <summary>Rebuilds the edited object's geometry (vertex moves, extrude preview …).</summary>
        public void RefreshEditMesh()
        {
            var a = _ed.Active;
            if (a == null || !_entries.TryGetValue(a.Id, out var e) || !e.IsEdit) return;
            e.Mesh = _ed.EditMesh!;
            BuildEntry(e);
            RefreshOverlays();
        }

        // ================================================================ camera-dependent overlays

        public void RefreshOverlays()
        {
            BuildGrid();
            var rb = new Ribbons(Cam);
            bool editing = _ed.Mode == EditorMode.Edit;
            bool showAllWire = _ed.Shading != ShadingMode.Solid || _ed.WireOverlay;
            var sel = new HashSet<string>(_ed.Selected);
            foreach (var e in _entries.Values)
            {
                if (e.IsEdit) continue;
                e.EnsureWorld();
                bool selected = sel.Contains(e.Obj.Id);
                bool active = e.Obj.Id == _ed.ActiveId;
                if (showAllWire && _ed.Shading == ShadingMode.Solid)
                {
                    // CAD-style edge overlay: sharp (feature) edges and open borders only
                    var wc = Color.FromRgb(0x14, 0x15, 0x19);
                    foreach (var kv in e.EdgeFaces!)
                    {
                        bool feature = kv.Value.Count != 2 || e.WN![kv.Value[0]].Dot(e.WN[kv.Value[1]]) < FeatureCos;
                        if (!feature) continue;
                        var (a, b) = PolyMesh.EdgeOf(kv.Key);
                        rb.Line(e.WV![a], e.WV[b], wc, 1.3);
                    }
                }
                else if (showAllWire)
                {
                    var wc = selected ? (active ? ActiveColor : SelectColor) : WireLight;
                    foreach (var (a, b) in e.Edges!) rb.Line(e.WV![a], e.WV[b], wc, selected ? 1.4 : 1.0);
                }

                if (selected && !editing && _ed.Shading == ShadingMode.Solid) Silhouette(rb, e, active ? ActiveColor : SelectColor);
            }

            if (editing) EditOverlay(rb);
            ModalOverlay(rb);
            MeasureOverlay(rb);
            _overlay3d.Content = rb.IsEmpty ? null : rb.Build();
            _overlay.InvalidateVisual();
        }

        private static readonly double FeatureCos = Math.Cos(25 * Math.PI / 180);

        private void Silhouette(Ribbons rb, Entry e, Color color)
        {
            var eye = Cam.Position;
            bool Front(int f) => Cam.Ortho ? e.WN![f].Dot(Cam.Forward) < 0 : e.WN![f].Dot(e.WC![f] - eye) < 0;
            foreach (var kv in e.EdgeFaces!)
            {
                bool draw;
                if (kv.Value.Count != 2) draw = true;
                else
                {
                    bool f0 = Front(kv.Value[0]), f1 = Front(kv.Value[1]);
                    draw = f0 != f1;
                }

                if (!draw) continue;
                var (a, b) = PolyMesh.EdgeOf(kv.Key);
                rb.Line(e.WV![a], e.WV[b], color, 3.0);
            }
        }

        private void EditOverlay(Ribbons rb)
        {
            var a = _ed.Active;
            if (a == null || !_entries.TryGetValue(a.Id, out var e)) return;
            e.EnsureWorld();
            var vs = _ed.SelectedVertexSet();
            bool face = _ed.Element == ElementMode.Face;
            var selFaces = face ? _ed.SelFaces : new HashSet<int>(_ed.SelectedFaceList());
            var faceEdges = new HashSet<long>();
            foreach (int f in selFaces)
            {
                var loop = e.Mesh.Faces[f];
                for (int k = 0; k < loop.Length; k++) faceEdges.Add(PolyMesh.EdgeKey(loop[k], loop[(k + 1) % loop.Length]));
            }

            foreach (var (va, vb) in e.Edges!)
            {
                bool s = face ? faceEdges.Contains(PolyMesh.EdgeKey(va, vb)) : vs.Contains(va) && vs.Contains(vb);
                rb.Line(e.WV![va], e.WV[vb], s ? SelectColor : WireColor, s ? 1.8 : 1.1);
            }

            if (_ed.Element == ElementMode.Vertex)
            {
                for (int v = 0; v < e.WV!.Length; v++) rb.Dot(e.WV[v], vs.Contains(v) ? ActiveColor : WireColor, vs.Contains(v) ? 6 : 4.5);
            }
            else if (face)
            {
                for (int f = 0; f < e.WC!.Length; f++)
                {
                    if (selFaces.Contains(f)) rb.Dot(e.WC[f], ActiveColor, 5);
                }
            }
        }

        private double _gridStep = 10;

        private void BuildGrid()
        {
            var rb = new Ribbons(Cam, 0);
            // adaptive spacing: about 30–60 major cells across the view
            double viewSize = Cam.Ortho ? Cam.OrthoWidth : Cam.Distance * 1.2;
            double major = Math.Pow(10, Math.Ceiling(Math.Log10(Math.Max(1e-3, viewSize / 8))));
            double minor = major / 10;
            _gridStep = minor;
            bool topish = Math.Abs(Cam.Forward.Z) > 0.25 || !Cam.Ortho;
            if (topish)
            {
                double extent = major * 12;
                double cx = Math.Round(Cam.Target.X / major) * major, cy = Math.Round(Cam.Target.Y / major) * major;
                int n = (int)Math.Round(extent / minor);
                for (int i = -n; i <= n; i++)
                {
                    double x = cx + i * minor;
                    bool isMajor = Math.Abs(Math.IEEERemainder(x, major)) < minor * 0.01;
                    if (Math.Abs(x) < minor * 0.01) continue;
                    if (!isMajor && Math.Abs(i) > n / 3) continue;
                    rb.Line(new Vec3(x, cy - (isMajor ? extent : extent / 3), 0), new Vec3(x, cy + (isMajor ? extent : extent / 3), 0), isMajor ? GridMajor : GridMinor, 1);
                }

                for (int i = -n; i <= n; i++)
                {
                    double y = cy + i * minor;
                    bool isMajor = Math.Abs(Math.IEEERemainder(y, major)) < minor * 0.01;
                    if (Math.Abs(y) < minor * 0.01) continue;
                    if (!isMajor && Math.Abs(i) > n / 3) continue;
                    rb.Line(new Vec3(cx - (isMajor ? extent : extent / 3), y, 0), new Vec3(cx + (isMajor ? extent : extent / 3), y, 0), isMajor ? GridMajor : GridMinor, 1);
                }

                if (Math.Abs(cy) < extent) rb.Line(new Vec3(cx - extent, 0, 0), new Vec3(cx + extent, 0, 0), GridAxisX, 1.6);
                if (Math.Abs(cx) < extent) rb.Line(new Vec3(0, cy - extent, 0), new Vec3(0, cy + extent, 0), GridAxisY, 1.6);
            }
            else
            {
                // side views: a vertical grid through the target, like Blender's ortho grid
                var right = Math.Abs(Cam.Forward.X) > 0.5 ? Vec3.UnitY : Vec3.UnitX;
                double extent = major * 12;
                double c0 = Math.Round(Cam.Target.Dot(right) / major) * major, z0 = Math.Round(Cam.Target.Z / major) * major;
                var depth = Cam.Target - right * Cam.Target.Dot(right);
                depth = new Vec3(depth.X, depth.Y, 0);
                int n = (int)Math.Round(extent / minor);
                for (int i = -n; i <= n; i++)
                {
                    double u = c0 + i * minor;
                    bool isMajor = Math.Abs(Math.IEEERemainder(u, major)) < minor * 0.01;
                    if (!isMajor && Math.Abs(i) > n / 3) continue;
                    var col = Math.Abs(u) < minor * 0.01 ? (right.X > 0.5 ? GridAxisY : GridAxisX) : isMajor ? GridMajor : GridMinor;
                    if (Math.Abs(u) < minor * 0.01) col = Color.FromRgb(0x2A, 0x4A, 0x7A);
                    rb.Line(depth + right * u + Vec3.UnitZ * (z0 - extent), depth + right * u + Vec3.UnitZ * (z0 + extent), col, 1);
                    double z = z0 + i * minor;
                    bool zMajor = Math.Abs(Math.IEEERemainder(z, major)) < minor * 0.01;
                    if (!zMajor && Math.Abs(i) > n / 3) continue;
                    var zc = Math.Abs(z) < minor * 0.01 ? (right.X > 0.5 ? GridAxisX : GridAxisY) : zMajor ? GridMajor : GridMinor;
                    rb.Line(depth + right * (c0 - extent) + Vec3.UnitZ * z, depth + right * (c0 + extent) + Vec3.UnitZ * z, zc, Math.Abs(z) < minor * 0.01 ? 1.6 : 1);
                }
            }

            _grid.Content = rb.Build();
        }

        public double GridStep => _gridStep;

        // ================================================================ picking

        private (Entry? Entry, double T, Vec3 Point, int Face) RayPick(Point screen, Func<Entry, bool>? filter = null)
        {
            var (o, d) = Cam.Ray(screen);
            Entry? best = null;
            double bestT = double.PositiveInfinity;
            Vec3 bestP = Vec3.Zero;
            int bestFace = -1;
            foreach (var e in _entries.Values)
            {
                if (filter != null && !filter(e)) continue;
                if (e.Tri.TriangleCount == 0) continue;
                var lo = e.Inverse.Apply(o);
                var ld = e.Inverse.ApplyVector(d);
                if (!e.Bvh_().Raycast(lo, ld, double.PositiveInfinity, out var hit)) continue;
                var wp = e.World.Apply(hit.Point);
                double t = (wp - o).Dot(d);
                if (t < bestT && t > 0)
                {
                    bestT = t;
                    best = e;
                    bestP = wp;
                    bestFace = hit.Triangle >= 0 && hit.Triangle < e.TriFace.Length ? e.TriFace[hit.Triangle] : -1;
                }
            }

            return (best, bestT, bestP, bestFace);
        }

        public string? PickObject(Point screen) => RayPick(screen).Entry?.Obj.Id;

        /// <summary>[fillet] The segments the next bevel starts with: 1 a chamfer, more a round (the Yuvarla button sets 8).</summary>
        public int NextBevelSegments { get; set; } = 1;

        /// <summary>[holes] While set, a click on a surface hands its object, world point and outward normal here (the hole wizard).</summary>
        public Action<string, Vec3, Vec3>? SurfacePick { get; set; }

        /// <summary>[holes] The object surface under the cursor: its object, the world point and the face's outward normal.</summary>
        public (string ObjectId, Vec3 Point, Vec3 Normal)? PickFace(Point screen)
        {
            var (e, _, point, face) = RayPick(screen, x => !x.IsEdit);
            if (e == null || face < 0 || face >= e.Mesh.Faces.Count) return null;
            var n = e.World.ApplyVector(e.Mesh.FaceNormal(face)).Normalized();
            return (e.Obj.Id, point, n);
        }

        /// <summary>Point on a surface under the cursor (or on the floor), snapped to nearby vertices.</summary>
        public Vec3? PickSurfacePoint(Point screen, bool snapVertices = true)
        {
            var (e, _, p, _) = RayPick(screen);
            if (e != null)
            {
                if (!snapVertices) return p;
                e.EnsureWorld();
                double best = 12;
                Vec3? snap = null;
                foreach (var v in e.WV!)
                {
                    var (s, _, ok) = Cam.Project(v);
                    if (!ok) continue;
                    double dist = (s - screen).Length;
                    if (dist < best)
                    {
                        best = dist;
                        snap = v;
                    }
                }

                return snap ?? p;
            }

            return Cam.OnPlane(screen, Vec3.Zero, Vec3.UnitZ);
        }

        /// <summary>True when nothing of the mesh hides the world point from the eye.</summary>
        private bool Visible(Entry e, Vec3 world)
        {
            if (_ed.Shading != ShadingMode.Solid) return true;
            Vec3 o, d;
            double dist;
            if (Cam.Ortho)
            {
                d = Cam.Forward;
                o = world - d * (Cam.Distance * 10);
                dist = Cam.Distance * 10;
            }
            else
            {
                o = Cam.Position;
                var v = world - o;
                dist = v.Length;
                d = v / dist;
            }

            var lo = e.Inverse.Apply(o);
            var ld = e.Inverse.ApplyVector(d);
            if (!e.Bvh_().Raycast(lo, ld, double.PositiveInfinity, out var hit)) return true;
            double t = (e.World.Apply(hit.Point) - o).Dot(d);
            return t >= dist - Math.Max(1e-4, dist * 2e-4);
        }

        private Entry? EditEntry => _ed.Active != null && _entries.TryGetValue(_ed.Active.Id, out var e) && e.IsEdit ? e : null;

        private void EditClick(Point p, bool extend)
        {
            var e = EditEntry;
            if (e == null) return;
            e.EnsureWorld();
            if (_ed.Element == ElementMode.Face)
            {
                var (hit, _, _, face) = RayPick(p, x => x == e);
                if (hit == null || face < 0)
                {
                    if (!extend) _ed.DeselectAll();
                    return;
                }

                if (extend) _ed.ToggleElement(Array.Empty<int>(), face);
                else _ed.SelectElements(Array.Empty<int>(), new[] { face }, false);
                return;
            }

            if (_ed.Element == ElementMode.Vertex)
            {
                int best = -1;
                double bestD = 14;
                for (int v = 0; v < e.WV!.Length; v++)
                {
                    var (s, _, ok) = Cam.Project(e.WV[v]);
                    if (!ok) continue;
                    double dd = (s - p).Length;
                    if (dd < bestD && Visible(e, e.WV[v]))
                    {
                        bestD = dd;
                        best = v;
                    }
                }

                if (best < 0)
                {
                    if (!extend) _ed.DeselectAll();
                    return;
                }

                if (extend) _ed.ToggleElement(new[] { best }, -1);
                else _ed.SelectElements(new[] { best }, Array.Empty<int>(), false);
                return;
            }

            // edges
            (int A, int B) bestE = (-1, -1);
            double bestDist = 10;
            foreach (var (a, b) in e.Edges!)
            {
                var (sa, _, oka) = Cam.Project(e.WV![a]);
                var (sb, _, okb) = Cam.Project(e.WV[b]);
                if (!oka || !okb) continue;
                double t;
                double dd = SegmentDistance(p, sa, sb, out t);
                if (dd < bestDist && Visible(e, e.WV[a] + (e.WV[b] - e.WV[a]) * t))
                {
                    bestDist = dd;
                    bestE = (a, b);
                }
            }

            if (bestE.A < 0)
            {
                if (!extend) _ed.DeselectAll();
                return;
            }

            if (extend) _ed.ToggleElement(new[] { bestE.A, bestE.B }, -1);
            else _ed.SelectElements(new[] { bestE.A, bestE.B }, Array.Empty<int>(), false);
        }

        internal static double SegmentDistance(Point p, Point a, Point b, out double t)
        {
            var ab = b - a;
            double len2 = ab.X * ab.X + ab.Y * ab.Y;
            t = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((p - a).X * ab.X + (p - a).Y * ab.Y) / len2));
            var c = a + ab * t;
            return (p - c).Length;
        }

        private void BoxSelect(Rect r, bool extend, bool subtract)
        {
            if (_ed.Mode == EditorMode.Edit)
            {
                var e = EditEntry;
                if (e == null) return;
                e.EnsureWorld();
                if (_ed.Element == ElementMode.Face)
                {
                    var faces = new List<int>();
                    for (int f = 0; f < e.WC!.Length; f++)
                    {
                        var (s, _, ok) = Cam.Project(e.WC[f]);
                        if (ok && r.Contains(s) && Visible(e, e.WC[f] + e.WN![f] * 1e-3)) faces.Add(f);
                    }

                    _ed.SelectElements(Array.Empty<int>(), faces, extend, subtract);
                }
                else
                {
                    var verts = new List<int>();
                    for (int v = 0; v < e.WV!.Length; v++)
                    {
                        var (s, _, ok) = Cam.Project(e.WV[v]);
                        if (ok && r.Contains(s) && Visible(e, e.WV[v])) verts.Add(v);
                    }

                    _ed.SelectElements(verts, Array.Empty<int>(), extend, subtract);
                }

                return;
            }

            var ids = new List<string>();
            foreach (var e in _entries.Values)
            {
                e.EnsureWorld();
                bool inside = e.WV!.Any(v =>
                {
                    var (s, _, ok) = Cam.Project(v);
                    return ok && r.Contains(s);
                });
                if (!inside)
                {
                    // box entirely inside the object's projection
                    var hit = RayPick(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), x => x == e);
                    inside = hit.Entry != null;
                }

                if (inside) ids.Add(e.Obj.Id);
            }

            _ed.SelectMany(ids, extend, subtract);
        }

        // ================================================================ export of state for the overlay

        internal Vec3? Pivot()
        {
            if (_ed.Mode == EditorMode.Edit)
            {
                var e = EditEntry;
                var vs = _ed.SelectedVertexSet();
                if (e == null || vs.Count == 0) return null;
                var c = Vec3.Zero;
                foreach (int v in vs) c += e.World.Apply(e.Mesh.Vertices[v]);
                return c / vs.Count;
            }

            var roots = _ed.SelectedRoots();
            if (roots.Count == 0) return null;
            var sum = Vec3.Zero;
            foreach (var o in roots) sum += _ed.Doc.WorldMatrix(o).Translation;
            return sum / roots.Count;
        }

        internal Rect? BoxRect => _boxStart is { } s && _boxNow is { } n ? new Rect(s, n) : (Rect?)null;

        internal string? ModalHeader => _modal?.Header;
        // [gizmo] as in Montaj and Dünya (and Unity): the handle stays while it is dragged
        internal bool GizmoVisible => (_modal == null || _modal.FromGizmo) && (_ed.Tool == ModelTool.Move || _ed.Tool == ModelTool.Rotate || _ed.Tool == ModelTool.Scale) && Pivot() != null;

        /// <summary>[gizmo] While a ring is dragged: its axis, the turn so far (°), where the drag began and the fixed pivot.</summary>
        internal (int Axis, double Degrees, Point Start, Vec3 Pivot)? RingDrag =>
            _modal is { Kind: ModalKind.Rotate, FromGizmo: true } m ? (m.Axis, m.Shown, m.Start, m.Pivot) : null;
        internal int HotAxis { get; private set; } = -1;
        internal (Vec3 A, Vec3 B)? MeasureSegment => _measureA is { } a ? (a, _measureB ?? a) : ((Vec3, Vec3)?)null;
        internal bool Navigating => _nav != Nav.None;
    }
}
