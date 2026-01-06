using System;
using System.Buffers.Binary;

namespace RobotTwin.CoreSim.IPC
{
    internal static class FirmwareProtocol
    {
        internal const uint ProtocolMagic = 0x57465452; // "RTFW"
        internal const ushort ProtocolMajor = 1;
        internal const ushort ProtocolMinor = 1;
        internal const int HeaderSize = 20;
        internal const uint MaxPayloadBytes = 8 * 1024 * 1024;

        internal readonly struct Header
        {
            public readonly uint Magic;
            public readonly ushort VersionMajor;
            public readonly ushort VersionMinor;
            public readonly ushort Type;
            public readonly ushort Flags;
            public readonly uint PayloadSize;
            public readonly uint Sequence;

            public Header(uint magic, ushort major, ushort minor, ushort type, ushort flags, uint payloadSize, uint sequence)
            {
                Magic = magic;
                VersionMajor = major;
                VersionMinor = minor;
                Type = type;
                Flags = flags;
                PayloadSize = payloadSize;
                Sequence = sequence;
            }
        }

        internal static bool TryParseHeader(ReadOnlySpan<byte> buffer, out Header header, out string error)
        {
            header = default;
            error = string.Empty;

            if (buffer.Length < HeaderSize)
            {
                error = "header_too_small";
                return false;
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
            if (magic != ProtocolMagic)
            {
                error = "invalid_magic";
                return false;
            }

            ushort major = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2));
            ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2));
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(8, 2));
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(10, 2));
            uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12, 4));
            uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16, 4));

            if (major != ProtocolMajor)
            {
                error = "unsupported_major";
                return false;
            }

            if (payloadSize > MaxPayloadBytes)
            {
                error = "payload_too_large";
                return false;
            }

            header = new Header(magic, major, minor, type, flags, payloadSize, sequence);
            return true;
        }
    }
}
