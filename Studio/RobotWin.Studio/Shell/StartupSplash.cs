using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [startup] The one window that opens first, before the studio itself (as Unity's editor does): a scene of the
    /// product (a board's traces leading to a robot arm), its name, what is being loaded right now, how far, and the
    /// version. It runs on a thread of its own, so it keeps drawing while the main thread builds the library and the
    /// pages. It stays on top for at least <see cref="MinimumShown"/> (a start of under a second made it flash by),
    /// then fades and hands over to the studio's window. Flat like the rest of the interface: no gradients, no shadows.
    /// </summary>
    internal sealed class StartupSplash
    {
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private readonly Stopwatch _since = Stopwatch.StartNew();
        private Dispatcher? _dispatcher;
        private Window? _window;
        private Parts? _parts;
        private double _target, _shown;

        /// <summary>How long the splash shows at the least, from when it opened.</summary>
        private static readonly TimeSpan MinimumShown = TimeSpan.FromMilliseconds(1150);

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

        // The studio's graphite theme, fixed (the theme is not loaded yet when the splash draws).
        private static readonly Color Base = Color.FromRgb(0x0E, 0x0E, 0x10);
        private static readonly Color Stage = Color.FromRgb(0x13, 0x13, 0x16);
        private static readonly Color Line = Color.FromRgb(0x26, 0x26, 0x2B);
        private static readonly Color Dot = Color.FromRgb(0x1E, 0x1E, 0x23);
        private static readonly Color Text = Color.FromRgb(0xED, 0xED, 0xEF);
        private static readonly Color Secondary = Color.FromRgb(0xB4, 0xB4, 0xBC);
        private static readonly Color Muted = Color.FromRgb(0x8A, 0x8A, 0x93);
        private static readonly Color Accent = Color.FromRgb(0x6E, 0x9B, 0xFF);

        private static SolidColorBrush B(Color c, byte alpha = 0xFF)
        {
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private sealed class Parts
        {
            public Border Frame = null!;
            public TextBlock Step = null!, Percent = null!;
            public Border Fill = null!;
            public double TrackWidth;
        }

        private void Run()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _parts = Build();
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
                Topmost = true,   // the studio's window opens behind it, not over it half-way through
                Content = _parts.Frame,
            };
            _window.ContentRendered += (_, _) => _ready.Set();
            // The bar moves on every frame towards what was reported, and creeps on a little while a step takes long,
            // so it never stands still.
            CompositionTarget.Rendering += (_, _) => Animate();
            _window.Show();
            Dispatcher.Run();
        }

        private const double Width = 720, Height = 396, Pad = 44;

        /// <summary>--snapshot out.png --page splash: the splash as it looks part-way through the start.</summary>
        public static void RenderPreview(string path, double dpiScale)
        {
            var parts = Build();
            parts.Step.Text = Lang.T("Dünya ve 3B görünüm");
            parts.Fill.Width = parts.TrackWidth * 0.42;
            parts.Percent.Text = Lang.Percent(42, "0");
            parts.Frame.Measure(new Size(Width, Height));
            parts.Frame.Arrange(new Rect(0, 0, Width, Height));
            parts.Frame.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(Width * dpiScale), (int)(Height * dpiScale), 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
            bmp.Render(parts.Frame);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }

        private static Parts Build()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : string.Empty;
            var ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
            var display = new FontFamily("Segoe UI Variable Display, Segoe UI");
            const double stageHeight = 250;

            // ---- the stage: plain, the name on it (no drawing: the artwork is the user's to make)
            var stage = new Canvas { Width = Width, Height = stageHeight, ClipToBounds = true, Background = B(Stage) };

            // ---- the name
            var mark = new Image { Width = 56, Height = 56, Source = Branding.Logo.Render(112), HorizontalAlignment = HorizontalAlignment.Left };
            var title = new TextBlock { Text = "RobotWin Studio", FontFamily = display, FontSize = 32, FontWeight = FontWeights.SemiBold, Foreground = B(Text), Margin = new Thickness(0, 18, 0, 0) };
            var tagline = new TextBlock
            {
                Text = Lang.T("Devre · PCB · Kod · Modelle · Montaj · Dünya"),
                FontFamily = ui, FontSize = 13.5, Foreground = B(Secondary), Margin = new Thickness(0, 6, 0, 0),
            };
            var name = new StackPanel { Margin = new Thickness(Pad, 38, 0, 0) };
            name.Children.Add(mark);
            name.Children.Add(title);
            name.Children.Add(tagline);

            var top = new Grid { Height = stageHeight };
            top.Children.Add(stage);
            top.Children.Add(name);

            // ---- what is being loaded, how far, the version
            double trackWidth = Width - 2 * Pad;
            var step = new TextBlock { Text = Lang.T("Başlatılıyor…"), FontFamily = ui, FontSize = 14, Foreground = B(Text), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 60, 0) };
            var percent = new TextBlock { Text = Lang.Percent(0, "0"), FontFamily = ui, FontSize = 14, Foreground = B(Muted), HorizontalAlignment = HorizontalAlignment.Right };
            var status = new Grid();
            status.Children.Add(step);
            status.Children.Add(percent);
            var fill = new Border { Background = B(Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, CornerRadius = new CornerRadius(2) };
            var track = new Border { Height = 4, Width = trackWidth, HorizontalAlignment = HorizontalAlignment.Left, Background = B(Line), CornerRadius = new CornerRadius(2), Child = fill, Margin = new Thickness(0, 12, 0, 0) };
            var foot = new Grid { Margin = new Thickness(0, 22, 0, 0) };
            foot.Children.Add(new TextBlock { Text = Lang.T("Sürüm") + " " + version, FontFamily = ui, FontSize = 12, Foreground = B(Muted) });
            foot.Children.Add(new TextBlock
            {
                Text = Lang.T("Tek fizikle birlikte simüle edilir"), FontFamily = ui, FontSize = 12, Foreground = B(Muted), HorizontalAlignment = HorizontalAlignment.Right
            });
            var bottom = new StackPanel { Margin = new Thickness(Pad, 28, Pad, 0) };
            bottom.Children.Add(status);
            bottom.Children.Add(track);
            bottom.Children.Add(foot);

            var body = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(top, Dock.Top);
            body.Children.Add(top);
            body.Children.Add(new Border { Background = B(Line), Height = 1 });
            DockPanel.SetDock((UIElement)body.Children[1], Dock.Top);
            body.Children.Add(bottom);

            var frame = new Border
            {
                Background = B(Base),
                BorderBrush = B(Line),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Child = new Border { CornerRadius = new CornerRadius(11), ClipToBounds = true, Child = body },
            };
            // The stage's corners follow the frame's round ones.
            frame.Child.SetValue(UIElement.ClipProperty, new RectangleGeometry(new Rect(0, 0, Width - 2, Height - 2), 11, 11));
            return new Parts { Frame = frame, Step = step, Percent = percent, Fill = fill, TrackWidth = trackWidth };
        }

        private void Animate()
        {
            if (_parts == null) return;
            // Towards the reported share; while a step runs long, on towards the next tenth (never past 96 %).
            double goal = Math.Max(_target, Math.Min(Math.Min(_target + 0.1, 0.96), _shown + 0.0006));
            _shown += (goal - _shown) * 0.18;
            if (goal - _shown < 0.0005) _shown = goal;
            _parts.Fill.Width = _parts.TrackWidth * _shown;
            _parts.Percent.Text = Lang.Percent(Math.Round(_shown * 100), "0");
        }

        /// <summary>Shows a step and moves the bar (never backwards).</summary>
        public void Report(string step, double progress)
        {
            _dispatcher?.BeginInvoke(new Action(() =>
            {
                if (_parts == null) return;
                _parts.Step.Text = Lang.T(step);
                _target = Math.Max(_target, Math.Min(1, progress));
            }));
        }

        /// <summary>Closes the splash once the studio has drawn its window: the bar fills, it fades, the studio comes to the front.</summary>
        public static void Finish(Window main)
        {
            var splash = Current;
            Current = null;
            if (splash?._dispatcher == null)
            {
                main.Activate();
                return;
            }

            splash.Report("Hazır", 1);
            var wait = MinimumShown - splash._since.Elapsed;
            if (wait < TimeSpan.FromMilliseconds(250)) wait = TimeSpan.FromMilliseconds(250);   // room for the bar to fill
            var mainDispatcher = main.Dispatcher;
            splash._dispatcher.BeginInvoke(new Action(() =>
            {
                var timer = new DispatcherTimer { Interval = wait };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var w = splash._window;
                    if (w == null)
                    {
                        Dispatcher.CurrentDispatcher.InvokeShutdown();
                        return;
                    }

                    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase() };
                    fade.Completed += (_, _) =>
                    {
                        w.Close();
                        mainDispatcher.BeginInvoke(new Action(() => main.Activate()));
                        Dispatcher.CurrentDispatcher.InvokeShutdown();
                    };
                    w.BeginAnimation(UIElement.OpacityProperty, fade);
                };
                timer.Start();
            }));
        }
    }
}
