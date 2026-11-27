using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Firmware;
using RobotWin.Studio.Firmware;

namespace RobotWin.Studio.Pages
{
    /// <summary>[registers] One line of the Yazmaçlar tab: a register, or the heading of its peripheral's group.</summary>
    public sealed class RegisterRow
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Binary { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Flags { get; set; } = string.Empty;
        public string? Tip { get; set; }
        public Brush NameBrush { get; set; } = Shell.ThemeBrushes.Text;
        public Brush ValueBrush { get; set; } = Shell.ThemeBrushes.Text;
        public FontWeight Weight { get; set; } = FontWeights.Normal;
        public Thickness Spacing { get; set; } = new Thickness(0, 1, 0, 1);
    }

    /// <summary>
    /// [debug] What a view showed at the program's last distinct instant, to colour what changed since. Keyed by the
    /// cycle count: a stop refreshes the page twice (the debugger's change and its stop), and the second look at the same
    /// instant must still compare against the instant before, not against the first look.
    /// </summary>
    internal sealed class LookBack<T>
    {
        private string? _of;
        private ulong _at = ulong.MaxValue;
        private Dictionary<string, T> _before = new Dictionary<string, T>(StringComparer.Ordinal);
        private Dictionary<string, T> _now = new Dictionary<string, T>(StringComparer.Ordinal);

        /// <summary>The program and instant about to be shown; a new instant makes the last one the comparison.</summary>
        public void At(string program, ulong cycles)
        {
            if (program == _of && cycles == _at) return;
            _before = program == _of ? _now : new Dictionary<string, T>(StringComparer.Ordinal);
            _now = new Dictionary<string, T>(StringComparer.Ordinal);
            _of = program;
            _at = cycles;
        }

        /// <summary>Records <paramref name="value"/> and says whether it differs from the previous instant's.</summary>
        public bool Changed(string name, T value)
        {
            _now[name] = value;
            return _before.TryGetValue(name, out var before) && !EqualityComparer<T>.Default.Equals(before, value);
        }
    }

    /// <summary>
    /// [registers] The Yazmaçlar tab: the ATmega328P's core (PC, SP, SREG, X/Y/Z, R0–R31, cycles) and its peripherals'
    /// registers by name, as the program would read them at this instant (<see cref="Atmega328P.Peek"/>: looking takes no
    /// byte off the UART and latches no timer), with the bits that are set named. What changed since the last look is in
    /// colour, so a step shows what the line did to the hardware.
    /// </summary>
    public partial class CodePage
    {
        private readonly LookBack<byte> _registersSeen = new LookBack<byte>();

        private static string Hex2(int v) => "0x" + v.ToString("X2", CultureInfo.InvariantCulture);

        private static string Hex4(int v) => "0x" + v.ToString("X4", CultureInfo.InvariantCulture);

        private void ShowRegisters()
        {
            var p = DebugKey is { } key ? FirmwareDebugger.ProgramFor(key) : null;
            if (p == null)
            {
                CoreLine.Text = CycleLine.Text = string.Empty;
                GeneralRegisters.ItemsSource = null;
                RegistersList.ItemsSource = null;
                RegistersNote.Text = "Kart çalışırken ATmega328P'nin yazmaçları burada görünür; bir kesme noktasında durduğunda o anki değerleriyle.";
                return;
            }

            var mcu = p.Cosim.Mcu;
            var normal = (Brush)FindResource("B.Text");
            var changed = (Brush)FindResource("B.Warning");
            var quiet = (Brush)FindResource("B.TextSecondary");
            _registersSeen.At(p.Key, mcu.Cycles);
            bool Differs(string name, byte v) => _registersSeen.Changed(name, v);

            int Pair(int low) => mcu.Peek(low) | (mcu.Peek(low + 1) << 8);
            byte sreg = mcu.StatusRegister;
            CoreLine.Text = $"PC {Hex4(mcu.ProgramCounter)}   SP {Hex4(mcu.StackPointer)}   SREG {Hex2(sreg)} {AvrRegisters.Flags(sreg)}\n"
                + $"X  {Hex4(Pair(26))}   Y  {Hex4(Pair(28))}   Z  {Hex4(Pair(30))}";
            CycleLine.Text = $"{mcu.Cycles.ToString("N0", CultureInfo.CurrentCulture)} çevrim · t = {p.Time().ToString("0.000000", CultureInfo.CurrentCulture)} s";

            var general = new List<RegisterRow>(32);
            for (int r = 0; r < 32; r++)
            {
                byte v = mcu.Peek(r);
                general.Add(new RegisterRow
                {
                    Value = $"R{r,-2} {Hex2(v)}",
                    ValueBrush = Differs("R" + r, v) ? changed : normal,
                    Tip = $"R{r} = {v} ({(sbyte)v})",
                });
            }

            GeneralRegisters.ItemsSource = general;

            var rows = new List<RegisterRow>();
            foreach (var group in AvrRegisters.Atmega328P.GroupBy(x => x.Group))
            {
                rows.Add(new RegisterRow { Name = group.Key, NameBrush = quiet, Weight = FontWeights.SemiBold, Spacing = new Thickness(0, rows.Count == 0 ? 0 : 8, 0, 2) });
                foreach (var reg in group)
                {
                    byte v = mcu.Peek(reg.Address);
                    string flags = reg.SetBits(v);
                    if (AvrRegisters.LowHalf(reg) is { } low)
                    {
                        int word = mcu.Peek(low.Address) | (v << 8);
                        flags = $"{reg.Name.Substring(0, reg.Name.Length - 1)} = {word} ({Hex4(word)})";
                    }

                    rows.Add(new RegisterRow
                    {
                        Name = reg.Name,
                        Value = Hex2(v),
                        Binary = AvrRegister.Binary(v),
                        Address = Hex2(reg.Address),
                        Flags = flags,
                        Tip = reg.Bits == null
                            ? $"{reg.Name} = {v}"
                            : string.Join("  ", reg.Bits.Select((b, i) => b == "-" ? null : b + " " + ((v >> (7 - i)) & 1)).Where(s => s != null)),
                        NameBrush = normal,
                        ValueBrush = Differs(reg.Name, v) ? changed : normal,
                    });
                }
            }

            RegistersList.ItemsSource = rows;
            RegistersNote.Text = (p.IsHalted ? "Değerler kartın durduğu andan" : "Değerler çalışırken okunuyor")
                + "; turuncu: son bakıştan beri değişen. Bakmak donanımı etkilemez (UDR0'daki bayt alınmaz, sayaç kilitlenmez).";
        }

        /// <summary>[registers] For checks: the core line, the peripheral registers listed and how many values (R0–R31 and I/O) are coloured as changed.</summary>
        internal (string Core, int Registers, int Changed) RegistersShown()
        {
            ShowRegisters();
            var changed = (Brush)FindResource("B.Warning");
            var general = GeneralRegisters.ItemsSource as IEnumerable<RegisterRow> ?? Enumerable.Empty<RegisterRow>();
            var io = (RegistersList.ItemsSource as IEnumerable<RegisterRow> ?? Enumerable.Empty<RegisterRow>()).Where(r => r.Address.Length > 0).ToList();
            return (CoreLine.Text, io.Count, general.Concat(io).Count(r => ReferenceEquals(r.ValueBrush, changed)));
        }

        /// <summary>[registers] Brings the Yazmaçlar tab to the front.</summary>
        internal void ShowRegistersTab() => _dock?.Reveal("regs");
    }
}
