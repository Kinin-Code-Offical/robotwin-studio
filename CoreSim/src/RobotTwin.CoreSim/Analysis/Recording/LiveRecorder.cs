using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RobotTwin.CoreSim.Analysis.Recording
{
    /// <summary>
    /// [unified-timeline] A recording made from a live run as it goes, without a render: the channels, serial lines,
    /// events, the boards' pins and interrupts (their exact times, from the chips' traces) and the poses a render records,
    /// sampled at the output rate from the run's own steps (on the thread that steps it). <see cref="Finish"/> gives the
    /// <see cref="AnalysisResult"/> the timeline and the replay show at once; a render can still recompute the same run
    /// with full physics from the recorded inputs.
    /// </summary>
    public sealed class LiveRecorder
    {
        private readonly IProbeSource _source;
        private readonly AnalysisResult _result;
        private readonly WorldProbe _probe;
        private readonly List<AnalysisRun.PoseChannels> _poses;
        private readonly List<CableTrack> _cables = new List<CableTrack>();
        private readonly double _interval, _start;
        private readonly Stopwatch _wall = Stopwatch.StartNew();
        private double _next;
        private bool _finished;

        /// <param name="outputRate">Samples a simulated second, at most (a step longer than the interval gives one).</param>
        public LiveRecorder(IProbeSource source, double outputRate = 200)
        {
            _source = source;
            var settings = new AnalysisSettings { Preset = AnalysisPreset.Custom, OutputRate = outputRate, Firmware = true };
            _result = new AnalysisResult { Settings = settings, WorldTitle = source.World.Title };
            _interval = 1.0 / Math.Max(1, outputRate);
            _start = source.Time;
            _probe = new WorldProbe(source, settings, _result, Array.Empty<string>());
            _poses = AnalysisRun.PoseRecorder(source.World, _result.Channels);
            if (source.Physics != null)
                foreach (var c in source.World.Cables)
                    if (source.Physics.CablePoints(c.Id) != null) _cables.Add(new CableTrack { CableId = c.Id });
            Sample();
            source.Sliced += OnSlice;   // after the probe's own (it subscribed first)
        }

        /// <summary>Samples taken so far.</summary>
        public int Samples => _result.Channels.Count;

        private void OnSlice(double dt)
        {
            if (_finished || _source.Time + 1e-12 < _next) return;
            Sample();
        }

        private void Sample()
        {
            _probe.Sample();
            AnalysisRun.RecordPoses(_source.Physics, _result.Channels, _poses, _source.World);
            if (_source.Physics != null)
                foreach (var c in _cables)
                {
                    if (_source.Physics.CablePoints(c.CableId) is not { } pts) continue;
                    var (a, b) = _source.Physics.PlugState(c.CableId);
                    c.Add(_source.Time, pts, a, b);
                }

            _next = _source.Time + _interval;
        }

        /// <summary>Stops recording and drops it (the run was stopped before the recording ended).</summary>
        public void Cancel()
        {
            if (_finished) return;
            _finished = true;
            _source.Sliced -= OnSlice;
            _probe.Detach();
        }

        /// <summary>Stops recording (call once the run no longer steps) and gives the recording.</summary>
        public AnalysisResult Finish()
        {
            if (_finished) return _result;
            _finished = true;
            _source.Sliced -= OnSlice;
            Sample();   // the last state
            _probe.Detach();
            _result.Cables.AddRange(_cables);
            _result.Settings.Duration = Math.Max(0, _source.Time - _start);
            _result.SimulatedSeconds = _source.Time - _start;
            _result.WallSeconds = _wall.Elapsed.TotalSeconds;
            _result.Note($"Canlı koşudan doğrudan kayıt: {_result.SimulatedSeconds:0.###} s, {_result.Channels.Count} örnek ({_result.Settings.OutputRate:0} Hz'e kadar), {_result.Serial.Count} seri satırı, {_result.Events.Count} olay; tam fizik (gerilme, akış, ısı) yok.");
            return _result;
        }
    }
}
