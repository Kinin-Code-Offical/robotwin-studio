using System;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>
    /// TWI (I²C) master of the ATmega328P on a logical <see cref="I2cBus"/>.
    /// Every bus action takes real time: SCL period = 16 + 2·TWBR·4^TWPS CPU cycles,
    /// a (repeated) START takes one SCL period and an address/data byte nine. When the
    /// action is done TWINT is set and TWSR holds the AVR status code; with TWIE and the
    /// global interrupt flag set the TWI vector (24) is taken. Writing TWINT=1 clears the
    /// flag and launches the next action; STOP completes at once and clears TWSTO.
    /// Slave mode, arbitration and bus errors are not modelled (single master).
    /// </summary>
    public sealed partial class Atmega328P
    {
        private const int TWBR = 0xB8, TWAR = 0xBA, TWAMR = 0xBD;
        private const byte TwInt = 0x80, TwEa = 0x40, TwSta = 0x20, TwSto = 0x10, TwWc = 0x08, TwEn = 0x04, TwIe = 0x01;

        private enum TwiAction
        {
            None,
            Start,
            Address,
            Transmit,
            Receive,
        }

        private TwiAction _twiAction;
        private double _twiCycles;
        private byte _twiShift;
        private bool _twiMaster;

        /// <summary>The I²C bus wired to SDA (PC4/A4) and SCL (PC5/A5). Attach device models to <see cref="I2cBus.Devices"/>.</summary>
        public I2cBus I2c { get; } = new I2cBus();

        /// <summary>True while TWEN is set: PC4/PC5 belong to the TWI, not to PORTC.</summary>
        public bool TwiEnabled => (_d[TWCR] & TwEn) != 0;

        /// <summary>SCL frequency the firmware configured (TWBR/TWPS), in Hz.</summary>
        public double TwiClockHz => ClockHz / TwiSclCycles();

        private double TwiSclCycles() => 16.0 + 2.0 * _d[TWBR] * (1 << (2 * (_d[TWSR] & 3)));

        private int TwiStatus => _d[TWSR] & 0xF8;

        private void SetTwiStatus(int status) => _d[TWSR] = (byte)((status & 0xF8) | (_d[TWSR] & 0x03));

        private bool TwiOwnsPin(int pin) => (pin == 18 || pin == 19) && TwiEnabled;

        private void ResetTwi()
        {
            if (_twiMaster || _twiAction != TwiAction.None) I2c.EndTransfer();
            _twiAction = TwiAction.None;
            _twiCycles = 0;
            _twiMaster = false;
            _twiShift = 0;
            _d[TWSR] = 0xF8;
            _d[TWAR] = 0xFE;
            _d[TWDR] = 0xFF;
        }

        private void TwiWriteStatus(byte v)
        {
            // Status bits are read-only; only the prescaler (TWPS1:0) is writable.
            _d[TWSR] = (byte)((_d[TWSR] & 0xF8) | (v & 0x03));
        }

        private void TwiWriteData(byte v)
        {
            byte c = _d[TWCR];
            if ((c & TwEn) != 0 && (c & TwInt) == 0)
            {
                _d[TWCR] = (byte)(c | TwWc); // write collision: TWDR is only writable while TWINT is set
                return;
            }

            _d[TWDR] = v;
            _d[TWCR] = (byte)(c & ~TwWc);
        }

        private void TwiControl(byte v)
        {
            byte old = _d[TWCR];
            bool wasEnabled = (old & TwEn) != 0;
            bool clearFlag = (v & TwInt) != 0;
            bool flag = (old & TwInt) != 0 && !clearFlag;
            byte next = (byte)((v & (TwEa | TwSta | TwSto | TwEn | TwIe)) | (old & TwWc) | (flag ? TwInt : 0));
            _d[TWCR] = next;

            if ((next & TwEn) == 0)
            {
                if (wasEnabled) DisableTwi();
                return;
            }

            if (!wasEnabled) DetectPinChanges(); // PC4/PC5 handed over to the TWI

            // While TWINT is set the hardware is stalled (SCL held low); while an action is
            // running new control bits only take effect when it completes.
            if (flag || _twiAction != TwiAction.None) return;
            if (clearFlag || (!_twiMaster && (next & TwSta) != 0)) TwiLaunch();
        }

        private void DisableTwi()
        {
            // TWEN = 0 switches the TWI off and terminates any transmission at once.
            if (_twiMaster || _twiAction != TwiAction.None) I2c.EndTransfer();
            _twiAction = TwiAction.None;
            _twiCycles = 0;
            _twiMaster = false;
            SetTwiStatus(0xF8);
            _d[TWCR] &= unchecked((byte)~(TwInt | TwSto | TwWc));
            DetectPinChanges();
        }

        private void TwiLaunch()
        {
            byte c = _d[TWCR];
            double scl = TwiSclCycles();
            if ((c & TwSto) != 0)
            {
                // STOP: completes immediately, clears TWSTO, TWINT stays clear.
                if (_twiMaster) I2c.EndTransfer();
                _twiMaster = false;
                _d[TWCR] = (byte)(c & ~TwSto);
                SetTwiStatus(0xF8);
                if ((c & TwSta) == 0) return; // TWSTA with TWSTO: a START follows the STOP
            }

            if ((c & TwSta) != 0)
            {
                BeginTwiAction(TwiAction.Start, scl);
                return;
            }

            switch (TwiStatus)
            {
                case 0x08: // START / repeated START sent: TWDR holds SLA+R/W
                case 0x10:
                    _twiShift = _d[TWDR];
                    BeginTwiAction(TwiAction.Address, 9 * scl);
                    break;
                case 0x18: // master transmitter
                case 0x20:
                case 0x28:
                case 0x30:
                    _twiShift = _d[TWDR];
                    BeginTwiAction(TwiAction.Transmit, 9 * scl);
                    break;
                case 0x40: // master receiver
                case 0x48:
                case 0x50:
                case 0x58:
                    BeginTwiAction(TwiAction.Receive, 9 * scl);
                    break;
            }
        }

        private void BeginTwiAction(TwiAction action, double cycles)
        {
            _twiAction = action;
            _twiCycles = cycles;
        }

        private void AdvanceTwi(int cycles)
        {
            _twiCycles -= cycles;
            if (_twiCycles > 0) return;
            var action = _twiAction;
            _twiAction = TwiAction.None;
            _twiCycles = 0;
            int status;
            switch (action)
            {
                case TwiAction.Start:
                    status = _twiMaster ? 0x10 : 0x08;
                    _twiMaster = true;
                    break;
                case TwiAction.Address:
                {
                    bool read = (_twiShift & 1) != 0;
                    bool ack = I2c.BeginTransfer((byte)(_twiShift >> 1), read);
                    status = read ? (ack ? 0x40 : 0x48) : (ack ? 0x18 : 0x20);
                    break;
                }

                case TwiAction.Transmit:
                    status = I2c.WriteByte(_twiShift) ? 0x28 : 0x30;
                    break;
                case TwiAction.Receive:
                {
                    // The ACK the master returns is TWEA at the ninth clock.
                    bool ack = (_d[TWCR] & TwEa) != 0;
                    int previous = TwiStatus;
                    _d[TWDR] = previous == 0x48 || previous == 0x58 ? (byte)0xFF : I2c.ReadByte(ack);
                    status = ack ? 0x50 : 0x58;
                    break;
                }

                default:
                    return;
            }

            SetTwiStatus(status);
            _d[TWCR] |= TwInt;
        }
    }
}
