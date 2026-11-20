using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RobotTwin.CoreSim.Analysis.Recording
{
    /// <summary>[analysis] One recorded signal: a column of samples sharing the store's time axis.</summary>
    public sealed class Channel : IReadOnlyList<double>
    {
        internal double[] Data = new double[256];

        internal Channel(string name, string label, string unit, string group)
        {
            Name = name;
            Label = label;
            Unit = unit;
            Group = group;
        }

        /// <summary>Unique key ("body/O1/pos.x", "dev/O3/R1:T" …).</summary>
        public string Name { get; }

        /// <summary>Display name (Turkish UI text).</summary>
        public string Label { get; set; }

        public string Unit { get; }

        /// <summary>Tree group ("Gövdeler/Şase", "Devre/Düğümler" …).</summary>
        public string Group { get; }

        /// <summary>Derived from other channels (not part of the recording file's raw data).</summary>
        public bool Derived { get; internal set; }

        public int Count { get; internal set; }

        public double this[int index] => index < Count ? Data[index] : throw new ArgumentOutOfRangeException(nameof(index));

        internal void Append(double v)
        {
            if (Count == Data.Length) Array.Resize(ref Data, Data.Length * 2);
            Data[Count++] = v;
        }

        internal void Set(int index, double v) => Data[index] = v;

        public double[] ToArray()
        {
            var a = new double[Count];
            Array.Copy(Data, a, Count);
            return a;
        }

        public IEnumerator<double> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return Data[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"{Group}/{Label} [{Unit}] ({Count})";
    }

    /// <summary>
    /// [analysis] Columnar store of every recorded signal on one shared time axis. Channels may
    /// appear mid-run (earlier samples are NaN). Binary format: little-endian, one block per channel
    /// (doubles), written into the .rwanalysis zip (deflate-compressed there).
    /// </summary>
    public sealed class ChannelStore
    {
        private const int Magic = 0x48435752;   // "RWCH"
        private readonly List<Channel> _channels = new List<Channel>();
        private readonly Dictionary<string, Channel> _byName = new Dictionary<string, Channel>(StringComparer.Ordinal);
        private double[] _time = new double[256];

        public int Count { get; private set; }
        public IReadOnlyList<Channel> Channels => _channels;

        public double Time(int i) => _time[i];

        public double[] TimeArray()
        {
            var a = new double[Count];
            Array.Copy(_time, a, Count);
            return a;
        }

        public IReadOnlyList<double> TimeAxis => new TimeView(this);

        public double Duration => Count > 0 ? _time[Count - 1] - _time[0] : 0;

        public Channel? Find(string name) => _byName.TryGetValue(name, out var c) ? c : null;

        /// <summary>Gets or creates a channel; a new channel is back-filled with NaN up to the current sample.</summary>
        public Channel Get(string name, string label, string unit, string group)
        {
            if (_byName.TryGetValue(name, out var c)) return c;
            c = new Channel(name, label, unit, group);
            int filled = Count;
            if (c.Data.Length < Math.Max(256, filled)) Array.Resize(ref c.Data, Math.Max(256, filled * 2));
            for (int i = 0; i < filled; i++) c.Data[i] = double.NaN;
            c.Count = filled;
            _channels.Add(c);
            _byName[name] = c;
            return c;
        }

        /// <summary>Adds a computed channel (same length as the time axis).</summary>
        public Channel AddDerived(string name, string label, string unit, string group, double[] values)
        {
            if (_byName.TryGetValue(name, out var old))
            {
                _channels.Remove(old);
                _byName.Remove(name);
            }

            var c = new Channel(name, label, unit, group) { Derived = true };
            c.Data = new double[Math.Max(1, values.Length)];
            Array.Copy(values, c.Data, values.Length);
            c.Count = values.Length;
            _channels.Add(c);
            _byName[name] = c;
            return c;
        }

        /// <summary>Starts a sample at time t: every channel gets a NaN slot that <see cref="Set"/> fills.</summary>
        public void BeginSample(double t)
        {
            if (Count == _time.Length) Array.Resize(ref _time, _time.Length * 2);
            _time[Count++] = t;
            foreach (var c in _channels)
            {
                while (c.Count < Count) c.Append(double.NaN);
            }
        }

        public void Set(Channel c, double v) => c.Set(Count - 1, v);

        public void Set(string name, string label, string unit, string group, double v)
        {
            var c = Get(name, label, unit, group);
            while (c.Count < Count) c.Append(double.NaN);
            c.Set(Count - 1, v);
        }

        /// <summary>Approximate in-memory size (bytes).</summary>
        public long Bytes
        {
            get
            {
                long b = (long)Count * 8;
                foreach (var c in _channels) b += (long)c.Count * 8;
                return b;
            }
        }

        // ================================================================ binary IO

        public void Write(Stream s)
        {
            using var w = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
            w.Write(Magic);
            w.Write(1);
            w.Write(Count);
            for (int i = 0; i < Count; i++) w.Write(_time[i]);
            int raw = 0;
            foreach (var c in _channels)
                if (!c.Derived)
                    raw++;
            w.Write(raw);
            foreach (var c in _channels)
            {
                if (c.Derived) continue;
                w.Write(c.Name);
                w.Write(c.Label);
                w.Write(c.Unit);
                w.Write(c.Group);
                for (int i = 0; i < Count; i++) w.Write(i < c.Count ? c.Data[i] : double.NaN);
            }
        }

        public static ChannelStore Read(Stream s)
        {
            using var r = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
            if (r.ReadInt32() != Magic) throw new InvalidDataException("kanal verisi değil / not a channel block");
            int version = r.ReadInt32();
            if (version != 1) throw new InvalidDataException("kanal sürümü / channel version " + version);
            var store = new ChannelStore();
            int n = r.ReadInt32();
            store._time = new double[Math.Max(256, n)];
            for (int i = 0; i < n; i++) store._time[i] = r.ReadDouble();
            store.Count = n;
            int channels = r.ReadInt32();
            for (int k = 0; k < channels; k++)
            {
                var c = new Channel(r.ReadString(), r.ReadString(), r.ReadString(), r.ReadString());
                c.Data = new double[Math.Max(256, n)];
                for (int i = 0; i < n; i++) c.Data[i] = r.ReadDouble();
                c.Count = n;
                store._channels.Add(c);
                store._byName[c.Name] = c;
            }

            return store;
        }

        private sealed class TimeView : IReadOnlyList<double>
        {
            private readonly ChannelStore _s;

            public TimeView(ChannelStore s)
            {
                _s = s;
            }

            public double this[int index] => _s._time[index];
            public int Count => _s.Count;

            public IEnumerator<double> GetEnumerator()
            {
                for (int i = 0; i < _s.Count; i++) yield return _s._time[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
