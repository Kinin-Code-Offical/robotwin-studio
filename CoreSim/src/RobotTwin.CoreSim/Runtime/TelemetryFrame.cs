using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Runtime
{
    public class TelemetryFrame
    {
        public long TickIndex { get; set; }
        public float TimeSeconds { get; set; }
        
        // Key: ComponentID:Pin or special keys
        // Value: Signal value (voltage, logic level, etc)
        public Dictionary<string, double> Signals { get; set; } = new Dictionary<string, double>();
        
        // Optional: List of current faults/warnings
        public List<string> ValidationMessages { get; set; } = new List<string>();
    }
}
