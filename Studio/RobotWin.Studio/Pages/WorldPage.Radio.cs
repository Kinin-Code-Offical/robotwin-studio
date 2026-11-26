using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Radio;
using RobotWin.Studio.Input;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [radio] Wireless / USB parts of the world page: inspector sections for controllers
    /// (pairing, input source, virtual pad, range), the PC/USB cable (plug / unplug), IR
    /// remotes (keypad) and ESP32 radios, a palette section and the "RF" dock panel.
    /// WorldPage.xaml.cs only calls InitRadio, RadioSection, RadioPalette, UpdateRadioLive
    /// and ShowRfPanel.
    /// </summary>
    public partial class WorldPage
    {
        private TextBlock? _radioLive;
        private static double _pathLossExponent = 3.0;
        private static double _fadingSigma = 4.0;

        private void InitRadio()
        {
            _session.GamepadPaired += (padId, host) => Dispatcher.BeginInvoke(new Action(() =>
            {
                // Remember the bond in the document (like the controller's NVS): next run it reconnects by itself.
                var o = W.ActiveWorld?.Find(padId);
                string hostObject = host.Id.Split('/')[0];
                if (o != null) o.Parameters["pairedWith"] = hostObject;
                W.Status = $"Eşleştirildi: {o?.Name ?? padId} ⇄ {host.Name} ({host.BluetoothMacText})";
                if (Scene.Selected == padId) BuildInspector();
            }));
        }

        // ================================================================ palette

        private void RadioPalette()
        {
            Palette.Children.Add(Caption("Kablosuz ve USB"));
            foreach (var p in Lib.Presets.Where(p => p.Category == WirelessParts.Category))
            {
                var id = p.Id;
                Palette.Children.Add(Row(p.Name, p.Description, () => AddComponent(id)));
            }
        }

        // ================================================================ inspector

        private void RadioSection(WorldDocument world, WorldObject o)
        {
            _radioLive = null;
            switch (o.Kind == WorldObjectKind.Component ? o.PartId : null)
            {
                case WirelessParts.GamepadId:
                    GamepadSection(world, o);
                    break;
                case WirelessParts.UsbHostId:
                    UsbSection(world, o);
                    break;
                case WirelessParts.IrRemoteId:
                    IrRemoteSection(o);
                    break;
                default:
                    if (IsEsp32Object(world, o)) Esp32RadioSection(o);
                    break;
            }

            if (WorldUsb.HostFor(world, o.Id) is { } host)
            {
                Inspector.Children.Add(Note($"USB: {host.Name} {(WorldUsb.IsPlugged(host) ? "takılı — 5 V VBUS ve seri monitör bağlı" : "çıkarılmış — USB'den güç ve seri bağlantı yok")}"));
            }
        }

        private bool IsEsp32Object(WorldDocument world, WorldObject o)
        {
            if (o.Kind == WorldObjectKind.Component) return o.PartId != null && o.PartId.StartsWith("esp32", StringComparison.OrdinalIgnoreCase);
            if (o.Kind != WorldObjectKind.Board) return false;
            var sch = world.FindDesign(o.DesignId)?.Package.Schematic;
            return sch != null && sch.Parts.Any(p => p.PartId.StartsWith("esp32", StringComparison.OrdinalIgnoreCase));
        }

        private TextBlock LiveText()
        {
            var t = new TextBlock { FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("B.TextSecondary"), Margin = new Thickness(0, 0, 0, 8) };
            _radioLive = t;
            return t;
        }

        private void GamepadSection(WorldDocument world, WorldObject o)
        {
            Inspector.Children.Add(Caption("Kablosuz kumanda"));
            Inspector.Children.Add(LiveText());
            bool running = _session.State != SimState.Stopped;
            Inspector.Children.Add(Action("Eşleştirme modu (PS + Create basılı)", () =>
            {
                if (!_session.StartPairing(o.Id))
                {
                    W.Status = "Önce dünyayı çalıştırın: eşleşme, ESP32'deki Bluepad32 çizimi çalışırken olur.";
                    return;
                }

                W.Status = $"{o.Name} eşleştirme modunda: BP32.setup() çağıran ESP32 kumandayı bulunca eşleşir.";
            }, "Kumanda keşfedilebilir olur. Bluepad32 kullanan (BP32.setup + BP32.update) ve menzil içindeki ESP32 ile ≈1.2 s içinde eşleşir; eşleşme bir sonraki çalıştırma için saklanır.", primary: true));
            Inspector.Children.Add(Action("Sanal kumandayı aç (fare / klavye)", () => VirtualGamepadWindow.ShowPad(Window.GetWindow(this)),
                "PS5 düzeninde ekran kumandası. Giriş kaynağı 'sanal' ya da bilgisayarda gerçek kumanda yokken 'otomatik' olduğunda kullanılır."));
            bool on = !o.Parameters.TryGetValue("power", out var pw) || pw != "false";
            Inspector.Children.Add(Action(on ? "Kumandayı kapat" : "Kumandayı aç", () =>
            {
                bool now = !(!o.Parameters.TryGetValue("power", out var p2) || p2 != "false");
                o.Parameters["power"] = now ? "true" : "false";
                _session.SetGamepadPower(o.Id, now);
                BuildInspector();
            }));
            if (o.Parameters.TryGetValue("pairedWith", out var paired) && !string.IsNullOrWhiteSpace(paired))
            {
                Inspector.Children.Add(Note($"Eşli: {world.Find(paired)?.Name ?? paired} (açılınca kendiliğinden bağlanır)"));
                Inspector.Children.Add(Action("Eşleşmeyi unut", () =>
                {
                    o.Parameters.Remove("pairedWith");
                    if (_session.GamepadNodeOf(o.Id) is { } node) node.BondedHost = null;
                    BuildInspector();
                }));

                if (world.Find(paired) is { } host)
                {
                    double d = Distance(o, host);
                    var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
                    var label = new TextBlock { Text = $"ESP32'ye uzaklık: {d:0.00} m", Foreground = (Brush)FindResource("B.TextSecondary") };
                    var slider = new Slider { Minimum = Math.Log10(0.2), Maximum = Math.Log10(200), Value = Math.Log10(Math.Max(0.2, d)), IsEnabled = running, ToolTip = "Simülasyon sırasında kumandayı ESP32'den uzaklaştırın: RSSI düşer, menzil dışında ≈2 s sonra bağlantı kopar." };
                    slider.ValueChanged += (_, _) =>
                    {
                        double want = Math.Pow(10, slider.Value);
                        MoveAway(o, host, want);
                        label.Text = $"ESP32'ye uzaklık: {want:0.00} m";
                        Scene.RefreshPose(o.Id);
                    };
                    sp.Children.Add(label);
                    var distRow = new DockPanel();
                    var distBox = RobotWin.Studio.Theme.SliderInput.Create(slider, "0.00", " m", v => Math.Pow(10, v), x => Math.Log10(Math.Max(1e-6, x)), width: 76);   // [slider-input]
                    distBox.IsEnabled = running;
                    DockPanel.SetDock(distBox, Dock.Right);
                    distRow.Children.Add(distBox);
                    slider.VerticalAlignment = VerticalAlignment.Center;
                    distRow.Children.Add(slider);
                    sp.Children.Add(distRow);
                    if (!running) sp.Children.Add(Note("Kaydırıcı simülasyon çalışırken etkin."));
                    Inspector.Children.Add(sp);
                }
            }

            var real = GamepadInputs.Connected;
            Inspector.Children.Add(Note(real.Count == 0
                ? "Bilgisayarda gerçek kumanda bulunamadı (DualSense / DualShock 4 USB veya Bluetooth, Xbox XInput). 'Otomatik' kaynak sanal kumandayı kullanır."
                : "Bilgisayardaki kumandalar: " + string.Join(", ", real.Select(r => r.Name))));
            Inspector.Children.Add(Action("Kumandaları yeniden tara", () =>
            {
                GamepadInputs.Rescan(true);
                BuildInspector();
            }));
            UpdateRadioLive();
        }

        private static double Distance(WorldObject a, WorldObject b) => (a.Pose.Position - b.Pose.Position).Length;

        private static void MoveAway(WorldObject pad, WorldObject host, double distance)
        {
            double dx = pad.Pose.X - host.Pose.X, dy = pad.Pose.Y - host.Pose.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
            {
                dx = 1;
                dy = 0;
                len = 1;
            }

            double dz = pad.Pose.Z - host.Pose.Z;
            double horizontal = Math.Sqrt(Math.Max(0, distance * distance - dz * dz));
            pad.Pose.X = host.Pose.X + dx / len * horizontal;
            pad.Pose.Y = host.Pose.Y + dy / len * horizontal;
        }

        private void UsbSection(WorldDocument world, WorldObject o)
        {
            Inspector.Children.Add(Caption("USB kablosu"));
            var boards = world.Objects.Where(b => b.Id != o.Id && IsUsbBoard(world, b)).ToList();
            var combo = new ComboBox();
            combo.Items.Add(new ComboBoxItem { Content = "(takılı değil)", Tag = string.Empty });
            o.Parameters.TryGetValue("target", out var target);
            foreach (var b in boards) combo.Items.Add(new ComboBoxItem { Content = b.Name, Tag = b.Id, IsSelected = b.Id == target });
            if (combo.SelectedItem == null) combo.SelectedIndex = 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem it && (string)it.Tag != (target ?? string.Empty))
                {
                    Edit("USB kablosu bağlandı", () =>
                    {
                        if (((string)it.Tag).Length == 0) o.Parameters.Remove("target");
                        else o.Parameters["target"] = (string)it.Tag;
                    });
                }
            };
            Inspector.Children.Add(Labeled("Kart", combo));
            bool plugged = WorldUsb.IsPlugged(o);
            Inspector.Children.Add(Action(plugged ? "USB'yi çıkar" : "USB'yi tak", () =>
            {
                bool now = !WorldUsb.IsPlugged(o);
                if (_session.State != SimState.Stopped)
                {
                    _session.SetUsbPlugged(o, now);
                    Scene.RebuildCables();
                    BuildInspector();
                }
                else
                {
                    Edit(now ? "USB takıldı" : "USB çıkarıldı", () => o.Parameters["plugged"] = now ? "true" : "false");
                }
            }, "Takılıyken kart USB'den 5 V alır ve seri monitöre bağlanır; çıkarınca ikisi de kesilir (simülasyon sırasında da).", primary: true));
            Inspector.Children.Add(Note("USB kablosu takılan kartlarda 'USB bağlı' parametresi yerine kablo geçerlidir. Kablosu olmayan kartlar eski davranışı korur (USB'den beslenmiş sayılır)."));
        }

        private bool IsUsbBoard(WorldDocument world, WorldObject b)
        {
            if (b.Kind == WorldObjectKind.Component)
            {
                var def = b.PartId != null ? Lib.Find(b.PartId) : null;
                return def != null && def.Symbol == SymbolKind.Board && def.Parameters.Any(p => p.Key == "usb");
            }

            if (b.Kind != WorldObjectKind.Board) return false;
            var sch = world.FindDesign(b.DesignId)?.Package.Schematic;
            return sch != null && sch.Parts.Any(p => Lib.Find(p.PartId) is { Symbol: SymbolKind.Board } d && d.Parameters.Any(x => x.Key == "usb"));
        }

        private void IrRemoteSection(WorldObject o)
        {
            Inspector.Children.Add(Caption("IR kumanda tuşları (NEC)"));
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (label, cmd) in WorldRadio.RemoteKeys)
            {
                var b = new Button { Content = label, Margin = new Thickness(2), Padding = new Thickness(4, 3, 4, 3), ToolTip = $"NEC komut 0x{cmd:X2}" };
                byte c = cmd;
                b.Click += (_, _) =>
                {
                    if (!_session.SendIr(o.Id, c)) W.Status = "IR göndermek için dünyayı çalıştırın.";
                };
                grid.Children.Add(b);
            }

            Inspector.Children.Add(grid);
            Inspector.Children.Add(Note("Kumandanın ön ucu nesnenin +X yönüdür; TSOP38238 alıcısının merceği de +X'e bakar. Arada engel olmamalı, açı ±30°'yi aşmamalı."));
        }

        private void Esp32RadioSection(WorldObject o)
        {
            Inspector.Children.Add(Caption("Radyo (Wi-Fi / BLE / Bluetooth)"));
            Inspector.Children.Add(LiveText());
            if (o.Kind == WorldObjectKind.Component)
            {
                Inspector.Children.Add(Action("Örnek kodu yükle: kumandalı araba (Bluepad32)", () =>
                    Edit("Örnek kod yüklendi", () => o.Parameters["firmware"] = "builtin:esp32-gamepad-car.ino"),
                    "ESP32'ye esp32-gamepad-car.ino yüklenir: eşleşen kumandanın R2/L2 ve sol çubuğu ile DRV8833 üzerinden iki motor sürülür."));
            }

            UpdateRadioLive();
        }

        // ================================================================ live text + RF panel

        private void UpdateRadioLive()
        {
            var radio = _session.Radio;
            if (radio != null)
            {
                radio.Propagation.PathLossExponent = _pathLossExponent;
                radio.Propagation.ShadowingSigmaDb = _fadingSigma;
            }

            if (_radioLive != null)
            {
                var o = World?.Find(Scene.Selected);
                if (o == null || radio == null)
                {
                    _radioLive.Text = "Simülasyon çalışırken burada bağlantı durumu, RSSI ve paket sayıları görünür.";
                }
                else if (_session.GamepadNodeOf(o.Id) is { } pad)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Durum   : " + pad.Status);
                    sb.AppendLine("Adres   : " + pad.MacText + " (" + pad.ModelName + ")");
                    if (pad.Host != null) sb.AppendLine($"RSSI    : {radio.Rssi(pad, pad.Host, RadioTech.Bluetooth, radio.Time):0} dBm · {pad.Stats.TxRate(radio.Time):0} rapor/s");
                    sb.AppendLine("Kaynak  : " + (_session.GamepadSourceName(o.Id) ?? "—"));
                    sb.Append("Giriş   : " + pad.Input);
                    _radioLive.Text = sb.ToString();
                }
                else if (_session.Esp32NodeOf(o.Id) is { } esp)
                {
                    _radioLive.Text = $"Durum   : {esp.Status}\nWi-Fi MAC: {esp.MacText}\nBT MAC  : {esp.BluetoothMacText}\nPaket   : TX {esp.Stats.TxRate(radio.Time):0}/s · RX {esp.Stats.RxRate(radio.Time):0}/s · kayıp {esp.Stats.LostPackets}";
                }
            }

            if (DockShowing("rf", DockRf)) UpdateRfPanel();   // [dock] also when it sits on a side or in its own window
        }

        private DateTime _lastRf = DateTime.MinValue;

        private void UpdateRfPanel()
        {
            if ((DateTime.Now - _lastRf).TotalMilliseconds < 250 && RfTable.Children.Count > 0) return;
            _lastRf = DateTime.Now;
            RfTable.Children.Clear();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var env = new ComboBox { Width = 230, Margin = new Thickness(0, 0, 8, 0), ToolTip = "2.4 GHz yol kaybı üssü (log-mesafe modeli, 1 m'ye kadar serbest uzay)" };
            foreach (var (name, n) in new[] { ("Açık alan (n = 2.0)", 2.0), ("Ev içi (n = 2.7)", 2.7), ("Ofis (n = 3.0)", 3.0), ("Yoğun engelli (n = 3.5)", 3.5) })
            {
                env.Items.Add(new ComboBoxItem { Content = name, Tag = n, IsSelected = Math.Abs(n - _pathLossExponent) < 1e-6 });
            }

            env.SelectionChanged += (_, _) =>
            {
                if (env.SelectedItem is ComboBoxItem it) _pathLossExponent = (double)it.Tag;
                UpdateRadioLive();
            };
            bar.Children.Add(env);
            var fading = new CheckBox { Foreground = (Brush)FindResource("B.TextSecondary"), Content = "Sönümleme (log-normal, σ = 4 dB)", IsChecked = _fadingSigma > 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            fading.Click += (_, _) => _fadingSigma = fading.IsChecked == true ? 4.0 : 0.0;
            bar.Children.Add(fading);
            var rings = new CheckBox { Foreground = (Brush)FindResource("B.TextSecondary"), Content = "Dalgalar", IsChecked = Scene.RadioLayer?.ShowRings ?? true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            rings.Click += (_, _) =>
            {
                if (Scene.RadioLayer != null) Scene.RadioLayer.ShowRings = rings.IsChecked == true;
            };
            bar.Children.Add(rings);
            var links = new CheckBox { Foreground = (Brush)FindResource("B.TextSecondary"), Content = "Bağlantı çizgileri", IsChecked = Scene.RadioLayer?.ShowLinks ?? true, VerticalAlignment = VerticalAlignment.Center };
            links.Click += (_, _) =>
            {
                if (Scene.RadioLayer != null) Scene.RadioLayer.ShowLinks = links.IsChecked == true;
            };
            bar.Children.Add(links);
            RfTable.Children.Add(bar);

            var radio = _session.Radio;
            if (radio == null)
            {
                RfTable.Children.Add(Note("Simülasyon çalışırken burada dünyadaki bütün radyolar (ESP32 Wi-Fi/BLE, kumandalar, modemler, beacon'lar), RSSI, kanal ve paket/s görünür."));
                return;
            }

            double t = radio.Time;
            RfTable.Children.Add(MonoLine($"{"Cihaz",-22} {"Tür",-11} {"Adres",-18} {"Kanal",5} {"TX/s",6} {"RX/s",6} {"Kayıp",6}  Durum", true));
            foreach (var n in radio.Nodes)
            {
                string kind = n.Kind switch { RadioNodeKind.Esp32 => "ESP32", RadioNodeKind.Gamepad => "Kumanda", RadioNodeKind.AccessPoint => "Wi-Fi AP", _ => "BLE beacon" };
                string addr = n.Kind == RadioNodeKind.Gamepad || n.Kind == RadioNodeKind.Beacon ? n.BluetoothMacText : n.MacText;
                RfTable.Children.Add(MonoLine($"{Trim(n.Name, 22),-22} {kind,-11} {addr,-18} {n.Channel,5} {n.Stats.TxRate(t),6:0} {n.Stats.RxRate(t),6:0} {n.Stats.LostPackets,6}  {n.Status}"));
            }

            var list = radio.Links();
            if (list.Count > 0)
            {
                RfTable.Children.Add(MonoLine(" ", false));
                RfTable.Children.Add(MonoLine("Bağlantılar", true));
                foreach (var l in list)
                {
                    double d = (l.A.Position() - l.B.Position()).Length;
                    var line = MonoLine($"{Trim(l.A.Name, 20)} ⇄ {Trim(l.B.Name, 20)}  {l.Label,-14} {l.RssiDbm,6:0} dBm  {d,6:0.00} m");
                    line.Foreground = new SolidColorBrush(RobotWin.Studio.World.RadioOverlay.RssiColor(l.RssiDbm));
                    RfTable.Children.Add(line);
                }
            }

            if (radio.Ir.Emitters.Count + radio.Ir.Receivers.Count > 0)
            {
                RfTable.Children.Add(MonoLine(" ", false));
                RfTable.Children.Add(MonoLine($"Kızılötesi (38 kHz): {radio.Ir.Emitters.Count} verici, {radio.Ir.Receivers.Count} alıcı", true));
                foreach (var r in radio.Ir.Receivers)
                {
                    var seen = radio.Ir.Emitters.Where(e => radio.Ir.LineOfSight(e, r)).Select(e => e.Name).ToList();
                    RfTable.Children.Add(MonoLine($"{Trim(r.Name, 22),-22} görüş hattında: {(seen.Count == 0 ? "—" : string.Join(", ", seen))}{(r.Active ? "  · taşıyıcı var" : string.Empty)}"));
                }
            }
        }

        private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        private TextBlock MonoLine(string text, bool header = false) => new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)FindResource("F.Mono"),
            FontSize = 12,
            Foreground = header ? (Brush)FindResource("B.TextSecondary") : (Brush)FindResource("B.Text"),
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal
        };
    }
}
