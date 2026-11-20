using System;
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

        /// <summary>Whole-interface zoom (0.75 – 1.75).</summary>
        public double UiScale { get; set; } = 1.0;

        /// <summary>Particle effects (smoke, sparks, fire) in 2D and 3D.</summary>
        public bool Effects { get; set; } = true;

        /// <summary>Navigation rail shows stage names and live status (Ctrl+B).</summary>
        public bool NavExpanded { get; set; }

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

        /// <summary>Autosave interval in minutes (0 = off). Saves a backup copy next to the file.</summary>
        public int AutosaveMinutes { get; set; } = 5;

        /// <summary>Folder offered for new projects.</summary>
        public string ProjectFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RobotWin");

        // ---------------------------------------------------------------- toolchains

        /// <summary>arduino-cli.exe override (empty = search the usual places).</summary>
        public string ArduinoCliPath { get; set; } = string.Empty;

        /// <summary>vcvars64.bat override for ESP32 builds (empty = find Visual Studio automatically).</summary>
        public string VcVarsPath { get; set; } = string.Empty;

        // ---------------------------------------------------------------- window

        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }

        private static AppSettings Load()
        {
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
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            Changed?.Invoke(this, EventArgs.Empty);
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
