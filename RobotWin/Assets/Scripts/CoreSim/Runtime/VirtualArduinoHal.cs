using System;
using System.Collections.Generic;
using System.Globalization;

namespace RobotTwin.CoreSim.Runtime
{
    public readonly struct PinState
    {
        public string Pin { get; }
        public bool IsOutput { get; }
        public bool PullupEnabled { get; }

        public PinState(string pin, bool isOutput, bool pullupEnabled)
        {
            Pin = pin;
            IsOutput = isOutput;
            PullupEnabled = pullupEnabled;
        }
    }

    public class VirtualArduinoHal
    {
        private readonly VirtualRegisterFile _registers;
        private readonly Dictionary<string, PinMapping> _pinMap = new Dictionary<string, PinMapping>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _pinOverrides = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _pinInputs = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private const double DefaultPullupResistance = 20000.0;

        public VirtualArduinoHal(VirtualRegisterFile registers, IReadOnlyList<string> pins = null)
        {
            _registers = registers;
            BuildPinMap(pins);
        }

        public bool TryGetPortBit(string pin, out byte ddr, out byte port, out int bit)
        {
            if (_pinMap.TryGetValue(pin, out var mapping))
            {
                ddr = mapping.Ddr;
                port = mapping.Port;
                bit = mapping.Bit;
                return true;
            }
            ddr = 0;
            port = 0;
            bit = 0;
            return false;
        }

        public void SetPinOutput(string pin, bool high)
        {
            if (!TryGetPortBit(pin, out var ddr, out var port, out var bit)) return;
            _registers.SetBit(ddr, bit, true);
            _registers.SetBit(port, bit, high);
        }

        public void SetPinOverride(string pin, string value)
        {
            if (string.IsNullOrWhiteSpace(pin)) return;
            if (TryParseVoltage(value, out var voltage))
            {
                _pinOverrides[pin] = voltage;
                SetPinOutput(pin, voltage >= VirtualArduino.DefaultHighVoltage * 0.5f);
                return;
            }
            if (IsHigh(value))
            {
                _pinOverrides[pin] = VirtualArduino.DefaultHighVoltage;
                SetPinOutput(pin, true);
                return;
            }
            if (IsLow(value))
            {
                _pinOverrides[pin] = 0f;
                SetPinOutput(pin, false);
            }
        }

        public void SetInputVoltage(string pin, float voltage)
        {
            if (string.IsNullOrWhiteSpace(pin)) return;
            _pinInputs[pin] = voltage;
        }

        public Dictionary<string, float> GetOutputVoltages()
        {
            var voltages = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _pinMap)
            {
                string pin = kvp.Key;
                if (_pinOverrides.TryGetValue(pin, out var overrideVoltage))
                {
                    voltages[pin] = overrideVoltage;
                    continue;
                }

                var mapping = kvp.Value;
                bool isOutput = _registers.GetBit(mapping.Ddr, mapping.Bit);
                if (isOutput)
                {
                    bool isHigh = _registers.GetBit(mapping.Port, mapping.Bit);
                    voltages[pin] = isHigh ? VirtualArduino.DefaultHighVoltage : 0f;
                    continue;
                }

                if (_pinOverrides.TryGetValue(pin, out var overrideVoltage))
                {
                    voltages[pin] = overrideVoltage;
                    continue;
                }

                if (_pinInputs.TryGetValue(pin, out var inputVoltage))
                {
                    voltages[pin] = inputVoltage;
                }
            }
            return voltages;
        }

        public List<PinState> GetPinStates()
        {
            var states = new List<PinState>();
            foreach (var kvp in _pinMap)
            {
                var mapping = kvp.Value;
                bool isOutput = _registers.GetBit(mapping.Ddr, mapping.Bit);
                bool pullup = !isOutput && _registers.GetBit(mapping.Port, mapping.Bit);
                states.Add(new PinState(kvp.Key, isOutput, pullup));
            }
            return states;
        }

        public double GetPullupResistance()
        {
            return DefaultPullupResistance;
        }

        private void BuildPinMap(IReadOnlyList<string> pins)
        {
            if (pins == null || pins.Count == 0)
            {
                var defaultPins = new List<string>();
                for (int i = 0; i <= 13; i++) defaultPins.Add($"D{i}");
                for (int i = 0; i <= 5; i++) defaultPins.Add($"A{i}");
                pins = defaultPins;
            }

            foreach (var pin in pins)
            {
                if (string.IsNullOrWhiteSpace(pin)) continue;
                if (!TryMapPin(pin.Trim().ToUpperInvariant(), out var mapping)) continue;
                _pinMap[pin] = mapping;
            }
        }

        private static bool TryMapPin(string pin, out PinMapping mapping)
        {
            mapping = default;
            if (pin.StartsWith("D", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(pin.Substring(1), out var d))
            {
                if (d >= 0 && d <= 7)
                {
                    mapping = new PinMapping(VirtualRegisterFile.DDRD, VirtualRegisterFile.PORTD, d);
                    return true;
                }
                if (d >= 8 && d <= 13)
                {
                    mapping = new PinMapping(VirtualRegisterFile.DDRB, VirtualRegisterFile.PORTB, d - 8);
                    return true;
                }
                return false;
            }
            if (pin.StartsWith("A", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(pin.Substring(1), out var a))
            {
                if (a >= 0 && a <= 5)
                {
                    mapping = new PinMapping(VirtualRegisterFile.DDRC, VirtualRegisterFile.PORTC, a);
                    return true;
                }
            }
            return false;
        }

        private static bool TryParseVoltage(string raw, out float voltage)
        {
            voltage = 0f;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim().ToLowerInvariant();
            s = s.Replace("v", string.Empty).Replace(" ", string.Empty);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out voltage);
        }

        private static bool IsHigh(string raw)
        {
            return string.Equals(raw, "high", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLow(string raw)
        {
            return string.Equals(raw, "low", StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct PinMapping
        {
            public byte Ddr { get; }
            public byte Port { get; }
            public int Bit { get; }

            public PinMapping(byte ddr, byte port, int bit)
            {
                Ddr = ddr;
                Port = port;
                Bit = bit;
            }
        }
    }
}
