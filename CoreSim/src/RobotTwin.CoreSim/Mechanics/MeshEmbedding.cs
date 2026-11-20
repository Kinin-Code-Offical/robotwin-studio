using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// Compact JSON-friendly form of a <see cref="MeshModel"/> for storing inside project files:
    /// typed little-endian arrays, deflate-compressed and base64-encoded. Positions are float32
    /// metres (Z up), indices uint16 or uint32. Plain POCO, so it can be a property of any
    /// document serialised with System.Text.Json.
    /// </summary>
    public sealed class EmbeddedMesh
    {
        public const string FormatId = "robotwin-mesh";

        public string Format { get; set; } = FormatId;
        public int Version { get; set; } = 1;
        public string? Name { get; set; }
        public int VertexCount { get; set; }
        public int TriangleCount { get; set; }

        /// <summary>16 or 32.</summary>
        public int IndexBits { get; set; } = 32;

        /// <summary>"deflate" (default) or "none".</summary>
        public string Compression { get; set; } = "deflate";

        /// <summary>float32 x, y, z per vertex.</summary>
        public string Positions { get; set; } = string.Empty;

        /// <summary>Three uint16/uint32 per triangle.</summary>
        public string Indices { get; set; } = string.Empty;

        /// <summary>Optional float32 x, y, z per triangle corner.</summary>
        public string? Normals { get; set; }

        /// <summary>Optional RGBA bytes per vertex.</summary>
        public string? VertexColors { get; set; }

        /// <summary>Optional RGBA bytes per triangle.</summary>
        public string? TriangleColors { get; set; }

        /// <summary>Optional int32 group index per triangle (−1 = none).</summary>
        public string? TriangleGroups { get; set; }

        public List<EmbeddedMeshGroup>? Groups { get; set; }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        public static EmbeddedMesh FromJson(string json) =>
            JsonSerializer.Deserialize<EmbeddedMesh>(json, Json) ?? throw new FormatException("Empty embedded mesh.");
    }

    public sealed class EmbeddedMeshGroup
    {
        public string Name { get; set; } = string.Empty;
        public string? Material { get; set; }

        /// <summary>"#RRGGBB" / "#RRGGBBAA" or null.</summary>
        public string? Color { get; set; }
    }

    public sealed partial class MeshModel
    {
        /// <summary>Packs the model into the compact embed format.</summary>
        public EmbeddedMesh ToEmbedded(bool includeNormals = false, bool compress = true)
        {
            Validate();
            var e = new EmbeddedMesh
            {
                Name = string.IsNullOrEmpty(Name) ? null : Name,
                VertexCount = Vertices.Count,
                TriangleCount = TriangleCount,
                IndexBits = Vertices.Count <= 65536 ? 16 : 32,
                Compression = compress ? "deflate" : "none"
            };

            var pos = new byte[Vertices.Count * 12];
            for (int i = 0; i < Vertices.Count; i++) PutVec(pos, i * 12, Vertices[i]);
            e.Positions = Encode(pos, compress);

            int ib = e.IndexBits / 8;
            var idx = new byte[Indices.Count * ib];
            for (int i = 0; i < Indices.Count; i++)
            {
                if (ib == 2) BinaryPrimitives.WriteUInt16LittleEndian(idx.AsSpan(i * 2), (ushort)Indices[i]);
                else BinaryPrimitives.WriteInt32LittleEndian(idx.AsSpan(i * 4), Indices[i]);
            }

            e.Indices = Encode(idx, compress);

            if (includeNormals && Normals != null)
            {
                var n = new byte[Normals.Count * 12];
                for (int i = 0; i < Normals.Count; i++) PutVec(n, i * 12, Normals[i]);
                e.Normals = Encode(n, compress);
            }

            if (VertexColors != null) e.VertexColors = Encode(ColorBytes(VertexColors), compress);
            if (TriangleColors != null) e.TriangleColors = Encode(ColorBytes(TriangleColors), compress);
            if (TriangleGroups != null)
            {
                var g = new byte[TriangleGroups.Count * 4];
                for (int i = 0; i < TriangleGroups.Count; i++) BinaryPrimitives.WriteInt32LittleEndian(g.AsSpan(i * 4), TriangleGroups[i]);
                e.TriangleGroups = Encode(g, compress);
            }

            if (Groups.Count > 0)
            {
                e.Groups = Groups.Select(g => new EmbeddedMeshGroup { Name = g.Name, Material = g.Material, Color = g.Color?.ToHex() }).ToList();
            }

            return e;
        }

        public string ToEmbeddedJson(bool includeNormals = false) => ToEmbedded(includeNormals).ToJson();

        public static MeshModel FromEmbedded(EmbeddedMesh e)
        {
            if (e.Format != EmbeddedMesh.FormatId) throw new FormatException($"Not an embedded mesh ('{e.Format}').");
            if (e.Version > 1) throw new FormatException($"Embedded mesh version {e.Version} is newer than supported.");
            bool compressed = e.Compression == "deflate";
            var m = new MeshModel { Name = e.Name ?? string.Empty };

            var pos = Decode(e.Positions, compressed, e.VertexCount * 12L);
            m.Vertices.Capacity = e.VertexCount;
            for (int i = 0; i < e.VertexCount; i++) m.Vertices.Add(GetVec(pos, i * 12));

            int ib = e.IndexBits / 8;
            if (ib != 2 && ib != 4) throw new FormatException("Embedded mesh: IndexBits must be 16 or 32.");
            var idx = Decode(e.Indices, compressed, e.TriangleCount * 3L * ib);
            m.Indices.Capacity = e.TriangleCount * 3;
            for (int i = 0; i < e.TriangleCount * 3; i++)
            {
                m.Indices.Add(ib == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(idx.AsSpan(i * 2)) : BinaryPrimitives.ReadInt32LittleEndian(idx.AsSpan(i * 4)));
            }

            if (e.Normals != null)
            {
                var n = Decode(e.Normals, compressed, e.TriangleCount * 36L);
                m.Normals = new List<Vec3>(e.TriangleCount * 3);
                for (int i = 0; i < e.TriangleCount * 3; i++) m.Normals.Add(GetVec(n, i * 12));
            }

            if (e.VertexColors != null) m.VertexColors = ColorList(Decode(e.VertexColors, compressed, e.VertexCount * 4L));
            if (e.TriangleColors != null) m.TriangleColors = ColorList(Decode(e.TriangleColors, compressed, e.TriangleCount * 4L));
            if (e.TriangleGroups != null)
            {
                var g = Decode(e.TriangleGroups, compressed, e.TriangleCount * 4L);
                m.TriangleGroups = new List<int>(e.TriangleCount);
                for (int i = 0; i < e.TriangleCount; i++) m.TriangleGroups.Add(BinaryPrimitives.ReadInt32LittleEndian(g.AsSpan(i * 4)));
            }

            if (e.Groups != null)
            {
                foreach (var g in e.Groups)
                    m.Groups.Add(new MeshGroup { Name = g.Name, Material = g.Material, Color = g.Color == null ? (MeshColor?)null : MeshColor.FromHex(g.Color) });
            }

            m.Validate();
            return m;
        }

        public static MeshModel FromEmbeddedJson(string json) => FromEmbedded(EmbeddedMesh.FromJson(json));

        private static void PutVec(byte[] b, int o, Vec3 v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), BitConverter.SingleToInt32Bits((float)v.X));
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o + 4), BitConverter.SingleToInt32Bits((float)v.Y));
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o + 8), BitConverter.SingleToInt32Bits((float)v.Z));
        }

        private static Vec3 GetVec(byte[] b, int o) => new Vec3(
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o))),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o + 4))),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o + 8))));

        private static byte[] ColorBytes(List<MeshColor> c)
        {
            var b = new byte[c.Count * 4];
            for (int i = 0; i < c.Count; i++)
            {
                b[4 * i] = c[i].R;
                b[4 * i + 1] = c[i].G;
                b[4 * i + 2] = c[i].B;
                b[4 * i + 3] = c[i].A;
            }

            return b;
        }

        private static List<MeshColor> ColorList(byte[] b)
        {
            var l = new List<MeshColor>(b.Length / 4);
            for (int i = 0; i + 3 < b.Length; i += 4) l.Add(new MeshColor(b[i], b[i + 1], b[i + 2], b[i + 3]));
            return l;
        }

        private static string Encode(byte[] data, bool compress)
        {
            if (!compress) return Convert.ToBase64String(data);
            using var ms = new MemoryStream();
            using (var z = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data, 0, data.Length);
            return Convert.ToBase64String(ms.ToArray());
        }

        private static byte[] Decode(string text, bool compressed, long expected)
        {
            var raw = Convert.FromBase64String(text);
            byte[] data;
            if (!compressed) data = raw;
            else
            {
                using var z = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress);
                using var ms = new MemoryStream((int)Math.Min(expected, int.MaxValue));
                z.CopyTo(ms);
                data = ms.ToArray();
            }

            if (data.Length != expected) throw new FormatException($"Embedded mesh: array has {data.Length} bytes, expected {expected}.");
            return data;
        }
    }
}
