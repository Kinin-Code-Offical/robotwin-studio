using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Industrial.OpcUa
{
    /// <summary>
    /// [opcua] A PLC's variables as the OPC UA server sees them, across threads: the PLC publishes their values after each
    /// scan, a client's writes wait here until the next scan takes them (as a real PLC's communication processor hands them
    /// over at the scan boundary). The set of tags changes when another program is loaded.
    /// </summary>
    public sealed class UaTagImage
    {
        public readonly struct Tag
        {
            public Tag(string name, StType type, string typeName, bool writable, double value, string? text = null)
            {
                Name = name;
                Type = type;
                TypeName = typeName;
                Writable = writable;
                Value = value;
                Text = text;
            }

            public string Name { get; }
            public StType Type { get; }
            public string TypeName { get; }
            public bool Writable { get; }
            public double Value { get; }

            /// <summary>[plc-string] A STRING's text (null for the others).</summary>
            public string? Text { get; }
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Tag> _tags = new Dictionary<string, Tag>(StringComparer.Ordinal);
        private readonly List<(string Name, double Value, string? Text)> _pending = new List<(string, double, string?)>();
        private DateTime _stamp = DateTime.UtcNow;

        public UaTagImage(string name, string bind, int port)
        {
            Name = name;
            Bind = bind;
            Port = port;
        }

        /// <summary>The PLC's name: its object in the address space.</summary>
        public string Name { get; }

        public string Bind { get; }
        public int Port { get; }

        /// <summary>RUN (scanning) or STOP.</summary>
        public bool Running { get; private set; }

        /// <summary>Counts every change of the tag set (a program loaded), so the server rebuilds the PLC's nodes.</summary>
        public int Generation { get; private set; }

        public IReadOnlyList<Tag> Tags
        {
            get
            {
                lock (_gate) return _tags.Values.ToList();
            }
        }

        public bool TryGet(string name, out Tag tag, out DateTime stamp)
        {
            lock (_gate)
            {
                stamp = _stamp;
                return _tags.TryGetValue(name, out tag);
            }
        }

        /// <summary>After a scan: the program's variables and its state.</summary>
        public void Publish(IEnumerable<Tag> tags, bool running)
        {
            lock (_gate)
            {
                var list = tags.ToList();
                bool same = list.Count == _tags.Count && list.All(t => _tags.TryGetValue(t.Name, out var o) && o.Type == t.Type && o.TypeName == t.TypeName);
                if (!same)
                {
                    _tags.Clear();
                    Generation++;
                }

                foreach (var t in list) _tags[t.Name] = t;
                Running = running;
                _stamp = DateTime.UtcNow;
            }
        }

        /// <summary>A client's write: kept for the next scan. False for a tag that is not there or not writable.</summary>
        public bool Write(string name, double value)
        {
            lock (_gate)
            {
                if (!_tags.TryGetValue(name, out var t) || !t.Writable || t.Type == StType.String) return false;
                _pending.Add((name, value, null));
                _tags[name] = new Tag(t.Name, t.Type, t.TypeName, t.Writable, value);   // read back at once, as the PLC will have it
                return true;
            }
        }

        /// <summary>[plc-string] A client's write of a STRING: kept for the next scan.</summary>
        public bool WriteText(string name, string text)
        {
            lock (_gate)
            {
                if (!_tags.TryGetValue(name, out var t) || !t.Writable || t.Type != StType.String) return false;
                _pending.Add((name, 0, text));
                _tags[name] = new Tag(t.Name, t.Type, t.TypeName, t.Writable, 0, text);
                return true;
            }
        }

        /// <summary>Before a scan: the writes that came in since the last one, oldest first (a STRING's with its Text).</summary>
        public List<(string Name, double Value, string? Text)> TakeWrites()
        {
            lock (_gate)
            {
                var w = _pending.ToList();
                _pending.Clear();
                return w;
            }
        }
    }
}
