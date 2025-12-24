using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.CoreSim.Runtime
{
    public class RunEngine
    {
        public RunSession Session { get; private set; }
        public TelemetryBus Bus { get; private set; }
        public CircuitSpec Circuit { get; private set; }

        public RunEngine(CircuitSpec circuit, int seed = 12345)
        {
            Circuit = circuit;
            Session = new RunSession(seed, 0.02f);
            Bus = new TelemetryBus();
        }

        public void Step(Dictionary<string, double>? injectedInputs = null)
        {
            Session.Advance();

            var frame = new TelemetryFrame
            {
                TickIndex = Session.TickIndex,
                TimeSeconds = Session.TimeSeconds
            };

            // MVP-0 Logic: Echo injected inputs to telemetry
            // Also add a "heartbeat" signal
            frame.Signals["sim_time"] = Session.TimeSeconds;
            frame.Signals["heartbeat"] = Math.Sin(Session.TimeSeconds);

            if (injectedInputs != null)
            {
                foreach (var kvp in injectedInputs)
                {
                    frame.Signals[kvp.Key] = kvp.Value;
                }
            }

            Bus.Publish(frame);

            // Periodically emit an event (e.g., every 100 ticks)
            if (Session.TickIndex % 100 == 0)
            {
                Bus.Publish(new EventLogEntry 
                { 
                    TimeSeconds = Session.TimeSeconds, 
                    Message = $"Heartbeat at tick {Session.TickIndex}" 
                });
            }
        }
    }
}
