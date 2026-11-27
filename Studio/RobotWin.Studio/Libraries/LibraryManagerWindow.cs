using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Libraries;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Libraries.LibUi;

namespace RobotWin.Studio.Libraries
{
    // [libs] Kütüphane yöneticisi: installed libraries (parts, KiCad, SPICE, Fritzing, Arduino)
    // with source, version, part count; enable/disable, update from source, remove, export;
    // id conflicts with a choice of which copy to use; search across every library.

    internal sealed class LibraryManagerWindow
    {
        private readonly Window _w;
        private readonly PartLibrary _lib;
        private readonly StackPanel _list = new StackPanel();
        private readonly StackPanel _side = new StackPanel();
        private readonly TextBox _search = new TextBox { Tag = "Bütün kütüphanelerde parça ara…", Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _sub = new TextBlock();
        private string _filter = "Tümü";

        /// <summary>Set when the user asked to show a part in the catalog.</summary>
        public string? ShowPartId { get; private set; }

        public LibraryManagerWindow(Window? owner, PartLibrary library)
        {
            _lib = library;
            _w = LibUi.Window(owner, "Kütüphane yöneticisi", 1240, 800);
            var root = new DockPanel();
            var head = new StackPanel();
            var titleRow = new DockPanel();
            var actions = Row(
                Btn("Klasörü aç", () => OpenFolder(UserLibrary.Folder), "Kullanıcı kütüphane klasörünü Gezgin'de aç"),
                Btn("Yenile", () =>
                {
                    LibraryManager.Refresh();
                    Build();
                }, "Klasördeki dosyaları yeniden tara"));
            DockPanel.SetDock(actions, Dock.Right);
            titleRow.Children.Add(actions);
            titleRow.Children.Add(Title("Kütüphane yöneticisi"));
            head.Children.Add(titleRow);
            _sub.Style = S("Text.Body");
            _sub.TextWrapping = TextWrapping.Wrap;
            _sub.Margin = new Thickness(0, 4, 0, 12);
            head.Children.Add(_sub);
            var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var f in new[] { "Tümü", "Kendi parçalarım", "KiCad", "SPICE", "Fritzing", "Arduino", "RobotWin (.rwlib)", "Kapalı" })
            {
                var chip = new ToggleButton { Content = f, Style = S("Chip"), Tag = f, IsChecked = f == _filter };
                chip.Click += (s, _) =>
                {
                    _filter = (string)((ToggleButton)s!).Tag;
                    foreach (ToggleButton c in filters.Children) c.IsChecked = (string)c.Tag == _filter;
                    Build();
                };
                filters.Children.Add(chip);
            }

            head.Children.Add(filters);
            _search.TextChanged += (_, _) => Build();
            head.Children.Add(_search);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var body = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            body.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) });
            var side = new ScrollViewer { Content = _side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(side, 2);
            body.Children.Add(side);
            root.Children.Add(body);
            _w.Content = new Border { Background = B("B.Base"), Padding = new Thickness(22, 18, 22, 16), Child = root };
            LibraryManager.Changed += OnChanged;
            _w.Closed += (_, _) => LibraryManager.Changed -= OnChanged;
            Build();
        }

        public Window Window => _w;

        public void ShowDialog() => _w.ShowDialog();

        private void OnChanged(object? sender, EventArgs e) => _w.Dispatcher.BeginInvoke(new Action(Build));

        private static void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
            }
        }

        private bool Matches(InstalledLibrary l) => _filter switch
        {
            "Kendi parçalarım" => l.Source == LibrarySourceKind.UserParts,
            "KiCad" => l.Source == LibrarySourceKind.KiCad,
            "SPICE" => l.Source == LibrarySourceKind.Spice,
            "Fritzing" => l.Source == LibrarySourceKind.Fritzing,
            "Arduino" => l.Source == LibrarySourceKind.Arduino,
            "RobotWin (.rwlib)" => l.Source == LibrarySourceKind.RobotWin,
            "Kapalı" => !l.Enabled,
            _ => true
        };

        private void Build()
        {
            var cat = LibraryManager.Catalog;
            // Arduino libraries installed outside the manager (copied by hand) appear too.
            foreach (var a in ArduinoLibraries.List(LibraryManager.ArduinoFolder))
            {
                if (!cat.Libraries.Any(l => l.Source == LibrarySourceKind.Arduino && string.Equals(l.File, a.Folder, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        cat.InstallArduino(a, a.Folder);
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            var builtIn = new HashSet<string>(PartLibrary.Default.Presets.Where(p => !UserLibrary.PartIds.Contains(p.Id)).Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            var conflicts = cat.Conflicts(builtIn);
            int parts = cat.Libraries.Where(l => l.Source != LibrarySourceKind.Arduino).Sum(l => l.PartIds.Count);
            _sub.Text = $"{cat.Libraries.Count} kütüphane · {parts} parça · {cat.Libraries.Count(l => !l.Enabled)} kapalı · {conflicts.Count} çakışma. Klasör: {cat.Folder}";

            _list.Children.Clear();
            string q = _search.Text.Trim();
            if (q.Length > 0)
            {
                _list.Children.Add(Overline("Arama sonuçları", 0));
                int n = 0;
                foreach (var (lib, id, name) in cat.Search(q).Take(300))
                {
                    n++;
                    var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    if (lib.Source != LibrarySourceKind.Arduino && lib.Enabled && _lib.FindPreset(id) != null)
                    {
                        var show = Btn("Göster", () =>
                        {
                            ShowPartId = id;
                            _w.Close();
                        });
                        DockPanel.SetDock(show, Dock.Right);
                        line.Children.Add(show);
                    }

                    var lab = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                    lab.Inlines.Add(new System.Windows.Documents.Run(name) { Foreground = B("B.Text") });
                    lab.Inlines.Add(new System.Windows.Documents.Run("   " + id + " · " + lib.Name + (lib.Enabled ? string.Empty : " (kapalı)")) { Foreground = B("B.TextMuted"), FontSize = 12 });
                    line.Children.Add(lab);
                    _list.Children.Add(line);
                }

                if (n == 0) _list.Children.Add(Caption("Eşleşen parça yok."));
            }
            else
            {
                foreach (var lib in cat.Libraries.Where(Matches).OrderBy(l => l.Source == LibrarySourceKind.Arduino ? 1 : 0).ThenBy(l => l.Priority).ThenBy(l => l.Name))
                {
                    _list.Children.Add(Entry(lib, conflicts));
                }

                if (_list.Children.Count == 0)
                {
                    _list.Children.Add(Body("Bu filtrede kütüphane yok. Kütüphane sayfasındaki 'Dış kütüphane' menüsünden KiCad, SPICE, Fritzing veya Arduino kütüphanesi ekleyin; ya da 'Parça oluştur' ile kendi parçanızı yapın."));
                }
            }

            BuildSide(conflicts);
        }

        private UIElement Entry(InstalledLibrary lib, List<LibraryConflict> conflicts)
        {
            var p = new StackPanel();
            var top = new DockPanel();
            var enabled = new CheckBox { IsChecked = lib.Enabled, Style = S("Switch"), VerticalAlignment = VerticalAlignment.Center, ToolTip = lib.Source == LibrarySourceKind.Arduino ? "Arduino kütüphaneleri her zaman derleyiciye açıktır" : "Açık/kapalı: kapalı kütüphanenin parçaları katalogda görünmez" };
            enabled.IsEnabled = lib.Source != LibrarySourceKind.Arduino;
            enabled.Click += (_, _) =>
            {
                bool on = enabled.IsChecked == true;
                if (!on)
                {
                    int used = LibraryManager.UsedInOpenProject(lib.PartIds);
                    if (used > 0 && Dialog.Show(_w, "Kütüphane kapatılsın mı?", $"Açık projede bu kütüphaneden {used} parça yerleştirilmiş. Kapatırsanız bu parçalar yeniden açılana kadar simüle edilemez.", "Kapat", "Vazgeç") != 0)
                    {
                        enabled.IsChecked = true;
                        return;
                    }
                }

                LibraryManager.SetEnabled(_lib, lib.Id, on);
                Workspace.Current.Status = $"'{lib.Name}' {(on ? "açıldı" : "kapatıldı")}";
            };
            DockPanel.SetDock(enabled, Dock.Right);
            top.Children.Add(enabled);
            var name = new TextBlock { Text = lib.Name, FontSize = Shell.TypeScale.Subtitle, Foreground = B(lib.Enabled ? "B.Text" : "B.TextMuted"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(name);
            nameRow.Children.Add(Chip(lib.SourceText, lib.Source switch
            {
                LibrarySourceKind.KiCad => "B.Accent",
                LibrarySourceKind.Spice => "B.Warning",
                LibrarySourceKind.Arduino => "B.Success",
                _ => "B.TextSecondary"
            }));
            int conflictCount = conflicts.Count(c => c.LibraryIds.Contains(lib.Id, StringComparer.OrdinalIgnoreCase));
            if (conflictCount > 0) nameRow.Children.Add(Chip(conflictCount + " çakışma", "B.Danger"));
            top.Children.Add(nameRow);
            p.Children.Add(top);

            string count = lib.Source == LibrarySourceKind.Arduino ? $"{lib.PartIds.Count} başlık dosyası" : $"{lib.PartIds.Count} parça" + (lib.FootprintCount > 0 ? $", {lib.FootprintCount} kılıf" : string.Empty);
            p.Children.Add(new TextBlock
            {
                Text = $"Sürüm {(string.IsNullOrEmpty(lib.Version) ? "—" : lib.Version)} · {count} · {(string.IsNullOrEmpty(lib.Author) ? "yazar yok" : lib.Author)} · kuruldu {lib.InstalledAt.ToLocalTime():dd.MM.yyyy}" + (lib.UpdatedAt > lib.InstalledAt.AddMinutes(1) ? $", güncellendi {lib.UpdatedAt.ToLocalTime():dd.MM.yyyy HH:mm}" : string.Empty),
                Style = S("Text.Caption"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            if (!string.IsNullOrWhiteSpace(lib.Description)) p.Children.Add(new TextBlock { Text = lib.Description, Style = S("Text.Caption"), TextWrapping = TextWrapping.Wrap, Foreground = B("B.TextSecondary") });
            if (lib.SourcePaths.Count > 0) p.Children.Add(new TextBlock { Text = "Kaynak: " + string.Join("; ", lib.SourcePaths), Style = S("Text.Caption"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = string.Join("\n", lib.SourcePaths) });

            var buttons = Row();
            buttons.Margin = new Thickness(-6, 6, 0, 0);
            bool canUpdate = lib.SourcePaths.Any(sp => File.Exists(sp) || Directory.Exists(sp)) && lib.Source != LibrarySourceKind.UserParts && lib.Source != LibrarySourceKind.RobotWin;
            var update = Btn("Güncelle", () => Update(lib), canUpdate ? "Kaynak dosyalardan yeniden içe aktar (önizlemeyle)" : "Kaynak dosyalar bulunamadı");
            update.IsEnabled = canUpdate;
            buttons.Children.Add(update);
            if (lib.Source != LibrarySourceKind.Arduino) buttons.Children.Add(Btn("Dışa aktar (.rwlib)…", () => Export(lib), "Paylaşmak için tek dosya"));
            buttons.Children.Add(Btn("Parçalar", () =>
            {
                _search.Text = lib.Name;
            }, "Bu kütüphanenin parçalarını listele"));
            buttons.Children.Add(Btn("Kaldır", () => RemoveLib(lib), "Kütüphaneyi kaldır (dosyalar 'Kaldırılanlar' klasörüne taşınır)", ghost: true));
            p.Children.Add(buttons);
            var card = Card(p, 14);
            card.Margin = new Thickness(0, 0, 0, 10);
            return card;
        }

        private void BuildSide(List<LibraryConflict> conflicts)
        {
            _side.Children.Clear();
            _side.Children.Add(new TextBlock { Text = "ÇAKIŞMALAR", Style = S("Text.Overline") });
            if (conflicts.Count == 0)
            {
                _side.Children.Add(Caption("Aynı kimlikle birden çok kütüphanede bulunan parça yok."));
            }
            else
            {
                _side.Children.Add(Caption("Aynı parça kimliği birden çok yerde. Hangi kopyanın kullanılacağını seçin (yerleşik katalog her zaman önce gelir; kendi parçanıza yeni bir kimlik vermek için Parça oluşturucuyla kopyalayın)."));
                foreach (var c in conflicts.Take(60))
                {
                    var box = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                    box.Children.Add(new TextBlock { Text = c.PartId, Style = S("Text.Mono"), Foreground = B(c.Resolved ? "B.Text" : "B.Warning") });
                    var choices = c.LibraryIds.ToList();
                    string Label(string id) => id == LibraryCatalog.BuiltInId ? "Yerleşik katalog" : LibraryManager.Catalog.Find(id)?.Name ?? id;
                    if (choices[0] == LibraryCatalog.BuiltInId)
                    {
                        box.Children.Add(Caption("Yerleşik parçayla aynı kimlik: yerleşik olan kullanılır (" + string.Join(", ", choices.Skip(1).Select(Label)) + " gizli)."));
                    }
                    else
                    {
                        var combo = Combo(choices, c.Winner, Label, id => LibraryManager.Resolve(_lib, c.PartId, id));
                        combo.Margin = new Thickness(0, 3, 0, 0);
                        box.Children.Add(combo);
                    }

                    _side.Children.Add(box);
                }
            }

            _side.Children.Add(new TextBlock { Text = "ARDUINO KÜTÜPHANE KLASÖRÜ", Style = S("Text.Overline"), Margin = new Thickness(0, 22, 0, 6) });
            _side.Children.Add(Caption(LibraryManager.ArduinoFolder));
            _side.Children.Add(Caption("Kod sayfası bu klasörü arduino-cli'ye --libraries ile verir; ayrıca arduino-cli'nin kendi kütüphaneleri (Belgeler\\Arduino\\libraries) de görülür."));
            _side.Children.Add(Row(Btn("Klasörü aç", () => OpenFolder(LibraryManager.ArduinoFolder))));
            if (UserLibrary.LoadErrors.Count > 0 || LibraryManager.Catalog.Log.Issues.Count > 0)
            {
                _side.Children.Add(new TextBlock { Text = "YÜKLEME HATALARI", Style = S("Text.Overline"), Margin = new Thickness(0, 22, 0, 6) });
                var log = new ImportLog();
                foreach (var e in UserLibrary.LoadErrors) log.Error(e);
                log.AddRange(LibraryManager.Catalog.Log.Issues);
                _side.Children.Add(Issues(log.Issues, 260));
            }
        }

        private void Update(InstalledLibrary lib)
        {
            var paths = lib.SourcePaths.Where(sp => File.Exists(sp) || Directory.Exists(sp)).ToList();
            ImportAdapter? adapter = lib.Source switch
            {
                LibrarySourceKind.KiCad => new KiCadAdapter(KiCadImportSession.Open(paths, _lib)),
                LibrarySourceKind.Spice => new SpiceAdapter(SpiceImportSession.Open(paths)),
                LibrarySourceKind.Fritzing => new FritzingAdapter(paths),
                LibrarySourceKind.Arduino => new ArduinoAdapter(paths, update: true),
                _ => null
            };
            if (adapter == null) return;
            if (lib.Source != LibrarySourceKind.Arduino) adapter.ReplaceId = lib.Id;
            var log = new ImportWindow(_w, adapter).ShowDialog();
            if (log != null) Workspace.Current.Status = $"'{lib.Name}' güncellendi: {log.Summary()}";
            Build();
        }

        private void Export(InstalledLibrary lib)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter("RobotWin kütüphanesi (*.rwlib)|*.rwlib"), FileName = lib.Name.Replace(':', '_'), DefaultExt = RobotWinFormats.LibraryExtension };
            if (dlg.ShowDialog(_w) != true) return;
            try
            {
                if (lib.Id == LibraryCatalog.UserPartsId) UserLibrary.Export(_lib, dlg.FileName, "Kendi parçalarım");
                else File.Copy(Path.Combine(LibraryManager.Catalog.Folder, lib.File), dlg.FileName, true);
                Workspace.Current.Status = "Kütüphane dışa aktarıldı: " + Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(_w, "Dışa aktarılamadı", ex.Message, "Tamam");
            }
        }

        private void RemoveLib(InstalledLibrary lib)
        {
            int used = LibraryManager.UsedInOpenProject(lib.PartIds);
            string what = lib.Source == LibrarySourceKind.Arduino ? "Arduino kütüphanesi klasörü" : $"{lib.PartIds.Count} parça";
            string msg = $"'{lib.Name}' kaldırılacak ({what}). Dosyalar silinmez, kullanıcı kütüphanesindeki 'Kaldırılanlar' klasörüne taşınır." +
                         (used > 0 ? $"\n\nAçık projede bu kütüphaneden {used} parça yerleştirilmiş; proje bu parçalar olmadan simüle edilemez." : string.Empty);
            if (Dialog.Show(_w, "Kütüphane kaldırılsın mı?", msg, "Kaldır", "Vazgeç") != 0) return;
            try
            {
                LibraryManager.Remove(_lib, lib.Id);
                Workspace.Current.Status = $"'{lib.Name}' kaldırıldı";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(_w, "Kaldırılamadı", ex.Message, "Tamam");
            }

            Build();
        }
    }
}
