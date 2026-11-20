using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Sandbox.Radio;

namespace RobotWin.Studio.Input
{
    /// <summary>
    /// On-screen PS5-style controller for users without hardware. Mouse: drag the sticks,
    /// press buttons, drag the triggers down. Keyboard (while the control has focus):
    /// WASD left stick, IJKL right stick, arrows D-pad, Space ✕, E ○, Q □, R △, Z L1, C R1,
    /// Shift L2, F R2, Tab Create, Enter Options, P PS, T touchpad.
    /// Writes into <see cref="VirtualGamepadSource.State"/>; draws the lightbar the ESP32 set.
    /// </summary>
    public sealed class VirtualGamepad : FrameworkElement
    {
        private const double W = 520, H = 330;

        private readonly GamepadState _mouse = new GamepadState();
        private readonly HashSet<Key> _keys = new HashSet<Key>();
        private string? _dragging;
        private Point _dragOrigin;

        private static readonly Brush Body = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF4));
        private static readonly Brush Dark = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x20));
        private static readonly Brush Mid = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42));
        private static readonly Brush Pressed = new SolidColorBrush(Color.FromRgb(0x6E, 0x9B, 0xFF));
        private static readonly Pen Edge = new Pen(new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xC0)), 1.5);
        private static readonly Typeface Face = new Typeface("Segoe UI Symbol");

        private static readonly (string Id, Point C, double R, string Label, Color Color)[] FaceButtons =
        {
            ("Y", new Point(410, 108), 17, "△", Color.FromRgb(0x3C, 0xC8, 0xA0)),
            ("B", new Point(445, 143), 17, "○", Color.FromRgb(0xE5, 0x48, 0x4D)),
            ("A", new Point(410, 178), 17, "✕", Color.FromRgb(0x5A, 0x8C, 0xF0)),
            ("X", new Point(375, 143), 17, "□", Color.FromRgb(0xE0, 0x7C, 0xC8))
        };

        private static readonly (string Id, Rect R, string Label)[] DpadButtons =
        {
            ("UP", new Rect(98, 92, 28, 32), "▲"), ("DOWN", new Rect(98, 162, 28, 32), "▼"),
            ("LEFT", new Rect(60, 128, 32, 28), "◀"), ("RIGHT", new Rect(132, 128, 32, 28), "▶")
        };

        private static readonly Point LeftStick = new Point(185, 225), RightStick = new Point(335, 225);
        private const double StickTravel = 30;

        public VirtualGamepad()
        {
            Focusable = true;
            Width = W;
            Height = H;
            Cursor = Cursors.Hand;
            ToolTip = "Sanal kumanda: çubukları sürükleyin, tuşlara tıklayın. Klavye: WASD sol çubuk, IJKL sağ çubuk, oklar D-pad, Boşluk ✕, E ○, Q □, R △, Z L1, C R1, Shift L2, F R2, Tab Create, Enter Options, P PS.";
            VirtualGamepadSource.Instance.FeedbackChanged += () => Dispatcher.BeginInvoke(new Action(InvalidateVisual));
        }

        private static GamepadState Target => VirtualGamepadSource.Instance.State;

        // ================================================================ state

        private void Publish()
        {
            var s = new GamepadState();
            s.CopyFrom(_mouse);
            double lx = s.AxisX / 512.0, ly = s.AxisY / 512.0, rx = s.AxisRX / 512.0, ry = s.AxisRY / 512.0;
            if (_keys.Contains(Key.A)) lx = -1;
            if (_keys.Contains(Key.D)) lx = 1;
            if (_keys.Contains(Key.W)) ly = -1;
            if (_keys.Contains(Key.S)) ly = 1;
            if (_keys.Contains(Key.J)) rx = -1;
            if (_keys.Contains(Key.L)) rx = 1;
            if (_keys.Contains(Key.I)) ry = -1;
            if (_keys.Contains(Key.K)) ry = 1;
            s.AxisX = GamepadState.Axis(lx);
            s.AxisY = GamepadState.Axis(ly);
            s.AxisRX = GamepadState.Axis(rx);
            s.AxisRY = GamepadState.Axis(ry);
            if (_keys.Contains(Key.LeftShift) || _keys.Contains(Key.RightShift)) s.Brake = 1023;
            if (_keys.Contains(Key.F)) s.Throttle = 1023;
            void B(Key k, ushort mask)
            {
                if (_keys.Contains(k)) s.Buttons |= mask;
            }

            B(Key.Space, GamepadState.ButtonA);
            B(Key.E, GamepadState.ButtonB);
            B(Key.Q, GamepadState.ButtonX);
            B(Key.R, GamepadState.ButtonY);
            B(Key.Z, GamepadState.ButtonL1);
            B(Key.C, GamepadState.ButtonR1);
            if (s.Brake > 100) s.Buttons |= GamepadState.ButtonL2;
            if (s.Throttle > 100) s.Buttons |= GamepadState.ButtonR2;
            if (_keys.Contains(Key.Up)) s.Dpad |= GamepadState.DpadUp;
            if (_keys.Contains(Key.Down)) s.Dpad |= GamepadState.DpadDown;
            if (_keys.Contains(Key.Left)) s.Dpad |= GamepadState.DpadLeft;
            if (_keys.Contains(Key.Right)) s.Dpad |= GamepadState.DpadRight;
            if (_keys.Contains(Key.Tab)) s.MiscButtons |= GamepadState.MiscSelect;
            if (_keys.Contains(Key.Enter)) s.MiscButtons |= GamepadState.MiscStart;
            if (_keys.Contains(Key.P)) s.MiscButtons |= GamepadState.MiscSystem;
            if (_keys.Contains(Key.T)) s.MiscButtons |= GamepadState.MiscCapture;
            Target.CopyFrom(s);
            InvalidateVisual();
        }

        /// <summary>Keys the pad uses (others are left to the window).</summary>
        public static bool Handles(Key k) => k is Key.W or Key.A or Key.S or Key.D or Key.I or Key.J or Key.K or Key.L or Key.Up or Key.Down or Key.Left or Key.Right
            or Key.Space or Key.E or Key.Q or Key.R or Key.Z or Key.C or Key.LeftShift or Key.RightShift or Key.F or Key.Tab or Key.Enter or Key.P or Key.T;

        public void KeyChanged(Key key, bool down)
        {
            if (!Handles(key)) return;
            bool changed = down ? _keys.Add(key) : _keys.Remove(key);
            if (changed) Publish();
        }

        public void ReleaseAll()
        {
            _keys.Clear();
            _mouse.Clear();
            _dragging = null;
            Publish();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Handles(e.Key))
            {
                KeyChanged(e.Key, true);
                e.Handled = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (Handles(e.Key))
            {
                KeyChanged(e.Key, false);
                e.Handled = true;
            }
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            _keys.Clear();
            Publish();
        }

        // ================================================================ mouse

        private string? HitTestPad(Point p)
        {
            foreach (var f in FaceButtons)
            {
                if ((p - f.C).Length <= f.R + 3) return f.Id;
            }

            foreach (var d in DpadButtons)
            {
                if (d.R.Contains(p)) return d.Id;
            }

            if ((p - LeftStick).Length <= 38) return "LS";
            if ((p - RightStick).Length <= 38) return "RS";
            if (new Rect(70, 14, 90, 22).Contains(p)) return "L2";
            if (new Rect(360, 14, 90, 22).Contains(p)) return "R2";
            if (new Rect(70, 40, 90, 18).Contains(p)) return "L1";
            if (new Rect(360, 40, 90, 18).Contains(p)) return "R1";
            if (new Rect(195, 55, 130, 70).Contains(p)) return "TOUCH";
            if ((p - new Point(260, 160)).Length <= 14) return "PS";
            if (new Rect(168, 62, 22, 14).Contains(p)) return "CREATE";
            if (new Rect(330, 62, 22, 14).Contains(p)) return "OPTIONS";
            return null;
        }

        private void SetMouseButton(string id, bool on)
        {
            switch (id)
            {
                case "A": _mouse.Set(GamepadState.ButtonA, on); break;
                case "B": _mouse.Set(GamepadState.ButtonB, on); break;
                case "X": _mouse.Set(GamepadState.ButtonX, on); break;
                case "Y": _mouse.Set(GamepadState.ButtonY, on); break;
                case "L1": _mouse.Set(GamepadState.ButtonL1, on); break;
                case "R1": _mouse.Set(GamepadState.ButtonR1, on); break;
                case "UP": _mouse.SetDpad(GamepadState.DpadUp, on); break;
                case "DOWN": _mouse.SetDpad(GamepadState.DpadDown, on); break;
                case "LEFT": _mouse.SetDpad(GamepadState.DpadLeft, on); break;
                case "RIGHT": _mouse.SetDpad(GamepadState.DpadRight, on); break;
                case "PS": _mouse.SetMisc(GamepadState.MiscSystem, on); break;
                case "CREATE": _mouse.SetMisc(GamepadState.MiscSelect, on); break;
                case "OPTIONS": _mouse.SetMisc(GamepadState.MiscStart, on); break;
                case "TOUCH": _mouse.SetMisc(GamepadState.MiscCapture, on); break;
                case "L2":
                    _mouse.Brake = on ? 1023 : 0;
                    _mouse.Set(GamepadState.ButtonL2, on);
                    break;
                case "R2":
                    _mouse.Throttle = on ? 1023 : 0;
                    _mouse.Set(GamepadState.ButtonR2, on);
                    break;
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            var p = Scale(e.GetPosition(this));
            _dragging = HitTestPad(p);
            _dragOrigin = p;
            if (_dragging == null) return;
            if (_dragging != "LS" && _dragging != "RS") SetMouseButton(_dragging, true);
            CaptureMouse();
            Publish();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging == null) return;
            var p = Scale(e.GetPosition(this));
            if (_dragging == "LS" || _dragging == "RS")
            {
                var c = _dragging == "LS" ? LeftStick : RightStick;
                var v = p - c;
                if (v.Length > StickTravel) v *= StickTravel / v.Length;
                int x = GamepadState.Axis(v.X / StickTravel), y = GamepadState.Axis(v.Y / StickTravel);
                if (_dragging == "LS")
                {
                    _mouse.AxisX = x;
                    _mouse.AxisY = y;
                }
                else
                {
                    _mouse.AxisRX = x;
                    _mouse.AxisRY = y;
                }

                Publish();
            }
            else if (_dragging == "L2" || _dragging == "R2")
            {
                // Drag down to press the trigger partially (0…60 px).
                double t = Math.Max(0.15, Math.Min(1.0, (p.Y - _dragOrigin.Y + 10) / 60.0));
                if (_dragging == "L2") _mouse.Brake = GamepadState.Trigger(t);
                else _mouse.Throttle = GamepadState.Trigger(t);
                Publish();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_dragging == "LS")
            {
                _mouse.AxisX = 0;
                _mouse.AxisY = 0;
            }
            else if (_dragging == "RS")
            {
                _mouse.AxisRX = 0;
                _mouse.AxisRY = 0;
            }
            else if (_dragging != null)
            {
                SetMouseButton(_dragging, false);
            }

            _dragging = null;
            ReleaseMouseCapture();
            Publish();
        }

        private Point Scale(Point p)
        {
            double k = Math.Min(ActualWidth / W, ActualHeight / H);
            if (k <= 0) k = 1;
            return new Point(p.X / k, p.Y / k);
        }

        // ================================================================ drawing

        protected override void OnRender(DrawingContext dc)
        {
            double k = Math.Min(ActualWidth / W, ActualHeight / H);
            if (k <= 0) return;
            dc.PushTransform(new ScaleTransform(k, k));
            var s = Target;
            var fb = VirtualGamepadSource.Instance.Feedback;

            // Triggers and bumpers
            Trigger(dc, new Rect(70, 14, 90, 22), "L2", s.Brake / 1023.0);
            Trigger(dc, new Rect(360, 14, 90, 22), "R2", s.Throttle / 1023.0);
            dc.DrawRoundedRectangle(s.Pressed(GamepadState.ButtonL1) ? Pressed : Mid, null, new Rect(70, 40, 90, 18), 8, 8);
            dc.DrawRoundedRectangle(s.Pressed(GamepadState.ButtonR1) ? Pressed : Mid, null, new Rect(360, 40, 90, 18), 8, 8);
            Text(dc, "L1", new Point(115, 49), 11, Brushes.White);
            Text(dc, "R1", new Point(405, 49), 11, Brushes.White);

            // Body: two grips joined by the centre section
            var body = new StreamGeometry();
            using (var g = body.Open())
            {
                g.BeginFigure(new Point(60, 70), true, true);
                g.BezierTo(new Point(120, 52), new Point(400, 52), new Point(460, 70), true, true);
                g.BezierTo(new Point(510, 90), new Point(525, 250), new Point(495, 305), true, true);
                g.BezierTo(new Point(470, 330), new Point(420, 320), new Point(395, 270), true, true);
                g.BezierTo(new Point(330, 250), new Point(190, 250), new Point(125, 270), true, true);
                g.BezierTo(new Point(100, 320), new Point(50, 330), new Point(25, 305), true, true);
                g.BezierTo(new Point(-5, 250), new Point(10, 90), new Point(60, 70), true, true);
            }

            body.Freeze();
            dc.DrawGeometry(Body, Edge, body);
            dc.DrawRoundedRectangle(Dark, null, new Rect(150, 130, 220, 110), 30, 30);   // black centre

            // Touchpad with the lightbar strips on both sides
            var light = fb.ColorSet ? Color.FromRgb(fb.Red, fb.Green, fb.Blue) : Color.FromRgb(0x30, 0x60, 0xFF);
            var lightBrush = new SolidColorBrush(light);
            dc.DrawRoundedRectangle((s.MiscButtons & GamepadState.MiscCapture) != 0 ? Pressed : new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x2A)), null, new Rect(195, 55, 130, 70), 10, 10);
            dc.DrawRoundedRectangle(lightBrush, null, new Rect(188, 60, 4, 60), 2, 2);
            dc.DrawRoundedRectangle(lightBrush, null, new Rect(328, 60, 4, 60), 2, 2);
            Text(dc, "dokunmatik", new Point(260, 90), 10, Brushes.Gray);
            dc.DrawRoundedRectangle((s.MiscButtons & GamepadState.MiscSelect) != 0 ? Pressed : Mid, null, new Rect(168, 62, 22, 14), 5, 5);
            dc.DrawRoundedRectangle((s.MiscButtons & GamepadState.MiscStart) != 0 ? Pressed : Mid, null, new Rect(330, 62, 22, 14), 5, 5);
            dc.DrawEllipse((s.MiscButtons & GamepadState.MiscSystem) != 0 ? Pressed : Mid, null, new Point(260, 160), 12, 12);
            Text(dc, "PS", new Point(260, 160), 9, Brushes.White);
            // Player LEDs
            for (int i = 0; i < 5; i++)
            {
                bool on = (fb.PlayerLeds & (1 << i)) != 0;
                dc.DrawRoundedRectangle(on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x58)), null, new Rect(240 + i * 8, 133, 5, 3), 1, 1);
            }

            foreach (var d in DpadButtons)
            {
                bool on = d.Id switch
                {
                    "UP" => (s.Dpad & GamepadState.DpadUp) != 0,
                    "DOWN" => (s.Dpad & GamepadState.DpadDown) != 0,
                    "LEFT" => (s.Dpad & GamepadState.DpadLeft) != 0,
                    _ => (s.Dpad & GamepadState.DpadRight) != 0
                };
                dc.DrawRoundedRectangle(on ? Pressed : Mid, null, d.R, 5, 5);
                Text(dc, d.Label, new Point(d.R.X + d.R.Width / 2, d.R.Y + d.R.Height / 2), 12, Brushes.White);
            }

            foreach (var f in FaceButtons)
            {
                bool on = f.Id switch { "A" => s.Pressed(GamepadState.ButtonA), "B" => s.Pressed(GamepadState.ButtonB), "X" => s.Pressed(GamepadState.ButtonX), _ => s.Pressed(GamepadState.ButtonY) };
                dc.DrawEllipse(on ? Pressed : Dark, null, f.C, f.R, f.R);
                Text(dc, f.Label, f.C, 16, new SolidColorBrush(f.Color));
            }

            Stick(dc, LeftStick, s.AxisX, s.AxisY, s.Pressed(GamepadState.ButtonThumbL));
            Stick(dc, RightStick, s.AxisRX, s.AxisRY, s.Pressed(GamepadState.ButtonThumbR));

            if (fb.WeakMotor > 0 || fb.StrongMotor > 0) Text(dc, "〰 titreşim", new Point(260, 300), 12, Brushes.OrangeRed);
            dc.Pop();
        }

        private static void Stick(DrawingContext dc, Point c, int x, int y, bool pressed)
        {
            dc.DrawEllipse(Mid, null, c, 36, 36);
            var knob = new Point(c.X + x / 512.0 * StickTravel, c.Y + y / 512.0 * StickTravel);
            dc.DrawEllipse(pressed ? Pressed : Dark, new Pen(Brushes.Black, 1), knob, 22, 22);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60)), 2), knob, 15, 15);
        }

        private static void Trigger(DrawingContext dc, Rect r, string label, double value)
        {
            dc.DrawRoundedRectangle(Mid, null, r, 8, 8);
            if (value > 0) dc.DrawRoundedRectangle(Pressed, null, new Rect(r.X, r.Y, r.Width * Math.Min(1, value), r.Height), 8, 8);
            Text(dc, label, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), 11, Brushes.White);
        }

        private static void Text(DrawingContext dc, string text, Point center, double size, Brush brush)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, brush, 1.0);
            dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
        }

        protected override Size MeasureOverride(Size availableSize) => new Size(W, H);
    }

    /// <summary>Small tool window hosting the virtual pad (keeps keyboard input away from the world editor's shortcuts).</summary>
    public sealed class VirtualGamepadWindow : Window
    {
        private static VirtualGamepadWindow? _instance;
        private readonly VirtualGamepad _pad = new VirtualGamepad();

        private VirtualGamepadWindow()
        {
            Title = "Sanal kumanda (PS5 düzeni)";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.ToolWindow;
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17));
            Topmost = true;
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(_pad);
            panel.Children.Add(new TextBlock
            {
                Text = "Klavye: WASD sol çubuk · IJKL sağ çubuk · oklar D-pad · Boşluk ✕ · E ○ · Q □ · R △ · Z L1 · C R1 · Shift L2 · F R2 · Tab Create · Enter Options · P PS · T dokunmatik",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA4)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0)
            });
            Content = panel;
            PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (!VirtualGamepad.Handles(key)) return;
                _pad.KeyChanged(key, true);
                e.Handled = true;
            };
            PreviewKeyUp += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (!VirtualGamepad.Handles(key)) return;
                _pad.KeyChanged(key, false);
                e.Handled = true;
            };
            Deactivated += (_, _) => _pad.ReleaseAll();
            Closed += (_, _) =>
            {
                _pad.ReleaseAll();
                _instance = null;
            };
        }

        public static void ShowPad(Window? owner)
        {
            if (_instance == null)
            {
                _instance = new VirtualGamepadWindow();
                if (owner != null && owner.IsVisible) _instance.Owner = owner;
                _instance.Show();
            }
            else
            {
                _instance.Activate();
            }
        }
    }
}
