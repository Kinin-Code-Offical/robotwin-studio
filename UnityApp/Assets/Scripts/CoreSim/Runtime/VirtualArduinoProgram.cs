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
        private const int IoAddressOffset = 0x20;

        public static VirtualArduinoProgram FromFirmwareString(string firmware, VirtualArduinoHal hal)
        {
            if (string.IsNullOrWhiteSpace(firmware))
            {
                return new VirtualArduinoProgram();
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

            return new VirtualArduinoProgram();
        }

        public static VirtualArduinoProgram FromHexImage(VirtualHexImage image, out int decoded, out int unknown)
        {
            decoded = 0;
            unknown = 0;
            var program = new VirtualArduinoProgram();
            if (image == null || !image.HasData) return program;

            int start = image.MinAddress & ~1;
            int end = image.MaxAddress;
            if (start < 0) start = 0;
            if (end >= image.Data.Length) end = image.Data.Length - 1;

            for (int addr = start; addr <= end - 1; addr += 2)
            {
                ushort opcode = (ushort)(image.Data[addr] | (image.Data[addr + 1] << 8));
                if (TryDecode(opcode, out var instr))
                {
                    program.Instructions.Add(instr);
                    decoded++;
                }
                else
                {
                    program.Instructions.Add(new VirtualInstruction { Op = VirtualOpCode.Nop });
                    unknown++;
                }
            }

            return program;
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

        private static bool TryDecode(ushort opcode, out VirtualInstruction instruction)
        {
            instruction = new VirtualInstruction { Op = VirtualOpCode.Nop };

            if (opcode == 0x0000)
            {
                instruction.Op = VirtualOpCode.Nop;
                return true;
            }

            if ((opcode & 0xF000) == 0xC000)
            {
                short k = (short)(opcode & 0x0FFF);
                if ((k & 0x0800) != 0)
                {
                    k |= unchecked((short)0xF000);
                }
                instruction.Op = VirtualOpCode.Rjmp;
                instruction.Arg2 = k;
                return true;
            }

            if ((opcode & 0xF000) == 0xE000)
            {
                int d = 16 + ((opcode >> 4) & 0xF);
                int k = (opcode & 0xF) | ((opcode >> 4) & 0xF0);
                instruction.Op = VirtualOpCode.Ldi;
                instruction.Arg0 = (byte)d;
                instruction.Arg1 = (byte)k;
                return true;
            }

            if ((opcode & 0xF800) == 0xB800)
            {
                int a = (opcode & 0xF) | ((opcode >> 5) & 0x30);
                int r = (opcode >> 4) & 0x1F;
                instruction.Op = VirtualOpCode.Out;
                instruction.Arg0 = (byte)(IoAddressOffset + a);
                instruction.Arg1 = (byte)r;
                return true;
            }

            if ((opcode & 0xFF00) == 0x9A00)
            {
                int a = (opcode >> 3) & 0x1F;
                int b = opcode & 0x7;
                instruction.Op = VirtualOpCode.Sbi;
                instruction.Arg0 = (byte)(IoAddressOffset + a);
                instruction.Arg1 = (byte)b;
                return true;
            }

            if ((opcode & 0xFF00) == 0x9800)
            {
                int a = (opcode >> 3) & 0x1F;
                int b = opcode & 0x7;
                instruction.Op = VirtualOpCode.Cbi;
                instruction.Arg0 = (byte)(IoAddressOffset + a);
                instruction.Arg1 = (byte)b;
                return true;
            }

            return false;
        }
    }
}
