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
        private static readonly string[] DockKeys = { "scope", "meter", "diag", "mc", "pins", "serial", "log", "ac", "nets" };

        private void InitLayout()
        {
            _dock = new DockManager("schematic", this, Root, MainArea, 0, 4, DockTabs, DockHost, BottomArea, DockRow, DockLineRow, DockSplitter, DockLine);
            // [dock-min] the library and the inspector move, minimize to a strip and tear off like every other panel
            _dock.Add(new DockItem("library", "Parça kütüphanesi", LibraryHost, DockPlace.Left));
            _dock.Add(new DockItem("inspector", "Özellikler", InspectorHost, DockPlace.Right));
            _dock.Changed += () =>
            {
                _libraryOpen = _dock.PlaceOf("library") != DockPlace.Hidden;
                _inspectorOpen = _dock.PlaceOf("inspector") != DockPlace.Hidden;
                LibraryToggle.IsChecked = _libraryOpen && !_dock.Collapsed;
                InspectorToggle.IsChecked = _inspectorOpen && !_dock.Collapsed;
            };
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
            if (_dockNets != null && _netsPane != null)
                _dock.Add(new DockItem("nets", "Hatlar", _netsPane, DockPlace.Bottom) { CanBottom = true, Chip = _dockNets, FramedMargin = framed });   // [net-navigator]
            _dock.Shown += key =>
            {
                if (key == "diag") RunDiagnosis();
                else if (key == "meter") BuildMeterPanel();
                else if (key == "mc") RefreshMonteCarloNets();
                else if (key == "ac") RefreshAcChoices();
                else if (key == "nets") RefreshNets();
            };
            _dock.Start();
        }

        /// <summary>Brings a bottom tab into view wherever the user put it (index into <see cref="DockKeys"/>).</summary>
        /// <summary>[palette] The part selected on the sheet, if any.</summary>
        public RobotTwin.CoreSim.Design.SchematicPart? SelectedPart => System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<RobotTwin.CoreSim.Design.SchematicPart>(Canvas.Selection));

        /// <summary>[workspaces] The page's panels (a workspace saves and restores their layout).</summary>
        public DockManager? Panels => _dock;

        /// <summary>[palette] Brings the oscilloscope tab forward.</summary>
        public void ShowScope() => ShowDock(0);

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
