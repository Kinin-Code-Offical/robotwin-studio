using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RobotTwin.Tools
{
    public static class FirmwareMonitorLauncher
    {
        public const string AutoLaunchKey = "RobotWin.FirmwareMonitor.AutoLaunch";
        public const string ForceVirtualMcuKey = "RobotWin.FirmwareMonitor.ForceVirtualMcuPipeMode";
        public const string LastTargetKey = "RobotWin.FirmwareMonitor.LastTarget";
        private static bool _hasLaunched;
        private static bool _warnedMissing;

        public static bool AutoLaunchEnabled
        {
            get
            {
                if (!PlayerPrefs.HasKey(AutoLaunchKey))
                {
#if UNITY_EDITOR
                    bool editorValue = UnityEditor.EditorPrefs.GetBool(AutoLaunchKey, false);
                    PlayerPrefs.SetInt(AutoLaunchKey, editorValue ? 1 : 0);
                    PlayerPrefs.Save();
#endif
                }
                return PlayerPrefs.GetInt(AutoLaunchKey, 0) == 1;
            }
            set
            {
                PlayerPrefs.SetInt(AutoLaunchKey, value ? 1 : 0);
                PlayerPrefs.Save();
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetBool(AutoLaunchKey, value);
#endif
            }
        }

        public static bool ForceVirtualMcuInPipeMode
        {
            get
            {
                if (!PlayerPrefs.HasKey(ForceVirtualMcuKey))
                {
#if UNITY_EDITOR
                    bool editorValue = UnityEditor.EditorPrefs.GetBool(ForceVirtualMcuKey, false);
                    PlayerPrefs.SetInt(ForceVirtualMcuKey, editorValue ? 1 : 0);
                    PlayerPrefs.Save();
#endif
                }
                return PlayerPrefs.GetInt(ForceVirtualMcuKey, 0) == 1;
            }
            set
            {
                PlayerPrefs.SetInt(ForceVirtualMcuKey, value ? 1 : 0);
                PlayerPrefs.Save();
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetBool(ForceVirtualMcuKey, value);
#endif
            }
        }

        public static string LastTarget
        {
            get
            {
                return PlayerPrefs.GetString(LastTargetKey, string.Empty);
            }
            set
            {
                PlayerPrefs.SetString(LastTargetKey, value ?? string.Empty);
                PlayerPrefs.Save();
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetString(LastTargetKey, value ?? string.Empty);
#endif
            }
        }

        public static bool IsUnityTargetPreferred
        {
            get
            {
                return string.Equals(LastTarget, "unity", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool TryAutoLaunch()
        {
            if (!AutoLaunchEnabled) return false;
            return LaunchIfNeeded();
        }

        public static bool LaunchIfNeeded()
        {
            if (_hasLaunched) return true;
            if (!IsWindows()) return false;

            if (IsMonitorRunning())
            {
                LastTarget = "unity";
                _hasLaunched = true;
                return true;
            }

            string exePath = FindMonitorExe();
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                if (!_warnedMissing)
                {
                    UnityEngine.Debug.LogWarning("[RobotWin] Firmware Monitor not found. Run Build Firmware Monitor first.");
                    _warnedMissing = true;
                }
                return false;
            }

            try
            {
                LastTarget = "unity";
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? GetRepoRoot(),
                    UseShellExecute = true,
                    Arguments = "--target=unity --auto-connect"
                };
                Process.Start(psi);
                _hasLaunched = true;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[RobotWin] Firmware Monitor launch failed: {ex.Message}");
                return false;
            }
        }

        private static bool IsMonitorRunning()
        {
            try
            {
                return Process.GetProcessesByName("RobotWinFirmwareMonitor").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWindows()
        {
            return Application.platform == RuntimePlatform.WindowsEditor ||
                   Application.platform == RuntimePlatform.WindowsPlayer;
        }

        private static string FindMonitorExe()
        {
            string projectRoot = GetRepoRoot();
            string releaseRoot = Path.Combine(projectRoot, "builds", "RobotWinFirmwareMonitor", "Release");
            string directExe = Path.Combine(releaseRoot, "win-x64", "RobotWinFirmwareMonitor.exe");
            if (File.Exists(directExe)) return directExe;
            if (!Directory.Exists(releaseRoot)) return null;

            try
            {
                return Directory.EnumerateFiles(releaseRoot, "RobotWinFirmwareMonitor.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string GetRepoRoot()
        {
            string assetsPath = Application.dataPath;
            return Path.GetFullPath(Path.Combine(assetsPath, "..", ".."));
        }
    }
}
