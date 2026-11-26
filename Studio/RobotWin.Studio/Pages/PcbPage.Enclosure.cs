using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Pcb;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [enclosure] The DRC tab's Kutu switch and fields: the box the board goes into (inside sizes, where the board sits, its
    /// standoffs, the leads under it) and the openings in its walls. With a box the DRC holds every part's body to it.
    /// </summary>
    public partial class PcbPage
    {
        private bool _enclosureSync;
        private bool _enclosureOpen;   // unfolded when the box is made, folded to its summary line otherwise

        private void OnEnclosureSwitch(object sender, RoutedEventArgs e)
        {
            if (_enclosureSync) return;
            var board = Board;
            bool on = EnclosureSwitch.IsChecked == true;
            if (on == (board.Enclosure != null)) return;
            _enclosureOpen = on;
            if (on) Edit("Kutu eklendi: kartın ve parçalarının sığdığı ölçülerle", () => board.Enclosure = BoardEnclosure.Around(board));
            else Edit("Kutu kaldırıldı", () => board.Enclosure = null);
            RunDrcNow();
        }

        private void UpdateEnclosurePanel(BoardDocument board)
        {
            _enclosureSync = true;
            EnclosureSwitch.IsChecked = board.Enclosure != null;
            _enclosureSync = false;
            EnclosurePanel.Children.Clear();
            var box = board.Enclosure;
            EnclosurePanel.Visibility = box == null ? Visibility.Collapsed : Visibility.Visible;
            if (box == null) return;

            // Folded to one line (its sizes) so the DRC list keeps the room; unfolded for editing.
            var fields = new StackPanel();
            var expander = new Expander
            {
                Header = Shell.Lang.T($"{Fmt(box.InnerLength)} × {Fmt(box.InnerWidth)} × {Fmt(box.InnerHeight)} mm iç · ayaklar {Fmt(box.Standoff)} mm · {box.Openings.Count} açıklık"),
                IsExpanded = _enclosureOpen,
                Content = fields,
                Margin = new Thickness(0, 0, 0, 4),
            };
            expander.Expanded += (_, _) => _enclosureOpen = true;
            expander.Collapsed += (_, _) => _enclosureOpen = false;
            EnclosurePanel.Children.Add(expander);
            var panel = fields;

            panel.Children.Add(Caption("İç ölçüler: uzunluk kartın X'i, genişlik Y'si boyunca, yükseklik tabandan kapağa. Kartın sol üst köşesi kutunun iç sol ve üst duvarlarından bu kadar içeride.", 0));
            void Field(string label, double value, Action<double> set, string what, double min = 0, double max = 2000) =>
                panel.Children.Add(Row(label, Number(value, v => { Edit($"Kutu: {what} {Fmt(v)} mm", () => set(v)); RunDrcNow(); }, min, max, true), 80));
            Field("İç uzunluk (mm)", box.InnerLength, v => box.InnerLength = v, "iç uzunluk", 1);
            Field("İç genişlik (mm)", box.InnerWidth, v => box.InnerWidth = v, "iç genişlik", 1);
            Field("İç yükseklik (mm)", box.InnerHeight, v => box.InnerHeight = v, "iç yükseklik", 1);
            Field("Kart sol duvardan (mm)", box.OffsetX, v => box.OffsetX = v, "kartın sol duvara uzaklığı", -2000);
            Field("Kart üst duvardan (mm)", box.OffsetY, v => box.OffsetY = v, "kartın üst duvara uzaklığı", -2000);
            Field("Ayak yüksekliği (mm)", box.Standoff, v => box.Standoff = v, "ayak yüksekliği");
            Field("Delikli uç boyu (mm)", box.LeadLength, v => box.LeadLength = v, "uç boyu");
            Field("Pay (mm)", box.Margin, v => box.Margin = v, "pay");

            panel.Children.Add(Overline("Duvardaki açıklıklar", 10));
            panel.Children.Add(Caption("Bir konnektör duvardan dışarı çıkacaksa (USB, DC jakı) oradaki açıklığı girin: gövdesi açıklığın içinde kalan parça duvara girmiş sayılmaz.", 0));
            foreach (var o in box.Openings.ToList())
            {
                var opening = o;
                var card = new Border { BorderBrush = B("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 6), Margin = new Thickness(0, 8, 0, 0) };
                var body = new StackPanel();
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var del = new Button { Content = "Sil", Style = S("Btn.Ghost"), Padding = new Thickness(8, 1, 8, 1), MinHeight = 22 };
                del.Click += (_, _) => { Edit($"'{opening.Name}' açıklığı kaldırıldı", () => box.Openings.Remove(opening)); RunDrcNow(); };
                DockPanel.SetDock(del, Dock.Right);
                head.Children.Add(del);
                head.Children.Add(Text(opening.Name, v => Edit($"Açıklık adı: {v.Trim()}", () => opening.Name = v.Trim())));
                body.Children.Add(head);
                body.Children.Add(Row("Duvar", Combo(new[] { EnclosureWall.Left, EnclosureWall.Right, EnclosureWall.Top, EnclosureWall.Bottom }, opening.Wall,
                    w => { Edit($"'{opening.Name}' açıklığı {WallName(w)} duvarda", () => opening.Wall = w); RunDrcNow(); }, WallName), 110));
                void OpeningField(string label, double value, Action<double> set, double min = 0) =>
                    body.Children.Add(Row(label, Number(value, v => { Edit($"'{opening.Name}' açıklığı: {label.ToLowerInvariant()} {Fmt(v)}", () => set(v)); RunDrcNow(); }, min, 2000, true), 80));
                OpeningField("Orta (kart mm)", opening.Position, v => opening.Position = v, -2000);
                OpeningField("Genişlik (mm)", opening.Width, v => opening.Width = v);
                OpeningField("Alt kenar, tabandan (mm)", opening.Bottom, v => opening.Bottom = v);
                OpeningField("Yükseklik (mm)", opening.Height, v => opening.Height = v);
                card.Child = body;
                panel.Children.Add(card);
            }

            panel.Children.Add(Buttons(Button("+ Açıklık ekle", (_, _) =>
            {
                string name = "Açıklık " + (box.Openings.Count + 1);
                Edit($"'{name}' eklendi", () => box.Openings.Add(new EnclosureOpening
                {
                    Name = name, Wall = EnclosureWall.Left, Position = board.Height / 2, Width = 12, Bottom = box.Standoff, Height = Math.Min(10, box.InnerHeight - box.Standoff),
                }));
                RunDrcNow();
            })));
        }

        private static string WallName(EnclosureWall w) => w switch
        {
            EnclosureWall.Left => Shell.Lang.T("sol (x−)"),
            EnclosureWall.Right => Shell.Lang.T("sağ (x+)"),
            EnclosureWall.Top => Shell.Lang.T("üst (y−)"),
            _ => Shell.Lang.T("alt (y+)"),
        };
    }
}
