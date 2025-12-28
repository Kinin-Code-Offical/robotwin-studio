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
            PinNets = new int[2]; // 2 Pins
        }

        public override void OnBuildCircuit(Func<int, int> resolver)
        {
            float[] params_ = new float[] { Resistance };
            NativeId = NativeBridge.Native_AddComponent((int)NativeBridge.ComponentType.Resistor, 1, params_);
            ConnectPins(resolver);
        }
    }
}
