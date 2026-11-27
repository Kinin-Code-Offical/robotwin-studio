using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Pcb;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Theme;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [flyout] The tool strip's groups (KiCad's and Blender's flyouts): drawing (line, rectangle, circle, arc, polygon) and
    /// holes (mounting hole, via); and the options bar of the active tool at the canvas' top left.
    /// </summary>
    public partial class PcbPage
    {
        // [overlays] the tool strip on the board's left edge (the World view's), the drawing tools as a strip of their own
        private OverlayHost? _overlays;
        private SceneOverlay? _toolsOverlay;
        private ToolPanel? _drawPanel;
        private List<System.Windows.Controls.Primitives.ToggleButton> _toolButtons = new List<System.Windows.Controls.Primitives.ToggleButton>();

        private static readonly PcbTool[] DrawingTools = { PcbTool.Line, PcbTool.Rectangle, PcbTool.Circle, PcbTool.Arc, PcbTool.Polygon, PcbTool.Bezier, PcbTool.Image, PcbTool.Table };

        private void InitToolGroups()
        {
            var items = PageOverlays.ItemsOf(ToolStrip);
            _toolButtons = items.OfType<System.Windows.Controls.Primitives.ToggleButton>().ToList();
            ContentGrid.ColumnDefinitions[0].Width = new GridLength(0);
            ToolStripHost.Visibility = Visibility.Collapsed;
            _drawPanel = new ToolPanel("draw", "Çizim", "I.DrawRect", new[]
            {
                new ToolChoice(nameof(PcbTool.Line), "Çizgi", "I.DrawLine"),
                new ToolChoice(nameof(PcbTool.Rectangle), "Dikdörtgen", "I.DrawRect"),
                new ToolChoice(nameof(PcbTool.Circle), "Daire", "I.DrawCircle"),
                new ToolChoice(nameof(PcbTool.Arc), "Yay", "I.DrawArc"),
                new ToolChoice(nameof(PcbTool.Polygon), "Çokgen", "I.DrawPolygon"),
                new ToolChoice(nameof(PcbTool.Bezier), "Bezier eğrisi", "I.DrawBezier"),   // [bezier]
                new ToolChoice(nameof(PcbTool.Image), "Resim…", "I.Picture"),   // [image]
                new ToolChoice(nameof(PcbTool.Table), "Tablo", "I.Table")   // [table]
            }, tag => Canvas.Tool.ToString() == tag, tag =>
            {
                if (!Enum.TryParse(tag, out PcbTool tool)) return;
                Show3D(false);
                Canvas.SetTool(tool);
                Canvas.Focus();
                SyncTools();
            });
            _toolsOverlay = new SceneOverlay("tools", "Araçlar", (System.Windows.Media.Geometry)FindResource("I.Pointer"), o => PageOverlays.Strip(o, items)) { CanHide = false };
            _overlays = PageOverlays.Attach(ContentGrid, "pcb", "tools:Left:40:40:Vertical:0:1:Left;draw:Left:40:40:Vertical:0:0:Left", new[] { _toolsOverlay, _drawPanel.Overlay }, column: 3);
            _drawPanel.Overlay.Shown = false;   // opens with the drawing button
            _overlays.Arrange();
            Canvas.KeepoutDrawn += OnKeepoutDrawn;   // [keepout]
            ToolFlyout.Attach(ToolRoute, new[]
            {
                new ToolChoice(nameof(PcbTool.Route), "Yol", "I.Wire", "X"),
                new ToolChoice(nameof(PcbTool.DiffPair), "Fark çifti yolu", "I.DiffPair", "6"),
                new ToolChoice(nameof(PcbTool.Tune), "Uzunluk ayarı (menderes)", "I.Meander")
            });
            ToolFlyout.Attach(ToolZone, new[]
            {
                new ToolChoice(nameof(PcbTool.Zone), "Bakır dolgu alanı", "I.Zone", "Z"),
                new ToolChoice(nameof(PcbTool.Keepout), "Yasak bölge", "I.Keepout")
            });
            ToolFlyout.Attach(ToolHole, new[]
            {
                new ToolChoice(nameof(PcbTool.Hole), "Montaj deliği", "I.Hole"),
                new ToolChoice(nameof(PcbTool.Via), "Via", "I.Via")
            });
        }

        /// <summary>[overlays] The drawing button opens or closes the drawing tools' own strip beside the tool strip.</summary>
        private void OnDrawTool(object sender, RoutedEventArgs e)
        {
            if (_drawPanel == null) return;
            bool open = !_drawPanel.IsOpen;
            _drawPanel.Toggle(open);
            if (!open && DrawingTools.Contains(Canvas.Tool)) Canvas.SetTool(PcbTool.Select);
            SyncTools();
            Canvas.Focus();
        }

        /// <summary>[tuning] The length tuner's options: target (the pair's or group's, or a length), amplitude, spacing, corners.</summary>
        private void TuneOptions()
        {
            var t = Canvas.Tuning;
            var auto = new CheckBox { Content = "Hedef: çift / grup", IsChecked = Canvas.TuneTarget == null, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Hedef uzunluk fark çiftinin öteki yarısından ya da uzunluk grubunun en uzun hattından gelir" };
            var target = Number(Canvas.TuneTarget ?? 50, v => Canvas.TuneTarget = v, 0.1, 10000);
            target.Width = 60;
            target.IsEnabled = Canvas.TuneTarget != null;
            target.ToolTip = "Elle hedef uzunluk (mm)";
            auto.Click += (_, _) =>
            {
                Canvas.TuneTarget = auto.IsChecked == true ? null : 50;
                UpdateToolOptions();
            };
            var amp = Number(t.Amplitude, v => t.Amplitude = v, 0.1, 20);
            amp.Width = 50;
            amp.ToolTip = "Kıvrımların yoldan uzaklığı (mm); parça kısaysa en çoka kadar büyür";
            var max = Number(t.MaxAmplitude, v => t.MaxAmplitude = v, 0.1, 50);
            max.Width = 50;
            max.ToolTip = "En çok genlik (mm)";
            var gap = Number(t.Spacing, v => t.Spacing = v, 0, 20, true);
            gap.Width = 50;
            gap.ToolTip = "Kıvrım bacakları arası (mm); 0: hat sınıfının aralığından";
            var corner = Combo(new[] { MeanderCorner.Chamfered, MeanderCorner.Square }, t.Corner, c => t.Corner = c, c => Shell.Lang.T(c == MeanderCorner.Chamfered ? "45° köşe" : "Dik köşe"));
            corner.Width = 100;
            ToolOptionsRow.Children.Add(auto);
            ToolOptionsRow.Children.Add(Label("Elle"));
            ToolOptionsRow.Children.Add(target);
            ToolOptionsRow.Children.Add(Label("Genlik"));
            ToolOptionsRow.Children.Add(amp);
            ToolOptionsRow.Children.Add(Label("En çok", 6));
            ToolOptionsRow.Children.Add(max);
            ToolOptionsRow.Children.Add(Label("Aralık"));
            ToolOptionsRow.Children.Add(gap);
            ToolOptionsRow.Children.Add(Label("Köşe"));
            ToolOptionsRow.Children.Add(corner);
            ToolOptions.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// [flyout] The routing options (KiCad's auxiliary toolbar): the track width (the net class's or one of the steps) and
        /// the via (the class's or a common size), the active layer beside them.
        /// </summary>
        private void RouteOptions()
        {
            var board = Board;
            var widths = new List<double?> { null };
            widths.AddRange(Canvas.TrackWidthSteps().Select(w => (double?)w));
            var width = Combo(widths, Canvas.RouteWidth, w => Canvas.RouteWidth = w, w => w is { } v ? Fmt(v) + " mm" : Shell.Lang.T("Hat sınıfından"));
            width.Width = 130;
            width.ToolTip = "Yeni yolların genişliği; W / Shift+W çizerken değiştirir";
            var vias = new List<(double D, double Drill)?> { null };
            foreach (var c in board.NetClasses) vias.Add((c.ViaDiameter, c.ViaDrill));
            foreach (var v in new[] { (0.6, 0.3), (0.8, 0.4), (1.0, 0.5), (1.2, 0.6) }) vias.Add(v);
            var distinct = vias.Distinct().ToList();
            var via = Combo(distinct, Canvas.RouteVia, v => Canvas.RouteVia = v, v => v is { } x ? $"Ø{Fmt(x.Item1)} / {Fmt(x.Item2)} mm" : Shell.Lang.T("Hat sınıfından"));
            via.Width = 150;
            via.ToolTip = "Yeni viaların çapı ve deliği; kart kurallarının altındakileri DRC bildirir";
            var layer = Combo(board.CopperLayers(), Canvas.ActiveLayer, l => Canvas.ActiveLayer = l, l => Shell.Lang.T(PcbLayers.DisplayName(l)));
            layer.Width = 130;
            layer.ToolTip = "Etkin katman (PgUp / PgDn)";
            bool laying = Canvas.Tool is PcbTool.Route or PcbTool.DiffPair;
            if (laying)
            {
                ToolOptionsRow.Children.Add(Label("Genişlik", 0));
                ToolOptionsRow.Children.Add(width);
            }

            if (Canvas.Tool != PcbTool.DiffPair)
            {
                ToolOptionsRow.Children.Add(Label("Via", laying ? 12 : 0));
                ToolOptionsRow.Children.Add(via);
            }

            if (laying)
            {
                ToolOptionsRow.Children.Add(Label("Katman"));
                ToolOptionsRow.Children.Add(layer);
            }

            if (Canvas.Tool == PcbTool.DiffPair)
            {
                // [diffroute] each pair keeps its own gap and skew limit (board setup); name-found pairs take 0.2 mm
                var pairs = board.DiffPairs.Count > 0 ? board.DiffPairs : NetLengths.Detect(board);
                string gaps = pairs.Count == 0 ? Shell.Lang.T("kartta çift yok") : string.Join(" · ", pairs.Take(3).Select(p => $"{p}: {Fmt(p.Gap)} mm"));
                var info = Label(Shell.Lang.T("Aralık") + ": " + gaps);
                info.ToolTip = Shell.Lang.T("Çiftler ve aralıkları kart kurulumunda; adlarından bulunan çiftler (D+/D−, _P/_N, CAN H/L) 0,2 mm alır");
                ToolOptionsRow.Children.Add(info);
            }

            ToolOptions.Visibility = Visibility.Visible;
        }

        /// <summary>[keepout] What a new rule area forbids and on which layers (the active one or all).</summary>
        private readonly RuleArea _keepoutTemplate = new RuleArea();
        private bool _keepoutAllLayers;

        private void KeepoutOptions()
        {
            var t = _keepoutTemplate;
            CheckBox Box(string label, bool on, Action<bool> set)
            {
                var cb = new CheckBox { Content = label, IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                cb.Click += (_, _) => set(cb.IsChecked == true);
                return cb;
            }

            ToolOptionsRow.Children.Add(Label("Yasak", 0));
            ToolOptionsRow.Children.Add(Box("Yol", t.NoTracks, v => t.NoTracks = v));
            ToolOptionsRow.Children.Add(Box("Via", t.NoVias, v => t.NoVias = v));
            ToolOptionsRow.Children.Add(Box("Dolgu", t.NoPour, v => t.NoPour = v));
            ToolOptionsRow.Children.Add(Box("Parça", t.NoParts, v => t.NoParts = v));
            ToolOptionsRow.Children.Add(Label("Katman", 6));
            var layers = Combo(new[] { false, true }, _keepoutAllLayers, v => _keepoutAllLayers = v, v => Shell.Lang.T(v ? "Bütün katmanlar" : "Etkin katman"));
            layers.Width = 140;
            ToolOptionsRow.Children.Add(layers);
            ToolOptions.Visibility = Visibility.Visible;
        }

        private void OnKeepoutDrawn(object? sender, List<PcbPoint> pts)
        {
            var t = _keepoutTemplate;
            var area = new RuleArea { Outline = pts, NoTracks = t.NoTracks, NoVias = t.NoVias, NoPour = t.NoPour, NoParts = t.NoParts };
            if (!_keepoutAllLayers) area.Layers.Add(Canvas.ActiveLayer);
            var board = Board;
            Edit($"Yasak bölge eklendi ({area.Describe()} · {(_keepoutAllLayers ? Shell.Lang.T("bütün katmanlar") : PcbLayers.DisplayName(Canvas.ActiveLayer))})", () =>
            {
                board.RuleAreas.Add(area);
                if (t.NoPour && board.Zones.Count > 0) ZoneFiller.FillAll(board);
            });
        }

        private static TextBlock Label(string text, double left = 12) =>
            new TextBlock { Text = text, Style = S("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 6, 0) };

        /// <summary>The options of the tool in use, over the board's top left corner; hidden for tools without any.</summary>
        /// <summary>[dims] A dimension kind's name.</summary>
        internal static string DimensionKindName(DimensionKind k) => k switch
        {
            DimensionKind.Orthogonal => "Dik (yatay / dikey)",
            DimensionKind.Radial => "Yarıçap",
            DimensionKind.Leader => "Kılavuz",
            DimensionKind.Center => "Merkez işareti",
            _ => "Hizalı"
        };

        private void UpdateToolOptions()
        {
            ToolOptionsRow.Children.Clear();
            if (Canvas.Tool == PcbTool.Tune)
            {
                TuneOptions();
                return;
            }

            if (Canvas.Tool == PcbTool.Keepout)
            {
                KeepoutOptions();
                return;
            }

            if (Canvas.Tool is PcbTool.Route or PcbTool.Via or PcbTool.DiffPair)
            {
                RouteOptions();
                return;
            }

            if (Canvas.Tool == PcbTool.Dimension)   // [dims] KiCad's dimension kinds
            {
                ToolOptions.Visibility = Visibility.Visible;
                var kinds = new[] { DimensionKind.Aligned, DimensionKind.Orthogonal, DimensionKind.Radial, DimensionKind.Leader, DimensionKind.Center };
                var kind = Combo(kinds, Canvas.DimKind, k =>
                {
                    Canvas.DimKind = k;
                    Canvas.InvalidateVisual();
                    W.Status = Shell.Lang.T(Canvas.ToolHint());
                }, k => Shell.Lang.T(DimensionKindName(k)));
                kind.Width = 150;
                kind.ToolTip = Shell.Lang.T("Hizalı: iki nokta arası · Dik: yalnız yatay ya da dikey · Yarıçap: dairenin · Kılavuz: oklu not · Merkez: artı işareti");
                ToolOptionsRow.Children.Add(Label("Tür"));
                ToolOptionsRow.Children.Add(kind);
                return;
            }

            if (!BoardCanvas.IsGraphicTool(Canvas.Tool))
            {
                ToolOptions.Visibility = Visibility.Collapsed;
                return;
            }

            var layers = new[] { GraphicLayer.SilkTop, GraphicLayer.SilkBottom, GraphicLayer.FabTop, GraphicLayer.FabBottom, GraphicLayer.Drawings };
            var layer = Combo(layers, Canvas.GraphicDrawLayer, l => Canvas.GraphicDrawLayer = l, l => Shell.Lang.T(BoardCanvas.LayerName(l)));
            layer.Width = 150;
            layer.ToolTip = "Yeni şekillerin çizildiği katman";
            var width = Number(Canvas.DrawWidth, v => Canvas.DrawWidth = v, 0.01, 10);
            width.Width = 60;
            width.ToolTip = "Yeni şekillerin çizgi kalınlığı (mm)";
            ToolOptionsRow.Children.Add(Label("Katman"));
            ToolOptionsRow.Children.Add(layer);
            ToolOptionsRow.Children.Add(Label("Kalınlık"));
            ToolOptionsRow.Children.Add(width);
            ToolOptionsRow.Children.Add(Label("mm", 4));
            if (Canvas.Tool is PcbTool.Rectangle or PcbTool.Circle or PcbTool.Polygon)
            {
                var filled = new CheckBox { Content = "Dolgulu", IsChecked = Canvas.DrawFilled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Dikdörtgen, daire ve çokgen içi dolu çizilir" };
                filled.Click += (_, _) => Canvas.DrawFilled = filled.IsChecked == true;
                ToolOptionsRow.Children.Add(filled);
            }

            ToolOptions.Visibility = Visibility.Visible;
        }
    }
}
