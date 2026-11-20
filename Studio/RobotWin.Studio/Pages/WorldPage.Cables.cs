using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Analysis;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// Cables: length check (is the jumper long enough, or pulled tight?), copper + plug-contact
    /// resistance, voltage drop, the "Kablolar ve ölçümler" table and the diagnostics card shown when
    /// a cable is picked in the scene or in the table — measurement, trend and likely cause together.
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>Standard jumper lengths offered in the inspector (m); 0 = automatic length.</summary>
        private static readonly double[] JumperLengths = { 0, 0.10, 0.15, 0.20, 0.30, 0.50 };

        /// <summary>Recent current of every cable (simulated time, A) for the trend plot.</summary>
        private readonly Dictionary<string, List<(double T, double I)>> _cableHistory = new Dictionary<string, List<(double, double)>>();

        private Grid? _diagValues;
        private TextBlock? _diagCauses;
        private LinePlot? _diagTrend;

        /// <summary>
        /// Length state of a cable: straight pin-to-pin distance it has to bridge, the length it
        /// has, and a short verdict. While simulating, the rope's real tension decides "tight".
        /// </summary>
        private (double Span, double Length, string Verdict, bool Bad) CableFit(WorldCable c)
        {
            var world = World;
            double len = c.Length > 0 ? c.Length : Scene.CableLength(c);
            var a = world?.Find(c.A.ObjectId);
            var b = world?.Find(c.B.ObjectId);
            if (a == null || b == null) return (0, len, string.Empty, false);
            double span = (Scene.WorldAnchor(a, c.A.Pin) - Scene.WorldAnchor(b, c.B.Pin)).Length;

            if (_session.State != SimState.Stopped && _session.Physics?.RopeOf(c.Id) is { } rope)
            {
                if (!rope.A.Attached || !rope.B.Attached) return (span, len, "fiş çıktı", true);
                double pull = Math.Max(rope.A.Tension, rope.B.Tension);
                if (pull > 0.5 * rope.PullOutForce) return (span, len, $"gergin, {pull:0.0} N çekiyor", true);
            }

            if (span > len) return (span, len, $"yetmiyor ({(span - len) * 100:0} cm kısa)", true);
            double ratio = span / Math.Max(len, 1e-6);
            if (ratio > 0.95) return (span, len, "gergin", true);
            if (ratio > 0.6) return (span, len, "yeterli", false);
            return (span, len, "bol", false);
        }

        /// <summary>Voltage across the cable (V, magnitude) from the solved circuit: copper and contacts together.</summary>
        private double CableDrop(WorldCable c)
        {
            if (_session.State == SimState.Stopped) return 0;
            double drop = Math.Abs(_session.CableElectrical(c).Drop);
            return drop < 1e-6 ? 0 : drop;   // signal lines: below a microvolt is just solver noise
        }

        /// <summary>Copper and contact resistance (Ω); while simulating the contact part includes a plug being pulled loose.</summary>
        private (double Copper, double Contact) CableResistance(WorldCable c, double length)
        {
            double copper = WorldCable.Resistance(c.Awg, length);
            double total = _session.State != SimState.Stopped ? _session.CableElectrical(c).Resistance : double.NaN;
            double contact = double.IsFinite(total) && total < 1e6 ? Math.Max(0, total - copper) : c.ContactResistance;
            return (copper, contact);
        }

        private ComboBox JumperLengthBox(WorldCable cable)
        {
            var box = new ComboBox { Width = 72, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Kablo boyu (hazır jumper boyları)" };
            bool matched = false;
            foreach (double l in JumperLengths)
            {
                bool sel = l == 0 ? cable.Length <= 0 : Math.Abs(cable.Length - l) < 0.0005;
                matched |= sel;
                box.Items.Add(new ComboBoxItem { Content = l == 0 ? "oto" : (l * 100).ToString("0", CultureInfo.InvariantCulture) + " cm", Tag = l, IsSelected = sel });
            }

            if (!matched) box.Items.Add(new ComboBoxItem { Content = (cable.Length * 100).ToString("0.#", CultureInfo.InvariantCulture) + " cm", Tag = cable.Length, IsSelected = true });
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is ComboBoxItem it && it.Tag is double l && Math.Abs(l - cable.Length) > 0.0001)
                    Edit("Kablo boyu değişti", () => cable.Length = l);
            };
            return box;
        }

        // ================================================================ history

        private void RecordCableHistory()
        {
            var world = World;
            if (world == null || _session.State == SimState.Stopped) return;
            double t = _session.Time;
            foreach (var c in world.Cables)
            {
                if (!_cableHistory.TryGetValue(c.Id, out var h)) _cableHistory[c.Id] = h = new List<(double, double)>();
                if (h.Count > 0 && t < h[^1].T) h.Clear();   // restarted
                h.Add((t, _session.CableState(c).Current));
                int drop = 0;
                while (drop < h.Count && h[drop].T < t - 10) drop++;
                if (drop > 0) h.RemoveRange(0, drop);
            }
        }

        // ================================================================ diagnostics card

        /// <summary>Inspector for a cable picked in the scene or table: what it measures, how that changed and why.</summary>
        private void BuildCableDiagnostics(WorldDocument world, WorldCable c)
        {
            var a = world.Find(c.A.ObjectId);
            var b = world.Find(c.B.ObjectId);
            Inspector.Children.Add(new TextBlock { Text = $"Kablo: {a?.Name} {c.A.Pin} → {b?.Name} {c.B.Pin}", FontSize = 15, TextWrapping = TextWrapping.Wrap });
            Inspector.Children.Add(Note($"AWG {c.Awg:0} · {(c.SolderedA ? "lehimli" : "geçmeli")} / {(c.SolderedB ? "lehimli" : "geçmeli")} uçlar"));

            var sizes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            var awg = new ComboBox { Width = 86, ToolTip = "Kablo kesiti (AWG)" };
            foreach (var g in new[] { 30, 28, 26, 24, 22, 20, 18, 16, 14, 12 }) awg.Items.Add(new ComboBoxItem { Content = "AWG " + g.ToString(CultureInfo.InvariantCulture), Tag = (double)g, IsSelected = Math.Abs(g - c.Awg) < 0.5 });
            awg.SelectionChanged += (_, _) =>
            {
                if (awg.SelectedItem is ComboBoxItem it && Math.Abs((double)it.Tag - c.Awg) > 0.1) Edit("Kablo kesiti değişti", () => c.Awg = (double)it.Tag);
            };
            sizes.Children.Add(awg);
            sizes.Children.Add(JumperLengthBox(c));
            Inspector.Children.Add(sizes);

            Inspector.Children.Add(Caption("Ölçüm"));
            _diagValues = new Grid();
            _diagValues.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _diagValues.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Inspector.Children.Add(_diagValues);
            Inspector.Children.Add(Caption("Akım (son 10 s)"));
            _diagTrend = new LinePlot { Height = 120, XLabel = "t (s)" };
            Inspector.Children.Add(_diagTrend);
            Inspector.Children.Add(Caption("Olası neden ve öneri"));
            _diagCauses = new TextBlock { TextWrapping = TextWrapping.Wrap };
            Inspector.Children.Add(_diagCauses);
            var del = Action("Kabloyu sök", () => Edit("Kablo söküldü", () => world.Cables.Remove(c)));
            del.Margin = new Thickness(0, 12, 0, 0);
            Inspector.Children.Add(del);
            UpdateCableDiagnostics();
        }

        private void UpdateCableDiagnostics()
        {
            var world = World;
            if (_diagValues == null || world == null || Scene.SelectedCable is not { } id) return;
            var c = world.Cables.FirstOrDefault(x => x.Id == id);
            if (c == null) return;
            var (span, len, verdict, bad) = CableFit(c);
            var (copper, contact) = CableResistance(c, len);
            bool live = _session.State != SimState.Stopped;
            var (i, burned, p) = live ? _session.CableState(c) : (0.0, false, 0.0);
            double amp = WorldCable.Ampacity(c.Awg);
            double drop = CableDrop(c);
            double va = live ? _session.Voltage(c.A) : double.NaN, vb = live ? _session.Voltage(c.B) : double.NaN;
            double supply = Math.Max(Math.Abs(double.IsFinite(va) ? va : 0), Math.Abs(double.IsFinite(vb) ? vb : 0));
            var rows = new List<(string Label, string Value, string Note, bool Warn)>
            {
                ("Kablonun boyu", $"{len * 100:0.0} cm", string.Empty, false),
                ("Fişler arası", $"{span * 100:0.0} cm", verdict, bad),
                ("Bakır direnci", SiValue.Format(copper, "Ω"), $"AWG {c.Awg:0}, {len * 100:0} cm", false),
                ("Temas direnci", SiValue.Format(contact, "Ω"), "iki fiş", contact > c.ContactResistance * 1.5),
            };
            if (live)
            {
                double load = Math.Abs(i) / amp;
                rows.Add(("Akım", burned ? "YANDI" : SiValue.Format(Math.Abs(i), "A"), $"yük %{load * 100:0} (en çok {amp:0.#} A)", burned || load > 1));
                rows.Add(("Gerilim düşümü", SiValue.Format(drop, "V"), supply > 0.1 ? $"hattın %{drop / supply * 100:0.0}'i" : string.Empty, supply > 0.5 && drop / supply > 0.03));
                rows.Add(("Isı kaybı", SiValue.Format(p, "W"), string.Empty, false));
                rows.Add(("Uçlarda", $"{SiValue.Format(va, "V")} → {SiValue.Format(vb, "V")}", string.Empty, false));
            }

            _diagValues.Children.Clear();
            _diagValues.RowDefinitions.Clear();
            var muted = (Brush)FindResource("B.TextMuted");
            var text = (Brush)FindResource("B.Text");
            for (int k = 0; k < rows.Count; k++)
            {
                var (label, value, note, warn) = rows[k];
                _diagValues.RowDefinitions.Add(new RowDefinition());
                var l = new TextBlock { Text = label, Foreground = muted, Margin = new Thickness(0, 2, 12, 2) };
                var v = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
                v.Inlines.Add(new System.Windows.Documents.Run(value) { FontWeight = FontWeights.SemiBold, Foreground = warn ? Brushes.OrangeRed : text });
                if (note.Length > 0) v.Inlines.Add(new System.Windows.Documents.Run("  " + note) { Foreground = warn ? Brushes.OrangeRed : muted, FontSize = 11 });
                Grid.SetRow(l, k);
                Grid.SetRow(v, k);
                Grid.SetColumn(v, 1);
                _diagValues.Children.Add(l);
                _diagValues.Children.Add(v);
            }

            if (!live)
            {
                _diagValues.RowDefinitions.Add(new RowDefinition());
                var hint = Note("Akım, düşüm ve kayıp simülasyon çalışırken görünür.");
                Grid.SetRow(hint, rows.Count);
                Grid.SetColumnSpan(hint, 2);
                _diagValues.Children.Add(hint);
            }

            if (_diagTrend != null)
            {
                _diagTrend.Series.Clear();
                if (_cableHistory.TryGetValue(c.Id, out var h) && h.Count > 1)
                    _diagTrend.Series.Add(new PlotSeries { Name = "akım", Unit = "A", X = h.Select(x => x.T).ToArray(), Y = h.Select(x => x.I).ToArray(), Color = LinePlot.Palette[0] });
                _diagTrend.Refresh();
            }

            if (_diagCauses != null) _diagCauses.Text = string.Join("\n", CableCauses(c, span, len, verdict, copper, contact, i, burned, drop, supply, amp));
        }

        /// <summary>Plain-language causes and fixes for what the cable shows right now.</summary>
        private IEnumerable<string> CableCauses(WorldCable c, double span, double len, string verdict, double copper, double contact,
            double i, bool burned, double drop, double supply, double amp)
        {
            bool live = _session.State != SimState.Stopped;
            int n = 0;
            if (burned)
            {
                n++;
                yield return $"• Kablo yandı: akım AWG {c.Awg:0} kesitin taşıyabileceği {amp:0.#} A'yı aştı. AWG {Math.Max(12, c.Awg - 4):0} kullanın ya da yükün akımını sınırlayın (sigorta, akım sınırlı kaynak).";
            }

            if (verdict == "fiş çıktı")
            {
                n++;
                yield return "• Fiş yuvasından çıktı ve devre açıldı: kablo hareket eden parçayla gerildi. Daha uzun kablo seçin ya da kabloyu parçaya sabitleyin.";
            }
            else if (verdict.StartsWith("yetmiyor", StringComparison.Ordinal) || verdict.StartsWith("gergin", StringComparison.Ordinal))
            {
                n++;
                double need = span * 1.15 + 0.03;
                double next = JumperLengths.Where(l => l >= need).DefaultIfEmpty(0).First();
                yield return $"• Kablo kısa: fişler {span * 100:0} cm ayrık, rahat bağlantı için yaklaşık {need * 100:0} cm gerekir"
                    + (next > 0 ? $" — {next * 100:0} cm jumper seçin." : " — daha uzun bir kablo kullanın.");
            }

            if (live && contact > c.ContactResistance * 1.5)
            {
                n++;
                yield return $"• Fiş zorlanıyor: temas direnci {SiValue.Format(contact, "Ω")} (normalde {SiValue.Format(c.ContactResistance, "Ω")}). Fiş çekildiği için gevşiyor; birazdan çıkabilir.";
            }

            if (live && !burned && Math.Abs(i) > amp)
            {
                n++;
                yield return $"• Aşırı yük: {SiValue.Format(Math.Abs(i), "A")} > {amp:0.#} A. Kablo ısınıyor; kesiti büyütün (AWG {Math.Max(12, c.Awg - 2):0}).";
            }
            else if (live && Math.Abs(i) > 0.8 * amp)
            {
                n++;
                yield return "• Sınırda yük: akım kesitin taşıyabileceğinin %80'inden fazla.";
            }

            if (live && supply > 0.5 && drop / supply > 0.03)
            {
                n++;
                yield return $"• Gerilim düşümü yüksek (%{drop / supply * 100:0.0}): yük daha düşük gerilim görüyor. Kabloyu kısaltın ya da kalınlaştırın; "
                    + (contact > copper ? "kaybın çoğu fiş temaslarında — lehimli bağlantı ya da vidalı klemens daha iyi olur." : "kaybın çoğu bakırda.");
            }

            if (live && Math.Abs(i) < 1e-6 && Math.Abs(drop) < 1e-6 && verdict != "fiş çıktı")
            {
                n++;
                yield return "• Akım yok: bu hat bir sinyal hattı, bağlı yük kapalı ya da devre başka bir yerde açık.";
            }

            if (n == 0) yield return live ? "• Sorun görünmüyor." : "• Simülasyonu başlatınca akım, düşüm ve olası sorunlar burada görünür.";
        }

        // ================================================================ table

        private void BuildCableTable()
        {
            CableTable.Children.Clear();
            var world = World;
            if (world == null) return;
            if (world.Cables.Count == 0)
            {
                CableTable.Children.Add(Note("Henüz kablo yok."));
                return;
            }

            bool live = _session.State != SimState.Stopped;
            string[] heads = { "Kablo", "Kesit", "Kablo boyu", "Fişler arası", "Durum", "Direnç (bakır + temas)", "Akım", "Gerilim düşümü", "Kayıp", "Yük" };
            var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
            foreach (var _ in heads) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            int row = 0;
            void Cell(string text, int col, Brush? fg = null, bool head = false, string? cableId = null)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    Margin = new Thickness(0, 1, 18, 1),
                    FontSize = head ? 11 : 12,
                    FontFamily = head ? (FontFamily)FindResource("F.Ui") : (FontFamily)FindResource("F.Mono"),
                    Foreground = fg ?? (Brush)FindResource(head ? "B.TextMuted" : "B.Text"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                if (cableId != null)
                {
                    tb.Cursor = Cursors.Hand;
                    tb.MouseLeftButtonDown += (_, _) => Scene.SelectCable(cableId);
                }

                Grid.SetRow(tb, row);
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }

            grid.RowDefinitions.Add(new RowDefinition());
            for (int k = 0; k < heads.Length; k++) Cell(heads[k], k, head: true);
            foreach (var c in world.Cables)
            {
                row++;
                grid.RowDefinitions.Add(new RowDefinition());
                var a = world.Find(c.A.ObjectId);
                var b = world.Find(c.B.ObjectId);
                var (span, len, verdict, bad) = CableFit(c);
                var (copper, contact) = CableResistance(c, len);
                Brush? warn = bad ? Brushes.OrangeRed : null;
                if (c.Id == Scene.SelectedCable)
                {
                    var hl = new Border { Background = (Brush)FindResource("B.AccentSoft") };
                    Grid.SetRow(hl, row);
                    Grid.SetColumnSpan(hl, heads.Length);
                    grid.Children.Add(hl);
                }

                Cell($"{a?.Name} {c.A.Pin} → {b?.Name} {c.B.Pin}", 0, null, false, c.Id);
                Cell($"AWG {c.Awg:0}", 1, null, false, c.Id);
                Cell($"{len * 100:0.0} cm", 2, null, false, c.Id);
                Cell($"{span * 100:0.0} cm", 3, null, false, c.Id);
                Cell(verdict, 4, warn, false, c.Id);
                Cell($"{SiValue.Format(copper + contact, "Ω")} ({copper * 1000:0} + {contact * 1000:0} mΩ)", 5, null, false, c.Id);
                if (live)
                {
                    var (i, burned, p) = _session.CableState(c);
                    double amp = WorldCable.Ampacity(c.Awg);
                    double load = Math.Abs(i) / amp;
                    if (burned)
                    {
                        Cell("YANDI", 6, Brushes.OrangeRed, false, c.Id);
                        continue;
                    }

                    Cell(SiValue.Format(Math.Abs(i), "A"), 6, null, false, c.Id);
                    Cell(SiValue.Format(CableDrop(c), "V"), 7, null, false, c.Id);
                    Cell(SiValue.Format(p, "W"), 8, null, false, c.Id);
                    Cell($"%{load * 100:0}", 9, load > 1 ? Brushes.OrangeRed : load > 0.8 ? Brushes.Orange : null, false, c.Id);
                }
            }

            CableTable.Children.Add(grid);
            CableTable.Children.Add(Note("Satıra ya da sahnedeki kabloya tıklayınca sağda ölçümü, değişimi ve olası nedeni görünür. Geçmeli (jumper) uçlar fiş başına ≈25 mΩ temas direnci ekler; PCB'de bakır yolla yapılan bağlantılarda bu yoktur."));
        }
    }
}
