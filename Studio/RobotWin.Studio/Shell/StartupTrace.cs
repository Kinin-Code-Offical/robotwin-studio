using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [startup] What the app does between its start and its first drawn window, and when (milliseconds since the process
    /// started, the .NET runtime's own start included). The splash window shows each step as it happens;
    /// <c>--startup-report file</c> writes them down and exits, for measuring.
    /// </summary>
    internal static class StartupTrace
    {
        private static readonly DateTime ProcessStart = SafeStart();
        private static readonly List<(string Step, double Ms)> Marks = new List<(string, double)>();

        private static DateTime SafeStart()
        {
            try
            {
                return Process.GetCurrentProcess().StartTime;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is NotSupportedException)
            {
                return DateTime.Now;
            }
        }

        public static double NowMs => (DateTime.Now - ProcessStart).TotalMilliseconds;

        /// <summary>A step begins (the splash shows <paramref name="text"/> with the share of the start done so far).</summary>
        public static void Mark(string text, double progress)
        {
            Marks.Add((text, NowMs));
            StartupSplash.Current?.Report(text, progress);
        }

        public static string Report()
        {
            var sb = new StringBuilder("RobotWin Studio açılış izi (ms, işlem başından)").AppendLine();
            for (int i = 0; i < Marks.Count; i++)
            {
                double next = i + 1 < Marks.Count ? Marks[i + 1].Ms : Marks[i].Ms;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,8:F0}  {1,7:F0}  {2}", Marks[i].Ms, next - Marks[i].Ms, Marks[i].Step));
            }

            return sb.ToString();
        }
    }
}
