using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>A material of the modeller (names match the world's material names).</summary>
    public sealed class ModelMaterial
    {
        public ModelMaterial(string name, double density, string color, bool printed)
        {
            Name = name;
            Density = density;
            Color = color;
            Printed = printed;
        }

        public string Name { get; }

        /// <summary>Solid density (kg/m³).</summary>
        public double Density { get; }

        /// <summary>Typical colour "#RRGGBB".</summary>
        public string Color { get; }

        /// <summary>FDM printed: infill reduces the effective density.</summary>
        public bool Printed { get; }

        /// <summary>Effective density for an infill fraction (kg/m³).</summary>
        public double EffectiveDensity(double infill) => Printed ? MaterialDensity.Printed(Density, infill) : Density;

        public override string ToString() => Name;
    }

    public static class ModelMaterials
    {
        public static IReadOnlyList<ModelMaterial> All { get; } = new[]
        {
            new ModelMaterial("PLA", MaterialDensity.Pla, "#E87A2C", true),
            new ModelMaterial("PETG", MaterialDensity.Petg, "#2E7DB8", true),
            new ModelMaterial("ABS", MaterialDensity.Abs, "#3A3A40", true),
            new ModelMaterial("NAYLON", MaterialDensity.Nylon, "#E8E4D8", true),
            new ModelMaterial("AKRİLİK", MaterialDensity.Acrylic, "#9EC9E0", false),
            new ModelMaterial("KONTRPLAK", MaterialDensity.Plywood, "#C8A06E", false),
            new ModelMaterial("MDF", MaterialDensity.Mdf, "#B89468", false),
            new ModelMaterial("FR4", MaterialDensity.Fr4, "#1E5631", false),
            new ModelMaterial("ALÜMİNYUM", MaterialDensity.Aluminium, "#B8BCC4", false),
            new ModelMaterial("ÇELİK", MaterialDensity.Steel, "#8A8E96", false),
            new ModelMaterial("KARBON", MaterialDensity.CarbonFibre, "#2A2A2E", false),
            new ModelMaterial("KAUÇUK", 1100, "#242427", false)
        };

        public static ModelMaterial Get(string? name) =>
            All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }

    /// <summary>Measurements of a solid (millimetre units, grams).</summary>
    public sealed class ModelStats
    {
        public Aabb Bounds { get; set; } = Aabb.Empty;
        public Vec3 Size => Bounds.Size;
        public double VolumeMm3 { get; set; }
        public double AreaMm2 { get; set; }
        public double MassG { get; set; }
        public Vec3 CenterOfMass { get; set; }
        public bool Closed { get; set; }
        public int Vertices { get; set; }
        public int Faces { get; set; }
        public int Triangles { get; set; }
        public double DensityKgM3 { get; set; }

        /// <summary>[mass-props] Inertia tensor about the centre of mass along the world axes (g·mm²), as SolidWorks' Mass Properties gives it.</summary>
        public Mat3 InertiaGmm2 { get; set; } = Mat3.Zero;

        /// <summary>[mass-props] Principal moments of inertia about the centre of mass (g·mm²), smallest first as the solver orders them.</summary>
        public Vec3 PrincipalMomentsGmm2 { get; set; }

        /// <summary>[mass-props] The principal axes (unit vectors in world coordinates), one per principal moment.</summary>
        public Vec3[] PrincipalAxes { get; set; } = { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1) };

        /// <summary>The same in SI units (kg, m), kept to add selections together.</summary>
        public MassProperties? Properties { get; set; }

        /// <summary>
        /// [mass-props] The report as SolidWorks' Mass Properties dialog writes it (copied to the clipboard): mass, volume,
        /// area, centre of mass, principal axes and moments, the tensor about the centre of mass.
        /// </summary>
        public string Report(string title, Func<string, string>? translate = null)
        {
            var t = translate ?? (x => x);
            var c = System.Globalization.CultureInfo.InvariantCulture;
            string F(double v, string f = "0.###") => v.ToString(f, c);
            string V(Vec3 v, string f = "0.###") => F(v.X, f) + ", " + F(v.Y, f) + ", " + F(v.Z, f);
            var b = new System.Text.StringBuilder();
            b.AppendLine(t("Kütle özellikleri") + ": " + title);
            b.AppendLine(t("Kütle") + " = " + F(MassG) + " g");
            b.AppendLine(t("Hacim") + " = " + F(VolumeMm3) + " mm³");
            b.AppendLine(t("Yüzey alanı") + " = " + F(AreaMm2) + " mm²");
            b.AppendLine(t("Ağırlık merkezi") + " (mm): " + V(CenterOfMass));
            b.AppendLine(t("Asal eksenler ve asal atalet momentleri (g·mm², ağırlık merkezinde)") + ":");
            for (int i = 0; i < 3; i++)
            {
                double m = i == 0 ? PrincipalMomentsGmm2.X : i == 1 ? PrincipalMomentsGmm2.Y : PrincipalMomentsGmm2.Z;
                b.AppendLine($"    I{i + 1} = ({V(PrincipalAxes[i], "0.####")})    P{i + 1} = {F(m, "0.##")}");
            }

            b.AppendLine(t("Atalet momentleri (g·mm², ağırlık merkezinde, dünya eksenleri)") + ":");
            var I = InertiaGmm2;
            b.AppendLine($"    Lxx = {F(I.M00, "0.##")}    Lxy = {F(I.M01, "0.##")}    Lxz = {F(I.M02, "0.##")}");
            b.AppendLine($"    Lyx = {F(I.M10, "0.##")}    Lyy = {F(I.M11, "0.##")}    Lyz = {F(I.M12, "0.##")}");
            b.AppendLine($"    Lzx = {F(I.M20, "0.##")}    Lzy = {F(I.M21, "0.##")}    Lzz = {F(I.M22, "0.##")}");
            if (!Closed) b.AppendLine(t("Not: ağ kapalı değil; değerler tahmindir."));
            return b.ToString();
        }

        /// <summary>Fills the inertia fields from SI mass properties (the mesh already in world coordinates).</summary>
        internal void SetInertia(MassProperties mp)
        {
            const double ToGmm2 = 1e9;   // kg·m² → g·mm²
            Properties = mp;
            InertiaGmm2 = mp.Inertia * ToGmm2;
            PrincipalMomentsGmm2 = mp.PrincipalMoments * ToGmm2;
            var axes = Mat3.FromQuat(mp.PrincipalAxes);
            PrincipalAxes = new[] { axes.Column(0), axes.Column(1), axes.Column(2) };
        }

        public override string ToString() =>
            $"{Size.X:0.##}×{Size.Y:0.##}×{Size.Z:0.##} mm, {VolumeMm3 / 1000:0.###} cm³, {MassG:0.##} g{(Closed ? string.Empty : " (açık ağ)")}";
    }

    public static class ModelMeasure
    {
        /// <summary>Measures a mesh in millimetres with a material (density from material and infill).</summary>
        public static ModelStats Of(PolyMesh mesh, string material = "PLA", double infill = 1.0)
        {
            var mat = ModelMaterials.Get(material);
            double density = mat.EffectiveDensity(infill);
            var stats = new ModelStats
            {
                Bounds = mesh.Bounds,
                Vertices = mesh.Vertices.Count,
                Faces = mesh.Faces.Count,
                AreaMm2 = mesh.SurfaceArea(),
                DensityKgM3 = density
            };
            if (mesh.Faces.Count == 0) return stats;
            var tri = mesh.Triangulate();
            stats.Triangles = tri.TriangleCount;
            stats.Closed = tri.AnalyzeTopology().IsClosed;
            var metres = tri.Clone();
            metres.Scale(0.001);
            var mp = MassProperties.FromMesh(metres, density);
            stats.VolumeMm3 = Math.Abs(mp.Volume) * 1e9;
            stats.MassG = Math.Abs(mp.Mass) * 1000;
            stats.CenterOfMass = mp.CenterOfMass * 1000;
            stats.SetInertia(mp);   // [mass-props]
            return stats;
        }

        /// <summary>Measures an object in document coordinates.</summary>
        public static ModelStats Of(ModelDocument doc, ModelObject o) => Of(doc.WorldMesh(o), o.Material, o.Infill);

        /// <summary>Total over several objects (each with its own material).</summary>
        public static ModelStats Total(ModelDocument doc, IEnumerable<ModelObject> objects)
        {
            var total = new ModelStats { Closed = true };
            var b = Aabb.Empty;
            double massG = 0;
            var com = Vec3.Zero;
            var parts = new List<MassPart>();
            foreach (var o in objects)
            {
                var s = Of(doc, o);
                if (s.Properties != null) parts.Add(new MassPart(s.Properties, Vec3.Zero));   // [mass-props] already in world coordinates
                b = b.Union(s.Bounds);
                total.VolumeMm3 += s.VolumeMm3;
                total.AreaMm2 += s.AreaMm2;
                massG += s.MassG;
                com += s.CenterOfMass * s.MassG;
                total.Closed &= s.Closed;
                total.Vertices += s.Vertices;
                total.Faces += s.Faces;
                total.Triangles += s.Triangles;
            }

            total.Bounds = b;
            total.MassG = massG;
            total.CenterOfMass = massG > 0 ? com / massG : b.Center;
            if (parts.Count > 0) total.SetInertia(MassProperties.Combine(parts));   // [mass-props] parallel axis theorem
            total.DensityKgM3 = total.VolumeMm3 > 0 ? massG / 1000 / (total.VolumeMm3 * 1e-9) : 0;
            return total;
        }

        /// <summary>Distance between two points (mm) and its axis components.</summary>
        public static (double Distance, Vec3 Delta) Distance(Vec3 a, Vec3 b) => ((b - a).Length, b - a);
    }
}
