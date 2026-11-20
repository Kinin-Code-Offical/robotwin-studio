using System;
using System.Runtime.InteropServices;
using RobotTwin.CoreSim.Sandbox.Radio;

namespace RobotWin.Studio.Input
{
    /// <summary>Xbox-style controller through XInput (xinput1_4.dll, part of Windows 8+): sticks, triggers, buttons, rumble.</summary>
    public sealed class XInputGamepad : IGamepadSource
    {
        private const int ErrorSuccess = 0;

        private const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008;
        private const ushort Start = 0x0010, Back = 0x0020, LeftThumb = 0x0040, RightThumb = 0x0080;
        private const ushort LeftShoulder = 0x0100, RightShoulder = 0x0200, Guide = 0x0400;
        private const ushort A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepadData
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepadData Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputVibration
        {
            public ushort LeftMotorSpeed;
            public ushort RightMotorSpeed;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState(int userIndex, out XInputState state);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        private static extern int XInputSetState(int userIndex, ref XInputVibration vibration);

        private bool _connected = true;
        private ushort _lastLeft = ushort.MaxValue, _lastRight = ushort.MaxValue;

        public XInputGamepad(int index) => Index = index;

        public int Index { get; }
        public string Id => "xinput:" + Index;
        public string Name => $"Xbox kumanda (XInput {Index + 1})";
        public string Kind => "xinput";
        public bool IsConnected => _connected;

        public static bool IsPresent(int index)
        {
            try
            {
                return XInputGetState(index, out _) == ErrorSuccess;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        public bool Read(GamepadState into)
        {
            if (XInputGetState(Index, out var s) != ErrorSuccess)
            {
                _connected = false;
                return false;
            }

            var g = s.Gamepad;
            into.AxisX = GamepadState.Axis(GamepadInputs.Dead(g.ThumbLX / 32767.0, 0.24));
            into.AxisY = GamepadState.Axis(-GamepadInputs.Dead(g.ThumbLY / 32767.0, 0.24));   // XInput up = +, Bluepad32 up = −
            into.AxisRX = GamepadState.Axis(GamepadInputs.Dead(g.ThumbRX / 32767.0, 0.27));
            into.AxisRY = GamepadState.Axis(-GamepadInputs.Dead(g.ThumbRY / 32767.0, 0.27));
            into.Brake = GamepadState.Trigger(g.LeftTrigger / 255.0);
            into.Throttle = GamepadState.Trigger(g.RightTrigger / 255.0);
            ushort b = 0;
            if ((g.Buttons & A) != 0) b |= GamepadState.ButtonA;
            if ((g.Buttons & B) != 0) b |= GamepadState.ButtonB;
            if ((g.Buttons & X) != 0) b |= GamepadState.ButtonX;
            if ((g.Buttons & Y) != 0) b |= GamepadState.ButtonY;
            if ((g.Buttons & LeftShoulder) != 0) b |= GamepadState.ButtonL1;
            if ((g.Buttons & RightShoulder) != 0) b |= GamepadState.ButtonR1;
            if (g.LeftTrigger > 30) b |= GamepadState.ButtonL2;
            if (g.RightTrigger > 30) b |= GamepadState.ButtonR2;
            if ((g.Buttons & LeftThumb) != 0) b |= GamepadState.ButtonThumbL;
            if ((g.Buttons & RightThumb) != 0) b |= GamepadState.ButtonThumbR;
            into.Buttons = b;
            byte d = 0;
            if ((g.Buttons & DpadUp) != 0) d |= GamepadState.DpadUp;
            if ((g.Buttons & DpadDown) != 0) d |= GamepadState.DpadDown;
            if ((g.Buttons & DpadLeft) != 0) d |= GamepadState.DpadLeft;
            if ((g.Buttons & DpadRight) != 0) d |= GamepadState.DpadRight;
            into.Dpad = d;
            byte misc = 0;
            if ((g.Buttons & Guide) != 0) misc |= GamepadState.MiscSystem;
            if ((g.Buttons & Back) != 0) misc |= GamepadState.MiscSelect;
            if ((g.Buttons & Start) != 0) misc |= GamepadState.MiscStart;
            into.MiscButtons = misc;
            into.Battery = 0;   // XInputGetBatteryInformation is not queried
            return true;
        }

        public void SetFeedback(GamepadFeedback f, double simulationTime)
        {
            bool on = f.Rumbling(simulationTime);
            ushort left = on ? (ushort)(f.StrongMotor * 257) : (ushort)0, right = on ? (ushort)(f.WeakMotor * 257) : (ushort)0;
            if (left == _lastLeft && right == _lastRight) return;
            _lastLeft = left;
            _lastRight = right;
            var v = new XInputVibration { LeftMotorSpeed = left, RightMotorSpeed = right };
            try
            {
                XInputSetState(Index, ref v);
            }
            catch (DllNotFoundException)
            {
            }
        }

        public void Dispose()
        {
            if (_lastLeft is > 0 and < ushort.MaxValue || _lastRight is > 0 and < ushort.MaxValue)
            {
                var v = new XInputVibration();
                try { XInputSetState(Index, ref v); } catch (DllNotFoundException) { }
            }
        }
    }
}
