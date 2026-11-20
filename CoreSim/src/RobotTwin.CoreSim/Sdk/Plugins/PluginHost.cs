using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RobotTwin.CoreSim.Design;
#if NET5_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>A plugin known to the host: loaded, failed or unloaded.</summary>
    public sealed class LoadedPlugin
    {
        public PluginManifest Manifest { get; internal set; } = new PluginManifest();

        /// <summary>"user" (%APPDATA% folder), "world" (next to the world file), "script" (compiled world scripts), "builtin", "code".</summary>
        public string Source { get; internal set; } = "user";

        public Assembly? Assembly { get; internal set; }
        public bool IsLoaded => Assembly != null;

        /// <summary>True when it runs in its own collectible load context (can be unloaded and reloaded).</summary>
        public bool CanUnload { get; internal set; }

        public IReadOnlyList<SdkTypeInfo> Types { get; internal set; } = Array.Empty<SdkTypeInfo>();
        public IReadOnlyList<string> PartIds { get; internal set; } = Array.Empty<string>();
        public string? Error { get; internal set; }
        public DateTime LoadedAt { get; internal set; }

        public string Id => Manifest.Id;

        internal object? Context;

        public override string ToString() => $"{Manifest.Name} {Manifest.Version} ({Source}{(Error != null ? ", hata" : IsLoaded ? "" : ", yüklü değil")})";
    }

    /// <summary>
    /// Finds, loads, unloads and reloads plugin assemblies. Every plugin gets its own collectible
    /// <c>AssemblyLoadContext</c> and is loaded from a copy in memory, so the DLL on disk stays
    /// writable (rebuild, then <see cref="Reload"/>). The SDK assembly (RobotTwin.CoreSim) is
    /// always shared with the host so the plugin's types implement the host's interfaces.
    /// </summary>
    /// <remarks>
    /// Unloading only completes once nothing references the plugin's types any more: stop the
    /// simulation first (running behaviours hold instances). Code in a plugin runs with the full
    /// rights of the application — only install plugins you trust.
    /// </remarks>
    public sealed class PluginHost
    {
        private readonly List<LoadedPlugin> _plugins = new List<LoadedPlugin>();
        private readonly Dictionary<string, (string PluginId, Action<PartBuildContext> Build)> _partBuilders =
            new Dictionary<string, (string, Action<PartBuildContext>)>(StringComparer.OrdinalIgnoreCase);
        private PartLibrary? _parts;

        private static readonly Lazy<PluginHost> SharedHost = new Lazy<PluginHost>(() => new PluginHost(BehaviorRegistry.Shared));

        public PluginHost(BehaviorRegistry registry, PartLibrary? parts = null)
        {
            Registry = registry;
            _parts = parts;
        }

        public static PluginHost Shared => SharedHost.Value;

        public BehaviorRegistry Registry { get; }

        /// <summary>Library that receives plugin parts (default: <see cref="PartLibrary.Default"/>).</summary>
        public PartLibrary Parts => _parts ??= PartLibrary.Default;

        public event EventHandler? Changed;

        public IReadOnlyList<LoadedPlugin> Plugins
        {
            get
            {
                lock (_plugins) return _plugins.ToList();
            }
        }

        public LoadedPlugin? Find(string id)
        {
            lock (_plugins) return _plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when this runtime can unload plugins (.NET 5+; not on netstandard hosts).</summary>
        public static bool SupportsUnload
        {
            get
            {
#if NET5_0_OR_GREATER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>%APPDATA%\RobotWinStudio\Plugins</summary>
        public static string UserPluginDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "Plugins");

        /// <summary>Plugins folder next to a world file ("&lt;folder of the .rwworld&gt;\Plugins").</summary>
        public static string? WorldPluginDirectory(string? worldPath) =>
            string.IsNullOrEmpty(worldPath) ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(worldPath!)) ?? ".", "Plugins");

        /// <summary>
        /// Manifests in a folder: every sub-folder with a plugin.json (or a single DLL), and
        /// loose DLLs directly in the folder.
        /// </summary>
        public static List<PluginManifest> Discover(string directory, List<string>? problems = null)
        {
            var list = new List<PluginManifest>();
            if (!Directory.Exists(directory)) return list;
            void Try(Func<PluginManifest> make, string where)
            {
                try
                {
                    list.Add(make());
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException || ex is InvalidDataException)
                {
                    problems?.Add($"{where}: {ex.Message}");
                }
            }

            foreach (var dir in Directory.GetDirectories(directory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string manifest = Path.Combine(dir, PluginManifest.FileName);
                if (File.Exists(manifest)) Try(() => PluginManifest.Read(manifest), manifest);
                else
                {
                    var dlls = Directory.GetFiles(dir, "*.dll").Where(f => !PluginManifest.IsSdkAssembly(f)).ToList();
                    if (dlls.Count == 1) Try(() => PluginManifest.ForAssembly(dlls[0]), dlls[0]);
                    else if (dlls.Count > 1) problems?.Add($"{dir}: birden çok DLL var ama plugin.json yok; hangisinin eklenti olduğunu plugin.json'da 'assembly' ile belirtin.");
                }
            }

            foreach (var dll in Directory.GetFiles(directory, "*.dll").Where(f => !PluginManifest.IsSdkAssembly(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                Try(() => PluginManifest.ForAssembly(dll), dll);
            }

            return list;
        }

        /// <summary>Loads every plugin of a folder (skipping ids in <paramref name="disabled"/> and ones already loaded).</summary>
        public List<LoadedPlugin> LoadDirectory(string directory, string source = "user", ICollection<string>? disabled = null)
        {
            var problems = new List<string>();
            var result = new List<LoadedPlugin>();
            foreach (var m in Discover(directory, problems))
            {
                if (disabled != null && disabled.Contains(m.Id)) continue;
                var existing = Find(m.Id);
                if (existing != null && existing.IsLoaded) continue;
                result.Add(Load(m, source));
            }

            foreach (var p in problems)
            {
                var failed = new LoadedPlugin { Manifest = new PluginManifest { Id = "?", Name = p }, Source = source, Error = p };
                result.Add(failed);
            }

            return result;
        }

        /// <summary>Loads a plugin from its manifest. Never throws: problems end up in <see cref="LoadedPlugin.Error"/>.</summary>
        public LoadedPlugin Load(PluginManifest manifest, string source = "user")
        {
            var entry = Find(manifest.Id);
            if (entry != null && entry.IsLoaded) Unload(entry.Id);
            if (entry == null)
            {
                entry = new LoadedPlugin();
                lock (_plugins) _plugins.Add(entry);
            }

            entry.Manifest = manifest;
            entry.Source = source;
            entry.Error = null;
            var problems = manifest.Validate(allowReserved: source == "script");
            if (problems.Count > 0)
            {
                entry.Error = string.Join(" ", problems);
                Changed?.Invoke(this, EventArgs.Empty);
                return entry;
            }

            try
            {
                var (asm, ctx) = LoadIsolated(manifest.AssemblyPath, manifest.Id);
                entry.Assembly = asm;
                entry.Context = ctx;
                entry.CanUnload = ctx != null;
                entry.LoadedAt = DateTime.Now;
                entry.Types = Registry.Register(asm, manifest);
                entry.PartIds = RegisterParts(entry);
                if (entry.Types.Count == 0)
                {
                    entry.Error = "Eklentide IWorldBehavior, IForceField, IWindSource veya IPartProvider uygulayan public bir sınıf bulunamadı.";
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException || ex is FileLoadException || ex is IOException || ex is ReflectionTypeLoadException || ex is TypeLoadException || ex is MissingMethodException || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is TargetInvocationException)
            {
                entry.Error = Describe(ex);
                Unload(entry.Id);
                entry.Error = Describe(ex);
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return entry;
        }

        public LoadedPlugin LoadAssemblyFile(string dllPath, string? id = null, string source = "user")
        {
            var m = PluginManifest.ForAssembly(dllPath, id);
            string manifest = Path.Combine(m.Directory, PluginManifest.FileName);
            if (id == null && File.Exists(manifest))
            {
                var read = PluginManifest.Read(manifest);
                if (string.Equals(read.Assembly, m.Assembly, StringComparison.OrdinalIgnoreCase)) m = read;
            }

            return Load(m, source);
        }

        /// <summary>Loads (or replaces) the compiled world scripts; their types get keys "script:Namespace.Type".</summary>
        public LoadedPlugin LoadScripts(string dllPath)
        {
            var m = PluginManifest.ForAssembly(dllPath, BehaviorRegistry.ScriptId);
            m.Name = "Dünya betikleri";
            m.Author = "kullanıcı";
            return Load(m, "script");
        }

        /// <summary>
        /// Registers an assembly that is already loaded (built-ins, tests, host code) through the
        /// same path as a plugin DLL: same type discovery, parameters and part registration.
        /// </summary>
        public LoadedPlugin RegisterAssembly(Assembly assembly, PluginManifest manifest, string source = "code")
        {
            var entry = Find(manifest.Id);
            if (entry == null)
            {
                entry = new LoadedPlugin();
                lock (_plugins) _plugins.Add(entry);
            }

            entry.Manifest = manifest;
            entry.Source = source;
            entry.Assembly = assembly;
            entry.CanUnload = false;
            entry.LoadedAt = DateTime.Now;
            entry.Error = null;
            entry.Types = Registry.Register(assembly, manifest);
            entry.PartIds = RegisterParts(entry);
            Changed?.Invoke(this, EventArgs.Empty);
            return entry;
        }

        /// <summary>
        /// Unregisters the plugin's types and parts and unloads its load context. Returns a weak
        /// reference to the context: it is collected once nothing else holds the plugin's objects.
        /// </summary>
        public WeakReference? Unload(string id)
        {
            var entry = Find(id);
            if (entry == null) return null;
            Registry.Unregister(entry.Id);
            lock (_partBuilders)
            {
                foreach (var key in _partBuilders.Where(kv => string.Equals(kv.Value.PluginId, entry.Id, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
                {
                    _partBuilders.Remove(key);
                }
            }

            WeakReference? weak = null;
#if NET5_0_OR_GREATER
            if (entry.Context is AssemblyLoadContext alc)
            {
                weak = new WeakReference(alc, trackResurrection: true);
                alc.Unload();
            }
#endif
            entry.Context = null;
            entry.Assembly = null;
            entry.Types = Array.Empty<SdkTypeInfo>();
            Changed?.Invoke(this, EventArgs.Empty);
            return weak;
        }

        /// <summary>Unloads and loads again from disk (after a rebuild). Stop the simulation first.</summary>
        public LoadedPlugin? Reload(string id)
        {
            var entry = Find(id);
            if (entry == null) return null;
            var manifest = entry.Manifest;
            if (manifest.ManifestPath != null && File.Exists(manifest.ManifestPath))
            {
                try
                {
                    manifest = PluginManifest.Read(manifest.ManifestPath);
                }
                catch (Exception ex) when (ex is IOException || ex is System.Text.Json.JsonException || ex is InvalidDataException)
                {
                    entry.Error = "plugin.json okunamadı: " + ex.Message;
                    return entry;
                }
            }

            Unload(id);
            return Load(manifest, entry.Source);
        }

        /// <summary>Forgets a plugin entirely (unloads it first).</summary>
        public WeakReference? Remove(string id)
        {
            var weak = Unload(id);
            lock (_plugins) _plugins.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            Changed?.Invoke(this, EventArgs.Empty);
            return weak;
        }

        private static string Describe(Exception ex)
        {
            if (ex is ReflectionTypeLoadException rtl)
            {
                var first = rtl.LoaderExceptions.FirstOrDefault(e => e != null);
                return "Türler yüklenemedi: " + (first?.Message ?? rtl.Message) + " (eklenti farklı bir SDK sürümüyle derlenmiş olabilir).";
            }

            if (ex is BadImageFormatException) return "Geçerli bir .NET derlemesi değil: " + ex.Message;
            if (ex is TargetInvocationException tie && tie.InnerException != null) return "Eklenti başlatılırken hata: " + tie.InnerException.Message;
            return ex.GetType().Name + ": " + ex.Message;
        }

        private static (Assembly Assembly, object? Context) LoadIsolated(string path, string id)
        {
            byte[] image = File.ReadAllBytes(path);
            string pdb = Path.ChangeExtension(path, ".pdb");
            byte[]? symbols = File.Exists(pdb) ? File.ReadAllBytes(pdb) : null;
#if NET5_0_OR_GREATER
            var ctx = new PluginLoadContext(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", id);
            using var ms = new MemoryStream(image);
            using var ps = symbols != null ? new MemoryStream(symbols) : null;
            var asm = ctx.LoadFromStream(ms, ps);
            return (asm, ctx);
#else
            return (symbols != null ? Assembly.Load(image, symbols) : Assembly.Load(image), null);
#endif
        }

#if NET5_0_OR_GREATER
        private sealed class PluginLoadContext : AssemblyLoadContext
        {
            private readonly string _directory;

            public PluginLoadContext(string directory, string id) : base("RobotWin eklentisi: " + id, isCollectible: true)
            {
                _directory = directory;
            }

            protected override Assembly? Load(AssemblyName name)
            {
                // Share the SDK and everything the host already has: one copy of each interface type.
                if (name.Name == null || name.Name.StartsWith("RobotTwin.CoreSim", StringComparison.OrdinalIgnoreCase)) return null;
                foreach (var a in Default.Assemblies)
                {
                    if (string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase)) return null;
                }

                string candidate = Path.Combine(_directory, name.Name + ".dll");
                if (!File.Exists(candidate)) return null;
                using var ms = new MemoryStream(File.ReadAllBytes(candidate));
                return LoadFromStream(ms);
            }
        }
#endif

        // ================================================================ custom parts

        private IReadOnlyList<string> RegisterParts(LoadedPlugin entry)
        {
            var ids = new List<string>();
            foreach (var t in entry.Types.Where(t => t.Kind == SdkTypeKind.PartProvider))
            {
                IPartProvider provider;
                try
                {
                    provider = (IPartProvider)BehaviorRegistry.Create(t, null);
                }
                catch (Exception ex) when (ex is TargetInvocationException || ex is MissingMethodException || ex is InvalidOperationException || ex is InvalidCastException)
                {
                    entry.Error = $"{t.Name} oluşturulamadı: {(ex.InnerException ?? ex).Message}";
                    continue;
                }

                try
                {
                    foreach (var def in provider.Parts())
                    {
                        if (def.Build == null || string.IsNullOrWhiteSpace(def.Id)) continue;
                        lock (_partBuilders) _partBuilders[def.Id] = (entry.Id, def.Build);
                        if (Parts.Find(def.Id) == null) Parts.Add(Forwarding(def));
                        ids.Add(def.Id);
                    }

                    foreach (var preset in provider.Presets())
                    {
                        if (Parts.FindPreset(preset.Id) != null || Parts.Find(preset.PartId) == null) continue;
                        Parts.AddPreset(new PartPreset(preset.Id, preset.Name, preset.PartId, preset.Category, preset.Description,
                            new Dictionary<string, string>(preset.Parameters.ToDictionary(k => k.Key, k => k.Value), StringComparer.OrdinalIgnoreCase)));
                    }
                }
                catch (Exception ex)
                {
                    entry.Error = $"{t.Name} parçaları eklenemedi: {ex.Message}";
                }
            }

            return ids;
        }

        /// <summary>
        /// A library copy of a plugin part whose Build forwards to the currently loaded plugin, so the
        /// library never holds the plugin's code (it can unload; the part then fails with a clear warning).
        /// </summary>
        private PartDefinition Forwarding(PartDefinition src)
        {
            var copy = new PartDefinition
            {
                Id = src.Id, Name = src.Name, Category = src.Category, Description = src.Description, RefPrefix = src.RefPrefix, Symbol = src.Symbol,
                SymbolVariant = src.SymbolVariant, HalfWidth = src.HalfWidth, HalfHeight = src.HalfHeight, Footprint = src.Footprint, ValueKey = src.ValueKey,
                NetName = src.NetName
            };
            copy.Pins.AddRange(src.Pins);
            copy.Parameters.AddRange(src.Parameters);
            copy.InternalConnections.AddRange(src.InternalConnections.Select(a => a.ToArray()));
            string id = src.Id;
            copy.Build = ctx =>
            {
                Action<PartBuildContext>? build;
                lock (_partBuilders) build = _partBuilders.TryGetValue(id, out var b) ? b.Build : null;
                if (build == null) throw new InvalidOperationException($"'{id}' parçasını sağlayan eklenti yüklü değil");
                build(ctx);
            };
            return copy;
        }
    }
}
