using System;
using System.Collections;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Analog
{
    /// <summary>
    /// Remembers where each "device:key" signal is kept while publishing. Devices report the same (id, key) string
    /// instances in the same order every time, so a positional cache checked by reference finds the signal's slot without
    /// building a string or hashing one per signal per step. A mismatch simply looks the slot up (and remembers it) again.
    /// </summary>
    internal sealed class SignalKeyCache
    {
        private const int MaxEntries = 1 << 16;

        private string?[] _ids = new string?[64];
        private string?[] _keys = new string?[64];
        private int[] _slots = new int[64];
        private int _cursor;

        /// <summary>Call before each publishing pass.</summary>
        public void Rewind() => _cursor = 0;

        /// <summary>Forgets every slot (the table they point into was cleared).</summary>
        public void Clear()
        {
            Array.Clear(_ids, 0, _ids.Length);
            Array.Clear(_keys, 0, _keys.Length);
            _cursor = 0;
        }

        /// <summary>[split] The slot remembered at this position, or false (and nothing remembered) when it is not that signal's.</summary>
        public bool TrySlot(string deviceId, string key, out int slot)
        {
            int i = _cursor++;
            if (i < _ids.Length && ReferenceEquals(_ids[i], deviceId) && ReferenceEquals(_keys[i], key))
            {
                slot = _slots[i];
                return true;
            }

            slot = -1;
            return false;
        }

        public int Slot(string deviceId, string key, SignalTable table)
        {
            int i = _cursor++;
            if (i < _ids.Length && ReferenceEquals(_ids[i], deviceId) && ReferenceEquals(_keys[i], key))
            {
                return _slots[i];
            }

            int slot = table.Slot(deviceId + ":" + key);
            if (i >= MaxEntries) return slot;
            if (i >= _ids.Length)
            {
                int capacity = Math.Max(_ids.Length * 2, i + 1);
                Array.Resize(ref _ids, capacity);
                Array.Resize(ref _keys, capacity);
                Array.Resize(ref _slots, capacity);
            }

            _ids[i] = deviceId;
            _keys[i] = key;
            _slots[i] = slot;
            return slot;
        }
    }

    /// <summary>
    /// [perf] The published signals ("NET:node", "device:key"): a name is hashed once, when it first appears, and after that
    /// every step writes its value into an array by slot. Writing them into a dictionary by name cost more than solving the
    /// circuit in a big scene (25 robots: 70 µs of every 50 µs slice). Read like a dictionary; names case-insensitive.
    /// </summary>
    internal sealed class SignalTable : IReadOnlyDictionary<string, double>
    {
        private readonly Dictionary<string, int> _slot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string[] _names = new string[64];
        private double[] _values = new double[64];
        private int _count;

        public int Slot(string name)
        {
            if (_slot.TryGetValue(name, out int slot)) return slot;
            slot = _count;
            if (slot == _names.Length)
            {
                Array.Resize(ref _names, slot * 2);
                var values = new double[slot * 2];
                Array.Copy(_values, values, slot);
                _values = values;
            }

            _names[slot] = name;
            _values[slot] = 0.0;
            _slot[name] = slot;
            _count = slot + 1;
            return slot;
        }

        public void Set(int slot, double value) => _values[slot] = value;

        public void Set(string name, double value) => _values[Slot(name)] = value;

        public void Clear()
        {
            _slot.Clear();
            _count = 0;
        }

        public double this[string key] => _slot.TryGetValue(key, out int s) ? _values[s] : throw new KeyNotFoundException(key);

        public IEnumerable<string> Keys
        {
            get
            {
                for (int i = 0; i < _count; i++) yield return _names[i];
            }
        }

        public IEnumerable<double> Values
        {
            get
            {
                for (int i = 0; i < _count; i++) yield return _values[i];
            }
        }

        public int Count => _count;

        public bool ContainsKey(string key) => _slot.ContainsKey(key);

        public bool TryGetValue(string key, out double value)
        {
            if (_slot.TryGetValue(key, out int s))
            {
                value = _values[s];
                return true;
            }

            value = 0.0;
            return false;
        }

        public IEnumerator<KeyValuePair<string, double>> GetEnumerator()
        {
            for (int i = 0; i < _count; i++) yield return new KeyValuePair<string, double>(_names[i], _values[i]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
