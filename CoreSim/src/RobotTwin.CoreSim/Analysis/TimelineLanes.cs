using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analysis.Recording;

namespace RobotTwin.CoreSim.Analysis
{
    /// <summary>[unified-timeline] How a lane is drawn.</summary>
    public enum TimelineLaneKind
    {
        /// <summary>Instants with a text: events, serial lines.</summary>
        Marks,

        /// <summary>A pin's level, edge by edge.</summary>
        Logic,

        /// <summary>Intervals: an interrupt handler from entry to RETI.</summary>
        Spans,

        /// <summary>A recorded channel's value over the frames.</summary>
        Analog,
    }

    /// <summary>[unified-timeline] One row of the unified timeline.</summary>
    public sealed class TimelineLane
    {
        public string Group { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public TimelineLaneKind Kind { get; set; }

        /// <summary>The colour family: "failure", "warning", "impact", "info", "serial", "logic", "isr", "analog".</summary>
        public string Tone { get; set; } = string.Empty;

        /// <summary>Shown before its group is opened in full (the lanes that tell the run's story; the rest one click away).</summary>
        public bool Default { get; set; }

        public List<(double Time, string Text)> Marks { get; } = new List<(double, string)>();
        public LogicTrack? Logic { get; set; }
        public List<(double Start, double End)> Spans { get; } = new List<(double, double)>();
        public Channel? Channel { get; set; }
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// An analog lane whose every value is 0 or 1 (a PLC's coil or input, a digital pin's state, a fuse that blew):
        /// drawn as a logic trace, read as HIGH / LOW.
        /// </summary>
        public bool Binary { get; set; }

        /// <summary>An analog lane's range over the run (NaN samples left out).</summary>
        public double Min { get; set; } = double.NaN;

        public double Max { get; set; } = double.NaN;

        /// <summary>What the lane says at <paramref name="t"/>, for the pointer's readout; empty when nothing is there.</summary>
        public string ReadAt(double t, double tolerance, ChannelStore store)
        {
            switch (Kind)
            {
                case TimelineLaneKind.Marks:
                {
                    var near = Marks.Where(m => Math.Abs(m.Time - t) <= tolerance).OrderBy(m => Math.Abs(m.Time - t)).Select(m => m.Text).FirstOrDefault();
                    return near ?? string.Empty;
                }

                case TimelineLaneKind.Logic:
                    return Logic == null || t < Logic.Start ? string.Empty : Logic.LevelAt(t) ? "HIGH" : "LOW";
                case TimelineLaneKind.Spans:
                {
                    foreach (var (s, e) in Spans)
                        if (t >= s - tolerance && t <= e + tolerance)
                            return SiTime(e - s);
                    return string.Empty;
                }

                default:
                {
                    if (Channel == null || store.Count == 0) return string.Empty;
                    int i = TimelineLanes.FrameAt(store, t);
                    double v = i < Channel.Count ? Channel[i] : double.NaN;
                    if (double.IsNaN(v)) return string.Empty;
                    if (Binary) return v >= 0.5 ? "HIGH" : "LOW";
                    return Format(v) + (Unit.Length > 0 ? " " + Unit : string.Empty);
                }
            }
        }

        internal static string Format(double v)
        {
            double a = Math.Abs(v);
            return a != 0 && (a < 1e-3 || a >= 1e5) ? v.ToString("0.###e0", System.Globalization.CultureInfo.CurrentCulture) : v.ToString(a < 1 ? "0.####" : "0.###", System.Globalization.CultureInfo.CurrentCulture);
        }

        internal static string SiTime(double s) => s < 1e-3 ? (s * 1e6).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " µs"
            : s < 1 ? (s * 1e3).ToString("0.###", System.Globalization.CultureInfo.CurrentCulture) + " ms" : s.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture) + " s";
    }

    /// <summary>
    /// [unified-timeline] A render's whole record on one time axis, as lanes: what happened (failures, warnings,
    /// impacts, information), what each board printed, each board's interrupts and pins edge by edge, and every
    /// recorded quantity (circuit signals and node voltages, joint angles, bodies' motion, the scene's contacts).
    /// Groups come in that order; in each, the lanes that tell the run's story are <see cref="TimelineLane.Default"/>.
    /// </summary>
    public static class TimelineLanes
    {
        public static List<TimelineLane> Build(AnalysisResult r)
        {
            var lanes = new List<TimelineLane>();

            // ---------------------------------------------------------------- events, by what they are
            foreach (var (kind, title) in new[] { ("failure", "Arızalar"), ("warning", "Uyarılar"), ("impact", "Çarpmalar"), ("info", "Bilgi") })
            {
                var ev = r.Events.Where(e => string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToList();
                if (ev.Count == 0) continue;
                var lane = new TimelineLane { Group = "Olaylar", Title = title, Kind = TimelineLaneKind.Marks, Tone = kind, Default = true };
                foreach (var e in ev) lane.Marks.Add((e.Time, (e.Source.Length > 0 ? e.Source + ": " : string.Empty) + e.Message));
                lanes.Add(lane);
            }

            // ---------------------------------------------------------------- each board: what it printed, its interrupts, its pins
            string NameOf(string key) => r.BoardNames.TryGetValue(key, out var n) ? n : key;
            var boards = r.Interrupts.Select(i => i.Board).Concat(r.Logic.Select(l => l.Board)).Concat(r.Serial.Select(s => NameOf(s.Board)))
                .Distinct(StringComparer.Ordinal).OrderBy(b => b, StringComparer.OrdinalIgnoreCase);
            foreach (string board in boards)
            {
                string group = "Yazılım · " + board;
                var printed = r.Serial.Where(s => NameOf(s.Board) == board).ToList();
                if (printed.Count > 0)
                {
                    var lane = new TimelineLane { Group = group, Title = "Seri port", Kind = TimelineLaneKind.Marks, Tone = "serial", Default = true };
                    foreach (var s in printed) lane.Marks.Add((s.Time, s.Text.TrimEnd('\r', '\n')));
                    lanes.Add(lane);
                }

                foreach (var v in r.Interrupts.Where(i => i.Board == board).GroupBy(i => i.Vector).OrderBy(v => v.Key, StringComparer.Ordinal))
                {
                    var lane = new TimelineLane { Group = group, Title = v.Key, Kind = TimelineLaneKind.Spans, Tone = "isr", Default = true };
                    foreach (var i in v) lane.Spans.Add((i.Start, i.End));
                    lanes.Add(lane);
                }

                foreach (var l in r.Logic.Where(l => l.Board == board).OrderBy(l => PinOrder(l.Pin)))
                    lanes.Add(new TimelineLane { Group = group, Title = l.Pin + (l.Truncated ? " …" : string.Empty), Kind = TimelineLaneKind.Logic, Tone = "logic", Logic = l, Default = true });
            }

            // ---------------------------------------------------------------- every recorded quantity, by its group
            var store = r.Channels;
            int Rank(string g) => g.StartsWith("Devre", StringComparison.Ordinal) ? 0 : g.StartsWith("Eklemler", StringComparison.Ordinal) ? 1
                : g.StartsWith("Gövdeler", StringComparison.Ordinal) ? 2 : g.StartsWith("Sahne", StringComparison.Ordinal) ? 3 : 4;
            var shown = new Dictionary<string, int>(StringComparer.Ordinal);
            // the playback's own copy of the poses ("Sahne/Poz (oynatma)") repeats the bodies' channels: left out
            foreach (var c in store.Channels.Where(c => c.Count > 0 && !c.Group.StartsWith("Sahne/Poz", StringComparison.Ordinal))
                         .OrderBy(c => Rank(c.Group)).ThenBy(c => c.Group, StringComparer.OrdinalIgnoreCase))
            {
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                bool binary = true;
                for (int i = 0; i < c.Count; i++)
                {
                    double v = c[i];
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    lo = Math.Min(lo, v);
                    hi = Math.Max(hi, v);
                    binary &= v == 0 || v == 1;
                }

                if (lo > hi) continue;   // never had a value
                string group = c.Group.Replace("/", " · ");
                // a quantity that never moved tells nothing on a time axis (an unused pin at 0 V): it waits behind "all"
                bool moved = hi - lo > 1e-6 * Math.Max(1, Math.Max(Math.Abs(hi), Math.Abs(lo)));
                shown.TryGetValue(group, out int n);
                bool story = moved && Story(c.Name) && n < MaxDefaultPerGroup;
                if (story) shown[group] = n + 1;
                lanes.Add(new TimelineLane
                {
                    Group = group, Title = c.Label, Kind = TimelineLaneKind.Analog, Tone = binary ? "logic" : "analog", Channel = c, Unit = c.Unit, Min = lo, Max = hi,
                    Default = story, Binary = binary && hi > lo,
                });
            }

            return lanes;
        }

        /// <summary>A group shows this many lanes until it is opened in full.</summary>
        public const int MaxDefaultPerGroup = 8;

        /// <summary>The quantities that tell the run's story: circuit signals, node voltages, joint angles, body speeds, contacts.</summary>
        private static bool Story(string name) =>
            name.StartsWith("dev/", StringComparison.Ordinal) || name.StartsWith("node/", StringComparison.Ordinal)
            || (name.StartsWith("joint/", StringComparison.Ordinal) && name.EndsWith("/angle", StringComparison.Ordinal))
            || (name.StartsWith("body/", StringComparison.Ordinal) && name.EndsWith("/v", StringComparison.Ordinal))
            || name == "scene/contacts" || name == "scene/peak_contact";

        private static int PinOrder(string pin) =>
            pin.StartsWith("GPIO", StringComparison.Ordinal) && int.TryParse(pin.Substring(4), out int g) ? g
            : pin.Length > 1 && int.TryParse(pin.Substring(1), out int n) ? (pin[0] == 'A' ? 100 : 0) + n : 1000;

        /// <summary>The frame at or just before <paramref name="t"/>.</summary>
        public static int FrameAt(ChannelStore store, double t)
        {
            int lo = 0, hi = store.Count - 1;
            if (hi < 0) return 0;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (store.Time(mid) <= t) lo = mid;
                else hi = mid - 1;
            }

            return lo;
        }
    }
}
