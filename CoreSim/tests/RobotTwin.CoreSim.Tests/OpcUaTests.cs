using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RobotTwin.CoreSim.Industrial.OpcUa;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[opcua] The OPC UA server: the binary encoding, then a client's whole conversation over UA TCP.</summary>
    public class OpcUaTests
    {
        private readonly ITestOutputHelper _out;

        public OpcUaTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void NodeIdsAndValuesAreEncodedAsPart6Says()
        {
            string Hex(Action<UaWriter> write)
            {
                var w = new UaWriter();
                write(w);
                return BitConverter.ToString(w.ToArray()).Replace("-", " ");
            }

            // Part 6 §5.2.2.9: the two-byte, four-byte and string forms
            Assert.Equal("00 48", Hex(w => w.NodeId(NodeId.Ns0(72))));
            Assert.Equal("01 05 01 04", Hex(w => w.NodeId(new NodeId(5, 1025))));
            Assert.Equal("03 01 00 05 00 00 00 48 6F 74 C3 A6", Hex(w => w.NodeId(new NodeId(1, "Hotæ"))));   // UTF-8, its byte count first
            Assert.Equal("02 0A 00 40 42 0F 00", Hex(w => w.NodeId(new NodeId(10, 1_000_000))));
            Assert.Equal("FF FF FF FF", Hex(w => w.String(null)));
            Assert.Equal("01 01", Hex(w => w.Variant(new Variant(UaType.Boolean, true))));
            Assert.Equal("86 02 00 00 00 01 00 00 00 02 00 00 00", Hex(w => w.Variant(new Variant(UaType.Int32, new object?[] { 1, 2 }))));

            // and back
            var all = new UaWriter();
            all.NodeId(new NodeId(1, "Plc.Speed"));
            all.Variant(new Variant(UaType.Double, 12.5));
            all.DataValue(new DataValue { Value = new Variant(UaType.Int16, (short)-3), Status = 0x80340000, SourceTimestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc) });
            all.LocalizedText(new LocalizedText("Hız", "tr"));
            var r = new UaReader(all.ToArray());
            Assert.Equal(new NodeId(1, "Plc.Speed"), r.NodeId());
            Assert.Equal(12.5, (double)r.Variant().Value!);
            var dv = r.DataValue();
            Assert.Equal((short)-3, (short)dv.Value.Value!);
            Assert.Equal(0x80340000u, dv.Status);
            Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), dv.SourceTimestamp);
            var text = r.LocalizedText();
            Assert.Equal(("tr", "Hız"), (text.Locale, text.Text));
            Assert.Equal(0, r.Remaining);
        }

        /// <summary>A small client of our own, enough for the conversation a SCADA has: UA TCP, security None, anonymous.</summary>
        private sealed class Client : IDisposable
        {
            private readonly TcpClient _tcp = new TcpClient();
            private readonly NetworkStream _s;
            private uint _channel, _token, _sequence = 1, _request = 1, _handle = 1;
            public NodeId Auth;

            public Client(int port, string url)
            {
                _tcp.Connect(IPAddress.Loopback, port);
                _s = _tcp.GetStream();
                var hel = new UaWriter();
                hel.UInt32(0);
                hel.UInt32(65536);
                hel.UInt32(65536);
                hel.UInt32(0);
                hel.UInt32(0);
                hel.String(url);
                Frame("HEL", hel.ToArray());
                var (type, _) = Receive();
                Assert.Equal("ACK", type);

                var opn = new UaWriter();
                opn.UInt32(0);
                opn.String(UaServer.SecurityPolicyNone);
                opn.ByteString(null);
                opn.ByteString(null);
                opn.UInt32(_sequence++);
                opn.UInt32(_request++);
                opn.NodeId(NodeId.Ns0(446));
                Header(opn);
                opn.UInt32(0);
                opn.Int32(0);   // issue
                opn.Int32(1);   // None
                opn.ByteString(null);
                opn.UInt32(600000);
                Frame("OPN", opn.ToArray());
                var (t, body) = Receive();
                Assert.Equal("OPN", t);
                var r = new UaReader(body);
                _channel = r.UInt32();
                Assert.Equal(UaServer.SecurityPolicyNone, r.String());
                r.ByteString();
                r.ByteString();
                r.UInt32();
                r.UInt32();
                Assert.Equal(NodeId.Ns0(449), r.NodeId());
                SkipResponseHeader(r, out uint status);
                Assert.Equal(0u, status);
                r.UInt32();
                Assert.Equal(_channel, r.UInt32());
                _token = r.UInt32();
            }

            private void Header(UaWriter w)
            {
                w.NodeId(Auth);
                w.DateTime(DateTime.UtcNow);
                w.UInt32(_handle++);
                w.UInt32(0);
                w.String(null);
                w.UInt32(10000);
                w.ExtensionObject(null);
            }

            private void Frame(string type, byte[] body)
            {
                var head = Encoding.ASCII.GetBytes(type + "F");
                _s.Write(head, 0, 4);
                _s.Write(BitConverter.GetBytes((uint)body.Length + 8), 0, 4);
                _s.Write(body, 0, body.Length);
            }

            public (string Type, byte[] Body) Receive()
            {
                var head = Read(8);
                var body = Read((int)BitConverter.ToUInt32(head, 4) - 8);
                return (Encoding.ASCII.GetString(head, 0, 3), body);
            }

            private byte[] Read(int n)
            {
                var b = new byte[n];
                int got = 0;
                _tcp.ReceiveTimeout = 5000;
                while (got < n)
                {
                    int k = _s.Read(b, got, n - got);
                    if (k <= 0) throw new IOException("bağlantı kapandı");
                    got += k;
                }

                return b;
            }

            public static void SkipResponseHeader(UaReader r, out uint status)
            {
                r.DateTime();
                r.UInt32();
                status = r.StatusCode();
                r.SkipDiagnostics();
                r.Array(r.String);
                r.ExtensionObject();
            }

            /// <summary>Sends a request, returns the response's reader past its header (and the type it answered with, the service result).</summary>
            public UaReader Call(uint type, Action<UaWriter> body, out uint responseType, out uint status, bool wait = true)
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
                responseType = 0;
                status = 0;
                return wait ? Answer(out responseType, out status) : null!;
            }

            public UaReader Answer(out uint responseType, out uint status)
            {
                var (t, reply) = Receive();
                Assert.Equal("MSG", t);
                var r = new UaReader(reply);
                r.UInt32();
                r.UInt32();
                r.UInt32();
                r.UInt32();
                responseType = r.NodeId().Numeric;
                SkipResponseHeader(r, out status);
                return r;
            }

            public void Dispose() => _tcp.Close();
        }

        /// <summary>A session opened and activated on a client.</summary>
        private static void Session(Client c, string url)
        {
            var r = c.Call(461, w =>
            {
                w.String("urn:test:client");
                w.String(null);
                w.LocalizedText(new LocalizedText("test"));
                w.Int32(1);
                w.String(null);
                w.String(null);
                w.Int32(0);
                w.String(null);
                w.String(url);
                w.String("s");
                w.ByteString(new byte[32]);
                w.ByteString(null);
                w.Double(60000);
                w.UInt32(0);
            }, out uint type, out uint status);
            Assert.Equal((464u, 0u), (type, status));
            r.NodeId();
            c.Auth = r.NodeId();
            c.Call(467, w =>
            {
                w.String(null);
                w.ByteString(null);
                w.Int32(0);
                w.Int32(0);
                w.ExtensionObject(NodeId.Ns0(321), b => b.String("anonymous"));
                w.String(null);
                w.ByteString(null);
            }, out type, out status);
            Assert.Equal((470u, 0u), (type, status));
        }

        private static List<DataValue> Read(Client c, params NodeId[] ids)
        {
            var r = c.Call(631, w =>
            {
                w.Double(0);
                w.Int32(0);
                w.Array(ids, id =>
                {
                    w.NodeId(id);
                    w.UInt32(13);
                    w.String(null);
                    w.QualifiedName(new QualifiedName(0, string.Empty));
                });
            }, out uint type, out _);
            Assert.Equal(634u, type);
            return r.Array(r.DataValue);
        }

        [Fact]
        public void AWorldsPlcServesItsVariables()
        {
            // A PLC counting its scans and following a setpoint an operator writes over OPC UA.
            var w = new RobotTwin.CoreSim.Sandbox.WorldDocument();
            var plc = w.AddComponent(RobotTwin.CoreSim.Design.PartLibrary.Default, "plc-st-8di8do", new RobotTwin.CoreSim.Sandbox.WorldPose());
            plc.Name = "Hat1";
            plc.Parameters["opcua"] = "açık";
            plc.Parameters["opcua_port"] = "0";
            plc.Parameters["program"] = @"
VAR
    Lamba AT %QX0.0 : BOOL;
    Hedef : INT := 10;
    Sayac : DINT;
    Hiz : REAL := 1.5;
    Kayit : ARRAY[1..3] OF INT;
END_VAR
Sayac := Sayac + 1;
Kayit[2] := Hedef * 2;
Lamba := Hedef > 50;
";
            using var runner = new RobotTwin.CoreSim.Automation.WorldRunner(w, new RobotTwin.CoreSim.Automation.RunnerOptions { Firmware = false });
            runner.Step(0.05);
            using var host = new OpcUaHost(runner.Physics!.OpcUaImages);
            Assert.Empty(host.Problems);
            var server = host.Servers[0];
            _out.WriteLine(string.Join("; ", host.Listening));
            using var c = new Client(server.Port, server.EndpointUrl);
            Session(c, server.EndpointUrl);

            var v = Read(c, new NodeId(1, "Hat1.HEDEF"), new NodeId(1, "Hat1.SAYAC"), new NodeId(1, "Hat1.HIZ"), new NodeId(1, "Hat1.KAYIT[2]"), new NodeId(1, "Hat1.RUN"), new NodeId(1, "Hat1.LAMBA"));
            Assert.Equal(UaType.Int16, v[0].Value.Type);   // INT
            Assert.Equal((short)10, (short)v[0].Value.Value!);
            Assert.Equal(UaType.Int32, v[1].Value.Type);   // DINT
            Assert.InRange((int)v[1].Value.Value!, 4, 6);   // 10 ms scans over 50 ms
            Assert.Equal(1.5f, (float)v[2].Value.Value!);   // REAL
            Assert.Equal((short)20, (short)v[3].Value.Value!);
            Assert.True((bool)v[4].Value.Value!);
            Assert.False((bool)v[5].Value.Value!);

            // the operator writes the setpoint; the next scan takes it
            var r = c.Call(673, wr =>
            {
                wr.Int32(1);
                wr.NodeId(new NodeId(1, "Hat1.HEDEF"));
                wr.UInt32(13);
                wr.String(null);
                wr.DataValue(new DataValue { Value = new Variant(UaType.Int16, (short)60) });
            }, out uint type, out _);
            Assert.Equal(676u, type);
            Assert.Equal(new[] { 0u }, r.Array(r.StatusCode));
            runner.Step(0.02);
            Assert.Equal(60, runner.Physics!.PlcOf(plc.Id)!.Program!.Read("HEDEF"));
            v = Read(c, new NodeId(1, "Hat1.KAYIT[2]"), new NodeId(1, "Hat1.LAMBA"));
            Assert.Equal((short)120, (short)v[0].Value.Value!);
            Assert.True((bool)v[1].Value.Value!);   // the output the program drove from it
        }

        [Fact]
        public void AClientBrowsesReadsWritesAndIsToldOfChanges()
        {
            var space = new UaAddressSpace("urn:robotwin:test");
            double speed = 1.5;
            bool run = false;
            var plc = space.AddObject(new NodeId(1, "PLC1"), "PLC1", NodeId.Ns0(UaAddressSpace.ObjectsFolder));
            space.AddVariable(new NodeId(1, "PLC1.SPEED"), "SPEED", plc.Id, 11, () => new DataValue { Value = new Variant(UaType.Double, speed), SourceTimestamp = DateTime.UtcNow });
            space.AddVariable(new NodeId(1, "PLC1.RUN"), "RUN", plc.Id, 1, () => new DataValue { Value = new Variant(UaType.Boolean, run) },
                v =>
                {
                    if (v.Type != UaType.Boolean) return 0x80740000;   // BadTypeMismatch
                    run = (bool)v.Value!;
                    return 0;
                });
            using var server = new UaServer(space, IPAddress.Loopback, 0);
            server.Start();
            using var c = new Client(server.Port, server.EndpointUrl);

            // discovery
            var r = c.Call(428, w => { w.String(server.EndpointUrl); w.Int32(0); w.Int32(0); }, out uint type, out uint status);
            Assert.Equal((431u, 0u), (type, status));
            Assert.Equal(1, r.Int32());
            Assert.Equal(server.EndpointUrl, r.String());

            // a session
            r = c.Call(461, w =>
            {
                w.String("urn:test:client");
                w.String(null);
                w.LocalizedText(new LocalizedText("test"));
                w.Int32(1);
                w.String(null);
                w.String(null);
                w.Int32(0);
                w.String(null);
                w.String(server.EndpointUrl);
                w.String("s");
                w.ByteString(new byte[32]);
                w.ByteString(null);
                w.Double(60000);
                w.UInt32(0);
            }, out type, out status);
            Assert.Equal((464u, 0u), (type, status));
            r.NodeId();
            c.Auth = r.NodeId();
            r = c.Call(467, w =>
            {
                w.String(null);
                w.ByteString(null);
                w.Int32(0);
                w.Int32(0);
                w.ExtensionObject(NodeId.Ns0(321), b => b.String("anonymous"));
                w.String(null);
                w.ByteString(null);
            }, out type, out status);
            Assert.Equal((470u, 0u), (type, status));

            // browse Objects: the server and the PLC
            r = c.Call(527, w =>
            {
                w.NodeId(NodeId.Null);
                w.DateTime(DateTime.MinValue);
                w.UInt32(0);
                w.UInt32(0);
                w.Int32(1);
                w.NodeId(NodeId.Ns0(UaAddressSpace.ObjectsFolder));
                w.Int32(0);
                w.NodeId(NodeId.Ns0(UaAddressSpace.HierarchicalReferences));
                w.Boolean(true);
                w.UInt32(0);
                w.UInt32(63);
            }, out type, out status);
            Assert.Equal((530u, 0u), (type, status));
            Assert.Equal(1, r.Int32());
            Assert.Equal(0u, r.StatusCode());
            r.ByteString();
            var names = r.Array(() =>
            {
                r.NodeId();
                r.Boolean();
                r.ExpandedNodeId();
                var name = r.QualifiedName().Name;
                r.LocalizedText();
                r.Int32();
                r.ExpandedNodeId();
                return name;
            });
            Assert.Equal(new[] { "Server", "PLC1" }, names);

            // read a value, the namespace array and an unknown node
            r = c.Call(631, w =>
            {
                w.Double(0);
                w.Int32(2);
                w.Array(new[] { new NodeId(1, "PLC1.SPEED"), NodeId.Ns0(UaAddressSpace.NamespaceArray), new NodeId(1, "NOPE") }, id =>
                {
                    w.NodeId(id);
                    w.UInt32(13);
                    w.String(null);
                    w.QualifiedName(new QualifiedName(0, string.Empty));
                });
            }, out type, out status);
            Assert.Equal(634u, type);
            var values = r.Array(r.DataValue);
            Assert.Equal(1.5, (double)values[0].Value.Value!);
            Assert.Equal("urn:robotwin:test", ((object?[])values[1].Value.Value!)[1]);
            Assert.Equal(0x80340000u, values[2].Status);   // BadNodeIdUnknown
            var serverStatus = (ExtensionObject)Read(c, NodeId.Ns0(UaAddressSpace.ServerStatus))[0].Value.Value!;
            Assert.Equal(NodeId.Ns0(864), serverStatus.TypeId);   // ServerStatusDataType
            var sr = new UaReader(serverStatus.Body!);
            Assert.Equal(space.Started, sr.DateTime(), TimeSpan.FromMilliseconds(1));
            Assert.InRange(sr.DateTime(), DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(1));
            Assert.Equal(0, sr.Int32());   // Running

            // write: RUN takes a boolean, SPEED is read-only
            r = c.Call(673, w =>
            {
                w.Int32(3);
                foreach (var (id, v) in new[] { (new NodeId(1, "PLC1.RUN"), new Variant(UaType.Boolean, true)), (new NodeId(1, "PLC1.SPEED"), new Variant(UaType.Double, 9.0)), (new NodeId(1, "PLC1.RUN"), new Variant(UaType.Int32, 5)) })
                {
                    w.NodeId(id);
                    w.UInt32(13);
                    w.String(null);
                    w.DataValue(new DataValue { Value = v });
                }
            }, out type, out status);
            Assert.Equal(676u, type);
            Assert.Equal(new[] { 0u, 0x803B0000u, 0x80740000u }, r.Array(r.StatusCode));   // Good, BadNotWritable, BadTypeMismatch
            Assert.True(run);

            // subscribe to SPEED every 50 ms: its first value, then a change
            r = c.Call(787, w =>
            {
                w.Double(50);
                w.UInt32(300);
                w.UInt32(10);
                w.UInt32(0);
                w.Boolean(true);
                w.Byte(0);
            }, out type, out status);
            Assert.Equal((790u, 0u), (type, status));
            uint sub = r.UInt32();
            r = c.Call(751, w =>
            {
                w.UInt32(sub);
                w.Int32(2);
                w.Int32(1);
                w.NodeId(new NodeId(1, "PLC1.SPEED"));
                w.UInt32(13);
                w.String(null);
                w.QualifiedName(new QualifiedName(0, string.Empty));
                w.Int32(2);   // reporting
                w.UInt32(77);   // client handle
                w.Double(20);
                w.ExtensionObject(null);
                w.UInt32(1);
                w.Boolean(true);
            }, out type, out status);
            Assert.Equal((754u, 0u), (type, status));
            Assert.Equal(1, r.Int32());
            Assert.Equal(0u, r.StatusCode());

            List<(uint Handle, double Value)> Publish(out uint sequence)
            {
                var p = c.Call(826, w => w.Int32(0), out uint t, out uint s);
                Assert.Equal((829u, 0u), (t, s));
                Assert.Equal(sub, p.UInt32());
                p.Array(p.UInt32);
                p.Boolean();
                sequence = p.UInt32();
                p.DateTime();
                var list = new List<(uint, double)>();
                foreach (var e in p.Array(p.ExtensionObject))
                {
                    Assert.Equal(NodeId.Ns0(811), e.TypeId);
                    var b = new UaReader(e.Body!);
                    list.AddRange(b.Array(() => (b.UInt32(), Convert.ToDouble(b.DataValue().Value.Value))));
                }

                return list;
            }

            // A publish answered before the item's first sample (a slow machine) is a keep-alive: no notifications, the
            // sequence number not used up. A client publishes again, as this one does, a bounded number of times.
            List<(uint Handle, double Value)> PublishData(out uint sequence)
            {
                for (int tries = 0; ; tries++)
                {
                    var got = Publish(out sequence);
                    if (got.Count > 0 || tries >= 20) return got;
                }
            }

            var first = PublishData(out uint seq1);
            Assert.Equal(new[] { (77u, 1.5) }, first);
            speed = 2.75;
            var second = PublishData(out uint seq2);
            _out.WriteLine($"notifications: {string.Join(", ", first.Concat(second))}; sequence {seq1} → {seq2}");
            Assert.Equal(new[] { (77u, 2.75) }, second);
            Assert.Equal(seq1 + 1, seq2);

            // nothing changes: a keep-alive (no notifications) after the keep-alive count of intervals
            var start = DateTime.UtcNow;
            var idle = Publish(out uint seq3);
            Assert.Empty(idle);
            Assert.Equal(seq2 + 1, seq3);   // a keep-alive carries the next number without using it
            Assert.InRange((DateTime.UtcNow - start).TotalMilliseconds, 300, 3000);

            r = c.Call(473, w => w.Boolean(true), out type, out status);
            Assert.Equal((476u, 0u), (type, status));
            Assert.Equal(0, server.Sessions);
        }
    }
}
