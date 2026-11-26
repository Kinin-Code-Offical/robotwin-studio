using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Industrial.OpcUa
{
    /// <summary>
    /// [opcua] Puts a running world's PLCs on the network as OPC UA servers: one server per listening address and port,
    /// each PLC an object under Objects (ns=1;s=&lt;name&gt;) with a variable per program variable (ns=1;s=&lt;name&gt;.&lt;VAR&gt;)
    /// in the OPC UA type of its IEC type, and RUN. Opened when the world starts and closed when it stops; when another
    /// program is loaded the PLC's variables follow.
    /// </summary>
    public sealed class OpcUaHost : IDisposable
    {
        private readonly List<UaServer> _servers = new List<UaServer>();
        private readonly List<(UaAddressSpace Space, UaTagImage Image)> _plcs = new List<(UaAddressSpace, UaTagImage)>();
        private readonly Dictionary<UaTagImage, int> _generation = new Dictionary<UaTagImage, int>();
        private readonly Timer _refresh;

        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _notices = new System.Collections.Concurrent.ConcurrentQueue<string>();

        /// <param name="pkiFolder">[opcua-sec] Where certificates are kept (null: %LOCALAPPDATA%\RobotWinStudio\pki).</param>
        public OpcUaHost(IEnumerable<UaTagImage> images, string? pkiFolder = null)
        {
            UaPki? pki = null;
            foreach (var group in images.GroupBy(i => (i.Bind, i.Port)))
            {
                if (!IPAddress.TryParse(group.Key.Bind, out var address))
                {
                    Problems.Add($"{group.Key.Bind}: geçerli bir IP adresi değil.");
                    continue;
                }

                var space = new UaAddressSpace("urn:robotwin:studio:plc:" + group.Key.Port);
                var first = group.First();
                if (group.Any(i => i.Security != first.Security || i.Users != first.Users || i.TrustAllClients != first.TrustAllClients))
                    Problems.Add($"{group.Key.Port} portundaki PLC'lerin OPC UA güvenlik ayarları farklı; '{first.Name}' PLC'sininki geçerli.");
                UaServerSecurity security;
                try
                {
                    security = Secure(first, address, group.Key.Port, ref pki, pkiFolder);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.Cryptography.CryptographicException)
                {
                    Problems.Add($"{group.Key.Port}: OPC UA sertifikası hazırlanamadı ({ex.Message}); sunucu güvenliksiz açılmadı.");
                    continue;
                }

                var server = new UaServer(space, address, group.Key.Port, "RobotWin Studio", security);
                server.Notice += _notices.Enqueue;
                foreach (var image in group)
                {
                    if (group.Count(i => i.Name == image.Name) > 1) Problems.Add($"{image.Name}: {group.Key.Port} portunda bu adla iki PLC var; biri görünmez.");
                    _plcs.Add((space, image));
                    Build(space, image);
                }

                try
                {
                    server.Start();
                    _servers.Add(server);
                    Listening.Add($"{server.EndpointUrl} ({string.Join(", ", group.Select(i => i.Name))})" +
                                  (security.Secure ? (security.AllowNone ? "" : security.AllowSign ? ", Basic256Sha256 imza / imza ve şifre" : ", Basic256Sha256 imza ve şifre") : ""));
                }
                catch (SocketException ex)
                {
                    server.Dispose();
                    Problems.Add($"{group.Key.Bind}:{group.Key.Port} açılamadı: {ex.Message}");
                }
            }

            _refresh = new Timer(_ => Refresh(), null, 250, 250);
        }

        /// <summary>
        /// [opcua-sec] A PLC's settings as the server's security: Basic256Sha256 endpoints with the certificate kept for this
        /// port, the users (with ":okur" read-only), anonymous only when no users are named, and the clients it trusts. A
        /// user's password goes encrypted with the certificate even when the PLC offers no secure endpoint.
        /// </summary>
        private UaServerSecurity Secure(UaTagImage image, IPAddress address, int port, ref UaPki? pki, string? pkiFolder)
        {
            var users = ParseUsers(image.Users);
            bool sign = image.Security == CellParts.OpcUaSecuritySign, encrypt = image.Security == CellParts.OpcUaSecurityEncrypt;
            var security = new UaServerSecurity
            {
                AllowNone = !sign && !encrypt,
                AllowSign = sign,
                AllowSignAndEncrypt = sign || encrypt,
                AllowAnonymous = users.Count == 0
            };
            foreach (var u in users) security.Users[u.Name] = (u.Password, u.CanWrite);
            if (!sign && !encrypt && users.Count == 0)
            {
                if (!IPAddress.IsLoopback(address))
                    Problems.Add($"{port}: OPC UA ağa güvenliksiz ve kullanıcısız açık; ağdaki herkes PLC'yi okuyup yazabilir. PLC'nin 'OPC UA güvenliği' ayarını '{CellParts.OpcUaSecurityEncrypt}' yapın ve kullanıcı tanımlayın.");
                return security;
            }

            pki ??= new UaPki(pkiFolder ?? UaPki.DefaultFolder);
            var hosts = new List<string> { "localhost", Environment.MachineName };
            if (!address.Equals(IPAddress.Any)) hosts.Add(address.ToString());
            security.Certificate = pki.Own("urn:robotwin:studio:plc:" + port, "RobotWin PLC " + port, hosts);
            security.Pki = image.TrustAllClients ? null : pki;
            return security;
        }

        /// <summary>"ad:parola, ad2:parola2:okur" (commas, semicolons or lines between them).</summary>
        public static List<(string Name, string Password, bool CanWrite)> ParseUsers(string text)
        {
            var list = new List<(string, string, bool)>();
            foreach (string item in (text ?? string.Empty).Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = item.Trim();
                int colon = t.IndexOf(':');
                if (colon <= 0) continue;
                string name = t.Substring(0, colon).Trim();
                string rest = t.Substring(colon + 1);
                bool readOnly = rest.EndsWith(":okur", StringComparison.OrdinalIgnoreCase) || rest.EndsWith(":ro", StringComparison.OrdinalIgnoreCase);
                if (readOnly) rest = rest.Substring(0, rest.LastIndexOf(':'));
                if (name.Length > 0) list.Add((name, rest, !readOnly));
            }

            return list;
        }

        /// <summary>[opcua-sec] What the servers said since the last call (a refused certificate, a refused user), for the run's log.</summary>
        public List<string> TakeNotices()
        {
            var list = new List<string>();
            while (_notices.TryDequeue(out string? n)) list.Add(n);
            return list;
        }

        /// <summary>Where it listens ("opc.tcp://127.0.0.1:4840 (PLC1)") and what could not be opened.</summary>
        public List<string> Listening { get; } = new List<string>();

        public List<string> Problems { get; } = new List<string>();

        public IReadOnlyList<UaServer> Servers => _servers;

        public int Clients => _servers.Sum(s => s.Clients);

        /// <summary>A PLC loaded another program: its variable nodes are made again.</summary>
        private void Refresh()
        {
            foreach (var (space, image) in _plcs)
            {
                if (_generation.TryGetValue(image, out int g) && g == image.Generation) continue;
                Build(space, image);
            }
        }

        private void Build(UaAddressSpace space, UaTagImage image)
        {
            lock (_generation)
            {
                _generation[image] = image.Generation;
                var plcId = new NodeId(1, image.Name);
                var old = space.Find(plcId);
                if (old != null)
                    foreach (var r in old.References.Where(r => r.IsForward && r.Target.Namespace == 1).ToList()) space.Remove(r.Target);
                else space.AddObject(plcId, image.Name, NodeId.Ns0(UaAddressSpace.ObjectsFolder));

                space.AddVariable(new NodeId(1, image.Name + ".RUN"), "RUN", plcId, 1, () => new DataValue { Value = new Variant(UaType.Boolean, image.Running), SourceTimestamp = DateTime.UtcNow });
                foreach (var tag in image.Tags)
                {
                    string name = tag.Name;
                    var (type, dataType) = Map(tag.TypeName, tag.Type);
                    space.AddVariable(new NodeId(1, image.Name + "." + name), name, plcId, dataType,
                        () => image.TryGet(name, out var t, out var stamp)
                            ? new DataValue { Value = type == UaType.String ? new Variant(UaType.String, t.Text ?? string.Empty) : Value(type, t.Value), SourceTimestamp = stamp, Status = image.Running ? 0u : 0x408F0000u }   // UncertainNoCommunication… in STOP: last value
                            : new DataValue { HasValue = false, Status = 0x80340000 },
                        tag.Writable ? v => Write(image, name, type, v) : (Func<Variant, uint>?)null);
                }
            }
        }

        /// <summary>The OPC UA type of an IEC type, and its data type node.</summary>
        public static (UaType Type, uint DataType) Map(string iec, StType type) => iec switch
        {
            "BOOL" => (UaType.Boolean, 1),
            "SINT" => (UaType.SByte, 2),
            "USINT" or "BYTE" => (UaType.Byte, 3),
            "INT" => (UaType.Int16, 4),
            "UINT" or "WORD" => (UaType.UInt16, 5),
            "DINT" => (UaType.Int32, 6),
            "UDINT" or "DWORD" => (UaType.UInt32, 7),
            "LINT" => (UaType.Int64, 8),
            "ULINT" or "LWORD" => (UaType.UInt64, 9),
            "REAL" => (UaType.Float, 10),
            "LREAL" or "TIME" => (UaType.Double, 11),   // TIME as milliseconds
            "STRING" or "WSTRING" => (UaType.String, 12),   // [plc-string]
            _ => type switch
            {
                StType.Bool => (UaType.Boolean, 1),
                StType.Int => (UaType.Int16, 4),
                StType.String => (UaType.String, 12),
                _ => (UaType.Double, 11)
            }
        };

        private static Variant Value(UaType type, double v) => type switch
        {
            UaType.Boolean => new Variant(type, v != 0),
            UaType.SByte => new Variant(type, unchecked((sbyte)(long)v)),
            UaType.Byte => new Variant(type, unchecked((byte)(long)v)),
            UaType.Int16 => new Variant(type, unchecked((short)(long)v)),
            UaType.UInt16 => new Variant(type, unchecked((ushort)(long)v)),
            UaType.Int32 => new Variant(type, unchecked((int)(long)v)),
            UaType.UInt32 => new Variant(type, unchecked((uint)(long)v)),
            UaType.Int64 => new Variant(type, (long)v),
            UaType.UInt64 => new Variant(type, unchecked((ulong)(long)v)),
            UaType.Float => new Variant(type, (float)v),
            _ => new Variant(UaType.Double, v)
        };

        /// <summary>A client's write: a BOOL takes a Boolean, a number any numeric type (the PLC wraps it as its type does).</summary>
        private static uint Write(UaTagImage image, string name, UaType type, Variant v)
        {
            if (type == UaType.String)
                return v.Value is string text ? image.WriteText(name, text) ? 0u : 0x803B0000 : 0x80740000;   // [plc-string]
            double value;
            switch (v.Value)
            {
                case bool b when type == UaType.Boolean:
                    value = b ? 1 : 0;
                    break;
                case bool:
                    return 0x80740000;   // BadTypeMismatch
                case IConvertible n when type != UaType.Boolean && v.Type is >= UaType.SByte and <= UaType.Double:
                    value = n.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default:
                    return 0x80740000;
            }

            return image.Write(name, value) ? 0u : 0x803B0000;   // BadNotWritable
        }

        public void Dispose()
        {
            _refresh.Dispose();
            foreach (var s in _servers) s.Dispose();
            _servers.Clear();
        }
    }
}
