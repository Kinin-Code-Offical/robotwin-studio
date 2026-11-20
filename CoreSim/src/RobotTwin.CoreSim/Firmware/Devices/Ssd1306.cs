using System;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    public enum Ssd1306AddressingMode
    {
        Horizontal = 0,
        Vertical = 1,
        Page = 2,
    }

    /// <summary>
    /// SSD1306 OLED controller on I²C (128×64 or 128×32 module). Each write transaction
    /// is a sequence of control bytes (Co, D/C#) followed by command or GDDRAM data
    /// bytes; command parameters may span transactions, as on the real chip.
    /// <see cref="GetPixel"/> returns what the module's panel shows, applying segment
    /// remap, COM scan direction, COM pin configuration, start line, display offset,
    /// multiplex ratio, inverse, entire-display-on and display/charge-pump power.
    /// Scroll commands are accepted and recorded but not animated.
    /// </summary>
    public sealed class Ssd1306 : II2cDevice
    {
        public const int RamColumns = 128;
        public const int RamPages = 8;

        private readonly byte[] _ram = new byte[RamColumns * RamPages];
        private readonly byte[] _params = new byte[8];

        private bool _expectControl;
        private bool _continuation;
        private bool _dataMode;
        private byte _pendingCommand;
        private int _paramsNeeded;
        private int _paramCount;
        private int _pageModeColumnStart;

        public Ssd1306(int height = 64, byte address = 0x3C)
        {
            if (height != 64 && height != 32) throw new ArgumentOutOfRangeException(nameof(height), "SSD1306 modules are 128x64 or 128x32.");
            if (address != 0x3C && address != 0x3D) throw new ArgumentOutOfRangeException(nameof(address), "SSD1306 I2C address is 0x3C or 0x3D.");
            Height = height;
            Address = address;
            Reset();
        }

        public byte Address { get; }
        public int Width => RamColumns;
        public int Height { get; }

        /// <summary>Increments on every change that can alter what the panel shows (UI redraw hint).</summary>
        public int Version { get; private set; }

        /// <summary>Graphic display data RAM: 8 pages × 128 columns, bit n of a byte = row page·8+n.</summary>
        public byte[] Gddram => _ram;

        public bool DisplayOn { get; private set; }
        public bool Inverted { get; private set; }
        public byte Contrast { get; private set; }
        public bool EntireDisplayOn { get; private set; }
        public Ssd1306AddressingMode AddressingMode { get; private set; }
        public int ColumnStart { get; private set; }
        public int ColumnEnd { get; private set; }
        public int PageStart { get; private set; }
        public int PageEnd { get; private set; }

        /// <summary>GDDRAM column pointer.</summary>
        public int Column { get; private set; }

        /// <summary>GDDRAM page pointer.</summary>
        public int Page { get; private set; }

        public int StartLine { get; private set; }
        public int DisplayOffset { get; private set; }
        public int MultiplexRatio { get; private set; }
        public bool SegmentRemap { get; private set; }
        public bool ComScanReversed { get; private set; }
        public byte ComPinsConfig { get; private set; }
        public byte ClockDivide { get; private set; }
        public byte PrechargePeriod { get; private set; }
        public byte VcomhDeselect { get; private set; }
        public byte ChargePumpSetting { get; private set; }
        public bool ChargePumpEnabled => (ChargePumpSetting & 0x04) != 0;

        /// <summary>The module has an external panel supply, so the charge pump is not needed to light it.</summary>
        public bool ExternalVcc { get; set; }

        public bool ScrollActive { get; private set; }

        /// <summary>Last scroll setup command (0x26/0x27/0x29/0x2A) followed by its parameters; empty if none.</summary>
        public byte[] ScrollSetup { get; private set; } = Array.Empty<byte>();

        public int VerticalScrollTop { get; private set; }
        public int VerticalScrollRows { get; private set; }

        /// <summary>True when the panel is lit at all: display on and powered (charge pump or external VCC).</summary>
        public bool PanelPowered => DisplayOn && (ChargePumpEnabled || ExternalVcc);

        /// <summary>Power-on reset state (RES# pulse).</summary>
        public void Reset()
        {
            Array.Clear(_ram, 0, _ram.Length);
            _expectControl = false;
            _continuation = false;
            _dataMode = false;
            _paramsNeeded = 0;
            _paramCount = 0;
            _pageModeColumnStart = 0;
            DisplayOn = false;
            Inverted = false;
            Contrast = 0x7F;
            EntireDisplayOn = false;
            AddressingMode = Ssd1306AddressingMode.Page;
            ColumnStart = 0;
            ColumnEnd = RamColumns - 1;
            PageStart = 0;
            PageEnd = RamPages - 1;
            Column = 0;
            Page = 0;
            StartLine = 0;
            DisplayOffset = 0;
            MultiplexRatio = 64;
            SegmentRemap = false;
            ComScanReversed = false;
            ComPinsConfig = 0x12;
            ClockDivide = 0x80;
            PrechargePeriod = 0x22;
            VcomhDeselect = 0x20;
            ChargePumpSetting = 0x10;
            ScrollActive = false;
            ScrollSetup = Array.Empty<byte>();
            VerticalScrollTop = 0;
            VerticalScrollRows = 64;
            Version++;
        }

        // ================================================================ II2cDevice

        public bool Start(bool read)
        {
            _expectControl = !read;
            return true;
        }

        public bool Write(byte value)
        {
            if (_expectControl)
            {
                _continuation = (value & 0x80) != 0;
                _dataMode = (value & 0x40) != 0;
                _expectControl = false;
                return true;
            }

            if (_dataMode) WriteData(value);
            else Command(value);
            if (_continuation) _expectControl = true;
            return true;
        }

        /// <summary>I²C read returns the status byte (D6 = 1 while the display is off).</summary>
        public byte Read(bool ack) => (byte)(DisplayOn ? 0x00 : 0x40);

        public void Stop()
        {
            _expectControl = false;
        }

        // ================================================================ panel

        /// <summary>Whether the panel pixel at (x, y) is lit, (0, 0) being the top-left of the module.</summary>
        public bool GetPixel(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
            if (!PanelPowered) return false;
            if (EntireDisplayOn) return true;
            int com = ComForPanelRow(y);
            if (com < 0 || com >= MultiplexRatio) return false;
            int line = ComScanReversed ? MultiplexRatio - 1 - com : com;
            int row = (line + DisplayOffset + StartLine) & 63;
            // The module's panel is mounted so that SEG127 is the left edge: A1 (remap) shows column 0 on the left.
            int column = SegmentRemap ? x : RamColumns - 1 - x;
            bool on = ((_ram[(row >> 3) * RamColumns + column] >> (row & 7)) & 1) != 0;
            return on != Inverted;
        }

        /// <summary>
        /// COM signal that drives panel row <paramref name="y"/>. The modules are mounted
        /// rotated by 180° (C8h is the upright setting); 128×64 panels alternate rows between
        /// the left and right COM pads (DAh 12h is the matching setting), 128×32 panels use the
        /// first 32 COM lines sequentially (DAh 02h).
        /// </summary>
        private int ComForPanelRow(int y)
        {
            int q = Height - 1 - y;
            bool right;
            int k;
            if (Height == 64)
            {
                right = (q & 1) != 0;
                k = q >> 1;
            }
            else
            {
                right = false;
                k = q;
            }

            bool alternative = (ComPinsConfig & 0x10) != 0;
            if ((ComPinsConfig & 0x20) != 0) right = !right;
            if (alternative) return 2 * k + (right ? 1 : 0);
            return right ? 32 + k : k;
        }

        // ================================================================ data

        private void WriteData(byte value)
        {
            int index = Page * RamColumns + Column;
            if (_ram[index] != value)
            {
                _ram[index] = value;
                Version++;
            }

            switch (AddressingMode)
            {
                case Ssd1306AddressingMode.Horizontal:
                    if (Column >= ColumnEnd)
                    {
                        Column = ColumnStart;
                        Page = Page >= PageEnd ? PageStart : Page + 1;
                    }
                    else
                    {
                        Column++;
                    }

                    break;
                case Ssd1306AddressingMode.Vertical:
                    if (Page >= PageEnd)
                    {
                        Page = PageStart;
                        Column = Column >= ColumnEnd ? ColumnStart : Column + 1;
                    }
                    else
                    {
                        Page++;
                    }

                    break;
                default:
                    // Page mode: the column wraps from 127 to 0 within the same page.
                    Column = Column >= RamColumns - 1 ? 0 : Column + 1;
                    break;
            }
        }

        // ================================================================ commands

        private static int ParameterCount(byte command)
        {
            switch (command)
            {
                case 0x81: case 0x20: case 0xA8: case 0xD3: case 0xD5: case 0xD9: case 0xDA: case 0xDB: case 0x8D:
                    return 1;
                case 0x21: case 0x22: case 0xA3:
                    return 2;
                case 0x29: case 0x2A:
                    return 5;
                case 0x26: case 0x27:
                    return 6;
                default:
                    return 0;
            }
        }

        private void Command(byte c)
        {
            if (_paramsNeeded > 0)
            {
                _params[_paramCount++] = c;
                if (_paramCount < _paramsNeeded) return;
                _paramsNeeded = 0;
                ExecuteWithParameters(_pendingCommand);
                return;
            }

            int n = ParameterCount(c);
            if (n > 0)
            {
                _pendingCommand = c;
                _paramsNeeded = n;
                _paramCount = 0;
                return;
            }

            Execute(c);
        }

        private void Execute(byte c)
        {
            if (c <= 0x0F)
            {
                _pageModeColumnStart = (_pageModeColumnStart & 0x70) | c;
                Column = _pageModeColumnStart;
            }
            else if (c <= 0x1F)
            {
                _pageModeColumnStart = ((c & 0x07) << 4) | (_pageModeColumnStart & 0x0F);
                Column = _pageModeColumnStart;
            }
            else if (c >= 0x40 && c <= 0x7F)
            {
                StartLine = c & 0x3F;
                Version++;
            }
            else if (c >= 0xB0 && c <= 0xB7)
            {
                Page = c & 0x07;
            }
            else if (c >= 0xC0 && c <= 0xCF)
            {
                ComScanReversed = (c & 0x08) != 0;
                Version++;
            }
            else
            {
                switch (c)
                {
                    case 0xA0: case 0xA1: SegmentRemap = c == 0xA1; Version++; break;
                    case 0xA4: case 0xA5: EntireDisplayOn = c == 0xA5; Version++; break;
                    case 0xA6: case 0xA7: Inverted = c == 0xA7; Version++; break;
                    case 0xAE: case 0xAF: DisplayOn = c == 0xAF; Version++; break;
                    case 0x2E: ScrollActive = false; break;
                    case 0x2F: ScrollActive = true; break;
                }
            }
        }

        private void ExecuteWithParameters(byte c)
        {
            byte p0 = _params[0], p1 = _params[1];
            switch (c)
            {
                case 0x81: Contrast = p0; Version++; break;
                case 0x20:
                    if ((p0 & 3) != 3) AddressingMode = (Ssd1306AddressingMode)(p0 & 3);
                    break;
                case 0x21:
                    ColumnStart = p0 & 0x7F;
                    ColumnEnd = p1 & 0x7F;
                    Column = ColumnStart;
                    break;
                case 0x22:
                    PageStart = p0 & 0x07;
                    PageEnd = p1 & 0x07;
                    Page = PageStart;
                    break;
                case 0xA8:
                    if ((p0 & 0x3F) < 15) break; // 0..14 are invalid multiplex ratios
                    MultiplexRatio = (p0 & 0x3F) + 1;
                    Version++;
                    break;
                case 0xD3: DisplayOffset = p0 & 0x3F; Version++; break;
                case 0xD5: ClockDivide = p0; break;
                case 0xD9: PrechargePeriod = p0; break;
                case 0xDA: ComPinsConfig = (byte)(p0 & 0x32); Version++; break;
                case 0xDB: VcomhDeselect = p0; break;
                case 0x8D: ChargePumpSetting = p0; Version++; break;
                case 0xA3:
                    VerticalScrollTop = p0 & 0x3F;
                    VerticalScrollRows = p1 & 0x7F;
                    break;
                case 0x26:
                case 0x27:
                case 0x29:
                case 0x2A:
                {
                    int n = ParameterCount(c);
                    var setup = new byte[n + 1];
                    setup[0] = c;
                    Array.Copy(_params, 0, setup, 1, n);
                    ScrollSetup = setup;
                    ScrollActive = false; // a new setup stops scrolling; 2Fh starts it
                    break;
                }
            }
        }
    }
}
