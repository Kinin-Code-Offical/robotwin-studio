using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RobotWin.Studio.Shell
{
    /// <summary>[workspaces] A saved workspace: the page it opens, the layout of each page's panels and its tool strips.</summary>
    public sealed class WorkspaceProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Page { get; set; } = "schematic";
        public Dictionary<string, DockLayoutState> Layouts { get; set; } = new Dictionary<string, DockLayoutState>();

        /// <summary>[workspaces] Each page's tool strips over its canvas, as <see cref="OverlayHost.SaveLayout"/> writes them.</summary>
        public Dictionary<string, string> Strips { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// [workspaces] Task-oriented layouts, as Blender's and Fusion's workspaces: a few built in (circuit design, measuring,
    /// code, PCB, mechanics, world, world simulation) and the user's own, saved from the layout on screen. Built-in ones
    /// only choose the page and its bottom row; a saved one brings back every panel where it was.
    /// </summary>
    public static class WorkspaceProfiles
    {
        /// <summary>
        /// A built-in workspace: its page, and on a page with panels whether the bottom row shows and on which tab; and its
        /// tool strips ("id=open|folded|closed,…"): as Fusion's workspaces change the toolbar, designing opens the tools,
        /// measuring and running fold them to their handle out of the way.
        /// </summary>
        public sealed record Builtin(string Name, string Page, string Description, bool Bottom = false, string? Tab = null, string? Strips = null);

        public static IReadOnlyList<Builtin> Builtins { get; } = new[]
        {
            new Builtin("Devre tasarımı", "schematic", "Şema geniş, alt panel kapalı, araç şeridi açık: parça yerleştirme ve kablolama", Strips: "tools=open,draw=closed"),
            new Builtin("Ölçüm", "schematic", "Şema ve altta osiloskop, araç şeridi katlı: simülasyonu izleme", true, "scope", "tools=folded,draw=closed"),
            new Builtin("Kod", "code", "Kartların kodu, derleme ve hata ayıklama"),
            new Builtin("PCB", "pcb", "Kart yerleşimi, yönlendirme ve DRC; araç şeridi açık", Strips: "tools=open,draw=closed"),
            new Builtin("Mekanik", "model", "Parça modelleme; araç şeridi açık", Strips: "tools=open"),
            new Builtin("Dünya", "world", "Sahne, palet ve denetçi; araçlar, ızgara ve yakalama açık: dünyayı kurma", false, null, "tools=open,grid=open"),
            new Builtin("Dünya simülasyonu", "world", "Sahne ve altta kablolar ve ölçümler; araçlar katlı, ızgara kapalı: çalışan dünyayı izleme", true, "cables", "tools=folded,grid=closed")
        };

        private static readonly string? TestPath = Environment.GetEnvironmentVariable("ROBOTWIN_WORKSPACES");

        private static string StorePath => string.IsNullOrWhiteSpace(TestPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "workspaces.json")
            : TestPath!;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        private sealed class Store
        {
            public string? Active { get; set; }
            public List<WorkspaceProfile> Saved { get; set; } = new List<WorkspaceProfile>();
        }

        private static Store? _store;

        private static Store Data
        {
            get
            {
                if (_store != null) return _store;
                try
                {
                    _store = File.Exists(StorePath) ? JsonSerializer.Deserialize<Store>(File.ReadAllText(StorePath), Json) : null;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                    _store = null;
                }

                return _store ??= new Store();
            }
        }

        /// <summary>The user's saved workspaces, in the order they were made.</summary>
        public static IReadOnlyList<WorkspaceProfile> Saved => Data.Saved;

        /// <summary>The workspace last chosen (built-in or saved), or null.</summary>
        public static string? Active
        {
            get => Data.Active;
            set
            {
                Data.Active = value;
                Persist();
            }
        }

        public static Builtin? FindBuiltin(string name) => Builtins.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.CurrentCultureIgnoreCase));

        public static WorkspaceProfile? Find(string name) => Data.Saved.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));

        /// <summary>A name not taken by a built-in or a saved workspace ("Benim düzenim", "Benim düzenim 2" …).</summary>
        public static string FreeName(string wanted)
        {
            string name = wanted.Trim().Length > 0 ? wanted.Trim() : "Çalışma alanı";
            if (FindBuiltin(name) == null && Find(name) == null) return name;
            for (int i = 2; ; i++)
            {
                string candidate = name + " " + i;
                if (FindBuiltin(candidate) == null && Find(candidate) == null) return candidate;
            }
        }

        /// <summary>Adds or replaces a saved workspace and makes it the active one.</summary>
        public static void Save(WorkspaceProfile profile)
        {
            if (FindBuiltin(profile.Name) != null) throw new ArgumentException("Hazır bir çalışma alanının adı: " + profile.Name);
            Data.Saved.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.CurrentCultureIgnoreCase));
            Data.Saved.Add(profile);
            Data.Active = profile.Name;
            Persist();
        }

        public static bool Rename(string from, string to)
        {
            var p = Find(from);
            if (p == null || to.Trim().Length == 0 || FindBuiltin(to) != null || (Find(to) != null && Find(to) != p)) return false;
            p.Name = to.Trim();
            if (string.Equals(Data.Active, from, StringComparison.CurrentCultureIgnoreCase)) Data.Active = p.Name;
            Persist();
            return true;
        }

        public static bool Delete(string name)
        {
            int n = Data.Saved.RemoveAll(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
            if (n == 0) return false;
            if (string.Equals(Data.Active, name, StringComparison.CurrentCultureIgnoreCase)) Data.Active = null;
            Persist();
            return true;
        }

        // ------------------------------------------------------------------ [workspaces] a project's own layout

        /// <summary>The name a project's own layout goes by (in the menu and the title bar).</summary>
        public const string ProjectLayoutName = "Projenin düzeni";

        private const string ProjectKey = "workspace";

        /// <summary>
        /// The layout kept in a project's settings (the package's app-owned settings.json), as KiCad keeps a project's view
        /// in its .kicad_prl and Visual Studio a solution's windows: null when it has none or it cannot be read.
        /// </summary>
        public static WorkspaceProfile? FromProject(string? settings)
        {
            try
            {
                return ProjectSettings.Get(settings, ProjectKey)?.Deserialize<WorkspaceProfile>(Json);
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException)
            {
                return null;
            }
        }

        /// <summary>The project's settings with its layout set (null: taken out); other settings it holds stay.</summary>
        public static string? WithProjectLayout(string? settings, WorkspaceProfile? profile) =>
            ProjectSettings.With(settings, ProjectKey, profile == null ? null : JsonSerializer.SerializeToNode(profile, Json));

        /// <summary>Forgets what was read (tests point the store elsewhere between cases).</summary>
        public static void Reload() => _store = null;

        private static void Persist()
        {
            if (Unattended.Active && string.IsNullOrWhiteSpace(TestPath)) return;   // a snapshot or test run writes nothing of the user's
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(Data, Json));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
