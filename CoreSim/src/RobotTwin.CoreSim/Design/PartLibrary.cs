using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>How the schematic editor draws a part.</summary>
    public enum SymbolKind
    {
        Resistor, Potentiometer, Thermistor, Photoresistor, Fuse,
        Capacitor, PolarCapacitor, Inductor,
        Diode, Zener, Schottky, Led,
        Npn, Pnp, Nmos, Pmos, Darlington,
        OpAmp, Regulator, Gate,
        Battery, VoltageSource, CurrentSource, SignalSource,
        Ground, PowerRail,
        PushButton, Switch, Relay,
        Motor, Servo, Buzzer, Lamp,
        Ammeter, Voltmeter,
        Connector, Module, Board,
        Multimeter, BenchSupply
    }

    public sealed class PinDefinition
    {
        public PinDefinition(string name, string pad, double x, double y)
        {
            Name = name;
            Pad = pad;
            X = x;
            Y = y;
        }

        public string Name { get; }

        /// <summary>Footprint pad number this pin is soldered to.</summary>
        public string Pad { get; }

        /// <summary>Pin tip position in symbol coordinates (grid units, 1 = 0.1 in).</summary>
        public double X { get; }
        public double Y { get; }
    }

    public sealed class ParameterDefinition
    {
        public ParameterDefinition(string key, string label, string defaultValue, string unit = "", string[]? choices = null)
        {
            Key = key;
            Label = label;
            DefaultValue = defaultValue;
            Unit = unit;
            Choices = choices;
        }

        public string Key { get; }
        public string Label { get; }
        public string DefaultValue { get; }
        public string Unit { get; }
        public string[]? Choices { get; }

        /// <summary>[uncluttered] Shown only while another parameter has this value (the OPC UA port while OPC UA is on …).</summary>
        public (string Key, string Value)? ShownWhen { get; init; }
    }

    /// <summary>Everything a part builder needs to emit devices for one placed part.</summary>
    public sealed class PartBuildContext
    {
        private readonly Func<string, int> _pinNode;
        private readonly IReadOnlyDictionary<string, string> _parameters;

        internal PartBuildContext(string refDes, AnalogCircuit circuit, Func<string, int> pinNode,
            IReadOnlyDictionary<string, string> parameters, List<AnalogDevice> devices)
        {
            RefDes = refDes;
            Circuit = circuit;
            _pinNode = pinNode;
            _parameters = parameters;
            Devices = devices;
        }

        public string RefDes { get; }
        public AnalogCircuit Circuit { get; }
        public List<AnalogDevice> Devices { get; }

        public int Pin(string name) => _pinNode(name);

        public int Internal(string suffix) => Circuit.InternalNode(RefDes, suffix);

        public string Text(string key) => _parameters.TryGetValue(key, out var value) ? value : string.Empty;

        public double Num(string key, double fallback = 0.0) =>
            _parameters.TryGetValue(key, out var value) ? SiValue.Parse(value, fallback) : fallback;

        public bool Flag(string key)
        {
            string v = Text(key).Trim().ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "on" || v == "closed";
        }

        public T Add<T>(T device) where T : AnalogDevice
        {
            Circuit.Add(device);
            Devices.Add(device);
            return device;
        }

        /// <summary>Id for a sub-device: the part itself for single-device parts, "R1.x" otherwise.</summary>
        public string Sub(string suffix) => RefDes + "." + suffix;
    }

    public sealed class PartDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string RefPrefix { get; set; } = "U";
        public SymbolKind Symbol { get; set; }

        /// <summary>Extra drawing hint: gate function, LED colour, board name...</summary>
        public string SymbolVariant { get; set; } = string.Empty;

        /// <summary>Half-size of the symbol body in grid units (used for box symbols and hit testing).</summary>
        public double HalfWidth { get; set; } = 2.0;
        public double HalfHeight { get; set; } = 1.0;

        public List<PinDefinition> Pins { get; } = new List<PinDefinition>();
        public string Footprint { get; set; } = string.Empty;
        public List<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>();

        /// <summary>Parameter shown under the reference designator ("220Ω", "LM358").</summary>
        public string ValueKey { get; set; } = string.Empty;

        /// <summary>Pins that are connected inside the part (all GND pins of a board...).</summary>
        public List<string[]> InternalConnections { get; } = new List<string[]>();

        /// <summary>Name of the net this part forces (ground and power-rail symbols).</summary>
        public string? NetName { get; set; }

        /// <summary>Emits the analog devices. Null for purely mechanical/connectivity parts.</summary>
        public Action<PartBuildContext>? Build { get; set; }

        public PinDefinition? FindPin(string name) =>
            Pins.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        public string DefaultParameter(string key) =>
            Parameters.FirstOrDefault(p => p.Key == key)?.DefaultValue ?? string.Empty;

        /// <summary>
        /// Footprint pad for a pin, honouring a "pinmap" parameter ("OUT:1,IN-:2") or a
        /// "pinout" letter string for three-leg packages ("EBC", "GDS", "IGO").
        /// </summary>
        public string ResolvePad(PinDefinition pin, IReadOnlyDictionary<string, string>? parameters)
        {
            if (parameters != null)
            {
                if (parameters.TryGetValue("pinmap", out var map) && !string.IsNullOrWhiteSpace(map))
                {
                    foreach (var entry in map.Split(','))
                    {
                        int colon = entry.LastIndexOf(':');
                        if (colon <= 0) continue;
                        if (string.Equals(entry.Substring(0, colon).Trim(), pin.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            return entry.Substring(colon + 1).Trim();
                        }
                    }
                }

                if (parameters.TryGetValue("pinout", out var pinout) && !string.IsNullOrWhiteSpace(pinout))
                {
                    char first = char.ToUpperInvariant(pin.Name[0]);
                    int index = pinout.ToUpperInvariant().IndexOf(first);
                    if (index >= 0) return (index + 1).ToString(CultureInfo.InvariantCulture);
                }
            }

            return pin.Pad;
        }

        /// <summary>Footprint id, honouring a "footprint" parameter override.</summary>
        public string ResolveFootprint(IReadOnlyDictionary<string, string>? parameters) =>
            parameters != null && parameters.TryGetValue("footprint", out var fp) && !string.IsNullOrWhiteSpace(fp)
                ? fp
                : Footprint;
    }

    /// <summary>A catalog entry: a part definition with a set of parameter values (a real part number).</summary>
    public sealed class PartPreset
    {
        public PartPreset(string id, string name, string partId, string category, string description,
            IReadOnlyDictionary<string, string> parameters)
        {
            Id = id;
            Name = name;
            PartId = partId;
            Category = category;
            Description = description;
            Parameters = parameters;
        }

        public string Id { get; }
        public string Name { get; }
        public string PartId { get; }
        public string Category { get; }
        public string Description { get; }
        public IReadOnlyDictionary<string, string> Parameters { get; }
    }

    /// <summary>The component catalog: generic parts plus real part numbers built on them.</summary>
    public sealed class PartLibrary
    {
        private readonly Dictionary<string, PartDefinition> _parts =
            new Dictionary<string, PartDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PartPreset> _presets = new List<PartPreset>();
        private readonly Dictionary<string, PartPreset> _presetById =
            new Dictionary<string, PartPreset>(StringComparer.OrdinalIgnoreCase);

        private static readonly Lazy<PartLibrary> DefaultLibrary = new Lazy<PartLibrary>(() =>
        {
            var lib = new PartLibrary();
            StandardParts.Register(lib);
            return lib;
        });

        public static PartLibrary Default => DefaultLibrary.Value;

        public IEnumerable<PartDefinition> Parts => _parts.Values;
        public IReadOnlyList<PartPreset> Presets => _presets;

        public IEnumerable<string> Categories =>
            _presets.Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase);

        public void Add(PartDefinition part)
        {
            if (_parts.ContainsKey(part.Id)) throw new InvalidOperationException($"'{part.Id}' parçası kütüphanede iki kez var.");
            _parts[part.Id] = part;
        }

        public void AddPreset(PartPreset preset)
        {
            if (!_parts.ContainsKey(preset.PartId))
            {
                throw new InvalidOperationException($"'{preset.Id}' hazır ayarı bilinmeyen '{preset.PartId}' parçasına bağlı.");
            }

            if (_presetById.ContainsKey(preset.Id)) throw new InvalidOperationException($"'{preset.Id}' hazır ayarı iki kez var.");
            _presets.Add(preset);
            _presetById[preset.Id] = preset;
        }

        public PartDefinition? Find(string id) => _parts.TryGetValue(id, out var part) ? part : null;

        /// <summary>[libs] Removes a preset (user library disabled/removed); returns false if unknown.</summary>
        public bool RemovePreset(string id)
        {
            if (!_presetById.TryGetValue(id, out var preset)) return false;
            _presetById.Remove(id);
            _presets.Remove(preset);
            return true;
        }

        /// <summary>[libs] Removes a part definition no preset uses any more; returns false if unknown or still used.</summary>
        public bool Remove(string partId)
        {
            if (!_parts.ContainsKey(partId) || _presets.Any(p => string.Equals(p.PartId, partId, StringComparison.OrdinalIgnoreCase))) return false;
            return _parts.Remove(partId);
        }

        public PartPreset? FindPreset(string id) => _presetById.TryGetValue(id, out var preset) ? preset : null;

        public PartDefinition Get(string id) =>
            Find(id) ?? throw new KeyNotFoundException($"Bilinmeyen parça '{id}'.");

        /// <summary>Full parameter set for a part: definition defaults, then preset values, then overrides.</summary>
        public Dictionary<string, string> ResolveParameters(PartDefinition part, PartPreset? preset,
            IReadOnlyDictionary<string, string>? overrides)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in part.Parameters) result[p.Key] = p.DefaultValue;
            if (preset != null)
            {
                foreach (var kv in preset.Parameters) result[kv.Key] = kv.Value;
            }

            if (overrides != null)
            {
                foreach (var kv in overrides) result[kv.Key] = kv.Value;
            }

            return result;
        }

        internal static string Inv(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
