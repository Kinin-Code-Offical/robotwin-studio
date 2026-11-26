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
        private RobotTwin.CoreSim.Industrial.OpcUa.OpcUaHost? _opcua;

        /// <summary>The open Modbus servers of this run, when it has modules.</summary>
        public ModbusHost? Modbus => _modbus;

        /// <summary>[opcua] The open OPC UA servers of this run, when a PLC serves its variables.</summary>
        public RobotTwin.CoreSim.Industrial.OpcUa.OpcUaHost? OpcUa => _opcua;

        private void StartModbus()
        {
            StopModbus();
            if (Physics != null && Physics.OpcUaImages.Count > 0)
            {
                _opcua = new RobotTwin.CoreSim.Industrial.OpcUa.OpcUaHost(Physics.OpcUaImages);
                foreach (string where in _opcua.Listening) AddLog(new SimLogEntry(0, "OPC UA", "OPC UA dinleniyor: " + where, AnalogEventKind.StateChange));
                foreach (string problem in _opcua.Problems) AddLog(new SimLogEntry(0, "OPC UA", problem, AnalogEventKind.Warning));
            }

            if (Physics == null || Physics.ModbusDevices.Count == 0) return;
            _modbus = new ModbusHost(Physics.ModbusDevices);
            foreach (string where in _modbus.Listening) AddLog(new SimLogEntry(0, "Modbus", "Modbus TCP dinleniyor: " + where, AnalogEventKind.StateChange));
            foreach (string problem in _modbus.Problems) AddLog(new SimLogEntry(0, "Modbus", problem, AnalogEventKind.Warning));
        }

        /// <summary>[opcua-sec] What the OPC UA servers said (a refused certificate or user) into the run's log, on the UI thread.</summary>
        private void NetworkNotices()
        {
            if (_opcua == null) return;
            foreach (string notice in _opcua.TakeNotices()) Log.Add(new SimLogEntry(0, "OPC UA", notice, AnalogEventKind.Warning));
        }

        private void StopModbus()
        {
            _opcua?.Dispose();
            _opcua = null;
            _modbus?.Dispose();
            _modbus = null;
        }
    }
}
