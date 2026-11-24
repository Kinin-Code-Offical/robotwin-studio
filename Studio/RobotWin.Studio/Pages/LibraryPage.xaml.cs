using System;
using RobotTwin.CoreSim.Formats;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Editor;
using RobotWin.Studio.Pcb;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    public partial class LibraryPage : UserControl
    {
        private readonly MainWindow _shell;
        private string _category = "Tümü";

        private static PartLibrary Lib => Workspace.Current.Library;

        public LibraryPage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();
            Subtitle.Text = $"{Lib.Presets.Count} gerçek parça · {Lib.Parts.Count()} simülasyon modeli · {Lib.Categories.Count()} kategori. Her parçanın elektriksel modeli, PCB kılıfı ve bacak eşleşmesi var.";
            BuildCategoryChips();

            Filter();
            ShowDetail(Lib.Presets.FirstOrDefault(p => p.Id == "lm7805") ?? Lib.Presets[0]);
        }

        /// <summary>[libs] Main-category chips; picking one shows its sub-categories in a second row.</summary>
        private void BuildCategoryChips()
        {
            Categories.Children.Clear();
            var tree = RobotTwin.CoreSim.Design.PartCategories.Tree(Lib.Presets);
            string main = _category.Split(new[] { RobotTwin.CoreSim.Design.PartCategories.Separator }, StringSplitOptions.None)[0];
            ToggleButton Chip(string text, string tag, bool on, bool sub = false)
            {
                var chip = new ToggleButton { Content = text, Style = (Style)FindResource("Chip"), Tag = tag, IsChecked = on, Margin = new Thickness(0, 0, 4, 4) };
                if (sub) chip.FontSize = 12;
                chip.Click += (s, _) =>
                {
                    _category = (string)((ToggleButton)s!).Tag;
                    BuildCategoryChips();
                    Filter();
                };
                return chip;
            }

            Categories.Children.Add(Chip($"Tümü ({Lib.Presets.Count})", "Tümü", _category == "Tümü"));
            foreach (var (m, count, _) in tree) Categories.Children.Add(Chip($"{m} ({count})", m, m == main));
            var subs = tree.FirstOrDefault(t => t.Main == main).Subs;
            if (subs != null && subs.Count > 1)
            {
                // The sub-categories of the picked one, on a line of their own.
                Categories.Children.Add(new Border { Width = 4000, Height = 0 });
                Categories.Children.Add(new TextBlock { Text = main + " ›", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 4), Foreground = (Brush)FindResource("B.TextMuted") });
                foreach (var (s, n) in subs)
                {
                    string tag = main + RobotTwin.CoreSim.Design.PartCategories.Separator + s;
                    Categories.Children.Add(Chip($"{s} ({n})", tag, _category == tag, true));
                }
            }
        }

        private void OnSearch(object sender, TextChangedEventArgs e) => Filter();

        private void Filter()
        {
            // [lang] the shown (translated) texts and the Turkish ones alike
            var tokens = RobotWin.Studio.Shell.Lang.Fold(SearchBox.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            PartGrid.ItemsSource = Lib.Presets.Where(p =>
                RobotTwin.CoreSim.Design.PartCategories.Matches(p, _category) &&
                (tokens.Length == 0 || RobotWin.Studio.Shell.Lang.Searchable(p.Name, p.Description, RobotTwin.CoreSim.Design.PartCategories.PathOf(p), p.Id) is var hay && tokens.All(t => hay.Contains(t))))
                .OrderBy(p => IsSeriesResistor(p) ? 1 : 0).ToList();
        }

        /// <summary>
        /// [libs] The part's electrical values as a datasheet table: every parameter of its simulation model with the value
        /// this part number sets (bold where it differs from the model's default), its unit and the choices it allows; then
        /// its pins and the model it is simulated with.
        /// </summary>
        private void AddSpecTable(RobotTwin.CoreSim.Design.PartDefinition def, IReadOnlyDictionary<string, string> values)
        {
            if (def.Parameters.Count == 0 && def.Pins.Count == 0) return;
            Detail.Children.Add(new TextBlock { Text = "Özellikler", Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 0, 0, 6) });
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int row = 0;
            void Row(string label, string value, bool strong = false, string? tip = null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 2, 10, 2), TextWrapping = TextWrapping.Wrap };
                var v = new TextBlock { Text = value, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, ToolTip = tip };
                v.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
                Grid.SetRow(l, row);
                Grid.SetRow(v, row);
                Grid.SetColumn(v, 1);
                grid.Children.Add(l);
                grid.Children.Add(v);
                row++;
            }

            foreach (var p in def.Parameters)
            {
                string value = values.TryGetValue(p.Key, out var v) && v.Length > 0 ? v : p.DefaultValue;
                if (value.Length == 0 && p.Choices == null) continue;
                string word = value switch { "on" => "açık", "off" => "kapalı", "dc" => "DC", "ac" => "AC", "acdc" => "AC + DC", "true" => "evet", "false" => "hayır", _ => value };
                string shown = word == value ? WithUnit(value, p.Unit) : word;
                Row(p.Label, shown, value != p.DefaultValue, p.Choices != null ? "Seçenekler: " + string.Join(", ", p.Choices) : "Model varsayılanı: " + WithUnit(p.DefaultValue, p.Unit));
            }

            if (def.Pins.Count > 0) Row("Bacaklar", string.Join(", ", def.Pins.Select(pin => pin.Name)));
            Row("Simülasyon modeli", def.Name + " (" + def.Id + ")");
            Detail.Children.Add(grid);
        }

        /// <summary>The E12 resistor series goes to the end so the catalog opens on varied parts.</summary>
        /// <summary>
        /// A parameter's value with its unit: an SI prefix written on the number goes onto the unit ("5m" A → "5 mA",
        /// "4.7k" Ω → "4.7 kΩ"); a value that already ends with its unit is shown as it is.
        /// </summary>
        public static string WithUnit(string value, string? unit)
        {
            if (string.IsNullOrEmpty(unit) || value.Length == 0 || value.EndsWith(unit, StringComparison.Ordinal)) return value;
            var m = System.Text.RegularExpressions.Regex.Match(value.Trim(), @"^([-+]?[0-9]+(?:[.,][0-9]+)?(?:[eE][-+]?[0-9]+)?)\s*([pnuµmkMG])?$");
            if (!m.Success) return value + " " + unit;
            string prefix = m.Groups[2].Value == "u" ? "µ" : m.Groups[2].Value;
            return m.Groups[1].Value + " " + prefix + unit;
        }

        internal static bool IsSeriesResistor(PartPreset p) => p.PartId == "resistor" && !p.Id.Contains("0805") && !p.Id.Contains("W");

        private void OnPick(object sender, SelectionChangedEventArgs e)
        {
            if (PartGrid.SelectedItem is PartPreset preset) ShowDetail(preset);
        }

        private void ShowDetail(PartPreset preset)
        {
            Detail.Children.Clear();
            var def = Lib.Get(preset.PartId);
            var parameters = Lib.ResolveParameters(def, preset, null);

            Detail.Children.Add(new TextBlock { Text = RobotTwin.CoreSim.Design.PartCategories.PathOf(preset), Style = (Style)FindResource("Text.Overline") });
            Detail.Children.Add(new TextBlock { Text = preset.Name, Style = (Style)FindResource("Text.Title"), TextWrapping = TextWrapping.Wrap });
            Detail.Children.Add(new TextBlock { Text = preset.Description, Style = (Style)FindResource("Text.Body"), Margin = new Thickness(0, 6, 0, 16), TextWrapping = TextWrapping.Wrap });
            AddSpecTable(def, parameters);

            var place = new Button { Style = (Style)FindResource("Btn.Primary"), Content = "Devreye ekle", Padding = new Thickness(18, 8, 18, 8) };
            place.Click += (_, _) => _shell.PlacePart(preset.Id);
            var derive = new Button { Content = "Bundan yeni parça", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Aynı simülasyon modeliyle kendi parametrelerinizle bir parça oluşturun (.rwpart)" };
            derive.Click += (_, _) => ShowEditor(preset);
            var actions = new WrapPanel(); // [libs] wraps when a part has more actions
            actions.Children.Add(place);
            actions.Children.Add(derive);
            if (UserLibrary.PartIds.Contains(preset.Id))
            {
                var export = new Button { Content = "Dışa aktar", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Bu parçayı .rwpart dosyası olarak kaydet" };
                export.Click += (_, _) => ExportPart(preset);
                actions.Children.Add(export);
            }

            Detail.Children.Add(actions);
            AddUserPartDetail(preset, actions); // [libs] edit in the part creator, datasheet, origin, breadboard picture

            Detail.Children.Add(new TextBlock { Text = "Şematik sembol", Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 22, 0, 8) });
            Detail.Children.Add(new Border
            {
                Height = 150,
                CornerRadius = new CornerRadius(6),
                BorderBrush = (Brush)FindResource("B.Border"),
                BorderThickness = new Thickness(1),
                Child = new SymbolPreview { PresetId = preset.Id, Margin = new Thickness(28) }
            });

            string fp = def.ResolveFootprint(parameters);
            if (!string.IsNullOrEmpty(fp))
            {
                Detail.Children.Add(new TextBlock { Text = "PCB kılıfı · " + fp, Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 18, 0, 8) });
                Detail.Children.Add(new Border
                {
                    Height = 150,
                    CornerRadius = new CornerRadius(6),
                    ClipToBounds = true,
                    Child = new FootprintPreview { FootprintId = fp }
                });

                var pins = string.Join("   ", def.Pins.Select(p => $"{p.Name}→{def.ResolvePad(p, parameters)}"));
                Detail.Children.Add(new TextBlock { Text = "Bacaklar: " + pins, Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            }

            // 3D model (generated, or the user's own STL/OBJ for user parts).
            var custom = RobotTwin.CoreSim.Design.PartModels3D.For(parameters);
            Detail.Children.Add(new TextBlock
            {
                Text = "3B model" + (custom != null ? " · " + custom.Name : string.Empty),
                Style = (Style)FindResource("Text.Overline"),
                Margin = new Thickness(0, 18, 0, 8)
            });
            var preview = new RobotWin.Studio.Models3D.ModelPreview { Height = 190, ToolTip = "Sürükleyerek döndürün, teker ile yakınlaştırın" };
            Detail.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = preview });
            preview.Show(def, parameters);
            if (UserLibrary.PartIds.Contains(preset.Id))
            {
                var modelRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                var assign = new Button { Content = custom != null ? "3B modeli değiştir…" : "3B model ata…", ToolTip = "Bu parça için kendi STL / OBJ / PLY modelinizi kullanın (parçayla birlikte .rwpart içinde saklanır)" };
                assign.Click += (_, _) => AssignModel(preset);
                modelRow.Children.Add(assign);
                if (custom != null)
                {
                    var remove = new Button { Content = "Üretilen modele dön", Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("Btn.Ghost") };
                    remove.Click += (_, _) => SetModel(preset, null);
                    modelRow.Children.Add(remove);
                }

                Detail.Children.Add(modelRow);
            }

            Detail.Children.Add(new TextBlock { Text = "Model parametreleri", Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 18, 0, 8) });
            foreach (var p in def.Parameters.Where(p => p.Key != "pinmap" && p.Key != "footprint"))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
                string value = parameters.TryGetValue(p.Key, out var v) ? v : p.DefaultValue;
                var val = new TextBlock { Text = WithUnit(value, p.Unit), Style = (Style)FindResource("Text.Mono"), HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(val, Dock.Right);
                row.Children.Add(val);
                row.Children.Add(new TextBlock { Text = p.Label, Style = (Style)FindResource("Text.Body") });
                Detail.Children.Add(row);
            }
        }

        // ================================================================ user parts

        private void Refresh(string? selectId = null)
        {
            Subtitle.Text = $"{Lib.Presets.Count} gerçek parça · {Lib.Parts.Count()} simülasyon modeli · {Lib.Categories.Count()} kategori · {UserLibrary.PartIds.Count} kullanıcı parçası.";
            var existing = Categories.Children.OfType<ToggleButton>().Select(c => (string)c.Tag).ToHashSet();
            foreach (var cat in Lib.Categories.Where(c => !existing.Contains(c)))
            {
                var chip = new ToggleButton { Content = cat, Style = (Style)FindResource("Chip"), Tag = cat };
                chip.Click += (s, _) =>
                {
                    _category = (string)((ToggleButton)s!).Tag;
                    foreach (ToggleButton c in Categories.Children) c.IsChecked = (string)c.Tag == _category;
                    Filter();
                };
                Categories.Children.Add(chip);
            }

            Filter();
            if (selectId != null && Lib.FindPreset(selectId) is { } p)
            {
                PartGrid.SelectedItem = p;
                ShowDetail(p);
            }
        }

        private void ShowEditor(PartPreset basis)
        {
            Detail.Children.Clear();
            var def = Lib.Get(basis.PartId);
            var values = Lib.ResolveParameters(def, basis, null);
            Detail.Children.Add(new TextBlock { Text = "Yeni parça · " + def.Name + " modeli", Style = (Style)FindResource("Text.Overline") });
            Detail.Children.Add(new TextBlock { Text = "Kendi parçanız", Style = (Style)FindResource("Text.Title") });
            Detail.Children.Add(new TextBlock
            {
                Text = "Elektriksel model, sembol ve kılıf " + basis.Name + " parçasından gelir; değerleri değiştirip kendi parça numaranızla kaydedin. Parça kullanıcı kütüphanesine .rwpart olarak yazılır.",
                Style = (Style)FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14)
            });

            TextBox Field(string label, string value)
            {
                Detail.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 0, 0, 4) });
                var box = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 10) };
                Detail.Children.Add(box);
                return box;
            }

            var name = Field("Ad (parça numarası)", basis.Name + " (özel)");
            var category = Field("Kategori", "Kullanıcı");
            var description = Field("Açıklama", basis.Description);
            var manufacturer = Field("Üretici", string.Empty);
            Detail.Children.Add(new TextBlock { Text = "Parametreler", Style = (Style)FindResource("Text.Overline"), Margin = new Thickness(0, 8, 0, 8) });
            var boxes = new Dictionary<string, TextBox>();
            foreach (var p in def.Parameters.Where(p => p.Key != "pinmap"))
            {
                string label = p.Label + (string.IsNullOrEmpty(p.Unit) ? string.Empty : " (" + p.Unit + ")");
                boxes[p.Key] = Field(label, values.TryGetValue(p.Key, out var v) ? v : p.DefaultValue);
            }

            var status = new TextBlock { Style = (Style)FindResource("Text.Caption"), Foreground = (System.Windows.Media.Brush)FindResource("B.Danger"), TextWrapping = TextWrapping.Wrap };
            var save = new Button { Style = (Style)FindResource("Btn.Primary"), Content = "Kütüphaneye kaydet", Padding = new Thickness(18, 8, 18, 8) };
            var cancel = new Button { Content = "Vazgeç", Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("Btn.Ghost") };
            cancel.Click += (_, _) => ShowDetail(basis);
            save.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(name.Text))
                {
                    status.Text = "Bir ad girin.";
                    return;
                }

                var part = new CustomPart
                {
                    Id = UserLibrary.MakeId(Lib, name.Text),
                    Name = name.Text.Trim(),
                    BasePartId = def.Id,
                    Category = string.IsNullOrWhiteSpace(category.Text) ? "Kullanıcı" : category.Text.Trim(),
                    Description = description.Text.Trim(),
                    Manufacturer = manufacturer.Text.Trim(),
                    Author = Environment.UserName
                };
                foreach (var kv in basis.Parameters) part.Parameters[kv.Key] = kv.Value;
                foreach (var kv in boxes) part.Parameters[kv.Key] = kv.Value.Text.Trim();
                try
                {
                    // The model must still build with these values.
                    var test = new Schematic();
                    test.Parts.Add(new SchematicPart { RefDes = "X1", PartId = def.Id, Parameters = new Dictionary<string, string>(part.Parameters, StringComparer.OrdinalIgnoreCase) });
                    CircuitCompiler.Compile(test, Lib);
                    UserLibrary.Save(Lib, part);
                }
                catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
                {
                    status.Text = "Kaydedilemedi: " + ex.Message;
                    return;
                }

                Workspace.Current.Status = "'" + part.Name + "' kütüphaneye eklendi";
                Refresh(part.Id);
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
            row.Children.Add(save);
            row.Children.Add(cancel);
            Detail.Children.Add(row);
            Detail.Children.Add(status);
        }

        private void AssignModel(PartPreset preset)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter("3B model (*.stl, *.obj, *.ply)|*.stl;*.obj;*.ply|Tüm dosyalar (*.*)|*.*"), Title = RobotWin.Studio.Shell.Lang.T("Parça için 3B model seçin") };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            RobotTwin.CoreSim.Design.PartModel3D model;
            try
            {
                model = RobotWin.Studio.Models3D.CustomModels.Load(dlg.FileName);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is FormatException || ex is NotSupportedException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(Window.GetWindow(this), "Model okunamadı", System.IO.Path.GetFileName(dlg.FileName) + ": " + ex.Message, "Tamam");
                return;
            }

            SetModel(preset, model);
        }

        private void SetModel(PartPreset preset, RobotTwin.CoreSim.Design.PartModel3D? model)
        {
            try
            {
                UserLibrary.SetModel(Lib, preset, model);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.IO.InvalidDataException)
            {
                Dialog.Show(Window.GetWindow(this), "Kaydedilemedi", ex.Message, "Tamam");
                return;
            }

            Workspace.Current.Status = model != null ? $"'{preset.Name}' için 3B model atandı: {model.Name}" : $"'{preset.Name}' üretilen 3B modele döndü";
            ShowDetail(preset);
        }

        private void ExportPart(PartPreset preset)
        {
            var path = System.IO.Path.Combine(UserLibrary.Folder, preset.Id + RobotWinFormats.PartExtension);
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter("RobotWin parçası (*.rwpart)|*.rwpart"), FileName = preset.Name, DefaultExt = RobotWinFormats.PartExtension };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                if (System.IO.File.Exists(path)) System.IO.File.Copy(path, dlg.FileName, true);
                else CustomPart.FromPreset(preset, preset.Id, preset.Name).Save(dlg.FileName);
                Workspace.Current.Status = "Parça dışa aktarıldı: " + System.IO.Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(Window.GetWindow(this), "Dışa aktarılamadı", ex.Message, "Tamam");
            }
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter("RobotWin parça ve kütüphaneleri (*.rwpart, *.rwlib)|*.rwpart;*.rwlib"), Multiselect = true, Title = RobotWin.Studio.Shell.Lang.T("Parça içe aktar") };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            var added = new List<CustomPart>();
            foreach (var file in dlg.FileNames)
            {
                try
                {
                    added.AddRange(UserLibrary.Import(Lib, file));
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
                {
                    Dialog.Show(Window.GetWindow(this), "İçe aktarılamadı", System.IO.Path.GetFileName(file) + ": " + ex.Message, "Tamam");
                }
            }

            Workspace.Current.Status = added.Count == 0 ? "Yeni parça yok" : added.Count + " parça eklendi";
            Refresh(added.FirstOrDefault()?.Id);
        }

        private void OnImportKiCadFiles(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = RobotWin.Studio.Shell.Lang.Filter("KiCad kütüphaneleri (*.kicad_sym, *.kicad_mod)|*.kicad_sym;*.kicad_mod|Tüm dosyalar (*.*)|*.*"),
                Multiselect = true,
                Title = RobotWin.Studio.Shell.Lang.T("KiCad sembol / footprint içe aktar")
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) ImportKiCad(dlg.FileNames);
        }

        private void OnImportKiCadFolder(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = RobotWin.Studio.Shell.Lang.T("KiCad .pretty footprint klasörü seçin"), Multiselect = true };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) ImportKiCad(dlg.FolderNames);
        }

        /// <summary>Imports KiCad symbols/footprints as user parts and keeps them in the user library.</summary>
        private void ImportKiCad(IReadOnlyList<string> paths)
        {
            PartLibraryFile file;
            ImportReport report;
            try
            {
                file = KiCadLibraryImport.ImportLibraries(paths, Lib, out report);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                Dialog.Show(Window.GetWindow(this), "KiCad içe aktarılamadı", ex.Message, "Tamam");
                return;
            }

            int added = KiCadLibraryImport.Register(file, report, Lib);
            foreach (var p in file.Parts) UserLibrary.PartIds.Add(p.Id);
            try
            {
                System.IO.Directory.CreateDirectory(UserLibrary.Folder);
                string name = "kicad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + RobotWinFormats.LibraryExtension;
                file.Name = "KiCad içe aktarımı";
                if (file.Parts.Count > 0) file.Save(System.IO.Path.Combine(UserLibrary.Folder, name));
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                report.Warnings.Add("Kullanıcı kütüphanesine yazılamadı: " + ex.Message);
            }

            Workspace.Current.Status = $"KiCad: {report.Footprints.Count} footprint, {report.Symbols.Count} sembol, {added} yeni parça";
            ShowReport(report.ToString());
            Refresh(file.Parts.FirstOrDefault()?.Id);
        }

        private void ShowReport(string text)
        {
            var w = new Window
            {
                Title = "KiCad içe aktarma raporu",
                Width = 760,
                Height = 560,
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (System.Windows.Media.Brush)FindResource("B.Surface"),
                Content = new TextBox
                {
                    Text = text,
                    IsReadOnly = true,
                    FontFamily = (System.Windows.Media.FontFamily)FindResource("F.Mono"),
                    FontSize = 12,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(12)
                }
            };
            w.ShowDialog();
        }

        private void OnExportLibrary(object sender, RoutedEventArgs e)
        {
            if (UserLibrary.PartIds.Count == 0)
            {
                Dialog.Show(Window.GetWindow(this), "Kullanıcı parçası yok", "Önce bir parçayı seçip 'Bundan yeni parça' ile kendi parçanızı oluşturun.", "Tamam");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter("RobotWin kütüphanesi (*.rwlib)|*.rwlib"), FileName = "Kütüphanem", DefaultExt = RobotWinFormats.LibraryExtension };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                UserLibrary.Export(Lib, dlg.FileName, System.IO.Path.GetFileNameWithoutExtension(dlg.FileName));
                Workspace.Current.Status = "Kütüphane dışa aktarıldı: " + System.IO.Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(Window.GetWindow(this), "Dışa aktarılamadı", ex.Message, "Tamam");
            }
        }
    }
}
