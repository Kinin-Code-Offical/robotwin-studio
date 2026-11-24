using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Firmware.Esp32;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// ESP32 development boards. The electrical model mirrors the Arduino boards: USB 5 V
    /// (through the reverse-protection Schottky) or an external 5 V/VIN feeds an AMS1117-3.3
    /// (≈1.1 V dropout) that makes the 3V3 rail; the chip is a load on 3V3 whose current the
    /// co-simulation sets (≈80 mA running, more with Wi-Fi, µA in deep sleep); EN has the
    /// 10 kΩ / 1 µF reset network; every exposed GPIO is a DigitalPin on the 3V3 rail
    /// (≈35 Ω drive, 45 kΩ pull-up) plus an <see cref="Esp32PadAux"/> for the pull-down and DAC.
    /// Pin names: "IO2", "IO34", "VP" (GPIO36), "VN" (GPIO39), "TX"/"RX" (GPIO1/3), "3V3",
    /// "5V"/"VIN", "EN", "GND1..3"; flash pins keep their silkscreen names (SD0..SD3, CMD, CLK).
    /// </summary>
    public static class Esp32Parts
    {
        public const string DevKitCId = "esp32-devkitc";
        public const string DevKitV1Id = "esp32-devkit-v1";

        /// <summary>ESP32-DevKitC V4 header J2 (left, top to bottom) and J3 (right, top to bottom).</summary>
        public static readonly string[] DevKitCLeft =
        {
            "3V3", "EN", "VP", "VN", "IO34", "IO35", "IO32", "IO33", "IO25", "IO26", "IO27", "IO14", "IO12", "GND1", "IO13", "SD2", "SD3", "CMD", "5V"
        };

        public static readonly string[] DevKitCRight =
        {
            "GND2", "IO23", "IO22", "TX", "RX", "IO21", "GND3", "IO19", "IO18", "IO5", "IO17", "IO16", "IO4", "IO0", "IO2", "IO15", "SD1", "SD0", "CLK"
        };

        /// <summary>DOIT ESP32 DevKit V1 (30 pin), left and right headers top to bottom (antenna up).</summary>
        public static readonly string[] DevKitV1Left =
        {
            "EN", "VP", "VN", "IO34", "IO35", "IO32", "IO33", "IO25", "IO26", "IO27", "IO14", "IO12", "IO13", "GND1", "VIN"
        };

        public static readonly string[] DevKitV1Right =
        {
            "IO23", "IO22", "TX", "RX", "IO21", "IO19", "IO18", "IO5", "IO17", "IO16", "IO4", "IO2", "IO15", "GND2", "3V3"
        };

        public static void Register(PartLibrary lib)
        {
            if (lib == null) throw new ArgumentNullException(nameof(lib));

            var devkitc = Board(lib, DevKitCId, "ESP32 DevKitC V4", "ESP32-DEVKITC", "ESP32-DEVKITC", DevKitCLeft, DevKitCRight,
                new[] { "GND1", "GND2", "GND3" }, "5V", onboardLed: false);
            lib.AddPreset(new PartPreset(DevKitCId, "ESP32 DevKitC V4", devkitc.Id, StandardParts.Boards,
                "ESP32-WROOM-32, 240 MHz çift çekirdek, Wi-Fi/BT, 3.3 V lojik, 38 pin", new Dictionary<string, string>()));

            var v1 = Board(lib, DevKitV1Id, "ESP32 DevKit V1 (DOIT)", "ESP32-DEVKIT-V1", "ESP32-DEVKIT-V1", DevKitV1Left, DevKitV1Right,
                new[] { "GND1", "GND2" }, "VIN", onboardLed: true);
            lib.AddPreset(new PartPreset(DevKitV1Id, "ESP32 DevKit V1 (DOIT)", v1.Id, StandardParts.Boards,
                "ESP32-WROOM-32, 30 pin, GPIO2'de mavi LED, 3.3 V lojik", new Dictionary<string, string>()));
        }

        /// <summary>[esp32hw] The board's run mode (docs/gelistirici/ESP32_MODLARI.md).</summary>
        public const string ModeKey = "mode";

        /// <summary>Mode 1: the sketch runs on RobotWin's Arduino-ESP32 model, natively (many boards, fast).</summary>
        public const string FastMode = "hızlı simülasyon";

        /// <summary>Mode 2: built by Espressif's toolchain for the real chip (and, with the Xtensa emulator, run as such).</summary>
        public const string HardwareMode = "donanım uyumlu";

        /// <summary>The board is set to the hardware-compatible mode.</summary>
        public static bool IsHardwareMode(IReadOnlyDictionary<string, string>? parameters) =>
            parameters != null && parameters.TryGetValue(ModeKey, out var mode) && mode == HardwareMode;

        /// <summary>What a board in the hardware-compatible mode says while the Xtensa emulator is not there yet.</summary>
        public const string HardwareModeNotice = "Donanım uyumlu mod: Xtensa emülatörü henüz hazır değil, bu kart simülasyonda hızlı modla çalışıyor. Gerçek kart için Kod → Gerçek kart.";

        private static PartDefinition Board(PartLibrary lib, string id, string name, string variant, string footprint,
            string[] left, string[] right, string[] grounds, string supplyPin, bool onboardLed)
        {
            var part = new PartDefinition
            {
                Id = id,
                Name = name,
                Category = StandardParts.Boards,
                RefPrefix = "U",
                Symbol = SymbolKind.Board,
                SymbolVariant = variant,
                Footprint = footprint,
                ValueKey = string.Empty,
                Description = "İki çalışma modu: hızlı simülasyon (çizim RobotWin'in Arduino-ESP32 modeliyle çalışır) ve donanım uyumlu (Espressif'in araç zinciriyle gerçek kart için derlenir; Xtensa emülatörü hazır olana dek simülasyonda hızlı modla çalışır)."
            };
            part.Parameters.Add(new ParameterDefinition("usb", "USB bağlı", "true", "", new[] { "true", "false" }));
            part.Parameters.Add(new ParameterDefinition(ModeKey, "Çalışma modu", FastMode, "", new[] { FastMode, HardwareMode }));
            part.Parameters.Add(new ParameterDefinition("idle", "Çalışma akımı", "80m", "A"));

            int rows = Math.Max(left.Length, right.Length);
            double top = -(rows - 1);
            for (int i = 0; i < left.Length; i++) part.Pins.Add(new PinDefinition(left[i], left[i], -7, top + 2 * i));
            for (int i = 0; i < right.Length; i++) part.Pins.Add(new PinDefinition(right[i], right[i], 7, top + 2 * i));
            part.HalfWidth = 6;
            part.HalfHeight = rows;
            part.InternalConnections.Add(grounds);

            var gpioPins = left.Concat(right)
                .Select(p => (Name: p, Gpio: Esp32Gpio.FromPinName(p)))
                .Where(p => p.Gpio >= 0 && !Esp32Gpio.IsFlashPin(p.Gpio))
                .ToList();
            part.Build = c => Build(c, gpioPins, grounds[0], supplyPin, onboardLed);
            lib.Add(part);
            return part;
        }

        private static void Build(PartBuildContext c, List<(string Name, int Gpio)> gpioPins, string ground, string supplyPin, bool onboardLed)
        {
            int vin = c.Pin(supplyPin), v3 = c.Pin("3V3"), gnd = c.Pin(ground), en = c.Pin("EN");

            if (c.Flag("usb") || WirelessParts.UsbFromCable(c))
            {
                // USB VBUS through the reverse-protection Schottky to the 5 V / VIN rail.
                int usb = WirelessParts.UsbSupply(c, gnd, 0.2);   // [radio] usb=cable: VBUS switched by the world's USB cable
                c.Add(new Diode(c.Sub("D1"), usb, vin)
                {
                    SaturationCurrent = 31.7e-6, Ideality = 1.373, SeriesResistance = 0.051, BreakdownVoltage = 40,
                    BreakdownCurrent = 1e-3, BandGap = 0.69, MaxCurrent = 1.0, ThermalResistance = 120
                });
            }

            c.Add(new Capacitor(c.Sub("CIN"), vin, gnd, 10e-6));

            // AMS1117-3.3 LDO: 1.1 V dropout, 1 A limit, 5 mA quiescent.
            c.Add(new LinearRegulator(c.Sub("REG3V3"), vin, gnd, v3, 3.3)
            {
                Dropout = 1.1, CurrentLimit = 1.0, MaxInputVoltage = 15, ThermalResistance = 90, QuiescentCurrent = 5e-3
            });

            // EN (CHIP_PU): 10 kΩ pull-up and 1 µF to ground (~10 ms power-on reset delay).
            c.Add(new Resistor(c.Sub("REN"), v3, en, 10e3));
            c.Add(new Capacitor(c.Sub("CEN"), en, gnd, 1e-6));

            c.Add(new Esp32ChipLoad(c.Sub("CHIP"), v3, gnd, en, c.Num("idle", 0.08)));

            foreach (var (name, gpio) in gpioPins)
            {
                int node = c.Pin(name);
                c.Add(new DigitalPin(c.Sub(name), node, v3, gnd)
                {
                    DriveResistance = 35.0,
                    PullupResistance = 45e3,
                    AbsoluteMaxCurrent = 0.04
                });
                if (Esp32Gpio.CanOutput(gpio)) c.Add(new Esp32PadAux(c.Sub(name + ".AUX"), node, gnd));
                if (gpio == 0) c.Add(new Resistor(c.Sub("RBOOT"), v3, node, 10e3));   // BOOT button pull-up
            }

            if (onboardLed)
            {
                // DOIT V1: blue LED on GPIO2 through 1 kΩ.
                int a = c.Internal("led");
                c.Add(new Resistor(c.Sub("RLED"), c.Pin("IO2"), a, 1e3));
                c.Add(new Led(c.Sub("LED"), a, gnd, 2.9, 0.02));
            }
        }
    }
}
