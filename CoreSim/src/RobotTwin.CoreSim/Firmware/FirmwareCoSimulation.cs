using System;
using System.Globalization;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>
    /// Couples an emulated ATmega328P to the analog circuit in lockstep. Before each
    /// slice the logic levels and ADC voltages the circuit presents are handed to
    /// the CPU and its supply voltage is updated (brown-out holds it in reset);
    /// the CPU runs for the slice; its pin state (LOW, HIGH, pull-up, timer PWM)
    /// then drives the board's pin devices for the next circuit solve.
    /// One clock: the CPU runs to the cycle that belongs to the slice's end time (an
    /// instruction that overran the last slice is taken off this one, so the program's
    /// time never drifts from the circuit's), and when the <see cref="Debugger"/> stops
    /// it, <see cref="Advance"/> says how far it got so the circuit stops there too.
    /// </summary>
    public sealed class FirmwareCoSimulation
    {
        public const double BrownOutVoltage = 2.7;

        private readonly DigitalPin?[] _pins = new DigitalPin?[Atmega328P.PinCount];
        private readonly double[] _appliedDuty = new double[Atmega328P.PinCount];
        private readonly Func<double> _supply;

        public FirmwareCoSimulation(Atmega328P mcu, CompiledCircuit circuit, string boardRefDes, Func<double> supplyVoltage)
        {
            Mcu = mcu;
            BoardRefDes = boardRefDes;
            _supply = supplyVoltage;
            for (int i = 0; i < _pins.Length; i++)
            {
                _appliedDuty[i] = double.NaN;
                string name = i < 14 ? "D" + i.ToString(CultureInfo.InvariantCulture) : "A" + (i - 14).ToString(CultureInfo.InvariantCulture);
                _pins[i] = circuit.Device<DigitalPin>(boardRefDes + "." + name);
            }
        }

        public Atmega328P Mcu { get; }
        public string BoardRefDes { get; }

        private double _epochTime = double.NaN;
        private ulong _epochCycles;

        /// <summary>[debug] Breakpoints and stepping (<see cref="AttachDebugger"/>); null until a debugger is attached.</summary>
        public Debug.McuDebugger? Debugger { get; private set; }

        /// <summary>[debug] Watches the program with its debug information (null: breakpoints cannot be placed by line).</summary>
        public Debug.McuDebugger AttachDebugger(Debug.FirmwareDebugInfo? info) => Debugger ??= new Debug.McuDebugger(Mcu, info);

        /// <summary>[debug] Stopped by the debugger: <see cref="Advance"/> runs nothing until it is resumed.</summary>
        public bool Halted => Mcu.Halted;

        /// <summary>Simulated time the program has reached (its cycles on the circuit's clock).</summary>
        public double ProgramTime => double.IsNaN(_epochTime) ? 0 : _epochTime + ((long)Mcu.Cycles - (long)_epochCycles) / Mcu.ClockHz;

        /// <summary>The MCU's I²C bus (SDA = A4, SCL = A5); add device models to <see cref="I2cBus.Devices"/>.</summary>
        public I2cBus I2c => Mcu.I2c;

        public bool InBrownOut { get; private set; }

        /// <summary>Text the firmware printed on Serial during the last slice.</summary>
        public event Action<string>? SerialOutput;

        /// <summary>
        /// Runs the firmware up to <paramref name="now"/> + <paramref name="dt"/> and returns how far the circuit should
        /// go: <paramref name="dt"/>, or less when the debugger stopped the CPU inside the slice (then exactly where it stopped).
        /// </summary>
        public double Advance(double dt, double now)
        {
            if (double.IsNaN(_epochTime))
            {
                _epochTime = now;
                _epochCycles = Mcu.Cycles;
            }

            if (Mcu.Halted) return 0;
            double vcc = _supply();
            bool brownOut = double.IsNaN(vcc) || vcc < BrownOutVoltage;
            if (brownOut && !InBrownOut)
            {
                Mcu.HoldReset = true;
            }
            else if (!brownOut && InBrownOut)
            {
                Mcu.Reset();
                Mcu.HoldReset = false;
            }

            InBrownOut = brownOut;
            if (!double.IsNaN(vcc)) Mcu.Vcc = vcc;

            for (int i = 0; i < _pins.Length; i++)
            {
                var pin = _pins[i];
                if (pin == null) continue;
                Mcu.SetPinInput(i, pin.DigitalValue);
                if (i >= 14) Mcu.SetAnalogInput(i - 14, pin.Voltage);
            }

            FeedUartRx(now);

            ulong startCycle = Mcu.Cycles;
            Mcu.PortLog.Clear();
            Mcu.RecordPortWrites = true;
            long target = (long)_epochCycles + (long)Math.Round((now + dt - _epochTime) * Mcu.ClockHz);
            if (target > (long)Mcu.Cycles) Mcu.RunCycles(target - (long)Mcu.Cycles);
            Mcu.RecordPortWrites = false;

            for (int i = 0; i < _pins.Length; i++)
            {
                var pin = _pins[i];
                if (pin == null) continue;
                if (InBrownOut)
                {
                    StopPwm(pin, i);
                    pin.Mode = PinMode.Input;
                    continue;
                }

                var state = Mcu.GetPin(i);
                if (state.IsPwm && state.PwmDuty > 0.0 && state.PwmDuty < 1.0)
                {
                    if (double.IsNaN(_appliedDuty[i]) || Math.Abs(_appliedDuty[i] - state.PwmDuty) > 1e-9)
                    {
                        pin.Mode = PinMode.OutputLow;
                        pin.SetPwm(state.PwmFrequency, state.PwmDuty, now);
                        _appliedDuty[i] = state.PwmDuty;
                    }

                    continue;
                }

                StopPwm(pin, i);
                if (i == 1 && Mcu.UartTxEnabled)
                {
                    ScheduleUartTx(pin, startCycle, now, dt);   // [uart] the frames' bits on D1
                    continue;
                }

                if (!state.IsPwm && ScheduleEdges(pin, i, startCycle, now, state)) continue;
                if (state.IsPwm) pin.Mode = state.PwmDuty >= 1.0 ? PinMode.OutputHigh : PinMode.OutputLow;
                else if (state.Driving) pin.Mode = state.Level ? PinMode.OutputHigh : PinMode.OutputLow;
                else pin.Mode = state.Pullup ? PinMode.InputPullup : PinMode.Input;
            }

            string text = Mcu.ReadSerial();
            if (text.Length > 0) SerialOutput?.Invoke(text);
            return Mcu.Halted ? Math.Max(0, Math.Min(dt, ProgramTime - now)) : dt;
        }

        /// <summary>
        /// [uart-rx] While the USART receiver is on, D0's level changes in the circuit, timed inside each step, go to it:
        /// whatever drives the wire (another board's TX, a module, a fault) is received as the chip would, frame errors and
        /// all. The line is known up to the circuit's time, one slice behind the core.
        /// </summary>
        private void FeedUartRx(double now)
        {
            if (_pins.Length == 0 || _pins[0] is not { } rx) return;
            bool on = Mcu.UartRxEnabled;
            if (!on)
            {
                if (rx.RecordEdges)
                {
                    rx.RecordEdges = false;
                    rx.TakeEdges();
                }

                return;
            }

            bool first = !rx.RecordEdges;
            rx.RecordEdges = true;
            ulong Cycle(double t) => (ulong)Math.Max(0, (long)_epochCycles + (long)Math.Round((t - _epochTime) * Mcu.ClockHz));
            var edges = rx.TakeEdges();
            var timed = new List<(ulong, bool)>(edges.Count);
            foreach (var (t, level) in edges) timed.Add((Cycle(t), level));
            Mcu.FeedUartRxLine(timed, Cycle(now), first ? rx.DigitalValue : (bool?)null);
        }

        /// <summary>
        /// Replays this slice's GPIO writes for one pin as timed mode changes. Returns
        /// false when the pin did not change inside the slice (nothing scheduled).
        /// </summary>
        private bool ScheduleEdges(DigitalPin pin, int index, ulong startCycle, double now, PinOutput final)
        {
            var log = Mcu.PortLog;
            if (log.Count == 0) return false;
            bool twiPin = Mcu.TwiEnabled && (index == 18 || index == 19);
            if (twiPin) return false;
            bool any = false;
            PinMode last = pin.Mode;
            foreach (var snap in log)
            {
                var (output, high) = snap.Pin(index);
                var mode = output ? (high ? PinMode.OutputHigh : PinMode.OutputLow) : (high ? PinMode.InputPullup : PinMode.Input);
                if (mode == last) continue;
                double t = Math.Max(now, _epochTime + ((long)snap.Cycle - (long)_epochCycles) / Mcu.ClockHz);
                pin.Schedule(t, mode);
                last = mode;
                any = true;
            }

            return any;
        }

        /// <summary>
        /// [uart] The TX pin through this slice: every bit edge of the frames the USART is sending, at its time, so the line
        /// carries the real waveform (a scope decodes it, another device's RX reads it). Frames finished before the slice go.
        /// </summary>
        private void ScheduleUartTx(DigitalPin pin, ulong startCycle, double now, double dt)
        {
            var frames = Mcu.TxFrames;
            double bit = Mcu.UartBitCycles;
            int frameBits = Mcu.UartFrameBits;   // start, data, parity, stop bits as UCSR0B/C set them
            frames.RemoveAll(f => f.Cycle + frameBits * bit < startCycle);
            PinMode last = pin.Mode is PinMode.OutputHigh or PinMode.OutputLow ? pin.Mode : PinMode.OutputHigh;
            if (pin.Mode != PinMode.OutputHigh && pin.Mode != PinMode.OutputLow) pin.Mode = PinMode.OutputHigh;   // the transmitter idles high
            foreach (var (cycle, value) in frames)
            {
                for (int k = 0; k < frameBits; k++)
                {
                    bool high = Mcu.UartTxBit(value, k);
                    var mode = high ? PinMode.OutputHigh : PinMode.OutputLow;
                    if (mode == last) continue;
                    double t = _epochTime + ((double)cycle + k * bit - _epochCycles) / Mcu.ClockHz;
                    if (t >= now + dt) break;   // the next slice's
                    if (t >= now) pin.Schedule(t, mode);
                    else pin.Mode = mode;   // already passed: the level it is at
                    last = mode;
                }
            }
        }

        private void StopPwm(DigitalPin pin, int index)
        {
            if (double.IsNaN(_appliedDuty[index])) return;
            pin.StopPwm();
            _appliedDuty[index] = double.NaN;
        }
    }
}
