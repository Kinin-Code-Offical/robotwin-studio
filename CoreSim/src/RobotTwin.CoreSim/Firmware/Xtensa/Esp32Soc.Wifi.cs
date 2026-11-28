using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Firmware.Debug;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    public sealed partial class Esp32Soc
    {
        // ================================================================ Wi-Fi
        //
        // The radio and its MAC are Espressif's closed driver (libnet80211, libpp, libphy), which programs registers no
        // document describes; the emulator serves the driver's API instead, where IDF's open code meets it: esp_wifi_init,
        // set_mode/config, start, connect, scan, the MAC, and the frame boundary (esp_wifi_internal_tx out, the receive
        // callback in). Everything above runs as built: esp_netif, the event loop, lwIP with its DHCP client, ARP, TCP and
        // UDP, Arduino's WiFi library. The events the driver posts (STA_START, CONNECTED, DISCONNECTED, SCAN_DONE …) and
        // the frames it receives reach the firmware through calls the emulator makes at an idle point (HostCall).

        /// <summary>The world's Wi-Fi for this chip; null when nothing is in range (a connect finds no network).</summary>
        public IEsp32WifiNetwork? WifiNetwork
        {
            get => _wifiNet;
            set
            {
                _wifiNet = value;
                _wifiStack = value == null ? null : new Esp32WifiStack(value) { Deliver = (ifx, f) => _rxQueue.Enqueue((ifx, f)), Log = s => WifiLog?.Invoke(s) };
            }
        }

        /// <summary>What the Wi-Fi model reports (connected, lost, frames dropped).</summary>
        public event Action<string>? WifiLog;

        /// <summary>The station's state as the driver sees it: 0 idle, 1 connecting, 2 connected.</summary>
        public int WifiStationState => _staState;

        /// <summary>The chip's own access point is up (WiFi.softAP).</summary>
        public bool SoftApUp => _apUp;

        private IEsp32WifiNetwork? _wifiNet;
        private Esp32WifiStack? _wifiStack;
        private uint _wifiEventBase, _eventPost, _malloc;
        private bool _wifiInit, _wifiStarted;
        private int _wifiMode, _staState;
        private double _connectAt, _wifiNextService, _scanDoneAt = double.NaN;
        private readonly byte[][] _wifiConfig = { new byte[184], new byte[184] };
        private readonly uint[] _rxCallback = new uint[2];
        private List<(string Ssid, int Rssi, int Channel, int Auth, byte[] Bssid)> _scan = new List<(string, int, int, int, byte[])>();
        private readonly Queue<(int Ifx, byte[] Frame)> _rxQueue = new Queue<(int, byte[])>();
        private bool _apUp;
        private List<(byte[] Mac, uint Ip, int Rssi)> _apStations = new List<(byte[], uint, int)>();
        private readonly List<(uint Id, byte[] Data)> _pendingEvents = new List<(uint, byte[])>();

        // Receive buffers and event data in the firmware's heap, allocated once at esp_wifi_init (as the driver's own).
        private const int RxSlots = 10, RxSlotSize = 1600, EventSlots = 8, EventSlotSize = 64;
        private uint _wifiPool;
        private bool _poolRequested;
        private readonly bool[] _rxBusy = new bool[RxSlots];
        private readonly bool[] _eventBusy = new bool[EventSlots];

        private const uint EspOk = 0, ErrWifiNotStarted = 0x3002, ErrWifiNotConnect = 0x300F;
        private const uint StaStart = 2, StaStop = 3, StaConnected = 4, StaDisconnected = 5, ScanDone = 1, ApStart = 12, ApStop = 13, ApStaConnected = 14, ApStaDisconnected = 15;

        /// <summary>The soft-AP's address (esp_netif's default, the medium's AP gateway).</summary>
        private const uint SoftApIp = 0x0104A8C0;   // 192.168.4.1

        /// <summary>IDF's open code around the driver, left to run.</summary>
        private static readonly HashSet<string> WifiOpenCode = new HashSet<string>
        {
            "esp_wifi_create_if_driver", "esp_wifi_destroy_if_driver", "esp_wifi_get_if_mac", "esp_wifi_register_if_rxcb",
            "esp_wifi_is_if_ready_when_started", "esp_wifi_clear_default_wifi_driver_and_handlers", "esp_wifi_set_default_wifi_sta_handlers",
            "esp_wifi_set_default_wifi_ap_handlers", "esp_wifi_set_default_wifi_nan_handlers", "esp_wifi_power_domain_on",
            "esp_wifi_power_domain_off", "esp_wifi_bt_power_domain_on", "esp_wifi_bt_power_domain_off", "esp_wifi_register_wpa3_cb",
            "esp_wifi_register_wpa3_ap_cb", "esp_wifi_register_owe_cb", "esp_wifi_build_rsnxe",
        };

        private void BindWifi(ElfImage elf)
        {
            _wifiInit = _wifiStarted = _apUp = false;
            _wifiMode = _staState = 0;
            _apStations = new List<(byte[], uint, int)>();
            _wifiPool = 0;
            _poolRequested = false;
            _rxQueue.Clear();
            _pendingEvents.Clear();
            Array.Clear(_rxBusy, 0, _rxBusy.Length);
            Array.Clear(_eventBusy, 0, _eventBusy.Length);
            Array.Clear(_rxCallback, 0, _rxCallback.Length);
            _wifiStack?.LinkDown(0);
            _wifiStack?.LinkDown(1);
            var code = new Dictionary<string, uint>();
            foreach (var symbol in elf.Symbols)
            {
                if (symbol.Value >= IramBase && symbol.Value < IromEnd && !code.ContainsKey(symbol.Name)) code[symbol.Name] = symbol.Value;
                if (symbol.Name == "WIFI_EVENT") _wifiEventBase = symbol.Value;
            }

            if (!code.TryGetValue("esp_wifi_init", out _) || !code.TryGetValue("esp_event_post", out _eventPost) || !code.TryGetValue("malloc", out _malloc)) return;

            var handlers = new Dictionary<string, Func<XtensaCore, bool>>
            {
                ["esp_wifi_init"] = c =>
                {
                    _wifiInit = true;
                    if (!_poolRequested)
                    {
                        _poolRequested = true;
                        CallFirmware(_malloc, p => _wifiPool = p, RxSlots * RxSlotSize + EventSlots * EventSlotSize);
                    }

                    return Return(c, EspOk);
                },
                ["esp_wifi_deinit"] = c =>
                {
                    _wifiInit = _wifiStarted = false;
                    return Return(c, EspOk);
                },
                ["esp_wifi_set_mode"] = c =>
                {
                    _wifiMode = (int)c.Argument(0);
                    _wifiNet?.Mode(_wifiMode);
                    if ((_wifiMode & 2) == 0 && _apUp) StopSoftAp(post: true);
                    else if ((_wifiMode & 2) != 0 && _wifiStarted && !_apUp) StartSoftAp();
                    if ((_wifiMode & 1) == 0 && _staState != 0) StationDown(8, post: true);
                    return Return(c, EspOk);
                },
                ["esp_wifi_get_mode"] = c =>
                {
                    if (c.Argument(0) != 0) Write32(c.Argument(0), (uint)_wifiMode);
                    return Return(c, EspOk);
                },
                ["esp_wifi_start"] = c =>
                {
                    if (!_wifiStarted)
                    {
                        _wifiStarted = true;
                        if ((_wifiMode & 1) != 0) PostWifiEvent(StaStart, Array.Empty<byte>());
                        if ((_wifiMode & 2) != 0) StartSoftAp();
                    }

                    return Return(c, EspOk);
                },
                ["esp_wifi_stop"] = c =>
                {
                    if (_wifiStarted)
                    {
                        StationDown(8, post: true);
                        _wifiStarted = false;
                        if ((_wifiMode & 1) != 0) PostWifiEvent(StaStop, Array.Empty<byte>());
                        if (_apUp) StopSoftAp(post: true);
                    }

                    return Return(c, EspOk);
                },
                ["esp_wifi_set_config"] = c =>
                {
                    int ifx = (int)c.Argument(0) & 1;
                    for (int i = 0; i < 184; i++) _wifiConfig[ifx][i] = Read8(c.Argument(1) + (uint)i);
                    // A running soft-AP takes its new config at once (Arduino starts the AP, then sets its SSID).
                    if (ifx == 1 && _apUp) ApplySoftApConfig();
                    return Return(c, EspOk);
                },
                ["esp_wifi_get_config"] = c =>
                {
                    int ifx = (int)c.Argument(0) & 1;
                    for (int i = 0; i < 184; i++) Write8(c.Argument(1) + (uint)i, _wifiConfig[ifx][i]);
                    return Return(c, EspOk);
                },
                ["esp_wifi_connect"] = c =>
                {
                    if (!_wifiStarted) return Return(c, ErrWifiNotStarted);
                    string ssid = CString(_wifiConfig[0], 0, 32), password = CString(_wifiConfig[0], 32, 64);
                    if (_staState == 2) StationDown(8, post: false);
                    _wifiNet?.Begin(ssid, password, Seconds);
                    _staState = 1;
                    _connectAt = Seconds;
                    return Return(c, EspOk);
                },
                ["esp_wifi_disconnect"] = c =>
                {
                    if (_staState != 0)
                    {
                        _wifiNet?.Disconnect();
                        StationDown(8, post: true);   // WIFI_REASON_ASSOC_LEAVE
                    }

                    return Return(c, EspOk);
                },
                ["esp_wifi_sta_get_ap_info"] = c =>
                {
                    if (_staState != 2 || _wifiNet == null) return Return(c, ErrWifiNotConnect);
                    var s = _wifiNet.Status(Seconds);
                    WriteApRecord(c.Argument(0), CString(_wifiConfig[0], 0, 32), s.Rssi, s.Channel, CString(_wifiConfig[0], 32, 64).Length > 0 ? 3 : 0, s.Bssid);
                    return Return(c, EspOk);
                },
                ["esp_wifi_get_mac"] = c =>
                {
                    var mac = (byte[])(_wifiNet?.Mac ?? new byte[] { 0x24, 0x0A, 0xC4, 0x5A, 0x7E, 0x11 }).Clone();
                    if ((c.Argument(0) & 1) != 0) mac[5]++;
                    for (int i = 0; i < 6; i++) Write8(c.Argument(1) + (uint)i, mac[i]);
                    return Return(c, EspOk);
                },
                ["esp_wifi_internal_reg_rxcb"] = c =>
                {
                    _rxCallback[c.Argument(0) & 1] = c.Argument(1);
                    return Return(c, EspOk);
                },
                ["esp_wifi_internal_tx"] = c =>
                {
                    int ifx = (int)c.Argument(0);
                    uint buffer = c.Argument(1);
                    int length = (int)(c.Argument(2) & 0xFFFF);
                    if (_wifiStack != null && ((ifx == 0 && _staState == 2) || (ifx == 1 && _apUp)))
                    {
                        var frame = new byte[length];
                        for (int i = 0; i < length; i++) frame[i] = Read8(buffer + (uint)i);
                        _wifiStack.Transmit(ifx, frame, Seconds);
                    }

                    return Return(c, EspOk);
                },
                ["esp_wifi_internal_free_rx_buffer"] = c =>
                {
                    uint at = c.Argument(0);
                    if (_wifiPool != 0 && at >= _wifiPool && at < _wifiPool + RxSlots * RxSlotSize) _rxBusy[(at - _wifiPool) / RxSlotSize] = false;
                    return Return(c, 0);
                },
                ["esp_wifi_scan_start"] = c =>
                {
                    if (!_wifiStarted) return Return(c, ErrWifiNotStarted);
                    _scan = _wifiNet?.Scan(Seconds) ?? new List<(string, int, int, int, byte[])>();
                    _scanDoneAt = Seconds + 13 * 0.12;   // an active scan: ~120 ms on each of 13 channels
                    return Return(c, EspOk);
                },
                ["esp_wifi_scan_get_ap_num"] = c =>
                {
                    Write16(c.Argument(0), (ushort)_scan.Count);
                    return Return(c, EspOk);
                },
                ["esp_wifi_scan_get_ap_records"] = c =>
                {
                    int n = Math.Min(Read16(c.Argument(0)), _scan.Count);
                    for (int k = 0; k < n; k++)
                    {
                        var r = _scan[k];
                        WriteApRecord(c.Argument(1) + (uint)(k * 92), r.Ssid, r.Rssi, r.Channel, r.Auth, r.Bssid);
                    }

                    Write16(c.Argument(0), (ushort)n);
                    _scan = new List<(string, int, int, int, byte[])>();
                    return Return(c, EspOk);
                },
                ["esp_wifi_clear_ap_list"] = c =>
                {
                    _scan = new List<(string, int, int, int, byte[])>();
                    return Return(c, EspOk);
                },
                ["esp_wifi_ap_get_sta_list"] = c =>
                {
                    uint list = c.Argument(0);
                    for (int i = 0; i < 184; i++) Write8(list + (uint)i, 0);
                    int n = Math.Min(15, _apStations.Count);
                    for (int k = 0; k < n; k++)
                    {
                        uint at = list + (uint)(k * 12);
                        for (int i = 0; i < 6; i++) Write8(at + (uint)i, _apStations[k].Mac[i]);
                        Write8(at + 6, (byte)(sbyte)Math.Max(-127, Math.Min(0, _apStations[k].Rssi)));
                        Write8(at + 8, 0x07);   // phy_11b | 11g | 11n
                    }

                    Write32(list + 180, (uint)n);
                    return Return(c, EspOk);
                },
                ["esp_wifi_get_protocol"] = c =>
                {
                    if (c.Argument(1) != 0) Write8(c.Argument(1), 7);   // 802.11 b/g/n
                    return Return(c, EspOk);
                },
            };

            AddEspNow(handlers);   // [espnow] Esp32Soc.EspNow.cs
            foreach (var (name, address) in code)
            {
                Func<XtensaCore, bool>? serve = handlers.TryGetValue(name, out var h) ? h
                    : (name.StartsWith("esp_wifi_", StringComparison.Ordinal) && !WifiOpenCode.Contains(name)) || name.StartsWith("esp_now_", StringComparison.Ordinal) ? c => Return(c, EspOk)
                    : null;
                if (serve == null) continue;
                foreach (var core in new[] { Pro, App }) core.AddAppTrap(address, serve);
            }

            // lwIP binding a port: the world delivers to it from now on (the binding itself runs as built).
            foreach (var (name, tcp) in new[] { ("udp_bind", false), ("tcp_bind", true) })
            {
                if (!code.TryGetValue(name, out uint at)) continue;
                foreach (var core in new[] { Pro, App })
                    core.AddAppTrap(at, c =>
                    {
                        _wifiStack?.Bound(tcp, (ushort)c.Argument(2));
                        return false;
                    });
            }
        }

        private static string CString(byte[] b, int at, int max)
        {
            int n = 0;
            while (n < max && b[at + n] != 0) n++;
            return Encoding.UTF8.GetString(b, at, n);
        }

        private void WriteApRecord(uint at, string ssid, int rssi, int channel, int auth, byte[] bssid)
        {
            for (int i = 0; i < 92; i++) Write8(at + (uint)i, 0);
            for (int i = 0; i < 6; i++) Write8(at + (uint)i, bssid[i]);
            var name = Encoding.UTF8.GetBytes(ssid);
            for (int i = 0; i < Math.Min(32, name.Length); i++) Write8(at + 6 + (uint)i, name[i]);
            Write8(at + 39, (byte)channel);
            Write8(at + 44, (byte)(sbyte)Math.Max(-127, Math.Min(0, rssi)));
            Write32(at + 48, (uint)auth);
            Write32(at + 52, auth != 0 ? 4u : 0u);   // CCMP
            Write32(at + 56, auth != 0 ? 4u : 0u);
            Write8(at + 64, 0x07);                    // phy_11b | 11g | 11n
        }

        /// <summary>The station leaves its network: the stack forgets its links; DISCONNECTED with <paramref name="reason"/> when asked.</summary>
        private void StationDown(int reason, bool post)
        {
            bool was = _staState != 0;
            _staState = 0;
            _wifiStack?.LinkDown(0);
            if (post && was) PostDisconnected(reason, 0, new byte[6]);
        }

        private void PostDisconnected(int reason, int rssi, byte[] bssid)
        {
            var d = new byte[41];
            var name = Encoding.UTF8.GetBytes(CString(_wifiConfig[0], 0, 32));
            Array.Copy(name, d, Math.Min(32, name.Length));
            d[32] = (byte)Math.Min(32, name.Length);
            Array.Copy(bssid, 0, d, 33, 6);
            d[39] = (byte)reason;
            d[40] = (byte)(sbyte)rssi;
            PostWifiEvent(StaDisconnected, d);
        }

        private void PostWifiEvent(uint id, byte[] data) => _pendingEvents.Add((id, data));

        /// <summary>The chip's own access point from its AP config (ssid, password, channel, max connections): on the medium, then AP_START.</summary>
        private void StartSoftAp()
        {
            string ssid = ApplySoftApConfig();
            var mac = (byte[])(_wifiNet?.Mac ?? new byte[6]).Clone();
            mac[5]++;
            _wifiStack?.LinkUp(1, SoftApIp, SoftApIp, mac, mac);
            _apUp = true;
            _apStations = new List<(byte[], uint, int)>();
            PostWifiEvent(ApStart, Array.Empty<byte>());
        }

        /// <summary>The AP config (ssid, password, channel, max connections) onto the medium; the SSID it opened under.</summary>
        private string ApplySoftApConfig()
        {
            var cfg = _wifiConfig[1];
            int ssidLength = cfg[96];
            string ssid = ssidLength > 0 && ssidLength <= 32 ? Encoding.UTF8.GetString(cfg, 0, ssidLength) : CString(cfg, 0, 32);
            if (ssid.Length == 0) ssid = "ESP_" + string.Concat((_wifiNet?.Mac ?? new byte[6]).Skip(3).Select(b => b.ToString("X2")));
            int channel = cfg[97] is > 0 and <= 13 ? cfg[97] : 1;
            _wifiNet?.SoftAp(ssid, CString(cfg, 32, 64), channel, cfg[105]);   // the medium reports the opening itself
            return ssid;
        }

        private void StopSoftAp(bool post)
        {
            if (!_apUp) return;
            _apUp = false;
            _wifiNet?.StopSoftAp();
            _wifiStack?.LinkDown(1);
            _apStations = new List<(byte[], uint, int)>();
            if (post) PostWifiEvent(ApStop, Array.Empty<byte>());
        }

        /// <summary>Stations joining or leaving the soft-AP: AP_STACONNECTED / AP_STADISCONNECTED, as the driver reports them.</summary>
        private void ServiceSoftAp(double t)
        {
            if (_wifiNet == null) return;
            var now = _wifiNet.SoftApStations(t);
            bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
            foreach (var s in now.Where(s => !_apStations.Any(o => Same(o.Mac, s.Mac))))
            {
                var d = new byte[8];
                Array.Copy(s.Mac, d, 6);
                d[6] = (byte)(1 + now.IndexOf(s));   // association id
                PostWifiEvent(ApStaConnected, d);
            }

            foreach (var o in _apStations.Where(o => !now.Any(s => Same(o.Mac, s.Mac))))
            {
                var d = new byte[10];
                Array.Copy(o.Mac, d, 6);
                d[6] = 1;
                d[8] = 8;   // WIFI_REASON_ASSOC_LEAVE
                PostWifiEvent(ApStaDisconnected, d);
            }

            _apStations = now;
            _wifiStack?.SetStations(now.Select(s => (s.Mac, s.Ip)));
        }

        /// <summary>Called each slice: the station's association, a scan's end, events and frames into the firmware.</summary>
        private void ServiceWifi()
        {
            if (!_wifiInit || Seconds < _wifiNextService) return;
            _wifiNextService = Seconds + 0.0005;
            double t = Seconds;
            if (_staState == 1)
            {
                var s = _wifiNet?.Status(t);
                if (s != null && s.Value.Status == 3)
                {
                    _staState = 2;
                    _wifiStack?.LinkUp(0, s.Value.Ip, s.Value.Gateway, s.Value.Bssid, _wifiNet!.Mac);
                    var d = new byte[48];
                    var name = Encoding.UTF8.GetBytes(CString(_wifiConfig[0], 0, 32));
                    Array.Copy(name, d, Math.Min(32, name.Length));
                    d[32] = (byte)Math.Min(32, name.Length);
                    Array.Copy(s.Value.Bssid, 0, d, 33, 6);
                    d[39] = (byte)s.Value.Channel;
                    d[40] = (byte)(CString(_wifiConfig[0], 32, 64).Length > 0 ? 3 : 0);   // WPA2-PSK or open
                    d[44] = 1;                                                              // association id
                    PostWifiEvent(StaConnected, d);
                }
                else if (s != null && (s.Value.Status == 1 || s.Value.Status == 4) && t - _connectAt > 0.05)
                {
                    _staState = 0;
                    PostDisconnected(s.Value.Status == 1 ? 201 : 15, 0, new byte[6]);   // NO_AP_FOUND / 4WAY_HANDSHAKE_TIMEOUT
                }
                else if (s == null && t - _connectAt > 2.5)
                {
                    _staState = 0;
                    PostDisconnected(201, 0, new byte[6]);
                }
            }
            else if (_staState == 2 && _wifiNet != null)
            {
                var s = _wifiNet.Status(t);
                if (s.Status != 3)
                {
                    StationDown(200, post: true);   // BEACON_TIMEOUT
                }
            }

            if (!double.IsNaN(_scanDoneAt) && t >= _scanDoneAt)
            {
                _scanDoneAt = double.NaN;
                var d = new byte[8];
                d[4] = (byte)Math.Min(255, _scan.Count);
                PostWifiEvent(ScanDone, d);
            }

            if (_apUp) ServiceSoftAp(t);
            if (_staState == 2 || _apUp) _wifiStack?.Poll(t);
            if (_wifiPool == 0) return;   // the buffers are not there yet

            // Events, in order, each with its data in a free event slot.
            while (_pendingEvents.Count > 0)
            {
                int slot = Array.IndexOf(_eventBusy, false);
                if (slot < 0) break;
                var (id, data) = _pendingEvents[0];
                _pendingEvents.RemoveAt(0);
                uint at = _wifiPool + (uint)(RxSlots * RxSlotSize + slot * EventSlotSize);
                for (int i = 0; i < data.Length; i++) Write8(at + (uint)i, data[i]);
                _eventBusy[slot] = true;
                int captured = slot;
                CallFirmware(_eventPost, _ => _eventBusy[captured] = false, Read32(_wifiEventBase), id, data.Length > 0 ? at : 0, (uint)data.Length, 0);
            }

            // Received frames into free buffers, handed to the station's receive callback (lwIP frees them as it is done).
            while (_rxQueue.Count > 0)
            {
                var (ifx, f) = _rxQueue.Peek();
                bool up = ifx == 0 ? _staState == 2 : _apUp;
                if (_rxCallback[ifx] == 0 || !up || f.Length > RxSlotSize)
                {
                    _rxQueue.Dequeue();   // no interface to take it
                    continue;
                }

                int slot = Array.IndexOf(_rxBusy, false);
                if (slot < 0)
                {
                    if (_rxQueue.Count > 32) _rxQueue.Dequeue();   // the driver's buffers are full: frames are lost
                    break;
                }

                _rxQueue.Dequeue();
                uint at = _wifiPool + (uint)(slot * RxSlotSize);
                for (int i = 0; i < f.Length; i++) Write8(at + (uint)i, f[i]);
                _rxBusy[slot] = true;
                CallFirmware(_rxCallback[ifx], null, at, (uint)f.Length, at);
            }

            ServiceEspNow(t);   // [espnow]
        }
    }
}
