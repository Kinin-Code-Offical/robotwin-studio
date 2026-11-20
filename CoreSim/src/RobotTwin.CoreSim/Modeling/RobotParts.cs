using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>A parametric robot part built from primitives and booleans.</summary>
    public sealed class PartTemplate
    {
        public PartTemplate(string id, string name, string description, string material, string color, ParamSpec[] parameters, Func<Func<string, double>, PolyMesh> build)
        {
            Id = id;
            Name = name;
            Description = description;
            Material = material;
            Color = color;
            Params = parameters;
            BuildMesh = build;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Material { get; }
        public string Color { get; }
        public IReadOnlyList<ParamSpec> Params { get; }
        public Func<Func<string, double>, PolyMesh> BuildMesh { get; }
    }

    /// <summary>Starter library of robot parts (chassis, brackets, hubs, enclosures). Dimensions in mm.</summary>
    public static class RobotParts
    {
        private const int HoleSeg = 32;

        public static IReadOnlyList<PartTemplate> All { get; } = new[]
        {
            new PartTemplate("chassis-2wd", "2WD şasi plakası", "İki TT motor bağlantı kulağı, sarhoş teker ve kablo delikleri olan şasi.", "PLA", "#3B82C4", new[]
            {
                new ParamSpec("l", "Uzunluk", "mm", 150, 60, 400), new ParamSpec("w", "Genişlik", "mm", 110, 50, 300), new ParamSpec("t", "Kalınlık", "mm", 3, 1.5, 10),
                new ParamSpec("r", "Köşe yarıçapı", "mm", 10, 0, 50), new ParamSpec("mx", "Motor ekseni X", "mm", 30, -150, 150), new ParamSpec("mounts", "Motor kulakları (1/0)", "", 1, 0, 1, true)
            }, Chassis2Wd),
            new PartTemplate("sg90-bracket", "SG90 servo braketi", "L tipi servo yuvası: 23×12.6 mm gövde boşluğu, 27.6 mm vida aralığı, destek üçgenleri.", "PETG", "#2A2D34", new[]
            {
                new ParamSpec("w", "Genişlik", "mm", 40, 34, 80), new ParamSpec("t", "Kalınlık", "mm", 3, 2, 6), new ParamSpec("h", "Dikme yüksekliği", "mm", 30, 22, 60),
                new ParamSpec("d", "Taban derinliği", "mm", 24, 12, 60)
            }, Sg90Bracket),
            new PartTemplate("l-bracket", "L braket", "Delikli, istenirse destekli 90° bağlantı braketi.", "PLA", "#8E98A8", new[]
            {
                new ParamSpec("a", "Yatay kol", "mm", 30, 8, 200), new ParamSpec("b", "Dikey kol", "mm", 30, 8, 200), new ParamSpec("w", "Genişlik", "mm", 20, 6, 100),
                new ParamSpec("t", "Kalınlık", "mm", 3, 1, 12), new ParamSpec("hd", "Delik çapı", "mm", 3.2, 0, 12), new ParamSpec("n", "Kol başına delik", "", 2, 0, 6, true),
                new ParamSpec("gusset", "Destek (1/0)", "", 1, 0, 1, true)
            }, LBracket),
            new PartTemplate("wheel-hub", "Tekerlek göbeği", "TT motor çift-D miline (5.4/3.7 mm) oturan, hafifletme delikli göbek.", "PLA", "#E0A030", new[]
            {
                new ParamSpec("d", "Disk çapı", "mm", 40, 16, 200), new ParamSpec("t", "Disk kalınlığı", "mm", 5, 2, 20), new ParamSpec("bd", "Göbek çapı", "mm", 14, 8, 60),
                new ParamSpec("bh", "Göbek yüksekliği", "mm", 8, 2, 40), new ParamSpec("sd", "Mil çapı", "mm", 5.4, 1, 20), new ParamSpec("sf", "Mil düzlük arası", "mm", 3.7, 0.5, 20),
                new ParamSpec("n", "Hafifletme deliği", "", 5, 0, 12, true)
            }, WheelHub),
            new PartTemplate("enclosure", "Muhafaza kutusu", "Köşelerinde vida kuleleri olan elektronik kutusu.", "PETG", "#4A5060", EnclosureParams(), p => Enclosure(p, false)),
            new PartTemplate("enclosure-lid", "Muhafaza kapağı", "Kutunun vida kulelerine oturan delikli kapak.", "PETG", "#5A6273", EnclosureParams(), p => Enclosure(p, true))
        };

        private static ParamSpec[] EnclosureParams() => new[]
        {
            new ParamSpec("x", "Dış genişlik", "mm", 80, 30, 300), new ParamSpec("y", "Dış derinlik", "mm", 60, 30, 300), new ParamSpec("z", "Yükseklik", "mm", 35, 10, 200),
            new ParamSpec("wall", "Duvar kalınlığı", "mm", 2, 1, 6), new ParamSpec("r", "Köşe yarıçapı", "mm", 4, 0, 30), new ParamSpec("boss", "Vida kulesi çapı", "mm", 7, 4, 16),
            new ParamSpec("screw", "Vida deliği çapı", "mm", 2.5, 1, 6), new ParamSpec("lid", "Kapak kalınlığı", "mm", 2.5, 1, 8)
        };

        public static PartTemplate? Find(string? id) => id == null ? null : All.FirstOrDefault(t => t.Id == id);

        public static PolyMesh Build(PrimitiveSpec spec)
        {
            var t = Find(spec.Template);
            if (t == null) return PrimitiveBuilder.Box(20, 20, 20);
            double Get(string key)
            {
                var ps = t.Params.FirstOrDefault(p => p.Key == key);
                double v = spec.Values.TryGetValue(key, out double x) ? x : ps?.Default ?? 0;
                return ps != null ? ps.Clamp(v) : v;
            }

            return t.BuildMesh(Get);
        }

        public static PrimitiveSpec CreateSpec(string templateId)
        {
            var t = Find(templateId) ?? throw new ArgumentException("Bilinmeyen parça: " + templateId);
            var s = new PrimitiveSpec { Kind = PrimitiveKind.Part, Template = t.Id };
            foreach (var p in t.Params) s.Values[p.Key] = p.Default;
            return s;
        }

        /// <summary>Adds the part (and its companion, e.g. the lid of an enclosure) standing on the floor at (x, y).</summary>
        public static List<ModelObject> Insert(ModelDocument doc, string templateId, double x = 0, double y = 0)
        {
            var result = new List<ModelObject>();
            var t = Find(templateId) ?? throw new ArgumentException("Bilinmeyen parça: " + templateId);
            var o = new ModelObject { Name = t.Name, Primitive = CreateSpec(templateId), Material = t.Material, Color = t.Color };
            doc.Add(o);
            o.Transform.Position = new Vec3(x, y, PrimitiveSchema.BottomOffset(o.Primitive));
            result.Add(o);
            if (templateId == "enclosure")
            {
                var lt = Find("enclosure-lid")!;
                var lid = new ModelObject { Name = lt.Name, Primitive = CreateSpec("enclosure-lid"), Material = lt.Material, Color = lt.Color };
                doc.Add(lid);
                double h = o.LocalMesh().Bounds.Size.Z;
                lid.Transform.Position = new Vec3(x, y, h + 8 + PrimitiveSchema.BottomOffset(lid.Primitive));
                result.Add(lid);
            }

            return result;
        }

        // ------------------------------------------------------------------ helpers

        private static PolyMesh Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
            PrimitiveBuilder.Box(x1 - x0, y1 - y0, z1 - z0).Translate(new Vec3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2));

        /// <summary>Cylinder along an axis (0 = X, 1 = Y, 2 = Z) between two coordinates on that axis.</summary>
        private static PolyMesh Cyl(double d, int axis, double a0, double a1, double u = 0, double v = 0, int seg = HoleSeg)
        {
            var c = PrimitiveBuilder.Cylinder(d / 2, a1 - a0, seg);
            double mid = (a0 + a1) / 2;
            switch (axis)
            {
                case 0:
                    // Z → X
                    return c.Transform(Affine.Translate(new Vec3(mid, u, v)) * Affine.Rotate(Vec3.UnitY, 90));
                case 1:
                    return c.Transform(Affine.Translate(new Vec3(u, mid, v)) * Affine.Rotate(Vec3.UnitX, -90));
                default:
                    return c.Translate(new Vec3(u, v, mid));
            }
        }

        /// <summary>Rounded rectangle slab from z0 to z1 with optional holes (sketch shapes).</summary>
        private static PolyMesh Slab(double sx, double sy, double r, double z0, double z1, IEnumerable<SketchShape>? holes = null, double cx = 0, double cy = 0)
        {
            var outer = SketchShape.Rect(sx, sy, cx, cy, r).ToPolygon(40);
            var hs = holes?.Select(h => h.ToPolygon(HoleSeg)).ToList() ?? new List<List<Vec2>>();
            return PrimitiveBuilder.Extrude(outer, hs, z1 - z0).Translate(new Vec3(0, 0, (z0 + z1) / 2));
        }

        /// <summary>Triangular gusset in the plane y-z (profile points), thickness along X.</summary>
        private static PolyMesh GussetYZ(Vec2 a, Vec2 b, Vec2 c, double x0, double x1)
        {
            var prism = PrimitiveBuilder.Extrude(new List<Vec2> { a, b, c }, new List<List<Vec2>>(), x1 - x0);
            // profile x→Y, profile y→Z, extrusion z→X
            var m = new Affine(0, 0, 1, 1, 0, 0, 0, 1, 0, (x0 + x1) / 2, 0, 0);
            return prism.Transform(m);
        }

        /// <summary>Triangular gusset in the plane x-z, thickness along Y.</summary>
        private static PolyMesh GussetXZ(Vec2 a, Vec2 b, Vec2 c, double y0, double y1)
        {
            var prism = PrimitiveBuilder.Extrude(new List<Vec2> { a, b, c }, new List<List<Vec2>>(), y1 - y0);
            // profile x→X, profile y→Z, extrusion z→−Y (right-handed)
            var m = new Affine(1, 0, 0, 0, 0, -1, 0, 1, 0, 0, (y0 + y1) / 2, 0);
            return prism.Transform(m);
        }

        private static PolyMesh UnionAll(PolyMesh first, params PolyMesh[] rest) => Csg.Apply(first, rest, BooleanOp.Union);

        private static PolyMesh Subtract(PolyMesh first, IEnumerable<PolyMesh> cutters) => Csg.Apply(first, cutters, BooleanOp.Difference);

        // ------------------------------------------------------------------ parts

        private static PolyMesh Chassis2Wd(Func<string, double> p)
        {
            double l = p("l"), w = p("w"), t = p("t"), r = Math.Min(p("r"), Math.Min(l, w) / 2 - 1), mx = p("mx");
            var holes = new List<SketchShape>();
            // corner mounting holes
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }) holes.Add(SketchShape.Circle(3.2, sx * (l / 2 - Math.Max(6, r * 0.6)), sy * (w / 2 - Math.Max(6, r * 0.6))));
            // caster mount at the rear
            double cx = -l / 2 + 18;
            holes.Add(SketchShape.Circle(3.2, cx, -10));
            holes.Add(SketchShape.Circle(3.2, cx, 10));
            // cable slots and a board mounting pattern (Arduino Uno-ish 4 holes)
            holes.Add(SketchShape.Slot(24, 8, mx, 0));
            holes.Add(SketchShape.Slot(24, 8, -l / 2 + 40, 0));
            foreach (var (hx, hy) in new[] { (-20.0, -24.0), (28.0, -24.0), (-20.0, 24.0), (28.0, 24.0) })
            {
                if (Math.Abs(hx) < l / 2 - 12 && Math.Abs(hy) < w / 2 - 12) holes.Add(SketchShape.Circle(3.2, hx - 5, hy));
            }

            var plate = Slab(l, w, r, 0, t, holes);
            if (p("mounts") < 0.5) return plate;
            // TT motor mounting tabs hanging under the plate, holes along Y (17.5 mm apart).
            var tabs = new List<PolyMesh>();
            var cut = new List<PolyMesh>();
            foreach (int side in new[] { -1, 1 })
            {
                double y0 = side * (w / 2 - 22);
                double ya = Math.Min(y0, y0 + side * 3), yb = Math.Max(y0, y0 + side * 3);
                tabs.Add(Box(mx - 16, ya, -24, mx + 16, yb, t));
                cut.Add(Cyl(3.2, 1, ya - 1, yb + 1, mx - 8.75, -12));
                cut.Add(Cyl(3.2, 1, ya - 1, yb + 1, mx + 8.75, -12));
            }

            var body = UnionAll(plate, tabs.ToArray());
            return Subtract(body, cut);
        }

        private static PolyMesh Sg90Bracket(Func<string, double> p)
        {
            double w = p("w"), t = p("t"), h = p("h"), d = p("d");
            // base plate (y from 0 to d), upright at the back (y from d - t to d)
            var basePlate = Box(-w / 2, 0, 0, w / 2, d, t);
            var upright = Box(-w / 2, d - t, 0, w / 2, d, h);
            double g = Math.Min(d - t, h - t) * 0.6;
            var g1 = GussetYZ(new Vec2(d - t - g, t), new Vec2(d - t, t), new Vec2(d - t, t + g), -w / 2, -w / 2 + t);
            var g2 = GussetYZ(new Vec2(d - t - g, t), new Vec2(d - t, t), new Vec2(d - t, t + g), w / 2 - t, w / 2);
            var body = UnionAll(basePlate, upright, g1, g2);
            double zc = t + 4 + 12.6 / 2;
            var cuts = new List<PolyMesh>
            {
                Box(-23.2 / 2, d - t - 1, zc - 12.8 / 2, 23.2 / 2, d + 1, zc + 12.8 / 2),
                Cyl(2.0, 1, d - t - 1, d + 1, -13.8, zc),
                Cyl(2.0, 1, d - t - 1, d + 1, 13.8, zc),
                Cyl(3.2, 2, -1, t + 1, -w / 2 + 7, (d - t) / 2),
                Cyl(3.2, 2, -1, t + 1, w / 2 - 7, (d - t) / 2)
            };
            return Subtract(body, cuts);
        }

        private static PolyMesh LBracket(Func<string, double> p)
        {
            double a = p("a"), b = p("b"), w = p("w"), t = p("t"), hd = p("hd");
            int n = (int)Math.Round(p("n"));
            var leg1 = Box(0, -w / 2, 0, a, w / 2, t);
            var leg2 = Box(0, -w / 2, 0, t, w / 2, b);
            var parts = new List<PolyMesh> { leg2 };
            if (p("gusset") > 0.5 && a > t + 2 && b > t + 2)
            {
                double g = Math.Min(a - t, b - t) * 0.55, gt = Math.Min(2.0, w / 6);
                parts.Add(GussetXZ(new Vec2(t, t), new Vec2(t + g, t), new Vec2(t, t + g), -w / 2, -w / 2 + gt));
                parts.Add(GussetXZ(new Vec2(t, t), new Vec2(t + g, t), new Vec2(t, t + g), w / 2 - gt, w / 2));
            }

            var body = UnionAll(leg1, parts.ToArray());
            if (n <= 0 || hd <= 0) return body;
            var cuts = new List<PolyMesh>();
            for (int i = 0; i < n; i++)
            {
                double f = (i + 1.0) / (n + 1);
                double xa = t + (a - t) * f, zb = t + (b - t) * f;
                if (a - t > hd + 2) cuts.Add(Cyl(hd, 2, -1, t + 1, xa, 0));
                if (b - t > hd + 2) cuts.Add(Cyl(hd, 0, -1, t + 1, 0, zb));
            }

            return Subtract(body, cuts);
        }

        private static PolyMesh WheelHub(Func<string, double> p)
        {
            double d = p("d"), t = p("t"), bd = Math.Min(p("bd"), d - 2), bh = p("bh"), sd = Math.Min(p("sd"), bd - 2), sf = Math.Min(p("sf"), sd);
            int n = (int)Math.Round(p("n"));
            var disk = Cyl(d, 2, 0, t, 0, 0, 64);
            var boss = Cyl(bd, 2, 0, t + bh, 0, 0, 48);
            var body = Csg.Union(disk, boss);
            var cuts = new List<PolyMesh>();
            // double-D shaft: round shaft clipped by two flats
            var shaft = Csg.Intersection(Cyl(sd, 2, -1, t + bh + 1, 0, 0, 32), Box(-sf / 2, -sd, -1.5, sf / 2, sd, t + bh + 1.5));
            cuts.Add(shaft);
            double pitch = (bd / 2 + d / 2) / 2;
            double holeD = Math.Min((d / 2 - bd / 2) * 0.55, 2 * Math.PI * pitch / Math.Max(1, n) * 0.5);
            for (int i = 0; i < n && holeD > 1; i++)
            {
                double a = 2 * Math.PI * i / n;
                cuts.Add(Cyl(holeD, 2, -1, t + 1, pitch * Math.Cos(a), pitch * Math.Sin(a)));
            }

            // radial set-screw hole through the boss
            if (bh > 4) cuts.Add(Cyl(2.5, 0, 0, bd / 2 + 1, 0, t + bh / 2, 24));
            return Subtract(body, cuts);
        }

        private static PolyMesh Enclosure(Func<string, double> p, bool lid)
        {
            double x = p("x"), y = p("y"), z = p("z"), wall = Math.Min(p("wall"), Math.Min(x, y) / 4), r = Math.Min(p("r"), Math.Min(x, y) / 2 - 1);
            double boss = p("boss"), screw = Math.Min(p("screw"), boss - 1.5), lt = p("lid");
            double ix = x / 2 - wall - boss / 2 + 0.6, iy = y / 2 - wall - boss / 2 + 0.6;
            var corners = new[] { (-ix, -iy), (ix, -iy), (ix, iy), (-ix, iy) };
            if (lid)
            {
                var holes = corners.Select(c => SketchShape.Circle(Math.Min(screw + 0.8, boss - 0.5), c.Item1, c.Item2)).ToList();
                var plate = Slab(x, y, r, 0, lt, holes);
                // locating lip that fits inside the walls
                double gap = 0.3, lw = 1.2;
                double ox = x - 2 * wall - 2 * gap, oy = y - 2 * wall - 2 * gap;
                var lipOuter = Slab(ox, oy, Math.Max(0, r - wall), -2, 0.01);
                var lipInner = Slab(ox - 2 * lw, oy - 2 * lw, Math.Max(0, r - wall - lw), -2.5, 0.5);
                var lip = Csg.Difference(lipOuter, lipInner);
                // clear the lip around the screw towers
                lip = Subtract(lip, corners.Select(c => Cyl(boss + 1.2, 2, -3, 1, c.Item1, c.Item2)));
                return Csg.Union(plate, lip);
            }

            var outer = Slab(x, y, r, 0, z);
            var inner = Slab(x - 2 * wall, y - 2 * wall, Math.Max(0, r - wall), wall, z + 1);
            var shell = Csg.Difference(outer, inner);
            var towers = corners.Select(c => Cyl(boss, 2, wall - 0.01, z, c.Item1, c.Item2, 24)).ToArray();
            var body = Csg.Apply(shell, towers, BooleanOp.Union);
            var cuts = corners.Select(c => Cyl(screw, 2, wall + 1.5, z + 1, c.Item1, c.Item2, 20)).ToList();
            // a cable/USB opening in the front wall
            cuts.Add(Box(-6, -y / 2 - 1, wall + 4, 6, -y / 2 + wall + 1, wall + 12));
            return Subtract(body, cuts);
        }
    }
}
