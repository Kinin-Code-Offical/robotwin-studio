using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RobotTwin.Core
{
    /// <summary>
    /// Bridge to the High-Performance C++ NativeEngine.
    /// Handles P/Invoke calls for generic circuit simulation.
    /// </summary>
    public static class NativeBridge
    {
        private const string PLUGIN_NAME = "NativeEngine";

        // --- Generic Circuit API ---

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_CreateContext")]
        public static extern void Native_CreateContext();

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_DestroyContext")]
        public static extern void Native_DestroyContext();

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_AddNode")]
        public static extern int Native_AddNode();

        // params is a reserved keyword, using parameters
        [DllImport(PLUGIN_NAME, EntryPoint = "Native_AddComponent")]
        public static extern int Native_AddComponent(int type, int paramCount, [In] float[] parameters);

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_Connect")]
        public static extern void Native_Connect(int compId, int pinIndex, int nodeId);

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_Step")]
        public static extern void Native_Step(float dt);

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_GetVoltage")]
        public static extern float Native_GetVoltage(int nodeId);

        [DllImport(PLUGIN_NAME, EntryPoint = "LoadHexFromFile")]
        public static extern int LoadHexFromFile(string path);

        // --- Legacy / Helper API ---

        [DllImport(PLUGIN_NAME, EntryPoint = "GetEngineVersion")]
        public static extern int GetEngineVersion();

        // Legacy Wrapper
        public static int GetVersion() => GetEngineVersion();
        public static void StepSimulation(float dt) => Native_Step(dt); // Wrapper for old StepSimulation


        // Enum mirroring Core/CircuitComponent.h
        public enum ComponentType
        {
            Resistor = 0,
            VoltageSource = 1,
            Ground = 2,
            Diode = 3,
            LED = 4,
            Switch = 5,
            IC_Pin = 6
        }
    }
}
