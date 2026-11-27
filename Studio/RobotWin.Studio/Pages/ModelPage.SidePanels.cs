using System.Windows;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [dock] The part editor's panels as the other pages': the library on the left, the parts and their properties on
    /// the right; each moves to the other side or the bottom, minimizes to a strip, tears off into its own window or hides
    /// (Shell/DockManager.cs), and the layout is kept. Blender's N minimizes the right side and brings it back, T hides the
    /// tool strip, Ctrl+Space (the workspace) hides everything round the view.
    /// </summary>
    public partial class ModelPage
    {
        private DockManager? _dock;

        /// <summary>[workspaces] The page's panels (a workspace saves and restores their layout).</summary>
        public DockManager? Panels => _dock;

        private void InitDock()
        {
            _dock = new DockManager("model", this, PageRoot, ContentGrid, 0, 4, DockTabs, DockHost, BottomArea, DockRow, DockLineRow);
            _dock.Add(new DockItem("library", "Kütüphane", LibraryPane, DockPlace.Left));
            _dock.Add(new DockItem("outliner", "Parçalar", OutlinerPane, DockPlace.Right));
            _dock.Add(new DockItem("properties", "Özellikler", PropsPane, DockPlace.Right));
            _dock.Add(new DockItem("study", "Statik analiz", StudyPane(), DockPlace.Left));   // [part-study]
            _dock.Start();
            _dock.SetVisible("study", false);   // opened by its toolbar button
        }

        private void ToggleSidePane()
        {
            if (_dock == null) return;
            bool shown = !_dock.IsMinimized(DockPlace.Right);
            _dock.Minimize(DockPlace.Right, shown);
            _ed.Status(Lang.T(shown ? "Yan panel gizli (N)" : "Yan panel açık (N)"));
        }
    }
}
