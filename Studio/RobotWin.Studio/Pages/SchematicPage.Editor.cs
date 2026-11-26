using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Editor;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// The KiCad-style editor UI around the schematic canvas: part/power pickers, in-place value
    /// editing, multi-part properties, find &amp; replace, ERC list, annotation, footprint chooser,
    /// shortcut help and exports (BOM, netlists, PNG/SVG, print). Overlays live in a layer above
    /// the canvas so they need no popup windows.
    /// </summary>
    public partial class SchematicPage
    {
        private System.Windows.Controls.Canvas _overlay = null!;
        private FrameworkElement? _picker, _inlineEditor, _findPanel, _ercPanel, _helpPanel, _snapshotMenu;
        private List<ErcViolation> _erc = new List<ErcViolation>();
        private bool _ercShowInfo;
        private bool _ercShowExcluded;   // [erc-policy]

        private T Res<T>(string key) where T : class => (T)FindResource(key);

        /// <summary>Called from the constructor.</summary>
        private void InitEditor()
        {
            _overlay = new System.Windows.Controls.Canvas { ClipToBounds = true };
            Panel.SetZIndex(_overlay, 4);
            if (Canvas.Parent is Grid host) host.Children.Add(_overlay);
            Canvas.CommandRequested += (_, e) => RunCommand(e.Command, e.Target);
            W.DocumentReplaced += (_, _) => CloseOverlays();
            W.DocumentChanged += (_, _) =>
            {
                if (_ercPanel != null) RunErc(keepOpen: true);
            };
        }

        private void CloseOverlays()
        {
            foreach (var el in new[] { _picker, _inlineEditor, _findPanel, _ercPanel, _helpPanel, _snapshotMenu })
            {
                if (el != null) _overlay.Children.Remove(el);
            }

            _picker = _inlineEditor = _findPanel = _ercPanel = _helpPanel = _snapshotMenu = null;
            Canvas.ErcMarkers = null;
        }

        private void RemoveOverlay(ref FrameworkElement? el, bool focusCanvas = true)
        {
            if (el != null) _overlay.Children.Remove(el);
            el = null;
            if (focusCanvas) Canvas.Focus();
        }

        private Border Card(double width, UIElement child, double padding = 12) => new Border
        {
            Width = width,
            Background = Res<Brush>("B.Overlay"),
            BorderBrush = Res<Brush>("B.BorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(padding),
            Child = child
        };

        private void Place(FrameworkElement el, double x, double y)
        {
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = el.DesiredSize.Width, h = el.DesiredSize.Height;
            double maxX = Math.Max(0, _overlay.ActualWidth - w - 8), maxY = Math.Max(0, _overlay.ActualHeight - h - 8);
            System.Windows.Controls.Canvas.SetLeft(el, Math.Max(8, Math.Min(maxX, x)));
            System.Windows.Controls.Canvas.SetTop(el, Math.Max(8, Math.Min(maxY, y)));
        }

        private TextBlock Caption(string text) => new TextBlock { Text = text, Style = Res<Style>("Text.Caption"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };

        // ================================================================ commands

        private void RunCommand(SchematicCommand command, object? target)
        {
            var part = target as SchematicPart ?? Canvas.Selection.OfType<SchematicPart>().FirstOrDefault();
            switch (command)
            {
                case SchematicCommand.AddPart: ShowPartPicker(false); break;
                case SchematicCommand.AddPower: ShowPartPicker(true); break;
                case SchematicCommand.EditValue:
                    if (target is NetLabel label) EditLabelInline(label);
                    else if (part != null) EditValueInline(part);
                    break;
                case SchematicCommand.EditProperties: ShowPropertiesDialog(target as SchematicPart); break;
                case SchematicCommand.ChooseFootprint: if (part != null) ChooseFootprint(part); break;
                case SchematicCommand.Datasheet: if (part != null) OpenDatasheet(part); break;
                case SchematicCommand.ShowInPcb: if (part != null) (Window.GetWindow(this) as MainWindow)?.ShowPartInPcb(part.RefDes); break;
                case SchematicCommand.Find: ShowFind(false); break;
                case SchematicCommand.EditNote: EditNote(target); break;   // [notes]
                case SchematicCommand.Replace: ShowFind(true); break;
                case SchematicCommand.Help: ToggleHelp(); break;
                case SchematicCommand.Erc: RunErc(keepOpen: false); break;
                case SchematicCommand.Annotate: ShowAnnotateDialog(); break;
                case SchematicCommand.ExportBom: ExportBom(); break;
                case SchematicCommand.ExportNetlist: ExportText("KiCad netlist (*.net)|*.net", ".net", () => KiCadExporter.ExportNetlist(W.Schematic, W.Library, W.Schematic.Title), "KiCad netlist"); break;
                case SchematicCommand.ExportSpice: ExportText("SPICE netlist (*.cir)|*.cir", ".cir", () => SpiceExporter.ExportSpice(W.Schematic, W.Library), "SPICE netlist"); break;
                case SchematicCommand.ExportImage: ExportImage(); break;
                case SchematicCommand.Print: PrintSheet(); break;
            }
        }

        /// <summary>Schematic commands offered in the command palette (Ctrl+K).</summary>
        public static readonly IReadOnlyList<(string Title, SchematicCommand Command, string? Keys)> PaletteEntries = new List<(string, SchematicCommand, string?)>
        {
            ("Parça ekle…", SchematicCommand.AddPart, "A"),
            ("Güç sembolü ekle…", SchematicCommand.AddPower, "P"),
            ("Bul / değiştir", SchematicCommand.Replace, "Ctrl+H"),
            ("Numaralandır…", SchematicCommand.Annotate, null),
            ("Elektriksel kural denetimi (ERC)", SchematicCommand.Erc, null),
            ("Malzeme listesi (BOM) dışa aktar…", SchematicCommand.ExportBom, null),
            ("KiCad netlist dışa aktar…", SchematicCommand.ExportNetlist, null),
            ("SPICE netlist dışa aktar…", SchematicCommand.ExportSpice, null),
            ("Şemayı PNG / SVG olarak kaydet…", SchematicCommand.ExportImage, null),
            ("Şemayı yazdır / PDF…", SchematicCommand.Print, "Ctrl+P"),
            ("Şema kısayolları", SchematicCommand.Help, "F1"),
        };

        public void RunEditorCommand(SchematicCommand command) => Dispatcher.BeginInvoke(new Action(() => RunCommand(command, null)), System.Windows.Threading.DispatcherPriority.Loaded);

        // ================================================================ toolbar

        private void OnPowerTool(object sender, RoutedEventArgs e) => ShowPartPicker(true);
        private void OnAddPartTool(object sender, RoutedEventArgs e) => ShowPartPicker(false);
        private void OnNoConnectTool(object sender, RoutedEventArgs e) { Canvas.Tool = EditorTool.NoConnect; Canvas.Focus(); }
        private void OnAnnotate(object sender, RoutedEventArgs e) => ShowAnnotateDialog();
        private void OnErc(object sender, RoutedEventArgs e) => RunErc(keepOpen: false);
        private void OnFind(object sender, RoutedEventArgs e) => ShowFind(false);
        private void OnHelp(object sender, RoutedEventArgs e) => ToggleHelp();

        private void OnExportMenu(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
            void Item(string header, string gesture, SchematicCommand cmd)
            {
                var mi = new MenuItem { Header = header, InputGestureText = gesture };
                mi.Click += (_, _) => RunCommand(cmd, null);
                menu.Items.Add(mi);
            }

            Item("Malzeme listesi (BOM, .csv)…", "", SchematicCommand.ExportBom);
            Item("KiCad netlist (.net)…", "", SchematicCommand.ExportNetlist);
            Item("SPICE netlist (.cir)…", "", SchematicCommand.ExportSpice);
            menu.Items.Add(new Separator());
            Item("Görüntü (PNG / SVG)…", "", SchematicCommand.ExportImage);
            Item("Yazdır / PDF…", "Ctrl+P", SchematicCommand.Print);
            menu.IsOpen = true;
        }

        // ================================================================ part picker (A / P)

        private static bool IsPowerPreset(PartPreset p, PartLibrary lib) =>
            lib.Find(p.PartId)?.Symbol is SymbolKind.Ground or SymbolKind.PowerRail;

        public void ShowPartPicker(bool power)
        {
            if (W.Simulation.IsActive)
            {
                ShowToast("Parça eklemek için önce simülasyonu durdurun.");
                return;
            }

            RemoveOverlay(ref _picker, false);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = power ? "Güç sembolü ekle" : "Parça ekle", Style = Res<Style>("Text.Overline"), Margin = new Thickness(0, 0, 0, 8) });
            var box = new TextBox { Tag = power ? "GND, 5V, 3V3…" : "Ara: direnç 10k, LED, LM358, arduino…" };
            stack.Children.Add(box);
            var list = new ListBox { Height = power ? 200 : 300, Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Add(list);
            stack.Children.Add(new TextBlock { Text = "↑↓ seç · Enter yerleştir · Esc kapat", Style = Res<Style>("Text.Caption"), Margin = new Thickness(0, 6, 0, 0) });
            var card = Card(360, stack);
            void Filter()
            {
                var tokens = RobotWin.Studio.Shell.Lang.Fold(box.Text.Trim()).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var items = W.Library.Presets.Where(p => !power || IsPowerPreset(p, W.Library)).Where(p =>
                {
                    if (tokens.Length == 0) return true;
                    string hay = RobotWin.Studio.Shell.Lang.Searchable(p.Name, p.Description, p.Category, p.Id);
                    return tokens.All(t => hay.Contains(t));
                }).Take(200).ToList();
                list.ItemsSource = items.Select(p => new ListBoxItem
                {
                    Content = new TextBlock
                    {
                        Inlines =
                        {
                            new System.Windows.Documents.Run(p.Name) { Foreground = Res<Brush>("B.Text") },
                            new System.Windows.Documents.Run("   " + p.Category) { Foreground = Res<Brush>("B.TextMuted"), FontSize = 12 }
                        }
                    },
                    Tag = p,
                    ToolTip = p.Description
                }).ToList();
                list.SelectedIndex = items.Count > 0 ? 0 : -1;
            }

            void Accept()
            {
                if ((list.SelectedItem as ListBoxItem)?.Tag is not PartPreset preset) return;
                RemoveOverlay(ref _picker);
                Canvas.BeginPlace(preset.Id);
                W.Status = $"{preset.Name}: tıklayarak yerleştirin (R döndür, X/Y aynala, Esc bitir)";
            }

            box.TextChanged += (_, _) => Filter();
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    int n = list.Items.Count;
                    if (n > 0) list.SelectedIndex = Math.Max(0, Math.Min(n - 1, list.SelectedIndex + (e.Key == Key.Down ? 1 : -1)));
                    list.ScrollIntoView(list.SelectedItem);
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    Accept();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    RemoveOverlay(ref _picker);
                    e.Handled = true;
                }
            };
            list.MouseDoubleClick += (_, _) => Accept();
            list.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
                if (e.Key == Key.Escape) { RemoveOverlay(ref _picker); e.Handled = true; }
            };
            Filter();
            _picker = card;
            _overlay.Children.Add(card);
            var at = Canvas.IsMouseOver ? Mouse.GetPosition(_overlay) : new Point(_overlay.ActualWidth / 2 - 180, 60);
            Place(card, at.X + 12, at.Y + 12);
            Dispatcher.BeginInvoke(new Action(() => box.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        // ================================================================ in-place editing (V / double-click)

        private void ShowInlineEditor(Rect anchor, string text, string hint, Action<string> commit)
        {
            RemoveOverlay(ref _inlineEditor, false);
            var box = new TextBox { Text = text, MinWidth = Math.Max(120, anchor.Width + 40), FontFamily = Res<FontFamily>("F.Mono") };
            var stack = new StackPanel();
            stack.Children.Add(box);
            stack.Children.Add(new TextBlock { Text = hint, Style = Res<Style>("Text.Caption"), Margin = new Thickness(2, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(160, box.MinWidth) });
            var card = Card(double.NaN, stack, 6);
            bool done = false;
            void Finish(bool apply)
            {
                if (done) return;
                done = true;
                RemoveOverlay(ref _inlineEditor);
                if (apply && box.Text != text) commit(box.Text.Trim());
            }

            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Finish(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
            };
            box.LostKeyboardFocus += (_, _) => Finish(true);
            _inlineEditor = card;
            _overlay.Children.Add(card);
            Place(card, anchor.Left - 7, anchor.Top - 7);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                box.Focus();
                box.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        public void EditValueInline(SchematicPart part)
        {
            if (W.Simulation.IsActive) return;
            var def = W.Library.Find(part.PartId);
            if (def == null) return;
            string key = def.Symbol == SymbolKind.PowerRail ? "net" : def.ValueKey;
            if (string.IsNullOrEmpty(key))
            {
                ShowPropertiesDialog(part);
                return;
            }

            var parameters = W.Schematic.ResolveParameters(W.Library, part);
            string current = parameters.TryGetValue(key, out var v) ? v : string.Empty;
            var pdef = def.Parameters.FirstOrDefault(p => p.Key == key);
            var anchor = Canvas.ValueTextRect(part) ?? Canvas.ScreenBounds(part);
            var origin = Canvas.TranslatePoint(new Point(0, 0), _overlay);
            anchor.Offset(origin.X, origin.Y);
            string hint = $"{part.RefDes} · {pdef?.Label ?? key}" + (string.IsNullOrEmpty(pdef?.Unit) ? string.Empty : $" ({pdef!.Unit})") +
                          (pdef?.Choices != null ? " · " + string.Join("/", pdef.Choices) : string.Empty) + " · Enter / Esc";
            ShowInlineEditor(anchor, current, hint, value =>
            {
                if (value.Length == 0) return;
                SetParameter(part, key, value);
            });
        }

        public void EditLabelInline(NetLabel label)
        {
            if (W.Simulation.IsActive) return;
            var anchor = Canvas.LabelScreenRect(label);
            var origin = Canvas.TranslatePoint(new Point(0, 0), _overlay);
            anchor.Offset(origin.X, origin.Y);
            ShowInlineEditor(anchor, label.Name, (label.Global ? "Global etiket" : "Hat etiketi") + " · aynı ad = aynı hat · Enter / Esc", value =>
            {
                if (value.Length == 0 || value == label.Name) return;
                W.BeginChange();
                label.Name = value;
                W.CommitChange($"Etiket: {value}");
            });
        }

        // ================================================================ properties dialog (E), multi-part

        /// <summary>Themed modal card; returns true when OK was pressed.</summary>
        private bool ShowCardDialog(string title, UIElement body, string ok = "Uygula", double width = 460)
        {
            bool result = false;
            var owner = Window.GetWindow(this);
            var window = new Window
            {
                Owner = owner,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 18), Width = width };
            panel.Children.Add(new TextBlock { Text = title, Style = Res<Style>("Text.Title"), FontSize = 17, Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(body);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var okButton = new Button { Content = ok, MinWidth = 96, Style = Res<Style>("Btn.Primary"), IsDefault = true };
            var cancel = new Button { Content = "Vazgeç", MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            okButton.Click += (_, _) =>
            {
                result = true;
                window.Close();
            };
            cancel.Click += (_, _) => window.Close();
            row.Children.Add(okButton);
            row.Children.Add(cancel);
            panel.Children.Add(row);
            var card = new Border
            {
                Margin = new Thickness(20),
                Background = Res<Brush>("B.Overlay"),
                BorderBrush = Res<Brush>("B.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = panel
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is Border) window.DragMove();
            };
            window.Content = card;
            window.ShowDialog();
            Canvas.Focus();
            return result;
        }

        private Grid FieldRow(string label, FrameworkElement editor)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, Style = Res<Style>("Text.Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            return row;
        }

        public void ShowPropertiesDialog(SchematicPart? target)
        {
            if (W.Simulation.IsActive)
            {
                ShowToast("Özellikleri değiştirmek için simülasyonu durdurun.");
                return;
            }

            var parts = Canvas.Selection.OfType<SchematicPart>().ToList();
            if (target != null && !parts.Contains(target)) parts = new List<SchematicPart> { target };
            if (parts.Count == 0) return;
            var defs = parts.Select(p => W.Library.Find(p.PartId)).ToList();
            if (defs.Any(d => d == null)) return;
            var resolved = parts.Select(p => W.Schematic.ResolveParameters(W.Library, p)).ToList();

            // Parameters every selected part has (same definition → all of its parameters).
            var common = defs[0]!.Parameters.Where(pd => pd.Key != "pinmap" && pd.Key != "pinout" && defs.All(d => d!.Parameters.Any(x => x.Key == pd.Key))).ToList();
            var body = new StackPanel();
            string title = parts.Count == 1 ? $"{parts[0].RefDes} · {(parts[0].PresetId != null ? W.Library.FindPreset(parts[0].PresetId!)?.Name : null) ?? defs[0]!.Name}"
                                            : $"{parts.Count} parça: " + string.Join(", ", parts.Select(p => p.RefDes).OrderBy(r => r, Annotator.NaturalComparer).Take(8)) + (parts.Count > 8 ? "…" : string.Empty);
            if (parts.Count > 1) body.Children.Add(Caption("Boş bırakılan alanlar (farklı değerler) değiştirilmez; doldurduğunuz alan tüm seçili parçalara uygulanır."));

            TextBox? refBox = null;
            if (parts.Count == 1)
            {
                refBox = new TextBox { Text = parts[0].RefDes };
                body.Children.Add(FieldRow("Tanımlayıcı", refBox));
            }

            var editors = new List<(ParameterDefinition Def, Func<string?> Read)>();
            foreach (var pd in common)
            {
                var values = resolved.Select(r => r.TryGetValue(pd.Key, out var v) ? v : pd.DefaultValue).Distinct(StringComparer.Ordinal).ToList();
                string? same = values.Count == 1 ? values[0] : null;
                string label = pd.Label + (string.IsNullOrEmpty(pd.Unit) ? string.Empty : $" ({pd.Unit})");
                if (pd.Choices != null)
                {
                    var combo = new ComboBox();
                    foreach (var c in pd.Choices) combo.Items.Add(c);
                    combo.SelectedItem = same == null ? null : pd.Choices.FirstOrDefault(c => string.Equals(c, same, StringComparison.OrdinalIgnoreCase));
                    body.Children.Add(FieldRow(label, combo));
                    editors.Add((pd, () => combo.SelectedItem as string is { } s && s != same ? s : null));
                }
                else
                {
                    var box = new TextBox { Text = same ?? string.Empty, Tag = same == null ? "‹farklı değerler›" : null };
                    body.Children.Add(FieldRow(label, box));
                    editors.Add((pd, () => box.Text != (same ?? string.Empty) && !(same == null && box.Text.Length == 0) ? box.Text.Trim() : null));
                }
            }

            string fpNow = defs[0]!.ResolveFootprint(resolved[0]);
            bool fpSame = parts.Select((p, i) => defs[i]!.ResolveFootprint(resolved[i])).Distinct().Count() == 1;
            var fpBox = new TextBox { Text = fpSame ? fpNow : string.Empty, Tag = fpSame ? null : "‹farklı›" };
            body.Children.Add(FieldRow("PCB kılıfı (footprint)", fpBox));

            if (!ShowCardDialog(title, body)) return;

            var changes = new List<(SchematicPart Part, string Key, string Value)>();
            foreach (var (pd, read) in editors)
            {
                var v = read();
                if (v == null) continue;
                foreach (var p in parts) changes.Add((p, pd.Key, v));
            }

            string fp = fpBox.Text.Trim();
            if (fp.Length > 0 && (!fpSame || fp != fpNow)) foreach (var p in parts) changes.Add((p, "footprint", fp));
            string? newRef = refBox?.Text.Trim();
            bool rename = refBox != null && newRef!.Length > 0 && newRef != parts[0].RefDes;
            if (rename && W.Schematic.FindPart(newRef!) is { } clash && !ReferenceEquals(clash, parts[0]))
            {
                ShowToast($"'{newRef}' zaten kullanılıyor.");
                rename = false;
            }

            if (changes.Count == 0 && !rename) return;
            W.BeginChange();
            foreach (var (p, key, value) in changes) p.Parameters[key] = value;
            if (rename)
            {
                var change = new RefDesChange(parts[0], parts[0].RefDes, newRef!);
                parts[0].RefDes = newRef!;
                Annotator.ApplyToLinkedData(W.Schematic, new[] { change });
                foreach (var f in W.Project.Firmware.Where(f => string.Equals(f.BoardRefDes, change.OldRefDes, StringComparison.OrdinalIgnoreCase))) f.BoardRefDes = change.NewRefDes;
            }

            W.CommitChange(parts.Count == 1 ? $"{parts[0].RefDes} güncellendi" : $"{parts.Count} parça güncellendi");
        }

        /// <summary>Inspector section for a multi-selection (called from BuildInspector).</summary>
        private void BuildMultiSelectionInspector(IReadOnlyCollection<object> selection)
        {
            int parts = selection.OfType<SchematicPart>().Count(), wires = selection.OfType<SchematicWire>().Count(), labels = selection.OfType<NetLabel>().Count();
            var summary = new List<string>();
            if (parts > 0) summary.Add($"{parts} parça");
            if (wires > 0) summary.Add($"{wires} kablo");
            if (labels > 0) summary.Add($"{labels} etiket");
            Inspector.Children.Add(new TextBlock { Text = string.Join(" · ", summary), Style = Res<Style>("Text.Body"), Margin = new Thickness(0, 4, 0, 10) });
            if (parts > 0)
            {
                var edit = new Button { Content = parts > 1 ? $"{parts} parçayı birlikte düzenle  (E)" : "Parça özellikleri  (E)", Margin = new Thickness(0, 0, 0, 6), IsEnabled = !W.Simulation.IsActive };
                edit.Click += (_, _) => ShowPropertiesDialog(null);
                Inspector.Children.Add(edit);
            }

            var copy = new Button { Content = "Kopyala  (Ctrl+C)", Margin = new Thickness(0, 0, 0, 6) };
            copy.Click += (_, _) => Canvas.CopySelection();
            Inspector.Children.Add(copy);
            Inspector.Children.Add(Hint("M taşı · G sürükle · R/Shift+R döndür · X/Y aynala · Ctrl+D çoğalt · oklar kaydır · Del sil. Tüm kısayollar: F1."));
        }

        // ================================================================ footprint chooser

        private void ChooseFootprint(SchematicPart part)
        {
            if (W.Simulation.IsActive) return;
            var def = W.Library.Find(part.PartId);
            if (def == null) return;
            var parameters = W.Schematic.ResolveParameters(W.Library, part);
            string current = def.ResolveFootprint(parameters);
            int pads = def.Pins.Select(p => def.ResolvePad(p, parameters)).Where(p => p.Length > 0).Distinct().Count();
            var ids = FootprintLibrary.KnownIds.Concat(FootprintLibrary.UserIds).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => (Id: id, Fp: FootprintLibrary.Get(id))).Where(x => x.Fp != null).ToList();
            var body = new StackPanel();
            body.Children.Add(Caption($"{part.RefDes}: {pads} pin/pad gerekiyor. Uyumlu kılıflar üstte (✓)."));
            var filter = new TextBox { Tag = "Kılıf ara (DIP, TO92, 0805…)" };
            body.Children.Add(filter);
            var list = new ListBox { Height = 280, Margin = new Thickness(0, 8, 0, 0) };
            body.Children.Add(list);
            void Fill()
            {
                string q = filter.Text.Trim();
                list.ItemsSource = ids
                    .Where(x => q.Length == 0 || x.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || x.Fp!.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(x => x.Fp!.Pads.Select(p => p.Name).Distinct().Count() == pads)
                    .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(x =>
                    {
                        int n = x.Fp!.Pads.Select(p => p.Name).Distinct().Count();
                        return new ListBoxItem
                        {
                            Content = (n == pads ? "✓  " : "     ") + x.Id + $"   · {n} pad" + (x.Fp.Description.Length > 0 ? " · " + x.Fp.Description : string.Empty),
                            Tag = x.Id,
                            IsSelected = string.Equals(x.Id, current, StringComparison.OrdinalIgnoreCase)
                        };
                    }).ToList();
            }

            filter.TextChanged += (_, _) => Fill();
            Fill();
            if (!ShowCardDialog("Footprint seç", body, "Seç", 520)) return;
            if ((list.SelectedItem as ListBoxItem)?.Tag is string id && !string.Equals(id, current, StringComparison.OrdinalIgnoreCase))
            {
                SetParameter(part, "footprint", id);
            }
        }

        // ================================================================ datasheet

        private void OpenDatasheet(SchematicPart part)
        {
            var def = W.Library.Find(part.PartId);
            if (def == null) return;
            var parameters = W.Schematic.ResolveParameters(W.Library, part);
            string target;
            if (parameters.TryGetValue("datasheet", out var ds) && !string.IsNullOrWhiteSpace(ds))
            {
                target = ds.Trim();
            }
            else
            {
                string query = parameters.TryGetValue("model", out var model) && model.Trim().Length > 0 ? model.Trim()
                    : part.PresetId != null ? W.Library.FindPreset(part.PresetId)?.Name ?? def.Name : def.Name;
                target = "https://www.alldatasheet.com/view.jsp?Searchword=" + Uri.EscapeDataString(query);
            }

            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                W.Status = $"{part.RefDes}: datasheet açılıyor";
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                ShowToast("Datasheet açılamadı: " + ex.Message);
            }
        }

        // ================================================================ find & replace (Ctrl+F / Ctrl+H)

        public void ShowFind(bool replace)
        {
            RemoveOverlay(ref _findPanel, false);
            var stack = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var close = new Button { Content = "✕", Style = Res<Style>("Btn.Ghost"), Padding = new Thickness(6, 0, 6, 0) };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(new TextBlock { Text = replace ? "Bul ve değiştir" : "Bul", Style = Res<Style>("Text.Overline"), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(head);
            var find = new TextBox { Tag = "Tanımlayıcı, değer, hat… (* ? joker)" };
            stack.Children.Add(find);
            var repl = new TextBox { Tag = "Yeni metin", Margin = new Thickness(0, 6, 0, 0), Visibility = replace ? Visibility.Visible : Visibility.Collapsed };
            stack.Children.Add(repl);
            var opts = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            CheckBox Opt(string text, bool on)
            {
                var cb = new CheckBox { Content = text, IsChecked = on, Margin = new Thickness(0, 0, 12, 4), Foreground = Res<Brush>("B.TextSecondary") };
                opts.Children.Add(cb);
                return cb;
            }

            var optRef = Opt("Tanımlayıcı", true);
            var optVal = Opt("Değer", true);
            var optNet = Opt("Hat/etiket", true);
            var optCase = Opt("Büyük/küçük", false);
            var optWhole = Opt("Tam eşleşme", false);
            stack.Children.Add(opts);
            var replaceButton = new Button { Content = "Tümünü değiştir", Margin = new Thickness(0, 4, 0, 0), Visibility = replace ? Visibility.Visible : Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(replaceButton);
            var count = new TextBlock { Style = Res<Style>("Text.Caption"), Margin = new Thickness(0, 8, 0, 4) };
            stack.Children.Add(count);
            var list = new ListBox { MaxHeight = 280 };
            stack.Children.Add(list);
            var card = Card(330, stack);

            SearchOptions Options() => new SearchOptions
            {
                RefDes = optRef.IsChecked == true,
                Values = optVal.IsChecked == true,
                Nets = optNet.IsChecked == true,
                MatchCase = optCase.IsChecked == true,
                WholeWord = optWhole.IsChecked == true
            };

            void Search()
            {
                var hits = SchematicSearch.Find(W.Schematic, W.Library, find.Text, Options());
                count.Text = find.Text.Trim().Length == 0 ? "Aramak için yazın · Enter: sonraki" : $"{hits.Count} sonuç";
                list.ItemsSource = hits.Select(h => new ListBoxItem
                {
                    Content = new TextBlock
                    {
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Inlines =
                        {
                            new System.Windows.Documents.Run(h.KindText.PadRight(7)) { Foreground = Res<Brush>("B.TextMuted"), FontSize = 12 },
                            new System.Windows.Documents.Run(h.Title) { Foreground = Res<Brush>("B.Text") },
                            new System.Windows.Documents.Run("  " + h.Detail) { Foreground = Res<Brush>("B.TextMuted"), FontSize = 12 }
                        }
                    },
                    Tag = h
                }).ToList();
            }

            void Go(SearchHit hit)
            {
                Canvas.ZoomToPoint(hit.Location);
                if (hit.Target != null) Canvas.SelectItems(new[] { hit.Target });
                if (hit.Net != null) Canvas.HighlightedNet = hit.Net;
            }

            find.TextChanged += (_, _) => Search();
            foreach (var cb in opts.Children.OfType<CheckBox>()) cb.Click += (_, _) => Search();
            list.SelectionChanged += (_, _) =>
            {
                if ((list.SelectedItem as ListBoxItem)?.Tag is SearchHit h) Go(h);
            };
            find.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && list.Items.Count > 0)
                {
                    list.SelectedIndex = (list.SelectedIndex + 1) % list.Items.Count;
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    RemoveOverlay(ref _findPanel);
                    e.Handled = true;
                }
            };
            replaceButton.Click += (_, _) =>
            {
                if (W.Simulation.IsActive || find.Text.Trim().Length == 0) return;
                W.BeginChange();
                int n = SchematicSearch.Replace(W.Schematic, W.Library, find.Text.Trim(), repl.Text, Options());
                if (n == 0)
                {
                    W.CancelChange();
                    count.Text = "Değiştirilecek bir şey bulunamadı";
                    return;
                }

                W.CommitChange($"{n} alan değiştirildi: '{find.Text.Trim()}' → '{repl.Text}'");
                count.Text = $"{n} alan değiştirildi (Ctrl+Z geri alır)";
            };
            close.Click += (_, _) => RemoveOverlay(ref _findPanel);
            _findPanel = card;
            _overlay.Children.Add(card);
            Place(card, _overlay.ActualWidth - 350, 12);
            Search();
            Dispatcher.BeginInvoke(new Action(() => find.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        // ================================================================ ERC

        public void RunErc(bool keepOpen)
        {
            _erc = ElectricalRulesChecker.Check(W.Schematic, W.Library);
            if (keepOpen && _ercPanel == null) return;
            BuildErcPanel();
        }

        private void BuildErcPanel()
        {
            RemoveOverlay(ref _ercPanel, false);
            var result = ErcPolicy.Apply(W.Schematic, _erc);   // [erc-policy] the user's severities and exclusions
            int errors = result.Errors, warnings = result.Warnings, infos = result.Infos;
            var shown = result.Shown.Where(v => _ercShowInfo || v.Severity != ErcSeverity.Info).ToList();
            Canvas.ErcMarkers = shown;
            var excluded = _ercShowExcluded ? result.Excluded : new List<(ErcViolation Violation, ErcExclusion Exclusion)>();
            var stack = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var close = new Button { Content = "✕", Style = Res<Style>("Btn.Ghost"), Padding = new Thickness(6, 0, 6, 0) };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(new TextBlock { Text = "Elektriksel kural denetimi", Style = Res<Style>("Text.Overline"), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(head);
            var summary = new TextBlock { Style = Res<Style>("Text.Body"), Margin = new Thickness(0, 0, 0, 6) };
            summary.Inlines.Add(new System.Windows.Documents.Run($"{errors} hata") { Foreground = errors > 0 ? Res<Brush>("B.Danger") : Res<Brush>("B.TextSecondary") });
            summary.Inlines.Add(new System.Windows.Documents.Run($"  ·  {warnings} uyarı") { Foreground = warnings > 0 ? Res<Brush>("B.Warning") : Res<Brush>("B.TextSecondary") });
            summary.Inlines.Add(new System.Windows.Documents.Run($"  ·  {infos} bilgi") { Foreground = Res<Brush>("B.TextMuted") });
            if (result.Excluded.Count > 0) summary.Inlines.Add(new System.Windows.Documents.Run($"  ·  {result.Excluded.Count} istisna") { Foreground = Res<Brush>("B.TextMuted") });
            stack.Children.Add(summary);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var rerun = new Button { Content = "Yeniden çalıştır", Padding = new Thickness(10, 2, 10, 2) };
            var info = new CheckBox { Content = "Bilgileri göster", IsChecked = _ercShowInfo, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Res<Brush>("B.TextSecondary") };
            var showExcluded = new CheckBox { Content = "İstisnalar", IsChecked = _ercShowExcluded, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Res<Brush>("B.TextSecondary"), ToolTip = "İstisna olarak işaretlenenleri de listele" };
            bar.Children.Add(rerun);
            bar.Children.Add(info);
            bar.Children.Add(showExcluded);
            stack.Children.Add(bar);
            int set = W.Schematic.ErcSeverities.Count;
            if (set > 0)
            {
                var reset = new Button { Content = "Varsayılana döndür", Style = Res<Style>("Btn.Ghost"), Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(8, 0, 0, 0) };
                reset.Click += (_, _) => ErcPolicyEdit("ERC kural önemleri varsayılana döndü", () => W.Schematic.ErcSeverities.Clear());
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                DockPanel.SetDock(reset, Dock.Right);
                line.Children.Add(reset);
                line.Children.Add(new TextBlock { Text = $"{set} kuralın önemi değiştirildi" + (result.Ignored.Count > 0 ? $" ({result.Ignored.Values.Sum()} işaret denetlenmedi)" : string.Empty), Style = Res<Style>("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                stack.Children.Add(line);
            }
            if (shown.Count == 0 && excluded.Count == 0)
            {
                stack.Children.Add(new TextBlock { Text = errors + warnings == 0 ? "✓ Sorun bulunamadı." : "Gösterilecek öğe yok.", Style = Res<Style>("Text.Body"), Foreground = Res<Brush>("B.Success") });
            }

            var list = new ListBox { MaxHeight = 360 };
            void AddRow(ErcViolation v, ErcExclusion? ex)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 7, Height = 7, Margin = new Thickness(0, 5, 8, 0), VerticalAlignment = VerticalAlignment.Top,
                    Fill = ex != null ? Res<Brush>("B.TextMuted") : v.Severity == ErcSeverity.Error ? Res<Brush>("B.Danger") : v.Severity == ErcSeverity.Warning ? Res<Brush>("B.Warning") : Res<Brush>("B.TextMuted")
                };
                row.Children.Add(dot);
                string text = ex == null ? v.Message : Lang.T("İstisna") + " · " + Lang.T(v.Message) + (ex.Comment.Length > 0 ? " — " + ex.Comment : string.Empty);
                row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Res<Brush>(ex == null ? "B.Text" : "B.TextSecondary"), FontSize = 12, MaxWidth = 300 });
                var item = new ListBoxItem { Content = row, Tag = v, ToolTip = v.SeverityText + " · " + (ErcRuleCatalog.Find(v.Code)?.Name ?? v.Code) };
                item.ContextMenu = ErcMenu(v, ex);
                list.Items.Add(item);
            }

            foreach (var v in shown) AddRow(v, null);
            foreach (var (v, ex) in excluded) AddRow(v, ex);
            if (list.Items.Count > 0) stack.Children.Add(list);
            list.SelectionChanged += (_, _) =>
            {
                if ((list.SelectedItem as ListBoxItem)?.Tag is not ErcViolation v) return;
                Canvas.ZoomToPoint(v.Location, 22);
                var part = v.RefDes != null ? W.Schematic.FindPart(v.RefDes) : null;
                if (part != null) Canvas.SelectItems(new object[] { part });
                Canvas.HighlightedNet = v.Net;
            };
            rerun.Click += (_, _) => RunErc(keepOpen: false);
            info.Click += (_, _) =>
            {
                _ercShowInfo = info.IsChecked == true;
                BuildErcPanel();
            };
            showExcluded.Click += (_, _) =>
            {
                _ercShowExcluded = showExcluded.IsChecked == true;
                BuildErcPanel();
            };
            close.Click += (_, _) =>
            {
                RemoveOverlay(ref _ercPanel);
                Canvas.ErcMarkers = null;
            };
            var card = Card(350, stack);
            _ercPanel = card;
            _overlay.Children.Add(card);
            Place(card, _overlay.ActualWidth - 370, 12);
            W.Status = $"ERC: {errors} hata, {warnings} uyarı";
        }

        /// <summary>[erc-policy] A change to the schematic's ERC settings: undoable, saved, and the list follows.</summary>
        private void ErcPolicyEdit(string message, Action change)
        {
            W.BeginChange();
            change();
            W.CommitChange(message);
            BuildErcPanel();
        }

        /// <summary>[erc-policy] A marker's right-click menu: accept it (or take it back), the rule's severity.</summary>
        private ContextMenu ErcMenu(ErcViolation v, ErcExclusion? ex)
        {
            var m = new ContextMenu();
            var rule = ErcRuleCatalog.Find(v.Code);
            string name = rule?.Name ?? v.Code;
            if (ex != null)
            {
                var undo = new MenuItem { Header = "İstisnayı kaldır" };
                undo.Click += (_, _) => ErcPolicyEdit($"ERC istisnası kaldırıldı: {name}", () => ErcPolicy.Include(W.Schematic, v));
                m.Items.Add(undo);
            }
            else
            {
                var accept = new MenuItem { Header = "İstisna olarak işaretle…" };
                accept.Click += (_, _) =>
                {
                    string? note = Dialog.Prompt(Window.GetWindow(this)!, "İstisna olarak işaretle", $"{v.Message}\n\nNeden kabul edildiği (isteğe bağlı):", string.Empty, allowEmpty: true);
                    if (note == null) return;
                    ErcPolicyEdit($"ERC istisnası: {name}", () => ErcPolicy.Exclude(W.Schematic, v, note));
                    ShowToast("İşaret istisna olarak işaretlendi; 'İstisnalar' ile görünür");
                };
                m.Items.Add(accept);
            }

            var sev = new MenuItem { Header = $"'{name}' kuralının önemi" };
            var current = ErcPolicy.SeverityOf(W.Schematic, v.Code);
            foreach (var s in new[] { ErcRuleSeverity.Default, ErcRuleSeverity.Error, ErcRuleSeverity.Warning, ErcRuleSeverity.Info, ErcRuleSeverity.Ignore })
            {
                var choice = s;
                string label = s switch
                {
                    ErcRuleSeverity.Error => "Hata",
                    ErcRuleSeverity.Warning => "Uyarı",
                    ErcRuleSeverity.Info => "Bilgi",
                    ErcRuleSeverity.Ignore => "Denetleme",
                    _ => rule?.Default switch
                    {
                        ErcSeverity.Error => "Varsayılan (hata)",
                        ErcSeverity.Warning => "Varsayılan (uyarı)",
                        ErcSeverity.Info => "Varsayılan (bilgi)",
                        _ => "Varsayılan (duruma göre)"
                    }
                };
                var it = new MenuItem { Header = label, IsCheckable = true, IsChecked = s == current };
                it.Click += (_, _) => ErcPolicyEdit($"ERC kuralı '{name}': {label}", () => ErcPolicy.SetSeverity(W.Schematic, v.Code, choice));
                sev.Items.Add(it);
            }

            m.Items.Add(sev);
            if (rule != null) m.Items.Add(new MenuItem { Header = rule.Description, IsEnabled = false });
            return m;
        }

        // ================================================================ annotation

        public void ShowAnnotateDialog()
        {
            if (W.Simulation.IsActive)
            {
                ShowToast("Numaralandırmak için simülasyonu durdurun.");
                return;
            }

            var body = new StackPanel();
            body.Children.Add(Caption("Parçaları sayfadaki konumlarına göre R1, R2, C1… şeklinde numaralandırır. PCB ve montajdaki tanımlayıcılar da güncellenir."));
            var rows = new RadioButton { Content = "Satır satır (yukarıdan aşağı, soldan sağa)", IsChecked = true, GroupName = "order", Foreground = Res<Brush>("B.Text"), Margin = new Thickness(0, 4, 0, 4) };
            var cols = new RadioButton { Content = "Sütun sütun (soldan sağa, yukarıdan aşağı)", GroupName = "order", Foreground = Res<Brush>("B.Text"), Margin = new Thickness(0, 0, 0, 10) };
            var onlyNew = new RadioButton { Content = "Yalnızca numarasız (R?) ve çakışanlar", IsChecked = true, GroupName = "scope", Foreground = Res<Brush>("B.Text"), Margin = new Thickness(0, 0, 0, 4) };
            var all = new RadioButton { Content = "Tümünü baştan numaralandır", GroupName = "scope", Foreground = Res<Brush>("B.Text"), Margin = new Thickness(0, 0, 0, 10) };
            var power = new CheckBox { Content = "Güç sembollerini de numaralandır (#PWR, #GND)", IsChecked = true, Foreground = Res<Brush>("B.Text") };
            foreach (var el in new UIElement[] { rows, cols, onlyNew, all, power }) body.Children.Add(el);
            var dups = Annotator.Duplicates(W.Schematic).Count;
            int unannotated = W.Schematic.Parts.Count(p => !Annotator.IsAnnotated(p.RefDes));
            body.Children.Add(Caption($"Şu an: {unannotated} numarasız, {dups} çakışan tanımlayıcı."));
            if (!ShowCardDialog("Numaralandır", body, "Numaralandır")) return;
            AnnotateNow(new AnnotationOptions
            {
                Order = cols.IsChecked == true ? AnnotationOrder.Columns : AnnotationOrder.Rows,
                ResetExisting = all.IsChecked == true,
                IncludePowerSymbols = power.IsChecked == true
            });
        }

        public void AnnotateNow(AnnotationOptions options)
        {
            W.BeginChange();
            var changes = Annotator.Annotate(W.Schematic, W.Library, options);
            if (changes.Count == 0)
            {
                W.CancelChange();
                W.Status = "Numaralandırma: değişiklik yok";
                return;
            }

            Annotator.ApplyToLinkedData(W.Schematic, changes);
            var map = changes.ToDictionary(c => c.OldRefDes, c => c.NewRefDes, StringComparer.OrdinalIgnoreCase);
            foreach (var f in W.Project.Firmware) if (map.TryGetValue(f.BoardRefDes, out var n)) f.BoardRefDes = n;
            W.CommitChange($"Numaralandırıldı: {changes.Count} parça ({string.Join(", ", changes.Take(5))}{(changes.Count > 5 ? "…" : string.Empty)})");
        }

        // ================================================================ help overlay (F1 / ?)

        public void ToggleHelp()
        {
            if (_helpPanel != null)
            {
                RemoveOverlay(ref _helpPanel);
                return;
            }

            var grid = new UniformGrid { Columns = 3 };
            foreach (var (group, items) in SchematicShortcuts.All)
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 18, 14) };
                col.Children.Add(new TextBlock { Text = group, Style = Res<Style>("Text.Overline"), Margin = new Thickness(0, 0, 0, 6) });
                foreach (var (keys, what) in items)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Children.Add(new TextBlock { Text = keys, FontFamily = Res<FontFamily>("F.Mono"), FontSize = 12, Foreground = Res<Brush>("B.Accent"), TextWrapping = TextWrapping.Wrap });
                    var t = new TextBlock { Text = what, Foreground = Res<Brush>("B.Text"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
                    Grid.SetColumn(t, 1);
                    row.Children.Add(t);
                    col.Children.Add(row);
                }

                grid.Children.Add(col);
            }

            var stack = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(new TextBlock { Text = "Esc / F1 kapat", Style = Res<Style>("Text.Caption"), VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(head.Children[0], Dock.Right);
            head.Children.Add(new TextBlock { Text = "Kısayollar", Style = Res<Style>("Text.Title"), FontSize = 18 });
            stack.Children.Add(head);
            stack.Children.Add(grid);
            stack.Children.Add(new TextBlock { Text = "Sağ tık her öğede bağlama uygun menüyü açar; menülerde kısayollar sağda gösterilir.", Style = Res<Style>("Text.Caption") });
            double width = Math.Min(980, Math.Max(560, _overlay.ActualWidth - 40));
            var card = Card(width, stack, 20);
            card.MouseLeftButtonDown += (_, _) => RemoveOverlay(ref _helpPanel);
            card.Focusable = true;
            card.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.F1)
                {
                    RemoveOverlay(ref _helpPanel);
                    e.Handled = true;
                }
            };
            _helpPanel = card;
            _overlay.Children.Add(card);
            card.Measure(new Size(width, double.PositiveInfinity));
            Place(card, (_overlay.ActualWidth - width) / 2, Math.Max(12, (_overlay.ActualHeight - card.DesiredSize.Height) / 2));
            Dispatcher.BeginInvoke(new Action(() => card.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        // ================================================================ exports

        private string SafeTitle => string.Concat((string.IsNullOrWhiteSpace(W.Schematic.Title) ? "devre" : W.Schematic.Title).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        private string? AskSavePath(string filter, string ext, string suffix = "")
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter(filter), DefaultExt = ext, FileName = SafeTitle + suffix + ext };
            return dlg.ShowDialog(Window.GetWindow(this)) == true ? dlg.FileName : null;
        }

        private BomWindow? _bomWindow;

        private void ExportBom()
        {
            var bom = BomBuilder.Build(Variants.Active(W.Schematic), W.Library);   // [variants] the active alternative's
            if (bom.Count == 0)
            {
                ShowToast("Malzeme listesi boş: sayfada parça yok.");
                return;
            }

            // [bom] the list with its sources, costing and the suppliers' order files
            var window = new BomWindow(Window.GetWindow(this)!, W.Schematic, W.Library, bom, W.BeginChange, () => W.CommitChange("Malzeme listesi güncellendi"));
            window.Show();
            _bomWindow = window;
        }

        private void ExportText(string filter, string ext, Func<string> build, string what)
        {
            var path = AskSavePath(filter, ext);
            if (path == null) return;
            try
            {
                File.WriteAllText(path, build(), new UTF8Encoding(false));
                W.Status = $"{what} kaydedildi → {Path.GetFileName(path)}";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowToast("Kaydedilemedi: " + ex.Message);
            }
        }

        private void ExportImage()
        {
            var path = AskSavePath("PNG görüntü (*.png)|*.png|SVG vektör (*.svg)|*.svg", ".png");
            if (path == null) return;
            try
            {
                var drawing = Canvas.RenderSheet(out var size);
                if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) File.WriteAllText(path, SchematicExport.ToSvg(drawing, size, W.Schematic.Title), new UTF8Encoding(false));
                else SchematicExport.SavePng(drawing, size, path);
                W.Status = $"Şema görüntüsü kaydedildi ({size.Width:0}×{size.Height:0}) → {Path.GetFileName(path)}";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowToast("Kaydedilemedi: " + ex.Message);
            }
        }

        /// <summary>Ctrl+P: prints the sheet fitted to the page ("Microsoft Print to PDF" gives a PDF).</summary>
        private void PrintSheet()
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;
            var drawing = Canvas.RenderSheet(out var size);
            double margin = 36;
            double w = dlg.PrintableAreaWidth - 2 * margin, h = dlg.PrintableAreaHeight - 2 * margin;
            double scale = Math.Min(w / size.Width, h / size.Height);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(margin + (w - size.Width * scale) / 2, margin));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawDrawing(drawing);
                dc.Pop();
                dc.Pop();
                var ft = new FormattedText(W.Schematic.Title + "   ·   " + DateTime.Now.ToString("d MMMM yyyy", RobotWin.Studio.Shell.Lang.Culture) + "   ·   RobotWin Studio",
                    CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, Brushes.Gray, 1.0);
                dc.DrawText(ft, new Point(margin, dlg.PrintableAreaHeight - margin + 8));
            }

            dlg.PrintVisual(visual, W.Schematic.Title);
            W.Status = "Yazdırma gönderildi";
        }

        // ================================================================ snapshot hooks

        /// <summary>--snapshot --page schematic --fault &lt;name&gt; without --simulate: shows an editor feature.</summary>
        public void PrepareEditorSnapshot(string? fault)
        {
            if (fault == null) return;
            UpdateLayout();
            var sch = W.Schematic;
            var b = SchematicEditing.Bounds(SchematicEditing.AllItems(sch), W.Library) ?? new GridRect(-10, -10, 10, 10);
            var firstPart = sch.Parts.OrderBy(p => p.RefDes, Annotator.NaturalComparer).FirstOrDefault(p => !Annotator.IsPowerSymbol(p.RefDes) && W.Library.Find(p.PartId)?.Symbol != SymbolKind.Board)
                            ?? sch.Parts.FirstOrDefault();
            switch (fault)
            {
                case "bom":   // [bom] the list with two lines sourced and costed, the window is the picture
                    ExportBom();
                    if (_bomWindow != null)
                    {
                        _bomWindow.FillForSnapshot();
                        App.SnapshotTarget = _bomWindow.Content as FrameworkElement;
                    }

                    return;
                case "variants":
                case "variants-window":
                {
                    // [variants] two alternatives of the example: a part left off, and another value; the first chosen
                    var parts = sch.Parts.Where(p => W.Library.Find(p.PartId) is { } d && d.Symbol != SymbolKind.Ground && d.Symbol != SymbolKind.PowerRail && d.Symbol != SymbolKind.Board).ToList();
                    var valued = parts.FirstOrDefault(p => p.RefDes.StartsWith("R", StringComparison.Ordinal) && W.Library.Find(p.PartId)!.ValueKey.Length > 0);
                    var left = parts.LastOrDefault(p => !ReferenceEquals(p, valued) && W.Library.Find(p.PartId)!.Symbol != SymbolKind.Battery);
                    sch.EnsurePartUids();
                    var lean = new DesignVariant { Name = "Hafif", Note = "pil ömrü için" };
                    if (left != null) lean.Edit(left).NotFitted = true;
                    if (valued != null) lean.Edit(valued).Parameters[W.Library.Find(valued.PartId)!.ValueKey] = "1k";
                    var strong = new DesignVariant { Name = "Güçlü" };
                    if (valued != null) strong.Edit(valued).Parameters[W.Library.Find(valued.PartId)!.ValueKey] = "47";
                    sch.Variants.Add(lean);
                    sch.Variants.Add(strong);
                    sch.ActiveVariant = lean.Name;
                    RefreshVariantBox();
                    if (left != null) Canvas.SelectItems(new object[] { left });
                    BuildInspector();
                    Canvas.InvalidateVisual();
                    if (fault == "variants-window")
                    {
                        ShowVariants();
                        if (Application.Current.Windows.OfType<VariantsWindow>().FirstOrDefault() is { } open) open.CompareForSnapshot();
                    }

                    break;
                }

                case "menu":
                case "menu-wire":
                case "menu-empty":
                    {
                        GridPoint at;
                        if (fault == "menu" && firstPart != null) at = firstPart.Position;
                        else if (fault == "menu-wire" && sch.Wires.FirstOrDefault(w => w.Points.Count >= 2) is { } w0)
                            at = new GridPoint((w0.Points[0].X + w0.Points[1].X) / 2, (w0.Points[0].Y + w0.Points[1].Y) / 2);
                        else at = new GridPoint(b.MinX - 2, b.MinY - 2);
                        var menu = Canvas.BuildContextMenuAt(at);
                        var p = Canvas.GridToScreen(at);
                        // Draw the menu items inline (a ContextMenu is a popup window and cannot be parented or captured).
                        var items = menu.Items.Cast<object>().ToList();
                        menu.Items.Clear();
                        var host = new StackPanel();
                        foreach (var item in items) host.Children.Add((UIElement)item);
                        var card = Card(double.NaN, host, 4);
                        card.CornerRadius = new CornerRadius(6);
                        _snapshotMenu = card;
                        _overlay.Children.Add(card);
                        Place(card, p.X + 4, p.Y + 4);
                        Canvas.SimulateCursor(at);
                        break;
                    }

                case "box":
                    Canvas.SimulateBox(new GridPoint(b.Center.X + 2, b.MaxY + 1), new GridPoint(b.MinX - 1, b.Center.Y - 2));
                    break;
                case "box-window":
                    Canvas.SimulateBox(new GridPoint(b.MinX - 1, b.MinY - 1), new GridPoint(b.Center.X, b.MaxY + 1));
                    break;
                case "select":
                    Canvas.SelectItems(SchematicEditing.ItemsInRect(sch, W.Library, new GridRect(b.MinX - 1, b.Center.Y, b.MaxX + 1, b.MaxY + 1), true));
                    break;
                case "erc":
                    RunErc(keepOpen: false);
                    break;
                case "erc-policy":   // [erc-policy] one marker accepted, one rule made an error, exclusions listed
                    _erc = ElectricalRulesChecker.Check(W.Schematic, W.Library);
                    var accepted = _erc.FirstOrDefault(v => v.Severity == ErcSeverity.Warning);
                    if (accepted != null) ErcPolicy.Exclude(W.Schematic, accepted, "bilerek");
                    if (_erc.FirstOrDefault(v => v.Severity == ErcSeverity.Warning && v.Code != accepted?.Code) is { } raised) ErcPolicy.SetSeverity(W.Schematic, raised.Code, ErcRuleSeverity.Error);
                    _ercShowExcluded = true;
                    _ercShowInfo = true;
                    BuildErcPanel();
                    break;
                case "notes":   // [notes] a title and a framed note on the sheet
                {
                    var nb = SchematicEditing.Bounds(SchematicEditing.AllItems(W.Schematic), W.Library);
                    double nx = nb?.MinX ?? 0, ny = (nb?.MinY ?? 0) - 6;
                    W.Schematic.Notes.Add(new SchematicNote { Text = "Motor sürücü", Position = new GridPoint(nx, ny), Size = 2, Bold = true });
                    W.Schematic.Notes.Add(new SchematicNote { Text = "Q1 soğutucusuz en çok 2 A\nBT1: 3S LiPo, 11,1 V", Position = new GridPoint((nb?.MaxX ?? 20) + 3, (nb?.MinY ?? 0)), Boxed = true });
                    Canvas.InvalidateVisual();
                    break;
                }
                case "dock-min":   // [dock-min] the library minimized to its strip
                    _dock?.Minimize(DockPlace.Left, true);
                    break;
                case "fullview":   // [dock-min] Ctrl+Space: the sheet alone
                    _dock?.ToggleFullView();
                    break;
                case "nets":   // [net-navigator] the nets tab with a net picked
                    _dock?.Reveal("nets");
                    RefreshNets();
                    if (_netsList != null && _netsList.Items.Count > 1) _netsList.SelectedIndex = 1;
                    break;
                case "find":
                    ShowFind(true);
                    if (_findPanel is Border fb && fb.Child is StackPanel fs && fs.Children[1] is TextBox ft) ft.Text = "R";
                    break;
                case "help":
                    ToggleHelp();
                    break;
                case "picker":
                    ShowPartPicker(false);
                    if (_picker is Border pb && pb.Child is StackPanel ps && ps.Children[1] is TextBox pt) pt.Text = "led";
                    break;
                case "power":
                    ShowPartPicker(true);
                    break;
                case "highlight":
                    {
                        var net = sch.BuildNetlist(W.Library).Nets.Where(n => !n.IsGround).OrderByDescending(n => n.Connections.Count).FirstOrDefault();
                        Canvas.HighlightedNet = net?.Name;
                        break;
                    }

                case "value":
                    if (firstPart != null) EditValueInline(firstPart);
                    break;
                case "paste":
                    {
                        var parts = sch.Parts.Where(p => !Annotator.IsPowerSymbol(p.RefDes)).Take(2).Cast<object>().ToList();
                        Canvas.SelectItems(parts);
                        var f = Canvas.SelectionFragment();
                        if (f != null)
                        {
                            Canvas.BeginFloating(f, "Yapıştırıldı");
                            f.MoveAnchorTo(new GridPoint(Math.Round(b.MaxX) - 4, Math.Round(b.MaxY) + 6));
                            Canvas.SimulateCursor(f.Anchor);
                        }

                        break;
                    }

                case "wire45":
                    {
                        var c = SchematicEditing.Snap(new GridPoint(b.MinX - 6, b.MaxY - 2));
                        Canvas.SimulateWire(WireMode.Diagonal45, c, c + new GridPoint(9, -5));
                        break;
                    }

                case "edit":
                    {
                        // Drives the real editor operations: G-drag with rubber-banding, rotate, copy/paste, export.
                        var r = sch.Parts.FirstOrDefault(p => p.PartId == "resistor");
                        if (r == null) break;
                        int undo0 = 0;
                        Canvas.SelectItems(new object[] { r });
                        Canvas.HandleKey(Key.G, false, false);
                        Canvas.SimulateCursor(Canvas.MouseGrid + new GridPoint(0, -4));
                        Canvas.CommitPending();
                        Canvas.HandleKey(Key.R, false, false);
                        Canvas.HandleKey(Key.C, true, false);
                        Canvas.HandleKey(Key.V, true, false);
                        Canvas.SimulateCursor(new GridPoint(Math.Round(b.MaxX) - 2, Math.Round(b.MaxY) + 4));
                        Canvas.CommitPending();
                        var log = new StringBuilder();
                        log.AppendLine("parts=" + string.Join(",", sch.Parts.Select(p => p.RefDes)));
                        log.AppendLine("r=" + r.RefDes + " pos=" + r.Position + " rot=" + r.Rotation);
                        log.AppendLine("status=" + W.Status + " canUndo=" + W.CanUndo + " undo0=" + undo0);
                        var drawing = Canvas.RenderSheet(out var size);
                        File.WriteAllText("edit-export.svg", SchematicExport.ToSvg(drawing, size, sch.Title));
                        SchematicExport.SavePng(drawing, size, "edit-export.png", 1.0);
                        File.WriteAllText("edit-bom.csv", BomBuilder.ToCsv(BomBuilder.Build(sch, W.Library)), new UTF8Encoding(true));
                        File.WriteAllText("edit-erc.txt", string.Join(Environment.NewLine, ElectricalRulesChecker.Check(sch, W.Library)));
                        File.WriteAllText("edit-log.txt", log.ToString());
                        RunErc(keepOpen: false);
                        break;
                    }

                case "grid":
                    Canvas.GridStyle = GridStyle.Lines;
                    Canvas.Crosshair = true;
                    Canvas.SimulateCursor(SchematicEditing.Snap(b.Center));
                    break;
            }
        }
    }
}
