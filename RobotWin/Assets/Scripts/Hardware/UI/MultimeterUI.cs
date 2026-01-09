using UnityEngine;
using UnityEngine.UI;
using RobotWin.Hardware.Instruments;

namespace RobotWin.Hardware.UI
{
    /// <summary>
    /// Controls the floating UI for the Digital Multimeter.
    /// Simulates a 7-Segment display behavior.
    /// </summary>
    public class MultimeterUI : MonoBehaviour
    {
        [Header("Backend Reference")]
        public DigitalMultimeter sourceInstrument;

        public void SetSource(DigitalMultimeter source)
        {
            this.sourceInstrument = source;
        }

        [Header("UI Controls")]
        public Text lcdText; // The main readout
        public Text unitText; // 'mV', 'V', 'Ohm'
        public Image modeIndicator; // Visual dial position (optional)

        void Awake()
        {
            EnsureUI();
        }

        void EnsureUI()
        {
            // Auto-generate UI if missing (scaffolding)
            if (lcdText == null)
            {
                GameObject tObj = new GameObject("RawLCD");
                tObj.transform.SetParent(this.transform);
                lcdText = tObj.AddComponent<Text>();
                lcdText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                lcdText.fontSize = 20;
                lcdText.color = Color.green;
                lcdText.alignment = TextAnchor.MiddleRight;
            }
            if (unitText == null)
            {
                GameObject uObj = new GameObject("UnitLCD");
                uObj.transform.SetParent(this.transform);
                unitText = uObj.AddComponent<Text>();
                unitText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                unitText.fontSize = 14;
                unitText.color = Color.green;
                unitText.alignment = TextAnchor.MiddleLeft;

                // Diff layout
                RectTransform rt = uObj.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(50, 0);
            }
        }

        void Update()
        {
            if (sourceInstrument != null && lcdText != null)
            {
                // Pull formatted value from backend
                string val = sourceInstrument.GetValueString();
                string unit = sourceInstrument.GetUnitString();

                lcdText.text = val;
                if (unitText != null) unitText.text = unit;
            }
        }
    }
}
