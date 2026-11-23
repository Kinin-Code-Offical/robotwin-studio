using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using RobotWin.Studio.Theme;

namespace RobotWin.Studio.Shell
{
    /// <summary>Live state of a stage shown on its pad: a status line, a badge and whether it is running.</summary>
    public readonly struct RailStatus
    {
        public RailStatus(string text, RailSeverity severity = RailSeverity.None, bool live = false)
        {
            Text = text;
            Severity = severity;
            Live = live;
        }

        public string Text { get; }
        public RailSeverity Severity { get; }
        public bool Live { get; }
    }

    public enum RailSeverity
    {
        None,
        Ok,
        Warning,
        Error
    }

    /// <summary>
    /// The "design-to-real line": RobotWin's navigation drawn as a copper trace running down
    /// the window edge. Every stage of building a robot (circuit → board → firmware → assembly
    /// → world) is a pad on that trace; the trace is energised up to the stage you are on,
    /// each pad carries a live status badge, and while a simulation runs current dots flow
    /// along it. Collapsed it is a slim rail; expanded it reads like a build pipeline with a
    /// status line per stage. Flat colours only (no gradients).
    /// </summary>
    public sealed class FlowRail : Grid
    {
        public const double CollapsedWidth = 76;
        public const double ExpandedWidth = 244;
        private const double Cx = 38;            // trace x (centre of the pads)
        private const double Row = 66;           // stage pitch
        private const double Pad = 38;           // pad size
        private const double SeparateGap = 22;   // [rail] space and divider above the world, which is not on the project's trace

        private sealed class Stage
        {
            public string Key = string.Empty;
            public string Label = string.Empty;
            public string Hint = string.Empty;
            public Color Accent;
            public Border PadBorder = null!;
            public Icon Glyph = null!;
            public TextBlock Caption = null!;
            public TextBlock Title = null!;
            public TextBlock Status = null!;
            public Ellipse Badge = null!;
            public Grid Host = null!;
            public RailStatus Current;
        }

        private readonly List<Stage> _stages = new List<Stage>();
        private readonly TraceLayer _trace;
        private readonly StackPanel _line = new StackPanel();
        private readonly TextBlock _header;
        private readonly Grid _home;
        private readonly Border _homeVia;
        private readonly Icon _homeIcon;
        private readonly List<(Grid Host, TextBlock Label, string Key)> _tools = new List<(Grid, TextBlock, string)>();
        private string _active = "home";
        private bool _expanded;

        public FlowRail()
        {
            Background = Res<Brush>("B.Canvas");
            ClipToBounds = true;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Home: the mark at the top of the line (where the trace starts).
            _home = new Grid { Height = 64, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Başlangıç (Ctrl+0)" };
            // The line starts at a large "origin via" carrying the home glyph.
            _homeIcon = new Icon { Data = Res<Geometry>("I.Home"), Size = 16, Foreground = Res<Brush>("B.TextMuted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _homeVia = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1.5),
                BorderBrush = Res<Brush>("B.BorderStrong"),
                Background = Res<Brush>("B.Raised"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Cx - 16, 0, 0, 0),
                Child = _homeIcon
            };
            _home.Children.Add(_homeVia);
            _header = new TextBlock
            {
                Text = "Başlangıç",
                FontSize = 13,
                Foreground = Res<Brush>("B.Text"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Cx + 24, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _home.Children.Add(_header);
            _home.MouseLeftButtonUp += (_, _) => Navigate?.Invoke("home");
            SetRow(_home, 0);
            Children.Add(_home);

            var middle = new Grid();
            _trace = new TraceLayer(this);
            middle.Children.Add(_trace);
            _line.Margin = new Thickness(0, 6, 0, 0);
            middle.Children.Add(_line);
            SetRow(middle, 1);
            Children.Add(middle);

            AddStage("schematic", "Devre", "I.Schematic", 0x5B8DFF, "Şema çiz, parçaları bağla, simüle et (Ctrl+1)");
            AddStage("pcb", "PCB", "I.Pcb", 0x4CC07E, "Kartı yerleştir, yolları çiz, üretime hazırla (Ctrl+2)");
            AddStage("code", "Kod", "I.Chip", 0xA77BFF, "Kartların yazılımını yaz ve derle (Ctrl+3)");
            AddStage("model", "Modelle", "I.Part", 0xE0B04A, "Şase, braket, kutu: 3D parça modelle, birleştir, dünyaya gönder (Ctrl+4)");
            AddStage("assembly", "Montaj", "I.Cube", 0xF08A3C, "Bu projenin devresini gerçek parçalarla 3B montaja çevirir: kart, parçalar ve kablolar bir dünyada; gövde, motor ve mekaniği orada eklersiniz (Ctrl+5)");
            // [rail] Worlds are documents of their own, not a step of the project: set apart, off the project's trace.
            _line.Children.Add(new Border { Height = 1, Margin = new Thickness(18, SeparateGap / 2, 18, SeparateGap / 2 - 1), Background = Res<Brush>("B.Border") });
            AddStage("world", "Dünya", "I.World", 0x2FC2B0, "Dünyalar (şablonlar, kendi test alanlarınız) projeden ayrı belgelerdir: hepsini bir arada gerçek fizikle deneyin (Ctrl+6)");

            var tools = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            tools.Children.Add(Tool("library", "Kütüphane", "I.Library", "Parçalar, modeller, ayak izleri (Ctrl+7)"));
            tools.Children.Add(Tool("settings", "Ayarlar", "I.Settings", "Uygulama ayarları"));
            tools.Children.Add(Tool("help", "Yardım", "I.Help", "Kullanım kılavuzu, haberler ve güncellemeler (F1)"));
            tools.Children.Add(ExpandToggle());
            SetRow(tools, 2);
            Children.Add(tools);

            Width = CollapsedWidth;
            ApplyMode();
        }

        /// <summary>Raised with the page key when a stage, home or a tool is clicked.</summary>
        public event Action<string>? Navigate;

        /// <summary>Supplies the live status of a stage (polled about twice a second).</summary>
        public Func<string, RailStatus>? StatusProvider { get; set; }

        public event EventHandler? ExpandedChanged;

        public bool IsExpanded
        {
            get => _expanded;
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                // Slide open / closed (ease-out, 180 ms); the first layout pass jumps straight to the size.
                double target = value ? ExpandedWidth : CollapsedWidth;
                if (!IsLoaded || double.IsNaN(Width))
                {
                    BeginAnimation(WidthProperty, null);
                    Width = target;
                }
                else
                {
                    var slide = new DoubleAnimation(ActualWidth, target, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    slide.Completed += (_, _) =>
                    {
                        BeginAnimation(WidthProperty, null);
                        Width = target;
                    };
                    BeginAnimation(WidthProperty, slide);
                }
                ApplyMode();
                ExpandedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string Active
        {
            get => _active;
            set
            {
                _active = value;
                bool home = value == "home";
                _homeVia.BorderBrush = Res<Brush>(home ? "B.Text" : "B.BorderStrong");
                _homeIcon.Foreground = Res<Brush>(home ? "B.Text" : "B.TextMuted");
                _header.Foreground = Res<Brush>(home ? "B.Text" : "B.TextSecondary");
                foreach (var s in _stages) Paint(s, s.Key == value, false);
                foreach (var (host, label, key) in _tools)
                {
                    bool on = key == value;
                    label.Foreground = Res<Brush>(on ? "B.Text" : "B.TextMuted");
                    ((Icon)host.Children[0]).Foreground = Res<Brush>(on ? "B.Text" : "B.TextMuted");
                    host.Background = on ? Res<Brush>("B.Raised") : Brushes.Transparent;
                }

                _trace.InvalidateVisual();
            }
        }

        /// <summary>Re-reads every stage's status from <see cref="StatusProvider"/>.</summary>
        public void Refresh()
        {
            if (StatusProvider == null) return;
            bool live = false;
            foreach (var s in _stages)
            {
                RailStatus st;
                try
                {
                    st = StatusProvider(s.Key);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NullReferenceException)
                {
                    continue;
                }

                live |= st.Live;
                if (st.Text == s.Current.Text && st.Severity == s.Current.Severity && st.Live == s.Current.Live) continue;
                s.Current = st;
                s.Status.Text = st.Text;
                s.Host.ToolTip = s.Hint + (string.IsNullOrEmpty(st.Text) ? string.Empty : "\n" + st.Text);
                ShowBadge(s);
            }

            _trace.Flowing = live;
        }

        // ------------------------------------------------------------------ building

        private void AddStage(string key, string label, string icon, uint rgb, string hint)
        {
            var accent = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            var s = new Stage { Key = key, Label = label, Hint = hint, Accent = accent };
            var host = new Grid { Height = Row, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = hint };
            s.Host = host;

            var pad = new Border
            {
                Width = Pad,
                Height = Pad,
                CornerRadius = new CornerRadius(Pad / 3.236),     // golden: side / (2φ)
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(Cx - Pad / 2, 4, 0, 0),
                SnapsToDevicePixels = true
            };
            s.Glyph = new Icon { Data = Res<Geometry>(icon), Size = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            pad.Child = s.Glyph;
            s.PadBorder = pad;
            host.Children.Add(pad);

            s.Badge = new Ellipse
            {
                Width = 9,
                Height = 9,
                StrokeThickness = 2,
                Stroke = Res<Brush>("B.Canvas"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(Cx + Pad / 2 - 7, 1, 0, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            host.Children.Add(s.Badge);

            // Collapsed: small caption under the pad. Expanded: title + status to the right.
            s.Caption = new TextBlock
            {
                Text = label,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                // A label sitting on the trace: the canvas colour behind it breaks the line neatly.
                Background = Res<Brush>("B.Canvas"),
                Padding = new Thickness(4, 1, 4, 2)
            };
            var captionLane = new Grid
            {
                Width = Cx * 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, Pad + 7, 0, 0),
                IsHitTestVisible = false
            };
            captionLane.Children.Add(s.Caption);
            host.Children.Add(captionLane);
            var text = new StackPanel { Margin = new Thickness(Cx + Pad / 2 + 14, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            s.Title = new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.SemiBold };
            s.Status = new TextBlock { FontSize = 12, Foreground = Res<Brush>("B.TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            text.Children.Add(s.Title);
            text.Children.Add(s.Status);
            host.Children.Add(text);

            host.MouseEnter += (_, _) => Paint(s, s.Key == _active, true);
            host.MouseLeave += (_, _) => Paint(s, s.Key == _active, false);
            host.MouseLeftButtonUp += (_, _) => Navigate?.Invoke(key);
            _stages.Add(s);
            _line.Children.Add(host);
            Paint(s, false, false);
        }

        private FrameworkElement Tool(string key, string label, string icon, string hint)
        {
            var host = new Grid { Height = 44, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = hint, Margin = new Thickness(8, 0, 8, 2) };
            var ic = new Icon { Data = Res<Geometry>(icon), Size = 18, Foreground = Res<Brush>("B.TextMuted"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Cx - 8 - 9, 0, 0, 0) };
            host.Children.Add(ic);
            var tb = new TextBlock { Text = label, FontSize = 12.5, Foreground = Res<Brush>("B.TextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Cx + Pad / 2 + 6, 0, 0, 0) };
            host.Children.Add(tb);
            host.MouseEnter += (_, _) => { if (key != _active) host.Background = Res<Brush>("B.Hover"); };
            host.MouseLeave += (_, _) => { if (key != _active) host.Background = Brushes.Transparent; };
            host.MouseLeftButtonUp += (_, _) => Navigate?.Invoke(key);
            _tools.Add((host, tb, key));
            return host;
        }

        private readonly Dictionary<string, Ellipse> _toolBadges = new Dictionary<string, Ellipse>();

        /// <summary>A small accent dot on a tool (e.g. Yardım: an update or unread news).</summary>
        public void SetToolBadge(string key, bool on)
        {
            if (!_toolBadges.TryGetValue(key, out var dot))
            {
                var host = _tools.Find(t => t.Key == key).Host;
                if (host == null) return;
                dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = Res<Brush>("B.Accent"),
                    Stroke = Res<Brush>("B.Canvas"),
                    StrokeThickness = 1.5,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(Cx - 8 + 2, -14, 0, 0),
                    IsHitTestVisible = false
                };
                host.Children.Add(dot);
                _toolBadges[key] = dot;
            }

            dot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        private TextBlock? _toggleLabel;
        private Icon? _toggleIcon;

        private FrameworkElement ExpandToggle()
        {
            var host = new Grid { Height = 36, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Hattı genişlet / daralt (Ctrl+B)", Margin = new Thickness(8, 4, 8, 0) };
            _toggleIcon = new Icon { Size = 14, Foreground = Res<Brush>("B.TextMuted"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Cx - 8 - 7, 0, 0, 0) };
            host.Children.Add(_toggleIcon);
            _toggleLabel = new TextBlock { Text = "Daralt", FontSize = 12, Foreground = Res<Brush>("B.TextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Cx + Pad / 2 + 6, 0, 0, 0) };
            host.Children.Add(_toggleLabel);
            host.MouseEnter += (_, _) => host.Background = Res<Brush>("B.Hover");
            host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
            host.MouseLeftButtonUp += (_, _) => IsExpanded = !IsExpanded;
            return host;
        }

        private void ApplyMode()
        {
            _header.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            foreach (var s in _stages)
            {
                s.Caption.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
                ((FrameworkElement)s.Title.Parent).Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            }

            foreach (var (_, label, _) in _tools) label.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            if (_toggleLabel != null) _toggleLabel.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            if (_toggleIcon != null) _toggleIcon.Data = Geometry.Parse(_expanded ? "M15,5 L8,12 L15,19" : "M9,5 L16,12 L9,19");
            _trace.InvalidateVisual();
        }

        private void Paint(Stage s, bool active, bool hover)
        {
            var muted = Res<Color>("C.TextMuted");
            s.PadBorder.Background = new SolidColorBrush(active ? Color.FromArgb(0x2E, s.Accent.R, s.Accent.G, s.Accent.B) : hover ? Res<Color>("C.Hover") : Res<Color>("C.Raised"));
            s.PadBorder.BorderBrush = new SolidColorBrush(active ? s.Accent : hover ? Res<Color>("C.TextMuted") : Res<Color>("C.BorderStrong"));
            s.Glyph.Foreground = new SolidColorBrush(active ? s.Accent : hover ? Res<Color>("C.TextSecondary") : muted);
            s.Caption.Foreground = Res<Brush>(active ? "B.Text" : hover ? "B.TextSecondary" : "B.TextMuted");
            s.Title.Foreground = Res<Brush>(active || hover ? "B.Text" : "B.TextSecondary");
        }

        private void ShowBadge(Stage s)
        {
            var st = s.Current;
            s.Badge.BeginAnimation(OpacityProperty, null);
            if (st.Live)
            {
                s.Badge.Fill = new SolidColorBrush(s.Accent);
                s.Badge.Visibility = Visibility.Visible;
                var pulse = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                s.Badge.BeginAnimation(OpacityProperty, pulse);
                return;
            }

            s.Badge.Opacity = 1;
            switch (st.Severity)
            {
                case RailSeverity.Error:
                    s.Badge.Fill = Res<Brush>("B.Danger");
                    s.Badge.Visibility = Visibility.Visible;
                    break;
                case RailSeverity.Warning:
                    s.Badge.Fill = Res<Brush>("B.Warning");
                    s.Badge.Visibility = Visibility.Visible;
                    break;
                case RailSeverity.Ok:
                    s.Badge.Fill = Res<Brush>("B.Success");
                    s.Badge.Visibility = Visibility.Visible;
                    break;
                default:
                    s.Badge.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private static T Res<T>(string key) => (T)Application.Current.FindResource(key);

        // ------------------------------------------------------------------ the trace

        /// <summary>Copper trace behind the pads: dim along its length, lit up to the active stage, dots flowing while running.</summary>
        private sealed class TraceLayer : FrameworkElement
        {
            private readonly FlowRail _rail;
            private bool _flowing;
            private double _phase;
            private TimeSpan _last;

            public TraceLayer(FlowRail rail)
            {
                _rail = rail;
                IsHitTestVisible = false;
            }

            public bool Flowing
            {
                get => _flowing;
                set
                {
                    if (_flowing == value) return;
                    _flowing = value;
                    if (value) CompositionTarget.Rendering += OnFrame;
                    else CompositionTarget.Rendering -= OnFrame;
                    InvalidateVisual();
                }
            }

            private void OnFrame(object? sender, EventArgs e)
            {
                var t = ((RenderingEventArgs)e).RenderingTime;
                if (t == _last) return;
                double dt = _last == TimeSpan.Zero ? 0 : Math.Min(0.05, (t - _last).TotalSeconds);
                _last = t;
                _phase = (_phase + dt * 34) % 22;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                // The project's stages share the trace; the world (last) stands apart below a divider.
                int n = _rail._stages.Count(s => s.Key != "world");
                if (n == 0) return;
                double top = 6 + 4 + Pad / 2;                 // centre of the first pad
                double y0 = -8, y1 = top + (n - 1) * Row;      // from the mark down to the last pad
                var dim = new Pen(new SolidColorBrush(Res<Color>("C.BorderStrong")), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(dim, new Point(Cx, y0), new Point(Cx, y1));

                int ai = _rail._stages.FindIndex(s => s.Key == _rail._active);
                if (ai >= n) ai = -1;   // the world lights only its own pad
                if (ai >= 0)
                {
                    var acc = _rail._stages[ai].Accent;
                    var lit = new Pen(new SolidColorBrush(acc), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    double ya = top + ai * Row;
                    dc.DrawLine(lit, new Point(Cx, y0), new Point(Cx, ya));
                    // Stub to the expanded labels: the active stage "breaks out" of the line like a fan-out trace.
                    if (_rail._expanded) dc.DrawLine(lit, new Point(Cx + Pad / 2, ya), new Point(Cx + Pad / 2 + 8, ya));
                }

                // Vias between the pads.
                var viaFill = new SolidColorBrush(Res<Color>("C.Canvas"));
                var viaRing = new Pen(new SolidColorBrush(Res<Color>("C.BorderStrong")), 1.5);
                // (Collapsed, the stage captions sit on the trace there instead.)
                for (int i = 0; i < n - 1 && _rail._expanded; i++)
                {
                    double y = top + i * Row + Row / 2;
                    var ring = ai >= 0 && i < ai ? new Pen(new SolidColorBrush(_rail._stages[ai].Accent), 1.5) : viaRing;
                    dc.DrawEllipse(viaFill, ring, new Point(Cx, y), 3.2, 3.2);
                }

                if (_flowing)
                {
                    var dot = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
                    for (double y = y0 + _phase; y < y1; y += 22) dc.DrawEllipse(dot, null, new Point(Cx, y), 1.6, 1.6);
                }
            }
        }
    }
}
