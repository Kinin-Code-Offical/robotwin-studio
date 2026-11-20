using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Audio
{
    /// <summary>
    /// Turns the running simulation into sound: motors whine with their speed and load,
    /// servos buzz while their gear train moves, active buzzers beep, relays click when
    /// their contacts change over, hot parts sizzle and failing parts pop, bang or spark.
    /// One instance per simulation session; voices are keyed by device id under a prefix.
    /// </summary>
    public sealed class SimSounds
    {
        private readonly string _prefix;
        private readonly Dictionary<string, bool> _relays = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _servoAngles = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly HashSet<string> _active = new HashSet<string>(StringComparer.Ordinal);
        private DateTime _lastShot;

        public SimSounds(string prefix)
        {
            _prefix = prefix;
        }

        private static SoundEngine Engine => SoundEngine.Shared;

        private static bool On
        {
            get
            {
                var s = AppSettings.Current;
                Engine.Enabled = s.Sound;
                Engine.Volume = Math.Max(0, Math.Min(1, s.Volume));
                return s.Sound && s.Volume > 0;
            }
        }

        /// <summary>Updates continuous voices from the device states (called once per rendered frame).</summary>
        /// <param name="devices">Every device of the running circuit.</param>
        /// <param name="speed">Simulation speed (sound pitch follows sim time).</param>
        /// <param name="hottestC">Hottest part temperature, or NaN.</param>
        public void Update(IEnumerable<AnalogDevice> devices, double speed, double hottestC = double.NaN)
        {
            if (!On)
            {
                Silence();
                return;
            }

            _active.Clear();
            int motors = 0, servos = 0, buzzers = 0;
            double pitch = Math.Max(0.25, Math.Min(2.0, speed));
            foreach (var d in devices)
            {
                string key = _prefix + d.Id;
                switch (d)
                {
                    case DcMotor m when !m.IsFailed && motors < 6:
                    {
                        double rpm = Math.Abs(m.AngularVelocity) * 60 / (2 * Math.PI);
                        if (rpm < 60) break;
                        // Commutator frequency: 3 slots × 2 brushes per motor revolution.
                        double f = rpm / 60.0 * 6.0 * pitch;
                        double load = Math.Min(1, Math.Abs(m.Current) / 0.8);
                        double gain = Math.Min(0.5, 0.12 + 0.35 * Math.Min(1, rpm / 12000)) * (0.7 + 0.3 * load);
                        Engine.SetVoice(key, Timbre.Motor, f, gain, load);
                        _active.Add(key);
                        motors++;
                        break;
                    }

                    case Servo s when !s.IsFailed && servos < 6:
                    {
                        double prev = _servoAngles.TryGetValue(d.Id, out var a) ? a : s.AngleDeg;
                        _servoAngles[d.Id] = s.AngleDeg;
                        bool moving = Math.Abs(s.AngleDeg - prev) > 0.05 || s.SupplyCurrent > s.MovingCurrent * 0.6;
                        if (!moving) break;
                        double load = Math.Min(1, s.SupplyCurrent / Math.Max(1e-3, s.StallCurrent));
                        Engine.SetVoice(key, Timbre.Gear, (180 + 220 * load) * pitch, 0.18 + 0.2 * load, load);
                        _active.Add(key);
                        servos++;
                        break;
                    }

                    case Buzzer b when !b.IsFailed && buzzers < 4:
                        if (b.Loudness <= 0) break;
                        Engine.SetVoice(key, Timbre.Square, 2400, 0.25 * b.Loudness);
                        _active.Add(key);
                        buzzers++;
                        break;

                    case Relay r:
                    {
                        bool on = r.Energized;
                        if (_relays.TryGetValue(d.Id, out var was) && was != on) Shot(OneShot.Click, on ? 0.9 : 0.6);
                        _relays[d.Id] = on;
                        break;
                    }
                }
            }

            if (!double.IsNaN(hottestC) && hottestC > 110)
            {
                string key = _prefix + "~sizzle";
                Engine.SetVoice(key, Timbre.Sizzle, 900, Math.Min(0.35, (hottestC - 110) / 200.0));
                _active.Add(key);
            }

            // Fade voices whose device went quiet this frame.
            Engine.Silence(_prefix, _active);
        }

        /// <summary>One-shot for a simulation event (part failure, contact, impact).</summary>
        public void OnEvent(AnalogEventKind kind, string deviceId, string message, AnalogDevice? device = null)
        {
            if (kind != AnalogEventKind.Failure || !On) return;
            string text = message.ToLowerInvariant();
            if (deviceId.StartsWith("cable:", StringComparison.Ordinal) || text.Contains("kablo") || text.Contains("sigorta"))
            {
                Shot(OneShot.Spark, 0.9);
                Shot(OneShot.Hiss, 0.5);
            }
            else if (device is Capacitor || text.Contains("kondansat") || text.Contains("patla") || text.Contains("dielektrik"))
            {
                Shot(OneShot.Bang, 1.0);
                Shot(OneShot.Hiss, 0.6);
            }
            else
            {
                Shot(OneShot.Pop, 0.9);
                Shot(OneShot.Crackle, 0.5);
            }
        }

        /// <summary>A mechanical knock (impact of the given strength in g).</summary>
        public void Impact(double g)
        {
            if (!On) return;
            Shot(OneShot.Click, Math.Min(1, 0.25 + g / 120));
        }

        public void Silence()
        {
            Engine.Silence(_prefix);
            _servoAngles.Clear();
        }

        public void Reset()
        {
            Silence();
            _relays.Clear();
        }

        private void Shot(OneShot kind, double gain)
        {
            // Many parts failing in the same instant would otherwise add up to a wall of noise.
            var now = DateTime.UtcNow;
            if ((now - _lastShot).TotalMilliseconds < 25 && kind != OneShot.Hiss && kind != OneShot.Crackle) return;
            _lastShot = now;
            Engine.Play(kind, gain);
        }
    }
}
