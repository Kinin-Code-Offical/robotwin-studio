using System;
using System.Linq;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// A run driven by the command line (snapshots, the frame-rate bench, the self-test, the model gallery …) works on
    /// its own: it starts from the default settings, panel layout and an empty recent-files list, and saves none of them,
    /// so the user's preferences, window position, recent files and backups are exactly as they were afterwards.
    /// </summary>
    internal static class Unattended
    {
        private static readonly string[] Flags =
        {
            "--snapshot", "--bench", "--selftest", "--gallery", "--export-logo", "--overlap-report",
        };

        public static bool Active { get; } = Environment.GetCommandLineArgs().Any(a => Flags.Contains(a, StringComparer.OrdinalIgnoreCase));
    }
}
