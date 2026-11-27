using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Theme;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [grid] Unity's Grid and Snap window for Montaj and Dünya: snapping on or off, the move, rotate and size steps,
    /// the floor grid shown or not and how strongly, and the selection put on the nearest grid point. Kept in the
    /// settings, so every world opens with them.
    /// </summary>
    public partial class WorldPage
    {
        private Popup? _gridPopup;

        /// <summary>The settings into the scene (at start and whenever they change).</summary>
        private void ApplyGridSettings()
        {
            var s = AppSettings.Current;
            double grid = Math.Max(0, s.WorldGridMm) / 1000.0;
            bool redraw = Math.Abs(Scene.Grid - grid) > 1e-12;
            Scene.Grid = grid;
            Scene.SnapOn = s.WorldSnap;
            Scene.RotateStep = Math.Max(0, s.WorldRotateStep);
            Scene.SizeStep = Math.Max(0, s.WorldSizeStepMm) / 1000.0;
            Scene.GridOpacity = s.WorldGridOpacity;
            Scene.GridShown = s.WorldGridShown;
            // [grid] per-axis steps (linked: all X's), the grid's position and its plane
            double gy = s.WorldGridLinked ? 0 : Math.Max(0, s.WorldGridYMm) / 1000.0, gz = s.WorldGridLinked ? 0 : Math.Max(0, s.WorldGridZMm) / 1000.0;
            var origin = new Point(s.WorldGridOriginXMm / 1000.0, s.WorldGridOriginYMm / 1000.0);
            string plane = s.WorldGridPlane is "xz" or "yz" ? s.WorldGridPlane : "xy";
            redraw |= Math.Abs(Scene.GridY - gy) > 1e-12 || Math.Abs(Scene.GridZ - gz) > 1e-12 || Scene.GridOrigin != origin || Scene.GridPlane != plane;
            Scene.GridY = gy;
            Scene.GridZ = gz;
            Scene.GridOrigin = origin;
            Scene.GridPlane = plane;
            if (redraw) Scene.RefreshGrid();
        }

        private void OnGridSnap(object sender, RoutedEventArgs e)
        {
            if (_gridPopup is { IsOpen: true })
            {
                _gridPopup.IsOpen = false;
                return;
            }

            _gridPopup = GridSnapPanel((FrameworkElement)sender);
            _gridPopup.IsOpen = true;
        }

        private Popup GridSnapPanel(FrameworkElement anchor)
        {
            var s = AppSettings.Current;
            var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = false };
            var root = new StackPanel { Width = 320, Margin = new Thickness(12, 10, 12, 12) };

            void Save()
            {
                s.Save();
                ApplyGridSettings();
            }

            root.Children.Add(new TextBlock { Text = Lang.T("Izgara ve yakalama"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

            var snap = new CheckBox { Content = Lang.T("Izgaraya yakala"), IsChecked = s.WorldSnap, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 6),
                ToolTip = Lang.T("Taşıma, döndürme ve boyut sürüklemeleri adımlara oturur; Alt basılıyken o sürükleme serbesttir") };
            snap.Click += (_, _) =>
            {
                s.WorldSnap = snap.IsChecked == true;
                Save();
            };
            root.Children.Add(snap);

            var shown = new CheckBox { Content = Lang.T("Izgarayı göster"), IsChecked = s.WorldGridShown, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 8) };
            shown.Click += (_, _) =>
            {
                s.WorldGridShown = shown.IsChecked == true;
                Save();
            };
            root.Children.Add(shown);

            root.Children.Add(SliderRow(Lang.T("Saydamlık"), 0, 100, s.WorldGridOpacity * 100, v =>
            {
                s.WorldGridOpacity = v / 100;
                Save();
            }, box => SliderInput.Attach(box.Slider, box.Box, "0", " %")));

            // [grid] Unity's grid plane: the floor only, or an upright grid through the grid's position too
            var planes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
            planes.Children.Add(new TextBlock { Text = Lang.T("Düzlem"), Width = 104, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("B.TextSecondary") });
            foreach (var (key, label, tip) in new[] { ("xy", "XY", "Yalnız zemindeki ızgara"), ("xz", "XZ", "Zemine ek olarak, ızgaranın yerinden geçen dik XZ ızgarası (Y'ye dik)"), ("yz", "YZ", "Zemine ek olarak, ızgaranın yerinden geçen dik YZ ızgarası (X'e dik)") })
            {
                var chip = new RadioButton { Content = label, GroupName = "gridplane", Style = (Style)FindResource("Chip"), IsChecked = s.WorldGridPlane == key || key == "xy" && s.WorldGridPlane is not ("xz" or "yz"), ToolTip = Lang.T(tip) };
                string k = key;
                chip.Click += (_, _) =>
                {
                    s.WorldGridPlane = k;
                    Save();
                };
                planes.Children.Add(chip);
            }

            root.Children.Add(planes);

            root.Children.Add(new TextBlock { Text = Lang.T("Adımlar"), Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 10, 0, 6) });
            root.Children.Add(SliderRow(Lang.T(s.WorldGridLinked ? "Taşı" : "Taşı X"), 0.5, 100, Math.Max(0.5, s.WorldGridMm), v =>
            {
                s.WorldGridMm = Math.Round(v, 4);
                Save();
            }, box => SliderInput.AttachLength(box.Slider, box.Box)));
            // [grid] Unity's linked step: off, Y and Z take their own
            var linked = new CheckBox { Content = Lang.T("X, Y ve Z aynı adım"), IsChecked = s.WorldGridLinked, Margin = new Thickness(104, 0, 0, 6),
                ToolTip = Lang.T("Kapalıyken Y ve Z kendi taşıma adımlarını alır (Unity'deki bağlı adım)") };
            linked.Click += (_, _) =>
            {
                s.WorldGridLinked = linked.IsChecked == true;
                if (!s.WorldGridLinked)
                {
                    s.WorldGridYMm = s.WorldGridMm;
                    s.WorldGridZMm = s.WorldGridMm;
                }

                Save();
                popup.IsOpen = false;
                OnGridSnap(anchor, new RoutedEventArgs());
            };
            root.Children.Add(linked);
            if (!s.WorldGridLinked)
            {
                root.Children.Add(SliderRow(Lang.T("Taşı Y"), 0.5, 100, Math.Max(0.5, s.WorldGridYMm), v =>
                {
                    s.WorldGridYMm = Math.Round(v, 4);
                    Save();
                }, box => SliderInput.AttachLength(box.Slider, box.Box)));
                root.Children.Add(SliderRow(Lang.T("Taşı Z"), 0.5, 100, Math.Max(0.5, s.WorldGridZMm), v =>
                {
                    s.WorldGridZMm = Math.Round(v, 4);
                    Save();
                }, box => SliderInput.AttachLength(box.Slider, box.Box)));
            }
            root.Children.Add(SliderRow(Lang.T("Döndür"), 1, 90, Math.Max(1, s.WorldRotateStep), v =>
            {
                s.WorldRotateStep = Math.Round(v, 2);
                Save();
            }, box => SliderInput.Attach(box.Slider, box.Box, "0.#", "°")));
            root.Children.Add(SliderRow(Lang.T("Boyut"), 0, 100, s.WorldSizeStepMm, v =>
            {
                s.WorldSizeStepMm = Math.Round(v, 4);
                Save();
            }, box => SliderInput.AttachLength(box.Slider, box.Box)));
            root.Children.Add(new TextBlock
            {
                Text = Lang.T("Boyut 0: taşıma adımı kullanılır. Ctrl+E seçiliyi döndürme adımı kadar çevirir."),
                Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10)
            });

            // [grid] Unity's Grid Position: where the lines start; from the selected part, or back to the world's origin
            root.Children.Add(new TextBlock { Text = Lang.T("Izgaranın yeri"), Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 4, 0, 6) });
            var place = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            TextBox Coord(string label, double mm, Action<double> set)
            {
                place.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Foreground = (Brush)FindResource("B.TextSecondary") });
                var box = new TextBox { Width = 72, Text = mm.ToString("0.###", CultureInfo.CurrentCulture), TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 10, 0), ToolTip = Lang.T("mm; yazıp Enter") };
                void Apply()
                {
                    if (!double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                    set(v);
                    Save();
                }

                box.KeyDown += (_, k) =>
                {
                    if (k.Key == System.Windows.Input.Key.Enter) Apply();
                };
                box.LostKeyboardFocus += (_, _) => Apply();
                place.Children.Add(box);
                return box;
            }

            var ox = Coord("X", s.WorldGridOriginXMm, v => s.WorldGridOriginXMm = v);
            var oy = Coord("Y", s.WorldGridOriginYMm, v => s.WorldGridOriginYMm = v);
            root.Children.Add(place);
            var originButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            var fromSelected = new Button { Content = Lang.T("Seçiliden al"), Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 6, 0),
                ToolTip = Lang.T("Izgaranın yerini seçili parçanın yerine taşır (Unity'deki etkin nesneden kopyala)"), IsEnabled = World?.Find(Scene.Selected) != null };
            fromSelected.Click += (_, _) =>
            {
                if (World?.Find(Scene.Selected) is not { } sel) return;
                s.WorldGridOriginXMm = Math.Round(sel.Pose.X * 1000, 3);
                s.WorldGridOriginYMm = Math.Round(sel.Pose.Y * 1000, 3);
                ox.Text = s.WorldGridOriginXMm.ToString("0.###", CultureInfo.CurrentCulture);
                oy.Text = s.WorldGridOriginYMm.ToString("0.###", CultureInfo.CurrentCulture);
                Save();
            };
            var toWorld = new Button { Content = Lang.T("Dünyaya sıfırla"), Style = (Style)FindResource("Btn.Ghost"), ToolTip = Lang.T("Izgara dünyanın başlangıç noktasından başlar") };
            toWorld.Click += (_, _) =>
            {
                s.WorldGridOriginXMm = 0;
                s.WorldGridOriginYMm = 0;
                ox.Text = "0";
                oy.Text = "0";
                Save();
            };
            originButtons.Children.Add(fromSelected);
            originButtons.Children.Add(toWorld);
            root.Children.Add(originButtons);

            var buttons = new WrapPanel();
            var align = new Button { Content = Lang.T("Seçiliyi ızgaraya oturt"), Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 6, 0),
                ToolTip = Lang.T("Seçili parçayı (ve üstüne takılanları) X ve Y'de en yakın ızgara noktasına taşır") };
            align.Click += (_, _) => AlignSelectedToGrid();
            var reset = new Button { Content = Lang.T("Varsayılana dön"), Style = (Style)FindResource("Btn.Ghost") };
            reset.Click += (_, _) =>
            {
                s.WorldSnap = true;
                s.WorldGridShown = true;
                s.WorldGridOpacity = 1;
                s.WorldGridMm = 5;
                s.WorldRotateStep = 15;
                s.WorldSizeStepMm = 0;
                s.WorldGridLinked = true;   // [grid]
                s.WorldGridYMm = 5;
                s.WorldGridZMm = 5;
                s.WorldGridOriginXMm = 0;
                s.WorldGridOriginYMm = 0;
                s.WorldGridPlane = "xy";
                Save();
                popup.IsOpen = false;
                OnGridSnap(anchor, new RoutedEventArgs());
            };
            buttons.Children.Add(align);
            buttons.Children.Add(reset);
            root.Children.Add(buttons);

            var frame = new Border { Child = root, BorderThickness = new Thickness(1) };
            frame.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            frame.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            popup.Child = frame;
            return popup;
        }

        private sealed class SliderBox
        {
            public Slider Slider = null!;
            public TextBox Box = null!;
        }

        /// <summary>A label, a slider and its typed box, committing on release and on Enter.</summary>
        private FrameworkElement SliderRow(string label, double min, double max, double value, Action<double> commit, Action<SliderBox> attach)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("B.TextSecondary") });
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);
            var box = new TextBox { Width = 78, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(6, 0, 0, 0) };
            Grid.SetColumn(box, 2);
            grid.Children.Add(box);
            attach(new SliderBox { Slider = slider, Box = box });
            double last = slider.Value;
            void Commit()
            {
                if (Math.Abs(slider.Value - last) < 1e-9) return;
                last = slider.Value;
                commit(slider.Value);
            }

            slider.PreviewMouseUp += (_, _) => Commit();
            slider.LostKeyboardFocus += (_, _) => Commit();
            slider.ValueChanged += (_, _) =>
            {
                if (!slider.IsMouseCaptureWithin) Commit();   // typed into the box, or the arrow keys
            };
            return grid;
        }

        /// <summary>Unity's "Align selected to grid": the selection (with what is mounted on it) onto the nearest grid point in X and Y.</summary>
        private void AlignSelectedToGrid()
        {
            var world = World;
            var o = world?.Find(Scene.Selected);
            if (world == null || o == null || Scene.Grid <= 0) return;
            var at = Scene.NearestGridPoint(o.Pose.X, o.Pose.Y);   // [grid] each axis' step, from the grid's position
            double dx = at.X - o.Pose.X, dy = at.Y - o.Pose.Y;
            if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
            Edit(string.Format(CultureInfo.InvariantCulture, "{0}: ızgaraya oturtuldu", o.Name), () => world.MoveAssembly(o, dx, dy, 0));
        }
    }
}
