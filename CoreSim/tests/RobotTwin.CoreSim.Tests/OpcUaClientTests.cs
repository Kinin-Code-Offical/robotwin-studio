using System;
using System.Diagnostics;
using System.Linq;
using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Industrial.OpcUa;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[opcua-client] The PLC as an OPC UA client: UA_READ and UA_WRITE against a server (ours, as another PLC serves it).</summary>
    public class OpcUaClientTests
    {
        private static StProgram Compile(string source)
        {
            var p = StProgram.Compile(source, out var errors);
            Assert.True(p != null, string.Join("\n", errors));
            return p!;
        }

        /// <summary>Scans (10 ms each) until <paramref name="done"/> holds, at most 10 s of the wall clock (the network is real).</summary>
        private static void ScanUntil(StProgram p, Func<bool> done)
        {
            var w = Stopwatch.StartNew();
            while (!done() && w.Elapsed.TotalSeconds < 10)
            {
                Assert.True(p.Scan(10), p.Fault);
                System.Threading.Thread.Sleep(1);
            }

            Assert.True(done(), "the request did not finish");
        }

        [Fact]
        public void APlcReadsAndWritesAnotherServersVariables()
        {
            var image = new UaTagImage("Line", "127.0.0.1", 0);
            image.Publish(new[]
            {
                new UaTagImage.Tag("SPEED", StType.Real, "REAL", true, 12.5),
                new UaTagImage.Tag("COUNT", StType.Int, "INT", true, 7),
                new UaTagImage.Tag("RECIPE", StType.String, "STRING", true, 0, "Bolt-M6"),
                new UaTagImage.Tag("SENSOR", StType.Bool, "BOOL", false, 1)
            }, true);
            using var host = new OpcUaHost(new[] { image });
            Assert.Empty(host.Problems);
            string url = host.Servers[0].EndpointUrl;

            var p = Compile($@"
VAR
    Rd, RdText, RdBad : UA_READ;
    Wr, WrText, WrLocked : UA_WRITE;
    Go : BOOL;
    Speed : REAL;
    Recipe : STRING;
    Url : STRING := '{url}';
    BadStatus, LockedStatus : DINT;
END_VAR
Rd(REQ := Go, URL := Url, NODE := 'ns=1;s=Line.SPEED');
RdText(REQ := Go, URL := Url, NODE := 'ns=1;s=Line.RECIPE');
RdBad(REQ := Go, URL := Url, NODE := 'ns=1;s=Line.NOPE');
Wr(REQ := Go, URL := Url, NODE := 'ns=1;s=Line.COUNT', VALUE := 41.6);
WrText(REQ := Go, URL := Url, NODE := 'ns=1;s=Line.RECIPE', TEXT := 'Nut-M8');
WrLocked(REQ := Go, URL := Url, NODE := 'ns=1;s=Line.SENSOR', VALUE := 0);
IF Rd.DONE THEN Speed := Rd.VALUE; END_IF;
IF RdText.DONE THEN Recipe := RdText.TEXT; END_IF;
IF RdBad.ERROR THEN BadStatus := RdBad.STATUS; END_IF;
IF WrLocked.ERROR THEN LockedStatus := WrLocked.STATUS; END_IF;
");
            Assert.True(p.Scan(10));
            Assert.Equal(0, p.Read("RD.BUSY"));
            p.Write("GO", 1);
            Assert.True(p.Scan(10));
            Assert.Equal(1, p.Read("RD.BUSY"));   // on the network, beside the scan
            ScanUntil(p, () => p.Read("SPEED") != 0 && p.ReadText("RECIPE") != string.Empty && p.Read("BADSTATUS") != 0
                               && p.Read("WR.BUSY") == 0 && p.Read("WRTEXT.BUSY") == 0 && p.Read("LOCKEDSTATUS") != 0);

            Assert.Equal(12.5, p.Read("SPEED"));
            Assert.Equal("Bolt-M6", p.ReadText("RECIPE"));
            Assert.Equal(0x8034, p.Read("BADSTATUS"));   // BadNodeIdUnknown
            Assert.Equal(0x803B, p.Read("LOCKEDSTATUS"));   // BadNotWritable: an input
            Assert.Equal(0, p.Read("WR.ERROR"));
            var writes = image.TakeWrites();
            Assert.Contains(writes, w => w.Name == "COUNT" && w.Value == 42);   // rounded into the node's INT
            Assert.Contains(writes, w => w.Name == "RECIPE" && w.Text == "Nut-M8");

            // nothing listens: an error, not a stopped PLC
            var q = Compile("VAR R : UA_READ; END_VAR R(REQ := TRUE, URL := 'opc.tcp://127.0.0.1:1', NODE := 'ns=1;s=X');");
            ScanUntil(q, () => q.Read("R.ERROR") == 1);
            Assert.Equal(UaClientStatusNoConnection, q.Read("R.STATUS"));
        }

        private const double UaClientStatusNoConnection = 0x8005;

        /// <summary>
        /// [opcua-sec] UA_READ with SECURITY, USER and PASSWORD: an unknown server's certificate is refused until the user
        /// trusts it, then the PLC reads signed and encrypted as a named user; a wrong password and an unknown SECURITY give
        /// the server's (or the block's) reason in STATUS.
        /// </summary>
        [Fact]
        public void APlcReadsASecuredServerAsANamedUser()
        {
            string pki = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rw-plc-pki-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            UaPki.DefaultFolderOverride = pki;
            try
            {
                var image = new UaTagImage("Line", "127.0.0.1", 0) { Security = RobotTwin.CoreSim.Design.CellParts.OpcUaSecurityEncrypt, Users = "plc:secret, guest:g:okur", TrustAllClients = true };
                image.Publish(new[] { new UaTagImage.Tag("SPEED", StType.Real, "REAL", true, 12.5) }, true);
                using var host = new OpcUaHost(new[] { image }, System.IO.Path.Combine(pki, "server"));
                Assert.Empty(host.Problems);
                string url = host.Servers[0].EndpointUrl;

                StProgram Read(string user, string password, string security) => Compile($@"
VAR
    R : UA_READ;
    Speed : REAL;
    Status : DINT;
END_VAR
R(REQ := TRUE, URL := '{url}', NODE := 'ns=1;s=Line.SPEED', USER := '{user}', PASSWORD := '{password}', SECURITY := '{security}');
IF R.DONE THEN Speed := R.VALUE; END_IF;
IF R.ERROR THEN Status := R.STATUS; END_IF;
");

                var unknown = Read("plc", "secret", "SIGNANDENCRYPT");
                ScanUntil(unknown, () => unknown.Read("STATUS") != 0);
                Assert.Equal(0x801A, unknown.Read("STATUS"));   // BadCertificateUntrusted: the server is not known yet
                var rejected = System.IO.Directory.GetFiles(System.IO.Path.Combine(pki, "rejected"));
                Assert.Single(rejected);
                var server = UaCertificates.Load(System.IO.File.ReadAllBytes(rejected[0]));
                new UaPki(pki).Trust(server);

                var ok = Read("plc", "secret", "SIGNANDENCRYPT");
                ScanUntil(ok, () => ok.Read("SPEED") != 0 || ok.Read("STATUS") != 0);
                Assert.Equal(0, ok.Read("STATUS"));
                Assert.Equal(12.5, ok.Read("SPEED"));

                var wrong = Read("plc", "nope", "SIGNANDENCRYPT");
                ScanUntil(wrong, () => wrong.Read("STATUS") != 0);
                Assert.Equal(0x801F, wrong.Read("STATUS"));   // BadUserAccessDenied

                var odd = Read("plc", "secret", "TLS");
                ScanUntil(odd, () => odd.Read("STATUS") != 0);
                Assert.Equal(0x8055, odd.Read("STATUS"));   // BadSecurityPolicyRejected

                var open = Read(string.Empty, string.Empty, string.Empty);
                ScanUntil(open, () => open.Read("STATUS") != 0);
                Assert.Equal(0x8055, open.Read("STATUS"));   // no None endpoint: discovery only
            }
            finally
            {
                UaPki.DefaultFolderOverride = null;
                try
                {
                    System.IO.Directory.Delete(pki, true);
                }
                catch (System.IO.IOException)
                {
                }
            }
        }

        [Fact]
        public void NodeIdsAndAddressesAreReadAsUaWritesThem()
        {
            Assert.True(UaClient.TryParseNodeId("ns=2;i=1001", out var a));
            Assert.Equal(new NodeId(2, 1001), a);
            Assert.True(UaClient.TryParseNodeId("ns=1;s=Line.Speed", out var b));
            Assert.Equal(new NodeId(1, "Line.Speed"), b);
            Assert.True(UaClient.TryParseNodeId("i=2258", out var c));
            Assert.Equal(NodeId.Ns0(2258), c);
            Assert.False(UaClient.TryParseNodeId("Line.Speed", out _));
            Assert.Equal(("plc.local", 4840), UaClient.Endpoint("opc.tcp://plc.local"));
            Assert.Equal(("10.0.0.5", 4841), UaClient.Endpoint("opc.tcp://10.0.0.5:4841/UA/Server"));
        }
    }
}
