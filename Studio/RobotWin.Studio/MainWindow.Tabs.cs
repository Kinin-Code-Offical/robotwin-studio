using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio
{
    /// <summary>Document tabs can be dragged sideways to reorder them, like in a browser.</summary>
    public partial class MainWindow
    {
        private OpenDocument? _dragTab;
        private Point _dragStart;
        private bool _dragging;

        private void SetupTabDrag()
        {
            Tabs.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _dragTab = DocumentAt(e.OriginalSource as DependencyObject);
                _dragStart = e.GetPosition(Tabs);
                _dragging = false;
            };
            Tabs.PreviewMouseMove += (_, e) =>
            {
                if (_dragTab == null || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(Tabs);
                if (!_dragging)
                {
                    if (System.Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
                    _dragging = true;
                    Tabs.CaptureMouse();
                    Mouse.OverrideCursor = Cursors.SizeWE;
                }

                // Swap with the neighbour the pointer has moved over.
                int from = W.Documents.IndexOf(_dragTab);
                if (from < 0) return;
                for (int i = 0; i < W.Documents.Count; i++)
                {
                    if (i == from || Tabs.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
                    var left = c.TranslatePoint(new Point(0, 0), Tabs).X;
                    double mid = left + c.ActualWidth / 2;
                    if ((i > from && p.X > mid) || (i < from && p.X < mid))
                    {
                        W.Documents.Move(from, i);
                        break;
                    }
                }
            };
            Tabs.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (_dragging)
                {
                    Tabs.ReleaseMouseCapture();
                    Mouse.OverrideCursor = null;
                    if (_dragTab != null) W.Activate(_dragTab);
                    e.Handled = true;
                }

                _dragTab = null;
                _dragging = false;
            };
            Tabs.LostMouseCapture += (_, _) =>
            {
                if (!_dragging) return;
                Mouse.OverrideCursor = null;
                _dragging = false;
                _dragTab = null;
            };
        }

        private static OpenDocument? DocumentAt(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is Button) return null;   // the close button keeps working normally
                if (d is FrameworkElement fe && fe.DataContext is OpenDocument doc) return doc;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }

            return null;
        }
    }
}
