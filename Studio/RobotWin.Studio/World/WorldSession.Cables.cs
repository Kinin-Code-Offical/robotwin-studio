using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    // [phys] Cables of a running world are physical ropes in its physics scene.
    public sealed partial class WorldSession
    {
        /// <summary>Plug positions of a cable in its objects' frames (set by the page from the 3D models).</summary>
        public Func<WorldCable, CablePins?>? CablePins { get; set; }

        /// <summary>The cable's resting shape from the editor, used as the rope's starting shape.</summary>
        public Func<string, IReadOnlyList<Vec3>?>? CableSeed { get; set; }

        private void AttachCablePhysics()
        {
            if (Physics == null || World == null || CablePins == null) return;
            try
            {
                Physics.AttachCables(World, CablePins, 120, CableSeed);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                Log.Add(new SimLogEntry(0, "fizik", "Kablolar fizik sahnesine eklenemedi: " + ex.Message, AnalogEventKind.Warning));
            }
        }

        /// <summary>Rope points of a cable while simulating (null when stopped or without physics).</summary>
        public IReadOnlyList<Vec3>? CablePoints(WorldCable cable) =>
            State == SimState.Stopped ? null : Physics?.CablePoints(cable.Id);

        /// <summary>Whether each plug of a cable is still in its pin.</summary>
        public (bool A, bool B) PlugState(WorldCable cable) =>
            State == SimState.Stopped || Physics == null ? (true, true) : Physics.PlugState(cable.Id);
    }
}
