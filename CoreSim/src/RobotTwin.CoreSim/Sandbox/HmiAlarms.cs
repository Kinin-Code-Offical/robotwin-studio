using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [hmi-alarms] An alarm on a PLC variable, as operator panels define them: a level (high-high, high, low, low-low, or
    /// a message), a condition against a threshold, and the text the operator reads.
    /// </summary>
    public sealed class HmiAlarm
    {
        public const string HighHigh = "HH", High = "H", Low = "L", LowLow = "LL", Message = "mesaj";

        public static readonly string[] Levels = { HighHigh, High, Low, LowLow, Message };

        public static readonly string[] Conditions = { ">=", ">", "<=", "<", "=", "<>" };

        public string Plc { get; set; } = string.Empty;

        public string Variable { get; set; } = string.Empty;

        public string Level { get; set; } = High;

        public string Condition { get; set; } = ">=";

        public double Threshold { get; set; } = 1;

        public string Text { get; set; } = string.Empty;

        /// <summary>Whether <paramref name="value"/> raises it (a NaN, a variable not readable, never does).</summary>
        public bool Holds(double value) => !double.IsNaN(value) && Condition switch
        {
            ">" => value > Threshold,
            "<=" => value <= Threshold,
            "<" => value < Threshold,
            "=" => Math.Abs(value - Threshold) < 1e-9,
            "<>" => Math.Abs(value - Threshold) >= 1e-9,
            _ => value >= Threshold,
        };

        /// <summary>Its severity for sorting (high-high first).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int Severity => Level switch { HighHigh => 0, LowLow => 1, High => 2, Low => 3, _ => 4 };

        public string Describe() => Text.Length > 0 ? Text : string.Format(CultureInfo.CurrentCulture, "{0} {1} {2}", Variable, Condition, Threshold);
    }

    /// <summary>[hmi-alarms] One occurrence: when it came, whether it went, whether the operator acknowledged it.</summary>
    public sealed class HmiAlarmEvent
    {
        public HmiAlarmEvent(HmiAlarm alarm, double raised)
        {
            Alarm = alarm;
            Raised = raised;
        }

        public HmiAlarm Alarm { get; }

        public double Raised { get; }

        /// <summary>When the condition stopped holding (NaN while it still does).</summary>
        public double Cleared { get; internal set; } = double.NaN;

        /// <summary>When the operator acknowledged it (NaN until then).</summary>
        public double Acknowledged { get; internal set; } = double.NaN;

        public bool Active => double.IsNaN(Cleared);

        public bool Acked => !double.IsNaN(Acknowledged);
    }

    /// <summary>
    /// [hmi-alarms] Watches the alarms as the world runs: an alarm comes when its condition starts to hold and goes when it
    /// stops; the list shows the ones still active and the ones gone but not yet acknowledged (as a panel keeps them until
    /// the operator has seen them). Every occurrence stays in <see cref="History"/>.
    /// </summary>
    public sealed class HmiAlarmMonitor
    {
        private readonly Dictionary<HmiAlarm, HmiAlarmEvent> _open = new Dictionary<HmiAlarm, HmiAlarmEvent>();

        public List<HmiAlarmEvent> History { get; } = new List<HmiAlarmEvent>();

        /// <summary>What the panel's list shows: active ones and unacknowledged ones, most severe and newest first.</summary>
        public IEnumerable<HmiAlarmEvent> Shown => History.Where(e => e.Active || !e.Acked).OrderBy(e => e.Alarm.Severity).ThenByDescending(e => e.Raised);

        public int ActiveCount => History.Count(e => e.Active);

        public int UnacknowledgedCount => History.Count(e => !e.Acked);

        /// <summary>Checks every alarm at <paramref name="time"/> with the values <paramref name="read"/> gives (plc, variable).</summary>
        public void Update(IEnumerable<HmiAlarm> alarms, Func<string, string, double> read, double time)
        {
            foreach (var a in alarms)
            {
                bool holds = a.Holds(read(a.Plc, a.Variable));
                if (holds && !_open.ContainsKey(a))
                {
                    var e = new HmiAlarmEvent(a, time);
                    _open[a] = e;
                    History.Add(e);
                    if (History.Count > 5000) History.RemoveAt(0);
                }
                else if (!holds && _open.TryGetValue(a, out var open))
                {
                    open.Cleared = time;
                    _open.Remove(a);
                }
            }
        }

        public void Acknowledge(HmiAlarmEvent e, double time)
        {
            if (!e.Acked) e.Acknowledged = time;
        }

        public void AcknowledgeAll(double time)
        {
            foreach (var e in History.Where(e => !e.Acked)) e.Acknowledged = time;
        }

        /// <summary>The world was started again: nothing is open.</summary>
        public void Reset()
        {
            _open.Clear();
            History.Clear();
        }
    }
}
