using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Industrial;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [modbus] Remote I/O modules (<see cref="CellParts.ModbusIoId"/>): their Modbus data table follows their pins every
    /// step. Coils 0–7 drive DO1–DO8, DI1–DI8 set discrete inputs 0–7; input register 0 is the inputs as a bit word and
    /// holding register 0 the outputs (writing it sets the coils). The network side (a <see cref="ModbusTcpServer"/>) is the
    /// host's: it serves <see cref="ModbusDevices"/>.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        /// <summary>A remote I/O module: its object, Modbus address (unit, port, listening address) and data table.</summary>
        public sealed class ModbusDevice
        {
            internal ModbusDevice(WorldObject o, byte unit, int port, string bind, ModbusImage? image = null)
            {
                Image = image ?? new ModbusImage(coils: 8, discreteInputs: 8, holdingRegisters: 8, inputRegisters: 8);
                Object = o;
                Unit = unit;
                Port = port;
                Bind = bind;
            }

            public WorldObject Object { get; }

            public byte Unit { get; }

            public int Port { get; }

            public string Bind { get; }

            public ModbusImage Image { get; }

            internal readonly DigitalPin?[] Inputs = new DigitalPin?[8];
            internal readonly DigitalPin?[] Outputs = new DigitalPin?[8];
            internal readonly DigitalPin?[] AnalogInputs = new DigitalPin?[4];   // [modbus] read as voltmeters
            internal readonly ControlledSource?[] AnalogOutputs = new ControlledSource?[2];
            internal ushort LastOutputs;

            /// <summary>[plc] A PLC serving its memory: holding registers are %MW0–%MW255, discrete inputs %IX, coils %QX.</summary>
            public MachinePlc? Plc { get; internal set; }

            internal readonly ushort[] LastMemory = new ushort[256];
            internal (MachineFaults.Fault Fault, double At, double Value)? Fault;   // [fault]
            internal bool FaultSaid;

            /// <summary>[rtu] On a serial line (Modbus RTU) instead of the network: its slave there; null on TCP.</summary>
            public ModbusRtuSlave? Rtu { get; internal set; }
        }

        private readonly List<ModbusDevice> _modbus = new List<ModbusDevice>();

        public IReadOnlyList<ModbusDevice> ModbusDevices => _modbus;

        private void BuildModbus(WorldObject o, IReadOnlyDictionary<string, string> p)
        {
            int unit = (int)CellParts.Num(p, "unit", 1), port = (int)CellParts.Num(p, "port", 5020);
            string bind = p.TryGetValue("bind", out var b) && !string.IsNullOrWhiteSpace(b) ? b.Trim() : "127.0.0.1";
            var device = new ModbusDevice(o, (byte)Math.Max(0, Math.Min(255, unit)), Math.Max(0, Math.Min(65535, port)), bind);
            device.Fault = MachineFaults.Of(o);
            var pins = IoPins(o);
            for (int i = 0; i < 8; i++)
            {
                device.Inputs[i] = pins.GetValueOrDefault(CellParts.ModbusInputs[i]);
                device.Outputs[i] = pins.GetValueOrDefault(CellParts.ModbusOutputs[i]);
            }

            for (int i = 0; i < CellParts.ModbusAnalogInputs.Length; i++) device.AnalogInputs[i] = pins.GetValueOrDefault(CellParts.ModbusAnalogInputs[i]);
            if (_circuit?.ComponentRef(o.Id) is { } refDes)
            {
                foreach (var dev in _circuit.Compiled.DevicesOf(refDes))
                {
                    if (dev is not ControlledSource cs) continue;
                    int k = Array.IndexOf(CellParts.ModbusAnalogOutputs, cs.Id.Substring(refDes.Length + 1));
                    if (k >= 0) device.AnalogOutputs[k] = cs;
                }
            }

            if (p.TryGetValue("bus", out var bus) && string.Equals(bus?.Trim(), "rtu", StringComparison.OrdinalIgnoreCase))
            {
                // [rtu] the same table on the serial header
                var line = new UartLine(pins.GetValueOrDefault("RX"), pins.GetValueOrDefault("TX"), CellParts.Num(p, "baud", 9600),
                    SerialFormat.Parse(p.TryGetValue("format", out var f) ? f : "8E1"));
                device.Rtu = new ModbusRtuSlave(line, device.Unit, device.Image);
            }

            _modbus.Add(device);
        }

        private void StepModbus(ModbusDevice d, AnalogSimulator? sim)
        {
            var image = d.Image;
            if (d.Rtu != null && sim != null) d.Rtu.Step(sim.Time);   // [rtu] frames off the RX wire, answers onto TX
            bool offline = FaultNow(d.Object, d.Fault, ref d.FaultSaid) == "network_down";
            if (d.Plc != null)
            {
                lock (image.Gate) image.Offline = offline;
                return;   // a PLC's table follows its scan (StepPlc), not pins
            }

            lock (image.Gate)
            {
                image.Offline = offline;
                // A write to holding register 0 since the last step sets the coils from its bits.
                if (image.HoldingRegisters[0] != d.LastOutputs)
                {
                    for (int i = 0; i < 8; i++) image.Coils[i] = (image.HoldingRegisters[0] >> i & 1) != 0;
                }

                ushort outputs = 0, inputs = 0;
                for (int i = 0; i < 8; i++)
                {
                    Drive(d.Outputs[i], image.Coils[i]);
                    if (image.Coils[i]) outputs |= (ushort)(1 << i);
                    bool high = PinHigh(d.Inputs[i], sim);
                    image.DiscreteInputs[i] = high;
                    if (high) inputs |= (ushort)(1 << i);
                }

                image.HoldingRegisters[0] = outputs;
                d.LastOutputs = outputs;
                image.InputRegisters[0] = inputs;

                // [modbus] analog, 0–10 V in millivolts through a 12-bit converter each way
                for (int i = 0; i < d.AnalogInputs.Length && 1 + i < image.InputRegisters.Length; i++)
                {
                    double v = d.AnalogInputs[i]?.Voltage ?? 0;
                    int code = (int)Math.Round(Math.Max(0, Math.Min(10, v)) / 10 * 4095);
                    image.InputRegisters[1 + i] = (ushort)Math.Round(code * 10000.0 / 4095);
                }

                for (int i = 0; i < d.AnalogOutputs.Length && 1 + i < image.HoldingRegisters.Length; i++)
                {
                    if (d.AnalogOutputs[i] is not { } ao) continue;
                    int code = (int)Math.Round(Math.Min(10000, (int)image.HoldingRegisters[1 + i]) / 10000.0 * 4095);
                    double level = code * 10.0 / 4095;
                    if (Math.Abs(ao.Level - level) > 1e-9) ao.Level = level;
                }
            }
        }

        /// <summary>[modbus] The module's analog output's voltage now (for the inspector), or NaN.</summary>
        public static double AnalogOutputVolts(ModbusDevice d, int index) => index >= 0 && index < d.AnalogOutputs.Length && d.AnalogOutputs[index] is { } ao ? ao.Level : double.NaN;
    }
}
