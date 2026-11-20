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

        /// <summary>The MCU's I²C bus (SDA = A4, SCL = A5); add device models to <see cref="I2cBus.Devices"/>.</summary>
        public I2cBus I2c => Mcu.I2c;

        public bool InBrownOut { get; private set; }

        /// <summary>Text the firmware printed on Serial during the last slice.</summary>
        public event Action<string>? SerialOutput;

        /// <summary>Runs the firmware for <paramref name="dt"/> seconds; the caller then steps the circuit by the same amount.</summary>
        public void Advance(double dt, double now)
        {
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

            ulong startCycle = Mcu.Cycles;
            Mcu.PortLog.Clear();
            Mcu.RecordPortWrites = true;
            Mcu.Run(dt);
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
                if (!state.IsPwm && ScheduleEdges(pin, i, startCycle, now, state)) continue;
                if (state.IsPwm) pin.Mode = state.PwmDuty >= 1.0 ? PinMode.OutputHigh : PinMode.OutputLow;
                else if (state.Driving) pin.Mode = state.Level ? PinMode.OutputHigh : PinMode.OutputLow;
                else pin.Mode = state.Pullup ? PinMode.InputPullup : PinMode.Input;
            }

            string text = Mcu.ReadSerial();
            if (text.Length > 0) SerialOutput?.Invoke(text);
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
                double t = now + (snap.Cycle - startCycle) / Mcu.ClockHz;
                pin.Schedule(t, mode);
                last = mode;
                any = true;
            }

            return any;
        }

        private void StopPwm(DigitalPin pin, int index)
        {
            if (double.IsNaN(_appliedDuty[index])) return;
            pin.StopPwm();
            _appliedDuty[index] = double.NaN;
        }
    }
}
