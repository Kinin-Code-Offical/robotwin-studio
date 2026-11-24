using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RobotWin.Studio.Shell
{
    /// <summary>User preferences persisted to %APPDATA%\RobotWinStudio\settings.json.</summary>
    public sealed class AppSettings
    {
        private static readonly string StorePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "settings.json");

        public static AppSettings Current { get; } = Load();

        /// <summary>Raised after <see cref="Save"/>; pages re-read what they use.</summary>
        public static event EventHandler? Changed;

        // ---------------------------------------------------------------- physics & solver

        /// <summary>Parts burn, blow and break down when driven outside their ratings.</summary>
        public bool EnableDamage { get; set; } = true;

        public double AmbientTempC { get; set; } = 25.0;

        /// <summary>Largest internal time step in seconds (smaller = more accurate, slower).</summary>
        public double MaxStep { get; set; } = 2e-4;

        /// <summary>Relative tolerance of the Newton iteration.</summary>
        public double RelTol { get; set; } = 1e-3;

        public bool ShowCurrentFlow { get; set; } = true;

        /// <summary>Simulation speed at start (1 = real time).</summary>
        public double DefaultSpeed { get; set; } = 1.0;

        /// <summary>Stop the simulation automatically when a part fails.</summary>
        public bool PauseOnFailure { get; set; }

        /// <summary>Gravity of new worlds (m/s²).</summary>
        public double WorldGravity { get; set; } = 9.81;

        // ---------------------------------------------------------------- appearance

        /// <summary>[theme] A theme id of <see cref="Themes.All"/> ("koyu", "pembe", "gece" …; "ozel" = the custom one).</summary>
        public string Theme { get; set; } = "koyu";

        /// <summary>[theme] Accent colour: an id of <see cref="Themes.Accents"/> ("mavi", "pembe" …; "tema" = the theme's own hue).</summary>
        public string Accent { get; set; } = "mavi";

        /// <summary>[theme] The custom theme: hue (°), tint (the neutrals' saturation, 0–80 %) and light or dark.</summary>
        public double ThemeHue { get; set; } = 330;

        public double ThemeTint { get; set; } = 30;

        public bool ThemeLight { get; set; }

        /// <summary>Whole-interface zoom (0.75 – 1.75).</summary>
        public double UiScale { get; set; } = 1.0;

        /// <summary>Particle effects (smoke, sparks, fire) in 2D and 3D.</summary>
        public bool Effects { get; set; } = true;

        /// <summary>FPS / frame-time overlay (Ctrl+Shift+F).</summary>
        public bool ShowPerf { get; set; }

        /// <summary>Navigation rail shows stage names and live status (Ctrl+B).</summary>
        public bool NavExpanded { get; set; }

        /// <summary>[guide] The first-robot guide was offered on a first start (it is offered once).</summary>
        public bool FirstRunGuideOffered { get; set; }

        /// <summary>Simulation sounds (motors, servos, buzzers, relays, failures).</summary>
        public bool Sound { get; set; } = true;

        /// <summary>Master volume of the simulation sounds, 0..1.</summary>
        public double Volume { get; set; } = 0.6;

        /// <summary>3D quality: "low", "medium", "high".</summary>
        public string Quality3D { get; set; } = "high";

        /// <summary>Length unit in editors: "mm" or "mil".</summary>
        public string Units { get; set; } = "mm";

        /// <summary>Snap grid of the world editor in millimetres.</summary>
        public double WorldGridMm { get; set; } = 5.0;

        /// <summary>Show the start page on launch (otherwise reopen the last file).</summary>
        public bool ShowHomeOnStart { get; set; } = true;

        // ---------------------------------------------------------------- files

        public int RecentFileCount { get; set; } = 10;

        /// <summary>Autosave interval in minutes (0 = off): unsaved tabs are copied into the backup folder.</summary>
        public int AutosaveMinutes { get; set; } = 5;

        /// <summary>[backup] Folder of autosaves and previous versions ("" = the project folder's Yedekler).</summary>
        public string BackupFolder { get; set; } = string.Empty;

        /// <summary>[backup] Copies kept per document (autosaves and previous versions each).</summary>
        public int BackupKeepPerFile { get; set; } = 10;

        /// <summary>[backup] The backup folder's size limit (MB); the oldest copies go first.</summary>
        public int BackupMaxMb { get; set; } = 500;

        /// <summary>[backup] Keep the previous version of a file each time it is saved over.</summary>
        public bool BackupOnSave { get; set; } = true;

        /// <summary>[backup] After a crash, the start page points at the latest autosaves.</summary>
        public bool CrashRecovery { get; set; } = true;

        /// <summary>Folder offered for new projects.</summary>
        public string ProjectFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RobotWin");

        /// <summary>[code] Folders opened on the code page from elsewhere on disk ("Klasör aç…").</summary>
        public List<string> CodeFolders { get; set; } = new List<string>();

        // ---------------------------------------------------------------- toolchains

        /// <summary>arduino-cli.exe override (empty = search the usual places).</summary>
        public string ArduinoCliPath { get; set; } = string.Empty;

        /// <summary>vcvars64.bat override for ESP32 builds (empty = find Visual Studio automatically).</summary>
        public string VcVarsPath { get; set; } = string.Empty;

        // ---------------------------------------------------------------- updates

        /// <summary>Ask GitHub for a new version at start-up (at most once a day).</summary>
        public bool AutoCheckUpdates { get; set; } = true;

        /// <summary>Offer pre-releases (beta, rc) too.</summary>
        public bool IncludePrereleases { get; set; }

        /// <summary>"Bu sürümü bir daha sorma": this version is not offered (a newer one is).</summary>
        public string SkippedUpdateVersion { get; set; } = string.Empty;

        /// <summary>The version whose news the user has seen ("Bu sürümde yenilikler" on the start page after an update).</summary>
        public string LastRunVersion { get; set; } = string.Empty;

        // ---------------------------------------------------------------- window

        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }
        public double WindowLeft { get; set; } = double.NaN;

        /// <summary>[keys] Shortcuts the user changed: command id → keys ("" = none). Others keep their defaults.</summary>
        public Dictionary<string, string> KeyBindings { get; set; } = new Dictionary<string, string>();

        /// <summary>[tabs] Files whose tabs are pinned (they open pinned).</summary>
        public List<string> PinnedFiles { get; set; } = new List<string>();
        public double WindowTop { get; set; } = double.NaN;

        private static AppSettings Load()
        {
            if (Unattended.Active) return new AppSettings();   // snapshots, bench, self-test: the defaults, never the user's
            try
            {
                if (File.Exists(StorePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(StorePath)) ?? new AppSettings();
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                if (!Unattended.Active)   // after a snapshot, bench or self-test the user's settings are as they were
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                    File.WriteAllText(StorePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>[settings] Takes every preference of <paramref name="other"/> (an imported file), keeping this machine's window placement.</summary>
        public void CopyFrom(AppSettings other)
        {
            foreach (var p in typeof(AppSettings).GetProperties())
            {
                if (!p.CanWrite || p.Name.StartsWith("Window", StringComparison.Ordinal)) continue;
                p.SetValue(this, p.GetValue(other));
            }
        }

        /// <summary>Restores every preference to its default (window size is kept).</summary>
        public void ResetToDefaults()
        {
            var d = new AppSettings();
            foreach (var p in typeof(AppSettings).GetProperties())
            {
                if (!p.CanWrite || p.Name.StartsWith("Window", StringComparison.Ordinal)) continue;
                p.SetValue(this, p.GetValue(d));
            }
        }
    }
}
