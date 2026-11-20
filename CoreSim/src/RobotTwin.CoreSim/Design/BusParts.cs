using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// Parts that talk over SPI (and I²C): an MCP3008 8-channel ADC and a BMP280 barometer. Their
    /// electrical side (supply current, input leakage, pull-ups) is in the circuit; the protocol
    /// models live in <see cref="Firmware.Devices"/> and are attached by <see cref="Firmware.SpiWiring"/>.
    /// </summary>
    public static class BusParts
    {
        public const string Category = "Sensör · Veri yolu (SPI / I²C)";

        public static void Register(PartLibrary lib)
        {
            // MCP3008: VDD 2.7–5.5 V, 500 µA typical; analog inputs see the sample capacitor only (≈1 GΩ leakage here).
            var adc = Part(lib, "mcp3008", "MCP3008 8 Kanal 10 bit ADC (SPI)", "U", "DIP-16",
                "8 kanallı 10 bit ardışık yaklaşımlı ADC. SPI (mod 0,0 / 1,1), 3.6 MHz'e kadar (5 V). Kanal gerilimi devreden okunur: kod = 1024·Vin/VREF.",
                c =>
                {
                    int vdd = c.Pin("VDD"), dgnd = c.Pin("DGND"), agnd = c.Pin("AGND");
                    c.Add(new Resistor(c.RefDes, vdd, dgnd, 5.0 / Math.Max(1e-5, c.Num("isupply", 0.5e-3))));
                    c.Add(new Resistor(c.Sub("RREF"), c.Pin("VREF"), agnd, 50e3));   // reference input during conversion
                    for (int ch = 0; ch < 8; ch++) c.Add(new Resistor(c.Sub("RIN" + ch), c.Pin("CH" + ch), agnd, 1e9));
                    c.Add(new Resistor(c.Sub("RDOUT"), c.Pin("DOUT"), dgnd, 1e9));
                    c.Add(new Resistor(c.Sub("RCLK"), c.Pin("CLK"), dgnd, 1e9));
                    c.Add(new Resistor(c.Sub("RDIN"), c.Pin("DIN"), dgnd, 1e9));
                    c.Add(new Resistor(c.Sub("RCS"), c.Pin("CS"), dgnd, 1e9));
                    c.Add(new Resistor(c.Sub("RGND"), agnd, dgnd, 1e9));
                },
                P("model", "Model", "MCP3008-I/P"), P("isupply", "Besleme akımı", "500u", "A"),
                P("pinmap", "Pin eşleme", "CH0:1,CH1:2,CH2:3,CH3:4,CH4:5,CH5:6,CH6:7,CH7:8,DGND:9,CS:10,DIN:11,DOUT:12,CLK:13,AGND:14,VREF:15,VDD:16"));
            for (int ch = 0; ch < 8; ch++) adc.Pin("CH" + ch, (ch + 1).ToString(), -6, -7 + 2 * ch);
            adc.Pin("VDD", "16", 6, -7).Pin("VREF", "15", 6, -5).Pin("AGND", "14", 6, -3).Pin("CLK", "13", 6, -1)
               .Pin("DOUT", "12", 6, 1).Pin("DIN", "11", 6, 3).Pin("CS", "10", 6, 5).Pin("DGND", "9", 6, 7).Size(5, 8);

            // GY-BMP280 module: on-board 3.3 V regulator and 10 k pull-ups on SCL/SDA/CSB/SDO.
            var baro = Part(lib, "bmp280", "BMP280 Basınç + Sıcaklık (I²C / SPI)", "U", "HDR-1x6",
                "Bosch BMP280 barometrik basınç ve sıcaklık sensörü. CSB yüksekte I²C (SDO: 0x76 / 0x77), CSB bir pine bağlıysa 4 telli SPI. Ölçülen basınç modülün dünyadaki yüksekliğinden (ISA atmosferi), sıcaklık ortamdan gelir.",
                c =>
                {
                    int vcc = c.Pin("VCC"), gnd = c.Pin("GND");
                    c.Add(new Resistor(c.RefDes, vcc, gnd, 3.3 / Math.Max(1e-6, c.Num("isupply", 0.7e-3))));
                    double pull = c.Num("pullup", 10e3);
                    foreach (var pin in new[] { "SCL", "SDA", "CSB", "SDO" }) c.Add(new Resistor(c.Sub("R" + pin), c.Pin(pin), vcc, pull));
                },
                P("model", "Model", "GY-BMP280"), P("address", "I²C adresi", "0x76", "", "0x76", "0x77"),
                P("isupply", "Besleme akımı", "0.7m", "A"), P("pullup", "Pull-up direnci", "10k", "Ω"),
                P("pinmap", "Pin eşleme", "VCC:1,GND:2,SCL:3,SDA:4,CSB:5,SDO:6"));
            baro.Pin("VCC", "1", -5, -5).Pin("GND", "2", -5, -3).Pin("SCL", "3", -5, -1).Pin("SDA", "4", -5, 1)
                .Pin("CSB", "5", -5, 3).Pin("SDO", "6", -5, 5).Size(4, 6);
            Preset(lib, "gy-bmp280", "GY-BMP280 Basınç Modülü", "bmp280", "300–1100 hPa, ±1 Pa çözünürlük, I²C 0x76 veya SPI");
            Preset(lib, "mcp3008-dip", "MCP3008 ADC (DIP-16)", "mcp3008", "8 kanal 10 bit SPI ADC — Raspberry Pi / ESP32 için analog giriş çoğaltıcı");
        }

        private static PartDefinition Part(PartLibrary lib, string id, string name, string prefix, string footprint, string description,
            Action<PartBuildContext> build, params ParameterDefinition[] parameters)
        {
            var part = new PartDefinition
            {
                Id = id, Name = name, Category = Category, RefPrefix = prefix, Symbol = SymbolKind.Module, Footprint = footprint,
                ValueKey = "model", Description = description, Build = build
            };
            part.Parameters.AddRange(parameters);
            lib.Add(part);
            return part;
        }

        private static PartDefinition Pin(this PartDefinition part, string name, string pad, double x, double y)
        {
            part.Pins.Add(new PinDefinition(name, pad, x, y));
            return part;
        }

        private static PartDefinition Size(this PartDefinition part, double hw, double hh)
        {
            part.HalfWidth = hw;
            part.HalfHeight = hh;
            return part;
        }

        private static ParameterDefinition P(string key, string label, string value, string unit = "", params string[] choices) =>
            new ParameterDefinition(key, label, value, unit, choices.Length > 0 ? choices : null);

        private static void Preset(PartLibrary lib, string id, string name, string partId, string description) =>
            lib.AddPreset(new PartPreset(id, name, partId, Category, description, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
    }
}
