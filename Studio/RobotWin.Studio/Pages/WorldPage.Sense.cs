using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;
using RobotWin.Studio.Examples;
using RobotWin.Studio.Simulation;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [sense] Sensors on the world page: the "Sensörler" dock panel (live camera feeds, LiDAR polar
    /// plot, line sensor channels, IMU / GPS / compass / encoder readouts), the light / track / GPS
    /// settings in the Ortam tab, and the Unity-style "Sahne + Oyun" layout (editor scene view and
    /// game view side by side or as tabs). WorldPage.xaml.cs only calls InitSense,
    /// SenseEnvironmentPalette and the snapshot hooks.
    /// </summary>
    public partial class WorldPage
    {
        private RadioButton? _dockSensors;
        private ScrollViewer? _sensorsPanel;
        private WrapPanel? _sensorCards;
        private readonly Dictionary<string, SensorCard> _cards = new Dictionary<string, SensorCard>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastSensorPanel = DateTime.MinValue;
        private ToggleButton? _studioToggle;
        private GameView? _game;
        private Grid? _viewHost;
        private Grid? _viewContent;
        private Border? _studioBar;
        private GridSplitter? _viewSplitter;
        private bool _studio, _studioTabbed, _studioAuto, _studioPanels;
        private string _studioTab = "scene";
        private readonly List<RadioButton> _studioTabs = new List<RadioButton>();
        private CheckBox? _studioTabbedBox, _studioPanelsBox;
        private TextBlock? _studioHint;
        private readonly List<(ColumnDefinition Column, GridLength Width, double Min)> _savedColumns = new List<(ColumnDefinition, GridLength, double)>();
        private (GridLength Height, double Min)? _savedDock;
        private string[] _snapshotModes = Array.Empty<string>();

        private void InitSense()
        {
            _session.Updated += (_, _) =>
            {
                Scene.RenderDueCameras();
                if ((DateTime.Now - _lastSensorPanel).TotalMilliseconds < 125) return;
                _lastSensorPanel = DateTime.Now;
                if (_dockSensors?.IsChecked == true) UpdateSensorsPanel();
            };
            _session.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_session.State == SimState.Stopped && _dockSensors?.IsChecked == true) UpdateSensorsPanel();
            }));
            W.WorldChanged += (_, _) => _game?.RefreshSources();
            BuildSensorsDock();
            BuildViewHost();
            PreviewKeyDown += (_, e) =>
            {
                if (e.OriginalSource is TextBox || Keyboard.Modifiers != ModifierKeys.None) return;
                if (e.Key == Key.G)
                {
                    SetStudio(!_studio);
                    e.Handled = true;
                }
            };
        }

        // ================================================================ "Sensörler" dock tab

        private void BuildSensorsDock()
        {
            if (DockRf.Parent is not Panel tabs || LogList.Parent is not Grid host) return;
            _dockSensors = new RadioButton
            {
                Style = (Style)FindResource("Chip"), Content = "Sensörler", GroupName = "dock", Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Kameraların canlı görüntüsü, LiDAR taraması, çizgi sensörü kanalları, IMU / GPS / pusula / enkoder"
            };
            tabs.Children.Add(_dockSensors);
            _sensorCards = new WrapPanel { Orientation = Orientation.Horizontal };
            _sensorsPanel = new ScrollViewer { Visibility = Visibility.Collapsed, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _sensorCards };
            host.Children.Add(_sensorsPanel);
            _dockSensors.Checked += (_, _) =>
            {
                LogList.Visibility = Visibility.Collapsed;
                SerialPanel.Visibility = Visibility.Collapsed;
                CablePanel.Visibility = Visibility.Collapsed;
                RfPanel.Visibility = Visibility.Collapsed;
                _sensorsPanel.Visibility = Visibility.Visible;
                if (DockRow.Height.Value < 260) DockRow.Height = new GridLength(280);
                UpdateSensorsPanel();
            };
            _dockSensors.Unchecked += (_, _) => _sensorsPanel.Visibility = Visibility.Collapsed;
        }

        private void UpdateSensorsPanel()
        {
            if (_sensorCards == null) return;
            var sensing = _session.Sensing;
            var readings = sensing?.Sensors.All.OrderBy(r => r.Kind == "camera" ? 0 : r.Kind == "lidar" ? 1 : r.Kind == "line" ? 2 : 3).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList()
                           ?? new List<SensorReading>();
            if (readings.Count == 0)
            {
                _cards.Clear();
                _sensorCards.Children.Clear();
                _sensorCards.Children.Add(Note(_session.State == SimState.Stopped
                    ? "Dünyayı çalıştırınca kameraların görüntüsü, LiDAR taraması, çizgi sensörlerinin her kanalı, IMU, GPS, pusula ve enkoder okumaları burada canlı görünür. Sensörleri Parçalar sekmesinde 'Sensör ·' başlıkları altında bulabilirsiniz."
                    : "Bu dünyada ölçüm yapan sensör yok (çizgi sensörü, kamera, LiDAR, GPS, IMU, pusula, enkoderli motor)."));
                return;
            }

            var ids = readings.Select(r => r.ObjectId).ToList();
            if (!ids.SequenceEqual(_cards.Keys.ToList()) || _sensorCards.Children.Count != _cards.Count)
            {
                _cards.Clear();
                _sensorCards.Children.Clear();
                foreach (var r in readings)
                {
                    var card = new SensorCard(this, r);
                    _cards[r.ObjectId] = card;
                    _sensorCards.Children.Add(card.Root);
                }
            }

            foreach (var r in readings)
            {
                if (_cards.TryGetValue(r.ObjectId, out var c)) c.Update(r, sensing!);
            }
        }

        /// <summary>One sensor's live readout.</summary>
        private sealed class SensorCard
        {
            private readonly WorldPage _page;
            private readonly TextBlock _title = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            private readonly TextBlock _text = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            private readonly Image _image = new Image { Stretch = Stretch.Uniform, Height = 150, HorizontalAlignment = HorizontalAlignment.Left };
            private readonly StackPanel _channels = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            private WriteableBitmap? _bitmap;
            private long _frame = -1;

            public SensorCard(WorldPage page, SensorReading r)
            {
                _page = page;
                _text.FontFamily = (FontFamily)page.FindResource("F.Mono");
                _text.Foreground = (Brush)page.FindResource("B.TextSecondary");
                var sp = new StackPanel();
                var head = new DockPanel();
                if (r is CameraReading)
                {
                    var open = new Button { Content = "Oyun görünümünde", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0), FontSize = 11 };
                    string id = r.ObjectId;
                    open.Click += (_, _) => page.ShowInGame(id, true);
                    var win = new Button { Content = "Ayrı pencere", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0), FontSize = 11 };
                    win.Click += (_, _) => page.OpenCameraWindow(id);
                    DockPanel.SetDock(win, Dock.Right);
                    DockPanel.SetDock(open, Dock.Right);
                    head.Children.Add(win);
                    head.Children.Add(open);
                }

                head.Children.Add(_title);
                sp.Children.Add(head);
                if (r is CameraReading || r is LidarReading)
                {
                    RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
                    _image.Margin = new Thickness(0, 6, 0, 6);
                    sp.Children.Add(_image);
                }

                if (r is LineSensorReading) sp.Children.Add(_channels);
                sp.Children.Add(_text);
                Root = new Border
                {
                    Child = sp, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1), BorderBrush = (Brush)page.FindResource("B.Border"), Background = (Brush)page.FindResource("B.Hover"),
                    Width = r is CameraReading ? 300 : r is LidarReading ? 250 : r is LineSensorReading ? 280 : 230
                };
            }

            public Border Root { get; }

            public void Update(SensorReading r, WorldSensing sensing)
            {
                var c = CultureInfo.InvariantCulture;
                _title.Text = r.Name + (r.Powered ? string.Empty : " · güç yok");
                switch (r)
                {
                    case CameraReading cam:
                        if (cam.FrameIndex != _frame && cam.Bgra.Length >= cam.Width * cam.Height * 4)
                        {
                            _frame = cam.FrameIndex;
                            if (_bitmap == null || _bitmap.PixelWidth != cam.Width || _bitmap.PixelHeight != cam.Height)
                            {
                                _bitmap = new WriteableBitmap(cam.Width, cam.Height, 96, 96, PixelFormats.Bgr32, null);
                                _image.Source = _bitmap;
                                _image.Width = 280;
                                _image.Height = 280.0 * cam.Height / cam.Width;
                            }

                            _bitmap.WritePixels(new Int32Rect(0, 0, cam.Width, cam.Height), cam.Bgra, cam.Width * 4, 0);
                        }

                        _text.Text = string.Format(c, "{0} · {1}×{2} {3}\nkare hızı {4:0.#} / {5:0.#} fps (üretilen / sensör)\npozlama {6:0.###} ms · kazanç {7:0.0}× · ışık {8:0} lx\nkare {9} · {10:0.#} kB/kare",
                            cam.Model, cam.Width, cam.Height, cam.Format, cam.AchievedFps, cam.TargetFps, cam.ExposureMs, cam.Gain, sensing.Environment.AmbientLux, cam.FrameIndex, cam.FrameBytes / 1024.0);
                        break;
                    case LidarReading lidar:
                        _image.Source = Polar(lidar);
                        _image.Width = 150;
                        int n = lidar.Scan.Ranges.Length, valid = lidar.Scan.Valid;
                        _text.Text = string.Format(c, "{0}\n{1:0.#} Hz · {2} ölçüm/tur · {3} kanal\ndönüş %{4:0} · en yakın {5}",
                            lidar.Model, lidar.Scan.ScanRate, n / Math.Max(1, lidar.Channels), lidar.Channels, n == 0 ? 0 : 100.0 * valid / n,
                            valid == 0 ? "—" : lidar.Scan.Ranges.Where(x => !float.IsNaN(x)).Min().ToString("0.000 m", c));
                        break;
                    case LineSensorReading line:
                        Channels(line);
                        var sb = new System.Text.StringBuilder();
                        sb.Append(line.Model).Append(" · yükseklik ").Append(line.Channels.Length > 0 && !double.IsInfinity(line.Channels[0].Distance) ? (line.Channels[0].Distance * 1000).ToString("0.0", c) + " mm" : "—");
                        foreach (var (ch, i) in line.Channels.Select((x, i) => (x, i)))
                        {
                            sb.Append('\n').Append(string.Format(c, "{0}: R {1:0.00} sinyal {2:0.00} ({3:0.00}+{4:0.00}+{5:0.00}) {6:0.00} V{7}",
                                i + 1, ch.Reflectance, ch.Signal, ch.Diffuse, ch.Specular, ch.Ambient, ch.Voltage, ch.DigitalLow == null ? string.Empty : ch.DigitalLow == true ? " BEYAZ" : " SİYAH"));
                        }

                        sb.Append("\n(sinyal = yayınık + parlama + ortam IR)");
                        _text.Text = sb.ToString();
                        break;
                    case ImuReading imu:
                        _text.Text = string.Format(c, "ivme  {0,7:0.00} {1,7:0.00} {2,7:0.00} m/s²\njiro  {3,7:0.000} {4,7:0.000} {5,7:0.000} rad/s\ngerçek ivme z {6:0.00} · jiro z {7:0.000}",
                            imu.Accel.X, imu.Accel.Y, imu.Accel.Z, imu.Gyro.X, imu.Gyro.Y, imu.Gyro.Z, imu.TrueAccel.Z, imu.TrueGyro.Z);
                        break;
                    case GpsReading gps:
                        var f = gps.Fix;
                        _text.Text = f.Valid
                            ? string.Format(c, "{0:0.000000}° {1:0.000000}°\nirtifa {2:0.0} m · {3} uydu · HDOP {4:0.0}\nhız {5:0.00} m/s · hata {6:0.00} m",
                                f.Latitude, f.Longitude, f.Altitude, f.Satellites, f.Hdop, f.SpeedMps, Math.Sqrt(f.ErrorEast * f.ErrorEast + f.ErrorNorth * f.ErrorNorth))
                            : string.Format(c, "Konum yok (uydu aranıyor) · {0} uydu\nNMEA: $GPRMC ... V", f.Satellites);
                        break;
                    case CompassReading mag:
                        _text.Text = string.Format(c, "yön {0:0.0}° (manyetik)\nalan {1:0.000} {2:0.000} {3:0.000} G", mag.HeadingDeg, mag.Field.X, mag.Field.Y, mag.Field.Z);
                        break;
                    case EncoderReading enc:
                        _text.Text = string.Format(c, "sayım {0} · {1:0} sayım/tur\nçıkış {2:0.0} rpm · A={3} B={4}", enc.Count, enc.CountsPerRevolution, enc.OutputRpm, enc.A ? 1 : 0, enc.B ? 1 : 0);
                        break;
                }
            }

            private void Channels(LineSensorReading line)
            {
                if (_channels.Children.Count != line.Channels.Length)
                {
                    _channels.Children.Clear();
                    for (int i = 0; i < line.Channels.Length; i++)
                    {
                        var g = new Grid { Width = 26, Height = 46, Margin = new Thickness(0, 0, 4, 0), Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1D)) };
                        g.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = 0 });
                        g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) });
                        _channels.Children.Add(g);
                    }
                }

                for (int i = 0; i < line.Channels.Length; i++)
                {
                    var ch = line.Channels[i];
                    var g = (Grid)_channels.Children[i];
                    var bar = (Border)g.Children[0];
                    double s = Math.Max(0, Math.Min(1, ch.Signal));
                    bar.Height = 4 + 40 * s;
                    byte v = (byte)(0x30 + 0xC0 * s);
                    bar.Background = new SolidColorBrush(Color.FromRgb(v, v, v));
                    ((System.Windows.Shapes.Ellipse)g.Children[1]).Fill = ch.DigitalLow == null ? Brushes.Transparent
                        : ch.DigitalLow == true ? new SolidColorBrush(Color.FromRgb(0x3C, 0xD0, 0x64)) : new SolidColorBrush(Color.FromRgb(0xF2, 0xA3, 0x3B));
                }
            }

            /// <summary>Top view of a scan around the sensor (rings every metre, forward up).</summary>
            private static ImageSource Polar(LidarReading lidar)
            {
                var dg = new DrawingGroup();
                const double size = 150;
                double max = Math.Min(6, Math.Max(1, lidar.Scan.Ranges.Where(x => !float.IsNaN(x)).DefaultIfEmpty(1).Max()));
                double k = size / 2 / max;
                using (var dc = dg.Open())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x12)), null, new Rect(0, 0, size, size));
                    var ring = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)), 1);
                    for (int m = 1; m <= (int)max; m++) dc.DrawEllipse(null, ring, new Point(size / 2, size / 2), m * k, m * k);
                    dc.DrawLine(ring, new Point(size / 2, size / 2), new Point(size / 2, 4));
                    var dot = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
                    var s = lidar.Scan;
                    for (int i = 0; i < s.Ranges.Length; i++)
                    {
                        float r = s.Ranges[i];
                        if (float.IsNaN(r) || Math.Abs(s.ElevationsDeg.Length > i ? s.ElevationsDeg[i] : 0) > 1.5) continue;
                        // Clockwise angle from the front; forward is up on the plot.
                        double a = s.AnglesDeg[i] * Math.PI / 180;
                        double x = size / 2 + Math.Sin(a) * r * k, y = size / 2 - Math.Cos(a) * r * k;
                        dc.DrawRectangle(dot, null, new Rect(x - 1, y - 1, 2, 2));
                    }

                    dc.DrawEllipse(Brushes.White, null, new Point(size / 2, size / 2), 2.5, 2.5);
                }

                dg.Freeze();
                return new DrawingImage(dg);
            }
        }

        /// <summary>A camera's sensor output in its own window (pixel-exact, scalable).</summary>
        private void OpenCameraWindow(string cameraId)
        {
            var image = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            var info = new TextBlock { Margin = new Thickness(8, 4, 8, 6), Foreground = Brushes.White, FontSize = 11.5 };
            var dock = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0D)) };
            DockPanel.SetDock(info, Dock.Bottom);
            dock.Children.Add(info);
            dock.Children.Add(image);
            var name = World?.Find(cameraId)?.Name ?? cameraId;
            var win = new Window { Title = "Kamera: " + name, Width = 680, Height = 560, Content = dock, Owner = Window.GetWindow(this), Background = dock.Background };
            WriteableBitmap? bmp = null;
            long shown = -1;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (_, _) =>
            {
                var r = _session.Sensing?.Sensors.Get<CameraReading>(cameraId);
                if (r == null)
                {
                    info.Text = "Simülasyon çalışmıyor";
                    return;
                }

                if (r.FrameIndex != shown && r.Bgra.Length >= r.Width * r.Height * 4)
                {
                    shown = r.FrameIndex;
                    if (bmp == null || bmp.PixelWidth != r.Width || bmp.PixelHeight != r.Height)
                    {
                        bmp = new WriteableBitmap(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null);
                        image.Source = bmp;
                    }

                    bmp.WritePixels(new Int32Rect(0, 0, r.Width, r.Height), r.Bgra, r.Width * 4, 0);
                }

                info.Text = string.Format(CultureInfo.InvariantCulture, "{0} · {1}×{2} {3} · {4:0.#}/{5:0.#} fps · pozlama {6:0.###} ms · kazanç {7:0.0}× · kare {8}",
                    r.Model, r.Width, r.Height, r.Format, r.AchievedFps, r.TargetFps, r.ExposureMs, r.Gain, r.FrameIndex);
            };
            win.Closed += (_, _) => timer.Stop();
            timer.Start();
            win.Show();
        }

        // ================================================================ Scene + Game layout

        private void BuildViewHost()
        {
            if (Scene.Parent is not Grid main) return;
            int column = Grid.GetColumn(Scene);
            main.Children.Remove(Scene);
            _viewHost = new Grid();
            _viewHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _viewHost.RowDefinitions.Add(new RowDefinition());
            Grid.SetColumn(_viewHost, column);
            main.Children.Add(_viewHost);

            _studioBar = new Border
            {
                Visibility = Visibility.Collapsed, Background = (Brush)FindResource("B.Base"), BorderBrush = (Brush)FindResource("B.Border"),
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 4, 8, 4)
            };
            _viewHost.Children.Add(_studioBar);

            _viewContent = new Grid();
            _viewContent.ColumnDefinitions.Add(new ColumnDefinition());
            _viewContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            _viewContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            Grid.SetRow(_viewContent, 1);
            _viewHost.Children.Add(_viewContent);
            Grid.SetColumn(Scene, 0);   // it kept Grid.Column=2 from the page's main grid
            _viewContent.Children.Add(Scene);
            _viewSplitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = (Brush)FindResource("B.Border"), Visibility = Visibility.Collapsed, ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(_viewSplitter, 1);
            _viewContent.Children.Add(_viewSplitter);
            _game = new GameView { Scene = Scene, Session = _session, Visibility = Visibility.Collapsed };
            Grid.SetColumn(_game, 2);
            _viewContent.Children.Add(_game);
            BuildStudioBar();

            // Toolbar toggle next to "follow".
            if (FollowToggle.Parent is Panel right)
            {
                _studioToggle = new ToggleButton
                {
                    Style = (Style)FindResource("Toggle.Tool"), ToolTip = "Sahne + Oyun görünümü (G): düzenleyici sahnesi ve seçili kameranın gördüğü, ikisi birden canlı",
                    Content = new Theme.Icon { Data = (Geometry)FindResource("I.Split"), Size = 16 }
                };
                _studioToggle.Click += (_, _) => SetStudio(_studioToggle.IsChecked == true);
                right.Children.Insert(right.Children.IndexOf(FollowToggle), _studioToggle);
            }
        }

        private void BuildStudioBar()
        {
            var row = new DockPanel();
            var close = new Button { Style = (Style)FindResource("Btn.Icon"), ToolTip = "Sahne + Oyun görünümünü kapat (G)", Content = new Theme.Icon { Data = (Geometry)FindResource("I.Close"), Size = 12 } };
            close.Click += (_, _) => SetStudio(false);
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
            var panels = _studioPanelsBox = new CheckBox { Content = "Paneller", Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Palet, denetçi ve alt paneli göster" };
            panels.Click += (_, _) =>
            {
                _studioPanels = panels.IsChecked == true;
                ApplyStudioPanels();
            };
            DockPanel.SetDock(panels, Dock.Right);
            row.Children.Add(panels);
            var tabbed = _studioTabbedBox = new CheckBox { Content = "Sekmeli", Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Sahne ve Oyun yan yana yerine sekme olarak" };
            tabbed.Click += (_, _) =>
            {
                _studioTabbed = tabbed.IsChecked == true;
                ApplyStudioLayout();
            };
            DockPanel.SetDock(tabbed, Dock.Right);
            row.Children.Add(tabbed);

            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (key, label) in new[] { ("scene", "Sahne"), ("game", "Oyun") })
            {
                var b = new RadioButton { Style = (Style)FindResource("Chip"), Content = label, GroupName = "studio-tab", Margin = new Thickness(0, 0, 4, 0), IsChecked = key == _studioTab, Tag = key };
                b.Click += (_, _) =>
                {
                    _studioTab = (string)b.Tag;
                    ApplyStudioLayout();
                };
                tabs.Children.Add(b);
                _studioTabs.Add(b);
            }

            _studioHint = new TextBlock { Style = (Style)FindResource("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            tabs.Children.Add(_studioHint);
            row.Children.Add(tabs);
            _studioBar!.Child = row;
        }

        /// <summary>Turns the Unity-style Scene + Game layout on or off.</summary>
        public void SetStudio(bool on)
        {
            if (_viewContent == null || _game == null || on == _studio) return;
            _studio = on;
            if (_studioToggle != null) _studioToggle.IsChecked = on;
            _studioBar!.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) _game.RefreshSources();
            ApplyStudioLayout();
            ApplyStudioPanels();
            W.Status = on ? "Sahne + Oyun: solda düzenleyici sahnesi, sağda kameranın gördüğü (G ile kapat)" : "Tek sahne görünümü";
        }

        private void ApplyStudioLayout()
        {
            if (_viewContent == null || _game == null || _viewSplitter == null) return;
            var cols = _viewContent.ColumnDefinitions;
            if (!_studio)
            {
                cols[0].Width = new GridLength(1, GridUnitType.Star);
                cols[1].Width = new GridLength(0);
                cols[2].Width = new GridLength(0);
                Scene.Visibility = Visibility.Visible;
                _game.Visibility = Visibility.Collapsed;
                _viewSplitter.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var t in _studioTabs)
            {
                t.Visibility = _studioTabbed ? Visibility.Visible : Visibility.Collapsed;
                t.IsChecked = (string)t.Tag == _studioTab;
            }

            if (_studioTabbedBox != null) _studioTabbedBox.IsChecked = _studioTabbed;
            if (_studioPanelsBox != null) _studioPanelsBox.IsChecked = _studioPanels;
            if (_studioHint != null)
            {
                _studioHint.Text = _studioTabbed
                    ? "Sahne: düzenleyici kamerası, ızgara, seçim ve sensör katmanları · Oyun: seçili kamera / takip kamerası, katmansız"
                    : "Sahne (sol): düzenleyici kamerası, ızgara, seçim ve sensör katmanları · Oyun (sağ): seçili kamera / takip kamerası, katmansız";
            }

            if (_studioTabbed)
            {
                bool game = _studioTab == "game";
                cols[0].Width = game ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                cols[1].Width = new GridLength(0);
                cols[2].Width = game ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Scene.Visibility = game ? Visibility.Collapsed : Visibility.Visible;
                _game.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
                _viewSplitter.Visibility = Visibility.Collapsed;
            }
            else
            {
                cols[0].Width = new GridLength(1, GridUnitType.Star);
                cols[1].Width = new GridLength(5);
                cols[2].Width = new GridLength(1, GridUnitType.Star);
                Scene.Visibility = Visibility.Visible;
                _game.Visibility = Visibility.Visible;
                _viewSplitter.Visibility = Visibility.Visible;
            }
        }

        /// <summary>In Scene + Game mode the palette, inspector and dock make room unless "Paneller" is on.</summary>
        private void ApplyStudioPanels()
        {
            if (_viewHost?.Parent is not Grid main) return;
            bool hide = _studio && !_studioPanels;
            if (hide && _savedColumns.Count == 0)
            {
                for (int i = 0; i < main.ColumnDefinitions.Count; i++)
                {
                    if (i == Grid.GetColumn(_viewHost)) continue;
                    var c = main.ColumnDefinitions[i];
                    _savedColumns.Add((c, c.Width, c.MinWidth));
                    c.MinWidth = 0;
                    c.Width = new GridLength(0);
                }

                _savedDock = (DockRow.Height, DockRow.MinHeight);
                DockRow.MinHeight = 0;
                DockRow.Height = new GridLength(0);
            }
            else if (!hide && _savedColumns.Count > 0)
            {
                foreach (var (c, w, min) in _savedColumns)
                {
                    c.Width = w;
                    c.MinWidth = min;
                }

                _savedColumns.Clear();
                if (_savedDock is { } d)
                {
                    DockRow.Height = d.Height;
                    DockRow.MinHeight = d.Min;
                }

                _savedDock = null;
            }
        }

        /// <summary>Called by the main window when it enters or leaves full screen.</summary>
        public void OnFullScreenChanged(bool fullScreen, bool worldShown)
        {
            if (fullScreen && worldShown && !_studio)
            {
                _studioAuto = true;
                SetStudio(true);
            }
            else if (!fullScreen && _studioAuto)
            {
                _studioAuto = false;
                SetStudio(false);
            }
        }

        private void ShowInGame(string cameraId, bool sensorOutput)
        {
            SetStudio(true);
            _game?.ShowCamera(cameraId, sensorOutput);
            if (_studioTabbed)
            {
                _studioTab = "game";
                ApplyStudioLayout();
            }
        }

        // ================================================================ Ortam tab: light, floor, tapes, GPS

        private void SenseEnvironmentPalette(WorldDocument world)
        {
            var env = world.Environment;
            var view = env.Sensing ?? new SensingEnvironment { FloorReflectance = 0.3, FloorGloss = 0.02 };
            SensingEnvironment Se() => env.Sensing ??= new SensingEnvironment { FloorReflectance = 0.3, FloorGloss = 0.02 };

            Palette.Children.Add(Caption("Işık (kameralar ve IR sensörler)"));
            var lux = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (name, v) in new[] { ("Loş oda · 50 lx", 50.0), ("Ev içi · 200 lx", 200.0), ("Atölye / ofis · 400 lx", 400.0), ("Parlak salon · 1000 lx", 1000.0), ("Bulutlu dış mekân · 10 000 lx", 10000.0), ("Güneşli dış mekân · 100 000 lx", 100000.0) })
            {
                lux.Items.Add(new ComboBoxItem { Content = name, Tag = v, IsSelected = Math.Abs(view.AmbientLux - v) < 1 });
            }

            if (lux.SelectedItem == null) lux.Items.Insert(0, new ComboBoxItem { Content = $"Özel · {view.AmbientLux:0} lx", Tag = view.AmbientLux, IsSelected = true });
            lux.SelectionChanged += (_, _) =>
            {
                if (lux.SelectedItem is ComboBoxItem it && Math.Abs((double)it.Tag - view.AmbientLux) > 0.5) Edit("Ortam ışığı değişti", () => Se().AmbientLux = (double)it.Tag);
            };
            Palette.Children.Add(Labeled("Aydınlık", lux));
            Palette.Children.Add(NumberField("Aydınlık (lx)", view.AmbientLux, v => Edit("Ortam ışığı değişti", () => Se().AmbientLux = v), 0.1, 150000, "0"));
            var ir = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (name, v) in new[] { ("LED / floresan (IR yok)", 0.0), ("Akkor / halojen lamba", 0.05), ("Pencere kenarı, güneş", 0.3), ("Doğrudan güneş", 1.0) })
            {
                ir.Items.Add(new ComboBoxItem { Content = name, Tag = v, IsSelected = Math.Abs(view.AmbientIr - v) < 1e-3 });
            }

            if (ir.SelectedItem == null) ir.Items.Insert(0, new ComboBoxItem { Content = $"Özel · {view.AmbientIr:0.###}", Tag = view.AmbientIr, IsSelected = true });
            ir.SelectionChanged += (_, _) =>
            {
                if (ir.SelectedItem is ComboBoxItem it && Math.Abs((double)it.Tag - view.AmbientIr) > 1e-4) Edit("Ortam IR ışığı değişti", () => Se().AmbientIr = (double)it.Tag);
            };
            Palette.Children.Add(Labeled("IR kaynağı", ir));
            Palette.Children.Add(NumberField("Ortam IR (güneş = 1)", view.AmbientIr, v => Edit("Ortam IR ışığı değişti", () => Se().AmbientIr = v), 0, 2));
            Palette.Children.Add(NumberField("Işık yüksekliği (°)", view.LightElevationDeg, v => Edit("Işık yönü değişti", () => Se().LightElevationDeg = v), 0, 90, "0"));
            Palette.Children.Add(NumberField("Işık yönü (° +X'ten)", view.LightAzimuthDeg, v => Edit("Işık yönü değişti", () => Se().LightAzimuthDeg = v), -360, 360, "0"));
            Palette.Children.Add(Note("Parlak bant ve parlak zemin, ışık kaynağını tam yansıtma açısında sensöre aynalar: o noktalarda siyah bant beyaz okunur. TCRT5000'in gün ışığı filtresi güneşi tam kesmez."));

            Palette.Children.Add(Caption("Zemin (IR yansıtma)"));
            Palette.Children.Add(NumberField("Yayınık yansıtma (0–1)", view.FloorReflectance, v => Edit("Zemin yansıtması değişti", () => Se().FloorReflectance = v), 0.01, 0.98, "0.00"));
            Palette.Children.Add(NumberField("Parlaklık (ayna, 0–1)", view.FloorGloss, v => Edit("Zemin parlaklığı değişti", () => Se().FloorGloss = v), 0, 0.9, "0.00"));

            Palette.Children.Add(Caption("Zemindeki bantlar"));
            var tapes = view.Tapes;
            if (tapes.Count == 0) Palette.Children.Add(Note("Henüz bant yok. Pist ekleyin; her bölümün cinsi (mat, solmuş, parlak, metalik) ayrı seçilir."));
            foreach (var tape in tapes.ToList())
            {
                var t = tape;
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                double len = 0;
                for (int i = 1; i < t.Points.Count; i++) len += Math.Sqrt(Math.Pow(t.Points[i][0] - t.Points[i - 1][0], 2) + Math.Pow(t.Points[i][1] - t.Points[i - 1][1], 2));
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Text = t.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                label.Children.Add(new TextBlock { Text = $"{len * 100:0} cm · {t.Width * 1000:0} mm", Style = (Style)FindResource("Text.Caption") });
                grid.Children.Add(label);
                var finish = new ComboBox { Margin = new Thickness(4, 0, 4, 0) };
                foreach (TapeFinish f in Enum.GetValues(typeof(TapeFinish)))
                {
                    if (f == TapeFinish.Custom) continue;
                    finish.Items.Add(new ComboBoxItem { Content = FloorTape.FinishName(f), Tag = f, IsSelected = f == t.Finish });
                }

                finish.SelectionChanged += (_, _) =>
                {
                    if (finish.SelectedItem is ComboBoxItem it && (TapeFinish)it.Tag != t.Finish) Edit("Bant cinsi değişti", () => t.Finish = (TapeFinish)it.Tag);
                };
                Grid.SetColumn(finish, 1);
                grid.Children.Add(finish);
                var del = new Button { Style = (Style)FindResource("Btn.Icon"), Width = 26, Height = 26, ToolTip = "Bandı sök", Content = new Theme.Icon { Data = (Geometry)FindResource("I.Delete"), Size = 13 } };
                del.Click += (_, _) => Edit("Bant söküldü", () => Se().Tapes.Remove(t));
                Grid.SetColumn(del, 2);
                grid.Children.Add(del);
                Palette.Children.Add(grid);
            }

            Palette.Children.Add(Action("Karışık oval pist ekle (mat + solmuş + parlak)", () => Edit("Pist eklendi", () =>
            {
                var se = Se();
                se.FloorReflectance = Math.Max(se.FloorReflectance, 0.85);
                se.Tapes.AddRange(SenseTemplates.MixedOval());
            }), "1.2 m düzlükler, 45 cm yarıçaplı virajlar, 19 mm bant; beyaz zemin"));
            Palette.Children.Add(Action("Mat oval pist ekle", () => Edit("Pist eklendi", () =>
            {
                var se = Se();
                se.FloorReflectance = Math.Max(se.FloorReflectance, 0.85);
                foreach (var t in SenseTemplates.MixedOval())
                {
                    t.Finish = TapeFinish.Matte;
                    se.Tapes.Add(t);
                }
            })));
            Palette.Children.Add(Action("Düz bant ekle (60 cm, mat)", () => Edit("Bant eklendi", () =>
            {
                var p = Scene.DropPoint();
                Se().Tapes.Add(new FloorTape { Name = "Bant " + (Se().Tapes.Count + 1), Points = { new[] { p.X - 0.3, p.Y }, new[] { p.X + 0.3, p.Y } } });
            })));
            if (tapes.Count > 0) Palette.Children.Add(Action($"Bantları temizle ({tapes.Count})", () => Edit("Bantlar söküldü", () => Se().Tapes.Clear())));

            Palette.Children.Add(Caption("Konum (GPS) ve manyetik alan"));
            Palette.Children.Add(NumberField("Başlangıç enlemi (°)", view.OriginLatitude, v => Edit("Konum değişti", () => Se().OriginLatitude = v), -89.9, 89.9, "0.000000"));
            Palette.Children.Add(NumberField("Başlangıç boylamı (°)", view.OriginLongitude, v => Edit("Konum değişti", () => Se().OriginLongitude = v), -180, 180, "0.000000"));
            Palette.Children.Add(NumberField("Rakım (m)", view.OriginAltitude, v => Edit("Konum değişti", () => Se().OriginAltitude = v), -400, 9000, "0"));
            var sky = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (name, v) in new[] { ("Açık gökyüzü", "open"), ("Şehir içi (yansıma)", "urban"), ("Kapalı mekân", "indoor") })
            {
                sky.Items.Add(new ComboBoxItem { Content = name, Tag = v, IsSelected = view.Sky == v });
            }

            sky.SelectionChanged += (_, _) =>
            {
                if (sky.SelectedItem is ComboBoxItem it && (string)it.Tag != view.Sky) Edit("Gökyüzü görünürlüğü değişti", () => Se().Sky = (string)it.Tag);
            };
            Palette.Children.Add(Labeled("GPS gökyüzü", sky));
            Palette.Children.Add(NumberField("Manyetik alan (µT)", view.MagneticFieldMicroTesla, v => Edit("Manyetik alan değişti", () => Se().MagneticFieldMicroTesla = v), 20, 70, "0.0"));
            Palette.Children.Add(NumberField("Eğim (inklinasyon, °)", view.MagneticInclinationDeg, v => Edit("Manyetik alan değişti", () => Se().MagneticInclinationDeg = v), -90, 90, "0.0"));
            Palette.Children.Add(NumberField("Sapma (deklinasyon, °)", view.MagneticDeclinationDeg, v => Edit("Manyetik alan değişti", () => Se().MagneticDeclinationDeg = v), -60, 60, "0.0"));
        }

        // ================================================================ snapshots

        /// <summary>--fault "template:studio:tabbed:game:sensor:sensors": the template id plus view modes for the snapshot.</summary>
        private string? SenseSnapshotTemplate(string? template)
        {
            if (template == null || !template.Contains(':')) return template;
            var parts = template.Split(':');
            _snapshotModes = parts.Skip(1).ToArray();
            return parts[0];
        }

        private void SenseSnapshotFinish()
        {
            if (_snapshotModes.Length == 0) return;
            if (_snapshotModes.Contains("sensors") && _dockSensors != null)
            {
                _dockSensors.IsChecked = true;
                UpdateSensorsPanel();
            }

            if (_snapshotModes.Contains("studio") || _snapshotModes.Contains("tabbed") || _snapshotModes.Contains("game"))
            {
                _studioTabbed = _snapshotModes.Contains("tabbed");
                if (_snapshotModes.Contains("game")) _studioTab = "game";
                _studioPanels = _snapshotModes.Contains("panels");
                SetStudio(true);
                var cam = World?.Objects.FirstOrDefault(o => o.PartId == SensingParts.CameraId);
                if (cam != null) _game?.ShowCamera(cam.Id, _snapshotModes.Contains("sensor"));
                if (!_snapshotModes.Contains("sensor") && _snapshotModes.Contains("chase")) _game?.ShowCamera(null, false);
            }

            if (_snapshotModes.Contains("top")) Scene.FrameFloor();
            if (_snapshotModes.Contains("close") && World?.Objects.FirstOrDefault(o => SensingParts.IsLineSensor(o.PartId)) is { } ls) Scene.LookLow(ls.Id);
            if (_snapshotModes.Contains("ortam"))
            {
                TabEnv.IsChecked = true;
                BuildPalette();
            }

            if (_snapshotModes.Contains("log"))
            {
                DockLog.IsChecked = true;
                OnDockTab(this, new RoutedEventArgs());
            }

            if (_snapshotModes.Contains("follow"))
            {
                Scene.Follow = true;
            }
        }
    }
}
