using System;
using System.Collections.Generic;
using System.Text;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The three UARTs (UART0 0x3FF40000 — Serial, UART1 0x3FF50000 — Serial1, UART2 0x3FF6E000 — Serial2) as
    /// IDF's driver uses them: what the firmware writes to a FIFO goes out at once (as UTF-8 text: <see cref="Uart0"/>,
    /// <see cref="UartText"/>), so the transmitter is always empty; what the world sends arrives one byte at a time at the
    /// UART's baud rate (ten bit-times each) into its 128-byte receive FIFO, which the firmware reads byte by byte. The
    /// FIFO's count (STATUS), its read and write pointers (MEM_RX_STATUS, which the driver uses on the ESP32 instead of the
    /// count), and the interrupts the driver runs on (RXFIFO_FULL at CONF1's threshold, RXFIFO_TOUT after the line has
    /// been quiet for the timeout, RXFIFO_OVF, TXFIFO_EMPTY) on sources 34, 35 and 36.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private static readonly uint[] UartBase = { 0x3FF40000, 0x3FF50000, 0x3FF6E000 };
        private const int UartFifoSize = 128;
        private const uint IntRxFull = 1u << 0, IntTxEmpty = 1u << 1, IntRxOverflow = 1u << 4, IntRxTimeout = 1u << 8;

        private sealed class UartPort
        {
            public UartPort(int index, StringBuilder text)
            {
                Index = index;
                Text = text;
            }

            public readonly int Index;
            public readonly StringBuilder Text;
            public readonly Decoder Decoder = Encoding.UTF8.GetDecoder();
            public readonly Queue<(byte Value, double At)> Line = new Queue<(byte, double)>();
            public readonly Queue<byte> Rx = new Queue<byte>();
            public uint RxWritten, RxRead, IntRaw, IntEnable;
            public double LastByte = double.NaN, LineFree;
            public bool TimeoutFired;
        }

        private UartPort[]? _uarts;

        private UartPort[] Uarts => _uarts ??= new[] { new UartPort(0, Uart0), new UartPort(1, new StringBuilder()), new UartPort(2, new StringBuilder()) };

        /// <summary>What the firmware wrote to UART <paramref name="port"/> (0 is <see cref="Uart0"/>: Serial; 1, 2: Serial1, Serial2).</summary>
        public StringBuilder UartText(int port) => Uarts[Math.Clamp(port, 0, 2)].Text;

        /// <summary>Sends bytes to UART0's receive line.</summary>
        public void SendToUart0(byte[] bytes) => SendToUart(0, bytes);

        /// <summary>Sends bytes to a UART's receive line: they arrive one after another at its baud rate (115200 until the firmware sets one).</summary>
        public void SendToUart(int port, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || port < 0 || port > 2) return;
            var u = Uarts[port];
            double baud = UartBaud(port) > 0 ? UartBaud(port) : 115200;
            double t = Math.Max(Seconds, u.LineFree);
            foreach (byte b in bytes)
            {
                t += 10 / baud;
                u.Line.Enqueue((b, t));
            }

            u.LineFree = t;
        }

        /// <summary>Bytes still on their way to UART0, or waiting in its receive FIFO for the firmware.</summary>
        public int Uart0Pending => Uarts[0].Line.Count + Uarts[0].Rx.Count;

        /// <summary>One byte out of a UART, as UTF-8 text.</summary>
        private void UartOut(int port, byte value)
        {
            var u = Uarts[port];
            _uartByte[0] = value;
            int n = u.Decoder.GetChars(_uartByte, 0, 1, _uartChars, 0);
            u.Text.Append(_uartChars, 0, n);
        }

        private readonly byte[] _uartByte = new byte[1];
        private readonly char[] _uartChars = new char[4];

        /// <summary>One byte out of UART0 (the ROM's putc).</summary>
        private void Uart0Out(byte value) => UartOut(0, value);

        /// <summary>Text the ROM's printf made from the firmware's bytes (one character per byte), out of UART0.</summary>
        private void Uart0Out(string bytesAsChars)
        {
            foreach (char ch in bytesAsChars) UartOut(0, (byte)ch);
        }

        /// <summary>Each turn: the bytes whose stop bit has passed enter the FIFOs; the interrupt conditions are brought up to date.</summary>
        private void ServiceUarts()
        {
            if (_uarts == null) return;
            double now = Seconds;
            foreach (var u in _uarts)
            {
                while (u.Line.Count > 0 && u.Line.Peek().At <= now)
                {
                    var (value, at) = u.Line.Dequeue();
                    if (u.Rx.Count < UartFifoSize)
                    {
                        u.Rx.Enqueue(value);
                        u.RxWritten++;
                    }
                    else
                    {
                        u.IntRaw |= IntRxOverflow;
                    }

                    u.LastByte = at;
                    u.TimeoutFired = false;
                }

                UpdateUart(u, now);
            }
        }

        private void UpdateUart(UartPort u, double now)
        {
            uint conf1 = Reg(UartBase[u.Index] + 0x24);
            uint fullThreshold = conf1 & 0x7F, emptyThreshold = (conf1 >> 8) & 0x7F;
            if (u.Rx.Count > 0 && u.Rx.Count >= Math.Max(1u, fullThreshold)) u.IntRaw |= IntRxFull;
            else u.IntRaw &= ~IntRxFull;
            if (emptyThreshold > 0) u.IntRaw |= IntTxEmpty;   // the transmitter is always empty: count 0 is below any threshold
            else u.IntRaw &= ~IntTxEmpty;
            // RX timeout: data waits and the line has been quiet for RX_TOUT_THRHD bit-times (once per quiet spell).
            bool timeoutOn = (conf1 & (1u << 31)) != 0;
            double baud = UartBaud(u.Index);
            double bitTime = 1 / (baud > 0 ? baud : 115200);
            uint timeout = Math.Max(1u, (conf1 >> 24) & 0x7F);
            if (timeoutOn && !u.TimeoutFired && u.Rx.Count > 0 && !double.IsNaN(u.LastByte) && now - u.LastByte >= timeout * bitTime)
            {
                u.IntRaw |= IntRxTimeout;
                u.TimeoutFired = true;
            }

            SetPeripheralLevel(34 + u.Index, (u.IntRaw & u.IntEnable) != 0);
        }

        private int UartIndex(uint register)
        {
            for (int i = 0; i < 3; i++)
                if (register >= UartBase[i] && register < UartBase[i] + 0x80) return i;
            return -1;
        }

        private bool ReadUartRegister(uint register, out uint value)
        {
            value = 0;
            int port = UartIndex(register);
            if (port < 0) return false;
            var u = Uarts[port];
            switch (register - UartBase[port])
            {
                case 0x00:
                    // RW_BYTE: the next received byte
                    if (u.Rx.Count > 0)
                    {
                        value = u.Rx.Dequeue();
                        u.RxRead++;
                        UpdateUart(u, Seconds);
                    }

                    return true;
                case 0x04: value = u.IntRaw; return true;
                case 0x08: value = u.IntRaw & u.IntEnable; return true;
                case 0x0C: value = u.IntEnable; return true;
                case 0x1C:
                    // STATUS: RXFIFO_CNT; the transmitter idle and empty
                    value = (uint)u.Rx.Count & 0xFF;
                    return true;
                case 0x60:
                    // MEM_RX_STATUS: read and write pointers in the UART's receive RAM, which wrap together
                    uint ram = 0x200 + 0x80 * (uint)port;
                    value = (ram + u.RxRead % UartFifoSize) << 2 | (ram + u.RxWritten % UartFifoSize) << 13;
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
            int port = UartIndex(register);
            if (port < 0) return false;
            var u = Uarts[port];
            switch (register - UartBase[port])
            {
                case 0x00:
                    UartOut(port, (byte)value);
                    return true;
                case 0x0C:
                    u.IntEnable = value;
                    break;
                case 0x10:
                    u.IntRaw &= ~value;   // INT_CLR (conditions still true come back)
                    break;
                case 0x24:
                    break;   // CONF1: thresholds and timeout (the register file keeps it; read below)
                default:
                    return true;   // the rest of the configuration: kept as written
            }

            _registers[register] = value;
            UpdateUart(u, Seconds);
            return true;
        }
    }
}
