using UnityEngine;
using RobotWin.Hardware.Core;

namespace RobotWin.Hardware.Instruments
{
    /// <summary>
    /// Attached to the 3D Model of a Probe tip (e.g., Red Probe, Black Probe).
    /// Handles Drag & Drop interaction and snapping to Circuit Pins.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class VirtualInstrumentProbe : MonoBehaviour
    {
        [Header("Probe Settings")]
        public VirtualInstrumentBase parentInstrument;
        [Tooltip("0 for Positive (Red), 1 for Negative/Ground (Black)")]
        public int channelId;
        public float snapRadius = 0.05f; // 5cm snap range

        private bool _isDragging = false;
        private Vector3 _dragOffset;
        private float _zDepth;

        private Vector3 _originalPos;
        private Quaternion _originalRot;

        void Start()
        {
            _originalPos = transform.localPosition;
            _originalRot = transform.localRotation;
        }

        void OnMouseDown()
        {
            // Calculate depth for mouse mapping
            _zDepth = Camera.main.WorldToScreenPoint(transform.position).z;
            _dragOffset = transform.position - GetMouseWorldPos();

            _isDragging = true;

            // Disconnect mechanically when picked up
            parentInstrument.DisconnectProbe(channelId);

            // Visual feedback
            GetComponent<Renderer>().material.color = Color.yellow; // Highlight
        }

        void OnMouseUp()
        {
            _isDragging = false;
            GetComponent<Renderer>().material.color = Color.white; // Restore

            // Attempt Snap
            CheckConnection();
        }

        void OnMouseDrag()
        {
            if (_isDragging)
            {
                transform.position = GetMouseWorldPos() + _dragOffset;
            }
        }

        private Vector3 GetMouseWorldPos()
        {
            Vector3 mousePoint = Input.mousePosition;
            mousePoint.z = _zDepth;
            return Camera.main.ScreenToWorldPoint(mousePoint);
        }

        private void CheckConnection()
        {
            // Sphere Cast to find Hardware Modules
            Collider[] hits = Physics.OverlapSphere(transform.position, snapRadius);

            foreach (var hit in hits)
            {
                // Look for HardwareOptimizedModule on the object or its parent
                var hardware = hit.GetComponentInParent<HardwareOptimizedModule>();

                if (hardware != null)
                {
                    string pinName;
                    Vector3 snapPos;

                    // Ask the hardware for the nearest pin
                    if (hardware.GetNearestPin(transform.position, snapRadius, out pinName, out snapPos))
                    {
                        // 1. Visually Snap
                        transform.position = snapPos;
                        transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward); // Point down

                        // 2. Logically Connect
                        parentInstrument.ConnectProbe(channelId, hardware, pinName);

                        Debug.Log($"Probe {channelId} snapped to {hardware.ModuleName}.{pinName}");
                        return;
                    }
                }
            }

            // If no connection, maybe return to holster?
            // For now, leave it floating where released.
        }
    }
}
