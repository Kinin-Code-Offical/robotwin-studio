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
            internal ModbusDevice(WorldObject o, byte unit, int port, string bind)
            {
                Object = o;
                Unit = unit;
                Port = port;
                Bind = bind;
            }

            public WorldObject Object { get; }

            public byte Unit { get; }

            public int Port { get; }

            public string Bind { get; }

            public ModbusImage Image { get; } = new ModbusImage(coils: 8, discreteInputs: 8, holdingRegisters: 8, inputRegisters: 8);

            internal readonly DigitalPin?[] Inputs = new DigitalPin?[8];
            internal readonly DigitalPin?[] Outputs = new DigitalPin?[8];
            internal ushort LastOutputs;
            internal (MachineFaults.Fault Fault, double At, double Value)? Fault;   // [fault]
            internal bool FaultSaid;
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

            _modbus.Add(device);
        }

        private void StepModbus(ModbusDevice d, AnalogSimulator? sim)
        {
            var image = d.Image;
            bool offline = FaultNow(d.Object, d.Fault, ref d.FaultSaid) == "network_down";
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
            }
        }
    }
}
