using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    // [terrain] World-editor helpers: props on the terrain, snapping, hide/lock/group flags.

    /// <summary>
    /// Editing helpers of the world builder. Editor flags live in <see cref="WorldObject.Parameters"/>
    /// ("hidden", "locked", "group") so the file format does not change.
    /// </summary>
    public static class WorldBuilding
    {
        public const string HiddenKey = "hidden";
        public const string LockedKey = "locked";
        public const string GroupKey = "group";

        /// <summary>[active] Unity's inactive GameObject: off in the run and out of the view, kept in the file.</summary>
        public const string InactiveKey = "inactive";

        /// <summary>Ground height at (x, y): the terrain, or the floor z = 0.</summary>
        public static double GroundZ(WorldDocument w, double x, double y) => w.Terrain?.Height(x, y) ?? 0.0;

        public static Vec3 GroundNormal(WorldDocument w, double x, double y) => w.Terrain?.Normal(x, y) ?? Vec3.UnitZ;

        public static bool IsProp(WorldObject o) => o.Kind == WorldObjectKind.Model && o.Parameters.ContainsKey(TerrainProps.Key);

        public static PropDefinition? PropOf(WorldObject o) => o.Parameters.TryGetValue(TerrainProps.Key, out var id) ? TerrainProps.Find(id) : null;

        internal static int SeedOf(WorldObject o) =>
            o.Parameters.TryGetValue("seed", out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 1;

        /// <summary>Adds a prop standing on the ground at (x, y); optionally tilted to the ground normal.</summary>
        public static WorldObject AddProp(WorldDocument w, string propId, double x, double y, double yawDeg = 0, bool alignToNormal = false, Vec3? size = null)
        {
            var def = TerrainProps.Find(propId) ?? TerrainProps.All[0];
            var s = size ?? new Vec3(def.SizeX, def.SizeY, def.SizeZ);
            int seed = w.Objects.Count * 7 + 3;
            var mesh = TerrainProps.BuildMesh(def.Id, s, seed);
            var o = new WorldObject
            {
                Id = w.NextId("O"),
                Name = w.UniqueName(def.Name),
                Kind = WorldObjectKind.Model,
                Mesh = mesh.ToEmbedded(),
                MeshName = def.Name,
                Material = def.Material,
                IsStatic = !def.Dynamic,
                MassKg = def.MassKg > 0 ? def.MassKg : (double?)null,
                Pose = new WorldPose { X = x, Y = y, Yaw = yawDeg }
            };
            o.Parameters[TerrainProps.Key] = def.Id;
            o.Parameters["seed"] = seed.ToString(CultureInfo.InvariantCulture);
            TerrainProps.StoreSize(o.Parameters, s);
            PlaceOnGround(w, o, alignToNormal);
            w.Objects.Add(o);
            return o;
        }

        /// <summary>Rebuilds a prop's mesh for a new size (m).</summary>
        public static void ResizeProp(WorldObject o, Vec3 size)
        {
            var def = PropOf(o);
            if (def == null) return;
            size = new Vec3(Math.Max(0.005, size.X), Math.Max(0.005, size.Y), Math.Max(0.002, size.Z));
            TerrainProps.StoreSize(o.Parameters, size);
            o.Mesh = TerrainProps.BuildMesh(def.Id, size, SeedOf(o)).ToEmbedded();
        }

        /// <summary>
        /// Puts an object's base on the ground under its pose point. With <paramref name="alignToNormal"/>
        /// it is also tilted to the local slope (roll/pitch), keeping its yaw.
        /// </summary>
        public static void PlaceOnGround(WorldDocument w, WorldObject o, bool alignToNormal)
        {
            double x = o.Pose.X, y = o.Pose.Y;
            double z = GroundZ(w, x, y);
            if (alignToNormal && w.Terrain != null)
            {
                var n = GroundNormal(w, x, y);
                // Tilt in the yaw frame: pitch about local y lifts +x, roll about local x lifts +y.
                double yaw = o.Pose.Yaw * Math.PI / 180, c = Math.Cos(yaw), s = Math.Sin(yaw);
                double nxLocal = c * n.X + s * n.Y, nyLocal = -s * n.X + c * n.Y;
                o.Pose.Pitch = Math.Atan2(nxLocal, n.Z) * 180 / Math.PI;
                o.Pose.Roll = -Math.Atan2(nyLocal, Math.Sqrt(nxLocal * nxLocal + n.Z * n.Z)) * 180 / Math.PI;
            }
            else if (alignToNormal)
            {
                o.Pose.Roll = 0;
                o.Pose.Pitch = 0;
            }

            o.Pose.Z = z;
        }

        // ================================================================== editor flags

        private static bool Flag(WorldObject o, string key) => o.Parameters.TryGetValue(key, out var v) && v == "true";

        private static void SetFlag(WorldObject o, string key, bool on)
        {
            if (on) o.Parameters[key] = "true";
            else o.Parameters.Remove(key);
        }

        public static bool IsHidden(WorldObject o) => Flag(o, HiddenKey);
        public static void SetHidden(WorldObject o, bool hidden) => SetFlag(o, HiddenKey, hidden);
        public static bool IsLocked(WorldObject o) => Flag(o, LockedKey);

        /// <summary>[active] Unity's activeSelf: the object's own switch.</summary>
        public static bool IsActive(WorldObject o) => !Flag(o, InactiveKey);

        public static void SetActive(WorldObject o, bool active) => SetFlag(o, InactiveKey, !active);

        /// <summary>
        /// [active] Unity's activeInHierarchy: the object and everything it is mounted on (a joint's ObjectA rides on its
        /// ObjectB) are active; a part on a switched-off base is off with it.
        /// </summary>
        public static bool ActiveInHierarchy(WorldDocument world, WorldObject o)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (WorldObject? cur = o; cur != null && seen.Add(cur.Id);)
            {
                if (!IsActive(cur)) return false;
                string? parent = world.Joints.FirstOrDefault(j => string.Equals(j.ObjectA, cur.Id, StringComparison.OrdinalIgnoreCase))?.ObjectB;
                cur = world.Find(parent);
            }

            return true;
        }
        public static void SetLocked(WorldObject o, bool locked) => SetFlag(o, LockedKey, locked);

        public static string? GroupOf(WorldObject o) => o.Parameters.TryGetValue(GroupKey, out var g) && !string.IsNullOrWhiteSpace(g) ? g : null;

        public static void SetGroup(WorldObject o, string? group)
        {
            if (string.IsNullOrWhiteSpace(group)) o.Parameters.Remove(GroupKey);
            else o.Parameters[GroupKey] = group!.Trim();
        }

        /// <summary>Other members of the object's group.</summary>
        public static List<WorldObject> GroupMates(WorldDocument w, WorldObject o)
        {
            var g = GroupOf(o);
            if (g == null) return new List<WorldObject>();
            return w.Objects.Where(x => x != o && string.Equals(GroupOf(x), g, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>Next free group name ("Grup 1", "Grup 2" …).</summary>
        public static string NewGroupName(WorldDocument w)
        {
            for (int i = 1; ; i++)
            {
                string n = "Grup " + i.ToString(CultureInfo.InvariantCulture);
                if (w.Objects.All(o => !string.Equals(GroupOf(o), n, StringComparison.OrdinalIgnoreCase))) return n;
            }
        }
    }
}
