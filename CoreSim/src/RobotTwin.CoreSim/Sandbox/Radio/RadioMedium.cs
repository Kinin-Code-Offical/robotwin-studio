using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>A BLE advertisement heard during a scan.</summary>
    public sealed class BleScanResult
    {
        public RadioNode Node = null!;
        public byte[] Address = new byte[6];
        public double RssiDbm;
        public string Name = string.Empty;
        public string Uuid = string.Empty;
        public byte[] Manufacturer = Array.Empty<byte>();
        public int TxPowerDbm;
        public int Heard;
    }

    /// <summary>
    /// The shared 2.4 GHz (and 38 kHz IR) environment of a world. Radios are placed at
    /// their objects' positions; every packet sees the propagation model (log-distance
    /// path loss, obstacle loss, deterministic shadow fading) and is lost with the packet
    /// error rate of its received level. Carries BLE advertising/scanning and a GATT-like
    /// notify channel, Wi-Fi association with UDP and reliable stream sockets between
    /// ESP32s, and Bluetooth Classic HID links of game controllers (pairing, bonding,
    /// reconnection, supervision timeout when out of range).
    /// All randomness is a hash of (seed, link, time): runs are reproducible.
    /// </summary>
    public sealed partial class RadioMedium
    {
        public const double SupervisionTimeout = 2.0;
        public const double WifiLossTimeout = 3.0;
        public const string ImplicitSsid = "RoboTwin-Lab";

        private readonly object _gate = new object();
        private readonly List<RadioNode> _nodes = new List<RadioNode>();
        private readonly List<RadioEmission> _emissions = new List<RadioEmission>();
        private readonly Dictionary<string, double> _lastEmission = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<RadioEvent> _events = new List<RadioEvent>();
        private readonly Dictionary<int, BleConnection> _ble = new Dictionary<int, BleConnection>();
        private readonly Dictionary<int, StreamConnection> _streams = new Dictionary<int, StreamConnection>();
        private readonly WifiNetwork _implicit = new WifiNetwork { Ssid = ImplicitSsid, Implicit = true, Gateway = Ip(192, 168, 1, 1), NextHost = 77, Channel = 6, MaxStations = 64 };
        private int _nextConnection = 1;
        private double _lastUpdate = double.NegativeInfinity;
        private ulong _packetCounter;

        public RadioMedium()
        {
            Ir = new IrChannel(this);
        }

        public RadioPropagation Propagation { get; } = new RadioPropagation();
        public IrChannel Ir { get; }
        public IReadOnlyList<RadioNode> Nodes => _nodes;
        public double Time { get; private set; }

        /// <summary>
        /// When the world has no router, an implicit "virtual home network" without a position
        /// accepts any SSID/password (like the old Wi-Fi stub, but stations on it can talk to each other).
        /// </summary>
        public bool ImplicitNetworkEnabled { get; set; } = true;

        public event Action<RadioEvent>? Event;

        public T Add<T>(T node) where T : RadioNode
        {
            lock (_gate) _nodes.Add(node);
            return node;
        }

        public RadioNode? Find(string id) => _nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));

        public static string FormatMac(byte[] mac) =>
            string.Join(":", mac.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

        public static uint Ip(int a, int b, int c, int d) => (uint)(a | (b << 8) | (c << 16) | (d << 24));

        public static string FormatIp(uint ip) =>
            string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", ip & 0xFF, (ip >> 8) & 0xFF, (ip >> 16) & 0xFF, ip >> 24);

        /// <summary>Events since the last call (pairing, drops, association).</summary>
        public List<RadioEvent> DrainEvents()
        {
            lock (_gate)
            {
                var list = new List<RadioEvent>(_events);
                _events.Clear();
                return list;
            }
        }

        private void Raise(double t, RadioNode node, string message, bool warning = false)
        {
            var e = new RadioEvent(t, node.Id, message, warning);
            _events.Add(e);
            if (_events.Count > 500) _events.RemoveAt(0);
            Event?.Invoke(e);
        }

        // ================================================================ propagation

        public double TxPower(RadioNode n, RadioTech tech) => n switch
        {
            Esp32RadioNode e when tech == RadioTech.Ble => e.BleTxPowerDbm,
            Esp32RadioNode e when tech == RadioTech.Bluetooth => e.BtTxPowerDbm,
            _ => n.TxPowerDbm
        };

        /// <summary>Received level at <paramref name="rx"/> of a packet from <paramref name="tx"/> (dBm), with fading at the time.</summary>
        public double Rssi(RadioNode tx, RadioNode rx, RadioTech tech, double time) =>
            Propagation.RssiDbm(TxPower(tx, tech), tx.AntennaGainDbi, rx.AntennaGainDbi, tx.Position(), rx.Position(), tx.Id, rx.Id, time);

        /// <summary>Median received level (no fading).</summary>
        public double MeanRssi(RadioNode tx, RadioNode rx, RadioTech tech) =>
            Propagation.MeanRssiDbm(TxPower(tx, tech), tx.AntennaGainDbi, rx.AntennaGainDbi, tx.Position(), rx.Position());

        /// <summary>One packet over one hop: received or lost (deterministic per packet), and its RSSI.</summary>
        public bool TryHop(RadioNode tx, RadioNode rx, RadioTech tech, int bits, double time, ulong salt, out double rssi)
        {
            rssi = Rssi(tx, rx, tech, time);
            if (!tx.IsOn() || !rx.IsOn()) return false;
            double per = RadioPropagation.PacketErrorRate(tech, rssi, bits);
            double u = RadioRandom.Uniform(Propagation.Seed ^ 0xA5A5UL, RadioRandom.Hash(tx.Id + ">" + rx.Id), salt);
            return u >= per;
        }

        private ulong NextSalt() => ++_packetCounter;

        private void Emit(RadioNode n, RadioTech tech, double t, double minSpacing = 0.18)
        {
            string key = n.Id + "|" + (int)tech;
            if (_lastEmission.TryGetValue(key, out double last) && t - last < minSpacing && t >= last) return;
            _lastEmission[key] = t;
            _emissions.Add(new RadioEmission(n.Id, tech, t, n.Position(), TxPower(n, tech)));
            if (_emissions.Count > 400) _emissions.RemoveRange(0, _emissions.Count - 400);
        }

        /// <summary>Emissions after <paramref name="since"/> (for the expanding rings in the 3D view).</summary>
        public List<RadioEmission> Emissions(double since)
        {
            lock (_gate) return _emissions.Where(e => e.Time >= since).ToList();
        }

        // ================================================================ time

        /// <summary>Advances housekeeping to <paramref name="now"/>: controller links, Wi-Fi association, BLE supervision (every 5 ms) and the IR channel (every call).</summary>
        public void Advance(double now, double dt)
        {
            lock (_gate)
            {
                Time = now;
                Ir.Advance(now, dt);
                if (now - _lastUpdate < 0.005) return;
                _lastUpdate = now;
                UpdatePads(now);
                foreach (var e in _nodes.OfType<Esp32RadioNode>())
                {
                    if (!e.IsOn()) continue;
                    UpdateStation(e, now);
                    if (e.Advertising)
                    {
                        Emit(e, RadioTech.Ble, now, Math.Max(0.18, e.AdvIntervalS));
                        e.Channel = 37 + (int)(now / Math.Max(0.02, e.AdvIntervalS)) % 3;
                    }
                }

                foreach (var b in _nodes.OfType<BeaconNode>())
                {
                    if (!b.IsOn()) continue;
                    Emit(b, RadioTech.Ble, now, Math.Max(0.25, b.AdvIntervalS));
                    b.Channel = 37 + (int)(now / Math.Max(0.02, b.AdvIntervalS)) % 3;
                    b.Status = "BLE reklam · " + b.AdvName;
                }

                foreach (var ap in _nodes.OfType<AccessPointNode>())
                {
                    Emit(ap, RadioTech.WiFi, now, 0.3);   // beacon frames every 102.4 ms
                    int n = _nodes.OfType<Esp32RadioNode>().Count(s => s.StaNetwork == ap.Network && s.StaStatus == 3);
                    ap.Status = $"Wi-Fi AP \"{ap.Network.Ssid}\" · kanal {ap.Network.Channel} · {n} istemci";
                }

                SuperviseBle(now);
                foreach (var e in _nodes.OfType<Esp32RadioNode>()) e.Status = DescribeEsp32(e);
            }
        }

        private static string DescribeEsp32(Esp32RadioNode e)
        {
            if (!e.IsOn()) return "Kapalı";
            var parts = new List<string>();
            if (e.StaSsid != null)
            {
                parts.Add(e.StaStatus switch
                {
                    3 => $"Wi-Fi: {e.StaSsid} ({FormatIp(e.StaIp)})",
                    1 => $"Wi-Fi: \"{e.StaSsid}\" bulunamadı",
                    4 => "Wi-Fi: şifre hatalı",
                    5 => "Wi-Fi: bağlantı koptu",
                    _ => $"Wi-Fi: {e.StaSsid}'e bağlanıyor…"
                });
            }

            if (e.SoftAp != null) parts.Add($"AP \"{e.SoftAp.Ssid}\"");
            if (e.Advertising) parts.Add("BLE reklam");
            if (e.Scanning) parts.Add("BLE tarama");
            if (e.PadEnabled)
            {
                int n = e.PadSlots.Count(s => s.Pad != null);
                parts.Add(n > 0 ? $"Bluepad32: {n} kumanda" : e.PadAllowNew ? "Bluepad32: kumanda aranıyor" : "Bluepad32");
            }

            return parts.Count == 0 ? "Çalışıyor (radyo kapalı)" : string.Join(" · ", parts);
        }

        // ================================================================ Wi-Fi

        private IEnumerable<WifiNetwork> Networks()
        {
            foreach (var ap in _nodes.OfType<AccessPointNode>())
            {
                if (ap.IsOn()) yield return ap.Network;
            }

            foreach (var e in _nodes.OfType<Esp32RadioNode>())
            {
                if (e.SoftAp != null && e.IsOn()) yield return e.SoftAp;
            }

            if (ImplicitNetworkEnabled && !_nodes.OfType<AccessPointNode>().Any()) yield return _implicit;
        }

        private double NetworkRssi(WifiNetwork net, RadioNode sta, double t) =>
            net.Node == null ? -48.0 + 0.5 * Propagation.FadingDb("implicit-ap", sta.Id, t) : Rssi(net.Node, sta, RadioTech.WiFi, t);

        private double JoinDelay(Esp32RadioNode e) => 0.85 + 0.25 * RadioRandom.Uniform(Propagation.Seed, RadioRandom.Hash(e.Id), 7);

        public void WifiMode(Esp32RadioNode e, int mode)
        {
            lock (_gate)
            {
                e.WifiMode = mode;
                if ((mode & 1) == 0)
                {
                    LeaveStation(e);
                    e.StaSsid = null;
                    e.StaStatus = 0;
                }

                if ((mode & 2) == 0) StopSoftAp(e);
            }
        }

        public void WifiBegin(Esp32RadioNode e, string ssid, string password, double t)
        {
            lock (_gate)
            {
                LeaveStation(e);
                e.WifiMode |= 1;
                e.StaSsid = ssid;
                e.StaPassword = password ?? string.Empty;
                e.StaBeganAt = t;
                e.StaStatus = 6;
                Emit(e, RadioTech.WiFi, t, 0);
            }
        }

        public void WifiDisconnect(Esp32RadioNode e)
        {
            lock (_gate)
            {
                LeaveStation(e);
                e.StaSsid = null;
                e.StaStatus = 6;
            }
        }

        private void LeaveStation(Esp32RadioNode e)
        {
            e.StaNetwork = null;
            e.StaIp = 0;
            e.StaLostSince = double.NaN;
            foreach (var s in _streams.Values.Where(s => s.Open && (s.A == e || s.B == e)).ToList()) s.Open = false;
        }

        private void UpdateStation(Esp32RadioNode e, double t)
        {
            if (e.StaSsid == null) return;
            if (e.StaNetwork != null)
            {
                var net = e.StaNetwork;
                bool gone = !Networks().Contains(net);
                double rssi = gone ? -200 : NetworkRssi(net, e, t);
                if (gone || rssi < RadioPropagation.SensitivityDbm(RadioTech.WiFi))
                {
                    if (double.IsNaN(e.StaLostSince)) e.StaLostSince = t;
                    if (gone || t - e.StaLostSince >= WifiLossTimeout)
                    {
                        Raise(t, e, $"Wi-Fi bağlantısı koptu ({e.StaSsid}, {(gone ? "erişim noktası kapandı" : $"sinyal {rssi:0} dBm")})", true);
                        LeaveStation(e);
                        e.StaStatus = 5;
                        e.StaBeganAt = t;   // auto-reconnect keeps trying
                    }
                }
                else
                {
                    e.StaLostSince = double.NaN;
                }

                return;
            }

            if (double.IsNaN(e.StaBeganAt) || t - e.StaBeganAt < JoinDelay(e)) return;
            var candidates = Networks().Where(n => n.Ssid == e.StaSsid).ToList();
            if (candidates.Count == 0 && ImplicitNetworkEnabled && !_nodes.OfType<AccessPointNode>().Any() &&
                !Networks().Any(n => !n.Implicit && n.Ssid == e.StaSsid))
            {
                candidates.Add(_implicit);   // the virtual home network answers to any SSID
            }

            var best = candidates.Select(n => (Net: n, Rssi: NetworkRssi(n, e, t))).OrderByDescending(x => x.Rssi).FirstOrDefault();
            if (best.Net == null || best.Rssi < RadioPropagation.SensitivityDbm(RadioTech.WiFi))
            {
                if (t - e.StaBeganAt >= 2.5)
                {
                    if (e.StaStatus != 1) Raise(t, e, $"Wi-Fi: \"{e.StaSsid}\" ağı bulunamadı (menzil dışı veya yok)", true);
                    e.StaStatus = 1;
                    e.StaBeganAt = t;
                }

                return;
            }

            var net2 = best.Net;
            if (!net2.Implicit && net2.Password.Length > 0 && net2.Password != e.StaPassword)
            {
                if (e.StaStatus != 4) Raise(t, e, $"Wi-Fi: \"{net2.Ssid}\" şifresi yanlış", true);
                e.StaStatus = 4;
                e.StaBeganAt = t + 1.0;
                return;
            }

            int members = _nodes.OfType<Esp32RadioNode>().Count(s => s.StaNetwork == net2);
            if (members >= net2.MaxStations)
            {
                e.StaStatus = 4;
                e.StaBeganAt = t;
                return;
            }

            e.StaNetwork = net2;
            e.StaIp = (net2.Gateway & 0x00FFFFFF) | ((uint)(net2.NextHost++ & 0xFF) << 24);
            e.StaStatus = 3;
            e.StaLostSince = double.NaN;
            e.Channel = net2.Channel;
            Emit(e, RadioTech.WiFi, t, 0);
            Raise(t, e, $"Wi-Fi bağlandı: \"{net2.Ssid}\" · IP {FormatIp(e.StaIp)} · {best.Rssi:0} dBm");
        }

        /// <summary>wl_status_t, RSSI, IP and channel of a station.</summary>
        public (int Status, int Rssi, uint Ip, uint Gateway, int Channel, byte[] Bssid) WifiStatus(Esp32RadioNode e, double t)
        {
            lock (_gate)
            {
                if (e.WifiMode == 0) return (254, 0, 0, 0, 0, new byte[6]);
                if (e.StaSsid == null) return (0, 0, 0, 0, 0, new byte[6]);
                UpdateStation(e, t);
                if (e.StaNetwork == null) return (e.StaStatus, 0, 0, 0, 0, new byte[6]);
                var net = e.StaNetwork;
                var bssid = net.Node is Esp32RadioNode ap ? ap.ApMac : net.Node?.Mac ?? new byte[] { 0x5C, 0x49, 0x79, 0x10, 0x20, 0x30 };
                return (3, (int)Math.Round(NetworkRssi(net, e, t)), e.StaIp, net.Gateway, net.Channel, bssid);
            }
        }

        public List<(string Ssid, int Rssi, int Channel, int Auth, byte[] Bssid)> WifiScan(Esp32RadioNode e, double t)
        {
            lock (_gate)
            {
                Emit(e, RadioTech.WiFi, t, 0);
                var list = new List<(string, int, int, int, byte[])>();
                foreach (var n in Networks())
                {
                    if (n.Node == e) continue;
                    double r = NetworkRssi(n, e, t);
                    if (r < RadioPropagation.SensitivityDbm(RadioTech.WiFi)) continue;
                    var bssid = n.Node is Esp32RadioNode ap ? ap.ApMac : n.Node?.Mac ?? new byte[] { 0x5C, 0x49, 0x79, 0x10, 0x20, 0x30 };
                    list.Add((n.Ssid, (int)Math.Round(r), n.Channel, n.Password.Length > 0 ? 3 : 0, bssid));
                }

                return list.OrderByDescending(x => x.Item2).ToList();
            }
        }

        public bool SoftAp(Esp32RadioNode e, string ssid, string password, int channel, int maxStations)
        {
            lock (_gate)
            {
                e.WifiMode |= 2;
                e.SoftAp = new WifiNetwork
                {
                    Ssid = ssid,
                    Password = password ?? string.Empty,
                    Channel = channel <= 0 || channel > 13 ? 1 : channel,
                    Gateway = Ip(192, 168, 4, 1),
                    Node = e,
                    NextHost = 2,
                    MaxStations = maxStations <= 0 ? 4 : maxStations
                };
                if (e.StaNetwork == null) e.Channel = e.SoftAp.Channel;
                Raise(Time, e, $"Wi-Fi erişim noktası açıldı: \"{ssid}\" (kanal {e.SoftAp.Channel}, 192.168.4.1)");
                return true;
            }
        }

        public void StopSoftAp(Esp32RadioNode e)
        {
            if (e.SoftAp == null) return;
            var net = e.SoftAp;
            e.SoftAp = null;
            foreach (var s in _nodes.OfType<Esp32RadioNode>().Where(s => s.StaNetwork == net)) s.StaLostSince = Time - WifiLossTimeout;
        }

        public int SoftApStations(Esp32RadioNode e)
        {
            lock (_gate) return e.SoftAp == null ? 0 : _nodes.OfType<Esp32RadioNode>().Count(s => s.StaNetwork == e.SoftAp && s.StaStatus == 3);
        }

        /// <summary>The stations associated with a node's soft-AP: their MAC, address and signal at the AP.</summary>
        public List<(byte[] Mac, uint Ip, int Rssi)> SoftApMembers(Esp32RadioNode e, double t)
        {
            lock (_gate)
            {
                var list = new List<(byte[], uint, int)>();
                if (e.SoftAp == null) return list;
                foreach (var s in _nodes.OfType<Esp32RadioNode>())
                {
                    if (s == e || s.StaNetwork != e.SoftAp || s.StaStatus != 3 || !s.IsOn()) continue;
                    list.Add(((byte[])s.Mac.Clone(), s.StaIp, (int)Math.Round(Rssi(s, e, RadioTech.WiFi, t))));
                }

                return list;
            }
        }

        private WifiNetwork? NetworkOf(Esp32RadioNode e) => e.StaNetwork != null && e.StaStatus == 3 ? e.StaNetwork : e.SoftAp;

        private uint IpOn(Esp32RadioNode e, WifiNetwork net) => e.StaNetwork == net && e.StaStatus == 3 ? e.StaIp : e.SoftAp == net ? net.Gateway : 0;

        private IEnumerable<Esp32RadioNode> Members(WifiNetwork net)
        {
            foreach (var s in _nodes.OfType<Esp32RadioNode>())
            {
                if (!s.IsOn()) continue;
                if ((s.StaNetwork == net && s.StaStatus == 3) || s.SoftAp == net) yield return s;
            }
        }

        /// <summary>Delivers a frame from <paramref name="from"/> to <paramref name="to"/> over the network (via the AP when both are stations): delivery time, or NaN when lost after MAC retries.</summary>
        private double Transfer(Esp32RadioNode from, Esp32RadioNode to, WifiNetwork net, int bytes, double t, int retries = 4)
        {
            int bits = 8 * (bytes + 60);
            double at = t;
            var hops = new List<(RadioNode Tx, RadioNode Rx)>();
            if (net.Node == null)
            {
                at += 0.0015 + 0.001 * RadioRandom.Uniform(Propagation.Seed, NextSalt(), 3);   // virtual network: no air interface
                return at;
            }

            if (from != net.Node) hops.Add((from, net.Node));
            if (to != net.Node) hops.Add((net.Node, to));
            foreach (var (tx, rx) in hops)
            {
                bool ok = false;
                for (int attempt = 0; attempt <= retries; attempt++)
                {
                    at += 0.0006 + bits / 11e6 + 0.0002 * attempt;
                    if (TryHop(tx, rx, RadioTech.WiFi, bits, at, NextSalt(), out _))
                    {
                        ok = true;
                        break;
                    }
                }

                if (!ok) return double.NaN;
            }

            return at;
        }

        public void UdpBind(Esp32RadioNode e, ushort port, bool on)
        {
            lock (_gate)
            {
                if (on)
                {
                    if (!e.Udp.ContainsKey(port)) e.Udp[port] = new Queue<NetPacket>();
                }
                else
                {
                    e.Udp.Remove(port);
                }
            }
        }

        /// <summary>Sends a datagram (unicast or broadcast). Returns the number of receivers it was addressed to.</summary>
        public int UdpSend(Esp32RadioNode e, uint dstIp, ushort dstPort, ushort srcPort, byte[] data, double t)
        {
            lock (_gate)
            {
                var net = NetworkOf(e);
                if (net == null) return -2;
                uint src = IpOn(e, net);
                // the subnet's broadcast, and [mdns] a multicast group (224–239.x.x.x): every member bound on the port
                bool broadcast = dstIp == 0xFFFFFFFF || (dstIp >> 24) == 255 || (dstIp & 0xF0) == 0xE0;
                if (dstIp == MdnsGroup && dstPort == MdnsPort) MdnsSeen(e, net, src, data, t);   // RadioMedium.Mdns.cs
                e.Stats.Sent(t, data.Length);
                Emit(e, RadioTech.WiFi, t, 0.05);
                int addressed = 0;
                foreach (var r in Members(net))
                {
                    if (r == e) continue;
                    if (!broadcast && IpOn(r, net) != dstIp) continue;
                    addressed++;
                    double at = Transfer(e, r, net, data.Length + 8, t, broadcast ? 0 : 4);
                    if (double.IsNaN(at) || !r.Udp.TryGetValue(dstPort, out var q))
                    {
                        if (double.IsNaN(at)) r.Stats.Lost();
                        continue;
                    }

                    q.Enqueue(new NetPacket { SrcIp = src, SrcPort = srcPort, DstIp = dstIp, DstPort = dstPort, Data = (byte[])data.Clone(), ArriveAt = at });
                    while (q.Count > 64) q.Dequeue();   // lwIP receive mailbox overflows
                }

                return addressed;
            }
        }

        public NetPacket? UdpReceive(Esp32RadioNode e, ushort port, double t)
        {
            lock (_gate)
            {
                if (!e.Udp.TryGetValue(port, out var q) || q.Count == 0) return null;
                var p = q.Peek();
                if (p.ArriveAt > t) return null;
                q.Dequeue();
                e.Stats.Received(t);
                return p;
            }
        }

        public void StreamListen(Esp32RadioNode e, ushort port, bool on)
        {
            lock (_gate)
            {
                if (on) e.Listening.Add(port);
                else e.Listening.Remove(port);
            }
        }

        /// <summary>Opens a stream to a listening ESP32. Returns the connection id (&gt; 0), −2 unreachable, −3 refused.</summary>
        public int StreamConnect(Esp32RadioNode e, uint ip, ushort port, double t)
        {
            lock (_gate)
            {
                var net = NetworkOf(e);
                if (net == null) return -2;
                var target = Members(net).FirstOrDefault(r => r != e && IpOn(r, net) == ip);
                if (target == null) return -2;
                if (!target.Listening.Contains(port)) return -3;
                double at = Transfer(e, target, net, 40, t);
                if (double.IsNaN(at)) return -2;
                var c = new StreamConnection { Id = _nextConnection++, A = e, B = target, PortA = (ushort)(49152 + _nextConnection % 16000), PortB = port };
                _streams[c.Id] = c;
                target.PendingAccepts.Enqueue(c.Id);
                return c.Id;
            }
        }

        public (int Id, uint Ip, ushort Port) StreamAccept(Esp32RadioNode e, ushort port)
        {
            lock (_gate)
            {
                int n = e.PendingAccepts.Count;
                for (int i = 0; i < n; i++)
                {
                    int id = e.PendingAccepts.Dequeue();
                    if (_streams.TryGetValue(id, out var c) && c.PortB == port && c.Open)
                    {
                        c.Accepted = true;
                        var net = NetworkOf(c.A);
                        return (id, net != null ? IpOn(c.A, net) : 0, c.PortA);
                    }

                    if (_streams.ContainsKey(id)) e.PendingAccepts.Enqueue(id);
                }

                return (0, 0, 0);
            }
        }

        public int StreamSend(Esp32RadioNode e, int id, byte[] data, double t)
        {
            lock (_gate)
            {
                if (!_streams.TryGetValue(id, out var c) || !c.Open) return -1;
                var other = c.A == e ? c.B : c.A;
                var net = NetworkOf(e);
                if (net == null || NetworkOf(other) != net)
                {
                    c.Open = false;
                    return -1;
                }

                double at = double.NaN;
                for (int attempt = 0; attempt < 8 && double.IsNaN(at); attempt++)
                {
                    at = Transfer(e, other, net, data.Length + 40, t + attempt * 0.2);   // TCP retransmission timeout ≈ 200 ms
                }

                e.Stats.Sent(t, data.Length);
                Emit(e, RadioTech.WiFi, t, 0.05);
                if (double.IsNaN(at))
                {
                    c.Open = false;
                    return -1;
                }

                var q = c.A == e ? c.ToB : c.ToA;
                double last = q.Count > 0 ? q.Last().ArriveAt : 0;
                q.Enqueue(new NetPacket { Data = (byte[])data.Clone(), ArriveAt = Math.Max(at, last) });
                return data.Length;
            }
        }

        private static void Pull(Queue<NetPacket> q, List<byte> buffer, double t)
        {
            while (q.Count > 0 && q.Peek().ArriveAt <= t) buffer.AddRange(q.Dequeue().Data);
        }

        /// <summary>Reads arrived stream bytes: count, 0 = nothing yet, −1 = closed and drained.</summary>
        public int StreamReceive(Esp32RadioNode e, int id, byte[] into, double t)
        {
            lock (_gate)
            {
                if (!_streams.TryGetValue(id, out var c)) return -1;
                var q = c.A == e ? c.ToA : c.ToB;
                var buffer = c.A == e ? c.BufferA : c.BufferB;
                Pull(q, buffer, t);
                if (buffer.Count == 0) return c.Open ? 0 : -1;
                int n = Math.Min(into.Length, buffer.Count);
                buffer.CopyTo(0, into, 0, n);
                buffer.RemoveRange(0, n);
                e.Stats.Received(t);
                return n;
            }
        }

        public (bool Connected, int Available, uint Ip, ushort Port) StreamState(Esp32RadioNode e, int id, double t)
        {
            lock (_gate)
            {
                if (!_streams.TryGetValue(id, out var c)) return (false, 0, 0, 0);
                var q = c.A == e ? c.ToA : c.ToB;
                var buffer = c.A == e ? c.BufferA : c.BufferB;
                Pull(q, buffer, t);
                var other = c.A == e ? c.B : c.A;
                var net = NetworkOf(other);
                return (c.Open, buffer.Count, net != null ? IpOn(other, net) : 0, c.A == e ? c.PortB : c.PortA);
            }
        }

        public void StreamClose(Esp32RadioNode e, int id)
        {
            lock (_gate)
            {
                if (_streams.TryGetValue(id, out var c)) c.Open = false;
            }
        }

        // ================================================================ BLE

        public void BleInit(Esp32RadioNode e, string name)
        {
            lock (_gate)
            {
                e.BleOn = true;
                e.BleName = name ?? string.Empty;
            }
        }

        public void BleAdvertise(Esp32RadioNode e, bool on, double intervalS, string name, string uuid, byte[] manufacturer, double t)
        {
            lock (_gate)
            {
                e.BleOn = true;
                e.Advertising = on;
                e.AdvIntervalS = Math.Max(0.02, intervalS);
                e.AdvName = string.IsNullOrEmpty(name) ? e.BleName : name;
                e.AdvUuid = uuid ?? string.Empty;
                e.AdvManufacturer = manufacturer ?? Array.Empty<byte>();
                if (on) Emit(e, RadioTech.Ble, t, 0);
            }
        }

        /// <summary>Advertisements <paramref name="scanner"/> receives in [from, to] (continuous scanning, 3 advertising channels).</summary>
        public List<BleScanResult> BleScan(Esp32RadioNode scanner, double from, double to)
        {
            lock (_gate)
            {
                var results = new List<BleScanResult>();
                scanner.BleOn = true;
                Emit(scanner, RadioTech.Ble, to, 0);
                foreach (var n in _nodes)
                {
                    if (n == scanner || !n.IsOn()) continue;
                    double interval;
                    string name, uuid;
                    byte[] mfr;
                    if (n is Esp32RadioNode adv && adv.Advertising)
                    {
                        interval = adv.AdvIntervalS;
                        name = adv.AdvName;
                        uuid = adv.AdvUuid;
                        mfr = adv.AdvManufacturer;
                    }
                    else if (n is BeaconNode b)
                    {
                        interval = b.AdvIntervalS;
                        name = b.AdvName;
                        uuid = b.AdvUuid;
                        mfr = b.Manufacturer;
                    }
                    else
                    {
                        continue;
                    }

                    // Advertising events every interval + 0…10 ms random delay; the scanner hears each with 1 − PER.
                    double period = interval + 0.005;
                    int events = (int)Math.Floor((to - from) / period);
                    events = Math.Max(0, Math.Min(events, 60));
                    var r = new BleScanResult { Node = n, Address = n.BluetoothMac, Name = name, Uuid = uuid, Manufacturer = mfr, TxPowerDbm = (int)Math.Round(TxPower(n, RadioTech.Ble)) };
                    ulong key = RadioRandom.Hash(n.Id);
                    for (int k = 0; k < events; k++)
                    {
                        double at = from + (k + RadioRandom.Uniform(Propagation.Seed, key, (ulong)k)) * period;
                        if (TryHop(n, scanner, RadioTech.Ble, 8 * (10 + 31), at, (ulong)(at * 1e4), out double rssi))
                        {
                            r.Heard++;
                            r.RssiDbm = rssi;
                            n.Stats.Sent(at, 31);
                            scanner.Stats.Received(at);
                        }
                    }

                    if (r.Heard > 0) results.Add(r);
                }

                return results.OrderByDescending(x => x.RssiDbm).ToList();
            }
        }

        public int BleAddCharacteristic(Esp32RadioNode e, string service, string uuid, uint properties)
        {
            lock (_gate)
            {
                var existing = e.Characteristics.FirstOrDefault(c => Same(c.Service, service) && Same(c.Uuid, uuid));
                if (existing != null) return existing.Handle;
                var ch = new BleCharacteristic { Handle = 0x2A + e.Characteristics.Count * 3, Service = service, Uuid = uuid, Properties = properties };
                e.Characteristics.Add(ch);
                return ch.Handle;
            }
        }

        private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Sets a characteristic value; with notify, every subscribed client gets it one connection event later (retransmitted until received).</summary>
        public void BleSetValue(Esp32RadioNode e, int handle, byte[] value, bool notify, double t)
        {
            lock (_gate)
            {
                var ch = e.Characteristics.FirstOrDefault(c => c.Handle == handle);
                if (ch == null) return;
                ch.Value = (byte[])value.Clone();
                if (!notify) return;
                foreach (var c in _ble.Values.Where(c => c.Open && c.Server == e && c.Subscribed.Contains(handle)))
                {
                    double at = t;
                    bool ok = false;
                    for (int attempt = 0; attempt < 20 && !ok; attempt++)
                    {
                        at += c.IntervalS * (attempt == 0 ? RadioRandom.Uniform(Propagation.Seed, NextSalt(), 9) : 1.0);
                        ok = TryHop(e, c.Client, RadioTech.Ble, 8 * (14 + value.Length), at, NextSalt(), out _);
                    }

                    e.Stats.Sent(t, value.Length);
                    Emit(e, RadioTech.Ble, t, 0.1);
                    if (!ok)
                    {
                        c.Client.Stats.Lost();
                        continue;
                    }

                    c.Client.BleEvents.Add(new BleEvent { Kind = BleEvent.Notify, Connection = c.Id, Handle = handle, Data = (byte[])value.Clone(), At = at, Peer = e.BluetoothMac });
                }
            }
        }

        /// <summary>Connects to an advertising ESP32 by address. Returns the connection id or −1.</summary>
        public int BleConnect(Esp32RadioNode client, byte[] address, double t)
        {
            lock (_gate)
            {
                var server = _nodes.OfType<Esp32RadioNode>().FirstOrDefault(n => n != client && n.BluetoothMac.SequenceEqual(address));
                if (server == null || !server.IsOn() || !server.Advertising) return -1;
                if (!TryHop(server, client, RadioTech.Ble, 8 * 40, t, NextSalt(), out double rssi) || rssi < RadioPropagation.SensitivityDbm(RadioTech.Ble)) return -1;
                var c = new BleConnection { Id = _nextConnection++, Client = client, Server = server };
                _ble[c.Id] = c;
                client.BleOn = true;
                server.Advertising = false;   // a peripheral stops advertising when connected (like the ESP32 stack)
                server.BleEvents.Add(new BleEvent { Kind = BleEvent.Connect, Connection = c.Id, Peer = client.BluetoothMac, At = t + 0.03 });
                Raise(t, client, $"BLE bağlandı: {server.Name} ({rssi:0} dBm)");
                return c.Id;
            }
        }

        public (int Handle, uint Properties) BleFindCharacteristic(int connection, string service, string uuid)
        {
            lock (_gate)
            {
                if (!_ble.TryGetValue(connection, out var c) || !c.Open || c.Server is not Esp32RadioNode s) return (-1, 0);
                var ch = s.Characteristics.FirstOrDefault(x => Same(x.Service, service) && Same(x.Uuid, uuid));
                return ch == null ? (-1, 0u) : (ch.Handle, ch.Properties);
            }
        }

        public bool BleSubscribe(int connection, int handle, bool on)
        {
            lock (_gate)
            {
                if (!_ble.TryGetValue(connection, out var c) || !c.Open) return false;
                if (on) c.Subscribed.Add(handle);
                else c.Subscribed.Remove(handle);
                return true;
            }
        }

        public byte[]? BleRead(int connection, int handle)
        {
            lock (_gate)
            {
                if (!_ble.TryGetValue(connection, out var c) || !c.Open || c.Server is not Esp32RadioNode s) return null;
                return s.Characteristics.FirstOrDefault(x => x.Handle == handle)?.Value;
            }
        }

        public bool BleWrite(int connection, int handle, byte[] value, double t)
        {
            lock (_gate)
            {
                if (!_ble.TryGetValue(connection, out var c) || !c.Open || c.Server is not Esp32RadioNode s) return false;
                var ch = s.Characteristics.FirstOrDefault(x => x.Handle == handle);
                if (ch == null) return false;
                ch.Value = (byte[])value.Clone();
                c.Client.Stats.Sent(t, value.Length);
                s.BleEvents.Add(new BleEvent { Kind = BleEvent.Write, Connection = c.Id, Handle = handle, Data = (byte[])value.Clone(), At = t + c.IntervalS, Peer = c.Client.BluetoothMac });
                return true;
            }
        }

        public void BleDisconnect(int connection, double t)
        {
            lock (_gate) CloseBle(connection, t, null);
        }

        private void CloseBle(int connection, double t, string? reason)
        {
            if (!_ble.TryGetValue(connection, out var c) || !c.Open) return;
            c.Open = false;
            c.Client.BleEvents.Add(new BleEvent { Kind = BleEvent.Disconnect, Connection = c.Id, At = t, Peer = c.Server.BluetoothMac });
            if (c.Server is Esp32RadioNode s) s.BleEvents.Add(new BleEvent { Kind = BleEvent.Disconnect, Connection = c.Id, At = t, Peer = c.Client.BluetoothMac });
            if (reason != null) Raise(t, c.Client, $"BLE bağlantısı koptu: {c.Server.Name} ({reason})", true);
        }

        /// <summary>Next due BLE stack event of a node, or null.</summary>
        public BleEvent? BlePoll(Esp32RadioNode e, double t)
        {
            lock (_gate)
            {
                int best = -1;
                for (int i = 0; i < e.BleEvents.Count; i++)
                {
                    if (e.BleEvents[i].At <= t && (best < 0 || e.BleEvents[i].At < e.BleEvents[best].At)) best = i;
                }

                if (best < 0) return null;
                var ev = e.BleEvents[best];
                e.BleEvents.RemoveAt(best);
                if (ev.Kind == BleEvent.Notify || ev.Kind == BleEvent.Write) e.Stats.Received(t);
                return ev;
            }
        }

        private void SuperviseBle(double t)
        {
            foreach (var c in _ble.Values.Where(c => c.Open).ToList())
            {
                bool off = !c.Client.IsOn() || !c.Server.IsOn();
                double rssi = off ? -200 : Rssi(c.Server, c.Client, RadioTech.Ble, t);
                if (off || rssi < RadioPropagation.SensitivityDbm(RadioTech.Ble))
                {
                    if (double.IsNaN(c.BadSince)) c.BadSince = t;
                    if (t - c.BadSince >= SupervisionTimeout) CloseBle(c.Id, t, off ? "karşı taraf kapandı" : $"sinyal {rssi:0} dBm, denetim zaman aşımı");
                }
                else
                {
                    c.BadSince = double.NaN;
                }
            }
        }

        /// <summary>Called when an ESP32 (re)boots: its radio state is gone, its links drop.</summary>
        public void ResetNode(Esp32RadioNode e, double t)
        {
            lock (_gate)
            {
                foreach (var c in _ble.Values.Where(c => c.Open && (c.Client == e || c.Server == e)).ToList()) CloseBle(c.Id, t, "yeniden başladı");
                _mdnsLearned.Remove(e);   // [mdns] its responder starts over
                foreach (var s in _streams.Values.Where(s => s.A == e || s.B == e)) s.Open = false;
                foreach (var p in _nodes.OfType<GamepadNode>().Where(p => p.Host == e || p.Connecting == e).ToList())
                {
                    if (p.Host == e) DropPad(p, t, "ESP32 yeniden başladı");
                    p.Connecting = null;
                }

                e.ResetRadio();
            }
        }

        // ================================================================ game controllers (Bluetooth Classic HID, Bluepad32)

        public void PadEnable(Esp32RadioNode host, bool allowNew)
        {
            lock (_gate)
            {
                host.PadEnabled = true;
                host.PadAllowNew = allowNew;
                host.BleOn = true;
            }
        }

        private bool HostReady(Esp32RadioNode h) => h.IsOn() && h.PadEnabled;

        private void UpdatePads(double t)
        {
            double sensitivity = RadioPropagation.SensitivityDbm(RadioTech.Bluetooth);
            foreach (var pad in _nodes.OfType<GamepadNode>())
            {
                pad.Channel = (int)(t * 1600) % 79;
                if (!pad.PoweredOn)
                {
                    if (pad.Host != null) DropPad(pad, t, "kumanda kapatıldı");
                    pad.Connecting = null;
                    pad.Status = "Kapalı";
                    continue;
                }

                if (pad.Host != null)
                {
                    var host = pad.Host;
                    if (!host.PadSlots.Any(s => s.Pad == pad))
                    {
                        DropPad(pad, t, "ESP32 bağlantıyı bıraktı");
                        continue;
                    }

                    double rssi = host.IsOn() ? Rssi(pad, host, RadioTech.Bluetooth, t) : -200;
                    pad.LastRssi = rssi;
                    if (rssi < sensitivity)
                    {
                        if (double.IsNaN(pad.BadSince)) pad.BadSince = t;
                        if (t - pad.BadSince >= SupervisionTimeout)
                        {
                            DropPad(pad, t, host.IsOn() ? $"menzil dışı ({rssi:0} dBm), bağlantı denetimi zaman aşımı" : "ESP32 kapandı");
                            continue;
                        }
                    }
                    else
                    {
                        pad.BadSince = double.NaN;
                    }

                    AccountReports(pad, host, t);
                    pad.Status = $"Bağlı: {host.Name} · {rssi:0} dBm";
                    continue;
                }

                if (pad.Connecting != null)
                {
                    var h = pad.Connecting;
                    double rssi = HostReady(h) ? Rssi(pad, h, RadioTech.Bluetooth, t) : -200;
                    if (rssi < sensitivity)
                    {
                        pad.Connecting = null;
                        pad.Status = pad.PairingMode ? "Eşleştirme modunda (ESP32 bulunamadı)" : "Bağlanamadı";
                        continue;
                    }

                    if (t < pad.ConnectDoneAt)
                    {
                        pad.Status = pad.ConnectIsPairing ? $"Eşleşiyor: {h.Name}…" : $"Bağlanıyor: {h.Name}…";
                        continue;
                    }

                    var slot = h.PadSlots.FirstOrDefault(s => s.Pad == null);
                    pad.Connecting = null;
                    if (slot == null)
                    {
                        pad.Status = "ESP32'de boş kumanda yuvası yok";
                        continue;
                    }

                    slot.Pad = pad;
                    slot.Seen.CopyFrom(pad.Input);
                    slot.Sequence++;
                    slot.LastReport = t;
                    pad.Host = h;
                    pad.BadSince = double.NaN;
                    if (pad.ConnectIsPairing)
                    {
                        pad.BondedHost = h.BluetoothMacText;
                        pad.PairingMode = false;
                        Raise(t, pad, $"Eşleştirildi: {pad.Name} ⇄ {h.Name} (BT {h.BluetoothMacText}, kumanda {pad.MacText}, {rssi:0} dBm)");
                    }
                    else
                    {
                        Raise(t, pad, $"Kumanda bağlandı: {pad.Name} ⇄ {h.Name} ({rssi:0} dBm)");
                    }

                    Emit(pad, RadioTech.Bluetooth, t, 0);
                    continue;
                }

                // Idle: a discoverable pad pairs with an ESP32 that accepts new controllers; a bonded pad pages its host.
                Esp32RadioNode? best = null;
                double bestRssi = double.NegativeInfinity;
                foreach (var h in _nodes.OfType<Esp32RadioNode>())
                {
                    if (!HostReady(h)) continue;
                    bool eligible = pad.PairingMode ? h.PadAllowNew : pad.BondedHost != null && pad.BondedHost == h.BluetoothMacText;
                    if (!eligible) continue;
                    double r = Rssi(pad, h, RadioTech.Bluetooth, t);
                    if (r >= sensitivity + 3 && r > bestRssi)
                    {
                        best = h;
                        bestRssi = r;
                    }
                }

                if (best != null)
                {
                    pad.Connecting = best;
                    pad.ConnectIsPairing = pad.PairingMode;
                    pad.ConnectDoneAt = t + (pad.PairingMode ? 1.2 : 0.5);   // inquiry + SSP pairing vs. page + HID reconnect
                    Emit(pad, RadioTech.Bluetooth, t, 0);
                }
                else
                {
                    pad.Status = pad.PairingMode ? "Eşleştirme modunda: Bluepad32 çalıştıran ESP32 aranıyor…"
                        : pad.BondedHost != null ? $"Eşli ESP32'ye ({pad.BondedHost}) ulaşılamıyor" : "Eşleşmemiş (Eşleştirme modu ile eşleştirin)";
                    if (pad.PairingMode) Emit(pad, RadioTech.Bluetooth, t, 0.5);
                }
            }
        }

        private void AccountReports(GamepadNode pad, Esp32RadioNode host, double t)
        {
            var slot = host.PadSlots.FirstOrDefault(s => s.Pad == pad);
            if (slot == null) return;
            double period = Math.Max(0.001, pad.ReportIntervalS);
            long first = (long)Math.Floor(slot.LastReport / period) + 1, last = (long)Math.Floor(t / period);
            if (last - first > 200) first = last - 200;
            bool any = false;
            for (long k = first; k <= last; k++)
            {
                double at = k * period;
                pad.Stats.Sent(at, 78);
                if (TryHop(pad, host, RadioTech.Bluetooth, 8 * 90, at, (ulong)k, out _))
                {
                    host.Stats.Received(at);
                    any = true;
                }
                else
                {
                    host.Stats.Lost();
                }
            }

            if (last >= first) slot.LastReport = last * period;
            if (any)
            {
                slot.Seen.CopyFrom(pad.Input);
                slot.Sequence++;
            }

            Emit(pad, RadioTech.Bluetooth, t, 0.35);
        }

        private void DropPad(GamepadNode pad, double t, string reason)
        {
            var host = pad.Host;
            pad.Host = null;
            pad.BadSince = double.NaN;
            if (host != null)
            {
                foreach (var s in host.PadSlots)
                {
                    if (s.Pad != pad) continue;
                    s.Pad = null;
                    s.Sequence++;
                }
            }

            Raise(t, pad, $"Kumanda bağlantısı koptu: {pad.Name} ({reason})", true);
            pad.Status = "Bağlantı koptu: " + reason;
        }

        /// <summary>Controller slots of a Bluepad32 host after bringing the links up to date (BP32.update()).</summary>
        public PadSlot[] PadPoll(Esp32RadioNode host, double t)
        {
            lock (_gate)
            {
                Time = Math.Max(Time, t);
                UpdatePads(t);
                return host.PadSlots;
            }
        }

        public void PadFeedback(Esp32RadioNode host, int index, Action<GamepadFeedback> change, double t)
        {
            lock (_gate)
            {
                if (index < 0 || index >= host.PadSlots.Length || host.PadSlots[index].Pad is not { } pad) return;
                change(pad.Feedback);
                host.Stats.Sent(t, 48);
            }
        }

        public void PadDisconnect(Esp32RadioNode host, int index, double t)
        {
            lock (_gate)
            {
                if (index < 0 || index >= host.PadSlots.Length || host.PadSlots[index].Pad is not { } pad) return;
                DropPad(pad, t, "ESP32 bağlantıyı kesti");
            }
        }

        /// <summary>BP32.forgetBluetoothKeys(): bonds with this host are erased; bonded pads must pair again.</summary>
        public void PadForget(Esp32RadioNode host, double t)
        {
            lock (_gate)
            {
                foreach (var p in _nodes.OfType<GamepadNode>().Where(p => p.BondedHost == host.BluetoothMacText).ToList())
                {
                    if (p.Host == host) DropPad(p, t, "eşleşme anahtarları silindi");
                    p.BondedHost = null;
                }
            }
        }

        /// <summary>Puts a controller into pairing mode (forgets its old bond, like holding PS + Create).</summary>
        public void StartPairing(GamepadNode pad)
        {
            lock (_gate)
            {
                if (pad.Host != null) DropPad(pad, Time, "eşleştirme moduna geçildi");
                pad.Connecting = null;
                pad.PoweredOn = true;
                pad.PairingMode = true;
                pad.Status = "Eşleştirme modunda: Bluepad32 çalıştıran ESP32 aranıyor…";
            }
        }

        // ================================================================ links for the view / RF panel

        /// <summary>Active associations with their current RSSI.</summary>
        public List<RadioLink> Links()
        {
            lock (_gate)
            {
                var list = new List<RadioLink>();
                double t = Time;
                foreach (var e in _nodes.OfType<Esp32RadioNode>())
                {
                    if (e.StaNetwork?.Node is { } apNode && e.StaStatus == 3)
                    {
                        list.Add(new RadioLink(apNode, e, RadioTech.WiFi, Rssi(apNode, e, RadioTech.WiFi, t), "Wi-Fi " + e.StaNetwork.Ssid));
                    }
                }

                foreach (var c in _ble.Values.Where(c => c.Open))
                {
                    list.Add(new RadioLink(c.Server, c.Client, RadioTech.Ble, Rssi(c.Server, c.Client, RadioTech.Ble, t), "BLE GATT"));
                }

                foreach (var p in _nodes.OfType<GamepadNode>())
                {
                    if (p.Host != null) list.Add(new RadioLink(p, p.Host, RadioTech.Bluetooth, Rssi(p, p.Host, RadioTech.Bluetooth, t), "BT HID"));
                    else if (p.Connecting != null) list.Add(new RadioLink(p, p.Connecting, RadioTech.Bluetooth, Rssi(p, p.Connecting, RadioTech.Bluetooth, t), "eşleşiyor"));
                }

                return list;
            }
        }
    }
}
