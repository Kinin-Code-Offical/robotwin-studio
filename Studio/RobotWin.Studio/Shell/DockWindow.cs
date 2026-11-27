using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [dock] A panel torn off into its own window. It reports when the user drags it by the title bar
    /// (so the page can show where it would dock) and when the drag ends (so it can dock there).
    /// </summary>
    internal sealed class DockWindow : Window
    {
        private const int WmMoving = 0x0216, WmExitSizeMove = 0x0232;
        private bool _moving;

        public DockWindow(string title)
        {
            Title = title;
            WindowStyle = WindowStyle.ToolWindow;
            ShowInTaskbar = false;
            ShowActivated = true;
            SetResourceReference(BackgroundProperty, "B.Base");
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");
            if (Application.Current?.MainWindow is { } main)
            {
                FontFamily = main.FontFamily;
                FontSize = main.FontSize;
            }

            Places = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 6) };
            Body = new Border();
            var root = new DockPanel { Margin = new Thickness(8, 6, 8, 8) };
            DockPanel.SetDock(Places, Dock.Top);
            root.Children.Add(Places);
            root.Children.Add(Body);
            Content = root;
        }

        /// <summary>Holds the panel's content.</summary>
        public Border Body { get; }

        /// <summary>"Sola / Sağa / Alta" buttons that dock the window back without dragging.</summary>
        public StackPanel Places { get; }

        /// <summary>Set when the dock manager closes the window itself (docking it elsewhere, shutting down).</summary>
        public bool ClosedByManager { get; set; }

        public event Action? Moving;

        public event Action? MoveEnded;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (PresentationSource.FromVisual(this) is HwndSource src)
            {
                src.AddHook(Hook);
                try
                {
                    int dark = 1;
                    DwmSetWindowAttribute(src.Handle, 20, ref dark, sizeof(int));
                }
                catch (DllNotFoundException)
                {
                }
                catch (EntryPointNotFoundException)
                {
                }
            }
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmMoving)
            {
                _moving = true;
                Moving?.Invoke();
            }
            else if (msg == WmExitSizeMove && _moving)
            {
                _moving = false;
                MoveEnded?.Invoke();
            }

            return IntPtr.Zero;
        }

        /// <summary>The cursor in physical screen pixels.</summary>
        public static Point CursorPosition()
        {
            return GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(double.NaN, double.NaN);
        }

        /// <summary>Physical screen pixels → device-independent units of <paramref name="v"/>'s monitor.</summary>
        public static Point ToDip(Visual v, Point device)
        {
            var src = PresentationSource.FromVisual(v);
            return src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(device) : device;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint p);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
