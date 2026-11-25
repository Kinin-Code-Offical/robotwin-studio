using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [variants] One alternative of a design: the same circuit with some parts left off (not fitted), some values changed,
    /// some parts swapped for another (a stronger motor, a 3.3 V regulator in place of the 5 V one). A project keeps its
    /// alternatives side by side; the active one is what is simulated, listed in the bill of materials and put together in
    /// the Montaj. Parts are named by their identity (<see cref="SchematicPart.Uid"/>), so renaming one keeps its overrides.
    /// </summary>
    public sealed class DesignVariant
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>What this alternative is for, in the user's words.</summary>
        public string? Note { get; set; }

        /// <summary>What it changes, per part identity.</summary>
        public Dictionary<string, PartOverride> Parts { get; set; } = new Dictionary<string, PartOverride>(StringComparer.Ordinal);

        public PartOverride? For(SchematicPart part) => Parts.TryGetValue(part.Uid, out var o) && !o.IsEmpty ? o : null;

        /// <summary>The override of a part, made if it has none.</summary>
        public PartOverride Edit(SchematicPart part)
        {
            if (!Parts.TryGetValue(part.Uid, out var o)) Parts[part.Uid] = o = new PartOverride();
            return o;
        }

        /// <summary>Overrides of parts that are no longer in the schematic, and empty ones, dropped.</summary>
        public void Tidy(Schematic schematic)
        {
            var live = new HashSet<string>(schematic.Parts.Select(p => p.Uid), StringComparer.Ordinal);
            foreach (var key in Parts.Where(kv => kv.Value.IsEmpty || !live.Contains(kv.Key)).Select(kv => kv.Key).ToList()) Parts.Remove(key);
        }
    }

    /// <summary>[variants] What one alternative changes about one part.</summary>
    public sealed class PartOverride
    {
        /// <summary>Left off in this alternative (DNP): not simulated, not in the bill of materials or the Montaj.</summary>
        public bool NotFitted { get; set; }

        /// <summary>Another part definition in its place (its pins must match), or null.</summary>
        public string? PartId { get; set; }

        /// <summary>Another preset in its place, or null.</summary>
        public string? PresetId { get; set; }

        /// <summary>Parameter values that differ in this alternative.</summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool IsEmpty => !NotFitted && PartId == null && PresetId == null && Parameters.Count == 0;
    }

    /// <summary>[variants] A schematic as one of its alternatives has it.</summary>
    public static class Variants
    {
        /// <summary>The alternative by name, or null for the design as drawn.</summary>
        public static DesignVariant? Find(Schematic schematic, string? name) =>
            string.IsNullOrEmpty(name) ? null : schematic.Variants.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The schematic with <paramref name="variant"/>'s changes made, a copy (the schematic itself when there are none):
        /// parts not fitted are taken out (their wires and labels stay, so the nets keep their names), swapped parts take
        /// the other definition or preset, values are overridden. Everything that simulates or lists the design reads this.
        /// </summary>
        public static Schematic Resolve(Schematic schematic, DesignVariant? variant)
        {
            if (variant == null || variant.Parts.Values.All(o => o.IsEmpty)) return schematic;
            schematic.EnsurePartUids();
            var copy = Schematic.FromJson(schematic.ToJson());   // its board too is a copy: syncing it cannot take footprints off the real one
            copy.Variants = new List<DesignVariant>();
            copy.ActiveVariant = null;
            copy.Parts.RemoveAll(p => variant.For(p) is { NotFitted: true });
            foreach (var p in copy.Parts)
            {
                if (variant.For(p) is not { } o) continue;
                if (o.PartId != null)
                {
                    p.PartId = o.PartId;
                    p.PresetId = null;
                }

                if (o.PresetId != null) p.PresetId = o.PresetId;
                foreach (var kv in o.Parameters) p.Parameters[kv.Key] = kv.Value;
            }

            return copy;
        }

        /// <summary>The schematic as its active alternative has it (<see cref="Resolve"/>).</summary>
        public static Schematic Active(Schematic schematic) => Resolve(schematic, Find(schematic, schematic.ActiveVariant));

        /// <summary>
        /// What the board's fabrication files need of the active alternative (null: the design as drawn): the parts left
        /// off (out of the bill of materials and the pick-and-place; their pads stay on the board) and the values that differ.
        /// </summary>
        public static Fitting? FittingOf(Schematic schematic, PartLibrary library)
        {
            var variant = Find(schematic, schematic.ActiveVariant);
            if (variant == null) return null;
            var resolved = Resolve(schematic, variant);
            var fitting = new Fitting { Name = variant.Name };
            foreach (var part in schematic.Parts)
            {
                var o = variant.For(part);
                if (o == null) continue;
                if (o.NotFitted)
                {
                    fitting.NotFitted.Add(part.RefDes);
                    continue;
                }

                if (resolved.FindPart(part.RefDes) is not { } now || library.Find(now.PartId) is not { } def) continue;
                var parameters = resolved.ResolveParameters(library, now);
                fitting.Values[part.RefDes] = !string.IsNullOrEmpty(def.ValueKey) && parameters.TryGetValue(def.ValueKey, out var v) ? v : def.Name;
            }

            return fitting;
        }
    }

    /// <summary>[variants] An alternative as the board's fabrication files see it (<see cref="Variants.FittingOf"/>).</summary>
    public sealed class Fitting
    {
        public string Name { get; set; } = string.Empty;
        public HashSet<string> NotFitted { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
