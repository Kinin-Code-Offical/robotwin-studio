using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The ESP32's SPI flash: a 4 MB chip (GigaDevice-like JEDEC id C8 40 16) holding the board's image
    /// (bootloader at 0x1000, partition table at 0x8000, the application at 0x10000, NVS and the rest erased), seen
    /// two ways as on the real chip: through the cache, where the MMU maps 64 KB flash pages into the IROM and DROM
    /// address ranges (the bootloader maps the application's segments from its image header), and through the SPI1
    /// controller, where the flash driver sends commands (id, status, read, page program, erases).
    /// </summary>
    public sealed partial class Esp32Soc
    {
        public const int FlashSize = 4 * 1024 * 1024;
        private const uint MmuTable = 0x3FF10000, MmuTableApp = 0x3FF12000, MmuInvalid = 0x100;
        private const uint Spi1 = 0x3FF42000;

        private byte[]? _flash;
        private readonly uint[] _mmu = NewMmu();
        private bool _writeEnabled;

        private static uint[] NewMmu()
        {
            var mmu = new uint[256];
            for (int i = 0; i < mmu.Length; i++) mmu[i] = MmuInvalid;
            return mmu;
        }

        /// <summary>The flash's contents (null when only an ELF was loaded: IROM and DROM then come straight from it).</summary>
        public byte[]? Flash => _flash;

        /// <summary>
        /// Loads the board's whole flash image (the build's merged .bin) and maps the application as the bootloader
        /// does: each IROM/DROM segment of the image at 0x10000, page by page, to its virtual addresses.
        /// </summary>
        public void LoadFlash(byte[] image, uint appOffset = 0x10000)
        {
            _flash = new byte[FlashSize];
            for (int i = 0; i < _flash.Length; i++) _flash[i] = 0xFF;
            Array.Copy(image, _flash, Math.Min(image.Length, FlashSize));
            Array.Fill(_mmu, MmuInvalid);
            if (_flash[appOffset] != 0xE9) return;   // no application image
            int segments = _flash[appOffset + 1];
            uint offset = appOffset + 24;
            for (int s = 0; s < segments && offset + 8 < FlashSize; s++)
            {
                uint load = U32(offset), length = U32(offset + 4);
                uint data = offset + 8;
                bool drom = load >= DromBase && load < DromEnd, irom = load >= 0x40000000 + 0xC0000 && load < IromEnd;
                if (drom || irom)
                {
                    // esptool aligns these segments so the address and the flash offset agree within a 64 KB page.
                    uint firstPage = load >> 16, lastPage = (load + length - 1) >> 16;
                    for (uint page = firstPage; page <= lastPage; page++)
                    {
                        int entry = MmuEntry(page << 16);
                        if (entry >= 0) _mmu[entry] = ((data & ~0xFFFFu) >> 16) + (page - firstPage);
                    }
                }

                offset = data + length;
            }
        }

        private uint U32(uint at) => (uint)(_flash![at] | _flash[at + 1] << 8 | _flash[at + 2] << 16 | _flash[at + 3] << 24);

        /// <summary>The MMU entry for a cached address: 0–63 DROM (0x3F400000…), 64–255 the instruction ranges from 0x40000000.</summary>
        private static int MmuEntry(uint address)
        {
            if (address >= DromBase && address < DromEnd) return (int)((address - DromBase) >> 16);
            if (address >= 0x40000000 && address < IromEnd) return 64 + (int)((address - 0x40000000) >> 16);
            return -1;
        }

        /// <summary>Where a cached IROM/DROM address lands in the flash (null when the page is not mapped).</summary>
        private (byte[]? Memory, uint Offset) ThroughCache(uint address)
        {
            int entry = MmuEntry(address);
            if (entry < 0 || _mmu[entry] >= MmuInvalid) return (null, 0);
            uint at = (_mmu[entry] << 16) | (address & 0xFFFF);
            return at < FlashSize ? (_flash, at) : (null, 0);
        }

        private bool ReadFlashRegister(uint register, out uint value)
        {
            if ((register >= MmuTable && register < MmuTable + 0x400) || (register >= MmuTableApp && register < MmuTableApp + 0x400))
            {
                value = _mmu[((register & 0x3FF) >> 2) & 255];
                return true;
            }

            if (register == Spi1)
            {
                value = 0;   // every command completes at once
                return true;
            }

            if (register == Spi1 + 0x10)
            {
                value = _writeEnabled ? 2u : 0u;   // SPI_RD_STATUS: never busy, WEL as the last WREN/WRDI left it
                return true;
            }

            value = 0;
            return false;
        }

        private bool WriteFlashRegister(uint register, uint value)
        {
            if ((register >= MmuTable && register < MmuTable + 0x400) || (register >= MmuTableApp && register < MmuTableApp + 0x400))
            {
                _mmu[((register & 0x3FF) >> 2) & 255] = value & 0x1FF;
                return true;
            }

            if (register == Spi1)
            {
                if (value != 0) FlashCommand(value);
                return true;
            }

            return register >= Spi1 && register < Spi1 + 0x100;   // the controller's set-up registers: kept as written
        }

        private uint Reg(uint register)
        {
            _registers.TryGetValue(register, out uint v);
            return v;
        }

        private void SetReg(uint register, uint value) => _registers[register] = value;

        /// <summary>A command the flash driver started on SPI1: a user transaction (USR) or one of the legacy command bits.</summary>
        private void FlashCommand(uint cmd)
        {
            if (_flash == null) return;
            if ((cmd & (1u << 18)) != 0)
            {
                UserTransaction();
                return;
            }

            uint addr = Reg(Spi1 + 0x04) & 0xFFFFFF;
            if ((cmd & (1u << 28)) != 0) SetReg(Spi1 + 0x80, 0x001640C8);                     // RDID
            if ((cmd & (1u << 27)) != 0) SetReg(Spi1 + 0x10, 0);                              // RDSR
            if ((cmd & (1u << 30)) != 0) _writeEnabled = true;                                 // WREN
            if ((cmd & (1u << 29)) != 0) _writeEnabled = false;                                // WRDI
            if ((cmd & (1u << 31)) != 0) ReadInto(addr, (int)(Reg(Spi1 + 0x04) >> 24));       // READ (length in ADDR[31:24])
            if ((cmd & (1u << 25)) != 0) Program(addr, (int)(Reg(Spi1 + 0x04) >> 24));        // PP
            if ((cmd & (1u << 24)) != 0) Erase(addr, 0x1000);                                  // SE
            if ((cmd & (1u << 23)) != 0) Erase(addr, 0x10000);                                 // BE
            if ((cmd & (1u << 22)) != 0) Erase(0, FlashSize);                                  // CE
        }

        private void UserTransaction()
        {
            uint user = Reg(Spi1 + 0x1C), user1 = Reg(Spi1 + 0x20), user2 = Reg(Spi1 + 0x24);
            int command = (int)(user2 & 0xFFFF);
            int addressBits = (user & (1u << 30)) != 0 ? (int)(user1 >> 26) + 1 : 0;
            // A 24-bit address goes out from the top of SPI_ADDR (the driver shifts it up by 8).
            uint address = addressBits == 0 ? 0 : addressBits >= 32 ? Reg(Spi1 + 0x04) : (Reg(Spi1 + 0x04) >> 8) & 0xFFFFFF;
            int misoBytes = (user & (1u << 28)) != 0 ? (int)((Reg(Spi1 + 0x2C) & 0xFFFFFF) + 1 + 7) / 8 : 0;
            int mosiBytes = (user & (1u << 27)) != 0 ? (int)((Reg(Spi1 + 0x28) & 0xFFFFFF) + 1 + 7) / 8 : 0;
            switch (command)
            {
                case 0x9F: SetReg(Spi1 + 0x80, 0x001640C8); break;                             // JEDEC id: C8 40 16
                case 0x05:
                case 0x35:
                case 0x15: SetReg(Spi1 + 0x80, command == 0x05 && _writeEnabled ? 2u : 0u); break;   // status registers
                case 0x06: _writeEnabled = true; break;
                case 0x04: _writeEnabled = false; break;
                case 0x03:
                case 0x0B:
                case 0x3B:
                case 0x6B:
                case 0xBB:
                case 0xEB: ReadInto(address, misoBytes); break;
                case 0x02: Program(address, mosiBytes); break;
                case 0x20: Erase(address, 0x1000); break;
                case 0x52: Erase(address, 0x8000); break;
                case 0xD8: Erase(address, 0x10000); break;
                case 0x60:
                case 0xC7: Erase(0, FlashSize); break;
                case 0x5A:
                case 0x4B:
                    for (uint w = 0; w < 16; w++) SetReg(Spi1 + 0x80 + w * 4, 0xFFFFFFFF);   // SFDP / unique id: none
                    break;
            }
        }

        private void ReadInto(uint address, int bytes)
        {
            bytes = Math.Clamp(bytes, 0, 64);
            for (int w = 0; w < (bytes + 3) / 4; w++)
            {
                uint v = 0;
                for (int b = 0; b < 4; b++)
                {
                    uint at = (address + (uint)(w * 4 + b)) % FlashSize;
                    v |= (uint)_flash![at] << (8 * b);
                }

                SetReg(Spi1 + 0x80 + (uint)w * 4, v);
            }
        }

        private void Program(uint address, int bytes)
        {
            if (!_writeEnabled) return;
            bytes = Math.Clamp(bytes, 0, 64);
            for (int i = 0; i < bytes; i++)
            {
                uint word = Reg(Spi1 + 0x80 + (uint)(i / 4) * 4);
                uint at = (address + (uint)i) % FlashSize;
                _flash![at] &= (byte)(word >> (8 * (i % 4)));   // programming only clears bits, as on a NOR flash
            }

            _writeEnabled = false;
        }

        private void Erase(uint address, int size)
        {
            if (!_writeEnabled) return;
            uint start = address & ~(uint)(size - 1);
            for (int i = 0; i < size && start + i < FlashSize; i++) _flash![start + i] = 0xFF;
            _writeEnabled = false;
        }
    }
}
