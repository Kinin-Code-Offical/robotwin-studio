using System;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    /// <summary>
    /// PCF8574 / PCF8574A 8-bit quasi-bidirectional I/O expander. Every byte written
    /// sets the output latch; a read returns the pin levels: a pin whose latch bit is 1
    /// is only weakly pulled up, so it reads low when something outside pulls it low.
    /// </summary>
    public class Pcf8574 : II2cDevice
    {
        private byte _latch = 0xFF;
        private byte _externalInputs = 0xFF;

        public Pcf8574(byte address = 0x20)
        {
            if (!((address >= 0x20 && address <= 0x27) || (address >= 0x38 && address <= 0x3F)))
                throw new ArgumentOutOfRangeException(nameof(address), "PCF8574 uses 0x20-0x27, PCF8574A 0x38-0x3F.");
            Address = address;
        }

        public byte Address { get; }

        /// <summary>Increments on every visible change (UI redraw hint).</summary>
        public int Version { get; private set; }

        /// <summary>Output latch (power-on 0xFF).</summary>
        public byte Port => _latch;

        /// <summary>Levels that circuitry outside the chip forces on P0-P7 (1 = not pulling low).</summary>
        public byte ExternalInputs
        {
            get => _externalInputs;
            set
            {
                if (_externalInputs == value) return;
                _externalInputs = value;
                Touch();
            }
        }

        /// <summary>Pin levels as a read returns them.</summary>
        public byte Pins => (byte)(_latch & _externalInputs & DeviceDrive());

        /// <summary>Raised after every byte written to the port: (previous latch, new latch).</summary>
        public event Action<byte, byte>? PortWritten;

        public virtual void Reset()
        {
            _latch = 0xFF;
            Touch();
        }

        public bool Start(bool read) => true;

        public bool Write(byte value)
        {
            byte previous = _latch;
            _latch = value;
            if (previous != value) OnPortChanged(previous, value);
            PortWritten?.Invoke(previous, value);
            return true;
        }

        public byte Read(bool ack) => Pins;

        public void Stop()
        {
        }

        /// <summary>The latch changed; the base implementation counts it as a visible change.</summary>
        protected virtual void OnPortChanged(byte previous, byte current) => Touch();

        /// <summary>Bits the attached device actively pulls low (0) on its own; 0xFF when it drives nothing.</summary>
        protected virtual byte DeviceDrive() => 0xFF;

        protected void Touch() => Version++;
    }
}
