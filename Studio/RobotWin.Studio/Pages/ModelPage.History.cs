using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using RobotWin.Studio.Modeling;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [history] SolidWorks' FeatureManager for a part, in the inspector: its base and the features after it (cuts, joins,
    /// intersections, holes), each with a box to suppress it, up / down, edit (the tool's own parameters and place, a
    /// hole's size, kind and depth), give the tool back as an object, and delete. The part is rebuilt from them each
    /// time (CoreSim Modeling/ModelFeatures.cs).
    /// </summary>
    public partial class ModelPage
    {
        /// <summary>The feature open for editing: its part and its place in the list.</summary>
        private (string Id, int Index)? _openFeature;

        private void FeatureHistorySection(ModelObject o)
        {
            var list = o.Features;
            if (list == null || list.Count == 0) return;
            Section("Özellik geçmişi", $"{list.Count} özellik");
            Caption("Taban: " + (o.Primitive != null ? PrimitiveSchema.Name(o.Primitive.Kind) + " (ölçüleri yukarıda)" : "düzenlenebilir ağ") + ". Özellikler sırayla uygulanır; kutu bir özelliği bastırır.");
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                var f = list[i];
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var on = new CheckBox { IsChecked = !f.Suppressed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = Shell.Lang.T("Etkin; boşsa bastırılmış (parçaya uygulanmaz)") };
                on.Click += (_, _) => _ed.Do(f.Suppressed ? "Özellik etkinleştirildi" : "Özellik bastırıldı", () => f.Suppressed = on.IsChecked != true);
                DockPanel.SetDock(on, Dock.Left);
                row.Children.Add(on);
                bool open = _openFeature == (o.Id, index);
                var tools = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(tools, Dock.Right);
                void Tool(string text, Action click, string tip, bool enabled = true)
                {
                    var b = SmallButton(text, click, tip);
                    b.Margin = new Thickness(2, 0, 0, 0);
                    b.Padding = new Thickness(6, 2, 6, 2);
                    b.IsEnabled = enabled;
                    tools.Children.Add(b);
                }

                Tool("↑", () => _ed.Do("Özellik yukarı alındı", () => ModelFeatures.Move(o, index, -1)), "Bir önceki özellikten önce uygulansın", index > 0);
                Tool("↓", () => _ed.Do("Özellik aşağı alındı", () => ModelFeatures.Move(o, index, 1)), "Bir sonraki özellikten sonra uygulansın", index < list.Count - 1);
                Tool(open ? "Kapat" : "Düzenle", () =>
                {
                    _openFeature = open ? null : (o.Id, index);
                    BuildProperties();
                }, "Özelliğin ölçülerini ve yerini değiştirin");
                if (f.Kind is not (FeatureKind.Hole or FeatureKind.Rib))
                    Tool("Ayır", () => _ed.Do("Araç geri alındı", () =>
                    {
                        var t = ModelFeatures.Extract(_ed.Doc, o, index);
                        _openFeature = null;
                        if (t != null) _ed.Select(t.Id);
                    }, ModelChange.All), "Özelliği kaldırıp aracını yeniden ayrı bir nesne yapar");
                Tool("Sil", () => _ed.Do("Özellik silindi", () =>
                {
                    ModelFeatures.Remove(o, index);
                    _openFeature = null;
                }, ModelChange.All), "Özelliği geçmişten çıkarır");
                row.Children.Add(tools);
                var name = new TextBlock { Text = (index + 1) + ". " + f.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                if (f.Suppressed)
                {
                    name.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
                    name.TextDecorations = TextDecorations.Strikethrough;
                }

                row.Children.Add(name);
                Props.Children.Add(row);
                if (open) FeatureEditor(f);
            }

            var bake = SmallButton("Geçmişi ağa uygula", _ed.ConvertToMesh, "Taban ve özellikler tek bir düzenlenebilir ağ olur; parametreler kaybolur");
            bake.Margin = new Thickness(0, 6, 0, 0);
            bake.HorizontalAlignment = HorizontalAlignment.Left;
            Props.Children.Add(bake);
        }

        /// <summary>Scrolls the properties to a section by its title (for the snapshots).</summary>
        private void ScrollPropsTo(string title)
        {
            if (Props.Parent is not ScrollViewer scroll) return;
            Props.UpdateLayout();
            string text = Shell.Lang.T(title);
            TextBlock? Find(DependencyObject at)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(at); i++)
                {
                    var child = VisualTreeHelper.GetChild(at, i);
                    if (child is TextBlock tb && tb.Text == text) return tb;
                    if (Find(child) is { } found) return found;
                }

                return null;
            }

            if (Find(Props) is { } head) scroll.ScrollToVerticalOffset(Math.Max(0, head.TransformToAncestor(Props).Transform(new Point(0, 0)).Y - 10));
        }

        private void FeatureEditor(ModelFeature f)
        {
            var host = new StackPanel { Margin = new Thickness(22, 2, 0, 8) };
            Props.Children.Add(host);
            if (f.Primitive is { } spec)
            {
                foreach (var p in PrimitiveSchema.For(spec))
                {
                    var ps = p;
                    double step = ps.Integer ? 1 : ps.Unit == "°" ? 5 : 0.5;
                    Num(host, ps.Label, spec.Get(ps.Key), ps.Unit, v => spec.Set(ps.Key, ps.Clamp(v)), ps.Label, true, step, 3, ps.Min, ps.Max, ps.Integer);
                }
            }

            if (f.Drill is { } drill)
            {
                var size = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
                foreach (var s in HoleWizard.Sizes) size.Items.Add(s.Name);
                size.SelectedItem = drill.Size;
                size.SelectionChanged += (_, _) =>
                {
                    if (size.SelectedItem is string pick && pick != drill.Size) _ed.Do("Delik ölçüsü değişti", () =>
                    {
                        drill.Size = pick;
                        f.Name = "Delik: " + HoleWizard.Describe(drill);
                    });
                };
                host.Children.Add(size);
                var kind = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
                foreach (var (type, label) in HoleTypes) kind.Items.Add(new ComboBoxItem { Content = Shell.Lang.T(label), Tag = type });
                kind.SelectedItem = kind.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (HoleType)i.Tag == drill.Type);
                kind.SelectionChanged += (_, _) =>
                {
                    if (kind.SelectedItem is ComboBoxItem { Tag: HoleType pick } && pick != drill.Type) _ed.Do("Delik türü değişti", () =>
                    {
                        drill.Type = pick;
                        f.Name = "Delik: " + HoleWizard.Describe(drill);
                    });
                };
                host.Children.Add(kind);
                Num(host, "Derinlik (0: boydan boya)", drill.Depth, "mm", v =>
                {
                    drill.Depth = Math.Max(0, v);
                    f.Name = "Delik: " + HoleWizard.Describe(drill);
                }, "Delik derinliği", true, 0.5, 2, 0, 1e5);
            }

            if (f.Rib is { Length: 9 } r)
            {
                // [rib] its line's two ends and its thickness, in the part's frame
                string[] names = { "Başlangıç X", "Başlangıç Y", "Başlangıç Z", "Bitiş X", "Bitiş Y", "Bitiş Z" };
                for (int i = 0; i < 6; i++)
                {
                    int k = i;
                    Num(host, names[i], r[k], "mm", v => r[k] = v, "Kaburga değişti", true, 0.5, 3);
                }

                Num(host, "Kalınlık", f.Thickness, "mm", v => f.Thickness = Math.Max(0.01, v), "Kaburga kalınlığı", true, 0.5, 2, 0.01, 1e4);
                return;
            }

            // its place in the part's frame
            var m = f.Matrix ??= new[] { 1.0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };
            Num(host, "Konum X", m[9], "mm", v => m[9] = v, "Özellik taşındı", true, 0.5, 3);
            Num(host, "Konum Y", m[10], "mm", v => m[10] = v, "Özellik taşındı", true, 0.5, 3);
            Num(host, "Konum Z", m[11], "mm", v => m[11] = v, "Özellik taşındı", true, 0.5, 3);
        }
    }
}
