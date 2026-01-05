using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim
{
    public sealed class BoardProfileInfo
    {
        public string Id { get; }
        public string Mcu { get; }
        public int PinCount { get; }
        public double ClockHz { get; }
        public int BootloaderBytes { get; }
        public bool CoreLimited { get; }
        public IReadOnlyList<string> Pins { get; }

        public BoardProfileInfo(string id, string mcu, IEnumerable<string> pins, double clockHz, int bootloaderBytes, bool coreLimited)
        {
            Id = id ?? string.Empty;
            Mcu = mcu ?? string.Empty;
            Pins = pins?.ToList() ?? new List<string>();
            PinCount = Pins.Count;
            ClockHz = clockHz;
            BootloaderBytes = bootloaderBytes;
            CoreLimited = coreLimited;
        }
    }

    public static class BoardProfiles
    {
        private static readonly BoardProfileInfo Uno = new BoardProfileInfo(
            "ArduinoUno",
            "ATmega328P",
            BuildUnoPins(),
            16_000_000.0,
            0x0200,
            false);

        private static readonly BoardProfileInfo Nano = new BoardProfileInfo(
            "ArduinoNano",
            "ATmega328P",
            BuildUnoPins(),
            16_000_000.0,
            0x0200,
            false);

        private static readonly BoardProfileInfo ProMini = new BoardProfileInfo(
            "ArduinoProMini",
            "ATmega328P",
            BuildUnoPins(),
            16_000_000.0,
            0x0200,
            false);

        private static readonly BoardProfileInfo Mega = new BoardProfileInfo(
            "ArduinoMega",
            "ATmega2560",
            BuildMegaPins(),
            16_000_000.0,
            0x2000,
            true);

        public static BoardProfileInfo Get(string id)
        {
            string key = Normalize(id);
            if (key == "arduinomega" || key == "mega" || key == "arduinomega2560")
            {
                return Mega;
            }
            if (key == "arduinonano" || key == "nano")
            {
                return Nano;
            }
            if (key == "arduinopromini" || key == "promini")
            {
                return ProMini;
            }
            return Uno;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Trim();
            var output = new char[chars.Length];
            int count = 0;
            foreach (char c in chars)
            {
                if (char.IsLetterOrDigit(c))
                {
                    output[count++] = char.ToLowerInvariant(c);
                }
            }
            return count == 0 ? string.Empty : new string(output, 0, count);
        }

        private static IReadOnlyList<string> BuildUnoPins()
        {
            var pins = new List<string>();
            for (int i = 0; i <= 13; i++)
            {
                pins.Add($"D{i}");
            }
            for (int i = 0; i <= 5; i++)
            {
                pins.Add($"A{i}");
            }
            return pins;
        }

        private static IReadOnlyList<string> BuildMegaPins()
        {
            var pins = new List<string>();
            for (int i = 0; i <= 53; i++)
            {
                pins.Add($"D{i}");
            }
            for (int i = 0; i <= 15; i++)
            {
                pins.Add($"A{i}");
            }
            return pins;
        }
    }
}
