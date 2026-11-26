using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [variants] The alternatives of the open design (<see cref="DesignVariant"/>): made, named, described, copied, removed,
    /// chosen, and compared on the same run. Their changes per part are made in the part's inspector while one is chosen.
    /// </summary>
    internal sealed class VariantsWindow : Window
    {
        private static Workspace W => Workspace.Current;

        private readonly Action<string?> _select;
        private readonly StackPanel _list = new StackPanel();
        private readonly StackPanel _compare = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        private CheckBox? _withCode;
        private readonly Button _compareButton;
        private CancellationTokenSource? _run;

        public VariantsWindow(Window owner, Action<string?> select)
        {
            _select = select;
            Owner = owner;
            Title = "Tasarım alternatifleri";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = R<FontFamily>("F.Ui");
            SetResourceReference(ForegroundProperty, "B.Text");

            var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20), Width = 680 };
            panel.Children.Add(new TextBlock { Text = "Tasarım alternatifleri", Style = R<Style>("Text.Title"), FontSize = 18 });
            panel.Children.Add(new TextBlock
            {
                Text = "Aynı devrenin sürümleri: bazı parçalar takılmaz, bazı değerler ya da parçalar başkadır. Seçili olan simüle edilir, BOM'a, üretim dosyalarına ve montaja girer. Bir alternatif seçiliyken parçanın değişikliğini denetçisinden yaparsınız.",
                Style = R<Style>("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14)
            });
            panel.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) });
            panel.Children.Add(_compare);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var add = new Button { Content = "Yeni alternatif", Style = R<Style>("Btn.Ghost"), MinWidth = 110 };
            add.Click += (_, _) => Edit(s =>
            {
                var v = new DesignVariant { Name = FreeName(s, "Alternatif") };
                s.Variants.Add(v);
                return v.Name;
            }, "Yeni alternatif eklendi", select: true);
            row.Children.Add(add);
            _withCode = new CheckBox
            {
                Content = "Kartların kodunu da çalıştır",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                ToolTip = "Uno / Nano'ların derlenmiş kodu ve ESP32'lerin çizimi her alternatifte çalışır (ESP32 çizimleri önce derlenir)"
            };
            row.Children.Add(_withCode);
            _compareButton = new Button { Content = "Karşılaştır", Style = R<Style>("Btn.Ghost"), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
            _compareButton.Click += async (_, _) => await Compare();
            row.Children.Add(_compareButton);
            var close = new Button { Content = "Kapat", MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsCancel = true, IsDefault = true };
            close.Click += (_, _) => Close();
            row.Children.Add(close);
            panel.Children.Add(row);

            var card = new Border
            {
                Margin = new Thickness(20),
                Background = R<Brush>("B.Overlay"),
                BorderBrush = R<Brush>("B.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Child = panel
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox) DragMove();
            };
            Content = card;
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");
            Closed += (_, _) => _run?.Cancel();
            Fill();
        }

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        private static string FreeName(Schematic s, string stem)
        {
            for (int i = 1; ; i++)
            {
                string name = stem + " " + i.ToString(CultureInfo.InvariantCulture);
                if (Variants.Find(s, name) == null) return name;
            }
        }

        /// <summary>A change to the alternatives, undoable like any edit; <paramref name="select"/>: the one it returns is chosen.</summary>
        private void Edit(Func<Schematic, string?> change, string status, bool select = false)
        {
            W.BeginChange();
            string? chosen = change(W.Schematic);
            W.CommitChange(status);
            if (select) _select(chosen);
            Fill();
        }

        private void Fill()
        {
            _list.Children.Clear();
            var s = W.Schematic;
            _list.Children.Add(Row(null, s));
            foreach (var v in s.Variants) _list.Children.Add(Row(v, s));
            _compareButton.IsEnabled = s.Variants.Count > 0;
        }

        private FrameworkElement Row(DesignVariant? v, Schematic s)
        {
            bool active = v == null ? string.IsNullOrEmpty(s.ActiveVariant) : string.Equals(s.ActiveVariant, v.Name, StringComparison.OrdinalIgnoreCase);
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bar = new Border { CornerRadius = new CornerRadius(2) };
            bar.SetResourceReference(Border.BackgroundProperty, active ? "B.Accent" : "B.Border");
            grid.Children.Add(bar);

            var body = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(body, 1);
            if (v == null)
            {
                body.Children.Add(new TextBlock { Text = "Çizildiği gibi", Style = R<Style>("Text.Subtitle") });
                body.Children.Add(new TextBlock { Text = "Şemadaki her parça, çizildiği değerle.", Style = R<Style>("Text.Caption") });
            }
            else
            {
                var name = new TextBox { Text = v.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
                Commit(name, value =>
                {
                    value = value.Trim();
                    if (value.Length == 0 || value == v.Name) return;
                    if (Variants.Find(s, value) != null)
                    {
                        name.Text = v.Name;
                        return;
                    }

                    string old = v.Name;
                    Edit(sch =>
                    {
                        var target = Variants.Find(sch, old)!;
                        target.Name = value;
                        if (string.Equals(sch.ActiveVariant, old, StringComparison.OrdinalIgnoreCase)) sch.ActiveVariant = value;
                        return null;
                    }, $"Alternatif yeniden adlandırıldı: {value}");
                });
                body.Children.Add(name);
                var note = new TextBox { Text = v.Note ?? string.Empty, Tag = "Ne için… (isteğe bağlı)", Margin = new Thickness(0, 0, 0, 4) };
                Commit(note, value =>
                {
                    value = value.Trim();
                    if (value == (v.Note ?? string.Empty)) return;
                    string target = v.Name;
                    Edit(sch =>
                    {
                        Variants.Find(sch, target)!.Note = value.Length == 0 ? null : value;
                        return null;
                    }, "Alternatifin notu değişti");
                });
                body.Children.Add(note);
                body.Children.Add(new TextBlock { Text = Summary(v, s), Style = R<Style>("Text.Caption"), TextWrapping = TextWrapping.Wrap });
            }

            grid.Children.Add(body);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(buttons, 2);
            if (active)
            {
                var chip = new TextBlock { Text = "Seçili", Style = R<Style>("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                chip.SetResourceReference(TextBlock.ForegroundProperty, "B.Accent");
                buttons.Children.Add(chip);
            }
            else
            {
                var pick = new Button { Content = "Seç", Style = R<Style>("Btn.Primary"), MinWidth = 60 };
                string? target = v?.Name;
                pick.Click += (_, _) =>
                {
                    _select(target);
                    Fill();
                };
                buttons.Children.Add(pick);
            }

            if (v != null)
            {
                string source = v.Name;
                var copy = new Button { Content = "Çoğalt", Style = R<Style>("Btn.Ghost"), Margin = new Thickness(6, 0, 0, 0) };
                copy.Click += (_, _) => Edit(sch =>
                {
                    var from = Variants.Find(sch, source)!;
                    var twin = new DesignVariant { Name = FreeName(sch, source), Note = from.Note };
                    foreach (var (uid, o) in from.Parts)
                        twin.Parts[uid] = new PartOverride { NotFitted = o.NotFitted, PartId = o.PartId, PresetId = o.PresetId, Parameters = new Dictionary<string, string>(o.Parameters, StringComparer.OrdinalIgnoreCase) };
                    sch.Variants.Add(twin);
                    return null;
                }, $"Alternatif çoğaltıldı: {source}");
                buttons.Children.Add(copy);
                var remove = new Button { Content = "Sil", Style = R<Style>("Btn.Ghost"), Margin = new Thickness(6, 0, 0, 0) };
                remove.Click += (_, _) =>
                {
                    bool wasActive = active;
                    Edit(sch =>
                    {
                        sch.Variants.RemoveAll(x => string.Equals(x.Name, source, StringComparison.OrdinalIgnoreCase));
                        return null;
                    }, $"Alternatif silindi: {source} (geri alınabilir)");
                    if (wasActive) _select(null);
                    Fill();
                };
                buttons.Children.Add(remove);
            }

            grid.Children.Add(buttons);
            return grid;
        }

        /// <summary>What an alternative changes, in a line ("takılmaz: D1 · değer: R1").</summary>
        private static string Summary(DesignVariant v, Schematic s)
        {
            var live = s.Parts.ToDictionary(p => p.Uid, p => p.RefDes, StringComparer.Ordinal);
            var changes = v.Parts.Where(kv => live.ContainsKey(kv.Key) && !kv.Value.IsEmpty).ToList();
            if (changes.Count == 0) return "Değişiklik yok: bir parçayı seçip denetçisinden değiştirin.";
            var off = changes.Where(kv => kv.Value.NotFitted).Select(kv => live[kv.Key]).OrderBy(r => r, StringComparer.Ordinal).ToList();
            var values = changes.Where(kv => !kv.Value.NotFitted && kv.Value.Parameters.Count > 0).Select(kv => live[kv.Key]).OrderBy(r => r, StringComparer.Ordinal).ToList();
            var swapped = changes.Where(kv => !kv.Value.NotFitted && (kv.Value.PartId != null || kv.Value.PresetId != null)).Select(kv => live[kv.Key]).OrderBy(r => r, StringComparer.Ordinal).ToList();
            var parts = new List<string>();
            if (off.Count > 0) parts.Add("takılmaz: " + string.Join(", ", off));
            if (values.Count > 0) parts.Add("değer: " + string.Join(", ", values));
            if (swapped.Count > 0) parts.Add("başka parça: " + string.Join(", ", swapped));
            return string.Join(" · ", parts);
        }

        private static void Commit(TextBox box, Action<string> apply)
        {
            box.LostKeyboardFocus += (_, _) => apply(box.Text);
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                apply(box.Text);
                e.Handled = true;
            };
        }

        /// <summary>--snapshot: the comparison run at once.</summary>
        internal async void CompareForSnapshot() => await Compare();

        /// <summary>Every alternative and the design as drawn, the same run each (the circuit only), side by side.</summary>
        private async Task Compare()
        {
            _run?.Cancel();
            _run = new CancellationTokenSource();
            var token = _run.Token;
            var copy = Schematic.FromJson(W.Schematic.ToJson());
            var lib = W.Library;
            bool code = _withCode?.IsChecked == true;
            var package = code ? W.Project : null;   // [variants-code] the boards' code: compiled HEX, ESP32 sketches built on the way
            var options = code ? new RobotTwin.CoreSim.Automation.RunnerOptions { Library = lib, Esp32Builder = RobotWin.Studio.Automation.StudioApiHost.BuildEsp32 } : null;
            double ambient = AppSettings.Current.AmbientTempC;
            _compare.Children.Clear();
            _compare.Children.Add(new TextBlock { Text = "Her alternatif 1 s simüle ediliyor…", Style = R<Style>("Text.Caption") });
            _compareButton.IsEnabled = false;
            try
            {
                var results = await Task.Run(() => VariantComparison.Run(copy, lib, 1.0, null, ambient, token, options, package), token);
                ShowResults(results, code);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _compareButton.IsEnabled = W.Schematic.Variants.Count > 0;
            }
        }

        private void ShowResults(List<VariantResult> results, bool code = false)
        {
            _compare.Children.Clear();
            _compare.Children.Add(new TextBlock
            {
                Text = code ? "Karşılaştırma (aynı 1 s, kartların kodu da çalıştı)" : "Karşılaştırma (aynı 1 s, yalnız devre: kartların kodu burada çalışmaz)",
                Style = R<Style>("Text.Subtitle"),
                Margin = new Thickness(0, 0, 0, 6)
            });
            var grid = new Grid();
            string[] heads = code
                ? new[] { "Alternatif", "Parça sayısı", "Ortalama güç", "Tepe güç", "Çekilen yük", "En sıcak parça", "Kart çıktısı", "Arıza" }
                : new[] { "Alternatif", "Parça sayısı", "Ortalama güç", "Tepe güç", "Çekilen yük", "En sıcak parça", "Arıza" };
            for (int c = 0; c < heads.Length; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == heads.Length - 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });   // the failures wrap in what is left
            grid.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < heads.Length; c++) Cell(grid, 0, c, heads[c], true);
            double baseline = results.Count > 0 ? results[0].SupplyW : double.NaN;
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                grid.RowDefinitions.Add(new RowDefinition());
                int row = i + 1;
                Cell(grid, row, 0, r.Name.Length == 0 ? "Çizildiği gibi" : r.Name, false);
                if (r.Error != null)
                {
                    var err = Cell(grid, row, 1, r.Error, false);
                    Grid.SetColumnSpan(err, heads.Length - 1);
                    continue;
                }

                Cell(grid, row, 1, r.Parts.ToString(CultureInfo.InvariantCulture), false);
                string power = SiValue.Format(r.SupplyW, "W");
                if (i > 0 && baseline > 1e-9) power += " (" + Lang.Percent((r.SupplyW / baseline - 1) * 100, "+0;−0;0") + ")";
                Cell(grid, row, 2, power, false);
                Cell(grid, row, 3, SiValue.Format(r.PeakSupplyW, "W"), false);
                Cell(grid, row, 4, r.ChargeMah.ToString("0.###", CultureInfo.InvariantCulture) + " mAh", false);   // as the SI values beside it
                Cell(grid, row, 5, r.Hottest == null ? "—" : $"{r.Hottest} {r.HottestC.ToString("0.0", CultureInfo.InvariantCulture)} °C", false);
                if (code)
                {
                    // what each board printed last, so the alternatives' behaviour can be told apart too
                    var said = r.Serial.Where(kv => kv.Value.Length > 0).Select(kv => kv.Key + ": " + kv.Value.Split('\n').Last()).ToList();
                    Cell(grid, row, 6, said.Count == 0 ? "—" : string.Join("\n", said), false);
                }

                var trouble = r.Failures.Concat(r.Notes).ToList();
                Cell(grid, row, heads.Length - 1, trouble.Count == 0 ? "—" : string.Join("\n", trouble), false);
            }

            _compare.Children.Add(grid);
        }

        private static TextBlock Cell(Grid grid, int row, int column, string text, bool head)
        {
            var t = new TextBlock { Text = text, Margin = new Thickness(0, 2, 16, 2), TextWrapping = TextWrapping.Wrap, MaxWidth = 220 };
            t.Style = R<Style>(head ? "Text.Caption" : "Text.Body");
            Grid.SetRow(t, row);
            Grid.SetColumn(t, column);
            grid.Children.Add(t);
            return t;
        }
    }
}
