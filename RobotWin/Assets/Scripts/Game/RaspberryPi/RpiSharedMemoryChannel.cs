using System;
using System.IO;
using System.Buffers.Binary;

namespace RobotTwin.Game.RaspberryPi
{
    public readonly struct RpiSharedHeader
    {
        public readonly uint Magic;
        public readonly ushort Version;
        public readonly ushort HeaderSize;
        public readonly int Width;
        public readonly int Height;
        public readonly int Stride;
        public readonly int PayloadSize;
        public readonly ulong Sequence;
        public readonly ulong TimestampMicros;
        public readonly uint Flags;

        public RpiSharedHeader(uint magic, ushort version, ushort headerSize, int width, int height, int stride,
            int payloadSize, ulong sequence, ulong timestampMicros, uint flags)
        {
            Magic = magic;
            Version = version;
            HeaderSize = headerSize;
            Width = width;
            Height = height;
            Stride = stride;
            PayloadSize = payloadSize;
            Sequence = sequence;
            TimestampMicros = timestampMicros;
            Flags = flags;
        }
    }

    public sealed class RpiSharedMemoryChannel : IDisposable
    {
        public const uint Magic = 0x4D495052; // "RPIM"
        public const ushort Version = 1;
        public const int HeaderBytes = 64;
        public const uint FlagUnavailable = 1u << 0;
        public const uint FlagError = 1u << 1;
        public const int StatusPayloadBytes = 256;
        public const int GpioPayloadBytes = 260;
        public const int ImuPayloadBytes = 64;
        public const int TimePayloadBytes = 16;
        public const int NetworkPayloadBytes = 16;

        private readonly string _path;
        private readonly int _payloadSize;
        private readonly byte[] _headerBuffer = new byte[HeaderBytes];
        private byte[] _payloadBuffer;
        private FileStream _stream;
        private ulong _sequence;

        public RpiSharedMemoryChannel(string path, int payloadSize)
        {
            _path = path;
            _payloadSize = payloadSize;
            _payloadBuffer = new byte[Math.Max(payloadSize, 1)];
        }

        public bool TryOpen(bool createIfMissing)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            int totalSize = HeaderBytes + _payloadSize;
            if (!File.Exists(_path))
            {
                if (!createIfMissing)
                {
                    return false;
                }
                using var fs = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
                fs.SetLength(totalSize);
            }
            else if (createIfMissing)
            {
                var info = new FileInfo(_path);
                if (info.Length < totalSize)
                {
                    using var fs = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    fs.SetLength(totalSize);
                }
            }

            _stream = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }

        public bool TryRead(out RpiSharedHeader header, out byte[] payload)
        {
            header = default;
            payload = Array.Empty<byte>();
            if (_stream == null) return false;
            if (_stream.Length < HeaderBytes) return false;

            _stream.Seek(0, SeekOrigin.Begin);
            int read = _stream.Read(_headerBuffer, 0, HeaderBytes);
            if (read != HeaderBytes) return false;

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(_headerBuffer.AsSpan(0, 4));
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(_headerBuffer.AsSpan(4, 2));
            ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(_headerBuffer.AsSpan(6, 2));
            int width = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer.AsSpan(8, 4));
            int height = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer.AsSpan(12, 4));
            int stride = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer.AsSpan(16, 4));
            int payloadSize = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer.AsSpan(20, 4));
            ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(_headerBuffer.AsSpan(24, 8));
            ulong timestamp = BinaryPrimitives.ReadUInt64LittleEndian(_headerBuffer.AsSpan(32, 8));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(_headerBuffer.AsSpan(40, 4));

            header = new RpiSharedHeader(magic, version, headerSize, width, height, stride, payloadSize, sequence, timestamp, flags);
            if (magic != Magic || version == 0 || headerSize != HeaderBytes || payloadSize <= 0 || payloadSize > _payloadSize)
            {
                return false;
            }

            if (_payloadBuffer.Length < payloadSize)
            {
                _payloadBuffer = new byte[payloadSize];
            }
            int total = 0;
            while (total < payloadSize)
            {
                int chunk = _stream.Read(_payloadBuffer, total, payloadSize - total);
                if (chunk <= 0) break;
                total += chunk;
            }
            if (total != payloadSize) return false;

            payload = _payloadBuffer;
            return true;
        }

        public void Write(int width, int height, int stride, byte[] payload, uint flags = 0)
        {
            if (_stream == null) return;
            if (payload == null)
            {
                payload = Array.Empty<byte>();
            }
            if (payload.Length < _payloadSize)
            {
                var temp = new byte[_payloadSize];
                Array.Copy(payload, temp, payload.Length);
                payload = temp;
            }

            _sequence++;
            var span = _headerBuffer.AsSpan();
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), Version);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6, 2), HeaderBytes);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8, 4), width);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12, 4), height);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16, 4), stride);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20, 4), _payloadSize);
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(24, 8), _sequence);
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(32, 8), NowMicros());
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(40, 4), flags);
            span.Slice(44, 20).Clear();

            _stream.Seek(0, SeekOrigin.Begin);
            _stream.Write(_headerBuffer, 0, HeaderBytes);
            _stream.Write(payload, 0, _payloadSize);
            _stream.Flush();
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }

        public bool IsNewSequence(ulong sequence)
        {
            if (sequence <= _sequence) return false;
            _sequence = sequence;
            return true;
        }

        private static ulong NowMicros()
        {
            return (ulong)(DateTime.UtcNow.Ticks / 10);
        }
    }
}
