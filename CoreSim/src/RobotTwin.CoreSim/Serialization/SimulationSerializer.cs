using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.CoreSim.Serialization
{
    /// <summary>
    /// Centralized serialization logic for .rtwin/.rtrobot packages (custom binary container).
    /// </summary>
    public static class SimulationSerializer
    {
        private const uint RtwinMagic = 0x4E575452; // "RTWN" little-endian
        private const uint RtrobotMagic = 0x4F525452; // "RTRO" little-endian
        private const ushort RtwinVersion = 1;
        private const int HeaderSize = 64;
        private const int EntrySize = 32;

        private const string ProjectManifestEntry = "project.json";
        private const string CircuitEntry = "circuit.json";
        private const string RobotEntry = "robot.json";
        private const string WorldEntry = "world.json";
        private const string AssemblyEntry = "assembly.json";
        private const string EnvironmentEntry = "environment.json";
        private const string MetadataEntry = "metadata.json";
        private const string PackageManifestEntry = "package.json";
        private const string CodeRootEntry = "Code";
        private const string FirmwareRootEntry = "firmware";

        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public enum WorkspaceSnapshotMode
        {
            Full = 0,
            CircuitOnly = 1
        }

        private enum EntryType : uint
        {
            Json = 1,
            Text = 2,
            Binary = 3
        }

        private sealed class RtwinEntry
        {
            public string Name { get; }
            public EntryType Type { get; }
            public byte[] Data { get; }

            public RtwinEntry(string name, EntryType type, byte[] data)
            {
                Name = name;
                Type = type;
                Data = data;
            }
        }

        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, _options);
        }

        public static T? Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _options);
        }

        // Persistence
        public static void SaveProject(ProjectManifest manifest, string filePath)
        {
            if (IsRtwinPath(filePath))
            {
                SaveProjectPackage(manifest, filePath);
                return;
            }

            var json = Serialize(manifest);
            File.WriteAllText(filePath, json);
        }

        public static ProjectManifest? LoadProject(string filePath)
        {
            return LoadProject(filePath, true);
        }

        public static ProjectManifest? LoadProject(string filePath, bool extractWorkspace)
        {
            return LoadProject(filePath, extractWorkspace, WorkspaceSnapshotMode.Full);
        }

        public static ProjectManifest? LoadProject(string filePath, bool extractWorkspace, WorkspaceSnapshotMode snapshotMode)
        {
            if (!File.Exists(filePath)) return null;

            if (IsRtwinPath(filePath))
            {
                if (TryLoadBinaryPackage(filePath, extractWorkspace, snapshotMode, out var manifest))
                {
                    return manifest;
                }
            }

            var json = File.ReadAllText(filePath);
            return Deserialize<ProjectManifest>(json);
        }

        public static void SaveRobotPackage(RobotStudioPackage package, string filePath)
        {
            if (package == null || string.IsNullOrWhiteSpace(filePath)) return;
            if (!IsRtrobotPath(filePath))
            {
                var json = Serialize(package);
                File.WriteAllText(filePath, json);
                return;
            }

            string workspaceRoot = ResolveWorkspaceRoot(filePath);
            Directory.CreateDirectory(workspaceRoot);

            WriteRobotWorkspaceSnapshot(package, workspaceRoot);

            var entries = BuildRobotEntries(package, workspaceRoot);
            WriteBinaryPackage(entries, filePath, RtrobotMagic);
        }

        public static RobotStudioPackage? LoadRobotPackage(string filePath, bool extractWorkspace)
        {
            if (!File.Exists(filePath)) return null;

            if (IsRtrobotPath(filePath))
            {
                if (TryLoadRobotBinaryPackage(filePath, extractWorkspace, out var package))
                {
                    return package;
                }
            }

            var json = File.ReadAllText(filePath);
            return Deserialize<RobotStudioPackage>(json);
        }

        private static bool IsRtwinPath(string filePath)
        {
            return string.Equals(Path.GetExtension(filePath), ".rtwin", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRtrobotPath(string filePath)
        {
            return string.Equals(Path.GetExtension(filePath), ".rtrobot", StringComparison.OrdinalIgnoreCase);
        }

        private static void SaveProjectPackage(ProjectManifest manifest, string filePath)
        {
            string? projectDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(projectDir))
            {
                Directory.CreateDirectory(projectDir);
            }

            string workspaceRoot = ResolveWorkspaceRoot(filePath);
            Directory.CreateDirectory(workspaceRoot);

            WriteWorkspaceSnapshot(manifest, workspaceRoot, WorkspaceSnapshotMode.Full);

            var entries = BuildEntries(manifest, workspaceRoot);
            WriteBinaryPackage(entries, filePath, RtwinMagic);
        }

        private static bool TryLoadBinaryPackage(string filePath, bool extractWorkspace, WorkspaceSnapshotMode snapshotMode, out ProjectManifest? manifest)
        {
            manifest = null;
            if (!TryReadBinary(filePath, out var payload, out var magic)) return false;
            if (magic != RtwinMagic) return false;

            if (!payload.TryGetValue(ProjectManifestEntry, out var manifestBytes))
            {
                manifest = RecoverProjectManifest(payload, filePath);
            }
            else
            {
                manifest = Deserialize<ProjectManifest>(Encoding.UTF8.GetString(manifestBytes));
            }

            if (manifest == null) return false;

            if (extractWorkspace)
            {
                string workspaceRoot = ResolveWorkspaceRoot(filePath);
                Directory.CreateDirectory(workspaceRoot);
                WriteWorkspaceSnapshot(manifest, workspaceRoot, snapshotMode);
                ExtractPayloadToWorkspace(payload, workspaceRoot);
            }
            return true;
        }

        private static bool TryLoadRobotBinaryPackage(string filePath, bool extractWorkspace, out RobotStudioPackage? package)
        {
            package = null;
            if (!TryReadBinary(filePath, out var payload, out var magic)) return false;
            if (magic != RtrobotMagic) return false;

            package = ReadRobotPackage(payload);
            if (package == null) return false;

            if (extractWorkspace)
            {
                string workspaceRoot = ResolveWorkspaceRoot(filePath);
                Directory.CreateDirectory(workspaceRoot);
                WriteRobotWorkspaceSnapshot(package, workspaceRoot);
                ExtractPayloadToWorkspace(payload, workspaceRoot);
            }
            return true;
        }

        private static void WriteWorkspaceSnapshot(ProjectManifest manifest, string workspaceRoot, WorkspaceSnapshotMode snapshotMode)
        {
            WriteTextFile(Path.Combine(workspaceRoot, ProjectManifestEntry), Serialize(manifest));
            WriteTextFile(Path.Combine(workspaceRoot, CircuitEntry), Serialize(manifest.Circuit));
            if (snapshotMode == WorkspaceSnapshotMode.Full)
            {
                WriteTextFile(Path.Combine(workspaceRoot, RobotEntry), Serialize(manifest.Robot));
                WriteTextFile(Path.Combine(workspaceRoot, WorldEntry), Serialize(manifest.World));

                if (manifest.Assembly != null)
                {
                    WriteTextFile(Path.Combine(workspaceRoot, AssemblyEntry), Serialize(manifest.Assembly));
                }
                if (manifest.Environment != null)
                {
                    WriteTextFile(Path.Combine(workspaceRoot, EnvironmentEntry), Serialize(manifest.Environment));
                }
            }
            WriteTextFile(Path.Combine(workspaceRoot, MetadataEntry), Serialize(manifest.Metadata ?? new Dictionary<string, object>()));
        }

        private static void WriteRobotWorkspaceSnapshot(RobotStudioPackage package, string workspaceRoot)
        {
            WriteTextFile(Path.Combine(workspaceRoot, RobotEntry), Serialize(package.Robot));
            WriteTextFile(Path.Combine(workspaceRoot, CircuitEntry), Serialize(package.Circuit));
            WriteTextFile(Path.Combine(workspaceRoot, AssemblyEntry), Serialize(package.Assembly));
            WriteTextFile(Path.Combine(workspaceRoot, EnvironmentEntry), Serialize(package.Environment));
            WriteTextFile(Path.Combine(workspaceRoot, MetadataEntry), Serialize(package.Metadata ?? new Dictionary<string, object>()));
            if (package.Project != null)
            {
                WriteTextFile(Path.Combine(workspaceRoot, ProjectManifestEntry), Serialize(package.Project));
            }
        }

        private static void WriteTextFile(string path, string content)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, content);
        }

        private static List<RtwinEntry> BuildEntries(ProjectManifest manifest, string workspaceRoot)
        {
            var entries = new List<RtwinEntry>
            {
                new RtwinEntry(ProjectManifestEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(manifest))),
                new RtwinEntry(CircuitEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(manifest.Circuit))),
                new RtwinEntry(RobotEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(manifest.Robot))),
                new RtwinEntry(WorldEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(manifest.World))),
                new RtwinEntry(MetadataEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(manifest.Metadata ?? new Dictionary<string, object>())))
            };

            if (manifest.Assembly != null)
            {
                entries.Add(new RtwinEntry(AssemblyEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(manifest.Assembly))));
            }
            if (manifest.Environment != null)
            {
                entries.Add(new RtwinEntry(EnvironmentEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(manifest.Environment))));
            }

            string codeRoot = Path.Combine(workspaceRoot, CodeRootEntry);
            if (Directory.Exists(codeRoot))
            {
                foreach (var file in Directory.GetFiles(codeRoot, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(codeRoot, file);
                    string entryName = Path.Combine(CodeRootEntry, relative).Replace('\\', '/');
                    entries.Add(new RtwinEntry(entryName, EntryType.Text, File.ReadAllBytes(file)));
                }
            }

            var package = new PackageManifest
            {
                FormatVersion = RtwinVersion,
                ProjectName = manifest.ProjectName,
                Version = manifest.Version,
                CreatedUtc = DateTime.UtcNow.ToString("O"),
                Entries = entries.Select(e => e.Name).ToList()
            };

            entries.Add(new RtwinEntry(PackageManifestEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(package))));
            return entries;
        }

        private static List<RtwinEntry> BuildRobotEntries(RobotStudioPackage package, string workspaceRoot)
        {
            var entries = new List<RtwinEntry>
            {
                new RtwinEntry(RobotEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(package.Robot))),
                new RtwinEntry(CircuitEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(package.Circuit))),
                new RtwinEntry(AssemblyEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(package.Assembly))),
                new RtwinEntry(EnvironmentEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(package.Environment))),
                new RtwinEntry(MetadataEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(package.Metadata ?? new Dictionary<string, object>())))
            };

            if (package.Project != null)
            {
                entries.Add(new RtwinEntry(ProjectManifestEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(package.Project))));
            }

            AddDirectoryEntries(entries, Path.Combine(workspaceRoot, CodeRootEntry), CodeRootEntry);
            AddDirectoryEntries(entries, Path.Combine(workspaceRoot, FirmwareRootEntry), FirmwareRootEntry);

            var manifest = new PackageManifest
            {
                FormatVersion = RtwinVersion,
                ProjectName = package.Project?.ProjectName ?? package.Robot?.Name ?? "Robot",
                Version = package.Project?.Version ?? "1.0.0",
                CreatedUtc = DateTime.UtcNow.ToString("O"),
                Entries = entries.Select(e => e.Name).ToList()
            };

            entries.Add(new RtwinEntry(PackageManifestEntry, EntryType.Json, Encoding.UTF8.GetBytes(Serialize(manifest))));
            return entries;
        }

        private static void AddDirectoryEntries(List<RtwinEntry> entries, string rootPath, string entryRoot)
        {
            if (!Directory.Exists(rootPath)) return;
            foreach (var file in Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(rootPath, file);
                string entryName = Path.Combine(entryRoot, relative).Replace('\\', '/');
                entries.Add(new RtwinEntry(entryName, EntryType.Text, File.ReadAllBytes(file)));
            }
        }

        private static RobotStudioPackage? ReadRobotPackage(Dictionary<string, byte[]> payload)
        {
            if (!payload.TryGetValue(RobotEntry, out var robotBytes)) return null;
            if (!payload.TryGetValue(CircuitEntry, out var circuitBytes)) return null;
            if (!payload.TryGetValue(AssemblyEntry, out var assemblyBytes)) return null;
            if (!payload.TryGetValue(EnvironmentEntry, out var environmentBytes)) return null;

            var package = new RobotStudioPackage
            {
                Robot = Deserialize<RobotSpec>(Encoding.UTF8.GetString(robotBytes)) ?? new RobotSpec { Name = "Robot" },
                Circuit = Deserialize<CircuitSpec>(Encoding.UTF8.GetString(circuitBytes)) ?? new CircuitSpec(),
                Assembly = Deserialize<AssemblySpec>(Encoding.UTF8.GetString(assemblyBytes)) ?? new AssemblySpec(),
                Environment = Deserialize<EnvironmentSpec>(Encoding.UTF8.GetString(environmentBytes)) ?? new EnvironmentSpec(),
                Metadata = Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(payload.TryGetValue(MetadataEntry, out var metaBytes) ? metaBytes : Encoding.UTF8.GetBytes("{}")))
                    ?? new Dictionary<string, object>()
            };

            if (payload.TryGetValue(ProjectManifestEntry, out var projectBytes))
            {
                package.Project = Deserialize<ProjectManifest>(Encoding.UTF8.GetString(projectBytes));
            }
            return package;
        }

        private static void WriteBinaryPackage(List<RtwinEntry> entries, string filePath, uint magic)
        {
            var nameTable = new MemoryStream();
            var nameOffsets = new List<(uint Offset, uint Length)>();
            foreach (var entry in entries)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(entry.Name);
                nameOffsets.Add(((uint)nameTable.Length, (uint)nameBytes.Length));
                nameTable.Write(nameBytes, 0, nameBytes.Length);
            }

            uint nameTableSize = (uint)nameTable.Length;
            long entryTableOffset = Align(HeaderSize + nameTableSize, 8);
            long dataOffset = Align(entryTableOffset + entries.Count * EntrySize, 8);

            var dataOffsets = new List<long>();
            long cursor = dataOffset;
            foreach (var entry in entries)
            {
                cursor = Align(cursor, 8);
                dataOffsets.Add(cursor);
                cursor += entry.Data.Length;
            }

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(magic);
            writer.Write(RtwinVersion);
            writer.Write((ushort)0);
            writer.Write((uint)HeaderSize);
            writer.Write((uint)entries.Count);
            writer.Write(nameTableSize);
            writer.Write((ulong)HeaderSize);
            writer.Write((ulong)entryTableOffset);
            writer.Write((ulong)dataOffset);
            writer.Write((ulong)0);
            writer.Write((ulong)0);
            writer.Write((uint)0);

            if (stream.Position < HeaderSize)
            {
                int padding = (int)(HeaderSize - stream.Position);
                if (padding > 0)
                {
                    writer.Write(new byte[padding]);
                }
            }

            nameTable.Position = 0;
            nameTable.CopyTo(stream);
            PadTo(stream, entryTableOffset);

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var nameOffset = nameOffsets[i];
                writer.Write(nameOffset.Offset);
                writer.Write(nameOffset.Length);
                writer.Write((uint)entry.Type);
                writer.Write((uint)0);
                writer.Write((ulong)dataOffsets[i]);
                writer.Write((ulong)entry.Data.Length);
            }

            PadTo(stream, dataOffset);

            for (int i = 0; i < entries.Count; i++)
            {
                PadTo(stream, dataOffsets[i]);
                writer.Write(entries[i].Data);
            }

            writer.Flush();
        }

        private static bool TryReadBinary(string filePath, out Dictionary<string, byte[]> payload, out uint magic)
        {
            payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            magic = 0;
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < HeaderSize) return false;

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            magic = reader.ReadUInt32();
            if (magic != RtwinMagic && magic != RtrobotMagic) return false;

            ushort version = reader.ReadUInt16();
            reader.ReadUInt16();
            uint headerSize = reader.ReadUInt32();
            uint entryCount = reader.ReadUInt32();
            uint nameTableSize = reader.ReadUInt32();
            ulong nameTableOffset = reader.ReadUInt64();
            ulong entryTableOffset = reader.ReadUInt64();
            reader.ReadUInt64();
            reader.ReadUInt64();
            reader.ReadUInt64();
            reader.ReadUInt32();

            if (version == 0 || entryCount == 0) return false;
            if (nameTableOffset + nameTableSize > (ulong)stream.Length) return false;
            if (entryTableOffset + entryCount * EntrySize > (ulong)stream.Length) return false;

            stream.Seek((long)nameTableOffset, SeekOrigin.Begin);
            byte[] nameTable = reader.ReadBytes((int)nameTableSize);

            stream.Seek((long)entryTableOffset, SeekOrigin.Begin);
            var entries = new List<(uint NameOffset, uint NameLength, EntryType Type, ulong Offset, ulong Size)>();
            for (int i = 0; i < entryCount; i++)
            {
                uint nameOffset = reader.ReadUInt32();
                uint nameLength = reader.ReadUInt32();
                EntryType type = (EntryType)reader.ReadUInt32();
                reader.ReadUInt32();
                ulong offset = reader.ReadUInt64();
                ulong size = reader.ReadUInt64();
                entries.Add((nameOffset, nameLength, type, offset, size));
            }

            foreach (var entry in entries)
            {
                if (entry.NameOffset + entry.NameLength > (uint)nameTable.Length) continue;
                string name = Encoding.UTF8.GetString(nameTable, (int)entry.NameOffset, (int)entry.NameLength);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (entry.Offset + entry.Size > (ulong)stream.Length) continue;
                if (entry.Size > int.MaxValue) continue;

                stream.Seek((long)entry.Offset, SeekOrigin.Begin);
                byte[] data = reader.ReadBytes((int)entry.Size);
                payload[name] = data;
            }

            return payload.Count > 0;
        }

        private static ProjectManifest RecoverProjectManifest(Dictionary<string, byte[]> payload, string filePath)
        {
            var circuit = LoadPart<CircuitSpec>(payload, CircuitEntry) ?? new CircuitSpec();
            var robot = LoadPart<RobotSpec>(payload, RobotEntry) ?? new RobotSpec { Name = "DefaultRobot" };
            var world = LoadPart<WorldSpec>(payload, WorldEntry) ?? new WorldSpec { Name = "DefaultWorld", Width = 0, Depth = 0 };
            var assembly = LoadPart<AssemblySpec>(payload, AssemblyEntry);
            var environment = LoadPart<EnvironmentSpec>(payload, EnvironmentEntry);
            var metadata = LoadPart<Dictionary<string, object>>(payload, MetadataEntry) ?? new Dictionary<string, object>();

            string name = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(name)) name = "RecoveredProject";

            return new ProjectManifest
            {
                ProjectName = name,
                Description = "Recovered project",
                Version = "1.0.0",
                Circuit = circuit,
                Robot = robot,
                World = world,
                Assembly = assembly,
                Environment = environment,
                Metadata = metadata
            };
        }

        private static T? LoadPart<T>(Dictionary<string, byte[]> payload, string entryName)
        {
            if (!payload.TryGetValue(entryName, out var data)) return default;
            string json = Encoding.UTF8.GetString(data);
            return Deserialize<T>(json);
        }

        private static void ExtractPayloadToWorkspace(Dictionary<string, byte[]> payload, string workspaceRoot)
        {
            foreach (var kvp in payload)
            {
                string name = kvp.Key.Replace('\\', '/');
                if (name.StartsWith(CodeRootEntry + "/", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(FirmwareRootEntry + "/", StringComparison.OrdinalIgnoreCase))
                {
                    string path = Path.Combine(workspaceRoot, name);
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllBytes(path, kvp.Value);
                }
            }
        }

        private static string ResolveWorkspaceRoot(string filePath)
        {
            string? projectDir = Path.GetDirectoryName(filePath);
            string baseName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(projectDir) || string.IsNullOrWhiteSpace(baseName))
            {
                return projectDir ?? string.Empty;
            }
            return Path.Combine(projectDir, baseName);
        }

        private static long Align(long value, int alignment)
        {
            long mask = alignment - 1;
            return (value + mask) & ~mask;
        }

        private static void PadTo(Stream stream, long target)
        {
            if (stream.Position >= target) return;
            long delta = target - stream.Position;
            if (delta <= 0) return;
            if (delta > int.MaxValue) throw new InvalidOperationException("Padding too large.");
            byte[] padding = new byte[(int)delta];
            stream.Write(padding, 0, padding.Length);
        }

        private class PackageManifest
        {
            public int FormatVersion { get; set; }
            public string ProjectName { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public string CreatedUtc { get; set; } = string.Empty;
            public List<string> Entries { get; set; } = new List<string>();
        }
    }
}
