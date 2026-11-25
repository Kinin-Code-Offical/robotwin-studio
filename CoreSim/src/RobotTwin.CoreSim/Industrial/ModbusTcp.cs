using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.Industrial
{
    /// <summary>
    /// [modbus] One Modbus device's data table: coils (bits a client writes), discrete inputs (bits it reads), holding
    /// registers (words it writes) and input registers (words it reads). The simulation and the network share it under
    /// <see cref="Gate"/>.
    /// </summary>
    public sealed class ModbusImage
    {
        public ModbusImage(int coils = 16, int discreteInputs = 16, int holdingRegisters = 16, int inputRegisters = 16)
        {
            Coils = new bool[Math.Max(0, coils)];
            DiscreteInputs = new bool[Math.Max(0, discreteInputs)];
            HoldingRegisters = new ushort[Math.Max(0, holdingRegisters)];
            InputRegisters = new ushort[Math.Max(0, inputRegisters)];
        }

        public object Gate { get; } = new object();

        public bool[] Coils { get; }

        public bool[] DiscreteInputs { get; }

        public ushort[] HoldingRegisters { get; }

        public ushort[] InputRegisters { get; }

        /// <summary>Requests answered and writes taken (for the panel).</summary>
        public long Requests { get; internal set; }

        public long Writes { get; internal set; }
    }

    /// <summary>
    /// [modbus] A Modbus TCP server (MBAP framing over TCP, function codes 1–6, 15, 16 and 23) serving one or more
    /// <see cref="ModbusImage"/>s by unit id, so a PLC, an HMI or a SCADA program talks to the simulated devices as to real
    /// ones. It listens on the loopback address unless told otherwise. <see cref="Answer"/> is the protocol itself, without
    /// the network.
    /// </summary>
    public sealed class ModbusTcpServer : IDisposable
    {
        public const byte IllegalFunction = 1, IllegalDataAddress = 2, IllegalDataValue = 3, GatewayTargetFailed = 0x0B;

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private int _connected;

        public ModbusTcpServer(IPAddress address, int port)
        {
            _listener = new TcpListener(address, port);
        }

        /// <summary>The devices by unit id. A request to unit 0 or 255 (or to any unit when there is one) goes to the first.</summary>
        public Dictionary<byte, ModbusImage> Units { get; } = new Dictionary<byte, ModbusImage>();

        public int Port { get; private set; }

        public int Clients => Volatile.Read(ref _connected);

        public string? Error { get; private set; }

        public void Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException || ex is InvalidOperationException)
                {
                    if (!_stop.IsCancellationRequested) Error = ex.Message;
                    return;
                }

                lock (_clients) _clients.Add(client);
                _ = Task.Run(() => Serve(client));
            }
        }

        private async Task Serve(TcpClient client)
        {
            Interlocked.Increment(ref _connected);
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var header = new byte[7];
                while (!_stop.IsCancellationRequested)
                {
                    if (!await Fill(stream, header).ConfigureAwait(false)) break;
                    int length = header[4] << 8 | header[5];   // unit id + PDU
                    if (header[2] != 0 || header[3] != 0 || length < 2 || length > 254) break;   // not Modbus: drop the connection
                    var pdu = new byte[length - 1];
                    if (!await Fill(stream, pdu).ConfigureAwait(false)) break;
                    byte unit = header[6];
                    var reply = Answer(Image(unit), pdu);
                    var frame = new byte[7 + reply.Length];
                    frame[0] = header[0];
                    frame[1] = header[1];
                    frame[4] = (byte)((reply.Length + 1) >> 8);
                    frame[5] = (byte)(reply.Length + 1);
                    frame[6] = unit;
                    Buffer.BlockCopy(reply, 0, frame, 7, reply.Length);
                    await stream.WriteAsync(frame, 0, frame.Length, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Decrement(ref _connected);
                lock (_clients) _clients.Remove(client);
                client.Dispose();
            }
        }

        private async Task<bool> Fill(NetworkStream stream, byte[] buffer)
        {
            int got = 0;
            while (got < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer, got, buffer.Length - got, _stop.Token).ConfigureAwait(false);
                if (n == 0) return false;
                got += n;
            }

            return true;
        }

        private ModbusImage? Image(byte unit)
        {
            lock (Units)
            {
                if (Units.TryGetValue(unit, out var image)) return image;
                if (unit == 0 || unit == 255 || Units.Count == 1) return Units.Values.FirstOrDefault();
                return null;
            }
        }

        /// <summary>Answers one request PDU (function code and data) from <paramref name="image"/>: the reply PDU, or an exception.</summary>
        public static byte[] Answer(ModbusImage? image, byte[] pdu)
        {
            if (pdu.Length == 0) return new byte[] { 0x80, IllegalFunction };
            byte fc = pdu[0];
            if (image == null) return Fail(fc, GatewayTargetFailed);
            lock (image.Gate)
            {
                image.Requests++;
                switch (fc)
                {
                    case 1:
                        return ReadBits(fc, pdu, image.Coils);
                    case 2:
                        return ReadBits(fc, pdu, image.DiscreteInputs);
                    case 3:
                        return ReadWords(fc, pdu, image.HoldingRegisters);
                    case 4:
                        return ReadWords(fc, pdu, image.InputRegisters);
                    case 5:
                    {
                        if (pdu.Length != 5) return Fail(fc, IllegalDataValue);
                        int at = Word(pdu, 1), value = Word(pdu, 3);
                        if (value != 0xFF00 && value != 0) return Fail(fc, IllegalDataValue);
                        if (at >= image.Coils.Length) return Fail(fc, IllegalDataAddress);
                        image.Coils[at] = value == 0xFF00;
                        image.Writes++;
                        return (byte[])pdu.Clone();
                    }

                    case 6:
                    {
                        if (pdu.Length != 5) return Fail(fc, IllegalDataValue);
                        int at = Word(pdu, 1);
                        if (at >= image.HoldingRegisters.Length) return Fail(fc, IllegalDataAddress);
                        image.HoldingRegisters[at] = (ushort)Word(pdu, 3);
                        image.Writes++;
                        return (byte[])pdu.Clone();
                    }

                    case 15:
                    {
                        if (pdu.Length < 6) return Fail(fc, IllegalDataValue);
                        int at = Word(pdu, 1), count = Word(pdu, 3), bytes = pdu[5];
                        if (count < 1 || count > 0x7B0 || bytes != (count + 7) / 8 || pdu.Length != 6 + bytes) return Fail(fc, IllegalDataValue);
                        if (at + count > image.Coils.Length) return Fail(fc, IllegalDataAddress);
                        for (int i = 0; i < count; i++) image.Coils[at + i] = (pdu[6 + i / 8] >> (i % 8) & 1) != 0;
                        image.Writes++;
                        return new[] { fc, pdu[1], pdu[2], pdu[3], pdu[4] };
                    }

                    case 16:
                    {
                        if (pdu.Length < 6) return Fail(fc, IllegalDataValue);
                        int at = Word(pdu, 1), count = Word(pdu, 3), bytes = pdu[5];
                        if (count < 1 || count > 123 || bytes != count * 2 || pdu.Length != 6 + bytes) return Fail(fc, IllegalDataValue);
                        if (at + count > image.HoldingRegisters.Length) return Fail(fc, IllegalDataAddress);
                        for (int i = 0; i < count; i++) image.HoldingRegisters[at + i] = (ushort)Word(pdu, 6 + 2 * i);
                        image.Writes++;
                        return new[] { fc, pdu[1], pdu[2], pdu[3], pdu[4] };
                    }

                    case 23:
                    {
                        // Read/write multiple registers: the write is done first, then the read (as the specification says).
                        if (pdu.Length < 10) return Fail(fc, IllegalDataValue);
                        int readAt = Word(pdu, 1), readCount = Word(pdu, 3), writeAt = Word(pdu, 5), writeCount = Word(pdu, 7), bytes = pdu[9];
                        if (readCount < 1 || readCount > 125 || writeCount < 1 || writeCount > 121 || bytes != writeCount * 2 || pdu.Length != 10 + bytes)
                            return Fail(fc, IllegalDataValue);
                        if (writeAt + writeCount > image.HoldingRegisters.Length || readAt + readCount > image.HoldingRegisters.Length) return Fail(fc, IllegalDataAddress);
                        for (int i = 0; i < writeCount; i++) image.HoldingRegisters[writeAt + i] = (ushort)Word(pdu, 10 + 2 * i);
                        image.Writes++;
                        return ReadWords(fc, new[] { fc, pdu[1], pdu[2], pdu[3], pdu[4] }, image.HoldingRegisters);
                    }

                    default:
                        return Fail(fc, IllegalFunction);
                }
            }
        }

        private static byte[] ReadBits(byte fc, byte[] pdu, bool[] table)
        {
            if (pdu.Length != 5) return Fail(fc, IllegalDataValue);
            int at = Word(pdu, 1), count = Word(pdu, 3);
            if (count < 1 || count > 2000) return Fail(fc, IllegalDataValue);
            if (at + count > table.Length) return Fail(fc, IllegalDataAddress);
            int bytes = (count + 7) / 8;
            var reply = new byte[2 + bytes];
            reply[0] = fc;
            reply[1] = (byte)bytes;
            for (int i = 0; i < count; i++)
                if (table[at + i]) reply[2 + i / 8] |= (byte)(1 << (i % 8));
            return reply;
        }

        private static byte[] ReadWords(byte fc, byte[] pdu, ushort[] table)
        {
            if (pdu.Length != 5) return Fail(fc, IllegalDataValue);
            int at = Word(pdu, 1), count = Word(pdu, 3);
            if (count < 1 || count > 125) return Fail(fc, IllegalDataValue);
            if (at + count > table.Length) return Fail(fc, IllegalDataAddress);
            var reply = new byte[2 + 2 * count];
            reply[0] = fc;
            reply[1] = (byte)(2 * count);
            for (int i = 0; i < count; i++)
            {
                reply[2 + 2 * i] = (byte)(table[at + i] >> 8);
                reply[3 + 2 * i] = (byte)table[at + i];
            }

            return reply;
        }

        private static int Word(byte[] b, int at) => b[at] << 8 | b[at + 1];

        private static byte[] Fail(byte fc, byte code) => new[] { (byte)(fc | 0x80), code };

        public void Dispose()
        {
            _stop.Cancel();
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
            }

            lock (_clients)
            {
                foreach (var c in _clients) c.Dispose();
                _clients.Clear();
            }

            _stop.Dispose();
        }
    }
}
