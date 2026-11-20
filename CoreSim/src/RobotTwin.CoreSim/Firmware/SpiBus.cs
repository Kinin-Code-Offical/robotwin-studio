using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>An SPI slave's protocol model (full duplex, one byte at a time).</summary>
    public interface ISpiDevice
    {
        /// <summary>Chip select changed: true when CS went low (selected), false when it went high (frame ends).</summary>
        void Select(bool selected);

        /// <summary>One byte clocked in on MOSI; returns the byte shifted out on MISO at the same time.</summary>
        byte Transfer(byte mosi);
    }

    /// <summary>A device on an SPI bus with the MCU pins its lines are really wired to.</summary>
    public sealed class SpiSlave
    {
        public SpiSlave(string refDes, ISpiDevice device, int sck, int miso, int mosi, int cs, Func<bool> powered)
        {
            RefDes = refDes;
            Device = device;
            Sck = sck;
            Miso = miso;
            Mosi = mosi;
            Cs = cs;
            Powered = powered;
        }

        public string RefDes { get; }
        public ISpiDevice Device { get; }

        /// <summary>MCU GPIO numbers its SCK / MISO / MOSI / CS lines reach (−1 = not wired to this MCU).</summary>
        public int Sck { get; }
        public int Miso { get; }
        public int Mosi { get; }
        public int Cs { get; }
        public Func<bool> Powered { get; }

        /// <summary>Highest SCK frequency the device accepts at its supply (datasheet), Hz.</summary>
        public Func<double> MaxClockHz { get; set; } = () => 10e6;

        /// <summary>CS is currently low.</summary>
        public bool Selected { get; internal set; }
    }

    /// <summary>
    /// The SPI master side of an MCU: decides which wired, powered device is selected (its CS GPIO
    /// driven low) and exchanges bytes with it. With nothing selected MISO floats high and reads
    /// 0xFF; two devices selected at once fight over MISO (wired-AND here) and a warning is raised —
    /// exactly what a logic analyser would show on real hardware.
    /// </summary>
    public sealed class SpiBus
    {
        public List<SpiSlave> Slaves { get; } = new List<SpiSlave>();

        /// <summary>(device, MOSI byte, MISO byte) of every exchanged byte, or (null, out, 0xFF) with nothing selected.</summary>
        public event Action<string?, byte, byte>? Traffic;

        /// <summary>A problem a developer should see (contention, CS of an unpowered device …).</summary>
        public event Action<string>? Warning;

        private readonly HashSet<string> _warned = new HashSet<string>();

        private void Warn(string key, string message)
        {
            if (_warned.Add(key)) Warning?.Invoke(message);
        }

        /// <summary>A GPIO the MCU drives changed level: CS edges start and end device frames.</summary>
        public void PinChanged(int gpio, bool level)
        {
            foreach (var s in Slaves)
            {
                if (s.Cs != gpio) continue;
                bool sel = !level;
                if (sel == s.Selected) continue;
                s.Selected = sel;
                s.Device.Select(sel);
            }
        }

        /// <summary>
        /// Exchanges one byte on the bus whose clock/data GPIOs are <paramref name="sck"/>,
        /// <paramref name="miso"/>, <paramref name="mosi"/>.
        /// </summary>
        public byte Transfer(int sck, int miso, int mosi, byte value, int mode = 0, double clockHz = 1e6)
        {
            int count = 0;
            byte result = 0xFF;
            string? who = null;
            foreach (var s in Slaves)
            {
                if (!s.Selected || s.Sck != sck || s.Mosi != mosi) continue;
                if (!s.Powered())
                {
                    Warn(s.RefDes + ":power", $"{s.RefDes}: CS düşük ama modülün beslemesi yok — SPI cevap vermiyor.");
                    continue;
                }

                byte r = s.Device.Transfer(value);
                if (s.Miso != miso) continue;   // MISO not wired to this MCU pin: the reply never arrives
                // Modes 1 and 2 sample on the other clock edge than these devices drive (modes 0 and 3
                // only); a clock above the device's limit samples before DOUT settles. Either way the
                // master reads every bit one position late — the classic "wrong values, wiring fine".
                bool late = mode == 1 || mode == 2;
                double max = s.MaxClockHz();
                if (late) Warn(s.RefDes + ":mode", $"{s.RefDes}: SPI modu {mode} — bu cihaz yalnız mod 0 ve 3'ü destekler; okunan bitler bir kayık.");
                if (clockHz > max * 1.0001)
                {
                    late = true;
                    Warn(s.RefDes + ":clk", $"{s.RefDes}: SPI saati {clockHz / 1e6:0.##} MHz, cihazın sınırı {max / 1e6:0.##} MHz — veri bozuluyor.");
                }

                if (late) r = (byte)((r >> 1) | 0x80);
                result &= r;
                who = s.RefDes;
                count++;
            }

            if (count > 1) Warn("contention", "SPI çakışması: aynı anda birden fazla cihazın CS'i düşük; MISO hattı karışıyor.");
            Traffic?.Invoke(who, value, result);
            return result;
        }
    }
}
