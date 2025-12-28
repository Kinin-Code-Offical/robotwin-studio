namespace RobotTwin.CoreSim.Runtime
{
    public class VirtualArduinoCpu
    {
        private readonly VirtualRegisterFile _registers;
        private VirtualArduinoProgram _program = new VirtualArduinoProgram();
        private int _pc;
        private long _delayCycles;

        public VirtualArduinoCpu(VirtualRegisterFile registers)
        {
            _registers = registers;
        }

        public void LoadProgram(VirtualArduinoProgram program)
        {
            _program = program ?? new VirtualArduinoProgram();
            _pc = 0;
            _delayCycles = 0;
        }

        public void ExecuteCycles(long cycles)
        {
            long remaining = cycles;
            while (remaining > 0)
            {
                if (_delayCycles > 0)
                {
                    long step = _delayCycles > remaining ? remaining : _delayCycles;
                    _delayCycles -= step;
                    remaining -= step;
                    continue;
                }

                if (_program.Instructions.Count == 0) return;
                if (_pc < 0 || _pc >= _program.Instructions.Count) _pc = 0;

                var instr = _program.Instructions[_pc];
                long cost = 1;
                switch (instr.Op)
                {
                    case VirtualOpCode.Nop:
                        break;
                    case VirtualOpCode.Ldi:
                        _registers.WriteRegister(instr.Arg0, instr.Arg1);
                        break;
                    case VirtualOpCode.Out:
                        _registers.WriteIo(instr.Arg0, _registers.ReadRegister(instr.Arg1));
                        break;
                    case VirtualOpCode.Sbi:
                        _registers.SetBit(instr.Arg0, instr.Arg1, true);
                        cost = 2;
                        break;
                    case VirtualOpCode.Cbi:
                        _registers.SetBit(instr.Arg0, instr.Arg1, false);
                        cost = 2;
                        break;
                    case VirtualOpCode.Rjmp:
                        _pc += (int)instr.Arg2;
                        cost = 2;
                        break;
                    case VirtualOpCode.DelayCycles:
                        _delayCycles = instr.Arg2;
                        break;
                }

                if (instr.Op != VirtualOpCode.Rjmp)
                {
                    _pc++;
                }
                remaining -= cost;
            }
        }
    }
}
