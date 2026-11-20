using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using RobotTwin.CoreSim.Sandbox.Radio;

namespace RobotWin.Studio.Input
{
    /// <summary>
    /// Sony DualSense (PS5) and DualShock 4 (PS4) through raw HID (hid.dll + setupapi.dll),
    /// over USB or Bluetooth: sticks, triggers, buttons, D-pad, touchpad click, gyro/accel,
    /// battery; lightbar colour and rumble are written back as output reports. Report
    /// layouts follow the Linux hid-playstation driver. A background thread reads reports;
    /// <see cref="Read"/> copies the latest one.
    /// </summary>
    public sealed class SonyHidGamepad : IGamepadSource
    {
        public const ushort SonyVendor = 0x054C;
        private static readonly ushort[] DualSenseIds = { 0x0CE6, 0x0DF2 };
        private static readonly ushort[] DualShock4Ids = { 0x05C4, 0x09CC, 0x0BA0 };

        private readonly object _gate = new object();
        private readonly GamepadState _latest = new GamepadState();
        private readonly SafeFileHandle _read;
        private readonly SafeFileHandle? _write;
        private readonly FileStream _readStream;
        private readonly FileStream? _writeStream;
        private readonly Thread _thread;
        private readonly int _inputLength;
        private readonly int _outputLength;
        private volatile bool _connected = true;
        private volatile bool _disposed;
        private bool _hasReport;
        private byte _seq;
        private (byte R, byte G, byte B, byte Weak, byte Strong, byte Leds) _lastOut = (1, 1, 1, 1, 1, 1);

        private SonyHidGamepad(string path, ushort productId, SafeFileHandle read, SafeFileHandle? write, int inputLength, int outputLength)
        {
            Path = path;
            ProductId = productId;
            IsDualSense = Array.IndexOf(DualSenseIds, productId) >= 0;
            _read = read;
            _write = write;
            _inputLength = inputLength;
            _outputLength = outputLength;
            // USB pads report 64-byte input reports; Bluetooth ones are longer (78 / 547 bytes).
            IsBluetooth = inputLength != 64;
            _readStream = new FileStream(read, FileAccess.Read, 1, false);
            if (write != null) _writeStream = new FileStream(write, FileAccess.Write, 1, false);
            if (IsBluetooth) EnableFullReports();
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "HID " + Name };
            _thread.Start();
        }

        public string Path { get; }
        public ushort ProductId { get; }
        public bool IsDualSense { get; }
        public bool IsBluetooth { get; }
        public string Id => "sony:" + Path;
        public string Name => (IsDualSense ? "DualSense" : "DualShock 4") + (IsBluetooth ? " (Bluetooth)" : " (USB)");
        public string Kind => IsDualSense ? "dualsense" : "dualshock4";
        public bool IsConnected => _connected && !_disposed;

        // ================================================================ discovery

        /// <summary>Sony controllers currently present (HID interface path, product id).</summary>
        public static List<(string Path, ushort ProductId)> Enumerate()
        {
            var list = new List<(string, ushort)>();
            Native.HidD_GetHidGuid(out var guid);
            IntPtr set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, Native.DigcfPresent | Native.DigcfDeviceInterface);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
            try
            {
                for (int i = 0; ; i++)
                {
                    var data = new Native.SpDeviceInterfaceData { CbSize = Marshal.SizeOf<Native.SpDeviceInterfaceData>() };
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                    if (size <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)) continue;
                        string? path = Marshal.PtrToStringUni(detail + 4);
                        if (string.IsNullOrEmpty(path)) continue;
                        using var h = Native.CreateFile(path!, 0, Native.FileShareRead | Native.FileShareWrite, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
                        if (h.IsInvalid) continue;
                        var attr = new Native.HiddAttributes { Size = Marshal.SizeOf<Native.HiddAttributes>() };
                        if (!Native.HidD_GetAttributes(h, ref attr) || attr.VendorID != SonyVendor) continue;
                        if (Array.IndexOf(DualSenseIds, attr.ProductID) < 0 && Array.IndexOf(DualShock4Ids, attr.ProductID) < 0) continue;
                        list.Add((path!, attr.ProductID));
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(set);
            }

            return list;
        }

        public static SonyHidGamepad? Open(string path, ushort productId)
        {
            var read = Native.CreateFile(path, Native.GenericRead | Native.GenericWrite, Native.FileShareRead | Native.FileShareWrite, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
            if (read.IsInvalid)
            {
                read.Dispose();
                return null;
            }

            int input = 64, output = 48;
            if (Native.HidD_GetPreparsedData(read, out IntPtr pre))
            {
                try
                {
                    if (Native.HidP_GetCaps(pre, out var caps) == Native.HidpStatusSuccess)
                    {
                        input = caps.InputReportByteLength;
                        output = caps.OutputReportByteLength;
                    }
                }
                finally
                {
                    Native.HidD_FreePreparsedData(pre);
                }
            }

            var write = Native.CreateFile(path, Native.GenericWrite, Native.FileShareRead | Native.FileShareWrite, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
            if (write.IsInvalid)
            {
                write.Dispose();
                write = null;
            }

            try
            {
                return new SonyHidGamepad(path, productId, read, write, Math.Max(10, input), output);
            }
            catch (IOException)
            {
                read.Dispose();
                write?.Dispose();
                return null;
            }
        }

        /// <summary>Over Bluetooth both pads send a short report until the calibration feature report is read.</summary>
        private void EnableFullReports()
        {
            var buf = new byte[IsDualSense ? 41 : 37];
            buf[0] = (byte)(IsDualSense ? 0x05 : 0x02);
            Native.HidD_GetFeature(_read, buf, buf.Length);
        }

        // ================================================================ input

        private void ReadLoop()
        {
            var buf = new byte[Math.Max(64, _inputLength)];
            var s = new GamepadState();
            while (!_disposed)
            {
                int n;
                try
                {
                    n = _readStream.Read(buf, 0, _inputLength);
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException || ex is UnauthorizedAccessException)
                {
                    _connected = false;
                    return;
                }

                if (n <= 0)
                {
                    _connected = false;
                    return;
                }

                if (!Parse(buf, n, IsDualSense, s)) continue;
                lock (_gate)
                {
                    _latest.CopyFrom(s);
                    _hasReport = true;
                }
            }
        }

        /// <summary>Decodes one input report (any of the USB / Bluetooth layouts). Public for the self test.</summary>
        public static bool Parse(byte[] r, int n, bool dualSense, GamepadState s)
        {
            if (n < 10) return false;
            int o;          // offset of the left stick X byte
            bool simple;    // short Bluetooth report: LX LY RX RY buttons×3 L2 R2
            if (dualSense)
            {
                if (r[0] == 0x01 && n >= 64) { o = 1; simple = false; }
                else if (r[0] == 0x31 && n >= 64) { o = 2; simple = false; }
                else if (r[0] == 0x01) { o = 1; simple = true; }
                else return false;
            }
            else
            {
                if (r[0] == 0x01) { o = 1; simple = n < 32; }
                else if (r[0] == 0x11 && n >= 64) { o = 3; simple = false; }
                else return false;
            }

            s.AxisX = Stick(r[o]);
            s.AxisY = Stick(r[o + 1]);
            s.AxisRX = Stick(r[o + 2]);
            s.AxisRY = Stick(r[o + 3]);
            int b0, b1, b2;
            if (dualSense && !simple)
            {
                s.Brake = r[o + 4] * 4;
                s.Throttle = r[o + 5] * 4;
                b0 = r[o + 7];
                b1 = r[o + 8];
                b2 = r[o + 9];
                if (n >= o + 27)
                {
                    s.GyroX = BitConverter.ToInt16(r, o + 15);
                    s.GyroY = BitConverter.ToInt16(r, o + 17);
                    s.GyroZ = BitConverter.ToInt16(r, o + 19);
                    s.AccelX = BitConverter.ToInt16(r, o + 21);
                    s.AccelY = BitConverter.ToInt16(r, o + 23);
                    s.AccelZ = BitConverter.ToInt16(r, o + 25);
                }

                if (n > o + 52) s.Battery = (byte)Math.Min(255, (r[o + 52] & 0x0F) * 25 + 5);
            }
            else
            {
                b0 = r[o + 4];
                b1 = r[o + 5];
                b2 = r[o + 6];
                s.Brake = r[o + 7] * 4;
                s.Throttle = r[o + 8] * 4;
                if (!dualSense && !simple && n >= o + 24)
                {
                    s.GyroX = BitConverter.ToInt16(r, o + 12);
                    s.GyroY = BitConverter.ToInt16(r, o + 14);
                    s.GyroZ = BitConverter.ToInt16(r, o + 16);
                    s.AccelX = BitConverter.ToInt16(r, o + 18);
                    s.AccelY = BitConverter.ToInt16(r, o + 20);
                    s.AccelZ = BitConverter.ToInt16(r, o + 22);
                    if (n > o + 29) s.Battery = (byte)Math.Min(255, (r[o + 29] & 0x0F) * 25 + 5);
                }
            }

            ushort buttons = 0;
            if ((b0 & 0x10) != 0) buttons |= GamepadState.ButtonX;   // Square
            if ((b0 & 0x20) != 0) buttons |= GamepadState.ButtonA;   // Cross
            if ((b0 & 0x40) != 0) buttons |= GamepadState.ButtonB;   // Circle
            if ((b0 & 0x80) != 0) buttons |= GamepadState.ButtonY;   // Triangle
            if ((b1 & 0x01) != 0) buttons |= GamepadState.ButtonL1;
            if ((b1 & 0x02) != 0) buttons |= GamepadState.ButtonR1;
            if ((b1 & 0x04) != 0) buttons |= GamepadState.ButtonL2;
            if ((b1 & 0x08) != 0) buttons |= GamepadState.ButtonR2;
            if ((b1 & 0x40) != 0) buttons |= GamepadState.ButtonThumbL;
            if ((b1 & 0x80) != 0) buttons |= GamepadState.ButtonThumbR;
            s.Buttons = buttons;
            byte misc = 0;
            if ((b1 & 0x10) != 0) misc |= GamepadState.MiscSelect;    // Create / Share
            if ((b1 & 0x20) != 0) misc |= GamepadState.MiscStart;     // Options
            if ((b2 & 0x01) != 0) misc |= GamepadState.MiscSystem;    // PS
            if ((b2 & 0x02) != 0) misc |= GamepadState.MiscCapture;   // touchpad click
            s.MiscButtons = misc;
            s.Dpad = (b0 & 0x0F) switch
            {
                0 => GamepadState.DpadUp,
                1 => (byte)(GamepadState.DpadUp | GamepadState.DpadRight),
                2 => GamepadState.DpadRight,
                3 => (byte)(GamepadState.DpadDown | GamepadState.DpadRight),
                4 => GamepadState.DpadDown,
                5 => (byte)(GamepadState.DpadDown | GamepadState.DpadLeft),
                6 => GamepadState.DpadLeft,
                7 => (byte)(GamepadState.DpadUp | GamepadState.DpadLeft),
                _ => 0
            };
            s.Touch = (b2 & 0x02) != 0;
            return true;
        }

        private static int Stick(byte raw)
        {
            double v = (raw - 128) / 127.0;
            return GamepadState.Axis(GamepadInputs.Dead(v, 0.06));
        }

        public bool Read(GamepadState into)
        {
            lock (_gate)
            {
                if (_hasReport) into.CopyFrom(_latest);
            }

            return IsConnected;
        }

        // ================================================================ output (lightbar, player LEDs, rumble)

        public void SetFeedback(GamepadFeedback f, double simulationTime)
        {
            if (_writeStream == null || !IsConnected) return;
            bool rumble = f.Rumbling(simulationTime);
            var want = (f.ColorSet ? f.Red : (byte)0, f.ColorSet ? f.Green : (byte)0, f.ColorSet ? f.Blue : (byte)64,
                rumble ? f.WeakMotor : (byte)0, rumble ? f.StrongMotor : (byte)0, f.PlayerLeds);
            if (want == _lastOut) return;
            _lastOut = want;
            byte[] report = IsDualSense ? DualSenseOutput(want.Item1, want.Item2, want.Item3, want.Item4, want.Item5, want.Item6)
                : DualShock4Output(want.Item1, want.Item2, want.Item3, want.Item4, want.Item5);
            try
            {
                int len = Math.Max(report.Length, _outputLength);
                if (len != report.Length) Array.Resize(ref report, len);
                _writeStream.Write(report, 0, report.Length);
                _writeStream.Flush();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is UnauthorizedAccessException)
            {
                // Output is best effort; some Bluetooth stacks reject writes from a second handle.
            }
        }

        private byte[] DualSenseOutput(byte r, byte g, byte b, byte weak, byte strong, byte leds)
        {
            // Common block (47 bytes): valid flags, motors, …, lightbar setup, brightness, player LEDs, RGB.
            var common = new byte[47];
            common[0] = 0x03;           // compatible vibration + haptics select
            common[1] = 0x04 | 0x10;    // lightbar + player indicator control
            common[2] = weak;           // right (high frequency) motor
            common[3] = strong;         // left (low frequency) motor
            common[38] = 0x02;          // valid_flag2: lightbar setup
            common[41] = 0x02;          // lightbar setup: light out (take over from the boot animation)
            common[42] = 0x00;          // LED brightness: full
            common[43] = (byte)(leds & 0x1F);
            common[44] = r;
            common[45] = g;
            common[46] = b;
            if (!IsBluetooth)
            {
                var usb = new byte[48];
                usb[0] = 0x02;
                Array.Copy(common, 0, usb, 1, 47);
                return usb;
            }

            var bt = new byte[78];
            bt[0] = 0x31;
            bt[1] = (byte)((_seq++ & 0x0F) << 4);
            bt[2] = 0x10;               // DS_OUTPUT_TAG
            Array.Copy(common, 0, bt, 3, 47);
            Crc(bt);
            return bt;
        }

        private byte[] DualShock4Output(byte r, byte g, byte b, byte weak, byte strong)
        {
            if (!IsBluetooth)
            {
                var usb = new byte[32];
                usb[0] = 0x05;
                usb[1] = 0x01 | 0x02;    // motor + LED valid
                usb[4] = weak;
                usb[5] = strong;
                usb[6] = r;
                usb[7] = g;
                usb[8] = b;
                return usb;
            }

            var bt = new byte[78];
            bt[0] = 0x11;
            bt[1] = 0xC0;                // HID + CRC32
            bt[3] = 0x01 | 0x02;
            bt[6] = weak;
            bt[7] = strong;
            bt[8] = r;
            bt[9] = g;
            bt[10] = b;
            Crc(bt);
            return bt;
        }

        /// <summary>Bluetooth output reports end with CRC-32 over 0xA2 + report[0..73].</summary>
        private static void Crc(byte[] report)
        {
            uint crc = 0xFFFFFFFF;
            crc = Crc32Step(crc, 0xA2);
            for (int i = 0; i < report.Length - 4; i++) crc = Crc32Step(crc, report[i]);
            crc = ~crc;
            report[^4] = (byte)crc;
            report[^3] = (byte)(crc >> 8);
            report[^2] = (byte)(crc >> 16);
            report[^1] = (byte)(crc >> 24);
        }

        private static uint Crc32Step(uint crc, byte b)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            return crc;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Native.CancelIoEx(_read, IntPtr.Zero); } catch (Exception ex) when (ex is ObjectDisposedException || ex is EntryPointNotFoundException) { }
            _readStream.Dispose();
            _writeStream?.Dispose();
            _read.Dispose();
            _write?.Dispose();
        }

        // ================================================================ Win32

        private static class Native
        {
            public const int DigcfPresent = 0x02;
            public const int DigcfDeviceInterface = 0x10;
            public const uint GenericRead = 0x80000000;
            public const uint GenericWrite = 0x40000000;
            public const uint FileShareRead = 1;
            public const uint FileShareWrite = 2;
            public const uint OpenExisting = 3;
            public const int HidpStatusSuccess = 0x00110000;

            [StructLayout(LayoutKind.Sequential)]
            public struct SpDeviceInterfaceData
            {
                public int CbSize;
                public Guid InterfaceClassGuid;
                public int Flags;
                public IntPtr Reserved;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct HiddAttributes
            {
                public int Size;
                public ushort VendorID;
                public ushort ProductID;
                public ushort VersionNumber;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct HidpCaps
            {
                public ushort Usage;
                public ushort UsagePage;
                public ushort InputReportByteLength;
                public ushort OutputReportByteLength;
                public ushort FeatureReportByteLength;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
                public ushort NumberLinkCollectionNodes;
                public ushort NumberInputButtonCaps;
                public ushort NumberInputValueCaps;
                public ushort NumberInputDataIndices;
                public ushort NumberOutputButtonCaps;
                public ushort NumberOutputValueCaps;
                public ushort NumberOutputDataIndices;
                public ushort NumberFeatureButtonCaps;
                public ushort NumberFeatureValueCaps;
                public ushort NumberFeatureDataIndices;
            }

            [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
            [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);
            [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
            [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] public static extern bool HidD_FreePreparsedData(IntPtr data);
            [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
            [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, int length);

            [DllImport("setupapi.dll", SetLastError = true)] public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);
            [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SpDeviceInterfaceData data);
            [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr info);
            [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
        }
    }
}
