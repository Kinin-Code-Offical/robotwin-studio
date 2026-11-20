using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>
    /// <c>plugin.json</c> next to a plugin DLL:
    /// <code>
    /// { "id": "acme.wind", "name": "ACME rüzgâr", "version": "1.2.0", "author": "ACME",
    ///   "assembly": "Acme.Wind.dll", "entryTypes": ["Acme.Wind.Turbine"], "sdkVersion": 1 }
    /// </code>
    /// A folder with a DLL but no manifest is loaded with a manifest made from the file name.
    /// </summary>
    public sealed class PluginManifest
    {
        public const string FileName = "plugin.json";

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0.0";
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>DLL file name, relative to the manifest.</summary>
        public string Assembly { get; set; } = string.Empty;

        /// <summary>Full type names to expose; empty = every public SDK type in the assembly.</summary>
        public List<string> EntryTypes { get; set; } = new List<string>();

        /// <summary>SDK API version the plugin was built for (<see cref="SdkInfo.ApiVersion"/>).</summary>
        public int SdkVersion { get; set; } = SdkInfo.ApiVersion;

        /// <summary>Folder of the plugin (set when read from disk).</summary>
        [JsonIgnore]
        public string Directory { get; set; } = string.Empty;

        [JsonIgnore]
        public string? ManifestPath { get; set; }

        [JsonIgnore]
        public string AssemblyPath => Path.Combine(Directory, Assembly);

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static PluginManifest Read(string path)
        {
            var m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), Options)
                    ?? throw new InvalidDataException("plugin.json boş.");
            m.Directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
            m.ManifestPath = path;
            if (string.IsNullOrWhiteSpace(m.Assembly))
            {
                var dll = System.IO.Directory.GetFiles(m.Directory, "*.dll").FirstOrDefault(f => !IsSdkAssembly(f));
                m.Assembly = dll != null ? Path.GetFileName(dll) : string.Empty;
            }

            if (string.IsNullOrWhiteSpace(m.Id)) m.Id = Path.GetFileNameWithoutExtension(m.Assembly);
            if (string.IsNullOrWhiteSpace(m.Name)) m.Name = m.Id;
            return m;
        }

        public static PluginManifest ForAssembly(string dllPath, string? id = null) => new PluginManifest
        {
            Id = id ?? Path.GetFileNameWithoutExtension(dllPath),
            Name = Path.GetFileNameWithoutExtension(dllPath),
            Assembly = Path.GetFileName(dllPath),
            Directory = Path.GetDirectoryName(Path.GetFullPath(dllPath)) ?? string.Empty
        };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        /// <summary>Problems that prevent loading (Turkish).</summary>
        public List<string> Validate(bool allowReserved = false)
        {
            var e = new List<string>();
            if (string.IsNullOrWhiteSpace(Id)) e.Add("Eklentinin kimliği (id) yok.");
            else if (Id.IndexOfAny(new[] { ':', '/', '\\' }) >= 0) e.Add($"Kimlik '{Id}' ':' '/' '\\' içeremez.");
            else if (!allowReserved && (string.Equals(Id, BehaviorRegistry.BuiltinId, StringComparison.OrdinalIgnoreCase) || string.Equals(Id, BehaviorRegistry.ScriptId, StringComparison.OrdinalIgnoreCase)))
                e.Add($"'{Id}' ayrılmış bir kimliktir.");
            if (string.IsNullOrWhiteSpace(Assembly)) e.Add("Derleme (assembly) dosyası belirtilmemiş ve klasörde DLL yok.");
            else if (!File.Exists(AssemblyPath)) e.Add($"'{Assembly}' bulunamadı ({Directory}).");
            if (SdkVersion > SdkInfo.ApiVersion) e.Add($"Eklenti daha yeni bir SDK için yazılmış (sürüm {SdkVersion}; bu uygulama {SdkInfo.ApiVersion}).");
            return e;
        }

        /// <summary>The SDK itself (and its dependencies) must not be loaded twice.</summary>
        public static bool IsSdkAssembly(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            return n.StartsWith("RobotTwin.CoreSim", StringComparison.OrdinalIgnoreCase) || n.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                   n.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) || n.Equals("RobotWinStudio", StringComparison.OrdinalIgnoreCase);
        }
    }
}
