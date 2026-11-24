using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [surface] Material, colour and surface finish of a world part: every material the physics knows with
    /// its numbers (density, stiffness, strength, heat), a colour picked from swatches or typed, and a finish
    /// that changes both the look (texture at real size, shine) and the thermal emissivity.
    /// </summary>
    public partial class WorldPage
    {
        private static readonly (string Name, string Hex)[] Swatches =
        {
            ("Beyaz", "#F2F2EE"), ("Açık gri", "#B8BCC4"), ("Koyu gri", "#4A4D55"), ("Siyah", "#1E1E22"),
            ("Kırmızı", "#E5484D"), ("Turuncu", "#F2A33B"), ("Sarı", "#F2C230"), ("Yeşil", "#3BA55C"),
            ("Mavi", "#3B82C4"), ("Lacivert", "#24407A"), ("Mor", "#7C5CC4"), ("Kraft", "#C9A56E")
        };

        private static string MaterialCard(string name)
        {
            var m = StructuralMaterial.For(name);
            string F(double v, string f) => v.ToString(f, RobotWin.Studio.Shell.Lang.Culture);
            string soft = !double.IsNaN(m.GlassTransition) ? $"camsı geçiş {F(m.GlassTransition, "0")} °C, " : string.Empty;
            string melt = !double.IsNaN(m.MeltingPoint) ? $"erime/bozunma {F(m.MeltingPoint, "0")} °C" : string.Empty;
            return $"{m.Name}: yoğunluk {F(m.Density, "0")} kg/m³ · E {F(m.E / 1e9, "0.##")} GPa · akma {F(m.Yield / 1e6, "0")} MPa · ısı iletimi {F(m.Conductivity, "0.###")} W/m·K · özgül ısı {F(m.SpecificHeat, "0")} J/kg·K · ışınım ε {F(m.Emissivity, "0.##")}" +
                   (soft.Length + melt.Length > 0 ? " · " + soft + melt : string.Empty);
        }

        /// <summary>[fasten] How a fixed joint is fastened: its strength, heat path and softening come with it.</summary>
        private void FasteningRow(WorldJoint j)
        {
            var combo = new ComboBox { Margin = new Thickness(14, 0, 0, 2) };
            foreach (var f in Fastening.All) combo.Items.Add(new ComboBoxItem { Content = f.Name, Tag = f.Id, IsSelected = string.Equals(f.Id, j.Fastening, StringComparison.OrdinalIgnoreCase) });
            if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem it && it.Tag is string id && id != j.Fastening) Edit("Bağlantı türü: " + it.Content, () => j.Fastening = id);
            };
            Inspector.Children.Add(combo);
            var spec = Fastening.For(j.Fastening);
            string soft = double.IsNaN(spec.SoftenC) ? string.Empty : $" {spec.SoftenC:0} °C'den sonra zayıflar, {spec.FailC:0} °C'de bırakır.";
            if (spec.Id.Length > 0) Inspector.Children.Add(new TextBlock { Text = spec.Note + soft, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 0, 6) });
        }

        private void SurfaceSection(WorldObject o)
        {
            if (o.Kind != WorldObjectKind.Body && o.Kind != WorldObjectKind.Model) return;
            Inspector.Children.Add(Caption("Görünüş ve yüzey"));

            // Colour: swatches (flat), a hex box, back to the material's own colour.
            var current = o.Color ?? string.Empty;
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (name, hex) in Swatches)
            {
                var col = (Color)ColorConverter.ConvertFromString(hex);
                bool on = string.Equals(current, hex, StringComparison.OrdinalIgnoreCase);
                var sw = new Button
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(0, 0, 4, 4),
                    ToolTip = name + " " + hex,
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(on ? 2 : 1),
                    BorderBrush = on ? (Brush)FindResource("B.Accent") : (Brush)FindResource("B.Border"),
                    Content = new Rectangle { Width = 16, Height = 16, Fill = new SolidColorBrush(col) }
                };
                sw.Click += (_, _) => Edit("Renk: " + name, () => o.Color = hex);
                row.Children.Add(sw);
            }

            var reset = new Button { Content = "Malzeme rengi", Height = 22, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 4), ToolTip = "Özel rengi kaldırır; malzemenin kendi rengi kullanılır" };
            reset.Click += (_, _) => Edit("Renk: malzeme rengi", () => o.Color = null);
            row.Children.Add(reset);
            Inspector.Children.Add(row);
            var color = new TextBox { Text = current, Tag = "#RRGGBB (boş = malzeme rengi)" };
            color.LostFocus += (_, _) =>
            {
                string c = color.Text.Trim();
                if (c == current) return;
                if (c.Length > 0)
                {
                    try
                    {
                        ColorConverter.ConvertFromString(c);
                    }
                    catch (FormatException)
                    {
                        color.Text = current;
                        return;
                    }
                }

                Edit("Renk değişti", () => o.Color = c.Length == 0 ? null : c);
            };
            Inspector.Children.Add(Labeled("Renk kodu", color));

            // Finish: look and thermal emissivity together.
            var finishes = SurfaceFinish.All.Select(f => (f.Id, f.Name)).ToArray();
            Inspector.Children.Add(Choice(o, "surface", "Yüzey", finishes, string.Empty));
            var mat = StructuralMaterial.For(o.Material);
            var finish = SurfaceFinish.For(o.Parameters.TryGetValue("surface", out var s) ? s : null);
            double eps = SurfaceFinish.EmissivityOf(o, mat.Emissivity);
            Inspector.Children.Add(Note((finish?.Note ?? "Malzemenin kendi yüzeyi.") + $" Isıl ışınım ε = {eps.ToString("0.##", RobotWin.Studio.Shell.Lang.Culture)}: ısı ağı, soğuma ve ısı kamerası bu değerle hesaplanır."));
        }
    }
}
