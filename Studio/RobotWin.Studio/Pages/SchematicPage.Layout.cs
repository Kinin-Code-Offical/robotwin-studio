using System;
using System.Linq;
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

        // [overlays] the tools down the canvas' left edge and the drawing tools as a strip of their own (Shell/PageOverlays.cs)
        private OverlayHost? _overlays;

        /// <summary>[crossprobe] Selecting what another page selected (it is not announced back).</summary>
        private bool _crossSelecting;

        /// <summary>[crossprobe] A part was picked on another page while this one was out of sight.</summary>
        private bool _revealCross;

        /// <summary>
        /// [crossprobe] Coming back to the sheet after a part was picked on another page: the part is brought into view if it
        /// is off the screen (the zoom stays), so the context follows from page to page; a view looked at is never moved.
        /// </summary>
        private void RevealCrossSelection()
        {
            if (!_revealCross) return;
            _revealCross = false;
            var part = W.Schematic.Parts.FirstOrDefault(x => CrossProbe.IsSelected(x.Uid, x.RefDes));
            if (part != null) Dispatcher.BeginInvoke(new Action(() => Canvas.EnsureVisible(part.Position)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>[workspaces] The tool strips over the sheet.</summary>
        public OverlayHost? ToolOverlays => _overlays;
        private ToolPanel? _drawPanel;

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
            _dock.StartFolded = true;   // [fold] the oscilloscope and the other instruments start closed: a tab opens them
            _dock.FoldEveryStart = true;
            _dock.Start();
            InitToolStrips();
        }

        /// <summary>
        /// [overlays] The World view's tool strip on the circuit page: the tools (select, wire, label, power, no-connect,
        /// junction, eraser, text, sheets, drawing, highlight, meter leads) in a column on the canvas' left edge, the drawing
        /// tools (line, rectangle, circle, arc, Bezier, rule area, table, picture) in a strip of their own the Çiz button opens.
        /// </summary>
        private void InitToolStrips()
        {
            // [flyout] KiCad's label group: a net label, a global label, a hierarchical label (hold or right-click)
            Theme.ToolFlyout.Attach(ToolLabel, new[]
            {
                new Theme.ToolChoice("Label", "Hat etiketi", "I.Label", "L"), new Theme.ToolChoice("GlobalLabel", "Global etiket", "I.GlobalLabel", "Ctrl+L"),
                new Theme.ToolChoice("HierLabel", "Hiyerarşik etiket", "I.HierLabel", "H")
            });
            var items = PageOverlays.ItemsOf(DrawTools);
            DrawToolsHost.Visibility = Visibility.Collapsed;
            ToolSep0.Visibility = Visibility.Collapsed;
            _drawPanel = new ToolPanel("draw", "Çizim", "I.DrawRect", new[]
            {
                new Theme.ToolChoice(nameof(Editor.EditorTool.Line), "Çizgi", "I.DrawLine"), new Theme.ToolChoice(nameof(Editor.EditorTool.Rectangle), "Dikdörtgen", "I.DrawRect"),
                new Theme.ToolChoice(nameof(Editor.EditorTool.Circle), "Daire", "I.DrawCircle"), new Theme.ToolChoice(nameof(Editor.EditorTool.Arc), "Yay", "I.DrawArc"),
                new Theme.ToolChoice(nameof(Editor.EditorTool.Bezier), "Bezier eğrisi", "I.DrawBezier"),
                new Theme.ToolChoice(nameof(Editor.EditorTool.RuleArea), "Kural alanı: iki köşe, sonra hat sınıfı", "I.RuleArea"),
                new Theme.ToolChoice(nameof(Editor.EditorTool.Table), "Tablo: tıklanan yer sol üst köşesi olur", "I.Table"),
                new Theme.ToolChoice("Image", "Resim…: imleçle gelir, tıklayınca yerleşir", "I.Picture")
            }, tag => Canvas.Tool.ToString() == tag, PickDrawing);
            var tools = new SceneOverlay("tools", "Araçlar", (System.Windows.Media.Geometry)FindResource("I.Pointer"), o => PageOverlays.Strip(o, items)) { CanHide = false };
            _overlays = PageOverlays.Attach((Panel)Canvas.Parent, "schematic", "tools:Left:40:40:Vertical:0:1:Left;draw:Left:40:40:Vertical:0:0:Left", new[] { tools, _drawPanel.Overlay });
            _drawPanel.Overlay.Shown = false;   // opens with the Çiz button
            _overlays.Arrange();
            _overlays.LayoutChanged += (_, _) => SyncTools();   // [workspaces] a workspace opened or closed the drawing strip
        }

        private void PickDrawing(string tag)
        {
            if (tag == "Image")
            {
                // [image] a picture from a file: it follows the cursor until a click puts it down
                if (RobotWin.Studio.Theme.Pictures.Pick(Window.GetWindow(this)) is { } data) Canvas.BeginImage(data);
            }
            else if (System.Enum.TryParse(tag, out Editor.EditorTool tool)) Canvas.Tool = tool;

            Canvas.Focus();
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
