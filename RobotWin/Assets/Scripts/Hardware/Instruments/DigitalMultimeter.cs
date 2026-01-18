using UnityEngine;
using UnityEngine.UI;
using RobotTwin.Game;

namespace RobotWin.Hardware.Instruments
{
    /// <summary>
    /// A concrete implementation of a Virtual DMM.
    /// Simulates DC Voltage calculation with error margins and auto-ranging delay.
    /// </summary>
    public class DigitalMultimeter : VirtualInstrumentBase
    {
        [Header("DMM Spec")]
        public Text displayReadout; // UI Text on the 3D model or Floating Window
        public float errorMarginPercent = 0.5f;
        public int errorUpdateRate = 2; // Updates per sec

        [Header("Window")]
        public bool AutoOpenWindow = true;

        [Header("Bridge Fallback")]
        public string simBoardId = string.Empty;
        public string simPinA = string.Empty;
        public string simPinB = string.Empty;

        [Header("State")]
        // private float _displayedValue = 0f;
        private float _timer = 0f;
        private string _valueString = "0.00";
        private string _unitString = "V";

        public string GetValueString() => _valueString;
        public string GetUnitString() => _unitString;

        void Start()
        {
            // Auto-create Floating Window if Manager exists
            if (AutoOpenWindow && RobotWin.Hardware.UI.InstrumentWindowManager.Instance != null)
            {
                var win = RobotWin.Hardware.UI.InstrumentWindowManager.Instance.OpenInstrumentWindow("Multimeter", "Digital Multimeter");
                AttachWindow(win);
            }
        }

        public void AttachWindow(GameObject window)
        {
            _activeWindow = window;
            if (window == null) return;

            var ui = window.GetComponent<RobotWin.Hardware.UI.MultimeterUI>();
            if (ui != null) ui.SetSource(this);
        }

        public void SetSimPins(string boardId, string pinA, string pinB)
        {
            simBoardId = boardId ?? string.Empty;
            simPinA = pinA ?? string.Empty;
            simPinB = pinB ?? string.Empty;
        }

        protected override void InstrumentUpdateLoop()
        {
            _timer += Time.deltaTime;

            // Simulate Display Refresh Rate (2Hz)
            if (_timer < (1.0f / errorUpdateRate)) return;
            _timer = 0f;

            float voltageA = 0f;
            float voltageB = 0f;

            // Read Probe A
            TryReadVoltage(_targetPinA, simPinA, out voltageA);

            // Read Probe B (Reference)
            // Note: Probe B could be on a DIFFERENT module in improved versions,
            // but for now Base assumes single module target or we need to expand Base.
            // Assuming simplified relative measurement on same reference ground or expanded logic.
            // Let's assume Probe B is Ground (0V) if unconnected, or read if connected.
            // *Constraint*: Current VirtualInstrumentBase stores ONE _targetModule.
            // *Improvement*: Probes should store their own target modules independently.
            // For this iteration, we measure A relative to Ground if B is not connected.
            TryReadVoltage(_targetPinB, simPinB, out voltageB);

            float trueVoltage = voltageA - voltageB;

            // Simulate Error / Noise
            // +/- 0.5% + Random Noise
            float error = trueVoltage * (errorMarginPercent / 100f);
            float noise = Random.Range(-0.02f, 0.02f); // 20mV thermal noise

            float measuredVal = trueVoltage + error + noise;

            UpdateDisplay(measuredVal);
        }

        private bool TryReadVoltage(string probePin, string fallbackPin, out float voltage)
        {
            voltage = 0f;
            if (_targetModule != null && !string.IsNullOrEmpty(probePin))
            {
                voltage = _targetModule.ProbePinVoltage(probePin);
                return true;
            }

            if (!string.IsNullOrEmpty(simBoardId) && !string.IsNullOrEmpty(fallbackPin))
            {
                var host = SimHost.Instance;
                if (host != null && host.TryGetPinVoltage(simBoardId, fallbackPin, out voltage))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateDisplay(float val)
        {
            // Auto-ranging logic for UI consumption
            if (Mathf.Abs(val) < 0.001f)
            {
                _valueString = "0.00";
                _unitString = "mV";
            }
            else if (Mathf.Abs(val) < 1.0f)
            {
                _valueString = (val * 1000f).ToString("F1");
                _unitString = "mV";
            }
            else
            {
                _valueString = val.ToString("F2");
                _unitString = "V";
            }

            if (displayReadout != null)
            {
                displayReadout.text = $"{_valueString} {_unitString}";
            }
        }
    }
}
