using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [shell] Seeing inside hollow bodies: X-ray makes shells and lids see-through, the exploded
    /// view lifts lids and the parts inside apart (display only: the simulation is untouched).
    /// </summary>
    public sealed partial class WorldScene
    {
        private readonly Dictionary<GeometryModel3D, (Material? Front, Material? Back)> _xrayed = new Dictionary<GeometryModel3D, (Material?, Material?)>();
        private Dictionary<string, Vector3D>? _explode;

        /// <summary>
        /// The cable-layout physics used while stopped (cables at rest), or null before cables were laid out. [perf] A layout
        /// still settling on its worker is waited for first (it is never used from two threads at once).
        /// </summary>
        public WorldPhysics? CableLayout
        {
            get
            {
                if (_settleTask is { IsCompleted: false } settling) settling.Wait(3000);
                return _cableLayout;
            }
        }

        public bool XRay { get; private set; }

        public bool Exploded => _explode != null;

        /// <summary>Shells and lids become 25 % opaque so the parts and cables inside show.</summary>
        public void SetXRay(bool on)
        {
            foreach (var kv in _xrayed)
            {
                kv.Key.Material = kv.Value.Front;
                kv.Key.BackMaterial = kv.Value.Back;
            }

            _xrayed.Clear();
            XRay = on;
            if (!on || World == null) return;
            foreach (var it in _items.Values)
            {
                if (!WorldPhysics.IsShell(it.Object) && WorldPhysics.LidOf(it.Object) == null) continue;
                foreach (var gm in Geometry(it.Visual.Content))
                {
                    if (gm.IsFrozen) continue;
                    _xrayed[gm] = (gm.Material, gm.BackMaterial);
                    var c = ColourOf(gm.Material) ?? Color.FromRgb(0xC8, 0xC8, 0xD0);
                    var glass = new MaterialGroup();
                    glass.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B))));
                    glass.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 40));
                    gm.Material = glass;
                    gm.BackMaterial = glass;
                }
            }
        }

        /// <summary>Exploded view: every lid rises 1.5 shell heights, the parts inside a shell 0.6 of it.</summary>
        public void SetExploded(bool on)
        {
            _explode = null;
            if (on && World != null)
            {
                var offsets = new Dictionary<string, Vector3D>(StringComparer.OrdinalIgnoreCase);
                foreach (var shell in World.Objects.Where(WorldPhysics.IsShell))
                {
                    double h = shell.SizeZ;
                    var up = shell.Pose.Orientation.Rotate(RobotTwin.CoreSim.Mechanics.Vec3.UnitZ);
                    Vector3D Up(double k) => new Vector3D(up.X * h * k, up.Y * h * k, up.Z * h * k);
                    foreach (var o in World.Objects)
                    {
                        if (string.Equals(WorldPhysics.LidOf(o), shell.Id, StringComparison.OrdinalIgnoreCase)) offsets[o.Id] = Up(1.5);
                        else if (o != shell && Inside(shell, o)) offsets[o.Id] = Up(0.6);
                    }
                }

                _explode = offsets;
            }

            UpdateKinematics();
            RebuildCables();
        }

        /// <summary>The object's centre lies within the shell's footprint and height (in the shell's frame).</summary>
        private static bool Inside(WorldObject shell, WorldObject o)
        {
            var d = new RobotTwin.CoreSim.Mechanics.Vec3(o.Pose.X - shell.Pose.X, o.Pose.Y - shell.Pose.Y, o.Pose.Z - shell.Pose.Z);
            var l = shell.Pose.Orientation.InverseRotate(d);
            return Math.Abs(l.X) < shell.SizeX / 2 && Math.Abs(l.Y) < shell.SizeY / 2 && l.Z >= -0.001 && l.Z < shell.SizeZ;
        }

        private Matrix3D ExplodedMatrix(string id, Matrix3D current)
        {
            if (_explode == null || !_explode.TryGetValue(id, out var off)) return current;
            current.Translate(off);
            return current;
        }

        private static IEnumerable<GeometryModel3D> Geometry(Model3D? m)
        {
            switch (m)
            {
                case GeometryModel3D g:
                    yield return g;
                    break;
                case Model3DGroup grp:
                    foreach (var c in grp.Children)
                        foreach (var x in Geometry(c))
                            yield return x;
                    break;
            }
        }

        private static Color? ColourOf(Material? m) => m switch
        {
            DiffuseMaterial { Brush: SolidColorBrush b } => b.Color,
            MaterialGroup g => g.Children.Select(ColourOf).FirstOrDefault(c => c != null),
            _ => null
        };
    }
}
