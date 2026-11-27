using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Editor;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [sheets] KiCad's hierarchy on the schematic page: the path bar over the sheet (back, up, forward and the path from
    /// the root, each step clickable; shown only once the design has sheets), the sheet tools' menu, and the in-place
    /// editors of a sheet's name and a sheet pin's name and shape.
    /// </summary>
    public partial class SchematicPage
    {
        private void InitSheets()
        {
            Canvas.SheetChanged += (_, _) =>
            {
                BuildSheetBar();
                UpdateChrome();
            };
            W.DocumentChanged += (_, _) => BuildSheetBar();
            W.DocumentReplaced += (_, _) =>
            {
                Canvas.EnterSheet(null, remember: false);
                BuildSheetBar();
            };
            BuildSheetBar();
        }

        /// <summary>The path bar: hidden while the design is one sheet (an uncluttered page), else back / up / forward and the path.</summary>
        private void BuildSheetBar()
        {
            var sch = W.Schematic;
            SheetBar.Visibility = sch.Sheets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            SheetPath.Children.Clear();
            if (sch.Sheets.Count == 0) return;
            Button Nav(string glyph, string tip, bool enabled, Action go)
            {
                var b = new Button { Content = glyph, Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(6, 0, 6, 0), MinHeight = 0, Height = 24, ToolTip = Shell.Lang.T(tip), IsEnabled = enabled };
                b.Click += (_, _) =>
                {
                    go();
                    Canvas.Focus();
                };
                return b;
            }

            SheetPath.Children.Add(Nav("◀", "Geri (Alt+←)", Canvas.CanSheetBack, Canvas.SheetBack));
            SheetPath.Children.Add(Nav("▲", "Üst sayfa (Alt+↑ · Alt+Backspace)", Canvas.CanSheetUp, Canvas.SheetUp));
            SheetPath.Children.Add(Nav("▶", "İleri (Alt+→)", Canvas.CanSheetForward, Canvas.SheetForward));
            SheetPath.Children.Add(new Border { Width = 1, Height = 14, Background = (Brush)FindResource("B.Border"), Margin = new Thickness(6, 0, 6, 0) });
            var chain = sch.SheetChain(Canvas.CurrentSheet);
            void Crumb(string text, string? uid, bool current)
            {
                var b = new Button
                {
                    Content = text, Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(6, 0, 6, 0), MinHeight = 0, Height = 24,
                    FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, IsEnabled = !current
                };
                b.Click += (_, _) =>
                {
                    Canvas.EnterSheet(uid);
                    Canvas.Focus();
                };
                SheetPath.Children.Add(b);
            }

            Crumb(Shell.Lang.T("Kök"), null, chain.Count == 0);
            for (int i = 0; i < chain.Count; i++)
            {
                SheetPath.Children.Add(new TextBlock { Text = "/", VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("B.TextSecondary") });
                Crumb(chain[i].Name, chain[i].Uid, i == chain.Count - 1);
            }

            // the sheets below this one, to go straight in
            var below = sch.SheetsOn(Canvas.CurrentSheet).ToList();
            if (below.Count == 0) return;
            var down = new Button { Content = "▾", Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(6, 0, 6, 0), MinHeight = 0, Height = 24, ToolTip = Shell.Lang.T("Alt sayfalar") };
            down.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = down, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                foreach (var s in below)
                {
                    var item = new MenuItem { Header = s.Name };
                    var uid = s.Uid;
                    item.Click += (_, _) => Canvas.EnterSheet(uid);
                    menu.Items.Add(item);
                }

                menu.IsOpen = true;
            };
            SheetPath.Children.Add(down);
        }

        /// <summary>[sheets] The sheet tools: a sheet, a pin on its edge, a hierarchical label.</summary>
        private void OnSheetTool(object sender, RoutedEventArgs e)
        {
            var button = (System.Windows.Controls.Primitives.ToggleButton)sender;
            button.IsChecked = Canvas.Tool is EditorTool.Sheet or EditorTool.SheetPin or EditorTool.HierLabel or EditorTool.Bus or EditorTool.BusEntry;
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var (text, tool) in new[]
                     {
                         ("Hiyerarşik sayfa  (S)", EditorTool.Sheet), ("Sayfa pini", EditorTool.SheetPin), ("Hiyerarşik etiket  (H)", EditorTool.HierLabel),
                         ("Bus  (B)", EditorTool.Bus), ("Bus girişi  (Z)", EditorTool.BusEntry)
                     })
            {
                var item = new MenuItem { Header = Shell.Lang.T(text), IsChecked = Canvas.Tool == tool };
                var chosen = tool;
                item.Click += (_, _) =>
                {
                    Canvas.Tool = chosen;
                    Canvas.Focus();
                };
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        /// <summary>[sheets] A sheet's name typed in place, over its top-left corner.</summary>
        private void EditSheetName(SchematicSheet sheet)
        {
            if (W.Simulation.IsActive) return;
            var corner = Canvas.ToScreenPoint(sheet.Position);
            var origin = Canvas.TranslatePoint(new Point(0, 0), _overlay);
            var anchor = new Rect(corner.X + origin.X, corner.Y + origin.Y - 26, 180, 22);
            ShowInlineEditor(anchor, sheet.Name, Shell.Lang.T("Sayfa adı · alt sayfanın yerel hatları /Ad/hat olur · Enter / Esc"), value =>
            {
                if (value.Length == 0 || value == sheet.Name) return;
                W.BeginChange();
                sheet.Name = value;
                W.CommitChange(Shell.Lang.T("Sayfa") + ": " + value);
                Canvas.InvalidateVisual();
            });
        }

        /// <summary>[sheets] A sheet pin's name typed in place (its direction is in the sheet's right-click menu).</summary>
        private void EditSheetPin(SchematicSheet sheet, SheetPin pin)
        {
            if (W.Simulation.IsActive) return;
            var at = Canvas.ToScreenPoint(pin.Position);
            var origin = Canvas.TranslatePoint(new Point(0, 0), _overlay);
            var anchor = new Rect(at.X + origin.X + 8, at.Y + origin.Y - 11, 150, 22);
            ShowInlineEditor(anchor, pin.Name, Shell.Lang.T("Sayfa pini · alt sayfadaki aynı adlı hiyerarşik etikete bağlanır · Enter / Esc"), value =>
            {
                if (value.Length == 0 || value == pin.Name) return;
                W.BeginChange();
                pin.Name = value;
                W.CommitChange(Shell.Lang.T("Sayfa pini") + ": " + value);
                Canvas.InvalidateVisual();
            });
        }
    }
}
