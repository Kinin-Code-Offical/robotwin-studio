using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>[supply] A supply pin's operating range from its datasheet: below <see cref="Min"/> the part is not working.</summary>
    public sealed class SupplyRating
    {
        public SupplyRating(string pin, string ground, double min, double max, string source)
        {
            Pin = pin;
            Ground = ground;
            Min = min;
            Max = max;
            Source = source;
        }

        public string Pin { get; }

        public string Ground { get; }

        /// <summary>Lowest supply (V) it works at, and the highest it takes: the operating range's top, or a 5 V module's
        /// absolute maximum (6 V) where its sheet only says "5 V".</summary>
        public double Min { get; }

        public double Max { get; }

        /// <summary>Where the numbers come from ("HC-SR04: 5 V DC, 4.5–5.5 V").</summary>
        public string Source { get; }
    }

    /// <summary>
    /// [supply] The operating supply ranges of the parts the power report (Sandbox/SupplyMonitor.cs) watches, from their
    /// modules' and chips' datasheets. Keyed by preset (a GY-521 is not an MPU-6050 on its own: its regulator takes 3–5 V),
    /// then by part. Parts not listed are not judged; boards report their own brown-outs (an AVR's BOD, an ESP32's).
    /// </summary>
    public static class SupplyRatings
    {
        private static SupplyRating[] One(string pin, double min, double max, string source) => new[] { new SupplyRating(pin, "GND", min, max, source) };

        private static readonly Dictionary<string, SupplyRating[]> ByPreset = new Dictionary<string, SupplyRating[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["gy-521"] = One("VCC", 3.0, 6.0, "GY-521: 3–5 V into its LDO (6 V at most), which feeds the MPU-6050's 2.375–3.46 V"),
            ["hc-sr04"] = One("VCC", 4.5, 6.0, "HC-SR04: 5 V DC (4.5–5.5 V; its MAX232-class driver takes up to 6 V)"),
            ["gps-neo6m"] = One("VCC", 3.0, 6.0, "GY-NEO6MV2: 3–5 V into its LDO (6 V at most), which feeds the NEO-6M's 2.7–3.6 V"),
            ["gps-neo-m8n"] = One("VCC", 3.0, 6.0, "NEO-M8N module: 3–5 V into its LDO (6 V at most)"),
            ["gy-271"] = One("VCC", 3.0, 6.0, "GY-271: 3–5 V into its LDO (6 V at most), which feeds the HMC5883L's 2.16–3.6 V"),
            ["lidar-a1"] = One("VCC", 4.9, 5.5, "RPLIDAR A1: 5 V (4.9–5.5 V) for its scan motor and ranging core"),
            ["lidar-ld06"] = One("VCC", 4.5, 5.5, "LD06: 5 V (4.5–5.5 V)"),
            ["lidar-16ch"] = One("VCC", 9.0, 18.0, "VLP-16: 9–18 V"),
            ["cam-ov2640"] = One("VCC", 3.0, 3.6, "OV2640 camera module: 3.3 V (3.0–3.6 V)"),
            ["ws2812-8"] = One("VDD", 3.5, 5.3, "WS2812B: 3.5–5.3 V"),
            ["servo-sg90"] = One("V+", 4.8, 6.0, "SG90: 4.8–6 V"),
            ["servo-mg90s"] = One("V+", 4.8, 6.0, "MG90S: 4.8–6 V"),
            ["servo-mg996r"] = One("V+", 4.8, 7.2, "MG996R: 4.8–7.2 V"),
            ["servo-ds3218"] = One("V+", 4.8, 6.8, "DS3218: 4.8–6.8 V"),
            ["tcrt5000"] = One("VCC", 3.3, 6.0, "TCRT5000 module: 3.3–5 V (its LED resistor and LM393 take 6 V)"),
            ["pir-hcsr501"] = One("VCC", 4.5, 20.0, "HC-SR501: 4.5–20 V (its regulator makes 3.3 V)"),
            ["lcd1602-i2c"] = One("VCC", 4.5, 6.0, "1602 LCD + PCF8574: 5 V (4.5–5.5 V, 6 V at most; at 3.3 V the contrast is gone)"),
            ["lcd2004-i2c"] = One("VCC", 4.5, 6.0, "2004 LCD + PCF8574: 5 V (4.5–5.5 V, 6 V at most)"),
        };

        /// <summary>The ratings of an object's supply pins (empty when its part is not listed).</summary>
        public static IReadOnlyList<SupplyRating> Of(WorldObject o) =>
            o.PresetId != null && ByPreset.TryGetValue(o.PresetId, out var p) ? p
            : o.PartId != null && ByPreset.TryGetValue(o.PartId, out var d) ? d
            : Array.Empty<SupplyRating>();
    }
}
