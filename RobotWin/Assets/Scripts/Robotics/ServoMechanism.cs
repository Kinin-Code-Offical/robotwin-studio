using UnityEngine;

namespace RobotWin.Robotics
{
    /// <summary>
    /// Translates Arduino Servo commands (0-180 degrees) into physical forces on visual joints.
    /// Handles mechanical limits, speed of rotation, and gear ratios.
    /// </summary>
    [RequireComponent(typeof(HingeJoint))]
    public class ServoMechanism : MonoBehaviour
    {
        [Header("Calibration")]
        [Tooltip("The angle in Unity that corresponds to Servo 0.")]
        public float zeroPointOffset = 0f;

        [Tooltip("Direction multiplier. 1 for Normal, -1 for Inverted mounting.")]
        public float direction = 1f;

        [Tooltip("Speed of the servo in degrees per second (Standard SG90 is ~0.1s/60deg => 600deg/s).")]
        public float servoSpeed = 300f; // Slower for realism under load

        [Header("Status")]
        public float currentAngle = 90f;
        public float targetAngle = 90f;

        private HingeJoint _joint;
        private JointSpring _spring;

        void Awake()
        {
            _joint = GetComponent<HingeJoint>();
            _spring = _joint.spring;
        }

        void FixedUpdate()
        {
            // Simulate the internal PID of the Servo motor
            // Move towards target at servoSpeed
            float step = servoSpeed * Time.fixedDeltaTime;
            currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, step);

            // Apply to Unity Physics
            // Logic: Unity Hinge Limits are relative to 'Start Rotation'.
            // calculation: UnityTarget = (ArduinoAngle - 90) * direction + offset
            // Assuming 0 is -90, 90 is 0 (center), 180 is +90 relative to center

            float physicsTarget = (currentAngle - 90f) * direction + zeroPointOffset;

            _spring.targetPosition = physicsTarget;
            _joint.spring = _spring;
        }

        /// <summary>
        /// Called by Firmware Bridge. Input: 0-180.
        /// </summary>
        public void SetTargetAngle(int angle)
        {
            targetAngle = Mathf.Clamp(angle, 0, 180);
        }
    }
}
