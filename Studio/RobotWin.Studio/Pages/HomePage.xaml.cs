using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                    Workspace.Current.Status = $"{example.Title} açıldı — F5 ile simüle edin";
                    if (example.Page == "assembly") _shell.OpenProjectAssembly(ask: false);   // the example is the robot: nothing to ask
                    else _shell.Navigate(example.Page);
                    // [assembly] a robot example opens on its Montaj: the robot put together, ▶ runs it on its own code
                    if (example.Page == "assembly") Workspace.Current.Status = $"{example.Title}: robot Montaj'da kuruldu — ▶ Dünyayı çalıştır ile kendi koduyla sürün";
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

        private void OnGuide(object sender, RoutedEventArgs e) => _shell.StartGuide();   // [guide]

        public void OnShown()
        {
            BuildRecent();
            BuildBackups();
        }

        private void OnNew(object sender, RoutedEventArgs e) => _shell.NewProject();

        private void OnNewWorld(object sender, RoutedEventArgs e) => _shell.NewWorld();

        private void OnOpen(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter(MainWindow.OpenFilter), Title = RobotWin.Studio.Shell.Lang.T("Aç"), Multiselect = true };
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
            foreach (var path in files.Take(6)) RecentList.Children.Add(RecentCard(path));
        }

        /// <summary>
        /// [home] A recent file as a card: the picture saved in it (the circuit sheet or the world as it was last seen), its
        /// name, folder and when it was saved; the file type's icon while it has no picture.
        /// </summary>
        private FrameworkElement RecentCard(string path)
        {
            const double width = 196, height = 122;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string label = Kind(ext);
            var picture = new Grid { Width = width, Height = height, ClipToBounds = true, Background = (Brush)FindResource("B.Raised") };   // flat, the theme's surface
            var icon = new Image { Source = FileIcons.Render(ext, 64), Width = 44, Height = 44, Opacity = 0.9 };
            picture.Children.Add(icon);
            // a world fills the card (its floor and sky are part of it); a circuit's sheet sits on the colour
            var image = ext == ".rwworld" ? new Image { Stretch = Stretch.UniformToFill } : new Image { Stretch = Stretch.Uniform, Margin = new Thickness(12, 24, 12, 22) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            picture.Children.Add(image);
            picture.Children.Add(Chip(label, HorizontalAlignment.Left, VerticalAlignment.Top));
            var stats = Chip(string.Empty, HorizontalAlignment.Left, VerticalAlignment.Bottom);
            stats.Visibility = Visibility.Collapsed;
            picture.Children.Add(stats);
            var frame = new Border { Child = picture, BorderBrush = (Brush)FindResource("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true };
            var text = new StackPanel { Width = width, Margin = new Thickness(0, 8, 0, 0) };
            text.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(path), Foreground = (Brush)FindResource("B.Text"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            string when = File.Exists(path) ? File.GetLastWriteTime(path).ToString("dd.MM.yyyy HH:mm") : RobotWin.Studio.Shell.Lang.T("dosya bulunamadı");
            text.Children.Add(new TextBlock { Text = when + " · " + Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty), Style = (Style)FindResource("Text.Caption"), TextTrimming = TextTrimming.CharacterEllipsis });
            var stack = new StackPanel();
            stack.Children.Add(frame);
            stack.Children.Add(text);
            var card = new Button
            {
                Style = (Style)FindResource("Btn.Ghost"),
                Padding = new Thickness(6),
                Margin = new Thickness(-6, 0, 10, 10),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = stack,
                ToolTip = path
            };
            card.Click += (_, _) => _shell.OpenFile(path);
            System.Threading.Tasks.Task.Run(() => ReadPreview(path)).ContinueWith(t =>
            {
                if (t.Status != System.Threading.Tasks.TaskStatus.RanToCompletion) return;
                var (bitmap, summary) = t.Result;
                if (bitmap != null)
                {
                    image.Source = bitmap;
                    icon.Visibility = Visibility.Collapsed;
                }

                string facts = Holds(ext, summary);
                if (facts.Length > 0 && stats.Child is TextBlock tb)
                {
                    tb.Text = facts;
                    stats.Visibility = Visibility.Visible;
                }
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            return card;
        }

        /// <summary>[home] A file kind's label on its card.</summary>
        private static string Kind(string ext) => ext switch
        {
            ".rwproj" or ".rwsch" => Lang.T("Devre"),
            ".rwworld" => Lang.T("Dünya"),
            ".rwmodel" => Lang.T("Parça"),
            _ => ext.TrimStart('.').ToUpperInvariant()
        };

        /// <summary>[home] What the file holds, from its manifest: parts and code of a circuit, objects, boards and cables of a world.</summary>
        private static string Holds(string ext, System.Collections.Generic.IReadOnlyDictionary<string, string>? s)
        {
            if (s == null) return string.Empty;
            var items = new System.Collections.Generic.List<string>();
            string? Get(string key) => s.TryGetValue(key, out var v) && v != "0" ? v : null;
            if (ext == ".rwworld")
            {
                if (Get("objects") is { } o) items.Add(Lang.T($"{o} nesne"));
                if (Get("boards") is { } b) items.Add(Lang.T($"{b} kart"));
                if (Get("cables") is { } c) items.Add(Lang.T($"{c} kablo"));
            }
            else if (ext == ".rwmodel")
            {
                if (Get("objects") is { } o) items.Add(Lang.T($"{o} gövde"));
            }
            else
            {
                if (Get("parts") is { } p) items.Add(Lang.T($"{p} parça"));
                if (Get("pcb") != null) items.Add("PCB");
                if (Get("firmware") != null) items.Add(Lang.T("kod"));
            }

            return string.Join(" · ", items);
        }

        private Border Chip(string text, HorizontalAlignment h, VerticalAlignment v) => new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x0B, 0x0B, 0x0D)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 2),
            Margin = new Thickness(6),
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.SemiBold }
        };

        /// <summary>
        /// The picture a RobotWin file carries (preview.png in its ZIP), decoded small and frozen, and its manifest's summary;
        /// nulls when it has none.
        /// </summary>
        private static (BitmapSource? Picture, System.Collections.Generic.IReadOnlyDictionary<string, string>? Summary) ReadPreview(string path)
        {
            try
            {
                if (!File.Exists(path)) return (null, null);
                using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                var summary = ReadSummary(zip);
                var entry = zip.GetEntry("preview.png");
                if (entry == null || entry.Length > 8 * 1024 * 1024) return (null, summary);
                var data = new MemoryStream();
                using (var s = entry.Open()) s.CopyTo(data);
                data.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 400;
                bmp.StreamSource = data;
                bmp.EndInit();
                bmp.Freeze();
                return (bmp, summary);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is NotSupportedException ||
                                       ex is FileFormatException || ex is ArgumentException)
            {
                return (null, null);
            }
        }

        private static System.Collections.Generic.IReadOnlyDictionary<string, string>? ReadSummary(System.IO.Compression.ZipArchive zip)
        {
            var entry = zip.GetEntry("manifest.json");
            if (entry == null || entry.Length > 1024 * 1024) return null;
            try
            {
                using var s = entry.Open();
                using var doc = System.Text.Json.JsonDocument.Parse(s);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (!p.Name.Equals("summary", StringComparison.OrdinalIgnoreCase) || p.Value.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    var d = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in p.Value.EnumerateObject()) d[kv.Name] = kv.Value.ToString();
                    return d;
                }
            }
            catch (System.Text.Json.JsonException)
            {
            }

            return null;
        }

        private void BuildBackups()
        {
            BackupList.Children.Clear();
            try
            {
                var files = Unattended.Active ? new System.Collections.Generic.List<FileInfo>() : Backups.Newest(5);
                foreach (var f in files) BackupList.Children.Add(FileButton(f.FullName, f.LastWriteTime.ToString("dd.MM.yyyy HH:mm")));
                BackupTitle.Visibility = files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                // [backup] the last run ended in a crash: the copies made during it come first
                var since = files.Where(f => f.LastWriteTime >= Backups.CrashedRunStart).ToList();
                CrashNote.Visibility = Backups.CrashedLastTime && since.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                CrashText.Text = $"RobotWin Studio geçen sefer beklenmedik biçimde kapandı. O çalışmada alınan {since.Count} otomatik yedek aşağıda: kaydetmediğiniz işi buradan açıp kurtarabilirsiniz.";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                BackupTitle.Visibility = Visibility.Collapsed;
            }
        }
    }
}
