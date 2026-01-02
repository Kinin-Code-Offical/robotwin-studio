using UnityEngine;
using RobotTwin.CoreSim.Runtime;
using RobotTwin.CoreSim.Runtime.Components;
using RobotTwin.Core;

public class CircuitManagerTest : MonoBehaviour
{
    void Start()
    {
        Debug.Log("Starting CircuitManager Integration Test...");

        // Ensure Manager exists
        if (CircuitManager.Instance == null)
        {
            GameObject container = new GameObject("CircuitManager_Auto");
            container.AddComponent<CircuitManager>();
        }

        CreateBlinkyCircuit();
    }

    void CreateBlinkyCircuit()
    {
        // 1. Arduino
        GameObject arduObj = new GameObject("ArduinoUno");
        var ardu = arduObj.AddComponent<Arduino>();
        ardu.PinNets = new string[20];
        for(int i=0; i<20; ++i) ardu.PinNets[i] = ""; // Not Connected
        ardu.PinNets[13] = "Net_Pin13"; // Connect Pin 13
        ardu.HexFilePath = "C:/Temp/blink.hex"; 
        ardu.AutoLoad = false; 

        // 2. Resistor
        GameObject rObj = new GameObject("Resistor");
        var res = rObj.AddComponent<Resistor>();
        res.PinNets = new string[] { "Net_Pin13", "Net_Anode" };
        res.Resistance = 220;

        // 3. LED (Diode)
        GameObject dObj = new GameObject("LED");
        var led = dObj.AddComponent<Diode>();
        led.PinNets = new string[] { "Net_Anode", "GND" };

        Debug.Log("Blinky Circuit Created in Hierarchy.");
        
        // Trigger Rebuild
        CircuitManager.Instance.RebuildCircuit();
        CircuitManager.Instance.RunSimulation();
        
        Debug.Log("Simulation Running...");
    }
}
