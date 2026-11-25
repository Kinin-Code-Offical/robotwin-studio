using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using RobotTwin.CoreSim.Industrial;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [plc] The ladder editor: the PLC's variables on the left, its rungs as grids in the middle (seven columns of contacts and
    /// blocks, the coils on the right, rows for parallel branches joined by vertical links), the selected cell's operand
    /// below. The program is checked as it is drawn; while the world runs, the contacts and coils that pass power are lit.
    /// </summary>
    public sealed class LadderWindow : Window
    {
        private const int LogicColumns = 7, CoilColumn = 7;
        private const double CellW = 96, CellH = 56, Rail = 14;

        private readonly LdProgram _ladder;
        private readonly Func<string, double>? _live;   // a running PLC's variable values, by name
        private readonly Action<LdProgram> _save;
        private readonly TextBox _declarations;
        private readonly StackPanel _rungs = new StackPanel();
        private readonly TextBlock _check = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBox _operand = new TextBox { Width = 150 };
        private readonly TextBox _param = new TextBox { Width = 110 };
        private readonly TextBox _reset = new TextBox { Width = 110 };
        private readonly TextBlock _selectedText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        private readonly TextBox _generated = new TextBox { IsReadOnly = true, AcceptsReturn = true, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        private (int Rung, int Row, int Col)? _selected;
        private bool _loading;

        public LadderWindow(LdProgram ladder, Action<LdProgram> save, Func<string, double>? live, string title)
        {
            _ladder = ladder;
            _save = save;
            _live = live;
            Title = "Merdiven diyagramı — " + title;
            Width = 1180;
            Height = 820;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Res("B.Surface");
            Foreground = Res("B.Text");
            if (_ladder.Rungs.Count == 0) _ladder.Rungs.Add(new LdRung());

            var root = new DockPanel { Background = Res("B.Surface") };

            // Toolbar: the elements, placed on the selected cell.
            var tools = new WrapPanel { Margin = new Thickness(10, 8, 10, 4) };
            void Tool(string text, string tip, Action click)
            {
                var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 4), FontFamily = Mono() };
                b.Click += (_, _) => click();
                tools.Children.Add(b);
            }

            Tool("─[ ]─", "Normalde açık kontak: değişkeni TRUE iken güç geçer", () => Place(LdKind.Contact));
            Tool("─[/]─", "Normalde kapalı kontak: değişkeni FALSE iken güç geçer", () => Place(LdKind.ContactNot));
            Tool("─[P]─", "Yükselen kenar: değişken TRUE olduğu tarama boyunca geçer", () => Place(LdKind.ContactRise));
            Tool("─[N]─", "Düşen kenar: değişken FALSE olduğu tarama boyunca geçer", () => Place(LdKind.ContactFall));
            Tool("[ ≥ ]", "Karşılaştırma: 'Seviye' ve '>= 50' gibi bir koşul tutarken geçer", () => Place(LdKind.Compare));
            Tool("TON", "Gecikmeli açan zamanlayıcı: güç PT süresi sürünce çıkış açılır", () => Place(LdKind.Ton));
            Tool("TOF", "Gecikmeli kapanan zamanlayıcı", () => Place(LdKind.Tof));
            Tool("TP", "Darbe: güç gelince PT süresince açık", () => Place(LdKind.Tp));
            Tool("CTU", "Yukarı sayıcı: her güç gelişinde sayar, PV'ye varınca çıkış açılır; R ile sıfırlanır", () => Place(LdKind.Ctu));
            Tool("───", "Tel: gücü olduğu gibi geçirir", () => Place(LdKind.Wire));
            Tool("─( )─", "Bobin: değişken basamağın gücünü alır", () => Place(LdKind.Coil));
            Tool("─(/)─", "Ters bobin", () => Place(LdKind.CoilNot));
            Tool("─(S)─", "Set bobini: güç gelince TRUE olur ve kalır", () => Place(LdKind.Set));
            Tool("─(R)─", "Reset bobini: güç gelince FALSE olur", () => Place(LdKind.Reset));
            Tool("│ bağ", "Seçili hücrenin sağından alt satıra dikey bağ (paralel kol): aç / kapat", ToggleLink);
            Tool("Sil", "Seçili hücreyi boşaltır", () => Place(LdKind.Empty));
            DockPanel.SetDock(tools, Dock.Top);
            root.Children.Add(tools);

            // Selected cell's operand.
            var props = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 6) };
            props.Children.Add(_selectedText);
            props.Children.Add(Label("Değişken"));
            props.Children.Add(_operand);
            props.Children.Add(Label("Parametre"));
            props.Children.Add(_param);
            props.Children.Add(Label("Sıfırla (CTU)"));
            props.Children.Add(_reset);
            _operand.TextChanged += (_, _) => Edit(c => c.Operand = _operand.Text.Trim());
            _param.TextChanged += (_, _) => Edit(c => c.Param = _param.Text.Trim());
            _reset.TextChanged += (_, _) => Edit(c => c.Reset = _reset.Text.Trim());
            DockPanel.SetDock(props, Dock.Top);
            root.Children.Add(props);

            // Bottom: check, generated text, save.
            var bottom = new StackPanel { Margin = new Thickness(10, 4, 10, 10) };
            bottom.Children.Add(_check);
            bottom.Children.Add(new Expander { Header = "Oluşan Yapısal Metin", Content = _generated, Margin = new Thickness(0, 6, 0, 0) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var saveButton = new Button { Content = "Kaydet ve PLC'ye yükle", Padding = new Thickness(12, 5, 12, 5), Style = (Style)Application.Current.FindResource("Btn.Primary") };
            saveButton.Click += (_, _) =>
            {
                _ladder.Declarations = _declarations!.Text.Replace("\r", string.Empty);
                _save(_ladder);
            };
            var close = new Button { Content = "Kapat", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6, 0, 0, 0) };
            close.Click += (_, _) => Close();
            buttons.Children.Add(saveButton);
            buttons.Children.Add(close);
            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            // Left: declarations.
            var left = new DockPanel { Width = 300, Margin = new Thickness(10, 0, 6, 0) };
            var leftTitle = new TextBlock { Text = "Değişkenler (VAR … END_VAR)", Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(leftTitle, Dock.Top);
            left.Children.Add(leftTitle);
            _declarations = new TextBox
            {
                Text = (_ladder.Declarations.Length > 0 ? _ladder.Declarations : "VAR\n    Start AT %IX0.0 : BOOL;\n    Stop AT %IX0.1 : BOOL;\n    Motor AT %QX0.0 : BOOL;\nEND_VAR").Replace("\n", Environment.NewLine),
                AcceptsReturn = true,
                FontFamily = Mono(),
                FontSize = 12,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            _declarations.TextChanged += (_, _) =>
            {
                _ladder.Declarations = _declarations.Text.Replace("\r", string.Empty);
                Check();
            };
            left.Children.Add(_declarations);
            DockPanel.SetDock(left, Dock.Left);
            root.Children.Add(left);

            // Middle: the rungs.
            var middle = new DockPanel();
            var addRung = new Button { Content = "+ Basamak", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            addRung.Click += (_, _) =>
            {
                _ladder.Rungs.Add(new LdRung());
                Redraw();
            };
            DockPanel.SetDock(addRung, Dock.Bottom);
            middle.Children.Add(addRung);
            middle.Children.Add(new ScrollViewer { Content = _rungs, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            root.Children.Add(middle);

            Content = root;
            Redraw();
            if (_live != null)
            {
                _timer.Tick += (_, _) => Redraw(keepFocus: true);
                _timer.Start();
                Closed += (_, _) => _timer.Stop();
            }
        }

        private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        private static FontFamily Mono() => (FontFamily)Application.Current.FindResource("F.Mono");

        private static TextBlock Label(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };

        private LdCell? Selected => _selected is { } s && s.Rung < _ladder.Rungs.Count ? _ladder.Rungs[s.Rung].At(s.Row, s.Col) : null;

        private void Edit(Action<LdCell> change)
        {
            if (_loading || Selected is not { } cell) return;
            change(cell);
            Redraw(keepFocus: true);
        }

        /// <summary>Puts an element on the selected cell: a coil goes to the coil column, the rest to the logic columns.</summary>
        private void Place(LdKind kind)
        {
            if (_selected is not { } s) return;
            var rung = _ladder.Rungs[s.Rung];
            bool coil = kind is LdKind.Coil or LdKind.CoilNot or LdKind.Set or LdKind.Reset;
            int col = coil ? CoilColumn : Math.Min(s.Col, LogicColumns - 1);
            var cell = rung.At(s.Row, col);
            if (kind == LdKind.Empty)
            {
                if (cell != null) rung.Cells.Remove(cell);
            }
            else
            {
                if (cell == null)
                {
                    cell = new LdCell { Row = s.Row, Col = col };
                    rung.Cells.Add(cell);
                }

                cell.Kind = kind;
                // A coil at the row's end: the empty cells before it become wire, as an editor draws them.
                if (coil)
                {
                    int lastLogic = rung.Cells.Where(c => c.Row == s.Row && c.Col < CoilColumn && c.Kind != LdKind.Empty).Select(c => c.Col).DefaultIfEmpty(-1).Max();
                    for (int c = lastLogic + 1; c < CoilColumn; c++)
                        if (rung.At(s.Row, c) == null && lastLogic >= 0) rung.Cells.Add(new LdCell { Row = s.Row, Col = c, Kind = LdKind.Wire });
                }
            }

            _selected = (s.Rung, s.Row, col);
            Redraw();
        }

        private void ToggleLink()
        {
            if (Selected is { } cell)
            {
                cell.LinkDown = !cell.LinkDown;
                var rung = _ladder.Rungs[_selected!.Value.Rung];
                if (cell.LinkDown && rung.Rows <= cell.Row + 1) rung.Cells.Add(new LdCell { Row = cell.Row + 1, Col = 0, Kind = LdKind.Empty });   // make room for the branch
                Redraw();
            }
            else if (_selected is { } s)
            {
                _ladder.Rungs[s.Rung].Cells.Add(new LdCell { Row = s.Row, Col = s.Col, Kind = LdKind.Wire, LinkDown = true });
                Redraw();
            }
        }

        private void Check()
        {
            string st = _ladder.ToStructuredText(out var problems);
            _generated.Text = st.Replace("\n", Environment.NewLine);
            var program = problems.Count == 0 ? StProgram.Compile(st, out problems) : null;
            _check.Text = program != null ? $"Merdiven geçerli: {_ladder.Rungs.Count} basamak." : string.Join("\n", problems.Take(4).Select(p => p.ToString()));
            _check.Foreground = Res(program != null ? "B.Success" : "B.Danger");
        }

        private void Redraw(bool keepFocus = false)
        {
            _rungs.Children.Clear();
            for (int k = 0; k < _ladder.Rungs.Count; k++) _rungs.Children.Add(RungView(k));
            if (!keepFocus) Check();
            _loading = true;
            var cell = Selected;
            _selectedText.Text = _selected is { } s ? $"Basamak {s.Rung + 1} · satır {s.Row + 1} · sütun {s.Col + 1}" : "Bir hücre seçin";
            if (!_operand.IsKeyboardFocusWithin) _operand.Text = cell?.Operand ?? string.Empty;
            if (!_param.IsKeyboardFocusWithin) _param.Text = cell?.Param ?? string.Empty;
            if (!_reset.IsKeyboardFocusWithin) _reset.Text = cell?.Reset ?? string.Empty;
            _loading = false;
            if (keepFocus) Check();
        }

        private FrameworkElement RungView(int k)
        {
            var rung = _ladder.Rungs[k];
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            head.Children.Add(new TextBlock { Text = $"Basamak {k + 1}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var comment = new TextBox { Text = rung.Comment, Width = 320, ToolTip = "Basamağın açıklaması" };
            comment.TextChanged += (_, _) => rung.Comment = comment.Text;
            head.Children.Add(comment);
            void HeadButton(string text, string tip, Action click)
            {
                var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(4, 0, 0, 0) };
                b.Click += (_, _) =>
                {
                    click();
                    Redraw();
                };
                head.Children.Add(b);
            }

            HeadButton("+ Satır", "Paralel kol için bir satır ekler", () => rung.Cells.Add(new LdCell { Row = rung.Rows, Col = 0, Kind = LdKind.Empty }));
            HeadButton("↑", "Basamağı yukarı taşır", () =>
            {
                if (k == 0) return;
                _ladder.Rungs.RemoveAt(k);
                _ladder.Rungs.Insert(k - 1, rung);
                _selected = null;
            });
            HeadButton("↓", "Basamağı aşağı taşır", () =>
            {
                if (k >= _ladder.Rungs.Count - 1) return;
                _ladder.Rungs.RemoveAt(k);
                _ladder.Rungs.Insert(k + 1, rung);
                _selected = null;
            });
            HeadButton("Sil", "Basamağı siler", () =>
            {
                _ladder.Rungs.RemoveAt(k);
                _selected = null;
            });
            panel.Children.Add(head);

            int rows = Math.Max(1, rung.Rows);
            double width = Rail * 2 + CellW * (CoilColumn + 1), height = CellH * rows;
            var canvas = new Canvas { Width = width, Height = height, Background = Res("B.Raised"), ClipToBounds = true };
            var line = Res("B.TextSecondary");
            var lit = new SolidColorBrush(Color.FromRgb(0x3C, 0xD0, 0x64));
            canvas.Children.Add(new Rectangle { Width = 3, Height = height, Fill = Res("B.Text") });
            var right = new Rectangle { Width = 3, Height = height, Fill = Res("B.Text") };
            Canvas.SetLeft(right, width - 3);
            canvas.Children.Add(right);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c <= CoilColumn; c++)
                {
                    double x = Rail + c * CellW, y = r * CellH;
                    var cell = rung.At(r, c);
                    bool selected = _selected is { } s && s.Rung == k && s.Row == r && s.Col == c;
                    var hit = new Border
                    {
                        Width = CellW,
                        Height = CellH,
                        Background = Brushes.Transparent,
                        BorderBrush = selected ? Res("B.Accent") : Brushes.Transparent,
                        BorderThickness = new Thickness(selected ? 2 : 0),
                        Cursor = Cursors.Hand,
                        ToolTip = c == CoilColumn ? "Bobin sütunu" : null
                    };
                    int rr = r, cc = c;
                    hit.MouseLeftButtonDown += (_, _) =>
                    {
                        _selected = (k, rr, cc);
                        Redraw(keepFocus: true);
                    };
                    if (cell != null) Draw(canvas, cell, x, y, Passes(cell) ? lit : line);
                    if (cell?.LinkDown == true)
                    {
                        var link = new Line { X1 = x + CellW, Y1 = y + CellH / 2, X2 = x + CellW, Y2 = y + CellH * 1.5, Stroke = line, StrokeThickness = 2 };
                        canvas.Children.Add(link);
                    }

                    Canvas.SetLeft(hit, x);
                    Canvas.SetTop(hit, y);
                    canvas.Children.Add(hit);
                }

                // The rail's connection to the row's first cell, and the last coil's to the right rail.
                if (rung.At(r, 0) is { Kind: not LdKind.Empty })
                    canvas.Children.Add(new Line { X1 = 3, Y1 = r * CellH + CellH / 2, X2 = Rail, Y2 = r * CellH + CellH / 2, Stroke = line, StrokeThickness = 2 });
                if (rung.At(r, CoilColumn) is { Kind: not LdKind.Empty })
                    canvas.Children.Add(new Line { X1 = Rail + (CoilColumn + 1) * CellW, Y1 = r * CellH + CellH / 2, X2 = width - 3, Y2 = r * CellH + CellH / 2, Stroke = line, StrokeThickness = 2 });
            }

            panel.Children.Add(canvas);
            return panel;
        }

        /// <summary>While the world runs: a contact that passes, a block whose output is on, a coil that is TRUE.</summary>
        private bool Passes(LdCell cell)
        {
            if (_live == null || cell.Operand.Length == 0) return false;
            double v = _live(cell.Kind is LdKind.Ton or LdKind.Tof or LdKind.Tp or LdKind.Ctu ? cell.Operand + ".Q" : cell.Operand);
            if (double.IsNaN(v)) return false;
            return cell.Kind switch
            {
                LdKind.ContactNot => v == 0,
                LdKind.CoilNot => v == 0,
                LdKind.Wire or LdKind.Empty or LdKind.Compare or LdKind.ContactRise or LdKind.ContactFall => false,
                _ => v != 0
            };
        }

        private static void Draw(Canvas canvas, LdCell cell, double x, double y, Brush stroke)
        {
            double mid = y + CellH / 2;
            void Wire(double x1, double x2) => canvas.Children.Add(new Line { X1 = x1, Y1 = mid, X2 = x2, Y2 = mid, Stroke = stroke, StrokeThickness = 2 });
            void Text(string text, double cx, double cy, double size = 11, FontWeight? weight = null)
            {
                var t = new TextBlock { Text = text, FontSize = size, Foreground = stroke, FontFamily = Mono(), FontWeight = weight ?? FontWeights.Normal };
                t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(t, cx - t.DesiredSize.Width / 2);
                Canvas.SetTop(t, cy - t.DesiredSize.Height / 2);
                canvas.Children.Add(t);
            }

            double cx0 = x + CellW / 2;
            switch (cell.Kind)
            {
                case LdKind.Empty:
                    return;
                case LdKind.Wire:
                    Wire(x, x + CellW);
                    return;
                case LdKind.Contact:
                case LdKind.ContactNot:
                case LdKind.ContactRise:
                case LdKind.ContactFall:
                    Wire(x, cx0 - 9);
                    Wire(cx0 + 9, x + CellW);
                    canvas.Children.Add(new Line { X1 = cx0 - 9, Y1 = mid - 11, X2 = cx0 - 9, Y2 = mid + 11, Stroke = stroke, StrokeThickness = 2 });
                    canvas.Children.Add(new Line { X1 = cx0 + 9, Y1 = mid - 11, X2 = cx0 + 9, Y2 = mid + 11, Stroke = stroke, StrokeThickness = 2 });
                    if (cell.Kind == LdKind.ContactNot) canvas.Children.Add(new Line { X1 = cx0 - 7, Y1 = mid + 9, X2 = cx0 + 7, Y2 = mid - 9, Stroke = stroke, StrokeThickness = 2 });
                    if (cell.Kind == LdKind.ContactRise) Text("P", cx0, mid);
                    if (cell.Kind == LdKind.ContactFall) Text("N", cx0, mid);
                    Text(cell.Operand, cx0, y + 9);
                    return;
                case LdKind.Coil:
                case LdKind.CoilNot:
                case LdKind.Set:
                case LdKind.Reset:
                    Wire(x, cx0 - 12);
                    Wire(cx0 + 12, x + CellW);
                    canvas.Children.Add(new Ellipse { Width = 24, Height = 24, Stroke = stroke, StrokeThickness = 2 });
                    Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], cx0 - 12);
                    Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], mid - 12);
                    Text(cell.Kind switch { LdKind.CoilNot => "/", LdKind.Set => "S", LdKind.Reset => "R", _ => string.Empty }, cx0, mid);
                    Text(cell.Operand, cx0, y + 9);
                    return;
                default:
                {
                    // A block (or a comparison): a box with its type, instance and parameter.
                    Wire(x, x + 14);
                    Wire(x + CellW - 14, x + CellW);
                    var box = new Rectangle { Width = CellW - 28, Height = CellH - 12, Stroke = stroke, StrokeThickness = 2 };
                    Canvas.SetLeft(box, x + 14);
                    Canvas.SetTop(box, y + 6);
                    canvas.Children.Add(box);
                    string type = cell.Kind switch { LdKind.Ton => "TON", LdKind.Tof => "TOF", LdKind.Tp => "TP", LdKind.Ctu => "CTU", _ => "CMP" };
                    Text(type, cx0, y + 16, 10, FontWeights.SemiBold);
                    Text(cell.Kind == LdKind.Compare ? cell.Operand + " " + cell.Param : cell.Operand, cx0, mid + 2, 10);
                    if (cell.Kind != LdKind.Compare) Text(cell.Param, cx0, y + CellH - 14, 10);
                    return;
                }
            }
        }
    }
}
