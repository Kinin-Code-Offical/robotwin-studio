using System;
using System.Text;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    /// <summary>
    /// Radio requests of the ESP32 HAL (ABI 2, rw_host_api.radio_request). Values and
    /// payload layouts mirror native/esp32hal/rw_host_api.h (little endian, packed).
    /// </summary>
    public static class Esp32RadioOp
    {
        /// <summary>Returned by the host when it has no radio medium: the HAL falls back to its stand-alone stub.</summary>
        public const int NotSupported = -1;

        public const int Reset = 0x01;            // in: -                         out: -
        public const int Info = 0x02;             // in: -                         out: u8 mac[6]

        public const int WifiMode = 0x10;         // in: i32 mode
        public const int WifiBegin = 0x11;        // in: char ssid[33], char pass[65]
        public const int WifiStatus = 0x12;       // out: i32 status, i32 rssi, u32 ip, u32 gw, i32 channel, u8 bssid[6]
        public const int WifiDisconnect = 0x13;
        public const int WifiScan = 0x14;         // out: i32 n, n × { char ssid[33], i8 rssi, u8 channel, u8 auth, u8 bssid[6] } (42 bytes)
        public const int WifiSoftAp = 0x15;       // in: char ssid[33], char pass[65], i32 channel, i32 max
        public const int WifiSoftApStop = 0x16;
        public const int WifiSoftApInfo = 0x17;   // out: i32 stations

        public const int UdpBind = 0x20;          // in: u16 port, u16 on
        public const int UdpSend = 0x21;          // in: u32 ip, u16 dport, u16 sport, payload    ret: receivers
        public const int UdpRecv = 0x22;          // in: u16 port   out: u32 ip, u16 sport, u16 len, payload  ret: bytes written (0 = none)
        public const int TcpListen = 0x23;        // in: u16 port, u16 on
        public const int TcpConnect = 0x24;       // in: u32 ip, u16 port     ret: id > 0 or < 0
        public const int TcpAccept = 0x25;        // in: u16 port   out: i32 id, u32 ip, u16 port
        public const int TcpSend = 0x26;          // in: i32 id, payload      ret: bytes or -1
        public const int TcpRecv = 0x27;          // in: i32 id   out: payload ret: bytes, 0, -1 closed
        public const int TcpClose = 0x28;         // in: i32 id
        public const int TcpState = 0x29;         // in: i32 id   out: i32 connected, i32 available, u32 ip, u16 port

        public const int BleInit = 0x30;          // in: name (utf-8)
        public const int BleAdvertise = 0x31;     // in: i32 on, i32 interval_ms, char name[32], char uuid[40], u8 mfr_len, u8 mfr[31]
        public const int BleScan = 0x32;          // in: u64 from_ns, u64 to_ns   out: i32 n, n × rec (112 bytes)
        public const int BleAddChar = 0x33;       // in: char svc[40], char chr[40], u32 props   ret: handle
        public const int BleSetValue = 0x34;      // in: i32 handle, i32 notify, payload
        public const int BleConnect = 0x35;       // in: u8 addr[6]   ret: connection id or -1
        public const int BleFindChar = 0x36;      // in: i32 conn, char svc[40], char chr[40]   out: i32 handle, u32 props
        public const int BleSubscribe = 0x37;     // in: i32 conn, i32 handle, i32 on
        public const int BleRead = 0x38;          // in: i32 conn, i32 handle   out: payload   ret: bytes or -1
        public const int BleWrite = 0x39;         // in: i32 conn, i32 handle, payload
        public const int BleDisconnect = 0x3A;    // in: i32 conn
        public const int BlePoll = 0x3B;          // out: i32 kind, i32 conn, i32 handle, u8 addr[6], u16 len, payload   ret: 1 event / 0 none

        public const int PadEnable = 0x40;        // in: i32 allow_new
        public const int PadPoll = 0x41;          // out: 4 × pad record (96 bytes)
        public const int PadOutput = 0x42;        // in: i32 index, i32 what, u8 r, g, b, leds, weak, strong, u16 pad, u32 duration_ms
        public const int PadForget = 0x43;
        public const int PadDisconnect = 0x44;    // in: i32 index

        // [espnow] ESP-NOW on the Wi-Fi radio.
        public const int EspNowInit = 0x50;       // in: i32 on   ret: 0, or −2 while Wi-Fi is off
        public const int EspNowSend = 0x51;       // in: u8 mac[6], payload (≤ 250)   ret: 0, or −2 when not initialised
        public const int EspNowPoll = 0x52;       // out: u8 kind (1 received, 2 sent), u8 status (0 ok, 1 fail), i8 rssi, u8 0, u8 mac[6], u16 len, payload   ret: 1 / 0

        // SPI master (handled by the board itself, not the radio medium).
        public const int SpiBegin = 0x60;         // in: i32 bus, i32 sck, i32 miso, i32 mosi, i32 ss   ret: 0
        public const int SpiTransfer = 0x61;      // in: i32 bus, u32 hz, u8 mode, u8 lsb_first, u16 n, n bytes   out: n bytes   ret: n
        public const int SpiEnd = 0x62;           // in: i32 bus

        // [neopixel] A frame an RMT TX pin sent, decoded to WS2812 bytes (handled by the board: its LED strips).
        public const int LedStripWrite = 0x63;    // in: i32 gpio, n bytes   ret: n

        public const int PadRecordSize = 96;
        public const int BleScanRecordSize = 112;
        public const int WifiScanRecordSize = 42;
    }

    /// <summary>The radio side of an ESP32 co-simulation (implemented by the world's radio medium).</summary>
    public interface IEsp32Radio
    {
        /// <summary>
        /// Handles one request made by the sketch at simulated <paramref name="time"/> (s).
        /// Runs on the sketch thread while the host waits in rw_run_until. Return
        /// <see cref="Esp32RadioOp.NotSupported"/> for requests the medium does not handle.
        /// </summary>
        int Request(int op, byte[] input, int inputLength, byte[] output, int outputCapacity, double time);
    }

    /// <summary>Little-endian helpers for the packed radio payloads.</summary>
    public static class RadioPayload
    {
        public static int I32(byte[] b, int at) => b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);

        public static uint U32(byte[] b, int at) => (uint)I32(b, at);

        public static ushort U16(byte[] b, int at) => (ushort)(b[at] | (b[at + 1] << 8));

        public static ulong U64(byte[] b, int at) => U32(b, at) | ((ulong)U32(b, at + 4) << 32);

        public static void Put(byte[] b, int at, int v)
        {
            b[at] = (byte)v;
            b[at + 1] = (byte)(v >> 8);
            b[at + 2] = (byte)(v >> 16);
            b[at + 3] = (byte)(v >> 24);
        }

        public static void Put(byte[] b, int at, uint v) => Put(b, at, (int)v);

        public static void Put16(byte[] b, int at, int v)
        {
            b[at] = (byte)v;
            b[at + 1] = (byte)(v >> 8);
        }

        /// <summary>NUL-terminated UTF-8 string in a fixed field.</summary>
        public static string Str(byte[] b, int at, int size)
        {
            int n = 0;
            while (n < size && at + n < b.Length && b[at + n] != 0) n++;
            return Encoding.UTF8.GetString(b, at, n);
        }

        public static void PutStr(byte[] b, int at, int size, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
            int n = Math.Min(bytes.Length, size - 1);
            Array.Copy(bytes, 0, b, at, n);
            for (int i = n; i < size; i++) b[at + i] = 0;
        }
    }
}
