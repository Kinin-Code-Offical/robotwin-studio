using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Mechanics
{
    public sealed class MeshLoadOptions
    {
        /// <summary>Unit of the numbers in the file; null = format default (STL: mm, OBJ/PLY: m).</summary>
        public MeshUnits? Units { get; set; }

        /// <summary>Extra multiplier applied after the unit conversion.</summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>Up axis of the source file; Y-up files are rotated to Z-up.</summary>
        public MeshUpAxis UpAxis { get; set; } = MeshUpAxis.Z;

        /// <summary>Merge identical STL vertices so triangles share them (needed for closedness checks).</summary>
        public bool Weld { get; set; } = true;

        public bool ReadNormals { get; set; } = true;
        public bool ReadColors { get; set; } = true;

        /// <summary>Read OBJ .mtl files (Kd colours) found next to the model or via <see cref="FileResolver"/>.</summary>
        public bool ReadMaterials { get; set; } = true;

        /// <summary>Opens files referenced by a model (OBJ mtllib) when loading from a stream; null = none.</summary>
        public Func<string, Stream?>? FileResolver { get; set; }

        internal static MeshUnits DefaultUnits(MeshFileFormat f) => f == MeshFileFormat.Stl ? MeshUnits.Millimeter : MeshUnits.Meter;
        internal double Factor(MeshFileFormat f) => (Units ?? DefaultUnits(f)).ToMetres() * Scale;
    }

    public sealed class MeshSaveOptions
    {
        /// <summary>Unit written to the file; null = format default (STL: mm, OBJ/PLY: m).</summary>
        public MeshUnits? Units { get; set; }

        public MeshUpAxis UpAxis { get; set; } = MeshUpAxis.Z;

        /// <summary>Binary STL / binary_little_endian PLY (default) or ASCII.</summary>
        public bool Binary { get; set; } = true;

        public bool WriteColors { get; set; } = true;
        public bool WriteNormals { get; set; } = true;

        internal double Factor(MeshFileFormat f) => (Units ?? MeshLoadOptions.DefaultUnits(f)).ToMetres();
    }

    public sealed partial class MeshModel
    {
        // ================================================================== loading

        /// <summary>Loads an STL, OBJ or PLY file (format from the extension, STL/PLY also sniffed).</summary>
        public static MeshModel Load(string path, MeshLoadOptions? options = null)
        {
            options ??= new MeshLoadOptions();
            var format = FormatFromExtension(path);
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            var resolver = options.FileResolver;
            if (resolver == null && dir != null)
            {
                resolver = name =>
                {
                    var p = Path.Combine(dir, name);
                    if (!File.Exists(p)) p = Path.Combine(dir, Path.GetFileName(name));
                    return File.Exists(p) ? File.OpenRead(p) : null;
                };
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            var m = Load(fs, format, options, resolver);
            if (string.IsNullOrEmpty(m.Name)) m.Name = Path.GetFileNameWithoutExtension(path);
            return m;
        }

        public static MeshModel Load(Stream stream, MeshFileFormat format, MeshLoadOptions? options = null) =>
            Load(stream, format, options ?? new MeshLoadOptions(), options?.FileResolver);

        private static MeshModel Load(Stream stream, MeshFileFormat format, MeshLoadOptions options, Func<string, Stream?>? resolver)
        {
            var reader = new ByteReader(stream);
            if (format == MeshFileFormat.Auto) format = Sniff(reader);
            switch (format)
            {
                case MeshFileFormat.Stl: return ReadStl(reader, stream, options);
                case MeshFileFormat.Obj: return ReadObj(reader, options, resolver);
                case MeshFileFormat.Ply: return ReadPly(reader, options);
                default: throw new NotSupportedException("Unknown model format.");
            }
        }

        public static MeshModel LoadStl(Stream stream, MeshLoadOptions? options = null) => Load(stream, MeshFileFormat.Stl, options);
        public static MeshModel LoadObj(Stream stream, MeshLoadOptions? options = null) => Load(stream, MeshFileFormat.Obj, options);
        public static MeshModel LoadPly(Stream stream, MeshLoadOptions? options = null) => Load(stream, MeshFileFormat.Ply, options);

        public static MeshFileFormat FormatFromExtension(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".stl": return MeshFileFormat.Stl;
                case ".obj": return MeshFileFormat.Obj;
                case ".ply": return MeshFileFormat.Ply;
                default: return MeshFileFormat.Auto;
            }
        }

        private static MeshFileFormat Sniff(ByteReader r)
        {
            r.Ensure(512);
            var head = r.Peek(Math.Min(r.Available, 512));
            if (head.Length >= 3 && head[0] == 'p' && head[1] == 'l' && head[2] == 'y') return MeshFileFormat.Ply;
            var text = Encoding.ASCII.GetString(head.ToArray());
            if (text.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase) && text.Contains("facet")) return MeshFileFormat.Stl;
            if (head.Length >= 84 && !text.Contains("\nv ") && !text.StartsWith("v ") && !text.Contains("\nf ")) return MeshFileFormat.Stl;
            return MeshFileFormat.Obj;
        }

        private static Vec3 FromFile(double x, double y, double z, double f, MeshUpAxis up) =>
            up == MeshUpAxis.Y ? new Vec3(x * f, -z * f, y * f) : new Vec3(x * f, y * f, z * f);

        private static Vec3 DirFromFile(double x, double y, double z, MeshUpAxis up) =>
            (up == MeshUpAxis.Y ? new Vec3(x, -z, y) : new Vec3(x, y, z)).Normalized();

        // ------------------------------------------------------------------ STL

        private static MeshModel ReadStl(ByteReader r, Stream stream, MeshLoadOptions o)
        {
            r.Ensure(84);
            var head = r.Peek(Math.Min(r.Available, 84));
            bool startsSolid = Encoding.ASCII.GetString(head.Slice(0, Math.Min(80, head.Length)).ToArray()).TrimStart()
                .StartsWith("solid", StringComparison.OrdinalIgnoreCase);
            long remaining = -1;
            try
            {
                if (stream.CanSeek) remaining = stream.Length - stream.Position + r.Available;
            }
            catch (NotSupportedException)
            {
            }

            bool binary;
            if (head.Length < 84) binary = false;
            else
            {
                uint n = BinaryPrimitives.ReadUInt32LittleEndian(head.Slice(80, 4));
                long expected = 84 + 50L * n;
                if (remaining >= 0) binary = expected == remaining || !startsSolid;
                else
                {
                    r.Ensure(1024);
                    var more = Encoding.ASCII.GetString(r.Peek(Math.Min(r.Available, 1024)).ToArray());
                    binary = !(startsSolid && more.IndexOf("facet", StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }

            return binary ? ReadStlBinary(r, remaining, o) : ReadStlAscii(r, o);
        }

        private static MeshModel ReadStlBinary(ByteReader r, long remaining, MeshLoadOptions o)
        {
            if (!r.Ensure(84)) throw new FormatException("STL file is too short.");
            var header = r.Peek(84);
            string headerText = Encoding.ASCII.GetString(header.Slice(0, 80).ToArray());
            long count = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(80, 4));
            r.Skip(84);
            if (remaining >= 0) count = Math.Min(count, (remaining - 84) / 50);

            // Materialise "COLOR=" header gives the default colour and flips the meaning of bit 15.
            bool magics = false;
            MeshColor magicsDefault = MeshColor.DefaultGrey;
            int ci = headerText.IndexOf("COLOR=", StringComparison.Ordinal);
            if (ci >= 0 && ci + 10 <= 80)
            {
                magics = true;
                magicsDefault = new MeshColor(header[ci + 6], header[ci + 7], header[ci + 8], header[ci + 9]);
            }

            var m = new MeshModel();
            var nameText = headerText.TrimEnd('\0', ' ');
            if (nameText.StartsWith("solid ", StringComparison.OrdinalIgnoreCase)) m.Name = nameText.Substring(6).Trim();
            double f = o.Factor(MeshFileFormat.Stl);
            int cap = (int)Math.Min(count, int.MaxValue / 4);
            m.Indices.Capacity = 3 * cap;
            m.Vertices.Capacity = o.Weld ? cap / 2 + 16 : 3 * cap;
            var weld = o.Weld ? new Dictionary<FloatKey, int>(cap / 2 + 16) : null;
            List<MeshColor>? colors = null;
            bool anyColor = false;

            for (long t = 0; t < count; t++)
            {
                if (!r.Ensure(50)) break;
                var span = r.Peek(50);
                for (int k = 0; k < 3; k++)
                {
                    int bx = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12 + 12 * k));
                    int by = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(16 + 12 * k));
                    int bz = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(20 + 12 * k));
                    int index;
                    if (weld != null)
                    {
                        var key = new FloatKey(bx, by, bz);
                        if (!weld.TryGetValue(key, out index))
                        {
                            index = m.Vertices.Count;
                            weld.Add(key, index);
                            m.Vertices.Add(FromFile(BitConverter.Int32BitsToSingle(bx), BitConverter.Int32BitsToSingle(by),
                                BitConverter.Int32BitsToSingle(bz), f, o.UpAxis));
                        }
                    }
                    else
                    {
                        index = m.Vertices.Count;
                        m.Vertices.Add(FromFile(BitConverter.Int32BitsToSingle(bx), BitConverter.Int32BitsToSingle(by),
                            BitConverter.Int32BitsToSingle(bz), f, o.UpAxis));
                    }

                    m.Indices.Add(index);
                }

                if (o.ReadColors)
                {
                    ushort attr = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(48));
                    MeshColor c;
                    bool has;
                    if (magics)
                    {
                        has = true;
                        c = (attr & 0x8000) == 0
                            ? new MeshColor(C5(attr), C5(attr >> 5), C5(attr >> 10))
                            : magicsDefault;
                    }
                    else
                    {
                        has = (attr & 0x8000) != 0;
                        c = has ? new MeshColor(C5(attr >> 10), C5(attr >> 5), C5(attr)) : MeshColor.DefaultGrey;
                    }

                    if (has && !anyColor)
                    {
                        anyColor = true;
                        colors = new List<MeshColor>(cap);
                        for (long k = 0; k < t; k++) colors.Add(MeshColor.DefaultGrey);
                    }

                    colors?.Add(c);
                }

                r.Skip(50);
            }

            m.TriangleColors = colors;
            return m;
        }

        private static byte C5(int v) => (byte)((v & 31) * 255 / 31);

        private static MeshModel ReadStlAscii(ByteReader r, MeshLoadOptions o)
        {
            var m = new MeshModel();
            double f = o.Factor(MeshFileFormat.Stl);
            var weld = o.Weld ? new Dictionary<FloatKey, int>() : null;
            var loop = new List<int>(4);
            var solids = new List<(string Name, int FirstTriangle)>();
            int lineNo = 0;
            while (r.ReadLine(out int start, out int len))
            {
                lineNo++;
                var tk = new Tokenizer(r.Slice(start, len));
                if (!tk.Next(out var kw)) continue;
                if (AsciiEq(kw, "vertex"u8))
                {
                    double x = Num(ref tk, lineNo), y = Num(ref tk, lineNo), z = Num(ref tk, lineNo);
                    float fx = (float)x, fy = (float)y, fz = (float)z;
                    int index;
                    var key = new FloatKey(BitConverter.SingleToInt32Bits(fx), BitConverter.SingleToInt32Bits(fy), BitConverter.SingleToInt32Bits(fz));
                    if (weld == null || !weld.TryGetValue(key, out index))
                    {
                        index = m.Vertices.Count;
                        weld?.Add(key, index);
                        m.Vertices.Add(FromFile(fx, fy, fz, f, o.UpAxis));
                    }

                    loop.Add(index);
                }
                else if (AsciiEq(kw, "endloop"u8))
                {
                    for (int i = 1; i + 1 < loop.Count; i++) m.AddTriangle(loop[0], loop[i], loop[i + 1]);
                    loop.Clear();
                }
                else if (AsciiEq(kw, "solid"u8))
                {
                    string name = Encoding.UTF8.GetString(tk.Rest().ToArray()).Trim();
                    solids.Add((name, m.TriangleCount));
                }
            }

            if (solids.Count == 1) m.Name = solids[0].Name;
            else if (solids.Count > 1)
            {
                m.TriangleGroups = new List<int>(m.TriangleCount);
                for (int s = 0; s < solids.Count; s++)
                {
                    m.Groups.Add(new MeshGroup { Name = solids[s].Name });
                    int end = s + 1 < solids.Count ? solids[s + 1].FirstTriangle : m.TriangleCount;
                    for (int t = solids[s].FirstTriangle; t < end; t++) m.TriangleGroups.Add(s);
                }
            }

            return m;
        }

        // ------------------------------------------------------------------ OBJ

        private static MeshModel ReadObj(ByteReader r, MeshLoadOptions o, Func<string, Stream?>? resolver)
        {
            var m = new MeshModel();
            double f = o.Factor(MeshFileFormat.Obj);
            var normals = new List<Vec3>();
            int texCount = 0;
            List<MeshColor>? vcolors = null;
            var groupKeys = new Dictionary<(string, string?), int>();
            var triGroups = new List<int>();
            string objName = string.Empty, groupName = string.Empty;
            string? material = null;
            int currentGroup = -1;
            bool groupDirty = true;
            var libs = new List<string>();
            var poly = new List<(int V, int N)>(8);
            int lineNo = 0;
            byte[]? joined = null;

            while (r.ReadLine(out int start, out int len))
            {
                lineNo++;
                ReadOnlySpan<byte> line = r.Slice(start, len);
                // Line continuation with a trailing backslash.
                if (line.Length > 0 && line[line.Length - 1] == '\\')
                {
                    var acc = new List<byte>(line.Slice(0, line.Length - 1).ToArray());
                    while (r.ReadLine(out int s2, out int l2))
                    {
                        lineNo++;
                        var l = r.Slice(s2, l2);
                        bool more = l.Length > 0 && l[l.Length - 1] == '\\';
                        acc.Add((byte)' ');
                        acc.AddRange((more ? l.Slice(0, l.Length - 1) : l).ToArray());
                        if (!more) break;
                    }

                    joined = acc.ToArray();
                    line = joined;
                }

                var tk = new Tokenizer(line);
                if (!tk.Next(out var kw) || kw[0] == '#') continue;

                if (kw.Length == 1 && kw[0] == 'v')
                {
                    double x = Num(ref tk, lineNo), y = Num(ref tk, lineNo), z = Num(ref tk, lineNo);
                    m.Vertices.Add(FromFile(x, y, z, f, o.UpAxis));
                    // Optional "v x y z r g b" vertex colours (or "v x y z w").
                    double a0 = 0, a1 = 0, a2 = 0;
                    int extra = 0;
                    if (tk.Next(out var t0))
                    {
                        a0 = ParseDouble(t0, lineNo);
                        extra++;
                        if (tk.Next(out var t1))
                        {
                            a1 = ParseDouble(t1, lineNo);
                            extra++;
                            if (tk.Next(out var t2))
                            {
                                a2 = ParseDouble(t2, lineNo);
                                extra++;
                            }
                        }
                    }

                    if (extra == 3 && o.ReadColors)
                    {
                        if (vcolors == null)
                        {
                            vcolors = new List<MeshColor>(m.Vertices.Count);
                            for (int i = 0; i < m.Vertices.Count - 1; i++) vcolors.Add(MeshColor.DefaultGrey);
                        }

                        vcolors.Add(MeshColor.FromFloat(a0, a1, a2));
                    }
                    else vcolors?.Add(MeshColor.DefaultGrey);
                }
                else if (AsciiEq(kw, "vn"u8))
                {
                    double x = Num(ref tk, lineNo), y = Num(ref tk, lineNo), z = Num(ref tk, lineNo);
                    normals.Add(DirFromFile(x, y, z, o.UpAxis));
                }
                else if (AsciiEq(kw, "vt"u8))
                {
                    texCount++;
                }
                else if (kw.Length == 1 && kw[0] == 'f')
                {
                    poly.Clear();
                    while (tk.Next(out var item))
                    {
                        int slash1 = item.IndexOf((byte)'/');
                        var vPart = slash1 < 0 ? item : item.Slice(0, slash1);
                        int vi = ResolveIndex(ParseInt(vPart, lineNo), m.Vertices.Count, lineNo, "vertex");
                        int ni = -1;
                        if (slash1 >= 0)
                        {
                            var rest = item.Slice(slash1 + 1);
                            int slash2 = rest.IndexOf((byte)'/');
                            if (slash2 >= 0 && slash2 + 1 < rest.Length && o.ReadNormals)
                                ni = ResolveIndex(ParseInt(rest.Slice(slash2 + 1), lineNo), normals.Count, lineNo, "normal");
                        }

                        poly.Add((vi, ni));
                    }

                    if (poly.Count < 3) continue;
                    if (groupDirty)
                    {
                        string name = groupName.Length > 0 ? groupName : objName;
                        var key = (name, material);
                        if (!groupKeys.TryGetValue(key, out currentGroup))
                        {
                            currentGroup = m.Groups.Count;
                            groupKeys[key] = currentGroup;
                            m.Groups.Add(new MeshGroup { Name = name, Material = material });
                        }

                        groupDirty = false;
                    }

                    bool hasNormals = poly.Any(p => p.N >= 0);
                    if (hasNormals && m.Normals == null)
                    {
                        m.Normals = new List<Vec3>(m.Indices.Count + 64);
                        for (int i = 0; i < m.Indices.Count; i++) m.Normals.Add(Vec3.Zero);
                    }

                    for (int i = 1; i + 1 < poly.Count; i++)
                    {
                        m.AddTriangle(poly[0].V, poly[i].V, poly[i + 1].V);
                        triGroups.Add(currentGroup);
                        if (m.Normals != null)
                        {
                            m.Normals.Add(poly[0].N >= 0 ? normals[poly[0].N] : Vec3.Zero);
                            m.Normals.Add(poly[i].N >= 0 ? normals[poly[i].N] : Vec3.Zero);
                            m.Normals.Add(poly[i + 1].N >= 0 ? normals[poly[i + 1].N] : Vec3.Zero);
                        }
                    }
                }
                else if (kw.Length == 1 && kw[0] == 'o')
                {
                    objName = Encoding.UTF8.GetString(tk.Rest().ToArray()).Trim();
                    groupName = string.Empty;
                    groupDirty = true;
                }
                else if (kw.Length == 1 && kw[0] == 'g')
                {
                    groupName = Encoding.UTF8.GetString(tk.Rest().ToArray()).Trim();
                    groupDirty = true;
                }
                else if (AsciiEq(kw, "usemtl"u8))
                {
                    material = Encoding.UTF8.GetString(tk.Rest().ToArray()).Trim();
                    groupDirty = true;
                }
                else if (AsciiEq(kw, "mtllib"u8))
                {
                    libs.Add(Encoding.UTF8.GetString(tk.Rest().ToArray()).Trim());
                }
            }

            m.VertexColors = vcolors;
            bool trivialGroups = m.Groups.Count == 1 && m.Groups[0].Name.Length == 0 && m.Groups[0].Material == null;
            if (m.Groups.Count > 0 && !trivialGroups) m.TriangleGroups = triGroups;
            else m.Groups.Clear();
            if (m.Groups.Count == 1 && string.IsNullOrEmpty(m.Name)) m.Name = m.Groups[0].Name;

            if (o.ReadMaterials && resolver != null && libs.Count > 0)
            {
                var colors = new Dictionary<string, MeshColor>(StringComparer.Ordinal);
                foreach (var lib in libs)
                {
                    Stream? s = null;
                    try
                    {
                        s = resolver(lib);
                    }
                    catch (IOException)
                    {
                    }

                    if (s == null) continue;
                    using (s) ReadMtl(s, colors);
                }

                foreach (var g in m.Groups)
                {
                    if (g.Material != null && colors.TryGetValue(g.Material, out var c)) g.Color = c;
                }
            }

            m.Validate();
            return m;
        }

        private static void ReadMtl(Stream s, Dictionary<string, MeshColor> colors)
        {
            var r = new ByteReader(s);
            string? current = null;
            double kr = 0.8, kg = 0.8, kb = 0.8, alpha = 1;
            void Flush()
            {
                if (current != null) colors[current] = MeshColor.FromFloat(kr, kg, kb, alpha);
            }

            int lineNo = 0;
            while (r.ReadLine(out int start, out int len))
            {
                lineNo++;
                var tk = new Tokenizer(r.Slice(start, len));
                if (!tk.Next(out var kw)) continue;
                if (AsciiEq(kw, "newmtl"u8))
                {
                    Flush();
                    current = Encoding.UTF8.GetString(tk.Rest().ToArray()).Trim();
                    kr = kg = kb = 0.8;
                    alpha = 1;
                }
                else if (AsciiEq(kw, "Kd"u8))
                {
                    if (!tk.Next(out var t0)) continue;
                    if (AsciiEq(t0, "spectral"u8) || AsciiEq(t0, "xyz"u8)) continue;
                    kr = ParseDouble(t0, lineNo);
                    kg = tk.Next(out var t1) ? ParseDouble(t1, lineNo) : kr;
                    kb = tk.Next(out var t2) ? ParseDouble(t2, lineNo) : kr;
                }
                else if (kw.Length == 1 && kw[0] == 'd')
                {
                    alpha = Num(ref tk, lineNo);
                }
                else if (AsciiEq(kw, "Tr"u8))
                {
                    alpha = 1 - Num(ref tk, lineNo);
                }
            }

            Flush();
        }

        private static int ResolveIndex(long raw, int count, int lineNo, string what)
        {
            long idx = raw < 0 ? count + raw : raw - 1;
            if (raw == 0 || idx < 0 || idx >= count) throw new FormatException($"OBJ line {lineNo}: {what} index {raw} out of range (have {count}).");
            return (int)idx;
        }

        // ------------------------------------------------------------------ PLY

        private enum PlyType
        {
            Int8,
            UInt8,
            Int16,
            UInt16,
            Int32,
            UInt32,
            Float32,
            Float64
        }

        private sealed class PlyProperty
        {
            public string Name = string.Empty;
            public PlyType Type;
            public bool IsList;
            public PlyType CountType;
        }

        private sealed class PlyElement
        {
            public string Name = string.Empty;
            public long Count;
            public List<PlyProperty> Properties = new List<PlyProperty>();
        }

        private static PlyType ParsePlyType(string s)
        {
            switch (s)
            {
                case "char":
                case "int8": return PlyType.Int8;
                case "uchar":
                case "uint8": return PlyType.UInt8;
                case "short":
                case "int16": return PlyType.Int16;
                case "ushort":
                case "uint16": return PlyType.UInt16;
                case "int":
                case "int32": return PlyType.Int32;
                case "uint":
                case "uint32": return PlyType.UInt32;
                case "float":
                case "float32": return PlyType.Float32;
                case "double":
                case "float64": return PlyType.Float64;
                default: throw new FormatException($"PLY: unknown property type '{s}'.");
            }
        }

        private static int PlySize(PlyType t) =>
            t == PlyType.Int8 || t == PlyType.UInt8 ? 1 : t == PlyType.Int16 || t == PlyType.UInt16 ? 2 : t == PlyType.Float64 ? 8 : 4;

        private static MeshModel ReadPly(ByteReader r, MeshLoadOptions o)
        {
            if (!r.ReadLine(out int s0, out int l0) || Encoding.ASCII.GetString(r.Slice(s0, l0).ToArray()).Trim() != "ply")
                throw new FormatException("Not a PLY file.");
            int format = -1; // 0 ascii, 1 LE, 2 BE
            var elements = new List<PlyElement>();
            while (true)
            {
                if (!r.ReadLine(out int s, out int l)) throw new FormatException("PLY: unexpected end of header.");
                var parts = Encoding.ASCII.GetString(r.Slice(s, l).ToArray()).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (parts[0] == "end_header") break;
                switch (parts[0])
                {
                    case "format":
                        format = parts[1] == "ascii" ? 0 : parts[1] == "binary_little_endian" ? 1 : parts[1] == "binary_big_endian" ? 2 : -1;
                        if (format < 0) throw new FormatException($"PLY: unsupported format '{parts[1]}'.");
                        break;
                    case "element":
                        elements.Add(new PlyElement { Name = parts[1], Count = long.Parse(parts[2], CultureInfo.InvariantCulture) });
                        break;
                    case "property":
                        if (elements.Count == 0) throw new FormatException("PLY: property before element.");
                        var prop = new PlyProperty();
                        if (parts[1] == "list")
                        {
                            prop.IsList = true;
                            prop.CountType = ParsePlyType(parts[2]);
                            prop.Type = ParsePlyType(parts[3]);
                            prop.Name = parts[4];
                        }
                        else
                        {
                            prop.Type = ParsePlyType(parts[1]);
                            prop.Name = parts[2];
                        }

                        elements[elements.Count - 1].Properties.Add(prop);
                        break;
                }
            }

            if (format < 0) throw new FormatException("PLY: missing format line.");
            var m = new MeshModel();
            double f = o.Factor(MeshFileFormat.Ply);
            var src = new PlyValueSource(r, format);
            var values = new double[64];
            var list = new List<long>(16);

            foreach (var el in elements)
            {
                bool isVertex = el.Name == "vertex", isFace = el.Name == "face";
                var props = el.Properties;
                int ix = props.FindIndex(p => p.Name == "x"), iy = props.FindIndex(p => p.Name == "y"), iz = props.FindIndex(p => p.Name == "z");
                int inx = props.FindIndex(p => p.Name == "nx"), iny = props.FindIndex(p => p.Name == "ny"), inz = props.FindIndex(p => p.Name == "nz");
                int ir = props.FindIndex(p => p.Name == "red" || p.Name == "r" || p.Name == "diffuse_red");
                int ig = props.FindIndex(p => p.Name == "green" || p.Name == "g" || p.Name == "diffuse_green");
                int ib = props.FindIndex(p => p.Name == "blue" || p.Name == "b" || p.Name == "diffuse_blue");
                int ia = props.FindIndex(p => p.Name == "alpha" || p.Name == "a" || p.Name == "diffuse_alpha");
                int il = props.FindIndex(p => p.IsList && (p.Name == "vertex_indices" || p.Name == "vertex_index"));
                if (props.Count > values.Length) values = new double[props.Count];
                bool hasColor = o.ReadColors && ir >= 0 && ig >= 0 && ib >= 0;
                bool hasNormal = o.ReadNormals && isVertex && inx >= 0 && iny >= 0 && inz >= 0;
                List<Vec3>? vnormals = hasNormal ? new List<Vec3>() : null;
                if (isVertex)
                {
                    if (ix < 0 || iy < 0 || iz < 0) throw new FormatException("PLY: vertex element without x/y/z.");
                    m.Vertices.Capacity = (int)Math.Min(el.Count, int.MaxValue / 2);
                    if (hasColor) m.VertexColors = new List<MeshColor>(m.Vertices.Capacity);
                }

                if (isFace && hasColor) m.TriangleColors = new List<MeshColor>();

                for (long item = 0; item < el.Count; item++)
                {
                    for (int p = 0; p < props.Count; p++)
                    {
                        var prop = props[p];
                        if (prop.IsList)
                        {
                            long n = (long)src.Read(prop.CountType);
                            if (p == il && isFace)
                            {
                                list.Clear();
                                for (long k = 0; k < n; k++) list.Add((long)src.Read(prop.Type));
                            }
                            else
                            {
                                for (long k = 0; k < n; k++) src.Read(prop.Type);
                            }
                        }
                        else values[p] = src.Read(prop.Type);
                    }

                    src.EndItem();
                    if (isVertex)
                    {
                        m.Vertices.Add(FromFile(values[ix], values[iy], values[iz], f, o.UpAxis));
                        vnormals?.Add(DirFromFile(values[inx], values[iny], values[inz], o.UpAxis));
                        if (hasColor) m.VertexColors!.Add(PlyColor(props, values, ir, ig, ib, ia));
                    }
                    else if (isFace && il >= 0)
                    {
                        var c = hasColor ? PlyColor(props, values, ir, ig, ib, ia) : default;
                        for (int k = 1; k + 1 < list.Count; k++)
                        {
                            int a = (int)list[0], b = (int)list[k], cc = (int)list[k + 1];
                            if ((uint)a >= (uint)m.Vertices.Count || (uint)b >= (uint)m.Vertices.Count || (uint)cc >= (uint)m.Vertices.Count)
                                throw new FormatException($"PLY: face {item} references a missing vertex.");
                            m.AddTriangle(a, b, cc);
                            if (hasColor) m.TriangleColors!.Add(c);
                        }
                    }
                }

                if (vnormals != null && vnormals.Count > 0)
                {
                    // Per-vertex normals become per-corner normals once faces are known (see below).
                    m._pendingVertexNormals = vnormals;
                }
            }

            if (m._pendingVertexNormals != null)
            {
                var vn = m._pendingVertexNormals;
                m.Normals = new List<Vec3>(m.Indices.Count);
                foreach (int i in m.Indices) m.Normals.Add(vn[i]);
                m._pendingVertexNormals = null;
            }

            m.Validate();
            return m;
        }

        private List<Vec3>? _pendingVertexNormals;

        private static MeshColor PlyColor(List<PlyProperty> props, double[] v, int ir, int ig, int ib, int ia)
        {
            bool isFloat = props[ir].Type == PlyType.Float32 || props[ir].Type == PlyType.Float64;
            double alpha = ia >= 0 ? v[ia] : isFloat ? 1.0 : 255;
            if (isFloat) return MeshColor.FromFloat(v[ir], v[ig], v[ib], alpha);
            byte B(double x) => (byte)Math.Max(0, Math.Min(255, x));
            return new MeshColor(B(v[ir]), B(v[ig]), B(v[ib]), B(alpha));
        }

        private sealed class PlyValueSource
        {
            private readonly ByteReader _r;
            private readonly int _format;
            private int _lineStart, _lineLen, _cursor = -1, _lineNo;

            public PlyValueSource(ByteReader r, int format)
            {
                _r = r;
                _format = format;
            }

            public double Read(PlyType t)
            {
                if (_format == 0) return ReadAscii();
                int size = PlySize(t);
                if (!_r.Ensure(size)) throw new FormatException("PLY: unexpected end of data.");
                var s = _r.Peek(size);
                bool le = _format == 1;
                double v;
                switch (t)
                {
                    case PlyType.Int8: v = (sbyte)s[0]; break;
                    case PlyType.UInt8: v = s[0]; break;
                    case PlyType.Int16: v = le ? BinaryPrimitives.ReadInt16LittleEndian(s) : BinaryPrimitives.ReadInt16BigEndian(s); break;
                    case PlyType.UInt16: v = le ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s); break;
                    case PlyType.Int32: v = le ? BinaryPrimitives.ReadInt32LittleEndian(s) : BinaryPrimitives.ReadInt32BigEndian(s); break;
                    case PlyType.UInt32: v = le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s); break;
                    case PlyType.Float32:
                        v = BitConverter.Int32BitsToSingle(le ? BinaryPrimitives.ReadInt32LittleEndian(s) : BinaryPrimitives.ReadInt32BigEndian(s));
                        break;
                    default:
                        v = BitConverter.Int64BitsToDouble(le ? BinaryPrimitives.ReadInt64LittleEndian(s) : BinaryPrimitives.ReadInt64BigEndian(s));
                        break;
                }

                _r.Skip(size);
                return v;
            }

            /// <summary>ASCII items normally sit on one line; values may also continue on the next line.</summary>
            private double ReadAscii()
            {
                while (true)
                {
                    if (_cursor >= 0)
                    {
                        var line = _r.Slice(_lineStart, _lineLen);
                        int i = _cursor;
                        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
                        if (i < line.Length)
                        {
                            int j = i;
                            while (j < line.Length && line[j] != ' ' && line[j] != '\t') j++;
                            _cursor = j;
                            return ParseDouble(line.Slice(i, j - i), _lineNo);
                        }
                    }

                    if (!_r.ReadLine(out _lineStart, out _lineLen)) throw new FormatException("PLY: unexpected end of data.");
                    _lineNo++;
                    _cursor = 0;
                }
            }

            public void EndItem()
            {
                if (_format == 0) _cursor = -1;
            }
        }

        // ================================================================== saving

        /// <summary>Saves as STL, OBJ (+ .mtl when there are colours) or PLY, chosen by extension.</summary>
        public void Save(string path, MeshSaveOptions? options = null)
        {
            options ??= new MeshSaveOptions();
            var format = FormatFromExtension(path);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            switch (format)
            {
                case MeshFileFormat.Obj:
                {
                    string mtlName = Path.GetFileNameWithoutExtension(path) + ".mtl";
                    bool needMtl = options.WriteColors && HasAnyColor();
                    if (needMtl)
                    {
                        using var mtl = new FileStream(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", mtlName), FileMode.Create);
                        SaveObj(fs, options, mtl, mtlName);
                    }
                    else SaveObj(fs, options);

                    break;
                }

                case MeshFileFormat.Ply:
                    SavePly(fs, options);
                    break;
                default:
                    SaveStl(fs, options);
                    break;
            }
        }

        private bool HasAnyColor() => TriangleColors != null || VertexColors != null || Groups.Any(g => g.Color.HasValue);

        // Multiplying by the inverse keeps round numbers round (1/0.001 == 1000 exactly).
        private Vec3 ToFile(Vec3 p, double f, MeshUpAxis up) => (up == MeshUpAxis.Y ? ZUpToYUp(p) : p) * (1.0 / f);

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string F(double v) => v.ToString("R", Inv);

        /// <summary>Binary STL (VisCAM/SolidView 15-bit face colours when the model has colours) or ASCII STL.</summary>
        public void SaveStl(Stream stream, MeshSaveOptions? options = null)
        {
            options ??= new MeshSaveOptions();
            double f = options.Factor(MeshFileFormat.Stl);
            var up = options.UpAxis;
            if (!options.Binary)
            {
                using var w = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16, leaveOpen: true) { NewLine = "\n" };
                string name = string.IsNullOrWhiteSpace(Name) ? "model" : Name.Replace('\n', ' ');
                w.WriteLine("solid " + name);
                for (int t = 0; t < TriangleCount; t++)
                {
                    var (a, b, c) = Triangle(t);
                    a = ToFile(a, f, up);
                    b = ToFile(b, f, up);
                    c = ToFile(c, f, up);
                    var n = (b - a).Cross(c - a).Normalized();
                    w.WriteLine($"  facet normal {F(n.X)} {F(n.Y)} {F(n.Z)}");
                    w.WriteLine("    outer loop");
                    w.WriteLine($"      vertex {F(a.X)} {F(a.Y)} {F(a.Z)}");
                    w.WriteLine($"      vertex {F(b.X)} {F(b.Y)} {F(b.Z)}");
                    w.WriteLine($"      vertex {F(c.X)} {F(c.Y)} {F(c.Z)}");
                    w.WriteLine("    endloop");
                    w.WriteLine("  endfacet");
                }

                w.WriteLine("endsolid " + name);
                return;
            }

            var header = new byte[84];
            var text = Encoding.ASCII.GetBytes("RobotWin binary STL " + (Name ?? string.Empty));
            Array.Copy(text, header, Math.Min(80, text.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80), (uint)TriangleCount);
            stream.Write(header, 0, header.Length);
            bool colors = options.WriteColors && HasAnyColor();
            const int chunk = 4096;
            var buf = new byte[50 * chunk];
            int used = 0;
            for (int t = 0; t < TriangleCount; t++)
            {
                var (a, b, c) = Triangle(t);
                a = ToFile(a, f, up);
                b = ToFile(b, f, up);
                c = ToFile(c, f, up);
                var n = (b - a).Cross(c - a).Normalized();
                var s = buf.AsSpan(used, 50);
                WriteF(s, 0, n);
                WriteF(s, 12, a);
                WriteF(s, 24, b);
                WriteF(s, 36, c);
                ushort attr = 0;
                if (colors)
                {
                    var col = ColorOf(t);
                    attr = (ushort)(0x8000 | (col.R * 31 / 255) << 10 | (col.G * 31 / 255) << 5 | (col.B * 31 / 255));
                }

                BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(48), attr);
                used += 50;
                if (used == buf.Length)
                {
                    stream.Write(buf, 0, used);
                    used = 0;
                }
            }

            if (used > 0) stream.Write(buf, 0, used);
        }

        private static void WriteF(Span<byte> s, int offset, Vec3 v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(s.Slice(offset), BitConverter.SingleToInt32Bits((float)v.X));
            BinaryPrimitives.WriteInt32LittleEndian(s.Slice(offset + 4), BitConverter.SingleToInt32Bits((float)v.Y));
            BinaryPrimitives.WriteInt32LittleEndian(s.Slice(offset + 8), BitConverter.SingleToInt32Bits((float)v.Z));
        }

        /// <summary>
        /// Wavefront OBJ. Groups become <c>g</c>/<c>usemtl</c>; when <paramref name="mtlStream"/> is given,
        /// colours are written as materials (Kd) into it and referenced via <c>mtllib</c>.
        /// </summary>
        public void SaveObj(Stream stream, MeshSaveOptions? options = null, Stream? mtlStream = null, string mtlFileName = "model.mtl")
        {
            options ??= new MeshSaveOptions();
            double f = options.Factor(MeshFileFormat.Obj);
            var up = options.UpAxis;
            using var w = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16, leaveOpen: true) { NewLine = "\n" };
            w.WriteLine("# RobotWin OBJ export");
            bool colors = options.WriteColors && mtlStream != null && HasAnyColor();
            if (colors) w.WriteLine("mtllib " + mtlFileName);
            if (!string.IsNullOrEmpty(Name)) w.WriteLine("o " + Name);
            foreach (var v in Vertices)
            {
                var p = ToFile(v, f, up);
                w.WriteLine($"v {F(p.X)} {F(p.Y)} {F(p.Z)}");
            }

            bool normals = options.WriteNormals && Normals != null;
            if (normals)
            {
                foreach (var n in Normals!)
                {
                    var d = up == MeshUpAxis.Y ? ZUpToYUp(n) : n;
                    w.WriteLine($"vn {F(d.X)} {F(d.Y)} {F(d.Z)}");
                }
            }

            var materials = new Dictionary<string, MeshColor>();
            int lastGroup = int.MinValue;
            string? lastMat = null;
            for (int t = 0; t < TriangleCount; t++)
            {
                int g = TriangleGroups != null ? TriangleGroups[t] : -1;
                if (g != lastGroup)
                {
                    if (g >= 0 && g < Groups.Count) w.WriteLine("g " + (Groups[g].Name.Length > 0 ? Groups[g].Name : "group" + g));
                    lastGroup = g;
                }

                if (colors)
                {
                    string mat;
                    if (TriangleColors == null && VertexColors == null && g >= 0 && g < Groups.Count && Groups[g].Material != null)
                        mat = Groups[g].Material!;
                    else mat = "c_" + ColorOf(t).ToHex().TrimStart('#');
                    if (!materials.ContainsKey(mat)) materials[mat] = ColorOf(t);
                    if (mat != lastMat)
                    {
                        w.WriteLine("usemtl " + mat);
                        lastMat = mat;
                    }
                }
                else if (g >= 0 && g < Groups.Count && Groups[g].Material != null && Groups[g].Material != lastMat)
                {
                    lastMat = Groups[g].Material;
                    w.WriteLine("usemtl " + lastMat);
                }

                int a = Indices[3 * t] + 1, b = Indices[3 * t + 1] + 1, c = Indices[3 * t + 2] + 1;
                if (normals) w.WriteLine($"f {a}//{3 * t + 1} {b}//{3 * t + 2} {c}//{3 * t + 3}");
                else w.WriteLine($"f {a} {b} {c}");
            }

            if (colors)
            {
                using var mw = new StreamWriter(mtlStream!, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n" };
                foreach (var kv in materials)
                {
                    mw.WriteLine("newmtl " + kv.Key);
                    mw.WriteLine($"Kd {F(kv.Value.R / 255.0)} {F(kv.Value.G / 255.0)} {F(kv.Value.B / 255.0)}");
                    if (kv.Value.A != 255) mw.WriteLine($"d {F(kv.Value.A / 255.0)}");
                    mw.WriteLine();
                }
            }
        }

        /// <summary>PLY (binary_little_endian by default, or ASCII) with vertex/face colours when present.</summary>
        public void SavePly(Stream stream, MeshSaveOptions? options = null)
        {
            options ??= new MeshSaveOptions();
            double f = options.Factor(MeshFileFormat.Ply);
            var up = options.UpAxis;
            bool vColors = options.WriteColors && VertexColors != null;
            bool fColors = options.WriteColors && !vColors && HasAnyColor();
            var sb = new StringBuilder();
            sb.Append("ply\n");
            sb.Append(options.Binary ? "format binary_little_endian 1.0\n" : "format ascii 1.0\n");
            sb.Append("comment RobotWin export\n");
            sb.Append($"element vertex {Vertices.Count}\nproperty float x\nproperty float y\nproperty float z\n");
            if (vColors) sb.Append("property uchar red\nproperty uchar green\nproperty uchar blue\nproperty uchar alpha\n");
            sb.Append($"element face {TriangleCount}\nproperty list uchar int vertex_indices\n");
            if (fColors) sb.Append("property uchar red\nproperty uchar green\nproperty uchar blue\nproperty uchar alpha\n");
            sb.Append("end_header\n");
            var head = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);

            if (!options.Binary)
            {
                using var w = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16, leaveOpen: true) { NewLine = "\n" };
                for (int i = 0; i < Vertices.Count; i++)
                {
                    var p = ToFile(Vertices[i], f, up);
                    w.Write($"{F((float)p.X)} {F((float)p.Y)} {F((float)p.Z)}");
                    if (vColors) w.Write($" {VertexColors![i].R} {VertexColors[i].G} {VertexColors[i].B} {VertexColors[i].A}");
                    w.WriteLine();
                }

                for (int t = 0; t < TriangleCount; t++)
                {
                    w.Write($"3 {Indices[3 * t]} {Indices[3 * t + 1]} {Indices[3 * t + 2]}");
                    if (fColors)
                    {
                        var c = ColorOf(t);
                        w.Write($" {c.R} {c.G} {c.B} {c.A}");
                    }

                    w.WriteLine();
                }

                return;
            }

            using var bw = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Binary PLY export needs a little-endian host.");
            for (int i = 0; i < Vertices.Count; i++)
            {
                var p = ToFile(Vertices[i], f, up);
                bw.Write((float)p.X);
                bw.Write((float)p.Y);
                bw.Write((float)p.Z);
                if (vColors)
                {
                    var c = VertexColors![i];
                    bw.Write(c.R);
                    bw.Write(c.G);
                    bw.Write(c.B);
                    bw.Write(c.A);
                }
            }

            for (int t = 0; t < TriangleCount; t++)
            {
                bw.Write((byte)3);
                bw.Write(Indices[3 * t]);
                bw.Write(Indices[3 * t + 1]);
                bw.Write(Indices[3 * t + 2]);
                if (fColors)
                {
                    var c = ColorOf(t);
                    bw.Write(c.R);
                    bw.Write(c.G);
                    bw.Write(c.B);
                    bw.Write(c.A);
                }
            }
        }

        // ================================================================== parsing helpers

        private static bool AsciiEq(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                int x = a[i], y = b[i];
                if (x >= 'A' && x <= 'Z') x += 32;
                if (y >= 'A' && y <= 'Z') y += 32;
                if (x != y) return false;
            }

            return true;
        }

        private static double Num(ref Tokenizer tk, int lineNo)
        {
            if (!tk.Next(out var t)) throw new FormatException($"Line {lineNo}: number expected.");
            return ParseDouble(t, lineNo);
        }

        internal static double ParseDouble(ReadOnlySpan<byte> tok, int lineNo)
        {
            // Fast path for plain decimals ("-12.345", "1e-3" falls through to the BCL parser).
            Span<char> chars = tok.Length <= 128 ? stackalloc char[tok.Length] : new char[tok.Length];
            for (int i = 0; i < tok.Length; i++) chars[i] = (char)tok[i];
            if (double.TryParse(chars, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return v;
            throw new FormatException($"Line {lineNo}: invalid number '{new string(chars.ToArray())}'.");
        }

        private static long ParseInt(ReadOnlySpan<byte> tok, int lineNo)
        {
            if (tok.Length == 0) throw new FormatException($"Line {lineNo}: index expected.");
            int i = 0;
            bool neg = false;
            if (tok[0] == '-' || tok[0] == '+')
            {
                neg = tok[0] == '-';
                i = 1;
            }

            long v = 0;
            if (i >= tok.Length) throw new FormatException($"Line {lineNo}: invalid index.");
            for (; i < tok.Length; i++)
            {
                int d = tok[i] - '0';
                if (d < 0 || d > 9) throw new FormatException($"Line {lineNo}: invalid index '{Encoding.ASCII.GetString(tok.ToArray())}'.");
                v = v * 10 + d;
            }

            return neg ? -v : v;
        }

        /// <summary>Whitespace tokenizer over one line of bytes.</summary>
        private ref struct Tokenizer
        {
            private readonly ReadOnlySpan<byte> _s;
            private int _pos;

            public Tokenizer(ReadOnlySpan<byte> s)
            {
                _s = s;
                _pos = 0;
            }

            public bool Next(out ReadOnlySpan<byte> token)
            {
                while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t')) _pos++;
                if (_pos >= _s.Length)
                {
                    token = default;
                    return false;
                }

                int start = _pos;
                while (_pos < _s.Length && _s[_pos] != ' ' && _s[_pos] != '\t') _pos++;
                token = _s.Slice(start, _pos - start);
                return true;
            }

            public ReadOnlySpan<byte> Rest()
            {
                while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t')) _pos++;
                return _s.Slice(_pos);
            }
        }

        /// <summary>Exact float-bit key used to weld STL vertices.</summary>
        private readonly struct FloatKey : IEquatable<FloatKey>
        {
            private readonly int _x, _y, _z;

            public FloatKey(int x, int y, int z)
            {
                // −0 and +0 are the same point.
                _x = x == int.MinValue ? 0 : x;
                _y = y == int.MinValue ? 0 : y;
                _z = z == int.MinValue ? 0 : z;
            }

            public bool Equals(FloatKey o) => _x == o._x && _y == o._y && _z == o._z;
            public override bool Equals(object? obj) => obj is FloatKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    uint h = (uint)_x * 0x9E3779B1u;
                    h = (h ^ (h >> 15)) + (uint)_y * 0x85EBCA77u;
                    h = (h ^ (h >> 13)) + (uint)_z * 0xC2B2AE3Du;
                    return (int)(h ^ (h >> 16));
                }
            }
        }
    }

    /// <summary>Buffered forward-only reader with line and fixed-size record access (no per-record allocations).</summary>
    internal sealed class ByteReader
    {
        private readonly Stream _s;
        private byte[] _buf;
        private int _pos, _len;
        private bool _eof;

        public ByteReader(Stream s, int size = 1 << 16)
        {
            _s = s;
            _buf = new byte[size];
        }

        public int Available => _len - _pos;

        /// <summary>Makes at least <paramref name="n"/> bytes available; false at end of stream.</summary>
        public bool Ensure(int n)
        {
            if (_len - _pos >= n) return true;
            if (_pos > 0)
            {
                Buffer.BlockCopy(_buf, _pos, _buf, 0, _len - _pos);
                _len -= _pos;
                _pos = 0;
            }

            if (n > _buf.Length) Array.Resize(ref _buf, Math.Max(n, _buf.Length * 2));
            while (_len < n && !_eof)
            {
                int read = _s.Read(_buf, _len, _buf.Length - _len);
                if (read <= 0) _eof = true;
                else _len += read;
            }

            return _len - _pos >= n;
        }

        public ReadOnlySpan<byte> Peek(int n) => new ReadOnlySpan<byte>(_buf, _pos, n);
        public ReadOnlySpan<byte> Slice(int start, int len) => new ReadOnlySpan<byte>(_buf, start, len);
        public void Skip(int n) => _pos += n;

        /// <summary>Next line (without CR/LF) as a range of the internal buffer, valid until the next read.</summary>
        public bool ReadLine(out int start, out int length)
        {
            int scanFrom = _pos;
            while (true)
            {
                int nl = Array.IndexOf(_buf, (byte)'\n', scanFrom, _len - scanFrom);
                if (nl >= 0)
                {
                    start = _pos;
                    length = nl - _pos;
                    if (length > 0 && _buf[nl - 1] == '\r') length--;
                    _pos = nl + 1;
                    return true;
                }

                if (_eof)
                {
                    if (_pos >= _len)
                    {
                        start = length = 0;
                        return false;
                    }

                    start = _pos;
                    length = _len - _pos;
                    if (length > 0 && _buf[start + length - 1] == '\r') length--;
                    _pos = _len;
                    return true;
                }

                int have = _len - _pos;
                Ensure(have + 1 + (have >= _buf.Length - 1 ? _buf.Length : 0));
                scanFrom = _pos + have;
            }
        }
    }
}
