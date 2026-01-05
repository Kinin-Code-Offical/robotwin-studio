namespace RobotTwin.CoreSim.Host
{
    public sealed class SimHostOptions
    {
        /// <summary>
        /// Fixed timestep used by the background run loop and the default StepOnce inputs.
        /// </summary>
        public double DtSeconds { get; set; } = 0.01;

        /// <summary>
        /// Optional deterministic config. When set and Enabled=true, DtSeconds is taken from it.
        /// </summary>
        public DeterministicModeConfig? Deterministic { get; set; }

        /// <summary>
        /// Optional realtime hardening settings for Windows runtimes.
        /// </summary>
        public RealtimeHardeningOptions? Realtime { get; set; }
    }
}
