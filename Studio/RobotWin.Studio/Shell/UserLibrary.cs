using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// The user's own parts (<c>.rwpart</c>) and libraries (<c>.rwlib</c>) kept in
    /// %APPDATA%\RobotWinStudio\Library and loaded into the catalog at start-up.
    /// </summary>
    public static class UserLibrary
    {
        public static string Folder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "Library");

        /// <summary>Ids of parts that came from the user library (for the "Kullanıcı" badge).</summary>
        public static HashSet<string> PartIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static List<string> LoadErrors { get; } = new List<string>();

        public static event EventHandler? Changed;

        /// <summary>User footprints (<c>.rwfp</c>) made with the footprint editor.</summary>
        public static string FootprintFolder { get; } = Path.Combine(Folder, "Footprints");

        /// <summary>Registers every saved user footprint (before parts, which may reference them).</summary>
        public static void LoadFootprints()
        {
            if (!Directory.Exists(FootprintFolder)) return;
            foreach (var file in Directory.EnumerateFiles(FootprintFolder, "*" + CustomFootprint.Extension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    RobotTwin.CoreSim.Pcb.FootprintLibrary.Register(CustomFootprint.Load(file).ToFootprint());
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    LoadErrors.Add(Path.GetFileName(file) + ": " + ex.Message);
                }
            }
        }

        /// <summary>Saves a footprint to the user footprint library (replacing a file with the same id).</summary>
        public static string SaveFootprint(RobotTwin.CoreSim.Pcb.Footprint footprint)
        {
            Directory.CreateDirectory(FootprintFolder);
            var invalid = Path.GetInvalidFileNameChars();
            string name = new string(footprint.Id.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            string path = Path.Combine(FootprintFolder, name + CustomFootprint.Extension);
            CustomFootprint.From(footprint).Save(path);
            RobotTwin.CoreSim.Pcb.FootprintLibrary.Register(footprint);
            Changed?.Invoke(null, EventArgs.Empty);
            return path;
        }

        public static void LoadAll(PartLibrary library)
        {
            LoadFootprints();
            if (!Directory.Exists(Folder)) return;
            foreach (var file in RobotWin.Studio.Libraries.LibraryManager.LoadOrder(Folder)) // [libs] enabled libraries, by priority
            {
                try
                {
                    foreach (var part in Read(file).Where(p => RobotWin.Studio.Libraries.LibraryManager.Accepts(file, p))) Register(library, part); // [libs] conflict resolution
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException || ex is InvalidOperationException) // [libs] + duplicate definitions
                {
                    LoadErrors.Add(Path.GetFileName(file) + ": " + ex.Message);
                }
            }
        }

        /// <summary>[libs] Raises <see cref="Changed"/> for changes made by the library manager.</summary>
        public static void NotifyChanged() => Changed?.Invoke(null, EventArgs.Empty);

        private static IEnumerable<CustomPart> Read(string file)
        {
            string ext = Path.GetExtension(file);
            if (string.Equals(ext, RobotWinFormats.PartExtension, StringComparison.OrdinalIgnoreCase)) return new[] { CustomPart.Load(file) };
            if (string.Equals(ext, RobotWinFormats.LibraryExtension, StringComparison.OrdinalIgnoreCase)) return PartLibraryFile.Load(file).Parts;
            return Array.Empty<CustomPart>();
        }

        private static bool Register(PartLibrary library, CustomPart part)
        {
            bool added = part.Register(library);
            PartIds.Add(part.Id);
            return added;
        }

        /// <summary>Copies a .rwpart/.rwlib into the user library and adds its parts. Returns the parts that were new.</summary>
        public static IReadOnlyList<CustomPart> Import(PartLibrary library, string path)
        {
            var parts = Read(path).ToList();
            if (parts.Count == 0) throw new InvalidDataException("Dosyada parça yok.");
            Directory.CreateDirectory(Folder);
            string target = Path.Combine(Folder, Path.GetFileName(path));
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(path, target, true);
            RobotWin.Studio.Libraries.LibraryManager.Refresh(); // [libs] list the copied file in the library manager
            var added = parts.Where(p => Register(library, p)).ToList();
            Changed?.Invoke(null, EventArgs.Empty);
            return added;
        }

        /// <summary>Saves a new user part into the library folder and registers it.</summary>
        public static string Save(PartLibrary library, CustomPart part)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, part.Id + RobotWinFormats.PartExtension);
            part.Save(path);
            Register(library, part);
            Changed?.Invoke(null, EventArgs.Empty);
            return path;
        }

        /// <summary>
        /// Attaches (or with null removes) the user's own 3D model of a part: written into the part's
        /// .rwpart in the user library (created from the preset if the part came from a bundle)
        /// and applied to the loaded catalog at once.
        /// </summary>
        public static void SetModel(PartLibrary library, PartPreset preset, PartModel3D? model)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, preset.Id + RobotWinFormats.PartExtension);
            var part = File.Exists(path) ? CustomPart.Load(path) : CustomPart.FromPreset(preset, preset.Id, preset.Name);
            part.Model3D = model;
            part.Parameters.Remove(PartModels3D.ParameterKey);
            part.Save(path);
            PartModels3D.Attach(library, preset.Id, model);
            PartIds.Add(preset.Id);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Makes a unique preset id from a display name.</summary>
        public static string MakeId(PartLibrary library, string name)
        {
            var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray();
            string stem = "user-" + string.Join("-", new string(chars).Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
            if (stem == "user-") stem = "user-part";
            string id = stem;
            for (int i = 2; library.FindPreset(id) != null; i++) id = stem + "-" + i;
            return id;
        }

        /// <summary>Bundles all user parts into one .rwlib to share.</summary>
        public static void Export(PartLibrary library, string path, string name)
        {
            var bundle = new PartLibraryFile { Name = name, Author = Environment.UserName };
            if (Directory.Exists(Folder))
            {
                foreach (var file in Directory.EnumerateFiles(Folder))
                {
                    try
                    {
                        foreach (var p in Read(file))
                        {
                            if (bundle.Parts.All(x => x.Id != p.Id)) bundle.Parts.Add(p);
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException)
                    {
                    }
                }
            }

            bundle.Save(path);
        }
    }
}
