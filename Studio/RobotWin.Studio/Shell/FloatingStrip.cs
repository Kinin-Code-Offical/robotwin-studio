using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [floatbar] A tool strip that tears off into a small window of its own and docks back, as SolidWorks' and
    /// Photoshop's floating toolbars: drag its grip off the page (or pick "Ayrı pencereye al"), move the window anywhere
    /// (a second screen too), turn it across or down; a double click on the grip, or "Yerine koy", puts it back. While it
    /// floats its column gives the canvas the room. Where it was is kept between sessions.
    /// </summary>
    public sealed class FloatingStrip
    {
        private sealed class State
        {
            public bool Floating { get; set; }
            public double Left { get; set; }
            public double Top { get; set; }
            public bool Across { get; set; }
        }

        private static readonly string? TestPath = Environment.GetEnvironmentVariable("ROBOTWIN_TOOLBARS");

        private static string StorePath => string.IsNullOrWhiteSpace(TestPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "toolbars.json")
            : TestPath!;

        private static Dictionary<string, State>? _store;

        private readonly string _key;
        private readonly Decorator _host;
        private readonly StackPanel _strip;
        private readonly Action<bool> _docked;
        private readonly Border _grip;
        private Window? _window;
        private readonly bool _dockedAcross;
        private Point _press;
        private bool _pressed;

        /// <param name="key">The strip's name in the store ("pcb.tools").</param>
        /// <param name="host">The border the strip sits in on the page.</param>
        /// <param name="docked">Called with true when the strip is back on the page, false when it floats (the page frees its column).</param>
        public FloatingStrip(string key, Decorator host, StackPanel strip, Action<bool> docked)
        {
            _key = key;
            _host = host;
            _strip = strip;
            _docked = docked;
            _grip = Grip();
            _strip.Children.Insert(0, _grip);
            _dockedAcross = strip.Orientation == Orientation.Horizontal;   // a toolbar row docks back across, a side strip down
            ShapeGrip(_dockedAcross);
            _host.Loaded += (_, _) =>
            {
                if (Unattended.Active || _window != null) return;   // a snapshot shows the strip where it belongs
                if (Load().TryGetValue(_key, out var st) && st.Floating) TearOff(new Point(st.Left, st.Top), st.Across, drag: false);
            };
        }

        public bool IsFloating => _window != null;

        /// <summary>Tears the strip off at a screen point (a snapshot of the floating window, the menu's command); the window.</summary>
        public Window? TearOffAt(Point screen, bool across = false)
        {
            TearOff(screen, across, drag: false);
            return _window;
        }

        // ---------------------------------------------------------------- the grip

        private Border Grip()
        {
            var dots = new UniformGrid2(3, 2);
            var grip = new Border
            {
                Height = 12, Margin = new Thickness(6, 0, 6, 6), Cursor = Cursors.SizeAll, Background = Brushes.Transparent, Child = dots,
                ToolTip = Lang.T("Tutamaktan sürükleyerek ayrı pencereye alın; çift tık yerine koyar. Sağ tık: seçenekler")
            };
            grip.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount >= 2)
                {
                    if (_window != null) Dock();
                    else TearOff(PointToScreen(e), across: false, drag: false);
                    e.Handled = true;
                    return;
                }

                if (_window != null)
                {
                    _window.DragMove();   // moves the floating window
                    Save();
                    e.Handled = true;
                    return;
                }

                _press = e.GetPosition(_host);
                _pressed = true;
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += (_, e) =>
            {
                if (!_pressed || _window != null) return;
                var p = e.GetPosition(_host);
                if ((p - _press).Length < 8) return;
                _pressed = false;
                grip.ReleaseMouseCapture();
                TearOff(PointToScreen(e), across: false, drag: true);
            };
            grip.MouseLeftButtonUp += (_, _) =>
            {
                _pressed = false;
                grip.ReleaseMouseCapture();
            };
            grip.MouseRightButtonUp += (_, e) =>
            {
                var m = new ContextMenu { PlacementTarget = grip };
                var tear = new MenuItem { Header = Lang.T(_window == null ? "Ayrı pencereye al" : "Yerine koy") };
                tear.Click += (_, _) =>
                {
                    if (_window == null) TearOff(PointToScreen(e), across: false, drag: false);
                    else Dock();
                };
                m.Items.Add(tear);
                if (_window != null)
                {
                    var turn = new MenuItem { Header = Lang.T(_strip.Orientation == Orientation.Vertical ? "Yatay çevir" : "Dikey çevir") };
                    turn.Click += (_, _) =>
                    {
                        Turn(_strip.Orientation == Orientation.Vertical);
                        Save();
                    };
                    m.Items.Add(turn);
                }

                m.IsOpen = true;
                e.Handled = true;
            };
            return grip;
        }

        private Point PointToScreen(MouseEventArgs e)
        {
            var source = PresentationSource.FromVisual(_host);
            var p = _host.PointToScreen(e.GetPosition(_host));
            return source?.CompositionTarget is { } t ? t.TransformFromDevice.Transform(p) : p;
        }

        // ---------------------------------------------------------------- floating and docking

        private void TearOff(Point at, bool across, bool drag)
        {
            if (_window != null) return;
            _host.Child = null;
            var frame = new Border { Child = _strip, Padding = new Thickness(4, 6, 4, 4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            frame.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            frame.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            _window = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false, Owner = Window.GetWindow(_host) ?? Application.Current.MainWindow, Content = frame,
                Left = at.X - 20, Top = at.Y - 8, Background = Brushes.Transparent, AllowsTransparency = true, Title = _key
            };
            _window.SetResourceReference(Control.FontFamilyProperty, "F.Ui");
            _window.Closed += (_, _) =>
            {
                if (_window != null) Dock();
            };
            Turn(across);
            _docked(false);
            _window.Show();
            if (drag && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    _window.DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }

            Save();
        }

        /// <summary>Puts the strip back on the page.</summary>
        public void Dock()
        {
            if (_window == null) return;
            var w = _window;
            _window = null;
            if (w.Content is Border frame) frame.Child = null;
            Turn(_dockedAcross);
            _host.Child = _strip;
            _docked(true);
            if (w.IsVisible) w.Close();
            Save();
        }

        /// <summary>Lays the strip across (horizontal) or down; one-pixel separators turn with it.</summary>
        private void Turn(bool across)
        {
            _strip.Orientation = across ? Orientation.Horizontal : Orientation.Vertical;
            foreach (var b in _strip.Children.OfType<Border>().Where(b => !ReferenceEquals(b, _grip)))
            {
                bool line = Math.Abs(b.Height - 1) < 1e-9 || Math.Abs(b.Width - 1) < 1e-9;
                if (!line) continue;
                b.Width = across ? 1 : double.NaN;
                b.Height = across ? 20 : 1;
                b.Margin = across ? new Thickness(4, 6, 8, 6) : new Thickness(6, 4, 6, 8);
            }

            ShapeGrip(across);
        }

        private void ShapeGrip(bool across)
        {
            _grip.Height = across ? double.NaN : 12;
            _grip.Width = across ? 12 : double.NaN;
            _grip.Margin = across ? new Thickness(0, 6, 6, 6) : new Thickness(6, 0, 6, 6);
            if (_grip.Child is UniformGrid2 g) g.Turn(across);
        }

        // ---------------------------------------------------------------- store

        private static Dictionary<string, State> Load()
        {
            if (_store != null) return _store;
            try
            {
                _store = File.Exists(StorePath) ? JsonSerializer.Deserialize<Dictionary<string, State>>(File.ReadAllText(StorePath)) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                _store = null;
            }

            return _store ??= new Dictionary<string, State>();
        }

        private void Save()
        {
            var all = Load();
            all[_key] = new State { Floating = _window != null, Left = _window?.Left ?? 0, Top = _window?.Top ?? 0, Across = _strip.Orientation == Orientation.Horizontal };
            if (Unattended.Active && string.IsNullOrWhiteSpace(TestPath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(all));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>The grip's dots: three by two, turned with the strip.</summary>
        private sealed class UniformGrid2 : FrameworkElement
        {
            private int _cols, _rows;

            public UniformGrid2(int cols, int rows)
            {
                _cols = cols;
                _rows = rows;
            }

            public void Turn(bool across)
            {
                (_cols, _rows) = across ? (2, 3) : (3, 2);
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                var brush = TryFindResource("B.TextMuted") as Brush ?? Brushes.Gray;
                double gap = 4, r = 1.1;
                double w = (_cols - 1) * gap, h = (_rows - 1) * gap;
                double x0 = (ActualWidth - w) / 2, y0 = (ActualHeight - h) / 2;
                for (int c = 0; c < _cols; c++)
                for (int k = 0; k < _rows; k++)
                    dc.DrawEllipse(brush, null, new Point(x0 + c * gap, y0 + k * gap), r, r);
            }
        }
    }
}
