using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Updates
{
    /// <summary>Facts about the running copy that the updater needs, read from disk and the registry.</summary>
    public static class InstallEnvironment
    {
        /// <summary>Written by Installer/setup.cpp (HKCU for a per-user install, HKLM for all users; not for /PORTABLE).</summary>
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RobotWinStudio";

        public static string AppDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        /// <summary>
        /// The running version: InformationalVersion (keeps a "-beta.1" label), else the assembly version.
        /// ROBOTWIN_UPDATE_TEST_VERSION=1.9.0 pretends to be an older copy (to try the update notice without a new release).
        /// </summary>
        public static SemVersion CurrentVersion
        {
            get
            {
                if (SemVersion.Parse(Environment.GetEnvironmentVariable("ROBOTWIN_UPDATE_TEST_VERSION")) is { } test) return test;
                var asm = typeof(InstallEnvironment).Assembly;
                string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                return SemVersion.Parse(info) ?? SemVersion.FromVersion(asm.GetName().Version ?? new Version(0, 0, 0));
            }
        }

        public static InstallInfo Detect()
        {
            string dir = AppDirectory;
            bool selfContained = File.Exists(Path.Combine(dir, "coreclr.dll"));
            var info = new InstallInfo
            {
                AppDirectory = dir,
                SelfContained = selfContained,
                RuntimeAvailable = !selfContained || DesktopRuntime9Installed(),
                Mode = UpdateLogic.DetectMode(dir, InstallLocation(Registry.CurrentUser), InstallLocation(LocalMachine()), File.Exists(Path.Combine(dir, "Uninstall.exe")))
            };
            bool allUsers = info.Mode == InstallMode.AllUsers;
            string desktop = Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory);
            info.DesktopShortcut = File.Exists(Path.Combine(desktop, "RobotWin Studio.lnk"));
            try
            {
                info.FileTypesRegistered = FileAssociations.IsRegistered();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
            {
                info.FileTypesRegistered = false;
            }

            return info;
        }

        private static RegistryKey? LocalMachine()
        {
            try
            {
                // setup.exe is a 64-bit program, so its HKLM entry is in the 64-bit view.
                return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
            {
                return null;
            }
        }

        private static string? InstallLocation(RegistryKey? hive)
        {
            if (hive == null) return null;
            try
            {
                using var key = hive.OpenSubKey(UninstallKey);
                return key?.GetValue("InstallLocation") as string;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
            {
                return null;
            }
        }

        public static bool DesktopRuntime9Installed()
        {
            foreach (var root in new[] { Environment.GetEnvironmentVariable("DOTNET_ROOT"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet") })
            {
                if (string.IsNullOrEmpty(root)) continue;
                try
                {
                    string shared = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
                    if (Directory.Exists(shared) && Directory.EnumerateDirectories(shared).Any(d => Path.GetFileName(d).StartsWith("9.", StringComparison.Ordinal))) return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }

            return false;
        }

        /// <summary>Can this user write into the folder (otherwise the installer must run elevated)?</summary>
        public static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".rw-write-test-" + Environment.ProcessId);
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Starts setup.exe silently into the current folder (it waits for this app to exit, installs, and starts the
        /// new version). Elevated for an all-users install or a folder this user cannot write.
        /// </summary>
        public static void StartSilentInstall(string installer, InstallInfo info)
        {
            bool elevate = info.Mode == InstallMode.AllUsers || !CanWrite(info.AppDirectory);
            Start(installer, UpdateLogic.InstallerArguments(info), elevate);
        }

        /// <summary>Opens the installer's wizard (development builds, or the user asked for it).</summary>
        public static void StartWizard(string installer) => Start(installer, string.Empty, false);

        private static void Start(string exe, string args, bool elevate)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty };
            if (elevate) psi.Verb = "runas";
            try
            {
                Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new UpdateException("Yönetici izni verilmedi; güncelleme kurulmadı.", ex);
            }
            catch (Win32Exception ex)
            {
                throw new UpdateException("Kurulum programı başlatılamadı: " + ex.Message, ex);
            }
        }
    }
}
