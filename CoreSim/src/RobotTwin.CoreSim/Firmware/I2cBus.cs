using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>
    /// A device on an I²C bus, seen byte by byte from the master side. The bus
    /// calls <see cref="Start"/> after the address byte; a device that returns
    /// false NACKs its address.
    /// </summary>
    public interface II2cDevice
    {
        /// <summary>7-bit bus address.</summary>
        byte Address { get; }

        /// <summary>Address phase. <paramref name="read"/> is the R/W bit. Return true to ACK.</summary>
        bool Start(bool read);

        /// <summary>Master writes one byte; return true to ACK it.</summary>
        bool Write(byte value);

        /// <summary>Master reads one byte; <paramref name="ack"/> is the master's ACK for it.</summary>
        byte Read(bool ack);

        /// <summary>STOP condition (or a repeated START addressed elsewhere).</summary>
        void Stop();
    }

    /// <summary>Logical I²C bus shared by a microcontroller and the devices wired to its SDA/SCL.</summary>
    public sealed class I2cBus
    {
        private II2cDevice? _active;

        public List<II2cDevice> Devices { get; } = new List<II2cDevice>();

        /// <summary>Raised for every completed transfer: (address, isRead, byte, acked).</summary>
        public event Action<byte, bool, byte, bool>? Traffic;

        public II2cDevice? Find(byte address)
        {
            foreach (var d in Devices)
            {
                if (d.Address == address) return d;
            }

            return null;
        }

        /// <summary>START + address byte. Returns true when a device ACKs.</summary>
        public bool BeginTransfer(byte address, bool read)
        {
            _active?.Stop();
            _active = Find(address);
            bool ack = _active != null && _active.Start(read);
            if (!ack) _active = null;
            Traffic?.Invoke(address, read, (byte)((address << 1) | (read ? 1 : 0)), ack);
            return ack;
        }

        public bool WriteByte(byte value)
        {
            bool ack = _active != null && _active.Write(value);
            Traffic?.Invoke(_active?.Address ?? 0, false, value, ack);
            return ack;
        }

        public byte ReadByte(bool ack)
        {
            byte v = _active != null ? _active.Read(ack) : (byte)0xFF;
            Traffic?.Invoke(_active?.Address ?? 0, true, v, ack);
            return v;
        }

        public void EndTransfer()
        {
            _active?.Stop();
            _active = null;
        }
    }
}

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>Wraps a bus device so it only answers while its module is powered.</summary>
    public sealed class GatedI2cDevice : II2cDevice
    {
        private readonly Func<bool> _powered;

        public GatedI2cDevice(II2cDevice inner, Func<bool> powered)
        {
            Inner = inner;
            _powered = powered;
        }

        public II2cDevice Inner { get; }
        public byte Address => Inner.Address;
        public bool Start(bool read) => _powered() && Inner.Start(read);
        public bool Write(byte value) => Inner.Write(value);
        public byte Read(bool ack) => Inner.Read(ack);
        public void Stop() => Inner.Stop();
    }
}
