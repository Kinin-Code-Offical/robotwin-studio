using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace RobotTwin.CoreSim.Industrial.OpcUa
{
    /// <summary>
    /// [opcua-client] A small OPC UA client (Part 6 binary over UA TCP, security None, an anonymous session): what a PLC's
    /// UA_READ and UA_WRITE need to read and write another server's variables. Synchronous: one request at a time, on
    /// the caller's thread (the PLC runs it beside its scan).
    /// </summary>
    public sealed class UaClient : IDisposable
    {
        private readonly TcpClient _tcp = new TcpClient();
        private NetworkStream _s = null!;
        private uint _channel, _token, _sequence = 1, _request = 1, _handle = 1;
        private NodeId _auth;

        private UaClient(string url)
        {
            Url = url;
        }

        public string Url { get; }

        public bool Connected => _tcp.Connected;

        /// <summary>Connects to opc.tcp://host:port[/path], opens a secure channel (None) and an anonymous session.</summary>
        public static UaClient Connect(string url, int timeoutMs = 3000)
        {
            var (host, port) = Endpoint(url);
            var c = new UaClient(url);
            try
            {
                var connect = c._tcp.ConnectAsync(host, port);
                if (!connect.Wait(timeoutMs)) throw new IOException($"{url} yanıt vermedi.");
                c._tcp.ReceiveTimeout = c._tcp.SendTimeout = timeoutMs;
                c._s = c._tcp.GetStream();
                c.Hello();
                c.OpenChannel();
                c.OpenSession();
                return c;
            }
            catch
            {
                c.Dispose();
                throw;
            }
        }

        /// <summary>"opc.tcp://host:4840/path" → (host, 4840).</summary>
        public static (string Host, int Port) Endpoint(string url)
        {
            const string scheme = "opc.tcp://";
            if (!url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) throw new FormatException($"'{url}' bir OPC UA adresi değil (opc.tcp://ana-makine:4840).");
            string rest = url.Substring(scheme.Length);
            int slash = rest.IndexOf('/');
            if (slash >= 0) rest = rest.Substring(0, slash);
            int colon = rest.LastIndexOf(':');
            if (colon < 0) return (rest, 4840);
            return int.TryParse(rest.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                ? (rest.Substring(0, colon), port)
                : throw new FormatException($"'{url}': bağlantı noktası okunamadı.");
        }

        /// <summary>"ns=1;s=Line.Speed", "ns=2;i=1001", "i=2258", "s=Tag" → the node's id; false when the text is not one.</summary>
        public static bool TryParseNodeId(string text, out NodeId id)
        {
            id = default;
            ushort ns = 0;
            string s = text.Trim();
            if (s.StartsWith("ns=", StringComparison.OrdinalIgnoreCase))
            {
                int semi = s.IndexOf(';');
                if (semi < 0 || !ushort.TryParse(s.Substring(3, semi - 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out ns)) return false;
                s = s.Substring(semi + 1);
            }

            if (s.StartsWith("i=", StringComparison.OrdinalIgnoreCase) && uint.TryParse(s.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint n))
            {
                id = new NodeId(ns, n);
                return true;
            }

            if (s.StartsWith("s=", StringComparison.OrdinalIgnoreCase) && s.Length > 2)
            {
                id = new NodeId(ns, s.Substring(2));
                return true;
            }

            return false;
        }

        /// <summary>A node's value (Read, attribute Value).</summary>
        public DataValue Read(NodeId node)
        {
            var r = Call(631, w =>
            {
                w.Double(0);
                w.Int32(0);   // timestamps: source
                w.Array(new[] { node }, id =>
                {
                    w.NodeId(id);
                    w.UInt32(13);
                    w.String(null);
                    w.QualifiedName(new QualifiedName(0, string.Empty));
                });
            }, 634, out uint status);
            if (status != 0) return new DataValue { HasValue = false, Status = status };
            var values = r.Array(r.DataValue);
            return values.Count == 1 ? values[0] : new DataValue { HasValue = false, Status = 0x80000000 };
        }

        /// <summary>Writes a node's value; the result's status code (0 good).</summary>
        public uint Write(NodeId node, Variant value)
        {
            var r = Call(673, w =>
            {
                w.Array(new[] { node }, id =>
                {
                    w.NodeId(id);
                    w.UInt32(13);
                    w.String(null);
                    w.DataValue(new DataValue { Value = value });
                });
            }, 676, out uint status);
            if (status != 0) return status;
            var results = r.Array(r.StatusCode);
            return results.Count == 1 ? results[0] : 0x80000000;
        }

        private void Hello()
        {
            var w = new UaWriter();
            w.UInt32(0);
            w.UInt32(65536);
            w.UInt32(65536);
            w.UInt32(0);
            w.UInt32(0);
            w.String(Url);
            Frame("HEL", w.ToArray());
            var (type, body) = Receive();
            if (type == "ERR") throw new IOException($"{Url}: sunucu bağlantıyı reddetti (0x{new UaReader(body).UInt32():X8}).");
            if (type != "ACK") throw new IOException($"{Url}: OPC UA sunucusu değil.");
        }

        private void OpenChannel()
        {
            var w = new UaWriter();
            w.UInt32(0);
            w.String(UaServer.SecurityPolicyNone);
            w.ByteString(null);
            w.ByteString(null);
            w.UInt32(_sequence++);
            w.UInt32(_request++);
            w.NodeId(NodeId.Ns0(446));
            Header(w);
            w.UInt32(0);
            w.Int32(0);   // issue
            w.Int32(1);   // None
            w.ByteString(null);
            w.UInt32(600000);
            Frame("OPN", w.ToArray());
            var (t, body) = Receive();
            if (t != "OPN") throw new IOException($"{Url}: güvenli kanal açılmadı ({t}).");
            var r = new UaReader(body);
            _channel = r.UInt32();
            r.String();
            r.ByteString();
            r.ByteString();
            r.UInt32();
            r.UInt32();
            r.NodeId();
            ResponseHeader(r, out uint status);
            if (status != 0) throw new IOException($"{Url}: güvenli kanal açılmadı (0x{status:X8}).");
            r.UInt32();
            r.UInt32();
            _token = r.UInt32();
        }

        private void OpenSession()
        {
            var r = Call(461, w =>
            {
                w.String("urn:robotwin:plc");
                w.String(null);
                w.LocalizedText(new LocalizedText("RobotWin PLC"));
                w.Int32(1);   // client
                w.String(null);
                w.String(null);
                w.Int32(0);
                w.String(null);
                w.String(Url);
                w.String("plc");
                w.ByteString(new byte[32]);
                w.ByteString(null);
                w.Double(600000);
                w.UInt32(0);
            }, 464, out uint status);
            if (status != 0) throw new IOException($"{Url}: oturum açılmadı (0x{status:X8}).");
            r.NodeId();
            _auth = r.NodeId();
            Call(467, w =>
            {
                w.String(null);
                w.ByteString(null);
                w.Int32(0);
                w.Int32(0);
                w.ExtensionObject(NodeId.Ns0(321), b => b.String("anonymous"));
                w.String(null);
                w.ByteString(null);
            }, 470, out status);
            if (status != 0) throw new IOException($"{Url}: oturum etkinleşmedi (0x{status:X8}).");
        }

        private void Header(UaWriter w)
        {
            w.NodeId(_auth);
            w.DateTime(DateTime.UtcNow);
            w.UInt32(_handle++);
            w.UInt32(0);
            w.String(null);
            w.UInt32(10000);
            w.ExtensionObject(null);
        }

        private static void ResponseHeader(UaReader r, out uint status)
        {
            r.DateTime();
            r.UInt32();
            status = r.StatusCode();
            r.SkipDiagnostics();
            r.Array(r.String);
            r.ExtensionObject();
        }

        private UaReader Call(uint type, Action<UaWriter> body, uint expected, out uint status)
        {
            var w = new UaWriter();
            w.UInt32(_channel);
            w.UInt32(_token);
            w.UInt32(_sequence++);
            w.UInt32(_request++);
            w.NodeId(NodeId.Ns0(type));
            Header(w);
            body(w);
            Frame("MSG", w.ToArray());
            var (t, reply) = Receive();
            if (t == "ERR") throw new IOException($"{Url}: sunucu hata verdi (0x{new UaReader(reply).UInt32():X8}).");
            var r = new UaReader(reply);
            r.UInt32();
            r.UInt32();
            r.UInt32();
            r.UInt32();
            uint answered = r.NodeId().Numeric;
            ResponseHeader(r, out status);
            if (answered != expected && status == 0) status = 0x80000000;   // a ServiceFault without a code
            return r;
        }

        private void Frame(string type, byte[] body)
        {
            var head = new byte[8];
            Encoding.ASCII.GetBytes(type + "F", 0, 4, head, 0);
            BitConverter.GetBytes((uint)body.Length + 8).CopyTo(head, 4);
            _s.Write(head, 0, 8);
            _s.Write(body, 0, body.Length);
        }

        private (string Type, byte[] Body) Receive()
        {
            var head = Take(8);
            uint size = BitConverter.ToUInt32(head, 4);
            if (size < 8 || size > 16 * 1024 * 1024) throw new IOException($"{Url}: geçersiz ileti boyu {size}.");
            return (Encoding.ASCII.GetString(head, 0, 3), Take((int)size - 8));
        }

        private byte[] Take(int n)
        {
            var b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int k = _s.Read(b, got, n - got);
                if (k <= 0) throw new IOException($"{Url}: bağlantı kapandı.");
                got += k;
            }

            return b;
        }

        public void Dispose()
        {
            try
            {
                _tcp.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
