using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>[spice] One <c>.model</c> card: its name, type (D, NPN, PNP, NMOS, PMOS) and parameters (upper-case names, SI values).</summary>
    public sealed class SpiceModel
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public Dictionary<string, double> Parameters { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// [spice] Manufacturers' SPICE models (<c>.model</c> cards, as datasheets and vendors' libraries give them) for our parts:
    /// a diode's IS, N, RS, BV, IBV, EG, CJO; a bipolar transistor's IS, BF, BR, VAF, CJE, CJC; a MOSFET's VTO. Cards may run
    /// over several lines (<c>+</c>), carry comments (<c>*</c>, <c>;</c>) and SPICE's suffixes (f p n u m k MEG G T, mil);
    /// parameters our models do not have are listed, not silently dropped.
    /// </summary>
    public static class SpiceModels
    {
        /// <summary>Every <c>.model</c> card in <paramref name="text"/>.</summary>
        public static List<SpiceModel> Parse(string text)
        {
            var models = new List<SpiceModel>();
            // Join continuation lines and drop comments.
            var joined = new StringBuilder();
            foreach (var raw in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                string line = raw;
                int semi = line.IndexOf(';');
                if (semi >= 0) line = line.Substring(0, semi);
                string t = line.Trim();
                if (t.StartsWith("*", StringComparison.Ordinal) || t.Length == 0) continue;
                if (t.StartsWith("+", StringComparison.Ordinal)) joined.Append(' ').Append(t.Substring(1));
                else joined.Append('\n').Append(t);
            }

            foreach (var card in joined.ToString().Split('\n'))
            {
                var m = Regex.Match(card.Trim(), @"^\.model\s+(\S+)\s+([A-Za-z]+)\s*\(?(.*?)\)?\s*$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var model = new SpiceModel { Name = m.Groups[1].Value, Type = m.Groups[2].Value.ToUpperInvariant() };
                foreach (Match p in Regex.Matches(m.Groups[3].Value, @"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([-+0-9.eE]+[A-Za-z]*)"))
                {
                    if (TryNumber(p.Groups[2].Value, out double v)) model.Parameters[p.Groups[1].Value.ToUpperInvariant()] = v;
                }

                models.Add(model);
            }

            return models;
        }

        /// <summary>A SPICE number: 14.34f, 2.5n, 100u, 1m (milli), 1.5MEG, 10k, 3e-9, 25mil.</summary>
        public static bool TryNumber(string text, out double value)
        {
            value = 0;
            var m = Regex.Match(text.Trim(), @"^([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)([A-Za-z]*)$");
            if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return false;
            string s = m.Groups[2].Value.ToUpperInvariant();
            double scale = s.StartsWith("MEG", StringComparison.Ordinal) ? 1e6
                : s.StartsWith("MIL", StringComparison.Ordinal) ? 25.4e-6
                : s.Length == 0 ? 1
                : s[0] switch { 'F' => 1e-15, 'P' => 1e-12, 'N' => 1e-9, 'U' => 1e-6, 'M' => 1e-3, 'K' => 1e3, 'G' => 1e9, 'T' => 1e12, _ => 1 };
            value = n * scale;
            return true;
        }

        /// <summary>The part kinds a model type fits ("diode" fits D; "npn" NPN …).</summary>
        public static bool Fits(SpiceModel model, string partId) => model.Type switch
        {
            "D" => partId is "diode" or "schottky" or "zener",
            "NPN" => partId == "npn",
            "PNP" => partId == "pnp",
            "NMOS" => partId == "nmos",
            "PMOS" => partId == "pmos",
            _ => false
        };

        /// <summary>
        /// The part parameters <paramref name="model"/> gives (our names, as the part's parameter text), and the SPICE parameters
        /// it has that our models do not use.
        /// </summary>
        public static (Dictionary<string, string> Parameters, List<string> Unused) ToPart(SpiceModel model)
        {
            var map = model.Type switch
            {
                "D" => new Dictionary<string, string> { ["IS"] = "is", ["N"] = "n", ["RS"] = "rs", ["BV"] = "bv", ["IBV"] = "ibv", ["EG"] = "eg", ["CJO"] = "cj", ["CJ0"] = "cj" },
                "NPN" or "PNP" => new Dictionary<string, string> { ["IS"] = "is", ["BF"] = "bf", ["BR"] = "br", ["VAF"] = "va", ["VA"] = "va", ["CJE"] = "cje", ["CJC"] = "cjc" },
                "NMOS" or "PMOS" => new Dictionary<string, string> { ["VTO"] = "vth", ["VTH0"] = "vth" },
                _ => new Dictionary<string, string>()
            };
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["model"] = model.Name };
            var unused = new List<string>();
            foreach (var kv in model.Parameters)
            {
                if (map.TryGetValue(kv.Key, out var ours))
                {
                    double v = kv.Value;
                    if (ours == "vth" && model.Type == "PMOS") v = Math.Abs(v);   // our P-channel threshold is a magnitude
                    result[ours] = v.ToString("G6", CultureInfo.InvariantCulture);
                }
                else
                {
                    unused.Add(kv.Key);
                }
            }

            return (result, unused);
        }
    }
}
