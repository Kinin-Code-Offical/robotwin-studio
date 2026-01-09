using UnityEngine;
using UnityEngine.UI;

namespace RobotWin.Hardware.Instruments
{
    /// <summary>
    /// A high-fidelity simulated Oscilloscope.
    /// Uses a rolling ring buffer to capture signal history.
    /// Simulates Analog Bandwidth limits and Triggering.
    /// </summary>
    public class Oscilloscope : VirtualInstrumentBase
    {
        [Header("Scope Settings")]
        public RawImage screenRenderer; // The texture on the 3D model
        public float timePerDiv = 0.001f; // 1ms
        public float voltsPerDiv = 1.0f;  // 1V

        [Header("Trigger")]
        public float triggerLevel = 2.5f;
        public bool triggerRising = true;

        // Simulation Constants
        private const int BUFFER_SIZE = 1024;
        private const float BANDWIDTH_HZ = 30000000f; // 30MHz

        // Ring Buffer
        private float[] _signalBuffer = new float[BUFFER_SIZE];
        private int _writeHead = 0;
        // private float _sampleTimer = 0f;
        // private float _sampleRate = 0.0001f; // 10kHz sample rate for display (Optimized)

        // Texture Data
        private Texture2D _displayTexture;
        private Color[] _clearPixels;

        // UI Accessor
        public Texture2D GetDisplayTexture() => _displayTexture;

        void Start()
        {
            // Init Texture
            _displayTexture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            if (screenRenderer != null) screenRenderer.texture = _displayTexture; // Set local 3D model

            _clearPixels = new Color[256 * 256];
            for (int i = 0; i < _clearPixels.Length; i++) _clearPixels[i] = Color.black;

            // Auto-create Floating Window if Manager exists
            if (RobotWin.Hardware.UI.InstrumentWindowManager.Instance != null)
            {
                var win = RobotWin.Hardware.UI.InstrumentWindowManager.Instance.OpenInstrumentWindow("Oscilloscope", "Oscilloscope - Channel 1");
                if (win != null)
                {
                    var ui = win.GetComponent<RobotWin.Hardware.UI.OscilloscopeUI>();
                    if (ui != null) ui.SetSource(this);
                }
            }
        }


        protected override void InstrumentUpdateLoop()
        {
            // 1. Signal Acquisition Step
            AcquireSignal();

            // 2. Render Step (Not every frame to save performance - e.g., 30fps)
            if (Time.frameCount % 2 == 0)
            {
                RenderTrace();
            }
        }

        private void AcquireSignal()
        {
            if (_targetModule == null || string.IsNullOrEmpty(_targetPinA)) return;

            // Read raw voltage
            float rawVoltage = _targetModule.ProbePinVoltage(_targetPinA);

            // Sim Bandwidth Limit (Low Pass Filter)
            // RC Filter: alpha = dt / (RC + dt)
            // Simple approach: Lerp towards new value
            float currentBufferVal = _signalBuffer[(_writeHead - 1 + BUFFER_SIZE) % BUFFER_SIZE];
            float smoothedVoltage = Mathf.Lerp(currentBufferVal, rawVoltage, 0.8f);

            // Write to buffer
            _signalBuffer[_writeHead] = smoothedVoltage;
            _writeHead = (_writeHead + 1) % BUFFER_SIZE;
        }

        private void RenderTrace()
        {
            // Clear
            _displayTexture.SetPixels(_clearPixels);

            // Find Trigger Point
            int triggerIndex = FindTriggerIndex();
            if (triggerIndex == -1) triggerIndex = (_writeHead - 256 + BUFFER_SIZE) % BUFFER_SIZE; // Auto mode

            // Draw Trace
            int previousY = -1;

            for (int x = 0; x < 256; x++)
            {
                // Get sample from buffer relative to trigger
                int sampleIdx = (triggerIndex + x) % BUFFER_SIZE;
                float volts = _signalBuffer[sampleIdx];

                // Map to Screen Y
                // Center is 128. 
                int y = 128 + Mathf.RoundToInt((volts / voltsPerDiv) * 25.0f); // 25 pixels per div
                y = Mathf.Clamp(y, 0, 255);

                // Line Drawing (Simple connection)
                _displayTexture.SetPixel(x, y, Color.green);

                // Fill gaps (vertical interpolation) if signal moves fast
                if (previousY != -1 && Mathf.Abs(y - previousY) > 1)
                {
                    int start = Mathf.Min(y, previousY);
                    int end = Mathf.Max(y, previousY);
                    for (int j = start; j < end; j++) _displayTexture.SetPixel(x, j, Color.green);
                }
                previousY = y;
            }

            _displayTexture.Apply();
        }

        private int FindTriggerIndex()
        {
            // Search backwards from write head
            for (int i = 0; i < BUFFER_SIZE - 1; i++)
            {
                int currIdx = (_writeHead - 1 - i + BUFFER_SIZE) % BUFFER_SIZE;
                int prevIdx = (_writeHead - 2 - i + BUFFER_SIZE) % BUFFER_SIZE;

                float vCurr = _signalBuffer[currIdx];
                float vPrev = _signalBuffer[prevIdx];

                // Rising Edge Cross
                if (triggerRising && vPrev < triggerLevel && vCurr >= triggerLevel)
                {
                    return currIdx;
                }
            }
            return -1; // Trigger lost
        }
    }
}
