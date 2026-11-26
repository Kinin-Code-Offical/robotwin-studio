using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Libraries;
using RobotWin.Studio.Libraries;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    // [libs] Kütüphane page: external libraries (KiCad, SPICE, Fritzing, Arduino), batch
    // import, the part creator, the library manager and extra detail for user-made parts.

    public partial class LibraryPage
    {
        private Window? Owner => Window.GetWindow(this);

        private void OnExternalMenu(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            void Item(string text, Action act, string? tip = null)
            {
                var mi = new MenuItem { Header = text, ToolTip = tip };
                mi.Click += (_, _) => act();
                menu.Items.Add(mi);
            }

            Item("KiCad sembol / footprint dosyaları…", () => PickFiles("KiCad kütüphaneleri (*.kicad_sym, *.kicad_mod)|*.kicad_sym;*.kicad_mod", paths => ShowImport(new KiCadAdapter(KiCadImportSession.Open(paths, Lib)))),
                ".kicad_sym sembolleri ve .kicad_mod footprint'leri; önizleme, model ve pad eşlemesi");
            Item("KiCad .pretty klasörü / kütüphane klasörü…", () => PickFolders("KiCad .pretty klasörü ya da kütüphane klasörü", paths => ShowImport(new KiCadAdapter(KiCadImportSession.Open(paths, Lib)))));
            menu.Items.Add(new Separator());
            Item("SPICE model dosyaları (.lib, .mod, .cir, .sub)…", () => PickFiles("SPICE (*.lib, *.mod, *.cir, *.sub, *.sp, *.spi)|*.lib;*.mod;*.cir;*.sub;*.sp;*.spi;*.inc|Tüm dosyalar (*.*)|*.*", paths => ShowImport(new SpiceAdapter(SpiceImportSession.Open(paths)))),
                ".model kartları ve .subckt alt devreleri");
            Item("Fritzing parçaları (.fzpz)…", () => PickFiles("Fritzing parçası (*.fzpz, *.fzp)|*.fzpz;*.fzp", paths => ShowImport(new FritzingAdapter(paths))));
            Item("FMU (FMI 2.0, .fmu)…", () => PickFiles("FMU (*.fmu)|*.fmu", ImportFmus),
                "Modelica, Simulink, 20-sim … modelleri: ortak benzetim (Co-Simulation) FMU'su, girişleri ve çıkışları pin olan bir parça olur");
            menu.Items.Add(new Separator());
            Item("Arduino kütüphanesi (.zip)…", () => PickFiles("Arduino kütüphanesi (*.zip)|*.zip", paths => ShowImport(new ArduinoAdapter(paths))), "library.properties içeren .zip; Kod sayfasının derleyicisi bulur");
            Item("Arduino kütüphanesi (klasör)…", () => PickFolders("Arduino kütüphane klasörü", paths => ShowImport(new ArduinoAdapter(paths))));
            menu.Items.Add(new Separator());
            Item("Toplu içe aktar (klasör tara)…", BatchImport, "Bir klasördeki bütün KiCad, SPICE, Fritzing ve Arduino kütüphanelerini önizlemesiz, varsayılan eşlemeyle içe aktar ve rapor göster");
            Item("RobotWin parça / kütüphane (.rwpart, .rwlib)…", () => OnImport(this, new RoutedEventArgs()));
            menu.IsOpen = true;
        }

        /// <summary>[fmi] Each FMU becomes a part of the user library (the FMU file inside it); the first is shown.</summary>
        private void ImportFmus(IReadOnlyList<string> paths)
        {
            string? first = null;
            foreach (var path in paths)
            {
                try
                {
                    var part = RobotTwin.CoreSim.Fmi.FmuImport.ToCustomPart(System.IO.File.ReadAllBytes(path));
                    Libraries.LibraryManager.SaveUserPart(Lib, part);
                    first ??= part.Id;
                }
                catch (Exception ex) when (ex is NotSupportedException || ex is System.IO.InvalidDataException || ex is FormatException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
                {
                    Dialog.Show(Owner, "FMU içe aktarılamadı", System.IO.Path.GetFileName(path) + ": " + ex.Message, "Tamam");
                }
            }

            if (first == null) return;
            Workspace.Current.Status = string.Format(System.Globalization.CultureInfo.InvariantCulture, RobotWin.Studio.Shell.Lang.T("{0} FMU parça olarak eklendi"), paths.Count);
            Refresh(first);
        }

        private void PickFiles(string filter, Action<IReadOnlyList<string>> run)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter(filter), Multiselect = true, Title = RobotWin.Studio.Shell.Lang.T("İçe aktarılacak dosyalar") };
            if (dlg.ShowDialog(Owner) == true) run(dlg.FileNames);
        }

        private void PickFolders(string title, Action<IReadOnlyList<string>> run)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = RobotWin.Studio.Shell.Lang.T(title), Multiselect = true };
            if (dlg.ShowDialog(Owner) == true) run(dlg.FolderNames);
        }

        private void ShowImport(ImportAdapter adapter)
        {
            ImportLog? log;
            try
            {
                log = new ImportWindow(Owner, adapter).ShowDialog();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is FormatException || ex is InvalidOperationException)
            {
                Dialog.Show(Owner!, "İçe aktarılamadı", ex.Message, "Tamam");
                return;
            }

            if (log == null) return;
            Workspace.Current.Status = adapter.Title + ": " + log.Summary();
            RefreshAll();
        }

        private void RefreshAll()
        {
            Refresh(null);
            LibraryManager.Refresh();
        }

        /// <summary>Scans a folder for every supported library and imports all of them with the defaults.</summary>
        private void BatchImport()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = RobotWin.Studio.Shell.Lang.T("Taranacak klasör (alt klasörler dahil)") };
            if (dlg.ShowDialog(Owner) != true) return;
            var report = RunBatch(dlg.FolderName, Lib);
            Workspace.Current.Status = "Toplu içe aktarma: " + report.Summary();
            RefreshAll();
            LibUi.Report(Owner, "Toplu içe aktarma raporu", "Klasör: " + dlg.FolderName + "\n" + report.Summary() + "\n\n" + report);
        }

        internal static ImportLog RunBatch(string folder, PartLibrary library)
        {
            var report = new ImportLog();
            List<string> All(string pattern) => Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories).Take(5000).ToList();
            try
            {
                var kicad = All("*.kicad_sym").Concat(Directory.EnumerateDirectories(folder, "*.pretty", SearchOption.AllDirectories)).ToList();
                if (kicad.Count > 0) report.AddRange(ImportWindow.Batch(new KiCadAdapter(KiCadImportSession.Open(kicad, library)), library).Issues);

                var spice = new List<string>();
                foreach (var f in All("*.*").Where(f => new[] { ".lib", ".mod", ".sub", ".cir", ".sp", ".spi" }.Contains(Path.GetExtension(f).ToLowerInvariant())))
                {
                    // KiCad 5 symbol libraries also use .lib.
                    string head;
                    using (var r = new StreamReader(f)) head = r.ReadLine() ?? string.Empty;
                    if (head.StartsWith("EESchema-LIBRARY", StringComparison.Ordinal))
                    {
                        report.Warn("KiCad 5 eski sembol kütüphanesi; atlandı (KiCad 6+ ile .kicad_sym olarak kaydedin).", f);
                        continue;
                    }

                    spice.Add(f);
                }

                if (spice.Count > 0) report.AddRange(ImportWindow.Batch(new SpiceAdapter(SpiceImportSession.Open(spice)), library).Issues);
                var fz = All("*.fzpz");
                if (fz.Count > 0) report.AddRange(ImportWindow.Batch(new FritzingAdapter(fz), library).Issues);
                var arduino = Directory.EnumerateFiles(folder, "library.properties", SearchOption.AllDirectories).Select(Path.GetDirectoryName).Where(d => d != null && !d.Contains(Path.DirectorySeparatorChar + "examples")).Distinct().Cast<string>().ToList();
                if (arduino.Count > 0) report.AddRange(ImportWindow.Batch(new ArduinoAdapter(arduino), library).Issues);
                if (kicad.Count + spice.Count + fz.Count + arduino.Count == 0) report.Warn("klasörde desteklenen kütüphane bulunamadı (.kicad_sym, .pretty, .lib/.mod/.sub/.cir, .fzpz, library.properties).", folder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                report.Error("klasör taranamadı: " + ex.Message, folder);
            }

            return report;
        }

        private void OnCreatePart(object sender, RoutedEventArgs e) => OpenCreator(null, null);

        private void OpenCreator(CustomPart? existing, PartPreset? basis)
        {
            var creator = new PartCreatorWindow(Owner, Lib, existing, basis);
            if (creator.ShowDialog() == true && creator.SavedId != null) Refresh(creator.SavedId);
        }

        private void OnOpenManager(object sender, RoutedEventArgs e)
        {
            var w = new LibraryManagerWindow(Owner, Lib);
            w.ShowDialog();
            Refresh(w.ShowPartId);
        }

        /// <summary>The user part behind a preset: its .rwpart, else the copy inside its library file.</summary>
        private static CustomPart? LoadUserPart(PartPreset preset) => LibraryManager.FindUserPart(preset.Id);

        /// <summary>Extra rows in the detail panel for user parts: edit, datasheet, origin, breadboard picture.</summary>
        private void AddUserPartDetail(PartPreset preset, Panel actions)
        {
            bool user = UserLibrary.PartIds.Contains(preset.Id);
            var edit = new Button
            {
                Content = user ? "Düzenle" : "Oluşturucuda aç",
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = user ? "Bacaklar, sembol, kılıf, model, parametreler, 3B (Parça oluşturucu)" : "Bu parçayı temel alıp bacaklarını, kılıfını ve modelini değiştirerek yeni parça yap"
            };
            edit.Click += (_, _) =>
            {
                if (user && LoadUserPart(preset) is { } part) OpenCreator(part, null);
                else OpenCreator(null, preset);
            };
            actions.Children.Add(edit);

            var def = Lib.Find(preset.PartId);
            var cp = user ? LoadUserPart(preset) : null;
            if (cp != null && !string.IsNullOrWhiteSpace(cp.Datasheet))
            {
                var ds = new Button { Content = "Veri sayfası", Margin = new Thickness(8, 6, 0, 0), Style = (Style)FindResource("Btn.Ghost"), ToolTip = cp.Datasheet };
                ds.Click += (_, _) => PartCreatorWindowOpen(cp.Datasheet);
                actions.Children.Add(ds);
            }

            var spec = cp?.Definition;
            if (spec == null) return;
            string model = spec.Model.Kind switch
            {
                UserModelKind.BuiltIn => "yerleşik model " + (Lib.Find(spec.Model.BuiltInPartId)?.Name ?? spec.Model.BuiltInPartId),
                UserModelKind.Subcircuit => "SPICE alt devresi " + spec.Model.SubcircuitName,
                UserModelKind.Behavioral => UserPartSpec.BehavioralName(spec.Model.Behavior),
                _ => "elektriksel model yok"
            };
            Detail.Children.Add(new TextBlock
            {
                Text = $"{spec.Pins.Count} bacak · {model}" + (spec.Source.Length > 0 ? " · kaynak: " + spec.Source : string.Empty),
                Style = (Style)FindResource("Text.Caption"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            if (!string.IsNullOrEmpty(spec.BreadboardSvg))
            {
                var img = SvgFlatImage.Render(spec.BreadboardSvg, out _);
                if (img != null)
                {
                    Detail.Children.Add(new TextBlock { Text = "Breadboard görünümü", Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 18, 0, 8) });
                    Detail.Children.Add(new Border
                    {
                        Height = 150, CornerRadius = new CornerRadius(6), BorderBrush = (Brush)FindResource("B.Border"), BorderThickness = new Thickness(1),
                        Child = new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(8) }
                    });
                }
            }
        }

        private static void PartCreatorWindowOpen(string target) => PartCreatorWindow.OpenDatasheet(target);

        // ================================================================== snapshots

        /// <summary>--snapshot --page library --fault creator|creator-model|manager|import-spice|import-kicad: renders a library window.</summary>
        internal void PrepareSnapshot(string? fault)
        {
            Window? w = null;
            if (fault != null && fault.StartsWith("part:", StringComparison.Ordinal) && Lib.FindPreset(fault.Substring(5)) is { } shown)
            {
                // [libs] A part's sheet with its category picked (docs pictures): "part:acdc-psu".
                _category = RobotTwin.CoreSim.Design.PartCategories.PathOf(shown);
                BuildCategoryChips();
                Filter();
                PartGrid.SelectedItem = shown;
                ShowDetail(shown);
                return;
            }

            switch (fault)
            {
                case "creator":
                case "creator-model":
                case "creator-3d":
                {
                    var c = new PartCreatorWindow(Owner, Lib);
                    c.LoadSample();
                    c.ShowStep(fault == "creator" ? 1 : fault == "creator-model" ? 4 : 6);
                    w = c.Window;
                    break;
                }

                case "manager":
                {
                    // Sample libraries in a temporary folder: the user's own library is never touched.
                    string? dir = FindFixtures();
                    string temp = Path.Combine(Path.GetTempPath(), "robotwin-snapshot-libs-" + Guid.NewGuid().ToString("N"));
                    var cat = LibraryCatalog.Load(temp);
                    if (dir != null)
                    {
                        var scratch = new PartLibrary();
                        foreach (var p in Lib.Parts) scratch.Add(p);
                        var log = new ImportLog();
                        var spice = SpiceImportSession.Open(new[] { Path.Combine(dir, "parts.lib") });
                        cat.Install(spice.Commit(scratch, log), LibrarySourceKind.Spice, spice.Paths);
                        var kicad = KiCadImportSession.Open(new[] { Path.Combine(dir, "Test.kicad_sym"), Path.Combine(dir, "Test.pretty") }, scratch);
                        var kfile = kicad.Commit(scratch, log);
                        cat.Install(kfile, LibrarySourceKind.KiCad, kicad.Paths);
                        var copy = new PartLibraryFile { Name = "Ekip ortak kütüphanesi", Author = "Ar-Ge", Version = "3.2.0", Description = "Paylaşılan .rwlib (KiCad parçalarının eski kopyası)" };
                        copy.Parts.AddRange(kfile.Parts.Take(2));
                        cat.Install(copy, LibrarySourceKind.RobotWin, new string[0]);
                        var fz = FritzingImporter.ToCustomPart(FritzingImporter.Read(Path.Combine(dir, "test_sensor.fzpz")), log);
                        if (fz != null) cat.Install(new PartLibraryFile { Name = "Fritzing: test_sensor", Author = "Fritzing içe aktarımı", Version = "4", Parts = { fz } }, LibrarySourceKind.Fritzing, new[] { Path.Combine(dir, "test_sensor.fzpz") });
                    }

                    LibraryManager.UseCatalog(cat);
                    w = new LibraryManagerWindow(Owner, Lib).Window;
                    break;
                }
                case "import-spice":
                case "import-kicad-opamp":
                case "import-kicad":
                case "import-fritzing":
                {
                    string? dir = FindFixtures();
                    if (dir == null) return;
                    ImportAdapter a = fault == "import-spice"
                        ? new SpiceAdapter(SpiceImportSession.Open(new[] { Path.Combine(dir, "parts.lib") }))
                        : fault == "import-fritzing" ? new FritzingAdapter(Directory.GetFiles(dir, "*.fzpz"))
                        : new KiCadAdapter(KiCadImportSession.Open(new[] { Path.Combine(dir, "Test.kicad_sym"), Path.Combine(dir, "Test.pretty") }, Lib));
                    var iw = new ImportWindow(Owner, a);
                    if (fault == "import-spice") iw.Select("AMP10");
                    if (fault == "import-kicad-opamp") iw.Select("TestOpAmp");
                    w = iw.Window;
                    break;
                }
            }

            if (w == null) return;
            var main = Owner;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = main?.Left ?? -3000;
            w.Top = 0;
            w.Width = main?.Width ?? 1600;
            w.Height = main?.Height ?? 960;
            w.ShowActivated = false;
            w.Show();
            App.SnapshotTarget = w.Content as FrameworkElement;
        }

        private static string? FindFixtures()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string p = Path.Combine(d.FullName, "CoreSim", "tests", "RobotTwin.CoreSim.Tests", "Fixtures", "Libs");
                if (Directory.Exists(p)) return p;
            }

            return null;
        }
    }
}
