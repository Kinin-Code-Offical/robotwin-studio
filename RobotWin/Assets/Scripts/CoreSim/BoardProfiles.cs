using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Runtime;

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
        public bool CoreSupported { get; }
        public IReadOnlyList<string> Pins { get; }

        public BoardProfileInfo(
            string id,
            string mcu,
            IEnumerable<string> pins,
            double clockHz,
            int bootloaderBytes,
            bool coreLimited,
            bool coreSupported = true)
        {
            Id = id ?? string.Empty;
            Mcu = mcu ?? string.Empty;
            Pins = pins?.ToList() ?? new List<string>();
            PinCount = Pins.Count;
            ClockHz = clockHz;
            BootloaderBytes = bootloaderBytes;
            CoreLimited = coreLimited;
            CoreSupported = coreSupported;
        }
    }

    public readonly struct PinMapEntry
    {
        public ushort Ddr { get; }
        public ushort Port { get; }
        public int Bit { get; }

        public PinMapEntry(ushort ddr, ushort port, int bit)
        {
            Ddr = ddr;
            Port = port;
            Bit = bit;
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
            false,
            true);

        private static readonly BoardProfileInfo Nano = new BoardProfileInfo(
            "ArduinoNano",
            "ATmega328P",
            BuildUnoPins(),
            16_000_000.0,
            0x0200,
            false,
            true);

        private static readonly BoardProfileInfo ProMini = new BoardProfileInfo(
            "ArduinoProMini",
            "ATmega328P",
            BuildUnoPins(),
            16_000_000.0,
            0x0200,
            false,
            true);

        private static readonly BoardProfileInfo Mega = new BoardProfileInfo(
            "ArduinoMega",
            "ATmega2560",
            BuildMegaPins(),
            16_000_000.0,
            0x2000,
            false,
            true);

        private static readonly BoardProfileInfo Stm32F103 = new BoardProfileInfo(
            "STM32F103",
            "STM32F103",
            BuildStm32Pins(),
            72_000_000.0,
            0,
            true,
            false);

        private static readonly BoardProfileInfo RaspberryPi = new BoardProfileInfo(
            "RaspberryPi",
            "RaspberryPi",
            BuildRaspberryPiPins(),
            700_000_000.0,
            0,
            true,
            false);

        public static BoardProfileInfo GetDefault() => Uno;

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
            if (key == "stm32f103" || key == "stm32")
            {
                return Stm32F103;
            }
            if (key == "raspberrypi" || key == "rpi")
            {
                return RaspberryPi;
            }
            return Uno;
        }

        public static IReadOnlyList<BoardProfileInfo> GetAll()
        {
            return new[]
            {
                Uno,
                Nano,
                ProMini,
                Mega,
                Stm32F103,
                RaspberryPi
            };
        }

        public static bool IsKnownProfileId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            string key = Normalize(id);
            return key == "arduinouno" || key == "uno" ||
                   key == "arduinonano" || key == "nano" ||
                   key == "arduinopromini" || key == "promini" ||
                   key == "arduinomega" || key == "mega" ||
                   key == "arduinomega2560" || key == "mega2560" ||
                   key == "stm32f103" || key == "stm32" ||
                   key == "raspberrypi" || key == "rpi";
        }

        public static IReadOnlyList<string> GetCorePins(BoardProfileInfo profile)
        {
            if (profile == null)
            {
                return BuildUnoPins();
            }
            if (profile.CoreLimited)
            {
                return BuildUnoPins();
            }
            return profile.Pins;
        }

        public static IReadOnlyDictionary<string, PinMapEntry> GetPinMap(BoardProfileInfo profile)
        {
            if (profile == null)
            {
                return BuildUnoPinMap();
            }
            if (string.Equals(profile.Id, "ArduinoMega", StringComparison.OrdinalIgnoreCase))
            {
                return BuildMegaPinMap();
            }
            if (string.Equals(profile.Id, "STM32F103", StringComparison.OrdinalIgnoreCase))
            {
                return BuildStm32PinMap();
            }
            if (string.Equals(profile.Id, "RaspberryPi", StringComparison.OrdinalIgnoreCase))
            {
                return BuildRaspberryPiPinMap();
            }
            return BuildUnoPinMap();
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

        private static IReadOnlyList<string> BuildStm32Pins()
        {
            var pins = new List<string>();
            for (int i = 0; i <= 15; i++)
            {
                pins.Add($"PA{i}");
            }
            for (int i = 0; i <= 15; i++)
            {
                pins.Add($"PB{i}");
            }
            for (int i = 13; i <= 15; i++)
            {
                pins.Add($"PC{i}");
            }
            return pins;
        }

        private static IReadOnlyList<string> BuildRaspberryPiPins()
        {
            var pins = new List<string>();
            for (int i = 0; i <= 27; i++)
            {
                pins.Add($"GPIO{i}");
            }
            return pins;
        }

        private static IReadOnlyDictionary<string, PinMapEntry> BuildUnoPinMap()
        {
            var map = new Dictionary<string, PinMapEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i <= 7; i++)
            {
                map[$"D{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRD, Runtime.VirtualRegisterFile.PORTD, i);
            }
            for (int i = 8; i <= 13; i++)
            {
                map[$"D{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, i - 8);
            }
            for (int i = 0; i <= 5; i++)
            {
                map[$"A{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRC, Runtime.VirtualRegisterFile.PORTC, i);
            }
            return map;
        }

        private static IReadOnlyDictionary<string, PinMapEntry> BuildMegaPinMap()
        {
            var map = new Dictionary<string, PinMapEntry>(StringComparer.OrdinalIgnoreCase);
            map["D0"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRE, Runtime.VirtualRegisterFile.PORTE, 0);
            map["D1"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRE, Runtime.VirtualRegisterFile.PORTE, 1);
            map["D2"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRE, Runtime.VirtualRegisterFile.PORTE, 4);
            map["D3"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRE, Runtime.VirtualRegisterFile.PORTE, 5);
            map["D4"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRG, Runtime.VirtualRegisterFile.PORTG, 5);
            map["D5"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRE, Runtime.VirtualRegisterFile.PORTE, 3);
            map["D6"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRH, Runtime.VirtualRegisterFile.PORTH, 3);
            map["D7"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRH, Runtime.VirtualRegisterFile.PORTH, 4);
            map["D8"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRH, Runtime.VirtualRegisterFile.PORTH, 5);
            map["D9"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRH, Runtime.VirtualRegisterFile.PORTH, 6);
            map["D10"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, 4);
            map["D11"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, 5);
            map["D12"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, 6);
            map["D13"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, 7);
            map["D14"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRJ, Runtime.VirtualRegisterFile.PORTJ, 1);
            map["D15"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRJ, Runtime.VirtualRegisterFile.PORTJ, 0);
            map["D16"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRH, Runtime.VirtualRegisterFile.PORTH, 1);
            map["D17"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRH, Runtime.VirtualRegisterFile.PORTH, 0);
            map["D18"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRD, Runtime.VirtualRegisterFile.PORTD, 3);
            map["D19"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRD, Runtime.VirtualRegisterFile.PORTD, 2);
            map["D20"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRD, Runtime.VirtualRegisterFile.PORTD, 1);
            map["D21"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRD, Runtime.VirtualRegisterFile.PORTD, 0);

            for (int i = 22; i <= 29; i++)
            {
                map[$"D{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRA, Runtime.VirtualRegisterFile.PORTA, i - 22);
            }
            for (int i = 30; i <= 37; i++)
            {
                map[$"D{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRC, Runtime.VirtualRegisterFile.PORTC, 37 - i);
            }

            map["D38"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRD, Runtime.VirtualRegisterFile.PORTD, 7);
            map["D39"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRG, Runtime.VirtualRegisterFile.PORTG, 2);
            map["D40"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRG, Runtime.VirtualRegisterFile.PORTG, 1);
            map["D41"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRG, Runtime.VirtualRegisterFile.PORTG, 0);

            for (int i = 42; i <= 49; i++)
            {
                map[$"D{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRL, Runtime.VirtualRegisterFile.PORTL, 49 - i);
            }
            map["D50"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, 3);
            map["D51"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, 2);
            map["D52"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, 1);
            map["D53"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, 0);

            for (int i = 0; i <= 7; i++)
            {
                map[$"A{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRF, Runtime.VirtualRegisterFile.PORTF, i);
            }
            for (int i = 8; i <= 15; i++)
            {
                map[$"A{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRK, Runtime.VirtualRegisterFile.PORTK, i - 8);
            }
            return map;
        }

        private static IReadOnlyDictionary<string, PinMapEntry> BuildStm32PinMap()
        {
            var map = new Dictionary<string, PinMapEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i <= 7; i++)
            {
                map[$"PA{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRD, Runtime.VirtualRegisterFile.PORTD, i);
            }
            for (int i = 8; i <= 15; i++)
            {
                map[$"PA{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, i - 8);
            }
            for (int i = 0; i <= 7; i++)
            {
                map[$"PB{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRC, Runtime.VirtualRegisterFile.PORTC, i);
            }
            for (int i = 8; i <= 15; i++)
            {
                map[$"PB{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRD, Runtime.VirtualRegisterFile.PORTD, i - 8);
            }
            for (int i = 13; i <= 15; i++)
            {
                map[$"PC{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, i - 13);
            }
            return map;
        }

        private static IReadOnlyDictionary<string, PinMapEntry> BuildRaspberryPiPinMap()
        {
            var map = new Dictionary<string, PinMapEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i <= 7; i++)
            {
                map[$"GPIO{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRD, Runtime.VirtualRegisterFile.PORTD, i);
            }
            for (int i = 8; i <= 13; i++)
            {
                map[$"GPIO{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRB, Runtime.VirtualRegisterFile.PORTB, i - 8);
            }
            for (int i = 14; i <= 19; i++)
            {
                map[$"GPIO{i}"] = new PinMapEntry(Runtime.VirtualRegisterFile.DDRC, Runtime.VirtualRegisterFile.PORTC, i - 14);
            }
            return map;
        }
    }
}
