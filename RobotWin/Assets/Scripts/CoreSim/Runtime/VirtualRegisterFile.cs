namespace RobotTwin.CoreSim.Runtime
{
    public class VirtualRegisterFile
    {
        public const ushort PINA = 0x20;
        public const ushort DDRA = 0x21;
        public const ushort PORTA = 0x22;
        public const ushort PINB = 0x23;
        public const ushort DDRB = 0x24;
        public const ushort PORTB = 0x25;
        public const ushort PINC = 0x26;
        public const ushort DDRC = 0x27;
        public const ushort PORTC = 0x28;
        public const ushort PIND = 0x29;
        public const ushort DDRD = 0x2A;
        public const ushort PORTD = 0x2B;
        public const ushort PINE = 0x2C;
        public const ushort DDRE = 0x2D;
        public const ushort PORTE = 0x2E;
        public const ushort PINF = 0x2F;
        public const ushort DDRF = 0x30;
        public const ushort PORTF = 0x31;
        public const ushort PING = 0x32;
        public const ushort DDRG = 0x33;
        public const ushort PORTG = 0x34;
        public const ushort PINH = 0x100;
        public const ushort DDRH = 0x101;
        public const ushort PORTH = 0x102;
        public const ushort PINJ = 0x103;
        public const ushort DDRJ = 0x104;
        public const ushort PORTJ = 0x105;
        public const ushort PINK = 0x106;
        public const ushort DDRK = 0x107;
        public const ushort PORTK = 0x108;
        public const ushort PINL = 0x109;
        public const ushort DDRL = 0x10A;
        public const ushort PORTL = 0x10B;

        private readonly byte[] _io = new byte[0x200];
        private readonly byte[] _registers = new byte[32];

        public byte ReadIo(ushort address)
        {
            int idx = address - PINA;
            if (idx < 0 || idx >= _io.Length) return 0;
            return _io[idx];
        }

        public void WriteIo(ushort address, byte value)
        {
            int idx = address - PINA;
            if (idx < 0 || idx >= _io.Length) return;
            _io[idx] = value;
        }

        public void SetBit(ushort address, int bit, bool state)
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

        public bool GetBit(ushort address, int bit)
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
