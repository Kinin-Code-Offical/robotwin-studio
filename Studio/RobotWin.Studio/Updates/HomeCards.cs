using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotWin.Studio.Theme;

namespace RobotWin.Studio.Updates
{
    /// <summary>Start page notice: "a new version is available" with Şimdi güncelle / Sürüm notları / Daha sonra.</summary>
    public sealed class UpdateNotice : Border
    {
        private readonly Action<string> _openHelp;

        public UpdateNotice(Action<string> openHelp)
        {
            _openHelp = openHelp;
            Margin = new Thickness(0, 22, 0, 0);
            Padding = new Thickness(16, 14, 16, 14);
            CornerRadius = (CornerRadius)Application.Current.FindResource("R.Large");
            BorderThickness = new Thickness(1);
            BorderBrush = R<Brush>("B.BorderStrong");
            Background = R<Brush>("B.AccentSoft");
            UpdateManager.Current.Changed += (_, _) => Build();
            Build();
        }

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        private void Build()
        {
            var um = UpdateManager.Current;
            var r = um.Available;
            Visibility = um.ShowNotice ? Visibility.Visible : Visibility.Collapsed;
            if (r == null)
            {
                Child = null;
                return;
            }

            var grid = new DockPanel();
            var icon = new Icon { Data = R<Geometry>("I.Import"), Size = 20, Foreground = R<Brush>("B.Accent"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 14, 0) };
            grid.Children.Add(icon);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = $"Yeni sürüm: RobotWin Studio {r.DisplayVersion}", Foreground = R<Brush>("B.Text"), FontSize = 14, FontWeight = FontWeights.SemiBold });
            var asset = UpdateLogic.SelectAsset(r, um.Install.SelfContained, um.Install.RuntimeAvailable);
            string date = r.PublishedAt?.ToLocalTime().ToString("d MMMM yyyy", RobotWin.Studio.Shell.Lang.Culture) ?? string.Empty;
            string detail = $"Şu anki sürüm {um.CurrentVersion}" + (date.Length > 0 ? " · " + date : string.Empty) + (asset != null ? " · " + UpdateLogic.FormatSize(asset.Size) : string.Empty) +
                            ". Projeleriniz ve ayarlarınız korunur.";
            sp.Children.Add(new TextBlock { Text = detail, Style = R<Style>("Text.Caption"), Foreground = R<Brush>("B.TextSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 12) });
            var buttons = new WrapPanel();
            var now = new Button { Content = um.Install.CanInstallInPlace ? "Şimdi güncelle" : "İndir", Style = R<Style>("Btn.Primary"), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
            now.Click += (_, _) => UpdateFlow.Install(Window.GetWindow(this), r);
            buttons.Children.Add(now);
            var notes = new Button { Content = "Sürüm notları", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
            notes.Click += (_, _) => _openHelp("news:" + NewsParser.ReleaseNewsId(r));
            buttons.Children.Add(notes);
            var later = new Button { Content = "Daha sonra", Style = R<Style>("Btn.Ghost"), Padding = new Thickness(10, 5, 10, 5) };
            later.Click += (_, _) => um.Dismiss();
            buttons.Children.Add(later);
            var skip = new Button { Content = "Bu sürümü atla", Style = R<Style>("Btn.Ghost"), Padding = new Thickness(10, 5, 10, 5), ToolTip = "Bu sürüm bir daha önerilmez; daha yenisi çıkınca yine haber verilir." };
            skip.Click += (_, _) => um.Skip(r);
            buttons.Children.Add(skip);
            sp.Children.Add(buttons);
            grid.Children.Add(sp);
            Child = grid;
        }
    }

    /// <summary>
    /// Start page, first start after an update: "Bu sürümde yenilikler" with the headlines of this version's section of
    /// the bundled guide page 11-yenilikler.md, a button to the whole page and one to put the card away until the next
    /// update. A first installation records its version without showing it.
    /// </summary>
    public sealed class WhatsNewNotice : Border
    {
        public static string GuidePage => Help.HelpLibrary.WhatsNewPage;

        public WhatsNewNotice(Action<string> openHelp)
        {
            Margin = new Thickness(0, 22, 0, 0);
            Padding = new Thickness(16, 14, 16, 14);
            CornerRadius = (CornerRadius)Application.Current.FindResource("R.Large");
            BorderThickness = new Thickness(1);
            BorderBrush = R<Brush>("B.BorderStrong");
            Background = R<Brush>("B.Surface");
            Visibility = Visibility.Collapsed;

            var settings = Shell.AppSettings.Current;
            string version = InstallEnvironment.CurrentVersion.ToString();
            if (string.IsNullOrEmpty(settings.LastRunVersion))
            {
                settings.LastRunVersion = version;
                settings.Save();
                return;
            }

            if (string.Equals(settings.LastRunVersion, version, StringComparison.Ordinal)) return;
            var (summary, headlines) = WhatsNew.Headlines(version, System.IO.Path.Combine(AppContext.BaseDirectory, "Docs", Help.HelpLibrary.GuideFolder.Replace('/', System.IO.Path.DirectorySeparatorChar), GuidePage));
            if (summary == null && headlines.Count == 0) return;

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = $"RobotWin Studio {version} kuruldu: bu sürümde yenilikler", Foreground = R<Brush>("B.Text"), FontSize = 14, FontWeight = FontWeights.SemiBold });
            if (summary != null)
                sp.Children.Add(new TextBlock { Text = summary, Style = R<Style>("Text.Caption"), Foreground = R<Brush>("B.TextSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) });
            foreach (var h in headlines)
                sp.Children.Add(new TextBlock { Text = "• " + h, Foreground = R<Brush>("B.Text"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var all = new Button { Content = "Tüm yenilikler", Style = R<Style>("Btn.Primary"), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
            all.Click += (_, _) => openHelp(GuidePage);
            buttons.Children.Add(all);
            var close = new Button { Content = "Kapat", Style = R<Style>("Btn.Ghost"), Padding = new Thickness(10, 5, 10, 5) };
            close.Click += (_, _) =>
            {
                settings.LastRunVersion = version;
                settings.Save();
                Visibility = Visibility.Collapsed;
            };
            buttons.Children.Add(close);
            sp.Children.Add(buttons);
            Child = sp;
            Visibility = Visibility.Visible;
        }

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);
    }

    /// <summary>Start page: the latest news items (unread ones marked) and a link to all of them.</summary>
    public sealed class HomeNews : StackPanel
    {
        private readonly Action<string> _openHelp;

        public HomeNews(Action<string> openHelp)
        {
            _openHelp = openHelp;
            Margin = new Thickness(0, 0, 0, 32);
            UpdateManager.Current.Changed += (_, _) => Build();
            Build();
        }

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        private void Build()
        {
            Children.Clear();
            var um = UpdateManager.Current;
            var items = um.News.Take(3).ToList();
            Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (items.Count == 0) return;
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var all = new Button { Content = "Tümü", Style = R<Style>("Btn.Ghost"), Padding = new Thickness(8, 2, 8, 2), MinHeight = 0, FontSize = 12, Margin = new Thickness(0, -6, -8, 0) };
            all.Click += (_, _) => _openHelp("news");
            DockPanel.SetDock(all, Dock.Right);
            head.Children.Add(all);
            int unread = um.UnreadCount;
            head.Children.Add(new TextBlock { Text = unread > 0 ? $"Haberler · {unread} yeni" : "Haberler", Style = R<Style>("Text.Overline") });
            Children.Add(head);
            foreach (var n in items)
            {
                bool isNew = um.IsUnread(n);
                var row = new DockPanel();
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = isNew ? R<Brush>("B.Accent") : Brushes.Transparent, Margin = new Thickness(0, 7, 8, 0), VerticalAlignment = VerticalAlignment.Top };
                row.Children.Add(dot);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = n.Title, Foreground = R<Brush>(isNew ? "B.Text" : "B.TextSecondary"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 270 });
                string date = n.Date == DateTimeOffset.MinValue ? string.Empty : n.Date.ToLocalTime().ToString("d MMMM yyyy", RobotWin.Studio.Shell.Lang.Culture);
                text.Children.Add(new TextBlock { Text = date + (n.IsRelease ? " · sürüm notları" : " · duyuru"), Style = R<Style>("Text.Caption") });
                row.Children.Add(text);
                var b = new Button
                {
                    Style = R<Style>("Btn.Ghost"),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(-8, 0, 0, 2),
                    Content = row,
                    Cursor = Cursors.Hand
                };
                string id = n.Id;
                b.Click += (_, _) => _openHelp("news:" + id);
                Children.Add(b);
            }
        }
    }
}
