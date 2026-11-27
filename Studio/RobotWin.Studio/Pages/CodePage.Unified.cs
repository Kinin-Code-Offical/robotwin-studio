using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [debug] The unified debug view: the code in the middle, its variables and the chip's registers stacked on the
    /// right, the timeline and the serial monitor under it, all in view at once (each otherwise a tab of the bottom row).
    /// The layout it replaced comes back when it is turned off.
    /// </summary>
    public partial class CodePage
    {
        private DockLayoutState? _beforeUnified;

        /// <summary>The unified view is on.</summary>
        internal bool UnifiedDebug => _beforeUnified != null;

        private void OnUnifiedDebug(object sender, System.Windows.RoutedEventArgs e) => ShowUnifiedDebug(UnifiedButton.IsChecked == true);

        internal void ShowUnifiedDebug(bool on)
        {
            if (_dock == null || on == UnifiedDebug)
            {
                UnifiedButton.IsChecked = UnifiedDebug;
                return;
            }

            if (on)
            {
                _beforeUnified = _dock.Snapshot();
                _dock.DockTo("vars", DockPlace.Right);
                _dock.DockTo("regs", DockPlace.Right);
                _dock.DockTo("serial", DockPlace.Bottom);
                _dock.DockTo("timeline", DockPlace.Bottom);
                _dock.ShowBottom(true, "timeline");
                ShowVariables();
                ShowRegisters();
                ShowTimeline();
                W.Status = Lang.T("Birleşik görünüm: değişkenler ve yazmaçlar sağda, zaman çizelgesi ve seri monitör altta. Yeniden basınca eski düzen.");
            }
            else
            {
                var before = _beforeUnified!;
                _beforeUnified = null;
                _dock.Restore(before);
            }

            UnifiedButton.IsChecked = UnifiedDebug;
        }
    }
}
