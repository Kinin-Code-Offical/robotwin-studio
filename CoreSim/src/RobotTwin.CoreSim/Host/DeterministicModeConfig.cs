using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Host
{
    /// <summary>
    /// Reference configuration for deterministic stepping.
    /// This is intentionally small and dependency-free so it can be used in CoreSim and tooling.
    /// </summary>
    public sealed class DeterministicModeConfig
    {
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Fixed simulation timestep in seconds.
        /// </summary>
        public double DtSeconds { get; set; } = 0.01;

        /// <summary>
        /// If set, overrides the firmware delta micros sent per step.
        /// When null, DeltaMicros is derived from DtSeconds.
        /// </summary>
        public uint? DeltaMicrosOverride { get; set; }

        /// <summary>
        /// Recommended seed for any deterministic randomization.
        /// </summary>
        public int RandomSeed { get; set; } = 1337;

        /// <summary>
        /// How long ConnectFirmware() should wait before failing.
        /// </summary>
        public int FirmwareConnectTimeoutMs { get; set; } = 5000;

        public static DeterministicModeConfig Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Config path is required.", nameof(filePath));
            }

            var json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<DeterministicModeConfig>(json, JsonOptions);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to parse deterministic mode config.");
            }
            return config;
        }

        public void Save(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Config path is required.", nameof(filePath));
            }

            var json = JsonSerializer.Serialize(this, JsonOptions);
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(filePath, json);
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }
}
