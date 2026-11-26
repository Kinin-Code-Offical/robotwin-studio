using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [dock] The world page's movable panels: the palette, the inspector and every bottom tab (events, serial
    /// monitor, cables, RF, sensors) can sit on the left or right (stacked, resizable), be a bottom tab, open in
    /// its own window (drag its header or tab off) or be hidden. The layout is remembered between sessions.
    /// </summary>
    public partial class WorldPage
    {
        private DockManager? _dock;

        /// <summary>[workspaces] The page's panels (a workspace saves and restores their layout); null before the page is built.</summary>
        public DockManager? Panels => _dock;

        private void InitLayout()
        {
            _dock = new DockManager("world", this, PageRoot, MainArea, 0, 4, DockTabs, DockHost, BottomArea, DockRow, DockLineRow, DockSplitter, DockLine);
            _dock.Add(new DockItem("palette", "Palet", PalettePane, DockPlace.Left) { FramedMargin = new Thickness(10, 8, 4, 0) });
            _dock.Add(new DockItem("inspector", "Denetçi", InspectorPane, DockPlace.Right));
            var framed = new Thickness(10, 6, 10, 8);
            _dock.Add(new DockItem("log", "Olaylar", LogList, DockPlace.Bottom) { CanBottom = true, Chip = DockLog, FramedMargin = framed });
            _dock.Add(new DockItem("serial", "Seri monitör", SerialPanel, DockPlace.Bottom) { CanBottom = true, Chip = DockSerial, FramedMargin = framed });
            _dock.Add(new DockItem("cables", "Kablolar ve ölçümler", CablePanel, DockPlace.Bottom) { CanBottom = true, Chip = DockCables, FramedMargin = framed });
            _dock.Add(new DockItem("rf", "RF", RfPanel, DockPlace.Bottom) { CanBottom = true, Chip = DockRf, FramedMargin = framed });
            if (_dockSensors != null && _sensorsPanel != null)
                _dock.Add(new DockItem("sensors", "Sensörler", _sensorsPanel, DockPlace.Bottom) { CanBottom = true, Chip = _dockSensors, FramedMargin = framed });
            if (_dockHmi != null && _hmiPanel != null)
                _dock.Add(new DockItem("hmi", "HMI", _hmiPanel, DockPlace.Bottom) { CanBottom = true, Chip = _dockHmi, FramedMargin = framed });
            _dock.Shown += key =>
            {
                if (key == "cables") BuildCableTable();
                else if (key == "rf") UpdateRfPanel();
                else if (key == "sensors") UpdateSensorsPanel();
                else if (key == "hmi") BuildHmiPanel();
            };
            _dock.StartFolded = true;   // [fold] events, serial, cables … open when picked or when something arrives
            _dock.Start();
        }

        /// <summary>[guide] The run button, the cables tab, and whether the cables table is on screen.</summary>
        internal FrameworkElement RunButtonElement => RunButton;

        internal FrameworkElement CablesChipElement => DockCables;

        internal bool CablesShowing => DockShowing("cables", DockCables);

        internal void ShowCables() => RevealDock("cables", DockCables);

        /// <summary>The panel is on screen (picked bottom tab, side column or its own window); falls back to the tab before the layout exists.</summary>
        private bool DockShowing(string key, ToggleButton? chip) => _dock?.IsShowing(key) ?? chip?.IsChecked == true;

        /// <summary>Brings a bottom panel into view wherever the user put it.</summary>
        private void RevealDock(string key, ToggleButton chip)
        {
            if (_dock != null) _dock.Reveal(key);
            else chip.IsChecked = true;
        }

        /// <summary>The "Panel düzeni" entries for the main window's View menu.</summary>
        public void FillLayoutMenu(ItemsControl menu) => _dock?.FillMenu(menu);

        private void OnLayoutMenu(object sender, RoutedEventArgs e)
        {
            var m = new ContextMenu { PlacementTarget = LayoutButton, Placement = PlacementMode.Bottom };
            FillLayoutMenu(m);
            m.IsOpen = true;
        }

        /// <summary>--fault "template:dock": serial monitor under the inspector, events under the palette (docs picture).</summary>
        private void DockSnapshot()
        {
            if (_dock == null || _snapshotModes.Length == 0) return;
            if (_snapshotModes.Contains("dock"))
            {
                foreach (var (key, place) in new[] { ("serial", DockPlace.Right), ("log", DockPlace.Left) })
                {
                    var it = _dock.Items.FirstOrDefault(i => i.Key == key);
                    if (it != null) _dock.Dock(it, place);
                }
            }

            if (_snapshotModes.Contains("swap")) _dock.SwapSides();

            // "float": the serial monitor in its own window; "guides": the drop guides a drag shows, the left one lit;
            // "drop": a drag released at the page's left edge docks the events panel there (the status line says where it went).
            var serial = _dock.Items.FirstOrDefault(i => i.Key == "serial");
            if (serial != null && _snapshotModes.Contains("float")) _dock.Float(serial);
            if (serial != null && _snapshotModes.Contains("guides")) _dock.ShowGuides(serial, DockPlace.Left);
            if (_snapshotModes.Contains("drop") && _dock.Items.FirstOrDefault(i => i.Key == "log") is { } log)
            {
                _dock.Float(log);
                var edge = MainArea.TranslatePoint(new Point(30, MainArea.ActualHeight / 2), PageRoot);
                var bottom = MainArea.TranslatePoint(new Point(MainArea.ActualWidth / 2, MainArea.ActualHeight - 30), PageRoot);
                var middle = MainArea.TranslatePoint(new Point(MainArea.ActualWidth / 2, MainArea.ActualHeight / 3), PageRoot);
                var at = _dock.TargetAt(log, edge);
                W.Status = $"Bırakma testi: sol kenar → {at?.ToString() ?? "pencere"}, alt kenar → {_dock.TargetAt(log, bottom)?.ToString() ?? "pencere"}, orta → {_dock.TargetAt(log, middle)?.ToString() ?? "pencere"}; pencere açık: {(_dock.PlaceOf("log") == DockPlace.Float)}";
                if (at is { } place) _dock.Dock(log, place);
                W.Status += $"; bırakınca: {_dock.PlaceOf("log")}, seri monitör: {_dock.PlaceOf("serial")}";
            }
        }
    }
}
