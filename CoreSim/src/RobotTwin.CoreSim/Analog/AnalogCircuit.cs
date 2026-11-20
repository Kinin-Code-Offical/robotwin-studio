using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Analog
{
    /// <summary>
    /// Netlist container: named nodes plus the devices connecting them.
    /// Node 0 is the reference (ground).
    /// </summary>
    public sealed class AnalogCircuit
    {
        public const int Ground = 0;

        private readonly Dictionary<string, int> _nodeByName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _nodeNames = new List<string> { "0" };
        private readonly List<AnalogDevice> _devices = new List<AnalogDevice>();
        private readonly Dictionary<string, AnalogDevice> _deviceById =
            new Dictionary<string, AnalogDevice>(StringComparer.OrdinalIgnoreCase);

        public AnalogCircuit()
        {
            _nodeByName["0"] = Ground;
            _nodeByName["gnd"] = Ground;
        }

        /// <summary>Number of non-ground nodes.</summary>
        public int NodeCount => _nodeNames.Count - 1;

        public IReadOnlyList<string> NodeNames => _nodeNames;

        public IReadOnlyList<AnalogDevice> Devices => _devices;

        /// <summary>Returns the node with this name, creating it on first use.</summary>
        public int Node(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Node name is empty.", nameof(name));
            if (_nodeByName.TryGetValue(name, out int index)) return index;
            index = _nodeNames.Count;
            _nodeNames.Add(name);
            _nodeByName[name] = index;
            return index;
        }

        public bool TryGetNode(string name, out int node) => _nodeByName.TryGetValue(name, out node);

        /// <summary>Makes <paramref name="name"/> another name for ground. Must be called before the node is used.</summary>
        public void AliasGround(string name)
        {
            if (_nodeByName.TryGetValue(name, out int existing) && existing != Ground)
            {
                throw new InvalidOperationException($"Node '{name}' already exists and cannot become ground.");
            }

            _nodeByName[name] = Ground;
        }

        /// <summary>Creates a node private to a device (series resistance tap, pole node...).</summary>
        public int InternalNode(string ownerId, string suffix) => Node(ownerId + "#" + suffix);

        public T Add<T>(T device) where T : AnalogDevice
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (_deviceById.ContainsKey(device.Id))
            {
                throw new InvalidOperationException($"Duplicate device id '{device.Id}'.");
            }

            _devices.Add(device);
            _deviceById[device.Id] = device;
            return device;
        }

        public AnalogDevice? Find(string id) => _deviceById.TryGetValue(id, out var device) ? device : null;

        public T? Find<T>(string id) where T : AnalogDevice => Find(id) as T;
    }
}
