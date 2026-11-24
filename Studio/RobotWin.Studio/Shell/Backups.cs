using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [backup] Copies that keep work from being lost:
    /// <list type="bullet">
    /// <item>autosaves of unsaved tabs every few minutes (the user's own files are never overwritten);</item>
    /// <item>the previous version of a file each time it is saved over;</item>
    /// <item>a note that the app is running, so a start after a crash can point at the latest autosaves.</item>
    /// </list>
    /// Each document keeps its newest copies up to a number, and the folder stays under a size.
    /// </summary>
    internal static class Backups
    {
        private static AppSettings S => AppSettings.Current;

        /// <summary>Where the copies go: the folder set in Ayarlar, else Belgeler\RobotWin\Yedekler.</summary>
        public static string Folder => string.IsNullOrWhiteSpace(S.BackupFolder) ? Path.Combine(S.ProjectFolder, "Yedekler") : S.BackupFolder;

        private static readonly Regex Stamped = new Regex(@"^(?<name>.*?)-(?<kind>önceki-)?(?<stamp>\d{8}-\d{4,6})$", RegexOptions.Compiled);

        public static string SafeName(string title)
        {
            string safe = new string(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            return safe.Length == 0 ? "Adsız" : safe;
        }

        /// <summary>A path for an autosave of a document called <paramref name="title"/> (the newest name wins in a minute).</summary>
        public static string AutosavePath(string title, string extension)
        {
            Directory.CreateDirectory(Folder);
            return Path.Combine(Folder, SafeName(title) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + extension);
        }

        /// <summary>
        /// Before <paramref name="path"/> is written over: its current content is kept as "name-önceki-stamp" (when
        /// "Kaydederken önceki sürümü sakla" is on). Never throws: a backup that fails does not stop the save.
        /// </summary>
        public static void KeepPrevious(string path)
        {
            if (!S.BackupOnSave || Unattended.Active) return;
            try
            {
                if (!File.Exists(path)) return;
                Directory.CreateDirectory(Folder);
                string name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
                string target = Path.Combine(Folder, SafeName(name) + "-önceki-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ext);
                File.Copy(path, target, true);
                Prune();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
            }
        }

        /// <summary>Keeps the newest copies of each document (autosaves and previous versions apart) and the folder under its size.</summary>
        public static void Prune()
        {
            try
            {
                var dir = new DirectoryInfo(Folder);
                if (!dir.Exists) return;
                var files = dir.GetFiles().Where(f => Stamped.IsMatch(Path.GetFileNameWithoutExtension(f.Name))).ToList();
                int keep = Math.Max(1, S.BackupKeepPerFile);
                foreach (var group in files.GroupBy(f =>
                {
                    var m = Stamped.Match(Path.GetFileNameWithoutExtension(f.Name));
                    return m.Groups["name"].Value.ToLowerInvariant() + "|" + m.Groups["kind"].Value + "|" + f.Extension.ToLowerInvariant();
                }))
                {
                    foreach (var old in group.OrderByDescending(f => f.LastWriteTimeUtc).Skip(keep)) TryDelete(old);
                }

                long cap = Math.Max(10, S.BackupMaxMb) * 1024L * 1024L;
                var left = dir.GetFiles().Where(f => Stamped.IsMatch(Path.GetFileNameWithoutExtension(f.Name))).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                long total = 0;
                foreach (var f in left)
                {
                    total += f.Length;
                    if (total > cap) TryDelete(f);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static void TryDelete(FileInfo f)
        {
            try
            {
                f.Delete();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>The newest copies (for the start page and the settings), newest first.</summary>
        public static List<FileInfo> Newest(int count)
        {
            try
            {
                var dir = new DirectoryInfo(Folder);
                if (!dir.Exists) return new List<FileInfo>();
                return dir.GetFiles().Where(f => f.Extension == RobotTwin.CoreSim.Formats.RobotWinFormats.ProjectExtension || f.Extension == RobotTwin.CoreSim.Formats.RobotWinFormats.WorldExtension)
                    .OrderByDescending(f => f.LastWriteTimeUtc).Take(count).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new List<FileInfo>();
            }
        }

        /// <summary>Size of the folder's copies (bytes) and their number.</summary>
        public static (long Bytes, int Count) Usage()
        {
            try
            {
                var dir = new DirectoryInfo(Folder);
                if (!dir.Exists) return (0, 0);
                var files = dir.GetFiles();
                return (files.Sum(f => f.Length), files.Length);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return (0, 0);
            }
        }

        // ================================================================ crash note

        private static string SessionFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "session.lock");

        /// <summary>The last run ended without closing (a crash, a power cut): the start page points at the autosaves.</summary>
        public static bool CrashedLastTime { get; private set; }

        /// <summary>When the crashed run had started (its autosaves are the ones after it).</summary>
        public static DateTime CrashedRunStart { get; private set; }

        /// <summary>At start: notes a run left open by another, crashed process; then marks this one as running.</summary>
        public static void BeginSession()
        {
            if (Unattended.Active) return;
            try
            {
                if (File.Exists(SessionFile))
                {
                    var parts = File.ReadAllText(SessionFile).Split('|');
                    bool alive = parts.Length > 0 && int.TryParse(parts[0], out int pid) && IsRunning(pid);
                    if (!alive)
                    {
                        CrashedLastTime = S.CrashRecovery;
                        CrashedRunStart = parts.Length > 1 && long.TryParse(parts[1], out long ticks) ? new DateTime(ticks) : File.GetLastWriteTime(SessionFile);
                    }
                    else
                    {
                        return;   // another window of the app is open: it owns the note
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(SessionFile)!);
                File.WriteAllText(SessionFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "|" + DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>At a normal exit: the note goes.</summary>
        public static void EndSession()
        {
            if (Unattended.Active) return;
            try
            {
                if (!File.Exists(SessionFile)) return;
                var parts = File.ReadAllText(SessionFile).Split('|');
                if (parts.Length > 0 && parts[0] == Environment.ProcessId.ToString(CultureInfo.InvariantCulture)) File.Delete(SessionFile);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static bool IsRunning(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return !p.HasExited && p.ProcessName.IndexOf("RobotWin", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
