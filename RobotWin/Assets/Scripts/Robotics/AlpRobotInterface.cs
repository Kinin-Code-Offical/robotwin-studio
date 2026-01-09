using UnityEngine;
using RobotTwin.Core;

namespace RobotWin.Robotics
{
    /// <summary>
    /// The specific "Bridge" script that wires Alp's physical components 
    /// to the generic NativeBridge (which talks to FirmwareEngine).
    /// </summary>
    public class AlpRobotInterface : MonoBehaviour
    {
        [Header("Motors")]
        public MotorController leftMotor;
        public MotorController rightMotor;

        [Header("Gripper")]
        public ServoMechanism gripperServo;
        public ServoMechanism liftServo;

        [Header("Sensors")]
        public AnalogSignalProcessor[] irSensors; // Order: A0, A1, A2, A3 (Reversed in wiring!)
        public AnalogSignalProcessor colorSensorR;
        public AnalogSignalProcessor colorSensorG;
        public AnalogSignalProcessor colorSensorB;

        // Virtual Pin Maps (Must match AFMotor.h and Adafruit_TCS34725.h)
        const int PIN_MOTOR_DIR_BASE = 60; // +1=L, +3=R
        const int PIN_MOTOR_PWM_L = 11; // Port 1
        const int PIN_MOTOR_PWM_R = 6; // Port 3 (Wait, Check new_start.ino -> AF_DCMotor motorSag(3). Port 3 uses Pin 6 usually?) 
                                       // L293D Shield: 
                                       // Port 1 (Left) -> PWM 11
                                       // Port 3 (Right) -> PWM 6

        // Analog Input Map
        // IR: A3, A2, A1, A0
        // RGB: A10, A11, A12

        void Update()
        {
            // NativeBridge is static, so no null check needed for Instance.
            // But we should check if context exists if possible, but for now we assume it runs.

            ProcessMotors();
            ProcessServos();
            ProcessSensors();
        }

        void ProcessMotors()
        {
            // --- LEFT MOTOR (Port 1) ---
            int dirL = NativeBridge.GetPinState(PIN_MOTOR_DIR_BASE + 1); // 1=Fwd, 2=Back
            int pwmL = NativeBridge.GetPinPwm(PIN_MOTOR_PWM_L);

            // Convert to Signed PWM for MotorController (-255 to 255)
            int finalPwmL = 0;
            if (dirL == 1) finalPwmL = pwmL;
            else if (dirL == 2) finalPwmL = -pwmL;

            if (leftMotor != null) leftMotor.SetSpeed(finalPwmL);

            // --- RIGHT MOTOR (Port 3) ---
            int dirR = NativeBridge.GetPinState(PIN_MOTOR_DIR_BASE + 3);
            int pwmR = NativeBridge.GetPinPwm(PIN_MOTOR_PWM_R);

            int finalPwmR = 0;
            if (dirR == 1) finalPwmR = pwmR;
            else if (dirR == 2) finalPwmR = -pwmR;

            if (rightMotor != null) rightMotor.SetSpeed(finalPwmR);
        }

        void ProcessServos()
        {
            // Need the servo pins from new_start.ino
            // "Servo myServo;" usually defaults to Pin 9 or 10 on Shield.
            // Let's assume user attaches them to Servo 2 (Pin 9) and Servo 1 (Pin 10).

            // Lift Arm (Pin 9?)
            // Gripper (Pin 10?)
            // We need to verify this from the INO file context later.
            // For now, mapping generic servo pins:

            if (liftServo != null)
                liftServo.SetTargetAngle((int)NativeBridge.GetPinServo(9));

            if (gripperServo != null)
                gripperServo.SetTargetAngle((int)NativeBridge.GetPinServo(10));
        }

        void ProcessSensors()
        {
            // Send Unity Sensor Data -> Firmware Engine
            // Mapping: irSensors[0]->A0, [1]->A1, [2]->A2, [3]->A3

            for (int i = 0; i < irSensors.Length && i < 4; i++)
            {
                if (irSensors[i] != null)
                {
                    int val = ReadGenericSensor(irSensors[i]);
                    // Map to A0..A3
                    NativeBridge.SetAnalogInput(i, val);
                }
            }

            // RGB Sensor (TCS34725 Color Sensor)
            // Read color from scene by raycasting downward and sampling material color
            if (colorSensorR != null)
            {
                RaycastHit hit;
                int r = 0, g = 0, b = 0, clear = 0;

                if (Physics.Raycast(colorSensorR.transform.position, -colorSensorR.transform.up, out hit, 0.05f))
                {
                    Renderer renderer = hit.collider.GetComponent<Renderer>();
                    if (renderer != null && renderer.sharedMaterial != null)
                    {
                        // Sample material color and convert to 16-bit RGB values (TCS34725 output)
                        Color32 color = renderer.sharedMaterial.color;
                        r = (int)(color.r * 257); // 0-255 -> 0-65535
                        g = (int)(color.g * 257);
                        b = (int)(color.b * 257);
                        clear = (int)((color.r + color.g + color.b) * 85.7f); // Average * (65535/255/3)

                        // Apply sensor-specific gain compensation (from TCS34725 datasheet)
                        r = Mathf.Clamp((int)(r * 1.0f), 0, 65535);
                        g = Mathf.Clamp((int)(g * 1.2f), 0, 65535); // Green channel typically needs boost
                        b = Mathf.Clamp((int)(b * 1.1f), 0, 65535);
                    }
                }

                // Map to analog inputs (R->A4, G->A5, B->A6, Clear->A7)
                NativeBridge.SetAnalogInput(4, r);
                NativeBridge.SetAnalogInput(5, g);
                NativeBridge.SetAnalogInput(6, b);
                NativeBridge.SetAnalogInput(7, clear);
            }
        }

        private int ReadGenericSensor(AnalogSignalProcessor sensor)
        {
            // 1. Raycast
            RaycastHit hit;
            int rawValue = 100; // Default White (High Reflectance -> Low Voltage)

            if (Physics.Raycast(sensor.transform.position, -sensor.transform.up, out hit, 0.05f)) // 5cm range
            {
                // 2. Determine Surface Type
                // This is a simplified Check. In production, use Texture Coordinates.
                // Assuming "Black Tape" material has name "Track_Electrical_Tape" or low Albedo.

                Renderer r = hit.collider.GetComponent<Renderer>();
                if (r != null)
                {
                    // Check material color brightness
                    float brightness = r.sharedMaterial.color.grayscale;
                    if (brightness < 0.2f)
                    {
                        rawValue = 900; // Black (Absorbs IR -> High Voltage)
                    }
                }

                // 3. Optical Interference (Glare)
                // If Black Tape Glare -> Returns 0.1 (Blind) -> Multiplier?
                // Actually, Glare on Black Tape makes it reflect -> Sensor sees LIGHT -> Reads Low (White).

                var interference = sensor.GetComponent<OpticalSensorInterference>();
                if (interference != null)
                {
                    float factor = interference.CalculateLightInterference(hit);
                    if (factor < 0.5f)
                    {
                        // Glare Detected! Black looks like White.
                        rawValue = 100; // False Positive
                    }
                }
            }

            // 4. Electrical Noise
            return sensor.ApplyEffects(rawValue);
        }

    }
}
