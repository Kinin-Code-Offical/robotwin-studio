using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// The far side of an emulated ESP32's Wi-Fi link: what the access point and the network behind it answer to the
    /// Ethernet frames the firmware's own lwIP sends (ARP, DHCP, ping to the gateway), and the bridge from its UDP and
    /// TCP to the world's datagrams and streams (<see cref="IEsp32WifiNetwork"/>). The firmware keeps its real stack:
    /// its DHCP client, ARP cache, TCP handshakes, windows and retransmissions all run as on the chip.
    /// </summary>
    public sealed class Esp32WifiStack
    {
        private readonly IEsp32WifiNetwork _net;
        private readonly Iface[] _if = { new Iface(), new Iface() };
        private readonly HashSet<ushort> _udpPorts = new HashSet<ushort>();
        private readonly HashSet<ushort> _tcpListen = new HashSet<ushort>();
        private readonly List<TcpLink> _tcp = new List<TcpLink>();
        private uint _isn = 0x1A2B0000;
        private ushort _ipId = 1;

        public Esp32WifiStack(IEsp32WifiNetwork net)
        {
            _net = net;
        }

        /// <summary>A frame for one of the chip's interfaces (0 station, 1 soft-AP): the driver hands it to that interface's receive callback.</summary>
        public Action<int, byte[]>? Deliver { get; set; }

        /// <summary>Notes for the board's log (a refused connection, a dropped stream).</summary>
        public Action<string>? Log { get; set; }

        /// <summary>One of the chip's interfaces as the network sees it: its address and MAC, the gateway's (the station's AP; the soft-AP is its own), the stations' MACs.</summary>
        private sealed class Iface
        {
            public bool Up;
            public uint Ip, Gateway;
            public byte[] Mac = new byte[6], GatewayMac = new byte[6];
            public Dictionary<uint, byte[]> Hosts = new Dictionary<uint, byte[]>();
        }

        /// <summary>The station is associated (0) / the soft-AP is up (1).</summary>
        public bool IsUp(int ifx) => _if[ifx & 1].Up;

        /// <summary>The station's address (0 until associated).</summary>
        public uint Ip => _if[0].Up ? _if[0].Ip : 0;

        /// <summary>An interface comes up: its address, the gateway's, the gateway's MAC (the AP's BSSID) and its own MAC.</summary>
        public void LinkUp(int ifx, uint ip, uint gateway, byte[] gatewayMac, byte[] ownMac)
        {
            var i = _if[ifx & 1];
            i.Up = true;
            i.Ip = ip;
            i.Gateway = gateway;
            i.GatewayMac = (byte[])gatewayMac.Clone();
            i.Mac = (byte[])ownMac.Clone();
        }

        public void LinkDown(int ifx)
        {
            _if[ifx & 1].Up = false;
            foreach (var t in _tcp.Where(t => t.Ifx == (ifx & 1)).ToList())
            {
                _net.StreamClose(t.Id);
                _tcp.Remove(t);
            }
        }

        /// <summary>The soft-AP's stations (address → MAC): ARP and frames from them carry their real MACs.</summary>
        public void SetStations(IEnumerable<(byte[] Mac, uint Ip)> stations)
        {
            _if[1].Hosts = stations.ToDictionary(s => s.Ip, s => s.Mac);
        }

        /// <summary>The interface a far address is reached on: the soft-AP's subnet, else the station's (−1: neither is up).</summary>
        private int Route(uint ip)
        {
            if (_if[1].Up && (ip & 0x00FFFFFF) == (_if[1].Ip & 0x00FFFFFF)) return 1;
            return _if[0].Up ? 0 : -1;
        }

        /// <summary>A port the firmware bound (lwIP's udp_bind / tcp_bind): the medium delivers to it from now on.</summary>
        public void Bound(bool tcp, ushort port)
        {
            if (port == 0 || port == 67 || port == 68) return;
            if (tcp)
            {
                if (_tcpListen.Add(port)) _net.StreamListen(port, true);
            }
            else if (_udpPorts.Add(port))
            {
                _net.UdpBind(port, true);
            }
        }

        /// <summary>The MAC the network answers ARP with for an address: the AP's for the gateway, a station's own, a local one per other host.</summary>
        private byte[] MacFor(int ifx, uint ip)
        {
            var i = _if[ifx];
            if (ifx == 0 && ip == i.Gateway) return i.GatewayMac;
            if (i.Hosts.TryGetValue(ip, out var mac)) return mac;
            return new byte[] { 0x02, 0x52, (byte)ip, (byte)(ip >> 8), (byte)(ip >> 16), (byte)(ip >> 24) };
        }

        // ================================================================ from the chip

        /// <summary>An Ethernet frame the firmware sent on one of its interfaces (0 station, 1 soft-AP).</summary>
        public void Transmit(int ifx, byte[] f, double t)
        {
            ifx &= 1;
            if (f.Length < 14) return;
            int type = f[12] << 8 | f[13];
            if (type == 0x0806) Arp(ifx, f);
            else if (type == 0x0800 && f.Length >= 34) Ipv4(ifx, f, t);
        }

        private void Arp(int ifx, byte[] f)
        {
            var i = _if[ifx];
            if (!i.Up || f.Length < 42 || (f[20] << 8 | f[21]) != 1) return;   // requests only
            uint sender = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(28));
            uint target = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(38));
            if (sender == 0 || target == i.Ip || (target & 0x00FFFFFF) != (i.Ip & 0x00FFFFFF)) return;   // probes, itself, off the subnet
            if (ifx == 1 && !i.Hosts.ContainsKey(target)) return;                                         // no such station on the soft-AP
            var mac = MacFor(ifx, target);
            var r = new byte[42];
            Array.Copy(f, 6, r, 0, 6);
            Array.Copy(mac, 0, r, 6, 6);
            r[12] = 0x08; r[13] = 0x06;
            r[14] = 0; r[15] = 1; r[16] = 0x08; r[17] = 0; r[18] = 6; r[19] = 4; r[20] = 0; r[21] = 2;
            Array.Copy(mac, 0, r, 22, 6);
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(28), target);
            Array.Copy(f, 22, r, 32, 6);
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(38), sender);
            Deliver?.Invoke(ifx, r);
        }

        private void Ipv4(int ifx, byte[] f, double t)
        {
            var i = _if[ifx];
            int ihl = (f[14] & 0xF) * 4;
            int total = f[16] << 8 | f[17];
            if (ihl < 20 || 14 + total > f.Length || total < ihl) return;
            int proto = f[23];
            uint src = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(26));
            uint dst = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(30));
            var payload = f.AsSpan(14 + ihl, total - ihl).ToArray();
            switch (proto)
            {
                case 17 when payload.Length >= 8:
                {
                    ushort sport = BinaryPrimitives.ReadUInt16BigEndian(payload), dport = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(2));
                    int len = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(4)), payload.Length);
                    var data = payload.AsSpan(8, Math.Max(0, len - 8)).ToArray();
                    if (dport == 67 && sport == 68)
                    {
                        if (ifx == 0) Dhcp(data);   // the AP's server; the soft-AP's own runs on the chip
                        return;
                    }

                    if (!i.Up || src != i.Ip) return;
                    Bound(false, sport);   // replies come back to the port it sent from
                    _net.UdpSend(dst, dport, sport, data, t);   // the subnet's .255 and 255.255.255.255 go to all
                    return;
                }

                case 1 when payload.Length >= 8 && payload[0] == 8 && i.Up && ((ifx == 0 && dst == i.Gateway) || (ifx == 1 && i.Hosts.ContainsKey(dst))):
                {
                    // ping to the gateway (the AP answers) or to a station on the soft-AP (its stack does)
                    var reply = (byte[])payload.Clone();
                    reply[0] = 0;
                    reply[2] = reply[3] = 0;
                    ushort sum = Checksum(reply, 0, reply.Length, 0);
                    reply[2] = (byte)(sum >> 8);
                    reply[3] = (byte)sum;
                    SendIp(ifx, dst, src, 1, reply);
                    return;
                }

                case 6 when payload.Length >= 20 && i.Up && src == i.Ip:
                    Tcp(ifx, dst, payload, t);
                    return;
            }
        }

        // ================================================================ DHCP (the AP's server)

        private void Dhcp(byte[] m)
        {
            var i = _if[0];
            if (!i.Up || m.Length < 240 || m[0] != 1 || BinaryPrimitives.ReadUInt32BigEndian(m.AsSpan(236)) != 0x63825363) return;
            int type = 0;
            for (int k = 240; k + 1 < m.Length && m[k] != 255;)
            {
                if (m[k] == 0)
                {
                    k++;
                    continue;
                }

                if (m[k] == 53 && k + 2 < m.Length) type = m[k + 2];
                k += 2 + m[k + 1];
            }

            int reply = type == 1 ? 2 : type == 3 ? 5 : 0;   // DISCOVER → OFFER, REQUEST → ACK
            if (reply == 0) return;
            var r = new byte[300];
            r[0] = 2; r[1] = 1; r[2] = 6;
            Array.Copy(m, 4, r, 4, 4);       // xid
            Array.Copy(m, 10, r, 10, 2);     // flags
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(16), i.Ip);        // yiaddr
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(20), i.Gateway);   // siaddr
            Array.Copy(m, 28, r, 28, 16);    // chaddr
            BinaryPrimitives.WriteUInt32BigEndian(r.AsSpan(236), 0x63825363);
            int o = 240;
            void Option(byte code, params byte[] value)
            {
                r[o++] = code;
                r[o++] = (byte)value.Length;
                Array.Copy(value, 0, r, o, value.Length);
                o += value.Length;
            }

            byte[] Addr(uint a) => new[] { (byte)a, (byte)(a >> 8), (byte)(a >> 16), (byte)(a >> 24) };
            Option(53, (byte)reply);
            Option(54, Addr(i.Gateway));
            Option(51, 0, 0, 0x1C, 0x20);   // 7200 s
            Option(1, 255, 255, 255, 0);
            Option(3, Addr(i.Gateway));
            Option(6, Addr(i.Gateway));
            r[o++] = 255;
            SendUdp(0, i.Gateway, 67, 0xFFFFFFFF, 68, r.AsSpan(0, Math.Max(o, 300)).ToArray(), broadcast: true);
        }

        // ================================================================ to the chip

        /// <summary>Takes what the world has for the chip: datagrams on its bound ports, stream data, new connections.</summary>
        public void Poll(double t)
        {
            if (!_if[0].Up && !_if[1].Up) return;
            foreach (ushort port in _udpPorts)
            {
                for (int n = 0; n < 8; n++)
                {
                    var p = _net.UdpReceive(port, t);
                    if (p == null) break;
                    int ifx = Route(p.Value.Ip);
                    if (ifx >= 0) SendUdp(ifx, p.Value.Ip, p.Value.Port, _if[ifx].Ip, port, p.Value.Data);
                }
            }

            foreach (ushort port in _tcpListen)
            {
                for (int n = 0; n < 4; n++)
                {
                    var (id, ip, remote) = _net.StreamAccept(port);
                    if (id <= 0) break;
                    int ifx = Route(ip);
                    if (ifx < 0)
                    {
                        _net.StreamClose(id);
                        continue;
                    }

                    var link = new TcpLink { Id = id, Ifx = ifx, LocalPort = port, RemoteIp = ip, RemotePort = remote, SndUna = _isn, SndNxt = _isn + 1, State = TcpState.SynSent, Window = 1460 };
                    _isn += 0x01000000;
                    _tcp.Add(link);
                    SendTcp(link, link.SndUna, 0, TcpSyn, Array.Empty<byte>(), mss: true);
                    link.LastSent = t;
                }
            }

            foreach (var link in _tcp.ToList()) PumpTcp(link, t);
        }

        private void SendUdp(int ifx, uint src, ushort sport, uint dst, ushort dport, byte[] data, bool broadcast = false)
        {
            var u = new byte[8 + data.Length];
            BinaryPrimitives.WriteUInt16BigEndian(u, sport);
            BinaryPrimitives.WriteUInt16BigEndian(u.AsSpan(2), dport);
            BinaryPrimitives.WriteUInt16BigEndian(u.AsSpan(4), (ushort)u.Length);
            Array.Copy(data, 0, u, 8, data.Length);
            ushort sum = Checksum(u, 0, u.Length, Pseudo(src, dst, 17, u.Length));
            if (sum == 0) sum = 0xFFFF;
            u[6] = (byte)(sum >> 8);
            u[7] = (byte)sum;
            SendIp(ifx, src, dst, 17, u, broadcast);
        }

        private void SendIp(int ifx, uint src, uint dst, byte proto, byte[] payload, bool broadcast = false)
        {
            var f = new byte[14 + 20 + payload.Length];
            Array.Copy(broadcast ? new byte[] { 255, 255, 255, 255, 255, 255 } : _if[ifx].Mac, 0, f, 0, 6);
            Array.Copy(MacFor(ifx, src), 0, f, 6, 6);
            f[12] = 0x08;
            f[14] = 0x45;
            BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(16), (ushort)(20 + payload.Length));
            BinaryPrimitives.WriteUInt16BigEndian(f.AsSpan(18), _ipId++);
            f[22] = 64;
            f[23] = proto;
            BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(26), src);
            BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(30), dst);
            ushort sum = Checksum(f, 14, 20, 0);
            f[24] = (byte)(sum >> 8);
            f[25] = (byte)sum;
            Array.Copy(payload, 0, f, 34, payload.Length);
            Deliver?.Invoke(ifx, f);
        }

        private static uint Pseudo(uint src, uint dst, int proto, int length)
        {
            uint s = 0;
            for (int i = 0; i < 4; i += 2)
            {
                s += (uint)(((byte)(src >> (8 * i))) << 8 | (byte)(src >> (8 * i + 8)));
                s += (uint)(((byte)(dst >> (8 * i))) << 8 | (byte)(dst >> (8 * i + 8)));
            }

            return s + (uint)proto + (uint)length;
        }

        private static ushort Checksum(byte[] b, int at, int length, uint sum)
        {
            for (int i = 0; i + 1 < length; i += 2) sum += (uint)(b[at + i] << 8 | b[at + i + 1]);
            if ((length & 1) != 0) sum += (uint)(b[at + length - 1] << 8);
            while (sum >> 16 != 0) sum = (sum & 0xFFFF) + (sum >> 16);
            return (ushort)~sum;
        }

        // ================================================================ TCP: the world's end of each connection

        private const int TcpFin = 1, TcpSyn = 2, TcpRst = 4, TcpPsh = 8, TcpAck = 16;
        private const int Mss = 1436;

        private enum TcpState { SynSent, SynReceived, Established, Closing, Closed }

        private sealed class TcpLink
        {
            public int Id, Ifx;
            public ushort LocalPort, RemotePort;   // the chip's port, the far node's
            public uint RemoteIp;
            public uint SndUna, SndNxt, RcvNxt;
            public int Window;
            public TcpState State;
            public bool FinSent, FinReceived, RemoteClosed;
            public readonly List<byte> Unacked = new List<byte>();
            public double LastSent;
        }

        private TcpLink? Find(uint remoteIp, ushort remotePort, ushort localPort) =>
            _tcp.FirstOrDefault(l => l.RemoteIp == remoteIp && l.RemotePort == remotePort && l.LocalPort == localPort);

        private void Tcp(int ifx, uint dst, byte[] s, double t)
        {
            ushort sport = BinaryPrimitives.ReadUInt16BigEndian(s), dport = BinaryPrimitives.ReadUInt16BigEndian(s.AsSpan(2));
            uint seq = BinaryPrimitives.ReadUInt32BigEndian(s.AsSpan(4)), ack = BinaryPrimitives.ReadUInt32BigEndian(s.AsSpan(8));
            int off = (s[12] >> 4) * 4, flags = s[13];
            int window = BinaryPrimitives.ReadUInt16BigEndian(s.AsSpan(14));
            if (off < 20 || off > s.Length) return;
            var data = s.AsSpan(off).ToArray();
            var link = Find(dst, dport, sport);
            if ((flags & TcpRst) != 0)
            {
                if (link != null)
                {
                    _net.StreamClose(link.Id);
                    _tcp.Remove(link);
                }

                return;
            }

            if ((flags & TcpSyn) != 0 && (flags & TcpAck) == 0)
            {
                // the chip connects: the far node must be listening
                if (link != null)
                {
                    SendTcp(link, link.SndUna, link.RcvNxt, TcpSyn | TcpAck, Array.Empty<byte>(), mss: true);   // its SYN again
                    return;
                }

                int id = _net.StreamConnect(dst, dport, t);
                if (id == -2) return;   // unreachable: no answer, its SYN times out
                link = new TcpLink { Id = id, Ifx = ifx, LocalPort = sport, RemoteIp = dst, RemotePort = dport, SndUna = _isn, SndNxt = _isn + 1, RcvNxt = seq + 1, Window = window, State = TcpState.SynReceived };
                _isn += 0x01000000;
                if (id < 0)
                {
                    SendTcp(link, 0, seq + 1, TcpRst | TcpAck, Array.Empty<byte>());   // refused
                    Log?.Invoke($"TCP {Esp32WifiText.Ip(dst)}:{dport} reddedildi (dinleyen yok)");
                    return;
                }

                _tcp.Add(link);
                SendTcp(link, link.SndUna, link.RcvNxt, TcpSyn | TcpAck, Array.Empty<byte>(), mss: true);
                link.LastSent = t;
                return;
            }

            if (link == null)
            {
                if ((flags & TcpAck) != 0) SendRst(ifx, dst, dport, sport, ack);
                return;
            }

            if (link.State == TcpState.SynSent)
            {
                if ((flags & (TcpSyn | TcpAck)) == (TcpSyn | TcpAck) && ack == link.SndNxt)
                {
                    link.RcvNxt = seq + 1;
                    link.SndUna = ack;
                    link.Window = window;
                    link.State = TcpState.Established;
                    SendTcp(link, link.SndNxt, link.RcvNxt, TcpAck, Array.Empty<byte>());
                }

                return;
            }

            if ((flags & TcpAck) != 0)
            {
                int acked = (int)(ack - link.SndUna);
                if (acked > 0 && acked <= (int)(link.SndNxt - link.SndUna))
                {
                    int bytes = Math.Min(acked, link.Unacked.Count);
                    link.Unacked.RemoveRange(0, bytes);
                    link.SndUna = ack;
                }

                link.Window = window;
                if (link.State == TcpState.SynReceived && ack == link.SndNxt) link.State = TcpState.Established;
            }

            if (seq != link.RcvNxt)
            {
                if (data.Length > 0 || (flags & TcpFin) != 0) SendTcp(link, link.SndNxt, link.RcvNxt, TcpAck, Array.Empty<byte>());   // a duplicate: ack again
                return;
            }

            if (data.Length > 0)
            {
                if (!link.RemoteClosed && _net.StreamSend(link.Id, data, t) < 0)
                {
                    link.RemoteClosed = true;
                    SendTcp(link, link.SndNxt, link.RcvNxt + (uint)data.Length, TcpRst | TcpAck, Array.Empty<byte>());
                    _tcp.Remove(link);
                    return;
                }

                link.RcvNxt += (uint)data.Length;
            }

            if ((flags & TcpFin) != 0)
            {
                link.RcvNxt++;
                link.FinReceived = true;
                _net.StreamClose(link.Id);
            }

            if (data.Length > 0 || (flags & TcpFin) != 0) SendTcp(link, link.SndNxt, link.RcvNxt, TcpAck, Array.Empty<byte>());
            if (link.FinSent && link.FinReceived && link.SndUna == link.SndNxt) _tcp.Remove(link);
        }

        private void PumpTcp(TcpLink link, double t)
        {
            if (link.State == TcpState.SynSent || link.State == TcpState.SynReceived)
            {
                if (t - link.LastSent > 1.0)
                {
                    int flags = link.State == TcpState.SynSent ? TcpSyn : TcpSyn | TcpAck;
                    SendTcp(link, link.SndUna, link.RcvNxt, flags, Array.Empty<byte>(), mss: true);
                    link.LastSent = t;
                }

                return;
            }

            // Unacknowledged data the chip did not take (its receive buffers were full): send again.
            if (link.Unacked.Count > 0 && t - link.LastSent > 0.5)
            {
                int n = Math.Min(Math.Min(Mss, link.Unacked.Count), Math.Max(1, link.Window));
                SendTcp(link, link.SndUna, link.RcvNxt, TcpAck | TcpPsh, link.Unacked.GetRange(0, n).ToArray());
                link.LastSent = t;
            }

            if (!link.FinSent)
            {
                int room = link.Window - (int)(link.SndNxt - link.SndUna);
                while (room > 0)
                {
                    var buffer = new byte[Math.Min(Mss, room)];
                    int got = link.RemoteClosed ? -1 : _net.StreamReceive(link.Id, buffer, t);
                    if (got <= 0)
                    {
                        if (got < 0) link.RemoteClosed = true;
                        break;
                    }

                    var bytes = buffer.AsSpan(0, got).ToArray();
                    SendTcp(link, link.SndNxt, link.RcvNxt, TcpAck | TcpPsh, bytes);
                    link.Unacked.AddRange(bytes);
                    link.SndNxt += (uint)got;
                    link.LastSent = t;
                    room -= got;
                }

                if (link.RemoteClosed && link.Unacked.Count == 0)
                {
                    SendTcp(link, link.SndNxt, link.RcvNxt, TcpFin | TcpAck, Array.Empty<byte>());
                    link.SndNxt++;
                    link.FinSent = true;
                    link.LastSent = t;
                }
            }
            else if (link.SndUna != link.SndNxt && t - link.LastSent > 0.5)
            {
                SendTcp(link, link.SndNxt - 1, link.RcvNxt, TcpFin | TcpAck, Array.Empty<byte>());
                link.LastSent = t;
            }

            if (link.FinSent && link.FinReceived && link.SndUna == link.SndNxt) _tcp.Remove(link);
        }

        private void SendRst(int ifx, uint remoteIp, ushort remotePort, ushort localPort, uint seq)
        {
            var link = new TcpLink { Ifx = ifx, RemoteIp = remoteIp, RemotePort = remotePort, LocalPort = localPort };
            SendTcp(link, seq, 0, TcpRst, Array.Empty<byte>());
        }

        private void SendTcp(TcpLink link, uint seq, uint ack, int flags, byte[] data, bool mss = false)
        {
            int header = mss ? 24 : 20;
            var s = new byte[header + data.Length];
            BinaryPrimitives.WriteUInt16BigEndian(s, link.RemotePort);
            BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(2), link.LocalPort);
            BinaryPrimitives.WriteUInt32BigEndian(s.AsSpan(4), seq);
            BinaryPrimitives.WriteUInt32BigEndian(s.AsSpan(8), (flags & TcpAck) != 0 ? ack : 0);
            s[12] = (byte)(header / 4 << 4);
            s[13] = (byte)flags;
            BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(14), 8192);
            if (mss)
            {
                s[20] = 2;
                s[21] = 4;
                BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(22), Mss);
            }

            Array.Copy(data, 0, s, header, data.Length);
            uint own = _if[link.Ifx].Ip;
            ushort sum = Checksum(s, 0, s.Length, Pseudo(link.RemoteIp, own, 6, s.Length));
            s[16] = (byte)(sum >> 8);
            s[17] = (byte)sum;
            SendIp(link.Ifx, link.RemoteIp, own, 6, s);
        }
    }

    internal static class Esp32WifiText
    {
        public static string Ip(uint ip) => $"{ip & 0xFF}.{(ip >> 8) & 0xFF}.{(ip >> 16) & 0xFF}.{ip >> 24}";
    }
}
