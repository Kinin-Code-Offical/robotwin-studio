using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>Themed modal message box. Returns the index of the button pressed (-1 when closed).</summary>
    public static class Dialog
    {
        public static int Show(Window owner, string title, string message, params string[] buttons)
        {
            int result = -1;
            var window = new Window
            {
                Owner = owner,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20), MaxWidth = 460 };
            panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("Text.Title"), FontSize = 18 });
            panel.Children.Add(new TextBlock { Text = message, Style = (Style)Application.Current.FindResource("Text.Body"), Margin = new Thickness(0, 10, 0, 20) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                var b = new Button
                {
                    Content = buttons[i],
                    Margin = new Thickness(8, 0, 0, 0),
                    MinWidth = 96,
                    Style = (Style)Application.Current.FindResource(i == 0 ? "Btn.Primary" : "Btn"),
                    IsDefault = i == 0,
                    IsCancel = i == buttons.Length - 1 && buttons.Length > 1
                };
                b.Click += (_, _) =>
                {
                    result = index;
                    window.Close();
                };
                row.Children.Add(b);
            }

            panel.Children.Add(row);
            var card = new Border
            {
                Margin = new Thickness(20),
                Background = (Brush)Application.Current.FindResource("B.Overlay"),
                BorderBrush = (Brush)Application.Current.FindResource("B.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Effect = (System.Windows.Media.Effects.Effect)Application.Current.FindResource("E.Popup"),
                Child = panel
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) window.DragMove();
            };
            window.Content = card;
            if (Unattended.Active)
            {
                // A snapshot or a benchmark has no one to answer: the message shows (it is in the picture) and nothing waits.
                window.Show();
                return -1;
            }

            window.ShowDialog();
            return result;
        }

        /// <summary>Asks for one line of text (a file or folder name); null when cancelled.</summary>
        public static string? Prompt(Window owner, string title, string message, string initial = "")
        {
            string? result = null;
            var window = new Window
            {
                Owner = owner,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20), Width = 420 };
            panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("Text.Title"), FontSize = 18 });
            panel.Children.Add(new TextBlock { Text = message, Style = (Style)Application.Current.FindResource("Text.Body"), Margin = new Thickness(0, 10, 0, 8), TextWrapping = TextWrapping.Wrap });
            var box = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 18) };
            panel.Children.Add(box);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tamam", MinWidth = 96, IsDefault = true, Style = (Style)Application.Current.FindResource("Btn.Primary") };
            var cancel = new Button { Content = "Vazgeç", MinWidth = 96, IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Style = (Style)Application.Current.FindResource("Btn") };
            ok.Click += (_, _) =>
            {
                result = box.Text.Trim();
                window.Close();
            };
            cancel.Click += (_, _) => window.Close();
            row.Children.Add(ok);
            row.Children.Add(cancel);
            panel.Children.Add(row);
            window.Content = new Border
            {
                Margin = new Thickness(20),
                Background = (Brush)Application.Current.FindResource("B.Overlay"),
                BorderBrush = (Brush)Application.Current.FindResource("B.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Effect = (System.Windows.Media.Effects.Effect)Application.Current.FindResource("E.Popup"),
                Child = panel
            };
            window.Loaded += (_, _) =>
            {
                box.Focus();
                int dot = box.Text.LastIndexOf('.');
                box.Select(0, dot > 0 ? dot : box.Text.Length);
            };
            window.ShowDialog();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
    }
}
