using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RobotTwin.Core
{
    /// <summary>
    /// Bridge to the High-Performance C++ NativeEngine.
    /// Handles P/Invoke calls for heavy simulation logic.
    /// </summary>
    public static class NativeBridge
    {
        // Name of the DLL (NativeEngine.dll on Windows, libNativeEngine.so on Linux)
        private const string PLUGIN_NAME = "NativeEngine";

        [DllImport(PLUGIN_NAME, EntryPoint = "StepSimulation")]
        private static extern void _StepSimulation(float dt);

        [DllImport(PLUGIN_NAME, EntryPoint = "GetEngineVersion")]
        private static extern int _GetEngineVersion();

        [DllImport(PLUGIN_NAME, EntryPoint = "CalculateCurrent")]
        private static extern float _CalculateCurrent(float voltage, float resistance);

        /// <summary>
        /// Steps the physics/circuit simulation by dt seconds.
        /// </summary>
        public static void StepSimulation(float dt)
        {
            try
            {
                _StepSimulation(dt);
            }
            catch (DllNotFoundException)
            {
                // Fallback for Editor without compiled Plugin
                Debug.LogWarning("[NativeBridge] NativeEngine DLL not found. Simulation stepping skipped.");
            }
        }

        public static int GetVersion()
        {
            try
            {
                return _GetEngineVersion();
            }
            catch (DllNotFoundException)
            {
                return -1;
            }
        }

        public static float CalculateCurrent(float voltage, float resistance)
        {
            try
            {
                return _CalculateCurrent(voltage, resistance);
            }
            catch (DllNotFoundException)
            {
                Debug.LogWarning("[NativeBridge] DLL not found.");
                return 0.0f;
            }
        }

        /* 
         * QA Automation Interface
         * Listens for command from Node.js Bridge via NamedPipe (Stubbed)
         */
        public static bool DEBUG_IPC_ENABLED = false;

        public static void StartDebugListener()
        {
            if (!DEBUG_IPC_ENABLED) return;
            Debug.Log("[NativeBridge] Starting Debug IPC Listener (NamedPipe: \\\\.\\pipe\\RoboTwin.FirmwareEngine.v1)...");
            // In a real implementation, this would spin up a specialized thread using System.IO.Pipes.NamedPipeServerStream
            // For MVP/Polyglot stub, we just log.
        }

        public static void PollDebugCommands()
        {
            if (!DEBUG_IPC_ENABLED) return;
            // Check pipe for {"action":"CLICK", ...}
        }
    }
}
