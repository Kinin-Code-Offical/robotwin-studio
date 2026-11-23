using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Radio;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>
    /// [replay] One thing the user did to a running world, stamped with the simulation time it took
    /// effect at: a controller report that changed, a controller switched on or off, pairing mode,
    /// text sent on the serial monitor, a USB cable plugged in, an IR key press, a conveyor started,
    /// a light curtain interrupted, a launch switch thrown.
    /// </summary>
    public sealed class InputEvent
    {
        public const string Gamepad = "gamepad";
        public const string GamepadPower = "gamepad.power";
        public const string Pairing = "gamepad.pair";
        public const string Serial = "serial";
        public const string Usb = "usb";
        public const string Ir = "ir";
        public const string Conveyor = "conveyor";
        public const string Curtain = "curtain";
        public const string Switch = "switch";

        /// <summary>Simulation time (s) from the start of the run.</summary>
        public double Time { get; set; }

        public string Kind { get; set; } = string.Empty;

        /// <summary>World object the input went to (controller, USB cable, remote, conveyor …); empty = every board (serial).</summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>Serial text.</summary>
        public string? Text { get; set; }

        /// <summary>On/off as 1/0, an IR command, a duration (s).</summary>
        public double Value { get; set; }

        /// <summary>The whole controller report (sticks, triggers, buttons, motion) for <see cref="Gamepad"/>.</summary>
        public GamepadState? Pad { get; set; }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0:0.000} s {1} {2}", Time, Kind, Target);
    }

    /// <summary>
    /// [replay] The time-stamped inputs of a live run, so that a later run of the same world — the offline
    /// analysis / render — receives exactly the same controller reports and interventions at the same
    /// simulation times. Controller reports are stored only when they change.
    /// </summary>
    public sealed class InputJournal
    {
        public List<InputEvent> Events { get; set; } = new List<InputEvent>();

        /// <summary>Length of the recorded run (s).</summary>
        public double Duration { get; set; }

        [JsonIgnore]
        public int Count => Events.Count;

        private readonly Dictionary<string, GamepadState> _lastPad = new Dictionary<string, GamepadState>(StringComparer.OrdinalIgnoreCase);

        public void Add(InputEvent e)
        {
            // Kept in time order; inputs of the same instant keep the order they happened in.
            int i = Events.Count;
            while (i > 0 && Events[i - 1].Time > e.Time) i--;
            Events.Insert(i, e);
        }

        /// <summary>Records a controller report if it differs from the last one recorded for that controller.</summary>
        public bool AddPad(double time, string padId, GamepadState state)
        {
            if (_lastPad.TryGetValue(padId, out var last) && SameReport(last, state)) return false;
            var copy = state.Clone();
            _lastPad[padId] = copy;
            Add(new InputEvent { Time = time, Kind = InputEvent.Gamepad, Target = padId, Pad = copy.Clone() });
            return true;
        }

        public void Note(double time, string kind, string target, double value = 0, string? text = null) =>
            Add(new InputEvent { Time = time, Kind = kind, Target = target, Value = value, Text = text });

        private static bool SameReport(GamepadState a, GamepadState b) =>
            a.AxisX == b.AxisX && a.AxisY == b.AxisY && a.AxisRX == b.AxisRX && a.AxisRY == b.AxisRY && a.Brake == b.Brake && a.Throttle == b.Throttle
            && a.Buttons == b.Buttons && a.Dpad == b.Dpad && a.MiscButtons == b.MiscButtons
            && a.GyroX == b.GyroX && a.GyroY == b.GyroY && a.GyroZ == b.GyroZ && a.AccelX == b.AccelX && a.AccelY == b.AccelY && a.AccelZ == b.AccelZ
            && a.Touch == b.Touch && a.TouchX == b.TouchX && a.TouchY == b.TouchY && a.Battery == b.Battery;

        /// <summary>"214 kumanda raporu, 2 seri gönderim …" for logs.</summary>
        public string Summary()
        {
            if (Events.Count == 0) return "kayıtlı giriş yok";
            var names = new Dictionary<string, string>
            {
                [InputEvent.Gamepad] = "kumanda raporu",
                [InputEvent.GamepadPower] = "kumanda aç/kapat",
                [InputEvent.Pairing] = "eşleştirme",
                [InputEvent.Serial] = "seri gönderim",
                [InputEvent.Usb] = "USB tak/çıkar",
                [InputEvent.Ir] = "IR tuşu",
                [InputEvent.Conveyor] = "bant komutu",
                [InputEvent.Curtain] = "perde kesme",
                [InputEvent.Switch] = "anahtar"
            };
            return string.Join(", ", Events.GroupBy(e => e.Kind).Select(g => g.Count() + " " + (names.TryGetValue(g.Key, out var n) ? n : g.Key)));
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        public static InputJournal FromJson(string json) => JsonSerializer.Deserialize<InputJournal>(json, Json) ?? new InputJournal();

        public void Save(string path) => File.WriteAllText(path, ToJson());

        public static InputJournal Load(string path) => FromJson(File.ReadAllText(path));
    }

    /// <summary>
    /// [replay] Feeds an <see cref="InputJournal"/> into a <see cref="WorldRunner"/>: every step is split at the
    /// recorded times so each input lands at the simulation instant it was made, then the world runs on.
    /// </summary>
    public sealed class InputReplay
    {
        private readonly InputJournal _journal;
        private int _next;

        public InputReplay(InputJournal journal)
        {
            _journal = journal;
        }

        /// <summary>Inputs applied so far.</summary>
        public int Applied => _next;

        /// <summary>Inputs the world could not take (an object missing from it).</summary>
        public List<string> Skipped { get; } = new List<string>();

        /// <summary>Advances <paramref name="runner"/> by <paramref name="dt"/>, applying the inputs that fall inside the step.</summary>
        public void Step(WorldRunner runner, double dt)
        {
            double end = runner.Time + dt;
            var events = _journal.Events;
            while (_next < events.Count && events[_next].Time <= end + 1e-12)
            {
                var e = events[_next];
                double gap = e.Time - runner.Time;
                if (gap > 1e-9) runner.Step(gap);
                if (!Apply(runner, e)) Skipped.Add(e.ToString());
                _next++;
            }

            double rest = end - runner.Time;
            if (rest > 1e-9) runner.Step(rest);
        }

        /// <summary>Applies the inputs recorded at or before the current time (call once before the first step).</summary>
        public void Start(WorldRunner runner) => Step(runner, 0);

        public static bool Apply(WorldRunner runner, InputEvent e)
        {
            switch (e.Kind)
            {
                case InputEvent.Gamepad:
                    if (e.Pad == null || runner.Radio?.Find(e.Target) is not GamepadNode pad) return false;
                    pad.Input.CopyFrom(e.Pad);
                    return true;
                case InputEvent.GamepadPower:
                    if (runner.Radio?.Find(e.Target) is not GamepadNode p) return false;
                    p.PoweredOn = e.Value > 0.5;
                    return true;
                case InputEvent.Pairing:
                    if (runner.Radio is not { } radio || radio.Find(e.Target) is not GamepadNode pp) return false;
                    radio.StartPairing(pp);
                    return true;
                case InputEvent.Serial:
                    runner.WriteSerial(e.Text ?? string.Empty, string.IsNullOrEmpty(e.Target) ? null : e.Target);
                    return true;
                case InputEvent.Usb:
                {
                    var host = runner.World.Find(e.Target);
                    if (host == null) return false;
                    bool plugged = e.Value > 0.5;
                    host.Parameters["plugged"] = plugged ? "true" : "false";
                    if (runner.Circuit != null && host.Parameters.TryGetValue("target", out var t) && !string.IsNullOrWhiteSpace(t)) WorldUsb.SetPlugged(runner.Circuit, t, plugged);
                    return true;
                }

                case InputEvent.Ir:
                {
                    if (runner.Radio is not { } r || runner.World.Find(e.Target) is not { } o) return false;
                    var emitter = r.Ir.Emitters.FirstOrDefault(x => x.Id == e.Target);
                    if (emitter == null) return false;
                    byte address = WorldRadio.ParseByte(o.Parameters.TryGetValue("address", out var a) ? a : null);
                    r.Ir.SendNec(emitter, runner.Time + 1e-4, address, (byte)e.Value);
                    return true;
                }

                case InputEvent.Conveyor:
                    if (runner.Physics?.ConveyorOf(e.Target) is not { } belt) return false;
                    belt.SetRunning(e.Value > 0.5);
                    return true;
                case InputEvent.Curtain:
                    if (runner.Physics == null) return false;
                    runner.Physics.InterruptCurtain(e.Target, e.Value);
                    return true;
                case InputEvent.Switch:
                    if (runner.Physics?.SwitchState(e.Target) is null) return false;
                    runner.Physics.SetSwitch(e.Target, e.Value > 0.5);
                    return true;
                default:
                    return false;
            }
        }
    }
}
