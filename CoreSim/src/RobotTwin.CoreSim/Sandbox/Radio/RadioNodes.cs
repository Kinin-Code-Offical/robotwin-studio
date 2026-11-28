using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    public enum RadioNodeKind
    {
        /// <summary>ESP32 board: Wi-Fi station/AP, BLE, Bluetooth Classic host for game controllers.</summary>
        Esp32,

        /// <summary>Wireless game controller (DualSense, DualShock 4, Xbox pad).</summary>
        Gamepad,

        /// <summary>Wi-Fi router / access point.</summary>
        AccessPoint,

        /// <summary>BLE advertising tag (iBeacon style).</summary>
        Beacon
    }

    /// <summary>Packet counters of a node and a short history for packets/second.</summary>
    public sealed class RadioNodeStats
    {
        private readonly Queue<double> _tx = new Queue<double>();
        private readonly Queue<double> _rx = new Queue<double>();

        public long TxPackets { get; private set; }
        public long RxPackets { get; private set; }
        public long LostPackets { get; private set; }
        public long TxBytes { get; private set; }

        internal void Sent(double t, int bytes)
        {
            TxPackets++;
            TxBytes += Math.Max(0, bytes);
            _tx.Enqueue(t);
            Trim(_tx, t);
        }

        internal void Received(double t)
        {
            RxPackets++;
            _rx.Enqueue(t);
            Trim(_rx, t);
        }

        internal void Lost() => LostPackets++;

        private static void Trim(Queue<double> q, double t)
        {
            while (q.Count > 0 && q.Peek() < t - 1.0) q.Dequeue();
            while (q.Count > 4000) q.Dequeue();
        }

        /// <summary>Packets sent during the last second before <paramref name="now"/>.</summary>
        public double TxRate(double now) => _tx.Count(t => t >= now - 1.0 && t <= now + 1e-9);

        public double RxRate(double now) => _rx.Count(t => t >= now - 1.0 && t <= now + 1e-9);
    }

    /// <summary>A radio in the world.</summary>
    public class RadioNode
    {
        public RadioNode(string id, string name, RadioNodeKind kind, byte[] mac)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Mac = mac.Length == 6 ? (byte[])mac.Clone() : throw new ArgumentException("MAC must have 6 bytes.", nameof(mac));
        }

        /// <summary>World object id.</summary>
        public string Id { get; }

        public string Name { get; set; }
        public RadioNodeKind Kind { get; }

        /// <summary>Base MAC address (Wi-Fi station). ESP32: AP = +1, Bluetooth = +2.</summary>
        public byte[] Mac { get; }

        public string MacText => RadioMedium.FormatMac(Mac);

        /// <summary>Antenna position (m, world frame).</summary>
        public Func<Vec3> Position { get; set; } = () => Vec3.Zero;

        /// <summary>Powered and running (an ESP32 that lost supply or crashed is silent).</summary>
        public Func<bool> IsOn { get; set; } = () => true;

        public double TxPowerDbm { get; set; } = 3.0;
        public double AntennaGainDbi { get; set; } = 2.0;

        /// <summary>Channel last used (Wi-Fi 1…13, BLE 37…39 advertising / 0…36 data, Bluetooth hop set 0…78).</summary>
        public int Channel { get; set; }

        public RadioNodeStats Stats { get; } = new RadioNodeStats();

        /// <summary>Address shown for Bluetooth (BR/EDR and BLE).</summary>
        public virtual byte[] BluetoothMac => Mac;

        public string BluetoothMacText => RadioMedium.FormatMac(BluetoothMac);

        /// <summary>Human readable state for the RF panel ("Reklam yayını", "Bağlı: Router" …).</summary>
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>A UDP datagram or a chunk of a stream in flight.</summary>
    public sealed class NetPacket
    {
        public uint SrcIp;
        public ushort SrcPort;
        public uint DstIp;
        public ushort DstPort;
        public byte[] Data = Array.Empty<byte>();
        public double ArriveAt;
    }

    /// <summary>A BLE GATT characteristic of a server.</summary>
    public sealed class BleCharacteristic
    {
        public int Handle;
        public string Service = string.Empty;
        public string Uuid = string.Empty;
        public uint Properties;
        public byte[] Value = Array.Empty<byte>();
    }

    /// <summary>An event queued for an ESP32's BLE stack (delivered by RW_RADIO_BLE_POLL).</summary>
    public sealed class BleEvent
    {
        public const int Notify = 1;
        public const int Write = 2;
        public const int Connect = 3;
        public const int Disconnect = 4;

        public int Kind;
        public int Connection;
        public int Handle;
        public byte[] Peer = new byte[6];
        public byte[] Data = Array.Empty<byte>();
        public double At;
    }

    public sealed class BleConnection
    {
        public int Id;
        public Esp32RadioNode Client = null!;
        public RadioNode Server = null!;
        public readonly HashSet<int> Subscribed = new HashSet<int>();
        public double BadSince = double.NaN;
        public double IntervalS = 0.015;
        public bool Open = true;
    }

    /// <summary>Stream ("TCP-like") connection between two Wi-Fi nodes: reliable, ordered, with link latency and retransmission delay.</summary>
    public sealed class StreamConnection
    {
        public int Id;
        public Esp32RadioNode A = null!;
        public Esp32RadioNode B = null!;
        public ushort PortA;
        public ushort PortB;
        public bool Accepted;
        public bool Open = true;
        public readonly Queue<NetPacket> ToA = new Queue<NetPacket>();
        public readonly Queue<NetPacket> ToB = new Queue<NetPacket>();
        public readonly List<byte> BufferA = new List<byte>();
        public readonly List<byte> BufferB = new List<byte>();
    }

    /// <summary>Wi-Fi network a station can join: a router, an ESP32 soft-AP or the implicit "virtual home network".</summary>
    public sealed class WifiNetwork
    {
        public string Ssid = string.Empty;
        public string Password = string.Empty;
        public int Channel = 6;
        public uint Gateway;
        public RadioNode? Node;   // null = implicit network (no position: fixed good signal)
        public bool Implicit;
        public int NextHost = 100;
        public int MaxStations = 10;
    }

    /// <summary>One game-controller slot of an ESP32 running Bluepad32.</summary>
    public sealed class PadSlot
    {
        public GamepadNode? Pad;
        public readonly GamepadState Seen = new GamepadState();
        public uint Sequence;
        public double LastReport;
    }

    /// <summary>ESP32 radio: Wi-Fi station/soft-AP + UDP/stream sockets, BLE advertiser/scanner/GATT, Bluepad32 controller host.</summary>
    public sealed class Esp32RadioNode : RadioNode
    {
        public Esp32RadioNode(string id, string name, byte[] mac) : base(id, name, RadioNodeKind.Esp32, mac)
        {
            TxPowerDbm = 19.5;   // Wi-Fi default (WIFI_POWER_19_5dBm); BLE uses BleTxPowerDbm
        }

        public override byte[] BluetoothMac
        {
            get
            {
                var m = (byte[])Mac.Clone();
                m[5] = (byte)(m[5] + 2);
                return m;
            }
        }

        public byte[] ApMac
        {
            get
            {
                var m = (byte[])Mac.Clone();
                m[5] = (byte)(m[5] + 1);
                return m;
            }
        }

        public double BleTxPowerDbm { get; set; } = 3.0;
        public double BtTxPowerDbm { get; set; } = 4.0;

        // ---- Wi-Fi station
        public int WifiMode;
        public string? StaSsid;
        public string StaPassword = string.Empty;
        public double StaBeganAt = double.NaN;
        public WifiNetwork? StaNetwork;
        public uint StaIp;
        public int StaStatus = 0;            // wl_status_t
        public double StaLostSince = double.NaN;

        // ---- Wi-Fi soft-AP
        public WifiNetwork? SoftAp;

        // ---- sockets
        public readonly Dictionary<ushort, Queue<NetPacket>> Udp = new Dictionary<ushort, Queue<NetPacket>>();
        public readonly HashSet<ushort> Listening = new HashSet<ushort>();
        public readonly Queue<int> PendingAccepts = new Queue<int>();

        // ---- BLE
        public bool BleOn;
        public string BleName = string.Empty;
        public bool Advertising;
        public double AdvIntervalS = 0.1;
        public string AdvName = string.Empty;
        public string AdvUuid = string.Empty;
        public byte[] AdvManufacturer = Array.Empty<byte>();
        public readonly List<BleCharacteristic> Characteristics = new List<BleCharacteristic>();
        public readonly List<BleEvent> BleEvents = new List<BleEvent>();
        public bool Scanning;
        public double ScanUntil;

        // ---- [mdns] the fast mode's ESPmDNS: the name it answers to (without .local) and its services
        public string? MdnsHost;
        public readonly List<MdnsService> MdnsServices = new List<MdnsService>();

        // ---- [espnow] ESP-NOW
        public bool EspNowOn;
        public readonly Queue<EspNowFrame> EspNowInbox = new Queue<EspNowFrame>();
        public readonly Queue<EspNowSent> EspNowSentQueue = new Queue<EspNowSent>();

        // ---- Bluepad32
        public bool PadEnabled;
        public bool PadAllowNew = true;
        public readonly PadSlot[] PadSlots = { new PadSlot(), new PadSlot(), new PadSlot(), new PadSlot() };

        internal void ResetRadio()
        {
            WifiMode = 0;
            MdnsHost = null;
            MdnsServices.Clear();
            EspNowOn = false;
            EspNowInbox.Clear();
            EspNowSentQueue.Clear();
            StaSsid = null;
            StaPassword = string.Empty;
            StaBeganAt = double.NaN;
            StaNetwork = null;
            StaIp = 0;
            StaStatus = 0;
            StaLostSince = double.NaN;
            SoftAp = null;
            Udp.Clear();
            Listening.Clear();
            PendingAccepts.Clear();
            BleOn = false;
            BleName = string.Empty;
            Advertising = false;
            AdvName = string.Empty;
            AdvUuid = string.Empty;
            AdvManufacturer = Array.Empty<byte>();
            Characteristics.Clear();
            BleEvents.Clear();
            Scanning = false;
            PadEnabled = false;
            PadAllowNew = true;
            foreach (var s in PadSlots)
            {
                s.Pad = null;
                s.Seen.Clear();
                s.Sequence = 0;
            }
        }
    }

    /// <summary>A wireless game controller. Its input comes from the Studio (real pad, virtual pad) or a test.</summary>
    public sealed class GamepadNode : RadioNode
    {
        public GamepadNode(string id, string name, byte[] mac, GamepadModel model) : base(id, name, RadioNodeKind.Gamepad, mac)
        {
            Model = model;
            TxPowerDbm = 4.0;          // Bluetooth class 2
            AntennaGainDbi = -3.0;      // small antenna inside a hand-held plastic shell
        }

        public GamepadModel Model { get; set; }

        /// <summary>Current input (written by the input source every frame).</summary>
        public GamepadState Input { get; } = new GamepadState();

        /// <summary>Lightbar / rumble requested by the host.</summary>
        public GamepadFeedback Feedback { get; } = new GamepadFeedback();

        /// <summary>Switched on (PS button). Off = no radio.</summary>
        public bool PoweredOn { get; set; } = true;

        /// <summary>Discoverable (PS + Create held on a DualSense, Share + PS on a DualShock 4).</summary>
        public bool PairingMode { get; set; }

        /// <summary>Bluetooth address of the host it is bonded to (reconnects to it automatically).</summary>
        public string? BondedHost { get; set; }

        public Esp32RadioNode? Host { get; internal set; }
        public Esp32RadioNode? Connecting { get; internal set; }
        internal double ConnectDoneAt = double.NaN;
        internal bool ConnectIsPairing;
        internal double BadSince = double.NaN;
        internal double LastRssi = double.NaN;

        public bool Connected => Host != null;

        /// <summary>Report interval over the air (DualSense/DS4 over Bluetooth ≈ 250 Hz).</summary>
        public double ReportIntervalS { get; set; } = 0.004;

        public string ModelName => Model switch
        {
            GamepadModel.DualSense => "DualSense",
            GamepadModel.DualShock4 => "DualShock 4",
            GamepadModel.Xbox => "Xbox Wireless",
            _ => "Gamepad"
        };
    }

    /// <summary>A Wi-Fi router.</summary>
    public sealed class AccessPointNode : RadioNode
    {
        public AccessPointNode(string id, string name, byte[] mac, WifiNetwork network) : base(id, name, RadioNodeKind.AccessPoint, mac)
        {
            Network = network;
            network.Node = this;
            TxPowerDbm = 20.0;
            AntennaGainDbi = 3.0;
            Channel = network.Channel;
        }

        public WifiNetwork Network { get; }
    }

    /// <summary>A BLE advertising tag.</summary>
    public sealed class BeaconNode : RadioNode
    {
        public BeaconNode(string id, string name, byte[] mac) : base(id, name, RadioNodeKind.Beacon, mac)
        {
            TxPowerDbm = 0.0;
            AntennaGainDbi = 0.0;
        }

        public string AdvName { get; set; } = "RW-Beacon";
        public string AdvUuid { get; set; } = string.Empty;
        public byte[] Manufacturer { get; set; } = Array.Empty<byte>();
        public double AdvIntervalS { get; set; } = 0.1;
    }

    /// <summary>What the 3D view draws: one burst of radio energy leaving a node.</summary>
    public readonly struct RadioEmission
    {
        public RadioEmission(string nodeId, RadioTech tech, double time, Vec3 position, double powerDbm)
        {
            NodeId = nodeId;
            Tech = tech;
            Time = time;
            Position = position;
            PowerDbm = powerDbm;
        }

        public string NodeId { get; }
        public RadioTech Tech { get; }
        public double Time { get; }
        public Vec3 Position { get; }
        public double PowerDbm { get; }
    }

    /// <summary>An active association between two radios (for the link lines and the RF panel).</summary>
    public readonly struct RadioLink
    {
        public RadioLink(RadioNode a, RadioNode b, RadioTech tech, double rssiDbm, string label)
        {
            A = a;
            B = b;
            Tech = tech;
            RssiDbm = rssiDbm;
            Label = label;
        }

        public RadioNode A { get; }
        public RadioNode B { get; }
        public RadioTech Tech { get; }
        public double RssiDbm { get; }
        public string Label { get; }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0} ⇄ {1} {2} {3:0} dBm", A.Name, B.Name, Tech, RssiDbm);
    }

    /// <summary>Something worth telling the user (pairing, connection drops, association).</summary>
    public sealed class RadioEvent
    {
        public RadioEvent(double time, string nodeId, string message, bool warning = false)
        {
            Time = time;
            NodeId = nodeId;
            Message = message;
            Warning = warning;
        }

        public double Time { get; }
        public string NodeId { get; }
        public string Message { get; }
        public bool Warning { get; }
    }
}
