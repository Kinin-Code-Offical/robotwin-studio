using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RobotWin.Studio.Editor.Completion;

namespace RobotWin.Studio.Editor
{
    /// <summary>
    /// [code] Completion in the code editor: a list under the caret while typing a name (from the second letter), after
    /// <c>.</c>, <c>-&gt;</c>, <c>::</c> and in <c>#include &lt;</c>, or on Ctrl+Space; ↑ ↓ choose, Enter / Tab insert, Esc closes.
    /// The chosen entry's signatures and Turkish explanation are shown beside it. Inside a call, the signatures and the
    /// argument being typed are shown above the line (Ctrl+Shift+Space brings them back).
    /// </summary>
    public sealed partial class CodeEditor
    {
        /// <summary>(text, caret, explicit request) → proposals; null = none. Set by the page that knows the board.</summary>
        public Func<string, int, bool, CompletionResult?>? CompletionProvider { get; set; }

        /// <summary>(text, caret) → the call the caret is in.</summary>
        public Func<string, int, SignatureHelp?>? SignatureProvider { get; set; }

        private Popup _listPopup = null!;
        private ListBox _list = null!;
        private TextBlock _detail = null!, _doc = null!;
        private Popup _sigPopup = null!;
        private TextBlock _sigText = null!;
        private CompletionResult? _completion;
        private string? _typed;
        private bool _inserting;

        private void InitCompletion()
        {
            _list = new ListBox
            {
                Focusable = false, BorderThickness = new Thickness(0), MaxHeight = 260, Width = 300, Background = Brushes.Transparent,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.SelectionChanged += (_, _) => ShowDetail();
            _list.MouseDoubleClick += (_, _) => Accept();
            _detail = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            _detail.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
            _doc = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            _doc.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
            var side = new StackPanel { Width = 320, Margin = new Thickness(10, 8, 10, 8) };
            side.Children.Add(_detail);
            side.Children.Add(_doc);
            var sideBorder = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Child = side };
            sideBorder.SetResourceReference(Border.BorderBrushProperty, "B.Border");
            var row = new DockPanel();
            DockPanel.SetDock(_list, Dock.Left);
            row.Children.Add(_list);
            row.Children.Add(sideBorder);
            var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = row, Padding = new Thickness(2) };
            frame.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            frame.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            _listPopup = new Popup { PlacementTarget = _box, Placement = PlacementMode.Relative, AllowsTransparency = true, StaysOpen = true, Focusable = false, Child = frame };

            _sigText = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
            _sigText.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
            var sigFrame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Child = _sigText };
            sigFrame.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            sigFrame.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            _sigPopup = new Popup { PlacementTarget = _box, Placement = PlacementMode.Relative, AllowsTransparency = true, StaysOpen = true, Focusable = false, Child = sigFrame };
            App.SnapshotPopups.Add(_listPopup);
            App.SnapshotPopups.Add(_sigPopup);

            _box.PreviewTextInput += (_, e) => _typed = e.Text;
            _box.TextChanged += (_, _) => AfterTyping();
            // Focus moving to another control closes the list; so does the window going to the background (a popup would
            // otherwise float over other programs).
            _box.LostKeyboardFocus += (_, e) =>
            {
                if (e.NewFocus != null) CloseAll();
            };
            Loaded += (_, _) =>
            {
                if (Window.GetWindow(this) is { } w) w.Deactivated += (_, _) => CloseAll();
            };
            _box.SelectionChanged += (_, _) =>
            {
                if (_inserting || _completion == null) return;
                int caret = _box.CaretIndex;
                if (caret < _completion.Start || caret > _box.Text.Length) CloseList();
            };
            _box.PreviewMouseDown += (_, _) => CloseAll();
        }

        private void CloseAll()
        {
            CloseList();
            _sigPopup.IsOpen = false;
        }

        private void CloseList()
        {
            _listPopup.IsOpen = false;
            _completion = null;
        }

        /// <summary>Typing decides: names refine or open the list, '.', "->", "::", '<' open it, '(' and ',' show the call.</summary>
        private void AfterTyping()
        {
            string? typed = _typed;
            _typed = null;
            if (_inserting) return;
            if (typed == null || typed.Length == 0)
            {
                // Backspace / Delete while the list is open: it follows what is left of the name.
                if (_listPopup.IsOpen) Open(true);
                if (_sigPopup.IsOpen) ShowSignature();
                return;
            }

            char c = typed[typed.Length - 1];
            int caret = _box.CaretIndex;
            string text = _box.Text;
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                Open(false);
            }
            else if (c == '.' || (c == '>' && caret >= 2 && text[caret - 2] == '-') || (c == ':' && caret >= 2 && text[caret - 2] == ':') || c == '<' || c == '"' && IsIncludeLine(text, caret))
            {
                Open(true);
            }
            else
            {
                CloseList();
            }

            if (c == '(' || c == ',') ShowSignature();
            else if (c == ')' || c == ';' || c == '{') _sigPopup.IsOpen = false;
            else if (_sigPopup.IsOpen) ShowSignature();
        }

        private static bool IsIncludeLine(string text, int caret)
        {
            int ls = caret > 0 ? text.LastIndexOf('\n', caret - 1) + 1 : 0;
            return text.Substring(ls, caret - ls).TrimStart().StartsWith("#include", StringComparison.Ordinal);
        }

        /// <summary>Opens (or refreshes) the list for the caret; explicit = Ctrl+Space or a member / header context.</summary>
        public void Open(bool explicitRequest)
        {
            if (CompletionProvider == null || IsReadOnly) return;
            CompletionResult? r;
            try
            {
                r = CompletionProvider(_box.Text, _box.CaretIndex, explicitRequest);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.IO.IOException)
            {
                r = null;
            }

            if (r == null || r.Items.Count == 0)
            {
                CloseList();
                return;
            }

            _completion = r;
            _list.Items.Clear();
            foreach (var it in r.Items) _list.Items.Add(Row(it));
            _list.SelectedIndex = 0;
            _list.ScrollIntoView(_list.Items[0]);
            var at = CaretRect(r.Start);
            _listPopup.HorizontalOffset = Math.Max(0, at.Left - 28);
            _listPopup.VerticalOffset = at.Bottom + 2;
            _listPopup.IsOpen = true;
            ShowDetail();
        }

        /// <summary>Where a character is drawn (laying the box out first if an edit left it stale).</summary>
        private Rect CaretRect(int index)
        {
            index = Math.Max(0, Math.Min(index, _box.Text.Length));
            var r = _box.GetRectFromCharacterIndex(index);
            if (r.IsEmpty)
            {
                _box.UpdateLayout();
                r = _box.GetRectFromCharacterIndex(index);
            }

            if (r.IsEmpty)
            {
                // Still not laid out: from the line and column and the font's cell size.
                var (origin, lh, cw, _, _) = Metrics();
                string text = _box.Text;
                int ls = index > 0 ? text.LastIndexOf('\n', index - 1) + 1 : 0;
                int line = text.Take(index).Count(ch => ch == '\n');
                r = new Rect(origin.X + (index - ls) * cw, origin.Y + line * lh, cw, lh);
            }

            return r;
        }

        /// <summary>The completion list is showing (and how many entries).</summary>
        public (bool Open, int Count) CompletionState => (_listPopup.IsOpen, _completion?.Items.Count ?? 0);

        private static readonly Dictionary<ApiKind, (string Mark, Color Color)> Marks = new Dictionary<ApiKind, (string, Color)>
        {
            [ApiKind.Function] = ("ƒ", Color.FromRgb(0xDC, 0xDC, 0x8B)),
            [ApiKind.Method] = ("m", Color.FromRgb(0xDC, 0xDC, 0x8B)),
            [ApiKind.Field] = ("■", Color.FromRgb(0x9C, 0xDC, 0xFE)),
            [ApiKind.Variable] = ("v", Color.FromRgb(0x9C, 0xDC, 0xFE)),
            [ApiKind.Constant] = ("π", Color.FromRgb(0xB5, 0xCE, 0xA8)),
            [ApiKind.Class] = ("C", Color.FromRgb(0x4E, 0xC9, 0xB0)),
            [ApiKind.Type] = ("T", Color.FromRgb(0x4E, 0xC9, 0xB0)),
            [ApiKind.Keyword] = ("k", Color.FromRgb(0xC5, 0x86, 0xC0)),
            [ApiKind.Snippet] = ("✎", Color.FromRgb(0xCE, 0x91, 0x78)),
            [ApiKind.Header] = ("#", Color.FromRgb(0xCE, 0x91, 0x78))
        };

        private static ListBoxItem Row(CompletionItem it)
        {
            var (mark, color) = Marks.TryGetValue(it.Kind, out var m) ? m : ("·", Colors.Gray);
            var panel = new DockPanel();
            // [theme] The kinds' colours are VS Code's dark ones: on a light theme the list's own surface needs them darker.
            var badge = new TextBlock { Text = mark, Width = 18, Foreground = new SolidColorBrush(Shell.Themes.Readable(color, "Overlay")), FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
            DockPanel.SetDock(badge, Dock.Left);
            panel.Children.Add(badge);
            var label = new TextBlock { Text = it.Label, Margin = new Thickness(4, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            label.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
            panel.Children.Add(label);
            return new ListBoxItem { Content = panel, Tag = it, Focusable = false, Padding = new Thickness(4, 2, 4, 2) };
        }

        private void ShowDetail()
        {
            if (_list.SelectedItem is not ListBoxItem { Tag: CompletionItem it }) return;
            _detail.Text = it.Detail;
            _doc.Text = it.Doc ?? string.Empty;
            _doc.Visibility = it.Doc == null ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Keys for the open list and the call tip; true when handled.</summary>
        private bool CompletionKey(KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            if (e.Key == Key.Space && mods == ModifierKeys.Control)
            {
                Open(true);
                e.Handled = true;
                return true;
            }

            if (e.Key == Key.Space && mods == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                ShowSignature();
                e.Handled = true;
                return true;
            }

            if (e.Key == Key.Escape && (_listPopup.IsOpen || _sigPopup.IsOpen))
            {
                if (_listPopup.IsOpen) CloseList();
                else _sigPopup.IsOpen = false;
                e.Handled = true;
                return true;
            }

            if (!_listPopup.IsOpen || _completion == null) return false;
            int n = _list.Items.Count, i = _list.SelectedIndex;
            switch (e.Key)
            {
                case Key.Down:
                    Select((i + 1) % n);
                    break;
                case Key.Up:
                    Select((i - 1 + n) % n);
                    break;
                case Key.PageDown:
                    Select(Math.Min(n - 1, i + 8));
                    break;
                case Key.PageUp:
                    Select(Math.Max(0, i - 8));
                    break;
                case Key.Enter when mods == ModifierKeys.None:
                case Key.Tab when mods == ModifierKeys.None:
                    Accept();
                    break;
                case Key.Left:
                case Key.Right:
                case Key.Home:
                case Key.End:
                    CloseList();
                    return false;
                default:
                    return false;
            }

            e.Handled = true;
            return true;
        }

        private void Select(int i)
        {
            _list.SelectedIndex = i;
            _list.ScrollIntoView(_list.SelectedItem);
        }

        /// <summary>Puts the chosen entry in place of what was typed; a call gets its brackets and the caret inside them.</summary>
        private void Accept()
        {
            if (_completion == null || _list.SelectedItem is not ListBoxItem { Tag: CompletionItem it }) return;
            int start = _completion.Start, end = _box.CaretIndex;
            string text = _box.Text;
            // Whatever of the name follows the caret is replaced too ("digi|tal" → "digitalWrite").
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
            string insert = it.Insert;
            int caret = it.Caret;
            if (insert.EndsWith("()", StringComparison.Ordinal) && end < text.Length && text[end] == '(')
            {
                insert = insert.Substring(0, insert.Length - 2);
                caret = insert.Length + 1;
            }

            _inserting = true;
            try
            {
                _box.Select(start, Math.Max(0, end - start));
                _box.SelectedText = insert;
                _box.Select(Math.Min(_box.Text.Length, start + caret), 0);
            }
            finally
            {
                _inserting = false;
            }

            CloseList();
            if (_box.CaretIndex > 0 && _box.Text[_box.CaretIndex - 1] == '(') ShowSignature();
        }

        /// <summary>Snapshots: the call tip for the caret.</summary>
        public void ShowSignatureForSnapshot() => ShowSignature();

        /// <summary>Shows the signatures of the call the caret is in, the argument being typed in bold.</summary>
        private void ShowSignature()
        {
            SignatureHelp? sig = null;
            try
            {
                sig = SignatureProvider?.Invoke(_box.Text, _box.CaretIndex);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
            }

            if (sig == null)
            {
                _sigPopup.IsOpen = false;
                return;
            }

            _sigText.Inlines.Clear();
            for (int s = 0; s < sig.Signatures.Count; s++)
            {
                if (s > 0) _sigText.Inlines.Add(new LineBreak());
                AddSignature(sig.Signatures[s], sig.Argument);
            }

            if (sig.Doc != null)
            {
                _sigText.Inlines.Add(new LineBreak());
                var doc = new Run(sig.Doc) { FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
                doc.SetResourceReference(TextElement.ForegroundProperty, "B.TextSecondary");
                _sigText.Inlines.Add(doc);
            }

            int ls = _box.CaretIndex > 0 ? _box.Text.LastIndexOf('\n', _box.CaretIndex - 1) + 1 : 0;
            var at = CaretRect(ls);
            int lines = sig.Signatures.Count + (sig.Doc != null ? 1 : 0);
            _sigPopup.HorizontalOffset = Math.Max(0, at.Left);
            _sigPopup.VerticalOffset = Math.Max(0, at.Top - 22 - 19 * lines);
            _sigPopup.IsOpen = true;
        }

        private void AddSignature(string signature, int argument)
        {
            int open = signature.IndexOf('('), close = signature.LastIndexOf(')');
            if (open < 0 || close < open)
            {
                _sigText.Inlines.Add(new Run(signature));
                return;
            }

            _sigText.Inlines.Add(new Run(signature.Substring(0, open + 1)));
            var args = SplitArgs(signature.Substring(open + 1, close - open - 1));
            for (int a = 0; a < args.Count; a++)
            {
                if (a > 0) _sigText.Inlines.Add(new Run(", "));
                var run = new Run(args[a].Trim());
                if (a == argument)
                {
                    run.FontWeight = FontWeights.Bold;
                    run.SetResourceReference(TextElement.ForegroundProperty, "B.Accent");
                }

                _sigText.Inlines.Add(run);
            }

            _sigText.Inlines.Add(new Run(signature.Substring(close)));
        }

        private static List<string> SplitArgs(string s)
        {
            var list = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(' || s[i] == '<' || s[i] == '[') depth++;
                else if (s[i] == ')' || s[i] == '>' || s[i] == ']') depth--;
                else if (s[i] == ',' && depth == 0)
                {
                    list.Add(s.Substring(start, i - start));
                    start = i + 1;
                }
            }

            if (s.Trim().Length > 0) list.Add(s.Substring(start));
            return list;
        }
    }
}
