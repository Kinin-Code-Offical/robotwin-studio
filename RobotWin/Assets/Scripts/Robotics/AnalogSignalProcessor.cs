using UnityEngine;

namespace RobotWin.Robotics
{
    public enum NoiseProfile
    {
        None,
        TCRT5000_Standard, // Standard IR Line Sensor flickers
        Dirty_Potentiometer, // Spikes
        Wireless_Interference, // Periodic drops
        High_Frequency_Vibration // Depends on speed
    }

    /// <summary>
    /// Middleware for introducing realistic analog signal degradation.
    /// Attach this to any sensor Game Object (IR, UltraSonic, Potentiometer).
    /// </summary>
    public class AnalogSignalProcessor : MonoBehaviour
    {
        [Header("Signal Degradation")]
        public NoiseProfile profile = NoiseProfile.None;

        [Tooltip("Base random noise to add +/- Percentage.")]
        [Range(0f, 20f)]
        public float jitterPercentage = 0f;

        [Header("Advanced Fault Injection")]
        [Tooltip("Simulates a loose wire connection that drops signal occasionally.")]
        public bool simulateLooseConnection = false;

        private float _nextDropTime = 0f;
        private bool _isDropped = false;

        public int ApplyEffects(int rawValue)
        {
            // 1. Loose Connection Fault
            if (simulateLooseConnection)
            {
                if (Time.time > _nextDropTime)
                {
                    _isDropped = !_isDropped;
                    _nextDropTime = Time.time + (_isDropped ? Random.Range(0.05f, 0.2f) : Random.Range(1.0f, 5.0f));
                }
                if (_isDropped) return 0; // Signal Cutout
            }

            if (profile == NoiseProfile.None && jitterPercentage <= 0) return rawValue;

            float noise = 0;

            // 2. Profile Based Noise
            switch (profile)
            {
                case NoiseProfile.TCRT5000_Standard:
                    // TCRT5000 tends to fluctuate +/- 3 units on 0-1023 scale even when still
                    // Plus more noise if surface is glossy (simulated via jitterPercentage)
                    noise = Random.Range(-3f, 3f);
                    break;
                case NoiseProfile.Wireless_Interference:
                    noise = Mathf.Sin(Time.time * 50) * 20; // 50Hz hum
                    break;
            }

            // 3. Percentage Jitter
            float jitter = rawValue * (jitterPercentage / 100f) * Random.Range(-1f, 1f);

            // 4. Combine and Clamp
            int result = Mathf.Clamp(Mathf.RoundToInt(rawValue + noise + jitter), 0, 1023);
            return result;
        }
    }
}
