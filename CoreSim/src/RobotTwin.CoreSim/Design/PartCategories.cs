using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [libs] The catalog in two levels: every part's main category (Pasif, Güç, Ölçüm &amp; Kaynak …) and a sub-category
    /// (Direnç, Kondansatör, Güç kaynağı, Sinyal jeneratörü …) chosen by its simulation model. A preset whose category
    /// already reads "Main · Sub" keeps it.
    /// </summary>
    public static class PartCategories
    {
        public const string Separator = " · ";

        /// <summary>Simulation model id → sub-category.</summary>
        private static readonly Dictionary<string, string> ByModel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Pasif
            ["resistor"] = "Direnç", ["potentiometer"] = "Potansiyometre & trimpot", ["ntc"] = "Sensör direnci (NTC, LDR)", ["ldr"] = "Sensör direnci (NTC, LDR)",
            ["fuse"] = "Sigorta", ["capacitor"] = "Kondansatör", ["electrolytic"] = "Kondansatör", ["inductor"] = "Bobin",
            // Yarı iletken
            ["diode"] = "Diyot", ["schottky"] = "Diyot", ["zener"] = "Diyot", ["led"] = "LED", ["led-rgb"] = "LED", ["led-power"] = "LED",
            ["npn"] = "BJT transistör", ["pnp"] = "BJT transistör", ["darlington"] = "BJT transistör", ["nmos"] = "MOSFET", ["pmos"] = "MOSFET",
            // Güç
            ["regulator"] = "Regülatör", ["battery"] = "Pil & akü", ["ground"] = "Toprak & besleme etiketi", ["power-rail"] = "Toprak & besleme etiketi",
            // Ölçüm & kaynak
            ["dc-source"] = "Güç kaynağı", ["bench-psu"] = "Güç kaynağı", ["acdc-psu"] = "Güç kaynağı", ["current-source"] = "Güç kaynağı",
            ["signal-generator"] = "Sinyal jeneratörü", ["multimeter"] = "Ölçü aleti", ["ammeter"] = "Ölçü aleti", ["voltmeter"] = "Ölçü aleti",
            // Anahtar & röle
            ["button"] = "Buton", ["switch"] = "Anahtar", ["relay"] = "Röle",
            // Analog / lojik
            ["opamp"] = "Op-amp", ["gate1"] = "Kapı", ["gate2"] = "Kapı",
            // Bağlantı
            ["header-2"] = "Pin başlığı", ["header-3"] = "Pin başlığı", ["header-4"] = "Pin başlığı", ["header-5"] = "Pin başlığı", ["header-6"] = "Pin başlığı",
            ["header-8"] = "Pin başlığı", ["terminal-2"] = "Klemens", ["terminal-3"] = "Klemens",
            // Ekran & modül
            ["oled-ssd1306"] = "Ekran", ["lcd-i2c"] = "Ekran", ["mpu6050"] = "Sensör modülü",
            // Elektromekanik
            ["dc-motor"] = "Motor", ["motor-encoder"] = "Motor", ["servo"] = "Servo", ["buzzer"] = "Ses", ["lamp"] = "Aydınlatma", ["led-strip"] = "Aydınlatma",
            ["drv8833"] = "Motor sürücü", ["motor-driver"] = "Motor sürücü", ["esc"] = "Motor sürücü",
            ["stepper"] = "Motor", ["a4988"] = "Motor sürücü", ["drv8825-stepper"] = "Motor sürücü", ["tmc2209"] = "Motor sürücü",
            // Kartlar
            ["arduino-uno"] = "Arduino", ["arduino-nano"] = "Arduino", ["esp32-devkitc"] = "ESP32", ["esp32-devkit-v1"] = "ESP32", ["flight-controller"] = "Uçuş kontrolcüsü",
            // Kablosuz & USB
            ["gamepad-wireless"] = "Kumanda", ["usb-host"] = "USB", ["wifi-router"] = "Wi-Fi & BLE", ["ble-beacon"] = "Wi-Fi & BLE",
            ["ir-remote"] = "Kızılötesi", ["ir-receiver"] = "Kızılötesi",
            // Robot hücresi
            ["robot-arm"] = "Robot & tutucu", ["gripper-parallel"] = "Robot & tutucu", ["vacuum-cup"] = "Robot & tutucu", ["conveyor"] = "Taşıma", ["pallet"] = "Taşıma",
            ["light-curtain"] = "Güvenlik", ["fence-panel"] = "Güvenlik", ["robot-pedestal"] = "Montaj",
            // Sensör (genel)
            ["hc-sr04"] = "Mesafe", ["sensor-module"] = "Modül"
        };

        /// <summary>The main category of a preset ("Sensör · Çizgi" → "Sensör").</summary>
        public static string MainOf(PartPreset p)
        {
            int i = p.Category.IndexOf(Separator, StringComparison.Ordinal);
            return i < 0 ? p.Category : p.Category.Substring(0, i);
        }

        /// <summary>The sub-category, or null when the part stands directly in its main category.</summary>
        public static string? SubOf(PartPreset p)
        {
            int i = p.Category.IndexOf(Separator, StringComparison.Ordinal);
            if (i >= 0) return p.Category.Substring(i + Separator.Length);
            return ByModel.TryGetValue(p.PartId, out var sub) ? sub : null;
        }

        /// <summary>"Main · Sub" (or just the main category).</summary>
        public static string PathOf(PartPreset p) => SubOf(p) is { } sub ? MainOf(p) + Separator + sub : MainOf(p);

        /// <summary>Main categories with their sub-categories and how many parts each holds, alphabetical (Turkish order).</summary>
        public static List<(string Main, int Count, List<(string Sub, int Count)> Subs)> Tree(IEnumerable<PartPreset> presets)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            var cmp = StringComparer.Create(culture, true);
            return presets.GroupBy(MainOf, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, cmp)
                .Select(g => (g.Key, g.Count(), g.Where(p => SubOf(p) != null).GroupBy(p => SubOf(p)!, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s.Key, cmp).Select(s => (s.Key, s.Count())).ToList()))
                .ToList();
        }

        /// <summary>A filter: "Tümü", a main category, or "Main · Sub".</summary>
        public static bool Matches(PartPreset p, string filter)
        {
            if (string.IsNullOrEmpty(filter) || filter == "Tümü") return true;
            return filter.Contains(Separator) ? string.Equals(PathOf(p), filter, StringComparison.OrdinalIgnoreCase) : string.Equals(MainOf(p), filter, StringComparison.OrdinalIgnoreCase);
        }
    }
}
