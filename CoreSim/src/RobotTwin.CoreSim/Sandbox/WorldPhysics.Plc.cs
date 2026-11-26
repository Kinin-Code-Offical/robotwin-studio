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
        public WorldPhysics.ModbusDevice? Modbus { get; internal set; }   // [modbus] when it serves its memory

        /// <summary>[opcua] Its variables as an OPC UA server shows them, when it serves them.</summary>
        public Industrial.OpcUa.UaTagImage? OpcUa { get; internal set; }
        internal bool FaultReported;

        /// <summary>RUN (scanning) or STOP (no program, or a fault: the outputs are off).</summary>
        public bool Running => Program != null && Program.Fault == null;

        /// <summary>How long the last scans took on this computer (µs), for the cycle-time readout.</summary>
        public double ScanMicros { get; internal set; }

        /// <summary>
        /// [cycle] The station's steps, timed: the variable a CASE runs on (<c>step_var</c>, "Adim" by default), how long the
        /// program has spent in each of its values, and its cycles (back to the first step). The longest step is the bottleneck.
        /// </summary>
        public string StepVariable { get; internal set; } = string.Empty;

        public SortedDictionary<double, (double Seconds, int Visits)> Steps { get; } = new SortedDictionary<double, (double, int)>();

        public List<double> Cycles { get; } = new List<double>();

        public double CurrentStep { get; internal set; } = double.NaN;

        /// <summary>Since when (simulated s) it is in the current step, and since when in the current cycle.</summary>
        public double StepSince { get; internal set; }

        internal double FirstStep = double.NaN, CycleSince = double.NaN;
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

        /// <summary>[opcua] The PLCs that serve their variables over OPC UA (Industrial/OpcUa/OpcUaHost opens their servers).</summary>
        public IReadOnlyList<Industrial.OpcUa.UaTagImage> OpcUaImages => _plcs.Where(p => p.OpcUa != null).Select(p => p.OpcUa!).ToList();

        private void BuildPlc(WorldObject o, Dictionary<string, string> p)
        {
            var plc = new MachinePlc(o) { CycleMs = Math.Max(1, Math.Min(1000, CellParts.Num(p, "cycle", 10))) };
            plc.StepVariable = p.TryGetValue("step_var", out var sv) && !string.IsNullOrWhiteSpace(sv) ? sv.Trim() : "Adim";
            // A PLC placed without a program of its own runs the example it comes with; a ladder one runs its rungs.
            List<StError> errors;
            if (p.TryGetValue("language", out var language) && language.Trim() == CellParts.PlcLadder)
            {
                plc.Program = LdProgram.Load(p.TryGetValue("ladder", out var ladder) ? ladder : string.Empty).Compile(out errors);
            }
            else
            {
                string source = p.TryGetValue("program", out var text) && !string.IsNullOrWhiteSpace(text) ? text : CellParts.PlcExample;
                plc.Program = StProgram.Compile(source, out errors);
            }

            plc.Errors = errors;
            if (plc.Program == null)
                MachineSay(o.Id, "PLC STOP: program derlenmedi: " + string.Join("; ", errors.Select(e => e.ToString())), true);
            var pins = IoPins(o);
            for (int i = 0; i < 8; i++)
            {
                plc.InputPins[i] = pins.GetValueOrDefault(CellParts.PlcInputs[i]);
                plc.OutputPins[i] = pins.GetValueOrDefault(CellParts.PlcOutputs[i]);
            }

            // [modbus] A PLC can serve its memory, inputs and outputs to a SCADA or HMI over Modbus TCP.
            if (p.TryGetValue("modbus", out var serve) && serve.Trim() == "açık")
            {
                int port = (int)CellParts.Num(p, "modbus_port", 5021);
                string bind = p.TryGetValue("bind", out var b) && !string.IsNullOrWhiteSpace(b) ? b.Trim() : "127.0.0.1";
                var device = new ModbusDevice(o, 1, Math.Max(0, Math.Min(65535, port)), bind, new Industrial.ModbusImage(64, 64, 256, 0)) { Plc = plc };
                device.Fault = MachineFaults.Of(o);
                plc.Modbus = device;
                _modbus.Add(device);
            }

            // [opcua] … and its variables over OPC UA, browsable by name.
            if (p.TryGetValue("opcua", out var ua) && ua.Trim() == "açık")
            {
                int port = (int)CellParts.Num(p, "opcua_port", 4840);
                string bind = p.TryGetValue("bind", out var b) && !string.IsNullOrWhiteSpace(b) ? b.Trim() : "127.0.0.1";
                string name = string.IsNullOrWhiteSpace(o.Name) ? o.Id : o.Name.Trim();
                plc.OpcUa = new Industrial.OpcUa.UaTagImage(name, bind, Math.Max(0, Math.Min(65535, port)));
                if (plc.Program != null) PublishOpcUa(plc, plc.Program);
            }

            _plcs.Add(plc);
        }

        /// <summary>[opcua] After a scan the server sees the program's variables; a client's writes go in before the next.</summary>
        private static void PublishOpcUa(MachinePlc plc, StProgram program) =>
            plc.OpcUa?.Publish(program.Tags.Select(t => new Industrial.OpcUa.UaTagImage.Tag(t.Name, t.Type, t.TypeName, t.Writable, t.Value)), plc.Running);

        /// <summary>[modbus] The PLC's Modbus table and its memory meet at the scan: a client's register writes go in before it, the results out after.</summary>
        private static void SyncPlcModbus(MachinePlc plc, StProgram program, bool before)
        {
            var d = plc.Modbus;
            if (d == null) return;
            var image = d.Image;
            lock (image.Gate)
            {
                if (before)
                {
                    for (int i = 0; i < 256; i++)
                        if (image.HoldingRegisters[i] != d.LastMemory[i]) program.Memory[i] = unchecked((short)image.HoldingRegisters[i]);
                    return;
                }

                for (int i = 0; i < 256; i++) image.HoldingRegisters[i] = d.LastMemory[i] = unchecked((ushort)program.Memory[i]);
                for (int i = 0; i < 64; i++)
                {
                    image.DiscreteInputs[i] = program.Inputs[i];
                    image.Coils[i] = program.Outputs[i];
                }
            }
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
                // [mbclient] its MB_READ / MB_WRITE blocks reach the world's other Modbus devices by unit id
                program.ModbusNetwork ??= unit => _modbus.FirstOrDefault(d => d.Unit == unit && d.Plc != plc)?.Image;
                SyncPlcModbus(plc, program, true);
                if (plc.OpcUa != null)
                    foreach (var (name, value) in plc.OpcUa.TakeWrites()) program.Write(name, value);
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                bool ok = program.Scan(plc.CycleMs);
                SyncPlcModbus(plc, program, false);
                PublishOpcUa(plc, program);
                TimeSteps(plc, program, Scene.Time);
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

        /// <summary>[cycle] After a scan: a change of the step variable closes the step it left (and a cycle on the first step).</summary>
        private static void TimeSteps(MachinePlc plc, StProgram program, double now)
        {
            double step = program.Read(plc.StepVariable);
            if (double.IsNaN(step)) return;
            if (double.IsNaN(plc.CurrentStep))
            {
                plc.CurrentStep = plc.FirstStep = step;
                plc.StepSince = plc.CycleSince = now;
                return;
            }

            if (step == plc.CurrentStep) return;
            plc.Steps.TryGetValue(plc.CurrentStep, out var s);
            plc.Steps[plc.CurrentStep] = (s.Seconds + (now - plc.StepSince), s.Visits + 1);
            if (step == plc.FirstStep)
            {
                plc.Cycles.Add(now - plc.CycleSince);
                plc.CycleSince = now;
            }

            plc.CurrentStep = step;
            plc.StepSince = now;
        }

        /// <summary>Loads a new program into a running PLC (a download): it restarts cold, or stays in STOP with the errors.</summary>
        public List<StError> LoadPlcProgram(string objectId, string source) => LoadPlcProgram(objectId, StProgram.Compile(source, out var errors), errors);

        /// <summary>Loads a ladder diagram into a running PLC, as <see cref="LoadPlcProgram(string, string)"/>.</summary>
        public List<StError> LoadPlcLadder(string objectId, LdProgram ladder) => LoadPlcProgram(objectId, ladder.Compile(out var errors), errors);

        private List<StError> LoadPlcProgram(string objectId, StProgram? program, List<StError> errors)
        {
            var plc = PlcOf(objectId);
            if (plc == null) return new List<StError>();
            plc.Errors = errors;
            if (program == null) return errors;
            plc.Program = program;
            plc.FaultReported = false;
            plc.Pending = 0;
            plc.OpcUa?.TakeWrites();   // [opcua] writes meant for the old program are not given to the new one
            PublishOpcUa(plc, program);
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
