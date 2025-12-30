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
        private const double DefaultSwitchClosedResistance = 0.05;
        private const double ShortCircuitCurrentA = 2.0;
        private const double DefaultContactResistance = 0.02;
        private const double DefaultAmbientTempC = 25.0;
        private const double DefaultResistorTempCoeff = 0.0004;
        private const double DefaultResistorThermalResistance = 150.0;
        private const double DefaultResistorThermalMass = 0.5;
        private const double DefaultLedThermalResistance = 200.0;
        private const double DefaultLedThermalMass = 0.2;
        private const double DefaultDiodeIs = 1e-12;
        private const double DefaultDiodeIdeality = 2.0;
        private const double DefaultDiodeThermalVoltage = 0.02585;
        private const double DefaultBatteryCapacityAh = 1.5;
        private const double DefaultBatteryInternalResistance = 0.2;
        private const double DefaultBatteryVoltageMin = 6.0;
        private const double DefaultBatteryVoltageMax = 9.0;

        private readonly Dictionary<string, string> _pinToNet =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _netToNode = new Dictionary<string, int>();
        private readonly HashSet<string> _groundNets = new HashSet<string>();
        private bool _hasExplicitGround;
        private int _nextVirtualNodeIndex;
        private readonly HashSet<string> _virtualNets = new HashSet<string>();
        private readonly Dictionary<string, ThermalState> _resistorThermal = new Dictionary<string, ThermalState>();
        private readonly Dictionary<string, ThermalState> _ledThermal = new Dictionary<string, ThermalState>();
        private readonly Dictionary<string, BatteryState> _batteryStates = new Dictionary<string, BatteryState>();
        private readonly Dictionary<string, string> _usbVirtualSupplyNets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public TelemetryFrame Step(
            CircuitSpec spec,
            Dictionary<string, float> pinVoltages,
            Dictionary<string, List<PinState>> pinStatesByComponent,
            Dictionary<string, double> pullupResistances,
            float dtSeconds,
            Dictionary<string, bool> boardPowerById = null,
            Dictionary<string, bool> usbConnectedById = null)
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

            BuildComponentElements(spec, pinVoltages, pinStatesByComponent, pullupResistances, resistors, diodes, voltageSources, frame, boardPowerById, usbConnectedById);
            var poweredNets = ValidateConnectivity(spec, voltageSources, frame);
            ValidateInputPins(pinStatesByComponent, poweredNets, frame);

            var solution = SolveCircuit(resistors, diodes, voltageSources, frame);
            if (solution == null)
            {
                return frame;
            }

            var nodeVoltages = ExtractNodeVoltages(solution, voltageSources.Count);
            PopulateTelemetry(frame, spec, resistors, diodes, voltageSources, nodeVoltages, solution, dtSeconds);

            return frame;
        }

        private void BuildNetIndex(CircuitSpec spec, TelemetryFrame frame)
        {
            _netToNode.Clear();
            _groundNets.Clear();
            _hasExplicitGround = false;
            _virtualNets.Clear();
            _nextVirtualNodeIndex = 0;

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
            _nextVirtualNodeIndex = index;
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
            TelemetryFrame frame,
            Dictionary<string, bool> boardPowerById,
            Dictionary<string, bool> usbConnectedById)
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
                        AddTwoPinResistor(comp, "A", "B", resistors, frame, HighResistance, false);
                        break;
                    case "LED":
                        AddLed(comp, diodes, frame);
                        break;
                    case "DCMotor":
                        AddTwoPinResistor(comp, "A", "B", resistors, frame, ParseResistance(comp, 10.0));
                        break;
                    case "Battery":
                        AddBattery(comp, voltageSources, frame);
                        break;
                    case "Button":
                    case "Switch":
                        AddButton(comp, resistors, frame);
                        break;
                    case "ArduinoUno":
                    case "ArduinoNano":
                    case "ArduinoProMini":
                        AddArduinoSources(comp, pinVoltages, voltageSources, frame, boardPowerById, usbConnectedById);
                        AddArduinoPullups(comp, pinStatesByComponent, pullupResistances, resistors, frame, usbConnectedById);
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
            double resistanceOverride = -1.0,
            bool trackThermal = true)
        {
            var netA = GetNetFor(comp.Id, pinA);
            var netB = GetNetFor(comp.Id, pinB);
            if (!IsConnected(netA) || !IsConnected(netB))
            {
                frame.ValidationMessages.Add($"Component '{comp.Id}' missing pin connection.");
                return;
            }

            double baseResistance = resistanceOverride > 0 ? resistanceOverride : ParseResistance(comp, 1000.0);
            double contactResistance = TryGetDouble(comp, "contactResistance", out var contact) ? contact : DefaultContactResistance;
            ThermalState thermal = null;
            double resistance = baseResistance;
            if (trackThermal)
            {
                thermal = GetOrCreateThermalState(_resistorThermal, comp, DefaultResistorTempCoeff, DefaultResistorThermalResistance, DefaultResistorThermalMass);
                resistance = ApplyTempCoeff(baseResistance, thermal);
            }
            resistance += contactResistance * 2.0;
            resistors.Add(new ResistorElement(comp.Id, netA, netB, resistance, baseResistance, contactResistance, thermal, trackThermal));
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
            double saturationCurrent = TryGetDouble(comp, "Is", out var isVal) ? isVal : DefaultDiodeIs;
            double ideality = TryGetDouble(comp, "ideality", out var nVal) ? nVal : DefaultDiodeIdeality;
            if (TryGetDouble(comp, "n", out var nAlt)) ideality = nAlt;
            double seriesResistance = TryGetDouble(comp, "seriesResistance", out var rsVal) ? rsVal : 0.0;
            double contactResistance = TryGetDouble(comp, "contactResistance", out var contact) ? contact : DefaultContactResistance;
            var thermal = GetOrCreateThermalState(_ledThermal, comp, 0.0, DefaultLedThermalResistance, DefaultLedThermalMass);

            diodes.Add(new DiodeElement(
                comp.Id,
                anodeNet,
                cathodeNet,
                forwardV,
                maxCurrent,
                saturationCurrent,
                ideality,
                DefaultDiodeThermalVoltage,
                seriesResistance,
                contactResistance,
                thermal,
                true));
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

            voltageSources.Add(new VoltageSourceElement(comp.Id, netPlus, netMinus, voltage, false, null));
        }

        private void AddBattery(
            ComponentSpec comp,
            List<VoltageSourceElement> voltageSources,
            TelemetryFrame frame)
        {
            var netPlus = GetNetFor(comp.Id, "+");
            var netMinus = GetNetFor(comp.Id, "-");
            if (!IsConnected(netPlus) || !IsConnected(netMinus))
            {
                frame.ValidationMessages.Add($"Source '{comp.Id}' missing pin connection.");
                return;
            }

            var state = GetOrCreateBatteryState(comp);
            double ocv = ComputeBatteryOpenCircuitVoltage(state);
            double voltage = ocv - state.LastCurrent * state.InternalResistance;
            voltage = Clamp(voltage, state.VoltageMin, state.VoltageMax);
            voltageSources.Add(new VoltageSourceElement(comp.Id, netPlus, netMinus, voltage, true, state));
        }

        private void AddButton(ComponentSpec comp, List<ResistorElement> resistors, TelemetryFrame frame)
        {
            bool closed = IsSwitchClosed(comp);
            double closedResistance = ParseResistance(comp, DefaultSwitchClosedResistance);
            double resistance = closed ? closedResistance : HighResistance;
            AddTwoPinResistor(comp, "A", "B", resistors, frame, resistance, false);
        }

        private void AddArduinoSources(
            ComponentSpec comp,
            Dictionary<string, float> pinVoltages,
            List<VoltageSourceElement> voltageSources,
            TelemetryFrame frame,
            Dictionary<string, bool> boardPowerById,
            Dictionary<string, bool> usbConnectedById)
        {
            if (!IsBoardPowered(comp, boardPowerById))
            {
                return;
            }
            if (IsUsbConnected(comp, usbConnectedById) && !HasAnySupplyNet(comp))
            {
                string usbNet = GetUsbSupplyNet(comp);
                RegisterVirtualNet(usbNet);
                var gndNet = GetGroundNet();
                if (IsConnected(gndNet))
                {
                    voltageSources.Add(new VoltageSourceElement($"{comp.Id}.USB", usbNet, gndNet, 5.0, false, null));
                }
            }
            AddArduinoPinSource(comp, "5V", 5.0, voltageSources);
            AddArduinoPinSource(comp, "3V3", 3.3, voltageSources);
            AddArduinoPinSource(comp, "IOREF", 5.0, voltageSources);
            AddArduinoPinSource(comp, "VCC", 5.0, voltageSources);

            if (pinVoltages == null) return;

            foreach (var kvp in pinVoltages)
            {
                if (!kvp.Key.StartsWith(comp.Id + ".", StringComparison.Ordinal)) continue;
                string pin = kvp.Key.Substring(comp.Id.Length + 1);
                if (!IsDigitalPin(pin)) continue;
                AddArduinoPinSource(comp, pin, kvp.Value, voltageSources);
            }
        }

        private static bool IsBoardPowered(ComponentSpec comp, Dictionary<string, bool> boardPowerById)
        {
            if (boardPowerById == null || comp == null || string.IsNullOrWhiteSpace(comp.Id)) return true;
            return !boardPowerById.TryGetValue(comp.Id, out var powered) || powered;
        }

        private static bool IsUsbConnected(ComponentSpec comp, Dictionary<string, bool> usbConnectedById)
        {
            if (usbConnectedById == null || comp == null || string.IsNullOrWhiteSpace(comp.Id)) return true;
            return !usbConnectedById.TryGetValue(comp.Id, out var connected) || connected;
        }

        private bool HasAnySupplyNet(ComponentSpec comp)
        {
            return IsConnected(GetNetFor(comp.Id, "5V")) ||
                   IsConnected(GetNetFor(comp.Id, "3V3")) ||
                   IsConnected(GetNetFor(comp.Id, "IOREF")) ||
                   IsConnected(GetNetFor(comp.Id, "VCC")) ||
                   IsConnected(GetNetFor(comp.Id, "VIN"));
        }

        private string GetUsbSupplyNet(ComponentSpec comp)
        {
            if (comp == null || string.IsNullOrWhiteSpace(comp.Id)) return "USB5V";
            if (_usbVirtualSupplyNets.TryGetValue(comp.Id, out var netId)) return netId;
            netId = $"USB5V_{comp.Id}";
            _usbVirtualSupplyNets[comp.Id] = netId;
            return netId;
        }

        private void AddArduinoPinSource(ComponentSpec comp, string pin, double voltage, List<VoltageSourceElement> voltageSources)
        {
            var net = GetNetFor(comp.Id, pin);
            if (!IsConnected(net)) return;
            voltageSources.Add(new VoltageSourceElement($"{comp.Id}.{pin}", net, GetGroundNet(), voltage, false, null));
        }

        private void AddArduinoPullups(
            ComponentSpec comp,
            Dictionary<string, List<PinState>> pinStatesByComponent,
            Dictionary<string, double> pullupResistances,
            List<ResistorElement> resistors,
            TelemetryFrame frame,
            Dictionary<string, bool> usbConnectedById)
        {
            if (pinStatesByComponent == null || comp == null) return;
            if (!pinStatesByComponent.TryGetValue(comp.Id, out var states) || states == null) return;

            double pullupResistance = 20000.0;
            if (pullupResistances != null && pullupResistances.TryGetValue(comp.Id, out var overrideResistance))
            {
                if (overrideResistance > 0) pullupResistance = overrideResistance;
            }

            var vccNet = GetNetFor(comp.Id, "5V");
            if (!IsConnected(vccNet) && IsUsbConnected(comp, usbConnectedById))
            {
                vccNet = GetUsbSupplyNet(comp);
                RegisterVirtualNet(vccNet);
            }
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
                resistors.Add(new ResistorElement($"{comp.Id}.{state.Pin}:PULLUP", pinNet, vccNet, pullupResistance, pullupResistance, 0.0, null, false));
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

            var diodeStates = new Dictionary<string, DiodeState>();
            for (int i = 0; i < 3; i++)
            {
                var matrix = new double[size, size];
                var rhs = new double[size];

                AddResistors(matrix, resistors);
                AddDiodes(matrix, rhs, diodes, diodeStates);
                AddVoltageSources(matrix, rhs, voltageSources, nodeCount);

                if (!TrySolve(matrix, rhs, out var solution))
                {
                    frame.ValidationMessages.Add("Circuit solver failed (singular matrix).");
                    ReportSolverFailure(frame, voltageSources);
                    return null;
                }

                UpdateDiodeStates(diodeStates, diodes, solution, nodeCount);
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

        private void AddDiodes(double[,] matrix, double[] rhs, List<DiodeElement> diodes, Dictionary<string, DiodeState> diodeStates)
        {
            foreach (var diode in diodes)
            {
                if (!diodeStates.TryGetValue(diode.Id, out var state))
                {
                    state = new DiodeState { IsOn = false };
                    diodeStates[diode.Id] = state;
                }

                double resistance = state.IsOn ? GetDiodeOnResistance(diode) : HighResistance;
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

                if (state.IsOn)
                {
                    double vf = GetDiodeForwardVoltage(diode);
                    double biasCurrent = -vf / Math.Max(resistance, 1e-6);
                    AddCurrentSource(rhs, diode.AnodeNet, diode.CathodeNet, biasCurrent);
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

        private void AddCurrentSource(double[] rhs, string netA, string netB, double current)
        {
            int a = GetNodeIndex(netA);
            int b = GetNodeIndex(netB);
            if (a != -1) rhs[a] -= current;
            if (b != -1) rhs[b] += current;
        }

        private void UpdateDiodeStates(
            Dictionary<string, DiodeState> diodeStates,
            List<DiodeElement> diodes,
            double[] solution,
            int nodeCount)
        {
            foreach (var diode in diodes)
            {
                var vA = GetNodeVoltage(diode.AnodeNet, solution, nodeCount);
                var vC = GetNodeVoltage(diode.CathodeNet, solution, nodeCount);
                double vDiff = vA - vC;
                double vf = GetDiodeForwardVoltage(diode);
                double onThreshold = vf * 0.95;
                double offThreshold = vf * 0.85;

                if (!diodeStates.TryGetValue(diode.Id, out var state))
                {
                    state = new DiodeState();
                    diodeStates[diode.Id] = state;
                }

                if (state.IsOn)
                {
                    if (vDiff < offThreshold) state.IsOn = false;
                }
                else
                {
                    if (vDiff > onThreshold) state.IsOn = true;
                }
            }
        }

        private double ComputeDiodeCurrent(DiodeElement diode, double vDiff)
        {
            double vf = GetDiodeForwardVoltage(diode);
            if (vDiff <= vf) return 0.0;
            double rOn = GetDiodeOnResistance(diode);
            return (vDiff - vf) / rOn;
        }

        private double GetDiodeOnResistance(DiodeElement diode)
        {
            double rOn = diode.SeriesResistance + diode.ContactResistance;
            if (rOn <= 0.0) rOn = DefaultLedOnResistance;
            return Math.Max(rOn, 0.001);
        }

        private double GetDiodeForwardVoltage(DiodeElement diode)
        {
            double vf = diode.ForwardVoltage;
            if (vf <= 0.0) vf = DefaultLedForwardV;
            return vf;
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
            double[] solution,
            float dtSeconds)
        {
            foreach (var net in voltages)
            {
                frame.Signals[$"NET:{net.Key}"] = net.Value;
            }

            foreach (var res in resistors)
            {
                double vA = GetNodeVoltage(res.NetA, voltages);
                double vB = GetNodeVoltage(res.NetB, voltages);
                double vDiff = vA - vB;
                double current = vDiff / Math.Max(res.Resistance, 1e-6);
                double power = current * current * res.Resistance;
                frame.Signals[$"COMP:{res.Id}:I"] = current;
                frame.Signals[$"COMP:{res.Id}:V"] = vDiff;
                frame.Signals[$"COMP:{res.Id}:P"] = power;
                frame.Signals[$"COMP:{res.Id}:R"] = res.Resistance;

                if (res.TrackThermal && res.Thermal != null)
                {
                    UpdateThermalState(res.Thermal, Math.Abs(power), dtSeconds);
                    frame.Signals[$"COMP:{res.Id}:T"] = res.Thermal.TempC;
                    res.Resistance = ApplyTempCoeff(res.BaseResistance, res.Thermal) + res.ContactResistance * 2.0;
                }
            }

            foreach (var diode in diodes)
            {
                double vA = GetNodeVoltage(diode.AnodeNet, voltages);
                double vB = GetNodeVoltage(diode.CathodeNet, voltages);
                double vDiff = vA - vB;
                double current = ComputeDiodeCurrent(diode, vDiff);
                double power = Math.Abs(current * vDiff);
                frame.Signals[$"COMP:{diode.Id}:I"] = current;
                frame.Signals[$"COMP:{diode.Id}:V"] = vDiff;
                frame.Signals[$"COMP:{diode.Id}:P"] = power;
                if (diode.MaxCurrent > 0)
                {
                    double intensity = Clamp(Math.Abs(current) / diode.MaxCurrent, 0.0, 1.0);
                    frame.Signals[$"COMP:{diode.Id}:L"] = intensity;
                }
                if (diode.TrackThermal && diode.Thermal != null)
                {
                    UpdateThermalState(diode.Thermal, power, dtSeconds);
                    frame.Signals[$"COMP:{diode.Id}:T"] = diode.Thermal.TempC;
                }
                if (diode.MaxCurrent > 0 && Math.Abs(current) > diode.MaxCurrent)
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
                    frame.Signals[$"SRC:{sources[i].Id}:V"] = sources[i].Voltage;
                    frame.Signals[$"SRC:{sources[i].Id}:P"] = current * sources[i].Voltage;
                    if (sources[i].IsBattery && sources[i].Battery != null)
                    {
                        UpdateBatteryState(sources[i].Battery, current, dtSeconds);
                        sources[i].Battery.LastCurrent = current;
                        frame.Signals[$"SRC:{sources[i].Id}:SOC"] = sources[i].Battery.Soc;
                        frame.Signals[$"SRC:{sources[i].Id}:RINT"] = sources[i].Battery.InternalResistance;
                    }
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

        private void RegisterVirtualNet(string netId)
        {
            if (string.IsNullOrWhiteSpace(netId)) return;
            if (_groundNets.Contains(netId)) return;
            if (_netToNode.ContainsKey(netId)) return;
            _netToNode[netId] = _nextVirtualNodeIndex++;
            _virtualNets.Add(netId);
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

        private ThermalState GetOrCreateThermalState(
            Dictionary<string, ThermalState> map,
            ComponentSpec comp,
            double defaultTempCoeff,
            double defaultThermalResistance,
            double defaultThermalMass)
        {
            if (comp == null) return null;
            if (map.TryGetValue(comp.Id, out var state)) return state;

            double ambient = TryGetDouble(comp, "ambientTemp", out var amb) ? amb : DefaultAmbientTempC;
            double tempCoeff = TryGetDouble(comp, "tempCoeff", out var tc) ? tc : defaultTempCoeff;
            if (TryGetDouble(comp, "tc", out var tcAlt)) tempCoeff = tcAlt;
            double thermalResistance = TryGetDouble(comp, "thermalResistance", out var rth) ? rth : defaultThermalResistance;
            if (TryGetDouble(comp, "rth", out var rthAlt)) thermalResistance = rthAlt;
            double thermalMass = TryGetDouble(comp, "thermalMass", out var cth) ? cth : defaultThermalMass;
            if (TryGetDouble(comp, "cth", out var cthAlt)) thermalMass = cthAlt;

            state = new ThermalState
            {
                TempC = ambient,
                AmbientTempC = ambient,
                ThermalResistance = Math.Max(thermalResistance, 0.001),
                ThermalMass = Math.Max(thermalMass, 0.001),
                TempCoeff = tempCoeff
            };
            map[comp.Id] = state;
            return state;
        }

        private static double ApplyTempCoeff(double baseResistance, ThermalState thermal)
        {
            if (thermal == null) return baseResistance;
            double delta = thermal.TempC - thermal.AmbientTempC;
            return baseResistance * (1.0 + thermal.TempCoeff * delta);
        }

        private BatteryState GetOrCreateBatteryState(ComponentSpec comp)
        {
            if (comp == null) return null;
            if (_batteryStates.TryGetValue(comp.Id, out var state)) return state;

            double nominal = ParseVoltage(comp, 9.0);
            double capacityAh = TryGetDouble(comp, "capacityAh", out var cap) ? cap : DefaultBatteryCapacityAh;
            double internalResistance = TryGetDouble(comp, "internalResistance", out var rint) ? rint : DefaultBatteryInternalResistance;
            double voltageMin = TryGetDouble(comp, "voltageMin", out var vmin) ? vmin : Math.Min(DefaultBatteryVoltageMin, nominal);
            double voltageMax = TryGetDouble(comp, "voltageMax", out var vmax) ? vmax : Math.Max(DefaultBatteryVoltageMax, nominal);
            double soc = TryGetDouble(comp, "soc", out var socVal) ? socVal : 1.0;
            if (soc > 1.0 && soc <= 100.0) soc /= 100.0;
            soc = Clamp(soc, 0.0, 1.0);

            state = new BatteryState
            {
                Soc = soc,
                CapacityAh = Math.Max(capacityAh, 0.001),
                InternalResistance = Math.Max(internalResistance, 0.0),
                VoltageMin = voltageMin,
                VoltageMax = voltageMax,
                NominalVoltage = nominal,
                LastCurrent = 0.0
            };
            _batteryStates[comp.Id] = state;
            return state;
        }

        private static double ComputeBatteryOpenCircuitVoltage(BatteryState state)
        {
            if (state == null) return 0.0;
            double soc = Clamp(state.Soc, 0.0, 1.0);
            return state.VoltageMin + (state.VoltageMax - state.VoltageMin) * soc;
        }

        private static void UpdateBatteryState(BatteryState state, double current, float dtSeconds)
        {
            if (state == null || dtSeconds <= 0) return;
            double capacityC = state.CapacityAh * 3600.0;
            if (capacityC <= 0) return;
            double deltaSoc = (current * dtSeconds) / capacityC;
            state.Soc = Clamp(state.Soc - deltaSoc, 0.0, 1.0);
        }

        private static void UpdateThermalState(ThermalState state, double power, float dtSeconds)
        {
            if (state == null || dtSeconds <= 0) return;
            double rth = Math.Max(state.ThermalResistance, 0.001);
            double cth = Math.Max(state.ThermalMass, 0.001);
            double delta = state.TempC - state.AmbientTempC;
            double dTdt = (power - (delta / rth)) / cth;
            state.TempC += dTdt * dtSeconds;
        }

        private static bool TryGetDouble(ComponentSpec comp, string key, out double value)
        {
            value = 0.0;
            if (comp?.Properties == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!comp.Properties.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
            return TryParseValue(raw, out value);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
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

        private static bool IsSwitchClosed(ComponentSpec comp)
        {
            if (comp?.Properties == null) return false;
            if (TryGetBool(comp.Properties, "closed", out var closed)) return closed;
            if (TryGetBool(comp.Properties, "pressed", out var pressed)) return pressed;
            if (comp.Properties.TryGetValue("state", out var state))
            {
                string value = (state ?? string.Empty).Trim().ToLowerInvariant();
                return value == "closed" || value == "on" || value == "pressed" || value == "true";
            }
            return false;
        }

        private static bool TryGetBool(Dictionary<string, string> props, string key, out bool value)
        {
            value = false;
            if (props == null || string.IsNullOrWhiteSpace(key)) return false;
            if (!props.TryGetValue(key, out var raw)) return false;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on" || s == "closed" || s == "pressed")
            {
                value = true;
                return true;
            }
            if (s == "false" || s == "0" || s == "no" || s == "off" || s == "open")
            {
                value = false;
                return true;
            }
            return false;
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

        private class ThermalState
        {
            public double TempC;
            public double AmbientTempC;
            public double ThermalResistance;
            public double ThermalMass;
            public double TempCoeff;
        }

        private class BatteryState
        {
            public double Soc;
            public double CapacityAh;
            public double InternalResistance;
            public double VoltageMin;
            public double VoltageMax;
            public double NominalVoltage;
            public double LastCurrent;
        }

        private class DiodeState
        {
            public bool IsOn;
        }

        private class ResistorElement
        {
            public string Id { get; }
            public string NetA { get; }
            public string NetB { get; }
            public double Resistance { get; set; }
            public double BaseResistance { get; }
            public double ContactResistance { get; }
            public ThermalState Thermal { get; }
            public bool TrackThermal { get; }

            public ResistorElement(string id, string netA, string netB, double resistance, double baseResistance, double contactResistance, ThermalState thermal, bool trackThermal)
            {
                Id = id;
                NetA = netA;
                NetB = netB;
                Resistance = resistance;
                BaseResistance = baseResistance;
                ContactResistance = contactResistance;
                Thermal = thermal;
                TrackThermal = trackThermal;
            }
        }

        private class DiodeElement
        {
            public string Id { get; }
            public string AnodeNet { get; }
            public string CathodeNet { get; }
            public double ForwardVoltage { get; }
            public double MaxCurrent { get; }
            public double SaturationCurrent { get; }
            public double Ideality { get; }
            public double ThermalVoltage { get; }
            public double SeriesResistance { get; }
            public double ContactResistance { get; }
            public ThermalState Thermal { get; }
            public bool TrackThermal { get; }

            public DiodeElement(
                string id,
                string anodeNet,
                string cathodeNet,
                double forwardVoltage,
                double maxCurrent,
                double saturationCurrent,
                double ideality,
                double thermalVoltage,
                double seriesResistance,
                double contactResistance,
                ThermalState thermal,
                bool trackThermal)
            {
                Id = id;
                AnodeNet = anodeNet;
                CathodeNet = cathodeNet;
                ForwardVoltage = forwardVoltage;
                MaxCurrent = maxCurrent;
                SaturationCurrent = saturationCurrent;
                Ideality = ideality;
                ThermalVoltage = thermalVoltage;
                SeriesResistance = seriesResistance;
                ContactResistance = contactResistance;
                Thermal = thermal;
                TrackThermal = trackThermal;
            }
        }

        private class VoltageSourceElement
        {
            public string Id { get; }
            public string NetPlus { get; }
            public string NetMinus { get; }
            public double Voltage { get; set; }
            public bool IsBattery { get; }
            public BatteryState Battery { get; }

            public VoltageSourceElement(string id, string netPlus, string netMinus, double voltage, bool isBattery, BatteryState battery)
            {
                Id = id;
                NetPlus = netPlus;
                NetMinus = netMinus;
                Voltage = voltage;
                IsBattery = isBattery;
                Battery = battery;
            }
        }
    }
}
