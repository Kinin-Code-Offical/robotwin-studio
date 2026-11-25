using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [drive] Gears, pulleys, bearings, shafts and lead screws: the palette, the inspector, and "Mekanizmayı kur", which
    /// reads the machine from where the parts are (Sandbox/DriveAssembly.cs). A world that has them is assembled when it
    /// starts too, so parts placed as in a real machine simply work.
    /// </summary>
    public partial class WorldPage
    {
        private DriveAssembly.Report? _driveReport;

        private void DrivePalette()
        {
            Palette.Children.Add(Caption(DriveParts.Group));
            Palette.Children.Add(Note("Gerçek ölçülü dişli, kasnak, rulman, mil ve vidalı mil. Gerçekteki gibi yerleştirin: mili rulmanların deliğinden geçirin, dişliyi mile oturtun, iki dişliyi eksen mesafesine koyun. Mekanizma parçaların yerinden kurulur."));
            Palette.Children.Add(Action("Mekanizmayı kur", AssembleDrive, "Parçaların yerinden milleri rulmanlara, dişlileri millere bağlar; kavrayan dişlileri ve vidalı mil ile somunu eşler; tutulmayan parçaları söyler."));
            foreach (var s in DriveParts.All)
            {
                var id = s.Id;
                Palette.Children.Add(Row(s.Name, s.Description, () => AddDrive(id)));
            }
        }

        private void AddDrive(string id)
        {
            EnsureWorld();
            if (DriveParts.Find(id) is not { } spec) return;
            WorldObject? added = null;
            Edit("Eklendi", () => added = DriveParts.Add(World!, id, NewPose()));
            if (added == null) return;
            Scene.Select(added.Id);
            W.Status = spec.Name + " eklendi";
        }

        private static string? DriveKind(WorldObject o) => DriveParts.Of(o) is { } s ? "Güç aktarımı · " + s.Name : null;

        /// <summary>Joins what the parts' places imply, as one undoable step; says what it found.</summary>
        private void AssembleDrive()
        {
            var world = World;
            if (world == null) return;
            DriveAssembly.Report? report = null;
            var trial = DriveAssembly.Assemble(WorldDocument.FromJson(world.ToJson()));
            if (trial.Joints + trial.Couplings > 0) Edit("Mekanizma kuruldu", () => report = DriveAssembly.Assemble(world));
            else report = trial;
            _driveReport = report;
            W.Status = report!.Joints + report.Couplings > 0
                ? $"Mekanizma kuruldu: {report.Joints} bağlantı, {report.Couplings} dişli / vida bağı" + (report.Problems.Count > 0 ? $", {report.Problems.Count} sorun" : string.Empty)
                : report.Problems.Count > 0 ? $"Mekanizmada {report.Problems.Count} sorun var" : "Mekanizma zaten kurulu";
            BuildInspector();
        }

        /// <summary>When a world with drive parts starts: assemble what is not yet joined, and put what it found in the log.</summary>
        private void AssembleDriveOnStart(WorldDocument world)
        {
            if (!world.Objects.Any(o => DriveParts.Of(o) != null)) return;
            var trial = DriveAssembly.Assemble(WorldDocument.FromJson(world.ToJson()));
            DriveAssembly.Report report = trial;
            if (trial.Joints + trial.Couplings > 0) Edit("Mekanizma kuruldu", () => report = DriveAssembly.Assemble(world));
            _driveReport = report;
        }

        /// <summary>The assembly's findings in the run's log.</summary>
        private void LogDriveReport()
        {
            if (_driveReport == null) return;
            foreach (var line in _driveReport.Links) _session.Log.Add(new SimLogEntry(0, "mekanizma", line, AnalogEventKind.StateChange));
            foreach (var line in _driveReport.Problems) _session.Log.Add(new SimLogEntry(0, "mekanizma", line, AnalogEventKind.Warning));
        }

        private string? _beltNote;

        /// <summary>A pulley's belts, and a belt to each other pulley it could carry one to.</summary>
        private void BeltRows(WorldDocument world, WorldObject o)
        {
            var belts = world.Couplings?.Where(c => c.Kind == WorldCoupling.Belt && (c.PulleyA == o.Id || c.PulleyB == o.Id)).ToList() ?? new List<WorldCoupling>();
            foreach (var c in belts)
            {
                var other = world.Find(c.PulleyA == o.Id ? c.PulleyB : c.PulleyA);
                Inspector.Children.Add(Note("Kayış: " + (other?.Name ?? "?") + " ile"));
            }

            foreach (var other in world.Objects.Where(x => x.Id != o.Id && DriveParts.Of(x)?.Kind == DrivePartKind.Pulley && !belts.Any(c => c.PulleyA == x.Id || c.PulleyB == x.Id)).Take(6))
            {
                var target = other;
                Inspector.Children.Add(Action("Kayış tak → " + other.Name, () => FitBelt(o, target), "İki kasnağa GT2 kayış takar: boyunu hesaplar, alınacak kapalı kayışı söyler, milleri çap oranında aynı yönde bağlar."));
            }

            if (_beltNote != null) Inspector.Children.Add(Note(_beltNote));
        }

        /// <summary>Fits a belt round two pulleys (after joining what their places imply), as one undoable step.</summary>
        private void FitBelt(WorldObject a, WorldObject b)
        {
            var world = World;
            if (world == null) return;
            var trial = WorldDocument.FromJson(world.ToJson());
            DriveAssembly.Assemble(trial);
            var (fits, why) = DriveAssembly.Belt(trial, trial.Find(a.Id)!, trial.Find(b.Id)!);
            string message = why;
            if (fits != null)
            {
                Edit("Kayış takıldı", () =>
                {
                    DriveAssembly.Assemble(world);
                    message = DriveAssembly.Belt(world, a, b).Message;
                });
            }

            _beltNote = message;
            W.Status = message;
            BuildInspector();
        }

        private void DriveSection(WorldDocument world, WorldObject o)
        {
            if (DriveParts.Of(o) is not { } s) return;
            Inspector.Children.Add(Caption("Güç aktarımı"));
            Inspector.Children.Add(Note(s.Description));
            Inspector.Children.Add(Note(s.Sizes));
            if (s.Bearing != null && Bearings.Find(s.Bearing) is { } b) Inspector.Children.Add(Note("Yatak: " + b.Name));
            Inspector.Children.Add(Action("Mekanizmayı kur", AssembleDrive, "Parçaların yerinden milleri rulmanlara, dişlileri millere bağlar; kavrayan dişlileri ve vidalı mil ile somunu eşler."));
            if (s.Kind == DrivePartKind.Pulley) BeltRows(world, o);
            if (_driveReport == null) return;
            var mine = _driveReport.Links.Concat(_driveReport.Problems).Where(l => l.Contains(o.Name, StringComparison.Ordinal)).ToList();
            foreach (var line in mine) Inspector.Children.Add(Note(line));
        }
    }
}
