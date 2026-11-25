using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] UART0 (0x3FF40000) as IDF's driver uses it: what the firmware writes to the FIFO goes out at once (to
    /// <see cref="Uart0"/>), so the transmitter is always empty; what the world sends arrives one byte at a time at the
    /// UART's baud rate (ten bit-times each) into the 128-byte receive FIFO, which the firmware reads byte by byte. The
    /// FIFO's count (STATUS), its read and write pointers (MEM_RX_STATUS, which the driver uses on the ESP32 instead of the
    /// count), and the interrupts the driver runs on (RXFIFO_FULL at CONF1's threshold, RXFIFO_TOUT after the line has
    /// been quiet for the timeout, RXFIFO_OVF, TXFIFO_EMPTY) on source 34.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const int Uart0InterruptSource = 34;
        private const int Uart0FifoSize = 128;
        private const uint IntRxFull = 1u << 0, IntTxEmpty = 1u << 1, IntRxOverflow = 1u << 4, IntRxTimeout = 1u << 8;

        private readonly Queue<(byte Value, double At)> _uart0Line = new Queue<(byte, double)>();
        private readonly Queue<byte> _uart0Rx = new Queue<byte>();
        private uint _uart0RxWritten, _uart0RxRead, _uart0IntRaw, _uart0IntEnable;
        private double _uart0LastByte = double.NaN, _uart0LineFree;
        private bool _uart0TimeoutFired;

        /// <summary>Sends bytes to UART0's receive line: they arrive one after another at the UART's baud rate (115200 until the firmware sets one).</summary>
        public void SendToUart0(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            double baud = Uart0Baud > 0 ? Uart0Baud : 115200;
            double t = Math.Max(Seconds, _uart0LineFree);
            foreach (byte b in bytes)
            {
                t += 10 / baud;
                _uart0Line.Enqueue((b, t));
            }

            _uart0LineFree = t;
        }

        /// <summary>Bytes still on their way, or waiting in the receive FIFO for the firmware.</summary>
        public int Uart0Pending => _uart0Line.Count + _uart0Rx.Count;

        /// <summary>Each turn: the bytes whose stop bit has passed enter the FIFO; the interrupt conditions are brought up to date.</summary>
        private void ServiceUart0()
        {
            double now = Seconds;
            while (_uart0Line.Count > 0 && _uart0Line.Peek().At <= now)
            {
                var (value, at) = _uart0Line.Dequeue();
                if (_uart0Rx.Count < Uart0FifoSize)
                {
                    _uart0Rx.Enqueue(value);
                    _uart0RxWritten++;
                }
                else
                {
                    _uart0IntRaw |= IntRxOverflow;
                }

                _uart0LastByte = at;
                _uart0TimeoutFired = false;
            }

            UpdateUart0(now);
        }

        private void UpdateUart0(double now)
        {
            uint conf1 = Reg(Uart0Fifo + 0x24);
            uint fullThreshold = conf1 & 0x7F, emptyThreshold = (conf1 >> 8) & 0x7F;
            if (_uart0Rx.Count > 0 && _uart0Rx.Count >= Math.Max(1u, fullThreshold)) _uart0IntRaw |= IntRxFull;
            else _uart0IntRaw &= ~IntRxFull;
            if (emptyThreshold > 0) _uart0IntRaw |= IntTxEmpty;   // the transmitter is always empty: count 0 is below any threshold
            else _uart0IntRaw &= ~IntTxEmpty;
            // RX timeout: data waits and the line has been quiet for RX_TOUT_THRHD bit-times (once per quiet spell).
            bool timeoutOn = (conf1 & (1u << 31)) != 0;
            double bitTime = 1 / (Uart0Baud > 0 ? Uart0Baud : 115200);
            uint timeout = Math.Max(1u, (conf1 >> 24) & 0x7F);
            if (timeoutOn && !_uart0TimeoutFired && _uart0Rx.Count > 0 && !double.IsNaN(_uart0LastByte) && now - _uart0LastByte >= timeout * bitTime)
            {
                _uart0IntRaw |= IntRxTimeout;
                _uart0TimeoutFired = true;
            }

            SetPeripheralLevel(Uart0InterruptSource, (_uart0IntRaw & _uart0IntEnable) != 0);
        }

        private bool ReadUartRegister(uint register, out uint value)
        {
            value = 0;
            if (register < Uart0Fifo || register >= Uart0Fifo + 0x80) return false;
            switch (register - Uart0Fifo)
            {
                case 0x00:
                    // RW_BYTE: the next received byte
                    if (_uart0Rx.Count > 0)
                    {
                        value = _uart0Rx.Dequeue();
                        _uart0RxRead++;
                        UpdateUart0(Seconds);
                    }

                    return true;
                case 0x04: value = _uart0IntRaw; return true;
                case 0x08: value = _uart0IntRaw & _uart0IntEnable; return true;
                case 0x0C: value = _uart0IntEnable; return true;
                case 0x1C:
                    // STATUS: RXFIFO_CNT; the transmitter idle and empty
                    value = (uint)_uart0Rx.Count & 0xFF;
                    return true;
                case 0x60:
                    // MEM_RX_STATUS: read and write pointers in the receive RAM (0x200…0x27F), which wrap together
                    value = (0x200 + _uart0RxRead % Uart0FifoSize) << 2 | (0x200 + _uart0RxWritten % Uart0FifoSize) << 13;
                    return true;
                case 0x5C:
                    value = 0;   // MEM_TX_STATUS: nothing waiting to go out
                    return true;
            }

            value = Reg(register);   // configuration (CONF0/1, CLKDIV …): as written
            return true;
        }

        private bool WriteUartRegister(uint register, uint value)
        {
            if (register < Uart0Fifo || register >= Uart0Fifo + 0x80) return false;
            switch (register - Uart0Fifo)
            {
                case 0x00:
                    Uart0Out((byte)value);
                    return true;
                case 0x0C:
                    _uart0IntEnable = value;
                    break;
                case 0x10:
                    _uart0IntRaw &= ~value;   // INT_CLR (conditions still true come back)
                    break;
                case 0x24:
                    break;   // CONF1: thresholds and timeout (the register file keeps it; read below)
                default:
                    return true;   // the rest of the configuration: kept as written
            }

            _registers[register] = value;
            UpdateUart0(Seconds);
            return true;
        }
    }
}
