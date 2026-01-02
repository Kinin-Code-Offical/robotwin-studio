using System;
using UnityEngine;
using RobotTwin.Core;

namespace RobotTwin.CoreSim.Runtime.Components
{
    public class Arduino : CircuitComponent
    {
        [Tooltip("Path to .hex firmware file")]
        public string HexFilePath;

        [Tooltip("Auto-load hex on start")]
        public bool AutoLoad = true;

        public override void OnBuildCircuit(Func<string, int> resolver)
        {
            // Arduino Uno has 20 significant pins for standard use (D0-D13, A0-A5)
            // Or typically represented as a DIP package.
            // We assume PinNets matches the Order defined in AvrComponent.
            // 0-7: PORTD (D0-D7)
            // 8-13: PORTB (D8-D13)
            // 14-19: PORTC (A0-A5)
            
            // Native Component
            NativeId = NativeBridge.Native_AddComponent((int)NativeBridge.ComponentType.IC_Pin, 0, null);

            if (NativeId != -1)
            {
                // Connect Pins
                for (int i = 0; i < PinNets.Length && i < 20; i++)
                {
                    int netId = resolver(PinNets[i]);
                    NativeBridge.Native_Connect(NativeId, i, netId);
                }

                // Load Firmware
                if (AutoLoad && !string.IsNullOrEmpty(HexFilePath))
                {
                    LoadFirmware(HexFilePath);
                }
            }
        }

        public void LoadFirmware(string path)
        {
            if (NativeId == -1) return;
            // The Native API LoadHexFromFile scans for ANY AvrComponent.
            // Ideally it should target THIS component ID.
            // But currently the Native API `LoadHexFromFile` iterates all components.
            // For MVP (Single MCU), this is fine.
            NativeBridge.LoadHexFromFile(path);
            Debug.Log($"Loaded Firmware: {path}");
        }
    }
}
