using System;
using System.Globalization;
using RobotTwin.CoreSim.Analog;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    /// <summary>ESP32 GPIO facts shared by the board parts and the co-simulation.</summary>
    public static class Esp32Gpio
    {
        public const int Count = 40;

        /// <summary>GPIOs that exist on the ESP32 (D0WD): 0-19, 21-23, 25-27, 32-39.</summary>
        public static bool IsValid(int gpio) =>
            gpio >= 0 && gpio < Count && gpio != 20 && gpio != 24 && !(gpio >= 28 && gpio <= 31);

        /// <summary>GPIO34-39 are input only (no output driver, no internal pull resistors).</summary>
        public static bool IsInputOnly(int gpio) => gpio >= 34 && gpio <= 39;

        public static bool CanOutput(int gpio) => IsValid(gpio) && gpio < 34;

        /// <summary>GPIO6-11 are wired to the module's SPI flash.</summary>
        public static bool IsFlashPin(int gpio) => gpio >= 6 && gpio <= 11;

        /// <summary>1 for ADC1 (GPIO32-39), 2 for ADC2, 0 when the pin has no ADC channel.</summary>
        public static int AdcUnit(int gpio)
        {
            if (gpio >= 32 && gpio <= 39) return 1;
            switch (gpio)
            {
                case 0: case 2: case 4: case 12: case 13: case 14: case 15: case 25: case 26: case 27: return 2;
                default: return 0;
            }
        }

        public static bool HasDac(int gpio) => gpio == 25 || gpio == 26;

        public static bool HasTouch(int gpio) =>
            gpio == 4 || gpio == 0 || gpio == 2 || gpio == 15 || gpio == 13 || gpio == 12 || gpio == 14 || gpio == 27 || gpio == 33 || gpio == 32;

        /// <summary>Board pin name of a GPIO ("IO2", "VP" for 36, "VN" for 39, "TX"/"RX" for 1/3, "SD2".. for flash pins).</summary>
        public static string? PinName(int gpio)
        {
            if (!IsValid(gpio)) return null;
            switch (gpio)
            {
                case 36: return "VP";
                case 39: return "VN";
                case 1: return "TX";
                case 3: return "RX";
                case 6: return "CLK";
                case 7: return "SD0";
                case 8: return "SD1";
                case 9: return "SD2";
                case 10: return "SD3";
                case 11: return "CMD";
                default: return "IO" + gpio.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Inverse of <see cref="PinName"/>; also accepts "GPIO5", "D5" and plain numbers. -1 if unknown.</summary>
        public static int FromPinName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            string n = name.Trim().ToUpperInvariant();
            for (int g = 0; g < Count; g++)
            {
                if (PinName(g) == n) return g;
            }

            foreach (var prefix in new[] { "GPIO", "IO", "D" })
            {
                if (n.StartsWith(prefix, StringComparison.Ordinal) &&
                    int.TryParse(n.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) && IsValid(g))
                {
                    return g;
                }
            }

            return int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out int plain) && IsValid(plain) ? plain : -1;
        }
    }

    /// <summary>
    /// The ESP32 chip as a load on its 3V3 rail (≈80 mA running, more with Wi-Fi on,
    /// µA in deep sleep) plus the sense point for the supply and the EN (CHIP_PU) pin.
    /// The co-simulation sets <see cref="Current"/> from the chip state.
    /// </summary>
    public sealed class Esp32ChipLoad : AnalogDevice
    {
        private double _current;

        public Esp32ChipLoad(string id, int vdd, int ground, int enable, double current) : base(id)
        {
            Vdd = vdd;
            Ground = ground;
            Enable = enable;
            _current = Math.Max(0.0, current);
        }

        public int Vdd { get; }
        public int Ground { get; }
        public int Enable { get; }

        /// <summary>Supply voltage at which <see cref="Current"/> is drawn (the load is resistive around it).</summary>
        public double NominalVoltage { get; set; } = 3.3;

        /// <summary>Current drawn at <see cref="NominalVoltage"/>, in amperes.</summary>
        public double Current
        {
            get => _current;
            set
            {
                double v = Math.Max(0.0, value);
                if (Math.Abs(v - _current) < 1e-12) return;
                _current = v;
                PendingDiscontinuity = true;
            }
        }

        public double SupplyVoltage { get; private set; } = double.NaN;
        public double EnableVoltage { get; private set; } = double.NaN;
        public double SupplyCurrent { get; private set; }

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(Vdd, Ground, _current / NominalVoltage);
            ctx.AddConductance(Enable, Ground, 1e-9);
        }

        public override void Accept(AcceptContext ctx)
        {
            SupplyVoltage = ctx.V(Vdd, Ground);
            EnableVoltage = ctx.V(Enable, Ground);
            SupplyCurrent = SupplyVoltage * _current / NominalVoltage;
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "VDD", SupplyVoltage);
            sink.Write(Id, "EN", EnableVoltage);
            sink.Write(Id, "I", SupplyCurrent);
        }
    }

    /// <summary>
    /// ESP32 pad features that <see cref="Analog.Devices.DigitalPin"/> does not have: the
    /// internal pull-down (≈45 kΩ) and the 8-bit DAC output (GPIO25/26) as a Thevenin source.
    /// Sits on the same node as the pin's DigitalPin.
    /// </summary>
    public sealed class Esp32PadAux : AnalogDevice
    {
        private bool _pulldown;
        private bool _dac;
        private double _dacVoltage;

        public Esp32PadAux(string id, int pin, int ground) : base(id)
        {
            Pin = pin;
            Ground = ground;
        }

        public int Pin { get; }
        public int Ground { get; }

        public double PulldownResistance { get; set; } = 45e3;

        /// <summary>DAC output impedance (approximate; the ESP32 DAC is a weak, unbuffered source).</summary>
        public double DacOutputResistance { get; set; } = 250.0;

        public bool Pulldown
        {
            get => _pulldown;
            set
            {
                if (_pulldown == value) return;
                _pulldown = value;
                PendingDiscontinuity = true;
            }
        }

        public bool DacEnabled
        {
            get => _dac;
            set
            {
                if (_dac == value) return;
                _dac = value;
                PendingDiscontinuity = true;
            }
        }

        public double DacVoltage
        {
            get => _dacVoltage;
            set
            {
                if (Math.Abs(_dacVoltage - value) < 1e-9) return;
                _dacVoltage = value;
                if (_dac) PendingDiscontinuity = true;
            }
        }

        public override void Stamp(StampContext ctx)
        {
            if (_pulldown) ctx.AddConductance(Pin, Ground, 1.0 / PulldownResistance);
            if (_dac)
            {
                double g = 1.0 / DacOutputResistance;
                ctx.AddConductance(Pin, Ground, g);
                ctx.AddCurrent(Ground, Pin, _dacVoltage * g);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "PD", _pulldown ? 1.0 : 0.0);
            sink.Write(Id, "DAC", _dac ? _dacVoltage : -1.0);
        }
    }
}
