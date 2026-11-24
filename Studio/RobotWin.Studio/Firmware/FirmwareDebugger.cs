using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Debug;

namespace RobotWin.Studio.Firmware
{
    /// <summary>
    /// [debug] The debug information of programs built here, found by their HEX: the ELF avr-gcc wrote beside it in the
    /// build folder. A program built elsewhere (or a built-in example's HEX) has none until it is built here.
    /// </summary>
    public static class DebugSymbols
    {
        private static readonly Dictionary<string, FirmwareDebugInfo?> Known = new Dictionary<string, FirmwareDebugInfo?>(StringComparer.Ordinal);

        private static string Key(string hex) => RobotTwin.CoreSim.Formats.FirmwareSource.HashOf(hex.Replace("\r", string.Empty));

        /// <summary>Notes a fresh build's ELF (called after a successful compile).</summary>
        public static void Remember(string hex, byte[]? elf)
        {
            if (elf == null) return;
            try
            {
                Known[Key(hex)] = FirmwareDebugInfo.FromElf(elf);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IndexOutOfRangeException || ex is ArgumentException)
            {
                Known[Key(hex)] = null;
            }
        }

        /// <summary>The debug information of a program, or null when it was not built here.</summary>
        public static FirmwareDebugInfo? For(string hex)
        {
            string key = Key(hex);
            if (Known.TryGetValue(key, out var info) && info != null) return info;
            // Built in an earlier run of the app: the build folders keep each board's last HEX and ELF.
            try
            {
                if (Directory.Exists(ArduinoCompiler.BuildRoot))
                {
                    foreach (var dir in Directory.EnumerateDirectories(ArduinoCompiler.BuildRoot))
                    {
                        string h = Path.Combine(dir, "out", "sketch.ino.hex"), e = Path.Combine(dir, "out", "sketch.ino.elf");
                        if (!File.Exists(h) || !File.Exists(e) || Key(File.ReadAllText(h)) != key) continue;
                        Remember(hex, File.ReadAllBytes(e));
                        return Known.TryGetValue(key, out info) ? info : null;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            return null;
        }
    }

    /// <summary>[debug] A running program the debugger can stop: one emulated MCU of a running circuit or world.</summary>
    public sealed class DebugProgram
    {
        internal DebugProgram(object owner, string key, string where, FirmwareCoSimulation cosim, Func<double> time, Action resume)
        {
            Owner = owner;
            Key = key;
            Where = where;
            Cosim = cosim;
            Time = time;
            ResumeOwner = resume;
        }

        /// <summary>The session it runs in (a world or the circuit).</summary>
        public object Owner { get; }

        /// <summary>The code page's key for its board ("w:O1", "d:O3:U1", "p:U1").</summary>
        public string Key { get; }

        public string Where { get; }
        public FirmwareCoSimulation Cosim { get; }
        public McuDebugger Debugger => Cosim.Debugger!;
        public FirmwareDebugInfo? Info => Cosim.Debugger?.Info;
        public bool IsHalted => Cosim.Halted;

        /// <summary>Simulated time of the session.</summary>
        public Func<double> Time { get; }

        internal Action ResumeOwner { get; }
    }

    /// <summary>
    /// [debug] Breakpoints by board and source line, kept while the app runs, and the programs they apply to. When a
    /// program reaches one, its session (circuit or world) stops at that very instant — the circuit, the mechanics and
    /// the code are all seen at one moment — and <see cref="Stopped"/> tells the code page which line it is.
    /// </summary>
    public static class FirmwareDebugger
    {
        /// <summary>The build names the sketch's main file "sketch.ino" whatever the editor calls it.</summary>
        public const string MainFile = "sketch.ino";

        private static readonly Dictionary<string, Dictionary<string, SortedSet<int>>> Breaks = new Dictionary<string, Dictionary<string, SortedSet<int>>>(StringComparer.Ordinal);
        private static readonly List<DebugProgram> Programs = new List<DebugProgram>();

        /// <summary>Breakpoints changed, a program came or went, or one stopped or went on.</summary>
        public static event Action? Changed;

        /// <summary>A program stopped at a breakpoint or after a step (its session is paused).</summary>
        public static event Action<DebugProgram>? Stopped;

        public static IReadOnlyList<DebugProgram> Running => Programs;

        /// <summary>The lines with a breakpoint in a board's file (build file names: <see cref="MainFile"/> for the main one).</summary>
        public static IReadOnlyCollection<int> LinesOf(string key, string file) =>
            Breaks.TryGetValue(key, out var files) && files.TryGetValue(file, out var lines) ? lines : (IReadOnlyCollection<int>)Array.Empty<int>();

        public static int CountOf(string key) => Breaks.TryGetValue(key, out var files) ? files.Values.Sum(l => l.Count) : 0;

        public static void Toggle(string key, string file, int line)
        {
            if (!Breaks.TryGetValue(key, out var files)) Breaks[key] = files = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
            if (!files.TryGetValue(file, out var lines)) files[file] = lines = new SortedSet<int>();
            if (!lines.Remove(line)) lines.Add(line);
            Apply(key);
            Changed?.Invoke();
        }

        public static void ClearAll(string key)
        {
            Breaks.Remove(key);
            Apply(key);
            Changed?.Invoke();
        }

        /// <summary>
        /// Where a breakpoint on <paramref name="line"/> really stops in the running program (a line without code moves to
        /// the next one that has some): the line, −1 when it can stop nowhere, 0 when no running program can tell.
        /// </summary>
        public static int Resolve(string key, string file, int line)
        {
            var info = Programs.FirstOrDefault(p => p.Key == key && p.Info != null)?.Info;
            return info == null ? 0 : info.Breakpoint(file, line).Line;
        }

        public static DebugProgram? ProgramFor(string key) => Programs.FirstOrDefault(p => p.Key == key && p.IsHalted) ?? Programs.FirstOrDefault(p => p.Key == key);

        public static DebugProgram? Halted => Programs.FirstOrDefault(p => p.IsHalted);

        /// <summary>A session starts running a program (it is watched only while it has breakpoints or a step pending).</summary>
        public static DebugProgram Register(object owner, string key, string where, FirmwareCoSimulation cosim, string hex, Func<double> time, Action resume)
        {
            cosim.AttachDebugger(DebugSymbols.For(hex));
            var p = new DebugProgram(owner, key, where, cosim, time, resume);
            Programs.Add(p);
            ApplyTo(p);
            Changed?.Invoke();
            return p;
        }

        /// <summary>A session stopped: its programs are gone.</summary>
        public static void Unregister(object owner)
        {
            if (Programs.RemoveAll(p => p.Owner == owner) > 0) Changed?.Invoke();
        }

        /// <summary>Called by a session that stopped because one of its programs did.</summary>
        public static void OnHalted(FirmwareCoSimulation cosim)
        {
            var p = Programs.FirstOrDefault(x => x.Cosim == cosim);
            if (p == null) return;
            Changed?.Invoke();
            Stopped?.Invoke(p);
        }

        /// <summary>A paused session goes on: its stopped programs go on with it.</summary>
        public static void ReleaseHalts(object owner)
        {
            bool any = false;
            foreach (var p in Programs.Where(x => x.Owner == owner && x.IsHalted))
            {
                p.Debugger.Resume();
                any = true;
            }

            if (any) Changed?.Invoke();
        }

        public static void Continue(DebugProgram p)
        {
            p.Debugger.Resume();
            p.ResumeOwner();
            Changed?.Invoke();
        }

        /// <summary>One source line (over calls, into them, or out of the function); false without line information.</summary>
        public static bool Step(DebugProgram p, DebugStepKind kind)
        {
            if (!p.Debugger.Step(kind)) return false;
            p.ResumeOwner();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Stops the program at its next line of the user's code.</summary>
        public static bool BreakNow(DebugProgram p)
        {
            if (!p.Debugger.BreakAtNextLine()) return false;
            p.ResumeOwner();
            Changed?.Invoke();
            return true;
        }

        private static void Apply(string key)
        {
            foreach (var p in Programs.Where(x => x.Key == key)) ApplyTo(p);
        }

        private static void ApplyTo(DebugProgram p)
        {
            var info = p.Info;
            var addresses = new List<int>();
            if (info != null && Breaks.TryGetValue(p.Key, out var files))
            {
                foreach (var (file, lines) in files)
                {
                    foreach (int line in lines) addresses.AddRange(info.Breakpoint(file, line).Addresses);
                }
            }

            p.Debugger.SetBreakpoints(addresses);
        }
    }
}
