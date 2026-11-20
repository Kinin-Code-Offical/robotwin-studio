using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.World
{
    // [terrain] Chunked, level-of-detail terrain mesh with baked (blended) layer textures.

    /// <summary>
    /// Renders a <see cref="TerrainMap"/> as chunks of 32×32 cells. Each chunk picks a level of
    /// detail (every 1st, 2nd, 4th or 8th vertex) from its distance to the camera and has skirts
    /// that hide the cracks between levels. Its texture is baked from the layer weights (with noisy
    /// borders) into a small bitmap. Only chunks whose vertices changed, or whose level changes, are
    /// rebuilt; meshes and bitmaps are frozen otherwise, so an idle terrain costs nothing per frame.
    /// </summary>
    public sealed class TerrainView
    {
        public const int ChunkCells = 32;

        private sealed class Chunk
        {
            public int C0, R0, C1, R1;
            public GeometryModel3D Model = null!;
            public int Lod = -1;
            public bool GeometryDirty = true, TextureDirty = true;
            public double Cx, Cy;
        }

        private readonly Chunk[] _chunks;
        private readonly int _nx, _ny;
        private readonly Model3DGroup _group = new Model3DGroup();
        private Point3D _lastCamera = new Point3D(double.NaN, 0, 0);
        private bool _anyDirty = true;

        public TerrainView(TerrainMap map, int textureSize = 128)
        {
            Map = map;
            TextureSize = textureSize;
            _nx = (map.Columns - 2) / ChunkCells + 1;
            _ny = (map.Rows - 2) / ChunkCells + 1;
            _chunks = new Chunk[_nx * _ny];
            for (int j = 0; j < _ny; j++)
            for (int i = 0; i < _nx; i++)
            {
                var c = new Chunk
                {
                    C0 = i * ChunkCells, R0 = j * ChunkCells,
                    C1 = Math.Min(map.Columns - 1, (i + 1) * ChunkCells), R1 = Math.Min(map.Rows - 1, (j + 1) * ChunkCells)
                };
                c.Cx = map.VertexX((c.C0 + c.C1) / 2);
                c.Cy = map.VertexY((c.R0 + c.R1) / 2);
                c.Model = new GeometryModel3D();
                _chunks[j * _nx + i] = c;
                _group.Children.Add(c.Model);
            }

            Visual = new ModelVisual3D { Content = _group };
            map.Changed += OnChanged;
        }

        public TerrainMap Map { get; }
        public ModelVisual3D Visual { get; }
        public int TextureSize { get; }
        public int ChunkCount => _chunks.Length;

        /// <summary>Triangles currently drawn (diagnostics).</summary>
        public int Triangles { get; private set; }

        public void Detach() => Map.Changed -= OnChanged;

        private void OnChanged(TerrainChange ch)
        {
            // A vertex on a chunk border belongs to both neighbours.
            int i0 = Math.Max(0, (ch.C0 - 1) / ChunkCells), i1 = Math.Min(_nx - 1, ch.C1 / ChunkCells);
            int j0 = Math.Max(0, (ch.R0 - 1) / ChunkCells), j1 = Math.Min(_ny - 1, ch.R1 / ChunkCells);
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                var c = _chunks[j * _nx + i];
                if (c.C1 < ch.C0 || c.C0 > ch.C1 || c.R1 < ch.R0 || c.R0 > ch.R1) continue;
                if (ch.Heights) c.GeometryDirty = true;
                if (ch.Paint) c.TextureDirty = true;
                _anyDirty = true;
            }
        }

        /// <summary>
        /// Per frame: re-picks levels of detail when the camera moved and rebuilds dirty chunks within
        /// <paramref name="budgetMs"/> (nearest first). Returns true when something was rebuilt.
        /// </summary>
        public bool Update(Point3D camera, double budgetMs = 6)
        {
            bool moved = double.IsNaN(_lastCamera.X) || (camera - _lastCamera).Length > Map.CellSize * ChunkCells * 0.25;
            if (!moved && !_anyDirty) return false;
            if (moved)
            {
                _lastCamera = camera;
                foreach (var c in _chunks)
                {
                    int lod = LodFor(c, camera);
                    if (lod != c.Lod)
                    {
                        c.Lod = lod;
                        c.GeometryDirty = true;
                        _anyDirty = true;
                    }
                }
            }

            if (!_anyDirty) return false;
            var order = new List<Chunk>();
            foreach (var c in _chunks)
                if (c.GeometryDirty || c.TextureDirty)
                    order.Add(c);
            order.Sort((a, b) => Dist2(a, camera).CompareTo(Dist2(b, camera)));
            var sw = Stopwatch.StartNew();
            int done = 0;
            foreach (var c in order)
            {
                if (done > 0 && sw.Elapsed.TotalMilliseconds > budgetMs) break;
                if (c.Lod < 0) c.Lod = LodFor(c, camera);
                if (c.GeometryDirty)
                {
                    c.Model.Geometry = BuildMesh(c);
                    c.GeometryDirty = false;
                }

                if (c.TextureDirty)
                {
                    c.Model.Material = new DiffuseMaterial(new ImageBrush(Bake(c)) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) });
                    c.Model.Material.Freeze();
                    c.TextureDirty = false;
                }

                done++;
            }

            _anyDirty = false;
            foreach (var c in _chunks)
                if (c.GeometryDirty || c.TextureDirty)
                    _anyDirty = true;
            int tris = 0;
            foreach (var c in _chunks)
                if (c.Model.Geometry is MeshGeometry3D g)
                    tris += g.TriangleIndices.Count / 3;
            Triangles = tris;
            return done > 0;
        }

        /// <summary>Builds everything now (snapshots, first show).</summary>
        public void BuildAll(Point3D camera) => Update(camera, double.PositiveInfinity);

        private double Dist2(Chunk c, Point3D cam)
        {
            double h = Map.Height(c.Cx, c.Cy);
            return (c.Cx - cam.X) * (c.Cx - cam.X) + (c.Cy - cam.Y) * (c.Cy - cam.Y) + (h - cam.Z) * (h - cam.Z);
        }

        private int LodFor(Chunk c, Point3D cam)
        {
            // Small terrains are cheap at full detail.
            if ((long)Map.Columns * Map.Rows <= 129 * 129) return 0;
            double size = Map.CellSize * ChunkCells;
            double d = Math.Sqrt(Dist2(c, cam));
            if (d < 1.6 * size) return 0;
            if (d < 3.2 * size) return 1;
            if (d < 6.4 * size) return 2;
            return 3;
        }

        // ================================================================== geometry

        private MeshGeometry3D BuildMesh(Chunk c)
        {
            int step = 1 << c.Lod;
            var cols = new List<int>();
            for (int x = c.C0; x < c.C1; x += step) cols.Add(x);
            cols.Add(c.C1);
            var rows = new List<int>();
            for (int y = c.R0; y < c.R1; y += step) rows.Add(y);
            rows.Add(c.R1);
            int nc = cols.Count, nr = rows.Count;
            int capacity = nc * nr + 2 * (nc + nr) * 2;
            var pos = new Point3DCollection(capacity);
            var nrm = new Vector3DCollection(capacity);
            var uv = new PointCollection(capacity);
            var idx = new Int32Collection((nc - 1) * (nr - 1) * 6 + (nc + nr) * 12);
            double x0 = Map.VertexX(c.C0), y0 = Map.VertexY(c.R0), w = Map.VertexX(c.C1) - x0, d = Map.VertexY(c.R1) - y0;
            for (int j = 0; j < nr; j++)
            for (int i = 0; i < nc; i++)
            {
                int vc = cols[i], vr = rows[j];
                double x = Map.VertexX(vc), y = Map.VertexY(vr);
                pos.Add(new Point3D(x, y, Map.Heights[Map.Index(vc, vr)]));
                var n = Map.VertexNormal(vc, vr);
                nrm.Add(new Vector3D(n.X, n.Y, n.Z));
                uv.Add(new Point((x - x0) / w, 1 - (y - y0) / d));
            }

            for (int j = 0; j + 1 < nr; j++)
            for (int i = 0; i + 1 < nc; i++)
            {
                int a = j * nc + i, b = a + 1, e = a + nc, f = e + 1;
                // Split along the diagonal that follows the surface better.
                double ha = pos[a].Z, hb = pos[b].Z, he = pos[e].Z, hf = pos[f].Z;
                if (Math.Abs(ha - hf) <= Math.Abs(hb - he))
                {
                    idx.Add(a); idx.Add(b); idx.Add(f);
                    idx.Add(a); idx.Add(f); idx.Add(e);
                }
                else
                {
                    idx.Add(a); idx.Add(b); idx.Add(e);
                    idx.Add(b); idx.Add(f); idx.Add(e);
                }
            }

            // Skirts: deep at the terrain's outer edge (a solid-looking block), short between chunks.
            var (lo, _) = Map.HeightRange;
            double inner = 0.01 + step * Map.CellSize * 0.6;
            void Skirt(int[] ring, bool outer, bool flip)
            {
                int start = pos.Count;
                foreach (int k in ring)
                {
                    var p = pos[k];
                    double z = outer ? lo - Math.Max(0.05, Map.CellSize * 2) : p.Z - inner;
                    pos.Add(new Point3D(p.X, p.Y, z));
                    nrm.Add(outer ? new Vector3D(0, 0, -1) : nrm[k]);
                    uv.Add(uv[k]);
                }

                for (int s = 0; s + 1 < ring.Length; s++)
                {
                    int a = ring[s], b = ring[s + 1], a2 = start + s, b2 = start + s + 1;
                    if (!flip)
                    {
                        idx.Add(a); idx.Add(a2); idx.Add(b);
                        idx.Add(b); idx.Add(a2); idx.Add(b2);
                    }
                    else
                    {
                        idx.Add(a); idx.Add(b); idx.Add(a2);
                        idx.Add(b); idx.Add(b2); idx.Add(a2);
                    }
                }
            }

            var bottom = new int[nc];
            var top = new int[nc];
            for (int i = 0; i < nc; i++)
            {
                bottom[i] = i;
                top[i] = (nr - 1) * nc + i;
            }

            var left = new int[nr];
            var right = new int[nr];
            for (int j = 0; j < nr; j++)
            {
                left[j] = j * nc;
                right[j] = j * nc + nc - 1;
            }

            // Winding so every skirt faces outwards of its chunk.
            Skirt(bottom, c.R0 == 0, false);
            Skirt(top, c.R1 == Map.Rows - 1, true);
            Skirt(left, c.C0 == 0, true);
            Skirt(right, c.C1 == Map.Columns - 1, false);

            var mesh = new MeshGeometry3D { Positions = pos, Normals = nrm, TextureCoordinates = uv, TriangleIndices = idx };
            mesh.Freeze();
            return mesh;
        }

        // ================================================================== texture baking

        private BitmapSource Bake(Chunk c)
        {
            int t = TextureSize;
            var px = new uint[t * t];
            double x0 = Map.VertexX(c.C0), y1 = Map.VertexY(c.R1), w = Map.VertexX(c.C1) - x0, d = y1 - Map.VertexY(c.R0);

            // Layers present in this chunk.
            var layers = new List<int>();
            foreach (int l in Map.ActiveLayers)
            {
                var wl = Map.LayerWeights(l)!;
                bool any = false;
                for (int r = c.R0; r <= c.R1 && !any; r++)
                for (int col = c.C0; col <= c.C1 && !any; col++)
                    any = wl[Map.Index(col, r)] != 0;
                if (any) layers.Add(l);
            }

            if (layers.Count == 0 && Map.ActiveLayers.Count > 0) layers.Add(Map.ActiveLayers[0]);
            var tex = new uint[layers.Count][];
            var tile = new double[layers.Count];
            var arrays = new byte[layers.Count][];
            for (int k = 0; k < layers.Count; k++)
            {
                tex[k] = TerrainTextures.Get(layers[k]);
                tile[k] = TerrainTextures.TileMeters(TerrainLayers.Get(layers[k]));
                arrays[k] = Map.LayerWeights(layers[k])!;
            }

            var wgt = new double[layers.Count];
            int size = TerrainTextures.Size;
            for (int j = 0; j < t; j++)
            {
                double y = y1 - (j + 0.5) / t * d;
                double v = (y - Map.OriginY) / Map.CellSize;
                int r = Math.Max(0, Math.Min(Map.Rows - 2, (int)v));
                double fy = Math.Max(0, Math.Min(1, v - r));
                for (int i = 0; i < t; i++)
                {
                    double x = x0 + (i + 0.5) / t * w;
                    double macro = 0.93 + 0.14 * TerrainTextures.BreakupAt(x * 0.37, y * 0.37, 3);
                    double rr = 0, gg = 0, bb = 0;
                    if (layers.Count == 1)
                    {
                        uint p = Sample(tex[0], x, y, tile[0], size);
                        rr = (p >> 16) & 0xFF;
                        gg = (p >> 8) & 0xFF;
                        bb = p & 0xFF;
                    }
                    else
                    {
                        double u = (x - Map.OriginX) / Map.CellSize;
                        int col = Math.Max(0, Math.Min(Map.Columns - 2, (int)u));
                        double fx = Math.Max(0, Math.Min(1, u - col));
                        int i00 = Map.Index(col, r), i10 = i00 + 1, i01 = i00 + Map.Columns, i11 = i01 + 1;
                        double sum = 0;
                        for (int k = 0; k < layers.Count; k++)
                        {
                            var a = arrays[k];
                            double wv = (a[i00] * (1 - fx) + a[i10] * fx) * (1 - fy) + (a[i01] * (1 - fx) + a[i11] * fx) * fy;
                            wv /= 255.0;
                            // Organic borders: noise sharpens the blend where layers meet.
                            if (wv > 0 && wv < 1) wv = Math.Min(1, Math.Max(0, wv * (0.45 + 1.1 * TerrainTextures.BreakupAt(x * 2.3, y * 2.3, layers[k]))));
                            wv = wv * wv * (3 - 2 * wv);
                            wgt[k] = wv;
                            sum += wv;
                        }

                        if (sum <= 1e-9)
                        {
                            wgt[0] = 1;
                            sum = 1;
                        }

                        for (int k = 0; k < layers.Count; k++)
                        {
                            double f = wgt[k] / sum;
                            if (f <= 0.002) continue;
                            uint p = Sample(tex[k], x, y, tile[k], size);
                            rr += f * ((p >> 16) & 0xFF);
                            gg += f * ((p >> 8) & 0xFF);
                            bb += f * (p & 0xFF);
                        }
                    }

                    px[j * t + i] = 0xFF000000u | (uint)Clamp(rr * macro) << 16 | (uint)Clamp(gg * macro) << 8 | Clamp(bb * macro);
                }
            }

            var bmp = BitmapSource.Create(t, t, 96, 96, PixelFormats.Bgr32, null, px, t * 4);
            bmp.Freeze();
            return bmp;
        }

        private static uint Clamp(double v) => v <= 0 ? 0u : v >= 255 ? 255u : (uint)v;

        private static uint Sample(uint[] tex, double x, double y, double tileMeters, int size)
        {
            double u = x / tileMeters, v = -y / tileMeters;
            int i = (int)Math.Floor((u - Math.Floor(u)) * size), j = (int)Math.Floor((v - Math.Floor(v)) * size);
            if (i >= size) i = size - 1;
            if (j >= size) j = size - 1;
            return tex[j * size + i];
        }
    }
}
