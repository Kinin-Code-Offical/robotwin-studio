using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace RobotTwin.CoreSim.Industrial.OpcUa
{
    /// <summary>
    /// [opcua-sec] What a server offers: endpoints without security and with Basic256Sha256 (signed, or signed and
    /// encrypted), anonymous users and/or named users with passwords (read-only or not), and which client certificates it
    /// trusts. <see cref="Open"/> is the old behaviour: security None, anyone.
    /// </summary>
    public sealed class UaServerSecurity
    {
        /// <summary>An endpoint without security (discovery over an unsecured channel works either way).</summary>
        public bool AllowNone { get; set; } = true;

        /// <summary>Basic256Sha256 Sign and SignAndEncrypt endpoints (they need <see cref="Certificate"/>).</summary>
        public bool AllowSign { get; set; } = true;
        public bool AllowSignAndEncrypt { get; set; } = true;

        /// <summary>The server's application instance certificate with its private key; null: no secure endpoints.</summary>
        public X509Certificate2? Certificate { get; set; }

        /// <summary>The client certificates it accepts; null accepts any (a closed test network).</summary>
        public UaPki? Pki { get; set; }

        public bool AllowAnonymous { get; set; } = true;

        /// <summary>Named users: password, and whether they may write.</summary>
        public Dictionary<string, (string Password, bool CanWrite)> Users { get; } = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);

        public static UaServerSecurity Open => new UaServerSecurity { AllowSign = false, AllowSignAndEncrypt = false };

        internal bool Secure => Certificate != null && (AllowSign || AllowSignAndEncrypt);
    }

    /// <summary>
    /// [opcua] An OPC UA server over UA TCP with the binary encoding (Part 6), [opcua-sec] security policy None or
    /// Basic256Sha256 (signed, or signed and encrypted) and anonymous or named users:
    /// discovery (GetEndpoints, FindServers), sessions, Browse/BrowseNext, Read, Write, TranslateBrowsePathsToNodeIds,
    /// and subscriptions with monitored items (data changes and keep-alives through Publish), enough for a SCADA, an HMI
    /// or a client such as UaExpert to browse the application's variables, watch them change and write them.
    /// </summary>
    public sealed class UaServer : IDisposable
    {
        private const uint BadDecodingError = 0x80070000, BadServiceUnsupported = 0x800B0000, BadSessionIdInvalid = 0x80250000, BadNodeIdUnknown = 0x80340000,
            BadAttributeIdInvalid = 0x80350000, BadNotWritable = 0x803B0000, BadSubscriptionIdInvalid = 0x80280000, BadMonitoredItemIdInvalid = 0x80420000,
            BadNothingToDo = 0x800F0000, BadTcpMessageTypeInvalid = 0x807E0000, BadNoSubscription = 0x80790000, BadMessageNotAvailable = 0x807B0000,
            BadSecurityPolicyRejected = 0x80550000, BadIdentityTokenInvalid = 0x80200000, BadSessionNotActivated = 0x80270000, BadTooManyPublishRequests = 0x80780000,
            BadSecurityModeRejected = 0x80540000, BadCertificateUriInvalid = 0x80170000, BadUserAccessDenied = 0x801F0000, BadIdentityTokenRejected = 0x80210000,
            BadApplicationSignatureInvalid = 0x80580000, BadNonceInvalid = 0x80240000, BadCertificateInvalid = 0x80120000;

        public const string SecurityPolicyNone = "http://opcfoundation.org/UA/SecurityPolicy#None";
        public const string TransportProfile = "http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary";

        private readonly TcpListener _listener;
        private readonly List<Connection> _connections = new List<Connection>();
        private readonly Dictionary<uint, Session> _sessions = new Dictionary<uint, Session>();
        private readonly object _gate = new object();
        private readonly Thread _publisher;
        private volatile bool _running;
        private int _nextChannel, _nextSession = 1, _nextSubscription = 1;

        public UaServer(UaAddressSpace space, IPAddress address, int port, string name = "RobotWin Studio", UaServerSecurity? security = null)
        {
            Space = space;
            Name = name;
            Security = security ?? UaServerSecurity.Open;
            _listener = new TcpListener(address, port);
            _publisher = new Thread(PublishLoop) { IsBackground = true, Name = "OPC UA publish" };
        }

        public UaAddressSpace Space { get; }
        public string Name { get; }
        public UaServerSecurity Security { get; }

        /// <summary>[opcua-sec] Said once per cause: a client certificate refused, a user turned away.</summary>
        public event Action<string>? Notice;

        private readonly HashSet<string> _noticed = new HashSet<string>();

        private void Note(string key, string text)
        {
            lock (_noticed)
                if (!_noticed.Add(key)) return;
            Notice?.Invoke(text);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>The address clients connect to.</summary>
        public string EndpointUrl => $"opc.tcp://{(((IPEndPoint)_listener.LocalEndpoint).Address.Equals(IPAddress.Any) ? "localhost" : ((IPEndPoint)_listener.LocalEndpoint).Address.ToString())}:{Port}";

        public int Clients
        {
            get
            {
                lock (_gate) return _connections.Count;
            }
        }

        public int Sessions
        {
            get
            {
                lock (_gate) return _sessions.Count;
            }
        }

        public void Start()
        {
            _listener.Start();
            _running = true;
            _publisher.Start();
            _listener.BeginAcceptTcpClient(Accepted, null);
        }

        private void Accepted(IAsyncResult ar)
        {
            TcpClient client;
            try
            {
                client = _listener.EndAcceptTcpClient(ar);
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException || ex is InvalidOperationException)
            {
                return;
            }

            if (!_running)
            {
                client.Close();
                return;
            }

            var c = new Connection(this, client);
            lock (_gate) _connections.Add(c);
            new Thread(c.Run) { IsBackground = true, Name = "OPC UA client" }.Start();
            if (!_running) return;
            try
            {
                _listener.BeginAcceptTcpClient(Accepted, null);
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is InvalidOperationException || ex is SocketException)
            {
                // stopped between the check and the call (a program loaded, the world closed): nothing more to accept
            }
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
            }

            List<Connection> open;
            lock (_gate) open = _connections.ToList();
            foreach (var c in open) c.Close();
        }

        // ================================================================ sessions and subscriptions

        private sealed class Session
        {
            public uint Id;
            public NodeId Token;
            public bool Activated;
            public Connection? Channel;
            public byte[] ServerNonce = Array.Empty<byte>();
            public byte[]? ClientCertificate;
            public string? User;
            public bool CanWrite = true;
            public readonly Dictionary<uint, Subscription> Subscriptions = new Dictionary<uint, Subscription>();
            public readonly Queue<(Connection Channel, uint RequestId, uint Handle)> Publishes = new Queue<(Connection, uint, uint)>();
            public readonly Dictionary<uint, List<uint>> Acks = new Dictionary<uint, List<uint>>();
        }

        private sealed class Subscription
        {
            public uint Id;
            public double Interval;
            public uint KeepAlive;
            public uint Lifetime;
            public bool Enabled = true;
            public uint NextSequence = 1;
            public DateTime Due;
            public uint IdleIntervals;
            public bool FirstPublish = true;
            public readonly Dictionary<uint, Monitored> Items = new Dictionary<uint, Monitored>();
            public uint NextItem = 1;
        }

        private sealed class Monitored
        {
            public uint Id;
            public uint ClientHandle;
            public NodeId Node;
            public uint Attribute;
            public int Mode = 2;   // Reporting
            public double Sampling;
            public DateTime NextSample;
            public DataValue? Last;
            public readonly Queue<DataValue> Queue = new Queue<DataValue>();
            public uint QueueSize = 1;
        }

        /// <summary>Samples the monitored items and answers queued Publish requests: data changes when there are some, a keep-alive when due.</summary>
        private void PublishLoop()
        {
            while (_running)
            {
                Thread.Sleep(5);
                var now = DateTime.UtcNow;
                List<Session> sessions;
                lock (_gate) sessions = _sessions.Values.ToList();
                foreach (var s in sessions)
                {
                    lock (s)
                    {
                        foreach (var sub in s.Subscriptions.Values)
                        {
                            foreach (var m in sub.Items.Values)
                            {
                                if (m.Mode == 0 || now < m.NextSample) continue;
                                m.NextSample = now.AddMilliseconds(Math.Max(1, m.Sampling));
                                var v = ReadAttribute(m.Node, m.Attribute);
                                if (m.Last != null && SameValue(m.Last, v)) continue;
                                m.Last = v;
                                if (m.Mode != 2) continue;
                                m.Queue.Enqueue(v);
                                while (m.Queue.Count > m.QueueSize) m.Queue.Dequeue();
                            }

                            if (now < sub.Due) continue;
                            sub.Due = now.AddMilliseconds(sub.Interval);
                            var changes = sub.Enabled ? sub.Items.Values.Where(m => m.Queue.Count > 0).ToList() : new List<Monitored>();
                            bool keepAlive = changes.Count == 0 && (sub.FirstPublish || ++sub.IdleIntervals >= sub.KeepAlive);
                            if (changes.Count == 0 && !keepAlive) continue;
                            if (s.Publishes.Count == 0) continue;   // nothing to answer with yet: it stays queued
                            var (channel, requestId, handle) = s.Publishes.Dequeue();
                            sub.IdleIntervals = 0;
                            sub.FirstPublish = false;
                            var notifications = changes.SelectMany(m => m.Queue.Select(dv => (m.ClientHandle, dv))).ToList();
                            foreach (var m in changes) m.Queue.Clear();
                            uint sequence = changes.Count > 0 ? sub.NextSequence++ : sub.NextSequence;
                            var acks = s.Acks.TryGetValue(handle, out var a) ? a : new List<uint>();
                            s.Acks.Remove(handle);
                            channel.Send(829, requestId, handle, 0, w =>
                            {
                                w.UInt32(sub.Id);
                                w.Array(changes.Count > 0 ? new[] { sequence } : Array.Empty<uint>(), w.UInt32);   // available sequence numbers
                                w.Boolean(false);   // more notifications
                                w.UInt32(sequence);
                                w.DateTime(DateTime.UtcNow);
                                if (notifications.Count == 0) w.Int32(0);
                                else
                                {
                                    w.Int32(1);
                                    w.ExtensionObject(NodeId.Ns0(811), b =>   // DataChangeNotification
                                    {
                                        b.Array(notifications, n =>
                                        {
                                            b.UInt32(n.ClientHandle);
                                            b.DataValue(n.dv);
                                        });
                                        b.Int32(0);   // diagnostic infos
                                    });
                                }

                                w.Array(acks, x => w.StatusCode(x));
                                w.Int32(0);
                            });
                        }
                    }
                }
            }
        }

        private static bool SameValue(DataValue a, DataValue b) =>
            a.Status == b.Status && a.HasValue == b.HasValue && a.Value.Type == b.Value.Type && Equals(a.Value.Value, b.Value.Value);

        // ================================================================ reading and writing nodes

        private DataValue ReadAttribute(NodeId id, uint attribute)
        {
            var n = Space.Find(id);
            if (n == null) return new DataValue { HasValue = false, Status = BadNodeIdUnknown };
            Variant v;
            switch (attribute)
            {
                case 1: v = new Variant(UaType.NodeId, n.Id); break;
                case 2: v = new Variant(UaType.Int32, (int)n.Class); break;
                case 3: v = new Variant(UaType.QualifiedName, n.BrowseName); break;
                case 4: v = new Variant(UaType.LocalizedText, n.DisplayName); break;
                case 5: v = new Variant(UaType.LocalizedText, n.Description ?? new LocalizedText(string.Empty)); break;
                case 6:
                case 7: v = new Variant(UaType.UInt32, 0u); break;
                case 8 when n.Class is UaNodeClass.ReferenceType or UaNodeClass.ObjectType or UaNodeClass.VariableType or UaNodeClass.DataType: v = new Variant(UaType.Boolean, n.IsAbstract); break;
                case 9 when n.Class == UaNodeClass.ReferenceType: v = new Variant(UaType.Boolean, n.Symmetric); break;
                case 10 when n.Class == UaNodeClass.ReferenceType: v = new Variant(UaType.LocalizedText, n.InverseName ?? new LocalizedText(string.Empty)); break;
                case 12 when n.Class is UaNodeClass.Object or UaNodeClass.View: v = new Variant(UaType.Byte, (byte)0); break;
                case 13 when n.Class == UaNodeClass.Variable:
                    try
                    {
                        return n.Read?.Invoke() ?? new DataValue { HasValue = false };
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
                    {
                        return new DataValue { HasValue = false, Status = 0x80010000 };
                    }

                case 14 when n.Class is UaNodeClass.Variable or UaNodeClass.VariableType: v = new Variant(UaType.NodeId, n.DataType); break;
                case 15 when n.Class is UaNodeClass.Variable or UaNodeClass.VariableType: v = new Variant(UaType.Int32, n.ValueRank); break;
                case 16 when n.Class is UaNodeClass.Variable or UaNodeClass.VariableType: v = n.ValueRank == 1 ? new Variant(UaType.UInt32, new object?[] { 0u }) : Variant.Null; break;
                case 17 when n.Class == UaNodeClass.Variable:
                case 18 when n.Class == UaNodeClass.Variable: v = new Variant(UaType.Byte, n.AccessLevel); break;
                case 19 when n.Class == UaNodeClass.Variable: v = new Variant(UaType.Double, n.MinimumSamplingInterval); break;
                case 20 when n.Class == UaNodeClass.Variable: v = new Variant(UaType.Boolean, false); break;
                default: return new DataValue { HasValue = false, Status = BadAttributeIdInvalid };
            }

            return new DataValue { Value = v };
        }

        private uint WriteAttribute(NodeId id, uint attribute, DataValue value)
        {
            var n = Space.Find(id);
            if (n == null) return BadNodeIdUnknown;
            if (attribute != 13 || n.Class != UaNodeClass.Variable) return attribute is >= 1 and <= 22 ? BadNotWritable : BadAttributeIdInvalid;
            if (n.Write == null || (n.AccessLevel & 2) == 0) return BadNotWritable;
            return n.Write(value.Value);
        }

        // ================================================================ one client's connection

        private sealed class Connection
        {
            private readonly UaServer _server;
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;
            private readonly object _send = new object();
            private uint _channelId, _tokenId, _sequence = 1;
            private uint _sendBuffer = 65536;
            private readonly MemoryStream _chunks = new MemoryStream();
            private UaChannelSecurity _sec = UaChannelSecurity.Unsecured;

            /// <summary>[opcua-sec] The channel's policy, mode and the client's certificate.</summary>
            public UaChannelSecurity Security => _sec;

            public Connection(UaServer server, TcpClient client)
            {
                _server = server;
                _client = client;
                _client.NoDelay = true;
                _stream = client.GetStream();
            }

            public void Close()
            {
                try
                {
                    _client.Close();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            public void Run()
            {
                try
                {
                    var header = new byte[8];
                    while (_server._running)
                    {
                        if (!ReadExactly(header, 8)) break;
                        string type = System.Text.Encoding.ASCII.GetString(header, 0, 3);
                        char chunk = (char)header[3];
                        uint size = BitConverter.ToUInt32(header, 4);
                        if (size < 8 || size > 16 * 1024 * 1024) break;
                        var body = new byte[size - 8];
                        if (!ReadExactly(body, body.Length)) break;
                        switch (type)
                        {
                            case "HEL":
                                Hello(body);
                                break;
                            case "OPN":
                                if (!Open(UaChannelSecurity.Concat(header, body))) return;
                                break;
                            case "CLO":
                                return;
                            case "MSG":
                                if (!Message(UaChannelSecurity.Concat(header, body), chunk)) return;
                                break;
                            default:
                                Error(BadTcpMessageTypeInvalid, "Bilinmeyen ileti türü " + type);
                                return;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is InvalidDataException)
                {
                }
                finally
                {
                    Close();
                    lock (_server._gate) _server._connections.Remove(this);
                }
            }

            private bool ReadExactly(byte[] buffer, int count)
            {
                int got = 0;
                while (got < count)
                {
                    int n = _stream.Read(buffer, got, count - got);
                    if (n <= 0) return false;
                    got += n;
                }

                return true;
            }

            private void Hello(byte[] body)
            {
                var r = new UaReader(body);
                r.UInt32();   // protocol version
                uint receive = r.UInt32();
                r.UInt32();   // client's send buffer
                r.UInt32();   // max message size
                r.UInt32();   // max chunk count
                _sendBuffer = Math.Max(8192, Math.Min(receive, 1u << 20));
                var w = new UaWriter();
                w.UInt32(0);
                w.UInt32(1u << 20);   // receive buffer
                w.UInt32(_sendBuffer);
                w.UInt32(16u * 1024 * 1024);   // max message size
                w.UInt32(0);   // max chunk count: no limit
                Frame("ACK", 'F', w.ToArray());
            }

            private void Error(uint status, string reason)
            {
                var w = new UaWriter();
                w.UInt32(status);
                w.String(reason);
                Frame("ERR", 'F', w.ToArray());
            }

            private void Frame(string type, char chunk, byte[] body)
            {
                var head = new byte[8];
                System.Text.Encoding.ASCII.GetBytes(type, 0, 3, head, 0);
                head[3] = (byte)chunk;
                BitConverter.GetBytes((uint)(body.Length + 8)).CopyTo(head, 4);
                lock (_send)
                {
                    _stream.Write(head, 0, 8);
                    _stream.Write(body, 0, body.Length);
                }
            }

            /// <summary>
            /// OpenSecureChannel: issue or renew. With Basic256Sha256 the request is decrypted with the server's key and its
            /// signature checked against the client's certificate (which must be trusted); the answer carries the server's
            /// nonce, and both sides derive the channel's keys from the two nonces (Part 6 §6.7.4).
            /// </summary>
            private bool Open(byte[] message)
            {
                var sec = _server.Security;
                var (_, policy, senderDer, thumb, offset) = UaChannelSecurity.ReadAsymmetricHeader(message);
                UaChannelSecurity opening;
                if (policy == SecurityPolicyNone)
                {
                    opening = UaChannelSecurity.Unsecured;
                }
                else if (policy == UaSecurityPolicy.Basic256Sha256 && sec.Secure)
                {
                    X509Certificate2 client;
                    try
                    {
                        client = UaCertificates.Load(senderDer ?? Array.Empty<byte>());
                    }
                    catch (CryptographicException)
                    {
                        return Refuse(BadCertificateInvalid, "İstemci sertifikası okunamadı.");
                    }

                    if (thumb == null || !UaChannelSecurity.FixedTimeEquals(thumb, UaCertificates.Thumbprint(sec.Certificate!)))
                        return Refuse(BadCertificateInvalid, "İleti bu sunucunun sertifikası için şifrelenmemiş.");
                    int bits = UaCertificates.KeyBits(client);
                    if (bits < UaSecurityPolicy.MinKeyBits || bits > UaSecurityPolicy.MaxKeyBits)
                        return Refuse(BadCertificateInvalid, "İstemci anahtarı " + bits + " bit; Basic256Sha256 2048–4096 bit ister.");
                    if (_sec.Secured && !UaChannelSecurity.FixedTimeEquals(_sec.Remote!.RawData, client.RawData))
                        return Refuse(UaSecurityException.BadSecurityChecksFailed, "Kanal başka bir sertifikayla yenilenemez.");
                    if (sec.Pki != null && !sec.Pki.Trusts(client))
                    {
                        string who = client.GetNameInfo(X509NameType.SimpleName, false);
                        _server.Note("untrusted:" + UaCertificates.ThumbprintHex(client),
                            $"OPC UA: '{who}' istemcisinin sertifikası güvenilir değil, bağlantı reddedildi; güvenmek için sertifikayı {sec.Pki.RejectedFolder} klasöründen {sec.Pki.TrustedFolder} klasörüne taşıyın");
                        return Refuse(UaSecurityException.BadCertificateUntrusted, "İstemci sertifikası güvenilir değil.");
                    }

                    opening = new UaChannelSecurity(policy, UaSecurityMode.SignAndEncrypt, sec.Certificate, client);
                }
                else
                {
                    return Refuse(BadSecurityPolicyRejected, "Bu güvenlik ilkesi desteklenmiyor: " + policy);
                }

                byte[] inner;
                try
                {
                    inner = opening.DecodeAsymmetric(message, offset);
                }
                catch (UaSecurityException ex)
                {
                    return Refuse(ex.Status, ex.Message);
                }

                var r = new UaReader(inner);
                r.UInt32();   // sequence number
                uint requestId = r.UInt32();
                r.NodeId();   // type
                var request = RequestHeader(r);
                r.UInt32();   // client protocol version
                int requestType = r.Int32();
                var mode = (UaSecurityMode)r.Int32();
                byte[] clientNonce = r.ByteString() ?? Array.Empty<byte>();
                uint lifetime = r.UInt32();
                if (opening.Secured)
                {
                    bool allowed = mode == UaSecurityMode.Sign ? sec.AllowSign : mode == UaSecurityMode.SignAndEncrypt && sec.AllowSignAndEncrypt;
                    if (!allowed) return Refuse(BadSecurityModeRejected, "Bu güvenlik kipi sunulmuyor.");
                    if (clientNonce.Length != UaSecurityPolicy.NonceLength) return Refuse(BadNonceInvalid, "İstemci rastgele sayısı 32 bayt olmalı.");
                    if (_sec.Secured && _sec.Mode != mode) return Refuse(BadSecurityModeRejected, "Kanal başka bir kiple yenilenemez.");
                }
                else if (mode != UaSecurityMode.None)
                {
                    return Refuse(BadSecurityModeRejected, "Güvenlik ilkesi None iken kip None olmalı.");
                }

                if (requestType == 0) _channelId = (uint)Interlocked.Increment(ref _server._nextChannel);
                _tokenId++;
                // A renewal keeps the channel (and reads the old token until the client moves on).
                var channel = opening.Secured
                    ? _sec.Secured ? _sec : new UaChannelSecurity(policy, mode, sec.Certificate, opening.Remote)
                    : new UaChannelSecurity(SecurityPolicyNone, UaSecurityMode.None, null, null);
                byte[] serverNonce = opening.Secured ? Random(UaSecurityPolicy.NonceLength) : Array.Empty<byte>();
                var w = new UaWriter();
                w.UInt32(_sequence++);
                w.UInt32(requestId);
                w.NodeId(NodeId.Ns0(449));
                ResponseHeader(w, request.Handle, 0);
                w.UInt32(0);
                w.UInt32(_channelId);
                w.UInt32(_tokenId);
                w.DateTime(DateTime.UtcNow);
                w.UInt32(Math.Max(10000u, Math.Min(lifetime, 3_600_000u)));
                w.ByteString(serverNonce);
                byte[] reply = (opening.Secured ? new UaChannelSecurity(policy, mode, sec.Certificate, opening.Remote) : UaChannelSecurity.Unsecured).EncodeAsymmetric(_channelId, w.ToArray());
                if (channel.Secured)
                {
                    // the client's keys from (server nonce, client nonce), the server's from (client nonce, server nonce)
                    channel.SetKeys(_tokenId, UaChannelKeys.Derive(clientNonce, serverNonce), UaChannelKeys.Derive(serverNonce, clientNonce));
                }
                else
                {
                    channel.SetToken(_tokenId);
                }

                _sec = channel;
                lock (_send) _stream.Write(reply, 0, reply.Length);
                return true;
            }

            private bool Refuse(uint status, string reason)
            {
                Error(status, reason);
                return false;
            }

            private static byte[] Random(int n)
            {
                var b = new byte[n];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
                return b;
            }

            public void Send(uint responseType, uint requestId, uint handle, uint result, Action<UaWriter> body)
            {
                var w = new UaWriter();
                w.NodeId(NodeId.Ns0(responseType));
                ResponseHeader(w, handle, result);
                body(w);
                SendMessage(requestId, w.ToArray());
            }

            private void Fault(uint requestId, uint handle, uint status)
            {
                var w = new UaWriter();
                w.NodeId(NodeId.Ns0(397));
                ResponseHeader(w, handle, status);
                SendMessage(requestId, w.ToArray());
            }

            private void SendMessage(uint requestId, byte[] payload)
            {
                // Chunks within the client's receive buffer: the sequence header and the payload's part, [opcua-sec] signed and
                // encrypted as the channel is.
                int room = _sec.MaxBodyPerChunk((int)_sendBuffer);
                lock (_send)
                {
                    for (int at = 0; at < payload.Length || at == 0; at += room)
                    {
                        int n = Math.Min(room, payload.Length - at);
                        bool last = at + n >= payload.Length;
                        var w = new UaWriter();
                        w.UInt32(_sequence++);
                        w.UInt32(requestId);
                        var part = new byte[n];
                        System.Array.Copy(payload, at, part, 0, n);
                        w.Bytes(part);
                        byte[] chunk = _sec.EncodeSymmetric("MSG", last ? 'F' : 'C', _channelId, w.ToArray());
                        _stream.Write(chunk, 0, chunk.Length);
                        if (last) break;
                    }
                }
            }

            private bool Message(byte[] message, char chunk)
            {
                byte[] inner;
                try
                {
                    inner = _sec.DecodeSymmetric(message);   // [opcua-sec] checked and decrypted with the token's keys
                }
                catch (UaSecurityException ex)
                {
                    return Refuse(ex.Status, ex.Message);
                }

                var r = new UaReader(inner);
                r.UInt32();   // sequence number
                uint requestId = r.UInt32();
                _chunks.Write(inner, r.Position, r.Remaining);
                if (chunk == 'C') return true;
                if (chunk == 'A')
                {
                    _chunks.SetLength(0);
                    return true;
                }

                var payload = _chunks.ToArray();
                _chunks.SetLength(0);
                var p = new UaReader(payload);
                var type = p.NodeId();
                RequestInfo request;
                try
                {
                    request = RequestHeader(p);
                }
                catch (InvalidDataException)
                {
                    Fault(requestId, 0, BadDecodingError);
                    return true;
                }

                try
                {
                    _server.Dispatch(this, type.Numeric, requestId, request, p);
                }
                catch (InvalidDataException)
                {
                    Fault(requestId, request.Handle, BadDecodingError);
                }
                catch (UaSecurityException ex)
                {
                    Fault(requestId, request.Handle, ex.Status);
                }

                return true;
            }

            public void Reply(uint requestId, uint handle, uint status) => Fault(requestId, handle, status);
        }

        private struct RequestInfo
        {
            public NodeId Token;
            public uint Handle;
        }

        private static RequestInfo RequestHeader(UaReader r)
        {
            var info = new RequestInfo { Token = r.NodeId() };
            r.DateTime();
            info.Handle = r.UInt32();
            r.UInt32();   // return diagnostics
            r.String();   // audit entry
            r.UInt32();   // timeout hint
            r.ExtensionObject();
            return info;
        }

        private static void ResponseHeader(UaWriter w, uint handle, uint result)
        {
            w.DateTime(DateTime.UtcNow);
            w.UInt32(handle);
            w.StatusCode(result);
            w.EmptyDiagnostics();
            w.Int32(0);   // string table
            w.ExtensionObject(null);
        }

        // ================================================================ services

        private void Dispatch(Connection c, uint type, uint requestId, RequestInfo h, UaReader r)
        {
            switch (type)
            {
                case 428: GetEndpoints(c, requestId, h, r); return;
                case 422: FindServers(c, requestId, h); return;
            }

            // [opcua-sec] Without a None endpoint an unsecured channel is for discovery only (Part 4 §5.4).
            if (!c.Security.Secured && !Security.AllowNone)
            {
                c.Reply(requestId, h.Handle, BadSecurityPolicyRejected);
                return;
            }

            if (type == 461)
            {
                CreateSession(c, requestId, h, r);
                return;
            }

            Session? s;
            lock (_gate) s = _sessions.Values.FirstOrDefault(x => x.Token == h.Token);
            if (s == null)
            {
                c.Reply(requestId, h.Handle, BadSessionIdInvalid);
                return;
            }

            if (type == 467)
            {
                ActivateSession(c, requestId, h, r, s);
                return;
            }

            if (!s.Activated)
            {
                c.Reply(requestId, h.Handle, BadSessionNotActivated);
                return;
            }

            lock (s)
            {
                switch (type)
                {
                    case 473: CloseSession(c, requestId, h, r, s); return;
                    case 527: Browse(c, requestId, h, r); return;
                    case 533: c.Send(536, requestId, h.Handle, 0, w => { w.Int32(0); w.Int32(0); }); return;   // no continuation points are handed out
                    case 554: Translate(c, requestId, h, r); return;
                    case 631: Read(c, requestId, h, r); return;
                    case 673: Write(c, requestId, h, r, s); return;
                    case 787: CreateSubscription(c, requestId, h, r, s); return;
                    case 793: ModifySubscription(c, requestId, h, r, s); return;
                    case 799: SetPublishingMode(c, requestId, h, r, s); return;
                    case 847: DeleteSubscriptions(c, requestId, h, r, s); return;
                    case 751: CreateMonitoredItems(c, requestId, h, r, s); return;
                    case 763: ModifyMonitoredItems(c, requestId, h, r, s); return;
                    case 769: SetMonitoringMode(c, requestId, h, r, s); return;
                    case 781: DeleteMonitoredItems(c, requestId, h, r, s); return;
                    case 826: Publish(c, requestId, h, r, s); return;
                    case 832: c.Reply(requestId, h.Handle, BadMessageNotAvailable); return;
                    case 560: RegisterNodes(c, requestId, h, r); return;
                    case 566: c.Send(569, requestId, h.Handle, 0, _ => { }); return;
                    default: c.Reply(requestId, h.Handle, BadServiceUnsupported); return;
                }
            }
        }

        private void ApplicationDescription(UaWriter w)
        {
            w.String(Space.ApplicationUri);
            w.String("urn:robotwin:studio");
            w.LocalizedText(new LocalizedText(Name, "en"));
            w.Int32(0);   // server
            w.String(null);
            w.String(null);
            w.Array(new[] { EndpointUrl }, w.String);
        }

        /// <summary>[opcua-sec] The endpoints offered: (policy, mode, security level).</summary>
        private List<(string Policy, UaSecurityMode Mode, byte Level)> Endpoints()
        {
            var list = new List<(string, UaSecurityMode, byte)>();
            if (Security.AllowNone) list.Add((SecurityPolicyNone, UaSecurityMode.None, 0));
            if (Security.Certificate != null && Security.AllowSign) list.Add((UaSecurityPolicy.Basic256Sha256, UaSecurityMode.Sign, 1));
            if (Security.Certificate != null && Security.AllowSignAndEncrypt) list.Add((UaSecurityPolicy.Basic256Sha256, UaSecurityMode.SignAndEncrypt, 2));
            return list;
        }

        private void Endpoint(UaWriter w, (string Policy, UaSecurityMode Mode, byte Level) e)
        {
            w.String(EndpointUrl);
            ApplicationDescription(w);
            w.ByteString(Security.Certificate?.RawData);
            w.Int32((int)e.Mode);
            w.String(e.Policy);
            // Anonymous, and named users whose password goes encrypted with the server's certificate (also on a None endpoint).
            var tokens = new List<(string Id, int Type, string? Policy)>();
            if (Security.AllowAnonymous) tokens.Add(("anonymous", 0, null));
            if (Security.Users.Count > 0 && Security.Certificate != null) tokens.Add(("username", 1, UaSecurityPolicy.Basic256Sha256));
            w.Array(tokens, t =>
            {
                w.String(t.Id);
                w.Int32(t.Type);
                w.String(null);
                w.String(null);
                w.String(t.Policy);
            });
            w.String(TransportProfile);
            w.Byte(e.Level);
        }

        private void GetEndpoints(Connection c, uint requestId, RequestInfo h, UaReader r) =>
            c.Send(431, requestId, h.Handle, 0, w => w.Array(Endpoints(), e => Endpoint(w, e)));

        private void FindServers(Connection c, uint requestId, RequestInfo h) =>
            c.Send(425, requestId, h.Handle, 0, w =>
            {
                w.Int32(1);
                ApplicationDescription(w);
            });

        private void CreateSession(Connection c, uint requestId, RequestInfo h, UaReader r)
        {
            string? clientUri = r.String();
            r.String();
            r.LocalizedText();
            r.Int32();
            r.String();
            r.String();
            r.Array(r.String);
            r.String();   // server uri
            r.String();   // endpoint url
            r.String();   // session name
            byte[] clientNonce = r.ByteString() ?? Array.Empty<byte>();
            byte[]? clientCertificate = r.ByteString();
            double timeout = r.Double();
            var channel = c.Security;
            string? algorithm = null;
            byte[]? signature = null;
            if (channel.Secured)
            {
                // [opcua-sec] The session belongs to the channel's application: the same certificate, its URI, a real nonce.
                if (clientCertificate == null || !UaChannelSecurity.FixedTimeEquals(clientCertificate, channel.Remote!.RawData))
                {
                    c.Reply(requestId, h.Handle, BadCertificateInvalid);
                    return;
                }

                string? certUri = UaCertificates.ApplicationUri(channel.Remote);
                if (certUri != null && clientUri != null && certUri != clientUri)
                {
                    c.Reply(requestId, h.Handle, BadCertificateUriInvalid);
                    return;
                }

                if (clientNonce.Length < UaSecurityPolicy.NonceLength)
                {
                    c.Reply(requestId, h.Handle, BadNonceInvalid);
                    return;
                }

                algorithm = UaSecurityPolicy.RsaSha256;
                signature = UaCertificates.Sign(Security.Certificate!, UaChannelSecurity.Concat(clientCertificate, clientNonce));
            }

            Session s;
            lock (_gate)
            {
                uint id = (uint)_nextSession++;
                s = new Session { Id = id, Token = new NodeId(1, 0x10000u + id * 7919u), Channel = c, ServerNonce = NewNonce(), ClientCertificate = clientCertificate };
                _sessions[id] = s;
            }

            c.Send(464, requestId, h.Handle, 0, w =>
            {
                w.NodeId(new NodeId(1, s.Id));
                w.NodeId(s.Token);
                w.Double(Math.Max(10000, Math.Min(timeout, 3_600_000)));
                w.ByteString(s.ServerNonce);
                w.ByteString(Security.Certificate?.RawData);
                w.Array(Endpoints(), e => Endpoint(w, e));
                w.Int32(0);   // server software certificates
                w.String(algorithm);   // server signature: over the client's certificate and nonce
                w.ByteString(signature);
                w.UInt32(16u * 1024 * 1024);
            });
        }

        private static byte[] NewNonce()
        {
            var b = new byte[UaSecurityPolicy.NonceLength];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        private static void SkipApplicationDescription(UaReader r)
        {
            r.String();
            r.String();
            r.LocalizedText();
            r.Int32();
            r.String();
            r.String();
            r.Array(r.String);
        }

        private void ActivateSession(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            r.String();   // client signature: algorithm
            byte[]? clientSignature = r.ByteString();
            r.Array(() =>
            {
                r.ByteString();
                r.ByteString();
                return 0;
            });
            r.Array(r.String);   // locale ids
            var token = r.ExtensionObject();
            var channel = c.Security;
            if (channel.Secured)
            {
                // [opcua-sec] The client proves it holds its key: its signature over the server's certificate and last nonce.
                if (s.ClientCertificate == null || !UaChannelSecurity.FixedTimeEquals(s.ClientCertificate, channel.Remote!.RawData) ||
                    !UaCertificates.Verify(channel.Remote, UaChannelSecurity.Concat(Security.Certificate!.RawData, s.ServerNonce), clientSignature))
                {
                    c.Reply(requestId, h.Handle, BadApplicationSignatureInvalid);
                    return;
                }
            }

            string? user = null;
            bool canWrite = true;
            if (token.TypeId.IsNull || token.TypeId == NodeId.Ns0(321))
            {
                if (!Security.AllowAnonymous)
                {
                    c.Reply(requestId, h.Handle, BadIdentityTokenRejected);
                    return;
                }
            }
            else if (token.TypeId == NodeId.Ns0(324))
            {
                uint status = CheckUser(token.Body ?? Array.Empty<byte>(), s, channel, out user, out canWrite);
                if (status != 0)
                {
                    Note("user:" + (user ?? string.Empty), $"OPC UA: '{user}' kullanıcısı reddedildi (adı ya da parolası yanlış)");
                    c.Reply(requestId, h.Handle, status);
                    return;
                }
            }
            else
            {
                c.Reply(requestId, h.Handle, BadIdentityTokenInvalid);   // anonymous and user name only
                return;
            }

            byte[] nonce = NewNonce();
            lock (s)
            {
                s.Activated = true;
                s.Channel = c;
                s.User = user;
                s.CanWrite = canWrite;
                s.ServerNonce = nonce;
            }

            c.Send(470, requestId, h.Handle, 0, w =>
            {
                w.ByteString(nonce);
                w.Int32(0);
                w.Int32(0);
            });
        }

        /// <summary>
        /// [opcua-sec] A UserNameIdentityToken: the password encrypted with the server's certificate (RSA-OAEP over its length,
        /// the password and the server's last nonce), or in clear only on a signed-and-encrypted channel.
        /// </summary>
        private uint CheckUser(byte[] body, Session s, UaChannelSecurity channel, out string? user, out bool canWrite)
        {
            canWrite = false;
            var t = new UaReader(body);
            t.String();   // policy id
            user = t.String();
            byte[] secret = t.ByteString() ?? Array.Empty<byte>();
            string? algorithm = t.String();
            string password;
            if (algorithm == UaSecurityPolicy.RsaOaep && Security.Certificate != null)
            {
                byte[] plain;
                try
                {
                    plain = UaCertificates.Decrypt(Security.Certificate, secret);
                }
                catch (Exception ex) when (ex is CryptographicException || ex is UaSecurityException)
                {
                    return BadIdentityTokenInvalid;
                }

                if (plain.Length < 4) return BadIdentityTokenInvalid;
                int length = BitConverter.ToInt32(plain, 0);
                int n = s.ServerNonce.Length;
                if (length < n || length > plain.Length - 4) return BadIdentityTokenInvalid;
                if (!UaChannelSecurity.FixedTimeEquals(UaChannelSecurity.Slice(plain, 4 + length - n, n), s.ServerNonce)) return BadIdentityTokenInvalid;
                password = System.Text.Encoding.UTF8.GetString(plain, 4, length - n);
            }
            else if (string.IsNullOrEmpty(algorithm) && channel.Mode == UaSecurityMode.SignAndEncrypt)
            {
                password = System.Text.Encoding.UTF8.GetString(secret);
            }
            else
            {
                return BadIdentityTokenRejected;   // a password must not cross the network readable
            }

            if (user == null || !Security.Users.TryGetValue(user, out var known)) return BadUserAccessDenied;
            var a = System.Text.Encoding.UTF8.GetBytes(password);
            var b = System.Text.Encoding.UTF8.GetBytes(known.Password);
            if (!UaChannelSecurity.FixedTimeEquals(a, b)) return BadUserAccessDenied;
            canWrite = known.CanWrite;
            return 0;
        }

        private void CloseSession(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            lock (_gate) _sessions.Remove(s.Id);
            c.Send(476, requestId, h.Handle, 0, _ => { });
        }

        private void Browse(Connection c, uint requestId, RequestInfo h, UaReader r)
        {
            r.NodeId();   // view
            r.DateTime();
            r.UInt32();
            uint max = r.UInt32();
            var nodes = r.Array(() => (Node: r.NodeId(), Direction: r.Int32(), Type: r.NodeId(), Subtypes: r.Boolean(), ClassMask: r.UInt32(), ResultMask: r.UInt32()));
            if (nodes.Count == 0)
            {
                c.Reply(requestId, h.Handle, BadNothingToDo);
                return;
            }

            c.Send(530, requestId, h.Handle, 0, w =>
            {
                w.Array(nodes, b =>
                {
                    var n = Space.Find(b.Node);
                    if (n == null)
                    {
                        w.StatusCode(BadNodeIdUnknown);
                        w.ByteString(null);
                        w.Int32(0);
                        return;
                    }

                    var refs = n.References.Where(x => (b.Direction == 2 || x.IsForward == (b.Direction == 0)) && UaAddressSpace.Matches(x.Type, b.Type, b.Subtypes))
                        .Select(x => (Ref: x, Target: Space.Find(x.Target)))
                        .Where(x => x.Target == null || b.ClassMask == 0 || (b.ClassMask & (uint)x.Target.Class) != 0)
                        .ToList();
                    if (max > 0 && refs.Count > max) refs = refs.Take((int)max).ToList();
                    w.StatusCode(0);
                    w.ByteString(null);
                    w.Array(refs, x =>
                    {
                        w.NodeId(x.Ref.Type);
                        w.Boolean(x.Ref.IsForward);
                        w.ExpandedNodeId(x.Ref.Target);
                        w.QualifiedName(x.Target?.BrowseName ?? new QualifiedName(0, string.Empty));
                        w.LocalizedText(x.Target?.DisplayName ?? new LocalizedText(string.Empty));
                        w.Int32((int)(x.Target?.Class ?? 0));
                        w.ExpandedNodeId(x.Target?.TypeDefinition ?? NodeId.Null);
                    });
                });
                w.Int32(0);
            });
        }

        private void Translate(Connection c, uint requestId, RequestInfo h, UaReader r)
        {
            var paths = r.Array(() => (Start: r.NodeId(), Elements: r.Array(() => (Type: r.NodeId(), Inverse: r.Boolean(), Subtypes: r.Boolean(), Name: r.QualifiedName()))));
            c.Send(557, requestId, h.Handle, 0, w =>
            {
                w.Array(paths, p =>
                {
                    var at = Space.Find(p.Start);
                    foreach (var e in p.Elements)
                    {
                        if (at == null) break;
                        var hop = at.References.Where(x => x.IsForward != e.Inverse && UaAddressSpace.Matches(x.Type, e.Type, e.Subtypes))
                            .Select(x => Space.Find(x.Target)).FirstOrDefault(n => n != null && n.BrowseName.Name == e.Name.Name && n.BrowseName.Namespace == e.Name.Namespace);
                        at = hop;
                    }

                    w.StatusCode(at == null ? 0x806F0000u : 0u);   // BadNoMatch
                    if (at == null) w.Int32(0);
                    else
                    {
                        w.Int32(1);
                        w.ExpandedNodeId(at.Id);
                        w.UInt32(uint.MaxValue);
                    }
                });
                w.Int32(0);
            });
        }

        private void Read(Connection c, uint requestId, RequestInfo h, UaReader r)
        {
            r.Double();   // max age
            int timestamps = r.Int32();
            var nodes = r.Array(() => (Node: r.NodeId(), Attribute: r.UInt32(), Range: r.String(), Encoding: r.QualifiedName()));
            if (nodes.Count == 0)
            {
                c.Reply(requestId, h.Handle, BadNothingToDo);
                return;
            }

            c.Send(634, requestId, h.Handle, 0, w =>
            {
                w.Array(nodes, x => w.DataValue(Stamp(ReadAttribute(x.Node, x.Attribute), timestamps)));
                w.Int32(0);
            });
        }

        /// <summary>The timestamps a client asked for: 0 source, 1 server, 2 both, 3 neither.</summary>
        private static DataValue Stamp(DataValue v, int which)
        {
            var now = DateTime.UtcNow;
            return new DataValue
            {
                Value = v.Value,
                HasValue = v.HasValue,
                Status = v.Status,
                SourceTimestamp = which is 0 or 2 && v.HasValue ? v.SourceTimestamp ?? now : null,
                ServerTimestamp = which is 1 or 2 ? v.ServerTimestamp ?? now : null
            };
        }

        private void Write(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            var nodes = r.Array(() => (Node: r.NodeId(), Attribute: r.UInt32(), Range: r.String(), Value: r.DataValue()));
            if (nodes.Count == 0)
            {
                c.Reply(requestId, h.Handle, BadNothingToDo);
                return;
            }

            // [opcua-sec] a read-only user sees everything and changes nothing
            var results = nodes.Select(x => s.CanWrite ? WriteAttribute(x.Node, x.Attribute, x.Value) : BadUserAccessDenied).ToList();
            c.Send(676, requestId, h.Handle, 0, w =>
            {
                w.Array(results, x => w.StatusCode(x));
                w.Int32(0);
            });
        }

        private void RegisterNodes(Connection c, uint requestId, RequestInfo h, UaReader r)
        {
            var nodes = r.Array(r.NodeId);
            c.Send(563, requestId, h.Handle, 0, w => w.Array(nodes, w.NodeId));
        }

        private void CreateSubscription(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            double interval = Math.Max(10, Math.Min(r.Double(), 3_600_000));
            uint lifetime = r.UInt32();
            uint keepAlive = Math.Max(1, r.UInt32());
            r.UInt32();   // max notifications per publish
            bool enabled = r.Boolean();
            r.Byte();   // priority
            var sub = new Subscription { Interval = interval, KeepAlive = keepAlive, Lifetime = Math.Max(lifetime, keepAlive * 3), Enabled = enabled, Due = DateTime.UtcNow };
            lock (_gate) sub.Id = (uint)_nextSubscription++;
            s.Subscriptions[sub.Id] = sub;
            c.Send(790, requestId, h.Handle, 0, w =>
            {
                w.UInt32(sub.Id);
                w.Double(sub.Interval);
                w.UInt32(sub.Lifetime);
                w.UInt32(sub.KeepAlive);
            });
        }

        private void ModifySubscription(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            uint id = r.UInt32();
            double interval = Math.Max(10, Math.Min(r.Double(), 3_600_000));
            uint lifetime = r.UInt32();
            uint keepAlive = Math.Max(1, r.UInt32());
            if (!s.Subscriptions.TryGetValue(id, out var sub))
            {
                c.Reply(requestId, h.Handle, BadSubscriptionIdInvalid);
                return;
            }

            sub.Interval = interval;
            sub.KeepAlive = keepAlive;
            sub.Lifetime = Math.Max(lifetime, keepAlive * 3);
            c.Send(796, requestId, h.Handle, 0, w =>
            {
                w.Double(sub.Interval);
                w.UInt32(sub.Lifetime);
                w.UInt32(sub.KeepAlive);
            });
        }

        private void SetPublishingMode(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            bool enabled = r.Boolean();
            var ids = r.Array(r.UInt32);
            var results = ids.Select(id =>
            {
                if (!s.Subscriptions.TryGetValue(id, out var sub)) return BadSubscriptionIdInvalid;
                sub.Enabled = enabled;
                return 0u;
            }).ToList();
            c.Send(802, requestId, h.Handle, 0, w =>
            {
                w.Array(results, x => w.StatusCode(x));
                w.Int32(0);
            });
        }

        private void DeleteSubscriptions(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            var ids = r.Array(r.UInt32);
            var results = ids.Select(id => s.Subscriptions.Remove(id) ? 0u : BadSubscriptionIdInvalid).ToList();
            c.Send(850, requestId, h.Handle, 0, w =>
            {
                w.Array(results, x => w.StatusCode(x));
                w.Int32(0);
            });
        }

        private void CreateMonitoredItems(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            uint id = r.UInt32();
            r.Int32();   // timestamps to return
            var items = r.Array(() =>
            {
                var node = r.NodeId();
                uint attribute = r.UInt32();
                r.String();
                r.QualifiedName();
                int mode = r.Int32();
                uint handle = r.UInt32();
                double sampling = r.Double();
                r.ExtensionObject();   // filter: data changes on value and status
                uint queue = r.UInt32();
                r.Boolean();
                return (node, attribute, mode, handle, sampling, queue);
            });
            if (!s.Subscriptions.TryGetValue(id, out var sub))
            {
                c.Reply(requestId, h.Handle, BadSubscriptionIdInvalid);
                return;
            }

            var results = new List<(uint Status, uint Id, double Sampling, uint Queue)>();
            foreach (var (node, attribute, mode, handle, sampling, queue) in items)
            {
                if (Space.Find(node) == null)
                {
                    results.Add((BadNodeIdUnknown, 0, 0, 0));
                    continue;
                }

                var m = new Monitored
                {
                    Id = sub.NextItem++,
                    ClientHandle = handle,
                    Node = node,
                    Attribute = attribute,
                    Mode = mode,
                    Sampling = sampling < 0 ? sub.Interval : Math.Max(10, sampling),
                    QueueSize = Math.Max(1, Math.Min(queue, 100)),
                    NextSample = DateTime.UtcNow
                };
                sub.Items[m.Id] = m;
                results.Add((0, m.Id, m.Sampling, m.QueueSize));
            }

            c.Send(754, requestId, h.Handle, 0, w =>
            {
                w.Array(results, x =>
                {
                    w.StatusCode(x.Status);
                    w.UInt32(x.Id);
                    w.Double(x.Sampling);
                    w.UInt32(x.Queue);
                    w.ExtensionObject(null);
                });
                w.Int32(0);
            });
        }

        private void ModifyMonitoredItems(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            uint id = r.UInt32();
            r.Int32();
            var items = r.Array(() =>
            {
                uint item = r.UInt32();
                r.UInt32();   // client handle
                double sampling = r.Double();
                r.ExtensionObject();
                uint queue = r.UInt32();
                r.Boolean();
                return (item, sampling, queue);
            });
            if (!s.Subscriptions.TryGetValue(id, out var sub))
            {
                c.Reply(requestId, h.Handle, BadSubscriptionIdInvalid);
                return;
            }

            c.Send(766, requestId, h.Handle, 0, w =>
            {
                w.Array(items, x =>
                {
                    if (!sub.Items.TryGetValue(x.item, out var m))
                    {
                        w.StatusCode(BadMonitoredItemIdInvalid);
                        w.Double(0);
                        w.UInt32(0);
                        w.ExtensionObject(null);
                        return;
                    }

                    m.Sampling = x.sampling < 0 ? sub.Interval : Math.Max(10, x.sampling);
                    m.QueueSize = Math.Max(1, Math.Min(x.queue, 100));
                    w.StatusCode(0);
                    w.Double(m.Sampling);
                    w.UInt32(m.QueueSize);
                    w.ExtensionObject(null);
                });
                w.Int32(0);
            });
        }

        private void SetMonitoringMode(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            uint id = r.UInt32();
            int mode = r.Int32();
            var items = r.Array(r.UInt32);
            if (!s.Subscriptions.TryGetValue(id, out var sub))
            {
                c.Reply(requestId, h.Handle, BadSubscriptionIdInvalid);
                return;
            }

            var results = items.Select(i =>
            {
                if (!sub.Items.TryGetValue(i, out var m)) return BadMonitoredItemIdInvalid;
                m.Mode = mode;
                if (mode == 2) m.Last = null;   // reporting again: the current value is reported
                return 0u;
            }).ToList();
            c.Send(772, requestId, h.Handle, 0, w =>
            {
                w.Array(results, x => w.StatusCode(x));
                w.Int32(0);
            });
        }

        private void DeleteMonitoredItems(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            uint id = r.UInt32();
            var items = r.Array(r.UInt32);
            if (!s.Subscriptions.TryGetValue(id, out var sub))
            {
                c.Reply(requestId, h.Handle, BadSubscriptionIdInvalid);
                return;
            }

            var results = items.Select(i => sub.Items.Remove(i) ? 0u : BadMonitoredItemIdInvalid).ToList();
            c.Send(784, requestId, h.Handle, 0, w =>
            {
                w.Array(results, x => w.StatusCode(x));
                w.Int32(0);
            });
        }

        private void Publish(Connection c, uint requestId, RequestInfo h, UaReader r, Session s)
        {
            var acks = r.Array(() => (Subscription: r.UInt32(), Sequence: r.UInt32()));
            if (s.Subscriptions.Count == 0)
            {
                c.Reply(requestId, h.Handle, BadNoSubscription);
                return;
            }

            if (s.Publishes.Count >= 32)
            {
                c.Reply(requestId, h.Handle, BadTooManyPublishRequests);
                return;
            }

            // Acknowledgements are good for any subscription this session has (nothing is kept for republishing).
            s.Acks[h.Handle] = acks.Select(a => s.Subscriptions.ContainsKey(a.Subscription) ? 0u : BadSubscriptionIdInvalid).ToList();
            s.Publishes.Enqueue((c, requestId, h.Handle));
        }
    }
}
