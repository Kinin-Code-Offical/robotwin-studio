using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Examples;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [examples] The start page's examples as cards (D2 "Başlangıç ekranı"): each with a picture drawn from its own
    /// circuit (not a decoration), its level and the areas it uses; a click opens a short info screen first: what it
    /// teaches, its parts, the areas and the tools it needs, then "Aç".
    /// </summary>
    public partial class HomePage
    {
        private static readonly Dictionary<string, BitmapSource?> Pictures = new Dictionary<string, BitmapSource?>(StringComparer.Ordinal);

        /// <summary>What an example uses and needs, from its own circuit.</summary>
        private sealed record ExampleFacts(Schematic Circuit, List<string> Areas, List<(string Name, int Count)> Parts, string Tools);

        private static ExampleFacts FactsOf(ExampleProject ex)
        {
            var sch = ex.Build();
            var lib = Workspace.Current.Library;
            var areas = new List<string> { "Devre" };
            var firmware = sch.Parts.Select(p => p.Parameters.TryGetValue("firmware", out var f) ? f : null).Where(f => !string.IsNullOrEmpty(f)).ToList();
            if (sch.Board != null) areas.Add("PCB");
            if (firmware.Count > 0) areas.Add("Kod");
            if (ex.Page == "assembly") areas.AddRange(new[] { "Montaj", "Dünya" });
            var parts = sch.Parts.Where(p => !string.IsNullOrEmpty(p.PartId))
                .GroupBy(p => Lang.T(lib.Find(p.PartId)?.Name ?? p.PartId))
                .Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.CurrentCulture).ToList();
            string tools = firmware.Count == 0 ? "Ek araç gerekmez"
                : firmware.All(f => f!.EndsWith(".hex", StringComparison.OrdinalIgnoreCase)) ? "Yazılımı hazır derlenmiş gelir: derleyici gerekmez (değiştirmek için Arduino derleyicisi, kurulumla gelir)"
                : "Yazılımı açılınca derlenir: Arduino derleyicisi (kurulumla gelir); ESP32 için hızlı kip ek bir şey istemez";
            return new ExampleFacts(sch, areas, parts, tools);
        }

        /// <summary>The level as a level ("Başlangıç" alone is also the start page's name).</summary>
        private static string LevelText(string level) => Lang.T(level switch
        {
            "Başlangıç" => "Başlangıç düzeyi",
            "Orta" => "Orta düzey",
            "İleri" => "İleri düzey",
            _ => level
        });

        /// <summary>The level and the areas, under a card's title.</summary>
        private static string MetaText(string level, IEnumerable<string> areas) => LevelText(level) + " · " + string.Join(", ", areas.Select(Lang.T));

        /// <summary>The circuit drawn as its file's picture is (the whole sheet, no grid), to fit a box of the given size.</summary>
        private static BitmapSource? Picture(ExampleProject ex, Schematic sch, double boxWidth = 260, double boxHeight = 150)
        {
            string key = ex.Id + "@" + boxWidth + "x" + boxHeight;
            if (Pictures.TryGetValue(key, out var cached)) return cached;
            BitmapSource? bmp = null;
            try
            {
                var canvas = new Editor.SchematicCanvas { Document = sch };
                canvas.RenderSheet(out var full, 20, clearBackground: true);
                if (full.Width >= 1 && full.Height >= 1)
                {
                    double ppu = 20 * Math.Min(1, Math.Min(boxWidth / full.Width, boxHeight / full.Height));
                    var drawing = canvas.RenderSheet(out var size, ppu, clearBackground: true);
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen()) dc.DrawDrawing(drawing);
                    // twice the pixels: sharp at large display scales, the same size on screen
                    var rtb = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(size.Width * 2)), Math.Max(1, (int)Math.Ceiling(size.Height * 2)), 192, 192, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    rtb.Freeze();
                    bmp = rtb;
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is KeyNotFoundException)
            {
                bmp = null;   // the card keeps its icon
            }

            Pictures[key] = bmp;
            return bmp;
        }

        /// <summary>A card: the picture (drawn when the page is idle), the title, the level and the areas.</summary>
        private FrameworkElement ExampleCard(ExampleProject ex)
        {
            var image = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Height = 120, Margin = new Thickness(10, 10, 10, 6) };
            var icon = new Theme.Icon { Data = TryFindResource(ex.Icon) as Geometry, Size = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
            var pictureArea = new Grid { Height = 136 };
            pictureArea.SetResourceReference(Panel.BackgroundProperty, "B.Canvas");
            pictureArea.Children.Add(icon);
            pictureArea.Children.Add(image);
            var title = new TextBlock { Text = ex.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 10, 12, 2) };
            title.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
            var meta = new TextBlock { Text = LevelText(ex.Level), Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(12, 0, 12, 10), TextTrimming = TextTrimming.CharacterEllipsis };
            var stack = new StackPanel();
            stack.Children.Add(pictureArea);
            stack.Children.Add(title);
            stack.Children.Add(meta);
            var card = new Border { Child = stack, Width = 260, Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = ex.Description, ClipToBounds = true };
            card.SetResourceReference(Border.BorderBrushProperty, "B.Border");
            card.SetResourceReference(Border.BackgroundProperty, "B.Surface");
            card.MouseEnter += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            card.MouseLeave += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "B.Border");
            card.MouseLeftButtonUp += (_, _) => ShowExampleInfo(ex);
            // drawn once the page is up, one card at a time: the start page opens at once
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var facts = FactsOf(ex);
                meta.Text = MetaText(ex.Level, facts.Areas);
                image.Source = Picture(ex, facts.Circuit, 240, 120);
                icon.Visibility = image.Source != null ? Visibility.Collapsed : Visibility.Visible;
            }), DispatcherPriority.Background);
            return card;
        }

        /// <summary>The info screen before an example opens: what it teaches, its parts, areas and tools; Aç opens it.</summary>
        private void ShowExampleInfo(ExampleProject ex)
        {
            var facts = FactsOf(ex);
            var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = Lang.T("Vazgeç"), Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(0, 0, 8, 0) };
            var open = new Button { Content = Lang.T("Aç"), Style = (Style)FindResource("Btn.Primary"), Padding = new Thickness(18, 4, 18, 4) };
            buttons.Children.Add(cancel);
            // [learning] the example that starts a level of the learning path: open it with its guide on the card
            Button? guide = null;
            if (MainWindow.LearningStarts.TryGetValue(ex.Id, out int level))
            {
                guide = new Button
                {
                    Content = Lang.T($"Rehberi başlat · düzey {level}"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 4, 12, 4),
                    ToolTip = Lang.T($"Öğrenme yolunun {level}. düzeyi ({MainWindow.LearningLevelNames[level - 1]}): örnek açılır, köşedeki kart adım adım ne yapacağınızı gösterir")
                };
                buttons.Children.Add(guide);
            }

            buttons.Children.Add(open);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = ex.Title, Style = (Style)FindResource("Text.Title"), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = MetaText(ex.Level, facts.Areas), Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 2, 0, 10) });
            if (Picture(ex, facts.Circuit, 496, 156) is { } pic)
            {
                var frame = new Border { Height = 180, Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(6) };
                frame.SetResourceReference(Border.BackgroundProperty, "B.Canvas");
                frame.Child = new Image { Source = pic, Stretch = Stretch.None, Margin = new Thickness(12) };
                body.Children.Add(frame);
            }

            void Section(string head, string text)
            {
                body.Children.Add(new TextBlock { Text = Lang.T(head), Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 6, 0, 4) });
                body.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Text.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            }

            Section("Ne öğretir", ex.Description);
            Section("Parçalar", string.Join(", ", facts.Parts.Take(12).Select(p => p.Count > 1 ? $"{p.Name} ×{p.Count}" : p.Name)) + (facts.Parts.Count > 12 ? " …" : string.Empty));
            Section("Gereken araçlar", Lang.T(facts.Tools));
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var window = new Window
            {
                Title = ex.Title, Owner = Window.GetWindow(this), Width = 560, Height = 600, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root, ShowActivated = !Unattended.Active
            };
            window.SetResourceReference(BackgroundProperty, "B.Surface");
            window.SetResourceReference(ForegroundProperty, "B.Text");
            cancel.Click += (_, _) => window.Close();
            open.Click += (_, _) =>
            {
                window.Close();
                OpenExample(ex);
            };
            if (guide != null)
                guide.Click += (_, _) =>
                {
                    window.Close();
                    OpenExample(ex);
                    _shell.StartLearningGuide(level);   // its first card sees the example open and moves on
                };
            Lang.Translate(root);
            window.Show();
            if (Unattended.Active)
            {
                window.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(window) > 0 ? VisualTreeHelper.GetChild(window, 0) as FrameworkElement : root;
            }
        }

        private void OpenExample(ExampleProject example)
        {
            Workspace.Current.Load(example.Build(), null);
            Workspace.Current.Status = $"{example.Title} açıldı — F5 ile simüle edin";
            if (example.Page == "assembly") _shell.OpenProjectAssembly(ask: false);   // the example is the robot: nothing to ask
            else _shell.Navigate(example.Page);
            // [assembly] a robot example opens on its Montaj: the robot put together, ▶ runs it on its own code
            if (example.Page == "assembly") Workspace.Current.Status = $"{example.Title}: robot Montaj'da kuruldu — ▶ Dünyayı çalıştır ile kendi koduyla sürün";
        }

        /// <summary>--snapshot --page home --fault example-info: the info screen of the first robot example.</summary>
        public void ShowExampleInfoForSnapshot(string? id) =>
            ShowExampleInfo(ExampleProjects.All.FirstOrDefault(e => e.Id == id) ?? ExampleProjects.All.First());
    }
}
