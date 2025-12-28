namespace RobotTwin.CoreSim.Runtime
{
    public class VirtualRegisterFile
    {
        public const byte PINB = 0x23;
        public const byte DDRB = 0x24;
        public const byte PORTB = 0x25;
        public const byte PINC = 0x26;
        public const byte DDRC = 0x27;
        public const byte PORTC = 0x28;
        public const byte PIND = 0x29;
        public const byte DDRD = 0x2A;
        public const byte PORTD = 0x2B;

        private readonly byte[] _io = new byte[0x40];
        private readonly byte[] _registers = new byte[32];

        public byte ReadIo(byte address)
        {
            int idx = address - PINB;
            if (idx < 0 || idx >= _io.Length) return 0;
            return _io[idx];
        }

        public void WriteIo(byte address, byte value)
        {
            int idx = address - PINB;
            if (idx < 0 || idx >= _io.Length) return;
            _io[idx] = value;
        }

        public void SetBit(byte address, int bit, bool state)
        {
            byte value = ReadIo(address);
            if (state)
            {
                value = (byte)(value | (1 << bit));
            }
            else
            {
                value = (byte)(value & ~(1 << bit));
            }
            WriteIo(address, value);
        }

        public bool GetBit(byte address, int bit)
        {
            byte value = ReadIo(address);
            return (value & (1 << bit)) != 0;
        }

        public byte ReadRegister(int index)
        {
            if (index < 0 || index >= _registers.Length) return 0;
            return _registers[index];
        }

        public void WriteRegister(int index, byte value)
        {
            if (index < 0 || index >= _registers.Length) return;
            _registers[index] = value;
        }
    }
}
