using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Design
{
    public enum SearchHitKind
    {
        Part,
        Value,
        Net,
        Label
    }

    public sealed class SearchHit
    {
        public SearchHit(SearchHitKind kind, string title, string detail, GridPoint location, string? refDes = null, string? net = null, object? target = null)
        {
            Kind = kind;
            Title = title;
            Detail = detail;
            Location = location;
            RefDes = refDes;
            Net = net;
            Target = target;
        }

        public SearchHitKind Kind { get; }
        public string Title { get; }
        public string Detail { get; }
        public GridPoint Location { get; }
        public string? RefDes { get; }
        public string? Net { get; }

        /// <summary>The document object (part or label) the hit belongs to, if any.</summary>
        public object? Target { get; }

        public string KindText => Kind switch
        {
            SearchHitKind.Part => "Parça",
            SearchHitKind.Value => "Değer",
            SearchHitKind.Net => "Hat",
            _ => "Etiket"
        };
    }

    public sealed class SearchOptions
    {
        public bool MatchCase { get; set; }
        public bool WholeWord { get; set; }
        public bool RefDes { get; set; } = true;
        public bool Values { get; set; } = true;
        public bool Nets { get; set; } = true;
    }

    /// <summary>
    /// Find and replace on a schematic (KiCad Ctrl+F / Ctrl+H): designators, values, part names,
    /// net and label names. '*' and '?' work as wildcards when the query contains them.
    /// </summary>
    public static class SchematicSearch
    {
        public static Regex BuildPattern(string query, SearchOptions options)
        {
            string body = query.IndexOfAny(new[] { '*', '?' }) >= 0
                ? Regex.Escape(query).Replace("\\*", ".*").Replace("\\?", ".")
                : Regex.Escape(query);
            if (options.WholeWord) body = "^" + body + "$";
            var flags = RegexOptions.CultureInvariant | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            return new Regex(body, flags);
        }

        public static List<SearchHit> Find(Schematic doc, PartLibrary library, string query, SearchOptions? options = null)
        {
            options ??= new SearchOptions();
            var hits = new List<SearchHit>();
            if (string.IsNullOrWhiteSpace(query)) return hits;
            var rx = BuildPattern(query.Trim(), options);
            var netlist = doc.BuildNetlist(library);

            foreach (var part in doc.Parts.OrderBy(p => p.RefDes, Annotator.NaturalComparer))
            {
                var def = library.Find(part.PartId);
                var parameters = def != null ? doc.ResolveParameters(library, part) : new Dictionary<string, string>();
                string value = def != null ? BomBuilder.ValueText(def, parameters) : string.Empty;
                string raw = def != null && def.ValueKey.Length > 0 && parameters.TryGetValue(def.ValueKey, out var r) ? r : string.Empty;
                var preset = part.PresetId != null ? library.FindPreset(part.PresetId) : null;
                string name = preset?.Name ?? def?.Name ?? part.PartId;
                if (options.RefDes && rx.IsMatch(part.RefDes))
                {
                    hits.Add(new SearchHit(SearchHitKind.Part, part.RefDes, name + (value.Length > 0 ? " · " + value : string.Empty), part.Position, part.RefDes, null, part));
                }
                else if (options.Values && ((value.Length > 0 && rx.IsMatch(value)) || (raw.Length > 0 && rx.IsMatch(raw)) || rx.IsMatch(name)))
                {
                    hits.Add(new SearchHit(SearchHitKind.Value, part.RefDes + "  " + (value.Length > 0 ? value : name), name, part.Position, part.RefDes, null, part));
                }
            }

            if (options.Nets)
            {
                foreach (var net in netlist.Nets.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (!rx.IsMatch(net.Name)) continue;
                    GridPoint at = default;
                    int w = netlist.WireNets.FindIndex(n => n == net.Name);
                    int l = netlist.LabelNets.FindIndex(n => n == net.Name);
                    if (l >= 0) at = doc.Labels[l].Position;
                    else if (w >= 0 && doc.Wires[w].Points.Count > 0) at = doc.Wires[w].Points[0];
                    else if (net.Connections.Count > 0 && doc.FindPart(net.Connections[0].RefDes) is { } owner &&
                             library.Find(owner.PartId)?.FindPin(net.Connections[0].Pin) is { } pin)
                    {
                        at = owner.Transform(pin.X, pin.Y);
                    }

                    hits.Add(new SearchHit(SearchHitKind.Net, net.Name, $"{net.Connections.Count} pin: " + string.Join(", ", net.Connections.Take(6)) + (net.Connections.Count > 6 ? "…" : string.Empty), at, null, net.Name));
                }

                foreach (var label in doc.Labels)
                {
                    if (rx.IsMatch(label.Name) && hits.All(h => h.Kind != SearchHitKind.Net || !string.Equals(h.Net, label.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        hits.Add(new SearchHit(SearchHitKind.Label, label.Name, label.Global ? "Global etiket" : "Etiket", label.Position, null, label.Name, label));
                    }
                }
            }

            return hits;
        }

        /// <summary>
        /// Replaces matches of <paramref name="find"/> with <paramref name="replacement"/> in values,
        /// designators (skipping ones that would clash), label names and power-rail net names.
        /// Returns the number of changed fields.
        /// </summary>
        public static int Replace(Schematic doc, PartLibrary library, string find, string replacement, SearchOptions? options = null)
        {
            options ??= new SearchOptions();
            if (string.IsNullOrEmpty(find)) return 0;
            var rx = BuildPattern(find, options);
            bool wildcard = find.IndexOfAny(new[] { '*', '?' }) >= 0;
            string Apply(string s) => wildcard || options.WholeWord ? rx.Replace(s, replacement.Replace("$", "$$"), 1) : rx.Replace(s, replacement.Replace("$", "$$"));
            int count = 0;

            foreach (var part in doc.Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null) continue;
                if (options.RefDes && rx.IsMatch(part.RefDes))
                {
                    string fresh = Apply(part.RefDes).Trim();
                    if (fresh.Length > 0 && fresh != part.RefDes && doc.Parts.All(p => ReferenceEquals(p, part) || !string.Equals(p.RefDes, fresh, StringComparison.OrdinalIgnoreCase)))
                    {
                        var change = new RefDesChange(part, part.RefDes, fresh);
                        part.RefDes = fresh;
                        Annotator.ApplyToLinkedData(doc, new[] { change });
                        count++;
                    }
                }

                if (options.Values && def.ValueKey.Length > 0 && def.Symbol != SymbolKind.PowerRail)
                {
                    var parameters = doc.ResolveParameters(library, part);
                    if (parameters.TryGetValue(def.ValueKey, out var raw) && raw.Length > 0 && rx.IsMatch(raw))
                    {
                        string fresh = Apply(raw);
                        if (fresh != raw)
                        {
                            part.Parameters[def.ValueKey] = fresh;
                            count++;
                        }
                    }
                }

                if (options.Nets && def.Symbol == SymbolKind.PowerRail)
                {
                    var parameters = doc.ResolveParameters(library, part);
                    if (parameters.TryGetValue("net", out var net) && rx.IsMatch(net))
                    {
                        string fresh = Apply(net);
                        if (fresh != net && fresh.Trim().Length > 0)
                        {
                            part.Parameters["net"] = fresh.Trim();
                            count++;
                        }
                    }
                }
            }

            if (options.Nets)
            {
                foreach (var label in doc.Labels)
                {
                    if (!rx.IsMatch(label.Name)) continue;
                    string fresh = Apply(label.Name).Trim();
                    if (fresh.Length > 0 && fresh != label.Name)
                    {
                        label.Name = fresh;
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
