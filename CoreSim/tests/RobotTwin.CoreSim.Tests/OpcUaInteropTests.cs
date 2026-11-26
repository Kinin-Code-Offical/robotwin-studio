using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using RobotTwin.CoreSim.Industrial.OpcUa;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>A [Fact] that runs only when RW_ASYNCUA_PYTHON names a Python with asyncua (pip install asyncua).</summary>
    public sealed class AsyncuaFactAttribute : FactAttribute
    {
        public AsyncuaFactAttribute()
        {
            string? python = Environment.GetEnvironmentVariable("RW_ASYNCUA_PYTHON");
            if (string.IsNullOrEmpty(python) || !File.Exists(python)) Skip = "RW_ASYNCUA_PYTHON (a Python with asyncua) not set: the interoperability check against asyncua is skipped.";
        }
    }

    /// <summary>
    /// [opcua-sec] Against an independent OPC UA stack (FreeOpcUa's asyncua, tools/interop/opcua): its client signs, encrypts
    /// and logs in to our server, and our PLC client to its server, both with Basic256Sha256 and a named user.
    /// </summary>
    [Trait("Category", "Interop")]
    public class OpcUaInteropTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rw-opcua-interop-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        public OpcUaInteropTests(ITestOutputHelper output)
        {
            _out = output;
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }

        private static string Script(string name)
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string p = Path.Combine(d.FullName, "tools", "interop", "opcua", name);
                if (File.Exists(p)) return p;
            }

            throw new FileNotFoundException(name);
        }

        /// <summary>The certificate as DER and its private key as PEM (PKCS #8), as asyncua reads them.</summary>
        private (string Cert, string Key) Export(System.Security.Cryptography.X509Certificates.X509Certificate2 c, string name)
        {
            string cert = Path.Combine(_dir, name + ".der"), key = Path.Combine(_dir, name + ".pem");
            File.WriteAllBytes(cert, c.RawData);
            using var rsa = c.GetRSAPrivateKey()!;
            File.WriteAllText(key, "-----BEGIN PRIVATE KEY-----\n" + Convert.ToBase64String(rsa.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks) + "\n-----END PRIVATE KEY-----\n");
            return (cert, key);
        }

        private static Process Python(string script, params string[] args)
        {
            var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("RW_ASYNCUA_PYTHON")!)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-I");
            psi.ArgumentList.Add(script);
            foreach (var a in args) psi.ArgumentList.Add(a);
            return Process.Start(psi)!;
        }

        [AsyncuaFact]
        public void AsyncuasClientSignsEncryptsAndLogsInToOurServer()
        {
            var sec = new UaServerSecurity { AllowNone = false, AllowAnonymous = false, Certificate = UaCertificates.CreateSelfSigned("urn:robotwin:test", "RobotWin test server") };
            sec.Users["operator"] = ("op-pass", true);
            var space = new UaAddressSpace("urn:robotwin:test");
            double speed = 1.5;
            var plc = space.AddObject(new NodeId(1, "PLC1"), "PLC1", NodeId.Ns0(UaAddressSpace.ObjectsFolder));
            space.AddVariable(new NodeId(1, "PLC1.SPEED"), "SPEED", plc.Id, 11, () => new DataValue { Value = new Variant(UaType.Double, speed) },
                v =>
                {
                    speed = Convert.ToDouble(v.Value);
                    return 0;
                });
            using var server = new UaServer(space, IPAddress.Loopback, 0, "test", sec);
            server.Notice += m => _out.WriteLine("notice: " + m);
            server.Start();
            var client = Export(UaCertificates.CreateSelfSigned("urn:asyncua:interop", "asyncua client"), "client");
            string serverCert = Path.Combine(_dir, "server.der");
            File.WriteAllBytes(serverCert, sec.Certificate.RawData);
            foreach (string mode in new[] { "enc", "sign" })
            {
                speed = 1.5;
                using var p = Python(Script("asyncua_client.py"), server.EndpointUrl, client.Cert, client.Key, serverCert, mode);
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
                _out.WriteLine(mode + ": " + output + error);
                Assert.Equal(0, p.ExitCode);
                Assert.Contains("read 1.5", output);
                Assert.Contains("read2 3.5", output);
                Assert.Contains("Basic256Sha256:SignAndEncrypt", output);
                Assert.Equal(3.5, speed);
            }
        }

        [AsyncuaFact]
        public void OurPlcClientSignsEncryptsAndLogsInToAsyncuasServer()
        {
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
            }

            string url = $"opc.tcp://127.0.0.1:{port}/interop";
            var serverCert = UaCertificates.CreateSelfSigned("urn:asyncua:server", "asyncua server", new[] { "localhost", "127.0.0.1" });
            var exported = Export(serverCert, "aserver");
            using var p = Python(Script("asyncua_server.py"), url, exported.Cert, exported.Key);
            try
            {
                string? line;
                var wall = Stopwatch.StartNew();
                while ((line = p.StandardOutput.ReadLine()) != null && !line.StartsWith("READY", StringComparison.Ordinal) && wall.Elapsed.TotalSeconds < 30) _out.WriteLine(line);
                Assert.NotNull(line);
                string ns = line!.Split(' ')[1];
                var own = UaCertificates.CreateSelfSigned("urn:robotwin:plc", "RobotWin PLC");
                using var c = UaClient.Connect(url, 10000, new UaClientSecurity
                {
                    Mode = UaSecurityMode.SignAndEncrypt, Certificate = own, ServerCertificate = serverCert, User = "operator", Password = "op-pass"
                });
                Assert.True(UaClient.TryParseNodeId($"ns={ns};s=Plant.Level", out var node));
                Assert.Equal(4.5, Convert.ToDouble(c.Read(node).Value.Value));
                Assert.Equal(0u, c.Write(node, new Variant(UaType.Double, 6.0)));
                Assert.Equal(6.0, Convert.ToDouble(c.Read(node).Value.Value));
            }
            finally
            {
                try
                {
                    p.StandardInput.WriteLine();
                    if (!p.WaitForExit(5000)) p.Kill();
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
                {
                    // it has ended already
                }

                _out.WriteLine(p.StandardError.ReadToEnd());
            }
        }
    }
}
