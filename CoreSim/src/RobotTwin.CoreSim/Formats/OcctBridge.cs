using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Formats
{
    /// <summary>
    /// [occt] CAD exchange files through Open CASCADE Technology: STEP / IGES / BREP in (meshed, with assembly placements
    /// and colours), STEP out (a RobotWin mesh sewn into a solid). OCCT is only the file-format bridge; modelling, physics
    /// and drawing stay in RobotWin's own engines. The bridge lives in the app's <c>occt</c> folder (RobotWin.Occt.dll with
    /// the OCCT toolkits it needs); without it, <see cref="Available"/> is false and callers say so.
    /// </summary>
    public static class OcctBridge
    {
        public const string FolderName = "occt";
        public const string DllName = "RobotWin.Occt.dll";

        private static readonly List<string> Extra = new List<string>();

        /// <summary>
        /// Another folder to look for the bridge in (tests, a portable copy); the app's own <c>occt</c> folder comes first.
        /// While the bridge has not loaded, the next use looks again.
        /// </summary>
        public static void AddSearchFolder(string folder)
        {
            lock (Gate)
            {
                if (Extra.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
                Extra.Add(folder);
                if (_read == null) _tried = false;
            }
        }

        private static readonly object Gate = new object();
        private static bool _tried;
        private static string? _loadError;
        private static VersionFn? _version;
        private static ReadFn? _read;
        private static MeshFn? _mesh;
        private static FreeFn? _free;
        private static WriteFn? _write;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int VersionFn(byte[] buffer, int capacity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate IntPtr ReadFn(string path, double linear, double angular, out int vertexCount, out int triangleCount, out int colored, byte[] error, int errorCapacity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MeshFn(IntPtr handle, [Out] double[] xyz, [Out] int[] tri, [Out] byte[] rgb);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FreeFn(IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate int WriteFn(string path, double[] xyz, int vertexCount, int[] tri, int triangleCount, byte[] error, int errorCapacity);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        private const uint LoadLibrarySearchDllLoadDir = 0x100, LoadLibrarySearchDefaultDirs = 0x1000;

        /// <summary>The bridge is installed and loads (STEP / IGES / BREP can be read, STEP written).</summary>
        public static bool Available
        {
            get
            {
                Load();
                return _read != null;
            }
        }

        /// <summary>Why the bridge is not available (null when it is).</summary>
        public static string? LoadError
        {
            get
            {
                Load();
                return _loadError;
            }
        }

        /// <summary>Open CASCADE's version ("8.0.2"), or null without the bridge.</summary>
        public static string? Version
        {
            get
            {
                Load();
                if (_version == null) return null;
                var buffer = new byte[64];
                _version(buffer, buffer.Length);
                return Text(buffer);
            }
        }

        private static IEnumerable<string> Folders()
        {
            yield return Path.Combine(AppContext.BaseDirectory, FolderName);
            string? env = Environment.GetEnvironmentVariable("ROBOTWIN_OCCT_DIR");
            if (!string.IsNullOrWhiteSpace(env)) yield return env!;
            foreach (var f in Extra.ToArray()) yield return f;
        }

        private static void Load()
        {
            lock (Gate)
            {
                if (_tried) return;
                _tried = true;
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    _loadError = "the Open CASCADE bridge is built for Windows";
                    return;
                }

                foreach (var folder in Folders())
                {
                    string dll = Path.Combine(folder, DllName);
                    if (!File.Exists(dll)) continue;
                    // Its OCCT toolkits sit beside it: they load from the same folder.
                    IntPtr module = LoadLibraryExW(Path.GetFullPath(dll), IntPtr.Zero, LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
                    if (module == IntPtr.Zero)
                    {
                        _loadError = $"{dll} did not load (Windows error {Marshal.GetLastWin32Error()})";
                        continue;
                    }

                    T? Fn<T>(string name) where T : class
                    {
                        IntPtr p = GetProcAddress(module, name);
                        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
                    }

                    _version = Fn<VersionFn>("rw_occt_version");
                    _read = Fn<ReadFn>("rw_occt_read");
                    _mesh = Fn<MeshFn>("rw_occt_mesh");
                    _free = Fn<FreeFn>("rw_occt_free");
                    _write = Fn<WriteFn>("rw_occt_write_step");
                    if (_read != null && _mesh != null && _free != null && _write != null && _version != null)
                    {
                        _loadError = null;
                        return;
                    }

                    _read = null;
                    _loadError = $"{dll} lacks the bridge's functions";
                }

                _loadError ??= $"{DllName} is not in the app's '{FolderName}' folder";
            }
        }

        private static string Text(byte[] b)
        {
            int n = Array.IndexOf(b, (byte)0);
            return Encoding.UTF8.GetString(b, 0, n < 0 ? b.Length : n);
        }

        /// <summary>
        /// Reads a STEP / IGES / BREP file as a triangle mesh in metres (Z up, as CAD files are): every body of every
        /// assembly at its placement, faces in their colours. <paramref name="deflectionMm"/>: the largest gap between the
        /// mesh and the true surface (≤ 0: a fine fraction of each body's size).
        /// </summary>
        public static MeshModel Read(string path, double deflectionMm = 0, double angularDegrees = 20)
        {
            if (!Available) throw new NotSupportedException(LoadError ?? "Open CASCADE bridge missing");
            if (!File.Exists(path)) throw new FileNotFoundException("File not found.", path);
            var error = new byte[512];
            // Open CASCADE's STEP / IGES translators keep global state: one call at a time (a parallel import and export
            // made a written box read back wrong).
            lock (NativeGate)
            {
                IntPtr handle = _read!(Path.GetFullPath(path), deflectionMm, angularDegrees * Math.PI / 180, out int vertexCount, out int triangleCount, out int colored, error, error.Length);
                if (handle == IntPtr.Zero) throw new InvalidDataException(Text(error));
                try
                {
                    var xyz = new double[vertexCount * 3];
                    var tri = new int[triangleCount * 3];
                    var rgb = new byte[triangleCount * 3];
                    _mesh!(handle, xyz, tri, rgb);
                    var mesh = new MeshModel { Name = Path.GetFileNameWithoutExtension(path) };
                    for (int i = 0; i < vertexCount; i++) mesh.Vertices.Add(new Vec3(xyz[3 * i] * 0.001, xyz[3 * i + 1] * 0.001, xyz[3 * i + 2] * 0.001));
                    mesh.Indices.AddRange(tri);
                    if (colored != 0)
                    {
                        mesh.TriangleColors = new List<MeshColor>(triangleCount);
                        for (int t = 0; t < triangleCount; t++) mesh.TriangleColors.Add(new MeshColor(rgb[3 * t], rgb[3 * t + 1], rgb[3 * t + 2]));
                    }

                    return mesh;
                }
                finally
                {
                    _free!(handle);
                }
            }
        }

        /// <summary>Serializes the calls into the native translators (their state is global).</summary>
        private static readonly object NativeGate = new object();

        /// <summary>Writes a mesh (metres) as a STEP AP214 file in millimetres: its triangles sewn into a shell, a solid when it closes.</summary>
        public static void WriteStep(MeshModel mesh, string path)
        {
            if (!Available) throw new NotSupportedException(LoadError ?? "Open CASCADE bridge missing");
            if (mesh.TriangleCount == 0) throw new InvalidDataException("The model has no triangles.");
            var xyz = new double[mesh.Vertices.Count * 3];
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                xyz[3 * i] = mesh.Vertices[i].X * 1000;
                xyz[3 * i + 1] = mesh.Vertices[i].Y * 1000;
                xyz[3 * i + 2] = mesh.Vertices[i].Z * 1000;
            }

            var error = new byte[512];
            int failed;
            lock (NativeGate) failed = _write!(Path.GetFullPath(path), xyz, mesh.Vertices.Count, mesh.Indices.ToArray(), mesh.TriangleCount, error, error.Length);
            if (failed != 0) throw new IOException(Text(error));
        }
    }
}
