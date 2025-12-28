using UnityEngine;
using RobotTwin.Core;
using System;

namespace RobotTwin.CoreSim.Runtime.Components
{
    public class VoltageSource : CircuitComponent
    {
        public float Voltage = 5.0f;

        private void Reset()
        {
            PinNets = new int[2]; // 2 Pins
        }

        public override void OnBuildCircuit(Func<int, int> resolver)
        {
            float[] params_ = new float[] { Voltage };
            NativeId = NativeBridge.Native_AddComponent((int)NativeBridge.ComponentType.VoltageSource, 1, params_);
            ConnectPins(resolver);
        }
    }
}
