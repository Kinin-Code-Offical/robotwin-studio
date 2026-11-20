using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RobotTwin.CoreSim.Mechanics
{
    // [terrain] Editable heightfield with painted surface layers (the world's "Arazi").

    /// <summary>Vertex rectangle (inclusive) that changed, and what changed.</summary>
    public readonly struct TerrainChange
    {
        public TerrainChange(int c0, int r0, int c1, int r1, bool heights, bool paint)
        {
            C0 = c0;
            R0 = r0;
            C1 = c1;
            R1 = r1;
            Heights = heights;
            Paint = paint;
        }

        public int C0 { get; }
        public int R0 { get; }
        public int C1 { get; }
        public int R1 { get; }
        public bool Heights { get; }
        public bool Paint { get; }
        public bool IsEmpty => C1 < C0 || R1 < R0;
    }

    /// <summary>
    /// A regular grid of heights (metres, z up) over Columns × Rows vertices spaced
    /// <see cref="CellSize"/>, starting at (<see cref="OriginX"/>, <see cref="OriginY"/>), with
    /// per-vertex weights (0…255, summing to 255) of the <see cref="TerrainLayers"/>. Heights are
    /// interpolated bilinearly (continuous surface, continuous normals inside cells). Outside the
    /// grid the edge heights continue.
    /// </summary>
    public sealed class TerrainMap
    {
        /// <summary>Largest number of height samples (1025 × 1025).</summary>
        public const int MaxSamples = 1025 * 1025;

        public const double MinCell = 0.005;

        private float[] _h;
        private readonly byte[]?[] _w = new byte[]?[TerrainLayers.Count];
        private int[] _active = Array.Empty<int>();
        private double _min, _max;
        private bool _rangeDirty = true;

        /// <summary>Flat terrain of the given size (m) centred on (cx, cy), covered with one layer.</summary>
        public TerrainMap(double width, double depth, double cellSize, double centerX = 0, double centerY = 0, string baseLayer = "grass")
        {
            cellSize = Math.Max(MinCell, cellSize);
            int cols = Math.Max(2, (int)Math.Round(Math.Max(cellSize, width) / cellSize) + 1);
            int rows = Math.Max(2, (int)Math.Round(Math.Max(cellSize, depth) / cellSize) + 1);
            while ((long)cols * rows > MaxSamples)
            {
                cellSize *= 1.25;
                cols = Math.Max(2, (int)Math.Round(width / cellSize) + 1);
                rows = Math.Max(2, (int)Math.Round(depth / cellSize) + 1);
            }

            Columns = cols;
            Rows = rows;
            CellSize = cellSize;
            OriginX = centerX - (cols - 1) * cellSize / 2;
            OriginY = centerY - (rows - 1) * cellSize / 2;
            _h = new float[cols * rows];
            FillLayer(TerrainLayers.IndexOf(baseLayer), notify: false);
        }

        private TerrainMap(int cols, int rows, double cell, double ox, double oy)
        {
            Columns = cols;
            Rows = rows;
            CellSize = cell;
            OriginX = ox;
            OriginY = oy;
            _h = new float[cols * rows];
        }

        public int Columns { get; }
        public int Rows { get; }
        public double CellSize { get; }
        public double OriginX { get; }
        public double OriginY { get; }
        public int VertexCount => Columns * Rows;
        public double Width => (Columns - 1) * CellSize;
        public double Depth => (Rows - 1) * CellSize;
        public double CenterX => OriginX + Width / 2;
        public double CenterY => OriginY + Depth / 2;

        /// <summary>Raw heights, row-major (index = row·Columns + column). Call <see cref="NotifyChanged"/> after writing.</summary>
        public float[] Heights => _h;

        /// <summary>Incremented on every change (render caches compare it).</summary>
        public long Revision { get; private set; }

        /// <summary>Raised after heights or paint changed in a vertex rectangle.</summary>
        public event Action<TerrainChange>? Changed;

        /// <summary>Undo history of strokes (not saved, not cloned).</summary>
        public TerrainHistory History { get; } = new TerrainHistory();

        /// <summary>Layers with a weight array (indices into <see cref="TerrainLayers.All"/>).</summary>
        public IReadOnlyList<int> ActiveLayers => _active;

        public int Index(int c, int r) => r * Columns + c;

        public bool Contains(double x, double y) => x >= OriginX && y >= OriginY && x <= OriginX + Width && y <= OriginY + Depth;

        public double VertexX(int c) => OriginX + c * CellSize;
        public double VertexY(int r) => OriginY + r * CellSize;

        public double VertexHeight(int c, int r)
        {
            c = c < 0 ? 0 : c >= Columns ? Columns - 1 : c;
            r = r < 0 ? 0 : r >= Rows ? Rows - 1 : r;
            return _h[r * Columns + c];
        }

        /// <summary>Vertex range covering a world rectangle (clamped; may be empty).</summary>
        public (int C0, int R0, int C1, int R1) VertexRange(double x0, double y0, double x1, double y1)
        {
            int c0 = Math.Max(0, (int)Math.Floor((x0 - OriginX) / CellSize));
            int r0 = Math.Max(0, (int)Math.Floor((y0 - OriginY) / CellSize));
            int c1 = Math.Min(Columns - 1, (int)Math.Ceiling((x1 - OriginX) / CellSize));
            int r1 = Math.Min(Rows - 1, (int)Math.Ceiling((y1 - OriginY) / CellSize));
            return (c0, r0, c1, r1);
        }

        // ================================================================== sampling

        private void Cell(double x, double y, out int c, out int r, out double fx, out double fy)
        {
            double u = (x - OriginX) / CellSize, v = (y - OriginY) / CellSize;
            if (u < 0) u = 0;
            else if (u > Columns - 1) u = Columns - 1;
            if (v < 0) v = 0;
            else if (v > Rows - 1) v = Rows - 1;
            c = (int)u;
            r = (int)v;
            if (c > Columns - 2) c = Columns - 2;
            if (r > Rows - 2) r = Rows - 2;
            fx = u - c;
            fy = v - r;
        }

        /// <summary>Bilinearly interpolated height (m).</summary>
        public double Height(double x, double y)
        {
            Cell(x, y, out int c, out int r, out double fx, out double fy);
            int i = r * Columns + c;
            double h00 = _h[i], h10 = _h[i + 1], h01 = _h[i + Columns], h11 = _h[i + Columns + 1];
            return h00 + (h10 - h00) * fx + (h01 - h00) * fy + (h00 - h10 - h01 + h11) * fx * fy;
        }

        /// <summary>(∂h/∂x, ∂h/∂y) of the bilinear surface (0 across the outside of the grid).</summary>
        public (double Dx, double Dy) Gradient(double x, double y)
        {
            double u = (x - OriginX) / CellSize, v = (y - OriginY) / CellSize;
            bool outX = u < 0 || u > Columns - 1, outY = v < 0 || v > Rows - 1;
            Cell(x, y, out int c, out int r, out double fx, out double fy);
            int i = r * Columns + c;
            double h00 = _h[i], h10 = _h[i + 1], h01 = _h[i + Columns], h11 = _h[i + Columns + 1];
            double dx = ((h10 - h00) * (1 - fy) + (h11 - h01) * fy) / CellSize;
            double dy = ((h01 - h00) * (1 - fx) + (h11 - h10) * fx) / CellSize;
            return (outX ? 0 : dx, outY ? 0 : dy);
        }

        public Vec3 Normal(double x, double y)
        {
            var (dx, dy) = Gradient(x, y);
            return new Vec3(-dx, -dy, 1).Normalized();
        }

        /// <summary>Slope angle (degrees) at a point.</summary>
        public double SlopeDegrees(double x, double y)
        {
            var (dx, dy) = Gradient(x, y);
            return Math.Atan(Math.Sqrt(dx * dx + dy * dy)) * 180 / Math.PI;
        }

        /// <summary>Smooth vertex normal (central differences) for rendering.</summary>
        public Vec3 VertexNormal(int c, int r)
        {
            double hl = VertexHeight(c - 1, r), hr = VertexHeight(c + 1, r), hd = VertexHeight(c, r - 1), hu = VertexHeight(c, r + 1);
            double sx = (c > 0 && c < Columns - 1 ? 2 : 1) * CellSize, sy = (r > 0 && r < Rows - 1 ? 2 : 1) * CellSize;
            return new Vec3(-(hr - hl) / sx, -(hu - hd) / sy, 1).Normalized();
        }

        /// <summary>Lowest and highest vertex (cached until the next change).</summary>
        public (double Min, double Max) HeightRange
        {
            get
            {
                if (_rangeDirty)
                {
                    float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                    var h = _h;
                    for (int i = 0; i < h.Length; i++)
                    {
                        float v = h[i];
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }

                    _min = lo;
                    _max = hi;
                    _rangeDirty = false;
                }

                return (_min, _max);
            }
        }

        /// <summary>
        /// Nearest intersection of a ray with the surface (marching half cells, then bisection).
        /// <paramref name="insideOnly"/> limits hits to the terrain's own rectangle (picking).
        /// </summary>
        public bool Raycast(Vec3 origin, Vec3 dir, double maxDistance, out double distance, bool insideOnly = false)
        {
            distance = -1;
            var (lo, hi) = HeightRange;
            double f0 = origin.Z - Height(origin.X, origin.Y);
            if (f0 <= 0)
            {
                if (insideOnly && !Contains(origin.X, origin.Y)) return false;
                distance = 0;
                return true;
            }

            double t0 = 0, t1 = maxDistance;
            if (Math.Abs(dir.Z) > 1e-12)
            {
                double tLo = (lo - 1e-6 - origin.Z) / dir.Z, tHi = (hi + 1e-6 - origin.Z) / dir.Z;
                if (dir.Z < 0)
                {
                    t0 = Math.Max(t0, tHi);
                    t1 = Math.Min(t1, tLo);
                }
                else
                {
                    if (origin.Z > hi) return false;
                    t1 = Math.Min(t1, tHi);
                }
            }
            else if (origin.Z > hi) return false;

            if (insideOnly)
            {
                // Clip to the terrain rectangle.
                if (!ClipAxis(origin.X, dir.X, OriginX, OriginX + Width, ref t0, ref t1)) return false;
                if (!ClipAxis(origin.Y, dir.Y, OriginY, OriginY + Depth, ref t0, ref t1)) return false;
            }

            if (t0 > t1) return false;
            double step = Math.Max(1e-4, 0.5 * CellSize);
            double t = t0;
            var p0 = origin + dir * t;
            double prev = p0.Z - Height(p0.X, p0.Y);
            if (prev <= 0)
            {
                distance = t;
                return true;
            }

            while (t < t1)
            {
                double tn = Math.Min(t1, t + step);
                var p = origin + dir * tn;
                double f = p.Z - Height(p.X, p.Y);
                if (f <= 0)
                {
                    double a = t, b = tn, fa = prev, fb = f;
                    for (int i = 0; i < 18; i++)
                    {
                        double m = 0.5 * (a + b);
                        var q = origin + dir * m;
                        double fm = q.Z - Height(q.X, q.Y);
                        if (fm > 0)
                        {
                            a = m;
                            fa = fm;
                        }
                        else
                        {
                            b = m;
                            fb = fm;
                        }
                    }

                    distance = fa - fb > 1e-15 ? a + (b - a) * fa / (fa - fb) : 0.5 * (a + b);
                    return true;
                }

                prev = f;
                t = tn;
            }

            return false;
        }

        private static bool ClipAxis(double o, double d, double min, double max, ref double t0, ref double t1)
        {
            if (Math.Abs(d) < 1e-15) return o >= min && o <= max;
            double a = (min - o) / d, b = (max - o) / d;
            if (a > b) (a, b) = (b, a);
            t0 = Math.Max(t0, a);
            t1 = Math.Min(t1, b);
            return t0 <= t1;
        }

        // ================================================================== layers

        /// <summary>Weight array of a layer (null when the layer is nowhere on the terrain).</summary>
        public byte[]? LayerWeights(int layer) => layer >= 0 && layer < _w.Length ? _w[layer] : null;

        /// <summary>Weight array of a layer, created on demand.</summary>
        public byte[] EnsureLayer(int layer)
        {
            var w = _w[layer];
            if (w != null) return w;
            w = new byte[_h.Length];
            _w[layer] = w;
            RebuildActive();
            return w;
        }

        private void RebuildActive()
        {
            var list = new List<int>();
            for (int i = 0; i < _w.Length; i++)
                if (_w[i] != null)
                    list.Add(i);
            _active = list.ToArray();
        }

        /// <summary>Drops layers that have no weight anywhere.</summary>
        public void CompactLayers()
        {
            bool changed = false;
            for (int l = 0; l < _w.Length; l++)
            {
                var w = _w[l];
                if (w == null) continue;
                bool any = false;
                for (int i = 0; i < w.Length && !any; i++) any = w[i] != 0;
                if (!any)
                {
                    _w[l] = null;
                    changed = true;
                }
            }

            if (changed) RebuildActive();
        }

        /// <summary>Covers the whole terrain with one layer.</summary>
        public void FillLayer(int layer, bool notify = true)
        {
            for (int i = 0; i < _w.Length; i++) _w[i] = null;
            var w = new byte[_h.Length];
            for (int i = 0; i < w.Length; i++) w[i] = 255;
            _w[Math.Max(0, Math.Min(_w.Length - 1, layer))] = w;
            RebuildActive();
            if (notify) NotifyChanged(0, 0, Columns - 1, Rows - 1, false, true);
        }

        /// <summary>
        /// Interpolated layer weights at a point (sum 1) into <paramref name="weights"/> (length ≥
        /// <see cref="TerrainLayers.Count"/>); returns the dominant layer.
        /// </summary>
        public int SampleLayers(double x, double y, double[] weights)
        {
            Array.Clear(weights, 0, weights.Length);
            Cell(x, y, out int c, out int r, out double fx, out double fy);
            int i = r * Columns + c;
            double w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            double sum = 0, best = -1;
            int dominant = _active.Length > 0 ? _active[0] : 0;
            foreach (int l in _active)
            {
                var w = _w[l]!;
                double v = w[i] * w00 + w[i + 1] * w10 + w[i + Columns] * w01 + w[i + Columns + 1] * w11;
                weights[l] = v;
                sum += v;
                if (v > best)
                {
                    best = v;
                    dominant = l;
                }
            }

            if (sum <= 1e-9)
            {
                weights[dominant] = 1;
                return dominant;
            }

            foreach (int l in _active) weights[l] /= sum;
            return dominant;
        }

        /// <summary>Layer with the largest weight at a point.</summary>
        public TerrainLayer LayerAt(double x, double y)
        {
            Cell(x, y, out int c, out int r, out double fx, out double fy);
            int i = r * Columns + c;
            double w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            double best = -1;
            int dominant = _active.Length > 0 ? _active[0] : 0;
            foreach (int l in _active)
            {
                var w = _w[l]!;
                double v = w[i] * w00 + w[i + 1] * w10 + w[i + Columns] * w01 + w[i + Columns + 1] * w11;
                if (v > best)
                {
                    best = v;
                    dominant = l;
                }
            }

            return TerrainLayers.Get(dominant);
        }

        /// <summary>
        /// Blends <paramref name="amount"/> (0…1) of a layer into a vertex: the other layers shrink
        /// proportionally so the weights keep summing to 255.
        /// </summary>
        public void PaintVertex(int vertex, int layer, double amount)
        {
            if (amount <= 0) return;
            var target = EnsureLayer(layer);
            int old = target[vertex];
            if (old >= 255) return;
            int next = (int)Math.Round(old + amount * (255 - old));
            if (next <= old) next = old + 1;
            if (next > 255) next = 255;
            int restOld = 255 - old, restNew = 255 - next;
            target[vertex] = (byte)next;
            int assigned = next;
            int last = -1;
            foreach (int l in _active)
            {
                if (l == layer) continue;
                var w = _w[l]!;
                if (w[vertex] == 0) continue;
                int v = restOld > 0 ? (int)Math.Floor(w[vertex] * (double)restNew / restOld) : 0;
                w[vertex] = (byte)v;
                assigned += v;
                if (v > 0) last = l;
            }

            // Rounding remainder goes to the largest other layer (or the painted one).
            int rem = 255 - assigned;
            if (rem != 0)
            {
                var w = last >= 0 ? _w[last]! : target;
                w[vertex] = (byte)Math.Max(0, Math.Min(255, w[vertex] + rem));
            }
        }

        // ================================================================== change tracking

        /// <summary>Call before writing vertices of a rectangle (undo capture).</summary>
        public void BeginModify(int c0, int r0, int c1, int r1) => History.Capture(this, c0, r0, c1, r1);

        public void NotifyChanged(int c0, int r0, int c1, int r1, bool heights, bool paint)
        {
            c0 = Math.Max(0, c0);
            r0 = Math.Max(0, r0);
            c1 = Math.Min(Columns - 1, c1);
            r1 = Math.Min(Rows - 1, r1);
            if (c1 < c0 || r1 < r0) return;
            if (heights) _rangeDirty = true;
            Revision++;
            Changed?.Invoke(new TerrainChange(c0, r0, c1, r1, heights, paint));
        }

        public void NotifyAll() => NotifyChanged(0, 0, Columns - 1, Rows - 1, true, true);

        // ================================================================== whole-map helpers

        /// <summary>Sets every vertex height from a function of (x, y).</summary>
        public void Fill(Func<double, double, double> height)
        {
            for (int r = 0; r < Rows; r++)
            {
                double y = VertexY(r);
                for (int c = 0; c < Columns; c++) _h[r * Columns + c] = (float)height(VertexX(c), y);
            }

            NotifyAll();
        }

        /// <summary>Deep copy (without history).</summary>
        public TerrainMap Clone()
        {
            var m = new TerrainMap(Columns, Rows, CellSize, OriginX, OriginY);
            Array.Copy(_h, m._h, _h.Length);
            for (int l = 0; l < _w.Length; l++)
                if (_w[l] is { } w)
                    m._w[l] = (byte[])w.Clone();
            m.RebuildActive();
            return m;
        }

        /// <summary>Same terrain resampled to a new size and cell (bilinear heights, nearest paint).</summary>
        public TerrainMap Resampled(double width, double depth, double cellSize)
        {
            var m = new TerrainMap(width, depth, cellSize, CenterX, CenterY, "grass");
            for (int l = 0; l < m._w.Length; l++) m._w[l] = null;
            var weights = new double[TerrainLayers.Count];
            foreach (int l in _active) m._w[l] = new byte[m._h.Length];
            for (int r = 0; r < m.Rows; r++)
            for (int c = 0; c < m.Columns; c++)
            {
                double x = m.VertexX(c), y = m.VertexY(r);
                int i = m.Index(c, r);
                m._h[i] = (float)Height(x, y);
                SampleLayers(x, y, weights);
                int sum = 0, maxL = -1;
                foreach (int l in _active)
                {
                    int v = (int)Math.Round(weights[l] * 255);
                    m._w[l]![i] = (byte)v;
                    sum += v;
                    if (maxL < 0 || weights[l] > weights[maxL]) maxL = l;
                }

                if (maxL >= 0) m._w[maxL]![i] = (byte)Math.Max(0, Math.Min(255, m._w[maxL]![i] + 255 - sum));
            }

            m.RebuildActive();
            m.CompactLayers();
            return m;
        }

        // ================================================================== persistence

        private const uint Magic = 0x4D545752; // "RWTM"
        private const int FileVersion = 1;

        /// <summary>Compact binary form: header + deflated heights (float32) and layer weights.</summary>
        public byte[] ToBytes()
        {
            using var raw = new MemoryStream();
            using (var w = new BinaryWriter(raw, Encoding.UTF8, true))
            {
                w.Write(Columns);
                w.Write(Rows);
                w.Write(CellSize);
                w.Write(OriginX);
                w.Write(OriginY);
                var bytes = new byte[_h.Length * 4];
                Buffer.BlockCopy(_h, 0, bytes, 0, bytes.Length);
                if (!BitConverter.IsLittleEndian) SwapFloats(bytes);
                w.Write(bytes);
                w.Write(_active.Length);
                foreach (int l in _active)
                {
                    w.Write(TerrainLayers.Get(l).Id);
                    w.Write(_w[l]!);
                }
            }

            using var outStream = new MemoryStream();
            using (var hw = new BinaryWriter(outStream, Encoding.UTF8, true))
            {
                hw.Write(Magic);
                hw.Write(FileVersion);
            }

            using (var z = new DeflateStream(outStream, CompressionLevel.Optimal, true))
            {
                raw.Position = 0;
                raw.CopyTo(z);
            }

            return outStream.ToArray();
        }

        public static TerrainMap FromBytes(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var hr = new BinaryReader(input, Encoding.UTF8, true);
            if (hr.ReadUInt32() != Magic) throw new InvalidDataException("Arazi verisi tanınmadı.");
            int version = hr.ReadInt32();
            if (version > FileVersion) throw new InvalidDataException("Arazi verisi daha yeni bir sürümle kaydedilmiş.");
            using var z = new DeflateStream(input, CompressionMode.Decompress);
            using var r = new BinaryReader(z, Encoding.UTF8);
            int cols = r.ReadInt32(), rows = r.ReadInt32();
            if (cols < 2 || rows < 2 || (long)cols * rows > MaxSamples) throw new InvalidDataException("Arazi boyutu geçersiz.");
            double cell = r.ReadDouble(), ox = r.ReadDouble(), oy = r.ReadDouble();
            var m = new TerrainMap(cols, rows, cell, ox, oy);
            var bytes = ReadExactly(r, cols * rows * 4);
            if (!BitConverter.IsLittleEndian) SwapFloats(bytes);
            Buffer.BlockCopy(bytes, 0, m._h, 0, bytes.Length);
            int layers = r.ReadInt32();
            for (int k = 0; k < layers; k++)
            {
                string id = r.ReadString();
                var w = ReadExactly(r, cols * rows);
                var layer = TerrainLayers.Find(id);
                if (layer == null) continue;
                m._w[layer.Index] = w;
            }

            m.RebuildActive();
            if (m._active.Length == 0) m.FillLayer(TerrainLayers.IndexOf("grass"), notify: false);
            return m;
        }

        private static byte[] ReadExactly(BinaryReader r, int count)
        {
            var b = r.ReadBytes(count);
            if (b.Length != count) throw new InvalidDataException("Arazi verisi eksik.");
            return b;
        }

        private static void SwapFloats(byte[] b)
        {
            for (int i = 0; i + 3 < b.Length; i += 4)
            {
                (b[i], b[i + 3]) = (b[i + 3], b[i]);
                (b[i + 1], b[i + 2]) = (b[i + 2], b[i + 1]);
            }
        }

        // ================================================================== snapshot helpers (undo)

        internal float[] CopyHeights(int c0, int r0, int c1, int r1)
        {
            int w = c1 - c0 + 1, h = r1 - r0 + 1;
            var a = new float[w * h];
            for (int r = 0; r < h; r++) Array.Copy(_h, (r0 + r) * Columns + c0, a, r * w, w);
            return a;
        }

        internal void PasteHeights(int c0, int r0, int c1, int r1, float[] a)
        {
            int w = c1 - c0 + 1, h = r1 - r0 + 1;
            for (int r = 0; r < h; r++) Array.Copy(a, r * w, _h, (r0 + r) * Columns + c0, w);
        }

        internal byte[]?[] CopyWeights(int c0, int r0, int c1, int r1)
        {
            int w = c1 - c0 + 1, h = r1 - r0 + 1;
            var all = new byte[]?[_w.Length];
            foreach (int l in _active)
            {
                var a = new byte[w * h];
                var src = _w[l]!;
                for (int r = 0; r < h; r++) Array.Copy(src, (r0 + r) * Columns + c0, a, r * w, w);
                all[l] = a;
            }

            return all;
        }

        internal void PasteWeights(int c0, int r0, int c1, int r1, byte[]?[] all)
        {
            int w = c1 - c0 + 1, h = r1 - r0 + 1;
            for (int l = 0; l < _w.Length; l++)
            {
                var a = all[l];
                if (a == null)
                {
                    var dst0 = _w[l];
                    if (dst0 == null) continue;
                    for (int r = 0; r < h; r++) Array.Clear(dst0, (r0 + r) * Columns + c0, w);
                    continue;
                }

                var dst = EnsureLayer(l);
                for (int r = 0; r < h; r++) Array.Copy(a, r * w, dst, (r0 + r) * Columns + c0, w);
            }
        }
    }
}
