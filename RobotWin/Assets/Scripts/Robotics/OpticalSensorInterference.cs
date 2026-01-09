using UnityEngine;

namespace RobotWin.Robotics
{
    /// <summary>
    /// Simulates the physical limitation of Optical Sensors (IR/Color) when exposed to varying light angles.
    /// Handles:
    /// 1. Ambient Light Saturation (Sunlight blinding the sensor).
    /// 2. Specular Glare (Reflection of light on glossy tape blinding the sensor).
    /// </summary>
    [RequireComponent(typeof(AnalogSignalProcessor))]
    public class OpticalSensorInterference : MonoBehaviour
    {
        [Header("Environmental Factors")]
        public Light mainLightSource;

        [Tooltip("How sensitive is this sensor to ambient light saturation?")]
        public float lightSensitivity = 0.5f;

        [Tooltip("The glossy threshold. If surface reflects light directly into sensor.")]
        public float glareThreshold = 0.95f;

        private AnalogSignalProcessor _signalProcessor;

        void Awake()
        {
            _signalProcessor = GetComponent<AnalogSignalProcessor>();
            if (mainLightSource == null)
            {
                // Auto-find the main directional light
                Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (Light l in lights)
                {
                    if (l.type == LightType.Directional) mainLightSource = l;
                }
            }
        }

        /// <summary>
        /// Calculates the degradation factor (0.0 to 1.0) based on lighting.
        /// 0.0 = Totally Blinded by Light. 1.0 = Perfect Dark Operation.
        /// </summary>
        public float CalculateLightInterference(RaycastHit hitInfo)
        {
            if (mainLightSource == null) return 1.0f;

            // 1. Surface Glare Calculation
            // Calculate the reflection vector of the light off the surface
            Vector3 incomingLightDir = mainLightSource.transform.forward; // Directional light direction
            Vector3 reflectionVector = Vector3.Reflect(incomingLightDir, hitInfo.normal);

            // Check if this reflection points straight at our sensor (which is looking down)
            // Sensor Look Dir is transform.forward (assuming it points down)
            // If Reflection is close to -transform.forward, it goes INTO the sensor eye.
            float glareFactor = Vector3.Dot(reflectionVector, -transform.forward);

            if (glareFactor > glareThreshold)
            {
                // Glare detected! The tape acts like a mirror.
                // IR Sensors hate this. They might read "White" (Input Low) constantly.
                return 0.1f; // High interference
            }

            // 2. Ambient Saturation
            // If the light is super bright and hitting the surface directly
            float surfaceIllumination = Vector3.Dot(-incomingLightDir, hitInfo.normal);
            if (surfaceIllumination > 0.8f && mainLightSource.intensity > 1.2f)
            {
                return 0.8f; // Slight saturation
            }

            return 1.0f;
        }
    }
}
