using System;
using RobotTwin.CoreSim.IPC;
using Xunit;

namespace RobotTwin.CoreSim.Tests;

public class FirmwareProtocolTests
{
    [Fact]
    public void TryParseHeader_RejectsTooSmallHeader()
    {
        var ok = FirmwareProtocol.TryParseHeader(Array.Empty<byte>(), out _, out var error);
        Assert.False(ok);
        Assert.Equal("header_too_small", error);
    }

    [Fact]
    public void TryParseHeader_RejectsInvalidMagic()
    {
        var header = BuildHeader(magic: 0x12345678, major: FirmwareProtocol.ProtocolMajor, minor: FirmwareProtocol.ProtocolMinor, type: 1, flags: 0, payloadSize: 0, sequence: 1);
        var ok = FirmwareProtocol.TryParseHeader(header, out _, out var error);
        Assert.False(ok);
        Assert.Equal("invalid_magic", error);
    }

    [Fact]
    public void TryParseHeader_RejectsUnsupportedMajor()
    {
        var header = BuildHeader(magic: FirmwareProtocol.ProtocolMagic, major: (ushort)(FirmwareProtocol.ProtocolMajor + 1), minor: 0, type: 1, flags: 0, payloadSize: 0, sequence: 1);
        var ok = FirmwareProtocol.TryParseHeader(header, out _, out var error);
        Assert.False(ok);
        Assert.Equal("unsupported_major", error);
    }

    [Fact]
    public void TryParseHeader_RejectsOversizedPayload()
    {
        var header = BuildHeader(magic: FirmwareProtocol.ProtocolMagic, major: FirmwareProtocol.ProtocolMajor, minor: FirmwareProtocol.ProtocolMinor, type: 1, flags: 0, payloadSize: FirmwareProtocol.MaxPayloadBytes + 1, sequence: 1);
        var ok = FirmwareProtocol.TryParseHeader(header, out _, out var error);
        Assert.False(ok);
        Assert.Equal("payload_too_large", error);
    }

    [Fact]
    public void TryParseHeader_ParsesValidHeader()
    {
        var headerBytes = BuildHeader(magic: FirmwareProtocol.ProtocolMagic, major: FirmwareProtocol.ProtocolMajor, minor: FirmwareProtocol.ProtocolMinor, type: 5, flags: 7, payloadSize: 123, sequence: 42);
        var ok = FirmwareProtocol.TryParseHeader(headerBytes, out var header, out var error);
        Assert.True(ok);
        Assert.Equal(string.Empty, error);
        Assert.Equal(FirmwareProtocol.ProtocolMagic, header.Magic);
        Assert.Equal(FirmwareProtocol.ProtocolMajor, header.VersionMajor);
        Assert.Equal(FirmwareProtocol.ProtocolMinor, header.VersionMinor);
        Assert.Equal((ushort)5, header.Type);
        Assert.Equal((ushort)7, header.Flags);
        Assert.Equal((uint)123, header.PayloadSize);
        Assert.Equal((uint)42, header.Sequence);
    }

    private static byte[] BuildHeader(uint magic, ushort major, ushort minor, ushort type, ushort flags, uint payloadSize, uint sequence)
    {
        var header = new byte[FirmwareProtocol.HeaderSize];
        WriteUInt32(header, 0, magic);
        WriteUInt16(header, 4, major);
        WriteUInt16(header, 6, minor);
        WriteUInt16(header, 8, type);
        WriteUInt16(header, 10, flags);
        WriteUInt32(header, 12, payloadSize);
        WriteUInt32(header, 16, sequence);
        return header;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }
}
