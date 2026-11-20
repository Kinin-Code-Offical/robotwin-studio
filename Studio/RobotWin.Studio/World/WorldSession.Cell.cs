using RobotTwin.CoreSim.Analog;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [arm] Robot cell messages (program start/stop, safety stops, faults, vacuum losses,
    /// pallet changes) into the world log. WorldSession.cs only calls <see cref="DrainCellMessages"/>.
    /// </summary>
    public sealed partial class WorldSession
    {
        private int _cellLogged;

        private void DrainCellMessages()
        {
            if (Physics == null) return;
            if (Log.Count == 0) _cellLogged = 0;
            foreach (var (time, objectId, text, fault) in Physics.DrainMachineMessages())
            {
                // Repeating notices (pallet changes, curtain clear) are capped; faults always show.
                if (!fault && _cellLogged++ > 400) continue;
                var o = World?.Find(objectId);
                Log.Add(new SimLogEntry(time, o?.Name ?? objectId, text, fault ? AnalogEventKind.Warning : AnalogEventKind.StateChange));
            }
        }
    }
}
