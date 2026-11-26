using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [bom] The bill of materials made ready to buy: every line with its maker, MPN, supplier, supplier's number, unit price
    /// and minimum order (kept with the schematic, one entry for identical parts); the order and cost for a number of boards;
    /// a full CSV and the order files JLCPCB/LCSC, Digi-Key and Mouser take.
    /// </summary>
    internal sealed class BomWindow : Window
    {
        private readonly Schematic _doc;
        private readonly PartLibrary _library;
        private readonly Action _before, _after;   // an undo point before each edit, the project marked changed after
        private readonly List<BomLine> _lines;
        private readonly Grid _table = new Grid();
        private readonly TextBox _boards = new TextBox { Width = 70 };
        private readonly TextBox _currency = new TextBox { Width = 60 };
        private readonly TextBlock _total = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly List<TextBlock> _costCells = new List<TextBlock>();
        private readonly List<TextBlock> _orderCells = new List<TextBlock>();

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        public BomWindow(Window owner, Schematic doc, PartLibrary library, IReadOnlyList<BomLine> lines, Action before, Action after)
        {
            _doc = doc;
            _library = library;
            _before = before;
            _after = after;
            _lines = lines.ToList();
            Owner = owner;
            Title = "Malzeme listesi";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = 1240;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = R<FontFamily>("F.Ui");
            SetResourceReference(ForegroundProperty, "B.Text");
            _total.Style = R<Style>("Text.Body");

            var root = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new TextBlock { Text = "Malzeme listesi ve tedarik", Style = R<Style>("Text.Title"), FontSize = 18 });
            head.Children.Add(new TextBlock
            {
                Text = "Her satırın üreticisi, üretici parça numarası, tedarikçisi, fiyatı ve en az sipariş adedi projeyle saklanır (aynı parçalar tek satır). Sipariş, istenen kart sayısı ve tedarikçinin en azına göre hesaplanır.",
                Style = R<Style>("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
            });
            head.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var settings = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _boards.Text = Math.Max(1, doc.BomBoards).ToString(CultureInfo.InvariantCulture);
            _currency.Text = string.IsNullOrWhiteSpace(doc.BomCurrency) ? "USD" : doc.BomCurrency;
            settings.Children.Add(Field("Kart sayısı", _boards));
            settings.Children.Add(Field("Para birimi", _currency));
            _boards.LostFocus += (_, _) => Settings();
            _currency.LostFocus += (_, _) => Settings();
            _boards.KeyDown += (_, e) => { if (e.Key == Key.Enter) Settings(); };
            DockPanel.SetDock(settings, Dock.Top);
            root.Children.Add(settings);

            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var close = new Button { Content = "Kapat", Style = R<Style>("Btn.Primary"), MinWidth = 96, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            close.Click += (_, _) => Close();
            foreach (var (label, make, suffix) in new (string, Func<BomCost, string>, string)[]
            {
                ("CSV kaydet…", BomSourcing.ToCsv, "-BOM"), ("JLCPCB / LCSC…", BomSourcing.ToJlcpcb, "-JLCPCB-BOM"),
                ("Digi-Key…", BomSourcing.ToDigiKey, "-DigiKey"), ("Mouser…", BomSourcing.ToMouser, "-Mouser"),
            })
            {
                var b = new Button { Content = label, Style = R<Style>("Btn.Ghost"), Margin = new Thickness(8, 0, 0, 0) };
                b.Click += (_, _) => Export(make, suffix);
                buttons.Children.Add(b);
            }

            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);
            footer.Children.Add(_total);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            BuildTable();
            root.Children.Add(new ScrollViewer { Content = _table, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = new Border
            {
                Margin = new Thickness(20), Background = R<Brush>("B.Overlay"), BorderBrush = R<Brush>("B.BorderStrong"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Child = root
            };
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");
            Refresh();
        }

        private static FrameworkElement Field(string label, TextBox box)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            sp.Children.Add(new TextBlock { Text = label, Style = R<Style>("Text.Caption"), Margin = new Thickness(0, 0, 0, 2) });
            sp.Children.Add(box);
            return sp;
        }

        private int Boards => int.TryParse(_boards.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 1;

        private void Settings()
        {
            string currency = string.IsNullOrWhiteSpace(_currency.Text) ? "USD" : _currency.Text.Trim().ToUpperInvariant();
            if (_doc.BomBoards != Boards || _doc.BomCurrency != currency)
            {
                _before();
                _doc.BomBoards = Boards;
                _doc.BomCurrency = currency;
                _after();
            }

            Refresh();
        }

        private void BuildTable()
        {
            string[] heads = { "Referans", "Adet", "Değer", "Kılıf", "Üretici", "Üretici parça no", "Tedarikçi", "Tedarikçi parça no", "Birim fiyat", "En az", "Sipariş", "Tutar" };
            double[] widths = { 116, 40, 88, 90, 100, 124, 104, 118, 70, 62, 56, 72 };
            foreach (var w in widths) _table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < heads.Length; c++) Put(new TextBlock { Text = heads[c], Style = R<Style>("Text.Caption"), Margin = new Thickness(0, 0, 6, 4) }, 0, c);

            for (int i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                var source = BomSourcing.SourceOf(_doc, _library, line);
                int row = i + 1;
                _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Put(Cell(line.ReferenceText, true), row, 0);
                Put(Cell(line.Quantity.ToString(CultureInfo.InvariantCulture), false), row, 1);
                Put(Cell(line.Value, false), row, 2);
                Put(Cell(line.Footprint, false), row, 3);

                TextBox Edit(string text, int col, Action<BomSource, string> set)
                {
                    var box = new TextBox { Text = text, Margin = new Thickness(0, 1, 6, 1) };
                    box.LostFocus += (_, _) =>
                    {
                        var s = BomSourcing.SourceOf(_doc, _library, line);
                        var copy = new BomSource { Manufacturer = s.Manufacturer, Mpn = s.Mpn, Supplier = s.Supplier, SupplierPn = s.SupplierPn, UnitPrice = s.UnitPrice, MinimumOrder = s.MinimumOrder };
                        set(copy, box.Text.Trim());
                        _before();
                        BomSourcing.Keep(_doc, line, copy);
                        _after();
                        Refresh();
                    };
                    Put(box, row, col);
                    return box;
                }

                Edit(source.Manufacturer, 4, (s, v) => s.Manufacturer = v);
                Edit(source.Mpn, 5, (s, v) => s.Mpn = v);
                // free text (this theme's editable combo boxes show no text); LCSC/JLCPCB, Digi-Key and Mouser get their own order file
                var supplierBox = Edit(source.Supplier, 6, (s, v) => s.Supplier = v);
                supplierBox.ToolTip = Shell.Lang.T("Tedarikçi adı: LCSC (JLCPCB), Digi-Key ve Mouser için sipariş dosyası da çıkar");
                Edit(source.SupplierPn, 7, (s, v) => s.SupplierPn = v);
                Edit(source.UnitPrice > 0 ? source.UnitPrice.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty, 8,
                    (s, v) => s.UnitPrice = double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double p) && p > 0 ? p : 0);
                Edit(source.MinimumOrder > 1 ? source.MinimumOrder.ToString(CultureInfo.InvariantCulture) : string.Empty, 9,
                    (s, v) => s.MinimumOrder = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && m > 1 ? m : 1);
                var order = Cell(string.Empty, false);
                var cost = Cell(string.Empty, false);
                _orderCells.Add(order);
                _costCells.Add(cost);
                Put(order, row, 10);
                Put(cost, row, 11);
            }
        }

        private static TextBlock Cell(string text, bool wrap) => new TextBlock
        {
            Text = text, Style = R<Style>("Text.Body"), Margin = new Thickness(0, 4, 6, 4), VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };

        private void Put(UIElement e, int row, int col)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            _table.Children.Add(e);
        }

        private BomCost Costing() => BomSourcing.Cost(_doc, _library, _lines, Boards);

        private void Refresh()
        {
            var cost = Costing();
            for (int i = 0; i < cost.Lines.Count && i < _costCells.Count; i++)
            {
                var l = cost.Lines[i];
                _orderCells[i].Text = l.Order.ToString(CultureInfo.InvariantCulture);
                _orderCells[i].Foreground = l.Order > l.Needed ? R<Brush>("B.Warning") : R<Brush>("B.Text");
                _orderCells[i].ToolTip = l.Order > l.Needed ? Shell.Lang.T($"Kartlar {l.Needed} adet istiyor; tedarikçinin en azı {l.Order}") : null;
                _costCells[i].Text = l.Priced ? l.Cost.ToString("0.00", CultureInfo.CurrentCulture) : "—";
            }

            string money(double v) => v.ToString("0.00", CultureInfo.CurrentCulture) + " " + cost.Currency;
            _total.Text = Shell.Lang.T($"{cost.Boards} kart: toplam {money(cost.Total)} · kart başına {money(cost.PerBoard)}")
                          + (cost.Unpriced > 0 ? Shell.Lang.T($" · {cost.Unpriced} satırın fiyatı yok") : string.Empty);
        }

        private void Export(Func<BomCost, string> make, string suffix)
        {
            var cost = Costing();
            var dlg = new SaveFileDialog
            {
                Filter = Shell.Lang.Filter("CSV (*.csv)|*.csv"), DefaultExt = ".csv",
                FileName = string.Concat((_doc.Title ?? "devre").Split(Path.GetInvalidFileNameChars())) + suffix + ".csv",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, make(cost), new UTF8Encoding(true));
                _total.Text = Shell.Lang.T("Kaydedildi: " + Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _total.Text = Shell.Lang.T("Kaydedilemedi: " + ex.Message);
            }
        }

        /// <summary>For the snapshot: two lines sourced so the costing has something to show.</summary>
        internal void FillForSnapshot()
        {
            foreach (var line in _lines.Take(2))
                BomSourcing.Keep(_doc, line, new BomSource { Mpn = BomSourcing.SourceOf(_doc, _library, line).Mpn, Supplier = "LCSC", SupplierPn = "C" + (100000 + line.Quantity * 731), UnitPrice = 0.12, MinimumOrder = 50 });
            _table.Children.Clear();
            _table.RowDefinitions.Clear();
            _table.ColumnDefinitions.Clear();
            _costCells.Clear();
            _orderCells.Clear();
            BuildTable();
            Refresh();
        }
    }
}
