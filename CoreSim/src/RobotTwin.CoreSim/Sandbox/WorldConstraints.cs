using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [constraints] Unity's constraint components: Aim (look at a target), Position, Rotation and Parent constraints.
    /// A part carrying one is placed from its source every time the world is edited: moved along when the source is
    /// moved, turned to keep looking at a target, everything mounted on it coming along (joint anchors too). Like Unity's
    /// "Activate", each keeps the offset the part had from its source when it was added. They lay a world out; a run is
    /// the physics, which moves parts only by forces and joints, so a run starts from where they put things and does
    /// not move a part by them (a part that must follow another while running is mounted on it, or mated). Unity's
    /// scale constraint has no counterpart: a part's size is its real size.
    /// </summary>
    public static class WorldConstraints
    {
        /// <summary>Aim: the target's id; <see cref="AimAxisKey"/> the part's own axis that points at it ("+X" … "-Z").</summary>
        public const string AimKey = "aim";
        public const string AimAxisKey = "aim.axis";

        /// <summary>Position: the source's id; the offset (m, world) and which axes follow ("xyz").</summary>
        public const string PositionKey = "follow.pos";
        public const string PositionOffsetKey = "follow.pos.offset";
        public const string PositionAxesKey = "follow.pos.axes";

        /// <summary>Rotation: the source's id; the offset turn after the source's (roll, pitch, yaw in degrees).</summary>
        public const string RotationKey = "follow.rot";
        public const string RotationOffsetKey = "follow.rot.offset";

        /// <summary>Parent: the source's id; where the part sits in the source's frame ("x,y,z;roll,pitch,yaw", m and degrees).</summary>
        public const string ParentKey = "follow.parent";
        public const string ParentRestKey = "follow.parent.rest";

        /// <summary>Every constraint, in the order they are applied (place first, then turn, then aim).</summary>
        public static readonly string[] Keys = { ParentKey, PositionKey, RotationKey, AimKey };

        public static IReadOnlyList<string> KeysOf(string key) => key switch
        {
            AimKey => new[] { AimKey, AimAxisKey },
            PositionKey => new[] { PositionKey, PositionOffsetKey, PositionAxesKey },
            RotationKey => new[] { RotationKey, RotationOffsetKey },
            ParentKey => new[] { ParentKey, ParentRestKey },
            _ => new[] { key }
        };

        public static readonly string[] Axes = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };

        public static Vec3 AxisVector(string? axis) => (axis ?? "+X").Trim().ToUpperInvariant() switch
        {
            "-X" => new Vec3(-1, 0, 0),
            "+Y" or "Y" => new Vec3(0, 1, 0),
            "-Y" => new Vec3(0, -1, 0),
            "+Z" or "Z" => new Vec3(0, 0, 1),
            "-Z" => new Vec3(0, 0, -1),
            _ => new Vec3(1, 0, 0)
        };

        // ------------------------------------------------------------------ adding (Unity's Activate: the present offset is kept)

        public static void AddAim(WorldObject o, WorldObject target, string axis = "+X")
        {
            o.Parameters[AimKey] = target.Id;
            o.Parameters[AimAxisKey] = axis;
        }

        public static void AddPosition(WorldObject o, WorldObject source, string axes = "xyz")
        {
            o.Parameters[PositionKey] = source.Id;
            o.Parameters[PositionOffsetKey] = Text(o.Pose.Position - source.Pose.Position);
            o.Parameters[PositionAxesKey] = axes;
        }

        public static void AddRotation(WorldObject o, WorldObject source)
        {
            var offset = (source.Pose.Orientation.Conjugate * o.Pose.Orientation).Normalized();
            o.Parameters[RotationKey] = source.Id;
            o.Parameters[RotationOffsetKey] = Text(Degrees(offset));
        }

        public static void AddParent(WorldObject o, WorldObject source)
        {
            var q = source.Pose.Orientation;
            var local = q.InverseRotate(o.Pose.Position - source.Pose.Position);
            var turn = (q.Conjugate * o.Pose.Orientation).Normalized();
            o.Parameters[ParentKey] = source.Id;
            o.Parameters[ParentRestKey] = Text(local) + ";" + Text(Degrees(turn));
        }

        /// <summary>Whether a constraint can point at that source: another part, not one that leans on this part already.</summary>
        public static bool CanUse(WorldDocument w, WorldObject o, WorldObject source) =>
            source.Id != o.Id && !w.MountedOn(o.Id).Any(m => m.Id == source.Id) && !DependsOn(w, source, o.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        private static bool DependsOn(WorldDocument w, WorldObject o, string id, HashSet<string> seen)
        {
            if (!seen.Add(o.Id)) return false;
            foreach (var key in Keys)
            {
                if (!o.Parameters.TryGetValue(key, out var src)) continue;
                if (string.Equals(src, id, StringComparison.OrdinalIgnoreCase)) return true;
                if (w.Find(src) is { } s && DependsOn(w, s, id, seen)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ applying

        /// <summary>
        /// Places every constrained part from its sources (a few passes, so a chain settles); returns how many moved. A
        /// constraint whose source is gone or switched off does nothing.
        /// </summary>
        public static int Apply(WorldDocument w)
        {
            var parts = w.Objects.Where(o => Keys.Any(o.Parameters.ContainsKey)).ToList();
            if (parts.Count == 0) return 0;
            var moved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int pass = 0; pass < 4; pass++)
            {
                bool any = false;
                foreach (var o in parts)
                {
                    var (p, q) = Target(w, o);
                    if (Place(w, o, p, q))
                    {
                        any = true;
                        moved.Add(o.Id);
                    }
                }

                if (!any) break;
            }

            return moved.Count;
        }

        /// <summary>Where a part's constraints put it (its present place for what they leave alone).</summary>
        public static (Vec3 Position, Quat Orientation) Target(WorldDocument w, WorldObject o)
        {
            var p = o.Pose.Position;
            var q = o.Pose.Orientation;
            if (Source(w, o, ParentKey) is { } parent)
            {
                var (local, turn) = Rest(o.Parameters.TryGetValue(ParentRestKey, out var rest) ? rest : null);
                p = parent.Pose.ToWorld(local);
                q = (parent.Pose.Orientation * turn).Normalized();
            }

            if (Source(w, o, PositionKey) is { } follow)
            {
                var at = follow.Pose.Position + (TryVector(o.Parameters.TryGetValue(PositionOffsetKey, out var off) ? off : null) ?? Vec3.Zero);
                string axes = o.Parameters.TryGetValue(PositionAxesKey, out var a) ? a.ToLowerInvariant() : "xyz";
                p = new Vec3(axes.Contains('x') ? at.X : p.X, axes.Contains('y') ? at.Y : p.Y, axes.Contains('z') ? at.Z : p.Z);
            }

            if (Source(w, o, RotationKey) is { } turnWith)
            {
                var offset = Turn(TryVector(o.Parameters.TryGetValue(RotationOffsetKey, out var ro) ? ro : null) ?? Vec3.Zero);
                q = (turnWith.Pose.Orientation * offset).Normalized();
            }

            if (Source(w, o, AimKey) is { } target)
            {
                var toward = target.Pose.Position - p;
                if (toward.Length > 1e-9) q = Aim(q, AxisVector(o.Parameters.TryGetValue(AimAxisKey, out var ax) ? ax : null), toward.Normalized());
            }

            return (p, q);
        }

        /// <summary>
        /// The turn that points the part's own <paramref name="axis"/> along <paramref name="toward"/> and keeps it as
        /// level as it can (Unity's aim with the scene's up as world up): its own up (Z, or X when Z is the aim axis) as
        /// near the world's up as the aim allows.
        /// </summary>
        public static Quat Aim(Quat current, Vec3 axis, Vec3 toward)
        {
            var a = axis.Normalized();
            var f = toward.Normalized();
            var swing = FromTo(a, f);
            var localUp = Math.Abs(a.Z) > 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 0, 1);
            var up = swing.Rotate(localUp);
            var worldUp = Math.Abs(f.Z) > 0.999 ? new Vec3(1, 0, 0) : new Vec3(0, 0, 1);
            Vec3 Flat(Vec3 v) => v - f * v.Dot(f);
            var u1 = Flat(up);
            var u2 = Flat(worldUp);
            if (u1.Length < 1e-9 || u2.Length < 1e-9) return swing;
            u1 = u1.Normalized();
            u2 = u2.Normalized();
            double angle = Math.Atan2(u1.Cross(u2).Dot(f), u1.Dot(u2));
            return (Quat.FromAxisAngle(f, angle) * swing).Normalized();
        }

        private static Quat FromTo(Vec3 a, Vec3 b)
        {
            var axis = a.Cross(b);
            double sin = axis.Length, cos = a.Dot(b);
            if (sin < 1e-12)
            {
                if (cos > 0) return Quat.Identity;
                var any = Math.Abs(a.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
                return Quat.FromAxisAngle(a.Cross(any).Normalized(), Math.PI);
            }

            return Quat.FromAxisAngle(axis / sin, Math.Atan2(sin, cos));
        }

        /// <summary>Moves and turns a part (and what is mounted on it) to a pose; false when it is there already.</summary>
        private static bool Place(WorldDocument w, WorldObject o, Vec3 p, Quat q)
        {
            var cur = o.Pose.Orientation;
            var turn = (q * cur.Conjugate).Normalized();
            double turnAngle = 2 * Math.Acos(Math.Min(1, Math.Abs(turn.W)));
            var d = p - o.Pose.Position;
            bool turns = turnAngle > 1e-9, moves = d.Length > 1e-9;
            if (!turns && !moves) return false;
            if (turns) w.RotateAssembly(o, turn, o.Pose.Position);
            if (moves) w.MoveAssembly(o, d.X, d.Y, d.Z);
            return true;
        }

        private static WorldObject? Source(WorldDocument w, WorldObject o, string key) =>
            o.Parameters.TryGetValue(key, out var id) && w.Find(id) is { } s && s.Id != o.Id && WorldBuilding.IsActive(s) ? s : null;

        private static (Vec3 Local, Quat Turn) Rest(string? text)
        {
            var parts = (text ?? "").Split(';');
            var local = TryVector(parts.Length > 0 ? parts[0] : null) ?? Vec3.Zero;
            var turn = Turn(TryVector(parts.Length > 1 ? parts[1] : null) ?? Vec3.Zero);
            return (local, turn);
        }

        /// <summary>Roll, pitch, yaw (degrees) as the turn a pose makes of them (yaw, then pitch, then roll, as <see cref="WorldPose"/>).</summary>
        private static Quat Turn(Vec3 rpy) =>
            (Quat.FromAxisAngle(Vec3.UnitZ, rpy.Z * Math.PI / 180) * Quat.FromAxisAngle(Vec3.UnitY, rpy.Y * Math.PI / 180) * Quat.FromAxisAngle(Vec3.UnitX, rpy.X * Math.PI / 180)).Normalized();

        private static Vec3 Degrees(Quat q)
        {
            var (r, p, y) = q.ToEuler();
            return new Vec3(r * 180 / Math.PI, p * 180 / Math.PI, y * 180 / Math.PI);
        }

        public static string Text(Vec3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0.#########},{1:0.#########},{2:0.#########}", Clean(v.X), Clean(v.Y), Clean(v.Z));

        private static double Clean(double v) => Math.Abs(v) < 1e-12 ? 0 : v;

        public static Vec3? TryVector(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var p = text!.Split(',');
            if (p.Length != 3) return null;
            var v = new double[3];
            for (int i = 0; i < 3; i++)
                if (!double.TryParse(p[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
            return new Vec3(v[0], v[1], v[2]);
        }
    }
}
