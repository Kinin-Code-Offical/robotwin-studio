using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[holes] What a hole is for (SolidWorks' Hole Wizard types, plus the 3D printer's heat-set insert).</summary>
    public enum HoleType
    {
        /// <summary>A plain clearance hole the screw passes through (ISO 273).</summary>
        Clearance,

        /// <summary>A clearance hole with a counterbore for a socket head cap screw (ISO 4762, DIN 974-1).</summary>
        Counterbore,

        /// <summary>A clearance hole with a 90° countersink for a flat head screw (ISO 10642, ISO 15065).</summary>
        Countersink,

        /// <summary>A tap drill hole for cutting the thread (coarse pitch).</summary>
        Tapped,

        /// <summary>A hole for a heat-set threaded insert pressed into a printed part.</summary>
        Insert
    }

    /// <summary>[holes] ISO 273's clearance classes.</summary>
    public enum HoleFit
    {
        Close,
        Normal,
        Loose
    }

    /// <summary>[holes] A hole as the wizard asks for it.</summary>
    public sealed class DrillSpec
    {
        public HoleType Type { get; set; } = HoleType.Clearance;
        public string Size { get; set; } = "M3";
        public HoleFit Fit { get; set; } = HoleFit.Normal;

        /// <summary>Depth of the bore (mm) from the surface; 0: through the part.</summary>
        public double Depth { get; set; }
    }

    /// <summary>A size's figures (mm).</summary>
    public readonly struct HoleSize
    {
        public HoleSize(string name, double close, double normal, double loose, double headDia, double counterbore, double counterboreDepth, double countersink, double tapDrill, double insertDia, double insertDepth)
        {
            Name = name;
            Close = close;
            Normal = normal;
            Loose = loose;
            HeadDia = headDia;
            Counterbore = counterbore;
            CounterboreDepth = counterboreDepth;
            Countersink = countersink;
            TapDrill = tapDrill;
            InsertDia = insertDia;
            InsertDepth = insertDepth;
        }

        public string Name { get; }
        public double Close { get; }
        public double Normal { get; }
        public double Loose { get; }
        public double HeadDia { get; }
        public double Counterbore { get; }
        public double CounterboreDepth { get; }
        public double Countersink { get; }
        public double TapDrill { get; }
        public double InsertDia { get; }
        public double InsertDepth { get; }
    }

    /// <summary>
    /// [holes] SolidWorks' Hole Wizard for the part editor: a hole made to a screw size, not to a guessed diameter. The
    /// figures are the standards' (ISO 273 clearance classes, DIN 974-1 counterbores for ISO 4762 cap screws, ISO 15065
    /// countersinks for ISO 10642 flat heads, coarse-pitch tap drills, common heat-set insert holes). The hole's tool solids
    /// are built in its own frame (the surface at the origin, the outward normal along +Z) and placed where it is drilled.
    /// </summary>
    public static class HoleWizard
    {
        // name, clearance close / normal / loose, cap screw head Ø, counterbore Ø, counterbore depth, countersink Ø, tap drill, insert hole Ø, insert depth
        public static readonly IReadOnlyList<HoleSize> Sizes = new[]
        {
            new HoleSize("M2", 2.2, 2.4, 2.6, 3.8, 4.3, 2.4, 4.4, 1.6, 3.2, 4.0),
            new HoleSize("M2.5", 2.7, 2.9, 3.1, 4.5, 5.0, 2.9, 5.5, 2.05, 3.6, 5.7),
            new HoleSize("M3", 3.2, 3.4, 3.6, 5.5, 6.0, 3.4, 6.9, 2.5, 4.0, 5.7),
            new HoleSize("M4", 4.3, 4.5, 4.8, 7.0, 8.0, 4.4, 9.2, 3.3, 5.6, 8.1),
            new HoleSize("M5", 5.3, 5.5, 5.8, 8.5, 10.0, 5.4, 11.5, 4.2, 6.4, 9.5),
            new HoleSize("M6", 6.4, 6.6, 7.0, 10.0, 11.0, 6.4, 13.7, 5.0, 8.0, 12.7),
            new HoleSize("M8", 8.4, 9.0, 10.0, 13.0, 15.0, 8.6, 18.3, 6.8, 9.7, 12.7)
        };

        public static HoleSize Find(string size) =>
            Sizes.FirstOrDefault(s => string.Equals(s.Name, size, StringComparison.OrdinalIgnoreCase)) is { Name: not null } s ? s : Sizes[2];

        /// <summary>The bore's diameter (mm): the clearance class, the tap drill or the insert hole.</summary>
        public static double BoreDiameter(DrillSpec spec)
        {
            var s = Find(spec.Size);
            return spec.Type switch
            {
                HoleType.Tapped => s.TapDrill,
                HoleType.Insert => s.InsertDia,
                _ => spec.Fit == HoleFit.Close ? s.Close : spec.Fit == HoleFit.Loose ? s.Loose : s.Normal
            };
        }

        /// <summary>The depth a hole of this kind takes when none is given (mm); 0 = through.</summary>
        public static double DefaultDepth(DrillSpec spec) => spec.Type == HoleType.Insert ? Find(spec.Size).InsertDepth + 0.5 : 0;

        /// <summary>One line describing the hole ("M3 havşa başlı · Ø3,4 / Ø6,9 90° · boydan boya").</summary>
        public static string Describe(DrillSpec spec)
        {
            var s = Find(spec.Size);
            string F(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);
            double depth = spec.Depth > 0 ? spec.Depth : DefaultDepth(spec);
            string deep = depth > 0 ? $"{F(depth)} mm derin" : "boydan boya";
            return spec.Type switch
            {
                HoleType.Counterbore => $"{s.Name} silindir başlı · Ø{F(BoreDiameter(spec))} / Ø{F(s.Counterbore)} × {F(s.CounterboreDepth)} · {deep}",
                HoleType.Countersink => $"{s.Name} havşa başlı · Ø{F(BoreDiameter(spec))} / Ø{F(s.Countersink)} 90° · {deep}",
                HoleType.Tapped => $"{s.Name} kılavuz deliği · Ø{F(s.TapDrill)} · {deep}",
                HoleType.Insert => $"{s.Name} gömme somun · Ø{F(s.InsertDia)} · {deep}",
                _ => $"{s.Name} geçiş deliği · Ø{F(BoreDiameter(spec))} · {deep}"
            };
        }

        /// <summary>
        /// The solids to subtract, in the hole's frame (surface at z = 0, material below): the bore down to the depth
        /// (<paramref name="through"/> when the hole goes through), and the counterbore or countersink at its mouth. Each
        /// starts a little above the surface so no face of it lies on the part's.
        /// </summary>
        public static List<PolyMesh> Tools(DrillSpec spec, double through, int segments = 40)
        {
            var s = Find(spec.Size);
            const double lift = 0.5;
            double depth = spec.Depth > 0 ? spec.Depth : DefaultDepth(spec);
            if (depth <= 0) depth = Math.Max(1, through);
            var tools = new List<PolyMesh> { Column(BoreDiameter(spec) / 2, lift, -depth, segments) };
            if (spec.Type == HoleType.Counterbore)
                tools.Add(Column(s.Counterbore / 2, lift, -Math.Min(depth, s.CounterboreDepth), segments));
            if (spec.Type == HoleType.Countersink)
            {
                double r = BoreDiameter(spec) / 2, R = s.Countersink / 2;
                double sink = Math.Min(depth, R - r);   // 90°: as deep as it is wide on each side
                var cone = PrimitiveBuilder.Cone(r, R + lift, sink + lift, segments);
                cone.Transform(Affine.Translate(new Vec3(0, 0, (lift - sink) / 2)));
                tools.Add(cone);
            }

            return tools;
        }

        private static PolyMesh Column(double r, double top, double bottom, int segments)
        {
            var c = PrimitiveBuilder.Cylinder(r, top - bottom, segments);
            c.Transform(Affine.Translate(new Vec3(0, 0, (top + bottom) / 2)));
            return c;
        }

        /// <summary>The hole's frame: the surface point, its outward normal along +Z.</summary>
        public static Affine Frame(Vec3 point, Vec3 normal)
        {
            var n = normal.Normalized();
            if (n.LengthSquared < 1e-12) n = Vec3.UnitZ;
            var x = n.AnyPerpendicular().Normalized();
            var y = n.Cross(x);
            return new Affine(x.X, y.X, n.X, x.Y, y.Y, n.Y, x.Z, y.Z, n.Z, point.X, point.Y, point.Z);
        }

        /// <summary>
        /// Drills the hole into an object at a world point, along the surface's outward normal there: its tools are made,
        /// subtracted and dropped. Returns the object.
        /// </summary>
        public static ModelObject Drill(ModelDocument doc, ModelObject target, Vec3 point, Vec3 normal, DrillSpec spec)
        {
            var frame = Frame(point, normal);
            var n = normal.Normalized();
            // through: as deep as the part reaches behind the surface, and a millimetre more
            double through = doc.WorldMesh(target).Vertices.Select(v => (point - v).Dot(n)).DefaultIfEmpty(0).Max() + 1;
            var tools = Tools(spec, through).Select((m, i) =>
            {
                m.Transform(frame);
                return ModelOps.AddMesh(doc, m, "delik " + i);
            }).ToList();
            return ModelOps.Boolean(doc, target, tools, BooleanOp.Difference);
        }
    }
}
