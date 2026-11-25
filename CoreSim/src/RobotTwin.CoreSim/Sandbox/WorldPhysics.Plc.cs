using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[plc] A PLC in the world: its Structured Text program, its scan, its I/O pins.</summary>
    public sealed class MachinePlc
    {
        internal MachinePlc(WorldObject o) => Object = o;

        public WorldObject Object { get; }

        /// <summary>The compiled program, or null when it did not compile (<see cref="Errors"/>).</summary>
        public StProgram? Program { get; internal set; }

        public List<StError> Errors { get; internal set; } = new List<StError>();

        internal readonly DigitalPin?[] InputPins = new DigitalPin?[8];
        internal readonly DigitalPin?[] OutputPins = new DigitalPin?[8];
        internal double CycleMs, Pending;
        internal bool FaultReported;

        /// <summary>RUN (scanning) or STOP (no program, or a fault: the outputs are off).</summary>
        public bool Running => Program != null && Program.Fault == null;

        /// <summary>How long the last scans took on this computer (µs), for the cycle-time readout.</summary>
        public double ScanMicros { get; internal set; }
    }

    /// <summary>
    /// [plc] PLCs (Design/CellParts.cs): every cycle time the inputs I0–I7 are read into %IX0.0–%IX0.7 (high above half the
    /// I/O voltage), the program is run once and %QX0.0–%QX0.7 drive Q0–Q7, as a PLC's scan does. A program that does not
    /// compile, or a fault while running (a division by zero, a loop that does not end), puts the PLC in STOP with its
    /// outputs off, and the events say why.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private readonly List<MachinePlc> _plcs = new List<MachinePlc>();

        public IReadOnlyList<MachinePlc> Plcs => _plcs;

        public MachinePlc? PlcOf(string objectId) => _plcs.FirstOrDefault(p => p.Object.Id == objectId);

        private void BuildPlc(WorldObject o, Dictionary<string, string> p)
        {
            var plc = new MachinePlc(o) { CycleMs = Math.Max(1, Math.Min(1000, CellParts.Num(p, "cycle", 10))) };
            // A PLC placed without a program of its own runs the example it comes with.
            string source = p.TryGetValue("program", out var text) && !string.IsNullOrWhiteSpace(text) ? text : CellParts.PlcExample;
            plc.Program = StProgram.Compile(source, out var errors);
            plc.Errors = errors;
            if (plc.Program == null)
                MachineSay(o.Id, "PLC STOP: program derlenmedi: " + string.Join("; ", errors.Select(e => e.ToString())), true);
            var pins = IoPins(o);
            for (int i = 0; i < 8; i++)
            {
                plc.InputPins[i] = pins.GetValueOrDefault(CellParts.PlcInputs[i]);
                plc.OutputPins[i] = pins.GetValueOrDefault(CellParts.PlcOutputs[i]);
            }

            _plcs.Add(plc);
        }

        private void StepPlc(MachinePlc plc, AnalogSimulator? sim, double dt)
        {
            var program = plc.Program;
            if (program == null) return;
            plc.Pending += dt * 1000;
            while (plc.Pending >= plc.CycleMs - 1e-9)
            {
                plc.Pending -= plc.CycleMs;
                for (int i = 0; i < 8; i++) program.Inputs[i] = PinHigh(plc.InputPins[i], sim);
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                bool ok = program.Scan(plc.CycleMs);
                plc.ScanMicros = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
                if (!ok)
                {
                    if (!plc.FaultReported) MachineSay(plc.Object.Id, "PLC STOP: " + program.Fault, true);
                    plc.FaultReported = true;
                    for (int i = 0; i < 8; i++) Drive(plc.OutputPins[i], false);
                    return;
                }
            }

            for (int i = 0; i < 8; i++) Drive(plc.OutputPins[i], program.Outputs[i]);
        }

        /// <summary>Loads a new program into a running PLC (a download): it restarts cold, or stays in STOP with the errors.</summary>
        public List<StError> LoadPlcProgram(string objectId, string source)
        {
            var plc = PlcOf(objectId);
            if (plc == null) return new List<StError>();
            var program = StProgram.Compile(source, out var errors);
            plc.Errors = errors;
            if (program == null) return errors;
            plc.Program = program;
            plc.FaultReported = false;
            plc.Pending = 0;
            MachineSay(objectId, "PLC programı yüklendi: RUN", false);
            return errors;
        }

        /// <summary>Clears a PLC's fault and restarts it cold (STOP → RUN).</summary>
        public void RestartPlc(string objectId)
        {
            var plc = PlcOf(objectId);
            if (plc?.Program == null) return;
            plc.Program.Restart();
            plc.FaultReported = false;
            MachineSay(objectId, "PLC yeniden başlatıldı: RUN", false);
        }
    }
}
