using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [rect] Unity's Rect Tool (T) on a 3D part: a rectangle on the part's own face most turned to the camera (its two
    /// axes there), a dot on each corner and the pivot ring in the middle. Dragging an edge or a corner resizes the part
    /// along those axes while the opposite edge stays where it is (Shift on a corner keeps its proportions); dragging
    /// inside moves it in that plane; dragging just outside a corner turns it about the face's normal, about the middle.
    /// The grid, the size step and the turn step apply unless Alt is held.
    /// </summary>
    public sealed partial class WorldScene
    {
        private int _rectHitSu, _rectHitSv;   // the corner or edge under the cursor: −1, 0, +1 along each of the rect's axes
        private int _rectSu, _rectSv, _rectKu, _rectKv;
        private Point3D _rectC0;
        private Vector3D _rectU0, _rectV0, _rectN0;
        private Vec3 _rectMin0, _rectMax0;

        private bool ShowsRect => Gizmo == GizmoMode.Rect;

        /// <summary>The rect on a part: its middle, its axes (the part's own, unit) and half sizes, the axes' indices.</summary>
        private readonly struct RectFace
        {
            public RectFace(Point3D c, Vector3D u, Vector3D v, Vector3D n, double hu, double hv, int ku, int kv, int kn)
            {
                C = c;
                U = u;
                V = v;
                N = n;
                Hu = hu;
                Hv = hv;
                Ku = ku;
                Kv = kv;
                Kn = kn;
            }

            public Point3D C { get; }
            public Vector3D U { get; }
            public Vector3D V { get; }
            public Vector3D N { get; }
            public double Hu { get; }
            public double Hv { get; }
            public int Ku { get; }
            public int Kv { get; }
            public int Kn { get; }

            public Point3D Corner(int su, int sv) => C + U * (Hu * su) + V * (Hv * sv);
        }

        private static double AxisOf(Vec3 v, int k) => k == 0 ? v.X : k == 1 ? v.Y : v.Z;

        private static Vec3 WithAxis(Vec3 v, int k, double value) =>
            new Vec3(k == 0 ? value : v.X, k == 1 ? value : v.Y, k == 2 ? value : v.Z);

        private static Vec3 UnitAxis(int k) => k == 0 ? Vec3.UnitX : k == 1 ? Vec3.UnitY : Vec3.UnitZ;

        /// <summary>The part's face most turned to the camera: the rect lies on it, across the part's shape box.</summary>
        private RectFace RectOf(WorldObject o)
        {
            var g = GeometryOf(o);
            var q = o.Pose.Orientation;
            var view = ViewDir();
            int kn = 0;
            double best = -1;
            for (int k = 0; k < 3; k++)
            {
                double a = Math.Abs(Vector3D.DotProduct(V(q.Rotate(UnitAxis(k))), view));
                if (a <= best) continue;
                best = a;
                kn = k;
            }

            int ku = kn == 0 ? 1 : 0, kv = kn == 2 ? 1 : 2;
            var size = g.Max - g.Min;
            var c = o.Pose.ToWorld((g.Min + g.Max) * 0.5);
            return new RectFace(new Point3D(c.X, c.Y, c.Z), V(q.Rotate(UnitAxis(ku))), V(q.Rotate(UnitAxis(kv))), V(q.Rotate(UnitAxis(kn))),
                AxisOf(size, ku) / 2, AxisOf(size, kv) / 2, ku, kv, kn);
        }

        private static readonly int[] RectSu = { -1, 1, 1, -1 };
        private static readonly int[] RectSv = { -1, -1, 1, 1 };

        private void DrawRect(DrawingContext dc, WorldObject o)
        {
            var f = RectOf(o);
            bool dragging = _gizmoDrag != GizmoPart.None;
            int su = dragging ? _rectSu : _rectHitSu, sv = dragging ? _rectSv : _rectHitSv;
            DrawPolygon(dc, new[] { f.Corner(-1, -1), f.Corner(1, -1), f.Corner(1, 1), f.Corner(-1, 1) }, null, Line(ViewRing, 1.2));
            if (_gizmoDrag == GizmoPart.RectEdge || (!dragging && _gizmoHot == GizmoPart.RectEdge))
            {
                // the edge under the cursor or dragged
                var (a, b) = su != 0 ? (f.Corner(su, -1), f.Corner(su, 1)) : (f.Corner(-1, sv), f.Corner(1, sv));
                DrawPolyline(dc, new[] { a, b }, Line(Active, 2.5));
            }

            bool cornerOn = _gizmoDrag == GizmoPart.RectCorner || (!dragging && _gizmoHot == GizmoPart.RectCorner);
            for (int i = 0; i < 4; i++)
            {
                var s = Project(f.Corner(RectSu[i], RectSv[i]));
                if (!s.Visible) continue;
                bool on = cornerOn && RectSu[i] == su && RectSv[i] == sv;
                dc.DrawEllipse(Fill(on ? Active : AxisZ), Line(ViewRing, 1), s.Screen, 4.5, 4.5);
            }

            // the pivot: a ring in the middle (yellow while moving or turning the part)
            var pc = Project(f.C);
            bool moving = _gizmoHot is GizmoPart.PlaneXY or GizmoPart.PlaneXZ or GizmoPart.PlaneYZ or GizmoPart.RotX or GizmoPart.RotY or GizmoPart.RotZ ||
                          _gizmoDrag is GizmoPart.PlaneXY or GizmoPart.PlaneXZ or GizmoPart.PlaneYZ or GizmoPart.RotX or GizmoPart.RotY or GizmoPart.RotZ;
            if (pc.Visible) dc.DrawEllipse(null, Line(moving ? Active : ViewRing, 1.5), pc.Screen, 6, 6);
        }

        /// <summary>A corner, an edge, the inside (move) or just outside a corner (turn); the corner's or edge's sides kept for the drag.</summary>
        private GizmoPart RectHit(WorldObject o, Point p)
        {
            var f = RectOf(o);
            var s = new Point[4];
            for (int i = 0; i < 4; i++)
            {
                var pr = Project(f.Corner(RectSu[i], RectSv[i]));
                if (!pr.Visible) return GizmoPart.None;
                s[i] = pr.Screen;
            }

            for (int i = 0; i < 4; i++)
            {
                if ((p - s[i]).Length >= 8) continue;
                (_rectHitSu, _rectHitSv) = (RectSu[i], RectSv[i]);
                return GizmoPart.RectCorner;
            }

            // edges: 0–1 the −V side, 1–2 the +U side, 2–3 the +V side, 3–0 the −U side
            int edge = -1;
            double bestD = 6;
            for (int e = 0; e < 4; e++)
            {
                double d = SegmentDistance(p, s[e], s[(e + 1) % 4]);
                if (d >= bestD) continue;
                bestD = d;
                edge = e;
            }

            if (edge >= 0)
            {
                (_rectHitSu, _rectHitSv) = edge switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };
                return GizmoPart.RectEdge;
            }

            (_rectHitSu, _rectHitSv) = (0, 0);
            if (InQuad(p, s[0], s[1], s[2], s[3])) return f.Kn == 0 ? GizmoPart.PlaneYZ : f.Kn == 1 ? GizmoPart.PlaneXZ : GizmoPart.PlaneXY;
            for (int i = 0; i < 4; i++)
                if ((p - s[i]).Length < 24)
                    return f.Kn == 0 ? GizmoPart.RotX : f.Kn == 1 ? GizmoPart.RotY : GizmoPart.RotZ;
            return GizmoPart.None;
        }

        /// <summary>The drag's start: the rect's face, the side taken, the part's shape box; moves and turns use its plane and middle.</summary>
        private void RectBegin(WorldObject o, Point p)
        {
            var f = RectOf(o);
            (_rectC0, _rectU0, _rectV0, _rectN0, _rectKu, _rectKv) = (f.C, f.U, f.V, f.N, f.Ku, f.Kv);
            (_rectSu, _rectSv) = (_rectHitSu, _rectHitSv);
            var g = GeometryOf(o);
            (_rectMin0, _rectMax0) = (g.Min, g.Max);
            _gizmoAxis = f.N;
            _gizmoPivot = f.C;
            _gizmoHit0 = PlaneHit(p, f.C, f.N);
            _gizmoFrom = (_gizmoHit0 ?? f.C + f.U) - f.C;
        }

        /// <summary>An edge or a corner dragged: the part's size along the rect's axes, the opposite side kept in place.</summary>
        private bool RectResize(WorldObject o, Point p, bool free)
        {
            if (_gizmoPose == null || _gizmoHit0 is not { } h0 || PlaneHit(p, _rectC0, _rectN0) is not { } h) return true;
            var d = h - h0;
            double s0u = AxisOf(_gizmoSize, _rectKu), s0v = AxisOf(_gizmoSize, _rectKv);
            double nu = _rectSu != 0 ? SnapSize(s0u + Vector3D.DotProduct(d, _rectU0) * _rectSu, free) : s0u;
            double nv = _rectSv != 0 ? SnapSize(s0v + Vector3D.DotProduct(d, _rectV0) * _rectSv, free) : s0v;
            if (_gizmoDrag == GizmoPart.RectCorner && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && s0u > 0 && s0v > 0)
            {
                double k = Math.Max(nu / s0u, nv / s0v);   // Shift: the corner keeps the proportions
                nu = s0u * k;
                nv = s0v * k;
            }

            nu = Math.Max(0.001, nu);
            nv = Math.Max(0.001, nv);
            // The shape box scales about the part's origin: shift the part so the side across from the one dragged stays put.
            double Shift(int sign, int k, double s0, double s1) =>
                s0 <= 0 || sign == 0 ? 0 : (sign > 0 ? AxisOf(_rectMin0, k) : AxisOf(_rectMax0, k)) * (1 - s1 / s0);
            var local = UnitAxis(_rectKu) * Shift(_rectSu, _rectKu, s0u, nu) + UnitAxis(_rectKv) * Shift(_rectSv, _rectKv, s0v, nv);
            var shift = _gizmoPose.Orientation.Rotate(local);
            var size = WithAxis(WithAxis(_gizmoSize, _rectKu, nu), _rectKv, nv);
            if (WorldBuilding.IsProp(o)) WorldBuilding.ResizeProp(o, size);
            else
            {
                o.SizeX = size.X;
                o.SizeY = size.Y;
                o.SizeZ = size.Z;
            }

            o.Pose.X = _gizmoPose.X + shift.X;
            o.Pose.Y = _gizmoPose.Y + shift.Y;
            o.Pose.Z = _gizmoPose.Z + shift.Z;
            Readout($"{o.Name}: {nu * 1000:0.#} × {nv * 1000:0.#} mm{(free ? string.Empty : " (boyut adımı; Alt: serbest, Shift köşede: oranı koru)")}");
            Rebuild();
            return true;
        }
    }
}
