using System;
using System.IO;
using UnityEngine;

namespace RobotTwin.Game.RaspberryPi
{
    public sealed class RpiRuntimeConfig
    {
        public static bool IsUnoOnlyMode()
        {
            string value = Environment.GetEnvironmentVariable("ROBOTWIN_UNO_ONLY") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim().ToLowerInvariant();
            return value == "1" || value == "true" || value == "yes" || value == "on";
        }

        public string QemuPath { get; set; }
        public string ImagePath { get; set; }
        public string SharedMemoryDir { get; set; }
        public int DisplayWidth { get; set; } = 320;
        public int DisplayHeight { get; set; } = 200;
        public int CameraWidth { get; set; } = 320;
        public int CameraHeight { get; set; } = 200;
        public int GpioCount { get; set; } = 32;
        public string NetworkMode { get; set; } = "nat";
        public bool AllowMock { get; set; } = false;
        public bool CreateShmIfMissing { get; set; } = false;
        public ulong CpuAffinityMask { get; set; }
        public uint CpuMaxPercent { get; set; }
        public uint ThreadCount { get; set; }
        public uint PriorityClass { get; set; }

        public static RpiRuntimeConfig FromEnvironment()
        {
            string repoRoot = ResolveRepoRoot();
            var config = new RpiRuntimeConfig();
            config.QemuPath = GetEnv("ROBOTWIN_RPI_QEMU");
            config.ImagePath = GetEnv("ROBOTWIN_RPI_IMAGE");
            config.SharedMemoryDir = GetEnv("ROBOTWIN_RPI_SHM_DIR");
            config.AllowMock = GetEnvBool("ROBOTWIN_RPI_ALLOW_MOCK");
            config.CreateShmIfMissing = GetEnvBool("ROBOTWIN_RPI_CREATE_SHM");
            config.CpuAffinityMask = GetEnvUlong("ROBOTWIN_RPI_CPU_AFFINITY");
            config.CpuMaxPercent = GetEnvUint("ROBOTWIN_RPI_CPU_MAX_PERCENT");
            config.ThreadCount = GetEnvUint("ROBOTWIN_RPI_THREADS");
            config.PriorityClass = GetEnvUint("ROBOTWIN_RPI_PRIORITY");

            if (string.IsNullOrWhiteSpace(config.SharedMemoryDir))
            {
                config.SharedMemoryDir = Path.Combine(repoRoot, "logs", "rpi", "shm");
            }
            return config;
        }

        public static string ResolveRepoRoot()
        {
            try
            {
                var assetsPath = Application.dataPath;
                if (string.IsNullOrWhiteSpace(assetsPath)) return Directory.GetCurrentDirectory();
                var assetsDir = new DirectoryInfo(assetsPath);
                return assetsDir.Parent?.Parent?.FullName ?? assetsDir.Parent?.FullName ?? assetsPath;
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }

        private static string GetEnv(string key)
        {
            return Environment.GetEnvironmentVariable(key) ?? string.Empty;
        }

        private static bool GetEnvBool(string key)
        {
            string value = GetEnv(key);
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim().ToLowerInvariant();
            return value == "1" || value == "true" || value == "yes" || value == "on";
        }

        private static ulong GetEnvUlong(string key)
        {
            string value = GetEnv(key);
            if (string.IsNullOrWhiteSpace(value)) return 0;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (ulong.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var hex))
                {
                    return hex;
                }
            }
            return ulong.TryParse(value, out var parsed) ? parsed : 0;
        }

        private static uint GetEnvUint(string key)
        {
            string value = GetEnv(key);
            return uint.TryParse(value, out var parsed) ? parsed : 0;
        }
    }
}
