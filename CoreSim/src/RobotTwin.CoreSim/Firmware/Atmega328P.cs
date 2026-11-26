using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>What an MCU pin is doing, as seen from outside the chip.</summary>
    public readonly struct PinOutput
    {
        public PinOutput(bool driving, bool level, bool pullup, double pwmDuty, double pwmFrequency)
        {
            Driving = driving;
            Level = level;
            Pullup = pullup;
            PwmDuty = pwmDuty;
            PwmFrequency = pwmFrequency;
        }

        public bool Driving { get; }
        public bool Level { get; }
        public bool Pullup { get; }

        /// <summary>Hardware PWM duty 0..1 when a timer drives the pin, otherwise NaN.</summary>
        public double PwmDuty { get; }
        public double PwmFrequency { get; }
        public bool IsPwm => !double.IsNaN(PwmDuty);
    }

    /// <summary>
    /// Cycle-counted ATmega328P (Arduino Uno / Nano / Pro Mini): complete AVRe+
    /// instruction set, interrupts, GPIO with pin-change and external interrupts,
    /// Timer0/1/2 with all waveform modes, USART0, ADC, EEPROM, watchdog, a
    /// timed TWI master on <see cref="I2c"/> and minimal SPI so libraries never hang.
    /// </summary>
    public sealed partial class Atmega328P
    {
        public const int FlashWords = 16384;
        public const int DataSize = 0x900;
        public const int EepromSize = 1024;
        public const int PinCount = 20;

        private readonly ushort[] _flash = new ushort[FlashWords];
        private readonly byte[] _d = new byte[DataSize];
        private readonly byte[] _eeprom = new byte[EepromSize];
        private int _pc;
        private bool _sleeping;
        private int _interruptInhibit;
        private bool _c, _z, _n, _v, _s, _h, _t, _i;

        public Atmega328P(double clockHz = 16e6)
        {
            ClockHz = clockHz;
            for (int i = 0; i < _eeprom.Length; i++) _eeprom[i] = 0xFF;
            for (int i = 0; i < _flash.Length; i++) _flash[i] = 0xFFFF;
            Reset();
        }

        public double ClockHz { get; }
        public ulong Cycles { get; private set; }
        public int ProgramCounter => _pc * 2;
        public bool IsSleeping => _sleeping;

        /// <summary>Supply voltage; also the ADC reference when AVcc is selected.</summary>
        public double Vcc { get; set; } = 5.0;

        /// <summary>Held in reset (brown-out or RESET pin low): the CPU does not run.</summary>
        public bool HoldReset { get; set; }

        /// <summary>[debug] Asked before every instruction while a debugger watches the program (null: not watched, no cost).</summary>
        internal Debug.IMcuDebugHook? DebugHook { get; set; }

        /// <summary>[debug] Stopped before an instruction for the debugger: runs nothing until it is resumed.</summary>
        public bool Halted { get; internal set; }

        /// <summary>The stack pointer (SPH:SPL).</summary>
        public int StackPointer => Sp;

        /// <summary>Counts instructions the core met but does not implement (should stay 0).</summary>
        public long UnknownInstructions { get; private set; }

        public byte[] Eeprom => _eeprom;

        // ================================================================ loading

        public void LoadHex(string hexText)
        {
            var blocks = IntelHex.Parse(hexText);
            if (blocks.Count == 0) throw new InvalidDataException("HEX dosyasında program verisi yok.");
            for (int i = 0; i < _flash.Length; i++) _flash[i] = 0xFFFF;
            foreach (var (address, data) in blocks)
            {
                if (address + data.Length > FlashWords * 2) throw new InvalidDataException($"Program 32 KB flash'a sığmıyor (0x{address + data.Length:X}).");
                for (int i = 0; i < data.Length; i++)
                {
                    uint a = address + (uint)i;
                    int w = (int)(a >> 1);
                    _flash[w] = (a & 1) == 0
                        ? (ushort)((_flash[w] & 0xFF00) | data[i])
                        : (ushort)((_flash[w] & 0x00FF) | (data[i] << 8));
                }
            }

            Reset();
        }

        /// <summary>Loads raw program words at address 0 (tests, hand-written code).</summary>
        public void LoadProgram(params ushort[] words)
        {
            for (int i = 0; i < _flash.Length; i++) _flash[i] = i < words.Length ? words[i] : (ushort)0xFFFF;
            Reset();
        }

        /// <summary>Reads data space (registers, I/O, SRAM) without side effects.</summary>
        public byte ReadData(int address) => address >= 0 && address < DataSize ? _d[address] : (byte)0;

        public void Reset()
        {
            Halted = false;
            Array.Clear(_d, 0, _d.Length);
            _pc = 0;
            _sleeping = false;
            _interruptInhibit = 0;
            _c = _z = _n = _v = _s = _h = _t = _i = false;
            Sp = DataSize - 1;
            ResetPeripherals();
        }

        // ================================================================ register file & memory

        private int Sp
        {
            get => _d[0x5D] | (_d[0x5E] << 8);
            set
            {
                _d[0x5D] = (byte)value;
                _d[0x5E] = (byte)(value >> 8);
            }
        }

        private byte Sreg
        {
            get => (byte)((_c ? 1 : 0) | (_z ? 2 : 0) | (_n ? 4 : 0) | (_v ? 8 : 0) | (_s ? 16 : 0) | (_h ? 32 : 0) | (_t ? 64 : 0) | (_i ? 128 : 0));
            set
            {
                _c = (value & 1) != 0;
                _z = (value & 2) != 0;
                _n = (value & 4) != 0;
                _v = (value & 8) != 0;
                _s = (value & 16) != 0;
                _h = (value & 32) != 0;
                _t = (value & 64) != 0;
                bool wasI = _i;
                _i = (value & 128) != 0;
                if (_i && !wasI) _interruptInhibit = 1;
            }
        }

        private byte Read(int address)
        {
            if (address < 0x20) return _d[address];
            if (address < 0x100) return ReadIo(address);
            return address < DataSize ? _d[address] : (byte)0;
        }

        private void Write(int address, byte value)
        {
            if (address < 0x20) _d[address] = value;
            else if (address < 0x100) WriteIo(address, value);
            else if (address < DataSize) _d[address] = value;
        }

        private void Push(byte value)
        {
            int sp = Sp;
            Write(sp, value);
            Sp = (sp - 1) & 0xFFFF;
        }

        private byte Pop()
        {
            int sp = (Sp + 1) & 0xFFFF;
            Sp = sp;
            return Read(sp);
        }

        private void PushPc(int pc)
        {
            Push((byte)pc);
            Push((byte)(pc >> 8));
        }

        private int PopPc()
        {
            int high = Pop();
            int low = Pop();
            return ((high << 8) | low) & (FlashWords - 1);
        }

        private int RegWord(int r) => _d[r] | (_d[r + 1] << 8);

        private void SetRegWord(int r, int value)
        {
            _d[r] = (byte)value;
            _d[r + 1] = (byte)(value >> 8);
        }

        private static bool IsTwoWord(ushort op) =>
            (op & 0xFE0F) == 0x9000 || (op & 0xFE0F) == 0x9200 || (op & 0xFE0C) == 0x940C;

        private int SkipNext()
        {
            ushort next = _flash[_pc];
            int words = IsTwoWord(next) ? 2 : 1;
            _pc = (_pc + words) & (FlashWords - 1);
            return words;
        }

        // ================================================================ execution

        /// <summary>Runs for at least <paramref name="cycles"/> clock cycles (fewer when the debugger stops it: <see cref="Halted"/>).</summary>
        public void RunCycles(long cycles)
        {
            long target = (long)Cycles + cycles;
            while ((long)Cycles < target)
            {
                if (Halted) return;
                if (HoldReset)
                {
                    long rest = target - (long)Cycles;
                    Cycles += (ulong)rest;
                    _pc = 0;
                    return;
                }

                int spent;
                if (_sleeping)
                {
                    spent = (int)Math.Min(64, target - (long)Cycles);
                    if (spent < 1) spent = 1;
                }
                else if (_interruptInhibit == 0 && _i && TryEnterInterrupt())
                {
                    spent = 5;
                }
                else
                {
                    if (DebugHook != null && DebugHook.ShouldStop(_pc))
                    {
                        Halted = true;   // before the instruction: the time it stopped at is exact
                        return;
                    }

                    if (_interruptInhibit > 0) _interruptInhibit--;
                    spent = Step();
                }

                Cycles += (ulong)spent;
                AdvancePeripherals(spent);
                if (_sleeping && _i && AnyInterruptPending()) _sleeping = false;
            }
        }

        public void Run(double seconds) => RunCycles((long)Math.Round(seconds * ClockHz));

        private bool TryEnterInterrupt()
        {
            int vector = PendingVector();
            if (vector < 0) return false;
            AcknowledgeInterrupt(vector);
            Trace?.Add(new TraceEvent(Cycles, TraceKind.Enter, (byte)vector, false));
            _sleeping = false;
            PushPc(_pc);
            _i = false;
            _pc = vector * 2;
            return true;
        }

        private bool AnyInterruptPending() => PendingVector() >= 0;

        /// <summary>Executes one instruction and returns the cycles it took.</summary>
        private int Step()
        {
            ushort op = _flash[_pc];
            _pc = (_pc + 1) & (FlashWords - 1);

            switch (op >> 12)
            {
                case 0x0:
                {
                    if (op == 0x0000) return 1;
                    switch (op & 0xFF00)
                    {
                        case 0x0100: // MOVW
                        {
                            int d = ((op >> 4) & 0xF) * 2, r = (op & 0xF) * 2;
                            _d[d] = _d[r];
                            _d[d + 1] = _d[r + 1];
                            return 1;
                        }

                        case 0x0200: // MULS
                        {
                            int d = 16 + ((op >> 4) & 0xF), r = 16 + (op & 0xF);
                            int res = (sbyte)_d[d] * (sbyte)_d[r];
                            StoreMul(res & 0xFFFF);
                            return 2;
                        }

                        case 0x0300: // MULSU / FMUL / FMULS / FMULSU
                        {
                            int d = 16 + ((op >> 4) & 7), r = 16 + (op & 7);
                            int res;
                            switch (op & 0x88)
                            {
                                case 0x00: res = (sbyte)_d[d] * _d[r]; StoreMul(res & 0xFFFF); return 2;
                                case 0x08: res = _d[d] * _d[r]; break;
                                case 0x80: res = (sbyte)_d[d] * (sbyte)_d[r]; break;
                                default: res = (sbyte)_d[d] * _d[r]; break;
                            }

                            res &= 0xFFFF;
                            _c = (res & 0x8000) != 0;
                            res = (res << 1) & 0xFFFF;
                            _z = res == 0;
                            _d[0] = (byte)res;
                            _d[1] = (byte)(res >> 8);
                            return 2;
                        }
                    }

                    int dd = (op >> 4) & 0x1F, rr = (op & 0xF) | ((op >> 5) & 0x10);
                    switch (op & 0xFC00)
                    {
                        case 0x0400: Sub(_d[dd], _d[rr], _c, true); return 1;                 // CPC
                        case 0x0800: _d[dd] = Sub(_d[dd], _d[rr], _c, true); return 1;        // SBC
                        case 0x0C00: _d[dd] = Add(_d[dd], _d[rr], false); return 1;           // ADD / LSL
                    }

                    break;
                }

                case 0x1:
                {
                    int d = (op >> 4) & 0x1F, r = (op & 0xF) | ((op >> 5) & 0x10);
                    switch (op & 0xFC00)
                    {
                        case 0x1000: // CPSE
                            if (_d[d] == _d[r]) return 1 + SkipNext();
                            return 1;
                        case 0x1400: Sub(_d[d], _d[r], false, false); return 1;              // CP
                        case 0x1800: _d[d] = Sub(_d[d], _d[r], false, false); return 1;      // SUB
                        default: _d[d] = Add(_d[d], _d[r], _c); return 1;                     // ADC / ROL
                    }
                }

                case 0x2:
                {
                    int d = (op >> 4) & 0x1F, r = (op & 0xF) | ((op >> 5) & 0x10);
                    switch (op & 0xFC00)
                    {
                        case 0x2000: _d[d] = Logic((byte)(_d[d] & _d[r])); return 1;         // AND / TST
                        case 0x2400: _d[d] = Logic((byte)(_d[d] ^ _d[r])); return 1;         // EOR / CLR
                        case 0x2800: _d[d] = Logic((byte)(_d[d] | _d[r])); return 1;         // OR
                        default: _d[d] = _d[r]; return 1;                                       // MOV
                    }
                }

                case 0x3: // CPI
                {
                    int d = 16 + ((op >> 4) & 0xF);
                    Sub(_d[d], (byte)(((op >> 4) & 0xF0) | (op & 0xF)), false, false);
                    return 1;
                }

                case 0x4: // SBCI
                {
                    int d = 16 + ((op >> 4) & 0xF);
                    _d[d] = Sub(_d[d], (byte)(((op >> 4) & 0xF0) | (op & 0xF)), _c, true);
                    return 1;
                }

                case 0x5: // SUBI
                {
                    int d = 16 + ((op >> 4) & 0xF);
                    _d[d] = Sub(_d[d], (byte)(((op >> 4) & 0xF0) | (op & 0xF)), false, false);
                    return 1;
                }

                case 0x6: // ORI
                {
                    int d = 16 + ((op >> 4) & 0xF);
                    _d[d] = Logic((byte)(_d[d] | (((op >> 4) & 0xF0) | (op & 0xF))));
                    return 1;
                }

                case 0x7: // ANDI
                {
                    int d = 16 + ((op >> 4) & 0xF);
                    _d[d] = Logic((byte)(_d[d] & (((op >> 4) & 0xF0) | (op & 0xF))));
                    return 1;
                }

                case 0x8:
                case 0xA: // LDD / STD with displacement
                {
                    int q = ((op >> 8) & 0x20) | ((op >> 7) & 0x18) | (op & 7);
                    int d = (op >> 4) & 0x1F;
                    int baseReg = (op & 0x8) != 0 ? 28 : 30;
                    int addr = (RegWord(baseReg) + q) & 0xFFFF;
                    if ((op & 0x0200) != 0) Write(addr, _d[d]);
                    else _d[d] = Read(addr);
                    return 2;
                }

                case 0x9:
                    return Execute9(op);

                case 0xB: // IN / OUT
                {
                    int d = (op >> 4) & 0x1F;
                    int a = ((op >> 5) & 0x30) | (op & 0xF);
                    if ((op & 0x0800) == 0) _d[d] = Read(a + 0x20);
                    else Write(a + 0x20, _d[d]);
                    return 1;
                }

                case 0xC: // RJMP
                {
                    int k = op & 0xFFF;
                    if ((k & 0x800) != 0) k -= 0x1000;
                    _pc = (_pc + k) & (FlashWords - 1);
                    return 2;
                }

                case 0xD: // RCALL
                {
                    int k = op & 0xFFF;
                    if ((k & 0x800) != 0) k -= 0x1000;
                    PushPc(_pc);
                    _pc = (_pc + k) & (FlashWords - 1);
                    return 3;
                }

                case 0xE: // LDI
                    _d[16 + ((op >> 4) & 0xF)] = (byte)(((op >> 4) & 0xF0) | (op & 0xF));
                    return 1;

                case 0xF:
                {
                    if ((op & 0xFC00) == 0xF000 || (op & 0xFC00) == 0xF400) // BRBS / BRBC
                    {
                        int s = op & 7;
                        bool set = (Sreg & (1 << s)) != 0;
                        bool branchIfSet = (op & 0x0400) == 0;
                        if (set == branchIfSet)
                        {
                            int k = (op >> 3) & 0x7F;
                            if ((k & 0x40) != 0) k -= 0x80;
                            _pc = (_pc + k) & (FlashWords - 1);
                            return 2;
                        }

                        return 1;
                    }

                    int d = (op >> 4) & 0x1F, b = op & 7;
                    switch (op & 0xFE08)
                    {
                        case 0xF800: // BLD
                            _d[d] = _t ? (byte)(_d[d] | (1 << b)) : (byte)(_d[d] & ~(1 << b));
                            return 1;
                        case 0xFA00: // BST
                            _t = (_d[d] & (1 << b)) != 0;
                            return 1;
                        case 0xFC00: // SBRC
                            if ((_d[d] & (1 << b)) == 0) return 1 + SkipNext();
                            return 1;
                        case 0xFE00: // SBRS
                            if ((_d[d] & (1 << b)) != 0) return 1 + SkipNext();
                            return 1;
                    }

                    break;
                }
            }

            UnknownInstructions++;
            return 1;
        }

        private int Execute9(ushort op)
        {
            int d = (op >> 4) & 0x1F;
            switch (op & 0xFE00)
            {
                case 0x9000: // loads
                {
                    switch (op & 0xF)
                    {
                        case 0x0: _d[d] = Read(_flash[_pc]); _pc = (_pc + 1) & (FlashWords - 1); return 2;      // LDS
                        case 0x1: { int z = RegWord(30); _d[d] = Read(z); SetRegWord(30, z + 1); return 2; }   // LD Z+
                        case 0x2: { int z = (RegWord(30) - 1) & 0xFFFF; SetRegWord(30, z); _d[d] = Read(z); return 2; }
                        case 0x4: _d[d] = FlashByte(RegWord(30)); return 3;                                      // LPM Z
                        case 0x5: { int z = RegWord(30); _d[d] = FlashByte(z); SetRegWord(30, z + 1); return 3; }
                        case 0x6: _d[d] = FlashByte(RegWord(30) | (_d[0x5B] << 16)); return 3;                  // ELPM Z
                        case 0x7:
                        {
                            int z = RegWord(30) | (_d[0x5B] << 16);
                            _d[d] = FlashByte(z);
                            z++;
                            SetRegWord(30, z);
                            _d[0x5B] = (byte)(z >> 16);
                            return 3;
                        }

                        case 0x9: { int y = RegWord(28); _d[d] = Read(y); SetRegWord(28, y + 1); return 2; }   // LD Y+
                        case 0xA: { int y = (RegWord(28) - 1) & 0xFFFF; SetRegWord(28, y); _d[d] = Read(y); return 2; }
                        case 0xC: _d[d] = Read(RegWord(26)); return 2;                                           // LD X
                        case 0xD: { int x = RegWord(26); _d[d] = Read(x); SetRegWord(26, x + 1); return 2; }   // LD X+
                        case 0xE: { int x = (RegWord(26) - 1) & 0xFFFF; SetRegWord(26, x); _d[d] = Read(x); return 2; }
                        case 0xF: _d[d] = Pop(); return 2;                                                        // POP
                    }

                    break;
                }

                case 0x9200: // stores
                {
                    switch (op & 0xF)
                    {
                        case 0x0: Write(_flash[_pc], _d[d]); _pc = (_pc + 1) & (FlashWords - 1); return 2;     // STS
                        case 0x1: { int z = RegWord(30); Write(z, _d[d]); SetRegWord(30, z + 1); return 2; }
                        case 0x2: { int z = (RegWord(30) - 1) & 0xFFFF; SetRegWord(30, z); Write(z, _d[d]); return 2; }
                        case 0x4: { int z = RegWord(30); byte m = Read(z); Write(z, _d[d]); _d[d] = m; return 2; }        // XCH
                        case 0x5: { int z = RegWord(30); byte m = Read(z); Write(z, (byte)(m | _d[d])); _d[d] = m; return 2; } // LAS
                        case 0x6: { int z = RegWord(30); byte m = Read(z); Write(z, (byte)(m & ~_d[d])); _d[d] = m; return 2; } // LAC
                        case 0x7: { int z = RegWord(30); byte m = Read(z); Write(z, (byte)(m ^ _d[d])); _d[d] = m; return 2; } // LAT
                        case 0x9: { int y = RegWord(28); Write(y, _d[d]); SetRegWord(28, y + 1); return 2; }
                        case 0xA: { int y = (RegWord(28) - 1) & 0xFFFF; SetRegWord(28, y); Write(y, _d[d]); return 2; }
                        case 0xC: Write(RegWord(26), _d[d]); return 2;
                        case 0xD: { int x = RegWord(26); Write(x, _d[d]); SetRegWord(26, x + 1); return 2; }
                        case 0xE: { int x = (RegWord(26) - 1) & 0xFFFF; SetRegWord(26, x); Write(x, _d[d]); return 2; }
                        case 0xF: Push(_d[d]); return 2;                                                           // PUSH
                    }

                    break;
                }

            }

            if ((op & 0xFE00) == 0x9400)
            {
                switch (op & 0xF)
                {
                    case 0x0: { byte r = (byte)~_d[d]; _d[d] = r; _c = true; _v = false; SetNZS(r); return 1; }                // COM
                    case 0x1:                                                                                                       // NEG
                    {
                        byte rd = _d[d];
                        byte r = (byte)(0 - rd);
                        _h = ((r | rd) & 0x08) != 0;
                        _v = r == 0x80;
                        _c = r != 0;
                        _d[d] = r;
                        SetNZS(r);
                        return 1;
                    }

                    case 0x2: { byte v = _d[d]; _d[d] = (byte)((v << 4) | (v >> 4)); return 1; }                                 // SWAP
                    case 0x3: { byte r = (byte)(_d[d] + 1); _v = r == 0x80; _d[d] = r; SetNZS(r); return 1; }                   // INC
                    case 0x5: { byte v = _d[d]; _c = (v & 1) != 0; byte r = (byte)((v >> 1) | (v & 0x80)); _d[d] = r; _n = (r & 0x80) != 0; _z = r == 0; _v = _n ^ _c; _s = _n ^ _v; return 1; } // ASR
                    case 0x6: { byte v = _d[d]; _c = (v & 1) != 0; byte r = (byte)(v >> 1); _d[d] = r; _n = false; _z = r == 0; _v = _c; _s = _v; return 1; }   // LSR
                    case 0x7: { byte v = _d[d]; byte r = (byte)((v >> 1) | (_c ? 0x80 : 0)); _c = (v & 1) != 0; _d[d] = r; _n = (r & 0x80) != 0; _z = r == 0; _v = _n ^ _c; _s = _n ^ _v; return 1; } // ROR
                    case 0xA: { byte r = (byte)(_d[d] - 1); _v = r == 0x7F; _d[d] = r; SetNZS(r); return 1; }                   // DEC
                    case 0xC:
                    case 0xD: // JMP (22-bit; 328P uses the low 14 bits)
                    {
                        int k = (((op >> 3) & 0x3E) | (op & 1)) << 16 | _flash[_pc];
                        _pc = k & (FlashWords - 1);
                        return 3;
                    }

                    case 0xE:
                    case 0xF: // CALL
                    {
                        int k = (((op >> 3) & 0x3E) | (op & 1)) << 16 | _flash[_pc];
                        PushPc((_pc + 1) & (FlashWords - 1));
                        _pc = k & (FlashWords - 1);
                        return 4;
                    }

                    case 0x8:
                        return Execute9408(op);
                    case 0x9:
                        switch (op)
                        {
                            case 0x9409: // IJMP
                            case 0x9419: // EIJMP
                                _pc = RegWord(30) & (FlashWords - 1);
                                return 2;
                            case 0x9509: // ICALL
                            case 0x9519: // EICALL
                                PushPc(_pc);
                                _pc = RegWord(30) & (FlashWords - 1);
                                return 3;
                        }

                        break;
                    case 0xB: // DES (XMEGA only)
                        return 1;
                }
            }

            switch (op & 0xFF00)
            {
                case 0x9600: // ADIW
                case 0x9700: // SBIW
                {
                    int r = 24 + ((op >> 4) & 3) * 2;
                    int k = ((op >> 2) & 0x30) | (op & 0xF);
                    int v = RegWord(r);
                    bool sub = (op & 0x0100) != 0;
                    int res = (sub ? v - k : v + k) & 0xFFFF;
                    bool rdh7 = (v & 0x8000) != 0, r15 = (res & 0x8000) != 0;
                    if (sub)
                    {
                        _v = rdh7 && !r15;
                        _c = r15 && !rdh7;
                    }
                    else
                    {
                        _v = !rdh7 && r15;
                        _c = !r15 && rdh7;
                    }

                    _n = r15;
                    _z = res == 0;
                    _s = _n ^ _v;
                    SetRegWord(r, res);
                    return 2;
                }

                case 0x9800: // CBI
                case 0x9A00: // SBI
                {
                    int a = 0x20 + ((op >> 3) & 0x1F);
                    int bit = op & 7;
                    byte v = Read(a);
                    if (a == 0x23 || a == 0x26 || a == 0x29)
                    {
                        // PINx: SBI toggles the port bit; CBI has no effect.
                        if ((op & 0x0200) != 0) Write(a, (byte)(1 << bit));
                    }
                    else
                    {
                        Write(a, (op & 0x0200) != 0 ? (byte)(v | (1 << bit)) : (byte)(v & ~(1 << bit)));
                    }

                    return 2;
                }

                case 0x9900: // SBIC
                case 0x9B00: // SBIS
                {
                    int a = 0x20 + ((op >> 3) & 0x1F);
                    bool set = (Read(a) & (1 << (op & 7))) != 0;
                    if (set == ((op & 0x0200) != 0)) return 1 + SkipNext();
                    return 1;
                }
            }

            if ((op & 0xFC00) == 0x9C00) // MUL
            {
                int r = (op & 0xF) | ((op >> 5) & 0x10);
                StoreMul(_d[d] * _d[r]);
                return 2;
            }

            UnknownInstructions++;
            return 1;
        }

        private int Execute9408(ushort op)
        {
            switch (op)
            {
                case 0x9508: _pc = PopPc(); return 4;                         // RET
                case 0x9518: _pc = PopPc(); _i = true; _interruptInhibit = 1; Trace?.Add(new TraceEvent(Cycles, TraceKind.Return, 0, false)); return 4; // RETI
                case 0x9588: if ((_d[0x53] & 1) != 0) _sleeping = true; return 1;     // SLEEP
                case 0x9598: return 1;                                         // BREAK
                case 0x95A8: _wdtCounter = 0; return 1;                        // WDR
                case 0x95C8: _d[0] = FlashByte(RegWord(30)); return 3;          // LPM r0
                case 0x95D8: _d[0] = FlashByte(RegWord(30) | (_d[0x5B] << 16)); return 3; // ELPM r0
                case 0x95E8: case 0x95F8: return 1;                             // SPM: self-programming not modelled
            }

            if ((op & 0xFF8F) == 0x9408) // BSET
            {
                int s = (op >> 4) & 7;
                Sreg = (byte)(Sreg | (1 << s));
                return 1;
            }

            if ((op & 0xFF8F) == 0x9488) // BCLR
            {
                int s = (op >> 4) & 7;
                Sreg = (byte)(Sreg & ~(1 << s));
                return 1;
            }

            UnknownInstructions++;
            return 1;
        }

        private byte FlashByte(int byteAddress)
        {
            int w = (byteAddress >> 1) & (FlashWords - 1);
            return (byteAddress & 1) == 0 ? (byte)_flash[w] : (byte)(_flash[w] >> 8);
        }

        // ================================================================ ALU

        private void SetNZS(byte r)
        {
            _n = (r & 0x80) != 0;
            _z = r == 0;
            _s = _n ^ _v;
        }

        private byte Logic(byte r)
        {
            _v = false;
            SetNZS(r);
            return r;
        }

        private byte Add(byte rd, byte rr, bool carry)
        {
            int sum = rd + rr + (carry ? 1 : 0);
            byte r = (byte)sum;
            _h = (((rd & 0xF) + (rr & 0xF) + (carry ? 1 : 0)) & 0x10) != 0;
            _c = sum > 0xFF;
            _v = ((~(rd ^ rr) & (rd ^ r)) & 0x80) != 0;
            _n = (r & 0x80) != 0;
            _z = r == 0;
            _s = _n ^ _v;
            return r;
        }

        /// <summary>Rd - Rr - carry. With <paramref name="keepZ"/> (SBC/SBCI/CPC) Z only stays set.</summary>
        private byte Sub(byte rd, byte rr, bool carry, bool keepZ)
        {
            int diff = rd - rr - (carry ? 1 : 0);
            byte r = (byte)diff;
            _h = ((rd & 0xF) - (rr & 0xF) - (carry ? 1 : 0)) < 0;
            _c = diff < 0;
            _v = (((rd ^ rr) & (rd ^ r)) & 0x80) != 0;
            _n = (r & 0x80) != 0;
            _z = keepZ ? (_z && r == 0) : r == 0;
            _s = _n ^ _v;
            return r;
        }

        private void StoreMul(int result)
        {
            result &= 0xFFFF;
            _c = (result & 0x8000) != 0;
            _z = result == 0;
            _d[0] = (byte)result;
            _d[1] = (byte)(result >> 8);
        }
    }
}
