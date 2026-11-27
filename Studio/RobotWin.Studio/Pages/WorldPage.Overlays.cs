using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotWin.Studio.Shell;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [overlays] Unity's Overlay menu (the ` key in the Scene view): every overlay of the scene with a box to show or hide
    /// it, and one to hide them all for a clean view: the hint line, the move / rotate handle, the sensors' beams and
    /// cones, the grid. Remembered in the settings.
    /// </summary>
    public partial class WorldPage
    {
        private Popup? _overlayMenu;

        private IEnumerable<(string Name, string Tip, Func<bool> Get, Action<bool> Set)> Overlays()
        {
            yield return ("İpucu satırı", "Sahnenin altındaki fare ve tuş ipuçları", () => Scene.ShowHint, v => { Scene.ShowHint = v; AppSettings.Current.WorldShowHint = v; });
            yield return ("Tutamaç", "Seçili nesneyi taşıma, döndürme ve ölçekleme tutamacı", () => Scene.ShowGizmo, v => { Scene.ShowGizmo = v; AppSettings.Current.WorldShowGizmo = v; });
            yield return ("Sensör kaplamaları", "Mesafe sensörlerinin ışınları, kameraların ve lidarların görüş alanları", () => Scene.ShowSensorOverlays, v => { Scene.ShowSensorOverlays = v; AppSettings.Current.WorldShowSensorOverlays = v; });
            yield return ("Izgara", "Zemindeki ızgara çizgileri (yakalama değişmez)", () => AppSettings.Current.WorldGridShown, v => { AppSettings.Current.WorldGridShown = v; ApplyGridSettings(); });
            yield return ("Görünüm küpü", "Sağ üstteki küp: bir yüzüne ya da köşesine tıklayınca o yönden bakılır", () => AppSettings.Current.WorldShowViewCube, v =>
            {
                AppSettings.Current.WorldShowViewCube = v;
                if (_viewCube != null) _viewCube.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        /// <summary>Puts the remembered choices on the scene (at start).</summary>
        private void ApplyOverlaySettings()
        {
            Scene.ShowHint = AppSettings.Current.WorldShowHint;
            Scene.ShowGizmo = AppSettings.Current.WorldShowGizmo;
            Scene.ShowSensorOverlays = AppSettings.Current.WorldShowSensorOverlays;
        }

        private void OnOverlayMenu(object sender, RoutedEventArgs e) => ShowOverlayMenu(sender as UIElement ?? Scene);

        // ================================================================ [overlays] the view's overlay panels (Unity's)

        /// <summary>
        /// Unity's default arrangement: the tools down the left, the grid and snap along the top, the view cube in the top
        /// right; all without labels (icons only).
        /// </summary>
        private const string DefaultOverlayLayout = "tools:Left:40:40:Vertical:0:1;grid:Top:40:40:Horizontal:0:1";

        private ViewCube? _viewCube;

        /// <summary>[workspaces] The tool strips over the scene.</summary>
        public OverlayHost ToolOverlays => Scene.OverlayPanels;

        private void InitOverlayPanels()
        {
            var host = Scene.OverlayPanels;
            Geometry Ic(string key) => (Geometry)FindResource(key);
            host.Add(new SceneOverlay("tools", "Araçlar", Ic("I.Transform"), ToolsOverlay));
            host.Add(new SceneOverlay("grid", "Izgara ve yakalama", Ic("I.Grid"), GridOverlay));
            host.LoadLayout(DefaultOverlayLayout);
            host.LoadLayout(AppSettings.Current.WorldOverlayLayout);
            host.Arrange();
            host.LayoutChanged += (_, _) =>
            {
                AppSettings.Current.WorldOverlayLayout = host.SaveLayout();
                AppSettings.Current.Save();
            };
            // the view cube: on the view itself in the top right corner, no frame, no handle (SolidWorks' view selector)
            _viewCube = new ViewCube { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 12, 0) };
            var (yaw0, pitch0) = Scene.CameraTurn;
            _viewCube.SetCamera(yaw0, pitch0);
            _viewCube.ViewRequested += (y, pt) => Scene.ViewFrom(y, pt);
            _viewCube.Visibility = AppSettings.Current.WorldShowViewCube ? Visibility.Visible : Visibility.Collapsed;
            Scene.Children.Add(_viewCube);
            Scene.CameraTurned += (yaw, pitch) => _viewCube?.SetCamera(yaw, pitch);
            InitToolGroups();
        }

        /// <summary>[toolbar] KiCad's tool groups: one button for each kind of tool, the rest behind a hold or a right click.</summary>
        private void InitToolGroups()
        {
            Theme.ToolFlyout.Attach(ToolCable, new[]
            {
                new Theme.ToolChoice("Cable", "Kablo çek", "I.Cable", "K"), new Theme.ToolChoice("Joint", "Bağla (eklem)", "I.Joint", "B"),
                new Theme.ToolChoice("Mate", "Hizala (ilişki)", "I.Assembly", "H")
            });
            Theme.ToolFlyout.Attach(EvaluateButton, new[]
            {
                new Theme.ToolChoice("Interference", "Çakışma algılama", "I.Interference"), new Theme.ToolChoice("Clearance", "Boşluk doğrulama", "I.Clearance"),
                new Theme.ToolChoice("Holes", "Delik hizası", "I.HoleAlign"), new Theme.ToolChoice("Explode", "Patlatılmış görünüm", "I.Explode")
            });
            Theme.ToolFlyout.Attach(EditButton, new[]
            {
                new Theme.ToolChoice("Rotate", "Döndürme adımı kadar çevir", "I.Rotate", "Ctrl+E"), new Theme.ToolChoice("Duplicate", "Çoğalt", "I.Plus", "Ctrl+D"),
                new Theme.ToolChoice("Delete", "Sil", "I.Delete", "Del")
            });
        }

        private void OnEvaluate(object sender, RoutedEventArgs e)
        {
            switch ((string?)((FrameworkElement)sender).Tag)
            {
                case "Clearance": OnClearance(sender, e); break;
                case "Holes": OnHoleAlignment(sender, e); break;
                case "Explode": OnExplode(sender, e); break;
                default: OnInterference(sender, e); break;
            }
        }

        private void OnEditAction(object sender, RoutedEventArgs e)
        {
            switch ((string?)((FrameworkElement)sender).Tag)
            {
                case "Duplicate": OnDuplicate(sender, e); break;
                case "Delete": OnDelete(sender, e); break;
                default: OnRotate(sender, e); break;
            }
        }

        /// <summary>A named button of the page moved into an overlay (its handlers and its state stay the page's).</summary>
        private static T Take<T>(T element) where T : FrameworkElement
        {
            switch (element.Parent)
            {
                case Panel panel: panel.Children.Remove(element); break;
                case Border border: border.Child = null; break;
                case ContentControl content: content.Content = null; break;
            }

            element.Margin = new Thickness(1);
            return element;
        }

        private static Border Divider(Orientation orientation)
        {
            var d = new Border { Margin = orientation == Orientation.Vertical ? new Thickness(4, 4, 4, 4) : new Thickness(4, 4, 4, 4) };
            if (orientation == Orientation.Vertical) d.Height = 1;
            else d.Width = 1;
            d.SetResourceReference(Border.BackgroundProperty, "B.Border");
            return d;
        }

        private ToggleButton IconToggle(string icon, string tip, bool on, Action<bool> set)
        {
            var b = new ToggleButton { Content = new Theme.Icon { Data = (Geometry)FindResource(icon), Size = 16 }, ToolTip = Lang.T(tip), IsChecked = on, Margin = new Thickness(1) };
            b.SetResourceReference(StyleProperty, "Toggle.Tool");
            b.Click += (_, _) =>
            {
                set(b.IsChecked == true);
                Scene.Focus();
            };
            return b;
        }

        private Button IconButton(string icon, string tip, Action click)
        {
            var b = new Button { Content = new Theme.Icon { Data = (Geometry)FindResource(icon), Size = 16 }, ToolTip = Lang.T(tip), Margin = new Thickness(1) };
            b.SetResourceReference(StyleProperty, "Btn.Icon");
            b.Click += (_, _) =>
            {
                click();
                Scene.Focus();
            };
            return b;
        }

        /// <summary>A text button for the overlay menu (a preset's name, save, default): a menu reads in words.</summary>
        private Button PanelButton(string text, string tip, Action click)
        {
            var b = new Button { Content = Lang.T(text), ToolTip = Lang.T(tip), Padding = new Thickness(8, 0, 8, 0), MinHeight = 24, Margin = new Thickness(2), FontSize = 12 };
            b.SetResourceReference(StyleProperty, "Btn.Ghost");
            b.Click += (_, _) => click();
            return b;
        }

        /// <summary>The tools overlay (Unity's): Q W E R Y T, and where the handles stand and which axes they take.</summary>
        private FrameworkElement ToolsOverlay(Orientation orientation)
        {
            var p = new StackPanel { Orientation = orientation };
            foreach (var b in new ButtonBase[] { ToolSelect, GizmoMove, GizmoRotate, GizmoScale, GizmoAll, GizmoRect }) p.Children.Add(Take(b));
            p.Children.Add(Divider(orientation));
            p.Children.Add(Take(GizmoPlace));
            p.Children.Add(Take(GizmoSpace));
            return p;
        }

        /// <summary>The grid and snap overlay (Unity's): snapping, the grid shown, the move step, and the full window.</summary>
        private FrameworkElement GridOverlay(Orientation orientation)
        {
            var s = AppSettings.Current;
            var p = new StackPanel { Orientation = orientation };
            p.Children.Add(IconToggle("I.Magnet", "Izgaraya yakala (Alt basılıyken serbest)", s.WorldSnap, on =>
            {
                s.WorldSnap = on;
                s.Save();
                ApplyGridSettings();
            }));
            p.Children.Add(IconToggle("I.Grid", "Izgarayı göster", s.WorldGridShown, on =>
            {
                s.WorldGridShown = on;
                s.Save();
                ApplyGridSettings();
            }));
            var step = new TextBox { Text = s.WorldGridMm.ToString("0.###", CultureInfo.CurrentCulture), Width = 46, TextAlignment = TextAlignment.Right, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.T("Taşıma adımı (mm); yazıp Enter") };
            void Apply()
            {
                if (!double.TryParse(step.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0) return;
                s.WorldGridMm = v;
                s.Save();
                ApplyGridSettings();
            }

            step.KeyDown += (_, k) =>
            {
                if (k.Key == System.Windows.Input.Key.Enter) Apply();
            };
            step.LostKeyboardFocus += (_, _) => Apply();
            p.Children.Add(step);
            var full = Take(GridSnapButton);
            full.Content = new Theme.Icon { Data = (Geometry)FindResource("I.Settings"), Size = 16 };
            p.Children.Add(full);
            return p;
        }

        /// <summary>The overlay menu's panels part: each panel shown or hidden, and Unity's presets (save, load, delete, default).</summary>
        private void OverlayPanelsMenu(StackPanel list)
        {
            var host = Scene.OverlayPanels;
            list.Children.Add(new TextBlock { Text = Lang.T("Paneller"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
            foreach (var o in host.Overlays)
            {
                var overlay = o;
                var box = new CheckBox { Content = Lang.T(o.Title), IsChecked = o.Shown, Margin = new Thickness(0, 2, 0, 2), ToolTip = Lang.T("Görünümün üstündeki panel: ≡ ile sürüklenir, sağ tıkla düzeni seçilir") };
                box.Click += (_, _) =>
                {
                    overlay.Shown = box.IsChecked == true;
                    host.Changed();
                };
                list.Children.Add(box);
            }

            list.Children.Add(new TextBlock { Text = Lang.T("Hazır düzenler"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
            var presets = AppSettings.Current.WorldOverlayPresets;
            foreach (var name in presets.Keys.OrderBy(k => k, StringComparer.CurrentCulture).ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var del = IconButton("I.Close", "Hazır düzeni sil", () =>
                {
                    presets.Remove(name);
                    AppSettings.Current.Save();
                    _overlayMenu!.IsOpen = false;
                });
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                row.Children.Add(PanelButton(name, "Bu düzene geç", () =>
                {
                    host.LoadLayout(presets[name]);
                    host.Changed();
                }));
                list.Children.Add(row);
            }

            var save = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var nameBox = new TextBox { Text = Lang.T("Düzen") + " " + (presets.Count + 1).ToString(CultureInfo.InvariantCulture), MinWidth = 110 };
            var keep = PanelButton("Kaydet", "Şimdiki düzeni bu adla sakla", () =>
            {
                string n = nameBox.Text.Trim();
                if (n.Length == 0) return;
                presets[n] = host.SaveLayout();
                AppSettings.Current.Save();
                _overlayMenu!.IsOpen = false;
            });
            DockPanel.SetDock(keep, Dock.Right);
            save.Children.Add(keep);
            save.Children.Add(nameBox);
            list.Children.Add(save);
            list.Children.Add(PanelButton("Varsayılan düzen", "Panelleri ilk yerlerine döndür", () =>
            {
                host.LoadLayout(DefaultOverlayLayout);
                host.Changed();
            }));
        }

        private void ShowOverlayMenu(UIElement anchor)
        {
            if (_overlayMenu?.IsOpen == true)
            {
                _overlayMenu.IsOpen = false;
                return;
            }

            var list = new StackPanel { Margin = new Thickness(10, 8, 10, 8), MinWidth = 220 };
            list.Children.Add(new TextBlock { Text = Lang.T("Kaplamalar"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var boxes = new List<CheckBox>();
            foreach (var (name, tip, get, set) in Overlays())
            {
                var box = new CheckBox { Content = Lang.T(name), ToolTip = Lang.T(tip), IsChecked = get(), Margin = new Thickness(0, 2, 0, 2) };
                box.Click += (_, _) =>
                {
                    set(box.IsChecked == true);
                    AppSettings.Current.Save();
                    Scene.RefreshOverlays();
                };
                boxes.Add(box);
                list.Children.Add(box);
            }

            var all = new CheckBox { Content = Lang.T("Hepsini gizle"), ToolTip = Lang.T("Temiz bir görünüm için bütün kaplamalar (`)"), Margin = new Thickness(0, 8, 0, 0) };
            all.IsChecked = boxes.TrueForAll(b => b.IsChecked != true);
            all.Click += (_, _) =>
            {
                bool show = all.IsChecked != true;
                foreach (var (_, _, _, set) in Overlays()) set(show);
                foreach (var b in boxes) b.IsChecked = show;
                AppSettings.Current.Save();
                Scene.RefreshOverlays();
            };
            list.Children.Add(all);
            OverlayPanelsMenu(list);   // [overlays] the panels and the presets
            var frame = new Border { Child = list, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            frame.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            frame.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            _overlayMenu = new Popup { Child = frame, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, IsOpen = true };
            Lang.Translate(frame);
            if (Unattended.Active)
            {
                frame.UpdateLayout();
                App.SnapshotTarget = frame;   // the menu is the picture
            }
        }
    }
}
