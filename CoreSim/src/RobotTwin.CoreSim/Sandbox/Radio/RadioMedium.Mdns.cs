using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>[mdns] A service a board announces: _type._proto, its instance name, port and host.</summary>
    public sealed class MdnsService
    {
        public string Type = string.Empty;    // "http" (without the underscore)
        public string Proto = string.Empty;   // "tcp" / "udp"
        public string Instance = string.Empty;
        public ushort Port;
        public string Host = string.Empty;    // without ".local"
    }

    public sealed partial class RadioMedium
    {
        /// <summary>224.0.0.251 as the medium keeps addresses (little-endian, as on the wire).</summary>
        public const uint MdnsGroup = 0xFB0000E0;

        public const ushort MdnsPort = 5353;

        /// <summary>
        /// [mdns] What emulated boards announced (their firmware's own mDNS responder, read off the packets they multicast):
        /// each board's host name and address, and its services.
        /// </summary>
        private readonly Dictionary<Esp32RadioNode, (string Host, uint Ip, List<MdnsService> Services)> _mdnsLearned =
            new Dictionary<Esp32RadioNode, (string, uint, List<MdnsService>)>();

        // ---------------------------------------------------------------- the fast mode's ESPmDNS

        /// <summary>[mdns] MDNS.begin: the board answers to "host.local" on its network from now on.</summary>
        public bool MdnsBegin(Esp32RadioNode e, string host)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(host)) return false;
                e.MdnsHost = Normalise(host);
                return true;
            }
        }

        public void MdnsEnd(Esp32RadioNode e)
        {
            lock (_gate)
            {
                e.MdnsHost = null;
                e.MdnsServices.Clear();
            }
        }

        /// <summary>[mdns] MDNS.addService("http", "tcp", 80): announced with the board's host name as its instance.</summary>
        public bool MdnsAddService(Esp32RadioNode e, string type, string proto, ushort port)
        {
            lock (_gate)
            {
                if (e.MdnsHost == null) return false;
                type = type.TrimStart('_').ToLowerInvariant();
                proto = proto.TrimStart('_').ToLowerInvariant();
                e.MdnsServices.RemoveAll(s => s.Type == type && s.Proto == proto);
                e.MdnsServices.Add(new MdnsService { Type = type, Proto = proto, Port = port, Instance = e.MdnsHost, Host = e.MdnsHost });
                return true;
            }
        }

        /// <summary>[mdns] MDNS.queryHost: the address of a board on the same network that answers to the name, or 0.</summary>
        public uint MdnsQueryHost(Esp32RadioNode e, string host, double t)
        {
            lock (_gate)
            {
                var net = NetworkOf(e);
                if (net == null) return 0;
                host = Normalise(host);
                foreach (var r in Members(net))
                {
                    if (r == e) continue;
                    if (r.MdnsHost == host && !double.IsNaN(Transfer(r, e, net, 60, t, 0))) return IpOn(r, net);
                    if (_mdnsLearned.TryGetValue(r, out var l) && l.Host == host && !double.IsNaN(Transfer(r, e, net, 60, t, 0))) return l.Ip;
                }

                return 0;
            }
        }

        /// <summary>[mdns] MDNS.queryService: the boards on the network offering _type._proto: host, address, port.</summary>
        public List<(string Host, uint Ip, ushort Port)> MdnsQueryService(Esp32RadioNode e, string type, string proto, double t)
        {
            lock (_gate)
            {
                var found = new List<(string, uint, ushort)>();
                var net = NetworkOf(e);
                if (net == null) return found;
                type = type.TrimStart('_').ToLowerInvariant();
                proto = proto.TrimStart('_').ToLowerInvariant();
                foreach (var r in Members(net))
                {
                    if (r == e || double.IsNaN(Transfer(r, e, net, 120, t, 0))) continue;
                    foreach (var s in r.MdnsServices.Where(s => s.Type == type && s.Proto == proto)) found.Add((s.Host, IpOn(r, net), s.Port));
                    if (_mdnsLearned.TryGetValue(r, out var l))
                        foreach (var s in l.Services.Where(s => s.Type == type && s.Proto == proto)) found.Add((s.Host.Length > 0 ? s.Host : l.Host, l.Ip, s.Port));
                }

                return found;
            }
        }

        private static string Normalise(string name)
        {
            name = name.Trim().TrimEnd('.').ToLowerInvariant();
            return name.EndsWith(".local", StringComparison.Ordinal) ? name.Substring(0, name.Length - 6) : name;
        }

        // ---------------------------------------------------------------- packets of the emulated boards' responders

        /// <summary>
        /// [mdns] A packet an emulated board multicast to 224.0.0.251:5353 (under the medium's lock): its answers are learned
        /// (A, SRV, PTR: the board's name and services, for the fast boards' queries), and its questions about a fast board's
        /// name or services are answered as that board's responder would, back to the asker's port 5353.
        /// </summary>
        private void MdnsSeen(Esp32RadioNode e, WifiNetwork net, uint srcIp, byte[] packet, double t)
        {
            if (packet.Length < 12) return;
            bool response = (packet[2] & 0x80) != 0;
            int qd = packet[4] << 8 | packet[5], an = packet[6] << 8 | packet[7], ns = packet[8] << 8 | packet[9], ar = packet[10] << 8 | packet[11];
            int pos = 12;
            var questions = new List<(string Name, int Type)>();
            try
            {
                for (int i = 0; i < qd; i++)
                {
                    string name = ReadName(packet, ref pos);
                    int type = packet[pos] << 8 | packet[pos + 1];
                    pos += 4;
                    questions.Add((name, type));
                }

                if (response || an + ns + ar > 0)
                {
                    for (int i = 0; i < an + ns + ar; i++)
                    {
                        string name = ReadName(packet, ref pos);
                        int type = packet[pos] << 8 | packet[pos + 1];
                        uint ttl = (uint)(packet[pos + 4] << 24 | packet[pos + 5] << 16 | packet[pos + 6] << 8 | packet[pos + 7]);
                        int len = packet[pos + 8] << 8 | packet[pos + 9];
                        int data = pos + 10;
                        pos = data + len;
                        if (pos > packet.Length) return;
                        // a probe's authority records (a question being asked) are claims, not yet the board's: learned too
                        Learn(e, name, type, ttl, packet, data, len, srcIp);
                    }
                }
            }
            catch (IndexOutOfRangeException)
            {
                return;   // a damaged packet: what was read stands
            }

            if (response) return;
            foreach (var (name, type) in questions) Answer(e, net, name, type, t);
        }

        private void Learn(Esp32RadioNode e, string name, int type, uint ttl, byte[] p, int at, int len, uint srcIp)
        {
            if (!_mdnsLearned.TryGetValue(e, out var l)) l = (string.Empty, srcIp, new List<MdnsService>());
            switch (type)
            {
                case 1 when len == 4 && name.EndsWith(".local", StringComparison.OrdinalIgnoreCase):   // A: the board's name
                    l = ttl == 0 ? (string.Empty, srcIp, l.Services) : (Normalise(name), BitConverter.ToUInt32(p, at), l.Services);
                    break;
                case 33 when len >= 7:   // SRV: instance._type._proto.local → port, target
                {
                    int port = p[at + 4] << 8 | p[at + 5];
                    int q = at + 6;
                    string target = Normalise(ReadName(p, ref q));
                    var parts = name.Split('.');
                    if (parts.Length >= 4)
                    {
                        string type_ = parts[parts.Length - 3].TrimStart('_').ToLowerInvariant(), proto = parts[parts.Length - 2].TrimStart('_').ToLowerInvariant();
                        string instance = string.Join(".", parts.Take(parts.Length - 3));
                        l.Services.RemoveAll(s => s.Instance == instance && s.Type == type_ && s.Proto == proto);
                        if (ttl > 0) l.Services.Add(new MdnsService { Type = type_, Proto = proto, Instance = instance, Port = (ushort)port, Host = target });
                    }

                    break;
                }
            }

            _mdnsLearned[e] = l;
        }

        /// <summary>A question the fast boards on the network answer: their name (A) or their services (PTR, with SRV and A).</summary>
        private void Answer(Esp32RadioNode asker, WifiNetwork net, string name, int qtype, double t)
        {
            foreach (var r in Members(net))
            {
                if (r == asker || r.MdnsHost == null) continue;
                uint ip = IpOn(r, net);
                var answers = new List<byte[]>();
                var extra = new List<byte[]>();
                string host = r.MdnsHost + ".local";
                if ((qtype == 1 || qtype == 255) && string.Equals(name, host, StringComparison.OrdinalIgnoreCase))
                    answers.Add(Record(host, 1, 120, BitConverter.GetBytes(ip)));
                foreach (var s in r.MdnsServices)
                {
                    string service = $"_{s.Type}._{s.Proto}.local", instance = $"{s.Instance}.{service}";
                    if ((qtype != 12 && qtype != 255) || !string.Equals(name, service, StringComparison.OrdinalIgnoreCase)) continue;
                    answers.Add(Record(service, 12, 4500, Name(instance)));
                    var srv = new List<byte> { 0, 0, 0, 0, (byte)(s.Port >> 8), (byte)s.Port };
                    srv.AddRange(Name(host));
                    extra.Add(Record(instance, 33, 120, srv.ToArray()));
                    extra.Add(Record(instance, 16, 4500, new byte[] { 0 }));
                    extra.Add(Record(host, 1, 120, BitConverter.GetBytes(ip)));
                }

                if (answers.Count == 0) continue;
                var packet = new List<byte> { 0, 0, 0x84, 0, 0, 0, 0, (byte)answers.Count, 0, 0, 0, (byte)extra.Count };
                foreach (var a in answers.Concat(extra)) packet.AddRange(a);
                double at = Transfer(r, asker, net, packet.Count + 8, t, 0);
                if (double.IsNaN(at) || !asker.Udp.TryGetValue(MdnsPort, out var q)) continue;
                q.Enqueue(new NetPacket { SrcIp = ip, SrcPort = MdnsPort, DstIp = MdnsGroup, DstPort = MdnsPort, Data = packet.ToArray(), ArriveAt = at });
            }
        }

        private static byte[] Record(string name, int type, uint ttl, byte[] data)
        {
            var b = new List<byte>(Name(name));
            b.Add((byte)(type >> 8));
            b.Add((byte)type);
            b.Add(type == 12 ? (byte)0x00 : (byte)0x80);   // class IN, cache-flush for the unique records
            b.Add(1);
            b.Add((byte)(ttl >> 24));
            b.Add((byte)(ttl >> 16));
            b.Add((byte)(ttl >> 8));
            b.Add((byte)ttl);
            b.Add((byte)(data.Length >> 8));
            b.Add((byte)data.Length);
            b.AddRange(data);
            return b.ToArray();
        }

        private static byte[] Name(string name)
        {
            var b = new List<byte>();
            foreach (var label in name.TrimEnd('.').Split('.'))
            {
                var bytes = Encoding.UTF8.GetBytes(label);
                b.Add((byte)Math.Min(63, bytes.Length));
                b.AddRange(bytes.Take(63));
            }

            b.Add(0);
            return b.ToArray();
        }

        /// <summary>A DNS name at <paramref name="pos"/> (labels, compression pointers followed), moving past it.</summary>
        private static string ReadName(byte[] p, ref int pos)
        {
            var labels = new List<string>();
            int at = pos, jumps = 0;
            bool jumped = false;
            while (true)
            {
                int len = p[at];
                if (len == 0)
                {
                    at++;
                    break;
                }

                if ((len & 0xC0) == 0xC0)
                {
                    int target = (len & 0x3F) << 8 | p[at + 1];
                    if (!jumped) pos = at + 2;
                    jumped = true;
                    at = target;
                    if (++jumps > 16) break;
                    continue;
                }

                labels.Add(Encoding.UTF8.GetString(p, at + 1, len));
                at += 1 + len;
            }

            if (!jumped) pos = at;
            return string.Join(".", labels);
        }
    }
}
