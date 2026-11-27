using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [surface-snap] Unity's surface snapping (Ctrl+Shift while moving): the part's base (its pivot) goes onto the
    /// surface under the cursor, any part's or the floor's, skipping the part itself and what is mounted on it. Works
    /// on a free drag and on the move handle's middle, in Montaj and Dünya alike.
    /// </summary>
    public sealed partial class WorldScene
    {
        private static bool SurfaceSnapHeld =>
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        /// <summary>Where the part's base lands for the cursor at <paramref name="p"/>, or null over nothing.</summary>
        private Point3D? SurfaceUnder(Point p, WorldObject moving)
        {
            if (World == null) return null;
            var skip = new HashSet<string>(new[] { moving.Id }.Concat(World.MountedOn(moving.Id).Select(x => x.Id)), StringComparer.OrdinalIgnoreCase);
            foreach (var other in _gizmoOthers) skip.Add(other.Id);   // [multiselect] the rest of a moving selection
            if (RayHitExcept(p, skip) is { } hit) return hit;
            // nothing in the way: the floor (or the terrain) under the cursor
            if (PickGround(p) is { } g) return new Point3D(g.X, g.Y, GroundZAt(g.X, g.Y));
            return OnPlane(p, 0) is { } q ? new Point3D(q.X, q.Y, 0) : (Point3D?)null;
        }

        /// <summary>
        /// The snapshot's --surface-snap: the selected part dropped, as a Ctrl+Shift drag would, onto the middle of another
        /// part's top as seen on the screen. True when it landed.
        /// </summary>
        public bool SnapSelectedOnto(string targetId)
        {
            if (World?.Find(Selected) is not { } o || !_items.TryGetValue(targetId, out var target) || target.Visual.Content == null) return false;
            var b = target.Visual.Content.Bounds;
            var top = target.Visual.Transform.Transform(new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ));
            var (screen, _, visible) = Project(top);
            if (!visible || SurfaceUnder(screen, o) is not { } onto) return false;
            BeginEdit?.Invoke();
            World.MoveAssembly(o, onto.X - o.Pose.X, onto.Y - o.Pose.Y, onto.Z - o.Pose.Z);
            EditCommitted?.Invoke($"{o.Name} yüzeye oturtuldu");
            return true;
        }

        /// <summary>
        /// [look-at] Unity's rotate tool with Ctrl+Shift: a click on any surface turns the selected part (and what is
        /// mounted on it) so its own +X, the way a camera or a distance sensor looks, points at that spot, kept level
        /// (CoreSim Sandbox/WorldConstraints.Aim). True when it turned.
        /// </summary>
        public bool TurnSelectedToward(Point p)
        {
            if (World?.Find(Selected) is not { } o || SurfaceUnder(p, o) is not { } at) return false;
            var toward = new RobotTwin.CoreSim.Mechanics.Vec3(at.X, at.Y, at.Z) - o.Pose.Position;
            if (toward.Length < 1e-6) return false;
            var q = WorldConstraints.Aim(o.Pose.Orientation, new RobotTwin.CoreSim.Mechanics.Vec3(1, 0, 0), toward.Normalized());
            BeginEdit?.Invoke();
            World.RotateAssembly(o, (q * o.Pose.Orientation.Conjugate).Normalized(), o.Pose.Position);
            EditCommitted?.Invoke($"{o.Name} yüzeydeki noktaya çevrildi");
            return true;
        }

        /// <summary>[look-at] The snapshot's look-at: the selection turned, as a Ctrl+Shift click would, to the middle of a part's top.</summary>
        public bool TurnSelectedTowardPart(string targetId)
        {
            if (!_items.TryGetValue(targetId, out var target) || target.Visual.Content == null) return false;
            var b = target.Visual.Content.Bounds;
            var top = target.Visual.Transform.Transform(new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ));
            var (screen, _, visible) = Project(top);
            return visible && TurnSelectedToward(screen);
        }

        /// <summary>The first object the camera's ray meets, leaving out some (the dragged part and its riders).</summary>
        private Point3D? RayHitExcept(Point p, ISet<string> skip)
        {
            Point3D? found = null;
            var (origin, dir) = Ray(p);
            var look = _camera.LookDirection;
            look.Normalize();
            double fromEye = _camera.NearPlaneDistance / Math.Max(1e-6, Vector3D.DotProduct(dir, look));
            VisualTreeHelper.HitTest(_view, PickFilter, r =>
            {
                if (r is not RayMeshGeometry3DHitTestResult hit) return HitTestResultBehavior.Continue;
                foreach (var it in _items.Values)
                {
                    if (it.Visual != hit.VisualHit) continue;
                    if (skip.Contains(it.Object.Id)) return HitTestResultBehavior.Continue;
                    found = origin + dir * (fromEye + hit.DistanceToRayOrigin);
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(p));
            return found;
        }
    }
}
