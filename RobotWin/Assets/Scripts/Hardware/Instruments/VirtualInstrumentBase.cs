using UnityEngine;

namespace RobotWin.Hardware.Instruments
{
    /// <summary>
    /// Base class for all virtual test intruments (Multimeter, Scope, etc.).
    /// Handles the 3D window, power button, and probe connections.
    /// </summary>
    public abstract class VirtualInstrumentBase : MonoBehaviour, IVirtualInstrument
    {
        [Header("UI Settings")]
        public GameObject windowPrefab; // The floating 2D panel
        protected GameObject _activeWindow;

        [Header("Probes")]
        public Transform probeA_Model;
        public Transform probeB_Model;

        protected IHardwareModule _targetModule;
        protected string _targetPinA;
        protected string _targetPinB;

        public virtual void ConnectProbe(int channel, IHardwareModule targetModule, string pinName)
        {
            _targetModule = targetModule;
            if (channel == 0) _targetPinA = pinName;
            else _targetPinB = pinName;

            Debug.Log($"[Instrument] Probe {channel} connected to {pinName}");
        }

        public virtual void DisconnectProbe(int channel)
        {
            if (channel == 0) _targetPinA = null;
            else _targetPinB = null;
        }

        public void SetActive(bool state)
        {
            gameObject.SetActive(state);
            if (_activeWindow != null) _activeWindow.SetActive(state);
        }

        // Child classes implement this (e.g. UpdateOscilloscope)
        protected abstract void InstrumentUpdateLoop();

        void Update()
        {
            if (!gameObject.activeSelf) return;
            InstrumentUpdateLoop();
        }
    }
}
