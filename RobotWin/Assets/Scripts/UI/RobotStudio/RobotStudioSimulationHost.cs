using System;
using System.Collections.Generic;
using UnityEngine;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.Runtime;

namespace RobotTwin.UI
{
    public class RobotStudioSimulationHost : MonoBehaviour
    {
        public bool IsRunning => _isRunning;
        public RobotStudioPackage Package => _package;
        public TelemetryFrame LastTelemetry => _telemetry;

        private bool _isRunning;
        private RobotStudioPackage _package = new RobotStudioPackage();
        private double _simTime;

        // Placeholder V1 telemetry: stress/temperature/force + data-loss counter.
        // This is intentionally simple and deterministic-ish, so visuals can be wired up
        // before NativeEngine/CoreSim coupling is implemented.
        private readonly TelemetryFrame _telemetry = new TelemetryFrame();
        private long _telemetryTick;
        private long _dataLossDrops;
        private readonly Dictionary<string, double> _lastWireStressRatio =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public void LoadPackage(RobotStudioPackage package)
        {
            _package = package ?? new RobotStudioPackage();
            InitializeSimulation();
        }

        public void StartSimulation()
        {
            if (_isRunning) return;
            _isRunning = true;
        }

        public void StopSimulation()
        {
            _isRunning = false;
        }

        private void Update()
        {
            if (!_isRunning) return;
            Step(Time.deltaTime);
        }

        private void InitializeSimulation()
        {
            // Stub: Hook NativeEngine/CoreSim + FirmwareEngine here.
            // Intentionally minimal for V1.
        }

        private void Step(float deltaTime)
        {
            _simTime += deltaTime;
            StepEnvironment(deltaTime);
            StepCircuit(deltaTime);
            StepFirmware(deltaTime);
            StepCoupling(deltaTime);

            BuildPlaceholderTelemetry(deltaTime);
        }

        private void StepEnvironment(float deltaTime)
        {
            _ = deltaTime;
        }

        private void StepCircuit(float deltaTime)
        {
            _ = deltaTime;
        }

        private void StepFirmware(float deltaTime)
        {
            _ = deltaTime;
        }

        private void StepCoupling(float deltaTime)
        {
            _ = deltaTime;
        }

        private void BuildPlaceholderTelemetry(float deltaTime)
        {
            _telemetry.TickIndex = _telemetryTick++;
            _telemetry.TimeSeconds = (float)_simTime;
            _telemetry.Signals.Clear();
            _telemetry.ValidationMessages.Clear();

            var env = _package?.Environment ?? new EnvironmentSpec();
            var assembly = _package?.Assembly;
            double envTemp = env.TemperatureC;
            double g = env.Gravity > 0 ? env.Gravity : 9.81;
            double vibA = Math.Max(0.0, env.VibrationAmplitude);
            double vibF = Math.Max(0.0, env.VibrationFrequencyHz);
            double omega = 2.0 * Math.PI * vibF;
            double vibAccel = vibA * omega * omega * Math.Sin(omega * _simTime);

            _telemetry.Signals["SIM:time"] = _simTime;
            _telemetry.Signals["ENV:T"] = envTemp;
            _telemetry.Signals["ENV:vib_a"] = vibA;
            _telemetry.Signals["ENV:vib_f"] = vibF;

            if (assembly?.Parts != null)
            {
                int index = 0;
                foreach (var part in assembly.Parts)
                {
                    index++;
                    if (part == null) continue;
                    string id = !string.IsNullOrWhiteSpace(part.InstanceId)
                        ? part.InstanceId
                        : (!string.IsNullOrWhiteSpace(part.ComponentType) ? $"{part.ComponentType}_{index}" : $"Part_{index}");

                    double mass = part.MassKg > 0 ? part.MassKg : 0.15;

                    // Super crude effective contact area estimate (avoid div-by-zero).
                    double sx = Math.Abs(part.Scale.X);
                    double sz = Math.Abs(part.Scale.Z);
                    // Treat scale as "centimeters-ish" for now: map into m^2 with a small factor.
                    double area = Math.Max(1e-4, (sx * sz) * 0.0004);

                    // Force magnitude: weight + vibration-induced acceleration.
                    double forceN = mass * (g + Math.Abs(vibAccel));
                    double stressPa = forceN / area;

                    // Temperature rises with vibration and stress (just to drive visuals).
                    double tempC = envTemp + 0.02 * stressPa / 1e5 + 6.0 * vibA * (1.0 + 0.2 * Math.Abs(Math.Sin(_simTime + index)));

                    _telemetry.Signals[$"PART:{id}:F"] = forceN;
                    _telemetry.Signals[$"PART:{id}:S"] = stressPa;
                    _telemetry.Signals[$"PART:{id}:T"] = tempC;
                }
            }

            if (assembly?.Wires != null)
            {
                foreach (var wire in assembly.Wires)
                {
                    if (wire == null || string.IsNullOrWhiteSpace(wire.Id)) continue;
                    // Fake wire stress ratio based on vibration; MaxStress is treated as a limit.
                    double maxStress = wire.MaxStress > 0 ? wire.MaxStress : 2.0e8;
                    double estStress = (Math.Abs(vibAccel) + 0.2 * g) * 5.0e6 * (1.0 + 0.05 * Math.Min(10.0, wire.LengthMeters));
                    double ratio = maxStress > 0 ? (estStress / maxStress) : 0.0;
                    _telemetry.Signals[$"WIRE:{wire.Id}:SR"] = ratio;

                    bool overstress = ratio > 1.0;
                    if (_lastWireStressRatio.TryGetValue(wire.Id, out var lastRatio))
                    {
                        // Count "drops" on rising edge into overstress to avoid spamming.
                        if (lastRatio <= 1.0 && overstress)
                        {
                            _dataLossDrops++;
                            _telemetry.ValidationMessages.Add($"Wire overstress: {wire.Id} (SR={ratio:F2})");
                        }
                    }
                    else if (overstress)
                    {
                        _dataLossDrops++;
                        _telemetry.ValidationMessages.Add($"Wire overstress: {wire.Id} (SR={ratio:F2})");
                    }
                    _lastWireStressRatio[wire.Id] = ratio;
                }
            }

            _telemetry.Signals["MECH:drops"] = _dataLossDrops;
            _telemetry.Signals["MECH:dt"] = deltaTime;
        }
    }
}
