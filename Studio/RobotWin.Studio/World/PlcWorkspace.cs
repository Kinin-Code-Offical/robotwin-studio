using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RobotTwin.CoreSim.Industrial;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [plc-workspace] A PLC's program in a workspace of its own, laid out as PLC editors are: the project tree on the left
    /// (the program, its variables, the function blocks and functions it defines, then the block library: click to put a
    /// call at the cursor), the editor in the middle with the variable table (section, name, address, type, initial value,
    /// comment, the live value, and whether to watch it) and the watch chart (the watched variables over the last
    /// seconds) as tabs beside it, and the console below: the check's messages, a click on one goes to its line. The program
    /// is checked as it is written; saving writes it to the PLC (and into the running PLC, cold).
    /// </summary>
    internal sealed class PlcWorkspace : Window
    {
        private readonly string _plcName;
        private readonly Func<string, double> _read;
        private readonly Func<string> _state;
        private readonly Action<string> _save;
        private readonly Action _restart;
        private readonly TextBox _editor = new TextBox();
        private readonly StackPanel _tree = new StackPanel();
        private readonly StackPanel _console = new StackPanel();
        private readonly Grid _table = new Grid();
        private readonly TabControl _tabs = new TabControl();
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 14, 0) };
        private readonly HmiTrend _chart = new HmiTrend { AutoRange = true, Seconds = 10 };
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        private readonly HashSet<string> _watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<(StVariable Var, TextBlock Value)> _live = new List<(StVariable, TextBlock)>();
        private readonly Func<double> _time;
        private List<StVariable> _vars = new List<StVariable>();
        private bool _dirty;

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        /// <summary>
        /// The block library: (name, what it does, the call it puts at the cursor). FBs need an instance declared under the
        /// same name; functions return their value.
        /// </summary>
        private static readonly (string Group, string Name, string Help, string Call)[] Library =
        {
            ("Zamanlayıcılar", "TON", "Gecikmeli açan: IN PT süresi TRUE kalınca Q açılır", "T1(IN := Giris, PT := T#1s);"),
            ("Zamanlayıcılar", "TOF", "Gecikmeli kapanan: IN düşünce Q PT süresi daha açık kalır", "T2(IN := Giris, PT := T#1s);"),
            ("Zamanlayıcılar", "TP", "Darbe: IN'in yükselişinde Q PT süresince açık", "T3(IN := Giris, PT := T#500ms);"),
            ("Sayıcılar", "CTU", "Yukarı sayar: CU'nun her yükselişinde CV artar, PV'ye varınca Q; R sıfırlar", "C1(CU := Giris, R := Sifirla, PV := 10);"),
            ("Sayıcılar", "CTD", "Aşağı sayar: CD'nin her yükselişinde CV azalır, 0'da Q; LD PV'yi yükler", "C2(CD := Giris, LD := Yukle, PV := 10);"),
            ("Sayıcılar", "CTUD", "İki yönlü sayıcı", "C3(CU := Ileri, CD := Geri, R := Sifirla, LD := Yukle, PV := 10);"),
            ("Kenarlar", "R_TRIG", "Yükselen kenar: CLK'nin TRUE olduğu taramada Q", "Yukselis(CLK := Giris);"),
            ("Kenarlar", "F_TRIG", "Düşen kenar: CLK'nin FALSE olduğu taramada Q", "Dusus(CLK := Giris);"),
            ("Tutucular", "SR", "Kur baskın tutucu: S1 kurar, R sıfırlar", "Tutucu(S1 := Kur, R := Sifirla);"),
            ("Tutucular", "RS", "Sıfırla baskın tutucu: S kurar, R1 sıfırlar", "Tutucu2(S := Kur, R1 := Sifirla);"),
            ("Modbus", "MB_READ", "REQ'in yükselişinde UNIT'teki cihazdan FC 1–4 ile ADDR'yi okur; DONE, BUSY, ERROR, VALUE", "Oku(REQ := Iste, UNIT := 1, FC := 3, ADDR := 0);"),
            ("Modbus", "MB_WRITE", "REQ'in yükselişinde FC 5 ya da 6 ile VALUE'yu yazar", "Yaz(REQ := Iste, UNIT := 1, FC := 6, ADDR := 0, VALUE := Deger);"),
            ("Fonksiyonlar", "LIMIT", "Değeri MN ile MX arasında tutar", "LIMIT(0, Deger, 100)"),
            ("Fonksiyonlar", "SEL", "G FALSE ise IN0, TRUE ise IN1", "SEL(Kosul, A, B)"),
            ("Fonksiyonlar", "MIN", "Küçüğü", "MIN(A, B)"),
            ("Fonksiyonlar", "MAX", "Büyüğü", "MAX(A, B)"),
            ("Fonksiyonlar", "ABS", "Mutlak değer", "ABS(Deger)"),
            ("Fonksiyonlar", "SQRT", "Karekök", "SQRT(Deger)"),
            ("Fonksiyonlar", "INT_TO_REAL", "Tamsayıdan gerçel sayıya", "INT_TO_REAL(Deger)"),
        };

        public PlcWorkspace(Window? owner, string plcName, string program, Func<string, double> read, Func<string> state, Func<double> time, Action<string> save, Action restart)
        {
            _plcName = plcName;
            _read = read;
            _state = state;
            _time = time;
            _save = save;
            _restart = restart;
            Owner = owner;
            Title = Lang.T($"PLC çalışma alanı: {plcName}");
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = Math.Min(1280, SystemParameters.WorkArea.Width - 24);
            Height = Math.Min(820, SystemParameters.WorkArea.Height - 24);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = R<FontFamily>("F.Ui");
            SetResourceReference(ForegroundProperty, "B.Text");

            _editor.Text = program.Replace("\n", Environment.NewLine);
            _editor.FontFamily = R<FontFamily>("F.Mono");
            _editor.FontSize = 13;
            _editor.AcceptsReturn = true;
            _editor.AcceptsTab = true;
            _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _editor.VerticalContentAlignment = VerticalAlignment.Top;
            _editor.TextChanged += (_, _) =>
            {
                _dirty = true;
                Check();
            };

            var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
            root.Children.Add(Head());
            var left = new ScrollViewer { Content = _tree, Width = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(left, Dock.Left);
            root.Children.Add(left);
            var consoleBox = new Border
            {
                Height = 130, Margin = new Thickness(0, 10, 0, 0), BorderBrush = R<Brush>("B.Border"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0),
                Child = new ScrollViewer { Content = _console, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            DockPanel.SetDock(consoleBox, Dock.Bottom);
            root.Children.Add(consoleBox);
            _tabs.Items.Add(new TabItem { Header = Lang.T("Program"), Content = _editor });
            _tabs.Items.Add(new TabItem { Header = Lang.T("Değişkenler"), Content = new ScrollViewer { Content = _table, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4, 8, 4, 4) } });
            _tabs.Items.Add(new TabItem { Header = Lang.T("İzleme"), Content = WatchTab() });
            _tabs.SelectionChanged += (_, e) =>
            {
                if (e.Source == _tabs && _tabs.SelectedIndex == 1) BuildTable();
            };
            root.Children.Add(_tabs);

            Content = new Border
            {
                Margin = new Thickness(20), Background = R<Brush>("B.Overlay"), BorderBrush = R<Brush>("B.BorderStrong"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Child = root,
            };
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");
            Check();
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
            Closed += (_, _) => _timer.Stop();
            Closing += (_, e) =>
            {
                if (!_dirty) return;
                int answer = Dialog.Show(this, Lang.T("Kaydedilmemiş program"), Lang.T("Programdaki değişiklikler PLC'ye kaydedilsin mi?"), Lang.T("Kaydet"), Lang.T("Kaydetme"), Lang.T("Vazgeç"));
                if (answer == 0) Save();
                else if (answer == 2) e.Cancel = true;
            };
        }

        private string Source => _editor.Text.Replace("\r", string.Empty);

        // ---------------------------------------------------------------- head

        private UIElement Head()
        {
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Control) DragMove();
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var save = new Button { Content = Lang.T("Kaydet ve PLC'ye yükle"), Style = R<Style>("Btn.Primary"), Margin = new Thickness(0, 0, 8, 0), ToolTip = Lang.T("Dünya çalışıyorsa program PLC'ye de yüklenir ve soğuk başlar (Ctrl+S)") };
            save.Click += (_, _) => Save();
            var restart = new Button { Content = Lang.T("Yeniden başlat"), Style = R<Style>("Btn.Ghost"), Margin = new Thickness(0, 0, 8, 0), ToolTip = Lang.T("Hatadan sonra PLC'yi STOP'tan RUN'a alır (değişkenler baştan)") };
            restart.Click += (_, _) => _restart();
            var close = new Button { Content = Lang.T("Kapat"), Style = R<Style>("Btn.Ghost"), MinWidth = 90, IsCancel = true };
            close.Click += (_, _) => Close();
            buttons.Children.Add(save);
            buttons.Children.Add(restart);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Right);
            head.Children.Add(buttons);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = Lang.T($"PLC çalışma alanı: {_plcName}"), Style = R<Style>("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(_status);
            head.Children.Add(title);
            DockPanel.SetDock(head, Dock.Top);
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    Save();
                    e.Handled = true;
                }
            };
            return head;
        }

        private void Save()
        {
            _save(Source);
            _dirty = false;
            Log(Lang.T("Kaydedildi."), false, -1);
        }

        // ---------------------------------------------------------------- check, console, project tree

        private void Check()
        {
            var program = StProgram.Compile(Source, out var errors);
            _vars = StDeclarations.Read(Source);
            _console.Children.Clear();
            if (program != null) Log(Lang.T($"Program geçerli: {_vars.Count} değişken, {Pous().Count} blok."), false, -1);
            foreach (var e in errors.Take(50)) Log(e.ToString(), true, e.Line);
            BuildTree();
        }

        private void Log(string text, bool error, int line)
        {
            var row = new TextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = R<FontFamily>("F.Mono"), FontSize = 12, Margin = new Thickness(0, 1, 0, 1),
                Foreground = error ? R<Brush>("B.Danger") : R<Brush>("B.TextSecondary"),
            };
            if (line > 0)
            {
                row.Cursor = Cursors.Hand;
                row.ToolTip = Lang.T("Tıklayın: satıra git");
                row.MouseLeftButtonUp += (_, _) => GoToLine(line);
            }

            _console.Children.Add(row);
        }

        private void GoToLine(int line)
        {
            _tabs.SelectedIndex = 0;
            int index = Math.Max(0, Math.Min(_editor.LineCount - 1, line - 1));
            int start = _editor.GetCharacterIndexFromLineIndex(index);
            _editor.Focus();
            _editor.Select(start, Math.Max(0, _editor.GetLineLength(index) - Environment.NewLine.Length));
            _editor.ScrollToLine(index);
        }

        /// <summary>The program's own function blocks and functions with their lines.</summary>
        private List<(string Kind, string Name, int Line)> Pous()
        {
            var list = new List<(string, string, int)>();
            var lines = Source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"^\s*(FUNCTION_BLOCK|FUNCTION)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase);
                if (m.Success) list.Add((m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value, i + 1));
            }

            return list;
        }

        private void BuildTree()
        {
            _tree.Children.Clear();
            _tree.Children.Add(Overline(Lang.T("Proje"), 0));
            _tree.Children.Add(TreeItem(Lang.T("Program"), Lang.T("Yapısal Metin"), () => _tabs.SelectedIndex = 0));
            _tree.Children.Add(TreeItem(Lang.T("Değişkenler"), _vars.Count.ToString(CultureInfo.CurrentCulture), () => _tabs.SelectedIndex = 1));
            foreach (var (kind, name, line) in Pous())
                _tree.Children.Add(TreeItem("   " + name, kind == "FUNCTION" ? Lang.T("fonksiyon") : Lang.T("fonksiyon bloğu"), () => GoToLine(line)));
            _tree.Children.Add(TreeItem(Lang.T("İzleme"), _watched.Count.ToString(CultureInfo.CurrentCulture), () => _tabs.SelectedIndex = 2));
            _tree.Children.Add(Overline(Lang.T("Kütüphane"), 14));
            _tree.Children.Add(new TextBlock { Text = Lang.T("Tıklayın: çağrı imlecin yerine yazılır (blokların örneği VAR içinde aynı adla tanımlanır)."), Style = R<Style>("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            foreach (var group in Library.GroupBy(l => l.Group))
            {
                _tree.Children.Add(new TextBlock { Text = Lang.T(group.Key), FontWeight = FontWeights.SemiBold, FontSize = TypeScale.Caption, Margin = new Thickness(0, 6, 0, 2), Foreground = R<Brush>("B.TextSecondary") });
                foreach (var item in group)
                {
                    var entry = item;
                    var tile = TreeItem("   " + entry.Name, string.Empty, () => Insert(entry.Call), Lang.T(entry.Help));
                    if (tile is Button b && b.Content is DockPanel dp) foreach (var t in dp.Children.OfType<TextBlock>()) Lang.SetKeep(t, true);   // block names are code
                    _tree.Children.Add(tile);
                }
            }
        }

        private static TextBlock Overline(string text, double top) => new TextBlock { Text = text, Style = R<Style>("Text.Overline"), Margin = new Thickness(0, top, 0, 6) };

        private UIElement TreeItem(string text, string note, Action click, string? tip = null)
        {
            var row = new DockPanel();
            if (note.Length > 0)
            {
                var n = new TextBlock { Text = note, Foreground = R<Brush>("B.TextSecondary"), FontSize = TypeScale.Caption, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(n, Dock.Right);
                row.Children.Add(n);
            }

            row.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = row, Style = R<Style>("Btn.Ghost"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 0, 1), ToolTip = tip };
            b.Click += (_, _) => click();
            return b;
        }

        /// <summary>A library call at the editor's cursor (on a line of its own when the cursor is at a line's end).</summary>
        private void Insert(string call)
        {
            _tabs.SelectedIndex = 0;
            int at = _editor.CaretIndex;
            string text = call;
            if (at > 0 && at <= _editor.Text.Length && _editor.Text[at - 1] != '\n' && call.EndsWith(";", StringComparison.Ordinal)) text = Environment.NewLine + text;
            _editor.Text = _editor.Text.Insert(at, text);
            _editor.CaretIndex = at + text.Length;
            _editor.Focus();
        }

        // ---------------------------------------------------------------- the variable table

        private static readonly string[] Sections = { "VAR", "VAR_GLOBAL", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_TEMP" };

        private static readonly string[] Types = { "BOOL", "INT", "DINT", "UINT", "WORD", "REAL", "LREAL", "TIME", "TON", "TOF", "TP", "CTU", "CTD", "CTUD", "R_TRIG", "F_TRIG", "SR", "RS", "MB_READ", "MB_WRITE" };

        private void BuildTable()
        {
            _vars = StDeclarations.Read(Source);
            _table.Children.Clear();
            _table.RowDefinitions.Clear();
            _table.ColumnDefinitions.Clear();
            _live.Clear();
            foreach (double w in new[] { 110.0, 150, 90, 150, 100, 0, 110, 50, 34 })
                _table.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star), MinWidth = w > 0 ? 0 : 120 });
            void Cell(UIElement e, int row, int col)
            {
                if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 6, 4);
                Grid.SetRow(e, row);
                Grid.SetColumn(e, col);
                _table.Children.Add(e);
            }

            _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] heads = { "Bölüm", "Ad", "Adres", "Tür", "İlk değer", "Açıklama", "Değer", "İzle", "" };
            for (int c = 0; c < heads.Length; c++) Cell(new TextBlock { Text = heads[c].Length > 0 ? Lang.T(heads[c]) : string.Empty, Style = R<Style>("Text.Caption") }, 0, c);
            for (int i = 0; i < _vars.Count; i++)
            {
                var v = _vars[i];
                int row = i + 1;
                _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Cell(Choice(Sections, v.Section, s => Change(() => v.Section = s)), row, 0);
                Cell(Field(v.Name, s =>
                {
                    if (StDeclarations.IsName(s)) Change(() => v.Name = s);
                }), row, 1);
                Cell(Field(v.Location, s => Change(() => v.Location = s.Trim().ToUpperInvariant()), Lang.T("%IX0.0 giriş, %QX0.0 çıkış, %MW0 bellek sözcüğü; boş: adressiz")), row, 2);
                Cell(Choice(Types.Contains(v.Type, StringComparer.OrdinalIgnoreCase) ? Types : Types.Concat(new[] { v.Type }).ToArray(), v.Type, s => Change(() => v.Type = s), editable: true), row, 3);
                Cell(Field(v.Initial, s => Change(() => v.Initial = s.Trim())), row, 4);
                Cell(Field(v.Comment, s => Change(() => v.Comment = s.Trim())), row, 5);
                var value = new TextBlock { FontFamily = R<FontFamily>("F.Mono"), VerticalAlignment = VerticalAlignment.Center, Text = "—" };
                Lang.SetKeep(value, true);   // TRUE / FALSE as the program writes them
                _live.Add((v, value));
                Cell(value, row, 6);
                var watch = new CheckBox { IsChecked = _watched.Contains(v.Name), VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.T("İzleme grafiğinde göster") };
                watch.Click += (_, _) =>
                {
                    if (watch.IsChecked == true) _watched.Add(v.Name);
                    else _watched.Remove(v.Name);
                    _chart.Names = _watched.ToList();
                    BuildTree();
                };
                Cell(watch, row, 7);
                var del = new Button { Content = "×", Style = R<Style>("Btn.Ghost"), Padding = new Thickness(6, 0, 6, 0), ToolTip = Lang.T("Değişkeni sil") };
                del.Click += (_, _) => Change(() => _vars.Remove(v));
                Cell(del, row, 8);
            }

            _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var add = new Button { Content = Lang.T("+ Değişken"), Style = R<Style>("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3) };
            add.Click += (_, _) =>
            {
                string name = "Degisken" + (_vars.Count + 1).ToString(CultureInfo.InvariantCulture);
                Change(() => _vars.Add(new StVariable { Section = "VAR", Name = name, Type = "BOOL" }));
            };
            Cell(add, _vars.Count + 1, 0);
            Grid.SetColumnSpan(add, 3);
            UpdateValues();
        }

        /// <summary>A change in the table goes into the program's text (lines the table cannot hold stay), then the table is drawn again.</summary>
        private void Change(Action change)
        {
            change();
            _editor.Text = StDeclarations.Write(Source, _vars).Replace("\n", Environment.NewLine);
            BuildTable();
        }

        private static TextBox Field(string value, Action<string> commit, string? tip = null)
        {
            var box = new TextBox { Text = value, ToolTip = tip };
            string current = value;
            void Commit()
            {
                if (box.Text == current) return;
                current = box.Text;
                commit(box.Text);
            }

            box.LostKeyboardFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit();
            };
            return box;
        }

        private static ComboBox Choice(string[] items, string value, Action<string> commit, bool editable = false)
        {
            var box = new ComboBox { ItemsSource = items, IsEditable = editable, SelectedItem = items.FirstOrDefault(i => string.Equals(i, value, StringComparison.OrdinalIgnoreCase)) };
            if (editable) box.Text = value;
            Lang.SetKeep(box, true);   // VAR, BOOL, TON: code, not words
            string current = value;
            void Commit(string v)
            {
                v = v.Trim();
                if (v.Length == 0 || string.Equals(v, current, StringComparison.Ordinal)) return;
                current = v;
                commit(v);
            }

            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is string s && box.IsDropDownOpen) Commit(s);
            };
            box.DropDownClosed += (_, _) =>
            {
                if (box.SelectedItem is string s) Commit(s);
            };
            if (editable)
            {
                box.LostKeyboardFocus += (_, _) => Commit(box.Text ?? string.Empty);
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) Commit(box.Text ?? string.Empty);
                };
            }

            return box;
        }

        // ---------------------------------------------------------------- live values and the watch chart

        private UIElement WatchTab()
        {
            var dock = new DockPanel { Margin = new Thickness(4, 8, 4, 4) };
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var span = new ComboBox { ItemsSource = new[] { 5.0, 10, 30, 60 }, SelectedItem = 10.0, Width = 80, ItemStringFormat = "0 s", ToolTip = Lang.T("Grafiğin gösterdiği süre") };
            span.SelectionChanged += (_, _) =>
            {
                if (span.SelectedItem is double s) _chart.Seconds = s;
            };
            DockPanel.SetDock(span, Dock.Right);
            bar.Children.Add(span);
            bar.Children.Add(new TextBlock { Text = Lang.T("Değişkenler sekmesinde İzle kutusu işaretli olanlar, dünya çalışırken son saniyeleriyle (en çok dört)."), Style = R<Style>("Text.Caption"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(_chart);
            return dock;
        }

        private void Tick()
        {
            _status.Text = _state();
            UpdateValues();
            if (_watched.Count > 0)
            {
                _chart.Names = _watched.Take(4).ToList();
                _chart.Add(_time(), _chart.Names.Select(_read).ToArray());
            }
        }

        private void UpdateValues()
        {
            foreach (var (v, text) in _live)
            {
                double x = _read(v.Name);
                text.Text = double.IsNaN(x) ? "—" : string.Equals(v.Type, "BOOL", StringComparison.OrdinalIgnoreCase) ? (x != 0 ? "TRUE" : "FALSE") : x.ToString("0.###", CultureInfo.CurrentCulture);
            }
        }

        /// <summary>--snapshot: a tab of the workspace in view ("program", "vars", "watch").</summary>
        internal void ShowForSnapshot(string tab, IEnumerable<string> watch)
        {
            foreach (string w in watch) _watched.Add(w);
            _chart.Names = _watched.ToList();
            _tabs.SelectedIndex = tab == "vars" ? 1 : tab == "watch" ? 2 : 0;
            if (_tabs.SelectedIndex == 1) BuildTable();
            BuildTree();
            Tick();
        }

    }
}
