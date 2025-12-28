using UnityEngine;
using RobotTwin.Core;
using System;

namespace RobotTwin.CoreSim.Runtime.Components
{
    public class Resistor : CircuitComponent
    {
        public float Resistance = 1000.0f;

        private void Reset()
        {
            PinNets = new string[2]; // 2 Pins
            PinNets[0] = "NetA";
            PinNets[1] = "NetB";
        }

        public override void OnBuildCircuit(Func<string, int> resolver)
        {
            float[] params_ = new float[] { Resistance };
            NativeId = NativeBridge.Native_AddComponent((int)NativeBridge.ComponentType.Resistor, 1, params_);
            ConnectPins(resolver);
        }
    }
}
