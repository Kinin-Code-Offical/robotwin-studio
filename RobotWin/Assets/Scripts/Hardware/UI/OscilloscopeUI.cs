using UnityEngine;
using UnityEngine.UI;
using RobotWin.Hardware.Instruments;

namespace RobotWin.Hardware.UI
{
    /// <summary>
    /// Controls the specific UI elements of the Oscilloscope Floating Window.
    /// Bridges the backend 'Oscilloscope.cs' data to the frontend 'RawImage'.
    /// </summary>
    public class OscilloscopeUI : MonoBehaviour
    {
        [Header("Backend Reference")]
        public Oscilloscope sourceInstrument;

        [Header("UI Controls")]
        public RawImage screenDisplay;
        public Button powerBtn;
        public Slider timebaseSlider;
        public Slider voltsSlider;
        public Text timebaseText;
        public Text voltsText;

        /// <summary>
        /// Manually assign the source instrument.
        /// </summary>
        public void SetSource(Oscilloscope source)
        {
            this.sourceInstrument = source;
            if (gameObject.activeInHierarchy) Start();
        }

        void Awake()
        {
            EnsureUI();
        }

        void EnsureUI()
        {
            if (screenDisplay == null)
            {
                GameObject imgObj = new GameObject("ScopeScreen");
                imgObj.transform.SetParent(this.transform);
                screenDisplay = imgObj.AddComponent<RawImage>();
                screenDisplay.color = Color.white;

                // Size
                RectTransform rt = imgObj.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(256, 128);
            }
        }

        void Start()
        {
            if (sourceInstrument != null)
            {
                // Bind the Texture from the backend to this UI RawImage
                if (screenDisplay != null)
                {
                    screenDisplay.texture = sourceInstrument.GetDisplayTexture();
                }
            }

            // Bind Events - with null checks
            if (timebaseSlider != null)
            {
                timebaseSlider.onValueChanged.AddListener(OnTimebaseChanged);
            }
            if (voltsSlider != null)
            {
                voltsSlider.onValueChanged.AddListener(OnVoltsChanged);
            }
        }


        void Update()
        {
            // Optional: Update LED indicators or numeric readouts live
        }

        void OnDestroy()
        {
            // Unregister event listeners to prevent memory leaks
            if (timebaseSlider != null)
            {
                timebaseSlider.onValueChanged.RemoveListener(OnTimebaseChanged);
            }
            if (voltsSlider != null)
            {
                voltsSlider.onValueChanged.RemoveListener(OnVoltsChanged);
            }
        }

        void OnTimebaseChanged(float val)
        {
            if (sourceInstrument != null)
            {
                sourceInstrument.timePerDiv = val;
                if (timebaseText != null)
                {
                    timebaseText.text = $"{val * 1000f:F1} ms/div";
                }
            }
        }

        void OnVoltsChanged(float val)
        {
            if (sourceInstrument != null)
            {
                sourceInstrument.voltsPerDiv = val;
                if (voltsText != null)
                {
                    voltsText.text = $"{val:F1} V/div";
                }
            }
        }
    }
}
