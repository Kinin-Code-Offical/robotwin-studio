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
            window.ShowDialog();
            return result;
        }
    }
}
