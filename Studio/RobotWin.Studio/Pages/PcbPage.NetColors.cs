using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [net-colors] KiCad's net colours in the Hatlar tab: a swatch beside each net, its menu a row of flat colours and
    /// "Renk yok". A coloured net's copper is drawn in its colour on every layer (Pcb/BoardCanvas.cs).
    /// </summary>
    public partial class PcbPage
    {
        private static readonly (string Name, string Hex)[] NetPalette =
        {
            ("Kırmızı", "#E5484D"), ("Turuncu", "#F08A3C"), ("Sarı", "#F2C14E"), ("Yeşil", "#46A758"),
            ("Turkuaz", "#2BB3A3"), ("Mavi", "#3E7BFA"), ("Lacivert", "#4C5FD5"), ("Mor", "#8E5BD9"),
            ("Pembe", "#D6409F"), ("Kahverengi", "#A0704A"), ("Gri", "#9A9AA4"), ("Beyaz", "#E8E8EC")
        };

        private FrameworkElement NetColorSwatch(BoardDocument board, string net)
        {
            string? now = board.NetColorOf(net);
            var chip = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("B.BorderStrong"),
                Background = now != null ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(now)) : Brushes.Transparent
            };
            var button = new Button
            {
                Style = (Style)FindResource("Btn.Icon"),
                Width = 24,
                Height = 24,
                MinHeight = 24,
                Padding = new Thickness(0),
                Content = chip,
                ToolTip = Lang.T(now != null ? "Ağın rengi: bakırı bu renkte görünür (değiştirmek için tıklayın)" : "Ağa renk ver: bakırı her katmanda bu renkte görünür")
            };
            button.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = button };
                foreach (var (name, hex) in NetPalette)
                {
                    var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) };
                    var item = new MenuItem { Header = Lang.T(name), Icon = dot, IsChecked = string.Equals(now, hex, System.StringComparison.OrdinalIgnoreCase) };
                    string chosen = hex;
                    item.Click += (_, _) => Edit(Lang.T($"{net} rengi değişti"), () => board.NetColors[net] = chosen);
                    menu.Items.Add(item);
                }

                menu.Items.Add(new Separator());
                var none = new MenuItem { Header = Lang.T("Renk yok (katmanın rengi)"), IsEnabled = now != null };
                none.Click += (_, _) => Edit(Lang.T($"{net} rengi kaldırıldı"), () => board.NetColors.Remove(net));
                menu.Items.Add(none);
                if (board.NetColors.Count > 0)
                {
                    var all = new MenuItem { Header = Lang.T("Bütün ağ renklerini kaldır") };
                    all.Click += (_, _) => Edit(Lang.T("Ağ renkleri kaldırıldı"), () => board.NetColors.Clear());
                    menu.Items.Add(all);
                }

                menu.IsOpen = true;
            };
            return button;
        }
    }
}
