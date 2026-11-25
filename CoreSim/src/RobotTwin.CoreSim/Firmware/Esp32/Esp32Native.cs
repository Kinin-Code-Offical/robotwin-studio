using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    /// <summary>Interop declarations mirroring native/esp32hal/rw_host_api.h (keep in sync).</summary>
    internal static class Esp32Abi
    {
        public const int Version = 2;   // 2: radio_request (Wi-Fi / BLE / Bluepad32 through the host's radio medium)

        public const int StatusOk = 0;
        public const int StatusWatchdog = 1;
        public const int StatusCrashed = 2;
        public const int StatusNotRunning = 3;

        public const int EventRestart = 1;
        public const int EventDeepSleep = 2;
        public const int EventWifi = 3;
        public const int EventExt0Wakeup = 4;
        public const int EventLightSleep = 5;   // arg 1 asleep, 0 awake

        public const int LogError = 1;
        public const int LogWarn = 2;
        public const int LogInfo = 3;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void LogFn(IntPtr user, int level, IntPtr message);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void PinModeFn(IntPtr user, int gpio, int mode);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DigitalWriteFn(IntPtr user, int gpio, int level);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int DigitalReadFn(IntPtr user, int gpio);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate double AnalogReadFn(IntPtr user, int gpio);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void PwmWriteFn(IntPtr user, int gpio, double frequency, double duty);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DacWriteFn(IntPtr user, int gpio, double volts);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int TouchReadFn(IntPtr user, int gpio);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SerialBeginFn(IntPtr user, int port, uint baud);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SerialWriteFn(IntPtr user, int port, IntPtr data, int length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int I2cStartFn(IntPtr user, int bus, int address, int read);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int I2cWriteFn(IntPtr user, int bus, int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int I2cReadFn(IntPtr user, int bus, int ack);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void I2cStopFn(IntPtr user, int bus);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SystemEventFn(IntPtr user, int kind, long arg);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int RadioRequestFn(IntPtr user, int op, IntPtr input, int inputLength, IntPtr output, int outputCapacity);

        [StructLayout(LayoutKind.Sequential)]
        public struct HostApi
        {
            public uint AbiVersion;
            public uint StructSize;
            public IntPtr User;
            public IntPtr Log;
            public IntPtr PinMode;
            public IntPtr DigitalWrite;
            public IntPtr DigitalRead;
            public IntPtr AnalogRead;
            public IntPtr PwmWrite;
            public IntPtr DacWrite;
            public IntPtr TouchRead;
            public IntPtr SerialBegin;
            public IntPtr SerialWrite;
            public IntPtr I2cStart;
            public IntPtr I2cWrite;
            public IntPtr I2cRead;
            public IntPtr I2cStop;
            public IntPtr SystemEvent;
            public IntPtr RadioRequest;   // ABI 2
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Config
        {
            public uint StructSize;
            public uint WatchdogMs;
            public ulong TimeOriginNs;
            public ulong RandomSeed;
            public int AdcModel;
            public int ResetReason;
            public int WakeupCause;
            public int Reserved0;
            public double CostScale;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int AbiVersionFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int InitFn(ref HostApi api, ref Config config);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int StartFn(ulong startNs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int RunUntilFn(ulong timeNs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void StopFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void PinChangedFn(int gpio, int level, ulong timeNs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int SerialRxFn(int port, byte[] data, int length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate ulong NowFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int StatusMessageFn(byte[] buffer, int capacity);
    }

    /// <summary>
    /// A loaded sketch DLL (LoadLibrary/GetProcAddress through kernel32, so it works on
    /// netstandard2.1 as well). Each instance owns its own copy of the module's globals:
    /// when the same file is already loaded by another instance, a private copy is loaded.
    /// </summary>
    internal sealed class Esp32SketchModule : IDisposable
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int> LoadedPaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private IntPtr _handle;
        private readonly string _loadedPath;
        private readonly bool _isCopy;

        public Esp32SketchModule(string dllPath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("ESP32 kodu MSVC ile derlenir ve yalnızca Windows x64'te çalışır.");
            }

            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("ESP32 kodu x64 olarak derlenir; uygulamanın 64 bit çalışması gerekir.");
            string full = Path.GetFullPath(dllPath);
            if (!File.Exists(full)) throw new FileNotFoundException("Derlenmiş ESP32 kodu (DLL) bulunamadı.", full);

            lock (Gate)
            {
                _loadedPath = full;
                if (LoadedPaths.ContainsKey(full))
                {
                    // Already mapped by another board: LoadLibrary would share its globals, so load a copy.
                    string copy = Path.Combine(Path.GetDirectoryName(full)!,
                        Path.GetFileNameWithoutExtension(full) + ".i" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".dll");
                    File.Copy(full, copy, true);
                    _loadedPath = copy;
                    _isCopy = true;
                }

                _handle = Kernel32.LoadLibraryW(_loadedPath);
                if (_handle == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"Derlenmiş ESP32 kodu yüklenemedi: '{_loadedPath}' (Win32 hata kodu {err}).");
                }

                LoadedPaths[_loadedPath] = LoadedPaths.TryGetValue(_loadedPath, out int n) ? n + 1 : 1;
            }

            AbiVersion = Get<Esp32Abi.AbiVersionFn>("rw_abi_version");
            Init = Get<Esp32Abi.InitFn>("rw_init");
            Start = Get<Esp32Abi.StartFn>("rw_start");
            RunUntil = Get<Esp32Abi.RunUntilFn>("rw_run_until");
            Stop = Get<Esp32Abi.StopFn>("rw_stop");
            PinChanged = Get<Esp32Abi.PinChangedFn>("rw_pin_changed");
            SerialRx = Get<Esp32Abi.SerialRxFn>("rw_serial_rx");
            Now = Get<Esp32Abi.NowFn>("rw_now_ns");
            StatusMessageRaw = Get<Esp32Abi.StatusMessageFn>("rw_status_message");
            int abi = AbiVersion();
            if (abi != Esp32Abi.Version)
            {
                Dispose();
                throw new InvalidOperationException($"Derlenmiş ESP32 kodu bu sürümle uyumsuz (ABI {abi}, beklenen {Esp32Abi.Version}); kodu yeniden derleyin.");
            }
        }

        public Esp32Abi.AbiVersionFn AbiVersion { get; }
        public Esp32Abi.InitFn Init { get; }
        public Esp32Abi.StartFn Start { get; }
        public Esp32Abi.RunUntilFn RunUntil { get; }
        public Esp32Abi.StopFn Stop { get; }
        public Esp32Abi.PinChangedFn PinChanged { get; }
        public Esp32Abi.SerialRxFn SerialRx { get; }
        public Esp32Abi.NowFn Now { get; }
        private Esp32Abi.StatusMessageFn StatusMessageRaw { get; }

        public string StatusMessage()
        {
            var buffer = new byte[4096];
            int n = StatusMessageRaw(buffer, buffer.Length);
            if (n <= 0) return string.Empty;
            return Encoding.UTF8.GetString(buffer, 0, Math.Min(n, buffer.Length));
        }

        private T Get<T>(string name) where T : Delegate
        {
            IntPtr p = Kernel32.GetProcAddress(_handle, name);
            if (p == IntPtr.Zero) throw new InvalidOperationException($"Derlenmiş ESP32 kodunda '{name}' yok; kod RobotWin'in ESP32 katmanıyla derlenmemiş olabilir, yeniden derleyin.");
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (_handle == IntPtr.Zero) return;
                Kernel32.FreeLibrary(_handle);
                _handle = IntPtr.Zero;
                if (LoadedPaths.TryGetValue(_loadedPath, out int n))
                {
                    if (n <= 1) LoadedPaths.Remove(_loadedPath);
                    else LoadedPaths[_loadedPath] = n - 1;
                }
            }

            if (_isCopy)
            {
                try { File.Delete(_loadedPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static class Kernel32
        {
            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
            public static extern IntPtr LoadLibraryW(string path);

            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false)]
            public static extern IntPtr GetProcAddress(IntPtr module, string name);

            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FreeLibrary(IntPtr module);
        }
    }
}
