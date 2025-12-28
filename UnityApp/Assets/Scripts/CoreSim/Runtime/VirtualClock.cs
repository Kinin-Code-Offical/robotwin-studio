namespace RobotTwin.CoreSim.Runtime
{
    public class VirtualClock
    {
        public double FrequencyHz { get; }
        public long TotalCycles { get; private set; }

        public VirtualClock(double frequencyHz)
        {
            FrequencyHz = frequencyHz;
        }

        public long Advance(double dtSeconds)
        {
            if (dtSeconds <= 0) return 0;
            long cycles = (long)(dtSeconds * FrequencyHz);
            if (cycles < 1) cycles = 1;
            TotalCycles += cycles;
            return cycles;
        }

        public double Micros => (TotalCycles / FrequencyHz) * 1_000_000.0;
    }
}
