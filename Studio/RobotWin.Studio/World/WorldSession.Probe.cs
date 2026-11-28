using System;
using System.Collections.Generic;
using System.Text;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analysis.Recording;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [unified-timeline] The live session as a probe's source: a recording made as the world runs (Kayıt), without a
    /// render, reads the same circuit, physics, boards (their chips' pin and interrupt traces), serial lines and log the
    /// headless runner gives a render. <see cref="Sliced"/> comes after each step on the thread that steps the world; the
    /// serial lines and log entries are kept for it only while something listens.
    /// </summary>
    public sealed partial class WorldSession : IProbeSource
    {
        private static readonly WorldDocument NoWorld = new WorldDocument();
        private readonly List<ProbeBoard> _probeBoards = new List<ProbeBoard>();
        private readonly Dictionary<string, StringBuilder> _probeSerial = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        private readonly List<RunnerEvent> _probeEvents = new List<RunnerEvent>();
        private readonly object _probeGate = new object();

        /// <summary>After each step of the running world (on the simulation's thread), with its length.</summary>
        public event Action<double>? Sliced;

        WorldDocument IProbeSource.World => World ?? NoWorld;

        CompiledCircuit? IProbeSource.Compiled => Circuit?.Compiled;

        IReadOnlyList<ProbeBoard> IProbeSource.ProbeBoards => _probeBoards;

        IReadOnlyDictionary<string, string> IProbeSource.TakeSerial()
        {
            var taken = new Dictionary<string, string>(StringComparer.Ordinal);
            lock (_probeGate)
            {
                foreach (var kv in _probeSerial)
                    if (kv.Value.Length > 0)
                    {
                        taken[kv.Key] = kv.Value.ToString();
                        kv.Value.Clear();
                    }
            }

            return taken;
        }

        IReadOnlyList<RunnerEvent> IProbeSource.TakeEvents()
        {
            lock (_probeGate)
            {
                var taken = _probeEvents.ToArray();
                _probeEvents.Clear();
                return taken;
            }
        }

        /// <summary>A board started: the probe can trace its chip.</summary>
        private void ProbeTrack(object cosim, WorldProgrammable prog)
        {
            string name = (World?.Find(prog.ObjectId)?.Name ?? prog.ObjectId) + " " + prog.InnerRefDes;
            _probeBoards.Add(new ProbeBoard
            {
                ObjectId = prog.ObjectId,
                RefDes = prog.RefDes,
                Name = name.Trim(),
                Running = true,
                Avr = cosim as FirmwareCoSimulation,
                Esp32 = cosim as IEsp32Board
            });
        }

        private void ProbeReset()
        {
            _probeBoards.Clear();
            lock (_probeGate)
            {
                _probeSerial.Clear();
                _probeEvents.Clear();
            }
        }

        /// <summary>A board's serial text, for a recording listening.</summary>
        private void ProbeSerial(string source, string text)
        {
            if (Sliced == null) return;
            lock (_probeGate)
            {
                if (!_probeSerial.TryGetValue(source, out var sb)) _probeSerial[source] = sb = new StringBuilder();
                sb.Append(text);
            }
        }

        /// <summary>A log entry, for a recording listening.</summary>
        private void ProbeLog(SimLogEntry e)
        {
            if (Sliced == null) return;
            var kind = e.Kind == AnalogEventKind.Failure ? RunnerEventKind.Failure : e.Kind == AnalogEventKind.Warning ? RunnerEventKind.Warning : RunnerEventKind.Info;
            lock (_probeGate) _probeEvents.Add(new RunnerEvent(e.Time, e.Device, kind, e.Message));
        }
    }
}
