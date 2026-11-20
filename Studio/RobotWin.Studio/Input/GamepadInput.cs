using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RobotTwin.CoreSim.Sandbox.Radio;

namespace RobotWin.Studio.Input
{
    /// <summary>A controller the Studio can read: a real pad on the PC or the on-screen virtual pad.</summary>
    public interface IGamepadSource : IDisposable
    {
        /// <summary>Stable id ("xinput:0", "sony:&lt;device path&gt;", "virtual").</summary>
        string Id { get; }

        /// <summary>Shown to the user ("DualSense (Bluetooth)", "Xbox kumanda (XInput 1)").</summary>
        string Name { get; }

        /// <summary>"dualsense", "dualshock4", "xinput" or "virtual".</summary>
        string Kind { get; }

        bool IsConnected { get; }

        /// <summary>Copies the latest report into <paramref name="into"/> (Bluepad32 units). False when the pad is gone.</summary>
        bool Read(GamepadState into);

        /// <summary>Lightbar / player LEDs / rumble the ESP32 asked for (best effort on real hardware).</summary>
        void SetFeedback(GamepadFeedback feedback, double simulationTime);
    }

    /// <summary>The on-screen pad: whatever <see cref="VirtualGamepad"/> controls (mouse / keyboard) write.</summary>
    public sealed class VirtualGamepadSource : IGamepadSource
    {
        public static VirtualGamepadSource Instance { get; } = new VirtualGamepadSource();

        /// <summary>Current state; written by the virtual pad control on the UI thread.</summary>
        public GamepadState State { get; } = new GamepadState();

        /// <summary>Last feedback from the paired ESP32 (the control draws the lightbar from it).</summary>
        public GamepadFeedback Feedback { get; } = new GamepadFeedback();

        public string Id => "virtual";
        public string Name => "Sanal kumanda (ekran / klavye)";
        public string Kind => "virtual";
        public bool IsConnected => true;

        public event Action? FeedbackChanged;

        public bool Read(GamepadState into)
        {
            into.CopyFrom(State);
            return true;
        }

        public void SetFeedback(GamepadFeedback f, double simulationTime)
        {
            Feedback.Red = f.Red;
            Feedback.Green = f.Green;
            Feedback.Blue = f.Blue;
            Feedback.ColorSet = f.ColorSet;
            Feedback.PlayerLeds = f.PlayerLeds;
            Feedback.WeakMotor = f.Rumbling(simulationTime) ? f.WeakMotor : (byte)0;
            Feedback.StrongMotor = f.Rumbling(simulationTime) ? f.StrongMotor : (byte)0;
            FeedbackChanged?.Invoke();
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Finds controllers connected to this PC without extra packages: XInput (xinput1_4.dll)
    /// for Xbox-style pads and raw HID (hid.dll / setupapi.dll) for Sony DualSense /
    /// DualShock 4 over USB or Bluetooth. Rescans at most every two seconds.
    /// </summary>
    public static class GamepadInputs
    {
        private static readonly List<IGamepadSource> Sources = new List<IGamepadSource>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static double _lastScan = double.NegativeInfinity;

        public static string? LastError { get; private set; }

        /// <summary>Real controllers found on the PC (not including the virtual pad).</summary>
        public static IReadOnlyList<IGamepadSource> Connected
        {
            get
            {
                Rescan(false);
                return Sources.Where(s => s.IsConnected).ToList();
            }
        }

        public static void Rescan(bool force)
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (!force && now - _lastScan < 2.0) return;
            _lastScan = now;
            try
            {
                foreach (var dead in Sources.Where(s => !s.IsConnected).ToList())
                {
                    dead.Dispose();
                    Sources.Remove(dead);
                }

                foreach (var path in SonyHidGamepad.Enumerate())
                {
                    if (Sources.Any(s => s.Id == "sony:" + path.Path)) continue;
                    var pad = SonyHidGamepad.Open(path.Path, path.ProductId);
                    if (pad != null) Sources.Add(pad);
                }

                for (int i = 0; i < 4; i++)
                {
                    if (Sources.Any(s => s.Id == "xinput:" + i)) continue;
                    if (XInputGamepad.IsPresent(i)) Sources.Add(new XInputGamepad(i));
                }

                LastError = null;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is System.ComponentModel.Win32Exception || ex is System.IO.IOException)
            {
                LastError = ex.Message;
            }
        }

        /// <summary>
        /// The source for a controller object's "source" parameter: "sanal" = virtual pad,
        /// "dualsense" = first Sony pad, "xinput" = first XInput pad, "otomatik" = Sony, then XInput, then virtual.
        /// </summary>
        public static IGamepadSource Resolve(string? source)
        {
            string s = (source ?? "otomatik").Trim().ToLowerInvariant();
            if (s == "sanal" || s == "virtual") return VirtualGamepadSource.Instance;
            var real = Connected;
            IGamepadSource? pick = s switch
            {
                "dualsense" or "sony" or "dualshock4" => real.FirstOrDefault(r => r.Kind == "dualsense" || r.Kind == "dualshock4"),
                "xinput" or "xbox" => real.FirstOrDefault(r => r.Kind == "xinput"),
                _ => real.FirstOrDefault(r => r.Kind == "dualsense" || r.Kind == "dualshock4") ?? real.FirstOrDefault()
            };
            return pick ?? VirtualGamepadSource.Instance;
        }

        /// <summary>Stick deadzone + rescale (-1…1).</summary>
        internal static double Dead(double v, double zone)
        {
            double a = Math.Abs(v);
            if (a <= zone) return 0;
            return Math.Sign(v) * Math.Min(1.0, (a - zone) / (1.0 - zone));
        }
    }
}
