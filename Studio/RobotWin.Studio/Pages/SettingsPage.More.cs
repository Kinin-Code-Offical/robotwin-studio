using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>[settings] Backups, keyboard shortcuts and the advanced section.</summary>
    public partial class SettingsPage
    {
        // ================================================================ backups

        private void BackupCards()
        {
            Card("Otomatik yedek", "Kaydedilmemiş sekmelerin kopyası düzenli aralıklarla yedek klasörüne yazılır; asıl dosyanız değiştirilmez.", c =>
            {
                c.Children.Add(Choice("Sıklık", new[] { ("Kapalı", 0), ("Her dakika", 1), ("Her 2 dakikada", 2), ("Her 5 dakikada", 5), ("Her 10 dakikada", 10), ("Her 30 dakikada", 30) },
                    S.AutosaveMinutes, v => S.AutosaveMinutes = v));
                c.Children.Add(Toggle("Kaydederken önceki sürümü sakla", S.BackupOnSave, v => S.BackupOnSave = v,
                    "Bir dosyanın üstüne her kaydedişte eski hâli 'ad-önceki-tarih' adıyla yedek klasörüne kopyalanır: yanlışlıkla kaydedilen bir değişiklikten geri dönebilirsiniz."));
                c.Children.Add(Toggle("Çökme sonrası kurtarmayı öner", S.CrashRecovery, v => S.CrashRecovery = v,
                    "Uygulama beklenmedik biçimde kapanırsa (çökme, elektrik kesintisi) bir sonraki açılışta başlangıç sayfası o çalışmada alınan yedekleri gösterir."));
                var now = new Button { Content = "Şimdi yedekle", Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left };
                now.Click += (_, _) => (Window.GetWindow(this) as MainWindow)?.AutosaveNow();
                c.Children.Add(now);
            });

            Card("Yedek klasörü ve saklama", "Yedeklerin yeri, her belge için kaç kopya tutulacağı ve klasörün en fazla ne kadar yer kaplayacağı.", c =>
            {
                c.Children.Add(Text("Yedek klasörü", S.BackupFolder, v => S.BackupFolder = v, "Boş bırakılırsa proje klasöründeki Yedekler kullanılır. Şu an: " + Unattended.Display(Backups.Folder)));
                c.Children.Add(Choice("Belge başına kopya", new[] { ("3", 3), ("5", 5), ("10", 10), ("20", 20), ("50", 50) }, S.BackupKeepPerFile, v => S.BackupKeepPerFile = v,
                    "Otomatik yedekler ve önceki sürümler için ayrı ayrı sayılır; sınırı aşınca en eskileri silinir."));
                c.Children.Add(Choice("Klasörün boyut sınırı", new[] { ("100 MB", 100), ("250 MB", 250), ("500 MB", 500), ("1 GB", 1000), ("2 GB", 2000) }, S.BackupMaxMb, v => S.BackupMaxMb = v,
                    "Sınır aşılınca hangi belgeye ait olursa olsun en eski kopyalar silinir."));
                var usage = new TextBlock { Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 2, 0, 10) };
                void ShowUsage()
                {
                    var (bytes, count) = Backups.Usage();
                    usage.Text = $"Klasörde {count} dosya, {bytes / 1048576.0:0.0} MB.";
                }

                ShowUsage();
                c.Children.Add(usage);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var open = new Button { Content = "Klasörü aç" };
                open.Click += (_, _) => OpenFolder(Backups.Folder);
                var prune = new Button { Content = "Sınırlara göre şimdi temizle", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(8, 0, 0, 0) };
                prune.Click += (_, _) =>
                {
                    Backups.Prune();
                    ShowUsage();
                    Workspace.Current.Status = "Yedek klasörü temizlendi: " + usage.Text;
                };
                row.Children.Add(open);
                row.Children.Add(prune);
                c.Children.Add(row);
            });
        }

        private static void OpenFolder(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                Workspace.Current.Status = "Klasör açılamadı: " + ex.Message;
            }
        }

        // ================================================================ keyboard shortcuts

        private void ShortcutCards()
        {
            foreach (var group in Shortcuts.All.GroupBy(c => c.Group).OrderBy(g => GroupOrder(g.Key)))
            {
                Card(group.Key, GroupNote(group.Key), c =>
                {
                    foreach (var cmd in group) c.Children.Add(ShortcutRow(cmd));
                });
            }

            Card("Diğer sayfa kısayolları", "Bu tuşlar yalnız kendi sayfalarında çalışır ve şimdilik değiştirilemez (Devre, PCB, Dünya, Kod ve Parça sayfalarınınkiler yukarıda değiştirilebilir).", c =>
            {
                foreach (var (keys, what) in new[]
                {
                    ("F5 · Shift+F5", "Simülasyon: başlat / duraklat · durdur"), ("Ctrl+Z · Ctrl+Y", "Geri al · yinele"),

                    ("1 / 2 / 3", "Parça: nokta / kenar / yüz seçimi"), ("F10 · Alt", "Menü çubuğu"),
                })
                {
                    var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                    g.ColumnDefinitions.Add(new ColumnDefinition());
                    g.Children.Add(new TextBlock { Text = keys, FontFamily = (FontFamily)FindResource("F.Mono"), Foreground = (Brush)FindResource("B.TextSecondary") });
                    var t = new TextBlock { Text = what, TextWrapping = TextWrapping.Wrap };
                    Grid.SetColumn(t, 1);
                    g.Children.Add(t);
                    c.Children.Add(g);
                }
            });

            Card("Kısayolları sıfırla", "Değiştirdiğiniz bütün tuşlar ilk hâline döner.", c =>
            {
                var reset = new Button { Content = "Tüm kısayolları varsayılana döndür", Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left };
                reset.Click += (_, _) =>
                {
                    Shortcuts.ResetAll();
                    Build();
                    Workspace.Current.Status = "Kısayollar varsayılana döndü";
                };
                c.Children.Add(reset);
            });
        }

        private static string GroupNote(string group) => group switch
        {
            "Dosya" => "Yeni belge, açma ve kaydetme.",
            "Sekmeler" => "Açık sekmeler arasında.",
            "Sayfalar" => "Sol şeritteki sayfalara geçiş.",
            "Görünüm" => "Pencere düzeni ve göstergeler.",
            "Uygulama" => "Ayarlar ve komut paleti.",
            "Devre sayfası" => "Şema düzenleyicinin araçları; yalnız şema odaktayken çalışır, tek harf olabilir.",
            "PCB sayfası" => "Kart düzenleyicinin araçları; yalnız kart odaktayken çalışır, tek harf olabilir.",
            "Dünya sayfası" => "Dünya düzenleyicinin araçları; bir yazı kutusundayken çalışmaz, tek harf olabilir.",
            "Kod sayfası" => "Derleme, yükleme ve hata ayıklama; kod yazarken de çalıştığı için Ctrl/Alt/Shift ya da F tuşlarıyla.",
            "Parça sayfası" => "Modelleyicinin araçları; bir yazı kutusundayken çalışmaz, tek harf olabilir.",
            _ => "Kılavuz ve yardım.",
        };

        private static int GroupOrder(string group) => group switch
        {
            "Dosya" => 0,
            "Sekmeler" => 1,
            "Sayfalar" => 2,
            "Görünüm" => 3,
            "Uygulama" => 4,
            "Devre sayfası" => 6,
            "PCB sayfası" => 7,
            "Dünya sayfası" => 8,
            "Kod sayfası" => 9,
            "Parça sayfası" => 10,
            _ => 5,
        };

        private static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

        /// <summary>One shortcut: its name, its keys (a click captures new ones), a note when they clash, and its reset.</summary>
        private FrameworkElement ShortcutRow(ShortcutCommand cmd)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = cmd.Title, Style = (Style)FindResource("Text.Body") });
            var clashes = Shortcuts.Clashes(cmd).ToList();
            if (clashes.Count > 0)
            {
                name.Children.Add(new TextBlock
                {
                    Text = "Çakışıyor: " + string.Join(", ", clashes.Select(x => x.Title)),
                    Foreground = (Brush)FindResource("B.Warning"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                });
            }

            g.Children.Add(name);
            string Shown() => cmd.Keys.Length == 0 ? "(tuş yok)" : cmd.Keys;
            var keys = new Button { Content = Shown(), FontFamily = (FontFamily)FindResource("F.Mono"), HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = "Değiştirmek için tıklayın" };
            bool capturing = false;
            keys.Click += (_, _) =>
            {
                capturing = true;
                keys.Content = "Tuşlara basın…";
                keys.Focus();
            };
            keys.LostKeyboardFocus += (_, _) =>
            {
                if (!capturing) return;
                capturing = false;
                keys.Content = Shown();
            };
            keys.PreviewKeyDown += (_, e) =>
            {
                if (!capturing) return;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                e.Handled = true;   // the window's own shortcuts stay quiet while new keys are taken
                if (IsModifier(key)) return;
                capturing = false;
                if (key == Key.Escape)
                {
                    keys.Content = Shown();
                    return;
                }

                if (key == Key.Back)
                {
                    Shortcuts.Set(cmd.Id, string.Empty);
                    Build();
                    Workspace.Current.Status = $"{cmd.Title}: tuşu kaldırıldı";
                    return;
                }

                var mods = Keyboard.Modifiers;
                bool function = key >= Key.F1 && key <= Key.F24;
                if (mods == ModifierKeys.None && !function && (cmd.Page == null || cmd.Page == PageShortcuts.Code))   // a page's own keys may be single letters (not the code page's: they would go into the code)
                {
                    keys.Content = Shown();
                    Workspace.Current.Status = "Tek bir tuş pencere kısayolu olamaz (yazarken çalışırdı): Ctrl, Alt ya da Shift ile birlikte basın; F1–F12 tek başına olur.";
                    return;
                }

                string text = Shortcuts.Format(key, mods);
                Shortcuts.Set(cmd.Id, text);
                Build();
                var clash = Shortcuts.Clashes(cmd).FirstOrDefault();
                Workspace.Current.Status = clash == null ? $"{cmd.Title}: {text}" : $"{cmd.Title}: {text} — {clash.Title} ile çakışıyor; ikisinden birini değiştirin.";
            };
            Grid.SetColumn(keys, 1);
            g.Children.Add(keys);
            if (cmd.IsChanged)
            {
                var reset = new Button { Content = "Varsayılan", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Varsayılan: " + (cmd.Defaults.Length == 0 ? "(yok)" : cmd.Defaults) };
                reset.Click += (_, _) =>
                {
                    Shortcuts.Reset(cmd.Id);
                    Build();
                };
                Grid.SetColumn(reset, 2);
                g.Children.Add(reset);
            }

            return g;
        }

        /// <summary>
        /// --snapshot --page settings --fault cat:Klavye kısayolları | search:yedek | theme:siyah:turuncu (in memory only).
        /// </summary>
        internal void PrepareSnapshot(string fault)
        {
            foreach (var part in fault.Split('|'))
            {
                if (part.StartsWith("cat:", StringComparison.Ordinal)) ShowCategory(part.Substring(4));
                else if (part.StartsWith("search:", StringComparison.Ordinal)) SearchBox.Text = part.Substring(7);
                else if (part.StartsWith("theme:", StringComparison.Ordinal))
                {
                    var t = part.Substring(6).Split(':');
                    Themes.Apply(t[0], t.Length > 1 ? t[1] : "mavi");
                }
            }
        }

        // ================================================================ advanced

        private void AdvancedCards()
        {
            Card("Ayarları taşı", "Bütün tercihlerinizi (tema, kısayollar, yedekleme dahil) bir dosyaya yazın ya da başka bir bilgisayardan alın.", c =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var export = new Button { Content = "Dışa aktar…" };
                export.Click += (_, _) => ExportSettings();
                var import = new Button { Content = "İçe aktar…", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(8, 0, 0, 0) };
                import.Click += (_, _) => ImportSettings();
                row.Children.Add(export);
                row.Children.Add(import);
                c.Children.Add(row);
            });

            Card("Tanılama", "Bir sorunu bildirirken işe yarayan dosyalar.", c =>
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio");
                string log = Path.Combine(dir, "crash.log");
                c.Children.Add(new TextBlock
                {
                    Text = File.Exists(log) ? $"Hata günlüğü: {new FileInfo(log).Length / 1024.0:0.0} KB, son kayıt {File.GetLastWriteTime(log):dd.MM.yyyy HH:mm}." : "Hata günlüğü boş: kaydedilmiş bir hata yok.",
                    Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap,
                });
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var folder = new Button { Content = "Ayar ve günlük klasörünü aç" };
                folder.Click += (_, _) => OpenFolder(dir);
                row.Children.Add(folder);
                c.Children.Add(row);
            });
        }

        private void ExportSettings()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Ayarları dışa aktar", FileName = "robotwin-ayarlar.json", Filter = "Ayar dosyası (*.json)|*.json" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(S, new JsonSerializerOptions { WriteIndented = true }));
                Workspace.Current.Status = "Ayarlar dışa aktarıldı: " + dlg.FileName;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Workspace.Current.Status = "Dışa aktarılamadı: " + ex.Message;
            }
        }

        private void ImportSettings()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Ayarları içe aktar", Filter = "Ayar dosyası (*.json)|*.json" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var other = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(dlg.FileName));
                if (other == null) throw new JsonException("boş dosya");
                S.CopyFrom(other);
                S.Save();
                Themes.Apply();
                Shortcuts.Reapply();
                (Window.GetWindow(this) as MainWindow)?.ApplyUiScale(S.UiScale);
                Build();
                Workspace.Current.Status = "Ayarlar içe aktarıldı (pencere yerleşimi bu bilgisayardaki gibi kaldı)";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is NotSupportedException)
            {
                Workspace.Current.Status = "İçe aktarılamadı: " + ex.Message;
            }
        }
    }
}
