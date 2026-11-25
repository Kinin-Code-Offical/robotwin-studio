using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sdk;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [sdk] Environment, force fields, plugins and scripts of a running world. WorldSession.cs
    /// only calls PrepareSdk (before the physics is built), StopSdk and DrainSdkEvents.
    /// </summary>
    public sealed partial class WorldSession
    {
        /// <summary>Wind, drag, fields and behaviours of the running world (null when stopped or physics-less).</summary>
        public WorldSdkRuntime? Sdk => Physics?.Sdk;

        private void PrepareSdk(WorldDocument world)
        {
            SdkServices.Init();
            SdkServices.LoadWorldPlugins(Workspace.Current.FilePath);
            var scripts = world.Sdk.Scripts.Where(s => s.Enabled).ToList();
            if (scripts.Count == 0 || SdkServices.ScriptsUpToDate(world)) return;
            if (!SdkServices.Settings.AutoCompileScripts)
            {
                AddLog(new SimLogEntry(0, "betikler", "Betikler değişti ama otomatik derleme kapalı; Betikler sekmesinden 'Derle'ye basın.", AnalogEventKind.Warning));
                return;
            }

            var info = ScriptCompiler.Detect();
            if (!info.Available)
            {
                AddLog(new SimLogEntry(0, "betikler", "C# betikleri derlenemiyor: " + info.Explanation, AnalogEventKind.Warning));
                return;
            }

            var r = SdkServices.CompileScripts(world);
            AddLog(new SimLogEntry(0, "betikler", r.Summary, r.Success ? AnalogEventKind.StateChange : AnalogEventKind.Failure));
            foreach (var d in r.Diagnostics.Where(d => d.IsError).Take(10)) AddLog(new SimLogEntry(0, "betikler", d.ToString(), AnalogEventKind.Failure));
        }

        private void StopSdk()
        {
            var sdk = Physics?.Sdk;
            if (sdk == null) return;
            sdk.Stop();
            DrainSdkEvents();
        }

        private void DrainSdkEvents()
        {
            var sdk = Physics?.Sdk;
            if (sdk == null) return;
            foreach (var e in sdk.DrainEvents())
            {
                var kind = e.Level == SdkEventLevel.Error ? AnalogEventKind.Failure : e.Level == SdkEventLevel.Warning ? AnalogEventKind.Warning : AnalogEventKind.StateChange;
                AddLog(new SimLogEntry(e.Time, e.Source, e.Message, kind));
            }
        }
    }
}
