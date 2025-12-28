using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Runtime
{
    public enum VirtualOpCode
    {
        Nop,
        Ldi,
        Out,
        Sbi,
        Cbi,
        Rjmp,
        DelayCycles
    }

    public struct VirtualInstruction
    {
        public VirtualOpCode Op;
        public byte Arg0;
        public byte Arg1;
        public long Arg2;
    }

    public class VirtualArduinoProgram
    {
        public List<VirtualInstruction> Instructions { get; } = new List<VirtualInstruction>();
    }

    public static class VirtualArduinoProgramFactory
    {
        private const int DefaultBlinkMs = 500;

        public static VirtualArduinoProgram FromFirmwareString(string firmware, VirtualArduinoHal hal)
        {
            if (string.IsNullOrWhiteSpace(firmware))
            {
                return BuildBlinkProgram("D13", DefaultBlinkMs, hal);
            }

            string[] parts = firmware.Split(':');
            if (parts.Length >= 1 && parts[0].Equals("blink", StringComparison.OrdinalIgnoreCase))
            {
                string pin = parts.Length > 1 ? parts[1] : "D13";
                int ms = DefaultBlinkMs;
                if (parts.Length > 2 && int.TryParse(parts[2], out var parsed))
                {
                    ms = parsed;
                }
                return BuildBlinkProgram(pin, ms, hal);
            }

            return BuildBlinkProgram("D13", DefaultBlinkMs, hal);
        }

        public static VirtualArduinoProgram BuildBlinkProgram(string pin, int intervalMs, VirtualArduinoHal hal)
        {
            var program = new VirtualArduinoProgram();
            if (!hal.TryGetPortBit(pin, out var ddr, out var port, out var bit))
            {
                return program;
            }

            long delayCycles = (long)(intervalMs * 16000.0);

            program.Instructions.Add(new VirtualInstruction { Op = VirtualOpCode.Sbi, Arg0 = ddr, Arg1 = (byte)bit });
            int loopStart = program.Instructions.Count;
            program.Instructions.Add(new VirtualInstruction { Op = VirtualOpCode.Sbi, Arg0 = port, Arg1 = (byte)bit });
            program.Instructions.Add(new VirtualInstruction { Op = VirtualOpCode.DelayCycles, Arg2 = delayCycles });
            program.Instructions.Add(new VirtualInstruction { Op = VirtualOpCode.Cbi, Arg0 = port, Arg1 = (byte)bit });
            program.Instructions.Add(new VirtualInstruction { Op = VirtualOpCode.DelayCycles, Arg2 = delayCycles });

            int offset = loopStart - program.Instructions.Count;
            program.Instructions.Add(new VirtualInstruction { Op = VirtualOpCode.Rjmp, Arg2 = offset });

            return program;
        }
    }
}
