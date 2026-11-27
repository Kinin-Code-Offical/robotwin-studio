using System.Windows;
using System.Windows.Controls;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [dock] The code page's panels as the other pages': the explorer on the left and the bottom tabs (compiler, serial
    /// monitor, variables, registers, timeline, libraries) move to either side or the bottom, minimize to a strip, tear
    /// off into their own windows (a second screen too) or hide; the layout is kept and goes into the workspaces.
    /// </summary>
    public partial class CodePage
    {
        private DockManager? _dock;

        /// <summary>[workspaces] The page's panels (a workspace saves and restores their layout).</summary>
        public DockManager? Panels => _dock;

        private void InitDock()
        {
            // the libraries' tab is built in code (Pages/CodePage.Libraries.cs): its chip and its place under the strip
            var libChip = new RadioButton { Content = "Kütüphaneler", GroupName = "codedock", Margin = new Thickness(4, 0, 0, 0) };
            libChip.SetResourceReference(StyleProperty, "Chip");
            DockTabs.Children.Add(libChip);
            _libPane.Visibility = Visibility.Collapsed;
            DockHost.Children.Add(_libPane);

            _dock = new DockManager("code", this, PageRoot, ContentGrid, 0, 4, DockTabs, DockHost, BottomArea, DockRow, DockLineRow, DockSplitter, DockLine);
            var framed = new Thickness(6, 4, 6, 6);
            _dock.Add(new DockItem("explorer", "Kod", ExplorerPane, DockPlace.Left));
            _dock.Add(new DockItem("build", "Derleyici", BuildPane, DockPlace.Bottom) { CanBottom = true, Chip = DockBuild, FramedMargin = framed });
            _dock.Add(new DockItem("serial", "Seri monitör", SerialPane, DockPlace.Bottom) { CanBottom = true, Chip = DockSerial, FramedMargin = framed });
            _dock.Add(new DockItem("vars", "Değişkenler", VariablesPane, DockPlace.Bottom) { CanBottom = true, Chip = DockVars, FramedMargin = framed });
            _dock.Add(new DockItem("regs", "Yazmaçlar", RegistersPane, DockPlace.Bottom) { CanBottom = true, Chip = DockRegs, FramedMargin = framed });
            _dock.Add(new DockItem("timeline", "Zaman çizelgesi", TimelinePane, DockPlace.Bottom) { CanBottom = true, Chip = DockTimeline, FramedMargin = framed });
            _dock.Add(new DockItem("libs", "Kütüphaneler", _libPane, DockPlace.Bottom) { CanBottom = true, Chip = libChip, FramedMargin = framed });
            _dock.Shown += key =>
            {
                switch (key)
                {
                    case "serial": UpdateSerial(); break;
                    case "vars": ShowVariables(); break;
                    case "regs": ShowRegisters(); break;
                    case "timeline": ShowTimeline(); break;
                    case "libs": ShowLibraries(); break;
                }
            };
            _dock.StartFolded = true;   // [fold] the bottom tabs start closed, as on the other pages: a tab (or a build) opens them
            _dock.FoldEveryStart = true;
            _dock.Start();
        }
    }
}
