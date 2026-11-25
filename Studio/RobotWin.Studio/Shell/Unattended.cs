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
            "--snapshot", "--bench", "--selftest", "--gallery", "--export-logo", "--overlap-report", "--startup-report", "--perf-check",
        };

        /// <summary>--bench: measured like the app is used (a just-opened world's cables settle over frames, as for a user).</summary>
        public static bool Benchmarking { get; } = Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--bench", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// A path as a command-line run shows it: the user's profile folder written as %USERPROFILE%, so pictures made for
        /// the documentation carry no one's user name. In normal use the path is shown as it is.
        /// </summary>
        public static string Display(string path)
        {
            if (!Active || string.IsNullOrEmpty(path)) return path;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home.Length > 0 && path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "%USERPROFILE%" + path.Substring(home.Length) : path;
        }

        /// <summary>--startup-report: a normal start (splash included) that writes its timings and exits.</summary>
        public static bool MeasuringStartup { get; } = Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--startup-report", StringComparison.OrdinalIgnoreCase));

        public static bool Active { get; } = Environment.GetCommandLineArgs().Any(a => Flags.Contains(a, StringComparer.OrdinalIgnoreCase));
    }
}
