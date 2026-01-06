using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

const uint ProtocolMagic = 0x57465452;
const ushort ProtocolMajor = 1;
const ushort ProtocolMinor = 1;
const int HeaderSize = 20;
const int PinCount = 70;
const int AnalogCount = 16;
const int BoardIdSize = 64;
const int BoardProfileSize = 64;

const uint FeatureTimestampMicros = 1u << 0;
const uint FeaturePerfCounters = 1u << 1;

const ushort MessageHello = 1;
const ushort MessageHelloAck = 2;
const ushort MessageLoadBvm = 3;
const ushort MessageStep = 4;
const ushort MessageOutputState = 5;
const ushort MessageError = 9;

string firmwareExe = "builds/firmware/RoboTwinFirmwareHost.exe";
string outputPath = "CoreSim/tests/RobotTwin.CoreSim.Tests/Fixtures/golden_trace_v1.json";
string pipeName = "RoboTwin.FirmwareEngine.Trace";
string boardId = "board";
string boardProfile = "ArduinoUno";
string? bvmPath = null;
string? firmwareLogPath = null;
bool launchFirmware = true;
int pinPrefix = 16;
double dtSeconds = 0.02;
int connectTimeoutMs = 15000;
int stepTimeoutMs = 3000;

var patterns = new List<int[]>
{
    new[] { 0, 1, 0, 1, 1, 0, 0, 1 },
    new[] { 1, 1, 1, 0, 0, 0, 1, 0 },
    new[] { 0, 0, 0, 0, 1, 1, 0, 0 },
    new[] { 1, 0, 1, 0, 1, 0, 1, 0 }
};

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--firmware":
            firmwareExe = args[++i];
            break;
        case "--output":
            outputPath = args[++i];
            break;
        case "--pipe":
            pipeName = args[++i];
            break;
        case "--board-id":
            boardId = args[++i];
            break;
        case "--board-profile":
            boardProfile = args[++i];
            break;
        case "--bvm":
            bvmPath = args[++i];
            break;
        case "--firmware-log":
            firmwareLogPath = args[++i];
            break;
        case "--no-launch":
            launchFirmware = false;
            break;
        case "--pin-prefix":
            pinPrefix = int.Parse(args[++i]);
            break;
        case "--dt":
            dtSeconds = double.Parse(args[++i]);
            break;
        case "--connect-timeout":
            connectTimeoutMs = int.Parse(args[++i]);
            break;
        case "--step-timeout":
            stepTimeoutMs = int.Parse(args[++i]);
            break;
        default:
            Console.WriteLine($"Unknown arg: {args[i]}");
            return 2;
    }
}

string ResolvePath(string path)
{
    if (Path.IsPathRooted(path))
    {
        return Path.GetFullPath(path);
    }
    return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
}

firmwareExe = ResolvePath(firmwareExe);
outputPath = ResolvePath(outputPath);
if (!string.IsNullOrWhiteSpace(bvmPath))
{
    bvmPath = ResolvePath(bvmPath);
}
if (!string.IsNullOrWhiteSpace(firmwareLogPath))
{
    firmwareLogPath = ResolvePath(firmwareLogPath);
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

if (!File.Exists(firmwareExe))
{
    Console.WriteLine($"Firmware exe not found: {firmwareExe}");
    return 1;
}

Console.WriteLine($"Firmware exe: {firmwareExe}");
Console.WriteLine($"Pipe: {pipeName} | BoardId: {boardId} | Profile: {boardProfile}");

Process? firmwareProc = null;
try
{
    if (launchFirmware)
    {
        var argsList = new List<string> { "--pipe", pipeName, "--lockstep" };
        if (!string.IsNullOrWhiteSpace(firmwareLogPath))
        {
            argsList.Add("--log");
            argsList.Add(firmwareLogPath);
        }
        string argLine = string.Join(" ", argsList);
        Console.WriteLine($"Launching firmware: {firmwareExe} {argLine}");
        firmwareProc = Process.Start(new ProcessStartInfo
        {
            FileName = firmwareExe,
            Arguments = argLine,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
    pipe.Connect(connectTimeoutMs);
    pipe.ReadMode = PipeTransmissionMode.Byte;
    SendHello(pipe);
    if (!WaitForHelloAck(pipe, stepTimeoutMs))
    {
        Console.WriteLine("HelloAck timeout.");
        return 1;
    }

    if (!string.IsNullOrWhiteSpace(bvmPath))
    {
        if (!File.Exists(bvmPath))
        {
            Console.WriteLine($"BVM not found: {bvmPath}");
            return 1;
        }

        SendLoadBvm(pipe, boardId, boardProfile, File.ReadAllBytes(bvmPath));
    }

    var trace = new GoldenTrace
    {
        Version = 1,
        DtSeconds = dtSeconds,
        Metadata = new Dictionary<string, string>
        {
            ["source"] = "firmware_lockstep",
            ["recorded_utc"] = DateTime.UtcNow.ToString("O"),
            ["firmware_exe"] = firmwareExe,
            ["board_id"] = boardId,
            ["board_profile"] = boardProfile,
            ["pin_prefix"] = pinPrefix.ToString()
        }
    };

    uint deltaMicros = (uint)Math.Max(1, (int)Math.Round(dtSeconds * 1_000_000.0));

    for (int step = 0; step < patterns.Count; step++)
    {
        var inputs = patterns[step];
        ulong sequence = (ulong)(step + 1);
        Console.WriteLine($"Stepping {sequence}...");
        SendStep(pipe, boardId, sequence, deltaMicros, inputs);
        var outputs = ReadOutputPins(pipe, sequence, stepTimeoutMs);
        var expected = new int[Math.Max(1, Math.Min(pinPrefix, outputs.Length))];
        Array.Copy(outputs, expected, expected.Length);
        trace.Steps.Add(new GoldenTraceStep
        {
            StepSequence = sequence,
            Inputs = inputs,
            ExpectedPins = expected
        });
    }

    var options = new JsonSerializerOptions { WriteIndented = true };
    string json = JsonSerializer.Serialize(trace, options);
    File.WriteAllText(outputPath, json);

    Console.WriteLine($"Golden trace recorded to {outputPath}");
    return 0;
}
finally
{
    if (firmwareProc != null && !firmwareProc.HasExited)
    {
        firmwareProc.Kill(entireProcessTree: true);
    }
}

static void SendHello(Stream stream)
{
    var payload = new byte[16];
    WriteUInt32(payload, 0, FeatureTimestampMicros | FeaturePerfCounters);
    WriteUInt32(payload, 4, PinCount);
    WriteUInt32(payload, 8, BoardIdSize);
    WriteUInt32(payload, 12, AnalogCount);
    WritePacket(stream, MessageHello, payload);
}

static bool WaitForHelloAck(Stream stream, int timeoutMs)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        if (!TryReadPacket(stream, timeoutMs, out ushort type, out byte[] payload))
        {
            continue;
        }

        if (type == MessageHelloAck)
        {
            Console.WriteLine("HelloAck received.");
            return payload.Length >= 16;
        }
        Console.WriteLine($"Skipping packet type {type} size={payload.Length}");
    }
    return false;
}

static void SendLoadBvm(Stream stream, string boardId, string boardProfile, byte[] data)
{
    var header = new byte[BoardIdSize + BoardProfileSize];
    WriteFixedString(header, 0, BoardIdSize, boardId);
    WriteFixedString(header, BoardIdSize, BoardProfileSize, boardProfile);
    var payload = new byte[header.Length + data.Length];
    Buffer.BlockCopy(header, 0, payload, 0, header.Length);
    Buffer.BlockCopy(data, 0, payload, header.Length, data.Length);
    WritePacket(stream, MessageLoadBvm, payload);
}

static void SendStep(Stream stream, string boardId, ulong sequence, uint deltaMicros, int[] inputs)
{
    var payload = new byte[BoardIdSize + 8 + 4 + PinCount + (AnalogCount * 2) + 8];
    WriteFixedString(payload, 0, BoardIdSize, boardId);
    WriteUInt64(payload, BoardIdSize, sequence);
    WriteUInt32(payload, BoardIdSize + 8, deltaMicros);
    for (int i = 0; i < PinCount; i++)
    {
        int value = (inputs != null && i < inputs.Length && inputs[i] > 0) ? 1 : 0;
        payload[BoardIdSize + 8 + 4 + i] = (byte)value;
    }
    int analogOffset = BoardIdSize + 8 + 4 + PinCount;
    for (int i = 0; i < AnalogCount; i++)
    {
        WriteUInt16(payload, analogOffset + (i * 2), 0);
    }
    WriteUInt64(payload, analogOffset + (AnalogCount * 2), 0);
    WritePacket(stream, MessageStep, payload);
}

static int[] ReadOutputPins(Stream stream, ulong expectedSequence, int timeoutMs)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        if (!TryReadPacket(stream, timeoutMs, out ushort type, out byte[] payload))
        {
            continue;
        }

        if (type == MessageError)
        {
            string message = payload.Length > BoardIdSize + 4
                ? Encoding.UTF8.GetString(payload, BoardIdSize + 4, payload.Length - (BoardIdSize + 4))
                : "Unknown firmware error";
            throw new InvalidOperationException($"Firmware error: {message}");
        }

        if (type != MessageOutputState || payload.Length < BoardIdSize + 16 + PinCount)
        {
            Console.WriteLine($"Skipping packet type {type} size={payload.Length}");
            continue;
        }

        ulong sequence = ReadUInt64(payload, BoardIdSize);
        if (sequence != expectedSequence)
        {
            continue;
        }

        var outputs = new int[PinCount];
        int offset = BoardIdSize + 16;
        for (int i = 0; i < PinCount; i++)
        {
            byte raw = payload[offset + i];
            outputs[i] = raw == 0xFF ? -1 : raw;
        }
        return outputs;
    }

    throw new TimeoutException($"Timed out waiting for output sequence {expectedSequence}.");
}

static void WritePacket(Stream stream, ushort type, byte[] payload)
{
    payload ??= Array.Empty<byte>();
    var header = new byte[HeaderSize];
    WriteUInt32(header, 0, ProtocolMagic);
    WriteUInt16(header, 4, ProtocolMajor);
    WriteUInt16(header, 6, ProtocolMinor);
    WriteUInt16(header, 8, type);
    WriteUInt16(header, 10, 0);
    WriteUInt32(header, 12, (uint)payload.Length);
    WriteUInt32(header, 16, 1);

    stream.Write(header, 0, header.Length);
    if (payload.Length > 0)
    {
        stream.Write(payload, 0, payload.Length);
    }
    stream.Flush();
}

static bool TryReadPacket(Stream stream, int timeoutMs, out ushort type, out byte[] payload)
{
    type = 0;
    payload = Array.Empty<byte>();
    var header = new byte[HeaderSize];
    if (!ReadExact(stream, header, 0, header.Length, timeoutMs))
    {
        return false;
    }
    uint magic = ReadUInt32(header, 0);
    if (magic != ProtocolMagic)
    {
        Console.WriteLine($"Invalid magic: 0x{magic:X8}");
        return false;
    }
    ushort major = ReadUInt16(header, 4);
    if (major != ProtocolMajor)
    {
        Console.WriteLine($"Unsupported major: {major}");
        return false;
    }
    type = ReadUInt16(header, 8);
    uint payloadSize = ReadUInt32(header, 12);
    if (payloadSize > 8 * 1024 * 1024)
    {
        return false;
    }
    if (payloadSize == 0)
    {
        return true;
    }
    payload = new byte[payloadSize];
    return ReadExact(stream, payload, 0, payload.Length, timeoutMs);
}

static bool ReadExact(Stream stream, byte[] buffer, int offset, int count, int timeoutMs)
{
    int total = 0;
    long deadline = Stopwatch.GetTimestamp() + (long)(timeoutMs * (Stopwatch.Frequency / 1000.0));
    while (total < count)
    {
        try
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= deadline)
            {
                return false;
            }
            int remainingMs = (int)Math.Max(1, (deadline - now) * 1000.0 / Stopwatch.Frequency);
            var readTask = stream.ReadAsync(buffer.AsMemory(offset + total, count - total)).AsTask();
            if (!readTask.Wait(remainingMs))
            {
                return false;
            }
            int read = readTask.Result;
            if (read <= 0)
            {
                return false;
            }
            total += read;
        }
        catch (IOException)
        {
            return false;
        }
    }
    return true;
}

static void WriteFixedString(byte[] buffer, int offset, int size, string value)
{
    for (int i = 0; i < size; i++)
    {
        buffer[offset + i] = 0;
    }
    if (string.IsNullOrWhiteSpace(value))
    {
        return;
    }
    var bytes = Encoding.UTF8.GetBytes(value);
    int count = Math.Min(bytes.Length, size - 1);
    Buffer.BlockCopy(bytes, 0, buffer, offset, count);
}

static void WriteUInt32(byte[] buffer, int offset, uint value)
{
    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);
}

static void WriteUInt64(byte[] buffer, int offset, ulong value)
{
    BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset, 8), value);
}

static void WriteUInt16(byte[] buffer, int offset, ushort value)
{
    BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), value);
}

static uint ReadUInt32(byte[] buffer, int offset)
{
    return BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
}

static ulong ReadUInt64(byte[] buffer, int offset)
{
    return BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset, 8));
}

static ushort ReadUInt16(byte[] buffer, int offset)
{
    return BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2));
}

internal sealed class GoldenTrace
{
    public int Version { get; set; }
    public double DtSeconds { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public List<GoldenTraceStep> Steps { get; set; } = new();
}

internal sealed class GoldenTraceStep
{
    public ulong StepSequence { get; set; }
    public int[] Inputs { get; set; } = Array.Empty<int>();
    public int[] ExpectedPins { get; set; } = Array.Empty<int>();
}
