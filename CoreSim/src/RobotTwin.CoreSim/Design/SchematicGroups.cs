using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [group] KiCad's Group / Ungroup on the sheet: items carrying the same group name (a sub-circuit's parts, wires,
    /// labels and its frame and note) are picked and moved as one. The name is the item's own <c>Group</c>, so it is saved
    /// with it and survives copy and undo; a pasted group gets a name of its own.
    /// </summary>
    public static class SchematicGroups
    {
        public static string? GroupOf(object item) => item switch
        {
            SchematicPart p => p.Group,
            SchematicWire w => w.Group,
            NetLabel l => l.Group,
            NoConnectFlag n => n.Group,
            SchematicNote t => t.Group,
            SchematicShape s => s.Group,
            _ => null
        };

        public static void SetGroup(object item, string? group)
        {
            switch (item)
            {
                case SchematicPart p: p.Group = group; break;
                case SchematicWire w: w.Group = group; break;
                case NetLabel l: l.Group = group; break;
                case NoConnectFlag n: n.Group = group; break;
                case SchematicNote t: t.Group = group; break;
                case SchematicShape s: s.Group = group; break;
            }
        }

        /// <summary>Every item of the sheet.</summary>
        public static IEnumerable<object> Items(Schematic doc) => SchematicEditing.AllItems(doc);

        public static List<object> Members(Schematic doc, string group) =>
            Items(doc).Where(i => string.Equals(GroupOf(i), group, StringComparison.Ordinal)).ToList();

        public static IReadOnlyList<string> Names(Schematic doc) =>
            Items(doc).Select(GroupOf).Where(g => g != null).Select(g => g!).Distinct(StringComparer.Ordinal).OrderBy(g => g, StringComparer.Ordinal).ToList();

        private static string FreeName(ISet<string> used)
        {
            int n = 1;
            while (used.Contains("Grup " + n)) n++;
            return "Grup " + n;
        }

        /// <summary>Groups the items under a new name ("Grup n"); an item already in a group moves to the new one. Returns the name.</summary>
        public static string Group(Schematic doc, IEnumerable<object> items)
        {
            string name = FreeName(new HashSet<string>(Names(doc), StringComparer.Ordinal));
            foreach (var i in items) SetGroup(i, name);
            return name;
        }

        /// <summary>Takes the items' groups apart: every member of every group among them leaves it.</summary>
        public static int Ungroup(Schematic doc, IEnumerable<object> items)
        {
            int count = 0;
            foreach (var name in items.Select(GroupOf).Where(g => g != null).Distinct().ToList())
                foreach (var m in Members(doc, name!))
                {
                    SetGroup(m, null);
                    count++;
                }

            return count;
        }

        /// <summary>Gives the groups among newly added items fresh names, so they do not join the groups they were copied from.</summary>
        public static void Renew(Schematic doc, IReadOnlyCollection<object> added)
        {
            var addedSet = new HashSet<object>(added);
            var used = new HashSet<string>(Items(doc).Where(i => !addedSet.Contains(i)).Select(GroupOf).Where(g => g != null).Select(g => g!), StringComparer.Ordinal);
            var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in added)
            {
                if (GroupOf(item) is not { } old) continue;
                if (!renamed.TryGetValue(old, out var name))
                {
                    name = FreeName(used);
                    used.Add(name);
                    renamed[old] = name;
                }

                SetGroup(item, name);
            }
        }
    }
}
