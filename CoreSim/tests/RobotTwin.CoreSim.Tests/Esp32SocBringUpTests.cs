using System;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Firmware.Xtensa;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [esp32emu] Bringing up a real firmware on the emulated chip: RW_ESP32_ELF=path to an application ELF built by the
    /// hardware-compatible mode (--esp32-build). The run's report (why it stopped, where, what it printed, the registers
    /// nothing models yet) goes to RW_ESP32_REPORT, or beside the ELF. Skipped when no ELF is given.
    /// </summary>
    public class Esp32SocBringUpTests
    {
        public sealed class ElfFactAttribute : FactAttribute
        {
            public ElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_ELF"))) Skip = "RW_ESP32_ELF verilmedi.";
            }
        }

        [ElfFact]
        public void BootsARealFirmware()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_ELF")!;
            long cycles = long.TryParse(Environment.GetEnvironmentVariable("RW_ESP32_CYCLES"), out var c) ? c : 50_000_000;
            var soc = new Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            // The board's whole flash (bootloader, partition table, application) when the build's merged image is beside the ELF.
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            soc.Pro.BreakOnIllegal = soc.App.BreakOnIllegal = Environment.GetEnvironmentVariable("RW_ESP32_BREAK_ILLEGAL") == "1";
            soc.Run(cycles);
            var report = new StringBuilder();
            report.AppendLine($"durdu: {soc.Stopped ?? (soc.Pro.AtBreak ? "BREAK" : "hayır")}");
            report.AppendLine($"PRO pc 0x{soc.Pro.Pc:X8} · döngü {soc.Pro.Cycles} · bekliyor {soc.Pro.Waiting} · son istisna {soc.Pro.LastException} · tanınmayan komut {soc.Pro.IllegalInstructions} (son 0x{soc.Pro.LastIllegalPc:X8})");
            report.AppendLine($"APP çalışıyor {soc.AppRunning} · pc 0x{soc.App.Pc:X8}");
            report.AppendLine($"PRO a2 dizgesi: {soc.ReadString(soc.Pro.A(2), 300)}");
            report.AppendLine("---- UART0 ----");
            report.AppendLine(soc.Uart0.ToString());
            report.AppendLine("---- son sıçramalar (eskiden yeniye) ----");
            foreach (var (from, to) in soc.Pro.RecentJumps()) report.AppendLine($"0x{from:X8} -> 0x{to:X8}");
            report.AppendLine("---- modellenmemiş yazmaçlar (en çok okunan önce) ----");
            foreach (var pair in soc.Unmodelled.OrderByDescending(p => p.Value.Reads).ThenByDescending(p => p.Value.Writes).Take(40))
                report.AppendLine($"0x{pair.Key:X8}  okuma {pair.Value.Reads,8}  yazma {pair.Value.Writes,6}  son 0x{pair.Value.Last:X8}");
            string path = Environment.GetEnvironmentVariable("RW_ESP32_REPORT") ?? Path.ChangeExtension(elf, ".bringup.txt");
            File.WriteAllText(path, report.ToString());
        }
    }
}
