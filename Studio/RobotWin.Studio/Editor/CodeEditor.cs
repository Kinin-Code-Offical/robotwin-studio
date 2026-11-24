using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RobotWin.Studio.Editor
{
    /// <summary>A compiler message attached to a line of the source.</summary>
    public sealed class CodeDiagnostic
    {
        public CodeDiagnostic(int line, int column, bool isError, string message, string? file = null)
        {
            Line = line;
            Column = column;
            IsError = isError;
            Message = message;
            File = string.IsNullOrEmpty(file) ? "sketch.ino" : file!;
        }

        /// <summary>[code] The sketch file it is about ("sketch.ino" is the main one, "src/motor.cpp" …).</summary>
        public string File { get; }

        /// <summary>1-based line.</summary>
        public int Line { get; }

        public int Column { get; }
        public bool IsError { get; }
        public string Message { get; }
        public override string ToString() => $"{(IsError ? "Hata" : "Uyarı")} · {(File == "sketch.ino" ? string.Empty : File + " · ")}satır {Line}: {Message}";
    }

    /// <summary>
    /// Arduino/C++ source editor: a plain TextBox for editing (caret, selection,
    /// undo, clipboard, IME) with its glyphs made transparent, over a layer that
    /// draws the same text with syntax colours, plus a line-number gutter with
    /// error markers. Monospaced font so columns line up exactly.
    /// </summary>
    public sealed partial class CodeEditor : Grid
    {
        private readonly TextBox _box;
        private readonly HighlightLayer _layer;
        private readonly Gutter _gutter;
        private List<CodeDiagnostic> _diagnostics = new List<CodeDiagnostic>();

        public CodeEditor()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            ColumnDefinitions.Add(new ColumnDefinition());
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10));

            _box = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = false,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 13.5,
                Foreground = Brushes.Transparent,
                CaretBrush = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xEF)),
                SelectionBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0x9B, 0xFF)),
                SelectionOpacity = 0.3,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 6, 6, 6),
                // The theme centres single-line inputs vertically; code starts at the top.
                VerticalContentAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsUndoEnabled = true,
                UndoLimit = 500
            };
            _box.Template = PlainTemplate();            _layer = new HighlightLayer(this) { IsHitTestVisible = false };
            _gutter = new Gutter(this);
            SetColumn(_layer, 1);
            SetColumn(_box, 1);
            Children.Add(_gutter);
            Children.Add(_layer);
            Children.Add(_box);

            _box.TextChanged += (_, _) =>
            {
                Redraw();
                TextChanged?.Invoke(this, EventArgs.Empty);
            };
            _box.SelectionChanged += (_, _) => Redraw();
            _box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => Redraw()));
            _box.PreviewKeyDown += OnKey;
            _box.SizeChanged += (_, _) => Redraw();
            InitCompletion();   // [code] Editor/CodeEditor.Completion.cs
        }

        public event EventHandler? TextChanged;

        // ------------------------------------------------------------------ [debug] breakpoints and the stopped line

        private List<(int Line, bool Active)> _breakpoints = new List<(int, bool)>();
        private int _executionLine;

        /// <summary>A click in the line-number gutter (1-based line): the code page toggles a breakpoint there.</summary>
        public event Action<int>? GutterClicked;

        /// <summary>Breakpoint marks (1-based lines); an inactive one (no code there) is drawn hollow.</summary>
        public void SetBreakpoints(IEnumerable<(int Line, bool Active)> marks)
        {
            _breakpoints = marks.ToList();
            _gutter.InvalidateVisual();
        }

        /// <summary>The line a stopped program is at (1-based; 0 = none), marked with an arrow and a band.</summary>
        public int ExecutionLine
        {
            get => _executionLine;
            set
            {
                if (_executionLine == value) return;
                _executionLine = value;
                Redraw();
            }
        }

        /// <summary>Scrolls a line into view without moving the caret.</summary>
        public void RevealLine(int line)
        {
            if (line < 1) return;
            int count = _box.LineCount;
            if (count <= 0) return;
            int first = _box.GetFirstVisibleLineIndex(), last = _box.GetLastVisibleLineIndex();
            int index = Math.Min(count - 1, line - 1);
            if (first >= 0 && index >= first + 2 && index <= last - 2) return;
            _box.ScrollToLine(Math.Max(0, index - 6));
            Redraw();
        }

        public string Text
        {
            get => _box.Text;
            set
            {
                _box.Text = value ?? string.Empty;
                _box.CaretIndex = 0;
                _box.ScrollToHome();
            }
        }

        public bool IsReadOnly
        {
            get => _box.IsReadOnly;
            set => _box.IsReadOnly = value;
        }

        internal TextBox Box => _box;

        public IReadOnlyList<CodeDiagnostic> Diagnostics
        {
            get => _diagnostics;
            set
            {
                _diagnostics = value?.ToList() ?? new List<CodeDiagnostic>();
                Redraw();
            }
        }

        /// <summary>1-based caret line, counted from the text itself (valid before the TextBox has laid out).</summary>
        public int CaretLine
        {
            get
            {
                string text = _box.Text;
                int end = Math.Min(_box.CaretIndex, text.Length), line = 1;
                for (int i = 0; i < end; i++)
                {
                    if (text[i] == '\n') line++;
                }

                return line;
            }
        }

        public int CaretColumn
        {
            get
            {
                string text = _box.Text;
                int caret = Math.Min(_box.CaretIndex, text.Length);
                int lineStart = caret > 0 ? text.LastIndexOf('\n', caret - 1) + 1 : 0;
                return caret - lineStart + 1;
            }
        }

        public new void Focus() => _box.Focus();

        public void GoTo(int line, int column = 1)
        {
            line = Math.Max(1, Math.Min(line, Math.Max(1, _box.LineCount)));
            int start = _box.GetCharacterIndexFromLineIndex(line - 1);
            if (start < 0) return;
            int len = _box.GetLineLength(line - 1);
            _box.Focus();
            _box.CaretIndex = start + Math.Max(0, Math.Min(column - 1, len));
            double lh = _box.FontFamily.LineSpacing * _box.FontSize;
            _box.ScrollToVerticalOffset(Math.Max(0, (line - 1) * lh - _box.ActualHeight / 3));
            Redraw();
        }

        private bool _redrawQueued;

        /// <summary>Repaints after the TextBox has finished its own layout (line metrics are then valid).</summary>
        private void Redraw()
        {
            if (_redrawQueued) return;
            _redrawQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _redrawQueued = false;
                _lines = null;
                _layer.InvalidateVisual();
                _gutter.InvalidateVisual();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private string[]? _lines;

        /// <summary>Source split into lines (cached until the next edit).</summary>
        internal string[] Lines => _lines ??= _box.Text.Split('\n');

        private static ControlTemplate PlainTemplate()
        {
            var t = new ControlTemplate(typeof(TextBox));
            var sv = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            sv.SetValue(ScrollViewer.FocusVisualStyleProperty, null);
            sv.SetValue(Control.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            t.VisualTree = sv;
            return t;
        }

        // ------------------------------------------------------------------ editing helpers

        private void OnKey(object sender, KeyEventArgs e)
        {
            var box = _box;
            if (CompletionKey(e)) return;   // [code] the completion list takes arrows, Enter, Tab, Esc while it is open
            if (e.Key == Key.Tab)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift) Outdent();
                else if (box.SelectionLength > 0 && box.SelectedText.Contains('\n')) IndentSelection();
                else box.SelectedText = "  ";
                if (box.SelectionLength > 0 && !box.SelectedText.Contains('\n')) box.Select(box.SelectionStart + box.SelectionLength, 0);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                int line = box.GetLineIndexFromCharacterIndex(box.CaretIndex);
                int start = box.GetCharacterIndexFromLineIndex(line);
                string before = box.Text.Substring(start, box.CaretIndex - start);
                string indent = new string(before.TakeWhile(c => c == ' ' || c == '\t').ToArray());
                if (before.TrimEnd().EndsWith("{", StringComparison.Ordinal)) indent += "  ";
                Insert("\n" + indent);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.OemCloseBrackets && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && box.SelectionLength == 0)
            {
                // '}' on a whitespace-only line pulls the indentation back one level.
                int line = box.GetLineIndexFromCharacterIndex(box.CaretIndex);
                int start = box.GetCharacterIndexFromLineIndex(line);
                string before = box.Text.Substring(start, box.CaretIndex - start);
                if (before.Length >= 2 && before.Trim().Length == 0)
                {
                    box.Select(box.CaretIndex - 2, 2);
                    box.SelectedText = "}";
                    box.Select(box.SelectionStart + 1, 0);
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.OemQuestion && Keyboard.Modifiers == ModifierKeys.Control || e.Key == Key.Divide && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleComment();
                e.Handled = true;
            }
        }

        private void Insert(string text)
        {
            int at = _box.SelectionStart;
            _box.SelectedText = text;
            _box.Select(at + text.Length, 0);
        }

        private (int First, int Last) SelectedLines()
        {
            int a = _box.GetLineIndexFromCharacterIndex(_box.SelectionStart);
            int endChar = Math.Max(_box.SelectionStart, _box.SelectionStart + _box.SelectionLength - 1);
            int b = _box.GetLineIndexFromCharacterIndex(endChar);
            return (a, Math.Max(a, b));
        }

        private void EditLines(Func<string, string> change)
        {
            var (first, last) = SelectedLines();
            int start = _box.GetCharacterIndexFromLineIndex(first);
            int end = _box.GetCharacterIndexFromLineIndex(last) + _box.GetLineLength(last);
            string block = _box.Text.Substring(start, end - start);
            var lines = block.Split('\n');
            string result = string.Join("\n", lines.Select(l => l.Length == 0 || l == "\r" ? l : change(l)));
            _box.Select(start, end - start);
            _box.SelectedText = result;
            _box.Select(start, result.Length);
        }

        private void IndentSelection() => EditLines(l => "  " + l);

        private void Outdent() => EditLines(l => l.StartsWith("  ", StringComparison.Ordinal) ? l.Substring(2) : l.TrimStart(' '));

        private void ToggleComment()
        {
            var (first, last) = SelectedLines();
            bool allCommented = true;
            for (int i = first; i <= last; i++)
            {
                string line = _box.GetLineText(i);
                if (line.Trim().Length > 0 && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)) allCommented = false;
            }

            if (_box.SelectionLength == 0)
            {
                int start = _box.GetCharacterIndexFromLineIndex(first);
                _box.Select(start, _box.GetLineLength(first));
            }

            EditLines(l =>
            {
                if (!allCommented) return "// " + l;
                int i = l.IndexOf("//", StringComparison.Ordinal);
                if (i < 0) return l;
                int cut = i + 2 < l.Length && l[i + 2] == ' ' ? 3 : 2;
                return l.Remove(i, cut);
            });
        }

        // ------------------------------------------------------------------ layout helpers

        /// <summary>
        /// Where line 0 starts (may be above the view when scrolled), the line height
        /// and the character advance, in TextBox coordinates.
        /// </summary>
        internal (Point Origin, double LineHeight, double CharWidth, int First, int Last) Metrics()
        {
            var tf = new Typeface(_box.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, _box.FontSize, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double lh = _box.FontFamily.LineSpacing * _box.FontSize;
            int lineCount = Lines.Length;
            int first = Math.Max(0, _box.GetFirstVisibleLineIndex());
            first = Math.Min(first, Math.Max(0, lineCount - 1));
            Point origin;
            int firstChar = first < _box.LineCount ? _box.GetCharacterIndexFromLineIndex(first) : -1;
            var r = firstChar >= 0 ? _box.GetRectFromCharacterIndex(firstChar) : Rect.Empty;
            if (!r.IsEmpty && !double.IsInfinity(r.Top) && !double.IsNaN(r.Top))
            {
                origin = new Point(r.Left, r.Top - first * lh);
            }
            else
            {
                origin = new Point(_box.Padding.Left + 2 - _box.HorizontalOffset, _box.Padding.Top - _box.VerticalOffset);
            }

            int visible = (int)Math.Ceiling(Math.Max(0, _box.ActualHeight) / lh) + 1;
            int last = Math.Min(lineCount - 1, first + visible);
            return (origin, lh, ft.WidthIncludingTrailingWhitespace, first, last);
        }

        // ------------------------------------------------------------------ syntax colouring

        private sealed class HighlightLayer : FrameworkElement
        {
            private readonly CodeEditor _owner;

            public HighlightLayer(CodeEditor owner)
            {
                _owner = owner;
            }

            protected override void OnRender(DrawingContext dc)
            {
                var box = _owner._box;
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
                if (box.ActualWidth <= 0)
                {
                    dc.Pop();
                    return;
                }

                var (origin, lh, cw, first, last) = _owner.Metrics();
                var lines = _owner.Lines;

                // Current line and error lines
                int caretLine = box.GetLineIndexFromCharacterIndex(box.CaretIndex);
                if (caretLine >= first && caretLine <= last && box.SelectionLength == 0)
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1A)), null, new Rect(0, origin.Y + caretLine * lh, ActualWidth, lh));
                }

                // [debug] the line a stopped program is about to run
                int exec = _owner._executionLine - 1;
                if (exec >= first && exec <= last)
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x3C, 0xF2, 0xC1, 0x4B)), null, new Rect(0, origin.Y + exec * lh, ActualWidth, lh));
                }

                foreach (var d in _owner._diagnostics)
                {
                    int li = d.Line - 1;
                    if (li < first || li > last) continue;
                    var color = d.IsError ? Color.FromArgb(0x30, 0xE5, 0x48, 0x4D) : Color.FromArgb(0x26, 0xD4, 0xA8, 0x4B);
                    dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, origin.Y + li * lh, ActualWidth, lh));
                }

                var typeface = new Typeface(box.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                int firstIndex = 0;
                for (int k = 0; k < first && k < lines.Length; k++) firstIndex += lines[k].Length + 1;
                bool inBlock = CppLexer.BlockCommentStateBefore(box.Text, firstIndex);
                for (int i = first; i <= last && i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (line.Length == 0) continue;
                    var ft = new FormattedText(line, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, box.FontSize, CppLexer.Default, dip);
                    foreach (var (start, length, brush) in CppLexer.Tokens(line, ref inBlock)) ft.SetForegroundBrush(brush, start, length);
                    dc.DrawText(ft, new Point(origin.X, origin.Y + i * lh + (lh - ft.Height) / 2));
                }

                // Squiggle under the reported column
                foreach (var d in _owner._diagnostics.Where(x => x.Column > 0))
                {
                    int li = d.Line - 1;
                    if (li < first || li > last) continue;
                    double x0 = origin.X + (d.Column - 1) * cw, y = origin.Y + (li + 1) * lh - 2;
                    var pen = new Pen(new SolidColorBrush(d.IsError ? Color.FromRgb(0xE5, 0x48, 0x4D) : Color.FromRgb(0xD4, 0xA8, 0x4B)), 1);
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(new Point(x0, y), false, false);
                        for (int k = 1; k <= 12; k++) g.LineTo(new Point(x0 + k * 2.5, y + (k % 2 == 0 ? 0 : 2)), true, false);
                    }

                    dc.DrawGeometry(null, pen, geo);
                }

                dc.Pop();
            }
        }

        private sealed class Gutter : FrameworkElement
        {
            private readonly CodeEditor _owner;

            public Gutter(CodeEditor owner)
            {
                _owner = owner;
                ClipToBounds = true;
                Cursor = System.Windows.Input.Cursors.Hand;
                ToolTip = "Kesme noktası koymak ya da kaldırmak için tıklayın: kart bu satıra gelince devre, dünya ve kod aynı anda durur";
            }

            protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
            {
                base.OnMouseLeftButtonDown(e);
                var (origin, lh, _, _, _) = _owner.Metrics();
                if (lh <= 0) return;
                int line = (int)Math.Floor((e.GetPosition(this).Y - origin.Y) / lh) + 1;
                if (line < 1 || line > _owner.Lines.Length) return;
                _owner.GutterClicked?.Invoke(line);
                e.Handled = true;
            }

            protected override void OnRender(DrawingContext dc)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10)), null, new Rect(0, 0, ActualWidth, ActualHeight));
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x28)), 1), new Point(ActualWidth - 0.5, 0), new Point(ActualWidth - 0.5, ActualHeight));
                var box = _owner._box;
                var (origin, lh, _, first, last) = _owner.Metrics();
                int caret = box.GetLineIndexFromCharacterIndex(box.CaretIndex);
                var tf = new Typeface(box.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                var errors = _owner._diagnostics.GroupBy(d => d.Line).ToDictionary(g => g.Key, g => g.Any(x => x.IsError));
                var marks = _owner._breakpoints.GroupBy(b => b.Line).ToDictionary(g => g.Key, g => g.Any(x => x.Active));
                var red = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
                for (int i = first; i <= last; i++)
                {
                    double y = origin.Y + i * lh;
                    var brush = i == caret ? new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xCE)) : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5C));
                    var ft = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, box.FontSize * 0.9, brush, dip);
                    dc.DrawText(ft, new Point(ActualWidth - 10 - ft.Width, y + (lh - ft.Height) / 2));
                    if (marks.TryGetValue(i + 1, out bool active))
                    {
                        // [debug] a breakpoint: filled where the program can stop, a ring where the line has no code
                        if (active) dc.DrawEllipse(red, null, new Point(10, y + lh / 2), 5, 5);
                        else dc.DrawEllipse(null, new Pen(red, 1.5), new Point(10, y + lh / 2), 4.25, 4.25);
                    }
                    else if (errors.TryGetValue(i + 1, out bool isError))
                    {
                        var c = isError ? Color.FromRgb(0xE5, 0x48, 0x4D) : Color.FromRgb(0xD4, 0xA8, 0x4B);
                        dc.DrawEllipse(new SolidColorBrush(c), null, new Point(8, y + lh / 2), 3.5, 3.5);
                    }

                    if (i + 1 == _owner._executionLine)
                    {
                        // [debug] the stopped program's line: a yellow arrow over the mark
                        double cy = y + lh / 2;
                        var arrow = new StreamGeometry();
                        using (var g = arrow.Open())
                        {
                            g.BeginFigure(new Point(3, cy - 4), true, true);
                            g.LineTo(new Point(10, cy - 4), true, false);
                            g.LineTo(new Point(16, cy), true, false);
                            g.LineTo(new Point(10, cy + 4), true, false);
                            g.LineTo(new Point(3, cy + 4), true, false);
                        }

                        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4B)), new Pen(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10)), 1), arrow);
                    }
                }
            }
        }
    }

    /// <summary>Tiny C/C++/Arduino tokenizer for colouring (keywords, types, API, literals, comments, preprocessor).</summary>
    public static class CppLexer
    {
        private static SolidColorBrush B(uint rgb)
        {
            var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            b.Freeze();
            return b;
        }

        public static readonly SolidColorBrush Default = B(0xD8D8DE);
        private static readonly SolidColorBrush Keyword = B(0xC792EA);
        private static readonly SolidColorBrush Type = B(0x6E9BFF);
        private static readonly SolidColorBrush Api = B(0x5BC0BE);
        private static readonly SolidColorBrush Constant = B(0xE5A06B);
        private static readonly SolidColorBrush Number = B(0xE5A06B);
        private static readonly SolidColorBrush Str = B(0x9CCC65);
        private static readonly SolidColorBrush Comment = B(0x6C6C74);
        private static readonly SolidColorBrush Preproc = B(0xD4A84B);

        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "if", "else", "for", "while", "do", "switch", "case", "break", "continue", "return", "default", "goto", "sizeof",
            "new", "delete", "class", "struct", "enum", "union", "public", "private", "protected", "virtual", "override",
            "template", "typename", "namespace", "using", "operator", "this", "typedef", "static", "const", "volatile",
            "extern", "inline", "constexpr", "auto", "register", "PROGMEM"
        };

        private static readonly HashSet<string> Types = new HashSet<string>
        {
            "void", "int", "long", "short", "float", "double", "char", "bool", "boolean", "byte", "word", "unsigned", "signed",
            "uint8_t", "uint16_t", "uint32_t", "uint64_t", "int8_t", "int16_t", "int32_t", "int64_t", "size_t", "String",
            "Servo", "TwoWire", "HardwareSerial", "Print", "Stream", "WiFiClass", "File"
        };

        private static readonly HashSet<string> ApiNames = new HashSet<string>
        {
            "setup", "loop", "pinMode", "digitalWrite", "digitalRead", "analogRead", "analogWrite", "analogReference",
            "delay", "delayMicroseconds", "millis", "micros", "pulseIn", "pulseInLong", "shiftOut", "shiftIn", "tone", "noTone",
            "attachInterrupt", "detachInterrupt", "digitalPinToInterrupt", "interrupts", "noInterrupts", "map", "constrain",
            "min", "max", "abs", "pow", "sqrt", "sin", "cos", "random", "randomSeed", "bitRead", "bitWrite", "bitSet", "bitClear",
            "highByte", "lowByte", "Serial", "Serial1", "Serial2", "Wire", "SPI", "EEPROM", "WiFi", "ESP", "ledcSetup",
            "ledcAttachPin", "ledcWrite", "ledcAttach", "dacWrite", "touchRead", "begin", "print", "println", "printf",
            "available", "read", "write", "beginTransmission", "endTransmission", "requestFrom", "attach", "snprintf", "memset", "memcpy"
        };

        private static readonly HashSet<string> Constants = new HashSet<string>
        {
            "HIGH", "LOW", "INPUT", "OUTPUT", "INPUT_PULLUP", "INPUT_PULLDOWN", "LED_BUILTIN", "true", "false", "NULL", "nullptr",
            "RISING", "FALLING", "CHANGE", "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7", "DEC", "HEX", "BIN", "OCT", "F"
        };

        /// <summary>Whether position <paramref name="index"/> lies inside a /* … */ comment.</summary>
        public static bool BlockCommentStateBefore(string text, int index)
        {
            bool inBlock = false, inString = false, inLine = false;
            char quote = '"';
            for (int i = 0; i < index && i < text.Length; i++)
            {
                char c = text[i];
                char n = i + 1 < text.Length ? text[i + 1] : '\0';
                if (inLine)
                {
                    if (c == '\n') inLine = false;
                    continue;
                }

                if (inBlock)
                {
                    if (c == '*' && n == '/')
                    {
                        inBlock = false;
                        i++;
                    }

                    continue;
                }

                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == quote || c == '\n') inString = false;
                    continue;
                }

                if (c == '/' && n == '/') inLine = true;
                else if (c == '/' && n == '*')
                {
                    inBlock = true;
                    i++;
                }
                else if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                }
            }

            return inBlock;
        }

        public static IEnumerable<(int Start, int Length, Brush Brush)> Tokens(string line, ref bool inBlock)
        {
            var result = new List<(int, int, Brush)>();
            int i = 0;
            if (inBlock)
            {
                int end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0)
                {
                    result.Add((0, line.Length, Comment));
                    return result;
                }

                result.Add((0, end + 2, Comment));
                i = end + 2;
                inBlock = false;
            }

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                int start = line.Length - trimmed.Length;
                int word = start + 1;
                while (word < line.Length && char.IsLetter(line[word])) word++;
                result.Add((start, word - start, Preproc));
                i = Math.Max(i, word);
            }

            while (i < line.Length)
            {
                char c = line[i];
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    result.Add((i, line.Length - i, Comment));
                    break;
                }

                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    int end = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        result.Add((i, line.Length - i, Comment));
                        inBlock = true;
                        break;
                    }

                    result.Add((i, end + 2 - i, Comment));
                    i = end + 2;
                    continue;
                }

                if (c == '"' || c == '\'' || c == '<' && trimmed.StartsWith("#include", StringComparison.Ordinal))
                {
                    char close = c == '<' ? '>' : c;
                    int j = i + 1;
                    while (j < line.Length && line[j] != close)
                    {
                        if (line[j] == '\\') j++;
                        j++;
                    }

                    int len = Math.Min(line.Length, j + 1) - i;
                    result.Add((i, len, Str));
                    i += len;
                    continue;
                }

                if (char.IsDigit(c) || c == '.' && i + 1 < line.Length && char.IsDigit(line[i + 1]))
                {
                    int j = i + 1;
                    while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '.' || line[j] == '\'')) j++;
                    result.Add((i, j - i, Number));
                    i = j;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int j = i + 1;
                    while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '_')) j++;
                    string w = line.Substring(i, j - i);
                    Brush? b = Keywords.Contains(w) ? Keyword : Types.Contains(w) ? Type : Constants.Contains(w) ? Constant : ApiNames.Contains(w) ? Api : null;
                    if (b != null) result.Add((i, j - i, b));
                    i = j;
                    continue;
                }

                i++;
            }

            return result;
        }
    }
}
