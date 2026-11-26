using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[print] An FDM printer: its build volume and the slicer settings the checks and the estimate use.</summary>
    public sealed class PrinterProfile
    {
        public PrinterProfile(string id, string name, double x, double y, double z)
        {
            Id = id;
            Name = name;
            BedX = x;
            BedY = y;
            BedZ = z;
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>Build volume (mm).</summary>
        public double BedX { get; }

        public double BedY { get; }
        public double BedZ { get; }

        public double Nozzle { get; set; } = 0.4;
        public double LineWidth { get; set; } = 0.45;
        public double Layer { get; set; } = 0.2;
        public int Perimeters { get; set; } = 2;

        /// <summary>The steepest overhang printed without support (degrees from vertical).</summary>
        public double MaxOverhangDeg { get; set; } = 45;

        /// <summary>Average extrusion speed (mm/s) and filament diameter (mm).</summary>
        public double Speed { get; set; } = 60;

        public double Filament { get; set; } = 1.75;

        /// <summary>The thinnest wall the nozzle lays down whole: one line each side.</summary>
        public double MinWall => 2 * LineWidth;

        public static IReadOnlyList<PrinterProfile> All { get; } = new[]
        {
            new PrinterProfile("ender3", "Creality Ender-3 V3 SE", 220, 220, 250),
            new PrinterProfile("mk4", "Prusa MK4", 250, 210, 220),
            new PrinterProfile("mini", "Prusa MINI+", 180, 180, 180),
            new PrinterProfile("a1", "Bambu Lab A1", 256, 256, 256),
            new PrinterProfile("a1mini", "Bambu Lab A1 mini", 180, 180, 180),
            new PrinterProfile("voron", "Voron 2.4 (350)", 350, 350, 340) { Speed = 120 }
        };

        public static PrinterProfile Find(string? id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
    }

    /// <summary>[print] What printing a part as it stands would meet: the checks, and an estimate of the job.</summary>
    public sealed class PrintReport
    {
        public Vec3 Size { get; internal set; }
        public bool Closed { get; internal set; }
        public int OpenEdges { get; internal set; }
        public bool FitsBed { get; internal set; }

        /// <summary>Downward faces steeper than the printer bridges, off the bed (mm²), and which.</summary>
        public double OverhangArea { get; internal set; }

        public List<int> OverhangTriangles { get; } = new List<int>();

        /// <summary>The thinnest wall measured (mm), and the faces thinner than the printer lays down whole.</summary>
        public double ThinnestWall { get; internal set; } = double.PositiveInfinity;

        public double ThinArea { get; internal set; }
        public List<int> ThinTriangles { get; internal set; } = new List<int>();

        /// <summary>Area on the bed (mm²): too little, and the part tips or comes off.</summary>
        public double BedContact { get; internal set; }

        /// <summary>The estimate: filament (m, g), time (h), layers.</summary>
        public double FilamentM { get; internal set; }

        public double FilamentG { get; internal set; }
        public double Hours { get; internal set; }
        public int Layers { get; internal set; }

        /// <summary>A better way down, if one has much less overhang: the face direction to put on the bed, and its overhang.</summary>
        public Vec3? BetterDown { get; internal set; }

        public double BetterOverhang { get; internal set; }

        public bool NeedsSupport => OverhangArea > 1.0;

        public bool Printable => Closed && FitsBed && ThinArea < 1.0;
    }

    /// <summary>
    /// [print] The FDM checks a slicer would raise, on the part as placed (Z up, the bed at its lowest point): a closed
    /// mesh, the build volume, overhangs past the printer's angle that need support, walls thinner than two lines
    /// (measured by a ray from each face through the solid to the other side), the footprint on the bed, and the
    /// six ways down for the one with least overhang. The estimate counts perimeters on the surface, the infill inside
    /// and the support under the overhangs at the printer's flow.
    /// </summary>
    public static class PrintCheck
    {
        public static PrintReport Analyze(PolyMesh mesh, PrinterProfile printer, double infill = 0.2, double densityGcm3 = 1.24)
        {
            var report = new PrintReport();
            var tri = mesh.Triangulate();
            var bounds = mesh.Bounds;
            report.Size = bounds.Max - bounds.Min;
            var topo = tri.AnalyzeTopology();
            report.OpenEdges = topo.BoundaryEdges + topo.NonManifoldEdges;
            report.Closed = report.OpenEdges == 0;
            var s = report.Size;
            // Either way round on the bed.
            report.FitsBed = s.Z <= printer.BedZ && ((s.X <= printer.BedX && s.Y <= printer.BedY) || (s.Y <= printer.BedX && s.X <= printer.BedY));

            int n = tri.TriangleCount;
            if (n == 0) return report;
            var normals = new Vec3[n];
            var centres = new Vec3[n];
            var areas = new double[n];
            for (int t = 0; t < n; t++)
            {
                var (a, b, c) = tri.Triangle(t);
                var cross = (b - a).Cross(c - a);
                areas[t] = cross.Length / 2;
                normals[t] = areas[t] > 1e-12 ? cross / (2 * areas[t]) : new Vec3(0, 0, 0);
                centres[t] = (a + b + c) / 3;
            }

            double bedZ = bounds.Min.Z;
            report.OverhangArea = Overhang(normals, centres, areas, new Vec3(0, 0, -1), bedZ, printer, report.OverhangTriangles, out double contact);
            report.BedContact = contact;

            // The six ways down: the part's faces turned so each axis points at the bed in turn.
            double best = report.OverhangArea;
            foreach (var down in new[] { new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(-1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, -1, 0) })
            {
                // Heights measured along −down; the bed where the part reaches furthest along down.
                var rotatedN = new Vec3[n];
                var rotatedC = new Vec3[n];
                var (u, v) = Basis(down);
                for (int t = 0; t < n; t++)
                {
                    rotatedN[t] = new Vec3(normals[t].Dot(u), normals[t].Dot(v), -normals[t].Dot(down));
                    rotatedC[t] = new Vec3(centres[t].Dot(u), centres[t].Dot(v), -centres[t].Dot(down));
                }

                double over = Overhang(rotatedN, rotatedC, areas, new Vec3(0, 0, -1), -MaxAlong(tri, down), printer, null, out _);
                if (over < best * 0.7 - 1.0)
                {
                    best = over;
                    report.BetterDown = down;
                    report.BetterOverhang = over;
                }
            }

            // Walls: from each face straight through the solid; the other side's distance is the wall there.
            if (report.Closed)
            {
                var bvh = new MeshBvh(tri);
                double thinArea = 0;
                for (int t = 0; t < n; t++)
                {
                    if (areas[t] < 1e-9) continue;
                    var dir = normals[t] * -1;
                    var start = centres[t] + dir * 1e-4;
                    if (!bvh.Raycast(start, dir, 1000, out var hit)) continue;
                    // Only an exit through the other side counts: a face whose own (outward) normal looks the way the ray
                    // goes. The hit's normal is turned to face the ray, so the triangle's winding decides.
                    if (hit.Triangle < 0 || hit.Triangle >= n || normals[hit.Triangle].Dot(dir) <= 0) continue;
                    double wall = hit.Distance + 1e-4;
                    // A face that meets this one at its own edge is a corner, not a wall.
                    if (wall < 1e-3) continue;
                    if (wall < report.ThinnestWall) report.ThinnestWall = wall;
                    if (wall < printer.MinWall)
                    {
                        thinArea += areas[t];
                        report.ThinTriangles.Add(t);
                    }
                }

                report.ThinArea = thinArea;
            }

            // The estimate: perimeters over the surface, top and bottom shells, infill inside, support under overhangs.
            double volume = Math.Abs(mesh.Volume()), area = areas.Sum();
            double shell = Math.Min(volume, area * printer.Perimeters * printer.LineWidth * 0.5);
            double inner = Math.Max(0, volume - shell);
            double support = report.OverhangArea * Math.Max(0, (bounds.Max.Z - bounds.Min.Z) * 0.5) * 0.15;
            double extruded = shell + inner * Math.Max(0, Math.Min(1, infill)) + support;
            double flow = printer.Speed * printer.LineWidth * printer.Layer;   // mm³/s
            report.Layers = (int)Math.Ceiling(report.Size.Z / printer.Layer);
            double seconds = extruded / flow * 1.35 + report.Layers * 1.5;   // travel, retracts and layer changes
            report.Hours = seconds / 3600;
            double filamentArea = Math.PI * printer.Filament * printer.Filament / 4;
            report.FilamentM = extruded / filamentArea / 1000;
            report.FilamentG = extruded / 1000 * densityGcm3;
            return report;
        }

        private static double MaxAlong(MeshModel tri, Vec3 d)
        {
            double m = double.NegativeInfinity;
            foreach (var p in tri.Vertices) m = Math.Max(m, p.Dot(d));
            return m;
        }

        private static (Vec3 U, Vec3 V) Basis(Vec3 down)
        {
            var u = Math.Abs(down.Z) < 0.9 ? new Vec3(0, 0, 1).Cross(down).Normalized() : new Vec3(1, 0, 0).Cross(down).Normalized();
            var v = down.Cross(u).Normalized();
            return (u, v);
        }

        /// <summary>Area facing down more steeply than the printer bridges, leaving out what lies on the bed.</summary>
        private static double Overhang(Vec3[] normals, Vec3[] centres, double[] areas, Vec3 down, double bedZ, PrinterProfile printer, List<int>? faces, out double contact)
        {
            double limit = Math.Cos(printer.MaxOverhangDeg * Math.PI / 180);   // facing down within this of straight down
            double area = 0;
            contact = 0;
            for (int t = 0; t < normals.Length; t++)
            {
                double facing = normals[t].Dot(down);
                if (facing <= limit + 1e-9) continue;
                if (centres[t].Z - bedZ < printer.Layer * 0.5)
                {
                    contact += areas[t];   // on the bed
                    continue;
                }

                area += areas[t];
                faces?.Add(t);
            }

            return area;
        }
    }
}
