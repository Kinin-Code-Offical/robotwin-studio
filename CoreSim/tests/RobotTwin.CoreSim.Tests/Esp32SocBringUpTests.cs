using System;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Firmware.Debug;
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
            bool profile = Environment.GetEnvironmentVariable("RW_ESP32_PROFILE") == "1";
            if (Environment.GetEnvironmentVariable("RW_ESP32_ROUTES") == "1") soc.RouteLog = new System.Collections.Generic.List<(int, int, int, uint)>();
            // RW_ESP32_PAD=34=1.0,35=2.5: pad voltages for the ADCs (a pot's wiper …); others read 0 V.
            var pads = new System.Collections.Generic.Dictionary<int, double>();
            foreach (string item in (Environment.GetEnvironmentVariable("RW_ESP32_PAD") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = item.Split('=');
                if (kv.Length == 2 && int.TryParse(kv[0], out int gpio) && double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) pads[gpio] = v;
            }

            soc.PadVoltage = gpio => pads.TryGetValue(gpio, out double v) ? v : 0;
            if (profile)
            {
                soc.Pro.JumpTargets = new System.Collections.Generic.Dictionary<ulong, long>();
                soc.App.JumpTargets = new System.Collections.Generic.Dictionary<ulong, long>();
            }

            // RW_ESP32_SEND=text and RW_ESP32_SEND_AT=cycles: the text goes to UART0's receive line at that point of the run.
            string? send = Environment.GetEnvironmentVariable("RW_ESP32_SEND");
            long sendAt = long.TryParse(Environment.GetEnvironmentVariable("RW_ESP32_SEND_AT"), out var at) ? Math.Min(at, cycles) : cycles / 2;
            if (!string.IsNullOrEmpty(send))
            {
                soc.Run(sendAt);
                soc.SendToUart0(Encoding.UTF8.GetBytes(send.Replace("\\n", "\n")));   // "\n" typed in the variable is a newline
                soc.Run(cycles - sendAt);
            }
            else
            {
                soc.Run(cycles);
            }

            var report = new StringBuilder();
            report.AppendLine($"durdu: {soc.Stopped ?? (soc.Pro.AtBreak ? "BREAK" : "hayır")}");
            report.AppendLine($"yonga zamanı {soc.Seconds * 1000:F3} ms · CPU {soc.CpuHz / 1e6:F0} MHz · APB {soc.ApbHz / 1e6:F0} MHz");
            report.AppendLine($"PRO pc 0x{soc.Pro.Pc:X8} · döngü {soc.Pro.Cycles} · bekliyor {soc.Pro.Waiting} · son istisna {soc.Pro.LastException} · tanınmayan komut {soc.Pro.IllegalInstructions} (son 0x{soc.Pro.LastIllegalPc:X8})");
            report.AppendLine($"APP çalışıyor {soc.AppRunning} · pc 0x{soc.App.Pc:X8}");
            report.AppendLine($"PRO a2 dizgesi: {soc.ReadString(soc.Pro.A(2), 300)}");
            foreach (var core in new[] { soc.Pro, soc.App })
                report.AppendLine($"çekirdek {core.CoreId}: pc 0x{core.Pc:X8} a2 0x{core.A(2):X8} [a2] 0x{soc.Read32(core.A(2) & ~3u):X8} 0x{soc.Read32((core.A(2) & ~3u) + 4):X8} a3 0x{core.A(3):X8} ps 0x{core.Ps:X8}");
            // RW_ESP32_PEEK=name,name…: the words at those symbols (the firmware's own variables) after the run.
            string? peek = Environment.GetEnvironmentVariable("RW_ESP32_PEEK");
            if (!string.IsNullOrEmpty(peek))
            {
                var symbols = ElfImage.Read(File.ReadAllBytes(elf)).Symbols;
                foreach (string name in peek.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var found = symbols.FirstOrDefault(s => s.Name == name);
                    report.AppendLine(found.Name == null ? $"{name}: yok" : $"{name} @0x{found.Value:X8} = 0x{soc.Read32(found.Value & ~3u):X8} 0x{soc.Read32((found.Value & ~3u) + 4):X8}");
                }
            }

            for (int ch = 0; ch < 16; ch++)
                if (soc.LedcOutput(ch) is { } pwm) report.AppendLine($"LEDC kanal {ch}: {pwm.Frequency:F1} Hz · görev %{pwm.Duty * 100:F2}");
            for (int gpio = 0; gpio < 40; gpio++)
            {
                var pad = soc.PadDrive(gpio);
                if (!pad.Driven && !pad.PullUp && !pad.PullDown) continue;
                string what = !pad.Driven ? "sürülmüyor" : pad.IsPwm ? $"PWM {pad.PwmFrequency:F1} Hz %{pad.PwmDuty * 100:F2}" : pad.Level ? "YÜKSEK" : "düşük";
                report.AppendLine($"GPIO{gpio}: {what}{(pad.PullUp ? " · yukarı çekme" : "")}{(pad.PullDown ? " · aşağı çekme" : "")}{(pad.OpenDrain ? " · açık akaç" : "")}");
            }
            foreach (var core in new[] { soc.Pro, soc.App })
                report.AppendLine($"çekirdek {core.CoreId} kesmeler: INTENABLE 0x{core.IntEnable:X8} INTERRUPT 0x{core.Interrupt:X8} dış 0x{core.ExternalLevels:X8}");
            var held = Enumerable.Range(0, 69).Where(soc.PeripheralLevel).Select(s => $"{s}→PRO {soc.InterruptRoute(0, s)}/APP {soc.InterruptRoute(1, s)}");
            report.AppendLine("tutulan kaynaklar: " + string.Join(", ", held));
            report.AppendLine($"UART0: INT_RAW 0x{soc.Read32(0x3FF40004):X8} INT_ENA 0x{soc.Read32(0x3FF4000C):X8} CONF0 0x{soc.Read32(0x3FF40020):X8} CONF1 0x{soc.Read32(0x3FF40024):X8} kaynak 34 → PRO {soc.InterruptRoute(0, 34)}/APP {soc.InterruptRoute(1, 34)}");
            if (soc.RouteLog != null)
            {
                report.AppendLine("---- kesme yönlendirmeleri (çekirdek, kaynak → kesme, pc) ----");
                foreach (var r in soc.RouteLog) report.AppendLine($"{r.Cpu} {r.Source,2} → {r.Interrupt,2}  pc 0x{r.Pc:X8}");
            }

            report.AppendLine("---- UART0 ----");
            report.AppendLine(soc.Uart0.ToString());
            report.AppendLine("---- son sıçramalar (eskiden yeniye) ----");
            foreach (var (from, to) in soc.Pro.RecentJumps()) report.AppendLine($"0x{from:X8} -> 0x{to:X8}");
            report.AppendLine("---- APP son sıçramalar (eskiden yeniye) ----");
            foreach (var (from, to) in soc.App.RecentJumps()) report.AppendLine($"0x{from:X8} -> 0x{to:X8}");
            report.AppendLine("---- modellenmemiş yazmaçlar (en çok okunan önce) ----");
            foreach (var pair in soc.Unmodelled.OrderByDescending(p => p.Value.Reads).ThenByDescending(p => p.Value.Writes).Take(40))
                report.AppendLine($"0x{pair.Key:X8}  okuma {pair.Value.Reads,8}  yazma {pair.Value.Writes,6}  son 0x{pair.Value.Last:X8}  pc 0x{pair.Value.Pc:X8} (çekirdek {pair.Value.Core})");
            if (profile)
            {
                // Function entries each core reached, most called first, with their main callers: what the firmware spent
                // its run doing, and who asked.
                var functions = new System.Collections.Generic.Dictionary<uint, string>();
                foreach (var symbol in ElfImage.Read(File.ReadAllBytes(elf)).Symbols)
                    if (symbol.IsFunction && !functions.ContainsKey(symbol.Value)) functions[symbol.Value] = symbol.Name;
                var starts = functions.Keys.OrderBy(a => a).ToArray();
                string within(uint address)
                {
                    int i = Array.BinarySearch(starts, address);
                    if (i < 0) i = ~i - 1;
                    return i >= 0 ? $"{functions[starts[i]]}+0x{address - starts[i]:X}" : $"0x{address:X8}";
                }

                foreach (var core in new[] { soc.Pro, soc.App })
                {
                    report.AppendLine($"---- çekirdek {core.CoreId} çağrılan işlevler (çağıranlar) ----");
                    var calls = core.JumpTargets!.Where(e => functions.ContainsKey((uint)e.Key)).GroupBy(e => (uint)e.Key);
                    foreach (var callee in calls.OrderByDescending(g => g.Sum(e => e.Value)).Take(400))
                    {
                        var callers = callee.GroupBy(e => within((uint)(e.Key >> 32))).Select(g => (Name: g.Key, Count: g.Sum(e => e.Value)))
                            .OrderByDescending(c => c.Count).Take(3).Select(c => $"{c.Name} {c.Count}");
                        report.AppendLine($"{callee.Sum(e => e.Value),10}  {functions[callee.Key]}  ← {string.Join(", ", callers)}");
                    }
                }
            }

            string path = Environment.GetEnvironmentVariable("RW_ESP32_REPORT") ?? Path.ChangeExtension(elf, ".bringup.txt");
            File.WriteAllText(path, report.ToString());
        }
    }
}
