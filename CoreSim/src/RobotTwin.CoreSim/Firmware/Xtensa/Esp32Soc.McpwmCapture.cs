using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] [motor] The MCPWM capture unit as IDF's mcpwm_cap driver programs it: per group a capture timer counting
    /// the APB clock from when CAP_TIMER_EN is set (its phase loaded by a software sync), three channels (CAP_CHn_CFG:
    /// enable, the edges to capture, the prescaler, input inversion, a software capture) each latching the timer into
    /// CAP_CHn on its matrix input's edges (PWM0_CAP0–2 = signals 109–111, PWM1_CAP0–2 = 112–114), the edge in CAP_STATUS
    /// and CAPn_INT in the interrupt registers (source 39 / 40, level). The inputs are the pads as the board samples them;
    /// a pad the chip drives with PWM itself gives its edges at the PWM's own times (rising at k/f, falling at (k + duty)/f
    /// from when the wave started), as the GPIO matrix feeds an output back on the chip. A prescale field p (IDF writes
    /// prescale − 1) makes the captured signal toggle every (p + 1)/2 rising edges, as the fast mode does.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const int McpwmInterruptSource = 39;   // PWM0; PWM1 is 40
        private const uint McpwmCapTimerCfg = 0xE8, McpwmCapTimerPhase = 0xEC, McpwmCapChCfg = 0xF0, McpwmCapCh = 0xFC, McpwmCapStatus = 0x108;
        private const uint McpwmIntEna = 0x110, McpwmIntRaw = 0x114, McpwmIntSt = 0x118, McpwmIntClr = 0x11C;

        private sealed class CaptureChannel
        {
            public bool Level;                // the input as last seen (after the matrix's inversion)
            public uint Rises;                // for the prescaler
            public bool Prescaled;
            public double PwmFrequency, PwmDuty, PwmOrigin;   // the chip's own PWM on the pad, when it is one
            public long PwmEdge;              // the next of its edges: even rising, odd falling
        }

        private readonly CaptureChannel[,] _cap = NewCaptureChannels();
        private readonly double[] _capOrigin = new double[2];   // the time the capture timer read 0
        private readonly uint[] _mcpwmIntRaw = new uint[2];
        private bool _capActive;

        private static CaptureChannel[,] NewCaptureChannels()
        {
            var c = new CaptureChannel[2, 3];
            for (int u = 0; u < 2; u++)
                for (int ch = 0; ch < 3; ch++)
                    c[u, ch] = new CaptureChannel();
            return c;
        }

        /// <summary>The capture timer's count at <paramref name="seconds"/> (it wraps at 32 bits).</summary>
        private uint CaptureCount(int unit, double seconds) =>
            (Reg(McpwmBase[unit] + McpwmCapTimerCfg) & 1) == 0 ? 0u : unchecked((uint)(long)Math.Floor((seconds - _capOrigin[unit]) * ApbHz));

        /// <summary>A write to a capture or interrupt register (true when it was one).</summary>
        private bool WriteMcpwmCapture(int unit, uint offset, uint value)
        {
            uint b = McpwmBase[unit];
            switch (offset)
            {
                case McpwmCapTimerCfg:
                {
                    bool was = (Reg(b + McpwmCapTimerCfg) & 1) != 0, on = (value & 1) != 0;
                    double phase = Reg(b + McpwmCapTimerPhase) / ApbHz;
                    if ((on && !was) || (value & (1u << 5)) != 0) _capOrigin[unit] = Seconds - phase;   // started, or a software sync
                    _registers[b + offset] = value & ~(1u << 5);   // CAP_SYNC_SW is a command
                    return true;
                }

                case McpwmCapChCfg:
                case McpwmCapChCfg + 4:
                case McpwmCapChCfg + 8:
                {
                    int ch = (int)((offset - McpwmCapChCfg) / 4);
                    bool was = (Reg(b + offset) & 1) != 0, on = (value & 1) != 0;
                    _registers[b + offset] = value & ~(1u << 12);   // CAPn_SW is a command
                    if (on && !was)
                    {
                        var c = _cap[unit, ch];
                        c.Level = InputSignalLevel(CaptureSignal(unit, ch), out _);
                        c.Rises = 0;
                        c.Prescaled = false;
                        c.PwmFrequency = 0;
                        _capActive = true;
                    }

                    if ((value & (1u << 12)) != 0 && on) Capture(unit, ch, negative: false, Seconds);   // a software capture
                    return true;
                }

                case McpwmIntEna:
                    _registers[b + offset] = value;
                    UpdateMcpwmInterrupt(unit);
                    return true;
                case McpwmIntClr:
                    _mcpwmIntRaw[unit] &= ~value;
                    UpdateMcpwmInterrupt(unit);
                    return true;
                case McpwmIntRaw:
                case McpwmIntSt:
                    return true;   // read-only
            }

            return false;
        }

        private bool ReadMcpwmRegister(uint register, out uint value)
        {
            value = 0;
            int unit = register >= McpwmBase[0] && register < McpwmBase[0] + 0x130 ? 0 : register >= McpwmBase[1] && register < McpwmBase[1] + 0x130 ? 1 : -1;
            if (unit < 0) return false;
            uint offset = register - McpwmBase[unit];
            if (offset == McpwmIntRaw) value = _mcpwmIntRaw[unit];
            else if (offset == McpwmIntSt) value = _mcpwmIntRaw[unit] & Reg(McpwmBase[unit] + McpwmIntEna);
            else _registers.TryGetValue(register, out value);
            return true;
        }

        private static int CaptureSignal(int unit, int ch) => (unit == 0 ? 109 : 112) + ch;

        private void UpdateMcpwmInterrupt(int unit) =>
            SetPeripheralLevel(McpwmInterruptSource + unit, (_mcpwmIntRaw[unit] & Reg(McpwmBase[unit] + McpwmIntEna)) != 0);

        /// <summary>Latches the timer into CAP_CHn at <paramref name="seconds"/>, the edge into CAP_STATUS, and raises CAPn_INT.</summary>
        private void Capture(int unit, int ch, bool negative, double seconds)
        {
            uint b = McpwmBase[unit];
            _registers[b + McpwmCapCh + 4 * (uint)ch] = CaptureCount(unit, seconds);
            uint status = Reg(b + McpwmCapStatus);
            _registers[b + McpwmCapStatus] = negative ? status | (1u << ch) : status & ~(1u << ch);
            _mcpwmIntRaw[unit] |= 1u << (27 + ch);
            UpdateMcpwmInterrupt(unit);
        }

        /// <summary>An edge of a channel's input: through the prescaler, captured when CAPn_MODE selects it (bit 1 rising, bit 0 falling).</summary>
        private void CaptureEdge(int unit, int ch, bool rising, double seconds)
        {
            uint cfg = Reg(McpwmBase[unit] + McpwmCapChCfg + 4 * (uint)ch);
            if ((cfg & 1) == 0) return;
            if ((cfg & (1u << 11)) != 0) rising = !rising;   // CAPn_IN_INVERT
            uint prescale = ((cfg >> 3) & 0xFF) + 1;
            var c = _cap[unit, ch];
            bool edgeRising = rising;
            if (prescale > 1)
            {
                if (!rising) return;
                uint half = Math.Max(1, prescale / 2);
                if (c.Rises++ % half != 0) return;
                c.Prescaled = !c.Prescaled;
                edgeRising = c.Prescaled;
            }

            uint mode = (cfg >> 1) & 3;
            if ((edgeRising && (mode & 2) != 0) || (!edgeRising && (mode & 1) != 0)) Capture(unit, ch, !edgeRising, seconds);
        }

        /// <summary>After each sample of the pads: the edges the capture inputs saw (a pad's own PWM aside, timed on its own).</summary>
        private void SampleMcpwmCapture()
        {
            if (!_capActive) return;
            for (int unit = 0; unit < 2; unit++)
                for (int ch = 0; ch < 3; ch++)
                {
                    if ((Reg(McpwmBase[unit] + McpwmCapChCfg + 4 * (uint)ch) & 1) == 0) continue;
                    bool level = InputSignalLevel(CaptureSignal(unit, ch), out int pad);
                    if (pad >= 0 && PadDrive(pad).IsPwm) continue;
                    var c = _cap[unit, ch];
                    if (level == c.Level) continue;
                    c.Level = level;
                    CaptureEdge(unit, ch, level, Seconds);
                }
        }

        /// <summary>Each turn: a capture input the chip drives with PWM itself gives its edges at their own times.</summary>
        private void ServiceMcpwmCapture()
        {
            if (!_capActive) return;
            double now = Seconds;
            for (int unit = 0; unit < 2; unit++)
                for (int ch = 0; ch < 3; ch++)
                {
                    if ((Reg(McpwmBase[unit] + McpwmCapChCfg + 4 * (uint)ch) & 1) == 0) continue;
                    InputSignalLevel(CaptureSignal(unit, ch), out int pad);
                    var c = _cap[unit, ch];
                    if (pad < 0)
                    {
                        c.PwmFrequency = 0;
                        continue;
                    }

                    var drive = PadDrive(pad);
                    if (!drive.IsPwm || !drive.Driven || !drive.InputEnabled)
                    {
                        c.PwmFrequency = 0;
                        continue;
                    }

                    if (drive.PwmFrequency != c.PwmFrequency || drive.PwmDuty != c.PwmDuty)
                    {
                        // a new wave: its first rising edge now, as the output starts its period
                        c.PwmFrequency = drive.PwmFrequency;
                        c.PwmDuty = drive.PwmDuty;
                        c.PwmOrigin = now;
                        c.PwmEdge = 0;
                    }

                    bool invert = (_funcIn[CaptureSignal(unit, ch)] & (1u << 6)) != 0;
                    for (int guard = 0; guard < 1 << 16; guard++)
                    {
                        long k = c.PwmEdge;
                        double at = c.PwmOrigin + ((k / 2) + (k % 2 != 0 ? c.PwmDuty : 0.0)) / c.PwmFrequency;
                        if (at > now) break;
                        c.PwmEdge++;
                        CaptureEdge(unit, ch, (k % 2 == 0) ^ invert, at);
                    }
                }
        }
    }
}
