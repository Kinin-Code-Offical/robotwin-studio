using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [layers] The circuit's simulation layers switched on and off (D3): the wires' voltage colours, the current's flow,
    /// how hard each part works (power, current, voltage, temperature against its ratings) and the faults. Remembered.
    /// </summary>
    public partial class SchematicPage
    {
        private Popup? _layersPopup;

        private void OnSimLayers(object sender, RoutedEventArgs e) => ShowSimLayers();

        private void ShowSimLayers()
        {
            if (_layersPopup?.IsOpen == true)
            {
                _layersPopup.IsOpen = false;
                return;
            }

            var s = AppSettings.Current;
            var list = new StackPanel { Margin = new Thickness(12, 10, 12, 10), MinWidth = 240 };
            list.Children.Add(new TextBlock { Text = Lang.T("Simülasyon katmanları"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            void Layer(string name, string tip, bool on, Action<bool> set)
            {
                var box = new CheckBox { Content = Lang.T(name), ToolTip = Lang.T(tip), IsChecked = on, Margin = new Thickness(0, 3, 0, 3) };
                box.Click += (_, _) =>
                {
                    set(box.IsChecked == true);
                    s.Save();
                    Canvas.InvalidateVisual();
                };
                list.Children.Add(box);
            }

            Layer("Gerilim", "Teller hattın gerilimine göre renklenir (mavi düşük, kırmızı yüksek)", s.SimLayerVoltage, v => s.SimLayerVoltage = v);
            Layer("Akım akışı", "İki bacaklı parçaların üstünde akımın yönünde ve hızında akan noktalar", s.ShowCurrentFlow, v => s.ShowCurrentFlow = v);
            Layer("Zorlanma (güç, sıcaklık)", "Sınırının %60'ından fazlasını kullanan parçanın çevresi sarıdan kırmızıya parlar; hangi sınır ve yüzdesi yazar", s.SimLayerStress, v => s.SimLayerStress = v);
            Layer("Arıza noktaları", "Hasar gören parça kırmızı halkayla ve nedeniyle işaretlenir; uzaktan da bulunur", s.SimLayerFaults, v => s.SimLayerFaults = v);
            var note = new TextBlock { Text = Lang.T("Yalnız simülasyon sürerken görünür."), Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 6, 0, 0) };
            list.Children.Add(note);
            var frame = new Border { Child = list, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            frame.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            frame.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            Lang.Translate(frame);
            _layersPopup = new Popup { Child = frame, PlacementTarget = SimLayersButton, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, IsOpen = true };
            if (Unattended.Active)
            {
                frame.UpdateLayout();
                App.SnapshotTarget = frame;   // --fault sim-layers: the menu is the picture
            }
        }
    }
}
