using System.Buffers.Binary;
using System.Diagnostics;

static class RpiSmokeTest
{
    private const uint Magic = 0x4D495052; // RPIM
    private const ushort Version = 1;
    private const int HeaderBytes = 64;

    private sealed class Args
    {
        public string RepoRoot { get; set; } = Directory.GetCurrentDirectory();
        public string FirmwareExe { get; set; } = string.Empty;
        public string ShmDir { get; set; } = string.Empty;
    }

    private readonly struct ShmHeader
    {
        public readonly uint Magic;
        public readonly ushort Version;
        public readonly ushort HeaderSize;
        public readonly int Width;
        public readonly int Height;
        public readonly int Stride;
        public readonly int PayloadBytes;
        public readonly ulong Sequence;
        public readonly ulong TimestampUs;
        public readonly uint Flags;

        public ShmHeader(uint magic, ushort version, ushort headerSize, int width, int height, int stride,
            int payloadBytes, ulong sequence, ulong timestampUs, uint flags)
        {
            Magic = magic;
            Version = version;
            HeaderSize = headerSize;
            Width = width;
            Height = height;
            Stride = stride;
            PayloadBytes = payloadBytes;
            Sequence = sequence;
            TimestampUs = timestampUs;
            Flags = flags;
        }
    }

    public static int Main(string[] args)
    {
        var options = ParseArgs(args);
        if (string.IsNullOrWhiteSpace(options.FirmwareExe))
        {
            options.FirmwareExe = Path.Combine(options.RepoRoot, "builds", "firmware", "RoboTwinFirmwareHost.exe");
        }
        if (string.IsNullOrWhiteSpace(options.ShmDir))
        {
            options.ShmDir = Path.Combine(options.RepoRoot, "logs", "rpi", "smoke_shm");
        }

        if (!File.Exists(options.FirmwareExe))
        {
            Console.WriteLine($"Missing firmware exe: {options.FirmwareExe}");
            return 1;
        }

        Directory.CreateDirectory(options.ShmDir);
        string logPath = Path.Combine(options.RepoRoot, "logs", "rpi", "rpi_smoke.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.FirmwareExe,
            Arguments = $"--pipe RoboTwin.FirmwareEngine --lockstep --rpi-enable --rpi-allow-mock --rpi-shm-dir \"{options.ShmDir}\" --rpi-display 96x64 --rpi-camera 96x64 --rpi-log \"{logPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(startInfo);
        if (proc == null)
        {
            Console.WriteLine("Failed to start firmware host.");
            return 1;
        }

        try
        {
            string displayPath = Path.Combine(options.ShmDir, "rpi_display.shm");
            string gpioPath = Path.Combine(options.ShmDir, "rpi_gpio.shm");
            string timePath = Path.Combine(options.ShmDir, "rpi_time.shm");
            string statusPath = Path.Combine(options.ShmDir, "rpi_status.shm");

            if (!WaitForDisplay(displayPath))
            {
                Console.WriteLine("Display channel did not update.");
                return 1;
            }

            WriteGpio(gpioPath);
            WriteTime(timePath);

            if (!TryReadStatus(statusPath, out var status))
            {
                Console.WriteLine("Status channel not readable.");
                return 1;
            }

            Console.WriteLine($"RPI status: {status}");
            return 0;
        }
        finally
        {
            proc.Kill(true);
            proc.WaitForExit(2000);
        }
    }

    private static Args ParseArgs(string[] args)
    {
        var options = new Args();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--repo" && i + 1 < args.Length)
            {
                options.RepoRoot = args[++i];
                continue;
            }
            if (args[i] == "--exe" && i + 1 < args.Length)
            {
                options.FirmwareExe = args[++i];
                continue;
            }
            if (args[i] == "--shm-dir" && i + 1 < args.Length)
            {
                options.ShmDir = args[++i];
                continue;
            }
        }
        return options;
    }

    private static bool WaitForDisplay(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (!File.Exists(path))
            {
                Thread.Sleep(100);
                continue;
            }
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (TryReadHeader(fs, out var header) && header.Sequence > 0)
            {
                return true;
            }
            Thread.Sleep(100);
        }
        return false;
    }

    private static void WriteGpio(string path)
    {
        if (!File.Exists(path)) return;
        byte[] payload = new byte[260];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 17);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
        WriteChannel(path, payload);
    }

    private static void WriteTime(string path)
    {
        if (!File.Exists(path)) return;
        byte[] payload = new byte[16];
        BinaryPrimitives.WriteDoubleLittleEndian(payload.AsSpan(0, 8), 1.0);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(8, 8), DateTime.UtcNow.Ticks);
        WriteChannel(path, payload);
    }

    private static void WriteChannel(string path, byte[] payload)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (!TryReadHeader(fs, out var header)) return;
        ulong nextSequence = header.Sequence + 1;

        var headerBuffer = new byte[HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(headerBuffer.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(headerBuffer.AsSpan(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(headerBuffer.AsSpan(6, 2), HeaderBytes);
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(12, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(16, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer.AsSpan(20, 4), payload.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(headerBuffer.AsSpan(24, 8), nextSequence);
        BinaryPrimitives.WriteUInt64LittleEndian(headerBuffer.AsSpan(32, 8), (ulong)(DateTime.UtcNow.Ticks / 10));
        BinaryPrimitives.WriteUInt32LittleEndian(headerBuffer.AsSpan(40, 4), 0);

        fs.Seek(0, SeekOrigin.Begin);
        fs.Write(headerBuffer, 0, headerBuffer.Length);
        fs.Write(payload, 0, payload.Length);
        fs.Flush();
    }

    private static bool TryReadStatus(string path, out string message)
    {
        message = string.Empty;
        if (!File.Exists(path)) return false;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (!TryReadHeader(fs, out var header)) return false;
        if (header.PayloadBytes < 8) return false;
        byte[] payload = new byte[header.PayloadBytes];
        if (!ReadExact(fs, payload)) return false;
        message = System.Text.Encoding.UTF8.GetString(payload, 8, payload.Length - 8).TrimEnd('\0', ' ');
        return true;
    }

    private static bool ReadExact(FileStream fs, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = fs.Read(buffer, total, buffer.Length - total);
            if (read <= 0) return false;
            total += read;
        }
        return true;
    }

    private static bool TryReadHeader(FileStream fs, out ShmHeader header)
    {
        header = default;
        fs.Seek(0, SeekOrigin.Begin);
        Span<byte> buffer = stackalloc byte[HeaderBytes];
        if (!ReadExact(fs, buffer)) return false;

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(4, 2));
        ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2));
        int width = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(8, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(12, 4));
        int stride = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(16, 4));
        int payloadBytes = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(20, 4));
        ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(24, 8));
        ulong timestamp = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(32, 8));
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(40, 4));

        if (magic != Magic || version == 0 || headerSize != HeaderBytes) return false;
        header = new ShmHeader(magic, version, headerSize, width, height, stride, payloadBytes, sequence, timestamp, flags);
        return true;
    }

    private static bool ReadExact(FileStream fs, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = fs.Read(buffer.Slice(total));
            if (read <= 0) return false;
            total += read;
        }
        return true;
    }
}
