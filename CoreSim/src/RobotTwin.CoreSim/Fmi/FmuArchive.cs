using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace RobotTwin.CoreSim.Fmi
{
    /// <summary>
    /// [fmi] An FMU file (a zip: modelDescription.xml, binaries/&lt;platform&gt;/, resources/) unpacked into a folder of its
    /// own, named by the file's content hash so that the same FMU is unpacked once. Entries that would land outside the
    /// folder are refused.
    /// </summary>
    public sealed class FmuArchive
    {
        private FmuArchive(string folder, FmuDescription description)
        {
            Folder = folder;
            Description = description;
        }

        /// <summary>Where the FMU is unpacked.</summary>
        public string Folder { get; }

        public FmuDescription Description { get; }

        /// <summary>The co-simulation binary for 64-bit Windows, or null when the FMU has none.</summary>
        public string? BinaryPath
        {
            get
            {
                if (Description.CoSimulationId.Length == 0) return null;
                string path = Path.Combine(Folder, "binaries", "win64", Description.CoSimulationId + ".dll");
                return File.Exists(path) ? path : null;
            }
        }

        /// <summary>The folder FMUs are unpacked under (the system's temporary folder by default).</summary>
        public static string CacheRoot { get; set; } = Path.Combine(Path.GetTempPath(), "robotwin-fmu");

        public static FmuArchive Open(string fmuPath) => Open(File.ReadAllBytes(fmuPath));

        /// <summary>Unpacks an FMU given as its bytes (as a project keeps it) and reads its description.</summary>
        public static FmuArchive Open(byte[] fmu)
        {
            string hash;
            using (var sha = SHA256.Create()) hash = string.Concat(sha.ComputeHash(fmu).Take(10).Select(b => b.ToString("x2")));
            string folder = Path.Combine(CacheRoot, hash);
            string marker = Path.Combine(folder, ".unpacked");
            if (!File.Exists(marker))
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
                using (var zip = new ZipArchive(new MemoryStream(fmu), ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(folder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"FMU'da klasörünün dışına çıkan bir dosya var: '{entry.FullName}'.");
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, true);
                    }
                }

                File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            }

            string xml = Path.Combine(folder, "modelDescription.xml");
            if (!File.Exists(xml)) throw new InvalidDataException("FMU'da modelDescription.xml yok.");
            return new FmuArchive(folder, FmuDescription.Parse(File.ReadAllText(xml)));
        }
    }
}
