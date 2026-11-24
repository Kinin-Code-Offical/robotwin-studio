using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>One line of the bill of materials: identical parts grouped together.</summary>
    public sealed class BomLine
    {
        public List<string> References { get; } = new List<string>();
        public int Quantity => References.Count;
        public string Value { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PartId { get; set; } = string.Empty;
        public string? PresetId { get; set; }
        public string Footprint { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string ReferenceText => string.Join(", ", References);
    }

    /// <summary>Bill of materials of a schematic and its CSV form.</summary>
    public static class BomBuilder
    {
        /// <summary>
        /// Groups the real components (power/ground symbols are left out) by part, preset,
        /// value and footprint, references in natural order ("R2" before "R10").
        /// </summary>
        public static List<BomLine> Build(Schematic doc, PartLibrary library)
        {
            var lines = new Dictionary<string, BomLine>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in doc.Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null || def.Symbol is SymbolKind.Ground or SymbolKind.PowerRail || Annotator.IsPowerSymbol(part.RefDes)) continue;
                var parameters = doc.ResolveParameters(library, part);
                var preset = part.PresetId != null ? library.FindPreset(part.PresetId) : null;
                string value = ValueText(def, parameters);
                string footprint = def.ResolveFootprint(parameters);
                string key = string.Join("|", def.Id, preset?.Id ?? string.Empty, value, footprint);
                if (!lines.TryGetValue(key, out var line))
                {
                    line = new BomLine
                    {
                        PartId = def.Id,
                        PresetId = preset?.Id,
                        Name = preset?.Name ?? def.Name,
                        Value = value.Length > 0 ? value : preset?.Name ?? def.Name,
                        Footprint = footprint,
                        Description = preset?.Description ?? def.Description
                    };
                    lines[key] = line;
                }

                line.References.Add(part.RefDes);
            }

            foreach (var line in lines.Values) line.References.Sort(Annotator.NaturalComparer);
            return lines.Values
                .OrderBy(l => Annotator.Prefix(l.References[0]), StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.References[0], Annotator.NaturalComparer)
                .ToList();
        }

        /// <summary>The value as printed on the sheet ("220Ω", "100nF", "LM358").</summary>
        public static string ValueText(PartDefinition def, IReadOnlyDictionary<string, string> parameters)
        {
            if (string.IsNullOrEmpty(def.ValueKey) || !parameters.TryGetValue(def.ValueKey, out var raw) || string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var unit = def.Parameters.FirstOrDefault(p => p.Key == def.ValueKey)?.Unit ?? string.Empty;
            if ((unit == "Ω" || unit == "F" || unit == "H" || unit == "V" || unit == "A") && SiValue.TryParse(raw, out double v))
            {
                return SiValue.Format(v, unit).Replace(" ", string.Empty);
            }

            return raw.Trim();
        }

        public static string ToCsv(IEnumerable<BomLine> lines, char separator = ',')
        {
            var sb = new StringBuilder();
            string Q(string s)
            {
                if (s.IndexOf(separator) < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0 && s.IndexOf('\r') < 0) return s;
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }

            sb.Append(string.Join(separator.ToString(), new[] { "#", "Referans", "Adet", "Değer", "Parça", "Kılıf", "Açıklama" }.Select(Localization.T))).Append("\r\n");   // [lang]
            int i = 1;
            foreach (var l in lines)
            {
                sb.Append(string.Join(separator.ToString(), new[]
                {
                    i++.ToString(CultureInfo.InvariantCulture), Q(l.ReferenceText), l.Quantity.ToString(CultureInfo.InvariantCulture),
                    Q(l.Value), Q(Localization.T(l.Name)), Q(l.Footprint), Q(Localization.T(l.Description))
                })).Append("\r\n");
            }

            return sb.ToString();
        }
    }
}
