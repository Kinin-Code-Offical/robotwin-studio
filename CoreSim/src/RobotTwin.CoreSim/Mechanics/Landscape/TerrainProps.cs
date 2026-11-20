using System;
using System.Collections.Generic;
using System.Globalization;

namespace RobotTwin.CoreSim.Mechanics
{
    // [terrain] World-building props: ramps, stairs, walls, kerbs, cones, pipes, rocks, trees …

    /// <summary>A prop of the world editor's prefab palette.</summary>
    public sealed class PropDefinition
    {
        public PropDefinition(string id, string name, string description, double sx, double sy, double sz, bool dynamic, string material, double massKg = 0)
        {
            Id = id;
            Name = name;
            Description = description;
            SizeX = sx;
            SizeY = sy;
            SizeZ = sz;
            Dynamic = dynamic;
            Material = material;
            MassKg = massKg;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public double SizeX { get; }
        public double SizeY { get; }
        public double SizeZ { get; }

        /// <summary>Moves when hit (cone, box); otherwise fixed scenery.</summary>
        public bool Dynamic { get; }

        /// <summary>World material name (as in the inspector: "AHŞAP", "PLA" …).</summary>
        public string Material { get; }

        public double MassKg { get; }
    }

    /// <summary>One convex collision piece of a prop in its own frame.</summary>
    public readonly struct PropCollider
    {
        public PropCollider(Vec3 center, Vec3 size, Quat orientation)
        {
            Center = center;
            Size = size;
            Orientation = orientation;
            Points = null;
        }

        public PropCollider(IReadOnlyList<Vec3> points)
        {
            Center = Vec3.Zero;
            Size = Vec3.Zero;
            Orientation = Quat.Identity;
            Points = points;
        }

        public Vec3 Center { get; }
        public Vec3 Size { get; }
        public Quat Orientation { get; }

        /// <summary>Convex hull points (when not a box).</summary>
        public IReadOnlyList<Vec3>? Points { get; }

        public bool IsBox => Points == null;
    }

    /// <summary>
    /// Builds prop meshes (centred in x/y, standing on z = 0, coloured per triangle) and their
    /// collision pieces. Static props collide as boxes/convex pieces so stairs, pipes and trees
    /// are not filled in by a single convex hull.
    /// </summary>
    public static class TerrainProps
    {
        public const string Key = "prop";

        public static IReadOnlyList<PropDefinition> All { get; } = new List<PropDefinition>
        {
            new PropDefinition("ramp", "Rampa", "Kama rampa; uzunluk × genişlik × yükseklik", 0.6, 0.4, 0.1, false, "AHŞAP"),
            new PropDefinition("step", "Basamak bloğu", "Tek basamaklı platform", 0.4, 0.4, 0.03, false, "AHŞAP"),
            new PropDefinition("stairs", "Merdiven", "3 basamak, 40 mm rıht", 0.36, 0.5, 0.12, false, "AHŞAP"),
            new PropDefinition("wall", "Duvar", "Beton duvar parçası", 1.0, 0.05, 0.25, false, "ÇELİK"),
            new PropDefinition("kerb", "Bordür", "Pahlı kaldırım taşı", 1.0, 0.1, 0.04, false, "ÇELİK"),
            new PropDefinition("cone", "Trafik konisi", "Devrilebilir koni, 15 cm", 0.1, 0.1, 0.15, true, "KAUÇUK", 0.12),
            new PropDefinition("box", "Karton kutu", "İtilebilir kutu", 0.2, 0.15, 0.12, true, "AHŞAP", 0.25),
            new PropDefinition("pipe", "Boru (tünel)", "İçinden geçilebilen boru, Ø 250 mm", 0.6, 0.25, 0.25, false, "PLA"),
            new PropDefinition("rock", "Kaya", "Düzensiz kaya", 0.3, 0.25, 0.18, false, "ÇELİK"),
            new PropDefinition("tree", "Ağaç", "Gövde çarpışır, yapraklar görsel", 0.8, 0.8, 1.6, false, "AHŞAP"),
            new PropDefinition("bush", "Çalı", "Görsel; araçlar içinden geçer", 0.45, 0.45, 0.35, false, "AHŞAP")
        };

        public static PropDefinition? Find(string? id)
        {
            foreach (var p in All)
                if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        private static double Read(IDictionary<string, string> p, string key, double fallback) =>
            p.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : fallback;

        /// <summary>Prop size stored on an object ("sx", "sy", "sz" in metres).</summary>
        public static Vec3 SizeOf(IDictionary<string, string> parameters, PropDefinition def) =>
            new Vec3(Read(parameters, "sx", def.SizeX), Read(parameters, "sy", def.SizeY), Read(parameters, "sz", def.SizeZ));

        public static void StoreSize(IDictionary<string, string> parameters, Vec3 size)
        {
            parameters["sx"] = F(size.X);
            parameters["sy"] = F(size.Y);
            parameters["sz"] = F(size.Z);
        }

        // ================================================================== meshes

        private sealed class Builder
        {
            public readonly MeshModel Mesh = new MeshModel();

            public Builder()
            {
                Mesh.TriangleColors = new List<MeshColor>();
            }

            public void Tri(Vec3 a, Vec3 b, Vec3 c, MeshColor col)
            {
                int i = Mesh.AddVertex(a), j = Mesh.AddVertex(b), k = Mesh.AddVertex(c);
                Mesh.AddTriangle(i, j, k);
                Mesh.TriangleColors!.Add(col);
            }

            public void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, MeshColor col)
            {
                Tri(a, b, c, col);
                Tri(a, c, d, col);
            }

            /// <summary>Axis-aligned box from min to max.</summary>
            public void Box(Vec3 lo, Vec3 hi, MeshColor col, MeshColor? top = null)
            {
                var p = new Vec3[8];
                for (int i = 0; i < 8; i++) p[i] = new Vec3((i & 1) != 0 ? hi.X : lo.X, (i & 2) != 0 ? hi.Y : lo.Y, (i & 4) != 0 ? hi.Z : lo.Z);
                Quad(p[0], p[2], p[3], p[1], col); // bottom (−z)
                Quad(p[4], p[5], p[7], p[6], top ?? col); // top
                Quad(p[0], p[1], p[5], p[4], col); // −y
                Quad(p[2], p[6], p[7], p[3], col); // +y
                Quad(p[0], p[4], p[6], p[2], col); // −x
                Quad(p[1], p[3], p[7], p[5], col); // +x
            }

            /// <summary>Convex prism from a polygon in the x/z plane (counter-clockwise seen from −y) extruded along y.</summary>
            public void PrismXZ(IReadOnlyList<(double X, double Z)> poly, double y0, double y1, MeshColor col, MeshColor? top = null)
            {
                int n = poly.Count;
                for (int i = 1; i + 1 < n; i++)
                {
                    Tri(new Vec3(poly[0].X, y0, poly[0].Z), new Vec3(poly[i].X, y0, poly[i].Z), new Vec3(poly[i + 1].X, y0, poly[i + 1].Z), col);
                    Tri(new Vec3(poly[0].X, y1, poly[0].Z), new Vec3(poly[i + 1].X, y1, poly[i + 1].Z), new Vec3(poly[i].X, y1, poly[i].Z), col);
                }

                for (int i = 0; i < n; i++)
                {
                    var a = poly[i];
                    var b = poly[(i + 1) % n];
                    bool up = b.X < a.X - 1e-9 || Math.Abs(b.Z - a.Z) < 1e-9 && a.Z > 1e-9;
                    Quad(new Vec3(a.X, y0, a.Z), new Vec3(a.X, y1, a.Z), new Vec3(b.X, y1, b.Z), new Vec3(b.X, y0, b.Z), up && top.HasValue ? top.Value : col);
                }
            }

            /// <summary>Closed cylinder/cone frustum along z.</summary>
            public void Frustum(double cx, double cy, double z0, double z1, double r0, double r1, int seg, MeshColor col, bool capBottom = true, bool capTop = true)
            {
                for (int i = 0; i < seg; i++)
                {
                    double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                    var b0 = new Vec3(cx + r0 * Math.Cos(a0), cy + r0 * Math.Sin(a0), z0);
                    var b1 = new Vec3(cx + r0 * Math.Cos(a1), cy + r0 * Math.Sin(a1), z0);
                    var t0 = new Vec3(cx + r1 * Math.Cos(a0), cy + r1 * Math.Sin(a0), z1);
                    var t1 = new Vec3(cx + r1 * Math.Cos(a1), cy + r1 * Math.Sin(a1), z1);
                    if (r1 > 1e-9) Quad(b0, b1, t1, t0, col);
                    else Tri(b0, b1, t0, col);
                    if (capBottom) Tri(new Vec3(cx, cy, z0), b1, b0, col);
                    if (capTop && r1 > 1e-9) Tri(new Vec3(cx, cy, z1), t0, t1, col);
                }
            }

            /// <summary>Deformed icosphere-like blob (rocks, foliage) — closed, roughly convex.</summary>
            public void Blob(Vec3 centre, Vec3 radii, int seed, double roughness, MeshColor col, MeshColor? col2 = null)
            {
                const int rings = 6, seg = 10;
                var pts = new Vec3[(rings - 1) * seg + 2];
                pts[0] = centre + new Vec3(0, 0, -radii.Z);
                pts[pts.Length - 1] = centre + new Vec3(0, 0, radii.Z);
                for (int r = 1; r < rings; r++)
                {
                    double phi = Math.PI * r / rings;
                    for (int s = 0; s < seg; s++)
                    {
                        double th = 2 * Math.PI * (s + 0.5 * (r & 1)) / seg;
                        double k = 1 + roughness * TerrainSculpt.ValueNoise(seed * 3.1 + r * 1.7, s * 1.3 + seed);
                        pts[1 + (r - 1) * seg + s] = centre + new Vec3(radii.X * Math.Sin(phi) * Math.Cos(th) * k, radii.Y * Math.Sin(phi) * Math.Sin(th) * k, -radii.Z * Math.Cos(phi) * k);
                    }
                }

                MeshColor C(int i) => col2.HasValue && ((i * 7 + seed) % 3 == 0) ? col2.Value : col;
                int t = 0;
                for (int s = 0; s < seg; s++) Tri(pts[0], pts[1 + (s + 1) % seg], pts[1 + s], C(t++));
                for (int r = 1; r < rings - 1; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = 1 + (r - 1) * seg + s, b = 1 + (r - 1) * seg + (s + 1) % seg;
                    int c = 1 + r * seg + s, d = 1 + r * seg + (s + 1) % seg;
                    Tri(pts[a], pts[b], pts[d], C(t++));
                    Tri(pts[a], pts[d], pts[c], C(t++));
                }

                int top = pts.Length - 1, last = 1 + (rings - 2) * seg;
                for (int s = 0; s < seg; s++) Tri(pts[top], pts[last + s], pts[last + (s + 1) % seg], C(t++));
            }
        }

        private static MeshColor Rgb(uint rgb) => new MeshColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        /// <summary>Mesh of a prop of the given size (centred in x/y, base on z = 0).</summary>
        public static MeshModel BuildMesh(string id, Vec3 size, int seed = 1)
        {
            var b = new Builder();
            double sx = size.X, sy = size.Y, sz = size.Z, hx = sx / 2, hy = sy / 2;
            switch (id)
            {
                case "ramp":
                    b.PrismXZ(new[] { (-hx, 0.0), (hx, 0.0), (hx, sz), (hx - Math.Min(0.02, sx * 0.05), sz) }, -hy, hy, Rgb(0xB88A5A), Rgb(0xC89A66));
                    break;
                case "step":
                    b.Box(new Vec3(-hx, -hy, 0), new Vec3(hx, hy, sz), Rgb(0xA87E52), Rgb(0xC19461));
                    break;
                case "stairs":
                {
                    int n = 3;
                    double tread = sx / n, rise = sz / n;
                    for (int i = 0; i < n; i++)
                        b.Box(new Vec3(-hx + i * tread, -hy, 0), new Vec3(-hx + (i + 1) * tread, hy, rise * (i + 1)), Rgb(0xA87E52), Rgb(0xC19461));
                    break;
                }

                case "wall":
                    b.Box(new Vec3(-hx, -hy, 0), new Vec3(hx, hy, sz), Rgb(0x9C9A94), Rgb(0xB0AEA8));
                    break;
                case "kerb":
                {
                    double ch = Math.Min(sz * 0.4, sy * 0.3);
                    b.PrismXZ(new[] { (-hy, 0.0), (hy, 0.0), (hy, sz - ch), (hy - ch, sz), (-hy, sz) }, -hx, hx, Rgb(0xBDBAB2), Rgb(0xD2CFC7));
                    // The prism was built across y: turn it to run along x.
                    var m = b.Mesh;
                    for (int i = 0; i < m.Vertices.Count; i++)
                    {
                        var v = m.Vertices[i];
                        m.Vertices[i] = new Vec3(v.Y, -v.X, v.Z);
                    }

                    break;
                }

                case "cone":
                {
                    double r = Math.Min(hx, hy);
                    double baseH = Math.Max(0.004, sz * 0.05);
                    b.Box(new Vec3(-r, -r, 0), new Vec3(r, r, baseH), Rgb(0x1E1E20));
                    double r0 = r * 0.82, rTop = r * 0.12, h = sz - baseH;
                    var orange = Rgb(0xF26A1B);
                    var white = Rgb(0xF2F2F2);
                    double z1 = baseH + h * 0.45, z2 = baseH + h * 0.62;
                    double Rz(double z) => r0 + (rTop - r0) * (z - baseH) / h;
                    b.Frustum(0, 0, baseH, z1, r0, Rz(z1), 20, orange, true, false);
                    b.Frustum(0, 0, z1, z2, Rz(z1), Rz(z2), 20, white, false, false);
                    b.Frustum(0, 0, z2, sz, Rz(z2), rTop, 20, orange, false, true);
                    break;
                }

                case "box":
                    b.Box(new Vec3(-hx, -hy, 0), new Vec3(hx, hy, sz), Rgb(0xB8925E), Rgb(0xC9A470));
                    break;
                case "pipe":
                {
                    // Lies along x; ring of thickness t.
                    double ro = Math.Min(sy, sz) / 2, t = Math.Max(0.004, ro * 0.08), ri = ro - t;
                    const int seg = 24;
                    var col = Rgb(0x8A8F96);
                    var inner = Rgb(0x5A5E64);
                    for (int i = 0; i < seg; i++)
                    {
                        double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                        Vec3 P(double x, double r, double a) => new Vec3(x, r * Math.Cos(a), ro + r * Math.Sin(a));
                        b.Quad(P(-hx, ro, a0), P(-hx, ro, a1), P(hx, ro, a1), P(hx, ro, a0), col);
                        b.Quad(P(-hx, ri, a0), P(hx, ri, a0), P(hx, ri, a1), P(-hx, ri, a1), inner);
                        b.Quad(P(-hx, ro, a0), P(-hx, ri, a0), P(-hx, ri, a1), P(-hx, ro, a1), col);
                        b.Quad(P(hx, ro, a0), P(hx, ro, a1), P(hx, ri, a1), P(hx, ri, a0), col);
                    }

                    break;
                }

                case "rock":
                    b.Blob(new Vec3(0, 0, sz / 2), new Vec3(hx, hy, sz / 2), seed, 0.22, Rgb(0x7D7A74), Rgb(0x6A6762));
                    break;
                case "tree":
                {
                    double trunkR = Math.Max(0.02, Math.Min(hx, hy) * 0.09), trunkH = sz * 0.42;
                    b.Frustum(0, 0, 0, trunkH + sz * 0.1, trunkR * 1.2, trunkR * 0.8, 10, Rgb(0x5B3F28));
                    b.Blob(new Vec3(0, 0, trunkH + (sz - trunkH) * 0.45), new Vec3(hx * 0.95, hy * 0.95, (sz - trunkH) * 0.55), seed, 0.25, Rgb(0x3E6B2A), Rgb(0x4F7F33));
                    break;
                }

                case "bush":
                    b.Blob(new Vec3(0, 0, sz / 2), new Vec3(hx, hy, sz / 2), seed, 0.3, Rgb(0x426E2C), Rgb(0x56853A));
                    break;
                default:
                    b.Box(new Vec3(-hx, -hy, 0), new Vec3(hx, hy, sz), Rgb(0x9A9A9A));
                    break;
            }

            b.Mesh.Name = id;
            Recentre(b.Mesh);
            return b.Mesh;
        }

        /// <summary>Centres the mesh bounds in x/y and puts its lowest point on z = 0 (the world's placement rule).</summary>
        private static void Recentre(MeshModel m)
        {
            var bb = m.Bounds;
            var off = new Vec3(-(bb.Min.X + bb.Max.X) / 2, -(bb.Min.Y + bb.Max.Y) / 2, -bb.Min.Z);
            for (int i = 0; i < m.Vertices.Count; i++) m.Vertices[i] += off;
        }

        // ================================================================== collision

        /// <summary>Convex pieces of a prop (object frame); empty = no collision (bushes).</summary>
        public static List<PropCollider> Colliders(string id, Vec3 size, int seed = 1)
        {
            var list = new List<PropCollider>();
            double sx = size.X, sy = size.Y, sz = size.Z, hx = sx / 2, hy = sy / 2;
            switch (id)
            {
                case "stairs":
                {
                    int n = 3;
                    double tread = sx / n, rise = sz / n;
                    for (int i = 0; i < n; i++)
                    {
                        double x0 = -hx + i * tread, h = rise * (i + 1);
                        // Each step reaches to the back so the stack has no gaps.
                        list.Add(new PropCollider(new Vec3((x0 + hx) / 2, 0, h / 2), new Vec3(hx - x0, sy, h), Quat.Identity));
                    }

                    break;
                }

                case "pipe":
                {
                    double ro = Math.Min(sy, sz) / 2, t = Math.Max(0.004, ro * 0.08);
                    const int seg = 16;
                    double chord = 2 * ro * Math.Sin(Math.PI / seg) * 1.05;
                    for (int i = 0; i < seg; i++)
                    {
                        double a = 2 * Math.PI * (i + 0.5) / seg;
                        double rm = ro - t / 2;
                        var c = new Vec3(0, rm * Math.Cos(a), ro + rm * Math.Sin(a));
                        list.Add(new PropCollider(c, new Vec3(sx, t, chord), Quat.FromAxisAngle(Vec3.UnitX, a)));
                    }

                    break;
                }

                case "tree":
                {
                    double trunkR = Math.Max(0.02, Math.Min(hx, hy) * 0.09);
                    double h = sz * 0.6;
                    list.Add(new PropCollider(new Vec3(0, 0, h / 2), new Vec3(trunkR * 2.2, trunkR * 2.2, h), Quat.Identity));
                    break;
                }

                case "bush":
                    break;
                default:
                {
                    var mesh = BuildMesh(id, size, seed);
                    list.Add(new PropCollider(mesh.Vertices));
                    break;
                }
            }

            return list;
        }
    }
}
