using System;
using System.Text;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    public sealed partial class Esp32Soc
    {
        private readonly System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<byte>> _md5 = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<byte>>();

        private static double Double(XtensaCore c, int index) =>
            BitConverter.Int64BitsToDouble((long)((ulong)c.Argument(index + 1) << 32 | c.Argument(index)));

        private static bool ReturnDouble(XtensaCore c, double value)
        {
            ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
            c.ReturnFromCall((uint)bits, (uint)(bits >> 32));
            return true;
        }

        private static long Long(XtensaCore c, int index) => (long)((ulong)c.Argument(index + 1) << 32 | c.Argument(index));

        private static bool ReturnLong(XtensaCore c, long value)
        {
            c.ReturnFromCall((uint)value, (uint)((ulong)value >> 32));
            return true;
        }

        /// <summary>Where the ROM keeps each core's CPU ticks per µs (from the ELF's ROM symbols; 0 when it does not link them).</summary>
        private uint _ticksPerUsPro, _ticksPerUsApp;

        /// <summary>Each core's busy-wait deadline (its cycle count), while it sits in ets_delay_us.</summary>
        private readonly ulong?[] _delayUntil = new ulong?[2];

        private uint TicksPerMicrosecond(XtensaCore c)
        {
            uint at = c.CoreId == 0 ? _ticksPerUsPro : _ticksPerUsApp;
            uint ticks = at != 0 ? Read32(at) : 0;
            return ticks != 0 ? ticks : (uint)(CpuHz / 1e6);
        }

        private bool DelayMicroseconds(XtensaCore c)
        {
            ulong until = _delayUntil[c.CoreId] ??= c.Cycles + (ulong)c.Argument(0) * TicksPerMicrosecond(c);
            if (c.Cycles >= until)
            {
                _delayUntil[c.CoreId] = null;
                return Return(c, 0);
            }

            c.TrapCycles = (int)Math.Min(until - c.Cycles, 1000);   // stays on the call, its clock running
            return true;
        }

        private static bool Return(XtensaCore c, uint value)
        {
            c.ReturnFromCall(value);
            return true;
        }

        private static int Compare(double a, double b, int unordered) => double.IsNaN(a) || double.IsNaN(b) ? unordered : a < b ? -1 : a > b ? 1 : 0;

        private static int Saturate(double v) => double.IsNaN(v) ? 0 : v >= int.MaxValue ? int.MaxValue : v <= int.MinValue ? int.MinValue : (int)v;

        /// <summary>The mask ROM's functions a firmware links, served in C# (libgcc's soft double and 64-bit helpers, string functions, printing, RF calibration's register access, syscall stubs).</summary>
        private void BindRom()
        {
            // libgcc: double precision (passed in register pairs a2:a3, a4:a5)
            Rom("__adddf3", c => ReturnDouble(c, Double(c, 0) + Double(c, 2)));
            Rom("__subdf3", c => ReturnDouble(c, Double(c, 0) - Double(c, 2)));
            Rom("__muldf3", c => ReturnDouble(c, Double(c, 0) * Double(c, 2)));
            Rom("__divdf3", c => ReturnDouble(c, Double(c, 0) / Double(c, 2)));
            Rom("__negdf2", c => ReturnDouble(c, -Double(c, 0)));
            Rom("__extendsfdf2", c => ReturnDouble(c, BitConverter.Int32BitsToSingle((int)c.Argument(0))));
            Rom("__truncdfsf2", c => Return(c, (uint)BitConverter.SingleToInt32Bits((float)Double(c, 0))));
            Rom("__fixdfsi", c => Return(c, (uint)Saturate(Math.Truncate(Double(c, 0)))));
            Rom("__fixunsdfsi", c =>
            {
                double v = Math.Truncate(Double(c, 0));
                return Return(c, double.IsNaN(v) || v <= 0 ? 0 : v >= uint.MaxValue ? uint.MaxValue : (uint)v);
            });
            Rom("__floatsidf", c => ReturnDouble(c, (int)c.Argument(0)));
            Rom("__floatunsidf", c => ReturnDouble(c, c.Argument(0)));
            Rom("__floatdidf", c => ReturnDouble(c, Long(c, 0)));
            Rom("__floatundidf", c => ReturnDouble(c, (ulong)Long(c, 0)));
            Rom("__fixdfdi", c => ReturnLong(c, (long)Math.Truncate(Double(c, 0))));
            Rom("__eqdf2", c => Return(c, (uint)Compare(Double(c, 0), Double(c, 2), 1)));
            Rom("__nedf2", c => Return(c, (uint)Compare(Double(c, 0), Double(c, 2), 1)));
            Rom("__ltdf2", c => Return(c, (uint)Compare(Double(c, 0), Double(c, 2), 1)));
            Rom("__ledf2", c => Return(c, (uint)Compare(Double(c, 0), Double(c, 2), 1)));
            Rom("__gtdf2", c => Return(c, (uint)Compare(Double(c, 0), Double(c, 2), -1)));
            Rom("__gedf2", c => Return(c, (uint)Compare(Double(c, 0), Double(c, 2), -1)));
            Rom("__unorddf2", c => Return(c, double.IsNaN(Double(c, 0)) || double.IsNaN(Double(c, 2)) ? 1u : 0u));

            // libgcc: bit operations
            Rom("__bswapsi2", c => Return(c, (c.Argument(0) >> 24) | ((c.Argument(0) >> 8) & 0xFF00) | ((c.Argument(0) << 8) & 0xFF0000) | (c.Argument(0) << 24)));
            Rom("__bswapdi2", c =>
            {
                ulong v = (ulong)Long(c, 0), r = 0;
                for (int i = 0; i < 8; i++) r |= ((v >> (8 * i)) & 0xFF) << (8 * (7 - i));
                return ReturnLong(c, (long)r);
            });
            Rom("__clzsi2", c =>
            {
                uint v = c.Argument(0);
                int n = 0;
                while (n < 32 && (v & 0x80000000) == 0) { v <<= 1; n++; }
                return Return(c, (uint)n);
            });
            Rom("__ctzsi2", c =>
            {
                uint v = c.Argument(0);
                int n = 0;
                while (n < 32 && (v & 1) == 0) { v >>= 1; n++; }
                return Return(c, (uint)n);
            });
            Rom("__popcountsi2", c =>
            {
                uint v = c.Argument(0), n = 0;
                while (v != 0) { n += v & 1; v >>= 1; }
                return Return(c, n);
            });
            Rom("__udivsi3", c => Return(c, c.Argument(1) == 0 ? 0 : c.Argument(0) / c.Argument(1)));
            Rom("__umodsi3", c => Return(c, c.Argument(1) == 0 ? 0 : c.Argument(0) % c.Argument(1)));
            Rom("__divsi3", c => Return(c, c.Argument(1) == 0 ? 0 : (uint)((int)c.Argument(0) / (int)c.Argument(1))));
            Rom("__modsi3", c => Return(c, c.Argument(1) == 0 ? 0 : (uint)((int)c.Argument(0) % (int)c.Argument(1))));

            // libgcc: 64-bit integers
            Rom("__muldi3", c => ReturnLong(c, Long(c, 0) * Long(c, 2)));
            Rom("__divdi3", c => ReturnLong(c, Long(c, 2) == 0 ? 0 : Long(c, 0) / Long(c, 2)));
            Rom("__moddi3", c => ReturnLong(c, Long(c, 2) == 0 ? 0 : Long(c, 0) % Long(c, 2)));
            Rom("__udivdi3", c => ReturnLong(c, (ulong)Long(c, 2) == 0 ? 0 : (long)((ulong)Long(c, 0) / (ulong)Long(c, 2))));
            Rom("__umoddi3", c => ReturnLong(c, (ulong)Long(c, 2) == 0 ? 0 : (long)((ulong)Long(c, 0) % (ulong)Long(c, 2))));
            Rom("__ashldi3", c => ReturnLong(c, Long(c, 0) << (int)(c.Argument(2) & 63)));
            Rom("__lshrdi3", c => ReturnLong(c, (long)((ulong)Long(c, 0) >> (int)(c.Argument(2) & 63))));
            Rom("__ashrdi3", c => ReturnLong(c, Long(c, 0) >> (int)(c.Argument(2) & 63)));

            // the Xtensa runtime's interrupt helpers (XTOS): the level in PS, the enable mask
            Rom("_xtos_set_intlevel", c =>
            {
                uint old = c.Ps;
                c.Ps = (c.Ps & ~0xFu) | (c.Argument(0) & 0xF);
                return Return(c, old);
            });
            Rom("_xtos_ints_on", c =>
            {
                uint old = c.IntEnable;
                c.IntEnable |= c.Argument(0);
                return Return(c, old);
            });
            Rom("_xtos_ints_off", c =>
            {
                uint old = c.IntEnable;
                c.IntEnable &= ~c.Argument(0);
                return Return(c, old);
            });
            Rom("_xtos_set_interrupt_handler", c => Return(c, 0));
            Rom("_xtos_set_interrupt_handler_arg", c => Return(c, 0));
            Rom("_xtos_set_exception_handler", c => Return(c, 0));

            // MD5 (partition table and image digests): the bytes kept per context address, hashed at the end
            foreach (var name in new[] { "esp_rom_md5_init", "MD5Init" })
                Rom(name, c =>
                {
                    _md5[c.Argument(0)] = new System.Collections.Generic.List<byte>();
                    return Return(c, 0);
                });
            foreach (var name in new[] { "esp_rom_md5_update", "MD5Update" })
                Rom(name, c =>
                {
                    if (!_md5.TryGetValue(c.Argument(0), out var data)) _md5[c.Argument(0)] = data = new System.Collections.Generic.List<byte>();
                    for (uint i = 0; i < c.Argument(2); i++) data.Add(Read8(c.Argument(1) + i));
                    return Return(c, 0);
                });
            foreach (var name in new[] { "esp_rom_md5_final", "MD5Final" })
                Rom(name, c =>
                {
                    // (digest, context)
                    _md5.TryGetValue(c.Argument(1), out var data);
                    using var md5 = System.Security.Cryptography.MD5.Create();
                    byte[] digest = md5.ComputeHash((data ?? new System.Collections.Generic.List<byte>()).ToArray());
                    for (uint i = 0; i < 16; i++) Write8(c.Argument(0) + i, digest[i]);
                    _md5.Remove(c.Argument(1));
                    return Return(c, 0);
                });

            // checksums, as the ROM computes them (the value passed in and returned is the complemented one)
            Rom("crc32_le", c => Return(c, Crc(c, 0xEDB88320u, 32)));
            Rom("crc16_le", c => Return(c, Crc(c, 0x8408u, 16)));
            Rom("crc8_le", c => Return(c, Crc(c, 0x8Cu, 8)));
            // (data, length): the factory MAC's CRC, as burned into BLK0
            foreach (var name in new[] { "esp_rom_efuse_mac_address_crc8", "esp_crc8" })
                Rom(name, c =>
                {
                    var data = new byte[c.Argument(1)];
                    for (uint i = 0; i < data.Length; i++) data[i] = Read8(c.Argument(0) + i);
                    return Return(c, MacCrc8(data, data.Length));
                });

            // string functions
            Rom("memcpy", c => Copy(c, c.Argument(0), c.Argument(1), c.Argument(2)));
            Rom("memmove", c => Copy(c, c.Argument(0), c.Argument(1), c.Argument(2)));
            Rom("memset", c =>
            {
                uint d = c.Argument(0), n = c.Argument(2);
                byte v = (byte)c.Argument(1);
                for (uint i = 0; i < n; i++) Write8(d + i, v);
                return Return(c, d);
            });
            Rom("bzero", c =>
            {
                for (uint i = 0; i < c.Argument(1); i++) Write8(c.Argument(0) + i, 0);
                return Return(c, 0);
            });
            Rom("memcmp", c =>
            {
                uint a = c.Argument(0), b = c.Argument(1), n = c.Argument(2);
                for (uint i = 0; i < n; i++)
                {
                    int d = Read8(a + i) - Read8(b + i);
                    if (d != 0) return Return(c, (uint)d);
                }

                return Return(c, 0);
            });
            Rom("strlen", c => Return(c, (uint)CString(c.Argument(0)).Length));
            Rom("strcmp", c => Return(c, (uint)string.CompareOrdinal(CString(c.Argument(0)), CString(c.Argument(1)))));

            // printing (the boot log) and time
            Rom("ets_printf", c => Return(c, Print(c)));
            Rom("esp_rom_printf", c => Return(c, Print(c)));
            // The ROM's busy-wait: CCOUNT runs for us × the CPU's ticks per µs (interrupts still come in meanwhile).
            Rom("ets_delay_us", DelayMicroseconds);
            Rom("esp_rom_delay_us", DelayMicroseconds);

            // start-up: reset reason (power on), the CPU clock, caches (the flash is mapped directly here), the console
            foreach (var name in new[] { "esp_rom_get_reset_reason", "rtc_get_reset_reason" }) Rom(name, c => Return(c, 1));
            foreach (var name in new[] { "rtc_get_wakeup_cause", "esp_rom_get_wakeup_cause" }) Rom(name, c => Return(c, 0));
            // The CPU's ticks per µs the ROM keeps for each core (g_ticks_per_us_pro/app): IDF sets them as it switches clocks.
            foreach (var name in new[] { "esp_rom_get_cpu_ticks_per_us", "ets_get_cpu_frequency" }) Rom(name, c => Return(c, TicksPerMicrosecond(c)));
            foreach (var name in new[] { "esp_rom_set_cpu_ticks_per_us", "ets_update_cpu_frequency" })
            {
                Rom(name, c =>
                {
                    uint ticks = c.Argument(0);
                    if (_ticksPerUsPro != 0) Write32(_ticksPerUsPro, ticks);
                    if (_ticksPerUsApp != 0) Write32(_ticksPerUsApp, ticks);
                    return Return(c, 0);
                });
            }

            foreach (var name in new[]
                     {
                         "Cache_Read_Enable", "Cache_Read_Disable", "Cache_Flush",
                         "Cache_Read_Init", "mmu_init", "cache_flash_mmu_set", "cache_sram_mmu_set", "esp_rom_uart_tx_wait_idle", "uart_tx_wait_idle",
                         "esp_rom_install_uart_printf", "ets_install_putc1", "ets_install_putc2", "ets_install_uart_printf", "esp_rom_uart_set_as_console",
                         "ets_intr_lock", "ets_intr_unlock", "esp_rom_install_channel_putc", "uart_div_modify", "esp_rom_uart_set_clock_baudrate",
                         "Cache_Flush_rom", "esp_rom_Cache_Flush", "spi_flash_attach", "esp_rom_spiflash_attach", "esp_rom_spiflash_unlock",
                         "esp_rom_spiflash_config_param", "esp_rom_spiflash_config_clk", "esp_rom_spiflash_wait_idle", "esp_rom_spiflash_select_qio_pins",
                         "esp_rom_spiflash_prepare_encrypted_data", "esp_rom_spiflash_write_disable",
                     })
                Rom(name, c => Return(c, 0));
            foreach (var name in new[] { "esp_rom_uart_putc", "uart_tx_one_char", "ets_write_char_uart", "esp_rom_output_tx_one_char", "esp_rom_output_putc", "esp_rom_uart_tx_one_char" })
                Rom(name, c =>
                {
                    Uart0.Append((char)(c.Argument(0) & 0xFF));
                    return Return(c, 0);
                });
            Rom("ets_set_appcpu_boot_addr", c =>
            {
                _registers[AppCpuCtrlD] = c.Argument(0);
                return Return(c, 0);
            });
            Rom("intr_matrix_set", c =>
            {
                // (cpu, peripheral source, CPU interrupt): kept for the peripherals to raise later.
                RouteInterrupt((int)c.Argument(0), (int)c.Argument(1), (int)c.Argument(2));
                return Return(c, 0);
            });

            // RF calibration's internal I2C (no radio hardware here) and newlib's ROM syscall stubs
            foreach (var name in new[] { "esp_rom_regi2c_read", "esp_rom_regi2c_read_mask", "rom_i2c_readReg", "rom_i2c_readReg_Mask" }) Rom(name, c => Return(c, 0));
            foreach (var name in new[] { "esp_rom_regi2c_write", "esp_rom_regi2c_write_mask", "rom_i2c_writeReg", "rom_i2c_writeReg_Mask" }) Rom(name, c => Return(c, 0));
            foreach (var name in new[] { "open", "read", "write", "close" }) Rom(name, c => Return(c, unchecked((uint)-1)));

            BindGpioRom();

            // The caches and the flash MMU: the flash is mapped straight into IROM/DROM here, so every cache and MMU
            // routine (whatever its ROM variant's name) succeeds without doing anything.
            foreach (var pair in _romNames)
            {
                if (_romFunctions.ContainsKey(pair.Key)) continue;
                string n = pair.Value;
                // eFuse flash pins (0: the default SPI pins) and the ROM's own SPI flash helpers (the driver talks to SPI1 itself).
                if (n.StartsWith("Cache_", StringComparison.Ordinal) || n.StartsWith("cache_", StringComparison.Ordinal) || n.StartsWith("mmu_", StringComparison.Ordinal)
                    || n.StartsWith("esp_rom_efuse_", StringComparison.Ordinal) || n.StartsWith("ets_efuse_", StringComparison.Ordinal)
                    || n.StartsWith("esp_rom_spiflash_", StringComparison.Ordinal) || n.StartsWith("SPI_", StringComparison.Ordinal) || n.StartsWith("spi_flash_", StringComparison.Ordinal)
                    || n.StartsWith("esp_rom_output_", StringComparison.Ordinal) || n.StartsWith("esp_rom_uart_", StringComparison.Ordinal)
                    || n.StartsWith("gpio_matrix_", StringComparison.Ordinal) || n.StartsWith("gpio_pad_", StringComparison.Ordinal) || n.StartsWith("esp_rom_gpio_", StringComparison.Ordinal))
                    _romFunctions[pair.Key] = c => Return(c, 0);
            }
        }

        /// <summary>A reflected CRC of (crc, buffer, length), complemented before and after as esp_rom_crc*_le.</summary>
        private uint Crc(XtensaCore c, uint poly, int bits)
        {
            uint mask = bits == 32 ? 0xFFFFFFFFu : (1u << bits) - 1;
            uint crc = ~c.Argument(0) & mask, buffer = c.Argument(1), length = c.Argument(2);
            for (uint i = 0; i < length; i++)
            {
                crc ^= Read8(buffer + i);
                for (int b = 0; b < 8; b++) crc = (crc & 1) != 0 ? (crc >> 1) ^ poly : crc >> 1;
            }

            return ~crc & mask;
        }

        private bool Copy(XtensaCore c, uint destination, uint source, uint count)
        {
            var buffer = new byte[count];
            for (uint i = 0; i < count; i++) buffer[i] = Read8(source + i);
            for (uint i = 0; i < count; i++) Write8(destination + i, buffer[i]);
            return Return(c, destination);
        }

        /// <summary>A zero-terminated string in the chip's memory (reports, panics).</summary>
        public string ReadString(uint address, int max = 512) => CString(address, max);

        private string CString(uint address, int max = 4096)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < max; i++)
            {
                byte b = Read8(address + (uint)i);
                if (b == 0) break;
                sb.Append((char)b);
            }

            return sb.ToString();
        }

        /// <summary>A printf of the ROM (%d %u %x %X %s %c %p, width and zero padding), its arguments from the callee's registers; to UART0.</summary>
        private uint Print(XtensaCore c)
        {
            string format = CString(c.Argument(0));
            var sb = new StringBuilder();
            int arg = 1;
            for (int i = 0; i < format.Length; i++)
            {
                char ch = format[i];
                if (ch != '%' || i + 1 >= format.Length)
                {
                    sb.Append(ch);
                    continue;
                }

                i++;
                bool zero = false;
                if (format[i] == '0')
                {
                    zero = true;
                    i++;
                }

                int width = 0;
                while (i < format.Length && char.IsDigit(format[i])) width = width * 10 + (format[i++] - '0');
                while (i < format.Length && format[i] == 'l') i++;
                if (i >= format.Length) break;
                uint v = arg <= 5 ? c.Argument(arg) : 0;
                string text = format[i] switch
                {
                    'd' or 'i' => ((int)v).ToString(),
                    'u' => v.ToString(),
                    'x' => v.ToString("x"),
                    'X' => v.ToString("X"),
                    'p' => "0x" + v.ToString("x8"),
                    'c' => ((char)v).ToString(),
                    's' => CString(v),
                    '%' => "%",
                    _ => string.Empty,
                };
                if (format[i] != '%') arg++;
                sb.Append(text.PadLeft(width, zero ? '0' : ' '));
            }

            Uart0.Append(sb);
            return (uint)sb.Length;
        }
    }
}
