using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [supply] The run's power report (CoreSim Sandbox/SupplyMonitor.cs): each rated part's supply pin read after every
    /// circuit step; a brown-out or an over-voltage goes to the events log when it happens, and when the run stops the
    /// log gets the power-up order, each part's brown-outs and the parts that never got enough.
    /// </summary>
    public sealed partial class WorldSession
    {
        public SupplyMonitor? Supply { get; private set; }

        private void StartSupply()
        {
            Supply = null;
            if (World == null || Circuit == null) return;
            var monitor = SupplyMonitor.Build(World, Circuit);
            if (monitor.Watches.Count == 0) return;
            monitor.Occurred += e =>
            {
                if (e.Kind != SupplyMonitor.EventKind.Ready)
                    AddLog(new SimLogEntry(e.Time, e.Watch.Name, SupplyMonitor.Describe(e), e.Kind == SupplyMonitor.EventKind.Recovered ? AnalogEventKind.StateChange : AnalogEventKind.Warning));
            };
            Supply = monitor;
        }

        private void SampleSupply()
        {
            if (Supply != null && Simulator != null) Supply.Sample(Simulator.Time, Simulator);
        }

        private void StopSupply()
        {
            var monitor = Supply;
            if (monitor == null) return;
            monitor.Finish(Time);
            var lines = monitor.Report();
            if (lines.Count == 0) return;
            bool trouble = monitor.Watches.Any(w => !w.Ready || w.BrownOuts > 0 || w.MaxSeen > w.Rating.Max);
            AddLog(new SimLogEntry(Time, "besleme", "Besleme raporu:\n" + string.Join("\n", lines), trouble ? AnalogEventKind.Warning : AnalogEventKind.StateChange));
        }
    }
}
