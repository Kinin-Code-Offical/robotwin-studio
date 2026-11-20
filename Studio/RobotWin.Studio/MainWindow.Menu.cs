using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio
{
    /// <summary>
    /// The menu bar in the title bar (Dosya · Düzen · Görünüm · Simülasyon · Pencere · Yardım).
    /// Alt or F10 focuses it, Alt+letter opens a menu; dynamic submenus (recent files, tabs,
    /// templates) are rebuilt every time they open so they never go stale.
    /// </summary>
    public partial class MainWindow
    {
        private Menu? _menuBar;

        private void BuildMenuBar()
        {
            var bar = new Menu { Style = (Style)FindResource("MenuBar"), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(bar, true);
            bar.Items.Add(TopMenu("_Dosya", FileMenu));
            bar.Items.Add(TopMenu("_Düzen", EditMenu));
            bar.Items.Add(TopMenu("_Görünüm", ViewMenu));
            bar.Items.Add(TopMenu("_Simülasyon", SimulationMenu));
            bar.Items.Add(TopMenu("_Pencere", WindowMenu));
            bar.Items.Add(TopMenu("_Yardım", HelpMenu));
            MenuHost.Child = bar;
            _menuBar = bar;
            Bind(Key.F10, ModifierKeys.None, () =>
            {
                if (bar.Items[0] is MenuItem first)
                {
                    first.Focus();
                    first.IsSubmenuOpen = true;
                }
            });
        }

        private MenuItem TopMenu(string header, Action<MenuItem> fill)
        {
            var top = new MenuItem { Header = header, Style = (Style)FindResource("MenuBar.Top") };
            top.Items.Add(new MenuItem { Header = "…" });   // placeholder so the item is a submenu header
            top.SubmenuOpened += (_, e) =>
            {
                if (e.OriginalSource != top) return;
                top.Items.Clear();
                fill(top);
            };
            return top;
        }

        private static MenuItem Item(string header, string? keys, Action action, bool enabled = true)
        {
            var m = new MenuItem { Header = header, InputGestureText = keys ?? string.Empty, IsEnabled = enabled };
            m.Click += (_, _) => action();
            return m;
        }

        private static MenuItem Check(string header, string? keys, bool on, Action<bool> set)
        {
            var m = new MenuItem { Header = header, InputGestureText = keys ?? string.Empty, IsCheckable = true, IsChecked = on };
            m.Click += (_, _) => set(m.IsChecked);
            return m;
        }

        private static MenuItem Sub(string header) => new MenuItem { Header = header };

        // ------------------------------------------------------------------ Dosya

        private void FileMenu(MenuItem m)
        {
            m.Items.Add(Item("Yeni devre / kart", "Ctrl+N", () => NewProject()));
            m.Items.Add(Item("Yeni dünya", "Ctrl+Shift+N", () => NewWorld()));
            m.Items.Add(Item("Yeni 3D model", null, () => Navigate("model")));
            var templates = Sub("Dünya şablonundan");
            foreach (var t in Examples.WorldTemplates.All)
            {
                var tpl = t;
                var it = Item(t.Title, null, () => NewWorld(tpl.Build()));
                it.ToolTip = t.Description;
                templates.Items.Add(it);
            }

            m.Items.Add(templates);
            var examples = Sub("Devre örneğinden");
            foreach (var ex in Examples.ExampleProjects.All)
            {
                var x = ex;
                examples.Items.Add(Item(x.Title, null, () =>
                {
                    W.Load(x.Build(), null);
                    Navigate(x.Page);
                }));
            }

            m.Items.Add(examples);
            m.Items.Add(new Separator());
            m.Items.Add(Item("_Aç…", "Ctrl+O", () => OnOpen(this, new RoutedEventArgs())));
            var recent = Sub("Son açılanlar");
            var files = RecentFiles.Load().ToList();
            foreach (var path in files)
            {
                var p = path;
                var it = Item(Path.GetFileName(path), null, () => OpenFile(p));
                it.ToolTip = path;
                recent.Items.Add(it);
            }

            if (files.Count == 0) recent.Items.Add(Item("(boş)", null, () => { }, false));
            else
            {
                recent.Items.Add(new Separator());
                recent.Items.Add(Item("Listeyi temizle", null, () => RecentFiles.Clear()));
            }

            m.Items.Add(recent);
            m.Items.Add(new Separator());
            m.Items.Add(Item("_Kaydet", "Ctrl+S", () => OnSave(this, new RoutedEventArgs())));
            m.Items.Add(Item("Farklı kaydet…", "Ctrl+Shift+S", () => SaveAs()));
            m.Items.Add(Item("Simülasyon kaydını kaydet (.rwsim)…", null, SaveRecording));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Otomatik yedekler klasörü", null, () => RevealInExplorer(BackupFolder, true)));
            m.Items.Add(Item("Dosya türlerini bu uygulamayla ilişkilendir", null, () =>
            {
                try
                {
                    FileAssociations.Register();
                    W.Status = "RobotWin dosya türleri bu kullanıcı için kaydedildi; Gezgin'de simgeleriyle görünür.";
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
                {
                    W.Status = "Dosya türleri kaydedilemedi: " + ex.Message;
                }
            }));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Ç_ıkış", "Alt+F4", Close));
        }

        // ------------------------------------------------------------------ Düzen

        private void EditMenu(MenuItem m)
        {
            m.Items.Add(Item("_Geri al", "Ctrl+Z", () => W.Undo(), W.CanUndo));
            m.Items.Add(Item("_Yinele", "Ctrl+Y", () => W.Redo(), W.CanRedo));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Komut paleti: her şeyi ara…", "Ctrl+K", OpenPalette));
            m.Items.Add(Item("Parça ekle…", null, OpenPalette));
            m.Items.Add(new Separator());
            m.Items.Add(Item("_Ayarlar", "Ctrl+,", () => Navigate("settings")));
        }

        // ------------------------------------------------------------------ Görünüm

        private void ViewMenu(MenuItem m)
        {
            var pages = new (string Key, string Title, string Keys)[]
            {
                ("home", "Başlangıç", "Ctrl+0"), ("schematic", "Devre", "Ctrl+1"), ("pcb", "PCB", "Ctrl+2"), ("code", "Kod", "Ctrl+3"),
                ("model", "Modelle", "Ctrl+4"), ("assembly", "Montaj", "Ctrl+5"), ("world", "Dünya", "Ctrl+6"), ("library", "Kütüphane", "Ctrl+7")
            };
            foreach (var (key, title, keys) in pages)
            {
                var k = key;
                var it = Item(title, keys, () => Navigate(k));
                it.IsCheckable = true;
                it.IsChecked = _current == key;
                m.Items.Add(it);
            }

            m.Items.Add(new Separator());
            m.Items.Add(Check("Tasarım hattını genişlet", "Ctrl+B", Rail.IsExpanded, v => Rail.IsExpanded = v));
            m.Items.Add(Check("Durum çubuğu", null, StatusBar.Visibility == Visibility.Visible, v =>
            {
                StatusRow.Height = new GridLength(v ? 26 : 0);
                StatusBar.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
            }));
            var scale = Sub("Arayüz ölçeği");
            foreach (var (label, value) in new[] { ("%75", 0.75), ("%90", 0.9), ("%100", 1.0), ("%110", 1.1), ("%125", 1.25), ("%150", 1.5) })
            {
                double v = value;
                var it = Item(label, null, () =>
                {
                    AppSettings.Current.UiScale = v;
                    AppSettings.Current.Save();
                    ApplyUiScale(v);
                });
                it.IsCheckable = true;
                it.IsChecked = Math.Abs(AppSettings.Current.UiScale - v) < 1e-3;
                scale.Items.Add(it);
            }

            m.Items.Add(scale);
            m.Items.Add(new Separator());
            m.Items.Add(Check("Performans göstergesi (FPS)", "Ctrl+Shift+F", _perf.Visibility == Visibility.Visible, ShowPerf));
            m.Items.Add(new Separator());
            m.Items.Add(Check("Odak modu", null, _focusMode, v => SetFocusMode(v)));
            m.Items.Add(Check("Tam ekran", "F11", _fullScreen, _ => ToggleFullScreen()));
        }

        // ------------------------------------------------------------------ Simülasyon

        private void SimulationMenu(MenuItem m)
        {
            var s = W.Simulation.State;
            m.Items.Add(Item(s == SimState.Running ? "Devreyi duraklat" : s == SimState.Paused ? "Devreye devam et" : "Devreyi simüle et", "F5", () =>
            {
                Navigate("schematic");
                if (W.Simulation.State == SimState.Running) W.Simulation.Pause();
                else if (W.Simulation.State == SimState.Paused) W.Simulation.Resume();
                else W.Simulation.Start();
            }));
            m.Items.Add(Item("Devre simülasyonunu durdur", "Shift+F5", () => W.Simulation.Stop(), s != SimState.Stopped));
            m.Items.Add(new Separator());
            bool world = W.ActiveWorld != null || W.Documents.Any(d => d.Kind == DocumentKind.World);
            var (ws, _) = _world.SimulationState;
            m.Items.Add(Item(ws == SimState.Running ? "Dünyayı duraklat" : ws == SimState.Paused ? "Dünyaya devam et" : "Dünyayı çalıştır", null, () =>
            {
                Navigate("world");
                _world.ToggleRun();
            }, world));
            m.Items.Add(Item("Dünyayı durdur", null, () => _world.StopSimulation(), ws != SimState.Stopped));
            m.Items.Add(Item("Analiz modu (gerilme, akış, grafikler)…", "Ctrl+F5", () =>
            {
                Navigate("world");
                _world.OpenAnalysis();
            }, world));
            m.Items.Add(new Separator());
            var cfg = AppSettings.Current;
            m.Items.Add(Check("Parça hasarı (yanma, sigorta)", null, cfg.EnableDamage, v => { cfg.EnableDamage = v; cfg.Save(); }));
            m.Items.Add(Check("Arıza olunca duraklat", null, cfg.PauseOnFailure, v => { cfg.PauseOnFailure = v; cfg.Save(); }));
            m.Items.Add(Check("Simülasyon sesleri", null, cfg.Sound, v => { cfg.Sound = v; cfg.Save(); }));
            m.Items.Add(Check("Parçacık efektleri", null, cfg.Effects, v => { cfg.Effects = v; cfg.Save(); }));
        }

        // ------------------------------------------------------------------ Pencere

        private void WindowMenu(MenuItem m)
        {
            m.Items.Add(Item("Simge durumuna küçült", "Win+↓", () => WindowState = WindowState.Minimized));
            m.Items.Add(Item(WindowState == WindowState.Maximized ? "Önceki boyuta getir" : "Ekranı kapla", "Win+↑", () =>
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
            m.Items.Add(Item("Pencereyi ortala", null, CenterWindow));
            m.Items.Add(Item("Varsayılan boyuta getir", null, () =>
            {
                WindowState = WindowState.Normal;
                Width = 1480;
                Height = 900;
                CenterWindow();
            }));
            m.Items.Add(Check("Her zaman üstte", null, Topmost, v => Topmost = v));
            m.Items.Add(Item("Sistem menüsü", "Alt+Boşluk", () => _behavior?.ShowSystemMenu(new Point(8, 36))));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Bölünmüş görünüm aç / kapat", "Ctrl+\\", ToggleSplit));
            m.Items.Add(Item("Bu sayfayı ayrı pencereye al", null, () => Detach(_current)));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Sonraki sekme", "Ctrl+Tab", () => CycleTab(1)));
            m.Items.Add(Item("Önceki sekme", "Ctrl+Shift+Tab", () => CycleTab(-1)));
            m.Items.Add(Item("Sekmeyi kapat", "Ctrl+W", () => CloseTab(W.ActiveDocument)));
            m.Items.Add(Item("Diğer sekmeleri kapat", null, () => CloseOthers(W.ActiveDocument), W.Documents.Count > 1));
            m.Items.Add(new Separator());
            int n = 0;
            foreach (var d in W.Documents.ToList())
            {
                var doc = d;
                n++;
                var it = Item((n <= 9 ? "_" + n + "  " : string.Empty) + d.Title, d.KindText, () => W.Activate(doc));
                it.IsCheckable = true;
                it.IsChecked = d.IsActive;
                m.Items.Add(it);
            }
        }

        private void CenterWindow()
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }

        // ------------------------------------------------------------------ Yardım

        private const string RepoUrl = "https://github.com/Kinin-Code-Offical/robotwin-studio";

        private void HelpMenu(MenuItem m)
        {
            m.Items.Add(Item("_Yardım ve belgeler", "F1", () => ShowHelp(null)));
            m.Items.Add(Item("Klavye kısayolları", null, () => ShowHelp("shortcuts")));
            m.Items.Add(Item("Komut paleti", "Ctrl+K", OpenPalette));
            m.Items.Add(Item("Haberler", null, () => ShowHelp("news")));
            m.Items.Add(Item("Başlangıç sayfası", "Ctrl+0", () => Navigate("home")));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Güncellemeleri denetle…", null, CheckForUpdates));
            m.Items.Add(Item("Eski sürümler…", null, () => ShowHelp("versions")));
            m.Items.Add(new Separator());
            m.Items.Add(Item("GitHub sayfası", null, () => OpenUrl(RepoUrl)));
            m.Items.Add(Item("Sorun bildir…", null, () => OpenUrl(RepoUrl + "/issues/new")));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Hakkında", null, () => ShowHelp("about")));
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

    }
}
