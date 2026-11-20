using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Formats;
using RobotWin.Studio.Branding;
using RobotWin.Studio.Examples;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    public partial class HomePage : UserControl
    {
        private readonly MainWindow _shell;

        public HomePage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();
            HeroLogo.Source = Logo.Render(144);
            Facts.Text = $"{Workspace.Current.Library.Presets.Count} parça  ·  analog devre çözücü  ·  ATmega328P ve ESP32  ·  çok katmanlı PCB, Gerber, KiCad  ·  çok gövdeli dünya";

            foreach (var t in WorldTemplates.All)
            {
                var template = t;
                WorldList.Children.Add(Row(t.Title, t.Description, "Dünya", () => _shell.NewWorld(template.Build())));
            }

            foreach (var ex in ExampleProjects.All)
            {
                var example = ex;
                ExampleList.Children.Add(Row(ex.Title, ex.Description, ex.Level, () =>
                {
                    Workspace.Current.Load(example.Build(), null);
                    Workspace.Current.Status = example.Page == "assembly" ? $"{example.Title} açıldı — Başlat ile simüle edin" : $"{example.Title} açıldı — F5 ile simüle edin";
                    _shell.Navigate(example.Page);
                }));
            }

            foreach (var (keys, what) in new[]
            {
                ("Ctrl N", "Yeni devre"), ("Ctrl ⇧ N", "Yeni dünya"), ("Ctrl O", "Aç"), ("Ctrl S", "Kaydet"), ("Ctrl W", "Sekmeyi kapat"),
                ("Ctrl \\", "Bölünmüş görünüm"), ("F11", "Tam ekran"), ("F5", "Simülasyon")
            })
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new TextBlock { Text = keys, Style = (Style)FindResource("Text.Mono"), Foreground = (Brush)FindResource("B.TextMuted"), Width = 80 });
                row.Children.Add(new TextBlock { Text = what, Style = (Style)FindResource("Text.Body") });
                Shortcuts.Children.Add(row);
            }

            Loaded += (_, _) => OnShown();
        }

        public void OnShown()
        {
            BuildRecent();
            BuildBackups();
        }

        private void OnNew(object sender, RoutedEventArgs e) => _shell.NewProject();

        private void OnNewWorld(object sender, RoutedEventArgs e) => _shell.NewWorld();

        private void OnOpen(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = MainWindow.OpenFilter, Title = "Aç", Multiselect = true };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            foreach (var f in dlg.FileNames) _shell.OpenFile(f);
        }

        private FrameworkElement Row(string title, string description, string tag, Action open)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("B.Text"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) });
            var desc = new TextBlock { Text = description, Style = (Style)FindResource("Text.Caption"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            Grid.SetColumn(desc, 1);
            grid.Children.Add(desc);
            var level = new TextBlock { Text = tag, Style = (Style)FindResource("Text.Caption"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(level, 2);
            grid.Children.Add(level);

            var row = new Border
            {
                Child = grid,
                Padding = new Thickness(10, 11, 10, 11),
                BorderBrush = (Brush)FindResource("B.Border"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = description
            };
            row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("B.Hover");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => open();
            return row;
        }

        private Button FileButton(string path, string caption)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new Image { Source = FileIcons.Render(Path.GetExtension(path).ToLowerInvariant(), 32), Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0) });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(path), Foreground = (Brush)FindResource("B.Text"), FontSize = 13 });
            text.Children.Add(new TextBlock { Text = caption, Style = (Style)FindResource("Text.Caption"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 250 });
            stack.Children.Add(text);
            var row = new Button
            {
                Style = (Style)FindResource("Btn.Ghost"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(-8, 0, 0, 2),
                Content = stack,
                ToolTip = path
            };
            row.Click += (_, _) => _shell.OpenFile(path);
            return row;
        }

        private void BuildRecent()
        {
            RecentList.Children.Clear();
            var files = RecentFiles.Load();
            RecentEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var path in files) RecentList.Children.Add(FileButton(path, Path.GetDirectoryName(path) ?? string.Empty));
        }

        private void BuildBackups()
        {
            BackupList.Children.Clear();
            try
            {
                var dir = new DirectoryInfo(MainWindow.BackupFolder);
                var files = dir.Exists ? dir.GetFiles().Where(f => f.Extension == RobotWinFormats.ProjectExtension || f.Extension == RobotWinFormats.WorldExtension)
                    .OrderByDescending(f => f.LastWriteTimeUtc).Take(5).ToList() : new System.Collections.Generic.List<FileInfo>();
                foreach (var f in files) BackupList.Children.Add(FileButton(f.FullName, f.LastWriteTime.ToString("dd.MM.yyyy HH:mm")));
                BackupTitle.Visibility = files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                BackupTitle.Visibility = Visibility.Collapsed;
            }
        }
    }
}
