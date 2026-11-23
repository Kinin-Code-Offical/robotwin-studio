using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Analysis;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>[render] Record the live world and render it offline (full physics, stress, flow, heat) for replay.</summary>
    public partial class WorldPage
    {
        /// <summary>Geometry captured on the UI thread so the render can run on a worker thread.</summary>
        private sealed class CapturedGeometry : IWorldGeometry
        {
            private readonly Dictionary<string, WorldBodyGeometry> _bodies = new Dictionary<string, WorldBodyGeometry>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, double> _cables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, RobotTwin.CoreSim.Sandbox.CablePins> _pins = new Dictionary<string, RobotTwin.CoreSim.Sandbox.CablePins>(StringComparer.OrdinalIgnoreCase);

            public CapturedGeometry(WorldDocument world, Func<WorldObject, WorldBodyGeometry> body, Func<WorldCable, double> cable, Func<WorldCable, RobotTwin.CoreSim.Sandbox.CablePins?>? pins = null)
            {
                foreach (var o in world.Objects) _bodies[o.Id] = body(o);
                foreach (var c in world.Cables)
                {
                    _cables[c.Id] = cable(c);
                    if (pins?.Invoke(c) is { } p) _pins[c.Id] = p;   // [route] so a headless run lays the same ropes
                }
            }

            public RobotTwin.CoreSim.Sandbox.CablePins? PinsOf(WorldCable cable) => _pins.TryGetValue(cable.Id, out var p) ? p : null;

            public WorldBodyGeometry GeometryOf(WorldObject o) => _bodies.TryGetValue(o.Id, out var g) ? g : new WorldBodyGeometry { Min = new RobotTwin.CoreSim.Mechanics.Vec3(-0.01, -0.01, 0), Max = new RobotTwin.CoreSim.Mechanics.Vec3(0.01, 0.01, 0.02), MassKg = 0.01 };

            public double CableLength(WorldCable cable) => _cables.TryGetValue(cable.Id, out var l) ? l : 0.2;
        }

        private bool _recording;
        private double _recordStart;

        private void OnRecord(object sender, RoutedEventArgs e)
        {
            if (!_recording)
            {
                // A render recomputes the run from its start, so a recording always starts the world fresh.
                if (_session.State != SimState.Stopped) StopSimulation();
                Run();
                _recording = _session.State != SimState.Stopped;
                _recordStart = _session.Time;
                if (_recording) _session.BeginJournal();   // [replay] controller reports and interventions, time-stamped
                if (_recording) Shell.Workspace.Current.Status = "Kayıt alınıyor: kumanda ve müdahaleler de kaydediliyor. Bitirmek için Kayıt düğmesine yeniden basın.";
            }
            else
            {
                double end = _session.Time;
                _recording = false;
                var inputs = _session.EndJournal();
                StopSimulation();
                OpenRender(Math.Max(0.05, end - _recordStart), autoStart: true, inputs);
            }

            RecordButton.IsChecked = _recording;
            RecordText.Text = _recording ? "Kaydı bitir" : "Kayıt";
        }

        /// <summary>Opens the render window on a private copy of the world (later edits do not affect it).</summary>
        public RenderWindow? OpenRender(double duration = 2, bool autoStart = false, RobotTwin.CoreSim.Automation.InputJournal? inputs = null)
        {
            var world = World;
            if (world == null)
            {
                Shell.Workspace.Current.Status = "Render için önce bir dünya açın.";
                return null;
            }

            string temp = Path.Combine(Path.GetTempPath(), "robotwin-render-" + Guid.NewGuid().ToString("N") + ".rwworld");
            WorldDocument copy;
            try
            {
                world.Save(temp);
                copy = WorldDocument.Load(temp);
            }
            finally
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                }
            }

            var geometry = new CapturedGeometry(copy, o => Scene.GeometryOf(world.Find(o.Id) ?? o), c => Scene.CableLength(world.Cables.FirstOrDefault(x => x.Id == c.Id) ?? c));
            var w = new RenderWindow(copy, geometry, duration, Scene.Library, Scene.MassOf) { Owner = Window.GetWindow(this), Inputs = inputs };
            w.Show();
            if (autoStart) _ = w.RenderNow();
            return w;
        }

        /// <summary>Simülasyon › Analiz / render: opens the render window for a 2 s run.</summary>
        public RenderWindow? OpenAnalysis() => OpenRender();

        /// <summary>--analysis in a world snapshot: opens the render window, renders and captures that window.</summary>
        private void AnalysisSnapshot()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--analysis")) return;
            int k = Array.IndexOf(args, "--render-seconds");
            double secs = k >= 0 && k + 1 < args.Length && double.TryParse(args[k + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 1.0;
            var owner = Window.GetWindow(this);
            var w = OpenRender(secs);
            if (w == null) return;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = owner?.Left ?? -3000;
            w.Top = 0;
            w.Width = owner?.Width ?? 1600;
            w.Height = owner?.Height ?? 960;
            App.SnapshotTarget = w.Content as FrameworkElement;
            int at = Array.IndexOf(args, "--render-at");
            double seek = at >= 0 && at + 1 < args.Length && double.TryParse(args[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sv) ? sv : secs / 2;
            int mi = Array.IndexOf(args, "--render-mode");
            int mode = mi >= 0 && mi + 1 < args.Length && int.TryParse(args[mi + 1], out int mv) ? mv : 0;
            w.RenderNow().ContinueWith(_ =>
            {
                w.SetColourMode(mode);
                w.SeekTo(seek);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
