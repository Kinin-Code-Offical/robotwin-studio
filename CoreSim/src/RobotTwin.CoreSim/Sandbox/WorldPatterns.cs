using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [pattern] SolidWorks' and Fusion's component patterns: copies of the chosen parts (with what is mounted on them) in a
    /// row by a step, or round an axis through a point. The count includes the original; a full turn shares 360° among
    /// them, a partial one puts the last copy at the end of the angle. A copy can be fixed where its original is fixed
    /// (screws on a plate stay screwed on), so the pattern needs no further joints.
    /// </summary>
    public static class WorldPatterns
    {
        /// <summary>A row: copies 1 … count−1 moved by step × i. Returns every object made.</summary>
        public static List<WorldObject> Linear(WorldDocument world, IReadOnlyList<string> ids, int count, Vec3 step, bool keepMount = true) =>
            Make(world, ids, count, (copy, i) => { }, i => step * i, keepMount);

        /// <summary>Round an axis through a pivot: copy i turned by i × (360° / count), or by i × angle / (count − 1) for less than a full turn.</summary>
        public static List<WorldObject> Circular(WorldDocument world, IReadOnlyList<string> ids, int count, double totalDegrees, Vec3 axis, Vec3 pivot, bool keepMount = true)
        {
            bool full = Math.Abs(Math.Abs(totalDegrees) - 360) < 1e-9 || Math.Abs(totalDegrees) > 360;
            double each = count <= 1 ? 0 : full ? 360.0 / count : totalDegrees / (count - 1);
            var unit = axis.Length > 1e-12 ? axis.Normalized() : Vec3.UnitZ;
            return Make(world, ids, count, (root, i) =>
            {
                var turn = Quat.FromAxisAngle(unit, each * i * Math.PI / 180);
                world.RotateAssembly(root, turn, pivot);
            }, _ => Vec3.Zero, keepMount);
        }

        private static List<WorldObject> Make(WorldDocument world, IReadOnlyList<string> ids, int count, Action<WorldObject, int> place, Func<int, Vec3> offset, bool keepMount)
        {
            var made = new List<WorldObject>();
            if (count < 2 || ids.Count == 0) return made;
            // the chosen parts not mounted on another chosen one: each carries its own riders
            var chosen = ids.Where(id => world.Find(id) != null).ToList();
            var roots = chosen.Where(id => !chosen.Any(other => !string.Equals(other, id, StringComparison.OrdinalIgnoreCase)
                                                                && world.MountedOn(other).Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))).ToList();
            var fragment = WorldFragment.Take(world, roots);
            var inside = new HashSet<string>(fragment.Objects.Select(o => o.Id), StringComparer.OrdinalIgnoreCase);
            // where each root is fixed outside the pattern (a plate, the ground): its copies are fixed there too
            var holds = keepMount
                ? roots.Select(r => (Root: r, Joint: world.Joints.FirstOrDefault(j => string.Equals(j.ObjectA, r, StringComparison.OrdinalIgnoreCase)
                                                                                       && j.Kind == WorldJointKind.Fixed && string.IsNullOrEmpty(j.PortB)
                                                                                       && (j.ObjectB == null || !inside.Contains(j.ObjectB)))))
                       .Where(x => x.Joint != null).ToDictionary(x => x.Root, x => x.Joint!, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, WorldJoint>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < count; i++)
            {
                int before = world.Objects.Count;
                fragment.PasteInto(world, offset(i));
                var copies = world.Objects.Skip(before).ToList();   // every copy, in the fragment's order (the paste returns only roots)
                made.AddRange(copies);
                for (int k = 0; k < fragment.Objects.Count && k < copies.Count; k++)
                {
                    string source = fragment.Objects[k].Id;
                    if (!roots.Contains(source, StringComparer.OrdinalIgnoreCase)) continue;
                    var copy = copies[k];
                    place(copy, i);
                    if (holds.TryGetValue(source, out var hold))
                        world.Joints.Add(new WorldJoint
                        {
                            Id = world.NextId("J"),
                            Kind = WorldJointKind.Fixed,
                            ObjectA = copy.Id,
                            ObjectB = hold.ObjectB,
                            AnchorX = copy.Pose.X,
                            AnchorY = copy.Pose.Y,
                            AnchorZ = copy.Pose.Z,
                            Fastening = hold.Fastening,
                            BreakForce = hold.BreakForce,
                            BreakTorque = hold.BreakTorque
                        });
                }
            }

            return made;
        }
    }
}
