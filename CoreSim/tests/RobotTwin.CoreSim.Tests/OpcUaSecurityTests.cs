using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using RobotTwin.CoreSim.Industrial.OpcUa;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [opcua-sec] Basic256Sha256: the key derivation against an independent vector, a channel's messages signed, encrypted
    /// and refused when changed, and whole conversations between our client and server: signed and encrypted, named users,
    /// read-only users, a wrong password, untrusted certificates and a server without a None endpoint.
    /// </summary>
    public class OpcUaSecurityTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly string _pkiRoot = Path.Combine(Path.GetTempPath(), "rw-opcua-pki-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        public OpcUaSecurityTests(ITestOutputHelper output) => _out = output;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_pkiRoot)) Directory.Delete(_pkiRoot, true);
            }
            catch (IOException)
            {
            }
        }

        private static byte[] Hex(string s) => Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(2 * i, 2), 16)).ToArray();

        [Fact]
        public void PSha256MatchesTheTls12Vector()
        {
            // RFC 5246's PRF with SHA-256: P_SHA256(secret, "test label" + seed), as computed by Python's hmac as well
            byte[] secret = Hex("9bbe436ba940f017b17652849a71db35");
            byte[] seed = Encoding.ASCII.GetBytes("test label").Concat(Hex("a0ba9f936cda311827a6f796ffd5198c")).ToArray();
            Assert.Equal("e3f229ba727be17b8d122620557cd453c2aab21d07c3d495329b52d4e61edb5a",
                BitConverter.ToString(UaChannelKeys.PSha256(secret, seed, 32)).Replace("-", string.Empty).ToLowerInvariant());
            var keys = UaChannelKeys.Derive(new byte[32], Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
            Assert.Equal((32, 32, 16), (keys.SigningKey.Length, keys.EncryptingKey.Length, keys.Iv.Length));
        }

        [Fact]
        public void AChannelsMessagesAreSignedAndEncryptedAndAChangedByteIsRefused()
        {
            var server = UaCertificates.CreateSelfSigned("urn:test:server", "server");
            var client = UaCertificates.CreateSelfSigned("urn:test:client", "client");
            byte[] cn = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(), sn = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();
            foreach (var mode in new[] { UaSecurityMode.Sign, UaSecurityMode.SignAndEncrypt })
            {
                var a = new UaChannelSecurity(UaSecurityPolicy.Basic256Sha256, mode, client, server);
                var b = new UaChannelSecurity(UaSecurityPolicy.Basic256Sha256, mode, server, client);
                a.SetKeys(1, UaChannelKeys.Derive(sn, cn), UaChannelKeys.Derive(cn, sn));
                b.SetKeys(1, UaChannelKeys.Derive(cn, sn), UaChannelKeys.Derive(sn, cn));
                byte[] inner = Enumerable.Range(0, 1000).Select(i => (byte)(i * 7)).ToArray();
                byte[] wire = a.EncodeSymmetric("MSG", 'F', 9, inner);
                Assert.Equal(inner, b.DecodeSymmetric(wire));
                if (mode == UaSecurityMode.SignAndEncrypt) Assert.True(wire.Skip(16).Take(inner.Length).SequenceEqual(inner) == false, "encrypted");
                else Assert.True(wire.Skip(16).Take(inner.Length).SequenceEqual(inner), "signed only: readable");
                wire[40] ^= 1;
                Assert.Throws<UaSecurityException>(() => b.DecodeSymmetric(wire));
            }

            // OpenSecureChannel: encrypted for the receiver's key, signed with the sender's
            var c2s = new UaChannelSecurity(UaSecurityPolicy.Basic256Sha256, UaSecurityMode.SignAndEncrypt, client, server);
            var s2c = new UaChannelSecurity(UaSecurityPolicy.Basic256Sha256, UaSecurityMode.SignAndEncrypt, server, client);
            byte[] body = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
            byte[] opn = c2s.EncodeAsymmetric(0, body);
            var head = UaChannelSecurity.ReadAsymmetricHeader(opn);
            Assert.Equal(UaSecurityPolicy.Basic256Sha256, head.Policy);
            Assert.Equal(client.RawData, head.SenderCertificate);
            Assert.Equal(body, s2c.DecodeAsymmetric(opn, head.Body));
            opn[opn.Length - 5] ^= 0x80;
            Assert.Throws<UaSecurityException>(() => s2c.DecodeAsymmetric(opn, head.Body));
            Assert.Equal("urn:test:client", UaCertificates.ApplicationUri(client));
        }

        private (UaServer Server, Func<double> Speed, Func<bool> Run) Serve(UaServerSecurity security)
        {
            var space = new UaAddressSpace("urn:robotwin:test");
            double speed = 1.5;
            bool run = false;
            var plc = space.AddObject(new NodeId(1, "PLC1"), "PLC1", NodeId.Ns0(UaAddressSpace.ObjectsFolder));
            space.AddVariable(new NodeId(1, "PLC1.SPEED"), "SPEED", plc.Id, 11, () => new DataValue { Value = new Variant(UaType.Double, speed) },
                v =>
                {
                    speed = Convert.ToDouble(v.Value);
                    return 0;
                });
            space.AddVariable(new NodeId(1, "PLC1.RUN"), "RUN", plc.Id, 1, () => new DataValue { Value = new Variant(UaType.Boolean, run) },
                v =>
                {
                    run = (bool)v.Value!;
                    return 0;
                });
            var server = new UaServer(space, IPAddress.Loopback, 0, "test", security);
            server.Notice += m => _out.WriteLine("notice: " + m);
            server.Start();
            return (server, () => speed, () => run);
        }

        private UaServerSecurity Secured(UaPki? pki = null, bool none = false, bool anonymous = false)
        {
            var sec = new UaServerSecurity
            {
                AllowNone = none,
                AllowAnonymous = anonymous,
                Certificate = new UaPki(Path.Combine(_pkiRoot, "server")).Own("urn:robotwin:test", "test server"),
                Pki = pki
            };
            sec.Users["operator"] = ("op-pass", true);
            sec.Users["viewer"] = ("view-pass", false);
            return sec;
        }

        [Fact]
        public void ASignedAndEncryptedSessionWithANamedUserReadsAndWrites()
        {
            var clientCert = UaCertificates.CreateSelfSigned("urn:robotwin:plc", "plc client");
            var (server, speed, run) = Serve(Secured());
            using (server)
            {
                foreach (var mode in new[] { UaSecurityMode.SignAndEncrypt, UaSecurityMode.Sign })
                {
                    using var c = UaClient.Connect(server.EndpointUrl, 5000, new UaClientSecurity { Mode = mode, Certificate = clientCert, User = "operator", Password = "op-pass" });
                    Assert.Equal(mode, c.Mode);
                    var v = c.Read(new NodeId(1, "PLC1.SPEED"));
                    Assert.Equal(speed(), Convert.ToDouble(v.Value.Value));
                    Assert.Equal(0u, c.Write(new NodeId(1, "PLC1.RUN"), new Variant(UaType.Boolean, true)));
                    Assert.True(run());
                    Assert.Equal(0u, c.Write(new NodeId(1, "PLC1.SPEED"), new Variant(UaType.Double, 2.25)));
                    Assert.Equal(2.25, speed());
                }

                // a read-only user reads and is refused writing
                using (var viewer = UaClient.Connect(server.EndpointUrl, 5000, new UaClientSecurity { Mode = UaSecurityMode.SignAndEncrypt, Certificate = clientCert, User = "viewer", Password = "view-pass" }))
                {
                    Assert.Equal(2.25, Convert.ToDouble(viewer.Read(new NodeId(1, "PLC1.SPEED")).Value.Value));
                    Assert.Equal(0x801F0000u, viewer.Write(new NodeId(1, "PLC1.SPEED"), new Variant(UaType.Double, 9.0)));   // BadUserAccessDenied
                    Assert.Equal(2.25, speed());
                }

                // a wrong password, an anonymous user (not offered), and an unsecured session (no None endpoint) are turned away
                var wrong = Assert.Throws<UaServiceException>(() => UaClient.Connect(server.EndpointUrl, 5000, new UaClientSecurity { Mode = UaSecurityMode.SignAndEncrypt, Certificate = clientCert, User = "operator", Password = "nope" }));
                Assert.Contains("0x801F0000", wrong.Message);
                var anonymous = Assert.Throws<UaServiceException>(() => UaClient.Connect(server.EndpointUrl, 5000, new UaClientSecurity { Mode = UaSecurityMode.SignAndEncrypt, Certificate = clientCert }));
                Assert.Contains("0x80210000", anonymous.Message);   // BadIdentityTokenRejected
                var open = Assert.Throws<UaServiceException>(() => UaClient.Connect(server.EndpointUrl, 5000));
                Assert.Contains("0x80550000", open.Message);   // BadSecurityPolicyRejected: discovery only
            }
        }

        [Fact]
        public void AnUntrustedClientIsRefusedUntilItsCertificateIsTrusted()
        {
            var pki = new UaPki(Path.Combine(_pkiRoot, "server-trust"));
            var clientCert = UaCertificates.CreateSelfSigned("urn:robotwin:plc", "unknown client");
            string? notice = null;
            var (server, _, _) = Serve(Secured(pki));
            server.Notice += m => notice = m;
            using (server)
            {
                var refused = Assert.Throws<UaServiceException>(() => UaClient.Connect(server.EndpointUrl, 5000, new UaClientSecurity { Mode = UaSecurityMode.SignAndEncrypt, Certificate = clientCert, User = "operator", Password = "op-pass" }));
                Assert.Contains("0x801A0000", refused.Message);   // BadCertificateUntrusted
                Assert.Contains("unknown client", notice);
                Assert.Single(Directory.GetFiles(pki.RejectedFolder));
                pki.Trust(clientCert);
                Assert.Empty(Directory.GetFiles(pki.RejectedFolder));
                using var c = UaClient.Connect(server.EndpointUrl, 5000, new UaClientSecurity { Mode = UaSecurityMode.SignAndEncrypt, Certificate = clientCert, User = "operator", Password = "op-pass" });
                Assert.Equal(1.5, Convert.ToDouble(c.Read(new NodeId(1, "PLC1.SPEED")).Value.Value));
            }
        }

        [Fact]
        public void TheServersCertificateIsKeptAndItsEndpointsListEverySecurityItOffers()
        {
            var pki = new UaPki(Path.Combine(_pkiRoot, "own"));
            var first = pki.Own("urn:robotwin:plc:4840", "PLC1");
            var again = pki.Own("urn:robotwin:plc:4840", "PLC1");
            Assert.Equal(first.Thumbprint, again.Thumbprint);   // made once, then read back
            Assert.True(again.HasPrivateKey);
            Assert.Equal(2048, UaCertificates.KeyBits(again));
            Assert.Equal("urn:robotwin:plc:4840", UaCertificates.ApplicationUri(again));

            var (server, _, _) = Serve(Secured(none: true, anonymous: true));
            using (server)
            using (var c = UaClient.Connect(server.EndpointUrl, 5000))
            {
                var endpoints = c.GetEndpoints();
                Assert.Equal(new[] { (UaSecurityPolicy.None, UaSecurityMode.None), (UaSecurityPolicy.Basic256Sha256, UaSecurityMode.Sign), (UaSecurityPolicy.Basic256Sha256, UaSecurityMode.SignAndEncrypt) },
                    endpoints.Select(e => (e.Policy, e.Mode)).ToArray());
                Assert.All(endpoints, e => Assert.NotNull(e.Certificate));
                Assert.Equal(UaSecurityMode.None, c.Mode);
                Assert.Equal(1.5, Convert.ToDouble(c.Read(new NodeId(1, "PLC1.SPEED")).Value.Value));   // None and anonymous still offered here
            }
        }

        [Fact]
        public void AWorldsPlcServesOnlyEncryptedSessionsToItsUsers()
        {
            Assert.Equal(new[] { ("op", "pw:x", true), ("izle", "vp", false) }, OpcUaHost.ParseUsers(" op:pw:x ;\n izle:vp:okur, bozuk").ToArray());
            var w = new RobotTwin.CoreSim.Sandbox.WorldDocument();
            var plc = w.AddComponent(RobotTwin.CoreSim.Design.PartLibrary.Default, "plc-st-8di8do", new RobotTwin.CoreSim.Sandbox.WorldPose());
            plc.Name = "Hat1";
            plc.Parameters["opcua"] = "açık";
            plc.Parameters["opcua_port"] = "0";
            plc.Parameters["opcua_security"] = RobotTwin.CoreSim.Design.CellParts.OpcUaSecurityEncrypt;
            plc.Parameters["opcua_users"] = "op:pw, izle:vp:okur";
            plc.Parameters["opcua_clients"] = RobotTwin.CoreSim.Design.CellParts.OpcUaClientsTrusted;
            plc.Parameters["program"] = "VAR" + Environment.NewLine + "    Hedef : INT := 10;" + Environment.NewLine + "END_VAR" + Environment.NewLine;
            using var runner = new RobotTwin.CoreSim.Automation.WorldRunner(w, new RobotTwin.CoreSim.Automation.RunnerOptions { Firmware = false });
            runner.Step(0.05);
            string pki = Path.Combine(_pkiRoot, "world");
            using var host = new OpcUaHost(runner.Physics!.OpcUaImages, pki);
            Assert.Empty(host.Problems);
            _out.WriteLine(string.Join("; ", host.Listening));
            Assert.Contains("Basic256Sha256 imza ve şifre", host.Listening[0]);
            var server = host.Servers[0];
            var client = UaCertificates.CreateSelfSigned("urn:robotwin:plc", "SCADA");

            // an unknown client is refused and said so; trusted, it reads, and the read-only user cannot write
            Assert.Throws<UaServiceException>(() => UaClient.Connect(server.EndpointUrl, 5000, new UaClientSecurity { Mode = UaSecurityMode.SignAndEncrypt, Certificate = client, User = "op", Password = "pw" }));
            var notices = host.TakeNotices();
            Assert.Contains(notices, n => n.Contains("SCADA") && n.Contains("güvenilir değil"));
            new UaPki(pki).Trust(client);
            using (var op = UaClient.Connect(server.EndpointUrl, 5000, new UaClientSecurity { Mode = UaSecurityMode.SignAndEncrypt, Certificate = client, User = "op", Password = "pw" }))
                Assert.Equal((short)10, (short)op.Read(new NodeId(1, "Hat1.HEDEF")).Value.Value!);
            using (var viewer = UaClient.Connect(server.EndpointUrl, 5000, new UaClientSecurity { Mode = UaSecurityMode.SignAndEncrypt, Certificate = client, User = "izle", Password = "vp" }))
                Assert.Equal(0x801F0000u, viewer.Write(new NodeId(1, "Hat1.HEDEF"), new Variant(UaType.Int16, (short)99)));

            // the certificate is the PLC's: kept, and the same at the next run
            Assert.Single(Directory.GetFiles(Path.Combine(pki, "own"), "*.pfx"));
        }
    }
}
