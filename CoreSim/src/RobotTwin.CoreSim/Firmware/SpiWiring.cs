using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Devices;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>
    /// Finds the SPI devices really wired to an MCU (shared by the app's world session and the
    /// headless runner): each device line (SCK, MOSI, MISO, CS) is followed through the board and
    /// the cables to the MCU GPIO it reaches. A device whose clock or data lines reach no GPIO is not
    /// on the bus at all; one whose CS is not wired is never selected.
    /// </summary>
    public static class SpiWiring
    {
        /// <summary>SPI line pin names of a part: (SCK, MOSI, MISO, CS), or null when it is not an SPI device.</summary>
        public static (string Sck, string Mosi, string Miso, string Cs)? LinesOf(PartDefinition def) => def.Id switch
        {
            "mcp3008" => ("CLK", "DIN", "DOUT", "CS"),
            "bmp280" => ("SCL", "SDA", "SDO", "CSB"),
            _ => null
        };

        /// <summary>
        /// The world inputs a device model needs: pin voltages from the solved circuit, ambient
        /// temperature and the height of a part above sea level.
        /// </summary>
        public sealed class Context
        {
            public Func<string, string, double> PinVolts { get; set; } = (_, _) => 0;
            public Func<double> AmbientC { get; set; } = () => 25;
            public Func<string, double> AltitudeOf { get; set; } = _ => 0;
            public int Seed { get; set; } = 1;
        }

        /// <summary>Protocol model of an SPI part (also used for parts that speak I²C and SPI).</summary>
        public static ISpiDevice? Create(string fullRef, PartDefinition def, IReadOnlyDictionary<string, string> parameters, Context ctx)
        {
            switch (def.Id)
            {
                case "mcp3008":
                    return new Mcp3008(ch => ctx.PinVolts(fullRef, "CH" + ch) - ctx.PinVolts(fullRef, "AGND"),
                        () => ctx.PinVolts(fullRef, "VREF") - ctx.PinVolts(fullRef, "AGND"));
                case "bmp280":
                    return Barometer(fullRef, parameters, ctx);
                default:
                    return null;
            }
        }

        /// <summary>BMP280 model reading the world at the part's height (I²C address from SDO / the parameter).</summary>
        public static Bmp280 Barometer(string fullRef, IReadOnlyDictionary<string, string> parameters, Context ctx)
        {
            byte address = parameters.TryGetValue("address", out var a) && a.Trim().EndsWith("77", StringComparison.Ordinal) ? (byte)0x77 : (byte)0x76;
            return new Bmp280(address, () => Bmp280.PressureAt(ctx.AltitudeOf(fullRef)), ctx.AmbientC, ctx.Seed ^ Automation.J.StableHash(fullRef));
        }

        /// <summary>
        /// Builds the bus slaves for <paramref name="boardRef"/>'s MCU. <paramref name="gpioNode"/> gives
        /// the circuit node of a GPIO (−1 if the board has no such pin); <paramref name="reach"/> the nodes
        /// joined to a node by wires; <paramref name="gpioName"/> the board's name of a GPIO for the log (IOn when not given).
        /// </summary>
        public static List<SpiSlave> Attach(
            IEnumerable<(string FullRef, PartDefinition Def, IReadOnlyDictionary<string, string> Parameters)> parts,
            Func<string, string, int> pinNode, Func<int, int> gpioNode, Func<int, HashSet<int>> reach,
            Func<string, bool> powered, Context ctx, Action<string>? log = null, Func<int, string?>? gpioName = null)
        {
            string Name(int g) => gpioName?.Invoke(g) ?? "IO" + g;
            // Node → GPIO over everything the MCU's pins reach.
            var gpioOf = new Dictionary<int, int>();
            for (int g = 0; g < 48; g++)
            {
                int n = gpioNode(g);
                if (n <= 0) continue;
                foreach (int r in reach(n))
                    if (!gpioOf.ContainsKey(r)) gpioOf[r] = g;
            }

            int Gpio(string fullRef, string pin)
            {
                int n = pinNode(fullRef, pin);
                return n > 0 && gpioOf.TryGetValue(n, out int g) ? g : -1;
            }

            var list = new List<SpiSlave>();
            foreach (var (fullRef, def, parameters) in parts)
            {
                if (def.Symbol == SymbolKind.Board || LinesOf(def) is not { } lines) continue;
                int sck = Gpio(fullRef, lines.Sck), mosi = Gpio(fullRef, lines.Mosi), miso = Gpio(fullRef, lines.Miso), cs = Gpio(fullRef, lines.Cs);
                if (sck < 0 || mosi < 0 || cs < 0) continue;   // not an SPI connection to this MCU (e.g. a BMP280 on I²C)
                var device = Create(fullRef, def, parameters, ctx);
                if (device == null) continue;
                string r = fullRef;
                var slave = new SpiSlave(r, device, sck, miso, mosi, cs, () => powered(r));
                if (def.Id == "mcp3008")
                {
                    // Datasheet: 3.6 MHz at VDD = 5 V, 1.35 MHz at 2.7 V (linear in between).
                    slave.MaxClockHz = () =>
                    {
                        double vdd = ctx.PinVolts(r, "VDD") - ctx.PinVolts(r, "DGND");
                        return 1.35e6 + (3.6e6 - 1.35e6) * Math.Max(0, Math.Min(1, (vdd - 2.7) / 2.3));
                    };
                }

                list.Add(slave);
                log?.Invoke($"{r}: SPI veri yoluna bağlandı (SCK {Name(sck)}, MOSI {Name(mosi)}, MISO {(miso >= 0 ? Name(miso) : "bağlı değil")}, CS {Name(cs)})");
            }

            return list;
        }
    }
}
