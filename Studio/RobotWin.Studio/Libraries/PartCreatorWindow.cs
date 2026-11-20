using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Libraries;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Libraries.LibUi;

namespace RobotWin.Studio.Libraries
{
    // [libs] Parça oluşturucu: a step-by-step editor for user parts — general data, pins,
    // symbol, footprint (library / generated package / KiCad or RobotWin file), simulation
    // model (built-in, SPICE subcircuit or ideal behavioural), parameters with units and
    // tolerances, 3D model, validation, save to the user library (.rwpart).

    internal sealed class PartCreatorWindow
    {
        private enum FootprintMode { Library, Generated, File, None }

        private enum ModelMode { FromFootprint, Box, File, OtherPart }

        private static readonly string[] StepNames =
        {
            "1  Genel bilgiler", "2  Bacaklar", "3  Sembol", "4  Kılıf (footprint)", "5  Simülasyon modeli", "6  Parametreler", "7  3B model", "8  Doğrula ve kaydet"
        };

        private readonly Window _w;
        private readonly PartLibrary _lib;
        private readonly CustomPart _part;
        private readonly UserPartSpec _spec;
        private readonly string? _editingId;
        private readonly ListBox _steps = new ListBox();
        private readonly ContentControl _body = new ContentControl();
        private readonly DefinitionPreview _symbol = new DefinitionPreview();
        private readonly FootprintView _fpView = new FootprintView();
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        private readonly DispatcherTimer _debounce;
        private int _step;

        private FootprintMode _fpMode = FootprintMode.Library;
        private string _fpLibraryId = string.Empty;
        private PackageSpec _pkg = PackageSpec.Default(PackageFamily.Dip);
        private Footprint? _fpFile;

        private ModelMode _modelMode = ModelMode.FromFootprint;
        private double _boxW = 10, _boxD = 10, _boxH = 5;
        private string _boxColor = "#2E3440";
        private PartModel3D? _model3d;
        private string _otherModel = string.Empty;

        /// <summary>Saved part id (null until saved).</summary>
        public string? SavedId { get; private set; }

        public PartCreatorWindow(Window? owner, PartLibrary library, CustomPart? existing = null, PartPreset? basis = null)
        {
            _lib = library;
            _w = LibUi.Window(owner, existing != null ? "Parça düzenle · " + existing.Name : "Parça oluşturucu", 1320, 820);
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                UpdatePreview();
            };

            if (existing != null)
            {
                _editingId = existing.Id;
                _part = existing;
                _spec = existing.Definition?.Clone() ?? FromPreset(existing);
                LoadFootprintState(existing);
                if (existing.Model3D != null)
                {
                    _modelMode = ModelMode.File;
                    _model3d = existing.Model3D;
                }
                else if (existing.Parameters.TryGetValue(PartModels3D.ParameterKey, out var other) && other.Length > 0)
                {
                    _modelMode = ModelMode.OtherPart;
                    _otherModel = other;
                }
            }
            else if (basis != null)
            {
                var def = _lib.Get(basis.PartId);
                var values = _lib.ResolveParameters(def, basis, null);
                _part = new CustomPart { Name = basis.Name + " (özel)", Category = "Kullanıcı", Description = basis.Description, Author = Environment.UserName };
                _spec = UserPartSpec.FromBuiltIn(def, values);
                _spec.Source = "Parça oluşturucu (" + basis.Name + " temel alındı)";
                _fpLibraryId = def.ResolveFootprint(values);
            }
            else
            {
                _part = new CustomPart { Name = "Yeni parça", Category = "Kullanıcı", Author = Environment.UserName };
                _spec = new UserPartSpec { Source = "Parça oluşturucu" };
                for (int i = 1; i <= 4; i++) _spec.Pins.Add(new UserPin { Name = "P" + i.ToString(CultureInfo.InvariantCulture), Pad = i.ToString(CultureInfo.InvariantCulture), Side = i <= 2 ? PinSide.Left : PinSide.Right });
                _fpMode = FootprintMode.Generated;
                _pkg = new PackageSpec { Family = PackageFamily.Header, Pins = 4, Rows = 1, Pitch = 2.54, Height = 8.5 };
            }

            var root = new Grid { Background = B("B.Base") };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

            var left = new DockPanel { Margin = new Thickness(14, 18, 10, 14) };
            var head = new StackPanel();
            head.Children.Add(new TextBlock { Text = existing != null ? "PARÇA DÜZENLE" : "PARÇA OLUŞTURUCU", Style = S("Text.Overline") });
            head.Children.Add(new TextBlock { Text = "Kendi bileşeniniz", Style = S("Text.Subtitle"), Margin = new Thickness(0, 2, 0, 12) });
            DockPanel.SetDock(head, Dock.Top);
            left.Children.Add(head);
            foreach (var s in StepNames) _steps.Items.Add(new ListBoxItem { Content = s, Padding = new Thickness(8, 7, 8, 7) });
            _steps.SelectionChanged += (_, _) =>
            {
                if (_steps.SelectedIndex >= 0 && _steps.SelectedIndex != _step) Go(_steps.SelectedIndex);
            };
            left.Children.Add(_steps);
            root.Children.Add(left);
            var sep1 = new Border { Background = B("B.Border") };
            Grid.SetColumn(sep1, 1);
            root.Children.Add(sep1);

            var center = new DockPanel { Margin = new Thickness(22, 18, 18, 14) };
            var nav = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var navRight = Row(Btn("‹ Geri", () => Go(Math.Max(0, _step - 1))), Btn("İleri ›", () => Go(Math.Min(StepNames.Length - 1, _step + 1))));
            navRight.Children.Add(new Border { Width = 10 });
            navRight.Children.Add(Primary("Kütüphaneye kaydet", Save));
            DockPanel.SetDock(navRight, Dock.Right);
            nav.Children.Add(navRight);
            nav.Children.Add(Btn("Vazgeç", () => _w.Close(), ghost: true));
            DockPanel.SetDock(nav, Dock.Bottom);
            center.Children.Add(nav);
            center.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) });
            Grid.SetColumn(center, 2);
            root.Children.Add(center);
            var sep2 = new Border { Background = B("B.Border") };
            Grid.SetColumn(sep2, 3);
            root.Children.Add(sep2);

            var right = new StackPanel { Margin = new Thickness(16, 18, 16, 14) };
            right.Children.Add(new TextBlock { Text = "ŞEMATİK SEMBOL", Style = S("Text.Overline") });
            _symbol.Height = 230;
            right.Children.Add(Card(_symbol, 6));
            right.Children.Add(new TextBlock { Text = "KILIF", Style = S("Text.Overline"), Margin = new Thickness(0, 14, 0, 6) });
            _fpView.Height = 190;
            right.Children.Add(Card(_fpView, 0));
            right.Children.Add(new TextBlock { Text = "DURUM", Style = S("Text.Overline"), Margin = new Thickness(0, 14, 0, 6) });
            right.Children.Add(_status);
            Grid.SetColumn(right, 4);
            root.Children.Add(right);

            _w.Content = root;
            Go(0);
            UpdatePreview();
        }

        public Window Window => _w;

        public bool? ShowDialog() => _w.ShowDialog();

        private void Changed() => _debounce.Start();

        private UserPartSpec FromPreset(CustomPart p)
        {
            var def = _lib.Find(p.BasePartId);
            if (def == null) return new UserPartSpec();
            var values = _lib.ResolveParameters(def, null, p.Parameters);
            var spec = UserPartSpec.FromBuiltIn(def, values);
            spec.Source = "Parça oluşturucu";
            return spec;
        }

        private void LoadFootprintState(CustomPart p)
        {
            if (p.Footprint != null)
            {
                _fpMode = FootprintMode.File;
                _fpFile = p.Footprint.ToFootprint();
            }
            else if (p.Parameters.TryGetValue("footprint", out var id) && id.Length > 0)
            {
                _fpMode = FootprintMode.Library;
                _fpLibraryId = id;
            }
            else if (_lib.Find(p.BasePartId) is { } def && def.Footprint.Length > 0)
            {
                _fpMode = FootprintMode.Library;
                _fpLibraryId = def.Footprint;
            }
            else
            {
                _fpMode = FootprintMode.None;
            }
        }

        // ================================================================== state → objects

        private Footprint? CurrentFootprint()
        {
            try
            {
                return _fpMode switch
                {
                    FootprintMode.Library => string.IsNullOrWhiteSpace(_fpLibraryId) ? null : FootprintLibrary.Get(_fpLibraryId),
                    FootprintMode.Generated => PackageGenerator.Check(_pkg) == null ? PackageGenerator.Generate(_pkg) : null,
                    FootprintMode.File => _fpFile,
                    _ => null
                };
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is IndexOutOfRangeException || ex is OverflowException)
            {
                return null;
            }
        }

        private string NameOrDefault => string.IsNullOrWhiteSpace(_part.Name) ? "Yeni parça" : _part.Name.Trim();

        private void UpdatePreview()
        {
            var fp = CurrentFootprint();
            try
            {
                var def = _spec.ToDefinition("~creator", NameOrDefault, _part.Category, _part.Description, fp?.Id ?? string.Empty, _lib);
                _symbol.Show(def, _lib.ResolveParameters(def, null, null));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                _symbol.Show(null, null);
            }

            var pads = new HashSet<string>(_spec.Pins.Select(p => p.Pad.Trim()), StringComparer.OrdinalIgnoreCase);
            _fpView.Show(fp, fp?.Pads.Where(p => !pads.Contains(p.Name)).Select(p => p.Name));
            var log = Validate();
            _status.Inlines.Clear();
            _status.Inlines.Add(new System.Windows.Documents.Run(log.HasErrors ? $"{log.ErrorCount} hata" : "Hata yok") { Foreground = B(log.HasErrors ? "B.Danger" : "B.Success") });
            _status.Inlines.Add(new System.Windows.Documents.Run($" · {log.WarningCount} uyarı · {_spec.Pins.Count} bacak · " + (fp != null ? $"{fp.Id} ({fp.Pads.Count} pad)" : "kılıf yok")) { Foreground = B("B.TextMuted") });
            foreach (var i in log.Issues.Where(i => i.Severity == IssueSeverity.Error).Take(4))
            {
                _status.Inlines.Add(new System.Windows.Documents.LineBreak());
                _status.Inlines.Add(new System.Windows.Documents.Run("• " + i.Message) { Foreground = B("B.Danger") });
            }
        }

        private ImportLog Validate()
        {
            var log = new ImportLog();
            if (string.IsNullOrWhiteSpace(_part.Name)) log.Error("parçaya bir ad verin.");
            if (_fpMode == FootprintMode.Generated && PackageGenerator.Check(_pkg) is string why) log.Error("kılıf üretilemiyor: " + why);
            try
            {
                log.AddRange(_spec.Validate(CurrentFootprint(), _lib, NameOrDefault).Issues);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
            {
                log.Error("doğrulama yapılamadı: " + ex.Message);
            }

            return log;
        }

        // ================================================================== steps

        private void Go(int step)
        {
            _step = step;
            if (_steps.SelectedIndex != step) _steps.SelectedIndex = step;
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = StepNames[step].Substring(3), Style = S("Text.Title") });
            switch (step)
            {
                case 0: General(p); break;
                case 1: Pins(p); break;
                case 2: Symbol(p); break;
                case 3: FootprintStep(p); break;
                case 4: ModelStep(p); break;
                case 5: ParametersStep(p); break;
                case 6: Model3DStep(p); break;
                default: Finish(p); break;
            }

            _body.Content = p;
            UpdatePreview();
        }

        private TextBox Bound(Panel p, string label, string value, Action<string> set, string? tip = null)
        {
            var box = Field(p, label, value, tip);
            box.TextChanged += (_, _) =>
            {
                set(box.Text);
                Changed();
            };
            return box;
        }

        private void General(StackPanel p)
        {
            p.Children.Add(Caption("Parçanın kütüphanede ve malzeme listesinde görünen bilgileri. Kimlik (id) adından otomatik üretilir."));
            Bound(p, "Ad (parça numarası)", _part.Name, v => _part.Name = v);
            p.Children.Add(new TextBlock { Text = "Kategori", Style = S("Text.Caption"), Margin = new Thickness(0, 6, 0, 3) });
            var cat = new ComboBox { IsEditable = true, Text = _part.Category };
            foreach (var c in _lib.Categories.OrderBy(c => c)) cat.Items.Add(c);
            cat.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => _part.Category = cat.Text));
            cat.SelectionChanged += (_, _) =>
            {
                if (cat.SelectedItem is string s) _part.Category = s;
            };
            p.Children.Add(cat);
            Bound(p, "Açıklama", _part.Description, v => _part.Description = v);
            Bound(p, "Üretici", _part.Manufacturer, v => _part.Manufacturer = v);
            var ds = Bound(p, "Veri sayfası (adres ya da dosya)", _part.Datasheet, v => _part.Datasheet = v, "https://… veya C:\\…\\datasheet.pdf");
            p.Children.Add(Row(Btn("Dosya seç…", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf|Tüm dosyalar (*.*)|*.*", Title = "Veri sayfası" };
                if (dlg.ShowDialog(_w) == true) ds.Text = dlg.FileName;
            }), Btn("Aç", () => OpenDatasheet(_part.Datasheet))));
            Bound(p, "Referans öneki (R, C, U, Q, J …)", _spec.RefPrefix, v => _spec.RefPrefix = v);
            Bound(p, "Anahtar kelimeler (arama için)", _spec.Keywords, v => _spec.Keywords = v);
            Bound(p, "Yazar", _part.Author, v => _part.Author = v);
            if (_editingId != null) p.Children.Add(Caption("Kimlik: " + _editingId + " (düzenlemede değişmez)"));
        }

        internal static void OpenDatasheet(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            bool web = target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (!web && !File.Exists(target)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
            }
        }

        private void Pins(StackPanel p)
        {
            p.Children.Add(Caption("Her bacağın sembol adı, PCB'deki pad numarası, elektriksel türü (ERC için) ve sembolün hangi kenarında duracağı. Pad numaraları kılıfla eşleşmeli."));
            var table = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var w in new[] { 70.0, 160, 150, 90, 0, 34 })
            {
                table.ColumnDefinitions.Add(w > 0 ? new ColumnDefinition { Width = new GridLength(w) } : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            void Cell(UIElement e, int r, int c)
            {
                Grid.SetRow(e, r);
                Grid.SetColumn(e, c);
                table.Children.Add(e);
            }

            table.RowDefinitions.Add(new RowDefinition());
            string[] heads = { "Pad", "Ad", "Tür", "Taraf", "Açıklama" };
            for (int i = 0; i < heads.Length; i++) Cell(Caption(heads[i]), 0, i);
            var types = Enum.GetValues(typeof(PinElectricalType)).Cast<PinElectricalType>().ToList();
            for (int i = 0; i < _spec.Pins.Count; i++)
            {
                var pin = _spec.Pins[i];
                int r = i + 1;
                table.RowDefinitions.Add(new RowDefinition());
                var pad = new TextBox { Text = pin.Pad, Margin = new Thickness(0, 2, 6, 2) };
                pad.TextChanged += (_, _) =>
                {
                    pin.Pad = pad.Text.Trim();
                    Changed();
                };
                var name = new TextBox { Text = pin.Name, Margin = new Thickness(0, 2, 6, 2) };
                string oldName = pin.Name;
                name.TextChanged += (_, _) =>
                {
                    string nv = name.Text.Trim();
                    // Keep model mappings pointing at the renamed pin.
                    foreach (var key in _spec.Model.PinMap.Where(kv => string.Equals(kv.Value, oldName, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList()) _spec.Model.PinMap[key] = nv;
                    pin.Name = nv;
                    oldName = nv;
                    Changed();
                };
                var type = Combo(types, pin.Type, UserPartSpec.TypeName, t =>
                {
                    pin.Type = t;
                    Changed();
                });
                type.Margin = new Thickness(0, 2, 6, 2);
                var side = Combo(new[] { PinSide.Left, PinSide.Right }, pin.Side, s => s == PinSide.Left ? "Sol" : "Sağ", s =>
                {
                    pin.Side = s;
                    Changed();
                });
                side.Margin = new Thickness(0, 2, 6, 2);
                var desc = new TextBox { Text = pin.Description, Margin = new Thickness(0, 2, 6, 2) };
                desc.TextChanged += (_, _) => pin.Description = desc.Text;
                var del = new Button { Content = "✕", Style = S("Btn.Ghost"), ToolTip = "Bacağı sil", Padding = new Thickness(4, 0, 4, 0) };
                del.Click += (_, _) =>
                {
                    _spec.Pins.Remove(pin);
                    Go(1);
                };
                Cell(pad, r, 0);
                Cell(name, r, 1);
                Cell(type, r, 2);
                Cell(side, r, 3);
                Cell(desc, r, 4);
                Cell(del, r, 5);
            }

            p.Children.Add(table);
            var count = new TextBox { Text = "8", Width = 56 };
            p.Children.Add(Row(
                Btn("Bacak ekle", () =>
                {
                    AddPins(1);
                    Go(1);
                }),
                Btn("Kılıfın padlerinden üret", () =>
                {
                    var fp = CurrentFootprint();
                    if (fp == null)
                    {
                        Dialog.Show(_w, "Kılıf yok", "Önce 4. adımda bir kılıf seçin ya da üretin.", "Tamam");
                        return;
                    }

                    var have = new HashSet<string>(_spec.Pins.Select(x => x.Pad), StringComparer.OrdinalIgnoreCase);
                    int k = 0;
                    var padNames = fp.Pads.Select(x => x.Name).Where(n => n.Length > 0 && !have.Contains(n)).Distinct().OrderBy(n => n, Comparer<string>.Create(Natural)).ToList();
                    foreach (var n in padNames)
                    {
                        _spec.Pins.Add(new UserPin { Name = UniquePinName("P" + n), Pad = n, Side = k++ < (padNames.Count + 1) / 2 ? PinSide.Left : PinSide.Right });
                    }

                    Go(1);
                }, "Kılıftaki her pad için (henüz bacağı olmayan) bir bacak ekle"),
                Btn("Pad numarasına göre sırala", () =>
                {
                    _spec.Pins = _spec.Pins.OrderBy(x => x.Pad, Comparer<string>.Create(Natural)).ToList();
                    Go(1);
                }),
                new TextBlock { Text = "   adet:", Style = S("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) },
                count,
                Btn("Toplu ekle", () =>
                {
                    if (int.TryParse(count.Text, out int n) && n > 0 && n <= 512)
                    {
                        AddPins(n);
                        Go(1);
                    }
                })));
        }

        private static int Natural(string a, string b)
        {
            bool na = int.TryParse(a, out int x), nb = int.TryParse(b, out int y);
            if (na && nb) return x.CompareTo(y);
            if (na != nb) return na ? -1 : 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private string UniquePinName(string stem)
        {
            string name = stem;
            for (int i = 2; _spec.Pins.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); i++) name = stem + "_" + i.ToString(CultureInfo.InvariantCulture);
            return name;
        }

        private void AddPins(int n)
        {
            int next = _spec.Pins.Select(p => int.TryParse(p.Pad, out int v) ? v : 0).DefaultIfEmpty(0).Max() + 1;
            for (int i = 0; i < n; i++, next++)
            {
                string pad = next.ToString(CultureInfo.InvariantCulture);
                _spec.Pins.Add(new UserPin { Name = UniquePinName("P" + pad), Pad = pad, Side = _spec.Pins.Count(x => x.Side == PinSide.Left) <= _spec.Pins.Count(x => x.Side == PinSide.Right) ? PinSide.Left : PinSide.Right });
            }
        }

        private void Symbol(StackPanel p)
        {
            p.Children.Add(Caption("Şemada nasıl çizileceği. Dikdörtgen sembol bacak adlarını gösterir; 'modelin sembolü' yerleşik modelin çizimini (direnç, transistör, op-amp …) kullanır ve her bacağın bir model bacağına birebir eşlenmesini gerektirir."));
            void Radio(string text, UserSymbolStyle style)
            {
                var r = new RadioButton { Content = text, IsChecked = _spec.Symbol == style, Foreground = B("B.Text"), Margin = new Thickness(0, 6, 0, 2), GroupName = "sym" };
                r.Checked += (_, _) =>
                {
                    _spec.Symbol = style;
                    Changed();
                };
                p.Children.Add(r);
            }

            p.Children.Add(Overline("Sembol türü"));
            Radio("Dikdörtgen (bacaklar sol ve sağda, adlarıyla)", UserSymbolStyle.Box);
            Radio("Konnektör şeridi (tek sütun)", UserSymbolStyle.Connector);
            Radio("Yerleşik modelin sembolü", UserSymbolStyle.FromModel);
            Bound(p, "Gövde yazısı (boşsa parça adı)", _spec.BodyLabel, v => _spec.BodyLabel = v);
            var big = new DefinitionPreview { Height = 320, Margin = new Thickness(0, 14, 0, 0) };
            try
            {
                var def = _spec.ToDefinition("~creator", NameOrDefault, _part.Category, _part.Description, CurrentFootprint()?.Id ?? string.Empty, _lib);
                big.Show(def, _lib.ResolveParameters(def, null, null));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
            }

            p.Children.Add(Card(big, 8));
            p.Children.Add(Caption("Bacak sırası ve tarafı 2. adımda değiştirilir; uçlardaki küçük sayılar pad numaralarıdır."));
        }

        private void FootprintStep(StackPanel p)
        {
            p.Children.Add(Caption("PCB'de kullanılacak lehim yüzeyi. Kütüphaneden seçin, yaygın bir paketi ölçülerinden üretin ya da KiCad (.kicad_mod) / RobotWin (.rwfp) dosyasından alın. Kırmızı padler hiçbir bacağa bağlı değildir."));
            void Radio(string text, FootprintMode mode)
            {
                var r = new RadioButton { Content = text, IsChecked = _fpMode == mode, Foreground = B("B.Text"), Margin = new Thickness(0, 6, 0, 2), GroupName = "fp" };
                r.Checked += (_, _) =>
                {
                    _fpMode = mode;
                    Go(3);
                };
                p.Children.Add(r);
            }

            Radio("Kütüphaneden seç", FootprintMode.Library);
            Radio("Paket üret (DIP, SOIC, QFN, QFP, header, SOT-23)", FootprintMode.Generated);
            Radio("Dosyadan (.kicad_mod, .rwfp)", FootprintMode.File);
            Radio("Kılıf yok (yalnız şematik)", FootprintMode.None);
            var box = new StackPanel { Margin = new Thickness(24, 8, 0, 0) };
            p.Children.Add(box);
            switch (_fpMode)
            {
                case FootprintMode.Library:
                {
                    var search = new TextBox { Tag = "Kılıf ara…", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
                    var list = new ListBox { Height = 260, Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                    void FillList()
                    {
                        list.Items.Clear();
                        string q = search.Text.Trim().ToLowerInvariant();
                        foreach (var id in FootprintLibrary.AllIds.Where(id => q.Length == 0 || id.ToLowerInvariant().Contains(q)).Take(500))
                        {
                            var item = new ListBoxItem { Content = id, Tag = id };
                            list.Items.Add(item);
                            if (string.Equals(id, _fpLibraryId, StringComparison.OrdinalIgnoreCase)) list.SelectedItem = item;
                        }
                    }

                    search.TextChanged += (_, _) => FillList();
                    list.SelectionChanged += (_, _) =>
                    {
                        if (list.SelectedItem is ListBoxItem { Tag: string id })
                        {
                            _fpLibraryId = id;
                            Changed();
                        }
                    };
                    FillList();
                    box.Children.Add(search);
                    box.Children.Add(list);
                    break;
                }

                case FootprintMode.Generated:
                {
                    var families = Enum.GetValues(typeof(PackageFamily)).Cast<PackageFamily>().ToList();
                    box.Children.Add(Caption("Aile"));
                    box.Children.Add(Combo(families, _pkg.Family, PackageSpec.FamilyName, f =>
                    {
                        _pkg = PackageSpec.Default(f);
                        Go(3);
                    }));
                    void Num(string label, double value, Action<double> set)
                    {
                        var t = Field(box, label, value.ToString("0.###", CultureInfo.InvariantCulture));
                        t.Width = 160;
                        t.HorizontalAlignment = HorizontalAlignment.Left;
                        t.TextChanged += (_, _) =>
                        {
                            if (double.TryParse(t.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                            {
                                set(v);
                                Changed();
                            }
                        };
                    }

                    Num("Bacak sayısı", _pkg.Pins, v => _pkg.Pins = (int)Math.Round(v));
                    Num("Adım (mm)", _pkg.Pitch, v => _pkg.Pitch = v);
                    if (_pkg.Family == PackageFamily.Header) Num("Sıra sayısı (1 / 2)", _pkg.Rows, v => _pkg.Rows = (int)Math.Round(v));
                    else if (_pkg.Family != PackageFamily.Sot23)
                    {
                        Num(_pkg.Family == PackageFamily.Dip ? "Sıra aralığı (mm)" : _pkg.Family == PackageFamily.Soic ? "Ped merkezleri arası (mm)" : "Gövde (mm)", _pkg.Span, v => _pkg.Span = v);
                    }

                    if (_pkg.Family == PackageFamily.Qfn || _pkg.Family == PackageFamily.Qfp) Num("Isı pedi (mm, 0 = yok)", _pkg.ExposedPad, v => _pkg.ExposedPad = v);
                    Num("Yükseklik (mm, 3B)", _pkg.Height, v => _pkg.Height = v);
                    box.Children.Add(Row(Btn("Bacak sayısını parçadan al", () =>
                    {
                        _pkg.Pins = Math.Max(1, _spec.Pins.Count);
                        Go(3);
                    })));
                    box.Children.Add(Caption("Kimlik: " + PackageGenerator.IdFor(_pkg) + (PackageGenerator.Check(_pkg) is string why ? " — " + why : string.Empty)));
                    break;
                }

                case FootprintMode.File:
                {
                    box.Children.Add(Row(Btn("Dosya seç…", PickFootprintFile)));
                    box.Children.Add(Caption(_fpFile != null ? $"Yüklenen: {_fpFile.Id} · {_fpFile.Pads.Count} pad · {_fpFile.Description}" : "Henüz dosya seçilmedi."));
                    break;
                }
            }

            var view = new FootprintView { Height = 260, Margin = new Thickness(0, 14, 0, 0) };
            var fp = CurrentFootprint();
            var pads = new HashSet<string>(_spec.Pins.Select(x => x.Pad.Trim()), StringComparer.OrdinalIgnoreCase);
            view.Show(fp, fp?.Pads.Where(x => !pads.Contains(x.Name)).Select(x => x.Name));
            p.Children.Add(Card(view, 0));
            if (fp != null)
            {
                var missing = _spec.Pins.Where(x => x.Pad.Length > 0 && fp.FindPad(x.Pad) == null).Select(x => x.Name + "→" + x.Pad).ToList();
                if (missing.Count > 0) p.Children.Add(new TextBlock { Text = "Kılıfta olmayan padler: " + string.Join(", ", missing), Foreground = B("B.Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            }
        }

        private void PickFootprintFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Footprint (*.kicad_mod, *.rwfp)|*.kicad_mod;*.rwfp|Tüm dosyalar (*.*)|*.*", Title = "Footprint dosyası" };
            if (dlg.ShowDialog(_w) != true) return;
            try
            {
                if (dlg.FileName.EndsWith(".rwfp", StringComparison.OrdinalIgnoreCase)) _fpFile = CustomFootprint.Load(dlg.FileName).ToFootprint();
                else
                {
                    _fpFile = KiCadLibraryImport.ImportFootprint(File.ReadAllText(dlg.FileName));
                    _fpFile.Id = Path.GetFileNameWithoutExtension(dlg.FileName);
                    if (FootprintLibrary.IsBuiltIn(_fpFile.Id)) _fpFile.Id = "KiCad_" + _fpFile.Id;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException)
            {
                Dialog.Show(_w, "Footprint okunamadı", Path.GetFileName(dlg.FileName) + ": " + ex.Message, "Tamam");
                return;
            }

            Go(3);
        }

        private void ModelStep(StackPanel p)
        {
            p.Children.Add(Caption("Parçanın simülasyonda nasıl davranacağı. Model bacakları parçanın bacaklarına eşlenir; eşlenmeyen model bacakları açık kalır."));
            var model = _spec.Model;
            void Radio(string text, UserModelKind kind)
            {
                var r = new RadioButton { Content = text, IsChecked = model.Kind == kind, Foreground = B("B.Text"), Margin = new Thickness(0, 6, 0, 2), GroupName = "model" };
                r.Checked += (_, _) =>
                {
                    model.Kind = kind;
                    if (kind == UserModelKind.Behavioral && model.PinMap.Count == 0) AutoMap();
                    Go(4);
                };
                p.Children.Add(r);
            }

            Radio("Elektriksel model yok (konnektör, mekanik parça, yalnız net taşır)", UserModelKind.None);
            Radio("Yerleşik simülasyon modeli (direnç … op-amp, regülatör, MOSFET …)", UserModelKind.BuiltIn);
            Radio("SPICE alt devresi (.subckt; R C L V I D Q M E G F H)", UserModelKind.Subcircuit);
            Radio("İdeal davranışsal model (kaynak, yük, regülatör)", UserModelKind.Behavioral);
            var box = new StackPanel { Margin = new Thickness(24, 10, 0, 0) };
            p.Children.Add(box);

            switch (model.Kind)
            {
                case UserModelKind.BuiltIn:
                {
                    var defs = _lib.Parts.Where(d => d.Build != null && !d.Id.StartsWith("udef-", StringComparison.OrdinalIgnoreCase) && !d.Id.StartsWith("~", StringComparison.Ordinal))
                        .OrderBy(d => d.Category).ThenBy(d => d.Name).ToList();
                    var current = defs.FirstOrDefault(d => d.Id == model.BuiltInPartId);
                    box.Children.Add(Caption("Model"));
                    var combo = Combo(defs, current ?? defs.First(), d => d.Category + " · " + d.Name + " (" + d.Id + ")", d =>
                    {
                        model.BuiltInPartId = d.Id;
                        model.PinMap.Clear();
                        AutoMap();
                        Go(4);
                    });
                    if (current == null && defs.Count > 0)
                    {
                        model.BuiltInPartId = defs[0].Id;
                        AutoMap();
                    }

                    combo.Width = 460;
                    combo.HorizontalAlignment = HorizontalAlignment.Left;
                    box.Children.Add(combo);
                    box.Children.Add(Row(
                        Btn("Bacakları modelden al", () =>
                        {
                            var def = _lib.Find(model.BuiltInPartId);
                            if (def == null) return;
                            var spec = UserPartSpec.FromBuiltIn(def);
                            _spec.Pins = spec.Pins;
                            model.PinMap = spec.Model.PinMap;
                            Go(4);
                        }, "Parçanın bacaklarını modelin bacaklarıyla değiştir (adlar ve pad numaraları modelden)"),
                        Btn("Model parametrelerini ekle", () =>
                        {
                            AddModelParameters();
                            Go(4);
                        })));
                    MapTable(box, model.ModelPins(_lib));
                    break;
                }

                case UserModelKind.Subcircuit:
                {
                    box.Children.Add(Row(Btn("SPICE dosyası aç…", () =>
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "SPICE (*.lib, *.mod, *.cir, *.sub, *.sp)|*.lib;*.mod;*.cir;*.sub;*.sp;*.spi|Tüm dosyalar (*.*)|*.*" };
                        if (dlg.ShowDialog(_w) != true) return;
                        var lib = SpiceParser.ParseFile(dlg.FileName);
                        string? name = lib.Subcircuits.Keys.FirstOrDefault();
                        if (name == null)
                        {
                            Dialog.Show(_w, ".subckt yok", "Dosyada alt devre tanımı bulunamadı.\n" + string.Join("\n", lib.Log.Issues.Take(6)), "Tamam");
                            return;
                        }

                        model.SpiceText = string.Join("\n", lib.Subcircuits.Keys.Select(k => lib.Extract(k)));
                        model.SubcircuitName = name;
                        model.PinMap.Clear();
                        AutoMap();
                        Go(4);
                    })));
                    var spice = model.Spice();
                    if (spice.Subcircuits.Count > 0)
                    {
                        box.Children.Add(Caption("Alt devre"));
                        var names = spice.Subcircuits.Keys.ToList();
                        if (!names.Contains(model.SubcircuitName)) model.SubcircuitName = names[0];
                        box.Children.Add(Combo(names, model.SubcircuitName, n => n + " (" + spice.Subcircuits[n].Pins.Count + " bacak)", n =>
                        {
                            model.SubcircuitName = n;
                            model.PinMap.Clear();
                            AutoMap();
                            Go(4);
                        }));
                    }

                    box.Children.Add(Caption("SPICE metni (düzenleyebilirsiniz; değişince 'Uygula')"));
                    var text = new TextBox
                    {
                        Text = model.SpiceText, AcceptsReturn = true, FontFamily = (FontFamily)Application.Current.FindResource("F.Mono"), FontSize = 12, Height = 200,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap
                    };
                    box.Children.Add(text);
                    box.Children.Add(Row(Btn("Uygula", () =>
                    {
                        var m2 = new UserModel { Kind = UserModelKind.Subcircuit, SpiceText = text.Text, SubcircuitName = model.SubcircuitName };
                        var parsed = m2.Spice();
                        model.SpiceText = text.Text;
                        _spec.Model = model.Clone();
                        if (!parsed.Subcircuits.ContainsKey(model.SubcircuitName)) _spec.Model.SubcircuitName = parsed.Subcircuits.Keys.FirstOrDefault() ?? string.Empty;
                        Go(4);
                    }), Btn("Bacakları alt devreden üret", () =>
                    {
                        var sub = model.Spice().Subcircuits.TryGetValue(model.SubcircuitName, out var s) ? s : null;
                        if (sub == null) return;
                        _spec.Pins.Clear();
                        model.PinMap.Clear();
                        for (int i = 0; i < sub.Pins.Count; i++)
                        {
                            string name = UniquePinName(sub.Pins[i]);
                            _spec.Pins.Add(new UserPin { Name = name, Pad = (i + 1).ToString(CultureInfo.InvariantCulture), Side = i < (sub.Pins.Count + 1) / 2 ? PinSide.Left : PinSide.Right });
                            model.PinMap[sub.Pins[i]] = name;
                        }

                        foreach (var kv in sub.Parameters.Where(kv => !_spec.Parameters.Any(x => x.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))))
                        {
                            _spec.Parameters.Add(new UserParameter { Key = kv.Key, Label = kv.Key, Value = SpiceSyntax.Unquote(kv.Value).Trim('{', '}') });
                        }

                        Go(4);
                    })));
                    MapTable(box, model.ModelPins(_lib));
                    if (spice.Subcircuits.ContainsKey(model.SubcircuitName))
                    {
                        var check = SpiceCircuitBuilder.Check(spice, model.SubcircuitName, out int dev, out int skipped);
                        box.Children.Add(Overline($"Alt devre denetimi: {dev} eleman, {skipped} atlanan"));
                        box.Children.Add(Issues(check.Issues.Concat(spice.Log.Issues.Where(i => i.Severity != IssueSeverity.Info)), 200));
                    }

                    break;
                }

                case UserModelKind.Behavioral:
                {
                    var kinds = Enum.GetValues(typeof(BehavioralKind)).Cast<BehavioralKind>().ToList();
                    box.Children.Add(Caption("Davranış"));
                    box.Children.Add(Combo(kinds, model.Behavior, UserPartSpec.BehavioralName, k =>
                    {
                        model.Behavior = k;
                        model.PinMap.Clear();
                        AutoMap();
                        Go(4);
                    }));
                    box.Children.Add(Caption("Değerler (gerilim, akım, direnç) 6. adımdaki parametrelerden okunur; her yerleştirilen parçada ayrıca değiştirilebilir."));
                    MapTable(box, model.ModelPins(_lib));
                    foreach (var (k, l, v, u) in UserPartSpec.BehavioralParameters(model.Behavior))
                    {
                        if (!_spec.Parameters.Any(x => x.Key.Equals(k, StringComparison.OrdinalIgnoreCase))) _spec.Parameters.Add(new UserParameter { Key = k, Label = l, Value = v, Unit = u });
                    }

                    break;
                }
            }
        }

        /// <summary>Maps model pins to user pins with the same name, else by position.</summary>
        private void AutoMap()
        {
            var model = _spec.Model;
            var pins = model.ModelPins(_lib);
            var used = new HashSet<string>(model.PinMap.Values, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pins.Count; i++)
            {
                if (model.PinMap.ContainsKey(pins[i])) continue;
                var same = _spec.Pins.FirstOrDefault(p => string.Equals(p.Name, pins[i], StringComparison.OrdinalIgnoreCase) && !used.Contains(p.Name));
                var byIndex = same ?? _spec.Pins.Where(p => !used.Contains(p.Name)).Skip(0).FirstOrDefault();
                if (byIndex != null)
                {
                    model.PinMap[pins[i]] = byIndex.Name;
                    used.Add(byIndex.Name);
                }
            }
        }

        private void AddModelParameters()
        {
            var def = _lib.Find(_spec.Model.BuiltInPartId);
            if (def == null) return;
            foreach (var p in def.Parameters.Where(p => p.Key != "pinmap" && p.Key != "footprint" && p.Key != "pinout" && p.Key != "model"))
            {
                if (_spec.Parameters.Any(x => x.Key.Equals(p.Key, StringComparison.OrdinalIgnoreCase))) continue;
                _spec.Parameters.Add(new UserParameter { Key = p.Key, Label = p.Label, Value = p.DefaultValue, Unit = p.Unit });
            }
        }

        private void MapTable(Panel box, IReadOnlyList<string> modelPins)
        {
            box.Children.Add(Overline("Model bacağı → parça bacağı"));
            if (modelPins.Count == 0)
            {
                box.Children.Add(Caption("Modelin bacağı yok."));
                return;
            }

            var options = new List<string> { string.Empty };
            options.AddRange(_spec.Pins.Select(p => p.Name));
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            for (int i = 0; i < modelPins.Count; i++)
            {
                string mp = modelPins[i];
                grid.RowDefinitions.Add(new RowDefinition());
                var label = Mono(mp);
                label.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(label, i);
                grid.Children.Add(label);
                string current = _spec.Model.PinMap.TryGetValue(mp, out var v) ? v : string.Empty;
                var combo = Combo(options, options.Contains(current) ? current : string.Empty, o => o.Length == 0 ? "— açık —" : o, o =>
                {
                    if (o.Length == 0) _spec.Model.PinMap.Remove(mp);
                    else _spec.Model.PinMap[mp] = o;
                    Changed();
                });
                combo.Margin = new Thickness(0, 2, 0, 2);
                Grid.SetRow(combo, i);
                Grid.SetColumn(combo, 1);
                grid.Children.Add(combo);
            }

            box.Children.Add(grid);
        }

        private void ParametersStep(StackPanel p)
        {
            p.Children.Add(Caption("Parçanın değerleri. Birimi olan değerler '4.7k', '100n', '3V3' gibi yazılabilir. Tolerans (%) verilen parametreler 'gerçekçi' ve Monte Carlo simülasyonlarında bu bant içinde dağıtılır. Her parametre yerleştirilen parçada ayrıca değiştirilebilir."));
            var table = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var w in new[] { 120.0, 180, 120, 70, 90, 0, 34 })
            {
                table.ColumnDefinitions.Add(w > 0 ? new ColumnDefinition { Width = new GridLength(w) } : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            void Cell(UIElement e, int r, int c)
            {
                Grid.SetRow(e, r);
                Grid.SetColumn(e, c);
                table.Children.Add(e);
            }

            table.RowDefinitions.Add(new RowDefinition());
            string[] heads = { "Anahtar", "Etiket", "Değer", "Birim", "Tolerans %", "Açıklama" };
            for (int i = 0; i < heads.Length; i++) Cell(Caption(heads[i]), 0, i);
            for (int i = 0; i < _spec.Parameters.Count; i++)
            {
                var prm = _spec.Parameters[i];
                int r = i + 1;
                table.RowDefinitions.Add(new RowDefinition());
                TextBox T(string v, Action<string> set)
                {
                    var t = new TextBox { Text = v, Margin = new Thickness(0, 2, 6, 2) };
                    t.TextChanged += (_, _) =>
                    {
                        set(t.Text.Trim());
                        Changed();
                    };
                    return t;
                }

                Cell(T(prm.Key, v => prm.Key = v), r, 0);
                Cell(T(prm.Label, v => prm.Label = v), r, 1);
                Cell(T(prm.Value, v => prm.Value = v), r, 2);
                Cell(T(prm.Unit, v => prm.Unit = v), r, 3);
                Cell(T(prm.Tolerance, v => prm.Tolerance = v), r, 4);
                Cell(T(prm.Description, v => prm.Description = v), r, 5);
                var del = new Button { Content = "✕", Style = S("Btn.Ghost"), Padding = new Thickness(4, 0, 4, 0) };
                del.Click += (_, _) =>
                {
                    _spec.Parameters.Remove(prm);
                    Go(5);
                };
                Cell(del, r, 6);
            }

            p.Children.Add(table);
            var buttons = Row(Btn("Parametre ekle", () =>
            {
                string key = "param";
                for (int i = 2; _spec.Parameters.Any(x => x.Key == key); i++) key = "param" + i.ToString(CultureInfo.InvariantCulture);
                _spec.Parameters.Add(new UserParameter { Key = key, Label = "Yeni parametre" });
                Go(5);
            }));
            if (_spec.Model.Kind == UserModelKind.BuiltIn)
            {
                buttons.Children.Add(Btn("Modelin parametrelerini ekle", () =>
                {
                    AddModelParameters();
                    Go(5);
                }));
            }

            p.Children.Add(buttons);
        }

        private void Model3DStep(StackPanel p)
        {
            p.Children.Add(Caption("3B görünümde (PCB 3B, Montaj, Dünya) parçanın nasıl görüneceği."));
            void Radio(string text, ModelMode mode)
            {
                var r = new RadioButton { Content = text, IsChecked = _modelMode == mode, Foreground = B("B.Text"), Margin = new Thickness(0, 6, 0, 2), GroupName = "m3d" };
                r.Checked += (_, _) =>
                {
                    _modelMode = mode;
                    Go(6);
                };
                p.Children.Add(r);
            }

            Radio("Kılıftan otomatik üret", ModelMode.FromFootprint);
            Radio("Kutu (ölçüleriyle)", ModelMode.Box);
            Radio("Dosyadan (STL, OBJ, PLY, WRL)", ModelMode.File);
            Radio("Başka bir parçanın modelini kullan", ModelMode.OtherPart);
            var box = new StackPanel { Margin = new Thickness(24, 8, 0, 0) };
            p.Children.Add(box);
            switch (_modelMode)
            {
                case ModelMode.Box:
                {
                    void Num(string label, double value, Action<double> set)
                    {
                        var t = Field(box, label, value.ToString("0.##", CultureInfo.InvariantCulture));
                        t.Width = 120;
                        t.HorizontalAlignment = HorizontalAlignment.Left;
                        t.LostFocus += (_, _) =>
                        {
                            if (double.TryParse(t.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0)
                            {
                                set(v);
                                Go(6);
                            }
                        };
                    }

                    Num("Genişlik X (mm)", _boxW, v => _boxW = v);
                    Num("Derinlik Y (mm)", _boxD, v => _boxD = v);
                    Num("Yükseklik Z (mm)", _boxH, v => _boxH = v);
                    var color = Field(box, "Renk (#RRGGBB)", _boxColor);
                    color.Width = 120;
                    color.HorizontalAlignment = HorizontalAlignment.Left;
                    color.LostFocus += (_, _) =>
                    {
                        _boxColor = color.Text.Trim();
                        Go(6);
                    };
                    box.Children.Add(Row(Btn("Kılıf boyutunu al", () =>
                    {
                        var fp = CurrentFootprint();
                        if (fp == null) return;
                        _boxW = Math.Round(fp.CourtyardMaxX - fp.CourtyardMinX - 0.5, 2);
                        _boxD = Math.Round(fp.CourtyardMaxY - fp.CourtyardMinY - 0.5, 2);
                        _boxH = fp.Height > 0 ? fp.Height : _boxH;
                        Go(6);
                    })));
                    break;
                }

                case ModelMode.File:
                    box.Children.Add(Row(Btn("Model dosyası seç…", PickModel)));
                    box.Children.Add(Caption(_model3d != null ? "Yüklenen: " + _model3d.Name : "STEP/IGES desteklenmez (CAD çekirdeği gerekir): KiCad/FreeCAD'den STL, OBJ ya da WRL olarak dışa aktarın."));
                    break;
                case ModelMode.OtherPart:
                {
                    var withModels = _lib.Presets.Where(x => x.Parameters.TryGetValue(PartModels3D.ParameterKey, out _)).ToList();
                    if (withModels.Count == 0)
                    {
                        box.Children.Add(Caption("Kendi 3B modeli olan başka parça yok."));
                        break;
                    }

                    var cur = withModels.FirstOrDefault(x => x.Id == _otherModel) ?? withModels[0];
                    _otherModel = cur.Id;
                    box.Children.Add(Combo(withModels, cur, x => x.Name + " (" + x.Id + ")", x =>
                    {
                        _otherModel = x.Id;
                        Go(6);
                    }));
                    break;
                }
            }

            // Preview through the normal 3D model pipeline.
            var preview = new RobotWin.Studio.Models3D.ModelPreview { Height = 300, Margin = new Thickness(0, 14, 0, 0) };
            p.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = preview });
            try
            {
                var fp = CurrentFootprint();
                if (fp != null && !FootprintLibrary.IsBuiltIn(fp.Id) && FootprintLibrary.Get(fp.Id) == null) FootprintLibrary.Register(fp);
                var def = _spec.ToDefinition("~creator", NameOrDefault, _part.Category, _part.Description, fp?.Id ?? string.Empty, _lib);
                var values = _lib.ResolveParameters(def, null, null);
                var m = Current3D();
                if (m != null)
                {
                    PartModels3D.Register("~creator-preview", m);
                    values[PartModels3D.ParameterKey] = "~creator-preview";
                }
                else if (_modelMode == ModelMode.OtherPart && _otherModel.Length > 0)
                {
                    values[PartModels3D.ParameterKey] = _otherModel;
                }

                preview.Show(def, values);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is InvalidDataException || ex is FormatException)
            {
                p.Children.Add(Caption("3B önizleme yok: " + ex.Message));
            }
        }

        private PartModel3D? Current3D()
        {
            switch (_modelMode)
            {
                case ModelMode.Box:
                    return PartModelFiles.Box(_boxW, _boxD, _boxH, _boxColor);
                case ModelMode.File:
                    return _model3d;
                default:
                    return null;
            }
        }

        private void PickModel()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "3B model (*.stl, *.obj, *.ply, *.wrl)|*.stl;*.obj;*.ply;*.wrl;*.wrz|STEP (*.step, *.stp)|*.step;*.stp|Tüm dosyalar (*.*)|*.*", Title = "3B model" };
            if (dlg.ShowDialog(_w) != true) return;
            try
            {
                _model3d = Models3D.CustomModels.Load(dlg.FileName);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is FormatException || ex is NotSupportedException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(_w, "Model okunamadı", Path.GetFileName(dlg.FileName) + ": " + ex.Message, "Tamam");
                return;
            }

            Go(6);
        }

        private void Finish(StackPanel p)
        {
            var log = Validate();
            p.Children.Add(Body(log.HasErrors
                ? "Kaydetmeden önce kırmızı hataları düzeltin. Uyarılar kaydetmeyi engellemez."
                : "Parça kaydedilmeye hazır. Kullanıcı kütüphanesine .rwpart olarak yazılır ve hemen yerleştirilebilir."));
            p.Children.Add(Overline($"Doğrulama · {log.Summary()}"));
            p.Children.Add(Issues(log.Issues, 420));
            p.Children.Add(Row(Primary("Kütüphaneye kaydet", Save), Btn(".rwpart olarak dışa aktar…", Export, "Paylaşmak için ayrı bir dosyaya yaz")));
        }

        // ================================================================== save

        private CustomPart? BuildPart(out ImportLog log)
        {
            log = Validate();
            if (log.HasErrors) return null;
            string id = _editingId ?? UserLibrary.MakeId(_lib, NameOrDefault);
            var part = new CustomPart
            {
                Id = id,
                Name = NameOrDefault,
                BasePartId = "udef-" + id,
                Category = string.IsNullOrWhiteSpace(_part.Category) ? "Kullanıcı" : _part.Category.Trim(),
                Description = _part.Description.Trim(),
                Author = string.IsNullOrWhiteSpace(_part.Author) ? Environment.UserName : _part.Author.Trim(),
                Manufacturer = _part.Manufacturer.Trim(),
                Datasheet = _part.Datasheet.Trim(),
                Definition = _spec.Clone(),
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
            foreach (var prm in _spec.Parameters.Where(x => !string.IsNullOrWhiteSpace(x.Key))) part.Parameters[prm.Key.Trim()] = prm.Value;
            var fp = CurrentFootprint();
            if (fp != null)
            {
                part.Parameters["footprint"] = fp.Id;
                if (_fpMode != FootprintMode.Library || !FootprintLibrary.IsBuiltIn(fp.Id)) part.Footprint = CustomFootprint.From(fp);
            }

            part.Model3D = Current3D();
            if (_modelMode == ModelMode.OtherPart && _otherModel.Length > 0 && part.Model3D == null) part.Parameters[PartModels3D.ParameterKey] = _otherModel;
            return part;
        }

        private void Save()
        {
            var part = BuildPart(out var log);
            if (part == null)
            {
                Dialog.Show(_w, "Kaydedilemedi", "Düzeltilmesi gerekenler:\n• " + string.Join("\n• ", log.Issues.Where(i => i.Severity == IssueSeverity.Error).Take(8).Select(i => i.Message)), "Tamam");
                Go(7);
                return;
            }

            try
            {
                LibraryManager.SaveUserPart(_lib, part, _editingId);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is InvalidDataException || ex is ArgumentException)
            {
                Dialog.Show(_w, "Kaydedilemedi", ex.Message, "Tamam");
                return;
            }

            PartModels3D.Unregister("~creator-preview");
            SavedId = part.Id;
            Workspace.Current.Status = $"'{part.Name}' kütüphaneye kaydedildi ({_spec.Pins.Count} bacak" + (log.WarningCount > 0 ? $", {log.WarningCount} uyarı" : string.Empty) + ")";
            _w.DialogResult = true;
            _w.Close();
        }

        private void Export()
        {
            var part = BuildPart(out var log);
            if (part == null)
            {
                Dialog.Show(_w, "Dışa aktarılamadı", "Önce hataları düzeltin.", "Tamam");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "RobotWin parçası (*.rwpart)|*.rwpart", FileName = part.Name, DefaultExt = RobotWinFormats.PartExtension };
            if (dlg.ShowDialog(_w) != true) return;
            try
            {
                part.Save(dlg.FileName);
                Workspace.Current.Status = "Parça dışa aktarıldı: " + Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(_w, "Dışa aktarılamadı", ex.Message, "Tamam");
            }
        }

        /// <summary>For snapshots: open on a given step with a sample part.</summary>
        internal void ShowStep(int step) => Go(step);

        internal void LoadSample()
        {
            _part.Name = "LM2596 modülü";
            _part.Category = "Güç modülleri";
            _part.Description = "Ayarlanabilir buck dönüştürücü modülü, 3 A";
            _part.Manufacturer = "Genel";
            _spec.RefPrefix = "U";
            _spec.Pins = new List<UserPin>
            {
                new UserPin { Name = "IN+", Pad = "1", Type = PinElectricalType.PowerIn, Side = PinSide.Left },
                new UserPin { Name = "IN-", Pad = "2", Type = PinElectricalType.PowerIn, Side = PinSide.Left },
                new UserPin { Name = "OUT+", Pad = "3", Type = PinElectricalType.PowerOut, Side = PinSide.Right },
                new UserPin { Name = "OUT-", Pad = "4", Type = PinElectricalType.PowerOut, Side = PinSide.Right }
            };
            _spec.Model = new UserModel { Kind = UserModelKind.Behavioral, Behavior = BehavioralKind.Regulator };
            _spec.Model.PinMap["IN"] = "IN+";
            _spec.Model.PinMap["GND"] = "IN-";
            _spec.Model.PinMap["OUT"] = "OUT+";
            _spec.Parameters = new List<UserParameter> { new UserParameter { Key = "voltage", Label = "Çıkış gerilimi", Value = "5", Unit = "V", Tolerance = "2" } };
            _fpMode = FootprintMode.Generated;
            _pkg = new PackageSpec { Family = PackageFamily.Header, Pins = 4, Rows = 1, Pitch = 2.54, Height = 8.5 };
            _modelMode = ModelMode.Box;
            _boxW = 43;
            _boxD = 21;
            _boxH = 14;
            _boxColor = "#1F5FAF";
        }
    }
}
