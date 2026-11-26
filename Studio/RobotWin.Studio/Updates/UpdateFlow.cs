using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Updates
{
    /// <summary>
    /// The user-facing update steps: confirm → download (progress, cancel, size + SHA-256 check) →
    /// save open work → start the installer silently into this folder → close; the installer starts the
    /// new version when it is done.
    /// </summary>
    public static class UpdateFlow
    {
        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        /// <summary>Installs <paramref name="release"/> in place of the running copy (newer, same or older).</summary>
        public static void Install(Window? owner, ReleaseInfo release)
        {
            owner ??= Application.Current.MainWindow;
            var um = UpdateManager.Current;
            var info = um.Install;
            var asset = UpdateLogic.SelectAsset(release, info.SelfContained, info.RuntimeAvailable);
            if (asset == null)
            {
                string why = info.SelfContained && !info.RuntimeAvailable
                    ? "Bu sürümün yalnızca .NET 10 gerektiren kurulumu var ve bu bilgisayarda .NET 10 Masaüstü Çalışma Zamanı yok."
                    : "Bu sürümde kurulum dosyası bulunmuyor.";
                if (Dialog.Show(owner, "Kurulamıyor", why + " Sürüm sayfasını açabilirsiniz.", "Sürüm sayfasını aç", "Kapat") == 0) OpenUrl(release.HtmlUrl);
                return;
            }

            int cmp = release.Version?.CompareTo(um.CurrentVersion) ?? -1;
            if (cmp < 0)
            {
                if (Dialog.Show(owner, "Eski sürüm kurulsun mu?",
                        $"RobotWin Studio {release.DisplayVersion}, şu anki sürümden ({um.CurrentVersion}) eski. Kurulursa şu anki sürümün yerine geçer.\n\n" +
                        "Projeleriniz, dünyalarınız, ayarlarınız ve kütüphaneniz silinmez; ancak yeni sürümde kaydedilen dosyalar eski sürümde açılmayabilir. " +
                        "Otomatik denetim açıksa yeni sürüm yeniden önerilir.",
                        "Eski sürümü kur", "Vazgeç") != 0) return;
            }
            else if (cmp == 0)
            {
                if (Dialog.Show(owner, "Yeniden kurulsun mu?", $"RobotWin Studio {release.DisplayVersion} zaten yüklü. Aynı sürüm yeniden kurulur (bozuk dosyaları onarmak için). Projeleriniz etkilenmez.",
                        "Yeniden kur", "Vazgeç") != 0) return;
            }

            var result = DownloadWindow.Run(owner, release, asset, null);
            if (result == null) return;
            if (!result.HashVerified &&
                Dialog.Show(owner, "Doğrulama yapılamadı", "Bu sürüm için SHA-256 değeri yayınlanmamış; yalnızca dosya boyutu denetlendi. Kurulum programı yine de çalıştırılsın mı?",
                    "Çalıştır", "Vazgeç") != 0) return;

            if (!info.CanInstallInPlace)
            {
                // A build folder: do not overwrite it; hand the wizard to the user.
                int choice = Dialog.Show(owner, "Kurulum dosyası hazır",
                    $"{asset.Name} indirildi ve doğrulandı.\n\nBu kopya kurulum programıyla kurulmamış (geliştirme derlemesi), bu yüzden yerinde güncellenmez. Kurulum sihirbazı açılsın mı?",
                    "Sihirbazı aç", "Klasörde göster", "Kapat");
                try
                {
                    if (choice == 0) InstallEnvironment.StartWizard(result.Path);
                    else if (choice == 1) Reveal(result.Path);
                }
                catch (UpdateException ex)
                {
                    Dialog.Show(owner, "Başlatılamadı", ex.Message, "Tamam");
                }

                return;
            }

            if (Dialog.Show(owner, "Güncellemeye hazır",
                    $"RobotWin Studio kapanacak, {release.DisplayVersion} sürümü \"{info.AppDirectory}\" klasörüne kurulacak ve uygulama yeniden açılacak.\n\n" +
                    "Kaydedilmemiş işiniz varsa şimdi kaydetmeniz istenecek. Projeleriniz ve ayarlarınız korunur." +
                    (info.Mode == InstallMode.AllUsers ? "\n\nTüm kullanıcılar için kurulu olduğundan Windows yönetici izni isteyecek." : string.Empty),
                    "Güncelle ve yeniden başlat", "Daha sonra") != 0) return;

            var shell = Application.Current.MainWindow as MainWindow;
            if (shell != null && !shell.ConfirmCloseAll()) return;
            try
            {
                InstallEnvironment.StartSilentInstall(result.Path, info);
            }
            catch (UpdateException ex)
            {
                Dialog.Show(owner, "Güncelleme başlatılamadı", ex.Message, "Tamam");
                return;
            }

            // The installer waits for this process to exit before it copies anything.
            if (shell != null) shell.ForceClose();
            Application.Current.Shutdown();
        }

        /// <summary>Downloads an installer into the Downloads folder (verified, not run) and shows it in Explorer.</summary>
        public static void Download(Window? owner, ReleaseInfo release, ReleaseAsset asset)
        {
            owner ??= Application.Current.MainWindow;
            string folder = DownloadsFolder();
            var result = DownloadWindow.Run(owner, release, asset, folder);
            if (result == null) return;
            string verified = result.HashVerified ? "SHA-256 doğrulandı." : "Bu sürüm için SHA-256 yayınlanmamış; boyut denetlendi.";
            if (Dialog.Show(owner, "İndirildi", $"{asset.Name}\n{folder}\n\n{verified}", "Klasörde göster", "Kapat") == 0) Reveal(result.Path);
        }

        private static string DownloadsFolder()
        {
            // FOLDERID_Downloads has no Environment.SpecialFolder value; it lives next to Documents on every Windows.
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dl = Path.Combine(user, "Downloads");
            return Directory.Exists(dl) ? dl : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        public static void Reveal(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        private static void OpenUrl(string url) => Pages.HelpPage.OpenUrl(url);

        /// <summary>Asks once (from the status bar / home notice): update now, notes, later, skip.</summary>
        public static ContextMenu NoticeMenu(Action showNotes)
        {
            var um = UpdateManager.Current;
            var menu = new ContextMenu();
            var r = um.Available;
            if (r == null) return menu;
            void Add(string text, Action a)
            {
                var item = new MenuItem { Header = text };
                item.Click += (_, _) => a();
                menu.Items.Add(item);
            }

            Add($"Şimdi güncelle ({r.DisplayVersion})", () => Install(Application.Current.MainWindow, r));
            Add("Sürüm notları", showNotes);
            Add("Daha sonra", um.Dismiss);
            menu.Items.Add(new Separator());
            Add("Bu sürümü bir daha sorma", () => um.Skip(r));
            return menu;
        }
    }

    /// <summary>Modal progress window for a download: bar, speed, remaining time, cancel; errors in place with "Yeniden dene".</summary>
    internal sealed class DownloadWindow : Window
    {
        private readonly ReleaseInfo _release;
        private readonly ReleaseAsset _asset;
        private readonly string? _folder;
        private readonly TextBlock _title = new TextBlock();
        private readonly TextBlock _status = new TextBlock();
        private readonly Border _bar = new Border();
        private readonly Grid _track = new Grid();
        private readonly Button _cancel = new Button();
        private readonly Button _retry = new Button();
        private CancellationTokenSource? _cts;

        private DownloadWindow(Window? owner, ReleaseInfo release, ReleaseAsset asset, string? folder)
        {
            _release = release;
            _asset = asset;
            _folder = folder;
            Owner = owner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            FontFamily = R<FontFamily>("F.Ui");
            SetResourceReference(ForegroundProperty, "B.Text");   // [theme] its buttons' and texts' ink (Windows' black otherwise)

            var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20), Width = 440 };
            _title.Style = R<Style>("Text.Title");
            _title.FontSize = 18;
            _title.Text = $"RobotWin Studio {release.DisplayVersion} indiriliyor";
            panel.Children.Add(_title);
            panel.Children.Add(new TextBlock { Text = asset.Name + " · " + UpdateLogic.FormatSize(asset.Size), Style = R<Style>("Text.Caption"), Margin = new Thickness(0, 6, 0, 16) });

            _track.Height = 6;
            _track.Background = R<Brush>("B.Raised");
            _bar.Background = R<Brush>("B.Accent");
            _bar.HorizontalAlignment = HorizontalAlignment.Left;
            _bar.Width = 0;
            _track.Children.Add(_bar);
            panel.Children.Add(new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = _track });

            _status.Style = R<Style>("Text.Body");
            _status.Margin = new Thickness(0, 10, 0, 18);
            _status.Text = "Bağlanılıyor…";
            panel.Children.Add(_status);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _retry.Content = "Yeniden dene";
            _retry.Style = R<Style>("Btn.Primary");
            _retry.MinWidth = 96;
            _retry.Visibility = Visibility.Collapsed;
            _retry.Click += (_, _) => Start();
            row.Children.Add(_retry);
            _cancel.Content = "Vazgeç";
            _cancel.MinWidth = 96;
            _cancel.Margin = new Thickness(8, 0, 0, 0);
            _cancel.IsCancel = true;
            _cancel.Click += (_, _) =>
            {
                if (_cts != null) _cts.Cancel();
                else Close();
            };
            row.Children.Add(_cancel);
            panel.Children.Add(row);

            var card = new Border
            {
                Margin = new Thickness(20),
                Background = R<Brush>("B.Overlay"),
                BorderBrush = R<Brush>("B.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Effect = R<System.Windows.Media.Effects.Effect>("E.Popup"),
                Child = panel
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Content = card;
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");
            Loaded += (_, _) => Start();
            Closing += (_, _) => _cts?.Cancel();
        }

        public DownloadResult? Result { get; private set; }

        public static DownloadResult? Run(Window? owner, ReleaseInfo release, ReleaseAsset asset, string? folder)
        {
            var w = new DownloadWindow(owner, release, asset, folder);
            w.ShowDialog();
            return w.Result;
        }

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        private async void Start()
        {
            _retry.Visibility = Visibility.Collapsed;
            _cancel.Content = "Vazgeç";
            _status.Foreground = R<Brush>("B.TextSecondary");
            _status.Text = "Bağlanılıyor…";
            _cts = new CancellationTokenSource();
            var progress = new Progress<DownloadProgress>(p =>
            {
                _bar.Width = _track.ActualWidth * p.Fraction;
                string text = $"{UpdateLogic.FormatSize(p.Received)} / {UpdateLogic.FormatSize(p.Total)}";
                if (p.BytesPerSecond > 1)
                {
                    text += $" · {UpdateLogic.FormatSize((long)p.BytesPerSecond)}/sn";
                    double left = (p.Total - p.Received) / p.BytesPerSecond;
                    if (left >= 1) text += left >= 90 ? $" · ~{Math.Ceiling(left / 60)} dk kaldı" : $" · ~{Math.Ceiling(left)} sn kaldı";
                }

                _status.Text = p.Fraction >= 1 ? "Doğrulanıyor (boyut ve SHA-256)…" : text;
            });
            try
            {
                var result = await UpdateService.DownloadAsync(_release, _asset, _folder, progress, _cts.Token);
                Result = result;
                _cts = null;
                Close();
            }
            catch (OperationCanceledException)
            {
                _cts = null;
                Close();
            }
            catch (UpdateException ex)
            {
                _cts = null;
                _status.Foreground = R<Brush>("B.Danger");
                _status.Text = ex.Message;
                _status.TextWrapping = TextWrapping.Wrap;
                _bar.Width = 0;
                _retry.Visibility = Visibility.Visible;
                _cancel.Content = "Kapat";
            }
        }
    }
}
