using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The two I²C controllers (I2C0 0x3FF53000, I2C1 0x3FF67000) as IDF's i2c_master driver (Arduino's Wire)
    /// programs them: a list of up to 16 commands (COMD0–15: RSTART, WRITE n bytes from the 32-byte TX FIFO with or without
    /// checking the ACK, READ n bytes into the RX FIFO answering each with ACK or NACK, STOP, END to pause for more), started
    /// by CTR's TRANS_START. The commands run against the board's bus (<see cref="I2cBuses"/>: the modules wired to it,
    /// byte by byte: the first byte after a START is the address), their results land at once, and the interrupt that ends
    /// them (TRANS_COMPLETE, END_DETECT, or ACK_ERR when a checked byte is not acknowledged) comes after the time the
    /// transfer takes on the wire at the SCL timing the driver set (9 clocks a byte). Sources 49 and 50.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private static readonly uint[] I2cBase = { 0x3FF53000, 0x3FF67000 };
        private const int I2cFifoSize = 32;
        private const uint I2cIntEndDetect = 1u << 3, I2cIntTransComplete = 1u << 7, I2cIntAckErr = 1u << 10;

        /// <summary>The buses the two controllers drive (Wire on I2C0, Wire1 on I2C1); null: nothing wired.</summary>
        public I2cBus?[] I2cBuses { get; } = new I2cBus?[2];

        private readonly I2cController[] _i2c = { new I2cController(), new I2cController() };

        private sealed class I2cController
        {
            public readonly uint[] Commands = new uint[16];
            public readonly Queue<byte> Tx = new Queue<byte>(), Rx = new Queue<byte>();
            public uint IntRaw, IntEnable, Status;
            public bool AddressNext, Addressed;

            /// <summary>When the running transfer's ending interrupt comes (NaN: none pending), and which bits it raises.</summary>
            public double DoneAt = double.NaN;
            public uint DoneBits;
        }

        private int I2cIndex(uint register) => register >= I2cBase[0] && register < I2cBase[0] + 0x180 ? 0
            : register >= I2cBase[1] && register < I2cBase[1] + 0x180 ? 1 : -1;

        private bool ReadI2cRegister(uint register, out uint value)
        {
            value = 0;
            int n = I2cIndex(register);
            if (n < 0) return false;
            var c = _i2c[n];
            uint offset = register - I2cBase[n];
            switch (offset)
            {
                case 0x08:
                    // SR: ACK_REC, BUS_BUSY while a transfer runs, the FIFOs' counts
                    value = c.Status | (double.IsNaN(c.DoneAt) ? 0 : 1u << 4) | (uint)Math.Min(c.Rx.Count, 63) << 8 | (uint)Math.Min(c.Tx.Count, 63) << 18;
                    return true;
                case 0x1C:
                    value = c.Rx.Count > 0 ? c.Rx.Dequeue() : 0u;   // DATA: the RX FIFO
                    return true;
                case 0x20: value = c.IntRaw; return true;
                case 0x28: value = c.IntEnable; return true;
                case 0x2C: value = c.IntRaw & c.IntEnable; return true;
            }

            if (offset >= 0x58 && offset < 0x98)
            {
                value = c.Commands[(offset - 0x58) / 4];
                return true;
            }

            value = Reg(register);   // timing and configuration: as written
            return true;
        }

        private bool WriteI2cRegister(uint register, uint value)
        {
            int n = I2cIndex(register);
            if (n < 0) return false;
            var c = _i2c[n];
            uint offset = register - I2cBase[n];
            switch (offset)
            {
                case 0x04:
                    if ((value & (1u << 5)) != 0) StartI2c(n);   // CTR: TRANS_START
                    return true;
                case 0x18:
                    if ((value & (1u << 12)) != 0) c.Rx.Clear();   // FIFO_CONF: RX_FIFO_RST
                    if ((value & (1u << 13)) != 0) c.Tx.Clear();   // TX_FIFO_RST
                    return true;
                case 0x1C:
                    if (c.Tx.Count < I2cFifoSize) c.Tx.Enqueue((byte)value);
                    return true;
                case 0x24:
                    c.IntRaw &= ~value;   // INT_CLR
                    break;
                case 0x28:
                    c.IntEnable = value;
                    break;
                default:
                    if (offset >= 0x58 && offset < 0x98) c.Commands[(offset - 0x58) / 4] = value;
                    return true;
            }

            UpdateI2cLine(n);
            return true;
        }

        /// <summary>TRANS_START: the command list runs from COMD0 until STOP, END or a NACK the command checks for.</summary>
        private void StartI2c(int n)
        {
            var c = _i2c[n];
            var bus = I2cBuses[n];
            int bits = 0;
            uint done = I2cIntTransComplete;
            for (int i = 0; i < 16; i++)
            {
                uint cmd = c.Commands[i];
                int op = (int)((cmd >> 11) & 7), count = (int)(cmd & 0xFF);
                bool checkAck = (cmd & (1u << 8)) != 0, expect = (cmd & (1u << 9)) != 0, nackRead = (cmd & (1u << 10)) != 0;
                c.Commands[i] = cmd | 1u << 31;   // COMMAND_DONE
                if (op == 0)
                {
                    // RSTART: (repeated) START; the next byte written is the address
                    bits += 1;
                    c.AddressNext = true;
                }
                else if (op == 1)
                {
                    bool failed = false;
                    for (int b = 0; b < count; b++)
                    {
                        byte value = c.Tx.Count > 0 ? c.Tx.Dequeue() : (byte)0xFF;
                        bool ack;
                        if (c.AddressNext)
                        {
                            ack = bus != null && bus.BeginTransfer((byte)(value >> 1), (value & 1) != 0);
                            c.AddressNext = false;
                            c.Addressed = ack;
                        }
                        else
                        {
                            ack = bus != null && c.Addressed && bus.WriteByte(value);
                        }

                        bits += 9;
                        c.Status = (c.Status & ~1u) | (ack ? 0u : 1u);   // SR.ACK_REC: the level seen (0 = ACK)
                        if (checkAck && (ack ? 0 : 1) != (expect ? 1 : 0))
                        {
                            failed = true;
                            break;
                        }
                    }

                    if (failed)
                    {
                        // A checked byte not acknowledged: the controller stops there and lets go of the bus.
                        bus?.EndTransfer();
                        c.Addressed = false;
                        done = I2cIntAckErr;
                        break;
                    }
                }
                else if (op == 2)
                {
                    for (int b = 0; b < count; b++)
                    {
                        byte value = bus != null && c.Addressed ? bus.ReadByte(!nackRead) : (byte)0xFF;
                        if (c.Rx.Count < I2cFifoSize) c.Rx.Enqueue(value);
                        bits += 9;
                    }
                }
                else if (op == 3)
                {
                    bits += 1;
                    bus?.EndTransfer();
                    c.Addressed = false;
                    done = I2cIntTransComplete;
                    break;
                }
                else if (op == 4)
                {
                    done = I2cIntEndDetect;   // END: pause, the driver loads more commands and starts again
                    break;
                }
            }

            // The time it takes on the wire: SCL low + high periods of the APB clock per bit.
            double period = ((Reg(I2cBase[n] + 0x00) & 0x3FFF) + (Reg(I2cBase[n] + 0x38) & 0x3FFF)) / ApbHz;
            if (period <= 0) period = 10e-6;
            c.DoneAt = Seconds + Math.Max(1, bits) * period;
            c.DoneBits = done;
        }

        /// <summary>Each turn: a transfer whose time is up raises its ending interrupt.</summary>
        private void ServiceI2c()
        {
            double now = Seconds;
            for (int n = 0; n < 2; n++)
            {
                var c = _i2c[n];
                if (double.IsNaN(c.DoneAt) || now < c.DoneAt) continue;
                c.DoneAt = double.NaN;
                c.IntRaw |= c.DoneBits;
                UpdateI2cLine(n);
            }
        }

        private void UpdateI2cLine(int n) => SetPeripheralLevel(49 + n, (_i2c[n].IntRaw & _i2c[n].IntEnable) != 0);
    }
}
