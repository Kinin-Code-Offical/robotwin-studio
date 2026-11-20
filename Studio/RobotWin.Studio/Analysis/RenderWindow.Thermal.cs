using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Analysis;

namespace RobotWin.Studio.Analysis
{
    /// <summary>
    /// [thermal] Temperature mode as a thermal camera: every part's own 3D model is coloured point by
    /// point — a board by its computed heat map, a lumped part by its recorded temperature — in the
    /// iron palette, on one fixed scale for the whole render. Hovering a part shows the temperature at
    /// that exact point.
    /// </summary>
    public sealed partial class RenderWindow
    {
        private readonly TextBlock _hover = new TextBlock { Foreground = Brushes.White, FontSize = 12.5, Margin = new Thickness(7, 4, 7, 4) };
        private Border? _hoverBox;
        private int _thermalFrame;
        private double _thermalTime;

        private void AttachThermalHover(Grid view)
        {
            _hoverBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x10, 0x10, 0x14)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x37)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = _hover,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed
            };
            view.Children.Add(_hoverBox);
            view.MouseMove += (_, e) => ShowHoverTemperature(e.GetPosition(_scene.View3D), e.GetPosition(view));
            view.MouseLeave += (_, _) => _hoverBox.Visibility = Visibility.Collapsed;
        }

        private void ShowHoverTemperature(Point inView, Point inGrid)
        {
            if (_hoverBox == null) return;
            if (_mode.SelectedIndex != 3 || _result == null || !_scene.ThermalCamera || _scene.SurfacePointAt(inView) is not { } hit ||
                SurfaceTemperature(hit.ObjectId, hit.Local) is not double t)
            {
                _hoverBox.Visibility = Visibility.Collapsed;
                return;
            }

            var o = _world.Find(hit.ObjectId);
            bool map = _result.Thermal.Any(th => string.Equals(th.ObjectId, hit.ObjectId, StringComparison.OrdinalIgnoreCase) && th.Frames.Count > 0);
            _hover.Text = $"{o?.Name ?? hit.ObjectId}: {t:0.0} °C" + (map ? $"  ({hit.Local.X * 1000:0}, {hit.Local.Y * 1000:0}) mm" : "  (parçanın sıcaklığı)");
            _hoverBox.Margin = new Thickness(inGrid.X + 16, inGrid.Y + 12, 0, 0);
            _hoverBox.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Temperature at a point of an object (its frame) at the shown time: bilinear in the object's
        /// thermal grid when it has one (centred on its body), else its recorded lumped temperature.
        /// </summary>
        private double? SurfaceTemperature(string id, Point3D p)
        {
            if (_result == null) return null;
            var th = _result.Thermal.FirstOrDefault(x => string.Equals(x.ObjectId, id, StringComparison.OrdinalIgnoreCase) && x.Frames.Count > 0);
            if (th != null)
            {
                int f = 0;
                for (int k = 0; k < th.Times.Count; k++)
                    if (th.Times[k] <= _thermalTime + 1e-9) f = k;
                var field = th.Frames[Math.Min(f, th.Frames.Count - 1)];
                double gx = (p.X - th.BodyOffset.X + th.Nx * th.Dx / 2) / th.Dx - 0.5;
                double gy = (p.Y - th.BodyOffset.Y + th.Ny * th.Dy / 2) / th.Dy - 0.5;
                int kz = th.Nz <= 1 ? 0 : Math.Clamp((int)Math.Floor((p.Z - th.BodyOffset.Z + th.Nz * th.Dz / 2) / th.Dz), 0, th.Nz - 1);
                gx = Math.Clamp(gx, 0, th.Nx - 1);
                gy = Math.Clamp(gy, 0, th.Ny - 1);
                int i0 = (int)Math.Floor(gx), j0 = (int)Math.Floor(gy), i1 = Math.Min(i0 + 1, th.Nx - 1), j1 = Math.Min(j0 + 1, th.Ny - 1);
                double fx = gx - i0, fy = gy - j0;
                double V(int i, int j) => field[(kz * th.Ny + j) * th.Nx + i];
                double v = (V(i0, j0) * (1 - fx) + V(i1, j0) * fx) * (1 - fy) + (V(i0, j1) * (1 - fx) + V(i1, j1) * fx) * fy;
                if (double.IsFinite(v)) return v;
            }

            if (_result.Channels.Find("pose/" + id + "/temp") is { } c && _thermalFrame < c.Count && double.IsFinite(c[_thermalFrame])) return c[_thermalFrame];
            return null;
        }

        private void ThermalCamera(bool on, int i, double t, StringBuilder legend)
        {
            if (!on || _result == null)
            {
                _scene.SetThermalCamera(null, 0, 0);
                return;
            }

            _thermalFrame = i;
            _thermalTime = t;
            // One fixed scale for the whole render, as a camera with a locked range: colours compare across time.
            double low = _world.Environment.AmbientC, high = low + 1;
            foreach (var o in _world.Objects)
                if (_result.Channels.Find("pose/" + o.Id + "/temp") is { } c)
                    high = Math.Max(high, c.ToArray().Where(double.IsFinite).DefaultIfEmpty(low).Max());
            foreach (var th in _result.Thermal)
                if (double.IsFinite(th.PeakTemperature)) high = Math.Max(high, th.PeakTemperature);

            _scene.SetThermalCamera(SurfaceTemperature, low, high);
            string hottest = string.Empty;
            double hot = double.MinValue;
            foreach (var o in _world.Objects)
            {
                if (_result.Channels.Find("pose/" + o.Id + "/temp") is not { } c || i >= c.Count || !(c[i] > hot)) continue;
                hot = c[i];
                var mat = RobotTwin.CoreSim.Analysis.Fem.StructuralMaterial.For(o.Material);
                hottest = $"{o.Name} {c[i]:0.0} °C ({mat.Name}: {mat.StateAt(c[i])})";
            }

            legend.AppendLine($"Isı kamerası (demir paleti): siyah {low:0} °C → kırmızı → sarı → beyaz {high:0.0} °C");
            legend.AppendLine("İmleci bir parçanın üstüne getirin: o noktanın sıcaklığı görünür");
            if (hottest.Length > 0) legend.AppendLine("En sıcak: " + hottest);
        }
    }
}
