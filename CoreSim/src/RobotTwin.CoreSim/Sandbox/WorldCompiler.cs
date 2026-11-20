using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>A programmable board somewhere in the world (on a designed PCB or placed on its own).</summary>
    public sealed class WorldProgrammable
    {
        public string ObjectId { get; set; } = string.Empty;

        /// <summary>Designator inside its object ("U1").</summary>
        public string InnerRefDes { get; set; } = string.Empty;

        /// <summary>Designator in the world circuit ("O3/U1").</summary>
        public string RefDes { get; set; } = string.Empty;

        public PartDefinition Definition { get; set; } = null!;
        public IReadOnlyDictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        public FirmwareSource? Firmware { get; set; }
    }

    /// <summary>The electrical side of a world: one circuit containing every object, plus the cables.</summary>
    public sealed class WorldCircuit
    {
        internal WorldCircuit(WorldDocument world, CompiledCircuit compiled)
        {
            World = world;
            Compiled = compiled;
        }

        public WorldDocument World { get; }
        public CompiledCircuit Compiled { get; }
        public List<WorldProgrammable> Programmables { get; } = new List<WorldProgrammable>();
        public Dictionary<string, Resistor> CableDevices { get; } = new Dictionary<string, Resistor>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Designator a component object's single part gets inside its object ("M1").</summary>
        public Dictionary<string, string> ComponentRefDes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string Prefix(string objectId) => objectId + "/";

        /// <summary>World designator of a part ("O3", "U1" -> "O3/U1").</summary>
        public string FullRefDes(string objectId, string innerRefDes) => Prefix(objectId) + innerRefDes;

        /// <summary>World designator of a component object's part.</summary>
        public string? ComponentRef(string objectId) =>
            ComponentRefDes.TryGetValue(objectId, out var r) ? FullRefDes(objectId, r) : null;

        /// <summary>Devices that model one part ("O3", "M1").</summary>
        public IReadOnlyList<AnalogDevice> DevicesOf(string objectId, string innerRefDes) => Compiled.DevicesOf(FullRefDes(objectId, innerRefDes));

        /// <summary>Node of a cable end, or -1.</summary>
        public int NodeOf(CableEnd end)
        {
            var (refDes, pin) = WorldCompiler.Resolve(this, end);
            return refDes == null ? -1 : Compiled.PinNode(refDes, pin);
        }

        public double Voltage(AnalogSimulator sim, CableEnd end)
        {
            int n = NodeOf(end);
            return n < 0 ? double.NaN : sim.NodeVoltage(n);
        }
    }

    /// <summary>
    /// Flattens a world into one analog circuit. Every object's circuit is instantiated
    /// with its designators and nets prefixed by the object id, exactly like a module;
    /// boards stay electrically separate (their own GND) until a cable joins them.
    /// Cables are copper conductors with gauge- and length-dependent resistance that heat
    /// up and burn open when overloaded.
    /// </summary>
    public static class WorldCompiler
    {
        private sealed class Unit
        {
            public WorldObject Object = null!;
            public string Prefix = string.Empty;
            public Schematic Schematic = null!;
            public Netlist Netlist = null!;
            public ProjectPackage? Package;
        }

        public static WorldCircuit Compile(WorldDocument world, PartLibrary library, Func<WorldCable, double>? cableLength = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (library == null) throw new ArgumentNullException(nameof(library));

            foreach (var part in world.Parts.Concat(world.Designs.SelectMany(d => d.Package.Parts)))
            {
                try
                {
                    if (library.Find(part.Id) == null) part.Register(library);
                }
                catch (System.IO.InvalidDataException)
                {
                }
            }

            var units = new List<Unit>();
            var componentRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();
            foreach (var o in world.Objects)
            {
                Schematic? sch = null;
                ProjectPackage? pkg = null;
                if (o.Kind == WorldObjectKind.Board)
                {
                    var design = world.FindDesign(o.DesignId);
                    if (design == null)
                    {
                        warnings.Add($"{o.Name}: kart tasarımı bulunamadı.");
                        continue;
                    }

                    pkg = design.Package;
                    sch = pkg.Schematic;
                }
                else if (o.Kind == WorldObjectKind.Component && o.PartId != null)
                {
                    var def = library.Find(o.PartId);
                    if (def == null)
                    {
                        warnings.Add($"{o.Name}: bilinmeyen parça '{o.PartId}'.");
                        continue;
                    }

                    sch = new Schematic { Title = o.Name };
                    var part = new SchematicPart
                    {
                        RefDes = (string.IsNullOrEmpty(def.RefPrefix) ? "X" : def.RefPrefix) + "1",
                        PartId = def.Id,
                        PresetId = o.PresetId,
                        Parameters = new Dictionary<string, string>(o.Parameters, StringComparer.OrdinalIgnoreCase)
                    };
                    sch.Parts.Add(part);
                    componentRefs[o.Id] = part.RefDes;
                }

                if (sch == null) continue;
                units.Add(new Unit { Object = o, Prefix = WorldCircuit.Prefix(o.Id), Schematic = sch, Netlist = sch.BuildNetlist(library), Package = pkg });
            }

            // ---- global netlist with prefixed names
            var netlist = new Netlist();
            var netOfPin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in units)
            {
                foreach (var net in u.Netlist.Nets)
                {
                    var g = new Net { Name = u.Prefix + net.Name, IsGround = net.IsGround, IsAutoNamed = net.IsAutoNamed };
                    foreach (var c in net.Connections)
                    {
                        g.Connections.Add(new NetConnection(u.Prefix + c.RefDes, c.Pin));
                        netOfPin[u.Prefix + c.RefDes + "\u0001" + c.Pin] = g.Name;
                    }

                    netlist.Nets.Add(g);
                }

                // A lone part's unconnected pins are expected in the world (cables do the wiring).
                if (u.Object.Kind == WorldObjectKind.Board)
                {
                    foreach (var w in u.Netlist.Warnings) netlist.Warnings.Add(u.Object.Name + ": " + w);
                }
            }

            string KeyOf(string fullRef, string pin) =>
                netOfPin.TryGetValue(fullRef + "\u0001" + pin, out var n) ? n : fullRef + "." + pin + "#open";

            // ---- cable endpoints
            var cableEnds = new List<(WorldCable Cable, string KeyA, string KeyB, string RefA, string PinA, string RefB, string PinB)>();
            var probe = new WorldCircuit(world, null!);
            foreach (var kv in componentRefs) probe.ComponentRefDes[kv.Key] = kv.Value;
            foreach (var cable in world.Cables)
            {
                var (ra, pa) = Resolve(probe, cable.A);
                var (rb, pb) = Resolve(probe, cable.B);
                if (ra == null || rb == null || !PinExists(units, library, ra, pa) || !PinExists(units, library, rb, pb))
                {
                    warnings.Add($"Kablo {cable.Name}: ucu bağlı olduğu pini bulamadı.");
                    continue;
                }

                cableEnds.Add((cable, KeyOf(ra, pa), KeyOf(rb, pb), ra, pa, rb, pb));
            }

            // ---- one 0 V reference per electrically connected island
            var circuit = new AnalogCircuit();
            var compiled = new CompiledCircuit(circuit, netlist);
            compiled.Warnings.AddRange(warnings);
            compiled.Warnings.AddRange(netlist.Warnings);
            foreach (var reference in ChooseReferences(units, library, netlist, KeyOf, cableEnds.Select(c => (c.KeyA, c.KeyB)), compiled.Warnings))
            {
                circuit.AliasGround(reference);
            }

            foreach (var net in netlist.Nets) compiled.NodeByNet[net.Name] = circuit.Node(net.Name);

            var result = new WorldCircuit(world, compiled);
            foreach (var kv in componentRefs) result.ComponentRefDes[kv.Key] = kv.Value;

            // ---- parts
            foreach (var u in units)
            {
                foreach (var part in u.Schematic.Parts)
                {
                    var def = library.Find(part.PartId);
                    if (def == null) continue;
                    string fullRef = u.Prefix + part.RefDes;
                    foreach (var pin in def.Pins)
                    {
                        if (netOfPin.TryGetValue(fullRef + "\u0001" + pin.Name, out var wired)) compiled.NodeByPin[fullRef + "." + pin.Name] = circuit.Node(wired);
                    }

                    if (def.Build == null) continue;
                    var parameters = u.Schematic.ResolveParameters(library, part);
                    parameters = Radio.WorldUsb.Override(world, u.Object, part.RefDes, def, parameters, library);   // [radio] USB cable drives VBUS
                    var devices = new List<AnalogDevice>();
                    int PinNode(string pin)
                    {
                        if (def.FindPin(pin) == null && !netOfPin.ContainsKey(fullRef + "\u0001" + pin)) throw new InvalidOperationException($"{def.Id} has no pin '{pin}'.");
                        int node = circuit.Node(KeyOf(fullRef, pin));
                        compiled.NodeByPin[fullRef + "." + pin] = node;
                        return node;
                    }

                    try
                    {
                        def.Build(new PartBuildContext(fullRef, circuit, PinNode, parameters, devices));
                    }
                    catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
                    {
                        compiled.Warnings.Add($"{u.Object.Name} {part.RefDes}: modellenemedi ({ex.Message}).");
                    }

                    compiled.DevicesByPart[fullRef] = devices;

                    if (def.Symbol == SymbolKind.Board)
                    {
                        result.Programmables.Add(new WorldProgrammable
                        {
                            ObjectId = u.Object.Id,
                            InnerRefDes = part.RefDes,
                            RefDes = fullRef,
                            Definition = def,
                            Parameters = parameters,
                            Firmware = u.Package?.FirmwareFor(part.RefDes) ?? u.Object.Firmware
                        });
                    }
                }
            }

            // ---- cables
            foreach (var (cable, keyA, keyB, _, _, _, _) in cableEnds)
            {
                double length = cable.Length > 0 ? cable.Length : cableLength?.Invoke(cable) ?? 0.2;
                double r = WorldCable.Resistance(cable.Awg, length);
                double amp = WorldCable.Ampacity(cable.Awg);
                var wire = new Resistor("cable:" + cable.Id, circuit.Node(keyA), circuit.Node(keyB), r)
                {
                    // Insulation (PVC) softens around 105 °C and burns through well above it;
                    // rated current gives ≈ 85 K rise in free air.
                    PowerRating = amp * amp * r,
                    MaxTempC = 160.0
                };
                circuit.Add(wire);
                result.CableDevices[cable.Id] = wire;
                compiled.DevicesByPart["cable:" + cable.Id] = new List<AnalogDevice> { wire };
            }

            return result;
        }

        /// <summary>World designator and pin of a cable end ("O3" + "J1.2" -> "O3/J1", "2").</summary>
        public static (string? RefDes, string Pin) Resolve(WorldCircuit circuit, CableEnd end)
        {
            var o = circuit.World.Find(end.ObjectId);
            if (o == null || string.IsNullOrEmpty(end.Pin)) return (null, string.Empty);
            if (o.Kind == WorldObjectKind.Component)
            {
                return circuit.ComponentRefDes.TryGetValue(o.Id, out var r) ? (circuit.FullRefDes(o.Id, r), end.Pin) : (null, string.Empty);
            }

            int dot = end.Pin.IndexOf('.');
            if (dot <= 0 || dot == end.Pin.Length - 1) return (null, string.Empty);
            return (circuit.FullRefDes(o.Id, end.Pin.Substring(0, dot)), end.Pin.Substring(dot + 1));
        }

        private static bool PinExists(List<Unit> units, PartLibrary library, string fullRef, string pin)
        {
            int slash = fullRef.IndexOf('/');
            if (slash < 0) return false;
            string objectId = fullRef.Substring(0, slash), inner = fullRef.Substring(slash + 1);
            var u = units.FirstOrDefault(x => string.Equals(x.Object.Id, objectId, StringComparison.OrdinalIgnoreCase));
            var part = u?.Schematic.FindPart(inner);
            var def = part != null ? library.Find(part.PartId) : null;
            return def?.FindPin(pin) != null;
        }

        private static List<string> ChooseReferences(List<Unit> units, PartLibrary library, Netlist netlist, Func<string, string, string> keyOf,
            IEnumerable<(string A, string B)> cables, List<string> warnings)
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

            void Union(int a, int b) => parent[Find(a)] = Find(b);

            foreach (var net in netlist.Nets) Key(net.Name);
            var score = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in units)
            {
                foreach (var part in u.Schematic.Parts)
                {
                    var def = library.Find(part.PartId);
                    if (def?.Build == null) continue;
                    int first = -1;
                    foreach (var pin in def.Pins)
                    {
                        string k = keyOf(u.Prefix + part.RefDes, pin.Name);
                        int id = Key(k);
                        if (first < 0) first = id;
                        else Union(id, first);
                        int s = pin.Name.StartsWith("GND", StringComparison.OrdinalIgnoreCase) ? 4
                            : pin.Name == "-" || pin.Name == "V-" ? 3
                            : pin.Name == "K" || pin.Name == "E" || pin.Name == "S" || pin.Name == "B" ? 1 : 0;
                        score[k] = Math.Max(score.TryGetValue(k, out int old) ? old : 0, s);
                    }
                }
            }

            foreach (var (a, b) in cables) Union(Key(a), Key(b));

            var groundNets = netlist.Nets.Where(n => n.IsGround).ToList();
            var result = new List<string>();
            foreach (var group in Enumerable.Range(0, keys.Count).GroupBy(Find).OrderBy(g => keys[g.Key], StringComparer.Ordinal))
            {
                var members = group.Select(i => keys[i]).ToList();
                if (members.Count == 1 && !score.ContainsKey(members[0])) continue;
                var set = new HashSet<string>(members, StringComparer.OrdinalIgnoreCase);

                // Boards joined by cables share one reference: the ground net with the most connections.
                var ground = groundNets.Where(n => set.Contains(n.Name))
                    .OrderByDescending(n => n.Connections.Count).ThenBy(n => n.Name, StringComparer.Ordinal).FirstOrDefault();
                if (ground != null)
                {
                    result.Add(ground.Name);
                    continue;
                }

                string reference = members
                    .OrderByDescending(k => score.TryGetValue(k, out int s) ? s : 0)
                    .ThenBy(k => k.EndsWith("#open", StringComparison.Ordinal) ? 1 : 0)
                    .ThenBy(k => k, StringComparer.Ordinal)
                    .First();
                result.Add(reference);
            }

            return result;
        }
    }
}
