using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Modeling
{
    public enum ModalKind
    {
        Move,
        Rotate,
        Scale,
        Extrude,
        Inset,

        /// <summary>K: a line drawn on the screen cuts through (Blender Knife through / Bisect); in object mode it slices the object in two.</summary>
        Knife,

        /// <summary>Ctrl+R: edge loops across a ring of quads.</summary>
        LoopCut,

        /// <summary>Wall thickness for the whole surface.</summary>
        Solidify,

        /// <summary>Ctrl+B: chamfer faces in place of the selected edges.</summary>
        Bevel
    }

    public sealed partial class ModelViewport
    {
        private enum Nav
        {
            None,
            Orbit,
            Pan,
            Zoom
        }

        private sealed class Modal
        {
            public ModalKind Kind;
            public int Axis = -1;
            public bool Plane;
            public string Typed = string.Empty;
            public bool FromGizmo;
            public Point Start;
            public Vec3 Pivot;
            public Vec3? PlaneStart;
            public readonly List<(ModelObject O, Affine World)> Objects = new List<(ModelObject, Affine)>();
            public readonly Dictionary<int, Vec3> Verts = new Dictionary<int, Vec3>();
            public Affine EW = Affine.Identity, EI = Affine.Identity;
            public PolyMesh? Base;
            public List<int> Faces = new List<int>();
            public Vec3 LocalDir;
            public Vec3 Dir;
            public double LocalPerWorld = 1;
            public double LastScreenAngle;
            public double Angle;
            public bool Individual;
            public string Header = string.Empty;

            // knife: two screen points; 0 cut, 1 delete the right-hand side, 2 delete the left-hand side
            public Point? KnifeA;
            public Point KnifeB;
            public int KnifeMode;

            // loop cut: number of cuts and the preview of the new edges (world)
            public int Cuts = 1;
            public List<(int A, int B)> Edges = new List<(int, int)>();
            public (int A, int B)? Ring;
            public readonly List<(Vec3 A, Vec3 B)> Preview = new List<(Vec3, Vec3)>();

            /// <summary>Undo text when the operator confirms (overrides the default for its kind).</summary>
            public string? Done;
        }

        private Nav _nav;
        private Point _navLast;
        private Point? _press;
        private Point? _boxStart, _boxNow;
        private bool _boxExtend, _boxSubtract;
        private Point? _rightPress;
        private Modal? _modal;
        private Vec3? _measureA, _measureB;
        private Point? _altClick;
        private bool _measureFixed;

        public bool IsModal => _modal != null;

        // ================================================================ mouse

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            var p = e.GetPosition(this);
            if (_modal != null)
            {
                if (e.ChangedButton == MouseButton.Left && _modal.Kind == ModalKind.Knife && _modal.KnifeA == null)
                {
                    _modal.KnifeA = p;
                    UpdateModal(p);
                }
                else if (e.ChangedButton == MouseButton.Left) ConfirmModal();
                else if (e.ChangedButton == MouseButton.Right) CancelModal();
                e.Handled = true;
                return;
            }

            if (SurfacePick != null && e.ChangedButton is MouseButton.Left or MouseButton.Right)
            {
                // [holes] placing: each click on an object's surface drills there; a right click stops
                if (e.ChangedButton == MouseButton.Right)
                {
                    SurfacePick = null;
                    _ed.Status("Delik yerleştirme bitti");
                }
                else if (PickFace(p) is { } hit)
                {
                    SurfacePick(hit.ObjectId, hit.Point, hit.Normal);
                }
                else
                {
                    _ed.Status("Bir nesnenin yüzeyine tıklayın · sağ tık: bitir");
                }

                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                int nav = _overlay.HitNavigator(p, out var navAxis);
                if (nav != 0)
                {
                    if (nav == 1) ViewAlong(navAxis);
                    else if (nav == 3) ToggleOrtho();
                    else if (nav == 4) FrameAll();
                    else
                    {
                        _nav = Nav.Orbit;
                        _navLast = p;
                        CaptureMouse();
                    }

                    e.Handled = true;
                    return;
                }
            }

            bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && alt))
            {
                // Alt+click without dragging selects a loop in edit mode; dragging still orbits
                _altClick = e.ChangedButton == MouseButton.Left && _ed.Mode == EditorMode.Edit ? p : null;
                var mods = Keyboard.Modifiers;
                _nav = mods.HasFlag(ModifierKeys.Shift) ? Nav.Pan : mods.HasFlag(ModifierKeys.Control) ? Nav.Zoom : Nav.Orbit;
                _navLast = p;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Right)
            {
                _rightPress = p;
                e.Handled = true;
                return;
            }

            if (e.ChangedButton != MouseButton.Left) return;
            if (_ed.Tool == ModelTool.Measure)
            {
                var hit = PickSurfacePoint(p);
                if (hit != null)
                {
                    if (_measureA == null || _measureFixed)
                    {
                        _measureA = hit;
                        _measureB = null;
                        _measureFixed = false;
                    }
                    else
                    {
                        _measureB = hit;
                        _measureFixed = true;
                        var d = _measureB.Value - _measureA.Value;
                        _ed.Status(string.Format(CultureInfo.InvariantCulture, "Ölçü: {0:0.###} mm  (ΔX {1:0.###} · ΔY {2:0.###} · ΔZ {3:0.###})", d.Length, d.X, d.Y, d.Z));
                    }

                    RefreshOverlays();
                }

                e.Handled = true;
                return;
            }

            if (GizmoVisible)
            {
                int g = _overlay.HitGizmo(p);
                if (g >= 0)
                {
                    var kind = _ed.Tool == ModelTool.Rotate ? ModalKind.Rotate : _ed.Tool == ModelTool.Scale ? ModalKind.Scale : ModalKind.Move;
                    int axis = g <= 2 ? g : g >= 4 ? g - 4 : -1;
                    StartModal(kind, p, true, axis, g >= 4);
                    e.Handled = true;
                    return;
                }
            }

            _press = p;
            _boxExtend = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            _boxSubtract = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_nav != Nav.None)
            {
                var d = p - _navLast;
                _navLast = p;
                switch (_nav)
                {
                    case Nav.Orbit:
                        Cam.Yaw -= d.X * 0.0085;
                        Cam.Pitch = Math.Max(-Math.PI / 2 + 1e-4, Math.Min(Math.PI / 2 - 1e-4, Cam.Pitch + d.Y * 0.0085));
                        if (Cam.Ortho && ViewName != "Kullanıcı Ortografik")
                        {
                            Cam.Ortho = false;
                        }

                        ViewName = Cam.Ortho ? "Kullanıcı Ortografik" : "Kullanıcı Perspektif";
                        break;
                    case Nav.Pan:
                        double px = Cam.PixelSize(Cam.Target);
                        Cam.Target = Cam.Target - Cam.Right * (d.X * px) + Cam.Up * (d.Y * px);
                        break;
                    case Nav.Zoom:
                        Cam.Distance = ClampDistance(Cam.Distance * Math.Exp(d.Y * 0.01));
                        break;
                }

                UpdateCamera();
                return;
            }

            if (_modal != null)
            {
                UpdateModal(p);
                return;
            }

            if (_press is { } start && e.LeftButton == MouseButtonState.Pressed)
            {
                if (_boxStart == null && (p - start).Length > 4) _boxStart = start;
                if (_boxStart != null)
                {
                    _boxNow = p;
                    _overlay.InvalidateVisual();
                }

                return;
            }

            if (_ed.Tool == ModelTool.Measure && _measureA != null && !_measureFixed)
            {
                _measureB = PickSurfacePoint(p);
                RefreshOverlays();
                return;
            }

            if (GizmoVisible)
            {
                int hot = _overlay.HitGizmo(p);
                if (hot != HotAxis)
                {
                    HotAxis = hot;
                    _overlay.InvalidateVisual();
                }
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_nav != Nav.None && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
            {
                _nav = Nav.None;
                ReleaseMouseCapture();
                if (_altClick is { } ac && e.ChangedButton == MouseButton.Left && (p - ac).Length < 4) SelectLoopAt(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                _altClick = null;
                return;
            }

            if (_modal != null)
            {
                if (_modal.FromGizmo && e.ChangedButton == MouseButton.Left) ConfirmModal();
                return;
            }

            if (e.ChangedButton == MouseButton.Right && _rightPress is { } rp)
            {
                _rightPress = null;
                if ((p - rp).Length < 6) ContextMenuRequested?.Invoke(p);
                return;
            }

            if (e.ChangedButton != MouseButton.Left || _press == null) return;
            var start = _press.Value;
            _press = null;
            ReleaseMouseCapture();
            if (_boxStart != null && _boxNow != null)
            {
                var r = new Rect(_boxStart.Value, _boxNow.Value);
                _boxStart = null;
                _boxNow = null;
                BoxSelect(r, _boxExtend, _boxSubtract);
                _overlay.InvalidateVisual();
                return;
            }

            _boxStart = null;
            _boxNow = null;
            bool extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (_ed.Mode == EditorMode.Edit)
            {
                EditClick(start, extend);
                return;
            }

            var id = PickObject(start);
            if (id == null)
            {
                if (!extend) _ed.Select(null);
            }
            else _ed.Select(id, extend);
        }

        private static double ClampDistance(double d) => Math.Max(0.5, Math.Min(50000, d));

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_modal is { Kind: ModalKind.LoopCut or ModalKind.Bevel } lc)   // [fillet] the wheel sets a bevel's segments too
            {
                lc.Typed = string.Empty;
                lc.Cuts = Math.Max(1, Math.Min(64, lc.Cuts + Math.Sign(e.Delta)));
                UpdateModal(e.GetPosition(this));
                e.Handled = true;
                return;
            }

            if (_modal != null) return;
            Cam.Distance = ClampDistance(Cam.Distance * Math.Pow(0.85, e.Delta / 120.0));
            UpdateCamera();
            e.Handled = true;
        }

        // ================================================================ keyboard (modal operators)

        /// <summary>Keys while a modal operator or the measure tool is active. Returns true when consumed.</summary>
        public bool HandleModalKey(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (_modal == null)
            {
                if (key == Key.Escape && _measureA != null)
                {
                    ClearMeasure();
                    return true;
                }

                return false;
            }

            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            switch (key)
            {
                case Key.Escape:
                    CancelModal();
                    return true;
                case Key.Enter:
                case Key.Space:
                    ConfirmModal();
                    return true;
                case Key.C when _modal.Kind == ModalKind.Knife && _ed.Mode == EditorMode.Edit:
                    _modal.KnifeMode = (_modal.KnifeMode + 1) % 3;
                    UpdateModal(Mouse.GetPosition(this));
                    return true;
                case Key.OemPlus when _modal.Kind is ModalKind.LoopCut or ModalKind.Bevel:
                case Key.Add when _modal.Kind is ModalKind.LoopCut or ModalKind.Bevel:
                case Key.OemMinus when _modal.Kind is ModalKind.LoopCut or ModalKind.Bevel:
                case Key.Subtract when _modal.Kind is ModalKind.LoopCut or ModalKind.Bevel:
                    _modal.Typed = string.Empty;
                    _modal.Cuts = Math.Max(1, Math.Min(64, _modal.Cuts + (key == Key.OemPlus || key == Key.Add ? 1 : -1)));
                    UpdateModal(Mouse.GetPosition(this));
                    return true;
                case Key.X:
                case Key.Y:
                case Key.Z:
                {
                    if (_modal.Kind != ModalKind.Move && _modal.Kind != ModalKind.Rotate && _modal.Kind != ModalKind.Scale) return true;
                    int axis = key == Key.X ? 0 : key == Key.Y ? 1 : 2;
                    if (_modal.Axis == axis && _modal.Plane == shift)
                    {
                        _modal.Axis = -1;
                        _modal.Plane = false;
                    }
                    else
                    {
                        _modal.Axis = axis;
                        _modal.Plane = shift && _modal.Kind != ModalKind.Rotate;
                    }

                    _modal.PlaneStart = null;
                    UpdateModal(Mouse.GetPosition(this));
                    return true;
                }

                case Key.I when _modal.Kind == ModalKind.Inset:
                    _modal.Individual = !_modal.Individual;
                    UpdateModal(Mouse.GetPosition(this));
                    return true;
                case Key.Back:
                    if (_modal.Typed.Length > 0) _modal.Typed = _modal.Typed.Substring(0, _modal.Typed.Length - 1);
                    UpdateModal(Mouse.GetPosition(this));
                    return true;
                case Key.OemMinus:
                case Key.Subtract:
                    _modal.Typed = _modal.Typed.StartsWith("-", StringComparison.Ordinal) ? _modal.Typed.Substring(1) : "-" + _modal.Typed;
                    UpdateModal(Mouse.GetPosition(this));
                    return true;
                case Key.OemPeriod:
                case Key.Decimal:
                case Key.OemComma:
                    if (!_modal.Typed.Contains('.')) _modal.Typed += ".";
                    UpdateModal(Mouse.GetPosition(this));
                    return true;
            }

            int digit = key >= Key.D0 && key <= Key.D9 ? key - Key.D0 : key >= Key.NumPad0 && key <= Key.NumPad9 ? key - Key.NumPad0 : -1;
            if (digit >= 0)
            {
                _modal.Typed += digit.ToString(CultureInfo.InvariantCulture);
                UpdateModal(Mouse.GetPosition(this));
                return true;
            }

            // swallow other keys while modal (no accidental shortcuts)
            return true;
        }

        // ================================================================ modal operators

        /// <summary>G / R / S (and the gizmo): starts an interactive transform of the selection.</summary>
        public bool StartModal(ModalKind kind, Point? at = null, bool fromGizmo = false, int axis = -1, bool plane = false)
        {
            if (_modal != null) return false;
            var p = at ?? Mouse.GetPosition(this);
            var m = new Modal { Kind = kind, Start = p, FromGizmo = fromGizmo, Axis = axis, Plane = plane };
            if (_ed.Mode == EditorMode.Edit)
            {
                var ent = EditEntry;
                if (ent == null) return false;
                if (kind == ModalKind.Extrude && _ed.SelectedFaceList().Count == 0 && _ed.SelectedEdges().Count > 0)
                {
                    // edges (vertex / edge mode): faces grow off the selected edges and their copies move (Blender E)
                    _ed.BeginChange();
                    var r = MeshCut.ExtrudeEdges(_ed.EditMesh!, _ed.SelectedEdges());
                    _ed.SelFaces.Clear();
                    _ed.SelVerts.Clear();
                    _ed.SelVerts.UnionWith(r.Vertices);
                    RefreshEditMesh();
                    if (StartModal(ModalKind.Move, p))
                    {
                        _modal!.Done = "Kenarlar çıkarıldı (extrude)";
                        return true;
                    }

                    _ed.CancelChange();
                    return false;
                }

                if (kind == ModalKind.Bevel && _ed.SelectedEdges().Count == 0)
                {
                    _ed.Status("Pah kırmak için kenar seçin (2: kenar modu)");
                    return false;
                }

                if (kind == ModalKind.Knife || kind == ModalKind.LoopCut || kind == ModalKind.Solidify || kind == ModalKind.Bevel)
                {
                    m.Edges = kind == ModalKind.Bevel ? _ed.SelectedEdges() : new List<(int, int)>();
                    if (kind == ModalKind.Bevel)
                    {
                        m.Cuts = NextBevelSegments;   // [fillet] 1: a chamfer; more: a round
                        NextBevelSegments = 1;
                    }
                    _ed.BeginChange();
                    m.Base = _ed.EditMesh!.Clone();
                    m.Faces = kind == ModalKind.Knife || kind == ModalKind.Solidify ? _ed.SelectedFaceList() : new List<int>();   // [shell] the faces a shell opens
                    m.EW = ent.World;
                    m.EI = ent.Inverse;
                    m.LocalPerWorld = 1 / Math.Max(1e-9, Math.Pow(Math.Abs(m.EW.Determinant), 1.0 / 3));
                    m.Pivot = Pivot() ?? m.EW.Apply(m.Base.Bounds.Center);
                    _modal = m;
                    CaptureMouse();
                    UpdateModal(p);
                    return true;
                }

                if (kind == ModalKind.Extrude || kind == ModalKind.Inset)
                {
                    var faces = _ed.SelectedFaceList();
                    if (faces.Count == 0)
                    {
                        _ed.Status(kind == ModalKind.Extrude ? "Çıkarmak için yüz seçin (3: yüz modu)" : "İçe kaydırmak için yüz seçin");
                        return false;
                    }

                    _ed.BeginChange();
                    m.Base = _ed.EditMesh!.Clone();
                    m.Faces = faces;
                    m.EW = ent.World;
                    m.EI = ent.Inverse;
                    m.LocalDir = MeshEdit.AverageNormal(m.Base, faces);
                    if (m.LocalDir.Length < 0.5) m.LocalDir = m.Base.FaceNormal(faces[0]);
                    var wd = m.EW.ApplyVector(m.LocalDir);
                    m.LocalPerWorld = 1 / Math.Max(1e-9, wd.Length);
                    m.Dir = wd.Normalized();
                    var c = Vec3.Zero;
                    var vs = MeshEdit.VerticesOfFaces(m.Base, faces);
                    foreach (int v in vs) c += m.EW.Apply(m.Base.Vertices[v]);
                    m.Pivot = c / Math.Max(1, vs.Count);
                }
                else
                {
                    var vs = _ed.SelectedVertexSet();
                    if (vs.Count == 0)
                    {
                        _ed.Status("Önce nokta / kenar / yüz seçin");
                        return false;
                    }

                    _ed.BeginChange();
                    m.EW = ent.World;
                    m.EI = ent.Inverse;
                    foreach (int v in vs) m.Verts[v] = _ed.EditMesh!.Vertices[v];
                    m.Pivot = Pivot() ?? Vec3.Zero;
                }
            }
            else
            {
                if (kind == ModalKind.Extrude || kind == ModalKind.Inset) return false;
                if (kind == ModalKind.LoopCut || kind == ModalKind.Solidify || kind == ModalKind.Bevel)
                {
                    _ed.Status((kind == ModalKind.LoopCut ? "Halka kesim" : kind == ModalKind.Bevel ? "Pah kırma" : "Kalınlık verme") + " düzenleme modunda çalışır (Tab)");
                    return false;
                }

                if (kind == ModalKind.Knife)
                {
                    var target = _ed.Active;
                    if (target == null || !ModelEditor.IsMeshLike(target))
                    {
                        _ed.Status("Dilimlemek için bir nesne seçin");
                        return false;
                    }

                    m.Objects.Add((target, _ed.Doc.WorldMatrix(target)));
                    m.Pivot = ModelOps.WorldBounds(_ed.Doc, new[] { target }).Center;
                    _modal = m;
                    CaptureMouse();
                    UpdateModal(p);
                    return true;
                }

                var roots = _ed.SelectedRoots();
                if (roots.Count == 0)
                {
                    _ed.Status("Önce bir nesne seçin");
                    return false;
                }

                _ed.BeginChange();
                foreach (var o in roots) m.Objects.Add((o, _ed.Doc.WorldMatrix(o)));
                m.Pivot = Pivot() ?? Vec3.Zero;
            }

            var ps = Cam.ProjectPoint(m.Pivot);
            m.LastScreenAngle = Math.Atan2(p.Y - ps.Y, p.X - ps.X);
            _modal = m;
            CaptureMouse();
            UpdateModal(p);
            return true;
        }

        /// <summary>Shift+D: the copies were just made inside an open undo step; move them right away.</summary>
        public void StartModalAfterDuplicate()
        {
            if (!StartModal(ModalKind.Move)) _ed.CommitChange("Çoğaltıldı", ModelChange.All);
        }

        /// <summary>Feeds a mouse position to the running modal operator (scripted demos).</summary>
        public void SimulateModal(Point p) => UpdateModal(p);

        /// <summary>A left click while a modal operator runs (scripted demos): the knife's first point, otherwise confirm.</summary>
        public void SimulateClick(Point p)
        {
            if (_modal == null) return;
            if (_modal.Kind == ModalKind.Knife && _modal.KnifeA == null)
            {
                _modal.KnifeA = p;
                UpdateModal(p);
                return;
            }

            UpdateModal(p);
            ConfirmModal();
        }

        private double? Typed()
        {
            if (_modal == null || _modal.Typed.Length == 0 || _modal.Typed == "-" || _modal.Typed == ".") return null;
            return double.TryParse(_modal.Typed, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;
        }

        private bool SnapActive => Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ^ _ed.SnapOn;

        private static Vec3 AxisVec(int a) => a == 0 ? Vec3.UnitX : a == 1 ? Vec3.UnitY : Vec3.UnitZ;

        private static string AxisName(int a) => a == 0 ? "X" : a == 1 ? "Y" : "Z";

        /// <summary>Movement of the mouse projected onto a world direction through the pivot (world units).</summary>
        private double AlongDirection(Point p, Point start, Vec3 pivot, Vec3 dir)
        {
            double unit = Cam.PixelSize(pivot) * 100;
            var s0 = Cam.ProjectPoint(pivot);
            var s1 = Cam.ProjectPoint(pivot + dir * unit);
            var sv = (s1 - s0) / unit; // pixels per world unit
            double len2 = sv.X * sv.X + sv.Y * sv.Y;
            if (len2 < 1e-6) return 0;
            var md = p - start;
            return (md.X * sv.X + md.Y * sv.Y) / len2;
        }

        private void UpdateModal(Point p)
        {
            var m = _modal;
            if (m == null) return;
            double grid = Math.Max(1e-6, _ed.Doc.GridMm);
            double? typed = Typed();
            bool snap = SnapActive;
            string typedText = m.Typed.Length > 0 ? $"[{m.Typed}|]  " : string.Empty;
            const string hint = "   ·   X/Y/Z eksen · Shift+X düzlem · Ctrl adım · sayı yazın · Enter onay · Esc iptal";
            Affine t;
            switch (m.Kind)
            {
                case ModalKind.Move:
                {
                    Vec3 d;
                    if (m.Axis >= 0 && !m.Plane)
                    {
                        var a = AxisVec(m.Axis);
                        double amount = typed ?? AlongDirection(p, m.Start, m.Pivot, a);
                        if (snap && typed == null) amount = Math.Round(amount / grid) * grid;
                        d = a * amount;
                        m.Header = $"Taşı ({AxisName(m.Axis)} ekseni): {typedText}{amount:0.###} mm" + hint;
                    }
                    else if (m.Axis >= 0 && m.Plane)
                    {
                        var n = AxisVec(m.Axis);
                        var hit = Cam.OnPlane(p, m.Pivot, n);
                        m.PlaneStart ??= Cam.OnPlane(m.Start, m.Pivot, n);
                        d = hit != null && m.PlaneStart != null ? hit.Value - m.PlaneStart.Value : Vec3.Zero;
                        if (snap) d = new Vec3(Math.Round(d.X / grid) * grid, Math.Round(d.Y / grid) * grid, Math.Round(d.Z / grid) * grid);
                        d -= n * d.Dot(n);
                        m.Header = $"Taşı ({AxisName(m.Axis)} hariç): ΔX {d.X:0.###}  ΔY {d.Y:0.###}  ΔZ {d.Z:0.###} mm" + hint;
                    }
                    else if (typed != null)
                    {
                        d = Vec3.UnitX * typed.Value;
                        m.Header = $"Taşı (X): {typedText}{typed.Value:0.###} mm" + hint;
                    }
                    else
                    {
                        double px = Cam.PixelSize(m.Pivot);
                        var md = p - m.Start;
                        d = Cam.Right * (md.X * px) - Cam.Up * (md.Y * px);
                        if (snap) d = new Vec3(Math.Round(d.X / grid) * grid, Math.Round(d.Y / grid) * grid, Math.Round(d.Z / grid) * grid);
                        m.Header = $"Taşı: ΔX {d.X:0.###}  ΔY {d.Y:0.###}  ΔZ {d.Z:0.###} mm" + hint;
                    }

                    t = Affine.Translate(d);
                    break;
                }

                case ModalKind.Rotate:
                {
                    var axis = m.Axis >= 0 ? AxisVec(m.Axis) : Cam.Forward;
                    var ps = Cam.ProjectPoint(m.Pivot);
                    double sa = Math.Atan2(p.Y - ps.Y, p.X - ps.X);
                    double diff = sa - m.LastScreenAngle;
                    while (diff > Math.PI) diff -= 2 * Math.PI;
                    while (diff < -Math.PI) diff += 2 * Math.PI;
                    m.LastScreenAngle = sa;
                    m.Angle += diff * 180 / Math.PI;
                    double sign = axis.Dot(Cam.Forward) >= 0 ? 1 : -1;
                    double deg = typed ?? m.Angle * sign;
                    // [grid] the rotate step of Grid and Snap, shared with Montaj and Dünya (15° unless set)
                    double turnStep = RobotWin.Studio.Shell.AppSettings.Current.WorldRotateStep > 0 ? RobotWin.Studio.Shell.AppSettings.Current.WorldRotateStep : 15;
                    if (snap && typed == null) deg = Math.Round(deg / turnStep) * turnStep;
                    t = Affine.Translate(m.Pivot) * Affine.Rotate(axis, deg) * Affine.Translate(-m.Pivot);
                    m.Header = $"Döndür ({(m.Axis >= 0 ? AxisName(m.Axis) + " ekseni" : "görüş ekseni")}): {typedText}{deg:0.##}°" + hint;
                    break;
                }

                case ModalKind.Scale:
                {
                    var ps = Cam.ProjectPoint(m.Pivot);
                    double d0 = Math.Max(8, (m.Start - ps).Length);
                    double f = typed ?? (p - ps).Length / d0;
                    if (snap && typed == null) f = Math.Round(f * 10) / 10;
                    var s = m.Axis < 0 ? new Vec3(f, f, f) :
                        m.Plane ? new Vec3(m.Axis == 0 ? 1 : f, m.Axis == 1 ? 1 : f, m.Axis == 2 ? 1 : f) :
                        new Vec3(m.Axis == 0 ? f : 1, m.Axis == 1 ? f : 1, m.Axis == 2 ? f : 1);
                    t = Affine.Translate(m.Pivot) * Affine.Scale(s) * Affine.Translate(-m.Pivot);
                    string ax = m.Axis < 0 ? string.Empty : m.Plane ? $" ({AxisName(m.Axis)} hariç)" : $" ({AxisName(m.Axis)} ekseni)";
                    m.Header = $"Ölçekle{ax}: {typedText}{f:0.###}" + hint;
                    break;
                }

                case ModalKind.Extrude:
                {
                    double amount = typed ?? AlongDirection(p, m.Start, m.Pivot, m.Dir);
                    if (snap && typed == null) amount = Math.Round(amount / grid) * grid;
                    var mesh = m.Base!.Clone();
                    var r = MeshEdit.ExtrudeFaces(mesh, m.Faces, amount * m.LocalPerWorld, m.LocalDir);
                    _ed.SetEditMesh(mesh);
                    _ed.SelectResult(r);
                    m.Header = $"Çıkar (normal yönünde): {typedText}{amount:0.###} mm   ·   Ctrl adım · sayı yazın · Enter onay · Esc iptal";
                    RefreshEditMesh();
                    return;
                }

                case ModalKind.Knife:
                {
                    m.KnifeB = p;
                    bool edit = _ed.Mode == EditorMode.Edit;
                    string what = !edit ? "Dilimle: nesne iki kapalı parçaya ayrılır"
                        : m.KnifeMode == 0 ? (m.Faces.Count > 0 ? "Bıçak: seçili yüzleri keser" : "Bıçak: tüm ağı keser")
                        : m.KnifeMode == 1 ? "Bıçak: kesip çizginin sağ tarafını siler (kesit kapatılır)"
                        : "Bıçak: kesip çizginin sol tarafını siler (kesit kapatılır)";
                    m.Header = m.KnifeA == null
                        ? what + "   ·   çizginin başlangıcına tıklayın · Esc iptal"
                        : what + $"   ·   bitişe tıklayın (Enter){(edit ? " · C kip" : string.Empty)} · Esc iptal";
                    RefreshOverlays();
                    return;
                }

                case ModalKind.LoopCut:
                {
                    if (typed is { } tc) m.Cuts = Math.Max(1, Math.Min(64, (int)Math.Round(tc)));
                    m.Ring = NearestEdge(p);
                    m.Preview.Clear();
                    _ed.SetEditMesh(m.Base!.Clone());
                    if (m.Ring is { } edge)
                    {
                        var probe = m.Base!.Clone();
                        var r = MeshCut.LoopCut(probe, edge.A, edge.B, m.Cuts);
                        var fresh = new HashSet<int>(r.Vertices);
                        var seen = new HashSet<long>();
                        foreach (var f in probe.Faces)
                        {
                            for (int k = 0; k < f.Length; k++)
                            {
                                int a = f[k], b = f[(k + 1) % f.Length];
                                if (fresh.Contains(a) && fresh.Contains(b) && seen.Add(PolyMesh.EdgeKey(a, b)))
                                    m.Preview.Add((m.EW.Apply(probe.Vertices[a]), m.EW.Apply(probe.Vertices[b])));
                            }
                        }
                    }

                    m.Header = $"Halka kesim: {typedText}{m.Cuts} kesim{(m.Ring == null ? " · bir kenarın üzerine gelin" : string.Empty)}   ·   tekerlek veya +/− kesim sayısı · tık onay · Esc iptal";
                    RefreshEditMesh();
                    RefreshOverlays();
                    return;
                }

                case ModalKind.Bevel:
                {
                    double px = Cam.PixelSize(m.Pivot);
                    double width = typed ?? Math.Max(0, (p.X - m.Start.X) * px);
                    if (snap && typed == null) width = Math.Round(width / (grid / 2)) * (grid / 2);
                    var mesh = m.Base!.Clone();
                    var r = MeshCut.Bevel(mesh, m.Edges, width * m.LocalPerWorld, m.Cuts);
                    _ed.SetEditMesh(mesh);
                    _ed.SelectResult(r);
                    m.Header = m.Cuts > 1
                        ? $"Yuvarla ({m.Edges.Count} kenar, {m.Cuts} parça): {typedText}{width:0.###} mm   ·   fareyi sağa sürükleyin · sayı yazın · tekerlek veya +/−: parça sayısı (1: pah) · Enter onay · Esc iptal"
                        : $"Pah kır ({m.Edges.Count} kenar): {typedText}{width:0.###} mm   ·   fareyi sağa sürükleyin · sayı yazın · tekerlek veya +: yuvarlat · Enter onay · Esc iptal";
                    RefreshEditMesh();
                    return;
                }

                case ModalKind.Solidify:
                {
                    double px = Cam.PixelSize(m.Pivot);
                    double thick = typed ?? Math.Max(0, (p.X - m.Start.X) * px);
                    if (snap && typed == null) thick = Math.Round(thick / (grid / 2)) * (grid / 2);
                    var mesh = m.Base!.Clone();
                    if (m.Faces.Count > 0) MeshCut.Shell(mesh, m.Faces, thick * m.LocalPerWorld);   // [shell] SolidWorks' Shell: the selected faces opened
                    else MeshCut.Solidify(mesh, thick * m.LocalPerWorld);
                    _ed.SetEditMesh(mesh);
                    _ed.SelVerts.Clear();
                    _ed.SelFaces.Clear();
                    m.Header = m.Faces.Count > 0
                        ? $"Kabuk ({m.Faces.Count} yüz açık): {typedText}{thick:0.###} mm duvar   ·   fareyi sağa sürükleyin · sayı yazın · Enter onay · Esc iptal"
                        : $"Kalınlık ver: {typedText}{thick:0.###} mm (içe doğru)   ·   fareyi sağa sürükleyin · sayı yazın · Enter onay · Esc iptal";
                    RefreshEditMesh();
                    return;
                }

                default:
                {
                    double px = Cam.PixelSize(m.Pivot);
                    double thick = typed ?? Math.Max(0, (p.X - m.Start.X) * px);
                    if (snap && typed == null) thick = Math.Round(thick / (grid / 2)) * (grid / 2);
                    var mesh = m.Base!.Clone();
                    var r = MeshEdit.InsetFaces(mesh, m.Faces, Math.Max(0, thick) * m.LocalPerWorld, m.Individual);
                    _ed.SetEditMesh(mesh);
                    _ed.SelectResult(r);
                    m.Header = $"İçe kaydır{(m.Individual ? " (tek tek)" : string.Empty)}: {typedText}{thick:0.###} mm   ·   fareyi sağa sürükleyin · I tek tek · Enter onay · Esc iptal";
                    RefreshEditMesh();
                    return;
                }
            }

            if (_ed.Mode == EditorMode.Edit)
            {
                var mesh = _ed.EditMesh!;
                foreach (var kv in m.Verts) mesh.Vertices[kv.Key] = m.EI.Apply(t.Apply(m.EW.Apply(kv.Value)));
                RefreshEditMesh();
            }
            else
            {
                foreach (var (o, w) in m.Objects)
                {
                    if (m.Kind == ModalKind.Scale && m.Axis >= 0)
                    {
                        // scale along the object's own axes keeps the transform free of shear
                        var nw = t * w;
                        var (pos, _, _) = ModelOps.Decompose(nw);
                        var (_, rot0, sc0) = ModelOps.Decompose(_ed.Doc.ParentMatrix(o).Inverse() * w);
                        var f = new Vec3(t.M00, t.M11, t.M22);
                        var r = Affine.Euler(rot0);
                        // world axis factor mapped to the local axis that is most aligned with it
                        double Fx(Vec3 localAxis)
                        {
                            var wa = r.ApplyVector(localAxis);
                            int dom = Math.Abs(wa.X) >= Math.Abs(wa.Y) && Math.Abs(wa.X) >= Math.Abs(wa.Z) ? 0 : Math.Abs(wa.Y) >= Math.Abs(wa.Z) ? 1 : 2;
                            return dom == 0 ? f.X : dom == 1 ? f.Y : f.Z;
                        }

                        ModelOps.SetWorldMatrix(_ed.Doc, o, nw);
                        o.Transform.Position = (_ed.Doc.ParentMatrix(o).Inverse()).Apply(pos);
                        o.Transform.Rotation = rot0;
                        o.Transform.Scale = new Vec3(sc0.X * Fx(Vec3.UnitX), sc0.Y * Fx(Vec3.UnitY), sc0.Z * Fx(Vec3.UnitZ));
                    }
                    else ModelOps.SetWorldMatrix(_ed.Doc, o, t * w);
                }

                RefreshTransforms();
            }
        }

        public void ConfirmModal()
        {
            var m = _modal;
            if (m == null) return;
            if (m.Kind == ModalKind.Knife && m.KnifeA == null) return; // still waiting for the first point
            if (m.Kind == ModalKind.LoopCut && m.Ring == null)
            {
                _ed.Status("Halka kesim için bir kenarın üzerine gelin");
                return;
            }

            _modal = null;
            ReleaseMouseCapture();
            if (m.Kind == ModalKind.Knife)
            {
                ConfirmKnife(m);
                return;
            }

            if (m.Kind == ModalKind.LoopCut)
            {
                var mesh = m.Base!.Clone();
                var r = MeshCut.LoopCut(mesh, m.Ring!.Value.A, m.Ring.Value.B, m.Cuts);
                _ed.SetEditMesh(mesh);
                _ed.SelFaces.Clear();
                _ed.SelVerts.Clear();
                if (_ed.Element != ElementMode.Face) _ed.SelVerts.UnionWith(r.Vertices);
                m.Preview.Clear();
                _ed.CommitChange($"Halka kesildi ({m.Cuts} kesim)", ModelChange.Geometry);
                RefreshOverlays();
                return;
            }

            string what = m.Done ?? m.Kind switch
            {
                ModalKind.Move => "Taşındı",
                ModalKind.Rotate => "Döndürüldü",
                ModalKind.Scale => "Ölçeklendi",
                ModalKind.Extrude => "Çıkarıldı (extrude)",
                ModalKind.Solidify => "Kalınlık verildi",
                ModalKind.Bevel => m.Cuts > 1 ? $"Kenar yuvarlatıldı ({m.Cuts} parça)" : "Pah kırıldı (bevel)",
                _ => "İçe kaydırıldı (inset)"
            };
            if (m.Kind == ModalKind.Extrude || m.Kind == ModalKind.Inset || (m.Kind == ModalKind.Move && m.Done != null))
            {
                // drop zero-length walls of an extrusion cancelled at zero distance
                var mesh = _ed.EditMesh!;
                if (mesh.RemoveDegenerateFaces(1e-12) > 0)
                {
                    mesh.Weld(1e-9);
                    _ed.SelVerts.Clear();
                    _ed.SelFaces.Clear();
                }
            }

            _ed.CommitChange(what, ModelChange.Geometry);
        }

        public void CancelModal()
        {
            if (_modal == null) return;
            _modal = null;
            ReleaseMouseCapture();
            _ed.CancelChange();
            _ed.Status("İptal edildi");
            RefreshOverlays();
        }

        /// <summary>
        /// The plane of a knife line: it holds both screen rays, so it cuts exactly along what was drawn
        /// (through the eye in perspective, along the view in orthographic).
        /// </summary>
        private (Vec3 Point, Vec3 Normal)? KnifePlane(Point a, Point b)
        {
            if ((a - b).Length < 4) return null;
            var (o0, d0) = Cam.Ray(a);
            var (o1, d1) = Cam.Ray(b);
            var n = (o1 - o0).Length < 1e-9 ? d0.Cross(d1) : d0.Cross(o1 - o0);
            if (n.Length < 1e-12) return null;
            return (o0, n.Normalized());
        }

        private void ConfirmKnife(Modal m)
        {
            var plane = KnifePlane(m.KnifeA!.Value, m.KnifeB);
            if (plane == null)
            {
                _ed.CancelChange();
                _ed.Status("Çizgi çok kısa: iptal edildi");
                RefreshOverlays();
                return;
            }

            var (pt, n) = plane.Value;
            if (_ed.Mode == EditorMode.Edit)
            {
                // world plane → mesh space (normals map with the transpose of the linear part)
                var w = m.EW;
                var lp = m.EI.Apply(pt);
                var ln = new Vec3(w.M00 * n.X + w.M10 * n.Y + w.M20 * n.Z, w.M01 * n.X + w.M11 * n.Y + w.M21 * n.Z, w.M02 * n.X + w.M12 * n.Y + w.M22 * n.Z);
                var mesh = m.Base!.Clone();
                var mode = m.KnifeMode == 1 ? BisectMode.ClearPositive : m.KnifeMode == 2 ? BisectMode.ClearNegative : BisectMode.Cut;
                var r = MeshCut.Bisect(mesh, lp, ln, m.Faces.Count > 0 ? m.Faces : null, mode);
                _ed.SetEditMesh(mesh);
                _ed.SelFaces.Clear();
                _ed.SelVerts.Clear();
                if (_ed.Element != ElementMode.Face) _ed.SelVerts.UnionWith(r.Vertices);
                _ed.CommitChange(r.Vertices.Count == 0 ? "Bıçak hiçbir yüzü kesmedi" : mode == BisectMode.Cut ? "Bıçakla kesildi" : "Kesildi, bir taraf silindi ve kapatıldı", ModelChange.Geometry);
                RefreshEditMesh();
                RefreshOverlays();
                return;
            }

            var target = m.Objects.Count > 0 ? m.Objects[0].O : null;
            if (target != null) _ed.Slice(target, pt, n);
            RefreshOverlays();
        }

        /// <summary>Alt+click in edit mode: the edge loop (face loop in face mode) through the edge under the cursor.</summary>
        public void SelectLoopAt(Point p, bool extend)
        {
            var ent = EditEntry;
            var mesh = _ed.EditMesh;
            if (ent == null || mesh == null) return;
            var edge = NearestEdge(p, mesh, ent.World);
            if (edge == null) return;
            _ed.SelectLoop(edge.Value.A, edge.Value.B, extend);
            RefreshEditMesh();
        }

        /// <summary>The mesh edge nearest to the cursor on screen (within 40 px) in the edited object.</summary>
        private (int A, int B)? NearestEdge(Point p)
        {
            var m = _modal;
            return m?.Base == null ? null : NearestEdge(p, m.Base, m.EW);
        }

        private (int A, int B)? NearestEdge(Point p, PolyMesh mesh, Affine world)
        {
            var screen = mesh.Vertices.Select(v => Cam.ProjectPoint(world.Apply(v))).ToArray();
            double best = 40;
            (int, int)? hit = null;
            foreach (var (a, b) in mesh.Edges())
            {
                var s0 = screen[a];
                var s1 = screen[b];
                var d = s1 - s0;
                double len2 = d.X * d.X + d.Y * d.Y;
                double t = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((p.X - s0.X) * d.X + (p.Y - s0.Y) * d.Y) / len2));
                var q = s0 + d * t;
                double dist = (p - q).Length;
                if (dist < best)
                {
                    best = dist;
                    hit = (a, b);
                }
            }

            return hit;
        }

        /// <summary>Draws the constraint axis line through the pivot while a modal transform runs.</summary>
        private void ModalOverlay(Ribbons rb)
        {
            var m = _modal;
            if (m == null) return;
            if (m.Kind == ModalKind.Knife)
            {
                var knife = System.Windows.Media.Color.FromRgb(0xF2, 0xC2, 0x4B);
                if (m.KnifeA is not { } ka) return;
                var a = Cam.OnPlane(ka, m.Pivot, Cam.Forward);
                var b = Cam.OnPlane(m.KnifeB, m.Pivot, Cam.Forward);
                if (a != null) rb.Dot(a.Value, knife, 8);
                if (a != null && b != null) rb.Line(a.Value, b.Value, knife, 2);
                return;
            }

            if (m.Kind == ModalKind.LoopCut)
            {
                var cut = System.Windows.Media.Color.FromRgb(0xF2, 0xC2, 0x4B);
                foreach (var (a, b) in m.Preview) rb.Line(a, b, cut, 2);
                return;
            }

            if (m.Kind == ModalKind.Extrude)
            {
                double len = Cam.PixelSize(m.Pivot) * 4000;
                rb.Line(m.Pivot - m.Dir * len, m.Pivot + m.Dir * len, AxisTint(m.Dir), 1.2);
                return;
            }

            if (m.Axis < 0) return;
            var c = m.Axis == 0 ? AxisXColor : m.Axis == 1 ? AxisYColor : AxisZColor;
            double l = Cam.PixelSize(m.Pivot) * 4000;
            if (!m.Plane)
            {
                var a = AxisVec(m.Axis);
                rb.Line(m.Pivot - a * l, m.Pivot + a * l, c, 1.3);
            }
            else
            {
                for (int k = 0; k < 3; k++)
                {
                    if (k == m.Axis) continue;
                    var a = AxisVec(k);
                    rb.Line(m.Pivot - a * l, m.Pivot + a * l, k == 0 ? AxisXColor : k == 1 ? AxisYColor : AxisZColor, 1.1);
                }
            }
        }

        private static System.Windows.Media.Color AxisTint(Vec3 dir)
        {
            var a = new Vec3(Math.Abs(dir.X), Math.Abs(dir.Y), Math.Abs(dir.Z));
            return a.X >= a.Y && a.X >= a.Z ? AxisXColor : a.Y >= a.Z ? AxisYColor : AxisZColor;
        }

        // ================================================================ measure tool

        public void ClearMeasure()
        {
            _measureA = null;
            _measureB = null;
            _measureFixed = false;
            RefreshOverlays();
        }

        private void MeasureOverlay(Ribbons rb)
        {
            if (_measureA is not { } a) return;
            var c = System.Windows.Media.Color.FromRgb(0xF2, 0xC2, 0x4B);
            rb.Dot(a, c, 7);
            if (_measureB is { } b)
            {
                rb.Dot(b, c, 7);
                rb.Line(a, b, c, 2);
                // axis-aligned legs
                var p1 = new Vec3(b.X, a.Y, a.Z);
                var p2 = new Vec3(b.X, b.Y, a.Z);
                if ((p1 - a).Length > 1e-6) rb.Line(a, p1, AxisXColor, 1);
                if ((p2 - p1).Length > 1e-6) rb.Line(p1, p2, AxisYColor, 1);
                if ((b - p2).Length > 1e-6) rb.Line(p2, b, AxisZColor, 1);
            }
        }
    }
}
