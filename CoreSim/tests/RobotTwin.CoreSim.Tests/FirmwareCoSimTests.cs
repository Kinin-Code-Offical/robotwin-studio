using System;
using System.IO;
using System.Text;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Firmware")]
    public class FirmwareCoSimTests
    {
        private static string? FindRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        [Fact]
        public void IntelHexParsesAndValidatesChecksums()
        {
            var blocks = IntelHex.Parse(":06000000259A2D9AFFCFA6\n:00000001FF\n");
            Assert.Single(blocks);
            Assert.Equal(new byte[] { 0x25, 0x9A, 0x2D, 0x9A, 0xFF, 0xCF }, blocks[0].Data);
            Assert.Throws<InvalidDataException>(() => IntelHex.Parse(":06000000259A2D9AFFCF70\n"));
        }

        [Fact]
        public void AluFlagsMultiplyAndSubroutines()
        {
            var mcu = new Atmega328P();
            mcu.LoadProgram(
                0xEC08,  // ldi r16, 200
                0xE614,  // ldi r17, 100
                0x0F01,  // add r16, r17      -> 44, C=1
                0xB72F,  // in  r18, SREG
                0x9F01,  // mul r16, r17      -> 4400
                0xD001,  // rcall sub
                0xCFFF,  // rjmp .
                0xE545,  // sub: ldi r20, 0x55
                0x9508); // ret
            mcu.RunCycles(100);
            Assert.Equal(44, mcu.ReadData(16));
            Assert.Equal(1, mcu.ReadData(18) & 1);
            Assert.Equal(0x30, mcu.ReadData(0));
            Assert.Equal(0x11, mcu.ReadData(1));
            Assert.Equal(0x55, mcu.ReadData(20));
            Assert.Equal(0, mcu.UnknownInstructions);
        }

        [Fact]
        public void IndirectJumpSkipsOverCode()
        {
            var mcu = new Atmega328P();
            mcu.LoadProgram(0xE0E4, 0xE0F0, 0x9409, 0xE051, 0xE062, 0xCFFF);
            mcu.RunCycles(50);
            Assert.Equal(0, mcu.ReadData(21));
            Assert.Equal(2, mcu.ReadData(22));
        }

        [Fact]
        public void Timer0FastPwmOnPin6()
        {
            var mcu = new Atmega328P();
            mcu.LoadProgram(0xE803, 0xBD04, 0xE003, 0xBD05, 0xE400, 0xBD07, 0x9A56, 0xCFFF);
            mcu.RunCycles(100_000);
            var pin = mcu.GetPin(6);
            Assert.True(pin.Driving);
            Assert.True(pin.IsPwm);
            Assert.Equal(65.0 / 256.0, pin.PwmDuty, 6);
            Assert.Equal(976.5625, pin.PwmFrequency, 3);
        }

        [Fact]
        public void RealArduinoSketchBlinksLedAndPrintsOverSerial()
        {
            string? hexPath = FindRepoFile(Path.Combine("workspace", "TestSketch", "TestSketch.ino.hex"));
            Assert.NotNull(hexPath);

            var lib = PartLibrary.Default;
            var sch = new Schematic();
            var uno = sch.AddPart(lib, "arduino-uno", new GridPoint(0, 0));
            var r = sch.AddPart(lib, "res-220", new GridPoint(16, 13));
            var led = sch.AddPart(lib, "led5-red", new GridPoint(24, 13));
            var gnd = sch.AddPart(lib, "gnd", new GridPoint(30, 20));
            sch.AddWire(uno.Transform(7, 13), r.Transform(-3, 0));
            sch.AddWire(r.Transform(3, 0), led.Transform(-3, 0));
            sch.AddWire(led.Transform(3, 0), new GridPoint(30, 13), gnd.Transform(0, -1));
            sch.Labels.Add(new NetLabel { Name = "GND", Position = uno.Transform(-7, -4) });

            var compiled = CircuitCompiler.Compile(sch, lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var mcu = new Atmega328P();
            mcu.LoadHex(File.ReadAllText(hexPath!));
            var cosim = new FirmwareCoSimulation(mcu, compiled, "U1", () => compiled.PinVoltage(sim, "U1", "5V"));
            var serial = new StringBuilder();
            cosim.SerialOutput += s => serial.Append(s);
            var diode = compiled.Device<Led>("D1")!;

            bool sawOn = false, sawOffAfterOn = false;
            const double slice = 100e-6;
            for (int i = 0; i < (int)(2.3 / slice); i++)
            {
                cosim.Advance(slice, sim.Time);
                sim.Step(slice);
                double t = sim.Time;
                if (t > 0.3 && t < 0.9 && diode.Current > 0.008) sawOn = true;
                if (sawOn && t > 1.3 && t < 1.9 && diode.Current < 1e-5) sawOffAfterOn = true;
            }

            Assert.Equal(0, mcu.UnknownInstructions);
            Assert.Contains("Hello from RoboTwin!", serial.ToString());
            Assert.True(sawOn, "LED never lit during the first second");
            Assert.True(sawOffAfterOn, "LED did not turn off during the second second");
        }
    }
}
