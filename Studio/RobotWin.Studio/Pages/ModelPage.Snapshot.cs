using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using RobotWin.Studio.Modeling;

namespace RobotWin.Studio.Pages
{
    public partial class ModelPage
    {
        /// <summary>
        /// Builds a demo model for screenshots / docs. <paramref name="demo"/>: "parts" (default),
        /// "edit", "boolean", "xray", "wire", "modal", "library", "empty".
        /// </summary>
        public void PrepareSnapshot(string? demo)
        {
            // [occt] "cad:<file>": a STEP / IGES file imported through the Open CASCADE bridge (as File → Import does).
            if (demo != null && demo.StartsWith("cad:", StringComparison.OrdinalIgnoreCase))
            {
                _ed.Load(ModelEditor.NewDocument(), null);
                ImportMesh(demo.Substring(4));
                _vp.FrameAll();
                return;
            }

            demo = (demo ?? "parts").ToLowerInvariant();
            if (demo == "e2e")
            {
                RunEndToEnd();
                return;
            }

            if (demo == "empty")
            {
                _vp.FrameAll();
                return;
            }

            if (demo is "sketch" or "sketch-editor" or "sketch-draw")
            {
                // [sketch] A motor bracket drawn as a dimensioned sketch: a 50 × 40 plate with a round end tangent to its
                // sides, a bolt hole and a bearing seat round the end's centre, fully constrained; extruded 6 mm.
                var bracket = new ModelDocument { Title = "Ölçülü eskiz" };
                var plate = ModelOps.AddPrimitive(bracket, PrimitiveKind.Extrude);
                plate.Name = "Motor braketi";
                plate.Primitive!.Set("h", 6);
                plate.Color = "#3B82C4";
                plate.Primitive.Sketch = new Sketch2D { Constrained = ConstrainedSketch.Bracket() };
                plate.Primitive.Sketch.Constrained.Solve();
                plate.Primitive.Sketch.ApplyConstrained();
                _ed.Load(bracket, null);
                _ed.Select(plate.Id);
                _vp.FrameAll();
                if (demo is "sketch-editor" or "sketch-draw")
                {
                    var start = demo == "sketch-draw" ? new ConstrainedSketch() : plate.Primitive.Sketch.Constrained;
                    var editor = new SketchEditorWindow(Window.GetWindow(this), start, _ => { });
                    editor.Show();
                    if (demo == "sketch-draw")
                    {
                        // Drawn as a user would: a rough 60 × 30 rectangle, a circle in it, the bottom given 64 mm, the
                        // circle 10 mm, its centre level with the rectangle's left corner (the log says how it came out).
                        editor.UpdateLayout();
                        editor.Script(new (string, double, double, string?)[]
                        {
                            ("rect", -30, -15, null), ("rect", 31, 16, null),
                            ("circle", 0.2, 0.4, null), ("circle", 6, 0.4, null),
                            ("select", 0, -15, null), ("relate", 0, 0, "Uzunluk"), ("value", 0, 0, "64"),
                            ("select", 6.2, 0.4, null), ("relate", 0, 0, "Çap"), ("value", 0, 0, "10"),
                            ("select", 45, 40, null)
                        });
                        var r = editor.Result;
                        string log = Environment.GetEnvironmentVariable("ROBOTWIN_SNAPSHOT_LOG") ?? "";
                        if (log.Length > 0)
                        {
                            var sk = editor.Current;
                            File.AppendAllText(log, $"sketch-draw: {r?.State} serbestlik {r?.Freedom}; " + string.Join(" ", sk.Points.Select(p => $"{p.Id}({p.X:0.###},{p.Y:0.###})")) +
                                "; " + string.Join(" ", sk.Constraints.Select(c => $"{c.Id}:{c.Kind}:{c.A}:{c.B}:{c.Value:0.###}")) + Environment.NewLine);
                        }
                    }

                    string? shot = Environment.GetCommandLineArgs().SkipWhile(a => a != "--snapshot").Skip(1).FirstOrDefault();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (shot != null && editor.Content is FrameworkElement content && content.ActualWidth > 0)
                        {
                            var bmp = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right),
                                (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
                            bmp.Render(content);
                            var png = new PngBitmapEncoder();
                            png.Frames.Add(BitmapFrame.Create(bmp));
                            using var file = File.Create(Path.ChangeExtension(shot, null) + "-sketch.png");
                            png.Save(file);
                        }

                        editor.Close();
                    }), DispatcherPriority.ApplicationIdle);
                }

                return;
            }

            var doc = new ModelDocument { Title = "2WD robot gövdesi" };
            switch (demo)
            {
                case "cut":
                case "cutedit":
                {
                    // a tube sliced at an angle, the top piece lifted and turned; a block with loop cuts and a hollow shell
                    var tube = ModelOps.AddPrimitive(doc, PrimitiveKind.Tube);
                    tube.Name = "Boru";
                    tube.Primitive!.Set("do", 50);
                    tube.Primitive.Set("di", 34);
                    tube.Primitive.Set("h", 70);
                    tube.Color = "#3B82C4";
                    tube.Transform.Position = new Vec3(-45, 0, 35);
                    var top = ModelOps.Slice(doc, tube, new Vec3(-45, 0, 42), new Vec3(0.45, 0, 1))!;
                    top.Name = "Boru (üst parça)";
                    top.Transform.Position = new Vec3(-52, 0, 58);
                    top.Transform.Rotation = new Vec3(0, -24, 0);

                    var block = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
                    block.Name = "Kutu";
                    block.Primitive!.Set("x", 60);
                    block.Primitive.Set("y", 40);
                    block.Primitive.Set("z", 30);
                    block.Transform.Position = new Vec3(45, 0, 15);
                    ModelOps.ConvertToMesh(block);
                    var mesh = block.LocalMesh().Clone();
                    int t0 = Enumerable.Range(0, mesh.Faces.Count).First(f => mesh.FaceNormal(f).Z > 0.9);
                    var tf = mesh.Faces[t0];
                    MeshCut.LoopCut(mesh, tf[0], tf[1], 3);
                    // open the top (all quads of the cut top face) and give the walls 2.5 mm: a tray
                    MeshEdit.DeleteFaces(mesh, Enumerable.Range(0, mesh.Faces.Count).Where(f => mesh.FaceNormal(f).Z > 0.9).ToList());
                    MeshCut.Solidify(mesh, 2.5);
                    block.SetMesh(mesh);

                    // a block with every edge chamfered (Ctrl+B)
                    var bev = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
                    bev.Name = "Pahlı blok";
                    bev.Primitive!.Set("x", 36);
                    bev.Primitive.Set("y", 36);
                    bev.Primitive.Set("z", 24);
                    bev.Transform.Position = new Vec3(5, -62, 12);
                    bev.Color = "#D9A441";
                    ModelOps.ConvertToMesh(bev);
                    var bm = bev.LocalMesh().Clone();
                    MeshCut.Bevel(bm, bm.Edges(), 4);
                    bev.SetMesh(bm);
                    _ed.Load(doc, null);
                    if (demo == "cutedit")
                    {
                        _ed.Select(block.Id);
                        _ed.ToggleEditMode();
                        _ed.SetElementMode(ElementMode.Edge);
                    }
                    else
                    {
                        _ed.Select(tube.Id);
                        _ed.Select(top.Id, true);
                    }

                    break;
                }

                case "edit":
                case "editv":
                {
                    var box = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
                    box.Primitive!.Set("x", 60);
                    box.Primitive.Set("y", 40);
                    box.Primitive.Set("z", 20);
                    box.Transform.Position = new Vec3(0, 0, 10);
                    ModelOps.ConvertToMesh(box);
                    var mesh = box.LocalMesh().Clone();
                    MeshEdit.Subdivide(mesh);
                    int top = Enumerable.Range(0, mesh.Faces.Count).Where(f => mesh.FaceNormal(f).Z > 0.9).OrderBy(f => mesh.FaceCenter(f).X + mesh.FaceCenter(f).Y).First();
                    var r = MeshEdit.InsetFaces(mesh, new[] { top }, 3);
                    MeshEdit.ExtrudeFaces(mesh, r.Faces, 12);
                    box.SetMesh(mesh);
                    box.Name = "Gövde";
                    _ed.Load(doc, null);
                    _ed.Select(box.Id);
                    _ed.ToggleEditMode();
                    _ed.SetElementMode(ElementMode.Face);
                    var em = _ed.EditMesh!;
                    var topFaces = Enumerable.Range(0, em.Faces.Count).Where(f => em.FaceNormal(f).Z > 0.9 && em.FaceCenter(f).Z > 15).ToList();
                    _ed.SelectElements(Array.Empty<int>(), topFaces, false);
                    if (demo == "editv") _ed.SetElementMode(ElementMode.Vertex);

                    _ed.Tool = ModelTool.Move;
                    SetTool(ModelTool.Move);
                    break;
                }

                case "boolean":
                {
                    var plate = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
                    plate.Name = "Motor plakası";
                    plate.Primitive!.Set("x", 90);
                    plate.Primitive.Set("y", 60);
                    plate.Primitive.Set("z", 6);
                    plate.Primitive.Set("fillet", 2);
                    plate.Color = "#3B82C4";
                    var cut = ModelOps.AddPrimitive(doc, PrimitiveKind.Cylinder);
                    cut.Primitive!.Set("d", 22);
                    cut.Primitive.Set("h", 20);
                    cut.Transform.Position = new Vec3(0, 0, 3);
                    var tools = new[] { cut }.ToList();
                    for (int i = 0; i < 4; i++)
                    {
                        var h = ModelOps.AddPrimitive(doc, PrimitiveKind.Cylinder);
                        h.Primitive!.Set("d", 3.2);
                        h.Primitive.Set("h", 20);
                        h.Primitive.Set("seg", 24);
                        h.Transform.Position = new Vec3((i % 2 == 0 ? -1 : 1) * 36, (i < 2 ? -1 : 1) * 22, 3);
                        tools.Add(h);
                    }

                    var slot = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
                    slot.Primitive!.Set("x", 30);
                    slot.Primitive.Set("y", 8);
                    slot.Primitive.Set("z", 3);
                    slot.Transform.Position = new Vec3(0, 22, 6);
                    tools.Add(slot);
                    ModelOps.Boolean(doc, plate, tools, BooleanOp.Difference);
                    var boss = ModelOps.AddPrimitive(doc, PrimitiveKind.Tube);
                    boss.Name = "Yatak yuvası";
                    boss.Primitive!.Set("do", 30);
                    boss.Primitive.Set("di", 22);
                    boss.Primitive.Set("h", 12);
                    boss.Transform.Position = new Vec3(0, 0, 8.5);
                    boss.Color = "#E0A030";
                    _ed.Load(doc, null);
                    _ed.Select(plate.Id);
                    _ed.WireOverlay = true;
                    _ed.Notify(ModelChange.All);
                    _ed.Status($"Fark: 'Motor plakası' (6 araç) · {plate.LocalMesh().Faces.Count} yüz · kapalı (su geçirmez)");
                    break;
                }

                case "print":
                case "print-turned":
                {
                    // [print] A cap on a stem, printed cap up: the cap's underside needs support; the cap down, none.
                    var stem = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
                    stem.Name = "Mantar tutucu";
                    stem.Primitive!.Set("x", 12);
                    stem.Primitive.Set("y", 12);
                    stem.Primitive.Set("z", 24);
                    stem.Transform.Position = new Vec3(0, 0, 12);
                    var cap = ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
                    cap.Primitive!.Set("x", 40);
                    cap.Primitive.Set("y", 40);
                    cap.Primitive.Set("z", 5);
                    cap.Transform.Position = new Vec3(0, 0, 26);
                    ModelOps.Boolean(doc, stem, new[] { cap }.ToList(), BooleanOp.Union);
                    stem.Material = "PLA";
                    stem.Color = "#E87A2C";
                    _ed.Load(doc, null);
                    _ed.Select(stem.Id);
                    if (demo == "print-turned") _ed.Do("Baskı yönü", () => TurnDown(stem, new Vec3(0, 0, 1)));   // as its button does
                    _ed.Notify(ModelChange.All);
                    // The inspector scrolled to the print checks.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        foreach (var g in Props.Children.OfType<System.Windows.Controls.Grid>())
                        {
                            if (g.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault()?.Text != "3B baskı") continue;
                            g.BringIntoView(new Rect(0, 0, 10, 900));
                            break;
                        }
                    }), DispatcherPriority.Loaded);
                    break;
                }

                default:
                {
                    var chassis = RobotParts.Insert(doc, "chassis-2wd", 0, 0)[0];
                    chassis.Transform.Z = 24 + 3 / 2.0 + 0.0;
                    chassis.Transform.Position = new Vec3(0, 0, 30);
                    var servo = RobotParts.Insert(doc, "sg90-bracket", 40, 0)[0];
                    servo.Transform.Position = new Vec3(45, -12, 33);
                    var box = RobotParts.Insert(doc, "enclosure", 0, 0);
                    box[0].Transform.Position = new Vec3(-25, 0, 33 + 17.5);
                    box[0].Primitive!.Set("z", 30);
                    box[0].Transform.Position = new Vec3(-25, 0, 33 + 15);
                    box[1].Transform.Position = new Vec3(-25, 0, 33 + 30 + 18);
                    foreach (int side in new[] { -1, 1 })
                    {
                        var hub = RobotParts.Insert(doc, "wheel-hub", 0, 0)[0];
                        hub.Transform.Position = new Vec3(30, side * 62, 12);
                        hub.Transform.RX = side * -90;
                        var tire = ModelOps.AddPrimitive(doc, PrimitiveKind.Torus);
                        tire.Name = "Lastik";
                        tire.Primitive!.Set("dm", 50);
                        tire.Primitive.Set("dt", 12);
                        tire.Transform.Position = new Vec3(30, side * 64, 12);
                        tire.Transform.RX = 90;
                        tire.Color = "#2A2D34";
                        tire.Material = "KAUÇUK";
                        tire.SmoothShading = true;
                    }

                    var lb = RobotParts.Insert(doc, "l-bracket", 0, 0)[0];
                    lb.Transform.Position = new Vec3(-62, -40, 33);
                    lb.Transform.RZ = 90;
                    var caster = ModelOps.AddPrimitive(doc, PrimitiveKind.Sphere);
                    caster.Name = "Sarhoş teker";
                    caster.Primitive!.Set("d", 14);
                    caster.Transform.Position = new Vec3(-57, 0, 7);
                    caster.Color = "#B8BCC4";
                    caster.Material = "ÇELİK";
                    caster.SmoothShading = true;
                    _ed.Load(doc, null);
                    _ed.Select(chassis.Id);
                    if (demo == "xray") _ed.Shading = ShadingMode.XRay;
                    if (demo == "wire") _ed.Shading = ShadingMode.Wireframe;
                    SetTool(ModelTool.Move);
                    if (demo == "library") SideTabs.SelectedIndex = 1;
                    _ed.Notify(ModelChange.All);
                    _ed.Status("2WD şasi seçili · Özellikler panelinden ölçüleri değiştirin, G ile taşıyın");
                    break;
                }
            }

            _vp.Cam.Yaw = Math.PI / 2 - 0.62;
            _vp.Cam.Pitch = 0.52;
            _vp.FrameAll();
            _vp.Cam.Distance *= demo == "edit" ? 0.95 : 0.82;
            _vp.UpdateCamera();
            if (demo == "modal")
            {
                var c = _vp.Cam.ProjectPoint(_vp.Cam.Target);
                _vp.StartModal(ModalKind.Move, new Point(c.X, c.Y), false, 0);
                _vp.SimulateModal(new Point(c.X + 120, c.Y + 10));
            }

            if (demo is "assembly" or "assembly-again" && (_main ?? Window.GetWindow(this) as MainWindow) is { } mw)
            {
                // [assembly] Every part of the model goes to the project's Montaj as a body of its own; sent again, they update.
                _ed.Select(null);
                SendToAssembly();
                if (demo == "assembly-again") SendToAssembly();
                mw.ProjectAssembly(create: false, show: true);
            }
        }

        /// <summary>Path of the e2e log (set by <see cref="RunSnapshot"/>).</summary>
        private static string? _e2eLog;

        /// <summary>Where the e2e log goes when the page is rendered inside MainWindow.</summary>
        public static string? E2eLogPath
        {
            get => _e2eLog;
            set => _e2eLog = value;
        }

        /// <summary>Scripted walk through the main features using the real editor, viewport and page code (--fault e2e).</summary>
        private void RunEndToEnd()
        {
            var log = new System.Text.StringBuilder();
            int failures = 0;
            void Check(string what, bool ok, string detail = "")
            {
                if (!ok) failures++;
                log.AppendLine((ok ? "OK   " : "FAIL ") + what + " " + detail);
            }

            string tmp = Path.Combine(Path.GetTempPath(), "rwmodel_e2e_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                _ed.Load(new ModelDocument { Title = "E2E" }, null);
                var box = _ed.AddPrimitive(PrimitiveKind.Box, new Vec3(0, 0, 0));
                var cyl = _ed.AddPrimitive(PrimitiveKind.Cylinder, new Vec3(0, 0, 0));
                cyl.Primitive!.Set("d", 8);
                cyl.Primitive.Set("h", 40);
                _ed.Select(cyl.Id);
                _ed.Select(box.Id, true);
                _ed.Boolean(BooleanOp.Difference);
                Check("boolean difference", _ed.Doc.Objects.Count == 1 && box.LocalMesh().IsClosed, "faces=" + box.LocalMesh().Faces.Count);
                _ed.Undo();
                Check("undo boolean", _ed.Doc.Objects.Count == 2);
                _ed.Redo();
                var b2 = _ed.Doc.Objects.Single();
                Check("redo boolean", _ed.Doc.Objects.Count == 1 && b2.Primitive == null);

                // modal move along X by mouse
                _ed.Select(b2.Id);
                var c = _vp.Cam.ProjectPoint(_vp.Cam.Target);
                double x0 = b2.Transform.X;
                _vp.StartModal(ModalKind.Move, c, false, 0);
                _vp.SimulateModal(new Point(c.X + 80, c.Y));
                _vp.ConfirmModal();
                Check("modal move X", Math.Abs(b2.Transform.X - x0) > 1 && Math.Abs(b2.Transform.Y) < 1e-9, "x=" + b2.Transform.X.ToString("0.###", CultureInfo.InvariantCulture));
                _ed.Undo();
                Check("undo move", Math.Abs(_ed.Doc.Objects.Single().Transform.X - x0) < 1e-9);

                // rotate about Z by mouse, then cancel
                b2 = _ed.Doc.Objects.Single();
                _ed.Select(b2.Id);
                _vp.StartModal(ModalKind.Rotate, new Point(c.X + 100, c.Y), false, 2);
                _vp.SimulateModal(new Point(c.X, c.Y - 100));
                double rotating = b2.Transform.RZ;
                _vp.CancelModal();
                Check("rotate preview + cancel", Math.Abs(rotating) > 1 && Math.Abs(_ed.Doc.Objects.Single().Transform.RZ) < 1e-9, "preview rz=" + rotating.ToString("0.#", CultureInfo.InvariantCulture));

                // edit mode: inset + extrude of the top face
                b2 = _ed.Doc.Objects.Single();
                _ed.Select(b2.Id);
                _ed.ToggleEditMode();
                _ed.SetElementMode(ElementMode.Face);
                var em = _ed.EditMesh!;
                int top = Enumerable.Range(0, em.Faces.Count).Where(f => em.FaceNormal(f).Z > 0.9).OrderByDescending(f => em.FaceArea(f)).First();
                double v0 = em.Volume();
                _ed.SelectElements(Array.Empty<int>(), new[] { top }, false);
                var ps = _vp.Cam.ProjectPoint(_ed.Doc.WorldMatrix(b2).Apply(em.FaceCenter(top)));
                _vp.StartModal(ModalKind.Inset, ps);
                _vp.SimulateModal(new Point(ps.X + 15, ps.Y));
                _vp.ConfirmModal();
                Check("inset", _ed.EditMesh!.IsClosed && Math.Abs(_ed.EditMesh.Volume() - v0) < 1e-6, "faces=" + _ed.EditMesh.Faces.Count);
                _vp.StartModal(ModalKind.Extrude, ps);
                _vp.SimulateModal(new Point(ps.X, ps.Y - 60));
                _vp.ConfirmModal();
                Check("extrude", _ed.EditMesh!.IsClosed && _ed.EditMesh.Volume() > v0 + 1, "vol " + v0.ToString("0") + " -> " + _ed.EditMesh.Volume().ToString("0"));
                _ed.SetElementMode(ElementMode.Vertex);
                _ed.SelectAll();
                _vp.StartModal(ModalKind.Scale, new Point(ps.X + 50, ps.Y));
                _vp.SimulateModal(new Point(ps.X + 100, ps.Y));
                _vp.ConfirmModal();
                Check("edit scale", _ed.EditMesh!.IsClosed);
                _ed.Subdivide();
                Check("subdivide", _ed.EditMesh!.IsClosed);
                _ed.RecalculateNormals();
                _ed.ToggleEditMode();
                Check("exit edit", _ed.Mode == EditorMode.Object && _ed.Doc.Objects.Single().LocalMesh().IsClosed);
                _ed.Undo();
                Check("undo returns to edit mode", _ed.Mode == EditorMode.Edit);
                _ed.Redo();
                if (_ed.Mode == EditorMode.Edit) _ed.ToggleEditMode();

                // copies, mirror, arrays, join
                _ed.Select(_ed.Doc.Objects[0].Id);
                _ed.DuplicateSelected();
                _ed.CommitChange("dup");
                Check("duplicate", _ed.Doc.Objects.Count == 2);
                _ed.MirrorCopy(RobotTwin.CoreSim.Modeling.Axis.X);
                Check("mirror copy", _ed.Doc.Objects.Count == 3);
                _ed.ArrayCircular(4, 360, RobotTwin.CoreSim.Modeling.Axis.Z, Vec3.Zero);
                Check("circular array", _ed.Doc.Objects.Count == 6);
                _ed.ArrayLinear(3, new Vec3(0, 40, 0));
                Check("linear array", _ed.Doc.Objects.Count >= 8, "n=" + _ed.Doc.Objects.Count);
                _ed.SelectAll();
                _ed.Join();
                Check("join", _ed.Doc.Objects.Count == 1);

                // parts
                foreach (var t in RobotParts.All.Where(t => t.Id != "enclosure-lid")) _ed.AddPart(t.Id, new Vec3(0, 0, 0));
                Check("parts added", _ed.Doc.Objects.Count == 1 + RobotParts.All.Count, "n=" + _ed.Doc.Objects.Count);
                var chassis = _ed.Doc.Objects.First(o => o.Primitive != null && o.Primitive.Template == "chassis-2wd");
                chassis.Primitive!.Set("l", 180);
                Check("part param", Math.Abs(chassis.LocalMesh().Bounds.Size.X - 180) < 1e-6);

                // save / load
                string file = Path.Combine(tmp, "e2e.rwmodel");
                _ed.Doc.Save(file);
                var back = ModelDocument.Load(file);
                Check("save/load", back.Objects.Count == _ed.Doc.Objects.Count && back.ToJson() == _ed.Doc.ToJson());

                // export / import
                string stl = Path.Combine(tmp, "e2e.stl");
                ModelOps.ToMeshModel(_ed.Doc, new[] { chassis }, "chassis").Save(stl);
                ImportMesh(stl);
                var imported = _ed.Doc.Objects.Last();
                double expect = ModelMeasure.Of(_ed.Doc, chassis).VolumeMm3;
                double got = ModelMeasure.Of(_ed.Doc, imported).VolumeMm3;
                Check("STL export/import", Math.Abs(expect - got) < expect * 1e-4 && imported.LocalMesh().IsClosed, expect.ToString("0") + " vs " + got.ToString("0") + " mm3");

                // send to world
                _ed.Select(chassis.Id);
                var wo = SendToWorld();
                Check("send to world", wo != null && wo.Kind == RobotTwin.CoreSim.Sandbox.WorldObjectKind.Model && wo.MassKg > 0 && wo.Mesh != null,
                    wo == null ? "" : wo.Name + " " + (wo.MassKg * 1000)?.ToString("0.#", CultureInfo.InvariantCulture) + " g, " + RobotTwin.CoreSim.Mechanics.MeshModel.FromEmbedded(wo.Mesh!).TriangleCount + " tris");
                Check("world contains it", wo != null && Shell.Workspace.Current.ActiveWorld?.Objects.Contains(wo) == true);

                // cutting tools on a fresh block: knife in edit mode, loop cut, solidify, then slice in object mode
                _ed.Load(new ModelDocument { Title = "Kesme araçları" }, null);
                var blk = _ed.AddPrimitive(PrimitiveKind.Box, new Vec3(0, 0, 0));
                _ed.Select(blk.Id);
                _vp.FrameAll();
                Point Screen(Vec3 local) => _vp.Cam.ProjectPoint(_ed.Doc.WorldMatrix(blk).Apply(local));
                var mid = Screen(blk.LocalMesh().Bounds.Center);
                double blockVolume = blk.LocalMesh().Volume();

                _ed.ToggleEditMode();
                _ed.SetElementMode(ElementMode.Vertex);
                _ed.DeselectAll();
                var cm = _ed.EditMesh!;
                int topFace = Enumerable.Range(0, cm.Faces.Count).Where(f => cm.FaceNormal(f).Z > 0.9).OrderByDescending(f => cm.FaceArea(f)).First();
                var tf = cm.Faces[topFace];
                int faces1 = cm.Faces.Count;
                var edgeMid = Screen((cm.Vertices[tf[0]] + cm.Vertices[tf[1]]) / 2);
                _vp.StartModal(ModalKind.LoopCut, edgeMid);
                _vp.SimulateClick(edgeMid);
                Check("loop cut", _ed.EditMesh!.IsClosed && _ed.EditMesh.Faces.Count > faces1 && Math.Abs(_ed.EditMesh.Volume() - blockVolume) < 1e-6,
                    "faces " + faces1 + " -> " + _ed.EditMesh.Faces.Count);
                var loopVerts = new System.Collections.Generic.HashSet<int>(_ed.SelVerts);
                var lm = _ed.EditMesh!;
                var loopEdge = lm.Edges().First(e => loopVerts.Contains(e.A) && loopVerts.Contains(e.B));
                _ed.DeselectAll();
                _vp.SelectLoopAt(Screen((lm.Vertices[loopEdge.A] + lm.Vertices[loopEdge.B]) / 2), false);
                Check("alt+click loop select", _ed.SelVerts.SetEquals(loopVerts), "selected " + _ed.SelVerts.Count + " of " + loopVerts.Count);
                _ed.SelectElements(new[] { loopEdge.A }, Array.Empty<int>(), false);
                _ed.SelectLinked();
                Check("select linked", _ed.SelVerts.Count == _ed.EditMesh!.Vertices.Count);
                _ed.Undo();
                Check("undo loop cut", _ed.EditMesh!.Faces.Count == faces1);

                int faces0 = _ed.EditMesh!.Faces.Count;
                _vp.StartModal(ModalKind.Knife, mid);
                _vp.SimulateClick(new Point(mid.X - 400, mid.Y - 20));
                _vp.SimulateModal(new Point(mid.X + 400, mid.Y + 20));
                _vp.SimulateClick(new Point(mid.X + 400, mid.Y + 20));
                Check("knife cut", _ed.EditMesh!.IsClosed && _ed.EditMesh.Faces.Count > faces0 && Math.Abs(_ed.EditMesh.Volume() - blockVolume) < 1e-6 && _ed.SelVerts.Count >= 4,
                    "faces " + faces0 + " -> " + _ed.EditMesh.Faces.Count + ", loop " + _ed.SelVerts.Count);

                _vp.StartModal(ModalKind.Solidify, mid);
                _vp.SimulateModal(new Point(mid.X + 25, mid.Y));
                _vp.ConfirmModal();
                double hollow = _ed.EditMesh!.Volume();
                Check("solidify (hollow)", _ed.EditMesh.IsClosed && hollow > 1 && hollow < blockVolume * 0.95, "vol " + blockVolume.ToString("0") + " -> " + hollow.ToString("0"));
                _ed.Undo();

                _ed.SetElementMode(ElementMode.Edge);
                _ed.SelectAll();
                int facesBeforeBevel = _ed.EditMesh!.Faces.Count;
                _vp.StartModal(ModalKind.Bevel, mid);
                _vp.SimulateModal(new Point(mid.X + 12, mid.Y));
                _vp.ConfirmModal();
                Check("bevel all edges", _ed.EditMesh!.IsClosed && _ed.EditMesh.Faces.Count > facesBeforeBevel && _ed.EditMesh.Volume() < blockVolume, "faces " + facesBeforeBevel + " -> " + _ed.EditMesh.Faces.Count + ", vol " + _ed.EditMesh.Volume().ToString("0"));
                _ed.Undo();
                _ed.SetElementMode(ElementMode.Vertex);
                _ed.ToggleEditMode();

                _ed.Select(blk.Id);
                _vp.StartModal(ModalKind.Knife, mid);
                _vp.SimulateClick(new Point(mid.X - 60, mid.Y - 400));
                _vp.SimulateClick(new Point(mid.X + 60, mid.Y + 400));
                var pieces = _ed.Doc.Objects.ToList();
                double pieceSum = pieces.Sum(o => o.LocalMesh().Volume());
                Check("slice in two", pieces.Count == 2 && pieces.All(o => o.LocalMesh().IsClosed) && Math.Abs(pieceSum - blockVolume) < 1e-3,
                    "pieces " + pieces.Count + ", vol " + blockVolume.ToString("0") + " = " + string.Join(" + ", pieces.Select(o => o.LocalMesh().Volume().ToString("0"))));

                // move and turn the new piece the way a user would (G, R)
                var front = _ed.Active!;
                _ed.Select(front.Id);
                var fc = _vp.Cam.ProjectPoint(ModelOps.WorldBounds(_ed.Doc, new[] { front }).Center);
                _vp.StartModal(ModalKind.Move, fc, false, 0);
                _vp.SimulateModal(new Point(fc.X + 120, fc.Y));
                _vp.ConfirmModal();
                _vp.StartModal(ModalKind.Rotate, new Point(fc.X + 100, fc.Y), false, 2);
                _vp.SimulateModal(new Point(fc.X + 60, fc.Y - 90));
                _vp.ConfirmModal();
                Check("piece moved and turned", Math.Abs(front.Transform.RZ) > 1, "rz=" + front.Transform.RZ.ToString("0.#", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                failures++;
                log.AppendLine("EXCEPTION " + ex);
            }
            finally
            {
                try
                {
                    Directory.Delete(tmp, true);
                }
                catch (IOException)
                {
                }
            }

            log.AppendLine(failures == 0 ? "E2E PASS" : "E2E FAIL (" + failures + ")");
            if (_e2eLog != null) File.WriteAllText(_e2eLog, log.ToString());
            _vp.FrameAll();
        }

        /// <summary>
        /// Renders the page alone in an off-screen window to a PNG (used by
        /// <c>RobotWinStudio.exe --snapshot out.png --page model --fault &lt;demo&gt;</c>).
        /// </summary>
        public static void RunSnapshot(string path, string? size, string? demo, Action done)
        {
            int w = 1600, h = 960;
            if (size != null)
            {
                var parts = size.Split('x');
                w = int.Parse(parts[0], CultureInfo.InvariantCulture);
                h = int.Parse(parts[1], CultureInfo.InvariantCulture);
            }

            _e2eLog = Path.ChangeExtension(path, ".txt");
            var page = new ModelPage();
            var window = new Window
            {
                Title = "RobotWin Studio — Modelle",
                Content = page,
                Width = w,
                Height = h,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -w - 200,
                Top = 0,
                ShowActivated = false,
                ShowInTaskbar = false,
                Background = (Brush)Application.Current.FindResource("B.Base")
            };
            window.Show();
            int phase = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (_, _) =>
            {
                phase++;
                if (phase == 1)
                {
                    page.PrepareSnapshot(demo);
                    return;
                }

                if (phase < 4) return;
                timer.Stop();
                window.UpdateLayout();
                var root = (FrameworkElement)window.Content;
                var dpi = VisualTreeHelper.GetDpi(window);
                var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (var fs = File.Create(path)) enc.Save(fs);
                window.Close();
                done();
            };
            timer.Start();
        }
    }
}
