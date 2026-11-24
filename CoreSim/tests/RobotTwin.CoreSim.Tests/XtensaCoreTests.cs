using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Firmware.Xtensa;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// A [Fact] skipped when the ESP32 toolchain (installed by the studio's Code → Real board) is not on this machine:
    /// the program is assembled by Espressif's own xtensa-esp32-elf-as, so the encodings are the real ones.
    /// </summary>
    public sealed class XtensaAsmFactAttribute : FactAttribute
    {
        public XtensaAsmFactAttribute()
        {
            if (XtensaAsm.Tools == null) Skip = "ESP32 araç zinciri yok (xtensa-esp32-elf-as): Kod → Gerçek kart → ESP32 araç zincirini kur.";
        }
    }

    /// <summary>A flat RAM: code at 0x40080000 (IRAM), data and stack at 0x3FFB0000 (DRAM), 64 KB each.</summary>
    internal sealed class TestBus : IXtensaBus
    {
        public const uint CodeBase = 0x40080000, DataBase = 0x3FFB0000;
        public readonly byte[] Code = new byte[0x10000], Data = new byte[0x10000];

        private byte[] Region(uint address, out uint offset)
        {
            if (address >= CodeBase && address < CodeBase + 0x10000)
            {
                offset = address - CodeBase;
                return Code;
            }

            if (address >= DataBase && address < DataBase + 0x10000)
            {
                offset = address - DataBase;
                return Data;
            }

            throw new InvalidOperationException($"bus error at 0x{address:X8}");
        }

        public byte Read8(uint a) => Region(a, out var o)[o];
        public ushort Read16(uint a) { var m = Region(a, out var o); return (ushort)(m[o] | m[o + 1] << 8); }
        public uint Read32(uint a) { var m = Region(a, out var o); return BitConverter.ToUInt32(m, (int)o); }
        public void Write8(uint a, byte v) => Region(a, out var o)[o] = v;
        public void Write16(uint a, ushort v) { var m = Region(a, out var o); m[o] = (byte)v; m[o + 1] = (byte)(v >> 8); }
        public void Write32(uint a, uint v) { var m = Region(a, out var o); BitConverter.GetBytes(v).CopyTo(m, (int)o); }
    }

    /// <summary>Assembles a test body with the real assembler, behind standard window vectors and a start-up that turns windows on.</summary>
    internal static class XtensaAsm
    {
        public static string? Tools { get; } = FindTools();

        private static string? FindTools()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobotWinStudio", "toolchains", "arduino-esp32", "data", "packages", "esp32", "tools", "esp-x32");
            if (!Directory.Exists(root)) return null;
            return Directory.GetDirectories(root).Select(d => Path.Combine(d, "bin")).FirstOrDefault(b => File.Exists(Path.Combine(b, "xtensa-esp32-elf-as.exe")));
        }

        // The window overflow and underflow handlers of the standard windowed ABI (as ESP-IDF's), level-3 timer handler.
        private const string Vectors = @"
    .text
    .org 0x000
    s32e a0, a5, -16
    s32e a1, a5, -12
    s32e a2, a5, -8
    s32e a3, a5, -4
    rfwo
    .org 0x040
    l32e a0, a5, -16
    l32e a1, a5, -12
    l32e a2, a5, -8
    l32e a3, a5, -4
    rfwu
    .org 0x080
    s32e a0, a9, -16
    l32e a0, a1, -12
    s32e a2, a9, -8
    s32e a1, a9, -12
    s32e a3, a9, -4
    s32e a4, a0, -32
    s32e a5, a0, -28
    s32e a6, a0, -24
    s32e a7, a0, -20
    rfwo
    .org 0x0C0
    l32e a1, a9, -12
    l32e a0, a9, -16
    l32e a7, a1, -12
    l32e a2, a9, -8
    l32e a4, a7, -32
    l32e a3, a9, -4
    l32e a5, a7, -28
    l32e a6, a7, -24
    l32e a7, a7, -20
    rfwu
    .org 0x100
    s32e a0, a13, -16
    l32e a0, a1, -12
    s32e a1, a13, -12
    s32e a2, a13, -8
    s32e a3, a13, -4
    s32e a4, a0, -48
    s32e a5, a0, -44
    s32e a6, a0, -40
    s32e a7, a0, -36
    s32e a8, a0, -32
    s32e a9, a0, -28
    s32e a10, a0, -24
    s32e a11, a0, -20
    rfwo
    .org 0x140
    l32e a1, a13, -12
    l32e a0, a13, -16
    l32e a11, a1, -12
    l32e a2, a13, -8
    l32e a4, a11, -48
    l32e a8, a11, -32
    l32e a3, a13, -4
    l32e a5, a11, -44
    l32e a6, a11, -40
    l32e a7, a11, -36
    l32e a9, a11, -28
    l32e a10, a11, -24
    l32e a11, a11, -20
    rfwu
    .org 0x1C0
    wsr.excsave3 a3
    rsr.ccompare1 a3
    addmi a3, a3, 768
    wsr.ccompare1 a3
    rsr.misc0 a3
    addi a3, a3, 1
    wsr.misc0 a3
    rsr.excsave3 a3
    rfi 3
    .org 0x300
    rsr.exccause a2         # kernel exception: its cause into a2, then stop
    break 1, 2
    .org 0x400
    j .Lmain
    .literal_position
    .literal .Lstack, 0x3FFBF000
    .literal .Lvecbase, 0x40080000
    .literal .Lps, 0x00040000
    .align 4
.Lmain:
    l32r a1, .Lstack
    addi a2, a1, -16        # the outermost frame's caller pointer (sp-12) points at its own stack, as a task's start does
    s32i a1, a2, 4
    l32r a2, .Lvecbase
    wsr.vecbase a2
    l32r a2, .Lps
    wsr.ps a2
    rsync
";

        /// <summary>The program's bytes, to load at <see cref="TestBus.CodeBase"/> (its entry is at +0x400).</summary>
        public static byte[] Assemble(string body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "rw-xtensa-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string src = Path.Combine(dir, "t.S"), obj = Path.Combine(dir, "t.o"), bin = Path.Combine(dir, "t.bin");
                File.WriteAllText(src, Vectors + body + "\n    break 1, 1\n");
                Run("xtensa-esp32-elf-as.exe", $"--text-section-literals -o \"{obj}\" \"{src}\"");
                Run("xtensa-esp32-elf-objcopy.exe", $"-O binary -j .text \"{obj}\" \"{bin}\"");
                return File.ReadAllBytes(bin);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static void Run(string tool, string args)
        {
            var psi = new ProcessStartInfo(Path.Combine(Tools!, tool), args) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            string err = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException(tool + ": " + err);
        }

        /// <summary>Runs an assembled program to its BREAK (or the cycle limit); the core is returned for checks.</summary>
        public static (XtensaCore Core, TestBus Bus) Run(string body, long cycles = 5_000_000)
        {
            var bus = new TestBus();
            Assemble(body).CopyTo(bus.Code, 0);
            var core = new XtensaCore(bus);
            core.Reset(TestBus.CodeBase + 0x400);
            core.Run(cycles);
            Assert.True(core.AtBreak, $"BREAK'a varmadı: pc 0x{core.Pc:X8}, son istisna {core.LastException}, tanınmayan {core.IllegalInstructions}");
            return (core, bus);
        }
    }

    public class XtensaCoreTests
    {
        [Fact]
        public void DenseInstructionsRunWithoutAToolchain()
        {
            // movi.n a2, 5 ; add.n a3, a2, a2 ; break.n 0 — encoded by hand
            var bus = new TestBus();
            new byte[] { 0x0C, 0x52, 0x2A, 0x32, 0x2D, 0xF0 }.CopyTo(bus.Code, 0);
            var core = new XtensaCore(bus);
            core.Reset(TestBus.CodeBase);
            core.Run(100);
            Assert.True(core.AtBreak);
            Assert.Equal(5u, core.A(2));
            Assert.Equal(10u, core.A(3));
        }

        [XtensaAsmFact]
        public void IntegerArithmeticAndShifts()
        {
            var (c, _) = XtensaAsm.Run(@"
    movi a2, 1000
    movi a3, -7
    add a4, a2, a3          # 993
    addx4 a5, a3, a2        # -28 + 1000 = 972
    subx8 a6, a2, a3        # 8000 + 7 = 8007
    mull a7, a2, a3         # -7000
    quos a8, a7, a2         # -7
    rems a9, a7, a3         # 0
    movi a10, 0x123
    slli a10, a10, 20       # 0x12300000
    srai a11, a10, 8        # 0x00123000
    extui a12, a10, 20, 8   # 0x23
    nsau a13, a11           # 11
    sext a14, a12, 7        # 0x23 (bit 7 clear)
    movi a15, 300
    clamps a15, a15, 7      # 255 (8-bit signed: 127? no: clamps imm 7 -> -128..127)
");
            Assert.Equal(993u, c.A(4));
            Assert.Equal(972u, c.A(5));
            Assert.Equal(8007u, c.A(6));
            Assert.Equal(unchecked((uint)-7000), c.A(7));
            Assert.Equal(unchecked((uint)-7), c.A(8));
            Assert.Equal(0u, c.A(9));
            Assert.Equal(0x12300000u, c.A(10));
            Assert.Equal(0x00123000u, c.A(11));
            Assert.Equal(0x23u, c.A(12));
            Assert.Equal(11u, c.A(13));
            Assert.Equal(0x23u, c.A(14));
            Assert.Equal(127u, c.A(15));
        }

        [XtensaAsmFact]
        public void FunnelShiftsMinMaxAndConditionalMoves()
        {
            var (c, _) = XtensaAsm.Run(@"
    movi a2, 0x7AB
    movi a3, 12
    ssl a3
    sll a4, a2              # 0x7AB000
    ssr a3
    srl a5, a4              # 0x7AB
    movi a6, -100
    sra a7, a6              # -100 >> 12 = -1
    movi a8, 5
    min a9, a6, a8          # -100
    maxu a10, a6, a8        # 0xFFFFFF9C
    movi a11, 0
    movi a12, 42
    moveqz a11, a12, a11    # 42 (a11 was 0)
    mul16s a13, a6, a8      # -500
");
            Assert.Equal(0x7AB000u, c.A(4));
            Assert.Equal(0x7ABu, c.A(5));
            Assert.Equal(uint.MaxValue, c.A(7));
            Assert.Equal(unchecked((uint)-100), c.A(9));
            Assert.Equal(unchecked((uint)-100), c.A(10));
            Assert.Equal(42u, c.A(11));
            Assert.Equal(unchecked((uint)-500), c.A(13));
        }

        [XtensaAsmFact]
        public void LoadsStoresLiteralsAndAtomics()
        {
            var (c, bus) = XtensaAsm.Run(@"
    j .Lgo
    .literal_position
    .literal .Ldata, 0x3FFB0100
    .literal .Lbig, 0xDEADBEEF
    .align 4
.Lgo:
    l32r a2, .Ldata
    l32r a3, .Lbig
    s32i a3, a2, 0
    movi a4, -2
    s16i a4, a2, 4
    s8i a4, a2, 6
    l32i a5, a2, 0          # 0xDEADBEEF
    l16si a6, a2, 4         # -2
    l16ui a7, a2, 4         # 0xFFFE
    l8ui a8, a2, 6          # 0xFE
    wsr.scompare1 a5
    movi a9, 77
    s32c1i a9, a2, 0        # swaps: a9 = old 0xDEADBEEF, memory = 77
    l32i a10, a2, 0         # 77
");
            Assert.Equal(0xDEADBEEFu, c.A(5));
            Assert.Equal(unchecked((uint)-2), c.A(6));
            Assert.Equal(0xFFFEu, c.A(7));
            Assert.Equal(0xFEu, c.A(8));
            Assert.Equal(0xDEADBEEFu, c.A(9));
            Assert.Equal(77u, c.A(10));
            Assert.Equal(77u, bus.Read32(0x3FFB0100));
        }

        [XtensaAsmFact]
        public void BranchesZeroOverheadLoopAndDenseCode()
        {
            var (c, _) = XtensaAsm.Run(@"
    movi a2, 0
    movi a3, 10
    loop a3, .Lend
    addi.n a2, a2, 3        # 10 times
.Lend:
    movi a4, 0
    movi a5, 6
.Lcount:
    addi a4, a4, 1
    bnei a4, 6, .Lcount     # counts to 6
    movi a6, 0x40
    movi a7, 0
    bbsi a6, 6, .Lbit       # bit 6 set: taken
    movi a7, 1
.Lbit:
    movi a8, 3
    movi a9, 0
    bltu a8, a5, .Lless     # 3 < 6
    movi a9, 1
.Lless:
");
            Assert.Equal(30u, c.A(2));
            Assert.Equal(6u, c.A(4));
            Assert.Equal(0u, c.A(7));
            Assert.Equal(0u, c.A(9));
        }

        [XtensaAsmFact]
        public void WindowedRecursionSpillsAndReloadsFrames()
        {
            // fib(15) with CALL8 / ENTRY / RETW: deep enough that frames go through the overflow and underflow handlers.
            var (c, _) = XtensaAsm.Run(@"
    movi a10, 15
    call8 fib
    mov a2, a10
    j .Ldone
    .align 4
fib:
    entry a1, 32
    movi a3, 2
    blt a2, a3, .Lbase
    addi a10, a2, -1
    call8 fib
    mov a4, a10
    addi a10, a2, -2
    call8 fib
    add a2, a4, a10
.Lbase:
    retw
.Ldone:
");
            Assert.Equal(610u, c.A(2));
        }

        [XtensaAsmFact]
        public void TimerInterruptWakesWaiti()
        {
            // CCOMPARE1 raises interrupt 15 (level 3) every 768 cycles; the handler counts in MISC0.
            var (c, _) = XtensaAsm.Run(@"
    j .Lgo
    .literal_position
    .literal .Lint15, 0x8000
    .align 4
.Lgo:
    movi a2, 0
    wsr.misc0 a2
    rsr.ccount a2
    addmi a2, a2, 768
    wsr.ccompare1 a2
    l32r a2, .Lint15
    wsr.intenable a2
    movi a3, 5
.Lwait:
    waiti 0
    addi a3, a3, -1
    bnez a3, .Lwait
    rsr.misc0 a2
");
            Assert.Equal(5u, c.A(2));
        }

        [XtensaAsmFact]
        public void DivideByZeroRaisesItsException()
        {
            var (c, _) = XtensaAsm.Run(@"
    movi a3, 0
    movi a4, 9
    quos a5, a4, a3
");
            Assert.Equal((uint)XtensaCause.IntegerDivideByZero, c.A(2));
            Assert.Equal(TestBus.CodeBase + 0x303, c.Pc);   // stopped at the BREAK in the kernel vector
        }

        [XtensaAsmFact]
        public void FloatWithTheCoprocessorOffRaisesItsException()
        {
            // FreeRTOS enables the FPU lazily: a task's first float instruction lands here.
            var (c, _) = XtensaAsm.Run(@"
    movi a3, 3
    float.s f0, a3, 0
");
            Assert.Equal((uint)XtensaCause.Coprocessor0Disabled, c.A(2));
        }

        [XtensaAsmFact]
        public void FloatingPoint()
        {
            var (c, _) = XtensaAsm.Run(@"
    movi a2, 1
    wsr.cpenable a2
    rsync
    movi a3, 3
    float.s f0, a3, 0
    movi a4, 4
    float.s f1, a4, 0
    mul.s f2, f0, f1        # 12
    add.s f2, f2, f0        # 15
    movi a5, 2
    float.s f3, a5, 1       # 2 / 2 = 1
    madd.s f2, f3, f1       # 15 + 4 = 19
    trunc.s a6, f2, 0       # 19
    olt.s b0, f0, f1        # 3 < 4
    movi a7, 0
    movi a8, 1
    movt a7, a8, b0         # 1
    neg.s f4, f1
    round.s a9, f4, 1       # -8
");
            Assert.Equal(19u, c.A(6));
            Assert.Equal(1u, c.A(7));
            Assert.Equal(unchecked((uint)-8), c.A(9));
        }
    }
}
