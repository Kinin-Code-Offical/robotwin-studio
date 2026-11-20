using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Analysis;

namespace RobotWin.Studio.Pages
{
    /// <summary>[analysis] Opens the offline analysis mode (stress, flow, recorded plots) for the current world.</summary>
    public partial class WorldPage
    {
        /// <summary>Geometry captured on the UI thread so the analysis can run on a worker thread.</summary>
        private sealed class CapturedGeometry : IWorldGeometry
        {
            private readonly Dictionary<string, WorldBodyGeometry> _bodies = new Dictionary<string, WorldBodyGeometry>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, double> _cables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            public CapturedGeometry(WorldDocument world, Func<WorldObject, WorldBodyGeometry> body, Func<WorldCable, double> cable)
            {
                foreach (var o in world.Objects) _bodies[o.Id] = body(o);
                foreach (var c in world.Cables) _cables[c.Id] = cable(c);
            }

            public WorldBodyGeometry GeometryOf(WorldObject o) => _bodies.TryGetValue(o.Id, out var g) ? g : new WorldBodyGeometry { Min = new RobotTwin.CoreSim.Mechanics.Vec3(-0.01, -0.01, 0), Max = new RobotTwin.CoreSim.Mechanics.Vec3(0.01, 0.01, 0.02), MassKg = 0.01 };

            public double CableLength(WorldCable cable) => _cables.TryGetValue(cable.Id, out var l) ? l : 0.2;
        }

        /// <summary>Opens the analysis window on a private copy of the world (later edits do not affect a run).</summary>
        public AnalysisWindow? OpenAnalysis()
        {
            var world = World;
            if (world == null)
            {
                Shell.Workspace.Current.Status = "Analiz için önce bir dünya açın.";
                return null;
            }

            string temp = Path.Combine(Path.GetTempPath(), "robotwin-analysis-" + Guid.NewGuid().ToString("N") + ".rwworld");
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
            var w = new AnalysisWindow(copy, geometry) { Owner = Window.GetWindow(this) };
            w.Show();
            return w;
        }

        /// <summary>--analysis in a world snapshot: opens the analysis window, runs it and captures that window.</summary>
        private void AnalysisSnapshot()
        {
            if (!Environment.GetCommandLineArgs().Contains("--analysis")) return;
            var owner = Window.GetWindow(this);
            var w = OpenAnalysis();
            if (w == null) return;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = owner?.Left ?? -3000;
            w.Top = 0;
            w.Width = owner?.Width ?? 1600;
            w.Height = owner?.Height ?? 960;
            App.SnapshotTarget = w.Content as FrameworkElement;
            _ = w.RunNow();
        }
    }
}
