using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [versions] The open document's earlier versions in the backup folder (its autosaves and how it was before each
    /// save), newest first: each opens as a copy beside it, or comes back in its place (the version it replaces is kept
    /// too). Dosya → Önceki sürümler.
    /// </summary>
    internal sealed class VersionsWindow : Window
    {
        private readonly MainWindow _main;

        public VersionsWindow(MainWindow main, string name, IReadOnlyList<BackupVersion> versions, bool canRestore)
        {
            _main = main;
            Owner = main;
            Title = "Önceki sürümler";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = R<FontFamily>("F.Ui");
            SetResourceReference(ForegroundProperty, "B.Text");

            var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20), Width = 600 };
            panel.Children.Add(new TextBlock { Text = "Önceki sürümler", Style = R<Style>("Text.Title"), FontSize = 18 });
            panel.Children.Add(new TextBlock
            {
                Text = versions.Count == 0
                    ? $"{name}: yedek klasöründe bu belgenin kopyası yok. Otomatik kayıt ve kaydetmeden önceki sürümü saklama Ayarlar → Kaydetme ve yedekleme'de açılır."
                    : $"{name}: {versions.Count} kopya, en yenisi üstte. Otomatik kayıtlar ve her kaydetmeden önceki hâli.",
                Style = R<Style>("Text.Caption"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 14)
            });

            var list = new StackPanel();
            foreach (var v in versions) list.Children.Add(Row(v, canRestore));
            panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) });

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var folder = new Button { Content = "Yedek klasörü", Style = R<Style>("Btn.Ghost"), MinWidth = 110 };
            folder.Click += (_, _) => MainWindow.RevealInExplorer(Backups.Folder, true);
            row.Children.Add(folder);
            var close = new Button { Content = "Kapat", MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsCancel = true, IsDefault = true };
            close.Click += (_, _) => Close();
            row.Children.Add(close);
            panel.Children.Add(row);

            var card = new Border
            {
                Margin = new Thickness(20),
                Background = R<Brush>("B.Overlay"),
                BorderBrush = R<Brush>("B.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Child = panel
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Content = card;
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");
        }

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        /// <summary>"bugün 14:32", "dün 09:15", "23 Kasım 18:05" (with the year when it is not this one).</summary>
        internal static string When(DateTime time)
        {
            var culture = Lang.Culture;
            string clock = time.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (time.Date == DateTime.Today) return "bugün " + clock;
            if (time.Date == DateTime.Today.AddDays(-1)) return "dün " + clock;
            return time.ToString(time.Year == DateTime.Today.Year ? "d MMMM" : "d MMMM yyyy", culture) + " " + clock;
        }

        private FrameworkElement Row(BackupVersion v, bool canRestore)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = When(v.Time), FontWeight = FontWeights.SemiBold });
            string kind = v.BeforeSave ? "Kaydetmeden önceki hâli" : "Otomatik kayıt";
            text.Children.Add(new TextBlock { Text = kind + " · " + Size(v.File.Length), Style = R<Style>("Text.Caption"), Margin = new Thickness(0, 2, 0, 0) });
            grid.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var copy = new Button { Content = "Kopya olarak aç", Padding = new Thickness(10, 3, 10, 3), ToolTip = "Bu sürümü yeni bir sekmede açar; şu anki belgeye dokunulmaz." };
            copy.Click += (_, _) =>
            {
                Close();
                _main.OpenVersionCopy(v);
            };
            buttons.Children.Add(copy);
            if (canRestore)
            {
                var restore = new Button { Content = "Geri yükle", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Dosya bu sürüme döner; şu anki hâli de önceki sürüm olarak saklanır." };
                restore.Click += (_, _) =>
                {
                    if (_main.RestoreVersion(v)) Close();
                };
                buttons.Children.Add(restore);
            }

            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
            return grid;
        }

        private static string Size(long bytes) => bytes >= 1024 * 1024
            ? (bytes / 1048576.0).ToString("0.0", Lang.Culture) + " MB"
            : Math.Max(1, bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
    }
}
