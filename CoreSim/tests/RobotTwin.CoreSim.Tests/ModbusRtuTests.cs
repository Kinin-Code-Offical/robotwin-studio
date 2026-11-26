using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[rtu] Modbus RTU on a wire: the remote I/O module as a slave on its serial header, an Uno running a master sketch.</summary>
    public class ModbusRtuTests
    {
        private readonly ITestOutputHelper _out;

        public ModbusRtuTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void TheCrcIsModbuss()
        {
            // The specification's own example: 01 03 00 00 00 01 → CRC 84 0A on the wire
            ushort crc = ModbusRtuSlave.Crc(new byte[] { 1, 3, 0, 0, 0, 1 }, 6);
            Assert.Equal(0x84, crc & 0xFF);
            Assert.Equal(0x0A, crc >> 8);
            Assert.Equal("8E1", SerialFormat.Parse("8e1").ToString());
            Assert.Equal(11, SerialFormat.Parse("8N2").FrameBits);
            Assert.Equal("8E1", SerialFormat.Parse("?").ToString());   // Modbus RTU's default
        }

        [Fact]
        public void AnUnoMasterTalksToTheModuleOverItsSerialWires()
        {
            var lib = PartLibrary.Default;
            var world = new WorldDocument();
            var uno = world.AddComponent(lib, "arduino-uno", new WorldPose());
            var io = world.AddComponent(lib, "modbus-rtu-io-8di8do", new WorldPose { X = 0.3 });
            io.Parameters["io_v"] = "5";
            void Cable(WorldObject a, string pa, WorldObject b, string pb) => world.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb });
            Cable(uno, "D1", io, "RX");     // the Uno's TX into the module's RX
            Cable(io, "TX", uno, "D0");     // and back
            Cable(uno, "GND1", io, "GND");
            Cable(io, "DO1", io, "DI3");    // coil 0 comes back as discrete input 2

            using var runner = new WorldRunner(world, new RunnerOptions());
            runner.FirmwareOverrides[uno.Id] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "modbus-master.hex"));
            runner.Reset();
            var device = Assert.Single(runner.Physics!.ModbusDevices);
            Assert.NotNull(device.Rtu);
            Assert.Equal("8E1", device.Rtu!.Line.Format.ToString());
            while (runner.Time < 0.6) runner.Step(0.01);

            var image = device.Image;
            ushort[] hr;
            bool[] coils;
            lock (image.Gate)
            {
                hr = (ushort[])image.HoldingRegisters.Clone();
                coils = (bool[])image.Coils.Clone();
            }

            _out.WriteLine($"answered {device.Rtu.Answered}, CRC errors {device.Rtu.CrcErrors}, broken characters {device.Rtu.CharacterErrors}; " +
                           $"tally: {hr[1]} good, {hr[2]} bad, {hr[3]} right silences; register 4 = 0x{hr[4]:X4}, 5 = 0x{hr[5]:X4}; coils {string.Join("", coils.Select(c => c ? '1' : '0'))}");
            Assert.True(coils[0], "the master's first write turned DO1 on");
            Assert.True(coils[1], "the master read DI3 back as set, through the module's answer and its CRC");
            Assert.Equal(4, hr[1]);         // every answer held
            Assert.Equal(0, hr[2]);
            Assert.Equal(3, hr[3]);         // bad CRC, other address and broadcast all went unanswered
            Assert.Equal(0x1234, hr[4]);    // the broadcast was carried out
            Assert.Equal(0x1234, hr[5]);    // and read back
            Assert.Equal(1, device.Rtu.CrcErrors);
            Assert.Equal(0, device.Rtu.CharacterErrors);
        }

        [Fact]
        public void TheModulesAnalogOutputComesBackOnItsAnalogInput()
        {
            // AO1 cabled to AI2: 3.300 V written to holding register 1 reads back on input register 2, through the 12-bit DAC and ADC
            var world = new WorldDocument();
            var io = world.AddComponent(PartLibrary.Default, "modbus-io-8di8do", new WorldPose());
            io.Parameters["port"] = "0";
            world.Connect(new CableEnd { ObjectId = io.Id, Pin = "AO1" }, new CableEnd { ObjectId = io.Id, Pin = "AI2" });
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            var device = Assert.Single(runner.Physics!.ModbusDevices);
            Assert.Equal(new byte[] { 6, 0, 1, 0x0C, 0xE4 }, ModbusTcpServer.Answer(device.Image, new byte[] { 6, 0, 1, 0x0C, 0xE4 }));   // 3300 mV
            while (runner.Time < 0.05) runner.Step(0.01);
            ushort ai1, ai2;
            lock (device.Image.Gate)
            {
                ai1 = device.Image.InputRegisters[1];
                ai2 = device.Image.InputRegisters[2];
            }

            _out.WriteLine($"AO1 {WorldPhysics.AnalogOutputVolts(device, 0):0.0000} V → AI2 {ai2} mV, AI1 (open) {ai1} mV");
            Assert.InRange(ai2, 3290, 3305);   // a step of the converters is 2.44 mV; the cable and the 10 Ω driver into 100 kΩ lose a little
            Assert.Equal(0, ai1);
        }
    }
}
