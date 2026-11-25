using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Industrial;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [modbus] A run's remote I/O modules on the network (CoreSim/Industrial): their Modbus TCP servers open when the world
    /// starts and close when it stops, so a PLC or SCADA program on this computer (or the network, when a module listens on
    /// 0.0.0.0) reads and drives them as real modules.
    /// </summary>
    public sealed partial class WorldSession
    {
        private ModbusHost? _modbus;

        /// <summary>The open Modbus servers of this run, when it has modules.</summary>
        public ModbusHost? Modbus => _modbus;

        private void StartModbus()
        {
            StopModbus();
            if (Physics == null || Physics.ModbusDevices.Count == 0) return;
            _modbus = new ModbusHost(Physics.ModbusDevices);
            foreach (string where in _modbus.Listening) AddLog(new SimLogEntry(0, "Modbus", "Modbus TCP dinleniyor: " + where, AnalogEventKind.StateChange));
            foreach (string problem in _modbus.Problems) AddLog(new SimLogEntry(0, "Modbus", problem, AnalogEventKind.Warning));
        }

        private void StopModbus()
        {
            _modbus?.Dispose();
            _modbus = null;
        }
    }
}
