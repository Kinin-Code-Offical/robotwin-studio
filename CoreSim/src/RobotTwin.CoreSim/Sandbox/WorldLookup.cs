using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [perf] A world's objects by id, what each is mounted on and whether it is active with its parents, read once: for
    /// code that looks up many at a time (every cable's two ends, every frame). <see cref="WorldDocument.Find"/> walks the
    /// list, and the active test walked the joints and the list again at every level, a new set each time: 600 cables in
    /// a world of 600 objects cost 20 ms a frame. A snapshot: build a new one when the world changes.
    /// </summary>
    public sealed class WorldLookup
    {
        private readonly Dictionary<string, WorldObject> _byId = new Dictionary<string, WorldObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<WorldObject, bool> _active = new Dictionary<WorldObject, bool>();

        public WorldLookup(WorldDocument world)
        {
            // the first of equal ids, as Find returns it
            foreach (var o in world.Objects)
                if (o.Id != null && !_byId.ContainsKey(o.Id))
                    _byId[o.Id] = o;
            // what an object is mounted on: the first joint that names it as its first object, as the active test reads it
            foreach (var j in world.Joints)
                if (j.ObjectA != null && !_parent.ContainsKey(j.ObjectA))
                    _parent[j.ObjectA] = j.ObjectB;
        }

        public WorldObject? Find(string? id) => id != null && _byId.TryGetValue(id, out var o) ? o : null;

        /// <summary>Whether the object and everything it is mounted on are active (<see cref="WorldBuilding.ActiveInHierarchy"/>).</summary>
        public bool ActiveInHierarchy(WorldObject o)
        {
            if (_active.TryGetValue(o, out bool known)) return known;
            var chain = new List<WorldObject>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool active = true;
            for (WorldObject? cur = o; cur != null && seen.Add(cur.Id);)
            {
                if (_active.TryGetValue(cur, out bool above))
                {
                    active = above;
                    break;
                }

                chain.Add(cur);
                if (!WorldBuilding.IsActive(cur))
                {
                    active = false;
                    break;
                }

                cur = _parent.TryGetValue(cur.Id, out var parent) ? Find(parent) : null;
            }

            // everything walked below an inactive object is inactive; on an active chain all are active
            foreach (var c in chain) _active[c] = active;
            return active;
        }
    }
}
