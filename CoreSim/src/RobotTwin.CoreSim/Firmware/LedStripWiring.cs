using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Devices;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>
    /// [neopixel] Which addressable LED strips a microcontroller drives: every WS2812B strip whose DIN is wired (directly or
    /// through wires) to one of the MCU's GPIOs, found as <see cref="SpiWiring"/> finds SPI lines. The board delivers each
    /// frame it sends on that GPIO to the strip (<see cref="Ws2812Load.Receive"/>). Strips chained DOUT → DIN are linked
    /// (<see cref="Ws2812Load.Next"/>), so the words past the first strip's end reach the next.
    /// </summary>
    public static class LedStripWiring
    {
        public const string PartId = "ws2812-strip";

        /// <param name="parts">The design's parts (full designator, definition).</param>
        /// <param name="pinNode">The circuit node of a part's pin (−1 when it has none).</param>
        /// <param name="gpioNode">The circuit node of a GPIO of the MCU (−1 if the board has no such pin).</param>
        /// <param name="reach">The nodes joined to a node by wires.</param>
        /// <param name="device">The strip's circuit device by its full designator.</param>
        public static List<(int Gpio, Ws2812Load Strip)> Attach(
            IEnumerable<(string FullRef, PartDefinition Def)> parts,
            Func<string, string, int> pinNode, Func<int, int> gpioNode, Func<int, HashSet<int>> reach,
            Func<string, Ws2812Load?> device)
        {
            var gpioOf = new Dictionary<int, int>();
            for (int g = 0; g < 48; g++)
            {
                int n = gpioNode(g);
                if (n <= 0) continue;
                foreach (int r in reach(n))
                    if (!gpioOf.ContainsKey(r)) gpioOf[r] = g;
            }

            var list = new List<(int, Ws2812Load)>();
            var strips = new List<(Ws2812Load Strip, int Din, int Dout)>();
            foreach (var (fullRef, def) in parts)
            {
                if (def.Id != PartId || device(fullRef) is not { } strip) continue;
                int din = pinNode(fullRef, "DIN");
                strips.Add((strip, din, pinNode(fullRef, "DOUT")));
                if (din > 0 && gpioOf.TryGetValue(din, out int gpio)) list.Add((gpio, strip));
            }

            // DOUT → DIN: each strip passes what runs past its end to the strip wired after it.
            foreach (var a in strips)
            {
                a.Strip.Next = null;
                if (a.Dout <= 0) continue;
                var joined = reach(a.Dout);
                foreach (var b in strips)
                    if (!ReferenceEquals(a.Strip, b.Strip) && b.Din > 0 && joined.Contains(b.Din)) a.Strip.Next = b.Strip;
            }

            return list;
        }
    }
}
