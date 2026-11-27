using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [analysis] SolidWorks' Evaluate analyses in the part editor, as colours on the faces: Draft Analysis (which faces
    /// come out of a mould along the pull direction, which need draft), Curvature (radius bands) and Symmetry Check
    /// (faces with no mirror image across a plane through the bodies' middle). Display only; the legend gives each
    /// colour's area.
    /// </summary>
    public partial class ModelPage
    {
        private enum AnalysisMode
        {
            Draft,
            Curvature,
            Symmetry
        }

        private AnalysisMode _analysisMode = AnalysisMode.Draft;
        private double _draftAngle = 1;
        private double[] _analysisArea = Array.Empty<double>();

        private static readonly Color[] DraftColors = { Color.FromRgb(0x3F, 0xB9, 0x50), Color.FromRgb(0xE5, 0x53, 0x4B), Color.FromRgb(0xD4, 0xA7, 0x2C) };
        private static readonly Color[] CurvatureColors =
        {
            Color.FromRgb(0x7D, 0x85, 0x90), Color.FromRgb(0x3F, 0xB9, 0x50), Color.FromRgb(0xD4, 0xA7, 0x2C), Color.FromRgb(0xF0, 0x88, 0x3E), Color.FromRgb(0xE5, 0x53, 0x4B)
        };

        private static readonly Color[] SymmetryColors = { Color.FromRgb(0x3F, 0xB9, 0x50), Color.FromRgb(0xE5, 0x53, 0x4B) };

        private void OnAnalysis(object sender, RoutedEventArgs e)
        {
            if (AnalysisToggle.IsChecked == true) ShowAnalysis(_analysisMode);
            else CloseAnalysis();
        }

        private void ShowAnalysis(AnalysisMode mode)
        {
            _analysisMode = mode;
            AnalysisToggle.IsChecked = true;
            AnalysisBar.Visibility = Visibility.Visible;
            AnalysisDraft.IsChecked = mode == AnalysisMode.Draft;
            AnalysisCurvature.IsChecked = mode == AnalysisMode.Curvature;
            AnalysisSymmetry.IsChecked = mode == AnalysisMode.Symmetry;
            AnalysisAxisPanel.Visibility = mode == AnalysisMode.Curvature ? Visibility.Collapsed : Visibility.Visible;
            AnalysisAnglePanel.Visibility = mode == AnalysisMode.Draft ? Visibility.Visible : Visibility.Collapsed;
            AnalysisFlip.Visibility = mode == AnalysisMode.Draft ? Visibility.Visible : Visibility.Collapsed;
            AnalysisAxisLabel.Text = Lang.T(mode == AnalysisMode.Draft ? "Çekme yönü" : "Düzlem");
            AnalysisAngle.Text = _draftAngle.ToString("0.##", CultureInfo.CurrentCulture);
            UpdateAnalysis();
        }

        private Vec3 AnalysisAxis()
        {
            var axis = AnalysisX.IsChecked == true ? Vec3.UnitX : AnalysisY.IsChecked == true ? Vec3.UnitY : Vec3.UnitZ;
            return AnalysisFlip.IsChecked == true && _analysisMode == AnalysisMode.Draft ? -axis : axis;
        }

        /// <summary>The middle of the visible bodies (the symmetry plane goes through it).</summary>
        private Vec3 BodiesMiddle()
        {
            var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = -lo;
            foreach (var o in _ed.Doc.Objects.Where(o => o.Visible && o.Reference == null))
            {
                var w = _ed.Doc.WorldMatrix(o);
                foreach (var v in o.LocalMesh().Vertices)
                {
                    var p = w.Apply(v);
                    lo = Vec3.Min(lo, p);
                    hi = Vec3.Max(hi, p);
                }
            }

            return lo.X > hi.X ? Vec3.Zero : (lo + hi) * 0.5;
        }

        private void UpdateAnalysis()
        {
            var axis = AnalysisAxis();
            var middle = BodiesMiddle();
            var colors = _analysisMode switch { AnalysisMode.Draft => DraftColors, AnalysisMode.Curvature => CurvatureColors, _ => SymmetryColors };
            _analysisArea = new double[colors.Length];
            _vp.AnalysisColors = colors;
            _vp.Analysis = (o, mesh, world) =>
            {
                var c = _analysisMode switch
                {
                    AnalysisMode.Draft => FaceAnalysis.Draft(mesh, world, axis, _draftAngle),
                    AnalysisMode.Curvature => FaceAnalysis.Curvature(mesh, world),
                    _ => FaceAnalysis.Symmetry(mesh, world, middle, axis)
                };
                for (int i = 0; i < c.Area.Length && i < _analysisArea.Length; i++) _analysisArea[i] += c.Area[i];
                return c;
            };
            _vp.Rebuild();
            FillAnalysisLegend(colors);
        }

        private void FillAnalysisLegend(Color[] colors)
        {
            string[] names = _analysisMode switch
            {
                AnalysisMode.Draft => new[] { "Pozitif draft", "Negatif draft", "Draft gerekli" },
                AnalysisMode.Curvature => new[] { "Düz", "R > 50 mm", "R 10…50 mm", "R 2…10 mm", "R < 2 mm" },
                _ => new[] { "Simetrik", "Simetrik değil" }
            };
            AnalysisLegend.Children.Clear();
            double total = _analysisArea.Sum();
            for (int i = 0; i < colors.Length && i < names.Length; i++)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
                item.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(colors[i]), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                double a = i < _analysisArea.Length ? _analysisArea[i] : 0;
                string share = total > 0 ? " · %" + (a / total * 100).ToString("0", CultureInfo.CurrentCulture) : string.Empty;
                var text = new TextBlock { Text = Lang.T(names[i]) + " · " + a.ToString(a < 100 ? "0.#" : "N0", CultureInfo.CurrentCulture) + " mm²" + share, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
                text.SetResourceReference(TextBlock.ForegroundProperty, a > 0 ? "B.Text" : "B.TextMuted");
                item.Children.Add(text);
                AnalysisLegend.Children.Add(item);
            }

            string what = _analysisMode switch
            {
                AnalysisMode.Draft => Lang.T($"Draft analizi: çekme {AxisName()} · en az {_draftAngle.ToString("0.##", CultureInfo.CurrentCulture)}° (yalnız görüntü)"),
                AnalysisMode.Curvature => Lang.T("Eğrilik analizi: yüzler eğrilik yarıçapına göre (yalnız görüntü)"),
                _ => Lang.T($"Simetri denetimi: gövdelerin ortasından geçen {AxisName()} düzlemine göre (yalnız görüntü)")
            };
            _ed.Status(what);
        }

        private string AxisName()
        {
            var a = AnalysisAxis();
            string s = a.X != 0 ? "X" : a.Y != 0 ? "Y" : "Z";
            return (a.X + a.Y + a.Z < 0 ? "−" : "+") + s;
        }

        private void CloseAnalysis()
        {
            _vp.Analysis = null;
            AnalysisToggle.IsChecked = false;
            AnalysisBar.Visibility = Visibility.Collapsed;
            _vp.Rebuild();
        }

        private void OnAnalysisMode(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out AnalysisMode mode)) ShowAnalysis(mode);
        }

        private void OnAnalysisAxis(object sender, RoutedEventArgs e)
        {
            if (_vp.Analysis != null) UpdateAnalysis();
        }

        private void ApplyDraftAngle()
        {
            if (double.TryParse(AnalysisAngle.Text.Replace('°', ' ').Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0 && v < 90)
                _draftAngle = v;
            AnalysisAngle.Text = _draftAngle.ToString("0.##", CultureInfo.CurrentCulture);
            if (_vp.Analysis != null) UpdateAnalysis();
        }

        private void OnAnalysisAngleKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ApplyDraftAngle();
            e.Handled = true;
        }

        private void OnAnalysisAngleLost(object sender, KeyboardFocusChangedEventArgs e) => ApplyDraftAngle();

        private void OnAnalysisClose(object sender, RoutedEventArgs e) => CloseAnalysis();
    }
}
