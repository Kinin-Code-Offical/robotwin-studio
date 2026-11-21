using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RobotWin.Studio.Help;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Updates;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// Yardım (F1): the bundled Turkish user guide rendered from Markdown with a topic tree and full-text
    /// search, plus generated sections: keyboard shortcuts, news, updates / old versions and about.
    /// </summary>
    public partial class HelpPage : UserControl
    {
        public const string Shortcuts = "app:shortcuts";
        public const string NewsId = "app:news";
        public const string UpdatesId = "app:updates";
        public const string AboutId = "app:about";

        private readonly Func<IEnumerable<(string Group, string Title, string Keys)>> _shortcuts;
        private readonly HelpTopic _shortcutsTopic = new HelpTopic { Id = Shortcuts, Title = "Klavye kısayolları", Kind = HelpTopicKind.Shortcuts };
        private readonly HelpTopic _newsTopic = new HelpTopic { Id = NewsId, Title = "Haberler", Kind = HelpTopicKind.News };
        private readonly HelpTopic _updatesTopic = new HelpTopic { Id = UpdatesId, Title = "Güncellemeler ve eski sürümler", Kind = HelpTopicKind.Updates };
        private readonly HelpTopic _aboutTopic = new HelpTopic { Id = AboutId, Title = "Hakkında", Kind = HelpTopicKind.About };
        private readonly Stack<(HelpTopic Topic, string? Anchor)> _history = new Stack<(HelpTopic, string?)>();
        private readonly DispatcherTimer _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        private readonly HashSet<string> _expandedNotes = new HashSet<string>(StringComparer.Ordinal);

        private HelpLibrary _library;
        private HelpTopic? _current;
        private MarkdownContext? _currentContext;
        private FlowDocumentScrollViewer? _currentViewer;
        private string _newsSignature = string.Empty;
        private ScrollViewer? _updatesScroll;
        private Button? _newsRefresh;
        private static readonly bool Snapshot = Environment.GetCommandLineArgs().Contains("--snapshot");

        public HelpPage(Func<IEnumerable<(string Group, string Title, string Keys)>> shortcuts)
        {
            _shortcuts = shortcuts;
            InitializeComponent();
            _library = HelpLibrary.Load();
            _searchDelay.Tick += (_, _) =>
            {
                _searchDelay.Stop();
                BuildNav();
            };
            UpdateManager.Current.Changed += (_, _) => OnUpdatesChanged();
            PreviewMouseDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.XButton1 && _history.Count > 0)
                {
                    GoBack();
                    e.Handled = true;
                }
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt && _history.Count > 0)
                {
                    GoBack();
                    e.Handled = true;
                }
                else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                    e.Handled = true;
                }
            };
            Open(_library.Guide.FirstOrDefault(t => t.Kind == HelpTopicKind.Guide) ?? MissingTopic(), null, false);
        }

        private string Query => SearchBox.Text.Trim();

        /// <summary>
        /// Opens a section: "guide" (start of the guide), "shortcuts", "news", "updates", "versions", "about",
        /// "news:&lt;id&gt;", or a guide file name ("02-devre.md").
        /// </summary>
        public void Show(string section)
        {
            if (section.StartsWith("news:", StringComparison.Ordinal))
            {
                Open(_newsTopic, section.Substring(5));
                return;
            }

            switch (section)
            {
                case "shortcuts":
                    Open(_shortcutsTopic, null);
                    return;
                case "news":
                    Open(_newsTopic, null);
                    return;
                case "updates":
                    Open(_updatesTopic, null);
                    return;
                case "versions":
                    Open(_updatesTopic, "versions");
                    return;
                case "about":
                    Open(_aboutTopic, null);
                    return;
            }

            var page = _library.AllGuidePages.FirstOrDefault(t => string.Equals(Path.GetFileName(t.FilePath), section, StringComparison.OrdinalIgnoreCase));
            Open(page ?? _library.Guide.FirstOrDefault(t => t.Kind == HelpTopicKind.Guide) ?? MissingTopic(), null);
        }

        /// <summary>--snapshot --page help --fault news|updates|about|shortcuts|search:&lt;text&gt;|&lt;file.md&gt;</summary>
        public void PrepareSnapshot(string? fault)
        {
            if (string.IsNullOrEmpty(fault)) return;
            if (fault == "updates" || fault == "versions" || fault == "news" || fault.StartsWith("release:", StringComparison.Ordinal))
            {
                // Fetch fresh data for the picture (use --wait 3); the section re-renders when it arrives.
                _ = UpdateManager.Current.CheckAsync();
            }

            if (fault.StartsWith("release:", StringComparison.Ordinal))
            {
                Open(_updatesTopic, fault, false);
                return;
            }

            if (fault.StartsWith("search:", StringComparison.Ordinal))
            {
                SearchBox.Text = fault.Substring(7);
                _searchDelay.Stop();
                BuildNav();
                var first = Nav.Children.OfType<Button>().FirstOrDefault(b => b.Tag is (HelpTopic, string));
                if (first?.Tag is (HelpTopic t, string _)) Open(t, null, false);
                return;
            }

            Show(fault);
        }

        private HelpTopic MissingTopic() => new HelpTopic { Id = "missing", Title = "Kullanım kılavuzu", Kind = HelpTopicKind.Missing };

        // ================================================================ navigation tree

        private void BuildNav()
        {
            Nav.Children.Clear();
            ClearSearch.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (Query.Length >= 2)
            {
                BuildSearchResults();
                return;
            }

            if (_library.Root == null)
            {
                Nav.Children.Add(NavButton(MissingTopic(), "Kullanım kılavuzu", 0, _current?.Kind == HelpTopicKind.Missing));
            }

            foreach (var t in _library.Guide)
            {
                if (t.Kind == HelpTopicKind.Group)
                {
                    Nav.Children.Add(Overline(t.Title, 14));
                    foreach (var c in t.Children) AddGuideNode(c, 0);
                }
                else
                {
                    AddGuideNode(t, 0);
                }
            }

            Nav.Children.Add(Overline("Uygulama", 18));
            var um = UpdateManager.Current;
            Nav.Children.Add(NavButton(_shortcutsTopic, _shortcutsTopic.Title, 0, _current == _shortcutsTopic));
            Nav.Children.Add(NavButton(_newsTopic, _newsTopic.Title, 0, _current == _newsTopic, um.UnreadCount > 0 ? um.UnreadCount + " yeni" : null));
            Nav.Children.Add(NavButton(_updatesTopic, _updatesTopic.Title, 0, _current == _updatesTopic, um.Available != null ? "1 yeni" : null));
            Nav.Children.Add(NavButton(_aboutTopic, _aboutTopic.Title, 0, _current == _aboutTopic));
        }

        private void AddGuideNode(HelpTopic t, int depth)
        {
            bool selected = _current != null && _current.FilePath != null && string.Equals(_current.FilePath, t.FilePath, StringComparison.OrdinalIgnoreCase);
            Nav.Children.Add(NavButton(t, t.Title, depth, selected));
            if (selected && t.FilePath != null)
            {
                // The open page's sections ("bu sayfada").
                foreach (var h in _library.GetDocument(t.FilePath).Headings.Where(h => h.Level == 2))
                {
                    var b = NavButton(t, h.Text, depth + 1, false, null, h.Anchor);
                    b.FontSize = 12;
                    Nav.Children.Add(b);
                }
            }

            foreach (var c in t.Children) AddGuideNode(c, depth + 1);
        }

        private TextBlock Overline(string text, double top) =>
            new TextBlock { Text = text, Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(10, top, 0, 6) };

        private Button NavButton(HelpTopic topic, string text, int depth, bool selected, string? badge = null, string? anchor = null)
        {
            var grid = new DockPanel { LastChildFill = true };
            if (badge != null)
            {
                var pill = new Border
                {
                    Style = (Style)FindResource("Badge"),
                    Background = (Brush)FindResource("B.AccentSoft"),
                    Child = new TextBlock { Text = badge, FontSize = 12, Foreground = (Brush)FindResource("B.Accent") },
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(pill, Dock.Right);
                grid.Children.Add(pill);
            }

            grid.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button
            {
                Style = (Style)FindResource("Btn.Ghost"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10 + depth * 14, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 1),
                Content = grid,
                Tag = (topic, anchor ?? string.Empty),
                ToolTip = text.Length > 30 ? text : null
            };
            if (selected)
            {
                b.Background = (Brush)FindResource("B.Raised");
                b.Foreground = (Brush)FindResource("B.Text");
            }

            b.Click += (_, _) => Open(topic, anchor);
            return b;
        }

        // ================================================================ search

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        }

        private void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Clear();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                _searchDelay.Stop();
                BuildNav();
                var first = Nav.Children.OfType<Button>().FirstOrDefault();
                if (first?.Tag is (HelpTopic t, string a)) Open(t, a.Length > 0 ? a : null);
                e.Handled = true;
            }
        }

        private void OnSearchFocus(object sender, KeyboardFocusChangedEventArgs e) =>
            SearchFrame.BorderBrush = (Brush)FindResource(SearchBox.IsKeyboardFocusWithin ? "B.TextMuted" : "B.Border");

        private void OnClearSearch(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private void BuildSearchResults()
        {
            string q = Query;
            var hits = new List<(HelpTopic Topic, HelpSearchHit Hit)>();
            foreach (var t in _library.AllGuidePages)
            {
                var hit = HelpSearch.Match(t.Id, t.Title, _library.GetPlainText(t), q);
                if (hit != null) hits.Add((t, hit));
            }

            foreach (var t in new[] { _shortcutsTopic, _newsTopic, _aboutTopic })
            {
                var hit = HelpSearch.Match(t.Id, t.Title, GeneratedText(t), q);
                if (hit != null) hits.Add((t, hit));
            }

            hits = hits.OrderByDescending(h => HelpSearch.Fold(h.Topic.Title).Contains(HelpSearch.Fold(q), StringComparison.Ordinal)).ThenByDescending(h => h.Hit.Count).ToList();
            Nav.Children.Add(new TextBlock
            {
                Text = hits.Count == 0 ? $"\"{q}\" için sonuç yok." : $"{hits.Sum(h => h.Hit.Count)} eşleşme, {hits.Count} sayfa",
                Style = (Style)FindResource("Text.Caption"),
                Margin = new Thickness(10, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var (topic, hit) in hits)
            {
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = topic.Title, Foreground = (Brush)FindResource("B.Text"), TextTrimming = TextTrimming.CharacterEllipsis });
                if (hit.Snippet.Length > 0)
                    sp.Children.Add(new TextBlock { Text = hit.Snippet, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, MaxHeight = 48, Margin = new Thickness(0, 2, 0, 0) });
                var b = new Button
                {
                    Style = (Style)FindResource("Btn.Ghost"),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(10, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 2),
                    Content = sp,
                    Tag = (topic, string.Empty)
                };
                if (_current == topic) b.Background = (Brush)FindResource("B.Raised");
                var target = topic;
                b.Click += (_, _) => Open(target, null);
                Nav.Children.Add(b);
            }
        }

        /// <summary>Searchable text of the generated sections.</summary>
        private string GeneratedText(HelpTopic t) => t.Kind switch
        {
            HelpTopicKind.Shortcuts => MarkdownParser.Parse(ShortcutsMarkdown()).PlainText(),
            HelpTopicKind.News => string.Join("\n", UpdateManager.Current.News.Select(n => n.Title + "\n" + MarkdownParser.Parse(n.Body).PlainText())),
            HelpTopicKind.About => MarkdownParser.Parse(AboutMarkdown()).PlainText(),
            _ => string.Empty
        };

        // ================================================================ opening topics

        private void Open(HelpTopic topic, string? anchor, bool remember = true)
        {
            if (remember && _current != null && (_current != topic || anchor != null)) _history.Push((_current, null));
            BackButton.Visibility = _history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _current = topic;
            _currentContext = null;
            _currentViewer = null;
            _updatesScroll = null;
            string? highlight = Query.Length >= 2 ? Query : null;
            switch (topic.Kind)
            {
                case HelpTopicKind.Guide when topic.FilePath != null:
                    ShowMarkdown(_library.GetDocument(topic.FilePath), Path.GetDirectoryName(topic.FilePath), highlight, anchor);
                    break;
                case HelpTopicKind.Shortcuts:
                    ShowMarkdown(MarkdownParser.Parse(ShortcutsMarkdown()), null, highlight, anchor);
                    break;
                case HelpTopicKind.News:
                    ShowNews(anchor, highlight);
                    break;
                case HelpTopicKind.Updates:
                    ShowUpdates(anchor);
                    break;
                case HelpTopicKind.About:
                    ShowAbout(highlight);
                    break;
                default:
                    ShowMarkdown(MarkdownParser.Parse(MissingMarkdown), null, null, null);
                    break;
            }

            BuildNav();
        }

        private void OnBack(object sender, RoutedEventArgs e) => GoBack();

        private void GoBack()
        {
            if (_history.Count == 0) return;
            var (topic, anchor) = _history.Pop();
            Open(topic, anchor, false);
        }

        private const string MissingMarkdown =
            "# Kullanım kılavuzu\n\nKullanım kılavuzu bu kopyada bulunamadı (uygulama klasöründe `Docs\\kullanim` yok).\n\n" +
            "Kılavuzu çevrimiçi okuyabilirsiniz: [belge sitesinde kullanım kılavuzu](" + HelpLibrary.OnlineGuide + ")\n\n" +
            "Soldaki **Klavye kısayolları**, **Haberler**, **Güncellemeler** ve **Hakkında** bölümleri her zaman kullanılabilir.";

        private void ShowMarkdown(MarkdownDocument md, string? baseDir, string? highlight, string? anchor)
        {
            var ctx = new MarkdownContext { BaseDirectory = baseDir, Highlight = highlight };
            ctx.OnLink = url => HandleLink(url, baseDir);
            var doc = MarkdownView.Render(md, ctx);
            doc.PagePadding = new Thickness(56, 36, 56, 56);
            doc.MaxPageWidth = 980;
            var viewer = MarkdownView.ScrollViewer(doc);
            _currentContext = ctx;
            _currentViewer = viewer;
            ContentHost.Content = viewer;
            ScrollTo(anchor, ctx);
        }

        private void ScrollTo(string? anchor, MarkdownContext ctx)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                FrameworkContentElement? target = null;
                if (anchor != null) target = FindAnchor(ctx, anchor);
                target ??= ctx.Highlights.FirstOrDefault();
                target?.BringIntoView();
            }), DispatcherPriority.Loaded);
        }

        /// <summary>Exact anchor first, then a Turkish-tolerant comparison (ı/i, ş/s …) for hand-written links.</summary>
        private static Block? FindAnchor(MarkdownContext ctx, string anchor)
        {
            if (ctx.Anchors.TryGetValue(anchor, out var b)) return b;
            string folded = HelpSearch.Fold(anchor.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).Aggregate(new StringBuilder(), (s, c) => s.Append(c)).ToString());
            return ctx.Anchors.FirstOrDefault(kv => HelpSearch.Fold(kv.Key) == folded).Value;
        }

        private void HandleLink(string url, string? baseDir)
        {
            if (url.StartsWith("#", StringComparison.Ordinal))
            {
                if (_currentContext != null) FindAnchor(_currentContext, Uri.UnescapeDataString(url.Substring(1)))?.BringIntoView();
                return;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && !abs.IsFile)
            {
                if (abs.Scheme == Uri.UriSchemeHttps || abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeMailto) OpenUrl(url);
                return;
            }

            if (baseDir == null) return;
            string? local = HelpLibrary.ResolveLocal(baseDir, url);
            if (local == null) return;
            string? anchor = HelpLibrary.AnchorOf(url);
            var topic = _library.FindByPath(local);
            if (topic != null)
            {
                Open(topic, anchor);
                return;
            }

            if (File.Exists(local) && local.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                Open(new HelpTopic { Id = _library.Id(local), Title = _library.GetDocument(local).Title ?? Path.GetFileName(local), Kind = HelpTopicKind.Guide, FilePath = local }, anchor);
                return;
            }

            if (File.Exists(local))
            {
                OpenUrl(local);
                return;
            }

            // A repository file that is not bundled (e.g. ../../LICENSE): show it on GitHub.
            if (_library.Root != null) OpenUrl(_library.OnlineUrl(Path.GetRelativePath(_library.Root, local)));
        }

        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                Workspace.Current.Status = "Açılamadı: " + url;
            }
        }

        // ================================================================ generated: shortcuts and about

        private string ShortcutsMarkdown()
        {
            var sb = new StringBuilder("# Klavye kısayolları\n\nBu liste uygulamanın menülerinden ve komut paletinden (Ctrl+K) üretilir; her zaman çalışan sürümle aynıdır.\n");
            foreach (var group in _shortcuts().GroupBy(s => s.Group))
            {
                sb.Append("\n## ").Append(group.Key).Append("\n\n| Kısayol | Komut |\n| --- | --- |\n");
                foreach (var (_, title, keys) in group) sb.Append("| `").Append(keys.Replace("|", "\\|")).Append("` | ").Append(title.Replace("|", "\\|")).Append(" |\n");
            }

            return sb.ToString();
        }

        private static string AboutMarkdown()
        {
            var um = UpdateManager.Current;
            var info = um.Install;
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio");
            return "## Bu kopya\n\n| | |\n| --- | --- |\n" +
                   $"| Sürüm | **{um.CurrentVersion}** |\n" +
                   $"| Kurulum | {info.ModeText} · {(info.SelfContained ? "tam kurulum (.NET dahil)" : ".NET 9 Masaüstü Çalışma Zamanı ile")} |\n" +
                   $"| Uygulama klasörü | `{info.AppDirectory}` |\n" +
                   $"| Ayarlar ve günlükler | `{data}` |\n" +
                   $"| .NET | {Environment.Version} · {(Environment.Is64BitProcess ? "64" : "32")} bit |\n" +
                   $"| Windows | {Environment.OSVersion.Version} |\n\n" +
                   "## Bağlantılar\n\n" +
                   $"- [GitHub sayfası]({UpdateService.RepoUrl})\n" +
                   $"- [Tüm sürümler ve indirmeler]({UpdateService.ReleasesPage})\n" +
                   $"- [Sorun bildir]({UpdateService.RepoUrl}/issues/new)\n" +
                   $"- [Lisans (MIT)]({UpdateService.RepoUrl}/blob/main/LICENSE)\n" +
                   $"- [Üçüncü taraf bileşenler]({UpdateService.RepoUrl}/blob/main/THIRD_PARTY_NOTICES.md)\n";
        }

        private void ShowAbout(string? highlight)
        {
            var root = new StackPanel { Margin = new Thickness(56, 36, 56, 48), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
            var logo = new Image { Source = Branding.Logo.Render(144), Width = 72, Height = 72, Margin = new Thickness(0, 0, 20, 0) };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            head.Children.Add(logo);
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "RobotWin Studio", Style = (Style)FindResource("Text.Hero") });
            title.Children.Add(new TextBlock
            {
                Text = $"Sürüm {UpdateManager.Current.CurrentVersion} · Devre, PCB, kod, 3D modelleme ve gerçek fizikli açık dünyada robot simülasyonu.",
                Style = (Style)FindResource("Text.Body"),
                Margin = new Thickness(0, 4, 0, 0)
            });
            head.Children.Add(title);
            root.Children.Add(head);

            var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            buttons.Children.Add(ActionButton("Güncellemeleri denetle", () => Open(_updatesTopic, null), primary: true));
            buttons.Children.Add(ActionButton("Haberler", () => Open(_newsTopic, null)));
            buttons.Children.Add(ActionButton("Ayar ve günlük klasörünü aç", () =>
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio");
                Directory.CreateDirectory(dir);
                OpenUrl(dir);
            }));
            root.Children.Add(buttons);

            var ctx = new MarkdownContext { Highlight = highlight, OnLink = url => HandleLink(url, null) };
            var doc = MarkdownView.Render(MarkdownParser.Parse(AboutMarkdown()), ctx);
            root.Children.Add(MarkdownView.InlineViewer(doc));
            _currentContext = ctx;
            ContentHost.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            ScrollTo(null, ctx);
        }

        private Button ActionButton(string text, Action click, bool primary = false, string? tip = null)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 6, 14, 6), ToolTip = tip };
            if (primary) b.Style = (Style)FindResource("Btn.Primary");
            b.Click += (_, _) => click();
            return b;
        }

        // ================================================================ news

        private void ShowNews(string? focusId, string? highlight)
        {
            var um = UpdateManager.Current;
            var items = um.News.ToList();
            _newsSignature = string.Join("|", items.Select(n => n.Id));
            var root = new StackPanel { Margin = new Thickness(56, 36, 56, 48), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var refresh = new Button { Content = um.IsChecking ? "Yenileniyor…" : "Yenile", Style = (Style)FindResource("Btn.Ghost"), IsEnabled = !um.IsChecking, VerticalAlignment = VerticalAlignment.Center };
            refresh.Click += (_, _) => _ = um.CheckAsync();
            _newsRefresh = refresh;
            DockPanel.SetDock(refresh, Dock.Right);
            head.Children.Add(refresh);
            head.Children.Add(new TextBlock { Text = "Haberler", Style = (Style)FindResource("Text.Title"), FontSize = 26 });
            root.Children.Add(head);
            root.Children.Add(new TextBlock
            {
                Text = "Duyurular ve sürüm notları. Okumadıklarınız \"Yeni\" olarak işaretlidir.",
                Style = (Style)FindResource("Text.Body"),
                Margin = new Thickness(0, 0, 0, 22)
            });
            if (items.Count == 0)
            {
                root.Children.Add(new TextBlock { Text = um.LastError ?? "Henüz haber yok. İnternete bağlıyken Yenile ile alınır.", Style = (Style)FindResource("Text.Body") });
            }

            FrameworkElement? focus = null;
            var ctxs = new List<MarkdownContext>();
            for (int i = 0; i < items.Count; i++)
            {
                var n = items[i];
                var card = NewsCard(n, um.IsUnread(n), expanded: i < 2 || n.Id == focusId || highlight != null, highlight, ctxs);
                root.Children.Add(card);
                if (n.Id == focusId) focus = card;
            }

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            ContentHost.Content = scroll;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (focus != null) focus.BringIntoView(new Rect(0, 0, 1, Math.Max(1, scroll.ViewportHeight - 40)));
                else ctxs.SelectMany(c => c.Highlights).FirstOrDefault()?.BringIntoView();
            }), DispatcherPriority.Loaded);
            // Shown = read (the badges stay until the section is opened again). Snapshots leave the state alone.
            if (!Snapshot) um.MarkRead(items);
        }

        private FrameworkElement NewsCard(NewsItem n, bool unread, bool expanded, string? highlight, List<MarkdownContext> ctxs)
        {
            var sp = new StackPanel();
            var title = new DockPanel();
            if (unread)
            {
                var badge = new Border
                {
                    Style = (Style)FindResource("Badge"),
                    Background = (Brush)FindResource("B.AccentSoft"),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Yeni", FontSize = 12, Foreground = (Brush)FindResource("B.Accent") }
                };
                title.Children.Add(badge);
            }

            title.Children.Add(new TextBlock { Text = n.Title, Style = (Style)FindResource("Text.Subtitle"), FontSize = 16, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(title);
            string kind = n.Release != null ? "Sürüm notları · " + n.Release.DisplayVersion + (n.Release.IsPrerelease ? " · ön sürüm" : string.Empty) : "Duyuru";
            string date = n.Date == DateTimeOffset.MinValue ? string.Empty : n.Date.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR")) + " · ";
            sp.Children.Add(new TextBlock { Text = date + kind, Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 4, 0, 10) });

            string docsDir = Path.Combine(AppContext.BaseDirectory, "Docs");
            if (n.Image != null && MarkdownView.LoadImage(n.Image, docsDir) is { } img)
            {
                var im = new Image { Source = img, Stretch = Stretch.Uniform, MaxHeight = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
                sp.Children.Add(im);
            }

            var bodyHost = new ContentControl();
            void Fill()
            {
                var ctx = new MarkdownContext { BaseDirectory = docsDir, HeadingOffset = 1, Highlight = highlight, OnLink = url => HandleLink(url, docsDir) };
                ctxs.Add(ctx);
                bodyHost.Content = MarkdownView.InlineViewer(MarkdownView.Render(MarkdownParser.Parse(n.Body), ctx));
            }

            if (expanded || _expandedNotes.Contains(n.Id)) Fill();
            sp.Children.Add(bodyHost);

            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (bodyHost.Content == null && n.Body.Trim().Length > 0)
            {
                var more = new Button { Content = "Devamını göster", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(-12, 0, 8, 0) };
                more.Click += (_, _) =>
                {
                    _expandedNotes.Add(n.Id);
                    Fill();
                    more.Visibility = Visibility.Collapsed;
                };
                actions.Children.Add(more);
            }

            if (n.Release != null)
            {
                var r = n.Release;
                var go = new Button { Content = "Güncellemeler ve bu sürüm", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(-12, 0, 8, 0) };
                go.Click += (_, _) => Open(_updatesTopic, "release:" + r.Tag);
                actions.Children.Add(go);
            }

            if (n.Link != null)
            {
                var link = new Button { Content = n.Release != null ? "GitHub'da aç" : "Bağlantıyı aç", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(-12, 0, 8, 0), ToolTip = n.Link };
                string url = n.Link;
                link.Click += (_, _) => OpenUrl(url);
                actions.Children.Add(link);
            }

            if (actions.Children.Count > 0) sp.Children.Add(actions);
            return new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14), Child = sp, BorderBrush = (Brush)FindResource(unread ? "B.BorderStrong" : "B.Border") };
        }

        // ================================================================ updates and old versions

        private void OnUpdatesChanged()
        {
            BuildNav();
            if (_current == _updatesTopic)
            {
                double offset = _updatesScroll?.VerticalOffset ?? 0;
                ShowUpdates(null);
                _updatesScroll?.ScrollToVerticalOffset(offset);
            }
            else if (_current == _newsTopic && string.Join("|", UpdateManager.Current.News.Select(n => n.Id)) != _newsSignature)
            {
                ShowNews(null, Query.Length >= 2 ? Query : null);
            }
            else if (_current == _newsTopic && _newsRefresh != null)
            {
                bool busy = UpdateManager.Current.IsChecking;
                _newsRefresh.Content = busy ? "Yenileniyor…" : "Yenile";
                _newsRefresh.IsEnabled = !busy;
            }
        }

        private void ShowUpdates(string? focus)
        {
            var um = UpdateManager.Current;
            var info = um.Install;
            var s = AppSettings.Current;
            var root = new StackPanel { Margin = new Thickness(56, 36, 56, 48), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            root.Children.Add(new TextBlock { Text = "Güncellemeler", Style = (Style)FindResource("Text.Title"), FontSize = 26, Margin = new Thickness(0, 0, 0, 18) });

            // ---- status card
            var status = new StackPanel();
            var line = new DockPanel();
            var ver = new StackPanel();
            ver.Children.Add(new TextBlock { Text = "Yüklü sürüm", Style = (Style)FindResource("Text.Caption") });
            ver.Children.Add(new TextBlock { Text = um.CurrentVersion.ToString(), Style = (Style)FindResource("Text.Title"), FontSize = 24, Margin = new Thickness(0, 2, 0, 0) });
            line.Children.Add(ver);
            var mode = new TextBlock
            {
                Text = info.ModeText + "\n" + (info.SelfContained ? "Tam kurulum (.NET dahil)" : ".NET 9 Masaüstü Çalışma Zamanı ile"),
                Style = (Style)FindResource("Text.Caption"),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(mode, Dock.Right);
            line.Children.Insert(0, mode);
            status.Children.Add(line);

            string state;
            string brush = "B.TextSecondary";
            if (um.IsChecking) state = "GitHub'a soruluyor…";
            else if (um.Available != null)
            {
                state = $"Yeni sürüm var: {um.Available.Title} ({um.Available.DisplayVersion}).";
                brush = "B.Accent";
            }
            else if (um.Releases.Count == 0) state = um.LastError ?? "Sürüm bilgisi henüz alınmadı.";
            else state = "Güncel: kullanılabilen en yeni sürüm bu.";
            status.Children.Add(new TextBlock { Text = state, Foreground = (Brush)FindResource(brush), FontSize = 14, Margin = new Thickness(0, 14, 0, 2), TextWrapping = TextWrapping.Wrap });
            if (um.LastError != null && um.Releases.Count > 0)
                status.Children.Add(new TextBlock { Text = um.LastError + " (Son bilinen liste gösteriliyor.)", Foreground = (Brush)FindResource("B.Warning"), Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
            status.Children.Add(new TextBlock
            {
                Text = um.LastCheckUtc is { } last ? "Son denetim: " + last.ToLocalTime().ToString("d MMMM yyyy HH:mm", CultureInfo.GetCultureInfo("tr-TR")) : "Henüz denetlenmedi.",
                Style = (Style)FindResource("Text.Caption"),
                Margin = new Thickness(0, 0, 0, 14)
            });

            var buttons = new WrapPanel();
            if (um.Available != null)
            {
                var available = um.Available;
                buttons.Children.Add(ActionButton(info.CanInstallInPlace ? "Şimdi güncelle" : "İndir ve kurulumu aç", () => UpdateFlow.Install(Window.GetWindow(this), available), primary: true));
                buttons.Children.Add(ActionButton("Sürüm notları", () => ScrollToRelease(available.Tag)));
                buttons.Children.Add(ActionButton("Bu sürümü atla", () => um.Skip(available), tip: "Bu sürüm bir daha önerilmez; daha yenisi çıkınca yine haber verilir."));
            }

            var check = ActionButton(um.IsChecking ? "Denetleniyor…" : "Güncellemeleri denetle", () => _ = um.CheckAsync(), primary: um.Available == null);
            check.IsEnabled = !um.IsChecking;
            buttons.Children.Add(check);
            status.Children.Add(buttons);

            status.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("B.Border"), Margin = new Thickness(0, 10, 0, 14) });
            status.Children.Add(Switch("Açılışta yeni sürümü denetle (günde en çok bir kez)", s.AutoCheckUpdates, v => s.AutoCheckUpdates = v));
            status.Children.Add(Switch("Ön sürümleri de öner (beta, rc)", s.IncludePrereleases, v =>
            {
                s.IncludePrereleases = v;
                um.Refresh();
            }));
            if (!string.IsNullOrEmpty(s.SkippedUpdateVersion))
            {
                var skip = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var undo = new Button { Content = "Yine sor", Style = (Style)FindResource("Btn.Ghost") };
                undo.Click += (_, _) => um.ClearSkip();
                DockPanel.SetDock(undo, Dock.Right);
                skip.Children.Add(undo);
                skip.Children.Add(new TextBlock { Text = $"{s.SkippedUpdateVersion} sürümü atlandı; önerilmeyecek.", Style = (Style)FindResource("Text.Caption"), VerticalAlignment = VerticalAlignment.Center });
                status.Children.Add(skip);
            }

            if (!info.CanInstallInPlace)
            {
                status.Children.Add(new TextBlock
                {
                    Text = "Bu kopya kurulum programıyla kurulmamış (geliştirme derlemesi). Güncellemeler yerinde kurulmaz: kurulum dosyası indirilir ve sihirbazı açılır.",
                    Style = (Style)FindResource("Text.Caption"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }

            root.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = status, Margin = new Thickness(0, 0, 0, 30) });

            // ---- every release
            var versionsHeader = new TextBlock { Text = "Tüm sürümler", Style = (Style)FindResource("Text.Subtitle"), FontSize = 16, Margin = new Thickness(0, 0, 0, 4) };
            root.Children.Add(versionsHeader);
            root.Children.Add(new TextBlock
            {
                Text = "Eski bir sürümü kurmak şu anki sürümün yerine geçer. Projeleriniz, dünyalarınız, ayarlarınız ve kütüphaneniz silinmez; " +
                       "ancak yeni sürümde kaydedilen dosyalar eski sürümde açılmayabilir.",
                Style = (Style)FindResource("Text.Body"),
                Margin = new Thickness(0, 0, 0, 14)
            });

            var list = um.Releases.Where(r => !r.IsDraft).ToList();
            if (list.Count == 0)
            {
                root.Children.Add(new TextBlock { Text = um.IsChecking ? "Sürüm listesi alınıyor…" : "Sürüm listesi yok. İnternete bağlıyken \"Güncellemeleri denetle\"ye basın.", Style = (Style)FindResource("Text.Body") });
            }

            FrameworkElement? focusCard = focus == "versions" ? versionsHeader : null;
            string? focusTag = focus != null && focus.StartsWith("release:", StringComparison.Ordinal) ? focus.Substring(8) : null;
            if (focusTag != null) _expandedNotes.Add("rel:" + focusTag);
            foreach (var r in list)
            {
                var card = ReleaseCard(r, info, um);
                root.Children.Add(card);
                if (r.Tag == focusTag) focusCard = card;
            }

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
            _updatesScroll = scroll;
            ContentHost.Content = scroll;
            if (focusCard != null)
            {
                var target = focusCard;
                Dispatcher.BeginInvoke(new Action(() => target.BringIntoView(new Rect(0, 0, 1, Math.Max(1, scroll.ViewportHeight - 40)))), DispatcherPriority.Loaded);
            }
        }

        private void ScrollToRelease(string tag)
        {
            if (_current == _updatesTopic) ShowUpdates("release:" + tag);
            else Open(_updatesTopic, "release:" + tag);
        }

        private FrameworkElement ReleaseCard(ReleaseInfo r, InstallInfo info, UpdateManager um)
        {
            var sp = new StackPanel();
            var head = new WrapPanel();
            head.Children.Add(new TextBlock { Text = r.Title, Style = (Style)FindResource("Text.Subtitle"), FontSize = 15, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            int cmp = r.Version?.CompareTo(um.CurrentVersion) ?? -1;
            if (cmp == 0) head.Children.Add(Pill("Yüklü", "B.Success"));
            else if (r == um.Available) head.Children.Add(Pill("Yeni", "B.Accent"));
            if (r.IsPrerelease) head.Children.Add(Pill("Ön sürüm", "B.Warning"));
            sp.Children.Add(head);

            var asset = UpdateLogic.SelectAsset(r, info.SelfContained, info.RuntimeAvailable);
            string date = r.PublishedAt?.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR")) ?? string.Empty;
            string assetText = asset != null ? asset.Name + " · " + UpdateLogic.FormatSize(asset.Size) : "Bu kopya için kurulum dosyası yok";
            sp.Children.Add(new TextBlock { Text = r.DisplayVersion + (date.Length > 0 ? " · " + date : string.Empty) + " · " + assetText, Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap });

            var actions = new WrapPanel();
            if (asset != null)
            {
                string label = cmp > 0 ? "Güncelle" : cmp == 0 ? "Yeniden kur" : "Bu sürümü kur";
                if (!info.CanInstallInPlace) label = "İndir ve kurulumu aç";
                var install = ActionButton(label, () => UpdateFlow.Install(Window.GetWindow(this), r), primary: r == um.Available);
                install.Padding = new Thickness(12, 4, 12, 4);
                actions.Children.Add(install);
                var download = ActionButton("İndir…", () => UpdateFlow.Download(Window.GetWindow(this), r, asset), tip: "Kurulum dosyasını İndirilenler klasörüne kaydeder (doğrulanır, kurulmaz).");
                download.Padding = new Thickness(12, 4, 12, 4);
                actions.Children.Add(download);
                var other = r.Assets.Where(a => a.IsInstaller && a != asset).ToList();
                foreach (var o in other)
                {
                    var alt = ActionButton(o.IsSelfContained ? ".NET dahil paketi indir…" : "Lite paketi indir (.NET 9 gerekir)…", () => UpdateFlow.Download(Window.GetWindow(this), r, o),
                        tip: o.Name + " · " + UpdateLogic.FormatSize(o.Size));
                    alt.Padding = new Thickness(12, 4, 12, 4);
                    alt.Style = (Style)FindResource("Btn.Ghost");
                    actions.Children.Add(alt);
                }
            }

            string key = "rel:" + r.Tag;
            bool open = _expandedNotes.Contains(key);
            var notesHost = new ContentControl();
            var toggle = ActionButton(open ? "Notları gizle" : "Sürüm notları", () => { }, tip: null);
            toggle.Style = (Style)FindResource("Btn.Ghost");
            toggle.Padding = new Thickness(12, 4, 12, 4);
            void SetNotes(bool show)
            {
                toggle.Content = show ? "Notları gizle" : "Sürüm notları";
                if (!show)
                {
                    notesHost.Content = null;
                    return;
                }

                var ctx = new MarkdownContext { HeadingOffset = 1, OnLink = url => HandleLink(url, null) };
                var viewer = MarkdownView.InlineViewer(MarkdownView.Render(MarkdownParser.Parse(r.Body.Trim().Length > 0 ? r.Body : "_Bu sürüm için not yazılmamış._"), ctx));
                notesHost.Content = new Border { BorderBrush = (Brush)FindResource("B.Border"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(0, 12, 0, 0), Child = viewer };
            }

            toggle.Click += (_, _) =>
            {
                bool show = !_expandedNotes.Contains(key);
                if (show) _expandedNotes.Add(key);
                else _expandedNotes.Remove(key);
                SetNotes(show);
            };
            actions.Children.Add(toggle);
            if (r.HtmlUrl.Length > 0)
            {
                var gh = ActionButton("GitHub'da aç", () => OpenUrl(r.HtmlUrl));
                gh.Style = (Style)FindResource("Btn.Ghost");
                gh.Padding = new Thickness(12, 4, 12, 4);
                actions.Children.Add(gh);
            }

            sp.Children.Add(actions);
            sp.Children.Add(notesHost);
            if (open) SetNotes(true);
            return new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 12), Child = sp, Tag = r.Tag, BorderBrush = (Brush)FindResource(r == um.Available ? "B.BorderStrong" : "B.Border") };
        }

        private Border Pill(string text, string brush) => new Border
        {
            Style = (Style)FindResource("Badge"),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 12, Foreground = (Brush)FindResource(brush) }
        };

        private FrameworkElement Switch(string label, bool value, Action<bool> set)
        {
            var cb = new CheckBox { Content = label, IsChecked = value, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 10) };
            cb.Click += (_, _) =>
            {
                set(cb.IsChecked == true);
                AppSettings.Current.Save();
            };
            return cb;
        }
    }
}
