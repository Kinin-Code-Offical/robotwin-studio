using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Libraries;
using RobotTwin.CoreSim.Pcb;
using static RobotWin.Studio.Libraries.LibUi;

namespace RobotWin.Studio.Libraries
{
    // [libs] Preview + mapping dialog shared by every importer: a checklist of what the files
    // contain on the left, details/mapping of the selected item on the right, a report and
    // "İçe aktar" at the bottom. Batch mode commits the defaults without the dialog.

    internal sealed class ImportRow
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public Func<bool> IsSelected { get; set; } = () => true;
        public Action<bool> Select { get; set; } = _ => { };
        public Func<(string Text, string Brush)> Status { get; set; } = () => ("hazır", "B.Success");
        public object? Tag { get; set; }
    }

    internal abstract class ImportAdapter
    {
        public abstract string Title { get; }
        public abstract string Subtitle { get; }
        public abstract List<ImportRow> Rows { get; }
        public abstract ImportLog SessionLog { get; }

        /// <summary>Library to replace (update) instead of installing a new one.</summary>
        public string? ReplaceId { get; set; }

        public abstract UIElement Detail(ImportRow row, Action changed);

        /// <summary>Installs the selected items; returns what happened.</summary>
        public abstract ImportLog Commit(PartLibrary library);

        public virtual string CommitText => "İçe aktar";
    }

    internal sealed class ImportWindow
    {
        private readonly ImportAdapter _a;
        private readonly Window _w;
        private readonly ListBox _list = new ListBox();
        private readonly ContentControl _detail = new ContentControl();
        private readonly Button _commit;
        private readonly TextBlock _summary = new TextBlock();
        private readonly TextBox _search = new TextBox { Width = 260, Tag = "Ara…" };
        private ImportLog? _result;

        public ImportWindow(Window? owner, ImportAdapter adapter)
        {
            _a = adapter;
            _w = LibUi.Window(owner, adapter.Title, 1240, 780);
            _commit = Primary(adapter.CommitText, Commit);
            var root = new DockPanel();

            var head = new StackPanel();
            head.Children.Add(Title(adapter.Title));
            head.Children.Add(new TextBlock { Text = adapter.Subtitle, Style = S("Text.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
            var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var sel = Row(Btn("Tümünü seç", () => SetAll(true)), Btn("Hiçbirini", () => SetAll(false)), Btn("Uyarısızları seç", SelectClean, "Hata içermeyenleri seç"));
            sel.Margin = new Thickness(0);
            DockPanel.SetDock(sel, Dock.Right);
            tools.Children.Add(sel);
            _search.TextChanged += (_, _) => Fill();
            tools.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _search } });
            head.Children.Add(tools);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var foot = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var buttons = Row(Btn("Rapor…", ShowReport, "Bütün bulguları satır satır göster"), Btn("Vazgeç", () => _w.Close(), ghost: true));
            buttons.Children.Add(new Border { Width = 8 });
            buttons.Children.Add(_commit);
            DockPanel.SetDock(buttons, Dock.Right);
            foot.Children.Add(buttons);
            _summary.Style = S("Text.Caption");
            _summary.VerticalAlignment = VerticalAlignment.Center;
            _summary.TextWrapping = TextWrapping.Wrap;
            foot.Children.Add(_summary);
            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _list.SelectionChanged += (_, _) => ShowDetail();
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            body.Children.Add(_list);
            var detailScroll = new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(detailScroll, 2);
            body.Children.Add(detailScroll);
            root.Children.Add(body);
            _w.Content = new Border { Background = B("B.Base"), Padding = new Thickness(20, 16, 20, 16), Child = root };
            Fill();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            else _detail.Content = Issues(_a.SessionLog.Issues, 600);
        }

        public Window Window => _w;

        public ImportLog? Result => _result;

        /// <summary>Selects the row whose name is <paramref name="name"/> (snapshots).</summary>
        internal void Select(string name)
        {
            foreach (ListBoxItem item in _list.Items)
            {
                if (item.Tag is ImportRow r && r.Name == name) _list.SelectedItem = item;
            }
        }

        public ImportLog? ShowDialog()
        {
            _w.ShowDialog();
            return _result;
        }

        /// <summary>Commits the defaults without showing the dialog (batch mode).</summary>
        public static ImportLog Batch(ImportAdapter adapter, PartLibrary library)
        {
            var log = new ImportLog();
            log.AddRange(adapter.SessionLog.Issues);
            log.AddRange(adapter.Commit(library).Issues);
            return log;
        }

        private void SetAll(bool on)
        {
            foreach (var r in _a.Rows) r.Select(on);
            Fill();
        }

        private void SelectClean()
        {
            foreach (var r in _a.Rows) r.Select(r.Status().Brush != "B.Danger");
            Fill();
        }

        private void Fill()
        {
            var keep = (_list.SelectedItem as ListBoxItem)?.Tag as ImportRow;
            _list.Items.Clear();
            string q = _search.Text.Trim().ToLowerInvariant();
            foreach (var row in _a.Rows.Where(r => q.Length == 0 || (r.Name + " " + r.Kind).ToLowerInvariant().Contains(q)))
            {
                var check = new CheckBox { IsChecked = row.IsSelected(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                var r = row;
                check.Click += (_, _) =>
                {
                    r.Select(check.IsChecked == true);
                    UpdateSummary();
                };
                var (text, brush) = row.Status();
                var line = new DockPanel();
                line.Children.Add(check);
                var chip = Chip(text, brush);
                DockPanel.SetDock(chip, Dock.Right);
                line.Children.Add(chip);
                var names = new StackPanel();
                names.Children.Add(new TextBlock { Text = row.Name, Foreground = B("B.Text"), TextTrimming = TextTrimming.CharacterEllipsis });
                names.Children.Add(new TextBlock { Text = row.Kind, Foreground = B("B.TextMuted"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                line.Children.Add(names);
                var item = new ListBoxItem { Content = line, Tag = row, Padding = new Thickness(6, 4, 6, 4) };
                _list.Items.Add(item);
                if (row == keep) _list.SelectedItem = item;
            }

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int n = _a.Rows.Count(r => r.IsSelected());
            _commit.Content = $"{_a.CommitText} ({n})";
            _commit.IsEnabled = n > 0 || _a.Rows.Count == 0;
            _summary.Text = $"{_a.Rows.Count} öğe, {n} seçili · dosya bulguları: {_a.SessionLog.Summary()}";
        }

        private void ShowDetail()
        {
            if (!((_list.SelectedItem as ListBoxItem)?.Tag is ImportRow row))
            {
                _detail.Content = null;
                return;
            }

            try
            {
                _detail.Content = _a.Detail(row, () =>
                {
                    Fill();
                    ShowDetail();
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is FormatException || ex is IOException)
            {
                _detail.Content = Caption("Önizleme oluşturulamadı: " + ex.Message);
            }
        }

        private void ShowReport()
        {
            var sb = new StringBuilder();
            sb.Append(_a.Title).Append('\n').Append(new string('=', _a.Title.Length)).Append("\n\n");
            sb.Append("Dosya bulguları (").Append(_a.SessionLog.Summary()).Append(")\n");
            foreach (var i in _a.SessionLog.Issues) sb.Append("  ").Append(i).Append('\n');
            if (_result != null)
            {
                sb.Append("\nİçe aktarma sonucu (").Append(_result.Summary()).Append(")\n");
                foreach (var i in _result.Issues) sb.Append("  ").Append(i).Append('\n');
            }

            Report(_w, "İçe aktarma raporu", sb.ToString());
        }

        private void Commit()
        {
            if (_result != null)
            {
                _w.Close();
                return;
            }

            ImportLog log;
            try
            {
                _w.Cursor = System.Windows.Input.Cursors.Wait;
                log = _a.Commit(Shell.Workspace.Current.Library);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is InvalidDataException || ex is ArgumentException)
            {
                log = new ImportLog();
                log.Error("içe aktarma yarıda kaldı: " + ex.Message);
            }
            finally
            {
                _w.Cursor = null;
            }

            _result = log;
            var panel = new StackPanel();
            panel.Children.Add(Overline("Sonuç", 0));
            panel.Children.Add(Body(log.HasErrors ? "İçe aktarma tamamlandı, bazı öğeler alınamadı:" : "İçe aktarma tamamlandı."));
            panel.Children.Add(new Border { Height = 8 });
            panel.Children.Add(Issues(log.Issues, 520));
            _detail.Content = panel;
            _commit.Content = "Kapat";
            _list.IsEnabled = false;
            _summary.Text = "Sonuç: " + log.Summary();
        }
    }

    /// <summary>Fitted footprint drawing with pad numbers (for footprints not registered yet).</summary>
    internal sealed class FootprintView : FrameworkElement
    {
        private Footprint? _fp;
        private HashSet<string> _bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Show(Footprint? fp, IEnumerable<string>? highlightMissing = null)
        {
            _fp = fp;
            _bad = new HashSet<string>(highlightMissing ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x15, 0x16, 0x18)), null, new Rect(0, 0, ActualWidth, ActualHeight));
            var fp = _fp;
            if (fp == null || ActualWidth < 4)
            {
                if (fp == null)
                {
                    var t = new FormattedText("kılıf yok", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Gray, 1.0);
                    dc.DrawText(t, new Point(ActualWidth / 2 - t.Width / 2, ActualHeight / 2 - t.Height / 2));
                }

                return;
            }

            double w = fp.CourtyardMaxX - fp.CourtyardMinX + 2, h = fp.CourtyardMaxY - fp.CourtyardMinY + 2;
            if (w <= 0 || h <= 0) return;
            double s = Math.Min(ActualWidth / w, ActualHeight / h);
            double ox = ActualWidth / 2 - (fp.CourtyardMinX + fp.CourtyardMaxX) / 2 * s;
            double oy = ActualHeight / 2 - (fp.CourtyardMinY + fp.CourtyardMaxY) / 2 * s;
            Point P(double x, double y) => new Point(ox + x * s, oy + y * s);
            var silk = new Pen(new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5)), Math.Max(1, 0.15 * s));
            foreach (var seg in fp.Silkscreen) dc.DrawLine(silk, P(seg.X1, seg.Y1), P(seg.X2, seg.Y2));
            foreach (var (x, y, r) in fp.SilkCircles) dc.DrawEllipse(null, silk, P(x, y), r * s, r * s);
            var gold = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xBE));
            var bad = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
            var hole = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10));
            var label = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
            foreach (var pad in fp.Pads)
            {
                var c = P(pad.X, pad.Y);
                double pw = pad.Width * s, ph = pad.Height * s;
                var fill = _bad.Contains(pad.Name) ? bad : gold;
                if (pad.Shape == PadShape.Circle) dc.DrawEllipse(fill, null, c, pw / 2, ph / 2);
                else if (pad.Shape == PadShape.Rect) dc.DrawRectangle(fill, null, new Rect(c.X - pw / 2, c.Y - ph / 2, pw, ph));
                else dc.DrawRoundedRectangle(fill, null, new Rect(c.X - pw / 2, c.Y - ph / 2, pw, ph), Math.Min(pw, ph) / 2, Math.Min(pw, ph) / 2);
                if (pad.IsThroughHole) dc.DrawEllipse(hole, null, c, pad.Drill / 2 * s, pad.Drill / 2 * s);
                double size = Math.Min(Math.Max(7, Math.Min(pw, ph) * 0.5), 12);
                if (Math.Min(pw, ph) > 9)
                {
                    // Through-hole pads have the hole in the middle: put the number beside the pad.
                    var t = new FormattedText(pad.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, pad.IsThroughHole ? label : hole, 1.0);
                    var at = pad.IsThroughHole ? new Point(c.X + pw / 2 + 2, c.Y - ph / 2 - t.Height / 2) : new Point(c.X - t.Width / 2, c.Y - t.Height / 2);
                    dc.DrawText(t, at);
                }
            }
        }
    }

    // ====================================================================== KiCad

    internal sealed class KiCadAdapter : ImportAdapter
    {
        private readonly KiCadImportSession _s;
        private readonly List<ImportRow> _rows = new List<ImportRow>();
        private readonly Dictionary<KiCadImportItem, ImportLog> _checks = new Dictionary<KiCadImportItem, ImportLog>();

        public KiCadAdapter(KiCadImportSession session)
        {
            _s = session;
            foreach (var item in session.Items)
            {
                var it = item;
                _rows.Add(new ImportRow
                {
                    Name = it.Symbol.Name,
                    Kind = $"{it.Symbol.Library} · {KiCadImportSession.PartPins(it.Symbol).Count} bacak · {(it.Footprint?.Id ?? "kılıf yok")}",
                    IsSelected = () => it.Selected,
                    Select = v => it.Selected = v,
                    Status = () =>
                    {
                        var c = Check(it);
                        if (c.HasErrors) return ("hata", "B.Danger");
                        if (it.Mode == KiCadPartMode.OwnPins) return (it.AutoStatus == KiCadImportStatus.Mapped ? "KiCad bacakları + model" : "KiCad bacakları", c.WarningCount > 0 ? "B.Warning" : "B.Accent");
                        return (it.StatusText, c.WarningCount > 0 ? "B.Warning" : "B.Success");
                    },
                    Tag = it
                });
            }
        }

        public override string Title => "KiCad kütüphanesi içe aktar";

        public override string Subtitle =>
            $"{_s.Items.Count} sembol, {_s.Footprints.Count} footprint · {string.Join(", ", _s.Paths.Select(Path.GetFileName))}. Her sembol için yerleşik simülasyon modeline otomatik eşleme ya da KiCad'deki bacaklarıyla kendi parçası seçilebilir; bacak→pad eşlemesi düzenlenebilir.";

        public override List<ImportRow> Rows => _rows;
        public override ImportLog SessionLog => _s.Log;

        private ImportLog Check(KiCadImportItem item)
        {
            if (_checks.TryGetValue(item, out var log)) return log;
            log = new ImportLog();
            bool models = _s.Load3DModels;
            _s.Load3DModels = false;
            try
            {
                _s.BuildPart(item, Shell.Workspace.Current.Library, log);
            }
            finally
            {
                _s.Load3DModels = models;
            }

            _checks[item] = log;
            return log;
        }

        public override UIElement Detail(ImportRow row, Action changed)
        {
            var item = (KiCadImportItem)row.Tag!;
            var sym = item.Symbol;
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = sym.Library.Length > 0 ? "KiCad · " + sym.Library : "KiCad", Style = S("Text.Overline") });
            p.Children.Add(Title(sym.Name + (sym.Value.Length > 0 && sym.Value != sym.Name && sym.Value != "~" ? "  (" + sym.Value + ")" : string.Empty)));
            if (sym.Description.Length > 0) p.Children.Add(Body(sym.Description));
            if (sym.Datasheet.Length > 0 && sym.Datasheet != "~") p.Children.Add(Caption("Veri sayfası: " + sym.Datasheet));

            p.Children.Add(Overline("Parça biçimi"));
            var auto = new RadioButton { Content = item.AutoPart != null ? $"Otomatik: yerleşik '{Shell.Workspace.Current.Library.Find(item.AutoPart.BasePartId)?.Name ?? item.AutoPart.BasePartId}' modeli ve sembolü" : "Otomatik eşleme yok", IsEnabled = item.AutoPart != null, IsChecked = item.Mode == KiCadPartMode.Automatic, Foreground = B("B.Text"), Margin = new Thickness(0, 2, 0, 2) };
            var own = new RadioButton { Content = "KiCad bacaklarıyla kendi parça (dikdörtgen sembol, bütün bacaklar" + (item.AutoStatus == KiCadImportStatus.Mapped ? ", aynı model" : ", modelsiz") + ")", IsChecked = item.Mode == KiCadPartMode.OwnPins, Foreground = B("B.Text"), Margin = new Thickness(0, 2, 0, 2) };
            auto.Checked += (_, _) =>
            {
                item.Mode = KiCadPartMode.Automatic;
                _checks.Remove(item);
                changed();
            };
            own.Checked += (_, _) =>
            {
                item.Mode = KiCadPartMode.OwnPins;
                _checks.Remove(item);
                changed();
            };
            p.Children.Add(auto);
            p.Children.Add(own);

            // Symbol preview of the part this item would become.
            var log = new ImportLog();
            CustomPart? part;
            bool models = _s.Load3DModels;
            _s.Load3DModels = false;
            try
            {
                part = _s.BuildPart(item, Shell.Workspace.Current.Library, log);
            }
            finally
            {
                _s.Load3DModels = models;
            }

            var grid = new Grid { Height = 220, Margin = new Thickness(0, 10, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var symbol = new DefinitionPreview();
            if (part != null)
            {
                var lib = Shell.Workspace.Current.Library;
                PartDefinition? def = part.Definition != null
                    ? part.Definition.ToDefinition(part.BasePartId, part.Name, part.Category, part.Description, item.Footprint?.Id ?? string.Empty, lib)
                    : lib.Find(part.BasePartId);
                if (def != null && part.Definition == null)
                {
                    // Show the model's pins with the KiCad pad numbers the pinmap gives them.
                    var shown = new PartDefinition { Id = def.Id, Name = def.Name, Symbol = def.Symbol, SymbolVariant = def.SymbolVariant, HalfWidth = def.HalfWidth, HalfHeight = def.HalfHeight, ValueKey = def.ValueKey };
                    foreach (var pin in def.Pins) shown.Pins.Add(new PinDefinition(pin.Name, def.ResolvePad(pin, part.Parameters), pin.X, pin.Y));
                    def = shown;
                }

                symbol.Show(def, part.Parameters);
            }

            grid.Children.Add(Card(symbol, 6));
            var fpView = new FootprintView();
            var pads = new HashSet<string>(item.Footprint?.Pads.Select(x => x.Name) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            fpView.Show(item.Footprint);
            var fpCard = Card(fpView, 0);
            Grid.SetColumn(fpCard, 2);
            grid.Children.Add(fpCard);
            p.Children.Add(grid);
            p.Children.Add(Caption("Sol: şematik sembol (bacak uçlarında pad numaraları) · Sağ: " + (item.Footprint != null ? $"kılıf '{item.Footprint.Id}' ({item.Footprint.Pads.Count} pad)" : "kılıf bulunamadı")));

            // Pin → pad table.
            p.Children.Add(Overline("Bacak → pad eşlemesi"));
            var table = new Grid();
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Cell(UIElement e, int r, int c)
            {
                if (e is TextBlock tb) tb.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(e, r);
                Grid.SetColumn(e, c);
                table.Children.Add(e);
            }

            table.RowDefinitions.Add(new RowDefinition());
            Cell(Caption("No"), 0, 0);
            Cell(Caption("Ad"), 0, 1);
            Cell(Caption("Tür"), 0, 2);
            Cell(Caption("Pad"), 0, 3);
            int ri = 1;
            foreach (var pin in KiCadImportSession.PartPins(sym))
            {
                table.RowDefinitions.Add(new RowDefinition());
                Cell(Mono(pin.Number), ri, 0);
                Cell(Body(pin.CleanName.Length > 0 ? pin.CleanName : "—"), ri, 1);
                Cell(Caption(UserPartSpec.TypeName(UserPartSpec.ParseKiCadType(pin.ElectricalType))), ri, 2);
                string pad = KiCadImportSession.PadOf(item, pin);
                var box = new TextBox { Text = pad, Margin = new Thickness(0, 1, 6, 1), MinWidth = 60 };
                var pinRef = pin;
                var ok = new TextBlock { Text = item.Footprint == null || pads.Contains(pad) ? "✓" : "✗", Foreground = B(item.Footprint == null || pads.Contains(pad) ? "B.Success" : "B.Danger"), VerticalAlignment = VerticalAlignment.Center };
                box.LostFocus += (_, _) =>
                {
                    string v = box.Text.Trim();
                    if (v == pinRef.Number) item.PadOverrides.Remove(pinRef.Number);
                    else item.PadOverrides[pinRef.Number] = v;
                    _checks.Remove(item);
                    bool good = item.Footprint == null || pads.Contains(v);
                    ok.Text = good ? "✓" : "✗";
                    ok.Foreground = B(good ? "B.Success" : "B.Danger");
                };
                Cell(box, ri, 3);
                Cell(ok, ri, 4);
                ri++;
            }

            p.Children.Add(table);
            p.Children.Add(Overline("Bulgular"));
            p.Children.Add(Issues(log.Issues, 260));
            if (item.AutoDetails.Length > 0)
            {
                p.Children.Add(Overline("Otomatik eşleme ayrıntısı"));
                p.Children.Add(new TextBlock { Text = item.AutoDetails, Style = S("Text.Mono"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = B("B.TextMuted") });
            }

            return p;
        }

        public override ImportLog Commit(PartLibrary library)
        {
            var log = new ImportLog();
            var file = _s.Commit(library, log);
            LibraryManager.Install(library, file, LibrarySourceKind.KiCad, _s.Paths, _s.Footprints, log, ReplaceId);
            return log;
        }
    }

    // ====================================================================== SPICE

    internal sealed class SpiceAdapter : ImportAdapter
    {
        private readonly SpiceImportSession _s;
        private readonly List<ImportRow> _rows = new List<ImportRow>();

        public SpiceAdapter(SpiceImportSession session)
        {
            _s = session;
            foreach (var item in session.Items)
            {
                var it = item;
                _rows.Add(new ImportRow
                {
                    Name = it.Name,
                    Kind = it.KindText + " · " + Path.GetFileName(it.File),
                    IsSelected = () => it.Selected,
                    Select = v => it.Selected = v,
                    Status = () => (it.StatusText, it.StatusText == "kullanılamaz" || it.StatusText == "desteklenmiyor" ? "B.Danger" : it.Log.HasErrors || it.Log.WarningCount > 0 ? "B.Warning" : "B.Success"),
                    Tag = it
                });
            }
        }

        public override string Title => "SPICE modelleri içe aktar";

        public override string Subtitle =>
            $"{_s.Items.Count(i => i.Kind == SpiceItemKind.Model)} .model, {_s.Items.Count(i => i.Kind == SpiceItemKind.Subcircuit)} .subckt · {string.Join(", ", _s.Paths.Select(Path.GetFileName))}. " +
            ".model kartları yerleşik diyot / BJT / MOSFET modellerinin parametrelerine çevrilir; .subckt tanımları desteklenen elemanlardan (R C L V I D Q M E G F H X) oluşan bileşik parçalar olur. Desteklenmeyen her satır raporlanır.";

        public override List<ImportRow> Rows => _rows;
        public override ImportLog SessionLog => _s.Log;

        public override UIElement Detail(ImportRow row, Action changed)
        {
            var item = (SpiceImportItem)row.Tag!;
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = "SPICE · " + Path.GetFileName(item.File), Style = S("Text.Overline") });
            p.Children.Add(Title(item.Name));
            var lib = Shell.Workspace.Current.Library;
            if (item.Kind == SpiceItemKind.Model && item.Card != null)
            {
                var card = item.Card;
                p.Children.Add(Body($"Tür: {card.Type}" + (card.Kind != card.Type ? $" ({card.Kind})" : string.Empty) + (card.Text.TryGetValue("MFG", out var mfg) ? " · Üretici: " + mfg : string.Empty)));
                p.Children.Add(Overline("Model parametreleri"));
                var wrap = new WrapPanel();
                foreach (var kv in card.Params.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    wrap.Children.Add(new Border
                    {
                        BorderBrush = B("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 6, 6),
                        Child = new TextBlock { Text = kv.Key + " = " + kv.Value.ToString("G4", CultureInfo.InvariantCulture), Style = S("Text.Mono"), FontSize = 11.5 }
                    });
                }

                p.Children.Add(wrap);
                var tmp = new ImportLog();
                var parameters = SpiceModelMap.ToPartParameters(card, out string basePart, tmp);
                if (parameters != null && lib.Find(basePart) is { } def)
                {
                    p.Children.Add(Overline("Parça önizlemesi · " + def.Name));
                    var prev = new DefinitionPreview { Height = 140 };
                    prev.Show(def, lib.ResolveParameters(def, null, parameters));
                    p.Children.Add(Card(prev, 6));
                }
            }
            else if (item.Subcircuit != null)
            {
                var sub = item.Subcircuit;
                p.Children.Add(Body($"{sub.Pins.Count} bacak · {sub.Elements.Count} eleman satırı · simüle edilecek: {item.Devices}, atlanacak: {item.Skipped}" + (sub.Parameters.Count > 0 ? " · parametreler: " + string.Join(", ", sub.Parameters.Select(kv => kv.Key + "=" + kv.Value)) : string.Empty)));
                p.Children.Add(Overline("Bacak adları (sembolde görünen)"));
                var pins = new WrapPanel();
                foreach (var port in sub.Pins)
                {
                    var portRef = port;
                    var box = new TextBox { Text = item.PinNames.TryGetValue(port, out var n) ? n : port, Width = 110, Margin = new Thickness(0, 0, 6, 6), ToolTip = "SPICE düğümü: " + port };
                    box.LostFocus += (_, _) => item.PinNames[portRef] = box.Text.Trim();
                    pins.Children.Add(new StackPanel { Children = { Caption((sub.Pins.IndexOf(port) + 1).ToString(CultureInfo.InvariantCulture) + ". " + port), box } });
                }

                p.Children.Add(pins);
                p.Children.Add(Overline("Kılıf"));
                var ids = new List<string> { item.FootprintId };
                ids.AddRange(FootprintLibrary.AllIds.Where(id => id != item.FootprintId));
                var combo = Combo(ids, item.FootprintId, id => id + (id == SpiceImportSession.DefaultFootprint(sub.Pins.Count) ? "  (önerilen)" : string.Empty), id =>
                {
                    item.FootprintId = id;
                    changed();
                });
                combo.Width = 360;
                combo.HorizontalAlignment = HorizontalAlignment.Left;
                p.Children.Add(combo);
                var fp = SpiceImportSession.ResolveFootprint(item.FootprintId);
                var view = new FootprintView { Height = 150, Margin = new Thickness(0, 8, 0, 0) };
                view.Show(fp);
                p.Children.Add(Card(view, 0));
                if (fp != null && fp.Pads.Count != sub.Pins.Count) p.Children.Add(new TextBlock { Text = $"Kılıfta {fp.Pads.Count} pad var, alt devrenin {sub.Pins.Count} bacağı var; bacak n → pad n eşlenir.", Foreground = B("B.Warning"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

                p.Children.Add(Overline("Kaynak"));
                p.Children.Add(new TextBox
                {
                    Text = string.Join("\n", sub.Source), IsReadOnly = true, FontFamily = (FontFamily)Application.Current.FindResource("F.Mono"), FontSize = 11.5,
                    MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap
                });
            }

            p.Children.Add(Overline("Satır satır bulgular"));
            p.Children.Add(Issues(item.Log.Issues, 300));
            return p;
        }

        public override ImportLog Commit(PartLibrary library)
        {
            var log = new ImportLog();
            var file = _s.Commit(library, log);
            var fps = file.Parts.Where(x => x.Footprint != null).Select(x => x.Footprint!.ToFootprint()).ToList();
            LibraryManager.Install(library, file, LibrarySourceKind.Spice, _s.Paths, fps, log, ReplaceId);
            return log;
        }
    }

    // ====================================================================== Fritzing

    internal sealed class FritzingAdapter : ImportAdapter
    {
        private readonly List<(FritzingPart Part, bool[] Selected)> _parts;
        private readonly List<ImportRow> _rows = new List<ImportRow>();
        private readonly ImportLog _log = new ImportLog();
        private readonly List<string> _paths;

        public FritzingAdapter(IEnumerable<string> paths)
        {
            _paths = paths.ToList();
            _parts = new List<(FritzingPart, bool[])>();
            foreach (var f in _paths)
            {
                if (Directory.Exists(f))
                {
                    foreach (var x in Directory.EnumerateFiles(f, "*.fzpz", SearchOption.AllDirectories).Take(1000)) _parts.Add((FritzingImporter.Read(x), new[] { true }));
                }
                else
                {
                    _parts.Add((FritzingImporter.Read(f), new[] { true }));
                }
            }

            foreach (var (part, sel) in _parts)
            {
                var pp = part;
                var ss = sel;
                ss[0] = !pp.Log.HasErrors;
                _rows.Add(new ImportRow
                {
                    Name = pp.Title,
                    Kind = $"{pp.Connectors.Count} bağlantı · {(pp.Family.Length > 0 ? pp.Family : "Fritzing")} · {Path.GetFileName(pp.File)}",
                    IsSelected = () => ss[0],
                    Select = v => ss[0] = v,
                    Status = () => pp.Log.HasErrors ? ("hata", "B.Danger") : pp.Log.WarningCount > 0 ? ("uyarılarla", "B.Warning") : ("hazır", "B.Success"),
                    Tag = pp
                });
            }
        }

        public override string Title => "Fritzing parçası içe aktar";

        public override string Subtitle => $"{_parts.Count} parça. Fritzing parçaları bağlantı noktaları, açıklama ve breadboard resmiyle gelir; elektriksel modelleri yoktur (Parça oluşturucuda eklenebilir).";

        public override List<ImportRow> Rows => _rows;
        public override ImportLog SessionLog => _log;

        public override UIElement Detail(ImportRow row, Action changed)
        {
            var fz = (FritzingPart)row.Tag!;
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = "Fritzing" + (fz.Family.Length > 0 ? " · " + fz.Family : string.Empty), Style = S("Text.Overline") });
            p.Children.Add(Title(fz.Title));
            if (fz.Author.Length > 0) p.Children.Add(Caption("Yazar: " + fz.Author + (fz.Version.Length > 0 ? " · sürüm " + fz.Version : string.Empty)));
            if (fz.Description.Length > 0) p.Children.Add(Body(fz.Description.Length > 500 ? fz.Description.Substring(0, 500) + "…" : fz.Description));
            var grid = new Grid { Height = 220, Margin = new Thickness(0, 12, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var img = SvgFlatImage.Render(fz.BreadboardSvg, out string? err);
            UIElement bb = img != null ? new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(6) } : (UIElement)Caption(err ?? "breadboard resmi yok");
            grid.Children.Add(Card(bb, 4));
            var view = new FootprintView();
            view.Show(fz.Footprint);
            var c2 = Card(view, 0);
            Grid.SetColumn(c2, 2);
            grid.Children.Add(c2);
            p.Children.Add(grid);
            p.Children.Add(Caption("Sol: breadboard görünümü (düz resim) · Sağ: PCB görünümünden çıkarılan kılıf" + (fz.Footprint == null ? " — çıkarılamadı, 1×N header kullanılacak" : string.Empty)));
            p.Children.Add(Overline("Bağlantı noktaları"));
            var wrap = new WrapPanel();
            int i = 1;
            foreach (var c in fz.Connectors)
            {
                wrap.Children.Add(new Border
                {
                    BorderBrush = B("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 6, 6),
                    ToolTip = c.Description.Length > 0 ? c.Description : null,
                    Child = new TextBlock { Text = $"{i++}: {c.Name}", Style = S("Text.Mono"), FontSize = 11.5 }
                });
            }

            p.Children.Add(wrap);
            p.Children.Add(Overline("Bulgular"));
            p.Children.Add(Issues(fz.Log.Issues, 240));
            return p;
        }

        public override ImportLog Commit(PartLibrary library)
        {
            var log = new ImportLog();
            var file = new PartLibraryFile { Name = "Fritzing: " + string.Join(", ", _paths.Select(Path.GetFileNameWithoutExtension).Take(3)), Author = "Fritzing içe aktarımı", Description = $"{_parts.Count} Fritzing parçası" };
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fz, sel) in _parts)
            {
                if (!sel[0]) continue;
                var part = FritzingImporter.ToCustomPart(fz, new ImportLog());
                if (part == null)
                {
                    log.Error("parça oluşturulamadı (bağlantı noktası yok).", fz.File);
                    continue;
                }

                string id = part.Id;
                for (int k = 2; !ids.Add(id); k++) id = part.Id + "-" + k.ToString(CultureInfo.InvariantCulture);
                part.Id = id;
                part.BasePartId = "udef-" + id;
                if (!string.IsNullOrEmpty(fz.Version)) file.Version = fz.Version;
                file.Parts.Add(part);
            }

            var fps = file.Parts.Where(x => x.Footprint != null).Select(x => x.Footprint!.ToFootprint()).ToList();
            LibraryManager.Install(library, file, LibrarySourceKind.Fritzing, _paths, fps, log, ReplaceId);
            return log;
        }
    }

    // ====================================================================== Arduino

    internal sealed class ArduinoAdapter : ImportAdapter
    {
        private readonly List<(string Path, ArduinoLibraryInfo Info, bool[] Selected)> _libs = new List<(string, ArduinoLibraryInfo, bool[])>();
        private readonly List<ImportRow> _rows = new List<ImportRow>();
        private readonly ImportLog _log = new ImportLog();

        public ArduinoAdapter(IEnumerable<string> paths, bool update = false)
        {
            Update = update;
            foreach (var p in paths)
            {
                var info = ArduinoLibraries.Inspect(p);
                var sel = new[] { !info.Log.HasErrors };
                _libs.Add((p, info, sel));
                bool installed = Directory.Exists(Path.Combine(LibraryManager.ArduinoFolder, info.FolderName));
                _rows.Add(new ImportRow
                {
                    Name = info.Name.Length > 0 ? info.Name : Path.GetFileName(p),
                    Kind = $"{(info.Version.Length > 0 ? "sürüm " + info.Version : "sürüm yok")} · {info.Headers.Count} başlık · {info.Examples} örnek" + (installed ? " · kurulu" : string.Empty),
                    IsSelected = () => sel[0],
                    Select = v => sel[0] = v,
                    Status = () => info.Log.HasErrors ? ("hata", "B.Danger") : installed && !Update ? ("zaten kurulu", "B.Warning") : info.Log.WarningCount > 0 ? ("uyarılarla", "B.Warning") : ("hazır", "B.Success"),
                    Tag = info
                });
            }
        }

        public bool Update { get; }

        public override string Title => Update ? "Arduino kütüphanesini güncelle" : "Arduino kütüphanesi kur";

        public override string Subtitle =>
            "Kütüphaneler '" + LibraryManager.ArduinoFolder + "' klasörüne kurulur; Kod sayfasındaki derleyici (arduino-cli) bu klasörü --libraries ile kullanır, #include ile doğrudan çağrılabilir.";

        public override List<ImportRow> Rows => _rows;
        public override ImportLog SessionLog => _log;
        public override string CommitText => Update ? "Güncelle" : "Kur";

        public override UIElement Detail(ImportRow row, Action changed)
        {
            var info = (ArduinoLibraryInfo)row.Tag!;
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = "Arduino kütüphanesi · düzen " + info.Layout, Style = S("Text.Overline") });
            p.Children.Add(Title(info.Name));
            if (info.Sentence.Length > 0) p.Children.Add(Body(info.Sentence));
            if (info.Paragraph.Length > 0) p.Children.Add(Caption(info.Paragraph));
            p.Children.Add(Overline("Bilgiler"));
            foreach (var (k, v) in new[] { ("Sürüm", info.Version), ("Yazar", info.Author), ("Bakımcı", info.Maintainer), ("Kategori", info.Category), ("Mimariler", info.Architectures), ("Bağımlılıklar", info.Depends), ("Adres", info.Url) })
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
                var key = new TextBlock { Text = k, Width = 120, Style = S("Text.Caption") };
                line.Children.Add(key);
                line.Children.Add(Body(v));
                p.Children.Add(line);
            }

            p.Children.Add(Overline("#include edilebilir başlıklar"));
            p.Children.Add(Mono(info.Headers.Count == 0 ? "—" : string.Join("\n", info.Headers.Take(40).Select(h => "#include <" + h + ">"))));
            p.Children.Add(Overline("Bulgular"));
            p.Children.Add(Issues(info.Log.Issues, 240));
            return p;
        }

        public override ImportLog Commit(PartLibrary library)
        {
            var log = new ImportLog();
            foreach (var (path, info, sel) in _libs)
            {
                if (!sel[0]) continue;
                var result = ArduinoLibraries.Install(path, LibraryManager.ArduinoFolder, overwrite: Update);
                foreach (var i in result.Log.Issues) log.Issues.Add(new ImportIssue(i.Severity, i.Message, path, i.Line, result.Name));
                if (!result.Log.HasErrors && result.Folder.Length > 0)
                {
                    try
                    {
                        LibraryManager.Catalog.InstallArduino(result, path);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        log.Warn("kütüphane listesine yazılamadı: " + ex.Message, path);
                    }
                }
            }

            return log;
        }
    }
}
