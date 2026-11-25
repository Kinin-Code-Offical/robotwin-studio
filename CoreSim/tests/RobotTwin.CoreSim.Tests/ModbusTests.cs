using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[modbus] The Modbus protocol as the specification frames it, and a TCP client talking to the server.</summary>
    public class ModbusTests
    {
        private static byte[] Pdu(params int[] bytes) => Array.ConvertAll(bytes, b => (byte)b);

        [Fact]
        public void ReadsAndWritesEveryTableAsTheSpecificationFramesIt()
        {
            var image = new ModbusImage(coils: 20, discreteInputs: 8, holdingRegisters: 10, inputRegisters: 4);
            image.DiscreteInputs[0] = image.DiscreteInputs[2] = image.DiscreteInputs[7] = true;
            image.InputRegisters[1] = 0x1234;

            // 05: write coil 3 on; 0F: coils 8–17 to 0b11_0000_0101 (LSB first).
            Assert.Equal(Pdu(5, 0, 3, 0xFF, 0), ModbusTcpServer.Answer(image, Pdu(5, 0, 3, 0xFF, 0)));
            Assert.Equal(Pdu(15, 0, 8, 0, 10), ModbusTcpServer.Answer(image, Pdu(15, 0, 8, 0, 10, 2, 0x05, 0x03)));
            Assert.True(image.Coils[3] && image.Coils[8] && !image.Coils[9] && image.Coils[10] && image.Coils[16] && image.Coils[17]);
            // 01: coils 0–19 back, packed LSB first.
            Assert.Equal(Pdu(1, 3, 0x08, 0x05, 0x03), ModbusTcpServer.Answer(image, Pdu(1, 0, 0, 0, 20)));
            // 02: discrete inputs.
            Assert.Equal(Pdu(2, 1, 0x85), ModbusTcpServer.Answer(image, Pdu(2, 0, 0, 0, 8)));
            // 06 and 10: holding registers; 03 reads them big-endian.
            Assert.Equal(Pdu(6, 0, 1, 0xAB, 0xCD), ModbusTcpServer.Answer(image, Pdu(6, 0, 1, 0xAB, 0xCD)));
            Assert.Equal(Pdu(16, 0, 4, 0, 2), ModbusTcpServer.Answer(image, Pdu(16, 0, 4, 0, 2, 4, 0, 7, 0x01, 0x00)));
            Assert.Equal(Pdu(3, 6, 0xAB, 0xCD, 0, 0, 0, 0), ModbusTcpServer.Answer(image, Pdu(3, 0, 1, 0, 3)));
            Assert.Equal(new ushort[] { 7, 256 }, new[] { image.HoldingRegisters[4], image.HoldingRegisters[5] });
            // 04: input registers.
            Assert.Equal(Pdu(4, 2, 0x12, 0x34), ModbusTcpServer.Answer(image, Pdu(4, 0, 1, 0, 1)));
            // 17: write register 0 = 9, then read 0–1.
            Assert.Equal(Pdu(23, 4, 0, 9, 0xAB, 0xCD), ModbusTcpServer.Answer(image, Pdu(23, 0, 0, 0, 2, 0, 0, 0, 1, 2, 0, 9)));

            // Exceptions: unknown function, past the table, a bad coil value, too many registers.
            Assert.Equal(Pdu(0x88, 1), ModbusTcpServer.Answer(image, Pdu(8, 0, 0, 0, 0)));
            Assert.Equal(Pdu(0x83, 2), ModbusTcpServer.Answer(image, Pdu(3, 0, 9, 0, 2)));
            Assert.Equal(Pdu(0x85, 3), ModbusTcpServer.Answer(image, Pdu(5, 0, 0, 0x12, 0x34)));
            Assert.Equal(Pdu(0x84, 3), ModbusTcpServer.Answer(image, Pdu(4, 0, 0, 0, 126)));
            Assert.Equal(Pdu(0x81, 0x0B), ModbusTcpServer.Answer(null, Pdu(1, 0, 0, 0, 1)));
        }

        /// <summary>The module in a world: a coil written over TCP drives DO1, a cable takes it to DI1, the client reads it back.</summary>
        [Fact]
        public async Task ARemoteIoModuleDrivesAndReadsItsPinsThroughTheCircuit()
        {
            var world = new WorldDocument();
            var io = world.AddComponent(PartLibrary.Default, "modbus-io-8di8do", new WorldPose());
            io.Parameters["port"] = "0";   // any free port
            world.Connect(new CableEnd { ObjectId = io.Id, Pin = "DO1" }, new CableEnd { ObjectId = io.Id, Pin = "DI3" });
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            var device = Assert.Single(runner.Physics!.ModbusDevices);
            Assert.Equal((1, "127.0.0.1"), ((int)device.Unit, device.Bind));
            using var host = new ModbusHost(runner.Physics.ModbusDevices);
            Assert.Empty(host.Problems);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, host.Servers[0].Port);
            var stream = client.GetStream();
            ushort transaction = 0;

            async Task<byte[]> Ask(params int[] pdu)
            {
                transaction++;
                var frame = new byte[7 + pdu.Length];
                frame[0] = (byte)(transaction >> 8);
                frame[1] = (byte)transaction;
                frame[5] = (byte)(pdu.Length + 1);
                frame[6] = 1;
                for (int i = 0; i < pdu.Length; i++) frame[7 + i] = (byte)pdu[i];
                await stream.WriteAsync(frame, 0, frame.Length);
                var header = new byte[7];
                for (int got = 0; got < 7;) got += await stream.ReadAsync(header, got, 7 - got);
                var reply = new byte[(header[4] << 8 | header[5]) - 1];
                for (int got = 0; got < reply.Length;) got += await stream.ReadAsync(reply, got, reply.Length - got);
                return reply;
            }

            void Run(double seconds)
            {
                double until = runner.Time + seconds;
                while (runner.Time < until - 1e-6) runner.Step(0.01);
            }

            Run(0.05);
            Assert.Equal(Pdu(2, 1, 0), await Ask(2, 0, 0, 0, 8));   // nothing in yet
            await Ask(5, 0, 0, 0xFF, 0);   // DO1 on
            Run(0.05);
            Assert.Equal(Pdu(2, 1, 0b100), await Ask(2, 0, 0, 0, 8));   // DI3 sees it through the cable
            Assert.Equal(Pdu(4, 2, 0, 0b100), await Ask(4, 0, 0, 0, 1));   // the inputs' word
            Assert.Equal(Pdu(3, 2, 0, 1), await Ask(3, 0, 0, 0, 1));   // the outputs' word
            await Ask(6, 0, 0, 0, 0);   // all outputs off through the word
            Run(0.05);
            Assert.Equal(Pdu(1, 1, 0), await Ask(1, 0, 0, 0, 8));
            Assert.Equal(Pdu(2, 1, 0), await Ask(2, 0, 0, 0, 8));
        }

        [Fact]
        public async Task AClientOverTcpReadsAndWritesTheDevice()
        {
            var image = new ModbusImage();
            image.InputRegisters[0] = 4242;
            using var server = new ModbusTcpServer(IPAddress.Loopback, 0);
            server.Units[1] = image;
            server.Start();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, server.Port);
            var stream = client.GetStream();

            async Task<byte[]> Ask(ushort transaction, byte unit, params int[] pdu)
            {
                var frame = new byte[7 + pdu.Length];
                frame[0] = (byte)(transaction >> 8);
                frame[1] = (byte)transaction;
                frame[5] = (byte)(pdu.Length + 1);
                frame[6] = unit;
                for (int i = 0; i < pdu.Length; i++) frame[7 + i] = (byte)pdu[i];
                await stream.WriteAsync(frame, 0, frame.Length);
                var header = new byte[7];
                for (int got = 0; got < 7;) got += await stream.ReadAsync(header, got, 7 - got);
                Assert.Equal((transaction, unit), ((ushort)(header[0] << 8 | header[1]), header[6]));
                var reply = new byte[(header[4] << 8 | header[5]) - 1];
                for (int got = 0; got < reply.Length;) got += await stream.ReadAsync(reply, got, reply.Length - got);
                return reply;
            }

            Assert.Equal(Pdu(4, 2, 4242 >> 8, 4242 & 0xFF), await Ask(1, 1, 4, 0, 0, 0, 1));
            Assert.Equal(Pdu(5, 0, 2, 0xFF, 0), await Ask(2, 1, 5, 0, 2, 0xFF, 0));
            Assert.True(image.Coils[2]);
            Assert.Equal(Pdu(1, 1, 4), await Ask(3, 255, 1, 0, 0, 0, 8));   // unit 255: the one device
            Assert.Equal(1, server.Clients);
            Assert.Equal(3, image.Requests);
        }
    }
}
