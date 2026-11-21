using System;
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
    /// <summary>[usbc] Lithium packs with a built-in USB-C charger and protection board.</summary>
    public class UsbcBatteryTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public UsbcBatteryTests(ITestOutputHelper output) => _out = output;

        private static (CompiledCircuit Compiled, AnalogSimulator Sim, Battery Cell, SchematicPart Bat) Circuit(string preset, double soc, bool usb, double? load)
        {
            var s = new Schematic { Title = "usbc" };
            var bat = s.AddPart(Lib, preset, new GridPoint(0, 0));
            bat.Parameters["soc"] = soc.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bat.Parameters["usb"] = usb ? "true" : "false";
            VehicleTemplates.Tie(s, Lib, bat, "+", "VB");
            VehicleTemplates.Tie(s, Lib, bat, "-", "GND");
            if (load is double ohms)
            {
                var r = s.AddPart(Lib, "res-100", new GridPoint(20, 0));
                r.Parameters["resistance"] = ohms.ToString(System.Globalization.CultureInfo.InvariantCulture);
                r.Parameters["power"] = "50";
                VehicleTemplates.Tie(s, Lib, r, "A", "VB");
                VehicleTemplates.Tie(s, Lib, r, "B", "GND");
            }

            var compiled = CircuitCompiler.Compile(s, Lib);
            var sim = new AnalogSimulator(compiled.Circuit) { MaxSubstepsPerCall = 20_000 };
            sim.Initialize();
            return (compiled, sim, compiled.Device<Battery>(bat.RefDes)!, bat);
        }

        private static void Run(AnalogSimulator sim, double seconds, double dt = 0.05)
        {
            for (int i = 0; i < (int)Math.Round(seconds / dt); i++) sim.Step(dt);
        }

        [Fact]
        public void A18650ChargesAtConstantCurrentThenHoldsFourPointTwoVolts()
        {
            var low = Circuit("bat-18650-usbc", 0.3, true, null);
            Run(low.Sim, 30);
            _out.WriteLine($"30 % pack: {low.Cell.Current:F3} A, terminal {low.Sim.NodeVoltage("VB"):F3} V");
            Assert.InRange(-low.Cell.Current, 0.95, 1.02);   // constant current, set by the charger (1 A)
            double soc0 = low.Cell.StateOfCharge;
            Run(low.Sim, 60);
            Assert.InRange(low.Cell.StateOfCharge - soc0, 0.005, 0.008);   // 1 A for 60 s into 2.6 Ah = +0.64 %

            var full = Circuit("bat-18650-usbc", 0.995, true, null);
            Run(full.Sim, 30);
            _out.WriteLine($"99.5 % pack: {full.Cell.Current:F3} A, terminal {full.Sim.NodeVoltage("VB"):F3} V");
            Assert.True(-full.Cell.Current < 0.95, "constant-voltage phase: the current tapers");
            Assert.InRange(full.Compiled.Circuit.Find<LinearRegulator>(full.Bat.RefDes + ".CHG")!.Vout, 4.15, 4.22);
        }

        [Fact]
        public void TheProtectionBoardCutsAFlatPackAndUsbCRevivesIt()
        {
            var flat = Circuit("bat-18650-usbc", 0.004, false, 4.0);   // a few tens of seconds from empty
            var bms = flat.Compiled.Circuit.Find<LithiumProtection>(flat.Bat.RefDes + ".BMS")!;
            Run(flat.Sim, 40);
            _out.WriteLine($"flat pack on 4 Ω: cell {bms.CellVoltage:F2} V, {bms.Tripped}");
            Assert.StartsWith("aşırı deşarj", bms.Tripped);
            Assert.True(Math.Abs(flat.Sim.NodeVoltage("VB") - flat.Sim.NodeVoltage("GND")) < 0.6, "the pack's output is cut");

            // Same flat cell with the USB-C cable in: the charger pushes current in through the FET's body diode.
            var charging = Circuit("bat-18650-usbc", 0.002, true, null);
            var bms2 = charging.Compiled.Circuit.Find<LithiumProtection>(charging.Bat.RefDes + ".BMS")!;
            Run(charging.Sim, 120);
            _out.WriteLine($"charging the flat pack: cell {bms2.CellVoltage:F2} V, {charging.Cell.Current:F2} A, cut: '{bms2.Tripped}'");
            Assert.True(bms2.Tripped.Length == 0, "the charger woke the protection chip and the FET reconnected");
            Assert.True(-charging.Cell.Current > 0.5);
        }

        [Fact]
        public void AUsbCAaCellHoldsOnePointFiveVoltsUntilEmpty()
        {
            var half = Circuit("bat-aa-usbc", 0.5, false, 5.0);
            var low = Circuit("bat-aa-usbc", 0.05, false, 5.0);
            Run(half.Sim, 2);
            Run(low.Sim, 2);
            double vHalf = half.Sim.NodeVoltage("VB"), vLow = low.Sim.NodeVoltage("VB");
            _out.WriteLine($"USB-C AA on 5 Ω: {vHalf:F3} V at 50 %, {vLow:F3} V at 5 %");
            Assert.InRange(vHalf, 1.45, 1.52);
            Assert.InRange(vLow, 1.45, 1.52);   // an alkaline AA would be near 1.0 V here
            var alkaline = Circuit("bat-aa", 0.05, false, 5.0);
            Run(alkaline.Sim, 2);
            Assert.True(alkaline.Sim.NodeVoltage("VB") < 1.3);
        }

        [Fact]
        public void AWorldUsbCableChargesAPackAndUnpluggingStopsIt()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-2s-usbc", new WorldPose());
            bat.Parameters["soc"] = "0.4";
            var pc = w.AddComponent(Lib, "usb-pc", new WorldPose { X = 0.3 });
            pc.Name = "Şarj adaptörü";
            pc.Parameters["target"] = bat.Id;
            var circuit = WorldCompiler.Compile(w, Lib);
            var sim = new AnalogSimulator(circuit.Compiled.Circuit) { MaxSubstepsPerCall = 20_000 };
            sim.Initialize();
            var cell = circuit.Compiled.DevicesOf(circuit.ComponentRef(bat.Id)!).OfType<Battery>().Single();
            Run(sim, 20);
            _out.WriteLine($"2S pack plugged: {cell.Current:F3} A");
            Assert.InRange(-cell.Current, 0.9, 1.02);
            Assert.True(WorldUsb.SetPlugged(circuit, bat.Id, false));
            Run(sim, 5);
            Assert.True(Math.Abs(cell.Current) < 0.01, "unplugged: no charge current");
        }
    }
}
