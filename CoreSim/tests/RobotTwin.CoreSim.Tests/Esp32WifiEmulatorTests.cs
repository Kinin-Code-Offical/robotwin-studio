using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Firmware.Xtensa;
using RobotTwin.CoreSim.Sandbox.Radio;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [esp32emu] Wi-Fi in the hardware-compatible mode: a real Arduino-ESP32 build joins the radio medium's network with
    /// its own lwIP (DHCP, ARP, UDP, TCP) through the emulator's model of the closed Wi-Fi driver.
    /// RW_ESP32_WIFI_ELF names the Wi-Fi sketch's ELF (WiFi.begin, then a UDP datagram and a TCP exchange with 192.168.1.10).
    /// </summary>
    public class Esp32WifiEmulatorTests
    {
        private readonly ITestOutputHelper _out;

        public Esp32WifiEmulatorTests(ITestOutputHelper output) => _out = output;

        public sealed class WifiElfFactAttribute : FactAttribute
        {
            public WifiElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_WIFI_ELF"))) Skip = "RW_ESP32_WIFI_ELF verilmedi.";
            }
        }

        [Fact]
        public void TheStackAnswersArpAndDhcpAsTheAccessPoint()
        {
            var medium = new RadioMedium();
            var node = medium.Add(new Esp32RadioNode("esp", "ESP32", new byte[] { 0x24, 0x0A, 0xC4, 1, 2, 3 }));
            var net = new MediumWifiNetwork(medium, node);
            net.Mode(1);
            net.Begin("ev", "", 0);
            var s = net.Status(2.0);
            Assert.Equal(3, s.Status);
            var stack = new Esp32WifiStack(net);
            var frames = new System.Collections.Generic.List<byte[]>();
            stack.Deliver = (ifx, frame) => frames.Add(frame);
            stack.LinkUp(0, s.Ip, s.Gateway, s.Bssid, node.Mac);

            // DHCPDISCOVER from 0.0.0.0:68 to 255.255.255.255:67
            var bootp = new byte[244];
            bootp[0] = 1; bootp[1] = 1; bootp[2] = 6;
            bootp[4] = 0xDE; bootp[5] = 0xAD;
            Array.Copy(node.Mac, 0, bootp, 28, 6);
            bootp[236] = 0x63; bootp[237] = 0x82; bootp[238] = 0x53; bootp[239] = 0x63;
            bootp[240] = 53; bootp[241] = 1; bootp[242] = 1; bootp[243] = 255;
            stack.Transmit(0, Ethernet(node.Mac, Ip(0, 0xFFFFFFFF, 17, Udp(68, 67, bootp))), 2.0);
            var offer = Assert.Single(frames);
            Assert.Equal(0xFF, offer[0]);                                         // broadcast to the station
            Assert.Equal(s.Ip, BitConverter.ToUInt32(offer, 14 + 20 + 8 + 16));  // yiaddr: the address the network gave it
            Assert.Equal(2, offer[14 + 20 + 8 + 240 + 2]);                       // DHCPOFFER

            // ARP for the gateway: the AP's MAC
            frames.Clear();
            var arp = new byte[28];
            arp[1] = 1; arp[2] = 8; arp[4] = 6; arp[5] = 4; arp[7] = 1;
            Array.Copy(node.Mac, 0, arp, 8, 6);
            BitConverter.GetBytes(s.Ip).CopyTo(arp, 14);
            BitConverter.GetBytes(s.Gateway).CopyTo(arp, 24);
            var f = new byte[14 + 28];
            for (int i = 0; i < 6; i++) f[i] = 0xFF;
            Array.Copy(node.Mac, 0, f, 6, 6);
            f[12] = 8; f[13] = 6;
            Array.Copy(arp, 0, f, 14, 28);
            stack.Transmit(0, f, 2.0);
            var reply = Assert.Single(frames);
            Assert.Equal(2, reply[21]);
            Assert.Equal(s.Bssid, reply.Skip(22).Take(6).ToArray());
        }

        private static byte[] Ethernet(byte[] src, byte[] ip)
        {
            var f = new byte[14 + ip.Length];
            for (int i = 0; i < 6; i++) f[i] = 0xFF;
            Array.Copy(src, 0, f, 6, 6);
            f[12] = 8;
            Array.Copy(ip, 0, f, 14, ip.Length);
            return f;
        }

        private static byte[] Ip(uint src, uint dst, byte proto, byte[] payload)
        {
            var p = new byte[20 + payload.Length];
            p[0] = 0x45;
            p[2] = (byte)(p.Length >> 8);
            p[3] = (byte)p.Length;
            p[8] = 64;
            p[9] = proto;
            BitConverter.GetBytes(src).CopyTo(p, 12);
            BitConverter.GetBytes(dst).CopyTo(p, 16);
            Array.Copy(payload, 0, p, 20, payload.Length);
            return p;
        }

        private static byte[] Udp(ushort sport, ushort dport, byte[] data)
        {
            var u = new byte[8 + data.Length];
            u[0] = (byte)(sport >> 8); u[1] = (byte)sport;
            u[2] = (byte)(dport >> 8); u[3] = (byte)dport;
            u[4] = (byte)(u.Length >> 8); u[5] = (byte)u.Length;
            Array.Copy(data, 0, u, 8, data.Length);
            return u;
        }

        [WifiElfFact]
        public void ARealWifiSketchJoinsTheNetworkAndTalksUdpAndTcp()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_WIFI_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var medium = new RadioMedium();
            // The other end: a node already on the network (192.168.1.77) with a UDP echo on 4210 and a TCP service on 8080.
            var peer = medium.Add(new Esp32RadioNode("pc", "Sunucu", new byte[] { 0x24, 0x0A, 0xC4, 9, 9, 9 }));
            medium.WifiMode(peer, 1);
            medium.WifiBegin(peer, "RobotWin", "12345678", -2.0);
            Assert.Equal(3, medium.WifiStatus(peer, 0).Status);
            uint peerIp = medium.WifiStatus(peer, 0).Ip;
            medium.UdpBind(peer, 4210, true);
            medium.StreamListen(peer, 8080, true);

            var node = medium.Add(new Esp32RadioNode("esp", "ESP32", new byte[] { 0x24, 0x0A, 0xC4, 1, 2, 3 }));
            var soc = new Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            soc.WifiNetwork = new MediumWifiNetwork(medium, node);
            soc.WifiLog += s => _out.WriteLine("wifi: " + s);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string peerUdp = "", peerTcp = "", clientReply = "";
            int served = 0, client = 0;
            var buffer = new byte[256];
            for (int i = 0; i < 800 && soc.Stopped == null; i++)
            {
                soc.Run((long)(0.01 * soc.CpuHz));
                double t = soc.Seconds;
                medium.WifiStatus(peer, t);
                var p = medium.UdpReceive(peer, 4210, t);
                if (p != null)
                {
                    peerUdp = System.Text.Encoding.ASCII.GetString(p.Data);
                    medium.UdpSend(peer, p.SrcIp, p.SrcPort, 4210, System.Text.Encoding.ASCII.GetBytes("cevap udp"), t);
                }

                if (served == 0) served = medium.StreamAccept(peer, 8080).Id;
                if (served > 0 && peerTcp.Length == 0)
                {
                    int n = medium.StreamReceive(peer, served, buffer, t);
                    if (n > 0)
                    {
                        peerTcp = System.Text.Encoding.ASCII.GetString(buffer, 0, n).Trim();
                        medium.StreamSend(peer, served, System.Text.Encoding.ASCII.GetBytes("merhaba " + peerTcp + "\n"), t);
                    }
                }

                // Then the sketch's own server: connect to it once it listens.
                if (client == 0 && soc.Uart0.ToString().Contains("tcp yanit"))
                {
                    uint espIp = medium.WifiStatus(node, t).Ip;
                    client = medium.StreamConnect(peer, espIp, 2323, t);
                    if (client > 0) medium.StreamSend(peer, client, System.Text.Encoding.ASCII.GetBytes("ping\n"), t);
                }
                else if (client > 0 && clientReply.Length == 0)
                {
                    int n = medium.StreamReceive(peer, client, buffer, t);
                    if (n > 0) clientReply = System.Text.Encoding.ASCII.GetString(buffer, 0, n).Trim();
                }

                if (clientReply.Length > 0 && soc.Uart0.ToString().Contains("sunucu:")) break;
            }

            _out.WriteLine($"{soc.Seconds:F2} s yonga zamanı {clock.Elapsed.TotalSeconds:F1} s'de · durdu: {soc.Stopped ?? "hayır"} · istasyon {soc.WifiStationState} · bekleyen çağrı {soc.PendingFirmwareCalls}");
            _out.WriteLine($"sunucu düğümü: UDP \"{peerUdp}\" · TCP \"{peerTcp}\" · istemci yanıtı \"{clientReply}\" (bağlantı {client})");
            string text = soc.Uart0.ToString();
            _out.WriteLine(text);
            Assert.Null(soc.Stopped);
            Assert.Contains($"wifi 3 ip 192.168.1.", text);
            Assert.DoesNotContain($"ip {Esp32WifiText(peerIp)} ", text);   // its own address, not the peer's
            Assert.Equal("merhaba udp", peerUdp);
            Assert.Contains("udp geldi: cevap udp", text);
            Assert.Contains("tcp baglandi", text);
            Assert.Equal("selam", peerTcp);
            Assert.Contains("tcp yanit: merhaba selam", text);
            Assert.Equal("eko: ping", clientReply);
            Assert.Contains("sunucu: ping", text);
        }

        [WifiElfFact]
        public void TheEmulatedBoardJoinsTheWorldsRadioMedium()
        {
            // The board path the app and the headless runners take: MediumWifiNetwork.Attach, the bootloader's delay, the
            // board's clock mapped to the world's.
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_WIFI_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;
            var sch = new RobotTwin.CoreSim.Design.Schematic { Title = "ESP32 Wi-Fi" };
            var part = sch.AddPart(lib, RobotTwin.CoreSim.Design.Esp32Parts.DevKitCId, new RobotTwin.CoreSim.Design.GridPoint(0, 0));
            var gnd = lib.Get(part.PartId).FindPin("GND2")!;
            sch.Labels.Add(new RobotTwin.CoreSim.Design.NetLabel { Name = "GND", Position = part.Transform(gnd.X, gnd.Y) });
            var compiled = RobotTwin.CoreSim.Design.CircuitCompiler.Compile(sch, lib);
            var sim = new RobotTwin.CoreSim.Analog.AnalogSimulator(compiled.Circuit);
            sim.Initialize(RobotTwin.CoreSim.Analog.InitialState.OperatingPoint);

            var medium = new RadioMedium();
            var peer = medium.Add(new Esp32RadioNode("pc", "Sunucu", new byte[] { 0x24, 0x0A, 0xC4, 9, 9, 9 }));
            medium.WifiMode(peer, 1);
            medium.WifiBegin(peer, "RobotWin", "12345678", -2.0);
            medium.WifiStatus(peer, 0);
            medium.UdpBind(peer, 4210, true);
            var node = medium.Add(new Esp32RadioNode("esp", "ESP32", new byte[] { 0x24, 0x0A, 0xC4, 1, 2, 3 }));
            using var board = new RobotTwin.CoreSim.Firmware.Esp32.Esp32EmulatedBoard(File.ReadAllBytes(elf), File.Exists(merged) ? File.ReadAllBytes(merged) : null, compiled, "U1");
            MediumWifiNetwork.Attach(medium, node, board);
            var serial = new System.Text.StringBuilder();
            board.SerialOutput += s => serial.Append(s);
            board.Log += s => _out.WriteLine("kart: " + s);
            string got = "";
            for (int i = 0; i < 4000 && got.Length == 0; i++)
            {
                board.Advance(1e-3, sim.Time);
                sim.Step(1e-3);
                var p = medium.UdpReceive(peer, 4210, sim.Time);
                if (p != null) got = System.Text.Encoding.ASCII.GetString(p.Data) + " @" + sim.Time.ToString("F2");
            }

            _out.WriteLine(serial.ToString());
            _out.WriteLine("sunucu düğümü: " + got);
            Assert.Contains("wifi 3 ip 192.168.1.78", serial.ToString());
            Assert.StartsWith("merhaba udp", got);
            Assert.True(node.IsOn());
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_WIFIAP_ELF names the soft-AP sketch's ELF (WiFi.softAP, a TCP echo on 80, a UDP answer on 4210).</summary>
        public sealed class WifiApElfFactAttribute : FactAttribute
        {
            public WifiApElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_WIFIAP_ELF"))) Skip = "RW_ESP32_WIFIAP_ELF verilmedi.";
            }
        }

        [WifiApElfFact]
        public void TheChipsOwnAccessPointTakesAStation()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_WIFIAP_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var medium = new RadioMedium { ImplicitNetworkEnabled = false };
            var node = medium.Add(new Esp32RadioNode("esp", "ESP32", new byte[] { 0x24, 0x0A, 0xC4, 1, 2, 3 }));
            var station = medium.Add(new Esp32RadioNode("phone", "Telefon", new byte[] { 0x24, 0x0A, 0xC4, 7, 7, 7 }));
            var soc = new Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            soc.WifiNetwork = new MediumWifiNetwork(medium, node);
            soc.WifiLog += s => _out.WriteLine("wifi: " + s);
            bool began = false;
            int stream = 0;
            string tcpReply = "", udpReply = "";
            uint stationIp = 0;
            var buffer = new byte[256];
            for (int i = 0; i < 600 && soc.Stopped == null && (tcpReply.Length == 0 || udpReply.Length == 0); i++)
            {
                soc.Run((long)(0.01 * soc.CpuHz));
                double t = soc.Seconds;
                if (!began && soc.SoftApUp)
                {
                    medium.WifiMode(station, 1);
                    medium.WifiBegin(station, "RobotWinAP", "12345678", t);
                    medium.UdpBind(station, 5000, true);
                    began = true;
                }

                if (!began) continue;
                var st = medium.WifiStatus(station, t);
                if (st.Status != 3) continue;
                stationIp = st.Ip;
                if (stream == 0)
                {
                    stream = medium.StreamConnect(station, MediumIp(192, 168, 4, 1), 80, t);
                    if (stream > 0) medium.StreamSend(station, stream, System.Text.Encoding.ASCII.GetBytes("selam ap\n"), t);
                    medium.UdpSend(station, MediumIp(192, 168, 4, 1), 4210, 5000, System.Text.Encoding.ASCII.GetBytes("merhaba ap"), t);
                }
                else if (stream > 0 && tcpReply.Length == 0)
                {
                    int n = medium.StreamReceive(station, stream, buffer, t);
                    if (n > 0) tcpReply = System.Text.Encoding.ASCII.GetString(buffer, 0, n).Trim();
                }

                var p = medium.UdpReceive(station, 5000, t);
                if (p != null) udpReply = System.Text.Encoding.ASCII.GetString(p.Data);
            }

            for (int i = 0; i < 20; i++) soc.Run((long)(0.01 * soc.CpuHz));
            string text = soc.Uart0.ToString();
            _out.WriteLine(text);
            _out.WriteLine($"istasyon {Esp32WifiText(stationIp)} · TCP \"{tcpReply}\" · UDP \"{udpReply}\" (akış {stream})");
            Assert.Null(soc.Stopped);
            Assert.Contains("ap 1 ip 192.168.4.1", text);
            Assert.Contains("istasyon 1", text);
            Assert.Equal("192.168.4.2", Esp32WifiText(stationIp));
            Assert.Equal("eko: selam ap", tcpReply);
            Assert.Equal("ap cevap", udpReply);
            Assert.Contains("udp 192.168.4.2: merhaba ap", text);
            Assert.Matches(@"tcp [0-9.]+: selam ap", text);   // (the sketch prints the peer after stop(): 0.0.0.0 on the chip too)
        }

        private static uint MediumIp(int a, int b, int c, int d) => (uint)(a | (b << 8) | (c << 16) | (d << 24));

        private static string Esp32WifiText(uint ip) => $"{ip & 0xFF}.{(ip >> 8) & 0xFF}.{(ip >> 16) & 0xFF}.{ip >> 24}";

        [WifiElfFact]
        public void TheWifiSketchFindsNoNetworkWhenNothingIsInRange()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_WIFI_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var soc = new Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            for (int i = 0; i < 1200 && soc.Stopped == null && !soc.Uart0.ToString().Contains("tcp "); i++) soc.Run((long)(0.01 * soc.CpuHz));
            string text = soc.Uart0.ToString();
            _out.WriteLine(text);
            Assert.Null(soc.Stopped);
            Assert.Contains("wifi 1 ip 0.0.0.0", text);   // WL_NO_SSID_AVAIL after the 10 s wait
        }
    }
}
