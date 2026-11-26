using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [startup] The one window that opens first, before the studio itself, laid out as Unity's: the artwork filling it
    /// (a stock photo until RobotWin's own), the name and version, what is being loaded right now, how far, the
    /// copyright and the artwork's credit. It runs on a thread of its own, so it keeps drawing while the main thread builds the library and the
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

        private const double Width = 768, Height = 432, Pad = 36, BandHeight = 86;

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

        /// <summary>
        /// The splash's photo (Branding/splash-photo.jpg, embedded): decoded on the splash's own thread and frozen.
        /// A stock photo stands in until RobotWin's own artwork (THIRD_PARTY_NOTICES.md names it).
        /// </summary>
        private static BitmapSource? Photo()
        {
            try
            {
                using var s = typeof(StartupSplash).Assembly.GetManifestResourceStream("RobotWin.Studio.Branding.splash-photo.jpg");
                if (s == null) return null;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = s;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch (Exception)
            {
                return null;   // no picture is no reason not to start
            }
        }

        /// <summary>Who made the splash's photo, for its credit line (as Unity's splash names its artwork).</summary>
        private const string PhotoCredit = "Simon Kadula · Unsplash";

        /// <summary>
        /// Laid out as Unity's opening screen: the artwork fills the window, the name and version on it at the top left,
        /// and along the bottom a band with what is being loaded, how far, the copyright and the artwork's credit, over a
        /// thin bar at the very edge. Flat: a single-tone veil over the photo (no gradient) keeps the text readable.
        /// </summary>
        private static Parts Build()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : string.Empty;
            var ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
            var display = new FontFamily("Segoe UI Variable Display, Segoe UI");
            var root = new Grid { Background = B(Stage) };

            // ---- the artwork, full-bleed, under a flat veil
            if (Photo() is { } photo)
            {
                root.Children.Add(new Image { Source = photo, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                root.Children.Add(new Border { Background = B(Base, 0x8C) });
            }

            // ---- the name, the version, what it does
            var mark = new Image { Width = 44, Height = 44, Source = Branding.Logo.Render(88), VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Text = "RobotWin Studio", FontFamily = display, FontSize = 36, FontWeight = FontWeights.SemiBold, Foreground = B(Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 2) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(mark);
            head.Children.Add(title);
            var name = new StackPanel { Margin = new Thickness(Pad, 32, Pad, 0), VerticalAlignment = VerticalAlignment.Top };
            name.Children.Add(head);
            name.Children.Add(new TextBlock
            {
                Text = Lang.T("Devre · PCB · Kod · Modelle · Montaj · Dünya"),
                FontFamily = ui, FontSize = 14, Foreground = B(Text), Margin = new Thickness(58, 6, 0, 0),
            });
            name.Children.Add(new TextBlock
            {
                Text = Lang.T("Sürüm") + " " + version, FontFamily = ui, FontSize = 12.5, Foreground = B(Text, 0xD0), Margin = new Thickness(58, 4, 0, 0),
            });
            root.Children.Add(name);

            // ---- the band: the step and its share, the copyright and the credit
            var step = new TextBlock { Text = Lang.T("Başlatılıyor…"), FontFamily = ui, FontSize = 14, Foreground = B(Text), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 60, 0) };
            var percent = new TextBlock { Text = Lang.Percent(0, "0"), FontFamily = ui, FontSize = 14, Foreground = B(Secondary), HorizontalAlignment = HorizontalAlignment.Right };
            var status = new Grid();
            status.Children.Add(step);
            status.Children.Add(percent);
            var foot = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            foot.Children.Add(new TextBlock { Text = "© " + DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) + " RobotWin Studio", FontFamily = ui, FontSize = 12, Foreground = B(Muted) });
            foot.Children.Add(new TextBlock { Text = Lang.T("Görsel") + ": " + PhotoCredit, FontFamily = ui, FontSize = 12, Foreground = B(Muted), HorizontalAlignment = HorizontalAlignment.Right });
            var lines = new StackPanel { Margin = new Thickness(Pad, 18, Pad, 0) };
            lines.Children.Add(status);
            lines.Children.Add(foot);
            root.Children.Add(new Border { Background = B(Base, 0xF0), Height = BandHeight, VerticalAlignment = VerticalAlignment.Bottom, Child = lines });

            // ---- the bar along the very bottom edge
            double trackWidth = Width - 2;
            var fill = new Border { Background = B(Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            root.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Background = B(Line), Child = fill });
            var body = root;

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
