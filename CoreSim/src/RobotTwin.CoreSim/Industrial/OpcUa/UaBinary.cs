using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RobotTwin.CoreSim.Industrial.OpcUa
{
    /// <summary>[opcua] A node's identifier (OPC UA Part 3 §8.2): a namespace index and a numeric or string identifier.</summary>
    public readonly struct NodeId : IEquatable<NodeId>
    {
        public NodeId(ushort ns, uint id)
        {
            Namespace = ns;
            Numeric = id;
            Text = null;
        }

        public NodeId(ushort ns, string text)
        {
            Namespace = ns;
            Numeric = 0;
            Text = text;
        }

        public ushort Namespace { get; }
        public uint Numeric { get; }

        /// <summary>The string identifier, or null for a numeric one.</summary>
        public string? Text { get; }

        public bool IsNull => Namespace == 0 && Text == null && Numeric == 0;

        public static NodeId Null => default;

        public static NodeId Ns0(uint id) => new NodeId(0, id);

        public bool Equals(NodeId other) => Namespace == other.Namespace && Numeric == other.Numeric && string.Equals(Text, other.Text, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is NodeId n && Equals(n);
        public override int GetHashCode() => unchecked(Namespace * 397 ^ (Text?.GetHashCode() ?? (int)Numeric));
        public static bool operator ==(NodeId a, NodeId b) => a.Equals(b);
        public static bool operator !=(NodeId a, NodeId b) => !a.Equals(b);
        public override string ToString() => (Namespace == 0 ? string.Empty : $"ns={Namespace};") + (Text != null ? "s=" + Text : "i=" + Numeric);
    }

    /// <summary>[opcua] Built-in type ids of a Variant (Part 6 §5.1.2).</summary>
    public enum UaType : byte
    {
        Null = 0,
        Boolean = 1,
        SByte = 2,
        Byte = 3,
        Int16 = 4,
        UInt16 = 5,
        Int32 = 6,
        UInt32 = 7,
        Int64 = 8,
        UInt64 = 9,
        Float = 10,
        Double = 11,
        String = 12,
        DateTime = 13,
        Guid = 14,
        ByteString = 15,
        XmlElement = 16,
        NodeId = 17,
        ExpandedNodeId = 18,
        StatusCode = 19,
        QualifiedName = 20,
        LocalizedText = 21,
        ExtensionObject = 22,
        DataValue = 23,
        Variant = 24,
        DiagnosticInfo = 25
    }

    /// <summary>[opcua] A Variant's value: its built-in type and the value itself (arrays as object[] of the element type).</summary>
    public readonly struct Variant
    {
        public Variant(UaType type, object? value)
        {
            Type = type;
            Value = value;
        }

        public UaType Type { get; }
        public object? Value { get; }

        public static Variant Null => default;

        public override string ToString() => $"{Type}:{Value}";
    }

    public sealed class QualifiedName
    {
        public QualifiedName(ushort ns, string name)
        {
            Namespace = ns;
            Name = name;
        }

        public ushort Namespace { get; }
        public string Name { get; }
        public override string ToString() => Namespace == 0 ? Name : $"{Namespace}:{Name}";
    }

    public sealed class LocalizedText
    {
        public LocalizedText(string text, string? locale = null)
        {
            Text = text;
            Locale = locale;
        }

        public string? Locale { get; }
        public string? Text { get; }
        public override string ToString() => Text ?? string.Empty;
    }

    /// <summary>[opcua] A value with its status and timestamps (Part 4 §7.11).</summary>
    public sealed class DataValue
    {
        public Variant Value { get; set; }
        public uint Status { get; set; }
        public DateTime? SourceTimestamp { get; set; }
        public DateTime? ServerTimestamp { get; set; }
        public bool HasValue { get; set; } = true;
    }

    /// <summary>[opcua] A structure carried with its type's binary encoding id (Part 6 §5.2.2.15); the body is undecoded bytes when unknown.</summary>
    public sealed class ExtensionObject
    {
        public NodeId TypeId { get; set; }
        public byte[]? Body { get; set; }
    }

    /// <summary>[opcua] Little-endian writer of OPC UA's binary encoding (Part 6 §5.2).</summary>
    public sealed class UaWriter
    {
        private readonly MemoryStream _s = new MemoryStream();

        public int Length => (int)_s.Length;

        public byte[] ToArray() => _s.ToArray();

        public void Byte(byte v) => _s.WriteByte(v);
        public void Boolean(bool v) => _s.WriteByte(v ? (byte)1 : (byte)0);
        public void SByte(sbyte v) => _s.WriteByte(unchecked((byte)v));
        public void Int16(short v) => Raw(BitConverter.GetBytes(v));
        public void UInt16(ushort v) => Raw(BitConverter.GetBytes(v));
        public void Int32(int v) => Raw(BitConverter.GetBytes(v));
        public void UInt32(uint v) => Raw(BitConverter.GetBytes(v));
        public void Int64(long v) => Raw(BitConverter.GetBytes(v));
        public void UInt64(ulong v) => Raw(BitConverter.GetBytes(v));
        public void Float(float v) => Raw(BitConverter.GetBytes(v));
        public void Double(double v) => Raw(BitConverter.GetBytes(v));

        public void Raw(byte[] bytes)
        {
            if (!BitConverter.IsLittleEndian) System.Array.Reverse(bytes);
            _s.Write(bytes, 0, bytes.Length);
        }

        public void Bytes(byte[] bytes) => _s.Write(bytes, 0, bytes.Length);

        public void String(string? v)
        {
            if (v == null)
            {
                Int32(-1);
                return;
            }

            var b = Encoding.UTF8.GetBytes(v);
            Int32(b.Length);
            Bytes(b);
        }

        public void ByteString(byte[]? v)
        {
            if (v == null)
            {
                Int32(-1);
                return;
            }

            Int32(v.Length);
            Bytes(v);
        }

        /// <summary>100 ns ticks since 1601-01-01 UTC; MinValue is 0.</summary>
        public void DateTime(DateTime v) => Int64(v <= System.DateTime.MinValue ? 0 : v.ToUniversalTime().ToFileTimeUtc());

        public void Guid(Guid g) => Bytes(g.ToByteArray());

        public void NodeId(NodeId id)
        {
            if (id.Text != null)
            {
                Byte(3);
                UInt16(id.Namespace);
                String(id.Text);
            }
            else if (id.Namespace == 0 && id.Numeric <= 255)
            {
                Byte(0);
                Byte((byte)id.Numeric);
            }
            else if (id.Namespace <= 255 && id.Numeric <= 65535)
            {
                Byte(1);
                Byte((byte)id.Namespace);
                UInt16((ushort)id.Numeric);
            }
            else
            {
                Byte(2);
                UInt16(id.Namespace);
                UInt32(id.Numeric);
            }
        }

        public void ExpandedNodeId(NodeId id) => NodeId(id);   // no namespace URI or server index here

        public void StatusCode(uint v) => UInt32(v);

        public void QualifiedName(QualifiedName? q)
        {
            UInt16(q?.Namespace ?? 0);
            String(q?.Name);
        }

        public void LocalizedText(LocalizedText? t)
        {
            byte mask = (byte)((t?.Locale != null ? 1 : 0) | (t?.Text != null ? 2 : 0));
            Byte(mask);
            if (t?.Locale != null) String(t.Locale);
            if (t?.Text != null) String(t.Text);
        }

        public void ExtensionObject(NodeId typeId, Action<UaWriter>? body)
        {
            NodeId(typeId);
            if (body == null)
            {
                Byte(0);
                return;
            }

            var inner = new UaWriter();
            body(inner);
            Byte(1);
            ByteString(inner.ToArray());
        }

        public void ExtensionObject(ExtensionObject? e)
        {
            if (e == null || e.TypeId.IsNull)
            {
                NodeId(OpcUa.NodeId.Null);
                Byte(0);
                return;
            }

            NodeId(e.TypeId);
            if (e.Body == null) Byte(0);
            else
            {
                Byte(1);
                ByteString(e.Body);
            }
        }

        public void Variant(Variant v)
        {
            if (v.Type == UaType.Null)
            {
                Byte(0);
                return;
            }

            if (v.Value is System.Array array && v.Type != UaType.ByteString)
            {
                Byte((byte)((byte)v.Type | 0x80));
                Int32(array.Length);
                foreach (var item in array) Scalar(v.Type, item);
                return;
            }

            Byte((byte)v.Type);
            Scalar(v.Type, v.Value);
        }

        private void Scalar(UaType type, object? value)
        {
            switch (type)
            {
                case UaType.Boolean: Boolean(Convert.ToBoolean(value)); break;
                case UaType.SByte: SByte(Convert.ToSByte(value)); break;
                case UaType.Byte: Byte(Convert.ToByte(value)); break;
                case UaType.Int16: Int16(Convert.ToInt16(value)); break;
                case UaType.UInt16: UInt16(Convert.ToUInt16(value)); break;
                case UaType.Int32: Int32(Convert.ToInt32(value)); break;
                case UaType.UInt32: UInt32(Convert.ToUInt32(value)); break;
                case UaType.Int64: Int64(Convert.ToInt64(value)); break;
                case UaType.UInt64: UInt64(Convert.ToUInt64(value)); break;
                case UaType.Float: Float(Convert.ToSingle(value)); break;
                case UaType.Double: Double(Convert.ToDouble(value)); break;
                case UaType.String: String((string?)value); break;
                case UaType.DateTime: DateTime((DateTime)value!); break;
                case UaType.Guid: Guid((Guid)value!); break;
                case UaType.ByteString: ByteString((byte[]?)value); break;
                case UaType.NodeId: NodeId((NodeId)value!); break;
                case UaType.ExpandedNodeId: ExpandedNodeId((NodeId)value!); break;
                case UaType.StatusCode: StatusCode(Convert.ToUInt32(value)); break;
                case UaType.QualifiedName: QualifiedName((QualifiedName?)value); break;
                case UaType.LocalizedText: LocalizedText((LocalizedText?)value); break;
                case UaType.ExtensionObject: ExtensionObject((ExtensionObject?)value); break;
                default: throw new NotSupportedException("Variant türü yazılamıyor: " + type);
            }
        }

        public void DataValue(DataValue? d)
        {
            if (d == null)
            {
                Byte(0);
                return;
            }

            byte mask = (byte)((d.HasValue ? 0x01 : 0) | (d.Status != 0 ? 0x02 : 0) | (d.SourceTimestamp != null ? 0x04 : 0) | (d.ServerTimestamp != null ? 0x08 : 0));
            Byte(mask);
            if (d.HasValue) Variant(d.Value);
            if (d.Status != 0) StatusCode(d.Status);
            if (d.SourceTimestamp is DateTime s) DateTime(s);
            if (d.ServerTimestamp is DateTime t) DateTime(t);
        }

        /// <summary>A DiagnosticInfo with nothing in it.</summary>
        public void EmptyDiagnostics() => Byte(0);

        public void Array<T>(IReadOnlyCollection<T>? items, Action<T> each)
        {
            if (items == null)
            {
                Int32(-1);
                return;
            }

            Int32(items.Count);
            foreach (var item in items) each(item);
        }
    }

    /// <summary>[opcua] Little-endian reader of OPC UA's binary encoding; a malformed message is a <see cref="InvalidDataException"/> (BadDecodingError).</summary>
    public sealed class UaReader
    {
        private readonly byte[] _b;
        private int _i;
        private readonly int _end;

        public UaReader(byte[] bytes, int offset = 0, int count = -1)
        {
            _b = bytes;
            _i = offset;
            _end = count < 0 ? bytes.Length : offset + count;
        }

        public int Position => _i;
        public int Remaining => _end - _i;

        private void Need(int n)
        {
            if (n < 0 || _i + n > _end) throw new InvalidDataException("OPC UA iletisi beklenenden kısa.");
        }

        public byte Byte()
        {
            Need(1);
            return _b[_i++];
        }

        public bool Boolean() => Byte() != 0;
        public sbyte SByte() => unchecked((sbyte)Byte());

        private byte[] Take(int n)
        {
            Need(n);
            var r = new byte[n];
            System.Array.Copy(_b, _i, r, 0, n);
            _i += n;
            if (!BitConverter.IsLittleEndian) System.Array.Reverse(r);
            return r;
        }

        public short Int16() => BitConverter.ToInt16(Take(2), 0);
        public ushort UInt16() => BitConverter.ToUInt16(Take(2), 0);
        public int Int32() => BitConverter.ToInt32(Take(4), 0);
        public uint UInt32() => BitConverter.ToUInt32(Take(4), 0);
        public long Int64() => BitConverter.ToInt64(Take(8), 0);
        public ulong UInt64() => BitConverter.ToUInt64(Take(8), 0);
        public float Float() => BitConverter.ToSingle(Take(4), 0);
        public double Double() => BitConverter.ToDouble(Take(8), 0);

        public byte[] Bytes(int n)
        {
            Need(n);
            var r = new byte[n];
            System.Array.Copy(_b, _i, r, 0, n);
            _i += n;
            return r;
        }

        public string? String()
        {
            int n = Int32();
            if (n < 0) return null;
            Need(n);
            string s = Encoding.UTF8.GetString(_b, _i, n);
            _i += n;
            return s;
        }

        public byte[]? ByteString()
        {
            int n = Int32();
            return n < 0 ? null : Bytes(n);
        }

        public DateTime DateTime()
        {
            long v = Int64();
            if (v <= 0) return System.DateTime.MinValue;
            try
            {
                return System.DateTime.FromFileTimeUtc(v);
            }
            catch (ArgumentOutOfRangeException)
            {
                return System.DateTime.MaxValue;
            }
        }

        public Guid Guid() => new Guid(Bytes(16));

        public NodeId NodeId()
        {
            byte encoding = Byte();
            switch (encoding & 0x3F)
            {
                case 0: return new NodeId(0, Byte());
                case 1:
                {
                    byte ns = Byte();
                    return new NodeId(ns, UInt16());
                }

                case 2:
                {
                    ushort ns = UInt16();
                    return new NodeId(ns, UInt32());
                }

                case 3:
                {
                    ushort ns = UInt16();
                    return new NodeId(ns, String() ?? string.Empty);
                }

                case 4:
                {
                    ushort ns = UInt16();
                    return new NodeId(ns, Guid().ToString());
                }

                case 5:
                {
                    ushort ns = UInt16();
                    return new NodeId(ns, Convert.ToBase64String(ByteString() ?? System.Array.Empty<byte>()));
                }

                default:
                    throw new InvalidDataException($"NodeId kodlaması tanınmadı ({encoding}).");
            }
        }

        public NodeId ExpandedNodeId()
        {
            byte encoding = _b[Math.Min(_i, _b.Length - 1)];
            var id = NodeId();
            if ((encoding & 0x80) != 0) String();   // namespace URI
            if ((encoding & 0x40) != 0) UInt32();   // server index
            return id;
        }

        public uint StatusCode() => UInt32();

        public QualifiedName QualifiedName()
        {
            ushort ns = UInt16();
            return new QualifiedName(ns, String() ?? string.Empty);
        }

        public LocalizedText LocalizedText()
        {
            byte mask = Byte();
            string? locale = (mask & 1) != 0 ? String() : null;
            string? text = (mask & 2) != 0 ? String() : null;
            return new LocalizedText(text ?? string.Empty, locale);
        }

        public ExtensionObject ExtensionObject()
        {
            var e = new ExtensionObject { TypeId = NodeId() };
            byte encoding = Byte();
            if (encoding == 1 || encoding == 2) e.Body = ByteString();
            return e;
        }

        public Variant Variant()
        {
            byte mask = Byte();
            var type = (UaType)(mask & 0x3F);
            if (type == UaType.Null) return OpcUa.Variant.Null;
            if ((mask & 0x80) != 0)
            {
                int n = Int32();
                if (n > 1_000_000) throw new InvalidDataException("Dizi çok uzun.");
                var items = new object?[Math.Max(0, n)];
                for (int i = 0; i < items.Length; i++) items[i] = Scalar(type);
                if ((mask & 0x40) != 0)
                {
                    int dims = Int32();
                    for (int i = 0; i < dims; i++) Int32();
                }

                return new Variant(type, items);
            }

            return new Variant(type, Scalar(type));
        }

        private object? Scalar(UaType type) => type switch
        {
            UaType.Boolean => Boolean(),
            UaType.SByte => SByte(),
            UaType.Byte => Byte(),
            UaType.Int16 => Int16(),
            UaType.UInt16 => UInt16(),
            UaType.Int32 => Int32(),
            UaType.UInt32 => UInt32(),
            UaType.Int64 => Int64(),
            UaType.UInt64 => UInt64(),
            UaType.Float => Float(),
            UaType.Double => Double(),
            UaType.String => String(),
            UaType.DateTime => DateTime(),
            UaType.Guid => Guid(),
            UaType.ByteString => ByteString(),
            UaType.XmlElement => ByteString(),
            UaType.NodeId => NodeId(),
            UaType.ExpandedNodeId => ExpandedNodeId(),
            UaType.StatusCode => StatusCode(),
            UaType.QualifiedName => QualifiedName(),
            UaType.LocalizedText => LocalizedText(),
            UaType.ExtensionObject => ExtensionObject(),
            UaType.DataValue => DataValue(),
            UaType.Variant => Variant(),
            UaType.DiagnosticInfo => SkipDiagnostics(),
            _ => throw new InvalidDataException("Variant türü tanınmadı: " + (int)type)
        };

        public DataValue DataValue()
        {
            byte mask = Byte();
            var d = new DataValue { HasValue = (mask & 0x01) != 0 };
            if ((mask & 0x01) != 0) d.Value = Variant();
            if ((mask & 0x02) != 0) d.Status = StatusCode();
            if ((mask & 0x04) != 0) d.SourceTimestamp = DateTime();
            if ((mask & 0x10) != 0) UInt16();
            if ((mask & 0x08) != 0) d.ServerTimestamp = DateTime();
            if ((mask & 0x20) != 0) UInt16();
            return d;
        }

        public object? SkipDiagnostics()
        {
            byte mask = Byte();
            if ((mask & 0x01) != 0) Int32();
            if ((mask & 0x02) != 0) Int32();
            if ((mask & 0x04) != 0) Int32();
            if ((mask & 0x08) != 0) Int32();
            if ((mask & 0x10) != 0) String();
            if ((mask & 0x20) != 0) StatusCode();
            if ((mask & 0x40) != 0) SkipDiagnostics();
            return null;
        }

        public List<T> Array<T>(Func<T> each)
        {
            int n = Int32();
            var list = new List<T>();
            if (n > 100_000) throw new InvalidDataException("Dizi çok uzun.");
            for (int i = 0; i < n; i++) list.Add(each());
            return list;
        }
    }
}
