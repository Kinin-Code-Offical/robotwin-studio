using UnityEngine;

namespace RobotWin.Robotics
{
    /// <summary>
    /// physics-based DC Motor controller that supports efficiency degradation.
    /// Used to simulate mechanical imperfections (e.g. gearbox friction, mismatched motors).
    /// </summary>
    [RequireComponent(typeof(HingeJoint))]
    public class MotorController : MonoBehaviour
    {
        [Header("Hardware Simulation")]
        [Tooltip("Multiplier for visual 'Realism'. 1.0 = New Motor. 0.8 = Worn brushes/Gearbox friction.")]
        [Range(0.0f, 2.0f)]
        public float efficiency = 1.0f;

        [Tooltip("Max torque in Nm at 100% duty cycle.")]
        public float maxTorque = 1.5f;

        [Tooltip("Max RPM at free run.")]
        public float maxRPM = 320f; // Standard for yellow gear motors

        [Header("Debug")]
        [Tooltip("Current PWM input from Firmware (0-255).")]
        public int currentPwmInput = 0;

        // Internal Physics
        private HingeJoint _joint;
        private JointMotor _motor;

        void Awake()
        {
            _joint = GetComponent<HingeJoint>();
            _joint.useMotor = true;
            _motor = _joint.motor;
        }

        void FixedUpdate()
        {
            ApplyPhysics();
        }

        private void ApplyPhysics()
        {
            if (currentPwmInput <= 0)
            {
                _motor.targetVelocity = 0;
                _motor.force = maxTorque * efficiency; // Braking torque
                _joint.motor = _motor;
                return;
            }

            // PWM to Voltage Duty Cycle
            float dutyCycle = Mathf.Clamp01(currentPwmInput / 255f);

            // Real DC Motor Model: Voltage controls Velocity, Torque is limited by mechanics
            float targetVel = maxRPM * dutyCycle * 6.0f; // Convert RPM to deg/s approx (simplified)

            // Apply Efficiency:
            // A heavily worn motor (efficiency 0.6) will have less torque AND slightly less top speed
            _motor.targetVelocity = targetVel * Mathf.Sqrt(efficiency);
            _motor.force = maxTorque * efficiency;

            _joint.motor = _motor;
        }

        /// <summary>
        /// Interface for the Virtual MCU (e.g., L293D simulation) to call.
        /// </summary>
        public void SetSpeed(int pwm)
        {
            currentPwmInput = pwm;
        }
    }
}
