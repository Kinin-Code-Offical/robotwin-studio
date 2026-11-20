using System;
using System.Globalization;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>Parses and formats engineering values such as "4k7", "100nF", "2.2M", "3V3", "20mA".</summary>
    public static class SiValue
    {
        private static readonly (string Prefix, double Scale)[] Prefixes =
        {
            ("f", 1e-15), ("p", 1e-12), ("n", 1e-9), ("u", 1e-6), ("µ", 1e-6), ("m", 1e-3),
            ("k", 1e3), ("K", 1e3), ("M", 1e6), ("G", 1e9)
        };

        public static bool TryParse(string? text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text!.Trim().Replace(" ", string.Empty).Replace(',', '.');

            // Strip unit words/symbols from the end ("Ω", "ohm", "F", "H", "V", "A", "W", "Hz", "s").
            string[] units = { "ohms", "ohm", "Ω", "Hz", "F", "H", "V", "A", "W", "s", "R" };
            foreach (string unit in units)
            {
                if (s.Length > unit.Length && s.EndsWith(unit, StringComparison.Ordinal))
                {
                    s = s.Substring(0, s.Length - unit.Length);
                    break;
                }
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;

            foreach (var (prefix, scale) in Prefixes)
            {
                int index = s.IndexOf(prefix, StringComparison.Ordinal);
                if (index < 0) continue;
                string whole = s.Substring(0, index);
                string frac = s.Substring(index + prefix.Length);
                if (whole.Length == 0) continue;
                string combined = frac.Length > 0 ? whole + "." + frac : whole;
                if (double.TryParse(combined, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    value = number * scale;
                    return true;
                }
            }

            // Letter used as decimal point with no prefix: "3V3", "4R7".
            for (int i = 1; i < s.Length - 1; i++)
            {
                if (char.IsLetter(s[i]))
                {
                    string combined = s.Substring(0, i) + "." + s.Substring(i + 1);
                    if (double.TryParse(combined, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
                }
            }

            return false;
        }

        public static double Parse(string? text, double fallback)
        {
            return TryParse(text, out double value) ? value : fallback;
        }

        public static string Format(double value, string unit = "", int digits = 3)
        {
            if (double.IsNaN(value)) return "—";
            if (double.IsInfinity(value)) return value > 0 ? "∞" : "-∞";
            double abs = Math.Abs(value);
            // Solver residue (e.g. 1e-206 V on an undriven node) reads as zero.
            if (abs < 1e-15) return "0 " + unit;
            (string Prefix, double Scale)[] table =
            {
                ("G", 1e9), ("M", 1e6), ("k", 1e3), ("", 1.0), ("m", 1e-3), ("µ", 1e-6), ("n", 1e-9), ("p", 1e-12)
            };

            foreach (var (prefix, scale) in table)
            {
                if (abs >= scale * 0.9995)
                {
                    double scaled = value / scale;
                    return scaled.ToString("G" + digits, CultureInfo.InvariantCulture) + " " + prefix + unit;
                }
            }

            return value.ToString("G" + digits, CultureInfo.InvariantCulture) + " " + unit;
        }
    }
}
