using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RobotTwin.CoreSim.Pcb;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [viewports] KiCad 8's Viewports panel, on the Layers tab under the presets: the view as it is (its middle and the
    /// width it shows) saved under a name, a click on a name goes back to it, × forgets it. Saved with the board.
    /// </summary>
    public partial class PcbPage
    {
        private void ViewportsSection(BoardDocument board)
        {
            LayersPanel.Children.Add(Overline("Görüş alanları", 8));
            foreach (var v in board.Viewports ?? new List<BoardViewport>())
            {
                var view = v;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var del = new Button
                {
                    Style = (Style)FindResource("Btn.Icon"), Width = 26, Height = 26, ToolTip = Shell.Lang.T("Görüş alanını sil"),
                    Content = new Theme.Icon { Data = (System.Windows.Media.Geometry)FindResource("I.Delete"), Size = 13 }
                };
                del.Click += (_, _) =>
                {
                    Edit($"'{view.Name}' görüş alanı silindi", () =>
                    {
                        board.Viewports!.Remove(view);
                        if (board.Viewports.Count == 0) board.Viewports = null;
                    });
                    UpdateLayersPanel();
                };
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                var go = new Button
                {
                    Content = view.Name, Style = (Style)FindResource("Btn.Ghost"), HorizontalContentAlignment = HorizontalAlignment.Left,
                    ToolTip = string.Format(System.Globalization.CultureInfo.CurrentCulture, "X {0:0.##} · Y {1:0.##} · {2:0.#} mm", view.X, view.Y, view.Width)
                };
                go.Click += (_, _) =>
                {
                    Canvas.ShowView(view);
                    W.Status = Shell.Lang.T("Görüş alanı") + ": " + view.Name;
                };
                row.Children.Add(go);
                LayersPanel.Children.Add(row);
            }

            var add = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
            var name = new TextBox { Text = NextViewName(board), ToolTip = Shell.Lang.T("Şimdiki görünümün adı") };
            var save = new Button { Content = Shell.Lang.T("Kaydet"), Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(4, 0, 0, 0), ToolTip = Shell.Lang.T("Şimdiki görünümü (yeri ve yakınlığı) bu adla kaydeder") };
            void Save()
            {
                string n = name.Text.Trim();
                if (n.Length == 0) return;
                var view = Canvas.CurrentView(n);
                Edit($"'{n}' görüş alanı kaydedildi", () =>
                {
                    board.Viewports ??= new List<BoardViewport>();
                    board.Viewports.RemoveAll(x => string.Equals(x.Name, n, StringComparison.CurrentCultureIgnoreCase));   // the same name: the new view
                    board.Viewports.Add(view);
                });
                UpdateLayersPanel();
            }

            save.Click += (_, _) => Save();
            name.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Save();
            };
            DockPanel.SetDock(save, Dock.Right);
            add.Children.Add(save);
            add.Children.Add(name);
            LayersPanel.Children.Add(add);
        }

        private static string NextViewName(BoardDocument board)
        {
            var names = new HashSet<string>((board.Viewports ?? new List<BoardViewport>()).Select(v => v.Name), StringComparer.CurrentCultureIgnoreCase);
            for (int i = 1; ; i++)
            {
                string n = Shell.Lang.T("Görünüm") + " " + i;
                if (!names.Contains(n)) return n;
            }
        }
    }
}
