using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RobotTwin.CoreSim;

namespace RobotTwin.CoreSim.Runtime
{
    public class VirtualArduino
    {
        public const float DefaultHighVoltage = 5.0f;
        private const double ClockHz = 16_000_000.0;

        public string Id { get; }
        public string BoardProfileId { get; }
        public VirtualClock Clock { get; }
        public VirtualRegisterFile Registers { get; }
        public VirtualArduinoHal Hal { get; }
        public VirtualArduinoCpu Cpu { get; }
        public VirtualMemory Memory { get; }
        public string FirmwareSource { get; private set; } = string.Empty;
        public bool FirmwareLoaded { get; private set; }

        public VirtualArduino(string id, BoardProfileInfo profile = null)
        {
            Id = id;
            var resolved = profile ?? BoardProfiles.Get("ArduinoUno");
            BoardProfileId = resolved.Id;
            Clock = new VirtualClock(resolved.ClockHz > 0 ? resolved.ClockHz : ClockHz);
            Registers = new VirtualRegisterFile();
            var pins = resolved.CoreLimited && resolved.Pins.Count > 20
                ? new List<string>(resolved.Pins).GetRange(0, 20)
                : resolved.Pins;
            Hal = new VirtualArduinoHal(Registers, pins);
            Cpu = new VirtualArduinoCpu(Registers);
            Memory = new VirtualMemory();
        }

        public void LoadProgram(VirtualArduinoProgram program)
        {
            Cpu.LoadProgram(program);
            FirmwareLoaded = true;
        }

        public void Step(float dtSeconds)
        {
            if (dtSeconds <= 0f) return;
            long cycles = Clock.Advance(dtSeconds);
            Cpu.ExecuteCycles(cycles);
        }

        public void ApplyProperty(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (string.Equals(key, "virtualFirmware", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    FirmwareSource = string.Empty;
                    FirmwareLoaded = false;
                    return;
                }
                LoadProgram(VirtualArduinoProgramFactory.FromFirmwareString(value, Hal));
                FirmwareSource = value;
                FirmwareLoaded = true;
                return;
            }
            if (key.StartsWith("pin:", StringComparison.OrdinalIgnoreCase))
            {
                string pin = key.Substring(4);
                Hal.SetPinOverride(pin, value);
                return;
            }
            if (string.Equals(key, "firmware", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    FirmwareSource = string.Empty;
                    FirmwareLoaded = false;
                    return;
                }
                if (TryLoadHexValue(value))
                {
                    return;
                }
                LoadProgram(VirtualArduinoProgramFactory.FromFirmwareString(value, Hal));
                FirmwareSource = value;
                FirmwareLoaded = true;
                return;
            }
            if (string.Equals(key, "firmwareHex", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "hex", StringComparison.OrdinalIgnoreCase))
            {
                TryLoadHexText(value);
                return;
            }
            if (string.Equals(key, "firmwarePath", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "hexPath", StringComparison.OrdinalIgnoreCase))
            {
                TryLoadHexPath(value);
                return;
            }
        }

        public void ConfigureFromProperties(Dictionary<string, string> properties)
        {
            if (properties == null) return;
            if (properties.TryGetValue("virtualFirmware", out var virtualFirmware))
            {
                foreach (var kvp in properties)
                {
                    if (string.Equals(kvp.Key, "virtualFirmware", StringComparison.OrdinalIgnoreCase)) continue;
                    ApplyProperty(kvp.Key, kvp.Value);
                }
                ApplyProperty("virtualFirmware", virtualFirmware);
                return;
            }

            foreach (var kvp in properties)
            {
                ApplyProperty(kvp.Key, kvp.Value);
            }
        }

        public void CopyVoltages(Dictionary<string, float> target)
        {
            var voltages = Hal.GetOutputVoltages();
            foreach (var kvp in voltages)
            {
                target[$"{Id}.{kvp.Key}"] = kvp.Value;
            }
        }

        public void SetInputVoltage(string pin, float voltage)
        {
            Hal.SetInputVoltage(pin, voltage);
        }

        private bool TryLoadHexValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string trimmed = value.Trim();
            if (trimmed.StartsWith(":", StringComparison.Ordinal))
            {
                return TryLoadHexText(trimmed);
            }
            if (trimmed.EndsWith(".hex", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".ihx", StringComparison.OrdinalIgnoreCase))
            {
                return TryLoadHexPath(trimmed);
            }
            return false;
        }

        private bool TryLoadHexPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!File.Exists(path)) return false;
            string text = File.ReadAllText(path);
            bool loaded = TryLoadHexText(text);
            if (loaded)
            {
                FirmwareSource = path;
            }
            return loaded;
        }

        private bool TryLoadHexText(string hexText)
        {
            var image = new VirtualHexImage(VirtualMemory.FlashSizeBytes);
            if (!VirtualHexLoader.TryLoad(hexText, image, out var error))
            {
                FirmwareSource = $"hex:invalid ({error})";
                return false;
            }

            Memory.ClearFlash();
            Array.Copy(image.Data, Memory.Flash, Memory.Flash.Length);

            int decoded;
            int unknown;
            var program = VirtualArduinoProgramFactory.FromHexImage(image, out decoded, out unknown);
            Cpu.LoadProgram(program);
            FirmwareSource = $"hex:{decoded} decoded, {unknown} unknown";
            FirmwareLoaded = true;
            return true;
        }

        public void SetVoltage(string pin, float voltage)
        {
            if (string.IsNullOrWhiteSpace(pin)) return;
            Hal.SetPinOverride(pin, voltage.ToString(CultureInfo.InvariantCulture));
        }
    }
}
