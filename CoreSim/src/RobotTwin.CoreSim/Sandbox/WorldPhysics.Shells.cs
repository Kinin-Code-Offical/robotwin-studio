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

        /// <summary>The four sides in wall order: front (+x), back (−x), left (+y), right (−y).</summary>
        public static readonly string[] ShellSides = { "+x", "-x", "+y", "-y" };

        /// <summary>Sides of a shell with a cable hole (parameter "holes": +x, -x, +y, -y).</summary>
        public static HashSet<string> HolesOf(WorldObject o)
        {
            var holes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (o.Parameters.TryGetValue("holes", out var hs))
                foreach (var h in hs.Split(',', ';', ' ')) if (h.Trim().Length > 0) holes.Add(h.Trim());
            return holes;
        }

        /// <summary>
        /// A wall's cable hole: a square opening of side "hole" (m, default 10 mm: a grommet a servo lead or a
        /// pushrod passes through), centred along the wall, its centre "hole.z" (m) above the floor's top (default
        /// mid-height). Returns the side and the centre height above the floor's underside.
        /// </summary>
        public static (double Size, double CentreZ) HoleOf(WorldObject o, Vec3 size, int wall)
        {
            var (s, zc, _) = Hole(o, size, wall);
            return (s, zc);
        }

        /// <summary>
        /// A wall's hole: side, centre height above the floor's underside and offset along the wall (m). "hole",
        /// "hole.z" and "hole.u" apply to every holed side; "hole.+x.size", "hole.+x.z", "hole.+x.u" (and so on for
        /// each side) override them, so a boom can pass one wall while the motor leads leave another beside the motor.
        /// </summary>
        public static (double Size, double CentreZ, double U) Hole(WorldObject o, Vec3 size, int wall)
        {
            double t = Math.Min(WallOf(o), Math.Min(size.X, size.Y) / 2), h = size.Z - t;
            double len = wall < 2 ? size.Y : size.X - 2 * t;
            string side = ShellSides[wall];
            double s = Math.Max(0.002, Math.Min(Param(o, "hole." + side + ".size", Param(o, "hole", 0.010)), 0.8 * Math.Min(len, h)));
            double zc = Math.Max(s / 2, Math.Min(h - s / 2, Param(o, "hole." + side + ".z", Param(o, "hole.z", h / 2))));
            double u = Math.Max(-len / 2 + s / 2, Math.Min(len / 2 - s / 2, Param(o, "hole." + side + ".u", Param(o, "hole.u", 0))));
            return (s, t + zc, u);
        }

        /// <summary>
        /// [shell] The walls as boxes in the shell's frame (origin at the floor's underside centre, metres): a plain
        /// wall is one box; a wall with a cable hole is the frame around the opening, so a wire or a pushrod passes
        /// through the hole and nowhere else. One description for the physics and the editor's model.
        /// </summary>
        public static List<(int Wall, Vec3 Centre, Vec3 Size)> WallBoxes(WorldObject o, Vec3 size)
        {
            var list = new List<(int, Vec3, Vec3)>();
            double t = Math.Min(WallOf(o), Math.Min(size.X, size.Y) / 2), h = size.Z - t;
            if (h <= 0) return list;
            var holes = HolesOf(o);
            for (int k = 0; k < 4; k++)
            {
                bool alongY = k < 2;   // the front and back walls run along y
                double sign = k % 2 == 0 ? 1 : -1;
                double len = alongY ? size.Y : size.X - 2 * t;
                var c = alongY ? new Vec3(sign * (size.X / 2 - t / 2), 0, t + h / 2) : new Vec3(0, sign * (size.Y / 2 - t / 2), t + h / 2);
                if (!holes.Contains(ShellSides[k]))
                {
                    list.Add((k, c, alongY ? new Vec3(t, len, h) : new Vec3(len, t, h)));
                    continue;
                }

                var (s, zc, hu) = Hole(o, size, k);
                void Piece(double u0, double u1, double z0, double z1)
                {
                    if (u1 - u0 < 1e-5 || z1 - z0 < 1e-5) return;
                    double u = (u0 + u1) / 2, z = (z0 + z1) / 2;
                    list.Add((k, alongY ? new Vec3(c.X, u, z) : new Vec3(u, c.Y, z), alongY ? new Vec3(t, u1 - u0, z1 - z0) : new Vec3(u1 - u0, t, z1 - z0)));
                }

                Piece(-len / 2, hu - s / 2, t, t + h);
                Piece(hu + s / 2, len / 2, t, t + h);
                Piece(hu - s / 2, hu + s / 2, t, zc - s / 2);
                Piece(hu - s / 2, hu + s / 2, zc + s / 2, t + h);
            }

            return list;
        }

        /// <summary>
        /// [tube] A cylinder with "tube.wall" (m) is a tube, open at both ends: an airframe's body tube with the motor in
        /// its bore, a pipe. It collides as a ring of wall segments and has a tube's inertia (the mass at the rim). With
        /// "tube.bottom" (m) the lower end is closed by a disc of that thickness: a cup, a glass, a bottle without its neck.
        /// </summary>
        private static void MakeTube(SceneBody body, double wall, double bottom = 0)
        {
            if (body.Shape is not { Kind: ShapeKind.Cylinder } cyl) return;
            double ro = cyl.Radius, ri = Math.Max(0, ro - Math.Min(wall, ro)), h = cyl.HalfHeight;
            if (ri <= 1e-5) return;
            bottom = Math.Max(0, Math.Min(bottom, 2 * h - 1e-4));
            // A vessel's inside is seen by liquids: a finer ring keeps its wall round (24 segments of 15°).
            int n = bottom > 0 ? 24 : 16;
            var pieces = new CollisionShape[n + (bottom > 0 ? 1 : 0)];
            for (int k = 0; k < n; k++)
            {
                double a0 = 2 * Math.PI * k / n, a1 = 2 * Math.PI * (k + 1) / n;
                // The outer edge on the circle, the inner one a little inside it, so the ring never closes the bore.
                var pts = new List<Vec3>(8);
                foreach (double a in new[] { a0, a1 })
                    foreach (double r in new[] { ro, ri })
                        foreach (double z in new[] { -h, h }) pts.Add(new Vec3(r * Math.Cos(a), r * Math.Sin(a), z));
                pieces[k] = CollisionShape.ConvexHull(pts, 8);
            }

            double m = body.Mass, r2 = ro * ro + ri * ri;
            var inertia = new Vec3(m * (3 * r2 + 4 * h * h) / 12, m * (3 * r2 + 4 * h * h) / 12, m * r2 / 2);
            if (bottom > 0)
            {
                // The base: a polygon prism inside the ring, from the lower rim up to the bottom's thickness.
                var pts = new List<Vec3>(2 * n);
                for (int k = 0; k < n; k++)
                {
                    double a = 2 * Math.PI * (k + 0.5) / n;
                    foreach (double z in new[] { -h, -h + bottom }) pts.Add(new Vec3(ri * 1.02 * Math.Cos(a), ri * 1.02 * Math.Sin(a), z));
                }

                pieces[n] = CollisionShape.ConvexHull(pts, 2 * n);
                // Mass shared by volume between the wall and the base; inertia about the shape centre (the base sits low).
                double vWall = Math.PI * (ro * ro - ri * ri) * 2 * h, vBase = Math.PI * ri * ri * bottom;
                double mw = m * vWall / (vWall + vBase), mb = m - mw, zb = -h + bottom / 2;
                double iw = mw * (3 * r2 + 4 * h * h) / 12, ib = mb * (3 * ri * ri + bottom * bottom) / 12 + mb * zb * zb;
                inertia = new Vec3(iw + ib, iw + ib, mw * r2 / 2 + mb * ri * ri / 2);
            }

            body.Pieces = pieces;
            body.Inertia = inertia;
        }

        private static CollisionShape BoxPiece(Vec3 centre, Vec3 size)
        {
            var hs = size * 0.5;
            var corners = new List<Vec3>(8);
            foreach (double x in new[] { -hs.X, hs.X })
                foreach (double y in new[] { -hs.Y, hs.Y })
                    foreach (double z in new[] { -hs.Z, hs.Z }) corners.Add(centre + new Vec3(x, y, z));
            return CollisionShape.ConvexHull(corners, 8);
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
            var boxes = WallBoxes(o, size);
            it.Walls = new List<SceneBody>();
            _shellSize[o.Id] = (size, t);
            for (int k = 0; k < walls.Length; k++)
            {
                var (c, s) = walls[k];
                var pieces = boxes.Where(b => b.Wall == k).ToList();
                double area = pieces.Sum(b => Math.Max(b.Size.X, b.Size.Y) * b.Size.Z);
                double m = mass * area / total;   // the shell's mass shared by area (a hole takes its share away)
                var wall = SceneBody.Box(o.Id + ":duvar" + k, s, Math.Max(1e-4, m), default, material);
                // [shell] A wall with a cable hole is the frame around it: the solid other bodies and wires hit.
                if (pieces.Count > 1) wall.Pieces = pieces.Select(b => BoxPiece(b.Centre - c, b.Size)).ToArray();
                wall.Orientation = q;
                wall.Position = o.Pose.Position + q.Rotate(basis + c);
                wall.IsStatic = o.IsStatic;
                wall.Tag = o;
                wall.DragArea = Vec3.Zero;   // the shell's outer drag is the floor body's job
                Scene.AddBody(wall);
                var joint = Scene.AddJoint(JointKind.Fixed, floor, wall, wall.Position, Vec3.UnitZ, o.Id + ":duvar" + k);
                joint.Weld = true;   // [weld] the walls and floor are one moulded / glued piece
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
                joint.Weld = true;   // [weld] screwed down: the lid moves with the box
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
                    var (centre, _, _) = Obb(it.Body);   // [hull] a model-shaped body's box is not centred on its mass centre
                    var c = ToShell(shell, centre);
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
                var holes = HolesOf(shell.Object);
                foreach (var cable in world.Cables)
                {
                    if (CablePoints(cable.Id) is not { Count: > 1 } pts || RopeOf(cable.Id) is not { } rope) continue;
                    string label = string.IsNullOrEmpty(cable.Name) ? cable.Id : cable.Name;
                    // Leaving over the rim: free while the lid is open, pinched between lid and rim once it is closed.
                    if (lid != null && lidClosed)
                    {
                        for (int k = 1; k < pts.Count; k++)
                        {
                            var a = ToShell(shell, pts[k - 1]);
                            var b = ToShell(shell, pts[k]);
                            bool ina = Math.Abs(a.X) < ix && Math.Abs(a.Y) < iy && a.Z > 0 && a.Z < rim;
                            bool inb = Math.Abs(b.X) < ix && Math.Abs(b.Y) < iy && b.Z > 0 && b.Z < rim;
                            if (ina == inb) continue;
                            var outside = ina ? b : a;
                            bool overRim = Math.Abs(outside.X) < sz.Size.X / 2 && Math.Abs(outside.Y) < sz.Size.Y / 2 && outside.Z >= rim;
                            if (!overRim) continue;
                            issues.Add(new ShellFitIssue(sid, cable.Id, "ezilme", 0, $"kablo {label} kapakla kenar arasında eziliyor"));
                            break;
                        }
                    }

                    // Through a wall: where the wire really runs through one of the shell's wall solids (a wall with a
                    // hole is the frame around it, so a wire through the opening does not count).
                    foreach (var (body, inside) in rope.Crossings(Scene))
                    {
                        int wallIndex = shell.Walls.IndexOf(body);
                        if (wallIndex < 0) continue;
                        string side = ShellSides[wallIndex];
                        issues.Add(new ShellFitIssue(sid, cable.Id, "delik", inside, holes.Contains(side)
                            ? $"kablo {label} {side} duvarını deliğin dışından geçiyor"
                            : $"kablo {label} {side} duvarının içinden geçiyor: orada kablo deliği yok"));
                        break;
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
