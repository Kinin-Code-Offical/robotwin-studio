using System;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    /// <summary>
    /// [neopixel] A chain of WS2812B addressable LEDs, as the protocol works: a frame is a stream of 24-bit GRB words on DIN
    /// (most significant bit first); each LED keeps the first word that reaches it and passes everything after it on through
    /// DOUT, so the first word lands on the first LED, the second on the second, and so on; a reset (the line low for 50 µs
    /// or more) ends the frame and every LED shows what it latched. LEDs the frame did not reach keep their colour. Words past
    /// the chain's end come out of the last LED's DOUT (<see cref="Show"/> returns them, for a chained strip). Each LED draws
    /// about 1 mA on its own and up to 20 mA per colour at full brightness (<see cref="SupplyCurrent"/>).
    /// </summary>
    public sealed class Ws2812Strip
    {
        public const double IdleCurrentPerLed = 1e-3, CurrentPerChannel = 20e-3;

        public Ws2812Strip(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            Colors = new (byte R, byte G, byte B)[count];
        }

        /// <summary>Each LED's colour now, the one nearest DIN first.</summary>
        public (byte R, byte G, byte B)[] Colors { get; }

        public int Count => Colors.Length;

        /// <summary>Frames latched so far.</summary>
        public int Frames { get; private set; }

        /// <summary>Raised after each latched frame.</summary>
        public event Action<Ws2812Strip>? Updated;

        /// <summary>
        /// One frame as it arrives on DIN (GRB, three bytes an LED), latched by the reset that follows it: the LEDs it reaches
        /// take their colours; what runs past the last LED comes back (it leaves through DOUT, toward a chained strip).
        /// A trailing partial word is not latched by any LED, as on the chip.
        /// </summary>
        public byte[] Show(byte[] grb)
        {
            if (grb == null) throw new ArgumentNullException(nameof(grb));
            int words = grb.Length / 3;
            int taken = Math.Min(words, Count);
            for (int i = 0; i < taken; i++) Colors[i] = (grb[3 * i + 1], grb[3 * i], grb[3 * i + 2]);
            Frames++;
            Updated?.Invoke(this);
            int passed = grb.Length - taken * 3;
            var rest = new byte[Math.Max(0, passed)];
            if (passed > 0) Array.Copy(grb, taken * 3, rest, 0, passed);
            return rest;
        }

        /// <summary>The current the strip draws from its supply with these colours, amperes.</summary>
        public double SupplyCurrent
        {
            get
            {
                double sum = 0;
                foreach (var (r, g, b) in Colors) sum += IdleCurrentPerLed + CurrentPerChannel * (r + g + b) / 255.0;
                return sum;
            }
        }
    }
}
