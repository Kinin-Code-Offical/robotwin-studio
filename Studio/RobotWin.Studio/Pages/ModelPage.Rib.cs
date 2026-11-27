using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using RobotWin.Studio.Modeling;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [rib] SolidWorks' rib in the modeller: a line in one of the part's own planes (its two ends, the plane's place)
    /// and a thickness; the rib fills from the line to the part and goes into its feature history (CoreSim
    /// Modeling/ModelFeatures.AddRib). The line starts across the part's corner, from its own bounds.
    /// </summary>
    public partial class ModelPage
    {
        /// <summary>The planes: their name, the axes of their U and V, and their normal (the part's own axes).</summary>
        private static readonly (string Name, (int U, int V, int N) Axes)[] RibPlanes = { ("XZ", (0, 2, 1)), ("YZ", (1, 2, 0)), ("XY", (0, 1, 2)) };

        private static double Get(Vec3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static Vec3 Put(int u, double a, int v, double b, int n, double c)
        {
            var x = new double[3];
            x[u] = a;
            x[v] = b;
            x[n] = c;
            return new Vec3(x[0], x[1], x[2]);
        }

        private void RibDialog(string name, (int U, int V, int N) axes)
        {
            var o = _ed.Active;
            if (o == null || o.IsReference || o.LocalMesh().IsEmpty)
            {
                _ed.Status("Kaburga için bir parça seçin");
                return;
            }

            var b = o.LocalMesh().Bounds;
            double u0 = Get(b.Min, axes.U), u1 = Get(b.Max, axes.U), v0 = Get(b.Min, axes.V), v1 = Get(b.Max, axes.V);
            double w = u1 - u0, h = v1 - v0;
            var r = ParamDialog.Show(Owner, "Kaburga (" + name + " düzlemi)",
                "Parçanın kendi " + name + " düzleminde bir çizgi: kaburga bu çizgiden parçaya doğru dolar ve verilen kalınlıkta, düzlemin iki yanına eşit olur. Ölçüler parçanın kendi ekseninde, mm.",
                ("Başlangıç " + name[0] + " (mm)", Math.Round(u0 + 0.1 * w, 2)), ("Başlangıç " + name[1] + " (mm)", Math.Round(v0 + 0.75 * h, 2)),
                ("Bitiş " + name[0] + " (mm)", Math.Round(u0 + 0.75 * w, 2)), ("Bitiş " + name[1] + " (mm)", Math.Round(v0 + 0.1 * h, 2)),
                ("Düzlemin yeri (mm)", Math.Round((Get(b.Min, axes.N) + Get(b.Max, axes.N)) / 2, 2)), ("Kalınlık (mm)", 3));
            if (r == null) return;
            var start = Put(axes.U, r[0], axes.V, r[1], axes.N, r[4]);
            var end = Put(axes.U, r[2], axes.V, r[3], axes.N, r[4]);
            var normal = Put(axes.U, 0, axes.V, 0, axes.N, 1);
            _ed.Do("Kaburga eklendi", () => ModelFeatures.AddRib(o, start, end, normal, Math.Max(0.01, r[5])), ModelChange.All);
        }
    }
}
