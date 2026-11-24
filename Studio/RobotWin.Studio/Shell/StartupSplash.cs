using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [startup] The window that opens first, before the studio itself (as Unity's editor does): the mark, the version and
    /// what is being loaded right now with a progress bar. It runs on a thread of its own, so it keeps drawing while the
    /// main thread builds the library and the pages; it closes when the studio's window has drawn itself.
    /// </summary>
    internal sealed class StartupSplash
    {
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private Dispatcher? _dispatcher;
        private Window? _window;
        private TextBlock? _step;
        private Border? _fill;
        private double _shown;

        public static StartupSplash? Current { get; private set; }

        /// <summary>Opens the splash (not for command-line runs: snapshots, the bench, the self-test).</summary>
        public static void Open()
        {
            if (Current != null || (Unattended.Active && !Unattended.MeasuringStartup)) return;
            var splash = new StartupSplash();
            var thread = new Thread(splash.Run) { IsBackground = true, Name = "Açılış ekranı" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            splash._ready.Wait(TimeSpan.FromMilliseconds(60));   // its window is on the way; the work need not wait for it
            Current = splash;
        }

        private static readonly Color Canvas = Color.FromRgb(0x0E, 0x0E, 0x10);
        private static readonly Color Line = Color.FromRgb(0x24, 0x24, 0x28);
        private static readonly Color Text = Color.FromRgb(0xED, 0xED, 0xEF);
        private static readonly Color Muted = Color.FromRgb(0x8A, 0x8A, 0x93);
        private static readonly Color Accent = Color.FromRgb(0x6E, 0x9B, 0xFF);

        private static SolidColorBrush B(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private void Run()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            var (frame, step, fill) = Build();
            _step = step;
            _fill = fill;
            _window = new Window
            {
                Title = "RobotWin Studio",
                Width = Width,
                Height = Height,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true,
                Topmost = false,
                Content = frame,
            };
            _window.ContentRendered += (_, _) => _ready.Set();
            _window.Show();
            Dispatcher.Run();
        }

        private const double Width = 620, Height = 360;

        /// <summary>--snapshot out.png --page splash: the splash as it looks part-way through the start.</summary>
        public static void RenderPreview(string path, double dpiScale)
        {
            var (frame, step, fill) = Build();
            step.Text = "Dünya ve 3B görünüm";
            fill.Width = (Width - 82) * 0.42;
            frame.Measure(new Size(Width, Height));
            frame.Arrange(new Rect(0, 0, Width, Height));
            frame.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(Width * dpiScale), (int)(Height * dpiScale), 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
            bmp.Render(frame);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }

        private static (Border Frame, TextBlock Step, Border Fill) Build()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : string.Empty;
            var ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
            var display = new FontFamily("Segoe UI Variable Display, Segoe UI");

            var mark = new Image { Width = 72, Height = 72, Source = Branding.Logo.Render(144), HorizontalAlignment = HorizontalAlignment.Left };
            var title = new TextBlock { Text = "RobotWin Studio", FontFamily = display, FontSize = 30, Foreground = B(Text), Margin = new Thickness(0, 22, 0, 0) };
            var tagline = new TextBlock
            {
                Text = "Devre, PCB, kod, mekanik ve dünya: tek fizikle birlikte simüle edilir.",
                FontFamily = ui, FontSize = 13, Foreground = B(Muted), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
            };
            var stepText = new TextBlock { Text = "Başlatılıyor…", FontFamily = ui, FontSize = 12, Foreground = B(Muted), TextTrimming = TextTrimming.CharacterEllipsis };
            var versionText = new TextBlock { Text = "Sürüm " + version, FontFamily = ui, FontSize = 12, Foreground = B(Muted), HorizontalAlignment = HorizontalAlignment.Right };
            var fill = new Border { Background = B(Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(1.5) };
            var track = new Border { Height = 3, Background = B(Line), CornerRadius = new CornerRadius(1.5), Child = fill, Margin = new Thickness(0, 10, 0, 0) };

            var footer = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            footer.Children.Add(stepText);
            footer.Children.Add(versionText);
            var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            bottom.Children.Add(footer);
            bottom.Children.Add(track);

            var top = new StackPanel();
            top.Children.Add(mark);
            top.Children.Add(title);
            top.Children.Add(tagline);

            var body = new Grid { Margin = new Thickness(40, 38, 40, 30) };
            body.Children.Add(top);
            body.Children.Add(bottom);

            var frame = new Border
            {
                Background = B(Canvas),
                BorderBrush = B(Line),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = body,
            };
            return (frame, stepText, fill);
        }

        /// <summary>Shows a step and moves the bar (smoothly; never backwards).</summary>
        public void Report(string step, double progress)
        {
            _dispatcher?.BeginInvoke(new Action(() =>
            {
                if (_step == null || _fill == null || _window == null) return;
                _step.Text = step;
                double target = Math.Max(_shown, Math.Min(1, progress));
                _shown = target;
                double width = Math.Max(0, (_window.ActualWidth - 82) * target);
                _fill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() });
            }));
        }

        /// <summary>Closes the splash once the studio has drawn its window (with a short fade).</summary>
        public static void Finish(Window main)
        {
            var splash = Current;
            Current = null;
            if (splash?._dispatcher == null) return;
            splash.Report("Hazır", 1);
            splash._dispatcher.BeginInvoke(new Action(() =>
            {
                var w = splash._window;
                if (w == null)
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                    return;
                }

                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
                fade.Completed += (_, _) =>
                {
                    w.Close();
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                };
                w.BeginAnimation(UIElement.OpacityProperty, fade);
            }));
            main.Activate();
        }
    }
}
