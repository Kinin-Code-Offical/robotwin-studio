using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [shell] Hollow bodies: an enclosure, fuselage pod or robot body with a given wall thickness
    /// and an open top. Physically it is a floor plus four walls welded rigidly together; unlike
    /// other joined parts, the walls DO collide with whatever is placed inside, so a battery or a
    /// board screwed to the floor cannot slide through the side wall, and a lid screwed on top sits
    /// on the walls. Parameters: "shell" = "true", "wall" (m, default 2 mm).
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private readonly HashSet<SceneJoint> _shellJoints = new HashSet<SceneJoint>();

        public static bool IsShell(WorldObject o) =>
            o.Kind == WorldObjectKind.Body && o.Parameters.TryGetValue("shell", out var s) && (s == "true" || s == "1");

        public static double WallOf(WorldObject o) => Math.Max(0.0005, Param(o, "wall", 0.002));

        /// <summary>Material volume of a body (m³): a shell is its floor and walls, not the solid box.</summary>
        public static double MaterialVolume(WorldObject o)
        {
            double solid = o.SizeX * o.SizeY * o.SizeZ;
            if (!IsShell(o)) return solid;
            double t = WallOf(o);
            return solid - Math.Max(0, o.SizeX - 2 * t) * Math.Max(0, o.SizeY - 2 * t) * Math.Max(0, o.SizeZ - t);
        }

        /// <summary>The floor plate of a shell (the object's own body); the walls follow in <see cref="AddShellWalls"/>.</summary>
        private SceneBody ShellFloor(WorldObject o, Vec3 size, Vec3 centre, double mass, string material, Item it)
        {
            double t = Math.Min(WallOf(o), size.Z / 2);
            double total = size.X * size.Y + 2 * (size.X + size.Y) * size.Z;
            it.Centre = new Vec3(centre.X, centre.Y, centre.Z - size.Z / 2 + t / 2);
            return SceneBody.Box(o.Id, new Vec3(size.X, size.Y, t), mass * size.X * size.Y / total, default, material);
        }

        private void AddShellWalls(Item it, Vec3 size, double mass, string material)
        {
            var o = it.Object;
            var floor = it.Body;
            double t = Math.Min(WallOf(o), Math.Min(size.X, size.Y) / 2), h = size.Z - t;
            if (h <= 0) return;
            double total = size.X * size.Y + 2 * (size.X + size.Y) * size.Z;
            var q = o.Pose.Orientation;
            var basis = it.Centre - new Vec3(0, 0, t / 2);   // floor underside centre, object frame
            var walls = new (Vec3 Centre, Vec3 Size)[]
            {
                (new Vec3(size.X / 2 - t / 2, 0, t + h / 2), new Vec3(t, size.Y, h)),
                (new Vec3(-size.X / 2 + t / 2, 0, t + h / 2), new Vec3(t, size.Y, h)),
                (new Vec3(0, size.Y / 2 - t / 2, t + h / 2), new Vec3(size.X - 2 * t, t, h)),
                (new Vec3(0, -size.Y / 2 + t / 2, t + h / 2), new Vec3(size.X - 2 * t, t, h))
            };
            it.Walls = new List<SceneBody>();
            _shellSize[o.Id] = (size, t);
            for (int k = 0; k < walls.Length; k++)
            {
                var (c, s) = walls[k];
                double m = mass * Math.Max(s.X, s.Y) * s.Z / total;   // the shell's mass shared by area
                var wall = SceneBody.Box(o.Id + ":duvar" + k, s, Math.Max(1e-4, m), default, material);
                wall.Orientation = q;
                wall.Position = o.Pose.Position + q.Rotate(basis + c);
                wall.IsStatic = o.IsStatic;
                wall.Tag = o;
                wall.DragArea = Vec3.Zero;   // the shell's outer drag is the floor body's job
                Scene.AddBody(wall);
                var joint = Scene.AddJoint(JointKind.Fixed, floor, wall, wall.Position, Vec3.UnitZ, o.Id + ":duvar" + k);
                _shellJoints.Add(joint);
                Scene.NoCollide.Add((floor, wall));
                foreach (var other in it.Walls) Scene.NoCollide.Add((other, wall));
                it.Walls.Add(wall);
            }
        }

        private readonly Dictionary<string, (Vec3 Size, double Wall)> _shellSize = new Dictionary<string, (Vec3, double)>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _screwedLids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The shell a lid object belongs to (parameter "lid.of"), or null.</summary>
        public static string? LidOf(WorldObject o) =>
            o.Parameters.TryGetValue("lid.of", out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

        private static bool Flag(WorldObject o, string key) => o.Parameters.TryGetValue(key, out var s) && (s == "true" || s == "1");

        /// <summary>Lids sitting on their shell's rim with "screwed" = true are screwed down (welded to the shell).</summary>
        private void ScrewLids()
        {
            foreach (var lid in _items.Values)
            {
                if (LidOf(lid.Object) is not { } sid || !_items.TryGetValue(sid, out var shell) || shell.Walls == null) continue;
                if (!Flag(lid.Object, "screwed")) continue;
                var (gap, _) = LidGap(shell, lid);
                if (Math.Abs(gap) > 0.003) continue;   // not seated on the rim: the screws do not reach
                var joint = Scene.AddJoint(JointKind.Fixed, shell.Body, lid.Body, lid.Body.Position, Vec3.UnitZ, lid.Object.Id + ":vida");
                _shellJoints.Add(joint);
                Scene.NoCollide.Add((shell.Body, lid.Body));
                foreach (var w in shell.Walls) Scene.NoCollide.Add((w, lid.Body));
                _screwedLids.Add(lid.Object.Id);
            }
        }

        public bool IsScrewed(string lidId) => _screwedLids.Contains(lidId);

        /// <summary>A world point in the shell floor's frame (origin at the floor centre).</summary>
        private static Vec3 ToShell(Item shell, Vec3 world) => shell.Body.Orientation.InverseRotate(world - shell.Body.Position);

        private static double RimTop((Vec3 Size, double Wall) sz) => sz.Size.Z - sz.Wall / 2;

        /// <summary>Half extents of a body measured along the shell's axes.</summary>
        private static Vec3 ExtentIn(Item shell, SceneBody body)
        {
            var (_, axes, half) = Obb(body);
            var q = shell.Body.Orientation;
            Vec3 ex = q.Rotate(Vec3.UnitX), ey = q.Rotate(Vec3.UnitY), ez = q.Rotate(Vec3.UnitZ);
            double E(Vec3 u) => half.X * Math.Abs(axes[0].Dot(u)) + half.Y * Math.Abs(axes[1].Dot(u)) + half.Z * Math.Abs(axes[2].Dot(u));
            return new Vec3(E(ex), E(ey), E(ez));
        }

        /// <summary>Gap between the lid's underside and the rim (m; negative = pushed into the shell) and the underside height.</summary>
        private (double Gap, double Bottom) LidGap(Item shell, Item lid)
        {
            var sz = _shellSize[shell.Object.Id];
            double bottom = ToShell(shell, lid.Body.Position).Z - ExtentIn(shell, lid.Body).Z;
            return (bottom - RimTop(sz), bottom);
        }

        /// <summary>
        /// Fit check of every shell: what sits inside, whether the lid can close over it (a part
        /// taller than the cavity holds the lid up), parts pushed into a wall, and cables that leave
        /// the shell between lid and rim (pinched) or through a wall without a hole ("holes" lists
        /// the sides with a cable hole: +x, -x, +y, -y).
        /// </summary>
        public IReadOnlyList<ShellFitIssue> ShellFit(WorldDocument? world = null, double tolerance = 0.0003)
        {
            var issues = new List<ShellFitIssue>();
            foreach (var shell in _items.Values)
            {
                if (shell.Walls == null || !_shellSize.TryGetValue(shell.Object.Id, out var sz)) continue;
                string sid = shell.Object.Id;
                double t = sz.Wall, ix = sz.Size.X / 2 - t, iy = sz.Size.Y / 2 - t, rim = RimTop(sz);
                var lid = _items.Values.FirstOrDefault(i => string.Equals(LidOf(i.Object), sid, StringComparison.OrdinalIgnoreCase));
                bool lidClosed = false;
                if (lid != null)
                {
                    var (gap, _) = LidGap(shell, lid);
                    lidClosed = gap < 0.01;   // within 1 cm of the rim counts as "being closed"
                    if (lidClosed && gap > 0.0005)
                        issues.Add(new ShellFitIssue(sid, lid.Object.Id, "kapak", gap, $"{NameOf(lid.Object)} kenara oturmuyor: {gap * 1000:0.0} mm aralık"));
                    if (lidClosed && Flag(lid.Object, "screwed") && !_screwedLids.Contains(lid.Object.Id))
                        issues.Add(new ShellFitIssue(sid, lid.Object.Id, "vida", gap, $"{NameOf(lid.Object)} vidalanamıyor: kapak kenardan {gap * 1000:0.0} mm uzakta"));
                }

                foreach (var it in _items.Values)
                {
                    if (it == shell || it == lid || it.Body == null) continue;
                    var c = ToShell(shell, it.Body.Position);
                    var e = ExtentIn(shell, it.Body);
                    if (Math.Abs(c.X) > ix || Math.Abs(c.Y) > iy || c.Z >= rim || c.Z < 0) continue;   // not inside (a wing or lid on top has its centre above the rim)
                    string name = NameOf(it.Object);
                    double over = c.Z + e.Z - rim;   // measured against the rim: a lid resting on the part is held up by exactly this
                    if (over > tolerance)
                        issues.Add(lid != null && lidClosed
                            ? new ShellFitIssue(sid, it.Object.Id, "yükseklik", over, $"{name} kapağa {over * 1000:0.0} mm çarpıyor: kapak kapanmaz")
                            : new ShellFitIssue(sid, it.Object.Id, "yükseklik", over, $"{name} kenardan {over * 1000:0.0} mm taşıyor: kapak kapanmaz"));
                    foreach (var w in shell.Walls)
                    {
                        double d = ObbOverlap(it.Body, w);
                        if (d > tolerance) issues.Add(new ShellFitIssue(sid, it.Object.Id, "duvar", d, $"{name} duvara {d * 1000:0.0} mm giriyor"));
                    }
                }

                if (world == null) continue;
                var holes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (shell.Object.Parameters.TryGetValue("holes", out var hs))
                    foreach (var h in hs.Split(',', ';', ' ')) if (h.Trim().Length > 0) holes.Add(h.Trim());
                foreach (var cable in world.Cables)
                {
                    if (CablePoints(cable.Id) is not { Count: > 1 } pts) continue;
                    bool reported = false;
                    for (int k = 1; k < pts.Count && !reported; k++)
                    {
                        var a = ToShell(shell, pts[k - 1]);
                        var b = ToShell(shell, pts[k]);
                        bool ina = Math.Abs(a.X) < ix && Math.Abs(a.Y) < iy && a.Z > 0 && a.Z < rim;
                        bool inb = Math.Abs(b.X) < ix && Math.Abs(b.Y) < iy && b.Z > 0 && b.Z < rim;
                        if (ina == inb) continue;
                        var inside = ina ? a : b;
                        var outside = ina ? b : a;
                        bool outX = Math.Abs(outside.X) >= sz.Size.X / 2, outY = Math.Abs(outside.Y) >= sz.Size.Y / 2;
                        if (!outX && !outY && outside.Z < rim) continue;   // still within the wall thickness
                        double z = (inside.Z + outside.Z) / 2;
                        string label = string.IsNullOrEmpty(cable.Name) ? cable.Id : cable.Name;
                        if (z >= rim - 0.002 || (!outX && !outY))
                        {
                            // Leaves over the rim: free while the lid is open, pinched once it closes.
                            if (lid != null && lidClosed)
                            {
                                issues.Add(new ShellFitIssue(sid, cable.Id, "ezilme", 0, $"kablo {label} kapakla kenar arasında eziliyor"));
                                reported = true;
                            }

                            continue;
                        }

                        string side = Math.Abs(outside.X) / (sz.Size.X / 2) >= Math.Abs(outside.Y) / (sz.Size.Y / 2)
                            ? (outside.X > 0 ? "+x" : "-x") : (outside.Y > 0 ? "+y" : "-y");
                        if (!holes.Contains(side))
                        {
                            issues.Add(new ShellFitIssue(sid, cable.Id, "delik", 0, $"kablo {label} {side} duvarının içinden geçiyor: orada kablo deliği yok"));
                            reported = true;
                        }
                    }
                }
            }

            return issues;
        }

        private static string NameOf(WorldObject o) => string.IsNullOrEmpty(o.Name) ? o.Id : o.Name;

        /// <summary>The wall bodies of a shell object (empty for others).</summary>
        public IReadOnlyList<SceneBody> WallsOf(string objectId) =>
            _items.TryGetValue(objectId, out var it) && it.Walls != null ? it.Walls : (IReadOnlyList<SceneBody>)Array.Empty<SceneBody>();
    }

    /// <summary>One fit problem of a shell: Kind is kapak, vida, yükseklik, duvar, ezilme or delik; Amount in m.</summary>
    public sealed record ShellFitIssue(string ShellId, string ObjectId, string Kind, double Amount, string Message);
}
