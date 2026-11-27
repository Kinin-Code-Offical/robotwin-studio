using System.Windows;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [nt] Blender's N and T in the part editor: N slides the side panel (parts and properties) out of the way and back,
    /// T hides the tool shelf; Ctrl+Space (the workspace) hides everything round the view.
    /// </summary>
    public partial class ModelPage
    {
        private GridLength _sideWidth = new GridLength(318);

        private void ToggleSidePane()
        {
            bool shown = SidePane.Visibility == Visibility.Visible;
            if (shown)
            {
                _sideWidth = SideColumn.Width;
                SideColumn.MinWidth = 0;
                SideColumn.Width = new GridLength(0);
                SideLineColumn.Width = new GridLength(0);
                SidePane.Visibility = Visibility.Collapsed;
            }
            else
            {
                SidePane.Visibility = Visibility.Visible;
                SideColumn.Width = _sideWidth;
                SideColumn.MinWidth = 240;
                SideLineColumn.Width = new GridLength(1);
            }

            _ed.Status(Lang.T(shown ? "Yan panel gizli (N)" : "Yan panel açık (N)"));
        }
    }
}
