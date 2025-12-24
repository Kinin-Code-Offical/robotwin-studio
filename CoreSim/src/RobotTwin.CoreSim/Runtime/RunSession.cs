using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Runtime
{
    public class RunSession
    {
        public int Seed { get; set; }
        public float FixedDeltaTime { get; set; } = 0.02f; // 50Hz
        public long TickIndex { get; set; }
        public float TimeSeconds { get; set; }
        
        public RunSession(int seed, float fixedDt)
        {
            Seed = seed;
            FixedDeltaTime = fixedDt;
            TickIndex = 0;
            TimeSeconds = 0f;
        }

        public void Advance()
        {
            TickIndex++;
            TimeSeconds += FixedDeltaTime;
        }
    }
}
