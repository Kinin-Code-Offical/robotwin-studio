using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        // [settings] categories: the left list shows one at a time; the search box looks through all of them
        private readonly List<(string Category, FrameworkElement Card, string Text)> _index = new List<(string, FrameworkElement, string)>();
        private readonly List<(string Name, string Note)> _categories = new List<(string, string)>();
        private string _category = "Genel";
        private string _shown = "Genel";

        private void Category(string name, string note)
        {
            _category = name;
            _categories.Add((name, note));
        }

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
            (FileTypesCard.Parent as Panel)?.Children.Remove(FileTypesCard);   // XAML cards: moved into their categories
            (AboutCard.Parent as Panel)?.Children.Remove(AboutCard);
            Sections.Children.Clear();
            _cards.Clear();
            _index.Clear();
            _categories.Clear();

            Category("Genel", "Dil, açılış, proje klasörü, son dosyalar ve birimler.");
            Card("Dil", "Arayüzün dili. Yeni dil, uygulama yeniden açılınca her yerde geçerli olur.", c =>
            {
                c.Children.Add(Choice("Dil", Lang.Available().Select(l => (l.Name, l.Id)).ToArray(), S.Language, v => S.Language = v,
                    "Kendi dil paketinizi de ekleyebilirsiniz: Türkçe metinlerin karşılıklarını içeren bir JSON dosyasını dil paketleri klasörüne koyun."));
                var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                var restart = new Button { Content = "Şimdi yeniden başlat", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(0, 0, 8, 0) };
                restart.Click += (_, _) =>
                {
                    S.Save();
                    (Window.GetWindow(this) as MainWindow)?.Restart();
                };
                row.Children.Add(restart);
                var folder = new Button { Content = "Dil paketleri klasörü", Style = (Style)FindResource("Btn.Ghost") };
                folder.Click += (_, _) =>
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(Lang.UserFolder);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Lang.UserFolder + "\"") { UseShellExecute = true });
                    }
                    catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
                    {
                        Workspace.Current.Status = "Klasör açılamadı: " + ex.Message;
                    }
                };
                row.Children.Add(folder);
                c.Children.Add(row);
            });
            Card("Açılış", "Uygulama açıldığında ne görüneceği.", c =>
            {
                c.Children.Add(Toggle("Açılışta başlangıç sayfasını göster", S.ShowHomeOnStart, v => S.ShowHomeOnStart = v, "Kapalıyken son açılan proje veya dünya doğrudan açılır."));
                var guide = new Button { Content = "İlk robot rehberini başlat", Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left };
                guide.Click += (_, _) => (Window.GetWindow(this) as MainWindow)?.StartGuide();
                c.Children.Add(guide);
            });
            Card("Dosyalar", "Yeni projelerin klasörü ve son açılanlar listesi.", c =>
            {
                c.Children.Add(Text("Proje klasörü", S.ProjectFolder, v => S.ProjectFolder = v, "Yeni projeler için varsayılan klasör (yedek klasörü ayrıca seçilmediyse yedekler de burada)."));
                c.Children.Add(Choice("Son dosyalar listesi", new[] { ("5", 5), ("10", 10), ("20", 20) }, S.RecentFileCount, v =>
                {
                    S.RecentFileCount = v;
                    RecentFiles.Limit = v;
                }));
                var clear = new Button { Content = "Son dosyalar listesini temizle", Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                clear.Click += (_, _) =>
                {
                    RecentFiles.Clear();
                    Workspace.Current.Status = "Son dosyalar listesi temizlendi";
                };
                c.Children.Add(clear);
            });
            Card("Birimler", "Editörlerde uzunluklar.", c =>
            {
                // [units] what the length boxes show and take (Parça, Montaj, Dünya; a typed "in" or "mm" works in any)
                c.Children.Add(Choice("Uzunluk birimi", LengthUnits.All, S.Units, v => S.Units = v));
            });

            Category("Görünüm ve tema", "Tema, vurgu rengi, arayüz boyutu, efektler ve 3B kalite.");
            ThemeCard();   // [theme] the gallery (Pages/SettingsPage.Themes.cs)
            Card("Arayüz", "Boyut ve göstergeler.", c =>
            {
                // [uiscale] 0 stands for automatic: proportional to the window
                c.Children.Add(Choice("Arayüz ölçeği", new[] { ("Otomatik", 0.0), ("%75", 0.75), ("%90", 0.9), ("%100", 1.0), ("%110", 1.1), ("%125", 1.25), ("%150", 1.5) }, S.UiScaleAuto ? 0.0 : S.UiScale, v =>
                {
                    S.UiScaleAuto = v <= 0;
                    if (v > 0) S.UiScale = v;
                    (Window.GetWindow(this) as MainWindow)?.ApplyUiScale(S.UiScale);
                }, "Otomatik: arayüz pencereyle orantılı (1920 × 1080'de %100); küçük ekranda ya da büyük ekran ölçeğinde simgeler ve paneller aynı oranda küçülür, kaydırma ya da taşma olmaz. Sunum için sabit bir ölçek seçin."));
                c.Children.Add(Toggle("Performans göstergesi (FPS, kare süreleri)", S.ShowPerf, v =>
                {
                    S.ShowPerf = v;
                    (Window.GetWindow(this) as MainWindow)?.ShowPerf(v);
                }, "Ctrl+Shift+F ile de açılıp kapanır."));
            });
            Card("Efektler ve ses", "Görsel efektler ve simülasyon sesleri.", c =>
            {
                c.Children.Add(Toggle("Parçacık efektleri (duman, kıvılcım, alev)", S.Effects, v => S.Effects = v, "Yanma ve aşırı ısınma görselleri. Kapatmak zayıf ekran kartlarında akıcılığı artırır."));
                c.Children.Add(Toggle("Simülasyon sesleri", S.Sound, v => S.Sound = v, "Motor uğultusu, servo dişli sesi, buzzer, röle tıkırtısı, yanan parçanın patlaması."));
                c.Children.Add(Choice("Ses düzeyi", new[] { ("%25", 0.25), ("%50", 0.5), ("%60", 0.6), ("%80", 0.8), ("%100", 1.0) }, S.Volume, v => S.Volume = v));
                c.Children.Add(Toggle("Akım akışı animasyonu", S.ShowCurrentFlow, v => S.ShowCurrentFlow = v));
            });
            Card("3B görünüm", "Dünya ve parça görünümlerinin çizim kalitesi.", c =>
            {
                c.Children.Add(Choice("3D kalitesi", new[] { ("Düşük", "low"), ("Orta", "medium"), ("Yüksek", "high") }, S.Quality3D, v => S.Quality3D = v, "Düşük: kenar yumuşatma kapalı, daha az parçacık."));
                c.Children.Add(Choice("3B çizici", new[] { ("GPU (RobotWin)", "gpu"), ("WPF", "wpf") }, S.Renderer3D, v => S.Renderer3D = v, "GPU: kendi çizicimiz, ekran kartında fiziksel tabanlı malzeme, güneş gölgesi ve 4× kenar yumuşatma. WPF: eski çizim; ekran kartı sorun çıkarırsa."));
            });

            Category("Kaydetme ve yedekleme", "Otomatik yedek, önceki sürümler, yedeklerin saklanması ve çökme sonrası kurtarma.");
            BackupCards();

            Category("Klavye kısayolları", "Tuşu değiştirmek için düğmeye tıklayıp yeni tuşlara basın: Esc vazgeçer, Backspace kaldırır. Çakışan tuşlar işaretlenir.");
            ShortcutCards();

            Category("Simülasyon ve fizik", "Çözücünün doğruluğu, hızı ve parçaların gerçekteki gibi bozulması.");
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
                c.Children.Add(Toggle("Simülasyonu ayrı iş parçacığında çalıştır", S.SimulationThread, v => S.SimulationThread = v,
                    "Açıkken simülasyon kendi işlemci çekirdeğinde ilerler: ağır dünyalar daha hızlı koşar, pencere akıcı kalır. Kapalıyken arayüzün karelerinin içinde ilerler. Bir sonraki başlatmada geçerli olur."));
            });

            Category("Dünya ve düzenleme", "Yeni dünyaların ortamı ve editörlerin ızgarası.");
            Card("Dünya ve düzenleme", "Yeni dünyaların ortamı ve editörlerin ızgarası.", c =>
            {
                c.Children.Add(Choice("Yeni dünyaların yerçekimi", new[] { ("Dünya 9.81 m/s²", 9.81), ("Ay 1.62 m/s²", 1.62), ("Mars 3.71 m/s²", 3.71), ("Sıfır", 0.0) }, S.WorldGravity, v => S.WorldGravity = v));
                c.Children.Add(Choice("Dünya editörü ızgarası", new[] { ("Kapalı", 0.0), ("1 mm", 1.0), ("2.5 mm", 2.5), ("5 mm", 5.0), ("10 mm", 10.0) }, S.WorldGridMm, v => S.WorldGridMm = v,
                    "Nesneler taşınırken bu adıma yaslanır; sürüklerken Alt basılıysa ızgara kapanır."));
                c.Children.Add(Toggle("Ortak seçim (devre ↔ PCB)", S.CrossSelect, v => S.CrossSelect = v,
                    "Devrede seçilen parça PCB'de de seçili olur, PCB'de seçilen devrede; görünüm yerinde kalır (KiCad'in çapraz seçimi gibi)."));
            });

            Category("Derleyiciler", "Kartların kodunu gerçek araç zinciriyle derleyen programlar.");
            Card("Derleyiciler", "Kartların kodunu gerçek araç zinciriyle derleyen programlar.", c =>
            {
                string? cli = Firmware.ArduinoCompiler.FindCli();
                c.Children.Add(Text("arduino-cli yolu", S.ArduinoCliPath, v => S.ArduinoCliPath = v,
                    cli == null ? "Bulunamadı: Arduino derleyicisini Güncellemeler → Bileşenleri değiştir ile ekleyin ya da kendi arduino-cli.exe yolunuzu yazın."
                    : Firmware.ArduinoCompiler.IsBundled(cli) ? "Kurulumla gelen derleyici kullanılıyor (arduino-cli + AVR çekirdeği). Kendi arduino-cli'nizi kullanmak için yolunu yazın."
                    : "Bulundu: " + cli));
                bool msvc = RobotTwin.CoreSim.Firmware.Esp32.Esp32SketchCompiler.IsToolchainAvailable;
                c.Children.Add(Text("vcvars64.bat yolu", S.VcVarsPath, v => S.VcVarsPath = v,
                    msvc ? "Visual Studio C++ araçları bulundu (ESP32 çizimleri yerel olarak derlenir)." : "Bulunamadı. Visual Studio Build Tools 'C++ ile masaüstü geliştirme' gerekir (ESP32)."));
            });

            Category("Güncellemeler", "Yeni sürümlerin denetlenmesi ve kurulması.");
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
                c.Children.Add(new TextBlock { Text = $"Yüklü sürüm {um.CurrentVersion} · {um.Install.ModeText} · {um.Install.ContentText}{skipped}", Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
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
                if (Updates.InstallEnvironment.CanModify(um.Install))
                {
                    // [setup] The installer's wizard on this copy: the Arduino compiler, STEP / IGES or its own .NET, added or removed.
                    var modify = new Button
                    {
                        Content = "Bileşenleri değiştir…",
                        Style = (Style)FindResource("Btn.Ghost"),
                        Margin = new Thickness(8, 0, 0, 0),
                        ToolTip = "Kurulum programını bu kopyada açar: Arduino derleyicisi, STEP / IGES desteği ya da uygulamanın kendi .NET kopyası eklenir veya kaldırılır. Değişiklik uygulanırken RobotWin Studio kapanır ve yeniden açılır."
                    };
                    modify.Click += (_, _) =>
                    {
                        try
                        {
                            Updates.InstallEnvironment.StartModify(um.Install);
                        }
                        catch (Updates.UpdateException ex)
                        {
                            Dialog.Show(Window.GetWindow(this), "Kurulum programı açılamadı", ex.Message, "Tamam");
                        }
                    };
                    row.Children.Add(modify);
                }
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

            Category("Eklentiler", "Kendi fizik ve davranış kodunuz: eklentiler, dünya betikleri, ifadeler.");
            SdkCard();   // [sdk] Pages/SettingsPage.Sdk.cs

            Category("Gelişmiş", "Dosya türleri, tanılama, ayarların taşınması ve sıfırlanması.");
            AdvancedCards();
            Sections.Children.Add(FileTypesCard);
            FileTypesCard.Margin = new Thickness(0, 0, 0, 14);
            _index.Add(("Gelişmiş", FileTypesCard, "dosya türleri rwproj rwworld ilişkilendirme windows file types association\n" + SearchText(FileTypesCard)));
            Category("Hakkında", "Sürüm ve uygulama bilgisi.");
            Sections.Children.Add(AboutCard);
            _index.Add(("Hakkında", AboutCard, "hakkında sürüm logo about version\n" + SearchText(AboutCard)));

            SectionNav.Children.Clear();
            foreach (var (name, _) in _categories)
            {
                var b = new ToggleButton { Content = name, Style = (Style)FindResource("List.Item"), Tag = name, Margin = new Thickness(0, 0, 0, 2) };
                b.Click += (_, _) =>
                {
                    SearchBox.Text = string.Empty;
                    ShowCategory(name);
                };
                SectionNav.Children.Add(b);
            }

            _building = false;
            ShowCategory(_categories.Any(x => x.Name == _shown) ? _shown : "Genel");
        }

        /// <summary>Shows one category's cards (and marks it in the list).</summary>
        internal void ShowCategory(string name)
        {
            _shown = name;
            foreach (var (category, card, _) in _index) card.Visibility = category == name ? Visibility.Visible : Visibility.Collapsed;
            foreach (ToggleButton b in SectionNav.Children.OfType<ToggleButton>()) b.IsChecked = (string)b.Tag == name;
            CategoryTitle.Text = name;
            CategoryNote.Text = _categories.FirstOrDefault(c => c.Name == name).Note ?? string.Empty;
            Scroll.ScrollToTop();
        }

        /// <summary>Search: every card whose title, notes or settings mention the words, from all categories.</summary>
        private void OnSearch(object sender, TextChangedEventArgs e)
        {
            string q = SearchBox.Text.Trim();
            if (q.Length == 0)
            {
                ShowCategory(_shown);
                return;
            }

            string[] words = RobotWin.Studio.Shell.Lang.Fold(q).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int found = 0;
            foreach (var (category, card, text) in _index)
            {
                bool hit = words.All(w => text.Contains(w, StringComparison.Ordinal));
                card.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
                if (hit) found++;
            }

            foreach (ToggleButton b in SectionNav.Children.OfType<ToggleButton>()) b.IsChecked = false;
            CategoryTitle.Text = $"Arama: \u201c{q}\u201d";
            CategoryNote.Text = found == 0 ? "Eşleşen ayar yok. Başka bir sözcük deneyin." : $"{found} bölümde bulundu.";
        }

        private void ApplyTheme()
        {
            Themes.Apply();
            Dispatcher.BeginInvoke(new Action(Build));   // this page's own rows took their colours in code
            Workspace.Current.Status = "Tema uygulandı";
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
            _index.Add((_category, card, RobotWin.Studio.Shell.Lang.Searchable(_category) + SearchText(sp)));
        }

        /// <summary>Every text a card shows (titles, labels, notes, choices) for the search box.</summary>
        private static string SearchText(DependencyObject root)
        {
            var sb = new System.Text.StringBuilder();
            void Walk(object o)
            {
                switch (o)
                {
                    // [lang] each text as shown and as written, lower-cased
                    case TextBlock t:
                        sb.Append(RobotWin.Studio.Shell.Lang.Searchable(t.Text));
                        break;
                    case ContentControl cc when cc.Content is string s:
                        sb.Append(RobotWin.Studio.Shell.Lang.Searchable(s));
                        break;
                    case TextBox tb:
                        sb.Append(RobotWin.Studio.Shell.Lang.Searchable(tb.Text));
                        break;
                }

                if (o is ItemsControl ic)
                {
                    foreach (var item in ic.Items)
                    {
                        if (item is ContentControl ci && ci.Content is string cs) sb.Append(RobotWin.Studio.Shell.Lang.Searchable(cs));
                        else if (item is string str) sb.Append(RobotWin.Studio.Shell.Lang.Searchable(str));
                    }
                }

                if (o is DependencyObject d)
                {
                    foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
                }
            }

            Walk(root);
            return sb.ToString();
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
            var tb = new TextBox { Text = Unattended.Display(value) };
            tb.LostKeyboardFocus += (_, _) =>
            {
                if (tb.Text == value || Unattended.Active) return;
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
            Themes.Apply();
            Shortcuts.Reapply();
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
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = RobotWin.Studio.Shell.Lang.T("Logo klasörü seçin"), FileName = "robotwin-logo", Filter = RobotWin.Studio.Shell.Lang.Filter("Klasör|*.folder") };
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
