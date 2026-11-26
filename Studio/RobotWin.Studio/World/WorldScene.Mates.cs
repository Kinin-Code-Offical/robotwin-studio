using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.World
{
    public sealed partial class WorldScene
    {
        /// <summary>[mate] A spot picked with the Hizala tool: the object, the world point, and the surface's normal towards the eye.</summary>
        public event Action<string, Point3D, Vector3D>? MateFacePicked;

        /// <summary>
        /// [mate] The object surface under a screen point: the world point where the camera's ray meets it, and the
        /// surface's normal from two more rays a few pixels off (so it needs no model transforms; a flat face gives its
        /// exact normal). Null when nothing, or only the floor, is there.
        /// </summary>
        internal (string Id, Point3D Point, Vector3D Normal)? SurfaceAt(Point p)
        {
            var h0 = RayHit(p);
            if (h0 == null) return null;
            var hx = RayHit(new Point(p.X + 2, p.Y));
            var hy = RayHit(new Point(p.X, p.Y + 2));
            var (_, dir) = Ray(p);
            Vector3D n;
            if (hx is { } a && hy is { } b && a.Id == h0.Value.Id && b.Id == h0.Value.Id)
            {
                n = Vector3D.CrossProduct(a.Point - h0.Value.Point, b.Point - h0.Value.Point);
                if (n.Length < 1e-18) n = -dir;
            }
            else n = -dir;   // at an edge: face the eye

            n.Normalize();
            if (Vector3D.DotProduct(n, dir) > 0) n = -n;
            return (h0.Value.Id, h0.Value.Point, n);
        }

        /// <summary>The first object the camera's ray through a screen point meets, and where (world).</summary>
        private (string Id, Point3D Point)? RayHit(Point p)
        {
            (string Id, Point3D Point)? found = null;
            var (origin, dir) = Ray(p);
            // WPF's ray starts on the near plane, and its hit distance is counted from there.
            var look = _camera.LookDirection;
            look.Normalize();
            double fromEye = _camera.NearPlaneDistance / Math.Max(1e-6, Vector3D.DotProduct(dir, look));
            VisualTreeHelper.HitTest(_view, PickFilter, r =>
            {
                if (r is not RayMeshGeometry3DHitTestResult hit) return HitTestResultBehavior.Continue;
                foreach (var it in _items.Values)
                {
                    if (it.Visual != hit.VisualHit) continue;
                    found = (it.Object.Id, origin + dir * (fromEye + hit.DistanceToRayOrigin));
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(p));
            return found;
        }

        /// <summary>[mate] A world point on screen (for the snapshot's check of the pick).</summary>
        internal Point? ScreenOf(Point3D p)
        {
            var (s, _, visible) = Project(p);
            return visible ? s : null;
        }
    }
}
