using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// [route] Where a wire can physically run between two plugs. The space around them is cut into cells; a cell is
    /// blocked when a solid part (or the floor) comes closer to its centre than the wire's radius plus half a cell, so
    /// even a 2 mm wall blocks a whole layer of cells. The shortest chain of free cells (A*, face neighbours) is then
    /// pulled taut: it runs straight wherever the straight line touches no solid. A wire between two parts inside a
    /// closed box therefore stays inside it, and one that has to leave it goes out through a real opening.
    /// </summary>
    public static class CableRouter
    {
        /// <summary>
        /// Path from <paramref name="from"/> to <paramref name="to"/> (world, both ends included), or null when no way
        /// exists within the searched region. <paramref name="passable"/> says where a body may be ignored (a plug's
        /// own part around its pin). <paramref name="keepOut"/> marks space no wire may enter although no solid is there
        /// (a propeller's swept disc: the spinning blades would cut it): it gets the point and the wire's clearance.
        /// </summary>
        public static List<Vec3>? Route(PhysicsScene scene, Vec3 from, Vec3 to, double radius, Func<SceneBody, Vec3, bool>? passable = null, int maxCells = 250_000,
            Func<Vec3, double, bool>? keepOut = null)
        {
            double span = (to - from).Length;
            foreach (double pad in new[] { Math.Max(0.03, 0.3 * span), Math.Max(0.15, span) })
            {
                // A coarse grid first; a finer one when an opening is too narrow for it (a 10 mm cable hole, the gap
                // between two servos) or its path, pulled taut, would cut through a thin part.
                double coarse = CellFor(from, to, pad, maxCells);
                foreach (double cell in new[] { coarse, coarse * 0.55 })
                {
                    if (cell < 0.0012) continue;
                    var path = Search(scene, from, to, radius, passable, pad, cell, maxCells, keepOut);
                    if (path == null) continue;
                    var pulled = Pull(scene, path, radius, passable, out bool clear, keepOut);
                    if (clear) return pulled;
                }
            }

            return null;
        }

        private static double CellFor(Vec3 from, Vec3 to, double pad, int maxCells)
        {
            var size = new Vec3(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y), Math.Abs(to.Z - from.Z)) + new Vec3(2 * pad, 2 * pad, 2 * pad);
            return Math.Max(0.002, Math.Min(0.02, Math.Cbrt(size.X * size.Y * size.Z / maxCells)));
        }

        /// <summary>Length of a polyline (m).</summary>
        public static double Length(IReadOnlyList<Vec3> path)
        {
            double len = 0;
            for (int i = 1; i < path.Count; i++) len += (path[i] - path[i - 1]).Length;
            return len;
        }

        private sealed class Grid
        {
            public Vec3 Origin;
            public double Cell, Inflate, Radius;
            public int Nx, Ny, Nz;
            public byte[] State = Array.Empty<byte>();   // 0 unknown, 1 free, 2 blocked
            public readonly List<SceneBody> Bodies = new List<SceneBody>();
            public PhysicsScene Scene = null!;
            public Func<SceneBody, Vec3, bool>? Passable;
            public Func<Vec3, double, bool>? KeepOut;

            public int Index(int x, int y, int z) => (z * Ny + y) * Nx + x;

            public Vec3 Centre(int x, int y, int z) => Origin + new Vec3((x + 0.5) * Cell, (y + 0.5) * Cell, (z + 0.5) * Cell);

            public bool Free(int x, int y, int z)
            {
                int i = Index(x, y, z);
                if (State[i] == 0) State[i] = Blocked(Centre(x, y, z), Inflate) ? (byte)2 : (byte)1;
                return State[i] == 1;
            }

            public bool Blocked(Vec3 c, double inflate)
            {
                if (Scene.GroundEnabled && c.Z < Scene.GroundHeight(c.X, c.Y) + inflate) return true;
                if (KeepOut != null && KeepOut(c, inflate)) return true;
                foreach (var b in Bodies)
                {
                    var shape = b.EffectiveShape!;
                    // [perf] BoundingRadius encloses every shape (a mesh's vertices included) about the body origin:
                    // a far body is rejected before its signed distance, which for a mesh is the costly part.
                    double reach = shape.BoundingRadius + inflate;
                    if ((c - b.Position).LengthSquared > reach * reach) continue;

                    var local = b.Orientation.InverseRotate(c - b.Position);
                    if (!b.SignedDistanceSolid(shape, local, inflate, out double d, out _) || d >= inflate) continue;
                    if (Passable != null && Passable(b, c)) continue;
                    return true;
                }

                return false;
            }
        }

        private static List<Vec3>? Search(PhysicsScene scene, Vec3 from, Vec3 to, double radius, Func<SceneBody, Vec3, bool>? passable, double pad, double cell, int maxCells,
            Func<Vec3, double, bool>? keepOut = null)
        {
            var lo = new Vec3(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Min(from.Z, to.Z)) - new Vec3(pad, pad, pad);
            var hi = new Vec3(Math.Max(from.X, to.X), Math.Max(from.Y, to.Y), Math.Max(from.Z, to.Z)) + new Vec3(pad, pad, pad);
            var size = hi - lo;
            if (size.X / cell * (size.Y / cell) * (size.Z / cell) > 12e6) return null;   // too fine for the memory it would take
            var g = new Grid
            {
                Origin = lo,
                Cell = cell,
                Radius = radius,
                // Blocked within a third of a cell of a solid: a 2 mm wall still blocks a whole layer of 4 mm cells,
                // while a 10 mm opening keeps a free lane.
                Inflate = radius + 0.3 * cell,
                Nx = Math.Max(1, (int)Math.Ceiling(size.X / cell)),
                Ny = Math.Max(1, (int)Math.Ceiling(size.Y / cell)),
                Nz = Math.Max(1, (int)Math.Ceiling(size.Z / cell)),
                Scene = scene,
                Passable = passable,
                KeepOut = keepOut
            };
            g.State = new byte[g.Nx * g.Ny * g.Nz];
            var centre = (lo + hi) * 0.5;
            double half = size.Length / 2;
            foreach (var b in scene.Bodies)
            {
                var shape = b.EffectiveShape;
                if (!b.Collides || shape == null || !shape.HasSolid) continue;
                if ((b.Position - centre).Length > half + shape.BoundingRadius + g.Inflate) continue;
                g.Bodies.Add(b);
            }

            (int, int, int) CellOf(Vec3 p) => (
                Math.Max(0, Math.Min(g.Nx - 1, (int)((p.X - lo.X) / cell))),
                Math.Max(0, Math.Min(g.Ny - 1, (int)((p.Y - lo.Y) / cell))),
                Math.Max(0, Math.Min(g.Nz - 1, (int)((p.Z - lo.Z) / cell))));
            if (Nearest(g, CellOf(from)) is not { } s || Nearest(g, CellOf(to)) is not { } e) return null;

            int start = g.Index(s.Item1, s.Item2, s.Item3), goal = g.Index(e.Item1, e.Item2, e.Item3);
            var cost = new Dictionary<int, int> { [start] = 0 };
            var parent = new Dictionary<int, int>();
            var open = new MinHeap();
            int H(int x, int y, int z) => Math.Abs(x - e.Item1) + Math.Abs(y - e.Item2) + Math.Abs(z - e.Item3);
            open.Enqueue(start, H(s.Item1, s.Item2, s.Item3));
            int nxy = g.Nx * g.Ny;
            int expanded = 0;
            bool found = false;
            while (open.TryDequeue(out int cur))
            {
                if (cur == goal)
                {
                    found = true;
                    break;
                }

                if (++expanded > maxCells) break;
                int cz = cur / nxy, cy = cur % nxy / g.Nx, cx = cur % g.Nx;
                int cc = cost[cur];
                for (int k = 0; k < 6; k++)
                {
                    int x = cx + (k == 0 ? 1 : k == 1 ? -1 : 0);
                    int y = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    int z = cz + (k == 4 ? 1 : k == 5 ? -1 : 0);
                    if (x < 0 || y < 0 || z < 0 || x >= g.Nx || y >= g.Ny || z >= g.Nz) continue;
                    int n = g.Index(x, y, z);
                    if (cost.TryGetValue(n, out int old) && old <= cc + 1) continue;
                    if (!g.Free(x, y, z)) continue;
                    cost[n] = cc + 1;
                    parent[n] = cur;
                    open.Enqueue(n, cc + 1 + H(x, y, z));
                }
            }

            if (!found) return null;
            var cells = new List<Vec3>();
            for (int c = goal; ; c = parent[c])
            {
                cells.Add(g.Centre(c % g.Nx, c % nxy / g.Nx, c / nxy));
                if (c == start) break;
            }

            cells.Reverse();
            var path = new List<Vec3>(cells.Count + 2) { from };
            path.AddRange(cells);
            path.Add(to);
            return path;
        }

        /// <summary>The free cell nearest to a (possibly blocked) cell, searched up to three cells away.</summary>
        private static (int, int, int)? Nearest(Grid g, (int X, int Y, int Z) c)
        {
            (int, int, int)? best = null;
            int bestD = int.MaxValue;
            for (int r = 0; r <= 3 && best == null; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) != r) continue;
                            int x = c.X + dx, y = c.Y + dy, z = c.Z + dz;
                            if (x < 0 || y < 0 || z < 0 || x >= g.Nx || y >= g.Ny || z >= g.Nz || !g.Free(x, y, z)) continue;
                            int d = dx * dx + dy * dy + dz * dz;
                            if (d < bestD)
                            {
                                bestD = d;
                                best = (x, y, z);
                            }
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Pulls a cell path taut: from each corner, the furthest later point the straight line reaches without passing
        /// through a solid or coming closer to one than the wire's radius.
        /// </summary>
        private static List<Vec3> Pull(PhysicsScene scene, List<Vec3> path, double radius, Func<SceneBody, Vec3, bool>? passable, out bool clear,
            Func<Vec3, double, bool>? keepOut = null)
        {
            // [perf] only the bodies that can come near the path's box
            var lo = path[0];
            var hi = path[0];
            foreach (var v in path)
            {
                lo = new Vec3(Math.Min(lo.X, v.X), Math.Min(lo.Y, v.Y), Math.Min(lo.Z, v.Z));
                hi = new Vec3(Math.Max(hi.X, v.X), Math.Max(hi.Y, v.Y), Math.Max(hi.Z, v.Z));
            }

            var bodies = new List<SceneBody>();
            foreach (var b in scene.Bodies)
            {
                if (!b.Collides || b.EffectiveShape is not { HasSolid: true } bs) continue;
                var c = b.Position;
                double dx = Math.Max(0, Math.Max(lo.X - c.X, c.X - hi.X));
                double dy = Math.Max(0, Math.Max(lo.Y - c.Y, c.Y - hi.Y));
                double dz = Math.Max(0, Math.Max(lo.Z - c.Z, c.Z - hi.Z));
                double reach = bs.BoundingRadius + radius + 1e-3;
                if (dx * dx + dy * dy + dz * dz <= reach * reach) bodies.Add(b);
            }
            var probe = new Grid { Scene = scene, Passable = passable, KeepOut = keepOut };
            probe.Bodies.AddRange(bodies);

            bool Clear(Vec3 p, Vec3 q)
            {
                var d = q - p;
                double len = d.Length;
                if (len < 1e-9) return true;
                var dir = d / len;
                foreach (var b in bodies)
                {
                    var shape = b.EffectiveShape!;
                    double reach = shape.BoundingRadius + radius;
                    var lp0 = p - b.Position;
                    double f = Math.Max(0, Math.Min(len, -lp0.Dot(dir)));
                    if ((lp0 + dir * f).LengthSquared > reach * reach) continue;

                    if (passable != null && (passable(b, p) || passable(b, q))) continue;
                    var lp = b.Orientation.InverseRotate(p - b.Position);
                    var ld = b.Orientation.InverseRotate(dir);
                    if (b.RaycastSolid(shape, lp, ld, len, out _, out _)) return false;
                }

                // Keep the wire's own thickness off the surfaces it passes (not right at its ends, where it leaves a pin
                // among neighbouring parts).
                int n = Math.Max(1, (int)Math.Ceiling(len / 0.002));
                for (int k = 1; k < n; k++)
                {
                    var x = p + d * (k / (double)n);
                    if ((x - path[0]).LengthSquared < 1.6e-5 || (x - path[path.Count - 1]).LengthSquared < 1.6e-5) continue;
                    if (probe.Blocked(x, radius)) return false;
                }

                return true;
            }

            var result = new List<Vec3> { path[0] };
            int i = 0;
            clear = true;
            while (i < path.Count - 1)
            {
                int j = i + 1;
                while (j + 1 < path.Count && Clear(path[i], path[j + 1])) j++;
                // Neighbouring cells the exact test still finds blocked: the grid let the path through a thin part.
                if (!Clear(path[i], path[j])) clear = false;
                result.Add(path[j]);
                i = j;
            }

            return result;
        }
            /// <summary>Binary min-heap of (item, priority) for the search (netstandard2.1 has no PriorityQueue).</summary>
        private sealed class MinHeap
        {
            private readonly List<(int Item, int Priority)> _h = new List<(int, int)>();

            public void Enqueue(int item, int priority)
            {
                _h.Add((item, priority));
                int i = _h.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_h[p].Priority <= _h[i].Priority) break;
                    (_h[p], _h[i]) = (_h[i], _h[p]);
                    i = p;
                }
            }

            public bool TryDequeue(out int item)
            {
                if (_h.Count == 0)
                {
                    item = 0;
                    return false;
                }

                item = _h[0].Item;
                var last = _h[_h.Count - 1];
                _h.RemoveAt(_h.Count - 1);
                if (_h.Count == 0) return true;
                _h[0] = last;
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _h.Count && _h[l].Priority < _h[m].Priority) m = l;
                    if (r < _h.Count && _h[r].Priority < _h[m].Priority) m = r;
                    if (m == i) break;
                    (_h[m], _h[i]) = (_h[i], _h[m]);
                    i = m;
                }

                return true;
            }
        }
    }
}
