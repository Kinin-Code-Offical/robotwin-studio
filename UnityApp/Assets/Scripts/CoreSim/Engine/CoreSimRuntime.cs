using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Runtime;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.CoreSim.Engine
{
    public class CoreSimRuntime
    {
        private const double DefaultLedForwardV = 2.0;
        private const double DefaultLedOnResistance = 15.0;
        private const double HighResistance = 1e9;
        private const double ShortCircuitCurrentA = 2.0;

        private readonly Dictionary<string, string> _pinToNet = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _netToNode = new Dictionary<string, int>();
        private readonly HashSet<string> _groundNets = new HashSet<string>();
        private bool _hasExplicitGround;

        public TelemetryFrame Step(
            CircuitSpec spec,
            Dictionary<string, float> pinVoltages,
            Dictionary<string, List<PinState>> pinStatesByComponent,
            Dictionary<string, double> pullupResistances,
            float dtSeconds)
        {
            var frame = new TelemetryFrame
            {
                TimeSeconds = dtSeconds
            };

            if (spec == null || spec.Components == null || spec.Nets == null || spec.Nets.Count == 0)
            {
                frame.ValidationMessages.Add("No circuit nets available.");
                return frame;
            }

            BuildNetIndex(spec, frame);
            BuildPinToNetMap(spec);

            var resistors = new List<ResistorElement>();
            var diodes = new List<DiodeElement>();
            var voltageSources = new List<VoltageSourceElement>();

            BuildComponentElements(spec, pinVoltages, pinStatesByComponent, pullupResistances, resistors, diodes, voltageSources, frame);
            var poweredNets = ValidateConnectivity(spec, voltageSources, frame);
            ValidateInputPins(pinStatesByComponent, poweredNets, frame);

            var solution = SolveCircuit(resistors, diodes, voltageSources, frame);
            if (solution == null)
            {
                return frame;
            }

            var nodeVoltages = ExtractNodeVoltages(solution, voltageSources.Count);
            PopulateTelemetry(frame, spec, resistors, diodes, voltageSources, nodeVoltages, solution);

            return frame;
        }

        private void BuildNetIndex(CircuitSpec spec, TelemetryFrame frame)
        {
            _netToNode.Clear();
            _groundNets.Clear();
            _hasExplicitGround = false;

            foreach (var net in spec.Nets)
            {
                if (string.IsNullOrWhiteSpace(net.Id) || net.Nodes == null) continue;
                if (_netToNode.ContainsKey(net.Id)) continue;

                foreach (var node in net.Nodes)
                {
                    var pin = GetPinName(node);
                    if (IsGroundPin(pin))
                    {
                        _groundNets.Add(net.Id);
                        _hasExplicitGround = true;
                    }
                }
            }

            if (_groundNets.Count == 0)
            {
                var firstNet = spec.Nets.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.Id));
                if (firstNet != null)
                {
                    _groundNets.Add(firstNet.Id);
                    frame.ValidationMessages.Add("Missing GND reference (no net contains GND pins).");
                    frame.ValidationMessages.Add($"Disconnected GND. Using net '{firstNet.Id}' as reference.");
                }
            }

            int index = 0;
            foreach (var net in spec.Nets)
            {
                if (string.IsNullOrWhiteSpace(net.Id)) continue;
                if (_groundNets.Contains(net.Id)) continue;
                if (_netToNode.ContainsKey(net.Id)) continue;
                _netToNode[net.Id] = index++;
            }
        }

        private void BuildPinToNetMap(CircuitSpec spec)
        {
            _pinToNet.Clear();
            foreach (var net in spec.Nets)
            {
                if (string.IsNullOrWhiteSpace(net.Id) || net.Nodes == null) continue;
                foreach (var node in net.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(node)) continue;
                    _pinToNet[node] = net.Id;
                }
            }
        }

        private HashSet<string> ValidateConnectivity(CircuitSpec spec, List<VoltageSourceElement> voltageSources, TelemetryFrame frame)
        {
            var powered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (spec.Nets == null) return powered;
            foreach (var source in voltageSources)
            {
                if (!string.IsNullOrWhiteSpace(source.NetPlus)) powered.Add(source.NetPlus);
                if (!string.IsNullOrWhiteSpace(source.NetMinus)) powered.Add(source.NetMinus);
            }
            foreach (var gnd in _groundNets) powered.Add(gnd);

            foreach (var net in spec.Nets)
            {
                if (string.IsNullOrWhiteSpace(net.Id)) continue;
                if (powered.Contains(net.Id)) continue;
                frame.ValidationMessages.Add($"Floating net '{net.Id}'.");
            }
            return powered;
        }

        private void BuildComponentElements(
            CircuitSpec spec,
            Dictionary<string, float> pinVoltages,
            Dictionary<string, List<PinState>> pinStatesByComponent,
            Dictionary<string, double> pullupResistances,
            List<ResistorElement> resistors,
            List<DiodeElement> diodes,
            List<VoltageSourceElement> voltageSources,
            TelemetryFrame frame)
        {
            foreach (var comp in spec.Components)
            {
                if (comp == null || string.IsNullOrWhiteSpace(comp.Type)) continue;
                switch (comp.Type)
                {
                    case "Resistor":
                        AddTwoPinResistor(comp, "A", "B", resistors, frame);
                        break;
                    case "Capacitor":
                        AddTwoPinResistor(comp, "A", "B", resistors, frame, HighResistance);
                        break;
                    case "LED":
                        AddLed(comp, diodes, frame);
                        break;
                    case "DCMotor":
                        AddTwoPinResistor(comp, "A", "B", resistors, frame, ParseResistance(comp, 10.0));
                        break;
                    case "Battery":
                        AddVoltageSource(comp, "+", "-", ParseVoltage(comp, 9.0), voltageSources, frame);
                        break;
                    case "ArduinoUno":
                        AddArduinoSources(comp, pinVoltages, voltageSources, frame);
                        AddArduinoPullups(comp, pinStatesByComponent, pullupResistances, resistors, frame);
                        break;
                }
            }
        }

        private void AddTwoPinResistor(
            ComponentSpec comp,
            string pinA,
            string pinB,
            List<ResistorElement> resistors,
            TelemetryFrame frame,
            double resistanceOverride = -1.0)
        {
            var netA = GetNetFor(comp.Id, pinA);
            var netB = GetNetFor(comp.Id, pinB);
            if (!IsConnected(netA) || !IsConnected(netB))
            {
                frame.ValidationMessages.Add($"Component '{comp.Id}' missing pin connection.");
                return;
            }

            double resistance = resistanceOverride > 0 ? resistanceOverride : ParseResistance(comp, 1000.0);
            resistors.Add(new ResistorElement(comp.Id, netA, netB, resistance));
        }

        private void AddLed(ComponentSpec comp, List<DiodeElement> diodes, TelemetryFrame frame)
        {
            var anodeNet = GetNetFor(comp.Id, "Anode");
            var cathodeNet = GetNetFor(comp.Id, "Cathode");
            if (!IsConnected(anodeNet) || !IsConnected(cathodeNet))
            {
                frame.ValidationMessages.Add($"LED '{comp.Id}' missing pin connection.");
                return;
            }

            double forwardV = ParseVoltage(comp, DefaultLedForwardV, "forwardV");
            double maxCurrent = ParseCurrent(comp, 0.02, "If_max");
            if (maxCurrent <= 0) maxCurrent = ParseCurrent(comp, 0.02, "current");
            diodes.Add(new DiodeElement(comp.Id, anodeNet, cathodeNet, forwardV, maxCurrent));
        }

        private void AddVoltageSource(
            ComponentSpec comp,
            string pinPlus,
            string pinMinus,
            double voltage,
            List<VoltageSourceElement> voltageSources,
            TelemetryFrame frame)
        {
            var netPlus = GetNetFor(comp.Id, pinPlus);
            var netMinus = GetNetFor(comp.Id, pinMinus);
            if (!IsConnected(netPlus) || !IsConnected(netMinus))
            {
                frame.ValidationMessages.Add($"Source '{comp.Id}' missing pin connection.");
                return;
            }

            voltageSources.Add(new VoltageSourceElement(comp.Id, netPlus, netMinus, voltage));
        }

        private void AddArduinoSources(
            ComponentSpec comp,
            Dictionary<string, float> pinVoltages,
            List<VoltageSourceElement> voltageSources,
            TelemetryFrame frame)
        {
            AddArduinoPinSource(comp, "5V", 5.0, voltageSources);
            AddArduinoPinSource(comp, "3V3", 3.3, voltageSources);
            AddArduinoPinSource(comp, "IOREF", 5.0, voltageSources);

            if (pinVoltages == null) return;

            foreach (var kvp in pinVoltages)
            {
                if (!kvp.Key.StartsWith(comp.Id + ".", StringComparison.Ordinal)) continue;
                string pin = kvp.Key.Substring(comp.Id.Length + 1);
                if (!IsDigitalPin(pin)) continue;
                AddArduinoPinSource(comp, pin, kvp.Value, voltageSources);
            }
        }

        private void AddArduinoPinSource(ComponentSpec comp, string pin, double voltage, List<VoltageSourceElement> voltageSources)
        {
            var net = GetNetFor(comp.Id, pin);
            if (!IsConnected(net)) return;
            voltageSources.Add(new VoltageSourceElement($"{comp.Id}.{pin}", net, GetGroundNet(), voltage));
        }

        private void AddArduinoPullups(
            ComponentSpec comp,
            Dictionary<string, List<PinState>> pinStatesByComponent,
            Dictionary<string, double> pullupResistances,
            List<ResistorElement> resistors,
            TelemetryFrame frame)
        {
            if (pinStatesByComponent == null || comp == null) return;
            if (!pinStatesByComponent.TryGetValue(comp.Id, out var states) || states == null) return;

            double pullupResistance = 20000.0;
            if (pullupResistances != null && pullupResistances.TryGetValue(comp.Id, out var overrideResistance))
            {
                if (overrideResistance > 0) pullupResistance = overrideResistance;
            }

            var vccNet = GetNetFor(comp.Id, "5V");
            if (!IsConnected(vccNet))
            {
                frame.ValidationMessages.Add($"Pull-up supply missing for '{comp.Id}' (5V not connected).");
                return;
            }

            foreach (var state in states)
            {
                if (state.IsOutput || !state.PullupEnabled) continue;
                var pinNet = GetNetFor(comp.Id, state.Pin);
                if (!IsConnected(pinNet))
                {
                    frame.ValidationMessages.Add($"Pull-up pin '{comp.Id}.{state.Pin}' missing net.");
                    continue;
                }
                resistors.Add(new ResistorElement($"{comp.Id}.{state.Pin}:PULLUP", pinNet, vccNet, pullupResistance));
            }
        }

        private double[] SolveCircuit(
            List<ResistorElement> resistors,
            List<DiodeElement> diodes,
            List<VoltageSourceElement> voltageSources,
            TelemetryFrame frame)
        {
            int nodeCount = _netToNode.Count;
            int sourceCount = voltageSources.Count;
            int size = nodeCount + sourceCount;
            if (size == 0)
            {
                frame.ValidationMessages.Add("No solvable nodes in circuit.");
                return null;
            }

            var diodeStates = new Dictionary<string, double>();
            for (int i = 0; i < 3; i++)
            {
                var matrix = new double[size, size];
                var rhs = new double[size];

                AddResistors(matrix, resistors);
                AddDiodes(matrix, diodes, diodeStates);
                AddVoltageSources(matrix, rhs, voltageSources, nodeCount);

                if (!TrySolve(matrix, rhs, out var solution))
                {
                    frame.ValidationMessages.Add("Circuit solver failed (singular matrix).");
                    ReportSolverFailure(frame, voltageSources);
                    return null;
                }

                UpdateDiodeStates(diodeStates, diodes, voltageSources, solution, nodeCount);
                if (i == 2) return solution;
            }

            return null;
        }

        private void AddResistors(double[,] matrix, List<ResistorElement> resistors)
        {
            foreach (var res in resistors)
            {
                double g = 1.0 / Math.Max(res.Resistance, 1e-6);
                int a = GetNodeIndex(res.NetA);
                int b = GetNodeIndex(res.NetB);

                if (a != -1) matrix[a, a] += g;
                if (b != -1) matrix[b, b] += g;
                if (a != -1 && b != -1)
                {
                    matrix[a, b] -= g;
                    matrix[b, a] -= g;
                }
            }
        }

        private void AddDiodes(double[,] matrix, List<DiodeElement> diodes, Dictionary<string, double> diodeStates)
        {
            foreach (var diode in diodes)
            {
                if (!diodeStates.TryGetValue(diode.Id, out var resistance))
                {
                    resistance = HighResistance;
                }

                double g = 1.0 / Math.Max(resistance, 1e-6);
                int a = GetNodeIndex(diode.AnodeNet);
                int c = GetNodeIndex(diode.CathodeNet);

                if (a != -1) matrix[a, a] += g;
                if (c != -1) matrix[c, c] += g;
                if (a != -1 && c != -1)
                {
                    matrix[a, c] -= g;
                    matrix[c, a] -= g;
                }
            }
        }

        private void AddVoltageSources(double[,] matrix, double[] rhs, List<VoltageSourceElement> sources, int nodeCount)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                int row = nodeCount + i;
                int a = GetNodeIndex(sources[i].NetPlus);
                int b = GetNodeIndex(sources[i].NetMinus);

                if (a != -1)
                {
                    matrix[a, row] += 1.0;
                    matrix[row, a] += 1.0;
                }
                if (b != -1)
                {
                    matrix[b, row] -= 1.0;
                    matrix[row, b] -= 1.0;
                }

                rhs[row] = sources[i].Voltage;
            }
        }

        private void UpdateDiodeStates(
            Dictionary<string, double> diodeStates,
            List<DiodeElement> diodes,
            List<VoltageSourceElement> sources,
            double[] solution,
            int nodeCount)
        {
            foreach (var diode in diodes)
            {
                var vA = GetNodeVoltage(diode.AnodeNet, solution, nodeCount);
                var vC = GetNodeVoltage(diode.CathodeNet, solution, nodeCount);
                double vDiff = vA - vC;
                diodeStates[diode.Id] = vDiff >= diode.ForwardVoltage ? DefaultLedOnResistance : HighResistance;
            }
        }

        private Dictionary<string, double> ExtractNodeVoltages(double[] solution, int sourceCount)
        {
            var voltages = new Dictionary<string, double>();
            foreach (var kvp in _netToNode)
            {
                if (kvp.Value < 0 || kvp.Value >= solution.Length - sourceCount) continue;
                voltages[kvp.Key] = solution[kvp.Value];
            }
            foreach (var ground in _groundNets)
            {
                voltages[ground] = 0.0;
            }
            return voltages;
        }

        private void PopulateTelemetry(
            TelemetryFrame frame,
            CircuitSpec spec,
            List<ResistorElement> resistors,
            List<DiodeElement> diodes,
            List<VoltageSourceElement> sources,
            Dictionary<string, double> voltages,
            double[] solution)
        {
            foreach (var net in voltages)
            {
                frame.Signals[$"NET:{net.Key}"] = net.Value;
            }

            foreach (var res in resistors)
            {
                double vA = GetNodeVoltage(res.NetA, voltages);
                double vB = GetNodeVoltage(res.NetB, voltages);
                double current = (vA - vB) / Math.Max(res.Resistance, 1e-6);
                frame.Signals[$"COMP:{res.Id}:I"] = current;
                frame.Signals[$"COMP:{res.Id}:P"] = current * current * res.Resistance;
            }

            foreach (var diode in diodes)
            {
                double vA = GetNodeVoltage(diode.AnodeNet, voltages);
                double vB = GetNodeVoltage(diode.CathodeNet, voltages);
                double vDiff = vA - vB;
                double resistance = vDiff >= diode.ForwardVoltage ? DefaultLedOnResistance : HighResistance;
                double current = vDiff / Math.Max(resistance, 1e-6);
                frame.Signals[$"COMP:{diode.Id}:I"] = current;
                frame.Signals[$"COMP:{diode.Id}:P"] = Math.Abs(current * vDiff);
                if (diode.MaxCurrent > 0 && current > diode.MaxCurrent)
                {
                    frame.ValidationMessages.Add($"Component Blown: {diode.Id} overcurrent {current * 1000.0:F1}mA");
                }
            }

            int nodeCount = _netToNode.Count;
            for (int i = 0; i < sources.Count; i++)
            {
                int idx = nodeCount + i;
                if (idx >= 0 && idx < solution.Length)
                {
                    double current = solution[idx];
                    frame.Signals[$"SRC:{sources[i].Id}:I"] = current;
                    if (Math.Abs(current) > ShortCircuitCurrentA)
                    {
                        frame.ValidationMessages.Add($"High current on {sources[i].Id}: {current:F2}A");
                    }
                }
            }

            if (spec.Nets != null)
            {
                foreach (var net in spec.Nets)
                {
                    if (net.Nodes == null || net.Nodes.Count < 2)
                    {
                        frame.ValidationMessages.Add($"Net '{net.Id}' has insufficient nodes.");
                    }
                }
            }
        }

        private void ValidateInputPins(
            Dictionary<string, List<PinState>> pinStatesByComponent,
            HashSet<string> poweredNets,
            TelemetryFrame frame)
        {
            if (pinStatesByComponent == null) return;
            foreach (var kvp in pinStatesByComponent)
            {
                string compId = kvp.Key;
                if (string.IsNullOrWhiteSpace(compId)) continue;
                var states = kvp.Value;
                if (states == null) continue;

                foreach (var state in states)
                {
                    if (state.IsOutput) continue;
                    var net = GetNetFor(compId, state.Pin);
                    if (!IsConnected(net))
                    {
                        frame.ValidationMessages.Add($"Floating input '{compId}.{state.Pin}' (unconnected).");
                        continue;
                    }
                    if (!state.PullupEnabled && poweredNets != null && !poweredNets.Contains(net))
                    {
                        frame.ValidationMessages.Add($"Floating input '{compId}.{state.Pin}' on net '{net}'.");
                    }
                }
            }
        }

        private void ReportSolverFailure(TelemetryFrame frame, List<VoltageSourceElement> sources)
        {
            if (!_hasExplicitGround)
            {
                frame.ValidationMessages.Add("Solver error: missing GND reference (connect U1.GND1/GND2/GND3 to a net).");
            }

            if (!HasSupplySource(sources))
            {
                frame.ValidationMessages.Add("Solver error: missing VCC supply (connect U1.5V/3V3/IOREF or Battery.+).");
            }
        }

        private bool HasSupplySource(List<VoltageSourceElement> sources)
        {
            if (sources == null) return false;
            foreach (var source in sources)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Id)) continue;
                if (source.Id.EndsWith(".5V", StringComparison.OrdinalIgnoreCase)) return true;
                if (source.Id.EndsWith(".3V3", StringComparison.OrdinalIgnoreCase)) return true;
                if (source.Id.EndsWith(".IOREF", StringComparison.OrdinalIgnoreCase)) return true;
                if (source.Id.StartsWith("Battery", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private bool TrySolve(double[,] matrix, double[] rhs, out double[] solution)
        {
            int n = rhs.Length;
            var a = (double[,])matrix.Clone();
            var b = (double[])rhs.Clone();
            solution = new double[n];

            for (int i = 0; i < n; i++)
            {
                int pivot = i;
                double max = Math.Abs(a[i, i]);
                for (int r = i + 1; r < n; r++)
                {
                    double val = Math.Abs(a[r, i]);
                    if (val > max)
                    {
                        max = val;
                        pivot = r;
                    }
                }

                if (max < 1e-9)
                {
                    return false;
                }

                if (pivot != i)
                {
                    SwapRows(a, b, i, pivot);
                }

                double diag = a[i, i];
                for (int c = i; c < n; c++)
                {
                    a[i, c] /= diag;
                }
                b[i] /= diag;

                for (int r = 0; r < n; r++)
                {
                    if (r == i) continue;
                    double factor = a[r, i];
                    if (Math.Abs(factor) < 1e-12) continue;
                    for (int c = i; c < n; c++)
                    {
                        a[r, c] -= factor * a[i, c];
                    }
                    b[r] -= factor * b[i];
                }
            }

            for (int i = 0; i < n; i++)
            {
                solution[i] = b[i];
            }

            return true;
        }

        private void SwapRows(double[,] matrix, double[] rhs, int a, int b)
        {
            int cols = matrix.GetLength(1);
            for (int c = 0; c < cols; c++)
            {
                double tmp = matrix[a, c];
                matrix[a, c] = matrix[b, c];
                matrix[b, c] = tmp;
            }

            double rhsTmp = rhs[a];
            rhs[a] = rhs[b];
            rhs[b] = rhsTmp;
        }

        private string GetNetFor(string componentId, string pin)
        {
            string key = $"{componentId}.{pin}";
            return _pinToNet.TryGetValue(key, out var net) ? net : string.Empty;
        }

        private int GetNodeIndex(string netId)
        {
            if (string.IsNullOrWhiteSpace(netId)) return -1;
            if (_groundNets.Contains(netId)) return -1;
            return _netToNode.TryGetValue(netId, out var idx) ? idx : -1;
        }

        private double GetNodeVoltage(string netId, Dictionary<string, double> voltages)
        {
            return voltages.TryGetValue(netId, out var v) ? v : 0.0;
        }

        private double GetNodeVoltage(string netId, double[] solution, int nodeCount)
        {
            int idx = GetNodeIndex(netId);
            if (idx < 0 || idx >= nodeCount) return 0.0;
            return solution[idx];
        }

        private string GetGroundNet()
        {
            return _groundNets.FirstOrDefault() ?? string.Empty;
        }

        private bool IsConnected(string netId)
        {
            return !string.IsNullOrWhiteSpace(netId);
        }

        private static string GetPinName(string node)
        {
            if (string.IsNullOrWhiteSpace(node)) return string.Empty;
            int dot = node.IndexOf('.');
            if (dot < 0 || dot == node.Length - 1) return string.Empty;
            return node.Substring(dot + 1);
        }

        private static bool IsGroundPin(string pin)
        {
            return pin.StartsWith("GND", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDigitalPin(string pin)
        {
            return pin.StartsWith("D", StringComparison.OrdinalIgnoreCase);
        }

        private static double ParseResistance(ComponentSpec comp, double fallback)
        {
            if (comp?.Properties == null) return fallback;
            if (comp.Properties.TryGetValue("resistance", out var raw) ||
                comp.Properties.TryGetValue("resistanceOhms", out raw))
            {
                if (TryParseValue(raw, out var val))
                {
                    return val;
                }
            }
            return fallback;
        }

        private static double ParseVoltage(ComponentSpec comp, double fallback, string key = "voltage")
        {
            if (comp?.Properties == null) return fallback;
            if (comp.Properties.TryGetValue(key, out var raw))
            {
                if (TryParseValue(raw, out var val))
                {
                    return val;
                }
            }
            return fallback;
        }

        private static double ParseCurrent(ComponentSpec comp, double fallback, string key)
        {
            if (comp?.Properties == null) return fallback;
            if (comp.Properties.TryGetValue(key, out var raw))
            {
                if (TryParseCurrent(raw, out var val))
                {
                    return val;
                }
            }
            return fallback;
        }

        private static bool TryParseCurrent(string raw, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().ToLowerInvariant().Replace(" ", string.Empty);
            double multiplier = 1.0;
            if (s.EndsWith("ma", StringComparison.Ordinal))
            {
                multiplier = 1e-3;
                s = s.Substring(0, s.Length - 2);
            }
            else if (s.EndsWith("a", StringComparison.Ordinal))
            {
                multiplier = 1.0;
                s = s.Substring(0, s.Length - 1);
            }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseValue))
            {
                return false;
            }
            value = baseValue * multiplier;
            return true;
        }
        private static bool TryParseValue(string raw, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().ToLowerInvariant();
            s = s.Replace("ohm", string.Empty).Replace("v", string.Empty).Replace("a", string.Empty);
            s = s.Replace(" ", string.Empty);

            double multiplier = 1.0;
            if (s.EndsWith("k", StringComparison.Ordinal))
            {
                multiplier = 1e3;
                s = s.Substring(0, s.Length - 1);
            }
            else if (s.EndsWith("m", StringComparison.Ordinal))
            {
                multiplier = 1e-3;
                s = s.Substring(0, s.Length - 1);
            }
            else if (s.EndsWith("u", StringComparison.Ordinal))
            {
                multiplier = 1e-6;
                s = s.Substring(0, s.Length - 1);
            }
            else if (s.EndsWith("n", StringComparison.Ordinal))
            {
                multiplier = 1e-9;
                s = s.Substring(0, s.Length - 1);
            }
            else if (s.EndsWith("meg", StringComparison.Ordinal))
            {
                multiplier = 1e6;
                s = s.Substring(0, s.Length - 3);
            }

            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseValue))
            {
                return false;
            }

            value = baseValue * multiplier;
            return true;
        }

        private class ResistorElement
        {
            public string Id { get; }
            public string NetA { get; }
            public string NetB { get; }
            public double Resistance { get; }

            public ResistorElement(string id, string netA, string netB, double resistance)
            {
                Id = id;
                NetA = netA;
                NetB = netB;
                Resistance = resistance;
            }
        }

        private class DiodeElement
        {
            public string Id { get; }
            public string AnodeNet { get; }
            public string CathodeNet { get; }
            public double ForwardVoltage { get; }
            public double MaxCurrent { get; }

            public DiodeElement(string id, string anodeNet, string cathodeNet, double forwardVoltage, double maxCurrent)
            {
                Id = id;
                AnodeNet = anodeNet;
                CathodeNet = cathodeNet;
                ForwardVoltage = forwardVoltage;
                MaxCurrent = maxCurrent;
            }
        }

        private class VoltageSourceElement
        {
            public string Id { get; }
            public string NetPlus { get; }
            public string NetMinus { get; }
            public double Voltage { get; }

            public VoltageSourceElement(string id, string netPlus, string netMinus, double voltage)
            {
                Id = id;
                NetPlus = netPlus;
                NetMinus = netMinus;
                Voltage = voltage;
            }
        }
    }
}
