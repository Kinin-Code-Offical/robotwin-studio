using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[assembly] What a Montaj change did, for the status line and the user.</summary>
    public sealed class AssemblyReport
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Removed { get; set; }
        public int CablesAdded { get; set; }
        public int CablesRemoved { get; set; }
        public bool BoardUpdated { get; set; }

        /// <summary>The project was built on its vehicle chassis (rover).</summary>
        public bool Vehicle { get; set; }

        public List<string> Notes { get; } = new List<string>();

        /// <summary>
        /// [integrity] The parts that left the Montaj: their reference, and whether they went onto the PCB (else they left
        /// the circuit). Said in <see cref="Summary"/> and <see cref="Lines"/>, in the tense each needs.
        /// </summary>
        public List<(string RefDes, bool OnPcb)> Left { get; } = new List<(string RefDes, bool OnPcb)>();

        /// <summary>[integrity] Parts renamed in the circuit: their objects took the new designators and stayed.</summary>
        public List<(string From, string To)> Renamed { get; } = new List<(string From, string To)>();

        public bool Changed => Added + Updated + Removed + CablesAdded + CablesRemoved + Renamed.Count > 0 || BoardUpdated;

        private IEnumerable<string> LeftLines(bool done) => Left.Select(l => l.OnPcb
            ? (done ? l.RefDes + " artık PCB'de, kartın üstünde" : l.RefDes + " PCB'ye geçti, kartın üstüne konacak")
            : (done ? l.RefDes + " devrede yok, kaldırıldı" : l.RefDes + " devrede yok, kaldırılacak"));

        public string Summary()
        {
            var parts = new List<string>();
            if (Added > 0) parts.Add(Added.ToString(CultureInfo.InvariantCulture) + " parça eklendi");
            if (Updated > 0) parts.Add(Updated.ToString(CultureInfo.InvariantCulture) + " parça güncellendi");
            if (BoardUpdated) parts.Add("kart güncellendi");
            if (Removed > 0) parts.Add(Removed.ToString(CultureInfo.InvariantCulture) + " parça kaldırıldı");
            if (Renamed.Count > 0) parts.Add(Renamed.Count.ToString(CultureInfo.InvariantCulture) + " parçanın adı değişti");
            if (CablesAdded + CablesRemoved > 0) parts.Add($"kablolar: +{CablesAdded} −{CablesRemoved}");
            string s = parts.Count == 0 ? "değişiklik yok" : string.Join(", ", parts);
            var notes = LeftLines(true).Concat(Notes).ToList();
            return notes.Count == 0 ? s : s + " · " + string.Join(" · ", notes);
        }

        /// <summary>
        /// [integrity] What changes, a line each (so a language pack translates each on its own); <paramref name="done"/>
        /// false: what bringing the Montaj up to date would do, for the project check.
        /// </summary>
        public List<string> Lines(bool done = true)
        {
            static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
            var lines = new List<string>();
            if (Added > 0) lines.Add($"{N(Added)} parça {(done ? "eklendi" : "eklenecek")}");
            if (Updated > 0) lines.Add($"{N(Updated)} parça {(done ? "güncellendi" : "güncellenecek")}");
            if (BoardUpdated) lines.Add(done ? "kart güncellendi" : "kart güncellenecek");
            if (Removed > 0) lines.Add($"{N(Removed)} parça {(done ? "kaldırıldı" : "kaldırılacak")}");
            foreach (var (from, to) in Renamed) lines.Add(done ? $"{from} → {to}: adı değişti, yeri korundu" : $"{from} → {to}: adı değişecek, yeri korunur");
            if (CablesAdded + CablesRemoved > 0) lines.Add($"kablolar: +{N(CablesAdded)} −{N(CablesRemoved)}");
            lines.AddRange(LeftLines(done));
            lines.AddRange(Notes);
            return lines;
        }
    }

    /// <summary>
    /// [assembly] Real geometry of the parts a Montaj places. The app passes its 3D models; without them the
    /// datasheet values of <see cref="ApproximateGeometry"/> and a TT gear motor are used.
    /// </summary>
    public sealed class AssemblyGeometry
    {
        /// <summary>Motor (preset or part id) → output shaft x / z in its frame, shaft tip (y), body height (m).</summary>
        public Func<string, (double ShaftX, double ShaftZ, double ShaftTip, double Height)?>? Motor { get; set; }

        /// <summary>Object → the direction its sensor looks in its own frame (HC-SR04 beam, IR emitter …).</summary>
        public Func<WorldObject, Vec3?>? SensorAxis { get; set; }

        /// <summary>Object → its bounds in its own frame (m).</summary>
        public Func<WorldObject, (Vec3 Min, Vec3 Max)?>? Bounds { get; set; }
    }

    /// <summary>
    /// [assembly] A project's circuit as a real mechanical assembly (its Montaj). Parts soldered on the project's PCB
    /// stay on one board object; every other part (motors, servos, batteries, sensor and controller modules, loose
    /// components) becomes its own real 3D part with its own mass, geometry and physics, cabled to exactly the pins
    /// its nets go to. A project whose mechanics say it is a rover is put together on its chassis: the motors under
    /// the plate with the wheels on their shafts, a ball caster, the boards, battery and modules on the deck without
    /// touching, the sensors where its mechanics put them, in its arena. A Montaj can hold several projects, each with
    /// or without its PCB, and follows them: <see cref="Sync"/> brings later circuit, PCB and code changes in, keeping
    /// where things were put and how they were joined.
    /// </summary>
    public static class ProjectAssembly
    {
        /// <summary>Symbols that are not physical parts.</summary>
        private static bool IsVirtual(PartDefinition def) =>
            def.Symbol is SymbolKind.Ground or SymbolKind.Ammeter or SymbolKind.Voltmeter or SymbolKind.Multimeter || def.Pins.Count == 0;

        /// <summary>Object refs of the chassis pieces a Montaj made itself ("project id/~şase" …): not circuit parts.</summary>
        private const string Own = "/~";

        public static string TitleOf(Schematic sch) => string.IsNullOrWhiteSpace(sch.Title) ? "Proje" : sch.Title;

        /// <summary>The project has a PCB with parts placed on it.</summary>
        public static bool HasPcb(Schematic sch) => sch.Board != null && sch.Board.Components.Count > 0;

        /// <summary>Physical parts of a circuit (what can become a real part).</summary>
        public static List<SchematicPart> PhysicalParts(Schematic sch, PartLibrary library) =>
            sch.Parts.Where(p => library.Find(p.PartId) is { } d && !IsVirtual(d)).ToList();

        /// <summary>A new Montaj with this project in it.</summary>
        public static WorldDocument Build(ProjectPackage package, PartLibrary library, string? sourcePath = null,
            AssemblyForm form = AssemblyForm.Auto, AssemblyGeometry? geometry = null)
        {
            package.Schematic.EnsureProjectId();
            var active = Variants.Active(package.Schematic);   // [variants] the active alternative is what is put together
            if (!ReferenceEquals(active, package.Schematic)) package = package.WithSchematic(active);
            var sch = package.Schematic;
            string pid = sch.EnsureProjectId();
            sch.EnsurePartUids();   // [integrity] objects follow their parts by identity
            var w = new WorldDocument { Title = TitleOf(sch) + " — montaj", IsAssembly = true, ProjectId = pid };
            Add(w, package, library, form, null, geometry);
            return w;
        }

        /// <summary>
        /// Brings a project into a Montaj, beside what is already there (its own project, or another one).
        /// A project already in it is synchronised instead (with the form given).
        /// </summary>
        public static AssemblyReport Add(WorldDocument w, ProjectPackage package, PartLibrary library, AssemblyForm form,
            string? sourcePath = null, AssemblyGeometry? geometry = null)
        {
            package.Schematic.EnsureProjectId();
            var active = Variants.Active(package.Schematic);   // [variants] the active alternative is what is put together
            if (!ReferenceEquals(active, package.Schematic)) package = package.WithSchematic(active);
            var sch = package.Schematic;
            string pid = sch.EnsureProjectId();
            sch.EnsurePartUids();   // [integrity] objects follow their parts by identity
            if (w.SourceOf(pid) is { } known)
            {
                known.Form = form;
                return Sync(w, package, library, sourcePath, geometry);
            }

            var report = new AssemblyReport();
            var source = new AssemblySource
            {
                ProjectId = pid, Title = TitleOf(sch), Form = form,
                Path = w.ProjectId != null && w.ProjectId != pid ? sourcePath : null
            };
            w.AssemblySources.Add(source);
            bool first = w.Objects.Count == 0;
            var before = new HashSet<WorldObject>(w.Objects);

            bool usePcb = UsePcb(form, sch, report);
            source.UsedPcb = usePcb;
            var onBoard = OnBoard(sch, usePcb);
            var board = usePcb ? AddBoard(w, package, library, onBoard, sourcePath, new WorldPose()) : null;
            var loose = new Dictionary<string, WorldObject>(StringComparer.OrdinalIgnoreCase);
            var ordered = PhysicalParts(sch, library).Where(p => !onBoard.Contains(p.RefDes))
                .OrderBy(p => Rank(library.Find(p.PartId)!)).ThenBy(p => p.RefDes, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var p in ordered) loose[p.RefDes] = AddPart(w, package, library, p, pid, new WorldPose());

            report.Vehicle = BuildRover(w, sch, library, pid, board, loose, geometry, report);
            if (!report.Vehicle) Ring(board, ordered.Select(p => loose[p.RefDes]).ToList());
            report.Added = loose.Count + (board != null ? 1 : 0);
            var made = SyncCables(w, sch, library, pid, report);
            report.CablesAdded = made;
            report.CablesRemoved = 0;

            var group = w.Objects.Where(o => !before.Contains(o)).ToList();
            if (first)
            {
                if (report.Vehicle && sch.Assembly != null) PutInArena(w, sch.Assembly, group);
            }
            else
            {
                MoveBeside(w, library, group, geometry);
            }

            CableLengths(w);
            return report;
        }

        /// <summary>
        /// Brings a project's current circuit, PCB and code into the Montaj: a part added to the circuit is added (and
        /// cabled), a part removed is removed, a changed part (type, values, code) is updated where it stands, the
        /// board takes the current PCB and code, and the cables follow the nets. Where parts were put and how they were
        /// joined is kept; cables you laid yourself are left alone.
        /// </summary>
        public static AssemblyReport Sync(WorldDocument w, ProjectPackage package, PartLibrary library, string? sourcePath = null,
            AssemblyGeometry? geometry = null)
        {
            package.Schematic.EnsureProjectId();
            var active = Variants.Active(package.Schematic);   // [variants] the active alternative is what is put together
            if (!ReferenceEquals(active, package.Schematic)) package = package.WithSchematic(active);
            var sch = package.Schematic;
            string pid = sch.EnsureProjectId();
            sch.EnsurePartUids();   // [integrity] objects follow their parts by identity
            var source = w.SourceOf(pid);
            if (source == null) return Add(w, package, library, AssemblyForm.Auto, sourcePath, geometry);

            var report = new AssemblyReport();
            source.Title = TitleOf(sch);
            bool usePcb = UsePcb(source.Form, sch, report);
            source.UsedPcb = usePcb;
            var onBoard = OnBoard(sch, usePcb);
            var mine = w.Objects.Where(o => IsFrom(o, pid) || BoardProject(w, o) == pid).ToList();

            // ---- the board: the current PCB and code (or gone, now that nothing is placed on it)
            var boards = w.Objects.Where(o => BoardProject(w, o) == pid).ToList();
            WorldObject? board = boards.FirstOrDefault();
            if (usePcb)
            {
                if (board == null)
                {
                    board = AddBoard(w, package, library, onBoard, sourcePath, Near(w, library, mine, geometry));
                    report.Added++;
                }
                else if (w.FindDesign(board.DesignId) is { } design)
                {
                    var fresh = BoardPackage(package, library, onBoard);
                    if (!SamePackage(design.Package, fresh))
                    {
                        design.Package = fresh;
                        design.Name = TitleOf(sch);
                        report.BoardUpdated = true;
                    }
                }
            }
            else
            {
                foreach (var b in boards)
                {
                    Remove(w, b);
                    report.Removed++;
                }

                board = null;
            }

            // ---- the separate parts
            FollowRenamedParts(w, sch, pid, report);
            var existing = w.Objects.Where(o => IsFrom(o, pid) && !o.ProjectRef!.Contains(Own))
                .GroupBy(o => o.ProjectRef!.Substring(pid.Length + 1), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var wanted = PhysicalParts(sch, library).Where(p => !onBoard.Contains(p.RefDes)).ToList();
            foreach (var p in wanted)
            {
                if (existing.TryGetValue(p.RefDes, out var o))
                {
                    o.PartUid = p.Uid;   // [integrity] an object found by designator takes its part's identity
                    if (UpdatePart(o, package, library, p)) report.Updated++;
                }
                else
                {
                    var added = AddPart(w, package, library, p, pid, Near(w, library, mine, geometry));
                    mine.Add(added);
                    report.Added++;
                }
            }

            foreach (var kv in existing.Where(kv => wanted.All(p => !string.Equals(p.RefDes, kv.Key, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                Remove(w, kv.Value);
                report.Removed++;
                report.Left.Add((kv.Key, sch.FindPart(kv.Key) != null));
            }

            // ---- cables follow the nets
            int before = w.Cables.Count(c => IsNetOf(c, pid));
            int made = SyncCables(w, sch, library, pid, report);
            int after = w.Cables.Count(c => IsNetOf(c, pid));
            report.CablesAdded = made;
            report.CablesRemoved = Math.Max(0, before + made - after);
            CableLengths(w);
            return report;
        }

        /// <summary>Removes a project from a Montaj: its board, its parts, their joints and cables (chassis pieces too).</summary>
        public static int RemoveProject(WorldDocument w, string projectId)
        {
            var gone = w.Objects.Where(o => IsFrom(o, projectId) || BoardProject(w, o) == projectId).ToList();
            foreach (var o in gone) Remove(w, o);
            w.Cables.RemoveAll(c => IsNetOf(c, projectId));
            w.AssemblySources.RemoveAll(s => string.Equals(s.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
            return gone.Count;
        }

        // ================================================================ placing

        /// <summary>Bounds of objects in the world (m, axis-aligned, poses applied); null when there are none.</summary>
        public static (Vec3 Min, Vec3 Max)? Extent(WorldDocument w, PartLibrary library, IEnumerable<WorldObject> objects, AssemblyGeometry? geometry = null)
        {
            var approx = new ApproximateGeometry(w, library);
            bool any = false;
            var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
            var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
            foreach (var o in objects)
            {
                var (lo, hi) = LocalBounds(o, approx, geometry);
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vec3((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z);
                    var p = o.Pose.ToWorld(corner);
                    min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                    max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                    any = true;
                }
            }

            return any ? (min, max) : ((Vec3, Vec3)?)null;
        }

        /// <summary>
        /// Moves a group of new objects (keeping their arrangement) beside everything else in the world, on the ground:
        /// nothing starts inside something else (bodies that start interpenetrating are thrown apart by the physics).
        /// </summary>
        public static void MoveBeside(WorldDocument w, PartLibrary library, IList<WorldObject> group, AssemblyGeometry? geometry = null, double gap = 0.06)
        {
            if (group.Count == 0) return;
            var set = new HashSet<WorldObject>(group);
            var rest = Extent(w, library, w.Objects.Where(o => !set.Contains(o) && !WorldBuilding.IsProp(o)), geometry);
            var mine = Extent(w, library, group, geometry);
            if (mine == null) return;
            double dx = 0, dy = 0;
            if (rest != null)
            {
                // To the right (−y) of what is there, centred on it along x.
                dy = rest.Value.Min.Y - gap - mine.Value.Max.Y;
                dx = (rest.Value.Min.X + rest.Value.Max.X) / 2 - (mine.Value.Min.X + mine.Value.Max.X) / 2;
            }

            double dz = mine.Value.Min.Z < 0 ? -mine.Value.Min.Z : 0;   // the whole group up, so it keeps its shape
            foreach (var o in group)
            {
                o.Pose.X += dx;
                o.Pose.Y += dy;
                o.Pose.Z += dz;
            }
        }

        /// <summary>Where a new part of a project goes: just beside the project's other objects (or beside everything).</summary>
        private static WorldPose Near(WorldDocument w, PartLibrary library, List<WorldObject> mine, AssemblyGeometry? geometry)
        {
            var ext = Extent(w, library, mine.Count > 0 ? mine : w.Objects, geometry);
            if (ext == null) return new WorldPose();
            // Off the far (+x) end of the group, on the ground, a little apart from what is already waiting there.
            int waiting = w.Objects.Count(o => o.Pose.X > ext.Value.Max.X);
            return new WorldPose { X = ext.Value.Max.X + 0.06 + 0.07 * waiting, Y = (ext.Value.Min.Y + ext.Value.Max.Y) / 2 };
        }

        private static (Vec3 Min, Vec3 Max) LocalBounds(WorldObject o, ApproximateGeometry approx, AssemblyGeometry? geometry)
        {
            if (geometry?.Bounds?.Invoke(o) is { } b) return b;
            try
            {
                var g = approx.GeometryOf(o);
                if (g.Max.X > g.Min.X) return (g.Min, g.Max);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is KeyNotFoundException || ex is InvalidOperationException)
            {
            }

            return (new Vec3(-0.015, -0.015, 0), new Vec3(0.015, 0.015, 0.02));
        }

        /// <summary>Concentric rings round the board (or round the first part), ~12 cm apart: room for every part.</summary>
        private static void Ring(WorldObject? board, List<WorldObject> parts)
        {
            double ring = board != null ? 0.16 : 0.0;
            int n = parts.Count;
            for (int i = 0; i < n; i++)
            {
                if (board == null && i == 0)
                {
                    parts[i].Pose = new WorldPose();
                    continue;
                }

                int k = board == null ? i - 1 : i;
                const int perRing = 8;
                int r = k / perRing;
                double radius = (board == null ? 0.14 : ring) + r * 0.12;
                double a = 2 * Math.PI * (k % perRing) / Math.Min(perRing, Math.Max(1, n - (board == null ? 1 : 0) - r * perRing)) + r * 0.3;
                parts[i].Pose = new WorldPose { X = radius * Math.Cos(a), Y = radius * Math.Sin(a), Yaw = a * 180 / Math.PI + 90 };
            }
        }

        private static int Rank(PartDefinition def) => def.Symbol == SymbolKind.Board ? 0 : def.Symbol == SymbolKind.Battery ? 1 : 2;

        // ================================================================ the rover on its chassis

        /// <summary>
        /// A project whose mechanics say "rover" (two motors, left and right) is put together as one: a plate, the
        /// motors under it with the wheels on their output shafts (the right motor mirrored on the same axle, its leads
        /// swapped as on a real kit, unless its mount says it is reversed), a ball caster at the front, the boards,
        /// battery and modules on the deck without touching, and the sensors where its mechanics put them. All real
        /// parts, fixed to the plate. False (and a note) when it cannot be built that way.
        /// </summary>
        private static bool BuildRover(WorldDocument w, Schematic sch, PartLibrary library, string pid, WorldObject? board,
            Dictionary<string, WorldObject> loose, AssemblyGeometry? geometry, AssemblyReport report)
        {
            var a = sch.Assembly;
            if (a == null || a.Kind != VehicleKind.Rover) return false;
            var leftMount = a.MotorFor("L");
            var rightMount = a.MotorFor("R");
            if (leftMount == null || rightMount == null || !loose.TryGetValue(leftMount.RefDes, out var ml) || !loose.TryGetValue(rightMount.RefDes, out var mr))
            {
                report.Notes.Add("araç gövdesi kurulamadı: sol ve sağ motor ayrı parça olmalı");
                return false;
            }

            string motorId = ml.PresetId ?? ml.PartId ?? string.Empty;
            var approx = new ApproximateGeometry(w, library);
            var m = geometry?.Motor?.Invoke(motorId) ?? SideShaft(ml, approx);
            if (m == null)
            {
                report.Notes.Add("araç gövdesi kurulamadı: motorun mili yana çıkmıyor (tekerlek doğrudan takılamaz)");
                return false;
            }

            var (shaftX, shaftZ, shaftTip, height) = m.Value;
            double r = a.WheelRadius > 0.005 ? a.WheelRadius : 0.0325;
            double wheelW = a.WheelWidth > 0.004 ? a.WheelWidth : 0.026;
            double motorZ = r - shaftZ, plateZ = motorZ + height, deck = plateZ + 0.003;
            double length = Math.Max(0.12, a.Length);
            const double plateWidth = 0.09;   // two gear motors back to back; the wheels run clear of its edges
            double axleX = Math.Max(-length / 2 + r * 0.6, Math.Min(length / 2 - 0.05, a.AxleX));

            var plate = w.AddBody("plate", length, plateWidth, 0.003, new WorldPose { Z = plateZ }, string.IsNullOrEmpty(a.ChassisMaterial) ? "PLA" : a.ChassisMaterial);
            plate.Name = w.UniqueName("Şase");
            plate.Color = "#3E7CC8";
            plate.ProjectRef = pid + Own + "şase";
            var fixedTo = new List<WorldObject>();

            ml.Pose = new WorldPose { X = axleX - shaftX, Y = 0.03, Z = motorZ };
            // Rolled over (as on a real kit) so both shafts share one axle; raised so its shaft is at the same height
            // as the left one's (the gearbox shaft is not at mid-height): both wheels touch the floor, the robot sits level.
            mr.Pose = new WorldPose { X = axleX - shaftX, Y = -0.03, Z = motorZ + 2 * shaftZ, Roll = 180 };
            ml.MirroredLeads = leftMount.Reverse;
            mr.MirroredLeads = !rightMount.Reverse;   // mirrored on the axle: swapped leads make "forward" forward
            fixedTo.Add(ml);
            fixedTo.Add(mr);
            var wl = w.AddBody("wheel", 2 * r, wheelW, wheelW, new WorldPose { X = axleX, Y = 0.03 + shaftTip + wheelW / 2 + 0.002, Z = 0 });
            var wr = w.AddBody("wheel", 2 * r, wheelW, wheelW, new WorldPose { X = axleX, Y = -0.03 - shaftTip - wheelW / 2 - 0.002, Z = 0 });
            wl.Name = w.UniqueName("Sol tekerlek");
            wr.Name = w.UniqueName("Sağ tekerlek");
            wl.ProjectRef = pid + Own + "tekerlek-sol";
            wr.ProjectRef = pid + Own + "tekerlek-sağ";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wl.Id, ObjectB = ml.Id, PortB = "shaft" });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wr.Id, ObjectB = mr.Id, PortB = "shaft" });

            // Ball caster on a post under the front of the plate.
            const double ball = 0.016;
            double casterX = length / 2 - 0.025;
            var caster = w.AddBody("sphere", ball, ball, ball, new WorldPose { X = casterX, Z = 0 }, "ÇELİK");
            caster.Name = w.UniqueName("Sarhoş teker");
            caster.ProjectRef = pid + Own + "sarhoş-teker";
            var post = w.AddBody("box", 0.012, 0.012, plateZ - ball * 0.8, new WorldPose { X = casterX, Z = ball * 0.8 }, "PLA");
            post.Name = w.UniqueName("Teker ayağı");
            post.ProjectRef = pid + Own + "teker-ayağı";
            fixedTo.Add(caster);
            fixedTo.Add(post);

            // Sensors where the mechanics put them. Their heights are measured from the mechanics' own deck (wheel radius
            // + deck), so a line sensor rides the few millimetres above the floor it was meant to, on a post under the plate.
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { leftMount.RefDes, rightMount.RefDes };
            foreach (var s in a.Sensors)
            {
                if (!loose.TryGetValue(s.RefDes, out var so)) continue;
                double x = Math.Min(length / 2 + 0.01, s.X);
                if (s.Kind == SensorKind.Line)
                {
                    double h = Math.Max(0.003, a.WheelRadius + a.DeckZ + s.Z);
                    so.Pose = new WorldPose { X = x, Y = s.Y, Z = h };
                    var (lo, hi) = LocalBounds(so, approx, geometry);
                    double top = h + (hi.Z - lo.Z);
                    if (plateZ - top > 0.002)
                    {
                        var leg = w.AddBody("box", 0.006, 0.01, plateZ - top, new WorldPose { X = Math.Min(x - (hi.X - lo.X) / 2 - 0.004, length / 2 - 0.004), Y = s.Y, Z = top }, "PLA");
                        leg.Name = w.UniqueName("Sensör ayağı");
                        leg.ProjectRef = pid + Own + "sensör-ayağı-" + s.RefDes;
                        fixedTo.Add(leg);
                    }
                }
                else
                {
                    double z = s.Kind == SensorKind.Imu ? deck : WorldExamples.OnPins(deck);
                    so.Pose = new WorldPose { X = Math.Min(length / 2, s.X), Y = s.Y, Z = z, Yaw = FacingYaw(so, s.Yaw, approx, geometry) };
                }

                fixedTo.Add(so);
                placed.Add(s.RefDes);
            }

            // The rest on the deck, packed in rows from the front back: real footprints, a few millimetres apart.
            var deckItems = new List<WorldObject>();
            if (board != null) deckItems.Add(board);
            deckItems.AddRange(loose.Where(kv => !placed.Contains(kv.Key)).Select(kv => kv.Value));
            double front = length / 2 - 0.02 - ball, back = -length / 2 + 0.004, rowX = front, rowDepth = 0, y = plateWidth / 2;
            var offDeck = new List<WorldObject>();
            foreach (var o in deckItems.OrderByDescending(o => Area(o, approx, geometry)))
            {
                var (lo, hi) = LocalBounds(o, approx, geometry);
                double sx = hi.X - lo.X, sy = hi.Y - lo.Y;
                bool turn = sy > sx && sy > plateWidth;   // a long part lengthwise along the deck
                if (turn) (sx, sy) = (sy, sx);
                if (sy > plateWidth + 0.03 || y - sy < -plateWidth / 2 - 0.004)
                {
                    // Next row (or too wide for the deck at all).
                    rowX -= rowDepth + 0.004;
                    rowDepth = 0;
                    y = plateWidth / 2;
                }

                if (sy > plateWidth + 0.03 || rowX - sx < back)
                {
                    offDeck.Add(o);
                    continue;
                }

                double cx = rowX - sx / 2, cy = y - sy / 2;
                y -= sy + 0.004;
                rowDepth = Math.Max(rowDepth, sx);
                double yaw = turn ? 90 : 0;
                // Place by the bounds' centre: the part's frame need not be its middle.
                double ox = turn ? -(lo.Y + hi.Y) / 2 : (lo.X + hi.X) / 2, oy = turn ? (lo.X + hi.X) / 2 : (lo.Y + hi.Y) / 2;
                double z = o.Kind == WorldObjectKind.Component && library.Find(o.PartId ?? string.Empty)?.Symbol == SymbolKind.Board ? WorldExamples.OnPins(deck) : deck;
                o.Pose = new WorldPose { X = cx - ox, Y = cy - oy, Z = z - lo.Z, Yaw = yaw };
                fixedTo.Add(o);
            }

            if (offDeck.Count > 0)
            {
                // Did not fit: on the floor beside the robot, not fixed (the user mounts them).
                double x = -length / 2;
                foreach (var o in offDeck)
                {
                    var (lo, hi) = LocalBounds(o, approx, geometry);
                    o.Pose = new WorldPose { X = x - lo.X, Y = -plateWidth / 2 - 0.1 - hi.Y, Z = -lo.Z };
                    x += hi.X - lo.X + 0.02;
                }

                report.Notes.Add(string.Join(", ", offDeck.Select(o => o.Name)) + " şaseye sığmadı, yanına kondu");
            }

            foreach (var o in fixedTo) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = o.Id, ObjectB = plate.Id });
            return true;
        }

        /// <summary>A gear motor's output shaft across its body (a TT motor): shaft x / z, tip y, body height; null otherwise.</summary>
        private static (double ShaftX, double ShaftZ, double ShaftTip, double Height)? SideShaft(WorldObject motor, ApproximateGeometry approx)
        {
            try
            {
                var g = approx.GeometryOf(motor);
                if (g.PortName != "shaft" || Math.Abs(g.PortAxis.Y) < 0.9 || g.Max.Z <= 0) return null;
                return (g.PortPivot.X, g.PortPivot.Z, Math.Abs(g.PortPivot.Y), g.Max.Z);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        private static double Area(WorldObject o, ApproximateGeometry approx, AssemblyGeometry? geometry)
        {
            var (lo, hi) = LocalBounds(o, approx, geometry);
            return (hi.X - lo.X) * (hi.Y - lo.Y);
        }

        /// <summary>The yaw (degrees) that turns a part's sensor to look along the robot's +x, turned by <paramref name="mountYaw"/>.</summary>
        private static double FacingYaw(WorldObject o, double mountYaw, ApproximateGeometry approx, AssemblyGeometry? geometry)
        {
            Vec3? axis = geometry?.SensorAxis?.Invoke(o);
            if (axis == null)
            {
                try
                {
                    var g = approx.GeometryOf(o);
                    if (g.SensorAxis.Length > 1e-9) axis = g.SensorAxis;
                }
                catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is KeyNotFoundException || ex is InvalidOperationException)
                {
                }
            }

            if (axis == null || Math.Abs(axis.Value.X) + Math.Abs(axis.Value.Y) < 1e-6) return mountYaw;
            double own = Math.Atan2(axis.Value.Y, axis.Value.X) * 180 / Math.PI;
            return mountYaw - own;
        }

        /// <summary>
        /// Where a robot starts in an arena (m, m, degrees): a custom world's own start, else the preset's — the oval's
        /// bottom straight, the figure eight's heading, the obstacle field's corner. "" = the plain workshop (origin).
        /// </summary>
        public static (double X, double Y, double YawDeg) ArenaStart(string arena, WorldSpec? custom = null)
        {
            if (arena == "custom" && custom != null) return (custom.StartX, custom.StartY, custom.StartYaw);
            if (string.IsNullOrEmpty(arena)) return (0, 0, 0);
            var preset = Arena.Create(arena);
            return (preset.StartX, preset.StartY, preset.StartYaw * 180 / Math.PI);
        }

        /// <summary>
        /// Moves everything in a Montaj to an arena's start, keeping how it is put together: a Montaj-built chassis is
        /// turned to the start heading and set on the start; anything else is moved so its middle is on the start.
        /// </summary>
        public static void MoveToStart(WorldDocument w, PartLibrary library, (double X, double Y, double YawDeg) start)
        {
            var chassis = w.Objects.FirstOrDefault(o => o.ProjectRef != null && o.ProjectRef.EndsWith(Own + "şase", StringComparison.Ordinal));
            double cx, cy, turn;
            if (chassis != null)
            {
                cx = chassis.Pose.X;
                cy = chassis.Pose.Y;
                turn = start.YawDeg - chassis.Pose.Yaw;
            }
            else if (Extent(w, library, w.Objects.Where(o => !WorldBuilding.IsProp(o))) is { } ext)
            {
                cx = (ext.Min.X + ext.Max.X) / 2;
                cy = (ext.Min.Y + ext.Max.Y) / 2;
                turn = 0;
            }
            else
            {
                return;
            }

            double c = Math.Cos(turn * Math.PI / 180), s = Math.Sin(turn * Math.PI / 180);
            foreach (var o in w.Objects.Where(o => !WorldBuilding.IsProp(o)))
            {
                double x = o.Pose.X - cx, y = o.Pose.Y - cy;
                o.Pose.X = start.X + c * x - s * y;
                o.Pose.Y = start.Y + s * x + c * y;
                o.Pose.Yaw += turn;
            }
        }

        /// <summary>The robot into its arena: the arena becomes the Montaj's environment and the robot stands at its start.</summary>
        private static void PutInArena(WorldDocument w, AssemblyDocument a, List<WorldObject> robot)
        {
            WorldSpec env;
            try
            {
                env = a.Arena == "custom" && a.World != null
                    ? System.Text.Json.JsonSerializer.Deserialize<WorldSpec>(System.Text.Json.JsonSerializer.Serialize(a.World, RobotWinFormats.Json), RobotWinFormats.Json)!
                    : WorldSpec.FromPreset(a.Arena);
                env.Name = a.Arena == "custom" ? (string.IsNullOrWhiteSpace(env.Name) ? "Özel alan" : env.Name) : Arena.Create(a.Arena).Name;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.Text.Json.JsonException)
            {
                return;
            }

            w.Environment = env;
            var arenaStart = ArenaStart(a.Arena, a.World);
            double sx = a.StartX ?? arenaStart.X, sy = a.StartY ?? arenaStart.Y, yaw = a.StartYaw ?? arenaStart.YawDeg;
            double c = Math.Cos(yaw * Math.PI / 180), s = Math.Sin(yaw * Math.PI / 180);
            foreach (var o in robot)
            {
                double x = o.Pose.X, y = o.Pose.Y;
                o.Pose.X = sx + c * x - s * y;
                o.Pose.Y = sy + s * x + c * y;
                o.Pose.Yaw += yaw;
            }
        }

        // ================================================================ parts, board, cables

        private static bool UsePcb(AssemblyForm form, Schematic sch, AssemblyReport report)
        {
            if (form == AssemblyForm.LooseParts) return false;
            bool has = HasPcb(sch);
            if (form == AssemblyForm.WithPcb && !has) report.Notes.Add($"'{TitleOf(sch)}' projesinin PCB'sinde yerleşik parça yok: parçalar ayrı geldi");
            return has;
        }

        private static HashSet<string> OnBoard(Schematic sch, bool usePcb) =>
            new HashSet<string>(usePcb ? sch.Board!.Components.Select(c => c.RefDes) : Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        private static bool IsFrom(WorldObject o, string pid) =>
            o.ProjectRef != null && o.ProjectRef.StartsWith(pid + "/", StringComparison.OrdinalIgnoreCase);

        private static bool IsNetOf(WorldCable c, string pid) =>
            c.ProjectNet != null && c.ProjectNet.StartsWith(pid + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>The project a board object's design came from (null for other objects).</summary>
        private static string? BoardProject(WorldDocument w, WorldObject o) =>
            o.Kind == WorldObjectKind.Board && w.FindDesign(o.DesignId)?.Package.Schematic.ProjectId is { Length: > 0 } id ? id : null;

        /// <summary>The project as its PCB: only what is soldered on it (the rest are separate parts).</summary>
        private static ProjectPackage BoardPackage(ProjectPackage package, PartLibrary library, HashSet<string> onBoard)
        {
            var copy = ProjectPackage.FromBytes(package.ToBytes());
            copy.Montaj = null;
            copy.Schematic.Parts.RemoveAll(p => !onBoard.Contains(p.RefDes) && library.Find(p.PartId) is { } d && !IsVirtual(d));
            return copy;
        }

        private static bool SamePackage(ProjectPackage a, ProjectPackage b)
        {
            if (a.Schematic.ToJson() != b.Schematic.ToJson()) return false;
            if (a.Firmware.Count != b.Firmware.Count) return false;
            foreach (var f in a.Firmware)
            {
                var g = b.FirmwareFor(f.BoardRefDes);
                if (g == null || g.Bundle() != f.Bundle() || g.Hex != f.Hex) return false;
            }

            return true;
        }

        private static WorldObject AddBoard(WorldDocument w, ProjectPackage package, PartLibrary library, HashSet<string> onBoard, string? sourcePath, WorldPose pose)
        {
            var design = w.AddDesign(BoardPackage(package, library, onBoard), sourcePath);
            var board = w.AddBoard(design, pose);
            board.Name = w.UniqueName(TitleOf(package.Schematic) + " (kart)");
            return board;
        }

        private static WorldObject AddPart(WorldDocument w, ProjectPackage package, PartLibrary library, SchematicPart p, string pid, WorldPose pose)
        {
            var def = library.Find(p.PartId)!;
            var o = w.AddComponent(library, p.PresetId ?? def.Id, pose);
            o.Name = w.UniqueName($"{p.RefDes} {def.Name}");
            o.ProjectRef = pid + "/" + p.RefDes;
            o.PartUid = p.Uid;   // [integrity]
            foreach (var kv in p.Parameters) o.Parameters[kv.Key] = kv.Value;
            if (package.FirmwareFor(p.RefDes) is { } fw) o.Firmware = Copy(fw);   // a copy: the Montaj follows the project through Sync
            return o;
        }

        /// <summary>
        /// [integrity] A part renamed in the circuit keeps its object: found by the part's identity, the object takes the new
        /// designator (and the name made of it), so where it was put, how it was joined and its cables stay. An object from
        /// before identities takes its part's by designator.
        /// </summary>
        private static void FollowRenamedParts(WorldDocument w, Schematic sch, string pid, AssemblyReport report)
        {
            var byUid = new Dictionary<string, SchematicPart>(StringComparer.Ordinal);
            foreach (var p in sch.Parts)
                if (!string.IsNullOrEmpty(p.Uid)) byUid.TryAdd(p.Uid, p);
            foreach (var o in w.Objects.Where(o => IsFrom(o, pid) && !o.ProjectRef!.Contains(Own)))
            {
                string refDes = o.ProjectRef!.Substring(pid.Length + 1);
                if (string.IsNullOrEmpty(o.PartUid))
                {
                    if (sch.FindPart(refDes) is { } owner) o.PartUid = owner.Uid;
                    continue;
                }

                if (!byUid.TryGetValue(o.PartUid!, out var part) || string.Equals(part.RefDes, refDes, StringComparison.Ordinal)) continue;
                o.ProjectRef = pid + "/" + part.RefDes;
                if (o.Name.StartsWith(refDes + " ", StringComparison.Ordinal)) o.Name = w.UniqueName(part.RefDes + o.Name.Substring(refDes.Length));
                report.Renamed.Add((refDes, part.RefDes));
            }
        }

        /// <summary>A part's type, values and code from the circuit; true when something changed.</summary>
        private static bool UpdatePart(WorldObject o, ProjectPackage package, PartLibrary library, SchematicPart p)
        {
            var def = library.Find(p.PartId)!;
            bool changed = false;
            string? preset = p.PresetId ?? (library.FindPreset(def.Id) != null ? def.Id : null);
            if (!string.Equals(o.PartId, def.Id, StringComparison.OrdinalIgnoreCase) || !string.Equals(o.PresetId ?? string.Empty, preset ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                o.PartId = def.Id;
                o.PresetId = preset;
                changed = true;
            }

            foreach (var kv in p.Parameters)
            {
                if (o.Parameters.TryGetValue(kv.Key, out var v) && v == kv.Value) continue;
                o.Parameters[kv.Key] = kv.Value;
                changed = true;
            }

            var fw = package.FirmwareFor(p.RefDes);
            if (fw != null && (o.Firmware == null || o.Firmware.Bundle() != fw.Bundle() || o.Firmware.Hex != fw.Hex))
            {
                o.Firmware = Copy(fw);
                changed = true;
            }

            return changed;
        }

        private static FirmwareSource Copy(FirmwareSource f) => new FirmwareSource
        {
            BoardRefDes = f.BoardRefDes, Target = f.Target, FileName = f.FileName, Source = f.Source, Hex = f.Hex,
            BuiltAt = f.BuiltAt, BuiltFromHash = f.BuiltFromHash,
            Files = f.Files.Select(x => new FirmwareFile { Path = x.Path, Content = x.Content }).ToList()
        };

        private static void Remove(WorldDocument w, WorldObject o)
        {
            w.Objects.Remove(o);
            w.Cables.RemoveAll(c => c.A.ObjectId == o.Id || c.B.ObjectId == o.Id);
            w.Joints.RemoveAll(j => j.ObjectA == o.Id || j.ObjectB == o.Id);
        }

        /// <summary>
        /// The cables a project's nets need: every separate part's pin to its net — to the board's own pin on that net
        /// when the net reaches the PCB (a connector pin first), otherwise to one anchor pin of the net (a controller's).
        /// Existing Montaj cables that still fit are kept (with their shape and length), the rest removed, the missing
        /// ones laid. Returns how many were laid.
        /// </summary>
        private static int SyncCables(WorldDocument w, Schematic sch, PartLibrary library, string pid, AssemblyReport report)
        {
            var netlist = sch.BuildNetlist(library);
            var objects = w.Objects.Where(o => IsFrom(o, pid) && !o.ProjectRef!.Contains(Own))
                .GroupBy(o => o.ProjectRef!.Substring(pid.Length + 1), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var board = w.Objects.FirstOrDefault(o => BoardProject(w, o) == pid);
            var onBoard = new HashSet<string>(board != null ? w.FindDesign(board.DesignId)!.Package.Schematic.Parts.Select(p => p.RefDes) : Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            string Pin(WorldObject o, string pin) => o.MirroredLeads ? pin == "A" ? "B" : pin == "B" ? "A" : pin : pin;

            var wanted = new List<(CableEnd A, CableEnd B, double Awg, string Color, string Net)>();
            int colour = 0;
            string[] signal = { "#F2A33B", "#2F7FD8", "#4CC38A", "#B07CF2", "#F2F2F2", "#E8C547" };
            foreach (var net in netlist.Nets.OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                var loose = net.Connections.Where(c => objects.ContainsKey(c.RefDes)).ToList();
                if (loose.Count == 0) continue;
                var onPcb = board == null ? new List<NetConnection>() : net.Connections.Where(c => onBoard.Contains(c.RefDes)).ToList();
                if (onPcb.Count == 0 && loose.Count < 2) continue;
                bool ground = net.IsGround || net.Name.StartsWith("GND", StringComparison.OrdinalIgnoreCase);
                bool power = !ground && (net.Name.Contains("V") && (net.Name.Contains("CC") || net.Name.Contains("IN") || net.Name.Contains("BAT") || char.IsDigit(net.Name.FirstOrDefault())) || net.Name.StartsWith("+"));
                string col = ground ? "#1F1F23" : power ? "#E5484D" : signal[colour++ % signal.Length];
                double awg = ground || power ? 22 : 26;
                CableEnd anchor;
                IEnumerable<NetConnection> rest;
                if (onPcb.Count > 0)
                {
                    var best = onPcb.OrderBy(c => sch.FindPart(c.RefDes) is { } bp && library.Find(bp.PartId)?.Symbol == SymbolKind.Connector ? 0 : 1)
                        .ThenBy(c => c.RefDes, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Pin, StringComparer.Ordinal).First();
                    anchor = new CableEnd { ObjectId = board!.Id, Pin = best.RefDes + "." + best.Pin };
                    rest = loose;
                }
                else
                {
                    var hub = loose.OrderBy(c => library.Find(sch.FindPart(c.RefDes)!.PartId)!.Symbol == SymbolKind.Board ? 0 : 1)
                        .ThenBy(c => c.RefDes, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Pin, StringComparer.Ordinal).First();
                    anchor = new CableEnd { ObjectId = objects[hub.RefDes].Id, Pin = Pin(objects[hub.RefDes], hub.Pin) };
                    rest = loose.Where(c => c != hub);
                }

                foreach (var c in rest)
                {
                    var o = objects[c.RefDes];
                    wanted.Add((anchor, new CableEnd { ObjectId = o.Id, Pin = Pin(o, c.Pin) }, awg, col, pid + "/" + net.Name));
                }
            }

            string Key(CableEnd a, CableEnd b)
            {
                string x = a.ObjectId + ":" + a.Pin, y = b.ObjectId + ":" + b.Pin;
                return string.CompareOrdinal(x, y) < 0 ? x + "|" + y : y + "|" + x;
            }

            var keep = new HashSet<string>(wanted.Select(x => Key(x.A, x.B)));
            var have = new HashSet<string>();
            foreach (var c in w.Cables.Where(c => IsNetOf(c, pid)).ToList())
            {
                string k = Key(c.A, c.B);
                if (keep.Contains(k) && have.Add(k)) continue;
                w.Cables.Remove(c);
            }

            int made = 0;
            foreach (var x in wanted)
            {
                if (have.Contains(Key(x.A, x.B))) continue;
                var cable = w.Connect(x.A, x.B, x.Awg, x.Color);
                cable.ProjectNet = x.Net;
                cable.Length = 0;   // measured once everything stands where it goes
                have.Add(Key(x.A, x.B));
                made++;
            }

            return made;
        }

        /// <summary>Jumper lengths (5 cm steps, with slack) for the Montaj cables that have none yet.</summary>
        private static void CableLengths(WorldDocument w)
        {
            foreach (var cable in w.Cables.Where(c => c.ProjectNet != null && c.Length <= 0))
            {
                var a = w.Find(cable.A.ObjectId);
                var b = w.Find(cable.B.ObjectId);
                if (a == null || b == null) continue;
                double run = (a.Pose.Position - b.Pose.Position).Length;
                cable.Length = Math.Round(Math.Max(0.10, 1.3 * run + 0.05) / 0.05) * 0.05;
            }
        }
    }
}
