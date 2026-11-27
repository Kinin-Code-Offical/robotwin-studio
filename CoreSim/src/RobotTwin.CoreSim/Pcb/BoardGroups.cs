using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// [group] KiCad's Group / Ungroup: items carrying the same group name are picked and moved as one (a connector with
    /// its mounting holes and label, a sub-circuit's parts). The name is the item's own <c>Group</c>, so it is saved with
    /// it and survives copy and undo.
    /// </summary>
    public static class BoardGroups
    {
        public static string? GroupOf(object item) => item switch
        {
            BoardComponent c => c.Group,
            Track t => t.Group,
            Via v => v.Group,
            Zone z => z.Group,
            MountingHole h => h.Group,
            BoardText x => x.Group,
            BoardDimension d => d.Group,
            BoardGraphic g => g.Group,
            _ => null
        };

        public static void SetGroup(object item, string? group)
        {
            switch (item)
            {
                case BoardComponent c: c.Group = group; break;
                case Track t: t.Group = group; break;
                case Via v: v.Group = group; break;
                case Zone z: z.Group = group; break;
                case MountingHole h: h.Group = group; break;
                case BoardText x: x.Group = group; break;
                case BoardDimension d: d.Group = group; break;
                case BoardGraphic g: g.Group = group; break;
            }
        }

        /// <summary>Every item of the board.</summary>
        public static IEnumerable<object> Items(BoardDocument b) =>
            b.Components.Cast<object>().Concat(b.Tracks).Concat(b.Vias).Concat(b.Zones).Concat(b.MountingHoles).Concat(b.Texts).Concat(b.Dimensions).Concat(b.Graphics);

        /// <summary>The items of a group.</summary>
        public static List<object> Members(BoardDocument b, string group) =>
            Items(b).Where(i => string.Equals(GroupOf(i), group, StringComparison.Ordinal)).ToList();

        /// <summary>The group names on the board.</summary>
        public static IReadOnlyList<string> Names(BoardDocument b) =>
            Items(b).Select(GroupOf).Where(g => g != null).Select(g => g!).Distinct(StringComparer.Ordinal).OrderBy(g => g, StringComparer.Ordinal).ToList();

        /// <summary>Groups the items under a new name ("Grup n"); an item already in a group moves to the new one. Returns the name.</summary>
        public static string Group(BoardDocument b, IEnumerable<object> items)
        {
            var used = new HashSet<string>(Names(b), StringComparer.Ordinal);
            int n = 1;
            while (used.Contains("Grup " + n)) n++;
            string name = "Grup " + n;
            foreach (var i in items) SetGroup(i, name);
            return name;
        }

        /// <summary>Takes the items' groups apart: every member of every group among them leaves it.</summary>
        public static int Ungroup(BoardDocument b, IEnumerable<object> items)
        {
            var names = items.Select(GroupOf).Where(g => g != null).Distinct().ToList();
            int count = 0;
            foreach (var name in names)
                foreach (var m in Members(b, name!))
                {
                    SetGroup(m, null);
                    count++;
                }

            return count;
        }
    }
}
