using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Analysis.Export
{
    /// <summary>
    /// Writes a stress result's critical instant as an Abaqus / CalculiX input deck (.inp): the same
    /// mesh (C3D10 / C3D4 — RobotWin's T10 node order is the C3D10 order), material, supports and
    /// consistent nodal loads (contacts, joint reactions, gravity and the inertia-relief body forces).
    /// CalculiX (ccx) solves it as is; ANSYS Mechanical APDL and Abaqus import it. RobotWin's own
    /// results are written as comments so the two solutions can be compared directly.
    /// </summary>
    public static class InpExport
    {
        public static bool CanExport(StressResult r) =>
            r.Method == "tet" && r.ExportForces != null && r.ExportFixed != null && r.NodeCount > 0 && r.Elements.Length > 0;

        public static string Write(StressResult r)
        {
            if (!CanExport(r)) throw new InvalidOperationException("Bu sonuç dışa aktarılamaz (yalnız dörtyüzlü FEM sonuçları).");
            var ci = CultureInfo.InvariantCulture;
            var crit = r.Frames.OrderByDescending(f => f.MaxVonMises).First();
            int npe = r.NodesPerElement;
            var sb = new StringBuilder();
            string F(double v) => v.ToString("0.#########E+00", ci);
            sb.AppendLine("** RobotWin Studio - stress case export (Abaqus / CalculiX input)");
            sb.AppendLine($"** Part: {r.Name} ({r.ObjectId}), material {r.Material}, E = {F(r.ExportYoung)} Pa, nu = {r.ExportPoisson.ToString(ci)}");
            sb.AppendLine($"** Critical instant t = {r.ExportTime.ToString("0.######", ci)} s; units: m, N, Pa; coordinates in the part's body frame");
            sb.AppendLine($"** RobotWin result: max von Mises = {F(crit.MaxVonMises)} Pa, 98th percentile = {F(Jobs.StressJob.P98(crit.VonMises))} Pa, max displacement = {F(crit.MaxDisplacement)} m");
            sb.AppendLine("** Compare the 98th percentile: the absolute peak sits under point loads, where every FEM code's stress grows with refinement.");
            sb.AppendLine("*HEADING");
            sb.AppendLine("RobotWin " + r.Name);
            sb.AppendLine("*NODE, NSET=NALL");
            for (int i = 0; i < r.NodeCount; i++)
            {
                var p = r.Node(i);
                sb.Append(i + 1).Append(", ").Append(F(p.X)).Append(", ").Append(F(p.Y)).Append(", ").AppendLine(F(p.Z));
            }

            sb.AppendLine($"*ELEMENT, TYPE={(npe == 10 ? "C3D10" : "C3D4")}, ELSET=EALL");
            for (int e = 0; e < r.Elements.Length / npe; e++)
            {
                sb.Append(e + 1);
                for (int k = 0; k < npe; k++) sb.Append(", ").Append(r.Elements[e * npe + k] + 1);
                sb.AppendLine();
            }

            sb.AppendLine("*MATERIAL, NAME=MAT");
            sb.AppendLine("*ELASTIC");
            sb.AppendLine($"{F(r.ExportYoung)}, {r.ExportPoisson.ToString(ci)}");
            sb.AppendLine("*SOLID SECTION, ELSET=EALL, MATERIAL=MAT");
            sb.AppendLine("*BOUNDARY");
            foreach (int dof in r.ExportFixed!) sb.Append(dof / 3 + 1).Append(", ").Append(dof % 3 + 1).Append(", ").Append(dof % 3 + 1).AppendLine(", 0.");
            sb.AppendLine("*STEP");
            sb.AppendLine("*STATIC");
            sb.AppendLine("*CLOAD");
            var f = r.ExportForces!;
            for (int dof = 0; dof < f.Length; dof++)
            {
                if (Math.Abs(f[dof]) < 1e-14) continue;
                sb.Append(dof / 3 + 1).Append(", ").Append(dof % 3 + 1).Append(", ").AppendLine(F(f[dof]));
            }

            sb.AppendLine("*NODE FILE");
            sb.AppendLine("U");
            sb.AppendLine("*EL FILE");
            sb.AppendLine("S");
            sb.AppendLine("*END STEP");
            return sb.ToString();
        }

        public static void Save(StressResult r, string path) => File.WriteAllText(path, Write(r), new UTF8Encoding(false));
    }
}
