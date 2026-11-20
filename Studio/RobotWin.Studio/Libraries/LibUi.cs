using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Libraries;
using RobotWin.Studio.Editor;

namespace RobotWin.Studio.Libraries
{
    // [libs] Small control factory so the library windows match the app theme (flat colours).

    internal static class LibUi
    {
        public static Brush B(string key) => (Brush)Application.Current.FindResource(key);

        public static Style S(string key) => (Style)Application.Current.FindResource(key);

        public static TextBlock Overline(string text, double top = 16) =>
            new TextBlock { Text = text, Style = S("Text.Overline"), Margin = new Thickness(0, top, 0, 6) };

        public static TextBlock Title(string text) => new TextBlock { Text = text, Style = S("Text.Title"), TextWrapping = TextWrapping.Wrap };

        public static TextBlock Body(string text) => new TextBlock { Text = text, Style = S("Text.Body"), TextWrapping = TextWrapping.Wrap };

        public static TextBlock Caption(string text) => new TextBlock { Text = text, Style = S("Text.Caption"), TextWrapping = TextWrapping.Wrap };

        public static TextBlock Mono(string text) => new TextBlock { Text = text, Style = S("Text.Mono"), TextWrapping = TextWrapping.Wrap };

        public static Button Primary(string text, Action click)
        {
            var b = new Button { Content = text, Style = S("Btn.Primary"), Padding = new Thickness(16, 7, 16, 7) };
            b.Click += (_, _) => click();
            return b;
        }

        public static Button Btn(string text, Action click, string? tip = null, bool ghost = false)
        {
            var b = new Button { Content = text, Margin = new Thickness(6, 0, 0, 0), ToolTip = tip };
            if (ghost) b.Style = S("Btn.Ghost");
            b.Click += (_, _) => click();
            return b;
        }

        public static StackPanel Row(params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            foreach (var c in children) p.Children.Add(c);
            return p;
        }

        /// <summary>"Label" above a text box.</summary>
        public static TextBox Field(Panel into, string label, string value, string? tip = null)
        {
            into.Children.Add(new TextBlock { Text = label, Style = S("Text.Caption"), Margin = new Thickness(0, 6, 0, 3) });
            var box = new TextBox { Text = value ?? string.Empty, ToolTip = tip };
            into.Children.Add(box);
            return box;
        }

        public static ComboBox Combo<T>(IEnumerable<T> items, T selected, Func<T, string> text, Action<T> changed)
        {
            var list = items.ToList();
            var combo = new ComboBox { MinWidth = 120 };
            foreach (var i in list) combo.Items.Add(new ComboBoxItem { Content = text(i), Tag = i });
            int index = list.FindIndex(i => EqualityComparer<T>.Default.Equals(i, selected));
            combo.SelectedIndex = Math.Max(0, index);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: T t }) changed(t);
            };
            return combo;
        }

        public static Border Card(UIElement child, double padding = 12) => new Border
        {
            BorderBrush = B("B.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(padding),
            Background = B("B.Surface"),
            Child = child
        };

        public static Border Chip(string text, string brushKey)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 1, 7, 2),
                Margin = new Thickness(6, 0, 0, 0),
                BorderBrush = B(brushKey),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 11, Foreground = B(brushKey) }
            };
        }

        public static string SeverityBrush(IssueSeverity s) => s switch
        {
            IssueSeverity.Error => "B.Danger",
            IssueSeverity.Warning => "B.Warning",
            _ => "B.TextMuted"
        };

        /// <summary>A scrolling list of findings, coloured by severity.</summary>
        public static UIElement Issues(IEnumerable<ImportIssue> issues, double maxHeight = 220)
        {
            var panel = new StackPanel();
            int n = 0;
            foreach (var i in issues.OrderByDescending(i => i.Severity))
            {
                n++;
                panel.Children.Add(new TextBlock
                {
                    Text = i.ToString(),
                    Foreground = B(SeverityBrush(i.Severity)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1)
                });
            }

            if (n == 0) panel.Children.Add(Caption("Sorun yok."));
            return new ScrollViewer { Content = panel, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        /// <summary>A themed window with a title bar row and a body.</summary>
        public static Window Window(Window? owner, string title, double width, double height)
        {
            var w = new Window
            {
                Title = title,
                Width = width,
                Height = height,
                MinWidth = Math.Min(width, 760),
                MinHeight = Math.Min(height, 480),
                Owner = owner,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Background = B("B.Base"),
                Foreground = B("B.Text"),
                ShowInTaskbar = false
            };
            return w;
        }

        /// <summary>Shows a long report in a read-only monospaced window.</summary>
        public static void Report(Window? owner, string title, string text)
        {
            var w = Window(owner, title, 820, 600);
            w.Content = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                FontFamily = (FontFamily)Application.Current.FindResource("F.Mono"),
                FontSize = 12,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.NoWrap
            };
            w.ShowDialog();
        }
    }

    /// <summary>Draws the schematic symbol of a part definition that is not (yet) in the catalog.</summary>
    internal sealed class DefinitionPreview : FrameworkElement
    {
        private PartDefinition? _def;
        private IReadOnlyDictionary<string, string> _parameters = new Dictionary<string, string>();

        public void Show(PartDefinition? def, IReadOnlyDictionary<string, string>? parameters)
        {
            _def = def;
            _parameters = parameters ?? new Dictionary<string, string>();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            var def = _def;
            if (def == null || ActualWidth < 4) return;
            double minX = -def.HalfWidth, maxX = def.HalfWidth, minY = -def.HalfHeight, maxY = def.HalfHeight;
            foreach (var pin in def.Pins)
            {
                minX = Math.Min(minX, pin.X);
                maxX = Math.Max(maxX, pin.X);
                minY = Math.Min(minY, pin.Y);
                maxY = Math.Max(maxY, pin.Y);
            }

            double w = maxX - minX + 1.6, h = maxY - minY + 1.6;
            double scale = Math.Min(ActualWidth / w, ActualHeight / h);
            scale = Math.Min(scale, 26);
            var part = new SchematicPart { PartId = def.Id };
            var origin = new Point(ActualWidth / 2 - (minX + maxX) / 2 * scale, ActualHeight / 2 - (minY + maxY) / 2 * scale);
            dc.PushTransform(new MatrixTransform(SymbolRenderer.PartMatrix(part, scale, origin)));
            try
            {
                SymbolRenderer.Draw(dc, def, _parameters, scale, SymbolRenderer.SymbolColor, default);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
            }

            dc.Pop();
            // Pad numbers next to the pin tips (what the PCB will connect).
            var ink = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
            foreach (var pin in def.Pins)
            {
                var pt = new Point(origin.X + pin.X * scale, origin.Y + pin.Y * scale);
                var ft = new FormattedText(pin.Pad, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(8, scale * 0.6), ink, 1.0);
                dc.DrawText(ft, new Point(pin.X < 0 ? pt.X - ft.Width - 2 : pt.X + 2, pt.Y - ft.Height - 1));
            }
        }
    }
}
