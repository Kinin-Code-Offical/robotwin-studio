using System;
using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.CoreSim.Runtime.Components
{
    public class Diode : CircuitComponent
    {
        public override void OnBuildCircuit(Func<string, int> resolver)
        {
            if (PinNets.Length < 2) return;

            // Resolve Net IDs
            int netAnode = resolver(PinNets[0]);
            int netCathode = resolver(PinNets[1]);

            // Add Component to Native Engine
            // Type 3 = Diode (NativeEngine_Core.cpp)
            NativeId = NativeBridge.Native_AddComponent((int)NativeBridge.ComponentType.Diode, 0, null);

            if (NativeId != -1)
            {
                // Connect
                NativeBridge.Native_Connect(NativeId, 0, netAnode);
                NativeBridge.Native_Connect(NativeId, 1, netCathode);
            }
        }
    }
}
