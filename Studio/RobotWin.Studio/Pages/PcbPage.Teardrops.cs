using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Theme;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [teardrop] KiCad's Teardrops dialog as a panel under the toolbar's Damla button: on / off, on pads and vias,
    /// curved or straight, the length and width as a share of the pad and their limits. A change refills the pours,
    /// since another net's pour keeps its clearance from the teardrops.
    /// </summary>
    public partial class PcbPage
    {
        private Popup? _teardropPanel;

        private void OnTeardrops(object sender, RoutedEventArgs e)
        {
            if (_teardropPanel is { IsOpen: true })
            {
                _teardropPanel.IsOpen = false;
                return;
            }

            _teardropPanel = TeardropPanel((FrameworkElement)sender);
            _teardropPanel.IsOpen = true;
        }

        private void ChangeTeardrops(string what, Action<TeardropSettings> change)
        {
            var board = Board;
            Edit(what, () =>
            {
                change(board.Teardrops);
                if (board.Zones.Count > 0) ZoneFiller.FillAll(board);
            });
            int count = Teardrops.Of(board).Count;
            W.Status = board.Teardrops.Enabled ? Lang.T($"Damla: {count} bağlantıda") : Lang.T("Damla kapalı");
        }

        private Popup TeardropPanel(FrameworkElement anchor)
        {
            var s = Board.Teardrops;
            var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = false };
            var root = new StackPanel { Width = 330, Margin = new Thickness(12, 10, 12, 12) };
            root.Children.Add(new TextBlock { Text = Lang.T("Damla (teardrop)"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            root.Children.Add(new TextBlock
            {
                Text = Lang.T("İnce hattın ped ya da via'ya girdiği yerde bakır pedden hatta doğru genişler: delik kayarsa, aşındırma yerse ya da kart esnerse bağlantı kopmaz. Başka ağlara kural aralığından yakın düşecek damla daraltılır ya da konmaz."),
                Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });

            CheckBox Switch(string text, bool value, Action<TeardropSettings, bool> set)
            {
                var box = new CheckBox { Content = Lang.T(text), IsChecked = value, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 6) };
                box.Click += (_, _) => ChangeTeardrops(Lang.T("Damla ayarı değişti"), t => set(t, box.IsChecked == true));
                return box;
            }

            root.Children.Add(Switch("Damla ekle", s.Enabled, (t, v) => t.Enabled = v));
            root.Children.Add(Switch("Pedlerde", s.OnPads, (t, v) => t.OnPads = v));
            root.Children.Add(Switch("Via'larda", s.OnVias, (t, v) => t.OnVias = v));
            root.Children.Add(Switch("Eğri kenarlar", s.Curved, (t, v) => t.Curved = v));
            root.Children.Add(SliderRow("Uzunluk (pedin %)", 10, 100, s.LengthRatio * 100, "0", " %", v => ChangeTeardrops(Lang.T("Damla ayarı değişti"), t => t.LengthRatio = v / 100)));
            root.Children.Add(SliderRow("En uzun (mm)", 0.1, 5, s.MaxLength, "0.##", " mm", v => ChangeTeardrops(Lang.T("Damla ayarı değişti"), t => t.MaxLength = v)));
            root.Children.Add(SliderRow("Genişlik (pedin %)", 30, 100, s.WidthRatio * 100, "0", " %", v => ChangeTeardrops(Lang.T("Damla ayarı değişti"), t => t.WidthRatio = v / 100)));
            root.Children.Add(SliderRow("En geniş (mm)", 0.2, 5, s.MaxWidth, "0.##", " mm", v => ChangeTeardrops(Lang.T("Damla ayarı değişti"), t => t.MaxWidth = v)));

            var frame = new Border { Child = root, BorderThickness = new Thickness(1) };
            frame.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            frame.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            popup.Child = frame;
            return popup;
        }

        /// <summary>A label, a slider and its typed box; the value is committed on release and on Enter.</summary>
        private FrameworkElement SliderRow(string label, double min, double max, double value, string format, string unit, Action<double> commit)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = Lang.T(label), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("B.TextSecondary"), TextWrapping = TextWrapping.Wrap });
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);
            var box = SliderInput.Create(slider, format, unit, width: 70);
            Grid.SetColumn(box, 2);
            grid.Children.Add(box);
            double last = slider.Value;
            void Commit()
            {
                if (Math.Abs(slider.Value - last) < 1e-9) return;
                last = slider.Value;
                commit(slider.Value);
            }

            slider.PreviewMouseUp += (_, _) => Commit();
            slider.ValueChanged += (_, _) =>
            {
                if (!slider.IsMouseCaptureWithin) Commit();
            };
            return grid;
        }
    }
}
