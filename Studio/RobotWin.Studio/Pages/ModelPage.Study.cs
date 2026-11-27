using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Analysis.Studies;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using RobotWin.Studio.Modeling;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [part-study] A linear static study of the part, laid out as structural tools lay one out (a study tree: material,
    /// fixtures, loads, mesh, the pre-check, solve, results): faces are picked in edit mode (face select) and turned into a
    /// fixture, a force or a pressure; Solve meshes the part in quadratic tetrahedra and solves it (Analysis/Studies/
    /// PartStudy.cs); the part is then drawn in the result's colours (von Mises, displacement or safety factor) on a
    /// banded scale with its unit and an automatic or fixed range, optionally deformed. Beside the result: how far to trust
    /// it (mesh, solver, convergence, assumptions) and, after a change, the previous result side by side.
    /// </summary>
    public partial class ModelPage
    {
        private readonly StackPanel _study = new StackPanel { Margin = new Thickness(14, 10, 14, 16) };
        private ScrollViewer? _studyPane;
        private PartStudyResult? _studyResult, _studyPrevious;
        private string _studyResultFor = string.Empty;
        private CancellationTokenSource? _studyCancel;
        private string _plot = "mises";   // mises, disp, sf
        private bool _deformed = true, _fixedScale;
        private double _scaleMin, _scaleMax = 100;
        private string _stressUnit = "MPa", _dispUnit = "mm";
        private string _studyStatus = string.Empty;
        private bool _sideBySide;
        private bool _studyStale;
        private StudyCompareView? _compare;
        private Border? _compareNow;

        private ScrollViewer StudyPane() => _studyPane ??= new ScrollViewer { Content = _study, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        private ModelObject? StudyTarget => _ed.Active ?? _ed.Doc.Objects.FirstOrDefault(o => o.Visible && !o.LocalMesh().IsEmpty);

        private PartStudy? StudyOf(ModelObject? o) => o == null ? null : _ed.Doc.Studies?.FirstOrDefault(s => s.ObjectId == o.Id);

        private void OnStudy(object sender, RoutedEventArgs e)
        {
            bool on = StudyToggle.IsChecked == true;
            _dock?.SetVisible("study", on);
            if (on)
            {
                _dock?.Reveal("study");
                if (AnalysisToggle.IsChecked == true) CloseAnalysis();
                BuildStudy();
            }
            else
            {
                _vp.ResultModel = null;
                _vp.Rebuild();
                ShowCompare(false);
            }
        }

        /// <summary>A change to the study, as one undo step (the geometry is not touched).</summary>
        private void StudyEdit(string what, Action<PartStudy> change)
        {
            if (StudyTarget is not { } o) return;
            _ed.Do(Lang.T($"Statik analiz: {what}"), () =>
            {
                var list = _ed.Doc.Studies ??= new List<PartStudy>();
                var s = list.FirstOrDefault(x => x.ObjectId == o.Id);
                if (s == null)
                {
                    s = new PartStudy { ObjectId = o.Id, Name = "Statik 1", Material = string.IsNullOrWhiteSpace(o.Material) ? "PLA" : o.Material };
                    list.Add(s);
                }

                change(s);
            }, ModelChange.View);
            BuildStudy();
        }

        private List<int> PickedFaces()
        {
            var o = StudyTarget;
            if (o == null || _ed.Mode != EditorMode.Edit || _ed.ActiveId != o.Id) return new List<int>();
            return _ed.SelectedFaceList();
        }

        private void BuildStudy()
        {
            _study.Children.Clear();
            var o = StudyTarget;
            _study.Children.Add(new TextBlock { Text = Lang.T("Statik analiz"), Style = (Style)FindResource("Text.Subtitle"), Margin = new Thickness(0, 0, 0, 2) });
            if (o == null)
            {
                _study.Children.Add(Pcb.PcbForms.Caption(Lang.T("Önce bir parça modelleyin ya da seçin."), 0));
                return;
            }

            var s = StudyOf(o) ?? new PartStudy { ObjectId = o.Id, Material = string.IsNullOrWhiteSpace(o.Material) ? "PLA" : o.Material };
            _study.Children.Add(Pcb.PcbForms.Caption(Lang.T($"Parça: {o.Name} · doğrusal statik"), 0));

            // material
            _study.Children.Add(Overline(Lang.T("Malzeme")));
            var names = StructuralMaterial.Names.ToList();
            string key = names.FirstOrDefault(n => StructuralMaterial.For(n).Name == StructuralMaterial.For(s.Material).Name) ?? "PLA";
            _study.Children.Add(Combo(names, key, n => StudyEdit("malzeme", st => st.Material = n), n => StructuralMaterial.For(n).Name));
            var mat = StructuralMaterial.For(s.Material);
            _study.Children.Add(Pcb.PcbForms.Caption(string.Format(CultureInfo.CurrentCulture, "E {0:0.##} GPa · ν {1:0.##} · {2} {3:0} MPa", mat.E / 1e9, mat.Nu, Lang.T("akma"), mat.Yield / 1e6), 2));

            // fixtures
            _study.Children.Add(Overline(Lang.T("Sabitlemeler")));
            for (int i = 0; i < s.Fixtures.Count; i++)
            {
                int k = i;
                _study.Children.Add(ItemRow(Lang.T($"Sabit · {s.Fixtures[i].Faces.Count} yüz"), () => StudyEdit("sabitleme kaldırıldı", st => st.Fixtures.RemoveAt(k)), () => ShowFaces(s.Fixtures[k].Faces)));
            }

            _study.Children.Add(Buttons(Button(Lang.T("+ Seçili yüzleri sabitle"), (_, _) =>
            {
                var faces = PickedFaces();
                if (faces.Count == 0)
                {
                    _ed.Status(Lang.T("Önce düzenleme kipinde (Tab) yüz seçimiyle sabitlenecek yüzleri seçin."));
                    return;
                }

                StudyEdit("sabitleme", st => st.Fixtures.Add(new StudyFixture { Faces = faces }));
            }, tip: Lang.T("Düzenleme kipinde seçili yüzlerin her düğümü yerinde tutulur"))));

            // loads
            _study.Children.Add(Overline(Lang.T("Yükler")));
            for (int i = 0; i < s.Loads.Count; i++)
            {
                int k = i;
                var l = s.Loads[i];
                bool pressure = l.Kind == StudyLoad.Pressure;
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(ItemRow(Lang.T(pressure ? $"Basınç · {l.Faces.Count} yüz" : $"Kuvvet · {l.Faces.Count} yüz"), () => StudyEdit("yük kaldırıldı", st => st.Loads.RemoveAt(k)), () => ShowFaces(l.Faces)));
                row.Children.Add(Row(Lang.T(pressure ? "Basınç (kPa)" : "Kuvvet (N)"), Number(pressure ? l.Magnitude / 1000 : l.Magnitude, v => StudyEdit("yük", st => st.Loads[k].Magnitude = pressure ? v * 1000 : v), 0, 1e9), 110));
                if (!pressure)
                {
                    var dirs = new[] { "−Z", "+Z", "−X", "+X", "−Y", "+Y" };
                    string now = l.Z < -0.5 ? "−Z" : l.Z > 0.5 ? "+Z" : l.X < -0.5 ? "−X" : l.X > 0.5 ? "+X" : l.Y < -0.5 ? "−Y" : "+Y";
                    row.Children.Add(Row(Lang.T("Yön"), Combo(dirs, now, d => StudyEdit("yük yönü", st =>
                    {
                        var ld = st.Loads[k];
                        (ld.X, ld.Y, ld.Z) = d switch { "+Z" => (0.0, 0.0, 1.0), "−X" => (-1.0, 0.0, 0.0), "+X" => (1.0, 0.0, 0.0), "−Y" => (0.0, -1.0, 0.0), "+Y" => (0.0, 1.0, 0.0), _ => (0.0, 0.0, -1.0) };
                    })), 110));
                }

                _study.Children.Add(row);
            }

            void AddLoad(string kind)
            {
                var faces = PickedFaces();
                if (faces.Count == 0)
                {
                    _ed.Status(Lang.T("Önce düzenleme kipinde (Tab) yük binecek yüzleri seçin."));
                    return;
                }

                StudyEdit(kind == StudyLoad.Pressure ? "basınç" : "kuvvet", st => st.Loads.Add(new StudyLoad { Faces = faces, Kind = kind, Magnitude = kind == StudyLoad.Pressure ? 100_000 : 10 }));
            }

            _study.Children.Add(Buttons(
                Button(Lang.T("+ Kuvvet"), (_, _) => AddLoad(StudyLoad.Force), tip: Lang.T("Seçili yüzlere toplam bir kuvvet, düğümlerine paylaştırılır")),
                Button(Lang.T("+ Basınç"), (_, _) => AddLoad(StudyLoad.Pressure), tip: Lang.T("Seçili yüzlere dik, alanıyla orantılı bir basınç"))));
            var gravity = new CheckBox { Content = Lang.T("Yerçekimi (parçanın kendi ağırlığı)"), IsChecked = s.Gravity, Margin = new Thickness(0, 4, 0, 0) };
            gravity.Click += (_, _) => StudyEdit("yerçekimi", st => st.Gravity = gravity.IsChecked == true);
            _study.Children.Add(gravity);

            // mesh
            _study.Children.Add(Overline(Lang.T("Ağ")));
            _study.Children.Add(Row(Lang.T("Eleman boyu (mm, 0 = otomatik)"), Number(s.ElementSize, v => StudyEdit("eleman boyu", st => st.ElementSize = v), 0, 1000, true), 80));
            var check = new CheckBox { Content = Lang.T("Ağ yakınsaması denetimi (iki kaba ağla daha)"), IsChecked = s.MeshCheck };
            check.Click += (_, _) => StudyEdit("ağ denetimi", st => st.MeshCheck = check.IsChecked == true);
            _study.Children.Add(check);

            // pre-check and solve
            var pre = PartStudyRunner.Check(o.LocalMesh(), s);
            _study.Children.Add(Overline(Lang.T("Ön denetim")));
            if (pre.Ready && pre.Warnings.Count == 0) _study.Children.Add(Pcb.PcbForms.Caption(Lang.T("Çözmeye hazır."), 0));
            foreach (string e in pre.Errors) _study.Children.Add(Colored(Lang.T(e), "B.Danger"));
            foreach (string w in pre.Warnings) _study.Children.Add(Colored(Lang.T(w), "B.Warning"));
            var solve = Button(Lang.T("Çöz"), (_, _) => Solve(o, s), primary: true, tip: Lang.T("Parçayı ağlar ve çözer; sonuç parçanın üstünde renklerle görünür"));
            solve.IsEnabled = pre.Ready && _studyCancel == null;
            _study.Children.Add(Buttons(solve));
            if (_studyStatus.Length > 0) _study.Children.Add(Pcb.PcbForms.Caption(_studyStatus, 2));

            if (_studyResult != null && _studyResultFor == o.Id) Results(_studyResult);
        }

        private UIElement ItemRow(string text, Action remove, Action show)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var del = new Button { Content = "×", Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(6, 0, 6, 0), ToolTip = Lang.T("Kaldır") };
            del.Click += (_, _) => remove();
            DockPanel.SetDock(del, Dock.Right);
            row.Children.Add(del);
            var name = new Button { Content = text, Style = (Style)FindResource("Btn.Ghost"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 2, 6, 2), ToolTip = Lang.T("Yüzleri seçili göster") };
            name.Click += (_, _) => show();
            row.Children.Add(name);
            return row;
        }

        private TextBlock Colored(string text, string brush)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), FontSize = TypeScale.Caption };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        /// <summary>The faces of a fixture or a load, selected in edit mode (to see where it is).</summary>
        private void ShowFaces(List<int> faces)
        {
            if (StudyTarget is not { } o) return;
            if (_ed.Mode != EditorMode.Edit || _ed.ActiveId != o.Id)
            {
                _ed.Status(Lang.T("Yüzleri görmek için parçayı düzenleme kipinde (Tab) açın."));
                return;
            }

            _ed.SelectFaces(faces);
        }

        private void Solve(ModelObject o, PartStudy study)
        {
            var mesh = o.LocalMesh().Clone();
            var s = study.Clone();
            _studyCancel = new CancellationTokenSource();
            var token = _studyCancel.Token;
            _studyStatus = Lang.T("Çözülüyor…");
            BuildStudy();
            var started = DateTime.Now;
            Task.Run(() =>
            {
                var result = PartStudyRunner.Run(mesh, s, token);
                PartStudyRunner.Reference();   // the known case the reliability summary quotes, solved once
                return result;
            }).ContinueWith(t =>
            {
                _studyCancel = null;
                if (t.Exception != null)
                {
                    _studyStatus = Lang.T("Çözülemedi: ") + Lang.T(t.Exception.InnerException?.Message ?? t.Exception.Message);
                }
                else if (!t.IsCanceled)
                {
                    if (_studyResult != null && _studyResultFor == o.Id) _studyPrevious = _studyResult;
                    _studyResult = t.Result;
                    _studyResultFor = o.Id;
                    _studyStale = false;
                    _studyStatus = Lang.T($"Çözüldü: {(DateTime.Now - started).TotalSeconds:0.0} s.");
                    ShowResult();
                }

                BuildStudy();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------------------------------------------------------------- results

        private double[] PlotValues(PartStudyResult r) => _plot switch
        {
            "disp" => Enumerable.Range(0, r.Nodes.Length).Select(r.DisplacementAt).ToArray(),
            "sf" => r.VonMises.Select(v => v > 1 ? Math.Min(10, r.Yield / v) : 10).ToArray(),
            _ => r.VonMises,
        };

        /// <summary>The unit's factor from SI (Pa, m) and its name.</summary>
        private (double Factor, string Unit) PlotUnit() => _plot switch
        {
            "disp" => _dispUnit == "µm" ? (1e6, "µm") : (1e3, "mm"),
            "sf" => (1.0, string.Empty),
            _ => _stressUnit == "kPa" ? (1e-3, "kPa") : _stressUnit == "Pa" ? (1.0, "Pa") : (1e-6, "MPa"),
        };

        /// <summary>
        /// The colour range in the plot's unit: fixed, or the result's own (stress and displacement from 0); two results
        /// side by side share one range spanning both, so a colour means the same value in either view.
        /// </summary>
        private (double Lo, double Hi) Range(PartStudyResult r)
        {
            if (_fixedScale && _scaleMax > _scaleMin) return (_scaleMin, _scaleMax);
            var own = OwnRange(r);
            if (!_sideBySide || _studyPrevious is not { } p) return own;
            var other = OwnRange(p);
            return (Math.Min(own.Lo, other.Lo), Math.Max(own.Hi, other.Hi));
        }

        private (double Lo, double Hi) OwnRange(PartStudyResult r)
        {
            var (f, _) = PlotUnit();
            var v = PlotValues(r);
            double hi = v.Length > 0 ? v.Max() * f : 1;
            double lo = _plot == "sf" ? (v.Length > 0 ? v.Min() * f : 0) : 0;
            return (lo, Math.Max(lo + 1e-12, hi));
        }

        private void ShowResult()
        {
            if (_studyStale || _studyResult is not { } r || StudyTarget is not { } o || _studyResultFor != o.Id) return;
            var model = ResultModel(r);
            _vp.ResultModel = obj => obj.Id == _studyResultFor ? model : null;
            _vp.Rebuild();
            ShowCompare(_sideBySide && _studyPrevious != null);
        }

        /// <summary>
        /// [part-study] Splits the view: the current solve on the left in the editor's own view (its tool strip and view
        /// cube stay with it), the previous one on the right in a second view on the same camera and colour scale; or back
        /// to one view.
        /// </summary>
        private void ShowCompare(bool on)
        {
            if (!on || _studyPrevious is not { } p || StudyTarget is not { } o)
            {
                if (_compare == null) return;
                ViewHost.Children.Remove(_compare);
                if (_compareNow != null) ViewHost.Children.Remove(_compareNow);
                ViewHost.ColumnDefinitions.Clear();
                Grid.SetColumn(_vp, 0);
                _vp.ViewChanged -= _compare.Sync;
                _compare = null;
                _compareNow = null;
                return;
            }

            if (_compare == null)
            {
                _compare = new StudyCompareView(_vp.Cam);
                _vp.ViewChanged += _compare.Sync;
                ViewHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ViewHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(_compare, 1);
                ViewHost.Children.Add(_compare);
                Grid.SetColumn(_vp, 0);
                _compareNow = StudyCompareView.Caption(null, VerticalAlignment.Bottom);
                Grid.SetColumn(_compareNow, 0);
                ViewHost.Children.Add(_compareNow);
            }

            string Line(string which, PartStudyResult x) => Lang.T(which) + " · " + Lang.T(x.Material) + " · " + PlotPeak(x);
            ((TextBlock)_compareNow!.Child).Text = _studyResult is { } now ? Line("Şimdiki çözüm", now) : string.Empty;
            _compare.Show(ResultModel(p), new MatrixTransform3D(ModelGeometry.ToMatrix(_ed.Doc.WorldMatrix(o))), Line("Önceki çözüm", p));
        }

        /// <summary>
        /// [part-study] The part's shape changed: its result no longer fits it, so the colours come off the part (and the
        /// second view closes) until it is solved again; the panel says why.
        /// </summary>
        private void StudyGeometryChanged()
        {
            if (_studyResult == null || _studyStale || _vp.ResultModel == null) return;
            _studyStale = true;
            _vp.ResultModel = null;
            _vp.Rebuild();
            ShowCompare(false);
            _studyStatus = Lang.T("Parça değişti: sonuç artık ona uymuyor, yeniden çözün.");
            if (StudyToggle.IsChecked == true) BuildStudy();
        }

        /// <summary>The peak of the plotted quantity, in its unit (the lowest for the safety factor).</summary>
        private string PlotPeak(PartStudyResult r)
        {
            var (f, unit) = PlotUnit();
            var v = PlotValues(r);
            if (v.Length == 0) return string.Empty;
            double x = _plot == "sf" ? v.Min() : v.Max() * f;
            string name = _plot == "disp" ? "en büyük yer değiştirme" : _plot == "sf" ? "en düşük emniyet katsayısı" : "en yüksek von Mises";
            return Lang.T(name) + " " + x.ToString(x >= 100 ? "0" : "0.###", CultureInfo.CurrentCulture) + (unit.Length > 0 ? " " + unit : string.Empty);
        }

        private Model3D ResultModel(PartStudyResult r)
        {
            var (f, _) = PlotUnit();
            var (lo, hi) = Range(r);
            var values = PlotValues(r);
            double size = r.Nodes.Length == 0 ? 1 : Math.Max(1e-9, new[] { r.Nodes.Max(n => n.X) - r.Nodes.Min(n => n.X), r.Nodes.Max(n => n.Y) - r.Nodes.Min(n => n.Y), r.Nodes.Max(n => n.Z) - r.Nodes.Min(n => n.Z) }.Max());
            double scale = _deformed && r.MaxDisplacement > 0 ? 0.05 * size / r.MaxDisplacement : 0;
            var geo = new MeshGeometry3D();
            var index = new Dictionary<int, int>();
            int Vertex(int n)
            {
                if (index.TryGetValue(n, out int i)) return i;
                var p = r.Nodes[n];
                var d = new Vec3(r.Displacement[3 * n], r.Displacement[3 * n + 1], r.Displacement[3 * n + 2]) * scale;
                geo.Positions.Add(new Point3D((p.X + d.X) * 1000, (p.Y + d.Y) * 1000, (p.Z + d.Z) * 1000));
                double u = (values[n] * f - lo) / (hi - lo);
                if (_plot == "sf") u = 1 - u;   // a low safety factor is the danger: red
                geo.TextureCoordinates.Add(new Point(Math.Max(0.001, Math.Min(0.999, u)), 0.5));
                i = geo.Positions.Count - 1;
                index[n] = i;
                return i;
            }

            // each quadratic boundary triangle as its four corner triangles (a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca)
            for (int t = 0; t + 5 < r.Surface.Length; t += 6)
            {
                int a = r.Surface[t], b = r.Surface[t + 1], c = r.Surface[t + 2], ab = r.Surface[t + 3], bc = r.Surface[t + 4], ca = r.Surface[t + 5];
                if (ab < 0 || bc < 0 || ca < 0)
                {
                    geo.TriangleIndices.Add(Vertex(a));
                    geo.TriangleIndices.Add(Vertex(b));
                    geo.TriangleIndices.Add(Vertex(c));
                    continue;
                }

                foreach (var (x, y, z) in new[] { (a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca) })
                {
                    geo.TriangleIndices.Add(Vertex(x));
                    geo.TriangleIndices.Add(Vertex(y));
                    geo.TriangleIndices.Add(Vertex(z));
                }
            }

            var brush = new ImageBrush(ResultBands.Texture()) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            var material = new DiffuseMaterial(brush);
            return new GeometryModel3D(geo, material) { BackMaterial = material };
        }

        private void Results(PartStudyResult r)
        {
            _study.Children.Add(Overline(Lang.T("Sonuçlar")));
            var plots = new WrapPanel();
            foreach (var (key, name) in new[] { ("mises", "von Mises"), ("disp", "Yer değiştirme"), ("sf", "Emniyet katsayısı") })
            {
                var chip = new System.Windows.Controls.Primitives.ToggleButton { Content = Lang.T(name), IsChecked = _plot == key, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 4, 4) };
                chip.Click += (_, _) =>
                {
                    _plot = key;
                    _fixedScale = false;
                    ShowResult();
                    BuildStudy();
                };
                plots.Children.Add(chip);
            }

            _study.Children.Add(plots);
            string Stress(double pa) => (pa / 1e6).ToString(pa < 1e6 ? "0.###" : "0.#", CultureInfo.CurrentCulture) + " MPa";
            string Disp(double m) => m < 1e-3 ? (m * 1e6).ToString("0.##", CultureInfo.CurrentCulture) + " µm" : (m * 1e3).ToString("0.###", CultureInfo.CurrentCulture) + " mm";
            _study.Children.Add(Pcb.PcbForms.Caption(Lang.T($"En yüksek von Mises {Stress(r.MaxVonMises)} (%98: {Stress(r.P98)}) · en büyük yer değiştirme {Disp(r.MaxDisplacement)} · en düşük emniyet katsayısı {r.MinSafetyFactor:0.##}"), 2));
            if (r.MinSafetyFactor < 1) _study.Children.Add(Colored(Lang.T("Emniyet katsayısı 1'in altında: parça bu yükte akar (kalıcı şekil değiştirir)."), "B.Danger"));

            // the colour scale: unit, range (automatic or fixed), the bands with their values
            var unitRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            if (_plot == "mises") unitRow.Children.Add(Combo(new[] { "MPa", "kPa", "Pa" }, _stressUnit, u => { _stressUnit = u; _fixedScale = false; ShowResult(); BuildStudy(); }));
            if (_plot == "disp") unitRow.Children.Add(Combo(new[] { "mm", "µm" }, _dispUnit, u => { _dispUnit = u; _fixedScale = false; ShowResult(); BuildStudy(); }));
            var deformed = new CheckBox { Content = Lang.T("Şekil değişimi (büyütülmüş)"), IsChecked = _deformed, Margin = new Thickness(8, 4, 0, 0) };
            deformed.Click += (_, _) => { _deformed = deformed.IsChecked == true; ShowResult(); };
            unitRow.Children.Add(deformed);
            _study.Children.Add(unitRow);
            var (lo, hi) = Range(r);
            var fixedBox = new CheckBox { Content = Lang.T("Sabit ölçek"), IsChecked = _fixedScale, Margin = new Thickness(0, 6, 0, 2), ToolTip = Lang.T("Renk aralığını elle verin: çalışmalar aynı ölçekte karşılaştırılır") };
            fixedBox.Click += (_, _) =>
            {
                _fixedScale = fixedBox.IsChecked == true;
                (_scaleMin, _scaleMax) = (lo, hi);
                ShowResult();
                BuildStudy();
            };
            _study.Children.Add(fixedBox);
            if (_fixedScale)
            {
                _study.Children.Add(Row(Lang.T("En küçük"), Number(_scaleMin, v => { _scaleMin = v; ShowResult(); BuildStudy(); }, double.MinValue, double.MaxValue, true), 90));
                _study.Children.Add(Row(Lang.T("En büyük"), Number(_scaleMax, v => { _scaleMax = v; ShowResult(); BuildStudy(); }, double.MinValue, double.MaxValue, true), 90));
            }

            var (_, unit) = PlotUnit();
            var legend = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            for (int i = ResultBands.Colors.Length - 1; i >= 0; i--)
            {
                int band = _plot == "sf" ? ResultBands.Colors.Length - 1 - i : i;
                double from = lo + (hi - lo) * i / ResultBands.Colors.Length, to = lo + (hi - lo) * (i + 1) / ResultBands.Colors.Length;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 1) };
                row.Children.Add(new Border { Width = 14, Height = 12, Background = new SolidColorBrush(ResultBands.Colors[band]), Margin = new Thickness(0, 0, 6, 0) });
                row.Children.Add(new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, "{0:0.###} … {1:0.###} {2}", from, to, unit), FontSize = TypeScale.Caption, FontFamily = (FontFamily)FindResource("F.Mono") });
                legend.Children.Add(row);
            }

            _study.Children.Add(legend);

            // reliability: how far to trust it
            _study.Children.Add(Overline(Lang.T("Güvenilirlik")));
            _study.Children.Add(Pcb.PcbForms.Caption(Lang.T($"Ağ: {r.Elements} eleman (T10), eleman boyu {r.ElementSize * 1000:0.##} mm · {r.FixedNodes} düğüm sabit, {r.LoadedNodes} düğüm yüklü"), 0));
            _study.Children.Add(Pcb.PcbForms.Caption(Lang.T($"Çözücü: {r.Iterations} yineleme, bağıl artık {r.Residual:0.#e0} · toplam yük {r.TotalLoad:0.###} N"), 2));
            if (double.IsNaN(r.MeshGci)) _study.Children.Add(Pcb.PcbForms.Caption(Lang.T("Ağ yakınsaması denetlenmedi (Ağ altındaki kutu)."), 2));
            else
            {
                string extra = double.IsNaN(r.MeshExtrapolated) ? string.Empty : Lang.T($"; ağdan bağımsız değer ≈ {Stress(r.MeshExtrapolated)}");
                _study.Children.Add(Colored(Lang.T($"Ağ belirsizliği (GCI, %98'lik gerilme): %{r.MeshGci * 100:0.#}") + extra, r.MeshGci > 0.1 ? "B.Warning" : "B.TextSecondary"));
            }

            if (PartStudyRunner.ReferenceSolved)
            {
                var rf = PartStudyRunner.Reference();
                _study.Children.Add(Pcb.PcbForms.Caption(Lang.T($"Referans çözüm: uçtan 100 N yüklü 100 × 10 × 10 mm çelik konsol kiriş, bu çözücüyle {Disp(rf.Fem)}, kiriş teorisiyle (kayma dahil) {Disp(rf.Theory)}: fark %{rf.Deviation * 100:0.0}"), 2));
            }

            foreach (string a in r.Assumptions) _study.Children.Add(Pcb.PcbForms.Caption("• " + Lang.T(a), 2));
            foreach (string n in r.Notes.Skip(1)) _study.Children.Add(Pcb.PcbForms.Caption("• " + Lang.T(n), 2));

            // [validation] the solver's other reference cases, in the guide's validation chapter at its stress section
            var report = new Button
            {
                Content = Lang.T("Doğrulama raporu: gerilme analizinin teoriden sapması"),
                Style = (Style)FindResource("Btn.Ghost"),
                Foreground = (Brush)FindResource("B.Accent"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0, 2, 0, 2),
                Margin = new Thickness(0, 4, 0, 0),
            };
            report.Click += (_, _) => (Window.GetWindow(this) as MainWindow)?.ShowHelp("12-dogrulama.md#" + (Lang.Current == "tr" ? "2. Gerilme analizi (FEM, ikinci derece T10 elemanlar)" : "2. Stress analysis (FEM, quadratic T10 elements)"));
            _study.Children.Add(report);

            // the previous solve beside this one
            if (_studyPrevious is { } p)
            {
                _study.Children.Add(Overline(Lang.T("Karşılaştırma (önceki çözüm)")));
                var grid = new Grid();
                foreach (double w in new[] { 0.0, 90, 90, 60 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });
                var rows = new (string Name, string Before, string Now, double Change)[]
                {
                    ("En yüksek von Mises", Stress(p.MaxVonMises), Stress(r.MaxVonMises), r.MaxVonMises / Math.Max(1e-12, p.MaxVonMises) - 1),
                    ("%98 gerilme", Stress(p.P98), Stress(r.P98), r.P98 / Math.Max(1e-12, p.P98) - 1),
                    ("En büyük yer değiştirme", Disp(p.MaxDisplacement), Disp(r.MaxDisplacement), r.MaxDisplacement / Math.Max(1e-15, p.MaxDisplacement) - 1),
                    ("En düşük emniyet katsayısı", p.MinSafetyFactor.ToString("0.##", CultureInfo.CurrentCulture), r.MinSafetyFactor.ToString("0.##", CultureInfo.CurrentCulture), r.MinSafetyFactor / Math.Max(1e-12, p.MinSafetyFactor) - 1),
                    ("Malzeme", p.Material, r.Material, double.NaN),
                };
                void Cell(string text, int row, int col, bool head = false)
                {
                    var t = new TextBlock { Text = text, FontSize = TypeScale.Caption, Margin = new Thickness(0, 1, 6, 1), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal };
                    Grid.SetRow(t, row);
                    Grid.SetColumn(t, col);
                    grid.Children.Add(t);
                }

                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Cell(string.Empty, 0, 0, true);
                Cell(Lang.T("Önceki"), 0, 1, true);
                Cell(Lang.T("Şimdiki"), 0, 2, true);
                Cell(Lang.T("Fark"), 0, 3, true);
                for (int i = 0; i < rows.Length; i++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Cell(Lang.T(rows[i].Name), i + 1, 0);
                    Cell(rows[i].Before, i + 1, 1);
                    Cell(rows[i].Now, i + 1, 2);
                    Cell(double.IsNaN(rows[i].Change) ? string.Empty : (rows[i].Change >= 0 ? "+" : "") + (rows[i].Change * 100).ToString("0.#", CultureInfo.CurrentCulture) + " %", i + 1, 3);
                }

                _study.Children.Add(grid);
                var side = new CheckBox { Content = Lang.T("İki görünümde yan yana"), IsChecked = _sideBySide, Margin = new Thickness(0, 8, 0, 0), ToolTip = Lang.T("Şimdiki çözüm solda, önceki sağda: aynı kamera ve aynı renk ölçeği") };
                side.Click += (_, _) =>
                {
                    _sideBySide = side.IsChecked == true;
                    ShowResult();
                    if (!_sideBySide) ShowCompare(false);
                    BuildStudy();
                };
                _study.Children.Add(side);
            }
        }

        /// <summary>--snapshot "study": a bar held at one end and pushed down at the other, solved, its stress on view.</summary>
        internal void StudyForSnapshot(string plot, bool sideBySide = false)
        {
            _sideBySide = sideBySide;
            var bar = PrimitiveBuilder.Box(100, 10, 10);
            var o = new ModelObject { Name = Lang.T("Çubuk"), Material = "ÇELİK" };
            o.SetMesh(bar);
            _ed.Doc.Add(o);
            _ed.Select(o.Id);
            int root = Enumerable.Range(0, bar.Faces.Count).First(f => bar.FaceNormal(f).X < -0.9), tip = Enumerable.Range(0, bar.Faces.Count).First(f => bar.FaceNormal(f).X > 0.9);
            var study = new PartStudy { ObjectId = o.Id, Material = "ÇELİK", ElementSize = 2.5, MeshCheck = true };
            study.Fixtures.Add(new StudyFixture { Faces = { root } });
            study.Loads.Add(new StudyLoad { Faces = { tip }, Magnitude = 100 });
            _ed.Doc.Studies = new List<PartStudy> { study };
            _studyPrevious = PartStudyRunner.Run(bar, new PartStudy { Material = "ALÜMİNYUM", ElementSize = 2.5, Fixtures = study.Clone().Fixtures, Loads = study.Clone().Loads });
            _studyResult = PartStudyRunner.Run(bar, study);
            PartStudyRunner.Reference();
            _studyResultFor = o.Id;
            _plot = plot;
            StudyToggle.IsChecked = true;
            _dock?.SetVisible("library", false);   // the study's column to itself, results in view
            _dock?.SetVisible("study", true);
            _vp.Rebuild();
            ShowResult();
            BuildStudy();
        }
    }
}
