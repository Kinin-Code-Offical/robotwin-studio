using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Runtime
{
    public class TelemetryBus
    {
        public event Action<TelemetryFrame>? OnFrame;
        public event Action<EventLogEntry>? OnEvent;

        public void Publish(TelemetryFrame frame)
        {
            OnFrame?.Invoke(frame);
        }

        public void Publish(EventLogEntry entry)
        {
            OnEvent?.Invoke(entry);
        }
    }
}
