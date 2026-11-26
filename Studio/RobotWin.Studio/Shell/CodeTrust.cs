using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using RobotTwin.CoreSim.Libraries;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [trust] As VS Code's workspace trust: a file Windows marked as coming from the internet or another computer (its
    /// Zone.Identifier stream) that carries code this PC would run directly — an FMU's native library, C# world scripts —
    /// asks once before that code runs. Trusting it unblocks the file as Windows' own "Unblock" does, so it is not asked
    /// again. ESP32 sketches run in their sandbox and Arduino (AVR) code in the emulator: they need no trust.
    /// </summary>
    public static class CodeTrust
    {
        private static readonly HashSet<string> Trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The code a circuit's parts would run on this PC: FMU parts (native libraries).</summary>
        public static List<string> CodeIn(RobotTwin.CoreSim.Design.Schematic schematic)
        {
            var found = new List<string>();
            foreach (string id in schematic.Parts.Select(p => p.PresetId).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var part = Libraries.LibraryManager.FindUserPart(id);
                if (part?.Definition?.Model.Kind == UserModelKind.Fmu) found.Add("FMU: " + part.Name);
            }

            return found;
        }

        /// <summary>The code a world would run on this PC: its enabled C# scripts.</summary>
        public static List<string> CodeIn(WorldDocument world) =>
            world.Sdk.Scripts.Where(s => s.Enabled).Select(s => Lang.T("C# betiği: ") + s.Name).ToList();

        /// <summary>
        /// True when the code may run: there is none, the file is this PC's own, it was trusted already, or the user trusts it
        /// now (the file is then unblocked). False: the user does not, and the simulation does not start.
        /// </summary>
        public static bool Confirm(Window? owner, string? path, IReadOnlyList<string> code)
        {
            if (code.Count == 0 || Unattended.Active || path == null || Trusted.Contains(path) || !ZoneMark.FromElsewhere(path)) return true;
            string list = string.Join("\n", code.Take(8).Select(c => "  • " + c)) + (code.Count > 8 ? "\n  • …" : string.Empty);
            string message = Lang.T("Bu dosya internetten ya da başka bir bilgisayardan geldi ve bilgisayarınızda doğrudan çalışacak kod içeriyor:") + "\n\n" + list + "\n\n" +
                             Lang.T("Bu kod dosyalarınıza ve ağa uygulamanın kendisi kadar erişebilir. ESP32 kodu korumalı alanda, Arduino kodu emülatörde çalışır; onlar için onay gerekmez. Kaynağına güveniyorsanız çalıştırın.");
            var window = owner ?? Application.Current?.MainWindow;
            if (window == null) return false;
            int choice = Dialog.Show(window, Lang.T("Bu dosyanın kaynağına güveniyor musunuz?"), message, Lang.T("Güven ve çalıştır"), Lang.T("Çalıştırma"));
            if (choice != 0) return false;
            Trusted.Add(path);
            ZoneMark.Unblock(path);
            return true;
        }
    }
}
