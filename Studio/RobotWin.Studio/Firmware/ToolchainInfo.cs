using System;
using System.Text.Json;

namespace RobotWin.Studio.Firmware
{
    /// <summary>
    /// [toolchain] Reads what arduino-cli says about itself and its board cores, for the "built with" line a project keeps
    /// (ArduinoCompiler.Toolchain.cs). No process here: the texts come in, so this is testable on its own.
    /// </summary>
    public static class ToolchainInfo
    {
        /// <summary>"arduino-cli 1.6.0 · arduino:avr 1.8.8".</summary>
        public static string Describe(string? cliVersion, string platform, string? coreVersion) =>
            $"arduino-cli {cliVersion ?? "?"} · {platform} {coreVersion ?? "?"}";

        /// <summary>"VersionString" of <c>arduino-cli version --format json</c>.</summary>
        public static string? CliVersion(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("VersionString", out var v) ? v.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The installed version of a platform in <c>arduino-cli core list --format json</c>: {"platforms":[{"id",
        /// "installed_version"}]} from 1.0 on, a bare array of {"id","installed"} before.
        /// </summary>
        public static string? CoreVersion(string json, string platform)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var list = root.ValueKind == JsonValueKind.Array ? root
                    : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("platforms", out var p) ? p : default;
                if (list.ValueKind != JsonValueKind.Array) return null;
                foreach (var item in list.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var id) || !string.Equals(id.GetString(), platform, StringComparison.OrdinalIgnoreCase)) continue;
                    if (item.TryGetProperty("installed_version", out var v) || item.TryGetProperty("installed", out v)) return v.GetString();
                }

                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
