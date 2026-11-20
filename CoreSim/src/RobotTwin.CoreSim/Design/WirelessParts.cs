using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// Wireless and USB catalogue entries used by the world's radio medium
    /// (Sandbox/Radio): a game controller, a PC with a USB cable, a Wi-Fi router, a BLE
    /// beacon, a TV-style IR remote and the TSOP38238 IR receiver; plus the DRV8833
    /// dual H-bridge used by the gamepad car example. Controllers, the PC, the router,
    /// the beacon and the remote have no pins: they act through the radio medium / the
    /// USB plug, not through cables.
    /// </summary>
    public static class WirelessParts
    {
        public const string Category = "Kablosuz & USB";

        public const string GamepadId = "gamepad-wireless";
        public const string UsbHostId = "usb-host";
        public const string RouterId = "wifi-router";
        public const string BeaconId = "ble-beacon";
        public const string IrRemoteId = "ir-remote";
        public const string IrReceiverId = "ir-receiver";
        public const string Drv8833Id = "drv8833";

        public static void Register(PartLibrary lib)
        {
            if (lib == null) throw new ArgumentNullException(nameof(lib));

            var pad = Part(lib, GamepadId, "Kablosuz Kumanda", "RC",
                "Bluetooth oyun kumandası: dünyadaki bir ESP32 ile eşleşir (Bluepad32 API'si). Girişi bilgisayara bağlı gerçek kumandadan (DualSense / DualShock 4 / Xbox) veya ekrandaki sanal kumandadan gelir.",
                new ParameterDefinition("model", "Model", "DualSense (PS5)", "", new[] { "DualSense (PS5)", "DualShock 4 (PS4)", "Xbox Wireless" }),
                new ParameterDefinition("source", "Giriş kaynağı", "otomatik", "", new[] { "otomatik", "sanal", "dualsense", "xinput" }),
                new ParameterDefinition("pairedWith", "Eşli ESP32 (nesne)", "", ""),
                new ParameterDefinition("power", "Açık", "true", "", new[] { "true", "false" }),
                new ParameterDefinition("txpower", "Verici gücü", "4", "dBm"));
            Preset(lib, "dualsense", "DualSense Kablosuz Kumanda (PS5)", pad.Id, "Bluetooth Classic HID; ESP32 + Bluepad32 ile eşleşir", "model=DualSense (PS5)");
            Preset(lib, "dualshock4", "DualShock 4 Kablosuz Kumanda (PS4)", pad.Id, "Bluetooth Classic HID; ESP32 + Bluepad32 ile eşleşir", "model=DualShock 4 (PS4)");
            Preset(lib, "xbox-pad", "Xbox Kablosuz Kumanda", pad.Id, "Bluetooth (BLE HID); ESP32 + Bluepad32 ile eşleşir", "model=Xbox Wireless");

            var pc = Part(lib, UsbHostId, "Bilgisayar (USB)", "PC",
                "Bilgisayar ve USB kablosu: hedef karta takıldığında VBUS 5 V verir ve kartın UART'ını seri monitöre bağlar; çıkarınca güç ve seri bağlantı kesilir.",
                new ParameterDefinition("target", "Takılı kart (nesne)", "", ""),
                new ParameterDefinition("part", "Kart içindeki parça", "", ""),
                new ParameterDefinition("plugged", "USB takılı", "true", "", new[] { "true", "false" }));
            Preset(lib, "usb-pc", "Bilgisayar + USB kablosu", pc.Id, "Karta USB ile güç ve seri monitör bağlantısı");

            var router = Part(lib, RouterId, "Wi-Fi Modem / Erişim Noktası", "AP",
                "2.4 GHz Wi-Fi erişim noktası: ESP32'ler WiFi.begin(ssid, şifre) ile bağlanır, aynı ağdaki kartlar UDP/TCP ile haberleşir.",
                new ParameterDefinition("ssid", "Ağ adı (SSID)", "RoboTwin-Ev", ""),
                new ParameterDefinition("password", "Şifre", "12345678", ""),
                new ParameterDefinition("channel", "Kanal", "6", "", new[] { "1", "6", "11" }),
                new ParameterDefinition("txpower", "Verici gücü", "20", "dBm"));
            Preset(lib, "wifi-router", "Wi-Fi Modem (2.4 GHz)", router.Id, "Erişim noktası; SSID ve şifre ayarlanabilir");

            var beacon = Part(lib, BeaconId, "BLE İşaretçi (Beacon)", "BT",
                "Bluetooth Low Energy reklam yayını yapan küçük etiket; ESP32 BLEScan ile ad ve RSSI'sini görür.",
                new ParameterDefinition("name", "Yayın adı", "RW-Beacon", ""),
                new ParameterDefinition("interval", "Yayın aralığı", "100m", "s"),
                new ParameterDefinition("txpower", "Verici gücü", "0", "dBm"));
            Preset(lib, "ble-beacon", "BLE Beacon Etiketi", beacon.Id, "BLE reklam yayını (iBeacon tarzı)");

            var remote = Part(lib, IrRemoteId, "IR Kumanda (TV tipi)", "IR",
                "38 kHz kızılötesi NEC kumanda (Arduino setlerindeki 21 tuşlu kumanda kodları). Ön ucu +X yönüne bakar; görüş hattı gerekir.",
                new ParameterDefinition("address", "NEC adresi", "0x00", ""),
                new ParameterDefinition("range", "Menzil", "8", "m"));
            Preset(lib, "ir-remote-nec", "IR Kumanda (NEC, 21 tuş)", remote.Id, "38 kHz NEC kodları, görüş hattı");

            var tsop = new PartDefinition
            {
                Id = IrReceiverId,
                Name = "IR Alıcı (38 kHz)",
                Category = Category,
                RefPrefix = "U",
                Symbol = SymbolKind.Module,
                Footprint = "TO92",
                ValueKey = "model",
                Description = "TSOP38238 tipi demodülatörlü IR alıcı: 38 kHz taşıyıcı görürken OUT LOW, yoksa HIGH (dahili 30 kΩ pull-up). Merceği +X yönüne bakar.",
                Build = c =>
                {
                    int vs = c.Pin("VS"), gnd = c.Pin("GND");
                    c.Add(new Resistor(c.Sub("ISUPPLY"), vs, gnd, 5.0 / Math.Max(1e-5, c.Num("isupply", 0.35e-3))));
                    var output = c.Add(new DigitalPin(c.Sub("OUT"), c.Pin("OUT"), vs, gnd)
                    {
                        PullupResistance = 30e3,
                        DriveResistance = 60.0,
                        AbsoluteMaxCurrent = 0.01
                    });
                    output.Mode = PinMode.InputPullup;
                }
            };
            tsop.Parameters.Add(new ParameterDefinition("model", "Model", "TSOP38238", ""));
            tsop.Parameters.Add(new ParameterDefinition("isupply", "Besleme akımı", "0.35m", "A"));
            tsop.Pins.Add(new PinDefinition("OUT", "1", -4, -2));
            tsop.Pins.Add(new PinDefinition("GND", "2", -4, 0));
            tsop.Pins.Add(new PinDefinition("VS", "3", -4, 2));
            tsop.HalfWidth = 3;
            tsop.HalfHeight = 3;
            lib.Add(tsop);
            Preset(lib, "tsop38238", "TSOP38238 IR Alıcı", tsop.Id, "38 kHz IR alıcı modülü, OUT aktif LOW");

            var drv = new PartDefinition
            {
                Id = Drv8833Id,
                Name = "DRV8833 Çift H-Köprü Motor Sürücü",
                Category = StandardParts.Electromechanical,
                RefPrefix = "U",
                Symbol = SymbolKind.Module,
                Footprint = "HDR-1x10",
                ValueKey = "model",
                Description = "İki kanal H-köprü: her çıkış kendi girişini izler (xIN1 PWM + xIN2 LOW = ileri, tersi = geri, ikisi LOW = fren).",
                Build = c =>
                {
                    int vm = c.Pin("VM"), gnd = c.Pin("GND");
                    double r = c.Num("rds", 0.36), imax = c.Num("imax", 1.5), logic = c.Num("logic", 3.3);
                    foreach (var (input, output) in new[] { ("AIN1", "AOUT1"), ("AIN2", "AOUT2"), ("BIN1", "BOUT1"), ("BIN2", "BOUT2") })
                    {
                        var d = c.Add(new AveragedPwmDriver(c.Sub(output), vm, c.Pin(output), gnd) { Resistance = r, CurrentRating = imax, ThermalResistance = 60 });
                        d.ControlNode = c.Pin(input);
                        d.LogicHigh = logic;
                        c.Add(new Resistor(c.Sub("R" + input), c.Pin(input), gnd, 100e3));
                    }

                    c.Add(new Resistor(c.Sub("IQ"), vm, gnd, 10.0 / 1.6e-3));
                }
            };
            drv.Parameters.Add(new ParameterDefinition("model", "Model", "DRV8833", ""));
            drv.Parameters.Add(new ParameterDefinition("rds", "Köprü direnci (HS+LS)", "0.36", "Ω"));
            drv.Parameters.Add(new ParameterDefinition("imax", "Sürekli akım / kanal", "1.5", "A"));
            drv.Parameters.Add(new ParameterDefinition("logic", "Lojik seviye", "3.3", "V"));
            string[] pins = { "VM", "GND", "AIN1", "AIN2", "BIN1", "BIN2", "AOUT1", "AOUT2", "BOUT1", "BOUT2" };
            for (int k = 0; k < pins.Length; k++) drv.Pins.Add(new PinDefinition(pins[k], (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), k < 6 ? -6 : 6, k < 6 ? -5 + 2 * k : -3 + 2 * (k - 6)));
            drv.HalfWidth = 5;
            drv.HalfHeight = 6;
            lib.Add(drv);
            Preset(lib, "drv8833", "DRV8833 Çift Motor Sürücü", drv.Id, "2 × 1.5 A H-köprü, 2.7–10.8 V, PWM girişli", string.Empty, StandardParts.Electromechanical);
        }

        /// <summary>Board parameter usb = "cable": VBUS comes from a world USB cable (see Sandbox/Radio/WorldUsb).</summary>
        public static bool UsbFromCable(PartBuildContext c) => string.Equals(c.Text("usb").Trim(), "cable", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The board's USB 5 V source; returns the VBUS node. With usb = "cable" the source
        /// sits behind a switch ("USBSW") that the world's USB plug opens and closes at run
        /// time (initial state from the "usbplugged" parameter); otherwise it is the fixed source.
        /// </summary>
        public static int UsbSupply(PartBuildContext c, int gnd, double seriesResistance)
        {
            int usb = c.Internal("usb");
            if (!UsbFromCable(c))
            {
                c.Add(new VoltageSource(c.Sub("USB"), usb, gnd, 5.0) { SeriesResistance = seriesResistance });
                return usb;
            }

            int host = c.Internal("vbus");
            c.Add(new VoltageSource(c.Sub("USB"), host, gnd, 5.0) { SeriesResistance = seriesResistance });
            c.Add(new Switch(c.Sub("USBSW"), host, usb, c.Flag("usbplugged")) { OnResistance = 0.05 });
            return usb;
        }

        private static PartDefinition Part(PartLibrary lib, string id, string name, string prefix, string description, params ParameterDefinition[] parameters)
        {
            var part = new PartDefinition
            {
                Id = id,
                Name = name,
                Category = Category,
                RefPrefix = prefix,
                Symbol = SymbolKind.Module,
                Footprint = string.Empty,
                ValueKey = "model",
                Description = description
            };
            part.Parameters.AddRange(parameters);
            part.HalfWidth = 3;
            part.HalfHeight = 2;
            lib.Add(part);
            return part;
        }

        private static void Preset(PartLibrary lib, string id, string name, string partId, string description, string parameters = "", string? category = null)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in parameters.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) map[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
            }

            lib.AddPreset(new PartPreset(id, name, partId, category ?? Category, description, map));
        }
    }
}
