using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RobotWin.Studio.Pcb
{
    /// <summary>Small themed form builders shared by the PCB page and its dialogs.</summary>
    internal static class PcbForms
    {
        public static T Res<T>(string key) where T : class => (T)Application.Current.FindResource(key);

        public static Style S(string key) => Res<Style>(key);

        public static Brush B(string key) => Res<Brush>(key);

        public static TextBlock Overline(string text, double top = 16) =>
            new TextBlock { Text = text, Style = S("Text.Overline"), Margin = new Thickness(0, top, 0, 6) };

        public static TextBlock Caption(string text, double top = 2) =>
            new TextBlock { Text = text, Style = S("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 2) };

        public static TextBlock Body(string text) => new TextBlock { Text = text, Style = S("Text.Body") };

        public static bool TryParse(string text, out double v) =>
            double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        public static string Fmt(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>A label on the left and an editor on the right (fixed editor width).</summary>
        public static Grid Row(string label, UIElement editor, double editorWidth = 96, string? tip = null)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(editorWidth) });
            var tb = new TextBlock { Text = label, Style = S("Text.Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            if (tip != null) tb.ToolTip = tip;
            row.Children.Add(tb);
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            return row;
        }

        /// <summary>A number box that commits on Enter or focus loss when the value is valid and changed.</summary>
        public static TextBox Number(double value, Action<double> commit, double min = 0, double max = double.MaxValue, bool allowZero = false)
        {
            var box = new TextBox { Text = Fmt(value) };
            double current = value;
            void Commit()
            {
                if (TryParse(box.Text, out double v) && (v > min || allowZero && v >= min) && v <= max)
                {
                    if (Math.Abs(v - current) > 1e-12)
                    {
                        current = v;
                        commit(v);
                    }
                }
                else
                {
                    box.Text = Fmt(current);
                }
            }

            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit();
            };
            box.LostKeyboardFocus += (_, _) => Commit();
            return box;
        }

        public static TextBox Text(string value, Action<string> commit)
        {
            var box = new TextBox { Text = value };
            string current = value;
            void Commit()
            {
                if (box.Text != current)
                {
                    current = box.Text;
                    commit(box.Text);
                }
            }

            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit();
            };
            box.LostKeyboardFocus += (_, _) => Commit();
            return box;
        }

        public static ComboBox Combo<T>(IEnumerable<T> items, T selected, Action<T> changed, Func<T, string>? label = null)
        {
            var box = new ComboBox();
            int index = 0, i = 0;
            foreach (var item in items)
            {
                box.Items.Add(new ComboBoxItem { Content = label != null ? label(item) : item?.ToString(), Tag = item });
                if (EqualityComparer<T>.Default.Equals(item, selected)) index = i;
                i++;
            }

            box.SelectedIndex = box.Items.Count > 0 ? index : -1;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is ComboBoxItem ci && ci.Tag is T t) changed(t);
            };
            return box;
        }

        public static CheckBox Switch(string label, bool value, Action<bool> changed)
        {
            var cb = new CheckBox { Content = label, IsChecked = value, Style = S("Switch"), Margin = new Thickness(0, 4, 0, 4) };
            cb.Click += (_, _) => changed(cb.IsChecked == true);
            return cb;
        }

        public static Button Button(string text, RoutedEventHandler click, bool primary = false, string? tip = null)
        {
            var b = new Button { Content = text, Style = S(primary ? "Btn.Primary" : "Btn"), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
            b.Click += click;
            return b;
        }

        public static WrapPanel Buttons(params UIElement[] items)
        {
            var p = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var i in items)
            {
                if (i is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 6, 6);
                p.Children.Add(i);
            }

            return p;
        }

        public static ToggleButton Chip(string text, bool on, RoutedEventHandler click)
        {
            var t = new ToggleButton { Content = text, IsChecked = on, Style = S("Chip"), Margin = new Thickness(0, 0, 4, 0) };
            t.Click += click;
            return t;
        }

        /// <summary>A themed modal window with a content panel and OK/Cancel buttons.</summary>
        public static Window MakeDialog(Window? owner, string title, double width, UIElement content, out Button ok, string okText = "Tamam", string? extraText = null, Action? extra = null)
        {
            var w = new Window
            {
                Title = title,
                Owner = owner,
                Width = width,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = B("B.Surface"),
                Foreground = B("B.Text")
            };
            var root = new DockPanel { Margin = new Thickness(20, 16, 20, 18) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            if (extraText != null && extra != null)
            {
                var x = new Button { Content = extraText, Style = S("Btn.Danger"), Margin = new Thickness(0, 0, 8, 0) };
                x.Click += (_, _) => extra();
                buttons.Children.Add(x);
            }

            var cancel = new Button { Content = "Vazgeç", Margin = new Thickness(0, 0, 8, 0), IsCancel = true, Style = S("Btn") };
            ok = new Button { Content = okText, Style = S("Btn.Primary"), IsDefault = true, Padding = new Thickness(14, 5, 14, 5) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            root.Children.Add(buttons);
            root.Children.Add(content);
            w.Content = root;
            return w;
        }
    }
}
