using System.Windows;
using System.Windows.Controls;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [dock] The circuit page's bottom tabs (oscilloscope, multimeter, fault finding, Monte Carlo, board pins, serial
    /// monitor, events) work like the world page's panels: the bottom pane is resized by its top edge, a tab can be
    /// hidden, docked beside the drawing or opened in its own window (drag it off, its buttons or a right click), and the
    /// layout is remembered.
    /// </summary>
    public partial class SchematicPage
    {
        private DockManager? _dock;

        /// <summary>The bottom tabs in their original order (the code picks them by this index).</summary>
        private static readonly string[] DockKeys = { "scope", "meter", "diag", "mc", "pins", "serial", "log", "ac" };

        private void InitLayout()
        {
            _dock = new DockManager("schematic", this, Root, CanvasArea, 0, 4, DockTabs, DockHost, BottomArea, DockRow, DockLineRow, DockSplitter, DockLine);
            var framed = new Thickness(6, 4, 6, 6);
            _dock.Add(new DockItem("scope", "Osiloskop", ScopePane, DockPlace.Bottom) { CanBottom = true, Chip = DockScope, FramedMargin = framed });
            _dock.Add(new DockItem("meter", "Multimetre", MeterPane, DockPlace.Bottom) { CanBottom = true, Chip = DockMeter, FramedMargin = framed });
            _dock.Add(new DockItem("diag", "Arıza tespiti", DiagPane, DockPlace.Bottom) { CanBottom = true, Chip = DockDiag, FramedMargin = framed });
            _dock.Add(new DockItem("mc", "Monte Carlo", McPane, DockPlace.Bottom) { CanBottom = true, Chip = DockMc, FramedMargin = framed });
            if (_dockAc != null && _acPane != null)
                _dock.Add(new DockItem("ac", "Frekans cevabı", _acPane, DockPlace.Bottom) { CanBottom = true, Chip = _dockAc, FramedMargin = framed });
            _dock.Add(new DockItem("pins", "Kart pinleri", PinsPane, DockPlace.Bottom) { CanBottom = true, Chip = DockPins, FramedMargin = framed });
            _dock.Add(new DockItem("serial", "Seri monitör", SerialPane, DockPlace.Bottom) { CanBottom = true, Chip = DockSerial, FramedMargin = framed });
            _dock.Add(new DockItem("log", "Olaylar", LogList, DockPlace.Bottom) { CanBottom = true, Chip = DockLog, FramedMargin = framed });
            _dock.Shown += key =>
            {
                if (key == "diag") RunDiagnosis();
                else if (key == "meter") BuildMeterPanel();
                else if (key == "mc") RefreshMonteCarloNets();
                else if (key == "ac") RefreshAcChoices();
            };
            _dock.Start();
        }

        /// <summary>Brings a bottom tab into view wherever the user put it (index into <see cref="DockKeys"/>).</summary>
        private void ShowDock(int index)
        {
            if (_dock != null) _dock.Reveal(DockKeys[index]);
        }

        /// <summary>The tab is on screen: the picked bottom tab, beside the drawing or in its own window.</summary>
        private bool DockShowing(int index) => _dock?.IsShowing(DockKeys[index]) ?? index == 0;

        /// <summary>The "Devre panelleri" entries of the main window's View menu.</summary>
        public void FillLayoutMenu(ItemsControl menu) => _dock?.FillMenu(menu);
    }
}
