using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotWin.Studio.Editor.Completion;
using RobotWin.Studio.Firmware;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [code] Completion and libraries on the code page. The editor completes from the headers the board's compiler really
    /// uses (arduino-cli's AVR core and installed libraries for Uno / Nano, the simulation's HAL for ESP32). The
    /// "Kütüphaneler" tab searches the Arduino library index offline, installs and removes libraries with arduino-cli and
    /// adds their <c>#include</c>; a "… bulunamadı" build error opens it on the missing header.
    /// </summary>
    public partial class CodePage
    {
        private ArduinoHeaders? _avrHeaders, _espHeaders;
        private readonly Dictionary<string, ApiIndex> _apiCache = new Dictionary<string, ApiIndex>(StringComparer.Ordinal);
        private static ArduinoLibraryIndex? _index;
        private static Task<ArduinoLibraryIndex?>? _indexLoad;
        private TextBox _libSearch = null!;
        private TextBlock _libStatus = null!;
        private StackPanel _libList = null!;
        private TabItem _libTab = null!;
        private bool _libBusy;

        private static string SketchbookLibraries => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Arduino", "libraries");

        private bool Esp32Board => ArduinoCompiler.IsEsp32(Fqbn);

        private void InitCompletion()
        {
            Editor.CompletionProvider = (text, caret, explicitRequest) =>
            {
                var headers = Headers();
                return CompletionEngine.Complete(text, caret, Api(text, headers), headers.Includable, explicitRequest);
            };
            Editor.SignatureProvider = (text, caret) => CompletionEngine.Signature(text, caret, Api(text, Headers()));
            BuildLibraryTab();
            Firmware.ArduinoCompiler.LibrariesChanged += () => Dispatcher.BeginInvoke(new Action(LibrariesChanged));   // [integrity] installed from the project check
        }

        private ArduinoHeaders Headers()
        {
            if (Esp32Board) return _espHeaders ??= new ArduinoHeaders(true, LibraryRoots(), null, HalHeaders());
            return _avrHeaders ??= new ArduinoHeaders(false, LibraryRoots(), ArduinoCompiler.DataFolder);   // [setup] the bundled compiler's core
        }

        private static IEnumerable<string> LibraryRoots() => ArduinoCompiler.LibraryRoots;

        /// <summary>The ESP32 simulation's headers, embedded in the simulator (what an ESP32 sketch is compiled against).</summary>
        private static Dictionary<string, string> HalHeaders() => Shell.ProjectChecking.Esp32HalHeaders();

        /// <summary>The API for this sketch's includes (cached until the includes or the libraries change).</summary>
        private ApiIndex Api(string text, ArduinoHeaders headers)
        {
            var includes = SketchModel.Scan(text).Includes;
            string key = (headers.Esp32 ? "esp32|" : "avr|") + string.Join("|", includes);
            if (_apiCache.TryGetValue(key, out var api)) return api;
            api = headers.IndexFor(includes);
            if (_apiCache.Count > 32) _apiCache.Clear();
            _apiCache[key] = api;
            return api;
        }

        private void LibrariesChanged()
        {
            _avrHeaders = null;
            _espHeaders = null;
            _apiCache.Clear();
            ShowLibraries();
        }

        // ================================================================ the Kütüphaneler tab

        private void BuildLibraryTab()
        {
            var root = new DockPanel { Margin = new Thickness(10, 8, 10, 6) };
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _libStatus = new TextBlock { Style = (Style)FindResource("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel.SetDock(_libStatus, Dock.Right);
            _libSearch = new TextBox { Tag = "Kütüphane ara: servo, lcd i2c, dht, json, Servo.h …", MinWidth = 280 };
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ShowLibraries();
            };
            _libSearch.TextChanged += (_, _) =>
            {
                timer.Stop();
                timer.Start();
            };
            top.Children.Add(_libStatus);
            top.Children.Add(_libSearch);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            _libList = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = _libList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _libTab = new TabItem { Header = "Kütüphaneler", Content = root };
            BottomTabs.Items.Add(_libTab);
            BottomTabs.SelectionChanged += (_, e) =>
            {
                if (e.OriginalSource == BottomTabs && BottomTabs.SelectedItem == _libTab) ShowLibraries();
            };
        }

        /// <summary>Opens the tab searching for a header (a "… bulunamadı" error).</summary>
        private void FindLibraryFor(string header)
        {
            BottomTabs.SelectedItem = _libTab;
            _libSearch.Text = header;
            ShowLibraries();
        }

        private async void ShowLibraries()
        {
            if (_libList == null) return;
            var headers = Headers();
            string query = _libSearch.Text.Trim();
            _libList.Children.Clear();
            if (headers.Esp32)
                _libList.Children.Add(Note("ESP32: kurulu kütüphaneler çizimle birlikte simülasyon katmanına (HAL) derlenir. ESP32 yazmaçlarını doğrudan kullanan kütüphaneler derlenmeyebilir."));
            else if (!headers.CoreFound)
                _libList.Children.Add(Note("Arduino AVR çekirdeği bulunamadı: Arduino derleyicisi kurulumun bir bileşenidir; Ayarlar → Güncellemeler → Bileşenleri değiştir ile ekleyin ya da Ayarlar → Derleyiciler'den kendi arduino-cli'nizi seçin. Tamamlama o zamana kadar sınırlıdır."));

            var installed = headers.Libraries.ToList();
            if (query.Length == 0)
            {
                _libStatus.Text = $"{installed.Count} kütüphane kullanılabilir";
                foreach (var l in installed.OrderBy(l => l.Origin).ThenBy(l => l.Name)) _libList.Children.Add(InstalledRow(l));
                return;
            }

            // Installed ones that match first, then the index.
            string q = query.ToLowerInvariant();
            foreach (var l in installed.Where(l => l.Name.ToLowerInvariant().Contains(q) || l.Headers.Any(h => h.ToLowerInvariant().Contains(q))))
                _libList.Children.Add(InstalledRow(l));
            _libStatus.Text = "Arduino kütüphane listesi okunuyor …";
            var index = await LoadIndexAsync();
            if (_libSearch.Text.Trim() != query) return;   // typed on meanwhile
            if (index == null)
            {
                _libStatus.Text = "Arduino kütüphane listesi yok";
                var b = new Button { Content = "Kütüphane listesini indir (arduino-cli lib update-index)", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                b.Click += async (_, _) => await RunCli("Kütüphane listesi indiriliyor …", "lib", "update-index");
                _libList.Children.Add(b);
                return;
            }

            var found = index.Search(Regex.Replace(query, @"\.h$", string.Empty), headers.Esp32 ? "esp32" : "avr");
            _libStatus.Text = $"{found.Count} sonuç · liste {index.Updated:d MMM yyyy}";
            foreach (var lib in found)
            {
                if (installed.Any(i => string.Equals(i.Name, lib.Name, StringComparison.OrdinalIgnoreCase))) continue;
                _libList.Children.Add(IndexRow(lib));
            }
        }

        private static Task<ArduinoLibraryIndex?> LoadIndexAsync()
        {
            if (_index != null) return Task.FromResult<ArduinoLibraryIndex?>(_index);
            ArduinoLibraryIndex.DataFolder = ArduinoCompiler.DataFolder;   // [setup] the bundled compiler's list
            return _indexLoad ??= Task.Run(() =>
            {
                var idx = ArduinoLibraryIndex.Load(ArduinoLibraryIndex.DefaultPath);
                _index = idx;
                if (idx == null) _indexLoad = null;
                return idx;
            });
        }

        private TextBlock Note(string text) => new TextBlock { Text = text, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

        private FrameworkElement InstalledRow(LocalLibrary lib)
        {
            string origin = lib.Origin switch { "çekirdek" => "kartla gelir", "HAL" => "ESP32 simülasyonu", _ => "kurulu" };
            var row = LibRow(lib.Name, lib.Version, origin, lib.Headers.Count > 0 ? string.Join(", ", lib.Headers.Take(6)) + (lib.Headers.Count > 6 ? " …" : string.Empty) : "başlık yok");
            var buttons = (StackPanel)row.Tag;
            foreach (var h in lib.Headers.Take(lib.Origin == "HAL" ? 0 : 2))
                buttons.Children.Add(SmallButton("#include <" + h + ">", "Bu başlığı çizimin başına ekler", () => AddInclude(h)));
            if (lib.Origin == "kurulu" && lib.Folder.StartsWith(SketchbookLibraries, StringComparison.OrdinalIgnoreCase))
                buttons.Children.Add(SmallButton("Kaldır", "arduino-cli lib uninstall", async () =>
                {
                    if (Shell.Dialog.Show(Window.GetWindow(this), "Kütüphaneyi kaldır", $"{lib.Name} kaldırılsın mı? Klasörü silinir.", "Kaldır", "Vazgeç") != 0) return;
                    await RunCli($"{lib.Name} kaldırılıyor …", "lib", "uninstall", lib.Name);
                }));
            if (lib.Origin == "HAL")
            {
                var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                foreach (var h in lib.Headers.Where(h => !h.StartsWith("esp_", StringComparison.Ordinal) && h != "Arduino.h"))
                    wrap.Children.Add(SmallButton(h, "#include <" + h + "> ekler", () => AddInclude(h)));
                ((StackPanel)row.Child).Children.Add(wrap);
            }

            return row;
        }

        private FrameworkElement IndexRow(IndexedLibrary lib)
        {
            var row = LibRow(lib.Name, lib.Version, lib.Author, lib.Sentence);
            var buttons = (StackPanel)row.Tag;
            buttons.Children.Add(SmallButton("Kur ve ekle", "arduino-cli lib install ile indirir, kurar ve #include satırını ekler", async () =>
            {
                if (await RunCli($"{lib.Name} {lib.Version} kuruluyor …", "lib", "install", lib.Name + "@" + lib.Version))
                    AddInclude(lib.MainHeader);
            }, primary: true));
            return row;
        }

        private Border LibRow(string name, string version, string by, string text)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var head = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Right);
            head.Children.Add(buttons);
            var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            title.Inlines.Add(new System.Windows.Documents.Run(name) { FontWeight = FontWeights.SemiBold });
            if (version.Length > 0) title.Inlines.Add(new System.Windows.Documents.Run("  " + version) { Foreground = (Brush)FindResource("B.TextMuted") });
            title.Inlines.Add(new System.Windows.Documents.Run("  · " + by) { Foreground = (Brush)FindResource("B.TextMuted") });
            title.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
            head.Children.Add(title);
            var body = new StackPanel();
            body.Children.Add(head);
            body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 2, 0, 0) });
            var border = new Border { Child = body, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(4), Tag = buttons };
            border.SetResourceReference(Border.BackgroundProperty, "B.Raised");
            return border;
        }

        private Button SmallButton(string text, string tip, Action click, bool primary = false)
        {
            var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1) };
            if (primary) b.Style = (Style)FindResource("Btn.Primary");
            b.Click += (_, _) =>
            {
                if (!_libBusy) click();
            };
            return b;
        }

        /// <summary>Adds "#include &lt;header&gt;" after the sketch's last include (or at the top), unless it is there.</summary>
        private void AddInclude(string header)
        {
            string text = Editor.Text;
            if (Regex.IsMatch(text, @"^\s*#\s*include\s*[<""]" + Regex.Escape(header) + @"[>""]", RegexOptions.Multiline))
            {
                W.Status = header + " zaten ekli";
                return;
            }

            var last = Regex.Matches(text, @"^\s*#\s*include[^\n]*\n", RegexOptions.Multiline).Cast<Match>().LastOrDefault();
            int at = last != null ? last.Index + last.Length : 0;
            Editor.Box.Select(at, 0);
            Editor.Box.SelectedText = "#include <" + header + ">\n";
            Editor.Box.Select(at + header.Length + 12, 0);
            Editor.Focus();
            W.Status = header + " eklendi";
        }

        /// <summary>Runs arduino-cli (install, uninstall, update-index) and shows how it went.</summary>
        private async Task<bool> RunCli(string doing, params string[] args)
        {
            if (ArduinoCompiler.FindCli() == null)
            {
                _libStatus.Text = "arduino-cli bulunamadı";
                return false;
            }

            _libBusy = true;
            _libStatus.Text = doing;
            try
            {
                var (ok, log) = await ArduinoCompiler.RunCliAsync(args);
                BuildLog.Text = "arduino-cli " + string.Join(" ", args) + "\n" + log;
                _libStatus.Text = ok ? "Tamam: " + doing.TrimEnd(' ', '…', '.') : "Olmadı — ayrıntı Derleyici sekmesinin günlüğünde";
                if (args.Length > 1 && args[1] == "update-index")
                {
                    _index = null;
                    _indexLoad = null;
                }

                LibrariesChanged();
                return ok;
            }
            finally
            {
                _libBusy = false;
            }
        }

        /// <summary>A "Servo.h bulunamadı (kütüphane kurulu değil)" diagnostic: the header it names.</summary>
        private static string? MissingHeader(string message)
        {
            var m = Regex.Match(message, @"^(.+?\.h) bulunamadı");
            return m.Success ? Path.GetFileName(m.Groups[1].Value.Trim()) : null;
        }
    }
}
