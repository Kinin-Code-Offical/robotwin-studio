using System;
using System.Collections.Generic;
using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.CoreSim.Runtime
{
    [DefaultExecutionOrder(-100)] // Initialize early
    public class CircuitManager : MonoBehaviour
    {
        public static CircuitManager Instance { get; private set; }

        [Header("Simulation Settings")]
        public float TimeStep = 0.001f; // 1ms
        public bool IsRunning = false;

        private List<CircuitComponent> _components = new List<CircuitComponent>();
        private bool _isInitialized = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            // Re-initialize context if needed
        }

        private void OnDisable()
        {
            StopSimulation();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                StopSimulation();
            }
        }

        public void RegisterComponent(CircuitComponent comp)
        {
            if (!_components.Contains(comp))
            {
                _components.Add(comp);
            }
        }

        public void UnregisterComponent(CircuitComponent comp)
        {
            _components.Remove(comp);
        }

        public void RebuildCircuit()
        {
            // 1. Reset Native Context
            NativeBridge.Native_DestroyContext();
            NativeBridge.Native_CreateContext();

            // 2. Create Nodes & Map them
            // In this simple version, we'll let components request node IDs 
            // or we automagically find connected nodes via a "Wire" system?
            // For MVP: Components define "Net IDs" (integers) publically.
            // A global Net 0 is Ground.
            
            // Map NetID (User) -> NodeID (Native)
            Dictionary<int, int> netToNodeMap = new Dictionary<int, int>();
            netToNodeMap[0] = 0; // Ground is always 0

            int GetNativeNode(int netId)
            {
                if (!netToNodeMap.ContainsKey(netId))
                {
                    netToNodeMap[netId] = NativeBridge.Native_AddNode();
                }
                return netToNodeMap[netId];
            }

            // 3. Add Components
            foreach (var comp in _components)
            {
                comp.OnBuildCircuit(GetNativeNode);
            }

            _isInitialized = true;
            Debug.Log($"[CircuitManager] Circuit Rebuilt. {netToNodeMap.Count} Nodes, {_components.Count} Components.");
        }

        public void RunSimulation()
        {
            if (!_isInitialized) RebuildCircuit();
            IsRunning = true;
        }

        public void StopSimulation()
        {
            IsRunning = false;
            NativeBridge.Native_DestroyContext();
            _isInitialized = false;
        }

        private void FixedUpdate()
        {
            if (IsRunning)
            {
                // Step Physics
                NativeBridge.Native_Step(TimeStep);
                
                // Sync Back
                foreach(var comp in _components)
                {
                    comp.OnSimulationStep();
                }
            }
        }
    }
}
