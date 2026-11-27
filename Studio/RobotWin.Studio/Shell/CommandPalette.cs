using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// Ctrl+K / Ctrl+Shift+P: type to find any command, page, open tab, file, template or part,
    /// arrow keys to choose, Enter to run, Esc to close.
    /// </summary>
    public sealed class CommandPalette : Grid
    {
        private readonly TextBox _query = new TextBox();
        private readonly StackPanel _list = new StackPanel();
        private readonly ScrollViewer _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 440 };
        private readonly TextBlock _empty = new TextBlock { Text = "Eşleşen komut yok", Margin = new Thickness(14, 10, 14, 12), Visibility = Visibility.Collapsed };
        private Func<IEnumerable<PaletteCommand>> _source = () => Array.Empty<PaletteCommand>();
        private string? _context;
        private List<PaletteCommand> _all = new List<PaletteCommand>();
        private List<PaletteCommand> _shown = new List<PaletteCommand>();
        private int _index;
        private IInputElement? _returnFocus;

        public CommandPalette()
        {
            Visibility = Visibility.Collapsed;
            Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0));
            MouseDown += (_, e) =>
            {
                if (e.OriginalSource == this) Close();
            };

            var panel = new Border
            {
                Width = 640,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 90, 0, 0),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
            };
            panel.SetResourceReference(Border.BackgroundProperty, "B.Raised");
            panel.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            var stack = new StackPanel();
            _query.Tag = "Komut, sayfa, sekme, dosya, şablon veya parça ara…";
            _query.FontSize = Shell.TypeScale.Subtitle;
            _query.Margin = new Thickness(10, 10, 10, 6);
            _query.Padding = new Thickness(10, 7, 10, 7);
            _query.TextChanged += (_, _) => Filter();
            _query.PreviewKeyDown += OnKey;
            stack.Children.Add(_query);
            _scroll.Content = _list;
            stack.Children.Add(_scroll);
            _empty.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
            stack.Children.Add(_empty);
            var hint = new TextBlock { Text = "↑↓ seç · Enter çalıştır · Esc kapat", FontSize = 12, Margin = new Thickness(14, 4, 14, 10) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
            stack.Children.Add(hint);
            panel.Child = stack;
            Children.Add(panel);
        }

        public bool IsOpen => Visibility == Visibility.Visible;

        public void Open(Func<IEnumerable<PaletteCommand>> source, string initial = "", string? context = null)
        {
            _source = source;
            _context = context;
            _all = source().Where(c => c.Ready == null || SafeReady(c)).ToList();
            _returnFocus = Keyboard.FocusedElement;
            Visibility = Visibility.Visible;
            _query.Text = initial;
            _query.CaretIndex = initial.Length;
            Filter();
            Dispatcher.BeginInvoke(new Action(() => _query.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        public void Close()
        {
            Visibility = Visibility.Collapsed;
            if (_returnFocus is UIElement e && e.IsVisible) e.Focus();
        }

        private static bool SafeReady(PaletteCommand c)
        {
            try
            {
                return c.Ready!();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NullReferenceException)
            {
                return false;
            }
        }

        private void Filter()
        {
            string q = _query.Text.Trim();
            IEnumerable<PaletteCommand> hits;
            if (q.Length == 0)
            {
                // Without a query: the page in front's commands first, then the rest; not the long lists (parts, probes).
                hits = _all.Where(c => !c.OnlyWhenAsked).OrderByDescending(c => _context != null && c.Context == _context).Take(60);
            }
            else
            {
                // [palette] word by word first ("MOSFET ekle"); the old run-together match for a query typed as one piece
                hits = _all.Select(c =>
                    {
                        int words = PaletteMatch.WordScore(c, q, _context);
                        int run = Math.Max(Math.Max(PaletteMatch.Score(c.Title, q), PaletteMatch.Score(RobotWin.Studio.Shell.Lang.T(c.Title), q)), Math.Max(PaletteMatch.Score(c.Group + " " + c.Title, q), PaletteMatch.Score(RobotWin.Studio.Shell.Lang.T(c.Group) + " " + RobotWin.Studio.Shell.Lang.T(c.Title), q)) - 40);
                        if (run >= 0 && _context != null && c.Context == _context) run += 60;
                        return (c, s: Math.Max(words >= 0 ? words + 300 : -1, run));
                    })
                    .Where(x => x.s >= 0).OrderByDescending(x => x.s).Take(60).Select(x => x.c);
            }

            _shown = hits.ToList();
            _index = 0;
            Render();
        }

        private void Render()
        {
            _list.Children.Clear();
            _empty.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            string? lastGroup = null;
            for (int i = 0; i < _shown.Count; i++)
            {
                var c = _shown[i];
                if (c.Group != lastGroup && _query.Text.Trim().Length == 0)
                {
                    var head = new TextBlock { Text = c.Group, FontSize = 12, Margin = new Thickness(14, i == 0 ? 2 : 8, 0, 2) };
                    head.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
                    _list.Children.Add(head);
                    lastGroup = c.Group;
                }

                var row = new Grid { Margin = new Thickness(6, 0, 6, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new StackPanel { Orientation = Orientation.Horizontal };
                if (_query.Text.Trim().Length > 0)
                {
                    var g = new TextBlock { Text = c.Group + "  ", FontSize = 12 };
                    g.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
                    left.Children.Add(g);
                }

                left.Children.Add(new TextBlock { Text = c.Title, FontSize = 13 });
                if (c.Detail.Length > 0)
                {
                    var d = new TextBlock { Text = "  " + c.Detail, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 };
                    d.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
                    left.Children.Add(d);
                }

                row.Children.Add(left);
                if (c.Shortcut.Length > 0)
                {
                    var sc = new TextBlock { Text = c.Shortcut, FontSize = 12 };
                    sc.SetResourceReference(TextBlock.FontFamilyProperty, "F.Mono");
                    sc.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
                    Grid.SetColumn(sc, 1);
                    row.Children.Add(sc);
                }

                var border = new Border { Child = row, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Tag = i };
                if (i == _index) border.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
                else border.Background = Brushes.Transparent;
                int index = i;
                border.MouseEnter += (_, _) =>
                {
                    if (_index == index) return;
                    _index = index;
                    Highlight();
                };
                border.MouseLeftButtonUp += (_, _) => Execute(index);
                _list.Children.Add(border);
            }
        }

        private void Highlight()
        {
            foreach (var child in _list.Children.OfType<Border>())
            {
                if ((int)child.Tag == _index)
                {
                    child.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
                    child.BringIntoView();
                }
                else
                {
                    child.Background = Brushes.Transparent;
                }
            }
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    break;
                case Key.Down:
                    if (_shown.Count > 0) _index = (_index + 1) % _shown.Count;
                    Highlight();
                    break;
                case Key.Up:
                    if (_shown.Count > 0) _index = (_index - 1 + _shown.Count) % _shown.Count;
                    Highlight();
                    break;
                case Key.PageDown:
                    _index = Math.Min(_shown.Count - 1, _index + 8);
                    Highlight();
                    break;
                case Key.PageUp:
                    _index = Math.Max(0, _index - 8);
                    Highlight();
                    break;
                case Key.Enter:
                    Execute(_index);
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        private void Execute(int index)
        {
            if (index < 0 || index >= _shown.Count) return;
            var cmd = _shown[index];
            Close();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    cmd.Run();
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is System.IO.IOException)
                {
                    Workspace.Current.Status = cmd.Title + ": " + ex.Message;
                }
            }));
        }
    }
}
