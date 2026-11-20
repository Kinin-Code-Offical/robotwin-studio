using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] VRML 2.0 / VRML97 (.wrl) mesh reader: the format of KiCad's 3D models.
    // Reads Transform/Group/Shape hierarchies, IndexedFaceSet (any polygon, fan
    // triangulated), Box/Cylinder/Sphere, DEF/USE and Material colours.

    public enum VrmlUnits
    {
        /// <summary>KiCad convention: 1 unit = 0.1 inch (2.54 mm), Z up.</summary>
        KiCad,

        /// <summary>Plain VRML: metres, Y up.</summary>
        Meter,

        /// <summary>Millimetres, Y up.</summary>
        Millimeter,

        /// <summary>KiCad when the file says so or the model is small, otherwise metres.</summary>
        Auto
    }

    public static class VrmlReader
    {
        private sealed class Node
        {
            public string Type = string.Empty;
            public Dictionary<string, object> Fields { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

            public object? F(string name) => Fields.TryGetValue(name, out var v) ? v : null;

            public List<double> Nums(string name) => F(name) is List<double> l ? l : new List<double>();
        }

        private sealed class Lexer
        {
            private readonly string _s;
            private int _i;

            public Lexer(string s) => _s = s;

            public int Line { get; private set; } = 1;

            public string? Peek()
            {
                int save = _i, line = Line;
                var t = Next();
                _i = save;
                Line = line;
                return t;
            }

            public string? Next()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == '\n') Line++;
                    if (char.IsWhiteSpace(c) || c == ',')
                    {
                        _i++;
                        continue;
                    }

                    if (c == '#')
                    {
                        while (_i < _s.Length && _s[_i] != '\n') _i++;
                        continue;
                    }

                    break;
                }

                if (_i >= _s.Length) return null;
                char ch = _s[_i];
                if (ch == '{' || ch == '}' || ch == '[' || ch == ']')
                {
                    _i++;
                    return ch.ToString();
                }

                if (ch == '"')
                {
                    var sb = new StringBuilder("\"");
                    _i++;
                    while (_i < _s.Length && _s[_i] != '"')
                    {
                        if (_s[_i] == '\\' && _i + 1 < _s.Length) _i++;
                        if (_s[_i] == '\n') Line++;
                        sb.Append(_s[_i++]);
                    }

                    _i++;
                    return sb.ToString();
                }

                int start = _i;
                while (_i < _s.Length && !char.IsWhiteSpace(_s[_i]) && _s[_i] != ',' && _s[_i] != '{' && _s[_i] != '}' && _s[_i] != '[' && _s[_i] != ']' && _s[_i] != '#' && _s[_i] != '"') _i++;
                return _s.Substring(start, _i - start);
            }
        }

        private static bool IsNumber(string? t, out double v)
        {
            v = 0;
            return t != null && t.Length > 0 && (char.IsDigit(t[0]) || t[0] == '-' || t[0] == '+' || t[0] == '.') &&
                   double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        /// <summary>Loads a .wrl file as a mesh in metres, Z up.</summary>
        public static MeshModel Load(string path, VrmlUnits units = VrmlUnits.Auto, ImportLog? log = null)
        {
            var info = new FileInfo(path);
            if (info.Length > 256L * 1024 * 1024) throw new InvalidDataException("VRML dosyası 256 MB'tan büyük.");
            string text;
            using (var fs = File.OpenRead(path))
            {
                // Some exporters write gzip-compressed .wrl (.wrz).
                int b0 = fs.ReadByte(), b1 = fs.ReadByte();
                fs.Position = 0;
                if (b0 == 0x1F && b1 == 0x8B)
                {
                    using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
                    using var r = new StreamReader(gz, Encoding.UTF8);
                    text = r.ReadToEnd();
                }
                else
                {
                    using var r = new StreamReader(fs, Encoding.UTF8);
                    text = r.ReadToEnd();
                }
            }

            var mesh = Parse(text, units, log, path);
            mesh.Name = Path.GetFileNameWithoutExtension(path);
            return mesh;
        }

        public static MeshModel Parse(string text, VrmlUnits units = VrmlUnits.Auto, ImportLog? log = null, string? file = null)
        {
            log ??= new ImportLog();
            if (text.StartsWith("#VRML V1.0", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("VRML 1.0 dosyaları desteklenmiyor (yalnız VRML 2.0 / VRML97). Modeli STL veya OBJ olarak dışa aktarıp ekleyin.");
            }

            if (!text.StartsWith("#VRML", StringComparison.OrdinalIgnoreCase))
            {
                log.Warn("'#VRML V2.0' başlığı yok; yine de VRML 2.0 olarak okunuyor.", file);
            }

            var lex = new Lexer(text);
            var defs = new Dictionary<string, Node>(StringComparer.Ordinal);
            var roots = new List<Node>();
            int guard = 0;
            while (lex.Peek() != null)
            {
                if (++guard > 10_000_000) break;
                // ReadNode consumes at least one token even when it returns null (stray tokens, ROUTE, PROTO).
                var n = ReadNode(lex, defs, log, file, 0);
                if (n != null) roots.Add(n);
            }

            var tris = new List<(Vec3 A, Vec3 B, Vec3 C, MeshColor Color)>();
            var unsupported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in roots) Visit(r, Mat4.Identity, tris, unsupported, 0);
            foreach (var u in unsupported) log.Info($"VRML '{u}' düğümü çizilemiyor; atlandı.", file);
            if (tris.Count == 0) throw new InvalidDataException("VRML dosyasında çizilebilir yüzey (IndexedFaceSet / Box / Cylinder / Sphere) bulunamadı.");

            // Units and up axis.
            double maxExtent = 0;
            {
                var b = Aabb.FromPoints(tris.SelectMany(t => new[] { t.A, t.B, t.C }));
                maxExtent = Math.Max(b.Size.X, Math.Max(b.Size.Y, b.Size.Z));
            }

            if (units == VrmlUnits.Auto)
            {
                bool kicad = text.IndexOf("kicad", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("kicad", StringComparison.OrdinalIgnoreCase) < 4096;
                units = kicad || (maxExtent > 0.05 && maxExtent < 200) ? VrmlUnits.KiCad : VrmlUnits.Meter;
                log.Info(units == VrmlUnits.KiCad ? "birim: KiCad (1 birim = 2.54 mm), Z yukarı." : "birim: metre, Y yukarı.", file);
            }

            double scale = units switch
            {
                VrmlUnits.KiCad => 0.00254,
                VrmlUnits.Millimeter => 0.001,
                _ => 1.0
            };
            bool yUp = units != VrmlUnits.KiCad;

            var mesh = new MeshModel();
            var colors = new List<MeshColor>();
            foreach (var (a, b, c, col) in tris)
            {
                Vec3 Map(Vec3 p) => yUp ? new Vec3(p.X * scale, -p.Z * scale, p.Y * scale) : p * scale;
                int i0 = mesh.AddVertex(Map(a)), i1 = mesh.AddVertex(Map(b)), i2 = mesh.AddVertex(Map(c));
                mesh.AddTriangle(i0, i1, i2);
                colors.Add(col);
            }

            mesh.TriangleColors = colors;
            return mesh;
        }

        // ------------------------------------------------------------------ parsing

        private static Node? ReadNode(Lexer lex, Dictionary<string, Node> defs, ImportLog log, string? file, int depth)
        {
            if (depth > 200) throw new InvalidDataException("VRML iç içe düğüm derinliği çok fazla.");
            string? t = lex.Peek();
            if (t == null) return null;
            if (t == "USE")
            {
                lex.Next();
                string name = lex.Next() ?? string.Empty;
                if (defs.TryGetValue(name, out var used)) return used;
                log.Warn($"USE {name}: tanımı (DEF) yok.", file, lex.Line);
                return new Node { Type = "Group" };
            }

            string? def = null;
            if (t == "DEF")
            {
                lex.Next();
                def = lex.Next();
                t = lex.Peek();
            }

            if (t == null) return null;
            if (t.Length == 0 || !(char.IsLetter(t[0]) || t[0] == '_'))
            {
                lex.Next();
                return null;
            }

            if (t == "PROTO" || t == "EXTERNPROTO" || t == "ROUTE")
            {
                lex.Next();
                log.Warn($"{t} desteklenmiyor; atlandı.", file, lex.Line);
                SkipUntilBlockEnd(lex, t == "ROUTE" ? 3 : -1);
                return null;
            }

            lex.Next();
            if (lex.Peek() != "{")
            {
                return null;
            }

            lex.Next();
            var node = new Node { Type = t };
            if (def != null) defs[def] = node;
            while (true)
            {
                string? field = lex.Next();
                if (field == null) throw new InvalidDataException($"VRML: '{t}' düğümü kapanmadan dosya bitti.");
                if (field == "}") break;
                node.Fields[field] = ReadValue(lex, defs, log, file, depth);
            }

            return node;
        }

        private static void SkipUntilBlockEnd(Lexer lex, int tokens)
        {
            if (tokens > 0)
            {
                for (int i = 0; i < tokens; i++) lex.Next();
                return;
            }

            int level = 0;
            while (true)
            {
                string? t = lex.Next();
                if (t == null) return;
                if (t == "{" || t == "[") level++;
                else if (t == "}" || t == "]")
                {
                    level--;
                    if (level <= 0 && t == "}") return;
                }
            }
        }

        private static object ReadValue(Lexer lex, Dictionary<string, Node> defs, ImportLog log, string? file, int depth)
        {
            string? t = lex.Peek();
            if (t == null) return string.Empty;
            if (t == "[")
            {
                lex.Next();
                var nums = new List<double>();
                var nodes = new List<Node>();
                var strings = new List<string>();
                while (true)
                {
                    string? x = lex.Peek();
                    if (x == null) break;
                    if (x == "]")
                    {
                        lex.Next();
                        break;
                    }

                    if (IsNumber(x, out double v))
                    {
                        lex.Next();
                        nums.Add(v);
                    }
                    else if (x.StartsWith("\"", StringComparison.Ordinal))
                    {
                        lex.Next();
                        strings.Add(x.Substring(1));
                    }
                    else if (x == "TRUE" || x == "FALSE")
                    {
                        lex.Next();
                        nums.Add(x == "TRUE" ? 1 : 0);
                    }
                    else
                    {
                        var n = ReadNode(lex, defs, log, file, depth + 1);
                        if (n != null) nodes.Add(n);
                    }
                }

                if (nodes.Count > 0) return nodes;
                if (strings.Count > 0) return strings;
                return nums;
            }

            if (IsNumber(t, out _))
            {
                var nums = new List<double>();
                while (IsNumber(lex.Peek(), out double v))
                {
                    lex.Next();
                    nums.Add(v);
                }

                return nums;
            }

            if (t.StartsWith("\"", StringComparison.Ordinal))
            {
                lex.Next();
                return t.Substring(1);
            }

            if (t == "TRUE" || t == "FALSE")
            {
                lex.Next();
                return new List<double> { t == "TRUE" ? 1 : 0 };
            }

            if (t == "NULL")
            {
                lex.Next();
                return string.Empty;
            }

            var node = ReadNode(lex, defs, log, file, depth + 1);
            return node != null ? node : (object)t;
        }

        // ------------------------------------------------------------------ geometry

        private readonly struct Mat4
        {
            private readonly double[] _m;

            private Mat4(double[] m) => _m = m;

            public static Mat4 Identity => new Mat4(new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 });

            public static Mat4 Translate(double x, double y, double z) => new Mat4(new double[] { 1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z });

            public static Mat4 Scale(double x, double y, double z) => new Mat4(new double[] { x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0 });

            public static Mat4 Rotate(double ax, double ay, double az, double angle)
            {
                double len = Math.Sqrt(ax * ax + ay * ay + az * az);
                if (len < 1e-12 || Math.Abs(angle) < 1e-15) return Identity;
                ax /= len;
                ay /= len;
                az /= len;
                double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c;
                return new Mat4(new[]
                {
                    t * ax * ax + c, t * ax * ay - s * az, t * ax * az + s * ay, 0,
                    t * ax * ay + s * az, t * ay * ay + c, t * ay * az - s * ax, 0,
                    t * ax * az - s * ay, t * ay * az + s * ax, t * az * az + c, 0
                });
            }

            public static Mat4 operator *(Mat4 a, Mat4 b)
            {
                var r = new double[12];
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        double v = a._m[i * 4 + 0] * b._m[0 * 4 + j] + a._m[i * 4 + 1] * b._m[1 * 4 + j] + a._m[i * 4 + 2] * b._m[2 * 4 + j];
                        if (j == 3) v += a._m[i * 4 + 3];
                        r[i * 4 + j] = v;
                    }
                }

                return new Mat4(r);
            }

            public Vec3 Apply(double x, double y, double z) => new Vec3(
                _m[0] * x + _m[1] * y + _m[2] * z + _m[3],
                _m[4] * x + _m[5] * y + _m[6] * z + _m[7],
                _m[8] * x + _m[9] * y + _m[10] * z + _m[11]);

            public bool Mirrors
            {
                get
                {
                    double det = _m[0] * (_m[5] * _m[10] - _m[6] * _m[9]) - _m[1] * (_m[4] * _m[10] - _m[6] * _m[8]) + _m[2] * (_m[4] * _m[9] - _m[5] * _m[8]);
                    return det < 0;
                }
            }
        }

        private static List<Node> Children(Node n, string field = "children") =>
            n.F(field) switch
            {
                List<Node> l => l,
                Node single => new List<Node> { single },
                _ => new List<Node>()
            };

        private static void Visit(Node n, Mat4 m, List<(Vec3, Vec3, Vec3, MeshColor)> tris, HashSet<string> unsupported, int depth)
        {
            if (depth > 200) return;
            switch (n.Type)
            {
                case "Transform":
                {
                    var t = n.Nums("translation");
                    var r = n.Nums("rotation");
                    var s = n.Nums("scale");
                    var c = n.Nums("center");
                    var local = Mat4.Identity;
                    if (t.Count >= 3) local = Mat4.Translate(t[0], t[1], t[2]);
                    if (c.Count >= 3) local = local * Mat4.Translate(c[0], c[1], c[2]);
                    if (r.Count >= 4) local = local * Mat4.Rotate(r[0], r[1], r[2], r[3]);
                    if (s.Count >= 3) local = local * Mat4.Scale(s[0], s[1], s[2]);
                    if (c.Count >= 3) local = local * Mat4.Translate(-c[0], -c[1], -c[2]);
                    foreach (var ch in Children(n)) Visit(ch, m * local, tris, unsupported, depth + 1);
                    break;
                }

                case "Group":
                case "Anchor":
                case "Billboard":
                case "Collision":
                    foreach (var ch in Children(n)) Visit(ch, m, tris, unsupported, depth + 1);
                    break;
                case "Switch":
                {
                    var choices = Children(n, "choice");
                    if (choices.Count == 0) choices = Children(n);
                    var w = n.Nums("whichChoice");
                    int k = w.Count > 0 ? (int)w[0] : -1;
                    if (k >= 0 && k < choices.Count) Visit(choices[k], m, tris, unsupported, depth + 1);
                    break;
                }

                case "LOD":
                {
                    var levels = Children(n, "level");
                    if (levels.Count == 0) levels = Children(n);
                    if (levels.Count > 0) Visit(levels[0], m, tris, unsupported, depth + 1);
                    break;
                }

                case "Shape":
                    Shape(n, m, tris, unsupported);
                    break;
                case "WorldInfo":
                case "NavigationInfo":
                case "Viewpoint":
                case "Background":
                case "DirectionalLight":
                case "PointLight":
                case "SpotLight":
                case "Fog":
                case "Sound":
                case "TimeSensor":
                    break;
                default:
                    unsupported.Add(n.Type);
                    break;
            }
        }

        private static MeshColor MaterialColor(Node shape)
        {
            var mat = (shape.F("appearance") as Node)?.F("material") as Node;
            var d = mat?.Nums("diffuseColor") ?? new List<double>();
            var e = mat?.Nums("emissiveColor") ?? new List<double>();
            double tr = mat?.Nums("transparency") is { Count: > 0 } tl ? tl[0] : 0;
            double r = 0.7, g = 0.7, b = 0.7;
            if (d.Count >= 3)
            {
                r = d[0];
                g = d[1];
                b = d[2];
            }
            else if (e.Count >= 3)
            {
                r = e[0];
                g = e[1];
                b = e[2];
            }

            byte B(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v * 255)));
            return new MeshColor(B(r), B(g), B(b), B(1 - tr));
        }

        private static void Shape(Node shape, Mat4 m, List<(Vec3, Vec3, Vec3, MeshColor)> tris, HashSet<string> unsupported)
        {
            var color = MaterialColor(shape);
            if (!(shape.F("geometry") is Node geo)) return;
            bool flip = m.Mirrors;
            void Tri(Vec3 a, Vec3 b, Vec3 c, MeshColor col)
            {
                if (flip) tris.Add((a, c, b, col));
                else tris.Add((a, b, c, col));
            }

            switch (geo.Type)
            {
                case "IndexedFaceSet":
                {
                    var pts = (geo.F("coord") as Node)?.Nums("point") ?? new List<double>();
                    var idx = geo.Nums("coordIndex");
                    bool ccw = !(geo.F("ccw") is List<double> cl && cl.Count > 0 && cl[0] == 0);
                    int count = pts.Count / 3;
                    var colorNode = (geo.F("color") as Node)?.Nums("color");
                    bool perVertex = !(geo.F("colorPerVertex") is List<double> pv && pv.Count > 0 && pv[0] == 0);
                    var poly = new List<int>();
                    int face = 0;
                    void Emit()
                    {
                        var col = color;
                        if (colorNode != null && colorNode.Count >= 3 && !perVertex && colorNode.Count >= (face + 1) * 3)
                        {
                            col = new MeshColor((byte)(colorNode[face * 3] * 255), (byte)(colorNode[face * 3 + 1] * 255), (byte)(colorNode[face * 3 + 2] * 255), color.A);
                        }

                        for (int k = 1; k + 1 < poly.Count; k++)
                        {
                            int a = poly[0], b = poly[k], c = poly[k + 1];
                            if (a >= count || b >= count || c >= count || a < 0 || b < 0 || c < 0) continue;
                            var pa = m.Apply(pts[a * 3], pts[a * 3 + 1], pts[a * 3 + 2]);
                            var pb = m.Apply(pts[b * 3], pts[b * 3 + 1], pts[b * 3 + 2]);
                            var pc = m.Apply(pts[c * 3], pts[c * 3 + 1], pts[c * 3 + 2]);
                            if (ccw) Tri(pa, pb, pc, col);
                            else Tri(pa, pc, pb, col);
                        }

                        poly.Clear();
                        face++;
                    }

                    foreach (double d in idx)
                    {
                        int i = (int)d;
                        if (i < 0) Emit();
                        else poly.Add(i);
                    }

                    if (poly.Count > 0) Emit();
                    break;
                }

                case "Box":
                {
                    var size = geo.Nums("size");
                    double sx = size.Count >= 3 ? size[0] : 2, sy = size.Count >= 3 ? size[1] : 2, sz = size.Count >= 3 ? size[2] : 2;
                    AddMesh(MeshModel.CreateBox(new Vec3(sx, sy, sz)), m, color, Tri);
                    break;
                }

                case "Sphere":
                {
                    var r = geo.Nums("radius");
                    AddMesh(MeshModel.CreateSphere(r.Count > 0 ? r[0] : 1, 24, 12), m, color, Tri);
                    break;
                }

                case "Cylinder":
                case "Cone":
                {
                    var r = geo.Nums(geo.Type == "Cone" ? "bottomRadius" : "radius");
                    var h = geo.Nums("height");
                    // MeshModel cylinders run along Z; VRML's along Y.
                    var cyl = MeshModel.CreateCylinder(r.Count > 0 ? r[0] : 1, h.Count > 0 ? h[0] : 2, 24);
                    AddMesh(cyl, m * Mat4.Rotate(1, 0, 0, -Math.PI / 2), color, Tri);
                    break;
                }

                default:
                    unsupported.Add(geo.Type);
                    break;
            }
        }

        private static void AddMesh(MeshModel mesh, Mat4 m, MeshColor color, Action<Vec3, Vec3, Vec3, MeshColor> tri)
        {
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                var (a, b, c) = mesh.Triangle(t);
                tri(m.Apply(a.X, a.Y, a.Z), m.Apply(b.X, b.Y, b.Z), m.Apply(c.X, c.Y, c.Z), color);
            }
        }
    }
}
