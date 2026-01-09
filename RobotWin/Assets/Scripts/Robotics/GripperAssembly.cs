using UnityEngine;

namespace RobotWin.Robotics
{
    /// <summary>
    /// Simulates the specific "Geared Grip" mechanism seen in the photos.
    /// One servo drives the Right Finger directly.
    /// The Left Finger is geared to move in the exact opposite direction.
    /// </summary>
    public class GripperAssembly : MonoBehaviour
    {
        [Header("Mechanical Linkage")]
        [Tooltip("The main servo driving the mechanism (Right Finger).")]
        public ServoMechanism mainServo;

        [Tooltip("The passive finger that moves opposite (Left Finger).")]
        public HingeJoint passiveFinger;

        [Header("Gear Ratio")]
        [Tooltip("If the main servo moves +10 deg, passive moves -10 * ratio.")]
        public float gearRatio = -1.0f;

        void FixedUpdate()
        {
            if (mainServo != null && passiveFinger != null)
            {
                // Get the current physical angle of the main servo
                // Unity HingeJoint angle is relative to anchor
                float mainAngle = mainServo.GetComponent<HingeJoint>().angle;

                // Drive the passive finger to match (mimicking physical gear teeth)
                JointSpring spring = passiveFinger.spring;
                spring.targetPosition = mainAngle * gearRatio;
                passiveFinger.spring = spring;
            }
        }

        // Helper Calculation for exact "Gap" width based on angle
        // Validates "gripPos = 70" from the .ino file
        public float CalculateCurrentGapWidth()
        {
            if (mainServo == null) return 0f;

            // Simple Trig approximation based on finger length
            // This is useful for the "Digital Twin" validation report
            float angle = mainServo.currentAngle;
            // Assuming 90 is parallel, 70 is slightly closed...
            return (angle - 70f) * 0.1f; // Dummy calc for now
        }
    }
}
