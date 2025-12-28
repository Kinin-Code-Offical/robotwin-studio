using UnityEngine;
using RobotTwin.Core;
using System;

namespace RobotTwin.CoreSim.Runtime
{
    public abstract class CircuitComponent : MonoBehaviour
    {
        [Header("Circuit Connections")]
        [Tooltip("Net ID for each pin. 0 is Ground.")]
        public int[] PinNets; 

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
        // resolver: Function to convert NetID -> Native NodeID
        public abstract void OnBuildCircuit(Func<int, int> resolver);

        // Called after step to update visuals (if needed)
        public virtual void OnSimulationStep() { }

        // Helper to connect pins
        protected void ConnectPins(Func<int, int> resolver)
        {
            if (NativeId == -1) return;
            for (int i = 0; i < PinNets.Length; i++)
            {
                int nodeId = resolver(PinNets[i]);
                NativeBridge.Native_Connect(NativeId, i, nodeId);
            }
        }
    }
}
