using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [gas] Pressure vessels on the world page: palette entries (PET bottle for a water rocket, an air
    /// tank, a solid-propellant motor, rocket fins) and the inspector's vessel section — the contents
    /// (gas, pressure, water, propellant grain), the nozzle, the valve and ignition, the burst pressure
    /// the wall can hold, and a live readout of pressure, temperature, thrust and what the vessel is doing.
    /// </summary>
    public partial class WorldPage
    {
        private TextBlock? _gasLive;

        private void GasPalette()
        {
            Palette.Children.Add(Caption("Basınçlı kaplar ve roketler (gaz fiziği)"));
            Palette.Children.Add(Note("Kabın içindeki gaz ideal gaz yasasıyla (sıcak yanma ürünlerinde Noble–Abel gerçek gaz düzeltmesi), lüleden çıkış izentropik akışla (boğazda kısılma, aşırı genişlemede Schmucker ayrılması), su Bernoulli ile hesaplanır. Katı yakıtı ateşleyicinin sıcak gazı ısıtır, yüzey tutuşma sıcaklığına çıkınca yanar ve alev yayılır; yanma hızı basınca ve kanal akışına (aşındırıcı yanma) bağlıdır. İtki, kütle kaybı, çeper ve lüle ısınması, patlama aynı fizikten çıkar."));
            Palette.Children.Add(Row("PET şişe 2 L (su roketi)", "0.6 L su, 4 bar hava, 21.5 mm ağız, 1 s sonra rampadan bırakılır", () => AddBody("cylinder", 0.105, 0.105, 0.30, o =>
            {
                o.Material = "PET";
                o.MassKg = 0.05;
                o.Name = World!.UniqueName("PET şişe");
                SetVessel(o, ("volume", "2.0"), ("wall", "0.3"), ("pressure", "4"), ("water", "0.6"), ("nozzle", "21.5"), ("nozzle.axis", "-z"), ("valve.at", "1"));
            })));
            Palette.Children.Add(Row("Hava tankı 1 L (çelik)", "6 bar, 2 mm lüle kapalı (vanayı açın)", () => AddBody("cylinder", 0.10, 0.10, 0.20, o =>
            {
                o.Material = "ÇELİK";
                o.Name = World!.UniqueName("Hava tankı");
                SetVessel(o, ("volume", "1.0"), ("wall", "2"), ("pressure", "6"), ("nozzle", "2"), ("nozzle.axis", "+z"), ("valve.at", "1"));
            })));
            Palette.Children.Add(Row("Katı yakıtlı motor (KNSB, Ø32 mm)", "2 BATES tanesi Ø25/8 × 60 mm, 6→14 mm lüle; e-match ya da zamanla ateşlenir", () => AddBody("cylinder", 0.032, 0.032, 0.16, o =>
            {
                o.Material = "ALÜMİNYUM";
                o.MassKg = 0.12;
                o.Name = World!.UniqueName("KNSB motor");
                SetVessel(o, ("wall", "1.5"), ("grain", "KNSB"), ("grain.diameter", "25"), ("grain.core", "8"), ("grain.length", "60"), ("grain.segments", "2"),
                    ("nozzle", "6"), ("nozzle.exit", "14"), ("nozzle.axis", "-z"), ("ignite.at", "1"));
            })));
            Palette.Children.Add(Row("Roket kanatçığı", "100 mm veter, 70 mm açıklık, ok açılı, balsa (tek panel)", () => AddBody("wing", 0.10, 0.07, 0.003, o =>
            {
                o.Material = "BALSA";
                o.MassKg = 0.012;
                o.Name = World!.UniqueName("Kanatçık");
                o.Parameters["symmetric"] = "false";
                o.Parameters["airfoil"] = "flat";
                o.Parameters["tipchord"] = "0.06";
                o.Parameters["sweep"] = "25";
                o.Pose.Pitch = -90;
            })));
        }

        private static void SetVessel(WorldObject o, params (string Key, string Value)[] values)
        {
            o.Parameters["vessel"] = "true";
            foreach (var (k, v) in values) o.Parameters[k] = v;
        }

        private void GasSection(WorldDocument world, WorldObject o)
        {
            _gasLive = null;
            if (o.Kind != WorldObjectKind.Body || o.Shape is not ("cylinder" or "box" or "sphere")) return;
            bool vessel = WorldPhysics.IsVessel(o);
            Inspector.Children.Add(Caption("Basınçlı kap (gaz fiziği)"));
            var on = new CheckBox { Content = "İçinde gaz tutan kapalı kap", IsChecked = vessel, Foreground = (Brush)FindResource("B.Text"), Margin = new Thickness(0, 0, 0, 6) };
            on.Click += (_, _) => Edit(on.IsChecked == true ? "Basınçlı kap" : "Basınçlı kap kaldırıldı", () =>
            {
                if (on.IsChecked == true) o.Parameters["vessel"] = "true";
                else o.Parameters.Remove("vessel");
            });
            Inspector.Children.Add(on);
            if (!vessel) return;

            _gasLive = LiveGasText();
            Inspector.Children.Add(_gasLive);
            UpdateGasLive();

            Inspector.Children.Add(Choice(o, "gas", "Gaz", new[] { ("hava", "Hava"), ("co2", "CO₂"), ("azot", "Azot"), ("helyum", "Helyum") }, "hava"));
            Inspector.Children.Add(Field(o, "pressure", "Basınç (bar, gösterge)", "0"));
            Inspector.Children.Add(Field(o, "volume", "İç hacim (L, boş = şekilden)", ""));
            Inspector.Children.Add(Field(o, "wall", "Duvar (mm)", "2"));
            Inspector.Children.Add(Field(o, "water", "Su (L)", "0"));
            Inspector.Children.Add(Caption("Lüle ve vana", 8));
            Inspector.Children.Add(Field(o, "nozzle", "Boğaz çapı (mm, 0 = kapalı)", "0"));
            Inspector.Children.Add(Field(o, "nozzle.exit", "Çıkış çapı (mm, genişleyen lüle)", ""));
            Inspector.Children.Add(Choice(o, "nozzle.axis", "Jetin çıktığı yüz", new[] { ("-z", "alt (−z)"), ("+z", "üst (+z)"), ("+x", "ön (+x)"), ("-x", "arka (−x)"), ("+y", "sol (+y)"), ("-y", "sağ (−y)") }, "-z"));
            Inspector.Children.Add(Field(o, "valve.at", "Vana açılır (s, rampa bırakır)", "0"));
            Inspector.Children.Add(Caption("Katı yakıt", 8));
            Inspector.Children.Add(Choice(o, "grain", "Yakıt", new[] { ("", "yok"), ("KNSB", "KNSB (şekerli)"), ("KNDX", "KNDX"), ("APCP", "APCP (kompozit)") }, ""));
            if (o.Parameters.TryGetValue("grain", out var grain) && grain.Length > 0)
            {
                Inspector.Children.Add(Field(o, "grain.diameter", "Tane dış çapı (mm)", ""));
                Inspector.Children.Add(Field(o, "grain.core", "Kor (delik) çapı (mm)", ""));
                Inspector.Children.Add(Field(o, "grain.length", "Tane boyu (mm)", ""));
                Inspector.Children.Add(Field(o, "grain.segments", "Tane sayısı (BATES)", "1"));
                Inspector.Children.Add(Field(o, "ignite.at", "Ateşleme zamanı (s, boş = e-match)", ""));
                var matches = world.Objects.Where(x => string.Equals(x.PartId, RobotTwin.CoreSim.Design.AvionicsParts.EMatchId, StringComparison.OrdinalIgnoreCase))
                    .Select(x => (x.Id, x.Name)).Prepend((string.Empty, "motora bağlı olan")).ToArray();
                Inspector.Children.Add(Choice(o, "igniter", "Ateşleme (e-kibrit)", matches, string.Empty));
                Inspector.Children.Add(Field(o, "igniter.charge", "Ateşleyici barutu (g, e-match başına ek)", "0.5"));
                Inspector.Children.Add(Field(o, "igniter.time", "Ateşleyici yanma süresi (ms)", "20"));
                Inspector.Children.Add(Note("E-match (elektrikli ateşleyici) motora mekanik olarak bağlıysa, devreden ateşlendiği an ateşleyici yanar. Yakıt hemen yanmaz: ateşleyicinin sıcak gazı yüzeyi ısıtır, yüzey tutuşma sıcaklığına (KNSB ≈ 317 °C) çıkan bölge yanar ve alev lüleye doğru yayılır. Yalnız e-match başı (≈ 0.03 g) şekerli yakıtı genellikle tutuşturamaz."));
            }

            // What the wall can hold: thin-wall hoop stress at the material's strength.
            var mat = StructuralMaterial.For(o.Material);
            double wall = (double.TryParse(o.Parameters.TryGetValue("wall", out var ws) ? ws : "2", NumberStyles.Float, CultureInfo.InvariantCulture, out var wv) ? wv : 2) / 1000;
            double r = (o.Shape == "box" ? Math.Min(o.SizeX, o.SizeY) : o.SizeX) / 2 - wall;
            double burst = r > 0 ? mat.Yield * wall / r * (o.Shape == "sphere" ? 2 : 1) / 1e5 : 0;
            Inspector.Children.Add(Field(o, "burst", "Patlama basıncı (bar, boş = malzemeden)", ""));
            Inspector.Children.Add(Note($"{mat.Name}, {wall * 1000:0.0#} mm duvar: çeper gerilmesi σ = p·r/t malzemenin dayanımına ({mat.Yield / 1e6:0} MPa) ≈ {burst:0.0} bar'da ulaşır."));
        }

        private TextBlock LiveGasText() => new TextBlock { FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(0, 0, 0, 8) };

        private void UpdateGasLive()
        {
            if (_gasLive == null) return;
            var o = World?.Find(Scene.Selected);
            var physics = _session.State == SimState.Stopped ? null : _session.Physics;
            var g = o != null ? physics?.GasOf(o.Id) : null;
            if (g == null)
            {
                _gasLive.Text = "Simülasyon çalışırken burada basınç, sıcaklık, itki ve kabın durumu görünür.";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Durum   : {g.Phase}");
            sb.AppendLine($"Basınç  : {g.GaugePressure / 1e5:0.00} bar (gösterge) · {g.TemperatureK - 273.15:0} °C");
            sb.AppendLine($"Gaz     : {g.GasMass * 1000:0.0} g" + (g.WaterLitres > 0 ? $" · su {g.WaterLitres:0.000} L" : string.Empty) + (g.GrainLeft > 0 || g.Burning ? $" · yakıt %{g.GrainLeft * 100:0}" : string.Empty));
            if (g.SurfaceK > 0) sb.AppendLine($"Yakıt   : yüzey {g.SurfaceK - 273.15:0} °C · yanan yüzey %{g.LitFraction * 100:0}" + (g.IgniterBurning ? " · ateşleyici yanıyor" : string.Empty));
            sb.Append($"İtki    : {g.Thrust:0.0} N · {g.MassFlow * 1000:0.0} g/s · çıkış {g.ExitVelocity:0} m/s{(g.Choked ? " (boğazda ses hızı)" : string.Empty)}");
            _gasLive.Text = sb.ToString();
        }

        /// <summary>A numeric parameter in the object's parameter table (empty = the model's default).</summary>
        private FrameworkElement Field(WorldObject o, string key, string label, string fallback)
        {
            var box = new TextBox { Text = o.Parameters.TryGetValue(key, out var v) ? v : fallback };
            box.LostFocus += (_, _) =>
            {
                string text = box.Text.Trim().Replace(',', '.');
                string current = o.Parameters.TryGetValue(key, out var c) ? c : fallback;
                if (text == current) return;
                if (text.Length > 0 && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return;
                Edit(label, () =>
                {
                    if (text.Length == 0) o.Parameters.Remove(key);
                    else o.Parameters[key] = text;
                });
            };
            return Labeled(label, box);
        }

        private FrameworkElement Choice(WorldObject o, string key, string label, (string Value, string Text)[] options, string fallback)
        {
            string current = o.Parameters.TryGetValue(key, out var v) ? v : fallback;
            var combo = new ComboBox();
            foreach (var (value, text) in options) combo.Items.Add(new ComboBoxItem { Content = text, Tag = value });
            combo.SelectedIndex = Math.Max(0, Array.FindIndex(options, x => string.Equals(x.Value, current, StringComparison.OrdinalIgnoreCase)));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not string value || value == current) return;
                Edit(label, () =>
                {
                    if (value.Length == 0) o.Parameters.Remove(key);
                    else o.Parameters[key] = value;
                });
            };
            return Labeled(label, combo);
        }
    }
}
