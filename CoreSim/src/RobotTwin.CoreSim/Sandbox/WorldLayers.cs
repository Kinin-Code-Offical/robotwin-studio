using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [layers] Unity's Tags and Layers. A tag is a word that names what a part is to scripts and searches ("engel",
    /// "hedef"); a layer (0 … 31, 0 = "Varsayılan") groups parts for collisions, and the world's collision matrix says
    /// which layers pass through each other (Unity's Layer Collision Matrix). Everything else keeps colliding: the floor,
    /// the terrain, the cables. Stored as the parts' "tag" and "layer" parameters and the world's names and pairs.
    /// </summary>
    public static class WorldLayers
    {
        public const int Count = 32;
        public const string LayerKey = "layer";
        public const string TagKey = "tag";
        public const string DefaultLayer = "Varsayılan";

        public static int LayerOf(WorldObject o) =>
            o.Parameters.TryGetValue(LayerKey, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0 && v < Count ? v : 0;

        public static void SetLayer(WorldObject o, int layer)
        {
            if (layer <= 0 || layer >= Count) o.Parameters.Remove(LayerKey);
            else o.Parameters[LayerKey] = layer.ToString(CultureInfo.InvariantCulture);
        }

        public static string? TagOf(WorldObject o) => o.Parameters.TryGetValue(TagKey, out var t) && !string.IsNullOrWhiteSpace(t) ? t.Trim() : null;

        public static void SetTag(WorldObject o, string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) o.Parameters.Remove(TagKey);
            else o.Parameters[TagKey] = tag!.Trim();
        }

        /// <summary>The tags the world's parts carry, sorted.</summary>
        public static IReadOnlyList<string> Tags(WorldDocument w) =>
            w.Objects.Select(TagOf).Where(t => t != null).Select(t => t!).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(t => t, StringComparer.CurrentCulture).ToList();

        /// <summary>A layer's name ("Varsayılan" for 0; "Katman n" for one without a name).</summary>
        public static string NameOf(WorldDocument w, int layer)
        {
            if (layer <= 0) return DefaultLayer;
            var names = w.LayerNames;
            return names != null && layer < names.Count && !string.IsNullOrWhiteSpace(names[layer]) ? names[layer] : "Katman " + layer.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The layers in use or named: 0 first.</summary>
        public static IReadOnlyList<int> Used(WorldDocument w)
        {
            var set = new SortedSet<int> { 0 };
            if (w.LayerNames != null)
                for (int i = 1; i < w.LayerNames.Count && i < Count; i++)
                    if (!string.IsNullOrWhiteSpace(w.LayerNames[i])) set.Add(i);
            foreach (var o in w.Objects) set.Add(LayerOf(o));
            return set.ToList();
        }

        /// <summary>Names a new layer in the first free place (1 … 31); -1 when all are taken.</summary>
        public static int Add(WorldDocument w, string name)
        {
            var used = new HashSet<int>(Used(w));
            for (int i = 1; i < Count; i++)
            {
                if (used.Contains(i)) continue;
                Rename(w, i, name);
                return i;
            }

            return -1;
        }

        public static void Rename(WorldDocument w, int layer, string name)
        {
            if (layer <= 0 || layer >= Count) return;
            var names = w.LayerNames ??= new List<string> { DefaultLayer };
            while (names.Count <= layer) names.Add(string.Empty);
            names[layer] = name.Trim();
        }

        private static string Pair(int a, int b) =>
            Math.Min(a, b).ToString(CultureInfo.InvariantCulture) + "," + Math.Max(a, b).ToString(CultureInfo.InvariantCulture);

        /// <summary>Whether parts of the two layers collide (all do, until the matrix says otherwise).</summary>
        public static bool Collide(WorldDocument w, int a, int b) => w.LayerIgnores == null || !w.LayerIgnores.Contains(Pair(a, b));

        public static void SetCollide(WorldDocument w, int a, int b, bool collide)
        {
            string key = Pair(a, b);
            if (collide)
            {
                w.LayerIgnores?.Remove(key);
                if (w.LayerIgnores is { Count: 0 }) w.LayerIgnores = null;
            }
            else
            {
                w.LayerIgnores ??= new List<string>();
                if (!w.LayerIgnores.Contains(key)) w.LayerIgnores.Add(key);
            }
        }

        /// <summary>Each layer's mask: bit b set when it collides with layer b.</summary>
        public static uint[] Masks(WorldDocument w)
        {
            var masks = Enumerable.Repeat(uint.MaxValue, Count).ToArray();
            if (w.LayerIgnores == null) return masks;
            foreach (var pair in w.LayerIgnores)
            {
                var parts = pair.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)) continue;
                if (a < 0 || b < 0 || a >= Count || b >= Count) continue;
                masks[a] &= ~(1u << b);
                masks[b] &= ~(1u << a);
            }

            return masks;
        }
    }
}
