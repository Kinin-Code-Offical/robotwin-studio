using System;

namespace RobotTwin.Game
{
    [Serializable]
    public sealed class RealtimeScheduleConfig
    {
        public bool Enabled = false;
        public float MasterDtSeconds = 1f / 60f;
        public float FirmwareDtSeconds = 0.02f;
        public float CircuitDtSeconds = 0.02f;
        public float PhysicsDtSeconds = 0.02f;
        public int MaxStepsPerFrame = 4;
        public float FrameBudgetMs = 8f;
        public float FirmwareBudgetMs = 2f;
        public float CircuitBudgetMs = 4f;
        public float PhysicsBudgetMs = 2f;
        public bool AllowFastPath = true;
        public float MaxSolveSkipSeconds = 0.2f;
    }
}
