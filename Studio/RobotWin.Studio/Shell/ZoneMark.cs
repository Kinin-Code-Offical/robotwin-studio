using System;
using System.Globalization;
using System.IO;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [trust] Windows' mark on a file that arrived from the internet or another computer: the Zone.Identifier stream a
    /// browser, mail program or archive tool writes beside it (Mark of the Web). No WPF: tested on its own.
    /// </summary>
    public static class ZoneMark
    {
        /// <summary>The file's security zone as Windows recorded it when it arrived (3 internet, 4 untrusted); 0 when none.</summary>
        public static int ZoneOf(string path)
        {
            try
            {
                string marker = path + ":Zone.Identifier";
                if (!File.Exists(marker)) return 0;
                foreach (string line in File.ReadAllLines(marker))
                {
                    string l = line.Trim();
                    if (l.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(l.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out int zone))
                        return zone;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
            {
            }

            return 0;
        }

        /// <summary>Came from the internet (or a zone Windows treats so): zone 3 or above.</summary>
        public static bool FromElsewhere(string? path) => path != null && File.Exists(path) && ZoneOf(path) >= 3;

        /// <summary>Removes the mark, as the file's Properties → Unblock does.</summary>
        public static void Unblock(string path)
        {
            try
            {
                File.Delete(path + ":Zone.Identifier");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
            {
            }
        }
    }
}
