using System;

namespace RobotTwin.CoreSim.Analog
{
    /// <summary>
    /// Remembers the "device:key" strings built while publishing signals. Devices
    /// report the same (id, key) string instances in the same order every time, so
    /// a positional cache checked by reference avoids building a new string per
    /// signal per step. A mismatch simply builds (and remembers) the string again.
    /// </summary>
    internal sealed class SignalKeyCache
    {
        private const int MaxEntries = 1 << 16;

        private string?[] _ids = new string?[64];
        private string?[] _keys = new string?[64];
        private string?[] _full = new string?[64];
        private int _cursor;

        /// <summary>Call before each publishing pass.</summary>
        public void Rewind() => _cursor = 0;

        public string Get(string deviceId, string key)
        {
            int i = _cursor++;
            if (i < _ids.Length && ReferenceEquals(_ids[i], deviceId) && ReferenceEquals(_keys[i], key))
            {
                return _full[i]!;
            }

            string full = deviceId + ":" + key;
            if (i >= MaxEntries) return full;
            if (i >= _ids.Length)
            {
                int capacity = Math.Max(_ids.Length * 2, i + 1);
                Array.Resize(ref _ids, capacity);
                Array.Resize(ref _keys, capacity);
                Array.Resize(ref _full, capacity);
            }

            _ids[i] = deviceId;
            _keys[i] = key;
            _full[i] = full;
            return full;
        }
    }
}
