using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio
{
    /// <summary>
    /// [assembly] Montaj: a project's robot put together — its circuit's boards and parts as real 3D parts (each project
    /// with or without its PCB), the parts designed on the Parça page, and the joints and cables between them. It is a
    /// world owned by the project: saved inside the project file, reopened from it, and brought up to date with the
    /// project (circuit, PCB, code) whenever it is opened.
    /// </summary>
    public partial class MainWindow
    {
        private AssemblyGeometry? _montajGeometry;

        /// <summary>The app's real geometry for a Montaj: motor shafts, sensor axes and part bounds from the 3D models.</summary>
        internal AssemblyGeometry MontajGeometry()
        {
            if (_montajGeometry != null) return _montajGeometry;
            var lib = W.Library;
            var cache = new Dictionary<string, World.ObjectModel>(StringComparer.Ordinal);
            World.ObjectModel? Model(WorldObject o)
            {
                if (o.Kind != WorldObjectKind.Component || o.PartId == null) return null;
                string key = o.PartId + "|" + o.PresetId + "|" + string.Join(";", o.Parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
                if (cache.TryGetValue(key, out var m)) return m;
                try
                {
                    m = World.ObjectModels.Build(o, new WorldDocument(), lib);
                }
                catch (Exception ex) when (ex is KeyNotFoundException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
                {
                    return null;
                }

                cache[key] = m;
                return m;
            }

            _montajGeometry = new AssemblyGeometry
            {
                Motor = id =>
                {
                    try
                    {
                        var (pivot, axis, height) = World.ObjectModels.Mount(lib, id);
                        return Math.Abs(axis.Y) > 0.9 && height > 0 ? (pivot.X, pivot.Z, Math.Abs(pivot.Y), height) : null;
                    }
                    catch (Exception ex) when (ex is KeyNotFoundException || ex is ArgumentException || ex is InvalidOperationException)
                    {
                        return null;
                    }
                },
                SensorAxis = o => Model(o)?.Sensor is { } s ? new Vec3(s.Axis.X, s.Axis.Y, s.Axis.Z) : (Vec3?)null,
                Bounds = o =>
                {
                    var m = Model(o);
                    if (m == null) return null;
                    var b = m.CollisionBounds.IsEmpty ? m.Bounds : m.CollisionBounds;
                    if (b.IsEmpty || b.SizeX <= 0) return null;
                    return (new Vec3(b.X, b.Y, b.Z), new Vec3(b.X + b.SizeX, b.Y + b.SizeY, b.Z + b.SizeZ));
                }
            };
            return _montajGeometry;
        }

        /// <summary>The project the circuit pages show, unless it is a board opened out of a world.</summary>
        private OpenDocument? MontajProject() => W.DesignDocument.IsEmbedded ? null : W.DesignDocument;

        /// <summary>The open Montaj of the current project (a world opened from a file is adopted when it is that project's).</summary>
        private OpenDocument? OpenMontajTab(OpenDocument project)
        {
            if (W.MontajOf(project) is { } tab) return tab;
            string pid = project.Package.Schematic.EnsureProjectId();
            var orphan = W.Documents.FirstOrDefault(d => d.Kind == DocumentKind.World && d.OwnerProject == null && d.World is { IsAssembly: true } w && w.ProjectId == pid);
            if (orphan != null) orphan.OwnerProject = project;
            return orphan;
        }

        /// <summary>The Montaj stage: opens the project's Montaj (from its project file), or sets one up.</summary>
        public void OpenProjectAssembly(bool ask = true) => ProjectAssembly(create: true, show: true, ask: ask);

        /// <summary>
        /// The project's Montaj: the open tab, the one saved in the project, or (with <paramref name="create"/>) a new one
        /// after asking which projects go in and how. Opening brings it up to date with its projects.
        /// <paramref name="show"/> switches to it; otherwise it is opened without leaving the current page.
        /// </summary>
        public WorldDocument? ProjectAssembly(bool create, bool show, bool includeParts = false, bool offerParts = true, bool ask = true)
        {
            var project = MontajProject();
            if (project == null)
            {
                // A board edited out of a world: that world is where it is put together.
                if (W.DesignDocument.OwnerWorld is { } owner)
                {
                    W.Activate(owner);
                    W.Status = "Bu kart bir dünyanın parçası: montajı o dünyadır";
                }

                return null;
            }

            var tab = OpenMontajTab(project);
            if (tab == null && project.Package.Montaj is { } saved)
            {
                try
                {
                    var world = WorldDocument.FromBytes(saved);
                    tab = W.OpenMontaj(project, world, activate: show);
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is System.Text.Json.JsonException)
                {
                    W.Status = "Projedeki montaj okunamadı (" + ex.Message + "); yeniden kuruluyor";
                }
            }

            if (tab != null)
            {
                if (show)
                {
                    bool wasActive = W.ActiveDocument == tab;
                    W.Activate(tab);   // syncs through OnActiveDocumentChanged
                    if (wasActive) SyncMontaj(tab, quiet: false);
                    if (_current != "world") Navigate("world");
                }
                else
                {
                    SyncMontaj(tab, quiet: true);
                }

                return tab.World;
            }

            if (!create) return null;
            var plan = App.IsSnapshot || !ask ? MontajPlan.Default(W, project, includeParts) : MontajDialog.Ask(this, W, project, offerParts ? _model.PartCount() : 0, includeParts);
            if (plan == null) return null;
            var sch = project.Package.Schematic;
            W.FlushActiveDesign();
            var w = new WorldDocument { Title = RobotTwin.CoreSim.Sandbox.ProjectAssembly.TitleOf(sch) + " — montaj", IsAssembly = true, ProjectId = sch.EnsureProjectId() };
            var notes = new List<string>();
            var geometry = MontajGeometry();
            foreach (var src in plan.Projects)
            {
                var r = RobotTwin.CoreSim.Sandbox.ProjectAssembly.Add(w, src.Package, W.Library, src.Form, src.Path, geometry);
                notes.AddRange(r.Notes);
            }

            var montaj = W.OpenMontaj(project, w, activate: show);
            montaj.IsDirty = true;
            project.IsDirty = true;
            if (show && _current != "world") Navigate("world");
            if (plan.Parts) _model.SendToAssembly();
            int boards = w.Objects.Count(o => o.Kind == WorldObjectKind.Board), parts = w.Objects.Count(o => o.ProjectRef != null && !o.ProjectRef.Contains("/~"));
            W.Status = (w.Objects.Count == 0
                ? "Montaj kuruldu ama boş: kartları ve parçaları Devre'de ekleyin, gövde ve braketleri Parça sayfasında modelleyip 'Montaja gönder' deyin."
                : $"Montaj kuruldu: {plan.Projects.Count} proje" + (boards > 0 ? $", {boards} PCB kartı" : string.Empty) + $", {parts} parça, {w.Cables.Count} kablo. Bağla aracıyla (B) birleştirin, ▶ Montajı çalıştır ile deneyin.")
                + (notes.Count > 0 ? " · " + string.Join(" · ", notes) : string.Empty);
            return w;
        }

        /// <summary>
        /// Brings a Montaj up to date with its projects: circuit, PCB and code changes come in; where things were put,
        /// how they were joined and cables laid by hand stay. A project not open is read from its file.
        /// </summary>
        public void SyncMontaj(OpenDocument tab, bool quiet)
        {
            var w = tab.World;
            if (w == null || w.AssemblySources.Count == 0) return;
            if (W.ActiveWorld == w && _world.SimulationState.State != SimState.Stopped)
            {
                // Not in the middle of a run: the parts it is simulating would change under it.
                if (!quiet) W.Status = "Montaj çalışırken güncellenmez: durdurup (Shift+F5) yeniden deneyin";
                return;
            }

            W.FlushActiveDesign();
            var said = new List<string>();
            var geometry = MontajGeometry();
            W.EditWorld(tab, () =>
            {
                bool changed = false;
                foreach (var src in w.AssemblySources.ToList())
                {
                    var (package, path) = SourcePackage(tab, src);
                    if (package == null)
                    {
                        said.Add($"{src.Title}: proje dosyası bulunamadı, güncellenemedi");
                        continue;
                    }

                    var r = RobotTwin.CoreSim.Sandbox.ProjectAssembly.Sync(w, package, W.Library, path, geometry);
                    if (r.Changed || r.Notes.Count > 0) said.Add(src.Title + ": " + r.Summary());
                    changed |= r.Changed;
                }

                return changed;
            });
            if (said.Count > 0) W.Status = "Montaj projeden güncellendi — " + string.Join(" · ", said);
            else if (!quiet) W.Status = "Montaj projeleriyle güncel";
        }

        /// <summary>The package of a Montaj source: the live project, an open tab of it, or its file.</summary>
        private (ProjectPackage? Package, string? Path) SourcePackage(OpenDocument tab, AssemblySource src)
        {
            var open = W.Documents.FirstOrDefault(d => d.Kind == DocumentKind.Design && !d.IsEmbedded &&
                                                       string.Equals(d.Package.Schematic.ProjectId, src.ProjectId, StringComparison.OrdinalIgnoreCase));
            if (open != null) return (open == W.DesignDocument ? W.Project : open.Package, open.FilePath ?? src.Path);
            if (src.Path != null && File.Exists(src.Path))
            {
                try
                {
                    return (ProjectPackage.Load(src.Path), src.Path);
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is System.Text.Json.JsonException)
                {
                    return (null, src.Path);
                }
            }

            return (null, src.Path);
        }

        /// <summary>Adds projects to an open Montaj (asks which and how).</summary>
        public void AddProjectsToMontaj(OpenDocument tab)
        {
            if (tab.World == null || tab.OwnerProject == null) return;
            var plan = MontajDialog.Ask(this, W, tab.OwnerProject, 0, false, tab.World);
            if (plan == null || plan.Projects.Count == 0) return;
            var notes = new List<string>();
            var geometry = MontajGeometry();
            W.EditWorld(tab, () =>
            {
                foreach (var src in plan.Projects)
                {
                    var r = RobotTwin.CoreSim.Sandbox.ProjectAssembly.Add(tab.World, src.Package, W.Library, src.Form, src.Path, geometry);
                    notes.Add(RobotTwin.CoreSim.Sandbox.ProjectAssembly.TitleOf(src.Package.Schematic) + ": " + r.Summary());
                }

                return true;
            }, "Montaja eklendi — " + string.Join(" · ", notes));
        }

        /// <summary>Changes how a project is in the Montaj (with / without its PCB) and brings it up to date.</summary>
        public void SetMontajForm(OpenDocument tab, AssemblySource src, AssemblyForm form)
        {
            if (src.Form == form) return;
            src.Form = form;
            SyncMontaj(tab, quiet: false);
        }

        /// <summary>Takes a project out of the Montaj (its board, parts and cables).</summary>
        public void RemoveProjectFromMontaj(OpenDocument tab, AssemblySource src)
        {
            if (tab.World == null) return;
            if (Dialog.Show(this, "Projeyi montajdan çıkar", $"'{src.Title}' projesinin kartı, parçaları, kabloları ve bunlara bağlanan bağlantılar Montaj'dan kaldırılsın mı?", "Çıkar", "Vazgeç") != 0) return;
            W.EditWorld(tab, () => RobotTwin.CoreSim.Sandbox.ProjectAssembly.RemoveProject(tab.World, src.ProjectId) >= 0, $"'{src.Title}' Montaj'dan çıkarıldı");
        }

        /// <summary>Ctrl+S on a Montaj: saves its project (the Montaj is inside it).</summary>
        private bool SaveMontajProject(OpenDocument montaj, bool saveAs)
        {
            var owner = montaj.OwnerProject;
            if (owner == null) return false;
            W.Activate(owner);
            bool ok = !saveAs && owner.FilePath != null ? Save(owner.FilePath) : SaveAs();
            if (W.Documents.Contains(montaj)) W.Activate(montaj);
            if (ok) W.Status = "Proje montajıyla birlikte kaydedildi: " + Path.GetFileName(owner.FilePath ?? string.Empty);
            return ok;
        }

        /// <summary>
        /// --snapshot --fault montaj-e2e: the Montaj's life with a project, through the real workspace — built, worked on
        /// (a bumper of the user's own fixed to the chassis), saved inside the project, closed, reopened, then the
        /// project changes (a new LED, new code) and the Montaj takes them, keeping the user's work. Results go to a log.
        /// </summary>
        internal void RunMontajCheck(string logPath)
        {
            var log = new System.Text.StringBuilder();
            int failures = 0;
            void Check(string what, bool ok, string detail = "")
            {
                if (!ok) failures++;
                log.AppendLine((ok ? "OK   " : "FAIL ") + what + (detail.Length > 0 ? " — " + detail : string.Empty));
            }

            try
            {
                var project = W.DesignDocument;
                var tab = W.MontajOf(project);
                Check("Montaj açık ve projeye bağlı", tab?.World != null && tab.World.IsAssembly && tab.World.ProjectId == project.Package.Schematic.ProjectId);
                var w = tab!.World!;
                var plate = w.Objects.FirstOrDefault(o => o.Shape == "plate");
                Check("araç şasesiyle kuruldu", plate != null && w.Objects.Count(o => o.Shape == "wheel") == 2);
                W.EditWorld(tab, () =>
                {
                    var bumper = w.AddBody("box", 0.02, 0.09, 0.015, new WorldPose { X = plate!.Pose.X + 0.12, Y = plate.Pose.Y, Z = plate.Pose.Z });
                    bumper.Name = "Tampon";
                    w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = bumper.Id, ObjectB = plate.Id });
                    return true;
                });
                Check("Montaj değişince proje kaydedilmemiş görünür", project.IsDirty);

                string dir = Path.Combine(Path.GetTempPath(), "robotwin-montaj-e2e");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "robot.rwproj");
                W.Activate(project);
                W.Save(path);
                var pkg = ProjectPackage.Load(path);
                Check("Montaj proje dosyasının içinde", pkg.Montaj != null);
                var saved = WorldDocument.FromBytes(pkg.Montaj!);
                Check("kendi parçam kaydedildi", saved.Objects.Any(o => o.Name == "Tampon"));

                W.Close(tab);
                Check("Montaj sekmesi kapanınca proje onu tutar", project.Package.Montaj != null);
                W.Close(project);
                W.Open(path);
                var reopened = W.DesignDocument;
                Check("proje yeniden açıldı", reopened.FilePath != null && reopened.Package.Schematic.ProjectId == pkg.Schematic.ProjectId);
                OpenProjectAssembly(ask: false);
                var tab2 = W.MontajOf(reopened);
                var w2 = tab2?.World;
                Check("Montaj projeden geri geldi", w2 != null && w2.Objects.Any(o => o.Name == "Tampon"), w2 == null ? "yok" : w2.Objects.Count + " nesne");

                // The project changes: a new LED on the trigger line, new code on the Uno.
                W.Activate(reopened);
                var sch = W.Schematic;
                W.BeginChange();
                var led = sch.AddPart(W.Library, "led3-red", new RobotTwin.CoreSim.Design.GridPoint(60, 30));
                RobotTwin.CoreSim.Mechanics.VehicleTemplates.Tie(sch, W.Library, led, "A", "TRIG");
                RobotTwin.CoreSim.Mechanics.VehicleTemplates.Tie(sch, W.Library, led, "K", "GND");
                W.CommitChange("LED eklendi");
                var uno = sch.Parts.First(p => p.PartId == "arduino-uno");
                var fw = W.FirmwareFor(uno.RefDes, "arduino:avr:uno");
                fw.Source = "// e2e: yeni kod" + Environment.NewLine + "void setup() {}" + Environment.NewLine + "void loop() {}" + Environment.NewLine;
                W.MarkFirmwareChanged("kod değişti");
                OpenProjectAssembly(ask: false);
                w2 = W.MontajOf(reopened)?.World;
                Check("yeni LED Montaj'a geldi", w2 != null && w2.Objects.Any(o => o.ProjectRef == sch.ProjectId + "/" + led.RefDes));
                var unoObj = w2?.Objects.FirstOrDefault(o => o.ProjectRef == sch.ProjectId + "/" + uno.RefDes);
                Check("yeni kod Montaj'daki Uno'ya geldi", unoObj?.Firmware?.Source.Contains("e2e: yeni kod") == true);
                var bumper2 = w2?.Objects.FirstOrDefault(o => o.Name == "Tampon");
                Check("kendi parçam ve bağlantısı yerinde", bumper2 != null && w2!.Joints.Any(j => j.ObjectA == bumper2.Id));
                var targets = _code.TargetKeys();
                Check("Kod sayfası Montaj'ın Uno'sunu ayrı kart diye göstermez", unoObj != null && !targets.Contains("w:" + unoObj.Id) && targets.Contains("p:" + uno.RefDes), string.Join(", ", targets));
            }
            catch (Exception ex)
            {
                Check("beklenmeyen hata", false, ex.ToString());
            }

            log.AppendLine(failures == 0 ? "TÜMÜ GEÇTİ" : failures + " HATA");
            File.WriteAllText(logPath, log.ToString());
            W.Status = failures == 0 ? "Montaj uçtan uca: tümü geçti" : $"Montaj uçtan uca: {failures} hata";
        }

        /// <summary>The rail's Montaj line.</summary>
        private RailStatus MontajStatus()
        {
            var project = MontajProject();
            if (project == null) return new RailStatus("Dünyadaki kart: montajı o dünya");
            var w = W.MontajOf(project)?.World;
            if (w != null) return new RailStatus($"{w.Objects.Count} parça · {w.Joints.Count} bağlantı", w.Objects.Count > 0 ? RailSeverity.Ok : RailSeverity.None);
            if (project.Package.Montaj != null) return new RailStatus("Projede kayıtlı · açınca güncellenir");
            int physical = RobotTwin.CoreSim.Sandbox.ProjectAssembly.PhysicalParts(W.Schematic, W.Library).Count;
            return new RailStatus(physical == 0 ? "Henüz kurulmadı" : $"Kurulmadı · devrede {physical} parça");
        }
    }
}
