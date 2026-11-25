using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [spice] A diode's or transistor's manufacturer model: paste the vendor's <c>.model</c> card and its numbers become the
    /// part's parameters in one undoable change; the panel says which SPICE parameters our model does not use.
    /// </summary>
    public partial class SchematicPage
    {
        private void BuildSpiceSection(SchematicPart part, bool locked)
        {
            if (part.PartId is not ("diode" or "schottky" or "zener" or "npn" or "pnp" or "nmos" or "pmos")) return;
            var button = new Button { Content = "SPICE modeli yapıştır…", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), IsEnabled = !locked,
                ToolTip = "Üreticinin .model kartını yapıştırın (örneğin .model 1N4148 D(IS=2.52n N=1.752 ...)): Is, N, Rs, BV, BF, VAF, Cje, VTO … parçanın değerleri olur" };
            button.Click += (_, _) => PasteSpiceModel(part);
            Inspector.Children.Add(button);
        }

        private void PasteSpiceModel(SchematicPart part)
        {
            var box = new TextBox { AcceptsReturn = true, Height = 180, Width = 520, FontFamily = (System.Windows.Media.FontFamily)FindResource("F.Mono"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap };
            if (Clipboard.ContainsText() && Clipboard.GetText().IndexOf(".model", System.StringComparison.OrdinalIgnoreCase) >= 0) box.Text = Clipboard.GetText();
            var window = new Window
            {
                Title = $"SPICE modeli — {part.RefDes}",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = (System.Windows.Media.Brush)FindResource("B.Surface")
            };
            var stack = new StackPanel { Margin = new Thickness(14) };
            stack.Children.Add(new TextBlock { Text = "Üreticinin .model kartı (birden çok satır ve + devam satırları olabilir):", Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(box);
            var note = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Add(note);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var apply = new Button { Content = "Uygula", Padding = new Thickness(12, 4, 12, 4), Style = (Style)FindResource("Btn.Primary") };
            var cancel = new Button { Content = "Vazgeç", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0) };
            cancel.Click += (_, _) => window.Close();
            apply.Click += (_, _) =>
            {
                var model = SpiceModels.Parse(box.Text).FirstOrDefault(m => SpiceModels.Fits(m, part.PartId));
                if (model == null)
                {
                    note.Text = "Bu parçaya uyan bir .model kartı bulunamadı (diyot için D, NPN/PNP transistör, NMOS/PMOS).";
                    return;
                }

                var (values, unused) = SpiceModels.ToPart(model);
                W.BeginChange();
                foreach (var kv in values) part.Parameters[kv.Key] = kv.Value;
                W.CommitChange($"{part.RefDes}: SPICE modeli {model.Name}");
                W.Status = $"{part.RefDes} artık {model.Name}: {values.Count - 1} değer alındı" + (unused.Count > 0 ? $"; modelimizde olmayanlar: {string.Join(", ", unused.Take(12))}" : string.Empty);
                window.Close();
                BuildInspector();
            };
            row.Children.Add(apply);
            row.Children.Add(cancel);
            stack.Children.Add(row);
            window.Content = stack;
            window.ShowDialog();
        }
    }
}
