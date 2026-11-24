using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotWin.Studio.Branding;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// Preferences, grouped into sections. Every control writes straight to
    /// <see cref="AppSettings"/> and saves; pages pick changes up via <see cref="AppSettings.Changed"/>.
    /// </summary>
    public partial class SettingsPage : UserControl
    {
        private readonly List<(string Title, FrameworkElement Card)> _cards = new List<(string, FrameworkElement)>();
        private bool _building;

        public SettingsPage()
        {
            InitializeComponent();
            AboutLogo.Source = Logo.Image;
            var lib = Workspace.Current.Library;
            AboutText.Text = $"Kendi analog devre çözücüsü (MNA, Newton-Raphson, uyarlamalı adım), {lib.Presets.Count} parçalık katalog, ATmega328P emülasyonu ve ESP32 HAL modeli, " +
                             "şematik editör, çok katmanlı PCB tasarımcısı, Gerber ve KiCad çıktısı, çok gövdeli dünya simülasyonu. " +
                             $".NET {Environment.Version.Major} · WPF · Windows";
            Build();
            BuildFileTypes();
        }

        private static AppSettings S => AppSettings.Current;

        private void Build()
        {
            _building = true;
            Sections.Children.Clear();
            _cards.Clear();

            Card("Görünüm", "Arayüzün boyutu, efektler ve 3D çizim kalitesi.", c =>
            {
                c.Children.Add(Choice("Arayüz ölçeği", new[] { ("%75", 0.75), ("%90", 0.9), ("%100", 1.0), ("%110", 1.1), ("%125", 1.25), ("%150", 1.5) }, S.UiScale, v =>
                {
                    S.UiScale = v;
                    (Window.GetWindow(this) as MainWindow)?.ApplyUiScale(v);
                }, "Yüksek çözünürlüklü ekranlarda veya sunumlarda büyütün."));
                c.Children.Add(Toggle("Parçacık efektleri (duman, kıvılcım, alev)", S.Effects, v => S.Effects = v, "Yanma ve aşırı ısınma görselleri. Kapatmak zayıf ekran kartlarında akıcılığı artırır."));
                c.Children.Add(Toggle("Simülasyon sesleri", S.Sound, v => S.Sound = v, "Motor uğultusu, servo dişli sesi, buzzer, röle tıkırtısı, yanan parçanın patlaması."));
                c.Children.Add(Choice("Ses düzeyi", new[] { ("%25", 0.25), ("%50", 0.5), ("%60", 0.6), ("%80", 0.8), ("%100", 1.0) }, S.Volume, v => S.Volume = v));
                c.Children.Add(Toggle("Akım akışı animasyonu", S.ShowCurrentFlow, v => S.ShowCurrentFlow = v));
                c.Children.Add(Choice("3D kalitesi", new[] { ("Düşük", "low"), ("Orta", "medium"), ("Yüksek", "high") }, S.Quality3D, v => S.Quality3D = v, "Düşük: kenar yumuşatma kapalı, daha az parçacık."));
                c.Children.Add(Toggle("Açılışta başlangıç sayfasını göster", S.ShowHomeOnStart, v => S.ShowHomeOnStart = v, "Kapalıyken son açılan proje veya dünya doğrudan açılır."));
            });

            Card("Simülasyon ve fizik", "Çözücünün doğruluğu ve parçaların gerçek dünyadaki gibi bozulması.", c =>
            {
                c.Children.Add(Toggle("Parça hasarı (yanma, sigorta atması, dielektrik delinme)", S.EnableDamage, v => S.EnableDamage = v,
                    "Kapalıyken parçalar sınırlarının üzerinde zorlansa da bozulmaz; ısınma yine hesaplanır."));
                c.Children.Add(Toggle("Arıza olunca simülasyonu duraklat", S.PauseOnFailure, v => S.PauseOnFailure = v, "Bir parça yandığında zaman durur; neyin neden bozulduğunu inceleyebilirsiniz."));
                c.Children.Add(Number("Ortam sıcaklığı (°C)", S.AmbientTempC, v => S.AmbientTempC = v, -55, 125, "Parçalar bu sıcaklıktan ısınır. Dünyalarda Ortam sekmesindeki değer kullanılır."));
                c.Children.Add(Choice("En büyük zaman adımı", new[] { ("10 µs (hassas)", 1e-5), ("50 µs", 5e-5), ("200 µs (dengeli)", 2e-4), ("1 ms (hızlı)", 1e-3) }, S.MaxStep, v => S.MaxStep = v,
                    "Kenar ve olay anları (PWM, röle, lojik) adım sınırından bağımsız olarak tam zamanında çözülür; bu değer sakin aralıklardaki en uzun adımdır."));
                c.Children.Add(Choice("Newton bağıl toleransı", new[] { ("1e-4 (çok hassas)", 1e-4), ("1e-3 (dengeli)", 1e-3), ("1e-2 (hızlı)", 1e-2) }, S.RelTol, v => S.RelTol = v));
                c.Children.Add(Choice("Başlangıç hızı", new[] { ("0.1×", 0.1), ("0.5×", 0.5), ("Gerçek zaman", 1.0), ("5×", 5.0) }, S.DefaultSpeed, v => S.DefaultSpeed = v));
            });

            Card("Dünya ve düzenleme", "Yeni dünyaların ortamı ve editörlerin ızgarası.", c =>
            {
                c.Children.Add(Choice("Yeni dünyaların yerçekimi", new[] { ("Dünya 9.81 m/s²", 9.81), ("Ay 1.62 m/s²", 1.62), ("Mars 3.71 m/s²", 3.71), ("Sıfır", 0.0) }, S.WorldGravity, v => S.WorldGravity = v));
                c.Children.Add(Choice("Dünya editörü ızgarası", new[] { ("Kapalı", 0.0), ("1 mm", 1.0), ("2.5 mm", 2.5), ("5 mm", 5.0), ("10 mm", 10.0) }, S.WorldGridMm, v => S.WorldGridMm = v,
                    "Nesneler taşınırken bu adıma yaslanır; sürüklerken Alt basılıysa ızgara kapanır."));
                c.Children.Add(Choice("Uzunluk birimi (PCB)", new[] { ("Milimetre", "mm"), ("Mil (1/1000 inç)", "mil") }, S.Units, v => S.Units = v));
            });

            Card("Dosyalar ve otomatik kayıt", "Çökme veya elektrik kesintisinde işinizi kaybetmeyin.", c =>
            {
                c.Children.Add(Choice("Otomatik yedek", new[] { ("Kapalı", 0), ("Her 2 dakikada", 2), ("Her 5 dakikada", 5), ("Her 10 dakikada", 10) }, S.AutosaveMinutes, v => S.AutosaveMinutes = v,
                    "Kaydedilmemiş sekmelerin bir kopyası 'Belgeler\\RobotWin\\Yedekler' klasörüne yazılır; asıl dosyanız değiştirilmez."));
                c.Children.Add(Choice("Son dosyalar listesi", new[] { ("5", 5), ("10", 10), ("20", 20) }, S.RecentFileCount, v =>
                {
                    S.RecentFileCount = v;
                    RecentFiles.Limit = v;
                }));
                c.Children.Add(Text("Proje klasörü", S.ProjectFolder, v => S.ProjectFolder = v, "Yeni projeler ve yedekler için varsayılan klasör."));
                var clear = new Button { Content = "Son dosyalar listesini temizle", Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                clear.Click += (_, _) =>
                {
                    RecentFiles.Clear();
                    Workspace.Current.Status = "Son dosyalar listesi temizlendi";
                };
                c.Children.Add(clear);
            });

            Card("Derleyiciler", "Kartların kodunu gerçek araç zinciriyle derleyen programlar.", c =>
            {
                string? cli = Firmware.ArduinoCompiler.FindCli();
                c.Children.Add(Text("arduino-cli yolu", S.ArduinoCliPath, v => S.ArduinoCliPath = v,
                    cli == null ? "Bulunamadı: derleyici kurulumla gelir; uygulamayı yeniden kurun ya da kendi arduino-cli.exe yolunuzu yazın."
                    : Firmware.ArduinoCompiler.IsBundled(cli) ? "Kurulumla gelen derleyici kullanılıyor (arduino-cli + AVR çekirdeği). Kendi arduino-cli'nizi kullanmak için yolunu yazın."
                    : "Bulundu: " + cli));
                bool msvc = RobotTwin.CoreSim.Firmware.Esp32.Esp32SketchCompiler.IsToolchainAvailable;
                c.Children.Add(Text("vcvars64.bat yolu", S.VcVarsPath, v => S.VcVarsPath = v,
                    msvc ? "Visual Studio C++ araçları bulundu (ESP32 çizimleri yerel olarak derlenir)." : "Bulunamadı. Visual Studio Build Tools 'C++ ile masaüstü geliştirme' gerekir (ESP32)."));
            });

            Card("Güncellemeler", "Yeni sürümler GitHub'dan denetlenir; kurulum tek tıkla yapılır, projeleriniz ve ayarlarınız korunur.", c =>
            {
                var um = Updates.UpdateManager.Current;
                c.Children.Add(Toggle("Açılışta yeni sürümü denetle", S.AutoCheckUpdates, v => S.AutoCheckUpdates = v, "Günde en çok bir kez, uygulama açıldıktan birkaç saniye sonra arka planda sorulur."));
                c.Children.Add(Toggle("Ön sürümleri de öner (beta, rc)", S.IncludePrereleases, v =>
                {
                    S.IncludePrereleases = v;
                    um.Refresh();
                }));
                string skipped = string.IsNullOrEmpty(S.SkippedUpdateVersion) ? string.Empty : $" · atlanan sürüm: {S.SkippedUpdateVersion}";
                c.Children.Add(new TextBlock { Text = $"Yüklü sürüm {um.CurrentVersion} · {um.Install.ModeText}{skipped}", Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 0, 0, 10) });
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var check = new Button { Content = "Güncellemeleri denetle" };
                check.Click += (_, _) =>
                {
                    (Window.GetWindow(this) as MainWindow)?.ShowHelp("updates");
                    _ = um.CheckAsync();
                };
                row.Children.Add(check);
                var older = new Button { Content = "Eski sürümler", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(8, 0, 0, 0) };
                older.Click += (_, _) => (Window.GetWindow(this) as MainWindow)?.ShowHelp("versions");
                row.Children.Add(older);
                if (!string.IsNullOrEmpty(S.SkippedUpdateVersion))
                {
                    var unskip = new Button { Content = "Atlanan sürümü yine öner", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(8, 0, 0, 0) };
                    unskip.Click += (_, _) =>
                    {
                        um.ClearSkip();
                        Build();
                    };
                    row.Children.Add(unskip);
                }

                c.Children.Add(row);
            });

            SdkCard();   // [sdk] Pages/SettingsPage.Sdk.cs

            Card("Klavye kısayolları", "Tüm sayfalarda geçerli tuşlar.", c =>
            {
                foreach (var (keys, what) in new[]
                {
                    ("Ctrl+N / Ctrl+Shift+N", "Yeni devre / yeni dünya"), ("Ctrl+O · Ctrl+S · Ctrl+Shift+S", "Aç · kaydet · farklı kaydet"),
                    ("Ctrl+W · Ctrl+Tab", "Sekmeyi kapat · sonraki sekme"), ("Ctrl+\\", "Bölünmüş görünüm"), ("F11 · Esc", "Tam ekran · çık"),
                    ("F5 · Shift+F5", "Simülasyon başlat/duraklat · durdur"), ("Ctrl+Z · Ctrl+Y", "Geri al · yinele"),
                    ("W · L · R · X · Del", "Devre: kablo · etiket · döndür · aynala · sil"), ("K · B · R · Ctrl+D", "Dünya: kablo · bağla · döndür · çoğalt"),
                    ("F7", "Kod: derle")
                })
                {
                    var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                    g.ColumnDefinitions.Add(new ColumnDefinition());
                    g.Children.Add(new TextBlock { Text = keys, FontFamily = (FontFamily)FindResource("F.Mono"), Foreground = (Brush)FindResource("B.TextSecondary") });
                    var t = new TextBlock { Text = what };
                    Grid.SetColumn(t, 1);
                    g.Children.Add(t);
                    c.Children.Add(g);
                }
            });

            _cards.Add(("Dosya türleri", FileTypesCard));
            _cards.Add(("Hakkında", AboutCard));
            SectionNav.Children.Clear();
            foreach (var (title, card) in _cards)
            {
                var b = new Button { Content = title, Style = (Style)FindResource("Btn.Ghost"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 2) };
                var target = card;
                b.Click += (_, _) => target.BringIntoView(new Rect(0, 0, 1, Scroll.ViewportHeight));
                SectionNav.Children.Add(b);
            }

            _building = false;
        }

        // ---------------------------------------------------------------- builders

        private void Card(string title, string subtitle, Action<StackPanel> fill)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Text.Subtitle") });
            sp.Children.Add(new TextBlock { Text = subtitle, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 14) });
            fill(sp);
            var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14), Child = sp };
            Sections.Children.Add(card);
            _cards.Add((title, card));
        }

        private void Saved()
        {
            if (_building) return;
            S.Save();
            Workspace.Current.Status = "Ayarlar kaydedildi";
        }

        private FrameworkElement Help(FrameworkElement control, string? help)
        {
            if (help == null) return control;
            var sp = new StackPanel();
            sp.Children.Add(control);
            sp.Children.Add(new TextBlock { Text = help, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -6, 0, 12) });
            return sp;
        }

        private FrameworkElement Row(string label, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            g.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Text.Body"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        private FrameworkElement Toggle(string label, bool value, Action<bool> set, string? help = null)
        {
            var cb = new CheckBox { Content = label, IsChecked = value, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, help == null ? 12 : 10) };
            cb.Click += (_, _) =>
            {
                set(cb.IsChecked == true);
                Saved();
            };
            if (help == null) return cb;
            var sp = new StackPanel();
            sp.Children.Add(cb);
            sp.Children.Add(new TextBlock { Text = help, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(42, -6, 0, 12) });
            return sp;
        }

        private FrameworkElement Choice<T>(string label, (string Text, T Value)[] options, T current, Action<T> set, string? help = null)
        {
            var box = new ComboBox();
            int best = -1;
            double bestDiff = double.MaxValue;
            for (int i = 0; i < options.Length; i++)
            {
                box.Items.Add(new ComboBoxItem { Content = options[i].Text });
                double diff = options[i].Value is IConvertible a && current is IConvertible b && !(a is string)
                    ? Math.Abs(Convert.ToDouble(a, CultureInfo.InvariantCulture) - Convert.ToDouble(b, CultureInfo.InvariantCulture))
                    : Equals(options[i].Value, current) ? 0 : 1;
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = i;
                }
            }

            box.SelectedIndex = best;
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0) return;
                set(options[box.SelectedIndex].Value);
                Saved();
            };
            return Help(Row(label, box), help);
        }

        private FrameworkElement Number(string label, double value, Action<double> set, double min, double max, string? help = null)
        {
            var tb = new TextBox { Text = value.ToString("0.##", CultureInfo.InvariantCulture) };
            void Apply()
            {
                if (double.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= min && v <= max)
                {
                    set(v);
                    Saved();
                }
                else
                {
                    tb.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
                    Workspace.Current.Status = $"{label}: {min}…{max} arasında bir sayı girin";
                }
            }

            tb.LostKeyboardFocus += (_, _) => Apply();
            tb.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) Apply();
            };
            return Help(Row(label, tb), help);
        }

        private FrameworkElement Text(string label, string value, Action<string> set, string? help = null)
        {
            var tb = new TextBox { Text = value };
            tb.LostKeyboardFocus += (_, _) =>
            {
                if (tb.Text == value) return;
                value = tb.Text.Trim();
                set(value);
                Saved();
            };
            return Help(Row(label, tb), help);
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            if (Dialog.Show(Window.GetWindow(this), "Varsayılanlara dön", "Tüm tercihler ilk haline dönsün mü? (Projeleriniz etkilenmez.)", "Sıfırla", "Vazgeç") != 0) return;
            S.ResetToDefaults();
            S.Save();
            (Window.GetWindow(this) as MainWindow)?.ApplyUiScale(S.UiScale);
            Build();
            Workspace.Current.Status = "Ayarlar varsayılanlara döndü";
        }

        private void OnOpenDataFolder(object sender, RoutedEventArgs e)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }

        private void OnExportLogo(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Logo klasörü seçin", FileName = "robotwin-logo", Filter = "Klasör|*.folder" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            string dir = Path.Combine(Path.GetDirectoryName(dlg.FileName)!, "robotwin-logo");
            Logo.Export(dir);
            Workspace.Current.Status = "Logo dışa aktarıldı: " + dir;
        }

        // ================================================================ file types

        private void BuildFileTypes()
        {
            FileTypes.Children.Clear();
            var contents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".rwproj"] = "Şema, PCB, mekanik montaj, kart kodları ve derlenmiş firmware, projeye özel parçalar, önizleme",
                [".rwpart"] = "Tek parça: simülasyon modeli, parametreler, isteğe bağlı PCB izdüşümü",
                [".rwlib"] = "Parça koleksiyonu; ekip ve bilgisayarlar arasında paylaşmak için",
                [".rwsim"] = "Simülasyon kaydı: osiloskop kanalları, olay günlüğü, seri çıktı, araç yörüngesi ve proje",
            };
            var types = RobotTwin.CoreSim.Formats.RobotWinFormats.All.Select(t => (t.Extension, t.Description)).ToList();
            contents[RobotTwin.CoreSim.Sandbox.WorldDocument.Extension] = "Açık dünya: gömülü kart tasarımları, parçalar, gövdeler, 3D modeller, kablolar ve mekanik bağlantılar";
            foreach (var (ext, desc) in types)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.Children.Add(new Image { Source = FileIcons.Render(ext, 64), Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left });
                var e = new TextBlock { Text = ext, FontFamily = (FontFamily)FindResource("F.Mono"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(e, 1);
                grid.Children.Add(e);
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = desc });
                sp.Children.Add(new TextBlock { Text = contents.TryGetValue(ext, out var c) ? c : string.Empty, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(sp, 2);
                grid.Children.Add(sp);
                FileTypes.Children.Add(grid);
            }

            UpdateAssocStatus();
        }

        private void UpdateAssocStatus()
        {
            bool on = FileAssociations.IsRegistered();
            AssocStatus.Text = on
                ? "Kayıtlı: Gezgin'de çift tıklanan RobotWin dosyaları bu uygulamayla açılır (yalnızca bu Windows kullanıcısı için)."
                : "Kayıtlı değil. Düğme yalnızca bu kullanıcının kayıt defterine (HKCU) yazar; yönetici izni gerekmez.";
            UnassocButton.IsEnabled = on;
        }

        private void OnRegisterFiles(object sender, RoutedEventArgs e)
        {
            try
            {
                FileAssociations.Register();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
            {
                AssocStatus.Text = "Kaydedilemedi: " + ex.Message;
                return;
            }

            UpdateAssocStatus();
        }

        private void OnUnregisterFiles(object sender, RoutedEventArgs e)
        {
            try
            {
                FileAssociations.Unregister();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
            {
                AssocStatus.Text = "Kaldırılamadı: " + ex.Message;
                return;
            }

            UpdateAssocStatus();
        }
    }
}
