using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sdk;

namespace RobotWin.Studio.Shell
{
    /// <summary>[sdk] Plugin preferences, kept in %APPDATA%\RobotWinStudio\plugins.json (separate from settings.json).</summary>
    public sealed class PluginSettings
    {
        private static readonly string StorePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "plugins.json");

        /// <summary>Plugin ids not to load.</summary>
        public List<string> Disabled { get; set; } = new List<string>();

        /// <summary>More folders to look for plugins in (besides %APPDATA%\RobotWinStudio\Plugins).</summary>
        public List<string> ExtraFolders { get; set; } = new List<string>();

        /// <summary>Load plugins from a "Plugins" folder next to the opened world file.</summary>
        public bool LoadWorldPlugins { get; set; } = true;

        /// <summary>Compile changed world scripts automatically when the world starts.</summary>
        public bool AutoCompileScripts { get; set; } = true;

        public static PluginSettings Load()
        {
            try
            {
                if (File.Exists(StorePath)) return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(StorePath)) ?? new PluginSettings();
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
            }

            return new PluginSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// [sdk] The application's side of the SDK: registers the SDK parts, loads user and world
    /// plugins into <see cref="PluginHost.Shared"/> and compiles world scripts.
    /// </summary>
    public static class SdkServices
    {
        private static bool _initialised;
        private static string? _scriptKey;
        private static readonly HashSet<string> WorldFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static PluginHost Host => PluginHost.Shared;

        public static PluginSettings Settings { get; } = PluginSettings.Load();

        /// <summary>Raised when plugins or scripts were (re)loaded.</summary>
        public static event EventHandler? Changed;

        public static ScriptBuildResult? LastScriptBuild { get; private set; }

        public static void Init()
        {
            if (_initialised) return;
            _initialised = true;
            SdkParts.EnsureRegistered(PartLibrary.Default);
            _ = BehaviorRegistry.Shared;
            LoadUserPlugins();
        }

        public static IEnumerable<string> Folders()
        {
            yield return PluginHost.UserPluginDirectory;
            foreach (var f in Settings.ExtraFolders.Where(f => !string.IsNullOrWhiteSpace(f))) yield return f;
        }

        public static void LoadUserPlugins()
        {
            try
            {
                Directory.CreateDirectory(PluginHost.UserPluginDirectory);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            foreach (var dir in Folders()) Host.LoadDirectory(dir, "user", Settings.Disabled);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Plugins in "&lt;world folder&gt;\Plugins" (once per folder and session).</summary>
        public static void LoadWorldPlugins(string? worldPath)
        {
            if (!Settings.LoadWorldPlugins) return;
            var dir = PluginHost.WorldPluginDirectory(worldPath);
            if (dir == null || !Directory.Exists(dir) || !WorldFolders.Add(dir)) return;
            Host.LoadDirectory(dir, "world", Settings.Disabled);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Unloads and loads every non-script plugin again (after rebuilding a plugin). Stop simulations first.</summary>
        public static void ReloadAll()
        {
            foreach (var p in Host.Plugins.Where(p => p.Source == "user" || p.Source == "world").ToList())
            {
                if (Settings.Disabled.Contains(p.Id)) Host.Unload(p.Id);
                else Host.Reload(p.Id);
            }

            LoadUserPlugins();
        }

        public static void SetDisabled(string id, bool disabled)
        {
            Settings.Disabled.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            if (disabled)
            {
                Settings.Disabled.Add(id);
                Host.Unload(id);
            }
            else
            {
                var p = Host.Find(id);
                if (p != null) Host.Reload(id);
                else LoadUserPlugins();
            }

            Settings.Save();
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static string Key(WorldDocument world) =>
            string.Join("\u0001", world.Sdk.Scripts.Where(s => s.Enabled).Select(s => s.Name + "\u0002" + s.Source));

        /// <summary>True when the loaded script assembly matches the world's enabled scripts.</summary>
        public static bool ScriptsUpToDate(WorldDocument world)
        {
            var enabled = world.Sdk.Scripts.Where(s => s.Enabled).ToList();
            if (enabled.Count == 0) return true;
            return _scriptKey == Key(world) && Host.Find(BehaviorRegistry.ScriptId)?.IsLoaded == true;
        }

        /// <summary>Compiles the world's scripts with the installed .NET SDK and loads them (blocking; cached by content).</summary>
        public static ScriptBuildResult CompileScripts(WorldDocument world)
        {
            var files = world.Sdk.Scripts.Where(s => s.Enabled).Select(s => (s.Name, s.Source)).ToList();
            var result = ScriptCompiler.Build(files, "RobotWinWorldScripts");
            LastScriptBuild = result;
            if (result.Success && result.AssemblyPath != null)
            {
                var loaded = Host.LoadScripts(result.AssemblyPath);
                if (loaded.Error != null)
                {
                    result.Diagnostics.Add(new ScriptDiagnostic { Message = loaded.Error, IsError = true });
                }
                else _scriptKey = Key(world);
            }

            Changed?.Invoke(null, EventArgs.Empty);
            return result;
        }
    }
}
