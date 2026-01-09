using UnityEngine;
using System;

namespace RobotTwin.Timing
{
    /// <summary>
    /// Adapter that bridges Circuit simulation timing to GlobalLatencyManager
    /// Captures latency from VirtualMcu and Circuit analysis, propagates to entire system
    /// </summary>
    public class CircuitLatencyAdapter : MonoBehaviour
    {
        [Header("Circuit Timing Source")]
        [SerializeField] private bool _captureFromVirtualMcu = true;
        [SerializeField] private bool _captureFromCircuitAnalyzer = true;

        [Header("Timing Configuration")]
        [SerializeField] private double _mcuClockFrequency = 16000000.0; // 16 MHz
        [SerializeField] private float _updateIntervalSeconds = 0.016f; // ~60 FPS

        [Header("Latency Calculation")]
        [SerializeField] private bool _includeADCLatency = true;
        [SerializeField] private bool _includeUARTLatency = true;
        [SerializeField] private bool _includeCircuitPropagationDelay = true;

        // VirtualMcu integration (native interface)
        private IntPtr _virtualMcuHandle = IntPtr.Zero;
        private long _lastCycleCount = 0;

        // Circuit timing data
        private double _accumulatedLatency = 0.0;
        private long _totalCyclesProcessed = 0;

        // Update timing
        private float _timeSinceLastUpdate = 0f;

        // Performance counters
        public LatencyMetrics Metrics { get; private set; } = new LatencyMetrics();

        private void Start()
        {
            // Register with GlobalLatencyManager
            if (GlobalLatencyManager.Instance != null)
            {
                Debug.Log("[CircuitLatencyAdapter] Connected to GlobalLatencyManager");
            }
            else
            {
                Debug.LogError("[CircuitLatencyAdapter] GlobalLatencyManager not found!");
            }
        }

        private void Update()
        {
            _timeSinceLastUpdate += Time.deltaTime;

            if (_timeSinceLastUpdate >= _updateIntervalSeconds)
            {
                CaptureAndPropagateLatency();
                _timeSinceLastUpdate = 0f;
            }
        }

        /// <summary>
        /// Capture latency from all circuit sources and propagate to GlobalLatencyManager
        /// </summary>
        private void CaptureAndPropagateLatency()
        {
            double totalLatency = 0.0;
            long cycleCount = 0;

            // 1. Capture from VirtualMcu (cycle-accurate firmware simulation)
            if (_captureFromVirtualMcu && _virtualMcuHandle != IntPtr.Zero)
            {
                cycleCount = GetVirtualMcuCycleCount();
                long deltaCycles = cycleCount - _lastCycleCount;
                _lastCycleCount = cycleCount;

                // Convert cycles to latency
                double cycleLatency = deltaCycles / _mcuClockFrequency;
                totalLatency += cycleLatency;

                Metrics.VirtualMcuCycles = cycleCount;
                Metrics.VirtualMcuLatencySeconds = cycleLatency;
            }

            // 2. Capture from Circuit Analyzer (component propagation delays)
            if (_captureFromCircuitAnalyzer)
            {
                double circuitLatency = CalculateCircuitPropagationLatency();
                totalLatency += circuitLatency;

                Metrics.CircuitPropagationLatencySeconds = circuitLatency;
            }

            // 3. Add peripheral latencies (ADC, UART, etc.)
            if (_includeADCLatency)
            {
                double adcLatency = CalculateADCLatency();
                totalLatency += adcLatency;
                Metrics.ADCLatencySeconds = adcLatency;
            }

            if (_includeUARTLatency)
            {
                double uartLatency = CalculateUARTLatency();
                totalLatency += uartLatency;
                Metrics.UARTLatencySeconds = uartLatency;
            }

            _accumulatedLatency = totalLatency;
            _totalCyclesProcessed = cycleCount;

            // CRITICAL: Propagate to GlobalLatencyManager
            // This ensures ALL subsystems use same timing
            GlobalLatencyManager.Instance.UpdateCircuitLatency(totalLatency, cycleCount);

            Metrics.TotalLatencySeconds = totalLatency;
            Metrics.UpdateCount++;
        }

        /// <summary>
        /// Get cycle count from VirtualMcu (native call)
        /// </summary>
        private long GetVirtualMcuCycleCount()
        {
            // TODO: Implement native interop to VirtualMcu::TickCount()
            // For now, use simulated cycle count based on real time
            return (long)(Time.realtimeSinceStartup * _mcuClockFrequency);
        }

        /// <summary>
        /// Calculate circuit propagation latency from component delays
        /// </summary>
        private double CalculateCircuitPropagationLatency()
        {
            if (!_includeCircuitPropagationDelay)
                return 0.0;

            // Circuit propagation delay calculation
            // Based on component types and signal paths
            double latency = 0.0;

            // Example: resistor networks (negligible), capacitors (RC time constant)
            // TODO: Integrate with actual CircuitAnalyzer data

            // Typical values for common components:
            // - Resistor: ~1ps (negligible)
            // - Capacitor: depends on RC constant, typically 1-100us
            // - Transistor switching: 1-10ns
            // - Op-amp slew rate: ~1us
            // - Digital logic gate: 1-10ns

            // For now, estimate based on circuit complexity
            latency = 5e-6; // 5 microseconds default

            return latency;
        }

        /// <summary>
        /// Calculate ADC conversion latency
        /// </summary>
        private double CalculateADCLatency()
        {
            // ATmega328P ADC timing:
            // - Clock: 125 kHz (prescaler /128 from 16 MHz)
            // - Conversion: 13 ADC clock cycles = 104 us
            // - First conversion: 25 cycles = 200 us

            double adcClockPeriod = 1.0 / 125000.0; // 125 kHz ADC clock
            double conversionCycles = 13;
            double adcLatency = adcClockPeriod * conversionCycles;

            return adcLatency; // ~104 microseconds
        }

        /// <summary>
        /// Calculate UART transmission latency
        /// </summary>
        private double CalculateUARTLatency()
        {
            // UART timing (9600 baud typical):
            // - Baud rate: 9600 bits/sec
            // - Bit time: 104.17 us
            // - Frame: 1 start + 8 data + 1 stop = 10 bits = 1041.7 us per byte

            double baudRate = 9600.0;
            double bitsPerFrame = 10.0; // 1 start + 8 data + 1 stop
            double uartLatency = bitsPerFrame / baudRate;

            return uartLatency; // ~1.04 milliseconds per byte
        }

        /// <summary>
        /// Set VirtualMcu handle for native integration
        /// </summary>
        public void SetVirtualMcuHandle(IntPtr handle)
        {
            _virtualMcuHandle = handle;
            _lastCycleCount = 0;
            Debug.Log($"[CircuitLatencyAdapter] VirtualMcu handle set: 0x{handle:X}");
        }

        /// <summary>
        /// Manual trigger for latency capture (for synchronous simulation)
        /// </summary>
        public void TriggerLatencyCapture()
        {
            CaptureAndPropagateLatency();
        }

        /// <summary>
        /// Get current accumulated latency
        /// </summary>
        public double GetCurrentLatency()
        {
            return _accumulatedLatency;
        }

        /// <summary>
        /// Get total cycles processed
        /// </summary>
        public long GetTotalCyclesProcessed()
        {
            return _totalCyclesProcessed;
        }

        /// <summary>
        /// Reset all timing counters
        /// </summary>
        public void Reset()
        {
            _accumulatedLatency = 0.0;
            _totalCyclesProcessed = 0;
            _lastCycleCount = 0;
            _timeSinceLastUpdate = 0f;
            Metrics = new LatencyMetrics();

            Debug.Log("[CircuitLatencyAdapter] Reset complete");
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUILayout.BeginArea(new Rect(10, 10, 400, 120));
                GUILayout.BeginVertical("box");
                GUILayout.Label("Circuit Latency Adapter");
                GUILayout.Label($"Total Latency: {_accumulatedLatency * 1000:F3}ms");
                GUILayout.Label($"VirtualMcu: {Metrics.VirtualMcuLatencySeconds * 1e6:F1}µs");
                GUILayout.Label($"Circuit: {Metrics.CircuitPropagationLatencySeconds * 1e6:F1}µs");
                GUILayout.Label($"ADC: {Metrics.ADCLatencySeconds * 1e6:F1}µs");
                GUILayout.Label($"UART: {Metrics.UARTLatencySeconds * 1e6:F1}µs");
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }
    }

    /// <summary>
    /// Latency metrics for analysis
    /// </summary>
    [Serializable]
    public class LatencyMetrics
    {
        public long VirtualMcuCycles;
        public double VirtualMcuLatencySeconds;
        public double CircuitPropagationLatencySeconds;
        public double ADCLatencySeconds;
        public double UARTLatencySeconds;
        public double TotalLatencySeconds;
        public long UpdateCount;

        public double GetTotalLatencyMicroseconds()
        {
            return TotalLatencySeconds * 1e6;
        }

        public double GetTotalLatencyMilliseconds()
        {
            return TotalLatencySeconds * 1000;
        }
    }
}
