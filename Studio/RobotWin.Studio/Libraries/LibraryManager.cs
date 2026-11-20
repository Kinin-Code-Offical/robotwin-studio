using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Libraries;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Libraries
{
    // [libs] Studio side of the library catalog: load order for UserLibrary, installing an
    // import (parts + footprints), enabling/disabling/removing/updating libraries at run time.

    public static class LibraryManager
    {
        private static LibraryCatalog? _catalog;

        public static LibraryCatalog Catalog => _catalog ??= LibraryCatalog.Load(UserLibrary.Folder);

        /// <summary>Snapshots: show a sample catalog from a temporary folder instead of the user's library.</summary>
        internal static void UseCatalog(LibraryCatalog catalog) => _catalog = catalog;

        /// <summary>Arduino libraries installed from the Kütüphane page (passed to arduino-cli with --libraries).</summary>
        public static string ArduinoFolder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "Arduino", "libraries");

        public static event EventHandler? Changed;

        /// <summary>Re-reads the folder (files added by hand, removed outside the app).</summary>
        public static void Refresh()
        {
            _catalog = LibraryCatalog.Load(UserLibrary.Folder);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Files the user library loads, enabled libraries only, in priority order.</summary>
        public static IEnumerable<string> LoadOrder(string folder)
        {
            try
            {
                return Catalog.LoadOrder();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                UserLibrary.LoadErrors.Add("Kütüphane listesi: " + ex.Message);
                return Directory.Exists(folder) ? Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>();
            }
        }

        public static bool Accepts(string file, CustomPart part) => Catalog.Accepts(file, part.Id);

        /// <summary>Registers the parts of a library file into the catalog; returns the number that were new.</summary>
        public static int RegisterParts(PartLibrary library, PartLibraryFile file, string? path = null)
        {
            int added = 0;
            foreach (var part in file.Parts)
            {
                if (path != null && !Catalog.Accepts(path, part.Id)) continue;
                try
                {
                    if (part.Register(library)) added++;
                    UserLibrary.PartIds.Add(part.Id);
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException)
                {
                    UserLibrary.LoadErrors.Add(part.Name + ": " + ex.Message);
                }
            }

            return added;
        }

        /// <summary>
        /// Installs an import: footprints into the user footprint library (so they survive a restart
        /// even when no symbol uses them), the parts into a new .rwlib and the catalog, then registers them.
        /// </summary>
        public static InstalledLibrary? Install(PartLibrary library, PartLibraryFile file, LibrarySourceKind source, IEnumerable<string> sourcePaths,
            IEnumerable<Footprint>? footprints, ImportLog log, string? replaceId = null)
        {
            int fpSaved = 0;
            foreach (var fp in footprints ?? Enumerable.Empty<Footprint>())
            {
                try
                {
                    if (FootprintLibrary.IsBuiltIn(fp.Id)) continue;
                    UserLibrary.SaveFootprint(fp);
                    fpSaved++;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    log.Warn($"footprint '{fp.Id}' kaydedilemedi: {ex.Message}");
                }
            }

            if (fpSaved > 0) log.Info($"{fpSaved} footprint kullanıcı kılıf kütüphanesine kaydedildi.");
            if (file.Parts.Count == 0)
            {
                if (fpSaved == 0) log.Error("içe aktarılacak parça yok.");
                return null;
            }

            InstalledLibrary entry;
            try
            {
                if (replaceId != null) Unload(library, replaceId);
                entry = Catalog.Install(file, source, sourcePaths, replaceId);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                log.Error("kütüphane kaydedilemedi: " + ex.Message);
                return null;
            }

            int added = RegisterParts(library, file, Path.Combine(Catalog.Folder, entry.File));
            int shadowed = file.Parts.Count - added;
            log.Info($"'{entry.Name}' kuruldu: {file.Parts.Count} parça ({added} yeni" + (shadowed > 0 ? $", {shadowed} parça kimliği başka kütüphanede de var — Kütüphane yöneticisinde çakışmaları çözün" : string.Empty) + ").");
            Changed?.Invoke(null, EventArgs.Empty);
            UserLibrary.NotifyChanged();
            return entry;
        }

        private static IEnumerable<CustomPart> PartsOf(InstalledLibrary lib)
        {
            if (lib.Source == LibrarySourceKind.Arduino) yield break;
            var files = lib.Id == LibraryCatalog.UserPartsId
                ? Directory.EnumerateFiles(Catalog.Folder, "*" + RobotWinFormats.PartExtension).ToList()
                : new List<string> { Path.Combine(Catalog.Folder, lib.File) };
            foreach (var f in files)
            {
                IEnumerable<CustomPart> parts;
                try
                {
                    parts = f.EndsWith(RobotWinFormats.PartExtension, StringComparison.OrdinalIgnoreCase) ? new[] { CustomPart.Load(f) } : PartLibraryFile.Load(f).Parts;
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
                {
                    UserLibrary.LoadErrors.Add(Path.GetFileName(f) + ": " + ex.Message);
                    continue;
                }

                foreach (var p in parts) yield return p;
            }
        }

        /// <summary>Takes a library's parts out of the running catalog (placed parts keep working until the project is reloaded only if another library provides them).</summary>
        public static int Unload(PartLibrary library, string libraryId)
        {
            var lib = Catalog.Find(libraryId);
            if (lib == null) return 0;
            int removed = 0;
            foreach (var id in lib.PartIds)
            {
                var preset = library.FindPreset(id);
                if (preset == null) continue;
                // Only when no other enabled library also provides this id.
                if (Catalog.Libraries.Any(l => l != lib && l.Enabled && l.PartIds.Contains(id, StringComparer.OrdinalIgnoreCase))) continue;
                library.RemovePreset(id);
                if (preset.PartId.StartsWith("udef-", StringComparison.OrdinalIgnoreCase)) library.Remove(preset.PartId);
                PartModels3D.Unregister(id);
                UserLibrary.PartIds.Remove(id);
                removed++;
            }

            return removed;
        }

        /// <summary>Loads (registers) the parts of an enabled library.</summary>
        public static int Load(PartLibrary library, string libraryId)
        {
            var lib = Catalog.Find(libraryId);
            if (lib == null) return 0;
            int added = 0;
            string fileKey = lib.Id == LibraryCatalog.UserPartsId ? Path.Combine(Catalog.Folder, "x" + RobotWinFormats.PartExtension) : Path.Combine(Catalog.Folder, lib.File);
            foreach (var part in PartsOf(lib))
            {
                if (!Catalog.Accepts(fileKey, part.Id)) continue;
                try
                {
                    if (part.Register(library)) added++;
                    UserLibrary.PartIds.Add(part.Id);
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException)
                {
                    UserLibrary.LoadErrors.Add(part.Name + ": " + ex.Message);
                }
            }

            return added;
        }

        public static void SetEnabled(PartLibrary library, string libraryId, bool enabled)
        {
            var lib = Catalog.Find(libraryId);
            if (lib == null || lib.Enabled == enabled) return;
            if (!enabled) Unload(library, libraryId);
            Catalog.SetEnabled(libraryId, enabled);
            if (enabled) Load(library, libraryId);
            Changed?.Invoke(null, EventArgs.Empty);
            UserLibrary.NotifyChanged();
        }

        public static bool Remove(PartLibrary library, string libraryId)
        {
            Unload(library, libraryId);
            bool ok = Catalog.Remove(libraryId);
            Changed?.Invoke(null, EventArgs.Empty);
            UserLibrary.NotifyChanged();
            return ok;
        }

        /// <summary>Applies a conflict resolution at once: the chosen library's copy replaces the loaded one.</summary>
        public static void Resolve(PartLibrary library, string partId, string libraryId)
        {
            Catalog.Resolve(partId, libraryId);
            var lib = Catalog.Find(libraryId);
            if (lib == null) return;
            var part = PartsOf(lib).FirstOrDefault(p => string.Equals(p.Id, partId, StringComparison.OrdinalIgnoreCase));
            if (part == null) return;
            var old = library.FindPreset(partId);
            if (old != null)
            {
                library.RemovePreset(partId);
                if (old.PartId.StartsWith("udef-", StringComparison.OrdinalIgnoreCase)) library.Remove(old.PartId);
            }

            try
            {
                part.Register(library);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException)
            {
                UserLibrary.LoadErrors.Add(part.Name + ": " + ex.Message);
            }

            Changed?.Invoke(null, EventArgs.Empty);
            UserLibrary.NotifyChanged();
        }

        /// <summary>Number of placed parts in the open project that come from these preset ids.</summary>
        public static int UsedInOpenProject(IEnumerable<string> presetIds)
        {
            var set = new HashSet<string>(presetIds, StringComparer.OrdinalIgnoreCase);
            try
            {
                return Workspace.Current.Schematic.Parts.Count(p => p.PresetId != null && set.Contains(p.PresetId));
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }

        private static (string Path, DateTime Stamp, PartLibraryFile File)? _lastFile;

        /// <summary>Library files can be large (embedded footprints and meshes): keep the last one read.</summary>
        private static PartLibraryFile CachedFile(string path)
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_lastFile is { } c && c.Path == path && c.Stamp == stamp) return c.File;
            var file = PartLibraryFile.Load(path);
            _lastFile = (path, stamp, file);
            return file;
        }

        /// <summary>The stored copy of a user / imported part: its own .rwpart, else the copy inside its library file.</summary>
        public static CustomPart? FindUserPart(string presetId)
        {
            try
            {
                string own = Path.Combine(UserLibrary.Folder, presetId + RobotWinFormats.PartExtension);
                if (File.Exists(own)) return CustomPart.Load(own);
                foreach (var lib in Catalog.Libraries.Where(l => l.Enabled && l.Source != LibrarySourceKind.Arduino && l.Id != LibraryCatalog.UserPartsId && l.PartIds.Contains(presetId, StringComparer.OrdinalIgnoreCase)))
                {
                    var p = CachedFile(Path.Combine(Catalog.Folder, lib.File)).Parts.FirstOrDefault(x => string.Equals(x.Id, presetId, StringComparison.OrdinalIgnoreCase));
                    if (p != null) return p;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
            {
            }

            return null;
        }

        /// <summary>
        /// Copies every user/imported part the design uses into the project package, so a .rwproj opens
        /// with its parts on a computer that does not have the same libraries.
        /// </summary>
        public static int EmbedUsedParts(ProjectPackage project)
        {
            int added = 0;
            var have = new HashSet<string>(project.Parts.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var id in project.Schematic.Parts.Select(p => p.PresetId).Where(id => id != null && UserLibrary.PartIds.Contains(id!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                if (have.Contains(id!)) continue;
                var part = FindUserPart(id!);
                if (part == null) continue;
                project.Parts.Add(part);
                have.Add(part.Id);
                added++;
            }

            return added;
        }

        /// <summary>Saves (or replaces) one user part as .rwpart and (re-)registers it.</summary>
        public static string SaveUserPart(PartLibrary library, CustomPart part, string? previousId = null)
        {
            foreach (var id in new[] { previousId, part.Id }.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var old = library.FindPreset(id!);
                if (old == null) continue;
                library.RemovePreset(id!);
                if (old.PartId.StartsWith("udef-", StringComparison.OrdinalIgnoreCase)) library.Remove(old.PartId);
            }

            if (part.Definition != null) library.Remove(part.BasePartId);
            if (previousId != null && !string.Equals(previousId, part.Id, StringComparison.OrdinalIgnoreCase))
            {
                string oldFile = Path.Combine(UserLibrary.Folder, previousId + RobotWinFormats.PartExtension);
                if (File.Exists(oldFile)) File.Delete(oldFile);
            }

            string path = UserLibrary.Save(library, part);
            Catalog.Sync();
            Catalog.Save();
            Changed?.Invoke(null, EventArgs.Empty);
            return path;
        }
    }
}
