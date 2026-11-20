using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Radio;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>Radio medium without firmware: propagation, IR, Wi-Fi, controller pairing, USB cable.</summary>
    public class RadioTests
    {
        private readonly ITestOutputHelper _out;

        public RadioTests(ITestOutputHelper output) => _out = output;

        private static byte[] Mac(byte last) => new byte[] { 0x24, 0x0A, 0xC4, 0, 0, last };

        [Fact]
        public void PathLossFollowsFreeSpaceThenLogDistanceAndObstaclesAttenuate()
        {
            var p = new RadioPropagation { PathLossExponent = 3.0, ShadowingSigmaDb = 0 };
            // FSPL(1 m, 2.44 GHz) = 20·log10(4π·2.44e9/c) ≈ 40.2 dB
            Assert.Equal(40.2, p.PathLossDb(1.0), 1);
            Assert.Equal(40.2 + 30.0, p.PathLossDb(10.0), 1);   // +10·n dB per decade
            Assert.Equal(40.2 - 20 * Math.Log10(2), p.PathLossDb(0.5), 1);   // free space below d0
            var a = new Vec3(0, 0, 0.1);
            var b = new Vec3(4, 0, 0.1);
            double clear = p.MeanRssiDbm(3, 2, 2, a, b);
            p.Obstacles.Add(new RadioObstacle(new Vec3(2, 0, 0.5), new Vec3(0.1, 1, 0.5), 0, 5.0));
            Assert.Equal(clear - 5.0, p.MeanRssiDbm(3, 2, 2, a, b), 6);
            Assert.Equal(clear, p.MeanRssiDbm(3, 2, 2, a, new Vec3(0, 4, 0.1)), 6);   // ray beside the wall
        }

        [Fact]
        public void FadingIsDeterministicSymmetricAndHasTheConfiguredSpread()
        {
            var p = new RadioPropagation { ShadowingSigmaDb = 4.0 };
            Assert.Equal(p.FadingDb("A", "B", 1.234), p.FadingDb("B", "A", 1.234), 12);
            Assert.Equal(p.FadingDb("A", "B", 1.234), new RadioPropagation { ShadowingSigmaDb = 4.0 }.FadingDb("A", "B", 1.234), 12);
            var samples = Enumerable.Range(0, 4000).Select(i => p.FadingDb("A", "B", i * 0.7)).ToList();
            double mean = samples.Average();
            double sd = Math.Sqrt(samples.Select(x => (x - mean) * (x - mean)).Average());
            _out.WriteLine($"fading mean {mean:F2} dB, sd {sd:F2} dB");
            Assert.InRange(mean, -0.4, 0.4);
            Assert.InRange(sd, 3.4, 4.6);
            // Changes smoothly: 10 ms apart differs far less than σ.
            Assert.True(Math.Abs(p.FadingDb("A", "B", 5.00) - p.FadingDb("A", "B", 5.01)) < 0.5);
        }

        [Fact]
        public void PacketErrorRateRisesAtTheSensitivity()
        {
            double strong = RadioPropagation.PacketErrorRate(RadioTech.Ble, -60, 300);
            double edge = RadioPropagation.PacketErrorRate(RadioTech.Ble, -95, 300);
            double dead = RadioPropagation.PacketErrorRate(RadioTech.Ble, -110, 300);
            _out.WriteLine($"PER -60: {strong:E2}  -95: {edge:P1}  -110: {dead:P0}");
            Assert.True(strong < 1e-9);
            Assert.InRange(edge, 0.05, 0.8);
            Assert.Equal(1.0, dead);
        }

        [Fact]
        public void InfraredNeedsLineOfSightAndAimAndReproducesNecTiming()
        {
            var m = new RadioMedium();
            var e = new IrEmitter("remote", "Kumanda") { Position = () => new Vec3(0, 0, 0.05), Direction = () => Vec3.UnitX, HalfAngleDeg = 30, RangeM = 8 };
            var facing = Vec3.UnitX * -1;
            var r = new IrReceiver("rx", "TSOP") { Position = () => new Vec3(3, 0, 0.05), Facing = () => facing };
            m.Ir.Emitters.Add(e);
            m.Ir.Receivers.Add(r);
            Assert.True(m.Ir.LineOfSight(e, r));
            facing = Vec3.UnitX;   // receiver turned away
            Assert.False(m.Ir.LineOfSight(e, r));
            facing = -Vec3.UnitX;
            e.Direction = () => new Vec3(0, 1, 0);   // remote pointed 90° off
            Assert.False(m.Ir.LineOfSight(e, r));
            e.Direction = () => Vec3.UnitX;
            Assert.False(m.Ir.LineOfSight(new IrEmitter("far", "far") { Position = () => new Vec3(-9, 0, 0.05), Direction = () => Vec3.UnitX, RangeM = 8 }, r));
            m.Propagation.Obstacles.Add(new RadioObstacle(new Vec3(1.5, 0, 0.1), new Vec3(0.1, 0.5, 0.1), 0, 5));
            Assert.False(m.Ir.LineOfSight(e, r));   // a box blocks the light
            m.Propagation.Obstacles.Clear();

            // NEC frame (address 0x00, command 0x45 "CH−"): decode the receiver's output edges.
            double end = m.Ir.SendNec(e, 0.010, 0x00, 0x45);
            var edges = new List<(double T, bool High)>();
            const double slice = 50e-6;
            for (double t = 0; t < end + 0.005; t += slice)
            {
                m.Advance(t, slice);
                edges.AddRange(r.Edges);
            }

            var falls = edges.Where(x => !x.High).Select(x => x.T).ToList();
            var rises = edges.Where(x => x.High).Select(x => x.T).ToList();
            Assert.Equal(34, falls.Count);   // leader + 32 bits + stop
            Assert.Equal(0.010, falls[0], 9);
            Assert.Equal(0.019, rises[0], 9);   // 9 ms leader mark
            uint frame = 0;
            for (int i = 0; i < 32; i++)
            {
                double space = falls[i + 2] - rises[i + 1];
                if (space > 1.1e-3) frame |= 1u << i;
            }

            Assert.Equal(0x00u, frame & 0xFF);
            Assert.Equal(0xFFu, (frame >> 8) & 0xFF);
            Assert.Equal(0x45u, (frame >> 16) & 0xFF);
            Assert.Equal((uint)(~0x45 & 0xFF), frame >> 24);
        }

        [Fact]
        public void IrReceiverPartDrivesItsOutputPinLowDuringMarks()
        {
            var lib = PartLibrary.Default;
            var world = new WorldDocument();
            var tsop = world.AddComponent(lib, "tsop38238", new WorldPose { X = 1.0, Yaw = 180 });   // lens (+X) turned toward −X
            var bat = world.AddComponent(lib, "bat-4aa", new WorldPose { X = 1.2 });
            var remote = world.AddComponent(lib, "ir-remote-nec", new WorldPose { X = 0 });
            world.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = tsop.Id, Pin = "VS" });
            world.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = tsop.Id, Pin = "GND" });
            var circuit = WorldCompiler.Compile(world, lib);
            var medium = WorldRadio.Build(world, circuit, lib);
            Assert.Single(medium.Ir.Receivers);
            Assert.Single(medium.Ir.Emitters);
            Assert.True(medium.Ir.LineOfSight(medium.Ir.Emitters[0], medium.Ir.Receivers[0]));
            var sim = new AnalogSimulator(circuit.Compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            string outRef = circuit.ComponentRef(tsop.Id)!;
            medium.Ir.SendNec(medium.Ir.Emitters[0], 0.005, 0x00, 0x16);
            double minV = double.MaxValue, maxV = 0;
            for (int i = 0; i < 400; i++)
            {
                medium.Advance(sim.Time, 100e-6);
                sim.Step(100e-6);
                double v = circuit.Compiled.PinVoltage(sim, outRef, "OUT");
                if (sim.Time > 0.0055 && sim.Time < 0.0135) minV = Math.Min(minV, v);
                if (sim.Time < 0.004) maxV = Math.Max(maxV, v);
            }

            _out.WriteLine($"TSOP OUT idle {maxV:F2} V, during leader {minV:F2} V");
            Assert.True(maxV > 4.5, "idle output should be pulled high");
            Assert.True(minV < 0.3, "output should be low while the carrier is seen");
        }

        [Fact]
        public void IrLedNeedsA38kHzCarrierToTriggerTheReceiver()
        {
            var lib = PartLibrary.Default;
            var world = new WorldDocument();
            var uno = world.AddComponent(lib, "arduino-uno", new WorldPose { X = -0.2 });
            var res = world.AddComponent(lib, "res-330", new WorldPose { X = -0.1 });
            var led = world.AddComponent(lib, "led5-ir", new WorldPose { X = 0, Pitch = 90 });   // optical axis +Z turned to +X
            var tsop = world.AddComponent(lib, "tsop38238", new WorldPose { X = 1.5, Yaw = 180 });
            CableEnd E(WorldObject o, string pin) => new CableEnd { ObjectId = o.Id, Pin = pin };
            world.Connect(E(uno, "D3"), E(res, "A"));
            world.Connect(E(res, "B"), E(led, "A"));
            world.Connect(E(led, "K"), E(uno, "GND1"));
            world.Connect(E(uno, "5V"), E(tsop, "VS"));
            world.Connect(E(uno, "GND2"), E(tsop, "GND"));
            var circuit = WorldCompiler.Compile(world, lib);
            var medium = WorldRadio.Build(world, circuit, lib);
            Assert.Single(medium.Ir.Emitters);
            Assert.False(medium.Ir.LineOfSight(medium.Ir.Emitters[0], medium.Ir.Receivers[0]));   // LED dark: no range yet
            var sim = new AnalogSimulator(circuit.Compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var d3 = circuit.Compiled.Device<DigitalPin>(circuit.ComponentRef(uno.Id)! + ".D3")!;
            string outRef = circuit.ComponentRef(tsop.Id)!;
            double Run(double seconds)
            {
                double min = double.MaxValue;
                for (int i = 0; i < (int)(seconds / 100e-6); i++)
                {
                    medium.Advance(sim.Time, 100e-6);
                    sim.Step(100e-6);
                    min = Math.Min(min, circuit.Compiled.PinVoltage(sim, outRef, "OUT"));
                }

                return min;
            }

            d3.Mode = PinMode.OutputHigh;   // steady IR light: no carrier
            double steady = Run(0.01);
            d3.SetPwm(38000, 0.33, sim.Time);   // 38 kHz bursts like IRremote's sendNEC
            double carrier = Run(0.01);
            _out.WriteLine($"TSOP OUT min: steady LED {steady:F2} V, 38 kHz {carrier:F2} V");
            Assert.True(medium.Ir.LineOfSight(medium.Ir.Emitters[0], medium.Ir.Receivers[0]));
            Assert.True(steady > 4.0);
            Assert.True(carrier < 0.5);
        }

        [Fact]
        public void WifiStationJoinsRouterAndExchangesUdpAndStream()
        {
            var m = new RadioMedium();
            var ap = m.Add(new AccessPointNode("R", "Modem", Mac(0x10), new WifiNetwork { Ssid = "Ev", Password = "12345678", Channel = 11, Gateway = RadioMedium.Ip(192, 168, 1, 1) }));
            ap.Position = () => new Vec3(0, 0, 1);
            var a = m.Add(new Esp32RadioNode("A", "A", Mac(0x20)));
            var b = m.Add(new Esp32RadioNode("B", "B", Mac(0x30)));
            a.Position = () => new Vec3(3, 0, 0.1);
            b.Position = () => new Vec3(-3, 2, 0.1);
            m.ImplicitNetworkEnabled = false;
            m.WifiBegin(a, "Ev", "12345678", 0);
            m.WifiBegin(b, "Ev", "yanlis", 0);
            for (double t = 0; t < 2.0; t += 0.01) m.Advance(t, 0.01);
            var sa = m.WifiStatus(a, 2.0);
            var sb = m.WifiStatus(b, 2.0);
            _out.WriteLine($"A: status {sa.Status} {RadioMedium.FormatIp(sa.Ip)} {sa.Rssi} dBm ch {sa.Channel}; B: status {sb.Status}");
            Assert.Equal(3, sa.Status);
            Assert.Equal(11, sa.Channel);
            Assert.InRange(sa.Rssi, -85, -30);
            Assert.Equal(4, sb.Status);   // wrong password
            m.WifiBegin(b, "Ev", "12345678", 2.0);
            for (double t = 2.0; t < 4.0; t += 0.01) m.Advance(t, 0.01);
            Assert.Equal(3, m.WifiStatus(b, 4.0).Status);
            Assert.NotEqual(a.StaIp, b.StaIp);

            m.UdpBind(b, 4210, true);
            Assert.Equal(1, m.UdpSend(a, b.StaIp, 4210, 5000, new byte[] { 1, 2, 3 }, 4.0));
            Assert.Null(m.UdpReceive(b, 4210, 4.0));   // still in flight
            var pkt = m.UdpReceive(b, 4210, 4.02);
            Assert.NotNull(pkt);
            Assert.Equal(a.StaIp, pkt!.SrcIp);
            Assert.Equal(new byte[] { 1, 2, 3 }, pkt.Data);

            m.StreamListen(b, 80, true);
            int id = m.StreamConnect(a, b.StaIp, 80, 4.1);
            Assert.True(id > 0);
            Assert.Equal(id, m.StreamAccept(b, 80).Id);
            m.StreamSend(a, id, System.Text.Encoding.ASCII.GetBytes("GET /"), 4.1);
            var buf = new byte[16];
            int n = m.StreamReceive(b, id, buf, 4.2);
            Assert.Equal("GET /", System.Text.Encoding.ASCII.GetString(buf, 0, n));
            Assert.Equal(-3, m.StreamConnect(a, b.StaIp, 81, 4.2));

            // Carried 3 km away: the station loses the AP after the timeout.
            a.Position = () => new Vec3(3000, 0, 0.1);
            for (double t = 4.2; t < 8.0; t += 0.01) m.Advance(t, 0.01);
            Assert.Equal(5, m.WifiStatus(a, 8.0).Status);
            Assert.Contains(m.DrainEvents(), e => e.Message.Contains("koptu"));
        }

        [Fact]
        public void ImplicitNetworkAcceptsAnySsidWhenTheWorldHasNoRouter()
        {
            var m = new RadioMedium();
            var a = m.Add(new Esp32RadioNode("A", "A", Mac(0x20)));
            m.WifiBegin(a, "evdeki-ag", "x", 0);
            for (double t = 0; t < 1.5; t += 0.01) m.Advance(t, 0.01);
            var s = m.WifiStatus(a, 1.5);
            Assert.Equal(3, s.Status);
            Assert.Equal("192.168.1.77", RadioMedium.FormatIp(s.Ip));
        }

        [Fact]
        public void GamepadPairsBondsDropsOutOfRangeAndReconnects()
        {
            var m = new RadioMedium();
            m.Propagation.ShadowingSigmaDb = 2;
            var host = m.Add(new Esp32RadioNode("E", "Araba", Mac(0x40)));
            host.Position = () => new Vec3(0, 0, 0.05);
            var padPos = new Vec3(1.0, 0.5, 0.8);
            var pad = m.Add(new GamepadNode("P", "DualSense", new byte[] { 0xA0, 0xAB, 0x51, 1, 2, 3 }, GamepadModel.DualSense));
            pad.Position = () => padPos;
            double t = 0;
            void Run(double seconds)
            {
                double end = t + seconds;
                for (; t < end; t += 0.005) m.Advance(t, 0.005);
            }

            m.PadEnable(host, true);
            Run(2.0);
            Assert.False(pad.Connected);   // not discoverable, not bonded
            m.StartPairing(pad);
            Run(0.6);
            Assert.False(pad.Connected);   // pairing takes ≈ 1.2 s
            Run(1.0);
            Assert.True(pad.Connected);
            Assert.Equal(host.BluetoothMacText, pad.BondedHost);
            Assert.False(pad.PairingMode);
            var events = m.DrainEvents();
            Assert.Contains(events, e => e.Message.StartsWith("Eşleştirildi", StringComparison.Ordinal));

            pad.Input.AxisY = GamepadState.Axis(-1.0);
            pad.Input.Set(GamepadState.ButtonA, true);
            var slots = m.PadPoll(host, t + 0.02);
            Assert.Same(pad, slots[0].Pad);
            Assert.Equal(-511, slots[0].Seen.AxisY);
            Assert.True(slots[0].Seen.Pressed(GamepadState.ButtonA));
            Run(0.6);
            Assert.True(pad.Stats.TxRate(t) > 150, "reports/s " + pad.Stats.TxRate(t));   // ≈ 250 reports/s

            m.PadFeedback(host, 0, f => { f.Red = 0; f.Green = 255; f.Blue = 64; }, t);
            Assert.Equal(255, pad.Feedback.Green);

            double rssiNear = m.MeanRssi(pad, host, RadioTech.Bluetooth);
            padPos = new Vec3(400, 0, 0.8);   // far beyond range
            double rssiFar = m.MeanRssi(pad, host, RadioTech.Bluetooth);
            _out.WriteLine($"RSSI near {rssiNear:F1} dBm, far {rssiFar:F1} dBm");
            Run(1.0);
            Assert.True(pad.Connected);   // supervision timeout not reached yet
            Run(1.5);
            Assert.False(pad.Connected);
            Assert.Null(m.PadPoll(host, t).First().Pad);
            Assert.Contains(m.DrainEvents(), e => e.Warning && e.Message.Contains("menzil dışı"));

            padPos = new Vec3(0.8, 0, 0.8);   // back: bonded pads page their host by themselves
            Run(1.0);
            Assert.True(pad.Connected);
            Assert.Contains(m.DrainEvents(), e => e.Message.StartsWith("Kumanda bağlandı", StringComparison.Ordinal));

            m.PadForget(host, t);
            Assert.False(pad.Connected);
            Assert.Null(pad.BondedHost);
        }

        [Fact]
        public void BleScanHearsBeaconsWithDistanceDependentRssi()
        {
            var m = new RadioMedium();
            m.Propagation.ShadowingSigmaDb = 0;
            var scanner = m.Add(new Esp32RadioNode("S", "Tarayıcı", Mac(0x50)));
            var near = m.Add(new BeaconNode("B1", "Yakın", Mac(0x60)) { AdvName = "yakin" });
            var far = m.Add(new BeaconNode("B2", "Uzak", Mac(0x70)) { AdvName = "uzak" });
            var gone = m.Add(new BeaconNode("B3", "Çok uzak", Mac(0x80)) { AdvName = "yok" });
            near.Position = () => new Vec3(0.5, 0, 0);
            far.Position = () => new Vec3(8, 0, 0);
            gone.Position = () => new Vec3(3000, 0, 0);
            var results = m.BleScan(scanner, 0, 2.0);
            foreach (var r in results) _out.WriteLine($"{r.Name}: {r.RssiDbm:F1} dBm heard {r.Heard}×");
            Assert.Equal(new[] { "yakin", "uzak" }, results.Select(r => r.Name).ToArray());
            Assert.True(results[0].RssiDbm > results[1].RssiDbm + 20);
            Assert.True(results[0].Heard >= 15);
        }

        [Fact]
        public void UsbCableSuppliesBoardAndUnplugRemovesPower()
        {
            var lib = PartLibrary.Default;
            var world = new WorldDocument();
            var esp = world.AddComponent(lib, "esp32-devkit-v1", new WorldPose());
            var pc = world.AddComponent(lib, "usb-pc", new WorldPose { X = -0.3 });
            pc.Parameters["target"] = esp.Id;
            pc.Parameters["plugged"] = "false";
            var circuit = WorldCompiler.Compile(world, lib);
            var prog = circuit.Programmables.Single();
            Assert.Equal("cable", prog.Parameters["usb"]);
            var sim = new AnalogSimulator(circuit.Compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            for (int i = 0; i < 50; i++) sim.Step(1e-3);
            double off = circuit.Compiled.PinVoltage(sim, prog.RefDes, "3V3");
            Assert.True(WorldUsb.SetPlugged(circuit, esp.Id, true));
            for (int i = 0; i < 100; i++) sim.Step(1e-3);
            double on = circuit.Compiled.PinVoltage(sim, prog.RefDes, "3V3");
            WorldUsb.SetPlugged(circuit, esp.Id, false);
            for (int i = 0; i < 200; i++) sim.Step(1e-3);
            double unplugged = circuit.Compiled.PinVoltage(sim, prog.RefDes, "3V3");
            _out.WriteLine($"3V3: unplugged at start {off:F2} V, plugged {on:F2} V, unplugged again {unplugged:F2} V");
            Assert.True(off < 0.5);
            Assert.InRange(on, 3.2, 3.4);
            Assert.True(unplugged < 1.0);
            Assert.False(WorldUsb.SerialConnected(world, esp.Id));
            pc.Parameters["plugged"] = "true";
            Assert.True(WorldUsb.SerialConnected(world, esp.Id));

            // A board without any USB cable keeps the legacy behaviour (powered).
            var legacy = new WorldDocument();
            legacy.AddComponent(lib, "arduino-uno", new WorldPose());
            var lc = WorldCompiler.Compile(legacy, lib);
            Assert.Equal("true", lc.Programmables.Single().Parameters["usb"]);
        }

        [Fact]
        public void CatalogHasWirelessParts()
        {
            var lib = PartLibrary.Default;
            foreach (var id in new[] { "dualsense", "dualshock4", "xbox-pad", "usb-pc", "wifi-router", "ble-beacon", "ir-remote-nec", "tsop38238", "drv8833" })
            {
                Assert.NotNull(lib.FindPreset(id));
            }

            var tsop = lib.Get(WirelessParts.IrReceiverId);
            Assert.Equal(new[] { "OUT", "GND", "VS" }, tsop.Pins.Select(p => p.Name).ToArray());
        }
    }
}
