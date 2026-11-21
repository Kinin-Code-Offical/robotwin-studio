using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Analysis.Recording;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Analysis
{
    /// <summary>Serial text a board printed, with the simulated time it appeared.</summary>
    public sealed class SerialLine
    {
        public double Time { get; set; }
        public string Board { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public sealed class EventLine
    {
        public double Time { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>A rigid body of the run, for 3D playback (pose channels "body/{Id}/…").</summary>
    public sealed class BodyInfo
    {
        public string Id { get; set; } = string.Empty;
        public string ObjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>"box", "sphere", "cylinder" (body z), "mesh".</summary>
        public string Shape { get; set; } = "box";

        /// <summary>Box half extents, or (radius, radius, half height) for a cylinder, radius for a sphere.</summary>
        public Vec3 Size { get; set; }

        public bool IsStatic { get; set; }
        public Vec3 StaticPosition { get; set; }
        public Quat StaticOrientation { get; set; } = Quat.Identity;
        public uint Color { get; set; } = 0xFFB0B4BA;
        public double Mass { get; set; }

        /// <summary>Mesh in the body frame (flattened xyz) for mesh bodies; null = draw the size box.</summary>
        public float[]? MeshVertices { get; set; }

        public int[]? MeshIndices { get; set; }
    }

    /// <summary>All loads on one part at one sample (body frame).</summary>
    public sealed class LoadSample
    {
        public double Time { get; set; }
        public Vec3 Gravity { get; set; }
        public Vec3 Omega { get; set; }
        public Vec3 Acceleration { get; set; }
        public List<(Vec3 Point, Vec3 Force, byte Kind)> Loads { get; } = new List<(Vec3, Vec3, byte)>();

        public const byte KindContact = 0, KindJoint = 1, KindJointTorque = 2;

        public double Magnitude
        {
            get
            {
                double s = 0;
                foreach (var l in Loads) s += l.Force.Length;
                return s;
            }
        }
    }

    /// <summary>One solved instant of a part's stress analysis.</summary>
    public sealed class StressFrame
    {
        public double Time { get; set; }
        public float[] VonMises { get; set; } = Array.Empty<float>();

        /// <summary>3 per node (m).</summary>
        public float[] Displacement { get; set; } = Array.Empty<float>();

        public double MaxVonMises { get; set; }

        /// <summary>[thermal] Part temperature at this instant (°C); its strength and thermal strain follow it.</summary>
        public double Temperature { get; set; } = double.NaN;
        public double MaxDisplacement { get; set; }
        public double MinSafetyFactor { get; set; }
        public double ReactionRatio { get; set; }
        public double TotalLoad { get; set; }
        public int Iterations { get; set; }
        public int LoadCount { get; set; }
    }

    /// <summary>Structural result of one part: its mesh (body frame) and the solved instants.</summary>
    public sealed class StressResult
    {
        public string ObjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Method { get; set; } = "tet";
        public string Material { get; set; } = string.Empty;
        public double Yield { get; set; }
        public double ElementSize { get; set; }
        public int Order { get; set; } = 2;

        /// <summary>Node coordinates (body frame, xyz).</summary>
        public float[] Nodes { get; set; } = Array.Empty<float>();

        /// <summary>Elements (10 or 4 nodes each, flattened) — tetrahedra; for beams the node chain.</summary>
        public int[] Elements { get; set; } = Array.Empty<int>();

        /// <summary>Boundary triangles (6 node indices each: a b c ab bc ca; −1 for linear).</summary>
        public int[] Surface { get; set; } = Array.Empty<int>();

        /// <summary>Beam: section width/height or diameter (m) and circular flag.</summary>
        public double BeamWidth { get; set; }
        public double BeamHeight { get; set; }
        public bool BeamCircular { get; set; }

        /// <summary>Critical instant as the exact discrete problem (for export to another solver): nodal forces incl. body loads, fixed DOFs, material.</summary>
        public double[]? ExportForces { get; set; }
        public int[]? ExportFixed { get; set; }
        public double ExportYoung { get; set; }
        public double ExportPoisson { get; set; }
        public double ExportTime { get; set; }

        /// <summary>Mesh convergence of the representative peak (98th percentile σ) at the critical instant: relative change on a finer mesh, NaN if not checked.</summary>
        public double MeshChange { get; set; } = double.NaN;

        /// <summary>Beam: direction of the section height (thinnest side) in the body frame.</summary>
        public Vec3 BeamUp { get; set; } = Vec3.UnitZ;

        public List<StressFrame> Frames { get; } = new List<StressFrame>();

        /// <summary>Body centre relative to the object origin (object frame): the mesh nodes are in the body frame.</summary>
        public Vec3 BodyOffset { get; set; }

        /// <summary>Rotation of the body relative to the object (object frame): a wheel's body is its model turned so the
        /// cylinder axis runs along the axle. Mesh and grid coordinates are in the body frame.</summary>
        public Quat BodyTurn { get; set; } = Quat.Identity;
        public List<string> Notes { get; } = new List<string>();
        public int NodeCount => Nodes.Length / 3;
        public int NodesPerElement => Method == "beam" ? 2 : Order == 2 ? 10 : 4;

        public StressFrame? Critical => Frames.Count == 0 ? null : Frames.OrderByDescending(f => f.MaxVonMises).First();
        public Vec3 Node(int i) => new Vec3(Nodes[3 * i], Nodes[3 * i + 1], Nodes[3 * i + 2]);
    }

    /// <summary>Spatial temperature result of one board or part.</summary>
    public sealed class ThermalResult
    {
        public string ObjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = "board";

        /// <summary>Centre of the heated body relative to the object origin (object frame): where the grid sits.</summary>
        public Vec3 BodyOffset { get; set; }

        /// <summary>Rotation of the body relative to the object (object frame): a wheel's body is its model turned so the
        /// cylinder axis runs along the axle. Mesh and grid coordinates are in the body frame.</summary>
        public Quat BodyTurn { get; set; } = Quat.Identity;
        public int Nx { get; set; }
        public int Ny { get; set; }
        public int Nz { get; set; }
        public double Dx { get; set; }
        public double Dy { get; set; }
        public double Dz { get; set; }
        public Vec3 Origin { get; set; }
        public List<double> Times { get; } = new List<double>();
        public List<float[]> Frames { get; } = new List<float[]>();

        /// <summary>Peak / mean temperature at every thermal step (for the time plot).</summary>
        public List<(double Time, double Max, double Mean, double Power)> History { get; } = new List<(double, double, double, double)>();

        /// <summary>Named heat sources (label, x, y in grid metres) for the map overlay.</summary>
        public List<(string Name, double X, double Y)> Sources { get; } = new List<(string, double, double)>();

        public List<string> Notes { get; } = new List<string>();
        public string Material { get; set; } = string.Empty;
        public double PeakTemperature => History.Count == 0 ? double.NaN : History.Max(h => h.Max);
    }

    /// <summary>Offline flow result for one body.</summary>
    public sealed class CfdResult
    {
        public string ObjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Is3D { get; set; }
        public string Axis { get; set; } = "+X";
        public int Nx { get; set; }
        public int Ny { get; set; }

        /// <summary>3D: number of cells along the third axis (fields below are the mid slice).</summary>
        public int Nz { get; set; } = 1;

        /// <summary>Cell size (m).</summary>
        public double Dx { get; set; }

        public double Speed { get; set; }
        public double ReferenceLength { get; set; }

        /// <summary>Reference (frontal) area, m² (3D) or m²/m (2D, per unit span).</summary>
        public double ReferenceArea { get; set; }

        public double ReynoldsPhysical { get; set; }
        public double ReynoldsLattice { get; set; }
        public double Tau { get; set; }
        public double LatticeSpeed { get; set; }
        public double AirDensity { get; set; } = 1.2;
        public double TimeStep { get; set; }
        public List<double> Times { get; } = new List<double>();
        public List<double> Cd { get; } = new List<double>();
        public List<double> Cl { get; } = new List<double>();
        public List<double> Cs { get; } = new List<double>();

        /// <summary>Slice fields (nx·ny, lattice → physical: velocity m/s, pressure Pa gauge).</summary>
        public float[] Ux { get; set; } = Array.Empty<float>();

        public float[] Uy { get; set; } = Array.Empty<float>();
        public float[] Pressure { get; set; } = Array.Empty<float>();
        public float[] Vorticity { get; set; } = Array.Empty<float>();
        public bool[] Solid { get; set; } = Array.Empty<bool>();

        /// <summary>3D only: full velocity magnitude (m/s) for VTK export, possibly coarsened.</summary>
        public float[]? Speed3D { get; set; }

        public int Coarse3D { get; set; } = 1;
        public List<float[]> Streamlines { get; } = new List<float[]>();
        public double MeanCd { get; set; }
        public double MeanCl { get; set; }
        public double Strouhal { get; set; }

        /// <summary>Mean drag force (N) at the physical speed.</summary>
        public double DragForce => Is3D ? Force.Length : 0.5 * AirDensity * Speed * Speed * ReferenceArea * MeanCd;

        /// <summary>Recording time of this flow frame (s).</summary>
        public double Time { get; set; }

        /// <summary>3D: world position of grid cell (0,0,0)'s corner and the grid axes (x = along the relative wind).</summary>
        public Vec3 Origin { get; set; }
        public Vec3 AxisX { get; set; } = Vec3.UnitX;
        public Vec3 AxisY { get; set; } = Vec3.UnitY;
        public Vec3 AxisZ { get; set; } = Vec3.UnitZ;

        /// <summary>3D: aerodynamic force on the assembly (N, world frame).</summary>
        public Vec3 Force { get; set; }

        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// [analysis] Everything an offline run produced. Saved as a .rwanalysis file (zip: JSON metadata,
    /// binary channel data, meshes and fields), normally next to the world file.
    /// </summary>
    public sealed class AnalysisResult
    {
        public const string Extension = ".rwanalysis";
        public const string Format = "robotwin.analysis";

        public AnalysisSettings Settings { get; set; } = new AnalysisSettings();
        public string WorldTitle { get; set; } = string.Empty;
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public double WallSeconds { get; set; }
        public double SimulatedSeconds { get; set; }
        public bool Cancelled { get; set; }
        public string HaltReason { get; set; } = string.Empty;
        public ChannelStore Channels { get; set; } = new ChannelStore();
        public List<SerialLine> Serial { get; } = new List<SerialLine>();
        public List<EventLine> Events { get; } = new List<EventLine>();
        public List<BodyInfo> Bodies { get; } = new List<BodyInfo>();
        public Dictionary<string, List<LoadSample>> Loads { get; } = new Dictionary<string, List<LoadSample>>(StringComparer.OrdinalIgnoreCase);
        public List<StressResult> Stress { get; } = new List<StressResult>();
        public List<ThermalResult> Thermal { get; } = new List<ThermalResult>();
        public CfdResult? Aero { get; set; }

        /// <summary>Min–max envelope of each channel over the tolerance runs (channel name → per-sample bounds).</summary>
        public Dictionary<string, (double[] Lo, double[] Hi)> Bands { get; } = new Dictionary<string, (double[] Lo, double[] Hi)>(StringComparer.Ordinal);

        /// <summary>Number of tolerance runs behind <see cref="Bands"/>.</summary>
        public int BandRuns { get; set; }

        /// <summary>Flow around the whole assembly at keyframes of the recording (render mode).</summary>
        public List<CfdResult> AeroFrames { get; } = new List<CfdResult>();
        public List<string> Log { get; } = new List<string>();

        /// <summary>Device id → part designator (for thermal source placement).</summary>
        public Dictionary<string, string> DeviceParts { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Note(string text) => Log.Add(text);

        // ================================================================ save / load

        public void Save(string path)
        {
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var manifest = new JsonObject
                {
                    ["format"] = Format,
                    ["version"] = 1,
                    ["world"] = WorldTitle,
                    ["created"] = Created.ToString("o", CultureInfo.InvariantCulture),
                    ["wall_seconds"] = WallSeconds,
                    ["simulated_seconds"] = SimulatedSeconds,
                    ["cancelled"] = Cancelled,
                    ["halt_reason"] = HaltReason,
                    ["settings"] = Settings.ToJson(),
                    ["log"] = new JsonArray(Log.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray())
                };
                Text(zip, "manifest.json", manifest.ToJsonString());
                using (var s = zip.CreateEntry("channels.bin", CompressionLevel.Optimal).Open()) Channels.Write(s);
                var serial = new JsonArray(Serial.Select(x => (JsonNode)new JsonObject { ["t"] = x.Time, ["b"] = x.Board, ["s"] = x.Text }).ToArray());
                Text(zip, "serial.json", serial.ToJsonString());
                var events = new JsonArray(Events.Select(x => (JsonNode)new JsonObject { ["t"] = x.Time, ["src"] = x.Source, ["k"] = x.Kind, ["m"] = x.Message }).ToArray());
                Text(zip, "events.json", events.ToJsonString());
                var parts = new JsonObject();
                foreach (var kv in DeviceParts) parts[kv.Key] = kv.Value;
                Text(zip, "device_parts.json", parts.ToJsonString());
                using (var s = zip.CreateEntry("bodies.bin", CompressionLevel.Optimal).Open()) WriteBodies(s);
                using (var s = zip.CreateEntry("loads.bin", CompressionLevel.Optimal).Open()) WriteLoads(s);
                for (int i = 0; i < Stress.Count; i++)
                {
                    using var s = zip.CreateEntry($"stress/{i}.bin", CompressionLevel.Optimal).Open();
                    WriteStress(s, Stress[i]);
                }

                for (int i = 0; i < Thermal.Count; i++)
                {
                    using var s = zip.CreateEntry($"thermal/{i}.bin", CompressionLevel.Optimal).Open();
                    WriteThermal(s, Thermal[i]);
                }

                if (Aero != null)
                {
                    using var s = zip.CreateEntry("cfd.bin", CompressionLevel.Optimal).Open();
                    WriteCfd(s, Aero);
                }
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static void Text(ZipArchive zip, string name, string text)
        {
            using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            var b = new UTF8Encoding(false).GetBytes(text);
            s.Write(b, 0, b.Length);
        }

        private static string? ReadText(ZipArchive zip, string name)
        {
            var e = zip.GetEntry(name);
            if (e == null) return null;
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            return r.ReadToEnd();
        }

        public static AnalysisResult Load(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = JsonNode.Parse(ReadText(zip, "manifest.json") ?? throw new InvalidDataException("manifest.json yok")) as JsonObject
                ?? throw new InvalidDataException("manifest");
            if ((string?)manifest["format"] != Format) throw new InvalidDataException("RobotWin analiz dosyası değil / not an analysis file");
            var r = new AnalysisResult
            {
                WorldTitle = (string?)manifest["world"] ?? string.Empty,
                Created = DateTime.TryParse((string?)manifest["created"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var c) ? c : DateTime.UtcNow,
                WallSeconds = (double?)manifest["wall_seconds"] ?? 0,
                SimulatedSeconds = (double?)manifest["simulated_seconds"] ?? 0,
                Cancelled = (bool?)manifest["cancelled"] ?? false,
                HaltReason = (string?)manifest["halt_reason"] ?? string.Empty,
                Settings = manifest["settings"] is JsonObject so ? AnalysisSettings.FromJson(so) : new AnalysisSettings()
            };
            if (manifest["log"] is JsonArray log) r.Log.AddRange(log.Select(x => (string?)x ?? string.Empty));
            var ch = zip.GetEntry("channels.bin");
            if (ch != null)
            {
                using var s = ch.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                ms.Position = 0;
                r.Channels = ChannelStore.Read(ms);
            }

            if (JsonNode.Parse(ReadText(zip, "serial.json") ?? "[]") is JsonArray serial)
                foreach (var x in serial.OfType<JsonObject>())
                    r.Serial.Add(new SerialLine { Time = (double?)x["t"] ?? 0, Board = (string?)x["b"] ?? string.Empty, Text = (string?)x["s"] ?? string.Empty });
            if (JsonNode.Parse(ReadText(zip, "events.json") ?? "[]") is JsonArray events)
                foreach (var x in events.OfType<JsonObject>())
                    r.Events.Add(new EventLine { Time = (double?)x["t"] ?? 0, Source = (string?)x["src"] ?? string.Empty, Kind = (string?)x["k"] ?? string.Empty, Message = (string?)x["m"] ?? string.Empty });
            if (JsonNode.Parse(ReadText(zip, "device_parts.json") ?? "{}") is JsonObject dp)
                foreach (var kv in dp)
                    r.DeviceParts[kv.Key] = (string?)kv.Value ?? string.Empty;
            Binary(zip, "bodies.bin", rd => r.ReadBodies(rd));
            Binary(zip, "loads.bin", rd => r.ReadLoads(rd));
            for (int i = 0; zip.GetEntry($"stress/{i}.bin") != null; i++) Binary(zip, $"stress/{i}.bin", rd => r.Stress.Add(ReadStress(rd)));
            for (int i = 0; zip.GetEntry($"thermal/{i}.bin") != null; i++) Binary(zip, $"thermal/{i}.bin", rd => r.Thermal.Add(ReadThermal(rd)));
            Binary(zip, "cfd.bin", rd => r.Aero = ReadCfd(rd));
            return r;
        }

        private static void Binary(ZipArchive zip, string name, Action<BinaryReader> read)
        {
            var e = zip.GetEntry(name);
            if (e == null) return;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            using var r = new BinaryReader(ms, Encoding.UTF8);
            read(r);
        }

        // ---------------------------------------------------------------- binary helpers

        private static void W(BinaryWriter w, Vec3 v)
        {
            w.Write(v.X);
            w.Write(v.Y);
            w.Write(v.Z);
        }

        private static Vec3 V(BinaryReader r) => new Vec3(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

        private static void W(BinaryWriter w, float[]? a)
        {
            w.Write(a?.Length ?? -1);
            if (a == null) return;
            var bytes = new byte[a.Length * 4];
            Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
            w.Write(bytes);
        }

        private static float[]? Fa(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0) return null;
            var bytes = r.ReadBytes(n * 4);
            var a = new float[n];
            Buffer.BlockCopy(bytes, 0, a, 0, bytes.Length);
            return a;
        }

        private static void W(BinaryWriter w, int[]? a)
        {
            w.Write(a?.Length ?? -1);
            if (a == null) return;
            foreach (int x in a) w.Write(x);
        }

        private static int[]? Ia(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0) return null;
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = r.ReadInt32();
            return a;
        }

        private static void W(BinaryWriter w, List<double> list)
        {
            w.Write(list.Count);
            foreach (double x in list) w.Write(x);
        }

        private static void Dl(BinaryReader r, List<double> into)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) into.Add(r.ReadDouble());
        }

        private static void W(BinaryWriter w, List<string> list)
        {
            w.Write(list.Count);
            foreach (var x in list) w.Write(x);
        }

        private static void Sl(BinaryReader r, List<string> into)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) into.Add(r.ReadString());
        }

        private void WriteBodies(Stream s)
        {
            using var w = new BinaryWriter(s, Encoding.UTF8, true);
            w.Write(Bodies.Count);
            foreach (var b in Bodies)
            {
                w.Write(b.Id);
                w.Write(b.ObjectId);
                w.Write(b.Name);
                w.Write(b.Shape);
                W(w, b.Size);
                w.Write(b.IsStatic);
                W(w, b.StaticPosition);
                w.Write(b.StaticOrientation.W);
                w.Write(b.StaticOrientation.X);
                w.Write(b.StaticOrientation.Y);
                w.Write(b.StaticOrientation.Z);
                w.Write(b.Color);
                w.Write(b.Mass);
                W(w, b.MeshVertices);
                W(w, b.MeshIndices);
            }
        }

        private void ReadBodies(BinaryReader r)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var b = new BodyInfo { Id = r.ReadString(), ObjectId = r.ReadString(), Name = r.ReadString(), Shape = r.ReadString(), Size = V(r), IsStatic = r.ReadBoolean(), StaticPosition = V(r) };
                b.StaticOrientation = new Quat(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
                b.Color = r.ReadUInt32();
                b.Mass = r.ReadDouble();
                b.MeshVertices = Fa(r);
                b.MeshIndices = Ia(r);
                Bodies.Add(b);
            }
        }

        private void WriteLoads(Stream s)
        {
            using var w = new BinaryWriter(s, Encoding.UTF8, true);
            w.Write(Loads.Count);
            foreach (var kv in Loads.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                w.Write(kv.Key);
                w.Write(kv.Value.Count);
                foreach (var l in kv.Value)
                {
                    w.Write(l.Time);
                    W(w, l.Gravity);
                    W(w, l.Omega);
                    W(w, l.Acceleration);
                    w.Write(l.Loads.Count);
                    foreach (var (p, f, k) in l.Loads)
                    {
                        W(w, p);
                        W(w, f);
                        w.Write(k);
                    }
                }
            }
        }

        private void ReadLoads(BinaryReader r)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                string key = r.ReadString();
                int m = r.ReadInt32();
                var list = new List<LoadSample>(m);
                for (int j = 0; j < m; j++)
                {
                    var l = new LoadSample { Time = r.ReadDouble(), Gravity = V(r), Omega = V(r), Acceleration = V(r) };
                    int k = r.ReadInt32();
                    for (int q = 0; q < k; q++) l.Loads.Add((V(r), V(r), r.ReadByte()));
                    list.Add(l);
                }

                Loads[key] = list;
            }
        }

        private static void WriteStress(Stream s, StressResult x)
        {
            using var w = new BinaryWriter(s, Encoding.UTF8, true);
            w.Write(x.ObjectId);
            w.Write(x.Name);
            w.Write(x.Method);
            w.Write(x.Material);
            w.Write(x.Yield);
            w.Write(x.ElementSize);
            w.Write(x.Order);
            W(w, x.Nodes);
            W(w, x.Elements);
            W(w, x.Surface);
            w.Write(x.BeamWidth);
            w.Write(x.BeamHeight);
            w.Write(x.BeamCircular);
            w.Write(x.Frames.Count);
            foreach (var f in x.Frames)
            {
                w.Write(f.Time);
                W(w, f.VonMises);
                W(w, f.Displacement);
                w.Write(f.MaxVonMises);
                w.Write(f.MaxDisplacement);
                w.Write(f.MinSafetyFactor);
                w.Write(f.ReactionRatio);
                w.Write(f.TotalLoad);
                w.Write(f.Iterations);
                w.Write(f.LoadCount);
            }

            W(w, x.Notes);
        }

        private static StressResult ReadStress(BinaryReader r)
        {
            var x = new StressResult
            {
                ObjectId = r.ReadString(), Name = r.ReadString(), Method = r.ReadString(), Material = r.ReadString(), Yield = r.ReadDouble(), ElementSize = r.ReadDouble(),
                Order = r.ReadInt32()
            };
            x.Nodes = Fa(r) ?? Array.Empty<float>();
            x.Elements = Ia(r) ?? Array.Empty<int>();
            x.Surface = Ia(r) ?? Array.Empty<int>();
            x.BeamWidth = r.ReadDouble();
            x.BeamHeight = r.ReadDouble();
            x.BeamCircular = r.ReadBoolean();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var f = new StressFrame { Time = r.ReadDouble() };
                f.VonMises = Fa(r) ?? Array.Empty<float>();
                f.Displacement = Fa(r) ?? Array.Empty<float>();
                f.MaxVonMises = r.ReadDouble();
                f.MaxDisplacement = r.ReadDouble();
                f.MinSafetyFactor = r.ReadDouble();
                f.ReactionRatio = r.ReadDouble();
                f.TotalLoad = r.ReadDouble();
                f.Iterations = r.ReadInt32();
                f.LoadCount = r.ReadInt32();
                x.Frames.Add(f);
            }

            Sl(r, x.Notes);
            return x;
        }

        private static void WriteThermal(Stream s, ThermalResult x)
        {
            using var w = new BinaryWriter(s, Encoding.UTF8, true);
            w.Write(x.ObjectId);
            w.Write(x.Name);
            w.Write(x.Kind);
            w.Write(x.Material);
            w.Write(x.Nx);
            w.Write(x.Ny);
            w.Write(x.Nz);
            w.Write(x.Dx);
            w.Write(x.Dy);
            w.Write(x.Dz);
            W(w, x.Origin);
            W(w, x.Times);
            w.Write(x.Frames.Count);
            foreach (var f in x.Frames) W(w, f);
            w.Write(x.History.Count);
            foreach (var h in x.History)
            {
                w.Write(h.Time);
                w.Write(h.Max);
                w.Write(h.Mean);
                w.Write(h.Power);
            }

            w.Write(x.Sources.Count);
            foreach (var so in x.Sources)
            {
                w.Write(so.Name);
                w.Write(so.X);
                w.Write(so.Y);
            }

            W(w, x.Notes);
        }

        private static ThermalResult ReadThermal(BinaryReader r)
        {
            var x = new ThermalResult
            {
                ObjectId = r.ReadString(), Name = r.ReadString(), Kind = r.ReadString(), Material = r.ReadString(), Nx = r.ReadInt32(), Ny = r.ReadInt32(), Nz = r.ReadInt32(),
                Dx = r.ReadDouble(), Dy = r.ReadDouble(), Dz = r.ReadDouble(), Origin = V(r)
            };
            Dl(r, x.Times);
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) x.Frames.Add(Fa(r) ?? Array.Empty<float>());
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) x.History.Add((r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble()));
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) x.Sources.Add((r.ReadString(), r.ReadDouble(), r.ReadDouble()));
            Sl(r, x.Notes);
            return x;
        }

        private static void WriteCfd(Stream s, CfdResult x)
        {
            using var w = new BinaryWriter(s, Encoding.UTF8, true);
            w.Write(x.ObjectId);
            w.Write(x.Name);
            w.Write(x.Is3D);
            w.Write(x.Axis);
            w.Write(x.Nx);
            w.Write(x.Ny);
            w.Write(x.Nz);
            w.Write(x.Dx);
            w.Write(x.Speed);
            w.Write(x.ReferenceLength);
            w.Write(x.ReferenceArea);
            w.Write(x.ReynoldsPhysical);
            w.Write(x.ReynoldsLattice);
            w.Write(x.Tau);
            w.Write(x.LatticeSpeed);
            w.Write(x.AirDensity);
            w.Write(x.TimeStep);
            W(w, x.Times);
            W(w, x.Cd);
            W(w, x.Cl);
            W(w, x.Cs);
            W(w, x.Ux);
            W(w, x.Uy);
            W(w, x.Pressure);
            W(w, x.Vorticity);
            w.Write(x.Solid.Length);
            foreach (bool b in x.Solid) w.Write(b);
            W(w, x.Speed3D);
            w.Write(x.Coarse3D);
            w.Write(x.Streamlines.Count);
            foreach (var sl in x.Streamlines) W(w, sl);
            w.Write(x.MeanCd);
            w.Write(x.MeanCl);
            w.Write(x.Strouhal);
            W(w, x.Notes);
        }

        private static CfdResult ReadCfd(BinaryReader r)
        {
            var x = new CfdResult
            {
                ObjectId = r.ReadString(), Name = r.ReadString(), Is3D = r.ReadBoolean(), Axis = r.ReadString(), Nx = r.ReadInt32(), Ny = r.ReadInt32(), Nz = r.ReadInt32(),
                Dx = r.ReadDouble(), Speed = r.ReadDouble(), ReferenceLength = r.ReadDouble(), ReferenceArea = r.ReadDouble(), ReynoldsPhysical = r.ReadDouble(),
                ReynoldsLattice = r.ReadDouble(), Tau = r.ReadDouble(), LatticeSpeed = r.ReadDouble(), AirDensity = r.ReadDouble(), TimeStep = r.ReadDouble()
            };
            Dl(r, x.Times);
            Dl(r, x.Cd);
            Dl(r, x.Cl);
            Dl(r, x.Cs);
            x.Ux = Fa(r) ?? Array.Empty<float>();
            x.Uy = Fa(r) ?? Array.Empty<float>();
            x.Pressure = Fa(r) ?? Array.Empty<float>();
            x.Vorticity = Fa(r) ?? Array.Empty<float>();
            int n = r.ReadInt32();
            x.Solid = new bool[n];
            for (int i = 0; i < n; i++) x.Solid[i] = r.ReadBoolean();
            x.Speed3D = Fa(r);
            x.Coarse3D = r.ReadInt32();
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) x.Streamlines.Add(Fa(r) ?? Array.Empty<float>());
            x.MeanCd = r.ReadDouble();
            x.MeanCl = r.ReadDouble();
            x.Strouhal = r.ReadDouble();
            Sl(r, x.Notes);
            return x;
        }
    }
}
