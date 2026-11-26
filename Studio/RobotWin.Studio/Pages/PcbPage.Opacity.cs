using System;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [opacity] KiCad's Objects tab: how opaque tracks (with their teardrops), vias, pads and pours are drawn, each a slider
    /// with a typed % box, remembered in the settings. Only the view changes.
    /// </summary>
    public partial class PcbPage
    {
        /// <summary>[layer-pair] The two layers V moves between, as two lists of the board's copper layers.</summary>
        private void LayerPairRow()
        {
            var stack = Board.CopperLayers();
            if (stack.Count < 2) return;
            LayersPanel.Children.Add(Overline("Katman çifti (V)"));
            LayersPanel.Children.Add(Caption("Yol çizerken V via koyup çiftin öbür katmanına geçer; çizmiyorken etkin katmanı değiştirir.", 0));
            var (a, b) = Canvas.LayerPair;
            var grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 4, 0, 6) };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            System.Windows.Controls.ComboBox Pick(RobotTwin.CoreSim.Pcb.CopperLayer now, Action<RobotTwin.CoreSim.Pcb.CopperLayer> set)
            {
                var box = new System.Windows.Controls.ComboBox();
                foreach (var l in stack) box.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = RobotTwin.CoreSim.Pcb.PcbLayers.DisplayName(l), Tag = l, IsSelected = l == now });
                box.SelectionChanged += (_, _) =>
                {
                    if (box.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: RobotTwin.CoreSim.Pcb.CopperLayer l }) set(l);
                };
                return box;
            }

            var first = Pick(a, l =>
            {
                Canvas.PairA = l;
                AppSettings.Current.PcbLayerPairA = l.ToString();
                AppSettings.Current.Save();
            });
            var second = Pick(b, l =>
            {
                Canvas.PairB = l;
                AppSettings.Current.PcbLayerPairB = l.ToString();
                AppSettings.Current.Save();
            });
            var arrow = new System.Windows.Controls.TextBlock { Text = "↔", Margin = new System.Windows.Thickness(8, 0, 8, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            System.Windows.Controls.Grid.SetColumn(arrow, 1);
            System.Windows.Controls.Grid.SetColumn(second, 2);
            grid.Children.Add(first);
            grid.Children.Add(arrow);
            grid.Children.Add(second);
            LayersPanel.Children.Add(grid);
        }

        private void OpacityRows()
        {
            LayersPanel.Children.Add(Overline("Saydamlık"));
            void Row(string label, double value, Action<double> apply)
            {
                LayersPanel.Children.Add(SliderRow(label, 10, 100, value * 100, "0", " %", v =>
                {
                    apply(v / 100);
                    AppSettings.Current.Save();
                    Canvas.InvalidateVisual();
                }));
            }

            Row("Yollar", Canvas.TrackOpacity, v => AppSettings.Current.PcbTrackOpacity = Canvas.TrackOpacity = v);
            Row("Via'lar", Canvas.ViaOpacity, v => AppSettings.Current.PcbViaOpacity = Canvas.ViaOpacity = v);
            Row("Pedler", Canvas.PadOpacity, v => AppSettings.Current.PcbPadOpacity = Canvas.PadOpacity = v);
            Row("Dolgular", Canvas.ZoneOpacity, v => AppSettings.Current.PcbZoneOpacity = Canvas.ZoneOpacity = v);
        }
    }
}
