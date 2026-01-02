using UnityEngine;
using RobotTwin.Core;
using System;

namespace RobotTwin.CoreSim.Runtime
{
    public abstract class CircuitComponent : MonoBehaviour
    {
        [Header("Circuit Connections")]
        [Tooltip("Net ID for each pin. e.g. 'GND', 'VCC', 'Net1'.")]
        public string[] PinNets; 

        protected int NativeId = -1;

        protected virtual void Start()
        {
            CircuitManager.Instance?.RegisterComponent(this);
        }

        protected virtual void OnDestroy()
        {
            CircuitManager.Instance?.UnregisterComponent(this);
        }

        // Called by Manager when rebuilding circuit
        // resolver: Function to convert NetID (String) -> Native NodeID (Int)
        public abstract void OnBuildCircuit(Func<string, int> resolver);

        // Called after step to update visuals (if needed)
        public virtual void OnSimulationStep() { }

        // Helper to connect pins
        protected void ConnectPins(Func<string, int> resolver)
        {
            if (NativeId == -1) return;
            if (PinNets == null) return;
            
            for (int i = 0; i < PinNets.Length; i++)
            {
                if (string.IsNullOrEmpty(PinNets[i])) continue;
                int nodeId = resolver(PinNets[i]);
                NativeBridge.Native_Connect(NativeId, i, nodeId);
            }
        }
    }
}
