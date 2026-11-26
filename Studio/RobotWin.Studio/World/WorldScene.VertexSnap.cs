using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [vertex-snap] Unity's vertex snapping (hold V while moving): the corner of the dragged part nearest the cursor is
    /// taken at the start, and while the cursor comes within a few pixels of another part's corner the part moves so
    /// that the two corners meet exactly. A part's corners are those of its shape's box in its own axes (8, turned with
    /// it), so boxes, boards and parts line up edge to edge. Works on a free drag and on the move handle's middle.
    /// </summary>
    public sealed partial class WorldScene
    {
        private Vector3D? _vertexOffset;

        /// <summary>How near (pixels) the cursor must come to a corner for it to take the part.</summary>
        private const double VertexReach = 18;

        private static bool VertexSnapHeld => Keyboard.IsKeyDown(Key.V);

        /// <summary>
        /// The snapshot's --vertex-snap: the selected part moved so that its corner nearest the other part meets that part's
        /// nearest corner, as a V drag ending there would. True when it moved.
        /// </summary>
        public bool SnapSelectedCornerOnto(string targetId)
        {
            if (World?.Find(Selected) is not { } o || !_items.TryGetValue(o.Id, out var it) || !_items.TryGetValue(targetId, out var target)) return false;
            var pairs = Corners(it).SelectMany(a => Corners(target).Select(b => (A: a, B: b))).ToList();
            if (pairs.Count == 0) return false;
            var (from, to) = pairs.OrderBy(x => (x.B - x.A).Length).First();
            BeginEdit?.Invoke();
            World.MoveAssembly(o, to.X - from.X, to.Y - from.Y, to.Z - from.Z);
            EditCommitted?.Invoke($"{o.Name}: köşesi {target.Object.Name} köşesine oturdu");
            return true;
        }

        /// <summary>A part's 8 corners in the world: its shape's box in its own axes, placed as it is now.</summary>
        private static IEnumerable<Point3D> Corners(Item it)
        {
            var b = it.Model.Bounds;
            if (b.IsEmpty) yield break;
            for (int i = 0; i < 8; i++)
                yield return it.Current.Transform(new Point3D(b.X + ((i & 1) != 0 ? b.SizeX : 0), b.Y + ((i & 2) != 0 ? b.SizeY : 0), b.Z + ((i & 4) != 0 ? b.SizeZ : 0)));
        }

        /// <summary>
        /// With V held: the position that puts the dragged part's grabbed corner on the corner nearest the cursor, and true;
        /// false (and nothing changed) when V is up or no corner is near.
        /// </summary>
        private bool VertexSnap(WorldObject o, Point p, ref double nx, ref double ny, ref double nz)
        {
            if (!VertexSnapHeld || World == null || !_items.TryGetValue(o.Id, out var it))
            {
                _vertexOffset = null;
                return false;
            }

            if (_vertexOffset == null)
            {
                // the corner of the dragged part nearest the cursor is the one carried
                var grab = Corners(it).Select(c => (C: c, D: (Project(c).Screen - p).Length)).OrderBy(x => x.D).FirstOrDefault();
                if (double.IsNaN(grab.D) || grab.D == 0 && grab.C == default) return false;
                _vertexOffset = grab.C - new Point3D(o.Pose.X, o.Pose.Y, o.Pose.Z);
            }

            var moving = new HashSet<string>(new[] { o.Id }.Concat(World.MountedOn(o.Id).Select(x => x.Id)), StringComparer.OrdinalIgnoreCase);
            foreach (var other in _gizmoOthers) moving.Add(other.Id);
            Point3D? target = null;
            double best = VertexReach;
            foreach (var other in _items.Values)
            {
                if (moving.Contains(other.Object.Id) || EditorHidden(other.Object)) continue;
                foreach (var c in Corners(other))
                {
                    var (screen, _, visible) = Project(c);
                    if (!visible) continue;
                    double d = (screen - p).Length;
                    if (d < best)
                    {
                        best = d;
                        target = c;
                    }
                }
            }

            _hint.Text = Shell.Lang.T(target != null ? "Köşe yakalama: tutulan köşe öbür parçanın köşesine oturdu" : "Köşe yakalama (V): imleci başka bir parçanın köşesine götürün");
            if (target is not { } t) return false;
            var at = t - _vertexOffset.Value;
            nx = at.X;
            ny = at.Y;
            nz = at.Z;
            return true;
        }
    }
}
