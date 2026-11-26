using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    /// <summary>
    /// [neopixel-avr] A WS2812B's input read from the edges a microcontroller's pin makes (an Uno's Adafruit_NeoPixel
    /// bit-bangs them cycle by cycle): each high pulse is a bit, MSB first — shorter than 0.5 µs a 0 (T0H, nominally
    /// 0.4 µs), longer a 1 (T1H, 0.8 µs); a low of more than 50 µs latches the frame (the reset code). A high pulse under
    /// 0.1 µs or over 5 µs is no bit a WS2812B takes: the frame so far is dropped, as the LEDs lose step.
    /// </summary>
    public sealed class Ws2812Decoder
    {
        public const double BitThreshold = 0.5e-6, ResetLow = 50e-6, ShortestHigh = 0.1e-6, LongestHigh = 5e-6;

        private readonly List<byte> _bytes = new List<byte>();
        private int _bits, _current;
        private bool _level;
        private double _rose = double.NaN, _fell = double.NaN;

        /// <summary>A latched frame: its bytes (G, R, B per LED).</summary>
        public event Action<byte[]>? Frame;

        /// <summary>Frames latched, and the high pulses no WS2812B would take.</summary>
        public int Frames { get; private set; }

        public int BadPulses { get; private set; }

        /// <summary>The pin's new level at <paramref name="time"/> (s).</summary>
        public void Edge(bool level, double time)
        {
            if (level == _level) return;
            _level = level;
            if (level)
            {
                if (!double.IsNaN(_fell) && time - _fell > ResetLow) Latch();
                _rose = time;
                return;
            }

            _fell = time;
            if (double.IsNaN(_rose)) return;
            double width = time - _rose;
            if (width < ShortestHigh || width > LongestHigh)
            {
                BadPulses++;
                _bytes.Clear();
                _bits = 0;
                _current = 0;
                return;
            }

            _current = (_current << 1) | (width >= BitThreshold ? 1 : 0);
            if (++_bits < 8) return;
            _bytes.Add((byte)_current);
            _bits = 0;
            _current = 0;
        }

        /// <summary>Time going on without an edge: a low held past the reset code latches what came.</summary>
        public void Poll(double time)
        {
            if (!_level && !double.IsNaN(_fell) && time - _fell > ResetLow) Latch();
        }

        private void Latch()
        {
            _fell = double.NaN;
            if (_bytes.Count == 0)
            {
                _bits = 0;
                _current = 0;
                return;
            }

            var frame = _bytes.ToArray();
            _bytes.Clear();
            _bits = 0;
            _current = 0;
            Frames++;
            Frame?.Invoke(frame);
        }
    }
}
