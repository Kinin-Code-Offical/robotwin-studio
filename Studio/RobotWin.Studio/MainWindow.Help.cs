using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotWin.Studio.Pages;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Updates;

namespace RobotWin.Studio
{
    /// <summary>
    /// Help, news and updates in the shell: the Yardım page (F1, rail, menu), the update notice in the
    /// status bar and on the start page, and the background update check at start-up.
    /// </summary>
    public partial class MainWindow
    {
        private HelpPage? _help;
        private Button? _updateBadge;

        private void SetupHelp()
        {
            var updates = UpdateManager.Current;
            _help = new HelpPage(ShortcutList);
            _pages["help"] = _help;
            _pageNames["help"] = "Yardım";
            Command("help.show", "Yardım", "Kılavuzu aç", "F1", () => ShowHelp(null));

            // Start page: update notice under the buttons, news at the top of the right column.
            _home.UpdateHost.Content = new StackPanel { Children = { new WhatsNewNotice(ShowHelp), new UpdateNotice(ShowHelp) } };
            _home.NewsHost.Content = new HomeNews(ShowHelp);

            // Status bar: a quiet "Güncelleme var" link next to the product name.
            if (StatusBar.Child is Grid g && g.Children.OfType<StackPanel>().LastOrDefault() is { } right)
            {
                _updateBadge = new Button
                {
                    Style = (Style)FindResource("Btn.Ghost"),
                    Padding = new Thickness(8, 0, 8, 0),
                    MinHeight = 0,
                    Height = 20,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 12, 0),
                    Foreground = (Brush)FindResource("B.Accent"),
                    Visibility = Visibility.Collapsed
                };
                _updateBadge.Click += (_, _) =>
                {
                    var menu = UpdateFlow.NoticeMenu(() => ShowHelp("news:" + (updates.Available != null ? NewsParser.ReleaseNewsId(updates.Available) : string.Empty)));
                    menu.PlacementTarget = _updateBadge;
                    menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                    menu.IsOpen = true;
                };
                right.Children.Insert(0, _updateBadge);
            }

            updates.Changed += (_, _) => UpdateHelpBadges();
            UpdateHelpBadges();

            // Background check a few seconds after start (never for snapshots, benchmarks or self-tests).
            if (!Unattended.Active)
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    updates.StartupCheck();
                };
                timer.Start();
            }
        }

        private void UpdateHelpBadges()
        {
            var um = UpdateManager.Current;
            if (_updateBadge != null)
            {
                _updateBadge.Visibility = um.ShowNotice ? Visibility.Visible : Visibility.Collapsed;
                _updateBadge.Content = um.Available != null ? "Güncelleme var: " + um.Available.DisplayVersion : string.Empty;
                _updateBadge.ToolTip = "Yeni sürümü kur, notlarını oku ya da ertele";
            }

            Rail.SetToolBadge("help", um.Available != null || um.UnreadCount > 0);
        }

        /// <summary>Opens Yardım at a section ("shortcuts", "news", "news:&lt;id&gt;", "updates", "versions", "about"; null = guide).</summary>
        public void ShowHelp(string? section)
        {
            Navigate("help");
            if (section != null) _help?.Show(section);
        }

        /// <summary>Asks to save every open document (as on closing). False if the user cancelled.</summary>
        public bool ConfirmCloseAll()
        {
            foreach (var doc in W.Documents.Where(d => !d.IsEmbedded).ToList())
            {
                if (!ConfirmDiscard(doc)) return false;
            }

            return true;
        }

        private IEnumerable<PaletteCommand> HelpPaletteCommands()
        {
            const string help = "Yardım";
            yield return new PaletteCommand(help, "Yardım ve belgeler", () => ShowHelp(null), "F1");
            yield return new PaletteCommand(help, "Doğrulama raporu", () => ShowHelp("12-dogrulama.md"));   // [validation] every solver against theory
            yield return new PaletteCommand(help, "Klavye kısayolları", () => ShowHelp("shortcuts"));
            yield return new PaletteCommand(help, "Haberler", () => ShowHelp("news"));
            yield return new PaletteCommand(help, "Güncellemeleri denetle", CheckForUpdates);
            yield return new PaletteCommand(help, "Eski sürümler", () => ShowHelp("versions"));
            yield return new PaletteCommand(help, "Hakkında", () => ShowHelp("about"));
        }

        private void CheckForUpdates()
        {
            ShowHelp("updates");
            _ = UpdateManager.Current.CheckAsync();
        }

        private static readonly Regex KeyGesture = new Regex(@"^((Ctrl|Shift|Alt|Win)\+)*(F\d{1,2}|[A-Z0-9]|Del|Esc|Tab|Enter|Boşluk|[↑↓←→,.\\])$|\+", RegexOptions.CultureInvariant);

        /// <summary>Every shortcut the menus and the command palette know, plus the editors' single keys.</summary>
        private IEnumerable<(string Group, string Title, string Keys)> ShortcutList()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<(string, string, string)>();
            void Add(string group, string title, string keys)
            {
                title = title.Replace("_", string.Empty).TrimEnd('…', '.').Trim();
                keys = keys.Trim();
                if (keys.Length == 0 || !KeyGesture.IsMatch(keys)) return;
                if (seen.Add(keys + "\u0001" + title)) list.Add((group, title, keys));
            }

            void Walk(string group, ItemCollection items)
            {
                foreach (var item in items.OfType<MenuItem>())
                {
                    if (item.Header is string h) Add(group, h, item.InputGestureText ?? string.Empty);
                    Walk(group, item.Items);
                }
            }

            foreach (var (name, fill) in new (string, Action<MenuItem>)[]
                     {
                         ("Dosya", FileMenu), ("Düzen", EditMenu), ("Görünüm", ViewMenu), ("Simülasyon", SimulationMenu), ("Pencere", WindowMenu), ("Yardım", HelpMenu)
                     })
            {
                var root = new MenuItem();
                try
                {
                    fill(root);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is NullReferenceException || ex is ArgumentException)
                {
                    continue;
                }

                Walk(name, root.Items);
            }

            foreach (var c in PaletteCommands().Where(c => c.Shortcut.Length > 0)) Add(c.Group == "Git" ? "Görünüm" : c.Group, c.Title, c.Shortcut);
            foreach (var (keys, what) in new[]
                     {
                         ("W", "Devre: kablo çiz"), ("L", "Devre: etiket"), ("R", "Devre ve dünya: döndür"), ("X", "Devre: aynala"), ("Del", "Seçileni sil"),
                         ("K", "Dünya: kablo"), ("B", "Dünya: mekanik bağlantı"), ("Ctrl+D", "Dünya: çoğalt"), ("F7", "Kod: derle"), ("Esc", "Tam ekrandan / araçtan çık")
                     })
            {
                Add("Düzenleyiciler", what, keys);
            }

            return list;
        }
    }
}
