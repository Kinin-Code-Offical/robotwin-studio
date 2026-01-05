namespace RobotTwin.CoreSim.Host
{
    public sealed class RealtimeHardeningOptions
    {
        public bool Enabled { get; set; } = false;
        public bool UseHighPriority { get; set; } = true;
        public bool UseHighResolutionTimer { get; set; } = true;
        public bool UseSpinWait { get; set; } = true;
        public double SpinBufferSeconds { get; set; } = 0.001;
        public double SleepThresholdSeconds { get; set; } = 0.002;
        public long? AffinityMask { get; set; }
    }
}
