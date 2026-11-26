using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RobotWin.Studio.Shell;

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
        }

        /// <summary>Puts the remembered choices on the scene (at start).</summary>
        private void ApplyOverlaySettings()
        {
            Scene.ShowHint = AppSettings.Current.WorldShowHint;
            Scene.ShowGizmo = AppSettings.Current.WorldShowGizmo;
            Scene.ShowSensorOverlays = AppSettings.Current.WorldShowSensorOverlays;
        }

        private void OnOverlayMenu(object sender, RoutedEventArgs e) => ShowOverlayMenu(sender as UIElement ?? Scene);

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
