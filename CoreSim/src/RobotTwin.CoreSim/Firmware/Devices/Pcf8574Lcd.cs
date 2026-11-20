using System;
using System.Text;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    /// <summary>
    /// HD44780 character LCD (16×2, 20×4, …) behind a PCF8574 "I2C backpack" with the
    /// common wiring P0=RS, P1=RW, P2=EN, P3=backlight, P4-P7=D4-D7. The HD44780 latches
    /// on the falling edge of EN; it powers up in 8-bit mode (DB0-DB3 are not wired and
    /// read as 0) and pairs nibbles (high first) once switched to 4-bit mode. Reads (RW=1)
    /// drive the busy flag/address counter or RAM data onto D4-D7 while EN is high, which
    /// a PCF8574 read returns together with the rest of the latch. The busy flag is always
    /// 0: instruction execution times are not modelled.
    /// </summary>
    public sealed class Pcf8574Lcd : Pcf8574
    {
        private const byte Rs = 0x01, Rw = 0x02, En = 0x04, Bl = 0x08;

        private readonly byte[] _ddram = new byte[0x80];
        private readonly byte[] _cgram = new byte[64];

        private bool _eightBit;
        private bool _secondNibble;
        private byte _pendingHigh;
        private bool _twoLine;
        private bool _font5x10;
        private bool _displayOn;
        private bool _cursorOn;
        private bool _blinkOn;
        private bool _increment;
        private bool _shiftOnWrite;
        private int _ac;
        private bool _acInCgram;
        private int _shift;
        private byte _readValue;
        private byte _lcdDrive = 0xFF;

        public Pcf8574Lcd(int columns = 16, int rows = 2, byte address = 0x27)
            : base(address)
        {
            if (rows != 1 && rows != 2 && rows != 4) throw new ArgumentOutOfRangeException(nameof(rows), "HD44780 modules have 1, 2 or 4 rows.");
            if (columns < 8 || columns > 40 || (rows == 4 && columns > 20)) throw new ArgumentOutOfRangeException(nameof(columns));
            Columns = columns;
            Rows = rows;
            ResetController();
        }

        public int Columns { get; }
        public int Rows { get; }

        public bool Backlight => (Port & Bl) != 0;
        public bool DisplayOn => _displayOn;
        public bool CursorOn => _cursorOn;
        public bool BlinkOn => _blinkOn;
        public bool FourBitMode => !_eightBit;
        public bool TwoLineMode => _twoLine;
        public bool Font5x10 => _font5x10;
        public bool EntryIncrement => _increment;
        public bool EntryShift => _shiftOnWrite;

        /// <summary>Address counter (DDRAM address, or CGRAM address when <see cref="AddressInCgram"/>).</summary>
        public int CursorAddress => _ac;

        public bool AddressInCgram => _acInCgram;

        /// <summary>Display shift in characters (positive = content moved left).</summary>
        public int DisplayShift => _shift;

        /// <summary>Character generator RAM: 8 user characters × 8 rows, bits 4..0 = pixels left to right.</summary>
        public byte[] Cgram => _cgram;

        /// <summary>Display data RAM indexed by DDRAM address (0x00-0x27 / 0x40-0x67 in 2-line mode).</summary>
        public byte[] Ddram => _ddram;

        public override void Reset()
        {
            base.Reset();
            ResetController();
        }

        /// <summary>HD44780 internal reset: 8-bit, 1 line, 5×8, display/cursor/blink off, increment, cleared.</summary>
        private void ResetController()
        {
            for (int i = 0; i < _ddram.Length; i++) _ddram[i] = 0x20;
            Array.Clear(_cgram, 0, _cgram.Length);
            _eightBit = true;
            _secondNibble = false;
            _pendingHigh = 0;
            _twoLine = false;
            _font5x10 = false;
            _displayOn = false;
            _cursorOn = false;
            _blinkOn = false;
            _increment = true;
            _shiftOnWrite = false;
            _ac = 0;
            _acInCgram = false;
            _shift = 0;
            _readValue = 0;
            _lcdDrive = 0xFF;
            Touch();
        }

        // ================================================================ what the glass shows

        /// <summary>Character code shown at (row, col), regardless of <see cref="DisplayOn"/>.</summary>
        public byte CodeAt(int row, int col)
        {
            if (row < 0 || row >= Rows || col < 0 || col >= Columns) throw new ArgumentOutOfRangeException(row < 0 || row >= Rows ? nameof(row) : nameof(col));
            int address = AddressAt(row, col);
            return address < 0 ? (byte)0x20 : _ddram[address];
        }

        /// <summary>
        /// Character shown at (row, col) as Unicode (A00 ROM: 0x5C = '¥', 0x7E/0x7F = arrows,
        /// katakana/Greek in the upper half). User characters (CGRAM, codes 0-15) come back as
        /// (char)0-(char)7.
        /// </summary>
        public char CharAt(int row, int col) => Hd44780Font.ToChar(CodeAt(row, col));

        /// <summary>The <see cref="Columns"/> characters of a row, as <see cref="CharAt"/> maps them.</summary>
        public string Line(int row)
        {
            var sb = new StringBuilder(Columns);
            for (int c = 0; c < Columns; c++) sb.Append(CharAt(row, c));
            return sb.ToString();
        }

        /// <summary>5×8 glyph rows of a character code, using this display's CGRAM for codes 0-15.</summary>
        public byte[] Glyph(byte code) => Hd44780Font.Glyph(code, _cgram);

        /// <summary>Visible cell under the cursor (address counter), if it is on screen.</summary>
        public bool TryGetCursorCell(out int row, out int col)
        {
            if (!_acInCgram)
            {
                for (row = 0; row < Rows; row++)
                {
                    for (col = 0; col < Columns; col++)
                    {
                        if (AddressAt(row, col) == _ac) return true;
                    }
                }
            }

            row = col = -1;
            return false;
        }

        private int AddressAt(int row, int col)
        {
            if (!_twoLine)
            {
                // 1-line mode: one 80-character line, only the first COM line is driven.
                if ((row & 1) != 0) return -1;
                return Mod((row >> 1) * Columns + col + _shift, 80);
            }

            int lineBase = (row & 1) != 0 ? 0x40 : 0x00;
            return lineBase + Mod((row >> 1) * Columns + col + _shift, 40);
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        // ================================================================ PCF8574 → HD44780 bus

        protected override byte DeviceDrive() => _lcdDrive;

        protected override void OnPortChanged(byte previous, byte current)
        {
            if (((previous ^ current) & Bl) != 0) Touch();
            bool enWas = (previous & En) != 0, enNow = (current & En) != 0;
            if (!enWas && enNow) EnableRising(current);
            else if (enWas && !enNow) EnableFalling(previous);
            else if (enNow && (current & Rw) == 0) _lcdDrive = 0xFF;
        }

        private void EnableRising(byte port)
        {
            _lcdDrive = 0xFF;
            if ((port & Rw) == 0) return;
            bool first = _eightBit || !_secondNibble;
            if (first)
            {
                _readValue = (port & Rs) != 0
                    ? (_acInCgram ? _cgram[_ac & 0x3F] : _ddram[_ac & 0x7F])
                    : (byte)(_ac & 0x7F); // busy flag (bit 7) never set
            }

            int nibble = _eightBit || !_secondNibble ? _readValue >> 4 : _readValue & 0x0F;
            _lcdDrive = (byte)((nibble << 4) | 0x0F);
        }

        private void EnableFalling(byte port)
        {
            _lcdDrive = 0xFF;
            bool rs = (port & Rs) != 0, rw = (port & Rw) != 0;
            byte high = (byte)(port & 0xF0);
            if (_eightBit)
            {
                if (rw) FinishRead(rs);
                else Execute(rs, high);
                return;
            }

            if (!_secondNibble)
            {
                _pendingHigh = high;
                _secondNibble = true;
                return;
            }

            _secondNibble = false;
            if (rw) FinishRead(rs);
            else Execute(rs, (byte)(_pendingHigh | (high >> 4)));
        }

        private void FinishRead(bool data)
        {
            if (data) MoveAddress(_increment ? 1 : -1);
        }

        // ================================================================ HD44780 instruction set

        private void Execute(bool data, byte value)
        {
            if (data)
            {
                WriteData(value);
                return;
            }

            if ((value & 0x80) != 0)
            {
                _ac = value & 0x7F;
                _acInCgram = false;
                Touch();
            }
            else if ((value & 0x40) != 0)
            {
                _ac = value & 0x3F;
                _acInCgram = true;
            }
            else if ((value & 0x20) != 0)
            {
                bool eightBit = (value & 0x10) != 0;
                if (eightBit != _eightBit) _secondNibble = false;
                _eightBit = eightBit;
                _twoLine = (value & 0x08) != 0;
                _font5x10 = (value & 0x04) != 0;
                Touch();
            }
            else if ((value & 0x10) != 0)
            {
                bool right = (value & 0x04) != 0;
                if ((value & 0x08) != 0) ShiftDisplay(right ? -1 : 1);
                else MoveAddress(right ? 1 : -1);
                Touch();
            }
            else if ((value & 0x08) != 0)
            {
                _displayOn = (value & 0x04) != 0;
                _cursorOn = (value & 0x02) != 0;
                _blinkOn = (value & 0x01) != 0;
                Touch();
            }
            else if ((value & 0x04) != 0)
            {
                _increment = (value & 0x02) != 0;
                _shiftOnWrite = (value & 0x01) != 0;
            }
            else if ((value & 0x02) != 0)
            {
                _ac = 0;
                _acInCgram = false;
                _shift = 0;
                Touch();
            }
            else if ((value & 0x01) != 0)
            {
                for (int i = 0; i < _ddram.Length; i++) _ddram[i] = 0x20;
                _ac = 0;
                _acInCgram = false;
                _shift = 0;
                _increment = true;
                Touch();
            }
        }

        private void WriteData(byte value)
        {
            if (_acInCgram) _cgram[_ac & 0x3F] = value;
            else _ddram[_ac & 0x7F] = value;
            MoveAddress(_increment ? 1 : -1);
            if (_shiftOnWrite && !_acInCgram) ShiftDisplay(_increment ? 1 : -1);
            Touch();
        }

        private void ShiftDisplay(int delta)
        {
            int length = _twoLine ? 40 : 80;
            _shift = Mod(_shift + delta, length);
        }

        private void MoveAddress(int delta)
        {
            if (_acInCgram)
            {
                _ac = (_ac + delta) & 0x3F;
                return;
            }

            if (!_twoLine)
            {
                _ac = Mod((_ac & 0x7F) + delta, 80);
                return;
            }

            // 2-line mode: 0x00-0x27 and 0x40-0x67, the end of each line runs into the other.
            if (delta > 0)
            {
                _ac = _ac == 0x27 ? 0x40 : _ac >= 0x67 ? 0x00 : _ac + 1;
            }
            else
            {
                _ac = _ac == 0x40 ? 0x27 : _ac == 0x00 ? 0x67 : _ac - 1;
            }
        }
    }
}
