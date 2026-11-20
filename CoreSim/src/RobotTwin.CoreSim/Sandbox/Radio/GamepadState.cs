using System;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>Controller families (what the ESP32 reports from getModelName()).</summary>
    public enum GamepadModel
    {
        DualSense,
        DualShock4,
        Xbox,
        Generic
    }

    /// <summary>
    /// One input report of a game controller in Bluepad32 units: sticks −511…512 (Y down
    /// positive), triggers 0…1023, button bit masks identical to Bluepad32's
    /// (BUTTON_A = Cross on PlayStation pads, B = Circle, X = Square, Y = Triangle).
    /// </summary>
    public sealed class GamepadState
    {
        public const ushort ButtonA = 0x0001;
        public const ushort ButtonB = 0x0002;
        public const ushort ButtonX = 0x0004;
        public const ushort ButtonY = 0x0008;
        public const ushort ButtonL1 = 0x0010;
        public const ushort ButtonR1 = 0x0020;
        public const ushort ButtonL2 = 0x0040;
        public const ushort ButtonR2 = 0x0080;
        public const ushort ButtonThumbL = 0x0100;
        public const ushort ButtonThumbR = 0x0200;

        public const byte DpadUp = 0x01;
        public const byte DpadDown = 0x02;
        public const byte DpadRight = 0x04;
        public const byte DpadLeft = 0x08;

        /// <summary>PS / Xbox / Home button.</summary>
        public const byte MiscSystem = 0x01;

        /// <summary>Create / Share / View.</summary>
        public const byte MiscSelect = 0x02;

        /// <summary>Options / Menu.</summary>
        public const byte MiscStart = 0x04;

        /// <summary>Touchpad click (PlayStation) / Capture.</summary>
        public const byte MiscCapture = 0x08;

        public int AxisX { get; set; }
        public int AxisY { get; set; }
        public int AxisRX { get; set; }
        public int AxisRY { get; set; }

        /// <summary>L2 (0…1023).</summary>
        public int Brake { get; set; }

        /// <summary>R2 (0…1023).</summary>
        public int Throttle { get; set; }

        public ushort Buttons { get; set; }
        public byte Dpad { get; set; }
        public byte MiscButtons { get; set; }

        /// <summary>Gyroscope (raw units, Bluepad32 scale) and accelerometer (≈ 8192 per g).</summary>
        public int GyroX { get; set; }
        public int GyroY { get; set; }
        public int GyroZ { get; set; }
        public int AccelX { get; set; }
        public int AccelY { get; set; }
        public int AccelZ { get; set; } = 8192;

        /// <summary>Battery 0 (unknown) … 255 (full).</summary>
        public byte Battery { get; set; } = 220;

        /// <summary>A finger on the touchpad, position 0…1.</summary>
        public bool Touch { get; set; }
        public double TouchX { get; set; }
        public double TouchY { get; set; }

        public GamepadState Clone() => (GamepadState)MemberwiseClone();

        public void CopyFrom(GamepadState s)
        {
            AxisX = s.AxisX;
            AxisY = s.AxisY;
            AxisRX = s.AxisRX;
            AxisRY = s.AxisRY;
            Brake = s.Brake;
            Throttle = s.Throttle;
            Buttons = s.Buttons;
            Dpad = s.Dpad;
            MiscButtons = s.MiscButtons;
            GyroX = s.GyroX;
            GyroY = s.GyroY;
            GyroZ = s.GyroZ;
            AccelX = s.AccelX;
            AccelY = s.AccelY;
            AccelZ = s.AccelZ;
            Battery = s.Battery;
            Touch = s.Touch;
            TouchX = s.TouchX;
            TouchY = s.TouchY;
        }

        public void Clear()
        {
            AxisX = AxisY = AxisRX = AxisRY = Brake = Throttle = 0;
            Buttons = 0;
            Dpad = 0;
            MiscButtons = 0;
            GyroX = GyroY = GyroZ = AccelX = AccelY = 0;
            AccelZ = 8192;
            Touch = false;
        }

        /// <summary>Stick value from −1…1 (left/up = −1).</summary>
        public static int Axis(double v) => (int)Math.Round(Math.Max(-1.0, Math.Min(1.0, v)) * (v < 0 ? 511.0 : 512.0));

        /// <summary>Trigger value from 0…1.</summary>
        public static int Trigger(double v) => (int)Math.Round(Math.Max(0.0, Math.Min(1.0, v)) * 1023.0);

        public bool Pressed(ushort mask) => (Buttons & mask) != 0;

        public void Set(ushort mask, bool on) => Buttons = (ushort)(on ? Buttons | mask : Buttons & ~mask);

        public void SetDpad(byte mask, bool on) => Dpad = (byte)(on ? Dpad | mask : Dpad & ~mask);

        public void SetMisc(byte mask, bool on) => MiscButtons = (byte)(on ? MiscButtons | mask : MiscButtons & ~mask);

        public bool IsNeutral => AxisX == 0 && AxisY == 0 && AxisRX == 0 && AxisRY == 0 && Brake == 0 && Throttle == 0 && Buttons == 0 && Dpad == 0 && MiscButtons == 0;

        public override string ToString() =>
            $"L({AxisX},{AxisY}) R({AxisRX},{AxisRY}) L2={Brake} R2={Throttle} btn=0x{Buttons:X4} dpad=0x{Dpad:X2} misc=0x{MiscButtons:X2}";
    }

    /// <summary>What the host (ESP32) asked the controller to show: lightbar, player LEDs, rumble.</summary>
    public sealed class GamepadFeedback
    {
        public byte Red { get; set; }
        public byte Green { get; set; }
        public byte Blue { get; set; } = 64;
        public byte PlayerLeds { get; set; }

        /// <summary>Rumble motor strengths 0…255 and the simulation time they stop.</summary>
        public byte WeakMotor { get; set; }
        public byte StrongMotor { get; set; }
        public double RumbleUntil { get; set; }
        public bool ColorSet { get; set; }

        public bool Rumbling(double now) => now < RumbleUntil && (WeakMotor > 0 || StrongMotor > 0);
    }
}
