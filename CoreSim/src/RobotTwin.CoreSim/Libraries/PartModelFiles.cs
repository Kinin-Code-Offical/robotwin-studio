using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] 3D model files for parts: STL/OBJ/PLY (MeshModel), VRML (VrmlReader), STEP/IGES/BREP through the Open
    // CASCADE bridge (OcctBridge; a clear message when it is missing), procedural boxes, and resolution of KiCad
    // (model "${KICAD8_3DMODEL_DIR}/…") references.

    public static class PartModelFiles
    {
        public static readonly string[] MeshExtensions = { ".stl", ".obj", ".ply", ".wrl", ".wrz", ".vrml" };
        public static readonly string[] CadExtensions = { ".step", ".stp", ".iges", ".igs", ".brep" };

        /// <summary>Why a STEP/IGES file cannot be read here: the Open CASCADE bridge (the app's occt folder) is missing.</summary>
        public const string StepMessage =
            "STEP/IGES dosyaları katı (B-rep) CAD modelidir; RobotWin onları Open CASCADE köprüsüyle okur, ancak köprü bu kurulumda yok (uygulama klasöründeki 'occt'). " +
            "Ayarlar → Güncellemeler → Bileşenleri değiştir ile 'STEP / IGES desteği'ni ekleyin ya da modeli KiCad, FreeCAD veya üreticinin aracıyla STL / OBJ / WRL olarak dışa aktarıp ekleyin.";

        /// <summary>STEP/IGES/BREP files can be read on this installation (the Open CASCADE bridge loads).</summary>
        public static bool CanReadCad => Formats.OcctBridge.Available;

        public static bool IsCad(string path) => CadExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        public static bool IsMesh(string path) => MeshExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        /// <summary>
        /// Loads any supported model file (metres, Z up): meshes, VRML, and STEP/IGES/BREP through the Open CASCADE bridge
        /// (every body at its assembly placement, in its colours). Without the bridge a CAD file throws
        /// <see cref="NotSupportedException"/> with the reason.
        /// </summary>
        public static MeshModel LoadMesh(string path, VrmlUnits vrmlUnits = VrmlUnits.Auto, ImportLog? log = null)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (IsCad(path))
            {
                if (!CanReadCad) throw new NotSupportedException(StepMessage);
                return Formats.OcctBridge.Read(path);
            }

            if (ext == ".wrl" || ext == ".wrz" || ext == ".vrml") return VrmlReader.Load(path, vrmlUnits, log);
            if (ext == ".stl" || ext == ".obj" || ext == ".ply") return MeshModel.Load(path);
            throw new NotSupportedException($"'{ext}' 3B model biçimi desteklenmiyor (STL, OBJ, PLY, WRL, STEP, IGES kullanın).");
        }

        /// <summary>
        /// Wraps a mesh as a part model. <paramref name="recenter"/>: rest on the board and centre
        /// in x/y (user meshes); false keeps the file's origin (KiCad models are aligned to the footprint origin).
        /// </summary>
        public static PartModel3D ToPartModel(MeshModel mesh, string name, bool recenter, string? color = null)
        {
            if (mesh.TriangleCount == 0) throw new InvalidDataException("Modelde üçgen yok.");
            if (mesh.TriangleCount > 2_000_000) throw new InvalidDataException($"Model çok büyük ({mesh.TriangleCount:N0} üçgen; en çok 2 milyon).");
            if (recenter)
            {
                var b = mesh.Bounds;
                mesh = mesh.Translate(new Vec3(-(b.Min.X + b.Max.X) / 2, -(b.Min.Y + b.Max.Y) / 2, -b.Min.Z));
            }

            return new PartModel3D { Mesh = mesh.ToEmbedded(), Name = name, Color = color };
        }

        /// <summary>A plain box (body of a module, relay, sensor …), sizes in mm, resting on the board.</summary>
        public static PartModel3D Box(double widthMm, double depthMm, double heightMm, string color = "#2E3440", string name = "Kutu")
        {
            double w = Math.Max(0.1, widthMm), d = Math.Max(0.1, depthMm), h = Math.Max(0.05, heightMm);
            var mesh = MeshModel.CreateBox(new Vec3(w / 1000, d / 1000, h / 1000)).Translate(new Vec3(0, 0, h / 2000));
            mesh.TriangleColors = Enumerable.Repeat(ParseColor(color), mesh.TriangleCount).ToList();
            return new PartModel3D { Mesh = mesh.ToEmbedded(), Name = $"{name} {w:0.##}×{d:0.##}×{h:0.##} mm", Color = color };
        }

        private static MeshColor ParseColor(string hex)
        {
            var m = Regex.Match(hex ?? string.Empty, "^#?([0-9a-fA-F]{2})([0-9a-fA-F]{2})([0-9a-fA-F]{2})$");
            if (!m.Success) return new MeshColor(0x2E, 0x34, 0x40);
            byte H(int g) => Convert.ToByte(m.Groups[g].Value, 16);
            return new MeshColor(H(1), H(2), H(3));
        }

        /// <summary>Applies KiCad's model offset/rotation/scale (mm, degrees) to a part model.</summary>
        public static void ApplyKiCadPlacement(PartModel3D model, KiCadModelRef r)
        {
            model.OffsetX = r.OffsetX;
            model.OffsetY = r.OffsetY;
            model.OffsetZ = r.OffsetZ;
            model.RotationX = r.RotateX;
            model.RotationY = r.RotateY;
            model.RotationZ = r.RotateZ;
            double s = (r.ScaleX + r.ScaleY + r.ScaleZ) / 3.0;
            model.Scale = s > 0 ? s : 1.0;
        }

        private static readonly string[] KiCadVariables =
        {
            "KICAD9_3DMODEL_DIR", "KICAD8_3DMODEL_DIR", "KICAD7_3DMODEL_DIR", "KICAD6_3DMODEL_DIR", "KISYS3DMOD", "KICAD_3DMODEL_DIR"
        };

        /// <summary>Folders where KiCad installs its 3D models on this machine (env variables and default install paths).</summary>
        public static IEnumerable<string> KiCadModelFolders()
        {
            foreach (var v in KiCadVariables)
            {
                string? dir = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) yield return dir!;
            }

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var ver in new[] { "9.0", "8.0", "7.0", "6.0" })
            {
                string dir = Path.Combine(pf, "KiCad", ver, "share", "kicad", "3dmodels");
                if (Directory.Exists(dir)) yield return dir;
            }
        }

        /// <summary>
        /// Finds the file a KiCad model reference points at: expands ${VARS}, then looks in the
        /// extra folders (the footprint's library folder, user folders) by "Lib.3dshapes/name" and by
        /// name. A STEP reference prefers a .wrl with the same name (KiCad ships both).
        /// </summary>
        public static string? ResolveKiCadModel(string reference, IEnumerable<string> extraFolders, out string note)
        {
            note = string.Empty;
            if (string.IsNullOrWhiteSpace(reference))
            {
                note = "model yolu boş.";
                return null;
            }

            string raw = reference.Trim().Trim('"').Replace('\\', '/');
            string expanded = Regex.Replace(raw, @"\$\{([^}]+)\}|\$\(([^)]+)\)", m =>
            {
                string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                string? v = Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrEmpty(v) && name.EndsWith("3DMODEL_DIR", StringComparison.OrdinalIgnoreCase)) v = KiCadModelFolders().FirstOrDefault();
                return string.IsNullOrEmpty(v) ? "\u0001" + name + "\u0001" : v!.Replace('\\', '/');
            });

            // Tail like "Package_DIP.3dshapes/DIP-8_W7.62mm.wrl".
            var segments = raw.Split('/');
            string fileName = segments[segments.Length - 1];
            string tail = segments.Length >= 2 ? segments[segments.Length - 2] + "/" + fileName : fileName;
            var candidates = new List<string>();
            if (!expanded.Contains('\u0001')) candidates.Add(expanded);
            foreach (var dir in extraFolders.Concat(KiCadModelFolders()).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(dir, tail));
                candidates.Add(Path.Combine(dir, fileName));
            }

            foreach (var c in candidates)
            {
                string path;
                try
                {
                    path = Path.GetFullPath(c.Replace('/', Path.DirectorySeparatorChar));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    continue;
                }

                if (IsCad(path))
                {
                    foreach (var ext in new[] { ".wrl", ".WRL", ".stl", ".obj" })
                    {
                        string alt = Path.ChangeExtension(path, ext);
                        if (File.Exists(alt))
                        {
                            note = $"STEP yerine aynı adlı '{Path.GetFileName(alt)}' kullanıldı.";
                            return alt;
                        }
                    }

                    if (File.Exists(path))
                    {
                        note = CanReadCad ? "STEP modeli Open CASCADE ile okundu." : "yalnız STEP dosyası var: " + StepMessage;
                        return path;
                    }
                }
                else if (File.Exists(path))
                {
                    return path;
                }
                else if (Path.GetExtension(path).Equals(".wrl", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var ext in new[] { ".step", ".stp" })
                    {
                        if (File.Exists(Path.ChangeExtension(path, ext)))
                        {
                            note = CanReadCad
                                ? $".wrl yok; '{Path.GetFileName(Path.ChangeExtension(path, ext))}' Open CASCADE ile okundu."
                                : $".wrl yok, yalnız '{Path.GetFileName(Path.ChangeExtension(path, ext))}' var: " + StepMessage;
                            return Path.ChangeExtension(path, ext);
                        }
                    }
                }
            }

            note = $"'{fileName}' bulunamadı; KiCad 3B model klasörünü (…/share/kicad/3dmodels) seçin ya da kendi STL/OBJ dosyanızı ekleyin.";
            return null;
        }
    }
}
