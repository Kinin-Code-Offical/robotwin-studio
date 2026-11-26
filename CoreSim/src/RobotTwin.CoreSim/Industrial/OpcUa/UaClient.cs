using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace RobotTwin.CoreSim.Industrial.OpcUa
{
    /// <summary>
    /// [opcua-sec] How a client connects: the mode (None, or Basic256Sha256 signed / signed and encrypted), its own
    /// certificate, the server's (taken from GetEndpoints when not given) and a user name and password (else anonymous).
    /// </summary>
    public sealed class UaClientSecurity
    {
        public UaSecurityMode Mode { get; set; } = UaSecurityMode.None;
        public X509Certificate2? Certificate { get; set; }
        public X509Certificate2? ServerCertificate { get; set; }

        /// <summary>Checks the server's certificate when it comes from GetEndpoints (null: accept it).</summary>
        public UaPki? Pki { get; set; }

        public string? User { get; set; }
        public string? Password { get; set; }
    }

    /// <summary>[opcua-sec] The server answered with a bad status (a refused certificate, user or policy): its code.</summary>
    public sealed class UaServiceException : IOException
    {
        public UaServiceException(uint status, string message) : base(message)
        {
            Status = status;
        }

        public uint Status { get; }
    }

    /// <summary>
    /// [opcua-client] A small OPC UA client (Part 6 binary over UA TCP, [opcua-sec] security None or Basic256Sha256, an
    /// anonymous or named session): what a PLC's
    /// UA_READ and UA_WRITE need to read and write another server's variables. Synchronous: one request at a time, on
    /// the caller's thread (the PLC runs it beside its scan).
    /// </summary>
    public sealed class UaClient : IDisposable
    {
        private readonly TcpClient _tcp = new TcpClient();
        private NetworkStream _s = null!;
        private uint _channel, _token, _sequence = 1, _request = 1, _handle = 1;
        private NodeId _auth;
        private UaChannelSecurity _sec = UaChannelSecurity.Unsecured;
        private readonly UaClientSecurity _options;
        private byte[] _serverNonce = Array.Empty<byte>();

        private UaClient(string url, UaClientSecurity? security)
        {
            Url = url;
            _options = security ?? new UaClientSecurity();
        }

        /// <summary>[opcua-sec] The channel's mode once connected.</summary>
        public UaSecurityMode Mode => _sec.Mode;

        public string Url { get; }

        public bool Connected => _tcp.Connected;

        /// <summary>
        /// Connects to opc.tcp://host:port[/path], opens a secure channel (None, or [opcua-sec] Basic256Sha256 as asked) and a
        /// session (anonymous, or the given user's).
        /// </summary>
        public static UaClient Connect(string url, int timeoutMs = 3000, UaClientSecurity? security = null)
        {
            security ??= new UaClientSecurity();
            if (security.Mode != UaSecurityMode.None)
            {
                if (security.Certificate == null || !security.Certificate.HasPrivateKey) throw new ArgumentException("Güvenli bağlantı için istemcinin özel anahtarlı sertifikası gerekir.");
                if (security.ServerCertificate == null)
                {
                    // Discovery over an unsecured channel: the endpoint with this mode, and the server's certificate.
                    using var probe = Open(url, timeoutMs, null);
                    var endpoint = probe.GetEndpoints().FirstOrDefault(e => e.Policy == UaSecurityPolicy.Basic256Sha256 && e.Mode == security.Mode);
                    if (endpoint.Certificate == null) throw new IOException($"{url}: sunucu Basic256Sha256 {security.Mode} uç noktası sunmuyor.");
                    var server = UaCertificates.Load(endpoint.Certificate);
                    if (security.Pki != null && !security.Pki.Trusts(server)) throw new UaServiceException(UaSecurityException.BadCertificateUntrusted, $"{url}: sunucunun sertifikası güvenilir değil ({security.Pki.RejectedFolder}).");
                    security.ServerCertificate = server;
                }
            }

            var c = Open(url, timeoutMs, security);
            try
            {
                c.OpenSession();
                return c;
            }
            catch
            {
                c.Dispose();
                throw;
            }
        }

        private static UaClient Open(string url, int timeoutMs, UaClientSecurity? security)
        {
            var (host, port) = Endpoint(url);
            var c = new UaClient(url, security);
            try
            {
                var connect = c._tcp.ConnectAsync(host, port);
                if (!connect.Wait(timeoutMs)) throw new IOException($"{url} yanıt vermedi.");
                c._tcp.ReceiveTimeout = c._tcp.SendTimeout = timeoutMs;
                c._s = c._tcp.GetStream();
                c.Hello();
                c.OpenChannel();
                return c;
            }
            catch
            {
                c.Dispose();
                throw;
            }
        }

        /// <summary>The server's endpoints: policy, mode and its certificate.</summary>
        public List<(string Policy, UaSecurityMode Mode, byte[]? Certificate)> GetEndpoints()
        {
            var r = Call(428, w =>
            {
                w.String(Url);
                w.Int32(0);
                w.Int32(0);
            }, 431, out uint status);
            if (status != 0) throw new UaServiceException(status, $"{Url}: uç noktalar alınamadı (0x{status:X8}).");
            return r.Array(() => ReadEndpoint(r));
        }

        private static (string Policy, UaSecurityMode Mode, byte[]? Certificate) ReadEndpoint(UaReader r)
        {
            r.String();   // url
            r.String();   // application: uri, product uri, name, type, gateway, discovery profile, discovery urls
            r.String();
            r.LocalizedText();
            r.Int32();
            r.String();
            r.String();
            r.Array(r.String);
            byte[]? certificate = r.ByteString();
            var mode = (UaSecurityMode)r.Int32();
            string policy = r.String() ?? string.Empty;
            r.Array(() =>
            {
                r.String();
                r.Int32();
                r.String();
                r.String();
                r.String();
                return 0;
            });
            r.String();   // transport profile
            r.Byte();   // security level
            return (policy, mode, certificate);
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
            var (type, _, body) = Receive();
            if (type == "ERR") throw new UaServiceException(new UaReader(body, 8).UInt32(), $"{Url}: sunucu bağlantıyı reddetti (0x{new UaReader(body, 8).UInt32():X8}).");
            if (type != "ACK") throw new IOException($"{Url}: OPC UA sunucusu değil.");
        }

        private void OpenChannel()
        {
            bool secure = _options.Mode != UaSecurityMode.None && _options.ServerCertificate != null;
            var sec = secure
                ? new UaChannelSecurity(UaSecurityPolicy.Basic256Sha256, _options.Mode, _options.Certificate, _options.ServerCertificate)
                : new UaChannelSecurity(UaSecurityPolicy.None, UaSecurityMode.None, null, null);
            byte[] clientNonce = secure ? Nonce() : Array.Empty<byte>();
            var w = new UaWriter();
            w.UInt32(_sequence++);
            w.UInt32(_request++);
            w.NodeId(NodeId.Ns0(446));
            Header(w);
            w.UInt32(0);
            w.Int32(0);   // issue
            w.Int32((int)sec.Mode);
            w.ByteString(clientNonce);
            w.UInt32(600000);
            Send(sec.EncodeAsymmetric(0, w.ToArray()));
            var (t, _, message) = Receive();
            if (t == "ERR") throw new UaServiceException(new UaReader(message, 8).UInt32(), $"{Url}: güvenli kanal açılmadı (0x{new UaReader(message, 8).UInt32():X8}).");
            if (t != "OPN") throw new IOException($"{Url}: güvenli kanal açılmadı ({t}).");
            var head = UaChannelSecurity.ReadAsymmetricHeader(message);
            byte[] inner;
            try
            {
                inner = sec.DecodeAsymmetric(message, head.Body);
            }
            catch (UaSecurityException ex)
            {
                throw new IOException($"{Url}: sunucunun yanıtı doğrulanamadı ({ex.Message}).");
            }

            var r = new UaReader(inner);
            r.UInt32();
            r.UInt32();
            r.NodeId();
            ResponseHeader(r, out uint status);
            if (status != 0) throw new UaServiceException(status, $"{Url}: güvenli kanal açılmadı (0x{status:X8}).");
            r.UInt32();
            _channel = r.UInt32();
            _token = r.UInt32();
            r.DateTime();
            r.UInt32();
            byte[] serverNonce = r.ByteString() ?? Array.Empty<byte>();
            if (secure)
            {
                if (serverNonce.Length != UaSecurityPolicy.NonceLength) throw new IOException($"{Url}: sunucunun rastgele sayısı geçersiz.");
                sec.SetKeys(_token, UaChannelKeys.Derive(serverNonce, clientNonce), UaChannelKeys.Derive(clientNonce, serverNonce));
            }
            else
            {
                sec.SetToken(_token);
            }

            _sec = sec;
        }

        private static byte[] Nonce()
        {
            var b = new byte[UaSecurityPolicy.NonceLength];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        private void OpenSession()
        {
            bool secure = _sec.Secured;
            byte[] clientNonce = Nonce();
            var own = _options.Certificate;
            string applicationUri = (own != null ? UaCertificates.ApplicationUri(own) : null) ?? "urn:robotwin:plc";
            var r = Call(461, w =>
            {
                w.String(applicationUri);
                w.String(null);
                w.LocalizedText(new LocalizedText("RobotWin PLC"));
                w.Int32(1);   // client
                w.String(null);
                w.String(null);
                w.Int32(0);
                w.String(null);
                w.String(Url);
                w.String("plc");
                w.ByteString(clientNonce);
                w.ByteString(secure ? own!.RawData : null);
                w.Double(600000);
                w.UInt32(0);
            }, 464, out uint status);
            if (status != 0) throw new UaServiceException(status, $"{Url}: oturum açılmadı (0x{status:X8}).");
            r.NodeId();
            _auth = r.NodeId();
            r.Double();
            _serverNonce = r.ByteString() ?? Array.Empty<byte>();
            byte[]? serverDer = r.ByteString();
            r.Array(() => ReadEndpoint(r));
            r.Array(() =>
            {
                r.ByteString();
                r.ByteString();
                return 0;
            });
            r.String();
            byte[]? serverSignature = r.ByteString();
            var server = _options.ServerCertificate ?? (serverDer != null && serverDer.Length > 0 ? UaCertificates.Load(serverDer) : null);
            if (secure)
            {
                // [opcua-sec] the server proves it holds its key: its signature over this client's certificate and nonce
                if (!UaCertificates.Verify(_options.ServerCertificate!, UaChannelSecurity.Concat(own!.RawData, clientNonce), serverSignature))
                    throw new IOException($"{Url}: sunucunun oturum imzası doğrulanamadı.");
            }

            byte[]? userSecret = null;
            if (_options.User != null)
            {
                if (server == null) throw new IOException($"{Url}: parolayı şifrelemek için sunucunun sertifikası yok.");
                byte[] password = Encoding.UTF8.GetBytes(_options.Password ?? string.Empty);
                byte[] plain = UaChannelSecurity.Concat(BitConverter.GetBytes(password.Length + _serverNonce.Length), password, _serverNonce);
                userSecret = UaCertificates.Encrypt(server, plain);
            }

            Call(467, w =>
            {
                w.String(secure ? UaSecurityPolicy.RsaSha256 : null);
                w.ByteString(secure ? UaCertificates.Sign(own!, UaChannelSecurity.Concat(_options.ServerCertificate!.RawData, _serverNonce)) : null);
                w.Int32(0);
                w.Int32(0);
                if (_options.User != null)
                {
                    w.ExtensionObject(NodeId.Ns0(324), b =>
                    {
                        b.String("username");
                        b.String(_options.User);
                        b.ByteString(userSecret);
                        b.String(UaSecurityPolicy.RsaOaep);
                    });
                }
                else
                {
                    w.ExtensionObject(NodeId.Ns0(321), b => b.String("anonymous"));
                }

                w.String(null);
                w.ByteString(null);
            }, 470, out status);
            if (status != 0) throw new UaServiceException(status, $"{Url}: oturum etkinleşmedi (0x{status:X8}).");
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
            w.UInt32(_sequence++);
            w.UInt32(_request++);
            w.NodeId(NodeId.Ns0(type));
            Header(w);
            body(w);
            Send(_sec.EncodeSymmetric("MSG", 'F', _channel, w.ToArray()));
            var payload = new MemoryStream();
            while (true)
            {
                var (t, chunk, message) = Receive();
                if (t == "ERR") throw new UaServiceException(new UaReader(message, 8).UInt32(), $"{Url}: sunucu hata verdi (0x{new UaReader(message, 8).UInt32():X8}).");
                if (t != "MSG") throw new IOException($"{Url}: beklenmeyen ileti {t}.");
                byte[] inner;
                try
                {
                    inner = _sec.DecodeSymmetric(message);   // [opcua-sec] the server's keys check and open it
                }
                catch (UaSecurityException ex)
                {
                    throw new IOException($"{Url}: sunucunun iletisi doğrulanamadı ({ex.Message}).");
                }

                payload.Write(inner, 8, inner.Length - 8);
                if (chunk == 'F') break;
                if (chunk == 'A') throw new IOException($"{Url}: sunucu yanıtı yarıda kesti.");
            }

            var r = new UaReader(payload.ToArray());
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

        private void Send(byte[] message) => _s.Write(message, 0, message.Length);

        /// <summary>One whole message: its type, chunk flag and every byte (header included).</summary>
        private (string Type, char Chunk, byte[] Message) Receive()
        {
            var head = Take(8);
            uint size = BitConverter.ToUInt32(head, 4);
            if (size < 8 || size > 16 * 1024 * 1024) throw new IOException($"{Url}: geçersiz ileti boyu {size}.");
            return (Encoding.ASCII.GetString(head, 0, 3), (char)head[3], UaChannelSecurity.Concat(head, Take((int)size - 8)));
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
