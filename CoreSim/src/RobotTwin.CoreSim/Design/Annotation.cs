using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>Order in which "Numaralandır" walks the sheet.</summary>
    public enum AnnotationOrder
    {
        /// <summary>Row by row: top to bottom, then left to right (KiCad "sort by Y position").</summary>
        Rows,

        /// <summary>Column by column: left to right, then top to bottom (KiCad "sort by X position").</summary>
        Columns
    }

    public sealed class AnnotationOptions
    {
        public AnnotationOrder Order { get; set; } = AnnotationOrder.Rows;

        /// <summary>Renumber every part (true) or only unannotated ("R?") and duplicate designators (false).</summary>
        public bool ResetExisting { get; set; }

        /// <summary>Also number power/ground symbols (#PWR, #GND).</summary>
        public bool IncludePowerSymbols { get; set; } = true;
    }

    public sealed class RefDesChange
    {
        public RefDesChange(SchematicPart part, string oldRefDes, string newRefDes)
        {
            Part = part;
            OldRefDes = oldRefDes;
            NewRefDes = newRefDes;
        }

        public SchematicPart Part { get; }
        public string OldRefDes { get; }
        public string NewRefDes { get; }

        public override string ToString() => OldRefDes + " → " + NewRefDes;
    }

    /// <summary>Reference designator handling: parsing, auto-annotation and conflict resolution.</summary>
    public static class Annotator
    {
        /// <summary>"R12" → "R", "#PWR3" → "#PWR", "R?" → "R".</summary>
        public static string Prefix(string refDes)
        {
            int i = 0;
            while (i < refDes.Length && !char.IsDigit(refDes[i]) && refDes[i] != '?') i++;
            return refDes.Substring(0, i);
        }

        /// <summary>The number of an annotated designator ("R12" → 12), null for "R?" or "R".</summary>
        public static int? Number(string refDes)
        {
            string prefix = Prefix(refDes);
            string rest = refDes.Substring(prefix.Length);
            return rest.Length > 0 && rest.All(char.IsDigit) && int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
        }

        public static bool IsAnnotated(string refDes) => Number(refDes) != null;

        public static bool IsPowerSymbol(string refDes) => refDes.StartsWith("#", StringComparison.Ordinal);

        /// <summary>Natural order for designators: prefix, then number ("R2" before "R10").</summary>
        public static int Compare(string a, string b)
        {
            int c = string.Compare(Prefix(a), Prefix(b), StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            int na = Number(a) ?? int.MaxValue, nb = Number(b) ?? int.MaxValue;
            return na != nb ? na.CompareTo(nb) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static readonly IComparer<string> NaturalComparer = Comparer<string>.Create(Compare);

        /// <summary>Groups of parts that share a designator (case-insensitive).</summary>
        public static List<IGrouping<string, SchematicPart>> Duplicates(Schematic doc) =>
            doc.Parts.Where(p => IsAnnotated(p.RefDes)).GroupBy(p => p.RefDes, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();

        private static IEnumerable<SchematicPart> InOrder(IEnumerable<SchematicPart> parts, AnnotationOrder order) =>
            order == AnnotationOrder.Rows
                ? parts.OrderBy(p => Math.Round(p.Position.Y, 3)).ThenBy(p => Math.Round(p.Position.X, 3))
                : parts.OrderBy(p => Math.Round(p.Position.X, 3)).ThenBy(p => Math.Round(p.Position.Y, 3));

        /// <summary>
        /// Numbers the sheet like KiCad's annotator: per prefix, in position order, using the
        /// lowest free numbers. Without <see cref="AnnotationOptions.ResetExisting"/>, valid unique
        /// designators are kept and only "R?"-style or duplicate ones are renumbered.
        /// Returns the changes (already applied to the parts).
        /// </summary>
        public static List<RefDesChange> Annotate(Schematic doc, PartLibrary library, AnnotationOptions? options = null)
        {
            options ??= new AnnotationOptions();
            var changes = new List<RefDesChange>();
            var byPrefix = doc.Parts
                .Where(p => options.IncludePowerSymbols || !IsPowerSymbol(p.RefDes))
                .GroupBy(p => PrefixFor(p, library), StringComparer.OrdinalIgnoreCase);
            foreach (var group in byPrefix)
            {
                string prefix = group.Key;
                var ordered = InOrder(group, options.Order).ToList();
                var used = new HashSet<int>();
                var renumber = new List<SchematicPart>();
                if (options.ResetExisting)
                {
                    renumber.AddRange(ordered);
                }
                else
                {
                    foreach (var p in ordered)
                    {
                        int? n = Number(p.RefDes);
                        bool samePrefix = string.Equals(Prefix(p.RefDes), prefix, StringComparison.OrdinalIgnoreCase);
                        if (n != null && samePrefix && used.Add(n.Value)) continue;
                        renumber.Add(p);
                    }
                }

                // Designators of other prefixes are never touched, but a part being renumbered must not take one.
                var taken = new HashSet<string>(doc.Parts.Where(p => !renumber.Contains(p)).Select(p => p.RefDes), StringComparer.OrdinalIgnoreCase);
                int next = 1;
                foreach (var p in renumber)
                {
                    while (used.Contains(next) || taken.Contains(prefix + next.ToString(CultureInfo.InvariantCulture))) next++;
                    used.Add(next);
                    string fresh = prefix + next.ToString(CultureInfo.InvariantCulture);
                    if (!string.Equals(fresh, p.RefDes, StringComparison.Ordinal)) changes.Add(new RefDesChange(p, p.RefDes, fresh));
                    p.RefDes = fresh;
                }
            }

            return changes;
        }

        /// <summary>The prefix a part should use: its current one, or the library's when it has none.</summary>
        private static string PrefixFor(SchematicPart part, PartLibrary library)
        {
            string prefix = Prefix(part.RefDes);
            if (prefix.Length > 0) return prefix;
            return library.Find(part.PartId)?.RefPrefix ?? "U";
        }

        /// <summary>
        /// Gives newly added parts (paste, duplicate) designators that do not clash with the rest
        /// of the sheet or each other. Unique annotated designators are kept as they are.
        /// </summary>
        public static List<RefDesChange> ResolveConflicts(Schematic doc, PartLibrary library, IReadOnlyCollection<SchematicPart> newcomers)
        {
            var changes = new List<RefDesChange>();
            var taken = new HashSet<string>(doc.Parts.Where(p => !newcomers.Contains(p)).Select(p => p.RefDes), StringComparer.OrdinalIgnoreCase);
            foreach (var part in InOrder(newcomers, AnnotationOrder.Rows))
            {
                if (IsAnnotated(part.RefDes) && taken.Add(part.RefDes)) continue;
                string prefix = PrefixFor(part, library);
                int n = 1;
                while (taken.Contains(prefix + n.ToString(CultureInfo.InvariantCulture))) n++;
                string fresh = prefix + n.ToString(CultureInfo.InvariantCulture);
                taken.Add(fresh);
                changes.Add(new RefDesChange(part, part.RefDes, fresh));
                part.RefDes = fresh;
            }

            return changes;
        }

        /// <summary>
        /// Carries designator changes into everything that refers to parts by name: the PCB
        /// components and the mechanical assembly mounts. Swaps (R1 ↔ R2) are handled.
        /// </summary>
        public static void ApplyToLinkedData(Schematic doc, IReadOnlyCollection<RefDesChange> changes)
        {
            if (changes.Count == 0) return;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in changes) map[c.OldRefDes] = c.NewRefDes;
            string Map(string r) => map.TryGetValue(r, out var n) ? n : r;

            if (doc.Board != null)
            {
                foreach (var comp in doc.Board.Components) comp.RefDes = Map(comp.RefDes);
            }

            if (doc.Assembly != null)
            {
                foreach (var m in doc.Assembly.Motors) m.RefDes = Map(m.RefDes);
                foreach (var s in doc.Assembly.Sensors) s.RefDes = Map(s.RefDes);
                foreach (var m in doc.Assembly.Modules) m.RefDes = Map(m.RefDes);
            }
        }
    }
}
