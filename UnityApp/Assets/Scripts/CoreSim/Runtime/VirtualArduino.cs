using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Runtime
{
    public class VirtualArduino
    {
        public const float DefaultHighVoltage = 5.0f;
        private const double ClockHz = 16_000_000.0;

        public string Id { get; }
        public VirtualClock Clock { get; }
        public VirtualRegisterFile Registers { get; }
        public VirtualArduinoHal Hal { get; }
        public VirtualArduinoCpu Cpu { get; }

        public VirtualArduino(string id)
        {
            Id = id;
            Clock = new VirtualClock(ClockHz);
            Registers = new VirtualRegisterFile();
            Hal = new VirtualArduinoHal(Registers);
            Cpu = new VirtualArduinoCpu(Registers);
        }

        public void LoadProgram(VirtualArduinoProgram program)
        {
            Cpu.LoadProgram(program);
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
            if (key.StartsWith("pin:", StringComparison.OrdinalIgnoreCase))
            {
                string pin = key.Substring(4);
                Hal.SetPinOverride(pin, value);
                return;
            }
            if (string.Equals(key, "firmware", StringComparison.OrdinalIgnoreCase))
            {
                LoadProgram(VirtualArduinoProgramFactory.FromFirmwareString(value, Hal));
            }
        }

        public void ConfigureFromProperties(Dictionary<string, string> properties)
        {
            if (properties == null) return;
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
    }
}
