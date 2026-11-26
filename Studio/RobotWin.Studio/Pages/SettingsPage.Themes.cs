using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [theme] The theme gallery of the settings: every theme as a small picture of the window in its colours (dark ones,
    /// light ones, the custom one), the custom theme's hue, tint and lightness, and the accent colours as dots. A click
    /// applies it at once.
    /// </summary>
    public partial class SettingsPage
    {
        private Border? _customTile;

        private void ThemeCard()
        {
            Card("Tema", "Arayüzün ve editörlerin renkleri; seçince hemen uygulanır. Açık temalarda şema ve kod düzenleyici de açılır; 3B görünümler kendi ışığıyla kalır.", c =>
            {
                c.Children.Add(Group("Koyu"));
                c.Children.Add(Tiles(Themes.All.Where(t => !t.Light && t.Id != "ozel").Select(t => t.Id)));
                c.Children.Add(Group("Açık"));
                c.Children.Add(Tiles(Themes.All.Where(t => t.Light).Select(t => t.Id)));
                c.Children.Add(Group("Özel"));
                var custom = new WrapPanel();
                _customTile = Tile("ozel");
                custom.Children.Add(_customTile);
                var knobs = new StackPanel { Width = 300, Margin = new Thickness(8, 0, 0, 10), VerticalAlignment = VerticalAlignment.Top };
                knobs.Children.Add(Slider("Ton", S.ThemeHue, 0, 360, v => S.ThemeHue = v, "Rengin kendisi: 0 kırmızı, 30 turuncu, 60 sarı, 120 yeşil, 200 mavi, 270 mor, 330 pembe"));
                knobs.Children.Add(Slider("Doygunluk", S.ThemeTint, 0, 80, v => S.ThemeTint = v, "Zeminlerin ne kadar renkli olduğu: 0 gri, 30 hafif, 60 belirgin"));
                var light = new CheckBox { Content = "Açık zemin", IsChecked = S.ThemeLight, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 4, 0, 0) };
                light.Click += (_, _) =>
                {
                    S.ThemeLight = light.IsChecked == true;
                    S.Theme = "ozel";
                    S.Save();
                    ApplyTheme();
                };
                knobs.Children.Add(light);
                custom.Children.Add(knobs);
                c.Children.Add(custom);

                c.Children.Add(Group("Vurgu rengi"));
                c.Children.Add(new TextBlock { Text = "Seçili öğeler, odak, canlı değerler ve bağlantılar bu renkle görünür. Açık temalarda karşıtlık için koyulaşır.", Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
                var dots = new WrapPanel();
                foreach (var (id, name, _) in Themes.Accents) dots.Children.Add(AccentDot(id, name));
                c.Children.Add(dots);
            });
        }

        private TextBlock Group(string text) => new TextBlock { Text = text, Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 10, 0, 6) };

        private WrapPanel Tiles(System.Collections.Generic.IEnumerable<string> ids)
        {
            var wrap = new WrapPanel();
            foreach (var id in ids) wrap.Children.Add(Tile(id));
            return wrap;
        }

        /// <summary>A theme as a small picture of the window: rail, panel, a card with two lines of text and an accent mark.</summary>
        private Border Tile(string id)
        {
            var t = Themes.Build(id);
            var accent = Themes.AccentFor(t, S.Accent);
            Brush B(string token) => new SolidColorBrush(t[token]);
            var picture = new Grid { Width = 128, Height = 72, ClipToBounds = true };
            picture.Children.Add(new Border { Background = B("Canvas") });
            picture.Children.Add(new Border { Background = B("Base"), Width = 18, HorizontalAlignment = HorizontalAlignment.Left });
            picture.Children.Add(new Border { Background = B("Surface"), Height = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 0, 0, 0), BorderBrush = B("Border"), BorderThickness = new Thickness(0, 0, 0, 1) });
            var card = new Border { Background = B("Raised"), BorderBrush = B("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Margin = new Thickness(26, 20, 10, 10) };
            var lines = new StackPanel { Margin = new Thickness(6, 6, 6, 0) };
            lines.Children.Add(new Border { Background = B("Text"), Height = 4, Width = 44, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) });
            lines.Children.Add(new Border { Background = B("TextMuted"), Height = 3, Width = 64, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 5, 0, 0) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
            row.Children.Add(new Border { Background = B("Primary"), Height = 8, Width = 22, CornerRadius = new CornerRadius(2) });
            row.Children.Add(new Ellipse { Fill = new SolidColorBrush(accent), Width = 8, Height = 8, Margin = new Thickness(6, 0, 0, 0) });
            lines.Children.Add(row);
            card.Child = lines;
            picture.Children.Add(card);
            bool selected = S.Theme == id;
            var body = new StackPanel();
            body.Children.Add(new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = picture });
            body.Children.Add(new TextBlock { Text = t.Name, Margin = new Thickness(2, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal });
            var tile = new Border
            {
                Child = body, Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2), BorderBrush = selected ? (Brush)FindResource("B.Accent") : Brushes.Transparent,
                ToolTip = t.Name + (t.Light ? " · açık" : " · koyu"), Tag = "tema " + t.Name
            };
            tile.MouseLeftButtonUp += (_, _) =>
            {
                if (S.Theme == id) return;
                S.Theme = id;
                S.Save();
                ApplyTheme();
            };
            return tile;
        }

        /// <summary>A slider for the custom theme: it recolours the window while dragged and is saved when let go.</summary>
        private FrameworkElement Slider(string label, double value, double min, double max, Action<double> set, string help)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6), ToolTip = help };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("B.TextSecondary") });
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = 1, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            var shown = new TextBox { Width = 56, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(shown, 2);
            g.Children.Add(slider);
            g.Children.Add(shown);
            slider.ValueChanged += (_, e) =>
            {
                if (_building) return;
                set(Math.Round(e.NewValue));
                S.Theme = "ozel";
                Themes.Apply();   // the window follows the drag; the page is rebuilt when it ends
                if (_customTile?.Parent is Panel p)
                {
                    int at = p.Children.IndexOf(_customTile);
                    p.Children.RemoveAt(at);
                    _customTile = Tile("ozel");
                    p.Children.Insert(at, _customTile);
                }
            };
            slider.LostMouseCapture += (_, _) =>
            {
                S.Save();
                ApplyTheme();
            };
            slider.KeyUp += (_, _) => S.Save();
            RobotWin.Studio.Theme.SliderInput.Attach(slider, shown, "0", committed: () =>   // [slider-input] a typed value is saved like a drag let go
            {
                S.Save();
                ApplyTheme();
            });
            return g;
        }

        /// <summary>An accent colour as a dot (a ring around the one in use).</summary>
        private FrameworkElement AccentDot(string id, string name)
        {
            var color = Themes.AccentFor(Themes.Current, id);
            bool selected = S.Accent == id;
            var dot = new Grid { Width = 26, Height = 26 };
            dot.Children.Add(new Ellipse { Fill = new SolidColorBrush(color), Width = 18, Height = 18 });
            if (id == "tema") dot.Children.Add(new TextBlock { Text = "T", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("B.OnPrimary"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var ring = new Border
            {
                Child = dot, Width = 32, Height = 32, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 6, 6),
                BorderBrush = selected ? (Brush)FindResource("B.Text") : Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = name, Tag = "vurgu " + name
            };
            ring.MouseLeftButtonUp += (_, _) =>
            {
                if (S.Accent == id) return;
                S.Accent = id;
                S.Save();
                ApplyTheme();
            };
            return ring;
        }
    }
}
