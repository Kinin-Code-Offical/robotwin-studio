using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware.Debug
{
    /// <summary>[debug] How far a source-level step goes.</summary>
    public enum DebugStepKind
    {
        /// <summary>To the next line of the same function (calls run through).</summary>
        Over,

        /// <summary>To the next line of the user's code wherever it is (into calls).</summary>
        Into,

        /// <summary>Until the current function has returned.</summary>
        Out,
    }

    /// <summary>[debug] Why the CPU stopped.</summary>
    public enum DebugStopReason
    {
        None,
        Breakpoint,
        Step,
    }

    /// <summary>The emulated CPU asks this before every instruction while a debugger watches it.</summary>
    internal interface IMcuDebugHook
    {
        bool ShouldStop(int wordPc);
    }

    /// <summary>
    /// [debug] Breakpoints and source-level stepping for an emulated ATmega328P. The CPU stops <em>before</em> the
    /// instruction a breakpoint is on, at an exact cycle; whoever runs it (the circuit or the world) then stops at the
    /// same simulated instant, so the circuit, the mechanics and the code are all seen at one moment. With no
    /// breakpoint and no step pending the CPU is not watched at all and runs at full speed.
    /// </summary>
    public sealed class McuDebugger : IMcuDebugHook
    {
        private readonly Atmega328P _mcu;
        private bool[]? _breaks;
        private int _skipWord = -1;
        private DebugStepKind? _step;
        private int _stepSp, _stepLine;
        private short _stepFile;

        public McuDebugger(Atmega328P mcu, FirmwareDebugInfo? info)
        {
            _mcu = mcu;
            Info = info;
        }

        /// <summary>The program's debug information (null: the program was not built here, so lines are unknown).</summary>
        public FirmwareDebugInfo? Info { get; }

        public Atmega328P Mcu => _mcu;

        public bool IsHalted => _mcu.Halted;

        public DebugStopReason Reason { get; private set; }

        /// <summary>The source line the CPU stopped before (or would run next).</summary>
        public SourcePlace? Place => Info?.Locate(_mcu.ProgramCounter);

        public string? Function => Info?.FunctionAt(_mcu.ProgramCounter);

        /// <summary>Program (byte) addresses to stop before; an empty list removes them all.</summary>
        public void SetBreakpoints(IEnumerable<int> byteAddresses)
        {
            bool[]? words = null;
            foreach (int a in byteAddresses)
            {
                int w = a / 2;
                if (w < 0 || w >= Atmega328P.FlashWords) continue;
                words ??= new bool[Atmega328P.FlashWords];
                words[w] = true;
            }

            _breaks = words;
            Hook();
        }

        /// <summary>Lets a stopped CPU go on (the instruction it stopped before runs first, without stopping again).</summary>
        public void Resume()
        {
            if (!_mcu.Halted) return;
            _step = null;
            _skipWord = _mcu.ProgramCounter / 2;
            Reason = DebugStopReason.None;
            _mcu.Halted = false;
            Hook();
        }

        /// <summary>
        /// Runs to the next line of the user's code (<see cref="DebugStepKind"/>), from a stop or from anywhere the
        /// program is. False when the program has no line information.
        /// </summary>
        public bool Step(DebugStepKind kind)
        {
            if (Info == null) return false;
            int w = _mcu.ProgramCounter / 2;
            var place = Info.Locate(_mcu.ProgramCounter);
            _stepLine = place is { InSketch: true } p ? p.Line : -1;
            _stepFile = -1;
            if (place is { InSketch: true } q)
            {
                for (short i = 0; i < Info.SketchFiles.Count; i++)
                {
                    if (string.Equals(Info.SketchFiles[i], q.File, System.StringComparison.OrdinalIgnoreCase)) _stepFile = i;
                }
            }

            _stepSp = _mcu.StackPointer;
            _step = kind;
            _skipWord = _mcu.Halted ? w : -1;
            Reason = DebugStopReason.None;
            _mcu.Halted = false;
            Hook();
            return true;
        }

        /// <summary>Stops at the next line of the user's code (a "pause" that lands on a line).</summary>
        public bool BreakAtNextLine()
        {
            if (!Step(DebugStepKind.Into)) return false;
            _stepLine = -1;
            return true;
        }

        private void Hook() => _mcu.DebugHook = _breaks != null || _step != null || _skipWord >= 0 ? this : null;

        bool IMcuDebugHook.ShouldStop(int wordPc)
        {
            if (wordPc == _skipWord)
            {
                _skipWord = -1;
                if (_breaks == null && _step == null) _mcu.DebugHook = null;
                return false;
            }

            if (_breaks != null && _breaks[wordPc])
            {
                Reason = DebugStopReason.Breakpoint;
                _step = null;
                return true;
            }

            if (_step is { } kind && Info != null)
            {
                var lines = Info.StatementLineByWord;
                if (wordPc >= lines.Length || lines[wordPc] <= 0) return false;
                bool otherLine = lines[wordPc] != _stepLine || Info.StatementFileByWord[wordPc] != _stepFile;
                int sp = _mcu.StackPointer;   // the AVR's stack grows down: a deeper call has a smaller SP
                bool stop = kind switch
                {
                    DebugStepKind.Into => otherLine,
                    DebugStepKind.Over => otherLine && sp >= _stepSp,
                    _ => sp > _stepSp,
                };

                if (stop)
                {
                    Reason = DebugStopReason.Step;
                    _step = null;
                    return true;
                }
            }

            return false;
        }
    }
}
