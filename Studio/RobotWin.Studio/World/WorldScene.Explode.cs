using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [explode] The exploded view's display offsets (SolidWorks' explode steps, played by the slider) and the clearance
    /// check's marker: the gap between two parts as a line with its two ends. Display only.
    /// </summary>
    public sealed partial class WorldScene
    {
        private Dictionary<string, Vector3D>? _steps;
        private (Point3D A, Point3D B)? _gap;

        /// <summary>The exploded view's steps are shown (some part is moved apart).</summary>
        public bool StepsExploded => _steps != null;

        /// <summary>Moves parts apart on screen by these offsets (world metres); null or empty puts them back.</summary>
        public void SetExplodeOffsets(IReadOnlyDictionary<string, Vec3>? offsets, bool cables = true)
        {
            _steps = offsets == null || offsets.Count == 0
                ? null
                : offsets.ToDictionary(kv => kv.Key, kv => new Vector3D(kv.Value.X, kv.Value.Y, kv.Value.Z), StringComparer.OrdinalIgnoreCase);
            UpdateKinematics();
            if (cables) RebuildCables();
            RebuildOverlay();
        }

        /// <summary>Marks a gap: a line between its two ends (world metres); null clears it.</summary>
        public void ShowGap(Vec3? a, Vec3? b)
        {
            _gap = a is { } p && b is { } q ? (new Point3D(p.X, p.Y, p.Z), new Point3D(q.X, q.Y, q.Z)) : null;
            RebuildOverlay();
        }

        /// <summary>A cable with a moved end runs slack between where its ends are shown (the settled rope belongs to the assembled parts).</summary>
        private bool ExplodedCable(RobotTwin.CoreSim.Sandbox.WorldObject a, RobotTwin.CoreSim.Sandbox.WorldObject b, ref Point3D pa, ref Point3D pb, out List<Point3D> path)
        {
            path = null!;
            if (_steps == null) return false;
            bool ma = _steps.TryGetValue(a.Id, out var oa), mb = _steps.TryGetValue(b.Id, out var ob);
            if (!ma && !mb) return false;
            pa += oa;
            pb += ob;
            double sag = (pb - pa).Length * 0.12;
            path = new List<Point3D>(17);
            for (int i = 0; i <= 16; i++)
            {
                double t = i / 16.0;
                var p = pa + (pb - pa) * t;
                path.Add(new Point3D(p.X, p.Y, p.Z - sag * 4 * t * (1 - t)));
            }

            return true;
        }

        private void AppendGap(Model3DGroup g)
        {
            if (_gap is not { } gap) return;
            var mark = new MaterialGroup();
            mark.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xF2, 0xB8, 0x3B))));
            mark.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x80, 0x5C, 0x10))));
            double r = Math.Max(0.0004, Math.Min(0.0012, (gap.B - gap.A).Length * 0.04));
            if ((gap.B - gap.A).Length > 1e-6) g.Children.Add(new GeometryModel3D(Edge(gap.A, gap.B, r * 0.6), mark));
            g.Children.Add(M.Model(M.Sphere(r * 1.8, gap.A.X, gap.A.Y, gap.A.Z, 10), mark));
            g.Children.Add(M.Model(M.Sphere(r * 1.8, gap.B.X, gap.B.Y, gap.B.Z, 10), mark));
        }
    }
}
