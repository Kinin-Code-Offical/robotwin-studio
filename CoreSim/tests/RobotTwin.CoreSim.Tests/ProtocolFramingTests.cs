using System;
using System.Buffers.Binary;
using RobotTwin.CoreSim.IPC;
using Xunit;

namespace RobotTwin.CoreSim.Tests;

public class ProtocolFramingTests
{
    [Fact]
    public void RejectsInvalidMagic()
    {
        var header = BuildHeader(0x0, FirmwareProtocol.ProtocolMajor, FirmwareProtocol.ProtocolMinor, 1, 0, 0, 1);
        Assert.False(FirmwareProtocol.TryParseHeader(header, out _, out var error));
        Assert.Equal("invalid_magic", error);
    }

    [Fact]
    public void RejectsUnsupportedMajor()
    {
        var header = BuildHeader(FirmwareProtocol.ProtocolMagic, 99, FirmwareProtocol.ProtocolMinor, 1, 0, 0, 1);
        Assert.False(FirmwareProtocol.TryParseHeader(header, out _, out var error));
        Assert.Equal("unsupported_major", error);
    }

    [Fact]
    public void RejectsOversizedPayload()
    {
        uint tooLarge = FirmwareProtocol.MaxPayloadBytes + 1;
        var header = BuildHeader(FirmwareProtocol.ProtocolMagic, FirmwareProtocol.ProtocolMajor, FirmwareProtocol.ProtocolMinor, 1, 0, tooLarge, 1);
        Assert.False(FirmwareProtocol.TryParseHeader(header, out _, out var error));
        Assert.Equal("payload_too_large", error);
    }

    [Fact]
    public void AcceptsNewerMinorVersion()
    {
        ushort minor = (ushort)(FirmwareProtocol.ProtocolMinor + 1);
        var header = BuildHeader(FirmwareProtocol.ProtocolMagic, FirmwareProtocol.ProtocolMajor, minor, 1, 0, 0, 7);
        Assert.True(FirmwareProtocol.TryParseHeader(header, out var parsed, out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(minor, parsed.VersionMinor);
    }

    private static byte[] BuildHeader(uint magic, ushort major, ushort minor, ushort type, ushort flags, uint payloadSize, uint sequence)
    {
        var buffer = new byte[FirmwareProtocol.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), magic);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), major);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), minor);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8, 2), type);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10, 2), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), payloadSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16, 4), sequence);
        return buffer;
    }
}
