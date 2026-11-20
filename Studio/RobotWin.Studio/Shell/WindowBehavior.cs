using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// Native window behaviour for the custom-drawn title bar:
    /// <list type="bullet">
    /// <item>Windows 11 snap layouts when hovering the maximise button (the button answers WM_NCHITTEST as HTMAXBUTTON),</item>
    /// <item>correct maximised size on every monitor (work area, taskbar left visible) and true full screen,</item>
    /// <item>Alt+Space and right-click system menu,</item>
    /// <item>window position, size and monitor remembered across sessions (and pulled back on screen if that monitor is gone).</item>
    /// </list>
    /// The window keeps its standard frame style underneath the chrome, so minimise/maximise/restore
    /// animations, Aero Snap and Win+arrow keys behave exactly like any other Windows app.
    /// </summary>
    public sealed class WindowBehavior
    {
        private const int WmNcHitTest = 0x0084;
        private const int WmNcMouseMove = 0x00A0;
        private const int WmNcLButtonDown = 0x00A1;
        private const int WmNcLButtonUp = 0x00A2;
        private const int WmNcMouseLeave = 0x02A2;
        private const int WmGetMinMaxInfo = 0x0024;
        private const int HtMaxButton = 9;

        private readonly Window _window;
        private readonly FrameworkElement _maxButton;
        private bool _maxHover;
        private bool _maxPressed;

        public WindowBehavior(Window window, FrameworkElement maxButton)
        {
            _window = window;
            _maxButton = maxButton;
            window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(Hook);
            window.PreviewKeyDown += (_, e) =>
            {
                // Alt+Space: the system menu (move, size, minimise …) like every native window.
                if (e.Key == Key.System && e.SystemKey == Key.Space && Keyboard.Modifiers == ModifierKeys.Alt)
                {
                    ShowSystemMenu(new Point(8, 36));
                    e.Handled = true;
                }
            };
        }

        /// <summary>When true the window covers the whole monitor (taskbar included) while maximised.</summary>
        public bool FullScreen { get; set; }

        public void ShowSystemMenu(Point at)
        {
            var p = _window.PointToScreen(at);
            var dpi = VisualTreeHelper.GetDpi(_window);
            SystemCommands.ShowSystemMenu(_window, new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY));
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WmGetMinMaxInfo:
                    FitToMonitor(hwnd, lParam);
                    handled = true;
                    return IntPtr.Zero;

                case WmNcHitTest:
                    if (OverMaxButton(lParam))
                    {
                        SetHover(true);
                        handled = true;
                        return new IntPtr(HtMaxButton);
                    }

                    SetHover(false);
                    break;

                case WmNcMouseMove:
                    SetHover(wParam.ToInt32() == HtMaxButton);
                    break;

                case WmNcMouseLeave:
                    SetHover(false);
                    _maxPressed = false;
                    break;

                case WmNcLButtonDown when wParam.ToInt32() == HtMaxButton:
                    _maxPressed = true;
                    handled = true;
                    return IntPtr.Zero;

                case WmNcLButtonUp when wParam.ToInt32() == HtMaxButton:
                    if (_maxPressed)
                    {
                        _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    }

                    _maxPressed = false;
                    handled = true;
                    return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        private bool OverMaxButton(IntPtr lParam)
        {
            if (!_maxButton.IsVisible) return false;
            int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
            try
            {
                var local = _maxButton.PointFromScreen(new Point(x, y));
                return local.X >= 0 && local.Y >= 0 && local.X < _maxButton.ActualWidth && local.Y < _maxButton.ActualHeight;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void SetHover(bool on)
        {
            if (on == _maxHover) return;
            _maxHover = on;
            if (_maxButton is Control c) c.Background = on ? (Brush)Application.Current.FindResource("B.Hover") : Brushes.Transparent;
        }

        // ------------------------------------------------------------------ monitors

        [StructLayout(LayoutKind.Sequential)]
        private struct PointI
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public PointI Reserved;
            public PointI MaxSize;
            public PointI MaxPosition;
            public PointI MinTrackSize;
            public PointI MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RectI
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfo
        {
            public int Size;
            public RectI Monitor;
            public RectI Work;
            public int Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(PointI pt, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        private void FitToMonitor(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var monitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var area = FullScreen ? info.Monitor : info.Work;
                mmi.MaxPosition.X = area.Left - info.Monitor.Left;
                mmi.MaxPosition.Y = area.Top - info.Monitor.Top;
                mmi.MaxSize.X = area.Right - area.Left;
                mmi.MaxSize.Y = area.Bottom - area.Top;
                var dpi = VisualTreeHelper.GetDpi(_window);
                mmi.MinTrackSize.X = (int)(_window.MinWidth * dpi.DpiScaleX);
                mmi.MinTrackSize.Y = (int)(_window.MinHeight * dpi.DpiScaleY);
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        /// <summary>True when a point (device-independent screen units) lies on any monitor's work area.</summary>
        public static bool IsOnScreen(double left, double top, double width)
        {
            // The title bar's left third must be reachable to drag the window back.
            var probe = new PointI { X = (int)(left + Math.Min(width / 3, 200)), Y = (int)(top + 16) };
            var m = MonitorFromPoint(probe, 0); // MONITOR_DEFAULTTONULL
            return m != IntPtr.Zero;
        }
    }
}
