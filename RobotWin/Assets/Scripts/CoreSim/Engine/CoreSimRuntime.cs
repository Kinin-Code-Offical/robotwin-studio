using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim;
using RobotTwin.CoreSim.Models.Physics;
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
        private const double DefaultResistorBlowTemp = 180.0;
        private const double DefaultLedThermalResistance = 200.0;
        private const double DefaultLedThermalMass = 0.2;
        private const double DefaultDiodeIs = 1e-12;
        private const double DefaultDiodeIdeality = 2.0;
        private const double DefaultDiodeThermalVoltage = 0.02585;
        private const double DefaultDiodeForwardTempCoeff = -0.002;
        private const double DefaultBatteryCapacityAh = 0.5;
        private const double DefaultBatteryInternalResistance = 1.5;
        private const double DefaultBatteryVoltageMin = 6.0;
        private const double DefaultBatteryVoltageMax = 9.0;
        private const double DefaultBatteryDepletionSoc = 0.01;
        private const double DefaultBatteryDrainScale = 1.0;
        private const double DefaultMcuIdleCurrent = 0.03;
        private const double UsbAutoSelectThresholdVin = 6.6;
        private const double GminConductance = 1e-8;

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
        private readonly Dictionary<string, string> _regulatorVirtualSupplyNets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _blownResistors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _blownDiodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _noiseStep;

        public double AmbientTempC { get; set; } = DefaultAmbientTempC;

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
            unchecked
            {
                _noiseStep++;
            }

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

            var explicitGroundNets = new HashSet<string>(_groundNets, StringComparer.OrdinalIgnoreCase);
            var islands = BuildIslands(resistors, diodes, voltageSources, explicitGroundNets);
            foreach (var island in islands)
            {
                if (!island.HasElements)
                {
                    PopulateIsolatedNetVoltages(frame, island);
                    continue;
                }

                SetIslandContext(island, explicitGroundNets, frame);
                var solution = SolveCircuit(island.Resistors, island.Diodes, island.Sources, frame);
                if (solution == null)
                {
                    continue;
                }

                var nodeVoltages = ExtractNodeVoltages(solution, island.Sources.Count);
                PopulateTelemetry(frame, spec, island.Resistors, island.Diodes, island.Sources, nodeVoltages, solution, dtSeconds, usbConnectedById);
            }

            if (frame.Signals != null && _batteryStates.Count > 0)
            {
                foreach (var kvp in _batteryStates)
                {
                    string socKey = $"SRC:{kvp.Key}:SOC";
                    if (!frame.Signals.ContainsKey(socKey))
                    {
                        frame.Signals[socKey] = kvp.Value.Soc;
                    }
                    if (kvp.Value.IsDepleted)
                    {
                        string vKey = $"SRC:{kvp.Key}:V";
                        if (!frame.Signals.ContainsKey(vKey))
                        {
                            frame.Signals[vKey] = 0.0;
                        }
                    }
                }
            }

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
                bool handled = false;
                switch (comp.Type)
                {
                    case "Resistor":
                        AddTwoPinResistor(comp, "A", "B", resistors, frame);
                        handled = true;
                        break;
                    case "Capacitor":
                        AddTwoPinResistor(comp, "A", "B", resistors, frame, HighResistance, false);
                        handled = true;
                        break;
                    case "LED":
                        AddLed(comp, diodes, frame);
                        handled = true;
                        break;
                    case "DCMotor":
                        AddTwoPinResistor(comp, "A", "B", resistors, frame, ParseResistance(comp, 10.0));
                        handled = true;
                        break;
                    case "Battery":
                        AddBattery(comp, voltageSources, frame);
                        handled = true;
                        break;
                    case "Button":
                    case "Switch":
                        AddButton(comp, resistors, frame);
                        handled = true;
                        break;
                    case "IRSensor":
                        AddIrSensor(comp, resistors, frame);
                        handled = true;
                        break;
                }

                if (!handled && IsMcuBoardComponent(comp))
                {
                    AddMcuSources(comp, spec, pinVoltages, voltageSources, frame, boardPowerById, usbConnectedById);
                    AddMcuIdleLoad(comp, spec, resistors, boardPowerById, usbConnectedById);
                    AddMcuPullups(comp, spec, pinStatesByComponent, pullupResistances, resistors, frame, usbConnectedById);
                }
            }
        }

        private List<IslandData> BuildIslands(
            List<ResistorElement> resistors,
            List<DiodeElement> diodes,
            List<VoltageSourceElement> sources,
            HashSet<string> explicitGroundNets)
        {
            var allNets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in _netToNode.Keys)
            {
                allNets.Add(net);
            }
            foreach (var net in _groundNets)
            {
                allNets.Add(net);
            }

            var parents = BuildNetParents(allNets);
            foreach (var res in resistors)
            {
                UnionNets(parents, res.NetA, res.NetB);
            }
            foreach (var diode in diodes)
            {
                UnionNets(parents, diode.AnodeNet, diode.CathodeNet);
            }
            foreach (var source in sources)
            {
                UnionNets(parents, source.NetPlus, source.NetMinus);
            }

            var islands = new Dictionary<string, IslandData>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in allNets)
            {
                var root = FindNetParent(parents, net);
                if (!islands.TryGetValue(root, out var island))
                {
                    island = new IslandData();
                    islands[root] = island;
                }
                island.Nets.Add(net);
            }

            foreach (var res in resistors)
            {
                var root = GetIslandRoot(parents, res.NetA, res.NetB);
                if (root == null) continue;
                islands[root].Resistors.Add(res);
            }
            foreach (var diode in diodes)
            {
                var root = GetIslandRoot(parents, diode.AnodeNet, diode.CathodeNet);
                if (root == null) continue;
                islands[root].Diodes.Add(diode);
            }
            foreach (var source in sources)
            {
                var root = GetIslandRoot(parents, source.NetPlus, source.NetMinus);
                if (root == null) continue;
                islands[root].Sources.Add(source);
            }

            foreach (var island in islands.Values)
            {
                island.HasExplicitGround = explicitGroundNets != null &&
                    island.Nets.Any(net => explicitGroundNets.Contains(net));
            }

            return islands.Values.ToList();
        }

        private void SetIslandContext(IslandData island, HashSet<string> explicitGroundNets, TelemetryFrame frame)
        {
            _netToNode.Clear();
            _groundNets.Clear();
            _hasExplicitGround = island != null && island.HasExplicitGround;

            if (island == null || island.Nets.Count == 0) return;

            if (explicitGroundNets != null)
            {
                foreach (var net in island.Nets)
                {
                    if (explicitGroundNets.Contains(net))
                    {
                        _groundNets.Add(net);
                    }
                }
            }

            if (_groundNets.Count == 0)
            {
                var fallbackNet = island.Nets.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(fallbackNet))
                {
                    _groundNets.Add(fallbackNet);
                    frame.ValidationMessages.Add("Missing GND reference (no net contains GND pins).");
                    frame.ValidationMessages.Add($"Disconnected GND. Using net '{fallbackNet}' as reference.");
                }
            }

            int index = 0;
            foreach (var net in island.Nets)
            {
                if (_groundNets.Contains(net)) continue;
                _netToNode[net] = index++;
            }
        }

        private void PopulateIsolatedNetVoltages(TelemetryFrame frame, IslandData island)
        {
            if (frame == null || island == null) return;
            foreach (var net in island.Nets)
            {
                if (string.IsNullOrWhiteSpace(net)) continue;
                frame.Signals[$"NET:{net}"] = 0.0;
            }
        }

        private static Dictionary<string, string> BuildNetParents(HashSet<string> nets)
        {
            var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (nets == null) return parents;
            foreach (var net in nets)
            {
                if (string.IsNullOrWhiteSpace(net)) continue;
                parents[net] = net;
            }
            return parents;
        }

        private static string FindNetParent(Dictionary<string, string> parents, string net)
        {
            if (string.IsNullOrWhiteSpace(net) || parents == null) return string.Empty;
            if (!parents.TryGetValue(net, out var parent)) return string.Empty;
            if (parent == net) return net;
            var root = FindNetParent(parents, parent);
            parents[net] = root;
            return root;
        }

        private static void UnionNets(Dictionary<string, string> parents, string netA, string netB)
        {
            if (string.IsNullOrWhiteSpace(netA) || string.IsNullOrWhiteSpace(netB)) return;
            if (parents == null || !parents.ContainsKey(netA) || !parents.ContainsKey(netB)) return;
            var rootA = FindNetParent(parents, netA);
            var rootB = FindNetParent(parents, netB);
            if (!string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase))
            {
                parents[rootB] = rootA;
            }
        }

        private static string GetIslandRoot(Dictionary<string, string> parents, string netA, string netB)
        {
            if (!string.IsNullOrWhiteSpace(netA) && parents != null && parents.ContainsKey(netA))
            {
                return FindNetParent(parents, netA);
            }
            if (!string.IsNullOrWhiteSpace(netB) && parents != null && parents.ContainsKey(netB))
            {
                return FindNetParent(parents, netB);
            }
            return null;
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
            baseResistance = ApplyStaticVariation(comp, "resistance", baseResistance);
            double blowTemp = DefaultResistorBlowTemp;
            if (TryGetDouble(comp, "blowTemp", out var blowTempVal) && blowTempVal > 0)
            {
                blowTemp = blowTempVal;
            }
            else if (TryGetDouble(comp, "maxTemp", out var maxTempVal) && maxTempVal > 0)
            {
                blowTemp = maxTempVal;
            }
            else if (TryGetDouble(comp, "burnTemp", out var burnTempVal) && burnTempVal > 0)
            {
                blowTemp = burnTempVal;
            }
            double contactResistance = TryGetDouble(comp, "contactResistance", out var contact) ? contact : DefaultContactResistance;
            contactResistance = ApplyStaticVariation(comp, "contactResistance", contactResistance);
            double noiseVrms = ReadNoiseRms(comp, "V", baseResistance);
            double noiseIrms = ReadNoiseRms(comp, "I", baseResistance);
            if (_blownResistors.Contains(comp.Id))
            {
                frame.ValidationMessages.Add($"Component Blown: {comp.Id}");
                var blownThermal = GetOrCreateThermalState(_resistorThermal, comp, DefaultResistorTempCoeff, DefaultResistorThermalResistance, DefaultResistorThermalMass);
                resistors.Add(new ResistorElement(comp.Id, netA, netB, HighResistance, baseResistance, contactResistance, blownThermal, true, blowTemp, noiseVrms, noiseIrms));
                return;
            }
            ThermalState thermal = null;
            double resistance = baseResistance;
            if (trackThermal)
            {
                thermal = GetOrCreateThermalState(_resistorThermal, comp, DefaultResistorTempCoeff, DefaultResistorThermalResistance, DefaultResistorThermalMass);
                resistance = ApplyTempCoeff(baseResistance, thermal);
            }
            resistance += contactResistance * 2.0;
            resistors.Add(new ResistorElement(comp.Id, netA, netB, resistance, baseResistance, contactResistance, thermal, trackThermal, blowTemp, noiseVrms, noiseIrms));
        }

        private void AddLed(ComponentSpec comp, List<DiodeElement> diodes, TelemetryFrame frame)
        {
            if (_blownDiodes.Contains(comp.Id))
            {
                frame.ValidationMessages.Add($"Component Blown: {comp.Id}");
                return;
            }
            var anodeNet = GetNetFor(comp.Id, "Anode");
            var cathodeNet = GetNetFor(comp.Id, "Cathode");
            if (!IsConnected(anodeNet) || !IsConnected(cathodeNet))
            {
                frame.ValidationMessages.Add($"LED '{comp.Id}' missing pin connection.");
                return;
            }

            double forwardV = ParseVoltage(comp, DefaultLedForwardV, "forwardV");
            forwardV = ApplyStaticVariation(comp, "forwardV", forwardV);
            double maxCurrent = ParseCurrent(comp, 0.02, "If_max");
            if (maxCurrent <= 0) maxCurrent = ParseCurrent(comp, 0.02, "current");
            double saturationCurrent = TryGetDouble(comp, "Is", out var isVal) ? isVal : DefaultDiodeIs;
            double ideality = TryGetDouble(comp, "ideality", out var nVal) ? nVal : DefaultDiodeIdeality;
            if (TryGetDouble(comp, "n", out var nAlt)) ideality = nAlt;
            saturationCurrent = ApplyStaticVariation(comp, "saturationCurrent", saturationCurrent);
            double seriesResistance = TryGetDouble(comp, "seriesResistance", out var rsVal) ? rsVal : 0.0;
            seriesResistance = ApplyStaticVariation(comp, "seriesResistance", seriesResistance);
            double contactResistance = TryGetDouble(comp, "contactResistance", out var contact) ? contact : DefaultContactResistance;
            contactResistance = ApplyStaticVariation(comp, "contactResistance", contactResistance);
            double forwardTempCoeff = TryGetDouble(comp, "vfTempCoeff", out var vfTc) ? vfTc : DefaultDiodeForwardTempCoeff;
            if (TryGetDouble(comp, "forwardTempCoeff", out var vfAlt)) forwardTempCoeff = vfAlt;
            double noiseVrms = ReadNoiseRms(comp, "V", forwardV);
            double noiseIrms = ReadNoiseRms(comp, "I", maxCurrent);
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
                forwardTempCoeff,
                noiseVrms,
                noiseIrms,
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
            if (state.IsDepleted)
            {
                frame.ValidationMessages.Add($"Battery depleted: {comp.Id}");
                return;
            }
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

        private void AddIrSensor(ComponentSpec comp, List<ResistorElement> resistors, TelemetryFrame frame)
        {
            if (comp == null) return;
            string outNet = GetNetFor(comp.Id, "OUT");
            if (!IsConnected(outNet))
            {
                frame.ValidationMessages.Add($"IR sensor '{comp.Id}' missing OUT connection.");
                return;
            }

            string vccNet = GetNetFor(comp.Id, "VCC");
            string gndNet = GetNetFor(comp.Id, "GND");
            if (!IsConnected(gndNet))
            {
                gndNet = GetGroundNet();
            }

            bool outputHigh = ReadIrSensorState(comp);
            double closedResistance = ParseResistance(comp, DefaultSwitchClosedResistance);

            if (outputHigh)
            {
                if (!IsConnected(vccNet))
                {
                    frame.ValidationMessages.Add($"IR sensor '{comp.Id}' missing VCC connection.");
                    return;
                }
                resistors.Add(new ResistorElement($"{comp.Id}:OUT_VCC", outNet, vccNet, closedResistance, closedResistance, 0.0, null, false, 0.0, 0.0, 0.0));
            }
            else
            {
                if (!IsConnected(gndNet))
                {
                    frame.ValidationMessages.Add($"IR sensor '{comp.Id}' missing GND connection.");
                    return;
                }
                resistors.Add(new ResistorElement($"{comp.Id}:OUT_GND", outNet, gndNet, closedResistance, closedResistance, 0.0, null, false, 0.0, 0.0, 0.0));
            }
        }

        private bool ReadIrSensorState(ComponentSpec comp)
        {
            if (comp?.Properties == null) return false;

            bool activeLow = false;
            if (TryGetBool(comp.Properties, "activeLow", out var activeLowValue) ||
                TryGetBool(comp.Properties, "invert", out activeLowValue) ||
                TryGetBool(comp.Properties, "inverted", out activeLowValue))
            {
                activeLow = activeLowValue;
            }

            if (TryGetBool(comp.Properties, "state", out var state) ||
                TryGetBool(comp.Properties, "out", out state) ||
                TryGetBool(comp.Properties, "output", out state) ||
                TryGetBool(comp.Properties, "high", out state) ||
                TryGetBool(comp.Properties, "detect", out state) ||
                TryGetBool(comp.Properties, "active", out state))
            {
                return activeLow ? !state : state;
            }

            if (!TryGetDoubleAny(comp, out var signal, "signal", "value", "reflectance", "analog"))
            {
                return activeLow ? true : false;
            }

            double vref = 5.0;
            double voltage;
            if (signal <= 1.0)
            {
                voltage = signal * vref;
            }
            else if (signal <= vref + 0.5)
            {
                voltage = signal;
            }
            else
            {
                voltage = Clamp(signal, 0.0, 1023.0) * vref / 1023.0;
            }

            double threshold = 0.5 * vref;
            if (TryGetDoubleAny(comp, out var rawThreshold, "threshold", "thresholdV", "thresholdPct"))
            {
                if (rawThreshold <= 1.0)
                {
                    threshold = rawThreshold * vref;
                }
                else if (rawThreshold <= vref + 0.5)
                {
                    threshold = rawThreshold;
                }
                else
                {
                    threshold = Clamp(rawThreshold, 0.0, 1023.0) * vref / 1023.0;
                }
            }

            double noiseVrms = ReadNoiseRms(comp, "V", voltage);
            if (noiseVrms > 0.0)
            {
                double sample = DeterministicNoise.SampleSigned($"{comp.Id}:IR:V", _noiseStep);
                voltage = Clamp(voltage + sample * noiseVrms, 0.0, vref);
            }

            bool high = voltage >= threshold;
            return activeLow ? !high : high;
        }

        private void AddMcuSources(
            ComponentSpec comp,
            CircuitSpec spec,
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
            string gndNet = GetGroundNetForComponent(comp);
            bool batterySupply = TryGetActiveBatterySupply(comp, spec, usbConnectedById, out var batterySupplyNet, out var batteryNominalVoltage, out _);
            bool usbSupply = IsUsbConnected(comp, usbConnectedById) && !batterySupply;

            string gndRef = IsConnected(gndNet) ? gndNet : GetGroundNet();

            // If powered via USB (and not via battery), the board provides a 5V rail.
            // We model this as an internal virtual supply net used for idle load/pullups,
            // and optionally as a 5V/3V3 output if those header pins are connected.
            if (usbSupply)
            {
                string usbNet = GetUsbSupplyNet(comp);
                RegisterVirtualNet(usbNet);
                if (IsConnected(gndRef))
                {
                    voltageSources.Add(new VoltageSourceElement($"{comp.Id}.USB", usbNet, gndRef, 5.0, false, null));
                }
            }

            // If powered via VIN/RAW (battery supply), the board provides a regulated 5V rail.
            bool vinOrRawSupply = false;
            if (batterySupply)
            {
                string vinNet = GetNetFor(comp.Id, "VIN");
                string rawNet = GetNetFor(comp.Id, "RAW");
                vinOrRawSupply = (!string.IsNullOrWhiteSpace(vinNet) && string.Equals(batterySupplyNet, vinNet, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(rawNet) && string.Equals(batterySupplyNet, rawNet, StringComparison.OrdinalIgnoreCase));
                if (vinOrRawSupply)
                {
                    string regNet = GetRegulatorSupplyNet(comp);
                    RegisterVirtualNet(regNet);
                    if (IsConnected(gndRef))
                    {
                        voltageSources.Add(new VoltageSourceElement($"{comp.Id}.REG5V", regNet, gndRef, 5.0, false, null));
                    }
                }
            }

            // Drive header rails only when the board is generating them (USB or VIN/RAW).
            // This avoids fights when the board is externally powered through 5V/3V3.
            bool canDrive5V = usbSupply || vinOrRawSupply;
            if (canDrive5V)
            {
                AddMcuPinSource(comp, "5V", 5.0, voltageSources, gndRef);
                AddMcuPinSource(comp, "3V3", 3.3, voltageSources, gndRef);
            }

            // IOREF/VCC are treated as board outputs indicating logic rail.
            // If supply is explicitly 3V3, reflect that; otherwise assume 5V logic.
            double logicVoltage = 5.0;
            if (batterySupply && !vinOrRawSupply && batteryNominalVoltage <= 5.5)
            {
                logicVoltage = Math.Max(0.0, Math.Min(5.0, batteryNominalVoltage));
            }
            AddMcuPinSource(comp, "IOREF", logicVoltage, voltageSources, gndRef);
            AddMcuPinSource(comp, "VCC", logicVoltage, voltageSources, gndRef);

            if (pinVoltages == null) return;

            foreach (var kvp in pinVoltages)
            {
                if (!kvp.Key.StartsWith(comp.Id + ".", StringComparison.Ordinal)) continue;
                string pin = kvp.Key.Substring(comp.Id.Length + 1);
                if (!IsDigitalPin(pin)) continue;
                AddMcuPinSource(comp, pin, kvp.Value, voltageSources, gndNet);

                // Arduino Uno R3 aliases: SDA=SDA(A4), SCL=SCL(A5)
                if (string.Equals(pin, "A4", StringComparison.OrdinalIgnoreCase))
                {
                    AddMcuPinSource(comp, "SDA", kvp.Value, voltageSources, gndRef);
                }
                else if (string.Equals(pin, "A5", StringComparison.OrdinalIgnoreCase))
                {
                    AddMcuPinSource(comp, "SCL", kvp.Value, voltageSources, gndRef);
                }
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
                   IsConnected(GetNetFor(comp.Id, "VIN")) ||
                   IsConnected(GetNetFor(comp.Id, "RAW"));
        }

        private string GetUsbSupplyNet(ComponentSpec comp)
        {
            if (comp == null || string.IsNullOrWhiteSpace(comp.Id)) return "USB5V";
            if (_usbVirtualSupplyNets.TryGetValue(comp.Id, out var netId)) return netId;
            netId = $"USB5V_{comp.Id}";
            _usbVirtualSupplyNets[comp.Id] = netId;
            return netId;
        }

        private string GetGroundNetForComponent(ComponentSpec comp)
        {
            if (comp == null || string.IsNullOrWhiteSpace(comp.Id)) return GetGroundNet();
            string prefix = comp.Id + ".";
            foreach (var kvp in _pinToNet)
            {
                if (!kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string pin = GetPinName(kvp.Key);
                if (!IsGroundPin(pin)) continue;
                if (IsConnected(kvp.Value)) return kvp.Value;
            }
            return GetGroundNet();
        }

        private void AddMcuPinSource(ComponentSpec comp, string pin, double voltage, List<VoltageSourceElement> voltageSources, string groundNet)
        {
            var net = GetNetFor(comp.Id, pin);
            if (!IsConnected(net)) return;
            string gndNet = IsConnected(groundNet) ? groundNet : GetGroundNet();
            voltageSources.Add(new VoltageSourceElement($"{comp.Id}.{pin}", net, gndNet, voltage, false, null));
        }

        private void AddMcuRegulatorSource(
            ComponentSpec comp,
            CircuitSpec spec,
            List<VoltageSourceElement> voltageSources,
            string groundNet,
            Dictionary<string, bool> usbConnectedById)
        {
            if (comp == null || voltageSources == null) return;
            if (IsConnected(GetNetFor(comp.Id, "5V")) ||
                IsConnected(GetNetFor(comp.Id, "VCC")) ||
                IsConnected(GetNetFor(comp.Id, "IOREF")) ||
                IsConnected(GetNetFor(comp.Id, "3V3")))
            {
                return;
            }
            if (!IsBatterySupplyingBoard(comp, spec, usbConnectedById)) return;

            string regNet = GetRegulatorSupplyNet(comp);
            if (string.IsNullOrWhiteSpace(regNet)) return;

            string gndNet = IsConnected(groundNet) ? groundNet : GetGroundNet();
            if (!IsConnected(gndNet)) return;

            RegisterVirtualNet(regNet);
            voltageSources.Add(new VoltageSourceElement($"{comp.Id}.REG5V", regNet, gndNet, 5.0, false, null));
        }

        private string GetRegulatorSupplyNet(ComponentSpec comp)
        {
            if (comp == null || string.IsNullOrWhiteSpace(comp.Id)) return string.Empty;
            if (_regulatorVirtualSupplyNets.TryGetValue(comp.Id, out var netId)) return netId;
            netId = $"REG5V_{comp.Id}";
            _regulatorVirtualSupplyNets[comp.Id] = netId;
            return netId;
        }

        private double GetMcuIdleCurrent(ComponentSpec comp)
        {
            if (comp?.Properties != null && comp.Properties.TryGetValue("idleCurrent", out var raw))
            {
                if (TryParseCurrent(raw, out var parsed))
                {
                    return Math.Max(parsed, 0.0);
                }
            }
            return GetMcuDefaultIdleCurrent(comp);
        }

        private static double GetMcuDefaultIdleCurrent(ComponentSpec comp)
        {
            if (comp == null) return DefaultMcuIdleCurrent;
            string profileId = ResolveMcuProfileId(comp);
            if (string.Equals(profileId, "ArduinoUno", StringComparison.OrdinalIgnoreCase)) return 0.05;
            if (string.Equals(profileId, "ArduinoNano", StringComparison.OrdinalIgnoreCase)) return 0.03;
            if (string.Equals(profileId, "ArduinoProMini", StringComparison.OrdinalIgnoreCase)) return 0.02;
            return DefaultMcuIdleCurrent;
        }

        private bool TryGetMcuIdleSupply(
            ComponentSpec comp,
            CircuitSpec spec,
            Dictionary<string, bool> usbConnectedById,
            out string supplyNet,
            out double nominalVoltage)
        {
            supplyNet = string.Empty;
            nominalVoltage = 0.0;
            if (comp == null) return false;

            if (TryGetActiveBatterySupply(comp, spec, usbConnectedById, out supplyNet, out nominalVoltage, out _))
            {
                return true;
            }

            if (IsUsbConnected(comp, usbConnectedById))
            {
                supplyNet = GetUsbSupplyNet(comp);
                RegisterVirtualNet(supplyNet);
                nominalVoltage = 5.0;
                return true;
            }

            if (IsConnected(GetNetFor(comp.Id, "5V")))
            {
                supplyNet = GetNetFor(comp.Id, "5V");
                nominalVoltage = 5.0;
                return true;
            }
            if (IsConnected(GetNetFor(comp.Id, "VCC")))
            {
                supplyNet = GetNetFor(comp.Id, "VCC");
                nominalVoltage = 5.0;
                return true;
            }
            if (IsConnected(GetNetFor(comp.Id, "IOREF")))
            {
                supplyNet = GetNetFor(comp.Id, "IOREF");
                nominalVoltage = 5.0;
                return true;
            }
            if (IsConnected(GetNetFor(comp.Id, "3V3")))
            {
                supplyNet = GetNetFor(comp.Id, "3V3");
                nominalVoltage = 3.3;
                return true;
            }
            if (IsConnected(GetNetFor(comp.Id, "VIN")))
            {
                supplyNet = GetNetFor(comp.Id, "VIN");
                nominalVoltage = 9.0;
                return true;
            }
            if (IsConnected(GetNetFor(comp.Id, "RAW")))
            {
                supplyNet = GetNetFor(comp.Id, "RAW");
                nominalVoltage = 9.0;
                return true;
            }
            return false;
        }

        private bool IsBatterySupplyingBoard(ComponentSpec board, CircuitSpec spec, Dictionary<string, bool> usbConnectedById)
        {
            return TryGetActiveBatterySupply(board, spec, usbConnectedById, out _, out _, out _);
        }

        private bool TryGetActiveBatterySupply(
            ComponentSpec board,
            CircuitSpec spec,
            Dictionary<string, bool> usbConnectedById,
            out string supplyNet,
            out double nominalVoltage,
            out ComponentSpec battery)
        {
            supplyNet = string.Empty;
            nominalVoltage = 0.0;
            battery = null;
            if (!TryGetBatteryForBoard(board, spec, out battery, out _, out supplyNet, out nominalVoltage)) return false;
            if (battery == null) return false;
            var state = GetOrCreateBatteryState(battery);
            if (state == null || state.IsDepleted) return false;

            if (usbConnectedById != null && IsUsbConnected(board, usbConnectedById) && nominalVoltage >= UsbAutoSelectThresholdVin)
            {
                double ocv = ComputeBatteryOpenCircuitVoltage(state);
                if (ocv < UsbAutoSelectThresholdVin)
                {
                    return false;
                }
            }
            return true;
        }

        private bool TryGetBatteryForBoard(
            ComponentSpec board,
            CircuitSpec spec,
            out ComponentSpec battery,
            out string batteryNet,
            out string boardSupplyNet,
            out double nominalVoltage)
        {
            battery = null;
            batteryNet = string.Empty;
            boardSupplyNet = string.Empty;
            nominalVoltage = 0.0;
            if (board == null || spec?.Components == null) return false;

            foreach (var comp in spec.Components)
            {
                if (comp == null || !string.Equals(comp.Type, "Battery", StringComparison.OrdinalIgnoreCase)) continue;
                string netPlus = GetNetFor(comp.Id, "+");
                if (string.IsNullOrWhiteSpace(netPlus)) continue;

                if (TryGetBoardSupplyNetFor(board, netPlus, out boardSupplyNet, out nominalVoltage))
                {
                    battery = comp;
                    batteryNet = netPlus;
                    return true;
                }

                foreach (var sw in spec.Components)
                {
                    if (!IsSwitchComponent(sw)) continue;
                    if (!IsSwitchClosed(sw)) continue;
                    string netA = GetNetFor(sw.Id, "A");
                    string netB = GetNetFor(sw.Id, "B");
                    if (string.IsNullOrWhiteSpace(netA) || string.IsNullOrWhiteSpace(netB)) continue;
                    if (string.Equals(netA, netPlus, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryGetBoardSupplyNetFor(board, netB, out boardSupplyNet, out nominalVoltage))
                        {
                            battery = comp;
                            batteryNet = netPlus;
                            return true;
                        }
                    }
                    else if (string.Equals(netB, netPlus, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryGetBoardSupplyNetFor(board, netA, out boardSupplyNet, out nominalVoltage))
                        {
                            battery = comp;
                            batteryNet = netPlus;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool IsSwitchComponent(ComponentSpec comp)
        {
            if (comp == null) return false;
            return string.Equals(comp.Type, "Switch", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comp.Type, "Button", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetBoardSupplyNetFor(ComponentSpec board, string netId, out string supplyNet, out double nominalVoltage)
        {
            supplyNet = string.Empty;
            nominalVoltage = 0.0;
            if (board == null || string.IsNullOrWhiteSpace(netId)) return false;
            string[] pins = { "VIN", "RAW", "5V", "VCC", "IOREF", "3V3" };
            foreach (var pin in pins)
            {
                string net = GetNetFor(board.Id, pin);
                if (string.Equals(netId, net, StringComparison.OrdinalIgnoreCase))
                {
                    supplyNet = netId;
                    nominalVoltage = GetNominalVoltageForPin(pin);
                    return true;
                }
            }
            return false;
        }

        private static double GetNominalVoltageForPin(string pin)
        {
            if (string.IsNullOrWhiteSpace(pin)) return 5.0;
            switch (pin.Trim().ToUpperInvariant())
            {
                case "3V3":
                    return 3.3;
                case "VIN":
                case "RAW":
                    return 9.0;
                default:
                    return 5.0;
            }
        }

        private void AddMcuIdleLoad(
            ComponentSpec comp,
            CircuitSpec spec,
            List<ResistorElement> resistors,
            Dictionary<string, bool> boardPowerById,
            Dictionary<string, bool> usbConnectedById)
        {
            if (comp == null || resistors == null) return;
            if (!IsBoardPowered(comp, boardPowerById)) return;
            if (!TryGetMcuIdleSupply(comp, spec, usbConnectedById, out var supplyNet, out var nominalVoltage))
            {
                return;
            }

            string gndNet = GetGroundNetForComponent(comp);
            if (!IsConnected(gndNet)) gndNet = GetGroundNet();
            if (!IsConnected(gndNet)) return;

            double idleCurrent = GetMcuIdleCurrent(comp);
            if (idleCurrent <= 0.0) return;

            double resistance = nominalVoltage / Math.Max(idleCurrent, 1e-6);
            resistors.Add(new ResistorElement($"{comp.Id}.IDLE", supplyNet, gndNet, resistance, resistance, 0.0, null, false, 0.0, 0.0, 0.0));
        }

        private void AddMcuPullups(
            ComponentSpec comp,
            CircuitSpec spec,
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

            string vccNet = GetNetFor(comp.Id, "5V");
            if (!IsConnected(vccNet)) vccNet = GetNetFor(comp.Id, "VCC");
            if (!IsConnected(vccNet)) vccNet = GetNetFor(comp.Id, "IOREF");
            if (!IsConnected(vccNet)) vccNet = GetNetFor(comp.Id, "3V3");
            if (!IsConnected(vccNet))
            {
                if (IsBatterySupplyingBoard(comp, spec, usbConnectedById))
                {
                    vccNet = GetRegulatorSupplyNet(comp);
                }
                else if (IsUsbConnected(comp, usbConnectedById))
                {
                    vccNet = GetUsbSupplyNet(comp);
                }
                if (IsConnected(vccNet))
                {
                    RegisterVirtualNet(vccNet);
                }
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
                resistors.Add(new ResistorElement($"{comp.Id}.{state.Pin}:PULLUP", pinNet, vccNet, pullupResistance, pullupResistance, 0.0, null, false, 0.0, 0.0, 0.0));
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
                // Add tiny leakage to ground to prevent singular matrices from floating nets.
                for (int n = 0; n < nodeCount; n++)
                {
                    matrix[n, n] += GminConductance;
                }

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
            if (diode.TrackThermal && diode.Thermal != null && diode.ForwardTempCoeff != 0.0)
            {
                double delta = diode.Thermal.TempC - diode.Thermal.AmbientTempC;
                vf += diode.ForwardTempCoeff * delta;
            }
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
            float dtSeconds,
            Dictionary<string, bool> usbConnectedById)
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
                double measV = vDiff;
                double measI = current;
                ApplyMeasurementNoise(res.Id, res.NoiseVrms, res.NoiseIrms, ref measV, ref measI);
                frame.Signals[$"COMP:{res.Id}:I"] = measI;
                frame.Signals[$"COMP:{res.Id}:V"] = measV;
                frame.Signals[$"COMP:{res.Id}:P"] = measI * measV;
                frame.Signals[$"COMP:{res.Id}:R"] = res.Resistance;

                if (res.TrackThermal && res.Thermal != null)
                {
                    UpdateThermalState(res.Thermal, Math.Abs(power), dtSeconds);
                    frame.Signals[$"COMP:{res.Id}:T"] = res.Thermal.TempC;
                    res.Resistance = ApplyTempCoeff(res.BaseResistance, res.Thermal) + res.ContactResistance * 2.0;
                    if (!_blownResistors.Contains(res.Id) && res.BlowTemp > 0 && res.Thermal.TempC >= res.BlowTemp)
                    {
                        _blownResistors.Add(res.Id);
                        frame.ValidationMessages.Add($"Component Blown: {res.Id} overtemp {res.Thermal.TempC:F1}C");
                    }
                }
            }

            foreach (var diode in diodes)
            {
                double vA = GetNodeVoltage(diode.AnodeNet, voltages);
                double vB = GetNodeVoltage(diode.CathodeNet, voltages);
                double vDiff = vA - vB;
                double current = ComputeDiodeCurrent(diode, vDiff);
                double power = Math.Abs(current * vDiff);
                double measV = vDiff;
                double measI = current;
                ApplyMeasurementNoise(diode.Id, diode.NoiseVrms, diode.NoiseIrms, ref measV, ref measI);
                frame.Signals[$"COMP:{diode.Id}:I"] = measI;
                frame.Signals[$"COMP:{diode.Id}:V"] = measV;
                frame.Signals[$"COMP:{diode.Id}:P"] = Math.Abs(measI * measV);
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
                    if (!_blownDiodes.Contains(diode.Id))
                    {
                        _blownDiodes.Add(diode.Id);
                        frame.ValidationMessages.Add($"Component Blown: {diode.Id} overcurrent {current * 1000.0:F1}mA");
                    }
                }
            }

            int nodeCount = _netToNode.Count;
            var sourceCurrents = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sources.Count; i++)
            {
                int idx = nodeCount + i;
                if (idx >= 0 && idx < solution.Length)
                {
                    sourceCurrents[sources[i].Id] = solution[idx];
                }
            }
            var batteryExtraCurrent = ComputeBatteryDerivedLoads(spec, sources, sourceCurrents, usbConnectedById);

            for (int i = 0; i < sources.Count; i++)
            {
                int idx = nodeCount + i;
                if (idx >= 0 && idx < solution.Length)
                {
                    double current = sourceCurrents.TryGetValue(sources[i].Id, out var value) ? value : 0.0;
                    if (sources[i].IsBattery && batteryExtraCurrent.TryGetValue(sources[i].Id, out var extra))
                    {
                        current += extra;
                    }
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

        private Dictionary<string, double> ComputeBatteryDerivedLoads(
            CircuitSpec spec,
            List<VoltageSourceElement> sources,
            Dictionary<string, double> sourceCurrents,
            Dictionary<string, bool> usbConnectedById)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (spec?.Components == null || sources == null || sourceCurrents == null) return result;

            var boardCurrents = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Id)) continue;
                int dot = source.Id.IndexOf('.');
                if (dot <= 0) continue;
                string boardId = source.Id.Substring(0, dot);
                if (!sourceCurrents.TryGetValue(source.Id, out var current)) continue;
                if (current <= 0.0) continue;
                boardCurrents[boardId] = boardCurrents.TryGetValue(boardId, out var total) ? total + current : current;
            }

            if (boardCurrents.Count == 0) return result;

            foreach (var comp in spec.Components)
            {
                if (comp == null) continue;
                if (!IsMcuBoardComponent(comp))
                {
                    continue;
                }
                if (!boardCurrents.TryGetValue(comp.Id, out var boardCurrent)) continue;
                if (!TryGetBatteryForBoard(comp, spec, out var battery, out var batteryNet, out var boardSupplyNet, out _)) continue;
                if (!TryGetActiveBatterySupply(comp, spec, usbConnectedById, out _, out _, out _)) continue;
                if (battery == null || string.IsNullOrWhiteSpace(battery.Id)) continue;

                if (string.Equals(batteryNet, boardSupplyNet, StringComparison.OrdinalIgnoreCase)) continue;

                result[battery.Id] = result.TryGetValue(battery.Id, out var total) ? total + boardCurrent : boardCurrent;
            }

            return result;
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

        private static bool IsMcuBoardComponent(ComponentSpec comp)
        {
            if (comp == null) return false;
            if (comp.Properties != null &&
                comp.Properties.TryGetValue("boardProfile", out var profile) &&
                BoardProfiles.IsKnownProfileId(profile))
            {
                return true;
            }
            return BoardProfiles.IsKnownProfileId(comp.Type);
        }

        private static string ResolveMcuProfileId(ComponentSpec comp)
        {
            if (comp?.Properties != null &&
                comp.Properties.TryGetValue("boardProfile", out var profile) &&
                !string.IsNullOrWhiteSpace(profile))
            {
                return BoardProfiles.Get(profile).Id;
            }
            if (comp == null || string.IsNullOrWhiteSpace(comp.Type))
            {
                return BoardProfiles.GetDefault().Id;
            }
            return BoardProfiles.Get(comp.Type).Id;
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
            if (string.IsNullOrWhiteSpace(pin)) return false;
            if (pin.StartsWith("D", StringComparison.OrdinalIgnoreCase)) return true;
            if (pin.StartsWith("A", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(pin, "SDA", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(pin, "SCL", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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

            double ambient = TryGetDouble(comp, "ambientTemp", out var amb) ? amb : AmbientTempC;
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
            double capacityAh = ParseCapacityAh(comp, DefaultBatteryCapacityAh);
            double internalResistance = TryGetDouble(comp, "internalResistance", out var rint)
                ? rint
                : (TryGetDouble(comp, "rint", out var rintAlt) ? rintAlt : DefaultBatteryInternalResistance);
            capacityAh = ApplyStaticVariation(comp, "capacity", capacityAh);
            internalResistance = ApplyStaticVariation(comp, "internalResistance", internalResistance);
            double voltageMin = TryGetDouble(comp, "voltageMin", out var vmin) ? vmin : Math.Min(DefaultBatteryVoltageMin, nominal);
            double voltageMax = TryGetDouble(comp, "voltageMax", out var vmax) ? vmax : Math.Max(DefaultBatteryVoltageMax, nominal);
            double soc = TryGetDouble(comp, "soc", out var socVal) ? socVal : 1.0;
            double drainScale = TryGetDouble(comp, "drainScale", out var scaleVal) ? scaleVal : DefaultBatteryDrainScale;
            if (soc > 1.0 && soc <= 100.0) soc /= 100.0;
            soc = Clamp(soc, 0.0, 1.0);
            drainScale = Math.Max(0.01, drainScale);

            state = new BatteryState
            {
                Soc = soc,
                CapacityAh = Math.Max(capacityAh, 0.001),
                InternalResistance = Math.Max(internalResistance, 0.0),
                VoltageMin = voltageMin,
                VoltageMax = voltageMax,
                NominalVoltage = nominal,
                LastCurrent = 0.0,
                DrainScale = drainScale
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
            if (state.IsDepleted)
            {
                state.Soc = 0.0;
                return;
            }
            double capacityC = state.CapacityAh * 3600.0;
            if (capacityC <= 0) return;
            double scaledDt = dtSeconds * Math.Max(0.01, state.DrainScale);
            double deltaSoc = (Math.Abs(current) * scaledDt) / capacityC;
            state.Soc = Clamp(state.Soc - deltaSoc, 0.0, 1.0);
            if (state.Soc <= DefaultBatteryDepletionSoc)
            {
                state.Soc = 0.0;
                state.IsDepleted = true;
            }
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

        private static bool TryGetDoubleAny(ComponentSpec comp, out double value, params string[] keys)
        {
            value = 0.0;
            if (keys == null || keys.Length == 0) return false;
            for (int i = 0; i < keys.Length; i++)
            {
                if (TryGetDouble(comp, keys[i], out value))
                {
                    return true;
                }
            }
            return false;
        }

        private static double ReadTolerance(ComponentSpec comp, string key)
        {
            if (TryGetDoubleAny(comp, out var val, $"{key}Tolerance", $"{key}Tol", $"{key}TolPct", "tolerance", "tol"))
            {
                return val;
            }
            return 0.0;
        }

        private static double ReadAgingYears(ComponentSpec comp)
        {
            if (TryGetDoubleAny(comp, out var val, "agingYears", "ageYears", "age"))
            {
                return Math.Max(0.0, val);
            }
            return 0.0;
        }

        private static double ReadAgingRate(ComponentSpec comp, string key)
        {
            if (TryGetDoubleAny(comp, out var val, $"{key}AgingRate", $"{key}AgingRatePct", "agingRate", "agingRatePct"))
            {
                return val;
            }
            return 0.0;
        }

        private double ApplyStaticVariation(ComponentSpec comp, string key, double nominal)
        {
            if (comp == null) return nominal;
            double tolerance = ReadTolerance(comp, key);
            if (tolerance != 0.0)
            {
                double sample = DeterministicNoise.SampleSigned($"{comp.Id}:{key}:tol");
                nominal = ComponentVariation.ApplyTolerance(nominal, tolerance, sample);
            }
            double years = ReadAgingYears(comp);
            double rate = ReadAgingRate(comp, key);
            if (years > 0.0 && rate != 0.0)
            {
                nominal = ComponentVariation.ApplyAging(nominal, rate, years);
            }
            return nominal;
        }

        private double ReadNoiseRms(ComponentSpec comp, string signalSuffix, double nominal)
        {
            if (comp == null) return 0.0;
            if (TryGetDoubleAny(comp, out var rms, $"noise{signalSuffix}", $"noise{signalSuffix}Rms", $"noise{signalSuffix}RmsValue"))
            {
                return Math.Abs(rms);
            }
            if (TryGetDoubleAny(comp, out var pct, $"noise{signalSuffix}Pct", $"noise{signalSuffix}Percent", "noisePct", "noisePercent"))
            {
                double ratio = Math.Abs(ComponentVariation.NormalizePercent(pct));
                return Math.Abs(nominal) * ratio;
            }
            if (TryGetDoubleAny(comp, out var rmsAny, "noiseRms", "noise"))
            {
                return Math.Abs(rmsAny);
            }
            return 0.0;
        }

        private void ApplyMeasurementNoise(string id, double noiseVrms, double noiseIrms, ref double v, ref double i)
        {
            if (noiseVrms > 0.0)
            {
                double sample = DeterministicNoise.SampleSigned($"{id}:V", _noiseStep);
                v = ComponentVariation.ApplyNoise(v, noiseVrms, sample);
            }
            if (noiseIrms > 0.0)
            {
                double sample = DeterministicNoise.SampleSigned($"{id}:I", _noiseStep);
                i = ComponentVariation.ApplyNoise(i, noiseIrms, sample);
            }
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
            if (s == "high")
            {
                value = true;
                return true;
            }
            if (s == "low")
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

        private static double ParseCapacityAh(ComponentSpec comp, double fallback)
        {
            if (comp?.Properties == null) return fallback;
            if (comp.Properties.TryGetValue("capacityAh", out var raw) ||
                comp.Properties.TryGetValue("capacity", out raw))
            {
                if (TryParseCapacity(raw, out var val))
                {
                    return val;
                }
            }
            return fallback;
        }

        private static bool TryParseCapacity(string raw, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().ToLowerInvariant().Replace(" ", string.Empty);
            double multiplier = 1.0;
            if (s.EndsWith("mah", StringComparison.Ordinal))
            {
                multiplier = 1e-3;
                s = s.Substring(0, s.Length - 3);
            }
            else if (s.EndsWith("ah", StringComparison.Ordinal))
            {
                s = s.Substring(0, s.Length - 2);
            }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseValue))
            {
                return false;
            }
            value = baseValue * multiplier;
            return true;
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
            public double DrainScale;
            public bool IsDepleted;
        }

        private class DiodeState
        {
            public bool IsOn;
        }

        private class IslandData
        {
            public HashSet<string> Nets { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<ResistorElement> Resistors { get; } = new List<ResistorElement>();
            public List<DiodeElement> Diodes { get; } = new List<DiodeElement>();
            public List<VoltageSourceElement> Sources { get; } = new List<VoltageSourceElement>();
            public bool HasExplicitGround { get; set; }

            public bool HasElements => Resistors.Count > 0 || Diodes.Count > 0 || Sources.Count > 0;
        }

        private class ResistorElement
        {
            public string Id { get; }
            public string NetA { get; }
            public string NetB { get; }
            public double Resistance { get; set; }
            public double BaseResistance { get; }
            public double ContactResistance { get; }
            public double BlowTemp { get; }
            public double NoiseVrms { get; }
            public double NoiseIrms { get; }
            public ThermalState Thermal { get; }
            public bool TrackThermal { get; }

            public ResistorElement(
                string id,
                string netA,
                string netB,
                double resistance,
                double baseResistance,
                double contactResistance,
                ThermalState thermal,
                bool trackThermal,
                double blowTemp,
                double noiseVrms,
                double noiseIrms)
            {
                Id = id;
                NetA = netA;
                NetB = netB;
                Resistance = resistance;
                BaseResistance = baseResistance;
                ContactResistance = contactResistance;
                BlowTemp = blowTemp;
                NoiseVrms = noiseVrms;
                NoiseIrms = noiseIrms;
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
            public double ForwardTempCoeff { get; }
            public double SeriesResistance { get; }
            public double ContactResistance { get; }
            public double NoiseVrms { get; }
            public double NoiseIrms { get; }
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
                double forwardTempCoeff,
                double noiseVrms,
                double noiseIrms,
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
                ForwardTempCoeff = forwardTempCoeff;
                SeriesResistance = seriesResistance;
                ContactResistance = contactResistance;
                NoiseVrms = noiseVrms;
                NoiseIrms = noiseIrms;
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
