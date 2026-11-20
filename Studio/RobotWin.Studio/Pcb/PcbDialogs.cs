using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using RobotTwin.CoreSim.Pcb;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pcb
{
    /// <summary>Board setup (stackup and rule minimums), zone properties and board text dialogs.</summary>
    internal static class PcbDialogs
    {
        private static readonly (string Name, double Um)[] Weights = { ("0.5 oz (17.5 µm)", 17.5), ("1 oz (35 µm)", 35), ("2 oz (70 µm)", 70), ("3 oz (105 µm)", 105) };
        private static readonly string[] Finishes = { "HAL lead-free", "HAL SnPb", "ENIG", "OSP", "Immersion silver", "None" };

        private static double Nearest(double um) => Weights.OrderBy(w => Math.Abs(w.Um - um)).First().Um;

        /// <summary>Stackup and rule minimums. Applies the changes and returns true on OK.</summary>
        public static bool BoardSetup(Window? owner, BoardDocument board, out string summary)
        {
            summary = string.Empty;
            var st = board.Stackup.Clone();
            var rules = board.Rules;
            double outer = Nearest(rules.CopperThicknessUm);
            double minTrack = rules.MinTrackWidth, minDrill = rules.MinDrill, ring = rules.MinAnnularRing, h2h = rules.MinHoleToHole;
            double edge = rules.EdgeClearance, holeClr = rules.HoleClearance, grid = rules.RoutingGrid;

            var panel = new StackPanel();
            var left = new StackPanel { Width = 300 };
            var right = new StackPanel { Width = 300, Margin = new Thickness(24, 0, 0, 0) };
            var columns = new StackPanel { Orientation = Orientation.Horizontal };
            columns.Children.Add(left);
            columns.Children.Add(right);
            panel.Children.Add(new TextBlock { Text = "Kart ayarları", Style = S("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(Caption("Katman yığını üretici formuna olduğu gibi aktarılır (Gerber, KiCad stackup)."));
            panel.Children.Add(columns);

            left.Children.Add(Overline("Bakır katmanları"));
            var countRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var chips = new List<ToggleButton>();
            var blind = Switch("Kör / gömülü via'lara izin ver", st.AllowBlindBuriedVias, v => st.AllowBlindBuriedVias = v);
            var table = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            void Redraw()
            {
                foreach (var c in chips) c.IsChecked = (int)c.Tag == st.CopperLayers;
                blind.IsEnabled = st.CopperLayers >= 4;
                table.Children.Clear();
                foreach (var l in st.Build(outer))
                {
                    var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    var color = l.Copper.HasValue ? BoardCanvas.LayerColor(l.Copper.Value) : Color.FromRgb(0x4A, 0x48, 0x3E);
                    row.Children.Add(new Rectangle { Width = 8, Height = l.Copper.HasValue ? 6 : 12, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, RadiusX = 1, RadiusY = 1 });
                    var name = new TextBlock { Text = l.Copper.HasValue ? $"{l.Name}  ({PcbLayers.DisplayName(l.Copper.Value)})" : $"{l.Name} · {l.Type} · {l.Material} εr {l.EpsilonR:0.##}", Style = S("Text.Caption") };
                    Grid.SetColumn(name, 1);
                    row.Children.Add(name);
                    var th = new TextBlock { Text = l.Thickness.ToString("0.000", CultureInfo.InvariantCulture) + " mm", Style = S("Text.Mono"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(th, 2);
                    row.Children.Add(th);
                    table.Children.Add(row);
                }
            }

            foreach (int n in PcbLayers.LayerCounts)
            {
                var chip = Chip(n.ToString(CultureInfo.InvariantCulture) + " katman", n == st.CopperLayers, (_, _) => { });
                chip.Tag = n;
                chip.Click += (_, _) =>
                {
                    st.CopperLayers = (int)chip.Tag;
                    if (st.CopperLayers < 4) st.AllowBlindBuriedVias = false;
                    blind.IsChecked = st.AllowBlindBuriedVias;
                    Redraw();
                };
                chips.Add(chip);
                countRow.Children.Add(chip);
            }

            left.Children.Add(countRow);
            left.Children.Add(Row("Kart kalınlığı (mm)", Number(st.Thickness, v => { st.Thickness = v; Redraw(); }, 0.2, 6)));
            left.Children.Add(Row("Dış bakır", Combo(Weights.Select(w => w.Um), outer, v => { outer = v; Redraw(); }, v => Weights.First(w => w.Um == v).Name), 140));
            left.Children.Add(Row("İç bakır", Combo(Weights.Select(w => w.Um), Nearest(st.InnerCopperUm), v => { st.InnerCopperUm = v; Redraw(); }, v => Weights.First(w => w.Um == v).Name), 140));
            left.Children.Add(Row("Dielektrik", Text(st.Dielectric, v => { st.Dielectric = v.Trim().Length > 0 ? v.Trim() : "FR4"; Redraw(); }), 140));
            left.Children.Add(Row("εr (dielektrik sabiti)", Number(st.DielectricConstant, v => { st.DielectricConstant = v; Redraw(); }, 1, 20)));
            left.Children.Add(Row("Kayıp tanjantı", Number(st.LossTangent, v => st.LossTangent = v, 0, 1, true)));
            left.Children.Add(Row("Prepreg (mm)", Number(st.PrepregThickness, v => { st.PrepregThickness = v; Redraw(); }, 0.03, 2)));
            left.Children.Add(Row("Yüzey kaplama", Combo(Finishes, Finishes.Contains(st.Finish) ? st.Finish : Finishes[0], v => st.Finish = v), 140));
            left.Children.Add(blind);
            left.Children.Add(Overline("Yığın"));
            left.Children.Add(table);

            right.Children.Add(Overline("Üretim kuralları (en az)"));
            right.Children.Add(Row("Yol genişliği (mm)", Number(minTrack, v => minTrack = v, 0.05, 5)));
            right.Children.Add(Row("Delik çapı (mm)", Number(minDrill, v => minDrill = v, 0.05, 6)));
            right.Children.Add(Row("Halka genişliği (mm)", Number(ring, v => ring = v, 0, 2, true), tip: "Ped/via bakırının delik etrafındaki en dar kısmı"));
            right.Children.Add(Row("Delik-delik (mm)", Number(h2h, v => h2h = v, 0, 5, true)));
            right.Children.Add(Row("Kart kenarına (mm)", Number(edge, v => edge = v, 0, 10, true)));
            right.Children.Add(Row("Montaj deliğine (mm)", Number(holeClr, v => holeClr = v, 0, 10, true)));
            right.Children.Add(Row("Oto. rota ızgarası (mm)", Number(grid, v => grid = v, 0.05, 2)));
            right.Children.Add(Caption("Hat başına boşluk, yol ve via ölçüleri 'Hatlar' sekmesindeki hat sınıflarındadır.", 8));
            Redraw();

            var dlg = MakeDialog(owner, "Kart ayarları", 680, panel, out var ok);
            ok.Click += (_, _) => dlg.DialogResult = true;
            if (dlg.ShowDialog() != true) return false;

            // Items on layers that no longer exist.
            var gone = board.CopperLayers().Where(l => !PcbLayers.Exists(l, st.CopperLayers)).ToList();
            int lostTracks = board.Tracks.Count(t => gone.Contains(t.Layer));
            int lostZones = board.Zones.Count(z => gone.Contains(z.Layer));
            if (lostTracks + lostZones > 0)
            {
                int answer = RobotWin.Studio.Shell.Dialog.Show(owner ?? Application.Current.MainWindow, "Katmanlar kaldırılıyor",
                    $"{string.Join(", ", gone.Select(PcbLayers.KiCadName))} üzerinde {lostTracks} yol ve {lostZones} dolgu var; bunlar silinecek.", "Devam", "Vazgeç");
                if (answer != 0) return false;
            }

            board.Tracks.RemoveAll(t => gone.Contains(t.Layer));
            board.Zones.RemoveAll(z => gone.Contains(z.Layer));
            board.Stackup = st;
            foreach (var v in board.Vias)
            {
                if (!PcbLayers.Exists(v.StartLayer, st.CopperLayers) || !PcbLayers.Exists(v.EndLayer, st.CopperLayers) || !st.AllowBlindBuriedVias)
                {
                    v.StartLayer = CopperLayer.Top;
                    v.EndLayer = CopperLayer.Bottom;
                }
            }

            rules.CopperThicknessUm = outer;
            rules.MinTrackWidth = minTrack;
            rules.MinDrill = minDrill;
            rules.MinAnnularRing = ring;
            rules.MinHoleToHole = h2h;
            rules.EdgeClearance = edge;
            rules.HoleClearance = holeClr;
            rules.RoutingGrid = grid;
            summary = $"Kart ayarları: {st.CopperLayers} katman, {st.Thickness:0.##} mm, dış bakır {BoardStackup.UmToOz(outer):0.#} oz";
            return true;
        }

        private static string PadConnectionName(ZonePadConnection c) => c switch
        {
            ZonePadConnection.Solid => "Katı (tam bağlantı)",
            ZonePadConnection.None => "Bağlama (boşluk bırak)",
            _ => "Termal rahatlatma"
        };

        private static string IslandName(IslandRemoval r) => r switch
        {
            IslandRemoval.Never => "Hiçbir zaman silme",
            IslandRemoval.BelowArea => "Alan sınırının altındakileri sil",
            _ => "Bağlantısız adaları sil"
        };

        /// <summary>Zone properties. Returns false on cancel; <paramref name="delete"/> is set when the user removed the zone.</summary>
        public static bool ZoneProperties(Window? owner, BoardDocument board, Zone zone, bool isNew, out bool delete)
        {
            bool del = false;
            var z = zone.CloneSettings();
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = isNew ? "Yeni dolgu alanı" : "Dolgu alanı", Style = S("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 10) });
            var nets = board.NetNames().Concat(board.Tracks.Select(t => t.Net)).Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => !string.Equals(n, "GND", StringComparison.OrdinalIgnoreCase)).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (!nets.Contains(z.Net, StringComparer.OrdinalIgnoreCase)) nets.Insert(0, z.Net);
            var netBox = new ComboBox { IsEditable = true, Text = z.Net };
            foreach (var n in nets) netBox.Items.Add(n);
            netBox.Text = z.Net;
            panel.Children.Add(Row("Hat", netBox, 170));
            panel.Children.Add(Row("Katman", Combo(board.CopperLayers(), z.Layer, v => z.Layer = v, l => PcbLayers.DisplayName(l) + "  " + PcbLayers.KiCadName(l)), 170));
            panel.Children.Add(Row("Ad", Text(z.Name, v => z.Name = v.Trim()), 170));
            panel.Children.Add(Row("Öncelik", Number(z.Priority, v => z.Priority = (int)Math.Round(v), 0, 100, true), 170, "Yüksek öncelikli dolgu çakışan alanda bakırını korur"));
            panel.Children.Add(Overline("Bakır"));
            panel.Children.Add(Row("Boşluk (mm, 0 = sınıf)", Number(z.Clearance, v => z.Clearance = v, 0, 10, true), 170));
            panel.Children.Add(Row("En küçük genişlik (mm)", Number(z.MinWidth, v => z.MinWidth = v, 0.05, 5), 170));
            panel.Children.Add(Row("Ped bağlantısı", Combo(new[] { ZonePadConnection.Thermal, ZonePadConnection.Solid, ZonePadConnection.None }, z.PadConnection, v => z.PadConnection = v, PadConnectionName), 170));
            panel.Children.Add(Row("Termal boşluk (mm)", Number(z.ThermalGap, v => z.ThermalGap = v, 0.05, 5), 170));
            panel.Children.Add(Row("Termal kol genişliği (mm)", Number(z.ThermalSpokeWidth, v => z.ThermalSpokeWidth = v, 0.05, 5), 170));
            panel.Children.Add(Row("Adalar", Combo(new[] { IslandRemoval.Always, IslandRemoval.Never, IslandRemoval.BelowArea }, z.IslandRemoval, v => z.IslandRemoval = v, IslandName), 170));
            panel.Children.Add(Row("Ada alan sınırı (mm²)", Number(z.MinIslandArea, v => z.MinIslandArea = v, 0, 1e6, true), 170));
            panel.Children.Add(Caption(z.IsWholeBoard ? "Sınır: kartın tamamı (kenar boşluğu kadar içeride)." : $"Sınır: {z.Outline.Count} köşeli çokgen (seçiliyken köşeleri sürükleyebilirsiniz).", 8));

            Window? dlg = null;
            dlg = MakeDialog(owner, "Dolgu alanı", 420, panel, out var ok, isNew ? "Ekle ve doldur" : "Uygula", isNew ? null : "Sil", () =>
            {
                del = true;
                dlg!.DialogResult = true;
            });
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(netBox.Text)) return;
                dlg.DialogResult = true;
            };
            delete = false;
            if (dlg.ShowDialog() != true) return false;
            delete = del;
            if (del) return true;
            z.Net = nets.FirstOrDefault(n => string.Equals(n, netBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)) ?? netBox.Text.Trim();
            zone.Net = z.Net;
            zone.Layer = z.Layer;
            zone.Name = z.Name;
            zone.Priority = z.Priority;
            zone.Clearance = z.Clearance;
            zone.MinWidth = z.MinWidth;
            zone.PadConnection = z.PadConnection;
            zone.ThermalGap = z.ThermalGap;
            zone.ThermalSpokeWidth = z.ThermalSpokeWidth;
            zone.IslandRemoval = z.IslandRemoval;
            zone.MinIslandArea = z.MinIslandArea;
            return true;
        }

        private static string GraphicLayerName(GraphicLayer l) => l switch
        {
            GraphicLayer.SilkTop => "Üst serigrafi (F.SilkS)",
            GraphicLayer.SilkBottom => "Alt serigrafi (B.SilkS, aynalı)",
            GraphicLayer.FabTop => "Üst montaj (F.Fab)",
            GraphicLayer.FabBottom => "Alt montaj (B.Fab)",
            _ => "Çizim (Dwgs.User)"
        };

        /// <summary>Board text properties; returns false on cancel.</summary>
        public static bool TextProperties(Window? owner, BoardText text, bool isNew, out bool delete)
        {
            bool del = false;
            string value = text.Text;
            double size = text.Size, thickness = text.Thickness;
            int rotation = text.Rotation;
            var layer = text.Layer;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = isNew ? "Yazı ekle" : "Yazı", Style = S("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 10) });
            var box = new TextBox { Text = value };
            panel.Children.Add(Row("Metin", box, 200));
            panel.Children.Add(Row("Katman", Combo(new[] { GraphicLayer.SilkTop, GraphicLayer.SilkBottom, GraphicLayer.FabTop, GraphicLayer.FabBottom, GraphicLayer.Drawings }, layer, v => layer = v, GraphicLayerName), 200));
            panel.Children.Add(Row("Yükseklik (mm)", Number(size, v => size = v, 0.3, 50)));
            panel.Children.Add(Row("Çizgi kalınlığı (mm)", Number(thickness, v => thickness = v, 0.05, 5)));
            panel.Children.Add(Row("Açı", Combo(new[] { 0, 90, 180, 270 }, rotation, v => rotation = v, v => v + "°")));
            panel.Children.Add(Caption("Serigrafi yazısı tek çizgili yazı tipiyle Gerber'e ve KiCad'e aynen aktarılır (Türkçe harfler temel harflerine çevrilir).", 6));
            Window? dlg = null;
            dlg = MakeDialog(owner, "Yazı", 440, panel, out var ok, isNew ? "Ekle" : "Uygula", isNew ? null : "Sil", () =>
            {
                del = true;
                dlg!.DialogResult = true;
            });
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(box.Text)) return;
                dlg.DialogResult = true;
            };
            box.Loaded += (_, _) =>
            {
                box.Focus();
                box.SelectAll();
            };
            delete = false;
            if (dlg.ShowDialog() != true) return false;
            delete = del;
            if (del) return true;
            text.Text = box.Text.Trim();
            text.Layer = layer;
            text.Size = size;
            text.Thickness = thickness;
            text.Rotation = rotation;
            return true;
        }

        /// <summary>Footprint properties of a placed part (position, rotation, side, lock). Returns false on cancel.</summary>
        public static bool ComponentProperties(Window? owner, BoardComponent comp, out (double X, double Y, int Rotation, CopperLayer Side, bool Locked) result)
        {
            double x = comp.X, y = comp.Y;
            int rot = ((comp.Rotation % 360) + 360) % 360;
            var side = comp.Side;
            bool locked = comp.Locked;
            var fp = FootprintLibrary.Get(comp.FootprintId);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = $"{comp.RefDes} · {comp.Value}", Style = S("Text.Title"), FontSize = 18 });
            panel.Children.Add(Caption($"{comp.FootprintId} — {fp?.Description}", 2));
            panel.Children.Add(Caption("Referans ve değer şematikten gelir; footprint'i sağ tık › Footprint değiştir ile seçin.", 2));
            panel.Children.Add(Overline("Konum"));
            panel.Children.Add(Row("X (mm)", Number(x, v => x = v, -1e4, 1e4, true)));
            panel.Children.Add(Row("Y (mm)", Number(y, v => y = v, -1e4, 1e4, true)));
            panel.Children.Add(Row("Açı", Combo(new[] { 0, 90, 180, 270 }, rot, v => rot = v, v => v + "°")));
            panel.Children.Add(Row("Yüz", Combo(new[] { CopperLayer.Top, CopperLayer.Bottom }, side, v => side = v, v => v == CopperLayer.Top ? "Üst (F.Cu)" : "Alt (B.Cu)"), 130));
            panel.Children.Add(Switch("Kilitli (otomatik yerleşim ve taşıma dokunmaz)", locked, v => locked = v));
            var dlg = MakeDialog(owner, "Parça özellikleri", 400, panel, out var ok, "Uygula");
            ok.Click += (_, _) => dlg.DialogResult = true;
            result = default;
            if (dlg.ShowDialog() != true) return false;
            result = (x, y, rot, side, locked);
            return true;
        }

        /// <summary>Footprint picker with search and preview; marks footprints that have every required pad.</summary>
        public static string? PickFootprint(Window? owner, string current, IReadOnlyCollection<string> requiredPads)
        {
            string? picked = null;
            var panel = new StackPanel();
            var search = new TextBox { Tag = "Footprint ara…" };
            var list = new ListBox { Height = 320, Margin = new Thickness(0, 8, 0, 0) };
            var preview = new FootprintPreview { Height = 180, Margin = new Thickness(0, 8, 0, 0) };
            var info = Caption(string.Empty, 4);
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            var left = new StackPanel();
            left.Children.Add(search);
            left.Children.Add(list);
            var right = new StackPanel();
            right.Children.Add(preview);
            right.Children.Add(info);
            Grid.SetColumn(right, 2);
            columns.Children.Add(left);
            columns.Children.Add(right);
            panel.Children.Add(new TextBlock { Text = "Footprint seç", Style = S("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(columns);
            bool Fits(string id)
            {
                var fp = FootprintLibrary.Get(id);
                return fp != null && requiredPads.All(p => fp.FindPad(p) != null);
            }

            void Fill()
            {
                string q = search.Text.Trim();
                list.Items.Clear();
                foreach (var id in FootprintLibrary.AllIds.Where(i => q.Length == 0 || i.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                       (FootprintLibrary.Get(i)?.Description ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                                                     .OrderByDescending(Fits).ThenBy(i => i, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new ListBoxItem { Content = (Fits(id) ? "✓ " : "   ") + id + (FootprintLibrary.UserIds.Contains(id) ? "  (kullanıcı)" : string.Empty), Tag = id };
                    list.Items.Add(item);
                    if (string.Equals(id, current, StringComparison.OrdinalIgnoreCase)) list.SelectedItem = item;
                }
            }

            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is not ListBoxItem li) return;
                string id = (string)li.Tag;
                preview.FootprintId = id;
                var fp = FootprintLibrary.Get(id);
                var missing = requiredPads.Where(p => fp?.FindPad(p) == null).ToList();
                info.Text = $"{fp?.Description}" + Environment.NewLine + $"{fp?.Pads.Count} ped · {(missing.Count == 0 ? "tüm pinler eşleşiyor" : "eksik pedler: " + string.Join(", ", missing))}";
            };
            search.TextChanged += (_, _) => Fill();
            Fill();
            var dlg = MakeDialog(owner, "Footprint seç", 620, panel, out var ok, "Ata");
            ok.Click += (_, _) =>
            {
                if (list.SelectedItem is ListBoxItem li)
                {
                    picked = (string)li.Tag;
                    dlg.DialogResult = true;
                }
            };
            list.MouseDoubleClick += (_, _) =>
            {
                if (list.SelectedItem is ListBoxItem li)
                {
                    picked = (string)li.Tag;
                    dlg.DialogResult = true;
                }
            };
            return dlg.ShowDialog() == true ? picked : null;
        }
    }
}
