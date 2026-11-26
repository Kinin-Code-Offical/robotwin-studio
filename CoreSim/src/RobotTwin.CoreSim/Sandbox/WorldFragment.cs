using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [clipboard] A piece of a world to copy, cut, paste or duplicate, as Unity's hierarchy does with an object and its
    /// children: the chosen objects with everything mounted on them, the joints among them (a joint to something left out
    /// is dropped: the copy stands free), the cables whose both ends are inside, and the mates and couplings that only
    /// involve them. Pasting gives every object, joint and cable a new id and a unique name, and moves it by an offset.
    /// </summary>
    public sealed class WorldFragment
    {
        public List<WorldObject> Objects { get; set; } = new List<WorldObject>();
        public List<WorldJoint> Joints { get; set; } = new List<WorldJoint>();
        public List<WorldCable> Cables { get; set; } = new List<WorldCable>();
        public List<WorldMate> Mates { get; set; } = new List<WorldMate>();
        public List<WorldCoupling> Couplings { get; set; } = new List<WorldCoupling>();

        public bool IsEmpty => Objects.Count == 0;

        private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, RobotWinFormats.Json), RobotWinFormats.Json)!;

        /// <summary>The chosen objects (and, unless told not to, what is mounted on them) with what lies among them.</summary>
        public static WorldFragment Take(WorldDocument world, IEnumerable<string> ids, bool withMounted = true)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<WorldObject>();
            void Add(WorldObject o)
            {
                if (set.Add(o.Id)) order.Add(o);
            }

            foreach (var id in ids)
            {
                if (world.Find(id) is not { } o) continue;
                Add(o);
                if (withMounted)
                    foreach (var m in world.MountedOn(o.Id)) Add(m);
            }

            var f = new WorldFragment();
            f.Objects.AddRange(order.Select(Copy));
            var joints = world.Joints.Where(j => set.Contains(j.ObjectA) && (string.IsNullOrEmpty(j.ObjectB) || set.Contains(j.ObjectB!))).ToList();
            f.Joints.AddRange(joints.Select(Copy));
            f.Cables.AddRange(world.Cables.Where(c => set.Contains(c.A.ObjectId) && set.Contains(c.B.ObjectId)).Select(Copy));
            if (world.Mates != null)
                f.Mates.AddRange(world.Mates.Where(m => set.Contains(m.A.Object) && set.Contains(m.B.Object)).Select(m => m.Clone()));
            var jointIds = new HashSet<string>(joints.Select(j => j.Id), StringComparer.OrdinalIgnoreCase);
            if (world.Couplings != null)
                f.Couplings.AddRange(world.Couplings.Where(c => jointIds.Contains(c.JointA) && jointIds.Contains(c.JointB)).Select(Copy));
            return f;
        }

        public string ToJson() => JsonSerializer.Serialize(this, RobotWinFormats.Json);

        public static WorldFragment? FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<WorldFragment>(json, RobotWinFormats.Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Adds a copy to the world, moved by an offset (world metres): new ids, unique names, the joints, cables, mates
        /// and couplings pointing at the copies. Returns the copied objects whose parent was not copied (the new roots).
        /// </summary>
        public List<WorldObject> PasteInto(WorldDocument world, Vec3 offset)
        {
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var made = new List<WorldObject>();
            foreach (var src in Objects)
            {
                var o = Copy(src);
                o.Id = world.NextId("O");
                o.Name = world.UniqueName(src.Name);
                o.Pose.X += offset.X;
                o.Pose.Y += offset.Y;
                o.Pose.Z += offset.Z;
                ids[src.Id] = o.Id;
                world.Objects.Add(o);
                made.Add(o);
            }

            var jointIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in Joints)
            {
                var j = Copy(src);
                j.Id = world.NextId("J");
                jointIds[src.Id] = j.Id;
                j.ObjectA = ids[src.ObjectA];
                if (!string.IsNullOrEmpty(src.ObjectB)) j.ObjectB = ids[src.ObjectB!];
                if (j.AnchorX != 0 || j.AnchorY != 0 || j.AnchorZ != 0)
                {
                    j.AnchorX += offset.X;
                    j.AnchorY += offset.Y;
                    j.AnchorZ += offset.Z;
                }

                world.Joints.Add(j);
            }

            foreach (var src in Cables)
            {
                var c = Copy(src);
                c.Id = world.NextId("C");
                c.A.ObjectId = ids[src.A.ObjectId];
                c.B.ObjectId = ids[src.B.ObjectId];
                world.Cables.Add(c);
            }

            if (Mates.Count > 0)
            {
                world.Mates ??= new List<WorldMate>();
                var used = new HashSet<string>(world.Mates.Select(m => m.Id), StringComparer.Ordinal);
                int n = 1;
                foreach (var src in Mates)
                {
                    var m = src.Clone();
                    while (used.Contains("MT" + n)) n++;
                    m.Id = "MT" + n;
                    used.Add(m.Id);
                    m.A.Object = ids[src.A.Object];
                    m.B.Object = ids[src.B.Object];
                    world.Mates.Add(m);
                }
            }

            if (Couplings.Count > 0)
            {
                world.Couplings ??= new List<WorldCoupling>();
                var used = new HashSet<string>(world.Couplings.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
                int n = 1;
                foreach (var src in Couplings)
                {
                    var c = Copy(src);
                    while (used.Contains("K" + n)) n++;
                    c.Id = "K" + n;
                    used.Add(c.Id);
                    c.JointA = jointIds[src.JointA];
                    c.JointB = jointIds[src.JointB];
                    world.Couplings.Add(c);
                }
            }

            var mounted = new HashSet<string>(Joints.Where(j => !string.IsNullOrEmpty(j.ObjectB)).Select(j => ids[j.ObjectA]), StringComparer.OrdinalIgnoreCase);
            return made.Where(o => !mounted.Contains(o.Id)).ToList();
        }

        /// <summary>Removes the chosen objects and everything mounted on them (a cut, a delete with the children).</summary>
        public static void RemoveFrom(WorldDocument world, IEnumerable<string> ids)
        {
            var all = new List<WorldObject>();
            foreach (var id in ids)
            {
                if (world.Find(id) is not { } o) continue;
                all.Add(o);
                all.AddRange(world.MountedOn(o.Id));
            }

            foreach (var o in all.Distinct().ToList())
                if (world.Objects.Contains(o))
                    world.Remove(o);
        }
    }
}
