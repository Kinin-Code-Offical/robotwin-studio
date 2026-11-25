using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RobotWin.Studio.Updates
{
    /// <summary>
    /// [setup] What the installer recorded in install.json beside the exe (Installer/setup.cpp): the kind of installation,
    /// its components, the language chosen in the setup and where it went. Null for a build folder or a copy an older
    /// setup made (<see cref="InstallEnvironment.Detect"/> then reads the components off the disk).
    /// </summary>
    public sealed class InstallProfile
    {
        /// <summary>The components a release's setup can install, in its order: the app, its own .NET, the Arduino compiler, STEP / IGES.</summary>
        public static readonly string[] Known = { "app", "dotnet", "arduino", "occt" };

        public string Version { get; init; } = string.Empty;

        /// <summary>"full", "lite" or "custom".</summary>
        public string Type { get; init; } = string.Empty;

        public IReadOnlyList<string> Components { get; init; } = Array.Empty<string>();

        /// <summary>The language chosen in the setup: the app's language for users who start it for the first time.</summary>
        public string Language { get; init; } = string.Empty;

        /// <summary>"user", "allusers" or "portable".</summary>
        public string Where { get; init; } = string.Empty;

        public bool Has(string id) => Components.Contains(id, StringComparer.OrdinalIgnoreCase);

        private static readonly Lazy<InstallProfile?> CurrentProfile = new Lazy<InstallProfile?>(() => Read(AppContext.BaseDirectory));

        /// <summary>The running copy's record (read once).</summary>
        public static InstallProfile? Current => CurrentProfile.Value;

        public static InstallProfile? Read(string dir)
        {
            try
            {
                string path = Path.Combine(dir, "install.json");
                return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static InstallProfile? Parse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                var parts = root.TryGetProperty("components", out var c) && c.ValueKind == JsonValueKind.Array
                    ? c.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.ToLowerInvariant()).ToList()
                    : new List<string>();
                return new InstallProfile { Version = Text("version"), Type = Text("type").ToLowerInvariant(), Components = parts, Language = Text("language").ToLowerInvariant(), Where = Text("where").ToLowerInvariant() };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The components of a copy an older setup made, read off its folder: the one with .NET inside (coreclr.dll
        /// beside the exe) was the full package.
        /// </summary>
        public static List<string> FromDisk(string dir)
        {
            var parts = new List<string> { "app" };
            if (File.Exists(Path.Combine(dir, "coreclr.dll")) || Directory.Exists(Path.Combine(dir, "dotnet", "host", "fxr"))) parts.Add("dotnet");
            if (File.Exists(Path.Combine(dir, "tools", "arduino", "arduino-cli.exe"))) parts.Add("arduino");
            if (File.Exists(Path.Combine(dir, "occt", "RobotWin.Occt.dll"))) parts.Add("occt");
            return parts;
        }

        /// <summary>A component's name as the setup shows it.</summary>
        public static string NameOf(string id) => id switch
        {
            "app" => "RobotWin Studio",
            "dotnet" => ".NET 9 çalışma zamanı",
            "arduino" => "Arduino derleyicisi",
            "occt" => "STEP / IGES desteği",
            _ => id
        };
    }
}
