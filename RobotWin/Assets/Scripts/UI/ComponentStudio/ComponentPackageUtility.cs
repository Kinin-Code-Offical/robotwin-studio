using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RobotTwin.UI
{
    public static class ComponentPackageUtility
    {
        public const string PackageExtension = ".rtcomp";
        public const string DefinitionFileName = "component.json";
        private const string AssetsFolder = "assets";
        private const int HeaderSize = 24;
        private static readonly byte[] PackageMagic = Encoding.ASCII.GetBytes("RTCOMP\0\0");

        public static void MigrateUserJsonToPackages()
        {
            string root = ComponentCatalog.GetUserComponentRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
            foreach (var jsonFile in files)
            {
                if (IsWithinPackageDir(jsonFile)) continue;
                if (string.Equals(Path.GetFileName(jsonFile), DefinitionFileName, StringComparison.OrdinalIgnoreCase)) continue;

                string packagePath = Path.ChangeExtension(jsonFile, PackageExtension);
                if (File.Exists(packagePath)) continue;

                string json = File.ReadAllText(jsonFile);
                if (string.IsNullOrWhiteSpace(json)) continue;

                string modelSource = ResolveModelSource(jsonFile, json);
                if (!string.IsNullOrWhiteSpace(modelSource))
                {
                    json = UpdateModelFileInJson(json, BuildAssetEntryName(Path.GetFileName(modelSource)));
                }
                SavePackage(packagePath, json, modelSource);
            }
        }

        public static void MigrateBundledJsonToPackages()
        {
            string root = ComponentCatalog.GetUserComponentRoot();
            if (string.IsNullOrWhiteSpace(root)) return;

            string bundledRoot = Path.Combine(root, "Bundled");
            Directory.CreateDirectory(bundledRoot);

            var resources = Resources.LoadAll<TextAsset>("Components");
            if (resources != null)
            {
                foreach (var asset in resources)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.text)) continue;
                    string packageName = SanitizeFileName(asset.name);
                    string packagePath = Path.Combine(bundledRoot, packageName + PackageExtension);
                    if (File.Exists(packagePath)) continue;
                    SavePackage(packagePath, asset.text, null);
                }
            }

            try
            {
                string streamingRoot = Path.Combine(Application.streamingAssetsPath, "Components");
                if (!Directory.Exists(streamingRoot)) return;

                var files = Directory.GetFiles(streamingRoot, "*.json", SearchOption.AllDirectories);
                foreach (var jsonFile in files)
                {
                    if (IsWithinPackageDir(jsonFile)) continue;
                    if (string.Equals(Path.GetFileName(jsonFile), DefinitionFileName, StringComparison.OrdinalIgnoreCase)) continue;

                    string packageName = SanitizeFileName(Path.GetFileNameWithoutExtension(jsonFile));
                    string packagePath = Path.Combine(bundledRoot, packageName + PackageExtension);
                    if (File.Exists(packagePath)) continue;

                    string json = File.ReadAllText(jsonFile);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    string modelSource = ResolveModelSource(jsonFile, json);
                    if (!string.IsNullOrWhiteSpace(modelSource))
                    {
                        json = UpdateModelFileInJson(json, BuildAssetEntryName(Path.GetFileName(modelSource)));
                    }
                    SavePackage(packagePath, json, modelSource);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComponentPackage] Failed to migrate bundled components: {ex.Message}");
            }
        }

        public static bool IsPackagePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                path.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryReadDefinitionJson(string packagePath, out string json)
        {
            json = string.Empty;
            if (!File.Exists(packagePath)) return false;

            try
            {
                using var stream = File.OpenRead(packagePath);
                if (!TryOpenArchiveRead(stream, out var archive))
                {
                    return false;
                }
                using (archive)
                {
                    var entry = archive.GetEntry(DefinitionFileName);
                    if (entry == null) return false;
                    using var reader = new StreamReader(entry.Open());
                    json = reader.ReadToEnd();
                    return !string.IsNullOrWhiteSpace(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComponentPackage] Failed to read {packagePath}: {ex.Message}");
                return false;
            }
        }

        public static bool TryExtractEntryToCache(string packagePath, string entryName, out string extractedPath)
        {
            extractedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(entryName)) return false;
            if (!File.Exists(packagePath)) return false;

            var normalized = NormalizeEntryPath(entryName);
            if (string.IsNullOrWhiteSpace(normalized)) return false;

            string cacheRoot = GetCacheRoot();
            string packageKey = SanitizeFileName(Path.GetFileNameWithoutExtension(packagePath));
            string targetDir = Path.Combine(cacheRoot, packageKey);
            string targetPath = Path.Combine(targetDir, normalized.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetDir);
                if (File.Exists(targetPath))
                {
                    extractedPath = targetPath;
                    return true;
                }

                using var stream = File.OpenRead(packagePath);
                if (!TryOpenArchiveRead(stream, out var archive))
                {
                    return false;
                }
                using (archive)
                {
                    var entry = archive.GetEntry(normalized);
                    if (entry == null) return false;
                    using var entryStream = entry.Open();
                    using var output = File.Create(targetPath);
                    entryStream.CopyTo(output);
                    extractedPath = targetPath;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComponentPackage] Failed to extract {entryName}: {ex.Message}");
                return false;
            }
        }

        public static void SavePackage(string packagePath, string definitionJson, string modelSourcePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath)) return;
            if (definitionJson == null) definitionJson = string.Empty;
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath) ?? string.Empty);

            string tempPath = packagePath + ".tmp";
            if (File.Exists(tempPath)) File.Delete(tempPath);

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    WriteHeader(stream);
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
                    {
                        WriteTextEntry(archive, DefinitionFileName, definitionJson);

                        foreach (var asset in CollectAssets(modelSourcePath))
                        {
                            AddFileEntry(archive, asset.SourcePath, asset.EntryPath);
                        }
                    }
                }

                if (File.Exists(packagePath)) File.Delete(packagePath);
                File.Move(tempPath, packagePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComponentPackage] Failed to write {packagePath}: {ex.Message}");
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        public static string BuildAssetEntryName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            return $"{AssetsFolder}/{fileName}".Replace('\\', '/');
        }

        private struct PackageAsset
        {
            public string SourcePath;
            public string EntryPath;
        }

        private static IEnumerable<PackageAsset> CollectAssets(string modelSourcePath)
        {
            if (string.IsNullOrWhiteSpace(modelSourcePath) || !File.Exists(modelSourcePath))
            {
                yield break;
            }

            var assets = new List<PackageAsset>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddAsset(string sourcePath, string entryPath)
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(entryPath)) return;
                if (!File.Exists(sourcePath)) return;
                entryPath = entryPath.Replace('\\', '/');
                if (!seen.Add(entryPath)) return;
                assets.Add(new PackageAsset
                {
                    SourcePath = sourcePath,
                    EntryPath = entryPath
                });
            }

            string modelDir = Path.GetDirectoryName(modelSourcePath) ?? string.Empty;
            string modelFile = Path.GetFileName(modelSourcePath);
            string modelBase = Path.GetFileNameWithoutExtension(modelSourcePath);
            string modelExt = Path.GetExtension(modelSourcePath).ToLowerInvariant();
            bool modelIsGlb = modelExt == ".glb" || modelExt == ".gltf";

            AddAsset(modelSourcePath, BuildAssetEntryName(modelFile));

            if (!string.IsNullOrWhiteSpace(modelBase))
            {
                foreach (var file in Directory.GetFiles(modelDir, modelBase + ".*", SearchOption.TopDirectoryOnly))
                {
                    if (string.Equals(file, modelSourcePath, StringComparison.OrdinalIgnoreCase)) continue;
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (modelIsGlb && (ext == ".fbx" || ext == ".obj")) continue;
                    if (!IsRelatedAsset(file)) continue;
                    AddAsset(file, BuildAssetEntryName(Path.GetFileName(file)));
                }
            }

            foreach (var file in Directory.GetFiles(modelDir, "*.mat", SearchOption.TopDirectoryOnly))
            {
                AddAsset(file, BuildAssetEntryName(Path.GetFileName(file)));
            }

            string texturesDir = Path.Combine(modelDir, "textures");
            if (!Directory.Exists(texturesDir))
            {
                texturesDir = Path.Combine(modelDir, "Textures");
            }
            if (Directory.Exists(texturesDir))
            {
                foreach (var file in Directory.GetFiles(texturesDir, "*.*", SearchOption.AllDirectories))
                {
                    if (!IsTextureAsset(file) && !IsMaterialAsset(file)) continue;
                    string relative = Path.GetRelativePath(modelDir, file);
                    AddAsset(file, BuildAssetEntryName(relative));
                }
            }

            if (!string.IsNullOrWhiteSpace(modelBase))
            {
                string fbmDir = Path.Combine(modelDir, modelBase + ".fbm");
                if (Directory.Exists(fbmDir))
                {
                    foreach (var file in Directory.GetFiles(fbmDir, "*.*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(modelDir, file);
                        AddAsset(file, BuildAssetEntryName(relative));
                    }
                }
            }

            foreach (var asset in assets)
            {
                yield return asset;
            }
        }

        private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(content ?? string.Empty);
        }

        private static void AddFileEntry(ZipArchive archive, string sourcePath, string entryName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(entryName)) return;
            if (!File.Exists(sourcePath)) return;
            entryName = entryName.Replace('\\', '/');
            var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var fileStream = File.OpenRead(sourcePath);
            fileStream.CopyTo(stream);
        }

        private static string NormalizeEntryPath(string entryName)
        {
            string normalized = entryName.Replace('\\', '/').TrimStart('/');
            if (normalized.Contains("..")) return string.Empty;
            return normalized;
        }

        private static bool TryOpenArchiveRead(FileStream stream, out ZipArchive archive)
        {
            archive = null;
            if (stream == null) return false;

            int zipOffset = 0;
            if (TryReadHeader(stream, out var header))
            {
                zipOffset = header.ZipOffset;
            }

            stream.Position = zipOffset;
            try
            {
                archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ComponentPackage] Failed to open archive: {ex.Message}");
                return false;
            }
        }

        private static bool TryReadHeader(FileStream stream, out PackageHeader header)
        {
            header = default;
            if (stream == null || stream.Length < HeaderSize) return false;

            stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.ASCII, true);
            var magic = reader.ReadBytes(PackageMagic.Length);
            if (magic.Length != PackageMagic.Length || !magic.SequenceEqual(PackageMagic)) return false;

            header.Version = reader.ReadInt32();
            header.HeaderSize = reader.ReadInt32();
            header.ZipOffset = reader.ReadInt32();
            header.Flags = reader.ReadInt32();

            if (header.HeaderSize < HeaderSize) return false;
            if (header.ZipOffset < header.HeaderSize || header.ZipOffset >= stream.Length) return false;
            return true;
        }

        private static void WriteHeader(FileStream stream)
        {
            if (stream == null) return;
            stream.Position = 0;
            using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
            writer.Write(PackageMagic);
            writer.Write(1);
            writer.Write(HeaderSize);
            writer.Write(HeaderSize);
            writer.Write(0);
            stream.Position = HeaderSize;
        }

        private static bool IsRelatedAsset(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return IsTextureAsset(path) || IsMaterialAsset(path) || ext == ".mtl" || ext == ".bin" || ext == ".glb";
        }

        private static bool IsTextureAsset(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".tif" ||
                ext == ".tiff" || ext == ".bmp" || ext == ".gif" || ext == ".dds";
        }

        private static bool IsMaterialAsset(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mat";
        }

        private static string GetCacheRoot()
        {
            string root = Path.Combine(Application.temporaryCachePath, "ComponentPackages");
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            return root;
        }

        private static bool IsWithinPackageDir(string path)
        {
            string dir = Path.GetDirectoryName(path);
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (dir.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase)) return true;
                dir = Path.GetDirectoryName(dir);
            }
            return false;
        }

        private static string ResolveModelSource(string jsonFile, string json)
        {
            try
            {
                var def = JsonUtility.FromJson<ComponentDefinitionStub>(json);
                if (def == null || string.IsNullOrWhiteSpace(def.modelFile)) return string.Empty;
                string path = def.modelFile.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(path) && File.Exists(path)) return path;
                string baseDir = Path.GetDirectoryName(jsonFile) ?? string.Empty;
                string candidate = Path.Combine(baseDir, path);
                return File.Exists(candidate) ? candidate : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string UpdateModelFileInJson(string json, string entryName)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            if (string.IsNullOrWhiteSpace(entryName)) return json;
            try
            {
                var obj = JObject.Parse(json);
                obj["modelFile"] = entryName;
                return obj.ToString();
            }
            catch
            {
                return json;
            }
        }

        private struct PackageHeader
        {
            public int Version;
            public int HeaderSize;
            public int ZipOffset;
            public int Flags;
        }

        [Serializable]
        private class ComponentDefinitionStub
        {
            public string modelFile;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "package";
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(safe) ? "package" : safe;
        }
    }
}
