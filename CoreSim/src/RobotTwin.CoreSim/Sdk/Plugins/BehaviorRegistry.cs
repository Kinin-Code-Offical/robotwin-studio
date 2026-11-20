using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sdk
{
    public enum SdkTypeKind
    {
        Behavior,
        ForceField,
        WindSource,
        PartProvider
    }

    /// <summary>A [Parameter] property of a behaviour or field type.</summary>
    public sealed class SdkParameterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ValueType { get; set; } = "double";
        public string DefaultText { get; set; } = string.Empty;
        public double Min { get; set; } = double.NegativeInfinity;
        public double Max { get; set; } = double.PositiveInfinity;
    }

    /// <summary>A usable type from a plugin, a script or the built-ins.</summary>
    public sealed class SdkTypeInfo
    {
        internal SdkTypeInfo(string key, string pluginId, Type type, SdkTypeKind kind)
        {
            Key = key;
            PluginId = pluginId;
            Type = type;
            Kind = kind;
        }

        /// <summary>"pluginId:Namespace.Type" — what attachments store.</summary>
        public string Key { get; }

        public string PluginId { get; }

        public Type Type { get; internal set; }

        public SdkTypeKind Kind { get; }

        public string Name { get; internal set; } = string.Empty;
        public string Description { get; internal set; } = string.Empty;

        /// <summary>"object" or "world".</summary>
        public string AttachTo { get; internal set; } = "object";

        public IReadOnlyList<SdkParameterInfo> Parameters { get; internal set; } = Array.Empty<SdkParameterInfo>();

        public override string ToString() => Name + " (" + Key + ")";
    }

    /// <summary>
    /// The catalogue of behaviour, field, wind-source and part-provider types. Plugins, scripts,
    /// built-ins and tests all register through <see cref="Register"/>, so they are found,
    /// parameterised and isolated the same way.
    /// </summary>
    public sealed class BehaviorRegistry
    {
        public const string BuiltinId = "builtin";
        public const string ScriptId = "script";

        private readonly object _lock = new object();
        private readonly List<SdkTypeInfo> _types = new List<SdkTypeInfo>();

        private static readonly Lazy<BehaviorRegistry> SharedRegistry = new Lazy<BehaviorRegistry>(() =>
        {
            var r = new BehaviorRegistry();
            r.Register(typeof(BehaviorRegistry).Assembly, new PluginManifest
            {
                Id = BuiltinId, Name = "Yerleşik davranışlar", Author = "RobotWin", Version = SdkInfo.Version,
                Description = "Sabit kuvvet, yay–sönüm, manyetik çekim, rüzgâr türbini, özel sürükleme."
            }, onlyNamespace: "RobotTwin.CoreSim.Sdk.Builtins");
            return r;
        });

        /// <summary>The process-wide registry (built-ins are always there).</summary>
        public static BehaviorRegistry Shared => SharedRegistry.Value;

        public event EventHandler? Changed;

        public IReadOnlyList<SdkTypeInfo> Types
        {
            get
            {
                lock (_lock) return _types.ToList();
            }
        }

        public SdkTypeInfo? Find(string key)
        {
            lock (_lock) return _types.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
        }

        public IReadOnlyList<SdkTypeInfo> OfKind(SdkTypeKind kind)
        {
            lock (_lock) return _types.Where(t => t.Kind == kind).ToList();
        }

        public static string PluginOf(string key)
        {
            int c = key.IndexOf(':');
            return c > 0 ? key.Substring(0, c) : key;
        }

        /// <summary>Registers every public, concrete SDK type of an assembly (or the manifest's entry types).</summary>
        public IReadOnlyList<SdkTypeInfo> Register(Assembly assembly, PluginManifest manifest, string? onlyNamespace = null)
        {
            var added = new List<SdkTypeInfo>();
            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            var entry = new HashSet<string>(manifest.EntryTypes ?? new List<string>(), StringComparer.Ordinal);
            foreach (var t in types)
            {
                if (!t.IsClass || t.IsAbstract || t.IsGenericTypeDefinition) continue;
                if (onlyNamespace != null && t.Namespace != onlyNamespace) continue;
                if (entry.Count > 0 && !entry.Contains(t.FullName ?? t.Name) && !entry.Contains(t.Name)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                foreach (var kind in KindsOf(t))
                {
                    var info = Describe(t, manifest.Id, kind);
                    added.Add(info);
                }
            }

            lock (_lock)
            {
                _types.RemoveAll(x => x.PluginId == manifest.Id && added.Any(a => a.Key == x.Key));
                _types.AddRange(added);
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return added;
        }

        /// <summary>Registers a single type (tests, hosts that build behaviours in code).</summary>
        public SdkTypeInfo RegisterType(Type type, string pluginId)
        {
            var kind = KindsOf(type).FirstOrDefault();
            var info = Describe(type, pluginId, kind);
            lock (_lock)
            {
                _types.RemoveAll(x => x.Key == info.Key);
                _types.Add(info);
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return info;
        }

        /// <summary>Forgets every type of a plugin (needed before its load context can unload).</summary>
        public int Unregister(string pluginId)
        {
            int n;
            lock (_lock) n = _types.RemoveAll(t => t.PluginId == pluginId);
            if (n > 0) Changed?.Invoke(this, EventArgs.Empty);
            return n;
        }

        private static IEnumerable<SdkTypeKind> KindsOf(Type t)
        {
            if (typeof(IWorldBehavior).IsAssignableFrom(t)) yield return SdkTypeKind.Behavior;
            if (typeof(IForceField).IsAssignableFrom(t)) yield return SdkTypeKind.ForceField;
            else if (typeof(IWindSource).IsAssignableFrom(t)) yield return SdkTypeKind.WindSource;
            if (typeof(IPartProvider).IsAssignableFrom(t)) yield return SdkTypeKind.PartProvider;
        }

        private static SdkTypeInfo Describe(Type t, string pluginId, SdkTypeKind kind)
        {
            string key = pluginId + ":" + (t.FullName ?? t.Name);
            var info = new SdkTypeInfo(key, pluginId, t, kind);
            var attr = t.GetCustomAttribute<BehaviorAttribute>();
            info.Name = attr?.Name ?? t.Name;
            info.Description = attr?.Description ?? string.Empty;
            info.AttachTo = attr?.AttachTo ?? (kind == SdkTypeKind.Behavior ? "object" : "world");
            object? sample = null;
            try
            {
                sample = Activator.CreateInstance(t);
            }
            catch (Exception)
            {
                // Defaults stay empty; the error shows up when the behaviour is created for a run.
            }

            var list = new List<SdkParameterInfo>();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var pa = p.GetCustomAttribute<ParameterAttribute>();
                if (pa == null || !p.CanWrite || !IsSupported(p.PropertyType)) continue;
                string def = string.Empty;
                try
                {
                    if (sample != null) def = Format(p.GetValue(sample));
                }
                catch (Exception)
                {
                    def = string.Empty;
                }

                list.Add(new SdkParameterInfo
                {
                    Name = p.Name, Label = string.IsNullOrEmpty(pa.Label) ? p.Name : pa.Label, Unit = pa.Unit, Description = pa.Description,
                    ValueType = TypeName(p.PropertyType), DefaultText = def, Min = pa.Min, Max = pa.Max
                });
            }

            info.Parameters = list;
            if (sample is IDisposable d) d.Dispose();
            return info;
        }

        private static bool IsSupported(Type t) =>
            t == typeof(double) || t == typeof(float) || t == typeof(int) || t == typeof(long) || t == typeof(bool) || t == typeof(string) || t == typeof(Vec3);

        private static string TypeName(Type t) =>
            t == typeof(bool) ? "bool" : t == typeof(string) ? "string" : t == typeof(Vec3) ? "vector" : t == typeof(int) || t == typeof(long) ? "int" : "double";

        public static string Format(object? v) => v switch
        {
            null => string.Empty,
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            Vec3 x => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", x.X, x.Y, x.Z),
            IFormattable fm => fm.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? string.Empty
        };

        /// <summary>Creates an instance and fills its [Parameter] properties; bad values are reported through <paramref name="warn"/>.</summary>
        public static object Create(SdkTypeInfo info, IReadOnlyDictionary<string, string>? values, Action<string>? warn = null)
        {
            var instance = Activator.CreateInstance(info.Type) ?? throw new InvalidOperationException(info.Type.FullName + " oluşturulamadı.");
            if (values != null) Bind(instance, info, values, warn);
            return instance;
        }

        public static void Bind(object instance, SdkTypeInfo info, IReadOnlyDictionary<string, string> values, Action<string>? warn)
        {
            foreach (var p in info.Parameters)
            {
                if (!values.TryGetValue(p.Name, out var text) || text == null) continue;
                var prop = instance.GetType().GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) continue;
                if (!TryParse(text, prop.PropertyType, out object? value))
                {
                    warn?.Invoke($"'{p.Label}' için '{text}' geçerli bir değer değil; varsayılan ({p.DefaultText}) kullanılıyor.");
                    continue;
                }

                if (value is double dv && (dv < p.Min || dv > p.Max))
                {
                    double c = Math.Max(p.Min, Math.Min(p.Max, dv));
                    warn?.Invoke($"'{p.Label}' = {dv.ToString(CultureInfo.InvariantCulture)} aralık dışında; {c.ToString(CultureInfo.InvariantCulture)} kullanılıyor.");
                    value = c;
                }

                prop.SetValue(instance, value is double d2 && prop.PropertyType == typeof(float) ? (float)d2 : value);
            }
        }

        public static bool TryParse(string text, Type t, out object? value)
        {
            value = null;
            text = text.Trim();
            var inv = CultureInfo.InvariantCulture;
            if (t == typeof(string))
            {
                value = text;
                return true;
            }

            if (t == typeof(bool))
            {
                string l = text.ToLowerInvariant();
                if (l == "true" || l == "1" || l == "evet" || l == "açık") value = true;
                else if (l == "false" || l == "0" || l == "hayır" || l == "kapalı") value = false;
                return value != null;
            }

            if (t == typeof(int) || t == typeof(long))
            {
                if (!long.TryParse(text, NumberStyles.Integer, inv, out long l)) return false;
                value = t == typeof(int) ? (object)(int)Math.Max(int.MinValue, Math.Min(int.MaxValue, l)) : l;
                return true;
            }

            if (t == typeof(Vec3))
            {
                var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3) return false;
                var v = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    if (!double.TryParse(parts[i], NumberStyles.Float, inv, out v[i])) return false;
                }

                value = new Vec3(v[0], v[1], v[2]);
                return true;
            }

            if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, inv, out double d))
            {
                value = d;
                return true;
            }

            return false;
        }
    }
}
