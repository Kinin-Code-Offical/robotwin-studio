using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>A schematic turned into a simulatable analog netlist.</summary>
    public sealed class CompiledCircuit
    {
        internal CompiledCircuit(AnalogCircuit circuit, Netlist netlist)
        {
            Circuit = circuit;
            Netlist = netlist;
        }

        public AnalogCircuit Circuit { get; }
        public Netlist Netlist { get; }
        public Dictionary<string, List<AnalogDevice>> DevicesByPart { get; } =
            new Dictionary<string, List<AnalogDevice>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> NodeByNet { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>"R1.A" -> node index (also for pins that are not wired anywhere).</summary>
        public Dictionary<string, int> NodeByPin { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public T? Device<T>(string id) where T : AnalogDevice => Circuit.Find<T>(id);

        public int PinNode(string refDes, string pin) =>
            NodeByPin.TryGetValue(refDes + "." + pin, out int node) ? node : -1;

        /// <summary>Voltage of a part pin against ground, or NaN if the pin is unknown.</summary>
        public double PinVoltage(AnalogSimulator simulator, string refDes, string pin)
        {
            int node = PinNode(refDes, pin);
            return node < 0 ? double.NaN : simulator.NodeVoltage(node);
        }

        public IReadOnlyList<AnalogDevice> DevicesOf(string refDes) =>
            DevicesByPart.TryGetValue(refDes, out var list) ? list : (IReadOnlyList<AnalogDevice>)Array.Empty<AnalogDevice>();

        /// <summary>Values drawn from the tolerance bands, per part (empty in nominal mode).</summary>
        public Dictionary<string, List<ToleranceDraw>> ToleranceDraws { get; } =
            new Dictionary<string, List<ToleranceDraw>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The part a device belongs to ("R1" for "R1", "U1" for "U1.REG5V"), or null.</summary>
        public string? PartOf(AnalogDevice device)
        {
            foreach (var kv in DevicesByPart)
            {
                foreach (var d in kv.Value)
                {
                    if (ReferenceEquals(d, device)) return kv.Key;
                }
            }

            int cut = device.Id.IndexOfAny(new[] { '.', '#' });
            string head = cut > 0 ? device.Id.Substring(0, cut) : device.Id;
            return DevicesByPart.ContainsKey(head) ? head : null;
        }
    }

    public static class CircuitCompiler
    {
        public static CompiledCircuit Compile(Schematic schematic, PartLibrary library) => Compile(schematic, library, null);

        /// <summary>Compiles with component tolerances (realistic / Monte Carlo draws) and the ambient temperature.</summary>
        public static CompiledCircuit Compile(Schematic schematic, PartLibrary library, CompileOptions? options)
        {
            if (schematic == null) throw new ArgumentNullException(nameof(schematic));
            if (library == null) throw new ArgumentNullException(nameof(library));

            var netlist = schematic.BuildNetlist(library);
            var circuit = new AnalogCircuit();
            var compiled = new CompiledCircuit(circuit, netlist);
            compiled.Warnings.AddRange(netlist.Warnings);

            var netOfPin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in netlist.Nets)
            {
                foreach (var c in net.Connections) netOfPin[c.RefDes + "\u0001" + c.Pin] = net.Name;
            }

            // Every electrically separate island needs its own 0 V reference, otherwise its
            // node voltages are undefined. Islands touching a GND net use it; the others get
            // the most ground-like terminal they contain.
            foreach (var reference in ChooseReferences(schematic, library, netlist, netOfPin, compiled.Warnings))
            {
                circuit.AliasGround(reference);
            }

            foreach (var net in netlist.Nets) compiled.NodeByNet[net.Name] = circuit.Node(net.Name);

            foreach (var part in schematic.Parts)
            {
                var def = library.Find(part.PartId);
                if (def != null)
                {
                    foreach (var pin in def.Pins)
                    {
                        if (netOfPin.TryGetValue(part.RefDes + "\u0001" + pin.Name, out var wired))
                        {
                            compiled.NodeByPin[part.RefDes + "." + pin.Name] = circuit.Node(wired);
                        }
                    }
                }

                if (def?.Build == null) continue;

                var parameters = schematic.ResolveParameters(library, part);
                if (options?.Tolerances != null && options.Tolerances.Active)
                {
                    var draws = ToleranceModel.Apply(def, parameters, part.RefDes, options.Tolerances, options.ToleranceRun, options.AmbientC);
                    if (draws.Count > 0) compiled.ToleranceDraws[part.RefDes] = draws;
                }

                var devices = new List<AnalogDevice>();
                string refDes = part.RefDes;
                int PinNode(string pin)
                {
                    int node;
                    if (netOfPin.TryGetValue(refDes + "\u0001" + pin, out var netName)) node = circuit.Node(netName);
                    else if (def.FindPin(pin) == null) throw new InvalidOperationException($"{def.Id} parçasında '{pin}' bacağı yok.");
                    else node = circuit.Node(refDes + "." + pin + "#open");
                    compiled.NodeByPin[refDes + "." + pin] = node;
                    return node;
                }

                try
                {
                    def.Build(new PartBuildContext(refDes, circuit, PinNode, parameters, devices));
                }
                catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    compiled.Warnings.Add($"{refDes}: modellenemedi ({ex.Message}).");
                }

                foreach (var d in devices) d.Group = refDes;   // [split] one component, one part of the circuit
                compiled.DevicesByPart[refDes] = devices;
            }

            return compiled;
        }

        private static string OpenNode(string refDes, string pin) => refDes + "." + pin + "#open";

        private static List<string> ChooseReferences(Schematic schematic, PartLibrary library, Netlist netlist,
            Dictionary<string, string> netOfPin, List<string> warnings)
        {
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var keys = new List<string>();
            var parent = new List<int>();
            int Key(string k)
            {
                if (!index.TryGetValue(k, out int i))
                {
                    i = keys.Count;
                    index[k] = i;
                    keys.Add(k);
                    parent.Add(i);
                }

                return i;
            }

            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            foreach (var net in netlist.Nets) Key(net.Name);

            var score = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in schematic.Parts)
            {
                var def = library.Find(part.PartId);
                if (def?.Build == null) continue;
                int first = -1;
                foreach (var pin in def.Pins)
                {
                    string k = netOfPin.TryGetValue(part.RefDes + "\u0001" + pin.Name, out var net) ? net : OpenNode(part.RefDes, pin.Name);
                    int id = Key(k);
                    if (first < 0) first = id;
                    else parent[Find(id)] = Find(first);

                    int s = pin.Name.StartsWith("GND", StringComparison.OrdinalIgnoreCase) ? 4
                        : pin.Name == "-" || pin.Name == "V-" ? 3
                        : pin.Name == "K" || pin.Name == "E" || pin.Name == "S" || pin.Name == "B" ? 1 : 0;
                    score[k] = Math.Max(score.TryGetValue(k, out int old) ? old : 0, s);
                }
            }

            var grounded = new HashSet<int>();
            var result = new List<string>();
            foreach (var net in netlist.Nets.Where(n => n.IsGround))
            {
                result.Add(net.Name);
                grounded.Add(Find(Key(net.Name)));
            }

            foreach (var group in Enumerable.Range(0, keys.Count).GroupBy(Find).OrderBy(g => keys[g.Key], StringComparer.Ordinal))
            {
                if (grounded.Contains(group.Key)) continue;
                var members = group.Select(i => keys[i]).ToList();
                if (members.Count == 1 && !score.ContainsKey(members[0])) continue;   // lone label/header net: nothing to solve

                string reference = members
                    .OrderByDescending(k => score.TryGetValue(k, out int s) ? s : 0)
                    .ThenBy(k => k.EndsWith("#open", StringComparison.Ordinal) ? 1 : 0)
                    .ThenBy(k => k, StringComparer.Ordinal)
                    .First();
                result.Add(reference);
                string shown = reference.EndsWith("#open", StringComparison.Ordinal) ? reference.Substring(0, reference.Length - 5) : reference;
                warnings.Add($"'{shown}' içeren devre parçası GND'ye bağlı değil; bu nokta 0 V referansı kabul edildi.");
            }

            return result;
        }
    }
}
